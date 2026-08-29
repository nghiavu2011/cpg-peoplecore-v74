using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/hr/migrations/employee-master")]
[Authorize(Policy = Policies.Hr)]
public sealed class EmployeeMigrationController(EmployeeMigrationService service) : ControllerBase
{
    [HttpPost("stage")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Stage([FromForm] IFormFile file, [FromForm] string? sourceSystem, CancellationToken ct)
    {
        try { return Ok(await service.StageAsync(file, sourceSystem ?? "APPROVED_HR_SOURCE", ct)); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30, CancellationToken ct = default) => Ok(await service.ListAsync(take, ct));

    [HttpGet("{batchId:guid}/rows")]
    public async Task<IActionResult> Rows(Guid batchId, CancellationToken ct) => Ok(await service.RowsAsync(batchId, ct));

    [HttpPost("{batchId:guid}/commit")]
    public async Task<IActionResult> Commit(Guid batchId, CommitMigrationRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CommitAsync(batchId, request, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "MIGRATION_BATCH_NOT_FOUND" => NotFound(new { code = ex.Message }),
        "MIGRATION_BATCH_NOT_READY" => Conflict(new { code = ex.Message }),
        _ when ex.Message.StartsWith("CSV_HEADER_MISSING:", StringComparison.Ordinal) => BadRequest(new { code = "CSV_HEADER_MISSING", detail = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
