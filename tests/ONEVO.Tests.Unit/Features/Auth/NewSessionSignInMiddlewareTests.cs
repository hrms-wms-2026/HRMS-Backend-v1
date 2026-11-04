using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ONEVO.Api.Middleware;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class NewSessionSignInMiddlewareTests
{
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    private readonly ITicketStore _store = Substitute.For<ITicketStore>();
    private readonly CookieAuthenticationOptions _options;
    private readonly IOptionsMonitor<CookieAuthenticationOptions> _monitor;

    public NewSessionSignInMiddlewareTests()
    {
        _options = new CookieAuthenticationOptions
        {
            SessionStore = _store,
            CookieManager = new ChunkingCookieManager(),
            TicketDataFormat = new TicketDataFormat(
                new EphemeralDataProtectionProvider().CreateProtector("test")),
        };
        _options.Cookie.Name = "admin_session";
        _monitor = Substitute.For<IOptionsMonitor<CookieAuthenticationOptions>>();
        _monitor.Get("AdminScheme").Returns(_options);
    }

    private string ProtectedCookieFor(string sessionKey) =>
        _options.TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(SessionIdClaim, sessionKey) }, "AdminScheme")),
            "AdminScheme"));

    private DefaultHttpContext MakeContext(string cookieHeader, bool marked)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = cookieHeader;
        var metadata = marked
            ? new EndpointMetadataCollection(new IssuesNewSessionAttribute("AdminScheme"))
            : EndpointMetadataCollection.Empty;
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test endpoint"));
        ctx.RequestServices = new ServiceCollection().AddSingleton(_monitor).BuildServiceProvider();
        return ctx;
    }

    [Fact]
    public async Task MarkedEndpoint_HidesSessionCookieFromHandler_KeepsOtherCookies()
    {
        var ctx = MakeContext(
            $"admin_csrf=old-token; admin_session={ProtectedCookieFor("old-key")}; theme=dark", marked: true);
        string? seenByNext = null;

        await new NewSessionSignInMiddleware(c => { seenByNext = c.Request.Headers.Cookie.ToString(); return Task.CompletedTask; })
            .InvokeAsync(ctx, _monitor);

        Assert.Equal("admin_csrf=old-token; theme=dark", seenByNext);
        Assert.False(ctx.Request.Cookies.ContainsKey("admin_session"));
    }

    [Fact]
    public async Task MarkedEndpoint_RevokeSupersededSession_RemovesOldSessionFromStore()
    {
        var ctx = MakeContext($"admin_session={ProtectedCookieFor("old-key")}", marked: true);
        await new NewSessionSignInMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx, _monitor);

        await NewSessionSignInMiddleware.RevokeSupersededSessionAsync(ctx, "AdminScheme");

        await _store.Received(1).RemoveAsync("old-key", ctx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkedEndpoint_WithoutSessionCookie_RevokesNothing()
    {
        var ctx = MakeContext("theme=dark", marked: true);
        await new NewSessionSignInMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx, _monitor);

        await NewSessionSignInMiddleware.RevokeSupersededSessionAsync(ctx, "AdminScheme");

        await _store.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default, default);
        Assert.Equal("theme=dark", ctx.Request.Headers.Cookie.ToString());
    }

    [Fact]
    public async Task UnmarkedEndpoint_LeavesSessionCookie_AndSignInGuardThrows()
    {
        var cookie = $"admin_session={ProtectedCookieFor("old-key")}";
        var ctx = MakeContext(cookie, marked: false);
        await new NewSessionSignInMiddleware(_ => Task.CompletedTask).InvokeAsync(ctx, _monitor);

        Assert.Equal(cookie, ctx.Request.Headers.Cookie.ToString());
        // Signing in here would make the cookie handler reuse the old session key.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewSessionSignInMiddleware.RevokeSupersededSessionAsync(ctx, "AdminScheme"));
    }
}
