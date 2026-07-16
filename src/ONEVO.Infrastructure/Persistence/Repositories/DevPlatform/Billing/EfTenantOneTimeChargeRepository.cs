using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;

public sealed class EfTenantOneTimeChargeRepository : ITenantOneTimeChargeRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantOneTimeChargeRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TenantOneTimeCharge>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.TenantOneTimeCharges
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<TenantOneTimeCharge?> GetByIdAsync(Guid chargeId, CancellationToken ct = default) =>
        await _db.TenantOneTimeCharges.FirstOrDefaultAsync(c => c.Id == chargeId, ct);

    public async Task AddAsync(TenantOneTimeCharge charge, CancellationToken ct = default) =>
        await _db.TenantOneTimeCharges.AddAsync(charge, ct);

    public async Task<bool> HasActiveChargeAsync(Guid tenantId, string setupOptionKey, CancellationToken ct = default) =>
        await _db.TenantOneTimeCharges
            .AnyAsync(c => c.TenantId == tenantId
                        && c.SetupOptionKey == setupOptionKey
                        && c.Status != "void", ct);
}
