using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>A feed row plus what the inbox needs that the row does not carry. Request is null for invitations.</summary>
public sealed record ApprovalFeedEntry(Guid ProjectId, ApprovalFeedItemResponse Item, WorkApprovalRequest? Request);

/// <summary>Every request the caller sent or received in one project (engine requests they can see under
/// ApprovalFeedParticipants + module invitations they sent or received). Shared by the project feed and
/// the user-scoped Inbox so both apply the same visibility rules.</summary>
public sealed class ApprovalFeedBuilder
{
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IWorkTaskRepository _tasks;
    private readonly IWorkApprovalCommentRepository _comments;

    public ApprovalFeedBuilder(
        ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy,
        IProjectMemberInvitationRepository invitations, IWorkTaskRepository tasks, IWorkApprovalCommentRepository comments)
    {
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
        _tasks = tasks;
        _comments = comments;
    }

    /// <summary>Unsorted; use ApprovalFeedItemFactory.Sort for the Approvals-page order.</summary>
    public async Task<IReadOnlyList<ApprovalFeedEntry>> BuildAsync(Guid tenantId, Project project, Guid caller, CancellationToken ct)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, project.Id, ct);
        var all = await _requests.ListByProjectAsync(tenantId, project.Id, null, null, ct);
        var requests = all.Where(r => ApprovalFeedParticipants.CanSee(tree, r, caller)).ToList();
        var invitations = await _invitations.ListForProjectAndEmployeeAsync(tenantId, project.Id, caller, ct);
        if (requests.Count == 0 && invitations.Count == 0)
            return [];

        var taskIds = ApprovalFeedItemFactory.TaskIdsNeedingModule(requests);
        var taskModules = taskIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _tasks.GetObjectiveIdsByTaskIdsAsync(tenantId, taskIds, ct);

        var employeeIds = requests.SelectMany(ApprovalFeedItemFactory.EmployeeIds)
            .Concat(invitations.SelectMany(ApprovalFeedItemFactory.EmployeeIds))
            .Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);

        var approvalCounts = await _comments.CountBySubjectsAsync(
            tenantId, WorkApprovalCommentSubjects.Approval, requests.Select(r => r.Id).ToList(), ct);
        var invitationCounts = await _comments.CountBySubjectsAsync(
            tenantId, WorkApprovalCommentSubjects.Invitation, invitations.Select(i => i.Id).ToList(), ct);
        var commentCounts = approvalCounts.Concat(invitationCounts).ToDictionary(kv => kv.Key, kv => kv.Value);

        var lookup = new ApprovalFeedLookup(tree, project.Name, names, taskModules, commentCounts);
        return requests.Select(r => new ApprovalFeedEntry(project.Id, ApprovalFeedItemFactory.FromRequest(r, caller, lookup), r))
            .Concat(invitations.Select(i => new ApprovalFeedEntry(project.Id, ApprovalFeedItemFactory.FromInvitation(i, caller, lookup), null)))
            .ToList();
    }
}
