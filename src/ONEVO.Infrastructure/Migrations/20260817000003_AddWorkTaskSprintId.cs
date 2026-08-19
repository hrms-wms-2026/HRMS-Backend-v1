using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTaskSprintId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sprint_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_sprint_id",
                table: "tasks",
                column: "sprint_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_sprints_sprint_id",
                table: "tasks",
                column: "sprint_id",
                principalTable: "sprints",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_sprints_sprint_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_sprint_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "sprint_id",
                table: "tasks");
        }
    }
}
