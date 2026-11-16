using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Security audit PA-SEC-002: every audit table is append-only for the runtime login, and no foreign key
/// can rewrite an audit row behind the DENY (a SET NULL cascade is not permission-checked).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditTablesAppendOnlyTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const int PermissionDenied = 229;
    private static readonly Guid Tenant = Guid.NewGuid();
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public static TheoryData<string> AuditTables() =>
        ["platform_audit_logs", "audit_logs", "platform_auth_events", "billing_audit_logs"];

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = await ConnectAsync(_database.Admin, "admin", null);
        await RowFactory.DisableConstraintsAsync(_admin);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private static async Task<SqlConnection> ConnectAsync(string connectionString, string mode, Guid? tenant)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, tenant?.ToString() ?? "", mode);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    // A tenant session, so the tenant-scoped audit_logs and billing_audit_logs rows are its own.
    private Task<SqlConnection> AppConnectionAsync() => ConnectAsync(_database.App, "tenant", Tenant);

    private static Dictionary<string, object?> Row(string table, Guid id) =>
        table is "audit_logs" or "billing_audit_logs"
            ? new() { ["id"] = id, ["tenant_id"] = Tenant }
            : new() { ["id"] = id };

    private async Task<Guid> InsertRowAsync(string table)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, table, Row(table, id));
        return id;
    }

    [Theory]
    [MemberData(nameof(AuditTables))]
    public async Task Runtime_login_can_add_an_audit_row(string table)
    {
        await using var app = await AppConnectionAsync();

        await RowFactory.InsertAsync(app, table, Row(table, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(AuditTables))]
    public async Task Runtime_login_cannot_change_an_audit_row(string table)
    {
        var id = await InsertRowAsync(table);
        await using var app = await AppConnectionAsync();
        await using var command = app.CreateCommand();
        command.CommandText = $"UPDATE [{table}] SET id = id WHERE id = '{id}'";

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PermissionDenied, error.Number);
    }

    [Theory]
    [MemberData(nameof(AuditTables))]
    public async Task Runtime_login_cannot_remove_an_audit_row(string table)
    {
        var id = await InsertRowAsync(table);
        await using var app = await AppConnectionAsync();
        await using var command = app.CreateCommand();
        command.CommandText = $"DELETE FROM [{table}] WHERE id = '{id}'";

        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PermissionDenied, error.Number);
    }

    [Fact]
    public async Task No_foreign_key_from_an_audit_table_rewrites_or_removes_audit_rows()
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = """
            SELECT OBJECT_NAME(parent_object_id), name, delete_referential_action_desc, update_referential_action_desc
            FROM sys.foreign_keys
            WHERE OBJECT_NAME(parent_object_id) IN
                  ('platform_audit_logs', 'audit_logs', 'platform_auth_events', 'billing_audit_logs')
              AND (delete_referential_action <> 0 OR update_referential_action <> 0)
            """;
        var offenders = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)} on delete {reader.GetString(2)}, on update {reader.GetString(3)}");

        Assert.Empty(offenders);
    }
}
