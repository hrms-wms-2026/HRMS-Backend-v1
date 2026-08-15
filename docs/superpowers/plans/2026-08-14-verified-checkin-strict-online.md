# Verified Employee Check-In — Plan 2: Strict Online Check-In — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make CLOCK IN require a real, backend-verified biometric check-in (fresh GPS + AWS Rekognition Face Liveness + face match against the Plan-1 enrollment reference) before monitoring can go Active — for tenants that opt in via `CameraVerificationEnabled`. Legacy tenants and dev bootstrap are untouched.

**Architecture:** Reuses Plan 1's `BiometricVerificationAttempt`/`IBiometricVerificationProvider`/AWS Rekognition infra with `Purpose = CheckIn`. Adds two new backend endpoints (`check-in-attempts`, `check-in-attempts/{id}/complete`) that create an idempotent, verification-carrying `EmployeeCheckIn` row keyed by a `AttendanceSessionId` GUID the Service generates before capture starts. A new `CheckInCoordinator` in the Service orchestrates the flow and, only on a `Verified` verdict, activates monitoring using that same GUID as the `PresenceSession` id — closing the loop the design doc calls out: "the Service must not enter `MonitoringState.Active` until the backend returns an allowed check-in verdict." Everything is additive on the backend; the only behavior-changing commit is the last Tray task, and it is gated behind the existing `AgentPolicy.CameraVerificationEnabled` toggle (defaults `false`, no admin write path yet — so this cannot regress any tenant, including the local dev tenant, until someone flips it on).

