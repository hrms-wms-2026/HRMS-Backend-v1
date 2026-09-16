using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyWorkAreaColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "legacy_work_mode_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "allowed_radius_meters",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_biometric_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_location_check_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_photo_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_source_rule",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_tray_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "either_web_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "field_biometric_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "field_photo_requirement",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "field_tray_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "field_web_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "location_verification_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "onsite_biometric_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "onsite_photo_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "onsite_tray_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "onsite_web_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "remote_biometric_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "remote_location_check_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "remote_photo_required",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "remote_tray_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "remote_web_enabled",
                table: "clock_in_policies");

            migrationBuilder.DropColumn(
                name: "expected_work_area",
                table: "attendance_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "legacy_work_mode_id",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "allowed_radius_meters",
                table: "clock_in_policies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "either_biometric_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "either_location_check_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "either_photo_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "either_source_rule",
                table: "clock_in_policies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "either_tray_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "either_web_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "field_biometric_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "field_photo_requirement",
                table: "clock_in_policies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "field_tray_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "field_web_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "location_verification_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "onsite_biometric_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "onsite_photo_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "onsite_tray_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "onsite_web_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "remote_biometric_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "remote_location_check_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "remote_photo_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "remote_tray_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "remote_web_enabled",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "expected_work_area",
                table: "attendance_records",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }
    }
}
