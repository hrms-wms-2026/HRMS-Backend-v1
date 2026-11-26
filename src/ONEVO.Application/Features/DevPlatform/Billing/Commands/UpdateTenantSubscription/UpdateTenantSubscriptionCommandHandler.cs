using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.Mappers;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateTenantSubscription;

public sealed class UpdateTenantSubscriptionCommandHandler
    : IRequestHandler<UpdateTenantSubscriptionCommand, Result<TenantSubscriptionDetailDto>>
{
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantSubscriptionRepository _subscriptionRepo;
    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly IBillingAuditLogRepository _auditLogRepo;
    private readonly ISeatEntitlementService _seats;
    private readonly ICurrentPlatformUserContext _platformUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateTenantSubscriptionCommandHandler(
        ITenantRepository tenantRepo,
        ITenantSubscriptionRepository subscriptionRepo,
        ISubscriptionPlanRepository planRepo,
        IBillingAuditLogRepository auditLogRepo,
        ISeatEntitlementService seats,
        ICurrentPlatformUserContext platformUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenantRepo = tenantRepo;
        _subscriptionRepo = subscriptionRepo;
        _planRepo = planRepo;
        _auditLogRepo = auditLogRepo;
        _seats = seats;
        _platformUser = platformUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantSubscriptionDetailDto>> Handle(
        UpdateTenantSubscriptionCommand request,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<TenantSubscriptionDetailDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var subscription = await _subscriptionRepo.GetByTenantIdAsync(request.TenantId, ct);
        if (subscription is null)
            return Result<TenantSubscriptionDetailDto>.NotFound(
                $"Subscription for tenant '{request.TenantId}' not found.");

        var before = new
        {
            included_seats = subscription.IncludedSeats,
            overage_allowed = subscription.OverageAllowed,
            commercial_model = subscription.CommercialModel,
            status = subscription.Status,
        };

        if (request.IncludedSeats is not null)
        {
            subscription.IncludedSeats = request.IncludedSeats.Value;
            // A seat count without an overage rule still evaluates as "Undetermined".
            subscription.OverageAllowed ??= false;
        }

        if (request.OverageAllowed is not null)
            subscription.OverageAllowed = request.OverageAllowed.Value;

        if (request.IsTest == true && !CommercialModels.IsTest(subscription))
            CommercialModels.ApplyTestMode(subscription);
        else if (request.IsTest == false && CommercialModels.IsTest(subscription))
            subscription.CommercialModel = CommercialModels.Subscription;

        var now = _clock.UtcNow;
        subscription.UpdatedAt = now;

        var after = new
        {
            included_seats = subscription.IncludedSeats,
            overage_allowed = subscription.OverageAllowed,
            commercial_model = subscription.CommercialModel,
            status = subscription.Status,
        };

        await _auditLogRepo.AddAsync(new BillingAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ActorAdminUserId = _platformUser.UserId,
            Action = "subscription.allocation_updated",
            Message = BuildMessage(subscription),
            MetadataJson = JsonSerializer.Serialize(new { before, after }),
            CreatedAt = now,
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        var plan = await _planRepo.GetByIdAsync(subscription.PlanId, ct);
        var seatDecision = await _seats.EvaluateAsync(tenant.Id, ct);

        return Result<TenantSubscriptionDetailDto>.Success(
            TenantSubscriptionMapper.ToDetailDto(tenant, subscription, plan, now, seatDecision.ActiveEmployeeCount));
    }

    private static string BuildMessage(TenantSubscription subscription)
    {
        var seats = subscription.IncludedSeats?.ToString() ?? "not set";
        var overage = subscription.OverageAllowed == true ? "overage allowed" : "no overage";
        var kind = CommercialModels.IsTest(subscription) ? "test tenant" : "billed tenant";
        return $"Seat allocation set to {seats} ({overage}); {kind}.";
    }
}
