using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;

public sealed record ClockOutResponse(
    [property: JsonPropertyName("clock_out_status")] string ClockOutStatus,
    [property: JsonPropertyName("attendance_id")] Guid? AttendanceId,
    [property: JsonPropertyName("presence_session_id")] Guid? PresenceSessionId,
    [property: JsonPropertyName("clocked_out_at")] DateTimeOffset? ClockedOutAt,
    [property: JsonPropertyName("worked_minutes")] int WorkedMinutes,
    [property: JsonPropertyName("break_minutes")] int BreakMinutes,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonIgnore] int HttpStatusCode);

public sealed record PresenceSessionEndedEvent(
    Guid TenantId,
    Guid AgentId,
    Guid EmployeeId,
    Guid AttendanceId,
    Guid PresenceSessionId,
    DateTimeOffset EndedAt,
    int WorkedMinutes,
    int BreakMinutes);

