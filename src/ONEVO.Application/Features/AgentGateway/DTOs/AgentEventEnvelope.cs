using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.AgentGateway.DTOs;

public sealed record AgentEventEnvelope
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("captured_at")]
    public DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("presence_session_id")]
    public Guid PresenceSessionId { get; init; }

    [JsonPropertyName("task_id")]
    public Guid? TaskId { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }
}
