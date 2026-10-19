using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>How one link's attendance sync went.</summary>
public enum MeetingAttendanceSyncOutcome
{
    /// <summary>The provider answered (with or without sessions) and the due occurrences moved on.</summary>
    Synced,
    /// <summary>Nothing was due, or the link is gone.</summary>
    NothingDue,
    /// <summary>Another sync of the link holds its lock.</summary>
    Busy,
    /// <summary>The provider could not be read (no connection, sign-in needed, or an API failure); recorded on the occurrences.</summary>
    ProviderError,
}

/// <summary>
/// Per-occurrence attendance for series-owned links (spec 5.8). One link is reused by every occurrence of a series, so each
/// sync lists the provider's sessions around the due occurrences, records each session once, assigns it to its occurrence
/// (<see cref="MeetingAttendanceMatcher"/>), upserts its records, and moves each due occurrence along its checkpoints:
/// pending -> provisional -> synced, or attendance_unavailable / sync_failed after the one automatic retry. A status never
/// leaves synced. Calendar-event links keep the legacy one-shot sync.
/// </summary>
public sealed class MeetingAttendanceSyncService
{
    private readonly IMeetingAttendanceRepository _attendance;
    private readonly IMeetingLinkRepository _links;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingAccessResolver _access;
    private readonly IExternalCalendarConnectionRepository _connections;
    private readonly ICalendarConnectionTokenProvider _tokens;
    private readonly IReadOnlyList<IMeetingAttendanceSessionClient> _clients;
    private readonly ICalendarNotificationSender _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<MeetingsOptions> _options;
    private readonly IDateTimeProvider _clock;

    public MeetingAttendanceSyncService(IMeetingAttendanceRepository attendance, IMeetingLinkRepository links, IMeetingRepository meetings,
        IMeetingAccessResolver access, IExternalCalendarConnectionRepository connections, ICalendarConnectionTokenProvider tokens,
        IEnumerable<IMeetingAttendanceSessionClient> clients, ICalendarNotificationSender notifications, IUnitOfWork unitOfWork,
        IOptions<MeetingsOptions> options, IDateTimeProvider clock)
    {
        _attendance = attendance;
        _links = links;
        _meetings = meetings;
        _access = access;
        _connections = connections;
        _tokens = tokens;
        _clients = clients.ToList();
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _options = options;
        _clock = clock;
    }

    private MeetingAttendanceOptions Settings => _options.Value.Attendance;

    /// <summary>The series-owned links with an occurrence whose check is due.</summary>
    public Task<IReadOnlyList<Guid>> ListDueLinksAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
        => _attendance.ListDueLinkIdsAsync(tenantId, now, ct);

