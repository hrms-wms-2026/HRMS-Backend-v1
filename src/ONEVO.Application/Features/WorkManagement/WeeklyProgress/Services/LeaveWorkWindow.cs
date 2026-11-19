using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;

/// <summary>Approved leave measured against the legal entity's work window, shared by Weekly Work Progress and Team
/// Progress capacity (My Team widgets, 2026-10-29).</summary>
public static class LeaveWorkWindow
{
    /// <summary>Per day, approved leave's overlap with that day's work window: null when it covers the whole window (a
    /// full leave day), else its hours. A partial cancellation ends the leave the day before its effective date.</summary>
    public static Dictionary<DateOnly, decimal?> HoursByDate(IEnumerable<LeaveRequest> leaves, DateOnly from, DateOnly to,
        TimeZoneInfo zone, TimeOnly? workStart, TimeOnly? workEnd)
    {
        var result = new Dictionary<DateOnly, decimal?>();
        if (workStart is not { } ws || workEnd is not { } we || ws >= we)
            return result;
        var windowMinutes = (we - ws).TotalMinutes;
        foreach (var leave in leaves)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (leave.PartialCancelEffectiveDate is { } cancelledFrom && day >= cancelledFrom)
                    break;
                var dayStart = PlannerActuals.DayBounds(day, zone).Start;
                var windowStart = dayStart.Add(ws.ToTimeSpan());
                var windowEnd = dayStart.Add(we.ToTimeSpan());
                var overlapStart = leave.StartAt > windowStart ? leave.StartAt : windowStart;
                var overlapEnd = leave.EndAt < windowEnd ? leave.EndAt : windowEnd;
                if (overlapEnd <= overlapStart)
                    continue;
                // Two leaves on one day add up; a day already fully on leave stays full (null).
                var prior = result.TryGetValue(day, out var hours) ? hours is { } h ? (double)h * 60 : windowMinutes : 0;
                var minutes = prior + (overlapEnd - overlapStart).TotalMinutes;
                result[day] = minutes >= windowMinutes ? null : Math.Round((decimal)minutes / 60m, 2);
            }
        }
        return result;
    }

    /// <summary>A day's leave in minutes of a <paramref name="requiredMinutes"/> work window: a full leave day is the whole
    /// window.</summary>
    public static int Minutes(IReadOnlyDictionary<DateOnly, decimal?> leave, DateOnly date, int requiredMinutes)
    {
        if (!leave.TryGetValue(date, out var hours))
            return 0;
        return hours is { } h ? Math.Min(requiredMinutes, (int)Math.Round(h * 60m)) : requiredMinutes;
    }
}
