# Task 18: index review from Query Store

**Date:** 2026-11-27. **Branch:** `perf/task-18-index-review`, on top of task 06 part B (#286) and `development`.
**Decision:** DEC-118 (local Query Store now; paged queries in 18b after tasks 08 and 12).

## Summary

- **Two index changes, one migration** (`20261127063521_ReviewTaskIndexesFromQueryStore`). Both replace an existing
  index of the same name, so the number of indexes the writes keep up to date does not change.
- **Measured effect:** the led-progress endpoints are 2–3× faster: `work/led-progress/employees` drops from 142 ms to 43 ms
  at p50, and its SQL time from 118 ms to 21 ms. The task-tree queries behind them fall out of the top 30.
- **What indexes cannot fix:** the rest of the slowest endpoints come from the query shape, not from a missing index.
  Those are listed below with the task that should take them: task 40 for `attendance_records`, task 14 for the inbox,
  and code changes for the forecast and the project calendar.
- **Done-when check:** one of the top 30 still scans a table above 100,000 rows (`attendance_records`, 182,464 rows).
  The fix is the clustered key, which is task 40's work, so it moves there with the evidence.

## How it was measured

| | |
|---|---|
| Data | Throwaway `OnevoDb_perf` with the task 06 seed: 500 employees, 50 projects, 20,313 tasks, 182,464 attendance rows |
| Load | `ops/perf-baseline/run-baseline.ps1`, every tenant GET, one warm-up and 20 timed calls each |
| Query Store | `QUERY_CAPTURE_MODE = ALL`, cleared before each run, so each run's store holds only that run |
| Top queries | `ops/perf-baseline/query-store-top.sql` (top 30 by total duration and by logical reads) |
| Plan problems | `ops/perf-baseline/plan_issues.py`: scans with table size, key lookups, sorts, missing-index hints |
| Noise | Two runs with no change differ by up to 20–60 ms at p50 on the heaviest endpoints. Only changes larger than that, and backed by a drop in logical reads, are counted |

The same two scripts run unchanged on UAT once Query Store is on there.

## Changes

### 1. `tasks`: top-level tasks of a set of objectives

The led-progress, work-graph and team views all ask the same thing:
`parent_task_id IS NULL AND objective_id IN (…)`, then sum hours, progress and status. The old
`(tenant_id, parent_task_id)` index found every top-level task of the tenant (about 20,000) and fetched each row from
the table to check the objective. Eleven of the top 30 queries had this shape, with 1,500–50,000 logical reads each.

| | Key | Include |
|---|---|---|
| Before | `tenant_id, parent_task_id` | — |
| After | `tenant_id, parent_task_id, objective_id` | `status_id, task_kind, is_deleted, estimated_hours, progress_percent, due_date, completed_at` |

The included columns are the ones those queries read, so the index answers them without touching the table. Lookups by
parent task alone (`GetTrackedByParentTaskIdAsync`) still use the same leading columns.

| Queries with this shape | avg ms | avg logical reads |
|---|---|---|
| Before (11 of the top 30) | 11.7–41.2 | 1,507–50,485 |
| After | 1.2–14.4, one at 30.2 (a paged list that sorts by hours) | 50–4,273 |

### 2. `task_assignments`: tasks assigned to a set of people

`ix_task_assignments_employee_id` now includes `task_id`. "Tasks assigned to these people" reads nothing else, so this
removes one table lookup per assignment.

### Endpoint timings (p50, before → after)

| Endpoint | p50 ms | SQL ms |
|---|---|---|
| `work/led-progress/employees` | 141.6 → 43.1 | 118.4 → 20.8 |
| `work/led-progress` | 91.1 → 33.0 | 70.5 → 10.7 |
| `work/led-progress/weekly-completion` | 66.9 → 28.2 | 48.0 → 8.2 |
| `employees/{id}/work-graph` | 137.2 → 99.5 | 116.2 → 80.5 |
| `work/led-progress/tasks` | 89.3 → 66.1 | 67.1 → 45.5 |

Other endpoints moved within the noise.

## Found, not fixed here

| # | Query (rank by duration) | What the plan shows | Where to fix |
|---|---|---|---|
| 1 | `attendance_records` by employee list and date range, `ORDER BY date DESC OFFSET` (rank 6; team attendance endpoints) | Full scan of 182,464 rows for 30 employees, 97 ms. The `(tenant_id, employee_id, date)` index exists, but it does not cover the selected columns, and the optimizer prefers the scan to about 700 lookups | **Task 40:** cluster `attendance_records` on `(tenant_id, employee_id, date)`. The rows of one person's month then sit together, and no lookup is needed |
| 2 | Forecast tasks query (rank 3, 191 ms, 13,962 rows per call) and its `task_assignments` count by `OPENJSON` of 13,962 ids (rank 4, 113,451 reads) | Not an index problem. `work/forecast/employees` loads every open task of the legal entity, then counts assignees for each | Forecast code: aggregate in SQL, or limit the load to the forecast window |
| 3 | `GetByProjectAsync` (rank 1 by duration: 504 runs of 25 ms) | Clustered scan of `tasks` each time. The filter is `objectives.project_id`, so the `(tenant_id, project_id, …)` indexes on `tasks` cannot be used | Filter on `tasks.project_id` directly (the column exists and is indexed). The callers are the calendar handlers and the project monitor; 504 runs during the run point to the background project monitor |
| 4 | Inbox reads (`inbox`, `inbox/counts`, `inbox/filters`, `inbox/chats`) | About 300 small commands each, none slow on its own | Task 14 (#276) |
| 5 | `planned_task_blocks` and `task_clocking_sessions` by employee list and time range | Small scans (2,900 rows), plus missing-index hints on `(tenant_id, is_deleted)` with a range on start and end | Too small to judge here. Recheck on UAT with the same script, because these tables grow with use |

## Unused index candidates (list only, nothing dropped)

From `sys.dm_db_index_usage_stats` after the seed and two baseline runs. Local usage is not production usage, so these
need a UAT check before anyone drops them:

| Table | Index | Reads | Writes | Note |
|---|---|---|---|---|
| `attendance_records` | `ix_attendance_records_employee_id` | 0 | 4,394 | Foreign key index; deletes of employees are soft |
| `attendance_records` | `ix_attendance_records_expected_work_mode_id` | 0 | 4,394 | Foreign key index |
| `tasks` | `ix_tasks_category_id`, `ix_tasks_parent_task_id`, `ix_tasks_status_id`, `ix_tasks_objective_id` | 0 | 834 | Single-column foreign key indexes. The tenant-first composite indexes cover the reads |
| `planned_task_blocks` | `ix_planned_task_blocks_task_id` | 0 | 87 | Foreign key index |
| `task_clocking_sessions` | `ix_task_clocking_sessions_task_id` | 0 | 113 | Covered by `(tenant_id, task_id, clock_in_at)` |
| `break_records`, `presence_sessions` | `…_employee_id` | 0 | 35–67 | Foreign key indexes |

Single-column foreign key indexes also serve the foreign key check when a parent row is deleted. Drop one only where the
parent is never hard-deleted, or where another index starts with that column.

## Applying the migration

The migration drops and recreates two indexes. On a large `tasks` table this takes a schema lock for the duration of the
build. Apply it outside working hours, or add `ONLINE = ON` by hand on an edition that supports it. It was applied
only to the throwaway database here; it is not applied to any shared database.

## Tests

Unit 8692/8692, architecture 847/847, integration `SqlServerMigration` (schema parity, migrations, RLS) 317/317. The full
integration suite was not run; the change touches only two index definitions.

## Not covered

- **Paged list queries** from tasks 08 and 12 are not in `development` yet. Task 18b reruns these scripts after they merge.
- **UAT data shape.** The seed spreads data evenly; real tenants are skewed. Rerun on UAT with the same two scripts.
