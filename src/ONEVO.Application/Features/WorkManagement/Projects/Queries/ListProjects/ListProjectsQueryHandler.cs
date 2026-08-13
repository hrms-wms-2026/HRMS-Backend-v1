using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Helpers;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

public class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, Result<PagedResult<ProjectListItemResponse>>>
{
    private const int MaxLabelsPerProject = 5;

    // Generous on purpose: the frontend fetches this once per list page and slices client-side for
    // the collapsed avatar stack (3-4) vs. the expanded full member list, so this needs to cover a
    // realistic project team size without a second round-trip when a card expands.
    private const int MaxMembersPerProject = 20;

    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ILabelRepository _labels;
    private readonly IProjectMemberRepository _members;
    private readonly IEmployeeRepository _employees;

    public ListProjectsQueryHandler(
        ICurrentUser currentUser, IProjectRepository projects, IEntityAssetRepository entityAssets,
        ILabelRepository labels, IProjectMemberRepository members, IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _projects = projects;
        _entityAssets = entityAssets;
        _labels = labels;
        _members = members;
        _employees = employees;
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

        var projectIds = items.Select(p => p.Id).ToList();

        var logos = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Project, projectIds, UploadPurposeCatalog.ProjectCover, ct);
        var labels = await _labels.GetByProjectIdsAsync(tenantId, projectIds, MaxLabelsPerProject, ct);

        var memberUserIdsByProject = await _members.ListDistinctActiveMemberUserIdsAsync(tenantId, projectIds, MaxMembersPerProject, ct);
        var memberCounts = await _members.CountDistinctActiveMembersAsync(tenantId, projectIds, ct);
        var displayNames = await ProjectMemberAvatarResolver.ResolveDisplayNamesAsync(_employees, tenantId, memberUserIdsByProject, ct);

        var dtoItems = items
            .Select(p => ProjectMapper.ToListItem(
                p, p.LeadId == targetUserId,
                logos.TryGetValue(p.Id, out var fileId) ? fileId : null,
                labels.TryGetValue(p.Id, out var projectLabels) ? projectLabels : null,
                ProjectMemberAvatarResolver.BuildAvatars(p.Id, memberUserIdsByProject, displayNames),
                memberCounts.TryGetValue(p.Id, out var count) ? count : 0))
            .ToList();

        return Result<PagedResult<ProjectListItemResponse>>.Success(
            new PagedResult<ProjectListItemResponse>(dtoItems, pageNumber, request.Paging.PageSize, total));
    }
}
