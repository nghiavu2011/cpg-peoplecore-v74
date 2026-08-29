using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Services.Bravo;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/project-codes")]
[Authorize(Policy = PeopleCore.Api.Security.Policies.PeopleCoreUser)]
public sealed class ProjectCodesController(BravoIntegrationService integration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool activeOnly = true, CancellationToken ct = default)
        => Ok((await integration.ListProjectCodesAsync(activeOnly, ct)).Select(x => new
        {
            x.Code, x.Name, x.ParentCode, x.Status, x.ValidFrom, x.ValidTo, x.SourceSystem, x.SourceRevision, x.SyncedAt
        }));
}
