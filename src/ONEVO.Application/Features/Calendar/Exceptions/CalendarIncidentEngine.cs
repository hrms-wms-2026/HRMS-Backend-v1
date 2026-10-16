using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>
/// Evaluates every calendar meeting-exception rule for one employee (plan C2) over the last 30 days and the next 14 (the
/// load rules look ahead), then hands the findings to <see cref="CalendarCaseService"/>. Idempotent: an incident is
/// recorded once, so every trigger (post-meeting, RSVP change, leave approval, event change, nightly) simply re-runs it.
/// </summary>
public sealed class CalendarIncidentEngine(
    ICalendarEventRepository events,
    ICalendarEventMeetingRepository meetings,
    ICalendarEventMeetingAttendanceRepository attendances,
    ICalendarRecurrenceExpander expander,
    ILeaveRequestReadRepository leaves,
    ILegalEntityRepository legalEntities,
    CoreEmployeeRepository employees,
    IExternalCalendarConnectionRepository connections,
    IMeetingComplianceReadRepository compliance,
    IEmployeeAuthorityResolverFactory authority,
    IEnumerable<ICalendarIncidentRule> rules,
    CalendarCaseService cases,
    IDateTimeProvider clock,
    IMonitoringToggleResolver? toggles = null)
{
    public const int LookAheadDays = 14;

    public async Task<CalendarCaseResult> EvaluateEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var input = await BuildInputAsync(tenantId, employeeId, ct);
        if (input is null)
            return new CalendarCaseResult(0, 0, []);
        var ruleList = rules.ToList();
        var findings = ruleList.SelectMany(r => r.Evaluate(input)).ToList();
        return await cases.ApplyAsync(tenantId, employeeId, findings, ruleList.Select(r => r.Code).ToList(), input.Now, input.Now, ct);
    }

    /// <summary>The employee's meetings in the evaluation window as the rules see them.</summary>
    public async Task<CalendarIncidentInput?> BuildInputAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null)
            return null;
        var now = clock.UtcNow;
        var from = now.AddDays(-CalendarExceptionDefaults.LongWindowDays);
        var to = now.AddDays(LookAheadDays);

        // Occurrences: single events and moved occurrences as they are, recurring series expanded (cancelled or moved
        // occurrences skipped - a moved one is its own row).
        var occurrences = new List<(CalendarEvent Event, Guid SeriesId, DateTimeOffset Start, DateTimeOffset End, bool Cancelled)>();
        foreach (var e in await events.GetInDateRangeForEmployeeAsync(tenantId, employeeId, from, to, ct))
            occurrences.Add((e, e.RecurrenceParentId ?? e.Id, e.StartDate, e.EndDate, IsCancelled(e)));
        foreach (var master in await events.GetRecurringMastersForEmployeeAsync(tenantId, employeeId, to, ct))
        {
            if (string.IsNullOrWhiteSpace(master.RecurrenceRule))
                continue;
            var overridden = (await events.GetChildrenForMasterAsync(tenantId, master.Id, ct))
                .Where(c => c.RecurrenceOriginalStart is not null).Select(c => c.RecurrenceOriginalStart!.Value).ToHashSet();
            var length = master.EndDate - master.StartDate;
            foreach (var start in expander.Expand(master.RecurrenceRule, master.StartDate, from, to).Where(s => !overridden.Contains(s)))
                occurrences.Add((master, master.Id, start, start + length, IsCancelled(master)));
        }

        var seriesIds = occurrences.Select(o => o.SeriesId).Distinct().ToList();
        var participants = seriesIds.Count == 0 ? new Dictionary<Guid, IReadOnlyList<CalendarEventParticipant>>()
            : await events.GetParticipantsForEventsAsync(tenantId, seriesIds, ct);
        var history = (await events.ListRsvpHistoryAsync(tenantId, employeeId, seriesIds, ct))
            .GroupBy(h => h.EventId).ToDictionary(g => g.Key, g => g.ToList());

        // Identity: the work email and every connected calendar account's email. Unmatched attendance is never evidence.
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(employee.Email))
            identities.Add(employee.Email);
        if (employee.UserId != Guid.Empty)
            foreach (var connection in await connections.GetForUserAsync(tenantId, employee.UserId, ct))
                if (!string.IsNullOrWhiteSpace(connection.ExternalAccountEmail))
                    identities.Add(connection.ExternalAccountEmail);

        // The desktop agent only counts when activity monitoring is on for this employee: without it there are no samples,
        // which would read as 0% and the company has not agreed to that monitoring (plan C2 security review).
        var agentAllowed = toggles is not null && await toggles.IsEnabledForEmployeeAsync(tenantId, employeeId, MonitoringCapability.ActivityMonitoring, ct);
        var agent = !agentAllowed
            ? new Dictionary<(Guid, DateTimeOffset), decimal>()
            : (await compliance.ListAsync(tenantId, employeeId, from, now, ct))
                .GroupBy(c => (c.EventId, c.StartsAt)).ToDictionary(g => g.Key, g => g.First().UsagePercentage / 100m);
        var managerId = await ReportingManagerAsync(tenantId, employee, ct);
        var organizerIds = new Dictionary<Guid, Guid?>();

        var views = new List<CalendarMeetingView>();
        var reports = new Dictionary<Guid, (CalendarEventMeeting? Meeting, IReadOnlyList<CalendarEventMeetingAttendance> Rows)>();
        foreach (var (e, seriesId, start, end, cancelled) in occurrences)
        {
            var people = participants.TryGetValue(seriesId, out var list) ? list : [];
            var mine = people.FirstOrDefault(p => p.EmployeeId == employeeId);
            var isOrganizer = e.CreatedById == employee.UserId;
            if (mine is null && !isOrganizer)
                continue;
            if (!organizerIds.TryGetValue(e.CreatedById, out var organizerId))
                organizerIds[e.CreatedById] = organizerId = (await employees.GetDefaultForUserAsync(tenantId, e.CreatedById, ct))?.Id;
            var peopleCount = people.Select(p => p.EmployeeId).Append(organizerId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct().Count();

            if (!reports.TryGetValue(seriesId, out var report))
            {
                var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, seriesId, ct);
                var rows = meeting is { Status: CalendarEventMeetingStatuses.Active } && meeting.LastAttendanceSyncedAt is not null
                    ? await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct) : [];
                reports[seriesId] = report = (meeting, rows);
            }
            var (state, spans) = ReportFor(report.Meeting, report.Rows, start, end, employeeId, identities, now);

            views.Add(new CalendarMeetingView(
                seriesId, start, end, DisplayTitle(e), e.IsAllDay, cancelled, isOrganizer, peopleCount, organizerId,
                organizerId is not null && organizerId == managerId,
                mine?.ResponseStatus ?? CalendarEventParticipantStatuses.Accepted,
                history.TryGetValue(seriesId, out var changes) ? changes.Select(h => new CalendarRsvpChange(h.FromStatus, h.ToStatus, h.ChangedAt)).ToList() : [],
                // The nominee of a replacement is not stored, so a replacement's attendance cannot be confirmed.
                ReplacementAttended: false,
                state, spans, agent.TryGetValue((seriesId, start), out var ratio) ? ratio : null));
        }

        var leaveIntervals = (await leaves.ListApprovedCoveringAsync(tenantId, [employeeId], DateOnly.FromDateTime(from.UtcDateTime), DateOnly.FromDateTime(to.UtcDateTime), ct))
            .Select(l => new CalendarInterval(l.StartAt, l.EndAt)).ToList();
        var legalEntity = employee.LegalEntityId is Guid le ? await legalEntities.GetByIdForTenantAsync(tenantId, le, ct) : null;
        var zone = AttendancePeriodCalculator.ResolveTimezone(legalEntity?.Timezone);
        var holidays = (await events.ListHolidayDatesAsync(tenantId, DateOnly.FromDateTime(from.UtcDateTime), DateOnly.FromDateTime(to.UtcDateTime), ct))
            .Select(d =>
            {
                var local = d.ToDateTime(TimeOnly.MinValue);
                var dayStart = new DateTimeOffset(local, zone.GetUtcOffset(local));
                return new CalendarInterval(dayStart, dayStart.AddDays(1));
            }).ToList();
        CalendarWorkingHours? hours = legalEntity is not null && AttendanceScheduleResolver.IsScheduleConfigured(legalEntity)
            ? new CalendarWorkingHours(zone, legalEntity.WorkStartTime!.Value, legalEntity.WorkEndTime!.Value,
                AttendanceScheduleResolver.ParseWorkingDays(legalEntity.StandardWorkingDays))
            : null;

        return new CalendarIncidentInput(employeeId, now, views, leaveIntervals, holidays, hours);
    }

    /// <summary>What the rules, the evidence and the reviewer may see of a meeting's title: a private event stays private.</summary>
    public static string DisplayTitle(CalendarEvent e) => e.IsPrivate ? "Private meeting" : e.Title;

    private static bool IsCancelled(CalendarEvent e) => e.IsRecurrenceCancelled || e.EventStatus == CalendarEventStatuses.Cancelled;

    /// <summary>Whether the provider report can be used for this occurrence, and the employee's matched rows in it. A
    /// recurring series shares one meeting: its report counts for an occurrence only when it was synced after that
    /// occurrence ended. Without a report, the rules wait until the cutoff, then fall back to the agent.</summary>
    public static (CalendarReportState State, IReadOnlyList<CalendarAttendanceSpan> Spans) ReportFor(
        CalendarEventMeeting? meeting, IReadOnlyList<CalendarEventMeetingAttendance> rows, DateTimeOffset start, DateTimeOffset end,
        Guid employeeId, IReadOnlySet<string> identities, DateTimeOffset now)
    {
        if (meeting is not { Status: CalendarEventMeetingStatuses.Active })
            return (CalendarReportState.NoOnlineMeeting, []);
        if (meeting.LastAttendanceSyncedAt is { } synced && synced >= end)
        {
            var spans = rows
                .Where(r => r.EmployeeId == employeeId || (r.ExternalParticipantEmail is { } email && identities.Contains(email)))
                .Where(r => r.JoinedAt < end.AddHours(1) && (r.LeftAt ?? r.JoinedAt) > start.AddHours(-1))
                .Select(r => new CalendarAttendanceSpan(r.JoinedAt, r.LeftAt)).ToList();
            return (CalendarReportState.Received, spans);
        }
        if (meeting.AttendanceUnavailableAt is not null || now >= end.AddHours(CalendarExceptionDefaults.AttendanceReportCutoffHours))
            return (CalendarReportState.Unavailable, []);
        return (CalendarReportState.Pending, []);
    }

    private async Task<Guid?> ReportingManagerAsync(Guid tenantId, ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct)
    {
        if (employee.LegalEntityId is not Guid legalEntityId)
            return null;
        var route = await authority.CreateForTenant(tenantId).ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            employee.Id, legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct);
        return route.IsSuccess && route.Value is { } approver && approver.ApproverEmployeeId != employee.Id ? approver.ApproverEmployeeId : null;
    }
}
