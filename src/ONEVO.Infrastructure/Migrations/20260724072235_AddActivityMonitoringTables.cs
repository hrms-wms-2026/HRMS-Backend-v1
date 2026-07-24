using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityMonitoringTables : Migration
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
                    top_apps_json = table.Column<string>(type: "jsonb", nullable: false),
                    intensity_avg = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyboard_total = table.Column<int>(type: "integer", nullable: false),
                    mouse_total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_daily_summary", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    keyboard_events_count = table.Column<int>(type: "integer", nullable: false),
                    mouse_events_count = table.Column<int>(type: "integer", nullable: false),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    idle_seconds = table.Column<int>(type: "integer", nullable: false),
                    intensity_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    foreground_process_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "application_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_name_pattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_productive = table.Column<bool>(type: "boolean", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "application_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    process_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    application_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    application_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    window_title_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    total_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_productive = table.Column<bool>(type: "boolean", nullable: true),
                    is_allowed = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_usage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_tracking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    laptop_active_minutes = table.Column<int>(type: "integer", nullable: false),
                    estimated_mobile_minutes = table.Column<int>(type: "integer", nullable: false),
                    laptop_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    detection_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tracking", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meeting_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    meeting_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    had_camera_on = table.Column<bool>(type: "boolean", nullable: false),
                    had_mic_activity = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_evidence_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activity_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    file_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_evidence_assets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_daily_summary_tenant_id_employee_id_date",
                table: "activity_daily_summary",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_id_captured_at",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_id_employee_id_captured_at",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_application_categories_tenant_id_application_name_pattern",
                table: "application_categories",
                columns: new[] { "tenant_id", "application_name_pattern" });

            migrationBuilder.CreateIndex(
                name: "ix_application_usage_tenant_id_date_application_category",
                table: "application_usage",
                columns: new[] { "tenant_id", "date", "application_category" });

            migrationBuilder.CreateIndex(
                name: "ix_application_usage_tenant_id_employee_id_date",
                table: "application_usage",
                columns: new[] { "tenant_id", "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_application_usage_tenant_id_employee_id_date_is_allowed",
                table: "application_usage",
                columns: new[] { "tenant_id", "employee_id", "date", "is_allowed" });

            migrationBuilder.CreateIndex(
                name: "ix_device_tracking_tenant_id_employee_id_date",
                table: "device_tracking",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_sessions_tenant_id_employee_id_meeting_start",
                table: "meeting_sessions",
                columns: new[] { "tenant_id", "employee_id", "meeting_start" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_tenant_id_employee_id_captured_at",
                table: "monitoring_evidence_assets",
                columns: new[] { "tenant_id", "employee_id", "captured_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_daily_summary");

            migrationBuilder.DropTable(
                name: "activity_snapshots");

            migrationBuilder.DropTable(
                name: "application_categories");

            migrationBuilder.DropTable(
                name: "application_usage");

            migrationBuilder.DropTable(
                name: "device_tracking");

            migrationBuilder.DropTable(
                name: "meeting_sessions");

            migrationBuilder.DropTable(
                name: "monitoring_evidence_assets");
        }
    }
}
