# Time Tracking Backend Part 5 — Employee List Not-Clocked-In Warning

## Status

Backend support for the People → Employees not-clocked-in warning is implemented in `C:\onevoNew\HRMS-Backend-v1`. The existing `GET /api/v1/employees` pipeline now optionally returns a server-computed attendance summary and places eligible employees who should have clocked in but have not done so at the beginning of the same paginated result.

The implementation is **backend-only**. No frontend files were changed, no new employee-warning endpoint was created, no files were staged, no commit was created, and nothing was pushed. The work was performed on the pre-existing branch `local/reporting-manager-run`, which already contained unrelated uncommitted Time Tracking Parts 2–4 and employee-authority changes.

## Files changed for Part 5

| Layer | Files changed or added | Purpose |
|---|---|---|
| Application response | `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs` | Added the nullable `EmployeeListAttendanceSummaryResponse` nested contract at the end of `EmployeeListItemResponse`, preserving existing constructor compatibility. |
| Application query flow | `src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs` | Preserved `IEmployeeAuthorityResolver` visibility resolution, gated attendance data on `attendance:read`, passed one server timestamp into the batch repository read, and forced pending invitation rows to have no attendance summary. |
| Application repository contract | `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeRepository.cs` | Added the optional `EmployeeListAttendanceOptions` batch-read option. A null option means attendance-sensitive data is not projected. |
| Application attendance logic | `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceScheduleResolver.cs`; `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` | Added a persistence-ignorant shared legal-entity schedule/timezone evaluator and reused it from the existing Today-state service so working-day and scheduled-start semantics do not diverge. |
| Infrastructure repository | `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` | Added a batch attendance read scoped to the filtered, resolver-authorized employee IDs and tenant, computed summaries in memory over the complete filtered result, ordered warnings before the existing last-name/employee-ID order, and paginated afterward. All reads use `AsNoTracking`. |
| Unit tests | `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeRepositoryTests.cs`; `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerTests.cs`; `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ListEmployeesQueryHandlerAuthorityResolverIntegrationTests.cs`; `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityTestGraph.cs` | Added warning, actual-clock-in, before-start, non-working-day, missing-schedule, ordering-before-pagination, permission, pending-invite, and constructor/test-double coverage while preserving authority-resolver coverage. |
| Architecture tests | `tests/ONEVO.Tests.Architecture/EmployeeListAttendanceWarningArchitectureTests.cs`; `tests/ONEVO.Tests.Architecture/ListEmployeesAuthorityResolverArchitectureTests.cs` | Guarded against per-employee Today-state calls, direct EF use in Application code, missing `AsNoTracking` batch reads, separate warning routes, and `org:manage` visibility bypasses. The existing authority guard was updated to allow only the required attendance permission check. |
| Existing integration fixture | `tests/ONEVO.Tests.Integration/CoreHr/Employee/EmployeesListIntegrationTests.cs` | Updated the handler construction to provide the existing deterministic date-time provider. No new Docker-dependent test was added because Docker was unavailable in this environment. |

The following pre-existing files were not part of the Part 5 implementation and were preserved: earlier Part 2–4 reports, attendance API/controller files, attendance migrations and model snapshot changes, and other unrelated dirty-worktree files shown by Git before this task.

## Exact response contract

The existing endpoint remains:

```text
GET /api/v1/employees
```

`EmployeeListItemResponse` now has the following optional final field:

```csharp
public sealed record EmployeeListAttendanceSummaryResponse(
    bool ShowNotClockedInWarning,
    bool ShouldHaveClockedIn,
    bool HasClockedInToday,
    DateOnly WorkDate,
    string Timezone,
    string? ScheduledStartTime,
    string? WarningLabel);
```

The field is appended to the existing response record as:

```csharp
EmployeeListAttendanceSummaryResponse? AttendanceSummary = null
```

For a visible active employee who should have clocked in but has no actual clock-in, the response shape is:

```json
{
  "attendanceSummary": {
    "showNotClockedInWarning": true,
    "shouldHaveClockedIn": true,
    "hasClockedInToday": false,
    "workDate": "2026-08-21",
    "timezone": "Asia/Colombo",
    "scheduledStartTime": "09:00",
    "warningLabel": "Still has not clocked in"
  }
}
```

