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

    /// <summary>Trimmed HTML, or the validation error. HTML is stored as written - the same trust model as
    /// task comments - and rendered through the frontend's HTML sanitizer.</summary>
    public static (string? Body, string? Error) ValidateBody(string? bodyHtml)
    {
        var body = bodyHtml?.Trim() ?? string.Empty;
        if (body.Length > MaxBodyLength)
            return (null, "Message is too long.");
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

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex("<(img|hr)[\\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex Embeds();
}
