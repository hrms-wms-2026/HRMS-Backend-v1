using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CompleteEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricEnrollmentAttemptRepository> _attempts = new();
    private readonly Mock<IBiometricProfileRepository> _profiles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<IFaceLivenessService> _liveness = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly BiometricEnrollmentOptions _options = new() { LivenessConfidenceThreshold = 90f, SessionTtlMinutes = 3 };

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();

    public CompleteEnrollmentAttemptCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
    }

    private CompleteEnrollmentAttemptCommandHandler CreateSut() => new(
        _attempts.Object, _profiles.Object, _device.Object, _liveness.Object, _clock, Options.Create(_options));

    private BiometricEnrollmentAttempt PendingAttempt(DateTimeOffset createdAt) => new()
    {
        Id = _attemptId, TenantId = _tenantId, EmployeeId = _userId, AgentDeviceId = Guid.NewGuid(),
        AwsSessionId = "aws-session-123", Region = "us-east-1", ChallengeType = "FaceMovementAndLightChallenge",
        Status = BiometricEnrollmentStatus.Pending, CreatedAt = createdAt
    };

    [Fact]
    public async Task HighConfidenceSuccess_CreatesProfileAndMarksAttemptSucceeded()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        _liveness.Setup(l => l.GetSessionResultAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f));
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Enrolled");
        attempt.Status.Should().Be(BiometricEnrollmentStatus.Succeeded);
        attempt.Confidence.Should().Be(97.5f);
        _profiles.Verify(p => p.AddAsync(It.Is<BiometricProfile>(bp => bp.Status == BiometricProfileStatus.Enrolled), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LowConfidence_MarksAttemptFailed_ReturnsUnprocessableEntity()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        _liveness.Setup(l => l.GetSessionResultAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 42f));

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        attempt.Status.Should().Be(BiometricEnrollmentStatus.Failed);
        _profiles.Verify(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpiredAttempt_ReturnsFailure_WithoutCallingAws()
    {
        var attempt = PendingAttempt(_clock.UtcNow.AddMinutes(-10));
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        attempt.Status.Should().Be(BiometricEnrollmentStatus.Expired);
        _liveness.Verify(l => l.GetSessionResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttemptNotFound_ReturnsNotFound()
    {
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricEnrollmentAttempt?)null);

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AlreadySettledAttempt_ReturnsConflict_WithoutCallingAwsAgain()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        attempt.Status = BiometricEnrollmentStatus.Succeeded;
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _liveness.Verify(l => l.GetSessionResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
