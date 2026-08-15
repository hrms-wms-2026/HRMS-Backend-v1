# Verified Employee Check-In — Plan 1: Foundation & Windows Compatibility — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the foundational backend + Windows client capability required before the strict online verified check-in flow (Plan 2) can exist: the AWS Rekognition Face Liveness provider abstraction with IAM-role credentials, the biometric database model, real `EmployeeId` identity resolution, a validated Windows/WebView2/AWS-Mumbai camera compatibility gate, and the full biometric **enrollment** flow (backend endpoints + TrayApp/Service capture UI).

**Source design doc:** [`docs/superpowers/specs/next/2026-08-13-verified-employee-check-in-design.md`](../specs/next/2026-08-13-verified-employee-check-in-design.md) — approved in chat. This plan implements only its "Foundation and Windows compatibility" decomposition item (design doc §Implementation Decomposition and Order, item 1). Plans 2–4 (strict online check-in, employer review/fallbacks, offline fallback) are separate future plans and are **out of scope here** — do not build check-in submission, CLOCK IN gating, or `EmployeeCheckIn`/`EmployeeWorkSession` schema changes in this plan.

**Architecture:** Two repos change together. `C:\HR\HRMS-Backend-v1` gets a new `Features/Monitoring/Biometrics` vertical slice (Clean Architecture + CQRS/MediatR, same shape as the existing `Features/Monitoring/CheckIn` slice) plus an `IBiometricVerificationProvider` Infrastructure adapter over AWS Rekognition. `C:\HR\tray_app_maui` gets a new WebView2-hosted capture page in `ONEVO.Agent.TrayApp`, a small `EnrollmentCoordinator` in `ONEVO.Agent.Service`, and new typed IPC contracts in `ONEVO.Agent.Shared` — following the existing Service-owns-JWT / Tray-owns-UI boundary. Nothing in this plan touches the CLOCK IN button, `LifecycleCommandPayload`, or any existing check-in code path.

**Tech Stack:** ASP.NET Core 10 / EF Core 10 / PostgreSQL / MediatR / FluentValidation (backend); `AWSSDK.Rekognition` + `AWSSDK.SecurityToken` added to `ONEVO.Infrastructure` (backend, credentials via ambient IAM role — no static keys); .NET MAUI Windows (`net10.0-windows10.0.19041.0`) + `Microsoft.Web.WebView2` (TrayApp); a minimal packaged React `FaceLivenessDetector` bundle served through a WebView2 virtual host origin.

---

## Before you start

Read these in full — this plan assumes their contents:

- [`ONEVO_Backend_Architecture_Document (2).md`](../../../../ONEVO_Backend_Architecture_Document%20%282%29.md) or the `onevo-backend-architecture` skill.
- [`ONEVO_Agent_Architecture_Flow_Folder_Structure.md`](../../../../ONEVO_Agent_Architecture_Flow_Folder_Structure.md) or the `onevo-maui-trayapp` skill — especially §4.2 (IPC trust boundary), §7.5 (screenshots/photos are a restricted upload flow, never general IPC/logs), §8.2 (credential storage), §20 (new feature checklist).
- The design doc linked above in full, especially "Locked Decisions," "Identity Contract," "Windows Camera Compatibility Gate," and "AWS Credentials and Security."

Non-negotiables this plan must satisfy (carried over from the design doc, restated here so no task accidentally violates them):

- The Tray never sends `isLive`/`faceMatched` booleans the backend trusts — the backend always re-derives the verdict from AWS results.
- No biometric image or video bytes ever cross the generic Named Pipe IPC channel or the ordinary `ActivityRecordBuffer`/SQLite queue used by `ActivitySnapshot`/`AppUsage`/`DeviceState`. This plan's enrollment flow streams video **directly from the browser (WebView2) to AWS**, not through the Service's existing collector pipeline.
- AWS credentials for the capture client are scoped (15 min, `rekognition:StartFaceLivenessSession` only, `ap-south-1` only) and exist only in memory — never in `Preferences`, SQLite, files, or logs.
- Backend compute uses its ambient IAM role for all AWS calls — no access key/secret in `appsettings.json` or source.
- `Environment.MachineName` is informational only; it is never treated as an authorization identity (existing bug — do not copy it into any new code this plan adds).

---

## File Structure

### Backend (`C:\HR\HRMS-Backend-v1`)

```
src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/
  BiometricVerificationAttempt.cs      (new — state-machine entity)
  BiometricAttemptStatus.cs            (new — status constants)
  BiometricAttemptPurpose.cs           (new — "enrollment" | "check_in")
  EmployeeBiometricProfile.cs          (new)
  BiometricProfileStatus.cs            (new — Active/Superseded/Revoked/Deleted)

src/ONEVO.Application/Common/ServiceInterfaces/
  IEmployeeIdentityResolver.cs         (new — reusable, not biometrics-specific)

src/ONEVO.Application/Features/Monitoring/Biometrics/
  RepositoryInterfaces/IBiometricRepository.cs           (new)
  ServiceInterfaces/IBiometricVerificationProvider.cs     (new)
  Commands/CreateEnrollmentAttempt/{Command,Handler,Validator}.cs   (new)
  Commands/CompleteEnrollmentAttempt/{Command,Handler,Validator}.cs (new)
  Queries/GetBiometricProfile/{Query,Handler}.cs                    (new)
  DTOs/Responses/{EnrollmentAttemptResponseDto,BiometricProfileResponseDto}.cs (new)

src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Biometrics/
  BiometricVerificationAttemptConfiguration.cs  (new)
  EmployeeBiometricProfileConfiguration.cs      (new)

src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Biometrics/
  EfBiometricRepository.cs             (new)

src/ONEVO.Infrastructure/Services/Common/
  EfEmployeeIdentityResolver.cs        (new)

src/ONEVO.Infrastructure/ExternalServices/Biometrics/
  AwsRekognitionBiometricVerificationProvider.cs  (new)
  BiometricProviderOptions.cs                     (new)

src/ONEVO.Infrastructure/Migrations/
  {timestamp}_AddBiometricVerification.cs  (new)

src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj   (modify — add AWSSDK.Rekognition, AWSSDK.SecurityToken)
src/ONEVO.Infrastructure/DependencyInjection.cs        (modify — register new services)
src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs  (modify — add MonitoringFaceLiveness purpose)
src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/MonitoringBiometricsController.cs  (new)
src/ONEVO.Api/appsettings.json  (modify — non-secret Biometrics:* section)

tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/
  BiometricVerificationAttemptTests.cs             (new)
  CreateEnrollmentAttemptCommandHandlerTests.cs     (new)
  CompleteEnrollmentAttemptCommandHandlerTests.cs   (new)
  GetBiometricProfileQueryHandlerTests.cs           (new)

tests/ONEVO.Tests.Integration/Monitoring/Biometrics/
  BiometricsTestFactory.cs             (new — includes FakeBiometricVerificationProvider)
  BiometricsIntegrationTests.cs        (new)
```

### MAUI Agent (`C:\HR\tray_app_maui`)

```
ONEVO.Agent.Shared/IPC/IpcMessages.cs         (modify — add enrollment-liveness IPC types)

ONEVO.Agent.Service/Api/AgentApiRoutes.cs     (modify — add biometrics routes)
ONEVO.Agent.Service/Api/OnevoApiClient.cs     (modify — add enrollment-attempt HTTP methods)
ONEVO.Agent.Service/Biometrics/EnrollmentCoordinator.cs  (new)
ONEVO.Agent.Service/AgentWorker.cs            (modify — dispatch new IPC messages to EnrollmentCoordinator)
ONEVO.Agent.Service/Program.cs                (modify — DI registration)

ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs   (modify — add enrollment round-trip methods)
ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs    (modify — implement them)
ONEVO.Agent.TrayApp/ViewModels/BiometricEnrollmentViewModel.cs  (new)
ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml            (new)
ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml.cs         (new)
ONEVO.Agent.TrayApp/Views/AppShell.xaml       (modify — add "enrollment-biometric" route)
ONEVO.Agent.TrayApp/Platforms/Windows/BiometricWebViewSetup.cs    (new — WebView2 virtual host + permission gate)
ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj (modify — add Microsoft.Web.WebView2)
ONEVO.Agent.TrayApp/wwwroot/biometric/         (new — packaged React FaceLivenessDetector build output)

Directory.Packages.props                       (modify — pin Microsoft.Web.WebView2)

tests/ONEVO.Agent.Service.Tests/Biometrics/EnrollmentCoordinatorTests.cs      (new)
tests/ONEVO.Agent.TrayApp.Tests/ViewModels/BiometricEnrollmentViewModelTests.cs (new)
```

---

## Task 0: Windows Camera Compatibility Gate (disposable probe — go/no-go)

This is the design doc's explicitly **disposable** spike (design doc §Windows Camera Compatibility Gate). It is not production code and does not get bite-sized TDD steps — it produces one artifact: a written pass/fail decision. **Do not proceed to Task 1 until this passes.** If it fails, stop and escalate — the whole design assumes it passes.

**Files:**
- Create (throwaway, delete after decision recorded): `tray_app_maui/spikes/webview2-liveness-probe/` (a standalone minimal MAUI Windows app or a throwaway branch of `ONEVO.Agent.TrayApp`)
- Create (keep): `docs/superpowers/plans/2026-08-13-camera-compatibility-gate-result.md` in `tray_app_maui` — the decision record

- [ ] **Step 1: Get AWS Amplify's packaged `FaceLivenessDetector` build**

Follow <https://ui.docs.amplify.aws/react/connected-components/liveness> to scaffold a minimal React app containing only `<FaceLivenessDetector>` from `@aws-amplify/ui-react-liveness`, pointed at a placeholder `sessionId`/region/credentials (hardcode dummy values for now — real wiring comes from Task 20). Run `npm run build` to produce static `dist/` assets.

- [ ] **Step 2: Host the build inside the probe app via WebView2 virtual host**

In the throwaway MAUI Windows app, add `Microsoft.Web.WebView2` and register a virtual host mapping:

```csharp
coreWebView2.SetVirtualHostNameToFolderMapping(
    "biometric.onevo.local",
    Path.Combine(AppContext.BaseDirectory, "wwwroot", "biometric"),
    CoreWebView2HostResourceAccessKind.DenyCors);

webView.Source = new Uri("https://biometric.onevo.local/index.html");
```

Do **not** load via `file://` — camera APIs (`getUserMedia`) are restricted or blocked on `file://` origins in Chromium.

- [ ] **Step 3: Handle `CoreWebView2.PermissionRequested` — allow only `Camera` on the exact origin**

```csharp
coreWebView2.PermissionRequested += (sender, args) =>
{
    var isBiometricOrigin = args.Uri.StartsWith("https://biometric.onevo.local", StringComparison.Ordinal);
    var isCameraRequest = args.PermissionKind == CoreWebView2PermissionKind.Camera;

    args.State = (isBiometricOrigin && isCameraRequest)
        ? CoreWebView2PermissionState.Allow
        : CoreWebView2PermissionState.Deny;
    args.Handled = true;
};
```

- [ ] **Step 4: Enumerate cameras and prefer the built-in/front camera**

Use `getUserMedia`/`enumerateDevices` inside the React app (or a small JS bridge) to list video input devices, reject any labeled as a known virtual-camera product (OBS Virtual Camera, Snap Camera, etc.), and prefer a device whose label contains "Integrated"/"Built-in"/"Front" when multiple are present.

- [ ] **Step 5: Record effective resolution and frame rate — do not save video**

Log (to console/debug output only, not disk) the negotiated `MediaStreamTrack.getSettings()` — `width`, `height`, `frameRate` — for each test run. Confirm ≥480x640, ≥15 FPS per AWS's documented minimums.

- [ ] **Step 6: Run one real staging Face Liveness session in `ap-south-1`**

Stand up a temporary Rekognition Face Liveness session in a staging AWS account (via AWS CLI `create-face-liveness-session` or the console) purely to get a real `sessionId` + short-lived credentials, plug them into the probe app, and complete one real capture end-to-end. Confirm a `GetFaceLivenessSessionResults` call afterward returns `SUCCEEDED` with a confidence score.

