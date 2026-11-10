using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>What the caller is to a meeting. <see cref="Details"/> is null while the occurrence is still virtual.
/// <see cref="ActingForEmployeeId"/> is set when the caller is a meeting delegate acting as the organizer: it names the
/// principal (the series' organizer). An action that must stay with the principal personally refuses when it is set.</summary>
public sealed record MeetingAccess(string Role, MeetingSeries Series, MeetingDetails? Details, Guid? ActingForEmployeeId = null);

/// <summary>
/// The single decision of who may see a meeting (spec 5.1, D6): its organizer and its attendees, nobody else. There is
/// deliberately no role, permission, tenant-owner, lead, module-owner, approver or HR check here. Null means 404.
/// </summary>
public interface IMeetingAccessResolver
{
    Task<MeetingAccess?> ResolveForOccurrenceAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct);

    /// <summary>The series as a whole: its organizer and series attendees. An occurrence-only add sees just that occurrence.</summary>
    Task<MeetingAccess?> ResolveForSeriesAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct);
}

public sealed class MeetingAccessResolver : IMeetingAccessResolver
{
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingDelegateRepository? _delegates;
    private readonly IDateTimeProvider? _clock;

    /// <param name="delegates">Meeting delegates (spec "Access"); without it nobody acts for an organizer.</param>
    public MeetingAccessResolver(IMeetingRepository meetings, IMeetingDelegateRepository? delegates = null, IDateTimeProvider? clock = null)
    {
        _meetings = meetings;
        _delegates = delegates;
        _clock = clock;
    }

    public async Task<MeetingAccess?> ResolveForOccurrenceAsync(
        Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct)
    {
        var series = await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (series is null)
            return null;
        var details = await _meetings.GetDetailsAsync(tenantId, seriesId, originalStart, ct);
        if (series.OrganizerEmployeeId == callerEmployeeId)
            return new MeetingAccess(MeetingRoles.Organizer, series, details);
        // A delegate runs the meeting as its organizer, before (and instead of) any attendee row they may also have.
        if (await IsDelegateOfOrganizerAsync(tenantId, series, callerEmployeeId, ct))
            return new MeetingAccess(MeetingRoles.Organizer, series, details, series.OrganizerEmployeeId);

        if (details is not null)
        {
            var mine = (await _meetings.ListOverridesAsync(tenantId, [details.TaskId], ct))
                .FirstOrDefault(o => o.AttendeeKind == MeetingAttendeeKinds.Employee && o.EmployeeId == callerEmployeeId);
            if (mine is not null)
                return mine.Change == MeetingAttendeeChanges.Add ? new MeetingAccess(MeetingRoles.Attendee, series, details) : null;
        }

        return await IsSeriesParticipantAsync(tenantId, callerEmployeeId, seriesId, ct)
            ? new MeetingAccess(MeetingRoles.Attendee, series, details)
            : null;
    }

    public async Task<MeetingAccess?> ResolveForSeriesAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
    {
        var series = await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (series is null)
            return null;
        if (series.OrganizerEmployeeId == callerEmployeeId)
            return new MeetingAccess(MeetingRoles.Organizer, series, null);
        if (await IsDelegateOfOrganizerAsync(tenantId, series, callerEmployeeId, ct))
            return new MeetingAccess(MeetingRoles.Organizer, series, null, series.OrganizerEmployeeId);
        return await IsSeriesParticipantAsync(tenantId, callerEmployeeId, seriesId, ct)
            ? new MeetingAccess(MeetingRoles.Attendee, series, null)
            : null;
    }

    private async Task<bool> IsDelegateOfOrganizerAsync(Guid tenantId, MeetingSeries series, Guid callerEmployeeId, CancellationToken ct)
        => _delegates is not null && _clock is not null
            && await _delegates.IsActiveDelegateAsync(tenantId, series.OrganizerEmployeeId, callerEmployeeId, _clock.UtcNow, ct);

    private async Task<bool> IsSeriesParticipantAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
        => (await _meetings.ListSeriesParticipantsAsync(tenantId, seriesId, ct)).Any(p => p.EmployeeId == callerEmployeeId && !p.IsDeleted);
}
