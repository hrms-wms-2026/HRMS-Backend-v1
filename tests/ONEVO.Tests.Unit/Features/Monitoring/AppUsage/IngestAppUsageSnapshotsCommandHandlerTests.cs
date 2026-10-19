using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Tests.Unit.Fakes;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class IngestAppUsageSnapshotsCommandHandlerTests
{
    private readonly Mock<IAppUsageSnapshotRepository> _snapshots = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Mock<IGlobalApplicationRepository> _globalApplications = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestAppUsageSnapshotsCommandHandlerTests()
    {
        // Default: nothing from this batch was stored before. Replay tests override this.
        _snapshots.Setup(s => s.GetExistingCapturedAtsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<DateTimeOffset>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlySet<DateTimeOffset>)new HashSet<DateTimeOffset>());

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
                _tenantId, _userId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private IngestAppUsageSnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _employeeIdentity.Object,
        _clock,
        _uow,
        _globalApplications.Object,
        NullLogger<IngestAppUsageSnapshotsCommandHandler>.Instance);

    private static AppUsageSnapshotItem Item(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        ProcessName = "code.exe",
        WindowTitleHash = "abc123"
    };

    [Fact]
    public async Task Happy_path_saves_snapshots()
    {
        IEnumerable<AppUsageSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<AppUsageSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppUsageSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().EmployeeId.Should().Be(_employeeId);
        saved.First().ProcessName.Should().Be("code.exe");
        saved.First().TenantId.Should().Be(_tenantId);
        saved.First().AgentDeviceId.Should().Be(_deviceId);
        _globalApplications.Verify(x => x.RecordObservedAsync(
            It.Is<IEnumerable<string>>(names => names.Single() == "code.exe"),
            _clock.UtcNow,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.AppTrackingDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Future_timestamp_returns_400()
    {
        var cmd = new IngestAppUsageSnapshotsCommand
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
        IEnumerable<AppUsageSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<AppUsageSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppUsageSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var fresh1 = _clock.UtcNow.AddMinutes(-10);
        var fresh2 = _clock.UtcNow.AddHours(-23);
        var cmd = new IngestAppUsageSnapshotsCommand
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
        var cmd = new IngestAppUsageSnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddHours(-25))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotTooOld);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
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

        var result = await CreateSut().Handle(new IngestAppUsageSnapshotsCommand { Snapshots = [Item(capturedAt)] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Stored.Should().Be(0);
        result.Value.Duplicates.Should().Be(1);
        _snapshots.Verify(s => s.AddRangeAsync(It.IsAny<IEnumerable<AppUsageSnapshot>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Same_item_twice_in_one_batch_is_stored_once()
    {
        IEnumerable<AppUsageSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<AppUsageSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppUsageSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);
        var at = _clock.UtcNow.AddMinutes(-1);

        var result = await CreateSut().Handle(new IngestAppUsageSnapshotsCommand { Snapshots = [Item(at), Item(at)] }, CancellationToken.None);

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
            new IngestAppUsageSnapshotsCommand { Snapshots = [Item(stale), Item(_clock.UtcNow.AddMinutes(-1)), Item(future)] },
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
            new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddHours(-30))] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(MonitoringErrors.SnapshotTooOldCode);
    }
}
