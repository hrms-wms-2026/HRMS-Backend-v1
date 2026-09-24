using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskStatusChangeLogRepository : ITaskStatusChangeLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskStatusChangeLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskStatusChangeLog log, CancellationToken ct = default)
        => await _db.TaskStatusChangeLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskStatusChangeLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskStatusChangeLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.TaskId == taskId)
            .OrderBy(log => log.ChangedAt)
            .ToListAsync(ct);
}
