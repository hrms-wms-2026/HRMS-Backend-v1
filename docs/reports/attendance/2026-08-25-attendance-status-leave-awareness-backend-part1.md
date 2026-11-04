# Attendance Status and Approved Leave Awareness — Backend Part 1

## Scope and repository state

Backend Part 1 was implemented only in `C:\onevoNew\HRMS-Backend-v1`. The frontend was not edited. No files were staged, committed, or pushed. The repository remains on `local/reporting-manager-run`.

The implementation establishes a backend-owned attendance-day status foundation, connects attendance reads to existing approved `LeaveRequest` data, expands employee-list attention summaries beyond missing clock-ins, and preserves real break duration when the configured allowance is exceeded. Time Off CRUD, public holidays, schedules, new schedule tables, and frontend work remain out of scope.

## Existing Leave and Time Off evidence used

The implementation uses the existing `LeaveRequest` entity and `LeaveRequestStatuses.Approved` constant. The source entity stores `TenantId`, `EmployeeId`, inclusive `StartDate` and `EndDate`, and `Status`; the existing EF configuration maps it to `leave_requests` and already provides tenant/employee, tenant/status, and tenant/date indexes. No new Time Off or Leave persistence schema was introduced, and persistence naming remains `LeaveRequest`/`leave_requests`.

The Time Off module documentation states that approved Time Off affects availability while attendance reflects actual presence. This task implements the read-side connector required for that relationship without building the Time Off request lifecycle or approval API.

## Files changed

| Layer | Files | Purpose |
|---|---|---|
| Application contract | `src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestReadRepository.cs` | Adds a tenant-scoped batch read for approved leave covering an employee set and inclusive date range. |
| Infrastructure repository | `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestReadRepository.cs` | Implements the approved-leave overlap query with `AsNoTracking`, tenant filtering, employee filtering, and approved-status filtering. |
| Attendance repository | `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs`; `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs` | Adds one batch break read for employee-list and history calculations. |
| Domain status vocabulary | `src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs` | Adds stable internal status constants for normal, leave-aware, non-working-day, and over-break outcomes. |
| Application resolver | `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceDayStatusResolver.cs` | Centralizes status, friendly label, attention type/severity/label, and break-overage resolution without EF Core dependencies. |
| Today state | `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` | Reads approved leave, resolves leave/non-working-day/over-break status, exposes attention fields, and allows configured-policy clock-in on non-working days. |
| Clock-in mutation | `src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommandHandler.cs` | Removes the obsolete `off_day` rejection and persists the actual resolved `ExpectedWorkingDay` value. Leave, holiday, and non-working-day checks do not block clock-in. |
| Today/history contracts | `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs` | Appends status-label, attention, and break-overage fields while retaining existing positional fields and compatibility. |
| History reads | `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` | Applies the shared leave-aware resolver to self and covered history while retaining covered-history authority filtering. |
| Employee list contract | `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs` | Extends the existing optional attendance summary with generic status, attention, and break-overage fields. |
| Employee list projection | `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` | Performs batched attendance, approved-leave, and break reads; calculates summaries for visible employees; orders attention rows before pagination. |
| Dependency injection | `src/ONEVO.Infrastructure/DependencyInjection.cs` | Registers the approved-leave read repository. |
| Unit tests | `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceDayStatusResolverTests.cs`; `AttendanceTodayLeaveAwareTests.cs`; update to `ClockInOutCommandHandlerTests.cs` | Covers leave-aware status outcomes, non-working-day Today behavior, over-break resolution, and allowed non-working-day clock-in. |
| Architecture tests | `tests/ONEVO.Tests.Architecture/AttendanceStatusLeaveAwarenessArchitectureTests.cs` | Guards Application-layer EF independence, no off-day clock-in rejection, repository filtering, and DI registration. |
| Report | `ATTENDANCE_STATUS_LEAVE_AWARENESS_BACKEND_PART1_REPORT.md` | Documents this implementation and verification state. |

## API response contract changes

The existing endpoint remains `GET /api/v1/attendance/time-tracking/today`. No new Time Off API was introduced. The existing response retains all prior fields and appends the following frontend-friendly fields after `Messages`:

