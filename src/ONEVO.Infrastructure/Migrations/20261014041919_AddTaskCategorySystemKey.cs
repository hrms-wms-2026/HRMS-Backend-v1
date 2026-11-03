using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.DataMigrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCategorySystemKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_key",
                table: "task_categories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(MeetingCategoryBackfill.Sql);

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_one_system_key_per_project",
                table: "task_categories",
                columns: new[] { "tenant_id", "project_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_categories_one_system_key_per_project",
                table: "task_categories");

            migrationBuilder.DropColumn(
                name: "system_key",
                table: "task_categories");
        }
    }
}
