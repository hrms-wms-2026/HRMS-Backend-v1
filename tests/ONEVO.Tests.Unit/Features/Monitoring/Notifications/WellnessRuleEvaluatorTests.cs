using FluentAssertions;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Infrastructure.Services.Monitoring.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public class WellnessRuleEvaluatorTests
{
    private static DeviceStateSnapshot Sample(DateTimeOffset capturedAt, bool isIdle) => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), AgentDeviceId = Guid.NewGuid(),
        CapturedAt = capturedAt, IsIdle = isIdle, IdleSeconds = isIdle ? 130 : 0
    };

    [Fact]
    public void Evaluate_120ConsecutiveActiveMinutes_TriggersBreakReminder()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, 121)
            .Select(i => Sample(now.AddMinutes(-121 + i), isIdle: false))
            .ToList();

        var result = WellnessRuleEvaluator.Evaluate(snapshots, now);

        result.BreakReminderTriggered.Should().BeTrue();
        result.LongIdleTriggered.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_30ConsecutiveIdleMinutes_TriggersLongIdleAlert()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, 31)
            .Select(i => Sample(now.AddMinutes(-31 + i), isIdle: true))
            .ToList();

        var result = WellnessRuleEvaluator.Evaluate(snapshots, now);

        result.LongIdleTriggered.Should().BeTrue();
        result.BreakReminderTriggered.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_IdleGapBreaksTheActiveStreak()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var snapshots = new List<DeviceStateSnapshot>();
        for (var i = 0; i < 100; i++) snapshots.Add(Sample(now.AddMinutes(-121 + i), isIdle: false));
        snapshots.Add(Sample(now.AddMinutes(-21), isIdle: true));
        for (var i = 0; i < 20; i++) snapshots.Add(Sample(now.AddMinutes(-20 + i), isIdle: false));

        var result = WellnessRuleEvaluator.Evaluate(snapshots, now);

        result.BreakReminderTriggered.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FewerThanThresholdMinutes_NoTrigger()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, 10)
            .Select(i => Sample(now.AddMinutes(-10 + i), isIdle: false))
            .ToList();

        var result = WellnessRuleEvaluator.Evaluate(snapshots, now);

        result.BreakReminderTriggered.Should().BeFalse();
        result.LongIdleTriggered.Should().BeFalse();
    }
}
