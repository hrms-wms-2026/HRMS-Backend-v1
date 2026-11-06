namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

using System.Text.Json.Serialization;

/// <summary>Tasks the tray employee clocked in on during their current work date.</summary>
public sealed record TrayDailyTasksDto(
    [property: JsonPropertyName("work_date")] DateOnly WorkDate,
    [property: JsonPropertyName("total_minutes")] int TotalMinutes,
    [property: JsonPropertyName("task_count")] int TaskCount,
    [property: JsonPropertyName("done_count")] int DoneCount,
    [property: JsonPropertyName("tasks")] IReadOnlyList<TrayDailyTaskDto> Tasks);

public sealed record TrayDailyTaskDto(
    [property: JsonPropertyName("task_id")] Guid TaskId,
    [property: JsonPropertyName("short_id")] string ShortId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("project_name")] string? ProjectName,
    [property: JsonPropertyName("status_name")] string? StatusName,
    [property: JsonPropertyName("status_color")] string? StatusColor,
    [property: JsonPropertyName("status_category")] string? StatusCategory,
    [property: JsonPropertyName("progress_percent")] int ProgressPercent,
    [property: JsonPropertyName("minutes_today")] int MinutesToday,
    [property: JsonPropertyName("session_count")] int SessionCount,
    [property: JsonPropertyName("is_done")] bool IsDone,
    [property: JsonPropertyName("is_clocked_in_now")] bool IsClockedInNow,
    [property: JsonPropertyName("first_clock_in_at_utc")] DateTimeOffset FirstClockInAtUtc,
    [property: JsonPropertyName("last_activity_at_utc")] DateTimeOffset LastActivityAtUtc,
    // % before the employee's first push today and after their last push today; null when they
    // did not push this task today (the tray then shows only the current progress).
    [property: JsonPropertyName("today_from_percent")] int? TodayFromPercent = null,
    [property: JsonPropertyName("today_to_percent")] int? TodayToPercent = null);
