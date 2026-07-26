namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;

public sealed record PlatformProviderCardReadModel(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string ProviderFamily,
    bool Configured,
    bool ConfigurationActive,
    DateTimeOffset? LastVerifiedAt);

/// <summary>
/// Read-only access to ONEVO-managed provider catalog metadata.
/// No create, update, or delete operation is intentionally exposed.
/// </summary>
public interface IPlatformProviderRepository
{
    Task<IReadOnlyList<PlatformProviderCardReadModel>> ListActiveCardsAsync(
        CancellationToken cancellationToken);

    Task<string?> GetActiveFamilyAsync(
        string providerKey,
        CancellationToken cancellationToken);

    Task<bool> IsActiveInFamilyAsync(
        string providerKey,
        IReadOnlySet<string> providerFamilies,
        CancellationToken cancellationToken);
}
