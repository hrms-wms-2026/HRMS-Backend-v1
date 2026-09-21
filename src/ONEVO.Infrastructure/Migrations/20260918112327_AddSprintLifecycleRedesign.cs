using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintLifecycleRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE sprints SET status = 'active' WHERE status IN ('future', 'incomplete');");

            migrationBuilder.DropColumn(
                name: "is_manually_overridden",
                table: "sprints");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "sprints",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "end_date",
                table: "sprints",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<string>(
                name: "goal",
                table: "sprints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_notified_at",
                table: "sprints",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "goal",
                table: "sprints");

            migrationBuilder.DropColumn(
                name: "overdue_notified_at",
                table: "sprints");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "sprints",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "end_date",
                table: "sprints",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_manually_overridden",
                table: "sprints",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
