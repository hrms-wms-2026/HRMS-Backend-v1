using ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.WeeklyProgress;

/// <summary>Weekly Work Progress widget (2026-10-28 design, section 6): one employee's week from raw sources.</summary>
public class WeeklyWorkProgressCalculatorTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+0530", Offset, "Test+0530", "Test+0530");
    private static readonly DateOnly Monday = new(2026, 10, 26);
    /// <summary>Wednesday 13:30 local: two full working days and half of today's 09:00-18:00 window have passed.</summary>
    private static readonly DateTimeOffset WednesdayAfternoon = Local(2, 13, 30);

    private static DateTimeOffset Local(int dayOfWeek, int hour, int minute = 0) =>
        new DateTimeOffset(Monday.AddDays(dayOfWeek).ToDateTime(new TimeOnly(0, 0)), Offset).AddHours(hour).AddMinutes(minute);

    private static WeeklyWorkProgressInput Input(
        DateTimeOffset? now = null,
        TimeOnly? workStart = null, TimeOnly? workEnd = null, bool scheduleConfigured = true,
        IReadOnlySet<DateOnly>? holidays = null, DateOnly? hire = null, DateOnly? termination = null,
        IReadOnlyDictionary<DateOnly, decimal?>? leave = null,
        IReadOnlyList<(DateTimeOffset, DateTimeOffset)>? blocks = null,
        IReadOnlyList<WeeklyMeetingInterval>? meetings = null,
        IReadOnlyList<(DateTimeOffset, DateTimeOffset?)>? sessions = null,
        IReadOnlyList<WeeklyAttendanceDay>? attendance = null) =>
        new(Monday, Zone, now ?? WednesdayAfternoon,
            new HashSet<int> { 1, 2, 3, 4, 5 },
            scheduleConfigured ? workStart ?? new TimeOnly(9, 0) : null,
            scheduleConfigured ? workEnd ?? new TimeOnly(18, 0) : null,
            holidays ?? new HashSet<DateOnly>(), hire ?? new DateOnly(2020, 1, 1), termination,
            leave ?? new Dictionary<DateOnly, decimal?>(),
            blocks ?? [], meetings ?? [], sessions ?? [], attendance ?? []);

    private static (DateTimeOffset, DateTimeOffset?) Session(DateTimeOffset from, DateTimeOffset? to) => (from, to);

    [Fact]
    public void Full_week_expected_is_working_days_times_required()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input());

        Assert.Equal(2700, week.ExpectedMinutes);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(Monday, week.Days[0].Date);
        Assert.Equal(540, week.Days[0].ExpectedMinutes);
        Assert.False(week.Days[5].IsExpected);
        Assert.False(week.Days[6].IsExpected);
        Assert.Equal(0, week.Days[6].ExpectedMinutes);
    }

    [Fact]
    public void Holiday_and_full_leave_remove_a_day_partial_leave_removes_hours()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            holidays: new HashSet<DateOnly> { Monday.AddDays(1) },
            leave: new Dictionary<DateOnly, decimal?> { [Monday.AddDays(2)] = null, [Monday.AddDays(3)] = 4m }));

        Assert.Equal(2700 - 540 - 540 - 240, week.ExpectedMinutes);
        Assert.True(week.Days[1].IsHoliday);
        Assert.Equal(540, week.Days[2].LeaveMinutes);
        Assert.Equal(240, week.Days[3].LeaveMinutes);
        Assert.Equal(300, week.Days[3].ExpectedMinutes);
    }

    [Fact]
    public void Hired_midweek_starts_expected_at_hire_date()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(hire: Monday.AddDays(2)));

        Assert.Equal(3 * 540, week.ExpectedMinutes);
        Assert.False(week.Days[1].IsExpected);
    }

    [Fact]
    public void Terminated_midweek_stops_expected()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(termination: Monday.AddDays(1)));

        Assert.Equal(2 * 540, week.ExpectedMinutes);
    }

    [Fact]
    public void No_schedule_gives_no_target()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(scheduleConfigured: false));

        Assert.Equal(0, week.ExpectedMinutes);
        Assert.Equal(WeeklyProgressStatuses.NoTarget, week.Status);
        Assert.Equal(0m, week.ProgressPercent);
        Assert.Equal(0m, week.PlanningCoveragePercent);
    }

    [Fact]
    public void Worked_is_logged_plus_credited_meetings_not_attendance()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            sessions: [Session(Local(0, 10), Local(0, 12))],
            meetings: [new WeeklyMeetingInterval(Local(0, 14), Local(0, 15), false, false, false)],
            attendance: [new WeeklyAttendanceDay(Monday, Local(0, 9), Local(0, 17), 480, 0)]));

        Assert.Equal(120, week.LoggedTaskMinutes);
        Assert.Equal(60, week.CreditedMeetingMinutes);
        Assert.Equal(180, week.WorkedMinutes);
        Assert.Equal(480, week.AttendanceMinutes);
        Assert.Equal(180, week.Days[0].WorkedMinutes);
        Assert.Equal(480, week.Days[0].AttendanceMinutes);
    }

    [Fact]
    public void Declined_cancelled_and_future_meetings_are_not_credited()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(meetings:
        [
            new WeeklyMeetingInterval(Local(0, 10), Local(0, 11), false, Declined: true, Cancelled: false),
            new WeeklyMeetingInterval(Local(0, 12), Local(0, 13), false, Declined: false, Cancelled: true),
            new WeeklyMeetingInterval(Local(4, 10), Local(4, 11), false, Declined: false, Cancelled: false),
        ]));

        Assert.Equal(60, week.ScheduledMeetingMinutes);
        Assert.Equal(0, week.CreditedMeetingMinutes);
    }

    [Fact]
    public void All_day_meeting_counts_a_workday()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(meetings:
            [new WeeklyMeetingInterval(Local(1, 0), Local(2, 0), IsAllDay: true, false, false)]));

        Assert.Equal(480, week.ScheduledMeetingMinutes);
        Assert.Equal(480, week.Days[1].ScheduledMinutes);
        Assert.Equal(480, week.CreditedMeetingMinutes);
    }

    [Fact]
    public void Session_over_midnight_splits_across_days()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(sessions: [Session(Local(0, 23), Local(1, 1))]));

        Assert.Equal(60, week.Days[0].WorkedMinutes);
        Assert.Equal(60, week.Days[1].WorkedMinutes);
    }

    [Fact]
    public void Open_session_counts_up_to_now()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(sessions: [Session(Local(2, 12, 30), null)]));

        Assert.Equal(60, week.LoggedTaskMinutes);
    }

    [Fact]
    public void Week_boundaries_follow_employee_zone()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            now: Local(7, 12),
            sessions:
            [
                Session(Local(0, 0, 10), Local(0, 0, 20)), // Monday 00:10 local = Sunday 18:40 UTC
                Session(Local(6, 23, 30), Local(6, 23, 50)), // Sunday 23:30 local
                Session(Local(-1, 23), Local(-1, 23, 30)), // last week's Sunday: not this week
            ]));

        Assert.Equal(10, week.Days[0].WorkedMinutes);
        Assert.Equal(20, week.Days[6].WorkedMinutes);
        Assert.Equal(30, week.LoggedTaskMinutes);
    }

    [Fact]
    public void Expected_to_date_prorates_today_by_work_window()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input());

        Assert.Equal(540 * 2 + 270, week.ExpectedToDateMinutes);
    }

    [Theory]
    [InlineData(1350, WeeklyProgressStatuses.Ahead)]
    [InlineData(1148, WeeklyProgressStatuses.OnTrack)]
    [InlineData(1134, WeeklyProgressStatuses.Behind)]
    public void Status_follows_worked_against_expected_to_date(int workedMinutes, string status)
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            sessions: [Session(Local(0, 0), Local(0, 0).AddMinutes(workedMinutes))]));

        Assert.Equal(status, week.Status);
    }

    [Fact]
    public void Before_work_starts_on_monday_is_on_track()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(now: Local(0, 8)));

        Assert.Equal(0, week.ExpectedToDateMinutes);
        Assert.Equal(WeeklyProgressStatuses.OnTrack, week.Status);
    }

    [Fact]
    public void Coverage_is_scheduled_over_expected_and_adherence_counts_only_days_so_far()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            blocks: [(Local(0, 9), Local(0, 18)), (Local(1, 9), Local(1, 18)), (Local(4, 9), Local(4, 11))],
            sessions: [Session(Local(0, 9), Local(0, 18)), Session(Local(1, 9), Local(1, 13, 30))]));

        Assert.Equal(1200, week.ScheduledTaskMinutes);
        Assert.Equal(1200, week.ScheduledMinutes);
        Assert.Equal(44.4m, week.PlanningCoveragePercent);
        Assert.Equal(75.0m, week.ScheduleAdherencePercent);
    }

    [Fact]
    public void Adherence_is_null_when_nothing_is_scheduled()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input());

        Assert.Null(week.ScheduleAdherencePercent);
    }

    [Fact]
    public void Remaining_never_negative()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            hire: Monday.AddDays(4), now: Local(4, 20),
            sessions: [Session(Local(4, 8), Local(4, 18))]));

        Assert.Equal(540, week.ExpectedMinutes);
        Assert.Equal(0, week.RemainingMinutes);
        Assert.Equal(111.1m, week.ProgressPercent);
    }

    [Fact]
    public void Clocked_in_attendance_counts_up_to_now_less_breaks()
    {
        var week = WeeklyWorkProgressCalculator.Compute(Input(
            attendance: [new WeeklyAttendanceDay(Monday.AddDays(2), Local(2, 9), null, 0, 30)]));

        Assert.Equal(270 - 30, week.Days[2].AttendanceMinutes);
    }
}
