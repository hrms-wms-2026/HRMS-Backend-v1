using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewTaskIndexesFromQueryStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks",
                columns: new[] { "tenant_id", "parent_task_id", "objective_id" })
                .Annotation("SqlServer:Include", new[] { "status_id", "task_kind", "is_deleted", "estimated_hours", "progress_percent", "due_date", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments",
                column: "employee_id")
                .Annotation("SqlServer:Include", new[] { "task_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks",
                columns: new[] { "tenant_id", "parent_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_employee_id",
                table: "task_assignments",
                column: "employee_id");
        }
    }
}
