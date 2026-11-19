# Part 4: Tests, live RLS proof, full gate

Read first: `part-0-index.md` (Global Constraints) and spec section 9. Parts 2 and 3 are complete: `src` has no Postgres code.

### Task 1: Move the integration harness

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Support/SharedSqlServerTemplate.cs`
- Delete: `tests/ONEVO.Tests.Integration/Support/SharedPostgresTemplate.cs`
- Move: all tests from `tests/ONEVO.Tests.SqlServer/` into `tests/ONEVO.Tests.Integration/SqlServerMigration/` on the shared template, then delete that project and its `SqlServerFixture`
- Modify: `Support/IntegrationDatabaseBootstrap.cs`, `Support/PrivilegedRoleTestBootstrap.cs`; `tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj`; the 83 classes using the template; the factories (`ApiBootTestFactory`, `AuthTestFactory`, `E2ETestFactory` and the rest)

**Interfaces:**
- Produces: `SharedSqlServerTemplate.CreateDatabaseAsync(string name) -> Task<ClonedDatabase>`, with `ClonedDatabase.AdminConnectionString`, `MigratorConnectionString` and `AppConnectionString`. Keep the existing method and property names from `SharedPostgresTemplate` wherever they exist, so call sites change as little as possible.

- [ ] **Step 1: Write `SharedSqlServerTemplateTests`:** two clones are independent; a clone has policies, logins mapped and settings on (`MAXDOP`, RCSI do not carry through restore in every case, so assert them); `onevo_app` in a clone is restricted.

- [ ] **Step 2: Implement.** One container, migrate a template once, `BACKUP DATABASE` once, `RESTORE ... WITH MOVE` per class. Users restored from backup need remapping to logins (`ALTER USER ... WITH LOGIN`). Clone concurrency limited by a semaphore (start at 4). Measure clone time and record it.

- [ ] **Step 3: Replace `UseNpgsql` in tests with a shared helper** `TestDb.Options(string connectionString)`, and port raw SQL in tests (`set_config` to `sp_set_session_context`, and so on). `NpgsqlConnection`/`NpgsqlCommand` become `SqlConnection`/`SqlCommand`.

- [ ] **Step 4: Remove the 5 retired test classes** named in spec section 9: `AttendanceCorrectionsMigrationUpgradeTests`, `AddTaskCategoryKindMigrationTests`, `TaskStatusGroupsMigrationTests`, `WorkModeDataMigrationTests`, `PositionMigrationSafetyIntegrationTests`. No others.

- [ ] **Step 5: Run in batches by folder, fix, commit.** Failures that show a behaviour difference go in a list for the final report, with the fix. Do not change an assertion to match new behaviour without recording why.

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -c Release --filter "FullyQualifiedName~<Folder>"`

### Task 2: Architecture tests

**Files:**
- Modify: `tests/ONEVO.Tests.Architecture/TenantIsolationArchitectureTests.cs` and the 28 files reading migrations
- Create: `docs/reports/database/2026-11-07-architecture-test-changes.md`

- [ ] **Step 1: Rewrite `TenantIsolationArchitectureTests`** exactly per the table in spec section 9. `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` compares the EF model's tenant-owned tables to `RlsTableRegistry.Tables`, with the same exception list as today and no additions. A new test `MigrationsCreatingATable_WithTenantId_CallCreatePolicy` protects future tables.

- [ ] **Step 2: Prove the tests still catch things.** Temporarily remove one table from `RlsTableRegistry`, add `ALTER SECURITY POLICY ... STATE = OFF` to a migration, and add `CREATE LOGIN` to a migration; each must fail the right test. Revert each.

- [ ] **Step 3: Re-point the other 27 files.** For each, write in the report what it asserted, what it asserts now, and where. Tests whose only purpose was a historic migration are listed with reasons for the user to see. Do not remove any other assertion.

- [ ] **Step 4: Run and commit**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release`
Expected: PASS.

### Task 3: Live RLS proof

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Security/SqlServerRlsCoverageTests.cs`, `SqlServerRlsCrossTenantTests.cs`, `SqlServerAppLoginRightsTests.cs`, `SqlServerConnectionIsolationTests.cs`

