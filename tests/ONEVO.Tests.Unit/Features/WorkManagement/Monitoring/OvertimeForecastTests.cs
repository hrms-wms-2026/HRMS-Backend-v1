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
        Assert.Equal(20m, f.OvertimeHours);   // 10 task hours short at 50% = 20 extra work hours
    }

    [Fact]
    public void Compute_CannotFitEvenAtFullRate_Overtime()
    {
        var f = OvertimeForecast.Compute(remaining: 120m, capacity: 100m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.Overtime, f.Status);
        Assert.Equal(1.2m, f.RequiredRate);
        Assert.Equal(140m, f.OvertimeHours);
    }

    [Fact]
    public void Compute_NoCapacityLeft_OvertimeWithNullRequiredRate()
    {
        var f = OvertimeForecast.Compute(remaining: 10m, capacity: 0m, rate: 0.5m);
        Assert.Equal(ForecastStatuses.Overtime, f.Status);
        Assert.Null(f.RequiredRate);
        Assert.Equal(20m, f.OvertimeHours);
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
    public void EmployeeWindows_PicksTheWindowWithMostOvertime_AndTheHighestRequiredRate()
    {
        // Window Fri 9: 5 days x 8h = 40h capacity, 30h demand at 50% -> required 0.75, overtime (30-20)/0.5 = 20.
        // Window Fri 16: 80h capacity, 30+20 = 50h demand -> required 0.625, overtime (50-40)/0.5 = 20... add 10 more -> 60h, overtime 40.
        var w = OvertimeForecast.EmployeeWindows([(Fri9, 30m), (Fri16, 20m), (Fri16, 10m)], Cal, Mon5, 0.5m)!;
        Assert.Equal(Fri16, w.Deadline);
        Assert.Equal(60m, w.Figures.Remaining);
        Assert.Equal(80m, w.Figures.Capacity);
        Assert.Equal(40m, w.Figures.OvertimeHours);
        Assert.Equal(0.75m, w.RequiredRate);
        Assert.Equal(ForecastStatuses.RaiseRate, w.Status);
    }

    [Fact]
    public void EmployeeWindows_AnyWindowOverFullRate_StatusOvertime()
    {
        var w = OvertimeForecast.EmployeeWindows([(Fri9, 45m), (Fri16, 1m)], Cal, Mon5, 0.9m)!;
        Assert.Equal(ForecastStatuses.Overtime, w.Status);
        Assert.True(w.RequiredRate > 1m);
    }

    [Fact]
    public void EmployeeWindows_NoHistory_StatusNoHistory()
        => Assert.Equal(ForecastStatuses.NoHistory, OvertimeForecast.EmployeeWindows([(Fri9, 50m)], Cal, Mon5, null)!.Status);
}
