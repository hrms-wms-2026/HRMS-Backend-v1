using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

/// <param name="Purpose">"clock_in" or "clock_out" — counted separately.</param>
/// <param name="DeviceRegistrationId">The tray device the check came from, kept on the identity case as evidence.</param>
public record FaceCheckAttemptContext(
    Guid TenantId,
    Guid UserId,
    Guid EmployeeId,
    Guid? DeviceLegalEntityId,
    string Purpose,
    Guid? DeviceRegistrationId = null);

public interface IFaceVerificationRetryPolicy
{
    /// <summary>
    /// Records a clock-in/out face check and applies the retry rule: after
    /// <c>MaxAttempts - 1</c> consecutive failures the next failed check still lets the employee
    /// through (<c>manager_review</c>), keeps the photo, and alerts their manager to review it.
    /// Returns the result the tray should act on.
    /// </summary>
    Task<FacePhotoValidationResponseDto> ApplyAsync(
        FaceCheckAttemptContext context,
        FacePhotoValidationResponseDto result,
        Stream photo,
        string contentType,
        CancellationToken ct);
}
