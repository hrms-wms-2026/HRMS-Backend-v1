# Backend load test — 2026-11-11

Local load test of the HRMS backend (`ONEVO.Api`, PID 42788) while it ran in **Debug via `dotnet run`** on the developer machine. Measures response time, throughput, RAM and SQL connections from 0 to 100 simulated users.

> **Short verdict (Tanglish):** Errors **0**, rate-limit 429 **0**, RAM leak **illa**. Aana **performance romba slow**: ~10 users-laye p95 1 s thaandiduchu, 25 users-la p95 4 s, 100 users-la p95 ~10.6 s. Server throughput **~10-14 req/s-la stuck aagiduchu**, CPU ~0.5 core thaan use aachu. Athaavathu CPU illa, **ethoo blocking/wait** thaan bottleneck. Ithu Debug + full SQL console logging-oda measure pannathu. Production-ku munnadi Release mode-la thirumba test pannanum (kizha "Next steps" paarunga).

## Setup

| Item | Value |
|---|---|
| Target | `https://dapi.crownus.test:7229/api/v1` (tenant `dapi`) |
| Build | Debug, `dotnet run`, Development config (Serilog `Default: Debug`, EF SQL commands `Information` → synchronous Console sink) |
| Machine | Single Windows dev laptop, 12 logical CPUs. ng serve (~1.7 GB), SQL Server, Claude sessions running on the same box. Machine CPU ~50 % busy during the test |
| Tool | `loadtest-runner.mjs` (Node, no deps). k6 couldn't be downloaded in this session; `k6-dashboard-load.js` has the same mix for later runs |
| User | One seeded test account (`kali.senanayake@dapi.test`, dev legal docs accepted for it), session cookie shared by all VUs |
| Per-VU IP | Each VU sent its own `X-Forwarded-For` (10.50.x.y). Loopback is a trusted proxy by default, so each VU counted as a separate client for the per-IP limiter |
| Request mix (one "page load") | `auth/me`, `dashboards/me`, `dashboards/{id}/bootstrap`, `notifications/unread-count`, `monitoring/activation/status`, `notifications`, `work/my-weekly-progress`, `dashboard/team/capabilities`, `work/projects/mine`, `work/projects/{id}/work-graph`. Then 1-3 s think time, repeat |
| Stages | 60 s → 10 VUs, 60 s → 25, 120 s → 50, 120 s → 100, 30 s → 0 (6.5 min). 60 s idle baseline before, 10 min idle after |

## Results — overall

| Metric | Value |
|---|---|
| Total requests | 3,724 |
| Avg throughput | **9.5 req/s** (peak 10 s window: 14.6 req/s) |
| HTTP 200 | 3,724 (100 %) |
| Errors (5xx / 4xx / timeouts) | **0** |
| 429 (rate limiter) | **0** |
| Latency p50 / p95 / p99 / max | 3,985 / **9,870** / 11,420 / 15,774 ms |
| Threshold p95 < 500 ms | ❌ failed |
| Threshold errors < 1 % | ✅ passed |

## Where it slows down (by stage)

| Users (stage target) | Req/s | p50 | p95 | p99 |
|---|---|---|---|---|
| ramp to 10 | 7.4 | 470 ms | 1,065 ms | 1,812 ms |
| ramp to 25 | 8.5 | 1,375 ms | 4,183 ms | 5,387 ms |
| ramp to 50 | 7.6 | 4,022 ms | 8,582 ms | 9,945 ms |
| ramp to 100 | 11.7 | 5,174 ms | 10,603 ms | 11,932 ms |

10-second timeline highlights: 2-5 users → p95 0.66-0.88 s; **8 users → p95 1.65 s** (first time over 1 s); 15 users → 2.1 s; 25 users → 5.0 s; 60 users → 12.5 s.

**Knee point: about 8-10 concurrent active users.** After that throughput stops rising (flat ~8-14 req/s) and extra users only add queueing time.

## Per endpoint (whole run)

| Endpoint | n | p50 ms | p95 ms | p99 ms | max ms |
|---|---|---|---|---|---|
| `dashboards/{id}/bootstrap` | 395 | 8,186 | **11,692** | 13,575 | 15,774 |
| `work/projects/{id}/work-graph` | 338 | 6,130 | 10,187 | 10,964 | 13,618 |
| `work/projects/mine` | 346 | 5,947 | 9,733 | 10,385 | 12,139 |
| `work/my-weekly-progress` | 359 | 5,078 | 8,574 | 10,409 | 11,227 |
| `dashboard/team/capabilities` | 353 | 4,430 | 8,284 | 10,325 | 11,983 |
| `auth/me` | 413 | 3,608 | 5,747 | 6,307 | 7,231 |
| `monitoring/activation/status` | 371 | 2,890 | 5,253 | 8,916 | 10,846 |
| `notifications` | 368 | 2,988 | 5,252 | 7,239 | 10,096 |
| `notifications/unread-count` | 377 | 3,101 | 5,250 | 8,161 | 10,460 |
| `dashboards/me` | 404 | 3,113 | 5,146 | 7,692 | 11,243 |

