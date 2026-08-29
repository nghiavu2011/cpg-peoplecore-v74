using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services;

public sealed class V72PayrollParallelRunService(PeopleCoreDbContext db, IConfiguration c, ICurrentUser current, IAuditService audit)
{
    private static readonly Regex PeriodRx = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private static readonly Regex ShaRx = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AcceptedDispositions = new(StringComparer.OrdinalIgnoreCase) { "EXPLAINED_ACCEPTED", "ROUNDING_ACCEPTED" };
    private static readonly HashSet<string> BlockingDispositions = new(StringComparer.OrdinalIgnoreCase) { "DATA_FIX_REQUIRED", "SHADOW_RULE_FIX_REQUIRED", "BRAVO_REVIEW_REQUIRED" };
    private static readonly HashSet<string> AllDispositions = new(AcceptedDispositions.Concat(BlockingDispositions), StringComparer.OrdinalIgnoreCase);

    public async Task<V72PayrollParallelRunDto> StartAsync(StartV72PayrollParallelRunRequest request, CancellationToken ct)
    {
        RequirePilot(); RequirePayrollActor();
        if (!PeriodRx.IsMatch((request.PayrollPeriod ?? string.Empty).Trim())) throw new InvalidOperationException("V72_INVALID_PAYROLL_PERIOD");
        if (request.IterationNo <= 0) throw new InvalidOperationException("V72_INVALID_ITERATION");
        if (string.IsNullOrWhiteSpace(request.OfficialSourceRunId) || string.IsNullOrWhiteSpace(request.ShadowRuleSnapshotId) || string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("V72_REQUIRED_FIELDS_MISSING");
        var period = request.PayrollPeriod.Trim();
        if (await db.PayrollParallelRuns.AnyAsync(x => x.PayrollPeriod == period && x.IterationNo == request.IterationNo, ct)) throw new InvalidOperationException("V72_RUN_ALREADY_EXISTS");
        var run = new PayrollParallelRun
        {
            Id = Guid.NewGuid(), PayrollPeriod = period, IterationNo = request.IterationNo,
            ExpectedPopulation = c.GetValue("V72:ExpectedPopulation", 140), OfficialSourceSystem = "BRAVO",
            OfficialSourceRunId = CleanText(request.OfficialSourceRunId, 200), ShadowRuleSnapshotId = CleanText(request.ShadowRuleSnapshotId, 200),
            V70RuntimeGateStatus = c["Journey:V70RuntimeGateStatus"] ?? "PENDING_EXTERNAL_EVIDENCE",
            V71HrPilotGateStatus = c["Journey:V71HrPilotGateStatus"] ?? "PENDING_EXTERNAL_EVIDENCE",
            Status = "OPEN", StartedAt = DateTimeOffset.UtcNow, StartedBy = Actor
        };
        db.PayrollParallelRuns.Add(run);
        audit.Record("V72_PARALLEL_RUN_STARTED", "PayrollParallelRun", run.Id.ToString(), new { run.PayrollPeriod, run.IterationNo, run.ExpectedPopulation, request.Reason, run.V70RuntimeGateStatus, run.V71HrPilotGateStatus });
        await db.SaveChangesAsync(ct);
        return await GetAsync(run.Id, ct);
    }

    public Task<V72PayrollParallelRunDto> ImportOfficialAsync(Guid runId, ImportV72PayrollResultBatchRequest request, CancellationToken ct)
        => ImportAsync(runId, request, official: true, ct);

    public Task<V72PayrollParallelRunDto> ImportShadowAsync(Guid runId, ImportV72PayrollResultBatchRequest request, CancellationToken ct)
        => ImportAsync(runId, request, official: false, ct);

    private async Task<V72PayrollParallelRunDto> ImportAsync(Guid runId, ImportV72PayrollResultBatchRequest request, bool official, CancellationToken ct)
    {
        RequirePilot(); RequirePayrollActor();
        var run = await MutableRun(runId, ct);
        if (run.Status == "COMPLETED") throw new InvalidOperationException("V72_RUN_COMPLETED");
        if (await db.PayrollParallelSnapshots.AnyAsync(x => x.PayrollParallelRunId == runId, ct)) throw new InvalidOperationException("V72_INPUTS_FROZEN_AFTER_COMPARISON");
        if (!ShaRx.IsMatch((request.SourceFileSha256 ?? string.Empty).Trim())) throw new InvalidOperationException("V72_SOURCE_SHA256_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.EvidenceReference)) throw new InvalidOperationException("V72_EVIDENCE_REFERENCE_REQUIRED");
        if (request.Rows is null || request.Rows.Count == 0 || request.Rows.Count > run.ExpectedPopulation) throw new InvalidOperationException("V72_INVALID_RESULT_BATCH_SIZE");
        var duplicateStaff = request.Rows.GroupBy(x => CleanCode(x.StaffCode), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (duplicateStaff is not null) throw new InvalidOperationException("V72_DUPLICATE_OR_EMPTY_STAFF_CODE");

        var codes = request.Rows.Select(x => CleanCode(x.StaffCode)).ToArray();
        var employees = await db.Employees.Where(x => codes.Contains(x.StaffCode) && x.EmploymentStatus == "ACTIVE").ToDictionaryAsync(x => x.StaffCode, StringComparer.OrdinalIgnoreCase, ct);
        if (employees.Count != codes.Length) throw new InvalidOperationException("V72_RESULT_EMPLOYEE_NOT_ACTIVE_OR_NOT_FOUND");

        foreach (var row in request.Rows)
        {
            var staff = CleanCode(row.StaffCode); var employee = employees[staff];
            var normalized = NormalizeComponents(row.Components); var json = JsonSerializer.Serialize(normalized); var hash = Sha256(json);
            if (official)
            {
                var existing = await db.PayrollParallelOfficialResults.SingleOrDefaultAsync(x => x.PayrollParallelRunId == runId && x.EmployeeId == employee.Id, ct);
                if (existing is not null) { if (!string.Equals(existing.ComponentsSha256, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V72_OFFICIAL_RESULT_CONFLICT"); continue; }
                db.PayrollParallelOfficialResults.Add(new PayrollParallelOfficialResult { Id=Guid.NewGuid(), PayrollParallelRunId=runId, EmployeeId=employee.Id, StaffCode=employee.StaffCode, SourceSystem="BRAVO", SourceRunId=run.OfficialSourceRunId, ComponentsJson=json, ComponentsSha256=hash, SourceFileSha256=request.SourceFileSha256.ToLowerInvariant(), EvidenceReference=CleanText(request.EvidenceReference,500), ImportedAt=DateTimeOffset.UtcNow, ImportedBy=Actor });
            }
            else
            {
                var existing = await db.PayrollParallelShadowResults.SingleOrDefaultAsync(x => x.PayrollParallelRunId == runId && x.EmployeeId == employee.Id, ct);
                if (existing is not null) { if (!string.Equals(existing.ComponentsSha256, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("V72_SHADOW_RESULT_CONFLICT"); continue; }
                db.PayrollParallelShadowResults.Add(new PayrollParallelShadowResult { Id=Guid.NewGuid(), PayrollParallelRunId=runId, EmployeeId=employee.Id, StaffCode=employee.StaffCode, RuleSnapshotId=run.ShadowRuleSnapshotId, ComponentsJson=json, ComponentsSha256=hash, SourceFileSha256=request.SourceFileSha256.ToLowerInvariant(), EvidenceReference=CleanText(request.EvidenceReference,500), Status="VALIDATION_ONLY", ImportedAt=DateTimeOffset.UtcNow, ImportedBy=Actor });
            }
        }
        audit.Record(official ? "V72_BRAVO_RESULTS_IMPORTED" : "V72_SHADOW_RESULTS_IMPORTED", "PayrollParallelRun", runId.ToString(), new { rowCount=request.Rows.Count, sourceFileSha256=request.SourceFileSha256.ToLowerInvariant(), evidenceReference=CleanText(request.EvidenceReference,200) });
        await db.SaveChangesAsync(ct);
        return await GetAsync(runId, ct);
    }

    public async Task<V72PayrollParallelRunDto> EvaluateAsync(Guid runId, CancellationToken ct)
    {
        RequirePilot(); RequirePayrollActor();
        var run = await MutableRun(runId, ct);
        if (run.Status == "COMPLETED") return await GetAsync(runId, ct);
        var official = await db.PayrollParallelOfficialResults.AsNoTracking().Where(x => x.PayrollParallelRunId == runId).ToListAsync(ct);
        var shadow = await db.PayrollParallelShadowResults.AsNoTracking().Where(x => x.PayrollParallelRunId == runId).ToListAsync(ct);
        var activePopulation = await db.Employees.AsNoTracking().CountAsync(x => x.EmploymentStatus == "ACTIVE", ct);
        var officialIds = official.Select(x=>x.EmployeeId).ToHashSet(); var shadowIds = shadow.Select(x=>x.EmployeeId).ToHashSet();
        var snapshotsExist = await db.PayrollParallelSnapshots.AnyAsync(x=>x.PayrollParallelRunId==runId,ct);
        if (!snapshotsExist && official.Count == run.ExpectedPopulation && shadow.Count == run.ExpectedPopulation && officialIds.SetEquals(shadowIds))
            await CreateComparisonEvidence(run, official, shadow, ct);

        var now = DateTimeOffset.UtcNow;
        AddCheck(run.Id,"ACTIVE_POPULATION_140",activePopulation==run.ExpectedPopulation?"PASS":"FAIL",$"Active Employee Master count={activePopulation}; expected={run.ExpectedPopulation}.", now);
        AddCheck(run.Id,"OFFICIAL_RESULTS_140",official.Count==run.ExpectedPopulation?"PASS":"FAIL",$"BRAVO parallel-run snapshots={official.Count}; expected={run.ExpectedPopulation}.", now);
        AddCheck(run.Id,"SHADOW_RESULTS_140",shadow.Count==run.ExpectedPopulation?"PASS":"FAIL",$"Shadow validation snapshots={shadow.Count}; expected={run.ExpectedPopulation}.", now);
        AddCheck(run.Id,"EMPLOYEE_SET_MATCH",officialIds.SetEquals(shadowIds)&&official.Count==shadow.Count?"PASS":"FAIL",$"Official employees={officialIds.Count}; Shadow employees={shadowIds.Count}.", now);
        AddCheck(run.Id,"OFFICIAL_SOURCE_BRAVO",official.All(x=>x.SourceSystem=="BRAVO" && x.SourceRunId==run.OfficialSourceRunId)?"PASS":"FAIL","All official comparison inputs must be BRAVO and use the run source ID.", now);
        AddCheck(run.Id,"SHADOW_VALIDATION_ONLY",shadow.All(x=>x.Status=="VALIDATION_ONLY" && x.RuleSnapshotId==run.ShadowRuleSnapshotId)?"PASS":"FAIL","All Shadow inputs must remain VALIDATION_ONLY and use the approved rule snapshot ID.", now);
        var prodLive=c.GetValue<bool>("Product:ProductionLive"); AddCheck(run.Id,"PRODUCTION_LIVE_FALSE",!prodLive?"PASS":"FAIL",$"Product.ProductionLive={prodLive}.", now);
        var payslipRelease=c.GetValue<bool>("Payroll:PayslipReleaseEnabled"); AddCheck(run.Id,"PAYSLIP_RELEASE_DISABLED",!payslipRelease?"PASS":"FAIL",$"Payroll.PayslipReleaseEnabled={payslipRelease}; V72 must never release employee payslips.", now);
        var boundary=string.Equals(c["Payroll:OfficialResultSource"],"BRAVO",StringComparison.OrdinalIgnoreCase)&&!c.GetValue<bool>("Payroll:ShadowEngineEnabled"); AddCheck(run.Id,"PAYROLL_BOUNDARY_PRESERVED",boundary?"PASS":"FAIL","BRAVO remains official Phase 1-2; Shadow remains validation-only and formula engine stays disabled until approved rules exist.", now);
        var variances=await db.PayrollParallelVariances.AsNoTracking().Where(x=>x.PayrollParallelRunId==runId).ToListAsync(ct);
        var varianceIds=variances.Select(x=>x.Id).ToArray();
        var resolutions=varianceIds.Length==0?new List<PayrollParallelVarianceResolution>():await db.PayrollParallelVarianceResolutions.AsNoTracking().Where(x=>varianceIds.Contains(x.PayrollParallelVarianceId)).ToListAsync(ct);
        var resolvedIds=resolutions.Select(x=>x.PayrollParallelVarianceId).ToHashSet();
        var unresolved=variances.Count(x=>!resolvedIds.Contains(x.Id));
        var blocking=resolutions.Count(x=>BlockingDispositions.Contains(x.Disposition));
        AddCheck(run.Id,"ALL_VARIANCES_DISPOSITIONED",unresolved==0?"PASS":"WARN",$"Variances={variances.Count}; unresolved={unresolved}.", now);
        AddCheck(run.Id,"BLOCKING_VARIANCES_NONE",blocking==0?"PASS":"FAIL",$"Blocking dispositions={blocking}. Corrections require a new V72 iteration; evidence is not overwritten.", now);
        await db.SaveChangesAsync(ct);
        var latest = await LatestChecks(runId, ct);
        run.Status = latest.Values.Any(x=>x.Status=="FAIL")?"FAIL":latest.Values.Any(x=>x.Status=="WARN")?"WARN":"PASS";
        audit.Record("V72_PARALLEL_RUN_EVALUATED","PayrollParallelRun",run.Id.ToString(),new{run.Status,varianceCount=variances.Count,unresolved,blocking});
        await db.SaveChangesAsync(ct);
        return await GetAsync(runId,ct);
    }

    private async Task CreateComparisonEvidence(PayrollParallelRun run, List<PayrollParallelOfficialResult> official, List<PayrollParallelShadowResult> shadow, CancellationToken ct)
    {
        var tolerance=c.GetValue<decimal?>("Payroll:VarianceTolerance")??0.01m;
        var shadowBy=shadow.ToDictionary(x=>x.EmployeeId);
        foreach(var o in official.OrderBy(x=>x.StaffCode,StringComparer.OrdinalIgnoreCase))
        {
            var sh=shadowBy[o.EmployeeId];
            var om=ParseComponents(o.ComponentsJson); var sm=ParseComponents(sh.ComponentsJson);
            var codes=om.Keys.Union(sm.Keys,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
            var snapshot=new PayrollParallelSnapshot{Id=Guid.NewGuid(),PayrollParallelRunId=run.Id,EmployeeId=o.EmployeeId,StaffCode=o.StaffCode,OfficialResultId=o.Id,ShadowResultId=sh.Id,OfficialComponentsSha256=o.ComponentsSha256,ShadowComponentsSha256=sh.ComponentsSha256,CreatedAt=DateTimeOffset.UtcNow};
            foreach(var code in codes)
            {
                var hasO=om.TryGetValue(code,out var oa); var hasS=sm.TryGetValue(code,out var sa); decimal? variance=hasO&&hasS?sa-oa:null;
                var match=hasO&&hasS&&Math.Abs(variance!.Value)<=tolerance;
                if(match){snapshot.MatchCount++;continue;}
                snapshot.ReviewCount++;
                db.PayrollParallelVariances.Add(new PayrollParallelVariance{Id=Guid.NewGuid(),PayrollParallelRunId=run.Id,SnapshotId=snapshot.Id,EmployeeId=o.EmployeeId,StaffCode=o.StaffCode,ComponentCode=code,OfficialAmount=hasO?oa:null,ShadowAmount=hasS?sa:null,Variance=variance,ReasonCode=!hasO?"SHADOW_ONLY":!hasS?"OFFICIAL_ONLY":"VALUE_DIFFERENCE",CreatedAt=DateTimeOffset.UtcNow});
            }
            snapshot.Status=snapshot.ReviewCount==0?"MATCH":"REVIEW"; db.PayrollParallelSnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<V72PayrollParallelVarianceDto> ResolveVarianceAsync(Guid runId, Guid varianceId, ResolveV72VarianceRequest request, CancellationToken ct)
    {
        RequirePilot(); RequirePayrollActor();
        var run=await MutableRun(runId,ct); if(run.Status=="COMPLETED")throw new InvalidOperationException("V72_RUN_COMPLETED");
        var disposition=(request.Disposition??string.Empty).Trim().ToUpperInvariant(); if(!AllDispositions.Contains(disposition))throw new InvalidOperationException("V72_INVALID_DISPOSITION");
        if(string.IsNullOrWhiteSpace(request.ResolutionNote)||string.IsNullOrWhiteSpace(request.EvidenceReference))throw new InvalidOperationException("V72_RESOLUTION_EVIDENCE_REQUIRED");
        var variance=await db.PayrollParallelVariances.SingleOrDefaultAsync(x=>x.Id==varianceId&&x.PayrollParallelRunId==runId,ct)??throw new InvalidOperationException("V72_VARIANCE_NOT_FOUND");
        if(await db.PayrollParallelVarianceResolutions.AnyAsync(x=>x.PayrollParallelVarianceId==varianceId,ct))throw new InvalidOperationException("V72_VARIANCE_ALREADY_RESOLVED");
        var resolution=new PayrollParallelVarianceResolution{Id=Guid.NewGuid(),PayrollParallelVarianceId=varianceId,Disposition=disposition,ResolutionNote=CleanText(request.ResolutionNote,2000),EvidenceReference=CleanText(request.EvidenceReference,500),ResolvedAt=DateTimeOffset.UtcNow,ResolvedBy=Actor};
        db.PayrollParallelVarianceResolutions.Add(resolution); audit.Record("V72_VARIANCE_RESOLVED","PayrollParallelVariance",varianceId.ToString(),new{disposition,evidenceReference=CleanText(request.EvidenceReference,200)}); await db.SaveChangesAsync(ct);
        return ToVarianceDto(variance,resolution);
    }

    public async Task<V72PayrollParallelRunDto> CompleteAsync(Guid runId, CompleteV72PayrollParallelRunRequest request, CancellationToken ct)
    {
        RequirePilot(); RequirePayrollActor(); if(string.IsNullOrWhiteSpace(request.Reason))throw new InvalidOperationException("AUDIT_REASON_REQUIRED");
        var run=await MutableRun(runId,ct); if(run.Status=="COMPLETED")return await GetAsync(runId,ct);
        await EvaluateAsync(runId,ct); run=await MutableRun(runId,ct);
        var checks=await LatestChecks(runId,ct); var mandatory=new[]{"ACTIVE_POPULATION_140","OFFICIAL_RESULTS_140","SHADOW_RESULTS_140","EMPLOYEE_SET_MATCH","OFFICIAL_SOURCE_BRAVO","SHADOW_VALIDATION_ONLY","PRODUCTION_LIVE_FALSE","PAYSLIP_RELEASE_DISABLED","PAYROLL_BOUNDARY_PRESERVED","ALL_VARIANCES_DISPOSITIONED","BLOCKING_VARIANCES_NONE"};
        if(mandatory.Any(code=>!checks.TryGetValue(code,out var check)||check.Status!="PASS"))throw new InvalidOperationException("V72_MANDATORY_PARALLEL_CHECKS_NOT_PASS");
        run.Status="COMPLETED";run.CompletedAt=DateTimeOffset.UtcNow;run.CompletionNote=CleanText(request.Reason,1000);audit.Record("V72_PARALLEL_RUN_COMPLETED","PayrollParallelRun",run.Id.ToString(),new{run.PayrollPeriod,run.IterationNo,request.Reason,productionLive=false,nextGate="V73"});await db.SaveChangesAsync(ct);return await GetAsync(runId,ct);
    }

    public async Task<IReadOnlyList<V72PayrollParallelVarianceDto>> GetVariancesAsync(Guid runId, CancellationToken ct)
    {
        RequirePayrollActor();
        var vars=await db.PayrollParallelVariances.AsNoTracking().Where(x=>x.PayrollParallelRunId==runId).OrderBy(x=>x.StaffCode).ThenBy(x=>x.ComponentCode).ToListAsync(ct);
        var ids=vars.Select(x=>x.Id).ToArray(); var res=ids.Length==0?new List<PayrollParallelVarianceResolution>():await db.PayrollParallelVarianceResolutions.AsNoTracking().Where(x=>ids.Contains(x.PayrollParallelVarianceId)).ToListAsync(ct); var map=res.ToDictionary(x=>x.PayrollParallelVarianceId);
        return vars.Select(v=>ToVarianceDto(v,map.GetValueOrDefault(v.Id))).ToList();
    }

    public async Task<V72PayrollParallelRunDto> GetAsync(Guid runId, CancellationToken ct)
    {
        RequirePayrollActor();
        var run=await db.PayrollParallelRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId,ct)??throw new InvalidOperationException("V72_RUN_NOT_FOUND");
        var official=await db.PayrollParallelOfficialResults.AsNoTracking().CountAsync(x=>x.PayrollParallelRunId==runId,ct); var shadow=await db.PayrollParallelShadowResults.AsNoTracking().CountAsync(x=>x.PayrollParallelRunId==runId,ct); var snapshots=await db.PayrollParallelSnapshots.AsNoTracking().CountAsync(x=>x.PayrollParallelRunId==runId,ct); var variances=await db.PayrollParallelVariances.AsNoTracking().CountAsync(x=>x.PayrollParallelRunId==runId,ct);
        var varIds=await db.PayrollParallelVariances.AsNoTracking().Where(x=>x.PayrollParallelRunId==runId).Select(x=>x.Id).ToListAsync(ct); var resolved=varIds.Count==0?0:await db.PayrollParallelVarianceResolutions.AsNoTracking().CountAsync(x=>varIds.Contains(x.PayrollParallelVarianceId),ct);
        var checks=(await LatestChecks(runId,ct)).Values.OrderBy(x=>x.CheckCode).Select(x=>new V72PayrollParallelCheckDto(x.CheckCode,x.Status,x.Summary,x.CheckedAt)).ToList();
        return new V72PayrollParallelRunDto(run.Id,run.PayrollPeriod,run.IterationNo,run.ExpectedPopulation,run.OfficialSourceSystem,run.OfficialSourceRunId,run.ShadowRuleSnapshotId,run.V70RuntimeGateStatus,run.V71HrPilotGateStatus,run.Status,run.StartedAt,run.CompletedAt,run.CompletionNote,official,shadow,snapshots,variances,Math.Max(0,variances-resolved),checks);
    }

    private async Task<Dictionary<string,PayrollParallelCheck>> LatestChecks(Guid runId,CancellationToken ct)=>(await db.PayrollParallelChecks.AsNoTracking().Where(x=>x.PayrollParallelRunId==runId).OrderBy(x=>x.CheckedAt).ToListAsync(ct)).GroupBy(x=>x.CheckCode,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.Last(),StringComparer.OrdinalIgnoreCase);
    private async Task<PayrollParallelRun> MutableRun(Guid runId,CancellationToken ct)=>await db.PayrollParallelRuns.SingleOrDefaultAsync(x=>x.Id==runId,ct)??throw new InvalidOperationException("V72_RUN_NOT_FOUND");
    private void AddCheck(Guid runId,string code,string status,string summary,DateTimeOffset now)=>db.PayrollParallelChecks.Add(new PayrollParallelCheck{Id=Guid.NewGuid(),PayrollParallelRunId=runId,CheckCode=code,Status=status,Summary=summary,CheckedAt=now,CheckedBy=Actor});
    private string Actor=>current.StaffCode??current.EntraObjectId??"SYSTEM";
    private void RequirePayrollActor(){if(!current.IsInRole(Roles.Payroll))throw new InvalidOperationException("V72_PAYROLL_ROLE_REQUIRED");}
    private void RequirePilot(){if(!c.GetValue<bool>("Pilot:Enabled"))throw new InvalidOperationException("PILOT_DISABLED");}
    private static string CleanCode(string? v)=>(v??string.Empty).Trim().ToUpperInvariant();
    private static string CleanText(string v,int max){var x=v.Trim();return x[..Math.Min(max,x.Length)];}
    private static SortedDictionary<string,decimal> NormalizeComponents(IReadOnlyDictionary<string,decimal>? input){if(input is null||input.Count==0)throw new InvalidOperationException("V72_EMPTY_COMPONENTS");var map=new SortedDictionary<string,decimal>(StringComparer.OrdinalIgnoreCase);foreach(var kv in input){var k=(kv.Key??string.Empty).Trim().ToUpperInvariant();if(string.IsNullOrWhiteSpace(k)||k.Length>80)throw new InvalidOperationException("V72_INVALID_COMPONENT_CODE");if(!map.TryAdd(k,kv.Value))throw new InvalidOperationException("V72_DUPLICATE_COMPONENT_CODE");}return map;}
    private static Dictionary<string,decimal> ParseComponents(string json)=>JsonSerializer.Deserialize<Dictionary<string,decimal>>(json)??throw new InvalidOperationException("V72_INVALID_COMPONENT_JSON");
    private static string Sha256(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static V72PayrollParallelVarianceDto ToVarianceDto(PayrollParallelVariance v,PayrollParallelVarianceResolution? r)=>new(v.Id,v.StaffCode,v.ComponentCode,v.OfficialAmount,v.ShadowAmount,v.Variance,v.ReasonCode,r?.Disposition,r?.ResolutionNote,r?.EvidenceReference);
}
