using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;

public class EfLeavePolicyResolutionRepository : ILeavePolicyResolutionRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeavePolicyResolutionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<LeavePolicyResolutionSnapshot> LoadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToArray();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && ids.Contains(e.Id))
            .Select(e => new LeaveResolutionEmployee(
                e.Id, (e.FirstName + " " + e.LastName).Trim(), e.LegalEntityId, e.DepartmentId))
            .ToListAsync(ct);

        var positions = await _db.PositionAssignments.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && ids.Contains(p.EmployeeId)
                && p.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && (p.AssignmentStatus == PositionAssignmentStatus.Active
                    || p.AssignmentStatus == PositionAssignmentStatus.Ended))
            .Select(p => new LeaveResolutionPosition(p.EmployeeId, p.PositionId, p.EffectiveFrom, p.EffectiveTo))
            .ToListAsync(ct);

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .Select(d => new { d.Id, d.ParentDepartmentId, d.Name })
            .ToListAsync(ct);

        var policies = await (
                from policy in _db.LeavePolicies.AsNoTracking()
                join leaveType in _db.LeaveTypes.AsNoTracking() on policy.LeaveTypeId equals leaveType.Id
                where policy.TenantId == tenantId && policy.IsActive && leaveType.IsActive
                select new LeaveResolutionPolicy(policy.Id, policy.Name, policy.LeaveTypeId))
            .ToListAsync(ct);
        var policyIds = policies.Select(p => p.Id).ToArray();

        var versions = await _db.LeavePolicyVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && policyIds.Contains(v.LeavePolicyId))
            .ToListAsync(ct);
        var versionIds = versions.Select(v => v.Id).ToArray();

        var blackouts = await _db.LeavePolicyBlackoutPeriods.AsNoTracking()
            .Where(b => b.TenantId == tenantId && versionIds.Contains(b.LeavePolicyVersionId))
            .ToListAsync(ct);

        var assignments = await _db.LeavePolicyAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && policyIds.Contains(a.LeavePolicyId))
            .ToListAsync(ct);

        var names = new Dictionary<(string Kind, Guid Id), string>();
        foreach (var employee in employees)
            names[(LeavePolicyTargetKinds.Employee, employee.Id)] = employee.Name;
        foreach (var department in departments)
            names[(LeavePolicyTargetKinds.Department, department.Id)] = department.Name;

        var positionIds = positions.Select(p => p.PositionId).Distinct().ToArray();
        foreach (var position in await _db.Positions.AsNoTracking()
                     .Where(p => p.TenantId == tenantId && positionIds.Contains(p.Id))
                     .Select(p => new { p.Id, p.Name })
                     .ToListAsync(ct))
            names[(LeavePolicyTargetKinds.Position, position.Id)] = position.Name;

        var legalEntityIds = employees.Select(e => e.LegalEntityId).OfType<Guid>().Distinct().ToArray();
        foreach (var legalEntity in await _db.LegalEntities.AsNoTracking()
                     .Where(l => l.TenantId == tenantId && legalEntityIds.Contains(l.Id))
                     .Select(l => new { l.Id, l.Name })
                     .ToListAsync(ct))
            names[(LeavePolicyTargetKinds.LegalEntity, legalEntity.Id)] = legalEntity.Name;

        return new LeavePolicyResolutionSnapshot(
            employees,
            positions,
            departments.Select(d => new LeaveResolutionDepartment(d.Id, d.ParentDepartmentId)).ToList(),
            policies,
            versions,
            blackouts,
            assignments,
            names);
    }
}
