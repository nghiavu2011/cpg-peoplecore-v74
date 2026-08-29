using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/admin/pilot")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class PilotUatController(PilotUatService service) : ControllerBase
{
    [HttpPost("runs")]
    public async Task<IActionResult> Start(StartPilotRunRequest request, CancellationToken ct)
    {
        try { return Ok(await service.StartAsync(request, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(runId, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("runs/{runId:guid}/evaluate")]
    public async Task<IActionResult> Evaluate(Guid runId, CancellationToken ct)
    {
        try { return Ok(await service.EvaluateAsync(runId, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("runs/{runId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid runId, CompletePilotRunRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CompleteAsync(runId, request, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "PILOT_UAT_DISABLED" => NotFound(new { code = ex.Message }),
        "PILOT_RUN_NOT_FOUND" or "EMPLOYEE_MIGRATION_BATCH_NOT_FOUND" or "ENTRA_MIGRATION_BATCH_NOT_FOUND" => NotFound(new { code = ex.Message }),
        "PILOT_RUN_ALREADY_COMPLETED" or "PILOT_RUN_NOT_READY_TO_COMPLETE" => Conflict(new { code = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
