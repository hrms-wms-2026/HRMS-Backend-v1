using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringConsentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_consent_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_consent_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_consent_events_tenant_id_employee_id_occurred_at",
                table: "monitoring_consent_events",
                columns: new[] { "tenant_id", "employee_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_consent_events_tenant_id_incident_id",
                table: "monitoring_consent_events",
                columns: new[] { "tenant_id", "incident_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_consent_events");
        }
    }
}
