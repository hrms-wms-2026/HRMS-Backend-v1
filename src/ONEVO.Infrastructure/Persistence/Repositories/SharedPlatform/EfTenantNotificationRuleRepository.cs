using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public sealed class EfTenantNotificationRuleRepository : ITenantNotificationRuleRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantNotificationRuleRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TenantNotificationRule>> ListAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.TenantNotificationRules.AsNoTracking().Where(r => r.TenantId == tenantId).ToListAsync(ct);

    public Task<TenantNotificationRule?> GetTrackedAsync(Guid tenantId, string eventKey, CancellationToken ct = default)
        => _db.TenantNotificationRules.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EventKey == eventKey, ct);

    public Task<TenantNotificationRule?> GetAsync(Guid tenantId, string eventKey, CancellationToken ct = default)
        => _db.TenantNotificationRules.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EventKey == eventKey, ct);

    public async Task AddAsync(TenantNotificationRule rule, CancellationToken ct = default)
        => await _db.TenantNotificationRules.AddAsync(rule, ct);

    public void Remove(TenantNotificationRule rule) => _db.TenantNotificationRules.Remove(rule);
}
