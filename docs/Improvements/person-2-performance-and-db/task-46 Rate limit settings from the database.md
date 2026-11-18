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

Task 07 is in commit `df2b8a177` (report `docs/reports/security/2026-11-17-task-07-rate-limiter.md`). Its interface is:

```csharp
// ONEVO.Application.Common.ServiceInterfaces
public interface IRateLimitSettingsProvider
{
    RateLimitSettingsSnapshot GetSettings(Guid? tenantId);   // null = anonymous and admin requests
}
public sealed record RateLimitSettingsSnapshot(long Version, RateLimitSettings Settings);
```

- `ApiRateLimitingExtensions` calls `GetSettings` **on every request, synchronously**, up to twice: once for the caller's own limit and once for the chained `TenantCeiling`. It must answer from memory. A database query here would add round trips to every API call.
- **The tenant is already passed in.** `RateLimitSubject.Resolve` reads the `tenant_id` claim of the user or tray device, and the limiter calls `GetSettings(subject.TenantId)`. From that snapshot it uses `SignedIn`, `TrayDevice` and `TenantCeiling`. `GetSettings(null)` supplies `Anonymous`, `AdminAnonymous` and `AdminSignedIn`. The provider has to find the tenant's plan itself.
- **The version goes into the key from the snapshot.** `RateLimitSubject.PartitionKey(snapshot.Version)` builds `anon:{ip}:v{n}`, `user:{tenant}:{user}:v{n}`, `tray:{device}:v{n}`, `admin-anon:{ip}:v{n}` and `admin:{admin}:v{n}`. `RateLimitSubject.ForTenantCeiling` builds `tenant:{tenant}:v{n}`. Because each call to `GetSettings(tenantId)` returns its own `Version`, **the version can be kept per tenant.** A change to one tenant then resets only that tenant's counters. The limiter does not need to change.
- If the snapshot's `Settings.Enabled` is false, that request is not limited at all. `Enabled` is read from the same snapshot, so a provider that returned `Enabled = false` for one tenant would switch limiting off for that tenant.
- `OptionsRateLimitSettingsProvider` (`ONEVO.Infrastructure/Services/SharedPlatform/RateLimiting/`) reads `IOptionsMonitor<RateLimitSettings>`. It bumps the version only when a reload changes a value (it compares a JSON fingerprint), keeps the last good settings if a reload fails `RateLimitSettings.Validate()`, and makes the API fail to boot if the startup settings are invalid.
- `AddApiRateLimiting` registers the default provider with `TryAddSingleton`, so a provider registered earlier wins. In `Program.cs`, `AddApiRateLimiting` is called at line 96.
- Counters are per process until task 21. Each API instance keeps its own copy of the settings.

## What to do

1. Add `DatabaseRateLimitSettingsProvider` in `ONEVO.Infrastructure/Services/SharedPlatform/RateLimiting/`, next to `OptionsRateLimitSettingsProvider`. Register it as the `IRateLimitSettingsProvider` singleton before `AddApiRateLimiting` runs: in Infrastructure DI, or in `Program.cs` before line 96. Keep `OptionsRateLimitSettingsProvider`. The new provider uses it as its base and its fallback.
2. **Base settings come from `OptionsRateLimitSettingsProvider.GetSettings(null).Settings`.** It provides `Enabled`, `ReplenishmentPeriodSeconds`, `WindowSeconds`, `SegmentsPerWindow`, and every number with no database row. Database rows only replace `per_minute` and `burst` for one limit. Never take `Enabled` from anywhere but the base.
3. Build the snapshots on each load, never per request:
   - Read every row of `rate_limit_settings`, plus the map of tenant id to plan id from `tenants.subscription_plan_id`.
   - Build one platform snapshot: the base with the global rows applied.
   - Build one snapshot for each tenant whose result differs from the platform snapshot, because the tenant or its plan has an override. Apply rows in this order, the last one winning: base, then global row, then plan row, then tenant row. That gives the precedence **tenant, then plan, then global, then `appsettings`**.
   - Only `SignedIn`, `TrayDevice` and `TenantCeiling` take plan or tenant rows. Ignore any plan or tenant row for the other three, even one added by hand.
   - Each snapshot gets a new `RateLimitSettings` object. Never change the base object or one that has already been handed out, because the limiter treats `Settings` as read-only.
   - Store all of this as one immutable object (`Volatile.Write` or `Interlocked.Exchange`), so a request never sees half of a reload.
