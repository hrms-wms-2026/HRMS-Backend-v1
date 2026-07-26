using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration;

/// <summary>
/// Boots the real API host end-to-end (including hosted services like DevSmokeTestTenantSeeder)
/// against an ephemeral Testcontainers PostgreSQL instance, the same bootstrap approach used by
/// AdminTestFactory/BaseDomainLoginTestFactory, instead of the developer's persistent local
/// OnevoDb. Requires Docker.
/// </summary>
public sealed class ApiBootTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_api_boot_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private ApiBootTestFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        // Migrate via a standalone ApplicationDbContext before the WebApplicationFactory is ever
        // touched. Accessing _factory.Services/CreateClient() starts hosted services (such as
        // DevSmokeTestTenantSeeder and PermissionSeeder) synchronously during host startup, which
        // query database tables that must already exist - mirrors AdminTestFactory.MigrateDatabaseAsync.
        await MigrateDatabaseAsync(connectionString);

        _factory = new ApiBootTestFactory(connectionString);
    }

    private static async Task MigrateDatabaseAsync(string connectionString)
    {
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(connectionString);

        var migrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var dateTimeProvider = new SystemDateTimeProvider();
        await using var migrationContext = new ApplicationDbContext(
            migrationOptions,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
        await migrationContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
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
