using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopeMonitoringDefaultsAndTrayProfilesToLegalEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_monitoring_feature_toggles_tenant",
                table: "monitoring_feature_toggles");

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "tray_device_registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_legal_entity_id",
                table: "tray_device_authorizations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "tray_activation_codes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "monitoring_feature_toggles",
                type: "uuid",
                nullable: true);

            // Legacy tray records did not carry company context. Backfill only identities with
            // exactly one active Employee row; multi-company identities deliberately remain null
            // and therefore fail closed until they activate from a selected company.
            migrationBuilder.Sql("""
                WITH unambiguous_employee AS (
                    SELECT employee.tenant_id, employee.user_id,
                           MAX(employee.legal_entity_id::text)::uuid AS legal_entity_id
                    FROM employees AS employee
                    JOIN employment_statuses AS status
                      ON status.id = employee.employment_status_id
                    WHERE status.code = 'active'
                      AND employee.legal_entity_id IS NOT NULL
                      AND employee.is_deleted = FALSE
                    GROUP BY employee.tenant_id, employee.user_id
                    HAVING COUNT(*) = 1
                )
                UPDATE tray_device_registrations AS registration
                SET legal_entity_id = employee.legal_entity_id
                FROM unambiguous_employee AS employee
                WHERE registration.tenant_id = employee.tenant_id
                  AND registration.user_id = employee.user_id
                  AND registration.legal_entity_id IS NULL;

                WITH unambiguous_employee AS (
                    SELECT employee.tenant_id, employee.user_id,
                           MAX(employee.legal_entity_id::text)::uuid AS legal_entity_id
                    FROM employees AS employee
                    JOIN employment_statuses AS status
                      ON status.id = employee.employment_status_id
                    WHERE status.code = 'active'
                      AND employee.legal_entity_id IS NOT NULL
                      AND employee.is_deleted = FALSE
                    GROUP BY employee.tenant_id, employee.user_id
                    HAVING COUNT(*) = 1
                )
                UPDATE tray_activation_codes AS code
                SET legal_entity_id = employee.legal_entity_id
                FROM unambiguous_employee AS employee
                WHERE code.tenant_id = employee.tenant_id
                  AND code.user_id = employee.user_id
                  AND code.legal_entity_id IS NULL;

                WITH unambiguous_employee AS (
                    SELECT employee.tenant_id, employee.user_id,
                           MAX(employee.legal_entity_id::text)::uuid AS legal_entity_id
                    FROM employees AS employee
                    JOIN employment_statuses AS status
                      ON status.id = employee.employment_status_id
                    WHERE status.code = 'active'
                      AND employee.legal_entity_id IS NOT NULL
                      AND employee.is_deleted = FALSE
                    GROUP BY employee.tenant_id, employee.user_id
                    HAVING COUNT(*) = 1
                )
                UPDATE tray_device_authorizations AS authz
                SET approved_legal_entity_id = employee.legal_entity_id
                FROM unambiguous_employee AS employee
                WHERE authz.approved_tenant_id = employee.tenant_id
                  AND authz.approved_user_id = employee.user_id
                  AND authz.approved_legal_entity_id IS NULL;
                """);

            // Preserve the original null-scoped row as an explicit tenant fallback, and copy it
            // to every currently-active legal entity. The NOT EXISTS guard makes the backfill
            // additive and protects any explicit legal-entity row if this SQL is replayed during
            // a controlled repair.
            migrationBuilder.Sql("""
                INSERT INTO monitoring_feature_toggles (
                    id, tenant_id, legal_entity_id, activity_monitoring,
                    application_tracking, document_tracking, communication_tracking,
                    screenshot_capture, auto_screenshot_capture, meeting_detection,
                    device_tracking, work_location_verification, identity_verification,
                    biometric, idle_threshold_minutes, created_at, updated_at)
                SELECT
                    gen_random_uuid(), fallback.tenant_id, legal_entity.id,
                    fallback.activity_monitoring, fallback.application_tracking,
                    fallback.document_tracking, fallback.communication_tracking,
                    fallback.screenshot_capture, fallback.auto_screenshot_capture,
                    fallback.meeting_detection, fallback.device_tracking,
                    fallback.work_location_verification, fallback.identity_verification,
                    fallback.biometric, fallback.idle_threshold_minutes,
                    fallback.created_at, fallback.updated_at
                FROM monitoring_feature_toggles AS fallback
                JOIN legal_entities AS legal_entity
                  ON legal_entity.tenant_id = fallback.tenant_id
                 AND legal_entity.is_active = TRUE
                WHERE fallback.legal_entity_id IS NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM monitoring_feature_toggles AS explicit_default
                      WHERE explicit_default.tenant_id = fallback.tenant_id
                        AND explicit_default.legal_entity_id = legal_entity.id);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_legal_entity_id",
                table: "tray_device_registrations",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_approved_legal_entity_id",
                table: "tray_device_authorizations",
                column: "approved_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_legal_entity_id",
                table: "tray_activation_codes",
                column: "legal_entity_id");

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
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_feature_toggles_legal_entities_legal_entity_id",
                table: "monitoring_feature_toggles",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tray_activation_codes_legal_entities_legal_entity_id",
                table: "tray_activation_codes",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_authorizations_legal_entities_approved_legal_en",
                table: "tray_device_authorizations",
                column: "approved_legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_registrations_legal_entities_legal_entity_id",
                table: "tray_device_registrations",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_feature_toggles_legal_entities_legal_entity_id",
                table: "monitoring_feature_toggles");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_activation_codes_legal_entities_legal_entity_id",
                table: "tray_activation_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_authorizations_legal_entities_approved_legal_en",
                table: "tray_device_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_registrations_legal_entities_legal_entity_id",
                table: "tray_device_registrations");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_registrations_legal_entity_id",
                table: "tray_device_registrations");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_authorizations_approved_legal_entity_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_tray_activation_codes_legal_entity_id",
                table: "tray_activation_codes");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_feature_toggles_legal_entity_id",
                table: "monitoring_feature_toggles");

            migrationBuilder.DropIndex(
                name: "ux_monitoring_feature_toggles_tenant_fallback",
                table: "monitoring_feature_toggles");

            migrationBuilder.DropIndex(
                name: "ux_monitoring_feature_toggles_tenant_legal_entity",
                table: "monitoring_feature_toggles");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "tray_device_registrations");

            migrationBuilder.DropColumn(
                name: "approved_legal_entity_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "tray_activation_codes");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "monitoring_feature_toggles");

            migrationBuilder.CreateIndex(
                name: "ux_monitoring_feature_toggles_tenant",
                table: "monitoring_feature_toggles",
                column: "tenant_id",
                unique: true);
        }
    }
}
