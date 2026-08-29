using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class V71HrPilotService(PeopleCoreDbContext db, IConfiguration c, ICurrentUser current, IAuditService audit)
{
    public static readonly IReadOnlySet<string> Personas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Roles.Employee, Roles.Manager, Roles.Hr, Roles.Payroll, Roles.Admin };

    public static readonly IReadOnlyDictionary<string, string> RequiredScenarios = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AUTH_LOGIN_EMPLOYEE"] = Roles.Employee,
        ["AUTH_LOGIN_MANAGER"] = Roles.Manager,
        ["AUTH_LOGIN_HR"] = Roles.Hr,
        ["AUTH_LOGIN_PAYROLL"] = Roles.Payroll,
        ["AUTH_LOGIN_ADMIN"] = Roles.Admin,
        ["SELF_PROFILE_READ"] = Roles.Employee,
        ["MANAGER_DIRECT_REPORT_BOUNDARY"] = Roles.Manager,
        ["HR_MASTER_EDIT_SCOPE"] = Roles.Hr,
        ["HR_PRIVATE_FIELD_ACCESS"] = Roles.Hr,
        ["PAYROLL_NO_HR_PRIVATE"] = Roles.Payroll,
        ["ADMIN_NO_HR_PRIVATE"] = Roles.Admin,
        ["AUDIT_CORRELATION"] = Roles.Admin,
        ["HCM_OFFICE_PATH"] = "ANY",
        ["HN_OFFICE_PATH"] = "ANY"
    };

    public async Task<V71HrPilotRunDto> StartAsync(StartV71HrPilotRequest request, CancellationToken ct)
    {
        RequirePilot();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        if (request.Cohort is null) throw new InvalidOperationException("V71_COHORT_REQUIRED");
        var min = c.GetValue("V71:CohortMin", 10); var max = c.GetValue("V71:CohortMax", 20);
        if (request.Cohort.Count < min || request.Cohort.Count > max) throw new InvalidOperationException("V71_COHORT_SIZE_OUT_OF_RANGE");
        var normalized = request.Cohort.Select(x => new V71PilotParticipantRequest(Clean(x.StaffCode), Clean(x.Persona))).ToArray();
        if (normalized.Any(x => string.IsNullOrWhiteSpace(x.StaffCode) || !Personas.Contains(x.Persona))) throw new InvalidOperationException("V71_INVALID_COHORT_ENTRY");
        if (normalized.Select(x => x.StaffCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length) throw new InvalidOperationException("V71_DUPLICATE_COHORT_STAFF_CODE");
        var missingPersona = Personas.Where(p => !normalized.Any(x => x.Persona.Equals(p, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missingPersona.Length > 0) throw new InvalidOperationException("V71_PERSONA_COVERAGE_REQUIRED:" + string.Join(',', missingPersona));

        var employeeBatch = await db.MigrationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EmployeeMigrationBatchId && x.BatchKind == "EMPLOYEE_MASTER", ct)
            ?? throw new InvalidOperationException("EMPLOYEE_MIGRATION_BATCH_NOT_FOUND");
        var entraBatch = await db.MigrationBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.EntraMigrationBatchId && x.BatchKind == "ENTRA_DIRECTORY", ct)
            ?? throw new InvalidOperationException("ENTRA_MIGRATION_BATCH_NOT_FOUND");

        var codes = normalized.Select(x => x.StaffCode).ToArray();
        var employees = await db.Employees.AsNoTracking().Where(x => codes.Contains(x.StaffCode)).ToListAsync(ct);
        if (employees.Count != codes.Length) throw new InvalidOperationException("V71_COHORT_EMPLOYEE_NOT_FOUND");
        if (employees.Any(x => !string.Equals(x.EmploymentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("V71_COHORT_MUST_BE_ACTIVE");
        if (!employees.Any(x => x.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)) || !employees.Any(x => x.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("V71_BOTH_OFFICES_REQUIRED");

        var employeeByCode = employees.ToDictionary(x => x.StaffCode, StringComparer.OrdinalIgnoreCase);
        var employeeIds = employees.Select(x => x.Id).ToArray();
        var identityIds = await db.EmployeeIdentities.AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null).Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
        if (identityIds.Count != employeeIds.Length) throw new InvalidOperationException("V71_COHORT_ACTIVE_IDENTITY_REQUIRED");

        var grantNow = DateTimeOffset.UtcNow;
        var grants = await db.AuthorizationGrants.AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) && x.RevokedAt == null && x.StartsAt <= grantNow && (x.EndsAt == null || x.EndsAt > grantNow)).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var managerIds = await db.EmployeeAssignments.AsNoTracking().Where(x => x.ManagerEmployeeId != null && employeeIds.Contains(x.ManagerEmployeeId.Value) && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).Select(x => x.ManagerEmployeeId!.Value).Distinct().ToListAsync(ct);
        foreach (var person in normalized)
        {
            var employee = employeeByCode[person.StaffCode];
            if (person.Persona == Roles.Manager && !managerIds.Contains(employee.Id)) throw new InvalidOperationException("V71_MANAGER_MUST_HAVE_DIRECT_REPORT");
            if (person.Persona == Roles.Hr || person.Persona == Roles.Payroll || person.Persona == Roles.Admin)
            {
                var has = grants.Any(g => g.EmployeeId == employee.Id && g.RoleCode.Equals(person.Persona, StringComparison.OrdinalIgnoreCase));
                if (!has) throw new InvalidOperationException("V71_PERSONA_ROLE_GRANT_REQUIRED:" + person.Persona);
            }
        }

        var canonical = string.Join('\n', normalized.OrderBy(x => x.StaffCode, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.StaffCode}|{x.Persona}"));
        var cohortSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var run = new HrPilotRun
        {
            Id = Guid.NewGuid(), EmployeeMigrationBatchId = employeeBatch.Id, EntraMigrationBatchId = entraBatch.Id,
            PopulationBaselineSha256 = c["V71:PopulationBaselineSha256"] ?? "4765b94cd3620d755d67fbab3814fe0b798ad856f201fcd0a35a8b552c2d1106",
            ExpectedPopulation = c.GetValue("Population:CurrentBaseline", 140), ExpectedHcm = c.GetValue("Population:HcmBaseline", 101), ExpectedHn = c.GetValue("Population:HnBaseline", 39),
            CohortSize = normalized.Length, CohortSha256 = cohortSha, V70RuntimeGateStatus = c["Journey:V70RuntimeGateStatus"] ?? "PENDING_EXTERNAL_EVIDENCE",
            Status = "OPEN", StartedAt = DateTimeOffset.UtcNow, StartedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        };
        db.HrPilotRuns.Add(run);
        foreach (var person in normalized)
        {
            var e = employeeByCode[person.StaffCode];
            db.HrPilotParticipants.Add(new HrPilotParticipant { Id = Guid.NewGuid(), HrPilotRunId = run.Id, EmployeeId = e.Id, StaffCode = e.StaffCode, OfficeCode = e.OfficeCode, Persona = person.Persona, CreatedAt = run.StartedAt });
        }
        AddCheck(run.Id, "COHORT_READY", "PASS", $"{run.CohortSize} active mapped users; HCM/HN and five required personas covered.");
        AddCheck(run.Id, "SOURCE_BOUNDARY", "PASS", "140-person Contact List remains population reference only; authoritative HR fields must come from approved HR Master source.");
        audit.Record("V71_HR_PILOT_STARTED", "HrPilotRun", run.Id.ToString(), new { run.EmployeeMigrationBatchId, run.EntraMigrationBatchId, run.CohortSize, run.CohortSha256, request.Reason });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<V71HrPilotRunDto> RecordScenarioAsync(Guid runId, RecordV71ScenarioRequest request, CancellationToken ct)
    {
        RequirePilot();
        var run = await db.HrPilotRuns.SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V71_RUN_NOT_FOUND");
        if (run.Status == "COMPLETED") throw new InvalidOperationException("V71_RUN_ALREADY_COMPLETED");
        var code = Clean(request.ScenarioCode); var persona = Clean(request.Persona); var staff = Clean(request.StaffCode); var status = Clean(request.Status);
        if (!RequiredScenarios.TryGetValue(code, out var requiredPersona)) throw new InvalidOperationException("V71_UNKNOWN_SCENARIO");
        if (!requiredPersona.Equals("ANY", StringComparison.OrdinalIgnoreCase) && !persona.Equals(requiredPersona, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V71_SCENARIO_PERSONA_MISMATCH");
        if (!Personas.Contains(persona)) throw new InvalidOperationException("V71_INVALID_SCENARIO_PERSONA");
        if (status is not ("PASS" or "FAIL" or "BLOCKED")) throw new InvalidOperationException("V71_INVALID_SCENARIO_STATUS");
        if (string.IsNullOrWhiteSpace(request.Summary)) throw new InvalidOperationException("V71_SCENARIO_SUMMARY_REQUIRED");
        if (status == "PASS" && string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V71_PASS_EVIDENCE_REFERENCE_REQUIRED");
        var participant = await db.HrPilotParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.HrPilotRunId == runId && x.StaffCode == staff && x.Persona == persona, ct)
            ?? throw new InvalidOperationException("V71_SCENARIO_PARTICIPANT_NOT_IN_COHORT");
        if (code == "HCM_OFFICE_PATH" && !participant.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V71_HCM_SCENARIO_REQUIRES_HCM_PARTICIPANT");
        if (code == "HN_OFFICE_PATH" && !participant.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V71_HN_SCENARIO_REQUIRES_HN_PARTICIPANT");
        db.HrPilotScenarioEvidence.Add(new HrPilotScenarioEvidence
        {
            Id = Guid.NewGuid(), HrPilotRunId = runId, ScenarioCode = code, Persona = persona, StaffCode = participant.StaffCode,
            Status = status, Summary = request.Summary.Trim()[..Math.Min(1000, request.Summary.Trim().Length)], EvidenceReference = TrimNullable(request.EvidenceReference, 500),
            RecordedAt = DateTimeOffset.UtcNow, RecordedBy = current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED"
        });
        audit.Record("V71_HR_PILOT_SCENARIO_RECORDED", "HrPilotRun", runId.ToString(), new { code, persona, staff, status });
        await db.SaveChangesAsync(ct);
        return await GetAsync(runId, ct);
    }

    public async Task<V71HrPilotRunDto> EvaluateAsync(Guid runId, CancellationToken ct)
    {
        RequirePilot();
        var run = await db.HrPilotRuns.SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V71_RUN_NOT_FOUND");
        if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        var now = DateTimeOffset.UtcNow;
        var employeeBatch = await db.MigrationBatches.AsNoTracking().SingleAsync(x => x.Id == run.EmployeeMigrationBatchId, ct);
        var entraBatch = await db.MigrationBatches.AsNoTracking().SingleAsync(x => x.Id == run.EntraMigrationBatchId, ct);
        AddCheck(run.Id, "V70_RUNTIME_GATE", run.V70RuntimeGateStatus.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? "PASS" : "WARN",
            run.V70RuntimeGateStatus.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? "V70 runtime gate evidence recorded as PASS." : "V70 runtime evidence remains external/PENDING and is carried forward; V71 package work may continue but production proof is not closed.");
        AddCheck(run.Id, "HR_MASTER_BATCH_COMMITTED", employeeBatch.Status == "COMMITTED" ? "PASS" : "FAIL", $"Employee Master batch status={employeeBatch.Status}.");
        var committedRows = await db.MigrationRows.AsNoTracking().CountAsync(x => x.BatchId == run.EmployeeMigrationBatchId && x.Status == "COMMITTED", ct);
        AddCheck(run.Id, "HR_MASTER_140_ROWS", committedRows == run.ExpectedPopulation ? "PASS" : "FAIL", $"Committed Employee Master rows={committedRows}; expected current baseline={run.ExpectedPopulation}.");
        var batchEmployeeIds = await db.MigrationRows.AsNoTracking().Where(x => x.BatchId == run.EmployeeMigrationBatchId && x.EmployeeId != null).Select(x => x.EmployeeId!.Value).Distinct().ToListAsync(ct);
        var officeCounts = await db.Employees.AsNoTracking().Where(x => batchEmployeeIds.Contains(x.Id)).GroupBy(x => x.OfficeCode).Select(g => new { Office = g.Key, Count = g.Count() }).ToListAsync(ct);
        var hcm = officeCounts.FirstOrDefault(x => x.Office == "HCM")?.Count ?? 0; var hn = officeCounts.FirstOrDefault(x => x.Office == "HN")?.Count ?? 0;
        AddCheck(run.Id, "OFFICE_POPULATION_RECONCILIATION", hcm == run.ExpectedHcm && hn == run.ExpectedHn ? "PASS" : "FAIL", $"HCM={hcm}/{run.ExpectedHcm}; HN={hn}/{run.ExpectedHn}.");
        AddCheck(run.Id, "ENTRA_BATCH_RESOLVED", entraBatch.Status == "REVIEW" ? "FAIL" : entraBatch.Status == "COMMITTED" ? "PASS" : "WARN", $"Entra mapping batch status={entraBatch.Status}.");
        var participantIds = await db.HrPilotParticipants.AsNoTracking().Where(x => x.HrPilotRunId == runId).Select(x => x.EmployeeId).ToListAsync(ct);
        var mapped = await db.EmployeeIdentities.AsNoTracking().Where(x => participantIds.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        AddCheck(run.Id, "COHORT_ACTIVE_IDENTITIES", mapped == participantIds.Count ? "PASS" : "FAIL", $"Active identity links={mapped}/{participantIds.Count} cohort users.");
        var scenarioRows = await db.HrPilotScenarioEvidence.AsNoTracking().Where(x => x.HrPilotRunId == runId).OrderBy(x => x.RecordedAt).ToListAsync(ct);
        var latest = scenarioRows.GroupBy(x => x.ScenarioCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var scenarioFailures = new List<string>(); var scenarioMissing = new List<string>();
        foreach (var required in RequiredScenarios.Keys)
        {
            if (!latest.TryGetValue(required, out var ev)) scenarioMissing.Add(required);
            else if (ev.Status != "PASS") scenarioFailures.Add($"{required}:{ev.Status}");
        }
        var scenarioStatus = scenarioFailures.Count > 0 ? "FAIL" : scenarioMissing.Count > 0 ? "WARN" : "PASS";
        AddCheck(run.Id, "REQUIRED_UAT_SCENARIOS", scenarioStatus, scenarioStatus == "PASS" ? $"All {RequiredScenarios.Count} required V71 scenarios have latest PASS evidence." : $"Missing={string.Join('|', scenarioMissing)}; non-pass={string.Join('|', scenarioFailures)}");
        var prodLive = c.GetValue<bool>("Product:ProductionLive");
        AddCheck(run.Id, "PRODUCTION_LIVE_FALSE", !prodLive ? "PASS" : "FAIL", $"Product.ProductionLive={prodLive}.");
        var boundaryOk = string.Equals(c["Payroll:OfficialResultSource"], "BRAVO", StringComparison.OrdinalIgnoreCase) && !c.GetValue<bool>("Payroll:ShadowEngineEnabled");
        AddCheck(run.Id, "PAYROLL_BOUNDARY_PRESERVED", boundaryOk ? "PASS" : "FAIL", "BRAVO remains official Phase 1-2 and Shadow remains validation-only.");
        await db.SaveChangesAsync(ct);
        var currentChecks = await db.HrPilotChecks.AsNoTracking().Where(x => x.HrPilotRunId == runId && x.CheckedAt >= now).ToListAsync(ct);
        run.Status = currentChecks.Any(x => x.Status == "FAIL") ? "FAIL" : currentChecks.Any(x => x.Status == "WARN") ? "WARN" : "PASS";
        audit.Record("V71_HR_PILOT_EVALUATED", "HrPilotRun", runId.ToString(), new { run.Status });
        await db.SaveChangesAsync(ct);
        return await GetAsync(runId, ct);
    }

    public async Task<V71HrPilotRunDto> CompleteAsync(Guid runId, CompleteV71HrPilotRequest request, CancellationToken ct)
    {
        RequirePilot();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var run = await db.HrPilotRuns.SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V71_RUN_NOT_FOUND");
        if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        if (run.Status is "OPEN" or "FAIL") throw new InvalidOperationException("V71_RUN_NOT_COMPLETABLE");
        var latestChecks = (await db.HrPilotChecks.AsNoTracking().Where(x => x.HrPilotRunId == runId).OrderBy(x => x.CheckedAt).ToListAsync(ct))
            .GroupBy(x => x.CheckCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var mandatoryPass = new[] { "HR_MASTER_BATCH_COMMITTED", "HR_MASTER_140_ROWS", "OFFICE_POPULATION_RECONCILIATION", "COHORT_ACTIVE_IDENTITIES", "REQUIRED_UAT_SCENARIOS", "PRODUCTION_LIVE_FALSE", "PAYROLL_BOUNDARY_PRESERVED" };
        if (mandatoryPass.Any(code => !latestChecks.TryGetValue(code, out var check) || check.Status != "PASS")) throw new InvalidOperationException("V71_MANDATORY_PILOT_CHECKS_NOT_PASS");
        run.Status = "COMPLETED"; run.CompletedAt = DateTimeOffset.UtcNow; run.CompletionNote = request.Reason.Trim()[..Math.Min(1000, request.Reason.Trim().Length)];
        audit.Record("V71_HR_PILOT_COMPLETED", "HrPilotRun", runId.ToString(), new { priorGate = run.V70RuntimeGateStatus, request.Reason });
        await db.SaveChangesAsync(ct);
        return await GetAsync(runId, ct);
    }

    public async Task<V71HrPilotRunDto> GetAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.HrPilotRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V71_RUN_NOT_FOUND");
        var checks = await db.HrPilotChecks.AsNoTracking().Where(x => x.HrPilotRunId == runId).OrderBy(x => x.CheckedAt).Select(x => new V71HrPilotCheckDto(x.CheckCode, x.Status, x.Summary, x.CheckedAt)).ToListAsync(ct);
        var scenarios = await db.HrPilotScenarioEvidence.AsNoTracking().Where(x => x.HrPilotRunId == runId).OrderBy(x => x.RecordedAt).Select(x => new V71HrPilotScenarioDto(x.ScenarioCode, x.Persona, x.StaffCode, x.Status, x.Summary, x.EvidenceReference, x.RecordedAt)).ToListAsync(ct);
        return new V71HrPilotRunDto(run.Id, run.EmployeeMigrationBatchId, run.EntraMigrationBatchId, run.PopulationBaselineSha256, run.ExpectedPopulation, run.ExpectedHcm, run.ExpectedHn, run.CohortSize, run.CohortSha256, run.V70RuntimeGateStatus, run.Status, run.StartedAt, run.CompletedAt, run.CompletionNote, checks, scenarios);
    }

    private void AddCheck(Guid runId, string code, string status, string summary) => db.HrPilotChecks.Add(new HrPilotCheck
    {
        Id = Guid.NewGuid(), HrPilotRunId = runId, CheckCode = code, Status = status, Summary = summary, CheckedAt = DateTimeOffset.UtcNow,
        CheckedBy = current.StaffCode ?? current.EntraObjectId ?? "SYSTEM"
    });
    private void RequirePilot() { if (!c.GetValue<bool>("Pilot:Enabled")) throw new InvalidOperationException("PILOT_DISABLED"); }
    private static string Clean(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string? TrimNullable(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim(); return v[..Math.Min(max, v.Length)]; }
}
