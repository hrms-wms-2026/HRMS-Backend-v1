# Backend NuGet Restore Environment Fix Report

## Final statement

> **Restore issue remains blocked because: the connected Windows machine’s .NET SDK 10.0.300/NuGet runtime fails inside `NuGet.Configuration.ConfigurationDefaults` and `NuGet.targets(782,5)` with `Value cannot be null. (Parameter 'path1')`, before package resolution.**

The issue was isolated to the local SDK/NuGet environment. It is not caused by the attendance implementation, an invalid AWS package reference, a repository-local NuGet file, or an invalid package source. Backend Part 1 can currently be verified only partially. A healthy .NET 10 SDK/NuGet installation or CI/another machine is required before full backend verification can continue.

No attendance behavior was changed in this task. No frontend files were touched. No global NuGet configuration was modified. No files were staged, committed, or pushed.

## Repository and Git state at start

Work was restricted to `C:\onevoNew\HRMS-Backend-v1` on branch `local/reporting-manager-run`. The starting status was backend-only:

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
```

Long paths wrapped in the terminal output. No frontend path appeared. The only new file created during this task is this report.

## Environment summary

The required `dotnet --info` command was run. It selected SDK `10.0.300`, commit `caa81fa497`, but exited with a Windows SDK-initialization exception:

```text
System.TypeInitializationException: The type initializer for
'Microsoft.DotNet.Cli.Installer.Windows.InstallerBase' threw an exception.
 ---> System.NullReferenceException: Object reference not set to an instance of an object.
```

The command output also reported:

```text
global.json file: Not found
SDKs installed: 10.0.300, 9.0.314
RID: win-x64
```

The repository targets `net10.0`, so the installed .NET 9 SDK is not a compatible replacement for this repository.

The requested environment-variable inspection returned no variables whose names begin with `NUGET` or `DOTNET`:

```text
Environment variable NUGET  not defined
Environment variable DOTNET  not defined
```

Standard Windows profile variables such as `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `TEMP`, and `TMP` are present and point to `C:\Users\User` locations. Explicitly setting those variables and isolated cache paths did not resolve the error.

## NuGet source and configuration inspection

The requested `dotnet nuget list source --format detailed` command did not reach source enumeration. It failed immediately with:

```text
error: Value cannot be null. (Parameter 'path1')
```

The requested `dotnet nuget locals all --list` command failed with the same error, before reporting cache locations.

The repository root contains none of the following files:

| File | Result |
|---|---|
| `NuGet.config` / `NuGet.Config` | Not present. |
| `global.json` | Not present. |
| `Directory.Packages.props` | Not present. |
| `Directory.Build.props` | Not present. |

The active user-level file at `%AppData%\NuGet\NuGet.Config` contains a single valid source:

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
</packageSources>
```

The Visual Studio fallback configuration contains a valid fallback folder at `C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages`. The Visual Studio offline configuration contains the valid local path `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\`. No empty source name, empty source path, or malformed local path was found in the inspected configuration files.

Generated project metadata from the prior restore state recorded normal paths, including `C:\Users\User\.nuget\packages\`, the Visual Studio fallback folder, and `https://api.nuget.org/v3/index.json`. The API project’s `$(NuGetPackageRoot)` content glob evaluates to a concrete user package path, so it is not the null value causing this failure.

## Restore comparison

Each requested project was restored with `--verbosity detailed`. Every restore failed in the same SDK target before package resolution:

| Project | Result | Error count reported |
|---|---|---:|
| `src\ONEVO.Domain\ONEVO.Domain.csproj` | Failed | 1 |
| `src\ONEVO.Application\ONEVO.Application.csproj` | Failed | 2 |
| `src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj` | Failed | 3 |
| `src\ONEVO.Api\ONEVO.Api.csproj` | Failed | 4 |
| `tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj` | Failed | 5 |

The first meaningful error for each project was:

```text
C:\Program Files\dotnet\sdk\10.0.300\NuGet.targets(782,5): error
Value cannot be null. (Parameter 'path1')
```

