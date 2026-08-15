using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Dashboard.Services;

namespace ONEVO.Tests.Unit.Features.Monitoring.Dashboard;

public class MonitoringAlertEvaluatorTests
{
    private static readonly DateOnly Date = new(2026, 8, 14);

    [Fact]
    public void Evaluate_returns_late_login_when_first_clock_in_exceeds_grace()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 90, idleMinutes: 30),
            [Session(clockInHour: 9, clockInMinute: 11, clockOutHour: 18, clockOutMinute: 0)]);

        alerts.Should().Contain(a => a.Code == "late_login");
    }

    [Fact]
    public void Evaluate_returns_early_logout_when_last_clock_out_is_before_grace_boundary()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 90, idleMinutes: 30),
            [Session(clockInHour: 9, clockInMinute: 0, clockOutHour: 17, clockOutMinute: 49)]);

        alerts.Should().Contain(a => a.Code == "early_logout");
    }

    [Fact]
    public void Evaluate_returns_long_idle_when_idle_minutes_exceed_threshold()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 90, idleMinutes: 121),
            [Session(clockInHour: 9, clockInMinute: 0, clockOutHour: 18, clockOutMinute: 0)]);

        alerts.Should().Contain(a => a.Code == "long_idle");
    }

    [Fact]
    public void Evaluate_returns_low_activity_score_when_score_is_low_and_coverage_is_meaningful()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 49, coverage: 80, idleMinutes: 30),
            [Session(clockInHour: 9, clockInMinute: 0, clockOutHour: 18, clockOutMinute: 0)]);

        alerts.Should().Contain(a => a.Code == "low_activity_score");
    }

    [Fact]
    public void Evaluate_returns_low_data_coverage_when_coverage_is_below_threshold()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 59, idleMinutes: 30),
            [Session(clockInHour: 9, clockInMinute: 0, clockOutHour: 18, clockOutMinute: 0)]);

        alerts.Should().Contain(a => a.Code == "low_data_coverage");
    }

    [Fact]
    public void Evaluate_returns_no_alerts_for_normal_day()
    {
        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 90, idleMinutes: 30),
            [Session(clockInHour: 9, clockInMinute: 0, clockOutHour: 18, clockOutMinute: 0)]);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_uses_employee_time_zone_for_shift_alerts()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Sri Lanka Test",
            TimeSpan.FromMinutes(330),
            "Sri Lanka Test",
            "Sri Lanka Test");

        var alerts = MonitoringAlertEvaluator.Evaluate(
            Summary(activityScore: 80, coverage: 90, idleMinutes: 30),
            [
                new WorkSessionReportDto(
                    SessionId: Guid.NewGuid(),
                    ClockInAt: new DateTimeOffset(2026, 8, 14, 3, 30, 0, TimeSpan.Zero),
                    ClockOutAt: new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero),
                    WorkSeconds: 8 * 60 * 60,
                    BreakSeconds: 60 * 60,
                    BreakCount: 1)
            ],
            timeZone);

        alerts.Should().BeEmpty();
    }

    private static ActivityDailySummaryDto Summary(
        decimal activityScore,
        decimal coverage,
        int idleMinutes) => new()
    {
        EmployeeId = Guid.NewGuid(),
        Date = Date,
        TotalActiveMinutes = 420,
        TotalIdleMinutes = idleMinutes,
        TotalMeetingMinutes = 30,
        ActivePercentage = 80,
        ActivityScore = activityScore,
        DataCoveragePercentage = coverage
    };

    private static WorkSessionReportDto Session(
        int clockInHour,
        int clockInMinute,
        int clockOutHour,
        int clockOutMinute) => new(
        SessionId: Guid.NewGuid(),
        ClockInAt: new DateTimeOffset(2026, 8, 14, clockInHour, clockInMinute, 0, TimeSpan.Zero),
        ClockOutAt: new DateTimeOffset(2026, 8, 14, clockOutHour, clockOutMinute, 0, TimeSpan.Zero),
        WorkSeconds: 8 * 60 * 60,
        BreakSeconds: 60 * 60,
        BreakCount: 1);
}
