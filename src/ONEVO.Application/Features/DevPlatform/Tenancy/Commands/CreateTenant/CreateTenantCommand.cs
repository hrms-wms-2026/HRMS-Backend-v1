using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

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
    TenantOwnerInviteRequest? OwnerInvite = null) : IRequest<Result<CreateTenantDraftResponseDto>>;
