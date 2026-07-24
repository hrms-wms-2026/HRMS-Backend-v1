using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeAttendanceClocking : Migration
    {
        private static readonly string[] TenantTables =
        [
            "attendance_records",
            "break_records",
            "clock_in_policies",
            "device_sessions",
            "presence_sessions",
            "schedule_assignments",
            "work_area_change_requests",
            "work_schedule_days",
            "work_schedule_holidays",
            "work_schedules"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "break_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    break_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    break_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    break_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    auto_detected = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_break_records", x => x.id);
                    table.CheckConstraint("ck_break_records_end_order", "break_end IS NULL OR break_end >= break_start");
                    table.CheckConstraint("ck_break_records_type", "break_type IN ('lunch', 'prayer', 'smoke', 'personal', 'other')");
                    table.ForeignKey(
                        name: "fk_break_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clock_in_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    department_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    position_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    employee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    location_verification_required = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_radius_meters = table.Column<int>(type: "integer", nullable: true),
                    onsite_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    remote_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_location_check_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_source_rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    field_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_photo_requirement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correction_requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    notification_recipient_resolver = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_in_policies", x => x.id);
                    table.CheckConstraint("ck_clock_in_policies_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_clock_in_policies_either_source_rule", "either_source_rule IN ('onsite', 'remote', 'employee_choice')");
                    table.CheckConstraint("ck_clock_in_policies_field_photo_requirement", "field_photo_requirement IN ('off', 'optional', 'required')");
                    table.CheckConstraint("ck_clock_in_policies_radius", "allowed_radius_meters IS NULL OR allowed_radius_meters BETWEEN 25 AND 50000");
                    table.CheckConstraint("ck_clock_in_policies_scope_type", "scope_type IN ('full_company', 'department', 'position', 'employee')");
                    table.ForeignKey(
                        name: "fk_clock_in_policies_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clock_in_policies_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_minutes = table.Column<int>(type: "integer", nullable: false),
                    idle_minutes = table.Column<int>(type: "integer", nullable: false),
                    active_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_sessions", x => x.id);
                    table.CheckConstraint("ck_device_sessions_active_percentage", "active_percentage BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_device_sessions_end_order", "session_end IS NULL OR session_end >= session_start");
                    table.CheckConstraint("ck_device_sessions_minutes", "active_minutes >= 0 AND idle_minutes >= 0");
                    table.ForeignKey(
                        name: "fk_device_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_sessions_registered_agents_device_id",
                        column: x => x.device_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presence_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_present_minutes = table.Column<int>(type: "integer", nullable: false),
                    total_break_minutes = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_sessions", x => x.id);
                    table.CheckConstraint("ck_presence_sessions_minutes", "total_present_minutes >= 0 AND total_break_minutes >= 0");
                    table.CheckConstraint("ck_presence_sessions_seen_order", "last_seen_at >= first_seen_at");
                    table.CheckConstraint("ck_presence_sessions_source", "source IN ('biometric', 'agent', 'manual', 'mixed')");
                    table.CheckConstraint("ck_presence_sessions_status", "status IN ('present', 'absent', 'partial', 'on_leave')");
                    table.ForeignKey(
                        name: "fk_presence_sessions_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_area_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_expected_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_area_change_requests", x => x.id);
                    table.CheckConstraint("ck_work_area_change_requests_current_area", "current_expected_work_area IN ('onsite', 'remote', 'either', 'field')");
                    table.CheckConstraint("ck_work_area_change_requests_requested_area", "requested_work_area IN ('onsite', 'remote', 'either', 'field')");
                    table.CheckConstraint("ck_work_area_change_requests_status", "status IN ('pending', 'approved', 'rejected', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    pull_public_holidays = table.Column<bool>(type: "boolean", nullable: false),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_for_new_employee = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_schedules", x => x.id);
                    table.CheckConstraint("ck_work_schedules_public_holiday_country", "NOT pull_public_holidays OR country_code IS NOT NULL");
                    table.CheckConstraint("ck_work_schedules_timezone", "length(trim(timezone)) > 0");
                    table.ForeignKey(
                        name: "fk_work_schedules_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_working_day = table.Column<bool>(type: "boolean", nullable: false),
                    work_time_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    scheduled_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    scheduled_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    required_work_minutes = table.Column<int>(type: "integer", nullable: true),
                    expected_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    schedule_timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_holiday = table.Column<bool>(type: "boolean", nullable: false),
                    holiday_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actual_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    late_minutes = table.Column<int>(type: "integer", nullable: true),
                    short_minutes = table.Column<int>(type: "integer", nullable: true),
                    detected_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    attendance_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.CheckConstraint("ck_attendance_records_detected_work_area", "detected_work_area IS NULL OR detected_work_area IN ('onsite', 'remote', 'field')");
                    table.CheckConstraint("ck_attendance_records_expected_work_area", "expected_work_area IN ('onsite', 'remote', 'either', 'field')");
                    table.CheckConstraint("ck_attendance_records_minutes", "worked_minutes >= 0 AND break_minutes >= 0 AND (late_minutes IS NULL OR late_minutes >= 0) AND (short_minutes IS NULL OR short_minutes >= 0)");
                    table.CheckConstraint("ck_attendance_records_source", "attendance_source IN ('biometric', 'agent', 'web', 'manual', 'mixed')");
                    table.CheckConstraint("ck_attendance_records_status", "status IN ('on_time', 'late', 'short_hours', 'absent', 'work_area_mismatch', 'on_time_off', 'holiday', 'off_day')");
                    table.CheckConstraint("ck_attendance_records_work_time_type", "work_time_type IS NULL OR work_time_type IN ('fixed', 'flexible')");
                    table.ForeignKey(
                        name: "fk_attendance_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_records_work_schedules_work_schedule_id",
                        column: x => x.work_schedule_id,
                        principalTable: "work_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "schedule_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_default_for_new_employee = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_assignments", x => x.id);
                    table.CheckConstraint("ck_schedule_assignments_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_schedule_assignments_target", "(assignment_type = 'full_company' AND department_id IS NULL AND position_id IS NULL AND employee_id IS NULL) OR (assignment_type = 'department' AND department_id IS NOT NULL AND position_id IS NULL AND employee_id IS NULL) OR (assignment_type = 'position' AND department_id IS NULL AND position_id IS NOT NULL AND employee_id IS NULL) OR (assignment_type = 'employee' AND department_id IS NULL AND position_id IS NULL AND employee_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_schedule_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_assignments_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_assignments_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_schedule_assignments_work_schedules_work_schedule_id",
                        column: x => x.work_schedule_id,
                        principalTable: "work_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_schedule_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    is_working_day = table.Column<bool>(type: "boolean", nullable: false),
                    work_time_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    required_work_minutes = table.Column<int>(type: "integer", nullable: true),
                    break_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    break_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    expected_work_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    is_overnight = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_schedule_days", x => x.id);
                    table.CheckConstraint("ck_work_schedule_days_break", "(break_type = 'none' AND break_start_time IS NULL AND break_end_time IS NULL AND break_duration_minutes IS NULL) OR (break_type = 'fixed' AND break_start_time IS NOT NULL AND break_end_time IS NOT NULL AND break_duration_minutes IS NULL) OR (break_type = 'flexible' AND break_duration_minutes BETWEEN 1 AND 720 AND break_start_time IS NULL AND break_end_time IS NULL) OR break_type IS NULL");
                    table.CheckConstraint("ck_work_schedule_days_day_of_week", "day_of_week BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_work_schedule_days_work_time", "(work_time_type = 'fixed' AND start_time IS NOT NULL AND end_time IS NOT NULL AND required_work_minutes IS NULL) OR (work_time_type = 'flexible' AND required_work_minutes BETWEEN 1 AND 1440 AND start_time IS NULL AND end_time IS NULL) OR work_time_type IS NULL");
                    table.CheckConstraint("ck_work_schedule_days_working_state", "(is_working_day AND work_time_type IN ('fixed', 'flexible') AND break_type IN ('none', 'fixed', 'flexible') AND expected_work_area IN ('onsite', 'remote', 'either', 'field')) OR (NOT is_working_day AND work_time_type IS NULL AND break_type IS NULL AND expected_work_area IS NULL)");
                    table.ForeignKey(
                        name: "fk_work_schedule_days_work_schedules_work_schedule_id",
                        column: x => x.work_schedule_id,
                        principalTable: "work_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_schedule_holidays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_holiday_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_schedule_holidays", x => x.id);
                    table.CheckConstraint("ck_work_schedule_holidays_source", "source IN ('country_public_holiday', 'manual')");
                    table.ForeignKey(
                        name: "fk_work_schedule_holidays_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_schedule_holidays_work_schedules_work_schedule_id",
                        column: x => x.work_schedule_id,
                        principalTable: "work_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_presence_session_id",
                table: "verification_evidence_assets",
                column: "presence_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_presence_session_id",
                table: "agent_work_location_evidence",
                column: "presence_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_employee_id",
                table: "attendance_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id_date_status",
                table: "attendance_records",
                columns: new[] { "tenant_id", "date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id_employee_id_date",
                table: "attendance_records",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_work_schedule_id",
                table: "attendance_records",
                column: "work_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_break_records_employee_id",
                table: "break_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_break_records_tenant_id_employee_id",
                table: "break_records",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "break_end IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_break_records_tenant_id_employee_id_break_start",
                table: "break_records",
                columns: new[] { "tenant_id", "employee_id", "break_start" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_created_by_id",
                table: "clock_in_policies",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_legal_entity_id",
                table: "clock_in_policies",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id_legal_entity_id_is_active_effec",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id_scope_type",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "scope_type" });

            migrationBuilder.CreateIndex(
                name: "ix_device_sessions_device_id",
                table: "device_sessions",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_sessions_employee_id",
                table: "device_sessions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_sessions_tenant_id_device_id",
                table: "device_sessions",
                columns: new[] { "tenant_id", "device_id" },
                unique: true,
                filter: "session_end IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_sessions_tenant_id_employee_id_session_start",
                table: "device_sessions",
                columns: new[] { "tenant_id", "employee_id", "session_start" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_sessions_employee_id",
                table: "presence_sessions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_sessions_tenant_id_date_status",
                table: "presence_sessions",
                columns: new[] { "tenant_id", "date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_sessions_tenant_id_employee_id_date",
                table: "presence_sessions",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_created_by_id",
                table: "schedule_assignments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_employee_id",
                table: "schedule_assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_legal_entity_id",
                table: "schedule_assignments",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_position_id",
                table: "schedule_assignments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_tenant_id_department_id_effective_from",
                table: "schedule_assignments",
                columns: new[] { "tenant_id", "department_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_tenant_id_employee_id_effective_from",
                table: "schedule_assignments",
                columns: new[] { "tenant_id", "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_tenant_id_legal_entity_id_assignment_t",
                table: "schedule_assignments",
                columns: new[] { "tenant_id", "legal_entity_id", "assignment_type", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_tenant_id_position_id_effective_from",
                table: "schedule_assignments",
                columns: new[] { "tenant_id", "position_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_assignments_work_schedule_id",
                table: "schedule_assignments",
                column: "work_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_employee_id",
                table: "work_area_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_legal_entity_id",
                table: "work_area_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_reviewed_by_id",
                table: "work_area_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_id_employee_id_date",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_id_status",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_schedule_days_tenant_id_work_schedule_id_day_of_week",
                table: "work_schedule_days",
                columns: new[] { "tenant_id", "work_schedule_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_schedule_days_work_schedule_id",
                table: "work_schedule_days",
                column: "work_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_schedule_holidays_created_by_id",
                table: "work_schedule_holidays",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_schedule_holidays_tenant_id_work_schedule_id_date",
                table: "work_schedule_holidays",
                columns: new[] { "tenant_id", "work_schedule_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_schedule_holidays_work_schedule_id",
                table: "work_schedule_holidays",
                column: "work_schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_legal_entity_id",
                table: "work_schedules",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_tenant_id_legal_entity_id_is_active",
                table: "work_schedules",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_work_schedules_tenant_id_legal_entity_id_name",
                table: "work_schedules",
                columns: new[] { "tenant_id", "legal_entity_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "fk_agent_work_location_evidence_presence_sessions_presence_ses",
                table: "agent_work_location_evidence",
                column: "presence_session_id",
                principalTable: "presence_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_evidence_assets_presence_sessions_presence_ses",
                table: "verification_evidence_assets",
                column: "presence_session_id",
                principalTable: "presence_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
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

            migrationBuilder.DropForeignKey(
                name: "fk_agent_work_location_evidence_presence_sessions_presence_ses",
                table: "agent_work_location_evidence");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_evidence_assets_presence_sessions_presence_ses",
                table: "verification_evidence_assets");

            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "break_records");

            migrationBuilder.DropTable(
                name: "clock_in_policies");

            migrationBuilder.DropTable(
                name: "device_sessions");

            migrationBuilder.DropTable(
                name: "presence_sessions");

            migrationBuilder.DropTable(
                name: "schedule_assignments");

            migrationBuilder.DropTable(
                name: "work_area_change_requests");

            migrationBuilder.DropTable(
                name: "work_schedule_days");

            migrationBuilder.DropTable(
                name: "work_schedule_holidays");

            migrationBuilder.DropTable(
                name: "work_schedules");

            migrationBuilder.DropIndex(
                name: "ix_verification_evidence_assets_presence_session_id",
                table: "verification_evidence_assets");

            migrationBuilder.DropIndex(
                name: "ix_agent_work_location_evidence_presence_session_id",
                table: "agent_work_location_evidence");
        }
    }
}
