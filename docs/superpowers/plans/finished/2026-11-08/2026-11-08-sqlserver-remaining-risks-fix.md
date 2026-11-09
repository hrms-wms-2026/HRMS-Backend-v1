# SQL Server migration: closing the remaining risks

Branch: `feature/sqlserver-migration` (clone `HRMS-Backend-sqlserver`, local only). Base: 58d3733c.
Follows `docs/reports/database/2026-11-07-sqlserver-migration-final-report.md`.

## Risks this plan covers

| # | Risk | Today |
|---|---|---|
| 1 | A query no test runs fails on SQL Server at runtime (LINQ it can't translate, or PostgreSQL-only raw SQL) | Not fixed |
| 2 | Integration test setup times out on a slow Docker host, so a test tenant is silently missing | Reduced (300 s timeouts) |
| 3 | Position list sorted by department or reports-to orders by GUID, which differs from PostgreSQL | Known, not fixed |

Scale: 158 repository classes, about 905 public async methods in `ONEVO.Infrastructure/Persistence`, plus 23 files with raw SQL (`FromSql`, `ExecuteSql`, `SqlQuery`).

## Risk 1: make every query run once on SQL Server

### Task 1: measure what the suite never runs

- Run the full integration suite with `coverlet.collector` (already referenced) limited to `ONEVO.Infrastructure`.
- Turn the report into a list of repository and query-service methods with zero hits. Save it to `docs/reports/database/2026-11-08-sqlserver-unexercised-queries.md`.
- Output: the baseline number of unexercised methods.

### Task 2: one test that calls every query method

New test `SqlServerMigration/QueryTranslationSweepTests` on the existing SQL Server fixture:

- Resolve every service from the real DI container whose implementation lives in `ONEVO.Infrastructure` and takes `ApplicationDbContext` (repositories, read services, query services). Using DI means real constructor dependencies.
- For each public method returning `Task`/`ValueTask`, generate arguments: the fixture's tenant id for tenant parameters, new GUIDs, `DateTime.UtcNow`/`DateOnly`, short strings, page 1 size 10, a one-item list for collections (an empty list often returns early and skips the query), `default` for enums and options records.
- Run each call inside a transaction that is rolled back, with the tenant session context set, so writes leave nothing behind and RLS stays on.
- **Fail only on database-translation errors**: `InvalidOperationException` saying "could not be translated", and `SqlException` syntax/type/object errors (102, 156, 207, 208, 245, 402, 4145, 8116, 8117 and similar). Domain errors (not found, argument checks, business rules) count as a pass, because the query part already ran or wasn't reached.
- Report each method as a separate theory case, so one failure names the method.
- Methods that can't be called generically (needing a real entity graph) go on an explicit skip list with a reason. The list is reviewed, not silently grown.

This does not weaken any RLS test; it only adds coverage.

### Task 3: review raw SQL by hand

- Read the 23 raw-SQL files for PostgreSQL syntax the earlier pattern scan could miss (`::` casts, `ILIKE`, `RETURNING`, `ON CONFLICT`, `LIMIT/OFFSET`, `now()`, double-quoted identifiers, `jsonb` operators, `= ANY(`).
- Each raw-SQL call site must be reached by Task 2 or by a focused test.

### Task 4: fix what fails

- Each failing sweep case is the failing test. Fix the query, watch the case pass, then run the related module's integration tests.
- Where the fix changes behaviour (ordering, NULL or case handling), add a focused test like `NullSortOrderTests` that pins the PostgreSQL behaviour.

### Task 5: measure again

- Rerun coverage. Every method still at zero hits is either on the skip list with a reason or gets a hand-written test.
- Done when: the sweep passes, the zero-hit list contains only skip-listed methods, and the full integration suite is green.

## Risk 2: test setup timeouts can't hide

- `DevSmokeTestTenantSeeder` logs and continues on failure, which is right for dev but hides problems in tests. In the test host, make a seeding failure fail the fixture with the tenant name and the exception, instead of letting a later test fail with a confusing "tenant not found".
- Retry a seeding step once on a SQL timeout before failing.
- Dev and production behaviour unchanged. Test: force a timeout in the seeder and assert the fixture reports it.

## Risk 3: position sorts by GUID (needs your decision)

On PostgreSQL these sorts also ordered by GUID, so the order was never meaningful, just different. Two options:

- **A. Sort by the department name and the reports-to position title** (recommended). Gives an order users understand, the same on both databases. Small behaviour change on the API.
- **B. Leave as is** and keep it in the report as a known difference.

## Order and checks

1. Task 1 (measure), 2. Tasks 2–3, 3. Task 4 fixes, 4. Risk 2, 5. Risk 3 if A, 6. Task 5 remeasure, 7. full gate: build, unit, architecture, integration (two clean runs), then update the final report.

Nothing is pushed or merged. No server is touched; everything runs on the local Testcontainers SQL Server.

## What it still won't prove

The sweep proves each query **translates and runs**, not that it returns the right rows for real data. Result correctness is still only covered where focused tests exist. Moving existing PostgreSQL data remains out of scope.
