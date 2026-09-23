# Face Match Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn biometric enrollment from "prove this is a live human" into "prove this is a live human AND store their face", and turn check-in face-scan upload from "store a photo" into "verify the photo matches the enrolled employee", using AWS Rekognition `CompareFaces`.

**Architecture:** Add a new `IFaceMatchService` (Infrastructure wrapper around Rekognition `CompareFaces`, same shape as the existing `IFaceLivenessService`). Extend enrollment completion to persist a reference photo (already captured by the liveness check, currently discarded) via the existing `IFileStorageService`. Extend check-in face-scan upload to fetch that reference photo and the just-uploaded capture, run them through `IFaceMatchService`, and persist a real match status + similarity score instead of the current hardcoded `Available`.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR (CQRS), EF Core + PostgreSQL, AWSSDK.Rekognition, xUnit + Moq + FluentAssertions.

**Working directory for all tasks:** `C:\HR\HRMS-Backend-v1\.worktrees\face-match-verification` (git worktree, branch `feature/face-match-verification`, based on `origin/development`). Baseline verified clean: `dotnet build src/ONEVO.Api` succeeds, `dotnet test tests/ONEVO.Tests.Unit` passes 3721/3721.

---

## Task 1: Config — `FaceMatchSimilarityThreshold`

**Files:**
- Modify: `src/ONEVO.Infrastructure/Configuration/AwsRekognitionOptions.cs`

- [ ] **Step 1: Add the option**

In `AwsRekognitionOptions.cs`, add this property inside the class, after `LivenessConfidenceThreshold`:

```csharp
    /// <summary>Minimum CompareFaces similarity (0-100) to accept a check-in face match.</summary>
    [Range(0, 100)] public float FaceMatchSimilarityThreshold { get; set; } = 80f;
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Configuration/AwsRekognitionOptions.cs
git commit -m "config: add FaceMatchSimilarityThreshold to AwsRekognitionOptions"
```

---

## Task 2: `IFaceMatchService` (AWS Rekognition CompareFaces wrapper)

**Files:**
- Create: `src/ONEVO.Application/Common/ServiceInterfaces/IFaceMatchService.cs`
- Create: `src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceMatchService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/RekognitionFaceMatchServiceTests.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace ONEVO.Application.Common.ServiceInterfaces;

public record FaceMatchOutcome(bool IsMatch, float Similarity);

public interface IFaceMatchService
{
    /// <summary>
    /// Compares a reference face photo against a newly captured photo.
    /// Both streams must be readable from position 0; neither is disposed by this call.
    /// </summary>
    Task<FaceMatchOutcome> CompareAsync(Stream referenceImage, Stream capturedImage, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/RekognitionFaceMatchServiceTests.cs`:

