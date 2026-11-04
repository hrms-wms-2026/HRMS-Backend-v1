using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ONEVO.Api.Middleware;

/// <summary>
/// Guarantees that a sign-in always creates a NEW server-side session.
///
/// ASP.NET Core's cookie handler, when a request already carries a session cookie that resolves to
/// a live session, does not create a new session on SignInAsync: it calls
/// ITicketStore.RenewAsync on the OLD session key and re-issues the cookie for that old key. Our
/// ticket stores' RenewAsync only extends expiry, so the new login's csrf_token_hash was dropped
/// while the response still set a NEW onevo_csrf/admin_csrf cookie: the browser ended up with a
/// CSRF cookie that could never match its session, and every mutation - including logout - failed
/// with a CSRF 403 until the cookies were cleared by hand. (When the old session had just been
/// signed out, it instead "renewed" a revoked row, leaving the user logged out right after login.)
///
/// On endpoints marked <see cref="IssuesNewSessionAttribute"/> this middleware, which must run
/// before UseAuthentication, records the incoming session key and removes the session cookie from
/// the request, so the handler never sees it and StoreAsync creates a fresh session. The sign-in
/// helpers then revoke the superseded session via <see cref="RevokeSupersededSessionAsync"/>.
/// </summary>
public sealed class NewSessionSignInMiddleware
{
    // CookieAuthenticationHandler's (private) SessionIdClaim: the claim inside the protected cookie
    // ticket that carries the ITicketStore key when a SessionStore is configured.
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";
    private const string SupersededKeyItemPrefix = "onevo.superseded_session_key:";

    private readonly RequestDelegate _next;

    public NewSessionSignInMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<CookieAuthenticationOptions> optionsMonitor)
    {
        var marker = context.GetEndpoint()?.Metadata.GetMetadata<IssuesNewSessionAttribute>();
        if (marker is not null)
        {
            var options = optionsMonitor.Get(marker.Scheme);
            var cookieName = options.Cookie.Name!;
            var protectedTicket = options.CookieManager.GetRequestCookie(context, cookieName);
            if (!string.IsNullOrEmpty(protectedTicket))
            {
                var sessionKey = options.TicketDataFormat.Unprotect(protectedTicket)?
                    .Principal.FindFirst(SessionIdClaim)?.Value;
                if (!string.IsNullOrEmpty(sessionKey))
                    context.Items[SupersededKeyItemPrefix + marker.Scheme] = sessionKey;

                RemoveRequestCookie(context, cookieName);
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Revokes the session the caller held before this sign-in (if any). Call right before
    /// HttpContext.SignInAsync on an <see cref="IssuesNewSessionAttribute"/> endpoint.
    /// </summary>
    public static async Task RevokeSupersededSessionAsync(HttpContext context, string scheme)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(scheme);

        // A session cookie still on the request here means the endpoint lacks
        // [IssuesNewSession]: the cookie handler would reuse the old session key (see summary).
        if (!string.IsNullOrEmpty(options.CookieManager.GetRequestCookie(context, options.Cookie.Name!)))
            throw new InvalidOperationException(
                $"Endpoint '{context.GetEndpoint()?.DisplayName}' signs in to {scheme} but is not marked " +
                $"[{nameof(IssuesNewSessionAttribute)}(\"{scheme}\")].");

        if (context.Items.TryGetValue(SupersededKeyItemPrefix + scheme, out var value)
            && value is string sessionKey
            && options.SessionStore is not null)
        {
            await options.SessionStore.RemoveAsync(sessionKey, context, context.RequestAborted);
        }
    }

    private static void RemoveRequestCookie(HttpContext context, string cookieName)
    {
        // Rewrites the raw Cookie header (rather than re-serializing Request.Cookies) so every other
        // cookie value reaches the endpoint byte-for-byte. Also drops ChunkingCookieManager chunks
        // ("{name}C1", "{name}C2", ...) in case the session cookie was ever chunked.
        var kept = context.Request.Headers.Cookie
            .SelectMany(header => (header ?? string.Empty).Split(';'))
            .Select(pair => pair.Trim())
            .Where(pair => pair.Length > 0 && !IsSessionCookiePart(pair, cookieName))
            .ToArray();

        context.Request.Headers.Cookie = kept.Length == 0
            ? StringValues.Empty
            : new StringValues(string.Join("; ", kept));
    }

    private static bool IsSessionCookiePart(string pair, string cookieName)
    {
        var separator = pair.IndexOf('=');
        var name = (separator < 0 ? pair : pair[..separator]).Trim();
        if (name.Equals(cookieName, StringComparison.Ordinal))
            return true;

        return name.Length > cookieName.Length + 1
            && name.StartsWith(cookieName + "C", StringComparison.Ordinal)
            && name[(cookieName.Length + 1)..].All(char.IsAsciiDigit);
    }
}
