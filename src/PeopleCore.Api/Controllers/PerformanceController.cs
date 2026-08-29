using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Api.Contracts;
using PeopleCore.Api.Security;
using PeopleCore.Api.Services.Functional;

namespace PeopleCore.Api.Controllers;

[ApiController, Route("api/v1/performance"), Authorize(Policy = Policies.PeopleCoreUser)]
public sealed class PerformanceController(PerformanceService service) : ControllerBase
{
    [HttpPost("periods"), Authorize(Policy = Policies.Hr)]
    public Task<PerformancePeriodDto> CreatePeriod(CreatePerformancePeriodRequest request, CancellationToken ct) => service.CreatePeriodAsync(request, ct);
    [HttpGet("periods")]
    public Task<List<PerformancePeriodDto>> GetPeriods(CancellationToken ct) => service.GetPeriodsAsync(ct);
    [HttpGet("manager/pending")]
    public Task<List<PerformanceReviewDto>> GetManagerPending(CancellationToken ct) => service.GetManagerPendingAsync(ct);
    [HttpPost("periods/{periodId:guid}/me/self")]
    public Task<PerformanceReviewDto> SubmitSelf(Guid periodId, SubmitPerformanceSelfRequest request, CancellationToken ct) => service.SubmitSelfAsync(periodId, request, ct);
    [HttpGet("periods/{periodId:guid}/me")]
    public Task<PerformanceReviewDto?> GetMine(Guid periodId, CancellationToken ct) => service.GetMineAsync(periodId, ct);
    [HttpPost("reviews/{reviewId:guid}/manager")]
    public Task<PerformanceReviewDto> ManagerReview(Guid reviewId, SubmitPerformanceManagerRequest request, CancellationToken ct) => service.ManagerReviewAsync(reviewId, request, ct);
}