**Tech Stack:** ASP.NET Core / EF Core / PostgreSQL (backend, unchanged from Plan 1), .NET MAUI Windows + WinUI3 WebView2 (Tray, unchanged from Plan 1), AWS Rekognition Face Liveness + CompareFaces (already wired in Plan 1's `AwsRekognitionBiometricVerificationProvider`).

---

## Before you start

This plan assumes Plan 1 (`docs/superpowers/plans/2026-08-13-verified-checkin-foundation.md`) is fully merged in both repos — `BiometricVerificationAttempt`, `EmployeeBiometricProfile`, `IBiometricVerificationProvider`, the enrollment endpoints, and the WebView2 capture surface (`BiometricWebView`/`BiometricEnrollmentPage`) all already exist and this plan builds directly on top of them without modifying their tested behavior (except Task 1, which fixes one Plan 1 gap — see below).

**Two things this plan explicitly does NOT do (confirm before executing):**
- Does not touch the legacy `POST /api/v1/monitoring/check-in` / `UploadFaceScan` endpoints or the `MonitoringFaceScan` table. Per the design doc these "remain for historical compatibility." `EmployeeCheckIn` gains new *nullable* columns so both the legacy writer and the new verified writer can coexist in the same table without collision (see Task 2).
- Does not implement `PendingReview` / provider-outage / offline fallback paths. Those are Plan 3/4. This plan is **strict-only**: any failure (no location, liveness fail, face mismatch, network error, no enrollment) blocks Clock In outright. No new column or branch is added for a status this plan never produces — that would be dead code.

**AWS/hardware dependency note (same caveat as Plan 1):** Tasks 4–7 call into `IBiometricVerificationProvider`, which is Plan-1 infrastructure already built and already unit/integration-tested against a fake provider (`FakeBiometricVerificationProvider` in `BiometricsTestFactory.cs`). Nothing in this plan requires new AWS IAM/KMS provisioning — Plan 1's Task 1/0/21 gate already covers that. This plan is safe to build and test end-to-end (backend integration tests + Service/Tray unit tests) without live AWS access, exactly like Plan 1's Tasks 2–20 were.

---

## File Structure

**Backend (`HRMS-Backend-v1`):**
```
src/ONEVO.Domain/Features/Monitoring/
  Biometrics/Entities/EmployeeBiometricProfile.cs          [MODIFY] +ReferenceFileId
  CheckIn/Entities/EmployeeCheckIn.cs                       [MODIFY] +5 nullable columns
  CheckIn/Entities/CheckInVerificationStatus.cs             [NEW] Verified/Rejected constants

src/ONEVO.Infrastructure/Persistence/
  Configurations/Monitoring/Biometrics/EmployeeBiometricProfileConfiguration.cs [MODIFY]
  Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs             [MODIFY]
  Repositories/Monitoring/CheckIn/EfCheckInRepository.cs                        [MODIFY]
  Migrations/<ts>_AddBiometricProfileReferenceFileId.cs                         [NEW]
  Migrations/<ts>_AddCheckInVerificationFields.cs                               [NEW]

src/ONEVO.Application/Features/Monitoring/
  Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs [MODIFY]
  Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommand.cs           [NEW]
  Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommandValidator.cs  [NEW]
  Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommandHandler.cs    [NEW]
  Biometrics/DTOs/Responses/CheckInAttemptResponseDto.cs                           [NEW]
  Biometrics/Commands/CompleteCheckInAttempt/CompleteCheckInAttemptCommand.cs          [NEW]
  Biometrics/Commands/CompleteCheckInAttempt/CompleteCheckInAttemptCommandValidator.cs [NEW]
  Biometrics/Commands/CompleteCheckInAttempt/CompleteCheckInAttemptCommandHandler.cs   [NEW]
  CheckIn/DTOs/Responses/CheckInVerificationResultDto.cs                           [NEW]
  CheckIn/RepositoryInterfaces/ICheckInRepository.cs                              [MODIFY]

src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/MonitoringBiometricsController.cs [MODIFY]

tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/
  CreateCheckInAttemptCommandHandlerTests.cs   [NEW]
  CompleteCheckInAttemptCommandHandlerTests.cs [NEW]
tests/ONEVO.Tests.Integration/Monitoring/Biometrics/
  CheckInVerificationIntegrationTests.cs       [NEW]
```

**MAUI (`tray_app_maui`):**
```
ONEVO.Agent.Service/Lifecycle/PresenceSession.cs        [MODIFY] +ClockIn(at, sessionId) overload
ONEVO.Agent.Shared/IPC/IpcMessages.cs                    [MODIFY] +check-in IPC contracts
ONEVO.Agent.Service/Api/AgentApiRoutes.cs                [MODIFY] +2 routes
ONEVO.Agent.Service/Api/OnevoApiClient.cs                [MODIFY] +2 methods, +wire records
ONEVO.Agent.Service/Biometrics/CheckInCoordinator.cs     [NEW]
ONEVO.Agent.Service/AgentWorker.cs                       [MODIFY] +wiring, refactor ExecuteClockIn
ONEVO.Agent.Service/Program.cs                           [MODIFY] +1 DI registration

ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs         [MODIFY] +2 methods
ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs          [MODIFY] +2 methods, +allowlist
ONEVO.Agent.TrayApp/ViewModels/CheckInBiometricViewModel.cs [NEW]
ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs        [MODIFY] +ILocationService, rewrite branch
ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs [MODIFY] retire clockin branch
ONEVO.Agent.TrayApp/Views/CheckInBiometricPage.xaml(.cs)  [NEW]
ONEVO.Agent.TrayApp/Views/AppShell.xaml                   [MODIFY] +1 route
ONEVO.Agent.TrayApp/MauiProgram.cs                        [MODIFY] +DI

tests/ONEVO.Agent.Service.Tests/Biometrics/CheckInCoordinatorTests.cs [NEW]
tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs          [MODIFY]
tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs [MODIFY] (RecordingPipeClient)
tests/ONEVO.Agent.TrayApp.Tests/ViewModels/CheckInBiometricViewModelTests.cs [NEW]
tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs   [MODIFY]
```

---

## Task 1: Fix Plan 1 gap — `EmployeeBiometricProfile.ReferenceFileId`

Plan 1 stored `ReferenceStorageKey` (a string) on the enrollment profile, but every read path on `IFileStorageService` (`OpenReadAsync`, `GetSignedUrlAsync`) takes a **Guid file id**, not a storage key. Without this fix, Task 5 (CompareFaces) has no way to fetch the enrollment reference image back out of storage.

**Files:**
- Modify: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/EmployeeBiometricProfile.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_AddBiometricProfileReferenceFileId.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs` (existing file — extend)

- [ ] **Step 1: Add the column to the entity**

In `EmployeeBiometricProfile.cs`, add directly below `ReferenceStorageKey`:

```csharp
    /// <summary>R2 storage key of the private reference image (design: "private and encrypted in R2").</summary>
    public string ReferenceStorageKey { get; set; } = string.Empty;

    /// <summary>File id for IFileStorageService.OpenReadAsync — the read path takes a Guid, not
    /// the storage key. Nullable only for schema-migration safety; always populated at creation
    /// (see CompleteEnrollmentAttemptCommandHandler) — no profile should ever be missing this in practice.</summary>
    public Guid? ReferenceFileId { get; set; }
```

- [ ] **Step 2: Extend the existing handler test to assert the new field is set**

Open `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs`. Find the test that asserts on the profile returned/persisted after a successful completion (the "happy path" test). Add this assertion alongside the existing ones that check `ReferenceStorageKey`:

```csharp
        Assert.NotNull(capturedProfile.ReferenceFileId);
        Assert.NotEqual(Guid.Empty, capturedProfile.ReferenceFileId!.Value);
```

(If the existing test captures the added profile via a mock `IBiometricRepository.AddProfileAsync` callback — check the file for the exact capture mechanism already in place — attach this assertion to that same captured instance. Do not change the capture mechanism itself.)

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteEnrollmentAttemptCommandHandlerTests" -c Release`
Expected: FAIL — `capturedProfile.ReferenceFileId` is null (property doesn't populate yet).

- [ ] **Step 4: Populate the field in the handler**

In `CompleteEnrollmentAttemptCommandHandler.cs`, the profile is built right after the upload:

```csharp
        var profile = new EmployeeBiometricProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = attempt.EmployeeId,
            UserId = _device.UserId,
            Provider = "aws_rekognition",
            Region = attempt.AwsRegion,
            ReferenceStorageKey = uploadResult.Value!.StorageKey,
            ReferenceFileId = uploadResult.Value!.Id,
            Status = BiometricProfileStatus.Active,
```

(Single line added — `ReferenceFileId = uploadResult.Value!.Id,` — right after the existing `ReferenceStorageKey` line. `FileRecordDto.Id` is the first positional parameter of that record, already returned by `IFileStorageService.UploadAsync`.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteEnrollmentAttemptCommandHandlerTests" -c Release`
Expected: PASS (all tests in the file, not just the new assertion).

- [ ] **Step 6: Generate and hand-verify the migration**

Run (from `src/ONEVO.Api`, with a syntactically valid fake connection string exactly as Plan 1 Task 6 did):

```bash
ConnectionStrings__MigrationConnection="Host=localhost;Database=fake;Username=fake;Password=fake" \
  dotnet ef migrations add AddBiometricProfileReferenceFileId --project ../ONEVO.Infrastructure --startup-project . --configuration Release
```

Expected generated migration (`src/ONEVO.Infrastructure/Migrations/<timestamp>_AddBiometricProfileReferenceFileId.cs`) — verify it matches this shape (EF Core + Npgsql snake_case convention, single nullable column, no RLS change since the table already has RLS from Plan 1's migration):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<Guid>(
        name: "reference_file_id",
        table: "employee_biometric_profiles",
        type: "uuid",
        nullable: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "reference_file_id",
        table: "employee_biometric_profiles");
}
```

If the generated file differs meaningfully (extra unrelated diffs), stop and investigate — the model snapshot may be out of sync with the DB; do not hand-edit around a real drift.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/EmployeeBiometricProfile.cs \
        src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs \
        tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs \
        src/ONEVO.Infrastructure/Migrations/
git commit -m "fix: persist ReferenceFileId on EmployeeBiometricProfile so CompareFaces can read it back"
```

---

## Task 2: `EmployeeCheckIn` schema additions

Add the verification fields the design doc calls for. All five are **nullable** — the legacy `SubmitCheckInCommandHandler` (untouched, still live) creates rows without any of them, and a partial unique index on `AttendanceSessionId` keeps this safe (`WHERE attendance_session_id IS NOT NULL` — the same trick Plan 1 used for `EmployeeBiometricProfile`'s active-profile index).

**Files:**
- Modify: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/EmployeeCheckIn.cs`
- Create: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/CheckInVerificationStatus.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<ts>_AddCheckInVerificationFields.cs`

- [ ] **Step 1: Add the constants class**

```csharp
namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

/// <summary>Strict-online outcomes only (Plan 2). PendingReview/offline-fallback statuses are
/// Plan 3/4 — do not add them here until a handler actually produces them.</summary>
public static class CheckInVerificationStatus
{
    public const string Verified = "verified";
    public const string Rejected = "rejected";
}
```

- [ ] **Step 2: Add the columns to the entity**

In `EmployeeCheckIn.cs`, add after `DeviceRegistrationId`:

```csharp
    public Guid DeviceRegistrationId { get; set; }

    /// <summary>Set only by the verified check-in flow (Plan 2). Null for legacy SubmitCheckIn rows.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>The PresenceSession id the Tray/Service generated before capture started — the
    /// idempotency + work-session correlation key for the verified flow. Null for legacy rows;
    /// unique per tenant when present (see EmployeeCheckInConfiguration's partial index).</summary>
    public Guid? AttendanceSessionId { get; set; }

    /// <summary>Links to the BiometricVerificationAttempt (Purpose = CheckIn) that produced this row.</summary>
    public Guid? BiometricAttemptId { get; set; }

    /// <summary>CheckInVerificationStatus.Verified | Rejected. Null for legacy rows (no verification ran).</summary>
    public string? VerificationStatus { get; set; }

    /// <summary>Client-reported timestamp the location fix was captured — used to enforce the
    /// "fresh location" rule (see CreateCheckInAttemptCommandHandler). Null for legacy rows.</summary>
    public DateTimeOffset? LocationCapturedAt { get; set; }
```

- [ ] **Step 3: Update the EF configuration**

```csharp
public class EmployeeCheckInConfiguration : IEntityTypeConfiguration<EmployeeCheckIn>
{
    public void Configure(EntityTypeBuilder<EmployeeCheckIn> builder)
    {
        builder.ToTable("employee_check_ins");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.LocationAddress).HasMaxLength(500);
        builder.Property(e => e.DeviceSerialNumber).HasMaxLength(200);
        builder.Property(e => e.VerificationStatus).HasMaxLength(20);

        builder.HasOne(e => e.FaceScan)
               .WithOne()
               .HasForeignKey<EmployeeCheckIn>(e => e.FaceScanId)
               .IsRequired(false)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.CheckedInAt });
        builder.HasIndex(e => new { e.TenantId, e.DeviceRegistrationId });

        // Idempotency key for the verified flow — a retried CreateCheckInAttempt call reuses the
        // same AttendanceSessionId, so this must reject a second distinct row for it. Partial so
        // legacy SubmitCheckIn rows (AttendanceSessionId == null) never collide.
        builder.HasIndex(e => new { e.TenantId, e.AttendanceSessionId })
               .IsUnique()
               .HasFilter("attendance_session_id IS NOT NULL")
               .HasDatabaseName("ix_employee_check_ins_tenant_attendance_session");
    }
}
```

- [ ] **Step 4: Generate and hand-verify the migration**

```bash
ConnectionStrings__MigrationConnection="Host=localhost;Database=fake;Username=fake;Password=fake" \
  dotnet ef migrations add AddCheckInVerificationFields --project ../ONEVO.Infrastructure --startup-project . --configuration Release
```

Expected shape:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<Guid>(
        name: "employee_id", table: "employee_check_ins", type: "uuid", nullable: true);

    migrationBuilder.AddColumn<Guid>(
        name: "attendance_session_id", table: "employee_check_ins", type: "uuid", nullable: true);

    migrationBuilder.AddColumn<Guid>(
        name: "biometric_attempt_id", table: "employee_check_ins", type: "uuid", nullable: true);

    migrationBuilder.AddColumn<string>(
        name: "verification_status", table: "employee_check_ins",
        type: "character varying(20)", maxLength: 20, nullable: true);

    migrationBuilder.AddColumn<DateTimeOffset>(
        name: "location_captured_at", table: "employee_check_ins",
        type: "timestamp with time zone", nullable: true);

    migrationBuilder.CreateIndex(
        name: "ix_employee_check_ins_tenant_attendance_session",
        table: "employee_check_ins",
        columns: new[] { "tenant_id", "attendance_session_id" },
        unique: true,
        filter: "attendance_session_id IS NOT NULL");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(name: "ix_employee_check_ins_tenant_attendance_session", table: "employee_check_ins");
    migrationBuilder.DropColumn(name: "location_captured_at", table: "employee_check_ins");
    migrationBuilder.DropColumn(name: "verification_status", table: "employee_check_ins");
    migrationBuilder.DropColumn(name: "biometric_attempt_id", table: "employee_check_ins");
    migrationBuilder.DropColumn(name: "attendance_session_id", table: "employee_check_ins");
    migrationBuilder.DropColumn(name: "employee_id", table: "employee_check_ins");
}
```

No RLS block needed — `employee_check_ins` already has RLS enabled from the migration that first created it.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/ \
        src/ONEVO.Infrastructure/Persistence/Configurations/Monitoring/CheckIn/EmployeeCheckInConfiguration.cs \
        src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add verification fields to EmployeeCheckIn for the strict-online check-in flow"
```

---

## Task 3: Repository lookup for idempotency

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/CheckIn/RepositoryInterfaces/ICheckInRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/EfCheckInRepository.cs`

- [ ] **Step 1: Add the method to the interface**

```csharp
public interface ICheckInRepository
{
    Task AddCheckInAsync(EmployeeCheckIn checkIn, CancellationToken ct);
    Task<EmployeeCheckIn?> FindCheckInAsync(Guid checkInId, Guid tenantId, CancellationToken ct);

    /// <summary>Idempotency lookup for the verified flow (Plan 2) — a retried
    /// CompleteCheckInAttempt call for the same AttendanceSessionId returns the already-landed row.</summary>
    Task<EmployeeCheckIn?> FindByAttendanceSessionIdAsync(Guid attendanceSessionId, Guid tenantId, CancellationToken ct);

    Task AddFaceScanAsync(MonitoringFaceScan faceScan, CancellationToken ct);
    Task UpdateFaceScanStatusAsync(Guid faceScanId, string status, CancellationToken ct);
}
```

- [ ] **Step 2: Implement it**

```csharp
    public async Task<EmployeeCheckIn?> FindByAttendanceSessionIdAsync(
        Guid attendanceSessionId, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeCheckIns
            .FirstOrDefaultAsync(c => c.AttendanceSessionId == attendanceSessionId && c.TenantId == tenantId, ct);
```

(Insert directly below `FindCheckInAsync` in `EfCheckInRepository.cs`, matching its exact style.)

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/CheckIn/RepositoryInterfaces/ICheckInRepository.cs \
        src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/CheckIn/EfCheckInRepository.cs
git commit -m "feat: add ICheckInRepository.FindByAttendanceSessionIdAsync for check-in idempotency"
```

---

## Task 4: `CreateCheckInAttempt` command

Mirrors `CreateEnrollmentAttemptCommandHandler`, but: takes a request body (AttendanceSessionId + location), requires an existing **Active** `EmployeeBiometricProfile` (nothing to compare against otherwise), enforces "fresh location" (reject if the client-reported capture time is more than 120 seconds old), and sets `Purpose = CheckIn` + `AttendanceSessionId` on the attempt (the column Plan 1 already added but never populated).

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommand.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateCheckInAttempt/CreateCheckInAttemptCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs/Responses/CheckInAttemptResponseDto.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CreateCheckInAttemptCommandHandlerTests.cs`

- [ ] **Step 1: Command + DTO**

```csharp
// CreateCheckInAttemptCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateCheckInAttempt;

public record CreateCheckInAttemptCommand(
    Guid AttendanceSessionId,
    double Latitude,
    double Longitude,
    double? LocationAccuracy,
    DateTimeOffset LocationCapturedAt) : IRequest<Result<CheckInAttemptResponseDto>>;
```

```csharp
// CheckInAttemptResponseDto.cs
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

public record CheckInAttemptResponseDto(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);
```

- [ ] **Step 2: Validator**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateCheckInAttempt;

public class CreateCheckInAttemptCommandValidator : AbstractValidator<CreateCheckInAttemptCommand>
{
    public CreateCheckInAttemptCommandValidator()
    {
        RuleFor(x => x.AttendanceSessionId).NotEmpty();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.LocationCapturedAt).NotEqual(default(DateTimeOffset));
    }
}
```

- [ ] **Step 3: Write the failing handler test (happy path)**

Mirror `CreateEnrollmentAttemptCommandHandlerTests.cs`'s exact conventions — field-based mocks (not a tuple-returning `Build()` helper), `Tenant` from `ONEVO.Domain.Features.InfrastructureModule.Entities` constructed as `new() { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active }`, and `Result<T>.UnprocessableEntity` confirmed (`src/ONEVO.Application/Common/Models/Result.cs`) to set `StatusCode = 422`:

```csharp
// CreateCheckInAttemptCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateCheckInAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CreateCheckInAttemptCommandHandlerTests
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
    private readonly DateTimeOffset _now = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private CreateCheckInAttemptCommandHandler CreateHandler() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _employeeResolver.Object, _provider.Object, _clock.Object, _unitOfWork.Object);

    private Tenant Tenant() => new() { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active };

    private void SetupHappyPath()
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(_deviceId);
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default)).ReturnsAsync(Tenant());
        _clock.SetupGet(c => c.UtcNow).Returns(_now);
        _employeeResolver.Setup(r => r.ResolveEmployeeIdAsync(_userId, _tenantId, default))
            .ReturnsAsync(_employeeId);
        _repository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, default))
            .ReturnsAsync(new EmployeeBiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Active
            });
        _provider.Setup(p => p.CreateLivenessSessionAsync(It.IsAny<CreateLivenessSessionRequest>(), default))
            .ReturnsAsync(new FaceLivenessSessionCreated("aws-session-1"));
        _provider.Setup(p => p.IssueScopedCaptureCredentialsAsync("aws-session-1", default))
            .ReturnsAsync(new ScopedCaptureCredentials("AKIA", "secret", "token", "ap-south-1", _now.AddMinutes(15)));
    }

    [Fact]
    public async Task Handle_ActiveProfileAndFreshLocation_CreatesAttemptAndReturnsSession()
    {
        SetupHappyPath();

        var result = await CreateHandler().Handle(new CreateCheckInAttemptCommand(
            Guid.NewGuid(), 12.9716, 77.5946, 15.0, _now.AddSeconds(-10)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("aws-session-1", result.Value!.AwsSessionId);
        _repository.Verify(r => r.AddAttemptAsync(
            It.Is<BiometricVerificationAttempt>(a => a.Purpose == BiometricAttemptPurpose.CheckIn),
            default), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveProfile_ReturnsUnprocessableEntity()
    {
        SetupHappyPath();
        _repository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, default))
            .ReturnsAsync((EmployeeBiometricProfile?)null);

        var result = await CreateHandler().Handle(new CreateCheckInAttemptCommand(
            Guid.NewGuid(), 12.9716, 77.5946, 15.0, _now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_StaleLocation_ReturnsUnprocessableEntity()
    {
        SetupHappyPath();

        var result = await CreateHandler().Handle(new CreateCheckInAttemptCommand(
            Guid.NewGuid(), 12.9716, 77.5946, 15.0, _now.AddSeconds(-121)), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateCheckInAttemptCommandHandlerTests" -c Release`
Expected: FAIL to compile — `CreateCheckInAttemptCommandHandler` doesn't exist yet.

- [ ] **Step 5: Implement the handler**

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

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateCheckInAttempt;

public class CreateCheckInAttemptCommandHandler
    : IRequestHandler<CreateCheckInAttemptCommand, Result<CheckInAttemptResponseDto>>
{
    private static readonly TimeSpan MaxLocationAge = TimeSpan.FromSeconds(120);

    private readonly IBiometricRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IEmployeeIdentityResolver _employeeResolver;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCheckInAttemptCommandHandler(
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

    public async Task<Result<CheckInAttemptResponseDto>> Handle(
        CreateCheckInAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<CheckInAttemptResponseDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<CheckInAttemptResponseDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var now = _clock.UtcNow;
        if (now - request.LocationCapturedAt > MaxLocationAge)
        {
            return Result<CheckInAttemptResponseDto>.UnprocessableEntity(
                "Location fix is stale. Capture location again and retry.");
        }

        var employeeId = await _employeeResolver.ResolveEmployeeIdAsync(
            _device.UserId, _device.TenantId, cancellationToken);
        if (employeeId is null)
        {
            return Result<CheckInAttemptResponseDto>.UnprocessableEntity(
                "No HR employee profile is linked to this account yet.");
        }

        var profile = await _repository.FindActiveProfileAsync(employeeId.Value, _device.TenantId, cancellationToken);
        if (profile is null)
        {
            return Result<CheckInAttemptResponseDto>.UnprocessableEntity(
                "No active biometric enrollment. Complete enrollment before checking in.");
        }

        var session = await _provider.CreateLivenessSessionAsync(
            new CreateLivenessSessionRequest("FaceMovementAndLightChallenge", KmsKeyId: string.Empty),
            cancellationToken);

        var credentials = await _provider.IssueScopedCaptureCredentialsAsync(
            session.AwsSessionId, cancellationToken);

        var attempt = new BiometricVerificationAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            EmployeeId = employeeId.Value,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            Purpose = BiometricAttemptPurpose.CheckIn,
            AttendanceSessionId = request.AttendanceSessionId,
            AwsSessionId = session.AwsSessionId,
            AwsRegion = credentials.Region,
            ChallengeType = "FaceMovementAndLightChallenge",
            AwsSessionExpiresAt = credentials.Expiration,
            Status = BiometricAttemptStatus.Created,
            CreatedAt = now
        };

        await _repository.AddAttemptAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CheckInAttemptResponseDto>.Success(new CheckInAttemptResponseDto(
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

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateCheckInAttemptCommandHandlerTests" -c Release`
Expected: PASS (3/3).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CreateCheckInAttempt/ \
        src/ONEVO.Application/Features/Monitoring/Biometrics/DTOs/Responses/CheckInAttemptResponseDto.cs \
        tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CreateCheckInAttemptCommandHandlerTests.cs
git commit -m "feat: add CreateCheckInAttempt command — Purpose=CheckIn liveness session with enrollment + freshness gates"
```

---

## Task 5: `CompleteCheckInAttempt` command

Mirrors `CompleteEnrollmentAttemptCommandHandler`'s liveness-status handling, then adds the Plan-2-specific steps: fetch the stored enrollment reference (via `ReferenceFileId` from Task 1), `CompareFacesAsync` against the fresh capture, and create (or return, if retried) the idempotent `EmployeeCheckIn` row keyed by `AttendanceSessionId`.

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteCheckInAttempt/CompleteCheckInAttemptCommand.cs`
- Create: `.../CompleteCheckInAttempt/CompleteCheckInAttemptCommandValidator.cs`
- Create: `.../CompleteCheckInAttempt/CompleteCheckInAttemptCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/CheckInVerificationResultDto.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteCheckInAttemptCommandHandlerTests.cs`

- [ ] **Step 1: Command, validator, DTO**

```csharp
// CompleteCheckInAttemptCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteCheckInAttempt;

public record CompleteCheckInAttemptCommand(Guid AttemptId) : IRequest<Result<CheckInVerificationResultDto>>;
```

```csharp
// CompleteCheckInAttemptCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteCheckInAttempt;

public class CompleteCheckInAttemptCommandValidator : AbstractValidator<CompleteCheckInAttemptCommand>
{
    public CompleteCheckInAttemptCommandValidator()
    {
        RuleFor(x => x.AttemptId).NotEmpty();
    }
}
```

```csharp
// CheckInVerificationResultDto.cs
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record CheckInVerificationResultDto(
    [property: JsonPropertyName("check_in_id")] Guid CheckInId,
    [property: JsonPropertyName("attendance_session_id")] Guid AttendanceSessionId,
    [property: JsonPropertyName("verification_status")] string VerificationStatus,
    [property: JsonPropertyName("checked_in_at")] DateTimeOffset CheckedInAt);
```

- [ ] **Step 2: Write the failing handler tests**

Same field-based-mock / `InfrastructureModule.Entities.Tenant` conventions as Task 4:

```csharp
// CompleteCheckInAttemptCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteCheckInAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class CompleteCheckInAttemptCommandHandlerTests
{
    private readonly Mock<IBiometricRepository> _biometricRepository = new();
    private readonly Mock<ICheckInRepository> _checkInRepository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IBiometricVerificationProvider> _provider = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _attendanceSessionId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly Guid _referenceFileId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 14, 9, 5, 0, TimeSpan.Zero);

    private CompleteCheckInAttemptCommandHandler CreateHandler() => new(
        _biometricRepository.Object, _checkInRepository.Object, _device.Object, _tenants.Object,
        _tenantSwitcher.Object, _provider.Object, _fileStorage.Object, _clock.Object, _unitOfWork.Object);

    private Tenant Tenant() => new() { Id = _tenantId, Slug = "acme", Status = TenantStatus.Active };

    private void SetupHappyPath(string livenessStatus = "SUCCEEDED", bool faceMatches = true)
    {
        _device.SetupGet(d => d.IsAuthenticated).Returns(true);
        _device.SetupGet(d => d.TenantId).Returns(_tenantId);
        _device.SetupGet(d => d.UserId).Returns(_userId);
        _device.SetupGet(d => d.DeviceRegistrationId).Returns(Guid.NewGuid());

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, default)).ReturnsAsync(Tenant());
        _clock.SetupGet(c => c.UtcNow).Returns(_now);

        _biometricRepository.Setup(r => r.FindAttemptAsync(_attemptId, _tenantId, default))
            .ReturnsAsync(new BiometricVerificationAttempt
            {
                Id = _attemptId, TenantId = _tenantId, EmployeeId = _employeeId, UserId = _userId,
                Purpose = BiometricAttemptPurpose.CheckIn, AttendanceSessionId = _attendanceSessionId,
                AwsSessionId = "aws-session-1", Status = BiometricAttemptStatus.Created
            });

        _biometricRepository.Setup(r => r.FindActiveProfileAsync(_employeeId, _tenantId, default))
            .ReturnsAsync(new EmployeeBiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Status = BiometricProfileStatus.Active, ReferenceFileId = _referenceFileId
            });

        _provider.Setup(p => p.GetLivenessSessionResultAsync("aws-session-1", default))
            .ReturnsAsync(new FaceLivenessSessionResult(livenessStatus, 98.0, [9, 9, 9]));

        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, _referenceFileId, default))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream([1, 2, 3]), "image/jpeg")));

        _provider.Setup(p => p.CompareFacesAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), default))
            .ReturnsAsync(new FaceMatchResult(faceMatches, faceMatches ? 96.0 : 40.0));

        _checkInRepository.Setup(r => r.FindByAttendanceSessionIdAsync(_attendanceSessionId, _tenantId, default))
            .ReturnsAsync((EmployeeCheckIn?)null);
    }

    [Fact]
    public async Task Handle_LivenessSucceededAndFaceMatches_CreatesVerifiedCheckIn()
    {
        SetupHappyPath(livenessStatus: "SUCCEEDED", faceMatches: true);

        var result = await CreateHandler().Handle(new CompleteCheckInAttemptCommand(_attemptId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("verified", result.Value!.VerificationStatus);
        _checkInRepository.Verify(r => r.AddCheckInAsync(
            It.Is<EmployeeCheckIn>(c => c.AttendanceSessionId == _attendanceSessionId && c.VerificationStatus == "verified"),
            default), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_FaceDoesNotMatch_ReturnsRejectedNoCheckInRow()
    {
        SetupHappyPath(livenessStatus: "SUCCEEDED", faceMatches: false);

        var result = await CreateHandler().Handle(new CompleteCheckInAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _checkInRepository.Verify(r => r.AddCheckInAsync(It.IsAny<EmployeeCheckIn>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_LivenessFailed_ReturnsUnprocessableEntity()
    {
        SetupHappyPath(livenessStatus: "FAILED");

        var result = await CreateHandler().Handle(new CompleteCheckInAttemptCommand(_attemptId), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Retried_ReturnsExistingCheckInWithoutDuplicating()
    {
        SetupHappyPath();
        var existing = new EmployeeCheckIn
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, AttendanceSessionId = _attendanceSessionId,
            VerificationStatus = "verified", CheckedInAt = DateTimeOffset.UtcNow
        };
        _checkInRepository.Setup(r => r.FindByAttendanceSessionIdAsync(_attendanceSessionId, _tenantId, default))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(new CompleteCheckInAttemptCommand(_attemptId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value!.CheckInId);
        _checkInRepository.Verify(r => r.AddCheckInAsync(It.IsAny<EmployeeCheckIn>(), default), Times.Never);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteCheckInAttemptCommandHandlerTests" -c Release`
Expected: FAIL to compile — handler doesn't exist yet.

- [ ] **Step 4: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteCheckInAttempt;

public class CompleteCheckInAttemptCommandHandler
    : IRequestHandler<CompleteCheckInAttemptCommand, Result<CheckInVerificationResultDto>>
{
    private readonly IBiometricRepository _biometricRepository;
    private readonly ICheckInRepository _checkInRepository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IBiometricVerificationProvider _provider;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteCheckInAttemptCommandHandler(
        IBiometricRepository biometricRepository,
        ICheckInRepository checkInRepository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IBiometricVerificationProvider provider,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _biometricRepository = biometricRepository;
        _checkInRepository = checkInRepository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _provider = provider;
        _fileStorage = fileStorage;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CheckInVerificationResultDto>> Handle(
        CompleteCheckInAttemptCommand request, CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
        {
            return Result<CheckInVerificationResultDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<CheckInVerificationResultDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var attempt = await _biometricRepository.FindAttemptAsync(request.AttemptId, _device.TenantId, cancellationToken);
        if (attempt is null)
            return Result<CheckInVerificationResultDto>.NotFound("Check-in attempt not found.");

        if (attempt.UserId != _device.UserId)
            return Result<CheckInVerificationResultDto>.Forbidden();

        if (attempt.AttendanceSessionId is null)
            return Result<CheckInVerificationResultDto>.Failure("Attempt is not a check-in attempt.", 409);

        if (string.IsNullOrEmpty(attempt.AwsSessionId))
            return Result<CheckInVerificationResultDto>.Failure("Attempt has no AWS session.", 409);

        // Idempotent — a retried completion call for an already-landed session returns the same row.
        var existingCheckIn = await _checkInRepository.FindByAttendanceSessionIdAsync(
            attempt.AttendanceSessionId.Value, _device.TenantId, cancellationToken);
        if (existingCheckIn is not null)
        {
            return Result<CheckInVerificationResultDto>.Success(new CheckInVerificationResultDto(
                existingCheckIn.Id, attempt.AttendanceSessionId.Value,
                existingCheckIn.VerificationStatus ?? CheckInVerificationStatus.Rejected,
                existingCheckIn.CheckedInAt));
        }

        var now = _clock.UtcNow;

        var sessionResult = await _provider.GetLivenessSessionResultAsync(attempt.AwsSessionId, cancellationToken);

        switch (sessionResult.Status)
        {
            case "SUCCEEDED":
                break;

            case "CREATED" or "IN_PROGRESS":
                return Result<CheckInVerificationResultDto>.Failure("Liveness session has not finished yet.", 409);

            case "FAILED":
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.Rejected, out _);
                attempt.FailureCode = "liveness_failed";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<CheckInVerificationResultDto>.UnprocessableEntity("Liveness check failed.");

            case "EXPIRED":
                attempt.TryTransition(BiometricAttemptStatus.Expired, out _);
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<CheckInVerificationResultDto>.Failure("Liveness session expired.", 410);

            default:
                attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
                attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
                attempt.FailureCode = "unexpected_provider_status";
                attempt.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<CheckInVerificationResultDto>.Failure("Unexpected provider response.", 502);
        }

        if (sessionResult.ReferenceImageBytes is null || sessionResult.ReferenceImageBytes.Length == 0)
        {
            attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
            attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
            attempt.FailureCode = "missing_reference_image";
            attempt.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<CheckInVerificationResultDto>.Failure("Provider returned no reference image.", 502);
        }

        var profile = await _biometricRepository.FindActiveProfileAsync(attempt.EmployeeId, _device.TenantId, cancellationToken);
        if (profile?.ReferenceFileId is null)
        {
            attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
            attempt.TryTransition(BiometricAttemptStatus.ProviderError, out _);
            attempt.FailureCode = "no_enrollment_reference";
            attempt.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<CheckInVerificationResultDto>.UnprocessableEntity("No active enrollment reference to compare against.");
        }

        var referenceRead = await _fileStorage.OpenReadAsync(_device.TenantId, profile.ReferenceFileId.Value, cancellationToken);
        if (!referenceRead.IsSuccess)
            return Result<CheckInVerificationResultDto>.Failure(referenceRead.Error!, referenceRead.StatusCode ?? 500);

        byte[] enrollmentReferenceBytes;
        await using (var stream = referenceRead.Value!.Content)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            enrollmentReferenceBytes = buffer.ToArray();
        }

        var matchResult = await _provider.CompareFacesAsync(
            sessionResult.ReferenceImageBytes, enrollmentReferenceBytes, cancellationToken);

        attempt.TryTransition(BiometricAttemptStatus.Verifying, out _);
        attempt.LivenessConfidence = sessionResult.Confidence;
        attempt.MatchConfidence = matchResult.Similarity;

        if (!matchResult.IsMatch)
        {
            attempt.TryTransition(BiometricAttemptStatus.Rejected, out _);
            attempt.FailureCode = "face_mismatch";
            attempt.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<CheckInVerificationResultDto>.UnprocessableEntity("Face did not match enrollment reference.");
        }

        attempt.TryTransition(BiometricAttemptStatus.Verified, out _);
        attempt.UpdatedAt = now;

        var checkIn = new EmployeeCheckIn
        {
            Id = Guid.NewGuid(),
            TenantId = _device.TenantId,
            UserId = _device.UserId,
            DeviceRegistrationId = _device.DeviceRegistrationId,
            EmployeeId = attempt.EmployeeId,
            AttendanceSessionId = attempt.AttendanceSessionId,
            BiometricAttemptId = attempt.Id,
            VerificationStatus = CheckInVerificationStatus.Verified,
            LocationCapturedAt = now,
            CheckedInAt = now,
            CreatedAt = now
        };

        await _checkInRepository.AddCheckInAsync(checkIn, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CheckInVerificationResultDto>.Success(new CheckInVerificationResultDto(
            checkIn.Id, attempt.AttendanceSessionId.Value, CheckInVerificationStatus.Verified, checkIn.CheckedInAt));
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteCheckInAttemptCommandHandlerTests" -c Release`
Expected: PASS (4/4).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteCheckInAttempt/ \
        src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/CheckInVerificationResultDto.cs \
        tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteCheckInAttemptCommandHandlerTests.cs
git commit -m "feat: add CompleteCheckInAttempt command — CompareFaces + idempotent verified EmployeeCheckIn"
```

---

## Task 6: Controller endpoints

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/MonitoringBiometricsController.cs`

- [ ] **Step 1: Add the two actions + request DTO**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteCheckInAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateCheckInAttempt;
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

    // ... existing CreateEnrollmentAttempt / CompleteEnrollmentAttempt / GetProfile actions unchanged ...

    /// <summary>
    /// Creates a new AWS Face Liveness check-in session (Plan 2, strict online).
    /// Requires an active biometric enrollment and a location fix no older than 120s.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("check-in-attempts")]
    public async Task<IActionResult> CreateCheckInAttempt(
        [FromBody] CreateCheckInAttemptRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateCheckInAttemptCommand(
            request.AttendanceSessionId,
            request.Latitude,
            request.Longitude,
            request.LocationAccuracy,
            request.LocationCapturedAt), ct);

        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }

    /// <summary>
    /// Completes a check-in attempt after the WebView2 liveness capture finished — compares the
    /// fresh capture against the employee's enrollment reference and, on match, creates the
    /// verified EmployeeCheckIn row.
    /// Authorization: Bearer {tray_access_token}
    /// </summary>
    [HttpPost("check-in-attempts/{id:guid}/complete")]
    public async Task<IActionResult> CompleteCheckInAttempt(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteCheckInAttemptCommand(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);
        return Ok(result.Value);
    }
}

public record CreateCheckInAttemptRequest(
    [property: JsonPropertyName("attendance_session_id")] Guid AttendanceSessionId,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("location_accuracy")] double? LocationAccuracy,
    [property: JsonPropertyName("location_captured_at")] DateTimeOffset LocationCapturedAt);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ONEVO.Api -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Monitoring/Biometrics/MonitoringBiometricsController.cs
git commit -m "feat: add check-in-attempts endpoints to MonitoringBiometricsController"
```

---

## Task 7: Integration tests

Reuses Plan 1's `BiometricsTestFactory` (already stubs `IBiometricVerificationProvider` with `FakeBiometricVerificationProvider`, and `IFileStorageService` with `NoOpFileStorageService`). `NoOpFileStorageService.OpenReadAsync` currently always returns `NotFound` — that's fine for the mismatch/no-reference test cases, but the happy-path test needs a real read to succeed, so this task extends the fake.

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/BiometricsTestFactory.cs`
- Create: `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/CheckInVerificationIntegrationTests.cs`

- [ ] **Step 1: Make `NoOpFileStorageService.OpenReadAsync` return real bytes for any file id**

In `BiometricsTestFactory.cs`, replace the existing `OpenReadAsync` implementation:

```csharp
        public Task<Result<FileStreamDto>> OpenReadAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default)
            => Task.FromResult(Result<FileStreamDto>.Success(
                new FileStreamDto(new MemoryStream([1, 2, 3, 4, 5]), "image/jpeg")));
```

(This is safe — the only tests that relied on `NotFound` behavior were hypothetical; grep `tests/ONEVO.Tests.Integration/Monitoring/Biometrics/BiometricsIntegrationTests.cs` for `OpenReadAsync` usage before changing to confirm no existing test asserts the old 404 behavior. If one does, keep both: check `fileId` against a sentinel `Guid.Empty` to preserve the not-found case, real bytes otherwise.)

- [ ] **Step 2: Write the integration test file**

Follow the exact auth-flow helper pattern already established in `BiometricsIntegrationTests.cs` (tenant provisioning, device activation, CoreHR employee seeding, enrollment completion via the two Plan-1 endpoints) — then exercise the new check-in endpoints on top:

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.Monitoring.Biometrics;

[Collection("PostgresIntegration")]
public class CheckInVerificationIntegrationTests : IClassFixture<PostgresIntegrationFixture>
{
    private readonly BiometricsTestFactory _factory;

    public CheckInVerificationIntegrationTests(PostgresIntegrationFixture fixture)
        => _factory = new BiometricsTestFactory(fixture.ConnectionString);

    // NOTE: reuse the exact same tenant/device-activation/employee-seeding/enrollment-completion
    // helper method already defined in BiometricsIntegrationTests.cs (e.g. a shared
    // `SetUpEnrolledDeviceAsync` — check that file for its real name/signature) rather than
    // duplicating it here. If it is private to that class, promote it to `internal static` on a
    // shared helper type in this test project before wiring these tests up.

    [Fact]
    public async Task CreateThenCompleteCheckInAttempt_EnrolledEmployee_CreatesVerifiedCheckIn()
    {
        var client = _factory.CreateClient();
        var accessToken = await SetUpEnrolledDeviceAsync(client);
        var attendanceSessionId = Guid.NewGuid();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/monitoring/biometrics/check-in-attempts", new
        {
            attendance_session_id = attendanceSessionId,
            latitude = 12.9716,
            longitude = 77.5946,
            location_accuracy = 15.0,
            location_captured_at = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CheckInAttemptCreatedResponse>();
        Assert.NotNull(created);

        var completeResponse = await client.PostAsync(
            $"/api/v1/monitoring/biometrics/check-in-attempts/{created!.attempt_id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var result = await completeResponse.Content.ReadFromJsonAsync<CheckInVerificationResultResponse>();
        Assert.NotNull(result);
        Assert.Equal("verified", result!.verification_status);
        Assert.Equal(attendanceSessionId, result.attendance_session_id);
    }

    [Fact]
    public async Task CreateCheckInAttempt_NoEnrollment_ReturnsUnprocessableEntity()
    {
        var client = _factory.CreateClient();
        var accessToken = await SetUpActivatedDeviceWithoutEnrollmentAsync(client);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/v1/monitoring/biometrics/check-in-attempts", new
        {
            attendance_session_id = Guid.NewGuid(),
            latitude = 12.9716,
            longitude = 77.5946,
            location_accuracy = 15.0,
            location_captured_at = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CompleteCheckInAttempt_CalledTwice_SecondCallReturnsSameCheckIn()
    {
        var client = _factory.CreateClient();
        var accessToken = await SetUpEnrolledDeviceAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/monitoring/biometrics/check-in-attempts", new
        {
            attendance_session_id = Guid.NewGuid(),
            latitude = 12.9716,
            longitude = 77.5946,
            location_accuracy = 15.0,
            location_captured_at = DateTimeOffset.UtcNow
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CheckInAttemptCreatedResponse>();

        var first = await client.PostAsync(
            $"/api/v1/monitoring/biometrics/check-in-attempts/{created!.attempt_id}/complete", null);
        var second = await client.PostAsync(
            $"/api/v1/monitoring/biometrics/check-in-attempts/{created.attempt_id}/complete", null);

        var firstResult = await first.Content.ReadFromJsonAsync<CheckInVerificationResultResponse>();
        var secondResult = await second.Content.ReadFromJsonAsync<CheckInVerificationResultResponse>();
        Assert.Equal(firstResult!.check_in_id, secondResult!.check_in_id);
    }

    // TODO for the executing engineer: implement SetUpEnrolledDeviceAsync and
    // SetUpActivatedDeviceWithoutEnrollmentAsync by lifting the tenant/device/employee/enrollment
    // setup steps already proven in BiometricsIntegrationTests.cs — copy its exact sequence
    // (tenant provisioning, TrayActivation exchange, CoreHR Employee seed, enrollment
    // create+complete for the "enrolled" variant, enrollment create only or entirely skipped for
    // the "not enrolled" variant) rather than re-deriving it.

    private sealed record CheckInAttemptCreatedResponse(Guid attempt_id, string aws_session_id);
    private sealed record CheckInVerificationResultResponse(
        Guid check_in_id, Guid attendance_session_id, string verification_status, DateTimeOffset checked_in_at);
}
```

- [ ] **Step 3: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~CheckInVerificationIntegrationTests" -c Release`
Expected: PASS (3/3). Requires Docker for Testcontainers, exactly like the rest of `ONEVO.Tests.Integration` — if Docker is unavailable in this environment, this step cannot run; note that explicitly rather than skipping silently, same caveat as Plan 1's integration tests.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Monitoring/Biometrics/
git commit -m "test: add integration coverage for check-in-attempts endpoints"
```

---

## Task 8: `PresenceSession.ClockIn` overload

The Service must activate monitoring using the **same** GUID the Tray/Service generated before capture started (so `EmployeeWorkSession.Id`, which is already client-generated and already equals `PresenceSession.CurrentSessionId` per `SubmitWorkSessionCommandHandler`'s existing idempotent-upsert pattern, lines up with `EmployeeCheckIn.AttendanceSessionId`). Add an overload rather than changing the existing signature — `PresenceSessionTests.cs` calls `session.ClockIn(t0)` in three places and must keep passing unmodified.

**Files:**
- Modify: `ONEVO.Agent.Service/Lifecycle/PresenceSession.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs` (existing file — extend)

- [ ] **Step 1: Write the failing test**

Add to `PresenceSessionTests.cs`:

```csharp
    [Fact]
    public void ClockIn_WithExplicitSessionId_UsesThatIdInsteadOfGeneratingOne()
    {
        var session = new PresenceSession();
        var sessionId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        session.ClockIn(t0, sessionId);

        Assert.Equal(sessionId, session.CurrentSessionId);
    }

    [Fact]
    public void ClockIn_WithoutSessionId_StillGeneratesOne()
    {
        var session = new PresenceSession();
        var t0 = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        session.ClockIn(t0);

        Assert.NotEqual(Guid.Empty, session.CurrentSessionId);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~PresenceSessionTests" -c Release`
Expected: FAIL to compile — no `ClockIn(DateTimeOffset, Guid)` overload exists.

- [ ] **Step 3: Add the overload**

In `PresenceSession.cs`, replace the existing `ClockIn` method:

```csharp
    public void ClockIn(DateTimeOffset at) => ClockIn(at, Guid.NewGuid());

    /// <summary>Overload used by the verified check-in flow (Plan 2) — sessionId is the
    /// AttendanceSessionId the Service generated before capture started, so PresenceSession's
    /// id lines up with the EmployeeCheckIn row and the EmployeeWorkSession upsert key.</summary>
    public void ClockIn(DateTimeOffset at, Guid sessionId)
    {
        lock (_lock)
        {
            _clockInAt = at;
            _clockOutAt = null;
            _isOnBreak = false;
            _currentBreakStartedAt = null;
            _accumulatedBreak = TimeSpan.Zero;
            _breakSessionCount = 0;
            _sessionId = sessionId;
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~PresenceSessionTests" -c Release`
Expected: PASS (5/5 — 3 existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.Service/Lifecycle/PresenceSession.cs tests/ONEVO.Agent.Service.Tests/Lifecycle/PresenceSessionTests.cs
git commit -m "feat: add PresenceSession.ClockIn overload accepting an external session id"
```

---

## Task 9: Shared IPC contracts for check-in

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`

- [ ] **Step 1: Add message type constants**

Add below the existing `BiometricEnrollment*` constants:

```csharp
    /// <summary>Tray → Service: employee pressed CLOCK IN and a fresh location fix is available (or capture failed).</summary>
    public const string CheckInStart = "CheckInStart";

    /// <summary>Service → Tray: AWS session + short-lived scoped credentials for the check-in WebView2 capture.</summary>
    public const string CheckInSessionReady = "CheckInSessionReady";

    /// <summary>Tray → Service: the check-in WebView2 capture finished (or failed).</summary>
    public const string CheckInCaptureFinished = "CheckInCaptureFinished";

    /// <summary>Service → Tray: final check-in verdict — on Success, monitoring has already gone Active.</summary>
    public const string CheckInResult = "CheckInResult";
```

- [ ] **Step 2: Add payload records**

Add below the existing `BiometricEnrollment*` payload records:

```csharp
/// <summary>Location captured by the Tray via ILocationService immediately before Clock In. Null
/// Latitude/Longitude means the Tray could not get a fix (denied/unsupported/timed out) —
/// strict-online policy blocks Clock In in that case; the Service never fabricates a location.</summary>
public sealed record CheckInStartPayload(
    double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset CapturedAt);

public sealed record CheckInSessionReadyPayload(
    bool Success,
    string? ErrorCode,
    Guid AttemptId,
    Guid AttendanceSessionId,
    string? AwsSessionId,
    string? Region,
    string? ChallengeType,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? SessionToken,
    DateTimeOffset? CredentialsExpireAt);

/// <summary>Mirrors BiometricEnrollmentCaptureFinishedPayload's trust boundary — CaptureSucceeded
/// is local UX signal only; the backend re-derives the verdict from AWS regardless. AttendanceSessionId
/// is carried explicitly (rather than cached as Service instance state between the Start and
/// CaptureFinished calls) because AgentWorker is a singleton handling a reconnecting client — an
/// instance-field cache would go stale on an abandoned start (crash/close/timeout) and could be
/// consumed by an unrelated later attempt. The backend still re-validates this id against the
/// attempt row, so nothing new is trusted from the Tray by carrying it this way.</summary>
public sealed record CheckInCaptureFinishedPayload(Guid AttemptId, Guid AttendanceSessionId, bool CaptureSucceeded, string? ClientErrorCode);

public sealed record CheckInResultPayload(bool Success, string? ErrorCode, MonitoringState State, SessionSnapshot? Session);
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ONEVO.Agent.Shared -c Release`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs
git commit -m "feat: add shared IPC contracts for the check-in liveness flow"
```

---

## Task 10: `AgentApiRoutes` + `OnevoApiClient` check-in methods

**Files:**
- Modify: `ONEVO.Agent.Service/Api/AgentApiRoutes.cs`
- Modify: `ONEVO.Agent.Service/Api/OnevoApiClient.cs`

- [ ] **Step 1: Add routes**

```csharp
    public const string BiometricEnrollmentAttemptCreate   = "/api/v1/monitoring/biometrics/enrollment-attempts";
    public const string BiometricEnrollmentAttemptComplete = "/api/v1/monitoring/biometrics/enrollment-attempts/{0}/complete";

    public const string CheckInAttemptCreate   = "/api/v1/monitoring/biometrics/check-in-attempts";
    public const string CheckInAttemptComplete = "/api/v1/monitoring/biometrics/check-in-attempts/{0}/complete";
```

- [ ] **Step 2: Add wire records + client methods**

Add the wire records near the existing `EnrollmentAttemptPayload`/`BiometricProfilePayload` records at the bottom of `OnevoApiClient.cs`:

```csharp
/// <summary>Wire-format mirror of the backend's CheckInAttemptResponseDto.</summary>
public sealed record CheckInAttemptPayload(
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("aws_session_id")] string AwsSessionId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("challenge_type")] string ChallengeType,
    [property: JsonPropertyName("access_key_id")] string AccessKeyId,
    [property: JsonPropertyName("secret_access_key")] string SecretAccessKey,
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("credentials_expire_at")] DateTimeOffset CredentialsExpireAt);

public sealed record CreateCheckInAttemptResult(bool Success, string? ErrorCode, CheckInAttemptPayload? Attempt);

/// <summary>Wire-format mirror of the backend's CheckInVerificationResultDto.</summary>
public sealed record CheckInVerificationResultPayload(
    [property: JsonPropertyName("check_in_id")] Guid CheckInId,
    [property: JsonPropertyName("attendance_session_id")] Guid AttendanceSessionId,
    [property: JsonPropertyName("verification_status")] string VerificationStatus,
    [property: JsonPropertyName("checked_in_at")] DateTimeOffset CheckedInAt);

public sealed record CompleteCheckInAttemptResult(bool Success, string? ErrorCode, CheckInVerificationResultPayload? Result);
```

Add the two client methods right below `CompleteEnrollmentAttemptAsync`:

```csharp
    /// <summary>Creates a new check-in attempt. Auth: Bearer Device JWT.</summary>
    public async Task<CreateCheckInAttemptResult> CreateCheckInAttemptAsync(
        string accessToken, Guid attendanceSessionId, double latitude, double longitude,
        double? locationAccuracy, DateTimeOffset locationCapturedAt, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, AgentApiRoutes.CheckInAttemptCreate)
        {
            Content = JsonContent.Create(new CheckInAttemptRequestBody(
                attendanceSessionId, latitude, longitude, locationAccuracy, locationCapturedAt))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi call to {Route} failed", AgentApiRoutes.CheckInAttemptCreate);
            return new CreateCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new CreateCheckInAttemptResult(
                false,
                response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "UNAUTHORIZED",
                    HttpStatusCode.UnprocessableEntity => "UNPROCESSABLE",
                    _ => "SERVICE_UNAVAILABLE"
                },
                null);
        }

        CheckInAttemptPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CheckInAttemptPayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", AgentApiRoutes.CheckInAttemptCreate);
            return new CreateCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        return payload is null
            ? new CreateCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null)
            : new CreateCheckInAttemptResult(true, null, payload);
    }

    /// <summary>Completes a check-in attempt. Auth: Bearer Device JWT.</summary>
    public async Task<CompleteCheckInAttemptResult> CompleteCheckInAttemptAsync(
        string accessToken, Guid attemptId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OnevoApi");
        var route = string.Format(AgentApiRoutes.CheckInAttemptComplete, attemptId);
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
            return new CompleteCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new CompleteCheckInAttemptResult(
                false,
                response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "UNAUTHORIZED",
                    HttpStatusCode.UnprocessableEntity => "UNPROCESSABLE",
                    _ => "SERVICE_UNAVAILABLE"
                },
                null);
        }

        CheckInVerificationResultPayload? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CheckInVerificationResultPayload>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnevoApi response from {Route} could not be parsed", route);
            return new CompleteCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null);
        }

        return payload is null
            ? new CompleteCheckInAttemptResult(false, "SERVICE_UNAVAILABLE", null)
            : new CompleteCheckInAttemptResult(true, null, payload);
    }

    private sealed record CheckInAttemptRequestBody(
        [property: JsonPropertyName("attendance_session_id")] Guid AttendanceSessionId,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude,
        [property: JsonPropertyName("location_accuracy")] double? LocationAccuracy,
        [property: JsonPropertyName("location_captured_at")] DateTimeOffset LocationCapturedAt);
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build ONEVO.Agent.Service -c Release`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Api/AgentApiRoutes.cs ONEVO.Agent.Service/Api/OnevoApiClient.cs
git commit -m "feat: add check-in-attempt HTTP methods to OnevoApiClient"
```

---

## Task 11: `CheckInCoordinator`

Mirrors `EnrollmentCoordinator`'s shape (Device JWT never leaves the Service). The Tray captures location itself (via its own `ILocationService`) and hands it up in `StartAsync` — the coordinator does not touch GPS.

**Files:**
- Create: `ONEVO.Agent.Service/Biometrics/CheckInCoordinator.cs`
- Test: `tests/ONEVO.Agent.Service.Tests/Biometrics/CheckInCoordinatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Mirror `EnrollmentCoordinatorTests.cs`'s structure exactly (same fake `OnevoApiClient` seam — check that file for how it fakes the HTTP layer, likely via a test `HttpMessageHandler` or an injected interface; replicate that exact mechanism here, do not invent a new one). Cover:

```csharp
using ONEVO.Agent.Service.Biometrics;
using Xunit;

namespace ONEVO.Agent.Service.Tests.Biometrics;

[Collection(CredentialStoreFileCollection.Name)]
public class CheckInCoordinatorTests
{
    [Fact]
    public async Task StartAsync_NoDeviceCredential_ReturnsNoDeviceCredentialError()
    {
        // Arrange: CredentialStore with no stored device JWT (mirror EnrollmentCoordinatorTests'
        // equivalent test for the exact CredentialStore setup/teardown pattern).
        // Act: coordinator.StartAsync(new GeoLocationInput(12.9, 77.5, 15.0, DateTimeOffset.UtcNow), CancellationToken.None)
        // Assert: result.Success is false, result.ErrorCode == "NO_DEVICE_CREDENTIAL"
    }

    [Fact]
    public async Task StartAsync_NoLocation_ReturnsNoLocationErrorWithoutCallingBackend()
    {
        // Arrange: valid device JWT stored, but location input has null Latitude/Longitude
        // (Tray's ILocationService.GetCurrentAsync returned null — permission denied/unsupported).
        // Act: coordinator.StartAsync(new GeoLocationInput(null, null, null, DateTimeOffset.UtcNow), ct)
        // Assert: result.Success is false, result.ErrorCode == "NO_LOCATION" — and the fake
        // OnevoApiClient's CreateCheckInAttemptAsync was never invoked (strict policy: don't even
        // ask the backend without a location fix).
    }

    [Fact]
    public async Task StartAsync_ValidCredentialAndLocation_CallsBackendAndReturnsSession()
    {
        // Arrange: valid device JWT + real location; fake CreateCheckInAttemptAsync returns success.
        // Assert: result.Success, result.AttendanceSessionId is a non-empty Guid the coordinator
        // itself generated (Guid.NewGuid()) and passed through to the backend call.
    }

    [Fact]
    public async Task CompleteAsync_BackendReturnsVerified_ReturnsSuccessWithVerifiedStatus()
    {
        // Assert: result.Success, result.VerificationStatus == "verified", result.AttendanceSessionId
        // matches what StartAsync generated.
    }

    [Fact]
    public async Task CompleteAsync_BackendReturnsUnprocessable_ReturnsFailureNotVerified()
    {
        // Assert: result.Success is false — a Rejected/mismatch verdict must never be treated as success.
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~CheckInCoordinatorTests" -c Release`
Expected: FAIL to compile — `CheckInCoordinator` doesn't exist yet.

- [ ] **Step 3: Implement the coordinator**

```csharp
namespace ONEVO.Agent.Service.Biometrics;

using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Security;

/// <summary>Service-local mirror of the backend's CheckInVerificationStatus constants (that type
/// lives in the backend repo's Domain layer and isn't shared across repos) — only the one value
/// AgentWorker needs to branch on for activation.</summary>
public static class CheckInVerificationStatus
{
    public const string Verified = "verified";
}

/// <summary>Location the Tray captured via its own ILocationService immediately before Clock In.
/// Null Latitude/Longitude means capture failed — the coordinator blocks before calling the
/// backend at all, matching strict-online policy (no location, no check-in attempt).</summary>
public sealed record GeoLocationInput(double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset CapturedAt);

public sealed record CheckInSessionResult(
    bool Success, string? ErrorCode, Guid AttemptId, Guid AttendanceSessionId, string? AwsSessionId,
    string? Region, string? ChallengeType, string? AccessKeyId, string? SecretAccessKey,
    string? SessionToken, DateTimeOffset? CredentialsExpireAt);

public sealed record CheckInCompletionResult(
    bool Success, string? ErrorCode, Guid AttendanceSessionId, string? VerificationStatus);

/// <summary>
/// Orchestrates the check-in subset of the biometric flow (Plan 2). Generates the
/// AttendanceSessionId up front — before any backend call — so it is available to hand to
/// PresenceSession.ClockIn(now, sessionId) the moment a Verified verdict comes back, without a
/// second round trip. Same Device-JWT-never-leaves-the-Service boundary as EnrollmentCoordinator.
/// </summary>
public sealed class CheckInCoordinator
{
    private readonly ILogger<CheckInCoordinator> _logger;
    private readonly OnevoApiClient _apiClient;
    private readonly CredentialStore _credentials;

    public CheckInCoordinator(
        ILogger<CheckInCoordinator> logger, OnevoApiClient apiClient, CredentialStore credentials)
    {
        _logger = logger;
        _apiClient = apiClient;
        _credentials = credentials;
    }

    public async Task<CheckInSessionResult> StartAsync(GeoLocationInput location, CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return new CheckInSessionResult(false, "NO_DEVICE_CREDENTIAL",
                Guid.Empty, Guid.Empty, null, null, null, null, null, null, null);
        }

        if (location.Latitude is null || location.Longitude is null)
        {
            return new CheckInSessionResult(false, "NO_LOCATION",
                Guid.Empty, Guid.Empty, null, null, null, null, null, null, null);
        }

        var attendanceSessionId = Guid.NewGuid();

        var result = await _apiClient.CreateCheckInAttemptAsync(
            jwt, attendanceSessionId, location.Latitude.Value, location.Longitude.Value,
            location.AccuracyMeters, location.CapturedAt, ct);

        if (!result.Success || result.Attempt is null)
        {
            _logger.LogWarning("CreateCheckInAttempt failed: {ErrorCode}", result.ErrorCode);
            return new CheckInSessionResult(false, result.ErrorCode ?? "SERVICE_UNAVAILABLE",
                Guid.Empty, attendanceSessionId, null, null, null, null, null, null, null);
        }

        var attempt = result.Attempt;
        return new CheckInSessionResult(
            true, null, attempt.AttemptId, attendanceSessionId, attempt.AwsSessionId, attempt.Region,
            attempt.ChallengeType, attempt.AccessKeyId, attempt.SecretAccessKey, attempt.SessionToken,
            attempt.CredentialsExpireAt);
    }

    public async Task<CheckInCompletionResult> CompleteAsync(Guid attemptId, Guid attendanceSessionId, CancellationToken ct)
    {
        var jwt = _credentials.ReadDeviceJwt();
        if (string.IsNullOrWhiteSpace(jwt))
            return new CheckInCompletionResult(false, "NO_DEVICE_CREDENTIAL", attendanceSessionId, null);

        var result = await _apiClient.CompleteCheckInAttemptAsync(jwt, attemptId, ct);
        if (!result.Success || result.Result is null)
        {
            _logger.LogWarning("CompleteCheckInAttempt failed: {ErrorCode}", result.ErrorCode);
            return new CheckInCompletionResult(false, result.ErrorCode, attendanceSessionId, null);
        }

        return new CheckInCompletionResult(true, null, attendanceSessionId, result.Result.VerificationStatus);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests --filter "FullyQualifiedName~CheckInCoordinatorTests" -c Release`
Expected: PASS (5/5).

- [ ] **Step 5: Register in DI**

In `ONEVO.Agent.Service/Program.cs`, add next to the existing `services.AddSingleton<EnrollmentCoordinator>();`:

```csharp
services.AddSingleton<CheckInCoordinator>();
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.Service/Biometrics/CheckInCoordinator.cs \
        tests/ONEVO.Agent.Service.Tests/Biometrics/CheckInCoordinatorTests.cs \
        ONEVO.Agent.Service/Program.cs
git commit -m "feat: add CheckInCoordinator orchestrating the strict-online check-in flow"
```

---

## Task 12: `AgentWorker` wiring — activate monitoring only on a Verified verdict

This is the Service-side non-negotiable: `TryActivateClockIn` is factored out of `ExecuteClockIn` so the unverified dev/legacy path (generates its own session id) and the new verified-completion path (reuses the coordinator's `AttendanceSessionId`) share the exact same gate-checking and state-transition logic — no duplicated branching, no drift between the two paths.

**Files:**
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Test: new file `tests/ONEVO.Agent.Service.Tests/AgentWorkerCheckInTests.cs`

- [ ] **Step 1: Refactor `ExecuteClockIn` into a shared helper (no behavior change yet)**

Replace the existing `ExecuteClockIn` method with:

```csharp
    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) ExecuteClockIn(
        DateTimeOffset now)
        => TryActivateClockIn(now, Guid.NewGuid());

    /// <summary>Shared by the legacy/dev-bootstrap Clock In path (ExecuteClockIn, generates its own
    /// session id) and the verified check-in completion path (HandleCheckInCaptureFinishedAsync,
    /// reuses the CheckInCoordinator's AttendanceSessionId) — both must apply the identical gates
    /// and state transition so a verified check-in can never activate monitoring more permissively
    /// than a normal Clock In would.</summary>
    private (bool Success, string? ErrorCode, string? Message, MonitoringState State) TryActivateClockIn(
        DateTimeOffset now, Guid sessionId)
    {
        var current = _stateMachine.CurrentState;
        if (current == MonitoringState.Active)
            return (false, "ALREADY_CLOCKED_IN", "You are already clocked in.", current);
        if (current == MonitoringState.Paused)
            return (false, "ON_BREAK", "End break or clock out first.", current);
        if (current == MonitoringState.Locked)
            return (false, "LOCKED", "Agent is locked. Re-enrollment required.", current);
        if (current == MonitoringState.Unenrolled)
            return (false, "UNENROLLED", "Device is not enrolled.", current);

        // Presence session must be active before CanActivate is true.
        _lifecycleGate.SetPresenceSessionActive(true);
        _lifecycleGate.SetNotOnBreak(true);

        if (!_options.AllowLocalLifecycleWithoutFullGates && !_lifecycleGate.CanActivate)
        {
            _lifecycleGate.SetPresenceSessionActive(false);
            return (false, "GATES_CLOSED", "Monitoring gates are not satisfied.", current);
        }

        if (!_stateMachine.TryTransition(MonitoringState.Active, out _))
            return (false, "INVALID_STATE", $"Cannot clock in from {current}.", current);

        _presenceSession.ClockIn(now, sessionId);
        return (true, null, "Clocked in successfully.", MonitoringState.Active);
    }
```

- [ ] **Step 2: Run the existing test suite to confirm zero regression from the refactor alone**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests -c Release`
Expected: PASS, same count as before this task (the refactor changes no observable behavior — `ExecuteClockIn(now)` still does exactly what it did, just via the shared helper).

- [ ] **Step 3: Add the field, constructor param, and switch cases**

In `AgentWorker.cs`, add the field and constructor parameter next to `_enrollmentCoordinator`:

```csharp
    private readonly EnrollmentCoordinator _enrollmentCoordinator;
    private readonly CheckInCoordinator _checkInCoordinator;
```

```csharp
    public AgentWorker(
        // ... existing params ...
        EnrollmentCoordinator enrollmentCoordinator,
        CheckInCoordinator checkInCoordinator)
    {
        // ... existing assignments ...
        _enrollmentCoordinator = enrollmentCoordinator;
        _checkInCoordinator = checkInCoordinator;
    }
```

Add the two new switch cases in `HandleMessageAsync`:

```csharp
            case IpcMessageTypes.CheckInStart:
                await HandleCheckInStartAsync(envelope, reply);
                break;

            case IpcMessageTypes.CheckInCaptureFinished:
                await HandleCheckInCaptureFinishedAsync(envelope, reply);
                break;
```

- [ ] **Step 4: Add the two handler methods**

No instance-field caching between the two IPC calls — `AttendanceSessionId` round-trips through `CheckInSessionReadyPayload` → Tray → `CheckInCaptureFinishedPayload` (Task 9/13/14 already carry it), so `AgentWorker` (a singleton serving a reconnecting client) never holds session-scoped state that an abandoned start (crash/close/timeout) could leave stale for a later, unrelated attempt:

```csharp
    private async Task HandleCheckInStartAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<CheckInStartPayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CheckInSessionReady,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(new CheckInSessionReadyPayload(
                    false, "INVALID_PAYLOAD", Guid.Empty, Guid.Empty, null, null, null, null, null, null, null))
            });
            return;
        }

        var location = new GeoLocationInput(payload.Latitude, payload.Longitude, payload.AccuracyMeters, payload.CapturedAt);
        var result = await _checkInCoordinator.StartAsync(location, CancellationToken.None);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.CheckInSessionReady,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(new CheckInSessionReadyPayload(
                result.Success, result.ErrorCode, result.AttemptId, result.AttendanceSessionId,
                result.AwsSessionId, result.Region, result.ChallengeType, result.AccessKeyId,
                result.SecretAccessKey, result.SessionToken, result.CredentialsExpireAt))
        });
    }

    private async Task HandleCheckInCaptureFinishedAsync(IpcEnvelope envelope, Func<IpcEnvelope, Task> reply)
    {
        var payload = envelope.Payload?.Deserialize<CheckInCaptureFinishedPayload>();
        if (payload is null)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CheckInResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CheckInResultPayload(false, "INVALID_PAYLOAD", _stateMachine.CurrentState, null))
            });
            return;
        }

        // The backend re-derives the verdict from AWS + CompareFaces regardless of CaptureSucceeded
        // — the Tray's local capture outcome is only used for logging/UX, never trusted as the
        // security decision. Only a Verified verdict may activate monitoring. AttendanceSessionId
        // came from the Tray round-tripping what CheckInSessionReady gave it — the backend still
        // validates it against the attempt row in CompleteCheckInAttempt, so this is a correlation
        // token, not a trust boundary.
        var completion = await _checkInCoordinator.CompleteAsync(payload.AttemptId, payload.AttendanceSessionId, CancellationToken.None);

        if (!completion.Success || completion.VerificationStatus != CheckInVerificationStatus.Verified)
        {
            await reply(new IpcEnvelope
            {
                Type = IpcMessageTypes.CheckInResult,
                CorrelationId = envelope.CorrelationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CheckInResultPayload(false, completion.ErrorCode ?? "NOT_VERIFIED", _stateMachine.CurrentState, null))
            });
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var (success, errorCode, message, state) = TryActivateClockIn(now, payload.AttendanceSessionId);

        _logger.LogInformation(
            "Verified check-in activation Success={Success} Error={Error} State={State}",
            success, errorCode ?? "-", state);

        await reply(new IpcEnvelope
        {
            Type = IpcMessageTypes.CheckInResult,
            CorrelationId = envelope.CorrelationId,
            Payload = JsonSerializer.SerializeToElement(
                new CheckInResultPayload(success, errorCode, state, null))
        });

        await reply(BuildStatusEnvelope(correlationId: null));
    }
