namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>One person's week as the handler prepared it: the seven composed days (person-local dates) and the elapsed planned
/// task minutes for this week and for the same span a week earlier.</summary>
public sealed record TeamWeekPersonInput(Guid EmployeeId, string DisplayName, Guid? AvatarFileId,
    IReadOnlyList<(DateOnly Date, TeamScheduleMember Day)> Days, int PlannedElapsedMinutes,
    int PreviousPlannedElapsedMinutes, int PreviousScheduledMinutes, int PreviousClockedMinutes, int PreviousUnscheduledMinutes);

/// <summary>Team week planned vs actual (2026-11-02 design match, section 7.4), pure: sums composed person-days into the seven
/// week slots by each person's own local date, and builds the totals, the previous-span totals and the per-person rows.</summary>
public static class TeamWeekComposer
{
    public static TeamScheduleWeekResponse Compose(DateOnly weekStart, bool plannerEnabled, DateTimeOffset asOf,
        IReadOnlyList<TeamWeekPersonInput> people)
    {
        var weekEnd = weekStart.AddDays(6);
        var slots = new int[7, 6];   // plannedTask, plannedMeeting, clocked, scheduled, outside, noPlan
        var members = new List<TeamWeekMember>(people.Count);
        var plannedElapsed = 0;

        foreach (var person in people)
        {
            int planned = 0, clocked = 0, scheduled = 0, unscheduled = 0;
            foreach (var (date, day) in person.Days)
            {
                var index = date.DayNumber - weekStart.DayNumber;
                if (index is < 0 or > 6)
                    continue;
                var (task, meeting, inPlan, outside, noPlan) = plannerEnabled
                    ? (day.PlannedTaskMinutes, day.PlannedMeetingMinutes, day.ScheduledMinutes, day.OutsideSlotMinutes, day.NoPlanMinutes)
                    : (0, 0, 0, 0, 0);
                slots[index, 0] += task;
                slots[index, 1] += meeting;
                slots[index, 2] += day.ClockedMinutes;
                slots[index, 3] += inPlan;
                slots[index, 4] += outside;
                slots[index, 5] += noPlan;
                planned += task + meeting;
                clocked += day.ClockedMinutes;
                scheduled += inPlan;
                unscheduled += outside + noPlan;
            }
            plannedElapsed += person.PlannedElapsedMinutes;
            members.Add(new TeamWeekMember(person.EmployeeId, person.DisplayName, person.AvatarFileId, planned, clocked, scheduled,
                unscheduled, Coverage(scheduled, person.PlannedElapsedMinutes, plannerEnabled)));
        }

        var days = Enumerable.Range(0, 7)
            .Select(i => new TeamWeekDay(weekStart.AddDays(i), slots[i, 0], slots[i, 1], slots[i, 2], slots[i, 3], slots[i, 4], slots[i, 5]))
            .ToList();
        var totalScheduled = members.Sum(m => m.ScheduledMinutes);
        var totals = new TeamWeekTotals(members.Sum(m => m.PlannedMinutes), members.Sum(m => m.ClockedMinutes), totalScheduled,
            members.Sum(m => m.UnscheduledMinutes), Coverage(totalScheduled, plannedElapsed, plannerEnabled));

        var previousPlanned = people.Sum(p => p.PreviousPlannedElapsedMinutes);
        var previousScheduled = people.Sum(p => p.PreviousScheduledMinutes);
        var previous = plannerEnabled
            ? new TeamWeekTotals(previousPlanned, people.Sum(p => p.PreviousClockedMinutes), previousScheduled,
                people.Sum(p => p.PreviousUnscheduledMinutes), Coverage(previousScheduled, previousPlanned, plannerEnabled))
            : new TeamWeekTotals(0, people.Sum(p => p.PreviousClockedMinutes), 0, 0, null);

        var ordered = members
            .OrderByDescending(m => m.PlannedMinutes)
            .ThenByDescending(m => m.ClockedMinutes)
            .ThenBy(m => m.DisplayName, StringComparer.Ordinal)
            .ThenBy(m => m.EmployeeId)
            .ToList();
        return new TeamScheduleWeekResponse(weekStart, weekEnd, plannerEnabled, asOf, days, totals, previous, ordered);
    }

    /// <summary>min(100, scheduled ÷ elapsed plan × 100), one decimal, half away from zero; null without elapsed plan or Planner.</summary>
    public static decimal? Coverage(int scheduled, int plannedElapsed, bool plannerEnabled)
        => !plannerEnabled || plannedElapsed <= 0
            ? null
            : Math.Min(100m, Math.Round(scheduled * 100m / plannedElapsed, 1, MidpointRounding.AwayFromZero));
}
