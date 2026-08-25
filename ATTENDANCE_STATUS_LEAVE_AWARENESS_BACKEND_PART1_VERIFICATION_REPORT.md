# Backend Part 1 Verification Report

## Final status

> **Backend Part 1 is still blocked because: the attached Windows environment cannot complete NuGet restore under .NET SDK 10.0.300 due to `NuGet.Configuration.ConfigurationDefaults` failing with `Value cannot be null. (Parameter 'path1')`. Consequently, the complete Domain → Application → Infrastructure → API → test build surface cannot currently be regenerated from a clean `obj` state.**

The attendance and approved-leave implementation was inspected directly. No frontend files were changed. No source correction was required during this verification pass because the blocker occurs before package resolution and affects even the package-free Domain project. No files were staged, committed, or pushed.

## Scope and starting state

All work was performed under `C:\onevoNew\HRMS-Backend-v1`. The starting repository state was:

```text
## local/reporting-manager-run
 M src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs
 M src/ONEVO.Application/Features/TimeAttendance/Commands/ClockIn/ClockInCommandHandler.cs
 M src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs
 M src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs
 M src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs
 M src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs
 M src/ONEVO.Domain/Features/TimeAttendance/Entities/AttendanceRecord.cs
 M src/ONEVO.Infrastructure/DependencyInjection.cs
 M src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs
?? src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestReadRepository.cs
?? src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceDayStatusResolver.cs
?? src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestReadRepository.cs
?? tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceDayStatusResolverTests.cs
?? tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceTodayLeaveAwareTests.cs
?? tests/ONEVO.Tests.Architecture/AttendanceStatusLeaveAwarenessArchitectureTests.cs
?? ATTENDANCE_STATUS_LEAVE_AWARENESS_BACKEND_PART1_REPORT.md
```

The terminal output wrapped several long paths, but the status contained only backend source, tests, and reports. There were no frontend paths.

## Direct code inspection

The implementation was inspected rather than relying on the earlier report. The existing controller remains `[Route("api/v1/attendance/time-tracking")]`, and `GET /api/v1/attendance/time-tracking/today` still dispatches through MediatR to `GetAttendanceTodayQuery`; no new Time Off API or controller was added.

`AttendanceTodayStateService` reads approved leave through the optional `ILeaveRequestReadRepository`, calculates break usage in the legal-entity-local day window, and delegates status and attention decisions to `AttendanceDayStatusResolver`. The service does not reject approved leave or non-working days. `ClockInCommandHandler` no longer contains the obsolete `off_day` rejection and persists `ExpectedWorkingDay = context.Schedule.IsWorkingDay`.

`AttendanceReadHandler` applies the same resolver to self-history and covered-history. Covered history still checks `attendance:read` and filters employee IDs through `IEmployeeAuthorityResolver` before querying records. The employee-list repository performs batched attendance, approved-leave, and break reads and orders attention rows before pagination. It accepts the repository interfaces in production DI while retaining a fallback for existing direct-construction tests.

The Application layer contains no `Microsoft.EntityFrameworkCore` reference in the inspected attendance code. The new leave repository uses `AsNoTracking`, tenant filtering, employee filtering, `LeaveRequestStatuses.Approved`, and inclusive overlap logic:

```csharp
request.StartDate <= to && request.EndDate >= from
```

The API response records append fields rather than changing the existing positional prefix. Today/history and employee-list responses include machine-readable status/attention fields plus friendly labels and break-overage fields.

## Restore diagnosis

The mandated restore command was run:

```text
dotnet restore src/ONEVO.Api/ONEVO.Api.csproj --verbosity minimal
```

It failed four times with:

```text
C:\Program Files\dotnet\sdk\10.0.300\NuGet.targets(782,5): error Value cannot be null. (Parameter 'path1')
Restore failed with 4 error(s) in 1.5s
```

A diagnostic restore log was captured. Its final MSBuild summary identified the failure in the SDK’s `_GetRestoreSettings` task for all four projects in the API graph:

```text
NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')
[...\src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj]
NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')
[...\src\ONEVO.Application\ONEVO.Application.csproj]
NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')
[...\src\ONEVO.Domain\ONEVO.Domain.csproj]
NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')
[...\src\ONEVO.Api\ONEVO.Api.csproj]
```

The failure occurs before package resolution. A separate restore of the package-free Domain project reproduced the same `path1` exception, proving that the attendance code and AWS references are not the trigger.

