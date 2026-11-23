using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Api.Middleware;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Api.Middleware;

public class HostTenantResolutionMiddlewareTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<IWritableTenantContext> _ctx = new();

    private bool _nextCalled;

    private HostTenantResolutionMiddleware Build(string rootDomain = "onevo.com", string? mode = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:RootDomain"] = rootDomain
        };
        if (mode is not null)
            settings["Tenancy:Mode"] = mode;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new HostTenantResolutionMiddleware(
            _ => { _nextCalled = true; return Task.CompletedTask; }, _cache, config);
    }

    private HostTenantResolutionMiddleware BuildSingleHost(string rootDomain = "app.example.net")
        => Build(rootDomain, "SingleHost");

    private HttpContext MakeSingleHostContext(
        string path, string? header = null, string? query = null, string host = "app.example.net")
    {
        var ctx = MakeContext(host, path);
        if (header is not null)
            ctx.Request.Headers[ONEVO.Application.Common.Tenancy.TenancyHeaders.Tenant] = header;
        if (query is not null)
            ctx.Request.QueryString = new QueryString("?" + query);
        return ctx;
    }

    private void SetupTenant(string slug, Guid? id = null)
        => _tenants.Setup(r => r.GetBySlugAsync(slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = id ?? Guid.NewGuid(), Slug = slug, Status = TenantStatus.Active });

    [Fact]
    public async Task SingleHost_HeaderSlug_ResolvesTenant()
    {
        var id = Guid.NewGuid();
        SetupTenant("acme", id);
        var ctx = MakeSingleHostContext("/api/v1/x", header: "acme");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.TenantId == id && e.Slug == "acme")), Times.Once);
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SingleHost_HubQuerySlug_ResolvesTenant()
    {
        var id = Guid.NewGuid();
        SetupTenant("acme", id);
        var ctx = MakeSingleHostContext("/hubs/inbox", query: "tenant=acme");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.TenantId == id)), Times.Once);
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SingleHost_QuerySlugOutsideHubs_Ignored()
    {
        SetupTenant("acme");
        var ctx = MakeSingleHostContext("/api/v1/x", query: "tenant=acme");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
    }

    [Fact]
    public async Task SingleHost_AdminPath_SetsAdminMode()
    {
        SetupTenant("acme");
        var ctx = MakeSingleHostContext("/admin/v1/tenants", header: "acme");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.SetAdminMode(), Times.Once);
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SingleHost_NoSlug_SystemMode()
    {
        var ctx = MakeSingleHostContext("/api/v1/auth/login");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
        _nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("console")]
    [InlineData("www")]
    [InlineData("assets")]
    [InlineData("api")]
    public async Task SingleHost_ReservedSlug_400(string slug)
    {
        var ctx = MakeSingleHostContext("/api/v1/x", header: slug);

        await BuildSingleHost().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("a")]
    [InlineData("x.y")]
    [InlineData("-bad")]
    public async Task SingleHost_InvalidSlug_400(string slug)
    {
        var ctx = MakeSingleHostContext("/api/v1/x", header: slug);

        await BuildSingleHost().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SingleHost_UnknownSlug_404()
    {
        _tenants.Setup(r => r.GetBySlugAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        var ctx = MakeSingleHostContext("/api/v1/x", header: "ghost");

        await BuildSingleHost().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SingleHost_OtherHost_400()
    {
        var ctx = MakeSingleHostContext("/api/v1/x", header: "acme", host: "evil.net");

        await BuildSingleHost().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task SingleHost_TenantSubdomainHost_400()
    {
        SetupTenant("acme");
        var ctx = MakeSingleHostContext("/api/v1/x", header: "acme", host: "acme.app.example.net");

        await BuildSingleHost().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
    }

    [Fact]
    public async Task DefaultMode_HeaderIgnored()
    {
        var id = Guid.NewGuid();
        SetupTenant("acme", id);
        SetupTenant("other");
        var ctx = MakeSingleHostContext("/api/v1/x", header: "other", host: "acme.app.example.net");

        await Build("app.example.net").InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.TenantId == id && e.Slug == "acme")), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SingleHost_BlankHeader_SystemMode(string header)
    {
        var ctx = MakeSingleHostContext("/api/v1/x", header: header);

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SingleHost_HubHeaderWinsOverQuery()
    {
        var id = Guid.NewGuid();
        SetupTenant("acme", id);
        SetupTenant("other");
        var ctx = MakeSingleHostContext("/hubs/inbox", header: "acme", query: "tenant=other");

        await BuildSingleHost().InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.TenantId == id && e.Slug == "acme")), Times.Once);
    }

    [Fact]
    public async Task Subdomain_HeaderIgnored()
    {
        var id = Guid.NewGuid();
        SetupTenant("acme", id);
        SetupTenant("other");
        var ctx = MakeSingleHostContext("/api/v1/x", header: "other", host: "acme.app.example.net");

        await Build("app.example.net", "Subdomain").InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.TenantId == id && e.Slug == "acme")), Times.Once);
    }

    private HttpContext MakeContext(string host, string path = "/api/v1/test")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        ctx.Request.Path = path;
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IWritableTenantContext))).Returns(_ctx.Object);
        sp.Setup(s => s.GetService(typeof(ITenantRepository))).Returns(_tenants.Object);
        ctx.RequestServices = sp.Object;
        return ctx;
    }

    [Theory]
    [InlineData("badonevo.com")]
    [InlineData("evil-onevo.com")]
    [InlineData("onevo.com.evil.com")]
    public async Task InvalidRootDomain_Returns400(string host)
    {
        var sut = Build();
        var ctx = MakeContext(host);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
    }

    // Container HEALTHCHECK calls localhost; load balancers often probe by IP. Neither is under the root domain.
    [Theory]
    [InlineData("localhost", "/health")]
    [InlineData("10.0.0.4", "/health/ready")]
    [InlineData("onevo.com", "/health")]
    [InlineData("acme.onevo.com", "/health/ready")]
    public async Task HealthProbe_AnyHost_SystemModeAndPassesThrough(string host, string path)
    {
        var ctx = MakeContext(host, path);

        await Build().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        _nextCalled.Should().BeTrue();
        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
        _tenants.Verify(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/health/ready/extra")]
    [InlineData("/health/other")]
    [InlineData("/api/v1/health")]
    public async Task NonHealthPath_ForeignHost_Still400(string path)
    {
        var ctx = MakeContext("localhost", path);

        await Build().InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task HealthProbe_SingleHostMode_ForeignHost_SystemMode()
    {
        var ctx = MakeContext("localhost", "/health");

        await BuildSingleHost().InvokeAsync(ctx);

        _nextCalled.Should().BeTrue();
        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
    }

    [Theory]
    [InlineData("api.onevo.com")]
    public async Task ReservedRejectedHost_Returns400(string host)
    {
        var sut = Build();
        var ctx = MakeContext(host);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("admin.onevo.com")]
    [InlineData("console.onevo.com")]
    public async Task AdminHost_SetsAdminMode(string host)
    {
        var sut = Build();
        var ctx = MakeContext(host);

        await sut.InvokeAsync(ctx);

        _ctx.Verify(c => c.SetAdminMode(), Times.Once);
    }

    [Fact]
    public async Task TenantHost_SlugFound_ResolvesContext()
    {
        var tenantId = Guid.NewGuid();
        _tenants.Setup(r => r.GetBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Status = TenantStatus.Active });

        var sut = Build();
        var ctx = MakeContext("acme.onevo.com");

        await sut.InvokeAsync(ctx);

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e =>
            e.TenantId == tenantId && e.Slug == "acme")), Times.Once);
        ctx.Response.StatusCode.Should().NotBe(404);
    }

    [Fact]
    public async Task DevelopmentTenantHost_AcmeLocalhost_ResolvesContext()
    {
        var tenantId = Guid.NewGuid();
        _tenants.Setup(r => r.GetBySlugAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Status = TenantStatus.Active });

        await Build("localhost").InvokeAsync(MakeContext("acme.localhost", "/api/v1/auth/login"));

        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(entry =>
            entry.TenantId == tenantId && entry.Slug == "acme")), Times.Once);
    }

    [Fact]
    public async Task DevelopmentRootHost_Localhost_SetsSystemMode()
    {
        await Build("localhost").InvokeAsync(MakeContext("localhost", "/api/v1/auth/login"));

        _ctx.Verify(c => c.SetSystemMode(), Times.Once);
        _ctx.Verify(c => c.Resolve(It.IsAny<TenantRegistryEntry>()), Times.Never);
    }

    [Fact]
    public async Task TenantHost_SlugNotFound_Returns404()
    {
        _tenants.Setup(r => r.GetBySlugAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = Build();
        var ctx = MakeContext("unknown.onevo.com");

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MultiLabelSlug_Returns400()
    {
        var sut = Build();
        var ctx = MakeContext("a.b.onevo.com");

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task TenantHost_CacheHit_DoesNotQueryRepository()
    {
        var entry = new TenantRegistryEntry(Guid.NewGuid(), "acme", TenantStatus.Active, null);
        _cache.Set("tenant:slug:acme", entry);

        var sut = Build();
        var ctx = MakeContext("acme.onevo.com");

        await sut.InvokeAsync(ctx);

        _tenants.Verify(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ctx.Verify(c => c.Resolve(It.Is<TenantRegistryEntry>(e => e.Slug == "acme")), Times.Once);
    }
}
