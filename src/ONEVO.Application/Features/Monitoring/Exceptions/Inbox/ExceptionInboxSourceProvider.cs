using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Employee = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// Monitoring exception cases in the Inbox (plan X1). The list is the cases routed to the caller - the same people
/// ReportingLineThenHrPolicy alerts, so nobody above sees a case just because their coverage is wide: the subject's
/// attendance approver (attendance:approve, ResolveApprovalInboxScopeAsync), whoever the latest escalation went to,
/// and HR only for cases of employees nobody can review (or escalated before the journal existed). Anyone else in the
/// exception scope can still open a case by link, as info. The employee a case is about sees their own open cases as
/// info - facts only (title, description, when, status), never the monitoring evidence (P10).
/// </summary>
public sealed class ExceptionInboxSourceProvider : IInboxSourceProvider, IInboxPersonCaseProvider
{
    public const int ListLimit = 500;

    private readonly IExceptionRepository _exceptions;
    private readonly IExceptionScopeResolver _scope;
    private readonly IEmployeeRepository _employees;
    private readonly ISender _sender;
    private readonly ExceptionCaseActions _actions;
    private readonly IConversationRepository _conversations;
    private readonly ExceptionCaseRouting _routing;

    public ExceptionInboxSourceProvider(
        IExceptionRepository exceptions, IExceptionScopeResolver scope, IEmployeeRepository employees, ISender sender,
        ExceptionCaseActions actions, IConversationRepository conversations, ExceptionCaseRouting routing)
    {
        _routing = routing;
        _exceptions = exceptions;
        _scope = scope;
        _employees = employees;
        _sender = sender;
        _actions = actions;
        _conversations = conversations;
    }

    public string Source => ExceptionInboxKinds.Source;
    public string Label => "Monitoring";

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
    {
        if (query.Category is { Count: > 0 } categories && !categories.Contains(InboxCategories.Alert))
            return [];

        var active = (await _exceptions.GetListAsync(tenantId, new ExceptionListFilter(null, null, null, null, ActiveOnly: true), 1, ListLimit, ct))
            .Where(e => !ExceptionInboxKinds.IsCalendarType(e.Type)).ToList();
        var scope = active.Count == 0 ? null : await _scope.ResolveAsync(forAction: true, active.Select(e => e.EmployeeId).Distinct().ToList(), ct);
        var routed = scope is null ? [] : await _routing.RoutedCasesAsync(tenantId, active, scope, hrFallback: true, ct);

        var items = new List<(MonitoringException Case, string Direction, bool Actionable)>();
        foreach (var exception in active)
        {
            if (exception.EmployeeId == callerEmployeeId)
                items.Add((exception, InboxDirections.Info, false));
            else if (routed.Contains(exception.Id))
                items.Add((exception, InboxDirections.Received, true));
        }
        foreach (var subject in await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct))
        {
            if (items.All(i => i.Case.Id != subject.Id) && await _exceptions.GetByIdAsync(tenantId, subject.Id, ct) is { } added)
                items.Add((added, InboxDirections.Info, false));
        }

