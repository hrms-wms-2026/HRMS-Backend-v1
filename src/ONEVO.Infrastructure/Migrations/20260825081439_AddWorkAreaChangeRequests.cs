using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAreaChangeRequests : Migration
    {
        private static readonly string[] TenantTables = ["work_area_change_requests"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_area_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    current_expected_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_area_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_employee_date",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_employee_id",
                table: "work_area_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_legal_entity_id",
                table: "work_area_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_reviewed_by_id",
                table: "work_area_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_status",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_legal_entity_status",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "legal_entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_work_area_change_requests_active_employee_date",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                filter: "status IN ('pending', 'approved')");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (tenant_id::text = current_setting('app.current_tenant_id', true))
                        WITH CHECK (tenant_id::text = current_setting('app.current_tenant_id', true));
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
                name: "work_area_change_requests");
        }
    }
}
