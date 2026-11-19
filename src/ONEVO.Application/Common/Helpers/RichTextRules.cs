using System.Net;
using System.Text.RegularExpressions;

namespace ONEVO.Application.Common.Helpers;

/// <summary>
/// Write-side guard for user-authored rich text (chat messages, task descriptions, task comments).
/// The frontend sanitizes on render, but the task editor loads stored HTML with innerHTML, so active
/// content is rejected here as defense in depth and never stored.
/// </summary>
public static partial class RichTextRules
{
    public const string ActiveContentError = "The text contains content that is not allowed (scripts, embedded frames or inline event handlers).";

    /// <summary>True when the HTML holds script-capable tags, inline event handlers, or script/data URLs.</summary>
    public static bool HasActiveContent(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return false;
        if (ForbiddenTags().IsMatch(html) || EventHandlerAttributes().IsMatch(html))
            return true;
        // Entities and tab/newline characters can hide a scheme from a naive match ("java&#115;cript:").
        var decoded = WebUtility.HtmlDecode(html).Replace("\t", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        return ScriptUrls().IsMatch(decoded);
    }

    [GeneratedRegex(@"<\s*/?\s*(script|iframe|frame|frameset|object|embed|applet|svg|math|style|link|meta|base|form|template)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenTags();

    [GeneratedRegex(@"<[^>]*[\s/""']on[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerAttributes();

    [GeneratedRegex(@"(href|src|action|formaction|data|xlink:href|background|poster)\s*=\s*[""']?[\s\x00-\x20]*(javascript|vbscript|data(?!:image/(png|jpe?g|gif|webp)))\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptUrls();
}