- [ ] **Step 7: Exercise every failure path**

Test and record the outcome for each: camera permission denied, camera already open in Teams/Zoom, no camera present, throttled/slow network, low light, an external USB webcam attached, mid-session cancellation, and app restart mid-session. None should crash the host process; each should surface a distinguishable error state in the WebView console.

- [ ] **Step 8: Test on 3–5 representative laptop models across Windows 10 and 11**

Run steps 2–7 on at least 3 physical (or realistic VM-with-passthrough) machines spanning both Windows 10 and Windows 11, different webcam vendors if possible.

- [ ] **Step 9: Write the decision record and delete the spike**

Create `docs/superpowers/plans/2026-08-13-camera-compatibility-gate-result.md` with: machines tested, pass/fail per step above, any AWS requirement that failed (front camera / 15 FPS / 480x640 / 60 Hz display / 4-inch screen / 100 kbps), and an explicit **GO** or **NO-GO** line. Delete `tray_app_maui/spikes/webview2-liveness-probe/` — its only job was to produce this decision. If GO, continue to Task 1. If NO-GO, stop this plan and escalate to the design owner before writing any more production code.

---

## Task 1: AWS IAM role and KMS key provisioning

**Files:**
- Create: `docs/superpowers/plans/2026-08-13-aws-biometric-infra-setup.md` (infra decision/runbook record, backend repo)

This is infrastructure provisioning, not application code — record exact commands run so they're reproducible, but there is nothing to unit-test here.

- [ ] **Step 1: Create the backend compute IAM role's Rekognition/KMS policy**

Attach (or create, if the backend doesn't already run under a dedicated role) a policy granting only:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "RekognitionLivenessControlPlane",
      "Effect": "Allow",
      "Action": [
        "rekognition:CreateFaceLivenessSession",
        "rekognition:GetFaceLivenessSessionResults",
        "rekognition:CompareFaces"
      ],
      "Resource": "*"
    },
    {
      "Sid": "KmsForLivenessSession",
      "Effect": "Allow",
      "Action": ["kms:GenerateDataKey", "kms:Decrypt"],
      "Resource": "arn:aws:kms:ap-south-1:<account-id>:key/<key-id>"
    }
  ]
}
```

`rekognition:CreateFaceLivenessSession`/`GetFaceLivenessSessionResults`/`CompareFaces` do not support resource-level restriction (`Resource: "*"` is required by AWS) — this is a known, documented AWS limitation, not a mistake. Compensate with the dedicated-role isolation and the 15-minute credential scoping in Task 2.

- [ ] **Step 2: Create the narrow capture-client role for `StartFaceLivenessSession`**

Create a **separate** IAM role (not the backend compute role) with only:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "StartLivenessSessionOnly",
      "Effect": "Allow",
      "Action": "rekognition:StartFaceLivenessSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": { "aws:RequestedRegion": "ap-south-1" }
      }
    }
  ]
}
```

Add a trust policy allowing the backend compute role to `sts:AssumeRole` into this role. Record the resulting Role ARN — this is the `CaptureRoleArn` used in Task 13's config.

- [ ] **Step 3: Create the KMS key for liveness session encryption**

Create a customer-managed KMS key in `ap-south-1` scoped to Rekognition liveness session encryption per <https://docs.aws.amazon.com/rekognition/latest/dg/face-liveness.html>. Record its key ID — this is `KmsKeyId` in Task 13's config. Do **not** configure an S3 output bucket for the liveness session (design doc: "does not configure AWS S3 output").

- [ ] **Step 4: Enable the AWS Organizations AI-services opt-out policy, if applicable**

Per the design doc's Privacy section — apply where the organization's AWS setup supports it.

- [ ] **Step 5: Write the runbook**

Record every ARN, key ID, and exact CLI/console step taken in `docs/superpowers/plans/2026-08-13-aws-biometric-infra-setup.md` so it can be reproduced in a second environment (staging vs. production).

---

## Task 2: Add AWS SDK package references

**Files:**
- Modify: `src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`

- [ ] **Step 1: Add the packages**

```bash
cd C:\HR\HRMS-Backend-v1
dotnet add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj package AWSSDK.Rekognition
dotnet add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj package AWSSDK.SecurityToken
```

This repo has no `Directory.Packages.props` (confirmed — every `.csproj` pins its own version inline, same style as the existing `AWSSDK.S3` reference), so `dotnet add package` resolving and writing the version directly into the `.csproj` is correct and matches convention — do not hand-type a guessed version number.

- [ ] **Step 2: Verify the build still compiles**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
git commit -m "chore: add AWSSDK.Rekognition and AWSSDK.SecurityToken package references"
```

---

## Task 3: `BiometricVerificationAttempt` domain entity with a validated state machine

This is the plan's first real TDD task — a pure-logic state-transition guard, same shape as `AgentStateMachine.IsValidTransition` on the MAUI side, kept in Domain per the "Helpers must be pure logic only" rule.

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricAttemptStatus.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricAttemptPurpose.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricVerificationAttempt.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/BiometricVerificationAttemptTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class BiometricVerificationAttemptTests
{
    private static BiometricVerificationAttempt NewAttempt() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        DeviceRegistrationId = Guid.NewGuid(),
        Purpose = BiometricAttemptPurpose.Enrollment,
        Status = BiometricAttemptStatus.Created,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Theory]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Capturing, true)]
    [InlineData(BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Verifying, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Verified, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Rejected, true)]
    [InlineData(BiometricAttemptStatus.Verifying, BiometricAttemptStatus.ProviderError, true)]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Expired, true)]
    [InlineData(BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Expired, true)]
    [InlineData(BiometricAttemptStatus.Verified, BiometricAttemptStatus.Capturing, false)]
    [InlineData(BiometricAttemptStatus.Rejected, BiometricAttemptStatus.Verified, false)]
    [InlineData(BiometricAttemptStatus.Created, BiometricAttemptStatus.Verified, false)]
    [InlineData(BiometricAttemptStatus.Expired, BiometricAttemptStatus.Capturing, false)]
    public void TryTransition_EnforcesAllowedStateGraph(string from, string to, bool expectedAllowed)
    {
        var attempt = NewAttempt();
        attempt.Status = from;

        var allowed = attempt.TryTransition(to, out var previous);

        Assert.Equal(expectedAllowed, allowed);
        Assert.Equal(from, previous);
        Assert.Equal(expectedAllowed ? to : from, attempt.Status);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~BiometricVerificationAttemptTests"
```

Expected: FAIL — `BiometricAttemptStatus`/`BiometricAttemptPurpose`/`BiometricVerificationAttempt` do not exist yet.

- [ ] **Step 3: Create the status and purpose constants**

`src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricAttemptStatus.cs`:
```csharp
namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public static class BiometricAttemptStatus
{
    public const string Created      = "created";
    public const string Capturing    = "capturing";
    public const string Verifying    = "verifying";
    public const string Verified     = "verified";
    public const string Rejected     = "rejected";
    public const string ProviderError = "provider_error";
    public const string Expired      = "expired";
}
```

`src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricAttemptPurpose.cs`:
```csharp
namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public static class BiometricAttemptPurpose
{
    public const string Enrollment = "enrollment";
    public const string CheckIn    = "check_in";
}
```

- [ ] **Step 4: Implement `BiometricVerificationAttempt` with the transition guard**

`src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricVerificationAttempt.cs`:
```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public class BiometricVerificationAttempt : ITenantOwnedEntity
{
    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [BiometricAttemptStatus.Created]   = [BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Expired],
        [BiometricAttemptStatus.Capturing] = [BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Expired, BiometricAttemptStatus.ProviderError],
        [BiometricAttemptStatus.Verifying] = [BiometricAttemptStatus.Verified, BiometricAttemptStatus.Rejected, BiometricAttemptStatus.ProviderError, BiometricAttemptStatus.Expired],
        [BiometricAttemptStatus.Verified]     = [],
        [BiometricAttemptStatus.Rejected]     = [],
        [BiometricAttemptStatus.ProviderError] = [],
        [BiometricAttemptStatus.Expired]      = []
    };

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    public string Purpose { get; set; } = BiometricAttemptPurpose.Enrollment;

    /// <summary>Set only when Purpose == CheckIn (Plan 2). Null for enrollment attempts.</summary>
    public Guid? AttendanceSessionId { get; set; }

    public string? AwsSessionId { get; set; }
    public string AwsRegion { get; set; } = "ap-south-1";
    public string ChallengeType { get; set; } = "FaceMovementAndLightChallenge";
    public DateTimeOffset? AwsSessionExpiresAt { get; set; }

    public string Status { get; set; } = BiometricAttemptStatus.Created;
    public double? LivenessConfidence { get; set; }
    public double? MatchConfidence { get; set; }
    public string? FailureCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Enforces the Created -> Capturing -> Verifying -> {Verified|Rejected|ProviderError}
    /// graph (plus Expired from any non-terminal state). Mirrors AgentStateMachine's
    /// validated-transition pattern on the Tray side so an invalid handler code path
    /// fails loudly instead of silently corrupting attempt state.
    /// </summary>
    public bool TryTransition(string target, out string previous)
    {
        previous = Status;
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(target))
            return false;

        Status = target;
        return true;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~BiometricVerificationAttemptTests"
```

Expected: PASS (11 theory cases).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/Biometrics tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics
git commit -m "feat: add BiometricVerificationAttempt entity with validated state transitions"
```

---

## Task 4: `EmployeeBiometricProfile` domain entity

**Files:**
- Create: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfileStatus.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/EmployeeBiometricProfile.cs`

No new test here — this entity is a plain data holder like `EmployeeCheckIn`/`MonitoringFaceScan` (no business-rule methods), consistent with how those two are implemented (§existing recon). Its correctness is exercised by the handler tests in Task 12/13.

- [ ] **Step 1: Create the status constants**

`src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfileStatus.cs`:
```csharp
namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public static class BiometricProfileStatus
{
    public const string Active     = "active";
    public const string Superseded = "superseded";
    public const string Revoked    = "revoked";
    public const string Deleted    = "deleted";
}
```

- [ ] **Step 2: Create the entity**

`src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/EmployeeBiometricProfile.cs`:
```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

/// <summary>
/// The trusted enrollment reference for CompareFaces during daily check-in (Plan 2).
/// Exactly one row per (TenantId, EmployeeId) may have Status == Active at a time —
/// enforced by a partial unique index (Task 6), not by application logic alone.
/// </summary>
public class EmployeeBiometricProfile : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }

    public string Provider { get; set; } = "aws_rekognition";
    public string Region { get; set; } = "ap-south-1";

    /// <summary>R2 storage key of the private reference image (design: "private and encrypted in R2").</summary>
    public string ReferenceStorageKey { get; set; } = string.Empty;

    public string Status { get; set; } = BiometricProfileStatus.Active;

    public string ConsentVersion { get; set; } = string.Empty;
    public DateTimeOffset ConsentAcceptedAt { get; set; }

    public Guid EnrollmentAttemptId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfileStatus.cs src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/EmployeeBiometricProfile.cs
git commit -m "feat: add EmployeeBiometricProfile entity"
```

---

## Task 5: EF configurations for both entities

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Biometrics/BiometricVerificationAttemptConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Biometrics/EmployeeBiometricProfileConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Add `DbSet` properties**

In `ApplicationDbContext.cs`, next to the existing `EmployeeCheckIns`/`MonitoringFaceScans` `DbSet`s, add:

```csharp
public DbSet<BiometricVerificationAttempt> BiometricVerificationAttempts => Set<BiometricVerificationAttempt>();
public DbSet<EmployeeBiometricProfile> EmployeeBiometricProfiles => Set<EmployeeBiometricProfile>();
```

