# Verified Employee Check-In — Foundation and Enrollment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the Windows laptop-camera path and deliver a trusted AWS Rekognition onboarding face reference bound to the activated employee and registered device.

**Architecture:** The backend resolves employee/device identity from the signed tray JWT, owns AWS session creation/results, and stores only a successful enrollment reference in private R2. The MAUI Tray hosts a packaged React Face Liveness module in WebView2; video streams directly to AWS and never crosses generic IPC or SQLite.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR, EF Core/PostgreSQL RLS, Cloudflare R2, AWS SDK v4, Rekognition Face Liveness and STS in `ap-south-1`, MAUI Windows, WebView2, React 19, Amplify UI Liveness 3.x, Amplify 6.x, Vite, xUnit.

**Spec:** `C:\HR\HRMS-Backend-v1\docs\superpowers\specs\next\2026-08-13-verified-employee-check-in-design.md`

## Global Constraints

- Resolve `TenantId`, `UserId`, and `DeviceRegistrationId` only from a validated `tray_device` JWT.
- Resolve real CoreHR `EmployeeId` server-side by `(TenantId, UserId)`; never accept it from Tray input.
- Region is exactly `ap-south-1`; application startup fails for any other configured biometric region.
- Client credentials expire within 15 minutes and authorize only `rekognition:StartFaceLivenessSession`.
- No access keys, session tokens, AWS session IDs, images, or videos in logs, Preferences, SQLite, or crash telemetry.
- No biometric media in generic `CollectionRecord` or the 65,536-byte named-pipe message path.
- Default challenge is `FaceMovementAndLightChallenge`; sessions are single-use.
- Run implementation only after the current backend merge is completed, then create isolated worktrees for both repositories.

---

### Task 1: Lock the employee identity contract

**Files:**
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\CoreHr\Employee\RepositoryInterfaces\IEmployeeRepository.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\CoreHr\EfEmployeeRepository.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\ServiceInterfaces\ITrayEmployeeResolver.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\TrayEmployeeResolver.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\DependencyInjection.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\Biometrics\TrayEmployeeResolverTests.cs`
- Modify: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\TrayActivation\TrayActivationIntegrationTests.cs`

**Interfaces:**
- Consumes: `ITrayCurrentDevice.TenantId`, `.UserId`, `.DeviceRegistrationId` after tenant switching.
- Produces: `Task<EmployeeIdentity?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct)` where `EmployeeIdentity` contains `EmployeeId`, `EmployeeNumber`, `DisplayName`, and `Email`.

- [ ] **Step 1: Write resolver failure and success tests**

```csharp
[Fact]
public async Task ResolveAsync_ReturnsCoreHrIdentityForMatchingTenantAndUser()
{
    var result = await sut.ResolveAsync(TenantId, UserId, default);
    result.Should().Be(new EmployeeIdentity(EmployeeId, "ONEVO1234", "Alex Smith", "alex@onevo.test"));
}

[Fact]
public async Task ResolveAsync_DoesNotReturnAnotherTenantsEmployee()
{
    var result = await sut.ResolveAsync(OtherTenantId, UserId, default);
    result.Should().BeNull();
}
```

- [ ] **Step 2: Run the focused tests and confirm the interface is missing**

Run from `C:\HR\HRMS-Backend-v1`:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~TrayEmployeeResolverTests
```

Expected: compilation failure because `ITrayEmployeeResolver` does not exist.

- [ ] **Step 3: Add the exact repository and resolver contracts**

```csharp
Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

public sealed record EmployeeIdentity(
    Guid EmployeeId,
    string EmployeeNumber,
    string DisplayName,
    string Email);

