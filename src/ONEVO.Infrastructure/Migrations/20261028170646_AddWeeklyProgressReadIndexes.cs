using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyProgressReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_tenant_id_employee_id_clock_in_at",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "employee_id", "clock_in_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_clocking_sessions_tenant_id_employee_id_clock_in_at",
                table: "task_clocking_sessions");
        }
    }
}
