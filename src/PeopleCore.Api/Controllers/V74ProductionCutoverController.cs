using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/production-cutover")]
[Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class V74ProductionCutoverController(V74ProductionCutoverService service) : ControllerBase
{
    [HttpPost("runs")] public Task<IActionResult> Start(StartV74ProductionCutoverRequest request, CancellationToken ct) => Domain(() => service.StartAsync(request, ct));
    [HttpGet("runs/{runId:guid}")] public Task<IActionResult> Get(Guid runId, CancellationToken ct) => Domain(() => service.GetAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/steps")] public Task<IActionResult> Step(Guid runId, RecordV74CutoverStepRequest request, CancellationToken ct) => Domain(() => service.RecordStepAsync(runId, request, ct));
    [HttpGet("runs/{runId:guid}/steps")] public Task<IActionResult> Steps(Guid runId, CancellationToken ct) => Domain(() => service.GetStepsAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/signoffs")] public Task<IActionResult> Signoff(Guid runId, RecordV74CutoverSignoffRequest request, CancellationToken ct) => Domain(() => service.RecordSignoffAsync(runId, request, ct));
    [HttpGet("runs/{runId:guid}/signoffs")] public Task<IActionResult> Signoffs(Guid runId, CancellationToken ct) => Domain(() => service.GetSignoffsAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/evaluate")] public Task<IActionResult> Evaluate(Guid runId, CancellationToken ct) => Domain(() => service.EvaluateAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/decision")] public Task<IActionResult> Decide(Guid runId, DecideV74GoNoGoRequest request, CancellationToken ct) => Domain(() => service.DecideAsync(runId, request, ct));
    [HttpGet("runs/{runId:guid}/decisions")] public Task<IActionResult> Decisions(Guid runId, CancellationToken ct) => Domain(() => service.GetDecisionsAsync(runId, ct));
    [HttpPost("runs/{runId:guid}/authorize-live")] public Task<IActionResult> AuthorizeLive(Guid runId, AuthorizeV74ProductionLiveRequest request, CancellationToken ct) => Domain(() => service.AuthorizeProductionLiveAsync(runId, request, ct));
    [HttpPost("runs/{runId:guid}/complete")] public Task<IActionResult> Complete(Guid runId, CompleteV74ProductionCutoverRequest request, CancellationToken ct) => Domain(() => service.CompleteAsync(runId, request, ct));

    private static async Task<IActionResult> Domain<T>(Func<Task<T>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_FOUND", StringComparison.Ordinal)) { return new NotFoundObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("CONFLICT", StringComparison.Ordinal) || ex.Message.Contains("NOT_PASS", StringComparison.Ordinal) || ex.Message.Contains("NOT_GO", StringComparison.Ordinal) || ex.Message.Contains("ALREADY", StringComparison.Ordinal)) { return new ConflictObjectResult(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return new BadRequestObjectResult(new { code = ex.Message }); }
    }
}
