using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionAssignmentsAndHierarchyClosure : Migration
    {
        private static readonly string[] TenantTables = ["position_assignments", "employee_hierarchy_closure"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_hierarchy_closure",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ancestor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descendant_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    source_position_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_hierarchy_closure", x => new { x.tenant_id, x.ancestor_employee_id, x.descendant_employee_id });
                });

            migrationBuilder.CreateTable(
                name: "position_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    assignment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_tenant_id_ancestor_employee_id_d",
                table: "employee_hierarchy_closure",
                columns: new[] { "tenant_id", "ancestor_employee_id", "depth" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_tenant_id_descendant_employee_id",
                table: "employee_hierarchy_closure",
                columns: new[] { "tenant_id", "descendant_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_one_active_primary_per_employee",
                table: "position_assignments",
                column: "employee_id",
                unique: true,
                filter: "assignment_kind = 'PrimaryEmployment' AND assignment_status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_position_id",
                table: "position_assignments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_tenant_id_employee_id",
                table: "position_assignments",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_tenant_id_position_id_assignment_status",
                table: "position_assignments",
                columns: new[] { "tenant_id", "position_id", "assignment_status" });

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
                name: "employee_hierarchy_closure");

            migrationBuilder.DropTable(
                name: "position_assignments");
        }
    }
}
