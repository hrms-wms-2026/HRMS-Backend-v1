using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Options;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class TrayPresenceEnforcementMiddlewareTests
{
    private bool _nextCalled;
    private readonly RequestDelegate _next;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public TrayPresenceEnforcementMiddlewareTests()
    {
        _next = _ => { _nextCalled = true; return Task.CompletedTask; };
    }

    private static TrayPresenceEnforcementMiddleware Build(RequestDelegate next) => new(next);

    private HttpContext MakeContext(
        string mode,
        TrayDeviceRegistration? device,
        bool authenticated = true,
        bool allowWithoutActiveTray = false)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);
        tenantContext.Setup(c => c.TenantId).Returns(_tenantId);

        var options = Options.Create(new TrayPresenceOptions { Mode = mode, GracePeriodSeconds = 120 });

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(_now);

        var repository = new Mock<ITrayActivationRepository>();
        repository
            .Setup(r => r.FindLatestActiveDeviceForUserAsync(_userId, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(device);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(ITenantContext))).Returns(tenantContext.Object);
        sp.Setup(s => s.GetService(typeof(IOptions<TrayPresenceOptions>))).Returns(options);
        sp.Setup(s => s.GetService(typeof(ITrayActivationRepository))).Returns(repository.Object);
        sp.Setup(s => s.GetService(typeof(IDateTimeProvider))).Returns(clock.Object);
        sp.Setup(s => s.GetService(typeof(ILogger<TrayPresenceEnforcementMiddleware>)))
            .Returns(NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        var ctx = new DefaultHttpContext { RequestServices = sp.Object, User = new ClaimsPrincipal() };
        ctx.Response.Body = new MemoryStream();

        if (authenticated)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _userId.ToString()) };
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        if (allowWithoutActiveTray)
        {
            var metadata = new EndpointMetadataCollection(new AllowWithoutActiveTrayAttribute());
            ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "exempt-test-endpoint"));
        }

        return ctx;
    }

    private TrayDeviceRegistration ConnectedDevice() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        UserId = _userId,
        DeviceName = "Test Device",
        LastSeenAt = _now.AddSeconds(-10),
    };

    [Fact]
    public async Task ObserveMode_NoDevice_DoesNotBlock()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Observe", device: null);

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task EnforceMode_NoConnectedDevice_Returns428()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Enforce", device: null);

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task EnforceMode_ConnectedDevice_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Enforce", device: ConnectedDevice());

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task OffMode_NoConnectedDevice_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Off", device: null);

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task EnforceMode_ExemptEndpoint_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Enforce", device: null, allowWithoutActiveTray: true);

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task EnforceMode_Unauthenticated_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeContext("Enforce", device: null, authenticated: false);

        await sut.InvokeAsync(ctx, NullLogger<TrayPresenceEnforcementMiddleware>.Instance);

        _nextCalled.Should().BeTrue();
    }
}
