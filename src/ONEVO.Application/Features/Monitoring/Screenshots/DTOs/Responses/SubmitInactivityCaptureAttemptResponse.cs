using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

public sealed record SubmitInactivityCaptureAttemptResponse(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("evidence_asset_id")] Guid? EvidenceAssetId,
    [property: JsonPropertyName("file_record_id")] Guid? FileRecordId);
