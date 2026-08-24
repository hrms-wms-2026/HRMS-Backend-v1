using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Department" segment would
// collide with the Department entity type and force using-aliases everywhere (same
// convention as ILegalEntityRepository/IPositionRepository).
namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public interface IDepartmentRepository
{
    Task<IReadOnlyList<Department>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default);

    Task<DepartmentPage> ListPageByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        Guid? parentDepartmentId,
        DepartmentSortBy sortBy,
        SortDirection sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<Department>> ListForTreeByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        CancellationToken ct = default);

    Task<Department?> GetByIdAsync(
        Guid tenantId, Guid departmentId, CancellationToken ct = default);

    Task<Department?> GetByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(
        Guid tenantId,
        Guid legalEntityId,
        string name,
        Guid? excludingDepartmentId,
        CancellationToken ct = default);

    Task<bool> ExistsByCodeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string code,
        Guid? excludingDepartmentId,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    Task<bool> IsDescendantAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        Guid possibleDescendantId,
        CancellationToken ct = default);

    Task<int> CountActiveChildrenAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    /// <summary>Transitive descendant department ids (any depth, departmentId itself excluded) of
    /// one department, used by IEmployeeAuthorityResolver to expand a covered department into its
    /// full sub-tree for visibility. Implemented as a recursive CTE, same convention as
    /// IsDescendantAsync above, filtered to is_active = true at every level - so an inactive
    /// intermediate department truncates the walk there, excluding its active children too, not
    /// just itself.</summary>
    Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    Task<int> CountActiveEmployeesAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default);

    // Batched variant of CountActiveEmployeesAsync: single grouped query for a page/tree of
    // departments instead of one query per department. Missing keys mean zero active employees.
    Task<IReadOnlyDictionary<Guid, int>> CountActiveEmployeesByDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default);

    Task AddAsync(Department department, CancellationToken ct = default);

    void Update(Department department);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
