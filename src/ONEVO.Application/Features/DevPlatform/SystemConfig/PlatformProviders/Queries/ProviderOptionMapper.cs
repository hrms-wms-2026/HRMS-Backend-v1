using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries;

/// <summary>
/// Filters and maps provider catalog cards down to the minimal selection option shape
/// used by the screen-specific System Config provider option endpoints.
/// </summary>
internal static class ProviderOptionMapper
{
    public static IReadOnlyList<ProviderOptionDto> ToOptions(
        IReadOnlyList<PlatformProviderCardReadModel> cards,
        IReadOnlySet<string> allowedProviderFamilies)
    {
        var matchingCards = new List<PlatformProviderCardReadModel>();
        foreach (var card in cards)
        {
            if (allowedProviderFamilies.Contains(card.ProviderFamily))
            {
                matchingCards.Add(card);
            }
        }

        var orderedCards = matchingCards
            .OrderBy(card => card.DisplayName, StringComparer.Ordinal)
            .ThenBy(card => card.ProviderKey, StringComparer.Ordinal)
            .ToList();

        var options = new List<ProviderOptionDto>(orderedCards.Count);
        foreach (var card in orderedCards)
        {
            var option = new ProviderOptionDto
            {
                ProviderKey = card.ProviderKey,
                DisplayName = card.DisplayName,
                Configured = card.Configured,
                IsActive = card.ConfigurationActive
            };
            options.Add(option);
        }

        return options;
    }
}
