using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceStateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_state_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_idle = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_state_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_employee_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_state_snapshots");
        }
    }
}
