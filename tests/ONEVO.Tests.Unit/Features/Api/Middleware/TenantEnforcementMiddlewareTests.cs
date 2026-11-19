using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class TenantEnforcementMiddlewareTests
{
    private bool _nextCalled;
    private readonly RequestDelegate _next;

    public TenantEnforcementMiddlewareTests()
    {
        _next = _ => { _nextCalled = true; return Task.CompletedTask; };
    }

    private static TenantEnforcementMiddleware Build(RequestDelegate next) => new(next);

    private HttpContext MakeTenantContext(
        Guid tenantId,
        TenantStatus status,
        bool authenticated = false,
        Guid? jwtTenantId = null,
        string path = "/api/v1/resources")
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(c => c.ContextMode).Returns(TenantContextMode.Tenant);
        mock.Setup(c => c.IsResolved).Returns(true);
        mock.Setup(c => c.TenantId).Returns(tenantId);
        mock.Setup(c => c.Status).Returns(status);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        if (authenticated)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new("tenant_id", (jwtTenantId ?? tenantId).ToString())
            };
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        ctx.RequestServices = MockServices(mock.Object);
        return ctx;
    }

    private static IServiceProvider MockServices(ITenantContext ctx)
    {
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(ITenantContext))).Returns(ctx);
        return sp.Object;
    }

    private static void AllowAnonymous(HttpContext context)
    {
        var metadata = new EndpointMetadataCollection(new AllowAnonymousAttribute());
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "anonymous-test-endpoint"));
    }

    [Fact]
    public async Task AnonymousLogin_TenantResolved_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeTenantContext(Guid.NewGuid(), TenantStatus.Active,
            authenticated: false, path: "/api/v1/auth/login");
        AllowAnonymous(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousMfaVerify_TenantResolved_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeTenantContext(Guid.NewGuid(), TenantStatus.Active,
            authenticated: false, path: "/api/v1/auth/mfa/verify");
        AllowAnonymous(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousLogin_WithHostMismatchedSession_ClearsSessionAndPasses()
    {
        var hostTenantId = Guid.NewGuid();
        var sut = Build(_next);
        var ctx = MakeTenantContext(hostTenantId, TenantStatus.Active,
            authenticated: true, jwtTenantId: Guid.NewGuid(), path: "/api/v1/auth/login");
        ctx.Request.Headers.Cookie = "crownus_session=other-tenant-ticket; crownus_csrf=stale";
        AllowAnonymous(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
        ctx.User.Identity?.IsAuthenticated.Should().BeFalse();
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("crownus_session=");
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("crownus_csrf=");
    }

    [Fact]
    public async Task ProtectedTenantEndpoint_WithStaleSessionCookie_Returns401()
    {
        var sut = Build(_next);
        var ctx = MakeTenantContext(Guid.NewGuid(), TenantStatus.Active,
            authenticated: false, path: "/api/v1/resources");
        ctx.Request.Headers.Cookie = "crownus_session=stale-invalid-ticket";
        ctx.Response.Body = new MemoryStream();

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticated_JwtTenantMatchesHost_ActiveTenant_Passes()
    {
        var tenantId = Guid.NewGuid();
        var sut = Build(_next);
        var ctx = MakeTenantContext(tenantId, TenantStatus.Active,
            authenticated: true, jwtTenantId: tenantId);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Authenticated_JwtTenantMismatch_Returns403()
    {
        var sut = Build(_next);
        var ctx = MakeTenantContext(Guid.NewGuid(), TenantStatus.Active,
            authenticated: true, jwtTenantId: Guid.NewGuid());

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
        ctx.Response.Headers["Set-Cookie"].ToString().Should().Contain("crownus_session");
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Cancelled)]
    [InlineData(TenantStatus.Provisioning)]
    public async Task Authenticated_BlockedTenantStatus_Returns403(TenantStatus status)
    {
        var tenantId = Guid.NewGuid();
        var sut = Build(_next);
        var ctx = MakeTenantContext(tenantId, status,
            authenticated: true, jwtTenantId: tenantId);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task AdminHost_TenantApiPath_Returns403()
    {
        var sp = new Mock<IServiceProvider>();
        var mock = new Mock<ITenantContext>();
        mock.Setup(c => c.ContextMode).Returns(TenantContextMode.Admin);
        sp.Setup(s => s.GetService(typeof(ITenantContext))).Returns(mock.Object);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/v1/roles";
        ctx.RequestServices = sp.Object;

        var sut = Build(_next);
        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    // ---- System context (root host / SingleHost without tenant header) ----

    private HttpContext MakeSystemContext(
        string path,
        string method = "GET",
        bool authenticated = false,
        bool withTenantClaim = true)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(c => c.ContextMode).Returns(TenantContextMode.System);
        mock.Setup(c => c.IsResolved).Returns(true);
        mock.Setup(c => c.TenantId).Returns(Guid.Empty);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();

        if (authenticated)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            if (withTenantClaim)
                claims.Add(new Claim("tenant_id", Guid.NewGuid().ToString()));
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        ctx.RequestServices = MockServices(mock.Object);
        return ctx;
    }

    private static void ProtectedEndpoint(HttpContext context)
    {
        var metadata = new EndpointMetadataCollection(new AuthorizeAttribute("TenantPolicy"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "protected-test-endpoint"));
    }

    private static async Task<string> ReadDetail(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var json = await System.Text.Json.JsonDocument.ParseAsync(ctx.Response.Body);
        return json.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task SystemContext_AuthenticatedTenantPrincipal_TenantApi_Returns403()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/employees", authenticated: true);
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
        (await ReadDetail(ctx)).Should().Be(TenantEnforcementMiddleware.TenantSessionWithoutTenantContextDetail);
    }

    [Fact]
    public async Task SystemContext_TenantPrincipal_UnmatchedRoute_Returns403()
    {
        // No endpoint metadata means not [AllowAnonymous]: fail closed.
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/employees", authenticated: true);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SystemContext_TenantPrincipal_DoesNotClearSessionCookie()
    {
        // In SingleHost mode the session is still valid for its own tenant; one request without
        // the tenant header must not sign the user out.
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/employees", authenticated: true);
        ctx.Request.Headers.Cookie = "crownus_session=valid-ticket";
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        ctx.Response.Headers.SetCookie.ToString().Should().NotContain("crownus_session");
    }

    [Fact]
    public async Task SystemContext_AnonymousLogin_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/auth/login", method: "POST");
        AllowAnonymous(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/login/select-workspace")]
    [InlineData("/api/v1/legal/acceptances/complete-login")]
    [InlineData("/api/v1/auth/session-exchange")]
    public async Task SystemContext_AllowAnonymousEndpoint_WithTenantPrincipal_Passes(string path)
    {
        // Re-login on the root host while an old session cookie is still present.
        var sut = Build(_next);
        var ctx = MakeSystemContext(path, method: "POST", authenticated: true);
        AllowAnonymous(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SystemContext_TenantPrincipal_Logout_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/auth/logout", method: "POST", authenticated: true);
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/auth/logout/anything", "POST")]
    [InlineData("/api/v1/auth/logout", "GET")]
    public async Task SystemContext_TenantPrincipal_NotExactLogout_Returns403(string path, string method)
    {
        // The logout exemption is exact: POST /api/v1/auth/logout only.
        var sut = Build(_next);
        var ctx = MakeSystemContext(path, method: method, authenticated: true);
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SystemContext_Unauthenticated_ProtectedEndpoint_PassesToAuthorization()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/employees");
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SystemContext_AuthenticatedWithoutTenantClaim_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/api/v1/employees", authenticated: true, withTenantClaim: false);
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SystemContext_TenantPrincipal_NonTenantApiPath_Passes()
    {
        var sut = Build(_next);
        var ctx = MakeSystemContext("/health/ready", authenticated: true);
        ProtectedEndpoint(ctx);

        await sut.InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TenantHost_AdminPath_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var sut = Build(_next);
        var ctx = MakeTenantContext(tenantId, TenantStatus.Active,
            authenticated: true, jwtTenantId: tenantId, path: "/admin/dashboard");

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        _nextCalled.Should().BeFalse();
    }
}
