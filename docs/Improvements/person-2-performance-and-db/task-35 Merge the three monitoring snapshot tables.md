# Task 35: Merge the three monitoring snapshot tables

| | |
|---|---|
| Priority | 35 of 42 (lower number = build first). Added 2026-11-15 |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day. Split with a suffix (a, b) if the migration work does not fit |
| Area | DB, Performance |
| Depends on | nothing |

## What we found

Review: `docs/reports/database/2026-11-15-database-design-review.md`, section A1.

`activity_snapshots`, `app_usage_snapshots` and `device_state_snapshots` share the same key columns (`tenant_id`, `employee_id`, `agent_device_id`, `captured_at`) and have exactly 13,604 rows each. They are the three largest tables in the database. One capture from the tray app becomes three inserts into three tables. Each table has a random GUID clustered key plus two more indexes, so one capture touches nine index structures.

`idle_seconds` is stored in two of them. The foreground process name is stored in two of them. The earlier code scan also found three ingest handlers that share the same 30 lines.

## What to add and change

1. Confirm the one-to-one assumption on real tray data first: do all three always arrive together, and can one capture ever carry several app usage rows? If several apps per capture are possible, keep `app_usage_snapshots` as a child table and merge only the other two.
2. Create `monitoring_snapshots` with: `id`, `tenant_id`, `employee_id`, `agent_device_id`, `captured_at`, the activity columns (`keyboard_events_count`, `mouse_events_count`, `active_seconds`, `idle_seconds`, `intensity_score`), the app columns (`process_name`, `window_title_hash`), the device columns (`is_idle`, `latitude`, `longitude`, `accuracy_meters`). Columns from a part that was not captured are nullable.
3. Cluster the table on `(tenant_id, captured_at, id)`, not on `id`. New rows then go to the end of the table, and the retention purge deletes from the front.
4. Add one nonclustered index: `(tenant_id, employee_id, captured_at)`. Add the device index only if a query needs it.
5. Add a unique index on `(tenant_id, agent_device_id, captured_at)` so a retried upload cannot insert the same capture twice.
6. Migration: copy existing rows with one `INSERT ... SELECT` joining on the four key columns, in batches. Keep the old tables for one release, then drop them in a second migration.
7. Replace the three ingest handlers with one, and update the daily summary job and the readers.
8. Register the new table in `RlsTableRegistry` and add its policy in the same migration.

## Done when

- One capture is one insert.
- Row counts match before and after the copy.
- The architecture suite (RLS coverage) passes.
- The migration is created and reported, not applied to a shared database.
