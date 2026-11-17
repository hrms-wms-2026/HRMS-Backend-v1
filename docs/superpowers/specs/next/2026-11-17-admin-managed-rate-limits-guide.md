# Admin-managed rate limits: guide

**Status:** Proposed 2026-11-17. Not built. Tasks 45 to 49 in `docs/Improvements/person-2-performance-and-db/`.
**For:** Person 2 (admin app owner). No rate limiting background is assumed.
**Builds on:** task 07, [Fix the global rate limiter](<../../../Improvements/person-1-security-and-auth/task-07 Fix the global rate limiter.md>), which is being built now. Names in this guide marked "names per task 07" come from the task 07 plan. If its report, `docs/reports/security/2026-11-17-task-07-rate-limiter.md`, uses other names when it lands, use those.

## The tasks

| # | Task | What it gives you |
|---|---|---|
| 45 | [Rate limit settings table](<../../../Improvements/person-2-performance-and-db/task-45 Rate limit settings table.md>) | Where the limits and their change history are stored |
| 46 | [Rate limit settings from the database](<../../../Improvements/person-2-performance-and-db/task-46 Rate limit settings from the database.md>) | The limiter reads the stored limits, from memory, and picks up changes |
| 47 | [Admin API for rate limits](<../../../Improvements/person-2-performance-and-db/task-47 Admin API for rate limits.md>) | Endpoints to read and change limits, with checks and audit |
| 48 | [Admin app screens for rate limits](<../../../Improvements/person-2-performance-and-db/task-48 Admin app screens for rate limits.md>) | The screens in the platform admin app |
| 49 | [Rate limit rejection counts for admins](<../../../Improvements/person-2-performance-and-db/task-49 Rate limit rejection counts for admins.md>) | Optional: who is being limited right now |

---

## 1. What rate limiting is, and what `AddRateLimiter` can do

A rate limiter counts requests and says "no" (HTTP 429 Too Many Requests) when a caller sends too many in a period. It protects the API and the database from one caller using all the capacity: a buggy page polling in a loop, a script, or an attack.

Two words come up all the time:

- **Partition:** whose requests are counted together. "Per IP address" means everyone behind one IP shares one counter. "Per user" means each user has their own. Task 07 exists because the old limiter counted per IP, so a whole office shared 300 requests a minute.
- **Permit:** one allowed request. A limit of "600 per minute" means 600 permits per minute.

ASP.NET Core has a built-in rate limiter (`builder.Services.AddRateLimiter(...)` and `app.UseRateLimiter()`). Task 07 switches to it.

### The four algorithms

| Algorithm | How it counts | Good for | Weak point |
|---|---|---|---|
| **Fixed window** | Up to N requests per clock window (for example 12:00:00 to 12:00:59), then the counter resets | Simple, cheap limits | A burst at the edge: N at 12:00:59 and N more at 12:01:00, so 2N in two seconds |
| **Sliding window** | The window is split into segments (for example 6 segments of 10 s). Old segments drop off one by one | Strict limits where edge bursts matter, such as anonymous traffic | A little more memory per partition |
| **Token bucket** | A bucket holds up to N tokens. Each request takes one. Tokens are added back at a steady rate | Real users: a page load can burst, but the steady rate stays bounded | Harder to explain in a screen label |
| **Concurrency** | At most N requests *in progress at once*, no time window | Protecting a slow endpoint (an export, a report) | Does not limit how many requests come in over time |

Task 07 uses a sliding window for anonymous callers and a token bucket for signed-in users (names per task 07).

### Partitions

You give the limiter a function that looks at the request and returns a **key**, for example `user:{userId}` or `ip:{address}`. The limiter keeps one small counter per key. The first request with a new key creates that key's counter, using the options you return at that moment.

Important: **the options are read only when a key's counter is created.** A counter that already exists keeps its old numbers. This is why section 3 needs the "version in the key" trick.

Counters for keys that have gone quiet are removed by the framework after a while, so old keys do not pile up in memory.

### A global limiter and per-endpoint policies

- `options.GlobalLimiter` runs for every request. Task 07 puts its partitions here.
- `options.AddPolicy("name", ...)` defines a named policy. You attach it to a controller or action with `[EnableRateLimiting("name")]`, or turn limiting off for one endpoint with `[DisableRateLimiting]`. A request must pass the global limiter *and* the endpoint policy.
- **Chaining:** `PartitionedRateLimiter.CreateChained(a, b, c)` makes one limiter from several. A request needs a permit from every one of them. Task 07 uses this to apply the per-user limit and the per-tenant ceiling to the same request.

### `OnRejected`

`options.OnRejected` runs when a request is refused. Task 07 uses it to:

- set status 429;
- set the `Retry-After` header from the limiter's metadata (`MetadataName.RetryAfter`), so clients know when to try again;
- write the same problem body the API sends today (`type` from `ProblemTypes.For("rate-limit")`).

