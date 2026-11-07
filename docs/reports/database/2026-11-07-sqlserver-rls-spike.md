# SQL Server RLS spike results

**Date:** 2026-11-07
**Result:** 38 of 38 tests pass. The design holds, with the additions listed below.
**Server tested:** Microsoft SQL Server 2022 CU27-GDR (16.0.4305.5), Docker container, 12 CPUs. Not the VM.
**Code:** `spikes/sqlserver-rls/`. The T-SQL that worked is `schema.sql` there; Part 2 copies it.

## What was proven, as `onevo_app`

| Area | Result |
|---|---|
| No context, unknown mode, `system` mode on a standard table | 0 rows |
| `system` mode on a tray-style table | Sees rows, as on Postgres |
| Tenant A | Sees only tenant A |
| Insert for another tenant, moving a row to another tenant | Blocked, error 33504 |
| Update or delete of another tenant's row | 0 rows affected, row unchanged |
| `ALTER`/`DROP SECURITY POLICY`, `ALTER TABLE`, `CREATE TABLE`, `EXECUTE AS` the lookup user or `dbo` | Refused |
| Calling a predicate function directly | Refused |
| Pooled connection reused (same SPID) | Context cleared, 0 rows |
| Session lookup with no tenant | Finds the row; key cleared even after a failure |
| `key_hash` differing only by case | No match |
| MERGE: cross-tenant update, insert, and moving a row | Blocked, same as plain statements |
| EF batched insert (confirmed to use MERGE) | Blocked for another tenant, works for own |
| EF save on a table with a trigger | Works with `HasTrigger`; trigger's write passes RLS |
| Login lookup procedure | Works with no context, limited to 9 rows; app login still sees 0 users directly; lookup user cannot read other columns |

## Parallel plans

- Control: the heavy query ran at degree of parallelism 12 with `MAXDOP = 0`, and serially (0) with database-scoped `MAXDOP = 1`. The setting works and is not overridden by RLS.
- Stress at `MAXDOP = 1`: 32 workers, 8 tenants, 20,000 pooled queries: **0 wrong rows, 0 empty results, 0 errors.** Rate was 75 queries per second, but each query scans 500,000 rows, so this is not a measure of real app speed.
- Stress at `MAXDOP = 0`, for information: 2,000 queries, 0 wrong rows. **This does not show parallel plans are safe.** It covers one patch level and a short run, and Microsoft documents the issue, so `MAXDOP = 1` stays.

## Findings to carry forward

1. **`SESSION_CONTEXT` survives a rollback.** Confirmed: after switching to tenant B in a transaction and rolling back, the connection is still on B. Postgres would have reverted. Part 2 Task 4 must restore it.
2. **Fourth policy shape on Postgres.** `file_records`, `file_upload_reservations` and `work_area_change_requests` have no admin exception. Added `rls.fn_tenant_strict` so they stay exactly as strict.
3. **`users` and `sessions` need separate read functions.** Their block predicates stay standard; only the filter gains the lookup rule. `tenants` has no `tenant_id` and no policy, on Postgres or here.
4. **The migrator needs one extra grant:** `GRANT SELECT, ALTER ON SCHEMA::rls` to bind a policy to a predicate function. The app login must not get it.
5. **`sa` is not exempt.** It needs admin context to seed tenant tables, so seed and data scripts must set it.

## Policy inventory

From a real Postgres run (`2026-11-07-postgres-policy-inventory.json`): 166 tables with RLS, 167 policies, all with `FORCE`.

| Shape | Tables |
|---|---|
| Admin, or tenant match | 160 |
| Tenant match only | 3 |
| Admin, system, or tenant match | 3 (tray activation) |
| `session_key_lookup`, added to `sessions` | 1 |

**Existing gap, not caused by this work:** 3 tables have `tenant_id` but no RLS on Postgres today: `position_access_templates`, `billing_audit_logs`, `outbox_messages`. Parity keeps them as they are. Whether to cover them is a separate decision.

## Reference data

A Production boot on an empty database, as `onevo_app`, leaves 20 tables and 455 rows (`2026-11-07-postgres-reference-data.json`), including 83 permissions, 59 notification templates and 27 module catalog entries. That is far less than the migrations suggested. The single `platform_users` row is the test bootstrap admin.

## Not proven here

- The real schema: 166 tables, triggers and app code. Part 4 covers that.
- Older patch levels or versions. The VM's version is still unknown, and the documented `SESSION_CONTEXT` issue depends on patch level.
- Reconnect and retry behaviour. Part 2 Task 4 covers that.
