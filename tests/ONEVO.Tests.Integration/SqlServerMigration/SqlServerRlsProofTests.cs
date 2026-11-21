using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>One database with constraints off, so a row can be made in any table.</summary>
public sealed class RlsProofDatabase : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public RlsProofDatabase(SqlServerFixture fixture) => _fixture = fixture;

    public TestDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Database = await _fixture.CreateDatabaseAsync();
        await using var admin = new SqlConnection(Database.Admin);
        await admin.OpenAsync();
        await RowFactory.DisableConstraintsAsync(admin);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerRlsCrossTenantTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public static TheoryData<string> Tables()
    {
        var data = new TheoryData<string>();
        foreach (var table in RlsTableRegistry.Tables.Keys)
        {
            data.Add(table);
        }

        return data;
    }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static TestDatabase? _shared;

    public async Task InitializeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_shared is null)
            {
                var proof = new RlsProofDatabase(fixture);
                await proof.InitializeAsync();
                _shared = proof.Database;
            }

            _database = _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<SqlConnection> ConnectAsync(string connectionString, string mode, Guid? tenant)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, tenant?.ToString() ?? "", mode);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(SqlConnection connection, string table, string where = "1 = 1")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE {where}";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // Audit tables the runtime login may not update or delete at all (post-migration-grants.sql, PA-SEC-002).
    private static readonly HashSet<string> AppendOnlyTables = ["audit_logs", "billing_audit_logs"];

    private static async Task AssertDeniedOrNoRowsAsync(SqlConnection app, string table, string sql)
    {
        if (!AppendOnlyTables.Contains(table))
        {
            Assert.Equal(0, await ExecuteAsync(app, sql));
            return;
        }

        var denied = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(app, sql));
        Assert.Equal(229, denied.Number);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task EveryTable_BlocksCrossTenantAccess(string table)
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var tenantColumn = new Dictionary<string, object?> { ["tenant_id"] = a };

        await using (var seedA = await ConnectAsync(_database.Admin, "tenant", a))
        {
            await RowFactory.InsertAsync(seedA, table, tenantColumn);
        }

        await using (var seedB = await ConnectAsync(_database.Admin, "tenant", b))
        {
            await RowFactory.InsertAsync(seedB, table, new Dictionary<string, object?> { ["tenant_id"] = b });
        }

        await using var app = await ConnectAsync(_database.App, "tenant", a);

        Assert.True(await CountAsync(app, table, $"tenant_id = '{a}'") >= 1, "Tenant A cannot see its own row.");
        Assert.Equal(0, await CountAsync(app, table, $"tenant_id = '{b}'"));
        await AssertDeniedOrNoRowsAsync(app, table, $"UPDATE [{table}] SET tenant_id = tenant_id WHERE tenant_id = '{b}'");
        await AssertDeniedOrNoRowsAsync(app, table, $"DELETE FROM [{table}] WHERE tenant_id = '{b}'");

        // A third tenant: on tables keyed by tenant_id, B's own row would raise a duplicate key first.
        var c = Guid.NewGuid();
        var insert = await Assert.ThrowsAsync<SqlException>(() => RowFactory.InsertAsync(
            app, table, new Dictionary<string, object?> { ["tenant_id"] = c }));
        Assert.Equal(33504, insert.Number);

        var move = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            app, $"UPDATE [{table}] SET tenant_id = '{c}' WHERE tenant_id = '{a}'"));
        Assert.Equal(AppendOnlyTables.Contains(table) ? 229 : 33504, move.Number);

        await using var none = new SqlConnection(_database.App);
        await none.OpenAsync();
        Assert.Equal(0, await CountAsync(none, table));

        await using var rowB = await ConnectAsync(_database.Admin, "tenant", b);
        Assert.Equal(1, await CountAsync(rowB, table, $"tenant_id = '{b}'"));
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerRlsCoverageTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task EveryCoveredTable_HasAFilterAndFourBlockPredicates()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using var connection = new SqlConnection(database.Admin);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(pr.target_object_id), pr.predicate_type_desc, ISNULL(pr.operation_desc, '')
            FROM sys.security_policies p
            JOIN sys.security_predicates pr ON pr.object_id = p.object_id
            WHERE p.is_enabled = 1 AND p.is_schema_bound = 1
            """;
        var seen = new Dictionary<string, HashSet<string>>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (!seen.TryGetValue(table, out var predicates))
                {
                    seen[table] = predicates = [];
                }

                predicates.Add($"{reader.GetString(1)} {reader.GetString(2)}".Trim());
            }
        }

        string[] required = ["FILTER", "BLOCK AFTER INSERT", "BLOCK AFTER UPDATE", "BLOCK BEFORE UPDATE", "BLOCK BEFORE DELETE"];
        var incomplete = RlsTableRegistry.Tables.Keys
            .Where(table => !seen.TryGetValue(table, out var predicates) || required.Except(predicates).Any())
            .ToList();

        Assert.Empty(incomplete);
    }

    [Fact]
    public async Task EveryModelTenantTable_IsCovered()
    {
        var database = await fixture.CreateDatabaseAsync(migrate: false);
        await using var context = database.CreateMigratorContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var tenantTables = model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null && entity.FindProperty("TenantId") is not null)
            .Select(entity => entity.GetTableName()!)
            .ToHashSet();

        Assert.Empty(tenantTables.Except(RlsTableRegistry.Tables.Keys));
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerAppLoginRightsTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await fixture.CreateDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(_database.App);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AppLogin_IsNotDbOwner()
        => Assert.Equal(0, await ScalarAsync("SELECT IS_ROLEMEMBER('db_owner')"));

    [Theory]
    [InlineData("ALTER SECURITY POLICY rls.policy_tasks WITH (STATE = OFF)")]
    [InlineData("DROP SECURITY POLICY rls.policy_tasks")]
    [InlineData("ALTER TABLE tasks ADD extra int NULL")]
    [InlineData("CREATE TABLE evil (id int)")]
    [InlineData("DROP TABLE tasks")]
    [InlineData("EXECUTE AS USER = 'crownus_auth_base_login_fn_owner'")]
    [InlineData("EXECUTE AS USER = 'dbo'")]
    [InlineData("SELECT * FROM rls.fn_tenant(NEWID())")]
    [InlineData("ALTER ROLE db_owner ADD MEMBER crownus_app")]
    public async Task AppLogin_CannotDo(string sql)
    {
        await using var connection = new SqlConnection(_database.App);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task AppLogin_CanExecuteOnlyTheLookupProcedure()
    {
        await using var connection = new SqlConnection(_database.App);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SCHEMA_NAME(o.schema_id) + '.' + o.name
            FROM sys.objects o
            WHERE o.type = 'P' AND HAS_PERMS_BY_NAME(SCHEMA_NAME(o.schema_id) + '.' + o.name, 'OBJECT', 'EXECUTE') = 1
            """;
        var procedures = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                procedures.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["auth_internal.auth_lookup_base_login_candidates"], procedures);
    }

    [Fact]
    public async Task DatabaseSettings_AreSafe()
    {
        Assert.Equal(1, await ScalarAsync(
            "SELECT CAST(value AS int) FROM sys.database_scoped_configurations WHERE name = 'MAXDOP'"));
        Assert.Equal(1, await ScalarAsync(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()"));
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerConnectionIsolationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private TestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        await using var admin = new SqlConnection(_database.Admin);
        await admin.OpenAsync();
        await RowFactory.DisableConstraintsAsync(admin);
        await using var mode = TenantRlsInterceptor.SetTenantCommand(admin, string.Empty, "admin");
        await mode.ExecuteNonQueryAsync();
        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            for (var i = 0; i < 20; i++)
            {
                await RowFactory.InsertAsync(admin, "sessions", new Dictionary<string, object?>
                {
                    ["id"] = Guid.NewGuid(),
                    ["tenant_id"] = tenant,
                    ["key_hash"] = $"KeyHash{tenant:N}{i}",
                });
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext Db(TenantContextAccessor context, string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(context))
            .Options;
        var clock = new SystemDateTimeProvider();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            context);
    }

    private static TenantContextAccessor Tenant(Guid id)
    {
        var context = new TenantContextAccessor();
        context.Resolve(new TenantRegistryEntry(id, "t", TenantStatus.Active, null));
        return context;
    }

    private static async Task<List<Guid>> VisibleTenantsAsync(ApplicationDbContext db)
        => await db.Database
            .SqlQueryRaw<Guid>("SELECT DISTINCT tenant_id AS [Value] FROM sessions")
            .ToListAsync();

    [Fact]
    public async Task PooledRequestThenBackgroundJob_DoesNotInheritTenant()
    {
        var singleConnection = new SqlConnectionStringBuilder(_database.App)
        {
            ApplicationName = "pool-reuse",
            MaxPoolSize = 1,
        }.ConnectionString;

        await using (var request = Db(Tenant(_tenantA), singleConnection))
        {
            Assert.Equal([_tenantA], await VisibleTenantsAsync(request));
        }

        await using var job = Db(new TenantContextAccessor(), singleConnection);

        Assert.Empty(await VisibleTenantsAsync(job));
    }

    [Fact]
    public async Task ConcurrentTenants_NeverCross()
    {
        await Task.WhenAll(Enumerable.Range(0, 400).Select(i => Task.Run(async () =>
        {
            var tenant = i % 2 == 0 ? _tenantA : _tenantB;
            await using var db = Db(Tenant(tenant), _database.App);
            Assert.Equal([tenant], await VisibleTenantsAsync(db));
        })));
    }

    [Fact]
    public async Task KeyHashDifferingByCase_DoesNotAuthenticate()
    {
        await using var db = Db(new TenantContextAccessor(), _database.App);
        var repository = new EfSessionRepository(db);
        var key = $"KeyHash{_tenantA:N}0";

        Assert.NotNull(await repository.GetByKeyHashForTenantResolutionAsync(key));
        Assert.Null(await repository.GetByKeyHashForTenantResolutionAsync(key.ToUpperInvariant()));
    }
}
