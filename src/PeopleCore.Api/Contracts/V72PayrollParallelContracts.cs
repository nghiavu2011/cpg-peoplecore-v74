using System.Collections.Generic;

namespace PeopleCore.Api.Contracts;

public sealed record StartV72PayrollParallelRunRequest(string PayrollPeriod, int IterationNo, string OfficialSourceRunId, string ShadowRuleSnapshotId, string Reason);
public sealed record V72PayrollResultRowRequest(string StaffCode, IReadOnlyDictionary<string, decimal> Components);
public sealed record ImportV72PayrollResultBatchRequest(string SourceFileSha256, string EvidenceReference, IReadOnlyList<V72PayrollResultRowRequest> Rows);
public sealed record ResolveV72VarianceRequest(string Disposition, string ResolutionNote, string EvidenceReference);
public sealed record CompleteV72PayrollParallelRunRequest(string Reason);

public sealed record V72PayrollParallelCheckDto(string CheckCode, string Status, string Summary, DateTimeOffset CheckedAt);
public sealed record V72PayrollParallelVarianceDto(Guid Id, string StaffCode, string ComponentCode, decimal? OfficialAmount, decimal? ShadowAmount, decimal? Variance, string ReasonCode, string? Disposition, string? ResolutionNote, string? EvidenceReference);
public sealed record V72PayrollParallelRunDto(Guid Id, string PayrollPeriod, int IterationNo, int ExpectedPopulation, string OfficialSourceSystem, string OfficialSourceRunId, string ShadowRuleSnapshotId, string V70RuntimeGateStatus, string V71HrPilotGateStatus, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? CompletionNote, int OfficialResults, int ShadowResults, int Snapshots, int Variances, int OpenVariances, IReadOnlyList<V72PayrollParallelCheckDto> Checks);
