using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingAttendanceSyncFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attendance_sync_error",
                table: "calendar_event_meetings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attendance_sync_failed_at",
                table: "calendar_event_meetings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attendance_sync_error",
                table: "calendar_event_meetings");

            migrationBuilder.DropColumn(
                name: "attendance_sync_failed_at",
                table: "calendar_event_meetings");
        }
    }
}
