using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingClockInOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "location_capture_override",
                table: "onboarding_drafts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "photo_required_override",
                table: "onboarding_drafts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "photo_required",
                table: "employee_monitoring_overrides",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location_capture_override",
                table: "onboarding_drafts");

            migrationBuilder.DropColumn(
                name: "photo_required_override",
                table: "onboarding_drafts");

            migrationBuilder.DropColumn(
                name: "photo_required",
                table: "employee_monitoring_overrides");
        }
    }
}
