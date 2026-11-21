using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Improvements task 36 (DEC-075 to DEC-077): one <c>monitoring_settings</c> table for every scope.
    /// <list type="bullet">
    /// <item>Copies <c>monitoring_feature_toggles</c> (a legal entity row becomes scope <c>legal_entity</c>;
    /// the legacy null-legal-entity row becomes scope <c>tenant</c> with scope_id = tenant_id),
    /// <c>monitoring_policy_overrides</c> (department, position, role, work_mode, as they are) and
    /// <c>employee_monitoring_overrides</c> (scope <c>employee</c>). Ids are kept, so audit log entries
    /// still point at the same rows. A policy row with any other scope type was never read by the
    /// resolver and is not copied; it stays in the old table.</item>
    /// <item>"Not set at this level": a base row (tenant, legal_entity) sets every switch, enforced
    /// by ck_monitoring_settings_base_rows_set_every_switch; on an override row null means inherit.</item>
    /// <item>The three old tables stay for one release and nothing reads or writes them. A later
    /// migration drops them.</item>
    /// <item>The copy runs in the 'admin' tenant context mode, because the old tables are under
    /// tenant RLS for every login; the new table gets its own policy after the copy.</item>
    /// </list>
    /// </summary>
    public partial class AddMonitoringSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "bit", nullable: true),
                    application_tracking = table.Column<bool>(type: "bit", nullable: true),
                    document_tracking = table.Column<bool>(type: "bit", nullable: true),
                    communication_tracking = table.Column<bool>(type: "bit", nullable: true),
                    screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    auto_screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    meeting_detection = table.Column<bool>(type: "bit", nullable: true),
                    device_tracking = table.Column<bool>(type: "bit", nullable: true),
                    work_location_verification = table.Column<bool>(type: "bit", nullable: true),
                    identity_verification = table.Column<bool>(type: "bit", nullable: true),
                    biometric = table.Column<bool>(type: "bit", nullable: true),
                    photo_required = table.Column<bool>(type: "bit", nullable: true),
                    idle_threshold_minutes = table.Column<int>(type: "int", nullable: true),
                    allowed_radius_meters = table.Column<int>(type: "int", nullable: true),
                    override_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    set_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_settings", x => x.id);
                    table.CheckConstraint("ck_monitoring_settings_base_rows_set_every_switch", "[scope_type] NOT IN ('tenant', 'legal_entity') OR ([activity_monitoring] IS NOT NULL AND [application_tracking] IS NOT NULL AND [document_tracking] IS NOT NULL AND [communication_tracking] IS NOT NULL AND [screenshot_capture] IS NOT NULL AND [auto_screenshot_capture] IS NOT NULL AND [meeting_detection] IS NOT NULL AND [device_tracking] IS NOT NULL AND [work_location_verification] IS NOT NULL AND [identity_verification] IS NOT NULL AND [biometric] IS NOT NULL)");
                    table.CheckConstraint("ck_monitoring_settings_scope_type", "[scope_type] IN ('tenant', 'legal_entity', 'department', 'position', 'role', 'work_mode', 'employee')");
                    table.CheckConstraint("ck_monitoring_settings_tenant_scope_id", "[scope_type] <> 'tenant' OR [scope_id] = [tenant_id]");
                    table.ForeignKey(
                        name: "fk_monitoring_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_settings_tenant_scope_id",
                table: "monitoring_settings",
                columns: new[] { "tenant_id", "scope_id" });

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_settings_tenant_scope",
                table: "monitoring_settings",
                columns: new[] { "tenant_id", "scope_type", "scope_id" },
                unique: true);

            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                INSERT INTO dbo.monitoring_settings
                    (id, tenant_id, scope_type, scope_id,
                     activity_monitoring, application_tracking, document_tracking, communication_tracking,
                     screenshot_capture, auto_screenshot_capture, meeting_detection, device_tracking,
                     work_location_verification, identity_verification, biometric,
                     photo_required, idle_threshold_minutes, allowed_radius_meters,
                     override_reason, set_by_id, created_at, updated_at)
                SELECT t.id, t.tenant_id,
                       CASE WHEN t.legal_entity_id IS NULL THEN 'tenant' ELSE 'legal_entity' END,
                       COALESCE(t.legal_entity_id, t.tenant_id),
                       t.activity_monitoring, t.application_tracking, t.document_tracking, t.communication_tracking,
                       t.screenshot_capture, t.auto_screenshot_capture, t.meeting_detection, t.device_tracking,
                       t.work_location_verification, t.identity_verification, t.biometric,
                       NULL, t.idle_threshold_minutes, t.allowed_radius_meters,
                       NULL, NULL, t.created_at, t.updated_at
                FROM dbo.monitoring_feature_toggles t
                WHERE EXISTS (SELECT 1 FROM dbo.tenants x WHERE x.id = t.tenant_id);

                INSERT INTO dbo.monitoring_settings
                    (id, tenant_id, scope_type, scope_id,
                     activity_monitoring, application_tracking, document_tracking, communication_tracking,
                     screenshot_capture, auto_screenshot_capture, meeting_detection, device_tracking,
                     work_location_verification, identity_verification, biometric,
                     photo_required, idle_threshold_minutes, allowed_radius_meters,
                     override_reason, set_by_id, created_at, updated_at)
                SELECT p.id, p.tenant_id, p.scope_type, p.scope_id,
                       p.activity_monitoring, p.application_tracking, p.document_tracking, p.communication_tracking,
                       p.screenshot_capture, p.auto_screenshot_capture, p.meeting_detection, p.device_tracking,
                       p.work_location_verification, p.identity_verification, p.biometric,
                       NULL, p.idle_threshold_minutes, p.allowed_radius_meters,
                       p.override_reason, p.set_by_id, p.created_at, p.updated_at
                FROM dbo.monitoring_policy_overrides p
                WHERE p.scope_type IN ('department', 'position', 'role', 'work_mode')
                  AND EXISTS (SELECT 1 FROM dbo.tenants x WHERE x.id = p.tenant_id);

                INSERT INTO dbo.monitoring_settings
                    (id, tenant_id, scope_type, scope_id,
                     activity_monitoring, application_tracking, document_tracking, communication_tracking,
                     screenshot_capture, auto_screenshot_capture, meeting_detection, device_tracking,
                     work_location_verification, identity_verification, biometric,
                     photo_required, idle_threshold_minutes, allowed_radius_meters,
                     override_reason, set_by_id, created_at, updated_at)
                SELECT e.id, e.tenant_id, 'employee', e.employee_id,
                       e.activity_monitoring, e.application_tracking, e.document_tracking, e.communication_tracking,
                       e.screenshot_capture, e.auto_screenshot_capture, e.meeting_detection, e.device_tracking,
                       e.work_location_verification, e.identity_verification, e.biometric,
                       e.photo_required, e.idle_threshold_minutes, e.allowed_radius_meters,
                       e.override_reason, e.set_by_id, e.created_at, e.updated_at
                FROM dbo.employee_monitoring_overrides e
                WHERE EXISTS (SELECT 1 FROM dbo.tenants x WHERE x.id = e.tenant_id);

                EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
                """);

            migrationBuilder.Sql(RlsSql.CreatePolicy("monitoring_settings", RlsPredicate.Tenant));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RlsSql.DropPolicy("monitoring_settings"));
            migrationBuilder.DropTable(
                name: "monitoring_settings");
        }
    }
}
