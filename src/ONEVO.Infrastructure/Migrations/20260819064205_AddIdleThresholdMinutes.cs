using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdleThresholdMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "idle_threshold_minutes",
                table: "monitoring_policy_overrides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "idle_threshold_minutes",
                table: "monitoring_feature_toggles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "idle_threshold_minutes",
                table: "employee_monitoring_overrides",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "idle_threshold_minutes",
                table: "monitoring_policy_overrides");

            migrationBuilder.DropColumn(
                name: "idle_threshold_minutes",
                table: "monitoring_feature_toggles");

            migrationBuilder.DropColumn(
                name: "idle_threshold_minutes",
                table: "employee_monitoring_overrides");
        }
    }
}
