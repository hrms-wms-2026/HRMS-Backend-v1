using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Identity;

/// <summary>
/// ITicketStore backing TenantScheme's AddCookie. Persists only a SHA-256 hash of the random ticket
/// key (never the raw key) in sessions.key_hash; the raw key is what AddCookie encrypts inside the
/// onevo_session cookie. Registered as a singleton (CookieAuthenticationOptions requirement), so it
/// resolves scoped DB/permission dependencies per call via IServiceScopeFactory instead of holding
/// them directly.
/// </summary>
public sealed class TenantDatabaseTicketStore : ITicketStore
{
    public const string TenantIdItemKey = "tenant_id";
    public const string CsrfTokenHashItemKey = "csrf_token_hash";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;

    public TenantDatabaseTicketStore(IServiceScopeFactory scopeFactory, IDateTimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    // ── Classic overloads (forward to the HttpContext-aware ones) ──────────

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, httpContext: null, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, httpContext: null, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, httpContext: null, CancellationToken.None);

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, httpContext: null, CancellationToken.None);

    // ── HttpContext + CancellationToken overloads (real logic lives here) ──

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var tenantId = Guid.Parse(ticket.Properties.Items[TenantIdItemKey]!);
        var csrfTokenHash = ticket.Properties.Items.TryGetValue(CsrfTokenHashItemKey, out var csrfVal) ? csrfVal : string.Empty;

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
            CsrfTokenHash = csrfTokenHash ?? string.Empty
        };

        await sessions.AddAsync(session, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return rawKey;
    }

    public async Task RenewAsync(
        string key, AuthenticationTicket ticket, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var session = await sessions.GetByKeyHashAsync(HashKey(key), cancellationToken);
        if (session is null || session.IsRevoked)
            return;

        var now = _clock.UtcNow;
        var absoluteCap = session.StartedAt.Add(SessionPolicy.AbsoluteLifetime);
        if (now >= absoluteCap)
            return;

        var newExpiry = ticket.Properties.ExpiresUtc ?? now.Add(SessionPolicy.SlidingWindow);
        if (newExpiry > absoluteCap)
            newExpiry = absoluteCap;

        session.ExpiresAt = newExpiry;
        session.LastActivityAt = now;
        await uow.SaveChangesAsync(cancellationToken);

        ReissueCsrfCookie(httpContext, "onevo_csrf", newExpiry);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var permissionResolver = scope.ServiceProvider.GetRequiredService<IPermissionResolver>();

        var session = await sessions.GetByKeyHashAsync(HashKey(key), cancellationToken);
        if (session is null || session.IsRevoked || session.ExpiresAt <= _clock.UtcNow)
            return null;

        if (_clock.UtcNow >= session.StartedAt.Add(SessionPolicy.AbsoluteLifetime))
            return null;

        var user = await users.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return null;

        var permissions = await permissionResolver.ResolveAsync(session.UserId, session.TenantId, cancellationToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new("tenant_id", session.TenantId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("csrf_token_hash", session.CsrfTokenHash ?? string.Empty),
            new("session_expires_at", session.ExpiresAt.ToString("O")),
        };
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

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

    public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await sessions.RevokeByKeyHashAsync(HashKey(key), cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
    }

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

    private static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