(Add the corresponding `using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;` at the top if not already covered by a wildcard-style using block — follow whatever the file already does for the CheckIn entities' using statement.)

- [ ] **Step 2: Write `BiometricVerificationAttemptConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class BiometricVerificationAttemptConfiguration : IEntityTypeConfiguration<BiometricVerificationAttempt>
{
    public void Configure(EntityTypeBuilder<BiometricVerificationAttempt> builder)
    {
        builder.ToTable("biometric_verification_attempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Purpose).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.AwsSessionId).HasMaxLength(200);
        builder.Property(a => a.AwsRegion).HasMaxLength(20).IsRequired();
        builder.Property(a => a.ChallengeType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.FailureCode).HasMaxLength(100);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.Purpose, a.CreatedAt });
        builder.HasIndex(a => new { a.TenantId, a.AwsSessionId }).IsUnique();
    }
}
```

- [ ] **Step 3: Write `EmployeeBiometricProfileConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Biometrics;

public class EmployeeBiometricProfileConfiguration : IEntityTypeConfiguration<EmployeeBiometricProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeBiometricProfile> builder)
    {
        builder.ToTable("employee_biometric_profiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Region).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ReferenceStorageKey).HasMaxLength(1000).IsRequired();
        builder.Property(p => p.Status).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ConsentVersion).HasMaxLength(20).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId });

        // Exactly one Active profile per (tenant, employee) — Postgres partial unique index.
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
               .IsUnique()
               .HasFilter("status = 'active'")
               .HasDatabaseName("ix_employee_biometric_profiles_tenant_employee_active");
    }
}
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat: add EF configurations for biometric verification attempt and profile"
```

---

## Task 6: Migration — create both tables with RLS

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/{timestamp}_AddBiometricVerification.cs` (generated)

- [ ] **Step 1: Generate the migration**

```bash
cd C:\HR\HRMS-Backend-v1
dotnet ef migrations add AddBiometricVerification --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

- [ ] **Step 2: Open the generated migration and add the RLS block**

Copy the exact `TenantTables` + `Sql($@"...")` loop pattern from `20260804095537_AddMonitoringCheckIn.cs` (Up and Down), retargeted to the two new tables. At the end of the generated `Up(MigrationBuilder migrationBuilder)` method, add:

```csharp
        private static readonly string[] TenantTables =
        [
            "biometric_verification_attempts",
            "employee_biometric_profiles"
        ];
```
(as a field on the migration class, same as the existing migration), and inside `Up`, after the generated `CreateTable`/`CreateIndex` calls:

```csharp
            // PostgreSQL RLS — tenant isolation on both biometric tables
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
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
                ");
            }
```

And in `Down(MigrationBuilder migrationBuilder)`, **before** the generated `DropTable` calls:

```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
```

- [ ] **Step 3: Verify the migration applies cleanly against a real dev database**

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: migration applies with no errors. Confirm the partial unique index from Task 5 Step 3 landed correctly:

```bash
psql -d <dev-db> -c "\d employee_biometric_profiles"
```

Expected: `ix_employee_biometric_profiles_tenant_employee_active` shown as `UNIQUE, WHERE (status = 'active'::text)`.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations
git commit -m "feat: add biometric_verification_attempts and employee_biometric_profiles tables with RLS"
```

---

## Task 7: `IBiometricRepository` + EF implementation

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/RepositoryInterfaces/IBiometricRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Biometrics/EfBiometricRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

No dedicated unit test for the EF repository itself — this repo's convention (confirmed: `EfCheckInRepository` has zero unit tests) is to exercise repositories only through integration tests (Task 15). Handler-level unit tests in Task 12/13 mock this interface directly.

- [ ] **Step 1: Define the interface**

```csharp
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;

public interface IBiometricRepository
{
    Task AddAttemptAsync(BiometricVerificationAttempt attempt, CancellationToken ct);
    Task<BiometricVerificationAttempt?> FindAttemptAsync(Guid attemptId, Guid tenantId, CancellationToken ct);

    Task<EmployeeBiometricProfile?> FindActiveProfileAsync(Guid employeeId, Guid tenantId, CancellationToken ct);
    Task AddProfileAsync(EmployeeBiometricProfile profile, CancellationToken ct);

    /// <summary>Marks the current Active profile (if any) Superseded. No-op if none exists.</summary>
    Task SupersedeActiveProfileAsync(Guid employeeId, Guid tenantId, DateTimeOffset supersededAt, CancellationToken ct);
}
```

- [ ] **Step 2: Implement it**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics;

public class EfBiometricRepository : IBiometricRepository
{
    private readonly ApplicationDbContext _db;

    public EfBiometricRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAttemptAsync(BiometricVerificationAttempt attempt, CancellationToken ct)
        => await _db.BiometricVerificationAttempts.AddAsync(attempt, ct);

    public async Task<BiometricVerificationAttempt?> FindAttemptAsync(Guid attemptId, Guid tenantId, CancellationToken ct)
        => await _db.BiometricVerificationAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.TenantId == tenantId, ct);

    public async Task<EmployeeBiometricProfile?> FindActiveProfileAsync(Guid employeeId, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeBiometricProfiles
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId
                                    && p.TenantId == tenantId
                                    && p.Status == BiometricProfileStatus.Active, ct);

    public async Task AddProfileAsync(EmployeeBiometricProfile profile, CancellationToken ct)
        => await _db.EmployeeBiometricProfiles.AddAsync(profile, ct);

    public async Task SupersedeActiveProfileAsync(Guid employeeId, Guid tenantId, DateTimeOffset supersededAt, CancellationToken ct)
    {
        await _db.EmployeeBiometricProfiles
            .Where(p => p.EmployeeId == employeeId && p.TenantId == tenantId && p.Status == BiometricProfileStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, BiometricProfileStatus.Superseded)
                .SetProperty(p => p.SupersededAt, supersededAt)
                .SetProperty(p => p.UpdatedAt, supersededAt), ct);
    }
}
```

- [ ] **Step 3: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, next to the existing `// Monitoring - Check-In` block:

```csharp
        // Monitoring - Biometrics
        services.AddScoped<IBiometricRepository, EfBiometricRepository>();
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Biometrics src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: add IBiometricRepository and EF implementation"
```

---

## Task 8: `IEmployeeIdentityResolver` — real `EmployeeId` from tenant + user

This closes the design doc's Identity Contract gap: today only `TrayEmployeeProfile` (name/email/number — display fields) is resolvable; nothing returns the actual `Employee.Id` GUID. This resolver is what lets Task 12/13 stamp a real `EmployeeId` onto biometric rows instead of only `UserId`.

**Files:**
- Create: `src/ONEVO.Application/Common/ServiceInterfaces/IEmployeeIdentityResolver.cs`
- Create: `src/ONEVO.Infrastructure/Services/Common/EfEmployeeIdentityResolver.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/BiometricsIntegrationTests.cs` (covered together with Task 15 — the resolver has no meaningful pure-logic branch to unit-test in isolation; its only behavior is an EF query, so it's exercised end-to-end via the enrollment integration tests, matching this repo's existing convention of not unit-testing EF repositories directly)

- [ ] **Step 1: Define the interface**

```csharp
namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Resolves the real CoreHR Employee.Id (Guid) for the authenticated tray user, distinct from
/// UserId (the auth-account identifier). Callers must already be running inside the correct
/// tenant context (see ITenantContextSwitcher) before calling this — it applies no tenant switch
/// of its own.
/// </summary>
public interface IEmployeeIdentityResolver
{
    Task<Guid?> ResolveEmployeeIdAsync(Guid userId, Guid tenantId, CancellationToken ct);
}
```

- [ ] **Step 2: Implement it against CoreHR `Employee`**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Common;

public class EfEmployeeIdentityResolver : IEmployeeIdentityResolver
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeIdentityResolver(ApplicationDbContext db) => _db = db;

    public async Task<Guid?> ResolveEmployeeIdAsync(Guid userId, Guid tenantId, CancellationToken ct)
        => await _db.Employees
            .Where(e => e.UserId == userId && e.TenantId == tenantId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);
}
```

- [ ] **Step 3: Register in DI**

```csharp
services.AddScoped<IEmployeeIdentityResolver, EfEmployeeIdentityResolver>();
```

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IEmployeeIdentityResolver.cs src/ONEVO.Infrastructure/Services/Common/EfEmployeeIdentityResolver.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: add IEmployeeIdentityResolver to resolve real Employee.Id from tenant+user"
```

---

## Task 9: `IBiometricVerificationProvider` abstraction + DTOs

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/ServiceInterfaces/IBiometricVerificationProvider.cs`

- [ ] **Step 1: Define the interface and its DTOs**

```csharp
namespace ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

public interface IBiometricVerificationProvider
{
    /// <summary>Control-plane call: creates a new AWS Face Liveness session. Returns the AWS session id.</summary>
    Task<FaceLivenessSessionCreated> CreateLivenessSessionAsync(
        CreateLivenessSessionRequest request, CancellationToken ct);

    /// <summary>Fetches the outcome of a completed (or in-progress) liveness session.</summary>
    Task<FaceLivenessSessionResult> GetLivenessSessionResultAsync(
        string awsSessionId, CancellationToken ct);

    /// <summary>Compares two face images (daily check-in reference vs. stored enrollment reference — used starting Plan 2).</summary>
    Task<FaceMatchResult> CompareFacesAsync(
        byte[] sourceImageBytes, byte[] targetImageBytes, CancellationToken ct);

    /// <summary>
    /// Issues short-lived (max 15 min) AWS credentials scoped to rekognition:StartFaceLivenessSession
    /// only, for the WebView2 capture client. Never persisted — caller must keep these in memory only.
    /// </summary>
    Task<ScopedCaptureCredentials> IssueScopedCaptureCredentialsAsync(
        string awsSessionId, CancellationToken ct);
}

public sealed record CreateLivenessSessionRequest(string ChallengeType, string KmsKeyId);

public sealed record FaceLivenessSessionCreated(string AwsSessionId);

/// <summary>Status: "CREATED" | "IN_PROGRESS" | "SUCCEEDED" | "FAILED" | "EXPIRED" (AWS's own status strings).</summary>
public sealed record FaceLivenessSessionResult(
    string Status,
    double? Confidence,
    byte[]? ReferenceImageBytes);

public sealed record FaceMatchResult(bool IsMatch, double Similarity);

public sealed record ScopedCaptureCredentials(
    string AccessKeyId,
    string SecretAccessKey,
    string SessionToken,
    string Region,
    DateTimeOffset Expiration);
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build src/ONEVO.Application/ONEVO.Application.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/ServiceInterfaces
git commit -m "feat: define IBiometricVerificationProvider abstraction"
```

---

## Task 10: `BiometricProviderOptions` (non-secret config)

Per the recon finding: this repo's secret credentials (AWS keys) do **not** go through `appsettings.json` + `IOptions<T>` — but this plan uses an ambient IAM role (design doc's Locked Decision), so there are no AWS access keys to store at all. Only non-secret settings (region, role ARN, KMS key ID, challenge type default) go here, following the `EmailOptions` POCO pattern exactly.

**Files:**
- Create: `src/ONEVO.Infrastructure/ExternalServices/Biometrics/BiometricProviderOptions.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Modify: `src/ONEVO.Api/appsettings.json`

- [ ] **Step 1: Define the options class**

```csharp
namespace ONEVO.Infrastructure.ExternalServices.Biometrics;

/// <summary>
/// Bound from the Biometrics:* section. NON-SECRET settings only — AWS credentials come from
/// the ambient IAM role on backend compute (design decision: IAM role, not static keys), and the
/// STS-assumed capture-client credentials are short-lived and never persisted here or anywhere else.
/// </summary>
public class BiometricProviderOptions
{
    public const string SectionName = "Biometrics";

    public string Region { get; set; } = "ap-south-1";
    public string CaptureRoleArn { get; set; } = string.Empty;
    public string KmsKeyId { get; set; } = string.Empty;
    public string DefaultChallengeType { get; set; } = "FaceMovementAndLightChallenge";
    public int CaptureCredentialsDurationSeconds { get; set; } = 900; // 15 minutes, per design doc
}
```

- [ ] **Step 2: Register the binding**

```csharp
services.Configure<BiometricProviderOptions>(configuration.GetSection(BiometricProviderOptions.SectionName));
```

- [ ] **Step 3: Add the appsettings.json section**

In `src/ONEVO.Api/appsettings.json`, next to the existing `"Email"` section:

