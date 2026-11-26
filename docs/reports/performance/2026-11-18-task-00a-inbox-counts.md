# Task 00a inbox count verification

**Date:** 2026-11-18  
**Environment:** local QA stack, Chrome DevTools, `No throttling`  
**Scope:** inbox count/list-summary requests only; CORS preflight and individual case-detail requests are excluded from the Task 00a target.

## Target

Inbox count paths must complete in less than 200 ms without loading and mapping the full inbox feed.

## Captured results

### Capture 1 — usable warm-run timing

| Request | Status | Duration | Result |
|---|---:|---:|---|
| `unread-count` | 200 | 6 ms | Pass |
| `cases?category=request...` | 200 | 85 ms | Pass |
| `cases?category=alert...` | 200 | 136 ms | Pass |
| `months?category=request...` | 200 | 82 ms | Pass |
| `months?category=alert...` | 200 | 120 ms | Pass |

All captured list-summary requests in this run were below 200 ms. This capture did not contain a completed
`/api/v1/inbox/counts` response, so it is supporting evidence rather than proof that the combined count endpoint passes.

### Capture 2 — retained as supporting evidence, not a count benchmark

The second capture showed the page loaded (`DOMContentLoaded` 125 ms, `Load` 127 ms, network finish 3.51 s), but the `counts` XHR was still pending when the capture was taken. It is therefore not used as a Task 00a pass/fail timing.

The same capture exposed a separate follow-up finding: the inbox made 296 requests and several individual case-card XHR calls took about 227–315 ms. Those calls are outside Task 00a, but should be investigated under the inbox paging/query-in-a-loop work rather than being hidden by this count optimisation.

### Capture 3 — retained as supporting evidence, not a count benchmark

The third capture again showed the `counts` XHR still pending, with its CORS preflight completing in 1 ms. It is therefore not used as a Task 00a pass/fail timing.

The page reported `DOMContentLoaded` 117 ms, `Load` 119 ms and network finish 1.35 s. It made 297 requests, transferred 132 kB and referenced 12.4 MB of resources. Visible individual case-card XHR calls ranged from about 159–236 ms, with several above the 200-ms target. This reinforces the separate inbox paging/query-in-a-loop follow-up finding.

### Capture 4 — instrumented combined-count diagnosis

After restarting the backend with per-provider timing markers and continuously draining its log output, the combined
`/api/v1/inbox/counts` request completed. The first successful run took **1443 ms**. A following warm run broke down as:

| Stage | Duration |
|---|---:|
| Monitoring provider | 125 ms |
| Calendar provider | 17 ms |
| Attendance provider | 20 ms |
| Leave provider | 45 ms |
| Onboarding provider | 1 ms |
| Work provider | 74 ms |
| Conversation unread count | 63 ms |

The sequential stages total at least 345 ms before request-pipeline overhead, so the combined endpoint still fails the
less-than-200-ms target. An earlier request during concurrent startup traffic returned 500 after about 3396 ms; it is
excluded from performance benchmarking but confirms that startup load must not be mixed into the clean benchmark.

## Pending-request diagnosis

The repeated pending `counts` entries had two different environmental causes during investigation. Initially the Task 00a
backend was stopped: `dapi.onexso.com` resolved to `127.0.0.1`, but nothing listened on port 7229. After restart, verbose
development SQL logging accumulated several megabytes of output in the attached process session; continuously consuming
and filtering that output removed the apparent indefinite pending state.

Seeded rows are not corrupt and SQL Server showed no blocking request in `sys.dm_exec_requests`. However, seed volume
exposes the application-level problem: Monitoring, Calendar and Work count providers still load case/project collections
(and Work loads hierarchy/audience data per project) instead of issuing only aggregate count queries. The router awaits all
providers sequentially, then separately calculates unread conversations, so their durations accumulate.

### Capture 5 — after the aggregate counts (2026-11-19)

Branch `perf/task-00a-inbox-counts` at `345ff855` (Monitoring, Calendar, Work and conversation-unread counts replaced
by aggregate reads), Release build, local SQL Server, seeded `dapi` tenant, Development logging (SQL logged), signed in
as the seeded demo user `mathusanth.kumaran@dapi.test`. Measured with `curl` against
`https://dapi.onexso.com:7229/api/v1/inbox/counts`, not in a browser.

The response was 8 pending requests, 115 open alerts and 367 unread messages, so every provider had rows to count.

| Run | Result |
|---|---|
| First call after start-up (cold) | 1337 ms |
| 12 warm calls, new connection each | 168–219 ms (2 of 12 over 200 ms, including the TLS handshake) |
| 10 warm calls, one reused connection | 167–191 ms after the first; server log 170–180 ms |

Warm requests over a reused connection are under 200 ms, but only by 10–30 ms. One request still issued about 340 SQL
commands. The log interleaves background jobs, so they cannot be attributed exactly, but the Leave provider visibly
repeats one read pattern per pending leave request (requests, entitlements, approvers, policy, policy versions, blackout
periods, assignments): 8 pending requests gave 8 repetitions. Authority lookups (employees, position assignments,
coverage, hierarchy closure) also repeat.

### Capture 6 — after batching the Leave count (2026-11-19)

Same setup as Capture 5, with the Leave actionable count for requests created before policy-version pinning batched
(one request read, one approver read and one policy snapshot for all of them, instead of `GetStateAsync` plus a full
policy resolution per request). The response was unchanged (8 pending leave requests, 115 open alerts).

