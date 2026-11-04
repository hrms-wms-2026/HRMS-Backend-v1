# Time Tracking Backend Part 4 — Break Actions Report

## Status

Backend Part 4 is implemented in `C:\onevoNew\HRMS-Backend-v1`. The backend now exposes authenticated self-service **start-break** and **end-break** mutations for the current tenant employee. Both successful mutations return the existing `AttendanceTodayResponse` used by the Today read endpoint and the Part 3 clock-in/clock-out mutations.

The implementation is **ready for frontend break buttons**, subject to applying the new database migration and completing PostgreSQL/Testcontainers verification before production release. No frontend files were touched, and no files were staged, committed, or pushed.

## Files changed for Part 4

| Layer | Files changed or added | Purpose |
|---|---|---|
| API | `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs` | Added the two break POST routes; both remain under `TenantPolicy` and have no management permission gate. |
| API contracts | `src/ONEVO.Api/Contracts/Attendance/TimeTracking/BreakRequests.cs` | Added empty `StartBreakRequest` and `EndBreakRequest` marker records. No client-controlled fields are exposed. The controller actions require no request body. |
| Application commands | `src/ONEVO.Application/Features/TimeAttendance/Commands/StartBreak/StartBreakCommand.cs`; `StartBreakCommandHandler.cs`; `src/ONEVO.Application/Features/TimeAttendance/Commands/EndBreak/EndBreakCommand.cs`; `EndBreakCommandHandler.cs` | Added MediatR commands and handlers using the shared Today-state context, attendance repository, transaction abstraction, and existing `Result<T>` conventions. |
| Application repository contract | `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs` | Added tracked current-day/global open-break lookups and `AddBreakAsync`. |
| Infrastructure repository | `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs` | Added tracked break queries, break insertion, tenant/employee scoping, and reuse of existing persistence exception translation. |
| Infrastructure model configuration | `src/ONEVO.Infrastructure/Persistence/Configurations/TimeAttendance/AttendanceReadModelConfigurations.cs` | Added PostgreSQL `xmin` optimistic-concurrency mapping and a filtered unique open-break index. |
| Migration and model artifacts | `src/ONEVO.Infrastructure/Migrations/20260822063849_AddBreakRecordOpenUniqueness.cs`; `.Designer.cs`; `ApplicationDbContextModelSnapshot.cs` | Added the partial unique index and synchronized EF target-model metadata. The migration itself only creates the index; it does not recreate the already-existing attendance tables. |
| Unit tests | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/BreakCommandHandlerTests.cs`; updates to `ClockInOutCommandHandlerTests.cs`, `EfAttendanceReadRepositoryTests.cs`, and `Controllers/Tenant/Attendance/TimeTrackingControllerTests.cs` | Covered successful actions, rejection rules, local-day behavior, tracked persistence, controller forwarding, and race conflicts. |
| Architecture tests | `tests/ONEVO.Tests.Architecture/TimeTrackingMutationArchitectureTests.cs` | Guarded routes, self-service permission boundaries, contract shape, no-EF application dependency, migration/index integrity, and existing attendance RLS declarations. |
| Report | `TIME_TRACKING_BACKEND_PART4_BREAK_ACTIONS_REPORT.md` | This report. |

The worktree already contained unrelated, uncommitted Part 2 and Part 3 backend files before this task. Those files were preserved and were not treated as new Part 4 scope.

## Endpoints and contracts

| Method | Route | Authentication / permission | Request body | Success response |
|---|---|---|---|---|
| `POST` | `/api/v1/attendance/time-tracking/break/start` | `[Authorize(Policy = "TenantPolicy")]`; no `attendance:write` requirement | None required; an empty object is also represented by `StartBreakRequest` if a client contract is desired. | `200 OK` with `AttendanceTodayResponse` |
| `POST` | `/api/v1/attendance/time-tracking/break/end` | `[Authorize(Policy = "TenantPolicy")]`; no `attendance:write` requirement | None required; an empty object is also represented by `EndBreakRequest` if a client contract is desired. | `200 OK` with `AttendanceTodayResponse` |

The controller actions do not accept tenant, employee, legal-entity, date, break timestamps, duration, or client time. The application derives tenant and user context from the authenticated request, resolves the employee and legal entity through the shared Today-state service, and uses `IDateTimeProvider.UtcNow` indirectly through that context.

The permission decision follows the Part 3 self-service precedent. These routes mutate only the authenticated employee’s own attendance state and do not accept an employee identifier, so they do not require the management-oriented `attendance:write` permission. The existing tenant authentication policy remains the authentication boundary. This is consistent with the self-service decision documented for clock-in and clock-out in the Part 3 report [1] and the existing read-model authority boundary [2].

## Start-break behavior

The handler first resolves the shared Today-state context. It rejects an unconfigured schedule with `schedule_not_configured` and a non-working local date with `off_day`. The local work date is the date derived from the legal entity timezone; no date is accepted from the client.

Break allowance is read from `LegalEntity.BreakDurationMinutes`. A null value returns `409 Conflict` with `break_allowance_not_configured`. A zero allowance, or completed local-day break usage greater than or equal to the configured allowance, returns `409 Conflict` with `break_allowance_used`.

The handler then loads the tracked attendance record for the derived legal-entity local work date. A missing record or a record without `ActualStart` returns `not_clocked_in`. A record with `ActualEnd` returns `already_clocked_out`.

The handler checks for a tracked open break that started during the current local-day window. It also checks for any older global open break for the same tenant and employee. Either condition returns `break_already_active`; the latter prevents a stale open break from being hidden by a new break. This ensures that two open breaks cannot be created for the employee.

On success, the handler persists a new `BreakRecord` with the following server-owned values:

| Field | Persisted value |
|---|---|
| `Id` | New `Guid` |
| `TenantId` | Authenticated employee tenant |
| `EmployeeId` | Authenticated employee |
| `BreakStart` | `AttendanceTodayContext.UtcNow` |
| `BreakEnd` | `null` |
| `BreakType` | `null`; not exposed because product documentation does not require it |
| `AutoDetected` | `false` |
| `CreatedAt` | `AttendanceTodayContext.UtcNow` |

The handler does not mutate `AttendanceRecord.WorkedMinutes` or apply payroll, late-deduction, device, biometric, geofence, holiday, time-off, shift, or correction behavior.

## End-break behavior

The handler resolves the same shared context and requires a configured working schedule and current working local date. It loads the tracked attendance record for the current legal-entity local date. Missing attendance or missing `ActualStart` returns `not_clocked_in`; an existing `ActualEnd` returns `already_clocked_out`.

It then loads only an open break whose start lies inside the current local-day window. If none exists, it performs a global open-break check to distinguish a stale open break from no open break. A current-day absence, including a stale prior-day open break, returns `no_active_break`; the stale row is deliberately not silently closed. This decision avoids changing historical attendance state without an explicit correction workflow, which remains out of scope.

`BreakEnd` is always assigned from the server-side `UtcNow`. If that instant precedes `BreakStart`, the handler returns `409 Conflict` with `invalid_break_time` and does not persist the mutation.

After setting `BreakEnd`, the handler recalculates completed break minutes for the current local-day window. The current break’s duration is clipped to that same window and added to the repository’s completed-break total. The resulting value is written to the tracked `AttendanceRecord.BreakMinutes`. `WorkedMinutes` is intentionally unchanged until clock-out, and the handler never auto-clocks out or invokes payroll or late-deduction behavior.

## Local date, timezone, and allowance behavior

The shared `AttendanceTodayStateService` remains the single source of truth for authenticated employee resolution, legal entity, timezone, local work date, current UTC/local time, and local-day window. For a legal entity in `Asia/Colombo`, a local work date of `2026-08-21` is represented by the UTC window `2026-08-20T18:30:00Z` through `2026-08-21T18:30:00Z`, matching the established Part 2 and Part 3 behavior [1] [3].

The repository’s completed-break query intersects stored breaks with the supplied UTC local-day window. The application therefore does not use server midnight or client-provided dates when checking allowance usage or recalculating `AttendanceRecord.BreakMinutes`. The configured allowance remains nullable and non-negative as established by the Legal Entity break-duration implementation [4].

## Concurrency and persistence

Both mutations execute through `IUnitOfWork.ExecuteInTransactionAsync`. Start-break insertion uses a tracked attendance repository and `AddBreakAsync`; end-break closure uses a tracked `BreakRecord` and tracked `AttendanceRecord`, never a detached blind update.

A new filtered unique index is configured and migrated as follows:

```sql
CREATE UNIQUE INDEX ux_break_records_one_open_per_employee
ON break_records (tenant_id, employee_id)
WHERE break_end IS NULL;
```

This is a global per-tenant/per-employee open-break boundary rather than a local-day-only boundary. That choice is intentional: it prevents a historical open break from permitting a second open break on a later day. PostgreSQL unique violations are translated by the repository to `UniqueConstraintConflictException`; the start-break handler maps the race to `409 Conflict` with `break_already_active` rather than leaking a provider exception.

End-break concurrency uses PostgreSQL’s implicit `xmin` system column as an EF Core concurrency token through a shadow property. The handler mutates the tracked break and maps a concurrent update to `409 Conflict` with `break_already_ended`. The `xmin` mapping is model metadata and does not add a physical application column. The generated designer and model snapshot contain the same concurrency and index metadata, while the migration body contains only the index creation and removal.

The existing `AddAttendanceReadModel` migration continues to enable and force tenant RLS for `attendance_records`, `presence_sessions`, and `break_records`. The Part 4 migration does not weaken or replace those policies [5].

## Result and error mapping

| Condition | Status | Machine-readable error |
|---|---:|---|
| Missing authenticated context or tenant context | Existing tenant-policy/result convention | Existing forbidden behavior |
| Current employee or legal entity missing | `404` | Existing shared-context error |
| Schedule not configured | `409` | `schedule_not_configured` |
| Local date is not a working day | `409` | `off_day` |
| Break allowance is null | `409` | `break_allowance_not_configured` |
| Break allowance is zero or exhausted | `409` | `break_allowance_used` |
| No active attendance record or no `ActualStart` | `409` | `not_clocked_in` |
| Attendance already ended | `409` | `already_clocked_out` |
| Current or historical open break blocks a new start | `409` | `break_already_active` |
| No current-day open break to end | `409` | `no_active_break` |
| Server time precedes break start | `409` | `invalid_break_time` |
| Duplicate open-break race | `409` | `break_already_active` |
| Concurrent end-break update | `409` | `break_already_ended` |
| Technical persistence error | No provider exception exposed | Existing repository/application translation |

## Tests added

The focused handler tests cover start-break success, provider-derived `BreakStart`, tenant/employee persistence, allowance and local-day repository arguments, missing employee, null/zero/exhausted allowance, missing/not-started/clocked-out attendance, current and historical open breaks, duplicate-key race mapping, end-break success, provider-derived `BreakEnd`, completed-break recalculation, missing/not-started/clocked-out attendance, stale open-break protection, time anomaly rejection, and concurrent-end conflict mapping.

The repository tests cover break insertion with null-safe fields, tracked open-break retrieval scoped by tenant, employee, and local-day window, and completed-break summation clipped to the supplied local-day window. Controller tests cover both `200 OK` routes, command forwarding, and empty request-contract shape. Architecture tests cover route templates, absence of management permission attributes, identifier restrictions, EF-layer boundaries, RLS persistence, and migration/snapshot/index consistency.

## Verification results

| Command / check | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | **Passed** — 0 errors. The build reported two unrelated pre-existing warnings in `PositionsController.cs` and `AdminAuthController.cs`. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "TimeTracking\|AttendanceRead\|ClockIn\|ClockOut\|Break"` | **Passed** — 82 passed, 0 failed, 0 skipped. The exact filter was executed through a temporary command script so the Windows shell did not reinterpret the pipe characters. |
| Focused controller subset `FullyQualifiedName~TimeTracking` | **Passed** — 6 passed, 0 failed, 0 skipped. |
| Focused time-attendance subset `FullyQualifiedName~TimeAttendance` | **Passed** — 66 passed, 0 failed, 0 skipped. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | **Passed** — 643 passed, 0 failed, 0 skipped. |
| `dotnet ef migrations has-pending-model-changes --project src\ONEVO.Infrastructure --startup-project src\ONEVO.Api --configuration Release --no-build` | **Passed** — no changes detected. A process-local design-time `ConnectionStrings__MigrationConnection` value was supplied because the checkout intentionally does not contain a migration connection string. Runtime configuration was not modified. |
| `git diff --check` | **Passed** — exit code 0. Git emitted only existing Windows line-ending advisory messages; no whitespace errors or conflict markers were reported. |

