using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Api.Data;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Payroll;

namespace PeopleCore.Api.Controllers;

[ApiController]
[Route("api/payroll/reconciliation")]
[Authorize(Policy = Policies.Payroll)]
public sealed class PayrollReconciliationController(PeopleCoreDbContext db, IPayrollReconciliationService service) : ControllerBase
{
    [HttpPost("{payrollPeriod}/{employeeId:guid}/run")]
    public async Task<IActionResult> Run(string payrollPeriod, Guid employeeId, CancellationToken ct)
        => Ok(await service.RunAsync(employeeId, payrollPeriod, ct));

    [HttpGet("{payrollPeriod}/{employeeId:guid}")]
    public async Task<IActionResult> Get(string payrollPeriod, Guid employeeId, CancellationToken ct)
    {
        var items = await db.ReconciliationItems.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PayrollPeriod == payrollPeriod)
            .OrderBy(x => x.ComponentCode)
            .ToListAsync(ct);
        return Ok(new
        {
            employeeId,
            payrollPeriod,
            officialSource = "BRAVO",
            shadowOfficial = false,
            payslipReleaseBlocked = items.Any(x => x.Status == "REVIEW" && x.ResolvedAt == null),
            items
        });
    }
}
