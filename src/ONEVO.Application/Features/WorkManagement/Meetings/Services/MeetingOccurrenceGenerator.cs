using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Spec 5.5 generator: keeps every live recurring series materialized for the next 7 days (D13). Per series, one
/// transaction under the series lock that re-reads the series before writing (the spec 5.6 contract), so a split that
/// committed meanwhile is respected. No emails. One series failing never stops the others; a lost lease stops the run.
/// </summary>
public sealed class MeetingOccurrenceGenerator
{
    public const string JobName = "meetings.occurrence-generator";
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(10);

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IJobLeaseService _leases;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<MeetingOccurrenceGenerator> _logger;

    public MeetingOccurrenceGenerator(IMeetingRepository meetings, IMeetingOccurrenceWriter writer, IMeetingOccurrenceMaterializer materializer,
        IMeetingRecurrence recurrence, IJobLeaseService leases, IUnitOfWork unitOfWork, IDateTimeProvider clock,
        ILogger<MeetingOccurrenceGenerator> logger)
    {
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _recurrence = recurrence;
        _leases = leases;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Every live recurring series of one tenant (the caller holds the job lease and the tenant context).
    /// False when the lease was lost: the run must stop.</summary>
    public async Task<bool> RunForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        foreach (var seriesId in await _meetings.ListActiveRecurringSeriesIdsAsync(tenantId, ct))
        {
            ct.ThrowIfCancellationRequested();
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await GenerateSeriesAsync(tenantId, seriesId, ct);
                    break;
                }
                catch (MeetingOccurrenceRaceLostException) when (attempt == 1)
                {
                    _unitOfWork.ClearTracking();   // a racing writer won a slot; the retry finds it taken
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _unitOfWork.ClearTracking();
                    _logger.LogWarning(ex, "Meeting occurrence generation failed for tenant {TenantId}, series {SeriesId}; skipping.", tenantId, seriesId);
                    break;
                }
            }
            if (!await _leases.RenewAsync(JobName, LeaseTtl, ct))
            {
                _logger.LogWarning("Meeting occurrence generator lost its lease; stopping after series {SeriesId}.", seriesId);
                return false;
            }
        }
        return true;
    }

    /// <summary>Materializes the series' missing occurrences in [now, now + 7 days); the number created.</summary>
    public async Task<int> GenerateSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct)
        => await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
            var series = await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt);   // re-read under the lock
            if (series is null || series.CancelledAt is not null || series.RecurrenceRule is null)
                return 0;

            var now = _clock.UtcNow;
            var created = 0;
            foreach (var key in _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now, now + MeetingSeriesFactory.Window))
            {
                if (await _meetings.GetDetailsAsync(tenantId, series.Id, key, innerCt) is not null)
                    continue;
                if ((await _materializer.EnsureAsync(tenantId, series, key, series.CreatedById, innerCt)).IsSuccess)
                    created++;
            }
            return created;
        }, ct);
}
