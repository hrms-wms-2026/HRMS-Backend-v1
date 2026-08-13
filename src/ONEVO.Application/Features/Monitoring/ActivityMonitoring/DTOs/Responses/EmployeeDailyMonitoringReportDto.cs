namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record EmployeeDailyMonitoringReportDto(
    Guid EmployeeId,
    DateOnly Date,
    ActivityDailySummaryDto? Activity,
    IReadOnlyList<WorkSessionReportDto> WorkSessions,
    int PromptCount,
    int CapturedCount,
    int DeclinedCount,
    int TimedOutCount,
    int ActivityResumedCount,
    int MonitoringStoppedCount,
    int FailedCount,
    IReadOnlyList<InactivityAttemptReportDto> InactivityAttempts);

public sealed record WorkSessionReportDto(
    Guid SessionId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int WorkSeconds,
    int BreakSeconds,
    int BreakCount);

public sealed record InactivityAttemptReportDto(
    Guid AttemptId,
    DateTimeOffset PromptedAt,
    DateTimeOffset? CapturedAt,
    int IdleDurationSeconds,
    int MonitorCount,
    string Outcome,
    string? FailureCode,
    Guid? EvidenceAssetId,
    bool ScreenshotAvailable);
