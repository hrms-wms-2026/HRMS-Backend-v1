# Task 18: Index review from real query data

| | |
|---|---|
| Priority | 18 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | DB, Performance |
| Depends on | 06, 08, 12 |

## What we found

The model has 215 `DbSet`s and 406 `HasIndex` calls, so indexes were thought about. But they were designed before the list endpoints had paging and server-side filters, and nobody has checked them against real query plans. Tasks 08 and 12 add `ORDER BY` plus `OFFSET` queries that need matching indexes or they will scan.

Row-level security adds a tenant predicate to every query, so an index that does not start with `tenant_id` is often not used.

## What to do

1. Turn on Query Store on the UAT database.
2. Run the baseline from task 06 again and export the top 30 queries by total duration and by logical reads.
3. For each one read the plan: look for scans on large tables, key lookups and sorts.
4. Add or change indexes: `tenant_id` first, then the filter columns, then the sort column, with `INCLUDE` for the list columns. Prefer filtered indexes for soft-deleted tables (`WHERE is_deleted = 0`).
5. Look for unused indexes on write-heavy tables (activity snapshots, attendance events) and list them for removal.
6. Every change is an EF migration. Do not apply it to a shared database yourself; report it.

## Done when

- A report in `docs/reports/performance/` shows each of the top 30 queries with its plan problem and the fix.
- None of the top 30 does a full scan on a table above 100,000 rows.
- The migration is created and reviewed.
