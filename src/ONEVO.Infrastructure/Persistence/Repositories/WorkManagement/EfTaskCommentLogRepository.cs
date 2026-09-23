using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentLogRepository : ITaskCommentLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCommentLog log, CancellationToken ct = default)
        => await _db.TaskCommentLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskCommentLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskCommentLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.TaskId == taskId)
            .OrderBy(log => log.OccurredAt)
            .ToListAsync(ct);
}
