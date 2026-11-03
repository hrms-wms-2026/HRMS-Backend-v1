using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.WeeklyProgress.DTOs;

namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;

/// <summary>
/// Weekly Work Progress widget (2026-10-28 design, section 6). Worked = logged task minutes + credited meeting minutes;
/// attendance is reported beside it, never added in. The planner's day rules (meeting minutes per day, all-day meetings,
/// open sessions up to now) are shared through <see cref="PlannerActuals"/>.
/// </summary>
public static class WeeklyWorkProgressCalculator
{
    public const decimal OnTrackRatio = 0.85m;

    public static WeekProgressDto Compute(WeeklyWorkProgressInput input)
    {
        var weekEnd = input.WeekStart.AddDays(6);
        var configured = input.WorkStart is { } ws && input.WorkEnd is { } we && ws < we;
        var requiredMinutes = configured ? (int)(input.WorkEnd!.Value - input.WorkStart!.Value).TotalMinutes : 0;
        // Passing the week's last day as "today" keeps the whole week (the calendar clips to today otherwise).
        var workdays = ExpectedWorkdayCalendar.Build(
            configured ? input.WorkingWeekdays : new HashSet<int>(), input.Holidays,
            input.WeekStart, weekEnd, input.HireDate, input.TerminationDate, weekEnd);
        var localNow = TimeZoneInfo.ConvertTime(input.Now, input.Zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        var days = new List<WeekDayProgressDto>(7);
        int scheduledTask = 0, scheduledMeeting = 0, logged = 0, credited = 0, expectedToDate = 0;
        int adherenceScheduled = 0, adherenceMet = 0;
        for (var date = input.WeekStart; date <= weekEnd; date = date.AddDays(1))
        {
            var (start, end) = PlannerActuals.DayBounds(date, input.Zone);
            var isExpected = workdays.ExpectedDates.Contains(date);
            var leave = isExpected ? LeaveMinutes(input.LeaveHoursByDate, date, requiredMinutes) : 0;
            var expected = isExpected ? Math.Max(0, requiredMinutes - leave) : 0;

            var dayTask = input.PlannedBlocks.Sum(b => OverlapMinutes(b.Start, b.End, start, end));
            int dayMeeting = 0, dayCredited = 0;
            foreach (var m in input.Meetings)
            {
                var minutes = PlannerActuals.MeetingMinutesInDay(m.Start, m.End, m.IsAllDay, start, end);
                if (!m.Declined && !m.Cancelled)
                    dayMeeting += minutes;
                dayCredited += PlannerActuals.MeetingMinutes(minutes, m.Declined, m.Cancelled, ended: input.Now >= m.End);
            }
            var dayLogged = PlannerActuals.TaskMinutes(input.ClockSessions, start, end, input.Now);
            var dayAttendance = input.Attendance.Where(a => a.Date == date)
                .Sum(a => PlannerActuals.AttendanceMinutes(a.ActualStart, a.ActualEnd, a.WorkedMinutes, a.BreakMinutes, input.Now));
            var dayScheduled = dayTask + dayMeeting;
            var dayWorked = dayLogged + dayCredited;

            if (date < today)
                expectedToDate += expected;
            else if (date == today)
                expectedToDate += (int)Math.Round(expected * ElapsedShareOfWorkWindow(input, localNow));

            if (date <= today && dayScheduled > 0)
            {
                adherenceScheduled += dayScheduled;
                adherenceMet += Math.Min(dayWorked, dayScheduled);
            }

            scheduledTask += dayTask;
            scheduledMeeting += dayMeeting;
            logged += dayLogged;
            credited += dayCredited;
            days.Add(new WeekDayProgressDto(date, isExpected, workdays.HolidayDates.Contains(date), leave, expected,
                dayScheduled, dayWorked, dayAttendance));
        }

        var expectedTotal = days.Sum(d => d.ExpectedMinutes);
        var scheduled = scheduledTask + scheduledMeeting;
        var worked = logged + credited;
        return new WeekProgressDto(
            expectedTotal, expectedToDate, scheduledTask, scheduledMeeting, scheduled, logged, credited, worked,
            days.Sum(d => d.AttendanceMinutes), Math.Max(0, expectedTotal - worked),
            Percent(worked, expectedTotal), Percent(scheduled, expectedTotal),
            adherenceScheduled > 0 ? Percent(adherenceMet, adherenceScheduled) : null,
            Status(expectedTotal, expectedToDate, worked), days);
    }

    private static int LeaveMinutes(IReadOnlyDictionary<DateOnly, decimal?> leave, DateOnly date, int requiredMinutes)
    {
        if (!leave.TryGetValue(date, out var hours))
            return 0;
        return hours is { } h ? Math.Min(requiredMinutes, (int)Math.Round(h * 60m)) : requiredMinutes;
    }

    private static int OverlapMinutes(DateTimeOffset from, DateTimeOffset to, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        var a = from > dayStart ? from : dayStart;
        var b = to < dayEnd ? to : dayEnd;
        return b > a ? (int)Math.Round((b - a).TotalMinutes) : 0;
    }

    /// <summary>0 before the work window starts, 1 after it ends, linear in between.</summary>
    private static double ElapsedShareOfWorkWindow(WeeklyWorkProgressInput input, DateTimeOffset localNow)
    {
        if (input.WorkStart is not { } start || input.WorkEnd is not { } end || start >= end)
            return 0;
        var nowTime = TimeOnly.FromDateTime(localNow.DateTime);
        if (nowTime <= start) return 0;
        if (nowTime >= end) return 1;
        return (nowTime - start).TotalMinutes / (end - start).TotalMinutes;
    }

    private static decimal Percent(int part, int whole) =>
        whole > 0 ? Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero) : 0m;

    private static string Status(int expected, int expectedToDate, int worked)
    {
        if (expected == 0)
            return WeeklyProgressStatuses.NoTarget;
        if (expectedToDate == 0)
            return WeeklyProgressStatuses.OnTrack;
        var ratio = (decimal)worked / expectedToDate;
        return ratio >= 1m ? WeeklyProgressStatuses.Ahead
            : ratio >= OnTrackRatio ? WeeklyProgressStatuses.OnTrack
            : WeeklyProgressStatuses.Behind;
    }
}