The package-free Domain restore reproduces the failure. This rules out AWS packages, the new leave repository, and the attendance changes as the trigger.

A diagnostic API restore identified the exact project-graph location:

```text
"...\\src\\ONEVO.Infrastructure\\ONEVO.Infrastructure.csproj"
  (_GetRestoreSettings target) ->
    NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')

"...\\src\\ONEVO.Application\\ONEVO.Application.csproj"
  (_GetRestoreSettings target) ->
    NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')

"...\\src\\ONEVO.Domain\\ONEVO.Domain.csproj"
  (_GetRestoreSettings target) ->
    NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')

"...\\src\\ONEVO.Api\\ONEVO.Api.csproj"
  (_GetRestoreSettings target) ->
    NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')
```

The failing SDK target is the `GetRestoreSettingsTask` invocation in `NuGet.targets` at line 782. The task receives restore settings from the broken SDK/NuGet runtime and throws from `NuGet.Configuration.ConfigurationDefaults`/path handling. The failure is independent of the package graph.

## Non-destructive isolation attempts

The following diagnostics were run without modifying global configuration or deleting caches:

| Attempt | Result |
|---|---|
| Standard `dotnet restore ... --verbosity minimal` | Failed with `NuGet.targets(782,5)` and `path1`. |
| `--packages C:\onevoNew\.nuget-cache` | Same failure. |
| `-p:NuGetAudit=false` | Same failure. |
| `--force-evaluate` | Same failure. |
| Explicit `NUGET_PACKAGES`, HTTP-cache, and scratch paths | Same failure. |
| Temporary local NuGet.Config containing only `nuget.org` | Same `ConfigurationDefaults` failure. Temporary file was removed. |
| `--source https://api.nuget.org/v3/index.json` | Same failure. This proves source configuration is not the cause. |
| Explicit restore package/repository/config paths | Same failure. |
| Non-static restore graph evaluation | Same failure. |
| Explicit profile variables including `HOME`, `USERPROFILE`, `APPDATA`, and `LOCALAPPDATA` | Same failure after correcting an initial trailing-space environment-value test. |

No cache clear was performed because the failure occurs before NuGet can enumerate or use the cache, and the instructions require approval before destructive cache clearing.

## AWS dependency assessment

`src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj` correctly declares the active production AWS dependencies:

```xml
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="4.0.100.8" />
<PackageReference Include="AWSSDK.Rekognition" Version="4.0.100.8" />
<PackageReference Include="AWSSDK.S3" Version="4.0.101.3" />
<PackageReference Include="AWSSDK.SecurityToken" Version="4.0.100.8" />
```

The referenced package versions are available from the public NuGet flat container. Active production code in `RekognitionFaceLivenessService.cs` and `DependencyInjection.cs` genuinely uses those AWS namespaces and types. Therefore, the earlier AWS assembly errors were downstream symptoms of incomplete/stale restore assets, not incorrect project references. No AWS reference was deleted, downgraded, or excluded.

## Required build and test verification

Because restore could not regenerate assets, the required no-restore builds fail with `NETSDK1004` missing-assets errors:

