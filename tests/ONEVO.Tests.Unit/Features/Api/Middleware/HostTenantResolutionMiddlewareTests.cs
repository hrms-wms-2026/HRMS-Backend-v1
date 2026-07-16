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

    private HostTenantResolutionMiddleware Build(string rootDomain = "onevo.com")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:RootDomain"] = rootDomain
            })
            .Build();
        return new HostTenantResolutionMiddleware(
            _ => Task.CompletedTask, _cache, config);
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
