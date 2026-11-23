# Admin-managed rate limits: guide

**Status:** Proposed 2026-11-17. Not built. Tasks 45 to 49 in `docs/Improvements/person-2-performance-and-db/`.
**For:** Person 2 (admin app owner). No rate limiting background is assumed.
**Builds on:** task 07, [Fix the global rate limiter](<../../../Improvements/person-1-security-and-auth/task-07 Fix the global rate limiter.md>):
- commit `df2b8a177` on `claude/improve-task-07-rate-limiter-63ef99`;
- its report, `docs/reports/security/2026-11-17-task-07-rate-limiter.md`;
- every limit, setting key and default, in `docs/deployment/rate-limits.md`.

Read those two files first if they are on your branch. All names in this guide are the real ones from that commit.

## The tasks

| # | Task | What it gives you |
|---|---|---|
| 45 | [Rate limit settings table](<../../../Improvements/person-2-performance-and-db/task-45 Rate limit settings table.md>) | Where the overrides and their change history are stored |
| 46 | [Rate limit settings from the database](<../../../Improvements/person-2-performance-and-db/task-46 Rate limit settings from the database.md>) | The limiter reads the overrides, from memory, and picks up changes |
| 47 | [Admin API for rate limits](<../../../Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md>) | Endpoints to read and change limits, with checks and audit |
| 48 | [Admin app screens for rate limits](<../../../Improvements/person-2-performance-and-db/task-48 Admin app screens for rate limits.md>) | The screens in the platform admin app |
| 49 | [Rate limit rejection counts for admins](<../../../Improvements/person-2-performance-and-db/task-49 Rate limit rejection counts for admins.md>) | Optional: who is being limited right now |

---

## 1. What rate limiting is, and what `AddRateLimiter` can do

A rate limiter counts requests and says "no" (HTTP 429 Too Many Requests) when a caller sends too many in a period. It protects the API and the database from one caller using all the capacity: a buggy page polling in a loop, a script, or an attack.

Three words come up all the time:

- **Partition:** whose requests are counted together. "Per IP address" means everyone behind one IP shares one counter. "Per user" means each user has their own. Task 07 exists because the old limiter counted per IP, so a whole office shared 300 requests a minute.
- **Permit:** one allowed request. A limit of "600 per minute" means 600 permits per minute.
- **Burst:** how many requests a caller can make *at once* before the steady rate applies. One dashboard load is about 15 requests in a second.

ASP.NET Core has a built-in rate limiter (`services.AddRateLimiter(...)` and `app.UseRateLimiter()`). Task 07 switched to it. The code is in `src/ONEVO.Api/RateLimiting/` (`ApiRateLimitingExtensions.AddApiRateLimiting` and `UseApiRateLimiting`).

### The four algorithms

| Algorithm | How it counts | Good for | Weak point |
|---|---|---|---|
| **Fixed window** | Up to N requests per clock window (for example 12:00:00 to 12:00:59), then the counter resets | Simple, cheap limits | A burst at the edge: N at 12:00:59 and N more at 12:01:00, so 2N in two seconds |
| **Sliding window** | The window is split into segments (for example 6 segments of 10 s). Old segments drop off one by one | Strict limits where edge bursts matter, such as anonymous traffic | A little more memory per partition |
| **Token bucket** | A bucket holds up to N tokens (the burst). Each request takes one. Tokens are added back at a steady rate | Real users: a page load can burst, but the steady rate stays bounded | Two numbers to explain instead of one |
| **Concurrency** | At most N requests *in progress at once*, no time window | Protecting a slow endpoint (an export, a report) | Does not limit how many requests come in over time |

Task 07 uses a sliding window for `Anonymous` and `AdminAnonymous` (counted by IP), and a token bucket for `SignedIn`, `TrayDevice`, `TenantCeiling` and `AdminSignedIn`.

### Partitions

You give the limiter a function that looks at the request and returns a **key**. The limiter keeps one small counter per key. The first request with a new key creates that key's counter, using the options you return at that moment.

In task 07, `RateLimitSubjectMiddleware` works out who the caller is (a `RateLimitSubject`), and `RateLimitSubject.PartitionKey` builds the key:

| Who | Subject kind | Key | Limit used |
|---|---|---|---|
| `/api/v1`, not signed in | `Anonymous` | `anon:{ip}:v{n}` | `Anonymous` |
| tenant user | `User` | `user:{tenant}:{user}:v{n}` | `SignedIn`, then `TenantCeiling` (`tenant:{tenant}:v{n}`) |
| tray device | `TrayDevice` | `tray:{device}:v{n}` | `TrayDevice`, then `TenantCeiling` |
| `/admin/v1`, not signed in | `AdminAnonymous` | `admin-anon:{ip}:v{n}` | `AdminAnonymous` |
| platform admin | `Admin` | `admin:{admin}:v{n}` | `AdminSignedIn` |
| health, SignalR hubs, anything else | `Exempt` | none | none |