| Command | Result |
|---|---|
| `dotnet build src\ONEVO.Domain\ONEVO.Domain.csproj --configuration Release --no-restore` | Failed: `src\ONEVO.Domain\obj\project.assets.json` not found. |
| `dotnet build src\ONEVO.Application\ONEVO.Application.csproj --configuration Release --no-restore` | Failed: `src\ONEVO.Application\obj\project.assets.json` not found. |
| `dotnet build src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --configuration Release --no-restore` | Failed: `src\ONEVO.Infrastructure\obj\project.assets.json` not found. |
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore` | Failed: `src\ONEVO.Api\obj\project.assets.json` not found. |
| `dotnet build tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --configuration Release --no-restore` | Failed because referenced project assets are missing. |
| `dotnet build tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --configuration Release --no-restore` | Failed because referenced project assets are missing. |
| Focused unit tests with the requested TimeAttendance/Attendance/EmployeeList/Leave OR filter | Failed during build because referenced assets are missing. |
| Architecture tests | Failed during build because referenced assets are missing. |
| `git diff --check` | Attempted; emitted LF-to-CRLF advisories and exceeded the mounted-worktree timeout. No whitespace-error lines were observed. |

Docker is available on the connected device. Integration tests remain unexecutable because the test project cannot build until restore succeeds. The prior integration attempt reached the Infrastructure AWS assembly errors before the clean restore diagnosis; the current clean state fails earlier with missing assets.

## Files changed during this task

No source, project, attendance, frontend, global NuGet, or repository configuration files were changed during this environment-fix task. Temporary diagnostic files and temporary package/cache directories were removed. The only intentional new repository file is:

```text
BACKEND_NUGET_RESTORE_ENVIRONMENT_FIX_REPORT.md
```

## Partial verification and next step

The attendance implementation can be reviewed structurally: the Application layer remains free of EF Core dependencies; the leave repository is tenant- and employee-scoped with approved-status and inclusive date-overlap filters; the API route remains unchanged; covered-history authorization remains authority-resolver driven; and no attendance behavior was modified here.

The next required step is to use a healthy .NET 10 SDK/NuGet installation or CI/another Windows machine. Verify that `dotnet --info`, `dotnet nuget list source --format detailed`, and `dotnet nuget locals all --list` run successfully there. Then rerun restore and the complete build/test matrix from the instructions. Do not clear the user’s global cache or edit global NuGet configuration without explicit approval.

## References

[1]: `src/ONEVO.Api/ONEVO.Api.csproj` — API project and project references.
[2]: `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj` — AWS and Infrastructure package references.
[3]: `src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceLivenessService.cs` — Active AWS Rekognition/SecurityToken production usage.
[4]: `%AppData%\NuGet\NuGet.Config` — Active user-level NuGet source configuration.
[5]: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceTodayStateService.cs` — Existing attendance Today behavior inspected without modification.
[6]: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` — Existing history behavior inspected without modification.
[7]: `ATTENDANCE_STATUS_LEAVE_AWARENESS_BACKEND_PART1_VERIFICATION_REPORT.md` — Previous Backend Part 1 verification findings.


## Follow-up code correction and verification

After the environment investigation, the connected repository exposed a genuine source compile error in `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestReadRepository.cs`: `DbSet<LeaveRequest>` could not resolve `AsNoTracking`. The minimal correction was adding:

```csharp
using Microsoft.EntityFrameworkCore;
```

No attendance behavior was changed. A related test-only compile error was also found in `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceTodayLeaveAwareTests.cs`; it lacked the namespace import for `IWorkModeRepository`. The test now imports `ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces`.

The API build now succeeds:

```text
dotnet build src\ONEVO.Api\ONEVO.Api.csproj --configuration Release --no-restore
Build succeeded in 1.5s
```

The architecture suite initially failed at runtime because the generated test output did not contain the existing AWS runtime assemblies used by Infrastructure reflection/model initialization. The assemblies were copied from the already-built API output into generated test output folders only; no project or production dependency was changed. The architecture suite then succeeded:

```text
ONEVO.Tests.Architecture test net10.0 succeeded (13.6s)
Test summary: successful
```

The focused attendance/employee-list/leave unit-test filter then succeeded after the generated-output dependency correction. The earlier run had compiled the test project successfully but reported six runtime failures caused by the missing `AWSSDK.Rekognition` assembly; the rerun completed successfully. A non-destructive targeted `git diff --check` on the corrected repository/test files completed with no whitespace errors.

The final verification position is therefore:

| Check | Result |
|---|---|
| EF Core import correction | Applied. |
| API Release build with `--no-restore` | Passed. |
| Architecture tests | Passed after generated test-output dependency correction. |
| Focused attendance/employee-list/leave unit tests | Passed after generated test-output dependency correction. |
| Targeted whitespace check | Passed. |
| Frontend changes | None. |
| Commit/push | None. |

The original clean-machine restore defect remains documented above and still requires a healthy SDK/NuGet environment for reproducible fresh restores. The current source compile blocker reported by the user is resolved.
