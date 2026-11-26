using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Queries.GetTenantSubscription;

public sealed class GetTenantSubscriptionQueryHandler
    : IRequestHandler<GetTenantSubscriptionQuery, Result<TenantSubscriptionDetailDto>>
{
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantSubscriptionRepository _subscriptionRepo;
    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatEntitlementService _seats;

    public GetTenantSubscriptionQueryHandler(
        ITenantRepository tenantRepo,
        ITenantSubscriptionRepository subscriptionRepo,
        ISubscriptionPlanRepository planRepo,
        IDateTimeProvider clock,
        ISeatEntitlementService seats)
    {
        _tenantRepo = tenantRepo;
        _subscriptionRepo = subscriptionRepo;
        _planRepo = planRepo;
        _clock = clock;
        _seats = seats;
    }

    public async Task<Result<TenantSubscriptionDetailDto>> Handle(
        GetTenantSubscriptionQuery request,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<TenantSubscriptionDetailDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var subscription = await _subscriptionRepo.GetByTenantIdAsync(request.TenantId, ct);
        if (subscription is null)
            return Result<TenantSubscriptionDetailDto>.NotFound(
                $"Subscription for tenant '{request.TenantId}' not found.");

        var plan = await _planRepo.GetByIdAsync(subscription.PlanId, ct);
        // Same count that gates onboarding finalization, so the admin card never disagrees with it.
        var seatDecision = await _seats.EvaluateAsync(tenant.Id, ct);

        return Result<TenantSubscriptionDetailDto>.Success(
            TenantSubscriptionMapper.ToDetailDto(tenant, subscription, plan, _clock.UtcNow, seatDecision.ActiveEmployeeCount));
    }
}