```json
{
  "attendanceStatus": "on_time_off",
  "attendanceStatusLabel": "On time off",
  "attentionType": null,
  "attentionLabel": null,
  "attentionSeverity": null,
  "breakOverageMinutes": 0,
  "isOverBreakAllowance": false
}
```

The supported status values include `normal`, `not_clocked_in`, `on_time_off`, `worked_during_time_off`, `non_working_day`, `worked_on_non_working_day`, and `over_break`. Existing statuses such as `active`, `clocked_out`, `no_schedule`, and `policy_not_configured` remain available where their existing conditions apply. Machine-readable codes are paired with user-facing labels; raw technical terms are not used as the only display value.

The employee-list endpoint remains `GET /api/v1/employees`. Its existing nullable `attendanceSummary` remains backward compatible and now may include:

```json
{
  "attendanceSummary": {
    "showNotClockedInWarning": true,
    "shouldHaveClockedIn": true,
    "hasClockedInToday": false,
    "workDate": "2026-08-21",
    "timezone": "Asia/Colombo",
    "scheduledStartTime": "09:00",
    "warningLabel": "Still has not clocked in",
    "attendanceStatus": "not_clocked_in",
    "attendanceStatusLabel": "Not clocked in",
    "attentionType": "not_clocked_in",
    "attentionSeverity": "critical",
    "attentionLabel": "Still has not clocked in",
    "breakUsedMinutes": 0,
    "breakAllowanceMinutes": 30,
    "breakOverageMinutes": 0,
    "isOverBreakAllowance": false
  }
}
```

The generic attention types are `not_clocked_in`, `over_break`, `worked_during_time_off`, and `worked_on_non_working_day`. Attention priority is decided by the backend before `Skip`/`Take`, followed by stable last-name and employee-ID ordering. The employee list continues to use `IEmployeeAuthorityResolver`; no tenant-wide visibility fallback was added.

History rows retain their existing fields and append `StatusLabel`, `AttentionType`, `AttentionLabel`, `AttentionSeverity`, `BreakOverageMinutes`, and `IsOverBreakAllowance`. Covered-history filtering remains authority-resolver driven.

## Approved leave behavior

A leave request is considered relevant only when its tenant and employee match the attendance subject, its status equals `LeaveRequestStatuses.Approved`, and its inclusive date range overlaps the requested work date or date range. Pending, rejected, and cancelled requests do not suppress missing-clock-in attention. The batch repository accepts tenant ID, employee IDs, inclusive dates, and cancellation token and uses `AsNoTracking`.

The implemented examples are as follows:

| Scenario | Backend result | Attention |
|---|---|---|
| Approved leave, no clock-in | `on_time_off` / `On time off` | None; no missing-clock-in warning. |
| Approved leave, clock-in exists | `worked_during_time_off` / `Worked during time off` | `worked_during_time_off`, warning. |
| Non-working day, no clock-in | `non_working_day` / `Non-working day` | None. |
| Non-working day, clock-in exists | `worked_on_non_working_day` / `Worked on non-working day` | `worked_on_non_working_day`, warning. |

No public-holiday source was found or added in this task. Today continues to return `IsHoliday = false` and `HolidayName = null`; holiday awareness remains explicitly deferred until an authoritative source is connected.

## Clock-in and break behavior

Clock-in remains available when the schedule is configured, the effective clock-in policy is configured, and the selected web method is allowed, regardless of whether the resolved day is a working day. The handler no longer rejects `off_day`, and it does not reject approved leave, non-working days, or holidays. The persisted attendance record records the resolved `ExpectedWorkingDay` value instead of always writing `true`. No new leave or non-working-day block was introduced.

Breaks are never auto-stopped. Open-break usage is calculated through the legal-entity-local day window using the current server time, so an allowance of 30 minutes and usage of 45 minutes produces `breakOverageMinutes = 15`, `isOverBreakAllowance = true`, and `over_break` status. The open record remains open until the employee ends it. `StartBreak` remains blocked after the allowance is fully used, while `EndBreak` remains allowance-agnostic and can close an already-open over-limit break.

## Time Off CRUD boundary

This task did not implement Time Off CRUD, leave request creation, leave request update, approval, cancellation, a new Time Off endpoint, a new Time Off table, or frontend code. The existing LeaveRequest table/entity is read only for attendance awareness.

