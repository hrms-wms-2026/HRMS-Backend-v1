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
