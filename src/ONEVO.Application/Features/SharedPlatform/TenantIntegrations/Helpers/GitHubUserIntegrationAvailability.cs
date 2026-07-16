using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;

public sealed class GitHubUserIntegrationAvailability
{
    private readonly IIntegrationCatalogRepository _catalog;
    private readonly IPlatformOAuthAppResolver _oauthApps;
    private readonly IModuleEntitlementService _entitlements;
    private readonly ITenantIntegrationCredentialRepository _tenantIntegrations;

    public GitHubUserIntegrationAvailability(
        IIntegrationCatalogRepository catalog,
        IPlatformOAuthAppResolver oauthApps,
        IModuleEntitlementService entitlements,
        ITenantIntegrationCredentialRepository tenantIntegrations)
    {
        _catalog = catalog;
        _oauthApps = oauthApps;
        _entitlements = entitlements;
        _tenantIntegrations = tenantIntegrations;
    }

    public async Task<Result<ResolvedPlatformOAuthApp>> ValidateAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var available = await ValidateTenantEnableAsync(tenantId, ct);
        if (!available.IsSuccess)
        {
            return available;
        }

        var tenantApproval = await _tenantIntegrations.GetByTenantAndIntegrationAsync(
            tenantId,
            GitHubUserOAuthRules.IntegrationKey,
            ct);
        if (tenantApproval is null || tenantApproval.Status != "connected")
        {
            return Result<ResolvedPlatformOAuthApp>.Forbidden(
                "GitHub is not enabled by the tenant administrator.");
        }

        return available;
    }

    public async Task<Result<ResolvedPlatformOAuthApp>> ValidateTenantEnableAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var integration = await _catalog.GetByKeyAsync(GitHubUserOAuthRules.IntegrationKey, ct);
        if (integration is null || !integration.IsActive)
        {
            return Result<ResolvedPlatformOAuthApp>.Failure("GitHub integration is not available.", 422);
        }

        if (!string.Equals(
                integration.OnevoAppProvider,
                GitHubUserOAuthRules.Provider,
                StringComparison.Ordinal))
        {
            return Result<ResolvedPlatformOAuthApp>.Failure(
                "GitHub OAuth configuration is unavailable.",
                422);
        }

        if (integration.ConnectionScope is not "user" and not "both")
        {
            return Result<ResolvedPlatformOAuthApp>.Failure(
                "GitHub is not configured for user connections.",
                422);
        }

        var app = await _oauthApps.GetActiveAppForProviderAsync(
            GitHubUserOAuthRules.Provider,
            ct);
        if (app is null)
        {
            return Result<ResolvedPlatformOAuthApp>.Failure(
                "GitHub OAuth configuration is unavailable.",
                422);
        }

        var linkedModules = await _catalog.GetLinkedModuleKeysAsync(
            GitHubUserOAuthRules.IntegrationKey,
            ct);
        var activeModules = await _entitlements.GetActiveModuleKeysForTenantAsync(tenantId, ct);
        var activeModuleSet = new HashSet<string>(activeModules, StringComparer.Ordinal);
        var hasEntitledLink = false;
        foreach (var linkedModule in linkedModules)
        {
            if (!activeModuleSet.Contains(linkedModule))
            {
                continue;
            }

            hasEntitledLink = true;
            break;
        }

        if (!hasEntitledLink)
        {
            return Result<ResolvedPlatformOAuthApp>.Forbidden(
                "GitHub is not available for this tenant.");
        }

        return Result<ResolvedPlatformOAuthApp>.Success(app);
    }
}
