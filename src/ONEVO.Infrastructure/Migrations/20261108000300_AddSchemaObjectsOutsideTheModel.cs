using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Schema objects the PostgreSQL migrations created with raw SQL, which the EF model does not
    /// describe. The composite foreign keys keep linked rows inside one tenant.
    /// SchemaParityTests compares this against docs/reports/database/2026-11-07-postgres-schema.json.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108000300_AddSchemaObjectsOutsideTheModel")]
    public class AddSchemaObjectsOutsideTheModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE dbo.global_email_directory (
                    email nvarchar(320) NOT NULL,
                    tenant_id uniqueidentifier NOT NULL,
                    created_at datetimeoffset NOT NULL CONSTRAINT df_global_email_directory_created_at DEFAULT SYSDATETIMEOFFSET(),
                    CONSTRAINT pk_global_email_directory PRIMARY KEY (email, tenant_id));
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_global_email_directory_email ON dbo.global_email_directory (email);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_calendar_event_meetings_tenant_id_id ON dbo.calendar_event_meetings (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_meeting_attendance_sessions_tenant_id_id ON dbo.meeting_attendance_sessions (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_meeting_details_tenant_id_task_id ON dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_meeting_series_tenant_id_id ON dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_objectives_tenant_project_id ON dbo.objectives (tenant_id, project_id, id);
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_tasks_tenant_id_id ON dbo.tasks (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_attendance_corrections_tenant_employee_work_date_type ON dbo.attendance_corrections (tenant_id, employee_id, work_date, correction_type);
                """);
            migrationBuilder.Sql("""
                CREATE INDEX ix_work_area_change_requests_tenant_employee_date ON dbo.work_area_change_requests (tenant_id, employee_id, date);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.departments ADD code_lower AS LOWER(code) PERSISTED;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower
                    ON dbo.departments (tenant_id, legal_entity_id, code_lower) WHERE code IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_attendees ADD email_lower AS LOWER(email) PERSISTED;
                """);
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_meeting_occurrence_attendees_guest_email
                    ON dbo.meeting_occurrence_attendees (occurrence_task_id, email_lower) WHERE attendee_kind = N'guest';
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_guests ADD CONSTRAINT fk_calendar_event_guests_series_tenant FOREIGN KEY (tenant_id, meeting_series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_meeting_attendances ADD CONSTRAINT fk_calendar_event_meeting_attendances_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id) REFERENCES dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_meeting_attendances ADD CONSTRAINT fk_calendar_event_meeting_attendances_session_tenant FOREIGN KEY (tenant_id, attendance_session_id) REFERENCES dbo.meeting_attendance_sessions (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_participants ADD CONSTRAINT fk_calendar_event_participants_series_tenant FOREIGN KEY (tenant_id, meeting_series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.external_calendar_event_links ADD CONSTRAINT fk_external_calendar_event_links_meeting_task_tenant FOREIGN KEY (tenant_id, meeting_task_id) REFERENCES dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.global_email_directory ADD CONSTRAINT fk_ged_tenant FOREIGN KEY (tenant_id) REFERENCES dbo.tenants (id) ON DELETE CASCADE;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_action_items ADD CONSTRAINT fk_meeting_action_items_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id) REFERENCES dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_action_items ADD CONSTRAINT fk_meeting_action_items_task_tenant FOREIGN KEY (tenant_id, task_id) REFERENCES dbo.tasks (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_attendance_sessions ADD CONSTRAINT fk_meeting_attendance_sessions_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id) REFERENCES dbo.calendar_event_meetings (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_attendance_sessions ADD CONSTRAINT fk_meeting_attendance_sessions_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id) REFERENCES dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_details ADD CONSTRAINT fk_meeting_details_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id) REFERENCES dbo.calendar_event_meetings (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_details ADD CONSTRAINT fk_meeting_details_series_tenant FOREIGN KEY (tenant_id, series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_details ADD CONSTRAINT fk_meeting_details_task_tenant FOREIGN KEY (tenant_id, task_id) REFERENCES dbo.tasks (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_attendees ADD CONSTRAINT fk_meeting_occurrence_attendees_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id) REFERENCES dbo.meeting_details (tenant_id, task_id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_suppressions ADD CONSTRAINT fk_meeting_occurrence_suppressions_moved_to_tenant FOREIGN KEY (tenant_id, moved_to_task_id) REFERENCES dbo.tasks (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_suppressions ADD CONSTRAINT fk_meeting_occurrence_suppressions_series_tenant FOREIGN KEY (tenant_id, series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series ADD CONSTRAINT fk_meeting_series_placement FOREIGN KEY (tenant_id, project_id, objective_id) REFERENCES dbo.objectives (tenant_id, project_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series ADD CONSTRAINT fk_meeting_series_split_from_tenant FOREIGN KEY (tenant_id, split_from_series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series_links ADD CONSTRAINT fk_meeting_series_links_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id) REFERENCES dbo.calendar_event_meetings (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series_links ADD CONSTRAINT fk_meeting_series_links_series_tenant FOREIGN KEY (tenant_id, series_id) REFERENCES dbo.meeting_series (tenant_id, id);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_guests ADD CONSTRAINT ck_calendar_event_guests_one_owner CHECK ((CASE WHEN event_id IS NULL THEN 0 ELSE 1 END + CASE WHEN meeting_series_id IS NULL THEN 0 ELSE 1 END) = 1);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_meetings ADD CONSTRAINT ck_calendar_event_meetings_owner CHECK ((owner_kind = N'calendar_event' AND calendar_event_id IS NOT NULL) OR (owner_kind = N'meeting_series' AND calendar_event_id IS NULL));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.calendar_event_participants ADD CONSTRAINT ck_calendar_event_participants_one_owner CHECK ((CASE WHEN event_id IS NULL THEN 0 ELSE 1 END + CASE WHEN meeting_series_id IS NULL THEN 0 ELSE 1 END) = 1);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.external_calendar_event_links ADD CONSTRAINT ck_external_calendar_event_links_one_owner CHECK ((CASE WHEN calendar_event_id IS NULL THEN 0 ELSE 1 END + CASE WHEN meeting_task_id IS NULL THEN 0 ELSE 1 END) = 1);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_details ADD CONSTRAINT ck_meeting_details_time CHECK (end_at >= start_at);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_attendees ADD CONSTRAINT ck_meeting_occurrence_attendees_change CHECK (change IN (N'add', N'remove'));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_attendees ADD CONSTRAINT ck_meeting_occurrence_attendees_kind CHECK ((attendee_kind = N'employee' AND employee_id IS NOT NULL AND email IS NULL) OR (attendee_kind = N'guest' AND email IS NOT NULL AND employee_id IS NULL));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_occurrence_suppressions ADD CONSTRAINT ck_meeting_occurrence_suppressions_reason CHECK (reason = N'moved_single');
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series ADD CONSTRAINT ck_meeting_series_creation_path CHECK (creation_path IN (N'direct', N'approval', N'calendar_office'));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series ADD CONSTRAINT ck_meeting_series_split_reason CHECK (split_reason IS NULL OR split_reason IN (N'edit_following', N'move_forward', N'move_single'));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE dbo.meeting_series_links ADD CONSTRAINT ck_meeting_series_links_origin CHECK (origin IN (N'created', N'inherited_on_split'));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("The SQL Server baseline has no earlier state to return to.");
    }
}
