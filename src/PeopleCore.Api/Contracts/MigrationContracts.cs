namespace PeopleCore.Api.Contracts;

public sealed record MigrationBatchDto(
    Guid Id,
    string BatchKind,
    string SourceSystem,
    string SourceFileName,
    string SourceSha256,
    string Status,
    int TotalRows,
    int ValidRows,
    int ReviewRows,
    int InvalidRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? CommittedAt);

public sealed record MigrationRowDto(
    Guid Id,
    int RowNumber,
    string? ExternalKey,
    string Status,
    string[] Errors,
    Guid? EmployeeId);

public sealed record IdentityMappingCandidateDto(
    Guid Id,
    Guid BatchId,
    Guid EmployeeId,
    string StaffCode,
    string CorporateEmail,
    string EntraTenantId,
    string EntraObjectId,
    string EntraUserPrincipalName,
    string EntraDisplayName,
    bool AccountEnabled,
    string MatchType,
    string Status,
    string? Reason,
    DateTimeOffset? ConfirmedAt,
    string? ConfirmedBy);

public sealed record CommitMigrationRequest(string Reason);
public sealed record ConfirmIdentityCandidateRequest(string Reason);
