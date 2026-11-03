using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// An occurrence's attendance (spec 5.8, 5.9). Visibility is the meeting's own (<see cref="IMeetingAccessResolver"/>): the
/// organizer sees every effective attendee and the link's unmatched sessions, an attendee only their own row, anyone else
/// 404. A person's time is the union of their intervals. Nobody is ever "absent": without a recorded join a person is
/// "unknown" until the occurrence is synced, and "no join recorded" after.
/// </summary>
public sealed class MeetingAttendanceReadService
{
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingAccessResolver _access;
    private readonly IMeetingAttendanceRepository _attendance;
    private readonly IMeetingLinkRepository _links;
    private readonly IEmployeeRepository _employees;

    public MeetingAttendanceReadService(IMeetingRepository meetings, IMeetingAccessResolver access, IMeetingAttendanceRepository attendance,
        IMeetingLinkRepository links, IEmployeeRepository employees)
    {
        _meetings = meetings;
        _access = access;
        _attendance = attendance;
        _links = links;
        _employees = employees;
    }

    public async Task<Result<MeetingAttendanceResponse>> GetAsync(Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart,
        CancellationToken ct)
    {
        var key = originalStart.ToUniversalTime();
        var access = await _access.ResolveForOccurrenceAsync(tenantId, callerEmployeeId, seriesId, key, ct);
        if (access is null)
            return MeetingReadService.NotFound<MeetingAttendanceResponse>();
        var organizer = access.Role == MeetingRoles.Organizer;
        var details = access.Details;

        // A virtual occurrence has not happened: pending when the series has a link, not applicable otherwise.
        var status = details?.AttendanceStatus
            ?? (details is null && await _links.GetActiveForSeriesAsync(tenantId, seriesId, ct) is not null
                ? MeetingAttendanceStatuses.Pending
                : MeetingAttendanceStatuses.NotApplicable);
        var records = details is null ? [] : await _attendance.ListOccurrenceRecordsAsync(tenantId, details.TaskId, ct);

        var attendees = EffectiveAttendees.Compute(
            await _meetings.ListSeriesParticipantsAsync(tenantId, seriesId, ct),
            await _meetings.ListSeriesGuestsAsync(tenantId, seriesId, ct),
            details is null ? [] : await _meetings.ListOverridesAsync(tenantId, [details.TaskId], ct));
        if (!organizer)
            attendees = attendees.Where(a => a.EmployeeId == callerEmployeeId).ToList();
        var names = await _employees.ListByIdsAsync(tenantId,
            attendees.Where(a => a.EmployeeId is not null).Select(a => a.EmployeeId!.Value).ToList(), ct);
        var endAt = details?.EndAt ?? key;

        var rows = attendees.Select(a =>
        {
            var mine = a.EmployeeId is Guid employeeId
                ? records.Where(r => r.EmployeeId == employeeId)
                : records.Where(r => r.EmployeeId is null && string.Equals(r.ExternalParticipantEmail, a.Email, StringComparison.OrdinalIgnoreCase));
            var intervals = mine.Select(r => (r.JoinedAt, r.LeftAt)).ToList();
            var joined = intervals.Count > 0;
            var name = a.EmployeeId is Guid id && names.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : a.Email ?? string.Empty;
            return new MeetingAttendanceRow(a.Kind, a.EmployeeId, a.Email, name,
                joined ? (int)MeetingAttendanceMatcher.Union(intervals, endAt).TotalSeconds : null,
                joined ? MeetingAttendanceOutcomes.Joined
                    : status == MeetingAttendanceStatuses.Synced ? MeetingAttendanceOutcomes.NoJoinRecorded
                    : MeetingAttendanceOutcomes.Unknown);
        }).ToList();

        IReadOnlyList<MeetingUnmatchedSession>? unmatched = null;
        if (organizer)
            unmatched = details?.CalendarEventMeetingId is Guid linkId
                ? (await _attendance.ListUnmatchedSessionsAsync(tenantId, linkId, ct)).Select(s => new MeetingUnmatchedSession(s.SessionStart, s.SessionEnd)).ToList()
                : [];

        return Result<MeetingAttendanceResponse>.Success(new MeetingAttendanceResponse(status, DisplayOf(status), rows, unmatched));
    }

    private static string DisplayOf(string status) => status switch
    {
        MeetingAttendanceStatuses.Provisional or MeetingAttendanceStatuses.Synced => MeetingAttendanceOutcomes.DisplayRecorded,
        MeetingAttendanceStatuses.NotApplicable => MeetingAttendanceOutcomes.DisplayNotApplicable,
        _ => MeetingAttendanceOutcomes.DisplayUnknown,
    };
}
