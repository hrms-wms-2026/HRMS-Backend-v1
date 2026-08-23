using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;

// Namespace deliberately stops at the feature segment: a ".Department" segment would
// collide with the Department entity type and force using-aliases everywhere (same
// convention as EfLegalEntityRepository/EfPositionRepository).
namespace ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;

public class EfDepartmentRepository : IDepartmentRepository
{
    private readonly ApplicationDbContext _db;

    public EfDepartmentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Department>> ListByLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        query = query.OrderBy(department => department.Name);

        var results = await query.ToListAsync(ct);
        return results;
    }

    public async Task<DepartmentPage> ListPageByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        Guid? parentDepartmentId,
        DepartmentSortBy sortBy,
        SortDirection sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(department =>
                department.Name.ToLower().Contains(normalizedSearch)
                || (department.Code != null && department.Code.ToLower().Contains(normalizedSearch)));
        }

        if (parentDepartmentId is not null)
        {
            query = query.Where(department => department.ParentDepartmentId == parentDepartmentId.Value);
        }

        query = ApplySort(query, sortBy, sortDirection);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new DepartmentPage(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<IReadOnlyList<Department>> ListForTreeByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        string? search,
        bool includeInactive,
        CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(department =>
                department.Name.ToLower().Contains(normalizedSearch)
                || (department.Code != null && department.Code.ToLower().Contains(normalizedSearch)));
        }

        query = query.OrderBy(department => department.Name).ThenBy(department => department.Id);

        var results = await query.ToListAsync(ct);
        return results;
    }

    private static IQueryable<Department> ApplySort(
        IQueryable<Department> query, DepartmentSortBy sortBy, SortDirection sortDirection)
    {
        var ascending = sortDirection == SortDirection.Ascending;

        return sortBy switch
        {
            DepartmentSortBy.Code => ascending
                ? query.OrderBy(department => department.Code).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.Code).ThenBy(department => department.Id),
            DepartmentSortBy.CreatedAt => ascending
                ? query.OrderBy(department => department.CreatedAt).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.CreatedAt).ThenBy(department => department.Id),
            DepartmentSortBy.UpdatedAt => ascending
                ? query.OrderBy(department => department.UpdatedAt).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.UpdatedAt).ThenBy(department => department.Id),
            _ => ascending
                ? query.OrderBy(department => department.Name).ThenBy(department => department.Id)
                : query.OrderByDescending(department => department.Name).ThenBy(department => department.Id),
        };
    }

    public async Task<Department?> GetByIdAsync(
        Guid tenantId, Guid departmentId, CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department => department.TenantId == tenantId && department.Id == departmentId);

        var result = await query.FirstOrDefaultAsync(ct);
        return result;
    }

    public async Task<Department?> GetByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.Id == departmentId);

        var result = await query.FirstOrDefaultAsync(ct);
        return result;
    }

    public async Task<bool> ExistsByNameAsync(
        Guid tenantId,
        Guid legalEntityId,
        string name,
        Guid? excludingDepartmentId,
        CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.Name == name);

        if (excludingDepartmentId is not null)
        {
            query = query.Where(department => department.Id != excludingDepartmentId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> ExistsByCodeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string code,
        Guid? excludingDepartmentId,
        CancellationToken ct = default)
    {
        var normalizedCode = code.ToLower();

        var query = _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.Code != null
                && department.Code.ToLower() == normalizedCode);

        if (excludingDepartmentId is not null)
        {
            query = query.Where(department => department.Id != excludingDepartmentId.Value);
        }

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> ExistsAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var query = _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.Id == departmentId);

        var exists = await query.AnyAsync(ct);
        return exists;
    }

    public async Task<bool> IsDescendantAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid departmentId,
        Guid possibleDescendantId,
        CancellationToken ct = default)
    {
        var descendantIds = _db.Database.SqlQuery<Guid>($@"
            WITH RECURSIVE descendants AS (
                SELECT id FROM departments
                WHERE tenant_id = {tenantId} AND legal_entity_id = {legalEntityId}
                    AND parent_department_id = {departmentId}
                UNION ALL
                SELECT d.id FROM departments d
                INNER JOIN descendants ON d.parent_department_id = descendants.id
                WHERE d.tenant_id = {tenantId} AND d.legal_entity_id = {legalEntityId}
            )
            SELECT id AS ""Value"" FROM descendants
        ");

        var isDescendant = await descendantIds.AnyAsync(id => id == possibleDescendantId, ct);
        return isDescendant;
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var descendantIds = _db.Database.SqlQuery<Guid>($@"
            WITH RECURSIVE descendants AS (
                SELECT id FROM departments
                WHERE tenant_id = {tenantId} AND legal_entity_id = {legalEntityId}
                    AND parent_department_id = {departmentId} AND is_active = true
                UNION ALL
                SELECT d.id FROM departments d
                INNER JOIN descendants ON d.parent_department_id = descendants.id
                WHERE d.tenant_id = {tenantId} AND d.legal_entity_id = {legalEntityId} AND d.is_active = true
            )
            SELECT id AS ""Value"" FROM descendants
        ");

        return await descendantIds.ToListAsync(ct);
    }

    public async Task<int> CountActiveChildrenAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        var count = await _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.ParentDepartmentId == departmentId
                && department.IsActive)
            .CountAsync(ct);

        return count;
    }

    public async Task<int> CountActiveEmployeesAsync(
        Guid tenantId, Guid legalEntityId, Guid departmentId, CancellationToken ct = default)
    {
        // "Active" means employment_statuses.code = "active" (not a hardcoded id), scoped
        // explicitly by tenant/legal-entity/department. BaseEntity's IsDeleted filter is
        // already applied automatically by the global query filter on Employees.
        var count = await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId
                && employee.LegalEntityId == legalEntityId
                && employee.DepartmentId == departmentId
                && status.Code == "active"
            select employee.Id)
            .CountAsync(ct);

        return count;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveEmployeesByDepartmentIdsAsync(
        Guid tenantId, Guid legalEntityId, IReadOnlyCollection<Guid> departmentIds, CancellationToken ct = default)
    {
        if (departmentIds.Count == 0)
            return new Dictionary<Guid, int>();

        // Same "active" semantics as CountActiveEmployeesAsync (employment_statuses.code =
        // "active"), grouped by department in one round trip instead of one query per department.
        var counts = await (
            from employee in _db.Employees.AsNoTracking()
            join status in _db.EmploymentStatuses.AsNoTracking()
                on employee.EmploymentStatusId equals status.Id
            where employee.TenantId == tenantId
                && employee.LegalEntityId == legalEntityId
                && employee.DepartmentId != null
                && departmentIds.Contains(employee.DepartmentId!.Value)
                && status.Code == "active"
            group employee by employee.DepartmentId!.Value into g
            select new { DepartmentId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(row => row.DepartmentId, row => row.Count);
    }

    public async Task AddAsync(Department department, CancellationToken ct = default)
    {
        await _db.Departments.AddAsync(department, ct);
    }

    public void Update(Department department)
    {
        _db.Departments.Update(department);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var affectedRows = await _db.SaveChangesAsync(ct);
        return affectedRows;
    }
}
