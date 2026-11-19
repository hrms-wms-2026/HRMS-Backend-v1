# Task 39b: Remaining foreign keys in core HR, leave, attendance and work

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

39 columns in the core modules are marked `task-39b` in `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt`. Task 39a left them out because each needs a decision first:

- `attendance_alert_deductions.alert_id`: `AttendanceAlertMaterializer` deletes an alert whose day no longer qualifies, but keeps the refunded deduction. Decide: soft-delete the alert, or make `alert_id` nullable with `SET NULL`.
- `leave_requests`, `leave_entitlements` and `leave_balance_audits` `.leave_policy_version_id`: `DeleteLeavePolicyVersion` and `AddLeavePolicyVersion` hard-delete a future version that a leave request may already use. Block that delete when the version is referenced, then add the keys.
- `positions.default_role_id`: roles are hard-deleted (`EfRoleRepository.Remove`) without checking positions. Block the role delete, or use `SET NULL`.
- Keys that point at tasks, sprints, task categories or task statuses (`task_edit_logs.edit_request_id`, the `creator_position_objective_id` columns, `wm_approval_requests.position_objective_id`): these parents are hard-deleted, so choose the behaviour per key.
- `employees.nationality_id`: find the table it points at.
- `leave_request_approvers.delegated_from_approver_id`, `offboarding_*` `approver_id` / `requested_by_id` / `initiated_by_id`, `task_assignments.assigned_by_id`, `task_drafts.owner_user_id`, `project_member_invitations.*`, `release_calendar.recipient_user_id`: find out whether each holds an employee id or a user id (finding C9 of the design review).
- `bulk_onboarding_*` resolved and default ids, `onboarding_drafts.*`, `reporting_manager_changes.batch_id`: plain references. Check the draft clean-up code for hard deletes.

## How to work

- The open columns are listed in `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt`. `SchemaIntegrityInventoryTests` fails until you delete the line of every column you fix.
- Add keys in `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`.
- Before each key, grep for hard deletes of the parent (`.Remove(`, `RemoveRange`, `ExecuteDelete`), and for writes of `Guid.Empty` or of a user id into an employee column.
- Use `NO ACTION` for people. `ForeignKeyRulesTests` rejects a cascade from `employees` or `users`.
- Create the migration with `dotnet ef migrations add` from an up-to-date branch. Do not apply it to a shared database. If orphan rows exist, the migration fails as a whole and deletes nothing.
- Run the full gate (`verify.ps1 <worktree> -Full`). Its Development boot test runs the demo seeders on top of the migration.

## Done when

- Every `task-39b` line is gone from the allow-list: the column has a key, or it moves to `polymorphic` or `external` with a reason.
