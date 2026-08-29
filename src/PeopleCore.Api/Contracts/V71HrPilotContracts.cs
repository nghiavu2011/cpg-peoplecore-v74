namespace PeopleCore.Api.Contracts;

public sealed record V71PilotParticipantRequest(string StaffCode, string Persona);
public sealed record StartV71HrPilotRequest(Guid EmployeeMigrationBatchId, Guid EntraMigrationBatchId, IReadOnlyList<V71PilotParticipantRequest> Cohort, string Reason);
public sealed record RecordV71ScenarioRequest(string ScenarioCode, string Persona, string StaffCode, string Status, string Summary, string? EvidenceReference);
public sealed record CompleteV71HrPilotRequest(string Reason);

public sealed record V71HrPilotCheckDto(string CheckCode, string Status, string Summary, DateTimeOffset CheckedAt);
public sealed record V71HrPilotScenarioDto(string ScenarioCode, string Persona, string StaffCode, string Status, string Summary, string? EvidenceReference, DateTimeOffset RecordedAt);
public sealed record V71HrPilotRunDto(Guid Id, Guid EmployeeMigrationBatchId, Guid EntraMigrationBatchId, string PopulationBaselineSha256,
    int ExpectedPopulation, int ExpectedHcm, int ExpectedHn, int CohortSize, string CohortSha256, string V70RuntimeGateStatus,
    string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? CompletionNote,
    IReadOnlyList<V71HrPilotCheckDto> Checks, IReadOnlyList<V71HrPilotScenarioDto> Scenarios);
