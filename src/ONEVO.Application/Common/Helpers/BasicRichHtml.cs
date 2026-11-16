using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ONEVO.Application.Common.Helpers;

/// <summary>The outcome of <see cref="BasicRichHtml.Validate"/>.</summary>
public sealed record RichHtmlCheck(bool IsValid, string? Error);

/// <summary>
/// The one allowlist for user-authored rich text that the shared editor's "basic" toolset produces
/// (legal documents, support ticket descriptions and comments). Allowlist-first: any tag not listed is
/// rejected, and any attribute not listed for its tag is rejected, which is what blocks script tags,
/// inline handlers, styles and javascript:/data: links. The tenant and admin apps sanitise with the
/// same list (basic-rich-html.util.ts); a test on each side pins it.
/// </summary>
public static partial class BasicRichHtml
{
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "strong", "b", "em", "i", "u", "s",
        "h1", "h2", "h3", "h4",
        "ul", "ol", "li",
        "blockquote",
        "a",
        "code", "pre",
        "table", "thead", "tbody", "tr", "th", "td",
    };

    private static readonly Dictionary<string, IReadOnlySet<string>> Attributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "title" },
        ["td"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
        ["th"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
    };

    private static readonly IReadOnlySet<string> NoAttributes = new HashSet<string>();

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "blockquote", "pre", "tr", "ul", "ol", "table",
    };

    public static IReadOnlySet<string> AllowedTags => Tags;

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedAttributes => Attributes;

    /// <summary>Checks <paramref name="html"/> against the allowlist. <paramref name="contentLabel"/>
    /// names the field in error messages.</summary>
    public static RichHtmlCheck Validate(string? html, string contentLabel = "rich text")
    {
        if (string.IsNullOrEmpty(html))
            return new RichHtmlCheck(true, null);

        foreach (Match tagMatch in TagPattern().Matches(html))
        {
            var isClosingTag = tagMatch.Groups[1].Value == "/";
            var tagName = tagMatch.Groups[2].Value.ToLowerInvariant();

            if (!Tags.Contains(tagName))
                return new RichHtmlCheck(false, $"Disallowed tag '<{tagName}>' is not permitted in {contentLabel}.");

            if (isClosingTag)
                continue;

            var allowedAttributes = Attributes.TryGetValue(tagName, out var tagAttributes) ? tagAttributes : NoAttributes;
            var attributesText = StripTrailingSelfClosingSlash(tagMatch.Groups[3].Value);
            var pos = 0;
            while (pos < attributesText.Length)
            {
                if (string.IsNullOrWhiteSpace(attributesText[pos..]))
                    break;

                var attributeMatch = AttributeTokenPattern().Match(attributesText, pos);
                if (!attributeMatch.Success || attributeMatch.Index != pos)
                {
                    return new RichHtmlCheck(
                        false, $"Unquoted or malformed attribute near '{attributesText[pos..].Trim()}' on '<{tagName}>'.");
                }

                var attributeName = attributeMatch.Groups[1].Value;
                var attributeValue = attributeMatch.Groups[3].Success
                    ? attributeMatch.Groups[3].Value
                    : attributeMatch.Groups[4].Value;

                if (!allowedAttributes.Contains(attributeName))
                    return new RichHtmlCheck(false, $"Disallowed attribute '{attributeName}' on '<{tagName}>'.");

                if (string.Equals(attributeName, "href", StringComparison.OrdinalIgnoreCase) && !IsSafeHref(attributeValue))
                    return new RichHtmlCheck(false, $"Unsafe href value '{attributeValue}' on '<a>' tag.");

                pos = attributeMatch.Index + attributeMatch.Length;
            }
        }

        return new RichHtmlCheck(true, null);
    }

    /// <summary>True only when <paramref name="value"/> holds a recognisable allowlisted tag, so old plain
    /// text that merely contains "&lt;" (e.g. "line 5 &lt; 6") stays plain text.</summary>
    public static bool IsRichHtml(string? value) =>
        !string.IsNullOrEmpty(value) && KnownTagPattern().IsMatch(value);

    /// <summary>Length of what a reader sees: plain text as is; rich text without markup, entities decoded.</summary>
    public static int VisibleLength(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        if (!IsRichHtml(value))
            return value.Length;
        return WebUtility.HtmlDecode(TagPattern().Replace(value, string.Empty)).Length;
    }

    /// <summary>Plain text for emails and notification placeholders: block tags become line breaks, list
    /// items get "- ", links keep their address, entities are decoded and every tag is removed.</summary>
    public static string ToPlainText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (!IsRichHtml(value))
            return value;

        var text = AnchorPattern().Replace(value, m =>
        {
            var inner = TagPattern().Replace(m.Groups[2].Value, string.Empty);
            return $"{inner} ({WebUtility.HtmlDecode(m.Groups[1].Value)})";
        });

        var builder = new StringBuilder();
        var last = 0;
        foreach (Match tag in TagPattern().Matches(text))
        {
            builder.Append(text, last, tag.Index - last);
            last = tag.Index + tag.Length;

            var name = tag.Groups[2].Value.ToLowerInvariant();
            var closing = tag.Groups[1].Value == "/";
            if (name == "br")
                builder.Append('\n');
            else if (name == "li" && !closing)
                builder.Append("\n- ");
            else if (BlockTags.Contains(name) && closing)
                builder.Append('\n');
            else if (name is "td" or "th" && closing)
                builder.Append(' ');
        }

        builder.Append(text, last, text.Length - last);
        var lines = WebUtility.HtmlDecode(builder.ToString())
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// TagPattern's attribute-text group only requires leading whitespace to start capturing, so a
    /// space-separated self-closing slash ("&lt;br /&gt;") ends up inside the captured text. Strip exactly one
    /// such trailing slash, but only outside any quoted value, so an unterminated quote is still rejected.
    /// </summary>
    private static string StripTrailingSelfClosingSlash(string attributesText)
    {
        var trimmedEnd = attributesText.TrimEnd();
        if (trimmedEnd.Length == 0 || trimmedEnd[^1] != '/')
            return attributesText;

        var beforeSlash = trimmedEnd[..^1];
        var doubleQuoteCount = beforeSlash.Count(c => c == '"');
        var singleQuoteCount = beforeSlash.Count(c => c == '\'');
        return doubleQuoteCount % 2 != 0 || singleQuoteCount % 2 != 0 ? attributesText : beforeSlash;
    }

    private static bool IsSafeHref(string href)
    {
        var trimmed = href.Trim();
        if (trimmed.Length == 0)
            return false;

        // Protocol-relative URLs inherit the page's scheme and can point off-origin like javascript:/data:.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return false;

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith('/')
            || trimmed.StartsWith('#'))
        {
            return true;
        }

        var colonIndex = trimmed.IndexOf(':');
        var slashIndex = trimmed.IndexOf('/');
        return !(colonIndex >= 0 && (slashIndex < 0 || colonIndex < slashIndex));
    }

    [GeneratedRegex(@"<\s*(/)?\s*([a-zA-Z][a-zA-Z0-9]*)((?:\s+[^<>]*)?)/?\s*>")]
    private static partial Regex TagPattern();

    // \G anchors the match to the position passed to Match(input, start), so any gap between attributes
    // (an unquoted value, a bare boolean attribute) surfaces as match.Index != pos and is rejected.
    [GeneratedRegex(@"\G\s*([a-zA-Z][a-zA-Z0-9\-]*)\s*=\s*(""([^""]*)""|'([^']*)')")]
    private static partial Regex AttributeTokenPattern();

    // Attributes, when present, must be quoted name="value" pairs, so prose like "a <b and c> d" is not rich text.
    [GeneratedRegex(@"<\s*/?\s*(p|br|strong|b|em|i|u|s|h[1-4]|ul|ol|li|blockquote|a|code|pre|table|thead|tbody|tr|th|td)(\s+[a-zA-Z][a-zA-Z0-9\-]*\s*=\s*(""[^""]*""|'[^']*'))*\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex KnownTagPattern();

    [GeneratedRegex(@"<a\s[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorPattern();
}
