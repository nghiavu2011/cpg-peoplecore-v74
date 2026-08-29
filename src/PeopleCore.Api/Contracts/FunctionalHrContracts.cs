namespace PeopleCore.Api.Contracts;

public sealed record CreateContractRequest(string ContractTypeCode, string? ContractNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string SourceReference, string ChangeReason);
public sealed record ContractDto(Guid Id, Guid EmployeeId, string ContractTypeCode, string? ContractNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Status, string SourceReference, string ChangeReason, DateTimeOffset CreatedAt);
public sealed record EmployeeLifecycleEventDto(Guid Id, Guid EmployeeId, string EventType, DateOnly EffectiveDate, string Reason, string SourceReference, Guid? ContractId, DateTimeOffset CreatedAt);

public sealed record CreateLeaveRequest(string LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal RequestedHours, string Reason, string PolicyReference);
public sealed record DecideLeaveRequest(string Decision, string Note);
public sealed record LeaveRequestDto(Guid Id, Guid EmployeeId, string LeaveTypeCode, DateOnly StartDate, DateOnly EndDate, decimal RequestedHours, string Reason, string PolicyReference, string Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecidedBy, string? DecisionNote);

public sealed record UpsertAttendanceRequest(int WorkedMinutes, string SourceSystem, string SourceReference, string? ExceptionCode);
public sealed record ReviewAttendanceRequest(string Decision, string Note);
public sealed record AttendanceDayDto(Guid Id, Guid EmployeeId, DateOnly WorkDate, int WorkedMinutes, string SourceSystem, string SourceReference, string Status, string? ExceptionCode, string? ReviewNote, DateTimeOffset RecordedAt, DateTimeOffset? ReviewedAt);

public sealed record CreateOvertimeRequest(DateOnly WorkDate, int RequestedMinutes, string? ProjectCode, string Reason);
public sealed record DecideOvertimeRequest(string Decision, string Note);
public sealed record OvertimeRequestDto(Guid Id, Guid EmployeeId, DateOnly WorkDate, int RequestedMinutes, string? ProjectCode, string Reason, string Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? DecidedBy, string? DecisionNote);

public sealed record CreateTimesheetEntryRequest(DateOnly WorkDate, string ProjectCode, int Minutes, string? Description);
public sealed record ValidateTimesheetProjectRequest(DateOnly WorkDate, string ProjectCode);
public sealed record TimesheetEntryDto(Guid Id, Guid EmployeeId, DateOnly WorkDate, string ProjectCode, int Minutes, string? Description, string Status, DateTimeOffset CreatedAt);
public sealed record WorkRuleDto(DateOnly EffectiveFrom, int StandardDayMinutes, int StandardWeekMinutes);
public sealed record TimesheetValidationDto(bool Valid, string Code, string Message, string? EvidenceReference);

public sealed record CreatePerformancePeriodRequest(string Code, string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record SubmitPerformanceSelfRequest(string SelfText);
public sealed record SubmitPerformanceManagerRequest(string ManagerText);
public sealed record PerformancePeriodDto(Guid Id, string Code, string Name, DateOnly StartDate, DateOnly EndDate, string Status);
public sealed record PerformanceReviewDto(Guid Id, Guid PerformancePeriodId, Guid EmployeeId, string? SelfText, DateTimeOffset? SelfSubmittedAt, Guid? ManagerEmployeeId, string? ManagerText, DateTimeOffset? ManagerReviewedAt, string Status);

public sealed record ImportTaxInsuranceSnapshotRequest(Guid EmployeeId, string PayrollPeriod, int IterationNo, string SourceSystem, string SourceReference, string RuleSnapshotId, decimal? InsuranceBaseA, decimal? TaxWithheld, decimal? EmployeeInsuranceAmount, decimal? EmployerInsuranceAmount, string? OutputsJson, bool Approved);
public sealed record TaxInsuranceSnapshotDto(Guid Id, Guid EmployeeId, string PayrollPeriod, int IterationNo, string SourceSystem, string SourceReference, string RuleSnapshotId, decimal? InsuranceBaseA, decimal? TaxWithheld, decimal? EmployeeInsuranceAmount, decimal? EmployerInsuranceAmount, DateTimeOffset ImportedAt, DateTimeOffset? ApprovedAt);

public sealed record PayslipPreviewDto(Guid EmployeeId, string PayrollPeriod, Guid OfficialPayrollResultId, string SourceSystem, string SourceRunId, IReadOnlyDictionary<string, decimal> Components, string EvidenceReference, bool ReleaseEnabled);
public sealed record PayslipDto(Guid EmployeeId, string PayrollPeriod, string SourceSystem, string SourceRunId, IReadOnlyDictionary<string, decimal> Components, DateTimeOffset ReleasedAt);

public sealed record FunctionalEvidenceDto(Guid Id, string ScenarioCode, string Domain, string Action, Guid? EmployeeId, Guid? RelatedEntityId, string Status, string PayloadSha256, string? CorrelationId, string CreatedBy, DateTimeOffset CreatedAt)
{
    public string Reference => $"FUNC:{Id:D}";
}

public sealed record RegisterEvidenceArtifactRequest(string ArtifactType, string Sha256, string StorageReference, string Result, DateTimeOffset ObservedAt, string? CorrelationId);
public sealed record EvidenceArtifactDto(Guid Id, string ArtifactType, string Sha256, string StorageReference, string Result, string? CorrelationId, DateTimeOffset ObservedAt, DateTimeOffset RecordedAt, string RecordedBy)
{
    public string Reference => $"ARTIFACT:{Id:D}";
}
