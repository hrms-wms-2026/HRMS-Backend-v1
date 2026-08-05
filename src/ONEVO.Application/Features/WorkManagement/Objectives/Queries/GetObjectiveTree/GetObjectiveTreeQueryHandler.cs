using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;

public class GetObjectiveTreeQueryHandler : IRequestHandler<GetObjectiveTreeQuery, Result<IReadOnlyList<ObjectiveTreeItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveTreeQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects,
        IProjectMemberRepository members, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _projects = projects;
        _members = members;
        _objectives = objectives;
    }

    public async Task<Result<IReadOnlyList<ObjectiveTreeItemResponse>>> Handle(GetObjectiveTreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.NotFound("Project not found.");

        var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
        if (!isMember)
            return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Forbidden("You do not have access to this project's milestone tree.");

        var tree = await _objectives.GetTreeByProjectIdAsync(tenantId, project.Id, ct);
        var items = tree.Select(ObjectiveMapper.ToTreeItem).ToList();

        return Result<IReadOnlyList<ObjectiveTreeItemResponse>>.Success(items);
    }
}
