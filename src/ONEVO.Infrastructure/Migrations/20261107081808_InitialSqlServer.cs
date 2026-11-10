using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_daily_summary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_active_minutes = table.Column<int>(type: "int", nullable: false),
                    total_idle_minutes = table.Column<int>(type: "int", nullable: false),
                    total_meeting_minutes = table.Column<int>(type: "int", nullable: false),
                    active_percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    productive_app_minutes = table.Column<int>(type: "int", nullable: false),
                    personal_app_minutes = table.Column<int>(type: "int", nullable: false),
                    unknown_app_minutes = table.Column<int>(type: "int", nullable: false),
                    focus_minutes = table.Column<int>(type: "int", nullable: false),
                    activity_score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    data_coverage_percentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    top_apps_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    intensity_avg = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    keyboard_total = table.Column<int>(type: "int", nullable: false),
                    mouse_total = table.Column<int>(type: "int", nullable: false),
                    document_time_minutes = table.Column<int>(type: "int", nullable: false),
                    deep_focus_sessions_count = table.Column<int>(type: "int", nullable: false),
                    data_source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_daily_summary", x => x.id);
                    table.CheckConstraint("ck_activity_daily_summary_top_apps_json_json", "ISJSON([top_apps_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "activity_raw_buffer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_raw_buffer", x => x.id);
                    table.CheckConstraint("ck_activity_raw_buffer_payload_json_json", "ISJSON([payload_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "activity_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    keyboard_events_count = table.Column<int>(type: "int", nullable: false),
                    mouse_events_count = table.Column<int>(type: "int", nullable: false),
                    active_seconds = table.Column<int>(type: "int", nullable: false),
                    idle_seconds = table.Column<int>(type: "int", nullable: false),
                    intensity_score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    foreground_process_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_usage_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    process_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    window_title_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_usage_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_alert_deductions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    alert_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    minutes = table.Column<int>(type: "int", nullable: false),
                    multiplier = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    hours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    charged_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    charge_audit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    refund_audit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_alert_deductions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attendance_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    signal_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    resolved_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    converted_leave_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_alerts", x => x.id);
                    table.CheckConstraint("ck_attendance_alerts_details_json_json", "ISJSON([details_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_working_day = table.Column<bool>(type: "bit", nullable: false),
                    work_time_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    scheduled_start = table.Column<TimeOnly>(type: "time", nullable: true),
                    scheduled_end = table.Column<TimeOnly>(type: "time", nullable: true),
                    required_work_minutes = table.Column<int>(type: "int", nullable: true),
                    expected_work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    expected_work_mode_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    schedule_timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    is_holiday = table.Column<bool>(type: "bit", nullable: false),
                    holiday_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    actual_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    actual_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    worked_minutes = table.Column<int>(type: "int", nullable: false),
                    break_minutes = table.Column<int>(type: "int", nullable: false),
                    late_minutes = table.Column<int>(type: "int", nullable: true),
                    attendance_source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    old_values_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.CheckConstraint("ck_audit_logs_new_values_json_json", "ISJSON([new_values_json]) = 1");
                    table.CheckConstraint("ck_audit_logs_old_values_json_json", "ISJSON([old_values_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "biometric_enrollment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    aws_session_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    region = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    challenge_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<float>(type: "real", nullable: true),
                    failure_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biometric_enrollment_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "biometric_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reference_photo_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    left_reference_photo_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    right_reference_photo_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biometric_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "break_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    break_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    break_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    break_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    auto_detected = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_break_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_meetings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "calendar_event"),
                    external_calendar_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    external_meeting_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    join_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    organizer_join_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    passcode_or_pin = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    last_attendance_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attendance_unavailable_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attendance_sync_failed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attendance_sync_error = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    link_generation = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    provisioning_started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    failure_reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    invites_pending = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_meetings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_participant_rsvp_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    participant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    to_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_participant_rsvp_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_reminders_sent",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_reminders_sent", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendar_meeting_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    confidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    evidence_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    exception_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_meeting_incidents", x => x.id);
                    table.CheckConstraint("ck_calendar_meeting_incidents_evidence_json_json", "ISJSON([evidence_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    color = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "device_state_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    idle_seconds = table.Column<int>(type: "int", nullable: false),
                    is_idle = table.Column<bool>(type: "bit", nullable: false),
                    latitude = table.Column<double>(type: "float", nullable: true),
                    longitude = table.Column<double>(type: "float", nullable: true),
                    accuracy_meters = table.Column<double>(type: "float", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_state_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_hierarchy_closure",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ancestor_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    descendant_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    depth = table.Column<int>(type: "int", nullable: false),
                    source_position_assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_hierarchy_closure", x => new { x.tenant_id, x.ancestor_employee_id, x.descendant_employee_id });
                });

            migrationBuilder.CreateTable(
                name: "employee_monitoring_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "bit", nullable: true),
                    application_tracking = table.Column<bool>(type: "bit", nullable: true),
                    document_tracking = table.Column<bool>(type: "bit", nullable: true),
                    communication_tracking = table.Column<bool>(type: "bit", nullable: true),
                    screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    auto_screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    meeting_detection = table.Column<bool>(type: "bit", nullable: true),
                    device_tracking = table.Column<bool>(type: "bit", nullable: true),
                    work_location_verification = table.Column<bool>(type: "bit", nullable: true),
                    identity_verification = table.Column<bool>(type: "bit", nullable: true),
                    biometric = table.Column<bool>(type: "bit", nullable: true),
                    photo_required = table.Column<bool>(type: "bit", nullable: true),
                    idle_threshold_minutes = table.Column<int>(type: "int", nullable: true),
                    allowed_radius_meters = table.Column<int>(type: "int", nullable: true),
                    override_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    set_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_monitoring_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_work_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clock_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    clock_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    accumulated_break_seconds = table.Column<int>(type: "int", nullable: false),
                    accumulated_work_seconds = table.Column<int>(type: "int", nullable: false),
                    break_session_count = table.Column<int>(type: "int", nullable: false),
                    schedule_display = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_work_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    acknowledged_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    resolved_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    escalated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exceptions", x => x.id);
                    table.CheckConstraint("ck_exceptions_metadata_json_json", "ISJSON([metadata_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "external_calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    external_account_email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    external_calendar_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    external_calendar_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    access_token_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    refresh_token_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    scopes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sync_direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sync_token_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    delta_link_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    failure_count = table.Column<int>(type: "int", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_successful_sync_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_calendar_connections", x => x.id);
                    table.CheckConstraint("ck_external_calendar_connections_scopes_json", "ISJSON([scopes]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "face_verification_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    similarity_score = table.Column<float>(type: "real", nullable: true),
                    photo_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_face_verification_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grantee_type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    grantee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    granted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_access_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "global_app_catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    app_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    process_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    publisher = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    logo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    logo_content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    is_public = table.Column<bool>(type: "bit", nullable: false),
                    is_productive_default = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_app_catalog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendar_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    default_country_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    override_country_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    holiday_sync_enabled = table.Column<bool>(type: "bit", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "nager_holidays"),
                    last_synced_year = table.Column<int>(type: "int", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendar_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    scope = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    requester_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    response_status_code = table.Column<int>(type: "int", nullable: true),
                    response_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_decision_journal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reverted_by_journal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_decision_journal", x => x.id);
                    table.CheckConstraint("ck_inbox_decision_journal_after_json_json", "ISJSON([after_json]) = 1");
                    table.CheckConstraint("ck_inbox_decision_journal_before_json_json", "ISJSON([before_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "invitation_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purpose = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    position_assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    onboarding_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invited_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    invited_full_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_with = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    completion_methods_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    allow_google_email_mismatch = table.Column<bool>(type: "bit", nullable: true),
                    allowed_email_domains_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_tokens", x => x.id);
                    table.CheckConstraint("ck_invitation_tokens_allowed_email_domains_json_json", "ISJSON([allowed_email_domains_json]) = 1");
                    table.CheckConstraint("ck_invitation_tokens_completion_methods_json_json", "ISJSON([completion_methods_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "job_leases",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    owner_instance_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_leases", x => x.job_name);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    color = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false, defaultValue: "#3B82F6"),
                    icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    is_paid = table.Column<bool>(type: "bit", nullable: false),
                    requires_document = table.Column<bool>(type: "bit", nullable: false),
                    document_required_after_hours = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    accepted_document_types = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    applicable_gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legal_acceptance_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    document_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    required = table.Column<bool>(type: "bit", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_acceptance_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legal_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    registration_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    country_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    currency_code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    parent_legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tax_registration_number = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    vat_gst_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    website = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    financial_year_start_month = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    week_start_day = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    standard_working_days = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[1,2,3,4,5]"),
                    default_language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "en-US"),
                    date_format = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "DD MMM YYYY"),
                    time_format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "12h"),
                    work_start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    work_end_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    break_duration_minutes = table.Column<int>(type: "int", nullable: true),
                    office_address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    office_latitude = table.Column<double>(type: "float", nullable: true),
                    office_longitude = table.Column<double>(type: "float", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_entities", x => x.id);
                    table.CheckConstraint("ck_legal_entities_break_duration_minutes", "break_duration_minutes IS NULL OR break_duration_minutes >= 0");
                    table.CheckConstraint("ck_legal_entities_financial_year_start_month", "financial_year_start_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_legal_entities_office_location", "(office_latitude IS NULL AND office_longitude IS NULL) OR (office_latitude IS NOT NULL AND office_longitude IS NOT NULL AND office_latitude BETWEEN -90 AND 90 AND office_longitude BETWEEN -180 AND 180)");
                    table.CheckConstraint("ck_legal_entities_standard_working_days_json", "ISJSON([standard_working_days]) = 1");
                    table.CheckConstraint("ck_legal_entities_time_format", "time_format IN ('12h', '24h')");
                    table.CheckConstraint("ck_legal_entities_week_start_day", "week_start_day BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_legal_entities_work_time_pair", "(work_start_time IS NULL AND work_end_time IS NULL) OR (work_start_time IS NOT NULL AND work_end_time IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_legal_entities_legal_entities_parent_legal_entity_id",
                        column: x => x.parent_legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "login_workspace_selection_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    challenge_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    candidate_workspaces_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "workspace_selection"),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_workspace_selection_challenges", x => x.id);
                    table.CheckConstraint("ck_login_workspace_selection_challenges_candidate_workspaces_json_json", "ISJSON([candidate_workspaces_json]) = 1");
                    table.CheckConstraint("ck_login_workspace_selection_challenges_failed_attempt_count", "failed_attempt_count BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_login_workspace_selection_challenges_purpose", "purpose = 'workspace_selection'");
                });

            migrationBuilder.CreateTable(
                name: "meeting_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_meeting_app_running = table.Column<bool>(type: "bit", nullable: false),
                    process_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "module_catalog",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    pillar = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    phase = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    pricing_unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    pricing_reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    storage_reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ai_token_reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_ai_enabled = table.Column<bool>(type: "bit", nullable: false),
                    is_storage_consuming = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_catalog", x => x.module_key);
                    table.CheckConstraint("ck_module_catalog_ai_token_reference_json", "ISJSON([ai_token_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_pricing_reference_json", "ISJSON([pricing_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_storage_reference_json", "ISJSON([storage_reference]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "monitoring_face_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    check_in_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(850)", maxLength: 850, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    similarity_score = table.Column<float>(type: "real", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_face_scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    delivered_to_tray_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_permission_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    screen_monitoring = table.Column<bool>(type: "bit", nullable: false),
                    app_tracking = table.Column<bool>(type: "bit", nullable: false),
                    location_access = table.Column<bool>(type: "bit", nullable: false),
                    camera_access = table.Column<bool>(type: "bit", nullable: false),
                    notifications = table.Column<bool>(type: "bit", nullable: false),
                    keyboard_mouse = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_permission_consents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_policy_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activity_monitoring = table.Column<bool>(type: "bit", nullable: true),
                    application_tracking = table.Column<bool>(type: "bit", nullable: true),
                    document_tracking = table.Column<bool>(type: "bit", nullable: true),
                    communication_tracking = table.Column<bool>(type: "bit", nullable: true),
                    screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    auto_screenshot_capture = table.Column<bool>(type: "bit", nullable: true),
                    meeting_detection = table.Column<bool>(type: "bit", nullable: true),
                    device_tracking = table.Column<bool>(type: "bit", nullable: true),
                    work_location_verification = table.Column<bool>(type: "bit", nullable: true),
                    identity_verification = table.Column<bool>(type: "bit", nullable: true),
                    biometric = table.Column<bool>(type: "bit", nullable: true),
                    idle_threshold_minutes = table.Column<int>(type: "int", nullable: true),
                    allowed_radius_meters = table.Column<int>(type: "int", nullable: true),
                    override_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    set_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_policy_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<int>(type: "int", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    email_subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    email_html = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    action_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    related_entity_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    notification_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    locale = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    active_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    in_app_title_template = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    in_app_body_template = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    mail_subject_template = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    mail_body_template = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    in_app_enabled = table.Column<bool>(type: "bit", nullable: false),
                    mail_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    related_entity_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_read = table.Column<bool>(type: "bit", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    encrypted_payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    deduplication_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    locked_by = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempt_started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    module = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "personal_calendar_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    start_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    recurrence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    external_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    external_source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    is_all_day = table.Column<bool>(type: "bit", nullable: false),
                    timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    event_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    is_private = table.Column<bool>(type: "bit", nullable: false),
                    organizer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    organizer_email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    meeting_link = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    external_attendees = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    recurrence_rule = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    external_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    recurrence_parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recurrence_original_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    is_recurrence_cancelled = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personal_calendar_events", x => x.id);
                    table.CheckConstraint("ck_personal_calendar_events_external_attendees_json", "ISJSON([external_attendees]) = 1");
                    table.ForeignKey(
                        name: "fk_personal_calendar_events_recurrence_parent_id",
                        column: x => x.recurrence_parent_id,
                        principalTable: "personal_calendar_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "platform_permissions",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    module_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_high_risk = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_permissions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "platform_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    provider_family = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_providers", x => x.id);
                    table.UniqueConstraint("ak_platform_providers_provider_key", x => x.provider_key);
                    table.CheckConstraint("ck_platform_providers_provider_family", "provider_family IN ('oauth_app', 'transactional_email', 'infrastructure', 'object_storage', 'ai_verification', 'payment_gateway')");
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    google_sub = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    mfa_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    mfa_secret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    invite_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_users_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "presence_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    total_present_minutes = table.Column<int>(type: "int", nullable: false),
                    total_break_minutes = table.Column<int>(type: "int", nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presence_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    replaced_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "role_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    module_keys_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    permission_codes_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_templates", x => x.id);
                    table.CheckConstraint("ck_role_templates_module_keys_json_json", "ISJSON([module_keys_json]) = 1");
                    table.CheckConstraint("ck_role_templates_permission_codes_json_json", "ISJSON([permission_codes_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_revoked = table.Column<bool>(type: "bit", nullable: false),
                    csrf_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    active_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    key_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "severities",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_severities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    creator_position_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    goal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    achieved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sprints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    feature_limits_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    included_modules_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    company_size_range = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    pricing_unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    calculated_monthly_price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    calculated_annual_price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    override_monthly_price = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    override_annual_price = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ai_token_limit_per_month = table.Column<int>(type: "int", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    trial_period_days = table.Column<int>(type: "int", nullable: false),
                    unpaid_grace_period_days = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                    table.CheckConstraint("ck_subscription_plans_feature_limits_json_json", "ISJSON([feature_limits_json]) = 1");
                    table.CheckConstraint("ck_subscription_plans_included_modules_json_json", "ISJSON([included_modules_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "task_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    system_key = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "task"),
                    icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_drafts", x => x.id);
                    table.CheckConstraint("ck_task_drafts_payload_json_json", "ISJSON([payload_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "task_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    approver_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    marks_task_complete = table.Column<bool>(type: "bit", nullable: false),
                    visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "public"),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "not_started"),
                    color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false, defaultValue: "#94A3B8"),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_auth_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    password_completion_allowed = table.Column<bool>(type: "bit", nullable: false),
                    google_completion_allowed = table.Column<bool>(type: "bit", nullable: false),
                    google_email_mismatch_default = table.Column<bool>(type: "bit", nullable: false),
                    allowed_login_domains_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    mfa_required = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_auth_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_one_time_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    setup_option_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    charged_once = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_one_time_charges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_provisioning_states",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    current_step = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    tenant_details_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    subscription_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    modules_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    roles_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    settings_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    owner_invite_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    activation_ready = table.Column<bool>(type: "bit", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_updated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_provisioning_states", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_setup_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    setup_option_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_setup_selections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    billing_cycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    included_seats = table.Column<int>(type: "int", nullable: true),
                    overage_allowed = table.Column<bool>(type: "bit", nullable: true),
                    current_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    current_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_provider_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    commercial_model = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    billing_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    subscription_collection_mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    gateway_provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    gateway_customer_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    gateway_subscription_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    license_payment_mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    full_license_amount = table.Column<decimal>(type: "decimal(14,2)", nullable: true),
                    license_paid_at = table.Column<DateOnly>(type: "date", nullable: true),
                    license_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    maintenance_collection_mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    maintenance_billing_cycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    contract_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    maintenance_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_renewal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_rate = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    maintenance_amount = table.Column<decimal>(type: "decimal(14,2)", nullable: true),
                    company_size_range = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    selected_modules = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    calculated_monthly_price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    calculated_annual_price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    override_monthly_price = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    override_annual_price = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ai_token_limit_per_month = table.Column<int>(type: "int", nullable: true),
                    custom_contract_value = table.Column<decimal>(type: "decimal(14,2)", nullable: true),
                    discount_percent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    trial_start_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    trial_end_date = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    unpaid_grace_period_days = table.Column<int>(type: "int", nullable: false),
                    access_ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_subscriptions", x => x.id);
                    table.CheckConstraint("ck_tenant_subscriptions_selected_modules_json", "ISJSON([selected_modules]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    industry_profile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    company_size_range = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subscription_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    settings_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_settings_json_json", "ISJSON([settings_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "tray_app_releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    download_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    publisher = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    minimum_windows_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    min_supported_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    release_notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_app_releases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tray_device_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_revoked = table.Column<bool>(type: "bit", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_dashboards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dashboard_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    layout_version = table.Column<long>(type: "bigint", nullable: false),
                    applied_role_ids = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    applied_no_role = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_dashboards", x => x.id);
                    table.CheckConstraint("ck_user_dashboards_applied_role_ids_json", "ISJSON([applied_role_ids]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "user_external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    provider_subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    provider_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    email_verified = table.Column<bool>(type: "bit", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_external_identities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    method_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    secret = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    is_verified = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false, computedColumnSql: "LOWER(LTRIM(RTRIM([email])))", stored: true),
                    password_hash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    email_verified = table.Column<bool>(type: "bit", nullable: false),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false),
                    password_set_by_admin = table.Column<bool>(type: "bit", nullable: false),
                    temporary_password_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    password_reset_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    password_reset_token_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    suspended_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "version_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_version_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wm_approval_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    position_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approver_source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    applied_payload_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decision_comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    target_updated_at_snapshot = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    undo_state_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reverted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reverted_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_approval_requests", x => x.id);
                    table.CheckConstraint("ck_wm_approval_requests_applied_payload_json_json", "ISJSON([applied_payload_json]) = 1");
                    table.CheckConstraint("ck_wm_approval_requests_payload_json_json", "ISJSON([payload_json]) = 1");
                    table.CheckConstraint("ck_wm_approval_requests_undo_state_json_json", "ISJSON([undo_state_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "wm_module_hour_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    not_started_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    in_progress_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    completed_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    task_count = table.Column<int>(type: "int", nullable: false),
                    unestimated_task_count = table.Column<int>(type: "int", nullable: false),
                    earliest_open_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_module_hour_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wm_monitor_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    first_detected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_monitor_alerts", x => x.id);
                    table.CheckConstraint("ck_wm_monitor_alerts_details_json_json", "ISJSON([details_json]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "wm_notification_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    action_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_notification_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wm_task_rate_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    max_task_rate = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    updated_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wm_task_rate_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_favorites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_favorites", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_favorites_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    body_html = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    system_event_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    tag = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    reply_to_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_mutes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_mutes", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_mutes_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    join_reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    added_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    removed_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    left_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_participants_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policies_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulk_onboarding_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    default_employment_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    default_work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    default_checklist_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    column_mapping_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    selected_draft_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    resolution_state_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    original_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    total_rows = table.Column<int>(type: "int", nullable: false),
                    valid_rows = table.Column<int>(type: "int", nullable: true),
                    invalid_rows = table.Column<int>(type: "int", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_onboarding_batches", x => x.id);
                    table.CheckConstraint("ck_bulk_onboarding_batches_column_mapping_json_json", "ISJSON([column_mapping_json]) = 1");
                    table.CheckConstraint("ck_bulk_onboarding_batches_resolution_state_json_json", "ISJSON([resolution_state_json]) = 1");
                    table.CheckConstraint("ck_bulk_onboarding_batches_selected_draft_ids_json_json", "ISJSON([selected_draft_ids_json]) = 1");
                    table.ForeignKey(
                        name: "fk_bulk_onboarding_batches_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clock_in_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    scope_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    department_ids = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    position_ids = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    employee_ids = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    correction_requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    notification_recipient_resolver = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    deduction_rules_changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_in_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_clock_in_policies_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_feature_toggles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    activity_monitoring = table.Column<bool>(type: "bit", nullable: false),
                    application_tracking = table.Column<bool>(type: "bit", nullable: false),
                    document_tracking = table.Column<bool>(type: "bit", nullable: false),
                    communication_tracking = table.Column<bool>(type: "bit", nullable: false),
                    screenshot_capture = table.Column<bool>(type: "bit", nullable: false),
                    auto_screenshot_capture = table.Column<bool>(type: "bit", nullable: false),
                    meeting_detection = table.Column<bool>(type: "bit", nullable: false),
                    device_tracking = table.Column<bool>(type: "bit", nullable: false),
                    work_location_verification = table.Column<bool>(type: "bit", nullable: false),
                    identity_verification = table.Column<bool>(type: "bit", nullable: false),
                    biometric = table.Column<bool>(type: "bit", nullable: false),
                    idle_threshold_minutes = table.Column<int>(type: "int", nullable: true),
                    allowed_radius_meters = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_feature_toggles", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitoring_feature_toggles_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    system_purpose = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    owning_legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    identifier = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    next_task_number = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    next_meeting_number = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    actual_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    allocated_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    completed_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_achieved = table.Column<bool>(type: "bit", nullable: false),
                    achieved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_legal_entities_owning_legal_entity_id",
                        column: x => x.owning_legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_work_modes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    biometric_enabled = table.Column<bool>(type: "bit", nullable: false),
                    web_enabled = table.Column<bool>(type: "bit", nullable: false),
                    tray_enabled = table.Column<bool>(type: "bit", nullable: false),
                    photo_required = table.Column<bool>(type: "bit", nullable: false),
                    self_registers_location = table.Column<bool>(type: "bit", nullable: false),
                    allows_daily_location_choice = table.Column<bool>(type: "bit", nullable: false),
                    is_system_seeded = table.Column<bool>(type: "bit", nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_work_modes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_work_modes_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tray_activation_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    code_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_activation_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tray_activation_codes_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tray_device_authorizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_code_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    user_code_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    device_fingerprint_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    device_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    device_os = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    client_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approved_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    poll_violation_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_authorizations", x => x.id);
                    table.CheckConstraint("ck_tray_device_authorizations_approval_identity", "status NOT IN ('Approved', 'Consumed') OR (approved_tenant_id IS NOT NULL AND approved_user_id IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_tray_device_authorizations_consumed_at", "consumed_at IS NULL OR status = 'Consumed'");
                    table.ForeignKey(
                        name: "fk_tray_device_authorizations_legal_entities_approved_legal_entity_id",
                        column: x => x.approved_legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tray_device_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    device_os = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    device_fingerprint = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_tray_device_registrations_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "module_catalog_price_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    module_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    old_pricing_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_pricing_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    old_storage_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_storage_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    old_ai_token_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_ai_token_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    old_pricing_unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    new_pricing_unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    changed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_catalog_price_history", x => x.id);
                    table.CheckConstraint("ck_module_catalog_price_history_new_ai_token_reference_json", "ISJSON([new_ai_token_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_price_history_new_pricing_reference_json", "ISJSON([new_pricing_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_price_history_new_storage_reference_json", "ISJSON([new_storage_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_price_history_old_ai_token_reference_json", "ISJSON([old_ai_token_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_price_history_old_pricing_reference_json", "ISJSON([old_pricing_reference]) = 1");
                    table.CheckConstraint("ck_module_catalog_price_history_old_storage_reference_json", "ISJSON([old_storage_reference]) = 1");
                    table.ForeignKey(
                        name: "fk_module_catalog_price_history_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_features",
                columns: table => new
                {
                    feature_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    module_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_default_included = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_features", x => x.feature_key);
                    table.ForeignKey(
                        name: "fk_module_features_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_permission_ownership",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    permission_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    is_default_permission = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_permission_ownership", x => new { x.module_key, x.permission_code });
                    table.ForeignKey(
                        name: "fk_module_permission_ownership_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    latitude = table.Column<double>(type: "float", nullable: true),
                    longitude = table.Column<double>(type: "float", nullable: true),
                    location_accuracy = table.Column<double>(type: "float", nullable: true),
                    location_address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    device_serial_number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    face_scan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_check_ins_monitoring_face_scans_face_scan_id",
                        column: x => x.face_scan_id,
                        principalTable: "monitoring_face_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false),
                    in_app_title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    in_app_body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    email_subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    email_html = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email_css = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tray_title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    tray_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    action_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_template_versions_notification_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "notification_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_notification_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    enabled_channels = table.Column<int>(type: "int", nullable: false),
                    recipient_rule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_notification_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_notification_rules_notification_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "notification_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    grant_type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    granted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permission_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gateway_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    public_key = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    merchant_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    webhook_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_configs", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_gateway_configs_platform_providers_provider",
                        column: x => x.provider,
                        principalTable: "platform_providers",
                        principalColumn: "provider_key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuration_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    template_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    module_keys_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    industry_profile_tag = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuration_templates", x => x.id);
                    table.CheckConstraint("ck_configuration_templates_module_keys_json_json", "ISJSON([module_keys_json]) = 1");
                    table.CheckConstraint("ck_configuration_templates_payload_json_json", "ISJSON([payload_json]) = 1");
                    table.ForeignKey(
                        name: "fk_configuration_templates_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legal_document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_required = table.Column<bool>(type: "bit", nullable: false),
                    block_scope = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    published_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    publish_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    content_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_document_versions", x => x.id);
                    table.CheckConstraint("ck_legal_document_versions_content_json_json", "ISJSON([content_json]) = 1");
                    table.ForeignKey(
                        name: "fk_legal_document_versions_platform_users_published_by_id",
                        column: x => x.published_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_auth_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    event_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    source_ip = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_auth_events", x => x.id);
                    table.CheckConstraint("ck_platform_auth_events_metadata_json_json", "ISJSON([metadata_json]) = 1");
                    table.ForeignKey(
                        name: "fk_platform_auth_events_platform_users_user_id",
                        column: x => x.user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_oauth_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    app_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    client_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    authorization_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    token_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    default_scopes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_oauth_apps", x => x.id);
                    table.UniqueConstraint("ak_platform_o_auth_apps_provider", x => x.provider);
                    table.ForeignKey(
                        name: "fk_platform_oauth_apps_platform_providers_provider",
                        column: x => x.provider,
                        principalTable: "platform_providers",
                        principalColumn: "provider_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_oauth_apps_platform_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_roles_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_service_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_service_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_service_keys_platform_providers_service_key",
                        column: x => x.service_key,
                        principalTable: "platform_providers",
                        principalColumn: "provider_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_service_keys_platform_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credential_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    password_algorithm = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    failed_login_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reset_token_hash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    reset_token_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_credentials", x => x.id);
                    table.CheckConstraint("ck_platform_user_credentials_credential_type", "credential_type IN ('password')");
                    table.CheckConstraint("ck_platform_user_credentials_password_hash", "credential_type <> 'password' OR password_hash IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_platform_user_credentials_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    invite_token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    invited_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_invites_platform_users_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_user_invites_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    csrf_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_sessions_platform_users_account_id",
                        column: x => x.account_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    source_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_role_templates_source_template_id",
                        column: x => x.source_template_id,
                        principalTable: "role_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sprint_activity_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sprint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sprint_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_sprint_activity_logs_sprints_sprint_id",
                        column: x => x.sprint_id,
                        principalTable: "sprints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "standard"),
                    visibility_scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "module"),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    creator_position_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sprint_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    short_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    story_points = table.Column<int>(type: "int", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    completed_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    progress_percent = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_sprints_sprint_id",
                        column: x => x.sprint_id,
                        principalTable: "sprints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_task_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "task_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_task_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "task_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_tasks_parent_task_id",
                        column: x => x.parent_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invoice_number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    external_invoice_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    gateway_provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_tenant_subscriptions_tenant_subscription_id",
                        column: x => x.tenant_subscription_id,
                        principalTable: "tenant_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_status_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    to_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    changed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_status_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_status_histories_platform_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tenant_status_histories_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_storage_stats",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    used_r2_bytes = table.Column<long>(type: "bigint", nullable: false),
                    used_db_bytes = table.Column<long>(type: "bigint", nullable: false),
                    reserved_r2_bytes = table.Column<long>(type: "bigint", nullable: false),
                    last_calculated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_storage_stats", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_tenant_storage_stats_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    widget_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    settings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_widgets", x => x.id);
                    table.CheckConstraint("ck_dashboard_widgets_settings_json", "ISJSON([settings]) = 1");
                    table.ForeignKey(
                        name: "fk_dashboard_widgets_user_dashboards_dashboard_id",
                        column: x => x.dashboard_id,
                        principalTable: "user_dashboards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                    original_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    safe_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    detected_content_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    checksum_sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    scan_provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    scan_result_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    scan_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    storage_deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_records_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_records_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legal_login_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    challenge_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    csrf_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    superseded_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_login_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_login_challenges_legal_login_challenges_superseded_by_id",
                        column: x => x.superseded_by_id,
                        principalTable: "legal_login_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_legal_login_challenges_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_legal_login_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mfa_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    challenge_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "password"),
                    failed_attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_challenges_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mfa_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_session_exchange_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    auth_origin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_session_exchange_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_session_exchange_challenges_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_session_exchange_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_attachments_conversation_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "conversation_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_hides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_hides", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_hides_conversation_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "conversation_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_mentions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_mentions", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_mentions_conversation_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "conversation_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    emoji = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_reactions_conversation_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "conversation_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    body_html = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    revised_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revised_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_revisions_conversation_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "conversation_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ended_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_policy_assignments_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_hours = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    amount_period = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    carry_forward_cap_hours = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "int", nullable: true),
                    eligible_from = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    eligible_after_months = table.Column<int>(type: "int", nullable: true),
                    minimum_notice_days = table.Column<int>(type: "int", nullable: false),
                    min_request_hours = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    max_request_hours = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    max_team_absence_percent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    approval_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clock_in_late_deduction_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clock_in_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    late_arrival_minute = table.Column<int>(type: "int", nullable: false),
                    multiplier = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    time_off_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_in_late_deduction_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_clock_in_late_deduction_rules_clock_in_policies_clock_in_policy_id",
                        column: x => x.clock_in_policy_id,
                        principalTable: "clock_in_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    archived_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_events_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labels", x => x.id);
                    table.ForeignKey(
                        name: "fk_labels_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reporting_manager_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    creator_position_objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    progress = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 0m),
                    actual_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    allocated_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    completed_hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    is_achieved = table.Column<bool>(type: "bit", nullable: false),
                    achieved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_objectives_objectives_parent_objective_id",
                        column: x => x.parent_objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_objectives_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status_id = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_versions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_versions_version_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "version_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    nationality_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    employment_type_id = table.Column<int>(type: "int", nullable: false),
                    employment_status_id = table.Column<int>(type: "int", nullable: false),
                    work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    probation_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    display_timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_time_attendance_work_modes_work_mode_id",
                        column: x => x.work_mode_id,
                        principalTable: "tenant_work_modes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    command_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_commands", x => x.id);
                    table.CheckConstraint("ck_agent_commands_payload_json_json", "ISJSON([payload_json]) = 1");
                    table.CheckConstraint("ck_agent_commands_result_json_json", "ISJSON([result_json]) = 1");
                    table.ForeignKey(
                        name: "fk_agent_commands_tray_device_registrations_agent_device_id",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    current_device_registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    new_device_fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    new_device_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    new_device_os = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    review_comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_change_requests_tray_device_registrations_current_device_registration_id",
                        column: x => x.current_device_registration_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_device_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_country_routes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    country_code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    country_name_snapshot = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    gateway_config_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_country_routes", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_gateway_country_routes_payment_gateway_configs_gateway_config_id",
                        column: x => x.gateway_config_id,
                        principalTable: "payment_gateway_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_gateway_config_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    secret_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    webhook_secret_encrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    encryption_key_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    credential_version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    rotated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deactivated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_gateway_credentials_payment_gateway_configs_payment_gateway_config_id",
                        column: x => x.payment_gateway_config_id,
                        principalTable: "payment_gateway_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_configuration_template_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    configuration_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    applied_version = table.Column<int>(type: "int", nullable: false),
                    applied_payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    custom_payload_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    warnings_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    applied_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_configuration_template_applications", x => x.id);
                    table.CheckConstraint("ck_tenant_config_template_apps_status", "status IN ('applied')");
                    table.CheckConstraint("ck_tenant_configuration_template_applications_applied_payload_json_json", "ISJSON([applied_payload_json]) = 1");
                    table.CheckConstraint("ck_tenant_configuration_template_applications_custom_payload_json_json", "ISJSON([custom_payload_json]) = 1");
                    table.CheckConstraint("ck_tenant_configuration_template_applications_warnings_json_json", "ISJSON([warnings_json]) = 1");
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_configuration_templates_configuration_template_id",
                        column: x => x.configuration_template_id,
                        principalTable: "configuration_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_platform_users_applied_by_id",
                        column: x => x.applied_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_catalog",
                columns: table => new
                {
                    integration_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    connection_scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    crownus_app_provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_catalog", x => x.integration_key);
                    table.ForeignKey(
                        name: "fk_integration_catalog_platform_o_auth_apps_crownus_app_provider",
                        column: x => x.crownus_app_provider,
                        principalTable: "platform_oauth_apps",
                        principalColumn: "provider",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_catalog_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_oauth_app_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform_oauth_app_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_secret_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    private_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    encryption_key_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    credential_version = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    rotated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deactivated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_oauth_app_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform_oauth_app_id",
                        column: x => x.platform_oauth_app_id,
                        principalTable: "platform_oauth_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_users_deactivated_by_id",
                        column: x => x.deactivated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_users_rotated_by_id",
                        column: x => x.rotated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_permissions_permission_code",
                        column: x => x.permission_code,
                        principalTable: "platform_permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_users_assigned_by_id",
                        column: x => x.assigned_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_users_user_id",
                        column: x => x.user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dashboard_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_no_role_template = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_templates", x => x.id);
                    table.CheckConstraint("ck_dashboard_templates_no_role_flag", "(is_no_role_template = 1 AND role_id IS NULL) OR (is_no_role_template = 0 AND role_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_dashboard_templates_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    source_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_position_access_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_assignments_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_clocking_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clock_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    clock_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    duration_minutes = table.Column<int>(type: "int", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_clocking_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_clocking_sessions_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_comment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_edited = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comments_task_comments_parent_comment_id",
                        column: x => x.parent_comment_id,
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_comments_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_edit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    edit_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    old_values_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    new_values_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_edit_logs", x => x.id);
                    table.CheckConstraint("ck_task_edit_logs_new_values_json_json", "ISJSON([new_values_json]) = 1");
                    table.CheckConstraint("ck_task_edit_logs_old_values_json_json", "ISJSON([old_values_json]) = 1");
                    table.ForeignKey(
                        name: "fk_task_edit_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_status_change_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    to_status_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_status_change_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_task_statuses_from_status_id",
                        column: x => x.from_status_id,
                        principalTable: "task_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_task_statuses_to_status_id",
                        column: x => x.to_status_id,
                        principalTable: "task_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_admin_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_audit_logs", x => x.id);
                    table.CheckConstraint("ck_billing_audit_logs_metadata_json_json", "ISJSON([metadata_json]) = 1");
                    table.ForeignKey(
                        name: "fk_billing_audit_logs_platform_users_actor_admin_user_id",
                        column: x => x.actor_admin_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_billing_audit_logs_subscription_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "subscription_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_billing_audit_logs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_widget_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    widget_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    breakpoint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    x = table.Column<int>(type: "int", nullable: false),
                    y = table.Column<int>(type: "int", nullable: false),
                    w = table.Column<int>(type: "int", nullable: false),
                    h = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_widget_layouts", x => x.id);
                    table.CheckConstraint("ck_dashboard_widget_layouts_breakpoint", "breakpoint IN ('desktop', 'tablet', 'mobile')");
                    table.CheckConstraint("ck_dashboard_widget_layouts_h", "h > 0");
                    table.CheckConstraint("ck_dashboard_widget_layouts_w", "w > 0");
                    table.CheckConstraint("ck_dashboard_widget_layouts_x", "x >= 0");
                    table.CheckConstraint("ck_dashboard_widget_layouts_y", "y >= 0");
                    table.ForeignKey(
                        name: "fk_dashboard_widget_layouts_dashboard_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "dashboard_widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    asset_purpose = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    file_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_by_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_assets", x => x.id);
                    table.CheckConstraint("ck_entity_assets_metadata_json_json", "ISJSON([metadata_json]) = 1");
                    table.ForeignKey(
                        name: "fk_entity_assets_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_upload_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reserved_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reserved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_file_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_upload_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_file_records_completed_file_record_id",
                        column: x => x.completed_file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_users_reserved_by_user_id",
                        column: x => x.reserved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_blackout_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_policy_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_blackout_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_blackout_periods_leave_policy_versions_leave_policy_version_id",
                        column: x => x.leave_policy_version_id,
                        principalTable: "leave_policy_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_activity_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    performed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_activity_logs", x => x.id);
                    table.CheckConstraint("ck_calendar_event_activity_logs_details_json_json", "ISJSON([details_json]) = 1");
                    table.ForeignKey(
                        name: "fk_calendar_event_activity_logs_calendar_events_calendar_event_id",
                        column: x => x.calendar_event_id,
                        principalTable: "calendar_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                        column: x => x.calendar_event_id,
                        principalTable: "calendar_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_event_tasks_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_objectives_calendar_events_calendar_event_id",
                        column: x => x.calendar_event_id,
                        principalTable: "calendar_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_event_objectives_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_member_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invited_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invite_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "member"),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    invited_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_member_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_member_invitations_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_member_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    membership_source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_members_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reminder_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_calendar", x => x.id);
                    table.ForeignKey(
                        name: "fk_release_calendar_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_release_calendar_versions_version_id",
                        column: x => x.version_id,
                        principalTable: "versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    presence_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attendance_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    correction_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    original_clock_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    original_clock_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    requested_clock_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    requested_clock_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    original_break_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    requested_break_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    approved_clock_in_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    approved_clock_out_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    approved_break_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approval_required = table.Column<bool>(type: "bit", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    review_comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_corrections", x => x.id);
                    table.CheckConstraint("ck_attendance_corrections_original_break_json_json", "ISJSON([original_break_json]) = 1");
                    table.CheckConstraint("ck_attendance_corrections_requested_break_json_json", "ISJSON([requested_break_json]) = 1");
                    table.ForeignKey(
                        name: "fk_attendance_corrections_attendance_records_attendance_record_id",
                        column: x => x.attendance_record_id,
                        principalTable: "attendance_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_corrections_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_corrections_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_corrections_presence_sessions_presence_session_id",
                        column: x => x.presence_session_id,
                        principalTable: "presence_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_corrections_users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attendance_corrections_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_work_location_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    latitude = table.Column<double>(type: "float", nullable: true),
                    longitude = table.Column<double>(type: "float", nullable: true),
                    accuracy_meters = table.Column<double>(type: "float", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_work_location_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_daily_work_location_confirmations_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    address_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    address_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_addresses", x => x.id);
                    table.CheckConstraint("ck_employee_addresses_address_json_json", "ISJSON([address_json]) = 1");
                    table.ForeignKey(
                        name: "fk_employee_addresses_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_bank_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bank_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    branch_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    account_holder_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    account_number_encrypted = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    account_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    routing_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_bank_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_bank_details_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_dependents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    relationship = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    is_emergency_contact = table.Column<bool>(type: "bit", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_dependents", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_dependents_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_emergency_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    relationship = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_emergency_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_emergency_contacts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_work_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    latitude = table.Column<double>(type: "float", nullable: false),
                    longitude = table.Column<double>(type: "float", nullable: false),
                    accuracy_meters = table.Column<double>(type: "float", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_work_locations", x => x.id);
                    table.CheckConstraint("ck_employee_work_locations_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_employee_work_locations_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_employee_work_locations_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_approval_delegates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_approval_delegates", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_approval_delegates_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_approval_delegates_employees_delegate_employee_id",
                        column: x => x.delegate_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    total_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    used_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    pending_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    carried_forward_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    manual_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    leave_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    leave_policy_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_entitlements_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_entitlements_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    total_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    paid_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    unpaid_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    conflict_snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    notice_period_missed = table.Column<bool>(type: "bit", nullable: false),
                    submitted_on_behalf_of_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    partial_cancel_effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    leave_policy_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    exceeds_balance_by_hours = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                    table.CheckConstraint("ck_leave_requests_conflict_snapshot_json_json", "ISJSON([conflict_snapshot_json]) = 1");
                    table.ForeignKey(
                        name: "fk_leave_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_requests_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_latitude = table.Column<double>(type: "float", nullable: false),
                    requested_longitude = table.Column<double>(type: "float", nullable: false),
                    requested_accuracy_meters = table.Column<double>(type: "float", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    review_comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_change_requests", x => x.id);
                    table.CheckConstraint("ck_location_change_requests_latitude", "requested_latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_location_change_requests_longitude", "requested_longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_location_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_change_requests_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    objective_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organizer_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_private = table.Column<bool>(type: "bit", nullable: false),
                    dt_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    is_all_day = table.Column<bool>(type: "bit", nullable: false),
                    timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    recurrence_rule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    creation_path = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    split_from_series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    split_reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    split_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revision = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
                name: "planned_task_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_task_blocks", x => x.id);
                    table.CheckConstraint("ck_planned_task_blocks_end_after_start", "end_at > start_at");
                    table.ForeignKey(
                        name: "fk_planned_task_blocks_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_planned_task_blocks_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reporting_manager_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previous_manager_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    new_manager_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    change_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_manager_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_reporting_manager_changes_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reporting_manager_changes_employees_new_manager_employee_id",
                        column: x => x.new_manager_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reporting_manager_changes_employees_previous_manager_employee_id",
                        column: x => x.previous_manager_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_area_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    current_work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    current_work_mode_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    requested_work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_work_mode_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    legacy_work_area_label = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    review_comment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_area_change_requests", x => x.id);
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
                        name: "fk_work_area_change_requests_time_attendance_work_modes_current_work_mode_id",
                        column: x => x.current_work_mode_id,
                        principalTable: "tenant_work_modes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_area_change_requests_time_attendance_work_modes_requested_work_mode_id",
                        column: x => x.requested_work_mode_id,
                        principalTable: "tenant_work_modes",
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
                name: "monitoring_evidence_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    activity_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    agent_command_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    file_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    evidence_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    trigger_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    retention_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    legal_hold_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_evidence_assets", x => x.id);
                    table.CheckConstraint("ck_monitoring_evidence_assets_metadata_json_json", "ISJSON([metadata_json]) = 1");
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id",
                        column: x => x.activity_snapshot_id,
                        principalTable: "activity_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_agent_commands_agent_command_id",
                        column: x => x.agent_command_id,
                        principalTable: "agent_commands",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_monitoring_evidence_assets_tray_device_registrations_agent_device_id",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "module_integration_links",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    integration_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    linked_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_integration_links", x => new { x.module_key, x.integration_key });
                    table.ForeignKey(
                        name: "fk_module_integration_links_integration_catalog_integration_key",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_integration_links_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_integration_links_platform_users_linked_by_id",
                        column: x => x.linked_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_integration_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    integration_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    access_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    scopes_granted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    external_account_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    external_account_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_integration_credentials", x => x.id);
                    table.CheckConstraint("ck_tenant_integration_credentials_status", "status IN ('connected', 'error', 'expired', 'disconnected', 'disabled')");
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_integration_catalog_integration_key",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_users_connected_by_user_id",
                        column: x => x.connected_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    integration_key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    provider_user_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    provider_username = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    provider_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    access_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    scopes_granted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_sync_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_integration_connections", x => x.id);
                    table.CheckConstraint("ck_user_integration_connections_status", "status IN ('connected', 'error', 'expired', 'disconnected')");
                    table.ForeignKey(
                        name: "fk_user_integration_connections_integration_catalog_integration_key",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_integration_connections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_integration_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_template_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    widget_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    settings = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_template_widgets", x => x.id);
                    table.CheckConstraint("ck_dashboard_template_widgets_settings_json", "ISJSON([settings]) = 1");
                    table.ForeignKey(
                        name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "dashboard_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_percentage_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previous_percent = table.Column<int>(type: "int", nullable: false),
                    new_percent = table.Column<int>(type: "int", nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    clocking_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_percentage_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_percentage_logs_task_clocking_sessions_clocking_session_id",
                        column: x => x.clocking_session_id,
                        principalTable: "task_clocking_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_percentage_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comment_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    comment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comment_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comment_logs_task_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_comment_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_comment_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    comment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    emoji = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comment_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comment_reactions_task_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_balance_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    change_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    hours_changed = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    related_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    leave_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    leave_policy_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    year = table.Column<int>(type: "int", nullable: true),
                    covers_from_month = table.Column<int>(type: "int", nullable: true),
                    covers_to_month = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_balance_audits", x => x.id);
                    table.CheckConstraint("ck_leave_balance_audits_covers_months", "(covers_from_month IS NULL AND covers_to_month IS NULL) OR (covers_from_month BETWEEN 1 AND 12 AND covers_to_month BETWEEN covers_from_month AND 12)");
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_leave_requests_related_request_id",
                        column: x => x.related_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_request_approvers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence_order = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    delegated_from_approver_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_approvers", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_approvers_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_request_day_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours_unit = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    paid_hours_unit = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    unpaid_hours_unit = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_day_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_day_allocations_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_request_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_documents_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_request_documents_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    meeting_series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_guests", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_guests_meeting_series_meeting_series_id",
                        column: x => x.meeting_series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    meeting_series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    response_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "pending"),
                    response_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_participants_meeting_series_meeting_series_id",
                        column: x => x.meeting_series_id,
                        principalTable: "meeting_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_details",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    original_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    is_all_day = table.Column<bool>(type: "bit", nullable: false),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_private = table.Column<bool>(type: "bit", nullable: false),
                    is_detached = table.Column<bool>(type: "bit", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revision = table.Column<int>(type: "int", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attendance_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    attendance_next_check_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attendance_checkpoint = table.Column<int>(type: "int", nullable: false),
                    attendance_finalized_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attendance_last_error = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    hours_stamped_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_details", x => x.task_id);
                    table.UniqueConstraint("uq_meeting_details_series_original_start", x => new { x.series_id, x.original_start });
                    table.ForeignKey(
                        name: "fk_meeting_details_calendar_event_meetings_calendar_event_meeting_id",
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
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    original_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    moved_to_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    series_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_series_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_series_links_calendar_event_meetings_calendar_event_meeting_id",
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
                name: "inactivity_capture_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    work_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idle_started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    prompted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decision_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    idle_duration_seconds = table.Column<int>(type: "int", nullable: false),
                    monitor_count = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    failure_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    evidence_asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    policy_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactivity_capture_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_inactivity_capture_attempts_monitoring_evidence_assets_evidence_asset_id",
                        column: x => x.evidence_asset_id,
                        principalTable: "monitoring_evidence_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inactivity_capture_attempts_tray_device_registrations_agent_device_id",
                        column: x => x.agent_device_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_template_widget_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_widget_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    breakpoint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    x = table.Column<int>(type: "int", nullable: false),
                    y = table.Column<int>(type: "int", nullable: false),
                    w = table.Column<int>(type: "int", nullable: false),
                    h = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_template_widget_layouts", x => x.id);
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_breakpoint", "breakpoint IN ('desktop', 'tablet', 'mobile')");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_h", "h > 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_w", "w > 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_x", "x >= 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_y", "y >= 0");
                    table.ForeignKey(
                        name: "fk_dashboard_template_widget_layouts_dashboard_template_widgets_template_widget_id",
                        column: x => x.template_widget_id,
                        principalTable: "dashboard_template_widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_calendar_event_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    meeting_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    external_calendar_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    external_calendar_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    external_event_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    external_etag = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    sync_direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sync_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    pushed_fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_calendar_event_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_calendar_event_links_meeting_details_meeting_task_id",
                        column: x => x.meeting_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_action_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_session_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    session_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    session_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    occurrence_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_attendance_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_attendance_sessions_calendar_event_meetings_calendar_event_meeting_id",
                        column: x => x.calendar_event_meeting_id,
                        principalTable: "calendar_event_meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_attendance_sessions_meeting_details_occurrence_task_id",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting_occurrence_attendees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurrence_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attendee_kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    change = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    response_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    response_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                        name: "fk_meeting_occurrence_attendees_meeting_details_occurrence_task_id",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event_meeting_attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    calendar_event_meeting_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    external_participant_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    external_participant_email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    duration_seconds = table.Column<int>(type: "int", nullable: true),
                    occurrence_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attendance_session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    participant_key = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_meeting_attendances", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_meeting_attendances_meeting_attendance_sessions_attendance_session_id",
                        column: x => x.attendance_session_id,
                        principalTable: "meeting_attendance_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calendar_event_meeting_attendances_meeting_details_occurrence_task_id",
                        column: x => x.occurrence_task_id,
                        principalTable: "meeting_details",
                        principalColumn: "task_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_grant_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    onboarding_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    target_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position_access_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approval_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reserved_position_assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    change_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    decision_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_grant_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_access_grant_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_requests_roles_requested_role_id",
                        column: x => x.requested_role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_requests_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulk_onboarding_batch_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    batch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_number = table.Column<int>(type: "int", nullable: false),
                    raw_data_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    resolved_department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_reports_to_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    onboarding_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_onboarding_batch_rows", x => x.id);
                    table.CheckConstraint("ck_bulk_onboarding_batch_rows_raw_data_json_json", "ISJSON([raw_data_json]) = 1");
                    table.ForeignKey(
                        name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "bulk_onboarding_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    template_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tasks_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                    table.CheckConstraint("ck_checklist_templates_tasks_json_json", "ISJSON([tasks_json]) = 1");
                    table.ForeignKey(
                        name: "fk_checklist_templates_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offboarding_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    last_working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    knowledge_risk_level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    rehire_eligibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    checklist_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    exit_interview_notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    penalties_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}"),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    initiated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previous_employment_status_id = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_records", x => x.id);
                    table.CheckConstraint("ck_offboarding_records_penalties_json_json", "ISJSON([penalties_json]) = 1");
                    table.ForeignKey(
                        name: "fk_offboarding_records_checklist_templates_checklist_template_id",
                        column: x => x.checklist_template_id,
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offboarding_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_checklist_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lifecycle_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    task_title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    owner_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: true),
                    assigned_to_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_required = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    is_bypassable = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    bypass_penalty_description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    attachment_file_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    offboarding_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    work_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_checklist_tasks", x => x.id);
                    table.CheckConstraint("ck_employee_checklist_tasks_attachment_file_ids_json_json", "ISJSON([attachment_file_ids_json]) = 1");
                    table.ForeignKey(
                        name: "fk_employee_checklist_tasks_checklist_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_checklist_tasks_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_checklist_tasks_offboarding_records_offboarding_record_id",
                        column: x => x.offboarding_record_id,
                        principalTable: "offboarding_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_checklist_tasks_users_assigned_to_id",
                        column: x => x.assigned_to_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_checklist_tasks_work_tasks_work_task_id",
                        column: x => x.work_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offboarding_task_bypass_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_checklist_task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    offboarding_record_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approver_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bypass_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    penalty_description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    prior_task_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decision_comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_task_bypass_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_offboarding_task_bypass_requests_employee_checklist_tasks_employee_checklist_task_id",
                        column: x => x.employee_checklist_task_id,
                        principalTable: "employee_checklist_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offboarding_task_bypass_requests_offboarding_records_offboarding_record_id",
                        column: x => x.offboarding_record_id,
                        principalTable: "offboarding_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    head_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parent_department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_departments_parent_department_id",
                        column: x => x.parent_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departments_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    position_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "unique"),
                    max_occupancy = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    reports_to_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    default_role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_positions_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_positions_reports_to_position_id",
                        column: x => x.reports_to_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "management_coverage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    covered_target_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    covered_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    covered_department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_order = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    is_locked = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    responsible_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_management_coverage_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_departments_covered_department_id",
                        column: x => x.covered_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_positions_covered_position_id",
                        column: x => x.covered_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_positions_owner_position_id",
                        column: x => x.owner_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    work_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reports_to_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    employment_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    employee_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    work_mode_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    photo_required_override = table.Column<bool>(type: "bit", nullable: true),
                    location_capture_override = table.Column<bool>(type: "bit", nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    selected_template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    edited_tasks_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    draft_reason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    last_saved_step = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    started_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    xmin = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_drafts", x => x.id);
                    table.CheckConstraint("ck_onboarding_drafts_edited_tasks_json_json", "ISJSON([edited_tasks_json]) = 1");
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_time_attendance_work_modes_work_mode_id",
                        column: x => x.work_mode_id,
                        principalTable: "tenant_work_modes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_access_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_access_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_access_templates_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_access_templates_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assignment_kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    assignment_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    change_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reports_to_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_assignments_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_reporting_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reports_to_position_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    change_reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_reporting_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_reporting_history_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_reporting_history_positions_reports_to_position_id",
                        column: x => x.reports_to_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seat_increase_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    onboarding_draft_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_seats = table.Column<int>(type: "int", nullable: false),
                    seats_at_request = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decision_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seat_increase_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_onboarding_drafts_onboarding_draft_id",
                        column: x => x.onboarding_draft_id,
                        principalTable: "onboarding_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "platform_providers",
                columns: new[] { "id", "created_at", "display_name", "is_active", "provider_family", "provider_key", "updated_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Google", true, "oauth_app", "google", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "GitHub", true, "oauth_app", "github", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Microsoft", true, "oauth_app", "microsoft", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Zoom", true, "oauth_app", "zoom", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SendGrid", true, "transactional_email", "sendgrid", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Resend", true, "transactional_email", "resend", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Cloudflare", true, "infrastructure", "cloudflare", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Cloudflare R2", true, "object_storage", "cloudflare_r2", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "AWS Rekognition", true, "ai_verification", "aws_rekognition", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Stripe", true, "payment_gateway", "stripe", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PayHere", true, "payment_gateway", "payhere", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Paddle", true, "payment_gateway", "paddle", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "id", "ai_token_limit_per_month", "calculated_annual_price", "calculated_monthly_price", "code", "company_size_range", "created_at", "currency", "feature_limits_json", "included_modules_json", "is_active", "name", "override_annual_price", "override_monthly_price", "pricing_unit", "tier", "trial_period_days", "unpaid_grace_period_days", "updated_at" },
                values: new object[] { new Guid("a1b2c3d4-0001-0001-0001-000000000001"), null, 75.00m, 7.50m, "starter_51_200", "51-200", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", null, "[\"org_structure\",\"core_hr\",\"leave\",\"calendar\",\"time_attendance\",\"monitoring\",\"discrepancy_engine\",\"identity_verification\",\"exception_engine\",\"productivity_analytics\",\"desktop_agent_gateway\",\"worksync_foundation\",\"projects\",\"objectives_milestones\",\"tasks\",\"boards\",\"planning_sprints\"]", true, "Starter - 51-200", null, null, "per_employee", "starter", 30, 7, null });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_decided_by_user_id",
                table: "access_grant_requests",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_employee_id",
                table: "access_grant_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_onboarding_draft_id",
                table: "access_grant_requests",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_position_access_template_id",
                table: "access_grant_requests",
                column: "position_access_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_requested_by_user_id",
                table: "access_grant_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_requested_role_id",
                table: "access_grant_requests",
                column: "requested_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_target_department_id",
                table: "access_grant_requests",
                column: "target_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_target_position_id",
                table: "access_grant_requests",
                column: "target_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_tenant_id_onboarding_draft_id_target_position_id_position_access_template_id",
                table: "access_grant_requests",
                columns: new[] { "tenant_id", "onboarding_draft_id", "target_position_id", "position_access_template_id" },
                unique: true,
                filter: "approval_status = 'Pending' AND onboarding_draft_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_requests_user_id",
                table: "access_grant_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_daily_summary_tenant_employee_date_desc",
                table: "activity_daily_summary",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_device_received",
                table: "activity_raw_buffer",
                columns: new[] { "agent_device_id", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_tenant_received",
                table: "activity_raw_buffer",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_employee_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_device_status_expires",
                table: "agent_commands",
                columns: new[] { "agent_device_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_tenant_device_created",
                table: "agent_commands",
                columns: new[] { "tenant_id", "agent_device_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_tenant_device_captured",
                table: "app_usage_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_tenant_employee_captured",
                table: "app_usage_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_approval_statuses_code",
                table: "approval_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alert_deductions_tenant_employee_status",
                table: "attendance_alert_deductions",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_alert_deductions_tenant_alert",
                table: "attendance_alert_deductions",
                columns: new[] { "tenant_id", "alert_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_alerts_tenant_id_status_employee_id",
                table: "attendance_alerts",
                columns: new[] { "tenant_id", "status", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_alerts_tenant_employee_date_signal_source",
                table: "attendance_alerts",
                columns: new[] { "tenant_id", "employee_id", "work_date", "signal_key", "source_id" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_attendance_record_id",
                table: "attendance_corrections",
                column: "attendance_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_employee_id",
                table: "attendance_corrections",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_legal_entity_id",
                table: "attendance_corrections",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_presence_session_id",
                table: "attendance_corrections",
                column: "presence_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_requested_by_id",
                table: "attendance_corrections",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_reviewed_by_id",
                table: "attendance_corrections",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_tenant_legal_entity_employee_created_at",
                table: "attendance_corrections",
                columns: new[] { "tenant_id", "legal_entity_id", "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_tenant_legal_entity_status_created_at",
                table: "attendance_corrections",
                columns: new[] { "tenant_id", "legal_entity_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_corrections_pending_record_type",
                table: "attendance_corrections",
                columns: new[] { "tenant_id", "employee_id", "work_date", "correction_type" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id_date",
                table: "attendance_records",
                columns: new[] { "tenant_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id_employee_id_date",
                table: "attendance_records",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_created_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_created_at",
                table: "audit_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_audit_logs_actor_admin_user_id",
                table: "billing_audit_logs",
                column: "actor_admin_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_billing_audit_logs_invoice_id_created_at",
                table: "billing_audit_logs",
                columns: new[] { "invoice_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_audit_logs_tenant_id_created_at",
                table: "billing_audit_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_biometric_enrollment_attempts_tenant_employee_created",
                table: "biometric_enrollment_attempts",
                columns: new[] { "tenant_id", "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_biometric_profiles_tenant_employee",
                table: "biometric_profiles",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_break_records_tenant_id_employee_id_break_start",
                table: "break_records",
                columns: new[] { "tenant_id", "employee_id", "break_start" });

            migrationBuilder.CreateIndex(
                name: "ux_break_records_one_open_per_employee",
                table: "break_records",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "break_end IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_batch_id",
                table: "bulk_onboarding_batch_rows",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_onboarding_draft_id",
                table: "bulk_onboarding_batch_rows",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_tenant_id_batch_id_row_number",
                table: "bulk_onboarding_batch_rows",
                columns: new[] { "tenant_id", "batch_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_tenant_id_status",
                table: "bulk_onboarding_batch_rows",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_legal_entity_id",
                table: "bulk_onboarding_batches",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_tenant_id_status",
                table: "bulk_onboarding_batches",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_activity_logs_calendar_event_id",
                table: "calendar_event_activity_logs",
                column: "calendar_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_activity_logs_tenant_event_performed_at",
                table: "calendar_event_activity_logs",
                columns: new[] { "tenant_id", "calendar_event_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_guests_meeting_series_id",
                table: "calendar_event_guests",
                column: "meeting_series_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_guests_one_row_per_email",
                table: "calendar_event_guests",
                columns: new[] { "tenant_id", "event_id", "email" },
                unique: true,
                filter: "[event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_guests_series_email",
                table: "calendar_event_guests",
                columns: new[] { "tenant_id", "meeting_series_id", "email" },
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
                name: "ix_calendar_event_meeting_attendances_tenant_id_meeting_id",
                table: "calendar_event_meeting_attendances",
                columns: new[] { "tenant_id", "calendar_event_meeting_id" });

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_meeting_attendances_session_participant_joined",
                table: "calendar_event_meeting_attendances",
                columns: new[] { "attendance_session_id", "participant_key", "joined_at" },
                unique: true,
                filter: "attendance_session_id IS NOT NULL AND participant_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meetings_one_per_event",
                table: "calendar_event_meetings",
                column: "calendar_event_id",
                unique: true,
                filter: "[calendar_event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meetings_tenant_id_connection_id",
                table: "calendar_event_meetings",
                columns: new[] { "tenant_id", "external_calendar_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_objectives_event_objective",
                table: "calendar_event_objectives",
                columns: new[] { "calendar_event_id", "objective_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_objectives_objective_id",
                table: "calendar_event_objectives",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participant_rsvp_history_participant",
                table: "calendar_event_participant_rsvp_history",
                columns: new[] { "tenant_id", "participant_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_meeting_series_id",
                table: "calendar_event_participants",
                column: "meeting_series_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_one_row_per_employee",
                table: "calendar_event_participants",
                columns: new[] { "tenant_id", "event_id", "employee_id" },
                unique: true,
                filter: "[event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_tenant_id_employee_id",
                table: "calendar_event_participants",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_participants_series_employee",
                table: "calendar_event_participants",
                columns: new[] { "tenant_id", "meeting_series_id", "employee_id" },
                unique: true,
                filter: "meeting_series_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_calendar_event_reminders_sent_once",
                table: "calendar_event_reminders_sent",
                columns: new[] { "tenant_id", "event_id", "occurrence_start", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_tasks_event_task",
                table: "calendar_event_tasks",
                columns: new[] { "calendar_event_id", "task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_tasks_task_id",
                table: "calendar_event_tasks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_project_id",
                table: "calendar_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_tenant_project_status",
                table: "calendar_events",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_meeting_incidents_exception",
                table: "calendar_meeting_incidents",
                columns: new[] { "tenant_id", "exception_id" });

            migrationBuilder.CreateIndex(
                name: "ux_calendar_meeting_incidents_once",
                table: "calendar_meeting_incidents",
                columns: new[] { "tenant_id", "employee_id", "rule_code", "event_id", "occurrence_start" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_department_id",
                table: "checklist_templates",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_legal_entity_id",
                table: "checklist_templates",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_position_id",
                table: "checklist_templates",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_department_id",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_legal_entity_id_template_type_is_active",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "legal_entity_id", "template_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_position_id",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "position_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_template_type_is_active",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "template_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_clock_in_policy_id",
                table: "clock_in_late_deduction_rules",
                column: "clock_in_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_id",
                table: "clock_in_late_deduction_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_id_policy_id",
                table: "clock_in_late_deduction_rules",
                columns: new[] { "tenant_id", "clock_in_policy_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_policy_minute",
                table: "clock_in_late_deduction_rules",
                columns: new[] { "tenant_id", "clock_in_policy_id", "late_arrival_minute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_legal_entity_id",
                table: "clock_in_policies",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id",
                table: "clock_in_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id_legal_entity_id",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_le_active_scope",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active", "scope_type" });

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_created_by_id",
                table: "configuration_templates",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_template_key",
                table: "configuration_templates",
                column: "template_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_template_type",
                table: "configuration_templates",
                column: "template_type");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_favorites_conversation_id",
                table: "conversation_favorites",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_favorites_tenant_id_employee_id",
                table: "conversation_favorites",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_favorites_employee_id_conversation_id",
                table: "conversation_favorites",
                columns: new[] { "employee_id", "conversation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_attachments_message_id",
                table: "conversation_message_attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_hides_tenant_id_employee_id",
                table: "conversation_message_hides",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_message_hides_message_id_employee_id",
                table: "conversation_message_hides",
                columns: new[] { "message_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_message_mentions_message_id_employee_id",
                table: "conversation_message_mentions",
                columns: new[] { "message_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_message_reactions_message_id_employee_id",
                table: "conversation_message_reactions",
                columns: new[] { "message_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_revisions_message_id_revised_at",
                table: "conversation_message_revisions",
                columns: new[] { "message_id", "revised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id_created_at",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_mutes_conversation_id",
                table: "conversation_mutes",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_mutes_tenant_id_employee_id",
                table: "conversation_mutes",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_mutes_employee_id_conversation_id",
                table: "conversation_mutes",
                columns: new[] { "employee_id", "conversation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_tenant_id_employee_id",
                table: "conversation_participants",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_participants_conversation_id_employee_id",
                table: "conversation_participants",
                columns: new[] { "conversation_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversations_tenant_id_subject_source_subject_kind_subject_id",
                table: "conversations",
                columns: new[] { "tenant_id", "subject_source", "subject_kind", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_daily_work_location_confirmations_employee_id",
                table: "daily_work_location_confirmations",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_daily_work_location_confirmations_tenant_employee_date",
                table: "daily_work_location_confirmations",
                columns: new[] { "tenant_id", "employee_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_template_widget_layouts_template_widget_id_breakpoint",
                table: "dashboard_template_widget_layouts",
                columns: new[] { "template_widget_id", "breakpoint" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_template_widgets_template_id_widget_key",
                table: "dashboard_template_widgets",
                columns: new[] { "template_id", "widget_key" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_templates_role_id",
                table: "dashboard_templates",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_templates_tenant_id_dashboard_key_no_role",
                table: "dashboard_templates",
                columns: new[] { "tenant_id", "dashboard_key" },
                unique: true,
                filter: "is_no_role_template = 1 AND is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_templates_tenant_id_role_id_dashboard_key",
                table: "dashboard_templates",
                columns: new[] { "tenant_id", "role_id", "dashboard_key" },
                unique: true,
                filter: "role_id IS NOT NULL AND is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_widget_layouts_widget_id_breakpoint",
                table: "dashboard_widget_layouts",
                columns: new[] { "widget_id", "breakpoint" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_widgets_dashboard_id_widget_key",
                table: "dashboard_widgets",
                columns: new[] { "dashboard_id", "widget_key" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_departments_head_position_id",
                table: "departments",
                column: "head_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_legal_entity_id",
                table: "departments",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_parent_department_id",
                table: "departments",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id",
                table: "departments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id_name",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_current_device_registration_id",
                table: "device_change_requests",
                column: "current_device_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_reviewed_by_id",
                table: "device_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_tenant_status",
                table: "device_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_device_change_requests_pending_employee",
                table: "device_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_employee_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_employee_addresses_employee_id",
                table: "employee_addresses",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_addresses_tenant_id_employee_id",
                table: "employee_addresses",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_bank_details_employee_id",
                table: "employee_bank_details",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_bank_details_tenant_id_employee_id",
                table: "employee_bank_details",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_face_scan_id",
                table: "employee_check_ins",
                column: "face_scan_id",
                unique: true,
                filter: "[face_scan_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_tenant_id_device_registration_id",
                table: "employee_check_ins",
                columns: new[] { "tenant_id", "device_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_tenant_id_user_id_checked_in_at",
                table: "employee_check_ins",
                columns: new[] { "tenant_id", "user_id", "checked_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_assigned_to_id",
                table: "employee_checklist_tasks",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_employee_id",
                table: "employee_checklist_tasks",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_offboarding_record_id",
                table: "employee_checklist_tasks",
                column: "offboarding_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_template_id",
                table: "employee_checklist_tasks",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_tenant_id_employee_id_lifecycle_type_sequence",
                table: "employee_checklist_tasks",
                columns: new[] { "tenant_id", "employee_id", "lifecycle_type", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_tenant_id_offboarding_record_id",
                table: "employee_checklist_tasks",
                columns: new[] { "tenant_id", "offboarding_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_work_task_id",
                table: "employee_checklist_tasks",
                column: "work_task_id",
                unique: true,
                filter: "work_task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_employee_id",
                table: "employee_dependents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependents_tenant_id_employee_id",
                table: "employee_dependents",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_emergency_contacts_employee_id",
                table: "employee_emergency_contacts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_emergency_contacts_tenant_id_employee_id",
                table: "employee_emergency_contacts",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_tenant_id_ancestor_employee_id_depth",
                table: "employee_hierarchy_closure",
                columns: new[] { "tenant_id", "ancestor_employee_id", "depth" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_hierarchy_closure_tenant_id_descendant_employee_id",
                table: "employee_hierarchy_closure",
                columns: new[] { "tenant_id", "descendant_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ux_employee_monitoring_overrides_tenant_employee",
                table: "employee_monitoring_overrides",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_locations_employee_id",
                table: "employee_work_locations",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_employee_work_locations_tenant_employee",
                table: "employee_work_locations",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_tenant_id_device_registration_id",
                table: "employee_work_sessions",
                columns: new[] { "tenant_id", "device_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_tenant_id_user_id_clock_in_at",
                table: "employee_work_sessions",
                columns: new[] { "tenant_id", "user_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_employee_number",
                table: "employees",
                columns: new[] { "tenant_id", "employee_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                table: "employees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id_legal_entity_id",
                table: "employees",
                columns: new[] { "user_id", "legal_entity_id" },
                unique: true,
                filter: "[legal_entity_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employees_work_mode_id",
                table: "employees",
                column: "work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_statuses_code",
                table: "employment_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_types_code",
                table: "employment_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_assets_file_record_id",
                table: "entity_assets",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_assets_owner_type_owner_id",
                table: "entity_assets",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_tenant_employee_type_status",
                table: "exceptions",
                columns: new[] { "tenant_id", "employee_id", "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_tenant_status_detected",
                table: "exceptions",
                columns: new[] { "tenant_id", "status", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_connections_one_per_user_provider",
                table: "external_calendar_connections",
                columns: new[] { "tenant_id", "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_connections_tenant_id_user_id",
                table: "external_calendar_connections",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_one_per_connection_event",
                table: "external_calendar_event_links",
                columns: new[] { "external_calendar_connection_id", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_tenant_id_calendar_event_id",
                table: "external_calendar_event_links",
                columns: new[] { "tenant_id", "calendar_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_tenant_id_connection_id",
                table: "external_calendar_event_links",
                columns: new[] { "tenant_id", "external_calendar_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ux_external_calendar_event_links_meeting_task_connection",
                table: "external_calendar_event_links",
                columns: new[] { "meeting_task_id", "external_calendar_connection_id" },
                unique: true,
                filter: "meeting_task_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_face_verification_attempts_tenant_employee_purpose_created",
                table: "face_verification_attempts",
                columns: new[] { "tenant_id", "employee_id", "purpose", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feature_access_grants_tenant_id_grantee_type_grantee_id_module",
                table: "feature_access_grants",
                columns: new[] { "tenant_id", "grantee_type", "grantee_id", "module" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_records_tenant_id_status",
                table: "file_records",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_file_records_uploaded_by_user_id",
                table: "file_records",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_completed_file_record_id",
                table: "file_upload_reservations",
                column: "completed_file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_reserved_by_user_id",
                table: "file_upload_reservations",
                column: "reserved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_tenant_id_status_expires_at",
                table: "file_upload_reservations",
                columns: new[] { "tenant_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_global_app_catalog_category_last_seen",
                table: "global_app_catalog",
                columns: new[] { "category", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ux_global_app_catalog_process_name",
                table: "global_app_catalog",
                column: "process_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_settings_one_per_legal_entity",
                table: "holiday_calendar_settings",
                columns: new[] { "tenant_id", "legal_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expires_at",
                table: "idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_key_scope_requester_id",
                table: "idempotency_records",
                columns: new[] { "key", "scope", "requester_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_agent_device_id",
                table: "inactivity_capture_attempts",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_evidence_asset_id",
                table: "inactivity_capture_attempts",
                column: "evidence_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_tenant_employee_prompted",
                table: "inactivity_capture_attempts",
                columns: new[] { "tenant_id", "employee_id", "prompted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_decision_journal_tenant_id_actor_employee_id_decided_at",
                table: "inbox_decision_journal",
                columns: new[] { "tenant_id", "actor_employee_id", "decided_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_decision_journal_tenant_id_source_kind_item_id_decided_at",
                table: "inbox_decision_journal",
                columns: new[] { "tenant_id", "source", "kind", "item_id", "decided_at" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_catalog_created_by_id",
                table: "integration_catalog",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_catalog_crownus_app_provider",
                table: "integration_catalog",
                column: "crownus_app_provider");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_expires_at",
                table: "invitation_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_tenant_id_invited_email",
                table: "invitation_tokens",
                columns: new[] { "tenant_id", "invited_email" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_token_hash",
                table: "invitation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_labels_project_id",
                table: "labels",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_labels_tenant_id_project_id_name",
                table: "labels",
                columns: new[] { "tenant_id", "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_approver_employee_id",
                table: "leave_approval_delegates",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_delegate_employee_id",
                table: "leave_approval_delegates",
                column: "delegate_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_tenant_approver",
                table: "leave_approval_delegates",
                columns: new[] { "tenant_id", "approver_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_employee_id",
                table: "leave_balance_audits",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_leave_type_id",
                table: "leave_balance_audits",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_related_request_id",
                table: "leave_balance_audits",
                column: "related_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_tenant_employee_type",
                table: "leave_balance_audits",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_tenant_employee_type_year",
                table: "leave_balance_audits",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "year" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_employee_id",
                table: "leave_entitlements",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_leave_type_id",
                table: "leave_entitlements",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_tenant_employee_type_year",
                table: "leave_entitlements",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_leave_type_id",
                table: "leave_policies",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_tenant_id",
                table: "leave_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_tenant_leave_type",
                table: "leave_policies",
                columns: new[] { "tenant_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_leave_policy_id",
                table: "leave_policy_assignments",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_leave_type_id",
                table: "leave_policy_assignments",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_open_target_leave_type",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "target_kind", "target_id", "leave_type_id" },
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_tenant_policy",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "leave_policy_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_tenant_target",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_blackout_periods_leave_policy_version_id",
                table: "leave_policy_blackout_periods",
                column: "leave_policy_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_blackout_periods_tenant_version",
                table: "leave_policy_blackout_periods",
                columns: new[] { "tenant_id", "leave_policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_policy_effective_from",
                table: "leave_policy_versions",
                columns: new[] { "leave_policy_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_policy_version_number",
                table: "leave_policy_versions",
                columns: new[] { "leave_policy_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_tenant_id",
                table: "leave_policy_versions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_approver_employee_id",
                table: "leave_request_approvers",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_leave_request_id",
                table: "leave_request_approvers",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_tenant_approver_status",
                table: "leave_request_approvers",
                columns: new[] { "tenant_id", "approver_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_tenant_request",
                table: "leave_request_approvers",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_leave_request_id",
                table: "leave_request_day_allocations",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_tenant_date_status",
                table: "leave_request_day_allocations",
                columns: new[] { "tenant_id", "leave_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_day_allocations_tenant_request_date",
                table: "leave_request_day_allocations",
                columns: new[] { "tenant_id", "leave_request_id", "leave_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_file_record_id",
                table: "leave_request_documents",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_leave_request_id",
                table: "leave_request_documents",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_tenant_request",
                table: "leave_request_documents",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_employee_id",
                table: "leave_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_leave_type_id",
                table: "leave_requests",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_employee",
                table: "leave_requests",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests",
                columns: new[] { "tenant_id", "start_at", "end_at" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_status",
                table: "leave_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id",
                table: "leave_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_code",
                table: "leave_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_name",
                table: "leave_types",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_acceptance_records_tenant_id_user_id_document_type",
                table: "legal_acceptance_records",
                columns: new[] { "tenant_id", "user_id", "document_type" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_content_hash",
                table: "legal_document_versions",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_document_type_published",
                table: "legal_document_versions",
                column: "document_type",
                unique: true,
                filter: "status = 'published'");

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_document_type_status_is_required_published_at",
                table: "legal_document_versions",
                columns: new[] { "document_type", "status", "is_required", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_document_type_version",
                table: "legal_document_versions",
                columns: new[] { "document_type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_published_by_id",
                table: "legal_document_versions",
                column: "published_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_parent_legal_entity_id",
                table: "legal_entities",
                column: "parent_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id",
                table: "legal_entities",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_company_code",
                table: "legal_entities",
                columns: new[] { "tenant_id", "company_code" },
                unique: true,
                filter: "company_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_name",
                table: "legal_entities",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_registration_number",
                table: "legal_entities",
                columns: new[] { "tenant_id", "registration_number" },
                unique: true,
                filter: "registration_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_legal_login_challenges_challenge_hash",
                table: "legal_login_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_login_challenges_expires_at",
                table: "legal_login_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_legal_login_challenges_superseded_by_id",
                table: "legal_login_challenges",
                column: "superseded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_login_challenges_tenant_id_user_id_expires_at",
                table: "legal_login_challenges",
                columns: new[] { "tenant_id", "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_login_challenges_user_id",
                table: "legal_login_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_employee_id",
                table: "location_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_legal_entity_id",
                table: "location_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_reviewed_by_id",
                table: "location_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_tenant_legal_entity_status",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "legal_entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_tenant_status",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_location_change_requests_active_employee",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status IN ('pending', 'approved')");

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_challenge_hash",
                table: "login_workspace_selection_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_expires_at",
                table: "login_workspace_selection_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_normalized_email_created_at",
                table: "login_workspace_selection_challenges",
                columns: new[] { "normalized_email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_company_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Company' AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_department_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "covered_department_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Department' AND status = 'active' AND covered_department_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_position_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "covered_position_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Position' AND status = 'active' AND covered_position_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_covered_department_id",
                table: "management_coverage_records",
                column: "covered_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_covered_position_id",
                table: "management_coverage_records",
                column: "covered_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_legal_entity_id",
                table: "management_coverage_records",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_owner_position_id",
                table: "management_coverage_records",
                column: "owner_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_tenant_id",
                table: "management_coverage_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_tenant_legal_entity_owner",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "owner_position_id" });

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
                filter: "attendee_kind = 'employee' AND employee_id IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "ix_meeting_signals_tenant_employee_captured",
                table: "meeting_signals",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_challenge_hash",
                table: "mfa_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_expires_at",
                table: "mfa_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_tenant_id_user_id_expires_at",
                table: "mfa_challenges",
                columns: new[] { "tenant_id", "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_user_id",
                table: "mfa_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_module_catalog_price_history_module_key",
                table: "module_catalog_price_history",
                column: "module_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_features_module_key",
                table: "module_features",
                column: "module_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_integration_links_integration_key",
                table: "module_integration_links",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_integration_links_linked_by_id",
                table: "module_integration_links",
                column: "linked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_module_permission_ownership_permission_code",
                table: "module_permission_ownership",
                column: "permission_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_activity_snapshot_id",
                table: "monitoring_evidence_assets",
                column: "activity_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_agent_command_id",
                table: "monitoring_evidence_assets",
                column: "agent_command_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_agent_device_id",
                table: "monitoring_evidence_assets",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_file_record_id",
                table: "monitoring_evidence_assets",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_tenant_command",
                table: "monitoring_evidence_assets",
                columns: new[] { "tenant_id", "agent_command_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_tenant_employee_captured",
                table: "monitoring_evidence_assets",
                columns: new[] { "tenant_id", "employee_id", "captured_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_face_scans_storage_key",
                table: "monitoring_face_scans",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_face_scans_tenant_id_check_in_id",
                table: "monitoring_face_scans",
                columns: new[] { "tenant_id", "check_in_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_feature_toggles_legal_entity_id",
                table: "monitoring_feature_toggles",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_feature_toggles_tenant_fallback",
                table: "monitoring_feature_toggles",
                column: "tenant_id",
                unique: true,
                filter: "legal_entity_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_feature_toggles_tenant_legal_entity",
                table: "monitoring_feature_toggles",
                columns: new[] { "tenant_id", "legal_entity_id" },
                unique: true,
                filter: "[legal_entity_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_notifications_tenant_employee_created",
                table: "monitoring_notifications",
                columns: new[] { "tenant_id", "employee_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_notifications_tenant_employee_type_created",
                table: "monitoring_notifications",
                columns: new[] { "tenant_id", "employee_id", "type", "created_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_permission_consents_tenant_employee_accepted",
                table: "monitoring_permission_consents",
                columns: new[] { "tenant_id", "employee_id", "accepted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_policy_overrides_tenant_scope",
                table: "monitoring_policy_overrides",
                columns: new[] { "tenant_id", "scope_type", "scope_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_identity",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "event_id", "recipient_user_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_channel_recipient_status",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "channel", "recipient_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_one_draft_per_template",
                table: "notification_template_versions",
                column: "template_id",
                unique: true,
                filter: "status = 'draft'");

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_template_id_version",
                table: "notification_template_versions",
                columns: new[] { "template_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_event_key_locale",
                table: "notification_templates",
                columns: new[] { "event_key", "locale" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_one_per_code",
                table: "notification_templates",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_recipient_user_id_is_read_created_at",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient_user_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_one_default_per_project",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id" },
                unique: true,
                filter: "is_default = 1");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_parent_objective_id",
                table: "objectives",
                column: "parent_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_project_id",
                table: "objectives",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_owner_id_is_active",
                table: "objectives",
                columns: new[] { "tenant_id", "owner_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_project_id_is_achieved",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id", "is_achieved" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_project_id_parent_objective_id",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id", "parent_objective_id" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_reporting_manager_id",
                table: "objectives",
                columns: new[] { "tenant_id", "reporting_manager_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_checklist_template_id",
                table: "offboarding_records",
                column: "checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_employee_id",
                table: "offboarding_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_tenant_id_employee_id",
                table: "offboarding_records",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status IN ('initiated','in_progress')");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_tenant_id_employee_id_status",
                table: "offboarding_records",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_employee_checklist_task_id",
                table: "offboarding_task_bypass_requests",
                column: "employee_checklist_task_id",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_offboarding_record_id",
                table: "offboarding_task_bypass_requests",
                column: "offboarding_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_tenant_id_approver_id_status",
                table: "offboarding_task_bypass_requests",
                columns: new[] { "tenant_id", "approver_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_department_id",
                table: "onboarding_drafts",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_legal_entity_id",
                table: "onboarding_drafts",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_position_id",
                table: "onboarding_drafts",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_tenant_id_started_by_id",
                table: "onboarding_drafts",
                columns: new[] { "tenant_id", "started_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_tenant_id_status",
                table: "onboarding_drafts",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_work_mode_id",
                table: "onboarding_drafts",
                column: "work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_deduplication_key",
                table: "outbox_messages",
                column: "deduplication_key",
                unique: true,
                filter: "deduplication_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processing_locked_until",
                table: "outbox_messages",
                column: "locked_until",
                filter: "status = 'processing'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id_tenant_id",
                table: "password_reset_tokens",
                columns: new[] { "user_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_configs_gateway_key",
                table: "payment_gateway_configs",
                column: "gateway_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_configs_provider",
                table: "payment_gateway_configs",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_country_routes_country_code_environment_is_active",
                table: "payment_gateway_country_routes",
                columns: new[] { "country_code", "environment", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_country_routes_gateway_config_id",
                table: "payment_gateway_country_routes",
                column: "gateway_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_credentials_payment_gateway_config_id_is_active",
                table: "payment_gateway_credentials",
                columns: new[] { "payment_gateway_config_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_recurrence_parent_id",
                table: "personal_calendar_events",
                column: "recurrence_parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_tenant_id_created_by_id",
                table: "personal_calendar_events",
                columns: new[] { "tenant_id", "created_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_tenant_id_recurrence_parent_id",
                table: "personal_calendar_events",
                columns: new[] { "tenant_id", "recurrence_parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_tenant_id_start_date_end_date",
                table: "personal_calendar_events",
                columns: new[] { "tenant_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_planned_task_blocks_employee_id",
                table: "planned_task_blocks",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_planned_task_blocks_task_id",
                table: "planned_task_blocks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_planned_task_blocks_tenant_id_employee_id_start_at",
                table: "planned_task_blocks",
                columns: new[] { "tenant_id", "employee_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_auth_events_created_at",
                table: "platform_auth_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_auth_events_user_id",
                table: "platform_auth_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_deactivated_by_id",
                table: "platform_oauth_app_credentials",
                column: "deactivated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_platform_oauth_app_id",
                table: "platform_oauth_app_credentials",
                column: "platform_oauth_app_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_rotated_by_id",
                table: "platform_oauth_app_credentials",
                column: "rotated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_apps_provider",
                table: "platform_oauth_apps",
                column: "provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_apps_updated_by_id",
                table: "platform_oauth_apps",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_providers_provider_family_is_active_display_name",
                table: "platform_providers",
                columns: new[] { "provider_family", "is_active", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_permissions_granted_by_id",
                table: "platform_role_permissions",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_permissions_permission_code",
                table: "platform_role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_platform_roles_created_by_id",
                table: "platform_roles",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_roles_name",
                table: "platform_roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_service_keys_service_key",
                table: "platform_service_keys",
                column: "service_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_service_keys_updated_by_id",
                table: "platform_service_keys",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_platform_user_id",
                table: "platform_user_credentials",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_platform_user_id_credential_type",
                table: "platform_user_credentials",
                columns: new[] { "platform_user_id", "credential_type" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_reset_token_hash",
                table: "platform_user_credentials",
                column: "reset_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_email",
                table: "platform_user_invites",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_invite_token_hash",
                table: "platform_user_invites",
                column: "invite_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_invited_by_id",
                table: "platform_user_invites",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_platform_user_id",
                table: "platform_user_invites",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_roles_assigned_by_id",
                table: "platform_user_roles",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_roles_role_id",
                table: "platform_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_account_id",
                table: "platform_user_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_expires_at",
                table: "platform_user_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_token_hash",
                table: "platform_user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_created_by_id",
                table: "platform_users",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_invite_status",
                table: "platform_users",
                column: "invite_status");

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_status",
                table: "platform_users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_position_id",
                table: "position_access_templates",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_role_id",
                table: "position_access_templates",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_tenant_id",
                table: "position_access_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_tenant_id_position_id",
                table: "position_access_templates",
                columns: new[] { "tenant_id", "position_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_one_active_primary_per_employee",
                table: "position_assignments",
                column: "employee_id",
                unique: true,
                filter: "assignment_kind = 'PrimaryEmployment' AND assignment_status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_position_id",
                table: "position_assignments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_tenant_id_employee_id",
                table: "position_assignments",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_assignments_tenant_id_position_id_assignment_status",
                table: "position_assignments",
                columns: new[] { "tenant_id", "position_id", "assignment_status" });

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_position_id",
                table: "position_reporting_history",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_reports_to_position_id",
                table: "position_reporting_history",
                column: "reports_to_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_tenant_id",
                table: "position_reporting_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_tenant_position_effective",
                table: "position_reporting_history",
                columns: new[] { "tenant_id", "position_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_department_id",
                table: "positions",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_legal_entity_id",
                table: "positions",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_reports_to_position_id",
                table: "positions",
                column: "reports_to_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id",
                table: "positions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_code",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true,
                filter: "code IS NOT NULL AND legal_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_department_id",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_name",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "name" },
                unique: true,
                filter: "legal_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_presence_sessions_tenant_id_employee_id_date",
                table: "presence_sessions",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_categories_tenant_id_name",
                table: "project_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_objective_id",
                table: "project_member_invitations",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "project_id", "objective_id", "invited_employee_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending_leader",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "objective_id" },
                unique: true,
                filter: "status = 'pending' AND invite_type = 'leader'");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_project_id",
                table: "project_member_invitations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_tenant_invited_employee_status",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "invited_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_objective_id",
                table: "project_members",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_id",
                table: "project_members",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_employee_active_project",
                table: "project_members",
                columns: new[] { "tenant_id", "employee_id", "is_active", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_project_objective_active",
                table: "project_members",
                columns: new[] { "tenant_id", "project_id", "objective_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_project_objective_employee",
                table: "project_members",
                columns: new[] { "tenant_id", "project_id", "objective_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_owning_legal_entity_id",
                table: "projects",
                column: "owning_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_category_id_is_active",
                table: "projects",
                columns: new[] { "tenant_id", "category_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_identifier",
                table: "projects",
                columns: new[] { "tenant_id", "identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_is_achieved",
                table: "projects",
                columns: new[] { "tenant_id", "is_achieved" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_owning_legal_entity_id_updated_at",
                table: "projects",
                columns: new[] { "tenant_id", "owning_legal_entity_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_system_purpose",
                table: "projects",
                columns: new[] { "tenant_id", "system_purpose" },
                unique: true,
                filter: "system_purpose IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_id",
                table: "refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_one_active_project_release",
                table: "release_calendar",
                columns: new[] { "version_id", "recipient_user_id" },
                unique: true,
                filter: "is_active = 1 AND reminder_type = 'project_release'");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_project_id",
                table: "release_calendar",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_tenant_recipient_scheduled_active",
                table: "release_calendar",
                columns: new[] { "tenant_id", "recipient_user_id", "scheduled_date", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_employee_id",
                table: "reporting_manager_changes",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_new_manager_employee_id",
                table: "reporting_manager_changes",
                column: "new_manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_previous_manager_employee_id",
                table: "reporting_manager_changes",
                column: "previous_manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_tenant_batch",
                table: "reporting_manager_changes",
                columns: new[] { "tenant_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reporting_manager_changes_tenant_employee_effective_at",
                table: "reporting_manager_changes",
                columns: new[] { "tenant_id", "employee_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_templates_name",
                table: "role_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_source_template_id",
                table: "roles",
                column: "source_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_source_template_id",
                table: "roles",
                columns: new[] { "tenant_id", "source_template_id" },
                unique: true,
                filter: "source_template_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_decided_by_user_id",
                table: "seat_increase_requests",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_onboarding_draft_id",
                table: "seat_increase_requests",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_requested_by_user_id",
                table: "seat_increase_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_tenant_id_onboarding_draft_id",
                table: "seat_increase_requests",
                columns: new[] { "tenant_id", "onboarding_draft_id" },
                unique: true,
                filter: "status = 'pending' AND onboarding_draft_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_tenant_id_status",
                table: "seat_increase_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_key_hash",
                table: "sessions",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id",
                table: "sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_is_revoked",
                table: "sessions",
                columns: new[] { "user_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "ix_severities_code",
                table: "severities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sprint_activity_logs_sprint_id",
                table: "sprint_activity_logs",
                column: "sprint_id");

            migrationBuilder.CreateIndex(
                name: "ix_sprint_activity_logs_tenant_id_sprint_id_occurred_at",
                table: "sprint_activity_logs",
                columns: new[] { "tenant_id", "sprint_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sprints_tenant_id_project_id_status",
                table: "sprints",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_external_invoice_id",
                table: "subscription_invoices",
                column: "external_invoice_id",
                unique: true,
                filter: "external_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_invoice_number",
                table: "subscription_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_tenant_id_due_at",
                table: "subscription_invoices",
                columns: new[] { "tenant_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_tenant_id_status",
                table: "subscription_invoices",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_tenant_subscription_id",
                table: "subscription_invoices",
                column: "tenant_subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_code",
                table: "subscription_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_assignments_one_per_task",
                table: "task_assignments",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_one_name_per_project",
                table: "task_categories",
                columns: new[] { "tenant_id", "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_one_system_key_per_project",
                table: "task_categories",
                columns: new[] { "tenant_id", "project_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_categories_tenant_id_project_id_display_order",
                table: "task_categories",
                columns: new[] { "tenant_id", "project_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_one_open_per_task",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "task_id" },
                unique: true,
                filter: "clock_out_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_task_id",
                table: "task_clocking_sessions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_tenant_id_employee_id_clock_in_at",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "employee_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_tenant_id_task_id_clock_in_at",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "task_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_logs_comment_id",
                table: "task_comment_logs",
                column: "comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_logs_task_id",
                table: "task_comment_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_logs_tenant_id_task_id_occurred_at",
                table: "task_comment_logs",
                columns: new[] { "tenant_id", "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id_emoji",
                table: "task_comment_reactions",
                columns: new[] { "comment_id", "employee_id", "emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_parent_comment_id",
                table: "task_comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_task_id",
                table: "task_comments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_tenant_id_task_id_created_at",
                table: "task_comments",
                columns: new[] { "tenant_id", "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_drafts_tenant_id_owner_user_id",
                table: "task_drafts",
                columns: new[] { "tenant_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_task_id",
                table: "task_edit_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_tenant_id_task_id_changed_at",
                table: "task_edit_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_clocking_session_id",
                table: "task_percentage_logs",
                column: "clocking_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_task_id",
                table: "task_percentage_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_tenant_id_task_id_changed_at",
                table: "task_percentage_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_from_status_id",
                table: "task_status_change_logs",
                column: "from_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_task_id",
                table: "task_status_change_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_tenant_id_task_id_changed_at",
                table: "task_status_change_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_to_status_id",
                table: "task_status_change_logs",
                column: "to_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_statuses_one_name_per_scope",
                table: "task_statuses",
                columns: new[] { "tenant_id", "project_id", "objective_id", "name" },
                unique: true,
                filter: "[objective_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_statuses_tenant_id_project_id_objective_id_display_order",
                table: "task_statuses",
                columns: new[] { "tenant_id", "project_id", "objective_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_category_id",
                table: "tasks",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_one_short_id_per_tenant",
                table: "tasks",
                columns: new[] { "tenant_id", "short_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_parent_task_id",
                table: "tasks",
                column: "parent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_sprint_id",
                table: "tasks",
                column: "sprint_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_status_id",
                table: "tasks",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_objective_id_status_id",
                table: "tasks",
                columns: new[] { "tenant_id", "objective_id", "status_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_parent_task_id",
                table: "tasks",
                columns: new[] { "tenant_id", "parent_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_project_id_category_id",
                table: "tasks",
                columns: new[] { "tenant_id", "project_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_project_id_visibility_scope",
                table: "tasks",
                columns: new[] { "tenant_id", "project_id", "visibility_scope" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_auth_policies_tenant_id",
                table: "tenant_auth_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_applied_by_id",
                table: "tenant_configuration_template_applications",
                column: "applied_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_configuration_template_id",
                table: "tenant_configuration_template_applications",
                column: "configuration_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_tenant_id_applied_at",
                table: "tenant_configuration_template_applications",
                columns: new[] { "tenant_id", "applied_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_connected_by_user_id",
                table: "tenant_integration_credentials",
                column: "connected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_integration_key",
                table: "tenant_integration_credentials",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_tenant_id_integration_key",
                table: "tenant_integration_credentials",
                columns: new[] { "tenant_id", "integration_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_tenant_id_status",
                table: "tenant_integration_credentials",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_rules_template_id",
                table: "tenant_notification_rules",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_rules_tenant_id_event_key",
                table: "tenant_notification_rules",
                columns: new[] { "tenant_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_one_time_charges_tenant_id_setup_option_key",
                table: "tenant_one_time_charges",
                columns: new[] { "tenant_id", "setup_option_key" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_session_exchange_challenges_code_hash_active",
                table: "tenant_session_exchange_challenges",
                column: "code_hash",
                unique: true,
                filter: "consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_session_exchange_challenges_expires_at",
                table: "tenant_session_exchange_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_session_exchange_challenges_tenant_id_user_id",
                table: "tenant_session_exchange_challenges",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_session_exchange_challenges_user_id",
                table: "tenant_session_exchange_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_setup_selections_tenant_id_setup_option_key",
                table: "tenant_setup_selections",
                columns: new[] { "tenant_id", "setup_option_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_changed_at",
                table: "tenant_status_histories",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_changed_by_id",
                table: "tenant_status_histories",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_tenant_id",
                table: "tenant_status_histories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_tenant_id",
                table: "tenant_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_legal_entity_id",
                table: "tenant_work_modes",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_id",
                table: "tenant_work_modes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_id_legal_entity_id",
                table: "tenant_work_modes",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_le_active",
                table: "tenant_work_modes",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_code_hash",
                table: "tray_activation_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_legal_entity_id",
                table: "tray_activation_codes",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_user_id_tenant_id_created_at",
                table: "tray_activation_codes",
                columns: new[] { "user_id", "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_app_releases_channel_is_active",
                table: "tray_app_releases",
                columns: new[] { "channel", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_app_releases_channel_version",
                table: "tray_app_releases",
                columns: new[] { "channel", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_approved_legal_entity_id",
                table: "tray_device_authorizations",
                column: "approved_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_device_code_hash",
                table: "tray_device_authorizations",
                column: "device_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_device_fingerprint_hash_created_at",
                table: "tray_device_authorizations",
                columns: new[] { "device_fingerprint_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_status_expires_at",
                table: "tray_device_authorizations",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_user_code_hash_status_expires_at",
                table: "tray_device_authorizations",
                columns: new[] { "user_code_hash", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_refresh_tokens_device_registration_id_is_revoked",
                table: "tray_device_refresh_tokens",
                columns: new[] { "device_registration_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_refresh_tokens_token_hash",
                table: "tray_device_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_device_fingerprint",
                table: "tray_device_registrations",
                column: "device_fingerprint");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_legal_entity_id",
                table: "tray_device_registrations",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_tenant_id_user_id_is_active",
                table: "tray_device_registrations",
                columns: new[] { "tenant_id", "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_tenant_id_user_id_is_active_last_seen_at",
                table: "tray_device_registrations",
                columns: new[] { "tenant_id", "user_id", "is_active", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ux_user_dashboards_tenant_id_user_id_dashboard_key",
                table: "user_dashboards",
                columns: new[] { "tenant_id", "user_id", "dashboard_key" },
                unique: true,
                filter: "is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_user_external_identities_tenant_id_provider_provider_subject",
                table: "user_external_identities",
                columns: new[] { "tenant_id", "provider", "provider_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_external_identities_user_id",
                table: "user_external_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_integration_key",
                table: "user_integration_connections",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_status",
                table: "user_integration_connections",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_integration_key",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "integration_key" });

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_user_id",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_user_id_integration_key",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "user_id", "integration_key" },
                unique: true,
                filter: "disconnected_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_user_id",
                table: "user_integration_connections",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_user_id_method_type_is_verified",
                table: "user_mfa",
                columns: new[] { "user_id", "method_type", "is_verified" });

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_permission_id",
                table: "user_permission_overrides",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_tenant_id_user_id_permission_id",
                table: "user_permission_overrides",
                columns: new[] { "tenant_id", "user_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_source_position_access_template_id",
                table: "user_roles",
                column: "source_position_access_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id",
                table: "user_roles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email_active_lookup",
                table: "users",
                columns: new[] { "normalized_email", "tenant_id", "id" },
                filter: "is_active = 1 AND is_deleted = 0");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_normalized_email",
                table: "users",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_version_statuses_code",
                table: "version_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versions_project_id",
                table: "versions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_versions_status_id",
                table: "versions",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_versions_tenant_id_project_id_status_id",
                table: "versions",
                columns: new[] { "tenant_id", "project_id", "status_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_tenant_id_approver_employee_id_status",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "approver_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_wm_approval_requests_tenant_id_project_id_status",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_wm_approval_requests_one_pending_per_target_action",
                table: "wm_approval_requests",
                columns: new[] { "tenant_id", "target_type", "target_id", "action_type" },
                unique: true,
                filter: "status = 'pending' AND target_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wm_module_hour_summaries_tenant_id_project_id",
                table: "wm_module_hour_summaries",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wm_monitor_alerts_tenant_id_project_id_resolved_at",
                table: "wm_monitor_alerts",
                columns: new[] { "tenant_id", "project_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ux_wm_monitor_alerts_open_key",
                table: "wm_monitor_alerts",
                columns: new[] { "tenant_id", "target_id", "rule_code", "subject_employee_id" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wm_notification_log_tenant_id_project_id_recipient_created_at",
                table: "wm_notification_log",
                columns: new[] { "tenant_id", "project_id", "recipient_employee_id", "created_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_wm_task_rate_settings_tenant_id_legal_entity_id",
                table: "wm_task_rate_settings",
                columns: new[] { "tenant_id", "legal_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_current_work_mode_id",
                table: "work_area_change_requests",
                column: "current_work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_employee_id",
                table: "work_area_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_legal_entity_id",
                table: "work_area_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_requested_work_mode_id",
                table: "work_area_change_requests",
                column: "requested_work_mode_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_reviewed_by_id",
                table: "work_area_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_legal_entity_status",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "legal_entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_area_change_requests_tenant_status",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_work_area_change_requests_active_employee_date",
                table: "work_area_change_requests",
                columns: new[] { "tenant_id", "employee_id", "date" },
                unique: true,
                filter: "status IN ('pending', 'approved')");

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_requests_departments_target_department_id",
                table: "access_grant_requests",
                column: "target_department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_requests_onboarding_drafts_onboarding_draft_id",
                table: "access_grant_requests",
                column: "onboarding_draft_id",
                principalTable: "onboarding_drafts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_requests_position_access_templates_position_access_template_id",
                table: "access_grant_requests",
                column: "position_access_template_id",
                principalTable: "position_access_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_access_grant_requests_positions_target_position_id",
                table: "access_grant_requests",
                column: "target_position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_bulk_onboarding_batch_rows_onboarding_drafts_onboarding_draft_id",
                table: "bulk_onboarding_batch_rows",
                column: "onboarding_draft_id",
                principalTable: "onboarding_drafts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_checklist_templates_departments_department_id",
                table: "checklist_templates",
                column: "department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_checklist_templates_positions_position_id",
                table: "checklist_templates",
                column: "position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_positions_head_position_id",
                table: "departments",
                column: "head_position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_positions_departments_department_id",
                table: "positions");

            migrationBuilder.DropTable(
                name: "access_grant_requests");

            migrationBuilder.DropTable(
                name: "activity_daily_summary");

            migrationBuilder.DropTable(
                name: "activity_raw_buffer");

            migrationBuilder.DropTable(
                name: "app_usage_snapshots");

            migrationBuilder.DropTable(
                name: "approval_statuses");

            migrationBuilder.DropTable(
                name: "attendance_alert_deductions");

            migrationBuilder.DropTable(
                name: "attendance_alerts");

            migrationBuilder.DropTable(
                name: "attendance_corrections");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "billing_audit_logs");

            migrationBuilder.DropTable(
                name: "biometric_enrollment_attempts");

            migrationBuilder.DropTable(
                name: "biometric_profiles");

            migrationBuilder.DropTable(
                name: "break_records");

            migrationBuilder.DropTable(
                name: "bulk_onboarding_batch_rows");

            migrationBuilder.DropTable(
                name: "calendar_event_activity_logs");

            migrationBuilder.DropTable(
                name: "calendar_event_guests");

            migrationBuilder.DropTable(
                name: "calendar_event_meeting_attendances");

            migrationBuilder.DropTable(
                name: "calendar_event_objectives");

            migrationBuilder.DropTable(
                name: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropTable(
                name: "calendar_event_participants");

            migrationBuilder.DropTable(
                name: "calendar_event_reminders_sent");

            migrationBuilder.DropTable(
                name: "calendar_event_tasks");

            migrationBuilder.DropTable(
                name: "calendar_meeting_incidents");

            migrationBuilder.DropTable(
                name: "clock_in_late_deduction_rules");

            migrationBuilder.DropTable(
                name: "conversation_favorites");

            migrationBuilder.DropTable(
                name: "conversation_message_attachments");

            migrationBuilder.DropTable(
                name: "conversation_message_hides");

            migrationBuilder.DropTable(
                name: "conversation_message_mentions");

            migrationBuilder.DropTable(
                name: "conversation_message_reactions");

            migrationBuilder.DropTable(
                name: "conversation_message_revisions");

            migrationBuilder.DropTable(
                name: "conversation_mutes");

            migrationBuilder.DropTable(
                name: "conversation_participants");

            migrationBuilder.DropTable(
                name: "daily_work_location_confirmations");

            migrationBuilder.DropTable(
                name: "dashboard_template_widget_layouts");

            migrationBuilder.DropTable(
                name: "dashboard_widget_layouts");

            migrationBuilder.DropTable(
                name: "device_change_requests");

            migrationBuilder.DropTable(
                name: "device_state_snapshots");

            migrationBuilder.DropTable(
                name: "employee_addresses");

            migrationBuilder.DropTable(
                name: "employee_bank_details");

            migrationBuilder.DropTable(
                name: "employee_check_ins");

            migrationBuilder.DropTable(
                name: "employee_dependents");

            migrationBuilder.DropTable(
                name: "employee_emergency_contacts");

            migrationBuilder.DropTable(
                name: "employee_hierarchy_closure");

            migrationBuilder.DropTable(
                name: "employee_monitoring_overrides");

            migrationBuilder.DropTable(
                name: "employee_work_locations");

            migrationBuilder.DropTable(
                name: "employee_work_sessions");

            migrationBuilder.DropTable(
                name: "employment_statuses");

            migrationBuilder.DropTable(
                name: "employment_types");

            migrationBuilder.DropTable(
                name: "entity_assets");

            migrationBuilder.DropTable(
                name: "exceptions");

            migrationBuilder.DropTable(
                name: "external_calendar_connections");

            migrationBuilder.DropTable(
                name: "external_calendar_event_links");

            migrationBuilder.DropTable(
                name: "face_verification_attempts");

            migrationBuilder.DropTable(
                name: "feature_access_grants");

            migrationBuilder.DropTable(
                name: "file_upload_reservations");

            migrationBuilder.DropTable(
                name: "global_app_catalog");

            migrationBuilder.DropTable(
                name: "holiday_calendar_settings");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "inactivity_capture_attempts");

            migrationBuilder.DropTable(
                name: "inbox_decision_journal");

            migrationBuilder.DropTable(
                name: "invitation_tokens");

            migrationBuilder.DropTable(
                name: "job_leases");

            migrationBuilder.DropTable(
                name: "labels");

            migrationBuilder.DropTable(
                name: "leave_approval_delegates");

            migrationBuilder.DropTable(
                name: "leave_balance_audits");

            migrationBuilder.DropTable(
                name: "leave_entitlements");

            migrationBuilder.DropTable(
                name: "leave_policy_assignments");

            migrationBuilder.DropTable(
                name: "leave_policy_blackout_periods");

            migrationBuilder.DropTable(
                name: "leave_request_approvers");

            migrationBuilder.DropTable(
                name: "leave_request_day_allocations");

            migrationBuilder.DropTable(
                name: "leave_request_documents");

            migrationBuilder.DropTable(
                name: "legal_acceptance_records");

            migrationBuilder.DropTable(
                name: "legal_document_versions");

            migrationBuilder.DropTable(
                name: "legal_login_challenges");

            migrationBuilder.DropTable(
                name: "location_change_requests");

            migrationBuilder.DropTable(
                name: "login_workspace_selection_challenges");

            migrationBuilder.DropTable(
                name: "management_coverage_records");

            migrationBuilder.DropTable(
                name: "meeting_action_items");

            migrationBuilder.DropTable(
                name: "meeting_occurrence_attendees");

            migrationBuilder.DropTable(
                name: "meeting_occurrence_suppressions");

            migrationBuilder.DropTable(
                name: "meeting_series_links");

            migrationBuilder.DropTable(
                name: "meeting_signals");

            migrationBuilder.DropTable(
                name: "mfa_challenges");

            migrationBuilder.DropTable(
                name: "module_catalog_price_history");

            migrationBuilder.DropTable(
                name: "module_features");

            migrationBuilder.DropTable(
                name: "module_integration_links");

            migrationBuilder.DropTable(
                name: "module_permission_ownership");

            migrationBuilder.DropTable(
                name: "monitoring_feature_toggles");

            migrationBuilder.DropTable(
                name: "monitoring_notifications");

            migrationBuilder.DropTable(
                name: "monitoring_permission_consents");

            migrationBuilder.DropTable(
                name: "monitoring_policy_overrides");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_template_versions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "offboarding_task_bypass_requests");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "payment_gateway_country_routes");

            migrationBuilder.DropTable(
                name: "payment_gateway_credentials");

            migrationBuilder.DropTable(
                name: "personal_calendar_events");

            migrationBuilder.DropTable(
                name: "planned_task_blocks");

            migrationBuilder.DropTable(
                name: "platform_auth_events");

            migrationBuilder.DropTable(
                name: "platform_oauth_app_credentials");

            migrationBuilder.DropTable(
                name: "platform_role_permissions");

            migrationBuilder.DropTable(
                name: "platform_service_keys");

            migrationBuilder.DropTable(
                name: "platform_user_credentials");

            migrationBuilder.DropTable(
                name: "platform_user_invites");

            migrationBuilder.DropTable(
                name: "platform_user_roles");

            migrationBuilder.DropTable(
                name: "platform_user_sessions");

            migrationBuilder.DropTable(
                name: "position_assignments");

            migrationBuilder.DropTable(
                name: "position_reporting_history");

            migrationBuilder.DropTable(
                name: "project_categories");

            migrationBuilder.DropTable(
                name: "project_member_invitations");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "release_calendar");

            migrationBuilder.DropTable(
                name: "reporting_manager_changes");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "seat_increase_requests");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "severities");

            migrationBuilder.DropTable(
                name: "sprint_activity_logs");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "task_assignments");

            migrationBuilder.DropTable(
                name: "task_comment_logs");

            migrationBuilder.DropTable(
                name: "task_comment_reactions");

            migrationBuilder.DropTable(
                name: "task_drafts");

            migrationBuilder.DropTable(
                name: "task_edit_logs");

            migrationBuilder.DropTable(
                name: "task_percentage_logs");

            migrationBuilder.DropTable(
                name: "task_status_change_logs");

            migrationBuilder.DropTable(
                name: "tenant_auth_policies");

            migrationBuilder.DropTable(
                name: "tenant_configuration_template_applications");

            migrationBuilder.DropTable(
                name: "tenant_integration_credentials");

            migrationBuilder.DropTable(
                name: "tenant_notification_rules");

            migrationBuilder.DropTable(
                name: "tenant_one_time_charges");

            migrationBuilder.DropTable(
                name: "tenant_provisioning_states");

            migrationBuilder.DropTable(
                name: "tenant_session_exchange_challenges");

            migrationBuilder.DropTable(
                name: "tenant_setup_selections");

            migrationBuilder.DropTable(
                name: "tenant_status_histories");

            migrationBuilder.DropTable(
                name: "tenant_storage_stats");

            migrationBuilder.DropTable(
                name: "tray_activation_codes");

            migrationBuilder.DropTable(
                name: "tray_app_releases");

            migrationBuilder.DropTable(
                name: "tray_device_authorizations");

            migrationBuilder.DropTable(
                name: "tray_device_refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_external_identities");

            migrationBuilder.DropTable(
                name: "user_integration_connections");

            migrationBuilder.DropTable(
                name: "user_mfa");

            migrationBuilder.DropTable(
                name: "user_permission_overrides");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "wm_approval_requests");

            migrationBuilder.DropTable(
                name: "wm_module_hour_summaries");

            migrationBuilder.DropTable(
                name: "wm_monitor_alerts");

            migrationBuilder.DropTable(
                name: "wm_notification_log");

            migrationBuilder.DropTable(
                name: "wm_task_rate_settings");

            migrationBuilder.DropTable(
                name: "work_area_change_requests");

            migrationBuilder.DropTable(
                name: "position_access_templates");

            migrationBuilder.DropTable(
                name: "attendance_records");

            migrationBuilder.DropTable(
                name: "presence_sessions");

            migrationBuilder.DropTable(
                name: "subscription_invoices");

            migrationBuilder.DropTable(
                name: "bulk_onboarding_batches");

            migrationBuilder.DropTable(
                name: "meeting_attendance_sessions");

            migrationBuilder.DropTable(
                name: "calendar_events");

            migrationBuilder.DropTable(
                name: "clock_in_policies");

            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropTable(
                name: "dashboard_template_widgets");

            migrationBuilder.DropTable(
                name: "dashboard_widgets");

            migrationBuilder.DropTable(
                name: "monitoring_face_scans");

            migrationBuilder.DropTable(
                name: "monitoring_evidence_assets");

            migrationBuilder.DropTable(
                name: "leave_policy_versions");

            migrationBuilder.DropTable(
                name: "leave_requests");

            migrationBuilder.DropTable(
                name: "module_catalog");

            migrationBuilder.DropTable(
                name: "employee_checklist_tasks");

            migrationBuilder.DropTable(
                name: "payment_gateway_configs");

            migrationBuilder.DropTable(
                name: "platform_permissions");

            migrationBuilder.DropTable(
                name: "platform_roles");

            migrationBuilder.DropTable(
                name: "versions");

            migrationBuilder.DropTable(
                name: "onboarding_drafts");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "task_clocking_sessions");

            migrationBuilder.DropTable(
                name: "configuration_templates");

            migrationBuilder.DropTable(
                name: "notification_templates");

            migrationBuilder.DropTable(
                name: "integration_catalog");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "tenant_subscriptions");

            migrationBuilder.DropTable(
                name: "meeting_details");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "dashboard_templates");

            migrationBuilder.DropTable(
                name: "user_dashboards");

            migrationBuilder.DropTable(
                name: "activity_snapshots");

            migrationBuilder.DropTable(
                name: "agent_commands");

            migrationBuilder.DropTable(
                name: "file_records");

            migrationBuilder.DropTable(
                name: "leave_policies");

            migrationBuilder.DropTable(
                name: "offboarding_records");

            migrationBuilder.DropTable(
                name: "version_statuses");

            migrationBuilder.DropTable(
                name: "platform_oauth_apps");

            migrationBuilder.DropTable(
                name: "calendar_event_meetings");

            migrationBuilder.DropTable(
                name: "meeting_series");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "tray_device_registrations");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "leave_types");

            migrationBuilder.DropTable(
                name: "checklist_templates");

            migrationBuilder.DropTable(
                name: "platform_providers");

            migrationBuilder.DropTable(
                name: "platform_users");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "objectives");

            migrationBuilder.DropTable(
                name: "sprints");

            migrationBuilder.DropTable(
                name: "task_categories");

            migrationBuilder.DropTable(
                name: "task_statuses");

            migrationBuilder.DropTable(
                name: "role_templates");

            migrationBuilder.DropTable(
                name: "tenant_work_modes");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "legal_entities");
        }
    }
}
