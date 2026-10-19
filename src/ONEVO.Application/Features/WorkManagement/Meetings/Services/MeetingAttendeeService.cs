using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Spec 5.3 attendees (organizer only) and RSVP (self):
/// - a series add revives a removed participant and clears that person's future "remove" overrides;
/// - a series remove revokes access at once and clears their future "add" overrides; the organizer cannot be removed;
/// - a per-occurrence add/remove materializes the occurrence and writes (or undoes) one override;
/// - declining keeps access (D18); answers are kept in the RSVP history.
/// </summary>
public sealed class MeetingAttendeeService
{
    private static readonly HashSet<int> ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingAccessResolver _access;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public MeetingAttendeeService(IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IMeetingOccurrenceMaterializer materializer, IMeetingAccessResolver access, IEmployeeRepository employees, IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _options = options;
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _access = access;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public Task<Result<MeetingAttendeesResponse>> AddSeriesAttendeesAsync(Guid tenantId, Guid caller, Guid seriesId,
        MeetingAttendeesRequest request, CancellationToken ct)
        => SeriesWriteAsync(tenantId, caller, seriesId, async (series, now) =>
        {
            var (active, skipped) = await SplitActiveAsync(tenantId, request.EmployeeIds, ct);
            var added = 0;
            foreach (var employeeId in active)
            {
                var existing = await _meetings.GetSeriesParticipantTrackedIncludingRemovedAsync(tenantId, series.Id, employeeId, ct);
                if (existing is { IsDeleted: false })
                    continue;
                if (existing is not null)
                {
                    existing.IsDeleted = false;
                    existing.DeletedAt = null;
                    existing.ResponseStatus = CalendarEventParticipantStatuses.Pending;
                    existing.ResponseReason = null;
                    existing.RespondedAt = null;
                }
                else
                {
                    _meetings.AddParticipants([new CalendarEventParticipant
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = series.Id, EmployeeId = employeeId,
                    }]);
                }
                await _meetings.DeleteFutureOverridesAsync(tenantId, series.Id, employeeId, null, MeetingAttendeeChanges.Remove, now, ct);
                added++;
            }
            foreach (var email in MeetingCreateValidator.NormalizeGuests(request.GuestEmails))
            {
                if (await _meetings.GetSeriesGuestTrackedAsync(tenantId, series.Id, email, ct) is not null)
                    continue;
                _meetings.AddGuests([new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = series.Id, Email = email, CreatedAt = now }]);
                await _meetings.DeleteFutureOverridesAsync(tenantId, series.Id, null, email, MeetingAttendeeChanges.Remove, now, ct);
                added++;
            }
            return Result<MeetingAttendeesResponse>.Success(new MeetingAttendeesResponse(added, skipped));
        }, ct);

    public Task<Result<bool>> RemoveSeriesEmployeeAsync(Guid tenantId, Guid caller, Guid seriesId, Guid employeeId, CancellationToken ct)
        => SeriesWriteAsync(tenantId, caller, seriesId, async (series, now) =>
        {
            if (employeeId == series.OrganizerEmployeeId)
                return Result<bool>.Failure("The organizer cannot be removed from their meeting.", 400, MeetingErrorCodes.OrganizerCannotBeRemoved);
            var participant = await _meetings.GetSeriesParticipantTrackedIncludingRemovedAsync(tenantId, series.Id, employeeId, ct);
            if (participant is { IsDeleted: false })
                _meetings.RemoveParticipant(participant);
            await _meetings.DeleteFutureOverridesAsync(tenantId, series.Id, employeeId, null, MeetingAttendeeChanges.Add, now, ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<bool>> RemoveSeriesGuestAsync(Guid tenantId, Guid caller, Guid seriesId, string email, CancellationToken ct)
        => SeriesWriteAsync(tenantId, caller, seriesId, async (series, now) =>
        {
            if (await _meetings.GetSeriesGuestTrackedAsync(tenantId, series.Id, email, ct) is { } guest)
                _meetings.RemoveGuest(guest);
            await _meetings.DeleteFutureOverridesAsync(tenantId, series.Id, null, email, MeetingAttendeeChanges.Add, now, ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<MeetingAttendeesResponse>> AddOccurrenceAttendeesAsync(Guid tenantId, Guid caller, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, MeetingAttendeesRequest request, CancellationToken ct)
        => OccurrenceWriteAsync(tenantId, caller, callerUserId, seriesId, originalStart, async (series, taskId, now) =>
        {
            var (active, skipped) = await SplitActiveAsync(tenantId, request.EmployeeIds, ct);
            var seriesPeople = (await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct)).Where(p => !p.IsDeleted)
                .Select(p => p.EmployeeId).ToHashSet();
            var added = 0;
            foreach (var employeeId in active)
                if (await SetOverrideAsync(tenantId, taskId, employeeId, null, seriesPeople.Contains(employeeId), add: true, ct))
                    added++;
            var seriesGuests = (await _meetings.ListSeriesGuestsAsync(tenantId, series.Id, ct)).Select(g => g.Email.ToLowerInvariant()).ToHashSet();
            foreach (var email in MeetingCreateValidator.NormalizeGuests(request.GuestEmails))
                if (await SetOverrideAsync(tenantId, taskId, null, email, seriesGuests.Contains(email.ToLowerInvariant()), add: true, ct))
                    added++;
            return Result<MeetingAttendeesResponse>.Success(new MeetingAttendeesResponse(added, skipped));
        }, ct);

    public Task<Result<bool>> RemoveOccurrenceEmployeeAsync(Guid tenantId, Guid caller, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, Guid employeeId, CancellationToken ct)
        => OccurrenceWriteAsync(tenantId, caller, callerUserId, seriesId, originalStart, async (series, taskId, _) =>
        {
            if (employeeId == series.OrganizerEmployeeId)
                return Result<bool>.Failure("The organizer cannot be removed from their meeting.", 400, MeetingErrorCodes.OrganizerCannotBeRemoved);
            var inSeries = (await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct)).Any(p => p.EmployeeId == employeeId && !p.IsDeleted);
            await SetOverrideAsync(tenantId, taskId, employeeId, null, inSeries, add: false, ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<bool>> RemoveOccurrenceGuestAsync(Guid tenantId, Guid caller, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, string email, CancellationToken ct)
        => OccurrenceWriteAsync(tenantId, caller, callerUserId, seriesId, originalStart, async (series, taskId, _) =>
        {
            var inSeries = await _meetings.GetSeriesGuestTrackedAsync(tenantId, series.Id, email, ct) is not null;
            await SetOverrideAsync(tenantId, taskId, null, email.Trim(), inSeries, add: false, ct);
            return Result<bool>.Success(true);
        }, ct);

    /// <summary>RSVP by the caller: for the series (a series participant), or - with OriginalStart - for one occurrence
    /// they were added to individually. Declining keeps access.</summary>
    public async Task<Result<bool>> RespondAsync(Guid tenantId, Guid caller, Guid seriesId, RespondToMeetingRequest request, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<bool>(_options.Value) is { } blocked)
            return blocked;
        if (request.Status is not (CalendarEventParticipantStatuses.Accepted or CalendarEventParticipantStatuses.Rejected))
            return Result<bool>.Failure("Answer accepted or rejected.", 400, MeetingErrorCodes.ResponseInvalid);
        var now = _clock.UtcNow;
        var reason = request.Reason?.Trim();

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (request.OriginalStart is { } start)
            {
                var details = await _meetings.GetDetailsAsync(tenantId, seriesId, start, innerCt);
                var add = details is null ? null : await _meetings.GetOverrideTrackedAsync(tenantId, details.TaskId, caller, null, innerCt);
                if (add is not { Change: MeetingAttendeeChanges.Add })
                    return MeetingReadService.NotFound<bool>();
                _meetings.AddRsvpHistory(History(tenantId, add.Id, details!.TaskId, caller, add.ResponseStatus, request.Status, reason, now));
                add.ResponseStatus = request.Status;
                add.ResponseReason = reason;
            }
            else
            {
                var participant = await _meetings.GetSeriesParticipantTrackedIncludingRemovedAsync(tenantId, seriesId, caller, innerCt);
                if (participant is not { IsDeleted: false })
                    return MeetingReadService.NotFound<bool>();
                _meetings.AddRsvpHistory(History(tenantId, participant.Id, seriesId, caller, participant.ResponseStatus, request.Status, reason, now));
                participant.ResponseStatus = request.Status;
                participant.ResponseReason = reason;
                participant.RespondedAt = now;
            }
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<bool>.Success(true);
        }, ct);
    }

    private static CalendarEventParticipantRsvpHistory History(Guid tenantId, Guid participantId, Guid eventId, Guid employeeId,
        string? from, string to, string? reason, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ParticipantId = participantId, EventId = eventId, EmployeeId = employeeId,
            FromStatus = from ?? CalendarEventParticipantStatuses.Pending, ToStatus = to, ChangedAt = now, Reason = reason,
        };

    /// <summary>Makes one person present (add) or absent (remove) on one occurrence with the fewest rows: an override
    /// that already says so is kept, one that says the opposite is removed when the series already gives that answer.
    /// True when something changed.</summary>
    private async Task<bool> SetOverrideAsync(Guid tenantId, Guid taskId, Guid? employeeId, string? email, bool inSeries, bool add, CancellationToken ct)
    {
        var existing = await _meetings.GetOverrideTrackedAsync(tenantId, taskId, employeeId, email, ct);
        var wanted = add ? MeetingAttendeeChanges.Add : MeetingAttendeeChanges.Remove;
        if (existing is not null)
        {
            if (existing.Change == wanted)
                return false;
            _meetings.RemoveOverride(existing);
            await _unitOfWork.SaveChangesAsync(ct);   // one row per person: the replacement may follow
            if (inSeries == add)
                return true;
        }
        else if (inSeries == add)
            return false;

        _meetings.AddOverride(new MeetingOccurrenceAttendee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OccurrenceTaskId = taskId,
            AttendeeKind = employeeId is not null ? MeetingAttendeeKinds.Employee : MeetingAttendeeKinds.Guest,
            EmployeeId = employeeId, Email = email, Change = wanted,
            ResponseStatus = add && employeeId is not null ? CalendarEventParticipantStatuses.Pending : null,
            CreatedAt = _clock.UtcNow,
        });
        return true;
    }

    private async Task<(List<Guid> Active, List<SkippedParticipant> Skipped)> SplitActiveAsync(Guid tenantId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var found = await _employees.ListByIdsAsync(tenantId, wanted, ct);
        var active = new List<Guid>();
        var skipped = new List<SkippedParticipant>();
        foreach (var id in wanted)
        {
            if (!found.TryGetValue(id, out var e))
                skipped.Add(new SkippedParticipant(id, "not_found"));
            else if (!ActiveStatuses.Contains(e.EmploymentStatusId))
                skipped.Add(new SkippedParticipant(id, "inactive"));
            else
                active.Add(id);
        }
        return (active, skipped);
    }

    /// <summary>Organizer-only series write under the series lock, the series re-read after locking.</summary>
    private async Task<Result<T>> SeriesWriteAsync<T>(Guid tenantId, Guid caller, Guid seriesId,
        Func<MeetingSeries, DateTimeOffset, Task<Result<T>>> write, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<T>(_options.Value) is { } blocked)
            return blocked;
        var access = await _access.ResolveForSeriesAsync(tenantId, caller, seriesId, ct);
        if (access is null)
            return MeetingReadService.NotFound<T>();
        if (access.Role != MeetingRoles.Organizer)
            return Result<T>.Failure("Only the organizer can change this meeting.", 403, MeetingErrorCodes.OrganizerRequired);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesTrackedAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<T>();
                var result = await write(series, _clock.UtcNow);
                if (result.IsSuccess)
                    await _unitOfWork.SaveChangesAsync(innerCt);
                return result;
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<T>();
        }
    }

    /// <summary>Organizer-only write to one not-started occurrence, materialized if needed, under the series lock.</summary>
    private async Task<Result<T>> OccurrenceWriteAsync<T>(Guid tenantId, Guid caller, Guid callerUserId, Guid seriesId, DateTimeOffset originalStart,
        Func<MeetingSeries, Guid, DateTimeOffset, Task<Result<T>>> write, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<T>(_options.Value) is { } blocked)
            return blocked;
        if (await MeetingGuard.OrganizerOnlyAsync<T>(_access, tenantId, caller, seriesId, originalStart, ct) is { } denied)
            return denied;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesTrackedAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<T>();
                var now = _clock.UtcNow;
                var key = originalStart.ToUniversalTime();
                var current = await _meetings.GetDetailsAsync(tenantId, seriesId, key, innerCt);
                if ((current?.StartAt ?? key) <= now)
                    return Result<T>.Failure("This occurrence has already started.", 409, MeetingErrorCodes.OccurrenceStarted);
                var ensured = await _materializer.EnsureAsync(tenantId, series, key, callerUserId, innerCt);
                if (!ensured.IsSuccess)
                    return Result<T>.Failure(ensured.Error!, ensured.StatusCode ?? 404, ensured.ErrorCode);
                var result = await write(series, ensured.Value, now);
                if (result.IsSuccess)
                    await _unitOfWork.SaveChangesAsync(innerCt);
                return result;
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<T>();
        }
    }
}
