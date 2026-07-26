using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Bootstraps a Testcontainers-backed database before any WebApplicationFactory&lt;Program&gt; is
/// touched: creates the privileged roles Program.cs's startup validators and production migrations
/// assume exist, then applies EF migrations through a standalone ApplicationDbContext. Accessing
/// _factory.Services/CreateClient() starts hosted services (PermissionSeeder,
/// DevSmokeTestTenantSeeder, etc.) synchronously during host startup, which query tables that must
/// already exist - this must run first, against the admin/superuser connection, exactly like every
/// existing MigrateDatabaseAsync helper in this suite.
/// </summary>
public static class IntegrationDatabaseBootstrap
{
    public static async Task InitializeAsync(string adminConnectionString, CancellationToken ct = default)
    {
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString, ct);

        var migrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var dateTimeProvider = new SystemDateTimeProvider();
        await using var migrationContext = new ApplicationDbContext(
            migrationOptions,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
        await migrationContext.Database.MigrateAsync(ct);
    }
}
