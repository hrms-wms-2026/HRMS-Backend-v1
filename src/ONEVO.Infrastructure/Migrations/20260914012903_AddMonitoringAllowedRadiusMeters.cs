using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAllowedRadiusMeters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "allowed_radius_meters",
                table: "monitoring_policy_overrides",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "allowed_radius_meters",
                table: "monitoring_feature_toggles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_radius_meters",
                table: "monitoring_policy_overrides");

            migrationBuilder.DropColumn(
                name: "allowed_radius_meters",
                table: "monitoring_feature_toggles");
        }
    }
}
