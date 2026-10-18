using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EmployeeVisibilityScopeResolver : IEmployeeVisibilityScopeResolver
{
    private readonly ApplicationDbContext _db;

    public EmployeeVisibilityScopeResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeVisibilityScope> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var ownEmployeeId = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UserId == userId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        if (ownEmployeeId is null)
        {
            // No employee profile for this user (or no active primary assignment yet):
            // position/department coverage is uncomputable, so the caller sees nothing
            // beyond an explicit company-wide grant they hold directly (there is none to
            // resolve without an owning position), matching Locked Decision 5.
            return new EmployeeVisibilityScope(
                false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }

        var ownPositionId = await _db.PositionAssignments
            .AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.EmployeeId == ownEmployeeId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active)
            .Select(pa => (Guid?)pa.PositionId)
            .FirstOrDefaultAsync(ct);

        if (ownPositionId is null)
        {
            return new EmployeeVisibilityScope(
                false, ownEmployeeId, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }

        var coverageRows = await _db.Set<ManagementCoverageRecord>()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                && c.OwnerPositionId == ownPositionId
                && c.Status == ManagementCoverageRecord.StatusActive)
            .ToListAsync(ct);

        var coveredPositionIds = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetPosition && c.CoveredPositionId is not null)
            .Select(c => c.CoveredPositionId!.Value)
            .ToHashSet();

        var coveredDepartmentIds = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetDepartment && c.CoveredDepartmentId is not null)
            .Select(c => c.CoveredDepartmentId!.Value)
            .ToHashSet();

        var companyWideLegalEntityIds = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetCompany)
            .Select(c => c.LegalEntityId)
            .ToHashSet();

        return new EmployeeVisibilityScope(
            false, ownEmployeeId, coveredPositionIds, coveredDepartmentIds, companyWideLegalEntityIds)
        {
            CoveredEmployeeIds = await ExpandCoveredEmployeesAsync(tenantId, coverageRows, ct),
        };
    }

    /// <summary>
    /// Mirrors EmployeeAuthorityResolver.AddManagedVisibilityAsync's expansion (covered position
    /// holders + their reporting-line subtree; descendant departments of covered departments),
    /// per coverage row's legal entity, so per-employee checks agree with the Employees list.
    /// Company-wide coverage needs no expansion - CompanyWideLegalEntityIds already matches it.
    /// Plain LINQ over the flat closure/department tables (no recursive SQL) keeps it testable on
    /// the InMemory provider.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> ExpandCoveredEmployeesAsync(
        Guid tenantId, IReadOnlyCollection<ManagementCoverageRecord> coverageRows, CancellationToken ct)
    {
        var covered = new HashSet<Guid>();

        var positionRows = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetPosition && c.CoveredPositionId is not null)
            .ToList();
        if (positionRows.Count > 0)
        {
            var positionIds = positionRows.Select(c => c.CoveredPositionId!.Value).Distinct().ToList();
            var holders = await _db.PositionAssignments
                .AsNoTracking()
                .Where(pa => pa.TenantId == tenantId
                    && positionIds.Contains(pa.PositionId)
                    && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                    && pa.AssignmentStatus == PositionAssignmentStatus.Active)
                .Select(pa => new { pa.EmployeeId, pa.PositionId })
                .ToListAsync(ct);

            if (holders.Count > 0)
            {
                var holderIds = holders.Select(h => h.EmployeeId).Distinct().ToList();
                var descendants = await _db.EmployeeHierarchyClosures
                    .AsNoTracking()
                    .Where(c => c.TenantId == tenantId && holderIds.Contains(c.AncestorEmployeeId))
                    .Select(c => new { c.AncestorEmployeeId, c.DescendantEmployeeId })
                    .ToListAsync(ct);

                var candidateIds = holderIds.Concat(descendants.Select(d => d.DescendantEmployeeId)).Distinct().ToList();
                var legalEntityByEmployee = await _db.Employees
                    .AsNoTracking()
                    .Where(e => e.TenantId == tenantId && candidateIds.Contains(e.Id))
                    .Select(e => new { e.Id, e.LegalEntityId })
                    .ToDictionaryAsync(e => e.Id, e => e.LegalEntityId, ct);

                foreach (var row in positionRows)
                {
                    var rowHolderIds = holders
                        .Where(h => h.PositionId == row.CoveredPositionId!.Value)
                        .Select(h => h.EmployeeId)
                        .ToHashSet();
                    var reached = rowHolderIds
                        .Concat(descendants.Where(d => rowHolderIds.Contains(d.AncestorEmployeeId)).Select(d => d.DescendantEmployeeId));
                    covered.UnionWith(reached.Where(id =>
                        legalEntityByEmployee.TryGetValue(id, out var le) && le == row.LegalEntityId));
                }
            }
        }

        var departmentRows = coverageRows
            .Where(c => c.CoveredTargetType == ManagementCoverageRecord.TargetDepartment && c.CoveredDepartmentId is not null)
            .ToList();
        if (departmentRows.Count > 0)
        {
            var legalEntityIds = departmentRows.Select(c => c.LegalEntityId).Distinct().ToList();
            var departments = await _db.Departments
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId && legalEntityIds.Contains(d.LegalEntityId) && d.IsActive)
                .Select(d => new { d.Id, d.ParentDepartmentId, d.LegalEntityId })
                .ToListAsync(ct);
            var childrenByParent = departments
                .Where(d => d.ParentDepartmentId is not null)
                .ToLookup(d => d.ParentDepartmentId!.Value);

            // Only DESCENDANT departments are collected here; the covered department itself is
            // already matched by CoveredDepartmentIds.
            var descendantDepartmentIds = new HashSet<Guid>();
            foreach (var row in departmentRows)
            {
                var pending = new Queue<Guid>();
                pending.Enqueue(row.CoveredDepartmentId!.Value);
                while (pending.Count > 0)
                {
                    foreach (var child in childrenByParent[pending.Dequeue()])
                    {
                        if (child.LegalEntityId == row.LegalEntityId && descendantDepartmentIds.Add(child.Id))
                            pending.Enqueue(child.Id);
                    }
                }
            }

            if (descendantDepartmentIds.Count > 0)
            {
                var departmentEmployeeIds = await _db.Employees
                    .AsNoTracking()
                    .Where(e => e.TenantId == tenantId
                        && e.DepartmentId != null
                        && descendantDepartmentIds.Contains(e.DepartmentId.Value))
                    .Select(e => e.Id)
                    .ToListAsync(ct);
                covered.UnionWith(departmentEmployeeIds);
            }
        }

        return covered;
    }
}
