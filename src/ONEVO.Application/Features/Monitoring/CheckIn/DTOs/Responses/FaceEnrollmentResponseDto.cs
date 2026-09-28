using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

/// <param name="Enrolled">All three face setup photos were accepted and saved.</param>
/// <param name="FailedPhoto">"front", "left" or "right" — the photo to retake; null when enrolled or not photo-specific.</param>
/// <param name="FailureReason">Same codes as face-preview, plus "already_enrolled" and "same_side".</param>
public record FaceEnrollmentResponseDto(
    [property: JsonPropertyName("enrolled")] bool Enrolled,
    [property: JsonPropertyName("failed_photo")] string? FailedPhoto,
    [property: JsonPropertyName("failure_reason")] string? FailureReason);
