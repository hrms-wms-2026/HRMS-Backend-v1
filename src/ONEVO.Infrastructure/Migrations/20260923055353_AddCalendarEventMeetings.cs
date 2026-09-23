using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventMeetings : Migration
    {
        private static readonly string[] TenantTables = ["calendar_event_meetings", "calendar_event_meeting_attendances"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calendar_event_meeting_attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_participant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_participant_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_meeting_attendances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_calendar_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_meeting_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    join_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    organizer_join_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    passcode_or_pin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_attendance_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_meetings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meeting_attendances_tenant_id_meeting_id",
                table: "calendar_event_meeting_attendances",
                columns: new[] { "tenant_id", "calendar_event_meeting_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meetings_one_per_event",
                table: "calendar_event_meetings",
                column: "calendar_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meetings_tenant_id_connection_id",
                table: "calendar_event_meetings",
                columns: new[] { "tenant_id", "external_calendar_connection_id" });

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(
                name: "calendar_event_meeting_attendances");

            migrationBuilder.DropTable(
                name: "calendar_event_meetings");
        }
    }
}
