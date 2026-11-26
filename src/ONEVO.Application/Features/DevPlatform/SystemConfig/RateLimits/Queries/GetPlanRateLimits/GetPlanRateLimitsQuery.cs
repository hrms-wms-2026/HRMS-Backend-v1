using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetPlanRateLimits;

/// <summary>A plan's overrides of SignedIn, TrayDevice and TenantCeiling, and what each inherits from global.</summary>
public sealed record GetPlanRateLimitsQuery(Guid SubscriptionPlanId) : IRequest<Result<PlanRateLimitsDto>>;

public sealed class GetPlanRateLimitsQueryHandler : IRequestHandler<GetPlanRateLimitsQuery, Result<PlanRateLimitsDto>>
{
    private readonly RateLimitAdminService _service;

    public GetPlanRateLimitsQueryHandler(RateLimitAdminService service) => _service = service;

    public Task<Result<PlanRateLimitsDto>> Handle(GetPlanRateLimitsQuery request, CancellationToken ct)
        => _service.GetPlanAsync(request.SubscriptionPlanId, ct);
}
