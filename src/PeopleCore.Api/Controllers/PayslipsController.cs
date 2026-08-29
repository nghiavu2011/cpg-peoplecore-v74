using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/payslips"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class PayslipsController(PayslipService service) : ControllerBase
{
    [HttpPost("employees/{employeeId:guid}/{payrollPeriod}/preview"), Authorize(Policy = Policies.Payroll)]
    public Task<PayslipPreviewDto> Preview(Guid employeeId, string payrollPeriod, CancellationToken ct) => service.PreviewAsync(employeeId, payrollPeriod, ct);
    [HttpPost("employees/{employeeId:guid}/{payrollPeriod}/prelive-safety-check"), Authorize(Policy = Policies.Payroll)]
    public Task<TimesheetValidationDto> Safety(Guid employeeId, string payrollPeriod, CancellationToken ct) => service.SafetyCheckAsync(employeeId, payrollPeriod, ct);
    [HttpPost("employees/{employeeId:guid}/{payrollPeriod}/release"), Authorize(Policy = Policies.Payroll)]
    public Task<PayslipDto> Release(Guid employeeId, string payrollPeriod, CancellationToken ct) => service.ReleaseAsync(employeeId, payrollPeriod, ct);
    [HttpGet("me/{payrollPeriod}")]
    public Task<PayslipDto?> GetMine(string payrollPeriod, CancellationToken ct) => service.GetMineAsync(payrollPeriod, ct);
}
