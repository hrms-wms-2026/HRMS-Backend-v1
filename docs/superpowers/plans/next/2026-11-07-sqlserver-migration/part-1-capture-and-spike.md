# Part 1: Capture Postgres truth, prove the RLS approach

Read first: `part-0-index.md` (Global Constraints) and spec sections 4 and 5.
Needs Docker Desktop running. Nothing in this part changes production code.

### Task 1: Capture policies and reference data from Postgres

The list of policies and the reference data must come from a real Postgres run, not from counting statements. This must run before the provider swap, while the Postgres harness still works.

**Files:**
- Create: `tests/ONEVO.Tests.Integration/SqlServerMigration/PostgresTruthCaptureTests.cs` (deleted in Part 2 Task 1)
- Create: `docs/reports/database/2026-11-07-postgres-policy-inventory.json`
- Create: `docs/reports/database/2026-11-07-postgres-reference-data.json`

**Interfaces:**
- Produces: `postgres-policy-inventory.json`: `[{ "table", "policy", "cmd", "using", "withCheck", "forceRls" }]`
- Produces: `postgres-reference-data.json`: `{ "<table>": { "rowCount": n, "rows": [ { column: value } ] } }`, for every table with rows, rows ordered by primary key

- [ ] **Step 1: Write `CapturePolicies` using `SharedPostgresTemplate`**

Read `pg_policies` joined to `pg_class.relforcerowsecurity` for all tables. Assert the result is non-empty, then write the JSON.

- [ ] **Step 2: Write `CaptureReferenceData`**

Boot the API with `ApiBootTestFactory` in the `Production` environment on a fresh clone, so the hosted seeders run exactly as they will on the VM. Wait for them to finish, then dump every non-empty table as the migrator role. Generated IDs and timestamps stay in the file; Part 2 Task 6 ignores them when comparing.

- [ ] **Step 3: Run and check**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -c Release --filter "FullyQualifiedName~PostgresTruthCaptureTests"`
Expected: PASS. Policy file has rows for the three tray activation tables with `'system'` in their `using` text, and for `sessions` with `session_key_lookup`. Reference file includes `permissions`.

- [ ] **Step 4: Note findings**

List every distinct `using`/`withCheck` shape in the report's header. If a shape is not one of the three functions in spec 4.1, stop and tell the user.

- [ ] **Step 5: Commit**

### Task 2: Spike the RLS approach

Throwaway project, not referenced by the solution. Its purpose is to prove spec section 4 on a small schema before the bulk of the work starts.

**Files:**
- Create: `spikes/sqlserver-rls/SqlServerRlsSpike.csproj` (xUnit, `Testcontainers.MsSql`, `Microsoft.Data.SqlClient`, `Microsoft.EntityFrameworkCore.SqlServer`)
- Create: `spikes/sqlserver-rls/RlsSpikeTests.cs`, `spikes/sqlserver-rls/schema.sql`
- Create: `docs/reports/database/2026-11-07-sqlserver-rls-spike.md`

**Interfaces:**
- Produces: confirmed T-SQL for predicates, policies and the login lookup procedure, which Part 2 Task 3 copies. The report records the exact statements that worked.

`schema.sql`: database with the required collation, `READ_COMMITTED_SNAPSHOT ON` and `MAXDOP = 1`; tables `tenants`, `users`, `sessions`, `items`; `rls.fn_tenant`, `rls.fn_tenant_or_system`, `rls.fn_sessions`; one policy per table with a filter and all four block predicates; logins `onevo_app` and `onevo_migrator`; the lookup user and procedure.

- [ ] **Step 1: Isolation tests (as `onevo_app`)**

- `NoContext_SeesNoRows`: count on `items` is 0.
- `UnknownMode_SeesNoRows`: mode `bogus`, count is 0.
- `SystemMode_SeesNoRows_OnStandardTable`.
- `TenantA_SeesOnlyTenantA`.
- `CrossTenantInsert_IsBlocked`, `CrossTenantUpdate_IsBlocked`, `CrossTenantDelete_AffectsZero`, `MoveRowToOtherTenant_IsBlocked`: expect error 33504 for blocked writes, or 0 rows affected.
- `AppLogin_CannotAlterPolicy_OrSchema`: `ALTER SECURITY POLICY`, `DROP SECURITY POLICY`, `ALTER TABLE` all fail.

- [ ] **Step 2: Parallel-plan tests**

- `Maxdop1_ProducesSerialPlan`: load `items` with 500,000 rows over 8 tenants, capture the actual plan with `SET STATISTICS XML ON` for a query that goes parallel when `MAXDOP` is 0 (prove that by running it first with the setting at 0), and assert `DegreeOfParallelism` is absent or 0/1 once the setting is 1.
- `ConcurrentPooledTenants_NeverSeeEachOther`: 32 concurrent workers, 8 tenants, 20,000 iterations total, pooled connections opening and closing each time; every row returned must match the worker's tenant. Zero mismatches allowed. Run with `MAXDOP = 1`.
- Record a run with `MAXDOP = 0` too. This one is for information: any mismatch there goes in the report as evidence for the setting.

- [ ] **Step 3: Connection state tests**

- `PoolReuse_ClearsContext`: open as tenant A, close, reopen the same pooled connection without setting context, count is 0.
- `ContextSurvivesRollback_Documented`: set tenant B inside a transaction, roll back, read `SESSION_CONTEXT`. Record the result. Expected: still B, which confirms Review Focus 1, and Part 2 Task 4 must reset it.
- `SessionLookup_ClearedAfterFailure`: set `app.session_lookup_key_hash`, throw inside the lookup, confirm it is cleared in `finally` on the same connection.
- `KeyHash_IsCaseSensitive`: a hash differing only by case returns no row.

- [ ] **Step 4: MERGE and EF tests**

- `Merge_CrossTenantUpdate_IsBlocked` and `Merge_CrossTenantInsert_IsBlocked`.
- `EfBatchInsert_RespectsBlockPredicate`: `SaveChanges` with 50 rows for another tenant fails; 50 for the current tenant succeeds. EF batches through MERGE, so this must pass.
- `EfSave_OnTableWithTrigger_Works` with `HasTrigger`.

If a MERGE test shows a block predicate is skipped, record it, and Part 3 must avoid MERGE, including EF's (`UseSqlOutputClause(false)` or batch size 1). Tell the user.

- [ ] **Step 5: Login lookup tests**

- `Lookup_ReturnsCandidates_WithNoContext`.
- `Lookup_LimitsTo9`.
- `AppLogin_CannotSelectUsers_WithoutContext`.
- `LookupUser_CannotReadOtherColumns`.

- [ ] **Step 6: Run everything, write the report, and stop**

Run: `dotnet test spikes/sqlserver-rls/SqlServerRlsSpike.csproj -c Release`
Write what passed and failed, the confirmed T-SQL, and the throughput with `MAXDOP = 1`.

Report to the user. **Do not start Part 2 until the user has seen this.** If `ConcurrentPooledTenants_NeverSeeEachOther` shows any mismatch at `MAXDOP = 1`, stop: the design needs changing.

- [ ] **Step 7: Commit**
