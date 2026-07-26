using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;

public class CreateTenantCommandHandler
    : IRequestHandler<CreateTenantCommand, Result<CreateTenantDraftResponseDto>>
{
    private static readonly IReadOnlyList<string> DefaultCompletionMethods =
        new[] { "password", "google" };

    private readonly ITenantRepository _tenants;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ITenantAuthPolicyRepository _authPolicies;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly IDefaultRoleSeeder _roleSeeder;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantOwnerInvitationService _invitationService;

    public CreateTenantCommandHandler(
        ITenantRepository tenants,
        ILegalEntityRepository legalEntities,
        ITenantAuthPolicyRepository authPolicies,
        ISubscriptionPlanRepository plans,
        ITenantSubscriptionRepository subscriptions,
        IDefaultRoleSeeder roleSeeder,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ITenantOwnerInvitationService invitationService)
    {
        _tenants = tenants;
        _legalEntities = legalEntities;
        _authPolicies = authPolicies;
        _plans = plans;
        _subscriptions = subscriptions;
        _roleSeeder = roleSeeder;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _invitationService = invitationService;
    }

    public async Task<Result<CreateTenantDraftResponseDto>> Handle(
        CreateTenantCommand request,
        CancellationToken ct)
    {
        // 1. Auth check
        if (!_currentUser.IsAuthenticated)
            return Result<CreateTenantDraftResponseDto>.Forbidden("Authentication required.");

        // 2. Slug validation
        var slug = request.Slug.Trim().ToLowerInvariant();
        var name = request.Name.Trim();
        if (await _tenants.SlugExistsAsync(slug, excludeId: null, ct))
            return Result<CreateTenantDraftResponseDto>.Conflict($"slug '{slug}' is already taken.");

        // 3. Plan lookup — fail early if not found
        var plan = await _plans.GetByIdAsync(request.Subscription.PlanId, ct);
        if (plan is null)
            return Result<CreateTenantDraftResponseDto>.NotFound(
                $"Subscription plan '{request.Subscription.PlanId}' not found.");

        // 3a. Validate trial/grace overrides before any entity creation
        var trialDays = request.Subscription.TrialPeriodDays ?? plan.TrialPeriodDays;
        var graceDays = request.Subscription.UnpaidGracePeriodDays ?? plan.UnpaidGracePeriodDays;
        if (trialDays < 0 || graceDays < 0)
            return Result<CreateTenantDraftResponseDto>.Failure("Trial period days and unpaid grace period days must be non-negative.", 400);

        var now = _clock.UtcNow;
        var settingsJson = JsonSerializer.Serialize(new { default_timezone = request.Timezone.Trim() });
        var companySize = request.CompanySizeRange.Trim();
        var billingCycle = request.Subscription.BillingCycle;
        var isAnnual = billingCycle.Equals("annual", StringComparison.OrdinalIgnoreCase);

        // 4. Tenant entity
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = name,
            IndustryProfile = request.IndustryProfile.Trim(),
            CompanySizeRange = companySize,
            Status = TenantStatus.Provisioning,
            SettingsJson = settingsJson,
            CreatedAt = now
        };
        await _tenants.AddAsync(tenant, ct);

        // 5. Legal entity
        await _legalEntities.AddAsync(new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = request.LegalEntityName.Trim(),
            RegistrationNumber = request.RegistrationNumber?.Trim(),
            CountryCode = request.Country.Trim().ToUpperInvariant(),
            CurrencyCode = request.Currency.Trim().ToUpperInvariant(),
            IsPrimary = true,
            CreatedAt = now
        }, ct);

        // 6. Tenant auth policy
        await _authPolicies.AddAsync(
            new TenantAuthPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                PasswordCompletionAllowed = true,
                GoogleCompletionAllowed = true,
                GoogleEmailMismatchDefault = false,
                CreatedAt = now
            },
            ct);

        // 7. Tenant subscription — full snapshot of the plan
        // trialDays/graceDays already resolved and validated in step 3a above.

        // Compute trial dates
        DateTimeOffset? trialStartDate = trialDays > 0 ? now : null;
        DateTimeOffset? trialEndDate = trialDays > 0 ? now.AddDays(trialDays) : null;
        string subscriptionStatus = trialDays > 0 ? "trialing" : "active";

        var subscription = new TenantSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            PlanId = plan.Id,
            BillingCycle = billingCycle,
            CommercialModel = request.Subscription.CommercialModel,
            Status = subscriptionStatus,
            CurrentPeriodStart = DateOnly.FromDateTime(now.UtcDateTime),
            CurrentPeriodEnd = isAnnual
                ? DateOnly.FromDateTime(now.AddYears(1).UtcDateTime)
                : DateOnly.FromDateTime(now.AddMonths(1).UtcDateTime),
            ContractStartDate = DateOnly.FromDateTime(now.UtcDateTime),
            CompanySizeRange = companySize,
            BillingCurrency = plan.Currency,
            SelectedModulesJson = plan.IncludedModulesJson ?? "[]",
            CalculatedMonthlyPrice = plan.CalculatedMonthlyPrice,
            CalculatedAnnualPrice = plan.CalculatedAnnualPrice,
            OverrideMonthlyPrice = plan.OverrideMonthlyPrice,
            OverrideAnnualPrice = plan.OverrideAnnualPrice,
            AiTokenLimitPerMonth = plan.AiTokenLimitPerMonth,
            TrialStartDate = trialStartDate,
            TrialEndDate = trialEndDate,
            UnpaidGracePeriodDays = graceDays,
            // AccessEndsAt: null initially; set by billing system when subscription lapses
            CreatedAt = now
        };
        await _subscriptions.AddAsync(subscription, ct);

        // 8. Seed Owner role (does NOT call SaveChanges)
        var ownerRole = await _roleSeeder.SeedOwnerRoleAsync(
            tenant.Id,
            plan.GetIncludedModules(),
            ct);

        // 10. Create invite records + queue the invite email outbox message
        //     (no SaveChanges yet; the tenant name is passed along because the
        //     tenant row is still unsaved at this point).
        PendingOwnerInvite? pendingInvite = null;
        if (request.OwnerInvite is not null)
        {
            var inviteRequest = NormalizeCompletionMethods(request.OwnerInvite);
            var inviteResult = await _invitationService.CreateInviteRecordsAsync(
                tenant.Id,
                ownerRole.Id,
                inviteRequest,
                ct);

            if (!inviteResult.IsSuccess)
                return Result<CreateTenantDraftResponseDto>.Failure(
                    inviteResult.Error ?? "Failed to create invite records.",
                    inviteResult.StatusCode ?? 400);

            pendingInvite = inviteResult.Value! with { TenantName = name };
            await _invitationService.QueueInviteEmailAsync(pendingInvite, ct);
        }

        // 11. ONE commit covering tenant + legal entity + auth policy + subscription
        //     + owner role + invite records + email outbox message.
        //     The outbox worker delivers the email after this commit.
        await _unitOfWork.SaveChangesAsync(ct);

        OwnerInviteResultDto? ownerInvite = null;
        if (pendingInvite is not null)
        {
            ownerInvite = new OwnerInviteResultDto(
                UserId: pendingInvite.UserId,
                InviteExpiresAt: pendingInvite.ExpiresAt,
                DeliveryStatus: "queued");
        }

        // 13. Response
        return Result<CreateTenantDraftResponseDto>.Success(
            new CreateTenantDraftResponseDto(
                TenantId: tenant.Id,
                Status: tenant.Status.ToString().ToLowerInvariant(),
                NextStep: ownerInvite is not null ? "owner_invite" : "provisioning",
                OwnerInvite: ownerInvite));
    }

    private static TenantOwnerInviteRequest NormalizeCompletionMethods(TenantOwnerInviteRequest source)
    {
        var methods = source.CompletionMethods is { Count: > 0 }
            ? source.CompletionMethods
            : DefaultCompletionMethods;

        return new TenantOwnerInviteRequest(
            Email: source.Email,
            FirstName: source.FirstName,
            LastName: source.LastName,
            CompletionMethods: methods,
            AllowGoogleEmailMismatch: source.AllowGoogleEmailMismatch,
            AllowedEmailDomains: source.AllowedEmailDomains);
    }
}
