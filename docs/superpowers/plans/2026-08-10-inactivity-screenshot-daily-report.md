# Inactivity Screenshot and Daily Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prompt an actively monitored employee every five continuous idle minutes, capture all monitors only after explicit Allow, sync evidence safely, and include the resulting audit/evidence in the employee daily report.

**Architecture:** The MAUI Tray App owns idle detection, interactive approval, and virtual-desktop capture. The Windows Service owns authenticated IPC, an encrypted evidence spool, ordered retries, and Device-JWT HTTP; the backend owns policy resolution, R2 upload, PostgreSQL metadata, idempotency, and daily-report composition.

**Tech Stack:** .NET 10, C# 14, .NET MAUI Windows, WinUI App SDK notifications, Win32/WinForms virtual-screen capture, Named Pipes, DPAPI, SQLite, ASP.NET Core, MediatR, FluentValidation, EF Core 10, PostgreSQL RLS, Cloudflare R2 through `IFileStorageService`, xUnit, Testcontainers.

## Global Constraints

- Prompt at 300 seconds of continuous idle time and again at each later 300-second boundary.
- Capture only after the employee selects **Allow**; decline, timeout, activity resume, and monitoring stop produce no image.
- Capture the complete Windows virtual desktop across all connected monitors.
- Notification expiry is 270 seconds; the idle polling interval is 5 seconds.
- JPEG quality is 75 and the encoded file must not exceed 10 MB.
- Raw IPC chunks are at most 32 KiB and each serialized envelope must remain below 65,536 characters.
- Local evidence is DPAPI `LocalMachine` protected, capped at 256 MB, retained for at most 72 hours, and deleted after backend acknowledgement.
- Screenshot bytes go to private object storage; PostgreSQL stores metadata and `FileRecordId`, never the image bytes.
- The backend derives tenant, employee, and device identity from the Device JWT, never from client identity fields.
- Collection is disabled outside `MonitoringState.Active`, during break/clock-out/lock/user-switch, after IPC loss, or when activity/screenshot policy is disabled.
- Keyboard keys, characters, mouse coordinates, clipboard content, raw window titles, and screenshot bytes must never be logged.
- Preserve all unrelated dirty-worktree changes in `C:\HR\tray_app_maui`; stage and commit only files named by the active task.

---

## File Structure

### Shared Agent contracts — `C:\HR\tray_app_maui`

- Modify `ONEVO.Agent.Shared/Constants.cs` — fixed timing, size, and chunk limits.
- Modify `ONEVO.Agent.Shared/Models/AgentPolicy.cs` — policy flags used by the inactivity collector.
- Create `ONEVO.Agent.Shared/Models/InactivityCaptureAttemptPayload.cs` — privacy-safe attempt metadata and stable outcomes.
- Create `ONEVO.Agent.Shared/IPC/EvidenceTransferMessages.cs` — start/chunk/complete/ack contracts.
- Modify `ONEVO.Agent.Shared/IPC/IpcMessages.cs` — evidence-transfer message names.
- Create `tests/ONEVO.Agent.Shared.Tests/InactivityCaptureAttemptPayloadTests.cs`.
- Create `tests/ONEVO.Agent.Shared.Tests/EvidenceTransferMessageTests.cs`.

### MAUI Tray App — `C:\HR\tray_app_maui`

- Create `ONEVO.Agent.TrayApp/Services/IInactivityPromptService.cs`.
- Create `ONEVO.Agent.TrayApp/Services/WindowsInactivityPromptService.cs`.
- Create `ONEVO.Agent.TrayApp/Services/NotificationActivationRouter.cs`.
- Modify `ONEVO.Agent.TrayApp/Services/NotificationService.cs` — real informational Windows notifications.
- Create `ONEVO.Agent.TrayApp/Capture/IScreenshotCaptureService.cs`.
- Create `ONEVO.Agent.TrayApp/Capture/VirtualDesktopGeometry.cs`.
- Create `ONEVO.Agent.TrayApp/Capture/VirtualDesktopScreenshotCaptureService.cs`.
- Create `ONEVO.Agent.TrayApp/Collectors/IIdleTimeProvider.cs`.
- Create `ONEVO.Agent.TrayApp/Collectors/WindowsIdleTimeProvider.cs`.
- Create `ONEVO.Agent.TrayApp/Collectors/ICollectorLifecycleCoordinator.cs`.
- Create `ONEVO.Agent.TrayApp/Collectors/InactivityScreenshotCollector.cs`.
- Delete `ONEVO.Agent.TrayApp/Collectors/ScreenshotCollector.cs` after replacement tests pass.
- Modify `ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs` — policy-change restart and cancellation.
- Modify `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`.
- Modify `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs` — bounded chunk transfer and correlated ack.
- Modify `ONEVO.Agent.TrayApp/MauiProgram.cs` and `ONEVO.Agent.TrayApp/App.xaml.cs` — registrations/lifetime.
- Modify `ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs` — pre-stop/drain before break and clock-out.

### Agent Service — `C:\HR\tray_app_maui`

- Modify `ONEVO.Agent.Service/IPC/NamedPipeServer.cs` — safe broadcast and serialized writes.
- Modify `ONEVO.Agent.Service/AgentWorker.cs` — evidence message routing and state/policy validation.
- Create `ONEVO.Agent.Service/Buffer/EvidenceTransferAssembler.cs`.
- Create `ONEVO.Agent.Service/Buffer/IEvidenceProtector.cs`.
- Create `ONEVO.Agent.Service/Buffer/DpapiEvidenceProtector.cs`.
- Create `ONEVO.Agent.Service/Buffer/EvidenceSpoolStore.cs`.
- Modify `ONEVO.Agent.Service/Buffer/ActivityRecordBuffer.cs` — evidence table plus peek/ack/retry semantics.
- Modify `ONEVO.Agent.Service/Sync/ActivitySyncService.cs` — ordered attempt multipart upload.
- Create `ONEVO.Agent.Service/Sync/PolicySyncService.cs`.
- Modify `ONEVO.Agent.Service/Api/OnevoApiClient.cs`, `ActivityIngestModels.cs`, and `AgentApiRoutes.cs`.
- Modify `ONEVO.Agent.Service/Configuration/AgentOptions.cs`, `appsettings.json`, and `Program.cs`.

### Backend — `C:\HR\HRMS-Backend-v1`

