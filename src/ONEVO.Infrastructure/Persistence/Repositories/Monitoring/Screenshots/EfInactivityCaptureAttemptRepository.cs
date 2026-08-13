using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots;

public sealed class EfInactivityCaptureAttemptRepository : IInactivityCaptureAttemptRepository
{
    private readonly ApplicationDbContext _db;

    public EfInactivityCaptureAttemptRepository(ApplicationDbContext db) => _db = db;

    public Task<InactivityCaptureAttempt?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct)
        => _db.InactivityCaptureAttempts
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);

    public Task AddAsync(InactivityCaptureAttempt attempt, CancellationToken ct)
        => _db.InactivityCaptureAttempts.AddAsync(attempt, ct).AsTask();

    public async Task<IReadOnlyList<InactivityCaptureAttempt>> GetByEmployeeRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
        => await _db.InactivityCaptureAttempts
            .Where(a => a.TenantId == tenantId
                        && a.EmployeeId == employeeId
                        && a.PromptedAt >= fromUtc
                        && a.PromptedAt < toUtc)
            .OrderBy(a => a.PromptedAt)
            .ToListAsync(ct);

    public async Task<Guid?> FindContainingWorkSessionAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset instantUtc,
        CancellationToken ct)
    {
        var sessionId = await _db.EmployeeWorkSessions
            .Where(s => s.TenantId == tenantId
                        && s.UserId == employeeId
                        && s.ClockInAt <= instantUtc
                        && s.ClockOutAt > instantUtc)
            .OrderByDescending(s => s.ClockInAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        return sessionId;
    }
}
