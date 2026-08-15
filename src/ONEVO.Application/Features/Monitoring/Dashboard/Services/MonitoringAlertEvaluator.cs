using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;

namespace ONEVO.Application.Features.Monitoring.Dashboard.Services;

public static class MonitoringAlertEvaluator
{
    private static readonly TimeOnly ShiftStart = new(9, 0);
    private static readonly TimeOnly ShiftEnd = new(18, 0);

    public const int GraceMinutes = 10;
    public const int LongIdleThresholdMinutes = 120;
    public const decimal LowActivityScoreThreshold = 50m;
    public const decimal LowDataCoverageThreshold = 60m;

    public static IReadOnlyList<MonitoringDashboardAlertDto> Evaluate(
        ActivityDailySummaryDto? summary,
        IReadOnlyList<WorkSessionReportDto> sessions,
        TimeZoneInfo? timeZone = null)
    {
        var alerts = new List<MonitoringDashboardAlertDto>();

        AddWorkSessionAlerts(alerts, sessions, timeZone ?? TimeZoneInfo.Utc);

        if (summary is null)
            return alerts;

        if (summary.TotalIdleMinutes > LongIdleThresholdMinutes)
        {
            alerts.Add(new MonitoringDashboardAlertDto(
                "long_idle",
                $"Idle time exceeded {LongIdleThresholdMinutes} minutes.",
                "warning"));
        }

        if (summary.DataCoveragePercentage < LowDataCoverageThreshold)
        {
            alerts.Add(new MonitoringDashboardAlertDto(
                "low_data_coverage",
                $"Data coverage is below {LowDataCoverageThreshold}%.",
                "warning"));
        }

        if (summary.DataCoveragePercentage >= LowDataCoverageThreshold
            && summary.ActivityScore < LowActivityScoreThreshold)
        {
            alerts.Add(new MonitoringDashboardAlertDto(
                "low_activity_score",
                $"Activity score is below {LowActivityScoreThreshold}.",
                "warning"));
        }

        return alerts;
    }

    private static void AddWorkSessionAlerts(
        List<MonitoringDashboardAlertDto> alerts,
        IReadOnlyList<WorkSessionReportDto> sessions,
        TimeZoneInfo timeZone)
    {
        if (sessions.Count == 0)
            return;

        var firstClockIn = sessions.Min(s => TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(s.ClockInAt, timeZone).DateTime));
        var latestClockOut = sessions.Max(s => TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(s.ClockOutAt, timeZone).DateTime));

        if (firstClockIn > ShiftStart.AddMinutes(GraceMinutes))
        {
            alerts.Add(new MonitoringDashboardAlertDto(
                "late_login",
                $"First clock-in was after {ShiftStart.AddMinutes(GraceMinutes):HH:mm}.",
                "warning"));
        }

        if (latestClockOut < ShiftEnd.AddMinutes(-GraceMinutes))
        {
            alerts.Add(new MonitoringDashboardAlertDto(
                "early_logout",
                $"Last clock-out was before {ShiftEnd.AddMinutes(-GraceMinutes):HH:mm}.",
                "warning"));
        }
    }
}
