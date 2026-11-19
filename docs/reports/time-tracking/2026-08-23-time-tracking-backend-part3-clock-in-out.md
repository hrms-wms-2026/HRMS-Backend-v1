# Time Tracking Backend Part 3 — Clock In / Clock Out

## Status

Backend Part 3 is implemented for the current authenticated tenant employee. The backend now exposes self-service clock-in and clock-out mutations, persists attendance records through the existing Part 2 attendance read-model foundation, and returns the updated Today response after each successful mutation.

The implementation is **ready for Frontend Time Tracking action buttons**, subject to the remaining operational caveat that Docker/Testcontainers integration verification was unavailable in this environment. Backend Part 4 — Start Break / End Break — is still required before the complete break-action UI can be considered implemented.

## Scope and preserved worktree state

The work was performed only in `C:\onevoNew\HRMS-Backend-v1`. No frontend files were touched. No commit or push was made. The repository began as a pre-existing dirty worktree on `local/reporting-manager-run`; unrelated modified and untracked Part 2 files were preserved.

No schema change was necessary. The existing `attendance_records` unique index on `(tenant_id, employee_id, date)` remains the concurrency boundary, and the existing attendance/break RLS migration was not altered.

## Files changed for Part 3

| Layer | Files changed or added for Part 3 |
|---|---|
| Domain | `src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs` — added narrowly scoped source, work-area, work-time, and status constants. |
| Application service | `src/ONEVO.Application/Features/TimeAttendance/Services/IAttendanceTodayStateService.cs`; `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` — shared authenticated-employee context and Today-state computation. |
| Application commands | `src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommand.cs`; `ClockInCommandValidator.cs`; `ClockInCommandHandler.cs`; `src/ONEVO.Application/Features/TimeAttendance/Commands/ClockOut/ClockOutCommand.cs`; `ClockOutCommandHandler.cs`. |
| Application query flow | `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` — Today now delegates to the shared service while history behavior remains intact. |
| Application repository contract | `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs` — tracked fetch, add, save, open-break, and completed-break aggregation methods. |
| Application DI | `src/ONEVO.Application/DependencyInjection.cs` — registered `IAttendanceTodayStateService`. |
| Infrastructure repository | `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs` — tracked mutation path, local-day break queries, aggregation, and unique-conflict mapping. |
| API | `src/ONEVO.Api/Contracts/Attendance/TimeTracking/ClockInRequest.cs` — `ClockInRequest` and empty `ClockOutRequest`; `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs` — mutation routes. |
| Unit tests | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs` — updated fixture composition; `tests/ONEVO.Tests.Unit/Features/TimeAttendance/ClockInOutCommandHandlerTests.cs`; `tests/ONEVO.Tests.Unit/Controllers/Tenant/Attendance/TimeTrackingControllerTests.cs`. |
| Architecture tests | `tests/ONEVO.Tests.Architecture/TimeTrackingMutationArchitectureTests.cs`. |

The following pre-existing Part 2 worktree files were not treated as new Part 3 schema work: the attendance read DTOs, read queries, read-model migration, `ApplicationDbContext`, Infrastructure DI registrations, and Part 2 report.

## Endpoints and contracts

| Method | Route | Authentication / permission | Success response |
|---|---|---|---|
| `POST` | `/api/v1/attendance/time-tracking/clock-in` | `[Authorize(Policy = "TenantPolicy")]`; no `attendance:write` action gate | `200 OK` with `AttendanceTodayResponse` |
| `POST` | `/api/v1/attendance/time-tracking/clock-out` | `[Authorize(Policy = "TenantPolicy")]`; no `attendance:write` action gate | `200 OK` with `AttendanceTodayResponse` |

Clock-in accepts only the following client field:

```json
{
  "source": "web"
}
```

The validator currently supports `web`, case-insensitively after trimming. Coordinates, photo identifiers, biometric fields, device fields, tenant identifiers, employee identifiers, legal-entity identifiers, work dates, and client timestamps are not accepted. Clock-out has no client-controlled fields and is issued as `new ClockOutCommand()`.

Both actions derive tenant, user, employee, legal entity, local work date, and server time from authenticated context and `IDateTimeProvider`. The response is the same `AttendanceTodayResponse` contract used by `GET /api/v1/attendance/time-tracking/today`, so the frontend can update its state from the mutation response without a second Today request.

## Shared Today-state computation

`AttendanceTodayStateService` is now the single source for Today-state computation used by `GetAttendanceTodayQueryHandler`, `ClockInCommandHandler`, and `ClockOutCommandHandler` through the shared service contract. The service resolves the current employee with `GetDefaultForUserAsync`, resolves the employee’s legal entity, converts `IDateTimeProvider.UtcNow` into the legal entity timezone, and derives the local `DateOnly` work date.

The service preserves the Part 2 policy behavior: only active, full-company, date-effective policies are considered. Zero policies produce `not_configured`; multiple policies produce `configuration_conflict`; no policy is selected arbitrarily. Work-mode policy fields are selected from the active WorkMode lookup code rather than employment type. `onsite`, `remote`, `hybrid`/`either`, and `field` map to their corresponding policy fields. An unresolved work mode fails closed for source authorization.

## Clock-in behavior

The handler first validates the source, authenticated employee context, schedule, working day, effective company policy, and work-mode source permission. Schedule configuration requires legal-entity timezone, work start time, and work end time. The local date must be present in the legal entity’s standard working days. Holiday and time-off overrides are intentionally not implemented.

When no record exists for the current tenant, employee, and legal-entity local date, the handler creates one. When a row exists with no `ActualStart`, it fills that row. A row with an existing `ActualStart` and no `ActualEnd` returns `already_clocked_in`. A row with an existing `ActualEnd` returns `already_clocked_out`. No multiple clock-ins are allowed for the same employee and local day.

The persisted clock-in fields include the tenant and employee identifiers, local work date, `ExpectedWorkingDay = true`, fixed work-time type, schedule start/end, required minutes, work-mode-derived expected work area, schedule timezone, `IsHoliday = false`, server-derived `ActualStart`, null `ActualEnd`, zero initial worked/break minutes, late minutes, `AttendanceSource = web`, status `on_time` or `late`, and provider-derived timestamps.

Late minutes are calculated from the legal-entity local clock-in time relative to the configured local scheduled start. Hybrid employees persist the existing inventory-compatible `either` expected-work-area value while the read-model normalization continues to expose `hybrid` to API consumers.

## Clock-out behavior

The handler resolves the same authenticated employee, legal entity, timezone, local work date, and server time. It loads the tracked attendance record for that local date. Missing records and records without `ActualStart` return `not_clocked_in`; an existing `ActualEnd` returns `already_clocked_out`.

The legal-entity local-day window is converted to UTC before break queries. An open break overlapping the current local day blocks clock-out with `open_break_must_be_ended_before_clock_out`; the handler does not auto-end it. Completed break durations are summed over the same local-day window, clipped to that window, and subtracted from the server-time interval between `ActualStart` and `ActualEnd`. `BreakMinutes`, `WorkedMinutes`, `ActualEnd`, and `UpdatedAt` are then persisted on the tracked record.

The final status is `short_hours` when configured required minutes exceed worked minutes; otherwise it is `clocked_out`. No payroll deduction or late-deduction behavior is applied. Existing late minutes are preserved during clock-out.

## Transaction and concurrency behavior

Each mutation runs through the existing Application `IUnitOfWork.ExecuteInTransactionAsync` abstraction. The attendance repository uses a tracked EF entity for updates and `AddAsync` for new records; it does not attach a detached entity and blindly call `Update`.

The attendance repository maps PostgreSQL unique violations from `SaveChangesAsync` to the existing application-level `UniqueConstraintConflictException`. The clock-in handler converts that race into a clean `409 Conflict` rather than exposing a provider exception. This handles two simultaneous clock-in requests that both observe no row and race on the existing unique tenant/employee/date index. Repository concurrency exceptions are also mapped through the existing application-level concurrency signal and returned as a clean conflict.

## Error mapping

| Condition | Result |
|---|---:|
| Missing authenticated context or tenant context | Existing forbidden/result convention; the controller’s tenant policy remains the normal authentication boundary. |
| Current employee or legal entity missing | `404 Not Found` from the shared context service. |
| Schedule missing | `409 Conflict`, `schedule_not_configured`. |
| Off day | `409 Conflict`, `off_day`. |
| No active full-company effective policy | `409 Conflict`, `clock_in_policy_not_configured`. |
| Multiple active full-company policies | `409 Conflict`, `multiple_active_company_policies`. |
| Web source disallowed by work-mode policy | `403 Forbidden`. |
| Already clocked in | `409 Conflict`, `already_clocked_in`. |
| Already clocked out/day closed | `409 Conflict`, `already_clocked_out`. |
| Clock-out before clock-in | `409 Conflict`, `not_clocked_in`. |
| Open break at clock-out | `409 Conflict`, `open_break_must_be_ended_before_clock_out`. |
| Unsupported source payload | `400 Bad Request` through the validation pipeline. |

The permission decision is deliberate: these routes are authenticated self-service actions and do not require `attendance:write`. The existing product foundation assigns `attendance:write` to Clock-in Policy administrative mutations, while the Part 2 authority report explicitly treats self-service visibility as authenticated self-service rather than management coverage. No request path allows one employee to identify or mutate another employee.

## Tests added and verification

| Verification | Result |
|---|---|
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Passed; 0 errors and 0 warnings in the final run. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "TimeTracking\|AttendanceRead\|ClockIn\|ClockOut\|ClockInPolicy"` | Passed; 53 passed, 0 failed, 0 skipped. The command was run with shell-safe equivalent quoting. |
| Focused `ClockInOutCommandHandlerTests` run | Passed; 15 passed, 0 failed, 0 skipped. |
| Focused `TimeTrackingControllerTests` run | Passed; 3 passed, 0 failed, 0 skipped. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Passed; 642 passed, 0 failed, 0 skipped. |
| `git diff --check` | Exit code 0. Git emitted only line-ending advisory warnings for existing modified files; no whitespace or conflict-marker errors were reported. |
| RLS architecture coverage | Passed as part of the 642 architecture tests; the existing attendance migration still enables and forces tenant RLS with the tenant isolation policy. |

