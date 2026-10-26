using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingRepository : IMeetingRepository
{
    // Meeting tasks are hidden from every ordinary task query; only meeting reads that already passed MeetingVisibility
    // or the access resolver ignore that one filter (tenant scoping stays).
    private static readonly string[] ExcludeMeetingsOnly = [WorkTaskQueryFilters.ExcludeMeetings];

    // Also drops the soft-delete half of TenantScope: only for placeholder re-pointing, which must reach removed meeting
    // tasks too because a status stays undeletable while any task - deleted or not - references it. Tenant re-applied by hand.
    private static readonly string[] ExcludeMeetingsAndTenantScope = [WorkTaskQueryFilters.ExcludeMeetings, WorkTaskQueryFilters.TenantScope];

    private readonly ApplicationDbContext _db;

    public EfMeetingRepository(ApplicationDbContext db) => _db = db;

    public async Task<MeetingSeries?> GetSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

    public async Task<MeetingDetails?> GetDetailsAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await _db.MeetingDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.OriginalStart == key, ct);
    }

    public async Task<IReadOnlyList<CalendarEventParticipant>> ListSeriesParticipantsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.MeetingSeriesId == seriesId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventGuest>> ListSeriesGuestsAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.CalendarEventGuests.AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.MeetingSeriesId == seriesId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingOccurrenceAttendee>> ListOverridesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> occurrenceTaskIds, CancellationToken ct = default)
        => occurrenceTaskIds.Count == 0
            ? []
            : await _db.MeetingOccurrenceAttendees.AsNoTracking()
                .Where(a => a.TenantId == tenantId && occurrenceTaskIds.Contains(a.OccurrenceTaskId))
                .ToListAsync(ct);

    public async Task<MeetingSeries?> FindSeriesByApprovalRequestAsync(Guid tenantId, Guid approvalRequestId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ApprovalRequestId == approvalRequestId, ct);

    public async Task<IReadOnlyList<Guid>> ListActiveRecurringSeriesIdsAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.RecurrenceRule != null && s.CancelledAt == null)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).Select(s => s.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingSeries>> ListSeriesAsync(Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking().Where(s => s.TenantId == tenantId && seriesIds.Contains(s.Id)).ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventParticipant>> ListParticipantsForSeriesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.MeetingSeriesId != null && seriesIds.Contains(p.MeetingSeriesId.Value))
            .ToListAsync(ct);

    public async Task<MeetingOccurrenceRow?> GetOccurrenceByTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await Rows(_db.MeetingDetails.Where(d => d.TenantId == tenantId && d.TaskId == taskId)).FirstOrDefaultAsync(ct);

    public async Task<MeetingOccurrenceRow?> GetOccurrenceAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await Rows(_db.MeetingDetails.Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.OriginalStart == key))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingOccurrenceRow>> ListOrganizerOccurrencesAsync(Guid tenantId, Guid organizerEmployeeId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        return await Rows(_db.MeetingDetails
                .Where(d => d.TenantId == tenantId && d.StartAt < toUtc && d.EndAt > fromUtc
                    && _db.MeetingSeries.Any(s => s.Id == d.SeriesId && s.OrganizerEmployeeId == organizerEmployeeId))
                .OrderBy(d => d.StartAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingOccurrenceRow>> ListOccurrencesByTaskIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default)
        => taskIds.Count == 0 ? [] : await Rows(_db.MeetingDetails.Where(d => d.TenantId == tenantId && taskIds.Contains(d.TaskId))).ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingSeries>> ListSeriesForAttendeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.MeetingSeries.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.CancelledAt == null && (s.OrganizerEmployeeId == employeeId
                || _db.CalendarEventParticipants.Any(p => p.MeetingSeriesId == s.Id && p.EmployeeId == employeeId && !p.IsDeleted)))
            .ToListAsync(ct);

    public async Task<IReadOnlySet<(Guid SeriesId, DateTimeOffset OriginalStart)>> ListTakenSlotsAsync(Guid tenantId,
        IReadOnlyCollection<Guid> seriesIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (seriesIds.Count == 0)
            return new HashSet<(Guid, DateTimeOffset)>();
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        var materialized = await _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && seriesIds.Contains(d.SeriesId) && d.OriginalStart >= fromUtc && d.OriginalStart < toUtc)
            .Select(d => new { d.SeriesId, d.OriginalStart }).ToListAsync(ct);
        var suppressed = await _db.MeetingOccurrenceSuppressions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && seriesIds.Contains(s.SeriesId) && s.OriginalStart >= fromUtc && s.OriginalStart < toUtc)
            .Select(s => new { s.SeriesId, s.OriginalStart }).ToListAsync(ct);
        return materialized.Concat(suppressed).Select(k => (k.SeriesId, k.OriginalStart.ToUniversalTime())).ToHashSet();
    }

    public async Task<IReadOnlyList<MeetingOccurrenceRow>> ListStartingBetweenAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        return await Rows(_db.MeetingDetails
                .Where(d => d.TenantId == tenantId && d.CancelledAt == null && d.StartAt >= fromUtc && d.StartAt < toUtc)
                .OrderBy(d => d.StartAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingOccurrenceRow>> ListVisibleAsync(Guid tenantId, Guid callerEmployeeId, DateTimeOffset from,
        DateTimeOffset to, Guid? projectId, Guid? seriesId, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var occurrences = MeetingVisibility.ForCaller(_db.MeetingDetails, _db, callerEmployeeId)
            .Where(d => d.TenantId == tenantId && d.StartAt < toUtc && d.EndAt > fromUtc);
        if (seriesId is Guid series)
            occurrences = occurrences.Where(d => d.SeriesId == series);
        if (projectId is Guid project)
            occurrences = occurrences.Where(d => _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly)
                .Any(t => t.Id == d.TaskId && t.ProjectId == project));
        return await Rows(occurrences.OrderBy(d => d.StartAt)).ToListAsync(ct);
    }

    public async Task<bool> IsOccurrenceRemovedAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default)
        => !await _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly).AnyAsync(t => t.TenantId == tenantId && t.Id == occurrenceTaskId, ct);

    public async Task<bool> IsSuppressedAsync(Guid tenantId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct = default)
    {
        var key = originalStart.ToUniversalTime();
        return await _db.MeetingOccurrenceSuppressions.AnyAsync(
            s => s.TenantId == tenantId && s.SeriesId == seriesId && s.OriginalStart == key, ct);
    }

    // The task side of an occurrence: meeting tasks are hidden from every ordinary task query (ExcludeMeetings), so this
    // join - and only meeting reads that already passed MeetingVisibility or the access resolver - ignores that filter.
    private IQueryable<MeetingOccurrenceRow> Rows(IQueryable<MeetingDetails> occurrences)
        => occurrences.AsNoTracking().Join(
            _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly),
            d => d.TaskId, t => t.Id,
            (d, t) => new MeetingOccurrenceRow(d, t.ShortId, t.Title, t.Description, t.ProjectId, t.ObjectiveId));

    public async Task<MeetingSeries?> GetSeriesTrackedAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
        => await _db.MeetingSeries.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == seriesId, ct);

    public async Task<MeetingDetails?> GetDetailsTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.MeetingDetails.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.TaskId == taskId, ct);

    public async Task<IReadOnlyList<MeetingDetails>> ListEndedUnstampedAsync(Guid tenantId, DateTimeOffset now, int take,
        CancellationToken ct = default)
        => await _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EndAt <= now && d.HoursStampedAt == null)
            .OrderBy(d => d.EndAt).ThenBy(d => d.TaskId).Take(take).ToListAsync(ct);

    public async Task<WorkTask?> GetMeetingTaskTrackedAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId && t.TaskKind == WorkTaskKinds.Meeting, ct);

    public async Task<IReadOnlyList<(MeetingDetails Details, WorkTask Task)>> ListTemplateOccurrencesTrackedAsync(
        Guid tenantId, Guid seriesId, DateTimeOffset after, CancellationToken ct = default)
    {
        var afterUtc = after.ToUniversalTime();
        var rows = await _db.MeetingDetails
            .Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.StartAt > afterUtc && !d.IsDetached && d.CancelledAt == null)
            .Join(_db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly), d => d.TaskId, t => t.Id, (d, t) => new { d, t })
            .OrderBy(x => x.d.StartAt)
            .ToListAsync(ct);
        return rows.Select(x => (x.d, x.t)).ToList();
    }

    public async Task<IReadOnlyList<MeetingDetails>> ListOccurrencesFromAsync(Guid tenantId, Guid seriesId, DateTimeOffset from, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        // A soft-deleted occurrence (split surplus) is gone: its row only keeps the key taken.
        return await _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.OriginalStart >= fromUtc
                && _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly).Any(t => t.Id == d.TaskId))
            .OrderBy(d => d.OriginalStart).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ListAttendeeEmployeeIdsInRangeAsync(Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        // The same window ListVisibleAsync reads for the engine; soft-deleted occurrences (split surplus) are gone.
        var occurrences = _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.StartAt < toUtc && d.EndAt > fromUtc
                && _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly).Any(t => t.Id == d.TaskId));

        var organizers = await _db.MeetingSeries.AsNoTracking()
            .Where(s => s.TenantId == tenantId && occurrences.Any(d => d.SeriesId == s.Id))
            .Select(s => s.OrganizerEmployeeId).ToListAsync(ct);
        var participants = await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.MeetingSeriesId != null && occurrences.Any(d => d.SeriesId == p.MeetingSeriesId))
            .Select(p => p.EmployeeId).ToListAsync(ct);
        var added = await _db.MeetingOccurrenceAttendees.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AttendeeKind == MeetingAttendeeKinds.Employee
                && a.Change == MeetingAttendeeChanges.Add && a.EmployeeId != null
                && occurrences.Any(d => d.TaskId == a.OccurrenceTaskId))
            .Select(a => a.EmployeeId!.Value).ToListAsync(ct);

        return organizers.Concat(participants).Concat(added).Distinct().ToList();
    }

    public async Task<IReadOnlyList<MeetingDetails>> ListNotStartedOccurrencesAsync(Guid tenantId, Guid seriesId, DateTimeOffset now, CancellationToken ct = default)
    {
        var nowUtc = now.ToUniversalTime();
        return await _db.MeetingDetails.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.SeriesId == seriesId && d.StartAt > nowUtc
                && _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly).Any(t => t.Id == d.TaskId))
            .OrderBy(d => d.OriginalStart).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MeetingTaskComplianceOccurrence>> ListEmployeeTaskMeetingsAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // The occurrences the employee effectively attends: a series participant (a removed one is filtered out)
        // without a "remove" override for that occurrence, or an occurrence "add". The task join ignores the meeting
        // filter (only this file may) and keeps the tenant filter, so a removed occurrence (a split surplus) is gone.
        var candidates = await (
            from d in _db.MeetingDetails.AsNoTracking()
            join t in _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly) on d.TaskId equals t.Id
            join m in _db.CalendarEventMeetings.AsNoTracking() on d.CalendarEventMeetingId equals m.Id
            where d.TenantId == tenantId
                  && t.TenantId == tenantId
                  && d.CancelledAt == null
                  && d.StartAt < toUtc && d.EndAt > fromUtc
                  && m.TenantId == tenantId && m.Status == CalendarEventMeetingStatuses.Active
                  && (_db.CalendarEventParticipants.Any(p => p.MeetingSeriesId == d.SeriesId && p.EmployeeId == employeeId && !p.IsDeleted)
                      ? !_db.MeetingOccurrenceAttendees.Any(a => a.OccurrenceTaskId == d.TaskId
                          && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == employeeId
                          && a.Change == MeetingAttendeeChanges.Remove)
                      : _db.MeetingOccurrenceAttendees.Any(a => a.OccurrenceTaskId == d.TaskId
                          && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == employeeId
                          && a.Change == MeetingAttendeeChanges.Add))
            orderby d.StartAt
            select new { d.TaskId, d.SeriesId, d.StartAt, d.EndAt, d.IsPrivate, t.Title, m.Provider })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        // The RSVP that decides a decline: the occurrence's own answer when the occurrence carries one, else the
        // series answer. Two batched reads, not one per occurrence, and only the employee's own rows - never the
        // other attendees of every series in the window.
        var seriesIds = candidates.Select(c => c.SeriesId).Distinct().ToList();
        var taskIds = candidates.Select(c => c.TaskId).ToList();
        var participants = await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId
                && p.MeetingSeriesId != null && seriesIds.Contains(p.MeetingSeriesId.Value))
            .ToListAsync(ct);
        var overrides = await _db.MeetingOccurrenceAttendees.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AttendeeKind == MeetingAttendeeKinds.Employee
                && a.EmployeeId == employeeId && taskIds.Contains(a.OccurrenceTaskId))
            .ToListAsync(ct);

        var result = new List<MeetingTaskComplianceOccurrence>();
        foreach (var c in candidates)
        {
            var own = overrides.FirstOrDefault(a => a.OccurrenceTaskId == c.TaskId);
            var series = participants.FirstOrDefault(p => p.MeetingSeriesId == c.SeriesId);
            // A "remove" override means the employee is not an attendee of this occurrence at all.
            if (own is { Change: MeetingAttendeeChanges.Remove })
                continue;
            var response = own?.ResponseStatus ?? series?.ResponseStatus;
            if (response == CalendarEventParticipantStatuses.Rejected)
                continue;   // a declined occurrence has no meeting application to measure

            result.Add(new MeetingTaskComplianceOccurrence(c.TaskId, c.StartAt, c.EndAt, c.Title, c.IsPrivate, c.Provider));
        }
        return result;
    }

    public async Task<IReadOnlyList<EmployeeDayMeeting>> ListEmployeeDayMeetingsAsync(Guid tenantId, Guid employeeId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = (from.ToUniversalTime(), to.ToUniversalTime());
        var candidates = await (
            from d in _db.MeetingDetails.AsNoTracking()
            join t in _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsOnly) on d.TaskId equals t.Id
            join s in _db.MeetingSeries.AsNoTracking() on d.SeriesId equals s.Id
            where d.TenantId == tenantId && t.TenantId == tenantId && s.TenantId == tenantId
                  && d.StartAt < toUtc && d.EndAt > fromUtc
                  && (s.OrganizerEmployeeId == employeeId
                      || _db.CalendarEventParticipants.Any(p => p.MeetingSeriesId == d.SeriesId && p.EmployeeId == employeeId && !p.IsDeleted)
                      || _db.MeetingOccurrenceAttendees.Any(a => a.OccurrenceTaskId == d.TaskId
                          && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == employeeId
                          && a.Change == MeetingAttendeeChanges.Add))
            orderby d.StartAt
            select new { Details = d, Series = s, t.Title })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return [];

        var seriesIds = candidates.Select(c => c.Series.Id).Distinct().ToList();
        var taskIds = candidates.Select(c => c.Details.TaskId).ToList();
        var participants = await _db.CalendarEventParticipants.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.EmployeeId == employeeId && !p.IsDeleted
                && p.MeetingSeriesId != null && seriesIds.Contains(p.MeetingSeriesId.Value))
            .ToListAsync(ct);
        var overrides = await _db.MeetingOccurrenceAttendees.AsNoTracking()
            .Where(a => a.TenantId == tenantId && taskIds.Contains(a.OccurrenceTaskId)
                && a.AttendeeKind == MeetingAttendeeKinds.Employee && a.EmployeeId == employeeId)
            .ToListAsync(ct);

        var result = new List<EmployeeDayMeeting>();
        foreach (var c in candidates)
        {
            var organizer = c.Series.OrganizerEmployeeId == employeeId;
            var own = overrides.FirstOrDefault(a => a.OccurrenceTaskId == c.Details.TaskId);
            if (own is { Change: MeetingAttendeeChanges.Remove } && !organizer)
                continue;   // removed from this occurrence
            var response = own?.ResponseStatus ?? participants.FirstOrDefault(p => p.MeetingSeriesId == c.Series.Id)?.ResponseStatus;
            var cancelled = c.Details.CancelledAt is not null || MeetingStatusCalculator.CancelledWithSeries(c.Details, c.Series);
            result.Add(new EmployeeDayMeeting(c.Details.TaskId, c.Series.Id, c.Details.OriginalStart, c.Details.StartAt, c.Details.EndAt,
                c.Details.IsAllDay, c.Title, c.Details.IsPrivate, cancelled,
                Declined: !organizer && response == CalendarEventParticipantStatuses.Rejected));
        }
        return result;
    }

    public async Task<IReadOnlyList<MeetingOccurrenceSuppression>> ListSuppressionsFromAsync(
        Guid tenantId, Guid seriesId, DateTimeOffset from, CancellationToken ct = default)
    {
        var fromUtc = from.ToUniversalTime();
        return await _db.MeetingOccurrenceSuppressions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.SeriesId == seriesId && s.OriginalStart >= fromUtc)
            .OrderBy(s => s.OriginalStart).ToListAsync(ct);
    }

    public async Task<bool> HasDependentsAsync(Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default)
        => await _db.MeetingOccurrenceAttendees.AnyAsync(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId, ct)
            || await _db.MeetingActionItems.AnyAsync(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId, ct)
            || await _db.CalendarEventMeetingAttendances.AnyAsync(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId, ct);

    public void AddSuppression(MeetingOccurrenceSuppression suppression) => _db.MeetingOccurrenceSuppressions.Add(suppression);

    public void RemoveMeetingTask(WorkTask task) => _db.WorkTasks.Remove(task);

    // IgnoreQueryFilters() drops the soft-delete half of the composed filter on purpose (a removed participant is revived,
    // like EfCalendarEventRepository.GetRemovedParticipantsAsync); tenant scoping is kept by the explicit TenantId predicate.
    public async Task<CalendarEventParticipant?> GetSeriesParticipantTrackedIncludingRemovedAsync(
        Guid tenantId, Guid seriesId, Guid employeeId, CancellationToken ct = default)
        => await _db.CalendarEventParticipants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.MeetingSeriesId == seriesId && p.EmployeeId == employeeId, ct);

    public async Task<CalendarEventGuest?> GetSeriesGuestTrackedAsync(Guid tenantId, Guid seriesId, string email, CancellationToken ct = default)
    {
        var key = email.Trim().ToLowerInvariant();
        return await _db.CalendarEventGuests
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.MeetingSeriesId == seriesId && g.Email.ToLower() == key, ct);
    }

    public async Task<MeetingOccurrenceAttendee?> GetOverrideTrackedAsync(
        Guid tenantId, Guid occurrenceTaskId, Guid? employeeId, string? email, CancellationToken ct = default)
    {
        var key = email?.Trim().ToLowerInvariant();
        return await _db.MeetingOccurrenceAttendees.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId
            && (employeeId != null ? a.EmployeeId == employeeId : a.Email != null && a.Email.ToLower() == key), ct);
    }

    public async Task DeleteFutureOverridesAsync(Guid tenantId, Guid seriesId, Guid? employeeId, string? email, string change,
        DateTimeOffset after, CancellationToken ct = default)
    {
        var afterUtc = after.ToUniversalTime();
        var key = email?.Trim().ToLowerInvariant();
        await _db.MeetingOccurrenceAttendees
            .Where(a => a.TenantId == tenantId && a.Change == change
                && (employeeId != null ? a.EmployeeId == employeeId : a.Email != null && a.Email.ToLower() == key)
                && _db.MeetingDetails.Any(d => d.TaskId == a.OccurrenceTaskId && d.SeriesId == seriesId && d.StartAt > afterUtc))
            .ExecuteDeleteAsync(ct);
    }

    public void AddOverride(MeetingOccurrenceAttendee row) => _db.MeetingOccurrenceAttendees.Add(row);

    public void RemoveOverride(MeetingOccurrenceAttendee row) => _db.MeetingOccurrenceAttendees.Remove(row);

    public void RemoveParticipant(CalendarEventParticipant participant) => _db.CalendarEventParticipants.Remove(participant);

    public void RemoveGuest(CalendarEventGuest guest) => _db.CalendarEventGuests.Remove(guest);

    public void AddRsvpHistory(CalendarEventParticipantRsvpHistory row) => _db.CalendarEventParticipantRsvpHistory.Add(row);

    public async Task RepointPlaceholderStatusAsync(Guid tenantId, Guid projectId, Guid fromStatusId, Guid toStatusId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters(ExcludeMeetingsAndTenantScope)
            .Where(t => t.TenantId == tenantId && t.ProjectId == projectId && t.StatusId == fromStatusId && t.TaskKind == WorkTaskKinds.Meeting)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StatusId, toStatusId), ct);

    public void AddEditLog(TaskEditLog log) => _db.TaskEditLogs.Add(log);

    public void AddSeries(MeetingSeries series) => _db.MeetingSeries.Add(series);

    public void AddParticipants(IEnumerable<CalendarEventParticipant> participants) => _db.CalendarEventParticipants.AddRange(participants);

    public void AddGuests(IEnumerable<CalendarEventGuest> guests) => _db.CalendarEventGuests.AddRange(guests);
}
