# Task 39c: Foreign keys for the created_by_id audit columns

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, half a day |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

76 `created_by_id` columns (group `audit` in `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt`). When no user is signed in (seeders, jobs, anonymous paths), `AuditableEntityInterceptor` writes `Guid.Empty`, so a key to `users` would fail.

1. Make `BaseEntity.CreatedById` nullable and write `null` instead of `Guid.Empty`. Change existing `Guid.Empty` values to `NULL` in the same migration.
2. Check that `created_by_id` always holds a `users.id`, never an employee id or a platform user id. If platform users write tenant rows, the key cannot point at `users` alone. Record that case and leave those columns on the list with that reason.
3. Add the keys to `users` with NO ACTION, in batches of about 30 tables per migration.

## How to work

- Add keys in `src/ONEVO.Infrastructure/Persistence/Integrity/ReferenceForeignKeys.cs`. Delete each fixed line from the allow-list.
- Create migrations with `dotnet ef migrations add`. Do not apply them to a shared database.
- Run the full gate with your worktree path and `-Full`.

## Done when

- No `audit` line is left, or each one left has a reason other than "Guid.Empty".

## Results

Done on branch `claude/improve-task-39c-audit-fks` (from local `feature/testing` 07e9075f4, which has 39b). Report: `docs/reports/database/2026-11-24-task-39c-audit-column-foreign-keys.md`.

- Migration: `20261123120447_AddAuditColumnForeignKeys39c`, generated with `dotnet ef migrations add`. 70 columns become nullable; 75 indexes and 75 NO ACTION foreign keys. One migration instead of batches, as Execution asked. Not applied to any shared database.
- 69 keys to `users`, 5 to `platform_users` (platform-admin tables), 1 to `employees` (Work Management `calendar_events`, whose writers store the creating employee).
- 75 of 76 `audit` lines are gone from `missing-foreign-keys.txt` (384 → 309 lines). `invitation_tokens.created_by_id` stays with a reason: it holds a `platform_users` id for the tenant owner's invitation and a `users` id otherwise.
- `BaseEntity.CreatedById` (and `ClockInPolicy`, `MeetingSeries`, the Work Management `CalendarEvent`) is nullable. `AuditableEntityInterceptor` writes `NULL` for anonymous callers and for platform admins (whose id is not a `users` id). Raw inserts write `NULL` too.
- Hand-written SQL: `Guid.Empty` → `NULL` repair that stops with error 50039 on any other bad value and never deletes (`AuditCreatorRepairTests`), and `wm_refresh_module_hour_summary`, which inserted `Guid.Empty`, now inserts `NULL`.
- Bad data: the agent cannot read the database. The user recreated the local `CrownusDb` at 39b level; the report has a read-only orphan query for any other database.
- Test fixtures: real creator and actor user ids instead of random ones (meetings, employee detail, position assignments).

## Status (2026-11-27)

Done. Merged into the local branch `claude/merge-development-1126` (7b9741846) together with 39b to 39g, 15b, 27b, 44b and the latest `origin/development`. Gate: unit 8695 passed, architecture 1028 passed, SqlServerMigration and RateLimit 333 passed. Not yet in `feature/testing` or `development`, not pushed. Migration `AddAuditColumnForeignKeys39c` is created, not applied; the order of all migrations is in `docs/reports/database/2026-11-26-merge-development-migrations.md`.
