using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringPermissionConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_permission_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    screen_monitoring = table.Column<bool>(type: "boolean", nullable: false),
                    app_tracking = table.Column<bool>(type: "boolean", nullable: false),
                    location_access = table.Column<bool>(type: "boolean", nullable: false),
                    camera_access = table.Column<bool>(type: "boolean", nullable: false),
                    notifications = table.Column<bool>(type: "boolean", nullable: false),
                    keyboard_mouse = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_permission_consents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_permission_consents_tenant_employee_accepted",
                table: "monitoring_permission_consents",
                columns: new[] { "tenant_id", "employee_id", "accepted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_permission_consents");
        }
    }
}
