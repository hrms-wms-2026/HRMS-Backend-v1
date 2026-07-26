using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration;

/// <summary>
/// WebApplicationFactory used by ApiBootTests. Wires the app to a Testcontainers Postgres
/// instance instead of the developer's persistent local OnevoDb, matching the pattern used
/// by AdminTestFactory/AuthTestFactory/BaseDomainLoginTestFactory.
/// </summary>
public sealed class ApiBootTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiBootTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // These values only reach post-Build() config consumers. Program.cs's pre-Build()
        // ConfigurationStartupValidator/DatabaseConnectionStartupValidator run before
        // ConfigureWebHost is ever applied, so ApiBootTests.InitializeAsync must put the same
        // values in process environment variables via IntegrationTestEnvironmentScope before this
        // factory is constructed - this callback cannot supply them in time.
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "test_secret_at_least_32_chars_long_!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["Tenancy:RootDomain"] = "localhost",
                ["Encryption:MasterKey"] = "api-boot-test-encryption-master-key!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                // Bootstrap the canonical platform_users row the DevAdmin login resolves.
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Api Boot Test Super Admin"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseNpgsql(_connectionString)
                       .UseSnakeCaseNamingConvention();
            });
        });
    }
}
