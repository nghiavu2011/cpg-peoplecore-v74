using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Bravo;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/integrations/bravo/adapter/pilot")]
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class BravoAdapterPilotController(BravoAdapterPilotService pilot) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(pilot.GetStatus());

    [HttpPost("outbound/{messageId:guid}/preview")]
    public async Task<IActionResult> PreviewOutbound(Guid messageId, CancellationToken ct)
    {
        try { return Ok(await pilot.PreviewOutboundAsync(messageId, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("fixture/preview")]
    public async Task<IActionResult> PreviewFixture(BravoAdapterFixturePreviewRequest request, CancellationToken ct)
    {
        try { return Ok(await pilot.PreviewFixtureAsync(request, ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet("evidence")]
    public async Task<IActionResult> Evidence([FromQuery] Guid? integrationMessageId, CancellationToken ct)
        => Ok((await pilot.ListEvidenceAsync(integrationMessageId, ct)).Select(x => new
        {
            x.Id, x.IntegrationMessageId, x.Direction, x.MessageType, x.MappingMode, x.MappingProfile,
            x.SourcePayloadSha256, x.MappedPayloadSha256, x.TransportMode, x.EnvelopeSha256,
            x.SignatureHex, x.Nonce, x.Status, x.CorrelationId, x.CreatedBy, x.CreatedAt
        }));

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "BRAVO_MAPPING_NOT_CONFIGURED" or "BRAVO_TRANSPORT_NOT_CONFIGURED" => Conflict(new { code = ex.Message }),
        "BRAVO_SOURCE_PAYLOAD_HASH_MISMATCH" => Conflict(new { code = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
