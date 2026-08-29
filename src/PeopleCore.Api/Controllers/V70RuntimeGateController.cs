using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/runtime/gate")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class V70RuntimeGateController(V70RuntimeProofService proof) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await proof.BuildAsync(ct));
}
