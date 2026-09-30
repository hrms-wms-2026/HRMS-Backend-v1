using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMonitorAlerts : Migration
    {
        private static readonly string[] TenantTables = ["wm_monitor_alerts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "overdue_notified_at",
                table: "sprints");

            migrationBuilder.CreateTable(
                name: "wm_monitor_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    rule_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    first_detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_monitor_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wm_monitor_alerts_tenant_id_project_id_resolved_at",
                table: "wm_monitor_alerts",
                columns: new[] { "tenant_id", "project_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_wm_monitor_alerts_open_key",
                table: "wm_monitor_alerts",
                columns: new[] { "tenant_id", "target_id", "rule_code", "subject_employee_id" },
                unique: true,
                filter: "resolved_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            // Tenant isolation, same policy as the other wm_* tables (FORCE RLS; admin mode for the
            // hourly cross-tenant ProjectMonitorJob sweep).
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
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");

            migrationBuilder.DropTable(
                name: "wm_monitor_alerts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_notified_at",
                table: "sprints",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
