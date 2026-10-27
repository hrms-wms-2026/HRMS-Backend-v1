# Backend Test-Failure Fix and Verification Report

## Scope and constraints

This continuation was restricted to the backend worktree:

```text
C:\onevoNew\HRMS-Backend-v1
```

The work remained on branch `local/reporting-manager-run`. No frontend file was modified. No file was staged, committed, or pushed. Temporary logs, TRX directories, PID files, generated SQL, and diagnostic scripts were removed before the final status review.

## Root cause 1: EF InMemory attendance-query translation

Six employee-list repository tests failed because `EfEmployeeRepository.ListVisibleAsync` embedded `resolutions.Min(resolution => resolution.WorkDate)` and `resolutions.Max(resolution => resolution.WorkDate)` inside an EF `IQueryable` attendance predicate. The EF Core InMemory provider attempted to translate those lambdas and raised a translation exception, even though the resolution collection had already been materialized.

The repository now computes `minWorkDate` and `maxWorkDate` as local `DateOnly` scalars before constructing the attendance query. The provider receives only scalar tenant, employee-id, and date comparisons. Attendance batch loading, legal-entity timezone evaluation, warning-first ordering, permission gating, and server-side pagination behavior were not weakened or removed. [1]

## Root cause 2: Attendance migration was not discoverable by EF

The source-only attendance migration created `attendance_records`, `presence_sessions`, and `break_records`, but it had no generated Designer file and was missing the metadata EF uses to associate a migration with `ApplicationDbContext`. Consequently, EF skipped `20260821120000_AddAttendanceReadModel`, and the subsequent `20260822063849_AddBreakRecordOpenUniqueness` migration attempted to create an index on the absent `break_records` table. The PostgreSQL failure was:

```text
42P01: relation "break_records" does not exist
```

The attendance migration now imports `Microsoft.EntityFrameworkCore.Infrastructure` and declares both `[DbContext(typeof(ApplicationDbContext))]` and `[Migration("20260821120000_AddAttendanceReadModel")]`. [2]

The Release EF migration list now proves the required ordering:

```text
20260821092355_AddLegalEntityBreakDurationMinutes
20260821120000_AddAttendanceReadModel
20260822063849_AddBreakRecordOpenUniqueness
```

The check was run with `--configuration Release`; an earlier no-configuration check inspected the stale Debug output and therefore did not reflect the fixed source. `dotnet ef migrations has-pending-model-changes` subsequently passed against the Release artifacts and reported that no model changes are pending.

## Root cause 3: Active-company integration fixture lacked the active lookup row

`SwitchActiveCompanyIntegrationTests` seeded employees with the default `EmploymentStatusId` value but did not seed the corresponding `EmploymentStatus` lookup row. The repository path used by the test performs an inner join and requires the status code `active`, so the target employee was not returned and the switch assertion failed.

The fixture now imports `ONEVO.Domain.Lookups` and seeds `EmploymentStatus { Id = 1, Code = "active" }` before inserting the test employees. This preserves the production lookup join and fixes only the incomplete test fixture. [3]

## Root cause 4: Project creation response mislabeled an employee ID as a user ID

The first complete integration attempt after the migration and fixture fixes exposed an independent Work Management failure:

```text
ONEVO.Tests.Integration.Features.WorkManagement.CreateProjectEndpointTests.ListForMember_MultiObjectiveMembership_DoesNotDuplicateProjectRow
System.InvalidOperationException: Sequence contains no elements
CreateProjectEndpointTests.cs:627
```

The failing helper searched for `Employee.UserId == creatorMembership.userId`. `ProjectMapper.ToSummary(ProjectMember)` populated the public `ProjectMembershipSummaryDto.UserId` field with `ProjectMember.EmployeeId`, which is an internal employee identifier rather than the authenticated user identifier. The API contract and test both identify this field as `UserId`. [4]

The mapper now accepts the authenticated user ID explicitly, and `CreateProjectCommandHandler` passes the current user ID when constructing the creator-membership response. The existing handler unit test was corrected to assert the documented public value. No membership storage schema or authorization boundary was changed. [5]

## Verification results

| Verification | Result |
|---|---:|
| Focused employee-list repository tests | **14 passed, 0 failed** |
| Complete Release unit suite | **2,819 passed, 0 failed** |
| Focused project-handler unit test for creator membership | **1 passed, 0 failed** |
| Release API build | **Passed** |
| Architecture suite | **647 passed, 0 failed** |
| Focused API boot integration tests on fresh PostgreSQL | **2 passed, 0 failed** |
| Focused active-company integration suite, corrected namespace filter | **2 passed, 0 failed** |
| Focused Work Management duplicate-membership integration test | **1 passed, 0 failed** |
| Release EF migration list | **Passed; attendance migration present in required order** |
| Release EF pending-model check | **Passed; no model changes pending** |
| `git -c core.safecrlf=false diff --check` | **Passed** |

The initial active-company rerun used an incorrect namespace filter and executed zero tests. That command was not counted as verification; the corrected filter was then run and executed both tests successfully.

## Complete integration-suite status

Docker/Testcontainers became available during this session, so the migration failure was reproduced and diagnosed against fresh PostgreSQL containers rather than dismissed as an environment limitation. The focused `ApiBootTests` suite passed after the migration discovery fix, and the focused active-company suite passed after its fixture fix.

A fresh complete integration run was then started after all four fixes. It produced **354 observed `Passed` output markers and zero `[FAIL]`, `Test Run Failed`, or `Failed!` markers**, while continuing to create fresh containers and execute additional test classes. The run remained active for more than 100 minutes without emitting its aggregate summary or TRX result and was stopped by terminating only that test-process tree. Therefore, there is no defensible complete-suite aggregate count to report: the full integration suite is **not claimed as fully completed**. The focused PostgreSQL results above are the completed integration proofs available for the fixes.

The run’s output also contained expected negative-path exception logging, including FluentValidation and unique-constraint messages. Those messages were not counted as test failures; no actual failed-test marker was observed before the run was stopped.

## Final repository state

The intended remaining changes are:

```text
src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs
src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs
src/ONEVO.Infrastructure/Migrations/20260821120000_AddAttendanceReadModel.cs
src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs
tests/ONEVO.Tests.Integration/Auth/Session/SwitchActiveCompanyIntegrationTests.cs
tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs
TIME_TRACKING_BACKEND_TEST_FAILURE_FIX_REPORT.md
```

The report is intentionally untracked, as requested. The worktree has no staged files. No frontend path appears in the backend diff, and no commit or push was performed.

Known non-failing warnings remain, including package advisories, duplicate-using warnings, nullable/compiler warnings, Testcontainers obsolescence notices, and development-only MediatR/Fluent Assertions license notices. None caused a test failure in the completed verification runs.

## References

[1]: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` — employee-list attendance batch query and scalar date bounds.

[2]: `src/ONEVO.Infrastructure/Migrations/20260821120000_AddAttendanceReadModel.cs` — discoverable attendance read-model migration.

[3]: `tests/ONEVO.Tests.Integration/Auth/Session/SwitchActiveCompanyIntegrationTests.cs` — active employment-status fixture seed.

[4]: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectCreationResponse.cs` — public `ProjectMembershipSummaryDto.UserId` contract.

[5]: `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs` and `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs` — corrected creator-membership response mapping.
