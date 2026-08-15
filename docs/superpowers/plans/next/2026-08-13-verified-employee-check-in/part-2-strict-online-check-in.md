# Verified Employee Check-In — Strict Online Check-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows CLOCK IN button create exactly one backend-verified attendance event using JWT-bound employee/device identity, fresh GPS, AWS liveness, and enrollment-reference face matching before local monitoring starts.

**Architecture:** The Tray captures fresh location and hosts AWS capture UI; the Service generates the attendance-session ID, owns the device JWT, coordinates backend/Tray messages, and transitions lifecycle only after an allowed backend verdict. The backend resolves CoreHR employee identity, evaluates AWS results, compares faces, and idempotently persists check-in.

**Tech Stack:** Part 1 foundation, .NET 10, MediatR, EF Core/PostgreSQL RLS, AWS Rekognition Mumbai, MAUI Windows, WebView2, typed named-pipe IPC, xUnit.

**Spec:** `C:\HR\HRMS-Backend-v1\docs\superpowers\specs\next\2026-08-13-verified-employee-check-in-design.md`

## Global Constraints

- Part 1 compatibility/enrollment milestone must pass before this plan starts.
- `AttendanceSessionId` is generated once by Service and reused across retries, check-in, PresenceSession, and final work-session upload.
- Backend trusts tenant/user/device only from `TrayDevicePolicy`; request bodies contain no employee/device identity.
- CLOCK IN always captures a new GPS reading; onboarding Preferences are display/default context only.
- Monitoring remains `Stopped` during location, capture, and verification.
- AWS session retry creates a new attempt/session but retains `AttendanceSessionId`.
- Face liveness below threshold or face similarity below threshold is rejected; it never enters fallback in this plan.
- No face bytes in IPC, Service SQLite, Preferences, or logs.

---

### Task 6: Add attendance correlation and idempotent persistence

**Files:**
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\CheckIn\Entities\EmployeeCheckIn.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\CheckIn\EmployeeCheckInConfiguration.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\WorkSessions\Entities\EmployeeWorkSession.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\WorkSessions\EmployeeWorkSessionConfiguration.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\WorkSessions\RepositoryInterfaces\IWorkSessionRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\WorkSessions\EfWorkSessionRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\WorkSessions\Commands\SubmitWorkSession\SubmitWorkSessionCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\CheckIn\AttendanceCorrelationIntegrationTests.cs`

**Interfaces:**
- Produces: unique `(TenantId, AttendanceSessionId)` on both check-ins and work sessions.
- Preserves: existing `SubmitWorkSessionCommand.SessionId` wire name; handler maps it to `AttendanceSessionId`.

- [ ] **Step 1: Write failing correlation/idempotency tests**

```csharp
[Fact]
public async Task SameAttendanceSessionId_CannotCreateTwoCheckInsForTenant()
{
    await InsertCheckInAsync(TenantId, AttendanceSessionId);
    Func<Task> duplicate = () => InsertCheckInAsync(TenantId, AttendanceSessionId);
    await duplicate.Should().ThrowAsync<DbUpdateException>();
}

[Fact]
public async Task WorkSessionRetry_ReturnsRowFoundByAttendanceSessionId()
{
    var first = await SubmitWorkSessionAsync(AttendanceSessionId);
    var retry = await SubmitWorkSessionAsync(AttendanceSessionId);

    retry.Id.Should().Be(first.Id);
    (await CountWorkSessionsAsync(TenantId, AttendanceSessionId)).Should().Be(1);
}
```

Also prove two tenants may reuse the same GUID and that `EmployeeId` is required for new check-ins.

- [ ] **Step 2: Run the focused tests**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~AttendanceCorrelationIntegrationTests
```

Expected: compilation failure because the new properties do not exist.

- [ ] **Step 3: Add exact check-in fields**

```csharp
public Guid EmployeeId { get; set; }
public Guid AttendanceSessionId { get; set; }
public Guid BiometricAttemptId { get; set; }
public string VerificationStatus { get; set; } = "Verified";
public string? WorkLocationCode { get; set; }
public DateTimeOffset LocationCapturedAt { get; set; }
public string? FallbackReason { get; set; }
```

Add required foreign keys where the current model supports them, max lengths, and the tenant-scoped unique index. Preserve the legacy nullable `FaceScanId` without using it in verified flow.

- [ ] **Step 4: Separate work-session primary key from attendance correlation**

