using FluentAssertions;
using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;
using ONEVO.Application.Features.Monitoring.Dashboard.Services;

namespace ONEVO.Tests.Unit.Features.Monitoring.Dashboard;

public class MonitoringDashboardStatusServiceTests
{
    [Fact]
    public void ResolveStatus_returns_active_when_latest_snapshot_is_fresh_and_not_idle()
    {
        var now = new DateTimeOffset(2026, 8, 14, 9, 5, 0, TimeSpan.Zero);

        var status = MonitoringDashboardStatusService.ResolveStatus(
            now.AddMinutes(-4),
            isIdle: false,
            now);

        status.Should().Be(MonitoringEmployeeStatus.Active);
    }

    [Fact]
    public void ResolveStatus_returns_idle_when_latest_snapshot_is_fresh_and_idle()
    {
        var now = new DateTimeOffset(2026, 8, 14, 9, 5, 0, TimeSpan.Zero);

        var status = MonitoringDashboardStatusService.ResolveStatus(
            now.AddMinutes(-5),
            isIdle: true,
            now);

        status.Should().Be(MonitoringEmployeeStatus.Idle);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(-6, false)]
    [InlineData(-6, true)]
    public void ResolveStatus_returns_offline_when_snapshot_is_missing_or_stale(int? ageMinutes, bool? isIdle)
    {
        var now = new DateTimeOffset(2026, 8, 14, 9, 5, 0, TimeSpan.Zero);
        var capturedAt = ageMinutes is null
            ? (DateTimeOffset?)null
            : now.AddMinutes(ageMinutes.Value);

        var status = MonitoringDashboardStatusService.ResolveStatus(capturedAt, isIdle, now);

        status.Should().Be(MonitoringEmployeeStatus.Offline);
    }

    [Fact]
    public void Summarize_counts_statuses_attention_and_average_score()
    {
        var items = new List<MonitoringEmployeeDashboardItemDto>
        {
            Item(MonitoringEmployeeStatus.Active, 90m, alertCount: 0),
            Item(MonitoringEmployeeStatus.Idle, 50m, alertCount: 2),
            Item(MonitoringEmployeeStatus.Offline, null, alertCount: 1),
        };

        var summary = MonitoringDashboardStatusService.Summarize(items);

        summary.TotalEmployees.Should().Be(3);
        summary.ActiveEmployees.Should().Be(1);
        summary.IdleEmployees.Should().Be(1);
        summary.OfflineEmployees.Should().Be(1);
        summary.AttentionNeededEmployees.Should().Be(2);
        summary.AverageActivityScore.Should().Be(70m);
    }

    private static MonitoringEmployeeDashboardItemDto Item(
        MonitoringEmployeeStatus status,
        decimal? score,
        int alertCount) => new(
        EmployeeId: Guid.NewGuid(),
        EmployeeNumber: "E-1",
        FullName: "Test Employee",
        Email: "employee@example.com",
        DepartmentName: "Engineering",
        PositionName: "Developer",
        Status: status,
        LastCapturedAt: null,
        ActiveMinutes: 0,
        IdleMinutes: 0,
        ActivityScore: score,
        DataCoveragePercentage: null,
        TopApps: [],
        Alerts: Enumerable.Range(0, alertCount)
            .Select(i => new MonitoringDashboardAlertDto($"alert_{i}", "Alert", "info"))
            .ToList());
}
