# Objective Asset Attachments (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let modules/sub-modules ("Objectives") have documents/images/ZIP files attached to them, via two new endpoints (`POST`/`DELETE .../assets`) built on the existing generic `entity_assets` table — no new migration.

**Architecture:** Reuse `EntityAsset` (currently scoped to `OwnerType = "project"`) for objective attachments by adding `OwnerType = "objective"`. Uploads go through the existing `IFileStorageService.UploadAsync` under a new `objective_asset` purpose. Two new MediatR command handlers (`UploadObjectiveAsset`, `DeleteObjectiveAsset`) follow the exact patterns already used by `CreateProjectCommandHandler` (upload) and `RemoveLegalEntityLogoCommandHandler` (delete-the-link-not-the-file). `GetObjectiveByIdQueryHandler` is extended to return the attached assets list with signed download URLs.

**Tech Stack:** .NET / ASP.NET Core, MediatR (CQRS), EF Core + PostgreSQL, xUnit + Moq + FluentAssertions (handler tests use Moq/FluentAssertions; the Storage/File area's existing tests use plain xUnit `Assert` — each task's tests follow whichever convention its file already uses).

**Base branch:** `feature/wm-approval-hours-and-component-tuning` (this feature area does not exist on `main` or `development`). Work on `docs/module-creation-assets-design`, which is already based on it, or a fresh branch off it.

## Global Constraints