```

- [ ] **Step 5: Write the activation test**

```csharp
// AgentWorkerCheckInTests.cs
// Mirror whatever harness (real NamedPipeServer + in-proc client, or a direct HandleMessageAsync
// invocation via reflection/InternalsVisibleTo) is used for AgentWorker's existing lifecycle tests —
// check the codebase for how LifecycleCommand/ClockIn is already tested end-to-end, if at all; if no
// such harness exists yet, test at the CheckInCoordinator + TryActivateClockIn boundary instead:
// construct AgentWorker with a fake CheckInCoordinator (wrap it behind a thin interface if needed
// for testability) and assert that a Verified completion result leaves the AgentStateMachine in
// MonitoringState.Active with PresenceSession.CurrentSessionId == the AttendanceSessionId, and that
// a Rejected/failed completion leaves the state machine in whatever state it started in (never Active).
```

(Because `AgentWorker` currently has no existing unit test file and its collaborators are concrete classes wired through DI rather than interfaces, the executing engineer should first check whether `CheckInCoordinator` needs to be exposed behind an interface — e.g. `ICheckInCoordinator` — purely to make this test constructible. If so, that is a small, mechanical, zero-behavior-change addition: extract the interface, keep `CheckInCoordinator` as the only implementation, update the DI registration and the `AgentWorker` field/constructor type. Do this before writing the test, not as a hack inside it.)

- [ ] **Step 6: Run the full Service test suite**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests -c Release`
Expected: PASS, full suite, no regressions.

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.Service/AgentWorker.cs ONEVO.Agent.Service/Biometrics/CheckInCoordinator.cs \
        tests/ONEVO.Agent.Service.Tests/
