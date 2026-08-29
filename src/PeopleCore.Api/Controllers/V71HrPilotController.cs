using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/hr/pilot/v71")]
[Authorize(Policy = Policies.HrOrAdmin)]
public sealed class V71HrPilotController(V71HrPilotService service) : ControllerBase
{
    [HttpPost("runs")]
    public async Task<IActionResult> Start(StartV71HrPilotRequest request, CancellationToken ct) => await Domain(() => service.StartAsync(request, ct));
    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct) => await Domain(() => service.GetAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/scenarios")]
    public async Task<IActionResult> RecordScenario(Guid runId, RecordV71ScenarioRequest request, CancellationToken ct) => await Domain(() => service.RecordScenarioAsync(runId, request, ct));
    [HttpPost("runs/{runId:guid}/evaluate")]
    public async Task<IActionResult> Evaluate(Guid runId, CancellationToken ct) => await Domain(() => service.EvaluateAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid runId, CompleteV71HrPilotRequest request, CancellationToken ct) => await Domain(() => service.CompleteAsync(runId, request, ct));

    private async Task<IActionResult> Domain<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (InvalidOperationException ex) when (ex.Message == "V71_RUN_NOT_FOUND") { return NotFound(new { code = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_COMPLETABLE", StringComparison.Ordinal)) { return Conflict(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = ex.Message }); }
    }
}
