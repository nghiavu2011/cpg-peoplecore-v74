using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/overtime"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class OvertimeController(OvertimeService service) : ControllerBase
{
    [HttpPost("me")]
    public Task<OvertimeRequestDto> CreateMine(CreateOvertimeRequest request, CancellationToken ct) => service.CreateMineAsync(request, ct);
    [HttpGet("me")]
    public Task<List<OvertimeRequestDto>> GetMine(CancellationToken ct) => service.GetMineAsync(ct);
    [HttpGet("pending-for-me")]
    public Task<List<OvertimeRequestDto>> GetPendingForMe(CancellationToken ct) => service.GetPendingForActorAsync(ct);
    [HttpPost("{id:guid}/decision")]
    public Task<OvertimeRequestDto> Decide(Guid id, DecideOvertimeRequest request, CancellationToken ct) => service.DecideAsync(id, request, ct);
}
