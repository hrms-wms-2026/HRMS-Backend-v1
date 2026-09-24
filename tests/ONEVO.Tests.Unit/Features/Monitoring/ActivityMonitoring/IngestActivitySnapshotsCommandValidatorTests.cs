using FluentAssertions;
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public class IngestActivitySnapshotsCommandValidatorTests
{
    private readonly IngestActivitySnapshotsCommandValidator _sut = new();

    private static ActivitySnapshotItem ValidItem(DateTimeOffset? capturedAt = null) => new()
    {
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow,
        KeyboardEventsCount = 10,
        MouseEventsCount = 20,
        ActiveSeconds = 60,
        IdleSeconds = 0,
        IntensityScore = 50,
        ForegroundProcessName = "code.exe"
    };

    [Fact]
    public void Valid_batch_passes()
    {
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [ValidItem()] };
        var result = _sut.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_batch_fails()
    {
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [] };
        var result = _sut.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Snapshots);
    }

    [Fact]
    public void Batch_over_200_fails()
    {
        var items = Enumerable.Range(0, 201).Select(_ => ValidItem()).ToList();
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = items };
        var result = _sut.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Snapshots);
    }

    [Fact]
    public void Keyboard_events_out_of_range_fails()
    {
        var item = ValidItem() with { KeyboardEventsCount = 100_001 };
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [item] };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Active_plus_idle_over_300_fails()
    {
        var item = ValidItem() with { ActiveSeconds = 200, IdleSeconds = 200 };
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [item] };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Intensity_out_of_range_fails()
    {
        var item = ValidItem() with { IntensityScore = 101 };
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [item] };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Process_name_with_path_separator_fails()
    {
        var item = ValidItem() with { ForegroundProcessName = @"C:\Windows\code.exe" };
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [item] };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Process_name_too_long_fails()
    {
        var item = ValidItem() with { ForegroundProcessName = new string('a', 101) };
        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [item] };
        var result = _sut.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
    }
}
