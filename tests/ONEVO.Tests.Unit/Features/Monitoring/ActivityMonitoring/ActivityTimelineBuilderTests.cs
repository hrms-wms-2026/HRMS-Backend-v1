using FluentAssertions;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class ActivityTimelineBuilderTests
{
    private static ActivitySnapshot Snap(
        int activeSeconds, int idleSeconds, string? process, DateTimeOffset capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        AgentDeviceId = Guid.NewGuid(),
        CapturedAt = capturedAt,
        ActiveSeconds = activeSeconds,
        IdleSeconds = idleSeconds,
        IntensityScore = 50,
        ForegroundProcessName = process,
        CreatedAt = capturedAt
    };

    [Fact]
    public void Empty_snapshots_returns_no_segments()
    {
        var segments = ActivityTimelineBuilder.BuildSegments([]);

        segments.Should().BeEmpty();
    }

    [Fact]
    public void Thirty_plus_minutes_same_app_becomes_one_focus_segment()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        // Six 5-minute active snapshots in the same app = 30 contiguous active minutes.
        var snapshots = Enumerable.Range(0, 6)
            .Select(i => Snap(300, 0, "code.exe", baseTime.AddMinutes((i + 1) * 5)))
            .ToList();

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().ContainSingle();
        segments[0].Type.Should().Be(ActivityTimelineBuilder.FocusType);
        segments[0].StartedAt.Should().Be(baseTime);
        segments[0].EndedAt.Should().Be(baseTime.AddMinutes(30));
    }

    [Fact]
    public void Under_thirty_minutes_active_streak_is_classified_idle()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        // Two 5-minute active snapshots in the same app = 10 contiguous active minutes.
        var snapshots = Enumerable.Range(0, 2)
            .Select(i => Snap(300, 0, "code.exe", baseTime.AddMinutes((i + 1) * 5)))
            .ToList();

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().ContainSingle();
        segments[0].Type.Should().Be(ActivityTimelineBuilder.IdleType);
    }

    [Fact]
    public void Fully_idle_snapshots_become_an_idle_segment()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshots = new List<ActivitySnapshot>
        {
            Snap(0, 300, null, baseTime.AddMinutes(5)),
            Snap(0, 300, null, baseTime.AddMinutes(10))
        };

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().ContainSingle();
        segments[0].Type.Should().Be(ActivityTimelineBuilder.IdleType);
        segments[0].StartedAt.Should().Be(baseTime);
        segments[0].EndedAt.Should().Be(baseTime.AddMinutes(10));
    }

    [Fact]
    public void Switching_app_mid_focus_streak_breaks_it_into_separate_segments()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshots = new List<ActivitySnapshot>
        {
            // 3x 5-min in code.exe = 15 active minutes (under threshold on its own)
            Snap(300, 0, "code.exe", baseTime.AddMinutes(5)),
            Snap(300, 0, "code.exe", baseTime.AddMinutes(10)),
            Snap(300, 0, "code.exe", baseTime.AddMinutes(15)),
            // switch to slack.exe for 5 min
            Snap(300, 0, "slack.exe", baseTime.AddMinutes(20))
        };

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().HaveCount(2);
        segments[0].Type.Should().Be(ActivityTimelineBuilder.IdleType); // 15 min, under threshold
        segments[1].Type.Should().Be(ActivityTimelineBuilder.IdleType); // 5 min, under threshold
    }

    [Fact]
    public void Active_then_idle_then_active_produces_three_ordered_segments()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, 6)
            .Select(i => Snap(300, 0, "code.exe", baseTime.AddMinutes((i + 1) * 5)))
            .Concat([Snap(0, 300, null, baseTime.AddMinutes(35))])
            .Concat(Enumerable.Range(6, 6)
                .Select(i => Snap(300, 0, "code.exe", baseTime.AddMinutes((i + 1) * 5 + 5))))
            .ToList();

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().HaveCount(3);
        segments[0].Type.Should().Be(ActivityTimelineBuilder.FocusType);
        segments[1].Type.Should().Be(ActivityTimelineBuilder.IdleType);
        segments[2].Type.Should().Be(ActivityTimelineBuilder.FocusType);
    }

    [Fact]
    public void Zero_duration_snapshots_are_ignored()
    {
        var baseTime = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var snapshots = new List<ActivitySnapshot>
        {
            Snap(0, 0, null, baseTime.AddMinutes(5)),
            Snap(300, 0, "code.exe", baseTime.AddMinutes(10))
        };

        var segments = ActivityTimelineBuilder.BuildSegments(snapshots);

        segments.Should().ContainSingle();
        segments[0].StartedAt.Should().Be(baseTime.AddMinutes(5));
        segments[0].EndedAt.Should().Be(baseTime.AddMinutes(10));
    }
}