git commit -m "feat: wire CheckInCoordinator into AgentWorker — monitoring activates only on a Verified verdict"
```

---

## Task 13: Tray IPC client — `StartCheckInAsync` / `CompleteCheckInAsync`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs` (its `RecordingPipeClient` also implements `INamedPipeClient` — Plan 1 hit this exact CS0535 build error; the interface addition breaks it the same way here)

- [ ] **Step 1: Add to the interface**

```csharp
    /// <summary>Reports fresh location (or a capture failure) and requests a new check-in liveness
    /// session. Waits for CheckInSessionReady (or timeout).</summary>
    Task<CheckInSessionReadyPayload?> StartCheckInAsync(
        double? latitude, double? longitude, double? accuracyMeters, DateTimeOffset capturedAt, CancellationToken ct);

    /// <summary>Reports the WebView2 capture outcome and waits for the final CheckInResult — on
    /// success, monitoring is already Active by the time this returns. attendanceSessionId is the
    /// value StartCheckInAsync's CheckInSessionReadyPayload returned — round-tripped rather than
    /// cached Service-side (see AgentWorker's HandleCheckInCaptureFinishedAsync for why).</summary>
    Task<CheckInResultPayload?> CompleteCheckInAsync(
        Guid attemptId, Guid attendanceSessionId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct);
```

