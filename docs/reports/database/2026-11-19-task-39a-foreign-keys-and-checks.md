# Task 39a: foreign keys and check constraints, first slice (2026-11-19)

Task: `docs/Improvements/person-1-security-and-auth/task-39 Foreign keys and check constraints.md`. Source finding: sections C1, C2, C3 and C10 of `2026-11-15-database-design-review.md`.

## What changed

| Item | Before | After |
|---|---|---|
| GUID `_id` columns without a foreign key (including `tenant_id`) | 477 | 414 |
| of which `tenant_id` | 163 | 163 (task 39d) |
| Status/type text columns without a check constraint | 124 | 103 |
| Cascade deletes from `employees` | 7 | 0 |

The review counted 313 id columns without a key; the EF model gives 314, because one column was added since.

- **Inventory and ratchet**: `tests/ONEVO.Tests.Architecture/Schema/SchemaIntegrityInventoryTests.cs` with two allow-lists. A new column without a key or constraint fails the architecture tests unless it is listed with a reason. To rewrite the lists from the model, run that class with `ONEVO_SCHEMA_INVENTORY_WRITE=1`.
- **63 foreign keys**, all `NO ACTION`: `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`. They cover core HR (11), leave (2), time and attendance (13) and work management (37). There are no navigation properties, so the domain model is unchanged. EF added 62 indexes, so every new key has an index that starts with it.
- **21 check constraints** (`col IN (...)`): `src/ONEVO.Infrastructure/Persistence/Integrity/StatusCheckConstraints.cs`. 8 of them cover enum-backed columns: `tenants.status`, `tenant_status_histories.from_status` and `to_status`, `exceptions.status` and `type`, `monitoring_notifications.type`, and `biometric_*.status`. The other 13 use constants classes: `leave_requests`, `attendance_records` and `outbox_messages` (named in task 39), plus leave approvers, day allocations, balance audit change types, attendance alerts and deductions, position assignments, onboarding drafts, bulk onboarding batches and rows, and sprints.
- **Enum sync test**: `StatusCheckConstraintTests.MigrationsAllowEveryValueTheCodeCanWrite` compares each constraint in the migration snapshot with the enum or constants. It fails when code gains a value that the migrations do not allow.
- **Delete behaviour**: 7 keys from `employees` changed from CASCADE to NO ACTION. `ForeignKeyRulesTests` guards the rule, and also checks that every foreign key has an index that starts with it.
- **Migration**: `20261119135816_AddReferenceForeignKeysAndStatusChecks39a`. It changes no tables or columns. It was created with `dotnet ef migrations add` and has not been applied to any shared database.

## Delete behaviour

Every new key is NO ACTION. This was checked against the code: none of the parents in this slice is hard-deleted by the application. Employees, projects, objectives, leave policies, leave requests and legal entities are soft-deleted or archived. NO ACTION also cannot create the multiple cascade paths that SQL Server rejects.

Keys left out on purpose, because the code hard-deletes the parent (details in task 39b):

- `attendance_alert_deductions.alert_id`: an alert is deleted, but its refunded deduction stays.
- `*.leave_policy_version_id`: a future policy version can be deleted while a leave request uses it.
- `positions.default_role_id`: roles are hard-deleted without checking positions.
- Keys to tasks, sprints, task categories and task statuses.

`created_by_id` (76 columns) is left out because the audit interceptor writes `Guid.Empty` for system writes (task 39c).

## Orphan rows

- If orphan rows or old status values exist, the migration fails as a whole: SQL Server rolls the migration's transaction back and no data is deleted or changed.
- **Fresh database and demo seed data**: the integration suite migrates fresh SQL Server databases and boots the API in Development, which runs every demo seeder on top of the migration. See the gate result below.
- **Local development database: not checked.** This session had no access to its connection settings. Before applying the migration locally, check every new key with a query like the one below. Run it with admin session context, because RLS hides rows from every login, `sa` included:

  ```sql
  EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
  SELECT COUNT(*) FROM attendance_records c
  WHERE c.employee_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM employees p WHERE p.id = c.employee_id);
  ```

  For a check constraint, count the rows where `status NOT IN (...)`. All 63 keys use tables whose RLS predicate allows admin mode, so the query sees every row.

## Gate

See the results section below.

## Follow-ups

These are the task files 39b to 39g in `docs/Improvements/person-1-security-and-auth/`:

- 39b: the remaining core keys that need a decision first.
- 39c: the `created_by_id` audit columns.
- 39d: `tenant_id` keys to `tenants`, in batches of about 30 tables.
- 39e: the other modules (124 columns).
- 39f: the remaining 97 check constraints, starting with the task 37 workflow tables.
- 39g: review of the 45 remaining cascade deletes.
