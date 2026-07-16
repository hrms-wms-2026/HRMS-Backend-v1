using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class PermissionVersionMiddlewareTests
{
    [Fact]
    public async Task TenantCookieSession_DoesNotRequireTokenPermissionVersionClaim()
    {
        var nextCalled = false;
        var middleware = new PermissionVersionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<PermissionVersionMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/auth/me";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            "TenantScheme"));

        var versions = new Mock<IPermissionVersionService>();

        await middleware.InvokeAsync(context, versions.Object);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        versions.Verify(
            instance => instance.GetCurrentVersionAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NonBrowserVersionedToken_StillChecksPermissionVersion()
    {
        var nextCalled = false;
        var middleware = new PermissionVersionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<PermissionVersionMiddleware>.Instance);

        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/agent/status";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("perm_ver", "2")
            ],
            "Bearer"));

        var versions = new Mock<IPermissionVersionService>();
        versions
            .Setup(instance => instance.GetCurrentVersionAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        await middleware.InvokeAsync(context, versions.Object);

        Assert.True(nextCalled);
        versions.Verify(
            instance => instance.GetCurrentVersionAsync(
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
