using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Queries.ListSubscriptionPlans;

public sealed class ListSubscriptionPlansQueryHandler
    : IRequestHandler<ListSubscriptionPlansQuery, Result<IReadOnlyList<SubscriptionPlanSummaryDto>>>
{
    private readonly ISubscriptionPlanRepository _planRepo;

    public ListSubscriptionPlansQueryHandler(ISubscriptionPlanRepository planRepo)
        => _planRepo = planRepo;

    public async Task<Result<IReadOnlyList<SubscriptionPlanSummaryDto>>> Handle(
        ListSubscriptionPlansQuery request,
        CancellationToken ct)
    {
        var plans = await _planRepo.ListAsync(ct);
        var dtos = plans.Select(SubscriptionPlanMapper.ToSummaryDto).ToList();
        return Result<IReadOnlyList<SubscriptionPlanSummaryDto>>.Success(dtos);
    }
}
