using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectiveAndProjectAchievedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "achieved_at",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_achieved",
                table: "projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "achieved_at",
                table: "objectives",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_achieved",
                table: "objectives",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_is_achieved",
                table: "projects",
                columns: new[] { "tenant_id", "is_achieved" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_project_id_is_achieved",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id", "is_achieved" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_projects_tenant_id_is_achieved",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_objectives_tenant_id_project_id_is_achieved",
                table: "objectives");

            migrationBuilder.DropColumn(
                name: "achieved_at",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "is_achieved",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "achieved_at",
                table: "objectives");

            migrationBuilder.DropColumn(
                name: "is_achieved",
                table: "objectives");
        }
    }
}
