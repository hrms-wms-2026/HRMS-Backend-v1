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

## Verification status

- The individual list-summary calls in Capture 1 pass, but the combined `/api/v1/inbox/counts` endpoint does not.
- The first completed combined-count run took 1443 ms; the following warm provider stages totalled at least 345 ms.
- Task 00a remains open until remaining list-based provider counts are replaced by aggregate count reads and two clean
  completed browser runs are below 200 ms.
- Unit and architecture verification remains recorded separately with the implementation branch evidence.
