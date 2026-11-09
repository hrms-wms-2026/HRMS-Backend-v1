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

**Stricter than PostgreSQL:** four tables with `tenant_id` had no RLS there and now do, at the user's request, taking the total to 170.

| Table | Rule | Why |
|---|---|---|
| `position_access_templates` | Standard tenant rule | Only used with a tenant set. |
| `global_email_directory` | Standard tenant rule | Written while accepting an invitation, in that tenant's context. Nothing reads it. |
| `outbox_messages` | `PlatformShared` | Null `tenant_id` is valid, and anonymous paths write it. System mode processes every tenant's messages. |
| `billing_audit_logs` | `PlatformShared` | Null `tenant_id` is valid, and Stripe webhooks write it with no tenant context. |

`PlatformShared` allows admin and system mode, limits tenants to their own rows, and lets a tenant insert an unowned row that it can never read back.

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
