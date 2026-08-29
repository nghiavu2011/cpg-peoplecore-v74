using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class EmployeeMigrationService(
    PeopleCoreDbContext db,
    IAccessControlService access,
    ICurrentUser current,
    IAuditService audit,
    IConfiguration configuration)
{
    private const string Kind = "EMPLOYEE_MASTER";
    private static readonly IReadOnlySet<string> Headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "staff_code","corporate_email","display_name","company_code","department_code","office_code","position_title","grade_code",
        "manager_staff_code","timesheet_policy","employment_status","effective_from","effective_to","hire_date","last_working_date"
    };

    public async Task<MigrationBatchDto> StageAsync(IFormFile file, string sourceSystem, CancellationToken ct)
    {
        var (bytes, rawRows) = await CsvImportParser.ReadAsync(file, Headers, maxRows: 5000, maxBytes: 10 * 1024 * 1024, ct);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existingBatch = await db.MigrationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.BatchKind == Kind && x.SourceSha256 == sha, ct);
        if (existingBatch is not null) return ToDto(existingBatch);

        var allowedDomains = (configuration["Migration:AllowedCorporateEmailDomains"] ?? "cpgcorp.com.sg")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = rawRows.Select((r, i) => Normalize(r, i + 2, allowedDomains)).ToList();
        MarkFileDuplicates(normalized);

        var batchStaff = normalized.Where(x => !string.IsNullOrWhiteSpace(x.Data.StaffCode)).Select(x => x.Data.StaffCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managerRefs = normalized.Select(x => x.Data.ManagerStaffCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingManagers = await db.Employees.AsNoTracking().Where(x => managerRefs.Contains(x.StaffCode)).Select(x => x.StaffCode).ToListAsync(ct);
        var knownManagers = existingManagers.Concat(batchStaff).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in normalized)
        {
            if (!string.IsNullOrWhiteSpace(row.Data.ManagerStaffCode) && !knownManagers.Contains(row.Data.ManagerStaffCode!))
                row.Errors.Add("MANAGER_STAFF_CODE_NOT_FOUND");
            if (string.Equals(row.Data.StaffCode, row.Data.ManagerStaffCode, StringComparison.OrdinalIgnoreCase))
                row.Errors.Add("SELF_MANAGER_NOT_ALLOWED");

            if (row.Errors.Count == 0)
                await ValidateAgainstDatabaseAsync(row, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var batch = new MigrationBatch
        {
            Id = Guid.NewGuid(), BatchKind = Kind, SourceSystem = CleanSource(sourceSystem), SourceFileName = Path.GetFileName(file.FileName),
            SourceSha256 = sha, CreatedAt = now, ValidatedAt = now, CreatedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        };
        foreach (var row in normalized)
        {
            var status = row.Errors.Count == 0 ? "VALID" : row.Errors.Any(IsReviewCode) ? "REVIEW" : "INVALID";
            db.MigrationRows.Add(new MigrationRow
            {
                Id = Guid.NewGuid(), BatchId = batch.Id, RowNumber = row.RowNumber, ExternalKey = row.Data.StaffCode,
                PayloadJson = JsonSerializer.Serialize(row.Raw), NormalizedJson = JsonSerializer.Serialize(row.Data), Status = status,
                ErrorsJson = JsonSerializer.Serialize(row.Errors.Distinct().ToArray()), CreatedAt = now
            });
            if (status == "VALID") batch.ValidRows++;
            else if (status == "REVIEW") batch.ReviewRows++;
            else batch.InvalidRows++;
        }
        batch.TotalRows = normalized.Count;
        batch.Status = batch.InvalidRows == 0 && batch.ReviewRows == 0 ? "READY" : "REVIEW";
        db.MigrationBatches.Add(batch);
        audit.Record("EMPLOYEE_MIGRATION_STAGED", "MigrationBatch", batch.Id.ToString(), new { batch.SourceSystem, batch.SourceFileName, batch.SourceSha256, batch.TotalRows, batch.ValidRows, batch.ReviewRows, batch.InvalidRows });
        await db.SaveChangesAsync(ct);
        return ToDto(batch);
    }

    public async Task<IReadOnlyList<MigrationBatchDto>> ListAsync(int take, CancellationToken ct) =>
        await db.MigrationBatches.AsNoTracking().Where(x => x.BatchKind == Kind).OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(x => new MigrationBatchDto(x.Id, x.BatchKind, x.SourceSystem, x.SourceFileName, x.SourceSha256, x.Status, x.TotalRows, x.ValidRows, x.ReviewRows, x.InvalidRows, x.CreatedAt, x.ValidatedAt, x.CommittedAt)).ToListAsync(ct);

    public async Task<IReadOnlyList<MigrationRowDto>> RowsAsync(Guid batchId, CancellationToken ct)
    {
        var rows = await db.MigrationRows.AsNoTracking().Where(x => x.BatchId == batchId).OrderBy(x => x.RowNumber).Take(5000).ToListAsync(ct);
        return rows.Select(x => new MigrationRowDto(x.Id, x.RowNumber, x.ExternalKey, x.Status, JsonSerializer.Deserialize<string[]>(x.ErrorsJson) ?? Array.Empty<string>(), x.EmployeeId)).ToList();
    }

    public async Task<MigrationBatchDto> CommitAsync(Guid batchId, CommitMigrationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var batch = await db.MigrationBatches.SingleOrDefaultAsync(x => x.Id == batchId && x.BatchKind == Kind, ct) ?? throw new InvalidOperationException("MIGRATION_BATCH_NOT_FOUND");
        if (batch.Status == "COMMITTED") return ToDto(batch);
        if (batch.Status != "READY" || batch.InvalidRows != 0 || batch.ReviewRows != 0) throw new InvalidOperationException("MIGRATION_BATCH_NOT_READY");

        var rows = await db.MigrationRows.Where(x => x.BatchId == batch.Id).OrderBy(x => x.RowNumber).ToListAsync(ct);
        var data = rows.Select(x => (Row: x, Data: JsonSerializer.Deserialize<EmployeeImportData>(x.NormalizedJson) ?? throw new InvalidOperationException("INVALID_STAGED_ROW"))).ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var staffCodes = data.Select(x => x.Data.StaffCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingList = await db.Employees.Where(x => staffCodes.Contains(x.StaffCode)).ToListAsync(ct);
        var existing = existingList.ToDictionary(x => x.StaffCode, x => x, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var item in data)
        {
            var d = item.Data;
            Employee employee;
            if (existing.TryGetValue(d.StaffCode, out var found))
            {
                employee = found;
                if (!await access.CanEditEmployeeAsync(employee, ct)) throw new UnauthorizedAccessException("HR_MIGRATION_EDIT_SCOPE_DENIED");
                employee.CorporateEmail = d.CorporateEmail;
                employee.DisplayName = d.DisplayName;
                employee.CompanyCode = d.CompanyCode;
                employee.DepartmentCode = d.DepartmentCode;
                employee.OfficeCode = d.OfficeCode;
                employee.PositionTitle = d.PositionTitle;
                employee.GradeCode = d.GradeCode;
                employee.TimesheetPolicy = d.TimesheetPolicy;
                employee.EmploymentStatus = d.EmploymentStatus;
                employee.HireDate = d.HireDate;
                employee.LastWorkingDate = d.LastWorkingDate;
                employee.EffectiveFrom = d.EffectiveFrom;
                employee.EffectiveTo = d.EffectiveTo;
                employee.UpdatedAt = now;
                employee.RowVersion++;
            }
            else
            {
                if (!await access.CanCreateEmployeeAsync(d.CompanyCode, d.DepartmentCode, ct)) throw new UnauthorizedAccessException("HR_MIGRATION_CREATE_SCOPE_DENIED");
                employee = new Employee
                {
                    Id = Guid.NewGuid(), StaffCode = d.StaffCode, CorporateEmail = d.CorporateEmail, DisplayName = d.DisplayName,
                    CompanyCode = d.CompanyCode, DepartmentCode = d.DepartmentCode, OfficeCode = d.OfficeCode, PositionTitle = d.PositionTitle,
                    GradeCode = d.GradeCode, TimesheetPolicy = d.TimesheetPolicy, EmploymentStatus = d.EmploymentStatus,
                    HireDate = d.HireDate, LastWorkingDate = d.LastWorkingDate, EffectiveFrom = d.EffectiveFrom, EffectiveTo = d.EffectiveTo,
                    UpdatedAt = now, RowVersion = 1, PrivateProfile = new EmployeePrivateProfile { EmployeeId = Guid.Empty }
                };
                employee.PrivateProfile.EmployeeId = employee.Id;
                db.Employees.Add(employee);
                existing[d.StaffCode] = employee;
            }
            item.Row.EmployeeId = employee.Id;
        }
        await db.SaveChangesAsync(ct);

        foreach (var item in data)
        {
            var d = item.Data;
            var employee = existing[d.StaffCode];
            Guid? managerId = null;
            if (!string.IsNullOrWhiteSpace(d.ManagerStaffCode))
            {
                if (!existing.TryGetValue(d.ManagerStaffCode!, out var manager))
                    manager = await db.Employees.SingleOrDefaultAsync(x => x.StaffCode == d.ManagerStaffCode, ct) ?? throw new InvalidOperationException("MANAGER_STAFF_CODE_NOT_FOUND_AT_COMMIT");
                managerId = manager.Id;
            }
            employee.ManagerEmployeeId = managerId;

            var exactAssignmentExists = await db.EmployeeAssignments.AnyAsync(x => x.EmployeeId == employee.Id && x.EffectiveFrom == d.EffectiveFrom
                && x.EffectiveTo == d.EffectiveTo && x.CompanyCode == d.CompanyCode && x.DepartmentCode == d.DepartmentCode && x.OfficeCode == d.OfficeCode
                && x.PositionTitle == d.PositionTitle && x.GradeCode == d.GradeCode && x.ManagerEmployeeId == managerId && x.TimesheetPolicy == d.TimesheetPolicy, ct);
            if (!exactAssignmentExists)
            {
                db.EmployeeAssignments.Add(new EmployeeAssignment
                {
                    Id = Guid.NewGuid(), EmployeeId = employee.Id, CompanyCode = d.CompanyCode, DepartmentCode = d.DepartmentCode,
                    OfficeCode = d.OfficeCode, PositionTitle = d.PositionTitle, GradeCode = d.GradeCode, ManagerEmployeeId = managerId,
                    TimesheetPolicy = d.TimesheetPolicy, EffectiveFrom = d.EffectiveFrom, EffectiveTo = d.EffectiveTo,
                    CreatedAt = now, CreatedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
                });
            }
            item.Row.Status = "COMMITTED";
        }
        batch.Status = "COMMITTED";
        batch.CommittedAt = now;
        audit.Record("EMPLOYEE_MIGRATION_COMMITTED", "MigrationBatch", batch.Id.ToString(), new { request.Reason, batch.SourceSha256, batch.TotalRows });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(batch);
    }

    private async Task ValidateAgainstDatabaseAsync(ImportRow row, CancellationToken ct)
    {
        var d = row.Data;
        var byStaff = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.StaffCode == d.StaffCode, ct);
        var byEmail = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.CorporateEmail == d.CorporateEmail, ct);
        if (byEmail is not null && byEmail.StaffCode != d.StaffCode) row.Errors.Add("CORPORATE_EMAIL_USED_BY_OTHER_STAFF");
        if (byStaff is not null && !string.Equals(byStaff.CorporateEmail, d.CorporateEmail, StringComparison.OrdinalIgnoreCase)) row.Errors.Add("EXISTING_STAFF_EMAIL_CHANGE_REVIEW");

        if (byStaff is null)
        {
            if (!await access.CanCreateEmployeeAsync(d.CompanyCode, d.DepartmentCode, ct)) row.Errors.Add("HR_CREATE_SCOPE_DENIED");
            return;
        }
        if (!await access.CanEditEmployeeAsync(byStaff, ct)) row.Errors.Add("HR_EDIT_SCOPE_DENIED");

        var overlap = await db.EmployeeAssignments.AsNoTracking().AnyAsync(x => x.EmployeeId == byStaff.Id
            && x.EffectiveFrom <= (d.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo ?? DateOnly.MaxValue) >= d.EffectiveFrom, ct);
        if (overlap) row.Errors.Add("ASSIGNMENT_OVERLAP_REVIEW");
    }

    private static ImportRow Normalize(Dictionary<string, string> raw, int rowNumber, HashSet<string> allowedDomains)
    {
        var errors = new List<string>();
        var staff = Code(raw, "staff_code");
        var email = Get(raw, "corporate_email").ToLowerInvariant();
        var display = Get(raw, "display_name").Trim();
        var company = Code(raw, "company_code");
        var department = Code(raw, "department_code");
        var office = Code(raw, "office_code");
        var title = Get(raw, "position_title").Trim();
        var grade = NullableCode(raw, "grade_code");
        var manager = NullableCode(raw, "manager_staff_code");
        var policy = Code(raw, "timesheet_policy");
        var status = Code(raw, "employment_status");
        var effectiveFrom = Date(raw, "effective_from", required: true, errors);
        var effectiveTo = Date(raw, "effective_to", required: false, errors);
        var hireDate = Date(raw, "hire_date", required: false, errors);
        var lastWorking = Date(raw, "last_working_date", required: false, errors);

        if (string.IsNullOrWhiteSpace(staff)) errors.Add("STAFF_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(display)) errors.Add("DISPLAY_NAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(company)) errors.Add("COMPANY_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(department)) errors.Add("DEPARTMENT_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(office)) errors.Add("OFFICE_CODE_REQUIRED");
        if (string.IsNullOrWhiteSpace(title)) errors.Add("POSITION_TITLE_REQUIRED");
        if (!IsEmail(email)) errors.Add("INVALID_CORPORATE_EMAIL");
        else
        {
            var domain = email[(email.LastIndexOf('@') + 1)..];
            if (allowedDomains.Count > 0 && !allowedDomains.Contains(domain)) errors.Add("CORPORATE_EMAIL_DOMAIN_REVIEW");
        }
        if (policy is not ("REQUIRED" or "EXEMPT")) errors.Add("INVALID_TIMESHEET_POLICY");
        if (status is not ("ACTIVE" or "INACTIVE" or "NOTICE" or "TERMINATED")) errors.Add("INVALID_EMPLOYMENT_STATUS");
        if (effectiveFrom is not null && effectiveTo is not null && effectiveTo < effectiveFrom) errors.Add("INVALID_EFFECTIVE_RANGE");
        if (hireDate is not null && lastWorking is not null && lastWorking < hireDate) errors.Add("INVALID_EMPLOYMENT_DATE_RANGE");

        return new ImportRow(rowNumber, raw, new EmployeeImportData(staff, email, display, company, department, office, title, grade, manager, policy, status,
            effectiveFrom ?? DateOnly.MinValue, effectiveTo, hireDate, lastWorking), errors);
    }

    private static void MarkFileDuplicates(List<ImportRow> rows)
    {
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.Data.StaffCode)).GroupBy(x => x.Data.StaffCode, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var row in group) row.Errors.Add("DUPLICATE_STAFF_CODE_IN_FILE");
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.Data.CorporateEmail)).GroupBy(x => x.Data.CorporateEmail, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var row in group) row.Errors.Add("DUPLICATE_CORPORATE_EMAIL_IN_FILE");
    }

    private static bool IsReviewCode(string code) => code.EndsWith("_REVIEW", StringComparison.Ordinal) || code is "CORPORATE_EMAIL_DOMAIN_REVIEW";
    private static string CleanSource(string source) => string.IsNullOrWhiteSpace(source) ? "APPROVED_HR_SOURCE" : source.Trim()[..Math.Min(source.Trim().Length, 120)];
    private static string Get(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value : string.Empty;
    private static string Code(Dictionary<string, string> row, string key) => Get(row, key).Trim().ToUpperInvariant();
    private static string? NullableCode(Dictionary<string, string> row, string key) { var x = Code(row, key); return string.IsNullOrWhiteSpace(x) ? null : x; }
    private static bool IsEmail(string value) { try { return new MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    private static DateOnly? Date(Dictionary<string, string> row, string key, bool required, List<string> errors)
    {
        var value = Get(row, key).Trim();
        if (string.IsNullOrWhiteSpace(value)) { if (required) errors.Add($"{key.ToUpperInvariant()}_REQUIRED"); return null; }
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        errors.Add($"{key.ToUpperInvariant()}_INVALID_DATE"); return null;
    }
    private static MigrationBatchDto ToDto(MigrationBatch x) => new(x.Id, x.BatchKind, x.SourceSystem, x.SourceFileName, x.SourceSha256, x.Status, x.TotalRows, x.ValidRows, x.ReviewRows, x.InvalidRows, x.CreatedAt, x.ValidatedAt, x.CommittedAt);

    private sealed record ImportRow(int RowNumber, Dictionary<string, string> Raw, EmployeeImportData Data, List<string> Errors);
    private sealed record EmployeeImportData(string StaffCode, string CorporateEmail, string DisplayName, string CompanyCode, string DepartmentCode, string OfficeCode,
        string PositionTitle, string? GradeCode, string? ManagerStaffCode, string TimesheetPolicy, string EmploymentStatus, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
        DateOnly? HireDate, DateOnly? LastWorkingDate);
}
