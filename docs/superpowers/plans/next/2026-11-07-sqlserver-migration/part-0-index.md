# SQL Server Migration Implementation Plan (index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crownus runs on Microsoft SQL Server with tenant isolation at least as strong as it is on PostgreSQL today, proven under the restricted app login.

**Architecture:** Swap the EF Core provider, replace 268 migrations with one generated baseline plus hand-written RLS, trigger, procedure and reference-data migrations, and rewrite Postgres-only SQL. RLS uses `SESSION_CONTEXT` with per-table security policies.

**Tech Stack:** .NET 10, EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`), `Microsoft.Data.SqlClient`, `EFCore.NamingConventions`, xUnit, `Testcontainers.MsSql`.

**Spec:** `docs/superpowers/specs/next/2026-11-07-sqlserver-migration-design.md`

## Parts, in order

| Part | Delivers | Gate before the next part |
|---|---|---|
| `part-1-capture-and-spike.md` | Postgres truth captured; RLS approach proven | **User sees spike results. If the parallel stress test fails, stop.** |
| `part-2-provider-baseline-rls.md` | API builds on SQL Server, clean database from migrations, RLS on | Clean database created; policy parity report clean |
| `part-3-raw-sql-and-behaviour.md` | All Postgres-only SQL gone | No `Npgsql` reference left in `src` |
| `part-4-tests-and-gate.md` | Tests moved, live RLS proof, full gate | Final report |
| `part-5-finish.md` | Replaces unfinished Part 4 tasks: architecture tests, integration port, CI, final gate | Final report |

## Global Constraints

Every task in every part includes these.

- Repository: `C:\onevoNew\HRMS-Backend-sqlserver`, branch `feature/sqlserver-migration`, cut from `origin/development` at `893f105f`. Never use git worktrees. Never touch `C:\onevoNew\HRMS-Backend-v1` or its branches.
- Never connect to `vm-tic-incubato` (9.223.115.62) or its instance `MSSQLSERVER01`. Do not touch the server at all. Use throwaway containers only.
- Do not apply migrations to a shared database. Do not push or merge unless the user asks.
- Do not rename code identifiers or `ONEVO.*` namespaces. Never touch the AES salt.
- Never weaken, skip or delete an RLS test to get a pass. Only the tests the spec lists as retired may go.
- Do not suppress analyzers or use `--no-verify`.
- Keep `UseSnakeCaseNamingConvention`, `EnableRetryOnFailure(3)` and `CommandTimeout(30)`.
- Login names stay `onevo_app`, `onevo_migrator`, `onevo_auth_base_login_fn_owner`.
- Session context keys stay `app.current_tenant_id` and `app.tenant_context_mode`; `app.session_lookup_key_hash` for the session lookup.
- Database collation `Latin1_General_100_CS_AS_SC`. `READ_COMMITTED_SNAPSHOT ON`. Database-scoped `MAXDOP = 1`.
- No `OPTION (MAXDOP ...)` hint anywhere in `src`: a hint would override the database setting.
- Container image: `mcr.microsoft.com/mssql/server:2022-latest` until the VM's version is known, then pin to that version.
- Reports go in `docs/reports/database/` with a `2026-11-07-` style date prefix.
- Commit to the branch after each task. End commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Gate commands: `pwsh ../dev-env/scripts/verify.ps1 backend -Full` if `dev-env` turns up. Otherwise run the raw commands from `CLAUDE.md` and report that the script could not run.

## Review Focus

Things no spec section spells out, most likely to bite first. Each has a test in the task named.

1. **Rollback after a tenant switch.** Postgres `set_config` reverts on rollback; `SESSION_CONTEXT` does not. After a rollback the connection must not stay on the switched tenant (Part 1 Task 2, Part 2 Task 4).
2. **Retry and reconnect.** When EF retries after a deadlock or dropped connection, the new connection must get the same context, or none, and never another tenant's (Part 2 Task 4).
3. **Background jobs reusing pooled connections** straight after an HTTP request must not inherit that request's tenant (Part 1 Task 2, Part 4 Task 3).
4. **Hash and email case.** A `key_hash` differing only by letter case must not match; login by mixed-case email must still work (Part 2 Task 2, Part 4 Task 3).
5. **Two workers at once.** Two outbox or job-lease workers must never claim the same row, and upserts under contention must not surface as 500s (Part 3 Task 1).
