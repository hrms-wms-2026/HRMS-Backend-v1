using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Employee = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Calendar.Inbox;

public static class CalendarInboxKinds
{
    public const string Source = CalendarCaseService.Source;
    public const string MeetingException = "meeting_exception";
    public const string Renderer = "meeting-exception-evidence";

    public static readonly InboxKindDescriptor Descriptor = new(
        Source, MeetingException, "Meeting exception", InboxCategories.Alert,
        InboxCapabilities.Chat | InboxCapabilities.Resolve | InboxCapabilities.Escalate | InboxCapabilities.Revert
        | InboxCapabilities.SubjectParticipates,
        Renderer);

    /// <summary>The only evidence keys an incident may show (meeting facts). Anything else - e.g. a desktop-agent usage
    /// figure - is dropped: the Inbox never shows application names, samples or other monitoring data (plan C3).</summary>
    public static readonly IReadOnlySet<string> EvidenceKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "title", "start", "end", "rsvp", "rsvpAtStart", "joinedAt", "leftAt", "minutesLate", "attendedRatio", "declinedAt", "request",
        "overlapsTitle", "overlapMinutes", "day", "meetingHours", "meetings", "invites", "declined", "rate", "organizedByManager", "oneToOne",
        "workStart", "workEnd",
    };
}