Task 49 also counts rejections here.

### Queueing

Each limiter has `QueueLimit` and `QueueProcessingOrder`. With a queue, an over-limit request *waits* for a permit instead of getting 429 straight away. For this API keep `QueueLimit = 0`. A waiting request still holds a connection and a thread-pool slot, so under a flood a queue makes things worse. Do not expose the queue in the admin app.

### What it cannot do

- **No shared counters.** Counts live in the memory of each API process. Nothing is shared between servers (section 3).
- **No live change of an existing counter's numbers.** See "partitions" above.
- **No `RateLimit-*` headers** are added for you. Only what you write in `OnRejected` is sent.
- **No built-in admin screen, storage or audit.** That is what tasks 45 to 48 add.
- It runs where you put it in the pipeline. Task 07 puts it after `UseAuthentication`, so it knows who the user is. Anything before that point (static files, health checks) is not limited by it.

---

## 2. How far admin control can go

| Level | What the admin can change | Example | In these tasks |
|---|---|---|---|
| **Global** | The default for every partition: `anonymous`, `user`, `tray`, `tenant`, `admin` | "Signed-in users get 600 per minute" | Yes (45, 47, 48) |
| **Per plan** | `user`, `tray` and `tenant` for every tenant on a subscription plan | "Enterprise plan: tenant ceiling 20,000 per minute" | Yes |
| **Per tenant** | The same three, for one tenant | "Acme runs a big import this week: user limit 1,500" | Yes |
| **Per endpoint** | A named policy on one endpoint, for example a stricter limit on an export | "Payroll export: 5 per minute per user" | Not in these tasks. Possible later: add a policy name to the table, and look its number up in the policy's partition function. The *list* of endpoint policies stays in code, because attaching a policy is a code change |
| **Live change** | All of the above apply without a restart | Change in the admin app, applies at once on that server, within 30 s on others | Yes (46) |

Precedence, the first match wins: **tenant override, then plan override, then global, then the code default.**

What admins can change is deliberately narrow: **one number, permits per minute, per partition.** The algorithm, the window length and the partition keys stay in code. An admin choosing "fixed window" or a 1-hour window by mistake could make the API either useless or unprotected, and those choices need a developer's review anyway.

Not editable, at any level:

- `anonymous` and `admin` per plan or tenant. Anonymous traffic is counted by IP before anyone is signed in, and the admin rule protects the admin API itself. Both are global only.
- The login, MFA, password reset and invitation limits (section 3).

---

## 3. Hard limits and rules

### Counters are per server until task 21

Each API process counts on its own. If UAT runs two instances behind a load balancer and the user limit is 600, a user can get up to about 1,200 requests a minute: 600 on each server. The same applies to the tenant ceiling. Task 21, [Shared state for running more than one instance](<../../../Improvements/person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md>), moves counters to a shared store (Redis). Until then:

- set limits for **one** server's share of the traffic;
- say so on the admin screen (task 48 has the sentence);
- the rejection counts in task 49 are per server too.

The settings themselves come from the database, so every server has the same numbers. Only the counting is per server.

### A changed limit needs the version in the key

Because a counter keeps the options it was created with (section 1), changing a number in the database does nothing to counters that already exist. Task 07 solves this by putting a **settings version** in every key, for example `user:{id}:v{version}` (names per task 07):

1. An admin saves a new value.
2. The provider reloads and adds 1 to the version (task 46).
3. The next request builds the key `user:{id}:v{version+1}`. That key is new, so a fresh counter is created with the new numbers.
4. The old counters go quiet and the framework removes them.

What follows from this:

- Every save **resets every caller's count**, not only the callers the change was for. That is fine for a rare admin action, and it is one more reason to audit every change. Do not save on a timer or in a loop.
- Task 46 must add 1 to the version **only when the data really changed**. Otherwise its 30-second check would reset everyone every 30 seconds.

### Auth limits stay in code

`AuthRateLimitingMiddleware` holds the strict limits on login, MFA, forgot and reset password, Google sign-in, invitation accept and some tray endpoints (for example 5 login attempts per email per 5 minutes). These stop password guessing and code guessing. **They are never editable from the admin app, and there is no "off" switch for any limit.** If an admin account were taken over, the attacker must not be able to turn brute-force protection off. The API in task 47 has no partition for them, and the partition list is a closed enum.

### No zero, no "unlimited"

Every partition has a floor and a ceiling (section 4). They are enforced in three places: the API validator (task 47), the database check constraint (task 45) and a clamp in the provider (task 46). The admin's own `admin` limit has a floor high enough that an admin cannot lock themselves out of the admin app.

---

## 4. Suggested defaults and bounds

All numbers are permits per minute, per server (see section 3). Where task 07's `appsettings` uses other defaults, keep task 07's defaults and use these bounds.