For a visible employee with attendance visibility but no warning, the summary remains available with `showNotClockedInWarning: false`, `shouldHaveClockedIn: false`, the computed `workDate` and legal-entity `timezone`, and a truthful `hasClockedInToday` value. `scheduledStartTime` and `warningLabel` are null when the warning is not shown. If the viewer lacks `attendance:read`, `attendanceSummary` is always null. Pending invited rows also always return `attendanceSummary: null`, including when the viewer has `attendance:read`. An employee without a legal entity returns null because there is no legal-entity attendance context from which to calculate a safe summary.

## Warning rule

The handler continues to obtain the authoritative visible employee ID set exclusively from `IEmployeeAuthorityResolver` using `employees:read`, `IncludeSelf: true`, and `EmployeeAuthorityPurpose.EmployeeListRead`. The repository receives that set through `EmployeeListFilter.RestrictToEmployeeIds`; no attendance query can expand beyond it.

For each returned employee with attendance visibility, `ShowNotClockedInWarning` and `ShouldHaveClockedIn` are true only when all of the following conditions hold:

| Condition | Implementation behavior |
|---|---|
| Employee is visible to the requester | Enforced before the repository read by the existing authority resolver and again by `RestrictToEmployeeIds`. |
| Employee is active | The joined employment-status code must be `active`. |
| Employee has a legal entity | Employees without a legal entity receive no attendance summary. |
| Legal-entity schedule is configured | A resolvable timezone, non-null work start, non-null work end, and same-day `WorkStartTime < WorkEndTime` are required. |
| Local date is a configured working day | `StandardWorkingDays` is interpreted as ISO weekdays Monday `1` through Sunday `7`, matching the existing Today-state behavior. |
| Local time is at or after scheduled start | The server timestamp is converted into the legal entity timezone before comparison. |
| Employee has not clocked in | The batch read treats both no attendance row and an existing row with `ActualStart == null` as not clocked in. |

An actual `AttendanceRecord.ActualStart` makes `HasClockedInToday` true and suppresses the warning. Before scheduled start, on a non-working day, with missing or invalid schedule configuration, or for inactive employees, the warning is false. Attendance records are read only; they are never mutated or created for display.

## Permission decision

Attendance status is treated as attendance-sensitive data. The employee list handler checks `currentUser.HasPermission("attendance:read")` independently from the existing `employees:read` resolver path. When the attendance permission is absent, the handler calls the normal employee-list repository path without attendance options and applies a defense-in-depth projection that removes any summary from returned rows. `org:manage` and `org:read` are not used as attendance or visibility bypasses.

When `attendance:read` is present, the same resolver-returned employee IDs and existing legal-entity filter are still authoritative. The implementation does not broaden the employee list to a tenant-wide or cross-company set.

## Legal-entity timezone and work-date calculation

The new `AttendanceScheduleResolver` is in the Application layer and has no EF Core or persistence dependency. It receives the server-side `IDateTimeProvider.UtcNow`, selects the legal entity’s configured timezone, converts the timestamp with `TimeZoneInfo`, and derives `DateOnly WorkDate` from the resulting local date. It then evaluates the local weekday and local time against `LegalEntity.StandardWorkingDays`, `WorkStartTime`, and `WorkEndTime`.

The implementation does not use browser time, machine local time, a direct UTC date, or a hardcoded `Asia/Colombo` value. A missing timezone or an unresolvable timezone causes the schedule to fail closed for warning purposes. Malformed or empty working-day JSON retains the existing Today-state fallback to Monday through Friday.

The attendance batch query loads records for the resolver-authorized employee IDs and a date range covering the local work dates calculated for those employees. Each employee is then matched against the attendance row for that employee’s own legal-entity-local work date. The query is tenant-scoped and does not use a per-employee repository call.

## Ordering before pagination

When attendance visibility is enabled, the repository first counts the complete filtered employee query, then materializes the complete filtered employee rows, performs one batch attendance query, calculates each summary, and applies this in-memory order:

```text
ShowNotClockedInWarning descending
LastName ascending
Employee Id ascending
```

Only after that ordering does it apply `Skip` and `Take`. Therefore a warning employee on a later normal-order position can move onto an earlier page, while `TotalCount` remains the count of the complete filtered employee query. When attendance visibility is absent, the existing SQL last-name/employee-ID ordering and pagination path remains unchanged.

The implementation intentionally chooses a correct batch-and-memory ordering strategy rather than calculating warnings after the existing page has already been selected. The trade-off is that attendance-authorized list requests materialize the complete filtered employee projection before pagination; the report does not claim that this path is fully SQL-sorted.

## Pending invited rows

