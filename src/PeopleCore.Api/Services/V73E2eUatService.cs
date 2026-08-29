using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Services;

public sealed class V73E2eUatService(PeopleCoreDbContext db, IConfiguration c, ICurrentUser current, IAuditService audit, FunctionalEvidenceService functionalEvidence)
{
    private sealed record ScenarioSpec(string Domain, string Persona);
    private static readonly Regex ShaRx = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly Regex CorrelationRx = new("^[A-Za-z0-9._:-]{6,128}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedPersonas = new(StringComparer.OrdinalIgnoreCase) { Roles.Employee, Roles.Manager, Roles.Hr, Roles.Payroll, Roles.Admin, Roles.Leadership };
    private static readonly HashSet<string> RequiredPersonas = new(StringComparer.OrdinalIgnoreCase) { Roles.Employee, Roles.Manager, Roles.Hr, Roles.Payroll, Roles.Admin };
    private static readonly HashSet<string> Severities = new(StringComparer.OrdinalIgnoreCase) { "P0", "P1", "P2", "P3" };
    private static readonly HashSet<string> DefectDispositions = new(StringComparer.OrdinalIgnoreCase) { "FIXED_VERIFIED", "ACCEPTED_RISK", "DEFERRED_TO_POST_LIVE" };
    private static readonly HashSet<string> CriticalDomains = new(StringComparer.OrdinalIgnoreCase) { "SECURITY", "PRIVACY", "IDENTITY", "COMPENSATION", "PAYROLL", "PAYSLIP", "TAX_INSURANCE" };
    private static readonly HashSet<string> MachineFunctionalScenarios = new(StringComparer.OrdinalIgnoreCase)
    {
        "V73-HR-03", "V73-LEAVE-01", "V73-ATT-01", "V73-OT-01", "V73-TIME-01", "V73-TIME-02", "V73-TIME-03",
        "V73-PERF-01", "V73-PERF-02", "V73-PAYS-01", "V73-PAYS-02", "V73-TAX-01"
    };
    private static readonly IReadOnlyDictionary<string, string> SignoffRoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HR_OWNER"] = Roles.Hr,
        ["PAYROLL_OWNER"] = Roles.Payroll,
        ["TECHNICAL_OWNER"] = Roles.Admin,
        ["BUSINESS_OWNER"] = Roles.Leadership
    };

    public static readonly IReadOnlyDictionary<string, (string Domain, string Persona)> RequiredScenarios =
        new Dictionary<string, (string Domain, string Persona)>(StringComparer.OrdinalIgnoreCase)
        {
            ["V73-ID-01"] = ("IDENTITY", Roles.Employee),
            ["V73-ID-02"] = ("IDENTITY", Roles.Admin),
            ["V73-AUTH-01"] = ("PRIVACY", Roles.Employee),
            ["V73-AUTH-02"] = ("SECURITY", Roles.Manager),
            ["V73-AUTH-03"] = ("PRIVACY", Roles.Hr),
            ["V73-AUTH-04"] = ("PRIVACY", Roles.Payroll),
            ["V73-HR-01"] = ("HR_CORE", Roles.Hr),
            ["V73-HR-02"] = ("HR_CORE", Roles.Hr),
            ["V73-HR-03"] = ("CONTRACT", Roles.Hr),
            ["V73-HR-04"] = ("HR_CORE", Roles.Hr),
            ["V73-LEAVE-01"] = ("LEAVE", Roles.Employee),
            ["V73-ATT-01"] = ("ATTENDANCE", Roles.Hr),
            ["V73-OT-01"] = ("OT", Roles.Manager),
            ["V73-TIME-01"] = ("TIMESHEET", Roles.Employee),
            ["V73-TIME-02"] = ("TIMESHEET", Roles.Employee),
            ["V73-TIME-03"] = ("TIMESHEET", Roles.Hr),
            ["V73-PERF-01"] = ("PERFORMANCE", Roles.Employee),
            ["V73-PERF-02"] = ("PERFORMANCE", Roles.Manager),
            ["V73-COMP-01"] = ("COMPENSATION", Roles.Hr),
            ["V73-BRAVO-01"] = ("INTEGRATION", Roles.Payroll),
            ["V73-BRAVO-02"] = ("INTEGRATION", Roles.Payroll),
            ["V73-PAY-01"] = ("PAYROLL", Roles.Payroll),
            ["V73-PAY-02"] = ("PAYROLL", Roles.Payroll),
            ["V73-PAYS-01"] = ("PAYSLIP", Roles.Payroll),
            ["V73-PAYS-02"] = ("PAYSLIP", Roles.Payroll),
            ["V73-TAX-01"] = ("TAX_INSURANCE", Roles.Payroll),
            ["V73-SEC-01"] = ("SECURITY", Roles.Admin),
            ["V73-SEC-02"] = ("SECURITY", Roles.Admin),
            ["V73-DR-01"] = ("OPERATIONS", Roles.Admin),
            ["V73-UX-01"] = ("UI_UX", Roles.Employee)
        };

