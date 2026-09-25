using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.Helpers;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;

/// <summary>
/// Checks one selfie. Clock-in/out: quality + match against the enrolled face.
/// Enrollment (tray face setup): quality + head pose for that step only — nothing is saved here;
/// the three setup photos are saved together by EnrollFacePhotosCommand.
/// </summary>
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
    public const string FailureWrongPose = "wrong_pose";
    public const string FailureGlassesGlare = "glasses_glare";
    public const string FailureEyesClosed = "eyes_closed";

    /// <summary>
    /// Face setup found this employee already enrolled and the photo matched — CanProceed is true;
    /// the tray skips the side photos. Not an error despite travelling in failure_reason.
    /// </summary>
    public const string AlreadyEnrolled = "already_enrolled";

    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IBiometricProfileRepository _profiles;
    private readonly IFaceQualityService _faceQuality;
    private readonly IEnrolledFaceMatcher _matcher;
    private readonly ITrayEmployeeIdentityResolver _employeeIdentity;

    public ValidateFacePhotoCommandHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IBiometricProfileRepository profiles,
        IFaceQualityService faceQuality,
        IEnrolledFaceMatcher matcher,
        ITrayEmployeeIdentityResolver employeeIdentity)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _profiles = profiles;
        _faceQuality = faceQuality;
        _matcher = matcher;
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

        // Every answer AWS produced carries what it saw, so a rejection can be diagnosed.
        Result<FacePhotoValidationResponseDto> Done(FacePhotoValidationResponseDto dto) =>
            Result<FacePhotoValidationResponseDto>.Success(WithDetectedFaces(dto, quality));

        if (!quality.LightingOk || !quality.FaceVisible || !quality.NoSunglassesOrMask)
            return Done(Rejected(quality, FirstQualityFailure(quality)));

        var enrollment = FacePhotoValidationPurpose.IsEnrollment(request.Purpose);
        if (enrollment && !FacePhotoPoseRules.Matches(request.Pose, quality))
            return Done(Rejected(quality, FailureWrongPose));

        var employeeId = await _employeeIdentity.ResolveEmployeeIdAsync(
            _device.TenantId, _device.UserId, _device.LegalEntityId, cancellationToken);
        var profile = await _profiles.GetByEmployeeIdAsync(_device.TenantId, employeeId, cancellationToken);

        EnrolledFaceMatch match;
        try
        {
            match = await _matcher.MatchAsync(_device.TenantId, profile, captured, cancellationToken);
        }
        catch (Exception)
        {
            return Done(FailedVerification(quality));
        }

        if (!match.HasReference)
        {
            // Face setup: this step's photo is fine; the reference is saved only when all three
            // setup photos are committed together. Clock-in/out must never create a reference —
            // whoever sits at the laptop first would become the "enrolled" face.
            return Done(enrollment
                ? new FacePhotoValidationResponseDto(
                    quality.LightingOk, quality.FaceVisible, quality.NoSunglassesOrMask,
                    IsMatch: false, CanProceed: true, SimilarityScore: null, FailureReason: null)
                : Rejected(quality, FailureNoReferencePhoto));
        }

        if (match.Failed)
            return Done(FailedVerification(quality));

        return Done(new FacePhotoValidationResponseDto(
            quality.LightingOk,
            quality.FaceVisible,
            quality.NoSunglassesOrMask,
            match.IsMatch,
            CanProceed: match.IsMatch,
            match.Similarity,
            FailureReason: !match.IsMatch ? FailureNotMatched
                : enrollment ? AlreadyEnrolled
                : null));
    }

    private static FacePhotoValidationResponseDto WithDetectedFaces(
        FacePhotoValidationResponseDto dto, FaceQualityOutcome quality) =>
        dto with
        {
            FaceCount = quality.FaceCount,
            Faces = quality.Faces?
                .Select(f => new FaceBoxDto(f.Confidence, f.Left, f.Top, f.Width, f.Height))
                .ToList()
        };

    private static FacePhotoValidationResponseDto Rejected(FaceQualityOutcome quality, string reason) =>
        new(
            quality.LightingOk,
            quality.FaceVisible,
            quality.NoSunglassesOrMask,
            IsMatch: false,
            CanProceed: false,
            SimilarityScore: null,
            FailureReason: reason);

    private static FacePhotoValidationResponseDto FailedVerification(FaceQualityOutcome? quality = null) =>
        new(
            quality?.LightingOk ?? false,
            quality?.FaceVisible ?? false,
            quality?.NoSunglassesOrMask ?? false,
            IsMatch: false,
            CanProceed: false,
            SimilarityScore: null,
            FailureReason: FailureVerificationFailed);

    internal static string FirstQualityFailure(FaceQualityOutcome quality)
    {
        if (quality.FaceCount == 0) return FailureNoFaceDetected;
        if (quality.FaceCount > 1) return FailureMultipleFaces;
        if (quality.EyesClosed) return FailureEyesClosed;
        if (!quality.FaceVisible) return FailureFaceNotVisible;
        if (!quality.LightingOk) return FailurePoorLighting;
        if (quality.GlassesGlare) return FailureGlassesGlare;
        return FailureSunglassesOrMask;
    }
}
