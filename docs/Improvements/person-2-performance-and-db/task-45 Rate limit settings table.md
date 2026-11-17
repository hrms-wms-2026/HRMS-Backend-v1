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

- Task 07 (commit `df2b8a177`, report `docs/reports/security/2026-11-17-task-07-rate-limiter.md`) replaced `PublicApiRateLimitingMiddleware` with the built-in ASP.NET Core rate limiter (`src/ONEVO.Api/RateLimiting/ApiRateLimitingExtensions.cs`). It reads limits through `IRateLimitSettingsProvider` (`ONEVO.Application/Common/ServiceInterfaces`). The only implementation, `OptionsRateLimitSettingsProvider`, reads the `RateLimiting` section of `appsettings` (class `RateLimitSettings` in `ONEVO.Application/Common/Configuration`). Nothing is stored in the database.
- `RateLimitSettings` has a master switch `Enabled` and six named limits. Every limit and its default is in `docs/deployment/rate-limits.md`:

  | Limit | Applies to | Shape | Default |
  |---|---|---|---|
  | `Anonymous` | `/api/v1`, not signed in, by IP | `SlidingWindowLimit` (`PermitLimit`, `WindowSeconds`, `SegmentsPerWindow`) | 300 per 60 s |
  | `SignedIn` | tenant user, by user | `TokenBucketLimit` (`TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriodSeconds`) | burst 100, 600/min |
  | `TrayDevice` | tray device, by device | `TokenBucketLimit` | burst 60, 300/min |
  | `TenantCeiling` | all users and devices of one tenant, chained after the two above | `TokenBucketLimit` | burst 3,000, 12,000/min |
  | `AdminAnonymous` | `/admin/v1`, not signed in, by IP | `SlidingWindowLimit` | 120 per 60 s |
  | `AdminSignedIn` | `/admin/v1`, by platform admin | `TokenBucketLimit` | burst 60, 300/min |

- The limiter calls `GetSettings(tenantId)` with the tenant of the user or tray device and uses that snapshot's `SignedIn`, `TrayDevice` and `TenantCeiling`. Anonymous and admin requests call `GetSettings(null)`. So per-tenant overrides make sense only for those three limits. The other three are platform-wide.
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
   - `limit_name`: one of the six `RateLimitSettings` property names above, stored as written (`SignedIn`, not `signed_in`), so the provider can map rows without a lookup table
   - `per_minute`: int, required. The sustained rate an admin edits
   - `burst`: int, nullable. Required for the four token-bucket limits (`TokenLimit`). Null for `Anonymous` and `AdminAnonymous`, which are sliding windows and have no burst
   - `updated_at`, `updated_by_platform_user_id`
   - `row_version` for concurrency

   Admins edit only `per_minute` and `burst`. `ReplenishmentPeriodSeconds`, `WindowSeconds` and `SegmentsPerWindow` stay in `appsettings`, and task 46 converts the stored numbers into them (see the guide, section 4).

   **There is no column for `Enabled`.** The master switch stays in `appsettings` only, so no admin action can turn the limiter off.
2. Add check constraints:
   - `scope = 'global'`: both ids are null.
   - `scope = 'plan'`: `subscription_plan_id` is set and `tenant_id` is null.
   - `scope = 'tenant'`: `tenant_id` is set and `subscription_plan_id` is null.
   - `limit_name` is one of the six names.
   - `Anonymous`, `AdminAnonymous` and `AdminSignedIn` are allowed only with `scope = 'global'`. Plans and tenants can change only `SignedIn`, `TrayDevice` and `TenantCeiling`.
   - `burst` is null when `limit_name` is `Anonymous` or `AdminAnonymous`, and not null otherwise.
   - `per_minute` is between 10 and 200,000, and `burst`, when set, is between 1 and 50,000. These are the absolute ranges in the guide. The API checks the tighter per-limit bounds (task 47). The constraint stops a hand-written SQL update from locking everyone out.
3. Add filtered unique indexes, one per scope, so each scope and limit has at most one row:
   `(limit_name) WHERE scope = 'global'`, `(subscription_plan_id, limit_name) WHERE scope = 'plan'`, `(tenant_id, limit_name) WHERE scope = 'tenant'`.
4. Add the append-only history table `rate_limit_setting_changes`: `id`, `changed_at`, `platform_user_id`, `scope`, `subscription_plan_id`, `tenant_id`, `limit_name`, `old_per_minute`, `old_burst`, `new_per_minute`, `new_burst`, `reason` (required), `correlation_id`. The old values are null for a new override, and the new values are null when an override is removed. Rows are never updated or deleted.
5. RLS for both tables:
   - Add both to `RlsTableRegistry` with `RlsPredicate.TenantOrSystem`. Admin mode writes them. System mode (the settings loader, task 46) reads them. Tenant mode never needs them. With this rule, the global and plan rows, whose `tenant_id` is null, cannot be seen from tenant mode.
   - Call `RlsSql.CreatePolicy` for both tables in the migration.
   - Do not make the entities `ITenantOwnedEntity`. Their `TenantId` is nullable and they have no tenant query filter. The RLS policy is the guard.
   - Do not rename `tenant_id` to something else to get past the architecture test. Never weaken or skip that test.
6. Do not seed any rows. An empty table means "use `appsettings`", which is exactly today's behaviour. A global row is an admin override of `appsettings`, not a copy of it.
7. Create the migration from an up-to-date checkout that already has task 07. Check that `ApplicationDbContextModelSnapshot.cs` shows only the two new tables (see `CLAUDE.md`, "Known gotchas"). Do not apply it to a shared database. Report it and let the developer apply it.

## Done when

- The migration creates both tables, their constraints, indexes and RLS policies.
- `pwsh ../dev-env/scripts/verify.ps1 backend` passes, including `ONEVO.Tests.Architecture`.
- An integration test (`-Full`, SQL Server) shows that:
  - tenant mode reads no rows from either table;
  - admin mode can insert a global, a plan and a tenant row;
  - the check constraint rejects an insert with `per_minute = 0`, a plan-scope `Anonymous` row, or a `SignedIn` row with no `burst`.