**Interfaces:**
- Consumes: `RlsTableRegistry.Tables`, `SharedSqlServerTemplate`.

- [ ] **Step 1: Write `SqlServerRlsCoverageTests`**

`EveryModelTenantTable_HasFilterAndFourBlockPredicates`: from `sys.security_policies` and `sys.security_predicates`, every table enabled, with one FILTER and BLOCK for AFTER INSERT, AFTER UPDATE, BEFORE UPDATE, BEFORE DELETE. `PolicyIsSchemaBound`. `MatchesPostgresInventory`, against `postgres-policy-inventory.json`.

- [ ] **Step 2: Write `SqlServerRlsCrossTenantTests`, a `[Theory]` over every registry table**

Generate one row per tenant by inserting as the migrator in admin mode (use model metadata to fill required columns and FKs; on tables where that is not possible, list them in the test with a hand-written seed. The list may not silently skip a table: the theory fails if a registry table has no seed). As `onevo_app` in tenant A:
- `Select_DoesNotReturnTenantB`
- `Update_TenantBRow_AffectsZero`
- `Delete_TenantBRow_AffectsZero`
- `Insert_ForTenantB_IsBlocked`: error 33504
- `Update_MoveRowToTenantB_IsBlocked`
- `NoContext_ReturnsNothing`

- [ ] **Step 3: Write `SqlServerAppLoginRightsTests`:** `AppLogin_IsNotDbOwner`, `AppLogin_CannotAlterSecurityPolicy`, `AppLogin_CannotCreateOrAlterObjects`, `AppLogin_CanExecuteOnlyTheLookupProcedure`, `Settings_Maxdop1_Rcsi_CollationOn`.

- [ ] **Step 4: Write `SqlServerConnectionIsolationTests`:** `PooledRequestThenBackgroundJob_DoesNotInheritTenant`, `ConcurrentTenantsThroughApi_NeverCross` (HTTP level, 8 tenants, 16 clients), `MixedCaseEmailLogin_Works`, `KeyHashDifferingByCase_DoesNotAuthenticate`.

- [ ] **Step 5: Run, confirm pass, commit**

### Task 4: CI, docs, cleanup, full gate

**Files:**
- Modify: `.github/workflows/ci.yml`, `.github/scripts/select-integration-filter.ps1`, `.env.example`, `README.md`, `CLAUDE.md`, `schema-snapshots/README.md`
- Delete: `spikes/sqlserver-rls/`, `ops/meetings/phase2-preflight.sql` if it is Postgres only
- Create: `docs/reports/database/2026-11-07-sqlserver-migration-final-report.md`

- [ ] **Step 1: Update CI and docs.** Mark `schema-snapshots/` as Postgres-era in its README rather than rewriting it. Do not edit `CLAUDE.md` rules on tenant isolation beyond the SQL Server wording.

- [ ] **Step 2: Confirm no leftovers**

Run: `git grep -il "npgsql\|testcontainers.postgresql" -- src tests '*.csproj'`
Expected: no output.

- [ ] **Step 3: Run the Done checks, in order**

1. `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release`: 0 errors.
2. Fresh container, `ops/sqlserver/setup-local-db.ps1`, migrations as `onevo_migrator`: clean.
3. Start the API in `Production` on it: boots, validator passes, seeders run, `/health` is OK. Sign-in needs the first-admin step, which is deployment work: note it, don't solve it here.
4. `pwsh ../dev-env/scripts/verify.ps1 backend -Full`, or the raw unit, architecture and integration runs if that script is still missing. Say which one ran.
5. `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` and `security-scan.ps1` if available.

- [ ] **Step 4: Write the final report:** what passed, what failed with output, behaviour differences, retired and re-pointed tests, what the server owner must do (patch level, trace flag 11042, version pin), and the merge-time steps (freeze, regenerate baseline, port new raw SQL).

- [ ] **Step 5: Ask for a whole-branch review, then commit.** Move nothing to `finished/` until the user accepts. Do not push.
