# Task 40: Index and clustered key fixes

| | |
|---|---|
| Priority | 40 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Performance |
| Depends on | 18 |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, sections C4, C5 and C6.

- **Random clustered keys.** 205 tables cluster on a `uniqueidentifier` primary key, and the code creates ids with `Guid.NewGuid()` in 480 places. Each new row lands on a random page. On tables that only ever grow, this causes page splits, fragmentation and a larger buffer pool than needed.
- **No tenant-first index on 20 tenant tables.** Row-level security adds a `tenant_id` filter to every query. Tables with rows today: `conversation_messages` (2,450 rows, only indexed on `conversation_id, created_at`), `outbox_messages`, `task_comment_reactions`, `role_permissions`, `dashboard_widget_layouts`, `user_roles`, `dashboard_widgets`.
- **27 redundant indexes.** Each is a prefix of another index on the same table, so it costs writes and gives nothing. `positions`, `departments`, `clock_in_policies`, `clock_in_late_deduction_rules` and `tenant_work_modes` each have `(tenant_id)`, `(tenant_id, X)` and `(tenant_id, X, Y)`.
- `tasks` has 11 indexes, including `(parent_task_id)` next to `(tenant_id, parent_task_id)`, and single-column indexes on `status_id`, `sprint_id` and `category_id`.

## What to add and change

1. Insert-heavy tables: change the clustered index to `(tenant_id, <time column>, id)` and keep the primary key on `id` as a nonclustered unique index. Do this for the snapshot table from task 35, `conversation_messages`, `notification_deliveries`, `notifications`, `attendance_records`, `break_records`, `task_clocking_sessions`, `presence_sessions`, `face_verification_attempts`, `outbox_messages`, the activity log from task 38.
2. For all other tables, generate ordered ids. Important for SQL Server: it sorts `uniqueidentifier` by its last bytes first, so `Guid.CreateVersion7()` is not ordered there. Use EF Core's `SequentialGuidValueGenerator`, which is built for SQL Server ordering, set once in the model convention. Ids created by hand in code with `Guid.NewGuid()` stay valid.
3. Add a tenant-first index to the 7 tables with rows. For `conversation_messages` use `(tenant_id, conversation_id, created_at)` and drop the old one.
4. Drop the 27 prefix indexes. The exact list is in the report query output; regenerate it with the query in the report before dropping.
5. `tasks`: with an admin login read `sys.dm_db_index_usage_stats` after a normal day of use on UAT, and drop the indexes with no seeks or scans. Expect to end with 6 or 7.
6. Run the same usage query for the whole database and list every index that was never read.
7. Every change is an EF migration. Changing a clustered index rebuilds the table, so note the expected duration per table for the UAT run.

## Done when

- The named insert-heavy tables cluster on tenant and time.
- No tenant table with data lacks a tenant-first index.
- The redundant index query returns no rows.
- The unused index list from UAT is saved in `docs/reports/database/`.
