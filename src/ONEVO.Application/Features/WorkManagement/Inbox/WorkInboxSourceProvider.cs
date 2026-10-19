using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetApprovalDetail;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.CancelObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>
/// Work Management in the Inbox: approval requests and module invitations (the same rows and visibility
/// as the project Approvals feed, over every project the caller can see) plus project-monitor alerts the
/// caller is a recipient of. Details and actions go through the existing WM queries and commands, so
/// every permission rule (who may decide, cancel, revert) stays where it already lives. People added to an
/// item's conversation also get the item, read-only: they can view it and chat but never decide (D6).
/// </summary>
public sealed class WorkInboxSourceProvider : IInboxSourceProvider
{
    public const int ResolvedAlertDays = 30;
    private const string UnknownAction = "This action is not available for this item.";

    private readonly IProjectRepository _projects;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly ApprovalFeedBuilder _feed;
    private readonly IMonitorAlertRepository _alerts;
    private readonly MonitorAlertAudience _audience;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISender _sender;
    private readonly IDateTimeProvider _clock;
    private readonly IConversationRepository _conversations;
    private readonly IInboxDecisionJournal? _journal;
    private readonly WorkAlertEscalation? _escalation;

    public WorkInboxSourceProvider(
        IProjectRepository projects, IProjectMemberInvitationRepository invitations, IWorkApprovalRequestRepository requests,
        ApprovalFeedBuilder feed, IMonitorAlertRepository alerts, MonitorAlertAudience audience, IWorkHierarchyService hierarchy,
        IWorkTaskRepository tasks, ICallerIdentityResolver identity, ISender sender, IDateTimeProvider clock,
        IConversationRepository conversations, IInboxDecisionJournal? journal = null, WorkAlertEscalation? escalation = null)
    {
        _journal = journal;
        _escalation = escalation;
        _projects = projects;
        _invitations = invitations;
        _requests = requests;
        _feed = feed;
        _alerts = alerts;
        _audience = audience;
        _hierarchy = hierarchy;
        _tasks = tasks;
        _identity = identity;
        _sender = sender;
        _clock = clock;
        _conversations = conversations;
    }

    public string Source => WorkInboxKinds.Source;
    public string Label => "Work";

    public async Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
        => (await CollectAsync(tenantId, callerEmployeeId, query, ct)).Items;

    public async Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        var (items, moduleTitles, projects) = await CollectAsync(tenantId, callerEmployeeId, null, ct);

        static InboxFilterOption Option(string value, string label) => new(value, label);
        return
        [
            new("project", "Project", true, projects.OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
            new("type", "Type", true, items.Select(i => i.ActionType).Distinct().OrderBy(t => t)
                .Select(t => Option(t, WorkInboxItemMapper.TypeLabel(t))).ToList()),
            new("status", "Status", true, items.Select(i => i.Status).Distinct().OrderBy(s => s)
                .Select(s => Option(s, WorkInboxItemMapper.Capitalize(s))).ToList()),
            new("direction", "Direction", true, items.Select(i => i.Direction).Distinct().OrderBy(d => d)
                .Select(d => Option(d, WorkInboxItemMapper.Capitalize(d))).ToList()),
            new("requestedBy", "Requested by", true, items.Where(i => i.RequestedBy is not null).Select(i => i.RequestedBy!)
                .DistinctBy(p => p.Id).OrderBy(p => p.Name).Select(p => Option(p.Id.ToString(), p.Name)).ToList()),
            new(WorkInboxItemMapper.ModuleFacet, "Module", true, moduleTitles.OrderBy(m => m.Value)
                .Select(m => Option(m.Key.ToString(), m.Value)).ToList()),
        ];
    }

