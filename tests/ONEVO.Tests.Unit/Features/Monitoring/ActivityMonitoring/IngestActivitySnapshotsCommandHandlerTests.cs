using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public class IngestActivitySnapshotsCommandHandlerTests
{
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();
    private readonly Mock<IActivityRawBufferRepository> _rawBuffer = new();
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

    public IngestActivitySnapshotsCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The resolved real Employee.Id is what gets persisted - distinct from the raw UserId so
        // tests can tell whether the handler stored the resolved value or the JWT identity.
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);

        // Default: this device has never reported any of the batch's timestamps before.
        // Duplicate-detection tests override this per-case.
        _snapshots.Setup(s => s.GetExistingCapturedAtsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset>());
    }

    private IngestActivitySnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _rawBuffer.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _employeeIdentity.Object,
        _clock,
        _uow,
        NullLogger<IngestActivitySnapshotsCommandHandler>.Instance);

    private static ActivitySnapshotItem Item(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        KeyboardEventsCount = 5,
        MouseEventsCount = 10,
        ActiveSeconds = 30,
        IdleSeconds = 0,
        IntensityScore = 40,
        ForegroundProcessName = "code.exe"
    };

    [Fact]
    public async Task Happy_path_saves_raw_buffer_and_snapshots()
    {
        ActivityRawBuffer? savedBuffer = null;
        IEnumerable<ActivitySnapshot>? savedSnapshots = null;

        _rawBuffer.Setup(r => r.AddAsync(It.IsAny<ActivityRawBuffer>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityRawBuffer, CancellationToken>((b, _) => savedBuffer = b)
            .Returns(Task.CompletedTask);

        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ActivitySnapshot>, CancellationToken>((list, _) => savedSnapshots = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        savedBuffer.Should().NotBeNull();
        savedBuffer!.TenantId.Should().Be(_tenantId);
        savedBuffer.AgentDeviceId.Should().Be(_deviceId);
        savedBuffer.PayloadJson.Should().NotBeNullOrWhiteSpace();
        savedSnapshots.Should().NotBeNull().And.HaveCount(1);
        savedSnapshots!.First().EmployeeId.Should().Be(_employeeId);
        savedSnapshots.First().KeyboardEventsCount.Should().Be(5);
    }

    [Fact]
    public async Task No_employee_resolved_falls_back_to_UserId()
    {
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_userId);

        IEnumerable<ActivitySnapshot>? savedSnapshots = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ActivitySnapshot>, CancellationToken>((list, _) => savedSnapshots = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))] };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        savedSnapshots!.First().EmployeeId.Should().Be(_userId);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow)]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.ActivityMonitoringDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Future_timestamp_returns_400()
    {
        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddHours(1))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotFutureTime);
    }

    [Fact]
    public async Task Mixed_batch_stores_in_window_items_and_skips_out_of_window_ones()
    {
        // A weekend-offline flush straddling the 24h edge must not lose its fresh items.
        IEnumerable<ActivitySnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ActivitySnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var fresh1 = _clock.UtcNow.AddMinutes(-10);
        var fresh2 = _clock.UtcNow.AddHours(-23);
        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots =
            [
                Item(_clock.UtcNow.AddHours(-25)),
                Item(fresh1),
                Item(_clock.UtcNow.AddHours(1)),
                Item(fresh2)
            ]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull();
        saved!.Select(x => x.CapturedAt).Should().BeEquivalentTo([SnapshotTimeWindow.Normalize(fresh1), SnapshotTimeWindow.Normalize(fresh2)]);
    }

    [Fact]
    public async Task Snapshot_older_than_24h_returns_400()
    {
        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddHours(-25))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotTooOld);
    }

    [Fact]
    public async Task Snapshot_already_captured_by_this_device_is_skipped_not_reinserted()
    {
        var alreadySent = _clock.UtcNow.AddMinutes(-5);
        var genuinelyNew = _clock.UtcNow.AddMinutes(-1);

        _snapshots.Setup(s => s.GetExistingCapturedAtsAsync(
                _tenantId, _deviceId, It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset> { alreadySent });

        IEnumerable<ActivitySnapshot>? savedSnapshots = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ActivitySnapshot>, CancellationToken>((list, _) => savedSnapshots = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestActivitySnapshotsCommand
        {
            Snapshots = [Item(alreadySent), Item(genuinelyNew)]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        savedSnapshots.Should().NotBeNull().And.HaveCount(1);
        savedSnapshots!.First().CapturedAt.Should().Be(SnapshotTimeWindow.Normalize(genuinelyNew));
    }

    [Fact]
    public async Task Whole_batch_already_captured_saves_nothing_and_does_not_call_SaveChanges()
    {
        var alreadySent = _clock.UtcNow.AddMinutes(-1);

        _snapshots.Setup(s => s.GetExistingCapturedAtsAsync(
                _tenantId, _deviceId, It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset> { alreadySent });

        var cmd = new IngestActivitySnapshotsCommand { Snapshots = [Item(alreadySent)] };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _snapshots.Verify(
            s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestActivitySnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    // AGT-01: the tray retries a batch whose 202 was lost. Its captured_at carries 100ns ticks while
    // PostgreSQL returned the stored microsecond value — the replay must still be recognised.
    [Fact]
    public async Task Replay_after_lost_response_is_acknowledged_as_duplicate_not_reinserted()
    {
        var capturedAt = new DateTimeOffset(_clock.UtcNow.AddMinutes(-2).UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
        _snapshots.Setup(s => s.GetExistingCapturedAtsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset> { SnapshotTimeWindow.Normalize(capturedAt) });

        var result = await CreateSut().Handle(new IngestActivitySnapshotsCommand { Snapshots = [Item(capturedAt)] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Stored.Should().Be(0);
        result.Value.Duplicates.Should().Be(1);
        _snapshots.Verify(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Same_item_twice_in_one_batch_is_stored_once()
    {
        IEnumerable<ActivitySnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<ActivitySnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ActivitySnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);
        var at = _clock.UtcNow.AddMinutes(-1);

        var result = await CreateSut().Handle(new IngestActivitySnapshotsCommand { Snapshots = [Item(at), Item(at)] }, CancellationToken.None);

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
            new IngestActivitySnapshotsCommand { Snapshots = [Item(stale), Item(_clock.UtcNow.AddMinutes(-1)), Item(future)] },
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
            new IngestActivitySnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddHours(-30))] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(MonitoringErrors.SnapshotTooOldCode);
    }
}
