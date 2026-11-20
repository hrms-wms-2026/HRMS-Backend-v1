# Load test, local Release build (2026-11-20)

Task 29. First runs of `ops/loadtest/journeys.js` (k6). The gate and what to do next:
[docs/deployment/release-gate.md](../../deployment/release-gate.md).

## Setup

| Item | Value |
|---|---|
| Build | Release, `dotnet run -c Release`, Development settings with Serilog and EF logging at Warning (`backend-api-loadtest` launch config) |
| Branch | `chore/task-29-load-test` (tasks 01 to 25 included; tasks 00a and 00b not merged) |
| Database | local SQL Server container, tenant `dapi` (development seed data) |
| Machine | the development laptop, 12 logical CPUs, other tools running |
| Tool | k6 0.54.0 from the `grafana/k6` image, TLS verified against the local mkcert root |
| Accounts | 25 seeded `@dapi.test` test accounts, one per virtual user (round robin), legal documents accepted by the script (`ACCEPT_TEST_LEGAL=1`) |
| Session | dashboard, task board, employee list, attendance today, leave balances, inbox; 1 to 3 s between pages |
| Sampling | every 5 s: API process CPU (% of the machine), working set, threads; SQL Server sessions of `crownus_app` |

Raw data in [2026-11-20-load-test/](2026-11-20-load-test/): k6 summaries (metrics only) and process samples per run.

## Results

| Run | Users | Length | Requests | Throughput | Failed | Overall p95 |
|---|---|---|---|---|---|---|
| normal | 20 | 6.5 min | 5,510 | 12.3/s | 0 % | 1,748 ms |
| peak | 60 | 7.5 min | 14,150 | 28.0/s | 0 % | 2,919 ms |
| soak | 20 | 23 min (20 min plateau) | 20,018 | 14.0/s | 0 % | 1,712 ms |

The soak ran 20 minutes, not 60: the local API runs under the session's preview tool, which stops when the session
ends. The 60-minute soak is part of the UAT run.

### p95 per endpoint (ms)

| Endpoint | normal | peak | soak |
|---|---|---|---|
| `GET /auth/me` | 46 | 96 | 46 |
| `GET /dashboards/me` | 95 | 39 | 18 |
| `GET /dashboards/{id}/bootstrap` | 177 | 429 | 178 |
| `GET /notifications/unread-count` | 14 | 37 | 15 |
| `GET /work/projects/mine` | 124 | 314 | 128 |
| `GET /work/projects/{id}/work-graph` | 120 | 285 | 119 |
| `GET /employees` | 79 | 166 | 75 |
| `GET /attendance/time-tracking/today` | 102 | 232 | 105 |
| `GET /leave/balances/my` | 130 | 341 | 130 |
| **`GET /inbox/counts`** | **2,207** | **5,238** | **2,203** |
| **`GET /inbox/chats`** | **2,381** | **5,276** | **2,316** |

### p95 per journey at normal load (the gate)

| Journey | p95 | Gate (<= 200 ms) |
|---|---|---|
| task board | 121 ms | pass |
| employee list | 79 ms | pass |
| attendance | 102 ms | pass |
| leave | 130 ms | pass |
| dashboard | 1,559 ms | **fail** (includes `/inbox/counts`) |
| inbox | 2,267 ms | **fail** |

### Process and database

| Run | CPU (% of 12 cores) avg / max | Working set avg / max | Threads max | SQL sessions max |
|---|---|---|---|---|
| normal | 7.6 / 11.7 % | 512 / 541 MB | 42 | 15 |
| peak | 14.9 / 20.2 % | 592 / 666 MB | 43 | 43 |
| soak | 8.0 / 14.3 % | 547 / 572 MB | 42 | 43 |

During the soak the working set averaged 545 MB in both the first and the last quarter: no growth.

## What it shows

- **No errors at any load**, no rate-limit rejections (each virtual user has its own account and client address).
- **The inbox endpoints are the limit.** `/inbox/counts` and `/inbox/chats` take 2 seconds at 20 users and 5 at 60,
  every other endpoint stays near or under 200 ms at 20 users. Tasks 00a (counts) and 00b (paged chat list) target
  exactly these and are not merged yet.
- **Not CPU, not memory.** At 60 users the API used under 2.5 of 12 cores and under 700 MB. The non-inbox endpoints
  roughly double from 20 to 60 users while CPU stays low, which points at waiting (database round trips per
  request, task 02) rather than computing.
- **Capacity of one instance (laptop):** 20 concurrent active users inside the gate for the non-inbox journeys;
  60 users without errors but over 200 ms. Repeat on UAT before using these numbers for sizing.
- Compared with the 11-11 run (Debug, SQL logging on, one shared account): p95 at 25 users was 4.2 s then; the
  non-inbox endpoints are now 46 to 177 ms at 20 users.

## Not measured

Writes (clock in, leave submission), thread-pool queue length and SQL waits, several tenants, realistic data
volumes (task 06). See the release-gate doc.
