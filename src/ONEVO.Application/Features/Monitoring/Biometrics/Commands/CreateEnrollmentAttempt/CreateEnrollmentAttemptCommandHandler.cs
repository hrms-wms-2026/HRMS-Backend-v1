using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

public class CreateEnrollmentAttemptCommandHandler
    : IRequestHandler<CreateEnrollmentAttemptCommand, Result<EnrollmentAttemptResponse>>
{
    private const string ChallengeType = "FaceMovementAndLightChallenge";

    private readonly IBiometricEnrollmentAttemptRepository _attempts;
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly IFaceLivenessService _liveness;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly IDateTimeProvider _clock;

    public CreateEnrollmentAttemptCommandHandler(
        IBiometricEnrollmentAttemptRepository attempts,
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        IFaceLivenessService liveness,
        ITrayEmployeeIdentityResolver employeeIdentity,
        IDateTimeProvider clock)
    {
        _attempts = attempts;
        _toggleResolver = toggleResolver;
        _device = device;
        _liveness = liveness;
        _employeeIdentity = employeeIdentity;
        _clock = clock;
    }

    public async Task<Result<EnrollmentAttemptResponse>> Handle(
        CreateEnrollmentAttemptCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<EnrollmentAttemptResponse>.Failure("A valid tray device token is required.", 401);
        }

        var tenantId = _device.TenantId;
        var userId = _device.UserId;

        var enabled = await _toggleResolver.IsEnabledAsync(tenantId, userId, MonitoringCapability.Biometric, ct);
        if (!enabled)
            return Result<EnrollmentAttemptResponse>.Failure(MonitoringErrors.BiometricDisabled, 403);

        // Resolves the real CoreHR Employee.Id to store, falling back to the raw UserId when no
        // Employee row exists yet - see ITrayEmployeeIdentityResolver's own doc comment.
        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            tenantId, userId, _device.LegalEntityId, ct);

        var session = await _liveness.CreateSessionAsync(ct);
        var credentials = await _liveness.AssumeLivenessRoleAsync(session.SessionId, ct);
        var now = _clock.UtcNow;

        var attempt = new BiometricEnrollmentAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            AgentDeviceId = _device.DeviceRegistrationId,
            AwsSessionId = session.SessionId,
            Region = session.Region,
            ChallengeType = ChallengeType,
            Status = BiometricEnrollmentStatus.Pending,
            CreatedAt = now
        };

        await _attempts.AddAsync(attempt, ct);
        await _attempts.SaveChangesAsync(ct);

        return Result<EnrollmentAttemptResponse>.Success(new EnrollmentAttemptResponse(
            attempt.Id, session.SessionId, session.Region, ChallengeType,
            credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, credentials.ExpiresAt));
    }
}
