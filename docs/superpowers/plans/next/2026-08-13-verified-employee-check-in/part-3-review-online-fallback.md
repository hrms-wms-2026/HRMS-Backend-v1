# Verified Employee Check-In — Employer Review and Online Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give tenants explicit biometric check-in policy, let authorized employers review attendance, and support auditable `PendingReview` check-ins only when AWS or location is unavailable while the backend remains reachable.

**Architecture:** A tenant-owned policy extends the effective Tray policy and fails closed by default. Provider/location fallback uses a separate endpoint and dedicated image transfer; employer review is a cookie/RBAC flow with immutable audit records and idempotent review request IDs.

**Tech Stack:** Parts 1–2, .NET 10, MediatR, EF Core/PostgreSQL RLS, private R2 through `IFileStorageService`, MAUI native camera, typed chunked named-pipe transfer, xUnit.

**Spec:** `C:\HR\HRMS-Backend-v1\docs\superpowers\specs\next\2026-08-13-verified-employee-check-in-design.md`

## Global Constraints

- Strict verification remains the tenant default.
- Only stable failure classes `provider_unavailable` and `location_unavailable` are fallback-eligible.
- `liveness_failed`, `face_mismatch`, and spoof results are always rejected.
- A fallback attendance event is `PendingReview`, even when all other evidence is present.
- Employer review uses cookie tenant authentication plus `monitoring:attendance:review`; Tray JWT cannot call review endpoints.
- Fallback photo evidence is private, type/size/signature validated, and never sent through generic collection records.
- Employee/device identity always comes from the tray JWT; fallback requests cannot override it.

---

### Task 11: Add tenant biometric check-in policy and effective Tray contract

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\Settings\Entities\BiometricCheckInPolicy.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\Settings\BiometricCheckInPolicyConfiguration.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\ApplicationDbContext.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\DTOs\TrayAgentPolicyDto.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Queries\GetEffectiveTrayPolicy\GetEffectiveTrayPolicyQueryHandler.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\Settings\Entities\MonitoringFeatureToggles.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\ActivityMonitoring\ServiceInterfaces\IMonitoringToggleResolver.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\ActivityMonitoring\MonitoringToggleResolverService.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Queries\GetBiometricCheckInPolicy\GetBiometricCheckInPolicyQuery.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Queries\GetBiometricCheckInPolicy\GetBiometricCheckInPolicyQueryHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Commands\UpdateBiometricCheckInPolicy\UpdateBiometricCheckInPolicyCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Commands\UpdateBiometricCheckInPolicy\UpdateBiometricCheckInPolicyCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Policy\Commands\UpdateBiometricCheckInPolicy\UpdateBiometricCheckInPolicyCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\Policy\BiometricCheckInPolicyController.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Seeders\PermissionSeeder.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\Models\AgentPolicy.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\Policy\PolicyCache.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Policy\BiometricCheckInPolicyIntegrationTests.cs`
- Modify: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\PolicyCacheTests.cs`

**Interfaces:**

```csharp
public sealed record EffectiveBiometricCheckInPolicy(
    bool Required,
    bool ProviderOutageFallbackEnabled,
    bool LocationFailureFallbackEnabled,
    bool OfflineFallbackEnabled,
    int MaxAttempts,
    int LocationFreshnessSeconds,
    double MaximumLocationAccuracyMeters,
    DateTimeOffset ValidUntil);
```

Threshold values remain backend platform configuration and are not tenant-editable.

- [ ] **Step 1: Write strict-default, authorization, and serialization tests**

Assert the existing tenant `Biometric` capability defaults disabled for rollout, an authorized tenant/employee toggle enables it, missing policy row then resolves to required/strict/all-fallbacks-false, only `monitoring:configure` may update it, invalid max attempts/freshness/accuracy returns 400, and Tray policy includes a version change when biometric policy changes.

