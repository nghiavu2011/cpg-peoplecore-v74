using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Audit;

namespace PeopleCore.Api.Services.Functional;

public sealed class ContractLifecycleService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<ContractDto> CreateAsync(Guid employeeId, CreateContractRequest request, CancellationToken ct = default)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        if (!await access.CanEditEmployeeAsync(employee, ct)) throw new UnauthorizedAccessException("HR_SCOPE_DENIED");
        var contractType = Clean(request.ContractTypeCode, 80).ToUpperInvariant();
        var source = Clean(request.SourceReference, 300); var reason = Clean(request.ChangeReason, 600);
        if (contractType.Length == 0 || source.Length == 0 || reason.Length == 0) throw new InvalidOperationException("CONTRACT_REQUIRED_FIELDS_MISSING");
        if (request.EffectiveTo is not null && request.EffectiveTo < request.EffectiveFrom) throw new InvalidOperationException("CONTRACT_DATE_RANGE_INVALID");
        var overlap = await db.EmploymentContracts.AsNoTracking().AnyAsync(x => x.EmployeeId == employeeId && x.Status == "ACTIVE" && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), ct);
        if (overlap) throw new InvalidOperationException("CONTRACT_EFFECTIVE_PERIOD_OVERLAP");
        var entity = new EmploymentContract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ContractTypeCode = contractType, ContractNumber = NullableClean(request.ContractNumber, 120),
            EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Status = "ACTIVE", SourceReference = source, ChangeReason = reason,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = Actor
        };
        db.EmploymentContracts.Add(entity);
        db.EmployeeLifecycleEvents.Add(new EmployeeLifecycleEvent
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, EventType = "CONTRACT_CHANGE", EffectiveDate = request.EffectiveFrom, Reason = reason,
            SourceReference = source, ContractId = entity.Id, CreatedAt = entity.CreatedAt, CreatedBy = Actor
        });
        var evidenceRef = evidence.Record("V73-HR-03", "CONTRACT", "CONTRACT_CHANGE_RECORDED", employeeId, entity.Id, "PASS", new { employee.StaffCode, entity.ContractTypeCode, entity.EffectiveFrom, entity.EffectiveTo, entity.SourceReference });
        audit.Record("CONTRACT_CREATED", "EmploymentContract", entity.Id.ToString(), new { employeeId, entity.ContractTypeCode, entity.EffectiveFrom, entity.EffectiveTo, evidenceRef });
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<ContractDto>> GetForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        var decision = await access.DecideReadEmployeeAsync(employee, ct);
        if (!decision.Allowed || (current.EmployeeId != employeeId && !current.IsInRole(Roles.Hr))) throw new UnauthorizedAccessException("CONTRACT_READ_DENIED");
        var rows = await db.EmploymentContracts.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<EmployeeLifecycleEventDto>> GetLifecycleAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        var decision = await access.DecideReadEmployeeAsync(employee, ct);
        if (!decision.Allowed || (current.EmployeeId != employeeId && !current.IsInRole(Roles.Hr))) throw new UnauthorizedAccessException("LIFECYCLE_READ_DENIED");
        var rows = await db.EmployeeLifecycleEvents.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
        return rows.Select(x => new EmployeeLifecycleEventDto(x.Id,x.EmployeeId,x.EventType,x.EffectiveDate,x.Reason,x.SourceReference,x.ContractId,x.CreatedAt)).ToList();
    }

    private string Actor => current.StaffCode ?? current.EntraObjectId ?? "UNMAPPED";
    private static ContractDto ToDto(EmploymentContract x) => new(x.Id, x.EmployeeId, x.ContractTypeCode, x.ContractNumber, x.EffectiveFrom, x.EffectiveTo, x.Status, x.SourceReference, x.ChangeReason, x.CreatedAt);
    private static string Clean(string? x, int max) { var s=(x??string.Empty).Trim(); return s.Length<=max?s:s[..max]; }
    private static string? NullableClean(string? x, int max) { var s=Clean(x,max); return s.Length==0?null:s; }
}

