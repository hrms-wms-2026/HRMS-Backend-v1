using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAreaChangeRequestWorkModeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "current_work_mode_id",
                table: "work_area_change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "current_work_mode_name",
                table: "work_area_change_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "requested_work_mode_id",
                table: "work_area_change_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_work_mode_name",
                table: "work_area_change_requests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_work_mode_id",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "current_work_mode_name",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "requested_work_mode_id",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "requested_work_mode_name",
                table: "work_area_change_requests");
        }
    }
}
