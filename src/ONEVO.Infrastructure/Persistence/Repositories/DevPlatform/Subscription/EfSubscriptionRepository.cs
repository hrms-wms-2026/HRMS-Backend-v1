using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Subscription;

public sealed class EfSubscriptionRepository
    : ISubscriptionPlanRepository, ITenantSubscriptionRepository
{
    private readonly ApplicationDbContext _db;

    public EfSubscriptionRepository(ApplicationDbContext db) => _db = db;

    public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
        _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, ct);

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAsync(CancellationToken ct = default) =>
        await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default) =>
        _db.SubscriptionPlans.AnyAsync(p => p.Code == code, ct);

    public async Task AddAsync(SubscriptionPlan plan, CancellationToken ct = default) =>
        await _db.SubscriptionPlans.AddAsync(plan, ct);

    public async Task AddAsync(TenantSubscription subscription, CancellationToken ct = default) =>
        await _db.TenantSubscriptions.AddAsync(subscription, ct);

    public Task<TenantSubscription?> GetByGatewaySubscriptionRefAsync(string gatewayRef, CancellationToken ct = default) =>
        _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.GatewaySubscriptionRef == gatewayRef, ct);

    public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
}
