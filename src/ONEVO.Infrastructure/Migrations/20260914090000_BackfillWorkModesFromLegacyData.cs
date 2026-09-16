using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Task 14: one-time backfill of tenant_work_modes/employees.work_mode_id/
    /// attendance_records.expected_work_mode_id-name/monitoring_feature_toggles from the legacy
    /// ClockInPolicy/legacy_work_mode_id/expected_work_area data every prior task in this plan
    /// deliberately left in place ("hold the drop"). Pure data movement, no schema change - hand
    /// written rather than scaffolded by `dotnet ef migrations add`, per Task 14 Step 3.
    ///
    /// Does NOT touch work_area_change_requests: Task 6's own migration
    /// (ReworkWorkAreaChangeRequestToWorkModeIds) already dropped requested_work_area/
    /// current_expected_work_area, so there is no legacy source left to remap from for that
    /// table - legacy_work_area_label stays permanently unpopulated as a consequence.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260914090000_BackfillWorkModesFromLegacyData")]
    public partial class BackfillWorkModesFromLegacyData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. For every legal entity with an active full_company ClockInPolicy row, derive 3
            //    tenant_work_modes rows from its Onsite*/Remote*/Either* columns (real data, not
            //    defaults).
            migrationBuilder.Sql("""
                INSERT INTO tenant_work_modes (id, tenant_id, legal_entity_id, name, biometric_enabled,
                    web_enabled, tray_enabled, photo_required, is_system_seeded, display_order,
                    is_active, created_at, updated_at)
                SELECT gen_random_uuid(), p.tenant_id, p.legal_entity_id, 'Remote',
                    p.remote_biometric_enabled, p.remote_web_enabled, p.remote_tray_enabled,
                    p.remote_photo_required, false, 0, true, now(), now()
                FROM clock_in_policies p
                WHERE p.scope_type = 'full_company' AND p.is_active = true
                  AND NOT EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = p.legal_entity_id);

                INSERT INTO tenant_work_modes (id, tenant_id, legal_entity_id, name, biometric_enabled,
                    web_enabled, tray_enabled, photo_required, is_system_seeded, display_order,
                    is_active, created_at, updated_at)
                SELECT gen_random_uuid(), p.tenant_id, p.legal_entity_id, 'Hybrid',
                    p.either_biometric_enabled, p.either_web_enabled, p.either_tray_enabled,
                    p.either_photo_required, false, 1, true, now(), now()
                FROM clock_in_policies p
                WHERE p.scope_type = 'full_company' AND p.is_active = true
                  AND EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = p.legal_entity_id AND w.name = 'Remote')
                  AND NOT EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = p.legal_entity_id AND w.name = 'Hybrid');

                INSERT INTO tenant_work_modes (id, tenant_id, legal_entity_id, name, biometric_enabled,
                    web_enabled, tray_enabled, photo_required, is_system_seeded, display_order,
                    is_active, created_at, updated_at)
                SELECT gen_random_uuid(), p.tenant_id, p.legal_entity_id, 'Onsite',
                    p.onsite_biometric_enabled, p.onsite_web_enabled, p.onsite_tray_enabled,
                    p.onsite_photo_required, false, 2, true, now(), now()
                FROM clock_in_policies p
                WHERE p.scope_type = 'full_company' AND p.is_active = true
                  AND EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = p.legal_entity_id AND w.name = 'Hybrid')
                  AND NOT EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = p.legal_entity_id AND w.name = 'Onsite');

                -- 2. Every legal entity that still has zero tenant_work_modes rows (no ClockInPolicy
                --    row existed) gets Task 3's plain defaults - matches what a brand-new legal
                --    entity would get via WorkModeSeeder, applied retroactively.
                INSERT INTO tenant_work_modes (id, tenant_id, legal_entity_id, name, biometric_enabled,
                    web_enabled, tray_enabled, photo_required, is_system_seeded, display_order,
                    is_active, created_at, updated_at)
                SELECT gen_random_uuid(), le.tenant_id, le.id, mode_name.name,
                    false, true, false, false, true, mode_name.ord, true, now(), now()
                FROM legal_entities le
                CROSS JOIN (VALUES ('Remote', 0), ('Hybrid', 1), ('Onsite', 2)) AS mode_name(name, ord)
                WHERE NOT EXISTS (SELECT 1 FROM tenant_work_modes w WHERE w.legal_entity_id = le.id);

                -- 3. Migrate location settings onto Monitoring config. Update the legal-entity-scoped
                --    toggles row where one already exists (only OR the verification flag on, to
                --    avoid silently turning tracking OFF where it was already on); insert a fresh
                --    row where none exists yet, since most legal entities never had one.
                UPDATE monitoring_feature_toggles t
                SET allowed_radius_meters = p.allowed_radius_meters,
                    work_location_verification = (t.work_location_verification OR p.location_verification_required),
                    updated_at = now()
                FROM clock_in_policies p
                WHERE p.scope_type = 'full_company' AND p.is_active = true
                  AND t.legal_entity_id = p.legal_entity_id AND t.tenant_id = p.tenant_id;

                INSERT INTO monitoring_feature_toggles (
                    id, tenant_id, legal_entity_id, activity_monitoring, application_tracking,
                    document_tracking, communication_tracking, screenshot_capture,
                    auto_screenshot_capture, meeting_detection, device_tracking,
                    work_location_verification, identity_verification, biometric,
                    idle_threshold_minutes, allowed_radius_meters, created_at, updated_at)
                SELECT gen_random_uuid(), p.tenant_id, p.legal_entity_id, false, false, false, false,
                    false, false, false, false, p.location_verification_required, false, false,
                    NULL, p.allowed_radius_meters, now(), now()
                FROM clock_in_policies p
                WHERE p.scope_type = 'full_company' AND p.is_active = true
                  AND NOT EXISTS (
                      SELECT 1 FROM monitoring_feature_toggles t
                      WHERE t.tenant_id = p.tenant_id AND t.legal_entity_id = p.legal_entity_id);

                -- 4. Remap employees.legacy_work_mode_id (1=on_site, 2=remote, 3=hybrid) to the new
                --    per-legal-entity tenant_work_modes rows by name.
                UPDATE employees e
                SET work_mode_id = w.id
                FROM tenant_work_modes w
                WHERE w.legal_entity_id = e.legal_entity_id
                  AND w.name = CASE e.legacy_work_mode_id WHEN 1 THEN 'Onsite' WHEN 2 THEN 'Remote' WHEN 3 THEN 'Hybrid' END;

                -- 5. Remap attendance_records the same way; "field" has no WorkMode counterpart, so
                --    it falls back to a plain display string in expected_work_mode_name rather than
                --    a legacy-label column (this table has none).
                UPDATE attendance_records a
                SET expected_work_mode_id = w.id, expected_work_mode_name = w.name
                FROM tenant_work_modes w, employees e
                WHERE e.id = a.employee_id AND w.legal_entity_id = e.legal_entity_id
                  AND w.name = CASE a.expected_work_area WHEN 'onsite' THEN 'Onsite' WHEN 'remote' THEN 'Remote' WHEN 'either' THEN 'Hybrid' END;

                UPDATE attendance_records
                SET expected_work_mode_name = expected_work_area
                WHERE expected_work_area = 'field' AND expected_work_mode_id IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design - this migration only ever runs once, forward, against
            // production data. Down() intentionally throws rather than pretending to undo it.
            throw new NotSupportedException("This data migration is not reversible.");
        }
    }
}