- [ ] **Step 2: Implement in `NamedPipeClient.cs`**

Add directly below `CompleteBiometricEnrollmentAsync`, following its exact correlation/timeout pattern:

```csharp
    public async Task<CheckInSessionReadyPayload?> StartCheckInAsync(
        double? latitude, double? longitude, double? accuracyMeters, DateTimeOffset capturedAt, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.CheckInStart,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CheckInStartPayload(latitude, longitude, accuracyMeters, capturedAt))
            };
            await WriteEnvelopeAsync(envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            IpcEnvelope reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Check-in start timed out waiting for session");
                return null;
            }

            return reply.Payload?.Deserialize<CheckInSessionReadyPayload>();
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    public async Task<CheckInResultPayload?> CompleteCheckInAsync(
        Guid attemptId, Guid attendanceSessionId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        try
        {
            var envelope = new IpcEnvelope
            {
                Type = IpcMessageTypes.CheckInCaptureFinished,
                CorrelationId = correlationId,
                Payload = JsonSerializer.SerializeToElement(
                    new CheckInCaptureFinishedPayload(attemptId, attendanceSessionId, captureSucceeded, clientErrorCode))
            };
            await WriteEnvelopeAsync(envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            IpcEnvelope reply;
            try
            {
                reply = await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Check-in completion timed out waiting for result");
                return null;
            }

            return reply.Payload?.Deserialize<CheckInResultPayload>();
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }
```

