# Part 2: Provider swap, baseline, RLS

Read first: `part-0-index.md` (Global Constraints), spec sections 3 to 7, and the spike report `docs/reports/database/2026-11-07-sqlserver-rls-spike.md`. Use the T-SQL the spike confirmed.

### Task 1: Swap the provider and make `src` compile

**Files:**
- Modify: `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`, plus any other csproj with `Npgsql`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:164`, `Configuration/DatabaseMigrationRunner.cs:37`, `Persistence/ApplicationDbContextFactory.cs:34`, `Configuration/DatabaseConnectionStartupValidator.cs`, `src/ONEVO.Api/Configuration/DotEnvLoader.cs`, `src/ONEVO.Api/Program.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/SqlServer/SqlServerErrors.cs`
- Test: `tests/ONEVO.Tests.Unit/Persistence/SqlServerErrorsTests.cs`
- Delete: `tests/ONEVO.Tests.Integration/SqlServerMigration/PostgresTruthCaptureTests.cs`

**Interfaces:**
- Produces, in `ONEVO.Infrastructure.Persistence.SqlServer`:
  - `static class SqlServerErrors`
  - `bool IsUniqueViolation(Exception ex)`: numbers 2601, 2627, including inner `SqlException`
  - `bool IsForeignKeyViolation(Exception ex)`: 547
  - `bool IsDeadlock(Exception ex)`: 1205
  - `bool IsRlsBlock(Exception ex)`: 33504
  - `string? ConstraintName(Exception ex)`: parses the index or constraint name from the message
- `UniqueConstraintConflictException` in Application must not reference SqlClient types if it does not already reference Npgsql types. Check and keep the layering as it is.

- [ ] **Step 1: Write `SqlServerErrorsTests`**

Message parsing for `ConstraintName`, with real SQL Server message text for 2601 and 2627, and a `DbUpdateException` wrapping. `SqlException` has no public constructor, so have the helpers take the number and message through an internal overload that the tests use.

- [ ] **Step 2: Run to confirm failure, implement, run to confirm pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~SqlServerErrorsTests"`

- [ ] **Step 3: Swap packages and `UseNpgsql` calls**

`UseSqlServer(connectionString, o => { o.EnableRetryOnFailure(3); o.CommandTimeout(30); })`. `ONEVO_DB_*` settings stay, and `DotEnvLoader` builds with `SqlConnectionStringBuilder`. Replace the error message that points to `ops/postgres/setup-local-db.ps1` with `ops/sqlserver/setup-local-db.ps1`.

- [ ] **Step 4: Replace exception handling in the 20 repositories** that catch `PostgresException` or check SqlState, using `SqlServerErrors`. Compare by the same constraint names; EF keeps those names.

- [ ] **Step 5: Keep the other test projects building.** Raw Postgres SQL strings in `src` still compile, and Part 3 replaces them. No `using Npgsql` may remain in `src`. In `ONEVO.Tests.Architecture` and `ONEVO.Tests.Unit`, change model-only `UseNpgsql` calls to `UseSqlServer`. `ONEVO.Tests.Integration` will not compile until Part 4 Task 1; that is expected, so create `tests/ONEVO.Tests.SqlServer/ONEVO.Tests.SqlServer.csproj` (xUnit, `Testcontainers.MsSql`, references Infrastructure and Api) with `SqlServerFixture.cs` for all Part 2 and Part 3 tests.

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release`
Expected: 0 errors.

- [ ] **Step 6: Commit**

### Task 2: Make the model SQL Server valid

**Files:**
- Modify: entity configurations under `src/ONEVO.Infrastructure/Persistence/Configurations/` (72 files with `HasColumnType`, 47 with `HasFilter` or index methods)
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/SqlServer/JsonColumnConvention.cs`
- Test: `tests/ONEVO.Tests.Architecture/SqlServerModelArchitectureTests.cs`

**Interfaces:**
- Produces: `JsonColumnConvention.Apply(ModelBuilder builder)`: every property previously typed `jsonb` is `nvarchar(max)` with check constraint `ck_<table>_<column>_json`, `ISJSON(col) = 1` (null allowed where the column is nullable).

