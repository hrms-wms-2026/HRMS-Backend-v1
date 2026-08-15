using ONEVO.Application.Features.Monitoring.Dashboard.DTOs;

namespace ONEVO.Application.Features.Monitoring.Dashboard.Services;

public static class MonitoringDashboardStatusService
{
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(5);

    public static MonitoringEmployeeStatus ResolveStatus(
        DateTimeOffset? latestCapturedAt,
        bool? isIdle,
        DateTimeOffset now)
    {
        if (latestCapturedAt is null || isIdle is null)
            return MonitoringEmployeeStatus.Offline;

        if (latestCapturedAt.Value < now.Subtract(FreshnessWindow))
            return MonitoringEmployeeStatus.Offline;

        return isIdle.Value
            ? MonitoringEmployeeStatus.Idle
            : MonitoringEmployeeStatus.Active;
    }

    public static MonitoringDashboardSummaryDto Summarize(
        IEnumerable<MonitoringEmployeeDashboardItemDto> items)
    {
        var list = items.ToList();
        var scored = list
            .Where(i => i.ActivityScore is not null)
            .Select(i => i.ActivityScore!.Value)
            .ToList();

        var averageScore = scored.Count == 0
            ? (decimal?)null
            : Math.Round(scored.Average(), 2);

        return new MonitoringDashboardSummaryDto(
            TotalEmployees: list.Count,
            ActiveEmployees: list.Count(i => i.Status == MonitoringEmployeeStatus.Active),
            IdleEmployees: list.Count(i => i.Status == MonitoringEmployeeStatus.Idle),
            OfflineEmployees: list.Count(i => i.Status == MonitoringEmployeeStatus.Offline),
            AttentionNeededEmployees: list.Count(i => i.Alerts.Count > 0),
            AverageActivityScore: averageScore);
    }
}
