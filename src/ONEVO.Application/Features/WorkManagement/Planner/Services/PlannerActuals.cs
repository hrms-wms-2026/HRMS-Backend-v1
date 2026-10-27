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
}
