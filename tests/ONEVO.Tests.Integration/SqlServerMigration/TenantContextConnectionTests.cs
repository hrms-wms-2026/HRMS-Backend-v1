using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using Xunit;

namespace ONEVO.Tests.SqlServer;

[Collection(SqlServerCollection.Name)]
public sealed class TenantContextConnectionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await fixture.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static TenantRegistryEntry Entry(Guid tenant) => new(tenant, "t", TenantStatus.Active, null);

    private (ApplicationDbContext Db, TenantContextAccessor Tenant, TenantRlsInterceptor Rls) Create(
        bool retry = false, params IInterceptor[] extra)
    {
        var tenant = new TenantContextAccessor();
        var rls = new TenantRlsInterceptor(tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_database.App, sql =>
            {
                if (retry)
                {
                    sql.EnableRetryOnFailure(3);
                }
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(rls)
            .AddInterceptors(extra)
            .Options;
        var clock = new SystemDateTimeProvider();
        var db = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenant);
        return (db, tenant, rls);
    }

    private static async Task<string?> ReadContextAsync(ApplicationDbContext db, string key)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT CAST(SESSION_CONTEXT(N'{key}') AS nvarchar(200))";
        return await command.ExecuteScalarAsync() as string;
    }

    [Fact]
    public async Task Open_SetsContext()
    {
        var (db, tenant, _) = Create();
        tenant.Resolve(Entry(TenantA));
        await db.Database.OpenConnectionAsync();

        Assert.Equal(TenantA.ToString(), await ReadContextAsync(db, "app.current_tenant_id"));
        Assert.Equal("tenant", await ReadContextAsync(db, "app.tenant_context_mode"));
    }

    [Fact]
    public async Task RollbackAfterSwitch_DoesNotKeepSwitchedTenant()
    {
        var (db, tenant, rls) = Create();
        tenant.Resolve(Entry(TenantA));
        await db.Database.OpenConnectionAsync();
        var switcher = new TenantContextSwitcher(db, tenant, rls);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await switcher.SwitchToTenantAsync(Entry(TenantB));
            Assert.Equal(TenantB.ToString(), await ReadContextAsync(db, "app.current_tenant_id"));
            await transaction.RollbackAsync();
        }

        Assert.Equal(TenantA.ToString(), await ReadContextAsync(db, "app.current_tenant_id"));
    }

    [Fact]
    public async Task RollbackOfALaterTransaction_DoesNotRestoreAnEarlierDisposedSwitch()
    {
        var (db, tenant, rls) = Create();
        tenant.Resolve(Entry(TenantA));
        await db.Database.OpenConnectionAsync();
        var switcher = new TenantContextSwitcher(db, tenant, rls);

        await using (await db.Database.BeginTransactionAsync())
        {
            await switcher.SwitchToTenantAsync(Entry(TenantB));
        }

        await using (var later = await db.Database.BeginTransactionAsync())
        {
            await later.RollbackAsync();
        }

        Assert.Equal(TenantB.ToString(), await ReadContextAsync(db, "app.current_tenant_id"));
    }

    [Fact]
    public async Task CommitAfterSwitch_KeepsSwitchedTenant()
    {
        var (db, tenant, rls) = Create();
        tenant.Resolve(Entry(TenantA));
        await db.Database.OpenConnectionAsync();
        var switcher = new TenantContextSwitcher(db, tenant, rls);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await switcher.SwitchToTenantAsync(Entry(TenantB));
            await transaction.CommitAsync();
        }

        Assert.Equal(TenantB.ToString(), await ReadContextAsync(db, "app.current_tenant_id"));
    }

    [Fact]
    public async Task RetryAfterTransientFailure_ReappliesContext()
    {
        var (db, tenant, _) = Create(retry: true);
        tenant.Resolve(Entry(TenantA));
        await db.Database.OpenConnectionAsync();
        var spid = await db.Database.SqlQueryRaw<short>("SELECT @@SPID AS [Value]").FirstAsync();
        await db.Database.CloseConnectionAsync();

        await using (var admin = new SqlConnection(_database.Admin))
        {
            await admin.OpenAsync();
            await using var kill = admin.CreateCommand();
            kill.CommandText = $"KILL {spid}";
            await kill.ExecuteNonQueryAsync();
        }

        var value = await db.Database
            .SqlQueryRaw<string>("SELECT CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36)) AS [Value]")
            .FirstAsync();

        Assert.Equal(TenantA.ToString(), value);
    }

    private sealed class FailOnSessionsRead : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => command.CommandText.Contains("[sessions]", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated failure.")
                : ValueTask.FromResult(result);
    }

    [Fact]
    public async Task SessionLookup_LeavesNoKey()
    {
        var (db, _, _) = Create();
        await db.Database.OpenConnectionAsync();
        await new EfSessionRepository(db).GetByKeyHashForTenantResolutionAsync("not-a-key");

        Assert.Null(await ReadContextAsync(db, "app.session_lookup_key_hash"));
    }

    [Fact]
    public async Task FailedSessionLookup_LeavesNoKey()
    {
        var (db, _, _) = Create(extra: new FailOnSessionsRead());
        await db.Database.OpenConnectionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new EfSessionRepository(db).GetByKeyHashForTenantResolutionAsync("not-a-key"));

        Assert.Null(await ReadContextAsync(db, "app.session_lookup_key_hash"));
    }

    private async Task ExecuteAsAdminAsync(string sql)
    {
        await using var admin = new SqlConnection(_database.Admin);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Validator_AcceptsCorrectDatabase()
        => await DatabaseSettingsStartupValidator.ValidateAsync(_database.App, CancellationToken.None);

    [Fact]
    public async Task Validator_RefusesWrongMaxdop()
    {
        await ExecuteAsAdminAsync("ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 0");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSettingsStartupValidator.ValidateAsync(_database.App, CancellationToken.None));
        await ExecuteAsAdminAsync("ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1");

        Assert.Contains("MAXDOP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validator_RefusesDbOwnerAppLogin()
    {
        await ExecuteAsAdminAsync("ALTER ROLE db_owner ADD MEMBER onevo_app");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSettingsStartupValidator.ValidateAsync(_database.App, CancellationToken.None));
        await ExecuteAsAdminAsync("ALTER ROLE db_owner DROP MEMBER onevo_app");

        Assert.Contains("db_owner", error.Message, StringComparison.Ordinal);
    }
}
