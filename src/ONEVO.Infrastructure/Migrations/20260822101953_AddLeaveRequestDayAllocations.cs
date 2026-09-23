using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRequestDayAllocations : Migration
    {
        private static readonly string[] TenantTables = ["leave_request_day_allocations"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_request_day_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_unit = table.Column<decimal>(type: "numeric(3,1)", nullable: false),
                    paid_unit = table.Column<decimal>(type: "numeric(3,1)", nullable: false),
                    unpaid_unit = table.Column<decimal>(type: "numeric(3,1)", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_day_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_day_allocations_leave_requests_leave_request_",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_leave_request_id",
                table: "leave_request_day_allocations",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_tenant_date_status",
                table: "leave_request_day_allocations",
                columns: new[] { "tenant_id", "leave_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_tenant_request_date",
                table: "leave_request_day_allocations",
                columns: new[] { "tenant_id", "leave_request_id", "leave_date" },
                unique: true);

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
                name: "leave_request_day_allocations");
        }
    }
}
