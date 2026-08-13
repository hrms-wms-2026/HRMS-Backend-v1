using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public class CompleteEnrollmentAttemptCommandHandler
    : IRequestHandler<CompleteEnrollmentAttemptCommand, Result<BiometricProfileResponseDto>>
{
    private const string ConsentVersion = "v1";

    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteEnrollmentAttemptCommandHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IBiometricVerificationProvider provider,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _provider = provider;
        _fileStorage = fileStorage;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BiometricProfileResponseDto>> Handle(
        CompleteEnrollmentAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<BiometricProfileResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<BiometricProfileResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var attempt = await _repository.FindAttemptAsync(request.AttemptId, _device.TenantId, cancellationToken);
        if (attempt is null)
            return Result<BiometricProfileResponseDto>.NotFound("Enrollment attempt not found.");

        if (attempt.UserId != _device.UserId)
            return Result<BiometricProfileResponseDto>.Forbidden();

        if (string.IsNullOrEmpty(attempt.AwsSessionId))
            return Result<BiometricProfileResponseDto>.Failure("Attempt has no AWS session.", 409);

        var sessionResult = await _provider.GetLivenessSessionResultAsync(attempt.AwsSessionId, cancellationToken);

        var now = _clock.UtcNow;

        switch (sessionResult.Status)
        {
            case "SUCCEEDED":
                break;

            case "CREATED" or "IN_PROGRESS":
                return Result<BiometricProfileResponseDto>.Failure(
                    "Liveness session has not finished yet.", 409);

            case "FAILED":
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.Rejected, out _);
                attempt.FailureCode = "liveness_failed";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.UnprocessableEntity("Liveness check failed.");

            case "EXPIRED":
                attempt.TryTransition(BiometricAttemptStatus.Expired, out _);
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.Failure("Liveness session expired.", 410);

            default:
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
                attempt.FailureCode = "unexpected_provider_status";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.Failure("Unexpected provider response.", 502);
        }

        if (sessionResult.ReferenceImageBytes is null || sessionResult.ReferenceImageBytes.Length == 0)
        {
            attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
            attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
            attempt.FailureCode = "missing_reference_image";
            attempt.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<BiometricProfileResponseDto>.Failure("Provider returned no reference image.", 502);
        }

        using var referenceStream = new MemoryStream(sessionResult.ReferenceImageBytes);
        var uploadResult = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            "enrollment-reference.jpg",
            "image/jpeg",
            UploadPurposeCatalog.MonitoringFaceLiveness,
            referenceStream,
            cancellationToken);

        if (!uploadResult.IsSuccess)
            return Result<BiometricProfileResponseDto>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 500);

        await _repository.SupersedeActiveProfileAsync(attempt.EmployeeId, _device.TenantId, now, cancellationToken);

        var profile = new EmployeeBiometricProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = attempt.EmployeeId,
            UserId = _device.UserId,
            Provider = "aws_rekognition",
            Region = attempt.AwsRegion,
            ReferenceStorageKey = uploadResult.Value!.StorageKey,
            Status = BiometricProfileStatus.Active,
            ConsentVersion = ConsentVersion,
            ConsentAcceptedAt = now,
            EnrollmentAttemptId = attempt.Id,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            CreatedAt = now
        };

        await _repository.AddProfileAsync(profile, cancellationToken);

        attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
        attempt.TryTransition(BiometricAttemptStatus.Verified, out _);
        attempt.LivenessConfidence = sessionResult.Confidence;
        attempt.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<BiometricProfileResponseDto>.Success(new BiometricProfileResponseDto(
            profile.Id, profile.Status, profile.CreatedAt));
    }
}