Important: **the options are read only when a key's counter is created.** A counter that already exists keeps its old numbers. That is what the `v{n}` at the end of each key is for (section 3).

Counters for keys that have gone quiet are removed by the framework after a while, so old keys do not pile up in memory.

### A global limiter and per-endpoint policies

- `options.GlobalLimiter` runs for every request. Task 07 puts all its partitions here.
- `options.AddPolicy("name", ...)` defines a named policy. You attach it to a controller or action with `[EnableRateLimiting("name")]`, or turn limiting off for one endpoint with `[DisableRateLimiting]`. A request must pass the global limiter *and* the endpoint policy. Task 07 has no named policies.
- **Chaining:** `PartitionedRateLimiter.CreateChained(a, b)` makes one limiter from several, and a request needs a permit from every one of them. Task 07 chains the caller's own limit with `TenantCeiling`, so every user and device request also counts against its tenant.

### `OnRejected`

`options.OnRejected` runs when a request is refused. In task 07 it is `WriteRejectionAsync` in `ApiRateLimitingExtensions`. It:

- sets status 429;
- sets `Retry-After` from the limiter's metadata (`MetadataName.RetryAfter`), so clients know when to try again;
- writes the problem body `urn:crownus:error:rate-limit`;
- logs the warning `API rate limit exceeded` with the subject kind and tenant id, but not the IP or user id.

It cannot tell whether the caller's own limit or the tenant ceiling refused the request. Task 49 counts rejections here too.

### Queueing

Each limiter has `QueueLimit` and `QueueProcessingOrder`. With a queue, an over-limit request *waits* for a permit instead of getting 429 straight away. Task 07 sets `QueueLimit = 0`. Keep it that way: a waiting request still holds a connection and a thread-pool slot, so under a flood a queue makes things worse. Do not expose the queue in the admin app.

### What it cannot do

- **No shared counters.** Counts live in the memory of each API process. Nothing is shared between servers (section 3).
- **No live change of an existing counter's numbers.** See "Partitions" above.
- **No `RateLimit-*` headers** are added for you. Only what `OnRejected` writes is sent.
- **No built-in admin screen, storage or audit.** That is what tasks 45 to 48 add.
- It runs where you put it in the pipeline. Task 07 puts it right after `UseAuthentication`, so it knows who the caller is. `AuthRateLimitingMiddleware` runs before it.

---

## 2. How far admin control can go

Today every number comes from the `RateLimiting` section of `appsettings` (class `RateLimitSettings`), through `IRateLimitSettingsProvider` and its implementation `OptionsRateLimitSettingsProvider`. The limiter calls `GetSettings(tenantId)` on every request: with the tenant of the user or tray device, or with `null` for anonymous and admin requests. That one call is the hook for everything below.

| Level | What the admin can change | Example | In these tasks |
|---|---|---|---|
| **Global** | Any of the six limits: `Anonymous`, `SignedIn`, `TrayDevice`, `TenantCeiling`, `AdminAnonymous`, `AdminSignedIn`. Replaces the `appsettings` value | "Signed-in users get 900 per minute" | Yes (45, 47, 48) |
| **Per plan** | `SignedIn`, `TrayDevice` and `TenantCeiling` for every tenant on a subscription plan | "Enterprise plan: tenant ceiling 30,000 per minute" | Yes |
| **Per tenant** | The same three, for one tenant | "Acme runs a big import this week: user limit 1,500" | Yes |
| **Per endpoint** | A named policy on one endpoint, for example a stricter limit on an export | "Payroll export: 5 per minute per user" | Not in these tasks. Possible later: add a policy name to the table and a property to `RateLimitSettings`. The *list* of endpoint policies stays in code, because attaching a policy is a code change |
| **Live change** | All of the above apply without a restart | Change in the admin app: applies at once on that server, within 30 s on others | Yes (46) |

Precedence, the first match wins: **tenant override, then plan override, then global row, then `appsettings`.** An empty table behaves exactly like today.

Only three limits can be overridden per plan or tenant, because the limiter reads only `SignedIn`, `TrayDevice` and `TenantCeiling` from a tenant's snapshot. `Anonymous`, `AdminAnonymous` and `AdminSignedIn` always come from `GetSettings(null)`: there is no tenant yet for an anonymous caller, and the admin rules protect the admin API itself.

What admins can change is deliberately narrow: **"per minute", and "burst" for token-bucket limits.** The algorithm, the refill period (`ReplenishmentPeriodSeconds`), the window (`WindowSeconds`, `SegmentsPerWindow`) and the partition keys stay in code and `appsettings`. Picking a 1-hour window by mistake could make the API either useless or unprotected, and those choices need a developer's review anyway.

