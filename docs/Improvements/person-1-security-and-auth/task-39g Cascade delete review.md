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
