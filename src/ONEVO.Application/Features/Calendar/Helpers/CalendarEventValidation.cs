using System.Net.Mail;

namespace ONEVO.Application.Features.Calendar.Helpers;

public static class CalendarEventValidation
{
    public const int MaxGuestsPerEvent = 20;

    /// <summary>A meeting link is optional; when present it must be an absolute http(s) URL.</summary>
    public static bool IsValidMeetingLink(string? meetingLink)
    {
        if (string.IsNullOrWhiteSpace(meetingLink))
            return true;

        return Uri.TryCreate(meetingLink, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Trims and lower-cases a guest email; false when it isn't a bare, dotted-domain
    /// address of at most 255 characters (a display-name form like "Ada &lt;a@b.co&gt;" is rejected).</summary>
    public static bool TryNormalizeGuestEmail(string? raw, out string email)
    {
        email = string.Empty;
        var trimmed = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 255)
            return false;
        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
            return false;
        if (!address.Host.Contains('.'))
            return false;

        email = trimmed;
        return true;
    }

    /// <summary>Normalizes and de-duplicates a batch of guest emails, or returns the first invalid one.</summary>
    public static (IReadOnlyList<string> Emails, string? InvalidEmail) NormalizeGuestEmails(IEnumerable<string>? raw)
    {
        var emails = new List<string>();
        foreach (var candidate in raw ?? [])
        {
            if (!TryNormalizeGuestEmail(candidate, out var email))
                return ([], candidate);
            if (!emails.Contains(email))
                emails.Add(email);
        }
        return (emails, null);
    }
}
