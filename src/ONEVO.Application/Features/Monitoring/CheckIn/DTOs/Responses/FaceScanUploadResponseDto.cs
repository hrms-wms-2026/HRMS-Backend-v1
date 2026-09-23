using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record FaceScanUploadResponseDto(
    [property: JsonPropertyName("face_scan_id")] Guid FaceScanId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("similarity_score")] float? SimilarityScore);
