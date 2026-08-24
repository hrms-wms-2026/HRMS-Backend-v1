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
    private readonly IPositionRepository _positions;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListDepartmentsQueryHandler(
        IDepartmentRepository departments,
        IPositionRepository positions,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _departments = departments;
        _positions = positions;
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

            var (positionCounts, employeeCounts, positionNames) =
                await LoadEnrichmentAsync(tenantId, request.LegalEntityId, treeDepartments, ct);

            var treeItems = DepartmentTreeMapper.BuildTree(treeDepartments, positionCounts, employeeCounts, positionNames);

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

        var (flatPositionCounts, flatEmployeeCounts, flatPositionNames) =
            await LoadEnrichmentAsync(tenantId, request.LegalEntityId, page.Items, ct);

        var items = page.Items
            .Select(department => DepartmentMapper.ToListItemResponse(department, flatPositionCounts, flatEmployeeCounts, flatPositionNames))
            .ToList();
        var flat = new DepartmentListPageResponse(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);

        return Result<DepartmentListResult>.Success(new DepartmentListResult(Flat: flat, Tree: null));
    }

    // Single batched round trip per count type (no per-row queries): position counts and
    // employee counts grouped by department id, plus a batched name lookup for whichever
    // positions are set as a department's HeadPositionId.
    private async Task<(
        IReadOnlyDictionary<Guid, int> PositionCounts,
        IReadOnlyDictionary<Guid, int> EmployeeCounts,
        IReadOnlyDictionary<Guid, string> PositionNames)> LoadEnrichmentAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyList<ONEVO.Domain.Features.OrgStructure.Entities.Department> departments, CancellationToken ct)
    {
        var departmentIds = departments.Select(department => department.Id).ToList();
        var headPositionIds = departments
            .Where(department => department.HeadPositionId is not null)
            .Select(department => department.HeadPositionId!.Value)
            .Distinct()
            .ToList();

        var positionCounts = await _positions.CountActiveByDepartmentIdsAsync(tenantId, legalEntityId, departmentIds, ct);
        var employeeCounts = await _departments.CountActiveEmployeesByDepartmentIdsAsync(tenantId, legalEntityId, departmentIds, ct);
        var headPositions = await _positions.GetByIdsAsync(tenantId, headPositionIds, ct);
        var positionNames = headPositions.ToDictionary(position => position.Id, position => position.Name);

        return (positionCounts, employeeCounts, positionNames);
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