    /// <summary>Syncs the link's due occurrences in one transaction under the link's sync lock (the provider call included).</summary>
    public Task<MeetingAttendanceSyncOutcome> SyncLinkAsync(Guid tenantId, Guid linkId, DateTimeOffset now, CancellationToken ct)
        => _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (!await _attendance.TryLockLinkSyncAsync(tenantId, linkId, innerCt))
                return MeetingAttendanceSyncOutcome.Busy;
            var link = await _links.GetAsync(tenantId, linkId, innerCt);
            var due = await _attendance.ListDueOccurrencesTrackedAsync(tenantId, linkId, now, innerCt);
            if (link is null || due.Count == 0)
                return MeetingAttendanceSyncOutcome.NothingDue;
            var outcome = await SyncCoreAsync(tenantId, link, due, recheck: false, now, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return outcome;
        }, ct);

    /// <summary>POST .../attendance/recheck: the organizer syncs one occurrence now, up to LateRecheckWindow after it ended. A
    /// recheck past the last checkpoint that finds sessions settles the occurrence as synced - also from
    /// attendance_unavailable or sync_failed.</summary>
    public async Task<Result<MeetingAttendanceStatusResponse>> RecheckAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId,
        DateTimeOffset originalStart, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingAttendanceStatusResponse>(_options.Value) is { } blocked)
            return blocked;
        if (await MeetingGuard.OrganizerOnlyAsync<MeetingAttendanceStatusResponse>(_access, tenantId, callerEmployeeId, seriesId, originalStart, ct)
            is { } denied)
            return denied;
        var details = await _meetings.GetDetailsAsync(tenantId, seriesId, originalStart.ToUniversalTime(), ct);
        var now = _clock.UtcNow;
        if (details?.CalendarEventMeetingId is not Guid linkId || details.CancelledAt is not null || details.StartAt > now)
            return Result<MeetingAttendanceStatusResponse>.Failure("This meeting has no attendance to check yet.", 409,
                MeetingErrorCodes.AttendanceNotApplicable);
        if (now > details.EndAt + Settings.LateRecheckWindow)
            return Result<MeetingAttendanceStatusResponse>.Failure("Attendance can be rechecked for up to 7 days after the meeting.", 409,
                MeetingErrorCodes.AttendanceRecheckExpired);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (!await _attendance.TryLockLinkSyncAsync(tenantId, linkId, innerCt))
                return Result<MeetingAttendanceStatusResponse>.Failure("Attendance is being checked right now. Try again shortly.", 409,
                    MeetingErrorCodes.AttendanceCheckInProgress);
            var link = await _links.GetAsync(tenantId, linkId, innerCt);
            var tracked = await _meetings.GetDetailsTrackedAsync(tenantId, details.TaskId, innerCt);
            if (link is null || tracked is null)
                return MeetingReadService.NotFound<MeetingAttendanceStatusResponse>();
            await SyncCoreAsync(tenantId, link, [tracked], recheck: true, now, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<MeetingAttendanceStatusResponse>.Success(new MeetingAttendanceStatusResponse(
                tracked.AttendanceStatus ?? MeetingAttendanceStatuses.Pending, tracked.AttendanceNextCheckAt, tracked.AttendanceFinalizedAt,
                tracked.AttendanceLastError));
        }, ct);
    }

    /// <summary>The first-checkpoint state of an occurrence that just got (or lost) a link.</summary>
    public static (string Status, DateTimeOffset? NextCheckAt) Initial(Guid? linkId, DateTimeOffset endAt, MeetingAttendanceOptions settings)
        => linkId is null ? (MeetingAttendanceStatuses.NotApplicable, null) : (MeetingAttendanceStatuses.Pending, endAt + settings.Checkpoints[0]);

    private async Task<MeetingAttendanceSyncOutcome> SyncCoreAsync(Guid tenantId, CalendarEventMeeting link, IReadOnlyList<MeetingDetails> targets,
        bool recheck, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(link.ExternalMeetingId))
        {
            // The provider meeting never existed (the link failed): there is no attendance to read.
            foreach (var target in targets.Where(t => t.AttendanceStatus != MeetingAttendanceStatuses.Synced))
            {
                target.AttendanceStatus = MeetingAttendanceStatuses.NotApplicable;
                target.AttendanceNextCheckAt = null;
            }
            return MeetingAttendanceSyncOutcome.Synced;
        }

        var before = Settings.MatchWindowBefore;
        var from = targets.Min(t => t.StartAt) - before;
        var to = targets.Max(t => t.EndAt);
        var (sessions, error) = await FetchAsync(tenantId, link, from, to, ct);

        if (error is null)
            await RecordAsync(tenantId, link, sessions!, from, to, ct);
        var assigned = error is null
            ? (await _attendance.ListSessionsTrackedAsync(tenantId, link.Id, ct)).Where(s => s.OccurrenceTaskId is not null)
                .Select(s => s.OccurrenceTaskId!.Value).ToHashSet()
            : [];

        var settled = new List<MeetingDetails>();
        foreach (var target in targets)
        {
            var previous = target.AttendanceStatus;
            if (recheck)
                AdvanceOnRecheck(target, error, assigned.Contains(target.TaskId), now);
            else
                Advance(target, error, assigned.Contains(target.TaskId), now);
            // It settled now (or settled differently, e.g. a late report after attendance_unavailable).
            if (IsFinal(target.AttendanceStatus) && target.AttendanceStatus != previous)
                settled.Add(target);
        }
        await QueueEvaluationAsync(tenantId, settled, ct);
        return error is null ? MeetingAttendanceSyncOutcome.Synced : MeetingAttendanceSyncOutcome.ProviderError;
    }

    private static bool IsFinal(string? status)
        => status is MeetingAttendanceStatuses.Synced or MeetingAttendanceStatuses.Unavailable or MeetingAttendanceStatuses.SyncFailed;

    /// <summary>The checkpoint machine (spec 5.8). <c>AttendanceCheckpoint</c> k is the scheduled check: 0..n-1 are the
    /// configured checkpoints after the end, n is the one automatic retry after an unavailable or failed last checkpoint.</summary>
    private void Advance(MeetingDetails d, string? error, bool hasSessions, DateTimeOffset now)
    {
        if (d.AttendanceStatus == MeetingAttendanceStatuses.Synced)
            return;   // never moves back
        var checkpoints = Settings.Checkpoints;
        var last = checkpoints.Length - 1;
        var k = d.AttendanceCheckpoint;

        if (error is not null)
        {
            d.AttendanceLastError = error;
            if (k < last)
                Schedule(d, k + 1);
            else if (k == last)
            {
                d.AttendanceStatus = MeetingAttendanceStatuses.SyncFailed;
                d.AttendanceCheckpoint = last + 1;
                d.AttendanceNextCheckAt = now + Settings.FailedRetryAfter;
            }
            else
                Finalize(d, MeetingAttendanceStatuses.SyncFailed, now);
            return;
        }

        d.AttendanceLastError = null;
        if (hasSessions)
        {
            if (k < last)
            {
                d.AttendanceStatus = MeetingAttendanceStatuses.Provisional;
                Schedule(d, k + 1);
            }
            else
                Finalize(d, MeetingAttendanceStatuses.Synced, now);
            return;
        }

        if (k < last)
            Schedule(d, k + 1);   // stays pending
        else if (k == last)
        {
            d.AttendanceStatus = MeetingAttendanceStatuses.Unavailable;
            d.AttendanceCheckpoint = last + 1;
            d.AttendanceNextCheckAt = now + Settings.FailedRetryAfter;
        }
        else
            Finalize(d, MeetingAttendanceStatuses.Unavailable, now);
    }

    /// <summary>A manual recheck: sessions settle the occurrence once the last checkpoint has passed (provisional before it);
    /// no sessions or an error change nothing but the error. The schedule is left as it is.</summary>
    private void AdvanceOnRecheck(MeetingDetails d, string? error, bool hasSessions, DateTimeOffset now)
    {
        if (d.AttendanceStatus == MeetingAttendanceStatuses.Synced)
            return;
        if (error is not null)
        {
            d.AttendanceLastError = error;
            return;
        }
        d.AttendanceLastError = null;
        if (!hasSessions)
            return;
        if (now >= d.EndAt + Settings.Checkpoints[^1])
            Finalize(d, MeetingAttendanceStatuses.Synced, now);
        else
            d.AttendanceStatus = MeetingAttendanceStatuses.Provisional;
    }

    private void Schedule(MeetingDetails d, int checkpoint)
    {
        d.AttendanceCheckpoint = checkpoint;
        d.AttendanceNextCheckAt = d.EndAt + Settings.Checkpoints[checkpoint];
    }

    private static void Finalize(MeetingDetails d, string status, DateTimeOffset now)
    {
        d.AttendanceStatus = status;
        d.AttendanceFinalizedAt = now;
        d.AttendanceNextCheckAt = null;
    }

    /// <summary>The provider's sessions starting in [from, to], or why they could not be read.</summary>
    private async Task<(IReadOnlyList<ProviderAttendanceSession>? Sessions, string? Error)> FetchAsync(Guid tenantId, CalendarEventMeeting link,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var client = _clients.FirstOrDefault(c => c.Provider == link.Provider);
        var connection = await _connections.GetByIdForTenantAsync(tenantId, link.ExternalCalendarConnectionId, ct);
        if (connection is null)
            return (null, CalendarAttendanceSyncErrors.NotConnected);
        if (client is null)
            return (null, CalendarAttendanceSyncErrors.ProviderError);
        var token = await _tokens.GetFreshAccessTokenAsync(connection,
            link.Provider == CalendarEventMeetingProviders.Zoom ? "zoom" : "microsoft", ct);
        if (token is null)
            return (null, CalendarAttendanceSyncErrors.ReauthRequired);
        try
        {
            return (await client.ListSessionsAsync(token, link.ExternalMeetingId, from, to, ct), null);
        }
        catch (MeetingProviderException ex)
        {
            return (null, ex.Reason ?? CalendarAttendanceSyncErrors.ProviderError);
        }
    }

    /// <summary>Upserts the sessions on (link, provider session id), assigns the unassigned ones, and upserts every session's
    /// records on (session, participant key, joined at).</summary>
    private async Task RecordAsync(Guid tenantId, CalendarEventMeeting link, IReadOnlyList<ProviderAttendanceSession> fetched,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var known = (await _attendance.ListSessionsTrackedAsync(tenantId, link.Id, ct)).ToDictionary(s => s.ProviderSessionId, StringComparer.Ordinal);
        var rows = new Dictionary<string, MeetingAttendanceSession>(StringComparer.Ordinal);
        foreach (var session in fetched)
        {
            if (!known.TryGetValue(session.ProviderSessionId, out var row))
            {
                row = new MeetingAttendanceSession
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = link.Id, ProviderSessionId = session.ProviderSessionId,
                    SessionStart = session.Start, CreatedAt = now,
                };
                _attendance.AddSession(row);
                known[session.ProviderSessionId] = row;
            }
            row.SessionStart = session.Start;
            row.SessionEnd = session.End ?? row.SessionEnd;
            rows[session.ProviderSessionId] = row;
        }

        // Every session still unassigned - also one from an earlier sync - gets its occurrence if one fits now.
        var unassigned = known.Values.Where(s => s.OccurrenceTaskId is null).ToList();
        if (unassigned.Count > 0)
        {
            var before = Settings.MatchWindowBefore;
            var windowFrom = unassigned.Min(s => s.SessionStart) - before;
            var windowTo = unassigned.Max(s => s.SessionStart) + before;
            var occurrences = await _attendance.ListLinkOccurrencesAsync(tenantId, link.Id, Min(from, windowFrom), Max(to, windowTo), ct);
            var assignment = MeetingAttendanceMatcher.Assign(unassigned.Select(s => (s.ProviderSessionId, s.SessionStart)).ToList(),
                occurrences.Select(o => (o.TaskId, o.StartAt, o.EndAt)).ToList(), before);
            foreach (var session in unassigned)
                if (assignment.TryGetValue(session.ProviderSessionId, out var taskId))
                    session.OccurrenceTaskId = taskId;
        }

        var sessionIds = rows.Values.Select(r => r.Id).ToList();
        var existing = (await _attendance.ListRecordsTrackedAsync(tenantId, sessionIds, ct))
            .ToDictionary(r => (r.AttendanceSessionId!.Value, r.ParticipantKey ?? string.Empty, r.JoinedAt.ToUniversalTime()));
        var people = await _attendance.ResolveEmployeesByEmailAsync(tenantId,
            fetched.SelectMany(s => s.Records).Select(r => r.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Cast<string>().ToList(), ct);
        foreach (var session in fetched)
        {
            var row = rows[session.ProviderSessionId];
            foreach (var record in session.Records)
            {
                Guid? employeeId = record.Email is { } email && people.TryGetValue(email.Trim().ToLowerInvariant(), out var id) ? id : null;
                var duration = record.LeftAt is { } left ? (int?)Math.Max(0, (int)(left - record.JoinedAt).TotalSeconds) : null;
                if (existing.TryGetValue((row.Id, record.ParticipantKey, record.JoinedAt.ToUniversalTime()), out var seen))
                {
                    seen.LeftAt = record.LeftAt ?? seen.LeftAt;
                    seen.DurationSeconds = duration ?? seen.DurationSeconds;
                    seen.EmployeeId ??= employeeId;
                    seen.OccurrenceTaskId = row.OccurrenceTaskId;
                    continue;
                }
                var added = new CalendarEventMeetingAttendance
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = link.Id, EmployeeId = employeeId,
                    ExternalParticipantName = record.Name, ExternalParticipantEmail = record.Email, JoinedAt = record.JoinedAt, LeftAt = record.LeftAt,
                    DurationSeconds = duration, OccurrenceTaskId = row.OccurrenceTaskId, AttendanceSessionId = row.Id,
                    ParticipantKey = record.ParticipantKey, CreatedAt = now,
                };
                _attendance.AddRecord(added);
                existing[(row.Id, record.ParticipantKey, record.JoinedAt.ToUniversalTime())] = added;
            }
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Plan C2 post-meeting trigger: the effective employee attendees of each occurrence that just settled.</summary>
    private async Task QueueEvaluationAsync(Guid tenantId, IReadOnlyList<MeetingDetails> settled, CancellationToken ct)
    {
        var employees = new HashSet<Guid>();
        foreach (var group in settled.GroupBy(d => d.SeriesId))
        {
            var participants = await _meetings.ListSeriesParticipantsAsync(tenantId, group.Key, ct);
            var guests = await _meetings.ListSeriesGuestsAsync(tenantId, group.Key, ct);
            var overrides = await _meetings.ListOverridesAsync(tenantId, group.Select(d => d.TaskId).ToList(), ct);
            foreach (var occurrence in group)
                foreach (var attendee in EffectiveAttendees.Compute(participants, guests,
                             overrides.Where(o => o.OccurrenceTaskId == occurrence.TaskId).ToList()))
                    if (attendee.EmployeeId is Guid id)
                        employees.Add(id);
        }
        if (employees.Count > 0)
            await _notifications.QueueIncidentEvaluationAsync(tenantId, employees, ct);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
