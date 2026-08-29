using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/admin/migrations/entra-directory")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class IdentityMappingPilotController(IdentityMappingPilotService service) : ControllerBase
{
    [HttpPost("stage")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Stage([FromForm] IFormFile file, [FromForm] string? sourceSystem, CancellationToken ct)
    {
        try { return Ok(await service.StageAsync(file, sourceSystem ?? "ENTRA_DIRECTORY_EXPORT", ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30, CancellationToken ct = default) => Ok(await service.ListAsync(take, ct));

    [HttpGet("{batchId:guid}/candidates")]
    public async Task<IActionResult> Candidates(Guid batchId, CancellationToken ct) => Ok(await service.CandidatesAsync(batchId, ct));

    [HttpPost("candidates/{candidateId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid candidateId, ConfirmIdentityCandidateRequest request, CancellationToken ct)
    {
        try { return Ok(await service.ConfirmAsync(candidateId, request, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("candidates/{candidateId:guid}/skip")]
    public async Task<IActionResult> Skip(Guid candidateId, ConfirmIdentityCandidateRequest request, CancellationToken ct)
    {
        try { return Ok(await service.SkipAsync(candidateId, request, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "IDENTITY_CANDIDATE_NOT_FOUND" => NotFound(new { code = ex.Message }),
        "IDENTITY_CANDIDATE_NOT_READY" or "ENTRA_IDENTITY_ALREADY_LINKED" or "EMPLOYEE_ALREADY_HAS_ACTIVE_IDENTITY" => Conflict(new { code = ex.Message }),
        _ when ex.Message.StartsWith("CSV_HEADER_MISSING:", StringComparison.Ordinal) => BadRequest(new { code = "CSV_HEADER_MISSING", detail = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
