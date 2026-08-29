using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/runtime")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class RuntimeStatusController(RuntimeStatusService runtime) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(runtime.GetSafeStatus());
}
