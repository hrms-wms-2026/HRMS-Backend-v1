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

    public async Task<IReadOnlyList<EmployeeWorkSession>> GetByEmployeeRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
        => await _db.EmployeeWorkSessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.UserId == employeeId
                        && s.ClockInAt >= fromUtc
                        && s.ClockInAt < toUtc)
            .OrderBy(s => s.ClockInAt)
            .ToListAsync(ct);
}
