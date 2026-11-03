using System.Globalization;

namespace ONEVO.Application.Features.Calendar.Services;

/// <summary>
/// The VTIMEZONE (RFC 5545 §3.6.5) a zoned .ics needs, read from the machine's time zone data. The offset changes of
/// the event's year become one STANDARD or DAYLIGHT component each, repeating yearly on the same weekday of the month
/// (e.g. "last Sunday of October"), which is how DST rules are written and what mail clients expect.
/// </summary>
public static class CalendarIcsTimeZone
{
    public static IEnumerable<string> Lines(string zoneId, TimeZoneInfo zone, DateTimeOffset aroundUtc)
    {
        yield return "BEGIN:VTIMEZONE";
        yield return $"TZID:{zoneId.Replace("\r", string.Empty).Replace("\n", string.Empty)}";

        var transitions = Transitions(zone, aroundUtc.UtcDateTime.Year).ToList();
        if (transitions.Count == 0)
        {
            // No change that year: a single standard offset.
            var offset = Offset(zone.GetUtcOffset(aroundUtc));
            yield return "BEGIN:STANDARD";
            yield return "DTSTART:19700101T000000";
            yield return $"TZOFFSETFROM:{offset}";
            yield return $"TZOFFSETTO:{offset}";
            yield return "END:STANDARD";
        }
        foreach (var (atUtc, from, to, daylight) in transitions)
        {
            // DTSTART is the wall-clock moment of the change, in the offset in force before it.
            var local = atUtc + from;
            var kind = daylight ? "DAYLIGHT" : "STANDARD";
            yield return $"BEGIN:{kind}";
            yield return $"DTSTART:{local.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)}";
            yield return $"TZOFFSETFROM:{Offset(from)}";
            yield return $"TZOFFSETTO:{Offset(to)}";
            yield return $"RRULE:FREQ=YEARLY;BYMONTH={local.Month};BYDAY={WeekOfMonth(local)}{Day(local.DayOfWeek)}";
            yield return $"END:{kind}";
        }
        yield return "END:VTIMEZONE";
    }

    /// <summary>Each offset change in <paramref name="year"/>, to the minute: day by day, then narrowed inside the day.</summary>
    private static IEnumerable<(DateTime AtUtc, TimeSpan From, TimeSpan To, bool Daylight)> Transitions(TimeZoneInfo zone, int year)
    {
        var day = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = day.AddYears(1);
        var offset = zone.GetUtcOffset(day);
        while (day < end)
        {
            var next = day.AddDays(1);
            var nextOffset = zone.GetUtcOffset(next);
            if (nextOffset != offset)
            {
                var (low, high) = (day, next);
                while (high - low > TimeSpan.FromMinutes(1))
                {
                    var mid = low + TimeSpan.FromTicks((high - low).Ticks / 2);
                    if (zone.GetUtcOffset(mid) == offset)
                        low = mid;
                    else
                        high = mid;
                }
                var at = new DateTime(high.Ticks - high.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
                yield return (at, offset, nextOffset, zone.IsDaylightSavingTime(at));
                offset = nextOffset;
            }
            day = next;
        }
    }

    /// <summary>"-1" for the last such weekday of the month, otherwise its position (1-4).</summary>
    private static string WeekOfMonth(DateTime local)
        => local.Day + 7 > DateTime.DaysInMonth(local.Year, local.Month)
            ? "-1"
            : ((local.Day - 1) / 7 + 1).ToString(CultureInfo.InvariantCulture);

    private static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };

    /// <summary>"+0100", "-0430".</summary>
    private static string Offset(TimeSpan offset)
        => (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString("hhmm", CultureInfo.InvariantCulture);
}
