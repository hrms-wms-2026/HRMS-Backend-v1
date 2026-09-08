using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInactivityCaptureAttempts : Migration
    {
        private static readonly string[] TenantTables = ["inactivity_capture_attempts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inactivity_capture_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idle_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prompted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idle_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    monitor_count = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    evidence_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    policy_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactivity_capture_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_inactivity_capture_attempts_monitoring_evidence_assets_evid",
                        column: x => x.evidence_asset_id,
                        principalTable: "monitoring_evidence_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inactivity_capture_attempts_tray_device_registrations_agent",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_agent_device_id",
                table: "inactivity_capture_attempts",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_evidence_asset_id",
                table: "inactivity_capture_attempts",
                column: "evidence_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_tenant_employee_prompted",
                table: "inactivity_capture_attempts",
                columns: new[] { "tenant_id", "employee_id", "prompted_at" });

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
                name: "inactivity_capture_attempts");
        }
    }
}
