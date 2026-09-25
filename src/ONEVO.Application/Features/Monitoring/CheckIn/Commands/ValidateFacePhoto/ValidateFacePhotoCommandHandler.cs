using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;

public class ValidateFacePhotoCommandHandler
    : IRequestHandler<ValidateFacePhotoCommand, Result<FacePhotoValidationResponseDto>>
{
    public const string FailurePoorLighting = "poor_lighting";
    public const string FailureFaceNotVisible = "face_not_visible";
    public const string FailureNoFaceDetected = "no_face_detected";
    public const string FailureMultipleFaces = "multiple_faces";
    public const string FailureSunglassesOrMask = "sunglasses_or_mask";
    public const string FailureNotMatched = "not_matched";
    public const string FailureNoReferencePhoto = "no_reference_photo";
    public const string FailureVerificationFailed = "verification_failed";

    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IFileStorageService _fileStorage;
    private readonly IBiometricProfileRepository _profiles;
    private readonly IFaceQualityService _faceQuality;
    private readonly IFaceMatchService _faceMatch;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;

    public ValidateFacePhotoCommandHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IFileStorageService fileStorage,
        IBiometricProfileRepository profiles,
        IFaceQualityService faceQuality,
        IFaceMatchService faceMatch,
        ITrayEmployeeIdentityResolver employeeIdentity)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _fileStorage = fileStorage;
        _profiles = profiles;
        _faceQuality = faceQuality;
        _faceMatch = faceMatch;
        _employeeIdentity = employeeIdentity;
    }

    public async Task<Result<FacePhotoValidationResponseDto>> Handle(
        ValidateFacePhotoCommand request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<FacePhotoValidationResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<FacePhotoValidationResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        await using var captured = new MemoryStream();
        await request.ImageStream.CopyToAsync(captured, cancellationToken);
        captured.Position = 0;

        FaceQualityOutcome quality;
        try
        {
            quality = await _faceQuality.AnalyzeAsync(captured, cancellationToken);
        }
        catch (Exception)
        {
            return Result<FacePhotoValidationResponseDto>.Success(FailedVerification());
        }

        if (!quality.LightingOk || !quality.FaceVisible || !quality.NoSunglassesOrMask)
        {
            return Result<FacePhotoValidationResponseDto>.Success(new FacePhotoValidationResponseDto(
                quality.LightingOk,
                quality.FaceVisible,
                quality.NoSunglassesOrMask,
                IsMatch: false,
                CanProceed: false,
                SimilarityScore: null,
                FailureReason: FirstQualityFailure(quality)));
        }

        captured.Position = 0;

        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            _device.TenantId, _device.UserId, _device.LegalEntityId, cancellationToken);
        var profile = await _profiles.GetByEmployeeIdAsync(_device.TenantId, employeeId, cancellationToken);
        if (profile?.ReferencePhotoFileId is null)
        {
            // Only face setup may create the reference. Letting clock-in do it would make
            // whoever sits at the laptop first the "enrolled" face for this employee.
            if (!FacePhotoValidationPurpose.IsEnrollment(request.Purpose))
            {
                return Result<FacePhotoValidationResponseDto>.Success(new FacePhotoValidationResponseDto(
                    quality.LightingOk,
                    quality.FaceVisible,
                    quality.NoSunglassesOrMask,
                    IsMatch: false,
                    CanProceed: false,
                    SimilarityScore: null,
                    FailureReason: FailureNoReferencePhoto));
            }

            var enrolled = await EnrollReferenceFromCaptureAsync(
                captured, request.ContentType, employeeId, profile, cancellationToken);

            return Result<FacePhotoValidationResponseDto>.Success(new FacePhotoValidationResponseDto(
                quality.LightingOk,
                quality.FaceVisible,
                quality.NoSunglassesOrMask,
                IsMatch: true,
                CanProceed: true,
                SimilarityScore: enrolled ? 100f : null,
                FailureReason: null));
        }

        try
        {
            var referenceRead = await _fileStorage.OpenReadAsync(
                _device.TenantId, profile.ReferencePhotoFileId.Value, cancellationToken);
            if (!referenceRead.IsSuccess)
                return Result<FacePhotoValidationResponseDto>.Success(FailedVerification(quality));

            await using var referenceStream = referenceRead.Value!.Content;
            var outcome = await _faceMatch.CompareAsync(referenceStream, captured, cancellationToken);

            return Result<FacePhotoValidationResponseDto>.Success(new FacePhotoValidationResponseDto(
                quality.LightingOk,
                quality.FaceVisible,
                quality.NoSunglassesOrMask,
                outcome.IsMatch,
                CanProceed: outcome.IsMatch,
                outcome.Similarity,
                FailureReason: outcome.IsMatch ? null : FailureNotMatched));
        }
        catch (Exception)
        {
            return Result<FacePhotoValidationResponseDto>.Success(FailedVerification(quality));
        }
    }

    private async Task<bool> EnrollReferenceFromCaptureAsync(
        MemoryStream captured,
        string contentType,
        Guid employeeId,
        BiometricProfile? existing,
        CancellationToken ct)
    {
        captured.Position = 0;
        var upload = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            "clock-in-reference.jpg",
            string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
            UploadPurposeCatalog.BiometricReferencePhoto,
            captured,
            ct);
        if (!upload.IsSuccess)
            return false;

        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.Status = BiometricProfileStatus.Enrolled;
            existing.EnrolledAt = now;
            existing.UpdatedAt = now;
            existing.ReferencePhotoFileId = upload.Value!.Id;
            _profiles.Update(existing);
        }
        else
        {
            await _profiles.AddAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(),
                TenantId = _device.TenantId,
                EmployeeId = employeeId,
                Status = BiometricProfileStatus.Enrolled,
                EnrolledAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ReferencePhotoFileId = upload.Value!.Id
            }, ct);
        }

        await _profiles.SaveChangesAsync(ct);
        return true;
    }

    private static FacePhotoValidationResponseDto FailedVerification(FaceQualityOutcome? quality = null) =>
        new(
            quality?.LightingOk ?? false,
            quality?.FaceVisible ?? false,
            quality?.NoSunglassesOrMask ?? false,
            IsMatch: false,
            CanProceed: false,
            SimilarityScore: null,
            FailureReason: FailureVerificationFailed);

    private static string FirstQualityFailure(FaceQualityOutcome quality)
    {
        if (quality.FaceCount == 0) return FailureNoFaceDetected;
        if (quality.FaceCount > 1) return FailureMultipleFaces;
        if (!quality.FaceVisible) return FailureFaceNotVisible;
        if (!quality.LightingOk) return FailurePoorLighting;
        return FailureSunglassesOrMask;
    }
}
