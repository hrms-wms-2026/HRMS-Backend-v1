using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.Tenancy;

namespace ONEVO.Tests.Integration.E2E;

/// <summary>
/// WebApplicationFactory for the full tenant-provisioning journey. Mirrors
/// AdminTestFactory (test database) and additionally:
/// - sets Tenancy:RootDomain so host-based tenant resolution works in tests, and
/// - swaps IEmailService for a capturing fake so the invite token can be read.
/// </summary>
public class E2ETestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly CapturingEmailService _email;

    public E2ETestFactory(string connectionString, CapturingEmailService email)
    {
        _connectionString = connectionString;
        _email = email;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // These values only reach post-Build() config consumers. Program.cs's pre-Build()
        // ConfigurationStartupValidator/DatabaseConnectionStartupValidator run before
        // ConfigureWebHost is ever applied, so TenantProvisioningE2ETests.InitializeAsync must put
        // the same values in process environment variables via IntegrationTestEnvironmentScope
        // before constructing this factory - this callback cannot supply them in time.
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = AdminTestFactory.TenantSecret,
                ["Jwt:TenantIssuer"] = AdminTestFactory.TenantIssuer,
                ["Jwt:TenantAudience"] = AdminTestFactory.TenantAudience,
                ["Tenancy:RootDomain"] = "localhost",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                // Bootstrap the canonical platform_users row the DevAdmin login resolves.
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "E2E Test Super Admin",
                // Outbox payloads are AES-encrypted at rest; tests need a key too.
                ["Encryption:MasterKey"] = "e2e-test-encryption-master-key-!!",
                // Fast outbox polling so the invite email lands quickly in tests.
                ["Outbox:PollSeconds"] = "1"
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

            services.RemoveAll(typeof(IEmailService));
            services.AddSingleton<IEmailService>(_email);
        });
    }
}
