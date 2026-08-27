using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

public class EfEmployeeHierarchyClosureRepository : IEmployeeHierarchyClosureRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public EfEmployeeHierarchyClosureRepository(ApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Guid>> GetDirectReportEmployeeIdsAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default)
    {
        return await _db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.AncestorEmployeeId == managerEmployeeId && c.Depth == 1)
            .Select(c => c.DescendantEmployeeId)
            .ToListAsync(ct);
    }

    public async Task<Guid?> GetDirectManagerEmployeeIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DescendantEmployeeId == employeeId && c.Depth == 1)
            .Select(c => (Guid?)c.AncestorEmployeeId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
        Guid tenantId,
        Guid managerEmployeeId,
        CancellationToken ct = default)
    {
        return await _db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.AncestorEmployeeId == managerEmployeeId && c.Depth > 0)
            .Select(c => c.DescendantEmployeeId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ancestorEmployeeIds, CancellationToken ct = default)
    {
        if (ancestorEmployeeIds.Count == 0)
            return Array.Empty<Guid>();

        return await _db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && ancestorEmployeeIds.Contains(c.AncestorEmployeeId))
            .Select(c => c.DescendantEmployeeId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetAncestorChainEmployeeIdsAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DescendantEmployeeId == employeeId)
            .OrderBy(c => c.Depth)
            .Select(c => c.AncestorEmployeeId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<Guid>>();

        var rows = await _db.EmployeeHierarchyClosures.AsNoTracking()
            .Where(c => c.TenantId == tenantId && employeeIds.Contains(c.DescendantEmployeeId))
            .OrderBy(c => c.Depth)
            .Select(c => new { c.DescendantEmployeeId, c.AncestorEmployeeId })
            .ToListAsync(ct);

        // GroupBy over a list already ordered by Depth preserves each group's element order
        // (LINQ-to-Objects GroupBy is stable), so nearest-manager-first survives the grouping.
        return rows.GroupBy(r => r.DescendantEmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.AncestorEmployeeId).ToList());
    }

    /// <summary>
    /// Full-tenant rebuild: walks positions.reports_to_position_id from every position that
    /// currently holds an active PrimaryEmployment assignment. Delete-then-reinsert in one
    /// transaction is safe because this table is documented as not source of truth.
    /// </summary>
    public async Task RebuildAsync(Guid tenantId, CancellationToken ct = default)
    {
        var activeAssignments = await _db.PositionAssignments
            .AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active)
            .ToListAsync(ct);

        var holdersByPositionId = activeAssignments
            .GroupBy(pa => pa.PositionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var positions = await _db.Positions
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.Id, ct);

        var newRows = new List<ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure>();
        var now = _clock.UtcNow;

        foreach (var assignment in activeAssignments)
        {
            var depth = 1;
            positions.TryGetValue(assignment.PositionId, out var ownPosition);
            var currentPositionId = ownPosition?.ReportsToPositionId;
            var currentReportsToEmployeeId = assignment.ReportsToEmployeeId;
            var visited = new HashSet<Guid> { assignment.PositionId };

            while (currentPositionId is not null
                && visited.Add(currentPositionId.Value)
                && holdersByPositionId.TryGetValue(currentPositionId.Value, out var holders))
            {
                ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment? ancestorAssignment = holders.Count switch
                {
                    1 => holders[0],
                    _ => currentReportsToEmployeeId is { } overrideId
                        ? holders.FirstOrDefault(h => h.EmployeeId == overrideId)
                        : null,
                };

                if (ancestorAssignment is null)
                    break;

                newRows.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
                {
                    TenantId = tenantId,
                    AncestorEmployeeId = ancestorAssignment.EmployeeId,
                    DescendantEmployeeId = assignment.EmployeeId,
                    Depth = depth,
                    SourcePositionAssignmentId = assignment.Id,
                    GeneratedAt = now,
                });

                depth++;
                currentReportsToEmployeeId = ancestorAssignment.ReportsToEmployeeId;
                currentPositionId = positions.TryGetValue(currentPositionId.Value, out var ancestorPosition)
                    ? ancestorPosition.ReportsToPositionId
                    : null;
            }
        }

        var existing = await _db.EmployeeHierarchyClosures
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        _db.EmployeeHierarchyClosures.RemoveRange(existing);
        await _db.EmployeeHierarchyClosures.AddRangeAsync(newRows, ct);
        await _db.SaveChangesAsync(ct);
    }
}
