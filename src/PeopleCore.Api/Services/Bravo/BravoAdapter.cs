namespace PeopleCore.Api.Services.Bravo;

public sealed record BravoAdapterStatus(string Mode, bool Connected, string Message);
public sealed record BravoAdapterResult(bool Accepted, string Status, string? Reference = null, string? Message = null);

public interface IBravoAdapter
{
    BravoAdapterStatus GetStatus();
    Task<BravoAdapterResult> PushApprovedCompensationAsync(string payloadJson, string idempotencyKey, CancellationToken ct = default);
    Task<BravoAdapterResult> PullProjectCodesAsync(CancellationToken ct = default);
    Task<BravoAdapterResult> PullOfficialPayrollResultsAsync(string payrollPeriod, CancellationToken ct = default);
}

public sealed class BravoAdapterStub(IConfiguration configuration) : IBravoAdapter
{
    private string Mode => configuration["Bravo:Mode"] ?? "Stub";

    public BravoAdapterStatus GetStatus() => new(
        Mode,
        Connected: false,
        Message: "V62 adapter abstraction only. BRAVO credentials/API/file contract are not configured; no live transfer occurs.");

    public Task<BravoAdapterResult> PushApprovedCompensationAsync(string payloadJson, string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(new BravoAdapterResult(false, "NOT_CONFIGURED", Message: "BRAVO write contract pending."));

    public Task<BravoAdapterResult> PullProjectCodesAsync(CancellationToken ct = default)
        => Task.FromResult(new BravoAdapterResult(false, "NOT_CONFIGURED", Message: "BRAVO project-code import contract pending."));

    public Task<BravoAdapterResult> PullOfficialPayrollResultsAsync(string payrollPeriod, CancellationToken ct = default)
        => Task.FromResult(new BravoAdapterResult(false, "NOT_CONFIGURED", Message: "BRAVO official payroll-result import contract pending."));
}
