using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// Improvements task 36: 20261121144123_AddMonitoringSettings copies the three old monitoring
/// tables into monitoring_settings row for row, under RLS, and the new table rejects a base row
/// that leaves a switch unset.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MonitoringSettingsMigrationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string MigrationBefore = "20261120015440_AddReferenceForeignKeysAndStatusChecks39a";

    private TestDatabase _database = null!;
    private SqlConnection _admin = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _orphanTenant = Guid.NewGuid();
    private readonly Guid _legalEntity = Guid.NewGuid();
    private readonly Guid _companyRow = Guid.NewGuid();
    private readonly Guid _tenantFallbackRow = Guid.NewGuid();
    private readonly Guid _roleRow = Guid.NewGuid();
    private readonly Guid _workModeRow = Guid.NewGuid();
    private readonly Guid _unknownScopeRow = Guid.NewGuid();
    private readonly Guid _employeeRow = Guid.NewGuid();
    private readonly Guid _orphanRow = Guid.NewGuid();
    private readonly Guid _role = Guid.NewGuid();
    private readonly Guid _workMode = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _setBy = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync(migrate: false);
        await using (var context = _database.CreateMigratorContext())
            await context.GetService<IMigrator>().MigrateAsync(MigrationBefore);

        _admin = new SqlConnection(_database.Admin);
        await _admin.OpenAsync();
        await AdminModeAsync();
        await RowFactory.DisableConstraintsAsync(_admin);

        foreach (var (id, slug) in new[] { (_tenant, "t36-a"), (_otherTenant, "t36-b") })
            await RowFactory.InsertAsync(_admin, "tenants", new Dictionary<string, object?> { ["id"] = id, ["slug"] = slug });

        var now = DateTimeOffset.UtcNow;
        await Toggles(_companyRow, _tenant, _legalEntity, activity: true, idle: 30, radius: 250, now);
        await Toggles(_tenantFallbackRow, _otherTenant, null, activity: false, idle: null, radius: null, now);
        await Toggles(_orphanRow, _orphanTenant, Guid.NewGuid(), activity: true, idle: null, radius: null, now);
        await Policy(_roleRow, "role", _role, screenshot: true, idle: 10, now);
        await Policy(_workModeRow, "work_mode", _workMode, screenshot: null, idle: null, now);
        await Policy(_unknownScopeRow, "team", Guid.NewGuid(), screenshot: false, idle: null, now);
        await RowFactory.InsertAsync(_admin, "employee_monitoring_overrides", new Dictionary<string, object?>
        {
            ["id"] = _employeeRow, ["tenant_id"] = _tenant, ["employee_id"] = _employee,
            ["photo_required"] = true, ["work_location_verification"] = false, ["activity_monitoring"] = null,
            ["allowed_radius_meters"] = 100, ["override_reason"] = "Set during onboarding", ["set_by_id"] = _setBy,
            ["created_at"] = now, ["updated_at"] = now,
        });

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

    private Task Toggles(Guid id, Guid tenant, Guid? legalEntity, bool activity, int? idle, int? radius, DateTimeOffset now) =>
        RowFactory.InsertAsync(_admin, "monitoring_feature_toggles", new Dictionary<string, object?>
        {
            ["id"] = id, ["tenant_id"] = tenant, ["legal_entity_id"] = legalEntity,
            ["activity_monitoring"] = activity, ["application_tracking"] = true, ["document_tracking"] = false,
            ["communication_tracking"] = false, ["screenshot_capture"] = true, ["auto_screenshot_capture"] = false,
            ["meeting_detection"] = false, ["device_tracking"] = true, ["work_location_verification"] = true,
            ["identity_verification"] = false, ["biometric"] = false,
            ["idle_threshold_minutes"] = idle, ["allowed_radius_meters"] = radius,
            ["created_at"] = now, ["updated_at"] = now,
        });

    private Task Policy(Guid id, string scope, Guid scopeId, bool? screenshot, int? idle, DateTimeOffset now) =>
        RowFactory.InsertAsync(_admin, "monitoring_policy_overrides", new Dictionary<string, object?>
        {
            ["id"] = id, ["tenant_id"] = _tenant, ["scope_type"] = scope, ["scope_id"] = scopeId,
            ["screenshot_capture"] = screenshot, ["idle_threshold_minutes"] = idle,
            ["override_reason"] = $"{scope} rule", ["set_by_id"] = _setBy,
            ["created_at"] = now, ["updated_at"] = now,
        });

    private async Task<Dictionary<string, object?>?> RowAsync(Guid id)
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = "SELECT * FROM monitoring_settings WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i));
    }

    [Fact]
    public async Task A_legal_entity_default_becomes_a_legal_entity_row_with_the_same_values()
    {
        var row = (await RowAsync(_companyRow))!;

        row["scope_type"].Should().Be("legal_entity");
        row["scope_id"].Should().Be(_legalEntity);
        row["tenant_id"].Should().Be(_tenant);
        row["activity_monitoring"].Should().Be(true);
        row["screenshot_capture"].Should().Be(true);
        row["document_tracking"].Should().Be(false);
        row["idle_threshold_minutes"].Should().Be(30);
        row["allowed_radius_meters"].Should().Be(250);
        row["override_reason"].Should().BeNull();
        row["photo_required"].Should().BeNull();
    }

    [Fact]
    public async Task The_tenant_fallback_row_becomes_tenant_scope_with_the_tenant_as_scope_id()
    {
        var row = (await RowAsync(_tenantFallbackRow))!;

        row["scope_type"].Should().Be("tenant");
        row["scope_id"].Should().Be(_otherTenant);
        row["activity_monitoring"].Should().Be(false);
        row["idle_threshold_minutes"].Should().BeNull();
    }

    [Fact]
    public async Task Policy_overrides_keep_their_scope_values_and_nulls()
    {
        var role = (await RowAsync(_roleRow))!;
        role["scope_type"].Should().Be("role");
        role["scope_id"].Should().Be(_role);
        role["screenshot_capture"].Should().Be(true);
        role["activity_monitoring"].Should().BeNull("an unset override switch still inherits");
        role["idle_threshold_minutes"].Should().Be(10);
        role["override_reason"].Should().Be("role rule");
        role["set_by_id"].Should().Be(_setBy);

        var workMode = (await RowAsync(_workModeRow))!;
        workMode["scope_type"].Should().Be("work_mode");
        workMode["scope_id"].Should().Be(_workMode);
        workMode["screenshot_capture"].Should().BeNull();
    }

    [Fact]
    public async Task Employee_overrides_become_employee_rows_with_photo_required()
    {
        var row = (await RowAsync(_employeeRow))!;

        row["scope_type"].Should().Be("employee");
        row["scope_id"].Should().Be(_employee);
        row["photo_required"].Should().Be(true);
        row["work_location_verification"].Should().Be(false);
        row["activity_monitoring"].Should().BeNull();
        row["allowed_radius_meters"].Should().Be(100);
        row["override_reason"].Should().Be("Set during onboarding");
    }

    [Fact]
    public async Task Rows_the_resolver_never_read_are_not_copied()
    {
        (await RowAsync(_unknownScopeRow)).Should().BeNull("scope 'team' was never read");
        (await RowAsync(_orphanRow)).Should().BeNull("its tenant does not exist");

        await using var count = _admin.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM monitoring_settings";
        ((int)(await count.ExecuteScalarAsync())!).Should().Be(5);
    }

    [Fact]
    public async Task The_old_tables_are_kept()
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM monitoring_feature_toggles)
                 + (SELECT COUNT(*) FROM monitoring_policy_overrides)
                 + (SELECT COUNT(*) FROM employee_monitoring_overrides)
            """;
        ((int)(await command.ExecuteScalarAsync())!).Should().Be(7);
    }

    [Fact]
    public async Task A_base_row_with_an_unset_switch_is_rejected()
    {
        await using var command = _admin.CreateCommand();
        command.CommandText = $"""
            INSERT INTO monitoring_settings (id, tenant_id, scope_type, scope_id, activity_monitoring, created_at, updated_at)
            VALUES (NEWID(), '{_tenant}', 'legal_entity', NEWID(), 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """;

        var insert = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());

        insert.Number.Should().Be(547);
    }

    [Fact]
    public async Task An_override_row_may_leave_switches_unset_but_not_use_an_unknown_scope()
    {
        await using var ok = _admin.CreateCommand();
        ok.CommandText = $"""
            INSERT INTO monitoring_settings (id, tenant_id, scope_type, scope_id, screenshot_capture, created_at, updated_at)
            VALUES (NEWID(), '{_tenant}', 'department', NEWID(), 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """;
        await ok.ExecuteNonQueryAsync();

        await using var bad = _admin.CreateCommand();
        bad.CommandText = $"""
            INSERT INTO monitoring_settings (id, tenant_id, scope_type, scope_id, created_at, updated_at)
            VALUES (NEWID(), '{_tenant}', 'team', NEWID(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """;
        (await Assert.ThrowsAsync<SqlException>(() => bad.ExecuteNonQueryAsync())).Number.Should().Be(547);
    }
}
