using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

public class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, Result<PagedResult<ProjectListItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;

    public ListProjectsQueryHandler(ICurrentUser currentUser, IProjectRepository projects)
    {
        _currentUser = currentUser;
        _projects = projects;
    }

    public async Task<Result<PagedResult<ProjectListItemResponse>>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PagedResult<ProjectListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PagedResult<ProjectListItemResponse>>.Forbidden("Tenant context missing.");

        var targetUserId = request.TargetUserId ?? _currentUser.UserId;
        var pageNumber = request.Paging.PageNumber < 1 ? 1 : request.Paging.PageNumber;
        var skip = (pageNumber - 1) * request.Paging.PageSize;

        var (items, total) = await _projects.ListForMemberAsync(
            tenantId, targetUserId, skip, request.Paging.PageSize, request.Paging.SortBy, request.Paging.SortDirection, ct);

        var dtoItems = items.Select(p => ProjectMapper.ToListItem(p, p.LeadId == targetUserId)).ToList();

        return Result<PagedResult<ProjectListItemResponse>>.Success(
            new PagedResult<ProjectListItemResponse>(dtoItems, pageNumber, request.Paging.PageSize, total));
    }
}
