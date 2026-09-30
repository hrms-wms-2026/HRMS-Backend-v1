using FluentAssertions;
using ONEVO.Application.Features.Monitoring.Exceptions.Helpers;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Exceptions;

public class DeviceActivityTimelineTests
{
    private static readonly Guid Device = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 3, 30, 0, TimeSpan.Zero);

    private static DeviceStateSnapshot At(int minute, bool idle, Guid? device = null) => new()
    {
        Id = Guid.NewGuid(), AgentDeviceId = device ?? Device, CapturedAt = Start.AddMinutes(minute), IsIdle = idle
    };

    [Fact]
    public void NoSamples_IsAnEmptyDay()
    {
        var result = DeviceActivityTimeline.Build([]);

        result.FirstSeenAt.Should().BeNull();
        result.Segments.Should().BeEmpty();
        result.ActiveMinutes.Should().Be(0);
    }

    [Fact]
    public void MinuteSamples_CollapseIntoRuns_WithActiveAndIdleTotals()
    {
        var samples = Enumerable.Range(0, 30).Select(m => At(m, idle: m >= 20)).ToList();

        var result = DeviceActivityTimeline.Build(samples);

        result.Segments.Select(s => s.State).Should().Equal(DeviceActivityTimeline.Active, DeviceActivityTimeline.Idle);
        result.ActiveMinutes.Should().Be(20);
        result.IdleMinutes.Should().Be(9);
        result.OfflineMinutes.Should().Be(0);
        result.FirstSeenAt.Should().Be(Start);
        result.LastSeenAt.Should().Be(Start.AddMinutes(29));
    }

    [Fact]
    public void ALongSilence_IsShownAsOffline_AndNotCountedAsActive()
    {
        var result = DeviceActivityTimeline.Build([At(0, false), At(5, false), At(65, false), At(70, false)]);

        result.Segments.Select(s => s.State).Should().Equal(
            DeviceActivityTimeline.Active, DeviceActivityTimeline.Offline, DeviceActivityTimeline.Active);
        result.Segments[1].Start.Should().Be(Start.AddMinutes(5));
        result.Segments[1].End.Should().Be(Start.AddMinutes(65));
        result.OfflineMinutes.Should().Be(60);
        result.ActiveMinutes.Should().Be(10);
    }

    [Fact]
    public void SamplesOutOfOrder_AreSorted_AndEveryReportingDeviceIsListed()
    {
        var second = Guid.NewGuid();

        var result = DeviceActivityTimeline.Build([At(10, false, second), At(0, false), At(5, false)]);

        result.FirstSeenAt.Should().Be(Start);
        result.DeviceIds.Should().BeEquivalentTo([Device, second]);
    }

    [Fact]
    public void TooManyRuns_AreTruncated_ButTotalsCoverTheWholeDay()
    {
        var samples = Enumerable.Range(0, (DeviceActivityTimeline.MaxSegments + 50) * 2)
            .Select(m => At(m, idle: m % 2 == 1)).ToList();

        var result = DeviceActivityTimeline.Build(samples);

        result.Truncated.Should().BeTrue();
        result.Segments.Should().HaveCount(DeviceActivityTimeline.MaxSegments);
        (result.ActiveMinutes + result.IdleMinutes).Should().Be(samples.Count - 1);
    }
}