- [ ] **Step 2: Run the exact policy tests and confirm the policy is missing**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricCheckInPolicyIntegrationTests
dotnet test C:/HR/tray_app_maui/tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~PolicyCacheTests
```

- [ ] **Step 3: Implement entity and validation**

```csharp
public class BiometricCheckInPolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool Required { get; set; } = true;
    public bool ProviderOutageFallbackEnabled { get; set; }
    public bool LocationFailureFallbackEnabled { get; set; }
    public bool OfflineFallbackEnabled { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int LocationFreshnessSeconds { get; set; } = 120;
    public double MaximumLocationAccuracyMeters { get; set; } = 200;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Configure one row per tenant. Restrict `MaxAttempts` to 1–3, freshness to 30–300 seconds, and accuracy to 10–1000 metres.

- [ ] **Step 4: Add admin GET/PUT and effective Tray policy**

Use the existing `MonitoringFeatureToggles.Biometric` capability as the rollout kill switch and `BiometricCheckInPolicy` as behavior configuration. Use `TenantPolicy` and `[RequirePermission("monitoring:configure")]` for admin routes. Add fallback fields and policy validity to the existing Tray response. If the capability is disabled, Tray hides/blocks biometric capture. If it is enabled but the policy row is missing/expired, `PolicyCache` treats verification as required/strict and offline-disabled.

- [ ] **Step 5: Generate migration, document, test, and commit**

Create `Get Biometric Check-In Policy.md` and `Update Biometric Check-In Policy.md` under `docs/postman-request/Monitoring Policy/`, then run and commit each repository separately:

```powershell
dotnet ef migrations add AddBiometricCheckInPolicy --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricCheckInPolicyIntegrationTests
git add src/ONEVO.Domain/Features/Monitoring/Settings src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/ServiceInterfaces/IMonitoringToggleResolver.cs src/ONEVO.Infrastructure/Services/Monitoring/ActivityMonitoring/MonitoringToggleResolverService.cs src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Settings src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations src/ONEVO.Application/Features/Monitoring/Policy src/ONEVO.Api/Controllers/Tenant/Monitoring/Policy/BiometricCheckInPolicyController.cs src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs tests/ONEVO.Tests.Integration/Monitoring/Policy 'docs/postman-request/Monitoring Policy'
git commit -m "feat(monitoring): add biometric check-in policy"
Set-Location C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~PolicyCacheTests
git add ONEVO.Agent.Shared/Models/AgentPolicy.cs ONEVO.Agent.Service/Policy/PolicyCache.cs tests/ONEVO.Agent.Service.Tests/PolicyCacheTests.cs
git commit -m "feat(agent): enforce biometric check-in policy"
```

---

### Task 12: Add employer attendance list, detail, and idempotent review

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\CheckIn\Entities\CheckInReviewAction.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\CheckIn\CheckInReviewActionConfiguration.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\ApplicationDbContext.cs`
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\RepositoryInterfaces\ICheckInRepository.cs`
- Extend: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\CheckIn\EfCheckInRepository.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Queries\ListCheckIns\ListCheckInsQuery.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Queries\ListCheckIns\ListCheckInsQueryHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Queries\ListCheckIns\ListCheckInsQueryValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Queries\GetCheckInDetail\GetCheckInDetailQuery.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Queries\GetCheckInDetail\GetCheckInDetailQueryHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Commands\ReviewCheckIn\ReviewCheckInCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Commands\ReviewCheckIn\ReviewCheckInCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\CheckIn\Commands\ReviewCheckIn\ReviewCheckInCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\CheckIn\AttendanceCheckInsController.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Seeders\PermissionSeeder.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\CheckIn\ReviewCheckInCommandHandlerTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\CheckIn\AttendanceReviewIntegrationTests.cs`

**Interfaces:**
- List filters: employee ID, UTC range, status, and device ID with bounded page/page-size.
- Review body:

```csharp
public sealed record ReviewCheckInRequest(Guid ReviewRequestId, string Decision, string Reason);
```

- `Decision` is `approve` or `reject`; only `PendingReview` may transition.

- [ ] **Step 1: Write RBAC, RLS, filter, and review tests**

Prove `monitoring:read` can list/detail but cannot review, `monitoring:attendance:review` can review, another tenant cannot see rows, employee name/number are joined from CoreHR, double submission of one `ReviewRequestId` returns the same result, a conflicting reuse returns 409, and rejection dispatches one `attendance.check_in_rejected` notification to the employee user.

- [ ] **Step 2: Run tests and confirm missing endpoints**

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ReviewCheckInCommandHandlerTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~AttendanceReviewIntegrationTests
```

- [ ] **Step 3: Implement immutable review audit**

```csharp
public class CheckInReviewAction : ITenantOwnedEntity
{
    public Guid Id { get; set; }              // ReviewRequestId
    public Guid TenantId { get; set; }
    public Guid CheckInId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; }
}
```

Approve sets attendance status to `Verified` while preserving fallback reason and audit source. Reject sets it to `Rejected`. Do the status update and audit insert atomically; use an optimistic concurrency token or conditional update so two reviewers cannot both win. After the rejection transaction commits, use existing `INotificationDispatcher.SendToUserAsync` with event `attendance.check_in_rejected`, check-in ID, reason, and review time. Notification retry must not repeat the status mutation.

- [ ] **Step 4: Implement list/detail DTOs without biometric media URLs**

Return check-in ID, employee ID/number/name, checked-in time, coordinates/accuracy/location code, registered device ID/name, verification status, attempt scores/status, fallback reason, and review history. Do not expose reference images or temporary AWS data.

- [ ] **Step 5: Generate migration, seed permission, document, test, and commit**

Create `List Check-Ins.md`, `Get Check-In Detail.md`, `Approve Check-In.md`, and `Reject Check-In.md` under `docs/postman-request/Monitoring Check-In/`, then run and commit:

```powershell
dotnet ef migrations add AddAttendanceReview --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~ReviewCheckInCommandHandlerTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~AttendanceReviewIntegrationTests
git add src/ONEVO.Domain/Features/Monitoring/CheckIn src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations src/ONEVO.Application/Features/Monitoring/CheckIn src/ONEVO.Api/Controllers/Tenant/Monitoring/CheckIn/AttendanceCheckInsController.cs src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn tests/ONEVO.Tests.Integration/Monitoring/CheckIn 'docs/postman-request/Monitoring Check-In'
git commit -m "feat(monitoring): review employee check-ins"
```

---

### Task 13: Implement backend-online provider/location fallback evidence

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\SubmitFallbackCheckIn\SubmitFallbackCheckInCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\SubmitFallbackCheckIn\SubmitFallbackCheckInCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\SubmitFallbackCheckIn\SubmitFallbackCheckInCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\ServiceInterfaces\IBiometricEvidenceValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\BiometricEvidenceValidator.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Storage\File\Helpers\UploadPurposeCatalog.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Storage\File\UploadPurposePolicy.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\Biometrics\BiometricCheckInController.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\FallbackCheckInIntegrationTests.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Shared\IPC\BiometricEvidenceTransferMessages.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\INamedPipeClient.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\NamedPipeClient.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\BiometricFallbackUploader.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.Service\CheckIn\CheckInCoordinator.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\VerifiedCheckInViewModel.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Shared.Tests\BiometricEvidenceTransferMessageTests.cs`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.Service.Tests\CheckIn\BiometricFallbackUploaderTests.cs`

**Interfaces:**
- Eligible online reason is exactly `provider_unavailable` or `location_unavailable`; Part 4 adds the separately validated `offline_policy_authorized` path.
- Backend endpoint is multipart with immutable `AttendanceSessionId`, attempt/reason/location metadata, and one JPEG/PNG still photo when provider outage requires evidence.
- Successful response is always `PendingReview`.

- [ ] **Step 1: Write fallback eligibility and evidence-security tests**

Cover strict-policy block, enabled-policy success, spoof/mismatch ineligibility, actual JPEG/PNG magic bytes, image decode/dimension limit, declared type mismatch, 5 MB limit, another device's attempt, duplicate attendance ID, and R2-private metadata.

- [ ] **Step 2: Run tests and confirm missing fallback endpoint/IPC**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~FallbackCheckInIntegrationTests
dotnet test C:/HR/tray_app_maui/tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter FullyQualifiedName~BiometricEvidenceTransferMessageTests
dotnet test C:/HR/tray_app_maui/tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~BiometricFallbackUploaderTests
```

- [ ] **Step 3: Implement backend fallback command**

Re-resolve JWT identity and current policy. Require that the associated attempt has `ProviderError` for provider fallback; never infer eligibility from client text. For location fallback, require a policy-approved location failure state and any available coordinates. Validate decoded image signature/dimensions, upload using purpose `biometric_pending_review_evidence`, create the idempotent `EmployeeCheckIn` as `PendingReview`, and link its private evidence file ID in a dedicated field/entity.

- [ ] **Step 4: Implement bounded dedicated media transfer**

Use start/chunk/complete envelopes with 32 KiB raw chunks, total size and SHA-256. The Service accepts the transfer only for its current fallback attempt and posts it immediately to the reachable backend. It does not enqueue it in `ActivityRecordBuffer`. If upload fails, remain `Stopped`, clear transient bytes, and offer retry; Part 4 owns durable offline storage.

- [ ] **Step 5: Wire native photo and pending-review activation**

Use existing `ICameraService` only for this fallback still photo. On backend `PendingReview`, Service may transition to Active only when the effective tenant policy explicitly allows that fallback reason. UI shows “Clocked in — pending employer review,” not “Face verified.”

- [ ] **Step 6: Document, run tests, and commit each repository**

Create `docs/postman-request/Monitoring Biometrics/Submit Fallback Check-In.md`. Run all fallback tests plus strict-online coordinator tests to prove mismatch/spoof cannot take the fallback branch, then commit each repository separately:

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~FallbackCheckInIntegrationTests
git add src/ONEVO.Application/Features/Monitoring/Biometrics src/ONEVO.Infrastructure/Services/Monitoring/Biometrics src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/BiometricCheckInController.cs tests/ONEVO.Tests.Integration/Monitoring/Biometrics 'docs/postman-request/Monitoring Biometrics/Submit Fallback Check-In.md'
git commit -m "feat(monitoring): accept review-only check-in fallback"
Set-Location C:\HR\tray_app_maui
dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj --filter FullyQualifiedName~BiometricEvidenceTransferMessageTests
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~BiometricFallbackUploaderTests|FullyQualifiedName~CheckInCoordinatorTests"
git add ONEVO.Agent.Shared/IPC/BiometricEvidenceTransferMessages.cs ONEVO.Agent.TrayApp/Services ONEVO.Agent.TrayApp/ViewModels/VerifiedCheckInViewModel.cs ONEVO.Agent.Service/CheckIn tests
git commit -m "feat(agent): submit review-only check-in fallback"
```
