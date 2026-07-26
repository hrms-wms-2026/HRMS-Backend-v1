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
/// expired sessions (idle or absolute) never authenticate.
/// </summary>
public sealed class AdminDatabaseTicketStore : ITicketStore
{
    public const string CsrfTokenHashItemKey = "csrf_token_hash";
    public const string PlatformRoleItemKey = "platform_role";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;

    public AdminDatabaseTicketStore(IServiceScopeFactory scopeFactory, IDateTimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
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
        var expiresAt = ticket.Properties.ExpiresUtc ?? now.Add(SessionPolicy.SlidingWindow);

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
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var session = await sessions.GetByTokenHashAsync(HashKey(key), cancellationToken);
        if (session is null || session.RevokedAt is not null)
            return;

        var now = _clock.UtcNow;
        var absoluteCap = session.CreatedAt.Add(SessionPolicy.AbsoluteLifetime);
        if (now >= absoluteCap)
            return;

        var newExpiry = ticket.Properties.ExpiresUtc ?? now.Add(SessionPolicy.SlidingWindow);
        if (newExpiry > absoluteCap)
            newExpiry = absoluteCap;

        session.ExpiresAt = newExpiry;
        session.LastActivityAt = now;
        await uow.SaveChangesAsync(cancellationToken);

        ReissueCsrfCookie(httpContext, "admin_csrf", newExpiry);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<IPlatformPermissionResolver>();

        var session = await sessions.GetByTokenHashAsync(HashKey(key), cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= _clock.UtcNow)
            return null;

        if (_clock.UtcNow >= session.CreatedAt.Add(SessionPolicy.AbsoluteLifetime))
            return null;

        var profile = await resolver.ResolveActiveUserAsync(session.AccountId, cancellationToken);
        if (profile is null)
            return null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, profile.UserId.ToString()),
            new(ClaimTypes.Email, profile.Email),
            new("platform_role", profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty),
            new("csrf_token_hash", session.CsrfTokenHash ?? string.Empty),
        };

        foreach (var permissionCode in profile.PermissionCodes)
        {
            claims.Add(new Claim(PlatformPermissionCatalog.PermissionClaimType, permissionCode));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AdminScheme"));
        var properties = new AuthenticationProperties
        {
            IssuedUtc = session.CreatedAt,
            ExpiresUtc = session.ExpiresAt,
        };
        properties.Items[PlatformRoleItemKey] = profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty;
        properties.Items[CsrfTokenHashItemKey] = session.CsrfTokenHash;

        return new AuthenticationTicket(principal, properties, "AdminScheme");
    }

    public async Task RemoveAsync(string key, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IPlatformUserSessionRepository>();
        var authEvents = scope.ServiceProvider.GetRequiredService<IPlatformAuthEventRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var session = await sessions.GetByTokenHashAsync(HashKey(key), cancellationToken);
        if (session is null)
            return;

        var alreadyRevoked = session.RevokedAt is not null;
        await sessions.RevokeByTokenHashAsync(HashKey(key), cancellationToken);

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

        httpContext.Response.Cookies.Append(cookieName, csrfValue, new CookieOptions
        {
            HttpOnly = false,
            Secure = !env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
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

    private static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