- [ ] **Step 1: Write the architecture tests** (`SqlServerModelArchitectureTests`, model built with `UseSqlServer` and no live server)

- `NoColumnUsesAPostgresType`: no `jsonb`, `uuid`, `xid`, `bytea`, `text`, `boolean`, `timestamp`, `character varying`, `numeric` or `[]` type name.
- `NoFilterUsesPostgresSyntax`: no `= false`, `= true`, `"` quotes.
- `IndexKeysFitTheLimit`: each index's combined key is at most 1700 bytes.
- `DatabaseCollationIsCaseSensitive`: `model.GetCollation()` equals `Latin1_General_100_CS_AS_SC`.

- [ ] **Step 2: Confirm they fail, then change the model**

- `modelBuilder.UseCollation("Latin1_General_100_CS_AS_SC")`.
- Type strings to their spec section 3 equivalents; remove `HasColumnType` where the default is right.
- Rewrite the 53 `HasFilter` strings and review the 27 check constraints.
- `xmin`/`xid` (8 sites) become `IsRowVersion()` shadow properties of type `byte[]`.
- Array columns (6) become primitive collections.
- `normalized_email` computed column: `LOWER(LTRIM(RTRIM(email)))` with `stored: true`.
- `AreNullsDistinct(false)` (2 sites): replace with a filtered unique index and a comment on the changed null handling; a test in Part 4 covers it.
- `monitoring_face_scans.storage_key`: drop the index key on it and add `storage_key_hash` (`binary(32)`, computed `HASHBYTES('SHA2_256', storage_key)`, stored), indexed in its place. Check the queries that use it.

- [ ] **Step 3: Run the tests to confirm pass**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release --filter "FullyQualifiedName~SqlServerModelArchitectureTests"`

- [ ] **Step 4: Commit**

### Task 3: Baseline migration and RLS migration

**Files:**
- Delete: all of `src/ONEVO.Infrastructure/Migrations/`
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_InitialSqlServer.cs`, `<ts>_AddRowLevelSecurity.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Rls/RlsSql.cs`, `RlsTableRegistry.cs`
- Create: `ops/sqlserver/bootstrap-logins.sql`, `ops/sqlserver/post-migration-grants.sql`, `ops/sqlserver/setup-local-db.ps1`, `ops/sqlserver/README.md`
- Delete: `ops/postgres/`
- Test: `tests/ONEVO.Tests.SqlServer/CleanDatabaseTests.cs`

**Interfaces:**
- Produces:
  - `enum RlsPredicate { Tenant, TenantOrSystem, Sessions }`
  - `static class RlsTableRegistry { IReadOnlyDictionary<string, RlsPredicate> Tables }`: built from `postgres-policy-inventory.json`
  - `static class RlsSql { string CreateFunctions(); string CreatePolicy(string table, RlsPredicate predicate); string DropPolicy(string table); }`
  - Policy name: `rls.policy_<table>`

- [ ] **Step 1: Write `CleanDatabaseTests`**

`Migrate_OnEmptyDatabase_Succeeds` as `onevo_migrator`; `HasPendingModelChanges_IsFalse`; `EveryRegistryTable_HasAnEnabledPolicy`.

- [ ] **Step 2: Remove migrations, generate the baseline**

Run: `dotnet ef migrations add InitialSqlServer -p src/ONEVO.Infrastructure -s src/ONEVO.Api`
Expected: created. CLAUDE.md warns a stale snapshot corrupts migrations, so the snapshot is regenerated from nothing here.

- [ ] **Step 3: Fix cascade paths**

Run the migration against a container. For each "may cause cycles or multiple cascade paths" error, set that FK to `NoAction`. Record each in `docs/reports/database/2026-11-07-cascade-changes.md`, with the code that deleted through it. Where code relied on the cascade, delete dependents explicitly in that code path. Regenerate the baseline after the last change.

- [ ] **Step 4: Write `RlsSql`, `RlsTableRegistry` and `AddRowLevelSecurity`**

Use the spike's T-SQL. The migration loops the registry. The `users` and `tenants` predicates also allow the lookup user (spec 4.4).

- [ ] **Step 5: Write the ops scripts**

