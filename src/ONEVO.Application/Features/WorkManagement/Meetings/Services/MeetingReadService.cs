using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The /meetings reads (spec 5.1, 5.9). Every single read goes through <see cref="IMeetingAccessResolver"/>, every list
/// through MeetingVisibility; "does not exist" and "not allowed" return the identical 404.
/// </summary>
public sealed class MeetingReadService
{
    private const string NotFoundMessage = "Meeting not found.";

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingAccessResolver _access;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IEmployeeRepository _employees;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IDateTimeProvider _clock;

    public MeetingReadService(IMeetingRepository meetings, IMeetingAccessResolver access, IProjectRepository projects,
        IObjectiveRepository objectives, IEmployeeRepository employees, IMeetingRecurrence recurrence, IDateTimeProvider clock)
    {
        _meetings = meetings;
        _access = access;
        _projects = projects;
        _objectives = objectives;
        _employees = employees;
        _recurrence = recurrence;
        _clock = clock;
    }

    public static Result<T> NotFound<T>() => Result<T>.Failure(NotFoundMessage, 404, MeetingErrorCodes.NotFound);

    public async Task<Result<MeetingOccurrenceResponse>> GetOccurrenceAsync(Guid tenantId, Guid caller, Guid taskId, CancellationToken ct)
    {
        var row = await _meetings.GetOccurrenceByTaskAsync(tenantId, taskId, ct);
        if (row is null)
            return NotFound<MeetingOccurrenceResponse>();
        var access = await _access.ResolveForOccurrenceAsync(tenantId, caller, row.Details.SeriesId, row.Details.OriginalStart, ct);
        return access is null
            ? NotFound<MeetingOccurrenceResponse>()
            : Result<MeetingOccurrenceResponse>.Success(await BuildAsync(tenantId, caller, access, row, row.Details.OriginalStart, ct));
    }

    /// <summary>An occurrence by its key, materialized or not. A virtual one exists only where the series' rule fires
    /// and no suppression covers it; it carries the task id it will get (<see cref="MeetingOccurrenceIds"/>).</summary>
    public async Task<Result<MeetingOccurrenceResponse>> GetOccurrenceAtAsync(Guid tenantId, Guid caller, Guid seriesId,
        DateTimeOffset originalStart, CancellationToken ct)
    {
        var access = await _access.ResolveForOccurrenceAsync(tenantId, caller, seriesId, originalStart, ct);
        if (access is null)
            return NotFound<MeetingOccurrenceResponse>();
        var row = await _meetings.GetOccurrenceAsync(tenantId, seriesId, originalStart, ct);
        if (row is null && !await IsVirtualSlotAsync(tenantId, access.Series, originalStart, ct))
            return NotFound<MeetingOccurrenceResponse>();
        return Result<MeetingOccurrenceResponse>.Success(await BuildAsync(tenantId, caller, access, row, originalStart, ct));
    }

    public async Task<Result<MeetingSeriesResponse>> GetSeriesAsync(Guid tenantId, Guid caller, Guid seriesId, CancellationToken ct)
    {
        var access = await _access.ResolveForSeriesAsync(tenantId, caller, seriesId, ct);
        if (access is null)
            return NotFound<MeetingSeriesResponse>();
        var s = access.Series;
        var now = _clock.UtcNow;
        var upcoming = await ListItemsAsync(tenantId, caller,
            await _meetings.ListVisibleAsync(tenantId, caller, now, DateTimeOffset.MaxValue, null, s.Id, ct), ct);
        var attendees = await AttendeeViewsAsync(tenantId, s, [], ct);
        return Result<MeetingSeriesResponse>.Success(new MeetingSeriesResponse(s.Id, s.Title, s.Description, s.Location, s.IsPrivate,
            s.DtStart, s.DurationMinutes, s.IsAllDay, s.Timezone, s.RecurrenceRule, s.CancelledAt is not null, s.Revision,
            await PlacementAsync(tenantId, s.ProjectId, s.ObjectiveId, ct), await PersonAsync(tenantId, s.OrganizerEmployeeId, ct),
            access.Role, attendees, upcoming));
    }

    public async Task<Result<IReadOnlyList<MeetingListItem>>> ListAsync(Guid tenantId, Guid caller, DateTimeOffset from, DateTimeOffset to,
        Guid? projectId, CancellationToken ct)
    {
        if (to <= from)
            return Result<IReadOnlyList<MeetingListItem>>.Failure("'to' must be after 'from'.", 400, MeetingErrorCodes.InvalidTime);
        var rows = await _meetings.ListVisibleAsync(tenantId, caller, from, to, projectId, null, ct);
        return Result<IReadOnlyList<MeetingListItem>>.Success(await ListItemsAsync(tenantId, caller, rows, ct));
    }

