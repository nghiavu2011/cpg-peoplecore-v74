namespace PeopleCore.Api.Contracts;

public sealed record StartPilotRunRequest(Guid EmployeeMigrationBatchId, Guid EntraMigrationBatchId, string Reason);
public sealed record CompletePilotRunRequest(string Reason);

public sealed record PilotCheckDto(
    Guid Id,
    string CheckCode,
    string Status,
    string Summary,
    DateTimeOffset CheckedAt);

public sealed record PilotRunDto(
    Guid Id,
    string Status,
    Guid EmployeeMigrationBatchId,
    Guid EntraMigrationBatchId,
    DateTimeOffset StartedAt,
    string StartedBy,
    DateTimeOffset? CompletedAt,
    string? CompletionNote,
    IReadOnlyList<PilotCheckDto> Checks);
