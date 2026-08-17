using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

public interface ISessionRepository
{
    Task<Session?> GetLatestActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<Session?> GetByKeyHashAsync(string keyHash, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> ListActiveByTenantIdAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// The only lookup permitted to find a session before its tenant is known: satisfies the
    /// session_key_lookup RLS policy (SELECT-only, matches on key_hash) rather than tenant_isolation.
    /// Callers must call ITenantContextSwitcher.SwitchToTenantAsync with the returned session's
    /// TenantId before doing anything else with it or with other tenant-scoped repositories.
    /// </summary>
    Task<Session?> GetByKeyHashForTenantResolutionAsync(string keyHash, CancellationToken ct = default);

    Task AddAsync(Session session, CancellationToken ct = default);
    Task RevokeByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task RevokeByKeyHashAsync(string keyHash, CancellationToken ct = default);
}
