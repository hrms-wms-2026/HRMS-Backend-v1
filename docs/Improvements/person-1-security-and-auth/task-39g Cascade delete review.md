# Task 39g: Review the cascade deletes

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, half a day |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

45 foreign keys still use `ON DELETE CASCADE`. The design review counted 51, and task 39a moved 7 keys from `employees` to NO ACTION. Item 4 of task 39 says to keep cascade only for true parts of a parent (line items, layout rows, a task's comments), and to write down the decision for each key.

- List the keys from `ApplicationDbContextModelSnapshot.cs` (`OnDelete(DeleteBehavior.Cascade)`), with the child, the parent, and whether the code ever hard-deletes the parent.
- A cascade from a parent that is only soft-deleted never fires. Prefer NO ACTION there, so that a stray hard delete fails instead of removing history.
- SQL Server rejects multiple cascade paths, so changing one key can make another key legal or illegal. Regenerate the migration and run it on a fresh database (integration suite).
- `ForeignKeyRulesTests.NoForeignKeyToAPersonCascades` already guards `employees` and `users`. Extend it with an allow-list of the cascades you keep, each with a reason.

## Done when

- Every remaining cascade is on the allow-list with its reason, and a report in `docs/reports/database/` lists the decision for each key.

## Results (2026-11-25)

Report: `docs/reports/database/2026-11-25-task-39efg-module-keys-checks-cascades.md`. Branch `claude/improve-task-39efg` (39e, 39f and 39g together).

- 45 cascades reviewed: 17 kept, 28 changed to NO ACTION by `CascadeDeleteRules` (migration `20261125053817_ReviewCascadeDeletes39g`, 28 drop and add, no data change). Not applied by the agent.
- Kept: line items and layout rows that the code clears or removes (clock-in late rules, dashboard widgets and layouts), the parts of a conversation message (system cards are hard-deleted), leave policy blackout periods, the grants and overrides of a retired permission, and the parts of roles and tasks, which are removed through EF.
- `ForeignKeyRulesTests.EveryCascadeIsOnTheAllowList` lists the kept keys with their reasons; `NoForeignKeyToAPersonCascades` now covers `platform_users`.
- The report has the decision for each key.

## Status (2026-11-27)

Done. Merged into the local branch `claude/merge-development-1126` (7b9741846) together with 39b to 39g, 15b, 27b, 44b and the latest `origin/development`. Gate: unit 8695 passed, architecture 1028 passed, SqlServerMigration and RateLimit 333 passed. Now in local `feature/testing` too, not yet in `development`, not pushed. Migration `ReviewCascadeDeletes39g` is created, not applied; the order of all migrations is in `docs/reports/database/2026-11-26-merge-development-migrations.md`.
