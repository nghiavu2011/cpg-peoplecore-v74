namespace PeopleCore.Api.Contracts;

public sealed record StartV74ProductionCutoverRequest(
    string ReleaseCandidate,
    Guid E2eUatRunId,
    string PopulationBaselineSha256,
    string V70RuntimeEvidenceSha256,
    string Reason);

public sealed record RecordV74CutoverStepRequest(
    string StepCode,
    string Status,
    string Summary,
    string EvidenceReference,
    string? EvidenceSha256);

public sealed record RecordV74CutoverSignoffRequest(
    string SignoffRole,
    string Decision,
    string Approver,
    string EvidenceReference,
    string Note);

public sealed record DecideV74GoNoGoRequest(string Decision, string Reason, string EvidenceReference);
public sealed record AuthorizeV74ProductionLiveRequest(string Reason, string EvidenceReference);
public sealed record CompleteV74ProductionCutoverRequest(string Reason);

public sealed record V74CutoverCheckDto(string CheckCode, string Status, string Summary, DateTimeOffset CheckedAt);
public sealed record V74CutoverStepDto(string StepCode, string Phase, string Status, string Summary, string EvidenceReference, string? EvidenceSha256, DateTimeOffset RecordedAt, string RecordedBy);
public sealed record V74CutoverDecisionDto(string Decision, string Reason, string EvidenceReference, DateTimeOffset DecidedAt, string DecidedBy);
public sealed record V74CutoverSignoffDto(string SignoffRole, string Decision, string Approver, string EvidenceReference, string Note, DateTimeOffset SignedAt, string SignedBy);

public sealed record V74ProductionCutoverRunDto(
    Guid Id,
    string ReleaseCandidate,
    Guid E2eUatRunId,
    string PopulationBaselineSha256,
    string V70RuntimeEvidenceSha256,
    int ExpectedPopulation,
    int ExpectedHcm,
    int ExpectedHn,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? LiveAuthorizedAt,
    DateTimeOffset? CompletedAt,
    string? CompletionNote,
    IReadOnlyList<V74CutoverCheckDto> Checks);
