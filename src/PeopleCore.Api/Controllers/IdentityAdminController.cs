using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class IdentityAdminController(IdentityAdminService service) : ControllerBase
{
    [HttpGet("identity-links")]
    public async Task<IActionResult> IdentityLinks([FromQuery] string? q, CancellationToken ct) => Ok(await service.SearchIdentityLinksAsync(q, ct));

    [HttpPut("identity-links/{staffCode}")]
    public async Task<IActionResult> UpsertIdentityLink(string staffCode, LinkIdentityRequest request, CancellationToken ct)
    {
        try { await service.UpsertIdentityLinkAsync(staffCode, request, ct); return NoContent(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("identity-links/{staffCode}/revoke")]
    public async Task<IActionResult> RevokeIdentityLink(string staffCode, RevokeRequest request, CancellationToken ct)
    {
        try { return await service.RevokeIdentityLinkAsync(staffCode, request, ct) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet("access-grants")]
    public async Task<IActionResult> Grants([FromQuery] Guid? employeeId, CancellationToken ct) => Ok(await service.ListGrantsAsync(employeeId, ct));

    [HttpPost("access-grants")]
    public async Task<IActionResult> CreateGrant(CreateAccessGrantRequest request, CancellationToken ct)
    {
        try { var id = await service.CreateGrantAsync(request, ct); return Created($"/api/v1/admin/access-grants/{id}", new { id }); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("access-grants/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeGrant(Guid id, RevokeRequest request, CancellationToken ct)
    {
        try { return await service.RevokeGrantAsync(id, request, ct) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "ENTRA_IDENTITY_ALREADY_LINKED" => Conflict(new { code = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
