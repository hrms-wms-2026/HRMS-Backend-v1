namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Eviction side of the short-lived cache the cookie ticket stores keep in front of their
/// per-request session lookups (PERF-01 / improvement task 03). Call it whenever something that
/// feeds an authentication ticket changes: a session is revoked, a password changes, a role or
/// permission changes, a user is deactivated, or the active legal entity switches.
/// <para>
/// Every eviction also blocks re-caching for a short grace window, so a request that read the old
/// rows just before the change committed can never put them back into the cache.
/// </para>
/// Per process today; task 21 moves it to a shared cache.
/// </summary>
public interface ISessionTicketCache
{
    /// <summary>One session, by the SHA-256 hash of its cookie key (sessions.key_hash or
    /// platform_user_sessions.token_hash).</summary>
    void EvictSession(string keyHash);

    /// <summary>Every cached session of one tenant user or platform user.</summary>
    void EvictUser(Guid userId);

    /// <summary>Every cached session in one tenant (role permissions, subscription, tenant row),
    /// or every platform admin session when given <see cref="SessionTicketCacheScopes.Platform"/>.</summary>
    void EvictTenant(Guid tenantId);

    /// <summary>Every cached session.</summary>
    void EvictAll();
}

public static class SessionTicketCacheScopes
{
    /// <summary>Scope id used for platform (admin) sessions, which have no tenant.</summary>
    public static readonly Guid Platform = new("00000000-0000-0000-0000-00000000ad31");
}
