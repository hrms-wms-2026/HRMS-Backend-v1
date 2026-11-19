using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.Hosting;

/// <summary>
/// Optional SPA hosting (<c>Hosting:ServeSpas</c>): the API serves the web app and the admin
/// console from <c>{SpaRoot}/web</c> and <c>{SpaRoot}/admin-console</c>. SingleHost picks the app
/// by path, Subdomain by host. Requires Docker (Testcontainers SQL Server).
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SpaHostingTests : IAsyncLifetime
{
    private const string WebMarker = "<!-- spa-test-marker:web -->";
    private const string AdminMarker = "<!-- spa-test-marker:admin-console -->";
    private const string SubdomainRoot = "crownus.test";

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private ApiBootTestFactory _factory = null!;
    private string _spaRoot = null!;
    private readonly List<WebApplicationFactory<Program>> _derived = new();

    public async Task InitializeAsync()
    {
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);
        _factory = new ApiBootTestFactory(connectionString);

        _spaRoot = Path.Combine(Path.GetTempPath(), "crownus-spa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_spaRoot, "web"));
        Directory.CreateDirectory(Path.Combine(_spaRoot, "admin-console"));
        await File.WriteAllTextAsync(
            Path.Combine(_spaRoot, "web", "index.html"),
            $"<!doctype html><html><body>{WebMarker}</body></html>");
        await File.WriteAllTextAsync(
            Path.Combine(_spaRoot, "admin-console", "index.html"),
            $"<!doctype html><html><body>{AdminMarker}</body></html>");
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _derived)
            await factory.DisposeAsync();
        await _factory.DisposeAsync();
        await _environmentScope.DisposeAsync();
        try { Directory.Delete(_spaRoot, recursive: true); } catch (IOException) { }
    }

    // ---- SingleHost ----

    [Fact]
    public async Task SingleHost_Root_ServesWebIndex()
    {
        var response = await GetAsync(SingleHost(), "localhost", "/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(WebMarker, body);
    }

    [Fact]
    public async Task SingleHost_DeepLink_ServesWebIndex()
    {
        var response = await GetAsync(SingleHost(), "localhost", "/dashboard/team");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SingleHost_AdminConsole_DeepLink_ServesAdminIndex()
    {
        var response = await GetAsync(SingleHost(), "localhost", "/admin-console/tenants/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(AdminMarker, body);
        Assert.DoesNotContain(WebMarker, body);
    }

    // ---- Subdomain ----

    [Fact]
    public async Task Subdomain_TenantHost_ServesWebIndex()
    {
        var factory = Subdomain();
        await SeedActiveTenantAsync(factory, "acme");

        var response = await GetAsync(factory, "acme." + SubdomainRoot, "/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Subdomain_RootHost_ServesWebIndex()
    {
        var response = await GetAsync(Subdomain(), SubdomainRoot, "/auth/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Subdomain_AdminHost_ServesAdminIndex()
    {
        var response = await GetAsync(Subdomain(), "admin." + SubdomainRoot, "/tenants/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(AdminMarker, body);
        Assert.DoesNotContain(WebMarker, body);
    }

    [Fact]
    public async Task Subdomain_AdminHost_AdminConsolePath_NotSpecial()
    {
        var response = await GetAsync(Subdomain(), "admin." + SubdomainRoot, "/admin-console/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(AdminMarker, body);
        Assert.DoesNotContain(WebMarker, body);
    }

    // ---- Both modes ----

    [Theory]
    [InlineData("SingleHost", "/api/v1/does-not-exist")]
    [InlineData("SingleHost", "/admin/v1/nope")]
    [InlineData("SingleHost", "/hubs/x")]
    [InlineData("SingleHost", "/health/x")]
    [InlineData("Subdomain", "/api/v1/does-not-exist")]
    [InlineData("Subdomain", "/admin/v1/nope")]
    [InlineData("Subdomain", "/hubs/x")]
    [InlineData("Subdomain", "/health/x")]
    public async Task UnknownApiPath_Is404NotIndex(string mode, string path)
    {
        var (factory, host) = mode == "SingleHost"
            ? (SingleHost(), "localhost")
            : (Subdomain(), SubdomainRoot);

        var response = await GetAsync(factory, host, path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode != HttpStatusCode.OK
                || (!body.Contains(WebMarker) && !body.Contains(AdminMarker)),
            $"{mode} {path} returned {(int)response.StatusCode} with an SPA index body.");
    }

    [Theory]
    [InlineData("SingleHost")]
    [InlineData("Subdomain")]
    public async Task MissingAsset_Is404(string mode)
    {
        var (factory, host) = mode == "SingleHost"
            ? (SingleHost(), "localhost")
            : (Subdomain(), SubdomainRoot);

        var response = await GetAsync(factory, host, "/main.missing.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("SingleHost")]
    [InlineData("Subdomain")]
    public async Task ServeSpasOff_NoSpaServing(string mode)
    {
        var (factory, host) = mode == "SingleHost"
            ? (Build("SingleHost", "localhost", serveSpas: false), "localhost")
            : (Build("Subdomain", SubdomainRoot, serveSpas: false), SubdomainRoot);

        var response = await GetAsync(factory, host, "/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(WebMarker, body);
        Assert.DoesNotContain(AdminMarker, body);
    }

    // ---- helpers ----

    private WebApplicationFactory<Program> SingleHost() => Build("SingleHost", "localhost", serveSpas: true);

    private WebApplicationFactory<Program> Subdomain() => Build("Subdomain", SubdomainRoot, serveSpas: true);

    private WebApplicationFactory<Program> Build(string mode, string rootDomain, bool serveSpas)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Tenancy:Mode"] = mode,
                    ["Tenancy:RootDomain"] = rootDomain,
                    // SpaRoot is set even when serving is off: proves the switch, not a missing
                    // folder, is what keeps the files from being served.
                    ["Hosting:SpaRoot"] = _spaRoot
                };
                if (serveSpas)
                    values["Hosting:ServeSpas"] = "true";
                config.AddInMemoryCollection(values);
            }));
        _derived.Add(factory);
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(
        WebApplicationFactory<Program> factory, string host, string path)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{host}/"),
            AllowAutoRedirect = false
        });
        return await client.GetAsync(path);
    }

    private static async Task SeedActiveTenantAsync(WebApplicationFactory<Program> factory, string slug)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The shared migrated template may already contain this slug; make sure it is Active.
        var existing = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Slug == slug);
        if (existing is not null)
        {
            existing.Status = TenantStatus.Active;
            await db.SaveChangesAsync();
            return;
        }

        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = slug,
            Slug = slug,
            CompanySizeRange = "1-10",
            Status = TenantStatus.Active
        });
        await db.SaveChangesAsync();
    }
}