public sealed class LeaveService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<LeaveRequestDto> CreateMineAsync(CreateLeaveRequest request, CancellationToken ct = default)
    {
        if (current.EmployeeId is not Guid employeeId) throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        if (request.EndDate < request.StartDate || request.RequestedHours <= 0) throw new InvalidOperationException("LEAVE_REQUEST_INVALID");
        var type = Clean(request.LeaveTypeCode, 80).ToUpperInvariant(); var reason=Clean(request.Reason,600); var policy=Clean(request.PolicyReference,300);
        if (type.Length==0 || reason.Length==0 || policy.Length==0) throw new InvalidOperationException("LEAVE_REQUIRED_FIELDS_MISSING");
        var entity = new LeaveRequest { Id=Guid.NewGuid(), EmployeeId=employeeId, LeaveTypeCode=type, StartDate=request.StartDate, EndDate=request.EndDate, RequestedHours=request.RequestedHours, Reason=reason, PolicyReference=policy, Status="PENDING", RequestedAt=DateTimeOffset.UtcNow, RequestedBy=Actor };
        db.LeaveRequests.Add(entity); audit.Record("LEAVE_REQUESTED","LeaveRequest",entity.Id.ToString(),new{employeeId,entity.LeaveTypeCode,entity.StartDate,entity.EndDate,entity.RequestedHours}); await db.SaveChangesAsync(ct); return ToDto(entity);
    }

    public async Task<LeaveRequestDto> DecideAsync(Guid id, DecideLeaveRequest request, CancellationToken ct = default)
    {
        var entity=await db.LeaveRequests.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new InvalidOperationException("LEAVE_REQUEST_NOT_FOUND");
        if(entity.Status!="PENDING") throw new InvalidOperationException("LEAVE_REQUEST_NOT_PENDING");
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==entity.EmployeeId,ct);
        var allowed=(current.IsInRole(Roles.Manager)&&await access.CanManagerActOnAsync(employee,ct)) || (current.IsInRole(Roles.Hr)&&await access.CanEditEmployeeAsync(employee,ct));
        if(!allowed) throw new UnauthorizedAccessException("LEAVE_APPROVAL_SCOPE_DENIED");
        var decision=Clean(request.Decision,20).ToUpperInvariant(); if(decision is not("APPROVE" or "REJECT")) throw new InvalidOperationException("LEAVE_DECISION_INVALID");
        var note=Clean(request.Note,600); if(note.Length==0) throw new InvalidOperationException("LEAVE_DECISION_NOTE_REQUIRED");
        entity.Status=decision=="APPROVE"?"APPROVED":"REJECTED"; entity.DecidedAt=DateTimeOffset.UtcNow; entity.DecidedBy=Actor; entity.DecisionNote=note;
        string? evidenceRef=null; if(entity.Status=="APPROVED") evidenceRef=evidence.Record("V73-LEAVE-01","LEAVE","REQUEST_APPROVED",entity.EmployeeId,entity.Id,"PASS",new{employee.StaffCode,entity.LeaveTypeCode,entity.StartDate,entity.EndDate,entity.RequestedHours,entity.PolicyReference});
        audit.Record("LEAVE_DECIDED","LeaveRequest",entity.Id.ToString(),new{entity.Status,employeeId=entity.EmployeeId,evidenceRef}); await db.SaveChangesAsync(ct); return ToDto(entity);
    }

    public Task<List<LeaveRequestDto>> GetMineAsync(CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid id) throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        return GetMineCoreAsync(id, ct);
    }

    public async Task<List<LeaveRequestDto>> GetPendingForActorAsync(CancellationToken ct=default)
    {
        if(!current.IsInRole(Roles.Manager) && !current.IsInRole(Roles.Hr)) throw new UnauthorizedAccessException("LEAVE_APPROVER_ROLE_REQUIRED");
        var pending=await db.LeaveRequests.AsNoTracking().Where(x=>x.Status=="PENDING").OrderBy(x=>x.RequestedAt).Take(200).ToListAsync(ct);
        var result=new List<LeaveRequestDto>();
        foreach(var row in pending)
        {
            var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==row.EmployeeId,ct);
            var allowed=(current.IsInRole(Roles.Manager)&&await access.CanManagerActOnAsync(employee,ct)) || (current.IsInRole(Roles.Hr)&&await access.CanEditEmployeeAsync(employee,ct));
            if(allowed)result.Add(ToDto(row));
        }
        return result;
    }

    private async Task<List<LeaveRequestDto>> GetMineCoreAsync(Guid id, CancellationToken ct)
    {
        var rows = await db.LeaveRequests.AsNoTracking().Where(x=>x.EmployeeId==id).OrderByDescending(x=>x.RequestedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";
    private static LeaveRequestDto ToDto(LeaveRequest x)=>new(x.Id,x.EmployeeId,x.LeaveTypeCode,x.StartDate,x.EndDate,x.RequestedHours,x.Reason,x.PolicyReference,x.Status,x.RequestedAt,x.DecidedAt,x.DecidedBy,x.DecisionNote);
    private static string Clean(string? x,int max){var s=(x??string.Empty).Trim();return s.Length<=max?s:s[..max];}
}

public sealed class AttendanceService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<AttendanceDayDto> UpsertAsync(Guid employeeId, DateOnly date, UpsertAttendanceRequest request, CancellationToken ct=default)
    {
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==employeeId,ct)??throw new InvalidOperationException("EMPLOYEE_NOT_FOUND");
        if(!current.IsInRole(Roles.Hr)||!await access.CanEditEmployeeAsync(employee,ct)) throw new UnauthorizedAccessException("ATTENDANCE_HR_SCOPE_DENIED");
        if(request.WorkedMinutes<0||request.WorkedMinutes>1440) throw new InvalidOperationException("ATTENDANCE_MINUTES_INVALID");
        var source=Clean(request.SourceSystem,80).ToUpperInvariant(); var reference=Clean(request.SourceReference,300); if(source.Length==0||reference.Length==0) throw new InvalidOperationException("ATTENDANCE_SOURCE_REQUIRED");
        var entity=await db.AttendanceDays.SingleOrDefaultAsync(x=>x.EmployeeId==employeeId&&x.WorkDate==date,ct);
        if(entity is null){entity=new AttendanceDay{Id=Guid.NewGuid(),EmployeeId=employeeId,WorkDate=date,WorkedMinutes=request.WorkedMinutes,SourceSystem=source,SourceReference=reference,Status=string.IsNullOrWhiteSpace(request.ExceptionCode)?"RECORDED":"EXCEPTION",ExceptionCode=NullableClean(request.ExceptionCode,80),RecordedAt=DateTimeOffset.UtcNow,RecordedBy=Actor};db.AttendanceDays.Add(entity);}else{entity.WorkedMinutes=request.WorkedMinutes;entity.SourceSystem=source;entity.SourceReference=reference;entity.ExceptionCode=NullableClean(request.ExceptionCode,80);entity.Status=entity.ExceptionCode is null?"RECORDED":"EXCEPTION";entity.RecordedAt=DateTimeOffset.UtcNow;entity.RecordedBy=Actor;}
        audit.Record("ATTENDANCE_RECORDED","AttendanceDay",entity.Id.ToString(),new{employeeId,date,request.WorkedMinutes,source});await db.SaveChangesAsync(ct);return ToDto(entity);
    }

    public async Task<AttendanceDayDto> ReviewAsync(Guid id, ReviewAttendanceRequest request, CancellationToken ct=default)
    {
        var entity=await db.AttendanceDays.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new InvalidOperationException("ATTENDANCE_NOT_FOUND");
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==entity.EmployeeId,ct);
        if(!current.IsInRole(Roles.Hr)||!await access.CanEditEmployeeAsync(employee,ct)) throw new UnauthorizedAccessException("ATTENDANCE_HR_SCOPE_DENIED");
        var decision=Clean(request.Decision,30).ToUpperInvariant(); if(decision is not("REVIEWED" or "EXCEPTION")) throw new InvalidOperationException("ATTENDANCE_REVIEW_DECISION_INVALID");
        var note=Clean(request.Note,600);if(note.Length==0)throw new InvalidOperationException("ATTENDANCE_REVIEW_NOTE_REQUIRED");
        entity.Status=decision;entity.ReviewNote=note;entity.ReviewedAt=DateTimeOffset.UtcNow;entity.ReviewedBy=Actor;
        var evidenceRef=evidence.Record("V73-ATT-01","ATTENDANCE","DAY_REVIEWED",entity.EmployeeId,entity.Id,"PASS",new{employee.StaffCode,entity.WorkDate,entity.WorkedMinutes,entity.Status,entity.SourceSystem,entity.SourceReference});
        audit.Record("ATTENDANCE_REVIEWED","AttendanceDay",entity.Id.ToString(),new{entity.Status,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(entity);
    }

    public async Task<List<AttendanceDayDto>> GetMineAsync(DateOnly? from,DateOnly? to,CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid id)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        var q=db.AttendanceDays.AsNoTracking().Where(x=>x.EmployeeId==id);if(from is not null)q=q.Where(x=>x.WorkDate>=from);if(to is not null)q=q.Where(x=>x.WorkDate<=to);var rows=await q.OrderByDescending(x=>x.WorkDate).ToListAsync(ct);return rows.Select(ToDto).ToList();
    }
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";
    private static AttendanceDayDto ToDto(AttendanceDay x)=>new(x.Id,x.EmployeeId,x.WorkDate,x.WorkedMinutes,x.SourceSystem,x.SourceReference,x.Status,x.ExceptionCode,x.ReviewNote,x.RecordedAt,x.ReviewedAt);
    private static string Clean(string? x,int max){var s=(x??string.Empty).Trim();return s.Length<=max?s:s[..max];}
    private static string? NullableClean(string? x,int max){var s=Clean(x,max);return s.Length==0?null:s;}
}

