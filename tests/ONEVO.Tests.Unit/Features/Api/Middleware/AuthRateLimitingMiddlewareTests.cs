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
}