Add `AttendanceSessionId` to `EmployeeWorkSession`. Change repository lookup to:

```csharp
Task<EmployeeWorkSession?> FindByAttendanceSessionIdAsync(Guid attendanceSessionId, Guid tenantId, CancellationToken ct);
```

For new rows set `Id = Guid.NewGuid()` and `AttendanceSessionId = request.SessionId`. Migration backfills existing rows with `attendance_session_id = id`, makes it non-null, and adds the unique tenant index.

- [ ] **Step 5: Generate/review migration, run tests, and commit**

```powershell
dotnet ef migrations add AddVerifiedCheckInCorrelation --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~AttendanceCorrelationIntegrationTests
git add src/ONEVO.Domain/Features/Monitoring/CheckIn src/ONEVO.Domain/Features/Monitoring/WorkSessions src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/WorkSessions src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/WorkSessions src/ONEVO.Application/Features/Monitoring/WorkSessions src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Integration/Monitoring/CheckIn
git commit -m "feat(monitoring): correlate check-ins and work sessions"
```

---

### Task 7: Implement strict check-in attempt APIs and backend verdict

**Files:**
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\RepositoryInterfaces\IBiometricRepository.cs`
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\Biometrics\EfBiometricRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\RepositoryInterfaces\ICheckInRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\CheckIn\EfCheckInRepository.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateCheckInAttempt\CreateCheckInAttemptCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateCheckInAttempt\CreateCheckInAttemptCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateCheckInAttempt\CreateCheckInAttemptCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteCheckInAttempt\CompleteCheckInAttemptCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteCheckInAttempt\CompleteCheckInAttemptCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteCheckInAttempt\CompleteCheckInAttemptCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Queries\GetCheckInAttempt\GetCheckInAttemptQuery.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Queries\GetCheckInAttempt\GetCheckInAttemptQueryHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\Biometrics\BiometricCheckInController.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\Biometrics\CompleteCheckInAttemptHandlerTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\BiometricCheckInIntegrationTests.cs`

**Interfaces:**
- Create request:

```csharp
public sealed record CreateCheckInAttemptCommand(
    Guid AttendanceSessionId,
    double Latitude,
    double Longitude,
    double LocationAccuracy,
    DateTimeOffset LocationCapturedAt,
    string WorkLocationCode);
```

- Completion request contains attempt ID only.
- Completion response returns `CheckInId`, `AttendanceSessionId`, and `VerificationStatus`.

- [ ] **Step 1: Write backend decision tests**

Cover: active enrollment required, GPS range/freshness, current device binding, liveness pass plus match pass, liveness fail, face mismatch, expired/reused AWS session, provider failure, and same attendance ID returning the existing result.

- [ ] **Step 2: Run tests and confirm missing handlers/routes**

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~CompleteCheckInAttemptHandlerTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricCheckInIntegrationTests
```

- [ ] **Step 3: Implement create-attempt validation and idempotency**

Switch to the JWT tenant, resolve the employee, validate the active profile, and check for an existing check-in/attempt by attendance ID before calling AWS. Require coordinates in range, positive bounded accuracy, location capture no older than the platform freshness window, and a non-empty bounded location code.

- [ ] **Step 4: Implement backend-only completion decision**

Set status to `Verifying`, call `GetResultAsync`, and compare `Confidence` with the platform liveness threshold. Open the trusted R2 reference with `IFileStorageService.OpenReadAsync`; call `CompareFacesAsync` against the current AWS reference bytes; require the platform similarity threshold. On pass, atomically create one `EmployeeCheckIn` with JWT employee/device IDs and mark the attempt `Verified`. On failure, set stable codes `liveness_failed`, `face_mismatch`, `session_expired`, or `provider_error`; do not create a check-in.

- [ ] **Step 5: Document endpoints, run tests, and commit**

Create:

```text
docs/postman-request/Monitoring Biometrics/Create Check-In Attempt.md
docs/postman-request/Monitoring Biometrics/Complete Check-In Attempt.md
docs/postman-request/Monitoring Biometrics/Get Check-In Attempt.md
```

Run and commit the strict backend slice:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~CompleteCheckInAttemptHandlerTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~BiometricCheckInIntegrationTests|FullyQualifiedName~CheckInIntegrationTests"
git add src/ONEVO.Application/Features/Monitoring/Biometrics src/ONEVO.Application/Features/Monitoring/CheckIn src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/BiometricCheckInController.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics tests/ONEVO.Tests.Integration/Monitoring/Biometrics 'docs/postman-request/Monitoring Biometrics'
git commit -m "feat(monitoring): verify strict employee check-in"
```

