using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntityWorkStartAndEndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_end_time",
                table: "legal_entities",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_start_time",
                table: "legal_entities",
                type: "time",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_work_time_pair",
                table: "legal_entities",
                sql: "(work_start_time IS NULL AND work_end_time IS NULL) OR (work_start_time IS NOT NULL AND work_end_time IS NOT NULL AND work_start_time < work_end_time)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_work_time_pair",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "work_end_time",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "work_start_time",
                table: "legal_entities");
        }
    }
}
