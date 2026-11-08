# SQL Server migration: which queries have run on SQL Server

Date: 2026-11-08. Branch `feature/sqlserver-migration`. Follows the plan
`docs/superpowers/plans/next/2026-11-08-sqlserver-remaining-risks-fix.md`.

## Why this matters

SQL Server reports LINQ it can't translate, and raw SQL it can't parse, only when the query runs.
A query that no test runs could fail for the first time in production.

## How it was measured

- **Query call site:** a line in `src/ONEVO.Infrastructure` (excluding migrations and seeders)
  that runs a query: `ToListAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`,
  `ToDictionaryAsync`, `ExecuteUpdateAsync`, `ExecuteDeleteAsync`, `FindAsync`, raw SQL
  (`ExecuteSql*`, `SqlQuery*`, `FromSql*`) and similar.
- **Run:** coverlet line coverage of the integration suite on SQL Server shows the statement holding
  that call executed. Line level, not method level, because many methods return before their query.
- Script: `querylines.py` (scratch, not committed); it reads Cobertura reports and lists call
  sites with zero hits.

## Results

| | Query call sites | Never run |
|---|---|---|
| Existing integration suite | 1155 | 553 (48%) |
| + translation sweep (`QueryTranslationSweepTests`) | 1155 | 54 |
| + seeded second sweep pass | 1155 | 41 |
| + `DataDependentQueryTests` | 1155 | **37** |

The sweep calls every public async method of every Infrastructure class that takes
`ApplicationDbContext` (1164 methods). It runs once on an empty tenant and once on a small connected
org with real ids. Result: 0 translation or SQL failures. It catches real failures: with the old
PostgreSQL-era `GetCompletionsByDayAsync` line put back, it fails and names that method.

Raw SQL: all 23 files were read by hand. Every statement uses SQL Server syntax. Two comments
still described PostgreSQL and were corrected.

## The 37 call sites still never run

All reached only when earlier lookups find specific rows (devices, conversation favourites, coverage
records, monitoring overrides, job state). Each was read: they use only equality, `IN (...)`
(`Contains`), `IS NULL`, `AND`/`OR`, simple projections, and updates that set constants or
parameters. Those constructs are proven by the more than 1100 call sites that did run. The two
unusual queries the sweep couldn't reach (unread counts and the employee list with attendance) got
focused tests instead.

These are **reviewed, not run**. A wrong column or type in one of them would still only show
at runtime.

| File | Method | Lines | Calls |
|---|---|---|---|
| `Persistence/Repositories/Auth/UserAccounts/EfUserAccountRepository.cs` | `RevokeDeviceAsync` | 56 | ExecuteUpdateAsync |
| `Persistence/Repositories/Auth/UserAccounts/EfUserAccountRepository.cs` | `RevokeAllAccessAsync` | 83, 90 | ExecuteUpdateAsync |
| `Persistence/Repositories/Conversations/EfConversationRepository.cs` | `AddMessageIfMissingAsync` | 138 | ExecuteUpdateAsync |
| `Persistence/Repositories/Conversations/EfConversationRepository.cs` | `SetFavoriteAsync` | 420 | AnyAsync |
| `Persistence/Repositories/Conversations/EfConversationRepository.cs` | `SetMutedAsync` | 451 | AnyAsync |
| `Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` | `FillPendingPositionsAsync` | 482, 499 | ToDictionaryAsync, ToListAsync |
| `Persistence/Repositories/CoreHr/EfOnboardingInboxRepository.cs` | `EmployeeByUserAsync` | 148 | ToListAsync |
| `Persistence/Repositories/CoreHr/EmployeeVisibilityScopeResolver.cs` | `ExpandCoveredEmployeesAsync` | 106, 115, 122, 148, 178 | ToDictionaryAsync, ToListAsync |
| `Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs` | `GetTargetNamesAsync` | 103, 111, 118 | ToListAsync |
| `Persistence/Repositories/Monitoring/TrayActivation/EfTrayActivationRepository.cs` | `RevokeActiveRegistrationsForIdentityAsync` | 70, 77 | ExecuteUpdateAsync |
| `Persistence/Repositories/OrgStructure/LegalEntity/EfLegalEntityRepository.cs` | `GetAccessibleByIdAsync` | 83 | FirstOrDefaultAsync |
| `Persistence/Repositories/WorkManagement/EfWorkApprovalHistoryRepository.cs` | `ListForEmployeeAsync` | 59 | ToDictionaryAsync |
| `Services/CoreHr/OfficeProjectProvisioner.cs` | `EnsureAsync` | 35, 38, 114 | FirstAsync, FirstOrDefaultAsync |
| `Services/Monitoring/ActivityMonitoring/MonitoringToggleResolverService.cs` | `IsPhotoRequiredCoreAsync` | 544 | FirstOrDefaultAsync |
| `Services/Monitoring/Notifications/LocationRuleEvaluatorJob.cs` | `RunOnceAsync` | 120, 124 | FirstOrDefaultAsync |
| `Services/Monitoring/Notifications/WellnessRuleEvaluatorJob.cs` | `PushToTraysAsync` | 170 | ToDictionaryAsync |
| `Services/Monitoring/Retention/MonitoringRetentionPurgeRunner.cs` | `RunOnceAsync` | 84 | ExecuteDeleteAsync |
| `Services/Monitoring/Settings/MonitoringPolicyConfigurationService.cs` | `UpsertOverrideAsync` | 85 | FirstOrDefaultAsync |
| `Services/Monitoring/Settings/MonitoringPolicyConfigurationService.cs` | `TargetExistsAsync` | 175, 176, 178 | AnyAsync |
| `Services/Monitoring/Settings/MonitoringPolicyConfigurationService.cs` | `ResolveTargetNameAsync` | 185, 187 | FirstOrDefaultAsync |
| `Services/OrgStructure/ReportingLineMoveService.cs` | `MoveAsync` | 71 | ToListAsync |
| `Services/OrgStructure/ReportingLineMoveService.cs` | `AddNotificationsAsync` | 148 | ToDictionaryAsync |

## What this does not prove

- The sweep proves a query translates and runs, not that it returns the right rows. Result
  correctness is covered only where focused tests exist.
- Only `ONEVO.Infrastructure` was measured. `ONEVO.Application` and `ONEVO.Api` run no queries
  themselves (no `ApplicationDbContext` use or async query calls), so nothing is missed there.
- Generated arguments are not realistic data, so a query whose SQL depends on argument values
  (for example a filter added only when an option is set) may have run only in one shape.
