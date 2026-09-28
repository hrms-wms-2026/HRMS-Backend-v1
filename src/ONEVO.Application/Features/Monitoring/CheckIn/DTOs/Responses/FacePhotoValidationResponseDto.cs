using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record FacePhotoValidationResponseDto(
    [property: JsonPropertyName("lighting_ok")] bool LightingOk,
    [property: JsonPropertyName("face_visible")] bool FaceVisible,
    [property: JsonPropertyName("no_sunglasses_or_mask")] bool NoSunglassesOrMask,
    [property: JsonPropertyName("is_match")] bool IsMatch,
    [property: JsonPropertyName("can_proceed")] bool CanProceed,
    [property: JsonPropertyName("similarity_score")] float? SimilarityScore,
    [property: JsonPropertyName("failure_reason")] string? FailureReason,
    [property: JsonPropertyName("face_count")] int? FaceCount = null,
    [property: JsonPropertyName("faces")] IReadOnlyList<FaceBoxDto>? Faces = null);

/// <summary>A face AWS saw in the photo; box values are fractions (0-1) of the image.</summary>
public record FaceBoxDto(
    [property: JsonPropertyName("confidence")] float Confidence,
    [property: JsonPropertyName("left")] float Left,
    [property: JsonPropertyName("top")] float Top,
    [property: JsonPropertyName("width")] float Width,
    [property: JsonPropertyName("height")] float Height);
