using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatuses;

public class GetProjectTaskStatusesQueryHandler : IRequestHandler<GetProjectTaskStatusesQuery, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;

    public GetProjectTaskStatusesQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects, ITaskStatusRepository statuses)
    {
        _currentUser = currentUser;
        _projects = projects;
        _statuses = statuses;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(GetProjectTaskStatusesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project not found.");

        var template = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        return Result<IReadOnlyList<TaskStatusResponse>>.Success(ToResponses(template));
    }

    private static IReadOnlyList<TaskStatusResponse> ToResponses(IReadOnlyList<TaskStatusEntity> statuses)
        => statuses.OrderBy(s => s.DisplayOrder)
            .Select(s => new TaskStatusResponse(
                s.Id, s.Name, s.DisplayOrder, s.RequiresApproval,
                s.ApproverId, s.MarksTaskComplete, s.Visibility))
            .ToList();
}
