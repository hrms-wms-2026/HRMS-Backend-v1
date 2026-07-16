using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserSessionRepository
{
    Task<IReadOnlyList<PlatformUserSession>> ListByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<PlatformUserSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<PlatformUserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(PlatformUserSession session, CancellationToken ct = default);
    Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task RevokeByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAllByUserIdAsync(Guid userId, CancellationToken ct = default);
}
