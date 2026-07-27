using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ONEVO.Tests.Integration.Auth;

/// <summary>
/// WebApplicationFactory for forgot-password HTTP tests that must prove the real onevo_app
/// runtime role + TenantRlsInterceptor combination end to end. Deliberately does NOT override
/// ApplicationDbContext's registration in ConfigureServices - unlike BaseDomainLoginTestFactory
/// and E2ETestFactory (which strip out AddInfrastructure's DbContext registration and rebind
/// ApplicationDbContext directly to whatever connection string the test passes in, without
/// TenantRlsInterceptor - see the identical warning on BaseForgotPasswordRlsIntegrationTests and
/// TenantSessionRlsIntegrationTests), this factory lets Program.cs's own
/// ONEVO.Infrastructure.DependencyInjection.AddInfrastructure(...) wire ApplicationDbContext
/// exactly like production does: the onevo_app connection string, with TenantRlsInterceptor.
///
/// AddInfrastructure reads configuration.GetConnectionString("DefaultConnection") eagerly, as part
/// of Program.cs's top-level statements, before WebApplicationFactory.ConfigureWebHost is ever
/// applied - so the ConfigureAppConfiguration override below only reaches post-Build() config
/// consumers (e.g. the /health/ready postgres check), never AddInfrastructure's own DbContext
/// wiring. The caller MUST therefore set the ConnectionStrings__DefaultConnection process
/// environment variable to the onevo_app connection string (IntegrationTestEnvironmentScope,
/// constructed with the admin connection string, derives this automatically) BEFORE constructing
/// this factory, and pass that same onevo_app connection string into this constructor.
///
/// Requires Docker.
/// </summary>
public sealed class BaseForgotPasswordRestrictedRoleTestFactory : WebApplicationFactory<Program>
{
    private readonly string _appConnectionString;

    public BaseForgotPasswordRestrictedRoleTestFactory(string appConnectionString)
        => _appConnectionString = appConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _appConnectionString,
                ["Jwt:Secret"] = "forgot-password-restricted-role-test-jwt-secret-32c!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["Tenancy:RootDomain"] = "localhost",
                ["Encryption:MasterKey"] = "forgot-password-restricted-role-test-master-key!!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Forgot Password Restricted Role Test Super Admin"
            });
        });

        // Deliberately no service overrides here. ApplicationDbContext must remain exactly what
        // Program.cs's AddInfrastructure(builder.Configuration) registers in production: the
        // onevo_app connection string (supplied via the process environment variable set before
        // this factory was constructed) with TenantRlsInterceptor wired.
    }
}
