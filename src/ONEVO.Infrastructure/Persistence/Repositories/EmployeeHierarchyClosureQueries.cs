using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

/// <summary>
/// The one query for a manager's direct reports, shared by the two repositories that need it.
/// <para>
/// The reporting-line closure is a raw organizational coverage table, and WorkManagement may never
/// reference <c>IEmployeeHierarchyClosureRepository</c> (My Team spec section 10). The meetings bulk
/// invite preview therefore reads the closure through the common
/// <c>ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository</c>. Both repositories run the
/// query defined here, so the rule is written once.
/// </para>
/// </summary>
internal static class EmployeeHierarchyClosureQueries
{
    /// <summary>The employees whose reporting line points straight at the manager (closure depth 1).</summary>
    public static async Task<IReadOnlyList<Guid>> ListDirectReportEmployeeIdsAsync(
        ApplicationDbContext db, Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default)
    {
        return await db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.AncestorEmployeeId == managerEmployeeId && c.Depth == 1)
            .Select(c => c.DescendantEmployeeId)
            .ToListAsync(ct);
    }

    /// <summary>The direct reports of every given manager in one query, one id per reporting line.</summary>
    public static async Task<IReadOnlyList<Guid>> ListDirectReportEmployeeIdsAsync(
        ApplicationDbContext db, Guid tenantId, IReadOnlyCollection<Guid> managerEmployeeIds, CancellationToken ct = default)
    {
        if (managerEmployeeIds.Count == 0)
            return Array.Empty<Guid>();
        return await db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && managerEmployeeIds.Contains(c.AncestorEmployeeId) && c.Depth == 1)
            .Select(c => c.DescendantEmployeeId)
            .ToListAsync(ct);
    }

    /// <summary>The employee's upward reporting chain, nearest manager first (closure depth ascending).</summary>
    public static async Task<IReadOnlyList<Guid>> ListAncestorChainEmployeeIdsAsync(
        ApplicationDbContext db, Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await db.EmployeeHierarchyClosures
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DescendantEmployeeId == employeeId)
            .OrderBy(c => c.Depth)
            .Select(c => c.AncestorEmployeeId)
            .ToListAsync(ct);
    }
}
