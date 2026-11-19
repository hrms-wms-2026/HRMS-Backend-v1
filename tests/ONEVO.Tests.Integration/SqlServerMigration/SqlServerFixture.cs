using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;
using Testcontainers.MsSql;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>One container per run; each caller gets its own database, set up as ops/sqlserver does.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string AppPassword = "Fixture_App_Pw_12345!";
    public const string MigratorPassword = "Fixture_Mig_Pw_12345!";
    private MsSqlContainer _container = null!;
    private TestDatabaseReaper? _reaper;

    public string MasterConnection { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        MasterConnection = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<TestDatabase> CreateDatabaseAsync(bool migrate = true)
    {
        _reaper ??= new TestDatabaseReaper(MasterConnection);
        await _reaper.DropFinishedAsync();
        var name = $"crownus_{Guid.NewGuid():N}";
        await RunScriptAsync("bootstrap-logins.sql", name);
        _reaper.Track(name);
        await CompactAsync(name);
        var database = new TestDatabase(MasterConnection, name);
        if (migrate)
        {
            await using var context = database.CreateMigratorContext();
            await context.Database.MigrateAsync();
            await RunScriptAsync("post-migration-grants.sql", name);
        }

        return database;
    }

    /// <summary>Each database is created and migrated from scratch: without this its migration log
    /// and 64 MB growth steps make it ~85 MB, and a run's 90 of them fill a CI runner's disk.</summary>
    private async Task CompactAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        string logicalData, logicalLog;
        await using (var files = connection.CreateCommand())
        {
            files.CommandText = $"""
                SELECT MAX(CASE WHEN type = 0 THEN name END), MAX(CASE WHEN type = 1 THEN name END)
                FROM sys.master_files WHERE database_id = DB_ID(N'{databaseName}')
                """;
            await using var reader = await files.ExecuteReaderAsync();
            await reader.ReadAsync();
            (logicalData, logicalLog) = (reader.GetString(0), reader.GetString(1));
        }

        await using var compact = connection.CreateCommand();
        compact.CommandTimeout = 300;
        compact.CommandText = TestDatabaseReaper.CompactSettingsSql(databaseName, logicalData, logicalLog);
        await compact.ExecuteNonQueryAsync();
    }

    private async Task RunScriptAsync(string fileName, string databaseName)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ops", fileName));
        script = script
            .Replace("$(DB_NAME)", databaseName)
            .Replace("$(APP_PASSWORD)", AppPassword)
            .Replace("$(MIGRATOR_PASSWORD)", MigratorPassword);
        await using var connection = new SqlConnection(MasterConnection);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 300; // CREATE DATABASE competes with the whole suite's restores.
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class TestDatabase(string masterConnection, string name)
{
    public string Name { get; } = name;

    public string Admin => Connection(null, null);
    public string App => Connection("crownus_app", SqlServerFixture.AppPassword);
    public string Migrator => Connection("crownus_migrator", SqlServerFixture.MigratorPassword);

    public ApplicationDbContext CreateMigratorContext() => CreateContext(Migrator);

    public ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        var clock = new SystemDateTimeProvider();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private string Connection(string? user, string? password)
    {
        var builder = new SqlConnectionStringBuilder(masterConnection) { InitialCatalog = Name };
        if (user is not null)
        {
            builder.UserID = user;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
