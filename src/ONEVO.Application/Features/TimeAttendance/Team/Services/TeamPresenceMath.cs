using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Team.Services;

/// <summary>Working / Break / Leave (My Team widgets design 2026-10-29, section 6.4): since-when and time-left figures for
/// one Team Status member. Every figure is null when it has no source, never a guessed 0.</summary>
public static class TeamPresenceMath
{
    /// <summary>Working: the clock-in. On break: the start of the open break. Any other status: null.</summary>
    public static DateTimeOffset? StateSince(string teamStatus, AttendanceRecord? record, IReadOnlyList<BreakRecord> breaks) => teamStatus switch
    {
        "working" => record?.ActualStart,
        "on_break" => breaks.Where(b => b.BreakEnd is null).OrderByDescending(b => b.BreakStart).Select(b => (DateTimeOffset?)b.BreakStart)
            .FirstOrDefault(),
        _ => null,
    };

    /// <summary>Only on break and only when the legal entity configures a break allowance: max(0, allowance - used).</summary>
    public static int? BreakRemainingMinutes(string teamStatus, int? breakDurationMinutes, int breakUsedMinutes)
        => teamStatus == "on_break" && breakDurationMinutes is > 0 ? Math.Max(0, breakDurationMinutes.Value - breakUsedMinutes) : null;

    /// <summary>The scheduled end of the work date, only while working or on break and only when the schedule has an end.</summary>
    public static DateTimeOffset? ShiftEndsAt(string teamStatus, DateTimeOffset? scheduledEnd)
        => teamStatus is "working" or "on_break" ? scheduledEnd : null;

    /// <summary>The schedule's end time on <paramref name="workDate"/> in <paramref name="zone"/>; null without a configured end.</summary>
    public static DateTimeOffset? ScheduledEnd(DateOnly workDate, TimeOnly? end, TimeZoneInfo zone)
    {
        if (end is not { } endTime)
            return null;
        var local = workDate.ToDateTime(endTime);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
