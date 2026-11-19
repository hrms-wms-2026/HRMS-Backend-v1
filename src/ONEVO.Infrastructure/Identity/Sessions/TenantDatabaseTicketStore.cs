using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using CoreHrEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Identity.Sessions;

/// <summary>
/// ITicketStore backing TenantScheme's AddCookie. Persists only a SHA-256 hash of the random ticket
/// key (never the raw key) in sessions.key_hash; the raw key is what AddCookie encrypts inside the
/// crownus_session cookie. Registered as a singleton (CookieAuthenticationOptions requirement), so it
/// resolves scoped DB/permission dependencies per call via IServiceScopeFactory instead of holding
/// them directly.
/// </summary>
public sealed class TenantDatabaseTicketStore : ITicketStore
{
    public const string TenantIdItemKey = "tenant_id";
    public const string CsrfTokenHashItemKey = "csrf_token_hash";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ISessionTicketStoreCache _cache;
    private readonly ILogger<TenantDatabaseTicketStore> _logger;

    public TenantDatabaseTicketStore(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        ISessionTicketStoreCache cache,
        ILogger<TenantDatabaseTicketStore> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _cache = cache;
        _logger = logger;
    }

    // Classic overloads (forward to the HttpContext-aware ones)

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, httpContext: null, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, httpContext: null, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, httpContext: null, CancellationToken.None);

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, httpContext: null, CancellationToken.None);

    // HttpContext + CancellationToken overloads (real logic lives here)

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        var tenantId = Guid.Parse(ticket.Properties.Items[TenantIdItemKey]!);

        // This scope's DbContext/TenantContextAccessor is brand new (this class is a DI singleton,
        // so _scopeFactory.CreateScope() never inherits the request scope's already-resolved tenant
        // context) and defaults to System mode, so without this lookup+switch the sessions insert
        // below would run with app.current_tenant_id unset and be rejected by the tenant_isolation
        // RLS policy regardless of which host resolved the login.
        //
        // Deliberately a denylist (Suspended/Cancelled), not an Active/Trial allowlist: StoreAsync is
        // a shared infrastructure chokepoint reached by invite-accept and password-reset flows too,
        // not just login, and those legitimately create a session while the tenant is still
        // Provisioning (before admin confirmation flips it to Trial). The stricter Active/Trial
        // business rule for login specifically already lives upstream in
        // LoginContinuationService.ContinueAsync. This is only a backstop against writing a session
        // for a tenant that is definitely dead.
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null || tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
            throw new InvalidOperationException(
                "Cannot store a session for an unresolved or inactive tenant.");

        await SwitchToTenantAsync(tenantSwitcher, tenant, cancellationToken);

        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var csrfTokenHash = ticket.Properties.Items.TryGetValue(CsrfTokenHashItemKey, out var csrfVal) ? csrfVal : string.Empty;

        var employees = scope.ServiceProvider.GetRequiredService<CoreHrEmployeeRepository>();
        var defaultEmployee = await employees.GetDefaultForUserAsync(tenantId, userId, cancellationToken);

        var rawKey = GenerateRawKey();
        var now = _clock.UtcNow;
        var expiresAt = ticket.Properties.ExpiresUtc ?? now.Add(SessionPolicy.SlidingWindow);

        var session = new Session
        {
            Id = Guid.NewGuid(),
            KeyHash = HashKey(rawKey),
            UserId = userId,
            TenantId = tenantId,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = httpContext?.Request.Headers.UserAgent.ToString() ?? string.Empty,
            StartedAt = now,
            LastActivityAt = now,
            ExpiresAt = expiresAt,
            CsrfTokenHash = csrfTokenHash ?? string.Empty,
            ActiveEmployeeId = defaultEmployee?.Id
        };

        await sessions.AddAsync(session, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return rawKey;
    }

    public async Task RenewAsync(
        string key, AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        // Old sessions are renewed on almost every request (IssuedUtc stays at StartedAt), so the
        // ExpiresAt/LastActivityAt write is throttled per session. Skipping it costs at most one
        // interval of sliding expiry on a 30-day window. The cached entry still slides, so its
        // expiry re-check and session_expires_at claim follow the cookie, not the stale value.
        var keyHash = HashKey(key);
        if (!_cache.ShouldWriteRenewal(keyHash))
        {
            if (_cache.TryGet<CachedTenantSession>(keyHash, out var throttled, out var throttledBuiltAt) && throttled is not null)
                SlideCachedExpiry(keyHash, throttled, throttledBuiltAt,
                    ticket.Properties.ExpiresUtc ?? _clock.UtcNow.Add(SessionPolicy.SlidingWindow));
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        // Unlike RetrieveAsync/RemoveAsync, the ticket passed here already carries the tenant id
        // (RetrieveAsync always sets it on the ticket it returns), so this scope can switch tenant
        // context up front, the same shape as the StoreAsync fix, without needing the
        // session_key_lookup RLS policy at all.
        if (!ticket.Properties.Items.TryGetValue(TenantIdItemKey, out var tenantIdValue) ||
            !Guid.TryParse(tenantIdValue, out var tenantId))
            return;

        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
            return;

        await SwitchToTenantAsync(tenantSwitcher, tenant, cancellationToken);

        var session = await sessions.GetByKeyHashAsync(keyHash, cancellationToken);
        if (session is null || session.IsRevoked)
            return;

        var now = _clock.UtcNow;
        var absoluteCap = session.StartedAt.Add(SessionPolicy.AbsoluteLifetime);
        if (now >= absoluteCap)
            return;

        var newExpiry = ticket.Properties.ExpiresUtc ?? now.Add(SessionPolicy.SlidingWindow);
        if (newExpiry > absoluteCap)
            newExpiry = absoluteCap;

        // Taken before the save: the EF eviction interceptor drops the entry when the renewal is
        // saved, and it is put back below with the new expiry and its original build time, so an
        // eviction that raced this renewal still wins.
        var hasCached = _cache.TryGet<CachedTenantSession>(keyHash, out var cached, out var cachedBuiltAt);

        session.ExpiresAt = newExpiry;
        session.LastActivityAt = now;
        await uow.SaveChangesAsync(cancellationToken);
        _cache.RecordRenewalWrite(keyHash);

        if (hasCached && cached is not null)
            SlideCachedExpiry(keyHash, cached, cachedBuiltAt, newExpiry);
        else
            _cache.DropSession(keyHash);

        ReissueCsrfCookie(httpContext, "crownus_csrf", newExpiry);
    }

    private void SlideCachedExpiry(
        string keyHash, CachedTenantSession cached, DateTimeOffset builtAt, DateTimeOffset newExpiry)
    {
        var absoluteCap = cached.StartedAt.Add(SessionPolicy.AbsoluteLifetime);
        if (newExpiry > absoluteCap)
            newExpiry = absoluteCap;
        if (newExpiry <= cached.ExpiresAt)
            return;

        _cache.Set(keyHash, cached with { ExpiresAt = newExpiry }, cached.UserId, cached.TenantId, builtAt);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        // PERF-01 instrumentation: this runs on every authenticated request and does several
        // sequential DB round trips (session, tenant, user, employee, permission resolution).
        // Logged at Debug so it costs nothing unless an operator turns Debug logging on for this
        // category to measure real p95 before deciding whether further optimization is worth it.
        var stopwatch = _logger.IsEnabled(LogLevel.Debug) ? Stopwatch.StartNew() : null;
        try
        {
            return await RetrieveCoreAsync(key, cancellationToken);
        }
        finally
        {
            if (stopwatch is not null)
            {
                stopwatch.Stop();
                _logger.LogDebug("TenantDatabaseTicketStore.RetrieveAsync took {ElapsedMs}ms.", stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }

    private async Task<AuthenticationTicket?> RetrieveCoreAsync(string key, CancellationToken cancellationToken)
    {
        // PERF-01 / improvement task 03: a page fires many requests per second with the same
        // cookie, so the built ticket data is reused for a short window. Expiry and the absolute
        // lifetime are always re-checked against the cached values; revocation, password, role,
        // permission and legal-entity changes evict the entry (ISessionTicketCache, fed by
        // SessionTicketCacheInvalidationInterceptor, the session repositories and
        // PermissionVersionService).
        var keyHash = HashKey(key);
        if (_cache.TryGet<CachedTenantSession>(keyHash, out var cached) && cached is not null)
        {
            if (IsLive(cached.ExpiresAt, cached.StartedAt))
                return BuildTicket(cached);

            _cache.DropSession(keyHash);
            return null;
        }

        var buildStartedAt = _cache.BeginBuild();
        var snapshot = await LoadAsync(keyHash, cancellationToken);
        if (snapshot is null)
            return null;

        _cache.Set(keyHash, snapshot, snapshot.UserId, snapshot.TenantId, buildStartedAt);
        return BuildTicket(snapshot);
    }

    private bool IsLive(DateTimeOffset expiresAt, DateTimeOffset startedAt)
    {
        var now = _clock.UtcNow;
        return expiresAt > now && now < startedAt.Add(SessionPolicy.AbsoluteLifetime);
    }

    private async Task<CachedTenantSession?> LoadAsync(string keyHash, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var permissionResolver = scope.ServiceProvider.GetRequiredService<IPermissionResolver>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var employees = scope.ServiceProvider.GetRequiredService<CoreHrEmployeeRepository>();

        // This scope has no tenant id yet - only the cookie's session key - so the only row this
        // scope is allowed to see before switching context is the one satisfying the
        // session_key_lookup RLS policy (matches on this session's own key_hash).
        var session = await sessions.GetByKeyHashForTenantResolutionAsync(keyHash, cancellationToken);
        if (session is null || session.IsRevoked || !IsLive(session.ExpiresAt, session.StartedAt))
            return null;

        var tenant = await tenants.GetByIdAsync(session.TenantId, cancellationToken);
        if (tenant is null)
            return null;

        await SwitchToTenantAsync(tenantSwitcher, tenant, cancellationToken);

        var user = await users.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return null;

        // PERF-01: only the LegalEntityId column is needed here - GetLegalEntityIdAsync is a
        // projection-only query, not a full Employee entity fetch, on every authenticated request.
        Guid? activeLegalEntityId = session.ActiveEmployeeId is Guid activeEmployeeId
            ? await employees.GetLegalEntityIdAsync(session.TenantId, activeEmployeeId, cancellationToken)
            : null;

        var permissions = await permissionResolver.ResolveAsync(
            session.UserId, session.TenantId, activeLegalEntityId, cancellationToken);

        return new CachedTenantSession(
            session.Id,
            session.UserId,
            session.TenantId,
            user.Email,
            session.CsrfTokenHash ?? string.Empty,
            session.StartedAt,
            session.ExpiresAt,
            activeLegalEntityId,
            [.. permissions]);
    }

    private static AuthenticationTicket BuildTicket(CachedTenantSession session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new("tenant_id", session.TenantId.ToString()),
            new(ClaimTypes.Email, session.Email),
            new("csrf_token_hash", session.CsrfTokenHash),
            new("session_expires_at", session.ExpiresAt.ToString("O")),
            new("session_id", session.SessionId.ToString()),
        };
        if (session.LegalEntityId is Guid legalEntityId)
            claims.Add(new Claim("legal_entity_id", legalEntityId.ToString()));
        claims.AddRange(session.Permissions.Select(p => new Claim("permission", p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantScheme"));
        var properties = new AuthenticationProperties
        {
            IssuedUtc = session.StartedAt,
            ExpiresUtc = session.ExpiresAt,
        };
        properties.Items[TenantIdItemKey] = session.TenantId.ToString();
        properties.Items[CsrfTokenHashItemKey] = session.CsrfTokenHash;

        return new AuthenticationTicket(principal, properties, "TenantScheme");
    }

    /// <summary>Immutable ticket data kept in the cache. A new ticket is built from it on every
    /// request, because the cookie handler mutates the ticket it is given.</summary>
    private sealed record CachedTenantSession(
        Guid SessionId,
        Guid UserId,
        Guid TenantId,
        string Email,
        string CsrfTokenHash,
        DateTimeOffset StartedAt,
        DateTimeOffset ExpiresAt,
        Guid? LegalEntityId,
        string[] Permissions);

    public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        var keyHash = HashKey(key);
        _cache.EvictSession(keyHash);
        try
        {
            await RevokeAsync(keyHash, cancellationToken);
        }
        finally
        {
            // Again once the revoke is committed, so a request that rebuilt the ticket while the
            // revoke was in flight cannot leave it cached.
            _cache.EvictSession(keyHash);
        }
    }

    private async Task RevokeAsync(string keyHash, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        // Same bootstrap problem as RetrieveAsync: only a session key is available here, so the
        // session_key_lookup RLS policy is what lets this scope find the row at all. Revoking it
        // (an UPDATE) still requires the correct tenant context - tenant_isolation, not
        // session_key_lookup, governs UPDATE/DELETE.
        var session = await sessions.GetByKeyHashForTenantResolutionAsync(keyHash, cancellationToken);
        if (session is null)
            return;

        var tenant = await tenants.GetByIdAsync(session.TenantId, cancellationToken);
        if (tenant is null)
            return;

        await SwitchToTenantAsync(tenantSwitcher, tenant, cancellationToken);

        session.IsRevoked = true;
        await uow.SaveChangesAsync(cancellationToken);
    }

    private static Task SwitchToTenantAsync(
        ITenantContextSwitcher tenantSwitcher, Tenant tenant, CancellationToken cancellationToken) =>
        tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), cancellationToken);

    private static void ReissueCsrfCookie(HttpContext? httpContext, string cookieName, DateTimeOffset expiresAt)
    {
        if (httpContext is null)
            return;
        if (!httpContext.Request.Cookies.TryGetValue(cookieName, out var csrfValue) || string.IsNullOrEmpty(csrfValue))
            return;

        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        httpContext.Response.Cookies.Append(cookieName, csrfValue, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt
        });
    }

    private static string GenerateRawKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>SHA-256 hex of the raw cookie key: the value stored in sessions.key_hash.</summary>
    public static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
