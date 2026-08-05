using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Monitoring.ActivityMonitoring;

public sealed class ActivityMonitoringTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ActivityMonitoringTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Secret"] = "activity-test-jwt-secret-min-32-chars-long!",
                ["Jwt:TenantIssuer"] = "onevo-api",
                ["Jwt:TenantAudience"] = "onevo-api",
                ["TrayApp:Jwt:Secret"] = "activity-test-tray-jwt-secret-min-32chars!",
                ["TrayApp:Jwt:Issuer"] = "onevo-tray",
                ["TrayApp:Jwt:Audience"] = "onevo-tray-app",
                ["Tenancy:RootDomain"] = "localhost",
                ["Urls:AppBaseUrl"] = "https://localhost",
                ["Encryption:MasterKey"] = "activity-test-master-key-32-chars-xxxx!",
                ["DevAdmin:Email"] = "test_admin@onevo.dev",
                ["DevAdmin:Password"] = "test_password_123",
                ["PlatformBootstrap:SuperAdminEmail"] = "test_admin@onevo.dev",
                ["PlatformBootstrap:SuperAdminFullName"] = "Activity Test Super Admin"
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

