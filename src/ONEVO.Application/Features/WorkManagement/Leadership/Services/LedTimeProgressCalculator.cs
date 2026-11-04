using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.DTOs;
using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>One person in Team Progress's time figures: their in-scope blocks and sessions (any range; clipped per local day
/// here) and their capacity for the period (null = their legal entity has no working hours).</summary>
public sealed record LedTimePerson(Guid EmployeeId, string DisplayName, TimeZoneInfo Zone, int? ExpectedMinutes,
    IReadOnlyList<AttributionBlock> Blocks, IReadOnlyList<AttributionSession> Sessions);

/// <summary>Team Progress planned vs worked (My Team widgets design 2026-10-29, section 6.1), pure. Every person's minutes
/// are attributed (TaskTimeAttribution) per their own local day inside the period window.</summary>
public static class LedTimeProgressCalculator
{
    public const int UnscheduledLimit = 200;

    /// <summary>The previous period over the same elapsed span. Week: from-7 .. today-7. Month: the first of the previous
    /// month .. the same day of month, clamped to that month's length.</summary>
    public static (DateOnly From, DateOnly To) PreviousSameElapsed(LedPeriod period, DateOnly from, DateOnly today)
    {
        if (period == LedPeriod.Month)
        {
            var first = new DateOnly(from.Year, from.Month, 1).AddMonths(-1);
            var day = Math.Min(today.Day, DateTime.DaysInMonth(first.Year, first.Month));
            return (first, new DateOnly(first.Year, first.Month, day));
        }
        return (from.AddDays(-7), today.AddDays(-7));
    }