/// <summary>
/// Calendar meeting-exception cases in the Inbox (plan C3): the Exception rows of the calendar types, with the X1 actions
/// (acknowledge / resolve excused|confirmed / escalate) through the shared <see cref="ExceptionCaseActions"/>. Received =
/// the cases of the people the caller reviews (the exception scope the commands check - scope = act); the employee sees
/// their own cases as info. The detail is meeting evidence only: each incident's meeting, time, RSVP and its history,
/// joined/left and an "estimated" badge for the desktop agent - never application names or activity samples.
/// Only the cases routed to the caller are listed (<see cref="ExceptionCaseRouting"/>: the subject's approver, then
/// whoever it was escalated to - never HR); anyone else in the exception scope can still open one by link, as info.
/// </summary>
public sealed class CalendarInboxSourceProvider(
    IExceptionRepository exceptions,
    IExceptionScopeResolver scope,
    IEmployeeRepository employees,
    ICalendarIncidentRepository incidents,
    ICalendarEventRepository events,
    ExceptionCaseActions actions,
    IConversationRepository conversations,
    ExceptionCaseRouting? routing = null) : IInboxSourceProvider, IInboxPersonCaseProvider
{
    public const int ListLimit = 500;

    public string Source => CalendarInboxKinds.Source;
    public string Label => "Calendar";

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
    {
        if (query.Category is { Count: > 0 } categories && !categories.Contains(InboxCategories.Alert))
            return [];
        var active = await exceptions.GetListAsync(tenantId, new ExceptionListFilter(null, null, null, null, ActiveOnly: true, Calendar: true), 1, ListLimit, ct);
        var reviewScope = active.Count == 0 ? null : await scope.ResolveAsync(forAction: true, active.Select(e => e.EmployeeId).Distinct().ToList(), ct);
        var routed = reviewScope is null || routing is null ? null : await routing.RoutedCasesAsync(tenantId, active, reviewScope, hrFallback: false, ct);

        var items = new List<(MonitoringException Case, string Direction, bool Actionable)>();
        foreach (var exception in active)
        {
            if (exception.EmployeeId == callerEmployeeId)
                items.Add((exception, InboxDirections.Info, false));
            else if (routed is not null)
            {
                if (routed.Contains(exception.Id))
                    items.Add((exception, InboxDirections.Received, true));
            }
            else if (reviewScope?.CanAct(exception.Id, exception.EmployeeId) == true)
                // HR is never in the calendar route (log only at the top), so HR sees the cases as info.
                items.Add((exception, reviewScope.IsHr ? InboxDirections.Info : InboxDirections.Received, true));
        }
        foreach (var subject in await conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct))
        {
            if (items.All(i => i.Case.Id != subject.Id) && await exceptions.GetByIdAsync(tenantId, subject.Id, ct) is { } added
                && CalendarExceptionTypes.IsCalendar(added.Type))
                items.Add((added, InboxDirections.Info, false));
        }

        var people = await PeopleAsync(tenantId, items.Select(i => i.Case.EmployeeId), ct);
        return items.Select(i => Item(i.Case, people, i.Direction, i.Actionable ? ExceptionInboxSourceProvider.Actions(i.Case) : [])).ToList();
    }

    /// <summary>The subject's meeting-exception cases of any status, as info. CanOpen follows GetDetailAsync.</summary>
    public async Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
        Guid tenantId, Guid subjectEmployeeId, string category, DateTimeOffset from, DateTimeOffset to, Guid callerEmployeeId, CancellationToken ct)
    {
        if (category != InboxCategories.Alert)
            return [];
        var cases = (await exceptions.GetListAsync(tenantId,
                new ExceptionListFilter(null, null, [subjectEmployeeId], null, Calendar: true, DetectedFrom: from, DetectedTo: to), 1, ListLimit, ct))
            .Where(e => CalendarExceptionTypes.IsCalendar(e.Type)).ToList();
        if (cases.Count == 0)
            return [];

        var isSubject = subjectEmployeeId == callerEmployeeId;
        var reviewScope = isSubject ? null : await scope.ResolveAsync(forAction: true, [subjectEmployeeId], ct);
        var added = isSubject ? [] : (await conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct)).ToHashSet();
        var people = await PeopleAsync(tenantId, [subjectEmployeeId], ct);
        return cases.Select(e => new InboxPersonCase(Item(e, people, InboxDirections.Info, []),
            isSubject || reviewScope?.CanAct(e.Id, e.EmployeeId) == true
            || added.Contains(new ItemRef(Source, CalendarInboxKinds.MeetingException, e.Id)))).ToList();
    }

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await ListAsync(tenantId, new InboxQuery(), callerEmployeeId, ct);
        static InboxFilterOption Option(string value, string label) => new(value, label);
        return
        [
            new("exception_type", "Exception type", true, items.Select(i => i.ActionType).Distinct().Select(t => Option(t, TypeLabel(t))).ToList()),
            new("severity", "Severity", true, items.Where(i => i.Severity is not null).Select(i => i.Severity!).Distinct().Select(s => Option(s, s)).ToList()),
            new("employee", "Employee", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
        ];
    }

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind != CalendarInboxKinds.MeetingException)
            return null;
        var exception = await exceptions.GetByIdAsync(tenantId, id, ct);
        if (exception is null || !CalendarExceptionTypes.IsCalendar(exception.Type))
            return null;

        var isSubject = exception.EmployeeId == callerEmployeeId;
        var reviewScope = isSubject ? null : await scope.ResolveAsync(forAction: true, [exception.EmployeeId], ct);
        var canAct = reviewScope?.CanAct(exception.Id, exception.EmployeeId) == true;
        var added = !isSubject && !canAct && (await conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct))
            .Contains(new ItemRef(Source, kind, id));
        if (!isSubject && !canAct && !added)
            return null;

        var people = await PeopleAsync(tenantId, [exception.EmployeeId], ct);
        // Routed to the caller = theirs to work; anyone else in scope opened it by link and sees it as info.
        var routedToMe = canAct && (routing is null
            ? !reviewScope!.IsHr
            : (await routing.RoutedCasesAsync(tenantId, [exception], reviewScope!, hrFallback: false, ct)).Contains(exception.Id));
        var direction = routedToMe ? InboxDirections.Received : InboxDirections.Info;
        var item = Item(exception, people, direction, routedToMe ? ExceptionInboxSourceProvider.Actions(exception) : []);
        return new InboxItemDetail(item, ExceptionInboxSourceProvider.Steps(exception), exception.Title, [], exception.Description,
            ExceptionInboxSourceProvider.Facts(exception), null, null, await EvidenceAsync(tenantId, exception, ct));
    }

    public Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
        => kind == CalendarInboxKinds.MeetingException
            ? actions.RunAsync(tenantId, new ItemRef(Source, kind, id), action, body, callerEmployeeId, ct)
            : Task.FromResult(Result<InboxActionResult>.UnprocessableEntity("This action is not available for this item."));

    /// <summary>The case's incidents as meeting facts (whitelisted keys only), each with the employee's RSVP history for
    /// that meeting and a confidence badge ("estimated" for the desktop agent).</summary>
    public async Task<string> EvidenceAsync(Guid tenantId, MonitoringException exception, CancellationToken ct)
    {
        var rows = await incidents.ListForExceptionAsync(tenantId, exception.Id, ct);
        var eventIds = rows.Select(r => r.EventId).Where(id => id != Guid.Empty).Distinct().ToList();
        var history = (await events.ListRsvpHistoryAsync(tenantId, exception.EmployeeId, eventIds, ct))
            .GroupBy(h => h.EventId).ToDictionary(g => g.Key, g => g.ToList());
        var list = rows.Select(r => new
        {
            id = r.Id,
            ruleCode = r.RuleCode,
            rule = CalendarRuleCatalog.Specs.TryGetValue(r.RuleCode, out var spec) ? spec.Title : r.RuleCode,
            occurrenceStart = r.OccurrenceStart,
            confidence = r.Confidence,
            estimated = r.Confidence == CalendarIncidentConfidence.Agent,
            facts = Facts(r.EvidenceJson),
            rsvpHistory = history.TryGetValue(r.EventId, out var changes)
                ? changes.Select(h => new { from = h.FromStatus, to = h.ToStatus, at = h.ChangedAt }).ToList()
                : [],
        }).ToList();
        string? severity = null;
        try
        {
            using var meta = JsonDocument.Parse(exception.MetadataJson);
            if (meta.RootElement.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String)
                severity = s.GetString();
        }
        catch (JsonException)
        {
        }
        return JsonSerializer.Serialize(new { severity, incidents = list }, Web);
    }

    private static Dictionary<string, JsonElement> Facts(string evidenceJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(evidenceJson);
            return doc.RootElement.ValueKind != JsonValueKind.Object
                ? []
                : doc.RootElement.EnumerateObject().Where(p => CalendarInboxKinds.EvidenceKeys.Contains(p.Name))
                    .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---- mapping --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<IReadOnlyDictionary<Guid, Employee>> PeopleAsync(Guid tenantId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new Dictionary<Guid, Employee>() : await employees.ListByIdsAsync(tenantId, list, ct);
    }

    public static string Severity(MonitoringException e)
    {
        try
        {
            using var meta = JsonDocument.Parse(e.MetadataJson);
            return meta.RootElement.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : CalendarRuleCatalog.Low;
        }
        catch (JsonException)
        {
            return CalendarRuleCatalog.Low;
        }
    }

    private static string TypeLabel(string type) => type.StartsWith("Meeting", StringComparison.Ordinal)
        ? "Meeting " + System.Text.RegularExpressions.Regex.Replace(type["Meeting".Length..], "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()
        : type;

    private static InboxItem Item(MonitoringException e, IReadOnlyDictionary<Guid, Employee> people, string direction, IReadOnlyList<InboxAction> actions)
    {
        var employee = people.GetValueOrDefault(e.EmployeeId);
        var name = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
        var severity = Severity(e);
        var facets = new Dictionary<string, IReadOnlyList<string>>
        {
            ["exception_type"] = [e.Type.ToString()],
            ["employee"] = [e.EmployeeId.ToString()],
            ["severity"] = [severity],
        };
        if (employee?.DepartmentId is { } department)
            facets["department"] = [department.ToString()];
        return new InboxItem(
            CalendarInboxKinds.Source, CalendarInboxKinds.MeetingException, e.Id, InboxCategories.Alert, e.Type.ToString(),
            $"{e.Title} - {name}", e.Description, e.Status.ToString().ToLowerInvariant(), direction, severity,
            "legal_entity", employee?.LegalEntityId, null, new InboxPerson(e.EmployeeId, name), null,
            e.DetectedAt, e.ResolvedAt ?? e.EscalatedAt ?? e.AcknowledgedAt ?? e.DetectedAt, actions, 0,
            e.Status != ExceptionStatus.Resolved, facets);
    }
}

/// <summary>
/// The chat of a calendar meeting-exception case (plan C3): the employee it is about and the reporting manager from the
/// calendar policy; whoever it is escalated to joins as a viewer (the escalation recorder). Viewing and the export follow
/// the caller's own exception scope, re-checked live.
/// </summary>
public sealed class CalendarConversationSubjectResolver(
    IExceptionRepository exceptions,
    IExceptionScopeResolver scope,
    IInboxEscalationPolicyRegistry policies,
    ICurrentUser currentUser,
    ICallerIdentityResolver identity) : IConversationSubjectResolver
{
    public string Source => CalendarInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        if (subject.Kind != CalendarInboxKinds.MeetingException)
            return null;
        var exception = await exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is null || !CalendarExceptionTypes.IsCalendar(exception.Type))
            return null;
        var people = new List<Guid> { exception.EmployeeId };
        if (policies.For(subject.Source, exception.Type.ToString()) is { } policy
            && await policy.InitialAsync(tenantId, new EscalationContext(subject, exception.Type.ToString(), [], exception.EmployeeId, null, null), ct)
                is EscalationTarget.Employees reviewers)
            people.AddRange(reviewers.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value));
        return new ConversationSubject(people.Distinct().ToList(), exception.Title, null, SubjectEmployeeId: exception.EmployeeId);
    }

    public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => InScopeAsync(tenantId, subject, employeeId, forAction: false, ct);

    public Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => InScopeAsync(tenantId, subject, employeeId, forAction: true, ct);

    private async Task<bool> InScopeAsync(Guid tenantId, ItemRef subject, Guid employeeId, bool forAction, CancellationToken ct)
    {
        if (await identity.ResolveCallerEmployeeIdAsync(tenantId, currentUser.UserId, ct) != employeeId)
            return false;
        var exception = await exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is null || !CalendarExceptionTypes.IsCalendar(exception.Type))
            return false;
        var resolved = await scope.ResolveAsync(forAction, [exception.EmployeeId], ct);
        return resolved?.CanAct(exception.Id, exception.EmployeeId) == true;
    }
}

/// <summary>The 30-minute undo of a decision on a calendar meeting-exception case: X1's exception reverter for the calendar source.</summary>
public sealed class CalendarMeetingExceptionReverter(
    IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, IExceptionRepository exceptions, IExceptionScopeResolver scope)
    : ExceptionCaseReverter(journal, unitOfWork, clock, exceptions, scope)
{
    public override string Source => CalendarInboxKinds.Source;
    public override string Kind => CalendarInboxKinds.MeetingException;
}
