using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public class IngestMeetingSignalsCommandHandlerTests
{
    private readonly Mock<IMeetingSignalRepository> _signals = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestMeetingSignalsCommandHandlerTests()
    {
        // Default: nothing from this batch was stored before. Replay tests override this.
        _signals.Setup(s => s.GetExistingCapturedAtsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset>());

        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.MeetingDetection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private IngestMeetingSignalsCommandHandler CreateSut() => new(
        _signals.Object, _toggles.Object, _device.Object, _tenants.Object, _switcher.Object,
        _employeeIdentity.Object, _clock, _uow, NullLogger<IngestMeetingSignalsCommandHandler>.Instance);

    private MeetingSignalItem Item(DateTimeOffset capturedAt, bool isRunning = true) => new()
    {
        CapturedAt = capturedAt, IsMeetingAppRunning = isRunning, ProcessName = "teams.exe"
    };

    [Fact]
    public async Task Happy_path_saves_signals()
    {
        IEnumerable<MeetingSignal>? saved = null;
        _signals.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<MeetingSignal>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MeetingSignal>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow.AddMinutes(-1))] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().ProcessName.Should().Be("teams.exe");
        saved.First().EmployeeId.Should().Be(_employeeId);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.MeetingDetection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow)] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.MeetingDetectionDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Mixed_batch_stores_in_window_signals_and_skips_stale_ones()
    {
        IEnumerable<MeetingSignal>? saved = null;
        _signals.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<MeetingSignal>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MeetingSignal>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var fresh = _clock.UtcNow.AddMinutes(-1);
        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow.AddHours(-25)), Item(fresh)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.Select(x => x.CapturedAt).Should().BeEquivalentTo([SnapshotTimeWindow.Normalize(fresh)]);
    }

    [Fact]
    public async Task All_stale_batch_returns_400()
    {
        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow.AddHours(-25))] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotTooOld);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow)] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    // AGT-01: the tray retries a batch whose 202 was lost. Its captured_at carries 100ns ticks while
    // PostgreSQL returned the stored microsecond value — the replay must still be recognised.
    [Fact]
    public async Task Replay_after_lost_response_is_acknowledged_as_duplicate_not_reinserted()
    {
        var capturedAt = new DateTimeOffset(_clock.UtcNow.AddMinutes(-2).UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
        _signals.Setup(s => s.GetExistingCapturedAtsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset> { SnapshotTimeWindow.Normalize(capturedAt) });

        var result = await CreateSut().Handle(new IngestMeetingSignalsCommand { Signals = [Item(capturedAt)] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Stored.Should().Be(0);
        result.Value.Duplicates.Should().Be(1);
        _signals.Verify(s => s.AddRangeAsync(It.IsAny<IEnumerable<MeetingSignal>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Same_item_twice_in_one_batch_is_stored_once()
    {
        IEnumerable<MeetingSignal>? saved = null;
        _signals.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<MeetingSignal>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MeetingSignal>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);
        var at = _clock.UtcNow.AddMinutes(-1);

        var result = await CreateSut().Handle(new IngestMeetingSignalsCommand { Signals = [Item(at), Item(at)] }, CancellationToken.None);

        result.Value!.Stored.Should().Be(1);
        result.Value.Duplicates.Should().Be(1);
        saved.Should().ContainSingle();
    }

    [Fact]
    public async Task Mixed_batch_reports_each_rejected_item_by_captured_at_and_code()
    {
        var stale = _clock.UtcNow.AddHours(-25);
        var future = _clock.UtcNow.AddMinutes(30);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(stale), Item(_clock.UtcNow.AddMinutes(-1)), Item(future)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Stored.Should().Be(1);
        result.Value.Rejected.Should().BeEquivalentTo(new[]
        {
            new SnapshotRejection(SnapshotTimeWindow.Normalize(stale), MonitoringErrors.SnapshotTooOldCode),
            new SnapshotRejection(SnapshotTimeWindow.Normalize(future), MonitoringErrors.SnapshotFutureTimeCode)
        });
    }

    [Fact]
    public async Task All_out_of_window_batch_fails_with_machine_readable_code()
    {
        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow.AddHours(-30))] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(MonitoringErrors.SnapshotTooOldCode);
    }
}
