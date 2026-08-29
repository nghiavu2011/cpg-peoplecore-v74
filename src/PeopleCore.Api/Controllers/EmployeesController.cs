using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/employees")]
public sealed class EmployeesController(EmployeeMasterService service, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int? take, CancellationToken ct)
    {
        var max = config.GetValue("Authorization:MaxDirectoryPageSize", 100);
        var def = config.GetValue("Authorization:DirectoryPageSize", 50);
        var size = Math.Clamp(take ?? def, 1, max);
        return Ok(await service.SearchAsync(q, size, ct));
    }

    [HttpGet("{staffCode}")]
    public async Task<IActionResult> GetByStaffCode(string staffCode, CancellationToken ct)
    {
        try { var row = await service.GetByStaffCodeAsync(staffCode, ct); return row is null ? NotFound() : Ok(row); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("by-id/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try { var row = await service.GetByIdAsync(id, ct); return row is null ? NotFound() : Ok(row); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost]
    [Authorize(Policy = Policies.Hr)]
    public async Task<IActionResult> Create(CreateEmployeeRequest request, CancellationToken ct)
    {
        try { var row = await service.CreateAsync(request, ct); return Created($"/api/v1/employees/{row.Work.StaffCode}", row); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPatch("{staffCode}")]
    [Authorize(Policy = Policies.Hr)]
    public async Task<IActionResult> Patch(string staffCode, PatchEmployeeRequest request, CancellationToken ct)
    {
        try { var row = await service.PatchAsync(staffCode, request, ct); return row is null ? NotFound() : Ok(row); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    [HttpPost("{staffCode}/assignments")]
    [Authorize(Policy = Policies.Hr)]
    public async Task<IActionResult> AddAssignment(string staffCode, CreateAssignmentRequest request, CancellationToken ct)
    {
        try
        {
            var id = await service.AddAssignmentAsync(staffCode, request, ct);
            return id is null ? NotFound() : Created($"/api/v1/employees/{staffCode}/assignments/{id}", new { id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return DomainError(ex); }
    }

    private IActionResult DomainError(InvalidOperationException ex) => ex.Message switch
    {
        "EMPLOYEE_ALREADY_EXISTS" or "EMPLOYEE_VERSION_CONFLICT" or "ASSIGNMENT_EFFECTIVE_DATE_OVERLAP" => Conflict(new { code = ex.Message }),
        _ => BadRequest(new { code = ex.Message })
    };
}
