using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Commands.IngestAppUsageSnapshots;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class IngestAppUsageSnapshotsCommandHandlerTests
{
    private readonly Mock<IAppUsageSnapshotRepository> _snapshots = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestAppUsageSnapshotsCommandHandlerTests()
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
                _tenantId, _userId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestAppUsageSnapshotsCommandHandler CreateSut() => new(
        _snapshots.Object,
        _toggles.Object,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _clock,
        _uow,
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
        saved!.First().EmployeeId.Should().Be(_userId);
        saved.First().ProcessName.Should().Be("code.exe");
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
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestAppUsageSnapshotsCommand { Snapshots = [Item(_clock.UtcNow)] },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
