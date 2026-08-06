using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public class ListDepartmentsQueryHandler
    : IRequestHandler<ListDepartmentsQuery, Result<DepartmentListResult>>
{
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListDepartmentsQueryHandler(
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<DepartmentListResult>> Handle(
        ListDepartmentsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<DepartmentListResult>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<DepartmentListResult>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<DepartmentListResult>.NotFound("Legal entity not found.");

        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var normalizedView = request.View.Trim().ToLowerInvariant();

        if (normalizedView == "tree")
        {
            var treeDepartments = await _departments.ListForTreeByLegalEntityAsync(
                tenantId, request.LegalEntityId, normalizedSearch, request.IncludeInactive, ct);
            var treeItems = DepartmentTreeMapper.BuildTree(treeDepartments);

            return Result<DepartmentListResult>.Success(
                new DepartmentListResult(Flat: null, Tree: new DepartmentTreeResponse(treeItems)));
        }

        var sortBy = ParseSortBy(request.SortBy);
        var sortDirection = ParseSortDirection(request.SortDirection);

        var page = await _departments.ListPageByLegalEntityAsync(
            tenantId,
            request.LegalEntityId,
            normalizedSearch,
            request.IncludeInactive,
            request.ParentDepartmentId,
            sortBy,
            sortDirection,
            request.Page,
            request.PageSize,
            ct);

        var items = page.Items.Select(DepartmentMapper.ToListItemResponse).ToList();
        var flat = new DepartmentListPageResponse(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);

        return Result<DepartmentListResult>.Success(new DepartmentListResult(Flat: flat, Tree: null));
    }

    private static DepartmentSortBy ParseSortBy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "name" => DepartmentSortBy.Name,
            "code" => DepartmentSortBy.Code,
            "createdat" => DepartmentSortBy.CreatedAt,
            "updatedat" => DepartmentSortBy.UpdatedAt,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sortBy value.")
        };
    }

    private static SortDirection ParseSortDirection(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "asc" => SortDirection.Ascending,
            "desc" => SortDirection.Descending,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sortDirection value.")
        };
    }
}
