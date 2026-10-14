using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.DataMigrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingAttendanceSyncStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attendance_sync_status",
                table: "calendar_event_meetings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(AttendanceSyncStatusBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attendance_sync_status",
                table: "calendar_event_meetings");
        }
    }
}
