# API response timings, 2026-11-15

First measured baseline against the 200 ms target. The fix tasks are in `docs/Improvements`.

## How it was measured

- Backend: the local Development instance on `https://dapi.crownus.test:7229` (Debug build started 2026-11-13, SQL command logging on, local SQL Server).
- Tenant and user: the `dapi` demo tenant, signed in as the tenant owner.
- Tool: a Playwright Chromium window driving the real frontend.
- Two passes:
  1. **Page pass.** Each main screen was opened with a full page load and every API call the app made was recorded. Calls run in parallel here, as they do for a real user.
  2. **Single-call pass.** Each distinct GET was then called alone, three times in a row (the slow inbox ones twice). The table shows the middle value.

Read the numbers as "slower than UAT will be, but in the right order". A Release build without SQL logging will be faster. The ranking and the 3-second inbox calls will not change.

## Result in one paragraph

Of 50 endpoints measured alone, 37 are at or under 200 ms and 13 are over. The four inbox endpoints take 3.1 to 3.4 seconds each. `GET /inbox/counts` is the worst case for users because the app shell calls it on every page load, where it took 3.6 to 6.3 seconds. The chat list (`GET /inbox/chats`) takes 3.4 seconds and returns 183 KB with all 274 chats in one response. The fastest possible request (27 bytes of data) still takes about 70 ms, which is the fixed cost of authentication and tenant set-up on every call.

## Endpoints over 200 ms (single-call pass)

| Endpoint | Time (ms) | Size | Note |
|---|---|---|---|
| `GET /inbox/chats` | 3,440 | 182.5 KB | 274 chats (261 case groups, 13 direct), no paging |
| `GET /inbox?pageSize=25` | 3,088 | 24.8 KB | loads every item from every module, then pages in memory |
| `GET /inbox/counts` | 3,200 | 0.3 KB | loads the whole inbox to return a few numbers; called on every page |
| `GET /inbox/filters` | 3,103 | 7.7 KB | static-looking data, still 3 seconds |
| `GET /dashboard/team/action-items?topPerSource=4` | 1,057 | 3.6 KB | |
| `GET /dashboards/{id}/bootstrap` (My Team) | 381 | 62.6 KB | large payload |
| `GET /employees/me` | 358 | 1.2 KB | shell call on every load |
| `GET /dashboards/{id}/bootstrap` (home) | 321 | 14.8 KB | |
| `GET /planner/me` | 299 | 0.2 KB | shell call on every load |
| `GET /onboarding/drafts?mine=true` | 259 | 50 B | shell call; returns an empty list |
| `GET /work/task-drafts` | 243 | 2 B | shell call; returns an empty list |
| `GET /attendance/time-tracking/team/attendance-employees` | 218 | 4.8 KB | |
| `GET /notifications/unread-count` | 204 | 13 B | shell call; the three runs were 226, 204 and 76 |

## Endpoints at or under 200 ms (single-call pass)

| Endpoint | Time (ms) | Size |
|---|---|---|
| `GET /work/projects/mine?pageNumber=1&pageSize=20` | 187 | 9.3 KB |
| `GET /work/led-progress/time?period=week` | 184 | 43.3 KB |
| `GET /dashboards/{id}/widgets/available` | 183 | 1.9 KB |
| `GET /leave/balances/my?year=2026` | 178 | 2.1 KB |
| `GET /leave/requests/pending-approvals` | 170 | 0.9 KB |
| `GET /work/forecast/employees` | 169 | 14.1 KB |
| `GET /dashboard/team/capabilities` | 163 | 0.3 KB |
| `GET /conversations/{id}/messages` (long chat) | 132, 358 first call | 28.2 KB |
| `GET /conversations/{id}/messages` (short chat) | 163 | 1.7 KB |
| `GET /attendance/time-tracking/today` | 159 | 1.3 KB |
| `GET /work/forecast/modules` | 157 | 17.3 KB |
| `GET /attendance/time-tracking/team/today?limit=50` | 154 | 12.3 KB |
| `GET /employees?page=1&pageSize=25` | 152, 394 first call | 29.6 KB |
| `GET /planner/team/week` | 139 | 7.1 KB |
| `GET /attendance/time-tracking/monthly-summary` | 122 | 93 B |
| `GET /attendance/time-tracking/team/work-vs-task` | 121 | 7.7 KB |
| `GET /calendar?from&to` (one week) | 118 | 1.9 KB |
| `GET /work/led-progress/weekly-completion?weeks=10` | 117 | 1.1 KB |
| `GET /attendance/time-tracking/history` | 116 | 4.0 KB |
| `GET /attendance/corrections/my` | 103 | 106 B |
| `GET /planner/me/range` (one week) | 89 | 1.6 KB |
| `GET /inbox/kinds` | 87 | 1.8 KB |
| `GET /planner/me/focus` | 87 | 0.2 KB |
| `GET /auth/me` | 84 | 1.5 KB |
| `GET /org/legal-entities/{id}/departments?view=tree` | 81 | 2.3 KB |
| `GET /leave/requests/my` | 79 | 2.3 KB |
| `GET /employees/{id}/identity` | 71 | 91 B |
| `GET /meetings?from&to` (one day) | 71 | 2 B |
| `GET /leave/types` | 71 | 1.0 KB |
| `GET /work/project-categories` | 70 | 0.3 KB |
| `GET /leave/balances/work-window` | 70 | 99 B |
| `GET /session/companies` | 69 | 146 B |
| `GET /calendar/connections` | 69 | 18 B |
| `GET /calendar/my-timezone` | 68 | 27 B |
| `GET /dashboards/me` | 68 | 198 B |
| `GET /monitoring/activation/status` | 64 | 154 B |

