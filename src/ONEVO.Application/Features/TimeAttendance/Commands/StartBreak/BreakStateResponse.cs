using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;

public sealed record BreakStateResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("break_id")] Guid? BreakId,
    [property: JsonPropertyName("break_type")] string? BreakType,
    [property: JsonPropertyName("break_started_at")] DateTimeOffset? BreakStartedAt,
    [property: JsonPropertyName("break_ended_at")] DateTimeOffset? BreakEndedAt,
    [property: JsonPropertyName("monitoring_state")] string MonitoringState);

public sealed record PresenceBreakStartedEvent(
    Guid TenantId,
    Guid AgentId,
    Guid EmployeeId,
    Guid BreakId,
    DateTimeOffset StartedAt,
    string BreakType);

public sealed record PresenceBreakEndedEvent(
    Guid TenantId,
    Guid AgentId,
    Guid EmployeeId,
    Guid BreakId,
    DateTimeOffset EndedAt,
    int BreakMinutes);