    private async Task<IReadOnlyList<MeetingListItem>> ListItemsAsync(Guid tenantId, Guid caller, IReadOnlyList<MeetingOccurrenceRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];
        var seriesIds = rows.Select(r => r.Details.SeriesId).Distinct().ToList();
        var series = (await _meetings.ListSeriesAsync(tenantId, seriesIds, ct)).ToDictionary(s => s.Id);
        var participants = (await _meetings.ListParticipantsForSeriesAsync(tenantId, seriesIds, ct))
            .Where(p => p.EmployeeId == caller).ToDictionary(p => p.MeetingSeriesId!.Value);
        var overrides = (await _meetings.ListOverridesAsync(tenantId, rows.Select(r => r.Details.TaskId).ToList(), ct))
            .Where(o => o.AttendeeKind == MeetingAttendeeKinds.Employee && o.EmployeeId == caller && o.Change == MeetingAttendeeChanges.Add)
            .ToDictionary(o => o.OccurrenceTaskId);
        var now = _clock.UtcNow;

        return rows.Select(r =>
        {
            var s = series[r.Details.SeriesId];
            var role = s.OrganizerEmployeeId == caller ? MeetingRoles.Organizer : MeetingRoles.Attendee;
            var response = overrides.TryGetValue(r.Details.TaskId, out var add) ? add.ResponseStatus
                : participants.TryGetValue(s.Id, out var p) ? p.ResponseStatus : null;
            return new MeetingListItem(r.Details.TaskId, r.ShortId, s.Id, r.Details.OriginalStart, r.Details.StartAt, r.Details.EndAt,
                r.Details.IsAllDay, r.Title, MeetingStatusCalculator.Of(r.Details, s, now), r.Details.IsPrivate, r.ProjectId,
                r.ObjectiveId, role, response);
        }).ToList();
    }

    private async Task<MeetingOccurrenceResponse> BuildAsync(Guid tenantId, Guid caller, MeetingAccess access, MeetingOccurrenceRow? row,
        DateTimeOffset originalStart, CancellationToken ct)
    {
        var s = access.Series;
        var key = originalStart.ToUniversalTime();
        var details = row?.Details ?? new MeetingDetails
        {
            TaskId = MeetingOccurrenceIds.For(s.Id, key), SeriesId = s.Id, OriginalStart = key, StartAt = key,
            EndAt = key.AddMinutes(s.DurationMinutes), IsAllDay = s.IsAllDay, Location = s.Location, IsPrivate = s.IsPrivate,
        };
        var attendees = await AttendeeViewsAsync(tenantId, s, row is null ? [] : [row.Details.TaskId], ct);
        var status = MeetingStatusCalculator.Of(details, s, _clock.UtcNow);
        var mine = attendees.FirstOrDefault(a => a.EmployeeId == caller);

        return new MeetingOccurrenceResponse(details.TaskId, row?.ShortId ?? string.Empty, s.Id, key, details.StartAt, details.EndAt,
            details.IsAllDay, s.Timezone, row?.Title ?? s.Title, row is null ? s.Description : row.Description, details.Location,
            details.IsPrivate, status, details.IsDetached, details.Revision, s.Revision, s.RecurrenceRule,
            await PlacementAsync(tenantId, row?.ProjectId ?? s.ProjectId, row?.ObjectiveId ?? s.ObjectiveId, ct),
            await PersonAsync(tenantId, s.OrganizerEmployeeId, ct), access.Role, mine?.Response, attendees,
            CanEdit: access.Role == MeetingRoles.Organizer && status is MeetingStatuses.Scheduled or MeetingStatuses.InProgress);
    }

    private async Task<IReadOnlyList<MeetingAttendeeView>> AttendeeViewsAsync(Guid tenantId, MeetingSeries s,
        IReadOnlyCollection<Guid> occurrenceTaskIds, CancellationToken ct)
    {
        var effective = EffectiveAttendees.Compute(
            await _meetings.ListSeriesParticipantsAsync(tenantId, s.Id, ct),
            await _meetings.ListSeriesGuestsAsync(tenantId, s.Id, ct),
            await _meetings.ListOverridesAsync(tenantId, occurrenceTaskIds, ct));
        var names = await _employees.ListByIdsAsync(tenantId, effective.Where(a => a.EmployeeId is not null).Select(a => a.EmployeeId!.Value).ToList(), ct);
        return effective.Select(a => new MeetingAttendeeView(a.Kind, a.EmployeeId,
            a.EmployeeId is Guid id && names.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
            a.Email, a.Response, a.ResponseReason, a.Source)).ToList();
    }

    private async Task<MeetingPersonRef> PersonAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var e = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        return new MeetingPersonRef(employeeId, e is null ? string.Empty : $"{e.FirstName} {e.LastName}".Trim());
    }

    private async Task<MeetingPlacementView> PlacementAsync(Guid tenantId, Guid projectId, Guid objectiveId, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);
        var module = await _objectives.GetByIdForTenantAsync(tenantId, objectiveId, ct);
        return new MeetingPlacementView(projectId, project?.Name ?? string.Empty, objectiveId, module?.Title ?? string.Empty,
            project?.SystemPurpose == ONEVO.Domain.Features.WorkManagement.Projects.Entities.ProjectSystemPurposes.Office);
    }

    private async Task<bool> IsVirtualSlotAsync(Guid tenantId, MeetingSeries s, DateTimeOffset originalStart, CancellationToken ct)
    {
        var key = originalStart.ToUniversalTime();
        var fires = _recurrence.Occurrences(s.RecurrenceRule, s.DtStart, s.Timezone, key, key.AddSeconds(1)).Contains(key);
        return fires && !await _meetings.IsSuppressedAsync(tenantId, s.Id, key, ct);
    }
}
