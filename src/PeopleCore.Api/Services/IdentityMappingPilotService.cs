using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class IdentityMappingPilotService(
    PeopleCoreDbContext db,
    ICurrentUser current,
    IAuditService audit,
    IConfiguration configuration)
{
    private const string Kind = "ENTRA_DIRECTORY";
    private static readonly IReadOnlySet<string> Headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "tenant_id", "object_id", "user_principal_name", "display_name", "account_enabled" };

    public async Task<MigrationBatchDto> StageAsync(IFormFile file, string sourceSystem, CancellationToken ct)
    {
        var (bytes, rawRows) = await CsvImportParser.ReadAsync(file, Headers, maxRows: 10000, maxBytes: 10 * 1024 * 1024, ct);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existingBatch = await db.MigrationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.BatchKind == Kind && x.SourceSha256 == sha, ct);
        if (existingBatch is not null) return ToDto(existingBatch);

        var configuredTenant = ConfiguredTenant(configuration);
        var parsed = rawRows.Select((r, i) => Normalize(r, i + 2, configuredTenant)).ToList();
        MarkDuplicates(parsed);

        var upns = parsed.Where(x => x.Errors.Count == 0).Select(x => x.Upn).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var employees = await db.Employees.AsNoTracking().Where(x => upns.Contains(x.CorporateEmail)).ToListAsync(ct);
        var employeeByEmail = employees.GroupBy(x => x.CorporateEmail, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var batch = new MigrationBatch
        {
            Id = Guid.NewGuid(), BatchKind = Kind, SourceSystem = CleanSource(sourceSystem), SourceFileName = Path.GetFileName(file.FileName), SourceSha256 = sha,
            CreatedAt = now, ValidatedAt = now, CreatedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        };
        db.MigrationBatches.Add(batch);

        foreach (var row in parsed)
        {
            Employee? employee = null;
            if (row.Errors.Count == 0)
            {
                if (!employeeByEmail.TryGetValue(row.Upn, out var matches) || matches.Count == 0) row.Errors.Add("NO_EMPLOYEE_EXACT_EMAIL_MATCH_REVIEW");
                else if (matches.Count != 1) row.Errors.Add("AMBIGUOUS_EMPLOYEE_EMAIL_MATCH_REVIEW");
                else employee = matches[0];
                if (employee is not null && !string.Equals(employee.EmploymentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)) row.Errors.Add("EMPLOYEE_NOT_ACTIVE_REVIEW");
                if (!row.AccountEnabled) row.Errors.Add("ENTRA_ACCOUNT_DISABLED_REVIEW");
            }

            var rowStatus = row.Errors.Count == 0 ? "VALID" : row.Errors.Any(IsReviewCode) ? "REVIEW" : "INVALID";
            var migrationRow = new MigrationRow
            {
                Id = Guid.NewGuid(), BatchId = batch.Id, RowNumber = row.RowNumber, ExternalKey = row.Upn,
                PayloadJson = JsonSerializer.Serialize(row.Raw), NormalizedJson = JsonSerializer.Serialize(new { row.TenantId, row.ObjectId, row.Upn, row.DisplayName, row.AccountEnabled }),
                Status = rowStatus, ErrorsJson = JsonSerializer.Serialize(row.Errors.Distinct().ToArray()), EmployeeId = employee?.Id, CreatedAt = now
            };
            db.MigrationRows.Add(migrationRow);

            if (rowStatus == "VALID" && employee is not null)
            {
                var sameIdentity = await db.EmployeeIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.EntraTenantId == row.TenantId && x.EntraObjectId == row.ObjectId, ct);
                var activeForEmployee = await db.EmployeeIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.IsActive && x.RevokedAt == null, ct);
                var candidateStatus = "READY";
                string? reason = null;
                if (sameIdentity is not null && sameIdentity.EmployeeId != employee.Id) { candidateStatus = "REVIEW"; reason = "ENTRA_IDENTITY_LINKED_TO_OTHER_EMPLOYEE"; }
                else if (activeForEmployee is not null && (sameIdentity is null || activeForEmployee.Id != sameIdentity.Id)) { candidateStatus = "REVIEW"; reason = "EMPLOYEE_ALREADY_HAS_DIFFERENT_ACTIVE_IDENTITY"; }
                else if (sameIdentity is not null && sameIdentity.EmployeeId == employee.Id && sameIdentity.IsActive && sameIdentity.RevokedAt == null) { candidateStatus = "SKIPPED"; reason = "ALREADY_LINKED"; }

                db.IdentityMappingCandidates.Add(new IdentityMappingCandidate
                {
                    Id = Guid.NewGuid(), BatchId = batch.Id, EmployeeId = employee.Id, StaffCode = employee.StaffCode, CorporateEmail = employee.CorporateEmail,
                    EntraTenantId = row.TenantId, EntraObjectId = row.ObjectId, EntraUserPrincipalName = row.Upn, EntraDisplayName = row.DisplayName,
                    AccountEnabled = row.AccountEnabled, MatchType = "EXACT_EMAIL", Status = candidateStatus, Reason = reason
                });
                if (candidateStatus == "REVIEW") { migrationRow.Status = "REVIEW"; migrationRow.ErrorsJson = JsonSerializer.Serialize(new[] { reason! }); }
            }

            if (migrationRow.Status == "VALID") batch.ValidRows++;
            else if (migrationRow.Status == "REVIEW") batch.ReviewRows++;
            else batch.InvalidRows++;
        }

        batch.TotalRows = parsed.Count;
        var readyCandidates = db.IdentityMappingCandidates.Local.Count(x => x.BatchId == batch.Id && x.Status == "READY");
        if (batch.InvalidRows != 0 || batch.ReviewRows != 0) batch.Status = "REVIEW";
        else if (readyCandidates == 0) { batch.Status = "COMMITTED"; batch.CommittedAt = now; }
        else batch.Status = "READY";
        audit.Record("ENTRA_DIRECTORY_PILOT_STAGED", "MigrationBatch", batch.Id.ToString(), new { batch.SourceFileName, batch.SourceSha256, batch.TotalRows, batch.ValidRows, batch.ReviewRows, batch.InvalidRows, match = "EXACT_EMAIL_ONLY" });
        await db.SaveChangesAsync(ct);
        return ToDto(batch);
    }

    public async Task<IReadOnlyList<MigrationBatchDto>> ListAsync(int take, CancellationToken ct) =>
        await db.MigrationBatches.AsNoTracking().Where(x => x.BatchKind == Kind).OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(x => new MigrationBatchDto(x.Id, x.BatchKind, x.SourceSystem, x.SourceFileName, x.SourceSha256, x.Status, x.TotalRows, x.ValidRows, x.ReviewRows, x.InvalidRows, x.CreatedAt, x.ValidatedAt, x.CommittedAt)).ToListAsync(ct);

    public async Task<IReadOnlyList<IdentityMappingCandidateDto>> CandidatesAsync(Guid batchId, CancellationToken ct) =>
        await db.IdentityMappingCandidates.AsNoTracking().Where(x => x.BatchId == batchId).OrderBy(x => x.StaffCode)
            .Select(x => new IdentityMappingCandidateDto(x.Id, x.BatchId, x.EmployeeId, x.StaffCode, x.CorporateEmail, x.EntraTenantId, x.EntraObjectId,
                x.EntraUserPrincipalName, x.EntraDisplayName, x.AccountEnabled, x.MatchType, x.Status, x.Reason, x.ConfirmedAt, x.ConfirmedBy)).ToListAsync(ct);

    public async Task<IdentityMappingCandidateDto> ConfirmAsync(Guid candidateId, ConfirmIdentityCandidateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var candidate = await db.IdentityMappingCandidates.SingleOrDefaultAsync(x => x.Id == candidateId, ct) ?? throw new InvalidOperationException("IDENTITY_CANDIDATE_NOT_FOUND");
        if (candidate.Status == "CONFIRMED") return ToDto(candidate);
        if (candidate.Status != "READY" || !candidate.AccountEnabled) throw new InvalidOperationException("IDENTITY_CANDIDATE_NOT_READY");

        var configuredTenant = ConfiguredTenant(configuration);
        if (!string.IsNullOrWhiteSpace(configuredTenant) && !string.Equals(configuredTenant, candidate.EntraTenantId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ENTRA_TENANT_MISMATCH");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sameIdentity = await db.EmployeeIdentities.SingleOrDefaultAsync(x => x.EntraTenantId == candidate.EntraTenantId && x.EntraObjectId == candidate.EntraObjectId, ct);
        if (sameIdentity is not null && sameIdentity.EmployeeId != candidate.EmployeeId) throw new InvalidOperationException("ENTRA_IDENTITY_ALREADY_LINKED");
        var currentActive = await db.EmployeeIdentities.SingleOrDefaultAsync(x => x.EmployeeId == candidate.EmployeeId && x.IsActive && x.RevokedAt == null, ct);
        if (currentActive is not null && (sameIdentity is null || currentActive.Id != sameIdentity.Id)) throw new InvalidOperationException("EMPLOYEE_ALREADY_HAS_ACTIVE_IDENTITY");

        var employee = await db.Employees.SingleAsync(x => x.Id == candidate.EmployeeId, ct);
        var now = DateTimeOffset.UtcNow;
        EmployeeIdentity link;
        if (sameIdentity is null)
        {
            link = new EmployeeIdentity
            {
                Id = Guid.NewGuid(), EmployeeId = candidate.EmployeeId, EntraTenantId = candidate.EntraTenantId, EntraObjectId = candidate.EntraObjectId,
                LinkedEmail = employee.CorporateEmail, IsActive = true, LinkedAt = now
            };
            db.EmployeeIdentities.Add(link);
        }
        else
        {
            link = sameIdentity;
            link.IsActive = true;
            link.RevokedAt = null;
            link.LinkedAt = now;
            link.LinkedEmail = employee.CorporateEmail;
        }

        candidate.Status = "CONFIRMED";
        candidate.Reason = request.Reason.Trim();
        candidate.ConfirmedAt = now;
        candidate.ConfirmedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED";
        audit.Record("ENTRA_IDENTITY_CANDIDATE_CONFIRMED", "IdentityMappingCandidate", candidate.Id.ToString(), new { candidate.StaffCode, candidate.EntraTenantId, candidate.EntraObjectId, request.Reason });
        await db.SaveChangesAsync(ct);
        await UpdateBatchStatusAsync(candidate.BatchId, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(candidate);
    }

    public async Task<IdentityMappingCandidateDto> SkipAsync(Guid candidateId, ConfirmIdentityCandidateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var candidate = await db.IdentityMappingCandidates.SingleOrDefaultAsync(x => x.Id == candidateId, ct) ?? throw new InvalidOperationException("IDENTITY_CANDIDATE_NOT_FOUND");
        if (candidate.Status != "READY") throw new InvalidOperationException("IDENTITY_CANDIDATE_NOT_READY");
        candidate.Status = "SKIPPED";
        candidate.Reason = request.Reason.Trim();
        audit.Record("ENTRA_IDENTITY_CANDIDATE_SKIPPED", "IdentityMappingCandidate", candidate.Id.ToString(), new { candidate.StaffCode, request.Reason });
        await db.SaveChangesAsync(ct);
        await UpdateBatchStatusAsync(candidate.BatchId, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(candidate);
    }

    private async Task UpdateBatchStatusAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.MigrationBatches.SingleAsync(x => x.Id == batchId && x.BatchKind == Kind, ct);
        var candidateStatuses = await db.IdentityMappingCandidates.Where(x => x.BatchId == batchId).Select(x => x.Status).ToListAsync(ct);
        var rowStatuses = await db.MigrationRows.Where(x => x.BatchId == batchId).Select(x => x.Status).ToListAsync(ct);
        if (rowStatuses.Any(x => x is "REVIEW" or "INVALID") || candidateStatuses.Any(x => x == "REVIEW")) batch.Status = "REVIEW";
        else if (candidateStatuses.Any(x => x == "READY")) batch.Status = "READY";
        else { batch.Status = "COMMITTED"; batch.CommittedAt = DateTimeOffset.UtcNow; }
    }

    private static ParsedRow Normalize(Dictionary<string, string> raw, int rowNumber, string configuredTenant)
    {
        var errors = new List<string>();
        var tenant = Get(raw, "tenant_id").Trim();
        var objectId = Get(raw, "object_id").Trim();
        var upn = Get(raw, "user_principal_name").Trim().ToLowerInvariant();
        var name = Get(raw, "display_name").Trim();
        var enabledRaw = Get(raw, "account_enabled").Trim();
        var enabledOk = bool.TryParse(enabledRaw, out var enabled);
        if (!Guid.TryParse(tenant, out _)) errors.Add("INVALID_TENANT_ID");
        if (!Guid.TryParse(objectId, out _)) errors.Add("INVALID_OBJECT_ID");
        if (string.IsNullOrWhiteSpace(upn) || !upn.Contains('@')) errors.Add("INVALID_USER_PRINCIPAL_NAME");
        if (string.IsNullOrWhiteSpace(name)) errors.Add("DISPLAY_NAME_REQUIRED");
        if (!enabledOk) errors.Add("INVALID_ACCOUNT_ENABLED");
        if (!string.IsNullOrWhiteSpace(configuredTenant) && !string.Equals(configuredTenant, tenant, StringComparison.OrdinalIgnoreCase)) errors.Add("ENTRA_TENANT_MISMATCH_REVIEW");
        return new ParsedRow(rowNumber, raw, tenant, objectId, upn, name, enabled, errors);
    }

    private static void MarkDuplicates(List<ParsedRow> rows)
    {
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.ObjectId)).GroupBy(x => $"{x.TenantId}|{x.ObjectId}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var row in group) row.Errors.Add("DUPLICATE_ENTRA_OBJECT_IN_FILE");
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.Upn)).GroupBy(x => x.Upn, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var row in group) row.Errors.Add("DUPLICATE_UPN_IN_FILE");
    }

    private static bool IsReviewCode(string code) => code.EndsWith("_REVIEW", StringComparison.Ordinal) || code.Contains("MATCH_REVIEW", StringComparison.Ordinal);
    private static string ConfiguredTenant(IConfiguration configuration)
    {
        var value = (configuration["Entra:TenantId"] ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("__", StringComparison.Ordinal) ? string.Empty : value;
    }
    private static string CleanSource(string source) => string.IsNullOrWhiteSpace(source) ? "ENTRA_DIRECTORY_EXPORT" : source.Trim()[..Math.Min(source.Trim().Length, 120)];
    private static string Get(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value : string.Empty;
    private static MigrationBatchDto ToDto(MigrationBatch x) => new(x.Id, x.BatchKind, x.SourceSystem, x.SourceFileName, x.SourceSha256, x.Status, x.TotalRows, x.ValidRows, x.ReviewRows, x.InvalidRows, x.CreatedAt, x.ValidatedAt, x.CommittedAt);
    private static IdentityMappingCandidateDto ToDto(IdentityMappingCandidate x) => new(x.Id, x.BatchId, x.EmployeeId, x.StaffCode, x.CorporateEmail, x.EntraTenantId, x.EntraObjectId, x.EntraUserPrincipalName, x.EntraDisplayName, x.AccountEnabled, x.MatchType, x.Status, x.Reason, x.ConfirmedAt, x.ConfirmedBy);
    private sealed record ParsedRow(int RowNumber, Dictionary<string, string> Raw, string TenantId, string ObjectId, string Upn, string DisplayName, bool AccountEnabled, List<string> Errors);
}