## Tests and verification

The Application project was built successfully with:

```text
dotnet build src\ONEVO.Application\ONEVO.Application.csproj --configuration Release --no-restore
```

The full API build, focused unit tests, architecture tests, and focused integration tests could not complete because the Infrastructure project’s existing AWS package references are unavailable in the current local assets and the environment’s .NET restore fails before package resolution. The exact restore attempts were:

```text
dotnet restore src\ONEVO.Api\ONEVO.Api.csproj
 dotnet restore src\ONEVO.Api\ONEVO.Api.csproj --packages C:\onevoNew\.nuget-cache
```

Both failed with:

```text
C:\Program Files\dotnet\sdk\10.0.300\NuGet.targets(782,5): error Value cannot be null. (Parameter 'path1')
```

Without restore, the API and test builds fail in pre-existing Infrastructure biometric files because `AWSSDK.Extensions.NETCore.Setup` and `AWSSDK.Rekognition` assemblies are not available in the local assets:

```text
error CS0234: The type or namespace name 'Extensions' does not exist in the namespace 'Amazon'
error CS0234: The type or namespace name 'Rekognition' does not exist in the namespace 'Amazon'
```

The focused commands attempted were:

| Check | Result |
|---|---|
| `dotnet build src\ONEVO.Application\ONEVO.Application.csproj --configuration Release --no-restore` | Passed. |
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Blocked by unavailable pre-existing AWS assemblies in Infrastructure. Application and Domain projects compiled successfully. |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter FullyQualifiedName~TimeAttendance` | Blocked by the same Infrastructure AWS assembly errors. |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Blocked by the same Infrastructure AWS assembly errors. |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --configuration Release --no-restore --filter FullyQualifiedName~Attendance` | Blocked by the same Infrastructure AWS assembly errors. Docker itself is available. |
| `git diff --check` | Attempted; Git emitted existing LF-to-CRLF advisory messages before the mounted Windows worktree scan exceeded the command timeout. No whitespace error output was observed. |
| `git status --short --branch` | Branch confirmed as `local/reporting-manager-run`; changes remain unstaged. |

## Remaining risks

The implementation still needs a successful restore and complete build/test run in an environment with valid NuGet path configuration and the AWS package assets. PostgreSQL/Testcontainers verification should then validate EF translation, leave date overlap queries, tenant isolation, batch query performance, exact JSON serialization, and the employee-list ordering-before-pagination behavior.

History rows are still based on attendance records returned by the existing history queries; this change does not synthesize missing attendance rows for every calendar date. Holiday awareness remains unavailable because no authoritative holiday source was found. The employee-list path materializes the complete resolver-authorized filtered employee set before attention ordering and pagination, which is correct for ordering but may need a SQL/denormalized read-model optimization for very large tenants.

The current employee-list status resolver uses the existing legal-entity fallback schedule and does not introduce shift, roster, work-schedule, or public-holiday tables. Partial-day leave semantics remain represented by the existing approved date-range source only; interval-specific attendance exclusion is outside this Part 1 read-model foundation.

## Next recommended task

First restore the backend in a healthy build environment and run the focused unit and architecture suites. Then add PostgreSQL/Testcontainers coverage for the approved-leave batch repository, employee-list attention ordering, tenant isolation, and exact serialized response contracts. After that, the next product task should connect an authoritative holiday source read-only and extend schedule resolution beyond the legal-entity fallback without changing the leave-aware status contract.

## References

[1]: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequest.cs` — Existing tenant-scoped LeaveRequest entity and inclusive date fields.
[2]: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs` — Existing `LeaveRequestStatuses.Approved` constant.
[3]: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs` — Existing `leave_requests` mapping and indexes.
[4]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceDayStatusResolver.cs` — Shared status and attention resolver.
[5]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` — Today response and local-day break calculations.
[6]: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` — Leave-aware self and covered history projection.
[7]: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` — Batched employee-list attention calculation and ordering.
[8]: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs` — Today/history response contract additions.
[9]: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs` — Backward-compatible employee-list summary additions.
[10]: `OneVo-HR/modules/time-attendance/overview.md` — Attendance module and Time Off relationship context.
[11]: `OneVo-HR/modules/time-off/overview.md` — Existing Time Off semantics and persistence boundary.
