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
    int? UnpaidGracePeriodDays = null,
    int? IncludedSeats = null,
    bool? OverageAllowed = null,
    bool IsTest = false);

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
    TenantOwnerInviteRequest? OwnerInvite = null) : IRequest<Result<CreateTenantDraftResponseDto>>, IAuditedCommand<Result<CreateTenantDraftResponseDto>>
{
    public AuditEntry DescribeAudit(Result<CreateTenantDraftResponseDto>? response)
        => AuditEntry.For("tenant.created", "tenant", response?.Value?.TenantId, response?.Value?.TenantId, new { Name, Slug, IndustryProfile, CompanySizeRange, LegalEntityName, RegistrationNumber, Country, Timezone, Currency, PlanId = Subscription?.PlanId, BillingCycle = Subscription?.BillingCycle, CommercialModel = Subscription?.CommercialModel, OwnerInviteEmail = OwnerInvite?.Email });
}