Add the two reply types to the `_pending` completion allowlist in `ReadLoopAsync`:

```csharp
                    && envelope.Type is IpcMessageTypes.LifecycleResult
                        or IpcMessageTypes.StatusResponse
                        or IpcMessageTypes.CollectionRecordAck
                        or IpcMessageTypes.EnrollmentResult
                        or IpcMessageTypes.LogoutResult
                        or IpcMessageTypes.BiometricEnrollmentSessionReady
                        or IpcMessageTypes.BiometricEnrollmentResult
                        or IpcMessageTypes.CheckInSessionReady
                        or IpcMessageTypes.CheckInResult)
```

- [ ] **Step 3: Update `FakeNamedPipeClient`**

Add below `NextEnrollmentCompletionResult`/`CompleteBiometricEnrollmentAsync`:

```csharp
    /// <summary>Optional canned result for StartCheckInAsync. Null = auto-success.</summary>
    public CheckInSessionReadyPayload? NextCheckInSessionResult { get; set; }

    public Task<CheckInSessionReadyPayload?> StartCheckInAsync(
        double? latitude, double? longitude, double? accuracyMeters, DateTimeOffset capturedAt, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.CheckInStart,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new CheckInStartPayload(latitude, longitude, accuracyMeters, capturedAt))
        });

        if (NextCheckInSessionResult is not null)
            return Task.FromResult<CheckInSessionReadyPayload?>(NextCheckInSessionResult);

        return Task.FromResult<CheckInSessionReadyPayload?>(new CheckInSessionReadyPayload(
            true, null, Guid.NewGuid(), Guid.NewGuid(), "aws-session-fake", "ap-south-1",
            "FaceMovementAndLightChallenge", "AKIA", "secret", "token", DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    /// <summary>Optional canned result for CompleteCheckInAsync. Null = auto-success (Active).</summary>
    public CheckInResultPayload? NextCheckInResult { get; set; }

    public Task<CheckInResultPayload?> CompleteCheckInAsync(
        Guid attemptId, Guid attendanceSessionId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        SentEnvelopes.Add(new IpcEnvelope
        {
            Type = IpcMessageTypes.CheckInCaptureFinished,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new CheckInCaptureFinishedPayload(attemptId, attendanceSessionId, captureSucceeded, clientErrorCode))
        });

        if (NextCheckInResult is not null)
            return Task.FromResult<CheckInResultPayload?>(NextCheckInResult);

        return Task.FromResult<CheckInResultPayload?>(
            new CheckInResultPayload(true, null, MonitoringState.Active, null));
    }
```

