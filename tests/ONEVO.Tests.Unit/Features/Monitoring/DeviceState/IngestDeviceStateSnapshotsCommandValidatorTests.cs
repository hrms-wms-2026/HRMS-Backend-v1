using FluentAssertions;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class IngestDeviceStateSnapshotsCommandValidatorTests
{
    private readonly IngestDeviceStateSnapshotsCommandValidator _sut = new();

    private static DeviceStateSnapshotItem Item() => new()
    {
        CapturedAt = DateTimeOffset.UtcNow,
        IdleSeconds = 30,
        IsIdle = false
    };

    [Fact]
    public void Empty_snapshots_fails()
    {
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_single_snapshot_passes()
    {
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [Item()] });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Negative_idle_seconds_fails()
    {
        var item = Item() with { IdleSeconds = -1 };
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Idle_seconds_over_ceiling_fails()
    {
        var item = Item() with { IdleSeconds = 90_000 };
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Batch_over_200_fails()
    {
        var items = Enumerable.Range(0, 201).Select(_ => Item()).ToList();
        var result = _sut.Validate(new IngestDeviceStateSnapshotsCommand { Snapshots = items });
        result.IsValid.Should().BeFalse();
    }
}
