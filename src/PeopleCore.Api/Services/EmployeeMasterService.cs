using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class EmployeeMasterService(PeopleCoreDbContext db, IAccessControlService access, IAuditService audit, ICurrentUser current)
{
    public async Task<EmployeeEnvelopeDto?> GetByStaffCodeAsync(string staffCode, CancellationToken ct)
    {
        staffCode = NormalizeCode(staffCode);
        var employee = await db.Employees.Include(x => x.PrivateProfile).AsNoTracking().SingleOrDefaultAsync(x => x.StaffCode == staffCode, ct);
        if (employee is null) return null;
        var decision = await access.DecideReadEmployeeAsync(employee, ct);
        if (!decision.Allowed) throw new UnauthorizedAccessException(decision.Reason);
        return ToEnvelope(employee, await CurrentAssignmentAsync(employee.Id, ct), decision);
    }

    public async Task<EmployeeEnvelopeDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.Include(x => x.PrivateProfile).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (employee is null) return null;
        var decision = await access.DecideReadEmployeeAsync(employee, ct);
        if (!decision.Allowed) throw new UnauthorizedAccessException(decision.Reason);
        return ToEnvelope(employee, await CurrentAssignmentAsync(employee.Id, ct), decision);
    }

    public async Task<IReadOnlyList<EmployeeWorkDto>> SearchAsync(string? query, int take, CancellationToken ct)
    {
        var allowed = await access.GetDirectoryEmployeeIdsAsync(ct);
        if (allowed.Count == 0) return Array.Empty<EmployeeWorkDto>();
        var ids = allowed.ToArray();
        var q = db.Employees.AsNoTracking().Where(x => ids.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            q = q.Where(x => x.StaffCode.ToLower().Contains(term) || x.DisplayName.ToLower().Contains(term) || x.CorporateEmail.ToLower().Contains(term));
        }
        var employees = await q.OrderBy(x => x.DisplayName).Take(take).ToListAsync(ct);
        var result = new List<EmployeeWorkDto>(employees.Count);
        foreach (var employee in employees)
            result.Add(ToWork(employee, await CurrentAssignmentAsync(employee.Id, ct)));
        return result;
    }

    public async Task<EmployeeEnvelopeDto> CreateAsync(CreateEmployeeRequest req, CancellationToken ct)
    {
        var staffCode = NormalizeCode(req.StaffCode);
        var company = NormalizeCode(req.CompanyCode);
        var department = NormalizeCode(req.DepartmentCode);
        if (!await access.CanCreateEmployeeAsync(company, department, ct)) throw new UnauthorizedAccessException("HR_CREATE_SCOPE_DENIED");
        if (await db.Employees.AnyAsync(x => x.StaffCode == staffCode || x.CorporateEmail == req.CorporateEmail.Trim().ToLower(), ct)) throw new InvalidOperationException("EMPLOYEE_ALREADY_EXISTS");
        await ValidateManagerAsync(req.ManagerEmployeeId, null, ct);

        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(), StaffCode = staffCode, CorporateEmail = req.CorporateEmail.Trim().ToLowerInvariant(), DisplayName = req.DisplayName.Trim(),
            CompanyCode = company, DepartmentCode = department, OfficeCode = NormalizeCode(req.OfficeCode), PositionTitle = req.PositionTitle.Trim(),
            GradeCode = NormalizeNullable(req.GradeCode), ManagerEmployeeId = req.ManagerEmployeeId, TimesheetPolicy = NormalizePolicy(req.TimesheetPolicy),
            EmploymentStatus = "ACTIVE", HireDate = req.HireDate, EffectiveFrom = req.EffectiveFrom, UpdatedAt = now, RowVersion = 1
        };
        var privateProfile = new EmployeePrivateProfile { EmployeeId = employee.Id };
        employee.PrivateProfile = privateProfile;
        var assignment = new EmployeeAssignment
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, CompanyCode = employee.CompanyCode, DepartmentCode = employee.DepartmentCode,
            OfficeCode = employee.OfficeCode, PositionTitle = employee.PositionTitle, GradeCode = employee.GradeCode, ManagerEmployeeId = employee.ManagerEmployeeId,
            TimesheetPolicy = employee.TimesheetPolicy, EffectiveFrom = req.EffectiveFrom, CreatedAt = now, CreatedBy = current.StaffCode ?? "SYSTEM"
        };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(assignment);
        audit.Record("EMPLOYEE_MASTER_CREATED", "Employee", employee.Id.ToString(), new { employee.StaffCode, employee.CompanyCode, employee.DepartmentCode });
        await db.SaveChangesAsync(ct);
        return ToEnvelope(employee, assignment, new(true, EmployeeFieldSet.Work | EmployeeFieldSet.HrSelf | EmployeeFieldSet.HrPrivate, "HR_SCOPED", "CREATED_BY_HR"));
    }

    public async Task<EmployeeEnvelopeDto?> PatchAsync(string staffCode, PatchEmployeeRequest req, CancellationToken ct)
    {
        staffCode = NormalizeCode(staffCode);
        var employee = await db.Employees.Include(x => x.PrivateProfile).SingleOrDefaultAsync(x => x.StaffCode == staffCode, ct);
        if (employee is null) return null;
        if (!await access.CanEditEmployeeAsync(employee, ct)) throw new UnauthorizedAccessException("HR_EDIT_SCOPE_DENIED");
        if (employee.RowVersion != req.ExpectedRowVersion) throw new InvalidOperationException("EMPLOYEE_VERSION_CONFLICT");

        var changed = new List<string>();
        if (req.CorporateEmail is not null && !string.Equals(employee.CorporateEmail, req.CorporateEmail.Trim(), StringComparison.OrdinalIgnoreCase)) { employee.CorporateEmail = req.CorporateEmail.Trim().ToLowerInvariant(); changed.Add(nameof(employee.CorporateEmail)); }
        if (req.DisplayName is not null && employee.DisplayName != req.DisplayName.Trim()) { employee.DisplayName = req.DisplayName.Trim(); changed.Add(nameof(employee.DisplayName)); }
        if (req.EmploymentStatus is not null && employee.EmploymentStatus != NormalizeCode(req.EmploymentStatus)) { employee.EmploymentStatus = NormalizeCode(req.EmploymentStatus); changed.Add(nameof(employee.EmploymentStatus)); }
        if (req.HireDate is not null && employee.HireDate != req.HireDate) { employee.HireDate = req.HireDate; changed.Add(nameof(employee.HireDate)); }
        if (req.LastWorkingDate is not null && employee.LastWorkingDate != req.LastWorkingDate) { employee.LastWorkingDate = req.LastWorkingDate; changed.Add(nameof(employee.LastWorkingDate)); }
        if (req.HrPrivateNotes is not null) { employee.PrivateProfile ??= new EmployeePrivateProfile { EmployeeId = employee.Id }; employee.PrivateProfile.HrPrivateNotes = req.HrPrivateNotes.Trim(); changed.Add(nameof(EmployeePrivateProfile.HrPrivateNotes)); }
        if (employee.LastWorkingDate is not null && employee.HireDate is not null && employee.LastWorkingDate < employee.HireDate) throw new InvalidOperationException("INVALID_LIFECYCLE_RANGE");

        employee.RowVersion++; employee.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Record("EMPLOYEE_MASTER_UPDATED", "Employee", employee.Id.ToString(), new { employee.StaffCode, changedFields = changed });
        await db.SaveChangesAsync(ct);
        var decision = await access.DecideReadEmployeeAsync(employee, ct);
        return ToEnvelope(employee, await CurrentAssignmentAsync(employee.Id, ct), decision);
    }

    public async Task<Guid?> AddAssignmentAsync(string staffCode, CreateAssignmentRequest req, CancellationToken ct)
    {
        staffCode = NormalizeCode(staffCode);
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.StaffCode == staffCode, ct);
        if (employee is null) return null;
        if (!await access.CanEditEmployeeAsync(employee, ct)) throw new UnauthorizedAccessException("HR_EDIT_SCOPE_DENIED");
        if (employee.RowVersion != req.ExpectedRowVersion) throw new InvalidOperationException("EMPLOYEE_VERSION_CONFLICT");
        if (req.EffectiveTo is not null && req.EffectiveTo < req.EffectiveFrom) throw new InvalidOperationException("INVALID_EFFECTIVE_RANGE");
        await ValidateManagerAsync(req.ManagerEmployeeId, employee.Id, ct);

        var overlapping = await db.EmployeeAssignments.Where(x => x.EmployeeId == employee.Id
            && (x.EffectiveTo == null || x.EffectiveTo >= req.EffectiveFrom)
            && (req.EffectiveTo == null || x.EffectiveFrom <= req.EffectiveTo)).OrderBy(x => x.EffectiveFrom).ToListAsync(ct);

        // Normal transition: a new assignment can close one preceding open-ended assignment.
        if (overlapping.Count == 1 && overlapping[0].EffectiveTo is null && overlapping[0].EffectiveFrom < req.EffectiveFrom)
            overlapping[0].EffectiveTo = req.EffectiveFrom.AddDays(-1);
        else if (overlapping.Count > 0)
            throw new InvalidOperationException("ASSIGNMENT_EFFECTIVE_DATE_OVERLAP");

        var row = new EmployeeAssignment
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id, CompanyCode = NormalizeCode(req.CompanyCode), DepartmentCode = NormalizeCode(req.DepartmentCode),
            OfficeCode = NormalizeCode(req.OfficeCode), PositionTitle = req.PositionTitle.Trim(), GradeCode = NormalizeNullable(req.GradeCode), ManagerEmployeeId = req.ManagerEmployeeId,
            TimesheetPolicy = NormalizePolicy(req.TimesheetPolicy), EffectiveFrom = req.EffectiveFrom, EffectiveTo = req.EffectiveTo,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = current.StaffCode ?? "SYSTEM"
        };
        db.EmployeeAssignments.Add(row);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (row.EffectiveFrom <= today && (row.EffectiveTo == null || row.EffectiveTo >= today))
        {
            employee.CompanyCode = row.CompanyCode; employee.DepartmentCode = row.DepartmentCode; employee.OfficeCode = row.OfficeCode;
            employee.PositionTitle = row.PositionTitle; employee.GradeCode = row.GradeCode; employee.ManagerEmployeeId = row.ManagerEmployeeId; employee.TimesheetPolicy = row.TimesheetPolicy;
        }
        employee.RowVersion++; employee.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Record("EMPLOYEE_ASSIGNMENT_CREATED", "EmployeeAssignment", row.Id.ToString(), new { employee.StaffCode, row.EffectiveFrom, row.EffectiveTo, row.DepartmentCode, row.ManagerEmployeeId });
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task<EmployeeAssignment?> CurrentAssignmentAsync(Guid employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.EmployeeAssignments.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
    }

    private async Task ValidateManagerAsync(Guid? managerId, Guid? employeeId, CancellationToken ct)
    {
        if (managerId is null) return;
        if (managerId == employeeId) throw new InvalidOperationException("EMPLOYEE_CANNOT_MANAGE_SELF");
        if (!await db.Employees.AnyAsync(x => x.Id == managerId && x.EmploymentStatus == "ACTIVE", ct)) throw new InvalidOperationException("MANAGER_NOT_FOUND_OR_INACTIVE");
    }

    private static EmployeeEnvelopeDto ToEnvelope(Employee x, EmployeeAssignment? a, EmployeeAccessDecision decision)
    {
        var self = decision.Fields.HasFlag(EmployeeFieldSet.HrSelf)
            ? new EmployeeSelfDto(x.PrivateProfile?.PersonalEmail, x.PrivateProfile?.Mobile, x.PrivateProfile?.DateOfBirth, x.PrivateProfile?.HomeCity, x.HireDate, x.LastWorkingDate) : null;
        var hrPrivate = decision.Fields.HasFlag(EmployeeFieldSet.HrPrivate) ? new EmployeeHrPrivateDto(x.PrivateProfile?.HrPrivateNotes) : null;
        var fieldSets = Enum.GetValues<EmployeeFieldSet>().Where(f => f != EmployeeFieldSet.None && decision.Fields.HasFlag(f)).Select(f => f.ToString().ToUpperInvariant()).ToArray();
        return new EmployeeEnvelopeDto(ToWork(x, a), self, hrPrivate, decision.Scope, fieldSets);
    }

    private static EmployeeWorkDto ToWork(Employee x, EmployeeAssignment? a) => new(
        x.Id, x.StaffCode, x.CorporateEmail, x.DisplayName,
        a?.CompanyCode ?? x.CompanyCode, a?.DepartmentCode ?? x.DepartmentCode, a?.OfficeCode ?? x.OfficeCode,
        a?.PositionTitle ?? x.PositionTitle, a?.GradeCode ?? x.GradeCode, a?.ManagerEmployeeId ?? x.ManagerEmployeeId,
        a?.TimesheetPolicy ?? x.TimesheetPolicy, x.EmploymentStatus, x.EffectiveFrom, x.EffectiveTo, x.RowVersion);

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static string NormalizePolicy(string value) => string.IsNullOrWhiteSpace(value) ? "REQUIRED" : value.Trim().ToUpperInvariant();
}
