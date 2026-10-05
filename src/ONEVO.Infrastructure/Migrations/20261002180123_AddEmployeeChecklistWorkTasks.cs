using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeChecklistWorkTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "task_kind",
                table: "tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "standard");

            migrationBuilder.AddColumn<string>(
                name: "visibility_scope",
                table: "tasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "module");

            migrationBuilder.AddColumn<string>(
                name: "system_purpose",
                table: "projects",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "work_task_id",
                table: "employee_checklist_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_project_id_visibility_scope",
                table: "tasks",
                columns: new[] { "tenant_id", "project_id", "visibility_scope" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_system_purpose",
                table: "projects",
                columns: new[] { "tenant_id", "system_purpose" },
                unique: true,
                filter: "system_purpose IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_work_task_id",
                table: "employee_checklist_tasks",
                column: "work_task_id",
                unique: true,
                filter: "work_task_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_checklist_tasks_work_tasks_work_task_id",
                table: "employee_checklist_tasks",
                column: "work_task_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employee_checklist_tasks_work_tasks_work_task_id",
                table: "employee_checklist_tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_id_project_id_visibility_scope",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_projects_tenant_id_system_purpose",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_employee_checklist_tasks_work_task_id",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "task_kind",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "visibility_scope",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "system_purpose",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "work_task_id",
                table: "employee_checklist_tasks");
        }
    }
}
