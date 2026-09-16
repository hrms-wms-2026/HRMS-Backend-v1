using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Data-correction migration, not a schema change. Every tray ingestion handler (Activity/
    /// AppUsage/DeviceState/Meetings) wrote the tray JWT's raw UserId into each row's EmployeeId
    /// column ("Phase 1: tray JWT binds to UserId; EmployeeId column stores that identity until
    /// CoreHR employee master is always present for activated devices" - see
    /// IngestActivitySnapshotsCommandHandler's prior history), while every consumer of that column
    /// (AttendanceReadHandlers' Activity Daily Summary panel, ActivityDailySummaryJob,
    /// ExceptionDetectionJob, LocationRuleEvaluatorJob's employee lookup) treats it as a real
    /// CoreHR Employee.Id. The two values are never equal, so the Attendance History Detail
    /// screen's app-usage/daily-activity panel has never shown data for any real employee, and
    /// LocationRuleEvaluatorJob's `Employees.Id == employeeId` lookup has silently matched zero
    /// rows since it shipped. The ingestion-side fix (ITrayEmployeeIdentityResolver, resolving the
    /// real Employee.Id at write time going forward) is a separate code change; this migration
    /// repairs the rows written before that fix landed.
    ///
    /// Scope: only the four raw tray tables (activity_snapshots, app_usage_snapshots,
    /// device_state_snapshots, meeting_signals) and the tables purely derived from them
    /// (activity_daily_summary, exceptions) that ActivityDailySummaryJob/ExceptionDetectionJob
    /// fully recompute from those raw rows on their normal schedule. monitoring_notifications is
    /// deliberately NOT touched here: GetPendingTrayNotificationsQueryHandler/
    /// AckTrayNotificationCommandHandler still match tray-created notifications by raw UserId, and
    /// fixing that read path is a separate, independently-testable change - rewriting or deleting
    /// notifications now would make the tray app unable to see/ack its own pending notifications
    /// until that companion fix ships. Same reasoning excludes biometric profiles/enrollment
    /// attempts, monitoring_evidence_assets, inactivity_capture_attempts, and
    /// daily_work_location_confirmations, whose write-side handlers have not been fixed yet.
    ///
    /// Rewrite rule for the four raw tables: a row's employee_id is only rewritten when it matches
    /// some Employee's user_id AND does not already match any Employee's id. The second condition
    /// is collision safety (see ITrayEmployeeIdentityResolver's doc comment) - a User.Id and an
    /// unrelated Employee.Id could theoretically collide in the same tenant's Guid space, and a
    /// row that already holds a real Employee.Id must never be reassigned to that employee's own
    /// user_id-owner by mistake. Idempotent: a second run touches zero rows, since every row this
    /// migration fixes now satisfies "matches an Employee.Id" and is excluded by that same guard.
    ///
    /// activity_daily_summary/exceptions rows keyed by a stale (UserId) identity are deleted
    /// rather than rewritten in place: activity_daily_summary has a (tenant_id, employee_id, date)
    /// unique constraint, and a real Employee.Id could already have its own correctly-keyed row
    /// for the same date (e.g. from a manual RunAggregationAsync backfill run before this
    /// migration), which an in-place UPDATE would collide with. Deleting and letting the jobs
    /// recompute from the now-fixed raw tables is simpler and cannot violate that constraint.
    ///
    /// SET LOCAL app.tenant_context_mode = 'admin' is required around every statement below -
    /// migrations run as onevo_migrator (NOSUPERUSER NOBYPASSRLS,
    /// ops/postgres/local-bootstrap-roles.sql) and every table here is FORCE ROW LEVEL SECURITY
    /// (20260809192445_AddAppUsageDeviceStateRlsPolicies.cs, 20260817201841_
    /// AddExceptionsRlsPolicyCoverage.cs), so without it every statement silently matches zero
    /// rows - see BackfillRolePermissionAndUserRoleTenantId's migration comment for the empirical
    /// verification behind this requirement.
    ///
    /// Down() is deliberately a no-op: there is no correct UserId to restore employee_id to (that
    /// was the bug), and reverting the delete would require re-deriving deleted summary/exception
    /// rows from data this migration no longer has once it has run.
    /// </summary>
    public partial class BackfillTrayEmployeeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE activity_snapshots s
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = s.tenant_id
                  AND e.user_id = s.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = s.employee_id);

                UPDATE app_usage_snapshots s
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = s.tenant_id
                  AND e.user_id = s.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = s.employee_id);

                UPDATE device_state_snapshots s
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = s.tenant_id
                  AND e.user_id = s.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = s.employee_id);

                UPDATE meeting_signals s
                SET employee_id = e.id
                FROM employees e
                WHERE e.tenant_id = s.tenant_id
                  AND e.user_id = s.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = s.employee_id);

                DELETE FROM activity_daily_summary ads
                USING employees e
                WHERE e.tenant_id = ads.tenant_id
                  AND e.user_id = ads.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = ads.employee_id);

                DELETE FROM exceptions ex
                USING employees e
                WHERE e.tenant_id = ex.tenant_id
                  AND e.user_id = ex.employee_id
                  AND NOT EXISTS (SELECT 1 FROM employees e2 WHERE e2.id = ex.employee_id);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op - see the class-level comment for why this cannot be reverted.
        }
    }
}
