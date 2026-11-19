using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.Helpers;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.EnrollFacePhotos;

/// <summary>
/// Saves the tray face setup photos (front, plus optional left/right) as the employee's
/// references, replacing any previously enrolled face. Every check the tray
/// already ran per step is repeated here — the per-step results are never trusted.
/// </summary>
public class EnrollFacePhotosCommandHandler
    : IRequestHandler<EnrollFacePhotosCommand, Result<FaceEnrollmentResponseDto>>
{
    /// <summary>Both side photos were turned the same way.</summary>
    public const string FailureSameSide = "same_side";

    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IFileStorageService _fileStorage;
    private readonly IBiometricProfileRepository _profiles;
    private readonly IFaceQualityService _faceQuality;
    private readonly IFaceMatchService _faceMatch;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;
    private readonly ILogger<EnrollFacePhotosCommandHandler> _logger;

    public EnrollFacePhotosCommandHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IFileStorageService fileStorage,
        IBiometricProfileRepository profiles,
        IFaceQualityService faceQuality,
        IFaceMatchService faceMatch,
        ITrayEmployeeIdentityResolver employeeIdentity,
        ILogger<EnrollFacePhotosCommandHandler>? logger = null)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _fileStorage = fileStorage;
        _profiles = profiles;
        _faceQuality = faceQuality;
        _faceMatch = faceMatch;
        _employeeIdentity = employeeIdentity;
        _logger = logger ?? NullLogger<EnrollFacePhotosCommandHandler>.Instance;
    }

    public async Task<Result<FaceEnrollmentResponseDto>> Handle(
        EnrollFacePhotosCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<FaceEnrollmentResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<FaceEnrollmentResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            _device.TenantId, _device.UserId, _device.LegalEntityId, cancellationToken);
        var profile = await _profiles.GetByEmployeeIdAsync(_device.TenantId, employeeId, cancellationToken);

        // Tray face setup always replaces the enrolled face with these three photos (product
        // decision: an employee re-running device setup re-enrols). Whoever completes setup on
        // the device becomes the reference face, so the replacement is logged below.
        var replacing = profile?.ReferencePhotoFileId is not null;

        var front = await ReadAsync(request.Front, cancellationToken);
        // Side photos are optional (the tray now takes only the front one); both or neither.
        var left = request.Left is null ? null : await ReadAsync(request.Left, cancellationToken);
        var right = request.Right is null ? null : await ReadAsync(request.Right, cancellationToken);

        try
        {
            var photos = new List<(string Pose, MemoryStream Bytes)> { (FacePhotoPose.Front, front) };
            if (left is not null && right is not null)
            {
                photos.Add((FacePhotoPose.Left, left));
                photos.Add((FacePhotoPose.Right, right));
            }

            var qualities = new Dictionary<string, FaceQualityOutcome>();
            foreach (var (pose, bytes) in photos)
            {
                bytes.Position = 0;
                var quality = await _faceQuality.AnalyzeAsync(bytes, cancellationToken);
                if (!quality.LightingOk || !quality.FaceVisible || !quality.NoSunglassesOrMask)
                    return Fail(pose, ValidateFacePhotoCommandHandler.FirstQualityFailure(quality));
                if (!FacePhotoPoseRules.Matches(pose, quality))
                    return Fail(pose, ValidateFacePhotoCommandHandler.FailureWrongPose);
                qualities[pose] = quality;
            }

            if (left is not null && right is not null)
            {
                if (!FacePhotoPoseRules.AreOppositeSides(qualities[FacePhotoPose.Left], qualities[FacePhotoPose.Right]))
                    return Fail(FacePhotoPose.Right, FailureSameSide);

                // All photos must be the same person, so a setup can't mix two people's faces.
                foreach (var (pose, side) in new[] { (FacePhotoPose.Left, left), (FacePhotoPose.Right, right) })
                {
                    front.Position = 0;
                    side.Position = 0;
                    var match = await _faceMatch.CompareAsync(front, side, cancellationToken);
                    if (!match.IsMatch)
                        return Fail(pose, ValidateFacePhotoCommandHandler.FailureNotMatched);
                }
            }

            var frontId = await UploadAsync(front, request.Front.ContentType, FacePhotoPose.Front, cancellationToken);
            if (frontId is null)
                return Fail(null, ValidateFacePhotoCommandHandler.FailureVerificationFailed);

            Guid? leftId = null, rightId = null;
            if (left is not null && right is not null)
            {
                leftId = await UploadAsync(left, request.Left!.ContentType, FacePhotoPose.Left, cancellationToken);
                rightId = await UploadAsync(right, request.Right!.ContentType, FacePhotoPose.Right, cancellationToken);
                if (leftId is null || rightId is null)
                    return Fail(null, ValidateFacePhotoCommandHandler.FailureVerificationFailed);
            }

            var now = DateTimeOffset.UtcNow;
            if (profile is not null)
            {
                profile.Status = BiometricProfileStatus.Enrolled;
                profile.EnrolledAt = now;
                profile.UpdatedAt = now;
                profile.ReferencePhotoFileId = frontId;
                profile.LeftReferencePhotoFileId = leftId;
                profile.RightReferencePhotoFileId = rightId;
                _profiles.Update(profile);
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
                    ReferencePhotoFileId = frontId,
                    LeftReferencePhotoFileId = leftId,
                    RightReferencePhotoFileId = rightId
                }, cancellationToken);
            }

            await _profiles.SaveChangesAsync(cancellationToken);
            if (replacing)
            {
                _logger.LogWarning(
                    "Tray face setup replaced the enrolled face of employee {EmployeeId} (user {UserId}, device {DeviceId})",
                    employeeId, _device.UserId, _device.DeviceRegistrationId);
            }
            return Result<FaceEnrollmentResponseDto>.Success(new FaceEnrollmentResponseDto(true, null, null));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(null, ValidateFacePhotoCommandHandler.FailureVerificationFailed);
        }
        finally
        {
            await front.DisposeAsync();
            if (left is not null) await left.DisposeAsync();
            if (right is not null) await right.DisposeAsync();
        }
    }

    private static Result<FaceEnrollmentResponseDto> Fail(string? photo, string reason) =>
        Result<FaceEnrollmentResponseDto>.Success(new FaceEnrollmentResponseDto(false, photo, reason));

    private static async Task<MemoryStream> ReadAsync(FaceSetupPhoto photo, CancellationToken ct)
    {
        var copy = new MemoryStream();
        await photo.Content.CopyToAsync(copy, ct);
        copy.Position = 0;
        return copy;
    }

    private async Task<Guid?> UploadAsync(MemoryStream bytes, string contentType, string pose, CancellationToken ct)
    {
        bytes.Position = 0;
        var upload = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            $"face-setup-{pose}.jpg",
            string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
            UploadPurposeCatalog.BiometricReferencePhoto,
            bytes,
            ct);
        return upload.IsSuccess ? upload.Value!.Id : null;
    }
}
