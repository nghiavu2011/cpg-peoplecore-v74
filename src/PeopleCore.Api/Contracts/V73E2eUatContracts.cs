namespace PeopleCore.Api.Contracts;

public sealed record V73UatParticipantRequest(string StaffCode, string Persona);
public sealed record StartV73E2eUatRunRequest(string ReleaseCandidate, string PopulationBaselineSha256, string V70RuntimeEvidenceSha256, Guid HrPilotRunId, Guid PayrollParallelRunId, IReadOnlyList<V73UatParticipantRequest> Participants, string Reason);
public sealed record RecordV73ScenarioEvidenceRequest(string ScenarioCode, string Persona, string StaffCode, string Status, string Summary, string EvidenceReference, string? CorrelationId);
public sealed record RaiseV73DefectRequest(string DefectCode, string Severity, string Domain, string Summary, string EvidenceReference);
public sealed record ResolveV73DefectRequest(string Disposition, string ResolutionNote, string EvidenceReference);
public sealed record RecordV73SignoffRequest(string SignoffRole, string Decision, string Approver, string EvidenceReference, string Note);
public sealed record CompleteV73E2eUatRunRequest(string Reason);

public sealed record V73UatCheckDto(string CheckCode, string Status, string Summary, DateTimeOffset CheckedAt);
public sealed record V73UatScenarioDto(string ScenarioCode, string Domain, string Persona, string StaffCode, string Status, string Summary, string EvidenceReference, string? CorrelationId, DateTimeOffset RecordedAt);
public sealed record V73UatDefectDto(Guid Id, string DefectCode, string Severity, string Domain, string Summary, string EvidenceReference, string? Disposition, string? ResolutionNote, string? ResolutionEvidenceReference, DateTimeOffset RaisedAt, DateTimeOffset? ResolvedAt);
public sealed record V73UatSignoffDto(string SignoffRole, string Decision, string Approver, string EvidenceReference, string Note, DateTimeOffset SignedAt, string SignedBy);
public sealed record V73E2eUatRunDto(Guid Id, string ReleaseCandidate, string PopulationBaselineSha256, string V70RuntimeEvidenceSha256, Guid HrPilotRunId, Guid PayrollParallelRunId, int ExpectedPopulation, int ExpectedHcm, int ExpectedHn, int TesterCohortSize, string V70RuntimeGateStatus, string V71HrPilotGateStatus, string V72PayrollParallelGateStatus, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? CompletionNote, int ScenarioEvidenceCount, int DefectCount, int SignoffCount, IReadOnlyList<V73UatCheckDto> Checks);
