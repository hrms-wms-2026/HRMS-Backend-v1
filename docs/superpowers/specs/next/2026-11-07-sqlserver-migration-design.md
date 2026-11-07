# Crownus: move from PostgreSQL to Microsoft SQL Server (design)

**Status:** Approved by the user on 2026-11-07, including the deferred-trigger move (section 8) and the retired tests (section 9).

## 1. Why, and what done means

The team test VM runs SQL Server only, and the user has chosen SQL Server. (The reason behind that choice is still an open question in section 11. It would change the design only if it means a specific version or feature.)

Done means:
- The API builds and runs on SQL Server.
- A clean database can be created from migrations.
- RLS blocks cross-tenant reads and writes under the restricted app login, proven by tests.
- `verify.ps1 backend -Full` passes, or any failure is reported plainly.

Not in scope: deployment, installing anything on the VM, the server itself, renaming code identifiers or `ONEVO.*` namespaces, the AES salt.

What would make this a failure even if it works: tenant isolation that is weaker than today, or that looks equal only because tests were loosened.

## 2. Decisions

| Decision | Choice | Made by |
|---|---|---|
| Database engine | SQL Server | User |
| Migrations | New SQL Server baseline replaces the 268 | User |
| Team cutover | Team keeps working; baseline regenerated at merge time after a short freeze | User |
| Test server | Docker + Testcontainers throwaway SQL Server | User |
| Where the work happens | New branch from `origin/development` in a separate clone | User |
| Local dev and tests | Also move to SQL Server, no dual-provider support | Brief default, please confirm |
| Everything else below | Proposed by me | Needs approval |

## 3. Provider swap

- Replace `Npgsql.EntityFrameworkCore.PostgreSQL` with `Microsoft.EntityFrameworkCore.SqlServer` 10.x, and `Testcontainers.PostgreSql` with `Testcontainers.MsSql`.
- Keep `UseSnakeCaseNamingConvention`, so table and column names stay as they are and raw SQL stays recognisable.
- Keep `EnableRetryOnFailure(3)` and the 30-second command timeout.
- Connection strings: keep the `ONEVO_DB_*` settings, built with `SqlConnectionStringBuilder`.
- Keep class names such as `PostgresMfaChallengeStore` (no renames, per the brief).

Type mapping: uuid to `uniqueidentifier`; timestamptz to `datetimeoffset`; boolean to `bit`; text to `nvarchar(max)`; bytea to `varbinary(max)`; double precision to `float`; numeric stays `decimal`. `normalized_email` becomes a persisted computed column.

Database settings, set by the setup script and checked at API startup and in a test:
- `READ_COMMITTED_SNAPSHOT ON`, so reads do not block writes, matching Postgres behaviour.
- `MAXDOP = 1` at database level (section 4.3).
- Case-sensitive collation (section 6).

## 4. Row Level Security

### 4.1 Mapping

The rule stays the same. `app.tenant_context_mode` and `app.current_tenant_id` become `SESSION_CONTEXT` keys, set with `sp_set_session_context`.

Each covered table gets a security policy with:
- a FILTER predicate (replaces USING: SELECT, UPDATE, DELETE);
- BLOCK predicates AFTER INSERT, AFTER UPDATE (replace WITH CHECK), BEFORE UPDATE and BEFORE DELETE.

Predicates live in a dedicated `rls` schema, as schema-bound inline functions. They fail closed: a missing or unreadable context matches no rows.

| Function | Rule | Tables |
|---|---|---|
| `rls.fn_tenant` | admin, or tenant with matching `tenant_id` | Standard tenant tables |
| `rls.fn_tenant_strict` | matching `tenant_id` only, no admin exception | `file_records`, `file_upload_reservations`, `work_area_change_requests` (found in the Postgres capture) |
| `rls.fn_tenant_or_system` | also allows `system` | Tables with the existing system-mode exception (3 tray activation tables, and any others found in the parity check) |
| `rls.fn_sessions_read` | filter only: standard rule, or `key_hash` equals the lookup key | `sessions` (SQL Server allows one filter per table, so the two Postgres policies merge) |
| `rls.fn_users_read` | filter only: standard rule, or the dedicated lookup user | `users` (section 4.4). `tenants` has no `tenant_id` and no policy |

