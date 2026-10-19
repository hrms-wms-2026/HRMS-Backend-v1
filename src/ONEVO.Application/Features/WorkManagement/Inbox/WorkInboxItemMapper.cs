using System.Globalization;
using System.Text.Json;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>Context an alert row needs beyond the alert itself.</summary>
public sealed record WorkAlertContext(string ProjectName, Guid? ModuleId, bool InAchievedModule);

/// <summary>Maps Work Management approval rows, invitations and monitor alerts onto the generic Inbox shapes.
/// Permissions come from the existing feed/detail logic (CanDecide, CanCancel, the revert window); this
/// class only translates them into actions.</summary>
public static class WorkInboxItemMapper
{
    public const string ModuleFacet = "module";
    private const string ProjectContext = "project";

    public static InboxItem FromFeed(ApprovalFeedEntry entry, Guid caller, bool? canRevertOverride = null)
    {
        var i = entry.Item;
        var isApproval = i.Source == ApprovalFeedSources.Engine;
        var actions = new List<InboxAction>();
        if (isApproval)
        {
            if (i.CanDecide)
            {
                actions.Add(new(WorkInboxActions.Approve, "Approve", "primary", NeedsComment: false, NeedsPayload: false));
                actions.Add(new(WorkInboxActions.Reject, "Reject", "danger", NeedsComment: true, NeedsPayload: false));
            }
            if (i.CanCancel)
                actions.Add(new(WorkInboxActions.Cancel, "Cancel request", "neutral", false, false));
            var window = entry.Request is { } r ? ApprovalRevertWindow.ComputeCanRevert(r, caller) : (false, null);
            var canRevert = canRevertOverride ?? window.CanRevert;
            // The standard undo: its deadline lets the Inbox show the "Undo - available until" banner (plan FW).
            if (canRevert)
                actions.Add(new(WorkInboxActions.Revert, "Undo", "neutral", false, false,
                    window.RevertableUntil is { } until
                        ? new Dictionary<string, string> { [InboxStandardActions.MetaRevertableUntil] = until.ToString("O") }
                        : null));
        }
        else
        {
            if (i.CanDecide)
            {
                actions.Add(new(WorkInboxActions.AcceptInvitation, "Accept", "primary", false, false));
                actions.Add(new(WorkInboxActions.DeclineInvitation, "Decline", "danger", false, false));
            }
            if (i.CanCancel)
                actions.Add(new(WorkInboxActions.Cancel, "Cancel invitation", "neutral", false, false));
        }

        var updatedAt = new[] { i.CreatedAt, i.DecidedAt ?? i.CreatedAt, entry.Request?.RevertedAt ?? i.CreatedAt }.Max();
        return new InboxItem(
            WorkInboxKinds.Source, isApproval ? WorkInboxKinds.Approval : WorkInboxKinds.Invitation, i.Id,
            InboxCategories.Request, i.ActionType, i.TargetTitle, i.Summary, i.Status, i.Direction, Severity: null,
            ProjectContext, entry.ProjectId, i.ProjectName,
            new InboxPerson(i.RequestedById, i.RequestedByName), new InboxPerson(i.ApproverId, i.ApproverName),
            i.CreatedAt, updatedAt, actions, UnreadMessages: 0, IsPending: i.Status == WorkApprovalRequestStatuses.Pending,
            Facets(i.ModuleId));
    }

    public static InboxItem FromAlert(MonitorAlert alert, Guid projectId, WorkAlertContext context)
    {
        var open = alert.ResolvedAt is null;
        return new InboxItem(
            WorkInboxKinds.Source, WorkInboxKinds.MonitorAlert, alert.Id, InboxCategories.Alert,
            WorkActionLabels.MonitorPrefix + alert.RuleCode, alert.TargetTitle, alert.Message,
            open ? WorkInboxAlertStatuses.Open : WorkInboxAlertStatuses.Resolved, InboxDirections.Info, Severity(alert.RuleCode),
            ProjectContext, projectId, context.ProjectName, RequestedBy: null, RequestedTo: null,
            alert.FirstDetectedAt, alert.ResolvedAt ?? alert.NotifiedAt ?? alert.FirstDetectedAt,
            [new InboxAction(WorkInboxActions.OpenTarget, "Open", "neutral", false, false)], UnreadMessages: 0,
            // Achieved Modules keep their alerts for display only: never pending, never counted.
            IsPending: open && !context.InAchievedModule,
            Facets(context.ModuleId));
    }

    /// <summary>Requested → Approved | Rejected | Cancelled | Closed as outdated (→ Reverted).</summary>
    public static IReadOnlyList<InboxStep> ApprovalSteps(ApprovalFeedItemResponse item, WorkApprovalRequest request, string? revertedByName)
    {
        var steps = new List<InboxStep>
        {
            new("requested", "Requested", new InboxPerson(item.RequestedById, item.RequestedByName), item.CreatedAt, InboxStepStates.Done, null)
        };
        if (request.RevertedAt is { } revertedAt)
            steps.Add(new("reverted", "Decision reverted",
                request.RevertedByEmployeeId is { } by ? new InboxPerson(by, revertedByName ?? "A teammate") : null,
                revertedAt, InboxStepStates.Done, null));

        steps.Add(item.Status switch
        {
            WorkApprovalRequestStatuses.Pending => new InboxStep("decision", $"Waiting for {item.ApproverName}",
                new InboxPerson(item.ApproverId, item.ApproverName), null, InboxStepStates.Current, null),
            WorkApprovalRequestStatuses.Cancelled => new InboxStep("cancelled", "Cancelled",
                new InboxPerson(item.RequestedById, item.RequestedByName), item.DecidedAt, InboxStepStates.Done, null),
            WorkApprovalRequestStatuses.Stale => new InboxStep("stale", "Closed as outdated", null, item.DecidedAt, InboxStepStates.Done,
                "The target changed after the request was made."),
            _ => new InboxStep(item.Status, Capitalize(item.Status),
                item.DecidedById is { } d ? new InboxPerson(d, item.DecidedByName ?? "A teammate") : null,
                item.DecidedAt, InboxStepStates.Done, item.DecisionComment),
        });
        return steps;
    }

