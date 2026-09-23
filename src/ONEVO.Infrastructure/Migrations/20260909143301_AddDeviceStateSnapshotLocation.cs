using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceStateSnapshotLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "accuracy_meters",
                table: "device_state_snapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "device_state_snapshots",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "device_state_snapshots",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accuracy_meters",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "device_state_snapshots");
        }
    }
}
