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

## Verification status

- Capture 1–4 (before): the combined endpoint failed (1443 ms first run, warm provider stages at least 345 ms).
- Capture 5 (after): warm combined counts are 167–191 ms over a reused connection; with a new connection per call 2 of 12
  runs were over 200 ms. The cold first call is 1.3 s.
- Not yet done: two clean browser runs (DevTools), and a run with production-level logging.
- Next candidates if the margin is not enough: the Leave count's per-request evaluation, and the authority lookups
  repeated per candidate employee.
- Unit, architecture and SQL Server integration verification is recorded with the branch commits.
