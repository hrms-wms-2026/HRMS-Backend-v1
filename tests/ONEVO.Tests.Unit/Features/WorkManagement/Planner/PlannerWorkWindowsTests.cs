using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Planner = Calendar (2026-10-27 design, section 4.5, calls C1/C5/C7): a day's work window from the legal entity.</summary>
public class PlannerWorkWindowsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Sunday = new(2026, 11, 8);

    private static LegalEntity Entity(TimeOnly? start = null, TimeOnly? end = null, int? breakMinutes = 60,
        string? timezone = "Asia/Colombo")
        => new()
        {
            Name = "Dapi", Timezone = timezone, StandardWorkingDays = "[1,2,3,4,5]",
            WorkStartTime = start ?? new TimeOnly(9, 0), WorkEndTime = end ?? new TimeOnly(17, 0), BreakDurationMinutes = breakMinutes,
        };

    [Fact]
    public void Configured_weekday_gives_the_window_minus_the_centred_break()
    {
        Assert.Equal(new PlannerWorkWindow(new TimeOnly(9, 0), new TimeOnly(17, 0), 60, new TimeOnly(12, 30), true, 420),
            PlannerWorkWindows.For(Entity(), Monday));
    }

    [Fact]
    public void Weekend_keeps_the_window_but_has_no_working_minutes()
    {
        var window = PlannerWorkWindows.For(Entity(), Sunday)!;

        Assert.Equal((false, 0), (window.IsWorkingDay, window.WorkingMinutes));
        Assert.Equal(new TimeOnly(9, 0), window.WorkStart);
    }

    [Fact]
    public void Overnight_or_missing_times_or_missing_zone_is_null()
    {
        Assert.Null(PlannerWorkWindows.For(Entity(start: new TimeOnly(22, 0), end: new TimeOnly(6, 0)), Monday));
        var noStart = Entity();
        noStart.WorkStartTime = null;
        Assert.Null(PlannerWorkWindows.For(noStart, Monday));
        Assert.Null(PlannerWorkWindows.For(Entity(timezone: null), Monday));
    }

    [Fact]
    public void No_break_has_no_break_start()
    {
        var window = PlannerWorkWindows.For(Entity(breakMinutes: null), Monday)!;

        Assert.Equal((0, (TimeOnly?)null, 480), (window.BreakMinutes, window.BreakStart, window.WorkingMinutes));
    }

    [Fact]
    public void An_odd_break_starts_on_a_quarter_hour()
    {
        // 09:00-17:00 with 45 minutes: (480 - 45) / 2 = 217.5 minutes after 09:00 = 12:37:30, rounded down to 12:30.
        Assert.Equal(new TimeOnly(12, 30), PlannerWorkWindows.For(Entity(breakMinutes: 45), Monday)!.BreakStart);
    }

    [Fact]
    public void No_legal_entity_is_null()
    {
        Assert.Null(PlannerWorkWindows.For(null, Monday));
    }
}
