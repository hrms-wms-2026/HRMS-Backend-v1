# Task 46: Rate limit settings from the database

| | |
|---|---|
| Priority | 46 (lower number = build first). Added 2026-11-17 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, Security |
| Depends on | 07 (built-in rate limiter), 45 (settings table) |
| Guide | [Admin-managed rate limits guide](<../../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>) |

## What we found

- After task 07, the limiter asks `IRateLimitSettingsProvider` for its numbers on every request. The first version reads them from `appsettings` through `IOptionsMonitor` (names per task 07).
- The limiter runs on every request, so the provider must answer from memory. A database query per request would add a round trip to every API call. The 200 ms target is already tight (task 06).
- Task 07 puts a settings version in each partition key, for example `user:{id}:v{version}`. A new version gives every caller a fresh limiter with the new numbers, so a change applies at once. The provider owns that version.
- Counters and caches are per process until task 21. Each API instance has its own copy of the settings.

## What to do

1. Add `DatabaseRateLimitSettingsProvider` in `ONEVO.Infrastructure` that implements `IRateLimitSettingsProvider` (names per task 07). Register it as a singleton in place of the `IOptionsMonitor` version. Keep the `IOptionsMonitor` version in the code as the fallback (step 6).
2. Keep one immutable snapshot in memory:
   - every row of `rate_limit_settings`,
   - a map of tenant id to subscription plan id, taken from `tenants.subscription_plan_id`,
   - a `long Version`.
   Replace the whole snapshot in one step (`Interlocked.Exchange` or a `volatile` field), so a request never sees half of a reload.
3. Resolve a limit in this order, the first match wins:
   1. a `tenant` row for this tenant and partition;
   2. a `plan` row for the tenant's plan and partition;
   3. the `global` row for the partition;
   4. the code default from task 07, if no global row exists.

   `anonymous` and `admin` use only steps 3 and 4. A request with no tenant (anonymous with no host tenant, or admin) skips steps 1 and 2.
4. Clamp every value to the bounds in the guide before returning it, even though the API and the check constraints validate already. A bad row must never turn into a limit of 0.
5. Reload and version:
   - Load once at startup, before the app takes traffic. Use a scoped `DbContext` created through `IServiceScopeFactory` and `IWritableTenantContext.SetSystemMode()`, as `OutboxProcessor` does.
   - Add `InvalidateAsync()` to the Application interface (or a small `IRateLimitSettingsInvalidator`). The admin commands in task 47 call it after they save. It reloads at once and adds 1 to `Version`.
   - Add a hosted service that checks every 30 seconds whether the data changed, using a cheap query (`COUNT(*)` and `MAX(updated_at)` over `rate_limit_settings`, plus the same for `tenants.subscription_plan_id` changes, or a plan-change timestamp if one exists). If something changed, it reloads and adds 1 to `Version`. Other API instances pick up a change this way until task 21 adds a shared signal.
   - Increase `Version` only when the loaded data is really different. A new version resets every caller's counter, so a reload with no change must not reset them.
6. If loading fails (database down at startup), log an error and use the `IOptionsMonitor` values. Never fail open to "no limit", and never block startup forever.
7. Add a log line on each reload with the new version and the row count. Do not log tenant names.

## Tests

Unit tests in `ONEVO.Tests.Unit`, with an in-memory or fake loader:

- Precedence: tenant row beats plan row, plan row beats global row, global row beats code default.
- A tenant on a plan with no plan row falls back to the global row.
- `anonymous` and `admin` ignore any tenant or plan row (even one that got into the table by hand).
- Clamping: a row below the floor returns the floor; a row above the ceiling returns the ceiling.
- Invalidation: after `InvalidateAsync()` the new value is returned and `Version` went up by 1.
- A reload with no data change keeps the same `Version`.
- A failed load returns the `IOptionsMonitor` values.

Integration test (`-Full`): change a tenant override through the database, call `InvalidateAsync()`, and check that the next request for that tenant gets the new limit. Send `limit + 1` requests and expect the last one to get 429.

## Done when

- The limiter reads every number from the database snapshot, and no request runs a settings query.
- A saved change applies on the same instance at once and on other instances within 30 seconds.
- All tests above pass in `pwsh ../dev-env/scripts/verify.ps1 backend -Full`.
