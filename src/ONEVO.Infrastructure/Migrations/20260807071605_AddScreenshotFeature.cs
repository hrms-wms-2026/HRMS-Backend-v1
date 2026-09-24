using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenshotFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_commands", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    agent_command_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    retention_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legal_hold_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_evidence_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snap",
                        column: x => x.activity_snapshot_id,
                        principalTable: "activity_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_agent_commands_agent_command_id",
                        column: x => x.agent_command_id,
                        principalTable: "agent_commands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_tray_device_registrations_agent_",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_device_status_expires",
                table: "agent_commands",
                columns: new[] { "agent_device_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_tenant_device_created",
                table: "agent_commands",
                columns: new[] { "tenant_id", "agent_device_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_activity_snapshot_id",
                table: "monitoring_evidence_assets",
                column: "activity_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_agent_command_id",
                table: "monitoring_evidence_assets",
                column: "agent_command_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_agent_device_id",
                table: "monitoring_evidence_assets",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_file_record_id",
                table: "monitoring_evidence_assets",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_tenant_command",
                table: "monitoring_evidence_assets",
                columns: new[] { "tenant_id", "agent_command_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_tenant_employee_captured",
                table: "monitoring_evidence_assets",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            // Tenant isolation RLS — same admin/tenant context-mode pattern as
            // activity monitoring, departments, and check-in tables.
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
                name: "monitoring_evidence_assets");

            migrationBuilder.DropTable(
                name: "agent_commands");
        }

        private static readonly string[] TenantTables =
        [
            "agent_commands", "monitoring_evidence_assets"
        ];
    }
}
