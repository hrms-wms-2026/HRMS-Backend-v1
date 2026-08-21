using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
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

    public async Task<IReadOnlyList<LeavePolicyAggregate>> ListAsync(
        Guid tenantId, bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.LeavePolicies.AsNoTracking().Where(p => p.TenantId == tenantId);
        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        var policies = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return await BuildAggregatesAsync(tenantId, policies, ct);
    }

    public async Task<LeavePolicyAggregate?> GetAggregateByIdAsync(
        Guid tenantId, Guid leavePolicyId, CancellationToken ct = default)
    {
        var policy = await _db.LeavePolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == leavePolicyId, ct);

        if (policy is null)
            return null;

        return (await BuildAggregatesAsync(tenantId, [policy], ct)).Single();
    }

    public async Task<bool> ExistsByNameAsync(
        Guid tenantId, string name, Guid? excludingLeavePolicyId, CancellationToken ct = default)
    {
        var normalized = name.ToLower();
        var query = _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Name.ToLower() == normalized);

        if (excludingLeavePolicyId is { } id)
            query = query.Where(p => p.Id != id);

        return await query.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveType>> ListActiveLeaveTypesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> leaveTypeIds, CancellationToken ct = default)
    {
        return await _db.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive && leaveTypeIds.Contains(t.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await _db.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && legalEntityIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeavePolicyLegalEntityConflict>> ListActiveAssignmentConflictsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await (
            from assignment in _db.LeavePolicyLegalEntities.AsNoTracking()
            join policy in _db.LeavePolicies.AsNoTracking() on assignment.LeavePolicyId equals policy.Id
            join legalEntity in _db.LegalEntities.AsNoTracking() on assignment.LegalEntityId equals legalEntity.Id
            where assignment.TenantId == tenantId
                && assignment.IsActive
                && legalEntityIds.Contains(assignment.LegalEntityId)
            orderby legalEntity.Name
            select new LeavePolicyLegalEntityConflict(
                assignment.LegalEntityId,
                legalEntity.Name,
                policy.Id,
                policy.Name))
            .ToListAsync(ct);
    }

    public async Task AddAggregateWithReplacementAsync(
        LeavePolicy policy,
        IReadOnlyCollection<LeavePolicyLeaveType> leaveTypes,
        IReadOnlyCollection<LeavePolicyBlackoutPeriod> blackoutPeriods,
        IReadOnlyCollection<LeavePolicyLegalEntity> legalEntityAssignments,
        IReadOnlyCollection<Guid> legalEntityIdsToReplace,
        CancellationToken ct = default)
    {
        try
        {
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;

            var activeAssignmentsToReplace = legalEntityIdsToReplace.Count == 0
                ? []
                : await _db.LeavePolicyLegalEntities
                    .Where(x => x.TenantId == policy.TenantId
                        && x.IsActive
                        && legalEntityIdsToReplace.Contains(x.LegalEntityId))
                    .ToListAsync(ct);

            foreach (var assignment in activeAssignmentsToReplace)
                assignment.IsActive = false;

            if (activeAssignmentsToReplace.Count > 0)
                await _db.SaveChangesAsync(ct);

            await _db.LeavePolicies.AddAsync(policy, ct);
            await _db.LeavePolicyLeaveTypes.AddRangeAsync(leaveTypes, ct);
            await _db.LeavePolicyBlackoutPeriods.AddRangeAsync(blackoutPeriods, ct);
            await _db.LeavePolicyLegalEntities.AddRangeAsync(legalEntityAssignments, ct);
            await _db.SaveChangesAsync(ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintConflictException(ex);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, LeavePolicyAggregate>> ListActiveAggregatesByLegalEntityIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        int year,
        CancellationToken ct = default)
    {
        if (legalEntityIds.Count == 0)
            return new Dictionary<Guid, LeavePolicyAggregate>();

        var yearEnd = new DateOnly(year, 12, 31);
        var assignments = await _db.LeavePolicyLegalEntities
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.IsActive
                && legalEntityIds.Contains(x.LegalEntityId)
                && x.EffectiveDate <= yearEnd)
            .ToListAsync(ct);

        var policyIds = assignments.Select(a => a.LeavePolicyId).Distinct().ToArray();
        if (policyIds.Length == 0)
            return new Dictionary<Guid, LeavePolicyAggregate>();

        var policies = await _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                && p.IsActive
                && policyIds.Contains(p.Id)
                && p.EffectiveFrom <= yearEnd)
            .ToListAsync(ct);

        var aggregates = await BuildAggregatesAsync(tenantId, policies, ct);
        var aggregateById = aggregates.ToDictionary(a => a.Policy.Id);

        return assignments
            .Where(a => aggregateById.ContainsKey(a.LeavePolicyId))
            .GroupBy(a => a.LegalEntityId)
            .ToDictionary(
                group => group.Key,
                group => aggregateById[group.OrderByDescending(a => a.EffectiveDate).First().LeavePolicyId]);
    }

    private async Task<IReadOnlyList<LeavePolicyAggregate>> BuildAggregatesAsync(
        Guid tenantId, IReadOnlyList<LeavePolicy> policies, CancellationToken ct)
    {
        if (policies.Count == 0)
            return [];

        var policyIds = policies.Select(p => p.Id).ToArray();

        var typeRules = await (
            from rule in _db.LeavePolicyLeaveTypes.AsNoTracking()
            join leaveType in _db.LeaveTypes.AsNoTracking() on rule.LeaveTypeId equals leaveType.Id
            where rule.TenantId == tenantId && policyIds.Contains(rule.LeavePolicyId)
            orderby leaveType.Name
            select new
            {
                rule.LeavePolicyId,
                Item = new LeavePolicyLeaveTypeWithType(rule, leaveType.Name, leaveType.Code)
            })
            .ToListAsync(ct);

        var blackoutPeriods = await _db.LeavePolicyBlackoutPeriods.AsNoTracking()
            .Where(p => p.TenantId == tenantId && policyIds.Contains(p.LeavePolicyId))
            .OrderBy(p => p.StartDate)
            .ToListAsync(ct);

        var legalEntities = await (
            from assignment in _db.LeavePolicyLegalEntities.AsNoTracking()
            join legalEntity in _db.LegalEntities.AsNoTracking() on assignment.LegalEntityId equals legalEntity.Id
            where assignment.TenantId == tenantId && policyIds.Contains(assignment.LeavePolicyId)
            orderby legalEntity.Name
            select new
            {
                assignment.LeavePolicyId,
                Item = new LeavePolicyLegalEntityWithName(assignment, legalEntity.Name, legalEntity.StandardWorkingDays)
            })
            .ToListAsync(ct);

        return policies.Select(policy => new LeavePolicyAggregate(
            policy,
            typeRules.Where(x => x.LeavePolicyId == policy.Id).Select(x => x.Item).ToList(),
            blackoutPeriods.Where(x => x.LeavePolicyId == policy.Id).ToList(),
            legalEntities.Where(x => x.LeavePolicyId == policy.Id).Select(x => x.Item).ToList()))
            .ToList();
    }
}