    public async Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
    {
        var detail = await GetScopedDetailAsync(tenantId, kind, id, callerEmployeeId, ct);
        if (detail is not null)
            return detail;

        var added = await _conversations.ListAddedSubjectsAsync(tenantId, callerEmployeeId, Source, ct);
        return added.Contains(new ItemRef(Source, kind, id))
            ? await ReadOnlyDetailAsync(tenantId, kind, id, callerEmployeeId, ct)
            : null;
    }

    /// <summary>The item as the caller sees it from their own Work access (feed, invitation, alert audience) -
    /// not through having been added to its chat. Null when they have no such access.</summary>
    public async Task<InboxItemDetail?> GetScopedDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
        => kind switch
        {
            WorkInboxKinds.Approval => await ApprovalDetailAsync(tenantId, id, callerEmployeeId, ct),
            WorkInboxKinds.Invitation => await InvitationDetailAsync(tenantId, id, callerEmployeeId, ct),
            WorkInboxKinds.MonitorAlert => await AlertDetailAsync(tenantId, id, callerEmployeeId, checkAudience: true, ct),
            _ => null,
        };

    public async Task<Result<InboxActionResult>> ExecuteActionAsync(
        Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
    {
        var comment = BodyString(body, "comment");
        // The approval's status before the decision, for the journal row (plan W1).
        var before = kind == WorkInboxKinds.Approval ? (await _requests.GetByIdForTenantAsync(tenantId, id, ct))?.Status : null;
        switch (kind, action)
        {
            case (WorkInboxKinds.Approval, WorkInboxActions.Approve):
                return await AfterApprovalAsync(tenantId, id, callerEmployeeId, await _sender.Send(
                    new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Approve, BodyString(body, "editedPayloadJson"), comment), ct),
                    InboxJournalActions.Approve, before, comment, ct);
            case (WorkInboxKinds.Approval, WorkInboxActions.Reject):
                return await AfterApprovalAsync(tenantId, id, callerEmployeeId, await _sender.Send(
                    new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Reject, null, comment), ct),
                    InboxJournalActions.Reject, before, comment, ct);
            case (WorkInboxKinds.Approval, WorkInboxActions.Cancel):
                return await AfterApprovalAsync(tenantId, id, callerEmployeeId, await _sender.Send(
                    new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Cancel, null, null), ct),
                    InboxJournalActions.Cancel, before, comment, ct);
            case (WorkInboxKinds.Approval, WorkInboxActions.Revert):
                return await AfterApprovalAsync(tenantId, id, callerEmployeeId,
                    await _sender.Send(new RevertWorkApprovalRequestCommand(id), ct), InboxJournalActions.Revert, before, comment, ct);
            case (WorkInboxKinds.MonitorAlert, WorkInboxActions.Escalate):
                return await EscalateAlertAsync(tenantId, id, callerEmployeeId, comment, ct);
            case (WorkInboxKinds.Invitation, WorkInboxActions.AcceptInvitation):
                return await AfterInvitationAsync(tenantId, id, callerEmployeeId, await _sender.Send(new AcceptObjectiveInvitationCommand(id), ct), ct);
            case (WorkInboxKinds.Invitation, WorkInboxActions.DeclineInvitation):
                return await AfterInvitationAsync(tenantId, id, callerEmployeeId, await _sender.Send(new RejectObjectiveInvitationCommand(id), ct), ct);
            case (WorkInboxKinds.Invitation, WorkInboxActions.Cancel):
                return await AfterInvitationAsync(tenantId, id, callerEmployeeId, await _sender.Send(new CancelObjectiveInvitationCommand(id), ct), ct);
            default:
                return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        }
    }

    // ---- listing ---------------------------------------------------------------------------------

    private sealed record Collected(IReadOnlyList<InboxItem> Items, IReadOnlyDictionary<Guid, string> ModuleTitles, IReadOnlyList<Project> Projects);

    /// <summary>Loads the caller's items. The router applies every filter afterwards; the project and category
    /// filters are also used here, only to skip loading what they would drop anyway.</summary>
    private async Task<Collected> CollectAsync(Guid tenantId, Guid caller, InboxQuery? query, CancellationToken ct)
    {
        var projects = await VisibleProjectsAsync(tenantId, caller, ct);
        var wantRequests = query?.Category is not { Count: > 0 } categories || categories.Contains(InboxCategories.Request);
        var wantAlerts = query?.Category is not { Count: > 0 } alertCategories || alertCategories.Contains(InboxCategories.Alert);
        var items = new List<InboxItem>();
        var moduleTitles = new Dictionary<Guid, string>();
        var resolvedSince = _clock.UtcNow.AddDays(-ResolvedAlertDays);

        foreach (var project in projects)
        {
            if (query?.ContextIds is { Count: > 0 } contextIds && !contextIds.Contains(project.Id))
                continue;
            if (wantRequests)
            {
                foreach (var entry in await _feed.BuildAsync(tenantId, project, caller, ct))
                {
                    items.Add(WorkInboxItemMapper.FromFeed(entry, caller));
                    if (entry.Item.ModuleId is { } moduleId && entry.Item.ModuleTitle is { } title)
                        moduleTitles.TryAdd(moduleId, title);
                }
            }
            if (!wantAlerts)
                continue;

            var alerts = await _alerts.ListOpenOrResolvedSinceAsync(tenantId, project.Id, resolvedSince, ct);
            if (alerts.Count == 0)
                continue;
            var audience = await _audience.ResolveAsync(tenantId, project.Id, alerts, ct);
            var mine = alerts.Where(a => audience.GetValueOrDefault(a.Id)?.Contains(caller) == true).ToList();
            if (mine.Count == 0)
                continue;

            var contexts = await AlertContextsAsync(tenantId, project, mine, ct);
            foreach (var alert in mine)
            {
                var context = contexts[alert.Id];
                items.Add(WithEscalate(WorkInboxItemMapper.FromAlert(alert, project.Id, context.Context), alert));
                if (context.Context.ModuleId is { } moduleId && context.ModuleTitle is { } title)
                    moduleTitles.TryAdd(moduleId, title);
            }
        }
        // Items whose conversation the caller was added to, read-only.
        var listed = items.Select(i => new ItemRef(i.Source, i.Kind, i.Id)).ToHashSet();
        foreach (var subject in await _conversations.ListAddedSubjectsAsync(tenantId, caller, Source, ct))
        {
            if (listed.Contains(subject))
                continue;
            var readOnly = await ReadOnlyDetailAsync(tenantId, subject.Kind, subject.Id, caller, ct);
            if (readOnly is not null)
                items.Add(readOnly.Item);
        }
        return new Collected(items, moduleTitles, projects);
    }

    /// <summary>The project list's visibility (active membership) plus projects the caller has a pending
    /// module invitation in - an invitee is not a member until they accept.</summary>
    private async Task<IReadOnlyList<Project>> VisibleProjectsAsync(Guid tenantId, Guid caller, CancellationToken ct)
    {
        var (memberProjects, _) = await _projects.ListForMemberAsync(tenantId, caller, 0, int.MaxValue, null, "asc", ct);
        var known = memberProjects.Select(p => p.Id).ToHashSet();
        var invitedProjectIds = (await _invitations.ListPendingForEmployeeAsync(tenantId, caller, ct))
            .Select(i => i.ProjectId).Where(id => !known.Contains(id)).Distinct().ToList();
        if (invitedProjectIds.Count == 0)
            return memberProjects;
        var invited = await _projects.ListByIdsAsync(tenantId, invitedProjectIds, ct);
        return memberProjects.Concat(invited).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, (WorkAlertContext Context, string? ModuleTitle)>> AlertContextsAsync(
        Guid tenantId, Project project, IReadOnlyList<MonitorAlert> alerts, CancellationToken ct)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, project.Id, ct);
        var taskIds = alerts.Where(a => a.TargetType == MonitorTargetTypes.Task).Select(a => a.TargetId).Distinct().ToList();
        var moduleByTask = taskIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _tasks.GetObjectiveIdsByTaskIdsAsync(tenantId, taskIds, ct);
        var allModules = tree.Root is { } root ? tree.AtOrBelow([root.Id]) : new HashSet<Guid>();
        var achievedScope = tree.AtOrBelow(allModules.Where(id => tree.Get(id)?.IsAchieved == true));

        return alerts.ToDictionary(a => a.Id, a =>
        {
            Guid? moduleId = a.TargetType switch
            {
                MonitorTargetTypes.Module => a.TargetId,
                MonitorTargetTypes.Task => moduleByTask.TryGetValue(a.TargetId, out var m) ? m : null,
                _ => null,
            };
            var module = moduleId is { } id ? tree.Get(id) : null;
            var title = module is null ? null : module.IsDefault ? project.Name : module.Title;
            var inAchieved = moduleId is { } mid && achievedScope.Contains(mid);
            return (new WorkAlertContext(project.Name, moduleId, inAchieved), title);
        });
    }

    // ---- details ---------------------------------------------------------------------------------

    private async Task<InboxItemDetail?> ApprovalDetailAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var detail = await _sender.Send(new GetApprovalDetailQuery(id, ApprovalFeedSources.Engine), ct);
        if (!detail.IsSuccess)
            return null;
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        if (request is null)
            return null;

        var d = detail.Value!;
        var item = WorkInboxItemMapper.FromFeed(new ApprovalFeedEntry(request.ProjectId, d.Item, request), caller, d.CanRevert);
        string? revertedByName = null;
        if (request.RevertedByEmployeeId is { } revertedBy)
            revertedByName = (await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [revertedBy], ct)).GetValueOrDefault(revertedBy);

        return new InboxItemDetail(
            item, WorkInboxItemMapper.ApprovalSteps(d.Item, request, revertedByName), d.Item.Summary,
            WorkInboxItemMapper.ChangeSet(d.Fields), d.Note, ExplanationDetails: null,
            WorkInboxItemMapper.Target(request.ProjectId, request.TargetType, request.TargetId, request.ActionType),
            d.CanEditPayload ? d.RequestedPayloadJson : null);
    }

    private async Task<InboxItemDetail?> InvitationDetailAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var detail = await _sender.Send(new GetApprovalDetailQuery(id, ApprovalFeedSources.Invitation), ct);
        if (!detail.IsSuccess)
            return null;
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, id, ct);
        if (invitation is null)
            return null;

        var d = detail.Value!;
        var item = WorkInboxItemMapper.FromFeed(new ApprovalFeedEntry(invitation.ProjectId, d.Item, null), caller);
        var explanation = d.Invitation is { } inv
            ? $"{inv.InvitedByName} invited {inv.InviteeName} to \"{inv.ModuleTitle}\" as {inv.InviteType}."
            : d.Item.Summary;
        return new InboxItemDetail(
            item, WorkInboxItemMapper.InvitationSteps(d.Item), d.Item.Summary, [], explanation, ExplanationDetails: null,
            WorkInboxItemMapper.Target(invitation.ProjectId, "module", invitation.ObjectiveId), EditablePayloadJson: null);
    }

    private async Task<InboxItemDetail?> AlertDetailAsync(Guid tenantId, Guid id, Guid caller, bool checkAudience, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(tenantId, id, ct);
        if (alert is null)
            return null;
        if (checkAudience)
        {
            var audience = await _audience.ResolveAsync(tenantId, alert.ProjectId, [alert], ct);
            if (audience.GetValueOrDefault(alert.Id)?.Contains(caller) != true)
                return null;
        }
        var project = await _projects.GetByIdForTenantAsync(tenantId, alert.ProjectId, ct);
        if (project is null)
            return null;

        var context = (await AlertContextsAsync(tenantId, project, [alert], ct))[alert.Id].Context;
        var item = WithEscalate(WorkInboxItemMapper.FromAlert(alert, project.Id, context), alert);
        return new InboxItemDetail(
            item, WorkInboxItemMapper.AlertSteps(alert, context.InAchievedModule), alert.Message, [],
            alert.Message, WorkInboxItemMapper.AlertDetails(alert.DetailsJson),
            WorkInboxItemMapper.Target(project.Id, alert.TargetType, alert.TargetId), EditablePayloadJson: null);
    }

    /// <summary>The item as seen by someone added to its conversation: no decision actions, not waiting on them.</summary>
    private async Task<InboxItemDetail?> ReadOnlyDetailAsync(Guid tenantId, string kind, Guid id, Guid caller, CancellationToken ct)
    {
        var detail = kind switch
        {
            WorkInboxKinds.Approval => await ReadOnlyApprovalAsync(tenantId, id, caller, ct),
            WorkInboxKinds.Invitation => await ReadOnlyInvitationAsync(tenantId, id, caller, ct),
            WorkInboxKinds.MonitorAlert => await AlertDetailAsync(tenantId, id, caller, checkAudience: false, ct),
            _ => null,
        };
        return detail is null ? null : detail with
        {
            Item = detail.Item with
            {
                AvailableActions = detail.Item.AvailableActions.Where(a => a.Key == WorkInboxActions.OpenTarget).ToList(),
                Direction = InboxDirections.Info,
                IsPending = false
            },
            EditablePayloadJson = null
        };
    }

    private async Task<InboxItemDetail?> ReadOnlyApprovalAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        var project = request is null ? null : await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (request is null || project is null)
            return null;
        var entry = (await _feed.MapAsync(tenantId, project, [request], [], caller, ct))[0];
        return new InboxItemDetail(
            WorkInboxItemMapper.FromFeed(entry, caller), WorkInboxItemMapper.ApprovalSteps(entry.Item, request, revertedByName: null),
            entry.Item.Summary, [], Explanation: null, ExplanationDetails: null,
            WorkInboxItemMapper.Target(project.Id, request.TargetType, request.TargetId, request.ActionType), EditablePayloadJson: null);
    }

    private async Task<InboxItemDetail?> ReadOnlyInvitationAsync(Guid tenantId, Guid id, Guid caller, CancellationToken ct)
    {
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, id, ct);
        var project = invitation is null ? null : await _projects.GetByIdForTenantAsync(tenantId, invitation.ProjectId, ct);
        if (invitation is null || project is null)
            return null;
        var entry = (await _feed.MapAsync(tenantId, project, [], [invitation], caller, ct))[0];
        return new InboxItemDetail(
            WorkInboxItemMapper.FromFeed(entry, caller), WorkInboxItemMapper.InvitationSteps(entry.Item), entry.Item.Summary, [],
            entry.Item.Summary, ExplanationDetails: null,
            WorkInboxItemMapper.Target(project.Id, "module", invitation.ObjectiveId), EditablePayloadJson: null);
    }

    // ---- actions ---------------------------------------------------------------------------------

    private async Task<Result<InboxActionResult>> AfterApprovalAsync<T>(
        Guid tenantId, Guid id, Guid caller, Result<T> result, string journalAction, string? before, string? comment, CancellationToken ct)
    {
        if (!result.IsSuccess)
            return Result<InboxActionResult>.Failure(result.Error ?? "The action failed.", result.StatusCode ?? 400, result.ErrorCode);
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        await JournalAsync(tenantId, id, caller, journalAction, before, request?.Status, comment, ct);
        var affected = request is null ? [caller] : ApprovalFeedParticipants.NotifyTargets(request).Append(caller).Distinct().ToList();
        return Result<InboxActionResult>.Success(new InboxActionResult(affected));
    }

    /// <summary>The decision in the Inbox journal (History, Steps), after the WM command committed its change (plan W1).
    /// A revert marks the caller's latest decision reverted. WM's own revert window and rules are unchanged.</summary>
    private async Task JournalAsync(
        Guid tenantId, Guid id, Guid caller, string action, string? before, string? after, string? comment, CancellationToken ct)
    {
        if (_journal is null)
            return;
        var now = _clock.UtcNow;
        var entry = new ONEVO.Domain.Features.Inbox.Entities.InboxDecisionJournalEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Source = Source, Kind = WorkInboxKinds.Approval, ItemId = id, Action = action,
            ActorEmployeeId = caller, BeforeJson = JsonSerializer.Serialize(new { status = before }),
            AfterJson = JsonSerializer.Serialize(new { status = after }),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), DecidedAt = now, CreatedAt = now,
        };
        var latest = action == InboxJournalActions.Revert ? await _journal.LatestAsync(tenantId, new ItemRef(Source, WorkInboxKinds.Approval, id), ct) : null;
        // WM's own revert has already committed, so the revert row is kept even if a concurrent one marked the decision first.
        if (latest is null || latest.Action == InboxJournalActions.Revert || latest.RevertedByJournalId is not null
            || !await _journal.MarkRevertedAsync(latest, entry, ct))
            await _journal.RecordAsync(entry, ct);
    }

    /// <summary>Manual escalation of a monitor alert (plan W1): only someone the alert is for (its audience or its current
    /// escalation holder) may escalate it, and only while it is open.</summary>
    private async Task<Result<InboxActionResult>> EscalateAlertAsync(Guid tenantId, Guid id, Guid caller, string? comment, CancellationToken ct)
    {
        if (_escalation is null)
            return Result<InboxActionResult>.UnprocessableEntity(UnknownAction);
        var alert = await _alerts.GetByIdAsync(tenantId, id, ct);
        if (alert is null)
            return Result<InboxActionResult>.NotFound("Alert not found.");
        if (alert.ResolvedAt is not null)
            return Result<InboxActionResult>.Conflict("This alert is already resolved.");
        var audience = (await _audience.ResolveAsync(tenantId, alert.ProjectId, [alert], ct)).GetValueOrDefault(alert.Id) ?? [];
        var holders = await _escalation.CurrentHoldersAsync(tenantId, alert, ct);
        if (!audience.Contains(caller) && !holders.Contains(caller))
            return Result<InboxActionResult>.Forbidden("Only someone this alert is for can escalate it.");
        var affected = await _escalation.EscalateAsync(tenantId, alert, caller, comment, ct);
        return Result<InboxActionResult>.Success(new InboxActionResult(affected.Append(caller).Distinct().ToList()));
    }

    private async Task<Result<InboxActionResult>> AfterInvitationAsync(
        Guid tenantId, Guid id, Guid caller, Result result, CancellationToken ct)
    {
        if (!result.IsSuccess)
            return Result<InboxActionResult>.Failure(result.Error ?? "The action failed.", result.StatusCode ?? 400, result.ErrorCode);
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, id, ct);
        IReadOnlyCollection<Guid> affected = invitation is null
            ? [caller]
            : new[] { invitation.InvitedById, invitation.InvitedEmployeeId, caller }.Distinct().ToList();
        return Result<InboxActionResult>.Success(new InboxActionResult(affected));
    }

    /// <summary>An open alert can be escalated up the Module chain by the people it is for (plan W1).</summary>
    private InboxItem WithEscalate(InboxItem item, MonitorAlert alert)
        => _escalation is null || alert.ResolvedAt is not null
            ? item
            : item with
            {
                AvailableActions = item.AvailableActions
                    .Append(new InboxAction(WorkInboxActions.Escalate, "Escalate", "danger", NeedsComment: true, NeedsPayload: false)).ToList(),
            };

    private static string? BodyString(JsonElement? body, string property)
    {
        if (body is not { ValueKind: JsonValueKind.Object } element)
            return null;
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetRawText();
        }
        return null;
    }
}
