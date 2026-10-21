using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.Conversations.Contracts;

namespace ONEVO.Application.Features.Conversations.Services;

internal static partial class ConversationRules
{
    public const string NotFoundMessage = "Conversation not found.";
    public const string NoEmployeeMessage = "No employee record for the current user.";
    public const int MaxBodyLength = 10_000;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    /// <summary>How long after sending its author may still edit or delete a message.</summary>
    public const int EditWindowMinutes = 30;
    public const string EditWindowClosed = "A message can only be changed within 30 minutes of sending it.";
    public const int MaxEmojiLength = 32;

    /// <summary>Whether the author may still edit or delete the message at <paramref name="now"/>.</summary>
    public static bool WithinEditWindow(DateTimeOffset createdAt, DateTimeOffset now)
        => now <= createdAt.AddMinutes(EditWindowMinutes);

    /// <summary>Trimmed HTML, or the validation error. The frontend renders messages through its HTML
    /// sanitizer; as defense in depth, active content (script-capable tags, inline event handlers and
    /// script/data URLs) is rejected here so it is never stored.</summary>
    public static (string? Body, string? Error) ValidateBody(string? bodyHtml, bool allowEmpty = false)
    {
        var body = bodyHtml?.Trim() ?? string.Empty;
        if (body.Length > MaxBodyLength)
            return (null, "Message is too long.");
        if (HasActiveContent(body))
            return (null, "Message contains content that is not allowed.");
        var text = WebUtility.HtmlDecode(Tags().Replace(body, " ")).Replace(' ', ' ').Trim();
        if (text.Length == 0 && !allowEmpty && !Embeds().IsMatch(body))
            return (null, "Message cannot be empty.");
        return (body, null);
    }

    public const int ReplyExcerptLength = 120;
    public const string ReplyTargetMissing = "The message you are replying to is not in this conversation.";
    public const string ReplyTargetNotAllowed = "You cannot reply to that message.";

    /// <summary>A message body as one line of plain text, cut to <paramref name="max"/> characters (a reply's quote).</summary>
    public static string Excerpt(string bodyHtml, int max = ReplyExcerptLength)
    {
        var text = Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(bodyHtml, " ")).Replace(' ', ' '), " ").Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
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

    /// <summary>Tells every activity listener, after the chat change committed; a listener failure is logged, never thrown.</summary>
    public static async Task NotifyActivityAsync(
        IEnumerable<IConversationActivityListener>? listeners, Guid tenantId, ItemRef subject, Guid actorEmployeeId, string activity,
        ILogger logger, CancellationToken ct)
    {
        foreach (var listener in listeners ?? [])
        {
            try
            {
                await listener.OnActivityAsync(tenantId, subject, actorEmployeeId, activity, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Conversation activity listener failed ({Activity}).", activity);
            }
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
