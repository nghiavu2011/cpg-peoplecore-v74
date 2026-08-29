using System.Text.Json;

namespace PeopleCore.Api.Contracts;

public sealed record CompensationComponentDto(string Code, decimal Amount, string Currency = "VND");
public sealed record ApprovedCompensationHandoffRequest(
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    string ApprovalReference,
    IReadOnlyList<CompensationComponentDto> Components);

public sealed record ProjectCodeDto(
    string Code,
    string Name,
    string? ParentCode,
    string Status,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

public sealed record BravoProjectCodeBatchRequest(
    string SourceBatchReference,
    string SourceRevision,
    IReadOnlyList<ProjectCodeDto> Projects);

public sealed record IntegrationEnvelopeResponse(
    Guid MessageId,
    string Direction,
    string MessageType,
    string IdempotencyKey,
    string PayloadSha256,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string? ExternalReference,
    string? LastError);
