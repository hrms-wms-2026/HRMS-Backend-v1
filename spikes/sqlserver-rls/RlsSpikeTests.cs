using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace SqlServerRlsSpike;

public sealed class SpikeFixture : IAsyncLifetime
{
    public const int TenantCount = 8;
    public const int RowsPerTenant = 62_500;
    private MsSqlContainer _container = null!;

    public Guid[] Tenants { get; } = Enumerable.Range(1, TenantCount)
        .Select(i => new Guid($"00000000-0000-0000-0000-{i:D12}")).ToArray();
    public Guid A => Tenants[0];
    public Guid B => Tenants[1];
    public string Sa { get; private set; } = "";
    public string App { get; private set; } = "";
    public string Migrator { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        var master = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "master",
            TrustServerCertificate = true,
        };
        Sa = new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = "crownus_spike" }.ConnectionString;
        App = new SqlConnectionStringBuilder(Sa) { UserID = "onevo_app", Password = "Spike_App_Pw_12345!", MaxPoolSize = 64 }.ConnectionString;
        Migrator = new SqlConnectionStringBuilder(Sa) { UserID = "onevo_migrator", Password = "Spike_Mig_Pw_12345!" }.ConnectionString;

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }

        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        await using var connection = new SqlConnection(Sa);
        await connection.OpenAsync();
        await Rls.SetAsync(connection, "admin", null);
        foreach (var (tenant, index) in Tenants.Select((t, i) => (t, i)))
        {
            await Exec(connection, $"""
                INSERT INTO tenants (id, slug, name, status) VALUES ('{tenant}', 't{index}', 'Tenant {index}', 'Active');
                INSERT INTO items (tenant_id, payload, amount)
                SELECT TOP ({RowsPerTenant}) '{tenant}', REPLICATE('x', 80), ABS(CHECKSUM(NEWID())) % 100000
                FROM sys.all_columns a CROSS JOIN sys.all_columns b;
                INSERT INTO tray_codes (tenant_id, code) VALUES ('{tenant}', 'c{index}');
                INSERT INTO sessions (id, tenant_id, key_hash) VALUES (NEWID(), '{tenant}', 'AbCdEf{index}');
                """);
        }
        await Rls.ClearAsync(connection);
        await Rls.SetAsync(connection, "tenant", A);
        await Exec(connection, $"INSERT INTO files (tenant_id, name) VALUES ('{A}', 'a.txt');");
        await Rls.SetAsync(connection, "tenant", B);
        await Exec(connection, $"INSERT INTO files (tenant_id, name) VALUES ('{B}', 'b.txt');");
    }

    public static async Task<int> Exec(SqlConnection connection, string sql, SqlTransaction? tx = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.CommandTimeout = 300;
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<T> Scalar<T>(SqlConnection connection, string sql, SqlTransaction? tx = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.CommandTimeout = 300;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("spike")]
public sealed class SpikeCollection : ICollectionFixture<SpikeFixture>;

public static class Rls
{
    public static async Task SetAsync(SqlConnection connection, string? mode, Guid? tenant, SqlTransaction? tx = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            EXEC sp_set_session_context N'app.current_tenant_id', @tenant;
            EXEC sp_set_session_context N'app.tenant_context_mode', @mode;
            """;
        command.Parameters.AddWithValue("@tenant", tenant?.ToString() ?? "");
        command.Parameters.AddWithValue("@mode", mode ?? "");
        await command.ExecuteNonQueryAsync();
    }

    public static Task ClearAsync(SqlConnection connection) => SetAsync(connection, null, null);
}

[Collection("spike")]
public sealed class RlsSpikeTests(SpikeFixture f)
{
    private static readonly string MetricsPath = Path.Combine(AppContext.BaseDirectory, "spike-metrics.txt");
    private static void Metric(string line) => File.AppendAllText(MetricsPath, line + Environment.NewLine);

    private async Task<SqlConnection> AppAsync(string? mode, Guid? tenant)
    {
        var connection = new SqlConnection(f.App);
        await connection.OpenAsync();
        if (mode is not null) await Rls.SetAsync(connection, mode, tenant);
        return connection;
    }

    private async Task<long> AnyRowIdForAsync(Guid tenant)
    {
        await using var connection = new SqlConnection(f.Sa);
        await connection.OpenAsync();
        await Rls.SetAsync(connection, "tenant", tenant);
        return await SpikeFixture.Scalar<long>(connection, "SELECT TOP 1 id FROM items ORDER BY id");
    }

    private async Task<string> PayloadAsAdminAsync(long id)
    {
        await using var connection = new SqlConnection(f.Sa);
        await connection.OpenAsync();
        await Rls.SetAsync(connection, "admin", null);
        return await SpikeFixture.Scalar<string>(connection, $"SELECT payload FROM items WHERE id = {id}");
    }

    // ---- Step 1: isolation ----

    [Fact]
    public async Task NoContext_SeesNoRows()
    {
        await using var c = await AppAsync(null, null);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task UnknownMode_SeesNoRows()
    {
        await using var c = await AppAsync("bogus", f.A);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task SystemMode_SeesNoRows_OnStandardTable()
    {
        await using var c = await AppAsync("system", null);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task SystemMode_SeesRows_OnTrayTable()
    {
        await using var c = await AppAsync("system", null);
        Assert.Equal(SpikeFixture.TenantCount, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM tray_codes"));
    }

    [Fact]
    public async Task StrictTable_AdminMode_SeesNoRows()
    {
        await using var c = await AppAsync("admin", null);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM files"));
    }

    [Fact]
    public async Task TenantA_SeesOnlyTenantA()
    {
        await using var c = await AppAsync("tenant", f.A);
        Assert.Equal(SpikeFixture.RowsPerTenant, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM items"));
        Assert.Equal(1, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(DISTINCT tenant_id) FROM items"));
        Assert.Equal(f.A, await SpikeFixture.Scalar<Guid>(c, "SELECT TOP 1 tenant_id FROM items"));
    }

    [Fact]
    public async Task CrossTenantInsert_IsBlocked()
    {
        await using var c = await AppAsync("tenant", f.A);
        var ex = await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c,
            $"INSERT INTO items (tenant_id, payload, amount) VALUES ('{f.B}', 'evil', 1)"));
        Assert.Equal(33504, ex.Number);
    }

    [Fact]
    public async Task CrossTenantUpdate_IsBlocked()
    {
        var id = await AnyRowIdForAsync(f.B);
        await using var c = await AppAsync("tenant", f.A);
        Assert.Equal(0, await SpikeFixture.Exec(c, $"UPDATE items SET payload = 'evil' WHERE id = {id}"));
        Assert.NotEqual("evil", await PayloadAsAdminAsync(id));
    }

    [Fact]
    public async Task CrossTenantDelete_AffectsZero()
    {
        var id = await AnyRowIdForAsync(f.B);
        await using var c = await AppAsync("tenant", f.A);
        Assert.Equal(0, await SpikeFixture.Exec(c, $"DELETE FROM items WHERE id = {id}"));
    }

    [Fact]
    public async Task MoveRowToOtherTenant_IsBlocked()
    {
        var id = await AnyRowIdForAsync(f.A);
        await using var c = await AppAsync("tenant", f.A);
        var ex = await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c,
            $"UPDATE items SET tenant_id = '{f.B}' WHERE id = {id}"));
        Assert.Equal(33504, ex.Number);
    }

    [Theory]
    [InlineData("ALTER SECURITY POLICY rls.policy_items WITH (STATE = OFF)")]
    [InlineData("DROP SECURITY POLICY rls.policy_items")]
    [InlineData("ALTER TABLE items ADD extra int NULL")]
    [InlineData("CREATE TABLE evil (id int)")]
    [InlineData("EXECUTE AS USER = 'onevo_auth_base_login_fn_owner'")]
    [InlineData("EXECUTE AS USER = 'dbo'")]
    public async Task AppLogin_CannotAlterPolicy_OrSchema(string sql)
    {
        await using var c = await AppAsync("tenant", f.A);
        await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c, sql));
    }

    [Fact]
    public async Task AppLogin_CannotCallPredicateFunctionDirectly()
    {
        await using var c = await AppAsync("tenant", f.A);
        await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c, $"SELECT * FROM rls.fn_tenant('{f.A}')"));
    }

    [Fact]
    public async Task RecordsServerVersion()
    {
        await using var c = await AppAsync(null, null);
        Metric(await SpikeFixture.Scalar<string>(c, "SELECT @@VERSION"));
    }

    [Fact]
    public async Task AppLogin_IsNotDbOwner()
    {
        await using var c = await AppAsync(null, null);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT IS_ROLEMEMBER('db_owner')"));
    }

    [Fact]
    public async Task Migrator_CanAddAPolicyForANewTable()
    {
        await using var c = new SqlConnection(f.Migrator);
        await c.OpenAsync();
        await SpikeFixture.Exec(c, """
            CREATE TABLE migrator_new (id int PRIMARY KEY, tenant_id uniqueidentifier NOT NULL);
            """);
        await SpikeFixture.Exec(c, """
            CREATE SECURITY POLICY rls.policy_migrator_new
                ADD FILTER PREDICATE rls.fn_tenant(tenant_id) ON dbo.migrator_new
                WITH (STATE = ON, SCHEMABINDING = ON);
            """);
    }

    // ---- Step 2: parallel plans ----

    private const string HeavyQuery = """
        SELECT a.amount % 1000 AS bucket, COUNT_BIG(*) AS n
        FROM items a JOIN items b ON a.id = b.id + 1
        GROUP BY a.amount % 1000
        """;

    private async Task<int> DegreeOfParallelismAsync(int maxdop)
    {
        await using var c = new SqlConnection(f.Sa);
        await c.OpenAsync();
        await SpikeFixture.Exec(c, $"ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = {maxdop}; DBCC FREEPROCCACHE;");
        await Rls.SetAsync(c, "admin", null);
        await SpikeFixture.Exec(c, "SET STATISTICS XML ON");
        await using var command = c.CreateCommand();
        command.CommandText = HeavyQuery;
        command.CommandTimeout = 300;
        await using var reader = await command.ExecuteReaderAsync();
        string? plan = null;
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                    plan = reader.GetString(0);
            }
        } while (await reader.NextResultAsync());
        Assert.NotNull(plan);
        var match = Regex.Match(plan!, "DegreeOfParallelism=\"(\\d+)\"");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private async Task SetMaxdopAsync(int maxdop)
    {
        await using var c = new SqlConnection(f.Sa);
        await c.OpenAsync();
        await SpikeFixture.Exec(c, $"ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = {maxdop}; DBCC FREEPROCCACHE;");
    }

    [Fact]
    public async Task Maxdop1_ProducesSerialPlan()
    {
        try
        {
            var parallel = await DegreeOfParallelismAsync(0);
            Metric($"DOP with MAXDOP=0: {parallel}");
            Assert.True(parallel > 1, $"Control failed: heavy query did not go parallel (DOP {parallel}).");

            var serial = await DegreeOfParallelismAsync(1);
            Metric($"DOP with MAXDOP=1: {serial}");
            Assert.True(serial <= 1, $"MAXDOP = 1 still produced DOP {serial}.");
        }
        finally
        {
            await SetMaxdopAsync(1);
        }
    }

    private async Task<(int Mismatches, int Empty, int Done, TimeSpan Elapsed)> StressAsync(int iterations, string sql)
    {
        const int workers = 32;
        var mismatches = 0;
        var empty = 0;
        var done = 0;
        var failures = new ConcurrentBag<Exception>();
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
        {
            var tenant = f.Tenants[worker % SpikeFixture.TenantCount];
            for (var i = worker; i < iterations; i += workers)
            {
                try
                {
                    await using var c = new SqlConnection(f.App);
                    await c.OpenAsync();
                    await Rls.SetAsync(c, "tenant", tenant);
                    await using var command = c.CreateCommand();
                    command.CommandText = sql;
                    command.CommandTimeout = 300;
                    var seen = new List<Guid>();
                    await using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) seen.Add(reader.GetGuid(0));
                    }
                    if (seen.Count == 0) Interlocked.Increment(ref empty);
                    if (seen.Any(g => g != tenant)) Interlocked.Increment(ref mismatches);
                    await Rls.ClearAsync(c);
                    Interlocked.Increment(ref done);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
        })));
        stopwatch.Stop();
        Assert.True(failures.IsEmpty, $"{failures.Count} failures, first: {failures.FirstOrDefault()?.Message}");
        return (mismatches, empty, done, stopwatch.Elapsed);
    }

    private const string StressQuery = "SELECT DISTINCT tenant_id FROM items";

    [Fact]
    public async Task ConcurrentPooledTenants_NeverSeeEachOther()
    {
        var result = await StressAsync(20_000, StressQuery);
        Metric($"Stress MAXDOP=1: {result}, {result.Done / result.Elapsed.TotalSeconds:F0} queries/s");
        Assert.Equal(20_000, result.Done);
        Assert.Equal(0, result.Mismatches);
        Assert.Equal(0, result.Empty);
    }

    [Fact]
    public async Task ConcurrentPooledTenants_WithParallelPlans_ForInformation()
    {
        try
        {
            await SetMaxdopAsync(0);
            var sql = """
                SELECT DISTINCT a.tenant_id
                FROM items a JOIN items b ON a.id = b.id + 1
                OPTION (HASH JOIN)
                """;
            var result = await StressAsync(2_000, sql);
            Metric($"Stress MAXDOP=0 (information only): {result}");
        }
        finally
        {
            await SetMaxdopAsync(1);
        }
    }

    // ---- Step 3: connection state ----

    private SqlConnection SinglePooled(string name) => new(new SqlConnectionStringBuilder(f.App)
    {
        ApplicationName = name,
        MaxPoolSize = 1,
    }.ConnectionString);

    [Fact]
    public async Task PoolReuse_ClearsContext()
    {
        short firstSpid;
        await using (var c = SinglePooled(nameof(PoolReuse_ClearsContext)))
        {
            await c.OpenAsync();
            await Rls.SetAsync(c, "tenant", f.A);
            firstSpid = await SpikeFixture.Scalar<short>(c, "SELECT @@SPID");
        }

        await using var again = SinglePooled(nameof(PoolReuse_ClearsContext));
        await again.OpenAsync();
        Assert.Equal(firstSpid, await SpikeFixture.Scalar<short>(again, "SELECT @@SPID"));
        Assert.Equal(0, await SpikeFixture.Scalar<int>(again, "SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task ContextSurvivesRollback_Documented()
    {
        await using var c = await AppAsync("tenant", f.A);
        await using (var tx = (SqlTransaction)await c.BeginTransactionAsync())
        {
            await Rls.SetAsync(c, "tenant", f.B, tx);
            await tx.RollbackAsync();
        }

        var after = await SpikeFixture.Scalar<string>(c,
            "SELECT CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36))");
        Metric($"Context after rollback: {(after == f.B.ToString() ? "still B (not transactional)" : after)}");
        Assert.Equal(f.B.ToString(), after);
    }

    private async Task<object?> LookupAsync(SqlConnection c, string keyHash, bool fail)
    {
        try
        {
            await using var set = c.CreateCommand();
            set.CommandText = "EXEC sp_set_session_context N'app.session_lookup_key_hash', @key";
            set.Parameters.AddWithValue("@key", keyHash);
            await set.ExecuteNonQueryAsync();
            await using var read = c.CreateCommand();
            read.CommandText = "SELECT id FROM sessions WHERE key_hash = @key";
            read.Parameters.AddWithValue("@key", keyHash);
            var result = await read.ExecuteScalarAsync();
            if (fail) throw new InvalidOperationException("boom");
            return result;
        }
        finally
        {
            await SpikeFixture.Exec(c, "EXEC sp_set_session_context N'app.session_lookup_key_hash', NULL");
        }
    }

    [Fact]
    public async Task SessionLookup_FindsRowWithNoTenantContext()
    {
        await using var c = await AppAsync(null, null);
        Assert.NotNull(await LookupAsync(c, "AbCdEf0", fail: false));
    }

    [Fact]
    public async Task SessionLookup_ClearedAfterFailure()
    {
        await using var c = await AppAsync(null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LookupAsync(c, "AbCdEf0", fail: true));
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM sessions"));
    }

    [Fact]
    public async Task KeyHash_IsCaseSensitive()
    {
        await using var c = await AppAsync(null, null);
        Assert.Null(await LookupAsync(c, "ABCDEF0", fail: false));
    }

    // ---- Step 4: MERGE and EF ----

    [Fact]
    public async Task Merge_CrossTenantUpdate_IsBlocked()
    {
        var id = await AnyRowIdForAsync(f.B);
        await using var c = await AppAsync("tenant", f.A);
        var affected = await SpikeFixture.Exec(c, $"""
            MERGE items AS t USING (SELECT {id} AS id) AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET payload = 'evil';
            """);
        Assert.Equal(0, affected);
        Assert.NotEqual("evil", await PayloadAsAdminAsync(id));
    }

    [Fact]
    public async Task Merge_CrossTenantInsert_IsBlocked()
    {
        await using var c = await AppAsync("tenant", f.A);
        var ex = await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c, $"""
            MERGE items AS t USING (SELECT CAST(-1 AS bigint) AS id) AS s ON t.id = s.id
            WHEN NOT MATCHED THEN INSERT (tenant_id, payload, amount) VALUES ('{f.B}', 'evil', 1);
            """));
        Assert.Equal(33504, ex.Number);
    }

    [Fact]
    public async Task Merge_MoveRowToOtherTenant_IsBlocked()
    {
        var id = await AnyRowIdForAsync(f.A);
        await using var c = await AppAsync("tenant", f.A);
        var ex = await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c, $"""
            MERGE items AS t USING (SELECT {id} AS id) AS s ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET tenant_id = '{f.B}';
            """));
        Assert.Equal(33504, ex.Number);
    }

    private sealed class Item
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public string Payload { get; set; } = "";
        public int Amount { get; set; }
    }

    private sealed class Tracked
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class SpikeContext(DbContextOptions<SpikeContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<Tracked> Tracked => Set<Tracked>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Item>(e =>
            {
                e.ToTable("items");
                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.TenantId).HasColumnName("tenant_id");
                e.Property(x => x.Payload).HasColumnName("payload");
                e.Property(x => x.Amount).HasColumnName("amount");
            });
            b.Entity<Tracked>(e =>
            {
                e.ToTable("tracked_items", t => t.HasTrigger("trg_tracked_items_log"));
                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.TenantId).HasColumnName("tenant_id");
                e.Property(x => x.Name).HasColumnName("name");
            });
        }
    }

    private static SpikeContext ContextOn(SqlConnection c, List<string> log) => new(
        new DbContextOptionsBuilder<SpikeContext>()
            .UseSqlServer(c)
            .LogTo(log.Add, Microsoft.Extensions.Logging.LogLevel.Information)
            .Options);

    [Fact]
    public async Task EfBatchInsert_ForOtherTenant_IsBlocked()
    {
        await using var c = await AppAsync("tenant", f.A);
        var log = new List<string>();
        await using var db = ContextOn(c, log);
        db.Items.AddRange(Enumerable.Range(0, 50).Select(i => new Item { TenantId = f.B, Payload = "evil", Amount = i }));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(33504, Assert.IsType<SqlException>(ex.InnerException).Number);
        Assert.Contains(log, l => l.Contains("MERGE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EfBatchInsert_ForOwnTenant_Succeeds()
    {
        await using var c = await AppAsync("tenant", f.A);
        var log = new List<string>();
        await using var db = ContextOn(c, log);
        var rows = Enumerable.Range(0, 50).Select(i => new Item { TenantId = f.A, Payload = "ok", Amount = i }).ToList();
        db.Items.AddRange(rows);
        await db.SaveChangesAsync();
        Assert.All(rows, r => Assert.True(r.Id > 0));
        db.Items.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EfSave_OnTableWithTrigger_Works()
    {
        await using var c = await AppAsync("tenant", f.A);
        await using var db = ContextOn(c, []);
        var row = new Tracked { TenantId = f.A, Name = "n" };
        db.Tracked.Add(row);
        await db.SaveChangesAsync();
        Assert.True(row.Id > 0);
        Assert.Equal(1, await SpikeFixture.Scalar<int>(c, $"SELECT COUNT(*) FROM tracked_log WHERE item_id = {row.Id}"));
    }

    // ---- Step 5: login lookup ----

    private async Task SeedUsersAsync(string email, int count)
    {
        await using var c = new SqlConnection(f.Sa);
        await c.OpenAsync();
        await Rls.SetAsync(c, "admin", null);
        for (var i = 0; i < count; i++)
        {
            var tenant = f.Tenants[i % SpikeFixture.TenantCount];
            await SpikeFixture.Exec(c, $"""
                INSERT INTO users (id, tenant_id, normalized_email, password_hash, is_active, is_deleted, private_note)
                VALUES (NEWID(), '{tenant}', '{email}', 'hash{i}', 1, 0, 'private');
                """);
        }
    }

    private static async Task<int> LookupRowsAsync(SqlConnection c, string email)
    {
        await using var command = c.CreateCommand();
        command.CommandText = "EXEC auth_internal.auth_lookup_base_login_candidates @normalized_email = @e";
        command.Parameters.AddWithValue("@e", email);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync()) rows++;
        return rows;
    }

    [Fact]
    public async Task Lookup_ReturnsCandidates_WithNoContext()
    {
        await SeedUsersAsync("two@spike.test", 2);
        await using var c = await AppAsync(null, null);
        Assert.Equal(2, await LookupRowsAsync(c, "two@spike.test"));
    }

    [Fact]
    public async Task Lookup_LimitsTo9()
    {
        await SeedUsersAsync("many@spike.test", 12);
        await using var c = await AppAsync(null, null);
        Assert.Equal(9, await LookupRowsAsync(c, "many@spike.test"));
    }

    [Fact]
    public async Task AppLogin_CannotSelectUsers_WithoutContext()
    {
        await SeedUsersAsync("direct@spike.test", 1);
        await using var c = await AppAsync(null, null);
        Assert.Equal(0, await SpikeFixture.Scalar<int>(c, "SELECT COUNT(*) FROM users"));
    }

    [Fact]
    public async Task LookupUser_CannotReadOtherColumns()
    {
        await using var c = new SqlConnection(f.Sa);
        await c.OpenAsync();
        await SpikeFixture.Exec(c, "EXECUTE AS USER = 'onevo_auth_base_login_fn_owner'");
        await Assert.ThrowsAsync<SqlException>(() => SpikeFixture.Exec(c, "SELECT private_note FROM users"));
        await SpikeFixture.Exec(c, "REVERT");
    }
}
