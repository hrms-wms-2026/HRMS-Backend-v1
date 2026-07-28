using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed record ClockInResponse(
    [property: JsonPropertyName("clock_in_status")] string ClockInStatus,
    [property: JsonPropertyName("attendance_id")] Guid? AttendanceId,
    [property: JsonPropertyName("presence_session_id")] Guid? PresenceSessionId,
    [property: JsonPropertyName("approval_request_id")] Guid? ApprovalRequestId,
    [property: JsonPropertyName("approval_request_type")] string? ApprovalRequestType,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("clocked_in_at")] DateTimeOffset? ClockedInAt,
    [property: JsonIgnore] int HttpStatusCode);

public sealed record PresenceSessionStartedEvent(
    Guid TenantId,
    Guid AgentId,
    Guid EmployeeId,
    Guid AttendanceId,
    Guid PresenceSessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? MonitoringHardStopAt);

