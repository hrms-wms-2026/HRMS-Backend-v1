using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>What the caller is to a meeting. <see cref="Details"/> is null while the occurrence is still virtual.</summary>
public sealed record MeetingAccess(string Role, MeetingSeries Series, MeetingDetails? Details);

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

    public MeetingAccessResolver(IMeetingRepository meetings) => _meetings = meetings;

    public async Task<MeetingAccess?> ResolveForOccurrenceAsync(
        Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct)
    {
        var series = await _meetings.GetSeriesAsync(tenantId, seriesId, ct);
        if (series is null)
            return null;
        var details = await _meetings.GetDetailsAsync(tenantId, seriesId, originalStart, ct);
        if (series.OrganizerEmployeeId == callerEmployeeId)
            return new MeetingAccess(MeetingRoles.Organizer, series, details);

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
        return await IsSeriesParticipantAsync(tenantId, callerEmployeeId, seriesId, ct)
            ? new MeetingAccess(MeetingRoles.Attendee, series, null)
            : null;
    }

    private async Task<bool> IsSeriesParticipantAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, CancellationToken ct)
        => (await _meetings.ListSeriesParticipantsAsync(tenantId, seriesId, ct)).Any(p => p.EmployeeId == callerEmployeeId && !p.IsDeleted);
}
