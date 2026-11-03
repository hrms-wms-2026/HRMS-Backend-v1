using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Planner.Services;

/// <summary>Day planner (2026-10-26 design, Part B): the actual minutes of one day.</summary>
public static class PlannerActuals
{
    /// <summary>A task's clocked minutes in [dayStart, min(dayEnd, now)]: each session clipped to that window, an open
    /// session counting up to now.</summary>
    public static int TaskMinutes(IEnumerable<(DateTimeOffset In, DateTimeOffset? Out)> sessions, DateTimeOffset dayStart,
        DateTimeOffset dayEnd, DateTimeOffset now)
    {
        var windowEnd = now < dayEnd ? now : dayEnd;
        var total = 0.0;
        foreach (var (clockIn, clockOut) in sessions)
        {
            var from = clockIn > dayStart ? clockIn : dayStart;
            var end = clockOut ?? now;
            var to = end < windowEnd ? end : windowEnd;
            if (to > from)
                total += (to - from).TotalMinutes;
        }
        return (int)Math.Round(total);
    }

    /// <summary>A meeting's actual: its duration once it has ended, unless the employee declined or it was cancelled.</summary>
    public static int MeetingMinutes(int durationMinutes, bool declined, bool cancelled, bool ended)
        => declined || cancelled || !ended ? 0 : durationMinutes;

    /// <summary>A meeting's minutes on this day only: its overlap with the day, or one workday when an all-day meeting
    /// covers the day (MeetingPersonHours.AllDayMinutesPerDay).</summary>
    public static int MeetingMinutesInDay(DateTimeOffset meetingStart, DateTimeOffset meetingEnd, bool isAllDay, DateTimeOffset dayStart,
        DateTimeOffset dayEnd)
    {
        var from = meetingStart > dayStart ? meetingStart : dayStart;
        var to = meetingEnd < dayEnd ? meetingEnd : dayEnd;
        if (to <= from)
            return 0;
        return isAllDay ? MeetingPersonHours.AllDayMinutesPerDay : (int)Math.Round((to - from).TotalMinutes);
    }

    /// <summary>Worked minutes once clocked out; while still clocked in, the time since clock-in less breaks so far.</summary>
    public static int AttendanceMinutes(DateTimeOffset? actualStart, DateTimeOffset? actualEnd, int workedMinutes, int breakMinutes,
        DateTimeOffset now)
    {
        if (actualStart is { } start && actualEnd is null)
            return Math.Max(0, (int)Math.Round((now - start).TotalMinutes) - breakMinutes);
        return workedMinutes;
    }

    /// <summary>The day's [local midnight, next local midnight) in UTC offsets of the given zone.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) DayBounds(DateOnly day, TimeZoneInfo zone)
    {
        DateTimeOffset At(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        return (At(day), At(day.AddDays(1)));
    }
}
