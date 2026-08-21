using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CreateEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricEnrollmentAttemptRepository> _attempts = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<IFaceLivenessService> _liveness = new();
    private readonly FakeDateTimeProvider _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public CreateEnrollmentAttemptCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.Biometric, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _liveness.Setup(l => l.CreateSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessSession("aws-session-123", "us-east-1"));
        _liveness.Setup(l => l.AssumeLivenessRoleAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopedAwsCredentials("AKIA...", "secret", "token", _clock.UtcNow.AddMinutes(15)));
    }

    private CreateEnrollmentAttemptCommandHandler CreateSut() =>
        new(_attempts.Object, _toggles.Object, _device.Object, _liveness.Object, _clock);

    [Fact]
    public async Task Happy_path_createsSessionAndPersistsPendingAttempt()
    {
        BiometricEnrollmentAttempt? saved = null;
        _attempts.Setup(a => a.AddAsync(It.IsAny<BiometricEnrollmentAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<BiometricEnrollmentAttempt, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(new CreateEnrollmentAttemptCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AwsSessionId.Should().Be("aws-session-123");
        result.Value.AccessKeyId.Should().Be("AKIA...");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(BiometricEnrollmentStatus.Pending);
        saved.TenantId.Should().Be(_tenantId);
        saved.EmployeeId.Should().Be(_userId);
        _attempts.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Monitoring_disabled_returns_403()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _userId, MonitoringCapability.Biometric, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateSut().Handle(new CreateEnrollmentAttemptCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(MonitoringErrors.BiometricDisabled);
        _liveness.Verify(l => l.CreateSessionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new CreateEnrollmentAttemptCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
