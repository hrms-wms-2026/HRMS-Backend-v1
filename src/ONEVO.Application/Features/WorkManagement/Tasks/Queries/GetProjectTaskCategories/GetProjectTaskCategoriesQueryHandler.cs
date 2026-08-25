using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskCategories;

public class GetProjectTaskCategoriesQueryHandler : IRequestHandler<GetProjectTaskCategoriesQuery, Result<IReadOnlyList<TaskCategoryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly ITaskCategoryRepository _categories;

    public GetProjectTaskCategoriesQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects, ITaskCategoryRepository categories)
    {
        _currentUser = currentUser;
        _projects = projects;
        _categories = categories;
    }

    public async Task<Result<IReadOnlyList<TaskCategoryResponse>>> Handle(GetProjectTaskCategoriesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskCategoryResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskCategoryResponse>>.NotFound("Project not found.");

        var categories = await _categories.GetByProjectIdAsync(tenantId, project.Id, ct);
        return Result<IReadOnlyList<TaskCategoryResponse>>.Success(
            categories.OrderBy(c => c.DisplayOrder)
                .Select(c => new TaskCategoryResponse(c.Id, c.Name, c.DisplayOrder))
                .ToList());
    }
}
