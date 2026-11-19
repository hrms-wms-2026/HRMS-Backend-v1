namespace ONEVO.Application.Features.Auth.UserAccounts;

/// <summary>
/// Turns a stored User-Agent into "Chrome on Windows" for the sessions list. Best effort only:
/// order matters because Edge/Opera UAs also contain "Chrome" and Chrome's contains "Safari".
/// </summary>
public static class UserAgentDescription
{
    public static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown browser";

        var ua = userAgent;
        var browser =
            Has(ua, "Edg/") ? "Edge" :
            Has(ua, "OPR/") || Has(ua, "Opera") ? "Opera" :
            Has(ua, "Firefox/") ? "Firefox" :
            Has(ua, "Chrome/") || Has(ua, "CriOS/") ? "Chrome" :
            Has(ua, "Safari/") ? "Safari" :
            null;

        var os =
            Has(ua, "Windows") ? "Windows" :
            Has(ua, "iPhone") || Has(ua, "iPad") ? "iOS" :
            Has(ua, "Android") ? "Android" :
            Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS" :
            Has(ua, "Linux") ? "Linux" :
            null;

        return (browser, os) switch
        {
            (not null, not null) => $"{browser} on {os}",
            (not null, null) => browser,
            (null, not null) => $"Browser on {os}",
            _ => "Unknown browser"
        };
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
