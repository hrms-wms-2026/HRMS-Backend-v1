using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// ReconcileModuleIdentities moves every persisted legacy module key to its canonical key
/// (DEC-040, DEC-041, DEC-042, DEC-048). The database is fully migrated first, then legacy rows are
/// seeded and the migration's SQL is run again, which also proves it is idempotent.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ModuleIdentityMigrationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await using var command = TenantRlsInterceptor.SetTenantCommand(_admin, "", "admin");
        await command.ExecuteNonQueryAsync();
        await RowFactory.DisableConstraintsAsync(_admin);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private async Task ReconcileAsync()
    {
        await using (var command = _admin.CreateCommand())
        {
            command.CommandText = ReconcileModuleIdentities.UpSql;
            await command.ExecuteNonQueryAsync();
        }

        // The script clears the RLS context it set; tenant tables are hidden again until the
        // test re-enters admin mode for its reads.
        Assert.Null(await ScalarAsync("SELECT CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))") as string);
        await using var admin = TenantRlsInterceptor.SetTenantCommand(_admin, "", "admin");
        await admin.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Migrated_database_has_canonical_starter_plan_and_inactive_legacy_catalog_rows()
    {
        Assert.Equal(
            """["org","core_hr","time_off","calendar","time_attendance","monitoring","verification","analytics","work_management"]""",
            await ScalarAsync("SELECT included_modules_json FROM dbo.subscription_plans WHERE code = N'starter_51_200'"));
        Assert.Equal(13, await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.module_catalog WHERE is_active = 0 AND module_key IN (N'org_structure', N'activity_monitoring', " +
            "N'desktop_agent_gateway', N'discrepancy_engine', N'exception_engine', N'identity_verification', N'productivity_analytics', " +
            "N'worksync_foundation', N'projects', N'objectives_milestones', N'tasks', N'boards', N'planning_sprints')"));
        Assert.Equal(27, await ScalarAsync("SELECT COUNT(*) FROM dbo.module_catalog"));
    }

    [Theory]
    [InlineData("tenant_subscriptions", "selected_modules")]
    [InlineData("subscription_plans", "included_modules_json")]
    [InlineData("role_templates", "module_keys_json")]
    [InlineData("configuration_templates", "module_keys_json")]
    public async Task Json_module_arrays_are_rewritten_deduplicated_and_ordered(string table, string column)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, table, new Dictionary<string, object?>
        {
            ["id"] = id,
            [column] = """[" tasks","projects","core_hr","leave","org_structure","integrations"]""",
        });

        await ReconcileAsync();

        Assert.Equal(
            """["work_management","core_hr","time_off","org","integrations"]""",
            await ScalarAsync($"SELECT {column} FROM dbo.{table} WHERE id = '{id}'"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"tasks":true}""")]
    [InlineData("""["tasks",1]""")]
    public async Task Unparseable_or_non_string_arrays_are_left_untouched(string value)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "tenant_subscriptions", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["selected_modules"] = value,
        });

        await ReconcileAsync();

        Assert.Equal(value, await ScalarAsync($"SELECT selected_modules FROM dbo.tenant_subscriptions WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Module_key_columns_are_rewritten()
    {
        await ScalarAsync("""
            UPDATE dbo.permissions SET module = N'org_structure' WHERE code = N'org:read';
            UPDATE dbo.permissions SET module = N'leave' WHERE code = N'leave:read';
            UPDATE dbo.permissions SET module = N'activity_monitoring' WHERE code = N'monitoring:read';
            UPDATE dbo.permissions SET module = N'core_hr' WHERE code = N'attendance:read';
            UPDATE dbo.permissions SET module = N'projects' WHERE code = N'projects:create';
            """);
        await RowFactory.InsertAsync(_admin, "module_permission_ownership", new Dictionary<string, object?>
        {
            ["module_key"] = "org_structure",
            ["permission_code"] = "test:ownership",
        });

        await ReconcileAsync();

        Assert.Equal("org", await ScalarAsync("SELECT module FROM dbo.permissions WHERE code = N'org:read'"));
        Assert.Equal("time_off", await ScalarAsync("SELECT module FROM dbo.permissions WHERE code = N'leave:read'"));
        Assert.Equal("monitoring", await ScalarAsync("SELECT module FROM dbo.permissions WHERE code = N'monitoring:read'"));
        Assert.Equal("time_attendance", await ScalarAsync("SELECT module FROM dbo.permissions WHERE code = N'attendance:read'"));
        Assert.Equal("work_management", await ScalarAsync("SELECT module FROM dbo.permissions WHERE code = N'projects:create'"));
        Assert.Equal("org", await ScalarAsync(
            "SELECT module_key FROM dbo.module_permission_ownership WHERE permission_code = N'test:ownership'"));
    }

    [Fact]
    public async Task Feature_owner_module_is_rewritten()
    {
        await RowFactory.InsertAsync(_admin, "module_features", new Dictionary<string, object?>
        {
            ["feature_key"] = "org.test_feature",
            ["module_key"] = "org_structure",
        });

        await ReconcileAsync();

        Assert.Equal("org", await ScalarAsync("SELECT module_key FROM dbo.module_features WHERE feature_key = N'org.test_feature'"));
    }

    [Fact]
    public async Task Integration_links_are_rewritten_without_duplicates()
    {
        foreach (var module in new[] { "tasks", "projects", "work_management" })
        {
            await RowFactory.InsertAsync(_admin, "module_integration_links", new Dictionary<string, object?>
            {
                ["module_key"] = module,
                ["integration_key"] = "test_integration",
            });
        }
        await RowFactory.InsertAsync(_admin, "module_integration_links", new Dictionary<string, object?>
        {
            ["module_key"] = "activity_monitoring",
            ["integration_key"] = "test_integration",
        });

        await ReconcileAsync();

        Assert.Equal(1, await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.module_integration_links WHERE integration_key = N'test_integration' AND module_key = N'work_management'"));
        Assert.Equal(1, await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.module_integration_links WHERE integration_key = N'test_integration' AND module_key = N'monitoring'"));
        Assert.Equal(2, await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.module_integration_links WHERE integration_key = N'test_integration'"));
    }

    [Fact]
    public async Task Running_twice_gives_the_same_result()
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "tenant_subscriptions", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["selected_modules"] = """["tasks","leave"]""",
        });

        await ReconcileAsync();
        await ReconcileAsync();

        Assert.Equal("""["work_management","time_off"]""",
            await ScalarAsync($"SELECT selected_modules FROM dbo.tenant_subscriptions WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Feature_access_grants_and_price_history_are_not_touched()
    {
        var grantId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "feature_access_grants", new Dictionary<string, object?>
        {
            ["id"] = grantId,
            ["module"] = "leave",
        });
        await RowFactory.InsertAsync(_admin, "module_catalog_price_history", new Dictionary<string, object?>
        {
            ["id"] = priceId,
            ["module_key"] = "tasks",
        });

        await ReconcileAsync();

        Assert.Equal("leave", await ScalarAsync($"SELECT module FROM dbo.feature_access_grants WHERE id = '{grantId}'"));
        Assert.Equal("tasks", await ScalarAsync($"SELECT module_key FROM dbo.module_catalog_price_history WHERE id = '{priceId}'"));
    }
}