4. `GetSettings(tenantId)`: return the tenant's snapshot if there is one, otherwise the platform snapshot. `null` returns the platform snapshot. This is one dictionary lookup.
5. Convert the stored numbers into the task 07 shapes:
   - Token bucket: `TokenLimit = burst` and `TokensPerPeriod = per_minute * ReplenishmentPeriodSeconds / 60`, where `ReplenishmentPeriodSeconds` comes from the base. The API in task 47 only accepts a `per_minute` that divides exactly, which is any multiple of 60 with the default 1-second period.
   - Sliding window: `PermitLimit = round(per_minute * WindowSeconds / 60)`, at least 1.
   - Clamp to the bounds in the guide before converting, even though the API and the check constraint validate already, then run `RateLimitSettings.Validate()`. A bad row must never become a limit of 0. If a built snapshot still fails `Validate()`, log it and use the base for that tenant.
6. Versions:
   - Keep the last version per tenant and for the platform snapshot. On a reload, compare each new snapshot's fingerprint with the old one, the same way `OptionsRateLimitSettingsProvider` does. Add 1 only where the result really changed. A reload with no change must not reset anyone's counters.
   - A tenant snapshot's version must never go down. This includes a tenant that loses its last override and drops back to the platform snapshot. Start each tenant's numbers from the platform version so they can't collide.
   - A change to the base, from an `appsettings` reload, triggers a rebuild. Subscribe to the base provider's version, or to `IOptionsMonitor.OnChange`.
7. Reload:
   - Load once at startup, before the app takes traffic. Use a scoped `DbContext` created through `IServiceScopeFactory`, with `IWritableTenantContext.SetSystemMode()`, as `OutboxProcessor` does.
   - Add `InvalidateAsync()`: a small `IRateLimitSettingsInvalidator` in `ONEVO.Application/Common/ServiceInterfaces`, implemented by the same singleton. The admin commands in task 47 call it after they save, and it reloads straight away.
   - Add a hosted service that checks every 30 seconds whether the data changed, using a cheap query: `COUNT(*)` and `MAX(updated_at)` over `rate_limit_settings`, and a check for changes to `tenants.subscription_plan_id`. If something changed, it reloads. Other API instances pick up a change this way until task 21 adds a shared signal. A tenant created since the last load gets the platform snapshot until the next check, at most 30 seconds.
8. If the database load fails, at startup or later, log an error and keep the last good snapshots. If there are none yet, use the base. Never fail open to "no limit", and never block startup forever.
9. Log one line per reload with the platform version, the number of tenant snapshots and the row count. Do not log tenant names.

## Tests

Unit tests in `ONEVO.Tests.Unit`, next to `tests/ONEVO.Tests.Unit/Features/Api/RateLimiting/ApiRateLimitingTests.cs`, with a fake loader:

- Precedence: a tenant row beats a plan row, a plan row beats a global row, and a global row beats `appsettings`.
- A tenant on a plan with no plan row gets the global row.
- `GetSettings(null)` ignores every plan and tenant row. A hand-added tenant row for `Anonymous` or `AdminSignedIn` has no effect.
- `Enabled` always equals the `appsettings` value, whatever the rows say.
- Conversion: `per_minute = 600` and `burst = 100` give `TokenLimit 100`, `TokensPerPeriod 10` with a 1 s period.
- Clamping: a row below the floor returns the floor, and a row above the ceiling returns the ceiling.
- Versions: changing tenant A's override bumps tenant A's version only, and tenant B's and the platform version stay the same. Changing a global row bumps the platform version and every tenant snapshot that inherits it. A reload with no change keeps every version.
- Removing tenant A's last override still gives tenant A a higher version than before.
- A failed load keeps the last good snapshots.

Pipeline test: reuse the TestServer setup in `ApiRateLimitingTests`, register the database provider with a fake loader, and change a tenant override. After `InvalidateAsync()`, the next request of that tenant uses the new limit: `burst + 1` quick requests, and the last one gets 429. A user of another tenant keeps their counter.

## Done when

- The limiter reads every number from in-memory snapshots, and no request runs a settings query.
- A saved change applies on the same instance at once and on other instances within 30 seconds.
- A change to one tenant resets only that tenant's counters.
- All the tests above pass in `pwsh ../dev-env/scripts/verify.ps1 backend -Full`.
