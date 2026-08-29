using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class PilotUatService(
    PeopleCoreDbContext db,
    ICurrentUser current,
    IAuditService audit,
    IConfiguration configuration)
{
    private const string EmployeeKind = "EMPLOYEE_MASTER";
    private const string EntraKind = "ENTRA_DIRECTORY";

    public bool Enabled => configuration.GetValue<bool>("Pilot:Enabled");

    public async Task<PilotRunDto> StartAsync(StartPilotRunRequest request, CancellationToken ct)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");

        var employeeBatch = await db.MigrationBatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.EmployeeMigrationBatchId && x.BatchKind == EmployeeKind, ct)
            ?? throw new InvalidOperationException("EMPLOYEE_MIGRATION_BATCH_NOT_FOUND");
        var entraBatch = await db.MigrationBatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.EntraMigrationBatchId && x.BatchKind == EntraKind, ct)
            ?? throw new InvalidOperationException("ENTRA_MIGRATION_BATCH_NOT_FOUND");

        var run = new PilotRun
        {
            Id = Guid.NewGuid(),
            EmployeeMigrationBatchId = employeeBatch.Id,
            EntraMigrationBatchId = entraBatch.Id,
            Status = "OPEN",
            StartedAt = DateTimeOffset.UtcNow,
            StartedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED",
            CompletionNote = null
        };
        db.PilotRuns.Add(run);
        audit.Record("PILOT_UAT_RUN_STARTED", "PilotRun", run.Id.ToString(), new
        {
            request.EmployeeMigrationBatchId,
            request.EntraMigrationBatchId,
            reason = request.Reason
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<PilotRunDto> EvaluateAsync(Guid runId, CancellationToken ct)
    {
        EnsureEnabled();
        var run = await db.PilotRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new InvalidOperationException("PILOT_RUN_NOT_FOUND");
        if (run.Status == "COMPLETED") throw new InvalidOperationException("PILOT_RUN_ALREADY_COMPLETED");

        var employeeBatch = await db.MigrationBatches.AsNoTracking().SingleAsync(x => x.Id == run.EmployeeMigrationBatchId, ct);
        var entraBatch = await db.MigrationBatches.AsNoTracking().SingleAsync(x => x.Id == run.EntraMigrationBatchId, ct);

        var employeeIds = await db.MigrationRows.AsNoTracking()
            .Where(x => x.BatchId == employeeBatch.Id && x.Status == "COMMITTED" && x.EmployeeId != null)
            .Select(x => x.EmployeeId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var activeLinks = await db.EmployeeIdentities.AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null)
            .Select(x => new { x.EmployeeId, x.EntraTenantId })
            .ToListAsync(ct);
        var linkedEmployeeIds = activeLinks.Select(x => x.EmployeeId).Distinct().ToList();
        var candidateEmployeeIds = await db.IdentityMappingCandidates.AsNoTracking()
            .Where(x => x.BatchId == entraBatch.Id)
            .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
        var candidateOutsideCohort = candidateEmployeeIds.Except(employeeIds).Count();
        var configuredTenant = (configuration["Entra:TenantId"] ?? string.Empty).Trim();
        var wrongTenantLinks = PlaceholderFree("Entra:TenantId")
            ? activeLinks.Count(x => !string.Equals(x.EntraTenantId, configuredTenant, StringComparison.OrdinalIgnoreCase))
            : 0;

        var readyCandidates = await db.IdentityMappingCandidates.AsNoTracking().CountAsync(x => x.BatchId == entraBatch.Id && x.Status == "READY", ct);
        var reviewCandidates = await db.IdentityMappingCandidates.AsNoTracking().CountAsync(x => x.BatchId == entraBatch.Id && x.Status == "REVIEW", ct);
        var confirmedCandidates = await db.IdentityMappingCandidates.AsNoTracking().CountAsync(x => x.BatchId == entraBatch.Id && x.Status == "CONFIRMED", ct);

        var checks = new List<(string Code, string Status, string Summary)>
        {
            ("ENTRA_TENANT_CONFIGURED", PlaceholderFree("Entra:TenantId") ? "PASS" : "FAIL", PlaceholderFree("Entra:TenantId") ? "Entra tenant is configured." : "Entra tenant is missing or still a placeholder."),
            ("ENTRA_CLIENT_CONFIGURED", PlaceholderFree("Entra:ClientId") ? "PASS" : "FAIL", PlaceholderFree("Entra:ClientId") ? "Entra API client is configured." : "Entra API client is missing or still a placeholder."),
            ("SHADOW_VALIDATION_ONLY", !configuration.GetValue<bool>("Payroll:ShadowEngineEnabled") ? "PASS" : "FAIL", !configuration.GetValue<bool>("Payroll:ShadowEngineEnabled") ? "Shadow Payroll remains disabled for official calculation." : "Shadow Payroll is enabled; V65 pilot boundary is violated."),
            ("OFFICIAL_PAYROLL_SOURCE_BRAVO", string.Equals(configuration["Payroll:OfficialResultSource"], "BRAVO", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", "Official payroll source must remain BRAVO in Phase 1-2."),
            ("HR_BATCH_COMMITTED", employeeBatch.Status == "COMMITTED" ? "PASS" : "FAIL", $"Employee Master batch status: {employeeBatch.Status}."),
            ("ENTRA_BATCH_RESOLVED", entraBatch.Status == "COMMITTED" ? "PASS" : entraBatch.Status == "READY" ? "WARN" : "FAIL", $"Entra mapping batch status: {entraBatch.Status}."),
            ("COHORT_EMPLOYEES_PRESENT", employeeIds.Count > 0 ? "PASS" : "FAIL", $"Committed pilot cohort employees: {employeeIds.Count}."),
            ("COHORT_IDENTITY_COVERAGE", employeeIds.Count > 0 && linkedEmployeeIds.Count == employeeIds.Count ? "PASS" : linkedEmployeeIds.Count > 0 ? "WARN" : "FAIL", $"Active Entra links: {linkedEmployeeIds.Count}/{employeeIds.Count}."),
            ("ENTRA_BATCH_COHORT_ALIGNMENT", candidateOutsideCohort == 0 ? "PASS" : "FAIL", $"Entra candidate employees outside selected HR cohort: {candidateOutsideCohort}."),
            ("COHORT_TENANT_ALIGNMENT", wrongTenantLinks == 0 ? "PASS" : "FAIL", $"Active cohort links on a different Entra tenant: {wrongTenantLinks}."),
            ("NO_READY_MAPPING_LEFT", readyCandidates == 0 ? "PASS" : "WARN", $"READY identity candidates remaining: {readyCandidates}."),
            ("NO_REVIEW_MAPPING_LEFT", reviewCandidates == 0 ? "PASS" : "FAIL", $"REVIEW identity candidates remaining: {reviewCandidates}."),
            ("CONFIRMED_MAPPING_EVIDENCE", confirmedCandidates > 0 ? "PASS" : "WARN", $"Confirmed Entra candidates: {confirmedCandidates}."),
            ("BRAVO_ADAPTER_BOUNDARY", string.Equals(configuration["Bravo:Mode"], "Stub", StringComparison.OrdinalIgnoreCase) ? "WARN" : "PASS", string.Equals(configuration["Bravo:Mode"], "Stub", StringComparison.OrdinalIgnoreCase) ? "BRAVO adapter is still Stub; acceptable for V65 identity/migration pilot, not for payroll integration UAT." : "BRAVO adapter is configured beyond Stub mode.")
        };

        var now = DateTimeOffset.UtcNow;
        var actor = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED";
        foreach (var check in checks)
        {
            db.PilotChecks.Add(new PilotCheck
            {
                Id = Guid.NewGuid(), PilotRunId = run.Id, CheckCode = check.Code, Status = check.Status,
                Summary = check.Summary, CheckedAt = now, CheckedBy = actor
            });
        }

        run.Status = checks.Any(x => x.Status == "FAIL") ? "FAIL" : checks.Any(x => x.Status == "WARN") ? "WARN" : "PASS";
        audit.Record("PILOT_UAT_RUN_EVALUATED", "PilotRun", run.Id.ToString(), new
        {
            run.Status,
            pass = checks.Count(x => x.Status == "PASS"),
            warn = checks.Count(x => x.Status == "WARN"),
            fail = checks.Count(x => x.Status == "FAIL"),
            cohort = employeeIds.Count,
            linked = linkedEmployeeIds.Count
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<PilotRunDto> CompleteAsync(Guid runId, CompletePilotRunRequest request, CancellationToken ct)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var run = await db.PilotRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new InvalidOperationException("PILOT_RUN_NOT_FOUND");
        if (run.Status == "FAIL" || run.Status == "OPEN") throw new InvalidOperationException("PILOT_RUN_NOT_READY_TO_COMPLETE");
        if (run.Status == "COMPLETED") return await GetAsync(run.Id, ct);

        run.Status = "COMPLETED";
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.CompletionNote = request.Reason.Trim();
        audit.Record("PILOT_UAT_RUN_COMPLETED", "PilotRun", run.Id.ToString(), new { reason = request.Reason });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<PilotRunDto> GetAsync(Guid runId, CancellationToken ct)
    {
        EnsureEnabled();
        var run = await db.PilotRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new InvalidOperationException("PILOT_RUN_NOT_FOUND");
        var allChecks = await db.PilotChecks.AsNoTracking().Where(x => x.PilotRunId == runId)
            .OrderByDescending(x => x.CheckedAt).ToListAsync(ct);
        var latest = allChecks.GroupBy(x => x.CheckCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(x => x.CheckCode)
            .Select(x => new PilotCheckDto(x.Id, x.CheckCode, x.Status, x.Summary, x.CheckedAt)).ToList();
        return new PilotRunDto(run.Id, run.Status, run.EmployeeMigrationBatchId, run.EntraMigrationBatchId,
            run.StartedAt, run.StartedBy, run.CompletedAt, run.CompletionNote, latest);
    }

    private bool PlaceholderFree(string key)
    {
        var value = (configuration[key] ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(value) && !value.StartsWith("__", StringComparison.Ordinal) && !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureEnabled()
    {
        if (!Enabled) throw new InvalidOperationException("PILOT_UAT_DISABLED");
    }
}
