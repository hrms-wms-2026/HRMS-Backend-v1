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
    IMonitoringToggleResolver? toggles = null,
    IEnumerable<IEmployeeMeetingOccurrenceSource>? occurrenceSources = null)
{
    public const int LookAheadDays = 14;

    /// <summary>Calendar events always; every registered source of other meetings after them.</summary>
    private IEnumerable<IEmployeeMeetingOccurrenceSource> Sources =>
        new[] { new LegacyCalendarOccurrenceSource(events, meetings, attendances, expander, employees) }
            .Concat(occurrenceSources ?? []);

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

        // Calendar events, then every other meeting kind (task meetings) through the same view (spec 10 "Engine integration").
        var context = new EmployeeOccurrenceContext(tenantId, employee, from, to, identities, managerId, agent, now);
        var views = new List<CalendarMeetingView>();
        foreach (var source in Sources)
            views.AddRange(await source.ListAsync(context, ct));

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
