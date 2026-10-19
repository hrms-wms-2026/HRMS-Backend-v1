using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Infrastructure.Services.WorkManagement.Meetings;

/// <summary>
/// <see cref="IMeetingRecurrence"/> on Ical.Net. DTSTART carries the series' TZID and its local wall-clock time, so the
/// expansion keeps that wall time through daylight-saving changes (the calendar's IcalNetRecurrenceExpander expands in
/// UTC, which would drift by an hour).
/// </summary>
public sealed class IcalNetMeetingRecurrence : IMeetingRecurrence
{
    public IReadOnlyList<DateTimeOffset> Occurrences(string? rule, DateTimeOffset dtStart, string timezone, DateTimeOffset from, DateTimeOffset to)
    {
        if (rule is null)
            return dtStart >= from && dtStart < to ? [dtStart.ToUniversalTime()] : [];

        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        return Build(rule, dtStart, timezone)
            .GetOccurrences(new CalDateTime(fromUtc))
            .Select(o => new DateTimeOffset(o.Period.StartTime.AsUtc, TimeSpan.Zero))
            .TakeWhile(o => o.UtcDateTime < toUtc)
            .Where(o => o.UtcDateTime >= fromUtc)
            .Take(IMeetingRecurrence.MaxOccurrences)
            .ToList();
    }

    public Result Validate(string? rule, DateTimeOffset dtStart, string timezone)
    {
        if (!TryFindZone(timezone, out _))
            return Result.Failure($"'{timezone}' is not a known timezone.", 400, MeetingErrorCodes.InvalidTimezone);
        if (rule is null)
            return Result.Success();

        try
        {
            var pattern = new RecurrencePattern(rule);
            if (pattern.Count is > IMeetingRecurrence.MaxOccurrences)
                return Invalid();
            var firstYear = Build(rule, dtStart, timezone)
                .GetOccurrences(new CalDateTime(dtStart.UtcDateTime))
                .TakeWhile(o => o.Period.StartTime.AsUtc < dtStart.UtcDateTime.AddYears(1))
                .Take(IMeetingRecurrence.MaxOccurrences + 1)
                .Count();
            return firstYear is 0 or > IMeetingRecurrence.MaxOccurrences ? Invalid() : Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return Invalid();
        }

        static Result Invalid() => Result.Failure(
            "The repeat rule is invalid or too frequent (at most 500 occurrences).", 400, MeetingErrorCodes.RecurrenceInvalid);
    }

    private static CalendarEvent Build(string rule, DateTimeOffset dtStart, string timezone)
    {
        TryFindZone(timezone, out var zone);
        var localWallTime = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(dtStart, zone!).DateTime, DateTimeKind.Unspecified);
        return new CalendarEvent
        {
            DtStart = new CalDateTime(localWallTime, timezone),
            RecurrenceRule = new RecurrencePattern(rule),
        };
    }

    private static bool TryFindZone(string timezone, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            zone = null;
            return false;
        }
    }
}