    /// <summary>Days (week) or ISO weeks clipped to the window (month).</summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> BucketRanges(LedPeriod period, DateOnly from, DateOnly to)
    {
        var ranges = new List<(DateOnly, DateOnly)>();
        if (period == LedPeriod.Month)
        {
            for (var monday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7)); monday <= to; monday = monday.AddDays(7))
                ranges.Add((monday < from ? from : monday, monday.AddDays(6) > to ? to : monday.AddDays(6)));
        }
        else
        {
            for (var day = from; day <= to; day = day.AddDays(1))
                ranges.Add((day, day));
        }
        return ranges;
    }

    /// <summary>Expected work minutes in [from, to]: working weekdays minus holidays (ExpectedWorkdayCalendar, the whole period,
    /// not clipped to today) times the work window, less approved leave. Null without configured working hours.</summary>
    public static int? ExpectedMinutes(IReadOnlySet<int> workingWeekdays, TimeOnly? workStart, TimeOnly? workEnd,
        IReadOnlySet<DateOnly> holidays, DateOnly from, DateOnly to, DateOnly hireDate, DateOnly? terminationDate,
        IReadOnlyDictionary<DateOnly, decimal?> leaveHoursByDate)
    {
        if (workStart is not { } ws || workEnd is not { } we || ws >= we || workingWeekdays.Count == 0)
            return null;
        var required = (int)(we - ws).TotalMinutes;
        var workdays = ExpectedWorkdayCalendar.Build(workingWeekdays, holidays, from, to, hireDate, terminationDate, to);
        return workdays.ExpectedDates.Sum(d => Math.Max(0, required - LeaveWorkWindow.Minutes(leaveHoursByDate, d, required)));
    }

    public static LedTimeProgressResponse Build(LedPeriod period, DateOnly from, DateOnly to, DateOnly today, DateTimeOffset now,
        bool plannerEnabled, IReadOnlyList<LedTimePerson> people, IReadOnlyDictionary<Guid, TeamTaskRow> tasks)
    {
        var ranges = BucketRanges(period, from, to);
        var bucketPlanned = new int[ranges.Count];
        var bucketScheduled = new int[ranges.Count];
        var bucketUnscheduled = new int[ranges.Count];
        int BucketOf(DateOnly day)
        {
            for (var i = 0; i < ranges.Count; i++)
                if (day >= ranges[i].From && day <= ranges[i].To) return i;
            return -1;
        }

        var (prevFrom, prevTo) = PreviousSameElapsed(period, from, today);
        var prevNow = now.AddDays(-(today.DayNumber - prevTo.DayNumber));

        int planned = 0, clocked = 0, scheduled = 0, outside = 0, noPlan = 0, elapsedPlanned = 0;
        int prevPlanned = 0, prevClocked = 0, prevScheduled = 0;
        var workload = new List<(LedTimePerson Person, int Planned, int Clocked, int Unscheduled)>();
        var segments = new List<LedUnscheduledRow>();

        foreach (var person in people)
        {
            var blocks = plannerEnabled ? person.Blocks : [];
            int pPlanned = 0, pClocked = 0, pUnscheduled = 0;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var (dayStart, dayEnd) = PlannerActuals.DayBounds(day, person.Zone);
                var r = TaskTimeAttribution.Attribute(blocks, person.Sessions, dayStart, dayEnd, now);
                pClocked += r.ClockedMinutes;
                if (!plannerEnabled)
                    continue;   // no plan: clocked time only, never shown as unscheduled
                var dayUnscheduled = r.OutsideSlotMinutes + r.NoPlanMinutes;
                pPlanned += r.PlannedMinutes;
                pUnscheduled += dayUnscheduled;
                scheduled += r.ScheduledMinutes;
                outside += r.OutsideSlotMinutes;
                noPlan += r.NoPlanMinutes;
                elapsedPlanned += ElapsedPlanned(blocks, dayStart, dayEnd, now);
                var bucket = BucketOf(day);
                bucketPlanned[bucket] += r.PlannedMinutes;
                bucketScheduled[bucket] += r.ScheduledMinutes;
                bucketUnscheduled[bucket] += dayUnscheduled;
                foreach (var s in r.Unscheduled)
                {
                    var task = tasks.GetValueOrDefault(s.TaskId);
                    segments.Add(new LedUnscheduledRow(person.EmployeeId, person.DisplayName, s.TaskId, task?.ShortId ?? string.Empty,
                        task?.Title ?? string.Empty, s.Start, s.End, s.Minutes, s.Kind == UnscheduledKind.NoPlan ? "no_plan" : "outside_slot"));
                }
            }
            for (var day = prevFrom; day <= prevTo; day = day.AddDays(1))
            {
                var (dayStart, dayEnd) = PlannerActuals.DayBounds(day, person.Zone);
                var r = TaskTimeAttribution.Attribute(blocks, person.Sessions, dayStart, dayEnd, prevNow);
                prevPlanned += ElapsedPlanned(blocks, dayStart, dayEnd, prevNow);
                prevClocked += r.ClockedMinutes;
                prevScheduled += r.ScheduledMinutes;
            }
            planned += pPlanned;
            clocked += pClocked;
            workload.Add((person, pPlanned, pClocked, pUnscheduled));
        }

        var rank = 0;
        var workloadRows = workload
            .OrderByDescending(w => w.Planned)
            .ThenByDescending(w => w.Clocked)
            .ThenBy(w => w.Person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(w => new LedWorkloadRow(w.Person.EmployeeId, w.Person.DisplayName, ++rank, w.Planned, w.Clocked, w.Unscheduled,
                w.Person.ExpectedMinutes, Percent(w.Planned, w.Person.ExpectedMinutes ?? 0, cap: false)))
            .ToList();

        return new LedTimeProgressResponse(
            LedProgressPeriod.Wire(period), from, to, plannerEnabled, now,
            planned, clocked, scheduled, outside, noPlan, Percent(scheduled, elapsedPlanned, cap: true),
            workload.Count(w => w.Unscheduled > 0),
            new LedTimeComparison(prevPlanned, prevClocked, Percent(prevScheduled, prevPlanned, cap: true)),
            ranges.Select((range, i) => new LedTimeBucket(range.From, range.To, bucketPlanned[i], bucketScheduled[i], bucketUnscheduled[i]))
                .ToList(),
            workloadRows,
            segments.OrderByDescending(s => s.Start).Take(UnscheduledLimit).ToList(),
            segments.Count);
    }

    /// <summary>Planned block minutes of one day elapsed by <paramref name="now"/>.</summary>
    private static int ElapsedPlanned(IReadOnlyList<AttributionBlock> blocks, DateTimeOffset dayStart, DateTimeOffset dayEnd,
        DateTimeOffset now)
    {
        var end = now < dayEnd ? now : dayEnd;
        return end <= dayStart ? 0 : TaskTimeAttribution.Attribute(blocks, [], dayStart, end, now).PlannedMinutes;
    }

    private static decimal? Percent(int part, int whole, bool cap)
    {
        if (whole <= 0)
            return null;
        var value = 100m * part / whole;
        return Math.Round(cap ? Math.Min(100m, value) : value, 1, MidpointRounding.AwayFromZero);
    }
}