The environment reports .NET SDK `10.0.300` and also has SDK `9.0.314` installed. `dotnet --info` itself fails during the SDK’s Windows installer initialization with a `System.TypeInitializationException` and inner `NullReferenceException` in `Microsoft.DotNet.Cli.Installer.Windows.InstallerBase`. The restore diagnostic also reports missing workload locator directories, although the workload resolver later finds the Android workload manifest.

The repository configuration was checked before any correction. There is no `global.json`, root `NuGet.Config`, `Directory.Build.props`, or `Directory.Packages.props` in the backend root. The active user NuGet configuration contains only the standard `nuget.org` source; generated restore metadata points to `C:\Users\User\.nuget\packages\`, the Visual Studio fallback folder, and `https://api.nuget.org/v3/index.json`. The API project’s `$(NuGetPackageRoot)` content-removal glob evaluates to `C:\Users\User\.nuget\packages\` and is not null, so it is not the root cause.

The following restore variations were attempted without changing production project references:

| Variation | Result |
|---|---|
| Standard restore | Same `NuGet.targets(782,5)` `path1` failure. |
| `--packages C:\onevoNew\.nuget-cache` | Same failure. |
| `-p:NuGetAudit=false` | Same failure. |
| `--force-evaluate` | Same failure. |
| Explicit `NUGET_PACKAGES`, HTTP cache, and scratch paths | Same failure. |
| Temporary minimal NuGet configuration containing only `nuget.org` | Same `ConfigurationDefaults` `path1` failure. |
| Explicit package, repository, config-directory, and config-file paths | Same `ConfigurationDefaults` `path1` failure. |
| Non-static restore graph evaluation | Same failure. |
| Explicit `RestoreRootConfigDirectory` alone | Did not produce a usable assets graph; subsequent clean verification confirmed assets were not generated. |

These results identify the blocker as a machine/SDK/NuGet environment problem, not an incorrect repository package source or attendance implementation problem. No package references were deleted, downgraded, or hidden.

## AWS assembly root cause

`ONEVO.Infrastructure.csproj` correctly declares active production dependencies:

```xml
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="4.0.100.8" />
<PackageReference Include="AWSSDK.Rekognition" Version="4.0.100.8" />
<PackageReference Include="AWSSDK.S3" Version="4.0.101.3" />
<PackageReference Include="AWSSDK.SecurityToken" Version="4.0.100.8" />
```

The AWS namespaces are used by active production code in `RekognitionFaceLivenessService.cs` and the Infrastructure DI bootstrap. The referenced AWS package versions are available from NuGet, but the local restore assets do not contain the Rekognition, SecurityToken, or Extensions packages. The older generated assets seen before the clean diagnostic contained only `AWSSDK.Core` and `AWSSDK.S3`; they were stale/incomplete and did not represent a successful current restore.

Therefore, the AWS errors are a downstream symptom of the failed restore, not a reason to remove or exclude biometric production code. A correct environment fix is to repair or replace the .NET/NuGet installation/profile so restore completes and regenerates assets. No production dependency workaround was applied.

## Build verification

The required layer-by-layer commands were executed after the restore attempts:

