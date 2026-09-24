using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_daily_summary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_active_minutes = table.Column<int>(type: "integer", nullable: false),
                    total_idle_minutes = table.Column<int>(type: "integer", nullable: false),
                    total_meeting_minutes = table.Column<int>(type: "integer", nullable: false),
                    active_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    productive_app_minutes = table.Column<int>(type: "integer", nullable: false),
                    personal_app_minutes = table.Column<int>(type: "integer", nullable: false),
                    unknown_app_minutes = table.Column<int>(type: "integer", nullable: false),
                    focus_minutes = table.Column<int>(type: "integer", nullable: false),
                    activity_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    data_coverage_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    top_apps_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    intensity_avg = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyboard_total = table.Column<int>(type: "integer", nullable: false),
                    mouse_total = table.Column<int>(type: "integer", nullable: false),
                    document_time_minutes = table.Column<int>(type: "integer", nullable: false),
                    deep_focus_sessions_count = table.Column<int>(type: "integer", nullable: false),
                    data_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_daily_summary", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_raw_buffer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_raw_buffer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    keyboard_events_count = table.Column<int>(type: "integer", nullable: false),
                    mouse_events_count = table.Column<int>(type: "integer", nullable: false),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    idle_seconds = table.Column<int>(type: "integer", nullable: false),
                    intensity_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    foreground_process_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_monitoring_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "boolean", nullable: true),
                    application_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    document_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    communication_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    screenshot_capture = table.Column<bool>(type: "boolean", nullable: true),
                    auto_screenshot_capture = table.Column<bool>(type: "boolean", nullable: true),
                    meeting_detection = table.Column<bool>(type: "boolean", nullable: true),
                    device_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    work_location_verification = table.Column<bool>(type: "boolean", nullable: true),
                    identity_verification = table.Column<bool>(type: "boolean", nullable: true),
                    biometric = table.Column<bool>(type: "boolean", nullable: true),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    set_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_monitoring_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_feature_toggles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "boolean", nullable: false),
                    application_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    document_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    communication_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    screenshot_capture = table.Column<bool>(type: "boolean", nullable: false),
                    auto_screenshot_capture = table.Column<bool>(type: "boolean", nullable: false),
                    meeting_detection = table.Column<bool>(type: "boolean", nullable: false),
                    device_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    work_location_verification = table.Column<bool>(type: "boolean", nullable: false),
                    identity_verification = table.Column<bool>(type: "boolean", nullable: false),
                    biometric = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_feature_toggles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_policy_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "boolean", nullable: true),
                    application_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    document_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    communication_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    screenshot_capture = table.Column<bool>(type: "boolean", nullable: true),
                    auto_screenshot_capture = table.Column<bool>(type: "boolean", nullable: true),
                    meeting_detection = table.Column<bool>(type: "boolean", nullable: true),
                    device_tracking = table.Column<bool>(type: "boolean", nullable: true),
                    work_location_verification = table.Column<bool>(type: "boolean", nullable: true),
                    identity_verification = table.Column<bool>(type: "boolean", nullable: true),
                    biometric = table.Column<bool>(type: "boolean", nullable: true),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    set_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_policy_overrides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_daily_summary_tenant_employee_date_desc",
                table: "activity_daily_summary",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_device_received",
                table: "activity_raw_buffer",
                columns: new[] { "agent_device_id", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_tenant_received",
                table: "activity_raw_buffer",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_employee_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_employee_monitoring_overrides_tenant_employee",
                table: "employee_monitoring_overrides",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_feature_toggles_tenant",
                table: "monitoring_feature_toggles",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_policy_overrides_tenant_scope",
                table: "monitoring_policy_overrides",
                columns: new[] { "tenant_id", "scope_type", "scope_id" },
                unique: true);

            // PostgreSQL RLS — tenant isolation on all activity monitoring tables
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(
                name: "activity_daily_summary");

            migrationBuilder.DropTable(
                name: "activity_raw_buffer");

            migrationBuilder.DropTable(
                name: "activity_snapshots");

            migrationBuilder.DropTable(
                name: "employee_monitoring_overrides");

            migrationBuilder.DropTable(
                name: "monitoring_feature_toggles");

            migrationBuilder.DropTable(
                name: "monitoring_policy_overrides");
        }

        private static readonly string[] TenantTables =
        [
            "activity_snapshots",
            "activity_raw_buffer",
            "activity_daily_summary",
            "monitoring_feature_toggles",
            "employee_monitoring_overrides",
            "monitoring_policy_overrides"
        ];
    }
}
