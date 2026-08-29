using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/evidence-artifacts"), Authorize(Policy = Policies.EvidenceRegistrar)]
public sealed class EvidenceArtifactsController(EvidenceArtifactService service) : ControllerBase
{
    [HttpPost]
    public Task<EvidenceArtifactDto> Register(RegisterEvidenceArtifactRequest request, CancellationToken ct) => service.RegisterAsync(request, ct);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EvidenceArtifactDto>> Get(Guid id, CancellationToken ct)
    {
        var x = await service.GetAsync(id, ct); return x is null ? NotFound() : Ok(x);
    }
}
