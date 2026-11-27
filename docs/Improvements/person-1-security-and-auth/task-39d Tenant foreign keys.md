# Task 39d: Foreign key from every tenant_id to tenants

| | |
|---|---|
| Priority | 39 of 42, after task 39a. Added 2026-11-19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | DB, Security |
| Depends on | task 39a (inventory test and first slice) |

## What is left

163 `tenant_id` columns (group `tenant` in `tests/ONEVO.Tests.Architecture/Schema/missing-foreign-keys.txt`) have no key to `tenants`. Task 39 asks for batches of about 30 tables per migration.

- The application never hard-deletes a `tenants` row: a tenant is cancelled, not removed. Use NO ACTION everywhere.
- Tables with a nullable `tenant_id` (the `PlatformShared` RLS tables, such as `outbox_messages`) keep it nullable. The key only checks values that are not null.
- RLS predicates do not apply to foreign key checks, so the keys work for every predicate, `TenantStrict` included. Still run `ONEVO.Tests.Architecture` (RLS registry tests) and the integration suite after each batch.
- A generic helper over `ITenantOwnedEntity` in `ReferenceForeignKeys` is fine, but add one batch per migration, so each migration stays short.

## Done when

- No `tenant` line is left in the allow-list.
- Migrations are created and reported, not applied to a shared database.

## Results

Done on branch `claude/improve-task-39d-tenant-fks` (from local `feature/testing` 7b4073f7a, with the later 39c fixes merged in). Report: `docs/reports/database/2026-11-25-task-39d-tenant-foreign-keys.md`.

- Migration: `20261124131426_AddTenantForeignKeys39d`, generated with `dotnet ef migrations add`. 163 NO ACTION keys to `tenants` and 19 indexes; no table or column change. One migration instead of batches, as Execution asked. Not applied to any shared database.
- All 163 `tenant` lines are gone from `missing-foreign-keys.txt` (309 → 146 lines). `ReferenceForeignKeys.ApplyTenantColumns` adds the keys from one loop over the `TenantId` property, so nullable PlatformShared columns stay nullable and later tables get their key.
- Hand-written SQL: a read-only check, in admin tenant context mode, that stops with error 50039 and names each table holding a `tenant_id` that is not a tenant (`TenantOrphanCheckTests`). Nothing is repaired or deleted.
- RLS: key checks are not filtered by RLS; the architecture (RLS registry) tests pass unchanged.
- Local `CrownusDb` check (run by the user): all 163 columns `unknown 0`.
- Test fixtures: new `TestTenants` helper for made-up tenant ids (dashboards, sessions); the work-area schema test names the new key.

## Status (2026-11-27)

Done. Merged into the local branch `claude/merge-development-1126` (7b9741846) together with 39b to 39g, 15b, 27b, 44b and the latest `origin/development`. Gate: unit 8695 passed, architecture 1028 passed, SqlServerMigration and RateLimit 333 passed. Now in local `feature/testing` too, not yet in `development`, not pushed. Migration `AddTenantForeignKeys39d` is created, not applied; the order of all migrations is in `docs/reports/database/2026-11-26-merge-development-migrations.md`.