One policy per table, created through one shared helper in migrations, so a new table is one call.

**The Postgres policy list is the source of truth.** I will build the real table and policy list by running the full Postgres migration chain once in a throwaway container and reading `pg_policies`, instead of trusting statement counts. A one-off parity report will compare that list to `sys.security_predicates` and go in `docs/reports`.

### 4.2 Connection handling

- `TenantRlsInterceptor` and `TenantContextSwitcher` call `sp_set_session_context` in place of `set_config`. Clearing on close stays.
- `sessions` lookup: Postgres uses a transaction-local setting that reverts by itself. SQL Server has none, so `GetByKeyHashForTenantResolutionAsync` sets the key and clears it in `finally`. A test will show that a failed lookup leaves nothing behind on a reused connection.
- A test will confirm a pooled connection does not carry tenant A's context into tenant B's request.

### 4.3 SESSION_CONTEXT in parallel plans (main security risk)

Microsoft documents crashes or **wrong results** when `SESSION_CONTEXT` runs in a parallel plan on a session that is reset for reuse. That is precisely a pooled app using it in an RLS predicate.

- Primary mitigation: `ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1` on the Crownus database. It keeps plans serial, needs no instance change, and is reasonable at this data size.
- API refuses to start if that setting is wrong.
- Also ask the server owner about trace flag 11042, which is Microsoft's documented fix but applies to the whole instance.
- The server must also be on a current cumulative update.

### 4.4 Login lookup without a tenant

Today a `SECURITY DEFINER` function owned by a BYPASSRLS role returns login candidates before a tenant is known.

On SQL Server: a stored procedure `auth_internal.auth_lookup_base_login_candidates` `WITH EXECUTE AS` a login-less database user that has `SELECT` on only the needed columns. Predicates on `users` and `tenants` allow that user name. The app login gets `EXECUTE` on that procedure and nothing wider. The repository changes to `EXEC`. Same limit of 9 rows, same eligibility rule.

### 4.5 Logins

Names stay `onevo_app`, `onevo_migrator` and `onevo_auth_base_login_fn_owner`.

- `onevo_app`: data read/write and execute on named procedures only. Not `db_owner`, no `ALTER ANY SECURITY POLICY`, no DDL.
- `onevo_migrator`: DDL and policy rights, plus `SELECT, ALTER` on schema `rls`, not `sysadmin`. The app login must not get that grant.
- Migrations never create logins, matching today's rule. New `ops/sqlserver/` scripts replace `ops/postgres/` and run as a privileged step before migrations.

SQL Server predicates apply to everyone, including `db_owner`, so `FORCE ROW LEVEL SECURITY` has no equivalent to carry. The real risk is that `db_owner` can alter policies, which is why the test in section 9 checks the app login's rights directly.

### 4.6 Known SQL Server limits

- A schema-bound policy blocks changes to `tenant_id` until the policy is dropped. Future migrations touching that column must drop and recreate it.
- Upserts: MERGE under RLS is not documented, so no raw upsert relies on it unless a test proves it. EF's own batched inserts may use MERGE, so a test covers that too.

## 5. Baseline migration

- One `InitialSqlServer` migration generated from the current model, plus hand-written migrations for RLS, triggers, procedures and reference data.
- New snapshot. Old migrations are deleted from the branch; they stay in git history.
- Regenerated at merge time. Any new raw SQL in incoming Postgres migrations gets ported by hand.

**Reference data must not be lost.** Migrations hold inserts (`InsertData` in 6 files, `INSERT INTO` in 16), many of them backfills. Check: run the full Postgres chain plus Production seeders on an empty database, do the same for the SQL Server baseline, and compare row counts and contents for every table. Any difference is either explained in the report or fixed.

