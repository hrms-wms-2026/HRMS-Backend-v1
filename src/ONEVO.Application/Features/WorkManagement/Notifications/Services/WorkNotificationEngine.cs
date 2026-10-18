using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.OutboxHandlers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

public sealed class WorkNotificationEngine : IWorkNotificationEngine
{
    public const string ActivityTemplate = "work_activity_recorded";
    public const string RequestedTemplate = "work_approval_requested";
    public const string DecidedTemplate = "work_approval_decided";
    public const string MonitorAlertTemplate = "work_monitor_alert";
    public const string ApprovalRelatedEntityType = "work_approval_request";
    public const string InboxSource = "work";
    public const string ApprovalInboxKind = "approval";
    public const string MonitorAlertInboxKind = "monitor_alert";
    public const string InvitationInboxKind = "invitation";

    private readonly IWorkNotificationLogRepository _logs;
    private readonly IOutboxWriter _outbox;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationOrchestrator _orchestrator;

    public WorkNotificationEngine(
        IWorkNotificationLogRepository logs, IOutboxWriter outbox,
        ICallerIdentityResolver identity, IMilestoneMembershipCoordinator membership,
        INotificationOrchestrator orchestrator)
    {
        _logs = logs;
        _outbox = outbox;
        _identity = identity;
        _membership = membership;
        _orchestrator = orchestrator;
    }

    public async Task NotifyAsync(WorkNotificationEvent e, CancellationToken ct = default)
    {
        // The Inbox shows approval requests and monitor alerts; everyone involved (actor included, for
        // their "Sent" list) gets a refresh signal once this transaction commits.
        if (e.ApprovalRequestId is { } approvalId)
            await InboxChangedAsync(e.TenantId, [e.ActorEmployeeId, .. e.RecipientEmployeeIds], ApprovalInboxKind, approvalId, ct);
        else if (e.Kind == WorkNotificationKinds.Alert && e.MonitorAlertId is { } alertId)
            await InboxChangedAsync(e.TenantId, e.RecipientEmployeeIds, MonitorAlertInboxKind, alertId, ct);

        var recipients = e.RecipientEmployeeIds
            .Where(id => id != Guid.Empty && id != e.ActorEmployeeId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
            return;

        // Monitor alerts come from the system (Guid.Empty), not from a person.
        IReadOnlyDictionary<Guid, string> names = e.ActorEmployeeId == Guid.Empty
            ? new Dictionary<Guid, string>()
            : await _identity.ResolveDisplayNamesByEmployeeIdAsync(e.TenantId, [e.ActorEmployeeId], ct);
        var placeholders = new Dictionary<string, string>
        {
            ["actorName"] = names.GetValueOrDefault(e.ActorEmployeeId) ?? "A teammate",
            ["actionLabel"] = WorkActionLabels.For(e.ActionType),
            ["targetTitle"] = e.TargetTitle,
            ["decision"] = DecisionWord(e.Kind),
        };
        var template = e.Kind switch
        {
            WorkNotificationKinds.Direct => ActivityTemplate,
            WorkNotificationKinds.Alert => MonitorAlertTemplate,
            WorkNotificationKinds.Requested => RequestedTemplate,
            _ => DecidedTemplate,
        };
        // A click opens the item in the Inbox: the request, or the alert itself (it links on to its target).
        var (relatedType, relatedId) = e.ApprovalRequestId is { } requestId
            ? (ApprovalRelatedEntityType, (Guid?)requestId)
            : e.Kind == WorkNotificationKinds.Alert && e.MonitorAlertId is { } monitorAlertId
                ? (MonitorAlertInboxKind, (Guid?)monitorAlertId)
                : (e.TargetType, e.TargetId);

        // Events cut over to the orchestrator are routed per tenant settings (in-app / email / tray) instead
        // of the legacy in-app-only payload - never both. One event id for the occurrence, shared by all recipients.
        var orchestratorOwned = NotificationEventCatalog.TryGet(template, out var definition) && definition.OrchestratorOwned;
        var eventId = Guid.NewGuid();

        foreach (var recipientId in recipients)
        {
            var recipient = await _membership.GetActiveAssigneeAsync(e.TenantId, recipientId, ct);
            if (recipient is null)
                continue;

            await _logs.AddAsync(new WorkNotificationLog
            {
                Id = Guid.NewGuid(), TenantId = e.TenantId, ProjectId = e.ProjectId,
                RecipientEmployeeId = recipientId, ActorEmployeeId = e.ActorEmployeeId, Kind = e.Kind,
                ActionType = e.ActionType, TargetType = e.TargetType, TargetId = e.TargetId,
                TargetTitle = e.TargetTitle, ApprovalRequestId = e.ApprovalRequestId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);

            if (orchestratorOwned)
            {
                await _orchestrator.EnqueueAsync(new NotificationDispatchRequest(
                    eventId, template, e.TenantId, recipient.UserId, new Dictionary<string, string>(placeholders),
                    relatedType, relatedId), ct);
                continue;
            }

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(e.TenantId, recipient.UserId, template, new Dictionary<string, string>(placeholders), relatedType, relatedId),
                e.TenantId,
                ct);
        }
    }

    public Task InboxChangedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, string kind, Guid id, CancellationToken ct = default)
        => EnqueueInboxChangedAsync(_outbox, tenantId, employeeIds, kind, id, ct);

    /// <summary>The inbox_changed outbox message, for Work code that writes to the outbox directly (module
    /// invitations) rather than through NotifyAsync. Pushed after the surrounding transaction commits.</summary>
    public static async Task EnqueueInboxChangedAsync(
        IOutboxWriter outbox, Guid tenantId, IEnumerable<Guid> employeeIds, string kind, Guid id, CancellationToken ct = default)
    {
        var people = employeeIds.Where(e => e != Guid.Empty).Distinct().ToList();
        if (people.Count == 0)
            return;
        await outbox.EnqueueAsync(OutboxMessageTypes.InboxChanged, new InboxChangedPayload(tenantId, people, InboxSource, kind, id), tenantId, ct);
    }

    private static string DecisionWord(string kind) => kind switch
    {
        WorkNotificationKinds.Approved => "approved",
        WorkNotificationKinds.Rejected => "rejected",
        WorkNotificationKinds.Cancelled => "cancelled",
        WorkNotificationKinds.Stale => "closed as outdated",
        WorkNotificationKinds.Reverted => "reopened for another decision",
        _ => string.Empty,
    };
}
