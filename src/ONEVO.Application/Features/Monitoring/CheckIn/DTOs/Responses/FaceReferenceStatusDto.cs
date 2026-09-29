using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

/// <param name="Enrolled">An active enrolled face exists; tray device setup skips face setup.</param>
/// <param name="ReferencePhotoCount">1 for single-photo profiles, 3 after tray face setup.</param>
public record FaceReferenceStatusDto(
    [property: JsonPropertyName("enrolled")] bool Enrolled,
    [property: JsonPropertyName("reference_photo_count")] int ReferencePhotoCount);
