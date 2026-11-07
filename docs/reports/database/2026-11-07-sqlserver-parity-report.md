# SQL Server parity report

**Date:** 2026-11-07
**Compared:** a clean SQL Server database built from migrations and booted in Production, against the PostgreSQL capture from the old migration chain.

## RLS policies

| | PostgreSQL | SQL Server |
|---|---|---|
| Tables with RLS | 166 | 166 |
| Admin or tenant match | 160 (includes `users` and `sessions`) | 158, plus `users` and `sessions` with their read rules |
| Tenant match only | 3 | 3 |
| Admin, system or tenant match | 3 | 3 |
| Session key lookup | 1 extra policy on `sessions` | Folded into the `sessions` read rule |

`EveryRegistryTable_HasAnEnabledPolicy` checks both directions: every table in the registry has an enabled policy, and no policy exists outside it.

**Gap carried over:** `position_access_templates`, `billing_audit_logs` and `outbox_messages` have `tenant_id` but no RLS, on PostgreSQL and here.

## Reference data

`ProductionBoot_MatchesPostgresReferenceData` compares every table: row counts, and all values except generated ids, timestamps and the test admin's details. It passes for all 20 tables and 455 rows, with no allowlisted differences.

| Source | Tables |
|---|---|
| Startup seeders | 16 tables |
| EF `HasData` in the baseline | `platform_providers`, `subscription_plans` |
| `SeedReferenceData` migration | `module_catalog`, `employment_statuses`, plus one `subscription_plans` update |

The `subscription_plans` update carries a later PostgreSQL migration that renamed `monitoring` to `activity_monitoring` in the Starter plan. The `HasData` value in code was never updated.

## Sorting the old migration inserts

The other `INSERT INTO` statements in the old migrations were backfills of existing tenant data, and do nothing on an empty database. The parity test confirms none of them left reference rows behind.
