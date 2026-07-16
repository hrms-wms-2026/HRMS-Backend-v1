using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Tenancy;

/// <summary>
/// WebApplicationFactory variant used by admin/v1 integration tests. Wires the
/// app to a Testcontainers Postgres instance 
/// 
/// </summary>
public class AdminTestFactory : WebApplicationFactory<Program>
{
    public const string TenantSecret = "test_secret_at_least_32_chars_long_!!";
    public const string TenantIssuer = "onevo-api";
    public const string TenantAudience = "onevo-api";

    private readonly string _connectionString;

    public AdminTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = TenantSecret,
                ["Jwt:TenantIssuer"] = TenantIssuer,
                ["Jwt:TenantAudience"] = TenantAudience,
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                // Bootstrap the canonical platform_users row the DevAdmin login resolves.
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Integration Test Super Admin"
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
