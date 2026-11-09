# Part 5: Finish the migration

Replaces the unfinished Tasks 1, 2 and 4 of `part-4-tests-and-gate.md`. Read `part-0-index.md` (Global Constraints) first.

**Goal:** every test project compiles and passes on SQL Server, CI runs it, and the branch is ready to review.

## Where things stand

| Area | State |
|---|---|
| `ONEVO.Tests.SqlServer` | Passing, including RLS on all 170 covered tables |
| `ONEVO.Tests.Unit` | Passing |
| `ONEVO.Tests.Architecture` | 54 failing: they read deleted migrations and `ops/postgres` |
| `ONEVO.Tests.Integration` | Does not compile. 185 files: 83 use the Postgres template, 61 call `UseNpgsql` |
| CI, `.env.example`, README | Still describe PostgreSQL |
| `verify.ps1 backend -Full` | Cannot run: `dev-env` and `pwsh` are missing on this machine |

## Decisions needed before starting

1. **Architecture tests.** Approve the groups in `docs/reports/database/2026-11-07-architecture-test-changes.md`. Removing them was blocked as security test removal, so this needs the user's explicit approval. It may also need a one-off permission in the Claude Code settings.
2. **`dev-env`.** Say where it lives, or accept raw `dotnet` commands as the final gate.

## Order of work

Tasks 1 and 2 are independent. Task 3 needs Task 2. Tasks 4 and 5 need Tasks 1 to 3.

### Task 1: Architecture tests (after decision 1)

Make `ONEVO.Tests.Architecture` fully pass with no test weakened.

- [ ] Rewrite the 4 tenant isolation tests per spec section 9: forbid `STATE = OFF` and `DROP`/`ALTER SECURITY POLICY`, forbid logins and grants in migrations, allow `EXECUTE AS` only for the login lookup, and read coverage from `RlsTableRegistry`.
- [ ] Add `MigrationsCreatingATenantTable_CreateItsPolicy`.
- [ ] Update the 2 payment gateway tests.
- [ ] Remove the approved tests one group per commit, each commit naming what now covers them.
- [ ] Remove the `LEGACY_MIGRATION_TESTS` blocks. `git grep LEGACY_MIGRATION_TESTS` must find nothing.
- [ ] **Prove the guards work:** drop a table from the registry, add `STATE = OFF` to a migration, add `CREATE LOGIN` to a migration. Each must fail the right test. Revert each.

Done when `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release` passes.

### Task 2: Integration harness

Build the shared base the 185 files depend on.

- [ ] Create `Support/SharedSqlServerTemplate.cs`: one container, migrate and run `ops/sqlserver` once, `BACKUP`, then `RESTORE ... WITH MOVE` per test class. Keep `CreateDatabaseAsync`'s name and return type so call sites barely change.
- [ ] After each restore, remap users to logins and re-check `MAXDOP` and snapshot settings. Limit concurrent restores (start at 4) and record the time per restore.
- [ ] Port `IntegrationDatabaseBootstrap`, `PrivilegedRoleTestBootstrap` and `IntegrationTestEnvironmentScope`, adding `TrayApp__Jwt__Secret`.
- [ ] Add `TestDb.Options(connectionString)` to replace the 67 `UseNpgsql` calls.
- [ ] Swap the csproj to `Testcontainers.MsSql`.

Done when `ApiBootTests` passes on SQL Server. If each restore takes over about 10 seconds, stop and rethink before porting everything.

### Task 3: Port the integration tests

Scripted changes first, then folder by folder, highest risk first. The raw SQL scan shows 410 hits in 103 files, many of them ordinary C# `= true`, so each batch needs reading, not just replacing.

- [ ] Script: `SharedPostgresTemplate` to `SharedSqlServerTemplate`, `UseNpgsql` to `TestDb.Options`, `NpgsqlConnection`/`NpgsqlCommand` to `SqlConnection`/`SqlCommand`, `set_config` to `sp_set_session_context`.
- [ ] Retire the 5 approved classes: `AttendanceCorrectionsMigrationUpgradeTests`, `AddTaskCategoryKindMigrationTests`, `TaskStatusGroupsMigrationTests`, `WorkModeDataMigrationTests`, `PositionMigrationSafetyIntegrationTests`.
- [ ] Port in this order, one commit per batch:
  1. `Security` and every `*Rls*` test
  2. `SharedPlatform` (outbox: exercises the new `outbox_messages` RLS)
  3. `Auth`
  4. `CoreHr` and `OrgStructure` (exercises the new `position_access_templates` and `global_email_directory` RLS)
  5. `WorkManagement` and `Calendar`
  6. `Monitoring`, `Features/TimeAttendance`, `Storage` and the rest
- [ ] Log every failure that shows changed behaviour, with its fix. An assertion changes only with a recorded reason.
- [ ] Move `ONEVO.Tests.SqlServer` into `tests/ONEVO.Tests.Integration/SqlServerMigration/` and delete that project.

**Watch for:** the new RLS on those four tables is proven by the SQL Server tests, but only this task runs the full application flows through it. A background job that reads `position_access_templates` in system mode would now see nothing. Fix such a job by giving it a tenant context, never by loosening the policy.

Done when `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -c Release` passes.

### Task 4: CI, config and docs

- [ ] `.github/workflows/ci.yml`: confirm the integration job runs on SQL Server, and restore steps cover the changed projects. Pin the image once the VM's version is known.
- [ ] `.github/scripts/select-integration-filter.ps1`: replace the Postgres references.
- [ ] `.env.example`: `ONEVO_DB_ADMIN_USER=sa`, port 1433, and a SQL Server admin password placeholder.
- [ ] `README.md`, `CLAUDE.md`, `schema-snapshots/README.md`: describe SQL Server. Mark `schema-snapshots/` as PostgreSQL-era.
- [ ] Add `ops/sqlserver/regenerate-baseline.ps1`, so merge-time regeneration keeps the hand-written migrations in order.
- [ ] Delete `spikes/sqlserver-rls/`, and `ops/meetings/phase2-preflight.sql` if it is PostgreSQL-only.
- [ ] Fix the `System.Security.Cryptography.Pkcs` 9.0.4 vulnerability warnings by pinning a patched version.
- [ ] Confirm the web app, admin console and tray only echo the version token, never parse it.

### Task 5: Final gate and review

- [ ] Fresh container, run `setup-local-db.ps1`, migrate as `onevo_migrator`, boot in Production, and check `/health`.
- [ ] Run the full gate: `pwsh ../dev-env/scripts/verify.ps1 backend -Full`, or all four raw `dotnet test` runs, saying which ran.
- [ ] Run `find-unauthorized-endpoints.ps1` and `security-scan.ps1` if available.
- [ ] Whole-branch review by a fresh reviewer. Fix Critical and Important findings, each with a test that failed first.
- [ ] Write `docs/reports/database/2026-11-07-sqlserver-migration-final-report.md`: results, behaviour differences, retired tests, what the server owner must do, and the merge-time steps.

Do not push or merge unless the user asks.

## Size

Task 3 is most of the remaining work and will take several sessions. Tasks 1, 2 and 4 are each about one session or less.
