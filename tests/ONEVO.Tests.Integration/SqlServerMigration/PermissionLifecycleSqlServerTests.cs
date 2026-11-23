using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.Authorization;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Proves on real SQL Server (RLS on, delete cascade on) that a rename keeps grants and overrides,
/// that a default grant reaches an existing tenant's Owner role, and that a refused retirement
/// rolls the whole run back. Runs as the app login in admin mode, like PermissionSeeder.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PermissionLifecycleSqlServerTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 11, 19);
    private readonly Guid _tenant = Guid.NewGuid();
    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync();
        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await using (var mode = TenantRlsInterceptor.SetTenantCommand(_admin, "", "admin"))
            await mode.ExecuteNonQueryAsync();
        await RowFactory.DisableConstraintsAsync(_admin);
        // Parents (tenants, users) are not built, but the cascade under test must stay live.
        await ExecAsync("""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' WITH NOCHECK CHECK CONSTRAINT ' + QUOTENAME(name) + N';'
            FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'permissions');
            EXEC sp_executesql @sql;
            """);
    }

    public async Task DisposeAsync() => await _admin.DisposeAsync();

    private async Task ExecAsync(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> PermissionAsync(string code)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "permissions", new Dictionary<string, object?>
        {
            ["id"] = id, ["code"] = code, ["description"] = code, ["module"] = "core_hr", ["retired_at"] = null,
        });
        return id;
    }

    private async Task<Guid> RoleAsync(string name, bool isSystem)
    {
        var id = Guid.NewGuid();
        await RowFactory.InsertAsync(_admin, "roles", new Dictionary<string, object?>
        {
            ["id"] = id, ["tenant_id"] = _tenant, ["name"] = name, ["is_system"] = isSystem, ["is_deleted"] = false,
            ["source_template_id"] = null,
        });
        return id;
    }

    private Task GrantAsync(Guid role, Guid permission) =>
        RowFactory.InsertAsync(_admin, "role_permissions", new Dictionary<string, object?>
        {
            ["role_id"] = role, ["permission_id"] = permission, ["tenant_id"] = _tenant,
        });

    private Task OverrideAsync(Guid permission) =>
        RowFactory.InsertAsync(_admin, "user_permission_overrides", new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(), ["tenant_id"] = _tenant, ["user_id"] = Guid.NewGuid(),
            ["permission_id"] = permission, ["grant_type"] = "grant",
        });

    private async Task<PermissionSyncReport> SyncAsync(PermissionLifecycleRules rules, params string[] catalogue)
    {
        var tenant = new TenantContextAccessor();
        tenant.SetAdminMode();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_database.App)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenant))
            .Options;
        var clock = new SystemDateTimeProvider();
        await using var db = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenant);
        return await new PermissionCatalogSynchronizer(db, clock, NullLogger.Instance).SyncAsync(
            catalogue.Select(c => new PermissionDefinition(c, "core_hr", c, PermissionKind.Assignable)).ToList(), rules);
    }

    [Fact]
    public async Task Rename_KeepsRoleGrantsAndOverrides_UnderTheNewCode()
    {
        var old = await PermissionAsync("reports:view");
        await GrantAsync(await RoleAsync("Analyst", false), old);
        await OverrideAsync(old);

        await SyncAsync(new([new PermissionRename("reports:view", "reports:read", Day, "test")], [], []), "reports:read");

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM permissions WHERE code = N'reports:view'"));
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id
            WHERE p.code = N'reports:read'
            """));
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM user_permission_overrides o JOIN permissions p ON p.id = o.permission_id
            WHERE p.code = N'reports:read'
            """));
    }

    [Fact]
    public async Task DefaultGrant_ReachesTheOwnerRoleOfAnExistingTenant()
    {
        var owner = await RoleAsync("Owner", true);

        await SyncAsync(
            new([], [new PermissionDefaultGrant("reports:export", [DefaultGrantTarget.Owner], Day, "test")], []),
            "reports:export");

        Assert.Equal(1, await CountAsync($"""
            SELECT COUNT(*) FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id
            WHERE p.code = N'reports:export' AND rp.role_id = '{owner}'
            """));
    }

    [Fact]
    public async Task RefusedRetirement_RollsBackTheWholeRun()
    {
        var code = await PermissionAsync("chat:read");
        await GrantAsync(await RoleAsync("R", false), code);

        await Assert.ThrowsAsync<PermissionRetirementRefusedException>(() => SyncAsync(
            new([], [], [new PermissionRetirement("chat:read", RetirementStage.Removed, "test")]),
            "chat:write"));

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM role_permissions"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM permissions WHERE code = N'chat:write'"));
    }
}