```json
"Biometrics": {
  "Region": "ap-south-1",
  "CaptureRoleArn": "",
  "KmsKeyId": "",
  "DefaultChallengeType": "FaceMovementAndLightChallenge",
  "CaptureCredentialsDurationSeconds": 900
}
```

Fill `CaptureRoleArn` and `KmsKeyId` with the values recorded in Task 1's runbook for each environment (dev/staging/prod) via the normal per-environment `appsettings.{Environment}.json` override — do not commit real production ARNs into `appsettings.json` if this repo's convention keeps prod secrets out of source (check `appsettings.Production.json` handling before filling this in for real; blank placeholders are correct for this commit).

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build src/ONEVO.Api/ONEVO.Api.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/ExternalServices/Biometrics/BiometricProviderOptions.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Api/appsettings.json
git commit -m "feat: add BiometricProviderOptions non-secret configuration"
```

---

## Task 11: `AwsRekognitionBiometricVerificationProvider`

**Files:**
- Create: `src/ONEVO.Infrastructure/ExternalServices/Biometrics/AwsRekognitionBiometricVerificationProvider.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

This wraps two AWS SDK clients (`AmazonRekognitionClient`, `AmazonSecurityTokenServiceClient`), both constructed with the default credential chain so they pick up the backend's ambient IAM role automatically — never pass explicit `AWSCredentials` here.

- [ ] **Step 1: Implement the provider**

```csharp
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Biometrics;

public class AwsRekognitionBiometricVerificationProvider : IBiometricVerificationProvider
{
    private const double CompareFacesSimilarityThreshold = 90.0;

    private readonly IAmazonRekognition _rekognition;
    private readonly IAmazonSecurityTokenService _sts;
    private readonly BiometricProviderOptions _options;

    public AwsRekognitionBiometricVerificationProvider(
        IAmazonRekognition rekognition,
        IAmazonSecurityTokenService sts,
        IOptions<BiometricProviderOptions> options)
    {
        _rekognition = rekognition;
        _sts = sts;
        _options = options.Value;
    }

    public async Task<FaceLivenessSessionCreated> CreateLivenessSessionAsync(
        CreateLivenessSessionRequest request, CancellationToken ct)
    {
        var response = await _rekognition.CreateFaceLivenessSessionAsync(new CreateFaceLivenessSessionRequest
        {
            Settings = new CreateFaceLivenessSessionRequestSettings
            {
                AuditImagesLimit = 4
            },
            KmsKeyId = request.KmsKeyId
        }, ct);

        return new FaceLivenessSessionCreated(response.SessionId);
    }

    public async Task<FaceLivenessSessionResult> GetLivenessSessionResultAsync(
        string awsSessionId, CancellationToken ct)
    {
        var response = await _rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = awsSessionId }, ct);

        byte[]? referenceBytes = null;
        if (response.ReferenceImage?.Bytes is { } stream)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            referenceBytes = ms.ToArray();
        }

        return new FaceLivenessSessionResult(
            response.Status.Value,
            response.Confidence,
            referenceBytes);
    }

    public async Task<FaceMatchResult> CompareFacesAsync(
        byte[] sourceImageBytes, byte[] targetImageBytes, CancellationToken ct)
    {
        using var sourceStream = new MemoryStream(sourceImageBytes);
        using var targetStream = new MemoryStream(targetImageBytes);

        var response = await _rekognition.CompareFacesAsync(new CompareFacesRequest
        {
            SourceImage = new Image { Bytes = sourceStream },
            TargetImage = new Image { Bytes = targetStream },
            SimilarityThreshold = CompareFacesSimilarityThreshold
        }, ct);

        var bestMatch = response.FaceMatches.OrderByDescending(m => m.Similarity).FirstOrDefault();
        return bestMatch is null
            ? new FaceMatchResult(false, 0)
            : new FaceMatchResult(true, bestMatch.Similarity);
    }

    public async Task<ScopedCaptureCredentials> IssueScopedCaptureCredentialsAsync(
        string awsSessionId, CancellationToken ct)
    {
        var response = await _sts.AssumeRoleAsync(new AssumeRoleRequest
        {
            RoleArn = _options.CaptureRoleArn,
            RoleSessionName = $"face-liveness-{awsSessionId}",
            DurationSeconds = _options.CaptureCredentialsDurationSeconds
        }, ct);

        var creds = response.Credentials;
        return new ScopedCaptureCredentials(
            creds.AccessKeyId,
            creds.SecretAccessKey,
            creds.SessionToken,
            _options.Region,
            creds.Expiration);
    }
}
```

- [ ] **Step 2: Register the AWS SDK clients and the provider in DI**

```csharp
services.AddSingleton<IAmazonRekognition>(_ => new AmazonRekognitionClient(
    Amazon.RegionEndpoint.GetBySystemName(configuration["Biometrics:Region"] ?? "ap-south-1")));
services.AddSingleton<IAmazonSecurityTokenService>(_ => new AmazonSecurityTokenServiceClient());
services.AddScoped<IBiometricVerificationProvider, AwsRekognitionBiometricVerificationProvider>();
```

Both clients use the default AWS credential resolution chain (environment/instance-profile/ECS-task-role) — no `AWSCredentials`/access key is passed explicitly anywhere, satisfying the "IAM role, not static keys" decision.

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Infrastructure/ExternalServices/Biometrics/AwsRekognitionBiometricVerificationProvider.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: implement AWS Rekognition Face Liveness provider"
```

---

## Task 12: `UploadPurposeCatalog` — add the biometric reference-image purpose

**Files:**
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposeCatalogTests.cs` (create if it doesn't already exist — check first; if a test file already covers this catalog, add to it instead of creating a duplicate)

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Application.Features.Storage.File.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class UploadPurposeCatalogTests
{
    [Fact]
    public void MonitoringFaceLiveness_IsSupported_WithImageOnlyRule()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.MonitoringFaceLiveness));

        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.MonitoringFaceLiveness);

        Assert.NotNull(rule);
        Assert.Equal(5 * 1024 * 1024, rule!.MaxSizeBytes);
        Assert.Contains("image/jpeg", rule.AllowedContentTypes);
        Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~UploadPurposeCatalogTests"
```

Expected: FAIL — `UploadPurposeCatalog.MonitoringFaceLiveness` does not exist.

- [ ] **Step 3: Add the purpose constant and rule**

In `UploadPurposeCatalog.cs`, add the constant next to `MonitoringFaceScan`:

```csharp
    public const string MonitoringFaceLiveness = "monitoring_face_liveness";
```

And add its rule to the `Rules` dictionary:

```csharp
        [MonitoringFaceLiveness] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~UploadPurposeCatalogTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs tests/ONEVO.Tests.Unit/Features/Storage/File
git commit -m "feat: add monitoring_face_liveness upload purpose"
```

---

## Task 13: `CreateEnrollmentAttempt` command

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateEnrollmentAttempt/CreateEnrollmentAttemptCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateEnrollmentAttempt/CreateEnrollmentAttemptCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs/Responses/EnrollmentAttemptResponseDto.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CreateEnrollmentAttemptCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing test (mocking repository, resolver, provider, tenant switcher)**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CreateEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IEmployeeIdentityResolver> _employeeResolver = new();
    private readonly Mock<IBiometricVerificationProvider> _provider = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private CreateEnrollmentAttemptCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _employeeResolver.Object, _provider.Object, _clock.Object, _unitOfWork.Object);

    private void SetupAuthenticatedDevice()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(_deviceId);
    }

    [Fact]
    public async Task Handle_WithoutAuthenticatedDevice_Returns401()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(false);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmployeeCannotBeResolved_Returns422()
    {
        SetupAuthenticatedDevice();
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default))
            .ReturnsAsync(new TenantRegistryEntry(_tenantId, "acme", TenantStatus.Active, null));
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, default))
            .ReturnsAsync((Guid?)null);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithResolvedEmployee_CreatesAttemptAndReturnsAwsSession()
    {
        SetupAuthenticatedDevice();
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default))
            .ReturnsAsync(new TenantRegistryEntry(_tenantId, "acme", TenantStatus.Active, null));
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, default))
            .ReturnsAsync(_employeeId);
        _provider.Setup(p => p.CreateLivenessSessionAsync(It.IsAny<CreateLivenessSessionRequest>(), default))
            .ReturnsAsync(new FaceLivenessSessionCreated("aws-session-123"));
        _provider.Setup(p => p.IssueScopedCaptureCredentialsAsync("aws-session-123", default))
            .ReturnsAsync(new ScopedCaptureCredentials("AKIA...", "secret", "token", "ap-south-1", DateTimeOffset.UtcNow.AddMinutes(15)));
        var now = DateTimeOffset.UtcNow;
        _clock.SetupGet(c => c.UtcNow).Returns(now);

        var result = await CreateHandler().Handle(new CreateEnrollmentAttemptCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("aws-session-123", result.Value!.AwsSessionId);
        Assert.Equal("ap-south-1", result.Value.Region);
        _repository.Verify(r => r.AddAttemptAsync(
            It.Is<ONEVO.Domain.Features.Monitoring.Biometrics.Entities.BiometricVerificationAttempt>(a =>
                a.TenantId == _tenantId && a.EmployeeId == _employeeId && a.UserId == _userId
                && a.Purpose == ONEVO.Domain.Features.Monitoring.Biometrics.Entities.BiometricAttemptPurpose.Enrollment
                && a.AwsSessionId == "aws-session-123"),
            default), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
```

(This test assumes `Moq` is already a test dependency — verify with `grep Moq tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`; if absent, check what mocking library `GetProjectByIdQueryHandlerTests.cs` or another existing handler test actually uses and match that instead of assuming Moq.)

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateEnrollmentAttemptCommandHandlerTests"
```

Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Write the response DTO**

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record EnrollmentAttemptResponseDto(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);
```

- [ ] **Step 4: Write the command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