| Command | Result | Exact blocker |
|---|---|---|
| `dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj --configuration Release --no-restore` | Failed | `NETSDK1004`: `src\ONEVO.Domain\obj\project.assets.json` not found after clean restore could not regenerate assets. |
| `dotnet build src/ONEVO.Application/ONEVO.Application.csproj --configuration Release --no-restore` | Failed | `NETSDK1004`: Application assets file not found. |
| `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --configuration Release --no-restore` | Failed | `NETSDK1004`: Infrastructure assets file not found. |
| `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --configuration Release --no-restore` | Failed | `NETSDK1004`: API assets file not found. |
| `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore` | Failed | Referenced project assets files not found. |
| `dotnet build tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Failed | Referenced project assets files not found. |

The prior task had successfully compiled the Application project before this verification pass. During diagnosis, only untracked generated `obj` directories were cleared to ensure that no stale assets could mask the restore failure. These generated artifacts are not tracked source files and will be recreated by a successful restore.

## Focused test verification

The requested focused unit-test command was attempted. The first shell-safe retry used the equivalent attendance filter because the pipe-separated filter was interpreted by the Windows command wrapper:

```text
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --configuration Release --no-restore --filter FullyQualifiedName~TimeAttendance
```

It failed during project build with the same `NETSDK1004` missing-assets errors. The architecture test suite was also attempted:

```text
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --configuration Release --no-restore
```

It failed for the same missing project assets. Tests were not weakened to obtain a pass.

Docker/Testcontainers is available on the connected device, but integration tests cannot start because the integration project cannot build until restore regenerates the project assets. The focused integration command was attempted previously and failed at the Infrastructure AWS assembly stage; after the clean diagnostic, the current no-restore surface fails earlier at missing assets.

`git diff --check` was attempted. Git emitted only LF-to-CRLF advisory messages for modified Windows-worktree files before the mounted worktree scan exceeded the command timeout. No whitespace error lines were observed. The command could not be allowed to finish in the mounted-worktree environment.

## Business-rule verification from code and tests

| Rule | Verification outcome |
|---|---|
| Approved leave suppresses missing-clock-in warning | Implemented in the shared resolver and Today/employee-list reads; direct Today tests cover the no-clock-in case. |
| Pending/rejected/cancelled leave does not suppress warning | Repository query restricts results to `LeaveRequestStatuses.Approved`; pending/rejected/cancelled requests are not returned by the connector. Requires a successful test run for runtime confirmation. |
| Other employee/tenant leave does not affect current employee | Batch query filters both `TenantId` and employee IDs; requires successful EF test execution for runtime confirmation. |
| Inclusive leave date overlap | Query uses `StartDate <= to` and `EndDate >= from`; requires successful EF test execution for provider translation confirmation. |
| Clock-in on approved leave/non-working day | Clock-in handler contains no leave/off-day rejection and persists the resolved working-day flag. Existing test was updated for allowed non-working-day clock-in. |
| Worked during time off is marked, not blocked | Resolver returns `worked_during_time_off` with warning attention when a record exists on approved leave. |
| Break is not auto-ended | Start/end command handlers remain unchanged in the correction pass; the resolver reads actual/open records and does not mutate them. |
| Open/completed break overage reports | Today/history and employee-list projections calculate usage and expose overage fields. |
| End Break remains available after overage | Existing end-break guard remains based on an open break, not allowance. |
| Start Break blocks after allowance | Existing Today action guard uses remaining allowance greater than zero. |
| Employee-list backend owns attention and ordering | Batched repository projection computes summaries and orders attention rows before `Skip`/`Take`. |
| Visibility scope still applies | Covered history and employee-list inputs remain authority-resolver scoped; no tenant-wide fallback was added. |
| History includes leave-aware/over-break fields | Shared resolver is applied to self and covered history rows. |

## Files changed during this correction pass

No tracked source or project files were changed during this verification pass. Temporary diagnostic files and generated `obj` outputs were removed after use. The earlier Backend Part 1 source and test changes remain unstaged and backend-only. A new report file was created:

```text
ATTENDANCE_STATUS_LEAVE_AWARENESS_BACKEND_PART1_VERIFICATION_REPORT.md
```

## Remaining risks and frontend readiness

The backend contract is structurally defined, but it is **not yet safe to declare verified for frontend contract work** because the complete build and test surface cannot currently run. In particular, EF query translation, tenant isolation at the provider level, exact JSON serialization, DI construction across API startup, employee-list ordering before pagination, and integration behavior remain unexecuted in this environment.

The specific environment repair needed is a functioning .NET 10 SDK/NuGet installation or a compatible clean build machine. After repair, run restore first, then the six layer builds, the focused unit and architecture tests, `git diff --check`, and the Docker-backed integration filter. If the regenerated assets then expose real compiler errors, correct those source or project issues rather than removing production dependencies.

## References

[1]: `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs` — Existing attendance API route and command/query surface.
[2]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` — Today attendance state, approved leave read, action gating, and local-day break usage.
[3]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceDayStatusResolver.cs` — Shared status and attention resolution.
[4]: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` — Self and covered history reads with authority filtering.
[5]: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestReadRepository.cs` — Approved-leave batch query.
[6]: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` — Batched employee-list status and attention projection.
[7]: `src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceLivenessService.cs` — Active AWS Rekognition and SecurityToken production integration.
[8]: `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj` — AWS package references.
[9]: `ATTENDANCE_STATUS_LEAVE_AWARENESS_BACKEND_PART1_REPORT.md` — Earlier implementation report.
