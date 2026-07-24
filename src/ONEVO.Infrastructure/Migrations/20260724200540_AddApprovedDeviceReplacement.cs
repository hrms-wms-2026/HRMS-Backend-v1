using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedDeviceReplacement : Migration
    {
        private static readonly string[] TenantTables =
        [
            "agent_device_change_requests"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_device_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_device_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_device_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_device_change_requests_registered_agents_current_agen",
                        column: x => x.current_agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_device_change_requests_registered_agents_requested_ag",
                        column: x => x.requested_agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_device_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_current_agent_id",
                table: "agent_device_change_requests",
                column: "current_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_employee_id",
                table: "agent_device_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_requested_agent_id",
                table: "agent_device_change_requests",
                column: "requested_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_reviewed_by_id",
                table: "agent_device_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_tenant_id_employee_id",
                table: "agent_device_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "\"status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_tenant_id_employee_id_status",
                table: "agent_device_change_requests",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_device_change_requests_tenant_id_requested_agent_id",
                table: "agent_device_change_requests",
                columns: new[] { "tenant_id", "requested_agent_id" });

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
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
                ");
            }

            migrationBuilder.DropTable(
                name: "agent_device_change_requests");
        }
    }
}
