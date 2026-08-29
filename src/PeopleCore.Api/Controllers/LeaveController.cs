using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/leave"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class LeaveController(LeaveService service) : ControllerBase
{
    [HttpPost("me")]
    public Task<LeaveRequestDto> CreateMine(CreateLeaveRequest request, CancellationToken ct) => service.CreateMineAsync(request, ct);
    [HttpGet("me")]
    public Task<List<LeaveRequestDto>> GetMine(CancellationToken ct) => service.GetMineAsync(ct);
    [HttpGet("pending-for-me")]
    public Task<List<LeaveRequestDto>> GetPendingForMe(CancellationToken ct) => service.GetPendingForActorAsync(ct);
    [HttpPost("{id:guid}/decision")]
    public Task<LeaveRequestDto> Decide(Guid id, DecideLeaveRequest request, CancellationToken ct) => service.DecideAsync(id, request, ct);
}
