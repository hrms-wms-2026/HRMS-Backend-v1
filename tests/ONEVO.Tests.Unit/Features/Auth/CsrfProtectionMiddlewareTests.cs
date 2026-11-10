using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class CsrfProtectionMiddlewareTests
{
    private readonly ISecureTokenGenerator _tokenService = Substitute.For<ISecureTokenGenerator>();
    private bool _nextCalled;

    private CsrfProtectionMiddleware CreateSut()
    {
        _nextCalled = false;
        RequestDelegate next = _ => { _nextCalled = true; return Task.CompletedTask; };
        var logger = Substitute.For<ILogger<CsrfProtectionMiddleware>>();
        return new CsrfProtectionMiddleware(next, logger);
    }

    private static DefaultHttpContext MakeContext(string method, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;

        // Mirrors real UseAuthentication() behavior: unless a scheme is explicitly
        // authenticated via AuthenticateUser below, AuthenticateAsync(scheme) fails.
        var authService = Substitute.For<IAuthenticationService>();
        authService.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string>())
            .Returns(AuthenticateResult.NoResult());

        var services = new ServiceCollection();
        services.AddSingleton(authService);
        ctx.RequestServices = services.BuildServiceProvider();

        return ctx;
    }

    private static void AuthenticateUser(DefaultHttpContext ctx, string scheme, string csrfTokenHash)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("csrf_token_hash", csrfTokenHash)
        }, scheme));

        var authService = ctx.RequestServices.GetRequiredService<IAuthenticationService>();
        authService.AuthenticateAsync(ctx, scheme)
            .Returns(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
    }

    [Fact]
    public async Task SafeMethod_IsNeverBlocked()
    {
        var ctx = MakeContext("GET", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task UnsafeMethod_ExemptLoginPath_IsNotBlocked()
    {
        var ctx = MakeContext("POST", "/api/v1/auth/login");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task UnsafeMethod_ExemptSessionExchangePath_IsNotBlocked()
    {
        // session-exchange is the call that creates crownus_session; it must never be blocked by
        // CSRF validation, including when the browser carries a stale/invalid crownus_session cookie
        // from an unrelated prior session - the exchange code itself is what proves authorization.
        var ctx = MakeContext("POST", "/api/v1/auth/session-exchange");
        ctx.Request.Headers["Cookie"] = "crownus_session=stale-unrelated-session";

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task UnsafeMethod_NoSessionCookie_IsNotBlocked()
    {
        var ctx = MakeContext("POST", "/api/v1/employees");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task UnsafeMethod_MissingCsrfHeader_Rejects403()
    {
        var ctx = MakeContext("POST", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";
        ctx.Response.Body = new MemoryStream();

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.False(_nextCalled);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task CsrfFailure_ProblemTypeIsCrownusUrn()
    {
        // The web app recognises a CSRF failure by this exact type (error.interceptor.ts).
        var ctx = MakeContext("POST", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";
        ctx.Response.Body = new MemoryStream();

        await CreateSut().InvokeAsync(ctx, _tokenService);

        ctx.Response.Body.Position = 0;
        using var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Response.Body);
        Assert.Equal("urn:crownus:error:csrf", body.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("csrf", "urn:crownus:error:csrf")]
    [InlineData("forbidden", "urn:crownus:error:forbidden")]
    [InlineData("rate-limit", "urn:crownus:error:rate-limit")]
    public void ProblemTypes_For_BuildsCrownusUrn(string name, string expected)
    {
        Assert.Equal(expected, ONEVO.Api.Errors.ProblemTypes.For(name));
    }

    [Fact]
    public async Task UnsafeMethod_NotAuthenticated_Rejects403()
    {
        var ctx = MakeContext("POST", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";
        ctx.Request.Headers["X-CSRF-Token"] = "raw-token";
        ctx.Response.Body = new MemoryStream();

        // User is not authenticated in context

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.False(_nextCalled);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task UnsafeMethod_HeaderHashMismatch_Rejects403()
    {
        var ctx = MakeContext("POST", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";
        ctx.Request.Headers["X-CSRF-Token"] = "wrong-raw-token";
        AuthenticateUser(ctx, "TenantScheme", "aabbccdd");
        ctx.Response.Body = new MemoryStream();

        _tokenService.HashToken("wrong-raw-token").Returns("11223344");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.False(_nextCalled);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task UnsafeMethod_HeaderHashMatchesSession_Allows()
    {
        var ctx = MakeContext("POST", "/api/v1/employees");
        ctx.Request.Headers["Cookie"] = "crownus_session=sess-1";
        ctx.Request.Headers["X-CSRF-Token"] = "correct-raw-token";
        AuthenticateUser(ctx, "TenantScheme", "aabbccdd");

        _tokenService.HashToken("correct-raw-token").Returns("aabbccdd");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AdminUnsafeMethod_ExemptLoginPath_IsNotBlocked()
    {
        var ctx = MakeContext("POST", "/admin/v1/auth/login");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AdminUnsafeMethod_ExemptAcceptInvitePath_IsNotBlockedEvenWithStaleAdminSessionCookie()
    {
        // The invited person has never logged in, but a stale admin_session cookie from an
        // unrelated earlier session in the same browser must not cause this to be treated as
        // an authenticated request requiring CSRF validation - same bug class as the
        // forgot-password/reset-password gap fixed on 2026-08-06.
        var ctx = MakeContext("POST", "/admin/v1/auth/accept-invite");
        ctx.Request.Headers["Cookie"] = "admin_session=stale-unrelated-session";

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AdminUnsafeMethod_HeaderHashMatchesAdminSession_Allows()
    {
        var ctx = MakeContext("POST", "/admin/v1/tenants");
        ctx.Request.Headers["Cookie"] = "admin_session=admin-sess-1";
        ctx.Request.Headers["X-CSRF-Token"] = "correct-raw-token";
        AuthenticateUser(ctx, "AdminScheme", "aabbccdd");

        _tokenService.HashToken("correct-raw-token").Returns("aabbccdd");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task AdminUnsafeMethod_HeaderHashMismatch_Rejects403()
    {
        var ctx = MakeContext("POST", "/admin/v1/tenants");
        ctx.Request.Headers["Cookie"] = "admin_session=admin-sess-1";
        ctx.Request.Headers["X-CSRF-Token"] = "wrong-raw-token";
        AuthenticateUser(ctx, "AdminScheme", "aabbccdd");
        ctx.Response.Body = new MemoryStream();

        _tokenService.HashToken("wrong-raw-token").Returns("11223344");

        var sut = CreateSut();
        await sut.InvokeAsync(ctx, _tokenService);

        Assert.False(_nextCalled);
        Assert.Equal(403, ctx.Response.StatusCode);
    }
}
