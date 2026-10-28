using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Meeting hours (2026-10-26 design, Part A), run by the hourly meeting job under its lease and the tenant context:
/// every occurrence that has ended is stamped once with its person-hours (<see cref="MeetingPersonHours"/>) as the
/// task's estimated and completed hours, cascaded up its current module chain to the project, in one transaction
/// per occurrence. Recurring slots of the last <see cref="Lookback"/> that were never materialized are materialized first.
/// </summary>
public sealed class MeetingHoursStamper
{
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(7);
    private const int BatchSize = 200;

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IJobLeaseService _leases;
    private readonly ILogger<MeetingHoursStamper> _logger;

    public MeetingHoursStamper(IMeetingRepository meetings, IMeetingOccurrenceWriter writer, IMeetingOccurrenceMaterializer materializer,
        IMeetingRecurrence recurrence, IObjectiveRepository objectives, IProjectRepository projects, IUnitOfWork unitOfWork,
        IDateTimeProvider clock, IJobLeaseService leases, ILogger<MeetingHoursStamper> logger)
    {
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _recurrence = recurrence;
        _objectives = objectives;
        _projects = projects;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _leases = leases;
        _logger = logger;
    }

    /// <summary>Stamps one tenant (the caller holds the job lease and the tenant context). The lease is renewed after
    /// every series and occurrence, so a long first run keeps it; once it is lost the run stops (another instance may
    /// hold the job now) and says so.</summary>
    public async Task<MeetingHoursStampRun> StampTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        if (!await MaterializeEndedSlotsAsync(tenantId, now, ct))
            return new MeetingHoursStampRun(0, LeaseLost: true);

        var stamped = 0;
        var failed = new HashSet<Guid>();
        while (true)
        {
            var batch = (await _meetings.ListEndedUnstampedAsync(tenantId, now, BatchSize + failed.Count, ct))
                .Where(d => !failed.Contains(d.TaskId)).ToList();
            if (batch.Count == 0)
                return new MeetingHoursStampRun(stamped, LeaseLost: false);

            foreach (var details in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (await _unitOfWork.ExecuteInTransactionAsync(innerCt => StampAsync(tenantId, details, now, innerCt), ct))
                        stamped++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _unitOfWork.ClearTracking();
                    failed.Add(details.TaskId);
                    _logger.LogWarning(ex, "Stamping meeting hours failed for tenant {TenantId}, occurrence {TaskId}; skipping.",
                        tenantId, details.TaskId);
                }
                if (!await RenewLeaseAsync(ct))
                    return new MeetingHoursStampRun(stamped, LeaseLost: true);
            }
        }
    }

    private async Task<bool> StampAsync(Guid tenantId, MeetingDetails details, DateTimeOffset now, CancellationToken ct)
    {
        // One job run shares a DbContext: drop what earlier stamps tracked so every attempt (a retry included) reads
        // the current row, module and project, and never writes back a value a user has since changed.
        _unitOfWork.ClearTracking();
        var tracked = await _meetings.GetDetailsTrackedAsync(tenantId, details.TaskId, ct);
        if (tracked is null || tracked.HoursStampedAt is not null)
            return false;
        var series = await _meetings.GetSeriesAsync(tenantId, tracked.SeriesId, ct);
        var task = await _meetings.GetMeetingTaskTrackedAsync(tenantId, tracked.TaskId, ct);

        if (series is not null && task is not null)
        {
            var cancelled = tracked.CancelledAt is not null || MeetingStatusCalculator.CancelledWithSeries(tracked, series);
            var attendees = EffectiveAttendees.Compute(
                await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct),
                await _meetings.ListSeriesGuestsAsync(tenantId, series.Id, ct),
                await _meetings.ListOverridesAsync(tenantId, [tracked.TaskId], ct));
            var hours = MeetingPersonHours.Compute(MeetingPersonHours.BillableMinutes(tracked.StartAt, tracked.EndAt, tracked.IsAllDay),
                series.OrganizerEmployeeId, attendees, cancelled);

            var delta = hours - task.CompletedHours;
            task.EstimatedHours = hours;
            task.CompletedHours = hours;
            if (await _objectives.GetTrackedByIdForTenantAsync(tenantId, task.ObjectiveId, ct) is { } module)
                await CompletedHoursCascade.ApplyAsync(_objectives, _projects, tenantId, module, delta, ct);
        }

        tracked.HoursStampedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> RenewLeaseAsync(CancellationToken ct)
    {
        if (await _leases.RenewAsync(MeetingOccurrenceGenerator.JobName, MeetingOccurrenceGenerator.LeaseTtl, ct))
            return true;
        _logger.LogWarning("Meeting hours stamping lost the job lease; stopping.");
        return false;
    }

    /// <summary>A recurring slot that ended within <see cref="Lookback"/> but was never generated (e.g. while the job was
    /// off) gets its occurrence now, so it has a task to carry its hours - also in a series cancelled since, for the
    /// meetings that took place before the cancel. False when the job lease was lost.</summary>
    private async Task<bool> MaterializeEndedSlotsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var seriesId in await _meetings.ListRecurringSeriesIdsLiveSinceAsync(tenantId, now - Lookback, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
                {
                    await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                    var series = await _meetings.GetSeriesAsync(tenantId, seriesId, innerCt);   // re-read under the lock
                    if (series is null || series.RecurrenceRule is null)
                        return 0;
                    // A cancelled series held its meetings up to the cancel.
                    var heldUntil = series.CancelledAt is { } cancelledAt && cancelledAt < now ? cancelledAt : now;
                    foreach (var key in _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now - Lookback, heldUntil))
                    {
                        if (key.AddMinutes(series.DurationMinutes) > heldUntil
                            || await _meetings.GetDetailsAsync(tenantId, series.Id, key, innerCt) is not null)
                            continue;
                        await _materializer.EnsureAsync(tenantId, series, key, series.CreatedById, innerCt);
                    }
                    return 0;
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _unitOfWork.ClearTracking();
                _logger.LogWarning(ex, "Materializing ended meeting slots failed for tenant {TenantId}, series {SeriesId}; skipping.",
                    tenantId, seriesId);
            }
            if (!await RenewLeaseAsync(ct))
                return false;
        }
        return true;
    }
}

/// <summary>One tenant's stamping: how many occurrences were stamped, and whether the job lease was lost (stop the run).</summary>
public sealed record MeetingHoursStampRun(int Stamped, bool LeaseLost);