The focused unit tests cover successful record creation, legal-entity local-date derivation, schedule persistence, late-minute calculation, Today response reuse, missing employee, missing schedule, off day, missing policy, multiple active policies, disallowed web source, already-clocked-in/out states, duplicate-record race mapping, clock-out calculation, completed-break subtraction, local-day break windows, missing record, missing clock-in, already-clocked-out state, and open-break rejection. Controller tests cover both `200 OK` mutation routes, command forwarding, and absence of tenant/employee/legal-entity identifiers from request contracts.

## Skipped checks and remaining risks

Integration tests were not run. A current Docker availability check returned `Error response from daemon: Docker Desktop is unable to start`, so a real PostgreSQL/Testcontainers verification environment was not available. The branch’s earlier Part 2 and authority reports also document unrelated integration-project compile failures in pre-existing BulkOnboarding test work; those checks were not re-run in this Part 3 session because the Docker prerequisite was already unavailable.

No live database migration or HTTP smoke test was performed. The existing attendance schema and RLS migration are unchanged, so there is no new migration to apply for this Part 3 code. A configured PostgreSQL environment should still verify the unique-race mapping, tracked writes, break-window SQL predicates, RLS behavior, and the exact JSON response at the HTTP boundary before production release.

Part 4 Start Break / End Break remains outstanding. This Part 3 implementation reads existing break records and blocks clock-out while a break is open, but it deliberately does not create, update, or auto-end breaks.

## Final repository state

The final branch remains `local/reporting-manager-run`. No files were staged, no commit was created, and nothing was pushed. Frontend files were not touched.

## References

[1]: `TIME_TRACKING_BACKEND_PART2_READ_MODEL_REPORT.md` — Part 2 attendance Today/read-model decisions and timezone behavior.
[2]: `CLOCK_IN_POLICY_BACKEND_PART1_REPORT.md` — Clock-in Policy scope, source fields, permissions, and effective-policy behavior.
[3]: `LEGAL_ENTITY_WORK_TIME_BACKEND_REPORT.md` — Legal-entity schedule configuration and same-day work-time semantics.
[4]: `LEGAL_ENTITY_BREAK_DURATION_BACKEND_REPORT.md` — Legal-entity break-duration storage and validation.
[5]: `EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0_REPORT.md` — Authenticated self-service and authority-resolution conventions.
[6]: `EMPLOYEE_LIST_AUTHORITY_RESOLVER_BACKEND_PART1_REPORT.md` — Current employee/legal-entity resolution and tenant-boundary decisions.
