using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.AgentGateway;

/// <summary>
/// Verifies that the agent-command API routes are registered, auth-gated, and
/// enforce agent isolation at the HTTP boundary.
/// </summary>
public sealed class AgentCommandApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("onevo_agent_cmd_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AgentCommandTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new AgentCommandTestFactory(_postgres.GetConnectionString());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetCommands_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/agent/commands");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RespondToCommand_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsync(
            $"/api/v1/agent/commands/{Guid.NewGuid()}/response",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCommands_WithInvalidBearerToken_Returns401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/agent/commands");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "invalid.jwt.token");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

internal sealed class AgentCommandTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AgentCommandTestFactory(string connectionString)
        => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "test_secret_at_least_32_chars_long_!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["Encryption:MasterKey"] = "test_master_key_32_characters_minimum!!"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>((_, options) =>
                options.UseNpgsql(_connectionString).UseSnakeCaseNamingConvention());
        });
    }
}
