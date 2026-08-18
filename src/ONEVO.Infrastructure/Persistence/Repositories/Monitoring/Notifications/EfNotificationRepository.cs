using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications;

public class EfNotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public EfNotificationRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct)
        => await _db.Notifications.AddAsync(notification, ct);

    public async Task<bool> ExistsRecentAsync(
        Guid tenantId, Guid employeeId, NotificationType type, DateTimeOffset sinceUtc, CancellationToken ct) =>
        await _db.Notifications.AsNoTracking().AnyAsync(
            n => n.TenantId == tenantId && n.EmployeeId == employeeId && n.Type == type && n.CreatedAt >= sinceUtc, ct);

    public async Task<IReadOnlyList<Notification>> GetPendingForTrayAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct) =>
        await _db.Notifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId
                        && n.DeliveredToTrayAt == null
                        && (n.Type == NotificationType.BreakReminder || n.Type == NotificationType.LongIdleAlert))
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<Notification?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        await _db.Notifications.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id, ct);

    public async Task<IReadOnlyList<Notification>> GetInboxAsync(
        Guid tenantId, Guid employeeId, int page, int pageSize, CancellationToken ct) =>
        await _db.Notifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<int> GetInboxTotalCountAsync(Guid tenantId, Guid employeeId, CancellationToken ct) =>
        await _db.Notifications.AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId && n.EmployeeId == employeeId, ct);

    public void Update(Notification notification) => _db.Notifications.Update(notification);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
