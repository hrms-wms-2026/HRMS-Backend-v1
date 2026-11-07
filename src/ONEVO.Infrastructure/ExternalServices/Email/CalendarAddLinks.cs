using System.Globalization;

namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>One-click "add this event" links for an invite email, for people who would not open its .ics. Unlike the .ics,
/// an event added this way is a separate copy: a later update or cancellation does not change it.</summary>
/// <param name="Outlook">Null for a repeating event: Outlook's compose link cannot repeat, so it would add one meeting of
/// the series.</param>
internal sealed record CalendarAddLinks(string Google, string? Outlook)
{
    /// <summary>Null without a start and an end that make a valid range: the link would open the wrong time.</summary>
    public static CalendarAddLinks? For(string title, string startUtc, string endUtc, string location, string meetingLink,
        string recurrenceRule, string timeZoneId)
    {
        if (!TryParse(startUtc, out var start) || !TryParse(endUtc, out var end) || end < start)
            return null;

        var details = string.IsNullOrWhiteSpace(meetingLink) ? "" : $"Join: {meetingLink}";
        var repeats = !string.IsNullOrWhiteSpace(recurrenceRule);

        var google = "https://calendar.google.com/calendar/render?action=TEMPLATE"
            + Param("text", title)
            + $"&dates={GoogleTime(start)}/{GoogleTime(end)}"
            + OptionalParam("details", details)
            + OptionalParam("location", location);
        if (repeats)
            google += Param("recur", $"RRULE:{recurrenceRule}") + OptionalParam("ctz", timeZoneId);

        var outlook = repeats
            ? null
            : "https://outlook.office.com/calendar/action/compose?rru=addevent"
                + Param("subject", title)
                + Param("startdt", OutlookTime(start))
                + Param("enddt", OutlookTime(end))
                + OptionalParam("body", details)
                + OptionalParam("location", location);

        return new CalendarAddLinks(google, outlook);
    }

    private static bool TryParse(string value, out DateTime utc)
    {
        var parsed = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at);
        utc = parsed ? at.UtcDateTime : default;
        return parsed;
    }

    private static string GoogleTime(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string OutlookTime(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Param(string name, string value) => $"&{name}={Uri.EscapeDataString(value)}";

    private static string OptionalParam(string name, string value) => string.IsNullOrWhiteSpace(value) ? "" : Param(name, value);
}
