using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Identity.Sessions;

/// <summary>
/// ITicketStore backing AdminScheme's AddCookie, persisting sessions in the canonical
/// platform_user_sessions table. Only the SHA-256 hash of the opaque session key is stored.
/// On every retrieve the principal is rebuilt from the database: platform user (status check)
/// -> platform_user_roles -> platform_role_permissions -> permission claims. Revoked or
/// expired sessions never authenticate.
///
/// Lifetime is <see cref="AdminSessionPolicy"/> (security audit F-03): a session ends 30 minutes after its
/// last renewal and 12 hours after sign-in, whatever it is used for. The ticket's IssuedUtc is the last
/// activity, so the cookie handler's sliding refresh (now + ExpiresUtc - IssuedUtc) stays the idle window,
/// and every renewal sets the expiry itself rather than trusting the ticket.
/// </summary>
public sealed class AdminDatabaseTicketStore : ITicketStore
{
    public const string CsrfTokenHashItemKey = "csrf_token_hash";
    public const string PlatformRoleItemKey = "platform_role";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ISessionTicketStoreCache _cache;

    public AdminDatabaseTicketStore(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock, ISessionTicketStoreCache cache)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _cache = cache;
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, httpContext: null, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, httpContext: null, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, httpContext: null, CancellationToken.None);

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, httpContext: null, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var platformUserId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var csrfTokenHash = ticket.Properties.Items.TryGetValue(CsrfTokenHashItemKey, out var csrfVal)
            ? (csrfVal ?? string.Empty)
            : string.Empty;

        var rawKey = GenerateRawKey();
        var now = _clock.UtcNow;
        var expiresAt = Earliest(ticket.Properties.ExpiresUtc ?? DateTimeOffset.MaxValue, now.Add(AdminSessionPolicy.IdleTimeout));

        var session = new PlatformUserSession
        {
            Id = Guid.NewGuid(),
            AccountId = platformUserId,
            TokenHash = HashKey(rawKey),
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString(), 500),
            CreatedAt = now,
            LastActivityAt = now,
            ExpiresAt = expiresAt,
            CsrfTokenHash = csrfTokenHash
        };

        await sessions.AddAsync(session, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return rawKey;
    }

    public async Task RenewAsync(
        string key, AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        // Throttled per session, and the cached entry slides with the cookie, as in
        // TenantDatabaseTicketStore.RenewAsync.
        var keyHash = HashKey(key);
        if (!_cache.ShouldWriteRenewal(keyHash))
        {
            if (_cache.TryGet<CachedAdminSession>(keyHash, out var throttled, out var throttledBuiltAt) && throttled is not null)
                SlideCachedExpiry(keyHash, throttled, throttledBuiltAt, _clock.UtcNow);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var session = await sessions.GetByTokenHashAsync(keyHash, cancellationToken);
        if (session is null || session.RevokedAt is not null)
            return;

        var now = _clock.UtcNow;
        if (!IsLive(session.ExpiresAt, session.CreatedAt, session.LastActivityAt))
            return;

        // The ticket's expiry is ignored: the handler derives it from IssuedUtc, so it is not the idle window.
        var newExpiry = SlidingExpiry(session.CreatedAt, now);

        // Taken before the save, which makes the EF eviction interceptor drop the entry; see
        // TenantDatabaseTicketStore.RenewAsync.
        var hasCached = _cache.TryGet<CachedAdminSession>(keyHash, out var cached, out var cachedBuiltAt);

        session.ExpiresAt = newExpiry;
        session.LastActivityAt = now;
        await uow.SaveChangesAsync(cancellationToken);
        _cache.RecordRenewalWrite(keyHash);

        if (hasCached && cached is not null)
            SlideCachedExpiry(keyHash, cached, cachedBuiltAt, now);
        else
            _cache.DropSession(keyHash);

        ReissueCsrfCookie(httpContext, "admin_csrf", newExpiry);
    }

    private void SlideCachedExpiry(
        string keyHash, CachedAdminSession cached, DateTimeOffset builtAt, DateTimeOffset now)
    {
        // The renewal decides the expiry (it may be shorter than a stored pre-F-03 expiry); only activity
        // must never move backwards, or the idle window would count from a stale time.
        if (now <= cached.LastActivityAt)
            return;

        _cache.Set(keyHash, cached with { ExpiresAt = SlidingExpiry(cached.CreatedAt, now), LastActivityAt = now },
            cached.UserId, SessionTicketCacheScopes.Platform, builtAt);
    }

    /// <summary>now + the idle timeout, never past 12 hours from sign-in.</summary>
    private static DateTimeOffset SlidingExpiry(DateTimeOffset createdAt, DateTimeOffset now) =>
        Earliest(now.Add(AdminSessionPolicy.IdleTimeout), createdAt.Add(AdminSessionPolicy.AbsoluteLifetime));

    private static DateTimeOffset Earliest(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>Activity cannot predate the session; a missing value counts from sign-in.</summary>
    private static DateTimeOffset LastActive(DateTimeOffset createdAt, DateTimeOffset lastActivityAt) =>
        lastActivityAt > createdAt ? lastActivityAt : createdAt;

    /// <summary>When the session really ends: its stored expiry, the idle window and the absolute cap.</summary>
    private static DateTimeOffset EffectiveExpiry(
        DateTimeOffset expiresAt, DateTimeOffset createdAt, DateTimeOffset lastActivityAt) =>
        Earliest(Earliest(expiresAt, LastActive(createdAt, lastActivityAt).Add(AdminSessionPolicy.IdleTimeout)),
            createdAt.Add(AdminSessionPolicy.AbsoluteLifetime));

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        // Improvement task 03: same short-lived cache as TenantDatabaseTicketStore. Expiry is
        // re-checked on every hit; revocation and platform user/role/permission changes evict.
        var keyHash = HashKey(key);
        if (_cache.TryGet<CachedAdminSession>(keyHash, out var cached) && cached is not null)
        {
            if (IsLive(cached.ExpiresAt, cached.CreatedAt, cached.LastActivityAt))
                return BuildTicket(cached);

            _cache.DropSession(keyHash);
            return null;
        }

        var buildStartedAt = _cache.BeginBuild();
        var snapshot = await LoadAsync(keyHash, cancellationToken);
        if (snapshot is null)
            return null;

        _cache.Set(keyHash, snapshot, snapshot.UserId, SessionTicketCacheScopes.Platform, buildStartedAt);
        return BuildTicket(snapshot);
    }

    private bool IsLive(DateTimeOffset expiresAt, DateTimeOffset createdAt, DateTimeOffset lastActivityAt) =>
        _clock.UtcNow < EffectiveExpiry(expiresAt, createdAt, lastActivityAt);

    private async Task<CachedAdminSession?> LoadAsync(string keyHash, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<IPlatformPermissionResolver>();

        var session = await sessions.GetByTokenHashAsync(keyHash, cancellationToken);
        if (session is null || session.RevokedAt is not null
            || !IsLive(session.ExpiresAt, session.CreatedAt, session.LastActivityAt))
            return null;

        var profile = await resolver.ResolveActiveUserAsync(session.AccountId, cancellationToken);
        if (profile is null)
            return null;

        return new CachedAdminSession(
            profile.UserId,
            profile.Email,
            profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty,
            session.CsrfTokenHash ?? string.Empty,
            profile.MfaEnrolled,
            session.CreatedAt,
            session.LastActivityAt,
            session.ExpiresAt,
            [.. profile.PermissionCodes]);
    }

    private static AuthenticationTicket BuildTicket(CachedAdminSession session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new(ClaimTypes.Email, session.Email),
            new("platform_role", session.RoleName),
            new("csrf_token_hash", session.CsrfTokenHash),
            // Live enrollment state (F-02). AdminPolicy requires "true"; a not-yet-enrolled admin's
            // session is limited to the MFA enrollment endpoints until confirm-setup succeeds.
            new(PlatformMfaClaims.EnrolledClaimType, session.MfaEnrolled ? PlatformMfaClaims.True : PlatformMfaClaims.False),
        };

        foreach (var permissionCode in session.PermissionCodes)
        {
            claims.Add(new Claim(PlatformPermissionCatalog.PermissionClaimType, permissionCode));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
        // IssuedUtc is the last activity, not sign-in: the handler's sliding refresh then stays the idle window.
        var properties = new AuthenticationProperties
        {
            IssuedUtc = LastActive(session.CreatedAt, session.LastActivityAt),
            ExpiresUtc = EffectiveExpiry(session.ExpiresAt, session.CreatedAt, session.LastActivityAt),
        };
        properties.Items[PlatformRoleItemKey] = session.RoleName;
        properties.Items[CsrfTokenHashItemKey] = session.CsrfTokenHash;

        return new AuthenticationTicket(principal, properties, "AdminScheme");
    }

    /// <summary>Immutable ticket data kept in the cache; a fresh ticket is built from it per request.</summary>
    private sealed record CachedAdminSession(
        Guid UserId,
        string Email,
        string RoleName,
        string CsrfTokenHash,
        bool MfaEnrolled,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        DateTimeOffset ExpiresAt,
        string[] PermissionCodes);

    public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var keyHash = HashKey(key);
        _cache.EvictSession(keyHash);
        try
        {
            await RevokeAsync(keyHash, httpContext, cancellationToken);
        }
        finally
        {
            _cache.EvictSession(keyHash);
        }
    }

    private async Task RevokeAsync(string keyHash, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var authEvents = scope.ServiceProvider.GetRequiredService<IPlatformAuthEventRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var session = await sessions.GetByTokenHashAsync(keyHash, cancellationToken);
        if (session is null)
            return;

        var alreadyRevoked = session.RevokedAt is not null;
        await sessions.RevokeByTokenHashAsync(keyHash, cancellationToken);

        if (!alreadyRevoked)
        {
            await authEvents.AddAsync(new PlatformAuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = session.AccountId,
                EventType = PlatformAuthEvent.SessionRevoked,
                SourceIp = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                MetadataJson = $"{{\"session_id\":\"{session.Id}\"}}",
                CreatedAt = _clock.UtcNow
            }, cancellationToken);
        }

        await uow.SaveChangesAsync(cancellationToken);
    }

    private static void ReissueCsrfCookie(HttpContext? httpContext, string cookieName, DateTimeOffset expiresAt)
    {
        if (httpContext is null)
            return;
        if (!httpContext.Request.Cookies.TryGetValue(cookieName, out var csrfValue) || string.IsNullOrEmpty(csrfValue))
            return;

        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var adminCookieDomain = configuration["AdminCookieDomain"];

        httpContext.Response.Cookies.Append(cookieName, csrfValue, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Domain = string.IsNullOrWhiteSpace(adminCookieDomain) ? null : adminCookieDomain,
            Expires = expiresAt
        });
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string GenerateRawKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>SHA-256 hex of the raw cookie key: the value stored in platform_user_sessions.token_hash.</summary>
    public static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
