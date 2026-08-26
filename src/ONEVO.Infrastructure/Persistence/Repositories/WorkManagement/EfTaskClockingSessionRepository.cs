using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskClockingSessionRepository : ITaskClockingSessionRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskClockingSessionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskClockingSession session, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AddAsync(session, ct);

    public async Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .FirstOrDefaultAsync(session => session.TenantId == tenantId && session.TaskId == taskId && session.ClockOutAt == null, ct);

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetOpenSessionsForTasksAsync(
        Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && taskIds.Contains(session.TaskId) && session.ClockOutAt == null)
            .ToDictionaryAsync(session => session.TaskId, session => session.EmployeeId, ct);

    public async Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskClockingSessions
            .FirstOrDefaultAsync(session => session.TenantId == tenantId && session.Id == id, ct);

    public async Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.TaskId == taskId)
            .OrderBy(session => session.ClockInAt)
            .ToListAsync(ct);

    public void Update(TaskClockingSession session) => _db.TaskClockingSessions.Update(session);
}
