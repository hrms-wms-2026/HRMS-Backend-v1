using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.WorkSessions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.WorkSessions.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.WorkSessions;

public class EfWorkSessionRepository : IWorkSessionRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkSessionRepository(ApplicationDbContext db) => _db = db;

    public async Task<EmployeeWorkSession?> FindByIdAsync(Guid id, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeWorkSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public async Task AddAsync(EmployeeWorkSession session, CancellationToken ct)
        => await _db.EmployeeWorkSessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<EmployeeWorkSession>> GetForUserAndDateAsync(
        Guid tenantId, Guid userId, DateOnly date, CancellationToken ct)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        return await _db.EmployeeWorkSessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.UserId == userId
                        && s.ClockInAt >= start
                        && s.ClockInAt < end)
            .OrderBy(s => s.ClockInAt)
            .ToListAsync(ct);
    }
}
