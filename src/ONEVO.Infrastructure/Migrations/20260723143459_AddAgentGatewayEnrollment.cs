using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentGatewayEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_enrollment_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    device_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    os_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    agent_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    authorization_code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_enrollment_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_health_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cpu_usage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    memory_mb = table.Column<int>(type: "integer", nullable: false),
                    errors_json = table.Column<string>(type: "jsonb", nullable: false),
                    tamper_detected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_health_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_json = table.Column<string>(type: "jsonb", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registered_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    device_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    os_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    agent_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registered_agents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_challenges_device_id",
                table: "agent_enrollment_challenges",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_enrollment_challenges_expires_at",
                table: "agent_enrollment_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_agent_health_logs_agent_id_reported_at",
                table: "agent_health_logs",
                columns: new[] { "agent_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_policies_agent_id",
                table: "agent_policies",
                column: "agent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_sessions_device_id",
                table: "agent_sessions",
                column: "device_id",
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_registered_agents_tenant_id_device_id",
                table: "registered_agents",
                columns: new[] { "tenant_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registered_agents_tenant_id_employee_id",
                table: "registered_agents",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registered_agents_tenant_id_status",
                table: "registered_agents",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_enrollment_challenges");

            migrationBuilder.DropTable(
                name: "agent_health_logs");

            migrationBuilder.DropTable(
                name: "agent_policies");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "registered_agents");
        }
    }
}
