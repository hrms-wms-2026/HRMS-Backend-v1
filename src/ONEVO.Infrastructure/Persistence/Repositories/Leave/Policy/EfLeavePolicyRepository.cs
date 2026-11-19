using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;

public class EfLeavePolicyRepository : ILeavePolicyRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeavePolicyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await _db.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && legalEntityIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }

    public Task<LeaveType?> GetLeaveTypeAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default) =>
        _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == leaveTypeId, ct);

    public Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingPolicyId, CancellationToken ct = default)
    {
        var normalized = name.ToLower();
        return _db.LeavePolicies.AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Name.ToLower() == normalized
                && (excludingPolicyId == null || p.Id != excludingPolicyId), ct);
    }

    public Task<LeavePolicy?> GetTrackedAsync(Guid tenantId, Guid policyId, CancellationToken ct = default) =>
        _db.LeavePolicies.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId, ct);

    public async Task<IReadOnlyList<LeavePolicy>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default) =>
        await _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && (includeInactive || p.IsActive))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeavePolicyVersion>> ListVersionsTrackedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        await _db.LeavePolicyVersions
            .Where(v => v.TenantId == tenantId && policyIds.Contains(v.LeavePolicyId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeavePolicyBlackoutPeriod>> ListBlackoutsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> versionIds, CancellationToken ct = default) =>
        await _db.LeavePolicyBlackoutPeriods.AsNoTracking()
            .Where(b => b.TenantId == tenantId && versionIds.Contains(b.LeavePolicyVersionId))
            .OrderBy(b => b.StartDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForLeaveTypeTrackedAsync(
        Guid tenantId, Guid leaveTypeId, CancellationToken ct = default) =>
        await _db.LeavePolicyAssignments
            .Where(a => a.TenantId == tenantId && a.LeaveTypeId == leaveTypeId && a.EffectiveTo == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeavePolicyAssignment>> ListOpenAssignmentsForPoliciesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        await _db.LeavePolicyAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && policyIds.Contains(a.LeavePolicyId) && a.EffectiveTo == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeavePolicyAssignmentInput>> ListMissingTargetsAsync(
        Guid tenantId, IReadOnlyCollection<LeavePolicyAssignmentInput> targets, CancellationToken ct = default)
    {
        var found = await GetTargetNamesAsync(
            tenantId, targets.Select(t => (t.TargetKind, t.TargetId)).ToList(), ct, activeOnly: true);
        return targets.Where(t => !found.ContainsKey((t.TargetKind, t.TargetId))).ToList();
    }

    public Task<IReadOnlyDictionary<(string Kind, Guid Id), string>> GetTargetNamesAsync(
        Guid tenantId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken ct = default) =>
        GetTargetNamesAsync(tenantId, targets, ct, activeOnly: false);

    private async Task<IReadOnlyDictionary<(string Kind, Guid Id), string>> GetTargetNamesAsync(
        Guid tenantId, IReadOnlyCollection<(string Kind, Guid Id)> targets, CancellationToken ct, bool activeOnly)
    {
        Guid[] Ids(string kind) => targets.Where(t => t.Kind == kind).Select(t => t.Id).Distinct().ToArray();
        var names = new Dictionary<(string Kind, Guid Id), string>();

        var legalEntityIds = Ids(LeavePolicyTargetKinds.LegalEntity);
        if (legalEntityIds.Length > 0)
            foreach (var x in await _db.LegalEntities.AsNoTracking()
                         .Where(e => e.TenantId == tenantId && legalEntityIds.Contains(e.Id) && (!activeOnly || e.IsActive))
                         .Select(e => new { e.Id, e.Name }).ToListAsync(ct))
                names[(LeavePolicyTargetKinds.LegalEntity, x.Id)] = x.Name;

        var departmentIds = Ids(LeavePolicyTargetKinds.Department);
        if (departmentIds.Length > 0)
            foreach (var x in await _db.Departments.AsNoTracking()
                         .Where(d => d.TenantId == tenantId && departmentIds.Contains(d.Id) && (!activeOnly || d.IsActive))
                         .Select(d => new { d.Id, d.Name }).ToListAsync(ct))
                names[(LeavePolicyTargetKinds.Department, x.Id)] = x.Name;

        var positionIds = Ids(LeavePolicyTargetKinds.Position);
        if (positionIds.Length > 0)
            foreach (var x in await _db.Positions.AsNoTracking()
                         .Where(p => p.TenantId == tenantId && positionIds.Contains(p.Id) && !p.IsDeleted
                             && (!activeOnly || p.IsActive))
                         .Select(p => new { p.Id, p.Name }).ToListAsync(ct))
                names[(LeavePolicyTargetKinds.Position, x.Id)] = x.Name;

        var employeeIds = Ids(LeavePolicyTargetKinds.Employee);
        if (employeeIds.Length > 0)
            foreach (var x in await _db.Employees.AsNoTracking()
                         .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id) && !e.IsDeleted)
                         .Select(e => new { e.Id, e.FirstName, e.LastName }).ToListAsync(ct))
                names[(LeavePolicyTargetKinds.Employee, x.Id)] = $"{x.FirstName} {x.LastName}".Trim();

        return names;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPolicyNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> policyIds, CancellationToken ct = default) =>
        await _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && policyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        var rows = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && userIds.Contains(e.UserId) && !e.IsDeleted)
            .Select(e => new { e.UserId, e.FirstName, e.LastName })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => $"{g.First().FirstName} {g.First().LastName}".Trim());
    }

    public Task<bool> IsUsersOwnEmployeeAsync(Guid tenantId, Guid employeeId, Guid userId, CancellationToken ct = default) =>
        _db.Employees.AsNoTracking().AnyAsync(e => e.TenantId == tenantId && e.Id == employeeId && e.UserId == userId, ct);

    public void AddPolicy(LeavePolicy policy) => _db.LeavePolicies.Add(policy);

    public void AddVersion(LeavePolicyVersion version, IReadOnlyList<LeavePolicyBlackoutPeriod> blackouts)
    {
        _db.LeavePolicyVersions.Add(version);
        _db.LeavePolicyBlackoutPeriods.AddRange(blackouts);
    }

    public void RemoveVersion(LeavePolicyVersion version) => _db.LeavePolicyVersions.Remove(version);

    public void AddAssignment(LeavePolicyAssignment assignment) => _db.LeavePolicyAssignments.Add(assignment);

    public void RemoveAssignment(LeavePolicyAssignment assignment) => _db.LeavePolicyAssignments.Remove(assignment);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
