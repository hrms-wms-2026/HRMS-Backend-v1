using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetProvisioningSummary;

public class GetProvisioningSummaryQueryHandler
    : IRequestHandler<GetProvisioningSummaryQuery, Result<ProvisioningSummaryDto>>
{
    private readonly ITenantRepository _tenants;
    private readonly IInvitationTokenRepository _invitations;
    private readonly ITenantSubscriptionStatusReader _subscriptionReader;
    private readonly ITenantModuleStatusReader _moduleReader;
    private readonly ITenantRoleStatusReader _roleReader;
    private readonly ITenantSettingsStatusReader _settingsReader;
    private readonly IDateTimeProvider _clock;

    public GetProvisioningSummaryQueryHandler(
        ITenantRepository tenants,
        IInvitationTokenRepository invitations,
        ITenantSubscriptionStatusReader subscriptionReader,
        ITenantModuleStatusReader moduleReader,
        ITenantRoleStatusReader roleReader,
        ITenantSettingsStatusReader settingsReader,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _invitations = invitations;
        _subscriptionReader = subscriptionReader;
        _moduleReader = moduleReader;
        _roleReader = roleReader;
        _settingsReader = settingsReader;
        _clock = clock;
    }

    public async Task<Result<ProvisioningSummaryDto>> Handle(
        GetProvisioningSummaryQuery request,
        CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<ProvisioningSummaryDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        var tenantDetails = ComputeTenantDetailsStatus(tenant);
        var ownerInvite = await ComputeOwnerInviteStatusAsync(tenant.Id, ct);

        var subscription = await _subscriptionReader.GetAsync(tenant.Id, ct);
        var modules = await _moduleReader.GetAsync(tenant.Id, ct);
        var roles = await _roleReader.GetAsync(tenant.Id, ct);
        var settings = await _settingsReader.GetAsync(tenant.Id, ct);

        var sections = new ProvisioningSectionsDto(
            TenantDetails: TenancyMapper.ToSectionStatusDto(tenantDetails),
            Subscription: TenancyMapper.ToSectionStatusDto(subscription),
            Modules: TenancyMapper.ToSectionStatusDto(modules),
            Roles: TenancyMapper.ToSectionStatusDto(roles),
            Settings: TenancyMapper.ToSectionStatusDto(settings),
            OwnerInvite: TenancyMapper.ToSectionStatusDto(ownerInvite));

        var allBlockers = tenantDetails.BlockingErrors
            .Concat(ownerInvite.BlockingErrors)
            .Concat(subscription.BlockingErrors)
            .Concat(modules.BlockingErrors)
            .Concat(roles.BlockingErrors)
            .Concat(settings.BlockingErrors)
            .Select(TenancyMapper.ToIssueDto)
            .ToList();

        var allWarnings = tenantDetails.Warnings
            .Concat(ownerInvite.Warnings)
            .Concat(subscription.Warnings)
            .Concat(modules.Warnings)
            .Concat(roles.Warnings)
            .Concat(settings.Warnings)
            .Select(TenancyMapper.ToIssueDto)
            .ToList();

        var canActivate =
            tenant.Status == TenantStatus.Provisioning &&
            tenantDetails.Complete &&
            ownerInvite.Complete &&
            subscription.Complete &&
            modules.Complete &&
            roles.Complete &&
            settings.Complete;

        return Result<ProvisioningSummaryDto>.Success(new ProvisioningSummaryDto(
            TenantId: tenant.Id,
            Status: tenant.Status.ToString().ToLowerInvariant(),
            Sections: sections,
            CanActivate: canActivate,
            BlockingErrors: allBlockers,
            Warnings: allWarnings));
    }

    private static ProvisioningSectionStatus ComputeTenantDetailsStatus(Tenant tenant)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(tenant.Name)) missing.Add("name");
        if (string.IsNullOrWhiteSpace(tenant.Slug)) missing.Add("slug");
        if (string.IsNullOrWhiteSpace(tenant.IndustryProfile)) missing.Add("industry_profile");

        var complete = missing.Count == 0;
        var summary = new Dictionary<string, object?>
        {
            ["name"] = tenant.Name,
            ["slug"] = tenant.Slug,
            ["industry_profile"] = tenant.IndustryProfile
        };

        var blockers = new List<ProvisioningIssue>();
        if (!complete)
        {
            blockers.Add(new ProvisioningIssue(
                Code: "tenant_details_incomplete",
                Message: $"missing required tenant_details fields: {string.Join(", ", missing)}.",
                Section: "tenant_details"));
        }

        return new ProvisioningSectionStatus(
            Complete: complete,
            Summary: summary,
            MissingFields: missing,
            BlockingErrors: blockers,
            Warnings: Array.Empty<ProvisioningIssue>());
    }

    private async Task<ProvisioningSectionStatus> ComputeOwnerInviteStatusAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var accepted = await _invitations.HasAcceptedInviteByTenantAsync(tenantId, ct);
        var pendingCount = await _invitations.CountActiveByTenantAsync(tenantId, now, ct);
        var blockers = new List<ProvisioningIssue>();

        if (!accepted)
        {
            blockers.Add(new ProvisioningIssue(
                Code: pendingCount > 0 ? "owner_invite_pending" : "owner_invite_missing",
                Message: pendingCount > 0
                    ? "owner has been invited but has not yet set their password and phone number."
                    : "send a tenant owner invite before activation.",
                Section: "owner_invite"));
        }

        return new ProvisioningSectionStatus(
            Complete: accepted,
            Summary: new Dictionary<string, object?>
            {
                ["accepted"] = accepted,
                ["pending_invites"] = pendingCount
            },
            MissingFields: accepted ? Array.Empty<string>() : new[] { "accepted_owner_invite" },
            BlockingErrors: blockers,
            Warnings: Array.Empty<ProvisioningIssue>());
    }

}
