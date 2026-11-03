using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

public sealed class EfMeetingActionItemRepository : IMeetingActionItemRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingActionItemRepository(ApplicationDbContext db) => _db = db;

    public Task<bool> ExistsForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => _db.MeetingActionItems.AsNoTracking()
            .AnyAsync(a => a.TenantId == tenantId && a.TaskId == taskId, ct);

    public void Add(MeetingActionItem row) => _db.MeetingActionItems.Add(row);

    public async Task<IReadOnlyList<MeetingActionItem>> ListForOccurrenceAsync(
        Guid tenantId, Guid occurrenceTaskId, CancellationToken ct = default)
        => await _db.MeetingActionItems.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<MeetingActionItem?> GetTrackedAsync(
        Guid tenantId, Guid occurrenceTaskId, Guid taskId, CancellationToken ct = default)
        => _db.MeetingActionItems
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.OccurrenceTaskId == occurrenceTaskId && a.TaskId == taskId, ct);

    public void Remove(MeetingActionItem row) => _db.MeetingActionItems.Remove(row);
}
