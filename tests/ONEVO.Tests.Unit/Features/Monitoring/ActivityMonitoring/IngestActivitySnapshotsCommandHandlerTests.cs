using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;
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
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
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
    }

    private IngestActivitySnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _rawBuffer.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
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
        savedSnapshots!.First().EmployeeId.Should().Be(_userId);
        savedSnapshots.First().KeyboardEventsCount.Should().Be(5);
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
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestActivitySnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
