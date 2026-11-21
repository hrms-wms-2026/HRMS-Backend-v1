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
