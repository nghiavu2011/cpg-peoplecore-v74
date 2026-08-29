using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/uat/e2e/v73")]
[Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class V73E2eUatController(V73E2eUatService service) : ControllerBase
{
    [HttpPost("runs")] public Task<IActionResult> Start(StartV73E2eUatRunRequest request, CancellationToken ct) => Domain(() => service.StartAsync(request, ct));
    [HttpGet("runs/{runId:guid}")] public Task<IActionResult> Get(Guid runId, CancellationToken ct) => Domain(() => service.GetAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/scenarios")] public Task<IActionResult> Scenario(Guid runId, RecordV73ScenarioEvidenceRequest request, CancellationToken ct) => Domain(() => service.RecordScenarioAsync(runId, request, ct));
    [HttpGet("runs/{runId:guid}/scenarios")] public Task<IActionResult> Scenarios(Guid runId, CancellationToken ct) => Domain(() => service.GetScenariosAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/defects")] public Task<IActionResult> RaiseDefect(Guid runId, RaiseV73DefectRequest request, CancellationToken ct) => Domain(() => service.RaiseDefectAsync(runId, request, ct));
    [HttpPost("runs/{runId:guid}/defects/{defectId:guid}/resolve")] public Task<IActionResult> ResolveDefect(Guid runId, Guid defectId, ResolveV73DefectRequest request, CancellationToken ct) => Domain(() => service.ResolveDefectAsync(runId, defectId, request, ct));
    [HttpGet("runs/{runId:guid}/defects")] public Task<IActionResult> Defects(Guid runId, CancellationToken ct) => Domain(() => service.GetDefectsAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/signoffs")] public Task<IActionResult> Signoff(Guid runId, RecordV73SignoffRequest request, CancellationToken ct) => Domain(() => service.RecordSignoffAsync(runId, request, ct));
    [HttpGet("runs/{runId:guid}/signoffs")] public Task<IActionResult> Signoffs(Guid runId, CancellationToken ct) => Domain(() => service.GetSignoffsAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/evaluate")] public Task<IActionResult> Evaluate(Guid runId, CancellationToken ct) => Domain(() => service.EvaluateAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/complete")] public Task<IActionResult> Complete(Guid runId, CompleteV73E2eUatRunRequest request, CancellationToken ct) => Domain(() => service.CompleteAsync(runId, request, ct));

    private static async Task<IActionResult> Domain<T>(Func<Task<T>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_FOUND", StringComparison.Ordinal)) { return new NotFoundObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CONFLICT", StringComparison.Ordinal) || ex.Message.Contains("ALREADY", StringComparison.Ordinal) || ex.Message.Contains("NOT_PASS", StringComparison.Ordinal)) { return new ConflictObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { code = ex.Message }); }
    }
}
