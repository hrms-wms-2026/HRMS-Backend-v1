using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjectCategories;

public class ListProjectCategoriesQueryHandler : IRequestHandler<ListProjectCategoriesQuery, Result<List<ProjectCategoryListItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectCategoryRepository _categories;

    public ListProjectCategoriesQueryHandler(ICurrentUser currentUser, IProjectCategoryRepository categories)
    {
        _currentUser = currentUser;
        _categories = categories;
    }

    public async Task<Result<List<ProjectCategoryListItemResponse>>> Handle(ListProjectCategoriesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<List<ProjectCategoryListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<List<ProjectCategoryListItemResponse>>.Forbidden("Tenant context missing.");

        var categories = await _categories.GetAllForTenantAsync(tenantId, request.IncludeInactive, ct);

        return Result<List<ProjectCategoryListItemResponse>>.Success(categories.Select(ProjectMapper.ToListItem).ToList());
    }
}