---

### Task 8: Add versioned typed IPC for check-in orchestration

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\IPC\CheckInMessages.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\IPC\IpcMessages.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\INamedPipeClient.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\NamedPipeClient.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Shared.Tests\CheckInMessageTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Services\CheckInNamedPipeClientTests.cs`

**Interfaces:**

```csharp
public sealed record CheckInLocationPayload(
    double Latitude, double Longitude, double AccuracyMeters,
    DateTimeOffset CapturedAt, string WorkLocationCode);

public sealed record StartCheckInPayload(CheckInLocationPayload Location);
public sealed record BeginBiometricCapturePayload(
    Guid AttendanceSessionId, Guid AttemptId, string AwsSessionId,
    string Region, DateTimeOffset SessionExpiresAt,
    TemporaryAwsCredentialsPayload Credentials);
public sealed record BiometricCaptureCompletedPayload(Guid AttendanceSessionId, Guid AttemptId);
public sealed record CheckInResultPayload(
    Guid AttendanceSessionId, bool Success, Guid? CheckInId,
    string? VerificationStatus, string? ErrorCode, string? Message);
```

- [ ] **Step 1: Write serialization, redaction, size, and correlation tests**

Serialize every message, assert round-trip values, assert no media field exists, assert each envelope is below `Constants.MaxMessageLengthBytes`, and assert duplicate/out-of-order completion is rejected by correlation.

- [ ] **Step 2: Run tests and confirm missing contracts**

```powershell
dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter FullyQualifiedName~CheckInMessageTests
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~CheckInNamedPipeClientTests
```

- [ ] **Step 3: Add exact message types and client surface**

```csharp
event Action<BeginBiometricCapturePayload>? OnBiometricCaptureRequired;
Task<CheckInResultPayload?> StartCheckInAsync(CheckInLocationPayload location, CancellationToken ct);
Task SendBiometricCaptureCompletedAsync(Guid attendanceSessionId, Guid attemptId, CancellationToken ct);
Task CancelCheckInAsync(Guid attendanceSessionId, string reason, CancellationToken ct);
```

Keep temporary credentials inside the pending in-memory envelope only. Add explicit `ToString()` overrides or logging destructuring safeguards that return redacted credential text.

- [ ] **Step 4: Implement client correlation and timeouts**

Use one pending `TaskCompletionSource<CheckInResultPayload?>` per attendance correlation. `BeginBiometricCapture` raises the event without completing the request; `CheckInResult` completes it. Disconnect/cancellation completes and removes pending entries.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter FullyQualifiedName~CheckInMessageTests
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~CheckInNamedPipeClientTests
git add ONEVO.Agent.Shared/IPC ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs tests/ONEVO.Agent.Shared.Tests tests/ONEVO.Agent.TrayApp.Tests/Services
git commit -m "feat(agent): add verified check-in IPC contracts"
```

---

### Task 9: Implement Service API client and CheckInCoordinator

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\AgentApiRoutes.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\BiometricApiModels.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\IBiometricApiClient.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\BiometricApiClient.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\CheckInWorkflowState.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\CheckInCoordinator.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\AgentWorker.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Program.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\CheckInCoordinatorTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Api\BiometricApiClientTests.cs`

**Interfaces:**
- Consumes: typed IPC, DPAPI JWT store, backend create/complete/status APIs, `PresenceSession`, `AgentStateMachine`.
- Produces: begin-capture event and final check-in result; only verified success invokes local clock-in.

- [ ] **Step 1: Write coordinator state tests**

Assert exact sequence `Idle -> CreatingAttempt -> CapturingLiveness -> Verifying -> Verified -> ActivatingMonitoring -> Active`. Assert rejection, timeout, duplicate completion, Tray disconnect, and Service restart never enter `Active` prematurely.

- [ ] **Step 2: Run tests and confirm missing coordinator**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~CheckInCoordinatorTests|FullyQualifiedName~BiometricApiClientTests"
```

- [ ] **Step 3: Implement JWT-authenticated API client**

