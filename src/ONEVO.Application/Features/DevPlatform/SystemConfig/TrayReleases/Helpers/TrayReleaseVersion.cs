using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;

public static class TrayReleaseVersion
{
    private static readonly Regex ThreePart = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

    public static bool TryParse(string? value, out Version parsed)
    {
        parsed = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value) || !ThreePart.IsMatch(value))
            return false;
        return Version.TryParse(value, out parsed!);
    }

    /// <summary>Numeric comparison. Both arguments must already be valid x.y.z.</summary>
    public static int Compare(string a, string b)
    {
        TryParse(a, out var va);
        TryParse(b, out var vb);
        return va.CompareTo(vb);
    }

    /// <summary>
    /// True when the caller must update: current is a valid version strictly below a valid minimum.
    /// Unparseable input never forces an update.
    /// </summary>
    public static bool IsMandatory(string currentVersion, string? minSupportedVersion)
    {
        if (!TryParse(currentVersion, out var current)) return false;
        if (!TryParse(minSupportedVersion, out var min)) return false;
        return current < min;
    }
}
