using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

public sealed class ActivityLiveDaySummary(
    IActivitySnapshotRepository snapshots,
    IAppUsageSnapshotRepository appUsage,
    IMeetingSignalRepository meetings) : IActivityLiveDaySummary
{
    public async Task<ActivityDailySummaryDto?> ComposeAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        var daySnapshots = await snapshots.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);
        var dayAppUsage = await appUsage.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);
        var dayMeetings = await meetings.GetAllByEmployeeDateAsync(tenantId, employeeId, date, ct);
        // App-usage samples are enough for the attendance drawer's "Apps used" list.
        // Activity snapshots are optional — a day can have foreground-app time with no keyboard/mouse rows.
        if (daySnapshots.Count == 0 && dayAppUsage.Count == 0 && dayMeetings.Count == 0)
            return null;
        var summary = ActivityDailySummaryAggregator.Aggregate(
            tenantId,
            employeeId,
            date,
            daySnapshots,
            DateTimeOffset.UtcNow,
            appUsageSnapshots: dayAppUsage,
            meetingSignals: dayMeetings);

        return GetActivityDailySummaryQueryHandler.Map(summary);
    }
}
