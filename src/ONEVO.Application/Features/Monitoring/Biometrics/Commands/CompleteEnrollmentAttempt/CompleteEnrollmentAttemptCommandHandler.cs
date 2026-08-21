using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Errors;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public class CompleteEnrollmentAttemptCommandHandler
    : IRequestHandler<CompleteEnrollmentAttemptCommand, Result<BiometricProfileResponse>>
{
    private readonly IBiometricEnrollmentAttemptRepository _attempts;
    private readonly IBiometricProfileRepository _profiles;
    private readonly ITrayCurrentDevice _device;
    private readonly IFaceLivenessService _liveness;
    private readonly IDateTimeProvider _clock;
    private readonly BiometricEnrollmentOptions _options;

    public CompleteEnrollmentAttemptCommandHandler(
        IBiometricEnrollmentAttemptRepository attempts,
        IBiometricProfileRepository profiles,
        ITrayCurrentDevice device,
        IFaceLivenessService liveness,
        IDateTimeProvider clock,
        IOptions<BiometricEnrollmentOptions> options)
    {
        _attempts = attempts;
        _profiles = profiles;
        _device = device;
        _liveness = liveness;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<BiometricProfileResponse>> Handle(
        CompleteEnrollmentAttemptCommand request, CancellationToken ct)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.UserId == Guid.Empty)
            return Result<BiometricProfileResponse>.Failure("A valid tray device token is required.", 401);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var now = _clock.UtcNow;

        var attempt = await _attempts.GetByIdAsync(tenantId, employeeId, request.AttemptId, ct);
        if (attempt is null)
            return Result<BiometricProfileResponse>.NotFound(MonitoringErrors.EnrollmentAttemptNotFound);

        if (attempt.Status != BiometricEnrollmentStatus.Pending)
            return Result<BiometricProfileResponse>.Conflict(MonitoringErrors.EnrollmentAttemptAlreadySettled);

        if (now - attempt.CreatedAt > TimeSpan.FromMinutes(_options.SessionTtlMinutes))
        {
            attempt.Status = BiometricEnrollmentStatus.Expired;
            attempt.CompletedAt = now;
            _attempts.Update(attempt);
            await _attempts.SaveChangesAsync(ct);
            return Result<BiometricProfileResponse>.Failure(MonitoringErrors.EnrollmentAttemptExpired, 410);
        }

        var outcome = await _liveness.GetSessionResultAsync(attempt.AwsSessionId, ct);
        attempt.Confidence = outcome.Confidence;
        attempt.CompletedAt = now;

        if (!string.Equals(outcome.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
            || outcome.Confidence < _options.LivenessConfidenceThreshold)
        {
            attempt.Status = BiometricEnrollmentStatus.Failed;
            attempt.FailureReason = $"status={outcome.Status} confidence={outcome.Confidence}";
            _attempts.Update(attempt);
            await _attempts.SaveChangesAsync(ct);
            return Result<BiometricProfileResponse>.UnprocessableEntity(MonitoringErrors.LivenessCheckFailed);
        }

        attempt.Status = BiometricEnrollmentStatus.Succeeded;
        _attempts.Update(attempt);

        var existingProfile = await _profiles.GetByEmployeeIdAsync(tenantId, employeeId, ct);
        BiometricProfile profile;
        if (existingProfile is not null)
        {
            existingProfile.Status = BiometricProfileStatus.Enrolled;
            existingProfile.EnrolledAt = now;
            existingProfile.UpdatedAt = now;
            _profiles.Update(existingProfile);
            profile = existingProfile;
        }
        else
        {
            profile = new BiometricProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                Status = BiometricProfileStatus.Enrolled,
                EnrolledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _profiles.AddAsync(profile, ct);
        }

        await _attempts.SaveChangesAsync(ct);
        await _profiles.SaveChangesAsync(ct);

        return Result<BiometricProfileResponse>.Success(
            new BiometricProfileResponse(profile.Id, profile.Status.ToString(), profile.EnrolledAt));
    }
}
