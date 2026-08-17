using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Identifier == identifier, ct);
    }

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        await _db.Projects.AddAsync(project, ct);
    }

    public async Task<Project?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);
    }

    public async Task<Project?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        // Deliberately no AsNoTracking - see interface doc. Callers must mutate and then call
        // SaveChanges without an explicit Update(), so only actually-changed columns are written.
        return await _db.Projects
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);
    }

    public void Update(Project project)
    {
        _db.Projects.Update(project);
    }

    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> ListForMemberAsync(
        Guid tenantId, Guid targetUserId, int skip, int take, string? sortBy, string sortDirection,
        CancellationToken ct = default)
    {
        var baseQuery = (
            from pm in _db.ProjectMembers.AsNoTracking()
            join p in _db.Projects.AsNoTracking() on pm.ProjectId equals p.Id
            where pm.TenantId == tenantId && pm.UserId == targetUserId && pm.IsActive && p.IsActive
            select p
        ).Distinct();

        var total = await baseQuery.CountAsync(ct);

        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        var ordered = (normalizedSortBy, descending) switch
        {
            ("name", true) => baseQuery.OrderByDescending(p => p.Name),
            ("name", false) => baseQuery.OrderBy(p => p.Name),
            ("startdate", true) => baseQuery.OrderByDescending(p => p.StartDate),
            ("startdate", false) => baseQuery.OrderBy(p => p.StartDate),
            ("targetdate", true) => baseQuery.OrderByDescending(p => p.TargetDate),
            ("targetdate", false) => baseQuery.OrderBy(p => p.TargetDate),
            (_, true) => baseQuery.OrderByDescending(p => p.CreatedAt),
            _ => baseQuery.OrderBy(p => p.CreatedAt)
        };

        var items = await ordered.Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }
}
