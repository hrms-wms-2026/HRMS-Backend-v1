using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ONEVO.Application.Features.Conversations.Services;

internal static partial class ConversationRules
{
    public const string NotFoundMessage = "Conversation not found.";
    public const string NoEmployeeMessage = "No employee record for the current user.";
    public const int MaxBodyLength = 10_000;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>Trimmed HTML, or the validation error. The frontend renders messages through its HTML
    /// sanitizer; as defense in depth, active content (script-capable tags, inline event handlers and
    /// script/data URLs) is rejected here so it is never stored.</summary>
    public static (string? Body, string? Error) ValidateBody(string? bodyHtml)
    {
        var body = bodyHtml?.Trim() ?? string.Empty;
        if (body.Length > MaxBodyLength)
            return (null, "Message is too long.");
        if (HasActiveContent(body))
            return (null, "Message contains content that is not allowed.");
        var text = WebUtility.HtmlDecode(Tags().Replace(body, " ")).Replace(' ', ' ').Trim();
        if (text.Length == 0 && !Embeds().IsMatch(body))
            return (null, "Message cannot be empty.");
        return (body, null);
    }

    public static int ClampPageSize(int take) => Math.Clamp(take <= 0 ? DefaultPageSize : take, 1, MaxPageSize);

    /// <summary>Realtime is best-effort: a publish failure is logged and never fails the request.</summary>
    public static async Task PublishSafelyAsync(Func<Task> publish, ILogger logger, string what)
    {
        try
        {
            await publish();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inbox realtime publish failed ({What}).", what);
        }
    }

    private static bool HasActiveContent(string body)
    {
        if (ForbiddenTags().IsMatch(body) || EventHandlerAttributes().IsMatch(body))
            return true;
        // Entities and tab/newline characters can hide a scheme from a naive match ("java&#115;cript:").
        var decoded = WebUtility.HtmlDecode(body).Replace("\t", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        return ScriptUrls().IsMatch(decoded);
    }

    [GeneratedRegex(@"<\s*/?\s*(script|iframe|frame|frameset|object|embed|applet|svg|math|style|link|meta|base|form|template)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenTags();

    [GeneratedRegex(@"<[^>]*[\s/""']on[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerAttributes();

    [GeneratedRegex(@"(href|src|action|formaction|data|xlink:href|background|poster)\s*=\s*[""']?[\s\x00-\x20]*(javascript|vbscript|data(?!:image/(png|jpe?g|gif|webp)))\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptUrls();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex("<(img|hr)[\\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex Embeds();
}