- [ ] **Step 4: Build to surface the `RecordingPipeClient` CS0535, then fix it**

Run: `dotnet build tests/ONEVO.Agent.TrayApp.Tests -c Release`
Expected: FAIL — `CS0535: 'RecordingPipeClient' does not implement interface member 'INamedPipeClient.StartCheckInAsync'` (and `CompleteCheckInAsync`), in `InactivityScreenshotCollectorTests.cs` — same class Plan 1 hit for the biometric-enrollment methods.

Add both methods to that file's nested `RecordingPipeClient`, matching whatever trivial pattern it already uses for `StartBiometricEnrollmentAsync`/`CompleteBiometricEnrollmentAsync` (check the file — likely a canned-success return with no recording, since that class's purpose is recording *inactivity* evidence transfers, not biometric calls):

```csharp
    public Task<CheckInSessionReadyPayload?> StartCheckInAsync(
        double? latitude, double? longitude, double? accuracyMeters, DateTimeOffset capturedAt, CancellationToken ct)
        => Task.FromResult<CheckInSessionReadyPayload?>(null);

    public Task<CheckInResultPayload?> CompleteCheckInAsync(
        Guid attemptId, Guid attendanceSessionId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
        => Task.FromResult<CheckInResultPayload?>(null);
```

- [ ] **Step 5: Build again to verify it compiles**

Run: `dotnet build tests/ONEVO.Agent.TrayApp.Tests -c Release`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs \
        tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs \
        tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs
git commit -m "feat: add check-in IPC client methods to NamedPipeClient"
```

---

## Task 14: `CheckInBiometricViewModel` + Page + routing + DI

A **new** ViewModel/Page rather than retrofitting `BiometricEnrollmentViewModel`/`Page` with a purpose flag — the enrollment component is already shipped and tested from Plan 1, and the only behavior-changing commit in this whole plan should be as small and isolated as possible. `BiometricWebView`/`BiometricSessionConfig`/`BiometricCaptureOutcome` (Task 20 of Plan 1) are already purpose-agnostic and are reused as-is.

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/CheckInBiometricViewModel.cs`
- Create: `ONEVO.Agent.TrayApp/Views/CheckInBiometricPage.xaml` + `.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/AppShell.xaml`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/CheckInBiometricViewModelTests.cs`

- [ ] **Step 1: Write the failing ViewModel tests**

Mirror `BiometricEnrollmentViewModelTests.cs`'s 4 tests, adapted:

```csharp
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;
using Xunit;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class CheckInBiometricViewModelTests
{
    [Fact]
    public void SessionConfig_NullBeforeStart()
    {
        var vm = new CheckInBiometricViewModel(new FakeNamedPipeClient());
        Assert.Null(vm.SessionConfig);
    }

    [Fact]
    public async Task StartSessionCommand_OnSuccess_PopulatesSessionConfig()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new CheckInBiometricViewModel(pipe);

        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.SessionConfig);
        Assert.True(vm.IsSessionReady);
    }

    [Fact]
    public async Task StartSessionCommand_OnFailure_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextCheckInSessionResult = new CheckInSessionReadyPayload(
                false, "NO_LOCATION", Guid.Empty, Guid.Empty, null, null, null, null, null, null, null)
        };
        var vm = new CheckInBiometricViewModel(pipe);

        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.Equal("NO_LOCATION", vm.ErrorMessage);
        Assert.False(vm.IsSessionReady);
    }

    [Fact]
    public async Task ReportCaptureFinishedAsync_OnVerified_DoesNotSetErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextCheckInResult = new CheckInResultPayload(true, null, MonitoringState.Active, null)
        };
        var vm = new CheckInBiometricViewModel(pipe);
        vm.SetLocation(12.9716, 77.5946, 15.0, DateTimeOffset.UtcNow);
        await vm.StartSessionCommand.ExecuteAsync(null);

        await vm.ReportCaptureFinishedAsync(true, null, CancellationToken.None);

        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ReportCaptureFinishedAsync_OnRejected_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextCheckInResult = new CheckInResultPayload(false, "NOT_VERIFIED", MonitoringState.Stopped, null)
        };
        var vm = new CheckInBiometricViewModel(pipe);
        vm.SetLocation(12.9716, 77.5946, 15.0, DateTimeOffset.UtcNow);
        await vm.StartSessionCommand.ExecuteAsync(null);

        await vm.ReportCaptureFinishedAsync(true, null, CancellationToken.None);

        Assert.Equal("NOT_VERIFIED", vm.ErrorMessage);
    }

    [Fact]
    public async Task StartSessionCommand_NoLocationSet_ReturnsNoLocationErrorWithoutCallingPipe()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = new CheckInBiometricViewModel(pipe);
        // SetLocation deliberately not called — mirrors a page reached without query params.

        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.Equal("NO_LOCATION", vm.ErrorMessage);
        Assert.DoesNotContain(pipe.SentEnvelopes, e => e.Type == IpcMessageTypes.CheckInStart);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~CheckInBiometricViewModelTests" -c Release`
Expected: FAIL to compile — `CheckInBiometricViewModel` doesn't exist yet.

