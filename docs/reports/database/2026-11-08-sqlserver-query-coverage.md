# SQL Server migration: which queries have run on SQL Server

Dates: 2026-11-08, completed 2026-11-09. Branch `feature/sqlserver-migration`. Follows the plan
`docs/superpowers/plans/finished/2026-11-08/2026-11-08-sqlserver-remaining-risks-fix.md`.

## Why this matters

SQL Server reports LINQ it can't translate, and raw SQL it can't parse, only when the query runs.
A query that no test runs could fail for the first time in production.

## How it was measured

- **Query call site:** a line in `src/ONEVO.Infrastructure` (excluding migrations and seeders)
  that runs a query: `ToListAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`,
  `ToDictionaryAsync`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync`, `FindAsync`, raw SQL
  (`ExecuteSql*`, `SqlQuery*`, `FromSql*`) and similar.
- **Run:** coverlet line coverage of the integration suite on SQL Server shows the statement holding
  that call executed. This is line level, not method level, because many methods return before
  their query.
- `ONEVO.Application` and `ONEVO.Api` run no queries themselves (no `ApplicationDbContext` use or
  async query calls), so measuring Infrastructure covers every query.

## Results

| | Query call sites | Never run |
|---|---|---|
| Integration suite before this work | 1155 | 553 (48%) |
| + translation sweep, passes 1 and 2 | 1155 | 41 |
| + first focused tests | 1155 | 37 |
| + sweep pass 3 and scenarios | 1157 | 13 |
| + final focused tests | 1157 | **0** |

The final figure comes from one full integration run on SQL Server at 13bddf94. The total grew to
1157 because the outbox claim became two statements.

### The sweep (`QueryTranslationSweepTests`)

It calls every public async method of every Infrastructure class that takes `ApplicationDbContext`
(1164 methods), with generated arguments and RLS on:

1. on an empty tenant;
2. on a small connected org, with real ids passed by parameter name;
3. on a database with one row in every table, all ids equal and constraints off, with a service
   provider that wires database-backed services for jobs and fakes everything else. A list of
   scenarios sets the few values the generic passes can't guess: a scope type, a flag, or a row
   state.

The result is 0 translation or SQL failures. The sweep can catch a real failure: with the old
PostgreSQL-era `GetCompletionsByDayAsync` line put back, it fails and names that method. In pass 3,
only SQL syntax and schema errors and untranslatable LINQ count as failures. Placeholder data can
break conversions, which isn't a query defect, and none occurred.

### Focused tests

`DataDependentQueryTests` covers unread counts, the employee list with attendance, pending
positions with their manager, and reporting line moves. `MonitoringJobQueryTests` covers the
location and wellness jobs. `OfficeProjectProvisionerRlsIntegrationTests` gained target date
extension and concurrent first creation.

### Raw SQL

All 23 files were read by hand. Every statement uses SQL Server syntax. Two comments still
described PostgreSQL and were corrected.

## A real defect this found: outbox claim locking

`OutboxProcessorConcurrencyIntegrationTests` once failed with one processor claiming all 60
messages. The cause was the claim query. One `UPDATE ... TOP (20)` over both pending and
expired-lease rows, ordered by `next_attempt_at, created_at`, made SQL Server read and sort every
claimable row, holding update locks on all of them. With 310 claimable rows, claiming 20 locked
310, so other instances saw an empty outbox and backed off.

Delivery was always exactly once with no overlap. The effect was that scaled-out API instances
stopped sharing outbox work. Expired leases and pending rows are now claimed by two statements that
read their index in order, so only the claimed rows are locked. `OutboxClaimLockingTests` measures
the locks held on a realistic mix (300 pending, 3000 published, 10 abandoned): exactly 20, and a
second claimer gets its own 20. PostgreSQL's `SKIP LOCKED` never had this problem.

## A test isolation defect this found

`ReferenceDataParityTests`, added on this branch, and the office project tests set process-wide
environment variables (connection strings, environment name) outside
`WebApplicationFactoryCollection`. A host booting in parallel could then read another test's
database. A forgot-password test once found its reset tokens missing this way: 200 OK with no
rows, and it passed 8 of 8 times on its own. Both now join the collection.

## What this does not prove

- The sweep proves a query translates and runs, not that it returns the right rows. Result
  correctness is covered only where focused tests exist.
- Some queries build different SQL depending on their arguments, for example a filter added only
  when an option is set. A query that ran in one shape may not have run in all of them.
