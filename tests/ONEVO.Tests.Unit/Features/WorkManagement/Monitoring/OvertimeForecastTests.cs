using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class OvertimeForecastTests
{
    // Mon 2026-10-05 .. Fri 2026-10-16 = 10 working days on the default 8h Mon-Fri calendar.
    private static readonly DateOnly Mon5 = new(2026, 10, 5);
    private static readonly DateOnly Fri9 = new(2026, 10, 9);
    private static readonly DateOnly Fri16 = new(2026, 10, 16);
    private static readonly WorkCalendar Cal = WorkCalendar.Default;

    [Fact]
    public void Window_IsThe28DaysBeforeToday_TodayExcluded()
    {
        var (start, end) = OvertimeForecast.Window(new DateOnly(2026, 11, 8));
        Assert.Equal(new DateOnly(2026, 10, 11), start);
        Assert.Equal(new DateOnly(2026, 11, 7), end);
    }

    [Theory]
    [InlineData(30, 60, 0.5)]
    [InlineData(90, 60, 1.0)]     // capped at 100%
    [InlineData(0, 60, 0.0)]
    public void Rate_IsTaskOverWorked_CappedAtOne(int task, int worked, double expected)
        => Assert.Equal((decimal)expected, OvertimeForecast.Rate(task, worked));

    [Fact]
    public void Rate_UnderAnHourOfWork_IsNull()
        => Assert.Null(OvertimeForecast.Rate(30, 59));

    [Fact]
    public void Compute_PredictedCoversRemaining_OnTrack()
    {
        var f = OvertimeForecast.Compute(remaining: 40m, capacity: 100m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.OnTrack, f.Status);
        Assert.Equal(50m, f.Predicted);
        Assert.Equal(0.4m, f.RequiredRate);
        Assert.Equal(0m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_FitsAtAHigherRate_RaiseRate()
    {
        var f = OvertimeForecast.Compute(remaining: 60m, capacity: 100m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.RaiseRate, f.Status);
        Assert.Equal(50m, f.Predicted);
        Assert.Equal(0.6m, f.RequiredRate);
        Assert.Equal(10m, f.OvertimeHours);   // the 10 task hours that won't get done in normal hours
    }

    [Fact]
    public void Compute_CannotFitEvenAtFullRate_Overload()
    {
        var f = OvertimeForecast.Compute(remaining: 120m, capacity: 100m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.Overload, f.Status);
        Assert.Equal(1.2m, f.RequiredRate);
        Assert.Equal(70m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_NoCapacityLeft_OverloadWithNullRequiredRate()
    {
        var f = OvertimeForecast.Compute(remaining: 10m, capacity: 0m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.Overload, f.Status);
        Assert.Null(f.RequiredRate);
        Assert.Equal(10m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_NothingRemainingAndNoCapacity_OnTrack()
        => Assert.Equal(ForecastStatuses.OnTrack, OvertimeForecast.Compute(0m, 0m, 0.5m).Status);

    [Fact]
    public void Compute_RateZero_DoesNotDivideByZero_ReportsTheShortfall()
    {
        var f = OvertimeForecast.Compute(remaining: 30m, capacity: 100m, rate: 0m);
        Assert.Equal(ForecastStatuses.RaiseRate, f.Status);
        Assert.Equal(0m, f.Predicted);
        Assert.Equal(30m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_NoHistory_UsesRateOneButSaysNoHistory()
    {
        var f = OvertimeForecast.Compute(remaining: 120m, capacity: 100m, rate: null);
        Assert.Equal(ForecastStatuses.NoHistory, f.Status);
        Assert.Null(f.Rate);
        Assert.Equal(100m, f.Predicted);
        Assert.Equal(20m, f.OvertimeHours);
    }

    [Fact]
    public void EmployeeWindows_NoItems_Null()
        => Assert.Null(OvertimeForecast.EmployeeWindows([], Cal, Mon5, 0.5m));

    [Fact]
    public void Compute_OvertimeNeverExceedsRemaining_EvenAtALowRate()
    {
        // 510h left, 4.6% rate: the old (shortfall / rate) gave ~10,600 work hours; now the unfinished task hours.
        var f = OvertimeForecast.Compute(remaining: 510m, capacity: 400m, rate: 0.046m);
        Assert.Equal(510m - 400m * 0.046m, f.OvertimeHours);
        Assert.True(f.OvertimeHours <= f.Remaining);
    }

    [Fact]
    public void EmployeeWindows_TakeEveryFigureFromTheBindingWindow_TheHighestRequiredRate()
    {
        // Window Fri 9: 40h capacity, 30h due -> required 0.75, predicted 20, overtime 10.
        // Window Fri 16: 80h capacity, 60h due -> required 0.75 too, predicted 40, overtime 20 -> the tie goes to the larger shortfall.
        var w = OvertimeForecast.EmployeeWindows([(Fri9, 30m), (Fri16, 20m), (Fri16, 10m)], Cal, Mon5, 0.5m)!;
        Assert.Equal(Fri16, w.Deadline);
        Assert.Equal((60m, 80m, 40m, 20m), (w.Figures.Remaining, w.Figures.Capacity, w.Figures.Predicted, w.Figures.OvertimeHours));
        Assert.Equal(0.75m, w.RequiredRate);
        Assert.Equal(w.Figures.RequiredRate, w.RequiredRate);
        Assert.Equal(ForecastStatuses.RaiseRate, w.Status);
    }

    [Fact]
    public void EmployeeWindows_ARushedEarlyDeadlineBinds_EvenWithLessOvertime()
    {
        // Window Mon 5: 8h capacity, 20h due -> required 2.5, overtime 20 - 4 = 16.
        // Window Fri 16: 80h capacity, 70h due -> required 0.875, overtime 70 - 40 = 30 (more hours, but not binding).
        var w = OvertimeForecast.EmployeeWindows([(Mon5, 20m), (Fri16, 50m)], Cal, Mon5, 0.5m)!;
        Assert.Equal(Mon5, w.Deadline);
        Assert.Equal((20m, 8m, 4m, 16m), (w.Figures.Remaining, w.Figures.Capacity, w.Figures.Predicted, w.Figures.OvertimeHours));
        Assert.Equal(2.5m, w.RequiredRate);
        Assert.Equal(ForecastStatuses.Overload, w.Status);
    }

    [Fact]
    public void EmployeeWindows_AnyWindowOverTheLimit_StatusOverload()
    {
        var w = OvertimeForecast.EmployeeWindows([(Fri9, 45m), (Fri16, 1m)], Cal, Mon5, 0.9m)!;
        Assert.Equal(ForecastStatuses.Overload, w.Status);
        Assert.True(w.RequiredRate > 1m);
    }

    // --- the max task rate (overload threshold) ---

    [Fact]
    public void Compute_RequiredExactlyAtTheLimit_IsRaiseRate_NotOverload()
    {
        var f = OvertimeForecast.Compute(remaining: 90m, capacity: 100m, rate: 0.5m, maxTaskRate: 0.9m);
        Assert.Equal(ForecastStatuses.RaiseRate, f.Status);
        Assert.False(f.Overload);
        Assert.Equal(40m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_RequiredAboveTheLimit_IsOverload_EvenUnderFullRate()
    {
        var f = OvertimeForecast.Compute(remaining: 95m, capacity: 100m, rate: 0.5m, maxTaskRate: 0.9m);
        Assert.Equal(ForecastStatuses.Overload, f.Status);
        Assert.True(f.Overload);
        Assert.Equal(45m, f.OvertimeHours);   // overtime hours are unchanged by the limit
        Assert.Equal(ForecastStatuses.RaiseRate, OvertimeForecast.Compute(95m, 100m, 0.5m, maxTaskRate: 1m).Status);
    }

    [Fact]
    public void Compute_NoCapacity_IsOverload_AndNoHistoryStillFlagsOverload()
    {
        Assert.Equal(ForecastStatuses.Overload, OvertimeForecast.Compute(10m, 0m, 0.5m, maxTaskRate: 0.9m).Status);
        var none = OvertimeForecast.Compute(95m, 100m, rate: null, maxTaskRate: 0.9m);
        Assert.Equal(ForecastStatuses.NoHistory, none.Status);
        Assert.True(none.Overload);
    }

    [Fact]
    public void Compute_DefaultLimit_Is90Percent()
    {
        Assert.Equal(ForecastStatuses.RaiseRate, OvertimeForecast.Compute(90m, 100m, 0.5m).Status);
        Assert.Equal(ForecastStatuses.Overload, OvertimeForecast.Compute(91m, 100m, 0.5m).Status);
    }

    [Theory]
    [InlineData(null, 0.90)]    // no settings row
    [InlineData(0.95, 0.95)]
    [InlineData(0.30, 0.50)]    // clamped up
    [InlineData(1.20, 1.00)]    // clamped down
    public void TaskRateLimit_DefaultsAndClamps(double? stored, double expected)
        => Assert.Equal((decimal)expected, TaskRateLimit.Resolve(stored is { } v ? (decimal)v : null));

    [Fact]
    public void EmployeeWindows_UseTheLimit()
    {
        // Fri 9 window: 40h capacity, 37h due -> required 0.925: overload at 90%, raise_rate at 95%.
        Assert.Equal(ForecastStatuses.Overload, OvertimeForecast.EmployeeWindows([(Fri9, 37m)], Cal, Mon5, 0.5m, 0.9m)!.Status);
        Assert.Equal(ForecastStatuses.RaiseRate, OvertimeForecast.EmployeeWindows([(Fri9, 37m)], Cal, Mon5, 0.5m, 0.95m)!.Status);
    }

    [Fact]
    public void EmployeeWindows_NoHistory_StatusNoHistory()
        => Assert.Equal(ForecastStatuses.NoHistory, OvertimeForecast.EmployeeWindows([(Fri9, 50m)], Cal, Mon5, null)!.Status);
}
