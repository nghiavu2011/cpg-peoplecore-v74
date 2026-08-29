using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/tax-insurance"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class TaxInsuranceController(TaxInsuranceService service) : ControllerBase
{
    [HttpPost("snapshots"), Authorize(Policy = Policies.Payroll)]
    public Task<TaxInsuranceSnapshotDto> Import(ImportTaxInsuranceSnapshotRequest request, CancellationToken ct) => service.ImportAsync(request, ct);
    [HttpGet("me/{payrollPeriod}")]
    public Task<TaxInsuranceSnapshotDto?> GetMine(string payrollPeriod, CancellationToken ct) => service.GetMineAsync(payrollPeriod, ct);
    [HttpGet("employees/{employeeId:guid}/{payrollPeriod}"), Authorize(Policy = Policies.Payroll)]
    public Task<TaxInsuranceSnapshotDto?> GetForPayroll(Guid employeeId, string payrollPeriod, CancellationToken ct) => service.GetForPayrollAsync(employeeId, payrollPeriod, ct);
}
