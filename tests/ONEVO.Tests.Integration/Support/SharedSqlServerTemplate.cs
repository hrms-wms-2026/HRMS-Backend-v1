using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Starts ONE SQL Server container and migrates a template database once, lazily, exactly as a real
/// server is set up: ops/sqlserver bootstrap, migrations as onevo_migrator, then grants. Each test
/// class gets its own database by restoring a backup of that template, so classes never see each
/// other's data, and migrations run once per test run instead of once per class.
///
/// The logins are server-wide, and a restore keeps the database's users and settings (collation,
/// READ_COMMITTED_SNAPSHOT, MAXDOP), so every clone is ready to use as it comes.
///
/// The container is never disposed: it lives for the test process and Testcontainers' Ryuk reaper
/// removes it when the process exits.
/// </summary>
public static class SharedSqlServerTemplate
{
    private const string TemplateDatabaseName = "onevo_integration_template";
    private const string BackupPath = "/var/opt/mssql/data/onevo_template.bak";
    private const string DataDirectory = "/var/opt/mssql/data/";

    private static readonly Lazy<Task<TemplateServer>> Template = new(InitializeTemplateAsync);

    // Restores are disk-bound; unbounded concurrency only makes each one slower.
    private static readonly SemaphoreSlim RestoreGate = new(4, 4);

    private sealed record TemplateServer(
        string MasterConnectionString, string LogicalData, string LogicalLog, TestDatabaseReaper Reaper);

    /// <summary>
    /// Returns an admin connection string to a fresh, fully migrated, isolated database. Open it with
    /// TestDb so the session has admin context: sa is not exempt from RLS on SQL Server.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        var template = await Template.Value;
        var databaseName = $"onevo_it_{Guid.NewGuid():N}";
        await template.Reaper.DropFinishedAsync();

        await RestoreGate.WaitAsync(ct);
        try
        {
            await using var connection = new SqlConnection(template.MasterConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 300;
            // Identifiers cannot be parameters. databaseName is our own Guid-derived value.
            command.CommandText = $"""
                RESTORE DATABASE [{databaseName}] FROM DISK = N'{BackupPath}'
                WITH MOVE N'{template.LogicalData}' TO N'{DataDirectory}{databaseName}.mdf',
                     MOVE N'{template.LogicalLog}' TO N'{DataDirectory}{databaseName}.ldf',
                     RECOVERY, NOUNLOAD, STATS = 100;
                """;
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            RestoreGate.Release();
        }

        template.Reaper.Track(databaseName);
        return new SqlConnectionStringBuilder(template.MasterConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    private static async Task<TemplateServer> InitializeTemplateAsync()
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync();

        var master = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        }.ConnectionString;

        await RunOpsScriptAsync(master, "bootstrap-logins.sql");

        var migrator = new SqlConnectionStringBuilder(master)
        {
            InitialCatalog = TemplateDatabaseName,
            UserID = "onevo_migrator",
            Password = PrivilegedRoleTestBootstrap.MigratorRolePassword,
        }.ConnectionString;
        var clock = new SystemDateTimeProvider();
        await using (var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(migrator)
                .UseSnakeCaseNamingConvention()
                .Options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor()))
        {
            await context.Database.MigrateAsync();
        }

        await RunOpsScriptAsync(master, "post-migration-grants.sql");

        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        string logicalData, logicalLog;
        await using (var files = connection.CreateCommand())
        {
            files.CommandText = $"""
                SELECT MAX(CASE WHEN type = 0 THEN name END), MAX(CASE WHEN type = 1 THEN name END)
                FROM sys.master_files WHERE database_id = DB_ID(N'{TemplateDatabaseName}')
                """;
            await using var reader = await files.ExecuteReaderAsync();
            await reader.ReadAsync();
            (logicalData, logicalLog) = (reader.GetString(0), reader.GetString(1));
        }

        // A restore recreates the files at the template's size, and migrations leave a ~72 MB log:
        // shrink before the backup so every copy starts at about 10 MB (see TestDatabaseReaper).
        await using (var shrink = connection.CreateCommand())
        {
            shrink.CommandTimeout = 300;
            shrink.CommandText = $"""
                {TestDatabaseReaper.CompactSettingsSql(TemplateDatabaseName, logicalData, logicalLog)}
                USE [{TemplateDatabaseName}];
                CHECKPOINT;
                DBCC SHRINKFILE (N'{logicalLog}', 1) WITH NO_INFOMSGS;
                DBCC SHRINKFILE (N'{logicalData}', 1) WITH NO_INFOMSGS;
                USE [master];
                """;
            await shrink.ExecuteNonQueryAsync();
        }

        await using var backup = connection.CreateCommand();
        backup.CommandTimeout = 300;
        backup.CommandText = $"BACKUP DATABASE [{TemplateDatabaseName}] TO DISK = N'{BackupPath}' WITH INIT, COPY_ONLY;";
        await backup.ExecuteNonQueryAsync();

        return new TemplateServer(master, logicalData, logicalLog, new TestDatabaseReaper(master));
    }

    private static async Task RunOpsScriptAsync(string masterConnectionString, string fileName)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ops", fileName));
        script = script
            .Replace("$(DB_NAME)", TemplateDatabaseName)
            .Replace("$(APP_PASSWORD)", PrivilegedRoleTestBootstrap.AppRolePassword)
            .Replace("$(MIGRATOR_PASSWORD)", PrivilegedRoleTestBootstrap.MigratorRolePassword);

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }
}