- Create `src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/InactivityCaptureAttempt.cs`.
- Modify `src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/MonitoringEvidenceAsset.cs`.
- Create `src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs` and its `GetEffectiveTrayPolicy` query/handler.
- Create `src/ONEVO.Application/Features/Monitoring/Screenshots/RepositoryInterfaces/IInactivityCaptureAttemptRepository.cs` and the `SubmitInactivityCaptureAttempt` command/validator/handler/request files.
- Create `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeDailyMonitoringReportDto.cs` and its `GetEmployeeDailyMonitoringReport` query/handler.
- Create `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/ServiceInterfaces/IActivityDailySummaryRebuilder.cs` and `IMonitoringReportTimeZoneResolver.cs`.
- Create `src/ONEVO.Application/Features/Monitoring/WorkSessions/OutboxPayloads/MonitoringWorkSessionCompletedPayload.cs` and `OutboxHandlers/MonitoringWorkSessionCompletedOutboxHandler.cs`.
- Create `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Screenshots/InactivityCaptureAttemptConfiguration.cs`.
- Create `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Screenshots/EfInactivityCaptureAttemptRepository.cs`.
- Create `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryRebuilder.cs` and `MonitoringReportTimeZoneResolver.cs`.
- Modify `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` and `src/ONEVO.Infrastructure/DependencyInjection.cs`.
- Create `src/ONEVO.Infrastructure/Migrations/20260810090000_AddInactivityCaptureAttempts.cs` and its designer with tenant RLS.
- Create `src/ONEVO.Api/Controllers/Tenant/Monitoring/Policy/TrayMonitoringPolicyController.cs`.
- Modify `src/ONEVO.Api/Controllers/Tenant/Monitoring/Screenshots/TrayScreenshotController.cs`.
- Modify `src/ONEVO.Api/Controllers/Tenant/Monitoring/ActivityMonitoring/MonitoringActivityController.cs`.
- Modify `src/ONEVO.Application/Features/Monitoring/WorkSessions/Commands/SubmitWorkSession/SubmitWorkSessionCommandHandler.cs`.

---

