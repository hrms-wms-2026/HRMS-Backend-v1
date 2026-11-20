# Task 39a: foreign keys and check constraints, first slice (2026-11-19)

Task: `docs/Improvements/person-1-security-and-auth/task-39 Foreign keys and check constraints.md`. Source finding: sections C1, C2, C3 and C10 of `2026-11-15-database-design-review.md`.

## What changed

| Item | Before | After |
|---|---|---|
| GUID `_id` columns without a foreign key (including `tenant_id`) | 477 | 418 |
| of which `tenant_id` | 163 | 163 (task 39d) |
| Status/type text columns without a check constraint | 124 | 103 |
| Cascade deletes from `employees` | 7 | 0 |

The review counted 313 id columns without a key; the EF model gives 314, because one column was added since.

- **Inventory and ratchet**: `tests/ONEVO.Tests.Architecture/Schema/SchemaIntegrityInventoryTests.cs` with two allow-lists. A new column without a key or constraint fails the architecture tests unless it is listed with a reason. To rewrite the lists from the model, run that class with `ONEVO_SCHEMA_INVENTORY_WRITE=1`.
- **59 foreign keys**, all `NO ACTION` (one is `Restrict` to match its table; same DDL): `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`. They cover core HR (9), leave (2), time and attendance (13) and work management (35). There are no navigation properties, so the domain model is unchanged. EF added 59 indexes, so every new key has an index that starts with it.
- **21 check constraints** (`col IN (...)`): `src/ONEVO.Infrastructure/Persistence/Integrity/StatusCheckConstraints.cs`. 8 of them cover enum-backed columns: `tenants.status`, `tenant_status_histories.from_status` and `to_status`, `exceptions.status` and `type`, `monitoring_notifications.type`, and `biometric_*.status`. The other 13 use constants classes: `leave_requests`, `attendance_records` and `outbox_messages` (named in task 39), plus leave approvers, day allocations, balance audit change types, attendance alerts and deductions, position assignments, onboarding drafts, bulk onboarding batches and rows, and sprints.
- **Enum sync test**: `StatusCheckConstraintTests.MigrationsAllowEveryValueTheCodeCanWrite` compares each constraint in the migration snapshot with the enum or constants. It fails when code gains a value that the migrations do not allow.
- **Delete behaviour**: 7 keys from `employees` changed from CASCADE to NO ACTION. `ForeignKeyRulesTests` guards the rule, and also checks that every foreign key has an index that starts with it.
- **Migration**: `20261120015440_AddReferenceForeignKeysAndStatusChecks39a`. It was first generated as `20261119161942_...` and re-created after merging feature/testing (task 33 and `SingleReactionPerCommentEmployee`). Up and Down are identical to the first version apart from the timestamp. It changes no tables or columns. It was created with `dotnet ef migrations add` and has not been applied to any shared database.

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

  For a check constraint, count the rows where `status NOT IN (...)`. All 59 keys use tables whose RLS predicate allows admin mode, so the query sees every row.

## Gate

### After merging feature/testing (474437482)

Gate `verify-20261120-072916` on 17f3f3b8f, with the migration re-created as `20261120015440`:

| Step | Result |
|---|---|
| build | PASS |
| unit | 8343 passed, 0 failed |
| architecture | 841 passed, 0 failed |
| integration | 1391 passed, 6 failed, 1 skipped |

The 6 failures, each re-run on its own:

- `PlatformAdminAuthIntegrationTests` (2) and `PermissionLifecycleSqlServerTests` (2): the classes pass when run alone. This full run took 2 h 16 min under load from parallel sessions.
- `SpaHostingTests.MissingAsset_Is404` (SingleHost and Subdomain): fails alone with 401 instead of 404, and fails the same way on feature/testing 474437482 without this branch. It is not caused by task 39a, which changes nothing in hosting or auth.

### Before the merge

Full gate (`verify.ps1 <worktree> -Full`) on commit 05f22978c: `dev-env/reports/improve-task-39-fk-check-constraints-b63e85/verify-20261120-001923`.