**Schema changes the baseline needs:**
- 53 `HasFilter` strings rewritten (`= false` to `= 0`, identifier quoting), and 27 check constraints reviewed.
- 49 cascade FKs: SQL Server will reject multiple cascade paths. Each rejected one becomes `NoAction`, and I will check whether any code relied on that cascade.
- `AreNullsDistinct` (2 indexes) rewritten with equivalent behaviour.
- `monitoring_face_scans.storage_key` (1000 chars) exceeds the index key limit: index a hash column, or include it instead of keying on it.

## 6. Text and case sensitivity

Postgres `=` is case-sensitive; SQL Server's common defaults are not. A case-insensitive compare on `key_hash` or a token would be a security bug, and unique indexes would start rejecting values that differ only by case.

Proposal: create the database with a case-sensitive collation (`Latin1_General_100_CS_AS_SC`), so `=` keeps today's behaviour everywhere. The 4 `ILike` calls become explicit case-insensitive compares. If the VM's instance default differs, only temp tables are affected, and the app does not use any. Sort order of names may differ slightly from Postgres.

## 7. JSON and arrays

- `jsonb` becomes `nvarchar(max)` with an `ISJSON` check, keeping today's rejection of invalid JSON. It works on all current versions, and EF's default compatibility level keeps it. Native `json` needs level 170 and is not needed, as no LINQ queries reach into these columns (to confirm while porting the 51 raw-SQL hits).
- Difference: `jsonb` normalises key order and whitespace; `nvarchar` keeps the text as written. Any test comparing text exactly will show this.
- The 6 array columns become EF primitive collections (JSON in `nvarchar(max)`); `Contains` still translates.

## 8. Database behaviour that has to be rewritten

| Today | SQL Server |
|---|---|
| `ON CONFLICT` | Insert, catch unique violation (2601/2627), or `UPDATE` then `INSERT` under `UPDLOCK, HOLDLOCK` |
| `RETURNING` | `OUTPUT` |
| `FOR UPDATE SKIP LOCKED` (outbox) | `WITH (UPDLOCK, READPAST, ROWLOCK)` |
| `FOR UPDATE` | `WITH (UPDLOCK, ROWLOCK)` in a transaction |
| Advisory locks | `sp_getapplock`, transaction-owned |
| `PostgresException` and SqlState (about 20 repositories) | One shared helper over `SqlException` numbers 2601, 2627, 547, 1205 |
| `xmin` (8 sites) | `rowversion` |
| Hour-summary triggers | `AFTER` triggers; tables declared with EF's `HasTrigger` so saves still work |

**Version token.** `xmin` is a 32-bit number exposed as `uint?`; `rowversion` is 64-bit. It stays opaque to the client but widens to `ulong`. The web app needs checking; I expect no change if it only echoes the value back. If it parses as a number, values could exceed JavaScript's safe range, and the token then becomes a string.

**Deferred constraint triggers (decision needed).** Meeting tables use `DEFERRABLE INITIALLY DEFERRED` triggers, checked at commit, to require a meeting task and its `meeting_details` row to exist together. SQL Server has no commit-time trigger, and an immediate trigger would fail on the first of the two inserts.

Proposal: move these checks into the meeting write path, inside the same transaction, with tests for each rule. The simple "key never changes" trigger stays as a normal trigger. **This is a real weakening:** an invariant moves from the database to the app, so a bug or manual script could break it. It concerns data shape, not tenant isolation. Tenant-match checks on these pairs stay enforced by RLS block predicates.

## 9. Tests

The harness moves to one shared SQL Server container. `CREATE DATABASE ... TEMPLATE` has no equivalent, so each test class restores a backup of the migrated template (slower; to be measured). Image pinned to the VM's version once known.

RLS architecture tests, old to new. None is removed without a replacement:

