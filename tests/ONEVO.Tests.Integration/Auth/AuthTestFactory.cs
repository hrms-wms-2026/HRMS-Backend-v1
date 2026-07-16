using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Auth;

public class AuthTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AuthTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "test_secret_at_least_32_chars_long_!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the DB context with test database
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
