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
        return new AuthRateLimitingMiddleware(next, _cache, Mock.Of<ILogger<AuthRateLimitingMiddleware>>());
    }

    private static DefaultHttpContext MakeContext(string? mfaCookie, string ip, string? jsonBody = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = MfaVerifyPath;
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Response.Body = new MemoryStream();

        if (mfaCookie is not null)
            ctx.Request.Headers["Cookie"] = $"onevo_mfa={mfaCookie}";

        if (jsonBody is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.ContentType = "application/json";
        }

        return ctx;
    }

    [Fact]
    public async Task SameCookie_SixthRequestWithinWindow_Returns429()
    {
        var sut = Build();

        for (var i = 0; i < 5; i++)
        {
            var ctx = MakeContext("challenge-a", "10.0.0.1");
            await sut.InvokeAsync(ctx);
            ctx.Response.StatusCode.Should().NotBe(429);
        }

        var sixth = MakeContext("challenge-a", "10.0.0.1");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task DifferentCookies_TrackedIndependently_NeitherRateLimited()
    {
        var sut = Build();

        for (var i = 0; i < 5; i++)
        {
            var ctxA = MakeContext("challenge-a", "10.0.0.1");
            await sut.InvokeAsync(ctxA);
            ctxA.Response.StatusCode.Should().NotBe(429);

            var ctxB = MakeContext("challenge-b", "10.0.0.1");
            await sut.InvokeAsync(ctxB);
            ctxB.Response.StatusCode.Should().NotBe(429);
        }
    }

    [Fact]
    public async Task NoCookie_FallsBackToIp_SixthRequestFromSameIp_Returns429()
    {
        var sut = Build();

        for (var i = 0; i < 5; i++)
        {
            var ctx = MakeContext(mfaCookie: null, ip: "10.0.0.5");
            await sut.InvokeAsync(ctx);
            ctx.Response.StatusCode.Should().NotBe(429);
        }

        var sixth = MakeContext(mfaCookie: null, ip: "10.0.0.5");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task LegacyMfaSessionTokenBodyField_IsIgnored_KeyingStillFollowsCookie()
    {
        var sut = Build();

        for (var i = 0; i < 5; i++)
        {
            var ctx = MakeContext(
                "challenge-c",
                "10.0.0.9",
                jsonBody: $$"""{"code":"111111","mfaSessionToken":"distinct-{{i}}"}""");
            await sut.InvokeAsync(ctx);
            ctx.Response.StatusCode.Should().NotBe(429);
        }

        var sixth = MakeContext(
            "challenge-c",
            "10.0.0.9",
            jsonBody: """{"code":"111111","mfaSessionToken":"distinct-final"}""");
        await sut.InvokeAsync(sixth);

        sixth.Response.StatusCode.Should().Be(429);
    }
}
