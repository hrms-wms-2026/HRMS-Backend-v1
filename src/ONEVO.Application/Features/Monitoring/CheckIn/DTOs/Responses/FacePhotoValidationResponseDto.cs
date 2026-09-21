using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record FacePhotoValidationResponseDto(
    [property: JsonPropertyName("lighting_ok")] bool LightingOk,
    [property: JsonPropertyName("face_visible")] bool FaceVisible,
    [property: JsonPropertyName("no_sunglasses_or_mask")] bool NoSunglassesOrMask,
    [property: JsonPropertyName("is_match")] bool IsMatch,
    [property: JsonPropertyName("can_proceed")] bool CanProceed,
    [property: JsonPropertyName("similarity_score")] float? SimilarityScore,
    [property: JsonPropertyName("failure_reason")] string? FailureReason);
