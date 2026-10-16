using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarMeetingIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_meeting_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    exception_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_meeting_incidents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_meeting_incidents_exception",
                table: "calendar_meeting_incidents",
                columns: new[] { "tenant_id", "exception_id" });

            migrationBuilder.CreateIndex(
                name: "ux_calendar_meeting_incidents_once",
                table: "calendar_meeting_incidents",
                columns: new[] { "tenant_id", "employee_id", "rule_code", "event_id", "occurrence_start" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_meeting_incidents");
        }
    }
}
