using Microsoft.EntityFrameworkCore;
using Npgsql;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Historically, every Testcontainers-backed test class in this suite started its own Postgres
/// container and ran the full EF migration chain (150+ migrations) from scratch in its
/// InitializeAsync. With ~74 such classes, that meant ~74 redundant container boots and ~74
/// redundant full migration runs - the dominant cost in the suite's wall-clock time, far more than
/// the actual test assertions.
///
/// This type starts ONE Postgres container and runs the migration chain into a template database
/// exactly once, lazily, on first use (thread-safe via Lazy&lt;Task&lt;T&gt;&gt; - concurrent first
/// callers await the same in-flight initialization rather than racing to start N containers).
/// Every test class then asks for its own isolated database via CreateDatabaseAsync, which clones
/// the already-migrated template using PostgreSQL's `CREATE DATABASE ... TEMPLATE`, a filesystem-
/// level copy that completes in well under a second - versus re-running 150+ migrations.
///
/// Each cloned database is fully independent (its own schema, data, and per-database grants/ACLs
/// copied verbatim from the template at clone time), so this preserves the exact isolation
/// guarantees every existing test already depends on: two test classes cloning concurrently never
/// see each other's data, and a class that GRANTs extra privileges or creates extra roles after
/// cloning affects only its own database (roles themselves are cluster-scoped in PostgreSQL, so
/// role-creation statements like PrivilegedRoleTestBootstrap's are naturally idempotent across
/// clones and only need to run once, during template setup).
///
/// The template database itself is never a connection target for any test - only ever a
/// `CREATE DATABASE ... TEMPLATE` source - because PostgreSQL refuses to clone a database that has
/// any active session. The pool for the template's own connection string is explicitly cleared
/// after migrating into it, since Npgsql pools connections past DisposeAsync/using-block exit.
///
/// The shared container is intentionally never disposed: it is a static, process-lifetime resource
/// reused by every test class in the run, and Testcontainers' Ryuk reaper (the same safety net
/// every other container in this suite already relies on if DisposeAsync is skipped, e.g. on a
/// crashed test run) cleans it up when the test process exits.
/// </summary>
public static class SharedPostgresTemplate
{
    private const string TemplateDatabaseName = "onevo_integration_template";
    private const string MaintenanceDatabaseName = "postgres";
    private const string SuperuserName = "test";
    private const string SuperuserPassword = "test";

    private static readonly Lazy<Task<string>> TemplateAdminConnectionString = new(InitializeTemplateAsync);

    /// <summary>
    /// Returns an admin/superuser connection string to a fresh, fully-migrated, isolated database
    /// cloned from the shared template. Safe to call concurrently from many test classes.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        var templateAdminConnectionString = await TemplateAdminConnectionString.Value;
        var databaseName = $"onevo_it_{Guid.NewGuid():N}";

        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(templateAdminConnectionString)
        {
            Database = MaintenanceDatabaseName
        };

        await using (var connection = new NpgsqlConnection(maintenanceBuilder.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            // Identifiers can't be parameterized. databaseName is our own Guid-derived value
            // (never external input) and TemplateDatabaseName/SuperuserName are compile-time
            // constants, so string interpolation here is safe.
            command.CommandText =
                $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabaseName}\" OWNER \"{SuperuserName}\";";
            await command.ExecuteNonQueryAsync(ct);
        }

        var cloneBuilder = new NpgsqlConnectionStringBuilder(templateAdminConnectionString)
        {
            Database = databaseName
            // Deliberately not capping MaxPoolSize here: a low cap (previously 20) throttled
            // pool-hungry classes under real concurrency and made the full run slower, not
            // faster - measured at 99 minutes, worse than the pre-conversion baseline. Npgsql's
            // own default (100) matches what every class effectively had available on its own
            // dedicated server before this change; max_connections below is raised generously so
            // several classes at their default pool size can run concurrently without exhausting
            // the shared server's connection ceiling.
        };
        return cloneBuilder.ConnectionString;
    }

    private static async Task<string> InitializeTemplateAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(TemplateDatabaseName)
            .WithUsername(SuperuserName)
            .WithPassword(SuperuserPassword)
            // Default Postgres max_connections (100) is sized for one container serving one test
            // class, not one container shared by every class in the run, each with its own
            // default-sized (100) Npgsql pool. Raised generously so real concurrency across many
            // classes doesn't exhaust the shared server's connection ceiling.
            .WithCommand("-c", "max_connections=2000")
            .Build();
        await postgres.StartAsync();

        var adminConnectionString = postgres.GetConnectionString();

        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString);

        var migrationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var dateTimeProvider = new SystemDateTimeProvider();
        await using (var migrationContext = new ApplicationDbContext(
            migrationOptions,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor()))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Production grants onevo_app access to every table via
        // "ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator ... TO onevo_app" (see
        // ops/postgres/local-bootstrap-roles.sql), which fires automatically for every object
        // onevo_migrator creates - including EF's own auto-created __EFMigrationsHistory table.
        // This suite migrates as the superuser instead of onevo_migrator (see the class doc
        // above), so neither role naturally owns/can-access anything: onevo_app never gets the
        // default-privileges rule (it only fires for objects onevo_migrator itself creates), and
        // onevo_migrator never gets ownership at all, since it never ran a single migration here.
        // DatabaseMigrationRunner.MigrateIfPendingAsync (Program.cs, non-Test environments) opens
        // __EFMigrationsHistory specifically as onevo_migrator (ConnectionStrings:MigrationConnection
        // - see IntegrationTestEnvironmentScope), which is what actually surfaced this: EF creates
        // that table itself, so no hand-written migration exists to carry an inline GRANT for it,
        // the way a few business tables' migrations do for onevo_app. Grant both roles everything
        // that exists at this point, once, directly - equivalent in effect to what production's
        // real deploy-time role setup achieves, since migrations have already finished.
        await using (var grantConnection = new NpgsqlConnection(adminConnectionString))
        {
            await grantConnection.OpenAsync();
            await using var grantCommand = grantConnection.CreateCommand();
            grantCommand.CommandText = """
                GRANT CREATE, USAGE ON SCHEMA public TO onevo_migrator;
                GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO onevo_migrator;
                GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO onevo_migrator;
                GRANT onevo_auth_base_login_fn_owner TO onevo_migrator;

                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO onevo_app;
                """;
            await grantCommand.ExecuteNonQueryAsync();
        }

        // CREATE DATABASE ... TEMPLATE fails with "source database is being accessed by other
        // users" if any session is still open against it. Npgsql pools connections past
        // DisposeAsync/using-block exit, so the pool backing this exact connection string must be
        // cleared explicitly - otherwise the very first CreateDatabaseAsync clone would race the
        // pooled migration connection and fail intermittently.
        await using (var pooledConnection = new NpgsqlConnection(adminConnectionString))
        {
            NpgsqlConnection.ClearPool(pooledConnection);
        }

        return adminConnectionString;
    }
}
