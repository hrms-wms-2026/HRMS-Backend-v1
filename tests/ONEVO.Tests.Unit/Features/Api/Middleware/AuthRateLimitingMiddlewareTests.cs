using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Api.Middleware;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public sealed class AuthRateLimitingMiddlewareTests
{
    private const string MfaVerifyPath = "/api/v1/auth/mfa/verify";
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private AuthRateLimitingMiddleware Build()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        return new AuthRateLimitingMiddleware(
            next,
            _cache,
            Mock.Of<ILogger<AuthRateLimitingMiddleware>>());
    }

    private static DefaultHttpContext MakeContext(string? mfaCookie, string ip, string? jsonBody = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = MfaVerifyPath;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();

        if (mfaCookie is not null)
            context.Request.Headers.Cookie = $"onevo_mfa={mfaCookie}";

        if (jsonBody is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
        }

        return context;
    }

    [Fact]
    public async Task SameCookie_SixthRequestWithinWindow_Returns429()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
            await sut.InvokeAsync(MakeContext("challenge-a", "10.0.0.1"));

        var sixth = MakeContext("challenge-a", "10.0.0.1");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task DifferentCookies_AreTrackedIndependently()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            var first = MakeContext("challenge-a", "10.0.0.1");
            var second = MakeContext("challenge-b", "10.0.0.1");
            await sut.InvokeAsync(first);
            await sut.InvokeAsync(second);
            first.Response.StatusCode.Should().NotBe(429);
            second.Response.StatusCode.Should().NotBe(429);
        }
    }

    [Fact]
    public async Task NoCookie_FallsBackToIp()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
            await sut.InvokeAsync(MakeContext(null, "10.0.0.5"));

        var sixth = MakeContext(null, "10.0.0.5");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task LegacyBodyToken_IsIgnored()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            await sut.InvokeAsync(MakeContext(
                "challenge-c",
                "10.0.0.9",
                $$"""{"code":"111111","mfaSessionToken":"distinct-{{i}}"}"""));
        }

        var sixth = MakeContext(
            "challenge-c",
            "10.0.0.9",
            """{"code":"111111","mfaSessionToken":"distinct-final"}""");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    private static DefaultHttpContext MakeContextForPath(
        string path, string ip, string? cookieHeader = null, string? jsonBody = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();

        if (cookieHeader is not null)
            context.Request.Headers.Cookie = cookieHeader;

        if (jsonBody is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
        }

        return context;
    }

    [Fact]
    public async Task AdminForgotPassword_EleventhRequestFromSameIp_Returns429()
    {
        // Proves the real request path (which always has a leading slash) actually matches
        // the configured rule - the rule array previously stored these paths without a
        // leading slash, so they silently never matched and never rate-limited anything.
        var sut = Build();
        for (var i = 0; i < 10; i++)
        {
            await sut.InvokeAsync(
                MakeContextForPath("/admin/v1/auth/forgot-password", "10.0.1.1", jsonBody: $$"""{"email":"a{{i}}@onexso.test"}"""));
        }

        var eleventh = MakeContextForPath(
            "/admin/v1/auth/forgot-password", "10.0.1.1", jsonBody: """{"email":"final@onexso.test"}""");
        await sut.InvokeAsync(eleventh);

        eleventh.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AdminResetPassword_SixthRequestWithSameToken_Returns429()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            await sut.InvokeAsync(MakeContextForPath(
                "/admin/v1/auth/reset-password", "10.0.1.2", jsonBody: """{"token":"tok-a","newPassword":"x"}"""));
        }

        var sixth = MakeContextForPath(
            "/admin/v1/auth/reset-password", "10.0.1.2", jsonBody: """{"token":"tok-a","newPassword":"y"}""");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AdminLogin_SixthRequestWithSameEmail_Returns429()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            await sut.InvokeAsync(MakeContextForPath(
                "/admin/v1/auth/login", $"10.0.2.{i}", jsonBody: """{"email":"admin@onexso.test","password":"x"}"""));
        }

        var sixth = MakeContextForPath(
            "/admin/v1/auth/login", "10.0.2.99", jsonBody: """{"email":"admin@onexso.test","password":"y"}""");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AdminLogin_TwentyFirstRequestFromSameIp_Returns429()
    {
        var sut = Build();
        for (var i = 0; i < 20; i++)
        {
            await sut.InvokeAsync(MakeContextForPath(
                "/admin/v1/auth/login", "10.0.3.1", jsonBody: $$"""{"email":"a{{i}}@onexso.test","password":"x"}"""));
        }

        var twentyFirst = MakeContextForPath(
            "/admin/v1/auth/login", "10.0.3.1", jsonBody: """{"email":"final@onexso.test","password":"y"}""");
        await sut.InvokeAsync(twentyFirst);

        twentyFirst.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AdminMfaVerify_UsesAdminMfaCookie_NotTenantOnevoMfaCookie()
    {
        // The admin login flow sets an "admin_mfa" challenge cookie, not "onevo_mfa" - the
        // per-challenge limit must key off the cookie admin actually sends.
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            await sut.InvokeAsync(MakeContextForPath(
                "/admin/v1/auth/mfa/verify", "10.0.4.1", cookieHeader: "admin_mfa=admin-challenge-a"));
        }

        var sixth = MakeContextForPath(
            "/admin/v1/auth/mfa/verify", "10.0.4.1", cookieHeader: "admin_mfa=admin-challenge-a");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AdminMfaVerify_DifferentAdminMfaCookies_AreTrackedIndependently()
    {
        var sut = Build();
        for (var i = 0; i < 5; i++)
        {
            var first = MakeContextForPath(
                "/admin/v1/auth/mfa/verify", "10.0.4.2", cookieHeader: "admin_mfa=admin-challenge-b");
            var second = MakeContextForPath(
                "/admin/v1/auth/mfa/verify", "10.0.4.2", cookieHeader: "admin_mfa=admin-challenge-c");
            await sut.InvokeAsync(first);
            await sut.InvokeAsync(second);
            first.Response.StatusCode.Should().NotBe(429);
            second.Response.StatusCode.Should().NotBe(429);
        }
    }
}