| Today (`TenantIsolationArchitectureTests`) | SQL Server version |
|---|---|
| The 4 query filter tests | Unchanged |
| `IgnoreQueryFilters_UsageIsExplicitlyAllowlisted` | Unchanged |
| `DefaultConnection_DoesNotUseSuperuserLookingRoleName` | Same, adding `sa` and `dbo` |
| `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` | Same model-driven check against the new policy helper, plus a stronger live test below |
| `NonMigrationSourceCode_NeverDisablesRowLevelSecurity` | Forbids `STATE = OFF`, `DROP SECURITY POLICY`, `ALTER SECURITY POLICY` outside migrations |
| `Migrations_NeverDisableRowLevelSecurity_InTheUpDirection` | Same forbidden list in `Up()`; drop-and-recreate only through a helper that re-adds the same predicates |
| `Migrations_NeverCreateRoles` | Forbids `CREATE LOGIN`, `CREATE USER ... WITH PASSWORD`, `CREATE ROLE`, role grants to `db_owner` (the login-less lookup user is created by ops scripts) |
| `Migrations_NeverPinACustomSettingOnAFunction` | Retired, no SQL Server equivalent; replaced by "no predicate or module uses `@read_only` context or `EXECUTE AS` except the allowlisted procedure" |
| BYPASSRLS allowlist exact-match check | Same exact-match check on the allowlisted procedure |

New live tests, stronger than today's static ones:
- Every model tenant table has an enabled filter and all four block predicates in `sys.security_predicates`.
- `onevo_app` is not `db_owner`, cannot alter policies, and cannot change schema.
- As `onevo_app`: cross-tenant SELECT, INSERT, UPDATE, DELETE and moving a row to another tenant are all blocked, table by table across every covered table.
- No context set means no rows.
- `MAXDOP = 1` and snapshot settings are on.
- Pool-reuse and session-lookup cleanup (section 4.2).

Existing RLS integration tests (`RestrictedRoleRlsEnforcementTests` and 7 others) are ported, not rewritten in spirit.

**To be retired because old migrations no longer exist** (needs your approval):
- `AttendanceCorrectionsMigrationUpgradeTests`
- `AddTaskCategoryKindMigrationTests`, `TaskStatusGroupsMigrationTests`, `WorkModeDataMigrationTests`
- `PositionMigrationSafetyIntegrationTests`

Architecture tests that inspect a named migration file (28 files read migrations) get re-pointed to the baseline or the model, keeping what each one asserts. Where one asserts only that a historic migration did something, it is listed for approval with the reason.

## 10. Work order

1. Set up clone and branch; run the Postgres chain once, capturing policies and reference data.
2. Spike: prove RLS, MAXDOP, pooling, MERGE and login lookup on a small schema in a container, before the bulk of the work. If the SESSION_CONTEXT mitigation does not hold up, stop and come back.
3. Provider swap and baseline until a clean database is created.
4. Rewrite raw SQL and error handling.
5. Move the test harness; port tests.
6. Full gate, reports, hand back. No push or merge unless asked.

This is a large job with real risk: expect several sessions, not one.

## 11. Open questions

1. Why SQL Server? Only matters if it implies a specific version, Azure SQL or a feature.
2. SQL Server version, edition, patch level and collation for Crownus (server owner). Express has a 10 GB database limit. Also, where Crownus's database will live.
3. Where is `dev-env`? `verify.ps1` is not on this machine, and `pwsh` is not installed.
4. Can the server owner enable trace flag 11042?
5. Resolved: the Crownus backend merge landed on `origin/development` on 2026-11-07, and the branch is cut from `893f105f`.
6. Resolved: approved.
7. Resolved: approved.

## 12. Left to the builder

Exact helper names, how `ISJSON` checks are generated, test-clone mechanics, the exact upsert per call site, and whether `rowversion` maps through a converter or a new property.

## Sources

- [SESSION_CONTEXT](https://learn.microsoft.com/en-us/sql/t-sql/functions/session-context-transact-sql)
- [SQL Server 2019 CU15 notes](https://learn.microsoft.com/en-us/troubleshoot/sql/releases/sqlserver-2019/cumulativeupdate15)
- [SQL Server 2025 known issues](https://learn.microsoft.com/en-us/sql/sql-server/sql-server-2025-known-issues)
- [Row-Level Security](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security)
- [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)
