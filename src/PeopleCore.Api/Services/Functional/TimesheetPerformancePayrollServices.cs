using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services.Functional;

public sealed class TimesheetService(PeopleCoreDbContext db, IConfiguration c, ICurrentUser current, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<TimesheetEntryDto> CreateMineAsync(CreateTimesheetEntryRequest request, CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid employeeId) throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        if(request.Minutes<=0||request.Minutes>1440) throw new InvalidOperationException("TIMESHEET_MINUTES_INVALID");
        var code=(request.ProjectCode??string.Empty).Trim().ToUpperInvariant(); if(code.Length==0)throw new InvalidOperationException("TIMESHEET_PROJECT_CODE_REQUIRED");
        var project=await db.ProjectCodes.AsNoTracking().SingleOrDefaultAsync(x=>x.Code==code,ct)??throw new InvalidOperationException("TIMESHEET_PROJECT_CODE_NOT_FOUND");
        if(!ProjectValid(project,request.WorkDate)) throw new InvalidOperationException("TIMESHEET_PROJECT_CODE_INACTIVE");
        var rule=GetRule();
        if(request.WorkDate>=rule.EffectiveFrom)
        {
            var dayUsed=await db.TimesheetEntries.AsNoTracking().Where(x=>x.EmployeeId==employeeId&&x.WorkDate==request.WorkDate&&x.Status!="VOID").SumAsync(x=>(int?)x.Minutes,ct)??0;
            if(dayUsed+request.Minutes>rule.StandardDayMinutes) throw new InvalidOperationException("TIMESHEET_STANDARD_DAY_EXCEEDED_USE_OT_WORKFLOW");
            var monday=StartOfWeek(request.WorkDate);var sunday=monday.AddDays(6);
            var weekUsed=await db.TimesheetEntries.AsNoTracking().Where(x=>x.EmployeeId==employeeId&&x.WorkDate>=monday&&x.WorkDate<=sunday&&x.Status!="VOID").SumAsync(x=>(int?)x.Minutes,ct)??0;
            if(weekUsed+request.Minutes>rule.StandardWeekMinutes) throw new InvalidOperationException("TIMESHEET_STANDARD_WEEK_EXCEEDED_USE_OT_WORKFLOW");
        }
        var entity=new TimesheetEntry{Id=Guid.NewGuid(),EmployeeId=employeeId,WorkDate=request.WorkDate,ProjectCode=code,Minutes=request.Minutes,Description=CleanNullable(request.Description,500),Status="SUBMITTED",CreatedAt=DateTimeOffset.UtcNow,CreatedBy=Actor};
        db.TimesheetEntries.Add(entity);
        var evidenceRef=evidence.Record("V73-TIME-01","TIMESHEET","ENTRY_ACCEPTED",employeeId,entity.Id,"PASS",new{entity.WorkDate,entity.ProjectCode,entity.Minutes,project.SourceRevision});
        audit.Record("TIMESHEET_SUBMITTED","TimesheetEntry",entity.Id.ToString(),new{employeeId,entity.WorkDate,entity.ProjectCode,entity.Minutes,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(entity);
    }

    public async Task<TimesheetValidationDto> ValidateProjectAsync(ValidateTimesheetProjectRequest request,CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid employeeId)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        var code=(request.ProjectCode??string.Empty).Trim().ToUpperInvariant();var project=await db.ProjectCodes.AsNoTracking().SingleOrDefaultAsync(x=>x.Code==code,ct);var valid=project is not null&&ProjectValid(project,request.WorkDate);
        string? evidenceRef=null;
        if(!valid){evidenceRef=evidence.Record("V73-TIME-02","TIMESHEET","INVALID_PROJECT_BLOCK_CONFIRMED",employeeId,null,"PASS",new{request.WorkDate,ProjectCode=code,Reason=project is null?"NOT_FOUND":"INACTIVE_OR_OUTSIDE_VALIDITY"});await db.SaveChangesAsync(ct);}
        return new TimesheetValidationDto(valid,valid?"VALID":"BLOCKED",valid?"Project code is active for the requested date.":"Project code is invalid/inactive for the requested date and must be blocked.",evidenceRef);
    }

    public async Task<WorkRuleDto> GetWorkRuleWithEvidenceAsync(CancellationToken ct=default)
    {
        var rule=GetRule();
        evidence.Record("V73-TIME-03","TIMESHEET","WORK_RULE_VERIFIED",current.EmployeeId,null,"PASS",new{rule.EffectiveFrom,rule.StandardDayMinutes,rule.StandardWeekMinutes});await db.SaveChangesAsync(ct);return rule;
    }

    public async Task<List<TimesheetEntryDto>> GetMineAsync(DateOnly? from,DateOnly? to,CancellationToken ct=default)
    {if(current.EmployeeId is not Guid id)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");var q=db.TimesheetEntries.AsNoTracking().Where(x=>x.EmployeeId==id&&x.Status!="VOID");if(from is not null)q=q.Where(x=>x.WorkDate>=from);if(to is not null)q=q.Where(x=>x.WorkDate<=to);var rows=await q.OrderByDescending(x=>x.WorkDate).ThenBy(x=>x.ProjectCode).ToListAsync(ct);return rows.Select(ToDto).ToList();}

    private WorkRuleDto GetRule(){var raw=c["WorkRules:EffectiveFrom"]??"2026-07-01";if(!DateOnly.TryParse(raw,out var effective))throw new InvalidOperationException("WORK_RULE_EFFECTIVE_DATE_INVALID");var day=c.GetValue("WorkRules:StandardDayMinutes",480);var week=c.GetValue("WorkRules:StandardWeekMinutes",2400);if(day<=0||week<day)throw new InvalidOperationException("WORK_RULE_CONFIGURATION_INVALID");return new WorkRuleDto(effective,day,week);}
    private static bool ProjectValid(ProjectCode p,DateOnly date)=>p.Status.Equals("ACTIVE",StringComparison.OrdinalIgnoreCase)&&(p.ValidFrom is null||p.ValidFrom<=date)&&(p.ValidTo is null||p.ValidTo>=date);
    private static DateOnly StartOfWeek(DateOnly d){var delta=((int)d.DayOfWeek+6)%7;return d.AddDays(-delta);}
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";
    private static TimesheetEntryDto ToDto(TimesheetEntry x)=>new(x.Id,x.EmployeeId,x.WorkDate,x.ProjectCode,x.Minutes,x.Description,x.Status,x.CreatedAt);
    private static string? CleanNullable(string? x,int max){var s=(x??string.Empty).Trim();if(s.Length==0)return null;return s.Length<=max?s:s[..max];}
}

public sealed class PerformanceService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<PerformancePeriodDto> CreatePeriodAsync(CreatePerformancePeriodRequest request,CancellationToken ct=default)
    {
        if(!current.IsInRole(Roles.Hr))throw new UnauthorizedAccessException("HR_ROLE_REQUIRED");
        var code=Clean(request.Code,80).ToUpperInvariant();var name=Clean(request.Name,200);if(code.Length==0||name.Length==0||request.EndDate<request.StartDate)throw new InvalidOperationException("PERFORMANCE_PERIOD_INVALID");
        if(await db.PerformancePeriods.AnyAsync(x=>x.Code==code,ct))throw new InvalidOperationException("PERFORMANCE_PERIOD_EXISTS");
        var entity=new PerformancePeriod{Id=Guid.NewGuid(),Code=code,Name=name,StartDate=request.StartDate,EndDate=request.EndDate,Status="OPEN",CreatedAt=DateTimeOffset.UtcNow,CreatedBy=Actor};db.PerformancePeriods.Add(entity);audit.Record("PERFORMANCE_PERIOD_CREATED","PerformancePeriod",entity.Id.ToString(),new{entity.Code,entity.StartDate,entity.EndDate});await db.SaveChangesAsync(ct);return ToDto(entity);
    }

    public async Task<PerformanceReviewDto> SubmitSelfAsync(Guid periodId,SubmitPerformanceSelfRequest request,CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid employeeId)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");var period=await db.PerformancePeriods.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==periodId,ct)??throw new InvalidOperationException("PERFORMANCE_PERIOD_NOT_FOUND");if(period.Status!="OPEN")throw new InvalidOperationException("PERFORMANCE_PERIOD_CLOSED");var text=Clean(request.SelfText,6000);if(text.Length==0)throw new InvalidOperationException("PERFORMANCE_SELF_TEXT_REQUIRED");
        var review=await db.PerformanceReviews.SingleOrDefaultAsync(x=>x.PerformancePeriodId==periodId&&x.EmployeeId==employeeId,ct);if(review is null){review=new PerformanceReview{Id=Guid.NewGuid(),PerformancePeriodId=periodId,EmployeeId=employeeId,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow};db.PerformanceReviews.Add(review);}review.SelfText=text;review.SelfSubmittedAt=DateTimeOffset.UtcNow;review.Status="SELF_SUBMITTED";review.UpdatedAt=DateTimeOffset.UtcNow;var evidenceRef=evidence.Record("V73-PERF-01","PERFORMANCE","SELF_SUBMITTED",employeeId,review.Id,"PASS",new{period.Code,SubmittedAt=review.SelfSubmittedAt});audit.Record("PERFORMANCE_SELF_SUBMITTED","PerformanceReview",review.Id.ToString(),new{periodId,employeeId,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(review);
    }

    public async Task<PerformanceReviewDto> ManagerReviewAsync(Guid reviewId,SubmitPerformanceManagerRequest request,CancellationToken ct=default)
    {
        var review=await db.PerformanceReviews.SingleOrDefaultAsync(x=>x.Id==reviewId,ct)??throw new InvalidOperationException("PERFORMANCE_REVIEW_NOT_FOUND");if(review.Status!="SELF_SUBMITTED")throw new InvalidOperationException("PERFORMANCE_SELF_SUBMISSION_REQUIRED");var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==review.EmployeeId,ct);if(!await access.CanManagerActOnAsync(employee,ct))throw new UnauthorizedAccessException("PERFORMANCE_MANAGER_SCOPE_DENIED");var text=Clean(request.ManagerText,6000);if(text.Length==0)throw new InvalidOperationException("PERFORMANCE_MANAGER_TEXT_REQUIRED");review.ManagerEmployeeId=current.EmployeeId;review.ManagerText=text;review.ManagerReviewedAt=DateTimeOffset.UtcNow;review.Status="MANAGER_REVIEWED";review.UpdatedAt=DateTimeOffset.UtcNow;var evidenceRef=evidence.Record("V73-PERF-02","PERFORMANCE","MANAGER_REVIEWED",review.EmployeeId,review.Id,"PASS",new{ManagerEmployeeId=current.EmployeeId,review.PerformancePeriodId,review.ManagerReviewedAt});audit.Record("PERFORMANCE_MANAGER_REVIEWED","PerformanceReview",review.Id.ToString(),new{review.EmployeeId,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(review);
    }

    public async Task<List<PerformancePeriodDto>> GetPeriodsAsync(CancellationToken ct=default){var rows=await db.PerformancePeriods.AsNoTracking().OrderByDescending(x=>x.StartDate).Take(100).ToListAsync(ct);return rows.Select(ToDto).ToList();}

    public async Task<List<PerformanceReviewDto>> GetManagerPendingAsync(CancellationToken ct=default){if(!current.IsInRole(Roles.Manager))throw new UnauthorizedAccessException("MANAGER_ROLE_REQUIRED");var rows=await db.PerformanceReviews.AsNoTracking().Where(x=>x.Status=="SELF_SUBMITTED").OrderBy(x=>x.SelfSubmittedAt).Take(200).ToListAsync(ct);var result=new List<PerformanceReviewDto>();foreach(var row in rows){var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==row.EmployeeId,ct);if(await access.CanManagerActOnAsync(employee,ct))result.Add(ToDto(row));}return result;}

    public async Task<PerformanceReviewDto?> GetMineAsync(Guid periodId,CancellationToken ct=default){if(current.EmployeeId is not Guid id)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");var x=await db.PerformanceReviews.AsNoTracking().SingleOrDefaultAsync(r=>r.PerformancePeriodId==periodId&&r.EmployeeId==id,ct);return x is null?null:ToDto(x);}
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";private static PerformancePeriodDto ToDto(PerformancePeriod x)=>new(x.Id,x.Code,x.Name,x.StartDate,x.EndDate,x.Status);private static PerformanceReviewDto ToDto(PerformanceReview x)=>new(x.Id,x.PerformancePeriodId,x.EmployeeId,x.SelfText,x.SelfSubmittedAt,x.ManagerEmployeeId,x.ManagerText,x.ManagerReviewedAt,x.Status);private static string Clean(string? x,int max){var s=(x??string.Empty).Trim();return s.Length<=max?s:s[..max];}
}

public sealed class TaxInsuranceService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    private static readonly Regex PeriodRx=new("^[0-9]{4}-(0[1-9]|1[0-2])$",RegexOptions.Compiled);
    public async Task<TaxInsuranceSnapshotDto> ImportAsync(ImportTaxInsuranceSnapshotRequest request,CancellationToken ct=default)
    {
        if(!current.IsInRole(Roles.Payroll))throw new UnauthorizedAccessException("PAYROLL_ROLE_REQUIRED");var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.EmployeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");if(!await access.CanPayrollActOnAsync(employee,ct))throw new UnauthorizedAccessException("PAYROLL_SCOPE_DENIED");if(!PeriodRx.IsMatch(request.PayrollPeriod??string.Empty))throw new InvalidOperationException("PAYROLL_PERIOD_INVALID");if(request.IterationNo<=0)throw new InvalidOperationException("TAX_INSURANCE_ITERATION_INVALID");var source=Clean(request.SourceSystem,80).ToUpperInvariant();if(source!="BRAVO")throw new InvalidOperationException("TAX_INSURANCE_OFFICIAL_SOURCE_MUST_BE_BRAVO");var sourceRef=Clean(request.SourceReference,300);var rule=Clean(request.RuleSnapshotId,200);if(sourceRef.Length==0||rule.Length==0)throw new InvalidOperationException("TAX_INSURANCE_SOURCE_EVIDENCE_REQUIRED");var outputs=string.IsNullOrWhiteSpace(request.OutputsJson)?"{}":request.OutputsJson!;try{JsonDocument.Parse(outputs);}catch(JsonException){throw new InvalidOperationException("TAX_INSURANCE_OUTPUTS_JSON_INVALID");}
        var existing=await db.TaxInsuranceSnapshots.AsNoTracking().AnyAsync(x=>x.EmployeeId==request.EmployeeId&&x.PayrollPeriod==request.PayrollPeriod&&x.IterationNo==request.IterationNo,ct);if(existing)throw new InvalidOperationException("TAX_INSURANCE_ITERATION_ALREADY_EXISTS");var entity=new TaxInsuranceSnapshot{Id=Guid.NewGuid(),EmployeeId=request.EmployeeId,PayrollPeriod=request.PayrollPeriod,IterationNo=request.IterationNo,SourceSystem=source,SourceReference=sourceRef,RuleSnapshotId=rule,InsuranceBaseA=request.InsuranceBaseA,TaxWithheld=request.TaxWithheld,EmployeeInsuranceAmount=request.EmployeeInsuranceAmount,EmployerInsuranceAmount=request.EmployerInsuranceAmount,OutputsJson=outputs,ImportedAt=DateTimeOffset.UtcNow,ImportedBy=Actor,ApprovedAt=request.Approved?DateTimeOffset.UtcNow:null,ApprovedBy=request.Approved?Actor:null};db.TaxInsuranceSnapshots.Add(entity);var evidenceRef=evidence.Record("V73-TAX-01","TAX_INSURANCE","SOURCE_SNAPSHOT_IMPORTED",request.EmployeeId,entity.Id,"PASS",new{employee.StaffCode,entity.PayrollPeriod,entity.IterationNo,entity.SourceSystem,entity.SourceReference,entity.RuleSnapshotId,entity.InsuranceBaseA,Approved=request.Approved});audit.Record("TAX_INSURANCE_SNAPSHOT_IMPORTED","TaxInsuranceSnapshot",entity.Id.ToString(),new{entity.EmployeeId,entity.PayrollPeriod,entity.IterationNo,entity.SourceSystem,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(entity);
    }
    public async Task<TaxInsuranceSnapshotDto?> GetMineAsync(string payrollPeriod,CancellationToken ct=default){if(current.EmployeeId is not Guid id)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");var x=await db.TaxInsuranceSnapshots.AsNoTracking().Where(r=>r.EmployeeId==id&&r.PayrollPeriod==payrollPeriod&&r.ApprovedAt!=null).OrderByDescending(r=>r.IterationNo).FirstOrDefaultAsync(ct);return x is null?null:ToDto(x);}
    public async Task<TaxInsuranceSnapshotDto?> GetForPayrollAsync(Guid employeeId,string payrollPeriod,CancellationToken ct=default){var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==employeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");if(!await access.CanPayrollActOnAsync(e,ct))throw new UnauthorizedAccessException("PAYROLL_SCOPE_DENIED");var x=await db.TaxInsuranceSnapshots.AsNoTracking().Where(r=>r.EmployeeId==employeeId&&r.PayrollPeriod==payrollPeriod).OrderByDescending(r=>r.IterationNo).FirstOrDefaultAsync(ct);return x is null?null:ToDto(x);}
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";private static TaxInsuranceSnapshotDto ToDto(TaxInsuranceSnapshot x)=>new(x.Id,x.EmployeeId,x.PayrollPeriod,x.IterationNo,x.SourceSystem,x.SourceReference,x.RuleSnapshotId,x.InsuranceBaseA,x.TaxWithheld,x.EmployeeInsuranceAmount,x.EmployerInsuranceAmount,x.ImportedAt,x.ApprovedAt);private static string Clean(string? x,int max){var s=(x??string.Empty).Trim();return s.Length<=max?s:s[..max];}
}

public sealed class PayslipService(PeopleCoreDbContext db,IConfiguration c,ICurrentUser current,IAccessControlService access,IAuditService audit,FunctionalEvidenceService evidence)
{
    public async Task<PayslipPreviewDto> PreviewAsync(Guid employeeId,string payrollPeriod,CancellationToken ct=default)
    {
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==employeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");if(!current.IsInRole(Roles.Payroll)||!await access.CanPayrollActOnAsync(employee,ct))throw new UnauthorizedAccessException("PAYSLIP_PREVIEW_SCOPE_DENIED");var official=await ApprovedOfficial(employeeId,payrollPeriod,ct);await EnsureNoBlockingVariance(employeeId,payrollPeriod,ct);var components=Parse(official.ComponentsJson);var refId=evidence.Record("V73-PAYS-01","PAYSLIP","OFFICIAL_PREVIEW",employeeId,official.Id,"PASS",new{employee.StaffCode,payrollPeriod,official.SourceSystem,official.SourceRunId,official.ApprovedAt});audit.Record("PAYSLIP_PREVIEWED","PayrollOfficialResult",official.Id.ToString(),new{employeeId,payrollPeriod,refId});await db.SaveChangesAsync(ct);return new PayslipPreviewDto(employeeId,payrollPeriod,official.Id,official.SourceSystem,official.SourceRunId,components,refId,c.GetValue<bool>("Payroll:PayslipReleaseEnabled"));
    }

    public async Task<TimesheetValidationDto> SafetyCheckAsync(Guid employeeId,string payrollPeriod,CancellationToken ct=default)
    {
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==employeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        if(!current.IsInRole(Roles.Payroll)||!await access.CanPayrollActOnAsync(employee,ct))throw new UnauthorizedAccessException("PAYSLIP_SAFETY_SCOPE_DENIED");
        var productionLive=c.GetValue<bool>("Product:ProductionLive");var releaseEnabled=c.GetValue<bool>("Payroll:PayslipReleaseEnabled");var safe=!productionLive&&!releaseEnabled;var refId=evidence.Record("V73-PAYS-02","PAYSLIP","PRELIVE_RELEASE_GUARD",employeeId,null,safe?"PASS":"FAIL",new{payrollPeriod,productionLive,releaseEnabled});await db.SaveChangesAsync(ct);return new TimesheetValidationDto(safe,safe?"BLOCKED_AS_REQUIRED":"UNSAFE","Pre-live payslip release must remain disabled.",refId);
    }

    public async Task<PayslipDto> ReleaseAsync(Guid employeeId,string payrollPeriod,CancellationToken ct=default)
    {
        if(!c.GetValue<bool>("Product:ProductionLive")||!c.GetValue<bool>("Payroll:PayslipReleaseEnabled"))throw new InvalidOperationException("PAYSLIP_RELEASE_NOT_AUTHORIZED_BY_RUNTIME_FLAGS");var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==employeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");if(!current.IsInRole(Roles.Payroll)||!await access.CanPayrollActOnAsync(employee,ct))throw new UnauthorizedAccessException("PAYSLIP_RELEASE_SCOPE_DENIED");var cutover=await db.ProductionCutoverRuns.AsNoTracking().OrderByDescending(x=>x.StartedAt).FirstOrDefaultAsync(ct);if(cutover?.Status!="COMPLETED")throw new InvalidOperationException("PAYSLIP_RELEASE_REQUIRES_COMPLETED_V74_CUTOVER");var official=await ApprovedOfficial(employeeId,payrollPeriod,ct);await EnsureNoBlockingVariance(employeeId,payrollPeriod,ct);if(await db.PayslipReleases.AnyAsync(x=>x.EmployeeId==employeeId&&x.PayrollPeriod==payrollPeriod,ct))throw new InvalidOperationException("PAYSLIP_ALREADY_RELEASED");var release=new PayslipRelease{Id=Guid.NewGuid(),EmployeeId=employeeId,PayrollPeriod=payrollPeriod,OfficialPayrollResultId=official.Id,SourceSystem="BRAVO",SourceRunId=official.SourceRunId,ReleasedAt=DateTimeOffset.UtcNow,ReleasedBy=Actor};db.PayslipReleases.Add(release);audit.Record("PAYSLIP_RELEASED","PayslipRelease",release.Id.ToString(),new{employeeId,payrollPeriod,official.SourceRunId});await db.SaveChangesAsync(ct);return new PayslipDto(employeeId,payrollPeriod,official.SourceSystem,official.SourceRunId,Parse(official.ComponentsJson),release.ReleasedAt);
    }

    public async Task<PayslipDto?> GetMineAsync(string payrollPeriod,CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid employeeId)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");if(!c.GetValue<bool>("Product:ProductionLive")||!c.GetValue<bool>("Payroll:PayslipReleaseEnabled"))throw new InvalidOperationException("PAYSLIP_RELEASE_NOT_LIVE");var release=await db.PayslipReleases.AsNoTracking().SingleOrDefaultAsync(x=>x.EmployeeId==employeeId&&x.PayrollPeriod==payrollPeriod,ct);if(release is null)return null;var official=await db.PayrollOfficialResults.AsNoTracking().SingleAsync(x=>x.Id==release.OfficialPayrollResultId,ct);return new PayslipDto(employeeId,payrollPeriod,official.SourceSystem,official.SourceRunId,Parse(official.ComponentsJson),release.ReleasedAt);
    }

    private async Task<PayrollOfficialResult> ApprovedOfficial(Guid employeeId,string payrollPeriod,CancellationToken ct){var x=await db.PayrollOfficialResults.AsNoTracking().SingleOrDefaultAsync(r=>r.EmployeeId==employeeId&&r.PayrollPeriod==payrollPeriod,ct)??throw new InvalidOperationException("OFFICIAL_BRAVO_PAYROLL_RESULT_NOT_FOUND");if(!x.SourceSystem.Equals("BRAVO",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("OFFICIAL_PAYROLL_SOURCE_NOT_BRAVO");if(x.ApprovedAt is null)throw new InvalidOperationException("OFFICIAL_BRAVO_PAYROLL_RESULT_NOT_APPROVED");return x;}
    private async Task EnsureNoBlockingVariance(Guid employeeId,string payrollPeriod,CancellationToken ct){if(await db.ReconciliationItems.AsNoTracking().AnyAsync(x=>x.EmployeeId==employeeId&&x.PayrollPeriod==payrollPeriod&&x.Status=="REVIEW"&&x.ResolvedAt==null,ct))throw new InvalidOperationException("PAYSLIP_BLOCKED_BY_UNRESOLVED_RECONCILIATION");var runs=await db.PayrollParallelRuns.AsNoTracking().Where(x=>x.PayrollPeriod==payrollPeriod).Select(x=>x.Id).ToListAsync(ct);if(runs.Count>0){var vars=await db.PayrollParallelVariances.AsNoTracking().Where(x=>runs.Contains(x.PayrollParallelRunId)&&x.EmployeeId==employeeId).Select(x=>x.Id).ToListAsync(ct);if(vars.Count>0){var resolved=await db.PayrollParallelVarianceResolutions.AsNoTracking().Where(x=>vars.Contains(x.PayrollParallelVarianceId)).Select(x=>x.PayrollParallelVarianceId).Distinct().ToListAsync(ct);if(vars.Except(resolved).Any())throw new InvalidOperationException("PAYSLIP_BLOCKED_BY_UNRESOLVED_V72_VARIANCE");}}}
    private static IReadOnlyDictionary<string,decimal> Parse(string json){var map=JsonSerializer.Deserialize<Dictionary<string,decimal>>(json)??new();return new Dictionary<string,decimal>(map,StringComparer.OrdinalIgnoreCase);}
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";
}