| Partition | Counted by | Algorithm (code-fixed) | Suggested default | Min | Max | Plan or tenant override? |
|---|---|---|---|---|---|---|
| `anonymous` | IP address | Sliding window, 6 segments | 60 | 20 | 600 | No, global only |
| `user` | Session or user id | Token bucket | 600 | 120 | 3,000 | Yes |
| `tray` | Tray device id | Token bucket | 120 | 30 | 1,200 | Yes |
| `tenant` | Tenant id (ceiling for all its users together) | Token bucket | 6,000 | 1,000 | 60,000 | Yes. Must be at least the effective `user` limit |
| `admin` | Platform admin session or user | Token bucket | 300 | 120 | 2,000 | No, global only |

For a token bucket, the one number is both the bucket size (the largest burst) and the refill per minute. For a sliding window, it is the number allowed in any 60-second span. Task 46 maps it to the limiter options so admins only ever see "per minute".

How the defaults were picked:

- **user 600:** one dashboard load makes many API calls (task 20). 600 a minute is 10 a second, enough for fast clicking with room to spare, and still stops a runaway loop.
- **anonymous 60:** anonymous calls are a few public pages and the tenant lookup. 60 a minute from one IP is plenty for an office of people opening the login page.
- **tenant 6,000:** about ten users at full speed, or many users at normal speed. It catches a whole tenant's scripts going wrong without touching normal use.
- **admin 300:** the old rule was 120 per IP. Per admin session, 300 allows busy screens.

Database check constraint (task 45): one absolute range for all partitions, **10 to 100,000**, as a last safety net. The tighter per-partition bounds above live in one code class, `RateLimitBounds`, used by the validator, the provider clamp and the GET response.

---

## 5. Build order

1. **Task 45: table.** Add `rate_limit_settings` and `rate_limit_setting_changes`, with check constraints, filtered unique indexes, RLS policies (`TenantOrSystem`, because the tables have a nullable `tenant_id`) and seeded global rows. Nothing uses them yet.
2. **Task 46: provider.** Swap the `IOptionsMonitor` provider for a database-backed one that keeps a snapshot in memory, resolves precedence, clamps values, and adds 1 to the version on a real change. Keep the `IOptionsMonitor` one as the fallback. After this step the API behaves exactly as before, but reads from the database.
3. **Task 47: admin API.** Add read and write endpoints, validation, `rowVersion` checks, a history row per change, and invalidation after save. Test it with Postman or the integration tests before any UI exists.
4. **Task 48: admin screens.** Add a global page under Platform Configuration, a card on plan detail and a panel on tenant detail, all in the admin frontend repository.
5. **Task 49 (optional): rejection counts.** Count 429s in `OnRejected`, add a read endpoint and a section on the screens.

Each step can be merged on its own. After step 2 nothing changes for users. After step 3 a developer can change limits. After step 4 an admin can.

---

## 6. How to test it locally

### Automated

- Backend while iterating: `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~RateLimit"`
- Backend full gate, needs Docker for SQL Server: `pwsh ../dev-env/scripts/verify.ps1 backend -Full`
- Unprotected endpoints: `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` must report 0.
- Admin frontend: `npm test`, `npm run lint`, `npm run e2e` in `HRMS-Platform-Administration-Front-End-v1`.

### By hand

1. Apply the task 45 migration to **your own local** database only. Never apply it to a shared database yourself.
2. Start the API (`https`, port 7229). Start the admin app with `npm start` and open it at `https://admin.localhost:4300`. The host name matters for the cookies (see `src/environments/environment-url.helper.ts` in that repo).
3. Sign in to the admin app, open **Platform Configuration → Rate Limits** and check that the values match `appsettings`.
4. Open a test tenant, and set its `user` override to the floor (120) with a reason.
5. In the tenant app, sign in as a Development seed user of that tenant. In the browser console, fire a burst, for example 150 calls to a cheap GET such as the current-user endpoint:

   ```js
   const r = await Promise.all([...Array(150)].map(() => fetch('/api/v1/<cheap-get>', { credentials: 'include' })));
   console.table(r.reduce((a, x) => (a[x.status] = (a[x.status] || 0) + 1, a), {}));
   ```

   You should see about 120 × 200 and the rest 429. A 429 response has a `Retry-After` header.
6. Remove the override in the admin app and run the burst again straight away. All 150 should pass: the change applied without a restart.
7. Check the change history on the Rate Limits page: two rows, with your name and reasons.
8. Optional, to see the per-server limit: start a second API instance on another port and send half the burst to each. Each server allows its own 120.
9. Try to break it: save 0, save 100,000 for `user`, call `PUT .../rate-limits/login` with Postman. All must fail with 400. A platform user without `platform.system_config.manage` must get 403.
