using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Bravo;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/integrations/bravo")]
public sealed class BravoController(IBravoAdapter adapter, BravoIntegrationService integration) : ControllerBase
{
    [HttpGet("status")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public IActionResult Status() => Ok(new
    {
        adapter = adapter.GetStatus(),
        contract = new { version = BravoIntegrationService.SchemaVersion, compensationOut = BravoIntegrationService.CompensationType, projectCodesIn = BravoIntegrationService.ProjectCodeType },
        officialPayrollResultImport = "DEFERRED_NOT_IN_V67"
    });

    [HttpPost("compensation/outbox")]
    [Authorize(Policy = Policies.HrOrPayroll)]
    public async Task<IActionResult> QueueCompensation(ApprovedCompensationHandoffRequest request, CancellationToken ct)
    {
        try { return Ok(ToResponse(await integration.QueueApprovedCompensationAsync(request, ct))); }
        catch (KeyNotFoundException ex) { return NotFound(new { code = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    // V67 connector-test endpoint only. Real BRAVO transport must call the same canonical service after its own machine authentication is approved.
    [HttpPost("project-codes/inbox")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public async Task<IActionResult> AcceptProjectCodes(BravoProjectCodeBatchRequest request, CancellationToken ct)
    {
        try { return Ok(ToResponse(await integration.AcceptProjectCodeBatchAsync(request, ct))); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("outbox/{messageId:guid}/attempt")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public async Task<IActionResult> Attempt(Guid messageId, CancellationToken ct)
    {
        try { return Ok(ToResponse(await integration.AttemptOutboundAsync(messageId, ct))); }
        catch (KeyNotFoundException ex) { return NotFound(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet("messages")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public async Task<IActionResult> Messages([FromQuery] string? direction, [FromQuery] string? status, CancellationToken ct)
        => Ok((await integration.ListMessagesAsync(direction, status, ct)).Select(ToResponse));

    private static IntegrationEnvelopeResponse ToResponse(PeopleCore.Api.Domain.IntegrationMessage x) => new(
        x.Id, x.Direction, x.MessageType, x.IdempotencyKey, x.PayloadSha256, x.Status, x.AttemptCount,
        x.CreatedAt, x.ProcessedAt, x.ExternalReference, x.LastError);

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "IDEMPOTENCY_CONFLICT" or "APPROVAL_REFERENCE_CONFLICT" => Conflict(new { code = ex.Message }),
        _ when ex.Message.StartsWith("PROJECT_CODE_DATE_RANGE_INVALID:", StringComparison.Ordinal) => BadRequest(new { code = "PROJECT_CODE_DATE_RANGE_INVALID", detail = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
