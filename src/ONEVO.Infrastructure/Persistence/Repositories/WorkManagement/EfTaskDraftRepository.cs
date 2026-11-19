using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public sealed class EfTaskDraftRepository(ApplicationDbContext db) : ITaskDraftRepository
{
    public async Task AddAsync(TaskDraft draft, CancellationToken ct = default) => await db.TaskDrafts.AddAsync(draft, ct);

    public Task<TaskDraft?> GetOwnedAsync(Guid tenantId, Guid ownerUserId, Guid draftId, CancellationToken ct = default) =>
        db.TaskDrafts.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.OwnerUserId == ownerUserId && d.Id == draftId, ct);

    public async Task<IReadOnlyList<TaskDraft>> ListOwnedAsync(Guid tenantId, Guid ownerUserId, CancellationToken ct = default) =>
        await db.TaskDrafts.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.OwnerUserId == ownerUserId)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt).ThenBy(d => d.Id)
            .ToListAsync(ct);

    public void Remove(TaskDraft draft) => db.TaskDrafts.Remove(draft);
}
