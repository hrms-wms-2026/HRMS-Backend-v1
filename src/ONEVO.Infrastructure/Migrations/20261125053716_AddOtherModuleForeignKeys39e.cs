using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOtherModuleForeignKeys39e : Migration
    {
        /// <summary>
        /// Hand-written (task 39e). Runs before the keys, in the admin tenant mode (RLS applies to sa): Guid.Empty becomes NULL,
        /// an id of the wrong identity table (users, platform_users, employees) becomes NULL on a nullable column,
        /// the Dapi demo tenant's seeded device ids get their missing tray_device_registrations row (same id),
        /// and any other value that is not a row of its parent stops the migration with 50039, naming each column.
        /// Nothing is deleted. The payment-gateway credential columns are left out (CredentialOwnershipCompletionArchitectureTests).
        /// </summary>
        public const string RepairOtherModuleReferencesSql = """
        SET NOCOUNT ON;
        EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
        DECLARE @n int, @wrongkind nvarchar(max) = N'', @unknown nvarchar(max) = N'';
        -- 1. Guid.Empty means no reference: NULL on the nullable columns.
        UPDATE audit_logs SET user_id = NULL WHERE user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE biometric_profiles SET left_reference_photo_file_id = NULL WHERE left_reference_photo_file_id = '00000000-0000-0000-0000-000000000000';
        UPDATE biometric_profiles SET reference_photo_file_id = NULL WHERE reference_photo_file_id = '00000000-0000-0000-0000-000000000000';
        UPDATE biometric_profiles SET right_reference_photo_file_id = NULL WHERE right_reference_photo_file_id = '00000000-0000-0000-0000-000000000000';
        UPDATE calendar_event_guests SET event_id = NULL WHERE event_id = '00000000-0000-0000-0000-000000000000';
        UPDATE calendar_event_meeting_attendances SET employee_id = NULL WHERE employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE calendar_event_meetings SET calendar_event_id = NULL WHERE calendar_event_id = '00000000-0000-0000-0000-000000000000';
        UPDATE calendar_event_participants SET event_id = NULL WHERE event_id = '00000000-0000-0000-0000-000000000000';
        UPDATE calendar_meeting_incidents SET exception_id = NULL WHERE exception_id = '00000000-0000-0000-0000-000000000000';
        UPDATE conversation_messages SET author_employee_id = NULL WHERE author_employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE conversation_messages SET deleted_by_employee_id = NULL WHERE deleted_by_employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE conversation_participants SET added_by_employee_id = NULL WHERE added_by_employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE conversation_participants SET removed_by_employee_id = NULL WHERE removed_by_employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE device_change_requests SET legal_entity_id = NULL WHERE legal_entity_id = '00000000-0000-0000-0000-000000000000';
        UPDATE exceptions SET acknowledged_by_id = NULL WHERE acknowledged_by_id = '00000000-0000-0000-0000-000000000000';
        UPDATE exceptions SET escalated_by_id = NULL WHERE escalated_by_id = '00000000-0000-0000-0000-000000000000';
        UPDATE exceptions SET resolved_by_id = NULL WHERE resolved_by_id = '00000000-0000-0000-0000-000000000000';
        UPDATE external_calendar_event_links SET calendar_event_id = NULL WHERE calendar_event_id = '00000000-0000-0000-0000-000000000000';
        UPDATE face_verification_attempts SET photo_file_id = NULL WHERE photo_file_id = '00000000-0000-0000-0000-000000000000';
        UPDATE holiday_calendar_settings SET updated_by_id = NULL WHERE updated_by_id = '00000000-0000-0000-0000-000000000000';
        UPDATE inactivity_capture_attempts SET work_session_id = NULL WHERE work_session_id = '00000000-0000-0000-0000-000000000000';
        UPDATE inbox_decision_journal SET reverted_by_journal_id = NULL WHERE reverted_by_journal_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET employee_id = NULL WHERE employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET legal_entity_id = NULL WHERE legal_entity_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET onboarding_draft_id = NULL WHERE onboarding_draft_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET position_assignment_id = NULL WHERE position_assignment_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET position_id = NULL WHERE position_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET revoked_by_id = NULL WHERE revoked_by_id = '00000000-0000-0000-0000-000000000000';
        UPDATE invitation_tokens SET role_id = NULL WHERE role_id = '00000000-0000-0000-0000-000000000000';
        UPDATE notification_deliveries SET notification_id = NULL WHERE notification_id = '00000000-0000-0000-0000-000000000000';
        UPDATE notification_deliveries SET template_version_id = NULL WHERE template_version_id = '00000000-0000-0000-0000-000000000000';
        UPDATE notification_template_versions SET created_by_user_id = NULL WHERE created_by_user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE notification_template_versions SET published_by_user_id = NULL WHERE published_by_user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE notification_template_versions SET updated_by_user_id = NULL WHERE updated_by_user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE sessions SET active_employee_id = NULL WHERE active_employee_id = '00000000-0000-0000-0000-000000000000';
        UPDATE tenant_notification_rules SET updated_by_user_id = NULL WHERE updated_by_user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE tenants SET subscription_plan_id = NULL WHERE subscription_plan_id = '00000000-0000-0000-0000-000000000000';
        UPDATE tray_device_authorizations SET approved_tenant_id = NULL WHERE approved_tenant_id = '00000000-0000-0000-0000-000000000000';
        UPDATE tray_device_authorizations SET approved_user_id = NULL WHERE approved_user_id = '00000000-0000-0000-0000-000000000000';
        UPDATE user_roles SET source_position_access_template_id = NULL WHERE source_position_access_template_id = '00000000-0000-0000-0000-000000000000';
        UPDATE user_roles SET source_position_id = NULL WHERE source_position_id = '00000000-0000-0000-0000-000000000000';
        UPDATE users SET suspended_by_id = NULL WHERE suspended_by_id = '00000000-0000-0000-0000-000000000000';
        -- 2. An id of the wrong identity table (a users, platform_users or employees id where the key points at another one):
        --    NULL on the nullable columns, counted per table.
        SELECT @n = COUNT(*) FROM audit_logs x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.user_id));
        IF @n > 0 BEGIN UPDATE x SET user_id = NULL FROM audit_logs x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.user_id)); SET @wrongkind = CONCAT(@wrongkind, N' audit_logs.user_id=', @n); END
        SELECT @n = COUNT(*) FROM calendar_event_meeting_attendances x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.employee_id));
        IF @n > 0 BEGIN UPDATE x SET employee_id = NULL FROM calendar_event_meeting_attendances x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' calendar_event_meeting_attendances.employee_id=', @n); END
        SELECT @n = COUNT(*) FROM conversation_messages x WHERE x.author_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.author_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.author_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.author_employee_id));
        IF @n > 0 BEGIN UPDATE x SET author_employee_id = NULL FROM conversation_messages x WHERE x.author_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.author_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.author_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.author_employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' conversation_messages.author_employee_id=', @n); END
        SELECT @n = COUNT(*) FROM conversation_messages x WHERE x.deleted_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.deleted_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.deleted_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.deleted_by_employee_id));
        IF @n > 0 BEGIN UPDATE x SET deleted_by_employee_id = NULL FROM conversation_messages x WHERE x.deleted_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.deleted_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.deleted_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.deleted_by_employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' conversation_messages.deleted_by_employee_id=', @n); END
        SELECT @n = COUNT(*) FROM conversation_participants x WHERE x.added_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.added_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.added_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.added_by_employee_id));
        IF @n > 0 BEGIN UPDATE x SET added_by_employee_id = NULL FROM conversation_participants x WHERE x.added_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.added_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.added_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.added_by_employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' conversation_participants.added_by_employee_id=', @n); END
        SELECT @n = COUNT(*) FROM conversation_participants x WHERE x.removed_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.removed_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.removed_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.removed_by_employee_id));
        IF @n > 0 BEGIN UPDATE x SET removed_by_employee_id = NULL FROM conversation_participants x WHERE x.removed_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.removed_by_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.removed_by_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.removed_by_employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' conversation_participants.removed_by_employee_id=', @n); END
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.acknowledged_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.acknowledged_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.acknowledged_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.acknowledged_by_id));
        IF @n > 0 BEGIN UPDATE x SET acknowledged_by_id = NULL FROM exceptions x WHERE x.acknowledged_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.acknowledged_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.acknowledged_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.acknowledged_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' exceptions.acknowledged_by_id=', @n); END
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.escalated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.escalated_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.escalated_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.escalated_by_id));
        IF @n > 0 BEGIN UPDATE x SET escalated_by_id = NULL FROM exceptions x WHERE x.escalated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.escalated_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.escalated_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.escalated_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' exceptions.escalated_by_id=', @n); END
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.resolved_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.resolved_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.resolved_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.resolved_by_id));
        IF @n > 0 BEGIN UPDATE x SET resolved_by_id = NULL FROM exceptions x WHERE x.resolved_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.resolved_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.resolved_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.resolved_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' exceptions.resolved_by_id=', @n); END
        SELECT @n = COUNT(*) FROM holiday_calendar_settings x WHERE x.updated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_id));
        IF @n > 0 BEGIN UPDATE x SET updated_by_id = NULL FROM holiday_calendar_settings x WHERE x.updated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' holiday_calendar_settings.updated_by_id=', @n); END
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.employee_id));
        IF @n > 0 BEGIN UPDATE x SET employee_id = NULL FROM invitation_tokens x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' invitation_tokens.employee_id=', @n); END
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.revoked_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.revoked_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.revoked_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.revoked_by_id));
        IF @n > 0 BEGIN UPDATE x SET revoked_by_id = NULL FROM invitation_tokens x WHERE x.revoked_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.revoked_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.revoked_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.revoked_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' invitation_tokens.revoked_by_id=', @n); END
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.created_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.created_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.created_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.created_by_user_id));
        IF @n > 0 BEGIN UPDATE x SET created_by_user_id = NULL FROM notification_template_versions x WHERE x.created_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.created_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.created_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.created_by_user_id)); SET @wrongkind = CONCAT(@wrongkind, N' notification_template_versions.created_by_user_id=', @n); END
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.published_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.published_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.published_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.published_by_user_id));
        IF @n > 0 BEGIN UPDATE x SET published_by_user_id = NULL FROM notification_template_versions x WHERE x.published_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.published_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.published_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.published_by_user_id)); SET @wrongkind = CONCAT(@wrongkind, N' notification_template_versions.published_by_user_id=', @n); END
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_user_id));
        IF @n > 0 BEGIN UPDATE x SET updated_by_user_id = NULL FROM notification_template_versions x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_user_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_user_id)); SET @wrongkind = CONCAT(@wrongkind, N' notification_template_versions.updated_by_user_id=', @n); END
        SELECT @n = COUNT(*) FROM sessions x WHERE x.active_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.active_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.active_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.active_employee_id));
        IF @n > 0 BEGIN UPDATE x SET active_employee_id = NULL FROM sessions x WHERE x.active_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.active_employee_id) AND (EXISTS (SELECT 1 FROM users p WHERE p.id = x.active_employee_id) OR EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.active_employee_id)); SET @wrongkind = CONCAT(@wrongkind, N' sessions.active_employee_id=', @n); END
        SELECT @n = COUNT(*) FROM tenant_notification_rules x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_user_id));
        IF @n > 0 BEGIN UPDATE x SET updated_by_user_id = NULL FROM tenant_notification_rules x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.updated_by_user_id)); SET @wrongkind = CONCAT(@wrongkind, N' tenant_notification_rules.updated_by_user_id=', @n); END
        SELECT @n = COUNT(*) FROM tray_device_authorizations x WHERE x.approved_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.approved_user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.approved_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.approved_user_id));
        IF @n > 0 BEGIN UPDATE x SET approved_user_id = NULL FROM tray_device_authorizations x WHERE x.approved_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.approved_user_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.approved_user_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.approved_user_id)); SET @wrongkind = CONCAT(@wrongkind, N' tray_device_authorizations.approved_user_id=', @n); END
        SELECT @n = COUNT(*) FROM users x WHERE x.suspended_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.suspended_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.suspended_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.suspended_by_id));
        IF @n > 0 BEGIN UPDATE x SET suspended_by_id = NULL FROM users x WHERE x.suspended_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.suspended_by_id) AND (EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.suspended_by_id) OR EXISTS (SELECT 1 FROM employees p WHERE p.id = x.suspended_by_id)); SET @wrongkind = CONCAT(@wrongkind, N' users.suspended_by_id=', @n); END
        -- 2b. The Dapi demo seeder (DapiHistorySeeder, tenant 6b0874ab-71db-401f-859f-bdd50c1317fb) wrote one device id per
        --     employee into the monitoring snapshots without its tray_device_registrations row. For that demo tenant only,
        --     add the missing row with the same id, for the employee's user (or the demo owner). Any other tenant's unknown
        --     device id still stops the migration in step 3.
        INSERT INTO tray_device_registrations (id, tenant_id, user_id, legal_entity_id, device_name, device_os, device_fingerprint, is_active, activated_at, last_seen_at, deactivated_at, created_at)
        SELECT d.agent_device_id, d.tenant_id, COALESCE(u.id, o.id), l.id, N'Demo laptop', N'Windows 11', CONCAT(N'dapi-demo:', LOWER(CONVERT(nchar(36), d.agent_device_id))), 1, d.first_seen, d.last_seen, NULL, d.first_seen
        FROM (SELECT s.tenant_id, s.agent_device_id, CONVERT(uniqueidentifier, MIN(CONVERT(nchar(36), s.employee_id))) AS employee_id, MIN(s.captured_at) AS first_seen, MAX(s.captured_at) AS last_seen
              FROM (SELECT tenant_id, agent_device_id, employee_id, captured_at FROM activity_snapshots UNION ALL SELECT tenant_id, agent_device_id, employee_id, captured_at FROM app_usage_snapshots UNION ALL SELECT tenant_id, agent_device_id, employee_id, captured_at FROM device_state_snapshots) s
              WHERE s.tenant_id = '6b0874ab-71db-401f-859f-bdd50c1317fb' AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = s.agent_device_id)
              GROUP BY s.tenant_id, s.agent_device_id) d
        LEFT JOIN employees e ON e.id = d.employee_id
        LEFT JOIN users u ON u.id = e.user_id
        LEFT JOIN users o ON o.id = 'cd49a0c2-e978-4055-b8be-7d46a3727e94'
        LEFT JOIN legal_entities l ON l.id = e.legal_entity_id
        WHERE COALESCE(u.id, o.id) IS NOT NULL;
        SET @n = @@ROWCOUNT;
        IF @n > 0 PRINT CONCAT(N'Task 39e: added ', @n, N' tray_device_registrations rows for the Dapi demo devices.');
        -- 3. Any other value that is not a row of the parent stops the migration. Nothing is deleted.
        SELECT @n = COUNT(*) FROM activity_daily_summary x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' activity_daily_summary.employee_id=', @n);
        SELECT @n = COUNT(*) FROM activity_raw_buffer x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' activity_raw_buffer.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM activity_snapshots x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' activity_snapshots.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM activity_snapshots x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' activity_snapshots.employee_id=', @n);
        SELECT @n = COUNT(*) FROM agent_commands x WHERE x.requested_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.requested_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' agent_commands.requested_by_id=', @n);
        SELECT @n = COUNT(*) FROM app_usage_snapshots x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' app_usage_snapshots.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM app_usage_snapshots x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' app_usage_snapshots.employee_id=', @n);
        SELECT @n = COUNT(*) FROM audit_logs x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' audit_logs.user_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_enrollment_attempts x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_enrollment_attempts.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_enrollment_attempts x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_enrollment_attempts.employee_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_profiles x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_profiles.employee_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_profiles x WHERE x.left_reference_photo_file_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM file_records p WHERE p.id = x.left_reference_photo_file_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_profiles.left_reference_photo_file_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_profiles x WHERE x.reference_photo_file_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM file_records p WHERE p.id = x.reference_photo_file_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_profiles.reference_photo_file_id=', @n);
        SELECT @n = COUNT(*) FROM biometric_profiles x WHERE x.right_reference_photo_file_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM file_records p WHERE p.id = x.right_reference_photo_file_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' biometric_profiles.right_reference_photo_file_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_guests x WHERE x.event_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM personal_calendar_events p WHERE p.id = x.event_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_guests.event_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_meeting_attendances x WHERE x.calendar_event_meeting_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM calendar_event_meetings p WHERE p.id = x.calendar_event_meeting_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_meeting_attendances.calendar_event_meeting_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_meeting_attendances x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_meeting_attendances.employee_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_meetings x WHERE x.calendar_event_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM personal_calendar_events p WHERE p.id = x.calendar_event_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_meetings.calendar_event_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_participant_rsvp_history x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_participant_rsvp_history.employee_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_participant_rsvp_history x WHERE x.participant_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM calendar_event_participants p WHERE p.id = x.participant_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_participant_rsvp_history.participant_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_participants x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_participants.employee_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_participants x WHERE x.event_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM personal_calendar_events p WHERE p.id = x.event_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_participants.event_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_event_reminders_sent x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_event_reminders_sent.employee_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_meeting_incidents x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_meeting_incidents.employee_id=', @n);
        SELECT @n = COUNT(*) FROM calendar_meeting_incidents x WHERE x.exception_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM exceptions p WHERE p.id = x.exception_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' calendar_meeting_incidents.exception_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_favorites x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_favorites.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_attachments x WHERE x.file_record_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM file_records p WHERE p.id = x.file_record_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_message_attachments.file_record_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_hides x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_message_hides.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_mentions x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_message_mentions.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_reactions x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_message_reactions.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_message_revisions x WHERE x.revised_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.revised_by_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_message_revisions.revised_by_employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_messages x WHERE x.author_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.author_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_messages.author_employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_messages x WHERE x.deleted_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.deleted_by_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_messages.deleted_by_employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_mutes x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_mutes.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_participants x WHERE x.added_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.added_by_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_participants.added_by_employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_participants x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_participants.employee_id=', @n);
        SELECT @n = COUNT(*) FROM conversation_participants x WHERE x.removed_by_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.removed_by_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' conversation_participants.removed_by_employee_id=', @n);
        SELECT @n = COUNT(*) FROM device_change_requests x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' device_change_requests.employee_id=', @n);
        SELECT @n = COUNT(*) FROM device_change_requests x WHERE x.legal_entity_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM legal_entities p WHERE p.id = x.legal_entity_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' device_change_requests.legal_entity_id=', @n);
        SELECT @n = COUNT(*) FROM device_state_snapshots x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' device_state_snapshots.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM device_state_snapshots x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' device_state_snapshots.employee_id=', @n);
        SELECT @n = COUNT(*) FROM employee_check_ins x WHERE x.device_registration_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.device_registration_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_check_ins.device_registration_id=', @n);
        SELECT @n = COUNT(*) FROM employee_check_ins x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_check_ins.user_id=', @n);
        SELECT @n = COUNT(*) FROM employee_monitoring_overrides x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_monitoring_overrides.employee_id=', @n);
        SELECT @n = COUNT(*) FROM employee_monitoring_overrides x WHERE x.set_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.set_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_monitoring_overrides.set_by_id=', @n);
        SELECT @n = COUNT(*) FROM employee_work_sessions x WHERE x.device_registration_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.device_registration_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_work_sessions.device_registration_id=', @n);
        SELECT @n = COUNT(*) FROM employee_work_sessions x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' employee_work_sessions.user_id=', @n);
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.acknowledged_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.acknowledged_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' exceptions.acknowledged_by_id=', @n);
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' exceptions.employee_id=', @n);
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.escalated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.escalated_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' exceptions.escalated_by_id=', @n);
        SELECT @n = COUNT(*) FROM exceptions x WHERE x.resolved_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.resolved_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' exceptions.resolved_by_id=', @n);
        SELECT @n = COUNT(*) FROM external_calendar_connections x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' external_calendar_connections.user_id=', @n);
        SELECT @n = COUNT(*) FROM external_calendar_event_links x WHERE x.calendar_event_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM personal_calendar_events p WHERE p.id = x.calendar_event_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' external_calendar_event_links.calendar_event_id=', @n);
        SELECT @n = COUNT(*) FROM external_calendar_event_links x WHERE x.external_calendar_connection_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM external_calendar_connections p WHERE p.id = x.external_calendar_connection_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' external_calendar_event_links.external_calendar_connection_id=', @n);
        SELECT @n = COUNT(*) FROM face_verification_attempts x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' face_verification_attempts.employee_id=', @n);
        SELECT @n = COUNT(*) FROM face_verification_attempts x WHERE x.photo_file_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM file_records p WHERE p.id = x.photo_file_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' face_verification_attempts.photo_file_id=', @n);
        SELECT @n = COUNT(*) FROM holiday_calendar_settings x WHERE x.legal_entity_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM legal_entities p WHERE p.id = x.legal_entity_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' holiday_calendar_settings.legal_entity_id=', @n);
        SELECT @n = COUNT(*) FROM holiday_calendar_settings x WHERE x.updated_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' holiday_calendar_settings.updated_by_id=', @n);
        SELECT @n = COUNT(*) FROM inactivity_capture_attempts x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' inactivity_capture_attempts.employee_id=', @n);
        SELECT @n = COUNT(*) FROM inactivity_capture_attempts x WHERE x.work_session_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employee_work_sessions p WHERE p.id = x.work_session_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' inactivity_capture_attempts.work_session_id=', @n);
        SELECT @n = COUNT(*) FROM inbox_decision_journal x WHERE x.actor_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.actor_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' inbox_decision_journal.actor_employee_id=', @n);
        SELECT @n = COUNT(*) FROM inbox_decision_journal x WHERE x.reverted_by_journal_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inbox_decision_journal p WHERE p.id = x.reverted_by_journal_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' inbox_decision_journal.reverted_by_journal_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.employee_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.legal_entity_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM legal_entities p WHERE p.id = x.legal_entity_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.legal_entity_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.onboarding_draft_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM onboarding_drafts p WHERE p.id = x.onboarding_draft_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.onboarding_draft_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.position_assignment_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM position_assignments p WHERE p.id = x.position_assignment_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.position_assignment_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.position_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM positions p WHERE p.id = x.position_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.position_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.revoked_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.revoked_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.revoked_by_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.role_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM roles p WHERE p.id = x.role_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.role_id=', @n);
        SELECT @n = COUNT(*) FROM invitation_tokens x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' invitation_tokens.user_id=', @n);
        SELECT @n = COUNT(*) FROM legal_acceptance_records x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' legal_acceptance_records.user_id=', @n);
        SELECT @n = COUNT(*) FROM meeting_signals x WHERE x.agent_device_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.agent_device_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' meeting_signals.agent_device_id=', @n);
        SELECT @n = COUNT(*) FROM meeting_signals x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' meeting_signals.employee_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_evidence_assets x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_evidence_assets.employee_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_face_scans x WHERE x.check_in_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employee_check_ins p WHERE p.id = x.check_in_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_face_scans.check_in_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_notifications x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_notifications.employee_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_permission_consents x WHERE x.device_registration_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.device_registration_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_permission_consents.device_registration_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_permission_consents x WHERE x.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_permission_consents.employee_id=', @n);
        SELECT @n = COUNT(*) FROM monitoring_policy_overrides x WHERE x.set_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.set_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' monitoring_policy_overrides.set_by_id=', @n);
        SELECT @n = COUNT(*) FROM notification_deliveries x WHERE x.notification_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM notifications p WHERE p.id = x.notification_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_deliveries.notification_id=', @n);
        SELECT @n = COUNT(*) FROM notification_deliveries x WHERE x.recipient_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.recipient_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_deliveries.recipient_user_id=', @n);
        SELECT @n = COUNT(*) FROM notification_deliveries x WHERE x.template_version_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM notification_template_versions p WHERE p.id = x.template_version_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_deliveries.template_version_id=', @n);
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.created_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.created_by_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_template_versions.created_by_user_id=', @n);
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.published_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.published_by_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_template_versions.published_by_user_id=', @n);
        SELECT @n = COUNT(*) FROM notification_template_versions x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM platform_users p WHERE p.id = x.updated_by_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notification_template_versions.updated_by_user_id=', @n);
        SELECT @n = COUNT(*) FROM notifications x WHERE x.recipient_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.recipient_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' notifications.recipient_user_id=', @n);
        SELECT @n = COUNT(*) FROM password_reset_tokens x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' password_reset_tokens.user_id=', @n);
        SELECT @n = COUNT(*) FROM refresh_tokens x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' refresh_tokens.user_id=', @n);
        SELECT @n = COUNT(*) FROM sessions x WHERE x.active_employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = x.active_employee_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' sessions.active_employee_id=', @n);
        SELECT @n = COUNT(*) FROM sessions x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' sessions.user_id=', @n);
        SELECT @n = COUNT(*) FROM tenant_notification_rules x WHERE x.updated_by_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.updated_by_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tenant_notification_rules.updated_by_user_id=', @n);
        SELECT @n = COUNT(*) FROM tenant_subscriptions x WHERE x.plan_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subscription_plans p WHERE p.id = x.plan_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tenant_subscriptions.plan_id=', @n);
        SELECT @n = COUNT(*) FROM tenants x WHERE x.subscription_plan_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM subscription_plans p WHERE p.id = x.subscription_plan_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tenants.subscription_plan_id=', @n);
        SELECT @n = COUNT(*) FROM tray_activation_codes x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_activation_codes.user_id=', @n);
        SELECT @n = COUNT(*) FROM tray_device_authorizations x WHERE x.approved_tenant_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tenants p WHERE p.id = x.approved_tenant_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_device_authorizations.approved_tenant_id=', @n);
        SELECT @n = COUNT(*) FROM tray_device_authorizations x WHERE x.approved_user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.approved_user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_device_authorizations.approved_user_id=', @n);
        SELECT @n = COUNT(*) FROM tray_device_refresh_tokens x WHERE x.device_registration_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM tray_device_registrations p WHERE p.id = x.device_registration_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_device_refresh_tokens.device_registration_id=', @n);
        SELECT @n = COUNT(*) FROM tray_device_refresh_tokens x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_device_refresh_tokens.user_id=', @n);
        SELECT @n = COUNT(*) FROM tray_device_registrations x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' tray_device_registrations.user_id=', @n);
        SELECT @n = COUNT(*) FROM user_dashboards x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_dashboards.user_id=', @n);
        SELECT @n = COUNT(*) FROM user_external_identities x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_external_identities.user_id=', @n);
        SELECT @n = COUNT(*) FROM user_mfa x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_mfa.user_id=', @n);
        SELECT @n = COUNT(*) FROM user_permission_overrides x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_permission_overrides.user_id=', @n);
        SELECT @n = COUNT(*) FROM user_roles x WHERE x.source_position_access_template_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM position_access_templates p WHERE p.id = x.source_position_access_template_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_roles.source_position_access_template_id=', @n);
        SELECT @n = COUNT(*) FROM user_roles x WHERE x.source_position_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM positions p WHERE p.id = x.source_position_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_roles.source_position_id=', @n);
        SELECT @n = COUNT(*) FROM user_roles x WHERE x.user_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.user_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' user_roles.user_id=', @n);
        SELECT @n = COUNT(*) FROM users x WHERE x.suspended_by_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM users p WHERE p.id = x.suspended_by_id);
        IF @n > 0 SET @unknown = CONCAT(@unknown, N' users.suspended_by_id=', @n);
        IF @wrongkind <> N'' PRINT CONCAT(N'Task 39e: set to NULL (an id of another identity table):', @wrongkind);
        EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
        IF @unknown <> N'' BEGIN DECLARE @msg nvarchar(2048) = LEFT(CONCAT(N'Task 39e: ids that are not a row of their parent table:', @unknown, N'. Fix or report them before applying the migration; nothing was deleted.'), 2048); THROW 50039, @msg, 1; END
        """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RepairOtherModuleReferencesSql);

            migrationBuilder.CreateIndex(
                name: "ix_users_suspended_by_id",
                table: "users",
                column: "suspended_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_source_position_id",
                table: "user_roles",
                column: "source_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_user_id",
                table: "user_permission_overrides",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_dashboards_user_id",
                table: "user_dashboards",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_user_id",
                table: "tray_device_registrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_refresh_tokens_user_id",
                table: "tray_device_refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_approved_tenant_id",
                table: "tray_device_authorizations",
                column: "approved_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_approved_user_id",
                table: "tray_device_authorizations",
                column: "approved_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_subscription_plan_id",
                table: "tenants",
                column: "subscription_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_plan_id",
                table: "tenant_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_rules_updated_by_user_id",
                table: "tenant_notification_rules",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_employee_id",
                table: "sessions",
                column: "active_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id",
                table: "notifications",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_created_by_user_id",
                table: "notification_template_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_published_by_user_id",
                table: "notification_template_versions",
                column: "published_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_updated_by_user_id",
                table: "notification_template_versions",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_notification_id",
                table: "notification_deliveries",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_recipient_user_id",
                table: "notification_deliveries",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_template_version_id",
                table: "notification_deliveries",
                column: "template_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_policy_overrides_set_by_id",
                table: "monitoring_policy_overrides",
                column: "set_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_permission_consents_device_registration_id",
                table: "monitoring_permission_consents",
                column: "device_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_permission_consents_employee_id",
                table: "monitoring_permission_consents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_notifications_employee_id",
                table: "monitoring_notifications",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_face_scans_check_in_id",
                table: "monitoring_face_scans",
                column: "check_in_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_evidence_assets_employee_id",
                table: "monitoring_evidence_assets",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_signals_agent_device_id",
                table: "meeting_signals",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_signals_employee_id",
                table: "meeting_signals",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_acceptance_records_user_id",
                table: "legal_acceptance_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_employee_id",
                table: "invitation_tokens",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_legal_entity_id",
                table: "invitation_tokens",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_onboarding_draft_id",
                table: "invitation_tokens",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_position_assignment_id",
                table: "invitation_tokens",
                column: "position_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_position_id",
                table: "invitation_tokens",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_revoked_by_id",
                table: "invitation_tokens",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_role_id",
                table: "invitation_tokens",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_user_id",
                table: "invitation_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_decision_journal_actor_employee_id",
                table: "inbox_decision_journal",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_decision_journal_reverted_by_journal_id",
                table: "inbox_decision_journal",
                column: "reverted_by_journal_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_employee_id",
                table: "inactivity_capture_attempts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactivity_capture_attempts_work_session_id",
                table: "inactivity_capture_attempts",
                column: "work_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_settings_legal_entity_id",
                table: "holiday_calendar_settings",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_settings_updated_by_id",
                table: "holiday_calendar_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_face_verification_attempts_employee_id",
                table: "face_verification_attempts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_face_verification_attempts_photo_file_id",
                table: "face_verification_attempts",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_calendar_event_id",
                table: "external_calendar_event_links",
                column: "calendar_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_connections_user_id",
                table: "external_calendar_connections",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_acknowledged_by_id",
                table: "exceptions",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_employee_id",
                table: "exceptions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_escalated_by_id",
                table: "exceptions",
                column: "escalated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_resolved_by_id",
                table: "exceptions",
                column: "resolved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_device_registration_id",
                table: "employee_work_sessions",
                column: "device_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_user_id",
                table: "employee_work_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_monitoring_overrides_employee_id",
                table: "employee_monitoring_overrides",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_monitoring_overrides_set_by_id",
                table: "employee_monitoring_overrides",
                column: "set_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_device_registration_id",
                table: "employee_check_ins",
                column: "device_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_user_id",
                table: "employee_check_ins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_agent_device_id",
                table: "device_state_snapshots",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_employee_id",
                table: "device_state_snapshots",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_employee_id",
                table: "device_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_legal_entity_id",
                table: "device_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_added_by_employee_id",
                table: "conversation_participants",
                column: "added_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_employee_id",
                table: "conversation_participants",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_removed_by_employee_id",
                table: "conversation_participants",
                column: "removed_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_author_employee_id",
                table: "conversation_messages",
                column: "author_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_deleted_by_employee_id",
                table: "conversation_messages",
                column: "deleted_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_revisions_revised_by_employee_id",
                table: "conversation_message_revisions",
                column: "revised_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_reactions_employee_id",
                table: "conversation_message_reactions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_mentions_employee_id",
                table: "conversation_message_mentions",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_hides_employee_id",
                table: "conversation_message_hides",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_attachments_file_record_id",
                table: "conversation_message_attachments",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_meeting_incidents_employee_id",
                table: "calendar_meeting_incidents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_meeting_incidents_exception_id",
                table: "calendar_meeting_incidents",
                column: "exception_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_reminders_sent_employee_id",
                table: "calendar_event_reminders_sent",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_employee_id",
                table: "calendar_event_participants",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participants_event_id",
                table: "calendar_event_participants",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participant_rsvp_history_employee_id",
                table: "calendar_event_participant_rsvp_history",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_participant_rsvp_history_participant_id",
                table: "calendar_event_participant_rsvp_history",
                column: "participant_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meeting_attendances_calendar_event_meeting_id",
                table: "calendar_event_meeting_attendances",
                column: "calendar_event_meeting_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_meeting_attendances_employee_id",
                table: "calendar_event_meeting_attendances",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_guests_event_id",
                table: "calendar_event_guests",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_profiles_employee_id",
                table: "biometric_profiles",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_profiles_left_reference_photo_file_id",
                table: "biometric_profiles",
                column: "left_reference_photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_profiles_reference_photo_file_id",
                table: "biometric_profiles",
                column: "reference_photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_profiles_right_reference_photo_file_id",
                table: "biometric_profiles",
                column: "right_reference_photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_enrollment_attempts_agent_device_id",
                table: "biometric_enrollment_attempts",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_biometric_enrollment_attempts_employee_id",
                table: "biometric_enrollment_attempts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_agent_device_id",
                table: "app_usage_snapshots",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_employee_id",
                table: "app_usage_snapshots",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_requested_by_id",
                table: "agent_commands",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_agent_device_id",
                table: "activity_snapshots",
                column: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_employee_id",
                table: "activity_snapshots",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_daily_summary_employee_id",
                table: "activity_daily_summary",
                column: "employee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_daily_summary_employees_employee_id",
                table: "activity_daily_summary",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_raw_buffer_tray_device_registrations_agent_device_id",
                table: "activity_raw_buffer",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_snapshots_employees_employee_id",
                table: "activity_snapshots",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_snapshots_tray_device_registrations_agent_device_id",
                table: "activity_snapshots",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_commands_users_requested_by_id",
                table: "agent_commands",
                column: "requested_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_app_usage_snapshots_employees_employee_id",
                table: "app_usage_snapshots",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_app_usage_snapshots_tray_device_registrations_agent_device_id",
                table: "app_usage_snapshots",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_users_user_id",
                table: "audit_logs",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_enrollment_attempts_employees_employee_id",
                table: "biometric_enrollment_attempts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_enrollment_attempts_tray_device_registrations_agent_device_id",
                table: "biometric_enrollment_attempts",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_profiles_employees_employee_id",
                table: "biometric_profiles",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_profiles_file_records_left_reference_photo_file_id",
                table: "biometric_profiles",
                column: "left_reference_photo_file_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_profiles_file_records_reference_photo_file_id",
                table: "biometric_profiles",
                column: "reference_photo_file_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_biometric_profiles_file_records_right_reference_photo_file_id",
                table: "biometric_profiles",
                column: "right_reference_photo_file_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_guests_personal_calendar_events_event_id",
                table: "calendar_event_guests",
                column: "event_id",
                principalTable: "personal_calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_meeting_attendances_calendar_event_meetings_calendar_event_meeting_id",
                table: "calendar_event_meeting_attendances",
                column: "calendar_event_meeting_id",
                principalTable: "calendar_event_meetings",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_meeting_attendances_employees_employee_id",
                table: "calendar_event_meeting_attendances",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_meetings_personal_calendar_events_calendar_event_id",
                table: "calendar_event_meetings",
                column: "calendar_event_id",
                principalTable: "personal_calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_calendar_event_participants_participant_id",
                table: "calendar_event_participant_rsvp_history",
                column: "participant_id",
                principalTable: "calendar_event_participants",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_employees_employee_id",
                table: "calendar_event_participant_rsvp_history",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participants_employees_employee_id",
                table: "calendar_event_participants",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_participants_personal_calendar_events_event_id",
                table: "calendar_event_participants",
                column: "event_id",
                principalTable: "personal_calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_event_reminders_sent_employees_employee_id",
                table: "calendar_event_reminders_sent",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_meeting_incidents_employees_employee_id",
                table: "calendar_meeting_incidents",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_meeting_incidents_exceptions_exception_id",
                table: "calendar_meeting_incidents",
                column: "exception_id",
                principalTable: "exceptions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_favorites_employees_employee_id",
                table: "conversation_favorites",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_attachments_file_records_file_record_id",
                table: "conversation_message_attachments",
                column: "file_record_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_hides_employees_employee_id",
                table: "conversation_message_hides",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_mentions_employees_employee_id",
                table: "conversation_message_mentions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_reactions_employees_employee_id",
                table: "conversation_message_reactions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_message_revisions_employees_revised_by_employee_id",
                table: "conversation_message_revisions",
                column: "revised_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_messages_employees_author_employee_id",
                table: "conversation_messages",
                column: "author_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_messages_employees_deleted_by_employee_id",
                table: "conversation_messages",
                column: "deleted_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_mutes_employees_employee_id",
                table: "conversation_mutes",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_employees_added_by_employee_id",
                table: "conversation_participants",
                column: "added_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_employees_employee_id",
                table: "conversation_participants",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_participants_employees_removed_by_employee_id",
                table: "conversation_participants",
                column: "removed_by_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_device_change_requests_employees_employee_id",
                table: "device_change_requests",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_device_change_requests_legal_entities_legal_entity_id",
                table: "device_change_requests",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_device_state_snapshots_employees_employee_id",
                table: "device_state_snapshots",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_device_state_snapshots_tray_device_registrations_agent_device_id",
                table: "device_state_snapshots",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_check_ins_tray_device_registrations_device_registration_id",
                table: "employee_check_ins",
                column: "device_registration_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_check_ins_users_user_id",
                table: "employee_check_ins",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_monitoring_overrides_employees_employee_id",
                table: "employee_monitoring_overrides",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_monitoring_overrides_users_set_by_id",
                table: "employee_monitoring_overrides",
                column: "set_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_work_sessions_tray_device_registrations_device_registration_id",
                table: "employee_work_sessions",
                column: "device_registration_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_work_sessions_users_user_id",
                table: "employee_work_sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_exceptions_employees_employee_id",
                table: "exceptions",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_exceptions_users_acknowledged_by_id",
                table: "exceptions",
                column: "acknowledged_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_exceptions_users_escalated_by_id",
                table: "exceptions",
                column: "escalated_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_exceptions_users_resolved_by_id",
                table: "exceptions",
                column: "resolved_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_external_calendar_connections_users_user_id",
                table: "external_calendar_connections",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_external_calendar_event_links_external_calendar_connections_external_calendar_connection_id",
                table: "external_calendar_event_links",
                column: "external_calendar_connection_id",
                principalTable: "external_calendar_connections",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_external_calendar_event_links_personal_calendar_events_calendar_event_id",
                table: "external_calendar_event_links",
                column: "calendar_event_id",
                principalTable: "personal_calendar_events",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_face_verification_attempts_employees_employee_id",
                table: "face_verification_attempts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_face_verification_attempts_file_records_photo_file_id",
                table: "face_verification_attempts",
                column: "photo_file_id",
                principalTable: "file_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_holiday_calendar_settings_legal_entities_legal_entity_id",
                table: "holiday_calendar_settings",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_holiday_calendar_settings_users_updated_by_id",
                table: "holiday_calendar_settings",
                column: "updated_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inactivity_capture_attempts_employee_work_sessions_work_session_id",
                table: "inactivity_capture_attempts",
                column: "work_session_id",
                principalTable: "employee_work_sessions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inactivity_capture_attempts_employees_employee_id",
                table: "inactivity_capture_attempts",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_decision_journal_employees_actor_employee_id",
                table: "inbox_decision_journal",
                column: "actor_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_decision_journal_inbox_decision_journal_reverted_by_journal_id",
                table: "inbox_decision_journal",
                column: "reverted_by_journal_id",
                principalTable: "inbox_decision_journal",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_employees_employee_id",
                table: "invitation_tokens",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_legal_entities_legal_entity_id",
                table: "invitation_tokens",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_onboarding_drafts_onboarding_draft_id",
                table: "invitation_tokens",
                column: "onboarding_draft_id",
                principalTable: "onboarding_drafts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_position_assignments_position_assignment_id",
                table: "invitation_tokens",
                column: "position_assignment_id",
                principalTable: "position_assignments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_positions_position_id",
                table: "invitation_tokens",
                column: "position_id",
                principalTable: "positions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_roles_role_id",
                table: "invitation_tokens",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_users_revoked_by_id",
                table: "invitation_tokens",
                column: "revoked_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_invitation_tokens_users_user_id",
                table: "invitation_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_legal_acceptance_records_users_user_id",
                table: "legal_acceptance_records",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_signals_employees_employee_id",
                table: "meeting_signals",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_signals_tray_device_registrations_agent_device_id",
                table: "meeting_signals",
                column: "agent_device_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_evidence_assets_employees_employee_id",
                table: "monitoring_evidence_assets",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_face_scans_employee_check_ins_check_in_id",
                table: "monitoring_face_scans",
                column: "check_in_id",
                principalTable: "employee_check_ins",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_notifications_employees_employee_id",
                table: "monitoring_notifications",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_permission_consents_employees_employee_id",
                table: "monitoring_permission_consents",
                column: "employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_permission_consents_tray_device_registrations_device_registration_id",
                table: "monitoring_permission_consents",
                column: "device_registration_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_policy_overrides_users_set_by_id",
                table: "monitoring_policy_overrides",
                column: "set_by_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_deliveries_notification_template_versions_template_version_id",
                table: "notification_deliveries",
                column: "template_version_id",
                principalTable: "notification_template_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_deliveries_notifications_notification_id",
                table: "notification_deliveries",
                column: "notification_id",
                principalTable: "notifications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_deliveries_users_recipient_user_id",
                table: "notification_deliveries",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_template_versions_platform_users_created_by_user_id",
                table: "notification_template_versions",
                column: "created_by_user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_template_versions_platform_users_published_by_user_id",
                table: "notification_template_versions",
                column: "published_by_user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notification_template_versions_platform_users_updated_by_user_id",
                table: "notification_template_versions",
                column: "updated_by_user_id",
                principalTable: "platform_users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_recipient_user_id",
                table: "notifications",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_password_reset_tokens_users_user_id",
                table: "password_reset_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_employees_active_employee_id",
                table: "sessions",
                column: "active_employee_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_notification_rules_users_updated_by_user_id",
                table: "tenant_notification_rules",
                column: "updated_by_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_subscriptions_subscription_plans_plan_id",
                table: "tenant_subscriptions",
                column: "plan_id",
                principalTable: "subscription_plans",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenants_subscription_plans_subscription_plan_id",
                table: "tenants",
                column: "subscription_plan_id",
                principalTable: "subscription_plans",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_activation_codes_users_user_id",
                table: "tray_activation_codes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_authorizations_tenants_approved_tenant_id",
                table: "tray_device_authorizations",
                column: "approved_tenant_id",
                principalTable: "tenants",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_authorizations_users_approved_user_id",
                table: "tray_device_authorizations",
                column: "approved_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_refresh_tokens_tray_device_registrations_device_registration_id",
                table: "tray_device_refresh_tokens",
                column: "device_registration_id",
                principalTable: "tray_device_registrations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_refresh_tokens_users_user_id",
                table: "tray_device_refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tray_device_registrations_users_user_id",
                table: "tray_device_registrations",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_dashboards_users_user_id",
                table: "user_dashboards",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_external_identities_users_user_id",
                table: "user_external_identities",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_mfa_users_user_id",
                table: "user_mfa",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_permission_overrides_users_user_id",
                table: "user_permission_overrides",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_roles_position_access_templates_source_position_access_template_id",
                table: "user_roles",
                column: "source_position_access_template_id",
                principalTable: "position_access_templates",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_roles_positions_source_position_id",
                table: "user_roles",
                column: "source_position_id",
                principalTable: "positions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_roles_users_user_id",
                table: "user_roles",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_users_suspended_by_id",
                table: "users",
                column: "suspended_by_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_daily_summary_employees_employee_id",
                table: "activity_daily_summary");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_raw_buffer_tray_device_registrations_agent_device_id",
                table: "activity_raw_buffer");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_snapshots_employees_employee_id",
                table: "activity_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_snapshots_tray_device_registrations_agent_device_id",
                table: "activity_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_agent_commands_users_requested_by_id",
                table: "agent_commands");

            migrationBuilder.DropForeignKey(
                name: "fk_app_usage_snapshots_employees_employee_id",
                table: "app_usage_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_app_usage_snapshots_tray_device_registrations_agent_device_id",
                table: "app_usage_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_logs_users_user_id",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_enrollment_attempts_employees_employee_id",
                table: "biometric_enrollment_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_enrollment_attempts_tray_device_registrations_agent_device_id",
                table: "biometric_enrollment_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_profiles_employees_employee_id",
                table: "biometric_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_profiles_file_records_left_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_profiles_file_records_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_biometric_profiles_file_records_right_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_guests_personal_calendar_events_event_id",
                table: "calendar_event_guests");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_meeting_attendances_calendar_event_meetings_calendar_event_meeting_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_meeting_attendances_employees_employee_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_meetings_personal_calendar_events_calendar_event_id",
                table: "calendar_event_meetings");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_calendar_event_participants_participant_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participant_rsvp_history_employees_employee_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participants_employees_employee_id",
                table: "calendar_event_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_participants_personal_calendar_events_event_id",
                table: "calendar_event_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_event_reminders_sent_employees_employee_id",
                table: "calendar_event_reminders_sent");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_meeting_incidents_employees_employee_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_calendar_meeting_incidents_exceptions_exception_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_favorites_employees_employee_id",
                table: "conversation_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_attachments_file_records_file_record_id",
                table: "conversation_message_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_hides_employees_employee_id",
                table: "conversation_message_hides");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_mentions_employees_employee_id",
                table: "conversation_message_mentions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_reactions_employees_employee_id",
                table: "conversation_message_reactions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_message_revisions_employees_revised_by_employee_id",
                table: "conversation_message_revisions");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_messages_employees_author_employee_id",
                table: "conversation_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_messages_employees_deleted_by_employee_id",
                table: "conversation_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_mutes_employees_employee_id",
                table: "conversation_mutes");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_employees_added_by_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_employees_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_conversation_participants_employees_removed_by_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_device_change_requests_employees_employee_id",
                table: "device_change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_device_change_requests_legal_entities_legal_entity_id",
                table: "device_change_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_device_state_snapshots_employees_employee_id",
                table: "device_state_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_device_state_snapshots_tray_device_registrations_agent_device_id",
                table: "device_state_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_check_ins_tray_device_registrations_device_registration_id",
                table: "employee_check_ins");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_check_ins_users_user_id",
                table: "employee_check_ins");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_monitoring_overrides_employees_employee_id",
                table: "employee_monitoring_overrides");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_monitoring_overrides_users_set_by_id",
                table: "employee_monitoring_overrides");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_work_sessions_tray_device_registrations_device_registration_id",
                table: "employee_work_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_work_sessions_users_user_id",
                table: "employee_work_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_exceptions_employees_employee_id",
                table: "exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_exceptions_users_acknowledged_by_id",
                table: "exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_exceptions_users_escalated_by_id",
                table: "exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_exceptions_users_resolved_by_id",
                table: "exceptions");

            migrationBuilder.DropForeignKey(
                name: "fk_external_calendar_connections_users_user_id",
                table: "external_calendar_connections");

            migrationBuilder.DropForeignKey(
                name: "fk_external_calendar_event_links_external_calendar_connections_external_calendar_connection_id",
                table: "external_calendar_event_links");

            migrationBuilder.DropForeignKey(
                name: "fk_external_calendar_event_links_personal_calendar_events_calendar_event_id",
                table: "external_calendar_event_links");

            migrationBuilder.DropForeignKey(
                name: "fk_face_verification_attempts_employees_employee_id",
                table: "face_verification_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_face_verification_attempts_file_records_photo_file_id",
                table: "face_verification_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_holiday_calendar_settings_legal_entities_legal_entity_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_holiday_calendar_settings_users_updated_by_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_inactivity_capture_attempts_employee_work_sessions_work_session_id",
                table: "inactivity_capture_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_inactivity_capture_attempts_employees_employee_id",
                table: "inactivity_capture_attempts");

            migrationBuilder.DropForeignKey(
                name: "fk_inbox_decision_journal_employees_actor_employee_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropForeignKey(
                name: "fk_inbox_decision_journal_inbox_decision_journal_reverted_by_journal_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_employees_employee_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_legal_entities_legal_entity_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_onboarding_drafts_onboarding_draft_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_position_assignments_position_assignment_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_positions_position_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_roles_role_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_users_revoked_by_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_invitation_tokens_users_user_id",
                table: "invitation_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_legal_acceptance_records_users_user_id",
                table: "legal_acceptance_records");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_signals_employees_employee_id",
                table: "meeting_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_signals_tray_device_registrations_agent_device_id",
                table: "meeting_signals");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_evidence_assets_employees_employee_id",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_face_scans_employee_check_ins_check_in_id",
                table: "monitoring_face_scans");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_notifications_employees_employee_id",
                table: "monitoring_notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_permission_consents_employees_employee_id",
                table: "monitoring_permission_consents");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_permission_consents_tray_device_registrations_device_registration_id",
                table: "monitoring_permission_consents");

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_policy_overrides_users_set_by_id",
                table: "monitoring_policy_overrides");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_deliveries_notification_template_versions_template_version_id",
                table: "notification_deliveries");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_deliveries_notifications_notification_id",
                table: "notification_deliveries");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_deliveries_users_recipient_user_id",
                table: "notification_deliveries");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_template_versions_platform_users_created_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_template_versions_platform_users_published_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_notification_template_versions_platform_users_updated_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_notifications_users_recipient_user_id",
                table: "notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_password_reset_tokens_users_user_id",
                table: "password_reset_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_sessions_employees_active_employee_id",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_notification_rules_users_updated_by_user_id",
                table: "tenant_notification_rules");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_subscriptions_subscription_plans_plan_id",
                table: "tenant_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_tenants_subscription_plans_subscription_plan_id",
                table: "tenants");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_activation_codes_users_user_id",
                table: "tray_activation_codes");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_authorizations_tenants_approved_tenant_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_authorizations_users_approved_user_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_refresh_tokens_tray_device_registrations_device_registration_id",
                table: "tray_device_refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_refresh_tokens_users_user_id",
                table: "tray_device_refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_tray_device_registrations_users_user_id",
                table: "tray_device_registrations");

            migrationBuilder.DropForeignKey(
                name: "fk_user_dashboards_users_user_id",
                table: "user_dashboards");

            migrationBuilder.DropForeignKey(
                name: "fk_user_external_identities_users_user_id",
                table: "user_external_identities");

            migrationBuilder.DropForeignKey(
                name: "fk_user_mfa_users_user_id",
                table: "user_mfa");

            migrationBuilder.DropForeignKey(
                name: "fk_user_permission_overrides_users_user_id",
                table: "user_permission_overrides");

            migrationBuilder.DropForeignKey(
                name: "fk_user_roles_position_access_templates_source_position_access_template_id",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_user_roles_positions_source_position_id",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_user_roles_users_user_id",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_users_users_suspended_by_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_suspended_by_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_user_roles_source_position_id",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "ix_user_permission_overrides_user_id",
                table: "user_permission_overrides");

            migrationBuilder.DropIndex(
                name: "ix_user_dashboards_user_id",
                table: "user_dashboards");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_registrations_user_id",
                table: "tray_device_registrations");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_refresh_tokens_user_id",
                table: "tray_device_refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_authorizations_approved_tenant_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_authorizations_approved_user_id",
                table: "tray_device_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_tenants_subscription_plan_id",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tenant_subscriptions_plan_id",
                table: "tenant_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_tenant_notification_rules_updated_by_user_id",
                table: "tenant_notification_rules");

            migrationBuilder.DropIndex(
                name: "ix_sessions_active_employee_id",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient_user_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notification_template_versions_created_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropIndex(
                name: "ix_notification_template_versions_published_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropIndex(
                name: "ix_notification_template_versions_updated_by_user_id",
                table: "notification_template_versions");

            migrationBuilder.DropIndex(
                name: "ix_notification_deliveries_notification_id",
                table: "notification_deliveries");

            migrationBuilder.DropIndex(
                name: "ix_notification_deliveries_recipient_user_id",
                table: "notification_deliveries");

            migrationBuilder.DropIndex(
                name: "ix_notification_deliveries_template_version_id",
                table: "notification_deliveries");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_policy_overrides_set_by_id",
                table: "monitoring_policy_overrides");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_permission_consents_device_registration_id",
                table: "monitoring_permission_consents");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_permission_consents_employee_id",
                table: "monitoring_permission_consents");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_notifications_employee_id",
                table: "monitoring_notifications");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_face_scans_check_in_id",
                table: "monitoring_face_scans");

            migrationBuilder.DropIndex(
                name: "ix_monitoring_evidence_assets_employee_id",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropIndex(
                name: "ix_meeting_signals_agent_device_id",
                table: "meeting_signals");

            migrationBuilder.DropIndex(
                name: "ix_meeting_signals_employee_id",
                table: "meeting_signals");

            migrationBuilder.DropIndex(
                name: "ix_legal_acceptance_records_user_id",
                table: "legal_acceptance_records");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_employee_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_legal_entity_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_onboarding_draft_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_position_assignment_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_position_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_revoked_by_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_role_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_invitation_tokens_user_id",
                table: "invitation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_inbox_decision_journal_actor_employee_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropIndex(
                name: "ix_inbox_decision_journal_reverted_by_journal_id",
                table: "inbox_decision_journal");

            migrationBuilder.DropIndex(
                name: "ix_inactivity_capture_attempts_employee_id",
                table: "inactivity_capture_attempts");

            migrationBuilder.DropIndex(
                name: "ix_inactivity_capture_attempts_work_session_id",
                table: "inactivity_capture_attempts");

            migrationBuilder.DropIndex(
                name: "ix_holiday_calendar_settings_legal_entity_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropIndex(
                name: "ix_holiday_calendar_settings_updated_by_id",
                table: "holiday_calendar_settings");

            migrationBuilder.DropIndex(
                name: "ix_face_verification_attempts_employee_id",
                table: "face_verification_attempts");

            migrationBuilder.DropIndex(
                name: "ix_face_verification_attempts_photo_file_id",
                table: "face_verification_attempts");

            migrationBuilder.DropIndex(
                name: "ix_external_calendar_event_links_calendar_event_id",
                table: "external_calendar_event_links");

            migrationBuilder.DropIndex(
                name: "ix_external_calendar_connections_user_id",
                table: "external_calendar_connections");

            migrationBuilder.DropIndex(
                name: "ix_exceptions_acknowledged_by_id",
                table: "exceptions");

            migrationBuilder.DropIndex(
                name: "ix_exceptions_employee_id",
                table: "exceptions");

            migrationBuilder.DropIndex(
                name: "ix_exceptions_escalated_by_id",
                table: "exceptions");

            migrationBuilder.DropIndex(
                name: "ix_exceptions_resolved_by_id",
                table: "exceptions");

            migrationBuilder.DropIndex(
                name: "ix_employee_work_sessions_device_registration_id",
                table: "employee_work_sessions");

            migrationBuilder.DropIndex(
                name: "ix_employee_work_sessions_user_id",
                table: "employee_work_sessions");

            migrationBuilder.DropIndex(
                name: "ix_employee_monitoring_overrides_employee_id",
                table: "employee_monitoring_overrides");

            migrationBuilder.DropIndex(
                name: "ix_employee_monitoring_overrides_set_by_id",
                table: "employee_monitoring_overrides");

            migrationBuilder.DropIndex(
                name: "ix_employee_check_ins_device_registration_id",
                table: "employee_check_ins");

            migrationBuilder.DropIndex(
                name: "ix_employee_check_ins_user_id",
                table: "employee_check_ins");

            migrationBuilder.DropIndex(
                name: "ix_device_state_snapshots_agent_device_id",
                table: "device_state_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_device_state_snapshots_employee_id",
                table: "device_state_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_device_change_requests_employee_id",
                table: "device_change_requests");

            migrationBuilder.DropIndex(
                name: "ix_device_change_requests_legal_entity_id",
                table: "device_change_requests");

            migrationBuilder.DropIndex(
                name: "ix_conversation_participants_added_by_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropIndex(
                name: "ix_conversation_participants_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropIndex(
                name: "ix_conversation_participants_removed_by_employee_id",
                table: "conversation_participants");

            migrationBuilder.DropIndex(
                name: "ix_conversation_messages_author_employee_id",
                table: "conversation_messages");

            migrationBuilder.DropIndex(
                name: "ix_conversation_messages_deleted_by_employee_id",
                table: "conversation_messages");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_revisions_revised_by_employee_id",
                table: "conversation_message_revisions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_reactions_employee_id",
                table: "conversation_message_reactions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_mentions_employee_id",
                table: "conversation_message_mentions");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_hides_employee_id",
                table: "conversation_message_hides");

            migrationBuilder.DropIndex(
                name: "ix_conversation_message_attachments_file_record_id",
                table: "conversation_message_attachments");

            migrationBuilder.DropIndex(
                name: "ix_calendar_meeting_incidents_employee_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropIndex(
                name: "ix_calendar_meeting_incidents_exception_id",
                table: "calendar_meeting_incidents");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_reminders_sent_employee_id",
                table: "calendar_event_reminders_sent");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participants_employee_id",
                table: "calendar_event_participants");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participants_event_id",
                table: "calendar_event_participants");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participant_rsvp_history_employee_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_participant_rsvp_history_participant_id",
                table: "calendar_event_participant_rsvp_history");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_meeting_attendances_calendar_event_meeting_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_meeting_attendances_employee_id",
                table: "calendar_event_meeting_attendances");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_guests_event_id",
                table: "calendar_event_guests");

            migrationBuilder.DropIndex(
                name: "ix_biometric_profiles_employee_id",
                table: "biometric_profiles");

            migrationBuilder.DropIndex(
                name: "ix_biometric_profiles_left_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropIndex(
                name: "ix_biometric_profiles_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropIndex(
                name: "ix_biometric_profiles_right_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropIndex(
                name: "ix_biometric_enrollment_attempts_agent_device_id",
                table: "biometric_enrollment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_biometric_enrollment_attempts_employee_id",
                table: "biometric_enrollment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_app_usage_snapshots_agent_device_id",
                table: "app_usage_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_app_usage_snapshots_employee_id",
                table: "app_usage_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_agent_commands_requested_by_id",
                table: "agent_commands");

            migrationBuilder.DropIndex(
                name: "ix_activity_snapshots_agent_device_id",
                table: "activity_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_activity_snapshots_employee_id",
                table: "activity_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_activity_daily_summary_employee_id",
                table: "activity_daily_summary");
        }
    }
}
