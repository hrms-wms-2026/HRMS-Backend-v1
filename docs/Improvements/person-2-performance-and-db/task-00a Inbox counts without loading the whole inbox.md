# Task 00a: Inbox counts without loading the whole inbox

| | |
|---|---|
| Priority | Urgent. Do before task 01. Added 2026-11-15 after measuring |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, DB |
| Depends on | nothing |

## What we found

Measured on 2026-11-15 (`docs/reports/performance/2026-11-15-api-response-timings.md`):

- `GET /api/v1/inbox/counts` takes 3.1 to 3.3 seconds when called alone and returns 0.3 KB.
- The app shell calls it on every page load. There, running next to the other calls, it took 3.6 to 6.3 seconds on all ten screens measured. It is the slowest call on every screen except the Inbox itself.
- While it runs it holds a database connection and slows the other calls on the page.

Cause: `InboxRouter.GetCountsAsync` calls `CollectAsync` with an empty query. That asks every module's source provider for all of the caller's items, one provider after another, builds them fully in memory with their actions, and then counts the list.

## What to do

1. Add a count method to `IInboxSourceProvider`, for example `CountAsync(tenantId, callerEmployeeId, ct)`, that returns pending received, pending sent and unread numbers using `COUNT` queries only. No item is built.
2. Implement it in each provider (leave, attendance, work, core HR, calendar, monitoring and the rest). Reuse the same permission and visibility filters that `ListAsync` uses, so the numbers match the list exactly.
3. Change `GetCountsAsync` to sum the provider counts. Run the providers one after another on the same connection first; measure before trying anything parallel, because they share one DbContext.
4. Add a short cache per caller (10 to 30 seconds) and clear it when `InboxChangedAsync` fires for that employee.
5. Add a test per provider: the count equals the number of rows the list returns for the same caller.
6. Use the SQL command counter from task 06 if it exists, or EF command logging, to show the query count before and after.

## Done when

- `GET /inbox/counts` answers in under 200 ms for the dapi owner user.
- Counts equal the list totals in the tests.
- The timing report has a new line with the after number.
