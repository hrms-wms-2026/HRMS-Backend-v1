using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_consent_denial_notice",
                table: "activity_daily_summary",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "screenshot_denied_count",
                table: "activity_daily_summary",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "agent_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    command_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    consent_notice_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    monitoring_evidence_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_commands", x => x.id);
                    table.CheckConstraint("ck_agent_commands_decision", "decision IS NULL OR decision IN ('allow', 'deny')");
                    table.CheckConstraint("ck_agent_commands_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_agent_commands_status", "status IN ('pending', 'accepted', 'denied', 'completed', 'failed', 'expired')");
                    table.CheckConstraint("ck_agent_commands_type", "command_type IN ('screenshot_capture_request')");
                    table.ForeignKey(
                        name: "fk_agent_commands_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_commands_monitoring_evidence_assets_monitoring_eviden",
                        column: x => x.monitoring_evidence_asset_id,
                        principalTable: "monitoring_evidence_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_commands_registered_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_agent_id",
                table: "agent_commands",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_employee_id",
                table: "agent_commands",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_monitoring_evidence_asset_id",
                table: "agent_commands",
                column: "monitoring_evidence_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_tenant_id_agent_id_command_type",
                table: "agent_commands",
                columns: new[] { "tenant_id", "agent_id", "command_type" },
                unique: true,
                filter: "status IN ('pending', 'accepted')");

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_tenant_id_agent_id_status_available_at",
                table: "agent_commands",
                columns: new[] { "tenant_id", "agent_id", "status", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_tenant_id_employee_id_created_at",
                table: "agent_commands",
                columns: new[] { "tenant_id", "employee_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_commands");

            migrationBuilder.DropColumn(
                name: "has_consent_denial_notice",
                table: "activity_daily_summary");

            migrationBuilder.DropColumn(
                name: "screenshot_denied_count",
                table: "activity_daily_summary");
        }
    }
}
