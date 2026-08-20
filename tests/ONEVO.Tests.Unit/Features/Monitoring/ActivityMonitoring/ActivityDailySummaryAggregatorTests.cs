using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public class ActivityDailySummaryAggregatorTests
{
    private static ActivitySnapshot Snap(
        int activeSeconds,
        int idleSeconds,
        int keyboard,
        int mouse,
        decimal intensity,
        string? process,
        DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        AgentDeviceId = Guid.NewGuid(),
        CapturedAt = capturedAt,
        ActiveSeconds = activeSeconds,
        IdleSeconds = idleSeconds,
        KeyboardEventsCount = keyboard,
        MouseEventsCount = mouse,
        IntensityScore = intensity,
        ForegroundProcessName = process,
        CreatedAt = capturedAt
    };

    [Fact]
    public void Aggregates_active_idle_keyboard_mouse()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

        var snapshots = new List<ActivitySnapshot>
        {
            Snap(120, 0, 50, 30, 80, "code.exe", baseTime),
            Snap(60, 60, 10, 5, 40, "code.exe", baseTime.AddMinutes(5)),
            Snap(0, 120, 0, 0, 0, "slack.exe", baseTime.AddMinutes(10)),
        };

        var summary = ActivityDailySummaryAggregator.Aggregate(
            tenantId, employeeId, new DateOnly(2026, 8, 5), snapshots, baseTime);

        summary.TenantId.Should().Be(tenantId);
        summary.EmployeeId.Should().Be(employeeId);
        summary.TotalActiveMinutes.Should().Be(3); // 180s
        summary.TotalIdleMinutes.Should().Be(3);   // 180s
        summary.KeyboardTotal.Should().Be(60);
        summary.MouseTotal.Should().Be(35);
        summary.IntensityAvg.Should().Be(60m); // (80+40)/2 — idle window excluded
        summary.DataSource.Should().Be("agent_windows");
        summary.TotalMeetingMinutes.Should().Be(0);
    }

    [Fact]
    public void Focus_requires_30_contiguous_active_minutes_same_process()
    {
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        // 6 x 5 min active in code.exe = 30 min
        var snapshots = Enumerable.Range(0, 6)
            .Select(i => Snap(300, 0, 1, 1, 70, "code.exe", baseTime.AddMinutes(i * 5)))
            .ToList();

        var summary = ActivityDailySummaryAggregator.Aggregate(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 5), snapshots, baseTime);

        summary.FocusMinutes.Should().Be(30);
        summary.DeepFocusSessionsCount.Should().Be(1);
    }

    [Fact]
    public void Process_change_breaks_focus_streak()
    {
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        var snapshots = new List<ActivitySnapshot>
        {
            Snap(300, 0, 1, 1, 70, "code.exe", baseTime),
            Snap(300, 0, 1, 1, 70, "code.exe", baseTime.AddMinutes(5)),
            Snap(300, 0, 1, 1, 70, "chrome.exe", baseTime.AddMinutes(10)), // break
            Snap(300, 0, 1, 1, 70, "chrome.exe", baseTime.AddMinutes(15)),
        };

        var (focusMinutes, sessions) = ActivityDailySummaryAggregator.ComputeFocus(snapshots);

        focusMinutes.Should().Be(0);
        sessions.Should().Be(0);
    }

    [Fact]
    public void Data_coverage_capped_at_100()
    {
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        // 10 hours of intervals (600 min) vs 480 expected
        var snapshots = Enumerable.Range(0, 120)
            .Select(i => Snap(300, 0, 0, 0, 50, "a.exe", baseTime.AddMinutes(i * 5)))
            .ToList();

        var summary = ActivityDailySummaryAggregator.Aggregate(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 5), snapshots, baseTime);

        summary.DataCoveragePercentage.Should().Be(100m);
    }

    [Fact]
    public void Computes_top_10_apps_by_active_seconds_descending()
    {
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        var snapshots = new List<ActivitySnapshot>
        {
            Snap(300, 0, 10, 10, 70, "code.exe", baseTime),
            Snap(120, 0, 5, 5, 60, "slack.exe", baseTime.AddMinutes(5)),
            Snap(180, 0, 8, 8, 65, "code.exe", baseTime.AddMinutes(10)),
            Snap(0, 300, 0, 0, 0, null, baseTime.AddMinutes(15)), // idle, no process — excluded
        };

        var summary = ActivityDailySummaryAggregator.Aggregate(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 5), snapshots, baseTime);

        var topApps = System.Text.Json.JsonSerializer.Deserialize<List<AppUsageSummary>>(summary.TopAppsJson)!;
        topApps.Should().HaveCount(2);
        topApps[0].AppName.Should().Be("code.exe");
        topApps[0].TotalSeconds.Should().Be(480); // 300 + 180
        topApps[1].AppName.Should().Be("slack.exe");
        topApps[1].TotalSeconds.Should().Be(120);
    }

    [Fact]
    public void Top_apps_caps_at_10_and_is_empty_json_array_when_no_active_process()
    {
        var baseTime = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, 12)
            .Select(i => Snap(60, 0, 1, 1, 50, $"app{i}.exe", baseTime.AddMinutes(i)))
            .ToList();

        var summary = ActivityDailySummaryAggregator.Aggregate(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 5), snapshots, baseTime);

        var topApps = System.Text.Json.JsonSerializer.Deserialize<List<AppUsageSummary>>(summary.TopAppsJson)!;
        topApps.Should().HaveCount(10);

        var idleOnly = new List<ActivitySnapshot> { Snap(0, 300, 0, 0, 0, "explorer.exe", baseTime) };
        var idleSummary = ActivityDailySummaryAggregator.Aggregate(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 5), idleOnly, baseTime);
        idleSummary.TopAppsJson.Should().Be("[]");
    }
}
