using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
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

    /// <summary>
    /// With no manager above the employee (e.g. the owner), HR — whoever can edit employee
    /// records — is alerted instead.
    /// </summary>
    public const string HrFallbackPermission = "employees:write";

    private const string NoReferencePhoto = "no_reference_photo";
    private const string VerificationFailed = "verification_failed";

    private readonly IFaceVerificationAttemptRepository _attempts;
    private readonly IFileStorageService _fileStorage;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly INotificationDispatcher _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly IPermissionRepository _permissions;
    private readonly ILogger<FaceVerificationRetryPolicy> _logger;

    public FaceVerificationRetryPolicy(
        IFaceVerificationAttemptRepository attempts,
        IFileStorageService fileStorage,
        IEmployeeRepository employees,
        IEmployeeAuthorityResolver authority,
        INotificationDispatcher notifications,
        IDateTimeProvider clock,
        IPermissionRepository permissions,
        ILogger<FaceVerificationRetryPolicy>? logger = null)
    {
        _attempts = attempts;
        _fileStorage = fileStorage;
        _employees = employees;
        _authority = authority;
        _notifications = notifications;
        _clock = clock;
        _permissions = permissions;
        _logger = logger ?? NullLogger<FaceVerificationRetryPolicy>.Instance;
    }

    public async Task<FacePhotoValidationResponseDto> ApplyAsync(
        FaceCheckAttemptContext context,
        FacePhotoValidationResponseDto result,
        Stream photo,
        string contentType,
        CancellationToken ct)
    {
        // Nothing was actually judged, so these never count toward the retry limit and never let
        // the employee through: no enrolled face to compare with, or AWS gave no answer
        // (unavailable / timed out). Only a face AWS looked at and rejected counts.
        if (result.FailureReason is NoReferencePhoto or VerificationFailed)
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
        var recipients = await ResolveReviewersAsync(context, employee, ct);
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Face verification override for employee {EmployeeId}: no manager with {Permission} and no HR user with {HrPermission} found, nobody alerted",
                context.EmployeeId, ReviewerPermission, HrFallbackPermission);
            return;
        }

        var name = employee is null ? "" : $"{employee.FirstName} {employee.LastName}".Trim();
        var placeholders = new Dictionary<string, string>
        {
            ["employeeName"] = string.IsNullOrWhiteSpace(name) ? "An employee" : name,
            ["attempts"] = MaxAttempts.ToString(),
            ["action"] = context.Purpose == "clock_out" ? "clock out" : "clock in",
            ["time"] = attempt.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["reasons"] = reasons.Count == 0 ? "unknown" : string.Join(", ", reasons.Select(Describe))
        };

        foreach (var recipient in recipients)
        {
            await _notifications.SendTemplatedAsync(
                context.TenantId, recipient, NotificationTemplate, placeholders, RelatedEntityType, attempt.Id, ct);
        }
    }

    /// <summary>
    /// The employee's manager (attendance approver on their reporting line) when there is one;
    /// otherwise every HR user in the tenant. The employee is never alerted about themselves.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveReviewersAsync(
        FaceCheckAttemptContext context, ONEVO.Domain.Features.CoreHr.Entities.Employee? employee, CancellationToken ct)
    {
        var legalEntityId = context.DeviceLegalEntityId ?? employee?.LegalEntityId;
        if (employee is not null && legalEntityId is not null)
        {
            var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                employee.Id, legalEntityId.Value, ReviewerPermission,
                EmployeeAuthorityPurpose.FaceVerificationOverrideReview), ct);
            if (route.IsSuccess && route.Value is not null && route.Value.ApproverUserId != context.UserId)
                return [route.Value.ApproverUserId];
        }

        _logger.LogInformation(
            "Face verification override for employee {EmployeeId}: no manager with {Permission}, alerting HR ({HrPermission})",
            context.EmployeeId, ReviewerPermission, HrFallbackPermission);

        var hr = await _permissions.ListUserIdsWithPermissionCodeAsync(
            context.TenantId, HrFallbackPermission, _clock.UtcNow, ct);
        return hr.Where(id => id != context.UserId).Distinct().ToList();
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
