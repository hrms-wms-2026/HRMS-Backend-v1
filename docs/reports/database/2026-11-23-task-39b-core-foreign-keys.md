# Task 39b: remaining core foreign keys (2026-11-23)

Task file: `docs/Improvements/person-1-security-and-auth/task-39b Remaining core foreign keys.md`. Follows task 39a (`2026-11-19-task-39a-foreign-keys-and-checks.md`).

## What changed

| Item | Before | After |
|---|---|---|
| Columns tagged `task-39b` in `missing-foreign-keys.txt` | 43 | 0 |
| Foreign keys from `ReferenceForeignKeys` | 59 | 98 (39 new) |
| Allow-list lines in `missing-foreign-keys.txt` | 418 | 379 |

- **Migration**: `20261123045150_AddCoreForeignKeys39b`, generated with `dotnet ef migrations add`. It adds 39 foreign keys and the 37 indexes they need (two columns already had one). It changes no table or column and has no hand-written SQL.
- **Keys**: `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`, the sections marked "Task 39b".

### The 39 keys

| Child column | Parent |
|---|---|
| `employees.user_id` | `users` |
| `employee_hierarchy_closure.source_position_assignment_id` | `position_assignments` |
| `bulk_onboarding_batches.created_by_user_id` | `users` |
| `bulk_onboarding_batches.default_work_mode_id`, `bulk_onboarding_batch_rows.resolved_work_mode_id` | `work_modes` |
| `bulk_onboarding_batches.default_checklist_template_id`, `bulk_onboarding_batch_rows.resolved_template_id`, `onboarding_drafts.selected_template_id` | `checklist_templates` |
| `bulk_onboarding_batch_rows.resolved_department_id` | `departments` |
| `bulk_onboarding_batch_rows.resolved_position_id` | `positions` |
| `bulk_onboarding_batch_rows.resolved_reports_to_employee_id` | `employees` |
| `onboarding_drafts.avatar_file_id` | `file_records` |
| `onboarding_drafts.started_by_id`, `offboarding_records.initiated_by_id`, `offboarding_task_bypass_requests.requested_by_id`, `offboarding_task_bypass_requests.approver_id`, `position_reporting_history.created_by_user_id` | `users` |
| `positions.default_role_id` | `roles` |
| `leave_requests`, `leave_entitlements`, `leave_balance_audits` `.leave_policy_version_id` | `leave_policy_versions` |
| `leave_request_approvers.delegated_from_approver_id` | `employees` |
| `objectives`, `sprints`, `tasks` `.creator_position_objective_id`; `wm_approval_requests.position_objective_id` | `objectives` |
| `meeting_series.approval_request_id`, `task_edit_logs.edit_request_id` | `wm_approval_requests` |
| `meeting_action_items.created_by_employee_id`, `meeting_occurrence_suppressions.created_by_employee_id`, `calendar_event_activity_logs.performed_by_id`, `calendar_events.archived_by_id`, `project_member_invitations.invited_by_id`, `project_member_invitations.invited_employee_id`, `task_assignments.assigned_by_id` | `employees` |
| `release_calendar.recipient_user_id`, `task_assignments.user_id`, `task_drafts.owner_user_id` | `users` |
| `task_drafts.project_id` | `projects` |

