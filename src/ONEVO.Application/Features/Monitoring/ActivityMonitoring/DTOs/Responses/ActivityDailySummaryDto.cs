namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public record ActivityDailySummaryDto
{
    public Guid EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public int TotalActiveMinutes { get; init; }
    public int TotalIdleMinutes { get; init; }
    public int TotalMeetingMinutes { get; init; }
    public decimal ActivePercentage { get; init; }
    public decimal ActivityScore { get; init; }
    public int KeyboardTotal { get; init; }
    public int MouseTotal { get; init; }
    public int FocusMinutes { get; init; }
    public int DeepFocusSessionsCount { get; init; }
    public decimal IntensityAvg { get; init; }
    public decimal DataCoveragePercentage { get; init; }
    public List<AppUsageSummary> TopApps { get; init; } = [];
}

public record AppUsageSummary
{
    public string AppName { get; init; } = string.Empty;
    public int TotalSeconds { get; init; }
    public string Category { get; init; } = string.Empty;
}
