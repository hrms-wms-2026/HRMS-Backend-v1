using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;

public interface IFaceVerificationAttemptRepository
{
    Task AddAsync(FaceVerificationAttempt attempt, CancellationToken ct);

    /// <summary>
    /// Failed attempts since the employee's last passed or overridden attempt for this purpose,
    /// counting only attempts made at or after <paramref name="sinceUtc"/>.
    /// </summary>
    Task<IReadOnlyList<FaceVerificationAttempt>> GetConsecutiveFailuresAsync(
        Guid tenantId, Guid employeeId, string purpose, DateTimeOffset sinceUtc, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