    public async Task<V73E2eUatRunDto> StartAsync(StartV73E2eUatRunRequest request, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator();
        var releaseCandidate = CleanText(request.ReleaseCandidate, 120);
        if (string.IsNullOrWhiteSpace(releaseCandidate) || string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("V73_REQUIRED_FIELDS_MISSING");
        var baselineSha = (request.PopulationBaselineSha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (!ShaRx.IsMatch(baselineSha)) throw new InvalidOperationException("V73_POPULATION_SHA256_REQUIRED");
        var configuredSha = (c["V71:PopulationBaselineSha256"] ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configuredSha) && !string.Equals(configuredSha, baselineSha, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_POPULATION_BASELINE_HASH_MISMATCH");
        var v70EvidenceSha = (request.V70RuntimeEvidenceSha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (!ShaRx.IsMatch(v70EvidenceSha)) throw new InvalidOperationException("V73_V70_EVIDENCE_SHA256_REQUIRED");
        var pinnedHrRun = await db.HrPilotRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.HrPilotRunId, ct) ?? throw new InvalidOperationException("V73_HR_PILOT_RUN_NOT_FOUND");
        var pinnedPayrollRun = await db.PayrollParallelRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PayrollParallelRunId, ct) ?? throw new InvalidOperationException("V73_PAYROLL_PARALLEL_RUN_NOT_FOUND");
        if (request.Participants is null) throw new InvalidOperationException("V73_PARTICIPANTS_REQUIRED");
        var min = c.GetValue("V73:TesterCohortMin", 20); var max = c.GetValue("V73:TesterCohortMax", 40);
        if (request.Participants.Count < min || request.Participants.Count > max) throw new InvalidOperationException("V73_TESTER_COHORT_SIZE_OUT_OF_RANGE");
        var participants = request.Participants.Select(x => new V73UatParticipantRequest(Clean(x.StaffCode), Clean(x.Persona))).ToArray();
        if (participants.Any(x => string.IsNullOrWhiteSpace(x.StaffCode) || !AllowedPersonas.Contains(x.Persona))) throw new InvalidOperationException("V73_INVALID_PARTICIPANT");
        if (participants.Select(x => x.StaffCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != participants.Length) throw new InvalidOperationException("V73_DUPLICATE_PARTICIPANT_STAFF_CODE");
        var missingPersonas = RequiredPersonas.Where(p => !participants.Any(x => x.Persona.Equals(p, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missingPersonas.Length > 0) throw new InvalidOperationException("V73_PERSONA_COVERAGE_REQUIRED:" + string.Join(',', missingPersonas));

        var codes = participants.Select(x => x.StaffCode).ToArray();
        var employees = await db.Employees.AsNoTracking().Where(x => codes.Contains(x.StaffCode) && x.EmploymentStatus == "ACTIVE").ToListAsync(ct);
        if (employees.Count != codes.Length) throw new InvalidOperationException("V73_PARTICIPANT_NOT_ACTIVE_OR_NOT_FOUND");
        if (!employees.Any(x => x.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)) || !employees.Any(x => x.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("V73_BOTH_OFFICES_REQUIRED");
        var ids = employees.Select(x => x.Id).ToArray();
        var linked = await db.EmployeeIdentities.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null).Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
        if (linked.Count != ids.Length) throw new InvalidOperationException("V73_ACTIVE_ENTRA_IDENTITY_REQUIRED");

        var byCode = employees.ToDictionary(x => x.StaffCode, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow; var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var grants = await db.AuthorizationGrants.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.RevokedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)).ToListAsync(ct);
        var managerIds = await db.EmployeeAssignments.AsNoTracking().Where(x => x.ManagerEmployeeId != null && ids.Contains(x.ManagerEmployeeId.Value) && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).Select(x => x.ManagerEmployeeId!.Value).Distinct().ToListAsync(ct);
        foreach (var p in participants)
        {
            var employee = byCode[p.StaffCode];
            if (p.Persona == Roles.Manager && !managerIds.Contains(employee.Id)) throw new InvalidOperationException("V73_MANAGER_MUST_HAVE_DIRECT_REPORT");
            if (p.Persona is Roles.Hr or Roles.Payroll or Roles.Admin or Roles.Leadership)
            {
                if (!grants.Any(g => g.EmployeeId == employee.Id && g.RoleCode.Equals(p.Persona, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("V73_PERSONA_ROLE_GRANT_REQUIRED:" + p.Persona);
            }
        }

        var run = new E2eUatRun
        {
            Id = Guid.NewGuid(), ReleaseCandidate = releaseCandidate, PopulationBaselineSha256 = baselineSha, V70RuntimeEvidenceSha256 = v70EvidenceSha, HrPilotRunId = pinnedHrRun.Id, PayrollParallelRunId = pinnedPayrollRun.Id,
            ExpectedPopulation = c.GetValue("Population:CurrentBaseline", 140), ExpectedHcm = c.GetValue("Population:HcmBaseline", 101), ExpectedHn = c.GetValue("Population:HnBaseline", 39),
            TesterCohortSize = participants.Length,
            V70RuntimeGateStatus = c["Journey:V70RuntimeGateStatus"] ?? "PENDING_EXTERNAL_EVIDENCE",
            V71HrPilotGateStatus = pinnedHrRun.Status == "COMPLETED" ? "PASS" : pinnedHrRun.Status,
            V72PayrollParallelGateStatus = pinnedPayrollRun.Status == "COMPLETED" ? "PASS" : pinnedPayrollRun.Status,
            Status = "OPEN", StartedAt = now, StartedBy = Actor
        };
        db.E2eUatRuns.Add(run);
        foreach (var p in participants)
        {
            var employee = byCode[p.StaffCode];
            db.E2eUatParticipants.Add(new E2eUatParticipant { Id = Guid.NewGuid(), E2eUatRunId = run.Id, EmployeeId = employee.Id, StaffCode = employee.StaffCode, OfficeCode = employee.OfficeCode, Persona = p.Persona, CreatedAt = now });
        }
        AddCheck(run.Id, "V73_PACKAGE_BOUNDARY", "PASS", "V73 is E2E UAT evidence orchestration only; it does not authorize Production Live.", now);
        audit.Record("V73_E2E_UAT_STARTED", "E2eUatRun", run.Id.ToString(), new { run.ReleaseCandidate, run.ExpectedPopulation, run.TesterCohortSize, request.Reason, productionLive = false });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<V73E2eUatRunDto> RecordScenarioAsync(Guid runId, RecordV73ScenarioEvidenceRequest request, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator();
        var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") throw new InvalidOperationException("V73_RUN_COMPLETED");
        var code = Clean(request.ScenarioCode); var persona = Clean(request.Persona); var staff = Clean(request.StaffCode); var status = Clean(request.Status);
        if (!RequiredScenarios.TryGetValue(code, out var spec)) throw new InvalidOperationException("V73_UNKNOWN_SCENARIO");
        if (!persona.Equals(spec.Persona, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_SCENARIO_PERSONA_MISMATCH");
        if (status is not ("PASS" or "FAIL" or "BLOCKED")) throw new InvalidOperationException("V73_INVALID_SCENARIO_STATUS");
        if (string.IsNullOrWhiteSpace(request.Summary) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V73_SCENARIO_EVIDENCE_REQUIRED");
        if (!string.IsNullOrWhiteSpace(request.CorrelationId) && !CorrelationRx.IsMatch(request.CorrelationId.Trim())) throw new InvalidOperationException("V73_INVALID_CORRELATION_ID");
        var participant = await db.E2eUatParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.E2eUatRunId == runId && x.StaffCode == staff && x.Persona == persona, ct) ?? throw new InvalidOperationException("V73_SCENARIO_PARTICIPANT_NOT_IN_COHORT");
        var evidenceReference = CleanText(request.EvidenceReference, 600);
        if (await db.E2eUatScenarioEvidence.AsNoTracking().AnyAsync(x => x.E2eUatRunId == runId && x.EvidenceReference == evidenceReference && x.ScenarioCode != code, ct))
            throw new InvalidOperationException("V73_EVIDENCE_REFERENCE_ALREADY_USED_FOR_DIFFERENT_SCENARIO");
        await VerifyScenarioEvidenceAsync(code, status, evidenceReference, participant.EmployeeId, run.StartedAt, ct);
        db.E2eUatScenarioEvidence.Add(new E2eUatScenarioEvidence
        {
            Id = Guid.NewGuid(), E2eUatRunId = runId, ScenarioCode = code, Domain = spec.Domain, Persona = persona, StaffCode = participant.StaffCode,
            Status = status, Summary = CleanText(request.Summary, 1500), EvidenceReference = evidenceReference, CorrelationId = TrimNullable(request.CorrelationId, 128), RecordedAt = DateTimeOffset.UtcNow, RecordedBy = Actor
        });
        audit.Record("V73_SCENARIO_EVIDENCE_RECORDED", "E2eUatRun", runId.ToString(), new { code, spec.Domain, persona, staff, status });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<V73UatDefectDto> RaiseDefectAsync(Guid runId, RaiseV73DefectRequest request, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator(); var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") throw new InvalidOperationException("V73_RUN_COMPLETED");
        var code = Clean(request.DefectCode); var severity = Clean(request.Severity); var domain = Clean(request.Domain);
        if (string.IsNullOrWhiteSpace(code) || code.Length > 80 || !Severities.Contains(severity) || string.IsNullOrWhiteSpace(domain)) throw new InvalidOperationException("V73_INVALID_DEFECT");
        if (string.IsNullOrWhiteSpace(request.Summary) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V73_DEFECT_EVIDENCE_REQUIRED");
        if (await db.E2eUatDefects.AnyAsync(x => x.E2eUatRunId == runId && x.DefectCode == code, ct)) throw new InvalidOperationException("V73_DEFECT_ALREADY_EXISTS");
        var defectReference = CleanText(request.EvidenceReference, 600);
        await RequireArtifactAsync(defectReference, $"V73-DEFECT-{code}", null, run.StartedAt, ct);
        var defect = new E2eUatDefect { Id = Guid.NewGuid(), E2eUatRunId = runId, DefectCode = code, Severity = severity, Domain = domain, Summary = CleanText(request.Summary, 2000), EvidenceReference = defectReference, RaisedAt = DateTimeOffset.UtcNow, RaisedBy = Actor };
        db.E2eUatDefects.Add(defect); audit.Record("V73_DEFECT_RAISED", "E2eUatDefect", defect.Id.ToString(), new { runId, code, severity, domain }); await db.SaveChangesAsync(ct); return ToDefectDto(defect, null);
    }

    public async Task<V73UatDefectDto> ResolveDefectAsync(Guid runId, Guid defectId, ResolveV73DefectRequest request, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator(); var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") throw new InvalidOperationException("V73_RUN_COMPLETED");
        var defect = await db.E2eUatDefects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == defectId && x.E2eUatRunId == runId, ct) ?? throw new InvalidOperationException("V73_DEFECT_NOT_FOUND");
        var disposition = Clean(request.Disposition); if (!DefectDispositions.Contains(disposition)) throw new InvalidOperationException("V73_INVALID_DEFECT_DISPOSITION");
        if (string.IsNullOrWhiteSpace(request.ResolutionNote) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V73_DEFECT_RESOLUTION_EVIDENCE_REQUIRED");
        if ((defect.Severity is "P0" or "P1" || CriticalDomains.Contains(defect.Domain)) && disposition != "FIXED_VERIFIED") throw new InvalidOperationException("V73_CRITICAL_DEFECT_MUST_BE_FIXED_VERIFIED");
        var resolutionReference = CleanText(request.EvidenceReference, 600);
        await RequireArtifactAsync(resolutionReference, $"V73-DEFECT-RESOLUTION-{defect.DefectCode}", disposition == "FIXED_VERIFIED" ? "PASS" : null, defect.RaisedAt, ct);
        var resolution = new E2eUatDefectResolution { Id = Guid.NewGuid(), E2eUatDefectId = defect.Id, Disposition = disposition, ResolutionNote = CleanText(request.ResolutionNote, 2000), EvidenceReference = resolutionReference, ResolvedAt = DateTimeOffset.UtcNow, ResolvedBy = Actor };
        db.E2eUatDefectResolutions.Add(resolution); audit.Record("V73_DEFECT_RESOLUTION_RECORDED", "E2eUatDefect", defect.Id.ToString(), new { disposition }); await db.SaveChangesAsync(ct); return ToDefectDto(defect, resolution);
    }

    public async Task<V73UatSignoffDto> RecordSignoffAsync(Guid runId, RecordV73SignoffRequest request, CancellationToken ct)
    {
        RequirePilot(); var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") throw new InvalidOperationException("V73_RUN_COMPLETED");
        var role = Clean(request.SignoffRole); var decision = Clean(request.Decision);
        if (!SignoffRoleMap.TryGetValue(role, out var requiredRole)) throw new InvalidOperationException("V73_UNKNOWN_SIGNOFF_ROLE");
        if (!current.IsInRole(requiredRole)) throw new InvalidOperationException("V73_SIGNOFF_ROLE_MISMATCH");
        if (decision is not ("APPROVE" or "REJECT")) throw new InvalidOperationException("V73_INVALID_SIGNOFF_DECISION");
        if (string.IsNullOrWhiteSpace(request.Approver) || string.IsNullOrWhiteSpace(request.EvidenceReference) || string.IsNullOrWhiteSpace(request.Note)) throw new InvalidOperationException("V73_SIGNOFF_EVIDENCE_REQUIRED");
        var signoffReference = CleanText(request.EvidenceReference, 600);
        await RequireArtifactAsync(signoffReference, $"V73-SIGNOFF-{role}", decision == "APPROVE" ? "PASS" : "FAIL", run.StartedAt, ct);
        var signoff = new E2eUatSignoff { Id = Guid.NewGuid(), E2eUatRunId = runId, SignoffRole = role, Decision = decision, Approver = CleanText(request.Approver, 200), EvidenceReference = signoffReference, Note = CleanText(request.Note, 1500), SignedAt = DateTimeOffset.UtcNow, SignedBy = Actor };
        db.E2eUatSignoffs.Add(signoff); audit.Record("V73_SIGNOFF_RECORDED", "E2eUatRun", runId.ToString(), new { role, decision, signoff.Approver }); await db.SaveChangesAsync(ct); return ToSignoffDto(signoff);
    }

    public async Task<V73E2eUatRunDto> EvaluateAsync(Guid runId, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator(); var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        var now = DateTimeOffset.UtcNow;
        run.V70RuntimeGateStatus = c["Journey:V70RuntimeGateStatus"] ?? run.V70RuntimeGateStatus;
        var pinnedHrRun = await db.HrPilotRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == run.HrPilotRunId, ct);
        var pinnedPayrollRun = await db.PayrollParallelRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == run.PayrollParallelRunId, ct);
        run.V71HrPilotGateStatus = pinnedHrRun?.Status == "COMPLETED" ? "PASS" : pinnedHrRun?.Status ?? "MISSING";
        run.V72PayrollParallelGateStatus = pinnedPayrollRun?.Status == "COMPLETED" ? "PASS" : pinnedPayrollRun?.Status ?? "MISSING";
        AddCheck(run.Id, "V70_RUNTIME_GATE_PASS", IsPass(run.V70RuntimeGateStatus) ? "PASS" : "WARN", $"V70={run.V70RuntimeGateStatus}.", now);
        AddCheck(run.Id, "V71_HR_PILOT_GATE_PASS", IsPass(run.V71HrPilotGateStatus) ? "PASS" : "WARN", $"V71={run.V71HrPilotGateStatus}.", now);
        AddCheck(run.Id, "V72_PARALLEL_RUN_GATE_PASS", IsPass(run.V72PayrollParallelGateStatus) ? "PASS" : "WARN", $"V72={run.V72PayrollParallelGateStatus}.", now);

        var active = await db.Employees.AsNoTracking().Where(x => x.EmploymentStatus == "ACTIVE").ToListAsync(ct);
        var hcm = active.Count(x => x.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)); var hn = active.Count(x => x.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase));
        AddCheck(run.Id, "ACTIVE_POPULATION_140", active.Count == run.ExpectedPopulation ? "PASS" : "FAIL", $"Active={active.Count}/{run.ExpectedPopulation}.", now);
        AddCheck(run.Id, "OFFICE_SPLIT_101_39", hcm == run.ExpectedHcm && hn == run.ExpectedHn ? "PASS" : "FAIL", $"HCM={hcm}/{run.ExpectedHcm}; HN={hn}/{run.ExpectedHn}.", now);
        var expectedSha = (c["V71:PopulationBaselineSha256"] ?? string.Empty).Trim().ToLowerInvariant();
        AddCheck(run.Id, "POPULATION_BASELINE_HASH", !string.IsNullOrWhiteSpace(expectedSha) && run.PopulationBaselineSha256 == expectedSha ? "PASS" : "FAIL", $"Baseline SHA locked={run.PopulationBaselineSha256}.", now);
        var activeIds = active.Select(x => x.Id).ToArray();
        var identityCount = activeIds.Length == 0 ? 0 : await db.EmployeeIdentities.AsNoTracking().Where(x => activeIds.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        AddCheck(run.Id, "ACTIVE_ENTRA_IDENTITY_140", identityCount == run.ExpectedPopulation ? "PASS" : "FAIL", $"Active Entra mappings={identityCount}/{run.ExpectedPopulation}.", now);

        var participants = await db.E2eUatParticipants.AsNoTracking().Where(x => x.E2eUatRunId == runId).ToListAsync(ct);
        var min = c.GetValue("V73:TesterCohortMin", 20); var max = c.GetValue("V73:TesterCohortMax", 40);
        var personaOk = RequiredPersonas.All(p => participants.Any(x => x.Persona.Equals(p, StringComparison.OrdinalIgnoreCase)));
        var officeOk = participants.Any(x => x.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)) && participants.Any(x => x.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase));
        AddCheck(run.Id, "TESTER_COHORT_READY", participants.Count >= min && participants.Count <= max && personaOk && officeOk ? "PASS" : "FAIL", $"Cohort={participants.Count}; required={min}-{max}; personas={personaOk}; offices={officeOk}.", now);

        var scenarioRows = await db.E2eUatScenarioEvidence.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.RecordedAt).ToListAsync(ct);
        var latestScenario = scenarioRows.GroupBy(x => x.ScenarioCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var missing = RequiredScenarios.Keys.Where(k => !latestScenario.ContainsKey(k)).ToArray();
        var nonPass = RequiredScenarios.Keys.Where(k => latestScenario.TryGetValue(k, out var e) && e.Status != "PASS").Select(k => $"{k}:{latestScenario[k].Status}").ToArray();
        AddCheck(run.Id, "REQUIRED_30_SCENARIOS_PASS", missing.Length == 0 && nonPass.Length == 0 ? "PASS" : nonPass.Length > 0 ? "FAIL" : "WARN", missing.Length == 0 && nonPass.Length == 0 ? "All 30 mandatory E2E scenarios have latest PASS evidence." : $"Missing={string.Join('|', missing)}; non-pass={string.Join('|', nonPass)}", now);

        var defects = await db.E2eUatDefects.AsNoTracking().Where(x => x.E2eUatRunId == runId).ToListAsync(ct);
        var defectIds = defects.Select(x => x.Id).ToArray();
        var resolutions = defectIds.Length == 0 ? [] : await db.E2eUatDefectResolutions.AsNoTracking().Where(x => defectIds.Contains(x.E2eUatDefectId)).OrderBy(x => x.ResolvedAt).ToListAsync(ct);
        var latestResolution = resolutions.GroupBy(x => x.E2eUatDefectId).ToDictionary(g => g.Key, g => g.Last());
        var unresolved = defects.Where(x => !latestResolution.ContainsKey(x.Id)).ToArray();
        var p01NotFixed = defects.Where(x => x.Severity is "P0" or "P1").Where(x => !latestResolution.TryGetValue(x.Id, out var r) || r.Disposition != "FIXED_VERIFIED").ToArray();
        var criticalNotFixed = defects.Where(x => CriticalDomains.Contains(x.Domain)).Where(x => !latestResolution.TryGetValue(x.Id, out var r) || r.Disposition != "FIXED_VERIFIED").ToArray();
        AddCheck(run.Id, "ALL_DEFECTS_DISPOSITIONED", unresolved.Length == 0 ? "PASS" : "FAIL", $"Defects={defects.Count}; unresolved={unresolved.Length}.", now);
        AddCheck(run.Id, "P0_P1_DEFECTS_FIXED", p01NotFixed.Length == 0 ? "PASS" : "FAIL", $"P0/P1 not FIXED_VERIFIED={p01NotFixed.Length}.", now);
        AddCheck(run.Id, "CRITICAL_DOMAIN_DEFECTS_FIXED", criticalNotFixed.Length == 0 ? "PASS" : "FAIL", $"Security/privacy/identity/compensation/payroll/payslip/tax-insurance defects not FIXED_VERIFIED={criticalNotFixed.Length}.", now);

        var signoffs = await db.E2eUatSignoffs.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.SignedAt).ToListAsync(ct);
        var latestSignoff = signoffs.GroupBy(x => x.SignoffRole, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var requiredSignoffs = SignoffRoleMap.Keys.ToArray();
        var approvalsOk = requiredSignoffs.All(role => latestSignoff.TryGetValue(role, out var s) && s.Decision == "APPROVE");
        var latestEvidenceAt = scenarioRows.Count == 0 ? run.StartedAt : scenarioRows.Max(x => x.RecordedAt);
        if (resolutions.Count > 0 && resolutions.Max(x => x.ResolvedAt) > latestEvidenceAt) latestEvidenceAt = resolutions.Max(x => x.ResolvedAt);
        var freshSignoffs = approvalsOk && requiredSignoffs.All(role => latestSignoff[role].SignedAt >= latestEvidenceAt);
        AddCheck(run.Id, "REQUIRED_SIGNOFFS_APPROVED", approvalsOk ? "PASS" : "FAIL", "Required roles: HR_OWNER, PAYROLL_OWNER, TECHNICAL_OWNER, BUSINESS_OWNER.", now);
        AddCheck(run.Id, "SIGNOFFS_AFTER_FINAL_EVIDENCE", freshSignoffs ? "PASS" : "FAIL", $"Final evidence cutoff={latestEvidenceAt:O}.", now);

        var officialSource = c["Payroll:OfficialResultSource"] ?? string.Empty;
        var shadowEnabled = c.GetValue<bool>("Payroll:ShadowEngineEnabled");
        var payslipReleaseEnabled = c.GetValue<bool>("Payroll:PayslipReleaseEnabled");
        var productionLive = c.GetValue<bool>("Product:ProductionLive");
        var bravoOfficial = string.Equals(officialSource, "BRAVO", StringComparison.OrdinalIgnoreCase);
        var shadowValidation = !shadowEnabled;
        var payslipDisabled = !payslipReleaseEnabled;
        AddCheck(run.Id, "BRAVO_OFFICIAL_SOURCE", bravoOfficial ? "PASS" : "FAIL", $"OfficialResultSource={officialSource}.", now);
        AddCheck(run.Id, "SHADOW_VALIDATION_ONLY", shadowValidation ? "PASS" : "FAIL", $"ShadowEngineEnabled={shadowEnabled}.", now);
        AddCheck(run.Id, "PAYSLIP_RELEASE_DISABLED_PRE_V74", payslipDisabled ? "PASS" : "FAIL", $"PayslipReleaseEnabled={payslipReleaseEnabled}.", now);
        AddCheck(run.Id, "PRODUCTION_LIVE_FALSE", !productionLive ? "PASS" : "FAIL", $"ProductionLive={productionLive}.", now);

        await db.SaveChangesAsync(ct);
        var currentChecks = await db.E2eUatChecks.AsNoTracking().Where(x => x.E2eUatRunId == runId && x.CheckedAt >= now).ToListAsync(ct);
        run.Status = currentChecks.Any(x => x.Status == "FAIL") ? "FAIL" : currentChecks.Any(x => x.Status == "WARN") ? "WARN" : "PASS";
        audit.Record("V73_E2E_UAT_EVALUATED", "E2eUatRun", run.Id.ToString(), new { run.Status, run.V70RuntimeGateStatus, run.V71HrPilotGateStatus, run.V72PayrollParallelGateStatus });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<V73E2eUatRunDto> CompleteAsync(Guid runId, CompleteV73E2eUatRunRequest request, CancellationToken ct)
    {
        RequirePilot(); RequireCoordinator(); if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        await EvaluateAsync(runId, ct); run = await MutableRun(runId, ct);
        var checks = await LatestChecks(runId, ct);
        var mandatory = new[]
        {
            "V70_RUNTIME_GATE_PASS","V71_HR_PILOT_GATE_PASS","V72_PARALLEL_RUN_GATE_PASS","ACTIVE_POPULATION_140","OFFICE_SPLIT_101_39","POPULATION_BASELINE_HASH","ACTIVE_ENTRA_IDENTITY_140","TESTER_COHORT_READY","REQUIRED_30_SCENARIOS_PASS","ALL_DEFECTS_DISPOSITIONED","P0_P1_DEFECTS_FIXED","CRITICAL_DOMAIN_DEFECTS_FIXED","REQUIRED_SIGNOFFS_APPROVED","SIGNOFFS_AFTER_FINAL_EVIDENCE","BRAVO_OFFICIAL_SOURCE","SHADOW_VALIDATION_ONLY","PAYSLIP_RELEASE_DISABLED_PRE_V74","PRODUCTION_LIVE_FALSE"
        };
        if (mandatory.Any(code => !checks.TryGetValue(code, out var check) || check.Status != "PASS")) throw new InvalidOperationException("V73_MANDATORY_E2E_CHECKS_NOT_PASS");
        run.Status = "COMPLETED"; run.CompletedAt = DateTimeOffset.UtcNow; run.CompletionNote = CleanText(request.Reason, 1500);
        audit.Record("V73_E2E_UAT_COMPLETED", "E2eUatRun", run.Id.ToString(), new { request.Reason, productionLive = false, nextGate = "V74_PRODUCTION_CUTOVER" });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<IReadOnlyList<V73UatScenarioDto>> GetScenariosAsync(Guid runId, CancellationToken ct)
    {
        RequireCoordinator(); await EnsureRunExists(runId, ct);
        return await db.E2eUatScenarioEvidence.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.RecordedAt).Select(x => new V73UatScenarioDto(x.ScenarioCode, x.Domain, x.Persona, x.StaffCode, x.Status, x.Summary, x.EvidenceReference, x.CorrelationId, x.RecordedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<V73UatDefectDto>> GetDefectsAsync(Guid runId, CancellationToken ct)
    {
        RequireCoordinator(); await EnsureRunExists(runId, ct);
        var defects = await db.E2eUatDefects.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.RaisedAt).ToListAsync(ct); var ids = defects.Select(x => x.Id).ToArray();
        var resolutions = ids.Length == 0 ? [] : await db.E2eUatDefectResolutions.AsNoTracking().Where(x => ids.Contains(x.E2eUatDefectId)).OrderBy(x => x.ResolvedAt).ToListAsync(ct);
        var latest = resolutions.GroupBy(x => x.E2eUatDefectId).ToDictionary(g => g.Key, g => g.Last()); return defects.Select(d => ToDefectDto(d, latest.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<IReadOnlyList<V73UatSignoffDto>> GetSignoffsAsync(Guid runId, CancellationToken ct)
    {
        RequireCoordinator(); await EnsureRunExists(runId, ct);
        return await db.E2eUatSignoffs.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.SignedAt).Select(x => new V73UatSignoffDto(x.SignoffRole, x.Decision, x.Approver, x.EvidenceReference, x.Note, x.SignedAt, x.SignedBy)).ToListAsync(ct);
    }

    public async Task<V73E2eUatRunDto> GetAsync(Guid runId, CancellationToken ct)
    {
        RequireCoordinator(); var run = await db.E2eUatRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V73_RUN_NOT_FOUND");
        var scenarioCount = await db.E2eUatScenarioEvidence.AsNoTracking().CountAsync(x => x.E2eUatRunId == runId, ct); var defectCount = await db.E2eUatDefects.AsNoTracking().CountAsync(x => x.E2eUatRunId == runId, ct); var signoffCount = await db.E2eUatSignoffs.AsNoTracking().CountAsync(x => x.E2eUatRunId == runId, ct);
        var checks = (await LatestChecks(runId, ct)).Values.OrderBy(x => x.CheckCode).Select(x => new V73UatCheckDto(x.CheckCode, x.Status, x.Summary, x.CheckedAt)).ToList();
        return new V73E2eUatRunDto(run.Id, run.ReleaseCandidate, run.PopulationBaselineSha256, run.V70RuntimeEvidenceSha256, run.HrPilotRunId, run.PayrollParallelRunId, run.ExpectedPopulation, run.ExpectedHcm, run.ExpectedHn, run.TesterCohortSize, run.V70RuntimeGateStatus, run.V71HrPilotGateStatus, run.V72PayrollParallelGateStatus, run.Status, run.StartedAt, run.CompletedAt, run.CompletionNote, scenarioCount, defectCount, signoffCount, checks);
    }

    private async Task<Dictionary<string, E2eUatCheck>> LatestChecks(Guid runId, CancellationToken ct) => (await db.E2eUatChecks.AsNoTracking().Where(x => x.E2eUatRunId == runId).OrderBy(x => x.CheckedAt).ToListAsync(ct)).GroupBy(x => x.CheckCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    private async Task<E2eUatRun> MutableRun(Guid runId, CancellationToken ct) => await db.E2eUatRuns.SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V73_RUN_NOT_FOUND");
    private async Task EnsureRunExists(Guid runId, CancellationToken ct) { if (!await db.E2eUatRuns.AsNoTracking().AnyAsync(x => x.Id == runId, ct)) throw new InvalidOperationException("V73_RUN_NOT_FOUND"); }
    private void AddCheck(Guid runId, string code, string status, string summary, DateTimeOffset when) => db.E2eUatChecks.Add(new E2eUatCheck { Id = Guid.NewGuid(), E2eUatRunId = runId, CheckCode = code, Status = status, Summary = summary, CheckedAt = when, CheckedBy = Actor });
    private string Actor => current.StaffCode ?? current.EntraObjectId ?? "SYSTEM";
    private async Task VerifyScenarioEvidenceAsync(string scenarioCode, string status, string reference, Guid participantEmployeeId, DateTimeOffset runStartedAt, CancellationToken ct)
    {
        if (MachineFunctionalScenarios.Contains(scenarioCode))
        {
            var functional = await functionalEvidence.ResolveAsync(reference, ct) ?? throw new InvalidOperationException("V73_FUNCTIONAL_EVIDENCE_REFERENCE_REQUIRED");
            if (!functional.ScenarioCode.Equals(scenarioCode, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_FUNCTIONAL_EVIDENCE_SCENARIO_MISMATCH");
            if (functional.EmployeeId is Guid evidenceEmployee && evidenceEmployee != participantEmployeeId && scenarioCode is not "V73-ATT-01" and not "V73-OT-01" and not "V73-PERF-02" and not "V73-TAX-01" and not "V73-PAYS-01" and not "V73-PAYS-02")
                throw new InvalidOperationException("V73_FUNCTIONAL_EVIDENCE_EMPLOYEE_MISMATCH");
            if (!functional.Status.Equals(status, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_FUNCTIONAL_EVIDENCE_STATUS_MISMATCH");
            if (functional.CreatedAt < runStartedAt) throw new InvalidOperationException("V73_FUNCTIONAL_EVIDENCE_PREDATES_RUN");
            return;
        }

        await RequireArtifactAsync(reference, scenarioCode, status, runStartedAt, ct);
    }

    private async Task<EvidenceArtifact> RequireArtifactAsync(string reference, string expectedType, string? expectedResult, DateTimeOffset notBefore, CancellationToken ct)
    {
        var artifact = await functionalEvidence.ResolveArtifactAsync(reference, ct) ?? throw new InvalidOperationException("V73_IMMUTABLE_EVIDENCE_ARTIFACT_REQUIRED");
        if (!artifact.ArtifactType.Equals(expectedType, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_EVIDENCE_ARTIFACT_TYPE_MISMATCH");
        if (expectedResult is not null && !artifact.Result.Equals(expectedResult, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V73_EVIDENCE_ARTIFACT_STATUS_MISMATCH");
        if (artifact.ObservedAt < notBefore) throw new InvalidOperationException("V73_EVIDENCE_ARTIFACT_PREDATES_REQUIRED_EVENT");
        return artifact;
    }

    private void RequirePilot() { if (!c.GetValue<bool>("Pilot:Enabled")) throw new InvalidOperationException("PILOT_DISABLED"); }
    private void RequireCoordinator() { if (!(current.IsInRole(Roles.Hr) || current.IsInRole(Roles.Payroll) || current.IsInRole(Roles.Admin) || current.IsInRole(Roles.Leadership))) throw new InvalidOperationException("V73_UAT_COORDINATOR_ROLE_REQUIRED"); }
    private static bool IsPass(string? value) => string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "COMPLETED", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string CleanText(string? value, int max) { var x = (value ?? string.Empty).Trim(); return x[..Math.Min(max, x.Length)]; }
    private static string? TrimNullable(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var x = value.Trim(); return x[..Math.Min(max, x.Length)]; }
    private static V73UatDefectDto ToDefectDto(E2eUatDefect d, E2eUatDefectResolution? r) => new(d.Id, d.DefectCode, d.Severity, d.Domain, d.Summary, d.EvidenceReference, r?.Disposition, r?.ResolutionNote, r?.EvidenceReference, d.RaisedAt, r?.ResolvedAt);
    private static V73UatSignoffDto ToSignoffDto(E2eUatSignoff s) => new(s.SignoffRole, s.Decision, s.Approver, s.EvidenceReference, s.Note, s.SignedAt, s.SignedBy);
}
