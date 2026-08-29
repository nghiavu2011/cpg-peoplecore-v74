using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Services;

public sealed class V74ProductionCutoverService(PeopleCoreDbContext db, IConfiguration c, ICurrentUser current, IAuditService audit, FunctionalEvidenceService functionalEvidence)
{
    private static readonly Regex ShaRx = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> EvidenceStatuses = new(StringComparer.OrdinalIgnoreCase) { "PASS", "FAIL", "BLOCKED" };
    private static readonly IReadOnlyDictionary<string, string> StepPhases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["PRE_BACKUP_VERIFIED"] = "PRE_CUTOVER",
        ["PRE_RESTORE_DRILL_PASS"] = "PRE_CUTOVER",
        ["PRE_ROLLBACK_PLAN_APPROVED"] = "PRE_CUTOVER",
        ["PRE_ENTRA_PRODUCTION_READY"] = "PRE_CUTOVER",
        ["PRE_POSTGRES_PRODUCTION_READY"] = "PRE_CUTOVER",
        ["PRE_BRAVO_FINANCE_READY"] = "PRE_CUTOVER",
        ["PRE_SECURITY_PRIVACY_READY"] = "PRE_CUTOVER",
        ["PRE_OBSERVABILITY_READY"] = "PRE_CUTOVER",
        ["PRE_CUTOVER_WINDOW_APPROVED"] = "PRE_CUTOVER",
        ["EXEC_DEPLOYMENT_PASS"] = "EXECUTION",
        ["POST_LIVE_SMOKE_PASS"] = "POST_CUTOVER",
        ["POST_MONITORING_PASS"] = "POST_CUTOVER"
    };
    private static readonly string[] RequiredPreSteps = StepPhases.Where(x => x.Value == "PRE_CUTOVER").Select(x => x.Key).ToArray();
    private static readonly IReadOnlyDictionary<string, string> SignoffRoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HR_OWNER"] = Roles.Hr,
        ["PAYROLL_OWNER"] = Roles.Payroll,
        ["TECHNICAL_OWNER"] = Roles.Admin,
        ["BUSINESS_OWNER"] = Roles.Leadership
    };

    public async Task<V74ProductionCutoverRunDto> StartAsync(StartV74ProductionCutoverRequest request, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator();
        var release = CleanText(request.ReleaseCandidate, 120);
        if (string.IsNullOrWhiteSpace(release) || string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("V74_REQUIRED_FIELDS_MISSING");
        var baselineSha = NormalizeSha(request.PopulationBaselineSha256, "V74_POPULATION_SHA256_REQUIRED");
        var v70Sha = NormalizeSha(request.V70RuntimeEvidenceSha256, "V74_V70_EVIDENCE_SHA256_REQUIRED");
        var configuredSha = (c["V71:PopulationBaselineSha256"] ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configuredSha) && !string.Equals(configuredSha, baselineSha, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_POPULATION_BASELINE_HASH_MISMATCH");
        var uat = await db.E2eUatRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.E2eUatRunId, ct) ?? throw new InvalidOperationException("V74_V73_UAT_RUN_NOT_FOUND");
        if (!string.Equals(uat.PopulationBaselineSha256, baselineSha, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_V73_POPULATION_HASH_MISMATCH");
        if (!string.Equals(uat.V70RuntimeEvidenceSha256, v70Sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_V70_EVIDENCE_PIN_MISMATCH");

        var run = new ProductionCutoverRun
        {
            Id = Guid.NewGuid(), ReleaseCandidate = release, E2eUatRunId = uat.Id, PopulationBaselineSha256 = baselineSha, V70RuntimeEvidenceSha256 = v70Sha,
            ExpectedPopulation = c.GetValue("Population:CurrentBaseline", 140), ExpectedHcm = c.GetValue("Population:HcmBaseline", 101), ExpectedHn = c.GetValue("Population:HnBaseline", 39),
            Status = "OPEN", StartedAt = DateTimeOffset.UtcNow, StartedBy = Actor
        };
        db.ProductionCutoverRuns.Add(run);
        audit.Record("V74_CUTOVER_RUN_STARTED", "ProductionCutoverRun", run.Id.ToString(), new { run.ReleaseCandidate, run.E2eUatRunId, request.Reason, productionLive = false });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public async Task<V74CutoverStepDto> RecordStepAsync(Guid runId, RecordV74CutoverStepRequest request, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator(); var run = await MutableRun(runId, ct); EnsureNotCompleted(run);
        var code = Clean(request.StepCode); var status = Clean(request.Status);
        if (!StepPhases.TryGetValue(code, out var phase)) throw new InvalidOperationException("V74_UNKNOWN_STEP_CODE");
        if (!EvidenceStatuses.Contains(status)) throw new InvalidOperationException("V74_INVALID_STEP_STATUS");
        if (string.IsNullOrWhiteSpace(request.Summary) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V74_STEP_EVIDENCE_REQUIRED");
        var suppliedSha = string.IsNullOrWhiteSpace(request.EvidenceSha256) ? null : NormalizeSha(request.EvidenceSha256!, "V74_STEP_SHA256_INVALID");
        var evidenceReference = CleanText(request.EvidenceReference, 500);
        var artifact = await RequireArtifactAsync(evidenceReference, code, status, run.StartedAt, ct);
        if (suppliedSha is not null && !string.Equals(suppliedSha, artifact.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_STEP_SHA256_ARTIFACT_MISMATCH");
        var sha = artifact.Sha256;
        if (phase != "PRE_CUTOVER")
        {
            var latestDecision = await LatestDecision(runId, ct);
            if (latestDecision?.Decision != "GO") throw new InvalidOperationException("V74_GO_DECISION_REQUIRED_BEFORE_EXECUTION");
            var pre = await LatestSteps(runId, ct);
            if (RequiredPreSteps.Any(required => !pre.TryGetValue(required, out var step) || step.Status != "PASS")) throw new InvalidOperationException("V74_PRECUTOVER_EVIDENCE_NO_LONGER_PASS");
            var preTime = pre.Where(x => RequiredPreSteps.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).Select(x => x.Value.RecordedAt).DefaultIfEmpty(run.StartedAt).Max();
            var signoffs = await LatestSignoffs(runId, ct);
            if (SignoffRoleMap.Keys.Any(role => !signoffs.TryGetValue(role, out var signoff) || signoff.Decision != "APPROVE" || signoff.SignedAt < preTime)) throw new InvalidOperationException("V74_GO_SIGNOFFS_NO_LONGER_VALID");
        }
        if (code == "POST_LIVE_SMOKE_PASS" || code == "POST_MONITORING_PASS")
        {
            var latestSteps = await LatestSteps(runId, ct);
            if (!latestSteps.TryGetValue("EXEC_DEPLOYMENT_PASS", out var deploy) || deploy.Status != "PASS") throw new InvalidOperationException("V74_DEPLOYMENT_PASS_REQUIRED");
        }
        if (code == "POST_MONITORING_PASS" && run.LiveAuthorizedAt is null) throw new InvalidOperationException("V74_LIVE_AUTHORIZATION_REQUIRED");

        var evidence = new ProductionCutoverStepEvidence
        {
            Id = Guid.NewGuid(), ProductionCutoverRunId = runId, StepCode = code, Phase = phase, Status = status,
            Summary = CleanText(request.Summary, 1500), EvidenceReference = evidenceReference, EvidenceSha256 = sha,
            RecordedAt = DateTimeOffset.UtcNow, RecordedBy = Actor
        };
        db.ProductionCutoverStepEvidence.Add(evidence);
        if (code == "EXEC_DEPLOYMENT_PASS" && status == "PASS") run.Status = "CUTOVER_EXECUTED";
        audit.Record("V74_CUTOVER_STEP_RECORDED", "ProductionCutoverRun", runId.ToString(), new { code, status, phase, evidence.EvidenceReference, sha });
        await db.SaveChangesAsync(ct);
        return ToStepDto(evidence);
    }

    public async Task<V74CutoverSignoffDto> RecordSignoffAsync(Guid runId, RecordV74CutoverSignoffRequest request, CancellationToken ct)
    {
        RequireEnabled(); await EnsureRunExists(runId, ct);
        var role = Clean(request.SignoffRole); var decision = Clean(request.Decision);
        if (!SignoffRoleMap.TryGetValue(role, out var requiredRole)) throw new InvalidOperationException("V74_INVALID_SIGNOFF_ROLE");
        if (!current.IsInRole(requiredRole)) throw new InvalidOperationException("V74_SIGNOFF_ACTOR_ROLE_MISMATCH");
        if (decision is not ("APPROVE" or "REJECT")) throw new InvalidOperationException("V74_INVALID_SIGNOFF_DECISION");
        if (string.IsNullOrWhiteSpace(request.Approver) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V74_SIGNOFF_EVIDENCE_REQUIRED");
        var run = await db.ProductionCutoverRuns.AsNoTracking().SingleAsync(x => x.Id == runId, ct);
        var signoffReference = CleanText(request.EvidenceReference, 500);
        await RequireArtifactAsync(signoffReference, $"V74-SIGNOFF-{role}", decision == "APPROVE" ? "PASS" : "FAIL", run.StartedAt, ct);
        var s = new ProductionCutoverSignoff
        {
            Id = Guid.NewGuid(), ProductionCutoverRunId = runId, SignoffRole = role, Decision = decision,
            Approver = CleanText(request.Approver, 200), EvidenceReference = signoffReference, Note = CleanText(request.Note, 1500), SignedAt = DateTimeOffset.UtcNow, SignedBy = Actor
        };
        db.ProductionCutoverSignoffs.Add(s);
        audit.Record("V74_CUTOVER_SIGNOFF_RECORDED", "ProductionCutoverRun", runId.ToString(), new { role, decision, s.Approver, s.EvidenceReference });
        await db.SaveChangesAsync(ct); return ToSignoffDto(s);
    }

    public async Task<V74ProductionCutoverRunDto> EvaluateAsync(Guid runId, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator(); var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        var now = DateTimeOffset.UtcNow;
        var uat = await db.E2eUatRuns.AsNoTracking().SingleAsync(x => x.Id == run.E2eUatRunId, ct);
        AddCheck(runId, "V73_E2E_UAT_COMPLETED", uat.Status == "COMPLETED" ? "PASS" : "FAIL", $"Pinned V73 run={uat.Id}; status={uat.Status}.", now);
        AddCheck(runId, "V70_RUNTIME_GATE_PASS", IsPass(uat.V70RuntimeGateStatus) ? "PASS" : "FAIL", $"V70={uat.V70RuntimeGateStatus}; evidence={run.V70RuntimeEvidenceSha256}.", now);
        AddCheck(runId, "V71_HR_PILOT_GATE_PASS", IsPass(uat.V71HrPilotGateStatus) ? "PASS" : "FAIL", $"V71={uat.V71HrPilotGateStatus}; run={uat.HrPilotRunId}.", now);
        AddCheck(runId, "V72_PARALLEL_RUN_GATE_PASS", IsPass(uat.V72PayrollParallelGateStatus) ? "PASS" : "FAIL", $"V72={uat.V72PayrollParallelGateStatus}; run={uat.PayrollParallelRunId}.", now);

        var active = await db.Employees.AsNoTracking().Where(x => x.EmploymentStatus == "ACTIVE").ToListAsync(ct);
        var hcm = active.Count(x => x.OfficeCode.Equals("HCM", StringComparison.OrdinalIgnoreCase)); var hn = active.Count(x => x.OfficeCode.Equals("HN", StringComparison.OrdinalIgnoreCase));
        AddCheck(runId, "ACTIVE_POPULATION_140", active.Count == run.ExpectedPopulation ? "PASS" : "FAIL", $"active={active.Count}; expected={run.ExpectedPopulation}.", now);
        AddCheck(runId, "OFFICE_SPLIT_101_39", hcm == run.ExpectedHcm && hn == run.ExpectedHn ? "PASS" : "FAIL", $"HCM={hcm}/{run.ExpectedHcm}; HN={hn}/{run.ExpectedHn}.", now);
        var activeIds = active.Select(x => x.Id).ToArray();
        var identities = await db.EmployeeIdentities.AsNoTracking().Where(x => activeIds.Contains(x.EmployeeId) && x.IsActive && x.RevokedAt == null).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
        AddCheck(runId, "ACTIVE_ENTRA_IDENTITY_140", identities == run.ExpectedPopulation ? "PASS" : "FAIL", $"activeIdentityEmployees={identities}; expected={run.ExpectedPopulation}.", now);

        var latestSteps = await LatestSteps(runId, ct);
        foreach (var code in RequiredPreSteps)
        {
            var pass = latestSteps.TryGetValue(code, out var step) && step.Status == "PASS";
            AddCheck(runId, code, pass ? "PASS" : "FAIL", pass ? $"Latest evidence PASS: {step!.EvidenceReference}." : "Required pre-cutover evidence is missing or not PASS.", now);
        }
        var preEvidenceTime = latestSteps.Where(x => RequiredPreSteps.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).Select(x => x.Value.RecordedAt).DefaultIfEmpty(run.StartedAt).Max();
        var signoffs = await LatestSignoffs(runId, ct); var allSignoffs = SignoffRoleMap.Keys.All(role => signoffs.TryGetValue(role, out var s) && s.Decision == "APPROVE");
        var freshSignoffs = allSignoffs && SignoffRoleMap.Keys.All(role => signoffs[role].SignedAt >= preEvidenceTime);
        AddCheck(runId, "REQUIRED_SIGNOFFS_APPROVED", allSignoffs ? "PASS" : "FAIL", allSignoffs ? "HR/Payroll/Technical/Business latest decisions are APPROVE." : "Four-owner approval is incomplete or rejected.", now);
        AddCheck(runId, "SIGNOFFS_AFTER_PRECUTOVER_EVIDENCE", freshSignoffs ? "PASS" : "FAIL", freshSignoffs ? "All approvals are after final pre-cutover evidence." : "Approval must be refreshed after final pre-cutover evidence.", now);

        var official = c["Payroll:OfficialResultSource"] ?? string.Empty; var shadow = c.GetValue<bool>("Payroll:ShadowEngineEnabled");
        AddCheck(runId, "BRAVO_OFFICIAL_SOURCE", official.Equals("BRAVO", StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL", $"OfficialResultSource={official}.", now);
        AddCheck(runId, "SHADOW_VALIDATION_ONLY", !shadow ? "PASS" : "FAIL", $"ShadowEngineEnabled={shadow}.", now);
        var nativeConfirmed = c.GetValue<bool>("Bravo:NativeSpecificationConfirmed"); var transport = c["Bravo:TransportMode"] ?? "NOT_CONFIGURED"; var mapping = c["Bravo:MappingMode"] ?? "NOT_CONFIGURED"; var mode = c["Bravo:Mode"] ?? "Stub";
        var bravoReady = nativeConfirmed && !mode.Equals("Stub", StringComparison.OrdinalIgnoreCase) && !transport.Equals("NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase) && !mapping.Equals("NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase);
        AddCheck(runId, "BRAVO_NATIVE_RUNTIME_READY", bravoReady ? "PASS" : "FAIL", $"Mode={mode}; NativeSpecificationConfirmed={nativeConfirmed}; TransportMode={transport}; MappingMode={mapping}.", now);

        var productionLive = c.GetValue<bool>("Product:ProductionLive"); var payslip = c.GetValue<bool>("Payroll:PayslipReleaseEnabled");
        if (run.LiveAuthorizedAt is null)
            AddCheck(runId, "PRELIVE_FLAGS_SAFE", !productionLive && !payslip ? "PASS" : "FAIL", $"ProductionLive={productionLive}; PayslipReleaseEnabled={payslip}; expected false before live authorization.", now);
        else
        {
            AddCheck(runId, "RUNTIME_PRODUCTION_LIVE_TRUE", productionLive ? "PASS" : "FAIL", $"ProductionLive={productionLive} after authorization.", now);
            AddCheck(runId, "PAYSLIP_RELEASE_ENABLED", payslip ? "PASS" : "FAIL", $"PayslipReleaseEnabled={payslip} after authorization.", now);
        }

        if (run.LiveAuthorizedAt is not null)
        {
            var deployPass = latestSteps.TryGetValue("EXEC_DEPLOYMENT_PASS", out var dep) && dep.Status == "PASS";
            var smokePass = latestSteps.TryGetValue("POST_LIVE_SMOKE_PASS", out var smoke) && smoke.Status == "PASS";
            var monitorPass = latestSteps.TryGetValue("POST_MONITORING_PASS", out var mon) && mon.Status == "PASS";
            AddCheck(runId, "EXEC_DEPLOYMENT_PASS", deployPass ? "PASS" : "FAIL", deployPass ? dep!.EvidenceReference : "Deployment evidence not PASS.", now);
            AddCheck(runId, "POST_LIVE_SMOKE_PASS", smokePass ? "PASS" : "FAIL", smokePass ? smoke!.EvidenceReference : "Live smoke evidence not PASS.", now);
            AddCheck(runId, "POST_MONITORING_PASS", monitorPass ? "PASS" : "FAIL", monitorPass ? mon!.EvidenceReference : "Post-cutover monitoring evidence not PASS.", now);
            var finalEvidenceTime = new[] { dep?.RecordedAt, smoke?.RecordedAt }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(run.LiveAuthorizedAt.Value).Max();
            var finalSignoffs = await LatestSignoffs(runId, ct);
            var finalApproved = SignoffRoleMap.Keys.All(role => finalSignoffs.TryGetValue(role, out var signoff) && signoff.Decision == "APPROVE" && signoff.SignedAt >= finalEvidenceTime);
            AddCheck(runId, "FINAL_SIGNOFFS_AFTER_LIVE_SMOKE", finalApproved ? "PASS" : "FAIL", finalApproved ? "Four-owner approvals were refreshed after deployment/live-smoke evidence." : "Final HR/Payroll/Technical/Business approvals must be refreshed after live-smoke evidence.", now);
        }

        await db.SaveChangesAsync(ct);
        var currentChecks = await db.ProductionCutoverChecks.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId && x.CheckedAt >= now).ToListAsync(ct);
        if (run.Status is not ("NO_GO" or "GO_APPROVED" or "CUTOVER_EXECUTED" or "LIVE_AUTHORIZED")) run.Status = currentChecks.Any(x => x.Status == "FAIL") ? "OPEN" : "OPEN";
        audit.Record("V74_CUTOVER_EVALUATED", "ProductionCutoverRun", runId.ToString(), new { run.Status, productionLive, payslip, bravoReady });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<V74CutoverDecisionDto> DecideAsync(Guid runId, DecideV74GoNoGoRequest request, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator(); var run = await MutableRun(runId, ct); EnsureNotCompleted(run);
        var decision = Clean(request.Decision); if (decision is not ("GO" or "NO_GO")) throw new InvalidOperationException("V74_INVALID_GO_NO_GO_DECISION");
        if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V74_DECISION_EVIDENCE_REQUIRED");
        var decisionReference = CleanText(request.EvidenceReference, 500);
        await RequireArtifactAsync(decisionReference, $"V74-DECISION-{decision}", decision == "GO" ? "PASS" : "FAIL", run.StartedAt, ct);
        if (decision == "GO")
        {
            await EvaluateAsync(runId, ct); var checks = await LatestChecks(runId, ct);
            var mandatory = new List<string> { "V73_E2E_UAT_COMPLETED", "V70_RUNTIME_GATE_PASS", "V71_HR_PILOT_GATE_PASS", "V72_PARALLEL_RUN_GATE_PASS", "ACTIVE_POPULATION_140", "OFFICE_SPLIT_101_39", "ACTIVE_ENTRA_IDENTITY_140", "REQUIRED_SIGNOFFS_APPROVED", "SIGNOFFS_AFTER_PRECUTOVER_EVIDENCE", "BRAVO_OFFICIAL_SOURCE", "SHADOW_VALIDATION_ONLY", "BRAVO_NATIVE_RUNTIME_READY", "PRELIVE_FLAGS_SAFE" };
            mandatory.AddRange(RequiredPreSteps);
            if (mandatory.Any(code => !checks.TryGetValue(code, out var check) || check.Status != "PASS")) throw new InvalidOperationException("V74_GO_MANDATORY_CHECKS_NOT_PASS");
            run.Status = "GO_APPROVED";
        }
        else run.Status = "NO_GO";
        var d = new ProductionCutoverDecision { Id = Guid.NewGuid(), ProductionCutoverRunId = runId, Decision = decision, Reason = CleanText(request.Reason, 1500), EvidenceReference = decisionReference, DecidedAt = DateTimeOffset.UtcNow, DecidedBy = Actor };
        db.ProductionCutoverDecisions.Add(d); audit.Record("V74_GO_NO_GO_DECISION", "ProductionCutoverRun", runId.ToString(), new { decision, d.EvidenceReference, d.Reason });
        await db.SaveChangesAsync(ct); return ToDecisionDto(d);
    }

    public async Task<V74ProductionCutoverRunDto> AuthorizeProductionLiveAsync(Guid runId, AuthorizeV74ProductionLiveRequest request, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator(); if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V74_LIVE_AUTH_EVIDENCE_REQUIRED");
        var run = await MutableRun(runId, ct); EnsureNotCompleted(run); if (run.LiveAuthorizedAt is not null) return await GetAsync(runId, ct);
        var decision = await LatestDecision(runId, ct); if (decision?.Decision != "GO") throw new InvalidOperationException("V74_GO_DECISION_REQUIRED");
        var steps = await LatestSteps(runId, ct);
        if (!steps.TryGetValue("EXEC_DEPLOYMENT_PASS", out var deployment) || deployment.Status != "PASS") throw new InvalidOperationException("V74_DEPLOYMENT_NOT_PASS");
        if (!steps.TryGetValue("POST_LIVE_SMOKE_PASS", out var smoke) || smoke.Status != "PASS") throw new InvalidOperationException("V74_LIVE_SMOKE_NOT_PASS");
        var liveAuthReference = CleanText(request.EvidenceReference, 500);
        await RequireArtifactAsync(liveAuthReference, "V74-LIVE-AUTHORIZATION", "PASS", smoke.RecordedAt, ct);
        if (c.GetValue<bool>("Product:ProductionLive") || c.GetValue<bool>("Payroll:PayslipReleaseEnabled")) throw new InvalidOperationException("V74_RUNTIME_FLAGS_MUST_REMAIN_FALSE_UNTIL_AUTHORIZATION");
        run.LiveAuthorizedAt = DateTimeOffset.UtcNow; run.LiveAuthorizedBy = Actor; run.Status = "LIVE_AUTHORIZED";
        audit.Record("V74_PRODUCTION_LIVE_AUTHORIZED", "ProductionCutoverRun", runId.ToString(), new { request.Reason, EvidenceReference = liveAuthReference, runtimeToggleStillExternal = true });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<V74ProductionCutoverRunDto> CompleteAsync(Guid runId, CompleteV74ProductionCutoverRequest request, CancellationToken ct)
    {
        RequireEnabled(); RequireOperator(); if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var run = await MutableRun(runId, ct); if (run.Status == "COMPLETED") return await GetAsync(runId, ct); if (run.LiveAuthorizedAt is null) throw new InvalidOperationException("V74_LIVE_NOT_AUTHORIZED");
        await EvaluateAsync(runId, ct); var checks = await LatestChecks(runId, ct);
        var mandatory = new[] { "V73_E2E_UAT_COMPLETED", "V70_RUNTIME_GATE_PASS", "V71_HR_PILOT_GATE_PASS", "V72_PARALLEL_RUN_GATE_PASS", "ACTIVE_POPULATION_140", "OFFICE_SPLIT_101_39", "ACTIVE_ENTRA_IDENTITY_140", "BRAVO_OFFICIAL_SOURCE", "SHADOW_VALIDATION_ONLY", "BRAVO_NATIVE_RUNTIME_READY", "RUNTIME_PRODUCTION_LIVE_TRUE", "PAYSLIP_RELEASE_ENABLED", "EXEC_DEPLOYMENT_PASS", "POST_LIVE_SMOKE_PASS", "POST_MONITORING_PASS", "FINAL_SIGNOFFS_AFTER_LIVE_SMOKE" };
        if (mandatory.Any(code => !checks.TryGetValue(code, out var check) || check.Status != "PASS")) throw new InvalidOperationException("V74_FINAL_MANDATORY_CHECKS_NOT_PASS");
        run.Status = "COMPLETED"; run.CompletedAt = DateTimeOffset.UtcNow; run.CompletionNote = CleanText(request.Reason, 1500);
        audit.Record("V74_PRODUCTION_CUTOVER_COMPLETED", "ProductionCutoverRun", runId.ToString(), new { request.Reason, productionLive = true, roadmap = "COMPLETE" });
        await db.SaveChangesAsync(ct); return await GetAsync(runId, ct);
    }

    public async Task<V74ProductionCutoverRunDto> GetAsync(Guid runId, CancellationToken ct)
    {
        RequireViewer(); var run = await db.ProductionCutoverRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V74_RUN_NOT_FOUND");
        var checks = (await LatestChecks(runId, ct)).Values.OrderBy(x => x.CheckCode).Select(x => new V74CutoverCheckDto(x.CheckCode, x.Status, x.Summary, x.CheckedAt)).ToList();
        return new V74ProductionCutoverRunDto(run.Id, run.ReleaseCandidate, run.E2eUatRunId, run.PopulationBaselineSha256, run.V70RuntimeEvidenceSha256, run.ExpectedPopulation, run.ExpectedHcm, run.ExpectedHn, run.Status, run.StartedAt, run.LiveAuthorizedAt, run.CompletedAt, run.CompletionNote, checks);
    }
    public async Task<IReadOnlyList<V74CutoverStepDto>> GetStepsAsync(Guid runId, CancellationToken ct) { RequireViewer(); await EnsureRunExists(runId, ct); return await db.ProductionCutoverStepEvidence.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.RecordedAt).Select(x => new V74CutoverStepDto(x.StepCode, x.Phase, x.Status, x.Summary, x.EvidenceReference, x.EvidenceSha256, x.RecordedAt, x.RecordedBy)).ToListAsync(ct); }
    public async Task<IReadOnlyList<V74CutoverSignoffDto>> GetSignoffsAsync(Guid runId, CancellationToken ct) { RequireViewer(); await EnsureRunExists(runId, ct); return await db.ProductionCutoverSignoffs.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.SignedAt).Select(x => new V74CutoverSignoffDto(x.SignoffRole, x.Decision, x.Approver, x.EvidenceReference, x.Note, x.SignedAt, x.SignedBy)).ToListAsync(ct); }
    public async Task<IReadOnlyList<V74CutoverDecisionDto>> GetDecisionsAsync(Guid runId, CancellationToken ct) { RequireViewer(); await EnsureRunExists(runId, ct); return await db.ProductionCutoverDecisions.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.DecidedAt).Select(x => new V74CutoverDecisionDto(x.Decision, x.Reason, x.EvidenceReference, x.DecidedAt, x.DecidedBy)).ToListAsync(ct); }

    private async Task<Dictionary<string, ProductionCutoverStepEvidence>> LatestSteps(Guid runId, CancellationToken ct) => (await db.ProductionCutoverStepEvidence.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.RecordedAt).ToListAsync(ct)).GroupBy(x => x.StepCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    private async Task<Dictionary<string, ProductionCutoverSignoff>> LatestSignoffs(Guid runId, CancellationToken ct) => (await db.ProductionCutoverSignoffs.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.SignedAt).ToListAsync(ct)).GroupBy(x => x.SignoffRole, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    private async Task<Dictionary<string, ProductionCutoverCheck>> LatestChecks(Guid runId, CancellationToken ct) => (await db.ProductionCutoverChecks.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderBy(x => x.CheckedAt).ToListAsync(ct)).GroupBy(x => x.CheckCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    private async Task<ProductionCutoverDecision?> LatestDecision(Guid runId, CancellationToken ct) => await db.ProductionCutoverDecisions.AsNoTracking().Where(x => x.ProductionCutoverRunId == runId).OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
    private async Task<EvidenceArtifact> RequireArtifactAsync(string reference, string expectedType, string expectedResult, DateTimeOffset notBefore, CancellationToken ct)
    {
        var artifact = await functionalEvidence.ResolveArtifactAsync(reference, ct) ?? throw new InvalidOperationException("V74_MACHINE_EVIDENCE_ARTIFACT_REQUIRED");
        if (!string.Equals(artifact.ArtifactType, expectedType, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_EVIDENCE_ARTIFACT_TYPE_MISMATCH");
        if (!string.Equals(artifact.Result, expectedResult, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V74_EVIDENCE_ARTIFACT_RESULT_MISMATCH");
        if (artifact.ObservedAt < notBefore) throw new InvalidOperationException("V74_EVIDENCE_ARTIFACT_PREDATES_REQUIRED_EVENT");
        return artifact;
    }
    private async Task<ProductionCutoverRun> MutableRun(Guid runId, CancellationToken ct) => await db.ProductionCutoverRuns.SingleOrDefaultAsync(x => x.Id == runId, ct) ?? throw new InvalidOperationException("V74_RUN_NOT_FOUND");
    private async Task EnsureRunExists(Guid runId, CancellationToken ct) { if (!await db.ProductionCutoverRuns.AsNoTracking().AnyAsync(x => x.Id == runId, ct)) throw new InvalidOperationException("V74_RUN_NOT_FOUND"); }
    private void AddCheck(Guid runId, string code, string status, string summary, DateTimeOffset now) => db.ProductionCutoverChecks.Add(new ProductionCutoverCheck { Id = Guid.NewGuid(), ProductionCutoverRunId = runId, CheckCode = code, Status = status, Summary = summary, CheckedAt = now, CheckedBy = Actor });
    private string Actor => current.StaffCode ?? current.EntraObjectId ?? "SYSTEM";
    private void RequireEnabled() { if (!c.GetValue<bool>("V74:CutoverOrchestrationEnabled")) throw new InvalidOperationException("V74_CUTOVER_ORCHESTRATION_DISABLED"); }
    private void RequireOperator() { if (!(current.IsInRole(Roles.Admin) || current.IsInRole(Roles.Leadership))) throw new InvalidOperationException("V74_CUTOVER_OPERATOR_ROLE_REQUIRED"); }
    private void RequireViewer() { if (!(current.IsInRole(Roles.Hr) || current.IsInRole(Roles.Payroll) || current.IsInRole(Roles.Admin) || current.IsInRole(Roles.Leadership))) throw new InvalidOperationException("V74_CUTOVER_VIEW_ROLE_REQUIRED"); }
    private static void EnsureNotCompleted(ProductionCutoverRun run) { if (run.Status == "COMPLETED") throw new InvalidOperationException("V74_RUN_ALREADY_COMPLETED"); }
    private static bool IsPass(string? value) => string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "COMPLETED", StringComparison.OrdinalIgnoreCase);
    private static string Clean(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string CleanText(string? value, int max) { var x = (value ?? string.Empty).Trim(); return x[..Math.Min(max, x.Length)]; }
    private static string NormalizeSha(string value, string error) { var x = (value ?? string.Empty).Trim().ToLowerInvariant(); if (!ShaRx.IsMatch(x)) throw new InvalidOperationException(error); return x; }
    private static V74CutoverStepDto ToStepDto(ProductionCutoverStepEvidence x) => new(x.StepCode, x.Phase, x.Status, x.Summary, x.EvidenceReference, x.EvidenceSha256, x.RecordedAt, x.RecordedBy);
    private static V74CutoverDecisionDto ToDecisionDto(ProductionCutoverDecision x) => new(x.Decision, x.Reason, x.EvidenceReference, x.DecidedAt, x.DecidedBy);
    private static V74CutoverSignoffDto ToSignoffDto(ProductionCutoverSignoff x) => new(x.SignoffRole, x.Decision, x.Approver, x.EvidenceReference, x.Note, x.SignedAt, x.SignedBy);
}
