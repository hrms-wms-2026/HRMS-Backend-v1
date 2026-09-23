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

    public async Task<IReadOnlyDictionary<Guid, OpenTaskClockingSessionSummary>> GetOpenSessionsForTasksAsync(
        Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && taskIds.Contains(session.TaskId) && session.ClockOutAt == null)
            .ToDictionaryAsync(
                session => session.TaskId,
                session => new OpenTaskClockingSessionSummary(session.EmployeeId, session.ClockInAt),
                ct);

    public async Task<IReadOnlyList<OpenEmployeeTaskSession>> GetOpenSessionsForEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.EmployeeId == employeeId && session.ClockOutAt == null)
            .Join(_db.WorkTasks.AsNoTracking(),
                session => session.TaskId,
                task => task.Id,
                (session, task) => new OpenEmployeeTaskSession(task.Id, task.Title))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> GetTotalClosedSessionMinutesForTasksAsync(
        Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
    {
        var totals = await _db.TaskClockingSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && taskIds.Contains(session.TaskId)
                && session.ClockOutAt != null && session.DurationMinutes != null)
            .GroupBy(session => session.TaskId)
            .Select(group => new { TaskId = group.Key, TotalMinutes = group.Sum(session => session.DurationMinutes!.Value) })
            .ToListAsync(ct);

        return totals.ToDictionary(total => total.TaskId, total => total.TotalMinutes);
    }

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
