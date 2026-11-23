using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Api.Middleware;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

/// <summary>
/// Task 15: session exchange and the pending-legal login step are anonymous, so they get the same
/// per-IP ceiling as login (20 per 5 minutes).
/// </summary>
public sealed class AuthRateLimitingPublicLoginStepsTests
{
    private readonly ONEVO.Infrastructure.SharedState.IAttemptCounter _attempts = new ONEVO.Infrastructure.SharedState.MemoryAttemptCounter();

    private AuthRateLimitingMiddleware Build() =>
        new(_ => Task.CompletedTask, _attempts, Mock.Of<ILogger<AuthRateLimitingMiddleware>>());

    private static DefaultHttpContext Post(string path, string ip)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Theory]
    [InlineData("/api/v1/auth/session-exchange")]
    [InlineData("/api/v1/legal/acceptances/complete-login")]
    public async Task TwentyFirstRequestFromOneIp_Returns429(string path)
    {
        var sut = Build();
        for (var i = 0; i < 20; i++)
        {
            var allowed = Post(path, "10.0.0.7");
            await sut.InvokeAsync(allowed);
            allowed.Response.StatusCode.Should().Be(200);
        }

        var blocked = Post(path, "10.0.0.7");
        await sut.InvokeAsync(blocked);
        blocked.Response.StatusCode.Should().Be(429);

        var otherIp = Post(path, "10.0.0.8");
        await sut.InvokeAsync(otherIp);
        otherIp.Response.StatusCode.Should().Be(200);
    }
}
