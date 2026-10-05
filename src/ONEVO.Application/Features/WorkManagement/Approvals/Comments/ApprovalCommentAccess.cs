using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Comments;

/// <summary>Participants = who gets notified of a new comment (caller excluded by the engine).</summary>
public sealed record ApprovalCommentSubject(
    string SubjectType, Guid SubjectId, Guid ProjectId, string ActionType, string TargetType,
    Guid? TargetId, string TargetTitle, Guid? ApprovalRequestId, IReadOnlyCollection<Guid> Participants);

/// <summary>Resolves the subject and says whether the caller may read/write its thread.</summary>
public interface IApprovalCommentAccess
{
    /// <summary>Null when the subject doesn't exist or the caller may not see it.</summary>
    Task<ApprovalCommentSubject?> ResolveAsync(Guid tenantId, string subjectType, Guid subjectId, Guid caller, CancellationToken ct);
}

/// <summary>An approval thread is open to the requester and every "received" party (D1/D6); an
/// invitation thread to the inviter and the invitee.</summary>
public sealed class ApprovalCommentAccess : IApprovalCommentAccess
{
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;

    public ApprovalCommentAccess(
        IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy, IProjectMemberInvitationRepository invitations)
    {
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
    }

    public async Task<ApprovalCommentSubject?> ResolveAsync(Guid tenantId, string subjectType, Guid subjectId, Guid caller, CancellationToken ct)
    {
        switch (subjectType)
        {
            case WorkApprovalCommentSubjects.Approval:
                var request = await _requests.GetByIdForTenantAsync(tenantId, subjectId, ct);
                if (request is null) return null;
                var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
                if (!ApprovalFeedParticipants.CanSee(tree, request, caller)) return null;
                return new ApprovalCommentSubject(
                    subjectType, request.Id, request.ProjectId, request.ActionType, request.TargetType,
                    request.TargetId, request.TargetTitle, request.Id, ApprovalFeedParticipants.NotifyTargets(request));

            case WorkApprovalCommentSubjects.Invitation:
                var invitation = await _invitations.GetByIdForTenantAsync(tenantId, subjectId, ct);
                if (invitation is null || (invitation.InvitedEmployeeId != caller && invitation.InvitedById != caller))
                    return null;
                var moduleTree = await _hierarchy.LoadTreeAsync(tenantId, invitation.ProjectId, ct);
                return new ApprovalCommentSubject(
                    subjectType, invitation.Id, invitation.ProjectId, ApprovalFeedItemFactory.InvitationActionType,
                    WorkTargetTypes.Module, invitation.ObjectiveId, moduleTree.Get(invitation.ObjectiveId)?.Title ?? "Module",
                    ApprovalRequestId: null, new[] { invitation.InvitedById, invitation.InvitedEmployeeId });

            default:
                return null;
        }
    }
}