public record CreateEnrollmentAttemptCommand : IRequest<Result<EnrollmentAttemptResponseDto>>;
```

- [ ] **Step 5: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

public class CreateEnrollmentAttemptCommandHandler
    : IRequestHandler<CreateEnrollmentAttemptCommand, Result<EnrollmentAttemptResponseDto>>
{
    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IEmployeeIdentityResolver _employeeResolver;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEnrollmentAttemptCommandHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IEmployeeIdentityResolver employeeResolver,
        IBiometricVerificationProvider provider,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeResolver = employeeResolver;
        _provider = provider;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<EnrollmentAttemptResponseDto>> Handle(
        CreateEnrollmentAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<EnrollmentAttemptResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<EnrollmentAttemptResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var employeeId = await _employeeResolver.ResolveEmployeeIdAsync(
            _device.UserId, _device.TenantId, cancellationToken);
        if (employeeId is null)
        {
            return Result<EnrollmentAttemptResponseDto>.Failure(
                "No HR employee profile is linked to this account yet.", 422);
        }

        var session = await _provider.CreateLivenessSessionAsync(
            new CreateLivenessSessionRequest("FaceMovementAndLightChallenge", KmsKeyId: string.Empty),
            cancellationToken);

        var credentials = await _provider.IssueScopedCaptureCredentialsAsync(
            session.AwsSessionId, cancellationToken);

        var now = _clock.UtcNow;
        var attempt = new BiometricVerificationAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = employeeId.Value,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Purpose = BiometricAttemptPurpose.Enrollment,
            AwsSessionId = session.AwsSessionId,
            AwsRegion = credentials.Region,
            ChallengeType = "FaceMovementAndLightChallenge",
            AwsSessionExpiresAt = credentials.Expiration,
            Status = BiometricAttemptStatus.Created,
            CreatedAt = now
        };

        await _repository.AddAttemptAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<EnrollmentAttemptResponseDto>.Success(new EnrollmentAttemptResponseDto(
            attempt.Id,
            session.AwsSessionId,
            credentials.Region,
            attempt.ChallengeType,
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.SessionToken,
            credentials.Expiration));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CreateEnrollmentAttemptCommandHandlerTests"
```

Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateEnrollmentAttempt src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CreateEnrollmentAttemptCommandHandlerTests.cs
git commit -m "feat: add CreateEnrollmentAttempt command"
```

---

## Task 14: `CompleteEnrollmentAttempt` command

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs/Responses/BiometricProfileResponseDto.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing test (three cases: not-found attempt, AWS still in-progress, and success path)**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CompleteEnrollmentAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IBiometricVerificationProvider> _provider = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();

    private CompleteEnrollmentAttemptCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _provider.Object, _fileStorage.Object, _clock.Object, _unitOfWork.Object);

    private void SetupAuthenticatedDevice()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(_deviceId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default))
            .ReturnsAsync(new TenantRegistryEntry(_tenantId, "acme", TenantStatus.Active, null));
    }

    private BiometricVerificationAttempt AttemptInStatus(string status) => new()
    {
        Id = _attemptId,
        TenantId = _tenantId,
        EmployeeId = _employeeId,
        UserId = _userId,
        DeviceRegistrationId = _deviceId,
        Purpose = BiometricAttemptPurpose.Enrollment,
        AwsSessionId = "aws-session-123",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_WhenAttemptNotFound_ReturnsNotFound()
    {
        SetupAuthenticatedDevice();
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default))
            .ReturnsAsync((BiometricVerificationAttempt?)null);

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenAwsSessionStillInProgress_ReturnsConflict()
    {
        SetupAuthenticatedDevice();
        var attempt = AttemptInStatus(BiometricAttemptStatus.Capturing);
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default)).ReturnsAsync(attempt);
        _provider.Setup(p => p.GetLivenessSessionResultAsync("aws-session-123", default))
            .ReturnsAsync(new FaceLivenessSessionResult("IN_PROGRESS", null, null));

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenLivenessSucceeds_CreatesProfileAndSupersedesPrevious()
    {
        SetupAuthenticatedDevice();
        var attempt = AttemptInStatus(BiometricAttemptStatus.Capturing);
        _repository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default)).ReturnsAsync(attempt);
        var referenceBytes = new byte[] { 1, 2, 3 };
        _provider.Setup(p => p.GetLivenessSessionResultAsync("aws-session-123", default))
            .ReturnsAsync(new FaceLivenessSessionResult("SUCCEEDED", 98.5, referenceBytes));
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), "image/jpeg",
                Application.Features.Storage.File.Helpers.UploadPurposeCatalog.MonitoringFaceLiveness,
                It.IsAny<Stream>(), default))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                Guid.NewGuid(), "tenants/x/files/y/ref.jpg", "ref.jpg", "image/jpeg", referenceBytes.Length)));
        var now = DateTimeOffset.UtcNow;
        _clock.SetupGet(c => c.UtcNow).Returns(now);

        var result = await CreateHandler().Handle(
            new CompleteEnrollmentAttemptCommand(_attemptId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(BiometricProfileStatus.Active, result.Value!.Status);
        _repository.Verify(r => r.SupersedeActiveProfileAsync(_employeeId, _tenantId, now, default), Times.Once);
        _repository.Verify(r => r.AddProfileAsync(
            It.Is<EmployeeBiometricProfile>(p => p.EmployeeId == _employeeId && p.Status == BiometricProfileStatus.Active),
            default), Times.Once);
        Assert.Equal(BiometricAttemptStatus.Verified, attempt.Status);
    }
}
```

(`FileRecordDto`'s exact constructor shape must match whatever `IFileStorageService.UploadAsync`'s real return type actually declares — check `src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileRecordDto.cs` before finalizing this test; adjust the mock setup to its real property list rather than the placeholder shown here.)

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CompleteEnrollmentAttemptCommandHandlerTests"
```

Expected: FAIL.

- [ ] **Step 3: Write the response DTO**

```csharp
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record BiometricProfileResponseDto(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enrolled_at")] DateTimeOffset EnrolledAt);
```

- [ ] **Step 4: Write the command and validator**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public record CompleteEnrollmentAttemptCommand(Guid AttemptId) : IRequest<Result<BiometricProfileResponseDto>>;
```

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public class CompleteEnrollmentAttemptCommandValidator : AbstractValidator<CompleteEnrollmentAttemptCommand>
{
    public CompleteEnrollmentAttemptCommandValidator()
    {
        RuleFor(x => x.AttemptId).NotEmpty();
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;

public class CompleteEnrollmentAttemptCommandHandler
    : IRequestHandler<CompleteEnrollmentAttemptCommand, Result<BiometricProfileResponseDto>>
{
    private const string ConsentVersion = "v1";

    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteEnrollmentAttemptCommandHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IBiometricVerificationProvider provider,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _provider = provider;
        _fileStorage = fileStorage;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BiometricProfileResponseDto>> Handle(
        CompleteEnrollmentAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<BiometricProfileResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<BiometricProfileResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var attempt = await _repository.FindAttemptAsync(request.AttemptId, _device.TenantId, cancellationToken);
        if (attempt is null)
            return Result<BiometricProfileResponseDto>.NotFound("Enrollment attempt not found.");

        if (attempt.UserId != _device.UserId)
            return Result<BiometricProfileResponseDto>.Forbidden();

        if (string.IsNullOrEmpty(attempt.AwsSessionId))
            return Result<BiometricProfileResponseDto>.Failure("Attempt has no AWS session.", 409);

        var sessionResult = await _provider.GetLivenessSessionResultAsync(attempt.AwsSessionId, cancellationToken);

        var now = _clock.UtcNow;

        switch (sessionResult.Status)
        {
            case "SUCCEEDED":
                break;

            case "CREATED" or "IN_PROGRESS":
                return Result<BiometricProfileResponseDto>.Failure(
                    "Liveness session has not finished yet.", 409);

            case "FAILED":
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.Rejected, out _);
                attempt.FailureCode = "liveness_failed";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.Failure("Liveness check failed.", 422);

            case "EXPIRED":
                attempt.TryTransition(BiometricAttemptStatus.Expired, out _);
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.Failure("Liveness session expired.", 410);

            default:
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
                attempt.FailureCode = "unexpected_provider_status";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<BiometricProfileResponseDto>.Failure("Unexpected provider response.", 502);
        }

        if (sessionResult.ReferenceImageBytes is null || sessionResult.ReferenceImageBytes.Length == 0)
        {
            attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
            attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
            attempt.FailureCode = "missing_reference_image";
            attempt.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<BiometricProfileResponseDto>.Failure("Provider returned no reference image.", 502);
        }

        using var referenceStream = new MemoryStream(sessionResult.ReferenceImageBytes);
        var uploadResult = await _fileStorage.UploadAsync(
            _device.TenantId,
            _device.UserId,
            "enrollment-reference.jpg",
            "image/jpeg",
            UploadPurposeCatalog.MonitoringFaceLiveness,
            referenceStream,
            cancellationToken);

        if (!uploadResult.IsSuccess)
            return Result<BiometricProfileResponseDto>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 500);

        await _repository.SupersedeActiveProfileAsync(attempt.EmployeeId, _device.TenantId, now, cancellationToken);

        var profile = new EmployeeBiometricProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = attempt.EmployeeId,
            UserId = _device.UserId,
            Provider = "aws_rekognition",
            Region = attempt.AwsRegion,
            ReferenceStorageKey = uploadResult.Value!.StorageKey,
            Status = BiometricProfileStatus.Active,
            ConsentVersion = ConsentVersion,
            ConsentAcceptedAt = now,
            EnrollmentAttemptId = attempt.Id,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            CreatedAt = now
        };

        await _repository.AddProfileAsync(profile, cancellationToken);

        attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
        attempt.TryTransition(BiometricAttemptStatus.Verified, out _);
        attempt.LivenessConfidence = sessionResult.Confidence;
        attempt.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<BiometricProfileResponseDto>.Success(new BiometricProfileResponseDto(
            profile.Id, profile.Status, profile.CreatedAt));
    }
}
```

`FileRecordDto`'s exact property name for the storage key (used above as `uploadResult.Value!.StorageKey`) must be verified against the real DTO before this compiles — check `src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileRecordDto.cs` (it was referenced but not read verbatim during recon) and adjust the property access if its actual name differs.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CompleteEnrollmentAttemptCommandHandlerTests"
```

Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs/Responses/BiometricProfileResponseDto.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs
git commit -m "feat: add CompleteEnrollmentAttempt command"
```

---

## Task 15: `GetBiometricProfile` query + controller + integration tests

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Queries/GetBiometricProfile/GetBiometricProfileQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Queries/GetBiometricProfile/GetBiometricProfileQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/MonitoringBiometricsController.cs`
- Create: `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/BiometricsTestFactory.cs`
- Create: `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/BiometricsIntegrationTests.cs`

- [ ] **Step 1: Write the query and handler (read-only, no test file needed per Application Rules — "Queries are read-only" and this repo's own `GetEffectiveTrayPolicyQueryHandlerTests.cs` precedent shows queries do get unit tests, so add one)**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

public record GetBiometricProfileQuery : IRequest<Result<BiometricProfileResponseDto>>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

public class GetBiometricProfileQueryHandler
    : IRequestHandler<GetBiometricProfileQuery, Result<BiometricProfileResponseDto>>
{
    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IEmployeeIdentityResolver _employeeResolver;

    public GetBiometricProfileQueryHandler(
        IBiometricRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IEmployeeIdentityResolver employeeResolver)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _employeeResolver = employeeResolver;
    }

    public async Task<Result<BiometricProfileResponseDto>> Handle(
        GetBiometricProfileQuery request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated || _device.TenantId == Guid.Empty || _device.UserId == Guid.Empty)
            return Result<BiometricProfileResponseDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<BiometricProfileResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new ONEVO.Application.Common.Models.TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var employeeId = await _employeeResolver.ResolveEmployeeIdAsync(_device.UserId, _device.TenantId, cancellationToken);
        if (employeeId is null)
            return Result<BiometricProfileResponseDto>.NotFound("No employee profile linked to this account.");

        var profile = await _repository.FindActiveProfileAsync(employeeId.Value, _device.TenantId, cancellationToken);
        if (profile is null)
            return Result<BiometricProfileResponseDto>.NotFound("No active biometric enrollment.");

        return Result<BiometricProfileResponseDto>.Success(new BiometricProfileResponseDto(
            profile.Id, profile.Status, profile.CreatedAt));
    }
}
```

- [ ] **Step 2: Write the controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Queries.GetBiometricProfile;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Biometrics;

[ApiController]
[Route("api/v1/monitoring/biometrics")]
[Authorize(Policy = "TrayDevicePolicy")]
public class MonitoringBiometricsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MonitoringBiometricsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a new AWS Face Liveness enrollment session. Authorization: Bearer {tray_access_token}</summary>
    [HttpPost("enrollment-attempts")]
    public async Task<IActionResult> CreateEnrollmentAttempt(CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateEnrollmentAttemptCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Completes an enrollment attempt after the WebView2 liveness capture finished. Authorization: Bearer {tray_access_token}</summary>
    [HttpPost("enrollment-attempts/{id:guid}/complete")]
    public async Task<IActionResult> CompleteEnrollmentAttempt(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteEnrollmentAttemptCommand(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>Returns the current employee's active biometric enrollment status, if any. Authorization: Bearer {tray_access_token}</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetBiometricProfileQuery(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}
```

- [ ] **Step 3: Write the integration test factory with `FakeBiometricVerificationProvider`**

Follow `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs`'s exact pattern (Testcontainers PostgreSQL, `NoOpFileStorageService` stub) — add a fake provider so CI never calls real AWS:

