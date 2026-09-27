using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Services;

public class FaceVerificationRetryPolicy : IFaceVerificationRetryPolicy
{
    /// <summary>Attempts per clock-in/out; the last one lets the employee through for manager review.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Failures older than this no longer count toward the limit.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(12);

    public const string ManagerReview = "manager_review";
    public const string NotificationTemplate = "face_verification_override";
    public const string RelatedEntityType = "face_verification_attempt";

    /// <summary>The employee's manager is whoever approves their attendance.</summary>
    public const string ReviewerPermission = "attendance:approve";

    private const string NoReferencePhoto = "no_reference_photo";

    private readonly IFaceVerificationAttemptRepository _attempts;
    private readonly IFileStorageService _fileStorage;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly INotificationDispatcher _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<FaceVerificationRetryPolicy> _logger;

    public FaceVerificationRetryPolicy(
        IFaceVerificationAttemptRepository attempts,
        IFileStorageService fileStorage,
        IEmployeeRepository employees,
        IEmployeeAuthorityResolver authority,
        INotificationDispatcher notifications,
        IDateTimeProvider clock,
        ILogger<FaceVerificationRetryPolicy>? logger = null)
    {
        _attempts = attempts;
        _fileStorage = fileStorage;
        _employees = employees;
        _authority = authority;
        _notifications = notifications;
        _clock = clock;
        _logger = logger ?? NullLogger<FaceVerificationRetryPolicy>.Instance;
    }

    public async Task<FacePhotoValidationResponseDto> ApplyAsync(
        FaceCheckAttemptContext context,
        FacePhotoValidationResponseDto result,
        Stream photo,
        string contentType,
        CancellationToken ct)
    {
        // No enrolled face: nothing was compared, and retrying can't fix it — stays blocked.
        if (result.FailureReason == NoReferencePhoto)
            return result;

        var now = _clock.UtcNow;
        var attempt = new FaceVerificationAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            Purpose = context.Purpose,
            SimilarityScore = result.SimilarityScore,
            CreatedAt = now
        };

        if (result.CanProceed)
        {
            attempt.Outcome = FaceVerificationAttempt.OutcomePassed;
            await _attempts.AddAsync(attempt, ct);
            await _attempts.SaveChangesAsync(ct);
            return result with { FailedAttempts = 0, MaxAttempts = MaxAttempts };
        }

        attempt.FailureReason = result.FailureReason;
        var earlier = await _attempts.GetConsecutiveFailuresAsync(
            context.TenantId, context.EmployeeId, context.Purpose, now - Window, ct);
        var failedSoFar = earlier.Count + 1;

        if (failedSoFar < MaxAttempts)
        {
            attempt.Outcome = FaceVerificationAttempt.OutcomeFailed;
            await _attempts.AddAsync(attempt, ct);
            await _attempts.SaveChangesAsync(ct);
            return result with { FailedAttempts = failedSoFar, MaxAttempts = MaxAttempts };
        }

        // Last attempt failed too: let the employee through, keep the photo, alert the manager.
        attempt.Outcome = FaceVerificationAttempt.OutcomeOverridden;
        attempt.PhotoFileId = await KeepPhotoAsync(context, photo, contentType, ct);
        await _attempts.AddAsync(attempt, ct);

        var reasons = earlier
            .Select(a => a.FailureReason)
            .Append(result.FailureReason)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();
        await AlertManagerAsync(context, attempt, reasons!, ct);

        await _attempts.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Face verification overridden after {Attempts} failed attempts for employee {EmployeeId} ({Purpose}); reasons {Reasons}",
            failedSoFar, context.EmployeeId, context.Purpose, string.Join(",", reasons));

        return result with
        {
            CanProceed = true,
            FailureReason = ManagerReview,
            FailedAttempts = failedSoFar,
            MaxAttempts = MaxAttempts
        };
    }

    private async Task<Guid?> KeepPhotoAsync(
        FaceCheckAttemptContext context, Stream photo, string contentType, CancellationToken ct)
    {
        try
        {
            if (photo.CanSeek)
                photo.Position = 0;
            var upload = await _fileStorage.UploadAsync(
                context.TenantId,
                context.UserId,
                $"face-check-override-{context.Purpose}.jpg",
                string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
                UploadPurposeCatalog.MonitoringFaceScan,
                photo,
                ct);
            if (upload.IsSuccess)
                return upload.Value!.Id;

            _logger.LogWarning("Could not keep the overridden face check photo: {Error}", upload.Error);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not keep the overridden face check photo");
        }

        // The employee is still let through: losing the photo must not lock them out of work.
        return null;
    }

    private async Task AlertManagerAsync(
        FaceCheckAttemptContext context, FaceVerificationAttempt attempt, IReadOnlyList<string> reasons, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(context.TenantId, context.EmployeeId, ct);
        var legalEntityId = context.DeviceLegalEntityId ?? employee?.LegalEntityId;
        if (employee is null || legalEntityId is null)
        {
            _logger.LogWarning(
                "Face verification override for employee {EmployeeId}: employee or legal entity not found, no manager alerted",
                context.EmployeeId);
            return;
        }

        var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            employee.Id, legalEntityId.Value, ReviewerPermission,
            EmployeeAuthorityPurpose.FaceVerificationOverrideReview), ct);
        if (!route.IsSuccess || route.Value is null)
        {
            _logger.LogWarning(
                "Face verification override for employee {EmployeeId}: no manager with {Permission} found, nobody alerted",
                employee.Id, ReviewerPermission);
            return;
        }

        var name = $"{employee.FirstName} {employee.LastName}".Trim();
        await _notifications.SendTemplatedAsync(
            context.TenantId,
            route.Value.ApproverUserId,
            NotificationTemplate,
            new Dictionary<string, string>
            {
                ["employeeName"] = string.IsNullOrWhiteSpace(name) ? "An employee" : name,
                ["attempts"] = MaxAttempts.ToString(),
                ["action"] = context.Purpose == "clock_out" ? "clock out" : "clock in",
                ["time"] = attempt.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                ["reasons"] = reasons.Count == 0 ? "unknown" : string.Join(", ", reasons.Select(Describe))
            },
            RelatedEntityType,
            attempt.Id,
            ct);
    }

    private static string Describe(string reason) => reason switch
    {
        "not_matched" => "face did not match",
        "no_face_detected" => "no face detected",
        "multiple_faces" => "more than one person",
        "face_not_visible" => "face not fully visible",
        "poor_lighting" => "poor lighting",
        "sunglasses_or_mask" => "sunglasses or mask",
        "glasses_glare" => "glare on glasses",
        "eyes_closed" => "eyes closed",
        "verification_failed" => "face check service unavailable",
        _ => reason
    };
}
