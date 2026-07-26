using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;

public class EfTenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantRepository(ApplicationDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(
            t => t.Slug == slug && t.Status != TenantStatus.Cancelled, ct);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct = default)
    {
        var query = _db.Tenants.Where(t => t.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<Tenant>> ListAsync(
        TenantStatus? statusFilter,
        string? searchTerm,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = ApplyFilters(statusFilter, searchTerm);
        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(
        TenantStatus? statusFilter,
        string? searchTerm,
        CancellationToken ct = default) =>
        ApplyFilters(statusFilter, searchTerm).CountAsync(ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default) =>
        await _db.Tenants.AddAsync(tenant, ct);

    private IQueryable<Tenant> ApplyFilters(TenantStatus? status, string? search)
    {
        var query = _db.Tenants.AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, pattern) ||
                EF.Functions.ILike(t.Slug, pattern));
        }

        return query;
    }
}

