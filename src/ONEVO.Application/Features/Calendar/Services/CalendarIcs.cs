using System.Globalization;
using System.Text;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>The event identity an email's .ics needs (plan C1 P2-1): a stable UID per event and a SEQUENCE that grows
/// with every change, so mail clients update or remove the same calendar entry.</summary>
public sealed record CalendarEventIcs(
    Guid EventId, DateTimeOffset EndUtc, int Sequence, string? RecurrenceRule = null, DateTimeOffset? RecurrenceId = null)
{
    /// <summary>The .ics identity of an event as it is now: SEQUENCE from the current time, so it grows with every email.</summary>
    public static CalendarEventIcs For(Guid eventId, DateTimeOffset endUtc, string? recurrenceRule, DateTimeOffset now)
        => new(eventId, endUtc, CalendarIcs.Sequence(now), string.IsNullOrWhiteSpace(recurrenceRule) ? null : recurrenceRule);
}

/// <summary>The kinds of calendar emails. Invite/updated carry an iCalendar REQUEST; cancelled/removed a CANCEL.</summary>
public static class CalendarEventEmailKinds
{
    public const string Invite = "invite";
    public const string Updated = "updated";
    public const string Cancelled = "cancelled";
    public const string Removed = "removed";

    public static bool IsCancel(string kind) => kind is Cancelled or Removed;
}

/// <summary>Builds RFC 5545 iCalendar text for one event and one attendee (plan C1 P2-1).</summary>
public static class CalendarIcs
{
    private static readonly DateTimeOffset SequenceEpoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A SEQUENCE that only grows: whole seconds since 2020 of the moment the event version was written.</summary>
    public static int Sequence(DateTimeOffset versionAt) => (int)Math.Max(0, (versionAt - SequenceEpoch).TotalSeconds);

    /// <summary>The moment an event version was written, read back from its <see cref="Sequence"/>. Used as DTSTAMP, so a
    /// resent invite is byte-identical (a provider idempotency key only matches the same body).</summary>
    public static DateTimeOffset VersionTime(int sequence) => SequenceEpoch.AddSeconds(sequence);

    public static string Uid(Guid eventId) => $"{eventId:N}@onevo";

    public static string Build(
        string kind, CalendarEventIcs ics, string title, DateTimeOffset startUtc, string? location, string organizerName,
        string attendeeEmail, string? meetingLink, DateTimeOffset stampUtc)
    {
        var cancel = CalendarEventEmailKinds.IsCancel(kind);
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "PRODID:-//ONEVO//Calendar//EN",
            "VERSION:2.0",
            "CALSCALE:GREGORIAN",
            $"METHOD:{(cancel ? "CANCEL" : "REQUEST")}",
            "BEGIN:VEVENT",
            $"UID:{Uid(ics.EventId)}",
            $"SEQUENCE:{ics.Sequence.ToString(CultureInfo.InvariantCulture)}",
            $"DTSTAMP:{Utc(stampUtc)}",
            $"DTSTART:{Utc(startUtc)}",
            $"DTEND:{Utc(ics.EndUtc < startUtc ? startUtc : ics.EndUtc)}",
            $"SUMMARY:{Escape(title)}",
            $"ORGANIZER;CN={Escape(organizerName)}:mailto:noreply@onevo.app",
            $"ATTENDEE;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:{Raw(attendeeEmail)}",
            $"STATUS:{(cancel ? "CANCELLED" : "CONFIRMED")}",
        };
        // One occurrence of a series is addressed by RECURRENCE-ID; the series itself carries its rule.
        if (ics.RecurrenceId is { } occurrence)
            lines.Add($"RECURRENCE-ID:{Utc(occurrence)}");
        else if (!string.IsNullOrWhiteSpace(ics.RecurrenceRule))
            lines.Add($"RRULE:{Raw(ics.RecurrenceRule!)}");
        if (!string.IsNullOrWhiteSpace(location))
            lines.Add($"LOCATION:{Escape(location)}");
        if (!string.IsNullOrWhiteSpace(meetingLink))
        {
            lines.Add($"URL:{Raw(meetingLink)}");
            lines.Add($"DESCRIPTION:{Escape($"Join: {meetingLink}")}");
        }
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var builder = new StringBuilder();
        foreach (var line in lines)
            builder.Append(Fold(line)).Append("\r\n");
        return builder.ToString();
    }

    private static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

    /// <summary>Values written without TEXT escaping (URL, RRULE, mailto) come from users: a CR or LF in them would start a new
    /// property line, so line breaks are dropped.</summary>
    private static string Raw(string value) => value.Replace("\r", string.Empty).Replace("\n", string.Empty);

    /// <summary>Lines longer than 75 octets are folded with CRLF + space (RFC 5545 §3.1).</summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75)
            return line;
        var builder = new StringBuilder();
        var count = 0;
        foreach (var ch in line)
        {
            var size = Encoding.UTF8.GetByteCount(ch.ToString());
            if (count + size > 75)
            {
                builder.Append("\r\n ");
                count = 1;
            }
            builder.Append(ch);
            count += size;
        }
        return builder.ToString();
    }
}
