# 6. Database and Migration Readiness Report

## Table of Contents

1. [Summary](#1-summary)
2. [Fresh Database Run](#2-fresh-database-run)
3. [Migration Inventory](#3-migration-inventory)
4. [Data-Changing Migrations](#4-data-changing-migrations)
5. [Deployment Order](#5-deployment-order)
6. [Risks and Gaps](#6-risks-and-gaps)

## 1. Summary

All 268 migrations on `development` apply cleanly to an empty PostgreSQL 16 database. The post-migration grants then apply cleanly as well. Data-changing migrations run under RLS admin mode on purpose. **Not verified:** upgrading a copy of the real shared or UAT database. That is the main remaining migration risk.

## 2. Fresh Database Run

| Step | Command | Result |
|---|---|---|
| 1. Bootstrap roles | `psql -f ops/postgres/local-bootstrap-roles.sql` with `db_name`, `app_user`, `migrator_user`, `base_login_fn_owner` | Pass |
| 2. EF migrations as `onevo_migrator` | `dotnet ef database update -p src/ONEVO.Infrastructure -s src/ONEVO.Api` | Pass. 268 rows in `__EFMigrationsHistory`. About 5 minutes, including the build. |
| 3. Post-migration grants | `psql -f ops/postgres/local-post-migration-grants.sql` | Pass |
| 4. API start as `onevo_app` | `dotnet run --launch-profile https` | Pass. Seeders completed. |

## 3. Migration Inventory

| Month | Migrations |
|---|---:|
| 2026-05 to 2026-06 | 13 |
| 2026-07 | 39 |
| 2026-08 | 99 |
| 2026-09 | 50 |
| 2026-10 | 67 (latest: `20261028170646_AddWeeklyProgressReadIndexes`) |

`feature/planner-redesign-backend` adds one more migration, `20261028233312_AddTaskCategoryKind`. It applied cleanly on top of `development` during the earlier merge check.

## 4. Data-Changing Migrations

| Migration | What it does | Assessment |
|---|---|---|
| `20261019040520_RemoveLeaveRequestInfoAndForward` | Resets leave requests and approvers in `information_requested` or `forwarded` back to `pending`/`skipped`; deletes related outbox rows, notifications and templates; drops a table. | Removes a feature. Runs in admin mode. **Not reversible:** take a backup before UAT deployment. |
| `20261021140000_RemoveTharmiFutureFaceAttempts` | Deletes future-dated face attempts for one hard-coded tenant and employee. | One-off fix inside a schema migration. Does nothing on other databases. P3 hygiene item (UAT-016). |
| `20261021150000/160000_BackfillDecisionLine*` | Backfills the decision journal. | Admin mode. OK. |
| `20261021181949`, `20261025220540`, `20261027090000` | Replace the module-hour summary function and trigger, then backfill every objective. | Admin mode inside `BackfillAllModules()`. Run time grows with the number of objectives. |
| `20261020100548`, `20261020105849` | Leave policy versioning and ledger policy version backfill. | Admin mode. OK. |

## 5. Deployment Order

This is required in every environment (from `ops/postgres/README.md`):

1. Bootstrap roles (database owner or DBA).
2. EF migrations as `onevo_migrator`.
3. Post-migration grants.
4. Start the API as `onevo_app`.

The API never migrates on startup. Per `CLAUDE.md`, coding agents must **not** apply migrations to shared databases. A developer applies them.

## 6. Risks and Gaps

| ID | Severity | Item |
|---|---|---|
| UAT-004 | P1 | The integration suite, which exercises RLS and repositories against real PostgreSQL, was not run on this HEAD. |
| UAT-020 | P2 | No rehearsal of upgrading a copy of the current UAT or shared database to HEAD (only an empty database was tested). |
| UAT-016 | P3 | Hard-coded tenant data fix inside a schema migration. |
| Info | | `dotnet ef migrations has-pending-model-changes` needs `MigrationConnection`. It was not run separately, but the fresh migration plus the architecture snapshot tests passed. |
