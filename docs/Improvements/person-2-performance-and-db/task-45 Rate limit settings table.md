# Task 45: Rate limit settings table

| | |
|---|---|
| Priority | 45 (lower number = build first). Added 2026-11-17 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | DB, Security |
| Depends on | 07 (built-in rate limiter) |
| Guide | [Admin-managed rate limits guide](<../../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>) |

First of five tasks (45 to 49) that let the platform admin app change API rate limits while the API is running. Read the guide first. It explains rate limiting from the start and shows how the five tasks fit together.

## What we found

- Task 07 replaces `PublicApiRateLimitingMiddleware` with the built-in ASP.NET Core rate limiter. It reads limits through an Application-layer interface, `IRateLimitSettingsProvider`, and its first version reads them from `appsettings` through `IOptionsMonitor` (names per task 07). Changing a limit still needs a settings change on the server. Nothing is stored in the database.
- The partitions from task 07 are `anonymous` (by IP), `user` (by session or user id), `tray` (by device id), `tenant` (a ceiling per tenant) and `admin` (the `/admin/v1` rule).
- `subscription_plans` and `tenants` are platform tables. They are not in `RlsTableRegistry`. `tenants.subscription_plan_id` links a tenant to its plan.
- `SubscriptionPlan.FeatureLimitsJson` already exists, but it is free-form JSON with no validation. Do not store rate limits there. One typed table gives one precedence query and real check constraints.
- The RLS architecture test `MigrationsCreatingATenantTable_CreateItsPolicy` (`tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs`) fails any new migration that creates a table with a `tenant_id` column and does not call `RlsSql.CreatePolicy`. The new table has a nullable `tenant_id`, so it needs a policy even though it is platform data.
- There is no general platform audit table. Billing has `billing_audit_logs`, and tenant actions go to `audit_logs`, which is tenant-owned. Rate limit changes need their own history table.

## What to do

1. Add the entity `RateLimitSetting` in `ONEVO.Domain/Features/SharedPlatform/RateLimiting/Entities/` and the table `rate_limit_settings`:
   - `id`
   - `scope`: `global`, `plan` or `tenant`
   - `subscription_plan_id`: nullable, FK to `subscription_plans`
   - `tenant_id`: nullable, FK to `tenants`
   - `partition`: `anonymous`, `user`, `tray`, `tenant` or `admin` (use the partition names from task 07)
   - `permits_per_minute`: int. This is the only number an admin edits. The algorithm and the 1-minute window stay in code (see the guide, section 4)
   - `updated_at`, `updated_by_platform_user_id`
   - `row_version` for concurrency
2. Add check constraints:
   - `scope = 'global'`: both ids are null.
   - `scope = 'plan'`: `subscription_plan_id` is set and `tenant_id` is null.
   - `scope = 'tenant'`: `tenant_id` is set and `subscription_plan_id` is null.
   - `partition` is one of the five values.
   - `anonymous` and `admin` are allowed only with `scope = 'global'`. Plans and tenants can change only `user`, `tray` and `tenant`.
   - `permits_per_minute` is between the hard floor and ceiling in the guide's bounds table. The API checks this too (task 47). The constraint stops a hand-written SQL update from locking everyone out.
3. Add filtered unique indexes, one per scope, so each scope and partition has at most one row:
   `(partition) WHERE scope = 'global'`, `(subscription_plan_id, partition) WHERE scope = 'plan'`, `(tenant_id, partition) WHERE scope = 'tenant'`.
4. Add the append-only history table `rate_limit_setting_changes`: `id`, `changed_at`, `platform_user_id`, `scope`, `subscription_plan_id`, `tenant_id`, `partition`, `old_permits_per_minute` (null for a new override), `new_permits_per_minute` (null when an override is removed), `reason` (required), `correlation_id`. Rows are never updated or deleted.
5. RLS for both tables:
   - Add both to `RlsTableRegistry` with `RlsPredicate.TenantOrSystem`. Admin mode writes them. System mode (the settings loader, task 46) reads them. Tenant mode never needs them. With this rule, the global and plan rows, whose `tenant_id` is null, cannot be seen from tenant mode.
   - Call `RlsSql.CreatePolicy` for both tables in the migration.
   - Do not make the entities `ITenantOwnedEntity`. Their `TenantId` is nullable and they have no tenant query filter. The RLS policy is the guard.
   - Do not rename `tenant_id` to something else to get past the architecture test. Never weaken or skip that test.
6. Seed the global rows from the task 07 defaults, using the same numbers as `appsettings`, so the database and the current behaviour match on day one.
7. Create the migration from an up-to-date checkout. Check `ApplicationDbContextModelSnapshot.cs` shows only the two new tables (see `CLAUDE.md`, "Known gotchas"). Do not apply it to a shared database. Report it and let the developer apply it.

## Done when

- The migration creates both tables, their constraints, indexes and RLS policies, and seeds the global rows.
- `pwsh ../dev-env/scripts/verify.ps1 backend` passes, including `ONEVO.Tests.Architecture`.
- An integration test (`-Full`, SQL Server) shows: tenant mode reads no rows from either table; admin mode can insert a global, a plan and a tenant row; an insert with `permits_per_minute = 0` or a plan-scope `anonymous` row fails on the check constraint.
