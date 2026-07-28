using System.Text.Json.Serialization;
using ONEVO.Application.Features.AgentGateway.Policy;

namespace ONEVO.Application.Features.AgentGateway.DTOs;

public sealed record AgentWorkContextDto(
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("monitoring_state")] string MonitoringState,
    [property: JsonPropertyName("presence_session_id")] Guid? PresenceSessionId,
    [property: JsonPropertyName("break_id")] Guid? BreakId,
    [property: JsonPropertyName("schedule_name")] string? ScheduleName,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("day_type")] string? DayType,
    [property: JsonPropertyName("scheduled_start")] TimeOnly? ScheduledStart,
    [property: JsonPropertyName("scheduled_end")] TimeOnly? ScheduledEnd,
    [property: JsonPropertyName("required_work_minutes")] int? RequiredWorkMinutes,
    [property: JsonPropertyName("expected_work_area")] string? ExpectedWorkArea,
    [property: JsonPropertyName("hard_stop_at")] DateTimeOffset? HardStopAt,
    [property: JsonPropertyName("effective_policy")] EffectiveAgentPolicy EffectivePolicy,
    [property: JsonPropertyName("task_feature_available")] bool TaskFeatureAvailable,
    [property: JsonPropertyName("assigned_tasks")] object[] AssignedTasks,
    [property: JsonPropertyName("active_task_timers")] object[] ActiveTaskTimers);