        var people = await PeopleAsync(tenantId, items.Select(i => i.Case.EmployeeId), ct);
        return items.Select(i => Item(i.Case, people, i.Direction, i.Actionable ? Actions(i.Case) : [])).ToList();
    }

    /// <summary>The subject's monitoring cases of any status, as info. CanOpen follows GetDetailAsync: the subject,
    /// anyone in the exception scope, or someone added to the case's chat.</summary>
    public async Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
        Guid tenantId, Guid subjectEmployeeId, string category, DateTimeOffset from, DateTimeOffset to, Guid callerEmployeeId, CancellationToken ct)
    {
        if (category != InboxCategories.Alert)
            return [];
        var cases = (await _exceptions.GetListAsync(tenantId,
                new ExceptionListFilter(null, null, [subjectEmployeeId], null, Calendar: false, DetectedFrom: from, DetectedTo: to), 1, ListLimit, ct))
            .Where(e => !ExceptionInboxKinds.IsCalendarType(e.Type)).ToList();
        if (cases.Count == 0)
            return [];

        var isSubject = subjectEmployeeId == callerEmployeeId;
        var scope = isSubject ? null : await _scope.ResolveAsync(forAction: true, [subjectEmployeeId], ct);
        var added = isSubject ? [] : (await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct)).ToHashSet();
        var people = await PeopleAsync(tenantId, [subjectEmployeeId], ct);
        return cases.Select(e => new InboxPersonCase(Item(e, people, InboxDirections.Info, []),
            isSubject || scope?.CanAct(e.Id, e.EmployeeId) == true || added.Contains(new ItemRef(Source, ExceptionInboxKinds.Case, e.Id)))).ToList();
    }

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var items = await ListAsync(tenantId, new InboxQuery(), callerEmployeeId, ct);
        static InboxFilterOption Option(string value, string label) => new(value, label);
        return
        [
            new("exception_type", "Exception type", true, items.Select(i => i.ActionType).Distinct().Select(t => Option(t, Humanize(t))).ToList()),
            new("severity", "Severity", true, items.Where(i => i.Severity is not null).Select(i => i.Severity!).Distinct()
                .Select(s => Option(s, Humanize(s))).ToList()),
            new("employee", "Employee", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
        ];
    }

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        if (kind != ExceptionInboxKinds.Case)
            return null;
        var exception = await _exceptions.GetByIdAsync(tenantId, id, ct);
        if (exception is null || ExceptionInboxKinds.IsCalendarType(exception.Type))
            return null;
        var people = await PeopleAsync(tenantId, [exception.EmployeeId], ct);

        // The employee it is about: facts only, never the monitoring evidence (P10).
        if (exception.EmployeeId == callerEmployeeId)
            return new InboxItemDetail(Item(exception, people, InboxDirections.Info, []), Steps(exception), exception.Title,
                [], exception.Description, Facts(exception), null, null, null);

        var scope = await _scope.ResolveAsync(forAction: true, [exception.EmployeeId], ct);
        var inScope = scope?.CanAct(exception.Id, exception.EmployeeId) == true;
        // Routed to the caller = theirs to work; anyone else in scope may still open it by link, as info.
        var canAct = inScope && (await _routing.RoutedCasesAsync(tenantId, [exception], scope!, hrFallback: true, ct)).Contains(exception.Id);
        var added = !inScope && (await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct))
            .Contains(new ItemRef(Source, kind, id));
        if (!inScope && !added)
            return null;

        string? evidence = null;
        if (inScope && await _sender.Send(new GetExceptionEvidenceQuery(id), ct) is { IsSuccess: true } loaded)
            evidence = JsonSerializer.Serialize(loaded.Value, Web);
        var direction = canAct ? InboxDirections.Received : InboxDirections.Info;
        var actions = canAct ? await WithEscalationTargetsAsync(tenantId, exception, Actions(exception), callerEmployeeId, ct) : [];
        return new InboxItemDetail(Item(exception, people, direction, actions), Steps(exception), exception.Title,
            [], exception.Description, Facts(exception), null, null, evidence);
    }

    public Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
        => kind == ExceptionInboxKinds.Case
            ? _actions.RunAsync(tenantId, new ItemRef(Source, kind, id), action, body, callerEmployeeId, ct)
            : Task.FromResult(Result<InboxActionResult>.UnprocessableEntity("This action is not available for this item."));

    // ---- mapping --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<IReadOnlyDictionary<Guid, Employee>> PeopleAsync(Guid tenantId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new Dictionary<Guid, Employee>() : await _employees.ListByIdsAsync(tenantId, list, ct);
    }

    /// <summary>The escalate dialog's preview (detail only - it costs a few lookups): "escalateTo" names who the policy
    /// picks, "escalateCandidates" (JSON) everyone the caller may pick instead.</summary>
    private async Task<IReadOnlyList<InboxAction>> WithEscalationTargetsAsync(
        Guid tenantId, MonitoringException exception, IReadOnlyList<InboxAction> actions, Guid callerEmployeeId, CancellationToken ct)
    {
        if (actions.All(a => a.Key != ExceptionCaseInboxActions.Escalate))
            return actions;
        var candidates = await _actions.EscalationCandidatesAsync(
            tenantId, new ItemRef(Source, ExceptionInboxKinds.Case, exception.Id), exception, callerEmployeeId, ct);
        var defaults = candidates.Where(c => c.IsDefault).ToList();
        var escalateTo = defaults.Count switch
        {
            0 => "",
            1 => defaults[0].Name,
            _ => defaults.All(d => d.Role == "HR") ? $"HR ({defaults.Count} people)" : $"{defaults.Count} people",
        };
        var meta = new Dictionary<string, string>
        {
            ["escalateTo"] = escalateTo,
            ["escalateCandidates"] = JsonSerializer.Serialize(
                candidates.Select(c => new { c.EmployeeId, c.Name, c.Position, c.AvatarFileId, c.IsDefault }), Web),
        };
        return actions.Select(a => a.Key == ExceptionCaseInboxActions.Escalate ? a with { Meta = meta } : a).ToList();
    }

    public static IReadOnlyList<InboxAction> Actions(MonitoringException exception)
    {
        // No manual Acknowledge: an open case either gets resolved, escalated by hand, or escalates itself when its SLA runs out.
        var actions = new List<InboxAction>();
        if (exception.Status != ExceptionStatus.Resolved)
            actions.Add(new InboxAction(ExceptionCaseInboxActions.Resolve, "Resolve", "primary", NeedsComment: true, NeedsPayload: true,
                new Dictionary<string, string> { ["outcomes"] = $"{ExceptionOutcomes.Excused},{ExceptionOutcomes.Confirmed}" }));
        if (exception.Status is ExceptionStatus.Open or ExceptionStatus.Acknowledged)
            actions.Add(new InboxAction(ExceptionCaseInboxActions.Escalate, "Escalate", "danger", NeedsComment: true, NeedsPayload: false));
        return actions;
    }

    private static string Severity(MonitoringException e) => e.Type == ExceptionType.IdentityAnomaly ? "high" : "medium";

    private static InboxItem Item(MonitoringException e, IReadOnlyDictionary<Guid, Employee> people, string direction, IReadOnlyList<InboxAction> actions)
    {
        var employee = people.GetValueOrDefault(e.EmployeeId);
        var name = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
        var facets = new Dictionary<string, IReadOnlyList<string>>
        {
            ["exception_type"] = [e.Type.ToString()],
            ["employee"] = [e.EmployeeId.ToString()],
            ["severity"] = [Severity(e)],
        };
        if (employee?.DepartmentId is { } department)
            facets["department"] = [department.ToString()];
        return new InboxItem(
            ExceptionInboxKinds.Source, ExceptionInboxKinds.Case, e.Id, InboxCategories.Alert, e.Type.ToString(),
            $"{e.Title} - {name}", e.Description, e.Status.ToString().ToLowerInvariant(), direction, Severity(e),
            "legal_entity", employee?.LegalEntityId, null, new InboxPerson(e.EmployeeId, name), null,
            e.DetectedAt, e.ResolvedAt ?? e.EscalatedAt ?? e.AcknowledgedAt ?? e.DetectedAt, actions, 0,
            e.Status != ExceptionStatus.Resolved, facets);
    }

    public static IReadOnlyList<InboxStep> Steps(MonitoringException e)
    {
        var steps = new List<InboxStep> { new("detected", "Detected", null, e.DetectedAt, InboxStepStates.Done, null) };
        if (e.AcknowledgedAt is { } ack)
            steps.Add(new InboxStep("acknowledged", "Acknowledged", null, ack, InboxStepStates.Done, null));
        if (e.EscalatedAt is { } esc)
            steps.Add(new InboxStep("escalated", e.EscalatedById is null ? "Escalated automatically" : "Escalated", null, esc, InboxStepStates.Done, null));
        steps.Add(e.ResolvedAt is { } res
            ? new InboxStep("resolved", $"Resolved - {Humanize(e.Outcome ?? ExceptionOutcomes.Confirmed)}", null, res, InboxStepStates.Done, e.ResolutionNote)
            : new InboxStep("resolved", "Waiting for review", null, null, InboxStepStates.Current, null));
        return steps;
    }

    public static IReadOnlyDictionary<string, string> Facts(MonitoringException e)
    {
        var facts = new Dictionary<string, string> { ["Status"] = e.Status.ToString(), ["Detected"] = e.DetectedAt.ToString("yyyy-MM-dd HH:mm") };
        if (e.Outcome is { } outcome)
            facts["Outcome"] = Humanize(outcome);
        return facts;
    }

    private static string Humanize(string value)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(value.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
    }
}
