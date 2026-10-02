using ONEVO.Application.Common.ServiceInterfaces;
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
    public const string CommentedTemplate = "work_approval_commented";
    public const string MonitorAlertTemplate = "work_monitor_alert";
    public const string ApprovalRelatedEntityType = "work_approval_request";

    private readonly IWorkNotificationLogRepository _logs;
    private readonly IOutboxWriter _outbox;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;

    public WorkNotificationEngine(
        IWorkNotificationLogRepository logs, IOutboxWriter outbox,
        ICallerIdentityResolver identity, IMilestoneMembershipCoordinator membership)
    {
        _logs = logs;
        _outbox = outbox;
        _identity = identity;
        _membership = membership;
    }

    public async Task NotifyAsync(WorkNotificationEvent e, CancellationToken ct = default)
    {
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
            WorkNotificationKinds.Commented => CommentedTemplate,
            _ => DecidedTemplate,
        };
        var (relatedType, relatedId) = e.ApprovalRequestId is { } requestId
            ? (ApprovalRelatedEntityType, (Guid?)requestId)
            : (e.TargetType, e.TargetId);

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

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(e.TenantId, recipient.UserId, template, new Dictionary<string, string>(placeholders), relatedType, relatedId),
                e.TenantId,
                ct);
        }
    }

    private static string DecisionWord(string kind) => kind switch
    {
        WorkNotificationKinds.Approved => "approved",
        WorkNotificationKinds.Rejected => "rejected",
        WorkNotificationKinds.Cancelled => "cancelled",
        WorkNotificationKinds.Stale => "closed as outdated",
        _ => string.Empty,
    };
}
