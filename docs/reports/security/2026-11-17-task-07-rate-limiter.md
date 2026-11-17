# Task 07: global rate limiter (2026-11-17)

Branch: `claude/improve-task-07-rate-limiter-63ef99`, cut from `development`. The commit has not been pushed.

## What changed

- `PublicApiRateLimitingMiddleware` (a fixed window per IP: 300/min on `/api/v1`, 120/min on `/admin/v1`,
  running before authentication) is deleted. The built-in ASP.NET Core rate limiter replaces it,
  wired with `AddApiRateLimiting` / `UseApiRateLimiting` in `src/ONEVO.Api/RateLimiting/`.
- The limiter now runs right **after** `UseAuthentication`. `RateLimitSubjectMiddleware` works out who
  the caller is. On `/admin/v1` it authenticates `AdminScheme`. On `/api/v1`, when there is no tenant
  cookie user and the request carries a bearer token, it authenticates `TrayDeviceScheme`.
  Authentication results are cached per request, so the authorization step reuses them. It does not
  change `context.User` and it does no authorization. `[RequirePermission]` and the policies are
  untouched.
- Partitions:
  - anonymous, by IP: sliding window;
  - tenant user, by user: token bucket, burst 100, 600/min;
  - tray device, by device: token bucket;
  - per-tenant ceiling: a chained token bucket;
  - `/admin/v1`: anonymous by IP, and per admin user once signed in.

  Health, Swagger and hubs are exempt, as is anything else outside `/api/v1` and `/admin/v1`.
- The 429 response has not changed: a `Retry-After` header plus the `urn:crownus:error:rate-limit`
  problem body, written in `OnRejected`, with a warning log that names the partition kind.
- `AuthRateLimitingMiddleware` has not changed.
- All limits and their defaults are documented in `docs/deployment/rate-limits.md`.

## Interface for the admin-app provider (person 2)

```csharp
// ONEVO.Application.Common.ServiceInterfaces
public interface IRateLimitSettingsProvider
{
    RateLimitSettingsSnapshot GetSettings(Guid? tenantId);   // null = anonymous/admin requests
}
public sealed record RateLimitSettingsSnapshot(long Version, RateLimitSettings Settings);
```

- The limiter calls `GetSettings` on **every request**, synchronously. Implementations must answer
  from memory: cache the database rows and refresh them in the background or on change.
- `Version` must change whenever any value in the returned settings changes. It is part of every
  partition key (`user:{tenant}:{user}:v{n}`, `tenant:{tenant}:v{n}`, `anon:{ip}:v{n}`, ...), so new
  limits apply on the next request with fresh counters. A per-tenant override can use a version per
  tenant, and the limiter does not need to know about it.
- Per tenant: `GetSettings(tenantId)` is called with the tenant of the user or tray device. Its
  `SignedIn`, `TrayDevice` and `TenantCeiling` values (and `Enabled`) are used for that request.
  `GetSettings(null)` supplies `Anonymous`, `AdminAnonymous` and `AdminSignedIn`.
- To swap the provider, register your implementation for `IRateLimitSettingsProvider` before
  `AddApiRateLimiting` (that method uses `TryAddSingleton`) or replace the registration. Nothing else changes.

Settings shape (`ONEVO.Application.Common.Configuration.RateLimitSettings`, section `RateLimiting`):

```text
RateLimitSettings
  Enabled         bool                 (true)
  Anonymous       SlidingWindowLimit   { PermitLimit 300, WindowSeconds 60, SegmentsPerWindow 6 }
  SignedIn        TokenBucketLimit     { TokenLimit 100,  TokensPerPeriod 10,  ReplenishmentPeriodSeconds 1 }  // 600/min
  TrayDevice      TokenBucketLimit     { TokenLimit 60,   TokensPerPeriod 5,   ReplenishmentPeriodSeconds 1 }  // 300/min
  TenantCeiling   TokenBucketLimit     { TokenLimit 3000, TokensPerPeriod 200, ReplenishmentPeriodSeconds 1 }  // 12,000/min
  AdminAnonymous  SlidingWindowLimit   { PermitLimit 120, WindowSeconds 60, SegmentsPerWindow 6 }
  AdminSignedIn   TokenBucketLimit     { TokenLimit 60,   TokensPerPeriod 5,   ReplenishmentPeriodSeconds 1 }  // 300/min
  Validate() -> IReadOnlyList<string>  // every number >= 1
```

The default implementation is `ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting.OptionsRateLimitSettingsProvider`.
It reads `IOptionsMonitor<RateLimitSettings>` and bumps the version only when a reload actually
changes a value. If a reload is invalid, it logs an error and keeps the last good settings. If the
startup settings are invalid, the API fails to boot.

## Tests

`tests/ONEVO.Tests.Unit/Features/Api/RateLimiting/ApiRateLimitingTests.cs` runs the real
`AddApiRateLimiting`/`UseApiRateLimiting` pipeline on a TestServer, with header-driven stand-ins for
the three auth schemes. It covers:

- two users on one IP do not block each other;
- 50 users on one IP, each sending a parallel 15-request dashboard burst with the default limits,
  get no 429;
- an anonymous flood is blocked with `Retry-After` and the problem body;
- tray devices are partitioned per device;
- the tenant ceiling is shared across one tenant's users;
- a changed setting applies on the next request;
- an invalid reload keeps the last good limits;
- health is exempt;
- admin has its own rules, per admin user;
- `Enabled=false` turns the limiter off;
- partition keys carry the version.

To check that the tests can fail, I keyed every partition by IP and removed the version from the
keys. With that change, 7 of the 12 tests failed.

Integration tests: the suites that build their own `ServiceCollection` only build Infrastructure
services, not the HTTP pipeline, so they need no new registration. The `WebApplicationFactory<Program>`
suites get the limiter from `Program.cs`. The Testcontainers suite (`-Full`) was not run.

## Follow-ups (not in scope)

- `AuthRateLimitingMiddleware` allows 20 logins per IP every 5 minutes. A UAT office of 50 people
  on one IP who all sign in at 9:00 will hit it. This task leaves it unchanged by design.
- Counters are process-local. Task 21 moves them to a shared store.
- For unauthenticated `/admin/v1` routes that carry an admin cookie, the limiter now authenticates
  the cookie. This is the same lookup that admin CSRF validation already does.
