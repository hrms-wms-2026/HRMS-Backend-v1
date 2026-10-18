using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public sealed class WebActivityTrackingMiddlewareTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private async Task<(WebActivityTracker Tracker, bool NextCalled, DateTimeOffset? SeenDuringNext)> Run(
        TenantContextMode mode, params Claim[] claims)
    {
        var tracker = new WebActivityTracker();
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(c => c.ContextMode).Returns(mode);
        tenant.Setup(c => c.TenantId).Returns(_tenantId);
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        var ctx = new DefaultHttpContext
        {
            User = claims.Length == 0 ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        var nextCalled = false;
        DateTimeOffset? seenDuringNext = null;
        var middleware = new WebActivityTrackingMiddleware(_ =>
        {
            nextCalled = true;
            seenDuringNext = tracker.LastSeenAt(_tenantId, _userId);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, tracker, tenant.Object, clock.Object);
        return (tracker, nextCalled, seenDuringNext);
    }

    [Fact]
    public async Task RecordsAnAuthenticatedWebUser_BeforeTheRequestIsHandled()
    {
        var (tracker, nextCalled, seenDuringNext) = await Run(
            TenantContextMode.Tenant, new Claim(ClaimTypes.NameIdentifier, _userId.ToString()));

        nextCalled.Should().BeTrue();
        seenDuringNext.Should().Be(Now);
        tracker.LastSeenAt(_tenantId, _userId).Should().Be(Now);
    }

    [Fact]
    public async Task IgnoresTrayDeviceTokens_AnonymousCallers_AndNonTenantContexts()
    {
        (await Run(TenantContextMode.Tenant,
                new Claim(ClaimTypes.NameIdentifier, _userId.ToString()), new Claim("token_type", "tray_device")))
            .Tracker.LastSeenAt(_tenantId, _userId).Should().BeNull();
        (await Run(TenantContextMode.Tenant)).Tracker.LastSeenAt(_tenantId, _userId).Should().BeNull();
        (await Run(TenantContextMode.Admin, new Claim(ClaimTypes.NameIdentifier, _userId.ToString())))
            .Tracker.LastSeenAt(_tenantId, _userId).Should().BeNull();
    }
}
