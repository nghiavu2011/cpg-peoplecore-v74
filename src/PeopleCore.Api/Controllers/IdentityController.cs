using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Security;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/v1/identity")]
public sealed class IdentityController(PeopleCoreDbContext db, ICurrentUser current, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (current.EmployeeId is not Guid employeeId) return Forbid();
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == employeeId, ct);
        var roles = http.HttpContext!.User.Claims.Where(c => c.Type == PeopleCoreClaims.Role && c.Issuer == PeopleCoreClaims.Issuer).Select(c => c.Value).Distinct().Order().ToArray();
        var scopes = http.HttpContext!.User.Claims.Where(c => c.Type == PeopleCoreClaims.Scope && c.Issuer == PeopleCoreClaims.Issuer).Select(c => c.Value).Distinct().Order().ToArray();
        return Ok(new
        {
            employee.Id, employee.StaffCode, employee.DisplayName, employee.CorporateEmail,
            tenantId = current.EntraTenantId, entraObjectId = current.EntraObjectId,
            roles, scopes, identitySource = http.HttpContext!.User.HasClaim(c => c.Type == TrialAuthenticationHandler.AuthModeClaim && c.Value == TrialAuthenticationHandler.AuthModeValue) ? "LOCAL TRIAL HARNESS (NOT ENTRA EVIDENCE)" : "Microsoft Entra ID", authorizationSource = "PeopleCore PostgreSQL",
            passwordStoredByPeopleCore = false
        });
    }
}