```csharp
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.Services.Monitoring.Biometrics;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Biometrics;

public class RekognitionFaceMatchServiceTests
{
    private readonly Mock<IAmazonRekognition> _rekognition = new();
    private readonly AwsRekognitionOptions _options = new()
    {
        Region = "us-east-1",
        LivenessRoleArn = "arn:aws:iam::123456789012:role/liveness",
        FaceMatchSimilarityThreshold = 80f
    };

    private RekognitionFaceMatchService CreateSut() => new(_rekognition.Object, Options.Create(_options));

    private static MemoryStream Bytes(params byte[] b) => new(b.Length == 0 ? new byte[] { 1, 2, 3 } : b);

    [Fact]
    public async Task MatchAboveThreshold_ReturnsIsMatchTrue_WithHighestSimilarity()
    {
        _rekognition.Setup(r => r.CompareFacesAsync(
                It.Is<CompareFacesRequest>(req => req.SimilarityThreshold == 80f),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompareFacesResponse
            {
                FaceMatches = new List<CompareFacesMatch>
                {
                    new() { Similarity = 91.2f },
                    new() { Similarity = 96.8f }
                }
            });

        var result = await CreateSut().CompareAsync(Bytes(), Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeTrue();
        result.Similarity.Should().Be(96.8f);
    }

    [Fact]
    public async Task NoFaceMatches_ReturnsIsMatchFalse_WithZeroSimilarity()
    {
        _rekognition.Setup(r => r.CompareFacesAsync(It.IsAny<CompareFacesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompareFacesResponse { FaceMatches = new List<CompareFacesMatch>() });

        var result = await CreateSut().CompareAsync(Bytes(), Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeFalse();
        result.Similarity.Should().Be(0f);
    }

    [Fact]
    public async Task NonMemoryStreamInput_IsBufferedAndComparedCorrectly()
    {
        _rekognition.Setup(r => r.CompareFacesAsync(It.IsAny<CompareFacesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompareFacesResponse
            {
                FaceMatches = new List<CompareFacesMatch> { new() { Similarity = 85f } }
            });

        using var reference = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        reference.Write(new byte[] { 9, 9, 9 });
        reference.Position = 0;

        var result = await CreateSut().CompareAsync(reference, Bytes(), CancellationToken.None);

        result.IsMatch.Should().BeTrue();
        result.Similarity.Should().Be(85f);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter RekognitionFaceMatchServiceTests`
Expected: FAIL — `RekognitionFaceMatchService` does not exist yet (compile error).

- [ ] **Step 4: Write the implementation**

Create `src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceMatchService.cs`:

```csharp
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.Monitoring.Biometrics;

public class RekognitionFaceMatchService : IFaceMatchService
{
    private readonly IAmazonRekognition _rekognition;
    private readonly AwsRekognitionOptions _options;

    public RekognitionFaceMatchService(IAmazonRekognition rekognition, IOptions<AwsRekognitionOptions> options)
    {
        _rekognition = rekognition;
        _options = options.Value;
    }

    public async Task<FaceMatchOutcome> CompareAsync(Stream referenceImage, Stream capturedImage, CancellationToken ct)
    {
        var sourceBytes = await ToMemoryStreamAsync(referenceImage, ct);
        var targetBytes = await ToMemoryStreamAsync(capturedImage, ct);

        var response = await _rekognition.CompareFacesAsync(new CompareFacesRequest
        {
            SourceImage = new Image { Bytes = sourceBytes },
            TargetImage = new Image { Bytes = targetBytes },
            SimilarityThreshold = _options.FaceMatchSimilarityThreshold
        }, ct);

        var bestMatch = response.FaceMatches?
            .OrderByDescending(m => m.Similarity)
            .FirstOrDefault();

        return bestMatch is null
            ? new FaceMatchOutcome(false, 0f)
            : new FaceMatchOutcome(true, bestMatch.Similarity ?? 0f);
    }

    private static async Task<MemoryStream> ToMemoryStreamAsync(Stream input, CancellationToken ct)
    {
        if (input is MemoryStream ms)
        {
            ms.Position = 0;
            return ms;
        }

        var copy = new MemoryStream();
        await input.CopyToAsync(copy, ct);
        copy.Position = 0;
        return copy;
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, immediately after the existing liveness service registration (currently around line 509-511):

```csharp
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceLivenessService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceLivenessService>();
        services.AddScoped<
            ONEVO.Application.Common.ServiceInterfaces.IFaceMatchService,
            ONEVO.Infrastructure.Services.Monitoring.Biometrics.RekognitionFaceMatchService>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter RekognitionFaceMatchServiceTests`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IFaceMatchService.cs src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceMatchService.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/RekognitionFaceMatchServiceTests.cs
git commit -m "feat: add IFaceMatchService wrapping AWS Rekognition CompareFaces"
```

---

## Task 3: Store a reference photo at enrollment

