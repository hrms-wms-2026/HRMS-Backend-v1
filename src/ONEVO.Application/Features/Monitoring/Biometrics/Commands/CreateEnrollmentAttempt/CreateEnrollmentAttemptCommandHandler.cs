using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

public class CreateEnrollmentAttemptCommandHandler
    : IRequestHandler<CreateEnrollmentAttemptCommand, Result<EnrollmentAttemptResponseDto>>
{
    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IEmployeeIdentityResolver _employeeResolver;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEnrollmentAttemptCommandHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IEmployeeIdentityResolver employeeResolver,
        IBiometricVerificationProvider provider,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeResolver = employeeResolver;
        _provider = provider;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<EnrollmentAttemptResponseDto>> Handle(
        CreateEnrollmentAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<EnrollmentAttemptResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<EnrollmentAttemptResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var employeeId = await _employeeResolver.ResolveEmployeeIdAsync(
            _device.UserId, _device.TenantId, cancellationToken);
        if (employeeId is null)
        {
            return Result<EnrollmentAttemptResponseDto>.UnprocessableEntity(
                "No HR employee profile is linked to this account yet.");
        }

        var session = await _provider.CreateLivenessSessionAsync(
            new CreateLivenessSessionRequest("FaceMovementAndLightChallenge", KmsKeyId: string.Empty),
            cancellationToken);

        var credentials = await _provider.IssueScopedCaptureCredentialsAsync(
            session.AwsSessionId, cancellationToken);

        var now = _clock.UtcNow;
        var attempt = new BiometricVerificationAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = employeeId.Value,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Purpose = BiometricAttemptPurpose.Enrollment,
            AwsSessionId = session.AwsSessionId,
            AwsRegion = credentials.Region,
            ChallengeType = "FaceMovementAndLightChallenge",
            AwsSessionExpiresAt = credentials.Expiration,
            Status = BiometricAttemptStatus.Created,
            CreatedAt = now
        };

        await _repository.AddAttemptAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<EnrollmentAttemptResponseDto>.Success(new EnrollmentAttemptResponseDto(
            attempt.Id,
            session.AwsSessionId,
            credentials.Region,
            attempt.ChallengeType,
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.SessionToken,
            credentials.Expiration));
    }
}
