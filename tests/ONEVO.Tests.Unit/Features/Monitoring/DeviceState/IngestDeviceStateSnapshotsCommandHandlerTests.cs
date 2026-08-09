using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.Commands.IngestDeviceStateSnapshots;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class IngestDeviceStateSnapshotsCommandHandlerTests
{
    private readonly Mock<IDeviceStateSnapshotRepository> _snapshots = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestDeviceStateSnapshotsCommandHandlerTests()
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
                _tenantId, _userId, MonitoringCapability.DeviceTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestDeviceStateSnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _clock,
        _uow,
        NullLogger<IngestDeviceStateSnapshotsCommandHandler>.Instance);

    private static DeviceStateSnapshotItem Item(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        IdleSeconds = 15,
        IsIdle = false
    };

    [Fact]
    public async Task Happy_path_saves_snapshots()
    {
        IEnumerable<DeviceStateSnapshot>? saved = null;
        _snapshots.Setup(s => s.AddRangeAsync(It.IsAny<IEnumerable<DeviceStateSnapshot>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DeviceStateSnapshot>, CancellationToken>((list, _) => saved = list.ToList())
            .Returns(Task.CompletedTask);

        var cmd = new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow.AddMinutes(-1))] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _uow.SaveCallCount.Should().Be(1);
        saved.Should().NotBeNull().And.HaveCount(1);
        saved!.First().EmployeeId.Should().Be(_userId);
        saved.First().TenantId.Should().Be(_tenantId);
        saved.First().AgentDeviceId.Should().Be(_deviceId);
        saved.First().IdleSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.DeviceTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var cmd = new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.DeviceTrackingDisabled);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestDeviceStateSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Future_timestamp_returns_400()
    {
        var cmd = new IngestDeviceStateSnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddHours(1))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotFutureTime);
    }

    [Fact]
    public async Task Snapshot_older_than_24h_returns_400()
    {
        var cmd = new IngestDeviceStateSnapshotsCommand
        {
            Snapshots = [Item(_clock.UtcNow.AddHours(-25))]
        };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be(MonitoringErrors.SnapshotTooOld);
    }
}
