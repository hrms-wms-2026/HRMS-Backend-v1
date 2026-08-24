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

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public class IngestMeetingSignalsCommandHandlerTests
{
    private readonly Mock<IMeetingSignalRepository> _signals = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IngestMeetingSignalsCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Name = "Test", Slug = "test", Status = TenantStatus.Active });

        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, MonitoringCapability.MeetingDetection, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private IngestMeetingSignalsCommandHandler CreateSut() => new(
        _signals.Object, _toggles.Object, _device.Object, _tenants.Object, _switcher.Object,
        _clock, _uow, NullLogger<IngestMeetingSignalsCommandHandler>.Instance);

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
        saved.First().EmployeeId.Should().Be(_userId);
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
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(
            new IngestMeetingSignalsCommand { Signals = [Item(_clock.UtcNow)] }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