Spec 4.5 rights. `setup-local-db.ps1` creates the database with collation and settings, then logins. Passwords come from `ONEVO_DB_*`, never in the scripts. Migrations must not create logins.

- [ ] **Step 6: Run `CleanDatabaseTests` to confirm pass, commit**

### Task 4: Tenant context on connections

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Interceptors/TenantRlsInterceptor.cs`, `Identity/Tenancy/TenantContextSwitcher.cs`, `Persistence/Repositories/Auth/Login/EfSessionRepository.cs`
- Create: `src/ONEVO.Infrastructure/Configuration/DatabaseSettingsStartupValidator.cs`
- Test: `tests/ONEVO.Tests.SqlServer/TenantContextConnectionTests.cs`

**Interfaces:**
- Produces: `DatabaseSettingsStartupValidator.ValidateAsync(string connectionString, CancellationToken ct)`: throws `InvalidOperationException` naming the wrong setting if MAXDOP is not 1, RCSI is off, the collation differs, or the app login is `db_owner` or holds `ALTER ANY SECURITY POLICY`.

- [ ] **Step 1: Write the tests**

- `Open_SetsContext`; `Close_ClearsContext`.
- `RollbackAfterSwitch_DoesNotKeepSwitchedTenant`: start in tenant A, switch to B in a transaction, roll back; the next query sees A's rows only, or none. Never B's.
- `RetryAfterTransientFailure_ReappliesContext`: kill the session with `KILL` mid-work; the retried command sees the same tenant.
- `FailedSessionLookup_LeavesNoKey`.
- `Validator_RefusesWrongMaxdop`, `Validator_RefusesDbOwnerAppLogin`.

- [ ] **Step 2: Confirm they fail, then implement**

`sp_set_session_context` twice in one command batch. `TenantContextSwitcher` must restore on rollback: since `SESSION_CONTEXT` is not transactional, wrap with a `DbTransactionInterceptor` override of `TransactionRolledBack` in `TenantRlsInterceptor` (add it to `AddInterceptors`) that re-applies the current `ITenantContext` state. Decide the state to re-apply from the spike's rollback result. `EfSessionRepository` sets and clears the key in `finally`.

- [ ] **Step 3: Run tests, confirm pass, commit**

### Task 5: Login lookup, triggers

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_AddAuthLookupAndTriggers.cs`
- Modify: `Persistence/Repositories/Auth/Login/EfBaseLoginCandidateRepository.cs`; configurations for tables with triggers (`HasTrigger`)
- Test: `tests/ONEVO.Tests.SqlServer/AuthLookupAndTriggerTests.cs`

- [ ] **Step 1: Write the tests.** Port `Security/AuthLookupBaseLoginCandidatesFunctionTests.cs` cases, plus `ModuleHourSummary_UpdatesOnTaskChange`, `ObjectivesTrigger_Updates`, `MeetingDetailsKey_CannotChange`.

- [ ] **Step 2: Implement.** Port from the latest definitions in the deleted migrations (git history): `wm_refresh_module_hour_summary` as of `20261025220540_AddMeetingHoursStamping`, the task, status and objective triggers, `meeting_details_key_immutable`. Deferred meeting triggers do not come here; see Part 3 Task 3.

- [ ] **Step 3: Run tests, confirm pass, commit**

### Task 6: Reference data and parity report

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_SeedReferenceData.cs` if needed
- Test: `tests/ONEVO.Tests.SqlServer/ReferenceDataParityTests.cs`
- Create: `docs/reports/database/2026-11-07-sqlserver-parity-report.md`

- [ ] **Step 1: Write `ProductionBoot_MatchesPostgresReferenceData`**

Boot in `Production` on an empty database. Compare to `postgres-reference-data.json`: same tables, same row counts, same non-generated values. Each explained difference goes in an allowlist in the test with a reason.

- [ ] **Step 2: Sort migration inserts.** For the 16 `INSERT INTO` and 6 `InsertData` files, mark each reference data or backfill. Backfills on an empty database do nothing. Port the reference data until the test passes.

- [ ] **Step 3: Write the parity report**, including `sys.security_predicates` against the policy inventory. Commit.
