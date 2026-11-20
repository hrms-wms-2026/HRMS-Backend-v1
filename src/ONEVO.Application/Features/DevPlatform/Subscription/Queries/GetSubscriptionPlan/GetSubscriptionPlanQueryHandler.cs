using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;

public sealed class GetSubscriptionPlanQueryHandler
    : IRequestHandler<GetSubscriptionPlanQuery, Result<SubscriptionPlanDetailDto>>
{
    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly RateLimitAdminService? _rateLimits;

    public GetSubscriptionPlanQueryHandler(ISubscriptionPlanRepository planRepo, RateLimitAdminService? rateLimits = null)
    {
        _planRepo = planRepo;
        _rateLimits = rateLimits;
    }

    public async Task<Result<SubscriptionPlanDetailDto>> Handle(
        GetSubscriptionPlanQuery request,
        CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result<SubscriptionPlanDetailDto>.NotFound(
                $"Subscription plan '{request.PlanId}' not found.");

        var detail = SubscriptionPlanMapper.ToDetailDto(plan);
        if (request.IncludeRateLimits && _rateLimits is not null)
            detail = detail with { RateLimits = (await _rateLimits.BuildPlanAsync(plan.Id, ct)).Limits };

        return Result<SubscriptionPlanDetailDto>.Success(detail);
    }
}
