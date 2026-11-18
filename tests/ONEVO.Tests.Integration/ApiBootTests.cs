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

    /// <summary>
    /// Task 23: Swagger UI and every runtime OpenAPI endpoint are removed in every environment,
    /// Development included. The API description is generated at build time (docs/api/openapi).
    /// The answer may be 401 (deny-by-default fallback) or 404 (no route), but never a document.
    /// </summary>
    [Theory]
    [InlineData("Test")]
    [InlineData("Development")]
    public async Task SwaggerAndOpenApiPaths_ServeNothing(string environment)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // Even an old Swagger:Enabled setting left on a server must not bring anything back.
            builder.UseSetting("Swagger:Enabled", "true");
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var path in new[]
                 {
                     "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/swagger/admin-v1/swagger.json",
                     "/openapi/v1.json", "/openapi/admin-v1.json"
                 })
        {
            var response = await client.GetAsync(path);
            Assert.True(
                response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized,
                $"{environment}: {path} returned {(int)response.StatusCode}; expected 401 or 404");
        }
    }
}
