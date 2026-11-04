using System.Globalization;
using System.Text.RegularExpressions;

namespace ONEVO.Application.Common.Helpers;

/// <summary>The icon a user picks for a project, leave type or group: "lucide:{name}" (a Lucide icon name) or
/// "emoji:{emoji}" (one emoji). Stored as-is; the frontend renders it.</summary>
public static partial class EntityIconRules
{
    public const int MaxLength = 64;
    public const string LucidePrefix = "lucide:";
    public const string EmojiPrefix = "emoji:";
    public const string InvalidMessage = "Pick an icon or an emoji from the picker.";

    /// <summary>True for null/empty (no icon) and for a well-formed icon value.</summary>
    public static bool IsValid(string? icon)
    {
        if (string.IsNullOrEmpty(icon))
            return true;
        if (icon.Length > MaxLength)
            return false;
        if (icon.StartsWith(LucidePrefix, StringComparison.Ordinal))
            return LucideName().IsMatch(icon[LucidePrefix.Length..]);
        if (icon.StartsWith(EmojiPrefix, StringComparison.Ordinal))
        {
            var emoji = icon[EmojiPrefix.Length..];
            // One grapheme (ZWJ families, flags, skin tones and keycaps are one), not plain ASCII, no spaces.
            return emoji.Length is > 0 and <= 16
                && new StringInfo(emoji).LengthInTextElements == 1
                && emoji.Any(c => c > '\u007f')
                && !emoji.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
        }
        return false;
    }

    /// <summary>Trims, and turns empty into null.</summary>
    public static string? Normalize(string? icon)
        => string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();

    /// <summary>True for null/empty (the default tint) and for a #RRGGBB colour.</summary>
    public static bool IsValidColor(string? color)
        => string.IsNullOrEmpty(color) || HexColor().IsMatch(color);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex LucideName();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
