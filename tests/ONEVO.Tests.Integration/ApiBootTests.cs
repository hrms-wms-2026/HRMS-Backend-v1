using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration;

/// <summary>
/// Boots the real API host end-to-end (including hosted services like DevSmokeTestTenantSeeder)
/// against an ephemeral Testcontainers PostgreSQL instance, the same bootstrap approach used by
/// AdminTestFactory/BaseDomainLoginTestFactory, instead of the developer's persistent local
/// CrownusDb. Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ApiBootTests : IAsyncLifetime
{
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private ApiBootTestFactory _factory = null!;

    public async Task InitializeAsync()
    {
        // Cloned from the shared, already-migrated template (see SharedSqlServerTemplate) before
        // the WebApplicationFactory is ever touched. Accessing _factory.Services/CreateClient()
        // starts hosted services (such as DevSmokeTestTenantSeeder and PermissionSeeder)
        // synchronously during host startup, which query database tables that must already exist.
        // The environment scope must exist before that same CreateClient()/Services access too,
        // since that is what first triggers Program.cs's pre-Build() startup validators.
        var connectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new ApiBootTestFactory(connectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Health endpoint returned {response.StatusCode}");
    }

    // Task 15 (deny by default): the fallback policy must not catch the probes, must protect the
    // hubs, and must refuse anonymous callers on routes nobody declared.

    [Fact]
    public async Task ReadinessEndpoint_IsAnonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/hubs/inbox/negotiate?negotiateVersion=1")]
    [InlineData("/hubs/agent-commands/negotiate?negotiateVersion=1")]
    public async Task Hubs_RefuseAnonymousNegotiate(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(path, content: null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnmappedRoute_IsRefusedForAnonymousCaller()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/task-15-no-such-route");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnmappedAdminRoute_IsChallengedOnceAsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/admin/v1/task-15-no-such-route");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);

        // One scheme challenges, so the body is a single JSON problem object.
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsOk_InDevelopment()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Swagger endpoint returned {response.StatusCode}");
    }
}
