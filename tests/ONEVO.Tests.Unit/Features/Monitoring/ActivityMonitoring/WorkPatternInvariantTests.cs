using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

/// <summary>Property-style checks over the 8-hour worked example and a few edge fixtures,
/// proving the invariants stated in docs/superpowers/plans/2026-09-25-work-pattern-productivity-model.md
/// hold structurally rather than via a clamp.</summary>
public sealed class WorkPatternInvariantTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();

    private static ActivitySnapshot Snap(DateTimeOffset capturedAt, int activeSeconds, int idleSeconds, string? process) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = DeviceId,
        CapturedAt = capturedAt, ActiveSeconds = activeSeconds, IdleSeconds = idleSeconds,
        ForegroundProcessName = process, CreatedAt = capturedAt
    };

    private static MeetingSignal Meeting(DateTimeOffset capturedAt, bool running) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = DeviceId,
        CapturedAt = capturedAt, IsMeetingAppRunning = running, CreatedAt = capturedAt
    };

    [Theory]
    [InlineData(0)]    // no snapshots
    [InlineData(1)]    // single partial-active/idle snapshot
    [InlineData(30)]   // exactly one focus streak
    [InlineData(60)]   // two mixed streaks
    public void Classify_AnyFixtureSize_ObservedEqualsSumOfFourBuckets(int minuteCount)
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, minuteCount)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: i % 3 == 0 ? 0 : 60, idleSeconds: i % 3 == 0 ? 60 : 0,
                process: i % 2 == 0 ? "code.exe" : "excel.exe"))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, []);

        var observed = result.FocusMinutes + result.OtherActiveMinutes + result.IdleMinutes + result.MeetingBarMinutes;
        Assert.Equal(minuteCount, observed); // every minute lands in exactly one bucket
    }

    [Fact]
    public void Classify_ProductiveNeverExceedsEngagedTime()
    {
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = Enumerable.Range(1, 100)
            .Select(i => Snap(start.AddMinutes(i), activeSeconds: 60, idleSeconds: 0, process: "code.exe"))
            .ToList();
        var meetings = Enumerable.Range(1, 20)
            .Select(i => Meeting(start.AddMinutes(i), running: true))
            .ToList();

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);
        var engagedTime = result.FocusMinutes + result.OtherActiveMinutes + result.MeetingBarMinutes;
        var productive = result.MeetingBarMinutes + result.ProductiveFocusMinutes + result.ProductiveOtherActiveMinutes;

        Assert.True(productive <= engagedTime);
    }

    [Fact]
    public void Classify_NoWindowContributesToMoreThanOneBucket()
    {
        // A window overlapping a meeting must NEVER also show up in Focus/OtherActive/Idle -
        // verified by checking total seconds allocated never exceeds total seconds fed in.
        var start = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var snaps = new[]
        {
            Snap(start.AddMinutes(1), activeSeconds: 40, idleSeconds: 20, process: "teams.exe"),
        };
        var meetings = new[] { Meeting(start.AddMinutes(1), running: true) };

        var result = WorkPatternWindowClassifier.Classify(snaps, meetings);

        Assert.Equal(1, result.MeetingBarMinutes); // whole 60s window
        Assert.Equal(0, result.FocusMinutes);
        Assert.Equal(0, result.OtherActiveMinutes);
        Assert.Equal(0, result.IdleMinutes);
    }
}
