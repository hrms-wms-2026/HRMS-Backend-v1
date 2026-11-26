using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Improvements task 45: rate_limit_settings and rate_limit_setting_changes. Admin mode writes them,
/// tenant mode sees no platform rows, and the check constraints stop bad limits even from hand-written SQL.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RateLimitSettingsSchemaTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;
    private Guid _planId;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _platformUser = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = await ConnectAsync(_database.Admin, "admin", null);

        // Tenants and platform users need many other rows; only the check constraints are under test,
        // so those two foreign keys are switched off. The plan foreign key stays on (plans are seeded).
        await ExecuteAsync(_admin, """
            ALTER TABLE rate_limit_settings NOCHECK CONSTRAINT fk_rate_limit_settings_tenants_tenant_id;
            ALTER TABLE rate_limit_settings NOCHECK CONSTRAINT fk_rate_limit_settings_platform_users_updated_by_platform_user_id;
            """);
        await using var plan = _admin.CreateCommand();
        plan.CommandText = "SELECT TOP 1 id FROM subscription_plans";
        _planId = (Guid)(await plan.ExecuteScalarAsync())!;
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

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(SqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM [{table}]";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static string Id(Guid? id) => id is null ? "NULL" : $"'{id}'";

    private string InsertSetting(string scope, Guid? planId, Guid? tenantId, string limit, int perMinute, int? burst) => $"""
        INSERT INTO rate_limit_settings
            (id, scope, subscription_plan_id, tenant_id, limit_name, per_minute, burst, updated_at, updated_by_platform_user_id)
        VALUES
            (NEWID(), '{scope}', {Id(planId)}, {Id(tenantId)}, '{limit}', {perMinute}, {burst?.ToString() ?? "NULL"},
             SYSDATETIMEOFFSET(), '{_platformUser}')
        """;

    private string InsertChange(string scope, Guid? planId, Guid? tenantId, string limit) => $"""
        INSERT INTO rate_limit_setting_changes
            (id, changed_at, platform_user_id, scope, subscription_plan_id, tenant_id, limit_name,
             old_per_minute, old_burst, new_per_minute, new_burst, reason, correlation_id)
        VALUES
            (NEWID(), SYSDATETIMEOFFSET(), '{_platformUser}', '{scope}', {Id(planId)}, {Id(tenantId)}, '{limit}',
             NULL, NULL, 900, 150, N'Customer import week', N'test')
        """;

    private async Task SeedOneOfEachScopeAsync(SqlConnection admin)
    {
        await ExecuteAsync(admin, InsertSetting("global", null, null, "Anonymous", 300, null));
        await ExecuteAsync(admin, InsertSetting("global", null, null, "AdminSignedIn", 300, 60));
        await ExecuteAsync(admin, InsertSetting("plan", _planId, null, "SignedIn", 900, 150));
        await ExecuteAsync(admin, InsertSetting("tenant", null, _tenantB, "TenantCeiling", 20_000, 4_000));
        await ExecuteAsync(admin, InsertChange("global", null, null, "Anonymous"));
        await ExecuteAsync(admin, InsertChange("plan", _planId, null, "SignedIn"));
        await ExecuteAsync(admin, InsertChange("tenant", null, _tenantB, "TenantCeiling"));
    }

    [Fact]
    public async Task Admin_mode_can_insert_a_global_a_plan_and_a_tenant_row()
    {
        await using var admin = await ConnectAsync(_database.App, "admin", null);

        await SeedOneOfEachScopeAsync(admin);

        Assert.Equal(4, await CountAsync(admin, "rate_limit_settings"));
        Assert.Equal(3, await CountAsync(admin, "rate_limit_setting_changes"));

        await using var system = await ConnectAsync(_database.App, "system", null);
        Assert.Equal(4, await CountAsync(system, "rate_limit_settings"));
        Assert.Equal(3, await CountAsync(system, "rate_limit_setting_changes"));
    }

    [Theory]
    [InlineData("rate_limit_settings")]
    [InlineData("rate_limit_setting_changes")]
    public async Task Tenant_mode_reads_no_platform_or_other_tenant_rows(string table)
    {
        await using (var admin = await ConnectAsync(_database.App, "admin", null))
            await SeedOneOfEachScopeAsync(admin);

        await using var tenant = await ConnectAsync(_database.App, "tenant", _tenantA);
        Assert.Equal(0, await CountAsync(tenant, table));

        await using var none = new SqlConnection(_database.App);
        await none.OpenAsync();
        Assert.Equal(0, await CountAsync(none, table));
    }

    [Fact]
    public async Task Tenant_mode_cannot_write_a_platform_row()
    {
        await using var tenant = await ConnectAsync(_database.App, "tenant", _tenantA);

        var insert = await Assert.ThrowsAsync<SqlException>(() =>
            ExecuteAsync(tenant, InsertSetting("global", null, null, "Anonymous", 300, null)));

        Assert.Equal(33504, insert.Number);
    }

    public static TheoryData<string, string> RejectedRows() => new()
    {
        { "per_minute = 0", "'global', NULL, NULL, 'SignedIn', 0, 100" },
        { "per_minute above 200,000", "'global', NULL, NULL, 'SignedIn', 200001, 100" },
        { "burst above 50,000", "'global', NULL, NULL, 'SignedIn', 600, 50001" },
        { "plan-scope Anonymous", "'plan', '{plan}', NULL, 'Anonymous', 300, NULL" },
        { "tenant-scope AdminSignedIn", "'tenant', NULL, '{tenant}', 'AdminSignedIn', 300, 60" },
        { "SignedIn with no burst", "'global', NULL, NULL, 'SignedIn', 600, NULL" },
        { "Anonymous with a burst", "'global', NULL, NULL, 'Anonymous', 300, 10" },
        { "unknown limit name", "'global', NULL, NULL, 'PreAuthentication', 6000, NULL" },
        { "unknown scope", "'team', NULL, NULL, 'SignedIn', 600, 100" },
        { "global row with a tenant", "'global', NULL, '{tenant}', 'SignedIn', 600, 100" },
        { "plan row with no plan", "'plan', NULL, NULL, 'SignedIn', 600, 100" },
        { "tenant row with a plan too", "'tenant', '{plan}', '{tenant}', 'SignedIn', 600, 100" },
    };

    [Theory]
    [MemberData(nameof(RejectedRows))]
    public async Task Check_constraints_reject_bad_limits(string because, string values)
    {
        await using var admin = await ConnectAsync(_database.App, "admin", null);
        values = values.Replace("{plan}", _planId.ToString()).Replace("{tenant}", _tenantA.ToString());

        var insert = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(admin, $"""
            INSERT INTO rate_limit_settings
                (id, scope, subscription_plan_id, tenant_id, limit_name, per_minute, burst, updated_at, updated_by_platform_user_id)
            VALUES (NEWID(), {values}, SYSDATETIMEOFFSET(), '{_platformUser}')
            """));

        Assert.True(insert.Number == 547, $"{because}: expected a check constraint error, got {insert.Number} {insert.Message}");
    }

    [Fact]
    public async Task Each_scope_and_limit_has_at_most_one_row()
    {
        await using var admin = await ConnectAsync(_database.App, "admin", null);
        await ExecuteAsync(admin, InsertSetting("tenant", null, _tenantA, "SignedIn", 900, 150));
        await ExecuteAsync(admin, InsertSetting("tenant", null, _tenantB, "SignedIn", 900, 150));
        await ExecuteAsync(admin, InsertSetting("plan", _planId, null, "SignedIn", 900, 150));
        await ExecuteAsync(admin, InsertSetting("global", null, null, "SignedIn", 900, 150));

        foreach (var duplicate in new[]
                 {
                     InsertSetting("tenant", null, _tenantA, "SignedIn", 1_000, 150),
                     InsertSetting("plan", _planId, null, "SignedIn", 1_000, 150),
                     InsertSetting("global", null, null, "SignedIn", 1_000, 150),
                 })
        {
            var insert = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(admin, duplicate));
            Assert.Equal(2601, insert.Number);
        }
    }

    [Fact]
    public async Task A_history_row_needs_a_reason_and_a_value()
    {
        await using var admin = await ConnectAsync(_database.App, "admin", null);

        var noReason = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(admin,
            InsertChange("global", null, null, "Anonymous").Replace("N'Customer import week'", "N'  '")));
        Assert.Equal(547, noReason.Number);

        var noValues = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(admin,
            InsertChange("global", null, null, "Anonymous").Replace("900, 150", "NULL, NULL")));
        Assert.Equal(547, noValues.Number);
    }
}
