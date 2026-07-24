using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationAndVerificationFoundation : Migration
    {
        private static readonly string[] TenantTables =
        [
            "agent_work_location_evidence",
            "employee_remote_work_profiles",
            "remote_work_location_change_requests",
            "verification_evidence_assets",
            "verification_policies",
            "verification_records"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "captured_device_id",
                table: "verification_reference_photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_acceptance_record_id",
                table: "verification_reference_photos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_comment",
                table: "verification_reference_photos",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "captured_agent_id",
                table: "gdpr_consent_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_version",
                table: "gdpr_consent_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "agent_work_location_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    presence_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    public_ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    local_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    wifi_ssid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    wifi_bssid_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gateway_mac_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vpn_detected = table.Column<bool>(type: "boolean", nullable: false),
                    coarse_location_json = table.Column<string>(type: "jsonb", nullable: true),
                    match_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    matched_location_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    matched_location_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_work_location_evidence", x => x.id);
                    table.CheckConstraint("ck_agent_work_location_evidence_confidence", "confidence IN ('high', 'medium', 'low', 'unknown')");
                    table.CheckConstraint("ck_agent_work_location_evidence_match_status", "match_status IN ('matched', 'mismatch', 'unknown', 'not_evaluated')");
                    table.CheckConstraint("ck_agent_work_location_evidence_matched_source", "matched_location_source IS NULL OR matched_location_source IN ('company_office', 'remote_profile', 'none')");
                    table.ForeignKey(
                        name: "fk_agent_work_location_evidence_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_work_location_evidence_registered_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_photo_clock_in = table.Column<bool>(type: "boolean", nullable: false),
                    require_photo_clock_out = table.Column<bool>(type: "boolean", nullable: false),
                    camera_photo_verification_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    absence_photo_capture_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    photo_capture_context_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    match_threshold = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 80m),
                    reference_enrollment_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    block_monitoring_until_reference_approved = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_policies", x => x.id);
                    table.CheckConstraint("ck_verification_policies_context_scope", "photo_capture_context_scope IN ('remote_only', 'onsite_only', 'remote_and_onsite', 'disabled')");
                    table.CheckConstraint("ck_verification_policies_enrollment_mode", "reference_enrollment_mode IN ('manual_review', 'trusted_sso_auto_approve')");
                    table.CheckConstraint("ck_verification_policies_match_threshold", "match_threshold BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "verification_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    match_confidence = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    biometric_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_records", x => x.id);
                    table.CheckConstraint("ck_verification_records_confidence", "match_confidence IS NULL OR match_confidence BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_verification_records_method", "method IN ('photo', 'biometric', 'on_demand_photo')");
                    table.CheckConstraint("ck_verification_records_review_status", "review_status IS NULL OR review_status IN ('pending', 'confirmed_mismatch', 'dismissed_false_positive')");
                    table.CheckConstraint("ck_verification_records_status", "status IN ('pending_review', 'verified', 'failed', 'skipped', 'expired')");
                    table.CheckConstraint("ck_verification_records_trigger", "trigger IN ('on_demand', 'clock_in', 'clock_out', 'absence_detected', 'biometric_scan')");
                    table.ForeignKey(
                        name: "fk_verification_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_records_registered_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_records_users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_records_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_remote_work_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    public_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    wifi_ssid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    wifi_bssid_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gateway_mac_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vpn_detected = table.Column<bool>(type: "boolean", nullable: false),
                    coarse_location_json = table.Column<string>(type: "jsonb", nullable: true),
                    verification_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_remote_work_profiles", x => x.id);
                    table.CheckConstraint("ck_employee_remote_work_profiles_status", "status IN ('pending_capture', 'active', 'archived', 'rejected')");
                    table.ForeignKey(
                        name: "fk_employee_remote_work_profiles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_remote_work_profiles_users_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_remote_work_profiles_verification_records_verifica",
                        column: x => x.verification_record_id,
                        principalTable: "verification_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_evidence_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    presence_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attendance_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    biometric_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    biometric_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retention_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legal_hold_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_evidence_assets", x => x.id);
                    table.CheckConstraint("ck_verification_evidence_assets_evidence_type", "evidence_type IN ('identity_verification_photo', 'clock_in_photo', 'clock_out_photo', 'verification_failure_photo')");
                    table.CheckConstraint("ck_verification_evidence_assets_trigger_type", "trigger_type IN ('on_demand', 'clock_in', 'clock_out', 'absence_detected')");
                    table.ForeignKey(
                        name: "fk_verification_evidence_assets_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_evidence_assets_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_evidence_assets_registered_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "registered_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_evidence_assets_verification_records_verificat",
                        column: x => x.verification_record_id,
                        principalTable: "verification_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "remote_work_location_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    new_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remote_work_location_change_requests", x => x.id);
                    table.CheckConstraint("ck_remote_work_location_change_requests_status", "status IN ('pending', 'approved', 'rejected', 'captured', 'expired')");
                    table.ForeignKey(
                        name: "fk_remote_work_location_change_requests_employee_remote_work_p",
                        column: x => x.current_profile_id,
                        principalTable: "employee_remote_work_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_remote_work_location_change_requests_employee_remote_work_p1",
                        column: x => x.new_profile_id,
                        principalTable: "employee_remote_work_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_remote_work_location_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_remote_work_location_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_captured_device_id",
                table: "verification_reference_photos",
                column: "captured_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_employee_id",
                table: "verification_reference_photos",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_legal_acceptance_record_id",
                table: "verification_reference_photos",
                column: "legal_acceptance_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_photo_file_id",
                table: "verification_reference_photos",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_reviewed_by_id",
                table: "verification_reference_photos",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_reference_photos_tenant_id_employee_id_status",
                table: "verification_reference_photos",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_captured_agent_id",
                table: "gdpr_consent_records",
                column: "captured_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_agent_id",
                table: "agent_work_location_evidence",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_employee_id",
                table: "agent_work_location_evidence",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_tenant_id_employee_id_captured",
                table: "agent_work_location_evidence",
                columns: new[] { "tenant_id", "employee_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_tenant_id_match_status_capture",
                table: "agent_work_location_evidence",
                columns: new[] { "tenant_id", "match_status", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_work_location_evidence_tenant_id_presence_session_id",
                table: "agent_work_location_evidence",
                columns: new[] { "tenant_id", "presence_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_remote_work_profiles_approved_by_id",
                table: "employee_remote_work_profiles",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_remote_work_profiles_employee_id",
                table: "employee_remote_work_profiles",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_remote_work_profiles_tenant_id_employee_id",
                table: "employee_remote_work_profiles",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_employee_remote_work_profiles_tenant_id_employee_id_status",
                table: "employee_remote_work_profiles",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_remote_work_profiles_verification_record_id",
                table: "employee_remote_work_profiles",
                column: "verification_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_current_profile_id",
                table: "remote_work_location_change_requests",
                column: "current_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_employee_id",
                table: "remote_work_location_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_new_profile_id",
                table: "remote_work_location_change_requests",
                column: "new_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_reviewed_by_id",
                table: "remote_work_location_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_tenant_id_employee_id",
                table: "remote_work_location_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_remote_work_location_change_requests_tenant_id_employee_id_",
                table: "remote_work_location_change_requests",
                columns: new[] { "tenant_id", "employee_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_agent_id",
                table: "verification_evidence_assets",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_employee_id",
                table: "verification_evidence_assets",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_file_record_id",
                table: "verification_evidence_assets",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_tenant_id_employee_id_captured",
                table: "verification_evidence_assets",
                columns: new[] { "tenant_id", "employee_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_tenant_id_presence_session_id",
                table: "verification_evidence_assets",
                columns: new[] { "tenant_id", "presence_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_tenant_id_verification_record_",
                table: "verification_evidence_assets",
                columns: new[] { "tenant_id", "verification_record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_evidence_assets_verification_record_id",
                table: "verification_evidence_assets",
                column: "verification_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_policies_tenant_id",
                table: "verification_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_agent_id",
                table: "verification_records",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_employee_id",
                table: "verification_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_requested_by_id",
                table: "verification_records",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_reviewed_by_id",
                table: "verification_records",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_tenant_id_agent_id_created_at",
                table: "verification_records",
                columns: new[] { "tenant_id", "agent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_tenant_id_employee_id_verified_at",
                table: "verification_records",
                columns: new[] { "tenant_id", "employee_id", "verified_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_records_tenant_id_status_created_at",
                table: "verification_records",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_gdpr_consent_records_registered_agents_captured_agent_id",
                table: "gdpr_consent_records",
                column: "captured_agent_id",
                principalTable: "registered_agents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_reference_photos_employees_employee_id",
                table: "verification_reference_photos",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_reference_photos_file_records_photo_file_id",
                table: "verification_reference_photos",
                column: "photo_file_id",
                principalTable: "file_records",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_reference_photos_gdpr_consent_records_legal_ac",
                table: "verification_reference_photos",
                column: "legal_acceptance_record_id",
                principalTable: "gdpr_consent_records",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_reference_photos_registered_agents_captured_de",
                table: "verification_reference_photos",
                column: "captured_device_id",
                principalTable: "registered_agents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_verification_reference_photos_users_reviewed_by_id",
                table: "verification_reference_photos",
                column: "reviewed_by_id",
                principalTable: "users",
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
                name: "fk_gdpr_consent_records_registered_agents_captured_agent_id",
                table: "gdpr_consent_records");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_reference_photos_employees_employee_id",
                table: "verification_reference_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_reference_photos_file_records_photo_file_id",
                table: "verification_reference_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_reference_photos_gdpr_consent_records_legal_ac",
                table: "verification_reference_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_reference_photos_registered_agents_captured_de",
                table: "verification_reference_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_verification_reference_photos_users_reviewed_by_id",
                table: "verification_reference_photos");

            migrationBuilder.DropTable(
                name: "agent_work_location_evidence");

            migrationBuilder.DropTable(
                name: "remote_work_location_change_requests");

            migrationBuilder.DropTable(
                name: "verification_evidence_assets");

            migrationBuilder.DropTable(
                name: "verification_policies");

            migrationBuilder.DropTable(
                name: "employee_remote_work_profiles");

            migrationBuilder.DropTable(
                name: "verification_records");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_captured_device_id",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_employee_id",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_legal_acceptance_record_id",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_photo_file_id",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_reviewed_by_id",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_verification_reference_photos_tenant_id_employee_id_status",
                table: "verification_reference_photos");

            migrationBuilder.DropIndex(
                name: "ix_gdpr_consent_records_captured_agent_id",
                table: "gdpr_consent_records");

            migrationBuilder.DropColumn(
                name: "captured_device_id",
                table: "verification_reference_photos");

            migrationBuilder.DropColumn(
                name: "legal_acceptance_record_id",
                table: "verification_reference_photos");

            migrationBuilder.DropColumn(
                name: "review_comment",
                table: "verification_reference_photos");

            migrationBuilder.DropColumn(
                name: "captured_agent_id",
                table: "gdpr_consent_records");

            migrationBuilder.DropColumn(
                name: "notice_version",
                table: "gdpr_consent_records");
        }
    }
}
