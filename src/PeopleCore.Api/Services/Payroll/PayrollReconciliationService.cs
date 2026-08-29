using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Domain;

namespace PeopleCore.Api.Services.Payroll;

public sealed record ReconciliationSummary(Guid EmployeeId, string PayrollPeriod, string Status, bool PayslipReleaseBlocked, int Matches, int Reviews);

public interface IPayrollReconciliationService
{
    Task<ReconciliationSummary> RunAsync(Guid employeeId, string payrollPeriod, CancellationToken ct = default);
}

public sealed class PayrollReconciliationService(PeopleCoreDbContext db, IConfiguration configuration) : IPayrollReconciliationService
{
    public async Task<ReconciliationSummary> RunAsync(Guid employeeId, string payrollPeriod, CancellationToken ct = default)
    {
        var official = await db.PayrollOfficialResults.SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.PayrollPeriod == payrollPeriod, ct)
            ?? throw new InvalidOperationException("Official BRAVO payroll result not found.");
        var shadow = await db.ShadowPayrollResults.SingleOrDefaultAsync(x => x.EmployeeId == employeeId && x.PayrollPeriod == payrollPeriod, ct)
            ?? throw new InvalidOperationException("Shadow payroll validation result not found.");

        var officialRaw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(official.ComponentsJson) ?? [];
        var shadowRaw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(shadow.ComponentsJson) ?? [];
        var officialMap = new Dictionary<string, decimal>(officialRaw, StringComparer.OrdinalIgnoreCase);
        var shadowMap = new Dictionary<string, decimal>(shadowRaw, StringComparer.OrdinalIgnoreCase);
        var tolerance = configuration.GetValue<decimal?>("Payroll:VarianceTolerance") ?? 0.01m;
        var componentCodes = officialMap.Keys.Union(shadowMap.Keys, StringComparer.OrdinalIgnoreCase).ToArray();

        await db.ReconciliationItems
            .Where(x => x.EmployeeId == employeeId && x.PayrollPeriod == payrollPeriod)
            .ExecuteDeleteAsync(ct);

        var matches = 0;
        var reviews = 0;
        foreach (var code in componentCodes)
        {
            var hasOfficial = officialMap.TryGetValue(code, out var officialAmount);
            var hasShadow = shadowMap.TryGetValue(code, out var shadowAmount);
            decimal? variance = hasOfficial && hasShadow ? shadowAmount - officialAmount : null;
            var status = hasOfficial && hasShadow && Math.Abs(variance!.Value) <= tolerance ? "MATCH" : "REVIEW";
            if (status == "MATCH") matches++; else reviews++;

            db.ReconciliationItems.Add(new ReconciliationItem
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                PayrollPeriod = payrollPeriod,
                ComponentCode = code,
                OfficialAmount = hasOfficial ? officialAmount : null,
                ShadowAmount = hasShadow ? shadowAmount : null,
                Variance = variance,
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return new ReconciliationSummary(employeeId, payrollPeriod, reviews == 0 ? "MATCH" : "REVIEW", reviews > 0, matches, reviews);
    }
}
