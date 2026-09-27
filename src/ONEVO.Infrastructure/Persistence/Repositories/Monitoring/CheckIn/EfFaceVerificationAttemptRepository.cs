using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.CheckIn;

public class EfFaceVerificationAttemptRepository : IFaceVerificationAttemptRepository
{
    private readonly ApplicationDbContext _db;

    public EfFaceVerificationAttemptRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(FaceVerificationAttempt attempt, CancellationToken ct)
        => await _db.FaceVerificationAttempts.AddAsync(attempt, ct);

    public async Task<IReadOnlyList<FaceVerificationAttempt>> GetConsecutiveFailuresAsync(
        Guid tenantId, Guid employeeId, string purpose, DateTimeOffset sinceUtc, CancellationToken ct)
    {
        var recent = await _db.FaceVerificationAttempts
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.EmployeeId == employeeId
                && a.Purpose == purpose
                && a.CreatedAt >= sinceUtc)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return recent
            .TakeWhile(a => a.Outcome == FaceVerificationAttempt.OutcomeFailed)
            .ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
