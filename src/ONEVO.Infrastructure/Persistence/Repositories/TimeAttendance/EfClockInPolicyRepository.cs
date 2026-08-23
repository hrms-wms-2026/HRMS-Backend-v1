using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;

public class EfClockInPolicyRepository : IClockInPolicyRepository
{
    private readonly ApplicationDbContext _db;

    public EfClockInPolicyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ClockInPolicy>> ListByLegalEntityAsync(
        Guid tenantId,
        Guid legalEntityId,
        bool includeInactive,
        CancellationToken ct = default)
    {
        var query = _db.ClockInPolicies
            .AsNoTracking()
            .Include(p => p.LateDeductionRules.OrderBy(r => r.LateArrivalMinute))
            .Where(p => p.TenantId == tenantId && p.LegalEntityId == legalEntityId);

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        return await query
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<ClockInPolicy?> GetByIdAsync(
        Guid tenantId, Guid policyId, CancellationToken ct = default)
    {
        return await _db.ClockInPolicies
            .AsNoTracking()
            .Include(p => p.LateDeductionRules.OrderBy(r => r.LateArrivalMinute))
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == policyId, ct);
    }

    public async Task<ClockInPolicy?> GetByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid policyId, CancellationToken ct = default)
    {
        return await _db.ClockInPolicies
            .AsNoTracking()
            .Include(p => p.LateDeductionRules.OrderBy(r => r.LateArrivalMinute))
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId
                    && p.LegalEntityId == legalEntityId
                    && p.Id == policyId,
                ct);
    }

    public async Task<ClockInPolicy?> GetTrackedByIdForLegalEntityAsync(
        Guid tenantId, Guid legalEntityId, Guid policyId, CancellationToken ct = default)
    {
        return await _db.ClockInPolicies
            .Include(p => p.LateDeductionRules)
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId
                    && p.LegalEntityId == legalEntityId
                    && p.Id == policyId,
                ct);
    }

    public async Task<bool> HasOverlappingActiveScopeAsync(
        Guid tenantId,
        Guid legalEntityId,
        string scopeType,
        Guid[]? departmentIds,
        Guid[]? positionIds,
        Guid[]? employeeIds,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? excludingPolicyId,
        CancellationToken ct = default)
    {
        // Conservative overlap: active policies of the same scope type with overlapping
        // effective dates. For full_company any second active overlapping policy conflicts.
        // For department/position/employee, conflict only when target ID sets intersect.
        // Product docs do not define precedence; see CLOCK_IN_POLICY_BACKEND_PART1_REPORT.md.
        var candidates = await _db.ClockInPolicies
            .AsNoTracking()
            .Where(p =>
                p.TenantId == tenantId
                && p.LegalEntityId == legalEntityId
                && p.IsActive
                && p.ScopeType == scopeType
                && (excludingPolicyId == null || p.Id != excludingPolicyId.Value)
                && p.EffectiveFrom <= (effectiveTo ?? DateOnly.MaxValue)
                && effectiveFrom <= (p.EffectiveTo ?? DateOnly.MaxValue))
            .Select(p => new
            {
                p.Id,
                p.ScopeType,
                p.DepartmentIds,
                p.PositionIds,
                p.EmployeeIds
            })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return false;

        if (scopeType == ClockInPolicy.ScopeFullCompany)
            return true;

        foreach (var candidate in candidates)
        {
            if (scopeType == ClockInPolicy.ScopeDepartment
                && SetsIntersect(departmentIds, candidate.DepartmentIds))
                return true;

            if (scopeType == ClockInPolicy.ScopePosition
                && SetsIntersect(positionIds, candidate.PositionIds))
                return true;

            if (scopeType == ClockInPolicy.ScopeEmployee
                && SetsIntersect(employeeIds, candidate.EmployeeIds))
                return true;
        }

        return false;
    }

    private static bool SetsIntersect(Guid[]? left, Guid[]? right)
    {
        if (left is null || right is null || left.Length == 0 || right.Length == 0)
            return false;

        var rightSet = right.ToHashSet();
        return left.Any(rightSet.Contains);
    }

    public async Task AddAsync(ClockInPolicy policy, CancellationToken ct = default)
    {
        await _db.ClockInPolicies.AddAsync(policy, ct);
    }

    public void Update(ClockInPolicy policy)
    {
        _db.ClockInPolicies.Update(policy);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _db.SaveChangesAsync(ct);
    }
}