| Step | Result |
|---|---|
| build | PASS |
| unit | 8019 passed, 0 failed |
| architecture | 784 passed, 0 failed |
| integration | 1250 passed, 1 failed (flaky, see below), 1 skipped |

Tests that failed once in a full run and passed when re-run on their own. None of them touches a key or check from this task:

- `QueryTranslationSweepTests.EveryQueryMethod_RunsOnSqlServer` (gate 20261120-001923): passed alone 4 times in a row on c154603e6 (2 m 17 s, 3 m 9 s, 2 m 44 s, 2 m 19 s). The three detailed runs gave identical counts: 195 tables populated, 979 queries ran, 211 unreached, 0 failed. The gate log does not record which query failed. Each query has a 60-second timeout, which fits a failure under full-suite load. Task 33 had the same failure.

## Hand-written parts of the migration

None. `20261120015440_AddReferenceForeignKeysAndStatusChecks39a` and its snapshot change are wholly generated by `dotnet ef migrations add`. There is no `migrationBuilder.Sql(...)` call and no orphan clean-up SQL. It can be re-created on top of newer migrations by deleting its two files and running `migrations add` again.
- `AuthLookupBaseLoginCandidatesFunctionTests.RestrictedRole_CanCallFunctionAndSeesBothTenantsCandidates` and `DataDependentQueryTests.ReportingLineMove_ChangesTheManagerAndNotifies` (gate 20261119-220005): both classes pass alone, 9/9.
- `WidgetDataOrchestratorTests.Slow_provider_times_out_into_error` (unit, gate 20261120-001557): passes alone 3/3. It is a timing test.

## Follow-ups

These are the task files 39b to 39g in `docs/Improvements/person-1-security-and-auth/`:

- 39b: the remaining core keys that need a decision first.
- 39c: the `created_by_id` audit columns.
- 39d: `tenant_id` keys to `tenants`, in batches of about 30 tables.
- 39e: the other modules (124 columns).
- 39f: the remaining 97 check constraints, starting with the task 37 workflow tables.
- 39g: review of the 45 remaining cascade deletes.

## Integration failures found and fixed

The first full gate failed with 337+ integration failures, all foreign key violations from test fixtures (no check constraint failed). Runs of the affected classes found them layer by layer, since each test stops at its first bad insert:

| Key | Failures | Cause | Change |
|---|---|---|---|
| `fk_employees_users_user_id` | 433 | ~26 fixtures insert employees with a random `UserId` and no `users` row | Key moved to task 39b |
| `fk_projects_project_categories_category_id` | 302 | `MyTeamDb.AddProject` (used by the meeting, planner and My Team suites) set a random `CategoryId` | Fixture now inserts a `project_categories` row; key kept |
| `fk_task_assignments_users_user_id` | 86 | Assignments copy the fixture employees' random user ids | Key moved to task 39b |
| `fk_employee_hierarchy_closure_..._source_position_assignment_id` | 26 | Six test files write closure rows with made-up assignment ids | Key moved to task 39b |
| `fk_task_drafts_projects_project_id` | 2 | Drafts stored for projects that do not exist | Key moved to task 39b |
| `fk_employee_hierarchy_closure_employees_ancestor_employee_id` | 1 | `PositionAssignmentRlsIntegrationTests` used a random ancestor | Fixture uses a real employee; key kept |
| `fk_task_comments_employees_employee_id` | 1 | `TaskCommentsIntegrationTests` gave a comment a random author | Fixture inserts the author employee; key kept |

The unit gate also failed once: `EfPositionRepositoryTests` requires every key of `management_coverage_records` to be `Restrict`, so the new key on that table uses `Restrict` (the same `NO ACTION` DDL).

No test was weakened. `MeetingIntegrityTests` sets random objective ids on purpose, but it turns constraints off first (`RowFactory.DisableConstraintsAsync`), so it is not affected.
