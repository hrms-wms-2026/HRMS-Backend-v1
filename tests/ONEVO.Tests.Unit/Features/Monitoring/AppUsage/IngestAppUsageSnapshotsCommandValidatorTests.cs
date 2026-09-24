using FluentAssertions;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class IngestAppUsageSnapshotsCommandValidatorTests
{
    private readonly IngestAppUsageSnapshotsCommandValidator _sut = new();

    private static AppUsageSnapshotItem Item() => new()
    {
        CapturedAt = DateTimeOffset.UtcNow,
        ProcessName = "code.exe",
        WindowTitleHash = "abc123"
    };

    [Fact]
    public void Empty_snapshots_fails()
    {
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_single_snapshot_passes()
    {
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [Item()] });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ProcessName_with_path_separator_fails()
    {
        var item = Item() with { ProcessName = "C:\\evil.exe" };
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = [item] });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Batch_over_200_fails()
    {
        var items = Enumerable.Range(0, 201).Select(_ => Item()).ToList();
        var result = _sut.Validate(new IngestAppUsageSnapshotsCommand { Snapshots = items });
        result.IsValid.Should().BeFalse();
    }
}
