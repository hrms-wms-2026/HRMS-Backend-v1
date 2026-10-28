using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The reporting-line attendance view on the employee profile (spec D16, D17).
/// <para>
/// Who may see what: the employee themself, and every manager above them in their <em>current</em> reporting
/// line. Nobody else - not HR, not the project owner, not a manager who left the line - gets a 404, the same
/// body as an employee that does not exist. There is no role, HR or Owner shortcut (spec 5.1).
/// </para>
/// <para>
/// The rows are the employee's own occurrences: the ones where they are an effective employee attendee (the
/// series attendees minus that occurrence's "remove" overrides plus its "add" overrides), that have ended, in
/// the window, newest first. Guests are never in it. A legacy calendar event is never in it (spec 6).
/// </para>
/// <para>
/// A private meeting is masked for a manager (D17): the title and the organizer are hidden. The employee
/// themself still sees their own meeting's title.
/// </para>
/// </summary>
public sealed class EmployeeMeetingAttendanceService
{
    /// <summary>The default window: the last 30 days.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>The longest window the endpoint serves; a longer one is a 400 (spec 5.1).</summary>
    public const int MaxWindowDays = 92;

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingAttendanceRepository _attendance;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public EmployeeMeetingAttendanceService(IMeetingRepository meetings, IMeetingAttendanceRepository attendance,
        IEmployeeRepository employees, IDateTimeProvider clock)
    {
        _meetings = meetings;
        _attendance = attendance;
        _employees = employees;
        _clock = clock;
    }

    public async Task<Result<EmployeeMeetingAttendanceResponse>> GetAsync(Guid tenantId, Guid callerEmployeeId, Guid employeeId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var windowTo = to ?? now;
        var windowFrom = from ?? windowTo.AddDays(-DefaultWindowDays);

        if (windowTo < windowFrom || (windowTo - windowFrom).TotalDays > MaxWindowDays)
            return Result<EmployeeMeetingAttendanceResponse>.Failure(
                $"The window may not be longer than {MaxWindowDays} days.", 400, MeetingErrorCodes.InvalidTime);

        // The employee themself, or one of their managers in the current reporting line.
        if (callerEmployeeId != employeeId
            && !(await _employees.ListAncestorChainEmployeeIdsAsync(tenantId, employeeId, ct)).Contains(callerEmployeeId))
            return MeetingReadService.NotFound<EmployeeMeetingAttendanceResponse>();

        var occurrences = await _attendance.ListEmployeeOccurrencesAsync(tenantId, employeeId, windowFrom, windowTo, now, ct);
        if (occurrences.Count == 0)
            return Result<EmployeeMeetingAttendanceResponse>.Success(new EmployeeMeetingAttendanceResponse([]));

        // A removed occurrence (a split surplus) keeps its meeting_details row, but the occurrence is gone:
        // only the surviving ones are listed.
        var rows = (await _meetings.ListOccurrencesByTaskIdsAsync(tenantId, occurrences.Select(o => o.TaskId).ToList(), ct))
            .ToDictionary(r => r.Details.TaskId);
        var series = (await _meetings.ListSeriesAsync(tenantId, occurrences.Select(o => o.SeriesId).Distinct().ToList(), ct))
            .ToDictionary(s => s.Id);
        var names = await _employees.ListByIdsAsync(tenantId,
            series.Values.Select(s => s.OrganizerEmployeeId).Distinct().ToList(), ct);

        var items = new List<EmployeeMeetingAttendanceItem>();
        foreach (var occurrence in occurrences)
        {
            if (!rows.TryGetValue(occurrence.TaskId, out var row))
                continue;
            if (!series.TryGetValue(occurrence.SeriesId, out var meetingSeries))
                continue;

            var privateMeeting = occurrence.IsPrivate;
            var masked = privateMeeting && callerEmployeeId != employeeId;
            var organizerName = names.TryGetValue(meetingSeries.OrganizerEmployeeId, out var organizer)
                ? $"{organizer.FirstName} {organizer.LastName}".Trim()
                : null;

            var status = occurrence.AttendanceStatus ?? MeetingAttendanceStatuses.NotApplicable;
            var intervals = occurrence.Intervals;
            var joined = intervals.Count > 0;

            items.Add(new EmployeeMeetingAttendanceItem(
                occurrence.TaskId, occurrence.StartAt, occurrence.EndAt,
                masked ? MeetingPrivacy.MaskedTitle : row.Title,
                masked ? null : organizerName,
                status,
                DisplayOf(status),
                joined ? MeetingAttendanceOutcomes.Joined
                    : status == MeetingAttendanceStatuses.Synced ? MeetingAttendanceOutcomes.NoJoinRecorded
                    : MeetingAttendanceOutcomes.Unknown,
                joined ? (int)MeetingAttendanceMatcher.Union(intervals, occurrence.EndAt).TotalSeconds : null));
        }

        return Result<EmployeeMeetingAttendanceResponse>.Success(new EmployeeMeetingAttendanceResponse(items));
    }

    private static string DisplayOf(string status) => status switch
    {
        MeetingAttendanceStatuses.Provisional or MeetingAttendanceStatuses.Synced => MeetingAttendanceOutcomes.DisplayRecorded,
        MeetingAttendanceStatuses.NotApplicable => MeetingAttendanceOutcomes.DisplayNotApplicable,
        _ => MeetingAttendanceOutcomes.DisplayUnknown,
    };
}
