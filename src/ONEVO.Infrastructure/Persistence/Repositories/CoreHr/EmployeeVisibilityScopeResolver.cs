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
            false, ownEmployeeId, coveredPositionIds, coveredDepartmentIds, companyWideLegalEntityIds);
    }
}
