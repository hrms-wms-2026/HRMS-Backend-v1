# Task 39: Foreign keys and check constraints

| | |
|---|---|
| Priority | 39 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Security |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, sections C1, C2, C3 and C10.

- 542 GUID columns end in `_id` (not counting `id` and `tenant_id`). 229 have a foreign key and 313 do not. Worst tables: `invitation_tokens` (9), `wm_approval_requests` (8), `attendance_alerts` (6), `attendance_alert_deductions` (6), `bulk_onboarding_batch_rows` (6), `wm_notification_log` (6), `employees` (5), `notification_deliveries` (5), `onboarding_drafts` (5).
- 174 tables have `tenant_id`. Only 24 have a foreign key to `tenants`.
- 127 text columns are named like status, type, kind or state. None has a check constraint. A typo or an old value written by a script is stored without complaint.
- 51 foreign keys use `ON DELETE CASCADE`.

Without these, a row can point at an employee, role or position that no longer exists, and the application finds out when a screen breaks.

## What to add and change

1. Sort the 313 columns into three groups and record the list in the pull request:
   - real references to one table (for example `reviewed_by_id`, `assigned_by_id`, `legal_entity_id`, `role_id`): add the foreign key;
   - polymorphic references (`target_id`, `related_entity_id`, `subject_id`, `scope_id`, `source_id`): no foreign key is possible, so make sure the type column next to it has a check constraint;
   - external ids that are not rows in this database: leave and rename if the name misleads.
2. Before each foreign key, find orphan rows with a `LEFT JOIN ... WHERE parent.id IS NULL` query. Fix or report them. Add the key `WITH CHECK` so it is trusted.
3. Use `ON DELETE NO ACTION` for references to people (employees, users). Never cascade-delete history because a person was removed.
4. Review the 51 cascade deletes. Keep cascade only for true parts of a parent (line items, layout rows). Write the decision per key.
5. `tenant_id`: add the foreign key to `tenants` on the tables where it is missing. Do it in batches of about 30 tables per migration to keep each migration short.
6. Status columns: for each one, take the allowed values from the constants or enum in code and add `CHECK (status IN (...))`. Start with the workflow tables listed in task 37 and with `tasks`, `leave_requests`, `attendance_records`, `outbox_messages`.
7. Add an architecture test: a new column ending in `_id` must have a foreign key or be on an allow-list with a reason.
8. Every foreign key column needs an index that starts with it. Today only one is missing; keep it that way.

## Done when

- The count of `_id` columns without a foreign key is under 100, and every remaining one is on the allow-list with its reason.
- Every `tenant_id` has a foreign key.
- The workflow tables and the four named tables have status check constraints.
- Orphan rows found are reported.
- Migrations are created and reported, not applied to a shared database.

## Results

Split into 39a (this file, done 2026-11-19) and follow-ups 39b to 39g. Report: `docs/reports/database/2026-11-19-task-39a-foreign-keys-and-checks.md`.

Task 39a:

- Inventory: `tests/ONEVO.Tests.Architecture/Schema/`. `SchemaIntegrityInventoryTests` reads the EF model and fails on a new GUID `_id` column without a foreign key, or a new status/type column without a check constraint, unless it is on the allow-list (`missing-foreign-keys.txt`, `missing-check-constraints.txt`) with a group and reason. Fixed columns must be removed, so the lists only shrink (item 7).
- 63 foreign keys added (core HR, leave, attendance, work management), all NO ACTION, in `Persistence/Integrity/ReferenceForeignKeys.cs`. Each is `WITH CHECK` (trusted).
- 21 check constraints added in `Persistence/Integrity/StatusCheckConstraints.cs`: the 8 enum-backed status columns, `leave_requests.status`, `attendance_records.status`, `outbox_messages.status`, and 10 more in core HR, leave, attendance and sprints. Values come from the C# enum or constants; `StatusCheckConstraintTests` fails if code gains a value the migrations do not allow. `tasks` has no status text column (it uses `status_id`, a key to `task_statuses`).
- 7 cascades from `employees` changed to NO ACTION (`daily_work_location_confirmations`, `employee_addresses`, `employee_bank_details`, `employee_dependents`, `employee_emergency_contacts`, `employee_work_locations`, `planned_task_blocks`). Employees are never hard-deleted, so behaviour does not change. `ForeignKeyRulesTests` now rejects any cascade from `employees` or `users` (item 3) and any foreign key without an index that starts with it (item 8).
- Migration: `20261119140707_AddReferenceForeignKeysAndStatusChecks39a`. Created, not applied to any shared database.
- Counts: GUID id columns without a key 477 → 414 (163 of them `tenant_id`); status columns without a check 124 → 103.

Not done yet (follow-up files in this folder): 39b remaining core keys that need a decision first, 39c `created_by_id`, 39d `tenant_id`, 39e other modules, 39f remaining check constraints (including the task 37 workflow tables), 39g cascade review.