Not editable, at any level:

- `RateLimiting:Enabled`, the master switch (section 3).
- The login, MFA, password reset and invitation limits (section 3).

---

## 3. Hard limits and rules

### Counters are per server until task 21

Each API process counts on its own. If UAT runs two instances behind a load balancer and `SignedIn` allows 600 a minute, a user can get up to about 1,200: 600 on each server. The same applies to the tenant ceiling. Task 21, [Shared state for running more than one instance](<../../../Improvements/person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md>), moves counters to a shared store (Redis). Until then:

- set limits for **one** server's share of the traffic;
- say so on the admin screen (task 48 has the sentence);
- the rejection counts in task 49 are per server too.

The overrides come from the database, so every server has the same numbers. Only the counting is per server.

### A changed limit needs a new version in the key

Because a counter keeps the options it was created with (section 1), changing a number does nothing to counters that already exist. Task 07 solves this with the version: `GetSettings(tenantId)` returns a `RateLimitSettingsSnapshot(long Version, RateLimitSettings Settings)`, and the limiter puts `Version` at the end of the key (`user:{tenant}:{user}:v{n}`).

1. An admin saves a new value for tenant Acme.
2. The provider reloads, sees that Acme's limits changed, and gives Acme's snapshot version n + 1 (task 46).
3. The next request from an Acme user builds the key `user:{acme}:{user}:v{n+1}`. That key is new, so a fresh counter is created with the new numbers.
4. The old counters go quiet and the framework removes them.

Because `GetSettings` is called with the tenant, **the version can be kept per tenant**:

- A tenant or plan change resets the counters of the affected tenants only.
- A global change resets everyone who inherits it.
- The provider must add 1 **only when the result really changed**, as `OptionsRateLimitSettingsProvider` already does with its fingerprint. Otherwise the 30-second check in task 46 would reset everyone every 30 seconds.
- A tenant's version must never go down, even when its last override is removed.
- Still, do not save on a timer or in a loop. Every real save resets someone's counters, which is one more reason to audit every change.

### `Enabled` and the auth limits stay out of reach

- `RateLimitSettings.Enabled = false` turns the global limiter off. The limiter reads it from the same per-tenant snapshot, so a provider that copied it from the database could switch limiting off for one tenant. **The database provider always takes `Enabled` from `appsettings`, and no table column or endpoint exists for it.**
- `AuthRateLimitingMiddleware` holds the strict limits on login, MFA, forgot and reset password, Google sign-in, invitation accept and two tray endpoints (for example 5 login attempts per email per 5 minutes). It runs before the global limiter. These limits stop password guessing and code guessing. **They are never editable from the admin app.** If an admin account were taken over, the attacker must not be able to turn brute-force protection off. The API in task 47 accepts only the six limit names, as a closed enum.

### No zero, no "unlimited"

Every limit has a floor and a ceiling (section 4). They are enforced in four places:

- the API validator (task 47);
- the database check constraint (task 45);
- a clamp in the provider (task 46);
- `RateLimitSettings.Validate()`, which already rejects anything below 1.

The `AdminSignedIn` and `AdminAnonymous` floors are high enough that an admin cannot lock themselves out of the admin app.

---

## 4. Defaults and bounds

Defaults are task 07's `appsettings` values (`docs/deployment/rate-limits.md`). All numbers are per server (section 3).

| Limit | Counted by | Algorithm | Default | Per minute: min to max | Burst: min to max | Plan or tenant override? |
|---|---|---|---|---|---|---|
| `Anonymous` | IP, `/api/v1` | Sliding window, 60 s, 6 segments | 300/min | 60 to 1,200 | none | No, global only |
| `SignedIn` | User | Token bucket | burst 100, 600/min | 120 to 3,000 | 30 to 500 | Yes |
| `TrayDevice` | Tray device | Token bucket | burst 60, 300/min | 60 to 1,200 | 20 to 300 | Yes |
| `TenantCeiling` | Tenant, all users and devices together | Token bucket, chained | burst 3,000, 12,000/min | 1,200 to 120,000 | 500 to 20,000 | Yes. Both numbers must be at least the effective `SignedIn` ones |
| `AdminAnonymous` | IP, `/admin/v1` | Sliding window, 60 s, 6 segments | 120/min | 60 to 600 | none | No, global only |
| `AdminSignedIn` | Platform admin | Token bucket | burst 60, 300/min | 120 to 2,400 | 30 to 300 | No, global only |

How the two admin numbers map to the task 07 shapes (task 46 does this):

- **Token bucket:** `TokenLimit = burst` and `TokensPerPeriod = perMinute × ReplenishmentPeriodSeconds / 60`. With the default 1-second period, "per minute" must be a multiple of 60. All the bounds above are.
- **Sliding window:** `PermitLimit = perMinute × WindowSeconds / 60`. With the default 60-second window, that is the number itself.

