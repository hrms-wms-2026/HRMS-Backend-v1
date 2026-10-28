using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingWorkItemsFoundation : Migration
    {
        // RLS coverage is checked by TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage.
        private static readonly string[] TenantTables =
        [
            "meeting_series",
            "meeting_details",
            "meeting_occurrence_suppressions",
            "meeting_occurrence_attendees",
            "meeting_series_links",
            "meeting_attendance_sessions",
            "meeting_action_items"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every lock below is held until this one transaction commits (ALTER on the five calendar child tables is
            // ACCESS EXCLUSIVE). Never queue behind a long reader and stall the app: give up after 10 s and let the deploy
            // be retried off-peak (runbook).
            migrationBuilder.Sql("SET LOCAL lock_timeout = '10s';");

            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "external_calendar_event_links",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "meeting_task_id",
                table: "external_calendar_event_links",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "calendar_event_participants",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "meeting_series_id",
                table: "calendar_event_participants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "calendar_event_meetings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "calendar_event_meetings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "calendar_event");

            migrationBuilder.AddColumn<Guid>(
                name: "attendance_session_id",
                table: "calendar_event_meeting_attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occurrence_task_id",
                table: "calendar_event_meeting_attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participant_key",
                table: "calendar_event_meeting_attendances",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "calendar_event_guests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "meeting_series_id",
                table: "calendar_event_guests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_leases",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    owner_instance_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_leases", x => x.job_name);
                });

            migrationBuilder.CreateTable(
                name: "meeting_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    dt_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recurrence_rule = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creation_path = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    split_from_series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    split_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    split_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_series", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_series_employees_organizer_employee_id",
                        column: x => x.organizer_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_series_meeting_series_split_from_series_id",
                        column: x => x.split_from_series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_series_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_details",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_all_day = table.Column<bool>(type: "boolean", nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    is_detached = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attendance_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    attendance_next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attendance_checkpoint = table.Column<int>(type: "integer", nullable: false),
                    attendance_finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attendance_last_error = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_details", x => x.task_id);
                    table.UniqueConstraint("uq_meeting_details_series_original_start", x => new { x.series_id, x.original_start });
                    table.ForeignKey(
                        name: "fk_meeting_details_calendar_event_meetings_calendar_event_meet",
                        column: x => x.calendar_event_meeting_id,
                        principalTable: "calendar_event_meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_details_meeting_series_series_id",
                        column: x => x.series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_details_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_occurrence_suppressions",
                columns: table => new
                {
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moved_to_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_occurrence_suppressions", x => new { x.series_id, x.original_start });
                    table.ForeignKey(
                        name: "fk_meeting_occurrence_suppressions_meeting_series_series_id",
                        column: x => x.series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_occurrence_suppressions_work_tasks_moved_to_task_id",
                        column: x => x.moved_to_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_series_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_series_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_series_links_calendar_event_meetings_calendar_event",
                        column: x => x.calendar_event_meeting_id,
                        principalTable: "calendar_event_meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_series_links_meeting_series_series_id",
                        column: x => x.series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_action_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_action_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_action_items_meeting_details_occurrence_task_id",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_action_items_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_attendance_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    session_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    occurrence_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_attendance_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_attendance_sessions_calendar_event_meetings_calenda",
                        column: x => x.calendar_event_meeting_id,
                        principalTable: "calendar_event_meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_attendance_sessions_meeting_details_occurrence_task",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_occurrence_attendees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendee_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    change = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    response_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    response_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_occurrence_attendees", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_occurrence_attendees_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_occurrence_attendees_meeting_details_occurrence_tas",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_external_calendar_event_links_meeting_task_connection",
                table: "external_calendar_event_links",
                columns: new[] { "meeting_task_id", "external_calendar_connection_id" },
                unique: true,
                filter: "meeting_task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_meeting_series_id",
                table: "calendar_event_participants",
                column: "meeting_series_id");

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_participants_series_employee",
                table: "calendar_event_participants",
                columns: new[] { "tenant_id", "meeting_series_id", "employee_id" },
                unique: true,
                filter: "meeting_series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meeting_attendances_occurrence",
                table: "calendar_event_meeting_attendances",
                columns: new[] { "tenant_id", "occurrence_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meeting_attendances_occurrence_task_id",
                table: "calendar_event_meeting_attendances",
                column: "occurrence_task_id");

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_meeting_attendances_session_participant_joined",
                table: "calendar_event_meeting_attendances",
                columns: new[] { "attendance_session_id", "participant_key", "joined_at" },
                unique: true,
                filter: "attendance_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_guests_meeting_series_id",
                table: "calendar_event_guests",
                column: "meeting_series_id");

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_guests_series_email",
                table: "calendar_event_guests",
                columns: new[] { "tenant_id", "meeting_series_id", "email" },
                unique: true,
                filter: "meeting_series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_action_items_occurrence",
                table: "meeting_action_items",
                columns: new[] { "tenant_id", "occurrence_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_action_items_occurrence_task_id",
                table: "meeting_action_items",
                column: "occurrence_task_id");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_action_items_task_id",
                table: "meeting_action_items",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_attendance_sessions_occurrence",
                table: "meeting_attendance_sessions",
                columns: new[] { "tenant_id", "occurrence_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_attendance_sessions_occurrence_task_id",
                table: "meeting_attendance_sessions",
                column: "occurrence_task_id");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_attendance_sessions_provider_session",
                table: "meeting_attendance_sessions",
                columns: new[] { "calendar_event_meeting_id", "provider_session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_details_attendance_due",
                table: "meeting_details",
                columns: new[] { "tenant_id", "attendance_next_check_at" },
                filter: "attendance_finalized_at IS NULL AND attendance_next_check_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_details_calendar_event_meeting",
                table: "meeting_details",
                columns: new[] { "tenant_id", "calendar_event_meeting_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_details_calendar_event_meeting_id",
                table: "meeting_details",
                column: "calendar_event_meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_details_tenant_id_start_at",
                table: "meeting_details",
                columns: new[] { "tenant_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_attendees_employee_id",
                table: "meeting_occurrence_attendees",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_attendees_tenant_id_employee_id",
                table: "meeting_occurrence_attendees",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_meeting_occurrence_attendees_employee",
                table: "meeting_occurrence_attendees",
                columns: new[] { "occurrence_task_id", "employee_id" },
                unique: true,
                filter: "attendee_kind = 'employee'");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_suppressions_moved_to",
                table: "meeting_occurrence_suppressions",
                columns: new[] { "tenant_id", "moved_to_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_occurrence_suppressions_moved_to_task_id",
                table: "meeting_occurrence_suppressions",
                column: "moved_to_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_objective_id",
                table: "meeting_series",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_organizer",
                table: "meeting_series",
                columns: new[] { "tenant_id", "organizer_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_organizer_employee_id",
                table: "meeting_series",
                column: "organizer_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_placement",
                table: "meeting_series",
                columns: new[] { "tenant_id", "project_id", "objective_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_split_from",
                table: "meeting_series",
                columns: new[] { "tenant_id", "split_from_series_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_split_from_series_id",
                table: "meeting_series",
                column: "split_from_series_id");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_series_approval_request_id",
                table: "meeting_series",
                column: "approval_request_id",
                unique: true,
                filter: "approval_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_links_active_by_link",
                table: "meeting_series_links",
                columns: new[] { "tenant_id", "calendar_event_meeting_id" },
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_series_links_calendar_event_meeting_id",
                table: "meeting_series_links",
                column: "calendar_event_meeting_id");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_series_links_one_active_per_series",
                table: "meeting_series_links",
                column: "series_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_series_links_series_link",
                table: "meeting_series_links",
                columns: new[] { "series_id", "calendar_event_meeting_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_guests_meeting_series_meeting_series_id",
                table: "calendar_event_guests",
                column: "meeting_series_id",
                principalTable: "meeting_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_meeting_attendances_meeting_attendance_sessi",
                table: "calendar_event_meeting_attendances",
                column: "attendance_session_id",
                principalTable: "meeting_attendance_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_meeting_attendances_meeting_details_occurren",
                table: "calendar_event_meeting_attendances",
                column: "occurrence_task_id",
                principalTable: "meeting_details",
                principalColumn: "task_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participants_meeting_series_meeting_series_id",
                table: "calendar_event_participants",
                column: "meeting_series_id",
                principalTable: "meeting_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_external_calendar_event_links_meeting_details_meeting_task_",
                table: "external_calendar_event_links",
                column: "meeting_task_id",
                principalTable: "meeting_details",
                principalColumn: "task_id",
                onDelete: ReferentialAction.Restrict);
            // Composite foreign keys need these as their referenced keys (spec 4.1).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_tasks_tenant_id_id ON tasks (tenant_id, id);
                CREATE UNIQUE INDEX ux_objectives_tenant_project_id ON objectives (tenant_id, project_id, id);
                CREATE UNIQUE INDEX ux_calendar_event_meetings_tenant_id_id ON calendar_event_meetings (tenant_id, id);
                CREATE UNIQUE INDEX ux_meeting_series_tenant_id_id ON meeting_series (tenant_id, id);
                CREATE UNIQUE INDEX ux_meeting_details_tenant_id_task_id ON meeting_details (tenant_id, task_id);
                CREATE UNIQUE INDEX ux_meeting_attendance_sessions_tenant_id_id ON meeting_attendance_sessions (tenant_id, id);
                """);

            // Tenant-composite FKs: a row can only point at a row of its own tenant. The plain FKs above only let EF
            // order inserts. The meeting series' (project, module) is the only placement authority.
            migrationBuilder.Sql("""
                ALTER TABLE meeting_series
                    ADD CONSTRAINT fk_meeting_series_placement FOREIGN KEY (tenant_id, project_id, objective_id)
                        REFERENCES objectives (tenant_id, project_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_series_split_from_tenant FOREIGN KEY (tenant_id, split_from_series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE meeting_details
                    ADD CONSTRAINT fk_meeting_details_task_tenant FOREIGN KEY (tenant_id, task_id)
                        REFERENCES tasks (tenant_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_details_series_tenant FOREIGN KEY (tenant_id, series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_details_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id)
                        REFERENCES calendar_event_meetings (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE meeting_occurrence_suppressions
                    ADD CONSTRAINT fk_meeting_occurrence_suppressions_series_tenant FOREIGN KEY (tenant_id, series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_occurrence_suppressions_moved_to_tenant FOREIGN KEY (tenant_id, moved_to_task_id)
                        REFERENCES tasks (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE meeting_occurrence_attendees
                    ADD CONSTRAINT fk_meeting_occurrence_attendees_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id)
                        REFERENCES meeting_details (tenant_id, task_id) ON DELETE RESTRICT;
                ALTER TABLE meeting_series_links
                    ADD CONSTRAINT fk_meeting_series_links_series_tenant FOREIGN KEY (tenant_id, series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_series_links_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id)
                        REFERENCES calendar_event_meetings (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE meeting_attendance_sessions
                    ADD CONSTRAINT fk_meeting_attendance_sessions_link_tenant FOREIGN KEY (tenant_id, calendar_event_meeting_id)
                        REFERENCES calendar_event_meetings (tenant_id, id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_attendance_sessions_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id)
                        REFERENCES meeting_details (tenant_id, task_id) ON DELETE RESTRICT;
                ALTER TABLE meeting_action_items
                    ADD CONSTRAINT fk_meeting_action_items_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id)
                        REFERENCES meeting_details (tenant_id, task_id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_meeting_action_items_task_tenant FOREIGN KEY (tenant_id, task_id)
                        REFERENCES tasks (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE calendar_event_participants
                    ADD CONSTRAINT fk_calendar_event_participants_series_tenant FOREIGN KEY (tenant_id, meeting_series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE calendar_event_guests
                    ADD CONSTRAINT fk_calendar_event_guests_series_tenant FOREIGN KEY (tenant_id, meeting_series_id)
                        REFERENCES meeting_series (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE calendar_event_meeting_attendances
                    ADD CONSTRAINT fk_calendar_event_meeting_attendances_occurrence_tenant FOREIGN KEY (tenant_id, occurrence_task_id)
                        REFERENCES meeting_details (tenant_id, task_id) ON DELETE RESTRICT,
                    ADD CONSTRAINT fk_calendar_event_meeting_attendances_session_tenant FOREIGN KEY (tenant_id, attendance_session_id)
                        REFERENCES meeting_attendance_sessions (tenant_id, id) ON DELETE RESTRICT;
                ALTER TABLE external_calendar_event_links
                    ADD CONSTRAINT fk_external_calendar_event_links_meeting_task_tenant FOREIGN KEY (tenant_id, meeting_task_id)
                        REFERENCES meeting_details (tenant_id, task_id) ON DELETE RESTRICT;
                """);

            // Exactly one owner per calendar child row. Existing tables: NOT VALID, then VALIDATE as its own step - in this
            // single transaction the tables stay locked until commit either way; every existing row has the legacy owner,
            // so the scan cannot fail (preflight: 0 violations).
            migrationBuilder.Sql("""
                ALTER TABLE calendar_event_participants ADD CONSTRAINT ck_calendar_event_participants_one_owner
                    CHECK (num_nonnulls(event_id, meeting_series_id) = 1) NOT VALID;
                ALTER TABLE calendar_event_participants VALIDATE CONSTRAINT ck_calendar_event_participants_one_owner;
                ALTER TABLE calendar_event_guests ADD CONSTRAINT ck_calendar_event_guests_one_owner
                    CHECK (num_nonnulls(event_id, meeting_series_id) = 1) NOT VALID;
                ALTER TABLE calendar_event_guests VALIDATE CONSTRAINT ck_calendar_event_guests_one_owner;
                ALTER TABLE calendar_event_meetings ADD CONSTRAINT ck_calendar_event_meetings_owner
                    CHECK ((owner_kind = 'calendar_event' AND calendar_event_id IS NOT NULL)
                        OR (owner_kind = 'meeting_series' AND calendar_event_id IS NULL)) NOT VALID;
                ALTER TABLE calendar_event_meetings VALIDATE CONSTRAINT ck_calendar_event_meetings_owner;
                ALTER TABLE external_calendar_event_links ADD CONSTRAINT ck_external_calendar_event_links_one_owner
                    CHECK (num_nonnulls(calendar_event_id, meeting_task_id) = 1) NOT VALID;
                ALTER TABLE external_calendar_event_links VALIDATE CONSTRAINT ck_external_calendar_event_links_one_owner;

                ALTER TABLE meeting_occurrence_attendees
                    ADD CONSTRAINT ck_meeting_occurrence_attendees_kind
                        CHECK ((attendee_kind = 'employee' AND employee_id IS NOT NULL AND email IS NULL)
                            OR (attendee_kind = 'guest' AND email IS NOT NULL AND employee_id IS NULL)),
                    ADD CONSTRAINT ck_meeting_occurrence_attendees_change CHECK (change IN ('add', 'remove'));
                ALTER TABLE meeting_series
                    ADD CONSTRAINT ck_meeting_series_creation_path CHECK (creation_path IN ('direct', 'approval', 'calendar_office')),
                    ADD CONSTRAINT ck_meeting_series_split_reason
                        CHECK (split_reason IS NULL OR split_reason IN ('edit_following', 'move_forward', 'move_single'));
                ALTER TABLE meeting_details ADD CONSTRAINT ck_meeting_details_time CHECK (end_at >= start_at);
                ALTER TABLE meeting_series_links
                    ADD CONSTRAINT ck_meeting_series_links_origin CHECK (origin IN ('created', 'inherited_on_split'));
                ALTER TABLE meeting_occurrence_suppressions
                    ADD CONSTRAINT ck_meeting_occurrence_suppressions_reason CHECK (reason IN ('moved_single'));

                -- One guest row per email per occurrence (EF cannot model an expression index).
                CREATE UNIQUE INDEX ux_meeting_occurrence_attendees_guest_email
                    ON meeting_occurrence_attendees (occurrence_task_id, lower(email)) WHERE attendee_kind = 'guest';
                """);

            // spec 5.6: an occurrence's key never changes inside its series, so the NOT DEFERRABLE unique constraint can
            // be an ON CONFLICT arbiter with no transient collision. A remap into another (new, empty) series may change
            // original_start; the task id never changes.
            migrationBuilder.Sql("""
                CREATE FUNCTION meeting_details_key_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.task_id <> OLD.task_id
                       OR (NEW.series_id = OLD.series_id AND NEW.original_start IS DISTINCT FROM OLD.original_start) THEN
                        RAISE EXCEPTION 'trg_meeting_details_key_immutable: occurrence % cannot change its key within series %',
                            OLD.task_id, OLD.series_id
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER trg_meeting_details_key_immutable
                    BEFORE UPDATE OF task_id, series_id, original_start ON meeting_details
                    FOR EACH ROW EXECUTE FUNCTION meeting_details_key_immutable();
                """);

            // spec 4.1: a meeting task and its meeting_details row exist together, 1:1. Deferred to commit, and each check
            // re-reads the current rows, so the pair may be written in either order within one transaction. The functions
            // run in admin RLS mode: under the caller's context a check that cannot see the rows - e.g. the tenant context
            // switched before COMMIT - would pass instead of failing. They set it with set_config(..., true) and put the
            // caller's mode back before returning; a function-level `SET app.tenant_context_mode` is refused to the
            // non-superuser onevo_migrator (42501). They only look rows up by id and return nothing; the composite FKs keep
            // each pair in one tenant.
            migrationBuilder.Sql("""
                CREATE FUNCTION meeting_task_has_details() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    prev_mode text := current_setting('app.tenant_context_mode', true);
                    current_kind text;
                BEGIN
                    PERFORM set_config('app.tenant_context_mode', 'admin', true);
                    SELECT t.task_kind INTO current_kind FROM tasks t WHERE t.id = NEW.id;
                    IF current_kind = 'meeting'
                       AND NOT EXISTS (SELECT 1 FROM meeting_details d WHERE d.task_id = NEW.id) THEN
                        RAISE EXCEPTION 'trg_meeting_task_has_details: meeting task % has no meeting_details row', NEW.id
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    IF current_kind IS DISTINCT FROM 'meeting'
                       AND EXISTS (SELECT 1 FROM meeting_details d WHERE d.task_id = NEW.id) THEN
                        RAISE EXCEPTION 'trg_meeting_task_has_details: task % has a meeting_details row but is not a meeting', NEW.id
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    PERFORM set_config('app.tenant_context_mode', coalesce(prev_mode, ''), true);
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER trg_meeting_task_has_details
                    AFTER INSERT OR UPDATE OF task_kind ON tasks
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW WHEN (NEW.task_kind = 'meeting')
                    EXECUTE FUNCTION meeting_task_has_details();
                CREATE CONSTRAINT TRIGGER trg_meeting_task_left_meeting_kind
                    AFTER UPDATE OF task_kind ON tasks
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW WHEN (OLD.task_kind = 'meeting' AND NEW.task_kind IS DISTINCT FROM 'meeting')
                    EXECUTE FUNCTION meeting_task_has_details();

                CREATE FUNCTION meeting_details_has_meeting_task() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    prev_mode text := current_setting('app.tenant_context_mode', true);
                BEGIN
                    PERFORM set_config('app.tenant_context_mode', 'admin', true);
                    IF TG_OP = 'DELETE' THEN
                        IF EXISTS (SELECT 1 FROM tasks t WHERE t.id = OLD.task_id AND t.task_kind = 'meeting')
                           AND NOT EXISTS (SELECT 1 FROM meeting_details d WHERE d.task_id = OLD.task_id) THEN
                            RAISE EXCEPTION 'trg_meeting_details_has_meeting_task: meeting task % lost its meeting_details row', OLD.task_id
                                USING ERRCODE = 'integrity_constraint_violation';
                        END IF;
                        PERFORM set_config('app.tenant_context_mode', coalesce(prev_mode, ''), true);
                        RETURN NULL;
                    END IF;
                    IF EXISTS (SELECT 1 FROM meeting_details d WHERE d.task_id = NEW.task_id)
                       AND NOT EXISTS (SELECT 1 FROM tasks t WHERE t.id = NEW.task_id AND t.task_kind = 'meeting') THEN
                        RAISE EXCEPTION 'trg_meeting_details_has_meeting_task: meeting_details % belongs to a task that is not a meeting', NEW.task_id
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    PERFORM set_config('app.tenant_context_mode', coalesce(prev_mode, ''), true);
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER trg_meeting_details_has_meeting_task
                    AFTER INSERT OR DELETE ON meeting_details
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION meeting_details_has_meeting_task();
                """);

            // spec 4.1: every meeting task's (project, module) equals its series' placement. Checked at commit from
            // whichever side changed: the task, its meeting_details row, or the series.
            migrationBuilder.Sql("""
                CREATE FUNCTION meeting_placement_matches_series() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    prev_mode text := current_setting('app.tenant_context_mode', true);
                    target_series uuid;
                    target_task uuid;
                    mismatched uuid;
                BEGIN
                    PERFORM set_config('app.tenant_context_mode', 'admin', true);
                    -- Separate branches: PL/pgSQL plans only the branch it runs, and each table has different columns.
                    IF TG_TABLE_NAME = 'meeting_series' THEN
                        target_series := NEW.id;
                    ELSIF TG_TABLE_NAME = 'meeting_details' THEN
                        target_task := NEW.task_id;
                    ELSE
                        target_task := NEW.id;
                    END IF;
                    SELECT d.task_id INTO mismatched
                    FROM meeting_details d
                    JOIN tasks t ON t.id = d.task_id
                    JOIN meeting_series s ON s.id = d.series_id
                    WHERE (d.series_id = target_series OR d.task_id = target_task)
                      AND (t.project_id, t.objective_id) IS DISTINCT FROM (s.project_id, s.objective_id)
                    LIMIT 1;
                    IF mismatched IS NOT NULL THEN
                        RAISE EXCEPTION 'trg_meeting_placement_matches_series: meeting task % is not at its series'' project and module', mismatched
                            USING ERRCODE = 'integrity_constraint_violation';
                    END IF;
                    PERFORM set_config('app.tenant_context_mode', coalesce(prev_mode, ''), true);
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER trg_meeting_placement_matches_series
                    AFTER UPDATE OF project_id, objective_id ON tasks
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW WHEN (NEW.task_kind = 'meeting')
                    EXECUTE FUNCTION meeting_placement_matches_series();
                CREATE CONSTRAINT TRIGGER trg_meeting_placement_matches_series
                    AFTER INSERT OR UPDATE OF series_id ON meeting_details
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION meeting_placement_matches_series();
                CREATE CONSTRAINT TRIGGER trg_meeting_placement_matches_series
                    AFTER UPDATE OF project_id, objective_id ON meeting_series
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION meeting_placement_matches_series();
                """);

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
            // spec 8: never roll back below Release A once meeting data exists - dropping these tables would lose
            // meetings, and restoring NOT NULL would fail on series-owned rows. Raising aborts the whole rollback.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    PERFORM set_config('app.tenant_context_mode', 'admin', true);
                    IF EXISTS (SELECT 1 FROM meeting_series)
                       OR EXISTS (SELECT 1 FROM tasks WHERE task_kind = 'meeting')
                       OR EXISTS (SELECT 1 FROM calendar_event_participants WHERE meeting_series_id IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM calendar_event_guests WHERE meeting_series_id IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM calendar_event_meetings WHERE owner_kind = 'meeting_series')
                       OR EXISTS (SELECT 1 FROM external_calendar_event_links WHERE meeting_task_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'AddMeetingWorkItemsFoundation cannot be rolled back: meeting data exists (meetings-as-tasks Phase 2 spec 8)';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_meeting_task_has_details ON tasks;
                DROP TRIGGER IF EXISTS trg_meeting_task_left_meeting_kind ON tasks;
                DROP TRIGGER IF EXISTS trg_meeting_placement_matches_series ON tasks;
                DROP TRIGGER IF EXISTS trg_meeting_placement_matches_series ON meeting_details;
                DROP TRIGGER IF EXISTS trg_meeting_placement_matches_series ON meeting_series;
                DROP TRIGGER IF EXISTS trg_meeting_details_has_meeting_task ON meeting_details;
                DROP TRIGGER IF EXISTS trg_meeting_details_key_immutable ON meeting_details;
                DROP FUNCTION IF EXISTS meeting_placement_matches_series();
                DROP FUNCTION IF EXISTS meeting_details_has_meeting_task();
                DROP FUNCTION IF EXISTS meeting_task_has_details();
                DROP FUNCTION IF EXISTS meeting_details_key_immutable();

                ALTER TABLE calendar_event_participants
                    DROP CONSTRAINT IF EXISTS ck_calendar_event_participants_one_owner,
                    DROP CONSTRAINT IF EXISTS fk_calendar_event_participants_series_tenant;
                ALTER TABLE calendar_event_guests
                    DROP CONSTRAINT IF EXISTS ck_calendar_event_guests_one_owner,
                    DROP CONSTRAINT IF EXISTS fk_calendar_event_guests_series_tenant;
                ALTER TABLE calendar_event_meetings DROP CONSTRAINT IF EXISTS ck_calendar_event_meetings_owner;
                ALTER TABLE calendar_event_meeting_attendances
                    DROP CONSTRAINT IF EXISTS fk_calendar_event_meeting_attendances_occurrence_tenant,
                    DROP CONSTRAINT IF EXISTS fk_calendar_event_meeting_attendances_session_tenant;
                ALTER TABLE external_calendar_event_links
                    DROP CONSTRAINT IF EXISTS ck_external_calendar_event_links_one_owner,
                    DROP CONSTRAINT IF EXISTS fk_external_calendar_event_links_meeting_task_tenant;
                """);

            // The meeting tables (and their own composite FKs, CHECKs, indexes and policies) are dropped below.
            migrationBuilder.Sql("""
                ALTER TABLE meeting_series
                    DROP CONSTRAINT IF EXISTS fk_meeting_series_placement,
                    DROP CONSTRAINT IF EXISTS fk_meeting_series_split_from_tenant;
                ALTER TABLE meeting_details
                    DROP CONSTRAINT IF EXISTS fk_meeting_details_task_tenant,
                    DROP CONSTRAINT IF EXISTS fk_meeting_details_link_tenant;
                ALTER TABLE meeting_occurrence_suppressions DROP CONSTRAINT IF EXISTS fk_meeting_occurrence_suppressions_moved_to_tenant;
                ALTER TABLE meeting_series_links DROP CONSTRAINT IF EXISTS fk_meeting_series_links_link_tenant;
                ALTER TABLE meeting_attendance_sessions DROP CONSTRAINT IF EXISTS fk_meeting_attendance_sessions_link_tenant;
                ALTER TABLE meeting_action_items DROP CONSTRAINT IF EXISTS fk_meeting_action_items_task_tenant;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_guests_meeting_series_meeting_series_id",
                table: "calendar_event_guests");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_meeting_attendances_meeting_attendance_sessi",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_meeting_attendances_meeting_details_occurren",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participants_meeting_series_meeting_series_id",
                table: "calendar_event_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_external_calendar_event_links_meeting_details_meeting_task_",
                table: "external_calendar_event_links");

            migrationBuilder.DropTable(
                name: "job_leases");

            migrationBuilder.DropTable(
                name: "meeting_action_items");

            migrationBuilder.DropTable(
                name: "meeting_attendance_sessions");

            migrationBuilder.DropTable(
                name: "meeting_occurrence_attendees");

            migrationBuilder.DropTable(
                name: "meeting_occurrence_suppressions");

            migrationBuilder.DropTable(
                name: "meeting_series_links");

            migrationBuilder.DropTable(
                name: "meeting_details");

            migrationBuilder.DropTable(
                name: "meeting_series");

            migrationBuilder.DropIndex(
                name: "ux_external_calendar_event_links_meeting_task_connection",
                table: "external_calendar_event_links");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participants_meeting_series_id",
                table: "calendar_event_participants");

            migrationBuilder.DropIndex(
                name: "ux_calendar_event_participants_series_employee",
                table: "calendar_event_participants");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_meeting_attendances_occurrence",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_meeting_attendances_occurrence_task_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropIndex(
                name: "ux_calendar_event_meeting_attendances_session_participant_joined",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_guests_meeting_series_id",
                table: "calendar_event_guests");

            migrationBuilder.DropIndex(
                name: "ux_calendar_event_guests_series_email",
                table: "calendar_event_guests");

            migrationBuilder.DropColumn(
                name: "meeting_task_id",
                table: "external_calendar_event_links");

            migrationBuilder.DropColumn(
                name: "meeting_series_id",
                table: "calendar_event_participants");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "calendar_event_meetings");

            migrationBuilder.DropColumn(
                name: "attendance_session_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropColumn(
                name: "occurrence_task_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropColumn(
                name: "participant_key",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropColumn(
                name: "meeting_series_id",
                table: "calendar_event_guests");

            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "external_calendar_event_links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "calendar_event_participants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "calendar_event_meetings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                table: "calendar_event_guests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ux_tasks_tenant_id_id;
                DROP INDEX IF EXISTS ux_objectives_tenant_project_id;
                DROP INDEX IF EXISTS ux_calendar_event_meetings_tenant_id_id;
                """);

        }
    }
}