- Route prefix for all objective endpoints is `api/v1/work/objectives` (confirmed from `ObjectivesController`'s `[Route]` attribute) — not `api/objectives`.
- `IFileStorageService` has no delete method. Never attempt to delete a `file_records` row from feature code — only ever remove the `entity_assets` join row, matching `RemoveLegalEntityLogoCommandHandler`.
- Extension allow-list: `pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif, zip`. Per-file size cap: 25MB. (From the approved spec, `docs/superpowers/specs/2026-09-02-module-creation-assets-members-design.md`.)
- New upload purpose constant: `objective_asset`. New owner type constant: `objective`.
- Mutating objective-scoped endpoints in this codebase use `[RequirePermission("projects:access")]` and do **not** use `[Idempotent]` (only `ProjectsController.Create` does) — follow that for both new endpoints.

---

### Task 1: Register the `objective_asset` upload purpose and fix a content-type/extension validation gap

**Files:**
- Modify: `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposePolicyTests.cs`

**Interfaces:**
- Produces: `EntityAssetOwnerTypes.Objective` (`"objective"`), `UploadPurposeCatalog.ObjectiveAsset` (`"objective_asset"`) — used by Tasks 3–5.

**Why this task exists:** `UploadPurposePolicy.ValidateUpload` checks `AllowedExtensions`/`AllowedContentTypes` from `UploadPurposeCatalog`, but *also* runs every upload through a second, independent check — `ContentTypeMatchesExtension`, a hardcoded `switch` that currently only recognizes `.png/.jpg/.jpeg/.webp/.pdf/.doc/.docx`. If we only add the new purpose rule without extending this switch, every `.gif`, `.xls`, `.xlsx`, and `.zip` upload — exactly the file types this feature needs — would pass the allow-list check and then fail the `_ => false` fallthrough, rejected with "contentType does not match file extension". Both changes are required together.

- [ ] **Step 1: Write the failing tests**

Add to `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposePolicyTests.cs` (plain xUnit `Assert`, matching this file's existing style):

```csharp
    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetZipUpload()
    {
        var result = _policy.ValidateUpload("objective_asset", "archive.zip", "application/zip", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetXlsxUpload()
    {
        var result = _policy.ValidateUpload(
            "objective_asset", "budget.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetGifUpload()
    {
        var result = _policy.ValidateUpload("objective_asset", "diagram.gif", "image/gif", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsOversizedObjectiveAsset()
    {
        var result = _policy.ValidateUpload("objective_asset", "huge.zip", "application/zip", 26 * 1024 * 1024);

        Assert.False(result.IsSuccess);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposePolicyTests"`
Expected: the three "Accepts..." tests FAIL (purpose `objective_asset` not yet supported), the oversized test passes trivially (already rejected as unsupported purpose) — re-run after Step 3 to confirm it fails for the *size* reason, not the purpose reason, is not required; the important signal is the three Accept tests failing now.

- [ ] **Step 3: Add the owner type constant**

In `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`, add:

```csharp
public static class EntityAssetOwnerTypes
{
    public const string Project = "project";
    public const string Objective = "objective";
}
```

- [ ] **Step 4: Register the purpose rule**

In `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`, add the constant, two new static lists, and a new `Rules` entry:

```csharp
    public const string ObjectiveAsset = "objective_asset";
```

(add this line alongside the other `public const string` purpose constants)

```csharp
    private static readonly IReadOnlyList<string> ObjectiveAssetContentTypes = new[]
    {
        "application/pdf", "image/png", "image/jpeg", "image/gif",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip"
    };

    private static readonly IReadOnlyList<string> ObjectiveAssetExtensions = new[]
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".doc", ".docx", ".xls", ".xlsx", ".zip"
    };
```

(add these two fields alongside `ImageContentTypes`/`ImageExtensions`)

```csharp
        [ObjectiveAsset] = new UploadPurposeRule(25 * 1024 * 1024, ObjectiveAssetContentTypes, ObjectiveAssetExtensions),
```

(add this line inside the `Rules` dictionary initializer)

- [ ] **Step 5: Extend the content-type/extension matcher**

In `src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs`, extend the `switch` inside `ContentTypeMatchesExtension`:

```csharp
    private static bool ContentTypeMatchesExtension(string contentType, string extension)
    {
        return extension switch
        {
            ".png" => contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".webp" => contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase),
            ".gif" => contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase),
            ".pdf" => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".doc" => contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase),
            ".docx" => contentType.Equals(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                StringComparison.OrdinalIgnoreCase),
            ".xls" => contentType.Equals("application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase),
            ".xlsx" => contentType.Equals(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                StringComparison.OrdinalIgnoreCase),
            ".zip" => contentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposePolicyTests"`
Expected: PASS (all tests in the file, including the 4 new ones)

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Infrastructure/Services/Storage/File/UploadPurposePolicy.cs tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposePolicyTests.cs
git commit -m "feat: register objective_asset upload purpose and fix zip/xlsx/gif validation gap"
```

---

### Task 2: Extend `IEntityAssetRepository` to list/fetch/delete objective assets

**Files:**
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs`

**Interfaces:**
- Consumes: `EntityAsset` (`ONEVO.Domain.Features.Storage.EntityAssets.Entities`), `FileRecord` (`ONEVO.Domain.Features.Storage.File.Entities`, already referenced by `EntityAssetConfiguration`).
- Produces: `EntityAssetWithFile(Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt)` record; `IEntityAssetRepository.ListByOwnerAsync(Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct)`, `GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)`, `DeleteAsync(EntityAsset asset, CancellationToken ct)` — used by Tasks 3, 4, 5.

No test file for this task: `EfEntityAssetRepository` has no existing unit-test file (it's thin LINQ over `ApplicationDbContext`, consistent with `GetPrimaryFileIdsByOwnerAsync` having no dedicated unit test either) — it's exercised by the integration test in Task 6 instead.

- [ ] **Step 1: Add the new interface methods**

Replace the full contents of `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`:

```csharp
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

/// <summary>Projection of an entity_assets row joined with its file_records metadata, for listing.</summary>
public sealed record EntityAssetWithFile(
    Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt);

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);

    /// <summary>Batched lookup of each owner's primary asset file id for a given purpose (e.g. project cover images for a page of project list rows). Owners with no matching primary asset are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default);

    /// <summary>All assets for a single owner (e.g. every file attached to one objective), joined with file metadata, oldest first.</summary>
    Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default);

    Task<EntityAsset?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task DeleteAsync(EntityAsset asset, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement the new methods**

Replace the full contents of `src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEntityAssetRepository : IEntityAssetRepository
{
    private readonly ApplicationDbContext _db;

    public EfEntityAssetRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(EntityAsset asset, CancellationToken ct = default)
    {
        await _db.EntityAssets.AddAsync(asset, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default)
    {
        if (ownerIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        return await _db.EntityAssets.AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId &&
                a.OwnerType == ownerType &&
                a.AssetPurpose == assetPurpose &&
                a.IsPrimary &&
                ownerIds.Contains(a.OwnerId))
            .ToDictionaryAsync(a => a.OwnerId, a => a.FileRecordId, ct);
    }

    public async Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        return await _db.EntityAssets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OwnerType == ownerType && a.OwnerId == ownerId)
            .Join(_db.FileRecords.AsNoTracking(), a => a.FileRecordId, f => f.Id,
                (a, f) => new EntityAssetWithFile(a.Id, f.Id, f.OriginalFileName, f.FileSizeBytes, f.ContentType, a.CreatedAt))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<EntityAsset?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.EntityAssets.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);
    }

    public Task DeleteAsync(EntityAsset asset, CancellationToken ct = default)
    {
        _db.EntityAssets.Remove(asset);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: build succeeds (no test yet — this task's behavior is verified end-to-end in Task 6)

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs
git commit -m "feat: add list/get/delete methods to IEntityAssetRepository"
```

---

### Task 3: `POST /api/v1/work/objectives/{id}/assets` — upload and attach a file

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveAssetResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/UploadObjectiveAssetCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/UploadObjectiveAssetCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/UploadObjectiveAssetRequest.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveAssetViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/UploadObjectiveAssetCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)` (existing); `IEntityAssetRepository.AddAsync` (existing); `IFileStorageService.UploadAsync(Guid tenantId, Guid userId, string originalFileName, string contentType, string purpose, Stream content, CancellationToken ct)` and `.GetSignedUrlAsync(Guid tenantId, Guid fileRecordId, TimeSpan expiry, CancellationToken ct)` (existing); `IUnitOfWork.SaveChangesAsync(CancellationToken ct)` (existing); `EntityAssetOwnerTypes.Objective`, `UploadPurposeCatalog.ObjectiveAsset` (Task 1).
- Produces: `ObjectiveAssetResponse(Guid Id, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt, string DownloadUrl)` — consumed by Task 5's `ObjectiveDetailResponse.Assets`.

- [ ] **Step 1: Write the failing unit test**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/UploadObjectiveAssetCommandHandlerTests.cs` (Moq + FluentAssertions, matching `LegalEntityLogoCommandHandlerTests.cs`'s conventions):

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class UploadObjectiveAssetCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private UploadObjectiveAssetCommandHandler BuildHandler()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        return new UploadObjectiveAssetCommandHandler(
            _currentUser.Object, _objectives.Object, _entityAssets.Object, _fileStorage.Object, _unitOfWork.Object);
    }

    private static Objective ActiveObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, IsActive = true, Title = "Sub",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_ValidUpload_UploadsAddsAssetAndReturnsSignedUrl()
    {
        var handler = BuildHandler();
        _objectives.Setup(r => r.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveObjective());
        var fileRecordId = Guid.NewGuid();
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        _fileStorage.Setup(f => f.UploadAsync(
                TenantId, UserId, "plan.pdf", "application/pdf", UploadPurposeCatalog.ObjectiveAsset, content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                fileRecordId, TenantId, "key", "plan.pdf", "plan.pdf", "application/pdf", 3, new string('a', 64), "PendingScan", DateTimeOffset.UtcNow)));
        _fileStorage.Setup(f => f.GetSignedUrlAsync(TenantId, fileRecordId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed.example/plan.pdf"));

        var result = await handler.Handle(
            new UploadObjectiveAssetCommand(ObjectiveId, "plan.pdf", "application/pdf", content), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileName.Should().Be("plan.pdf");
        result.Value.DownloadUrl.Should().Be("https://signed.example/plan.pdf");
        _entityAssets.Verify(r => r.AddAsync(
            It.Is<EntityAsset>(a =>
                a.TenantId == TenantId &&
                a.OwnerType == EntityAssetOwnerTypes.Objective &&
                a.OwnerId == ObjectiveId &&
                a.FileRecordId == fileRecordId &&
                !a.IsPrimary),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound_AndNeverUploads()
    {
        var handler = BuildHandler();
        _objectives.Setup(r => r.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Objective?)null);
        using var content = new MemoryStream(new byte[] { 1 });

        var result = await handler.Handle(
            new UploadObjectiveAssetCommand(ObjectiveId, "plan.pdf", "application/pdf", content), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UploadRejected_ReturnsUploadFailure_AndNeverAddsAsset()
    {
        var handler = BuildHandler();
        _objectives.Setup(r => r.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveObjective());
        using var content = new MemoryStream(new byte[] { 1 });
        _fileStorage.Setup(f => f.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("File exceeds the maximum allowed size.", 400));

        var result = await handler.Handle(
            new UploadObjectiveAssetCommand(ObjectiveId, "huge.zip", "application/zip", content), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _entityAssets.Verify(r => r.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadObjectiveAssetCommandHandlerTests"`
Expected: FAIL to compile — `UploadObjectiveAssetCommand`/`Handler` don't exist yet.

- [ ] **Step 3: Create the response DTO**

`src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveAssetResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveAssetResponse(
    Guid Id, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt, string DownloadUrl);
```

- [ ] **Step 4: Create the command**

`src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/UploadObjectiveAssetCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;

public sealed record UploadObjectiveAssetCommand(
    Guid ObjectiveId,
    string FileName,
    string ContentType,
    Stream Content
) : IRequest<Result<ObjectiveAssetResponse>>;
```

- [ ] **Step 5: Create the handler**

`src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/UploadObjectiveAssetCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;

public class UploadObjectiveAssetCommandHandler : IRequestHandler<UploadObjectiveAssetCommand, Result<ObjectiveAssetResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public UploadObjectiveAssetCommandHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, IEntityAssetRepository entityAssets,
        IFileStorageService fileStorage, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _entityAssets = entityAssets;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ObjectiveAssetResponse>> Handle(UploadObjectiveAssetCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveAssetResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveAssetResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveAssetResponse>.NotFound("Objective not found.");

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId, userId, request.FileName, request.ContentType,
            UploadPurposeCatalog.ObjectiveAsset, request.Content, ct);

        if (!uploadResult.IsSuccess)
            return Result<ObjectiveAssetResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        var uploadedFile = uploadResult.Value!;
        var now = DateTimeOffset.UtcNow;

        var asset = new EntityAsset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = EntityAssetOwnerTypes.Objective,
            OwnerId = objective.Id,
            AssetPurpose = UploadPurposeCatalog.ObjectiveAsset,
            FileRecordId = uploadedFile.Id,
            IsPrimary = false,
            CreatedByType = "user",
            CreatedById = userId,
            CreatedAt = now
        };

        await _entityAssets.AddAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, uploadedFile.Id, TimeSpan.FromMinutes(15), ct);

        return Result<ObjectiveAssetResponse>.Success(new ObjectiveAssetResponse(
            asset.Id, uploadedFile.OriginalFileName, uploadedFile.FileSizeBytes, uploadedFile.ContentType,
            asset.CreatedAt, urlResult.IsSuccess ? urlResult.Value! : string.Empty));
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadObjectiveAssetCommandHandlerTests"`
Expected: PASS (all 3 tests)

- [ ] **Step 7: Wire the HTTP endpoint**

Create `src/ONEVO.Api/Contracts/WorkManagement/Objectives/UploadObjectiveAssetRequest.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public class UploadObjectiveAssetRequest
{
    public IFormFile File { get; set; } = null!;
}
```

Create `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveAssetViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveAssetViewModel(
    Guid Id, string FileName, long SizeBytes, string ContentType, DateTimeOffset UploadedAt, string DownloadUrl);
```

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, add this method (anywhere in the class, e.g. right after the `ObjectiveDetailViewModel` mapper — Task 5 will change the `ObjectiveDetailViewModel` mapper's body to call it):

```csharp
    public static ObjectiveAssetViewModel ToViewModel(this ObjectiveAssetResponse dto) => new(
        dto.Id, dto.FileName, dto.SizeBytes, dto.ContentType, dto.UploadedAt, dto.DownloadUrl);
```

Add `using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;` is already present in that file (used by `ObjectiveDetailResponse` etc.) — no new using needed.

In `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`, add this action (place it near `RemoveMember`, following the same `[RequirePermission("projects:access")]` pattern):

```csharp
    [HttpPost("{id:guid}/assets")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> UploadAsset(Guid id, [FromForm] UploadObjectiveAssetRequest request, CancellationToken ct)
    {
        if (request.File is not { Length: > 0 })
            return Problem("A file is required.", statusCode: 400);

        await using var stream = request.File.OpenReadStream();
        var command = new UploadObjectiveAssetCommand(id, request.File.FileName, request.File.ContentType, stream);
        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the necessary `using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;` to the top of `ObjectivesController.cs` if not already covered by an existing wildcard-free using block (add it explicitly).

- [ ] **Step 8: Build to verify the API layer compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: build succeeds

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveAssetResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/UploadObjectiveAsset/ src/ONEVO.Api/Contracts/WorkManagement/Objectives/UploadObjectiveAssetRequest.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveAssetViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/UploadObjectiveAssetCommandHandlerTests.cs
git commit -m "feat: add POST /objectives/{id}/assets upload endpoint"
```

---

### Task 4: `DELETE /api/v1/work/objectives/{id}/assets/{assetId}` — remove an attached asset

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/DeleteObjectiveAssetCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/DeleteObjectiveAssetCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveAssetCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository.GetByIdForTenantAsync`, `.DeleteAsync` (Task 2); `EntityAssetOwnerTypes.Objective` (Task 1).
- Produces: nothing consumed by later tasks (terminal endpoint).

- [ ] **Step 1: Write the failing unit test**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveAssetCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteObjectiveAssetCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid AssetId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DeleteObjectiveAssetCommandHandler BuildHandler()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new DeleteObjectiveAssetCommandHandler(_currentUser.Object, _entityAssets.Object, _unitOfWork.Object);
    }

    private static EntityAsset MatchingAsset() => new()
    {
        Id = AssetId, TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Objective, OwnerId = ObjectiveId,
        AssetPurpose = "objective_asset", FileRecordId = Guid.NewGuid(), CreatedByType = "user",
        CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_MatchingAsset_DeletesAndSaves()
    {
        var handler = BuildHandler();
        var asset = MatchingAsset();
        _entityAssets.Setup(r => r.GetByIdForTenantAsync(TenantId, AssetId, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await handler.Handle(new DeleteObjectiveAssetCommand(ObjectiveId, AssetId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _entityAssets.Verify(r => r.DeleteAsync(asset, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AssetNotFound_ReturnsNotFound()
    {
        var handler = BuildHandler();
        _entityAssets.Setup(r => r.GetByIdForTenantAsync(TenantId, AssetId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);

        var result = await handler.Handle(new DeleteObjectiveAssetCommand(ObjectiveId, AssetId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_AssetBelongsToDifferentObjective_ReturnsNotFound_AndDoesNotDelete()
    {
        var handler = BuildHandler();
        var asset = MatchingAsset();
        _entityAssets.Setup(r => r.GetByIdForTenantAsync(TenantId, AssetId, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await handler.Handle(new DeleteObjectiveAssetCommand(Guid.NewGuid(), AssetId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _entityAssets.Verify(r => r.DeleteAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteObjectiveAssetCommandHandlerTests"`
Expected: FAIL to compile — command/handler don't exist yet.

- [ ] **Step 3: Create the command**

`src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/DeleteObjectiveAssetCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;

public sealed record DeleteObjectiveAssetCommand(Guid ObjectiveId, Guid AssetId) : IRequest<Result>;
```

- [ ] **Step 4: Create the handler**

`src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/DeleteObjectiveAssetCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;

public class DeleteObjectiveAssetCommandHandler : IRequestHandler<DeleteObjectiveAssetCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteObjectiveAssetCommandHandler(
        ICurrentUser currentUser, IEntityAssetRepository entityAssets, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _entityAssets = entityAssets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteObjectiveAssetCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var asset = await _entityAssets.GetByIdForTenantAsync(tenantId, request.AssetId, ct);
        if (asset is null
            || asset.OwnerType != EntityAssetOwnerTypes.Objective
            || asset.OwnerId != request.ObjectiveId)
        {
            return Result.NotFound("Asset not found.");
        }

        await _entityAssets.DeleteAsync(asset, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteObjectiveAssetCommandHandlerTests"`
Expected: PASS (all 3 tests)

- [ ] **Step 6: Wire the HTTP endpoint**

In `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`, add (near `UploadAsset` from Task 3):

```csharp
    [HttpDelete("{id:guid}/assets/{assetId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> DeleteAsset(Guid id, Guid assetId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteObjectiveAssetCommand(id, assetId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;` to the top of the file.

- [ ] **Step 7: Build to verify the API layer compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: build succeeds

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/DeleteObjectiveAsset/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteObjectiveAssetCommandHandlerTests.cs
git commit -m "feat: add DELETE /objectives/{id}/assets/{assetId} endpoint"
```

---

### Task 5: Return attached assets from `GET /api/v1/work/objectives/{id}`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs` (existing file — extend, don't replace)

**Interfaces:**
- Consumes: `IEntityAssetRepository.ListByOwnerAsync` (Task 2), `IFileStorageService.GetSignedUrlAsync` (existing), `ObjectiveAssetResponse` (Task 3).
- Produces: `ObjectiveDetailResponse.Assets : IReadOnlyList<ObjectiveAssetResponse>` — this is the field the frontend reads to populate the Assets section when editing a sub-module.

**Note:** `ObjectiveMapper.ToDetail(...)` is a single method with optional trailing parameters (not two overloads) — `CreateObjectiveCommandHandler` calls it with 1 argument, `GetObjectiveByIdQueryHandler` with 3. Adding one more *optional* trailing parameter keeps `CreateObjectiveCommandHandler`'s call site compiling unchanged (new objectives are created with zero assets, which is correct — assets are attached via a separate call after creation).

- [ ] **Step 1: Write the failing test (extend the existing file)**

In `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs`, update the `BuildHandler` method's signature and body to add the two new dependencies, defaulting to an empty asset list so every existing test keeps passing unchanged:

```csharp
    private (GetObjectiveByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members, Mock<IEntityAssetRepository> EntityAssets) BuildHandler(
        Objective? target, List<string> permissions, bool hasAncestorOrSelfMembership,
        Guid? callerId = null, IReadOnlyDictionary<Guid, string>? names = null,
        IReadOnlyList<EntityAssetWithFile>? assets = null)
    {
        var resolvedCallerId = callerId ?? UserId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, resolvedCallerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(names ?? new Dictionary<Guid, string>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(Parent());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAncestorOrSelfMembership);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var entityAssets = new Mock<IEntityAssetRepository>();
        entityAssets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Objective, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets ?? new List<EntityAssetWithFile>());

        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(x => x.GetSignedUrlAsync(TenantId, It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("https://signed.example/file"));

        var handler = new GetObjectiveByIdQueryHandler(
            currentUser.Object, identity.Object, objectives.Object, members.Object, permissionResolver.Object,
            entityAssets.Object, fileStorage.Object);
        return (handler, members, entityAssets);
    }
```

`BuildHandler` now returns a 3-element tuple instead of 2, so every existing call site in this file that destructures its result must gain a third element. There are two destructuring patterns already in the file — update both:
- `var (handler, members) = BuildHandler(...)` → `var (handler, members, _) = BuildHandler(...)`
- `var (handler, _) = BuildHandler(...)` → `var (handler, _, _) = BuildHandler(...)`

Every existing test's assertions stay unchanged — the extra tuple element is simply discarded (`_`) wherever a test doesn't need to assert on `IEntityAssetRepository` calls.

Add these two new usings at the top of the file:

```csharp
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
```

Add this new test at the end of the class, before the closing brace:

```csharp
    [Fact]
    public async Task Handle_ObjectiveHasAssets_ReturnsThemWithSignedUrls()
    {
        var asset = new EntityAssetWithFile(Guid.NewGuid(), Guid.NewGuid(), "plan.pdf", 2048, "application/pdf", DateTimeOffset.UtcNow);
        var (handler, _, entityAssets) = BuildHandler(Target(), ["projects:read"], hasAncestorOrSelfMembership: false, assets: [asset]);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Assets);
        Assert.Equal("plan.pdf", result.Value.Assets[0].FileName);
        Assert.Equal("https://signed.example/file", result.Value.Assets[0].DownloadUrl);
        entityAssets.Verify(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Objective, ObjectiveId, It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveByIdQueryHandlerTests"`
Expected: FAIL to compile — `ObjectiveDetailResponse.Assets`, the 7-arg constructor, and `EntityAssetWithFile` don't exist yet.

- [ ] **Step 3: Extend `ObjectiveDetailResponse`**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`, append `Assets` as the last positional parameter:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveDetailResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner,
    IReadOnlyList<ObjectiveAssetResponse> Assets);
```

- [ ] **Step 4: Update `ObjectiveMapper.ToDetail`**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`, change the `ToDetail` method signature and body:

```csharp
    public static ObjectiveDetailResponse ToDetail(
        Objective objective, IReadOnlyDictionary<Guid, string>? namesByEmployeeId = null, Guid? callerEmployeeId = null,
        IReadOnlyList<ObjectiveAssetResponse>? assets = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByEmployeeId), ResolveName(objective.ReportingManagerId, namesByEmployeeId),
        callerEmployeeId.HasValue && objective.OwnerId == callerEmployeeId.Value,
        assets ?? Array.Empty<ObjectiveAssetResponse>());
```

(only the signature's last line and the `new(...)` call's last line change — everything else in the method, and the rest of the file, stays exactly as-is)

- [ ] **Step 5: Wire `GetObjectiveByIdQueryHandler` to fetch and pass assets**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`, add two constructor dependencies and the asset-fetching logic:

```csharp
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public class GetObjectiveByIdQueryHandler : IRequestHandler<GetObjectiveByIdQuery, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileStorageService _fileStorage;

    public GetObjectiveByIdQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver,
        IEntityAssetRepository entityAssets, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
        _entityAssets = entityAssets;
        _fileStorage = fileStorage;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ObjectiveDetailResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var parent = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (parent is null)
                    break;

                selfAndAncestorIds.Add(parent.Id);
                cursor = parent;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, callerEmployeeId.Value, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        var nameLookupIds = new List<Guid> { objective.OwnerId };
        if (objective.ReportingManagerId.HasValue)
            nameLookupIds.Add(objective.ReportingManagerId.Value);

        var namesByEmployeeId = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameLookupIds, ct);

        var assetRows = await _entityAssets.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Objective, objective.Id, ct);
        var assets = new List<ObjectiveAssetResponse>(assetRows.Count);
        foreach (var row in assetRows)
        {
            var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, row.FileRecordId, TimeSpan.FromMinutes(15), ct);
            assets.Add(new ObjectiveAssetResponse(
                row.Id, row.OriginalFileName, row.FileSizeBytes, row.ContentType, row.CreatedAt,
                urlResult.IsSuccess ? urlResult.Value! : string.Empty));
        }

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective, namesByEmployeeId, callerEmployeeId.Value, assets));
    }
}
```

- [ ] **Step 6: Update the API view-model layer**

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveDetailViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner,
    IReadOnlyList<ObjectiveAssetViewModel> Assets);
```

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, update the `ObjectiveDetailViewModel` mapper (only this one method changes; leave every other mapper in the file untouched):

```csharp
    public static ObjectiveDetailViewModel ToViewModel(this ObjectiveDetailResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.IsAchieved, dto.AchievedAt, dto.CreatedAt, dto.UpdatedAt,
        dto.OwnerName, dto.ReportingManagerName, dto.IsOwner,
        dto.Assets.Select(a => a.ToViewModel()).ToList());
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveByIdQueryHandlerTests"`
Expected: PASS (all tests in the file, including the new one)

- [ ] **Step 8: Build the full solution to catch any other call sites**

Run: `dotnet build src/ONEVO.Api`
Expected: build succeeds. If it doesn't, the compiler error will name any other file constructing `ObjectiveDetailResponse`/`ObjectiveDetailViewModel` positionally that this task's search missed — fix those call sites the same way (append the new `Assets`/`assets` argument) before proceeding.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveDetailViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs
git commit -m "feat: return attached assets from GET /objectives/{id}"
```

---

### Task 6: Integration test — full upload/list/delete round trip over real HTTP

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Features/WorkManagement/ObjectiveAssetEndpointTests.cs`

**Interfaces:**
- Consumes: the full stack built in Tasks 1–5, exercised only through real HTTP requests (no mocks) against a real PostgreSQL Testcontainer, following `CreateProjectEndpointTests.cs`'s exact fixture pattern (each integration test file is self-contained — this is the established convention, not a shared base class).

- [ ] **Step 1: Write the test file**

Create `tests/ONEVO.Tests.Integration/Features/WorkManagement/ObjectiveAssetEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.E2E;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.Features.WorkManagement;

/// <summary>
/// HTTP integration tests for POST/DELETE /api/v1/work/objectives/{id}/assets, mirroring the
/// fixture pattern in CreateProjectEndpointTests.cs (two fully-provisioned tenants via the
/// admin API + owner invite acceptance + session exchange).
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public class ObjectiveAssetEndpointTests : IAsyncLifetime
{
    private const string AdminHost = "admin.localhost";
    private static readonly Guid SeededPlanId = new("a1b2c3d4-0001-0001-0001-000000000001");

    private readonly CapturingEmailService _email = new();

    private PostgreSqlContainer? _postgres;
    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private E2ETestFactory _factory = null!;
    private HttpClient _client = null!;
    private string _adminCookie = null!;
    private string _adminCsrfToken = null!;

    private TenantSession _tenantA = null!;
    private Guid _tenantACategoryId;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ONEVO_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("onevo_objective_assets_test")
                .WithUsername("test")
                .WithPassword("test")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new E2ETestFactory(connectionString, _email);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

        await WaitForSeedersAsync();

        var loginResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/auth/login",
            new { email = "test_admin@onevo.dev", password = "test_password_123" });
        var adminCookies = ParseSetCookies(loginResponse);
        _adminCsrfToken = adminCookies["admin_csrf"];
        _adminCookie = $"admin_session={adminCookies["admin_session"]}";

        _tenantA = await ProvisionAndLoginOwnerAsync("wm-asset-a", "Work Mgmt Asset Co", "owner-a@wm-asset.test");
        _tenantACategoryId = await SeedProjectCategoryAsync(_tenantA.TenantId, "General");
        await SeedEmployeeForOwnerAsync(_tenantA.TenantId, "owner-a@wm-asset.test");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task UploadListDelete_FullRoundTrip_Succeeds()
    {
        var projectCreated = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Asset Round Trip", "ART1");
        var defaultObjectiveId = (await ReadJsonAsync(projectCreated)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var objectiveCreated = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Design Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 20m);
        var objectiveId = (await ReadJsonAsync(objectiveCreated)).GetProperty("id").GetGuid();

        var uploadResponse = await SendUploadAssetAsync(_tenantA, objectiveId, "plan.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF-1.4 test content"));
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK, await uploadResponse.Content.ReadAsStringAsync());
        var uploadJson = await ReadJsonAsync(uploadResponse);
        var assetId = uploadJson.GetProperty("id").GetGuid();
        uploadJson.GetProperty("fileName").GetString().Should().Be("plan.pdf");
        uploadJson.GetProperty("downloadUrl").GetString().Should().NotBeNullOrEmpty();

        var detailAfterUpload = await SendGetObjectiveAsync(_tenantA, objectiveId);
        var assetsAfterUpload = (await ReadJsonAsync(detailAfterUpload)).GetProperty("assets");
        assetsAfterUpload.GetArrayLength().Should().Be(1);
        assetsAfterUpload[0].GetProperty("id").GetGuid().Should().Be(assetId);

        var deleteResponse = await SendDeleteAssetAsync(_tenantA, objectiveId, assetId);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detailAfterDelete = await SendGetObjectiveAsync(_tenantA, objectiveId);
        (await ReadJsonAsync(detailAfterDelete)).GetProperty("assets").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task UploadAsset_DisallowedExtension_Returns400()
    {
        var projectCreated = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Asset Reject Target", "ARJ1");
        var defaultObjectiveId = (await ReadJsonAsync(projectCreated)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var objectiveCreated = await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Design Phase", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 20m);
        var objectiveId = (await ReadJsonAsync(objectiveCreated)).GetProperty("id").GetGuid();

        var uploadResponse = await SendUploadAssetAsync(_tenantA, objectiveId, "virus.exe", "application/x-msdownload", new byte[] { 1, 2, 3 });

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAsset_NotBelongingToObjective_Returns404()
    {
        var projectCreated = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Asset Isolation Target", "AIT1");
        var defaultObjectiveId = (await ReadJsonAsync(projectCreated)).GetProperty("defaultObjective").GetProperty("id").GetGuid();
        var firstObjective = (await ReadJsonAsync(await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Phase A", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m))).GetProperty("id").GetGuid();
        var secondObjective = (await ReadJsonAsync(await SendCreateObjectiveAsync(_tenantA, defaultObjectiveId, "Phase B", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), 10m))).GetProperty("id").GetGuid();
        var uploadJson = await ReadJsonAsync(await SendUploadAssetAsync(_tenantA, firstObjective, "plan.pdf", "application/pdf", Encoding.UTF8.GetBytes("content")));
        var assetId = uploadJson.GetProperty("id").GetGuid();

        var deleteResponse = await SendDeleteAssetAsync(_tenantA, secondObjective, assetId);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendUploadAssetAsync(TenantSession session, Guid objectiveId, string fileName, string contentType, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "File", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/work/objectives/{objectiveId}/assets") { Content = form };
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendDeleteAssetAsync(TenantSession session, Guid objectiveId, Guid assetId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/objectives/{objectiveId}/assets/{assetId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendGetObjectiveAsync(TenantSession session, Guid objectiveId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/work/objectives/{objectiveId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendCreateProjectAsync(TenantSession session, Guid categoryId, string name, string identifier)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(categoryId.ToString()), "CategoryId" },
            { new StringContent(name), "Name" },
            { new StringContent(identifier), "Identifier" },
            { new StringContent("2026-01-01"), "StartDate" },
            { new StringContent("2026-06-01"), "TargetDate" },
            { new StringContent("2026-06-15"), "ReleaseDate" },
            { new StringContent("40"), "DefaultObjectiveAllocatedHours" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/work/projects") { Content = form };
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendCreateObjectiveAsync(
        TenantSession session, Guid parentObjectiveId, string title, DateOnly startDate, DateOnly endDate, decimal allocatedHours)
    {
        var body = new { parentObjectiveId, title, description = "test description", startDate, endDate, allocatedHours, headUserId = (Guid?)null };
        return await SendJsonAsync(HttpMethod.Post, session.Host, "/api/v1/work/objectives", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private sealed record TenantSession(Guid TenantId, string Host, string SessionCookie, string CsrfHeader);

    private async Task<TenantSession> ProvisionAndLoginOwnerAsync(string slug, string companyName, string ownerEmail)
    {
        const string ownerPassword = "OwnerPass@2026!";
        var host = $"{slug}.localhost";

        var createBody = new
        {
            company_name = companyName,
            slug,
            industry_profile = "technology",
            company_size_range = "11-50",
            legal_entity_name = companyName,
            registration_number = $"PV-{slug}",
            country = "LK",
            timezone = "Asia/Colombo",
            currency = "LKR",
            subscription = new { plan_id = SeededPlanId, billing_cycle = "monthly", commercial_model = "standard" },
            owner_invite = new
            {
                email = ownerEmail,
                first_name = "Test",
                last_name = "Owner",
                completion_methods = new[] { "password" }
            }
        };

        var createResponse = await SendJsonAsync(HttpMethod.Post, AdminHost, "/admin/v1/tenants", createBody,
            cookie: _adminCookie, csrfToken: _adminCsrfToken, idempotencyKey: Guid.NewGuid().ToString());
        var createJson = await ReadJsonAsync(createResponse);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createJson.ToString());
        var tenantId = createJson.GetProperty("tenantId").GetGuid();

        await GrantWorkManagementAccessToOwnerRoleAsync(tenantId);

        var inviteToken = await WaitForInviteTokenForAsync(ownerEmail);
        inviteToken.Should().NotBeNullOrEmpty();

        var acceptResponse = await SendJsonAsync(HttpMethod.Post, host,
            $"/api/v1/auth/invitations/{inviteToken}/accept-password",
            new
            {
                password = ownerPassword,
                confirm_password = ownerPassword,
                acceptances = new[]
                {
                    new { document_type = "terms", version = "1.0", decision = "accepted" },
                    new { document_type = "privacy_notice", version = "1.0", decision = "acknowledged" }
                }
            });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmResponse = await SendJsonAsync(HttpMethod.Patch, AdminHost,
            $"/admin/v1/tenants/{tenantId}/provision/confirm", new { confirm = true },
            cookie: _adminCookie, csrfToken: _adminCsrfToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        const string baseHost = "localhost";
        var loginResponse = await SendJsonAsync(HttpMethod.Post, baseHost, "/api/v1/auth/login",
            new { email = ownerEmail, password = ownerPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var loginJson = await ReadJsonAsync(loginResponse);
        var continueUrl = new Uri(loginJson.GetProperty("continue_url").GetString()!, UriKind.Absolute);
        var exchangeCode = QueryHelpers.ParseQuery(continueUrl.Query)["code"].ToString();

        var exchangeResponse = await SendJsonAsync(HttpMethod.Post, host, "/api/v1/auth/session-exchange", new { code = exchangeCode });
        exchangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = ParseSetCookies(exchangeResponse);

        var sessionCookie = $"onevo_session={cookies["onevo_session"]}; onevo_csrf={cookies["onevo_csrf"]}";
        var csrfHeader = Uri.UnescapeDataString(cookies["onevo_csrf"]);

        return new TenantSession(tenantId, host, sessionCookie, csrfHeader);
    }

    private async Task<string?> WaitForInviteTokenForAsync(string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var template in _email.Templates)
            {
                if (template.TemplateId != "tenant_owner_invite") continue;
                if (!string.Equals(template.To, email, StringComparison.OrdinalIgnoreCase)) continue;
                if (template.Data.TryGetProperty("invite_token", out var token)) return token.GetString();
            }
            await Task.Delay(250);
        }
        return null;
    }

    private async Task WaitForSeedersAsync()
    {
        await using (var migrateScope = _factory.Services.CreateAsyncScope())
        {
            var migrateDb = migrateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await migrateDb.Database.MigrateAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            try
            {
                var permissionsReady = await db.Set<ONEVO.Domain.Features.Auth.Entities.Permission>().AnyAsync();
                var planReady = await db.Set<ONEVO.Domain.Features.SharedPlatform.Entities.SubscriptionPlan>().AnyAsync(p => p.Id == SeededPlanId);
                if (permissionsReady && planReady) return;
            }
            catch { /* Schema not created yet; keep polling. */ }
            await Task.Delay(250);
        }

        throw new TimeoutException("Seeders did not finish within 30s (permissions / subscription plan missing).");
    }

    private async Task<Guid> SeedProjectCategoryAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = new ProjectCategory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name, IsActive = true,
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        db.ProjectCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SeedEmployeeForOwnerAsync(Guid tenantId, string ownerEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == ownerEmail);

        db.Employees.Add(new ONEVO.Domain.Features.CoreHr.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, EmployeeNumber = "OWNER-1",
            FirstName = "Test", LastName = "Owner", Email = ownerEmail,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow), EmploymentStatusId = EmploymentStatusIds.Active,
            CreatedById = user.Id, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task GrantWorkManagementAccessToOwnerRoleAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ownerRole = await db.Roles.SingleAsync(r => r.TenantId == tenantId && r.Name == "Owner");

        var workManagementPermissions = await db.Permissions.Where(p => p.Module == "work_management").ToListAsync();
        var alreadyGrantedIds = (await db.RolePermissions.Where(rp => rp.RoleId == ownerRole.Id).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();

        foreach (var permission in workManagementPermissions)
        {
            if (!alreadyGrantedIds.Contains(permission.Id))
                db.RolePermissions.Add(new ONEVO.Domain.Features.Auth.Entities.RolePermission { TenantId = tenantId, RoleId = ownerRole.Id, PermissionId = permission.Id });
        }

        var subscription = await db.TenantSubscriptions.Where(s => s.TenantId == tenantId).OrderByDescending(s => s.CreatedAt).FirstAsync();
        var modules = JsonSerializer.Deserialize<List<string>>(subscription.SelectedModulesJson) ?? [];
        if (!modules.Contains("work_management"))
        {
            modules.Add("work_management");
            subscription.SelectedModulesJson = JsonSerializer.Serialize(modules);
        }

        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method, string host, string path, object? body,
        string? cookie = null, string? csrfToken = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null) request.Headers.Add("X-CSRF-Token", csrfToken);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, string> ParseSetCookies(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return cookies;
        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var idx = pair.IndexOf('=');
            if (idx > 0) cookies[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }
        return cookies;
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~ObjectiveAssetEndpointTests"`
Expected: PASS (all 3 tests). Requires Docker running (Testcontainers spins up real PostgreSQL) unless `ONEVO_TEST_DB` is set to an existing connection string.

- [ ] **Step 3: Run the full backend test suite as a final check**

Run: `dotnet test tests/ONEVO.Tests.Unit && dotnet test tests/ONEVO.Tests.Integration`
Expected: PASS, no regressions in any other test (particularly `CreateObjectiveCommandHandlerTests` and any other test constructing `ObjectiveDetailResponse`/`ObjectiveDetailViewModel` that this plan's search may have missed).

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/WorkManagement/ObjectiveAssetEndpointTests.cs
git commit -m "test: add integration coverage for objective asset upload/list/delete"
```