## What a user feels: full page loads (page pass)

On a real page the calls run together and slow each other down. The same endpoints that take 70 to 300 ms alone took 300 to 900 ms here.

| Screen | API calls | Slowest call | Time until the network was quiet |
|---|---|---|---|
| Dashboard (home) | 26 | `inbox/counts` 5,235 ms | 8.2 s |
| Dashboard, My Team | 23 | `inbox/counts` 5,310 ms | 8.2 s |
| Work (projects) | 15 | `inbox/counts` 3,788 ms | 6.7 s |
| Inbox | 16 | `inbox/chats` 5,830 ms, then again 4,772 ms | 13.4 s |
| People | 13 | `inbox/counts` 6,322 ms | 9.6 s |
| Calendar | 23 | `inbox/counts` 4,283 ms | 7.4 s |
| Planner | 23 | `inbox/counts` 4,227 ms | 7.1 s |
| Time off | 17 | `inbox/counts` 4,152 ms | 7.0 s |
| Attendance | 16 | `inbox/counts` 3,792 ms | 6.6 s |
| Organization | 12 | `inbox/counts` 3,586 ms | 6.5 s |

"Time until the network was quiet" includes about 2 seconds of waiting that the measurement itself adds, so use it to compare screens, not as an absolute load time.

Other things seen in the page pass:

- Every full page load starts with the same 10 shell calls before any screen data: `auth/me`, `monitoring/activation/status` (twice), `work/task-drafts`, `onboarding/drafts`, `planner/me`, `employees/me`, the inbox hub negotiate, `notifications/unread-count`, `session/companies`, and then `inbox/counts`.
- The Inbox screen calls `inbox/counts` twice and `inbox/chats` twice during one load.
- Calendar and Planner each call `planner/me/focus` and `planner/me/range` twice.
- Work calls `work/project-categories` twice.
- `employees/{id}/identity` is called once per person shown (3 to 4 separate calls per screen).
- Attendance calls `time-tracking/history-detail` for today and gets a 404.

## Why the inbox and chat are slow

All four inbox endpoints share one cause. `InboxRouter.CollectAsync` asks every module's source provider for all of the caller's items, one provider after another, and puts them in one list in memory. Only then does it filter, sort and cut the page.

- `GetCountsAsync` runs that full collection just to count.
- `ListAsync` runs it and then takes 25 rows.
- `GetFiltersAsync` asks every provider for its filter fields, which also takes 3 seconds.

The chat list (`InboxChatService.ListAsync`) adds more on top:

1. It runs the same full inbox collection with a page size of 100.
2. It reads the caller's conversations.
3. It runs `InboxChatProjector.ProjectAsync`, which writes during a GET: it creates chats, inserts case cards and adds "resolved" lines. For each item it can make several more database calls inside a loop.
4. It reads the caller's conversations again.
5. It runs about eight more queries to build the rows.
6. It returns every chat. For this user that is 274 rows and 183 KB, with no paging.

Opening one conversation is fine: `conversations/{id}/messages` takes 130 to 160 ms.

## The 70 ms floor

Endpoints that return almost nothing (`calendar/my-timezone` with 27 bytes, `calendar/connections` with 18 bytes) still take about 70 ms. That is the cost every request pays before its own work starts. It matches the two findings from the code scan: the session ticket store reads session, tenant, user, employee and permissions on every request, and the tenant context is set and cleared around each query. Improvements tasks 02 and 03 target this. Removing it would bring most of the 200 to 360 ms endpoints under the target without touching their handlers.

## What to fix first

| Order | Fix | Task file |
|---|---|---|
| 1 | Inbox counts without loading the whole inbox | `Improvements/person-2-performance-and-db/task-00a ...` |
| 2 | Chat list that only reads, with paging | `Improvements/person-3-api-and-pagination/task-00b ...` |
| 3 | Inbox list and filters paged in the database | `Improvements/person-4-deployment-and-architecture/task-00c ...` |
| 4 | Frontend: stop the duplicate inbox, chat and planner calls | frontend `Improvements/person-2-store-and-data/task-00a ...` |
| 5 | Per-request overhead | tasks 02 and 03 |
| 6 | `dashboard/team/action-items` at 1.1 s | task 14 (query loops) and task 20 (dashboard requests) |

## Not measured

- Write endpoints (POST, PUT, DELETE).
- Screens that need a selected item: task board, backlog, project tree, sprint views, employee profile, settings pages.
- Any other role. The tenant owner sees the most data, so this is close to the worst case for one user.
- Load from more than one user at a time.
