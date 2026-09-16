using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Data-correction migration, not a schema change. Phase 2 of the tray identity fix -
    /// see BackfillTrayEmployeeIdentity (Phase 1)'s class comment for the root cause
    /// (ITrayEmployeeIdentityResolver, and every write-side handler wrote the raw tray-JWT UserId
    /// into EmployeeId until it was fixed). Phase 1 deliberately excluded the tables below because
    /// their write-side handlers had not been fixed yet; that companion code fix
    /// (CreateEnrollmentAttemptCommandHandler, CompleteEnrollmentAttemptCommandHandler,
    /// UploadFaceScanCommandHandler, SubmitPeriodicScreenshotCommandHandler,
    /// SubmitInactivityCaptureAttemptCommandHandler, CompleteAgentCommandHandler,
    /// ConfirmWorkLocationCommandHandler, GetPendingTrayNotificationsQueryHandler,
    /// AckTrayNotificationCommandHandler) ships alongside this migration, so it is now safe to
    /// repair the historical rows.
    ///
    /// Rewrite rule (identical to Phase 1): a row's employee_id is only rewritten when it matches
    /// some Employee's user_id AND does not already match any Employee's id - collision safety per
    /// ITrayEmployeeIdentityResolver's doc comment. Idempotent for the same reason Phase 1's is.
    ///
    /// employee_work_locations needs one extra guard Phase 1's raw/derived tables didn't: this
    /// table already had a second, correct writer before this fix shipped
    /// (SubmitCheckInCommandHandler's self-registering-work-mode path and
    /// LocationChangeRequestWorkflow both already resolve and store the real Employee.Id). An
    /// employee whose work mode changed from self-registering to daily-choice (or vice versa)
    /// could therefore already have a correctly-keyed row for the same (tenant_id, employee_id)
    /// that a straight UPDATE of the stale UserId-keyed row would collide with on the unique
    /// index. Where that collision would occur, the stale UserId-keyed row is deleted instead
    /// (it is a duplicate of the same "reference point" concept, and the correctly-keyed row is
    /// kept); where no collision exists, it is rewritten in place like every other table here.
    ///
    /// monitoring_notifications rows written by LocationRuleEvaluatorJob/WellnessRuleEvaluatorJob
    /// already read employee_id off device_state_snapshots/activity data, which Phase 1 already
    /// backfilled - so most existing rows are already correctly keyed. This still repairs any
    /// notification created before Phase 1's backfill ran, when that source data was itself still
    /// UserId-keyed.
    ///
    /// SET LOCAL app.tenant_context_mode = 'admin' is required around every statement below -
    /// same reasoning as BackfillTrayEmployeeIdentity (Phase 1) and BackfillRolePermissionAndUserRoleTenantId.
    ///
    /// Down() is deliberately a no-op: there is no correct UserId to restore employee_id to (that
    /// was the bug), and reverting the employee_work_locations delete would require re-deriving
    /// rows this migration no longer has once it has run.
    /// </summary>
    public partial class BackfillTrayEmployeeIdentityPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE biometric_profiles t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                UPDATE biometric_enrollment_attempts t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                UPDATE monitoring_evidence_assets t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                UPDATE inactivity_capture_attempts t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                UPDATE daily_work_location_confirmations t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                UPDATE monitoring_notifications t
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = t.tenant_id
                  AND e.user_id = t.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = t.employee_id);

                -- employee_work_locations: rewrite in place only when no correctly-keyed row
                -- already exists for that employee (see class comment).
                UPDATE employee_work_locations w
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = w.tenant_id
                  AND e.user_id = w.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = w.employee_id)
                  AND NOT EXISTS (
                      SELECT 1 FROM employee_work_locations w2
                      WHERE w2.tenant_id = w.tenant_id AND w2.employee_id = e.id
                  );

                -- employee_work_locations: a correctly-keyed row already exists for that employee
                -- (written by SubmitCheckInCommandHandler/LocationChangeRequestWorkflow) - the
                -- stale UserId-keyed row is a duplicate reference point; delete it rather than
                -- collide on the (tenant_id, employee_id) unique index.
                DELETE FROM employee_work_locations w
                USING employees e
                WHERE e.tenant_id = w.tenant_id
                  AND e.user_id = w.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = w.employee_id)
                  AND EXISTS (
                      SELECT 1 FROM employee_work_locations w2
                      WHERE w2.tenant_id = w.tenant_id AND w2.employee_id = e.id
                  );
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op - see the class-level comment for why this cannot be reverted.
        }
    }
}
