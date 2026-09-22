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

    // IgnoreQueryFilters() bypasses the soft-delete half of the composed query filter on
    // purpose: a soft-deleted top-level comment with surviving replies must still be returned
    // (as a tombstone) - filtering deleted rows out is this method's documented contract to leave
    // to the caller, not something the global filter should silently do first. Tenant scoping is
    // preserved manually via the TenantId equality below.
    public async Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskComments.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
}
