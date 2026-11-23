using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Improvements task 35 (DEC-089): 20261123111349_ClusterSnapshotsByCaptureTime clusters the three
/// snapshot tables on (tenant_id, captured_at, id), makes the device idempotency key unique on all
/// three, removes retry duplicates first, and keeps the evidence-asset link to activity_snapshots.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SnapshotClusteringMigrationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string MigrationBefore = "20261120172929_AddRowVersionTokens";
    private static readonly string[] Tables = ["activity_snapshots", "app_usage_snapshots", "device_state_snapshots"];

    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _device = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _activityRow = Guid.NewGuid();
    private readonly Guid _evidence = Guid.NewGuid();
    private readonly DateTimeOffset _capturedAt = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);
    private readonly Dictionary<string, (Guid Kept, Guid Removed)> _duplicates = new();

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync(migrate: false);
        await using (var context = _database.CreateMigratorContext())
            await context.GetService<IMigrator>().MigrateAsync(MigrationBefore);

        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await AdminModeAsync();
        await RowFactory.DisableConstraintsAsync(_admin);

        await Insert("activity_snapshots", _activityRow, _capturedAt, DateTimeOffset.UtcNow);
        await RowFactory.InsertAsync(_admin, "monitoring_evidence_assets", new Dictionary<string, object?>
        {
            ["id"] = _evidence, ["tenant_id"] = _tenant, ["activity_snapshot_id"] = _activityRow,
        });

        // A retry that slipped past the app check: the same capture stored twice, a minute apart.
        foreach (var table in new[] { "app_usage_snapshots", "device_state_snapshots" })
        {
            var (kept, removed) = (Guid.NewGuid(), Guid.NewGuid());
            await Insert(table, kept, _capturedAt, DateTimeOffset.UtcNow.AddMinutes(-2));
            await Insert(table, removed, _capturedAt, DateTimeOffset.UtcNow.AddMinutes(-1));
            await Insert(table, Guid.NewGuid(), _capturedAt.AddMinutes(1), DateTimeOffset.UtcNow);
            _duplicates[table] = (kept, removed);
        }

        await using (var context = _database.CreateMigratorContext())
            await context.Database.MigrateAsync();
        await AdminModeAsync();
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private async Task AdminModeAsync()
    {
        await using var mode = TenantRlsInterceptor.SetTenantCommand(_admin, string.Empty, "admin");
        await mode.ExecuteNonQueryAsync();
    }

    private Task Insert(string table, Guid id, DateTimeOffset capturedAt, DateTimeOffset createdAt) =>
        RowFactory.InsertAsync(_admin, table, new Dictionary<string, object?>
        {
            ["id"] = id, ["tenant_id"] = _tenant, ["employee_id"] = _employee, ["agent_device_id"] = _device,
            ["captured_at"] = capturedAt, ["created_at"] = createdAt,
        });

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData("activity_snapshots")]
    [InlineData("app_usage_snapshots")]
    [InlineData("device_state_snapshots")]
    public async Task Each_table_is_clustered_on_tenant_captured_at_id_and_keeps_a_nonclustered_key(string table)
    {
        var clustered = await ScalarAsync<string>($"""
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('{table}') AND i.type = 1
            """);
        clustered.Should().Be("tenant_id,captured_at,id");

        var primaryKeyType = await ScalarAsync<string>(
            $"SELECT type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('{table}') AND is_primary_key = 1");
        primaryKeyType.Should().Be("NONCLUSTERED");
    }

    [Theory]
    [InlineData("activity_snapshots")]
    [InlineData("app_usage_snapshots")]
    [InlineData("device_state_snapshots")]
    public async Task The_device_capture_key_is_unique(string table)
    {
        (await ScalarAsync<bool>(
            $"SELECT is_unique FROM sys.indexes WHERE name = 'ix_{table}_tenant_device_captured'")).Should().BeTrue();

        // Every table holds one row for this device at _capturedAt after the migration.
        var second = await Assert.ThrowsAsync<SqlException>(() =>
            Insert(table, Guid.NewGuid(), _capturedAt, DateTimeOffset.UtcNow));
        second.Number.Should().BeOneOf(2601, 2627);
    }

    [Theory]
    [InlineData("app_usage_snapshots")]
    [InlineData("device_state_snapshots")]
    public async Task Retry_duplicates_are_removed_keeping_the_earliest_row(string table)
    {
        var (kept, removed) = _duplicates[table];
        (await ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE id = '{kept}'")).Should().Be(1);
        (await ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE id = '{removed}'")).Should().Be(0);
        (await ScalarAsync<int>($"SELECT COUNT(*) FROM {table}")).Should().Be(2, "the other capture is untouched");
    }

    [Fact]
    public async Task The_evidence_asset_still_points_at_its_activity_snapshot()
    {
        (await ScalarAsync<int>($"""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE name = 'fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id'
              AND referenced_object_id = OBJECT_ID('activity_snapshots')
              AND delete_referential_action_desc = 'SET_NULL'
            """)).Should().Be(1);
        (await ScalarAsync<Guid>($"SELECT activity_snapshot_id FROM monitoring_evidence_assets WHERE id = '{_evidence}'"))
            .Should().Be(_activityRow);
    }
}
