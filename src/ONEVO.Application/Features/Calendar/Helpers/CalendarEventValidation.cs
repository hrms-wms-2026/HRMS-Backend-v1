namespace ONEVO.Application.Features.Calendar.Helpers;

public static class CalendarEventValidation
{
    /// <summary>A meeting link is optional; when present it must be an absolute http(s) URL.</summary>
    public static bool IsValidMeetingLink(string? meetingLink)
    {
        if (string.IsNullOrWhiteSpace(meetingLink))
            return true;

        return Uri.TryCreate(meetingLink, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