`ListEmployeesQueryHandler` continues to append pending invitations from `ListInvitedPendingByInviterAsync` after the resolver-authorized employee page is obtained. These rows do not have evidence of an active employment attendance context, so the handler explicitly sets `AttendanceSummary` to null for every appended pending row. Existing legal-entity filtering and de-duplication behavior is unchanged.

## Tests added or updated

The focused unit coverage now verifies that an active employee with no attendance row after the local scheduled start receives a warning; an employee with `ActualStart` does not; an employee before local start does not; an employee on Sunday with Monday–Friday configuration does not; missing schedule configuration does not warn; and warning employees sort before normal employees before page slicing. It also verifies legal-entity local work dates, summary fields, tenant-scoped attendance reads, and the absence of attendance state without `attendance:read`.

Handler tests continue to verify resolver-only visibility, no unrestricted-scope fallback, no `org:manage` bypass, pending invitation merging and cross-legal-entity exclusion. New tests verify that attendance-authorized pending invitees remain summary-free. Architecture tests verify that the handler does not invoke `GetTodayAsync` per employee, the Application layer has no EF Core dependency, the repository uses a batch `AsNoTracking` attendance query, and the existing employee list controller remains the only API surface for this feature.

## Verification commands and results

| Command or check | Result |
|---|---|
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release --no-restore` | **Passed** — 0 errors and 0 warnings in the final run. |
| `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore` | **Passed** — 0 errors; only pre-existing warnings in unrelated test files and the existing SQLite package advisory were reported. |
| `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ListEmployees|FullyQualifiedName~EmployeeList|FullyQualifiedName~Attendance"` | **Passed** — 99 passed, 0 failed, 0 skipped. |
| `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | **Passed** — 647 passed, 0 failed, 0 skipped. |
| `dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api` | The exact command exited 1 with the generic `Build failed. Use dotnet build to see the errors.` message. The API Release build itself passed. |
| `dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release --no-build` with a process-local design-time `ConnectionStrings__MigrationConnection` | **Passed** — `No changes have been made to the model since the last migration.` Runtime configuration was not modified. |
| `git diff --check` | **Passed** — no whitespace errors or conflict markers. Git emitted only existing LF-to-CRLF advisory messages for the dirty Windows worktree. |
| `docker version` | **Skipped integration prerequisite** — Docker CLI was installed, but the Docker Desktop Linux engine was unavailable: `failed to connect to the docker API ... dockerDesktopLinuxEngine ... The system cannot find the file specified.` |

## Skipped checks and remaining risks

PostgreSQL/Testcontainers integration tests were not run because the Docker daemon was unavailable. Consequently, this task does not claim live SQL translation, PostgreSQL query-plan performance, RLS behavior, exact HTTP JSON serialization, or live endpoint verification. No browser verification was performed, and the frontend is not claimed to be implemented.

The attendance-authorized path currently materializes the complete filtered employee projection before in-memory warning ordering. This guarantees correct ordering-before-pagination but may require a later SQL projection or denormalized read model if very large employee lists make the full filtered materialization too expensive. The batch attendance query uses a date range spanning the local dates represented by visible legal entities; it remains bounded by the resolver-authorized employee IDs and tenant predicate.

The existing product model retains same-day legal-entity work-hour semantics, and holiday/time-off overrides remain outside this feature. Invalid timezone identifiers fail closed for warnings but still use the established UTC fallback for deriving a safe context date. A configured PostgreSQL environment should still verify the batch query against the real provider, tenant RLS, and production data volumes before release.

## Final repository state

The repository remains on `local/reporting-manager-run`. The worktree was already dirty before this task; those pre-existing changes were preserved. No frontend paths were changed by this implementation. No files were staged, committed, or pushed.

## References

[1]: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs` — Employee list response and attendance summary contract.
[2]: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs` — Existing visibility resolver flow, attendance permission gate, and pending invitation merge.
[3]: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` — Tenant-scoped employee projection, batch attendance read, warning calculation, and ordering-before-pagination.
[4]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceScheduleResolver.cs` — Shared legal-entity timezone, working-day, and scheduled-start evaluator.
[5]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` — Existing Today-state service now reusing the shared evaluator.
[6]: `TIME_TRACKING_BACKEND_PART2_READ_MODEL_REPORT.md` — Prior read-model timezone and `shouldHaveClockedIn` decisions.
[7]: `TIME_TRACKING_BACKEND_PART3_CLOCK_IN_OUT_REPORT.md` — Prior shared Today-state and self-service clock-in/out behavior.
[8]: `TIME_TRACKING_BACKEND_PART4_BREAK_ACTIONS_REPORT.md` — Prior break-action and local-day attendance behavior.
