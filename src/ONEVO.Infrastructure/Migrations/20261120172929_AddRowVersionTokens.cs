using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Task 42: a rowversion concurrency token (xmin) on the tables users edit through forms. Left out: projects (its
    /// task and meeting counters change with raw SQL on every new task) and personal_calendar_events (meeting-link
    /// provisioning and calendar sync save the row in the background while users edit it, by design).
    /// </summary>
    public partial class AddRowVersionTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "tasks",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "sprints",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "roles",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "positions",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "objectives",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "legal_entities",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "leave_policies",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "departments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "xmin",
                table: "clock_in_policies",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "sprints");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "objectives");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "leave_policies");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "clock_in_policies");
        }
    }
}
