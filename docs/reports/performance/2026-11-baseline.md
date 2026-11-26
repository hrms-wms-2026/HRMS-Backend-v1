# Endpoint timing baseline (task 06 Part B)

**Date:** 2026-11-26. **Build:** `development` with #238 (one connection per request) and #242 (inbox counts), plus
the task 06 Part A request timing (#245). **Re-run:** `./ops/perf-baseline/run-baseline.ps1 -Database <throwaway db>`
([how](../../../ops/perf-baseline/README.md)).

## Summary

- **186 tenant `GET` endpoints timed**, 20 warm calls each. Half answer in 13 ms or less (median p50).
- **14 endpoints have p50 over 200 ms; 19 have p95 over 200 ms.** Four of the 19 are *spiky*: their usual time is
  15–83 ms, and a background job running during the run lifted one or two calls.
- The slow endpoints fall into two groups that need different work:
  - **Too many SQL commands (about 300 per call):** the four inbox reads and the team action items. The time goes
    on repeated scope lookups, one employee at a time. That is task 14 (#276).
  - **Few commands that each take long (15–35 commands, 150–370 ms of SQL):** team attendance, the planner team
    views, the work forecast, leave balances and the employee list. These read the year of attendance and the 20,000
    tasks. That is task 18 (index review) and task 40 (clustered keys).
- **The API itself adds little.** The client time is within about 10 ms of the server time on every slow endpoint,
  and SQL time is most of the server time. Serialisation and the network are not where the time goes on this
  machine.

## Setup

| | |
|---|---|
| Data | Dapi demo tenant filled by `DapiPerformanceBaselineSeeder`: 500 employees in a five-level reporting line, 50 projects, 20,000 tasks, 365 days of attendance (180,940 new rows) |
| Caller | The tenant owner, who sees all 500 people: the widest scope, so team lists are at their largest |
| Method | One call at a time; one warm-up call, then 20 timed calls. Server time, SQL count and SQL time from the task 06 request log line. Date filters set to the last 30 days, as the screens send |
| Machine | Developer laptop, 12 logical cores; API (Release) and SQL Server on the same machine; production log levels; rate limiter off for this one caller |

## The 20 slowest (by p50)

| # | Endpoint | p50 ms | SQL | SQL ms | Group | Task |
|---|---|---|---|---|---|---|
| 1 | `work/forecast/employees` | 601 | 20 | 296 | Heavy queries, plus about 300 ms in code | 18; check the in-memory forecast |
| 2 | `attendance/time-tracking/team/working-hours` | 431 | 16 | 367 | Heavy queries | 18, 40 |
| 3 | `attendance/time-tracking/team/attendance-employees` | 428 | 32 | 357 | Heavy queries | 18, 40 |
| 4 | `attendance/time-tracking/team/attendance-summary` | 426 | 32 | 354 | Heavy queries | 18, 40 |
| 5 | `planner/team/schedule` | 335 | 34 | 278 | Heavy queries | 18 |
| 6 | `inbox/chats` | 319 | 328 | 166 | Many commands | 14 |
| 7 | `inbox/filters` | 288 | 297 | 147 | Many commands | 14 |
| 8 | `inbox` | 284 | 300 | 148 | Many commands | 14 |
| 9 | `inbox/counts` | 276 | 320 | 132 | Many commands | 14 |
| 10 | `attendance/time-tracking/team/work-vs-task` | 262 | 15 | 212 | Heavy queries | 18, 40 |
| 11 | `dashboard/team/action-items` | 234 | 307 | 154 | Many commands | 14 |
| 12 | `planner/team/week` | 230 | 16 | 174 | Heavy queries | 18 |
| 13 | `attendance/time-tracking/covered-history` | 204 | 16 | 185 | Heavy queries | 18, 40 |
| 14 | `leave/balances/team` | 204 | 35 | 146 | Heavy queries | 18 |
| 15 | `employees` | 178 | 19 | 150 | Heavy queries | 18 |
| 16 | `leave/balances/all` | 170 | 33 | 111 | Heavy queries | 18 |
| 17 | `attendance/time-tracking/team/today` | 152 | 19 | 126 | Heavy queries | 18, 40 |
| 18 | `work/led-progress/employees` | 112 | 10 | 95 | Heavy queries | 18 |
| 19 | `employees/{id}/work-graph` | 102 | 43 | 88 | Mixed | 14, 18 |
| 20 | `leave/entitlements` | 101 | 13 | 62 | Heavy queries | 18 |

All paths start with `/api/v1/`. "SQL ms" is the time spent waiting on SQL Server within the call.

**Task 22 (reference data cache)** does not show up here: reference lookups add 1–3 short commands to a call, and
none of the 20 slowest is waiting on them.

**Inbox after #238 and #242:** `inbox/counts` is 276 ms at p50 with 500 people, against 108–136 ms on the 26-person
demo tenant (task 00a). Its about 320 commands grow with the number of people in scope, so task 14 matters more as
tenants grow.

## Found on the way

1. **`GET planner/employees/{employeeId}/range` returns 500 without `from` and `to`.** `TeamPlannerService.LoadLeaveAsync`
   subtracts days from `DateOnly.MinValue` and throws `ArgumentOutOfRangeException`. It should be a 400. The screens
   always send dates, so users do not see it. The baseline times it with dates.
2. **Twelve reads need a query parameter that the API description marks optional.** For example,
   `attendance/clock-in-policies` needs `legalEntityId` and `work/led-progress/period` needs an id filter. They answer
   400 without it. The table marks the ones the runner could time with an id (†); the rest are under "Failed calls".
   The OpenAPI document should mark them required.
3. **The tenant owner gets 403 "You do not have access to this employee" on seven monitoring reads**
   (`monitoring/activity/*`, `app-usage/snapshots`, `device-state/snapshots`, `daily-report`, `meetings/signals`) for
   the first employee id the runner found. Monitoring has its own access rules, so this may be correct, but it is
   worth checking with the monitoring owner. These endpoints are not timed.

## Not covered

- **Writes** (`POST`, `PUT`, `DELETE`), the **admin API**, and **concurrency**. The k6 load test in `ops/loadtest`
  covers concurrency.
- **A database across a network.** On UAT every SQL command also pays a network round trip, so endpoints with many
  commands (the inbox) will be slower there than here.
- **37 skipped and 49 failed routes** (listed under the table). The skipped routes are redirects, exports, file
  downloads, and routes whose ids the runner could not find. The failed routes are tray-device endpoints (401), reads
  the owner may not make (403), missing setup such as holiday settings or a tray installer (404), and the 400s in
  finding 2.

## Full results

<!-- baseline:start -->

Run 2026-11-26 05:38 UTC: 186 endpoints timed (20 warm calls each), 49 failed, 37 skipped. **14 have p50 over 200 ms; 19 have p95 over 200 ms.** Sorted by p50. *Spiky*: p95 over 200 ms and more than 3 times p50 (usually a background job running at the same moment).

| # | Route | p50 ms | p95 ms | max ms | SQL | SQL ms | Client p50 ms |
|---|---|---|---|---|---|---|---|
| 1 | `/api/v1/work/forecast/employees` **(top 20)** | 601 | 645 | 668 | 20 | 296 | 605 |
| 2 | `/api/v1/attendance/time-tracking/team/working-hours` **(top 20)** | 431 | 632 | 673 | 16 | 367 | 436 |
| 3 | `/api/v1/attendance/time-tracking/team/attendance-employees` **(top 20)** | 428 | 490 | 530 | 32 | 357 | 436 |
| 4 | `/api/v1/attendance/time-tracking/team/attendance-summary` **(top 20)** | 426 | 508 | 614 | 32 | 354 | 432 |
| 5 | `/api/v1/planner/team/schedule` **(top 20)** | 335 | 358 | 361 | 34 | 278 | 345 |
| 6 | `/api/v1/inbox/chats` **(top 20)** | 319 | 350 | 350 | 328 | 166 | 327 |
| 7 | `/api/v1/inbox/filters` **(top 20)** | 288 | 324 | 343 | 297 | 147 | 297 |
| 8 | `/api/v1/inbox` **(top 20)** | 284 | 308 | 322 | 300 | 148 | 294 |
| 9 | `/api/v1/inbox/counts` **(top 20)** | 276 | 292 | 293 | 320 | 132 | 284 |
| 10 | `/api/v1/attendance/time-tracking/team/work-vs-task` **(top 20)** | 262 | 521 | 561 | 15 | 212 | 274 |
| 11 | `/api/v1/dashboard/team/action-items` **(top 20)** | 234 | 539 | 649 | 307 | 154 | 245 |
| 12 | `/api/v1/planner/team/week` **(top 20)** | 230 | 249 | 253 | 16 | 174 | 240 |
| 13 | `/api/v1/attendance/time-tracking/covered-history` **(top 20)** | 204 | 221 | 222 | 16 | 185 | 217 |
| 14 | `/api/v1/leave/balances/team` **(top 20)** | 204 | 224 | 229 | 35 | 146 | 214 |
| 15 | `/api/v1/employees` **(top 20)** | 178 | 212 | 216 | 19 | 150 | 183 |
| 16 | `/api/v1/leave/balances/all` **(top 20)** | 170 | 185 | 192 | 33 | 111 | 176 |
| 17 | `/api/v1/attendance/time-tracking/team/today` **(top 20)** | 152 | 178 | 179 | 19 | 126 | 166 |
| 18 | `/api/v1/work/led-progress/employees` **(top 20)** | 112 | 130 | 130 | 10 | 95 | 123 |
| 19 | `/api/v1/employees/{id}/work-graph` **(top 20)** | 102 | 110 | 117 | 43 | 88 | 112 |
| 20 | `/api/v1/leave/entitlements` **(top 20)** | 101 | 136 | 137 | 13 | 62 | 112 |
| 21 | `/api/v1/inbox/history` | 100 | 105 | 108 | 194 | 58 | 109 |
| 22 | `/api/v1/employees/offboarding-overview` | 97 | 120 | 161 | 11 | 81 | 105 |
| 23 | `/api/v1/meetings` | 83 | 247 | 436 | 7 | 34 | 103 |
| 24 | `/api/v1/users` | 81 | 95 | 112 | 8 | 71 | 92 |
| 25 | `/api/v1/monitoring/screenshots` *spiky* | 81 | 515 | 531 | 11 | 72 | 89 |
| 26 | `/api/v1/employees/{id}/overview/signals` | 78 | 84 | 86 | 64 | 56 | 86 |
| 27 | `/api/v1/attendance/corrections/approvals` | 72 | 79 | 107 | 12 | 38 | 79 |
| 28 | `/api/v1/employees/{id}/overview/recent-activity` | 70 | 102 | 126 | 27 | 58 | 78 |
| 29 | `/api/v1/work/led-progress/tasks` | 67 | 81 | 100 | 10 | 50 | 79 |
| 30 | `/api/v1/employees/{employeeId}/checklist-tasks` | 64 | 85 | 86 | 11 | 55 | 74 |
| 31 | `/api/v1/work/led-progress` | 63 | 74 | 77 | 13 | 46 | 77 |
| 32 | `/api/v1/employees/{employeeId}/offboarding` | 63 | 92 | 94 | 11 | 54 | 74 |
| 33 | `/api/v1/employees/{id}/overview/time-off` | 60 | 66 | 68 | 43 | 46 | 64 |
| 34 | `/api/v1/employees/{employeeId}/leave-policies` | 59 | 64 | 65 | 20 | 48 | 65 |
| 35 | `/api/v1/employees/{id}/overview/checklists` | 58 | 63 | 66 | 14 | 39 | 67 |
| 36 | `/api/v1/employees/{employeeId}/offboarding/checklist-matches` | 55 | 59 | 62 | 13 | 47 | 61 |
| 37 | `/api/v1/work/projects/{projectId}/tasks` | 54 | 64 | 66 | 35 | 20 | 66 |
| 38 | `/api/v1/employees/{id}/overview/attendance-discipline` | 53 | 65 | 66 | 21 | 42 | 61 |
| 39 | `/api/v1/employees/{id}/overview/attendance/days` | 53 | 60 | 62 | 21 | 44 | 64 |
| 40 | `/api/v1/employees/{id}/overview/attendance` | 51 | 56 | 57 | 21 | 41 | 56 |
| 41 | `/api/v1/employees/{id}/work-activity/recent-tasks` | 51 | 62 | 64 | 15 | 41 | 63 |
| 42 | `/api/v1/employees/{employeeId}/offboarding/open-work-tasks` | 50 | 54 | 54 | 14 | 40 | 55 |
| 43 | `/api/v1/employees/{id}/overview/approvals` | 50 | 55 | 58 | 21 | 41 | 57 |
| 44 | `/api/v1/employees/{id}/overview/history` | 50 | 54 | 57 | 18 | 41 | 57 |
| 45 | `/api/v1/employees/{id}/overview/upcoming` | 50 | 54 | 57 | 17 | 40 | 62 |
| 46 | `/api/v1/employees/{id}/overview/delivery` | 49 | 54 | 58 | 14 | 40 | 55 |
| 47 | `/api/v1/work/forecast/modules` | 49 | 54 | 98 | 23 | 38 | 59 |
| 48 | `/api/v1/employees/{id}/overview/work` | 49 | 53 | 57 | 14 | 40 | 54 |
| 49 | `/api/v1/employees/{id}/work-activity/delivery-trend` | 48 | 55 | 65 | 14 | 40 | 62 |
| 50 | `/api/v1/employees/{id}/work-activity/needs-attention` | 48 | 52 | 55 | 14 | 40 | 60 |
| 51 | `/api/v1/attendance/time-tracking/today` | 48 | 53 | 58 | 22 | 26 | 54 |
| 52 | `/api/v1/employees/{id}` | 48 | 52 | 61 | 12 | 40 | 54 |
| 53 | `/api/v1/employees/{id}/overview/work/tasks` | 48 | 55 | 66 | 14 | 39 | 60 |
| 54 | `/api/v1/employees/{id}/overview/activity` | 48 | 52 | 59 | 12 | 39 | 52 |
| 55 | `/api/v1/employees/{id}/work-activity/work-pattern` | 47 | 50 | 59 | 12 | 39 | 60 |
| 56 | `/api/v1/work/led-progress/weekly-completion` | 47 | 55 | 56 | 9 | 32 | 58 |
| 57 | `/api/v1/employees/{id}/work-activity/app-usage` | 47 | 54 | 59 | 12 | 39 | 60 |
| 58 | `/api/v1/employees/{id}/work-activity/activity-by-hour` | 47 | 59 | 68 | 13 | 39 | 59 |
| 59 | `/api/v1/work/projects/{id}/work-graph` | 45 | 50 | 53 | 27 | 12 | 58 |
| 60 | `/api/v1/work/led-progress/time/unscheduled` | 44 | 49 | 51 | 13 | 21 | 58 |
| 61 | `/api/v1/monitoring/activity/my-work-pattern` | 41 | 45 | 45 | 26 | 32 | 49 |
| 62 | `/api/v1/leave/requests/pending-approvals` | 38 | 181 | 182 | 4 | 18 | 89 |
| 63 | `/api/v1/work/projects/{projectId}/calendar` | 38 | 42 | 45 | 11 | 7 | 52 |
| 64 | `/api/v1/dashboard/team/capabilities` *spiky* | 30 | 245 | 556 | 24 | 9 | 36 |
| 65 | `/api/v1/auth/me` | 30 | 97 | 139 | 9 | 24 | 42 |
| 66 | `/api/v1/planner/team/range` | 28 | 30 | 31 | 29 | 18 | 33 |
| 67 | `/api/v1/employees/{id}/detail` | 27 | 35 | 37 | 20 | 6 | 33 |
| 68 | `/api/v1/work/projects/{projectId}/my-tasks` | 27 | 30 | 33 | 9 | 7 | 34 |
| 69 | `/api/v1/attendance/corrections/my` | 22 | 25 | 25 | 6 | 2 | 28 |
| 70 | `/api/v1/employees/me` | 22 | 68 | 564 | 14 | 4 | 34 |
| 71 | `/api/v1/employees/{employeeId}/meeting-attendance` | 21 | 30 | 44 | 9 | 13 | 28 |
| 72 | `/api/v1/work/sprints/{id}/tasks` | 21 | 23 | 24 | 13 | 5 | 34 |
| 73 | `/api/v1/attendance/time-tracking/history` | 20 | 33 | 34 | 10 | 4 | 31 |
| 74 | `/api/v1/attendance/work-area-change-requests/approvals` | 20 | 22 | 24 | 6 | 2 | 25 |
| 75 | `/api/v1/conversations/{id}/messages` | 19 | 24 | 25 | 14 | 4 | 29 |
| 76 | `/api/v1/employees/me/payroll` | 19 | 23 | 26 | 4 | 2 | 25 |
| 77 | `/api/v1/meetings/series/{seriesId}/link` | 19 | 21 | 21 | 5 | 2 | 24 |
| 78 | `/api/v1/attendance/time-tracking/monthly-summary` | 19 | 21 | 21 | 12 | 4 | 25 |
| 79 | `/api/v1/attendance/work-area-change-requests/my` | 18 | 22 | 24 | 7 | 2 | 24 |
| 80 | `/api/v1/inbox/chats/{conversationId}/media` | 18 | 21 | 21 | 12 | 3 | 24 |
| 81 | `/api/v1/planner/team` | 18 | 21 | 22 | 29 | 10 | 33 |
| 82 | `/api/v1/work/projects/{projectId}/approvals` | 18 | 20 | 20 | 14 | 4 | 31 |
| 83 | `/api/v1/work/projects/mine` | 18 | 22 | 24 | 37 | 10 | 31 |
| 84 | `/api/v1/work/projects/{projectId}/monitor/alerts` | 18 | 20 | 20 | 8 | 3 | 22 |
| 85 | `/api/v1/work/projects/{projectId}/task-status-change-requests` | 16 | 20 | 26 | 8 | 2 | 21 |
| 86 | `/api/v1/work/projects/{projectId}/work-notifications` | 16 | 17 | 18 | 6 | 2 | 22 |
| 87 | `/api/v1/conversations/people` *spiky* | 15 | 672 | 728 | 26 | 8 | 31 |
| 88 | `/api/v1/planner/employees/{employeeId}/range` | 15 | 17 | 20 | 14 | 9 | 27 |
| 89 | `/api/v1/inbox/chats/{conversationId}/search` | 15 | 16 | 16 | 3 | 1 | 25 |
| 90 | `/api/v1/onboarding/drafts/clock-in-defaults` | 15 | 16 | 17 | 5 | 1 | 20 |
| 91 | `/api/v1/leave/balances/my` | 14 | 16 | 17 | 33 | 7 | 20 |
| 92 | `/api/v1/work/my-weekly-progress` | 14 | 16 | 18 | 17 | 8 | 18 |
| 93 | `/api/v1/onboarding/drafts/reporting-chain` | 14 | 16 | 19 | 3 | 1 | 23 |
| 94 | `/api/v1/work/projects/{id}` | 13 | 15 | 16 | 29 | 6 | 25 |
| 95 | `/api/v1/onboarding/drafts/manager-candidates` | 13 | 14 | 15 | 2 | 1 | 26 |
| 96 | `/api/v1/announcements/active` | 13 | 20 | 20 | 3 | 1 | 18 |
| 97 | `/api/v1/inbox/kinds` | 13 | 14 | 15 | 2 | 1 | 26 |
| 98 | `/api/v1/calendar` | 12 | 15 | 16 | 14 | 6 | 18 |
| 99 | `/api/v1/leave/requests/{requestId}/detail` | 11 | 12 | 12 | 19 | 5 | 16 |
| 100 | `/api/v1/employees/me/documents` | 11 | 19 | 34 | 4 | 3 | 20 |
| 101 | `/api/v1/meetings/series/{seriesId}` | 11 | 86 | 200 | 14 | 5 | 17 |
| 102 | `/api/v1/org/legal-entities/{legalEntityId}/positions/people-chart` | 10 | 14 | 19 | 4 | 3 | 15 |
| 103 | `/api/v1/leave/calendar` | 10 | 12 | 12 | 4 | 3 | 23 |
| 104 | `/api/v1/planner/employees/{employeeId}` | 10 | 13 | 14 | 12 | 5 | 16 |
| 105 | `/api/v1/planner/me` | 9 | 12 | 17 | 11 | 5 | 22 |
| 106 | `/api/v1/org/legal-entities/{legalEntityId}/positions/tree` | 9 | 11 | 16 | 5 | 4 | 19 |
| 107 | `/api/v1/monitoring/exceptions` | 8 | 59 | 60 | 7 | 3 | 15 |
| 108 | `/api/v1/org/legal-entities/{legalEntityId}/positions` | 8 | 9 | 9 | 7 | 4 | 12 |
| 109 | `/api/v1/work/projects/{projectId}/objectives` | 8 | 10 | 10 | 16 | 3 | 21 |
| 110 | `/api/v1/work/projects/{projectId}/sprints` | 7 | 9 | 9 | 12 | 4 | 11 |
| 111 | `/api/v1/planner/me/range` | 7 | 8 | 9 | 9 | 4 | 12 |
| 112 | `/api/v1/roles` | 7 | 9 | 9 | 11 | 3 | 12 |
| 113 | `/api/v1/work/sprints/{id}/activity` | 7 | 9 | 9 | 11 | 3 | 12 |
| 114 | `/api/v1/work/projects/{projectId}/objectives/mine` | 7 | 8 | 8 | 13 | 3 | 12 |
| 115 | `/api/v1/planner/me/focus` | 7 | 8 | 8 | 8 | 3 | 20 |
| 116 | `/api/v1/employees/{id}/position-history` | 6 | 8 | 8 | 8 | 2 | 12 |
| 117 | `/api/v1/org/position-template-packs` | 6 | 9 | 9 | 10 | 3 | 20 |
| 118 | `/api/v1/onboarding/drafts/approval-preview` | 6 | 9 | 10 | 7 | 3 | 11 |
| 119 | `/api/v1/leave/requests/all` | 6 | 9 | 9 | 4 | 2 | 13 |
| 120 | `/api/v1/org/legal-entities/{legalEntityId}/departments` | 6 | 8 | 8 | 7 | 2 | 11 |
| 121 | `/api/v1/leave/policies` | 6 | 7 | 9 | 7 | 2 | 11 |
| 122 | `/api/v1/leave/requests/my` | 6 | 74 | 88 | 5 | 2 | 20 |
| 123 | `/api/v1/users/{id}/sessions` | 6 | 8 | 8 | 10 | 2 | 20 |
| 124 | `/api/v1/attendance/location-change-requests/my` | 6 | 6 | 7 | 6 | 2 | 11 |
| 125 | `/api/v1/leave/balance-audit` | 6 | 8 | 8 | 3 | 3 | 12 |
| 126 | `/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/coverage-visualization` | 6 | 7 | 8 | 7 | 2 | 10 |
| 127 | `/api/v1/employees/{id}/documents` | 5 | 6 | 7 | 6 | 2 | 19 |
| 128 | `/api/v1/roles/{id}` | 5 | 7 | 8 | 9 | 2 | 10 |
| 129 | `/api/v1/work/my-deadlines` | 5 | 7 | 7 | 6 | 2 | 10 |
| 130 | `/api/v1/attendance/location-change-requests/approvals` | 5 | 7 | 8 | 5 | 2 | 10 |
| 131 | `/api/v1/monitoring/settings` | 5 | 11 | 18 | 5 | 2 | 13 |
| 132 | `/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}` | 5 | 7 | 8 | 8 | 2 | 11 |
| 133 | `/api/v1/work/projects/{id}/approval-history` | 5 | 7 | 7 | 8 | 2 | 10 |
| 134 | `/api/v1/monitoring/activity/my-timeline` | 5 | 13 | 18 | 5 | 2 | 11 |
| 135 | `/api/v1/onboarding/drafts/self-setup` | 5 | 6 | 7 | 7 | 2 | 9 |
| 136 | `/api/v1/onboarding/access-grant-requests` | 5 | 9 | 13 | 4 | 1 | 9 |
| 137 | `/api/v1/monitoring/device-change-requests/approvals` | 4 | 6 | 7 | 4 | 1 | 10 |
| 138 | `/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/active-holders` | 4 | 6 | 6 | 4 | 2 | 9 |
| 139 | `/api/v1/work/objectives/mine/history` | 4 | 5 | 6 | 5 | 2 | 18 |
| 140 | `/api/v1/monitoring/activity/my-focus-status` | 4 | 159 | 180 | 4 | 1 | 19 |
| 141 | `/api/v1/monitoring/notifications` | 4 | 31 | 36 | 5 | 2 | 18 |
| 142 | `/api/v1/work/my-task-progress` | 4 | 6 | 6 | 5 | 2 | 9 |
| 143 | `/api/v1/work/my-tasks` | 4 | 7 | 7 | 5 | 2 | 18 |
| 144 | `/api/v1/work/objectives/invitations/mine` | 4 | 6 | 7 | 5 | 1 | 9 |
| 145 | `/api/v1/onboarding/drafts` | 4 | 6 | 6 | 4 | 1 | 9 |
| 146 | `/api/v1/permissions` | 4 | 6 | 6 | 5 | 2 | 8 |
| 147 | `/api/v1/work/me` | 4 | 6 | 7 | 4 | 1 | 8 |
| 148 | `/api/v1/work/projects/{projectId}/task-statuses` | 4 | 6 | 6 | 5 | 1 | 9 |
| 149 | `/api/v1/leave/types` | 4 | 6 | 6 | 4 | 1 | 9 |
| 150 | `/api/v1/org/legal-entities/{id}/general-settings` | 4 | 6 | 7 | 5 | 1 | 18 |
| 151 | `/api/v1/settings/notifications` | 4 | 5 | 5 | 4 | 1 | 18 |
| 152 | `/api/v1/work/projects/{projectId}/task-categories` | 4 | 7 | 8 | 5 | 1 | 12 |
| 153 | `/api/v1/onboarding/employee-number-suggestion` † | 4 | 5 | 5 | 5 | 1 | 8 |
| 154 | `/api/v1/org/legal-entities/{legalEntityId}/departments/{departmentId}` | 4 | 6 | 8 | 5 | 1 | 8 |
| 155 | `/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/coverage` | 4 | 6 | 7 | 5 | 1 | 17 |
| 156 | `/api/v1/calendar/my-timezone` | 4 | 87 | 104 | 4 | 1 | 17 |
| 157 | `/api/v1/leave/holidays` | 4 | 6 | 7 | 3 | 1 | 9 |
| 158 | `/api/v1/leave/types/{leaveTypeId}` | 4 | 6 | 6 | 4 | 1 | 10 |
| 159 | `/api/v1/notifications` | 4 | 6 | 6 | 3 | 1 | 10 |
| 160 | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies/{id}` | 4 | 6 | 6 | 4 | 1 | 10 |
| 161 | `/api/v1/onboarding/access-grant-requests/pending-for-me` | 4 | 5 | 5 | 3 | 1 | 9 |
| 162 | `/api/v1/attendance/legal-entities/{legalEntityId}/clock-in-policies` | 4 | 5 | 5 | 4 | 1 | 18 |
| 163 | `/api/v1/dashboards/me` | 4 | 6 | 7 | 3 | 1 | 8 |
| 164 | `/api/v1/leave/approval-delegates` | 4 | 5 | 8 | 4 | 1 | 17 |
| 165 | `/api/v1/leave/balances/work-window` | 4 | 5 | 5 | 4 | 1 | 17 |
| 166 | `/api/v1/org/legal-entities` | 4 | 5 | 6 | 4 | 1 | 9 |
| 167 | `/api/v1/work/project-categories` | 4 | 6 | 14 | 4 | 1 | 17 |
| 168 | `/api/v1/attendance/monitoring/policy` | 4 | 4 | 5 | 4 | 1 | 9 |
| 169 | `/api/v1/org/legal-entities/{legalEntityId}/positions/{positionId}/access` | 4 | 6 | 6 | 4 | 1 | 9 |
| 170 | `/api/v1/people/checklist-templates` † | 4 | 5 | 7 | 4 | 1 | 8 |
| 171 | `/api/v1/work/task-drafts` | 4 | 5 | 6 | 4 | 1 | 8 |
| 172 | `/api/v1/offboarding-bypass-requests` | 4 | 5 | 6 | 3 | 1 | 10 |
| 173 | `/api/v1/onboarding/checklist-templates/matches` † | 4 | 7 | 7 | 3 | 1 | 18 |
| 174 | `/api/v1/support/tickets` | 4 | 5 | 7 | 4 | 1 | 8 |
| 175 | `/api/v1/attendance/legal-entities/{legalEntityId}/work-modes` | 4 | 5 | 5 | 3 | 1 | 8 |
| 176 | `/api/v1/calendar/connections` | 3 | 6 | 8 | 3 | 1 | 9 |
| 177 | `/api/v1/employees/{id}/identity` | 3 | 4 | 5 | 4 | 1 | 8 |
| 178 | `/api/v1/monitoring/activation/status` | 3 | 5 | 8 | 2 | 1 | 9 |
| 179 | `/api/v1/session/companies` | 3 | 4 | 4 | 4 | 1 | 7 |
| 180 | `/api/v1/notifications/unread-count` | 3 | 5 | 5 | 3 | 1 | 17 |
| 181 | `/api/v1/legal/pending` | 3 | 11 | 17 | 4 | 1 | 18 |
| 182 | `/api/v1/subscription-plans/{id}` | 3 | 6 | 6 | 3 | 1 | 10 |
| 183 | `/api/v1/work-modes` | 3 | 4 | 4 | 3 | 1 | 8 |
| 184 | `/api/v1/integrations/github/status` | 3 | 5 | 5 | 3 | 1 | 16 |
| 185 | `/api/v1/subscription-plans` | 3 | 4 | 5 | 3 | 1 | 7 |
| 186 | `/api/v1/legal/documents/current` | 3 | 5 | 6 | 1 | 0 | 7 |

† Answers 400 without an id filter that the API description marks optional; timed with one.

### Failed calls

| Route | Status | Error |
|---|---|---|
| `/api/v1/attendance/clock-in-policies` | 400 | legalEntityId query parameter is required. |
| `/api/v1/attendance/time-tracking/history-detail` | 403 | Access denied. |
| `/api/v1/calendar/holiday-settings` | 404 | Holiday calendar settings have not been set up for this company yet. |
| `/api/v1/employees/{id}/overview/signals/{key}/items` | 404 | Unknown overview signal 'my-work'. |
| `/api/v1/inbox/people/{employeeId}/cases` | 400 | One or more validation errors occurred. |
| `/api/v1/inbox/people/{employeeId}/cases/months` | 400 | One or more validation errors occurred. |
| `/api/v1/integrations/github/tenant/status` | 403 | Permission 'integrations:manage' required. |
| `/api/v1/leave/requests/{requestId}/approval` | 403 | This request is not assigned to you |
| `/api/v1/meetings/occurrences/{taskId}` | 404 | Meeting not found. |
| `/api/v1/meetings/occurrences/{taskId}/action-items` | 404 | Meeting not found. |
| `/api/v1/monitoring/activity/daily-range` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/activity/daily-summary` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/activity/snapshots` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/app-usage/snapshots` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/check-in/face-reference` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/daily-report` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/device-authorization/{requestId}` | 400 | One or more validation errors occurred. |
| `/api/v1/monitoring/device-state/snapshots` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/location-change-requests/pending` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/meetings/signals` | 403 | You do not have access to this employee. |
| `/api/v1/monitoring/permission-consent` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/reports/productivity` | 400 | scopeId is required for employee/department scope. |
| `/api/v1/monitoring/tray/attendance-status` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/tray/commands` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/tray/daily-tasks` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/tray/notifications/deliveries/pending` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/tray/notifications/pending` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/monitoring/tray/policy` | 401 | A valid tray device token is required to access this resource. |
| `/api/v1/onboarding/bulk-batches/template` | 400 | One or more validation errors occurred. |
| `/api/v1/onboarding/employee-number-availability` | 400 | Employee number is required. |
| `/api/v1/org/legal-entities/{legalEntityId}/positions/coverage/resolve` | 400 | One or more validation errors occurred. |
| `/api/v1/people/checklist-assignee-positions` | 404 | Company not found. |
| `/api/v1/people/checklist-assignees` | 404 | Company not found. |
| `/api/v1/tray/releases/check` | 400 | One or more validation errors occurred. |
| `/api/v1/tray/releases/latest` | 404 | No tray installer has been published yet. |
| `/api/v1/work/led-progress/period` | 400 | One or more validation errors occurred. |
| `/api/v1/work/led-progress/time` | 400 | One or more validation errors occurred. |
| `/api/v1/work/notification-navigation` | 400 | One or more validation errors occurred. |
| `/api/v1/work/objectives/{id}` | 403 | You do not have access to this milestone. |
| `/api/v1/work/objectives/{id}/members` | 403 | You do not have access to this milestone's members. |
| `/api/v1/work/objectives/{id}/tree` | 403 | You do not have access to this milestone. |
| `/api/v1/work/objectives/{objectiveId}/sprints` | 403 | You do not have access to this milestone. |
| `/api/v1/work/objectives/{objectiveId}/tasks` | 403 | You do not have access to this milestone. |
| `/api/v1/work/projects` | 403 | Work project visibility is relationship-based; you can only list your own projects. |
| `/api/v1/work/projects/{id}/banner` | 404 | Project banner not found. |
| `/api/v1/work/projects/{id}/logo` | 404 | Project logo not found. |
| `/api/v1/work/tasks/{id}` | 404 | Task not found. |
| `/api/v1/work/tasks/{id}/history` | 404 | Task not found. |
| `/api/v1/work/tasks/{taskId}/comments` | 404 | Task not found. |

### Skipped

| Route | Why |
|---|---|
| `/api/v1/calendar/connections/{provider}/callback` | not timed (redirect, stream or file flow) |
| `/api/v1/inbox/{source}/{kind}/{id}/export` | not timed (redirect, stream or file flow) |
| `/api/v1/integrations/github/connect/callback` | not timed (redirect, stream or file flow) |
| `/api/v1/leave/balance-audit/export` | not timed (redirect, stream or file flow) |
| `/api/v1/monitoring/daily-report/export` | not timed (redirect, stream or file flow) |
| `/api/v1/support/tickets/{id}/attachments/{attachmentId}/download-url` | not timed (redirect, stream or file flow) |
| `/api/v1/attendance/clock-in-policies/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/auth/invitations/{token}` | no value for token |
| `/api/v1/calendar/{id}/eligible-nominees` | no value for id (no list rows and no matching id field) |
| `/api/v1/calendar/{id}/meeting` | no value for id (no list rows and no matching id field) |
| `/api/v1/calendar/{id}/meeting/attendance` | no value for id (no list rows and no matching id field) |
| `/api/v1/conversations/{id}/attachments/{attachmentId}` | no value for attachmentId |
| `/api/v1/dashboard-widgets/{instanceId}/data` | no value for instanceId |
| `/api/v1/dashboards/{id}/bootstrap` | no value for id (no list rows and no matching id field) |
| `/api/v1/dashboards/{id}/widgets/available` | no value for id (no list rows and no matching id field) |
| `/api/v1/files/{fileId}` | no value for fileId |
| `/api/v1/inbox/chats/for-item/{source}/{kind}/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/inbox/{source}/{kind}/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/leave/policies/{leavePolicyId}` | no value for leavePolicyId |
| `/api/v1/legal/documents/{documentType}/{version}` | no value for documentType |
| `/api/v1/meetings/series/{seriesId}/occurrences/{originalStart}` | no value for originalStart |
| `/api/v1/meetings/series/{seriesId}/occurrences/{originalStart}/attendance` | no value for originalStart |
| `/api/v1/monitoring/exceptions/{exceptionId}/evidence` | no value for exceptionId |
| `/api/v1/monitoring/screenshots/{id}/url` | no value for id (no list rows and no matching id field) |
| `/api/v1/onboarding/bulk-batches/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/onboarding/drafts/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/people/checklist-templates/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/people/checklist-templates/{id}/files/{fileId}` | no value for id (no list rows and no matching id field) |
| `/api/v1/support/tickets/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/approvals/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/calendar-events/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/calendar-events/{id}/activity` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/calendar-events/{id}/tasks` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/objectives/{id}/assets/{assetId}/file` | no value for assetId |
| `/api/v1/work/task-drafts/{id}` | no value for id (no list rows and no matching id field) |
| `/api/v1/work/tasks/files/{fileId}` | no value for fileId |
| `/api/v1/work/tasks/{parentTaskId}/subtasks` | no value for parentTaskId |

<!-- baseline:end -->
