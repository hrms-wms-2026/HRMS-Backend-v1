using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReworkWorkAreaChangeRequestToWorkModeIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_expected_work_area",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "requested_work_area",
                table: "work_area_change_requests");

            migrationBuilder.AlterColumn<string>(
                name: "requested_work_mode_name",
                table: "work_area_change_requests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "current_work_mode_name",
                table: "work_area_change_requests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legacy_work_area_label",
                table: "work_area_change_requests",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "expected_work_mode_id",
                table: "attendance_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_work_mode_name",
                table: "attendance_records",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_current_work_mode_id",
                table: "work_area_change_requests",
                column: "current_work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_requested_work_mode_id",
                table: "work_area_change_requests",
                column: "requested_work_mode_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_area_change_requests_time_attendance_work_modes_curren",
                table: "work_area_change_requests",
                column: "current_work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_work_area_change_requests_time_attendance_work_modes_reques",
                table: "work_area_change_requests",
                column: "requested_work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_area_change_requests_time_attendance_work_modes_curren",
                table: "work_area_change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_work_area_change_requests_time_attendance_work_modes_reques",
                table: "work_area_change_requests");

            migrationBuilder.DropIndex(
                name: "ix_work_area_change_requests_current_work_mode_id",
                table: "work_area_change_requests");

            migrationBuilder.DropIndex(
                name: "ix_work_area_change_requests_requested_work_mode_id",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "legacy_work_area_label",
                table: "work_area_change_requests");

            migrationBuilder.DropColumn(
                name: "expected_work_mode_id",
                table: "attendance_records");

            migrationBuilder.DropColumn(
                name: "expected_work_mode_name",
                table: "attendance_records");

            migrationBuilder.AlterColumn<string>(
                name: "requested_work_mode_name",
                table: "work_area_change_requests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "current_work_mode_name",
                table: "work_area_change_requests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AddColumn<string>(
                name: "current_expected_work_area",
                table: "work_area_change_requests",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "requested_work_area",
                table: "work_area_change_requests",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }
    }
}
