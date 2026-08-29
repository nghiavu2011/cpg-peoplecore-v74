namespace PeopleCore.Api.Domain;

public sealed class Employee
{
    public Guid Id { get; set; }
    public required string StaffCode { get; set; }
    public required string CorporateEmail { get; set; }
    public required string DisplayName { get; set; }

    // V63 keeps a current assignment snapshot for fast daily queries.
    // Effective-dated history is stored in EmployeeAssignment.
    public required string CompanyCode { get; set; }
    public required string DepartmentCode { get; set; }
    public required string OfficeCode { get; set; }
    public required string PositionTitle { get; set; }
    public string? GradeCode { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public string TimesheetPolicy { get; set; } = "REQUIRED";

    public string EmploymentStatus { get; set; } = "ACTIVE";
    public DateOnly? HireDate { get; set; }
    public DateOnly? LastWorkingDate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
    public EmployeePrivateProfile? PrivateProfile { get; set; }
}

public sealed class EmployeePrivateProfile
{
    public Guid EmployeeId { get; set; }
    public string? PersonalEmail { get; set; }
    public string? Mobile { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? HomeCity { get; set; }
    public string? HrPrivateNotes { get; set; }
}

public sealed class EmployeeAssignment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string CompanyCode { get; set; }
    public required string DepartmentCode { get; set; }
    public required string OfficeCode { get; set; }
    public required string PositionTitle { get; set; }
    public string? GradeCode { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public string TimesheetPolicy { get; set; } = "REQUIRED";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
}

public sealed class EmployeeIdentity
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string EntraTenantId { get; set; }
    public required string EntraObjectId { get; set; }
    public string? LinkedEmail { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class AuthorizationGrant
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string RoleCode { get; set; }
    public string ScopeType { get; set; } = "SELF"; // SELF | DEPARTMENT | COMPANY | GLOBAL
    public string? ScopeValue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string Reason { get; set; } = "INITIAL_GRANT";
    public string GrantedBy { get; set; } = "SYSTEM";
    public DateTimeOffset? RevokedAt { get; set; }
}


public sealed class MigrationBatch
{
    public Guid Id { get; set; }
    public required string BatchKind { get; set; } // EMPLOYEE_MASTER | ENTRA_DIRECTORY
    public required string SourceSystem { get; set; }
    public required string SourceFileName { get; set; }
    public required string SourceSha256 { get; set; }
    public string Status { get; set; } = "STAGED"; // STAGED | REVIEW | READY | COMMITTED | REJECTED
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ReviewRows { get; set; }
    public int InvalidRows { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
}

public sealed class MigrationRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string? ExternalKey { get; set; }
    public required string PayloadJson { get; set; }
    public required string NormalizedJson { get; set; }
    public string Status { get; set; } = "VALID"; // VALID | REVIEW | INVALID | COMMITTED
    public string ErrorsJson { get; set; } = "[]";
    public Guid? EmployeeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IdentityMappingCandidate
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public required string CorporateEmail { get; set; }
    public required string EntraTenantId { get; set; }
    public required string EntraObjectId { get; set; }
    public required string EntraUserPrincipalName { get; set; }
    public required string EntraDisplayName { get; set; }
    public bool AccountEnabled { get; set; }
    public string MatchType { get; set; } = "EXACT_EMAIL";
    public string Status { get; set; } = "READY"; // READY | REVIEW | CONFIRMED | SKIPPED
    public string? Reason { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? ActorEmployeeId { get; set; }
    public string? ActorObjectId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? CorrelationId { get; set; }
    public string? DataJson { get; set; }
}

public sealed class IntegrationMessage
{
    public Guid Id { get; set; }
    public required string Direction { get; set; } // IN | OUT
    public required string Integration { get; set; } // BRAVO
    public required string MessageType { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string PayloadJson { get; set; }
    public string SchemaVersion { get; set; } = "1.0";
    public required string PayloadSha256 { get; set; }
    public string? CorrelationId { get; set; }
    public string? ExternalReference { get; set; }
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class PayrollOfficialResult
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PayrollPeriod { get; set; } // YYYY-MM
    public required string SourceSystem { get; set; } // BRAVO
    public required string SourceRunId { get; set; }
    public required string ComponentsJson { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}

public sealed class ShadowPayrollResult
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PayrollPeriod { get; set; }
    public required string RuleSnapshotId { get; set; }
    public required string ComponentsJson { get; set; }
    public string Status { get; set; } = "VALIDATION_ONLY";
    public DateTimeOffset CalculatedAt { get; set; }
}

public sealed class ReconciliationItem
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PayrollPeriod { get; set; }
    public required string ComponentCode { get; set; }
    public decimal? ShadowAmount { get; set; }
    public decimal? OfficialAmount { get; set; }
    public decimal? Variance { get; set; }
    public required string Status { get; set; } // MATCH | REVIEW
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}

public sealed class PilotRun
{
    public Guid Id { get; set; }
    public Guid EmployeeMigrationBatchId { get; set; }
    public Guid EntraMigrationBatchId { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN | PASS | WARN | FAIL | COMPLETED
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = "SYSTEM";
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed class PilotCheck
{
    public Guid Id { get; set; }
    public Guid PilotRunId { get; set; }
    public required string CheckCode { get; set; }
    public required string Status { get; set; } // PASS | WARN | FAIL
    public required string Summary { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string CheckedBy { get; set; } = "SYSTEM";
}

// V67 canonical BRAVO projection. BRAVO/Finance remains the source of truth.
public sealed class ProjectCode
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ParentCode { get; set; }
    public string Status { get; set; } = "ACTIVE"; // ACTIVE | INACTIVE
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string SourceSystem { get; set; } = "BRAVO";
    public required string SourceRevision { get; set; }
    public Guid LastSourceMessageId { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class CompensationHandoff
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public required string ApprovalReference { get; set; }
    public required string ApprovedPayloadSha256 { get; set; }
    public Guid IntegrationMessageId { get; set; }
    public string Status { get; set; } = "QUEUED"; // QUEUED | SENT | REVIEW
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// V68 adapter/transport pilot evidence. Metadata only; mapped compensation payload is not duplicated here.
public sealed class BravoTransportEvidence
{
    public Guid Id { get; set; }
    public Guid? IntegrationMessageId { get; set; }
    public required string Direction { get; set; }
    public required string MessageType { get; set; }
    public required string MappingMode { get; set; }
    public required string MappingProfile { get; set; }
    public required string SourcePayloadSha256 { get; set; }
    public required string MappedPayloadSha256 { get; set; }
    public required string TransportMode { get; set; }
    public required string EnvelopeSha256 { get; set; }
    public required string SignatureHex { get; set; }
    public required string Nonce { get; set; }
    public required string Status { get; set; } // DRY_RUN_SIGNED
    public string? CorrelationId { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}


// V71 Real HR Pilot orchestration. Pilot metadata only; this does not replace Employee Master or Entra source-of-truth records.
public sealed class HrPilotRun
{
    public Guid Id { get; set; }
    public Guid EmployeeMigrationBatchId { get; set; }
    public Guid EntraMigrationBatchId { get; set; }
    public required string PopulationBaselineSha256 { get; set; }
    public int ExpectedPopulation { get; set; }
    public int ExpectedHcm { get; set; }
    public int ExpectedHn { get; set; }
    public int CohortSize { get; set; }
    public required string CohortSha256 { get; set; }
    public string V70RuntimeGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string Status { get; set; } = "OPEN"; // OPEN | PASS | WARN | FAIL | COMPLETED
    public DateTimeOffset StartedAt { get; set; }
    public string StartedBy { get; set; } = "SYSTEM";
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed class HrPilotParticipant
{
    public Guid Id { get; set; }
    public Guid HrPilotRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public required string OfficeCode { get; set; }
    public required string Persona { get; set; } // EMPLOYEE | MANAGER | HR | PAYROLL | ADMIN
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class HrPilotCheck
{
    public Guid Id { get; set; }
    public Guid HrPilotRunId { get; set; }
    public required string CheckCode { get; set; }
    public required string Status { get; set; } // PASS | WARN | FAIL
    public required string Summary { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string CheckedBy { get; set; } = "SYSTEM";
}

public sealed class HrPilotScenarioEvidence
{
    public Guid Id { get; set; }
    public Guid HrPilotRunId { get; set; }
    public required string ScenarioCode { get; set; }
    public required string Persona { get; set; }
    public required string StaffCode { get; set; }
    public required string Status { get; set; } // PASS | FAIL | BLOCKED
    public required string Summary { get; set; }
    public string? EvidenceReference { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = "SYSTEM";
}


// V72 payroll parallel-run evidence. These tables do not feed employee-facing payslips.
public sealed class PayrollParallelRun
{
    public Guid Id { get; set; }
    public required string PayrollPeriod { get; set; } // YYYY-MM
    public int IterationNo { get; set; }
    public int ExpectedPopulation { get; set; }
    public string OfficialSourceSystem { get; set; } = "BRAVO";
    public required string OfficialSourceRunId { get; set; }
    public required string ShadowRuleSnapshotId { get; set; }
    public string V70RuntimeGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string V71HrPilotGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string Status { get; set; } = "OPEN"; // OPEN | PASS | WARN | FAIL | COMPLETED
    public DateTimeOffset StartedAt { get; set; }
    public required string StartedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed class PayrollParallelOfficialResult
{
    public Guid Id { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public string SourceSystem { get; set; } = "BRAVO";
    public required string SourceRunId { get; set; }
    public required string ComponentsJson { get; set; }
    public required string ComponentsSha256 { get; set; }
    public required string SourceFileSha256 { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public required string ImportedBy { get; set; }
}

public sealed class PayrollParallelShadowResult
{
    public Guid Id { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public required string RuleSnapshotId { get; set; }
    public required string ComponentsJson { get; set; }
    public required string ComponentsSha256 { get; set; }
    public required string SourceFileSha256 { get; set; }
    public required string EvidenceReference { get; set; }
    public string Status { get; set; } = "VALIDATION_ONLY";
    public DateTimeOffset ImportedAt { get; set; }
    public required string ImportedBy { get; set; }
}

public sealed class PayrollParallelSnapshot
{
    public Guid Id { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public Guid OfficialResultId { get; set; }
    public Guid ShadowResultId { get; set; }
    public required string OfficialComponentsSha256 { get; set; }
    public required string ShadowComponentsSha256 { get; set; }
    public int MatchCount { get; set; }
    public int ReviewCount { get; set; }
    public required string Status { get; set; } // MATCH | REVIEW
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PayrollParallelVariance
{
    public Guid Id { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public required string ComponentCode { get; set; }
    public decimal? OfficialAmount { get; set; }
    public decimal? ShadowAmount { get; set; }
    public decimal? Variance { get; set; }
    public required string ReasonCode { get; set; } // VALUE_DIFFERENCE | OFFICIAL_ONLY | SHADOW_ONLY
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PayrollParallelVarianceResolution
{
    public Guid Id { get; set; }
    public Guid PayrollParallelVarianceId { get; set; }
    public required string Disposition { get; set; } // EXPLAINED_ACCEPTED | ROUNDING_ACCEPTED | DATA_FIX_REQUIRED | SHADOW_RULE_FIX_REQUIRED | BRAVO_REVIEW_REQUIRED
    public required string ResolutionNote { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset ResolvedAt { get; set; }
    public required string ResolvedBy { get; set; }
}

public sealed class PayrollParallelCheck
{
    public Guid Id { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public required string CheckCode { get; set; }
    public required string Status { get; set; } // PASS | WARN | FAIL
    public required string Summary { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public required string CheckedBy { get; set; }
}


// V73 Full End-to-End UAT orchestration. Evidence-only; it does not authorize Production Live.
public sealed class E2eUatRun
{
    public Guid Id { get; set; }
    public required string ReleaseCandidate { get; set; }
    public required string PopulationBaselineSha256 { get; set; }
    public required string V70RuntimeEvidenceSha256 { get; set; }
    public Guid HrPilotRunId { get; set; }
    public Guid PayrollParallelRunId { get; set; }
    public int ExpectedPopulation { get; set; }
    public int ExpectedHcm { get; set; }
    public int ExpectedHn { get; set; }
    public int TesterCohortSize { get; set; }
    public string V70RuntimeGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string V71HrPilotGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string V72PayrollParallelGateStatus { get; set; } = "PENDING_EXTERNAL_EVIDENCE";
    public string Status { get; set; } = "OPEN"; // OPEN | PASS | WARN | FAIL | COMPLETED
    public DateTimeOffset StartedAt { get; set; }
    public required string StartedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed class E2eUatParticipant
{
    public Guid Id { get; set; }
    public Guid E2eUatRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public required string StaffCode { get; set; }
    public required string OfficeCode { get; set; }
    public required string Persona { get; set; } // EMPLOYEE | MANAGER | HR | PAYROLL | ADMIN | LEADERSHIP
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class E2eUatScenarioEvidence
{
    public Guid Id { get; set; }
    public Guid E2eUatRunId { get; set; }
    public required string ScenarioCode { get; set; }
    public required string Domain { get; set; }
    public required string Persona { get; set; }
    public required string StaffCode { get; set; }
    public required string Status { get; set; } // PASS | FAIL | BLOCKED
    public required string Summary { get; set; }
    public required string EvidenceReference { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string RecordedBy { get; set; }
}

public sealed class E2eUatDefect
{
    public Guid Id { get; set; }
    public Guid E2eUatRunId { get; set; }
    public required string DefectCode { get; set; }
    public required string Severity { get; set; } // P0 | P1 | P2 | P3
    public required string Domain { get; set; }
    public required string Summary { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public required string RaisedBy { get; set; }
}

public sealed class E2eUatDefectResolution
{
    public Guid Id { get; set; }
    public Guid E2eUatDefectId { get; set; }
    public required string Disposition { get; set; } // FIXED_VERIFIED | ACCEPTED_RISK | DEFERRED_TO_POST_LIVE
    public required string ResolutionNote { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset ResolvedAt { get; set; }
    public required string ResolvedBy { get; set; }
}

public sealed class E2eUatSignoff
{
    public Guid Id { get; set; }
    public Guid E2eUatRunId { get; set; }
    public required string SignoffRole { get; set; } // HR_OWNER | PAYROLL_OWNER | TECHNICAL_OWNER | BUSINESS_OWNER
    public required string Decision { get; set; } // APPROVE | REJECT
    public required string Approver { get; set; }
    public required string EvidenceReference { get; set; }
    public required string Note { get; set; }
    public DateTimeOffset SignedAt { get; set; }
    public required string SignedBy { get; set; }
}

public sealed class E2eUatCheck
{
    public Guid Id { get; set; }
    public Guid E2eUatRunId { get; set; }
    public required string CheckCode { get; set; }
    public required string Status { get; set; } // PASS | WARN | FAIL
    public required string Summary { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public required string CheckedBy { get; set; }
}

// V74 Production Cutover + Final Release orchestration.
// This evidence model does not itself switch runtime configuration to Production Live.
public sealed class ProductionCutoverRun
{
    public Guid Id { get; set; }
    public required string ReleaseCandidate { get; set; }
    public Guid E2eUatRunId { get; set; }
    public required string PopulationBaselineSha256 { get; set; }
    public required string V70RuntimeEvidenceSha256 { get; set; }
    public int ExpectedPopulation { get; set; }
    public int ExpectedHcm { get; set; }
    public int ExpectedHn { get; set; }
    public string Status { get; set; } = "OPEN"; // OPEN | NO_GO | GO_APPROVED | CUTOVER_EXECUTED | LIVE_AUTHORIZED | COMPLETED
    public DateTimeOffset StartedAt { get; set; }
    public required string StartedBy { get; set; }
    public DateTimeOffset? LiveAuthorizedAt { get; set; }
    public string? LiveAuthorizedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed class ProductionCutoverStepEvidence
{
    public Guid Id { get; set; }
    public Guid ProductionCutoverRunId { get; set; }
    public required string StepCode { get; set; }
    public required string Phase { get; set; } // PRE_CUTOVER | EXECUTION | POST_CUTOVER
    public required string Status { get; set; } // PASS | FAIL | BLOCKED
    public required string Summary { get; set; }
    public required string EvidenceReference { get; set; }
    public string? EvidenceSha256 { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string RecordedBy { get; set; }
}

public sealed class ProductionCutoverDecision
{
    public Guid Id { get; set; }
    public Guid ProductionCutoverRunId { get; set; }
    public required string Decision { get; set; } // GO | NO_GO
    public required string Reason { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public required string DecidedBy { get; set; }
}

public sealed class ProductionCutoverSignoff
{
    public Guid Id { get; set; }
    public Guid ProductionCutoverRunId { get; set; }
    public required string SignoffRole { get; set; } // HR_OWNER | PAYROLL_OWNER | TECHNICAL_OWNER | BUSINESS_OWNER
    public required string Decision { get; set; } // APPROVE | REJECT
    public required string Approver { get; set; }
    public required string EvidenceReference { get; set; }
    public required string Note { get; set; }
    public DateTimeOffset SignedAt { get; set; }
    public required string SignedBy { get; set; }
}

public sealed class ProductionCutoverCheck
{
    public Guid Id { get; set; }
    public Guid ProductionCutoverRunId { get; set; }
    public required string CheckCode { get; set; }
    public required string Status { get; set; } // PASS | WARN | FAIL
    public required string Summary { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public required string CheckedBy { get; set; }
}
