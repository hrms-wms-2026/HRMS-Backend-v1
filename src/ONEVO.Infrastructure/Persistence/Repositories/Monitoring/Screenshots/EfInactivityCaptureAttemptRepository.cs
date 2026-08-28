using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Screenshots;

public class EfInactivityCaptureAttemptRepository : IInactivityCaptureAttemptRepository
{
    private readonly ApplicationDbContext _db;

    public EfInactivityCaptureAttemptRepository(ApplicationDbContext db) => _db = db;

    public void Add(InactivityCaptureAttempt attempt)
        => _db.InactivityCaptureAttempts.Add(attempt);

    public Task<InactivityCaptureAttempt?> GetByIdAsync(Guid tenantId, Guid attemptId, CancellationToken ct)
        => _db.InactivityCaptureAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == attemptId, ct);
}
