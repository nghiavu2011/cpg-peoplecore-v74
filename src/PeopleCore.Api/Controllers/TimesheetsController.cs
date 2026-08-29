using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/timesheets"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class TimesheetsController(TimesheetService service) : ControllerBase
{
    [HttpPost("me")]
    public Task<TimesheetEntryDto> CreateMine(CreateTimesheetEntryRequest request, CancellationToken ct) => service.CreateMineAsync(request, ct);
    [HttpGet("me")]
    public Task<List<TimesheetEntryDto>> GetMine([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => service.GetMineAsync(from, to, ct);
    [HttpPost("validate-project")]
    public Task<TimesheetValidationDto> ValidateProject(ValidateTimesheetProjectRequest request, CancellationToken ct) => service.ValidateProjectAsync(request, ct);
    [HttpPost("work-rule-evidence"), Authorize(Policy = Policies.Hr)]
    public Task<WorkRuleDto> WorkRuleEvidence(CancellationToken ct) => service.GetWorkRuleWithEvidenceAsync(ct);
}
