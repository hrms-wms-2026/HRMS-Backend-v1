using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Monitoring.Meetings.Services;

/// <summary>
/// The desktop apps of the meeting providers ONEVO creates meetings with. A meeting created through ONEVO knows its
/// provider, so only that provider's app counts as attending it; a pasted link has no known provider, so any app in the
/// Meeting category counts. Process names are compared without case and without a trailing ".exe".
/// </summary>
public static class MeetingProviderApplications
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByProvider =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [CalendarEventMeetingProviders.MicrosoftTeams] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "ms-teams", "teams", "msteams", "microsoft teams" },
            [CalendarEventMeetingProviders.Zoom] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "zoom", "zoom.us" },
        };

    /// <summary>The process stems for a provider, or null when the provider is unknown (fall back to the Meeting category).</summary>
    public static IReadOnlySet<string>? ProcessStemsFor(string? provider)
        => provider is not null && ByProvider.TryGetValue(provider, out var stems) ? stems : null;

    public static string Stem(string processName)
    {
        var normalized = processName.Trim().ToLowerInvariant();
        return normalized.EndsWith(".exe", StringComparison.Ordinal) ? normalized[..^4] : normalized;
    }
}
