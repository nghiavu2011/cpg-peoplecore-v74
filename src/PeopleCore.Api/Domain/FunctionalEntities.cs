namespace PeopleCore.Api.Domain;

public sealed class FunctionalEvidence
{
    public Guid Id { get; set; }
    public required string ScenarioCode { get; set; }
    public required string Domain { get; set; }
    public required string Action { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public required string Status { get; set; }
    public required string PayloadSha256 { get; set; }
    public string? CorrelationId { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EmploymentContract
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string ContractTypeCode { get; set; }
    public string? ContractNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public required string Status { get; set; }
    public required string SourceReference { get; set; }
    public required string ChangeReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class EmployeeLifecycleEvent
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string EventType { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public required string Reason { get; set; }
    public required string SourceReference { get; set; }
    public Guid? ContractId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string LeaveTypeCode { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal RequestedHours { get; set; }
    public required string Reason { get; set; }
    public required string PolicyReference { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset RequestedAt { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}

public sealed class AttendanceDay
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public int WorkedMinutes { get; set; }
    public required string SourceSystem { get; set; }
    public required string SourceReference { get; set; }
    public string Status { get; set; } = "RECORDED";
    public string? ExceptionCode { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string RecordedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public sealed class OvertimeRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public int RequestedMinutes { get; set; }
    public string? ProjectCode { get; set; }
    public required string Reason { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset RequestedAt { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}

public sealed class TimesheetEntry
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public required string ProjectCode { get; set; }
    public int Minutes { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "SUBMITTED";
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class PerformancePeriod
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class PerformanceReview
{
    public Guid Id { get; set; }
    public Guid PerformancePeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public string? SelfText { get; set; }
    public DateTimeOffset? SelfSubmittedAt { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public string? ManagerText { get; set; }
    public DateTimeOffset? ManagerReviewedAt { get; set; }
    public string Status { get; set; } = "SELF_DRAFT";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaxInsuranceSnapshot
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PayrollPeriod { get; set; }
    public int IterationNo { get; set; }
    public required string SourceSystem { get; set; }
    public required string SourceReference { get; set; }
    public required string RuleSnapshotId { get; set; }
    public decimal? InsuranceBaseA { get; set; }
    public decimal? TaxWithheld { get; set; }
    public decimal? EmployeeInsuranceAmount { get; set; }
    public decimal? EmployerInsuranceAmount { get; set; }
    public string OutputsJson { get; set; } = "{}";
    public DateTimeOffset ImportedAt { get; set; }
    public required string ImportedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
}

public sealed class PayslipRelease
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PayrollPeriod { get; set; }
    public Guid OfficialPayrollResultId { get; set; }
    public required string SourceSystem { get; set; }
    public required string SourceRunId { get; set; }
    public DateTimeOffset ReleasedAt { get; set; }
    public required string ReleasedBy { get; set; }
}

public sealed class EvidenceArtifact
{
    public Guid Id { get; set; }
    public required string ArtifactType { get; set; }
    public required string Sha256 { get; set; }
    public required string StorageReference { get; set; }
    public required string Result { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string RecordedBy { get; set; }
}
