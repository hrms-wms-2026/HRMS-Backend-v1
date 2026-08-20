using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;

public record EmployeeDailyReportDto
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }

    /// <summary>Null until the nightly ActivityDailySummaryJob has aggregated this date.</summary>
    public ActivityDailySummaryDto? Activity { get; init; }

    public DateTimeOffset? ClockInAt { get; init; }
    public DateTimeOffset? ClockOutAt { get; init; }
    public int WorkedMinutes { get; init; }
    public int BreakMinutes { get; init; }
    public int BreakSessionCount { get; init; }

    public List<ScreenshotEntryDto> Screenshots { get; init; } = [];
}

public record ScreenshotEntryDto(
    Guid Id,
    DateTimeOffset CapturedAt,
    string EvidenceType,
    string TriggerType,
    string? Url);