```csharp
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

namespace ONEVO.Tests.Integration.Monitoring.Biometrics;

/// <summary>Deterministic in-memory fake — CI never calls real AWS. Always "succeeds" a liveness session.</summary>
public sealed class FakeBiometricVerificationProvider : IBiometricVerificationProvider
{
    public Task<FaceLivenessSessionCreated> CreateLivenessSessionAsync(
        CreateLivenessSessionRequest request, CancellationToken ct)
        => Task.FromResult(new FaceLivenessSessionCreated($"fake-session-{Guid.NewGuid():N}"));

    public Task<FaceLivenessSessionResult> GetLivenessSessionResultAsync(string awsSessionId, CancellationToken ct)
        => Task.FromResult(new FaceLivenessSessionResult("SUCCEEDED", 99.0, [1, 2, 3, 4]));

    public Task<FaceMatchResult> CompareFacesAsync(byte[] sourceImageBytes, byte[] targetImageBytes, CancellationToken ct)
        => Task.FromResult(new FaceMatchResult(true, 99.0));

    public Task<ScopedCaptureCredentials> IssueScopedCaptureCredentialsAsync(string awsSessionId, CancellationToken ct)
        => Task.FromResult(new ScopedCaptureCredentials(
            "FAKE_AKID", "FAKE_SECRET", "FAKE_TOKEN", "ap-south-1", DateTimeOffset.UtcNow.AddMinutes(15)));
}
```

Follow `CheckInTestFactory`'s `ConfigureWebHost`/`ConfigureTestServices` structure exactly to swap in `IBiometricVerificationProvider` → `FakeBiometricVerificationProvider` and `IFileStorageService` → its existing `NoOpFileStorageService`, reusing that same class rather than duplicating it — check whether it's `internal` (same-assembly-only, needs a new local one) or `public`/`InternalsVisibleTo`-exposed before deciding to reuse vs. duplicate.

