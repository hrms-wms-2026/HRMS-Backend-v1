using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Allowlist-first legal document HTML validator. Any tag not explicitly allowed is rejected,
/// and any attribute not explicitly allowed for its tag is rejected - this is what actually
/// blocks &lt;script&gt;, on*= handlers, javascript: URLs, and &lt;iframe|object|embed&gt; (none of
/// them appear in the allowlists below), rather than trying to blacklist each one individually.
/// </summary>
public static class LegalHtmlValidator
{
    public sealed record LegalHtmlValidationResult(bool IsValid, string? ErrorMessage);

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s",
        "h1", "h2", "h3", "h4",
        "ul", "ol", "li",
        "blockquote",
        "a",
        "table", "thead", "tbody", "tr", "th", "td"
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedAttributesByTag =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "title" },
            ["td"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
            ["th"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" }
        };

    private static readonly HashSet<string> NoAttributesAllowed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TagPattern =
        new(@"<\s*(/)?\s*([a-zA-Z][a-zA-Z0-9]*)((?:\s+[^<>]*)?)/?\s*>", RegexOptions.Compiled);

    // \G anchors the match to the exact position passed to Match(input, start) below, so any
    // gap between consecutive matches (an unquoted value, a bare boolean attribute, stray
    // punctuation) surfaces as match.Index != pos and is rejected rather than silently skipped.
    private static readonly Regex AttributeTokenPattern =
        new(@"\G\s*([a-zA-Z][a-zA-Z0-9\-]*)\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.Compiled);

    public static LegalHtmlValidationResult Validate(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new LegalHtmlValidationResult(false, "content_html must not be empty.");
        }

        foreach (Match tagMatch in TagPattern.Matches(html))
        {
            var isClosingTag = tagMatch.Groups[1].Value == "/";
            var tagName = tagMatch.Groups[2].Value.ToLowerInvariant();

            if (!AllowedTags.Contains(tagName))
            {
                return new LegalHtmlValidationResult(
                    false, $"Disallowed tag '<{tagName}>' is not permitted in legal document content.");
            }

            if (isClosingTag)
            {
                continue;
            }

            var allowedAttributes = AllowedAttributesByTag.TryGetValue(tagName, out var tagAttributes)
                ? tagAttributes
                : NoAttributesAllowed;

            var attributesText = StripTrailingSelfClosingSlash(tagMatch.Groups[3].Value);
            var pos = 0;
            while (pos < attributesText.Length)
            {
                if (string.IsNullOrWhiteSpace(attributesText[pos..]))
                {
                    break;
                }

                var attributeMatch = AttributeTokenPattern.Match(attributesText, pos);
                if (!attributeMatch.Success || attributeMatch.Index != pos)
                {
                    return new LegalHtmlValidationResult(
                        false,
                        $"Unquoted or malformed attribute near '{attributesText[pos..].Trim()}' on '<{tagName}>'.");
                }

                var attributeName = attributeMatch.Groups[1].Value;
                var attributeValue = attributeMatch.Groups[3].Success
                    ? attributeMatch.Groups[3].Value
                    : attributeMatch.Groups[4].Value;

                if (!allowedAttributes.Contains(attributeName))
                {
                    return new LegalHtmlValidationResult(
                        false, $"Disallowed attribute '{attributeName}' on '<{tagName}>'.");
                }

                if (string.Equals(attributeName, "href", StringComparison.OrdinalIgnoreCase)
                    && !IsSafeHref(attributeValue))
                {
                    return new LegalHtmlValidationResult(
                        false, $"Unsafe href value '{attributeValue}' on '<a>' tag.");
                }

                pos = attributeMatch.Index + attributeMatch.Length;
            }
        }

        return new LegalHtmlValidationResult(true, null);
    }

    /// <summary>
    /// TagPattern's attribute-text group only requires leading whitespace to start capturing, so
    /// a space-separated self-closing slash (e.g. "&lt;br /&gt;") ends up inside the captured
    /// text instead of being consumed by the tag pattern's own trailing "/?". Strip exactly one
    /// such trailing slash before scanning attributes - but only when it sits outside any quoted
    /// value, so a genuinely unterminated quote is still (correctly) rejected as malformed.
    /// </summary>
    private static string StripTrailingSelfClosingSlash(string attributesText)
    {
        var trimmedEnd = attributesText.TrimEnd();
        if (trimmedEnd.Length == 0 || trimmedEnd[^1] != '/')
        {
            return attributesText;
        }

        var beforeSlash = trimmedEnd[..^1];
        var doubleQuoteCount = beforeSlash.Count(c => c == '"');
        var singleQuoteCount = beforeSlash.Count(c => c == '\'');
        if (doubleQuoteCount % 2 != 0 || singleQuoteCount % 2 != 0)
        {
            return attributesText;
        }

        return beforeSlash;
    }

    private static bool IsSafeHref(string href)
    {
        var trimmed = href.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Protocol-relative URLs ("//evil.example/path") inherit whatever scheme the page
        // happens to load under and can point off-origin exactly like a javascript:/data: URL -
        // reject them before the single-"/" prefix check below would otherwise treat them as a
        // safe same-origin relative path.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return true;
        }

        var colonIndex = trimmed.IndexOf(':');
        var slashIndex = trimmed.IndexOf('/');
        var hasUnsafeScheme = colonIndex >= 0 && (slashIndex < 0 || colonIndex < slashIndex);

        return !hasUnsafeScheme;
    }
}