## Skipped checks and remaining risks

PostgreSQL integration/Testcontainers tests and live HTTP smoke tests were not run. The repository’s preceding Part 3 verification documented that Docker Desktop could not start and no configured `ONEVO_TEST_DB` environment was available [3]. The new migration was generated and model-validated, but it was not applied to a live database in this task.

Applying the partial unique index can fail if a target database already contains multiple open break rows for the same tenant and employee. A deployment should inspect and resolve such duplicates before applying the migration. The global uniqueness decision intentionally blocks a new break while a stale prior-day open break exists; an explicit operational cleanup or future correction workflow may be needed for those rows.

The `xmin` concurrency behavior is PostgreSQL-specific and is not fully exercised by the in-memory repository tests. A configured PostgreSQL integration run should verify duplicate-key races, optimistic-concurrency races, tracked writes, RLS, migration application, and exact HTTP Problem Details responses before production deployment.

No broad integration suite was run because the required database/container environment was unavailable, and no frontend verification was performed because frontend work was explicitly excluded.

## Final readiness and repository state

The backend is ready for the frontend to implement the complete current flow:

| Frontend capability | Backend status |
|---|---|
| Today read model | Available at `GET /api/v1/attendance/time-tracking/today` from Part 2. |
| Clock-in | Available at `POST /api/v1/attendance/time-tracking/clock-in` from Part 3. |
| Start break | **Available** at `POST /api/v1/attendance/time-tracking/break/start`. |
| End break | **Available** at `POST /api/v1/attendance/time-tracking/break/end`. |
| Clock-out | Available at `POST /api/v1/attendance/time-tracking/clock-out`; existing behavior blocks an open break. |

The final branch remains `local/reporting-manager-run`. The worktree was already dirty before Part 4 with uncommitted Part 2 and Part 3 backend changes. Part 4 added further unstaged backend changes only. **No frontend files were touched. No files were staged. No commit was created. Nothing was pushed.**

## References

[1]: `TIME_TRACKING_BACKEND_PART3_CLOCK_IN_OUT_REPORT.md` — Part 3 self-service mutation, shared Today-state, local-day, and permission decisions.
[2]: `TIME_TRACKING_BACKEND_PART2_READ_MODEL_REPORT.md` — Part 2 Today/history authority and timezone behavior.
[3]: `TIME_TRACKING_BACKEND_PART3_CLOCK_IN_OUT_REPORT.md` — Part 3 verification limitations, including unavailable Docker/Testcontainers environment.
[4]: `LEGAL_ENTITY_BREAK_DURATION_BACKEND_REPORT.md` — Nullable legal-entity break allowance storage and validation.
[5]: `src/ONEVO.Infrastructure/Migrations/20260821120000_AddAttendanceReadModel.cs` — Existing attendance/break schema and tenant RLS declarations.
