using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentReactionRepository : ITaskCommentReactionRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentReactionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCommentReaction reaction, CancellationToken ct = default)
        => await _db.TaskCommentReactions.AddAsync(reaction, ct);

    public Task RemoveAsync(TaskCommentReaction reaction, CancellationToken ct = default)
    {
        _db.TaskCommentReactions.Remove(reaction);
        return Task.CompletedTask;
    }

    public async Task<TaskCommentReaction?> GetAsync(Guid tenantId, Guid commentId, Guid employeeId, string emoji, CancellationToken ct = default)
        => await _db.TaskCommentReactions.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.CommentId == commentId && r.EmployeeId == employeeId && r.Emoji == emoji, ct);

    public async Task<IReadOnlyList<TaskCommentReaction>> GetForCommentIdsAsync(Guid tenantId, IReadOnlyList<Guid> commentIds, CancellationToken ct = default)
        => await _db.TaskCommentReactions.AsNoTracking()
            .Where(r => r.TenantId == tenantId && commentIds.Contains(r.CommentId))
            .ToListAsync(ct);
}