**Files:**
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IFaceLivenessService.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceLivenessService.cs`
- Modify: `src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfile.cs`
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- Modify: `src/ONEVO.Domain/Errors/MonitoringErrors.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs`
- Create: migration via `dotnet ef migrations add AddBiometricProfileReferencePhoto`

- [ ] **Step 1: Extend `FaceLivenessOutcome` to carry the reference image**

In `IFaceLivenessService.cs`, change:

```csharp
public record FaceLivenessOutcome(string Status, float Confidence);
```

to:

```csharp
public record FaceLivenessOutcome(string Status, float Confidence, MemoryStream? ReferenceImageBytes);
```

- [ ] **Step 2: Populate it from the AWS response**

In `RekognitionFaceLivenessService.cs`, change `GetSessionResultAsync`:

```csharp
    public async Task<FaceLivenessOutcome> GetSessionResultAsync(string sessionId, CancellationToken ct)
    {
        var response = await _rekognition.GetFaceLivenessSessionResultsAsync(
            new GetFaceLivenessSessionResultsRequest { SessionId = sessionId }, ct);

        return new FaceLivenessOutcome(
            response.Status.Value,
            response.Confidence ?? 0f,
            response.ReferenceImage?.Bytes);
    }
```

- [ ] **Step 3: Add the reference-photo error message**

In `src/ONEVO.Domain/Errors/MonitoringErrors.cs`, add after `LivenessCheckFailed`:

```csharp
    public const string ReferenceImageMissing =
        "Liveness session did not return a reference photo; please retry enrollment.";
```

- [ ] **Step 4: Add the domain field**

In `BiometricProfile.cs`, add after `EnrolledAt`:

```csharp
    /// <summary>file_records.Id of the reference photo captured at enrollment. Null until Task 3 lands / enrollment completes with a reference image.</summary>
    public Guid? ReferencePhotoFileId { get; set; }
```

- [ ] **Step 5: Add the upload purpose**

In `UploadPurposeCatalog.cs`, add the constant after `MonitoringFaceScan`:

```csharp
    public const string BiometricReferencePhoto = "biometric_reference_photo";
```

and add its rule to the `Rules` dictionary, after the `MonitoringFaceScan` entry:

```csharp
        [BiometricReferencePhoto] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
