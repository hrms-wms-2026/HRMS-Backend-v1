using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskPercentageLogRepository : ITaskPercentageLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskPercentageLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskPercentageLog log, CancellationToken ct = default)
        => await _db.TaskPercentageLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskPercentageLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskPercentageLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.TaskId == taskId)
            .OrderBy(log => log.ChangedAt)
            .ToListAsync(ct);

    public async Task<TaskPercentageLog?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskPercentageLogs
            .FirstOrDefaultAsync(log => log.TenantId == tenantId && log.Id == id, ct);

    public void Update(TaskPercentageLog log) => _db.TaskPercentageLogs.Update(log);
}