Add create, complete, and get-status methods. Read JWT for every request, send `Authorization: Bearer`, map `401` to `reauth_required`, `409` to idempotent status reconciliation, and transient `5xx/timeout` to `provider_or_network_unavailable`. Never log response credential bodies.

- [ ] **Step 4: Implement coordinator and narrow AgentWorker**

Use a semaphore to allow one in-flight check-in. Generate `AttendanceSessionId` once. Persist only non-secret recovery metadata needed to query attempt status after restart. Send capture contract to the correlated Tray. After completion, call backend; on `Verified`, call a new internal `ExecuteVerifiedClockIn(attendanceSessionId, verifiedAt)` that starts `PresenceSession` with that ID and transitions state. Direct `LifecycleAction.ClockIn` must return `verified_check_in_required` when biometric policy is enabled.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~CheckInCoordinatorTests|FullyQualifiedName~BiometricApiClientTests|FullyQualifiedName~AgentStateMachineTests|FullyQualifiedName~PresenceSessionTests"
git add ONEVO.Agent.Service/Api ONEVO.Agent.Service/CheckIn ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/Api tests/ONEVO.Agent.Service.Tests/CheckIn
git commit -m "feat(agent): coordinate backend verified check-in"
```

---

### Task 10: Capture fresh GPS, run liveness, and complete the CLOCK IN UI

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\ILocationService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\GeolocationService.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\VerifiedCheckInViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\ClockInViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\PhotoCaptureWindowViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Views\ClockInPage.xaml`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Views\PhotoCaptureWindow.xaml`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\MauiProgram.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Lifecycle\PresenceSession.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\AgentWorker.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\VerifiedCheckInViewModelTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\ClockInViewModelTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Lifecycle\VerifiedAttendanceSessionTests.cs`

**Interfaces:**
- `ILocationService.GetCurrentAsync` returns latitude, longitude, accuracy, and capture timestamp.
- `PresenceSession.ClockIn(Guid attendanceSessionId, DateTimeOffset now)` uses the Service-generated correlation ID.

- [ ] **Step 1: Write UI, location-freshness, and lifecycle tests**

Assert each button click requests fresh GPS, double-click creates one request, stale/missing/denied GPS blocks strict flow, capture errors do not navigate, verified result navigates to `//active`, and the completed WorkSession carries the same attendance ID.

- [ ] **Step 2: Run the exact UI and lifecycle tests**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~VerifiedCheckInViewModelTests|FullyQualifiedName~ClockInViewModelTests"
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~VerifiedAttendanceSessionTests
```

- [ ] **Step 3: Implement fresh GPS and progress UI**

Do not read `onevo.live_latitude/longitude` for authorization. On CLOCK IN obtain a new `GeoPoint`, combine it with selected work-location code, call `StartCheckInAsync`, and show `Getting location`, `Starting verification`, `Scanning face`, `Verifying`, or a stable failure message. Disable the button for the whole request.

- [ ] **Step 4: Connect WebView completion to Service**

When `OnBiometricCaptureRequired` fires, open the liveness view with its in-memory contract. `analysis_complete` sends only attendance/attempt IDs to Service. Remove the verified-clock-in use of `CollectionRecordTypes.FacePhoto`, `Environment.MachineName`, and direct `SendLifecycleAsync(ClockIn)`.

- [ ] **Step 5: Preserve correlation through clock-out**

Start `PresenceSession` with `AttendanceSessionId`; existing `EnqueueWorkSessionSync` uses this as `WorkSessionPayload.SessionId`. Backend Task 6 maps that value to `EmployeeWorkSession.AttendanceSessionId`.

- [ ] **Step 6: Run complete strict-online verification and commit**

```powershell
dotnet test ONEVO.Agent.slnx
dotnet build ONEVO.Agent.slnx
git add ONEVO.Agent.TrayApp ONEVO.Agent.Service/Lifecycle/PresenceSession.cs ONEVO.Agent.Service/AgentWorker.cs tests
git commit -m "feat(tray): complete verified online clock-in"
```

Manual staging acceptance:

```text
Activated employee -> fresh GPS -> laptop liveness -> face match
-> one backend EmployeeCheckIn -> Service Active -> ClockOut
-> one linked EmployeeWorkSession
```

Query PostgreSQL to prove matching tenant, employee, device, and attendance correlation IDs. Confirm no face media or AWS credentials exist under `%ProgramData%\ONEVO\Agent`, Preferences, or SQLite.