- [ ] **Step 4: Write the integration tests (mirroring `CheckInIntegrationTests.cs`'s structure)**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Biometrics;

public class BiometricsIntegrationTests : IClassFixture<BiometricsTestFactory>
{
    private readonly BiometricsTestFactory _factory;

    public BiometricsIntegrationTests(BiometricsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Migrations_ApplyCleanly_AndLeaveNoPendingMigrations()
        => await _factory.AssertNoPendingMigrationsAsync();

    [Fact]
    public async Task CreateEnrollmentAttempt_WithValidTrayJwt_Returns200AndAwsSession()
    {
        var client = await _factory.CreateAuthenticatedTrayClientAsync();

        var response = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("aws_session_id"));
    }

    [Fact]
    public async Task CreateEnrollmentAttempt_WithoutJwt_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteEnrollmentAttempt_AfterCreate_Returns200AndActiveProfile()
    {
        var client = await _factory.CreateAuthenticatedTrayClientAsync();
        var createResponse = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);
        var created = await createResponse.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        var attemptId = created!["attempt_id"].GetGuid();

        var completeResponse = await client.PostAsync(
            $"/api/v1/monitoring/biometrics/enrollment-attempts/{attemptId}/complete", null);

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var profile = await completeResponse.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        Assert.Equal("active", profile!["status"].GetString());
    }

    [Fact]
    public async Task GetProfile_AfterEnrollment_ReturnsActiveProfile()
    {
        var client = await _factory.CreateAuthenticatedTrayClientAsync();
        var createResponse = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);
        var created = await createResponse.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        var attemptId = created!["attempt_id"].GetGuid();
        await client.PostAsync($"/api/v1/monitoring/biometrics/enrollment-attempts/{attemptId}/complete", null);

        var response = await client.GetAsync("/api/v1/monitoring/biometrics/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithoutEnrollment_Returns404()
    {
        var client = await _factory.CreateAuthenticatedTrayClientAsync(useFreshUnenrolledUser: true);

        var response = await client.GetAsync("/api/v1/monitoring/biometrics/profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReEnrollment_SupersedesPreviousProfile()
    {
        var client = await _factory.CreateAuthenticatedTrayClientAsync();

        var first = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);
        var firstAttempt = await first.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        await client.PostAsync(
            $"/api/v1/monitoring/biometrics/enrollment-attempts/{firstAttempt!["attempt_id"].GetGuid()}/complete", null);

        var second = await client.PostAsync("/api/v1/monitoring/biometrics/enrollment-attempts", null);
        var secondAttempt = await second.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        var completeSecond = await client.PostAsync(
            $"/api/v1/monitoring/biometrics/enrollment-attempts/{secondAttempt!["attempt_id"].GetGuid()}/complete", null);

        Assert.Equal(HttpStatusCode.OK, completeSecond.StatusCode);
        var profile = await client.GetAsync("/api/v1/monitoring/biometrics/profile");
        var profileBody = await profile.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        Assert.Equal("active", profileBody!["status"].GetString());
        // Exactly one Active row is enforced by the partial unique index (Task 5) — a second
        // Active row here would have thrown a Postgres unique-violation on SaveChangesAsync.
    }
}
```

`BiometricsTestFactory`'s exact `CreateAuthenticatedTrayClientAsync(...)` signature must mirror whatever `CheckInTestFactory` actually exposes (check its real method name/signature — it wasn't captured verbatim in recon beyond the `NoOpFileStorageService` mention) — adjust these calls to match, including whether it needs a `DevSmokeTestTenantSeeder`-style employee row seeded so `IEmployeeIdentityResolver` can resolve a real `EmployeeId` (it must — `CreateEnrollmentAttempt_WithValidTrayJwt_Returns200AndAwsSession` will 422 otherwise).

- [ ] **Step 5: Run the integration tests (requires Docker for Testcontainers)**

```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~BiometricsIntegrationTests"
```

Expected: PASS (7 tests). If Docker is unavailable in this environment, this step cannot run — flag it explicitly rather than claiming success; per project memory, do a live run against the real dev DB before calling this feature done (the System-mode RLS gap has bitten this exact feature area before).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/Queries src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics tests/ONEVO.Tests.Integration/Monitoring/Biometrics
git commit -m "feat: add GetBiometricProfile query, biometrics controller, and integration tests"
```

- [ ] **Step 7: Update Swagger**

Confirm the new `MonitoringBiometricsController` routes appear in `/swagger` after a local run (`dotnet run --project src/ONEVO.Api`) — this repo generates OpenAPI from controller attributes automatically, so no manual doc file should need editing; just verify.

---

## Task 16: TrayApp — add `Microsoft.Web.WebView2` package

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`

- [ ] **Step 1: Add the version to central package management**

In `Directory.Packages.props`, under the `<!-- TrayApp -->` group:

```xml
<PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.3124.44" />
```

(This repo uses `ManagePackageVersionsCentrally=true` — unlike the backend repo, do not inline a version in the `.csproj`. If `1.0.3124.44` is not the latest stable at implementation time, run `dotnet add package Microsoft.Web.WebView2 --project ONEVO.Agent.TrayApp -v` first to check current latest and use that instead of this placeholder.)

- [ ] **Step 2: Reference it in the TrayApp project**

In `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`, add to the existing `PackageReference` `ItemGroup`:

```xml
<PackageReference Include="Microsoft.Web.WebView2" />
```

- [ ] **Step 3: Verify the build still compiles**

```bash
cd C:\HR\tray_app_maui
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj
git commit -m "chore: add Microsoft.Web.WebView2 package reference"
```

---

## Task 17: Shared IPC contracts for enrollment liveness

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`

Follows the existing `ActivationCodeSubmit`/`EnrollmentResult` request/response naming pattern exactly. This is a synchronous Tray↔Service round trip — no video/image bytes ever appear in these payloads (only IDs, credentials, and status strings), satisfying §4.2/§7.5 of the architecture doc.

- [ ] **Step 1: Add new message type constants**

In `IpcMessageTypes`, add after `EnrollmentResult`:

```csharp
    /// <summary>Tray → Service: employee wants to start biometric enrollment.</summary>
    public const string BiometricEnrollmentStart = "BiometricEnrollmentStart";

    /// <summary>Service → Tray: AWS session + short-lived scoped credentials for the WebView2 capture client.</summary>
    public const string BiometricEnrollmentSessionReady = "BiometricEnrollmentSessionReady";

    /// <summary>Tray → Service: the WebView2 capture finished (or failed) — ask the Service to ask the backend to complete the attempt.</summary>
    public const string BiometricEnrollmentCaptureFinished = "BiometricEnrollmentCaptureFinished";

    /// <summary>Service → Tray: final enrollment outcome after the backend's CompleteEnrollmentAttempt call.</summary>
    public const string BiometricEnrollmentResult = "BiometricEnrollmentResult";
```

- [ ] **Step 2: Add the payload records**

After `EnrollmentResultPayload`:

```csharp
public sealed record BiometricEnrollmentStartPayload;

public sealed record BiometricEnrollmentSessionReadyPayload(
    bool Success,
    string? ErrorCode,
    Guid AttemptId,
    string? AwsSessionId,
    string? Region,
    string? ChallengeType,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? SessionToken,
    DateTimeOffset? CredentialsExpireAt);

/// <summary>CaptureSucceeded distinguishes a clean AWS-side liveness completion from a local
/// capture-side failure (camera denied/occupied, WebView2 crash, cancellation) — the Service
/// still asks the backend to check the AWS session either way, since AWS is the source of truth.</summary>
public sealed record BiometricEnrollmentCaptureFinishedPayload(Guid AttemptId, bool CaptureSucceeded, string? ClientErrorCode);

public sealed record BiometricEnrollmentResultPayload(bool Success, string? ErrorCode, string? ProfileStatus);
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build ONEVO.Agent.Shared\ONEVO.Agent.Shared.csproj
```

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs
git commit -m "feat: add IPC contracts for biometric enrollment"
```

---

## Task 18: `AgentApiRoutes` + `OnevoApiClient` enrollment methods

**Files:**
- Modify: `ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Modify: `ONEVO.Agent.Service/Api/OnevoApiClient.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Biometrics/EnrollmentCoordinatorTests.cs` (covers the client indirectly through the coordinator in Task 19 — `OnevoApiClient`'s existing methods have no dedicated unit tests of their own per recon, so follow that same convention here)

- [ ] **Step 1: Add the routes**

```csharp
    public const string BiometricEnrollmentAttemptCreate   = "/api/v1/monitoring/biometrics/enrollment-attempts";
    public const string BiometricEnrollmentAttemptComplete = "/api/v1/monitoring/biometrics/enrollment-attempts/{0}/complete";
```

- [ ] **Step 2: Add typed client methods and wire-format DTOs**

In `OnevoApiClient.cs`, add:

```csharp
    /// <summary>Creates a new enrollment attempt. Auth: Bearer Device JWT.</summary>
    public async Task<EnrollmentAttemptResult> CreateEnrollmentAttemptAsync(string accessToken, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.BiometricEnrollmentAttemptCreate);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.BiometricEnrollmentAttemptCreate);
            return new EnrollmentAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
            return new EnrollmentAttemptResult(false, response.StatusCode == HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "SERVICE_UNAVAILABLE", null);

        var payload = await response.Content.ReadFromJsonAsync<EnrollmentAttemptPayload>(cancellationToken: ct);
        return payload is null
            ? new EnrollmentAttemptResult(false, "SERVICE_UNAVAILABLE", null)
            : new EnrollmentAttemptResult(true, null, payload);
    }

    /// <summary>Completes an enrollment attempt. Auth: Bearer Device JWT.</summary>
    public async Task<CompleteEnrollmentResult> CompleteEnrollmentAttemptAsync(
        string accessToken, Guid attemptId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        var route = string.Format(AgentApiRoutes.BiometricEnrollmentAttemptComplete, attemptId);
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", route);
            return new CompleteEnrollmentResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
            return new CompleteEnrollmentResult(false, response.StatusCode == HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "SERVICE_UNAVAILABLE", null);

        var payload = await response.Content.ReadFromJsonAsync<BiometricProfilePayload>(cancellationToken: ct);
        return payload is null
            ? new CompleteEnrollmentResult(false, "SERVICE_UNAVAILABLE", null)
            : new CompleteEnrollmentResult(true, null, payload.Status);
    }
```

And the wire-format records at the bottom of the file, next to `TrayAuthPayload`:

```csharp
public sealed record EnrollmentAttemptPayload(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);

public sealed record EnrollmentAttemptResult(bool Success, string? ErrorCode, EnrollmentAttemptPayload? Attempt);

public sealed record BiometricProfilePayload(
    [property: JsonPropertyName("profile_id")] Guid ProfileId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("enrolled_at")] DateTimeOffset EnrolledAt);

public sealed record CompleteEnrollmentResult(bool Success, string? ErrorCode, string? ProfileStatus);
```

- [ ] **Step 3: Verify it compiles**

```bash
dotnet build ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
```

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Api/AgentApiRoutes.cs ONEVO.Agent.Service/Api/OnevoApiClient.cs
git commit -m "feat: add enrollment-attempt HTTP client methods"
```

---

## Task 19: `EnrollmentCoordinator` + `AgentWorker` wiring

**Files:**
- Create: `ONEVO.Agent.Service/Biometrics/EnrollmentCoordinator.cs`
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Modify: `ONEVO.Agent.Service/Program.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Biometrics/EnrollmentCoordinatorTests.cs`

This is the Service-side orchestrator the design doc calls out by name (`CheckInCoordinator` in the full design; this plan builds only its enrollment-purpose subset, per Plan 1's scope). It owns exactly one thing: turning `BiometricEnrollmentStart`/`BiometricEnrollmentCaptureFinished` IPC messages into `OnevoApiClient` calls, using the Service's own stored JWT — the credential never touches the Tray process.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Biometrics;
using ONEVO.Agent.Service.Security;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Biometrics;

public class EnrollmentCoordinatorTests
{
    private readonly Mock<OnevoApiClientWrapper> _apiWrapper = new(); // see Step 2 note on testability seam
    private readonly CredentialStore _credentials = new();

    [Fact]
    public async Task StartAsync_WhenNoDeviceJwt_ReturnsFailure()
    {
        // CredentialStore reads real DPAPI-protected files from disk; a freshly
        // constructed instance with nothing written returns null from ReadDeviceJwt(),
        // which is exactly the "not enrolled yet" case this test exercises.
        var coordinator = new EnrollmentCoordinator(
            NullLogger<EnrollmentCoordinator>.Instance, _apiWrapper.Object, _credentials);

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NO_DEVICE_CREDENTIAL", result.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~EnrollmentCoordinatorTests"
```

Expected: FAIL — `EnrollmentCoordinator` doesn't exist. Note: `OnevoApiClient` is a `sealed class` with no interface, matching this repo's convention for `OnevoApiClient`/`CredentialStore` (also concrete, unmocked types elsewhere) — check whether existing Service tests mock these via a thin wrapper interface or construct real instances against a test `HttpClient`/temp directory; match whatever `ActivitySyncServiceTests.cs` or similar already does for `CredentialStore`/`OnevoApiClient` rather than introducing a new `OnevoApiClientWrapper` seam if one doesn't already exist in this codebase.

- [ ] **Step 3: Implement `EnrollmentCoordinator`**

```csharp
namespace ONEVO.Agent.Service.Biometrics;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;

public sealed record EnrollmentSessionResult(
    bool Success, string? ErrorCode, Guid AttemptId, string? AwsSessionId, string? Region,
    string? ChallengeType, string? AccessKeyId, string? SecretAccessKey, string? SessionToken,
    DateTimeOffset? CredentialsExpireAt);

public sealed record EnrollmentCompletionResult(bool Success, string? ErrorCode, string? ProfileStatus);

/// <summary>
/// Orchestrates the enrollment subset of the biometric flow: Service holds the Device JWT
/// (never handed to the Tray, per §8.2 of the architecture doc) and makes the two backend
/// calls on the Tray's behalf. The Tray only ever sees the short-lived AWS capture credentials
/// this returns — never the Device JWT itself.
/// </summary>
public sealed class EnrollmentCoordinator
{
    private readonly ILogger<EnrollmentCoordinator> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;

    public EnrollmentCoordinator(
        ILogger<EnrollmentCoordinator> logger, OnevoApiClient apiClient, CredentialStore credentials)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
    }

    public async Task<EnrollmentSessionResult> StartAsync(CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return new EnrollmentSessionResult(false, "NO_DEVICE_CREDENTIAL",
                Guid.Empty, null, null, null, null, null, null, null);
        }

        var result = await _apiClient.CreateEnrollmentAttemptAsync(jwt, ct);
        if (!result.Success || result.Attempt is null)
        {
            _logger.LogWarning("CreateEnrollmentAttempt failed: {ErrorCode}", result.ErrorCode);
            return new EnrollmentSessionResult(false, result.ErrorCode ?? "SERVICE_UNAVAILABLE",
                Guid.Empty, null, null, null, null, null, null, null);
        }

        var attempt = result.Attempt;
        return new EnrollmentSessionResult(
            true, null, attempt.AttemptId, attempt.AwsSessionId, attempt.Region, attempt.ChallengeType,
            attempt.AccessKeyId, attempt.SecretAccessKey, attempt.SessionToken, attempt.CredentialsExpireAt);
    }

    public async Task<EnrollmentCompletionResult> CompleteAsync(Guid attemptId, CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
            return new EnrollmentCompletionResult(false, "NO_DEVICE_CREDENTIAL", null);

        var result = await _apiClient.CompleteEnrollmentAttemptAsync(jwt, attemptId, ct);
        if (!result.Success)
            _logger.LogWarning("CompleteEnrollmentAttempt failed: {ErrorCode}", result.ErrorCode);

        return new EnrollmentCompletionResult(result.Success, result.ErrorCode, result.ProfileStatus);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~EnrollmentCoordinatorTests"
```

Expected: PASS.

- [ ] **Step 5: Wire `AgentWorker`'s dispatch switch**

In `AgentWorker.cs`, add the `EnrollmentCoordinator` as a constructor-injected field (same style as the other fields listed in §5 of recon), then extend the message dispatch switch (the one shown in recon at lines 373–394):

```csharp
            case IpcMessageTypes.BiometricEnrollmentStart:
                await HandleBiometricEnrollmentStartAsync(envelope, reply);
                break;

            case IpcMessageTypes.BiometricEnrollmentCaptureFinished:
                await HandleBiometricEnrollmentCaptureFinishedAsync(envelope, reply);
                break;
```

And the two handler methods:

```csharp
    private async Task HandleBiometricEnrollmentStartAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var result = await _enrollmentCoordinator.StartAsync(CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.BiometricEnrollmentSessionReady,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new BiometricEnrollmentSessionReadyPayload(
                result.Success, result.ErrorCode, result.AttemptId, result.AwsSessionId, result.Region,
                result.ChallengeType, result.AccessKeyId, result.SecretAccessKey, result.SessionToken,
                result.CredentialsExpireAt))
        });
    }

    private async Task HandleBiometricEnrollmentCaptureFinishedAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<BiometricEnrollmentCaptureFinishedPayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.BiometricEnrollmentResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new BiometricEnrollmentResultPayload(false, "INVALID_PAYLOAD", null))
            });
            return;
        }

        // The backend re-derives the verdict from AWS regardless of CaptureSucceeded — the Tray's
        // local capture outcome is only used for logging/UX, never trusted as the security decision.
        var result = await _enrollmentCoordinator.CompleteAsync(payload.AttemptId, CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.BiometricEnrollmentResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new BiometricEnrollmentResultPayload(result.Success, result.ErrorCode, result.ProfileStatus))
        });
    }
```

- [ ] **Step 6: Register `EnrollmentCoordinator` in `Program.cs`**

```csharp
builder.Services.AddSingleton<EnrollmentCoordinator>();
```

(Match whatever lifetime `CredentialStore`/`DeviceIdentityStore` are already registered with — check `Program.cs` before assuming `Singleton`; those two look like they should be singletons given they wrap file-backed state, but verify against the real registration before copying blindly.)

- [ ] **Step 7: Verify the build compiles**

```bash
dotnet build ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
```

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.Service/Biometrics ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Program.cs tests/ONEVO.Agent.Service.Tests/Biometrics
git commit -m "feat: add EnrollmentCoordinator and wire biometric enrollment IPC handling"
```

---

## Task 20: TrayApp — `BiometricEnrollmentPage` (WebView2 host)

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Create: `ONEVO.Agent.TrayApp/ViewModels/BiometricEnrollmentViewModel.cs`
- Create: `ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml`
- Create: `ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml.cs`
- Create: `ONEVO.Agent.TrayApp/Platforms/Windows/BiometricWebViewSetup.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/AppShell.xaml`
- Create: `ONEVO.Agent.TrayApp/wwwroot/biometric/` (packaged React build from Task 0's probe, now made permanent)
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/BiometricEnrollmentViewModelTests.cs`

- [ ] **Step 1: Add the two round-trip methods to `INamedPipeClient`**

```csharp
    /// <summary>Requests a new enrollment liveness session and waits for BiometricEnrollmentSessionReady (or timeout).</summary>
    Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct);

    /// <summary>Reports the WebView2 capture outcome and waits for the final BiometricEnrollmentResult (or timeout).</summary>
    Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct);
```

- [ ] **Step 2: Write the failing ViewModel test first (drives the two new client methods)**

```csharp
using Moq;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public class BiometricEnrollmentViewModelTests
{
    [Fact]
    public async Task StartSessionAsync_OnSuccess_PopulatesCaptureCredentials()
    {
        var pipe = new Mock<INamedPipeClient>();
        var attemptId = Guid.NewGuid();
        pipe.Setup(p => p.StartBiometricEnrollmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricEnrollmentSessionReadyPayload(
                true, null, attemptId, "aws-session-1", "ap-south-1", "FaceMovementAndLightChallenge",
                "AKIA", "secret", "token", DateTimeOffset.UtcNow.AddMinutes(15)));

        var vm = new BiometricEnrollmentViewModel(pipe.Object);
        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.True(vm.IsSessionReady);
        Assert.Equal(attemptId, vm.AttemptId);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task StartSessionAsync_OnFailure_SetsErrorMessage()
    {
        var pipe = new Mock<INamedPipeClient>();
        pipe.Setup(p => p.StartBiometricEnrollmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricEnrollmentSessionReadyPayload(
                false, "NO_DEVICE_CREDENTIAL", Guid.Empty, null, null, null, null, null, null, null));

        var vm = new BiometricEnrollmentViewModel(pipe.Object);
        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.False(vm.IsSessionReady);
        Assert.NotNull(vm.ErrorMessage);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~BiometricEnrollmentViewModelTests"
```

Expected: FAIL — `BiometricEnrollmentViewModel` doesn't exist.

- [ ] **Step 4: Implement `NamedPipeClient`'s two new methods**

Follow the exact `SendLifecycleAsync` correlation pattern shown in recon §14 (new `TaskCompletionSource`, register in `_pending`, send envelope, await with a 10s timeout, deserialize the typed reply) — and add both new reply types (`BiometricEnrollmentSessionReady`, `BiometricEnrollmentResult`) to the `_pending`-completion allowlist shown in recon §14 (the `envelope.Type is IpcMessageTypes.LifecycleResult or ...` check) so replies actually resolve the waiting `TaskCompletionSource`.

```csharp
    public Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct)
        => SendCorrelatedAsync<BiometricEnrollmentSessionReadyPayload>(
            IpcMessageTypes.BiometricEnrollmentStart,
            new BiometricEnrollmentStartPayload(),
            ct);

    public Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
        => SendCorrelatedAsync<BiometricEnrollmentResultPayload>(
            IpcMessageTypes.BiometricEnrollmentCaptureFinished,
            new BiometricEnrollmentCaptureFinishedPayload(attemptId, captureSucceeded, clientErrorCode),
            ct);
```

If `NamedPipeClient` does not already have a generic `SendCorrelatedAsync<TReply>(...)` helper factoring out the pattern duplicated across `SendActivationAsync`/`SendLogoutAsync`/`SendLifecycleAsync`, do **not** introduce one as a silent refactor inside this task — either extract it as its own small preparatory commit first (touching only existing methods, with existing tests as your safety net), or duplicate the ~20-line pattern inline for these two new methods matching the existing style exactly. Prefer the extraction if it's low-risk; note the decision in the commit message either way.

- [ ] **Step 5: Implement `BiometricEnrollmentViewModel`**

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class BiometricEnrollmentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private bool _isSessionReady;
    [ObservableProperty] private bool _isCompleting;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Guid _attemptId;
    [ObservableProperty] private string? _awsSessionId;
    [ObservableProperty] private string? _region;
    [ObservableProperty] private string? _challengeType;
    [ObservableProperty] private string? _accessKeyId;
    [ObservableProperty] private string? _secretAccessKey;
    [ObservableProperty] private string? _sessionToken;

    public BiometricEnrollmentViewModel(INamedPipeClient pipe)
    {
        Title = "Face Enrollment";
        _pipe = pipe;
    }

    [RelayCommand]
    private async Task StartSessionAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        var result = await _pipe.StartBiometricEnrollmentAsync(ct);

        if (result is null || !result.Success)
        {
            ErrorMessage = result?.ErrorCode ?? "No response from OneXso Agent Service.";
            IsSessionReady = false;
            return;
        }

        AttemptId = result.AttemptId;
        AwsSessionId = result.AwsSessionId;
        Region = result.Region;
        ChallengeType = result.ChallengeType;
        AccessKeyId = result.AccessKeyId;
        SecretAccessKey = result.SecretAccessKey;
        SessionToken = result.SessionToken;
        IsSessionReady = true;
    }

    /// <summary>Called by the WebView2 host once the JS FaceLivenessDetector fires its analysis-complete or error event.</summary>
    public async Task ReportCaptureFinishedAsync(bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        IsCompleting = true;
        try
        {
            var result = await _pipe.CompleteBiometricEnrollmentAsync(AttemptId, captureSucceeded, clientErrorCode, ct);

            if (result is null || !result.Success)
            {
                ErrorMessage = result?.ErrorCode ?? "Enrollment could not be completed.";
                return;
            }

            try { await Shell.Current.GoToAsync("//review"); }
            catch { /* unit tests */ }
        }
        finally
        {
            IsCompleting = false;
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~BiometricEnrollmentViewModelTests"
```

Expected: PASS (2 tests).

- [ ] **Step 7: Build the packaged React app into `wwwroot/biometric/`**

Reuse the React `FaceLivenessDetector` app built during Task 0's probe (do not rebuild from scratch) — but this time it must read `sessionId`/`region`/credentials dynamically from a small JS bridge object the WebView2 host injects, and call back into .NET on completion. Add a minimal bridge contract to the React app's entry point:

```javascript
// wwwroot/biometric/src/bridge.js
export function getSessionConfig() {
  // window.chrome.webview.hostObjects or postMessage — populated by BiometricWebViewSetup.cs Step 8
  return window.__onevoLivenessConfig;
}

export function reportCaptureFinished(succeeded, errorCode) {
  window.chrome.webview.postMessage(JSON.stringify({ succeeded, errorCode }));
}
```

Run `npm run build` and copy the output into `ONEVO.Agent.TrayApp/wwwroot/biometric/`. Mark the folder as `<MauiAsset>`/content in the `.csproj` if MAUI's default glob doesn't already pick up `wwwroot/**` (check `EnableDefaultMauiItems` behavior — it's `true` per the recon'd `.csproj`, which typically includes `wwwroot` automatically, but verify the build output actually lands next to the executable).

- [ ] **Step 8: Implement `BiometricWebViewSetup.cs` (Windows platform-specific)**

```csharp
namespace ONEVO.Agent.TrayApp.Platforms.Windows;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

public static class BiometricWebViewSetup
{
    private const string VirtualHost = "biometric.onevo.local";

    public static async Task InitializeAsync(WebView2 webView, BiometricSessionConfig config, Action<bool, string?> onCaptureFinished)
    {
        await webView.EnsureCoreWebView2Async();
        var core = webView.CoreWebView2;

        core.Settings.AreDevToolsEnabled = false;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "biometric"),
            CoreWebView2HostResourceAccessKind.DenyCors);

        core.PermissionRequested += (_, args) =>
        {
            var isBiometricOrigin = args.Uri.StartsWith($"https://{VirtualHost}", StringComparison.Ordinal);
            var isCameraRequest = args.PermissionKind == CoreWebView2PermissionKind.Camera;
            args.State = (isBiometricOrigin && isCameraRequest)
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };

        core.WebMessageReceived += (_, args) =>
        {
            try
            {
                var message = System.Text.Json.JsonSerializer.Deserialize<CaptureFinishedMessage>(args.WebMessageAsJson);
                if (message is not null)
                    onCaptureFinished(message.Succeeded, message.ErrorCode);
            }
            catch (System.Text.Json.JsonException)
            {
                onCaptureFinished(false, "MALFORMED_BRIDGE_MESSAGE");
            }
        };

        core.Navigate($"https://{VirtualHost}/index.html");
        core.DOMContentLoaded += (_, _) =>
        {
            var configJson = System.Text.Json.JsonSerializer.Serialize(config);
            core.ExecuteScriptAsync($"window.__onevoLivenessConfig = {configJson};");
        };
    }

    private sealed record CaptureFinishedMessage(bool Succeeded, string? ErrorCode);
}

public sealed record BiometricSessionConfig(
    string SessionId, string Region, string ChallengeType,
    string AccessKeyId, string SecretAccessKey, string SessionToken);
```

Credentials in `BiometricSessionConfig` live only in this in-memory object and the WebView2 JS runtime's memory — never written to `Preferences`, disk, or logged (`ExecuteScriptAsync`'s argument is not logged by this code; confirm no ambient logging middleware captures it either).

- [ ] **Step 9: Wire the page and its code-behind**

`Views/BiometricEnrollmentPage.xaml.cs` — follow the `WorkLocationPage.xaml.cs`/`PhotoCaptureWindow.xaml.cs` constructor-DI + `OnAppearing` pattern exactly:

```csharp
namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class BiometricEnrollmentPage : ContentPage
{
    private readonly BiometricEnrollmentViewModel _vm;

    public BiometricEnrollmentPage(BiometricEnrollmentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.StartSessionCommand.ExecuteAsync(null);
    }
}
```

`Views/BiometricEnrollmentPage.xaml` — minimal shell hosting a native WebView2 control via a MAUI handler (follow whatever pattern `controls:CameraPreview` uses in `PhotoCaptureWindow.xaml` to host a native Windows control inside MAUI XAML — reuse that same custom-control-hosting mechanism for a new `controls:BiometricWebView` rather than inventing a different interop approach):

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             xmlns:controls="clr-namespace:ONEVO.Agent.TrayApp.Controls"
             x:Class="ONEVO.Agent.TrayApp.Views.BiometricEnrollmentPage"
             x:DataType="vm:BiometricEnrollmentViewModel"
             Title="{Binding Title}">
    <Grid RowDefinitions="Auto,*,Auto" Padding="24">
        <Label Grid.Row="0" Text="{Binding ErrorMessage}" TextColor="Red" IsVisible="{Binding ErrorMessage, Converter={StaticResource StringToBoolConverter}}" />
        <controls:BiometricWebView Grid.Row="1" IsVisible="{Binding IsSessionReady}" />
        <ActivityIndicator Grid.Row="2" IsRunning="{Binding IsCompleting}" IsVisible="{Binding IsCompleting}" />
    </Grid>
</ContentPage>
```

(`StringToBoolConverter` and the exact `controls:BiometricWebView` hosting-control implementation depend on conventions already established elsewhere in `ONEVO.Agent.TrayApp/Controls/` — check `PageAnimations.cs`/existing converters before inventing new ones; if no string-null-to-bool converter exists yet, add one following whatever converter pattern the codebase already uses, or restructure the binding to a bool property on the ViewModel instead.)

- [ ] **Step 10: Register the route in `AppShell.xaml`**

```xml
  <ShellContent Route="enrollment-biometric" ContentTemplate="{DataTemplate views:BiometricEnrollmentPage}" />
```

Where in the onboarding sequence this route is actually navigated *to* (replacing/alongside the existing `photo` step, and where consent/`policy` fits relative to it) is a UX/product decision the design doc does not fully pin down for the onboarding order — flag this explicitly to the design owner rather than silently reordering `connect→prepare→location→photo→review→policy→clockin`. For this plan, it is sufficient that the route exists, is reachable by direct `Shell.Current.GoToAsync("//enrollment-biometric")`, and is functionally complete; wiring it into the exact onboarding sequence position is a one-line follow-up once that product decision is made.

- [ ] **Step 11: Register `BiometricEnrollmentViewModel`/`BiometricEnrollmentPage` in `MauiProgram.cs`**

Follow whatever DI registration style `PhotoCaptureWindowViewModel`/`PhotoCaptureWindow` already use there (transient page+viewmodel pair, matching MAUI Shell convention).

- [ ] **Step 12: Verify the full TrayApp build compiles**

```bash
cd C:\HR\tray_app_maui
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

- [ ] **Step 13: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs ONEVO.Agent.TrayApp/ViewModels/BiometricEnrollmentViewModel.cs ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml.cs ONEVO.Agent.TrayApp/Platforms/Windows/BiometricWebViewSetup.cs ONEVO.Agent.TrayApp/Views/AppShell.xaml ONEVO.Agent.TrayApp/wwwroot/biometric ONEVO.Agent.TrayApp/MauiProgram.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/BiometricEnrollmentViewModelTests.cs
git commit -m "feat: add WebView2-hosted biometric enrollment capture page"
```

---

## Task 21: Manual end-to-end verification on real Windows hardware

**Files:** none — this is the "live run against the real dev DB" step called out in project memory for this exact feature area (the System-mode RLS gap has broken this class of feature silently before, and Testcontainers bypasses `FORCE ROW LEVEL SECURITY` entirely as the table owner).

- [ ] **Step 1: Deploy backend to a real dev environment with the real IAM role attached**

Confirm the backend process actually has the IAM role from Task 1 attached (not running under a developer's personal AWS credentials) — `aws sts get-caller-identity` from the backend host should show the expected role ARN.

- [ ] **Step 2: Run the TrayApp on a Windows machine that passed Task 0's compatibility gate**

```powershell
dotnet publish .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj --configuration Release -f net10.0-windows10.0.19041.0
```

- [ ] **Step 3: Walk through enrollment end-to-end with a real seeded employee**

Using a tenant/employee seeded via `DevSmokeTestTenantSeeder` (per project memory, acme/dapi tenants), complete: device-code enrollment → navigate to `//enrollment-biometric` → real webcam liveness capture against real AWS `ap-south-1` → confirm `GET /api/v1/monitoring/biometrics/profile` returns `status: "active"` → confirm the R2 reference image actually exists at the recorded `ReferenceStorageKey`.

- [ ] **Step 4: Confirm re-enrollment supersedes cleanly**

Repeat enrollment for the same employee; confirm exactly one `Active` profile exists afterward (query `employee_biometric_profiles` directly) and the prior row is `Superseded` with a non-null `SupersededAt`.

- [ ] **Step 5: Record results**

Append findings to `docs/superpowers/plans/2026-08-13-camera-compatibility-gate-result.md` (or a new dated report file if that one is considered closed) — this is the evidence gate before Plan 1 can be marked done.

---

## Self-Review Notes (already applied above, kept here for the next worker's context)

- **Spec coverage:** Identity contract → Task 8. Windows Camera Compatibility Gate → Task 0. IAM/KMS → Task 1. Database model → Tasks 3–7 (enrollment-relevant tables only; `EmployeeCheckIn`/`EmployeeWorkSession` changes are explicitly deferred to Plan 2, see header). Provider abstraction → Tasks 9–11. Enrollment (backend + Tray/Service) → Tasks 12–20. Manual verification → Task 21.
- **Explicitly out of scope, do not implement here:** CLOCK IN gating on a biometric verdict, `SubmitCheckIn`/`UploadFaceScan` changes, `AttendanceSessionId` on `EmployeeWorkSession`, employer review/approve/reject endpoints, provider-outage/offline fallback paths. These belong to Plans 2–4.
- **Known verification gaps flagged inline, not silently assumed:** `FileRecordDto`'s exact property name (Task 14), whether `NoOpFileStorageService`/`CheckInTestFactory`'s exact method signatures are reusable as-is (Task 15), which mocking library this repo's handler tests actually use (Task 13), whether `OnevoApiClient`/`CredentialStore` are mocked via a wrapper interface elsewhere in Service tests (Task 19), and the exact onboarding-screen ordering for the new enrollment route (Task 20) — each is called out at its exact step rather than guessed silently, per this repo's own "don't fail silently" instinct evident throughout its existing code comments.
