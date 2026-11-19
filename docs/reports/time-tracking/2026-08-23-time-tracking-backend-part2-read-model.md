# Backend Part 2 — Time Tracking Read Model Correction Report

## Scope

The first implementation was audited against the correction plan and corrected without touching frontend code, starting Backend Part 3 mutations, committing, or pushing.

## What was wrong in the first implementation

| Defect | Impact | Correction |
|---|---|---|
| `shouldHaveClockedIn` used `!record?.ActualStart.HasValue == true` | A missing `AttendanceRecord` produced `false`, so a normal post-start day with no row was not flagged. | Replaced it with explicit `hasActualClockIn`, schedule, working-day, and local-time variables. It now works both when the row is absent and when `ActualStart` is null. |
| Covered history validated `employeeId` but still queried all visible IDs | A filtered request returned unrelated visible employees. | When `employeeId` is supplied, the handler verifies visibility first and queries only that ID. |
| Covered-history identity was returned as empty strings | The frontend could not display employee identity and received fake values. | Added a single batched identity projection with display name, employee number, position, department, and avatar file ID. Missing optional fields are null. |
| Handler logic was densely compressed into one-line expressions | Business rules were difficult to review and easy to change incorrectly. | Refactored schedule, policy, break, status, action, local-day-window, and message logic into named variables and helper methods. |
| Break query used server/UTC midnight semantics | Non-UTC legal entities could read the wrong local work day. | Local midnight is converted through the legal-entity `TimeZoneInfo` to the correct UTC instants before querying. Break durations are clipped to that local-day window. |
| No matching regression tests existed | The prior report overstated confidence in the new behavior. | Added focused handler and repository tests and reran them. |

## Corrected behavior

### Today state and clock-in detection

`workDate` is derived by converting `IDateTimeProvider.UtcNow` into the legal entity timezone. The handler calculates:

```text
hasActualClockIn = attendanceRecord?.ActualStart is not null
isAtOrAfterScheduledStart = local time >= configured local start
shouldHaveClockedIn = working day && configured schedule && at/after start && !hasActualClockIn
```

Consequently, the flag is true for both an absent attendance row and a row whose `ActualStart` is null, and false before start, on an off day, without a schedule, after an actual clock-in, or after clock-out.

### Covered history filtering and identity enrichment

The covered endpoint still requires `attendance:read` and resolves visibility with `EmployeeAuthorityPurpose.TimeTrackingRead`. An omitted `employeeId` queries the complete resolver-returned visible set. A supplied ID must first be in that set; otherwise the endpoint returns 403 and does not query attendance records. A valid supplied ID becomes the sole repository filter.

The new `ListEmployeeIdentitiesAsync` repository method performs one `AsNoTracking` batched query scoped by tenant, legal entity, and requested IDs. It joins department and active primary position data and returns `displayName`, `employeeNumber`, `position`, `department`, and `avatarFileId`. It exposes no storage key or internal file path.

### Timezone and break windows

For a legal entity in `Asia/Colombo` on work date `2026-08-21`, the repository window is local `2026-08-21 00:00` through local `2026-08-22 00:00`, represented as UTC `2026-08-20T18:30:00Z` through `2026-08-21T18:30:00Z`. Completed and open breaks are intersected with that window; open breaks are measured through the current provider time and never beyond the local-day end.

### Schedule and policy behavior

Standard working days use ISO weekday values: Monday 1 through Sunday 7. Existing legal-entity fallback semantics are retained for malformed or empty stored JSON. Schedule configuration requires timezone, work start, and work end; missing configuration disables actions and returns `schedule_not_configured` / `no_schedule` behavior.

The effective policy is limited to active, date-effective, company-wide (`full_company`) policies. No policy returns `policyStatus = not_configured`, disables clock-in, and emits `clock_in_policy_not_configured`. Multiple active policies return `policyStatus = configuration_conflict`, disable clock-in, and emit `multiple_active_company_policies`; no policy is selected arbitrarily.

Allowed methods are derived from the active employee WorkMode lookup code, not employment type. `onsite`, `remote`, `hybrid`, and `field` map to the corresponding existing policy fields. Persistence may retain `Either*`; the response terminology remains `hybrid`.

## Exact files changed

| Layer | Files |
|---|---|
| Application | `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs`; `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs`; `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs`. |
| Infrastructure | `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs`. |
| Tests | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs`; `tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs`. |
| Report | `TIME_TRACKING_BACKEND_PART2_READ_MODEL_REPORT.md`. |

The earlier Part 2 files for entities, EF mappings, DbSets, migration, controller, and dependency registrations remain part of the uncommitted worktree. No frontend files were changed.

## Tests added

`AttendanceReadHandlerTests` now covers missing-row and null-clock-in `shouldHaveClockedIn`, before-start and off-day behavior, non-UTC break windows, active-break start, exhausted allowance, open-break end-only behavior, missing allowance, all four work modes, missing policy, multiple active policies, filtered covered history, unfiltered visible IDs, identity preservation, and out-of-scope employee rejection.

`EfAttendanceReadRepositoryTests` covers one batched identity query, tenant filtering, legal-entity filtering, requested-ID filtering, display-name and employee-number projection, and avatar-file-ID projection.

## Verification commands and results

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Passed with two unrelated pre-existing warnings in `PositionsController.cs` and `AdminAuthController.cs`. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "AttendanceReadHandler|EfAttendanceReadRepository|TimeTracking|AttendanceRead"` | Passed: 19 tests, 0 failed, 0 skipped. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Passed in the previous final verification after RLS coverage was added; the new correction changes application/repository/test code only and does not alter the RLS model. |
| `git diff --check` | Passed. |

Restore-enabled checks were not used as the final gate because this checkout reports the unrelated NuGet error `Value cannot be null. (Parameter 'path1')`. Integration tests were not run because Docker/Testcontainers availability and a runnable integration environment were not established.

## Safety assessment and remaining risks

The corrected read model is now suitable as the contract foundation for Frontend Time Tracking Part 1 and Backend Clock In/Out Part 3: self reads remain employee-scoped, covered reads are authority-scoped and filterable, policy methods are server-derived, and local date/break calculations use the legal-entity timezone.

Remaining risks are intentionally outside this correction: the attendance migration has not been applied to a live database; holiday/time-off integration is not implemented; position/department enrichment uses the current active primary assignment model; correction and mutation actions remain disabled; and integration tests still need to run in a configured database environment.

No files were staged, committed, or pushed.
