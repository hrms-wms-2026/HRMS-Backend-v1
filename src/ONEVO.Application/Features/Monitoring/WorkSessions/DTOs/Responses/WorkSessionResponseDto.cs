using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.WorkSessions.DTOs.Responses;

public record WorkSessionResponseDto(
    [property: JsonPropertyName("session_id")] Guid SessionId);
