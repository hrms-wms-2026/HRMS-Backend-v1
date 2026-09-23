using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkModeLocationFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allows_daily_location_choice",
                table: "tenant_work_modes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "self_registers_location",
                table: "tenant_work_modes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // One-time backfill: any work mode currently named literally "remote" was relying on
            // SubmitCheckInCommandHandler's old name-match to self-register location. Preserve that
            // behavior for existing tenants without requiring them to re-toggle it manually. This is
            // a data migration only - the application never matches on this name again afterward.
            migrationBuilder.Sql(
                "UPDATE tenant_work_modes SET self_registers_location = TRUE WHERE lower(name) = 'remote';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allows_daily_location_choice",
                table: "tenant_work_modes");

            migrationBuilder.DropColumn(
                name: "self_registers_location",
                table: "tenant_work_modes");
        }
    }
}