Single-request latency with no load (curl, warm): 0.10-0.35 s for these endpoints. So most of the loaded latency is **queueing**, not the work itself. `dashboards/{id}/bootstrap` is the heaviest call and the first one to optimise.

## RAM, CPU, threads, SQL connections (ONEVO.Api)

Sampled every 5 s with `sample-process.ps1` → `process-samples.csv`.

| Moment | Working set | Private | Threads | SQL conns (TCP :1433) |
|---|---|---|---|---|
| Before (13:31, idle baseline) | 487 MB | 290 MB | 36-41 | 9 |
| Peak RAM (13:34:45, ~35 users) | **554 MB** | **353 MB** | 73 | 36 |
| End of load (13:38:30, 100 → 0 users) | 513 MB | 313 MB | 124-128 | **81** |
| 10 min after load (13:48:50) | **462 MB** | **259 MB** | 36 | 7 |

- **CPU:** ONEVO.Api used 250 CPU-seconds over 7.5 min ≈ **0.55 of one core** on a 12-core machine. CPU is not the limit.
- **Threads 36 → 128:** the .NET thread pool kept adding threads while throughput stayed flat. That is the classic **thread-pool starvation** pattern: request threads are blocked waiting on something synchronous, not doing work.
- **SQL connections 9 → 81:** default SqlClient pool max is 100. At 100 users the pool was close to exhaustion. If it runs out, requests wait up to the 15 s connect timeout and then fail.
- **RAM:** rose only ~65 MB at peak and came back down while load was still running. 10 min after load it settled at 462 MB / 259 MB private, **below the pre-test baseline** (487 / 290). Threads went back to 36 and SQL connections to 7. **No memory leak.**

> GC heap (gen2 / committed) and thread-pool queue length could not be recorded: dotnet-counters allows only one session per process, and the user's own `dotnet-counters monitor` in Terminal tab 0 already held it.

## Rate limiter finding (production risk)

`PublicApiRateLimitingMiddleware` limits **all `/api/v1/*` requests, including logged-in ones, to 300 per minute per client IP** (`/admin/v1`: 120). The test did not hit it only because each VU used its own forwarded IP.

In production, an office where all employees share one NAT IP shares those 300 requests per minute. One dashboard load here is 10 requests, and the app also polls (unread-count, activation status). So **roughly 15-30 active users in one office can start getting 429s.** Consider limiting per user/tenant for authenticated requests, or raising the per-IP limit, before go-live. Also make sure production `ForwardedHeaders:KnownProxies` lists the real reverse proxy, or every user will appear as the proxy's IP and share one bucket.

## Likely cause of the slowness (hypothesis, not proven)

1. **Synchronous console logging in Development.** `appsettings.Development.json` sets Serilog `Default: Debug` and `Microsoft.EntityFrameworkCore.Database.Command: Information`, writing every SQL statement (some 45-column SELECTs) to a synchronous Console sink. Console writes are serialised behind a lock and slow in a Windows terminal. Low CPU + rising threads + flat throughput fits this. Production config (`appsettings.json`) uses `Information` / EF `Warning`, so this may mostly disappear in production.
2. **Repeated per-request queries.** Every request re-reads `tenant_subscriptions` (and tray device registration) — 2+ extra queries per call. Caching for 1-5 min would cut DB round-trips a lot.
3. **Heavy endpoints:** `dashboards/{id}/bootstrap`, `work-graph`, `projects/mine`.
4. Debug build + shared dev machine (ng serve, SQL Server, other tools on the same CPU).

## Next steps

1. **Re-run in Release with production log levels** (most important — tells whether #1 is the real cause):
   `dotnet run -c Release --project src/ONEVO.Api` with `ASPNETCORE_ENVIRONMENT` set so EF command logging is `Warning`, then run the same runner:
   `node loadtest-runner.mjs` with `BASE`, `COOKIE`, `DASH_ID`, `PROJECT_ID`, `INSECURE=1` (see the script header).
2. Stop `dotnet-counters monitor` in Terminal tab 0 during the run so `dotnet-counters collect -p <pid> --format csv` can record GC heap and thread-pool queue.
3. If still slow in Release: capture `dotnet-trace` / `dotnet-stack` during load to find the blocking call, and cache the `tenant_subscriptions` lookup.
4. Fix the per-IP rate limit for authenticated traffic before production.
5. Run the final test on a staging server sized like production, from a separate machine.

## Files

- `loadtest-runner.mjs` — runner used for this report
- `k6-dashboard-load.js` — same test for k6
- `sample-process.ps1` — RAM / CPU / SQL connection sampler
- `loadtest-summary.json` — full results incl. 10 s timeline
- `loadtest-run.log`, `process-samples.csv` — raw output
