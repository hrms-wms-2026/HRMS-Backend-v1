using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Calendar.Exceptions;

/// <summary>What one evaluation changed: incidents recorded / removed, cases opened.</summary>
public sealed record CalendarCaseResult(int IncidentsAdded, int IncidentsRemoved, IReadOnlyList<Guid> CasesOpened);

/// <summary>
/// Turns rule findings into incidents and meeting-exception cases (plan C2). New incidents are recorded once
/// (employee, rule, meeting occurrence). An immediate finding opens a case at once; otherwise a case opens when the
/// rule's threshold is reached within its window - incidents on an excused (dismissed) case stop counting. A case is
/// never duplicated: an open or acknowledged case of the same type takes the new incidents. A new case is routed through
/// the calendar policy (the reporting manager; log only when there is none) and the subject is told.
/// ONLEAVE / DOUBLEBOOK resolve themselves: once an upcoming occurrence no longer qualifies (declined, removed), its
/// incident is removed, and a case left with no incident is resolved as excused.
/// </summary>
public sealed class CalendarCaseService(
    ICalendarIncidentRepository incidents,
    IExceptionRepository exceptions,
    IInboxEscalationPolicyRegistry policies,
    INotificationDispatcher notifications,
    CoreEmployeeRepository employees,
    ILogger<CalendarCaseService>? logger = null,
    IInboxChangeNotifier? inbox = null)
{
    public const string Source = "calendar";
    public const string ReviewerTemplate = "calendar_exception_detected";
    public const string SubjectTemplate = "calendar_exception_raised_for_you";
    public static readonly IReadOnlySet<string> SelfResolvingRules = new HashSet<string> { CalendarRuleCodes.OnLeave, CalendarRuleCodes.DoubleBook };
    public static string RelatedEntityType => InboxNotificationNavigation.RelatedEntityType(Source, "meeting_exception");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger = logger ?? NullLogger<CalendarCaseService>.Instance;

    /// <param name="evaluatedRules">Rules evaluated in full for occurrences from <paramref name="reconcileFrom"/>: their stale
    /// self-resolving incidents are removed.</param>
    public async Task<CalendarCaseResult> ApplyAsync(
        Guid tenantId, Guid employeeId, IReadOnlyList<CalendarIncidentFinding> findings, IReadOnlyCollection<string> evaluatedRules,
        DateTimeOffset reconcileFrom, DateTimeOffset now, CancellationToken ct)
    {
        var windowStart = now.AddDays(-CalendarExceptionDefaults.LongWindowDays * 2);
        var stored = await incidents.ListForEmployeeAsync(tenantId, employeeId, windowStart, ct);
        var known = stored.ToDictionary(i => (i.RuleCode, i.EventId, i.OccurrenceStart));

        // 1. Self-resolving rules: an upcoming occurrence that no longer qualifies loses its incident.
        var current = findings.Select(f => (f.RuleCode, f.EventId, f.OccurrenceStart)).ToHashSet();
        var removed = 0;
        var touchedCases = new HashSet<Guid>();
        foreach (var stale in stored.Where(i => SelfResolvingRules.Contains(i.RuleCode) && evaluatedRules.Contains(i.RuleCode)
                     && i.OccurrenceStart >= reconcileFrom && !current.Contains((i.RuleCode, i.EventId, i.OccurrenceStart))).ToList())
        {
            if (stale.ExceptionId is Guid caseId)
                touchedCases.Add(caseId);
            incidents.Remove(stale);
            stored.Remove(stale);
            removed++;
        }

        // 2. Record new incidents.
        var added = new List<(CalendarMeetingIncident Incident, CalendarIncidentFinding Finding)>();
        foreach (var finding in findings)
        {
            if (known.ContainsKey((finding.RuleCode, finding.EventId, finding.OccurrenceStart)))
                continue;
            var incident = new CalendarMeetingIncident
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, RuleCode = finding.RuleCode, EventId = finding.EventId,
                OccurrenceStart = finding.OccurrenceStart, DetectedAt = now, Confidence = finding.Confidence,
                EvidenceJson = JsonSerializer.Serialize(finding.Evidence, Json), CreatedAt = now,
            };
            incidents.Add(incident);
            stored.Add(incident);
            known[(finding.RuleCode, finding.EventId, finding.OccurrenceStart)] = incident;
            added.Add((incident, finding));
        }

        // 3. Roll up into cases.
        var excused = await ExcusedCasesAsync(tenantId, stored, ct);
        var opened = new List<MonitoringException>();
        foreach (var (incident, finding) in added)
        {
            if (incident.ExceptionId is not null || !CalendarRuleCatalog.Specs.TryGetValue(incident.RuleCode, out var spec))
                continue;
            List<CalendarMeetingIncident> attach;
            if (finding.Immediate || spec.IsImmediate)
                attach = [incident];
            else
            {
                var from = incident.OccurrenceStart.AddDays(-spec.WindowDays);
                var counted = stored.Where(i => i.RuleCode == spec.Code && i.OccurrenceStart > from && i.OccurrenceStart <= incident.OccurrenceStart
                    && (i.ExceptionId is not Guid caseId || !excused.Contains(caseId))).ToList();
                if (counted.Count < spec.Threshold)
                    continue;
                attach = counted.Where(i => i.ExceptionId is null).ToList();
            }
            var (exception, isNew) = await CaseForAsync(tenantId, employeeId, spec, finding, opened, now, ct);
            foreach (var i in attach)
                i.ExceptionId = exception.Id;
            if (isNew)
                opened.Add(exception);
        }

        // 4. Cases whose last self-resolving incident went away are resolved as excused.
        foreach (var caseId in touchedCases.Where(id => stored.All(i => i.ExceptionId != id)))
        {
            if (await exceptions.GetByIdAsync(tenantId, caseId, ct) is { Status: ExceptionStatus.Open or ExceptionStatus.Acknowledged } resolved)
            {
                resolved.Status = ExceptionStatus.Resolved;
                resolved.Outcome = ExceptionOutcomes.Excused;
                resolved.ResolvedAt = now;
                resolved.ResolutionNote = "Resolved automatically: the meeting was declined or the employee was removed.";
                exceptions.Update(resolved);
            }
        }

        foreach (var exception in opened)
            await NotifyOpenedAsync(exception, ct);
        if (added.Count > 0 || removed > 0 || touchedCases.Count > 0)
            await incidents.SaveChangesAsync(ct);
        return new CalendarCaseResult(added.Count, removed, opened.Select(e => e.Id).ToList());
    }

    private async Task<HashSet<Guid>> ExcusedCasesAsync(Guid tenantId, IEnumerable<CalendarMeetingIncident> stored, CancellationToken ct)
    {
        var excused = new HashSet<Guid>();
        foreach (var caseId in stored.Where(i => i.ExceptionId is not null).Select(i => i.ExceptionId!.Value).Distinct())
        {
            if (await exceptions.GetByIdAsync(tenantId, caseId, ct) is { Outcome: ExceptionOutcomes.Excused })
                excused.Add(caseId);
        }
        return excused;
    }

    /// <summary>The open or acknowledged case of the rule's type, or a new one.</summary>
    private async Task<(MonitoringException Case, bool IsNew)> CaseForAsync(
        Guid tenantId, Guid employeeId, CalendarRuleSpec spec, CalendarIncidentFinding finding, List<MonitoringException> opened,
        DateTimeOffset now, CancellationToken ct)
    {
        if (opened.FirstOrDefault(e => e.Type == spec.Type) is { } justOpened)
            return (justOpened, false);
        if (await exceptions.GetOpenOrAcknowledgedAsync(tenantId, employeeId, spec.Type, ct) is { } existing)
            return (existing, false);
        var severity = finding.Severity ?? spec.Severity;
        var exception = new MonitoringException
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Type = spec.Type, Status = ExceptionStatus.Open,
            Title = spec.Title,
            Description = spec.IsImmediate || finding.Immediate
                ? $"{spec.Title}: \"{finding.Evidence.GetValueOrDefault("title") ?? "meeting"}\"."
                : $"{spec.Title}: {spec.Threshold} or more times in {spec.WindowDays} days.",
            MetadataJson = JsonSerializer.Serialize(new { source = Source, ruleCode = spec.Code, severity }, Json),
            DetectedAt = now,
        };
        await exceptions.AddAsync(exception, ct);
        return (exception, true);
    }

    /// <summary>The reviewers from the calendar policy (the reporting manager; nobody - only logged - when there is none) and
    /// the subject are told; their inboxes refresh once the case commits.</summary>
    private async Task NotifyOpenedAsync(MonitoringException exception, CancellationToken ct)
    {
        var item = new ItemRef(Source, "meeting_exception", exception.Id);
        var target = policies.For(Source, exception.Type.ToString()) is { } policy
            ? await policy.InitialAsync(exception.TenantId, new EscalationContext(item, exception.Type.ToString(), [], exception.EmployeeId, null, null), ct)
            : new EscalationTarget.LogOnly("No calendar policy registered.");
        var subject = await employees.GetByIdAsync(exception.TenantId, exception.EmployeeId, ct);
        var name = subject is null ? "An employee" : $"{subject.FirstName} {subject.LastName}".Trim();
        var placeholders = new Dictionary<string, string> { ["employeeName"] = name, ["title"] = exception.Title, ["description"] = exception.Description };

        var reviewerEmployees = new List<Guid>();
        if (target is EscalationTarget.Employees reviewers)
        {
            foreach (var recipient in reviewers.Recipients)
            {
                await notifications.SendTemplatedAsync(exception.TenantId, recipient.UserId, ReviewerTemplate, placeholders, RelatedEntityType, exception.Id, ct);
                if (recipient.EmployeeId is Guid reviewer)
                    reviewerEmployees.Add(reviewer);
            }
        }
        else if (target is EscalationTarget.LogOnly log)
            _logger.LogInformation("Calendar exception {ExceptionId} ({Type}) for employee {EmployeeId}: {Reason}",
                exception.Id, exception.Type, exception.EmployeeId, log.Reason);

        if (subject is { UserId: var userId } && userId != Guid.Empty)
            await notifications.SendTemplatedAsync(exception.TenantId, userId, SubjectTemplate, placeholders, RelatedEntityType, exception.Id, ct);
        if (inbox is not null)
            await inbox.EnqueueAsync(exception.TenantId, reviewerEmployees.Append(exception.EmployeeId), item, ct);
    }
}
