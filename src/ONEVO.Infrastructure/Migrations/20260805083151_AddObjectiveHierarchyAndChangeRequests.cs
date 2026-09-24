using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectiveHierarchyAndChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reporting_manager_id",
                table: "objectives",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "objective_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporting_manager_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objective_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_objective_change_requests_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_reporting_manager_id",
                table: "objectives",
                columns: new[] { "tenant_id", "reporting_manager_id" });

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_objective_id",
                table: "objective_change_requests",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_one_pending_per_objective",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "objective_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_tenant_id_objective_id_status",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "objective_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_objective_change_requests_tenant_id_reporting_manager_id_status",
                table: "objective_change_requests",
                columns: new[] { "tenant_id", "reporting_manager_id", "status" });

            migrationBuilder.Sql(@"
                ALTER TABLE objective_change_requests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE objective_change_requests FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON objective_change_requests;
                CREATE POLICY tenant_isolation ON objective_change_requests
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON objective_change_requests;
                ALTER TABLE objective_change_requests DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "objective_change_requests");

            migrationBuilder.DropIndex(
                name: "ix_objectives_tenant_id_reporting_manager_id",
                table: "objectives");

            migrationBuilder.DropColumn(
                name: "reporting_manager_id",
                table: "objectives");
        }
    }
}