- [ ] **Step 3: Implement the ViewModel**

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class CheckInBiometricViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private bool _isSessionReady;

    [ObservableProperty] private bool _isCompleting;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Guid _attemptId;
    [ObservableProperty] private Guid _attendanceSessionId;

    private double? _latitude;
    private double? _longitude;
    private double? _accuracyMeters;
    private DateTimeOffset _capturedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _awsSessionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _region;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _challengeType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _accessKeyId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _secretAccessKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _sessionToken;

    public CheckInBiometricViewModel(INamedPipeClient pipe)
    {
        Title = "Verify to Clock In";
        _pipe = pipe;
    }

    public BiometricSessionConfig? SessionConfig =>
        IsSessionReady && AwsSessionId is not null && Region is not null && ChallengeType is not null
            && AccessKeyId is not null && SecretAccessKey is not null && SessionToken is not null
            ? new BiometricSessionConfig(AwsSessionId, Region, ChallengeType, AccessKeyId, SecretAccessKey, SessionToken)
            : null;

    /// <summary>Called by CheckInBiometricPage.ApplyQueryAttributes before this page appears —
    /// mirrors PhotoCaptureWindow's IQueryAttributable → PhotoCaptureWindowViewModel.SetContext
    /// pattern rather than a shared static, so two ViewModel instances (e.g. under parallel test
    /// execution) never observe each other's location. ClockInViewModel captures the fix and passes
    /// it as Shell route query parameters (see ClockInViewModel.ClockInAsync) — the same mechanism
    /// //photo?context=clockin already used for its one string parameter, extended to four values.</summary>
    public void SetLocation(double? latitude, double? longitude, double? accuracyMeters, DateTimeOffset capturedAt)
    {
        _latitude = latitude;
        _longitude = longitude;
        _accuracyMeters = accuracyMeters;
        _capturedAt = capturedAt;
    }

    [RelayCommand]
    private async Task StartSessionAsync(CancellationToken ct)
    {
        ErrorMessage = null;

        if (_latitude is null || _longitude is null)
        {
            ErrorMessage = "NO_LOCATION";
            IsSessionReady = false;
            return;
        }

        var result = await _pipe.StartCheckInAsync(_latitude, _longitude, _accuracyMeters, _capturedAt, ct);

        if (result is null || !result.Success)
        {
            ErrorMessage = result?.ErrorCode ?? "No response from OneXso Agent Service.";
            IsSessionReady = false;
            return;
        }

        AttemptId = result.AttemptId;
        AttendanceSessionId = result.AttendanceSessionId;
        AwsSessionId = result.AwsSessionId;
        Region = result.Region;
        ChallengeType = result.ChallengeType;
        AccessKeyId = result.AccessKeyId;
        SecretAccessKey = result.SecretAccessKey;
        SessionToken = result.SessionToken;
        IsSessionReady = true;
    }

    [RelayCommand]
    private Task CaptureFinished(BiometricCaptureOutcome outcome) =>
        ReportCaptureFinishedAsync(outcome.Succeeded, outcome.ErrorCode, CancellationToken.None);

    public async Task ReportCaptureFinishedAsync(bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        IsCompleting = true;
        try
        {
            var result = await _pipe.CompleteCheckInAsync(AttemptId, AttendanceSessionId, captureSucceeded, clientErrorCode, ct);

            if (result is null || !result.Success)
            {
                ErrorMessage = result?.ErrorCode ?? "Check-in could not be verified.";
                return;
            }

            try { await Shell.Current.GoToAsync("//active"); }
            catch { /* unit tests */ }
        }
        finally
        {
            IsCompleting = false;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~CheckInBiometricViewModelTests" -c Release`
Expected: PASS (6/6).

- [ ] **Step 5: Add the Page**

```xml
<!-- CheckInBiometricPage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             xmlns:controls="clr-namespace:ONEVO.Agent.TrayApp.Controls"
             x:Class="ONEVO.Agent.TrayApp.Views.CheckInBiometricPage"
             x:DataType="vm:CheckInBiometricViewModel"
             Title="{Binding Title}">

  <Grid RowDefinitions="Auto,*,Auto" Padding="20,16">
    <Grid.Background>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="{StaticResource BackgroundWashStart}" Offset="0" />
        <GradientStop Color="{StaticResource BackgroundWashEnd}" Offset="1" />
      </LinearGradientBrush>
    </Grid.Background>

    <controls:AppHeaderBar Grid.Row="0" ShowSubtitle="False" />

    <Grid Grid.Row="1" RowDefinitions="Auto,*">
      <Label Grid.Row="0"
             Text="{Binding ErrorMessage}"
             TextColor="#DC2626"
             FontSize="14"
             Margin="0,0,0,12"
             IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />

      <controls:BiometricWebView Grid.Row="1"
                                  SessionConfig="{Binding SessionConfig}"
                                  CaptureFinishedCommand="{Binding CaptureFinishedCommand}"
                                  IsVisible="{Binding IsSessionReady}" />
    </Grid>

    <ActivityIndicator Grid.Row="2"
                        IsRunning="{Binding IsCompleting}"
                        IsVisible="{Binding IsCompleting}"
                        Margin="0,12,0,0" />
  </Grid>
</ContentPage>
```

```csharp
// CheckInBiometricPage.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;

using System.Globalization;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class CheckInBiometricPage : ContentPage, IQueryAttributable
{
    private readonly CheckInBiometricViewModel _vm;

    public CheckInBiometricPage(CheckInBiometricViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    // Mirrors PhotoCaptureWindow.ApplyQueryAttributes — ClockInViewModel navigates here with
    // lat/lng/accuracy/capturedAt query params instead of a shared static (see Task 14/15 notes).
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        double? lat = query.TryGetValue("lat", out var latRaw)
            && double.TryParse(latRaw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal)
            ? latVal : null;
        double? lng = query.TryGetValue("lng", out var lngRaw)
            && double.TryParse(lngRaw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lngVal)
            ? lngVal : null;
        double? accuracy = query.TryGetValue("accuracy", out var accRaw)
            && double.TryParse(accRaw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var accVal)
            ? accVal : null;
        var capturedAt = query.TryGetValue("capturedAt", out var capRaw)
            && DateTimeOffset.TryParse(capRaw?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capVal)
            ? capVal : DateTimeOffset.UtcNow;

        _vm.SetLocation(lat, lng, accuracy, capturedAt);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.StartSessionCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 6: Route + DI**

In `AppShell.xaml`, add below the `enrollment-biometric` route:

```xml
  <ShellContent Route="checkin-biometric" ContentTemplate="{DataTemplate views:CheckInBiometricPage}" />
```

In `MauiProgram.cs`, add next to the existing `BiometricEnrollmentViewModel`/`BiometricEnrollmentPage` registrations:

```csharp
        builder.Services.AddTransient<CheckInBiometricViewModel>();
        // ...
        builder.Services.AddTransient<CheckInBiometricPage>();
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build ONEVO.Agent.TrayApp -c Release -f net9.0-windows10.0.19041.0`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/CheckInBiometricViewModel.cs \
        ONEVO.Agent.TrayApp/Views/CheckInBiometricPage.xaml ONEVO.Agent.TrayApp/Views/CheckInBiometricPage.xaml.cs \
        ONEVO.Agent.TrayApp/Views/AppShell.xaml ONEVO.Agent.TrayApp/MauiProgram.cs \
        tests/ONEVO.Agent.TrayApp.Tests/ViewModels/CheckInBiometricViewModelTests.cs
git commit -m "feat: add CheckInBiometricPage — WebView2 capture surface for verified check-in"
```

---

## Task 15: `ClockInViewModel` wiring — the one behavior-changing commit

Everything up to here is additive and inert. This task flips the switch: when `CameraVerificationEnabled` is true, Clock In captures a fresh GPS fix and routes to the new verified flow instead of the old unverified `//photo?context=clockin` path. When the flag is false (every tenant today, including local dev — confirmed by `GetEffectiveTrayPolicyQueryHandler` sourcing it from `MonitoringCapability.IdentityVerification`, which defaults `false` with no admin write path yet), behavior is byte-for-byte unchanged.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`

- [ ] **Step 1: Write the failing test for the new branch**

Add to `ClockInViewModelTests.cs`:

```csharp
    [Fact]
    public async Task ClockInCommand_CameraVerificationEnabled_WithLocation_DoesNotUseLegacyLifecyclePath()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", CameraVerificationEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        var location = new FixedLocationService(new GeoPoint(12.9716, 77.5946, 15.0));
        var vm = new ClockInViewModel(pipe, location);

        await vm.ClockInCommand.ExecuteAsync(null);

        // No lifecycle command sent directly and no error — the verified page (reached via a
        // Shell.GoToAsync query-string route, not asserted here since Shell isn't available in
        // unit tests, same boundary as every other navigation call in this ViewModel) owns
        // completing the check-in from here.
        Assert.DoesNotContain(LifecycleAction.ClockIn, pipe.LifecycleActions);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ClockInCommand_CameraVerificationEnabled_NoLocation_SetsErrorMessageAndDoesNotNavigate()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", CameraVerificationEnabled = true, ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        var location = new FixedLocationService(null);
        var vm = new ClockInViewModel(pipe, location);

        await vm.ClockInCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.DoesNotContain(LifecycleAction.ClockIn, pipe.LifecycleActions);
    }

    [Fact]
    public async Task ClockInCommand_CameraVerificationDisabled_StillUsesLegacyLifecyclePath()
    {
        var pipe = new FakeNamedPipeClient();
        var location = new FixedLocationService(new GeoPoint(12.9716, 77.5946, 15.0));
        var vm = new ClockInViewModel(pipe, location);

        await vm.ClockInCommand.ExecuteAsync(null);

        Assert.Contains(LifecycleAction.ClockIn, pipe.LifecycleActions);
    }

    private sealed class FixedLocationService(GeoPoint? point) : ILocationService
    {
        public Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(point);
    }
```

(Add `using ONEVO.Agent.TrayApp.Services;` to the test file's usings if not already present — `WorkLocationViewModelTests.cs` already has a private `FixedLocationService` of the same shape; this is a separate copy scoped to this test class, matching that established per-test-class-fake pattern rather than sharing one across files.)

Also update the two pre-existing tests that call `new ClockInViewModel(pipe)` — the constructor now takes a second required parameter:

```csharp
    private static ClockInViewModel Make(FakeNamedPipeClient? pipe = null) =>
        new(pipe ?? new FakeNamedPipeClient(), new FixedLocationService(new GeoPoint(12.9716, 77.5946, 15.0)));
```

And the two inline `new ClockInViewModel(pipe)` calls in `ClockInCommand_SendsLifecycleClockIn` / `ClockInCommand_OnFailure_SetsErrorMessage` become `new ClockInViewModel(pipe, new FixedLocationService(new GeoPoint(12.9716, 77.5946, 15.0)))` (those two tests exercise the non-verified path already — `LastKnownPolicy` is null by default on `FakeNamedPipeClient`, so `CameraVerificationEnabled` is falsy and behavior is unchanged; they only need the constructor call fixed to compile).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~ClockInViewModelTests" -c Release`
Expected: FAIL to compile — `ClockInViewModel` has no two-arg constructor yet.

- [ ] **Step 3: Update `ClockInViewModel`**

Add the field and constructor parameter:

```csharp
    private readonly INamedPipeClient _pipe;
    private readonly ILocationService _location;
    private readonly System.Timers.Timer _clockTimer;
```

```csharp
    public ClockInViewModel(INamedPipeClient pipe, ILocationService location)
    {
        Title    = "Ready to Start Work";
        _pipe    = pipe;
        _location = location;
        Greeting = GetGreeting();
        // ... rest of constructor body unchanged ...
```

Replace the `CameraVerificationEnabled` branch inside `ClockInAsync`:

```csharp
    [RelayCommand]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            if (_currentPolicy?.CameraVerificationEnabled == true)
            {
                var point = await _location.GetCurrentAsync(ct);
                if (point is null)
                {
                    ErrorMessage = "Location is required to clock in. Enable location access and try again.";
                    return;
                }

                // Query params, not shared static state — mirrors //photo?context=clockin's
                // existing IQueryAttributable pattern (see CheckInBiometricPage.ApplyQueryAttributes).
                var capturedAt = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                var accuracy = point.AccuracyMeters?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                var route = $"//checkin-biometric?lat={point.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                            $"&lng={point.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                            $"&accuracy={accuracy}&capturedAt={capturedAt}";

                try { await Shell.Current.GoToAsync(route); }
                catch { /* unit tests */ }
                return;
            }

            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, ct);
            if (result is null)
            {
                ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.Message
                    ?? result.ErrorCode
                    ?? "Clock-in failed.";
                return;
            }

            try
            {
                await Shell.Current.GoToAsync("//active");
            }
            catch
            {
                // Shell may not be ready in unit tests.
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsClockinIn = false;
        }
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests --filter "FullyQualifiedName~ClockInViewModelTests" -c Release`
Expected: PASS, full file (existing 6 tests + 3 new).

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs
git commit -m "feat: route verified-tenant Clock In through fresh GPS capture + CheckInBiometricPage"
```

---

## Task 16: Retire the legacy `clockin` branch in `PhotoCaptureWindowViewModel`

Once Task 15 lands, nothing navigates to `//photo?context=clockin` anymore for any tenant with `CameraVerificationEnabled == true`. The branch itself — which fires `SendLifecycleAsync(LifecycleAction.ClockIn)` directly from a raw, unverified camera snapshot — must be removed outright rather than left dead, so it can never become a second, unverified Clock In path if something else were ever routed there by mistake.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`

(No test changes needed — confirmed by inspection that `PhotoCaptureWindowViewModelTests.cs` has zero tests exercising `SetContext("clockin")` or the `_captureContext == "clockin"` branch; this removal is test-neutral.)

- [ ] **Step 1: Remove the branch**

In `Continue()`, delete:

```csharp
        if (_captureContext == "clockin")
        {
            CaptureStatusText = "Completing clock-in...";
            var result = await _pipe.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);
            if (result is null || !result.Success)
            {
                CaptureStatusText = result?.Message ?? result?.ErrorCode ?? "Clock-in failed. Please try again.";
                IsCaptured = false;
                _capturedBytes = null;
                return;
            }

            try { await Shell.Current.GoToAsync("//active"); }
            catch { /* unit tests */ }
            return;
        }

        try { Preferences.Set("onevo.face_verified", true); }
```

Replacing it with just:

```csharp
        try { Preferences.Set("onevo.face_verified", true); }
```

`SetContext(string? context)` and the `_captureContext` field can stay (still used by the onboarding review flow's own context value, if any — check the file: if `_captureContext` is now write-only/unused anywhere else, remove the field and the `context` parameter from `SetContext` too, and update its one caller in `PhotoCaptureWindow.xaml.cs` accordingly. Confirm via `grep -n "_captureContext\|SetContext"` across the Views folder before deciding — do not remove a parameter still read elsewhere.)

- [ ] **Step 2: Run the full Tray test suite**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests -c Release`
Expected: PASS, full suite, no regressions.

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs
git commit -m "refactor: retire unverified clockin branch in PhotoCaptureWindowViewModel — superseded by CheckInBiometricPage"
```

---

## Task 17: Full regression + wrap-up

**Files:** none (verification only)

- [ ] **Step 1: Full backend test suite**

Run: `dotnet test HRMS-Backend-v1.sln -c Release` (or the unit + integration projects separately, as Plan 1 did, if the solution-level run is impractical in this environment)
Expected: PASS, all projects, including every Plan 1 test that was passing before this plan started.

- [ ] **Step 2: Full Tray/Service test suite**

Run: `dotnet test tray_app_maui.sln -c Release` (or per-project)
Expected: PASS, all projects.

- [ ] **Step 3: Confirm the dev bootstrap path is untouched**

Manually trace (no code change expected — this is a read-through check, not a step that edits anything): with `AllowLocalLifecycleWithoutFullGates=true` and no tenant having `CameraVerificationEnabled` set, `ClockInViewModel.ClockInAsync` must still take the `_pipe.SendLifecycleAsync(LifecycleAction.ClockIn, ct)` branch exactly as before this plan. If any test added in Task 15 exercises this path, its pass is sufficient confirmation; otherwise manually verify by reading the merged diff.

- [ ] **Step 4: Final status check**

```bash
git log --oneline -20
git status
```

Confirm every task's commit landed on the working branch and the tree is clean.
