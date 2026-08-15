namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

public enum AppUsageCategory
{
    Productive,
    Meeting,
    Personal,
    Unknown
}

public static class AppUsageCategorizer
{
    private static readonly string[] ProductiveTokens =
    [
        "code",
        "devenv",
        "excel",
        "winword",
        "powerpnt",
        "outlook",
        "notepad",
        "postman",
        "ssms"
    ];

    private static readonly string[] MeetingTokens =
    [
        "teams",
        "zoom",
        "slack",
        "meet",
        "webex"
    ];

    private static readonly string[] PersonalTokens =
    [
        "youtube",
        "netflix",
        "spotify",
        "steam",
        "discord",
        "facebook",
        "instagram",
        "tiktok"
    ];

    public static AppUsageCategory Categorize(string? processName)
    {
        var normalized = Normalize(processName);
        if (normalized.Length == 0)
            return AppUsageCategory.Unknown;

        if (ContainsAny(normalized, ProductiveTokens))
            return AppUsageCategory.Productive;

        if (ContainsAny(normalized, MeetingTokens))
            return AppUsageCategory.Meeting;

        if (ContainsAny(normalized, PersonalTokens))
            return AppUsageCategory.Personal;

        return AppUsageCategory.Unknown;
    }

    public static string ToWireValue(AppUsageCategory category) => category switch
    {
        AppUsageCategory.Productive => "productive",
        AppUsageCategory.Meeting => "meeting",
        AppUsageCategory.Personal => "personal",
        _ => "unknown"
    };

    private static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return string.Empty;

        var trimmed = processName.Trim().ToLowerInvariant();
        return trimmed.EndsWith(".exe", StringComparison.Ordinal)
            ? trimmed[..^4]
            : trimmed;
    }

    private static bool ContainsAny(string normalizedProcessName, IEnumerable<string> tokens)
        => tokens.Any(token => normalizedProcessName.Contains(token, StringComparison.Ordinal));
}
