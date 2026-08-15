using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Proven by EmployeesListIntegrationTests against real PostgreSQL (the EF InMemory
    /// provider used by the unit tests is more lenient and does not reproduce this): EF Core's
    /// query translator treats an anonymous-type projection as transparent and will keep
    /// composing further Where/OrderBy/Select over it, but refuses to translate anything
    /// chained after a projection into a user-defined record (constructor-call projection) -
    /// and a C# tuple literal can't be used in a query expression at all (CS8143: expression
    /// trees may not contain a tuple literal), so a tuple-typed helper method isn't an option
    /// either. Net effect: this whole join-filter-order-project pipeline has to stay inside a
    /// single unbroken `var`-typed anonymous-type chain, with no extracted method boundary in
    /// between (a private method can't declare an anonymous type as its return type). That
    /// forces the join clauses to be duplicated between ListVisibleAsync and
    /// GetVisibleByIdAsync below rather than shared.
    /// </summary>
    public async Task<(IReadOnlyList<EmployeeListItemResponse> Items, int TotalCount)> ListVisibleAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        EmployeeListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var directManagerClosure = _db.EmployeeHierarchyClosures.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Depth == 1);

        var joined =
            from e in _db.Employees.AsNoTracking()
            where e.TenantId == tenantId
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals legalEntity.Id into leJoin
            from legalEntity in leJoin.DefaultIfEmpty()
            join empType in _db.EmploymentTypes.AsNoTracking() on e.EmploymentTypeId equals empType.Id into typeJoin
            from empType in typeJoin.DefaultIfEmpty()
            join empStatus in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals empStatus.Id into statusJoin
            from empStatus in statusJoin.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on e.Id equals primaryAssignment.EmployeeId into paJoin
            from primaryAssignment in paJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            join closure in directManagerClosure on e.Id equals closure.DescendantEmployeeId into closureJoin
            from closure in closureJoin.DefaultIfEmpty()
            join manager in _db.Employees.AsNoTracking() on closure!.AncestorEmployeeId equals manager.Id into managerJoin
            from manager in managerJoin.DefaultIfEmpty()
            select new { e, dept, legalEntity, empType, empStatus, position, manager };

        if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            joined = joined.Where(row =>
                (ownEmployeeId != null && row.e.Id == ownEmployeeId.Value)
                || (row.position != null && coveredPositionIds.Contains(row.position.Id))
                || (row.dept != null && coveredDepartmentIds.Contains(row.dept.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var normalized = filter.Search.Trim().ToLower();
            joined = joined.Where(row =>
                row.e.FirstName.ToLower().Contains(normalized)
                || row.e.LastName.ToLower().Contains(normalized)
                || row.e.Email.ToLower().Contains(normalized)
                || row.e.EmployeeNumber.ToLower().Contains(normalized));
        }

        if (filter.DepartmentId is not null)
        {
            joined = joined.Where(row => row.dept != null && row.dept.Id == filter.DepartmentId.Value);
        }

        if (filter.LegalEntityId is not null)
        {
            joined = joined.Where(row => row.legalEntity != null && row.legalEntity.Id == filter.LegalEntityId.Value);
        }

        var totalCount = await joined.CountAsync(ct);

        var items = await joined
            .OrderBy(row => row.e.LastName).ThenBy(row => row.e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new EmployeeListItemResponse(
                row.e.Id,
                row.e.EmployeeNumber,
                row.e.FirstName + " " + row.e.LastName,
                row.e.Email,
                row.dept != null ? row.dept.Id : (Guid?)null,
                row.dept != null ? row.dept.Name : null,
                row.position != null ? row.position.Id : (Guid?)null,
                row.position != null ? row.position.Name : null,
                row.legalEntity != null ? row.legalEntity.Id : (Guid?)null,
                row.legalEntity != null ? row.legalEntity.Name : null,
                row.empType != null ? row.empType.Label : row.e.EmploymentTypeId.ToString(),
                row.empStatus != null ? row.empStatus.Code : "active",
                row.manager != null ? row.manager.Id : (Guid?)null,
                row.manager != null ? row.manager.FirstName + " " + row.manager.LastName : null))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<EmployeeListItemResponse?> GetVisibleByIdAsync(
        Guid tenantId, EmployeeVisibilityScope scope, Guid employeeId, CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var directManagerClosure = _db.EmployeeHierarchyClosures.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Depth == 1);

        var joined =
            from e in _db.Employees.AsNoTracking()
            where e.TenantId == tenantId && e.Id == employeeId
            join dept in _db.Departments.AsNoTracking() on e.DepartmentId equals dept.Id into deptJoin
            from dept in deptJoin.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on e.LegalEntityId equals legalEntity.Id into leJoin
            from legalEntity in leJoin.DefaultIfEmpty()
            join empType in _db.EmploymentTypes.AsNoTracking() on e.EmploymentTypeId equals empType.Id into typeJoin
            from empType in typeJoin.DefaultIfEmpty()
            join empStatus in _db.EmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals empStatus.Id into statusJoin
            from empStatus in statusJoin.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on e.Id equals primaryAssignment.EmployeeId into paJoin
            from primaryAssignment in paJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into posJoin
            from position in posJoin.DefaultIfEmpty()
            join closure in directManagerClosure on e.Id equals closure.DescendantEmployeeId into closureJoin
            from closure in closureJoin.DefaultIfEmpty()
            join manager in _db.Employees.AsNoTracking() on closure!.AncestorEmployeeId equals manager.Id into managerJoin
            from manager in managerJoin.DefaultIfEmpty()
            select new { e, dept, legalEntity, empType, empStatus, position, manager };

        if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            joined = joined.Where(row =>
                (ownEmployeeId != null && row.e.Id == ownEmployeeId.Value)
                || (row.position != null && coveredPositionIds.Contains(row.position.Id))
                || (row.dept != null && coveredDepartmentIds.Contains(row.dept.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        return await joined
            .Select(row => new EmployeeListItemResponse(
                row.e.Id,
                row.e.EmployeeNumber,
                row.e.FirstName + " " + row.e.LastName,
                row.e.Email,
                row.dept != null ? row.dept.Id : (Guid?)null,
                row.dept != null ? row.dept.Name : null,
                row.position != null ? row.position.Id : (Guid?)null,
                row.position != null ? row.position.Name : null,
                row.legalEntity != null ? row.legalEntity.Id : (Guid?)null,
                row.legalEntity != null ? row.legalEntity.Name : null,
                row.empType != null ? row.empType.Label : row.e.EmploymentTypeId.ToString(),
                row.empStatus != null ? row.empStatus.Code : "active",
                row.manager != null ? row.manager.Id : (Guid?)null,
                row.manager != null ? row.manager.FirstName + " " + row.manager.LastName : null))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<EmployeeEntity?> GetByIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);

    public async Task<EmployeeEntity?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);

    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLower();
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Email.ToLower() == normalized);

        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> EmployeeNumberExistsAsync(Guid tenantId, string employeeNumber, Guid? excludeId, CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmployeeNumber == employeeNumber);

        if (excludeId is not null)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.Employees.AsNoTracking().CountAsync(e => e.TenantId == tenantId, ct);

    public async Task AddAsync(EmployeeEntity employee, CancellationToken ct = default)
        => await _db.Employees.AddAsync(employee, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
