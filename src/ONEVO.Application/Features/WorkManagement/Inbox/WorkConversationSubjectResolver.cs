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
/// approval → requester + today's eligible approver (+ whoever decided it);
/// invitation → inviter + invitee; alert → its recipients (MonitorAlertAudience).
/// </summary>
public sealed class WorkConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IMonitorAlertRepository _alerts;
    private readonly MonitorAlertAudience _audience;

    public WorkConversationSubjectResolver(
        IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy, IProjectMemberInvitationRepository invitations,
        IMonitorAlertRepository alerts, MonitorAlertAudience audience)
    {
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
        _alerts = alerts;
        _audience = audience;
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

    private async Task<ConversationSubject?> ApprovalAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var request = await _requests.GetByIdForTenantAsync(tenantId, id, ct);
        if (request is null)
            return null;

        Guid? approver = request.ApproverEmployeeId;
        if (request.ApproverSource == WorkApprovalSources.Hierarchy && request.PositionObjectiveId is { } position)
        {
            // Same lookup the approval engine uses to pick the approver, re-run against today's tree.
            var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
            approver = await _hierarchy.FindActiveHolderAsync(tenantId, tree, position, request.RequestedByEmployeeId, ct)
                ?? request.ApproverEmployeeId;
        }

        var people = new[] { request.RequestedByEmployeeId, approver ?? Guid.Empty, request.DecidedByEmployeeId ?? Guid.Empty }
            .Where(p => p != Guid.Empty).Distinct().ToList();
        return new ConversationSubject(people, $"{WorkInboxItemMapper.TypeLabel(request.ActionType)}: {request.TargetTitle}", request.ProjectId);
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
            $"{WorkActionLabels.For(WorkActionLabels.MonitorPrefix + alert.RuleCode)}: {alert.TargetTitle}", alert.ProjectId);
    }
}
