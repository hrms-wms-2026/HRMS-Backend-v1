using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropSprintObjectiveId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive re-sync: project_id was always written at creation, but make sure it matches
            // the owning objective before that link is dropped for good.
            migrationBuilder.Sql(@"
                UPDATE sprints s
                SET project_id = o.project_id
                FROM objectives o
                WHERE s.objective_id = o.id AND s.project_id <> o.project_id;
            ");

            migrationBuilder.DropIndex(
                name: "ix_sprints_tenant_id_objective_id_status",
                table: "sprints");

            migrationBuilder.DropColumn(
                name: "objective_id",
                table: "sprints");

            migrationBuilder.CreateIndex(
                name: "ix_sprints_tenant_id_project_id_status",
                table: "sprints",
                columns: new[] { "tenant_id", "project_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sprints_tenant_id_project_id_status",
                table: "sprints");

            migrationBuilder.AddColumn<Guid>(
                name: "objective_id",
                table: "sprints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_sprints_tenant_id_objective_id_status",
                table: "sprints",
                columns: new[] { "tenant_id", "objective_id", "status" });
        }
    }
}
