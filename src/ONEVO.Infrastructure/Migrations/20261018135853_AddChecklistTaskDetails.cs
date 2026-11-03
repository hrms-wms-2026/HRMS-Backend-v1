using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklistTaskDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attachment_file_ids_json",
                table: "employee_checklist_tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "employee_checklist_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "employee_checklist_tasks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_file_ids_json",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "description",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "employee_checklist_tasks");
        }
    }
}
