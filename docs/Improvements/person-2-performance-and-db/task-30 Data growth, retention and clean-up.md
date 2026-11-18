# Task 30: Data growth, retention and clean-up

| | |
|---|---|
| Priority | 30 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | DB, Scaling |
| Depends on | 18 |

## What we found

Some tables grow without a natural limit and will slow every query that touches them:

- Monitoring: activity snapshots, app usage, screenshots. Settings exist for 14 days of raw buffer and 90 days of screenshots, and a `MonitoringRetentionPurgeJob` exists.
- `sessions`: one row per sign-in. Check whether expired and revoked rows are ever removed.
- Outbox messages, notifications, tray notification deliveries, audit and activity history, login challenge rows, idempotency keys.
- Attendance events and daily summaries for every employee every day.

Only monitoring has a visible retention rule in settings. The migrations folder is 2.6 MB for 6 migrations, which suggests one very large baseline: check that a fresh database can be created in reasonable time.

## What to do

1. List every table that grows with time, its current retention rule and its expected size after one year for a 500-employee tenant.
2. Agree a retention period for each with the product owner (some are legal records and must stay).
3. Add purge jobs in small batches (`DELETE TOP (n)` in a loop) so they never hold long locks. Reuse the job base from task 28.
4. For the largest tables consider monthly partitions or an archive table.
5. Check the purge jobs respect row-level security and run per tenant.
6. Add the row counts of these tables to the metrics from task 17.

## Done when

- The retention table is saved in `docs/deployment/data-retention.md` and approved.
- Each growing table has either a purge job or a written reason to keep everything.
- Purge runs are visible in the logs with row counts.