```

- [ ] **Step 6: Update existing enrollment tests to supply a reference image**

In `CompleteEnrollmentAttemptCommandHandlerTests.cs`, this file will fail to compile once `FaceLivenessOutcome` gains a third parameter. Update every existing `new FaceLivenessOutcome(...)` call:

- `HighConfidenceSuccess_CreatesProfileAndMarksAttemptSucceeded`: change
  ```csharp
  .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f));
  ```
  to
  ```csharp
  .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f, new MemoryStream(new byte[] { 1, 2, 3 })));
  ```

- `LowConfidence_MarksAttemptFailed_ReturnsUnprocessableEntity`: change
  ```csharp
  .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 42f));
  ```
  to
  ```csharp
  .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 42f, new MemoryStream(new byte[] { 1, 2, 3 })));
  ```

- [ ] **Step 7: Write the new failing tests for reference-photo behavior**

Add to the same test class (needs `using ONEVO.Application.Features.Storage.File.DTOs.Responses;`, `using ONEVO.Application.Features.Storage.File.ServiceInterfaces;`, `using ONEVO.Application.Common.Models;` at the top, and a `Mock<IFileStorageService> _fileStorage = new();` field, and update `CreateSut()` to pass `_fileStorage.Object`):

```csharp
    [Fact]
    public async Task HighConfidenceSuccess_UploadsReferencePhoto_AndSetsFileIdOnProfile()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        var referenceFileId = Guid.NewGuid();
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        _liveness.Setup(l => l.GetSessionResultAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f, new MemoryStream(new byte[] { 1, 2, 3 })));
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), "image/jpeg",
                UploadPurposeCatalog.BiometricReferencePhoto, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                referenceFileId, _tenantId, "tenants/x/files/y/z.jpg", "reference-photo.jpg", "z.jpg",
                "image/jpeg", 3, "checksum", "available", DateTimeOffset.UtcNow)));

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _profiles.Verify(p => p.AddAsync(
            It.Is<BiometricProfile>(bp => bp.ReferencePhotoFileId == referenceFileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingReferenceImage_MarksAttemptFailed_ReturnsUnprocessableEntity_WithoutUploading()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        _liveness.Setup(l => l.GetSessionResultAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f, null));

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        attempt.Status.Should().Be(BiometricEnrollmentStatus.Failed);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReferencePhotoUploadFails_MarksAttemptFailed_ReturnsUploadError()
    {
        var attempt = PendingAttempt(_clock.UtcNow);
        _attempts.Setup(a => a.GetByIdAsync(_tenantId, _userId, _attemptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        _liveness.Setup(l => l.GetSessionResultAsync("aws-session-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceLivenessOutcome("SUCCEEDED", 97.5f, new MemoryStream(new byte[] { 1, 2, 3 })));
        _fileStorage.Setup(f => f.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("Storage quota exceeded.", 507));

        var result = await CreateSut().Handle(new CompleteEnrollmentAttemptCommand(_attemptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(507);
        attempt.Status.Should().Be(BiometricEnrollmentStatus.Failed);
        _profiles.Verify(p => p.AddAsync(It.IsAny<BiometricProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Also update `CreateSut()`:

```csharp
    private CompleteEnrollmentAttemptCommandHandler CreateSut() => new(
        _attempts.Object, _profiles.Object, _device.Object, _liveness.Object, _fileStorage.Object, _clock, Options.Create(_options));
```

- [ ] **Step 8: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CompleteEnrollmentAttemptCommandHandlerTests`
Expected: FAIL — constructor arg count mismatch (handler doesn't take `IFileStorageService` yet).

- [ ] **Step 9: Update the handler**

In `CompleteEnrollmentAttemptCommandHandler.cs`:

Add the field, constructor param, and using statements:

```csharp
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
```

```csharp
    private readonly IBiometricEnrollmentAttemptRepository _attempts;
    private readonly IBiometricProfileRepository _profiles;
    private readonly ITrayCurrentDevice _device;
    private readonly IFaceLivenessService _liveness;
    private readonly IFileStorageService _fileStorage;
    private readonly IDateTimeProvider _clock;
    private readonly BiometricEnrollmentOptions _options;

    public CompleteEnrollmentAttemptCommandHandler(
        IBiometricEnrollmentAttemptRepository attempts,
        IBiometricProfileRepository profiles,
        ITrayCurrentDevice device,
        IFaceLivenessService liveness,
        IFileStorageService fileStorage,
        IDateTimeProvider clock,
        IOptions<BiometricEnrollmentOptions> options)
    {
        _attempts = attempts;
        _profiles = profiles;
        _device = device;
        _liveness = liveness;
        _fileStorage = fileStorage;
        _clock = clock;
        _options = options.Value;
    }
```

Replace the block from `attempt.Status = BiometricEnrollmentStatus.Succeeded;` through the end of the method with:

```csharp
        if (outcome.ReferenceImageBytes is null)
        {
            attempt.Status = BiometricEnrollmentStatus.Failed;
            attempt.FailureReason = MonitoringErrors.ReferenceImageMissing;
            _attempts.Update(attempt);
            await _attempts.SaveChangesAsync(ct);
            return Result<BiometricProfileResponse>.UnprocessableEntity(MonitoringErrors.LivenessCheckFailed);
        }

        var referenceUpload = await _fileStorage.UploadAsync(
            tenantId, employeeId, "reference-photo.jpg", "image/jpeg",
            UploadPurposeCatalog.BiometricReferencePhoto, outcome.ReferenceImageBytes, ct);

        if (!referenceUpload.IsSuccess)
        {
            attempt.Status = BiometricEnrollmentStatus.Failed;
            attempt.FailureReason = $"Reference photo storage failed: {referenceUpload.Error}";
            _attempts.Update(attempt);
            await _attempts.SaveChangesAsync(ct);
            return Result<BiometricProfileResponse>.Failure(
                referenceUpload.Error!, referenceUpload.StatusCode ?? 500);
        }

        attempt.Status = BiometricEnrollmentStatus.Succeeded;
        _attempts.Update(attempt);

        var referencePhotoFileId = referenceUpload.Value!.Id;

        var existingProfile = await _profiles.GetByEmployeeIdAsync(tenantId, employeeId, ct);
        BiometricProfile profile;
        if (existingProfile is not null)
        {
            existingProfile.Status = BiometricProfileStatus.Enrolled;
            existingProfile.EnrolledAt = now;
            existingProfile.UpdatedAt = now;
            existingProfile.ReferencePhotoFileId = referencePhotoFileId;
            _profiles.Update(existingProfile);
            profile = existingProfile;
        }
        else
        {
            profile = new BiometricProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                Status = BiometricProfileStatus.Enrolled,
                EnrolledAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ReferencePhotoFileId = referencePhotoFileId
            };
            await _profiles.AddAsync(profile, ct);
        }

        await _attempts.SaveChangesAsync(ct);
        await _profiles.SaveChangesAsync(ct);

        return Result<BiometricProfileResponse>.Success(
            new BiometricProfileResponse(profile.Id, profile.Status.ToString(), profile.EnrolledAt));
    }
}
```

(This replaces the old unconditional `attempt.Status = BiometricEnrollmentStatus.Succeeded;` block — the `if (!string.Equals(outcome.Status, ...` confidence check above it is unchanged.)

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CompleteEnrollmentAttemptCommandHandlerTests`
Expected: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`

- [ ] **Step 11: Generate and review the migration**

Ensure local Postgres is set up (see `ops/postgres/setup-local-db.ps1` if `MigrationConnection` isn't configured yet — see [[project_hrms_migration_drift_2026-08-20]] memory for why this matters), then run:

```bash
dotnet ef migrations add AddBiometricProfileReferencePhoto --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: creates `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddBiometricProfileReferencePhoto.cs` (+ `.Designer.cs`) adding a nullable `reference_photo_file_id uuid` column to `biometric_profiles`, and updates `ApplicationDbContextModelSnapshot.cs`. Open the generated `.cs` file and confirm it contains exactly one `AddColumn` call for `reference_photo_file_id` on table `biometric_profiles` — no unrelated changes. If EF also picked up unrelated pending model changes from other in-progress work on `development`, stop and report rather than committing them.

- [ ] **Step 12: Apply the migration to local DB and verify**

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: `Done.` Then confirm via `psql`: `\d biometric_profiles` shows `reference_photo_file_id | uuid |`.

- [ ] **Step 13: Full build + test run**

```bash
dotnet build src/ONEVO.Api
dotnet test tests/ONEVO.Tests.Unit
```

Expected: build succeeds, all tests pass.

- [ ] **Step 14: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IFaceLivenessService.cs \
        src/ONEVO.Infrastructure/Services/Monitoring/Biometrics/RekognitionFaceLivenessService.cs \
        src/ONEVO.Domain/Features/Monitoring/Biometrics/Entities/BiometricProfile.cs \
        src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs \
        src/ONEVO.Domain/Errors/MonitoringErrors.cs \
        src/ONEVO.Application/Features/Monitoring/Biometrics/Commands/CompleteEnrollmentAttempt/CompleteEnrollmentAttemptCommandHandler.cs \
        tests/ONEVO.Tests.Unit/Features/Monitoring/Biometrics/CompleteEnrollmentAttemptCommandHandlerTests.cs \
        src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: store reference photo on BiometricProfile at enrollment completion"
```

---

## Task 4: Real verification on check-in face-scan upload

**Files:**
- Modify: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScanStatus.cs`
- Modify: `src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/FaceScanUploadResponseDto.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandHandler.cs`
- Create/modify: `tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/UploadFaceScanCommandHandlerTests.cs`
- Create: migration via `dotnet ef migrations add AddMonitoringFaceScanSimilarityScore`

- [ ] **Step 1: Confirm no test file exists yet**

Run: `find tests/ONEVO.Tests.Unit -iname "UploadFaceScanCommandHandlerTests.cs"`
Expected: no output (confirmed already during planning — this handler has no unit tests today). Step 5 below creates the file fresh.

- [ ] **Step 2: Add the new statuses**

In `MonitoringFaceScanStatus.cs`:

```csharp
public static class MonitoringFaceScanStatus
{
    public const string PendingScan     = "pending_scan";
    public const string Available       = "available";
    public const string Failed          = "failed";
    public const string Verified        = "verified";
    public const string NotMatched      = "not_matched";
    public const string NoReferencePhoto = "no_reference_photo";
}
```

- [ ] **Step 3: Add the domain column**

In `MonitoringFaceScan.cs`, add after `ContentType`:

```csharp
    /// <summary>CompareFaces similarity 0-100 against the employee's enrolled reference photo. Null when there was no reference photo to compare against.</summary>
    public float? SimilarityScore { get; set; }
```

- [ ] **Step 4: Extend the response DTO**

In `FaceScanUploadResponseDto.cs`:

```csharp
public record FaceScanUploadResponseDto(
    [property: JsonPropertyName("face_scan_id")] Guid FaceScanId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("similarity_score")] float? SimilarityScore);
```

- [ ] **Step 5: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/UploadFaceScanCommandHandlerTests.cs` in full:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.Commands.UploadFaceScan;
using ONEVO.Application.Features.Monitoring.CheckIn.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;
using ONEVO.Domain.Features.Monitoring.CheckIn.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Commands;

public class UploadFaceScanCommandHandlerTests
{
    private readonly Mock<ICheckInRepository> _repository = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _tenantSwitcher = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IBiometricProfileRepository> _profiles = new();
    private readonly Mock<IFaceMatchService> _faceMatch = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public UploadFaceScanCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(Guid.NewGuid());
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = _tenantId, Slug = "acme" });
        _tenantSwitcher.Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private UploadFaceScanCommandHandler CreateSut() => new(
        _repository.Object, _device.Object, _tenants.Object, _tenantSwitcher.Object,
        _fileStorage.Object, _profiles.Object, _faceMatch.Object, _clock, _unitOfWork.Object);

    private (EmployeeCheckIn CheckIn, Guid UploadedFileId) SetupSuccessfulUploadPath()
    {
        var checkIn = new EmployeeCheckIn { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId };
        var uploadedFileId = Guid.NewGuid();

        _repository.Setup(r => r.FindCheckInAsync(checkIn.Id, _tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIn);
        _fileStorage.Setup(f => f.UploadAsync(
                _tenantId, _userId, It.IsAny<string>(), It.IsAny<string>(),
                UploadPurposeCatalog.MonitoringFaceScan, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                uploadedFileId, _tenantId, "tenants/x/files/y/scan.jpg", "scan.jpg", "scan.jpg",
                "image/jpeg", 3, "checksum", "available", DateTimeOffset.UtcNow)));

        return (checkIn, uploadedFileId);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_AndFacesMatch_SetsVerifiedWithSimilarity()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(true, 93.4f));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.Verified);
        result.Value!.SimilarityScore.Should().Be(93.4f);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_ButFacesDoNotMatch_SetsNotMatched()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1 }), "image/jpeg")));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));
        _faceMatch.Setup(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FaceMatchOutcome(false, 12.1f));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.NotMatched);
        result.Value!.SimilarityScore.Should().Be(12.1f);
    }

    [Fact]
    public async Task NoBiometricProfile_SetsNoReferencePhoto_WithoutCallingFaceMatch()
    {
        var (checkIn, _) = SetupSuccessfulUploadPath();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BiometricProfile?)null);

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.NoReferencePhoto);
        result.Value!.SimilarityScore.Should().BeNull();
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileHasReferencePhoto_ButOpenReadFails_SetsFailed_WithoutCallingFaceMatch()
    {
        var (checkIn, uploadedFileId) = SetupSuccessfulUploadPath();
        var referenceFileId = Guid.NewGuid();
        _profiles.Setup(p => p.GetByEmployeeIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BiometricProfile
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _userId,
                Status = BiometricProfileStatus.Enrolled, ReferencePhotoFileId = referenceFileId
            });
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, referenceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Failure("Reference photo not found.", 404));
        _fileStorage.Setup(f => f.OpenReadAsync(_tenantId, uploadedFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 2 }), "image/jpeg")));

        var result = await CreateSut().Handle(
            new UploadFaceScanCommand(checkIn.Id, new MemoryStream(new byte[] { 3 }), "image/jpeg", 3),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(MonitoringFaceScanStatus.Failed);
        result.Value!.SimilarityScore.Should().BeNull();
        _faceMatch.Verify(m => m.CompareAsync(It.IsAny<Stream>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 6: Run tests to verify the new ones fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter UploadFaceScanCommandHandlerTests`
Expected: FAIL to compile — the handler's constructor doesn't take `IBiometricProfileRepository`/`IFaceMatchService` yet, and `FaceScanUploadResponseDto`'s 4th positional arg doesn't exist yet. (Steps 2/3/4 above must already be done before this run — they add `MonitoringFaceScanStatus.Verified`/`NotMatched`/`NoReferencePhoto`, `MonitoringFaceScan.SimilarityScore`, and the DTO's `SimilarityScore` field.)

- [ ] **Step 7: Update the handler**

In `UploadFaceScanCommandHandler.cs`, add usings:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
```

Add fields + constructor params:

```csharp
    private readonly ICheckInRepository _repository;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IFileStorageService _fileStorage;
    private readonly IBiometricProfileRepository _profiles;
    private readonly IFaceMatchService _faceMatch;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UploadFaceScanCommandHandler(
        ICheckInRepository repository,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IFileStorageService fileStorage,
        IBiometricProfileRepository profiles,
        IFaceMatchService faceMatch,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _fileStorage = fileStorage;
        _profiles = profiles;
        _faceMatch = faceMatch;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }
```

Replace the block from `var fileRecord = uploadResult.Value!;` to the end of the method with:

```csharp
        var fileRecord = uploadResult.Value!;

        var (matchStatus, similarity) = await VerifyAgainstReferencePhotoAsync(fileRecord.Id, cancellationToken);

        var now = _clock.UtcNow;
        var faceScan = new MonitoringFaceScan
        {
            Id              = Guid.NewGuid(),
            TenantId        = _device.TenantId,
            CheckInId       = request.CheckInId,
            StorageKey      = fileRecord.StorageKey,
            FileSizeBytes   = request.FileSizeBytes,
            ContentType     = request.ContentType,
            Status          = matchStatus,
            SimilarityScore = similarity,
            CreatedAt       = now,
            UpdatedAt       = null
        };

        await _repository.AddFaceScanAsync(faceScan, cancellationToken);

        checkIn.FaceScanId = faceScan.Id;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FaceScanUploadResponseDto>.Success(new FaceScanUploadResponseDto(
            faceScan.Id,
            faceScan.Status,
            faceScan.FileSizeBytes,
            faceScan.SimilarityScore));
    }

    private async Task<(string Status, float? Similarity)> VerifyAgainstReferencePhotoAsync(
        Guid capturedFileId, CancellationToken ct)
    {
        var profile = await _profiles.GetByEmployeeIdAsync(_device.TenantId, _device.UserId, ct);
        if (profile?.ReferencePhotoFileId is null)
            return (MonitoringFaceScanStatus.NoReferencePhoto, null);

        var referenceRead = await _fileStorage.OpenReadAsync(_device.TenantId, profile.ReferencePhotoFileId.Value, ct);
        var capturedRead = await _fileStorage.OpenReadAsync(_device.TenantId, capturedFileId, ct);

        if (!referenceRead.IsSuccess || !capturedRead.IsSuccess)
            return (MonitoringFaceScanStatus.Failed, null);

        var outcome = await _faceMatch.CompareAsync(referenceRead.Value!.Content, capturedRead.Value!.Content, ct);
        return outcome.IsMatch
            ? (MonitoringFaceScanStatus.Verified, outcome.Similarity)
            : (MonitoringFaceScanStatus.NotMatched, outcome.Similarity);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter UploadFaceScanCommandHandlerTests`
Expected: all pass, including the 3 new ones from Step 5.

- [ ] **Step 9: Generate, review, and apply the migration**

```bash
dotnet ef migrations add AddMonitoringFaceScanSimilarityScore --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: adds a single nullable `similarity_score real` column to `monitoring_face_scans`. Review the generated file before proceeding — confirm no unrelated diffs.

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Expected: `Done.`

- [ ] **Step 10: Full build + full test suite**

```bash
dotnet build src/ONEVO.Api
dotnet test tests/ONEVO.Tests.Unit
```

Expected: build succeeds, all tests pass (3721 + new tests from this plan).

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScanStatus.cs \
        src/ONEVO.Domain/Features/Monitoring/CheckIn/Entities/MonitoringFaceScan.cs \
        src/ONEVO.Application/Features/Monitoring/CheckIn/DTOs/Responses/FaceScanUploadResponseDto.cs \
        src/ONEVO.Application/Features/Monitoring/CheckIn/Commands/UploadFaceScan/UploadFaceScanCommandHandler.cs \
        tests/ONEVO.Tests.Unit/Features/Monitoring/CheckIn/Commands/UploadFaceScanCommandHandlerTests.cs \
        src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: verify check-in face-scan against enrolled reference photo via CompareFaces"
```

---

## Task 5: Integration test (end-to-end sanity check)

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInIntegrationTests.cs`

- [ ] **Step 1: Read the existing file fully** to match its existing setup (auth, tenant seeding, real Postgres via Testcontainers, HTTP client conventions) before adding to it.

- [ ] **Step 2: Add one integration test** that: enrolls a profile with a stubbed/faked `IFaceMatchService` and `IFaceLivenessService` (check how other integration tests fake AWS-backed services — likely a test double registered in the test `WebApplicationFactory`), completes enrollment, submits a check-in, uploads a face-scan, and asserts the returned `status` is `verified` and `similarity_score` is non-null. Follow the existing file's structure for exact assertion style — do not invent a new pattern.

- [ ] **Step 3: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter CheckInIntegrationTests` (per [[reference_local_integration_test_repro_without_docker]] memory, requires `ONEVO_TEST_DB` pointed at a scratch Postgres, or Docker/Testcontainers if available).
Expected: passes.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInIntegrationTests.cs
git commit -m "test: add end-to-end face match verification integration test"
```

---

## Open design decision to confirm with teammate before merging

`VerifyAgainstReferencePhotoAsync` currently treats "no enrolled reference photo" (`NoReferencePhoto`) and "AWS/storage read failure" (`Failed`) as non-blocking — the check-in itself still succeeds (HTTP 200), only the face-scan `status` field reflects the verification outcome. This matches today's behavior (check-in was never blocked by face-scan issues) and is called out explicitly in the original task breakdown as something to decide with the teammate. If the product requirement is actually to **block** check-in on `NotMatched`/`NoReferencePhoto`, that changes `UploadFaceScanCommandHandler`'s return contract and needs the API consumer (TrayApp) updated too — out of scope for this plan as written.
