using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <summary>
/// Allow-list validator and renderer for notification templates. Unsafe email markup is rejected at
/// publication rather than silently cleaned, so an admin always sees why a template cannot go live.
/// CSS support is deliberately small: element, <c>.class</c> and <c>#id</c> rules, inlined into
/// <c>style</c> attributes because most mail clients drop &lt;style&gt; blocks.
/// </summary>
public sealed partial class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    private const int MaxTitleLength = 255;

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "b", "strong", "i", "em", "u", "s", "small", "p", "br", "hr", "div", "span", "blockquote",
        "h1", "h2", "h3", "h4", "ul", "ol", "li", "table", "thead", "tbody", "tfoot", "tr", "td", "th", "img"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "class", "id", "style", "width", "height", "align", "valign",
        "colspan", "rowspan", "border", "cellpadding", "cellspacing", "target", "rel", "role"
    };

    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase) { "href", "src" };

    private static readonly string[] ForbiddenDeclarationTokens =
        ["url(", "expression(", "javascript:", "vbscript:", "@import", "behavior", "-moz-binding", "\\", "<", ">"];

    public IReadOnlyList<string> Validate(NotificationTemplateContent content, NotificationEventDefinition definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(content.InAppTitle))
            errors.Add("In-app title is required.");
        if (!definition.AllowedActionKeys.Contains(content.ActionKey, StringComparer.Ordinal))
            errors.Add($"Action '{content.ActionKey}' is not registered for event '{definition.Key}'.");

        var allowed = definition.AllowedVariables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (field, text) in Fields(content))
        {
            foreach (Match m in PlaceholderRegex().Matches(text ?? string.Empty))
            {
                if (!allowed.Contains(m.Groups[1].Value))
                    errors.Add($"{field}: variable '{{{{{m.Groups[1].Value}}}}}' is not available for this event.");
            }
        }

        if (!string.IsNullOrWhiteSpace(content.EmailHtml))
            ValidateHtml(content.EmailHtml, errors);
        if (!string.IsNullOrWhiteSpace(content.EmailCss))
            ParseCss(content.EmailCss, errors);

        return errors;
    }

    public NotificationRenderResult Render(
        NotificationTemplateContent content,
        NotificationEventDefinition definition,
        IReadOnlyDictionary<string, string> variables)
    {
        var errors = Validate(content, definition).ToList();
        foreach (var variable in definition.AllowedVariables.Where(v => v.Required))
        {
            if (!variables.TryGetValue(variable.Key, out var value) || value is null)
                errors.Add($"Required variable '{variable.Key}' was not supplied.");
        }
        if (errors.Count > 0)
            return NotificationRenderResult.Fail(errors);

        string Text(string? template) => Substitute(template ?? string.Empty, variables, v => v);

        var inAppTitle = Truncate(Text(content.InAppTitle), MaxTitleLength);
        var inAppBody = Text(content.InAppBody);
        var subject = Truncate(
            LineBreakRegex().Replace(Text(Blank(content.EmailSubject) ?? content.InAppTitle), " ").Trim(), MaxTitleLength);
        var emailText = Text(Blank(content.EmailText) ?? content.InAppBody);
        var html = Blank(content.EmailHtml) is { } emailHtml
            ? NeutraliseUrls(Substitute(InlineCss(emailHtml, content.EmailCss), variables, WebUtility.HtmlEncode))
            : "<p>" + WebUtility.HtmlEncode(emailText).Replace("\n", "<br>") + "</p>";

        return new NotificationRenderResult(new RenderedNotificationContent(
            InAppTitle: inAppTitle,
            InAppBody: inAppBody,
            EmailSubject: subject,
            EmailHtml: html,
            EmailText: emailText,
            TrayTitle: Truncate(Text(Blank(content.TrayTitle) ?? content.InAppTitle), MaxTitleLength),
            TrayBody: Text(Blank(content.TrayBody) ?? content.InAppBody),
            ActionKey: content.ActionKey), []);
    }

    private static IEnumerable<(string Field, string? Text)> Fields(NotificationTemplateContent c) =>
    [
        ("In-app title", c.InAppTitle), ("In-app message", c.InAppBody), ("Email subject", c.EmailSubject),
        ("Email HTML", c.EmailHtml), ("Email plain text", c.EmailText), ("Tray title", c.TrayTitle),
        ("Tray message", c.TrayBody), ("Email CSS", c.EmailCss)
    ];

    private static string Substitute(string template, IReadOnlyDictionary<string, string> variables, Func<string, string> encode)
        => PlaceholderRegex().Replace(template, m =>
            variables.TryGetValue(m.Groups[1].Value, out var value) && value is not null ? encode(value) : string.Empty);

    private static void ValidateHtml(string html, List<string> errors)
    {
        if (html.Contains("<!", StringComparison.Ordinal) || html.Contains("<?", StringComparison.Ordinal))
            errors.Add("Email HTML: comments, doctype and processing instructions are not supported.");

        foreach (Match tag in TagRegex().Matches(html))
        {
            var name = tag.Groups["name"].Value;
            if (!AllowedTags.Contains(name))
            {
                errors.Add($"Email HTML: <{name.ToLowerInvariant()}> is not allowed.");
                continue;
            }
            if (tag.Groups["close"].Success && tag.Groups["close"].Value == "/")
                continue;

            var attributes = tag.Groups["attrs"].Value;
            var consumed = AttributeRegex().Replace(attributes, string.Empty).Replace("/", string.Empty);
            if (!string.IsNullOrWhiteSpace(consumed))
                errors.Add($"Email HTML: attributes on <{name}> must be quoted name=\"value\" pairs.");

            foreach (Match attr in AttributeRegex().Matches(attributes))
            {
                var attrName = attr.Groups["name"].Value;
                var value = attr.Groups["value"].Value;
                if (attrName.StartsWith("on", StringComparison.OrdinalIgnoreCase) || !AllowedAttributes.Contains(attrName))
                    errors.Add($"Email HTML: attribute '{attrName}' is not allowed.");
                else if (UrlAttributes.Contains(attrName) && !IsSafeUrl(value, allowPlaceholder: true))
                    errors.Add($"Email HTML: '{attrName}' must be an https, http or mailto link.");
                else if (attrName.Equals("style", StringComparison.OrdinalIgnoreCase))
                    ValidateDeclarations(value, "inline style", errors);
            }
        }
    }

    private static void ValidateDeclarations(string declarations, string where, List<string> errors)
    {
        var lowered = declarations.ToLowerInvariant();
        foreach (var token in ForbiddenDeclarationTokens)
        {
            if (lowered.Contains(token, StringComparison.Ordinal))
                errors.Add($"Email CSS ({where}): '{token}' is not supported.");
        }
    }

    private sealed record CssRule(string Kind, string Value, int Specificity, int Order, string Declarations);

    private static List<CssRule> ParseCss(string css, List<string> errors)
    {
        var rules = new List<CssRule>();
        var stripped = CssCommentRegex().Replace(css, string.Empty);
        if (stripped.Contains('@'))
            errors.Add("Email CSS: @-rules (@import, @media, @font-face...) are not supported.");

        var order = 0;
        foreach (Match rule in CssRuleRegex().Matches(stripped))
        {
            var declarations = NormaliseDeclarations(rule.Groups["decls"].Value);
            ValidateDeclarations(declarations, rule.Groups["sel"].Value.Trim(), errors);
            foreach (var selector in rule.Groups["sel"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SimpleSelectorRegex().IsMatch(selector))
                {
                    errors.Add($"Email CSS: selector '{selector}' is not supported (use element, .class or #id).");
                    continue;
                }
                var (kind, specificity) = selector[0] switch { '.' => ("class", 2), '#' => ("id", 3), _ => ("tag", 1) };
                rules.Add(new CssRule(kind, kind == "tag" ? selector.ToLowerInvariant() : selector[1..], specificity, order++, declarations));
            }
        }

        if (!string.IsNullOrWhiteSpace(CssRuleRegex().Replace(stripped, string.Empty).Replace("@", string.Empty)))
            errors.Add("Email CSS: could not be parsed; use 'selector { property: value; }' rules.");
        return rules;
    }

    private static string NormaliseDeclarations(string declarations)
        => string.Join("; ", declarations.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[0].Length > 0 && p[1].Length > 0)
            .Select(p => $"{p[0]}: {p[1]}"));

    private static string InlineCss(string html, string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return html;
        var rules = ParseCss(css, []);
        if (rules.Count == 0)
            return html;

        return StartTagRegex().Replace(html, tag =>
        {
            var name = tag.Groups["name"].Value.ToLowerInvariant();
            var attributes = tag.Groups["attrs"].Value;
            string? Attr(string attrName) => AttributeRegex().Matches(attributes)
                .FirstOrDefault(a => a.Groups["name"].Value.Equals(attrName, StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value;

            var classes = (Attr("class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var id = Attr("id");
            var matched = rules
                .Where(r => r.Kind switch
                {
                    "tag" => r.Value == name,
                    "class" => classes.Contains(r.Value, StringComparer.Ordinal),
                    _ => r.Value == id
                })
                .OrderBy(r => r.Specificity).ThenBy(r => r.Order)
                .Select(r => r.Declarations)
                .ToList();
            if (matched.Count == 0)
                return tag.Value;

            var existing = Attr("style");
            if (!string.IsNullOrWhiteSpace(existing))
                matched.Add(NormaliseDeclarations(existing));
            var withoutStyle = AttributeRegex().Replace(attributes, a =>
                a.Groups["name"].Value.Equals("style", StringComparison.OrdinalIgnoreCase) ? string.Empty : a.Value).TrimEnd();
            var style = WebUtility.HtmlEncode(string.Join("; ", matched));
            return $"<{tag.Groups["name"].Value}{withoutStyle} style=\"{style}\"{tag.Groups["self"].Value}>";
        });
    }

    /// <summary>After substitution a variable may have produced an unsafe link; replace it rather than send it.</summary>
    private static string NeutraliseUrls(string html)
        => UrlAttributeRegex().Replace(html, m =>
            IsSafeUrl(WebUtility.HtmlDecode(m.Groups["value"].Value), allowPlaceholder: false)
                ? m.Value
                : $"{m.Groups["name"].Value}=\"#\"");

    private static bool IsSafeUrl(string value, bool allowPlaceholder)
    {
        var url = new string(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        if (allowPlaceholder && url.StartsWith("{{", StringComparison.Ordinal))
            return true;
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith('#');
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<\s*(?<close>/)?\s*(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>[^>]*)>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:\s+[^>]*?)?)\s*(?<self>/?)>")]
    private static partial Regex StartTagRegex();

    [GeneratedRegex(@"\s*(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*""(?<value>[^""]*)""|\s*(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*'(?<value>[^']*)'")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"(?<name>href|src)\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributeRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssCommentRegex();

    [GeneratedRegex(@"(?<sel>[^{}]+)\{(?<decls>[^{}]*)\}")]
    private static partial Regex CssRuleRegex();

    [GeneratedRegex(@"^(?:[a-zA-Z][a-zA-Z0-9]*|\.[A-Za-z_][\w-]*|#[A-Za-z_][\w-]*)$")]
    private static partial Regex SimpleSelectorRegex();

    [GeneratedRegex(@"[\r\n]+")]
    private static partial Regex LineBreakRegex();
}
