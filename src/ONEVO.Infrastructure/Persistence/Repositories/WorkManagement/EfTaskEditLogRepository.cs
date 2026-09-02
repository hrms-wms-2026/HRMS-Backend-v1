using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskEditLogRepository : ITaskEditLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskEditLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskEditLog log, CancellationToken ct = default)
        => await _db.TaskEditLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskEditLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskEditLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.TaskId == taskId)
            .OrderBy(log => log.ChangedAt)
            .ToListAsync(ct);
}
