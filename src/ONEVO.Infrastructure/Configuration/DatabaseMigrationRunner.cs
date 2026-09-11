using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Infrastructure.Configuration;

/// <summary>
/// Applies any pending EF Core migrations at API startup so a freshly cloned/reset local
/// database does not require a separate manual `dotnet ef database update` step. Builds its
/// own short-lived ApplicationDbContext against MigrationConnection (the elevated
/// onevo_migrator role) - never the app's DI-registered runtime context, which uses the
/// restricted onevo_app role and intentionally lacks DDL rights. Mirrors
/// ApplicationDbContextFactory and IntegrationDatabaseBootstrap's own migration context setup.
///
/// Caller must gate this to Development only: Test/Testing bootstrap their own Testcontainers
/// database via IntegrationDatabaseBootstrap before the host is even built, and
/// Production/Staging must apply migrations through their own deployment pipeline, never
/// automatically on process start.
/// </summary>
public static class DatabaseMigrationRunner
{
    public static async Task MigrateIfPendingAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("MigrationConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'MigrationConnection' not found. Run " +
                "ops/postgres/setup-local-db.ps1 from the repository root before starting the API.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        var dateTimeProvider = new SystemDateTimeProvider();
        await using var migrationContext = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor()); // System mode - no tenant filter during migrations

        var pending = (await migrationContext.Database
            .GetPendingMigrationsAsync(cancellationToken))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        Console.WriteLine(
            $"[MIGRATIONS] Applying {pending.Count} pending migration(s): {string.Join(", ", pending)}");
        await migrationContext.Database.MigrateAsync(cancellationToken);
        Console.WriteLine("[MIGRATIONS] Database schema is up to date.");
    }
}
