using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface ISessionRepository
{
    Task<Session?> GetLatestActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<Session?> GetByKeyHashAsync(string keyHash, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task RevokeByKeyHashAsync(string keyHash, CancellationToken ct = default);
}
