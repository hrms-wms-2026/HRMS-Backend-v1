namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

using System.Text.Json.Serialization;

public sealed record TrayAttendanceStatusDto(
    [property: JsonPropertyName("is_clocked_in")] bool IsClockedIn,
    [property: JsonPropertyName("clocked_in_at_utc")] DateTimeOffset? ClockedInAtUtc,
    [property: JsonPropertyName("is_on_break")] bool IsOnBreak,
    [property: JsonPropertyName("break_started_at_utc")] DateTimeOffset? BreakStartedAtUtc,
    [property: JsonPropertyName("can_start_break")] bool CanStartBreak = true,
    [property: JsonPropertyName("break_allowance_minutes")] int? BreakAllowanceMinutes = null,
    [property: JsonPropertyName("completed_break_minutes")] int CompletedBreakMinutes = 0);
