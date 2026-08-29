using System.Text.Json;
using PeopleCore.Api.Domain;

namespace PeopleCore.Api.Services.Payroll;

public sealed record ShadowPayrollRequest(Guid EmployeeId, string PayrollPeriod, string RuleSnapshotId, IReadOnlyDictionary<string, decimal> ApprovedInputs);

public interface IShadowPayrollEngine
{
    Task<ShadowPayrollResult> CalculateAsync(ShadowPayrollRequest request, CancellationToken ct = default);
}

public sealed class ShadowPayrollEngine(IConfiguration configuration) : IShadowPayrollEngine
{
    public Task<ShadowPayrollResult> CalculateAsync(ShadowPayrollRequest request, CancellationToken ct = default)
    {
        var enabled = configuration.GetValue<bool>("Payroll:ShadowEngineEnabled");
        if (!enabled)
            throw new InvalidOperationException("Shadow Payroll is disabled until verified effective-dated legal rules are approved. V62 is validation scaffold only.");

        // Deliberately no Vietnam payroll formula is hard-coded in V62.
        // The production engine must consume an approved, effective-dated legal-rule snapshot.
        return Task.FromResult(new ShadowPayrollResult
        {
            Id = Guid.NewGuid(),
            EmployeeId = request.EmployeeId,
            PayrollPeriod = request.PayrollPeriod,
            RuleSnapshotId = request.RuleSnapshotId,
            ComponentsJson = JsonSerializer.Serialize(new Dictionary<string, decimal>()),
            Status = "VALIDATION_ONLY_NO_RULES",
            CalculatedAt = DateTimeOffset.UtcNow
        });
    }
}
