using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetProjectApprovalFeed;

public sealed class GetProjectApprovalFeedQueryHandler
    : IRequestHandler<GetProjectApprovalFeedQuery, Result<IReadOnlyList<ApprovalFeedItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;

    public GetProjectApprovalFeedQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IWorkHierarchyService hierarchy, IProjectMemberInvitationRepository invitations,
        IWorkTaskRepository tasks, IProjectRepository projects)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
        _invitations = invitations;
        _tasks = tasks;
        _projects = projects;
    }

    public async Task<Result<IReadOnlyList<ApprovalFeedItemResponse>>> Handle(GetProjectApprovalFeedQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var project = await _projects.GetByIdForTenantAsync(tenantId, query.ProjectId, ct);
        if (project is null)
            return Result<IReadOnlyList<ApprovalFeedItemResponse>>.NotFound("Project not found.");

        var tree = await _hierarchy.LoadTreeAsync(tenantId, query.ProjectId, ct);
        var all = await _requests.ListByProjectAsync(tenantId, query.ProjectId, null, null, ct);
        var requests = all.Where(r => ApprovalFeedParticipants.CanSee(tree, r, caller)).ToList();
        var invitations = await _invitations.ListForProjectAndEmployeeAsync(tenantId, query.ProjectId, caller, ct);

        var taskIds = ApprovalFeedItemFactory.TaskIdsNeedingModule(requests);
        var taskModules = taskIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await _tasks.GetObjectiveIdsByTaskIdsAsync(tenantId, taskIds, ct);

        var employeeIds = requests.SelectMany(ApprovalFeedItemFactory.EmployeeIds)
            .Concat(invitations.SelectMany(ApprovalFeedItemFactory.EmployeeIds))
            .Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);

        // Comment counts are wired in with the comments feature.
        var lookup = new ApprovalFeedLookup(tree, project.Name, names, taskModules, new Dictionary<Guid, int>());

        var items = requests.Select(r => ApprovalFeedItemFactory.FromRequest(r, caller, lookup))
            .Concat(invitations.Select(i => ApprovalFeedItemFactory.FromInvitation(i, caller, lookup)));
        return Result<IReadOnlyList<ApprovalFeedItemResponse>>.Success(ApprovalFeedItemFactory.Sort(items));
    }
}