public interface ITrayEmployeeResolver
{
    Task<EmployeeIdentity?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
```

Implement `GetByUserIdAsync` with `AsNoTracking()` and both tenant/user predicates. Compose the display name with `FirstName + " " + LastName` and trim it. Register the resolver as scoped.

- [ ] **Step 4: Extend activation integration coverage**

Assert activation response still returns `employee_name`, `employee_email`, and `employee_number`, while decoding the JWT proves it contains device/user/tenant identifiers but no employee name, email, or number.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~TrayEmployeeResolverTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~TrayActivationIntegrationTests
git add src/ONEVO.Application/Features/CoreHr/Employee src/ONEVO.Application/Features/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr src/ONEVO.Infrastructure/Services/Monitoring/Biometrics src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics tests/ONEVO.Tests.Integration/Monitoring/TrayActivation
git commit -m "feat(monitoring): resolve tray employee identity"
```

Expected: all focused tests pass.

---

### Task 2: Add biometric entities, RLS configuration, and migration

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\Biometrics\Entities\EmployeeBiometricProfile.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\Biometrics\Entities\BiometricVerificationAttempt.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\Biometrics\BiometricEnums.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\Biometrics\EmployeeBiometricProfileConfiguration.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Configurations\Monitoring\Biometrics\BiometricVerificationAttemptConfiguration.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\ApplicationDbContext.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\BiometricPersistenceIntegrationTests.cs`

**Interfaces:**
- Consumes: real `EmployeeId`, source `UserId`, JWT device ID, private `FileRecordId`.
- Produces: tenant-owned profile/attempt persistence used by enrollment and check-in handlers.

- [ ] **Step 1: Write PostgreSQL persistence tests**

Cover one active profile per `(TenantId, EmployeeId)`, cross-tenant RLS denial, attempt state round-trip, and required AWS session/region maximum lengths.

- [ ] **Step 2: Run the test and confirm missing tables**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricPersistenceIntegrationTests
```

Expected: compilation/table failure.

- [ ] **Step 3: Implement exact entity states**

```csharp
public enum BiometricProfileStatus { Active, Superseded, Revoked, Deleted }
public enum BiometricAttemptPurpose { Enrollment, CheckIn }
public enum BiometricAttemptStatus { Created, Capturing, Verifying, Verified, Rejected, ProviderError, Expired }

public class EmployeeBiometricProfile : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public Guid ReferenceFileId { get; set; }
    public Guid EnrollmentAttemptId { get; set; }
    public Guid EnrollmentDeviceRegistrationId { get; set; }
    public string Provider { get; set; } = "aws_rekognition";
    public string Region { get; set; } = "ap-south-1";
    public BiometricProfileStatus Status { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public DateTimeOffset ConsentedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`BiometricVerificationAttempt` stores the identifiers, purpose/status, nullable `AttendanceSessionId`, AWS session ID, challenge, expiry, nullable liveness/match scores, stable failure code, and timestamps. Do not add credential or byte columns.

- [ ] **Step 4: Configure indexes and generate migration**

Use a PostgreSQL partial unique index for one active profile:

```csharp
builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
    .HasFilter("status = 'Active'")
    .IsUnique();
```

Generate using the repository's API startup project:

```powershell
dotnet ef migrations add AddEmployeeBiometrics --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Add explicit PostgreSQL RLS enable/force/policies following `AddMonitoringCheckIn` conventions.

- [ ] **Step 5: Run migration tests and commit**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricPersistenceIntegrationTests
git add src/ONEVO.Domain/Features/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Integration/Monitoring/Biometrics
git commit -m "feat(monitoring): add biometric persistence"
```

---

### Task 3: Implement the AWS Rekognition provider boundary

**Files:**
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\ServiceInterfaces\IBiometricVerificationProvider.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\AwsRekognitionOptions.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\BiometricVerificationOptions.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Biometrics\AwsRekognitionBiometricProvider.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\DependencyInjection.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\Biometrics\AwsRekognitionBiometricProviderTests.cs`
- Create: `C:\HR\HRMS-Backend-v1\docs\operations\aws-rekognition-biometric-setup.md`

**Interfaces:**
- Produces:

```csharp
Task<LivenessSession> CreateSessionAsync(BiometricAttemptPurpose purpose, CancellationToken ct);
Task<LivenessResult> GetResultAsync(string sessionId, CancellationToken ct);
Task<FaceComparisonResult> CompareFacesAsync(Stream trustedReference, ReadOnlyMemory<byte> currentReference, CancellationToken ct);
Task<TemporaryAwsCredentials> IssueStartCredentialsAsync(Guid attemptId, CancellationToken ct);
```

- [ ] **Step 1: Write tests with mocked AWS clients**

Assert exact Mumbai region, challenge type, KMS key forwarding, 15-minute-or-less STS duration, result mapping, and no credential values in logged state.

- [ ] **Step 2: Add AWS SDK v4 packages and confirm tests fail for the missing provider**

Add package references compatible with the existing AWS SDK v4 line:

```xml
<PackageReference Include="AWSSDK.Rekognition" Version="4.0.4.1" />
<PackageReference Include="AWSSDK.SecurityToken" Version="4.0.100.8" />
```

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AwsRekognitionBiometricProviderTests
```

- [ ] **Step 3: Implement and validate options**

```csharp
public sealed class AwsRekognitionOptions
{
    public const string SectionName = "Biometrics:AwsRekognition";
    public string Region { get; init; } = "ap-south-1";
    public string KmsKeyId { get; init; } = string.Empty;
    public string ClientRoleArn { get; init; } = string.Empty;
    public int CredentialLifetimeMinutes { get; init; } = 15;
}

public sealed class BiometricVerificationOptions
{
    public const string SectionName = "Biometrics:Verification";
    public decimal LivenessConfidenceThreshold { get; init; } = 90m;
    public decimal FaceSimilarityThreshold { get; init; } = 90m;
    public int MaxAutomaticAttempts { get; init; } = 3;
}
```

Validation rejects non-Mumbai region, empty KMS/role values, credential lifetime outside 5–15 minutes, confidence values outside 80–99, and a retry count outside 1–3. The initial release values are 90 liveness, 90 face similarity, and three fresh sessions. They are platform release configuration—not tenant settings—and may change only through measured pilot evidence. Use default AWS credential resolution/IAM role; never add access-key configuration fields.

- [ ] **Step 4: Document and provision the Mumbai AWS boundary**

Create the operations document with the exact staging and production resources: a backend runtime role, a separate client role assumed through STS, a customer-managed KMS key, CloudTrail coverage, alarms, and the fixed `ap-south-1` region. The backend role may create/read liveness sessions, compare faces, use the configured KMS key, and assume the client role. The client role may only call `rekognition:StartFaceLivenessSession`; it must not call result, comparison, S3, R2, or STS APIs. Add an `aws:RequestedRegion = ap-south-1` condition wherever the AWS action supports it, a trust-policy condition that binds assumptions to this backend, and the organization's Rekognition AI-services opt-out procedure.

Record the real role ARNs and KMS key ARN in the deployment secret/configuration system, never in Git. Verify the client policy before staging:

```powershell
aws iam simulate-principal-policy --policy-source-arn $env:ONEVO_BIOMETRIC_CLIENT_ROLE_ARN --action-names rekognition:StartFaceLivenessSession --region ap-south-1
aws iam simulate-principal-policy --policy-source-arn $env:ONEVO_BIOMETRIC_CLIENT_ROLE_ARN --action-names rekognition:GetFaceLivenessSessionResults rekognition:CompareFaces s3:GetObject --region ap-south-1
```

Expected: `StartFaceLivenessSession` is allowed and every result/comparison/storage action is denied.

- [ ] **Step 5: Implement provider mapping**

Use `CreateFaceLivenessSessionAsync`, `GetFaceLivenessSessionResultsAsync`, `CompareFacesAsync`, and STS `AssumeRoleAsync`. Return byte arrays only in method results and dispose AWS response streams promptly.

- [ ] **Step 6: Run tests and commit**

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AwsRekognitionBiometricProviderTests
git add src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj src/ONEVO.Application/Features/Monitoring/Biometrics src/ONEVO.Infrastructure/Services/Monitoring/Biometrics src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics docs/operations/aws-rekognition-biometric-setup.md
git commit -m "feat(monitoring): add AWS biometric provider"
```

---

### Task 4: Build enrollment commands and tray-device endpoints

**Files:**
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\RepositoryInterfaces\IBiometricRepository.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Persistence\Repositories\Monitoring\Biometrics\EfBiometricRepository.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateEnrollmentAttempt\CreateEnrollmentAttemptCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateEnrollmentAttempt\CreateEnrollmentAttemptCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CreateEnrollmentAttempt\CreateEnrollmentAttemptCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteEnrollmentAttempt\CompleteEnrollmentAttemptCommand.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteEnrollmentAttempt\CompleteEnrollmentAttemptCommandHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Commands\CompleteEnrollmentAttempt\CompleteEnrollmentAttemptCommandValidator.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Queries\GetBiometricProfile\GetBiometricProfileQuery.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Monitoring\Biometrics\Queries\GetBiometricProfile\GetBiometricProfileQueryHandler.cs`
- Create: `C:\HR\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\Biometrics\BiometricEnrollmentController.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Application\Features\Storage\File\Helpers\UploadPurposeCatalog.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Storage\File\UploadPurposePolicy.cs`
- Modify: `C:\HR\HRMS-Backend-v1\src\ONEVO.Infrastructure\DependencyInjection.cs`
- Create: `C:\HR\HRMS-Backend-v1\tests\ONEVO.Tests.Integration\Monitoring\Biometrics\BiometricEnrollmentIntegrationTests.cs`

**Interfaces:**
- `POST enrollment-attempts` consumes `consent_version` and `consented_at`; identity comes from JWT.
- Response contains ONEVO attempt ID, AWS session ID, `ap-south-1`, challenge, temporary credentials, credential expiry, and session expiry.
- Completion accepts no result boolean; backend fetches AWS results itself.

- [ ] **Step 1: Write HTTP integration tests**

Cover valid creation, missing employee, missing consent, cross-device completion, low liveness rejection, success storing an R2 `FileRecordId`, and re-enrollment superseding the old profile.

- [ ] **Step 2: Run tests and confirm endpoints return 404**

```powershell
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricEnrollmentIntegrationTests
```

- [ ] **Step 3: Implement repository and create handler**

Create attempt status `Created`, call the provider, then persist AWS session metadata before returning credentials. Bind employee/device from resolver/current-device services. Use stable errors such as `employee_not_found`, `biometric_consent_required`, and `biometric_provider_unavailable`.

- [ ] **Step 4: Implement completion handler**

Transition `Created/Capturing -> Verifying`, retrieve the provider result, reject below the platform liveness threshold, upload the successful reference with purpose `biometric_enrollment_reference`, supersede the prior profile, create the new active profile, and mark the attempt `Verified` in one application transaction. Delete an uploaded orphan through the storage cleanup mechanism if persistence fails.

- [ ] **Step 5: Add endpoint documentation and run tests**

Create plain Markdown API docs under:

```text
docs/postman-request/Monitoring Biometrics/Create Enrollment Attempt.md
docs/postman-request/Monitoring Biometrics/Complete Enrollment Attempt.md
docs/postman-request/Monitoring Biometrics/Get Biometric Profile.md
```

Run and commit only the enrollment slice:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AwsRekognitionBiometricProviderTests
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~BiometricEnrollmentIntegrationTests
git add src/ONEVO.Application/Features/Monitoring/Biometrics src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Biometrics src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/BiometricEnrollmentController.cs src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Integration/Monitoring/Biometrics 'docs/postman-request/Monitoring Biometrics'
git commit -m "feat(monitoring): enroll verified biometric profile"
```

---

### Task 5: Prove Windows laptop-camera liveness and wire onboarding

**Files:**
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\BiometricWeb\package.json`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\BiometricWeb\package-lock.json`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\BiometricWeb\src\App.tsx`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\BiometricWeb\src\bridge.ts`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Services\BiometricCaptureHost.cs`
- Create: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ViewModels\BiometricEnrollmentViewModel.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Views\PhotoCaptureWindow.xaml`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\Views\PhotoCaptureWindow.xaml.cs`
- Modify: `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj`
- Create: `C:\HR\tray_app_maui\tests\ONEVO.Agent.TrayApp.Tests\Services\BiometricCaptureHostTests.cs`
- Create: `C:\HR\tray_app_maui\docs\testing\windows-biometric-compatibility-checklist.md`

**Interfaces:**
- JavaScript input: session ID, region, challenge, temporary access key/secret/session token, expiry.
- JavaScript output: `ready`, `capture_started`, `analysis_complete`, or stable error code. No video/image payload.

- [ ] **Step 1: Write host origin/permission and bridge tests**

Assert only `https://biometric.onevo.local` receives camera permission; microphone, geolocation, unknown origins, and virtual-camera selections are denied. Assert JS messages deserialize to the four allowed event types.

- [ ] **Step 2: Create and lock the React module**

From `C:\HR\tray_app_maui\ONEVO.Agent.TrayApp\BiometricWeb`:

```powershell
npm init -y
npm install react@19.2.8 react-dom@19.2.8 aws-amplify@6 @aws-amplify/ui-react-liveness@3
npm install --save-dev vite@8.1.5 typescript @vitejs/plugin-react
```

Commit `package-lock.json`; CI/build uses `npm ci` and `npm run build`.

- [ ] **Step 3: Implement the detector and native bridge**

```tsx
<FaceLivenessDetectorCore
  sessionId={contract.sessionId}
  region="ap-south-1"
  config={{ credentialProvider: async () => contract.credentials }}
  onAnalysisComplete={() => bridge.post("analysis_complete")}
  onError={(error) => bridge.post(mapStableError(error))}
/>
```

Map built assets into the app and use WebView2 virtual-host folder mapping. In `PermissionRequested`, allow only a user-initiated `Camera` request from the exact virtual origin.

- [ ] **Step 4: Replace onboarding's fake face flag**

Remove `Preferences.Set("onevo.face_verified", true)` as the source of truth. Onboarding advances only after Service/backend enrollment status is `Verified`; Preferences may cache display state but cannot authorize check-in.

- [ ] **Step 5: Run automated and real-device gates**

```powershell
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~BiometricCaptureHostTests
dotnet build ONEVO.Agent.slnx
```

Manually record PASS/FAIL for Windows 10/11, three-to-five laptop models, 640x480+ and 15 FPS+, low light, glasses, camera denied, camera occupied, external camera, slow network, cancellation, and restart. Complete a real Mumbai staging session and confirm backend receives confidence/reference data without local media files.

- [ ] **Step 6: Commit and apply the milestone gate**

```powershell
git add ONEVO.Agent.TrayApp/BiometricWeb ONEVO.Agent.TrayApp/Services/BiometricCaptureHost.cs ONEVO.Agent.TrayApp/ViewModels/BiometricEnrollmentViewModel.cs ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml.cs ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj tests/ONEVO.Agent.TrayApp.Tests/Services/BiometricCaptureHostTests.cs docs/testing/windows-biometric-compatibility-checklist.md
git commit -m "feat(tray): enroll employee with AWS face liveness"
```

Do not start Part 2 unless automated tests pass and the staging compatibility checklist has an accepted laptop-camera result.
