using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Auth;

public sealed class BaseDomainLoginTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public BaseDomainLoginTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // These values only reach post-Build() config consumers (e.g. the /health/ready postgres
        // check reading builder.Configuration.GetConnectionString). Program.cs's pre-Build()
        // ConfigurationStartupValidator/DatabaseConnectionStartupValidator run before
        // ConfigureWebHost is ever applied, so BaseDomainLoginIntegrationTests.InitializeAsync must
        // put the same values in process environment variables via IntegrationTestEnvironmentScope
        // before this factory is constructed - this callback cannot supply them in time.
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "test_secret_at_least_32_chars_long_!!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                // Host-based tenant resolution rejects unknown hosts with 400; the test client
                // calls https://localhost for base-domain requests and https://{slug}.localhost
                // for direct tenant-host requests, mirroring E2ETestFactory/TenantProvisioningE2ETests.
                ["Tenancy:RootDomain"] = "localhost",
                ["Encryption:MasterKey"] = "base-login-test-encryption-master-key!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                // Bootstrap the canonical platform_users row the DevAdmin login resolves.
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Base Login Test Super Admin"
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

            services.RemoveAll<ITotpService>();
            services.AddSingleton<ITotpService, AlwaysValidTotpService>();
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.AddSingleton<IGoogleIdTokenValidator, EmailAsGoogleTokenValidator>();
            services.RemoveAll<IPlatformOAuthAppResolver>();
            services.AddSingleton<IPlatformOAuthAppResolver, TestPlatformOAuthAppResolver>();
        });
    }

    private sealed class AlwaysValidTotpService : ITotpService
    {
        public bool Verify(string base32Secret, string code) => code == "123456";
    }

    private sealed class EmailAsGoogleTokenValidator : IGoogleIdTokenValidator
    {
        public Task<GoogleIdTokenPayload?> ValidateAsync(
            string idToken,
            string expectedAudience,
            CancellationToken ct = default)
        {
            return Task.FromResult<GoogleIdTokenPayload?>(
                new GoogleIdTokenPayload(idToken, idToken, true, "Google Test User"));
        }
    }

    private sealed class TestPlatformOAuthAppResolver : IPlatformOAuthAppResolver
    {
        public Task<ResolvedPlatformOAuthApp?> GetActiveAppForProviderAsync(
            string provider,
            CancellationToken ct = default)
        {
            return Task.FromResult<ResolvedPlatformOAuthApp?>(
                new ResolvedPlatformOAuthApp(
                    provider,
                    "test-google-client",
                    "https://accounts.google.com/o/oauth2/v2/auth",
                    "https://oauth2.googleapis.com/token",
                    []));
        }

        public Task<ResolvedPlatformOAuthAppCredential?> GetActiveCredentialForProviderAsync(
            string provider,
            CancellationToken ct = default)
        {
            return Task.FromResult<ResolvedPlatformOAuthAppCredential?>(null);
        }
    }
}
