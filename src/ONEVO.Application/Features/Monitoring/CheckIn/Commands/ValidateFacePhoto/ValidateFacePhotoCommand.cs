using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.Helpers;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Commands.ValidateFacePhoto;

/// <param name="Purpose">
/// One of <see cref="FacePhotoValidationPurpose"/>. Null is treated as clock-in so a caller
/// that omits it can never silently enrol a reference face.
/// </param>
/// <param name="Pose">
/// Face setup step (<see cref="FacePhotoPose"/>); only used with the enrollment purpose.
/// Null means front.
/// </param>
public record ValidateFacePhotoCommand(
    Stream ImageStream,
    string ContentType,
    long FileSizeBytes,
    string? Purpose = null,
    string? Pose = null
) : IRequest<Result<FacePhotoValidationResponseDto>>;

public static class FacePhotoValidationPurpose
{
    public const string Enrollment = "enrollment";
    public const string ClockIn = "clock_in";
    public const string ClockOut = "clock_out";

    public static readonly string[] All = [Enrollment, ClockIn, ClockOut];

    public static bool IsEnrollment(string? purpose) =>
        string.Equals(purpose, Enrollment, StringComparison.OrdinalIgnoreCase);
}
