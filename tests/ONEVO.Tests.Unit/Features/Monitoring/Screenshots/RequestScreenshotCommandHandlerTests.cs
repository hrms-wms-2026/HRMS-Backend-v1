using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.Commands.RequestScreenshot;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Tests.Unit.Fakes;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

public class RequestScreenshotCommandHandlerTests
{
    private readonly Mock<IAgentCommandRepository> _commands = new();
    private readonly Mock<ITrayActivationRepository> _trayRepo = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly Mock<IMonitoringPermissionConsentRepository> _consents = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeUnitOfWork _uow = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    // Distinct from _userId so tests prove consent is looked up by the resolved Employee.Id.
    private readonly Guid _employeeId = Guid.NewGuid();

    private readonly TrayDeviceRegistration _activeDevice;

    public RequestScreenshotCommandHandlerTests()
    {
        _activeDevice = new TrayDeviceRegistration
        {
            Id = _deviceId,
            TenantId = _tenantId,
            UserId = _userId,
            LegalEntityId = _legalEntityId,
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _currentUser.Setup(u => u.TenantId).Returns(_tenantId);
        _currentUser.Setup(u => u.UserId).Returns(_userId);

        // Same key the tray's consent screen writes under: (tenant, user, device legal entity).
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Consent(screenMonitoring: true));
    }

    private MonitoringPermissionConsent Consent(bool screenMonitoring) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        EmployeeId = _employeeId,
        DeviceRegistrationId = _deviceId,
        AcceptedAt = DateTimeOffset.UtcNow.AddDays(-1),
        ScreenMonitoring = screenMonitoring,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private void SetupActiveDeviceWithScreenshotsEnabled()
    {
        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_activeDevice);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RequestScreenshotCommandHandler CreateHandler() => new(
        _commands.Object,
        _trayRepo.Object,
        _toggles.Object,
        _currentUser.Object,
        _employeeIdentity.Object,
        _consents.Object,
        _clock,
        _uow,
        NullLogger<RequestScreenshotCommandHandler>.Instance);

    [Fact]
    public async Task Handle_DeviceNotFound_Returns404()
    {
        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrayDeviceRegistration?)null);

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, null), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ScreenshotToggleDisabled_Returns403()
    {
        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_activeDevice);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, null), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesCommandWithPendingStatus()
    {
        _trayRepo.Setup(r => r.FindActiveDeviceAsync(_deviceId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_activeDevice);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.ScreenshotCapture, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        AgentCommand? savedCommand = null;
        _commands.Setup(r => r.Add(It.IsAny<AgentCommand>()))
            .Callback<AgentCommand>(c => savedCommand = c);

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, "test note"), default);

        result.IsSuccess.Should().BeTrue();
        savedCommand.Should().NotBeNull();
        savedCommand!.Status.Should().Be("pending");
        savedCommand.CommandType.Should().Be("capture_screenshot");
        savedCommand.TenantId.Should().Be(_tenantId);
        savedCommand.AgentDeviceId.Should().Be(_deviceId);
        savedCommand.RequestedById.Should().Be(_userId);
        savedCommand.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), precision: TimeSpan.FromSeconds(5));
        _uow.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoConsentOnFile_Returns403_AndCreatesNoCommand()
    {
        SetupActiveDeviceWithScreenshotsEnabled();
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoringPermissionConsent?)null);

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, null), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(MonitoringErrors.ScreenshotConsentMissingCode);
        _commands.Verify(r => r.Add(It.IsAny<AgentCommand>()), Times.Never);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ScreenMonitoringDeclined_Returns403_AndCreatesNoCommand()
    {
        SetupActiveDeviceWithScreenshotsEnabled();
        _consents.Setup(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Consent(screenMonitoring: false));

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, null), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(MonitoringErrors.ScreenshotConsentMissingCode);
        _commands.Verify(r => r.Add(It.IsAny<AgentCommand>()), Times.Never);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_LooksUpConsentByEmployeeResolvedFromDeviceLegalEntity()
    {
        SetupActiveDeviceWithScreenshotsEnabled();

        var result = await CreateHandler().Handle(new RequestScreenshotCommand(_deviceId, null), default);

        result.IsSuccess.Should().BeTrue();
        _employeeIdentity.Verify(r => r.ResolveEmployeeIdAsync(
            _tenantId, _userId, _legalEntityId, It.IsAny<CancellationToken>()), Times.Once);
        _consents.Verify(c => c.GetLatestAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