public sealed class OvertimeService(PeopleCoreDbContext db, ICurrentUser current, IAccessControlService access, IAuditService audit, FunctionalEvidenceService evidence)
{
    public async Task<OvertimeRequestDto> CreateMineAsync(CreateOvertimeRequest request,CancellationToken ct=default)
    {
        if(current.EmployeeId is not Guid employeeId)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
        if(request.RequestedMinutes<=0||request.RequestedMinutes>1440)throw new InvalidOperationException("OT_MINUTES_INVALID");
        if(!string.IsNullOrWhiteSpace(request.ProjectCode)){var code=request.ProjectCode.Trim().ToUpperInvariant();var project=await db.ProjectCodes.AsNoTracking().SingleOrDefaultAsync(x=>x.Code==code,ct)??throw new InvalidOperationException("OT_PROJECT_CODE_NOT_FOUND");if(project.Status!="ACTIVE"||(project.ValidFrom!=null&&project.ValidFrom>request.WorkDate)||(project.ValidTo!=null&&project.ValidTo<request.WorkDate))throw new InvalidOperationException("OT_PROJECT_CODE_INACTIVE");}
        var reason=Clean(request.Reason,600);if(reason.Length==0)throw new InvalidOperationException("OT_REASON_REQUIRED");
        var entity=new OvertimeRequest{Id=Guid.NewGuid(),EmployeeId=employeeId,WorkDate=request.WorkDate,RequestedMinutes=request.RequestedMinutes,ProjectCode=string.IsNullOrWhiteSpace(request.ProjectCode)?null:request.ProjectCode.Trim().ToUpperInvariant(),Reason=reason,Status="PENDING",RequestedAt=DateTimeOffset.UtcNow,RequestedBy=Actor};db.OvertimeRequests.Add(entity);audit.Record("OT_REQUESTED","OvertimeRequest",entity.Id.ToString(),new{employeeId,entity.WorkDate,entity.RequestedMinutes,entity.ProjectCode});await db.SaveChangesAsync(ct);return ToDto(entity);
    }
    public async Task<OvertimeRequestDto> DecideAsync(Guid id,DecideOvertimeRequest request,CancellationToken ct=default)
    {
        var entity=await db.OvertimeRequests.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new InvalidOperationException("OT_REQUEST_NOT_FOUND");if(entity.Status!="PENDING")throw new InvalidOperationException("OT_REQUEST_NOT_PENDING");
        var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==entity.EmployeeId,ct);var allowed=(current.IsInRole(Roles.Manager)&&await access.CanManagerActOnAsync(employee,ct))||(current.IsInRole(Roles.Hr)&&await access.CanEditEmployeeAsync(employee,ct));if(!allowed)throw new UnauthorizedAccessException("OT_APPROVAL_SCOPE_DENIED");
        var decision=Clean(request.Decision,20).ToUpperInvariant();if(decision is not("APPROVE" or "REJECT"))throw new InvalidOperationException("OT_DECISION_INVALID");var note=Clean(request.Note,600);if(note.Length==0)throw new InvalidOperationException("OT_DECISION_NOTE_REQUIRED");
        entity.Status=decision=="APPROVE"?"APPROVED":"REJECTED";entity.DecidedAt=DateTimeOffset.UtcNow;entity.DecidedBy=Actor;entity.DecisionNote=note;string? evidenceRef=null;if(entity.Status=="APPROVED")evidenceRef=evidence.Record("V73-OT-01","OT","REQUEST_APPROVED",entity.EmployeeId,entity.Id,"PASS",new{employee.StaffCode,entity.WorkDate,entity.RequestedMinutes,entity.ProjectCode});audit.Record("OT_DECIDED","OvertimeRequest",entity.Id.ToString(),new{entity.Status,evidenceRef});await db.SaveChangesAsync(ct);return ToDto(entity);
    }
    public async Task<List<OvertimeRequestDto>> GetMineAsync(CancellationToken ct=default){if(current.EmployeeId is not Guid id)throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");var rows=await db.OvertimeRequests.AsNoTracking().Where(x=>x.EmployeeId==id).OrderByDescending(x=>x.RequestedAt).ToListAsync(ct);return rows.Select(ToDto).ToList();}
    public async Task<List<OvertimeRequestDto>> GetPendingForActorAsync(CancellationToken ct=default){if(!current.IsInRole(Roles.Manager)&&!current.IsInRole(Roles.Hr))throw new UnauthorizedAccessException("OT_APPROVER_ROLE_REQUIRED");var rows=await db.OvertimeRequests.AsNoTracking().Where(x=>x.Status=="PENDING").OrderBy(x=>x.RequestedAt).Take(200).ToListAsync(ct);var result=new List<OvertimeRequestDto>();foreach(var row in rows){var employee=await db.Employees.AsNoTracking().SingleAsync(x=>x.Id==row.EmployeeId,ct);var allowed=(current.IsInRole(Roles.Manager)&&await access.CanManagerActOnAsync(employee,ct))||(current.IsInRole(Roles.Hr)&&await access.CanEditEmployeeAsync(employee,ct));if(allowed)result.Add(ToDto(row));}return result;}
    private string Actor=>current.StaffCode??current.EntraObjectId??"UNMAPPED";
    private static OvertimeRequestDto ToDto(OvertimeRequest x)=>new(x.Id,x.EmployeeId,x.WorkDate,x.RequestedMinutes,x.ProjectCode,x.Reason,x.Status,x.RequestedAt,x.DecidedAt,x.DecidedBy,x.DecisionNote);
    private static string Clean(string? x,int max){var s=(x??string.Empty).Trim();return s.Length<=max?s:s[..max];}
}
