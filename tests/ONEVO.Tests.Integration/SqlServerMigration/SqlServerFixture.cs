using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Testcontainers.MsSql;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>One container per run; each caller gets its own database, set up as ops/sqlserver does.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string AppPassword = "Fixture_App_Pw_12345!";
    public const string MigratorPassword = "Fixture_Mig_Pw_12345!";
    private MsSqlContainer _container = null!;

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
        var name = $"crownus_{Guid.NewGuid():N}";
        await RunScriptAsync("bootstrap-logins.sql", name);
        var database = new TestDatabase(MasterConnection, name);
        if (migrate)
        {
            await using var context = database.CreateMigratorContext();
            await context.Database.MigrateAsync();
            await RunScriptAsync("post-migration-grants.sql", name);
        }

        return database;
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
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class TestDatabase(string masterConnection, string name)
{
    public string Name { get; } = name;

    public string Admin => Connection(null, null);
    public string App => Connection("onevo_app", SqlServerFixture.AppPassword);
    public string Migrator => Connection("onevo_migrator", SqlServerFixture.MigratorPassword);

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
