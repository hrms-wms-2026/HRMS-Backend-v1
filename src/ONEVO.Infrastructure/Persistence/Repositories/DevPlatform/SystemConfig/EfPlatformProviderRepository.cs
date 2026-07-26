using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

public sealed class EfPlatformProviderRepository : IPlatformProviderRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformProviderRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformProviderCardReadModel>> ListActiveCardsAsync(
        CancellationToken cancellationToken)
    {
        var providers = await _db.PlatformProviders
            .AsNoTracking()
            .Where(provider => provider.IsActive)
            .OrderBy(provider => provider.ProviderFamily)
            .ThenBy(provider => provider.DisplayName)
            .ToListAsync(cancellationToken);

        var serviceKeys = await _db.PlatformServiceKeys
            .AsNoTracking()
            .Select(key => new
            {
                key.ServiceKey,
                key.IsActive,
                key.LastVerifiedAt
            })
            .ToListAsync(cancellationToken);

        var oauthApps = await _db.PlatformOAuthApps
            .AsNoTracking()
            .Select(app => new
            {
                app.Id,
                app.Provider,
                app.IsActive,
                app.LastVerifiedAt
            })
            .ToListAsync(cancellationToken);

        var activeOAuthCredentialAppIds = await _db.PlatformOAuthAppCredentials
            .AsNoTracking()
            .Where(credential => credential.IsActive)
            .Select(credential => credential.PlatformOAuthAppId)
            .ToListAsync(cancellationToken);

        var paymentConfigs = await _db.PaymentGatewayConfigs
            .AsNoTracking()
            .Select(config => new
            {
                config.Id,
                config.Provider,
                config.IsActive
            })
            .ToListAsync(cancellationToken);

        var activePaymentCredentialConfigIds = await _db.PaymentGatewayCredentials
            .AsNoTracking()
            .Where(credential => credential.IsActive)
            .Select(credential => credential.PaymentGatewayConfigId)
            .ToListAsync(cancellationToken);

        var serviceKeyByProvider = serviceKeys.ToDictionary(
            key => key.ServiceKey,
            StringComparer.Ordinal);
        var oauthAppByProvider = oauthApps.ToDictionary(
            app => app.Provider,
            StringComparer.Ordinal);
        var activeOAuthCredentialSet = activeOAuthCredentialAppIds.ToHashSet();
        var activePaymentCredentialSet = activePaymentCredentialConfigIds.ToHashSet();

        var result = new List<PlatformProviderCardReadModel>(providers.Count);
        foreach (var provider in providers)
        {
            var configured = false;
            var configurationActive = false;
            DateTimeOffset? lastVerifiedAt = null;

            if (PlatformProviderFamilies.PlatformServiceKeyFamilies.Contains(provider.ProviderFamily) &&
                serviceKeyByProvider.TryGetValue(provider.ProviderKey, out var serviceKey))
            {
                configured = true;
                configurationActive = serviceKey.IsActive;
                lastVerifiedAt = serviceKey.LastVerifiedAt;
            }
            else if (provider.ProviderFamily == PlatformProviderFamilies.OAuthApp &&
                     oauthAppByProvider.TryGetValue(provider.ProviderKey, out var oauthApp))
            {
                configured = activeOAuthCredentialSet.Contains(oauthApp.Id);
                configurationActive = oauthApp.IsActive && configured;
                lastVerifiedAt = oauthApp.LastVerifiedAt;
            }
            else if (provider.ProviderFamily == PlatformProviderFamilies.PaymentGateway)
            {
                var matchingConfigs = paymentConfigs
                    .Where(config => config.Provider == provider.ProviderKey)
                    .ToList();

                configured = matchingConfigs.Any(config =>
                    activePaymentCredentialSet.Contains(config.Id));
                configurationActive = matchingConfigs.Any(config =>
                    config.IsActive && activePaymentCredentialSet.Contains(config.Id));
            }

            result.Add(new PlatformProviderCardReadModel(
                provider.Id,
                provider.ProviderKey,
                provider.DisplayName,
                provider.ProviderFamily,
                configured,
                configurationActive,
                lastVerifiedAt));
        }

        return result;
    }

    public Task<bool> IsActiveInFamilyAsync(
        string providerKey,
        IReadOnlySet<string> providerFamilies,
        CancellationToken cancellationToken)
    {
        var families = providerFamilies.ToArray();
        return _db.PlatformProviders
            .AsNoTracking()
            .AnyAsync(
                provider =>
                    provider.ProviderKey == providerKey &&
                    provider.IsActive &&
                    families.Contains(provider.ProviderFamily),
                cancellationToken);
    }

    public Task<string?> GetActiveFamilyAsync(
        string providerKey,
        CancellationToken cancellationToken) =>
        _db.PlatformProviders
            .AsNoTracking()
            .Where(provider =>
                provider.ProviderKey == providerKey &&
                provider.IsActive)
            .Select(provider => provider.ProviderFamily)
            .SingleOrDefaultAsync(cancellationToken);
}
