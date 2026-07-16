using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Queries.GetSubscriptionPlan;

public sealed class GetSubscriptionPlanQueryHandler
    : IRequestHandler<GetSubscriptionPlanQuery, Result<SubscriptionPlanDetailDto>>
{
    private readonly ISubscriptionPlanRepository _planRepo;

    public GetSubscriptionPlanQueryHandler(ISubscriptionPlanRepository planRepo)
        => _planRepo = planRepo;

    public async Task<Result<SubscriptionPlanDetailDto>> Handle(
        GetSubscriptionPlanQuery request,
        CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result<SubscriptionPlanDetailDto>.NotFound(
                $"Subscription plan '{request.PlanId}' not found.");

        return Result<SubscriptionPlanDetailDto>.Success(SubscriptionPlanMapper.ToDetailDto(plan));
    }
}
