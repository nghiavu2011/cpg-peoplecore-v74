using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/payroll/parallel/v72")]
[Authorize(Policy = Policies.Payroll)]
public sealed class V72PayrollParallelRunController(V72PayrollParallelRunService service) : ControllerBase
{
    [HttpPost("runs")] public Task<IActionResult> Start(StartV72PayrollParallelRunRequest request, CancellationToken ct) => Domain(() => service.StartAsync(request, ct));
    [HttpGet("runs/{runId:guid}")] public Task<IActionResult> Get(Guid runId, CancellationToken ct) => Domain(() => service.GetAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/official-results")] public Task<IActionResult> ImportOfficial(Guid runId, ImportV72PayrollResultBatchRequest request, CancellationToken ct) => Domain(() => service.ImportOfficialAsync(runId, request, ct));
    [HttpPost("runs/{runId:guid}/shadow-results")] public Task<IActionResult> ImportShadow(Guid runId, ImportV72PayrollResultBatchRequest request, CancellationToken ct) => Domain(() => service.ImportShadowAsync(runId, request, ct));
    [HttpPost("runs/{runId:guid}/evaluate")] public Task<IActionResult> Evaluate(Guid runId, CancellationToken ct) => Domain(() => service.EvaluateAsync(runId, ct));
    [HttpGet("runs/{runId:guid}/variances")] public Task<IActionResult> Variances(Guid runId, CancellationToken ct) => Domain(() => service.GetVariancesAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/variances/{varianceId:guid}/resolve")] public Task<IActionResult> Resolve(Guid runId, Guid varianceId, ResolveV72VarianceRequest request, CancellationToken ct) => Domain(() => service.ResolveVarianceAsync(runId, varianceId, request, ct));
    [HttpPost("runs/{runId:guid}/complete")] public Task<IActionResult> Complete(Guid runId, CompleteV72PayrollParallelRunRequest request, CancellationToken ct) => Domain(() => service.CompleteAsync(runId, request, ct));

    private static async Task<IActionResult> Domain<T>(Func<Task<T>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_FOUND", StringComparison.Ordinal)) { return new NotFoundObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CONFLICT", StringComparison.Ordinal) || ex.Message.Contains("ALREADY", StringComparison.Ordinal) || ex.Message.Contains("NOT_PASS", StringComparison.Ordinal)) { return new ConflictObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { code = ex.Message }); }
    }
}
