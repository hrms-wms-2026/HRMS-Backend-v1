using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatusChangeRequests;

public class GetProjectTaskStatusChangeRequestsQueryHandler
    : IRequestHandler<GetProjectTaskStatusChangeRequestsQuery, Result<ProjectTaskStatusChangeRequestsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusChangeRequestRepository _requests;
    private readonly ITaskStatusChangeAccessService _access;

    public GetProjectTaskStatusChangeRequestsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        ITaskStatusChangeRequestRepository requests, ITaskStatusChangeAccessService access)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _requests = requests;
        _access = access;
    }

    public async Task<Result<ProjectTaskStatusChangeRequestsResponse>> Handle(
        GetProjectTaskStatusChangeRequestsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectTaskStatusChangeRequestsResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ProjectTaskStatusChangeRequestsResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<ProjectTaskStatusChangeRequestsResponse>.NotFound("Project not found.");

        var access = await _access.ResolveAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (access is null)
            return Result<ProjectTaskStatusChangeRequestsResponse>.Success(new(false, false, []));

        var pending = await _requests.ListPendingForProjectAsync(tenantId, project.Id, ct);
        var visible = access.CanEditDirectly
            ? pending
            : pending.Where(r => r.RequestedByEmployeeId == callerEmployeeId.Value).ToList();

        var names = visible.Count == 0
            ? new Dictionary<Guid, string>()
            : await _identity.ResolveDisplayNamesByEmployeeIdAsync(
                tenantId, visible.Select(r => r.RequestedByEmployeeId).Distinct().ToList(), ct);

        return Result<ProjectTaskStatusChangeRequestsResponse>.Success(new(
            access.CanEditDirectly,
            access.CanRequest,
            visible.Select(r => TaskStatusChangeRequestResponse.From(
                    r,
                    names.GetValueOrDefault(r.RequestedByEmployeeId) ?? "A teammate",
                    canDecide: access.CanEditDirectly,
                    canCancel: r.RequestedByEmployeeId == callerEmployeeId.Value))
                .ToList()));
    }
}
