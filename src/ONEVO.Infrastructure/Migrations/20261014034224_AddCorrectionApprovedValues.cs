using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrectionApprovedValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approved_break_json",
                table: "attendance_corrections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_clock_in_at",
                table: "attendance_corrections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_clock_out_at",
                table: "attendance_corrections",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_break_json",
                table: "attendance_corrections");

            migrationBuilder.DropColumn(
                name: "approved_clock_in_at",
                table: "attendance_corrections");

            migrationBuilder.DropColumn(
                name: "approved_clock_out_at",
                table: "attendance_corrections");
        }
    }
}
