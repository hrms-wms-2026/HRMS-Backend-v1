# Task 06: Measure every endpoint against 200 ms

| | |
|---|---|
| Priority | 6 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance |
| Depends on | nothing |

## What we found

The team target is 200 ms per API call, but nothing measures it. `PerformanceBehavior` only warns above 500 ms and only covers MediatR handlers, so authentication, middleware and serialisation time are invisible. Logs go to the console only.

## What to do

1. Log the full request duration per route template (not per raw URL) with tenant and correlation id. `UseSerilogRequestLogging` is already in place; add the route template and make sure the elapsed time is kept.
2. Lower the slow-handler warning from 500 ms to 200 ms and make the number a setting.
3. Add a count of SQL commands and total SQL time per request (an EF command interceptor that adds to a scoped counter, written into the request log line).
4. Seed a realistic data set on a UAT-like database: at least one tenant with 500 employees, 50 projects, 20,000 tasks, one year of attendance.
5. Call every GET endpoint once warm and record p50 and p95.
6. Write the result table to `docs/reports/performance/2026-11-baseline.md`, sorted slowest first, with the SQL command count next to each.

## Done when

- The baseline report lists every GET endpoint with time and query count.
- The 20 slowest endpoints are named. They become the work list for tasks 14, 18 and 22.
- The same run can be repeated with one command.