Why the defaults are what they are, so you can explain them to an admin:

- **`SignedIn`, burst 100 and 600/min:** a dashboard load is about 15 requests at once. 100 covers several fast page loads in a row, and 600 a minute (10 a second) still stops a runaway loop. Task 07's tests show 50 users on one IP loading the dashboard together get no 429.
- **`Anonymous`, 300/min per IP:** anonymous calls are the login page and a few public lookups. Signed-in users no longer share it, so an office behind one IP is fine.
- **`TenantCeiling`, 12,000/min:** a safety net against one tenant swamping a server, not a per-user limit. That is 20 users at full speed.
- **Admin, 300/min signed in and 120/min anonymous:** busy admin screens fit inside it, and the anonymous rule covers the admin login page.

Database check constraint (task 45): one absolute range for every limit, as a last safety net. "Per minute" is 10 to 200,000 and "burst" is 1 to 50,000. The tighter bounds in the table live in one code class, `RateLimitBounds`, used by the validator, the provider clamp and the GET response.

---

## 5. Build order

1. **Task 45: table.** Add `rate_limit_settings` and `rate_limit_setting_changes`:
   - check constraints and filtered unique indexes;
   - RLS policies (`TenantOrSystem`, because the tables have a nullable `tenant_id`);
   - no seed rows.

   Nothing uses the tables yet.
2. **Task 46: provider.** Add `DatabaseRateLimitSettingsProvider` and register it before `AddApiRateLimiting`, which registers the default with `TryAddSingleton`. It:
   - builds the snapshots from `appsettings` plus the stored overrides;
   - keeps a version per tenant;
   - clamps values;
   - reloads on save and every 30 seconds.

   With an empty table, the API behaves exactly as before.
3. **Task 47: admin API.** Add the read and write endpoints, validation, `rowVersion` checks, a history row per change, and invalidation after save. Test it with Postman or the integration tests before any UI exists.
4. **Task 48: admin screens.** Add a global page under Platform Configuration, a card on plan detail and a panel on tenant detail, all in the admin frontend repository.
5. **Task 49 (optional): rejection counts.** Count 429s in `WriteRejectionAsync`, add a read endpoint and a section on the screens.

Each step can be merged on its own. After step 2 nothing changes for users. After step 3 a developer can change limits. After step 4 an admin can.

---

## 6. How to test it locally

### Automated

- Backend while iterating: `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~RateLimit"`. Task 07's tests are in `tests/ONEVO.Tests.Unit/Features/Api/RateLimiting/ApiRateLimitingTests.cs`. They run the real limiter on a TestServer, and new tests can reuse that setup.
- Backend full gate, needs Docker for SQL Server: `pwsh ../dev-env/scripts/verify.ps1 backend -Full`.
- Unprotected endpoints: `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` must report 0.
- Admin frontend: `npm test`, `npm run lint` and `npm run e2e` in `HRMS-Platform-Administration-Front-End-v1`.

### By hand

1. Apply the task 45 migration to **your own local** database only. Never apply it to a shared database yourself.
2. Start the API (`https`, port 7229). Start the admin app with `npm start` and open it at `https://admin.localhost:4300`. The host name matters for the cookies (see `src/environments/environment-url.helper.ts` in that repo).
3. Sign in to the admin app, open **Platform Configuration → Rate Limits** and check that every limit shows its `appsettings` value as the source.
4. Open a test tenant and set its `SignedIn` override to the floor (burst 30, 120 per minute) with a reason.
5. In the tenant app, sign in as a Development seed user of that tenant. In the browser console, send 50 calls at once to a cheap GET:

   ```js
   const r = await Promise.all([...Array(50)].map(() => fetch('/api/v1/<cheap-get>', { credentials: 'include' })));
   console.table(r.reduce((a, x) => (a[x.status] = (a[x.status] || 0) + 1, a), {}));
   ```

   You should see about 30 × 200 and the rest 429. A few more may pass, because the bucket refills 2 per second. A 429 response has a `Retry-After` header.
6. Remove the override in the admin app and run the burst again straight away. All 50 should pass, because the default burst is 100. The change applied without a restart.
7. Sign in as a user of another tenant during step 5. Their requests are not affected, because only the changed tenant got a new version.
8. Check the change history on the Rate Limits page: two rows, with your name and reasons.
9. Optional, to see the per-server limit: start a second API instance on another port and send half the burst to each. Each server allows its own 30.
10. Try to break it:
    - save `perMinute = 0`;
    - save `SignedIn` with `perMinute = 100000`;
    - save `SignedIn` with no burst;
    - call `PUT .../rate-limits/Enabled` and `PUT .../rate-limits/login` with Postman.

    All must fail with 400. A platform user without `platform.system_config.manage` must get 403.