### Task 1: Versioned inactivity and evidence-transfer contracts

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\Constants.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\Models\AgentPolicy.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\Models\InactivityCaptureAttemptPayload.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\IPC\EvidenceTransferMessages.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\IPC\IpcMessages.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Shared.Tests\InactivityCaptureAttemptPayloadTests.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Shared.Tests\EvidenceTransferMessageTests.cs`

**Interfaces:**
- Produces: `InactivityCaptureAttemptPayload`, `EvidenceTransferStartPayload`, `EvidenceTransferChunkPayload`, `EvidenceTransferCompletePayload`, `EvidenceTransferAckPayload`.
- Produces constants: `InactivityThresholdSeconds=300`, `InactivityPromptExpirySeconds=270`, `EvidenceChunkSizeBytes=32768`, `MaxScreenshotBytes=10485760`.

- [ ] **Step 1: Write failing shared-contract tests**

```csharp
[Fact]
public void Attempt_payload_serializes_without_identity_or_image_data()
{
    var value = new InactivityCaptureAttemptPayload
    {
        AttemptId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PolicyVersion = "policy-7",
        IdleStartedAt = DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
        PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z"),
        DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z"),
        CapturedAt = null,
        IdleDurationSeconds = 300,
        MonitorCount = 0,
        Outcome = InactivityCaptureOutcomes.Declined,
        FailureCode = null
    };

    var json = JsonSerializer.Serialize(value);
    Assert.DoesNotContain("tenant", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("employee", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("data_base64", json, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Chunk_limit_fits_existing_ipc_envelope_limit()
{
    var encodedCharacters = 4 * ((Constants.EvidenceChunkSizeBytes + 2) / 3);
    Assert.True(encodedCharacters + 8_192 < Constants.MaxMessageLengthBytes);
}
```

- [ ] **Step 2: Run the tests and verify the contracts are missing**

Run: `dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj --filter "InactivityCaptureAttemptPayloadTests|EvidenceTransferMessageTests"`
Expected: FAIL because the new payload and constants do not exist.

- [ ] **Step 3: Add the stable contracts**

```csharp
public static class InactivityCaptureOutcomes
{
    public const string Captured = "captured";
    public const string Declined = "declined";
    public const string TimedOut = "timed_out";
    public const string ActivityResumed = "activity_resumed";
    public const string MonitoringStopped = "monitoring_stopped";
    public const string CaptureFailed = "capture_failed";
}

public sealed record InactivityCaptureAttemptPayload
{
    public required Guid AttemptId { get; init; }
    public required string PolicyVersion { get; init; }
    public required DateTimeOffset IdleStartedAt { get; init; }
    public required DateTimeOffset PromptedAt { get; init; }
    public DateTimeOffset? DecisionAt { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public required int IdleDurationSeconds { get; init; }
    public required int MonitorCount { get; init; }
    public required string Outcome { get; init; }
    public string? FailureCode { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record EvidenceTransferStartPayload(
    InactivityCaptureAttemptPayload Attempt,
    int TotalBytes,
    int ChunkCount);
public sealed record EvidenceTransferChunkPayload(Guid AttemptId, int Index, string DataBase64);
public sealed record EvidenceTransferCompletePayload(Guid AttemptId);
public sealed record EvidenceTransferAckPayload(Guid AttemptId, bool Accepted, string? ErrorCode);
```

Add `EvidenceTransferStart`, `EvidenceTransferChunk`, `EvidenceTransferComplete`, and `EvidenceTransferAck` message constants. Extend `AgentPolicy` with `InactivityScreenshotEnabled`; it must default to `false` when absent from JSON.

- [ ] **Step 4: Run all shared tests**

Run: `dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit Task 1**

```powershell
git add ONEVO.Agent.Shared tests/ONEVO.Agent.Shared.Tests
git commit -m "feat(agent): add inactivity evidence contracts"
```

---

### Task 2: Backend effective monitoring-policy endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Policy/DTOs/TrayAgentPolicyDto.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Policy/Queries/GetEffectiveTrayPolicy/GetEffectiveTrayPolicyQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Policy/Queries/GetEffectiveTrayPolicy/GetEffectiveTrayPolicyQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Policy/TrayMonitoringPolicyController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Policy/GetEffectiveTrayPolicyQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/Policy/TrayMonitoringPolicyIntegrationTests.cs`

**Interfaces:**
- Consumes: `IMonitoringToggleResolver`, `ITrayCurrentDevice`, `IDateTimeProvider`.
- Produces: authenticated `GET /api/v1/monitoring/tray/policy`.

- [ ] **Step 1: Write handler tests for safe policy composition**

```csharp
[Fact]
public async Task Screenshot_prompt_requires_activity_capture_and_auto_capture()
{
    _toggles.Set(MonitoringCapability.ActivityMonitoring, true);
    _toggles.Set(MonitoringCapability.ScreenshotCapture, true);
    _toggles.Set(MonitoringCapability.AutoScreenshotCapture, false);

    var result = await _handler.Handle(new GetEffectiveTrayPolicyQuery(), default);

    result.IsSuccess.Should().BeTrue();
    result.Value!.ActivitySignalEnabled.Should().BeTrue();
    result.Value.InactivityScreenshotEnabled.Should().BeFalse();
}
```

Also test unauthenticated device returns 401 and `ValidUntil` is exactly one hour after the injected clock.

- [ ] **Step 2: Run the focused unit test**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter GetEffectiveTrayPolicyQueryHandlerTests`
Expected: FAIL because the query does not exist.

- [ ] **Step 3: Implement the query and version hash**

```csharp
public sealed record TrayAgentPolicyDto(
    string Version,
    bool ActivitySignalEnabled,
    bool AppUsageEnabled,
    bool ScreenshotEnabled,
    bool InactivityScreenshotEnabled,
    bool CameraVerificationEnabled,
    DateTimeOffset ValidUntil);
```

Resolve the authenticated device's employee ID and fetch `ActivityMonitoring`, `ApplicationTracking`, `ScreenshotCapture`, `AutoScreenshotCapture`, and `IdentityVerification`; then compute:

```csharp
var inactivityEnabled = activityEnabled && screenshotEnabled && autoScreenshotEnabled;
var fingerprint = $"{activityEnabled}:{appUsageEnabled}:{screenshotEnabled}:{autoScreenshotEnabled}:{cameraEnabled}";
var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];
```

- [ ] **Step 4: Add the Tray-device controller and integration tests**

The controller must use `[Authorize(Policy = "TrayDevicePolicy")]`, return 401 without a Device JWT, and return a tenant-specific policy without accepting tenant or employee IDs in query/body data.

- [ ] **Step 5: Run backend policy tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter Monitoring.Policy`
Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter TrayMonitoringPolicyIntegrationTests`
Expected: PASS.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src/ONEVO.Application/Features/Monitoring/Policy src/ONEVO.Api/Controllers/Tenant/Monitoring/Policy tests/ONEVO.Tests.Unit/Features/Monitoring/Policy tests/ONEVO.Tests.Integration/Monitoring/Policy
git commit -m "feat(monitoring): expose effective tray policy"
```

---

### Task 3: Agent Service policy refresh and Tray broadcast

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\AgentApiRoutes.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\OnevoApiClient.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Sync\PolicySyncService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\IPC\NamedPipeServer.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Policy\PolicyCache.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Program.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Sync\PolicySyncServiceTests.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\IPC\NamedPipeServerBroadcastTests.cs`

**Interfaces:**
- Consumes: backend policy DTO and Device JWT.
- Produces: `PolicyCache.Set(AgentPolicy)` plus `NamedPipeServer.BroadcastAsync(PolicyPush)`.

- [ ] **Step 1: Write policy refresh tests**

Test successful refresh updates the cache and broadcasts once; 401 leaves the last valid policy only until `ValidUntil`; expired policy changes screenshot flags to false; no JWT makes no HTTP call.

- [ ] **Step 2: Run tests and confirm failure**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "PolicySyncServiceTests|NamedPipeServerBroadcastTests"`
Expected: FAIL because refresh and broadcast APIs are missing.

- [ ] **Step 3: Implement serialized connection broadcasts**

Track each authenticated connection as a small object containing its writer and `SemaphoreSlim`. Both request replies and broadcasts must acquire the same write lock so JSON lines cannot interleave. Remove the connection in `finally` when its client loop ends.

- [ ] **Step 4: Implement startup/hourly policy refresh**

`PolicySyncService` performs an immediate fetch after a Device JWT becomes available, refreshes hourly, rejects `ValidUntil <= UtcNow`, and broadcasts only when the policy version changes. Register it after token refresh and before activity sync.

- [ ] **Step 5: Run Service tests**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "PolicySyncServiceTests|NamedPipeServerBroadcastTests|PolicyCacheTests"`
Expected: PASS.

- [ ] **Step 6: Commit Task 3**

```powershell
git add ONEVO.Agent.Service tests/ONEVO.Agent.Service.Tests
git commit -m "feat(agent): refresh and broadcast monitoring policy"
```

---

### Task 4: Actionable Windows Allow/Skip notification

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\IInactivityPromptService.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\NotificationActivationRouter.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\WindowsInactivityPromptService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\NotificationService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\App.xaml.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\MauiProgram.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Notifications\NotificationActivationRouterTests.cs`

**Interfaces:**
- Produces: `Task<InactivityPromptDecision> PromptAsync(Guid attemptId, TimeSpan idleFor, TimeSpan expiresIn, CancellationToken ct)` and `Dismiss(Guid attemptId)`.

- [ ] **Step 1: Write activation-router tests**

```csharp
[Theory]
[InlineData("attempt=11111111-1111-1111-1111-111111111111&decision=allow", InactivityPromptDecision.Allowed)]
[InlineData("attempt=11111111-1111-1111-1111-111111111111&decision=skip", InactivityPromptDecision.Declined)]
public void Routes_only_known_attempt_and_decision(string args, InactivityPromptDecision expected)
{
    var router = new NotificationActivationRouter();
    var pending = router.WaitAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"), default);
    router.Route(args);
    Assert.Equal(expected, pending.Result);
}
```

Also test unknown attempt IDs, duplicate activation, cancellation, and expiry.

- [ ] **Step 2: Run focused tests**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter NotificationActivationRouterTests`
Expected: FAIL because the router is missing.

- [ ] **Step 3: Implement App SDK notification registration and routing**

Define the testable contract in `IInactivityPromptService.cs`:

```csharp
public enum InactivityPromptDecision
{
    Allowed,
    Declined,
    TimedOut,
    ActivityResumed,
    MonitoringStopped
}

public interface IInactivityPromptService
{
    Task<InactivityPromptDecision> PromptAsync(
        Guid attemptId,
        TimeSpan idleFor,
        TimeSpan expiresIn,
        CancellationToken ct);
    void Dismiss(Guid attemptId);
}
```

Register `AppNotificationManager.Default` once during app startup, subscribe to `NotificationInvoked`, and unregister during shutdown. Build the notification with only these arguments:

```text
attempt=11111111-1111-1111-1111-111111111111&decision=allow
attempt=11111111-1111-1111-1111-111111111111&decision=skip
```

Use title `Activity check` and body `No keyboard or mouse activity was detected for 5 minutes. Allow a screenshot of all connected monitors?` Set expiration to 270 seconds. Do not activate or foreground the MAUI window.

- [ ] **Step 4: Run tests and perform a Windows smoke check**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter NotificationActivationRouterTests`
Expected: PASS.

Manual: run the Tray App, issue a test prompt, verify Allow and Skip complete exactly one correlated task, and verify the notification disappears after a decision.

- [ ] **Step 5: Commit Task 4**

```powershell
git add ONEVO.Agent.TrayApp/Services ONEVO.Agent.TrayApp/App.xaml.cs ONEVO.Agent.TrayApp/MauiProgram.cs tests/ONEVO.Agent.TrayApp.Tests/Notifications
git commit -m "feat(tray): add actionable inactivity notification"
```

---

### Task 5: Combined virtual-desktop screenshot capture

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Capture\IScreenshotCaptureService.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Capture\VirtualDesktopGeometry.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Capture\VirtualDesktopScreenshotCaptureService.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Capture\VirtualDesktopGeometryTests.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Capture\JpegSizeReducerTests.cs`

**Interfaces:**
- Produces: `Task<ScreenshotCaptureResult> CaptureAsync(CancellationToken ct)` returning JPEG bytes, capture time, monitor count, virtual bounds, checksum, or a stable failure code.

- [ ] **Step 1: Write geometry and encoding-policy tests**

```csharp
[Fact]
public void Union_supports_monitors_left_of_primary()
{
    var bounds = VirtualDesktopGeometry.Union([
        new Rectangle(-1920, 0, 1920, 1080),
        new Rectangle(0, 0, 2560, 1440)]);

    Assert.Equal(new Rectangle(-1920, 0, 4480, 1440), bounds);
}
```

Test empty monitor list fails with `no_displays`; encoded output is JPEG; oversize output is proportionally reduced; cancellation returns no bytes.

- [ ] **Step 2: Run capture tests and confirm failure**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "VirtualDesktopGeometryTests|JpegSizeReducerTests"`
Expected: FAIL because capture components are absent.

- [ ] **Step 3: Implement virtual-screen capture**

Define the result without exposing `Bitmap` outside the capture boundary:

```csharp
public sealed record ScreenshotCaptureResult(
    bool Success,
    ReadOnlyMemory<byte> JpegBytes,
    DateTimeOffset? CapturedAt,
    int MonitorCount,
    Rectangle VirtualBounds,
    string? Sha256,
    string? FailureCode);

public interface IScreenshotCaptureService
{
    Task<ScreenshotCaptureResult> CaptureAsync(CancellationToken ct);
}
```

Use `Screen.AllScreens` for count and `SystemInformation.VirtualScreen` for bounds. Pass the virtual origin to `Graphics.CopyFromScreen`; do not assume `(0,0)`. Encode at JPEG quality 75, compute SHA-256 over final bytes, and repeat proportional downscaling until bytes are at most `Constants.MaxScreenshotBytes` or the minimum scale of 0.35 is reached.

- [ ] **Step 4: Run automated and manual capture checks**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "VirtualDesktopGeometryTests|JpegSizeReducerTests"`
Expected: PASS.

Manual: attach two monitors with different resolutions and one negative X origin; verify both appear once, in correct relative positions, and the JPEG is at most 10 MB.

- [ ] **Step 5: Commit Task 5**

```powershell
git add ONEVO.Agent.TrayApp/Capture tests/ONEVO.Agent.TrayApp.Tests/Capture
git commit -m "feat(tray): capture the complete virtual desktop"
```

---

### Task 6: Five-minute inactivity workflow collector

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\IIdleTimeProvider.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\WindowsIdleTimeProvider.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\ICollectorLifecycleCoordinator.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\InactivityScreenshotCollector.cs`
- Delete: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\ScreenshotCollector.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Collectors\CollectorCoordinator.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\ActiveSessionViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\INamedPipeClient.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\NamedPipeClient.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\MauiProgram.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Collectors\InactivityScreenshotCollectorTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Collectors\CollectorCoordinatorTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Services\EvidenceTransferClientTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\ViewModels\ActiveSessionViewModelTests.cs`

**Interfaces:**
- Consumes: idle provider, prompt service, capture service, Named Pipe evidence submit.
- Produces: exactly one attempt per continuous-idle bucket and `SubmitInactivityAttemptAsync(InactivityCaptureAttemptPayload attempt, ReadOnlyMemory<byte> jpegBytes, CancellationToken ct)`.

- [ ] **Step 1: Write the state-machine tests**

```csharp
[Theory]
[InlineData(299, 0)]
[InlineData(300, 1)]
[InlineData(599, 1)]
[InlineData(600, 2)]
public async Task Prompts_once_per_five_minute_bucket(int idleSeconds, int expected)
{
    await _sut.StartAsync(EnabledPolicy(), default);
    if (idleSeconds >= 300)
        await _sut.EvaluateAsync(300, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);
    if (idleSeconds >= 600)
        await _sut.EvaluateAsync(600, DateTimeOffset.Parse("2026-08-10T01:10:00Z"), default);
    Assert.Equal(expected, _prompt.RequestCount);
}
```

Add tests for Allow capture, Skip without capture, expiry, input reset, Allow-versus-poll race, policy disabled, state pause, IPC loss, collector restart after policy change, and pre-stop ordering. `ActiveSessionViewModelTests` must prove the coordinator finishes `PrepareForPauseAsync` before it sends `StartBreak` or `ClockOut`, and that a rejected lifecycle result resumes collectors when the authoritative state is still Active. `EvidenceTransferClientTests` must prove 65,537 bytes become three chunks of 32,768, 32,768, and 1 byte, and metadata-only attempts emit no chunk envelopes.

- [ ] **Step 2: Run collector tests and verify failure**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "InactivityScreenshotCollectorTests|CollectorCoordinatorTests|ActiveSessionViewModelTests|EvidenceTransferClientTests"`
Expected: FAIL because the collector is missing and the old coordinator does not restart on policy change.

- [ ] **Step 3: Implement serialized bucket evaluation**

`StartAsync` must require `ActivitySignalEnabled`, `ScreenshotEnabled`, and `InactivityScreenshotEnabled`. The five-second loop calls an internal testable `EvaluateAsync`. Use a `SemaphoreSlim` around bucket transition, prompt result, capture, and stop. On Allow, create a `captured` attempt only after capture succeeds; otherwise create `capture_failed` with one of `session_locked`, `no_displays`, `capture_api_failed`, or `capture_too_large`.

`ICollectorLifecycleCoordinator.PrepareForPauseAsync` stops collectors, dismisses the prompt, waits for any accepted capture and attempt IPC acknowledgement, and returns only after the final Named Pipe write completes. `ActiveSessionViewModel` calls it before `StartBreak` and `ClockOut`; if the Service rejects the lifecycle change while state remains Active, call `ResumeAfterRejectedPauseAsync` to reconcile the current policy.

Implement `NamedPipeClient.SubmitInactivityAttemptAsync` with Task 1's start/chunk/complete envelopes. Split the JPEG into 32 KiB raw chunks, send each through the existing serialized writer, and wait for the correlated `EvidenceTransferAck`. Metadata-only outcomes send start plus complete with zero chunks.

- [ ] **Step 4: Replace the periodic collector and fix policy reconfiguration**

Remove `ScreenshotCollector` from DI. Register `InactivityScreenshotCollector` as the screenshot `IAgentCollector`. When a new policy version arrives, `CollectorCoordinator` must stop all active collectors and restart them with the new validated policy. Its local fallback policy must keep inactivity screenshots disabled.

- [ ] **Step 5: Run Tray collector tests**

Run: `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "InactivityScreenshotCollectorTests|CollectorCoordinatorTests|ActiveSessionViewModelTests|EvidenceTransferClientTests"`
Expected: PASS.

- [ ] **Step 6: Commit Task 6**

```powershell
git add ONEVO.Agent.TrayApp/Collectors ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs ONEVO.Agent.TrayApp/MauiProgram.cs tests/ONEVO.Agent.TrayApp.Tests/Collectors tests/ONEVO.Agent.TrayApp.Tests/Services/EvidenceTransferClientTests.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ActiveSessionViewModelTests.cs
git commit -m "feat(tray): prompt on each five-minute idle bucket"
```

---

### Task 7: Chunked IPC and encrypted evidence spool

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\EvidenceTransferAssembler.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\IEvidenceProtector.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\DpapiEvidenceProtector.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\EvidenceSpoolStore.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\ActivityRecordBuffer.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\AgentWorker.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Program.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Buffer\EvidenceTransferAssemblerTests.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Buffer\EvidenceSpoolStoreTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\ActivityRecordBufferTests.cs`

**Interfaces:**
- Consumes: Task 1 evidence envelopes.
- Produces: `SubmitInactivityAttemptAsync(attempt, jpegBytes, ct)` and durable pending record keyed by `AttemptId`.

- [ ] **Step 1: Write chunk validation and durable-queue tests**

Test a valid three-chunk transfer; missing, duplicate, out-of-order, oversize, and checksum-mismatch transfers; metadata-only outcome with zero chunks; state/policy rejection; spool quota; 72-hour purge; and duplicate attempt idempotency.

Add queue tests proving `PeekPendingBatch` does not change status, `MarkAcknowledged` removes eligibility, `ScheduleRetry` preserves the event ID, and a later work session remains behind an earlier failed evidence record.

- [ ] **Step 2: Run focused Service tests**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "EvidenceTransferAssemblerTests|EvidenceSpoolStoreTests|ActivityRecordBufferTests"`
Expected: FAIL against the current pre-mark-synced queue.

- [ ] **Step 3: Implement bounded chunk transfer**

The Task 6 Tray method sends start, indexed chunks, then complete, and waits for the correlated ack. The Service assembler caps concurrent transfers at two, total bytes at 10 MB per transfer, and idle assembly lifetime at two minutes. It never logs chunk content.

- [ ] **Step 4: Implement DPAPI spool and atomic metadata enqueue**

Protect bytes with `ProtectedData.Protect(bytes, attemptId.ToByteArray(), DataProtectionScope.LocalMachine)`. Write to a random `.evidence` filename under `%ProgramData%\ONEVO\Agent\EvidenceSpool`; apply service/admin-only ACLs; then insert the queue row and `evidence_spool` row in one SQLite transaction. If the DB transaction fails, delete the file.

`evidence_spool` columns are:

```sql
event_id TEXT PRIMARY KEY,
encrypted_path TEXT NULL,
encrypted_size INTEGER NOT NULL,
created_at TEXT NOT NULL,
expires_at TEXT NOT NULL,
FOREIGN KEY(event_id) REFERENCES collection_records(event_id) ON DELETE CASCADE
```

- [ ] **Step 5: Replace dequeue semantics**

Expose `PeekPendingBatch`, `MarkAcknowledged`, and `ScheduleRetry`. Do not mark records synced before HTTP acknowledgement. On startup, mark legacy `record_type='screenshot'` rows as quarantined and clear their base64 payload because they lack the new per-attempt approval proof; record `legacy_unapproved_payload` without transmitting the image.

- [ ] **Step 6: Run Service tests**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "EvidenceTransferAssemblerTests|EvidenceSpoolStoreTests|ActivityRecordBufferTests"`
Expected: PASS.

- [ ] **Step 7: Commit Task 7**

```powershell
git add ONEVO.Agent.Shared ONEVO.Agent.Service/Buffer ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Program.cs tests
git commit -m "feat(agent): spool approved evidence through bounded IPC"
```

---

### Task 8: Backend inactivity-attempt schema, repository, and RLS

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/InactivityCaptureAttempt.cs`
- Modify: `src/ONEVO.Domain/Features/Monitoring/Screenshots/Entities/MonitoringEvidenceAsset.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Screenshots/RepositoryInterfaces/IInactivityCaptureAttemptRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Screenshots/InactivityCaptureAttemptConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Screenshots/MonitoringEvidenceAssetConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Screenshots/EfInactivityCaptureAttemptRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20260810090000_AddInactivityCaptureAttempts.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20260810090000_AddInactivityCaptureAttempts.Designer.cs`
- Modify: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `tests/ONEVO.Tests.Architecture/InactivityCaptureAttemptArchitectureTests.cs`

**Interfaces:**
- Produces: tenant-owned idempotent attempt storage and optional one-to-one evidence link.

- [ ] **Step 1: Write architecture tests before the entity**

Verify the entity implements `ITenantOwnedEntity`; configuration creates the tenant/employee/prompted index; the attempt ID is not database-generated; the evidence foreign key is restrictive; and the generated migration enables and forces RLS with tenant policy expressions.

- [ ] **Step 2: Run the architecture test**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter InactivityCaptureAttemptArchitectureTests`
Expected: FAIL because the entity/configuration do not exist.

- [ ] **Step 3: Add entity and repository**

```csharp
public sealed class InactivityCaptureAttempt : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public Guid? WorkSessionId { get; set; }
    public DateTimeOffset IdleStartedAt { get; set; }
    public DateTimeOffset PromptedAt { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public int IdleDurationSeconds { get; set; }
    public int MonitorCount { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public Guid? EvidenceAssetId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Repository methods: `GetByIdAsync(tenantId, id)`, `Add`, `GetByEmployeeRangeAsync(tenantId, employeeId, fromUtc, toUtc)`, and `FindContainingWorkSessionAsync`.

- [ ] **Step 4: Generate and harden the migration**

Run:

```powershell
dotnet ef migrations add AddInactivityCaptureAttempts --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj
```

Rename the generated migration files to `20260810090000_AddInactivityCaptureAttempts.cs` and `20260810090000_AddInactivityCaptureAttempts.Designer.cs`, and set the designer migration attribute to `20260810090000_AddInactivityCaptureAttempts`.

Add tenant-aware indexes and FK constraints, then use this exact policy shape for `inactivity_capture_attempts`:

```sql
ALTER TABLE inactivity_capture_attempts ENABLE ROW LEVEL SECURITY;
ALTER TABLE inactivity_capture_attempts FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON inactivity_capture_attempts;
CREATE POLICY tenant_isolation ON inactivity_capture_attempts
USING (
    current_setting('app.tenant_context_mode', true) = 'admin'
    OR (
        current_setting('app.tenant_context_mode', true) = 'tenant'
        AND tenant_id::text = current_setting('app.current_tenant_id', true)
    )
)
WITH CHECK (
    current_setting('app.tenant_context_mode', true) = 'admin'
    OR (
        current_setting('app.tenant_context_mode', true) = 'tenant'
        AND tenant_id::text = current_setting('app.current_tenant_id', true)
    )
);
```

- [ ] **Step 5: Run architecture and migration checks**

Run: `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj --filter InactivityCaptureAttemptArchitectureTests`
Run: `dotnet ef migrations script --project src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj --startup-project src\ONEVO.Api\ONEVO.Api.csproj --idempotent`
Expected: PASS; generated SQL contains table, indexes, FKs, and RLS statements.

- [ ] **Step 6: Commit Task 8**

```powershell
git add src/ONEVO.Domain/Features/Monitoring/Screenshots src/ONEVO.Application/Features/Monitoring/Screenshots/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Architecture
git commit -m "feat(monitoring): add inactivity capture attempt schema"
```

---

### Task 9: Idempotent backend attempt and screenshot ingest

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Screenshots/Commands/SubmitInactivityCaptureAttempt/SubmitInactivityCaptureAttemptCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Screenshots/Commands/SubmitInactivityCaptureAttempt/SubmitInactivityCaptureAttemptCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Screenshots/Commands/SubmitInactivityCaptureAttempt/SubmitInactivityCaptureAttemptCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Screenshots/DTOs/Requests/InactivityCaptureAttemptForm.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Screenshots/TrayScreenshotController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Screenshots/SubmitInactivityCaptureAttemptValidatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Screenshots/SubmitInactivityCaptureAttemptHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/Screenshots/InactivityCaptureIngestIntegrationTests.cs`

**Interfaces:**
- Produces: `POST /api/v1/monitoring/tray/inactivity-attempts` multipart endpoint.

- [ ] **Step 1: Write validator tests**

Cover allowed outcome values, minimum 300 idle seconds, timestamp ordering, `captured` requiring JPEG and monitor count at least one, non-captured outcomes forbidding a file, 10 MB limit, and unknown failure codes rejected.

- [ ] **Step 2: Write handler tests**

Prove the handler derives identity from `ITrayCurrentDevice`, checks activity/screenshot/auto-screenshot toggles for captured images, calls `IFileStorageService.UploadAsync` once, creates `MonitoringEvidenceAsset` with `TriggerType="inactivity_approved"`, and returns the existing IDs without a second upload on identical retry. Conflicting retries return 409.

- [ ] **Step 3: Run focused unit tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "SubmitInactivityCaptureAttempt"`
Expected: FAIL because the feature is missing.

- [ ] **Step 4: Implement command, validator, and handler**

The command carries metadata and `Stream? Content`; it does not accept tenant, employee, or device identity. For captured attempts, upload with purpose `UploadPurposeCatalog.MonitoringScreenshot`, then save attempt and evidence metadata once. Store virtual bounds and checksum in `MetadataJson`; do not store the image or file URL.

- [ ] **Step 5: Add endpoint and PostgreSQL integration tests**

Integration cases: valid captured JPEG returns 200; declined without file returns 200; captured without file returns 400; tenant A cannot read tenant B; same attempt/file retry is idempotent; conflicting retry returns 409; policy-disabled capture returns 403; stored file is referenced by `FileRecordId` and no byte column exists.

- [ ] **Step 6: Run all ingest tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "SubmitInactivityCaptureAttempt"`
Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter InactivityCaptureIngestIntegrationTests`
Expected: PASS.

- [ ] **Step 7: Commit Task 9**

```powershell
git add src/ONEVO.Application/Features/Monitoring/Screenshots src/ONEVO.Api/Controllers/Tenant/Monitoring/Screenshots tests/ONEVO.Tests.Unit/Features/Monitoring/Screenshots tests/ONEVO.Tests.Integration/Monitoring/Screenshots
git commit -m "feat(monitoring): ingest approved inactivity evidence"
```

---

### Task 10: Ordered Agent upload and acknowledgement

**Files:**
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\AgentApiRoutes.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Api\ActivityIngestModels.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Sync\ActivitySyncService.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Buffer\EvidenceSpoolStore.cs`
- Test: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\Sync\ActivitySyncServiceTests.cs`

**Interfaces:**
- Consumes: pending inactivity attempt plus optional encrypted spool file.
- Produces: multipart backend request; ack/delete on success; retry without reordering on failure.

- [ ] **Step 1: Add sync-order and multipart tests**

Prove metadata fields use exact backend names; captured outcome decrypts and attaches one JPEG; declined attaches no file; 200/duplicate-idempotent 409 acknowledges; 500/network failure remains pending; a failed earlier attempt prevents a later work session from being submitted; successful upload deletes the encrypted file only after queue acknowledgement.

- [ ] **Step 2: Run ActivitySyncService tests**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter ActivitySyncServiceTests`
Expected: FAIL because the current service groups by record type, pre-marks records synced, and submits work sessions before screenshots.

- [ ] **Step 3: Implement adjacent ordered batching**

Read pending rows in ascending SQLite ID. Batch only adjacent records of the same batchable type. Process an inactivity attempt as one multipart request. Stop the batch at the first retryable failure; acknowledge only successful IDs before that failure. This guarantees a completed work session cannot overtake earlier activity/evidence.

- [ ] **Step 4: Implement response classification**

- 200/202: acknowledge and delete the spool file.
- 409 with backend idempotency code `attempt_already_recorded`: acknowledge and delete.
- 400 validation failure: quarantine metadata, delete the local image, and do not retry.
- 401: leave pending and trigger one token-refresh path.
- 403: quarantine as `policy_rejected`, delete the local image, and stop screenshot collection until policy refresh.
- 429/5xx/network: retain pending data and schedule exponential backoff with jitter.

- [ ] **Step 5: Run Service sync tests**

Run: `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "ActivitySyncServiceTests|EvidenceSpoolStoreTests|ActivityRecordBufferTests"`
Expected: PASS.

- [ ] **Step 6: Commit Task 10**

```powershell
git add ONEVO.Agent.Service/Api ONEVO.Agent.Service/Sync ONEVO.Agent.Service/Buffer tests/ONEVO.Agent.Service.Tests
git commit -m "feat(agent): sync inactivity evidence in durable order"
```

---

### Task 11: Clock-out finalization and employee daily report

**Files:**
- Move: `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregator.cs` to `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Services/ActivityDailySummaryAggregator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/ServiceInterfaces/IActivityDailySummaryRebuilder.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/ServiceInterfaces/IMonitoringReportTimeZoneResolver.cs`
- Create: `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryRebuilder.cs`
- Create: `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/MonitoringReportTimeZoneResolver.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/ActivityDailySummaryJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/WorkSessions/OutboxPayloads/MonitoringWorkSessionCompletedPayload.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/WorkSessions/OutboxHandlers/MonitoringWorkSessionCompletedOutboxHandler.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/WorkSessions/Commands/SubmitWorkSession/SubmitWorkSessionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeDailyMonitoringReportDto.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetEmployeeDailyMonitoringReport/GetEmployeeDailyMonitoringReportQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/GetEmployeeDailyMonitoringReport/GetEmployeeDailyMonitoringReportQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/WorkSessions/RepositoryInterfaces/IWorkSessionRepository.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Screenshots/RepositoryInterfaces/IInactivityCaptureAttemptRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/WorkSessions/EfWorkSessionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Screenshots/EfInactivityCaptureAttemptRepository.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Monitoring/ActivityMonitoring/MonitoringActivityController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/WorkSessions/MonitoringWorkSessionCompletedOutboxHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/GetEmployeeDailyMonitoringReportQueryHandlerTests.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Monitoring/ActivityMonitoring/ActivityDailySummaryAggregatorTests.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/ActivityMonitoring/DailyMonitoringReportIntegrationTests.cs`

**Interfaces:**
- Produces: idempotent clock-out summary rebuild and `GET /api/v1/monitoring/activity/daily-report`.

- [ ] **Step 1: Write clock-out outbox tests**

Verify `SubmitWorkSessionCommandHandler` adds `monitoring_work_session_completed` in the same unit of work as the session. Verify the handler resolves the employee legal entity's IANA/Windows timezone, computes the local `DateOnly`, and calls `RebuildAsync(tenantId, employeeId, date)` exactly once per delivery while remaining idempotent.

- [ ] **Step 2: Write daily-report query tests**

```csharp
result.Value.Should().BeEquivalentTo(new
{
    EmployeeId = employeeId,
    Date = new DateOnly(2026, 8, 10),
    PromptCount = 4,
    CapturedCount = 2,
    DeclinedCount = 1,
    TimedOutCount = 1
});
```

Also verify each captured item returns `EvidenceAssetId` and `ScreenshotAvailable=true`, non-captured attempts return no asset ID, and the DTO contains no object key or permanent URL.

- [ ] **Step 3: Run focused backend tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "MonitoringWorkSessionCompletedOutboxHandlerTests|GetEmployeeDailyMonitoringReportQueryHandlerTests"`
Expected: FAIL because outbox finalization and report composition are absent.

- [ ] **Step 4: Extract the reusable summary rebuilder**

Move the pure aggregator into Application, implement `IActivityDailySummaryRebuilder` in Infrastructure, and make both the nightly job and outbox handler call it. The rebuilder loads snapshots and upserts the existing `ActivityDailySummary`; repeated calls replace totals rather than adding them.

Define timezone resolution explicitly:

```csharp
public interface IMonitoringReportTimeZoneResolver
{
    Task<TimeZoneInfo> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct);
}
```

`MonitoringReportTimeZoneResolver` finds the employee by `Employee.Id` or `Employee.UserId`, loads `LegalEntity.Timezone`, and calls `TimeZoneInfo.FindSystemTimeZoneById`. A missing/invalid value resolves to `TimeZoneInfo.Utc` and logs only tenant/employee IDs plus the stable code `invalid_monitoring_timezone`.

- [ ] **Step 5: Enqueue finalization transactionally**

Add `OutboxMessageTypes.MonitoringWorkSessionCompleted = "monitoring_work_session_completed"`. Enqueue a payload containing work-session ID, tenant ID, employee ID, and clock-out instant before the existing single `SaveChangesAsync` call. Register the real outbox handler.

- [ ] **Step 6: Implement report composition and endpoint**

Return:

```csharp
public sealed record EmployeeDailyMonitoringReportDto(
    Guid EmployeeId,
    DateOnly Date,
    ActivityDailySummaryDto? Activity,
    IReadOnlyList<WorkSessionReportDto> WorkSessions,
    int PromptCount,
    int CapturedCount,
    int DeclinedCount,
    int TimedOutCount,
    int ActivityResumedCount,
    int MonitoringStoppedCount,
    int FailedCount,
    IReadOnlyList<InactivityAttemptReportDto> InactivityAttempts);

public sealed record WorkSessionReportDto(
    Guid SessionId,
    DateTimeOffset ClockInAt,
    DateTimeOffset ClockOutAt,
    int WorkSeconds,
    int BreakSeconds,
    int BreakCount);

public sealed record InactivityAttemptReportDto(
    Guid AttemptId,
    DateTimeOffset PromptedAt,
    DateTimeOffset? CapturedAt,
    int IdleDurationSeconds,
    int MonitorCount,
    string Outcome,
    string? FailureCode,
    Guid? EvidenceAssetId,
    bool ScreenshotAvailable);
```

Interpret `date` in the employee legal entity's configured timezone, convert its start/end to UTC for repository filters, and require `monitoring:read`. Evidence URLs remain behind the existing authorized `GetScreenshotUrl` query.

- [ ] **Step 7: Run unit and integration tests**

Run: `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter "MonitoringWorkSessionCompletedOutboxHandlerTests|GetEmployeeDailyMonitoringReportQueryHandlerTests|ActivityDailySummaryAggregatorTests"`
Run: `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter DailyMonitoringReportIntegrationTests`
Expected: PASS.

- [ ] **Step 8: Commit Task 11**

```powershell
git add src/ONEVO.Application src/ONEVO.Infrastructure src/ONEVO.Api/Controllers/Tenant/Monitoring/ActivityMonitoring tests
git commit -m "feat(monitoring): finalize and expose employee daily reports"
```

---

### Task 12: End-to-end verification, API examples, and operational checks

**Files:**
- Modify: `C:\HR\tray_app_maui\docs\postman\ONEVO-Tray-Monitoring.postman_collection.json`
- Modify: `C:\HR\tray_app_maui\docs\postman\README.md`
- Modify: `C:\HR\tray_app_maui\README.md`
- Modify: `C:\HR\HRMS-Backend-v1\ONEVO-HRMS.postman_collection.json`
- Create: `C:\HR\HRMS-Backend-v1\docs\superpowers\workflow\INACTIVITY_SCREENSHOT_DAILY_REPORT_VALIDATION.md`

**Interfaces:**
- Validates the complete MAUI → Service → backend → R2/PostgreSQL → daily-report path.

- [ ] **Step 1: Run the complete automated test suites**

Run in `C:\HR\tray_app_maui`:

```powershell
dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj
dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
dotnet build ONEVO.Agent.slnx -c Debug
```

Run in `C:\HR\HRMS-Backend-v1`:

```powershell
dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj
dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj
dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj
dotnet build src\ONEVO.Api\ONEVO.Api.csproj -c Debug
```

Expected: all commands exit 0.

- [ ] **Step 2: Verify the Allow path with two monitors**

Clock in, leave the device idle for five minutes, select Allow, and verify:

1. One combined JPEG is produced.
2. The Service SQLite row remains pending until backend 200.
3. The local encrypted evidence file exists before ack and is deleted after ack.
4. PostgreSQL has one inactivity attempt and one evidence asset, with no image byte column.
5. R2 has one private object.
6. Daily report returns `CapturedCount=1` and the evidence asset ID.
7. Opening the screenshot uses a short-lived signed URL.

- [ ] **Step 3: Verify Skip, timeout, repeat, reset, and lifecycle gates**

- Skip: metadata only, no local image, no R2 object.
- Timeout: no image; at ten continuous idle minutes a new prompt appears.
- Allow/Skip click: Windows input resets the next threshold to five minutes after that click.
- Mouse/keyboard activity before decision: prompt closes as `activity_resumed`.
- Break, clock-out, lock, user switch, policy disable, and IPC disconnect: no prompt and no capture.

- [ ] **Step 4: Verify offline retry and ordering**

Stop the backend, approve one capture, then clock out. Confirm the encrypted file and attempt remain pending and the work session is not uploaded ahead of them. Restart the backend; confirm attempt/evidence upload first, work session second, local evidence deletion, outbox summary rebuild, and a complete daily report.

- [ ] **Step 5: Verify resource and privacy constraints**

Measure Tray plus Service normal CPU below 2% average outside capture and normal working-set target below 50 MB. Inspect logs and SQLite to prove no key data, mouse coordinates, raw screenshot bytes, base64 image, object key, access token, tenant payload identity, or permanent URL is present.

- [ ] **Step 6: Update Postman and validation documentation**

Add requests for Tray policy, captured attempt multipart ingest, declined attempt ingest, daily report, and signed screenshot URL. Record exact commands, response status, database evidence, R2 evidence, and privacy checks in `INACTIVITY_SCREENSHOT_DAILY_REPORT_VALIDATION.md`.

- [ ] **Step 7: Commit Task 12 in each repository**

Tray repository:

```powershell
git add docs/postman README.md
git commit -m "docs(agent): validate inactivity screenshot workflow"
```

Backend repository:

```powershell
git add ONEVO-HRMS.postman_collection.json docs/superpowers/workflow/INACTIVITY_SCREENSHOT_DAILY_REPORT_VALIDATION.md
git commit -m "docs(monitoring): document inactivity report validation"
```

---

## Final Acceptance Criteria

- At 299 idle seconds there is no prompt; at 300 seconds there is exactly one prompt.
- With no response and no activity, later five-minute buckets can issue new prompts without overlap.
- No screenshot exists for Skip, timeout, resumed activity, stopped monitoring, break, lock, policy disable, or IPC loss.
- Allow captures all connected monitors in one JPEG at most 10 MB.
- No single IPC message exceeds 65,536 characters.
- Local screenshot bytes are DPAPI protected and removed only after backend acknowledgement or enforced expiry.
- Backend retry is idempotent by attempt ID and tenant isolated by EF filters plus PostgreSQL RLS.
- R2 stores the private image; PostgreSQL stores only metadata and `FileRecordId`.
- Clock-out causes an idempotent daily-summary rebuild, while late evidence appears through live report composition.
- Daily report exposes outcome counts and evidence asset IDs without permanent URLs.
- All Agent and backend automated suites pass, plus the two-monitor/offline manual matrix.
