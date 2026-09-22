using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentRepository : ITaskCommentRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskComment comment, CancellationToken ct = default)
        => await _db.TaskComments.AddAsync(comment, ct);

    public async Task<TaskComment?> GetByIdForTenantAsync(Guid tenantId, Guid commentId, CancellationToken ct = default)
        => await _db.TaskComments.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == commentId, ct);

    public async Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskComments.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
}
