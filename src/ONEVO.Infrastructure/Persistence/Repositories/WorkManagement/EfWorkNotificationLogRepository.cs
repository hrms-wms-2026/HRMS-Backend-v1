using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkNotificationLogRepository : IWorkNotificationLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkNotificationLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkNotificationLog log, CancellationToken ct = default)
        => await _db.WorkNotificationLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<WorkNotificationLog>> ListForRecipientAsync(
        Guid tenantId, Guid projectId, Guid recipientEmployeeId, int skip, int take, CancellationToken ct = default)
        => await _db.WorkNotificationLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ProjectId == projectId && l.RecipientEmployeeId == recipientEmployeeId)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
}