| Run | Result |
|---|---|
| First call after start-up (cold) | 1288 ms |
| 12 warm calls, one reused connection | 117–163 ms |
| 10 warm calls, new connection each | 125–157 ms (all under 200 ms) |
| Server log, last 10 | 113–144 ms |

SQL commands logged during one request fell from about 340 to about 120 (the log still interleaves background jobs).

### Capture 7 — routing-only exception scope (2026-11-19)

Branch at `f7c823ef`: the Monitoring and Calendar counts read only the routing parts of the exception scope (HR flag,
actor, held cases), remembered for the request, instead of resolving coverage visibility and the exact-approver set.
Same setup as Capture 5, Release build, same demo user; the response was unchanged.

Per-provider time inside one warm request (temporary local timing markers, not committed):

| Provider | Capture 6 | Capture 7 |
|---|---:|---:|
| Monitoring | 46–48 ms | 26 ms |
| Calendar | 40 ms | 19 ms |
| Attendance | 20–22 ms | 20 ms |
| Work | 14–20 ms | 14 ms |
| Leave | 12 ms | 12 ms |
| Conversation unread | 5–6 ms | 6 ms |
| Onboarding | 0 ms | 0 ms |

| Run | Result |
|---|---|
| First call after start-up (cold) | 1160 ms |
| Second call | 215 ms |
| 10 warm calls, one reused connection | 108–136 ms |

### Cold first call

The 1.1–1.3 s first call is a once-per-process cost (JIT and EF Core query compilation), not a per-user cost. With the
process already warm, a second seeded user (`tharmi.rajendran@dapi.test`, 18 open alerts, 145 unread) got 282 ms on
their first call, including a new TLS connection and query shapes the first user had not used, then 64–73 ms. It
affects only the first request after each deployment or restart; a start-up warm-up request would hide it, but that is
outside the Task 00a target, which is about warm requests.

### Capture 8 — browser runs (2026-11-19)

The inbox page (`https://dapi.onexso.com:4200/inbox`, frontend `Development` dev server) loaded in headless Chromium
(Playwright's bundled Chromium, the same browser engine as Chrome) against the Capture 7 backend, Release build, signed
in with the seeded demo user's session cookie. Each value is the `/inbox/counts` request's duration from the browser's
Resource Timing API, the data DevTools' Network panel shows. Desktop Chrome showed a certificate error for the local
host through the browser extension, so it could not be used.

The page sends `/inbox/counts` twice on every load; the second request starts while the first is still running.

| Load | First counts request | Second counts request | API calls on the page |
|---|---:|---:|---:|
| First load (cold page) | 494 ms | 581 ms | 16 |
| Reload 1 (clean run) | 105 ms | 186 ms | 15 |
| Reload 2 (clean run) | 93 ms | 175 ms | 15 |

Both clean runs are under 200 ms for both requests. The duplicate came from the frontend's rail-badge readiness effect,
which ran again when modules arrived after sign-in. Fixed on frontend branch `fix/inbox-counts-once`: with it the page
sends one `/inbox/counts` per load, 96-120 ms on reloads (two runs of three loads).

### Capture 9 — backend and frontend fixes together (2026-11-19)

Both fixes merged into local `development` (backend, this branch) and `Development` (frontend, `fix/inbox-counts-once`,
including the latest `origin/Development`), not yet merged upstream. Backend Release build on a fresh process, frontend
dev server, same seeded demo user, same headless Chromium method as Capture 8.

| Measurement | `/inbox/counts` |
|---|---|
| Cold first call after process start | 1,386 ms |
| Warm, one reused connection (10 calls) | 108–175 ms; 8 of 10 between 108 and 130 ms |
| New connection per call (6 calls) | 121–142 ms |

| Browser load | `/inbox/counts` requests | Duration | API calls on the page |
|---|---:|---:|---:|
| First load (cold page) | 1 | 335 ms | 13 |
| Reload 1 | 1 | 121 ms | 13 |
| Reload 2 | 1 | 116 ms | 13 |

The page now sends one counts request per load (two before), and the page makes 13 API calls instead of 15.

## Verification status

- Capture 1–4 (before): the combined endpoint failed (1443 ms first run, warm provider stages at least 345 ms).
- Capture 5 (aggregate counts): warm 167–191 ms over a reused connection; 2 of 12 new-connection runs over 200 ms.
- Capture 6 (plus Leave batching): every warm run 117–163 ms, new connection or not.
- Capture 7 (routing-only scope): warm 108–136 ms; the slowest provider is now Monitoring at about 26 ms.
- Cold first call: about 1.2 s once per process start; a second user's first call is about 280 ms.
- Capture 8 (browser): two clean inbox reloads, `/inbox/counts` 93–105 ms (and 175–186 ms for the duplicate
  request the page sends at the same time) - the Task 00a target of two clean browser runs under 200 ms is met.
- Capture 9 (both fixes together, local): one `/inbox/counts` per load, 116–121 ms on clean reloads; warm API calls
  108–175 ms. Still to do: the same check on the shared dev environment after both PRs merge.
- Not yet done: a run with production-level logging.
- Remaining repeated reads: the exact-approver lookups in case routing, Attendance's scope, and Work alert audiences
  per project with open alerts.
- Unit, architecture and SQL Server integration verification is recorded with the branch commits.
