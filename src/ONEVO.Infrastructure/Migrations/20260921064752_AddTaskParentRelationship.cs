using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskParentRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_tasks_parent_task_id",
                table: "tasks",
                column: "parent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks",
                columns: new[] { "tenant_id", "parent_task_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_tasks_parent_task_id",
                table: "tasks",
                column: "parent_task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_tasks_parent_task_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_parent_task_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks");
        }
    }
}
