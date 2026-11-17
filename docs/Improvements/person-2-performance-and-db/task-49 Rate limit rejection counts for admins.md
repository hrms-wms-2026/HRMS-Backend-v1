# Task 49: Rate limit rejection counts for admins (optional)

| | |
|---|---|
| Priority | 49 (lower number = build first). Added 2026-11-17. Optional: build it after 45 to 48 if there is time |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Observability, Admin frontend |
| Depends on | 07, 47 (admin API), 48 (admin screens). Task 17 (metrics) makes it better but does not block it |
| Guide | [Admin-managed rate limits guide](<../../superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md>) |

## What we found

- **There are no metrics in the backend today.** A search of `src` finds no `System.Diagnostics.Metrics`, no `new Meter(` and no `AddOpenTelemetry`. Logging is console only (README, "What the scan found"). Task 17 adds logs, metrics and alerts for UAT.
- Task 07 (commit `df2b8a177`) sends every rejection through `options.OnRejected = WriteRejectionAsync` in `src/ONEVO.Api/RateLimiting/ApiRateLimitingExtensions.cs`. The only trace of a 429 today is the warning log written there, `API rate limit exceeded`, with `Partition` (the `RateLimitSubjectKind`: `Anonymous`, `User`, `TrayDevice`, `AdminAnonymous` or `Admin`), `TenantId`, path and retry delay. It does not log the IP or user id.
- `WriteRejectionAsync` gets the subject from `RateLimitSubjectMiddleware.GetSubject(context)`, so the kind and tenant are already at hand.
- The global limiter is a chain: the caller's own limit, then `TenantCeiling`. `OnRejected` does not say which link refused. A `User` rejection may come from the user's own `SignedIn` bucket or from the tenant ceiling.
- So an admin cannot see who is being limited. Before raising a tenant's limit, the admin needs to know whether that tenant is really hitting it.

## What to do

1. In `WriteRejectionAsync`, next to the existing log call, record each rejection in two places. Resolve the recorder from `context.RequestServices`, as the method already does for the logger:
   - A .NET `Meter` named `ONEVO.RateLimiting`, with a counter `rate_limit_rejections` and the tags `kind` (the `RateLimitSubjectKind` name) and `tenant_id`. Never tag with a user id, IP or session id, because metric tags must not hold personal data. Task 17 exports this counter when it lands.
   - An in-memory rolling counter (`IRateLimitRejectionStats`, singleton) that keeps counts per tenant and subject kind in one-minute buckets for the last 60 minutes. Cap it at 5,000 tenant keys, dropping the oldest, so a flood cannot grow memory without limit.
2. Add `GET admin/v1/system-config/rate-limits/rejections?minutes=60` with `[Authorize(Policy = "AdminPolicy")]` and `[RequirePlatformPermission(PlatformPermissionCatalog.SystemConfigRead)]`. It returns the top 50 tenants by rejections, with the counts per subject kind and each tenant's effective `SignedIn`, `TrayDevice` and `TenantCeiling` limits. Resolve the tenant name in the query handler.
3. `OnRejected` cannot tell a tenant-ceiling rejection from a rejection by the caller's own limit, so say so on the screen: "A `User` or `TrayDevice` rejection can come from the person's own limit or from the tenant ceiling. If one tenant shows many rejections across many users, the ceiling is the likely cause."
4. Mark the response clearly as "this API server only, last N minutes, since last restart". Until task 21, each instance has its own counts. With two instances the page shows only one of them. Put that sentence in the response (`scope: "instance"`) and on the screen.
5. In the admin app, add a "Rejections (last hour)" section to the `system-config/rate-limits` page from task 48, and a "429s in the last hour" line on `tenant-rate-limits-panel`. Use `shared/ui/metric-card` and `shared/ui/table`.

## Tests

- Unit: the rolling counter drops buckets older than 60 minutes and never holds more than the key cap.
- Unit, with the TestServer setup in `tests/ONEVO.Tests.Unit/Features/Api/RateLimiting/ApiRateLimitingTests.cs`: a rejection adds 1 for the right tenant and kind. Anonymous and admin rejections, which have no tenant, go under `(none)`.
- Integration: flood one test tenant past its limit, then call the rejections endpoint and see that tenant at the top. A user without `platform.system_config.read` gets 403.

## Done when

- An admin can see which tenants hit their limits in the last hour before deciding to change a limit.
- The screen says the numbers are per API server.
- No personal data is in metric tags or in the response.
