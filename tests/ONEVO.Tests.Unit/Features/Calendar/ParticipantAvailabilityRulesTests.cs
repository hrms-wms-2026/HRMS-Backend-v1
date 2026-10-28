using ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class ParticipantAvailabilityRulesTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly LegalEntity Entity = new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Timezone = "Asia/Colombo",
        WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0), BreakDurationMinutes = 60,
        StandardWorkingDays = "[1,2,3,4,5]",
    };

    // 2026-11-02 is a Monday.
    private static DateTimeOffset At(int day, int hour, int minute = 0, TimeZoneInfo? zone = null)
    {
        var local = new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, (zone ?? Colombo).GetUtcOffset(local));
    }

    private static AvailabilityParticipant Person(
        LegalEntity? entity = null, bool leaveVisible = true, TimeZoneInfo? zone = null, bool noEntity = false) =>
        new(EmployeeId, "Ada Lovelace", zone ?? Colombo, noEntity ? null : entity ?? Entity, leaveVisible);

    private static LeaveRequest Leave(DateTimeOffset start, DateTimeOffset end, DateOnly? partialCancel = null) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = EmployeeId, StartAt = start, EndAt = end,
        Status = partialCancel is null ? "approved" : "cancelled",
        Reason = "Family trip", PartialCancelEffectiveDate = partialCancel,
    };

    private static IReadOnlyList<ONEVO.Application.Features.Calendar.Queries.CheckCalendarConflicts.CalendarConflict> Run(
        AvailabilityParticipant person, DateTimeOffset start, DateTimeOffset end, bool isAllDay = false,
        IReadOnlyList<LeaveRequest>? leave = null, IReadOnlyList<LeaveCalendarHoliday>? holidays = null) =>
        ParticipantAvailabilityRules.Evaluate(person, start, end, isAllDay, leave ?? [], holidays ?? []);

    [Fact]
    public void Approved_leave_overlapping_warns_on_leave()
    {
        var rows = Run(Person(), At(3, 10), At(3, 11), leave: [Leave(At(3, 0), At(4, 0))]);

        var row = Assert.Single(rows);
        Assert.Equal(CalendarConflictKinds.Leave, row.Kind);
        Assert.Equal("On leave", row.ConflictingEventTitle);
        Assert.Equal(Guid.Empty, row.ConflictingEventId);
        Assert.Equal(At(3, 10), row.OverlapStart);
        Assert.Equal(At(3, 11), row.OverlapEnd);
    }

    [Fact]
    public void Leave_outside_scope_reads_busy()
    {
        var rows = Run(Person(leaveVisible: false), At(3, 10), At(3, 11), leave: [Leave(At(3, 0), At(4, 0))]);

        var row = Assert.Single(rows);
        Assert.Equal(CalendarConflictKinds.Leave, row.Kind);
        Assert.Equal("Busy", row.ConflictingEventTitle);
    }

    [Fact]
    public void Morning_half_day_does_not_warn_on_afternoon_meeting()
    {
        var rows = Run(Person(), At(3, 14), At(3, 15), leave: [Leave(At(3, 9), At(3, 13))]);

        Assert.Empty(rows);
    }

    [Fact]
    public void Partial_cancel_ends_leave_before_effective_date()
    {
        var leave = Leave(At(2, 0), At(7, 0), partialCancel: new DateOnly(2026, 11, 4));

        Assert.Empty(Run(Person(), At(4, 10), At(4, 11), leave: [leave]));
        Assert.Single(Run(Person(), At(3, 10), At(3, 11), leave: [leave]));
    }

    [Fact]
    public void Holiday_for_entity_warns_with_name()
    {
        var holiday = new LeaveCalendarHoliday(new DateOnly(2026, 11, 3), "Deepavali", Entity.Id, null, "nager");

        var row = Assert.Single(Run(Person(), At(3, 10), At(3, 11), holidays: [holiday, holiday]));

        Assert.Equal(CalendarConflictKinds.Holiday, row.Kind);
        Assert.Equal("Public holiday: Deepavali", row.ConflictingEventTitle);
        Assert.Equal(Guid.Empty, row.ConflictingEventId);
    }

    [Fact]
    public void Holiday_of_other_entity_is_ignored()
    {
        var holiday = new LeaveCalendarHoliday(new DateOnly(2026, 11, 3), "Deepavali", Guid.NewGuid(), null, "nager");

        Assert.Empty(Run(Person(), At(3, 10), At(3, 11), holidays: [holiday]));
    }

    [Fact]
    public void Holiday_on_second_local_day_of_a_cross_midnight_meeting_warns()
    {
        var holiday = new LeaveCalendarHoliday(new DateOnly(2026, 11, 4), "Founders day", Entity.Id, null, "nager");

        var rows = Run(Person(), At(3, 23), At(4, 1), holidays: [holiday]);

        var row = Assert.Single(rows, r => r.Kind == CalendarConflictKinds.Holiday);
        Assert.Equal(At(4, 0), row.OverlapStart);
        Assert.Equal(At(4, 1), row.OverlapEnd);
    }

    [Fact]
    public void Meeting_inside_window_has_no_outside_row()
    {
        Assert.Empty(Run(Person(), At(3, 10), At(3, 11)));
    }

    [Fact]
    public void Meeting_ending_after_window_warns_with_window_label()
    {
        var row = Assert.Single(Run(Person(), At(3, 16, 30), At(3, 17, 30)));

        Assert.Equal(CalendarConflictKinds.OutsideHours, row.Kind);
        Assert.Equal("Outside working hours (09:00–17:00)", row.ConflictingEventTitle);
        Assert.Equal(At(3, 17), row.OverlapStart);
        Assert.Equal(At(3, 17, 30), row.OverlapEnd);
    }

    [Fact]
    public void Meeting_in_break_is_not_flagged()
    {
        Assert.Empty(Run(Person(), At(3, 12, 30), At(3, 13)));
    }

    [Fact]
    public void Saturday_reads_non_working_day()
    {
        var row = Assert.Single(Run(Person(), At(7, 10), At(7, 11)));

        Assert.Equal(CalendarConflictKinds.OutsideHours, row.Kind);
        Assert.Equal("Non-working day", row.ConflictingEventTitle);
    }

    [Fact]
    public void Window_read_in_participant_zone()
    {
        // 14:00 in Colombo is 08:30 in London, before a London 09:00 start.
        var rows = Run(Person(zone: London), At(3, 14), At(3, 14, 30));

        var row = Assert.Single(rows);
        Assert.Equal(CalendarConflictKinds.OutsideHours, row.Kind);
        Assert.Equal(At(3, 14), row.OverlapStart);
        Assert.Equal(At(3, 14, 30), row.OverlapEnd);
    }

    [Fact]
    public void No_schedule_or_no_entity_means_no_outside_or_holiday_rows()
    {
        var unscheduled = new LegalEntity { Id = Entity.Id, TenantId = Entity.TenantId, StandardWorkingDays = "[1,2,3,4,5]" };
        var holiday = new LeaveCalendarHoliday(new DateOnly(2026, 11, 7), "Deepavali", Entity.Id, null, "nager");

        Assert.Empty(Run(Person(entity: unscheduled), At(7, 20), At(7, 21)));
        Assert.Empty(Run(Person(noEntity: true), At(7, 20), At(7, 21), holidays: [holiday]));
    }

    [Fact]
    public void All_day_meeting_skips_outside_hours_but_keeps_leave_and_holiday()
    {
        var holiday = new LeaveCalendarHoliday(new DateOnly(2026, 11, 7), "Deepavali", Entity.Id, null, "nager");

        var rows = Run(Person(), At(7, 0), At(8, 0), isAllDay: true,
            leave: [Leave(At(7, 0), At(8, 0))], holidays: [holiday]);

        Assert.Equal([CalendarConflictKinds.Leave, CalendarConflictKinds.Holiday], rows.Select(r => r.Kind));
    }
}