Employee or user id: each column was checked against the code that writes it (finding C9 of the design review). The onboarding, offboarding and position-history columns hold `ICurrentUser.UserId`. `delegated_from_approver_id` holds an employee id (the leave inbox adds it to the conversation's people). `task_assignments.assigned_by_id` holds an employee id in the application. The demo seeders wrote the Dapi owner's user id there; they now write the employee id (5 seeders).

### Four columns without a key, with a reason

| Column | Group | Why |
|---|---|---|
| `attendance_alert_deductions.alert_id` | reference | `AttendanceAlertMaterializer` hard-deletes an alert whose day no longer qualifies and keeps the refunded deduction. The unique `(tenant_id, alert_id)` index rules out `SET NULL`. A key needs a product decision: soft-delete alerts instead. |
| `employees.nationality_id` | external | No nationalities table exists. The API stores the client's id unchecked. |
| `reporting_manager_changes.batch_id` | external | A group id that `ReportingLineMoveService` makes with `Guid.NewGuid()` for the changes of one move. There is no batch table. |
| `task_statuses.approver_id` | unused | A dead field (see `TaskStatus.RequiresApproval`): stored from the client unchecked and read by no logic. |

## Delete behaviour

All keys are `NO ACTION`. The one on `position_reporting_history.created_by_user_id` is `Restrict`, like that table's other keys; SQL Server emits the same DDL. There is no cascade, so no multiple cascade path. `ForeignKeyRulesTests` still rejects a cascade from `employees` or `users`.

Parents that the application hard-deletes were checked one by one (`.Remove(`, `RemoveRange`, `ExecuteDelete`, raw `DELETE`):

- `leave_policy_versions`: `DeleteLeavePolicyVersion`, and `AddLeavePolicyVersion` when it replaces a future version on the same date. Both now refuse when a leave request, entitlement or balance audit points at the version (`ILeavePolicyRepository.IsVersionInUseAsync`), instead of failing on the key. Two unit tests cover this.
- `roles`: `ArchiveRole` calls `Remove`, but the soft-delete interceptor turns it into an update, so the row stays.
- `attendance_alerts`: hard-deleted, so no key (see above).
- `objectives`, `wm_approval_requests`, `users`, `employees`, `checklist_templates`, `file_records`, `work_modes`, `departments`, `positions`, `position_assignments`, `projects`: no hard delete in the application.

## Bad data in the local database

**Not checked.** The orphan count against the local `CrownusDb` (localhost) was blocked: the auto-mode classifier refused the database connection, which needs the `sa` password from the repo-root `.env`. For the same reason the migration has **not** been applied to the local database. The UAT database was not touched.

Expect orphans in a local database seeded before this task: the demo seeders wrote user ids into `task_assignments.assigned_by_id`. The seeder fix only changes new rows, because the seeders skip rows that already exist. If orphans exist, the migration fails as a whole and changes nothing; no data is deleted.

To count them, run this read-only query as `sa` on the local database. It prints, for each column, the non-null rows, the orphans and up to 3 sample ids.

```sql
SET NOCOUNT ON;
EXEC sp_set_session_context N'app.current_tenant_id', N'';
EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
DECLARE @k TABLE (child sysname, col sysname, parent sysname);
INSERT @k VALUES
('employees','user_id','users'), ('employee_hierarchy_closure','source_position_assignment_id','position_assignments'),
('bulk_onboarding_batches','created_by_user_id','users'), ('bulk_onboarding_batches','default_work_mode_id','work_modes'),
('bulk_onboarding_batches','default_checklist_template_id','checklist_templates'),
('bulk_onboarding_batch_rows','resolved_department_id','departments'), ('bulk_onboarding_batch_rows','resolved_position_id','positions'),
('bulk_onboarding_batch_rows','resolved_reports_to_employee_id','employees'), ('bulk_onboarding_batch_rows','resolved_template_id','checklist_templates'),
('bulk_onboarding_batch_rows','resolved_work_mode_id','work_modes'), ('onboarding_drafts','avatar_file_id','file_records'),
('onboarding_drafts','selected_template_id','checklist_templates'), ('onboarding_drafts','started_by_id','users'),
('offboarding_records','initiated_by_id','users'), ('offboarding_task_bypass_requests','requested_by_id','users'),
('offboarding_task_bypass_requests','approver_id','users'), ('position_reporting_history','created_by_user_id','users'),
('positions','default_role_id','roles'), ('leave_requests','leave_policy_version_id','leave_policy_versions'),
('leave_entitlements','leave_policy_version_id','leave_policy_versions'), ('leave_balance_audits','leave_policy_version_id','leave_policy_versions'),
('leave_request_approvers','delegated_from_approver_id','employees'), ('objectives','creator_position_objective_id','objectives'),
('sprints','creator_position_objective_id','objectives'), ('tasks','creator_position_objective_id','objectives'),
('wm_approval_requests','position_objective_id','objectives'), ('meeting_series','approval_request_id','wm_approval_requests'),
('task_edit_logs','edit_request_id','wm_approval_requests'), ('meeting_action_items','created_by_employee_id','employees'),
('meeting_occurrence_suppressions','created_by_employee_id','employees'), ('calendar_event_activity_logs','performed_by_id','employees'),
('calendar_events','archived_by_id','employees'), ('project_member_invitations','invited_by_id','employees'),
('project_member_invitations','invited_employee_id','employees'), ('task_assignments','assigned_by_id','employees'),
('release_calendar','recipient_user_id','users'), ('task_assignments','user_id','users'),
('task_drafts','owner_user_id','users'), ('task_drafts','project_id','projects');
DECLARE @c sysname, @col sysname, @p sysname, @sql nvarchar(max);
DECLARE cur CURSOR FOR SELECT child, col, parent FROM @k;
OPEN cur; FETCH NEXT FROM cur INTO @c, @col, @p;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @sql = N'DECLARE @n int, @t int, @s nvarchar(400);
    SELECT @t = COUNT(*) FROM ' + QUOTENAME(@c) + N' WHERE ' + QUOTENAME(@col) + N' IS NOT NULL;
    SELECT @n = COUNT(*) FROM ' + QUOTENAME(@c) + N' c WHERE c.' + QUOTENAME(@col) + N' IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@p) + N' p WHERE p.id = c.' + QUOTENAME(@col) + N');
    SELECT @s = STRING_AGG(CAST(v AS nvarchar(36)), N'','') FROM (SELECT DISTINCT TOP 3 c.' + QUOTENAME(@col) + N' v FROM '
      + QUOTENAME(@c) + N' c WHERE c.' + QUOTENAME(@col) + N' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM '
      + QUOTENAME(@p) + N' p WHERE p.id = c.' + QUOTENAME(@col) + N')) x;
    PRINT CONCAT(''' + @c + N'.' + @col + N' -> ' + @p + N' | non-null '', @t, '' | orphans '', @n,
      CASE WHEN @n > 0 THEN '' | sample '' + @s ELSE '''' END);';
  EXEC sp_executesql @sql;
  FETCH NEXT FROM cur INTO @c, @col, @p;
END
CLOSE cur; DEALLOCATE cur;
```

Task 39b adds no check constraints, so there are no invalid values to count.

## Test fixtures

The integration fixtures wrote rows that point at nothing. They were fixed; no test was weakened:

- `TestUsers` (new, `tests/ONEVO.Tests.Integration/Support`) adds the users row behind a fixture employee or caller. `MyTeamDb.AddEmployee` uses it, and 18 other fixtures call `TestUsers.AddForNewEmployees` before saving.
- `MyTeamDb.ClosureSourceFor` gives hand-written closure rows a real source assignment (an existing one, or an ended one that no visibility rule counts).
- Task assignments copy the assignee's real user id (`MyTeamDb.UserIdOf`).
- `TaskDraftRepositoryIntegrationTests` seeds real owners and a real project.
- The bulk-onboarding tests' callers are real users.
- `EfPositionRepositoryTests` counts 3 keys on `position_reporting_history` (it was 2).

## Gate

Filled in below once the full gate has run.