    /// <summary>Invited → Accepted | Declined | Cancelled | Expired.</summary>
    public static IReadOnlyList<InboxStep> InvitationSteps(ApprovalFeedItemResponse item)
    {
        var invitee = new InboxPerson(item.ApproverId, item.ApproverName);
        return
        [
            new("invited", "Invited", new InboxPerson(item.RequestedById, item.RequestedByName), item.CreatedAt, InboxStepStates.Done, item.Summary),
            item.Status switch
            {
                WorkApprovalRequestStatuses.Pending => new InboxStep("response", $"Waiting for {item.ApproverName}", invitee, null, InboxStepStates.Current, null),
                WorkApprovalRequestStatuses.Approved => new InboxStep("accepted", "Accepted", invitee, item.DecidedAt, InboxStepStates.Done, null),
                WorkApprovalRequestStatuses.Rejected => new InboxStep("declined", "Declined", invitee, item.DecidedAt, InboxStepStates.Done, null),
                WorkApprovalRequestStatuses.Cancelled => new InboxStep("cancelled", "Cancelled",
                    new InboxPerson(item.RequestedById, item.RequestedByName), item.DecidedAt, InboxStepStates.Done, null),
                _ => new InboxStep("expired", "Expired", null, item.DecidedAt, InboxStepStates.Done, null),
            }
        ];
    }

    /// <summary>Detected → Notified → Resolved.</summary>
    public static IReadOnlyList<InboxStep> AlertSteps(MonitorAlert alert, bool inAchievedModule)
    {
        var notified = alert.NotifiedAt is not null;
        var resolved = alert.ResolvedAt is not null;
        return
        [
            new("detected", "Detected", null, alert.FirstDetectedAt, InboxStepStates.Done, null),
            new("notified", "Notified", null, alert.NotifiedAt,
                notified ? InboxStepStates.Done : resolved ? InboxStepStates.Upcoming : InboxStepStates.Current,
                inAchievedModule && !notified ? "Not notified: the Module is achieved." : null),
            new("resolved", "Resolved", null, alert.ResolvedAt,
                resolved ? InboxStepStates.Done : notified ? InboxStepStates.Current : InboxStepStates.Upcoming, null),
        ];
    }

    public static IReadOnlyList<InboxChange> ChangeSet(IReadOnlyList<ApprovalFieldResponse> fields)
        => fields.Where(f => f.Changed || f.Applied is not null)
            .Select(f => new InboxChange(f.Key, f.Label, f.Current, f.Requested, f.Kind, f.Editable, f.Applied))
            .ToList();

    public static InboxTarget Target(Guid projectId, string targetType, Guid? targetId, string? actionType = null)
    {
        var project = projectId.ToString();
        return (targetType, targetId) switch
        {
            (WorkTargetTypes.Task, { } taskId) when actionType != WorkActionTypes.TaskDelete
                => new InboxTarget(["/work", project, "board"], "Open task", new Dictionary<string, string> { ["taskId"] = taskId.ToString() }),
            (WorkTargetTypes.Module, { } moduleId) when actionType != WorkActionTypes.ModuleDelete
                => new InboxTarget(["/work", project, "milestones", moduleId.ToString(), "tree"], "Open module"),
            (WorkTargetTypes.Sprint, _) => new InboxTarget(["/work", project, "backlog"], "Open backlog"),
            _ => new InboxTarget(["/work", project], "Open project"),
        };
    }

    public static IReadOnlyDictionary<string, string>? AlertDetails(string detailsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            var details = doc.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
                .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText());
            return details.Count == 0 ? null : details;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"task.edit" → "Task edit"; monitor rule codes use their alert labels.</summary>
    public static string TypeLabel(string actionType)
        => actionType.StartsWith(WorkActionLabels.MonitorPrefix, StringComparison.Ordinal)
            ? WorkActionLabels.For(actionType)
            : Capitalize(actionType.Replace('.', ' ').Replace('_', ' '));

    public static string Capitalize(string text)
        => string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    private static string Severity(string ruleCode)
        => ruleCode.Contains("overdue", StringComparison.Ordinal) || ruleCode.Contains("over_capacity", StringComparison.Ordinal)
           || ruleCode.Contains("overload", StringComparison.Ordinal)
            ? "high"
            : "medium";

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Facets(Guid? moduleId)
        => moduleId is { } id
            ? new Dictionary<string, IReadOnlyList<string>> { [ModuleFacet] = [id.ToString()] }
            : new Dictionary<string, IReadOnlyList<string>>();
}
