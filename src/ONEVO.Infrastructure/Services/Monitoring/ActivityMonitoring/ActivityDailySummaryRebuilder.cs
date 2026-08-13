using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

public sealed class ActivityDailySummaryRebuilder : IActivityDailySummaryRebuilder
{
    private readonly IActivitySnapshotRepository _snapshots;
    private readonly IActivityDailySummaryRepository _summaries;
    private readonly IMonitoringReportTimeZoneResolver _timeZoneResolver;
    private readonly IDateTimeProvider _clock;

    public ActivityDailySummaryRebuilder(
        IActivitySnapshotRepository snapshots,
        IActivityDailySummaryRepository summaries,
        IMonitoringReportTimeZoneResolver timeZoneResolver,
        IDateTimeProvider clock)
    {
        _snapshots = snapshots;
        _summaries = summaries;
        _timeZoneResolver = timeZoneResolver;
        _clock = clock;
    }

    public async Task RebuildAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        var timeZone = await _timeZoneResolver.ResolveAsync(tenantId, employeeId, ct);
        var (fromUtc, toUtc) = MonitoringReportDateRange.ToUtcBounds(date, timeZone);

        var daySnapshots = await _snapshots.GetAllByEmployeeCapturedRangeAsync(
            tenantId, employeeId, fromUtc, toUtc, ct);

        if (daySnapshots.Count == 0)
            return;

        var summary = ActivityDailySummaryAggregator.Aggregate(
            tenantId, employeeId, date, daySnapshots, _clock.UtcNow);

        await _summaries.UpsertAsync(summary, ct);
    }
}
