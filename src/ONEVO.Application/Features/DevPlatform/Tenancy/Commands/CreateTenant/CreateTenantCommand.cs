using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;

public sealed record SubscriptionInfo(
    Guid PlanId,
    string BillingCycle,
    string CommercialModel,
    int? TrialPeriodDays = null,
    int? UnpaidGracePeriodDays = null);

public sealed record CreateTenantCommand(
    string Name,
    string Slug,
    string IndustryProfile,
    string CompanySizeRange,
    string LegalEntityName,
    string? RegistrationNumber,
    string Country,
    string Timezone,
    string Currency,
    SubscriptionInfo Subscription,
    TenantOwnerInviteRequest? OwnerInvite = null) : IRequest<Result<CreateTenantDraftResponseDto>>, IPlatformAuditedCommand<Result<CreateTenantDraftResponseDto>>
{
    // The owner invite is recorded as a flag and user id only: no email address or names.
    public AuditEntry DescribeAudit(Result<CreateTenantDraftResponseDto> response) => new()
    {
        Action = "tenant.created",
        TargetType = "tenant",
        TargetId = response.Value?.TenantId.ToString(),
        TargetTenantId = response.Value?.TenantId,
        After = new
        {
            name = Name,
            slug = Slug,
            industryProfile = IndustryProfile,
            companySizeRange = CompanySizeRange,
            country = Country,
            timezone = Timezone,
            currency = Currency,
            planId = Subscription.PlanId,
            billingCycle = Subscription.BillingCycle,
            commercialModel = Subscription.CommercialModel,
            trialPeriodDays = Subscription.TrialPeriodDays,
            unpaidGracePeriodDays = Subscription.UnpaidGracePeriodDays,
            ownerInviteQueued = response.Value?.OwnerInvite is not null,
            ownerUserId = response.Value?.OwnerInvite?.UserId,
        },
    };
}
