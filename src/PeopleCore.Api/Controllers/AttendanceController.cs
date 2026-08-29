using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/attendance"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class AttendanceController(AttendanceService service) : ControllerBase
{
    [HttpPut("employees/{employeeId:guid}/{date}"), Authorize(Policy = Policies.Hr)]
    public Task<AttendanceDayDto> Upsert(Guid employeeId, DateOnly date, UpsertAttendanceRequest request, CancellationToken ct) => service.UpsertAsync(employeeId, date, request, ct);
    [HttpPost("{id:guid}/review"), Authorize(Policy = Policies.Hr)]
    public Task<AttendanceDayDto> Review(Guid id, ReviewAttendanceRequest request, CancellationToken ct) => service.ReviewAsync(id, request, ct);
    [HttpGet("me")]
    public Task<List<AttendanceDayDto>> GetMine([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => service.GetMineAsync(from, to, ct);
}
