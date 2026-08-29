using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/contracts"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class ContractsController(ContractLifecycleService service, ICurrentUser current) : ControllerBase
{
    [HttpPost("employees/{employeeId:guid}"), Authorize(Policy = Policies.Hr)]
    public Task<ContractDto> Create(Guid employeeId, CreateContractRequest request, CancellationToken ct) => service.CreateAsync(employeeId, request, ct);

    [HttpGet("employees/{employeeId:guid}")]
    public Task<IReadOnlyList<ContractDto>> Get(Guid employeeId, CancellationToken ct) => service.GetForEmployeeAsync(employeeId, ct);

    [HttpGet("me")]
    public Task<IReadOnlyList<ContractDto>> GetMine(CancellationToken ct) => current.EmployeeId is Guid id ? service.GetForEmployeeAsync(id, ct) : throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");

    [HttpGet("employees/{employeeId:guid}/lifecycle")]
    public Task<IReadOnlyList<EmployeeLifecycleEventDto>> GetLifecycle(Guid employeeId, CancellationToken ct) => service.GetLifecycleAsync(employeeId, ct);

    [HttpGet("me/lifecycle")]
    public Task<IReadOnlyList<EmployeeLifecycleEventDto>> GetMyLifecycle(CancellationToken ct) => current.EmployeeId is Guid id ? service.GetLifecycleAsync(id, ct) : throw new UnauthorizedAccessException("IDENTITY_NOT_MAPPED");
}
