using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Inbox;

/// <summary>
/// Base participants of a Work item's conversation, resolved now (never stored), so a transferred
/// approver position moves the chat to its new holder:
/// approval → requester + everyone who can decide it today (WorkApprovalDecisionRules: the owners of the
/// position Module and of every Module above it; the stored approver for HR-routed requests) + whoever
/// decided it;
/// invitation → inviter + invitee; alert → its recipients (MonitorAlertAudience).
/// </summary>
public sealed class WorkConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IMonitorAlertRepository _alerts;
    private readonly MonitorAlertAudience _audience;
    private readonly WorkInboxSourceProvider? _inbox;

    public WorkConversationSubjectResolver(
        IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy, IProjectMemberInvitationRepository invitations,
        IMonitorAlertRepository alerts, MonitorAlertAudience audience, WorkInboxSourceProvider? inbox = null)
    {
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
        _alerts = alerts;
        _audience = audience;
        _inbox = inbox;
    }

    public string Source => WorkInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
        => subject.Kind switch
        {
            WorkInboxKinds.Approval => await ApprovalAsync(tenantId, subject.Id, ct),
            WorkInboxKinds.Invitation => await InvitationAsync(tenantId, subject.Id, ct),
            WorkInboxKinds.MonitorAlert => await AlertAsync(tenantId, subject.Id, ct),
            _ => null,
        };

    /// <summary>Anyone who can open the item in their own Work inbox (e.g. a Module owner above the approver chain
    /// who sees it in the project feed) may read and post in its chat as a viewer.</summary>
    public async Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => _inbox is not null && await _inbox.GetScopedDetailAsync(tenantId, subject.Kind, subject.Id, employeeId, ct) is not null;

    private async Task<ConversationSubject?> ApprovalAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        if (request is null)
            return null;

        IEnumerable<Guid> deciders = [request.ApproverEmployeeId];
        if (request.ApproverSource == WorkApprovalSources.Hierarchy && request.PositionObjectiveId is { } position)
        {
            // Re-run against today's tree, so a transferred position moves the chat with it.
            var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
            deciders = tree.AncestorChain(position).Select(m => m.OwnerId);
        }

        var people = new[] { request.RequestedByEmployeeId }
            .Concat(deciders)
            .Append(request.DecidedByEmployeeId ?? Guid.Empty)
            .Where(p => p != Guid.Empty).Distinct().ToList();
        // The requested changes sit in the chat where the request was made, so people can reply to them.
        return new ConversationSubject(people, $"{WorkInboxItemMapper.TypeLabel(request.ActionType)}: {request.TargetTitle}", request.ProjectId,
            EvidenceAt: request.CreatedAt);
    }

    private async Task<ConversationSubject?> InvitationAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var invitation = await _invitations.GetByIdForTenantAsync(tenantId, id, ct);
        if (invitation is null)
            return null;
        var tree = await _hierarchy.LoadTreeAsync(tenantId, invitation.ProjectId, ct);
        var module = tree.Get(invitation.ObjectiveId)?.Title ?? "Module";
        return new ConversationSubject(
            new[] { invitation.InvitedById, invitation.InvitedEmployeeId }.Distinct().ToList(),
            $"Invitation: {module}", invitation.ProjectId);
    }

    private async Task<ConversationSubject?> AlertAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(tenantId, id, ct);
        if (alert is null)
            return null;
        var audience = await _audience.ResolveAsync(tenantId, alert.ProjectId, [alert], ct);
        return new ConversationSubject(
            audience.GetValueOrDefault(alert.Id) ?? [],
            $"{WorkActionLabels.For(WorkActionLabels.MonitorPrefix + alert.RuleCode)}: {alert.TargetTitle}", alert.ProjectId,
            // The employee a deadline-overload alert is about (P10).
            SubjectEmployeeId: alert.SubjectEmployeeId);
    }
}
