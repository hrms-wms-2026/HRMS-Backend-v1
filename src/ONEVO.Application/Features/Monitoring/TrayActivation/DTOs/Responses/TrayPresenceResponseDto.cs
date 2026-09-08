using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

public sealed record TrayPresenceResponseDto(
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("grace_period_seconds")] int GracePeriodSeconds,
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("presence_valid_until")] DateTimeOffset? PresenceValidUntil,
    [property: JsonPropertyName("device")] TrayPresenceDeviceDto? Device);

public sealed record TrayPresenceDeviceDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("last_seen_at")] DateTimeOffset LastSeenAt);
