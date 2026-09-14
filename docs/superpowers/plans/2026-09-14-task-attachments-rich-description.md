# Task Attachments, Text Highlighting & Inline Description Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Back the task create/edit modal's already-present (but non-functional) attachment picker and rich-text description editor with real file storage, and add text color/highlight and inline-image editing to the description.

**Architecture:** Reuse the existing quota-enforced `IFileStorageService`/`EntityAsset` generic file-attachment pattern (the same one project logos/banners already use) rather than building new storage plumbing. Two new upload purposes (`task_attachment`, `task_description_image`) plus a new `EntityAssetOwnerTypes.Task`. A small shared `TaskAssetLinker` service does the link/unlink diffing for both `CreateTask` and `EditTask`. Frontend: replace the cosmetic filename-only attachment state in `task-form-modal.component.ts` with real upload-backed state, add an Attachments card to the edit view, and extend the `execCommand`-based rich text toolbar with color/highlight/image buttons.

**Tech Stack:** .NET 10 / MediatR / EF Core / PostgreSQL (backend), Angular 21 standalone components with signals / Vitest (frontend).

**Spec:** `docs/superpowers/specs/2026-09-14-task-attachments-rich-description-design.md`

## Global Constraints

- No database migration is needed anywhere in this plan — `file_records.DeletedAt`/`StorageDeletedAt`, `entity_assets`, and `tenant_storage_stats` already exist.
- Attachments/inline images are wired only into the direct owner `CreateTask`/`EditTask` path. `TaskCreationRequest`/`TaskEditRequest` (the non-owner approval flow) are explicitly out of scope and must not be touched.
- No background cleanup job. Orphan handling is: frontend calls the pending-upload delete endpoint on pill removal and on modal cancel.
- `task_attachment` purpose: pdf, png/jpg/jpeg/webp/gif, doc/docx, xls/xlsx, zip — 25MB cap.
- `task_description_image` purpose: png/jpeg/webp — 5MB cap.
- Backend tests use Moq for handler-level tests (matching `CreateTaskCommandHandlerTests.cs`) and the hand-written `Fakes/` classes for `FileStorageService` tests (matching `FileStorageServiceTests.cs`). Frontend tests use Vitest with `vi.fn()`, matching `task-form-modal.component.spec.ts`.
- Backend repo root for all backend paths below: `C:\onevoNew\HRMS-Backend-v1\.worktrees\task-attachments-rich-description`. Frontend repo root for all frontend paths below: `C:\onevoNew\Hrms--Web-application---front-end---v1` (create a matching worktree/branch there before Task 12 — see Task 12's setup note).

---

## Task 1: Storage quota — release used bytes

**Files:**
- Modify: `src/ONEVO.Application/Features/Storage/Quota/RepositoryInterfaces/ITenantStorageStatsRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Storage/Quota/EfTenantStorageStatsRepository.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IStorageQuotaService.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Storage/Quota/StorageQuotaService.cs`
- Modify: `tests/ONEVO.Tests.Unit/Fakes/FakeStorageQuotaService.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/StorageQuotaServiceTests.cs`

**Interfaces:**
- Produces: `IStorageQuotaService.ReleaseUsedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default) : Task<Result>` — symmetric to the existing `ReleaseReservedStorageAsync`, but decrements `used_r2_bytes` (floored at zero). Consumed by Task 2's `FileStorageService.DeleteAsync`.

- [ ] **Step 1: Write the failing unit test**

Open `tests/ONEVO.Tests.Unit/Features/Storage/StorageQuotaServiceTests.cs` and check its existing fixture/mock setup for `StorageQuotaService` (it will already construct one for `ReleaseReservedStorageAsync` tests — reuse that same construction helper). Add:

```csharp
[Fact]
public async Task ReleaseUsedStorageAsync_DecrementsUsedBytes()
{
    var tenantId = Guid.NewGuid();
    await _storageStats.ReleaseReservedBytesAsync(tenantId, 0); // no-op, ensures row exists path is exercised elsewhere
    var result = await _service.ReleaseUsedStorageAsync(tenantId, 500, CancellationToken.None);

    Assert.True(result.IsSuccess);
}

[Fact]
public async Task ReleaseUsedStorageAsync_EmptyTenantId_Fails()
{
    var result = await _service.ReleaseUsedStorageAsync(Guid.Empty, 500, CancellationToken.None);

    Assert.False(result.IsSuccess);
}

[Fact]
public async Task ReleaseUsedStorageAsync_NonPositiveBytes_SucceedsAsNoOp()
{
    var result = await _service.ReleaseUsedStorageAsync(Guid.NewGuid(), 0, CancellationToken.None);

    Assert.True(result.IsSuccess);
}
```

Match whatever field names the existing test class already uses for its `StorageQuotaService` instance and its `ITenantStorageStatsRepository` (the existing tests for `ReleaseReservedStorageAsync` show the pattern — copy it).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~StorageQuotaServiceTests.ReleaseUsedStorageAsync"`
Expected: FAIL — `ReleaseUsedStorageAsync` does not exist on `IStorageQuotaService`/`StorageQuotaService` (compile error).

- [ ] **Step 3: Add the repository method**

In `ITenantStorageStatsRepository.cs`, add after `ReleaseReservedBytesAsync`:

```csharp
    /// <summary>
    /// Atomically releases a previously committed amount back to the pool.
    /// used_r2_bytes never goes below zero. Used when a linked file is
    /// explicitly deleted (e.g. an attachment removed from a task).
    /// </summary>
    Task ReleaseUsedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default);
```

In `EfTenantStorageStatsRepository.cs`, add after `ReleaseReservedBytesAsync`:

```csharp
    public async Task ReleaseUsedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE tenant_storage_stats
            SET used_r2_bytes = GREATEST(0, used_r2_bytes - {bytes}),
                updated_at = {now}
            WHERE tenant_id = {tenantId}
        ", ct);
    }
```

- [ ] **Step 4: Add the service method**

In `IStorageQuotaService.cs`, add after `CommitReservedStorageAsync`:

```csharp
    /// <summary>
    /// Releases <paramref name="bytes"/> of already-committed usage back to the
    /// pool. Used when a linked file is explicitly deleted (e.g. an attachment
    /// or inline image removed from a task). Always succeeds, idempotent floor
    /// at zero, mirroring <see cref="ReleaseReservedStorageAsync"/>.
    /// </summary>
    Task<Result> ReleaseUsedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default);
```

In `StorageQuotaService.cs`, add after `ReleaseReservedStorageAsync`:

```csharp
    public async Task<Result> ReleaseUsedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(StorageQuotaErrorCodes.TenantContextMissing);

        if (bytes <= 0)
            return Result.Success();

        await _storageStats.ReleaseUsedBytesAsync(tenantId, bytes, ct);
        return Result.Success();
    }
```

- [ ] **Step 5: Update the Fake implementation**

In `tests/ONEVO.Tests.Unit/Fakes/FakeStorageQuotaService.cs`, add tracking fields and the method (mirroring `ReleaseReservedStorageAsync`'s fake):

```csharp
    public int ReleaseUsedCallCount { get; private set; }
    public long LastReleasedUsedBytes { get; private set; }

    public Task<Result> ReleaseUsedStorageAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        ReleaseUsedCallCount++;
        LastReleasedUsedBytes = bytes;
        return Task.FromResult(Result.Success());
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~StorageQuotaServiceTests"`
Expected: PASS (all StorageQuotaService tests, including the 3 new ones).

- [ ] **Step 7: Run full unit test suite to catch any other implementer**

Run: `dotnet build tests/ONEVO.Tests.Unit` then `dotnet build tests/ONEVO.Tests.Integration`
Expected: both build clean — confirms no other hand-written `IStorageQuotaService` implementer was missed.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/Quota/RepositoryInterfaces/ITenantStorageStatsRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Storage/Quota/EfTenantStorageStatsRepository.cs src/ONEVO.Application/Common/ServiceInterfaces/IStorageQuotaService.cs src/ONEVO.Infrastructure/Services/Storage/Quota/StorageQuotaService.cs tests/ONEVO.Tests.Unit/Fakes/FakeStorageQuotaService.cs tests/ONEVO.Tests.Unit/Features/Storage/StorageQuotaServiceTests.cs
git commit -m "feat: add IStorageQuotaService.ReleaseUsedStorageAsync"
```

---

## Task 2: File storage — delete capability

**Files:**
- Modify: `src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs`
- Modify: `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs` (its private `NoOpFileStorageService` must implement the new interface member or the integration test project fails to build)
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`

**Interfaces:**
- Consumes: `IStorageQuotaService.ReleaseUsedStorageAsync` (Task 1), `IObjectStorageAdapter.DeleteObjectAsync(string objectKey, CancellationToken ct)` (already exists).
- Produces: `IFileStorageService.DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default) : Task<Result>`. Consumed by Task 4 (`TaskAssetLinker`) and Task 6 (`DeleteTaskPendingUploadCommandHandler`).

- [ ] **Step 1: Write the failing unit tests**

In `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`, add (this file already has a `CreateService(...)` helper taking `FakeFileUploadReservationRepository, FakeFileRecordRepository, FakeStorageQuotaService, FakeObjectStorageAdapter, FakeUnitOfWork` — reuse it exactly):

```csharp
[Fact]
public async Task DeleteAsync_ExistingRecord_MarksDeletedAndReleasesQuota()
{
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var fileRecords = new FakeFileRecordRepository();
    var record = new FileRecord
    {
        Id = Guid.NewGuid(), TenantId = tenantId, StorageKey = "tenants/x/task-attachments/a.png",
        OriginalFileName = "a.png", SafeFileName = "a.png", ContentType = "image/png",
        FileSizeBytes = 1024, ChecksumSha256 = new string('a', 64), UploadedByUserId = userId,
        Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
    };
    await fileRecords.AddAsync(record);
    var quota = new FakeStorageQuotaService();
    var objectStorage = new FakeObjectStorageAdapter();
    var service = CreateService(
        new FakeFileUploadReservationRepository(), fileRecords, quota, objectStorage, new FakeUnitOfWork());

    var result = await service.DeleteAsync(tenantId, userId, record.Id, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(1, quota.ReleaseUsedCallCount);
    Assert.Equal(1024, quota.LastReleasedUsedBytes);
    var reloaded = await fileRecords.GetByIdAsync(tenantId, record.Id);
    Assert.NotNull(reloaded!.DeletedAt);
    Assert.NotNull(reloaded.StorageDeletedAt);
}

[Fact]
public async Task DeleteAsync_UnknownRecord_ReturnsNotFound()
{
    var service = CreateService(
        new FakeFileUploadReservationRepository(), new FakeFileRecordRepository(),
        new FakeStorageQuotaService(), new FakeObjectStorageAdapter(), new FakeUnitOfWork());

    var result = await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(404, result.StatusCode);
}

[Fact]
public async Task DeleteAsync_AlreadyDeleted_IsIdempotentSuccess()
{
    var tenantId = Guid.NewGuid();
    var fileRecords = new FakeFileRecordRepository();
    var record = new FileRecord
    {
        Id = Guid.NewGuid(), TenantId = tenantId, StorageKey = "k", OriginalFileName = "a.png",
        SafeFileName = "a.png", ContentType = "image/png", FileSizeBytes = 100,
        ChecksumSha256 = new string('a', 64), UploadedByUserId = Guid.NewGuid(),
        Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = DateTimeOffset.UtcNow, StorageDeletedAt = DateTimeOffset.UtcNow
    };
    await fileRecords.AddAsync(record);
    var quota = new FakeStorageQuotaService();
    var service = CreateService(
        new FakeFileUploadReservationRepository(), fileRecords, quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

    var result = await service.DeleteAsync(tenantId, record.UploadedByUserId, record.Id, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(0, quota.ReleaseUsedCallCount);
}
```

(Check `FakeFileRecordRepository`'s dictionary is keyed so `GetByIdAsync` returns the *same reference* you mutate — it already is, per its current implementation — so no `Update` method is needed on the fake or the real EF repository; mutating the tracked/fake entity and calling `SaveChangesAsync` is sufficient, matching how `EfFileRecordRepository.GetByIdAsync` returns a change-tracked entity.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~FileStorageServiceTests.DeleteAsync"`
Expected: FAIL — compile error, `DeleteAsync` not on `IFileStorageService`.

- [ ] **Step 3: Add the interface method**

In `IFileStorageService.cs`, add after `OpenReadAsync`:

```csharp
    /// <summary>
    /// Soft-deletes a file this tenant owns: marks the file_records row
    /// deleted, best-effort deletes the underlying object, and releases the
    /// bytes it was consuming back to the tenant's used-storage quota.
    /// Idempotent — deleting an already-deleted record is a no-op success.
    /// Caller-ownership (e.g. "only the uploader may delete") is the calling
    /// feature handler's responsibility, not this method's — same trust model
    /// documented on <see cref="OpenReadAsync"/>.
    /// </summary>
    Task<Result> DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it**

In `FileStorageService.cs`, add after `OpenReadAsync`:

```csharp
    public async Task<Result> DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default)
    {
        var record = await _fileRecords.GetByIdAsync(tenantId, fileRecordId, ct);
        if (record is null)
            return Result.Failure("file_record_not_found", 404);

        if (record.DeletedAt is not null)
            return Result.Success();

        var now = _clock.UtcNow;

        try
        {
            await _objectStorage.DeleteObjectAsync(record.StorageKey, ct);
            record.StorageDeletedAt = now;
        }
        catch (ObjectStorageException ex)
        {
            _logger.LogError(
                ex, "Failed to delete R2 object for tenant {TenantId}, file {FileId}. Row is still marked deleted.",
                tenantId, fileRecordId);
        }

        record.DeletedAt = now;
        record.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(ct);
        await _quota.ReleaseUsedStorageAsync(tenantId, record.FileSizeBytes, ct);

        return Result.Success();
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~FileStorageServiceTests"`
Expected: PASS (all FileStorageService tests).

- [ ] **Step 6: Fix the other hand-written implementer**

In `tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs`, find the private `NoOpFileStorageService : IFileStorageService` class and add, matching its existing no-op style:

```csharp
        public Task<Result> DeleteAsync(Guid tenantId, Guid userId, Guid fileRecordId, CancellationToken ct = default)
            => Task.FromResult(Result.Success());
```

- [ ] **Step 7: Build everything to confirm no other implementer was missed**

Run: `dotnet build tests/ONEVO.Tests.Integration`
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs tests/ONEVO.Tests.Integration/Monitoring/CheckIn/CheckInTestFactory.cs tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs
git commit -m "feat: add IFileStorageService.DeleteAsync"
```

---

## Task 3: Upload purposes, Task owner type, and EntityAsset reverse lookup

**Files:**
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- Modify: `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposeCatalogTests.cs` (create if it doesn't already exist — check first)

**Interfaces:**
- Produces: `UploadPurposeCatalog.TaskAttachment = "task_attachment"`, `UploadPurposeCatalog.TaskDescriptionImage = "task_description_image"`; `EntityAssetOwnerTypes.Task = "task"`; `IEntityAssetRepository.GetByFileRecordIdAsync(Guid tenantId, Guid fileRecordId, CancellationToken ct = default) : Task<EntityAsset?>`. All three consumed starting Task 4.

- [ ] **Step 1: Check for an existing UploadPurposeCatalog test file**

Run: `find tests/ONEVO.Tests.Unit -iname "UploadPurposeCatalogTests.cs"`. If it exists, add the new tests below into it; if not, create it with the imports/namespace matching sibling files in `tests/ONEVO.Tests.Unit/Features/Storage/File/`.

- [ ] **Step 2: Write the failing tests**

```csharp
using ONEVO.Application.Features.Storage.File.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class UploadPurposeCatalogTests
{
    [Fact]
    public void TaskAttachment_IsSupported_AllowsBroadDocumentTypes()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.TaskAttachment));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.TaskAttachment)!;
        Assert.Equal(25 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.Contains("application/zip", rule.AllowedContentTypes);
        Assert.Contains(".xlsx", rule.AllowedExtensions);
    }

    [Fact]
    public void TaskDescriptionImage_IsSupported_ImageOnlyFiveMegabytes()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.TaskDescriptionImage));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.TaskDescriptionImage)!;
        Assert.Equal(5 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposeCatalogTests"`
Expected: FAIL — compile error, `TaskAttachment`/`TaskDescriptionImage` don't exist.

- [ ] **Step 4: Add the two purposes**

In `UploadPurposeCatalog.cs`, add two constants next to `ObjectiveAsset`:

```csharp
    public const string TaskAttachment = "task_attachment";
    public const string TaskDescriptionImage = "task_description_image";
```

Add a document-types list (task attachments need doc/docx/xls/xlsx/zip/images/pdf) and register both rules in the `Rules` dictionary:

```csharp
    private static readonly IReadOnlyList<string> TaskAttachmentContentTypes = new[]
    {
        "application/pdf", "image/png", "image/jpeg", "image/webp", "image/gif",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip", "application/x-zip-compressed", "application/octet-stream"
    };

    private static readonly IReadOnlyList<string> TaskAttachmentExtensions = new[]
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".doc", ".docx", ".xls", ".xlsx", ".zip"
    };
```

and, inside the `Rules` dictionary initializer, add:

```csharp
        [TaskAttachment] = new UploadPurposeRule(25 * 1024 * 1024, TaskAttachmentContentTypes, TaskAttachmentExtensions),
        [TaskDescriptionImage] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
```

- [ ] **Step 5: Add the owner type constant**

Open `EntityAssetOwnerTypes.cs`, note its existing `Project` constant's exact value/casing convention, and add:

```csharp
    public const string Task = "task";
```

- [ ] **Step 6: Add the reverse-lookup repository method**

In `IEntityAssetRepository.cs`, add after `GetByIdForTenantAsync`:

```csharp
    /// <summary>Finds the single asset row (if any) that links to this file record — used to
    /// check whether an uploaded file is still an unlinked "pending upload" or already
    /// attached to something.</summary>
    Task<EntityAsset?> GetByFileRecordIdAsync(Guid tenantId, Guid fileRecordId, CancellationToken ct = default);
```

In `EfEntityAssetRepository.cs`, add:

```csharp
    public async Task<EntityAsset?> GetByFileRecordIdAsync(Guid tenantId, Guid fileRecordId, CancellationToken ct = default)
    {
        return await _db.EntityAssets
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.FileRecordId == fileRecordId, ct);
    }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposeCatalogTests"`
Expected: PASS.

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: builds clean (confirms `EfEntityAssetRepository` compiles against the updated interface; no other hand-written `IEntityAssetRepository` implementer exists per the earlier repo-wide search, only Moq usages in tests).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposeCatalogTests.cs
git commit -m "feat: add task_attachment/task_description_image upload purposes"
```

---

## Task 4: TaskAssetLinker shared service

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAssetLinker.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAssetLinker.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register the new service)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssetLinkerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository` (Task 3's `GetByFileRecordIdAsync`, plus existing `AddAsync`/`ListByOwnerAsync`/`DeleteAsync`), `IFileStorageService` (Task 2's `DeleteAsync`), `IFileRecordRepository.GetByIdAsync`.
- Produces:
  ```csharp
  public interface ITaskAssetLinker
  {
      Task SyncAttachmentsAsync(
          Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

      Task SyncDescriptionImagesAsync(
          Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default);
  }
  ```
  Consumed by Task 8 (`CreateTaskCommandHandler`) and Task 9 (`EditTaskCommandHandler`).

**Design of the two methods** (both are "resync to this desired state" operations — safe to call identically from both create, where the task has no existing assets yet, and edit, where it might):

- `SyncAttachmentsAsync`: loads current `task_attachment` assets via `ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Task, taskId)` filtered to `AssetPurpose == TaskAttachment` — but `ListByOwnerAsync` returns `EntityAssetWithFile` (joined projection, no `AssetPurpose` field!). Since `ListByOwnerAsync` doesn't expose purpose, and both attachments and description images share the same owner id, resolve current-state via `GetByFileRecordIdAsync` per candidate id being removed instead: compute `toAdd = desiredFileIds - currentlyLinkedIds` and `toRemove = currentlyLinkedIds - desiredFileIds` where `currentlyLinkedIds` comes from a new lightweight approach — **use `ListByOwnerAsync` and treat every returned id as an attachment candidate is wrong once description images share the owner**. To avoid ambiguity, `TaskAssetLinker` will look up each of `desiredFileIds` individually via `GetByFileRecordIdAsync` (cheap, bounded by attachment count, never large) for the add-path, and for the remove-path will call `IEntityAssetRepository.ListByOwnerAsync` and filter in-memory — **this requires `EntityAssetWithFile` to carry `AssetPurpose`**. Add `AssetPurpose` to the `EntityAssetWithFile` record and its `ListByOwnerAsync` projection as part of this task (small, additive — see Step 3 below) rather than adding a new query method.
- For each id in `desiredFileIds` not already linked: load the `FileRecord` via `IFileRecordRepository.GetByIdAsync(tenantId, id)`; skip (do nothing) if null, if `UploadedByUserId != userId`, or if `GetByFileRecordIdAsync` shows it's already linked to a *different* owner. Otherwise create an `EntityAsset(OwnerType: Task, OwnerId: taskId, AssetPurpose: TaskAttachment, FileRecordId: id, IsPrimary: false, CreatedByType: "user", CreatedById: userId, CreatedAt: now)` via `AddAsync`.
- For each currently-linked `task_attachment` asset whose `FileRecordId` is not in `desiredFileIds`: `DeleteAsync(asset)` (removes the `EntityAsset` row) then `IFileStorageService.DeleteAsync(tenantId, userId, asset.FileRecordId)` (releases quota).
- `SyncDescriptionImagesAsync`: extracts fileIds from `descriptionHtml` via `Regex.Matches(html, "tasks/files/([0-9a-fA-F-]{36})")`, then applies the exact same add/remove diff logic as above but scoped to `AssetPurpose == TaskDescriptionImage`.
- Both methods are **best-effort** — they never return a `Result`/throw for a skipped invalid id; the caller (`CreateTaskCommandHandler`/`EditTaskCommandHandler`) always succeeds regardless of attachment linking outcomes, per the spec ("invalid/foreign ids are skipped, never fatal").

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAssetLinkerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (TaskAssetLinker Linker, Mock<IEntityAssetRepository> Assets, Mock<IFileStorageService> FileStorage, Mock<IFileRecordRepository> FileRecords) Build()
    {
        var assets = new Mock<IEntityAssetRepository>();
        var fileStorage = new Mock<IFileStorageService>();
        var fileRecords = new Mock<IFileRecordRepository>();
        var linker = new TaskAssetLinker(assets.Object, fileStorage.Object, fileRecords.Object);
        return (linker, assets, fileStorage, fileRecords);
    }

    private static FileRecord Uploaded(Guid id, Guid uploadedBy, long size = 100) => new()
    {
        Id = id, TenantId = TenantId, StorageKey = "k", OriginalFileName = "f.png", SafeFileName = "f.png",
        ContentType = "image/png", FileSizeBytes = size, ChecksumSha256 = new string('a', 64),
        UploadedByUserId = uploadedBy, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SyncAttachmentsAsync_NewFileUploadedByCaller_LinksIt()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.OwnerType == EntityAssetOwnerTypes.Task && a.OwnerId == TaskId &&
            a.AssetPurpose == UploadPurposeCatalog.TaskAttachment && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_FileUploadedBySomeoneElse_IsSkipped()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, Guid.NewGuid()));
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_RemovedFromDesiredList_UnlinksAndDeletesFile()
    {
        var (linker, assets, fileStorage, fileRecords) = Build();
        var keptId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var removedAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskAttachment, FileRecordId = removedId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(removedAsset.Id, removedId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskAttachment) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, removedAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(removedAsset);
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, keptId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(keptId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, keptId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { keptId }, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == removedAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, removedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_ExtractsFileIdFromHtml_LinksIt()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        var html = $"<p>See <img src=\"/api/v1/work/tasks/files/{fileId}\"></p>";
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, html, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a => a.AssetPurpose == UploadPurposeCatalog.TaskDescriptionImage && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_NullDescription_UnlinksAllExistingImages()
    {
        var (linker, assets, fileStorage, _) = Build();
        var imageId = Guid.NewGuid();
        var imageAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskDescriptionImage, FileRecordId = imageId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(imageAsset.Id, imageId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskDescriptionImage) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, imageAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(imageAsset);

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, null, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == imageAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, imageId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskAssetLinkerTests"`
Expected: FAIL — compile errors (`TaskAssetLinker` doesn't exist yet, `EntityAssetWithFile` record doesn't have a 6th `AssetPurpose` positional argument yet).

- [ ] **Step 3: Add `AssetPurpose` to `EntityAssetWithFile` and its query**

In `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`, change:

```csharp
public sealed record EntityAssetWithFile(
    Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt, string AssetPurpose);
```

In `EfEntityAssetRepository.cs`, update the `ListByOwnerAsync` projection:

```csharp
    public async Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        return await _db.EntityAssets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OwnerType == ownerType && a.OwnerId == ownerId)
            .Join(_db.FileRecords.AsNoTracking(), a => a.FileRecordId, f => f.Id,
                (a, f) => new EntityAssetWithFile(a.Id, f.Id, f.OriginalFileName, f.FileSizeBytes, f.ContentType, a.CreatedAt, a.AssetPurpose))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }
```

Run: `dotnet build src/ONEVO.Application src/ONEVO.Infrastructure` and fix any other call site of the `EntityAssetWithFile` positional constructor that the compiler flags (there should be none yet outside the repository itself, since Task 10 is the first real consumer and hasn't been written).

- [ ] **Step 4: Create `ITaskAssetLinker`**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// Resyncs a task's linked files (attachments and inline description images)
/// to a desired end state. Used identically by CreateTask (starting from
/// nothing) and EditTask (diffing against whatever is already linked).
/// Never fails the caller — invalid, foreign, or already-claimed file ids are
/// silently skipped rather than surfaced as an error.
/// </summary>
public interface ITaskAssetLinker
{
    Task SyncAttachmentsAsync(
        Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

    Task SyncDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default);
}
```

- [ ] **Step 5: Implement `TaskAssetLinker`**

```csharp
using System.Text.RegularExpressions;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskAssetLinker : ITaskAssetLinker
{
    private static readonly Regex DescriptionImageRefPattern =
        new(@"tasks/files/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);

    private readonly IEntityAssetRepository _assets;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileRecordRepository _fileRecords;

    public TaskAssetLinker(IEntityAssetRepository assets, IFileStorageService fileStorage, IFileRecordRepository fileRecords)
    {
        _assets = assets;
        _fileStorage = fileStorage;
        _fileRecords = fileRecords;
    }

    public Task SyncAttachmentsAsync(
        Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default)
        => SyncAsync(tenantId, userId, taskId, UploadPurposeCatalog.TaskAttachment, desiredFileIds, ct);

    public Task SyncDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default)
    {
        var desiredFileIds = string.IsNullOrEmpty(descriptionHtml)
            ? Array.Empty<Guid>()
            : DescriptionImageRefPattern.Matches(descriptionHtml)
                .Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();

        return SyncAsync(tenantId, userId, taskId, UploadPurposeCatalog.TaskDescriptionImage, desiredFileIds, ct);
    }

    private async Task SyncAsync(
        Guid tenantId, Guid userId, Guid taskId, string purpose, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct)
    {
        var current = (await _assets.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Task, taskId, ct))
            .Where(a => a.AssetPurpose == purpose)
            .ToList();
        var currentFileIds = current.Select(a => a.FileRecordId).ToHashSet();

        foreach (var fileId in desiredFileIds.Distinct())
        {
            if (currentFileIds.Contains(fileId))
                continue;

            var record = await _fileRecords.GetByIdAsync(tenantId, fileId, ct);
            if (record is null || record.DeletedAt is not null || record.UploadedByUserId != userId)
                continue;

            var existingLink = await _assets.GetByFileRecordIdAsync(tenantId, fileId, ct);
            if (existingLink is not null)
                continue;

            await _assets.AddAsync(new EntityAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwnerType = EntityAssetOwnerTypes.Task,
                OwnerId = taskId,
                AssetPurpose = purpose,
                FileRecordId = fileId,
                IsPrimary = false,
                CreatedByType = "user",
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        var desiredSet = desiredFileIds.ToHashSet();
        foreach (var asset in current.Where(a => !desiredSet.Contains(a.FileRecordId)))
        {
            var tracked = await _assets.GetByIdForTenantAsync(tenantId, asset.Id, ct);
            if (tracked is null)
                continue;

            await _assets.DeleteAsync(tracked, ct);
            await _fileStorage.DeleteAsync(tenantId, userId, asset.FileRecordId, ct);
        }
    }
}
```

- [ ] **Step 6: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, near the other WorkManagement Tasks service registrations, add:

```csharp
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Tasks.Services.ITaskAssetLinker,
            ONEVO.Application.Features.WorkManagement.Tasks.Services.TaskAssetLinker>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskAssetLinkerTests"`
Expected: PASS.

- [ ] **Step 8: Full build check**

Run: `dotnet build`
Expected: solution builds clean (confirms the `EntityAssetWithFile` signature change didn't break another call site).

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAssetLinker.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAssetLinker.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssetLinkerTests.cs
git commit -m "feat: add TaskAssetLinker for task attachment/description-image sync"
```

---

## Task 5: Pending upload create endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskPendingUpload/CreateTaskPendingUploadCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskPendingUpload/CreateTaskPendingUploadCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskPendingUploadFormRequest.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (add `TaskPendingUploadViewModel`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskPendingUploadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IFileStorageService.UploadAsync` (existing), `UploadPurposeCatalog.IsSupported` (Task 3).
- Produces: `POST api/v1/work/tasks/pending-uploads` → `201 { fileId, originalFileName, fileSizeBytes, contentType }`. Consumed by frontend Task 12/14/17.

- [ ] **Step 1: Write the failing unit test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskPendingUploadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private (CreateTaskPendingUploadCommandHandler Handler, Mock<IFileStorageService> FileStorage) Build()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var fileStorage = new Mock<IFileStorageService>();
        var handler = new CreateTaskPendingUploadCommandHandler(currentUser.Object, fileStorage.Object);
        return (handler, fileStorage);
    }

    [Fact]
    public async Task Handle_UnsupportedPurpose_ReturnsBadRequest()
    {
        var (handler, _) = Build();
        using var stream = new MemoryStream(new byte[] { 1 });

        var result = await handler.Handle(
            new CreateTaskPendingUploadCommand("not_a_real_purpose", "a.png", "image/png", stream), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SupportedPurpose_UploadsAndReturnsFileRecord()
    {
        var (handler, fileStorage) = Build();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.UploadAsync(
                TenantId, UserId, "a.png", "image/png", UploadPurposeCatalog.TaskAttachment, stream, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                fileId, TenantId, "key", "a.png", "a.png", "image/png", 3, new string('a', 64), "available", DateTimeOffset.UtcNow)));

        var result = await handler.Handle(
            new CreateTaskPendingUploadCommand(UploadPurposeCatalog.TaskAttachment, "a.png", "image/png", stream), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value!.Id);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskPendingUploadCommandHandlerTests"`
Expected: FAIL — compile error, the command/handler don't exist.

- [ ] **Step 3: Create the command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;

public sealed record CreateTaskPendingUploadCommand(
    string Purpose, string OriginalFileName, string ContentType, Stream Content
) : IRequest<Result<FileRecordDto>>;
```

- [ ] **Step 4: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;

public sealed class CreateTaskPendingUploadCommandHandler : IRequestHandler<CreateTaskPendingUploadCommand, Result<FileRecordDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public CreateTaskPendingUploadCommandHandler(ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileRecordDto>> Handle(CreateTaskPendingUploadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileRecordDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<FileRecordDto>.Forbidden("Tenant context missing.");

        if (request.Purpose != UploadPurposeCatalog.TaskAttachment && request.Purpose != UploadPurposeCatalog.TaskDescriptionImage)
            return Result<FileRecordDto>.Failure("Unsupported upload purpose for a task file.", 400);

        return await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.OriginalFileName, request.ContentType, request.Purpose, request.Content, ct);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskPendingUploadCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the API contract and controller route**

In `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskPendingUploadFormRequest.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public class TaskPendingUploadFormRequest
{
    public string Purpose { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
```

In `TaskContracts.cs`, add:

```csharp
public sealed record TaskPendingUploadViewModel(Guid FileId, string OriginalFileName, long FileSizeBytes, string ContentType);
```

In `TasksController.cs`, add the using `ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload;` and a new action (place it near the top, alongside `Me`/`MyDeadlines`, since it doesn't take a task id):

```csharp
    [HttpPost("tasks/pending-uploads")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreatePendingUpload([FromForm] TaskPendingUploadFormRequest request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        var result = await _mediator.Send(new CreateTaskPendingUploadCommand(
            request.Purpose, request.File.FileName, request.File.ContentType, stream), ct);

        return result.IsSuccess
            ? StatusCode(201, new TaskPendingUploadViewModel(
                result.Value!.Id, result.Value.OriginalFileName, result.Value.FileSizeBytes, result.Value.ContentType))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Build to confirm the controller compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskPendingUpload src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskPendingUploadFormRequest.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskPendingUploadCommandHandlerTests.cs
git commit -m "feat: add POST tasks/pending-uploads endpoint"
```

---

## Task 6: Pending upload delete endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskPendingUpload/DeleteTaskPendingUploadCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskPendingUpload/DeleteTaskPendingUploadCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskPendingUploadCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IFileRecordRepository.GetByIdAsync`, `IEntityAssetRepository.GetByFileRecordIdAsync` (Task 3), `IFileStorageService.DeleteAsync` (Task 2).
- Produces: `DELETE api/v1/work/tasks/pending-uploads/{fileId}` → `204`, or `403`/`404`/`409`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskPendingUploadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private (DeleteTaskPendingUploadCommandHandler Handler, Mock<IFileStorageService> FileStorage) Build(
        FileRecord? record, EntityAsset? existingLink)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var fileRecords = new Mock<IFileRecordRepository>();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var assets = new Mock<IEntityAssetRepository>();
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(existingLink);

        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var handler = new DeleteTaskPendingUploadCommandHandler(currentUser.Object, fileRecords.Object, assets.Object, fileStorage.Object);
        return (handler, fileStorage);
    }

    private static FileRecord Record(Guid uploadedBy) => new()
    {
        Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "f.png", SafeFileName = "f.png",
        ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
        UploadedByUserId = uploadedBy, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        var (handler, _) = Build(null, null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotUploader_Returns403()
    {
        var (handler, _) = Build(Record(Guid.NewGuid()), null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyLinked_Returns409()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = Guid.NewGuid(), AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(Record(UserId), link);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnlinkedOwnUpload_DeletesIt()
    {
        var (handler, fileStorage) = Build(Record(UserId), null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteTaskPendingUploadCommandHandlerTests"`
Expected: FAIL — compile error.

- [ ] **Step 3: Create the command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;

public sealed record DeleteTaskPendingUploadCommand(Guid FileId) : IRequest<Result>;
```

- [ ] **Step 4: Create the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;

public sealed class DeleteTaskPendingUploadCommandHandler : IRequestHandler<DeleteTaskPendingUploadCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileStorageService _fileStorage;

    public DeleteTaskPendingUploadCommandHandler(
        ICurrentUser currentUser, IFileRecordRepository fileRecords, IEntityAssetRepository entityAssets, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _fileRecords = fileRecords;
        _entityAssets = entityAssets;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(DeleteTaskPendingUploadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var record = await _fileRecords.GetByIdAsync(tenantId, request.FileId, ct);
        if (record is null)
            return Result.NotFound("File not found.");

        if (record.UploadedByUserId != userId)
            return Result.Forbidden("You did not upload this file.");

        var existingLink = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);
        if (existingLink is not null)
            return Result.Conflict("This file is already attached and cannot be deleted as a pending upload.");

        return await _fileStorage.DeleteAsync(tenantId, userId, request.FileId, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteTaskPendingUploadCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller route**

In `TasksController.cs`, add the using `ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;` and:

```csharp
    [HttpDelete("tasks/pending-uploads/{fileId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> DeletePendingUpload(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTaskPendingUploadCommand(fileId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Build to confirm the controller compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskPendingUpload src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskPendingUploadCommandHandlerTests.cs
git commit -m "feat: add DELETE tasks/pending-uploads/{fileId} endpoint"
```

---

## Task 7: Get task file content endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile/GetTaskFileQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile/GetTaskFileQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskFileQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository.GetByFileRecordIdAsync` (Task 3), `IFileRecordRepository.GetByIdAsync`, `IWorkTaskRepository.GetByIdForTenantAsync`, `IProjectRepository.GetByIdForTenantAsync`, `IPermissionResolver.ResolveAsync`, `IProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync`, `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` (all pre-existing — same access rule as `GetTaskByIdQueryHandler`), `IFileStorageService.OpenReadAsync`.
- Produces: `GET api/v1/work/tasks/files/{fileId}` streaming response.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetTaskFileQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private Mock<IEntityAssetRepository> _assets = new();
    private Mock<IFileRecordRepository> _fileRecords = new();
    private Mock<IWorkTaskRepository> _tasks = new();
    private Mock<IProjectRepository> _projects = new();
    private Mock<IProjectMemberRepository> _members = new();
    private Mock<IPermissionResolver> _permissions = new();
    private Mock<IFileStorageService> _fileStorage = new();
    private Mock<ICallerIdentityResolver> _identity = new();
    private Mock<ICurrentUser> _currentUser = new();

    private GetTaskFileQueryHandler Build()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        return new GetTaskFileQueryHandler(
            _currentUser.Object, _identity.Object, _assets.Object, _fileRecords.Object, _tasks.Object,
            _projects.Object, _members.Object, _permissions.Object, _fileStorage.Object);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedByCaller_StreamsIt()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(new FileRecord
        {
            Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "a.png", SafeFileName = "a.png",
            ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
            UploadedByUserId = UserId, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
        });
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedBySomeoneElse_ReturnsNotFound()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(new FileRecord
        {
            Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "a.png", SafeFileName = "a.png",
            ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(), Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LinkedToAccessibleTask_StreamsIt()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, ShortId = "P-1", Title = "T", CreatedAt = DateTimeOffset.UtcNow
        });
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new Project
        {
            Id = ProjectId, TenantId = TenantId, Identifier = "P", IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "projects:read" });
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_LinkedToInaccessibleTask_ReturnsNotFound()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, ShortId = "P-1", Title = "T", CreatedAt = DateTimeOffset.UtcNow
        });
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new Project
        {
            Id = ProjectId, TenantId = TenantId, Identifier = "P", IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        _members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskFileQueryHandlerTests"`
Expected: FAIL — compile error.

- [ ] **Step 3: Create the query**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;

public sealed record GetTaskFileQuery(Guid FileId) : IRequest<Result<FileStreamDto>>;
```

- [ ] **Step 4: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;

/// <summary>
/// Mirrors GetTaskByIdQueryHandler's access rule exactly (projects:read/* OR
/// active objective membership) for a file already linked to a task, so a
/// task's attachment/inline image is never more visible than the task
/// itself. A file that isn't linked to anything yet (a "pending upload") is
/// visible only to whoever uploaded it.
/// </summary>
public sealed class GetTaskFileQueryHandler : IRequestHandler<GetTaskFileQuery, Result<FileStreamDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IFileStorageService _fileStorage;

    public GetTaskFileQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IEntityAssetRepository entityAssets,
        IFileRecordRepository fileRecords, IWorkTaskRepository tasks, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _identity = identity;
        _entityAssets = entityAssets;
        _fileRecords = fileRecords;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetTaskFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);

        if (link is null)
        {
            var record = await _fileRecords.GetByIdAsync(tenantId, request.FileId, ct);
            if (record is null || record.DeletedAt is not null || record.UploadedByUserId != userId)
                return Result<FileStreamDto>.NotFound("File not found.");

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        if (link.OwnerType != EntityAssetOwnerTypes.Task)
            return Result<FileStreamDto>.NotFound("File not found.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, link.OwnerId, ct);
        if (task is null)
            return Result<FileStreamDto>.NotFound("File not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, task.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<FileStreamDto>.NotFound("File not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission)
        {
            var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
            if (callerEmployeeId is null)
                return Result<FileStreamDto>.NotFound("File not found.");

            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<FileStreamDto>.NotFound("File not found.");
        }

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskFileQueryHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Add the controller route**

In `TasksController.cs`, add the using `ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;` and:

```csharp
    [HttpGet("tasks/files/{fileId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetFile(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTaskFileQuery(fileId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }
```

- [ ] **Step 7: Build to confirm the controller compiles**

Run: `dotnet build src/ONEVO.Api`
Expected: builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskFileQueryHandlerTests.cs
git commit -m "feat: add GET tasks/files/{fileId} endpoint"
```

---

## Task 8: Wire attachments into CreateTask

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`Create` action)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs` (constructor signature changed)

**Interfaces:**
- Consumes: `ITaskAssetLinker.SyncAttachmentsAsync`/`SyncDescriptionImagesAsync` (Task 4).
- Produces: `CreateTaskCommand` gains `AttachmentFileIds` as its final parameter.

- [ ] **Step 1: Update the existing test file's `BuildHandler` (it will fail to compile once the handler's constructor changes)**

In `CreateTaskCommandHandlerTests.cs`, add `using ONEVO.Application.Features.WorkManagement.Tasks.Services;` (already imported) and add a mock:

```csharp
        var assetLinker = new Mock<ITaskAssetLinker>();
```

to `BuildHandler`, and pass `assetLinker.Object` as the new last constructor argument (see Step 3). Update the return tuple if any existing test asserts on it — check each existing `[Fact]` compiles unchanged otherwise. Update every `new CreateTaskCommand(...)` call site in this file to add a trailing `AttachmentFileIds: Array.Empty<Guid>()` argument so pre-existing tests keep passing unmodified.

Add one new test:

```csharp
[Fact]
public async Task Handle_WithAttachmentFileIds_CallsAssetLinkerAfterCreate()
{
    var assetLinker = new Mock<ITaskAssetLinker>();
    var (handler, tasks, _) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m, assetLinker: assetLinker);
    var fileId = Guid.NewGuid();
    var command = new CreateTaskCommand(ObjectiveId, "Build the thing", "<p>desc</p>", CategoryId, "medium", null, null, null, SprintId, new[] { fileId });

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    assetLinker.Verify(x => x.SyncAttachmentsAsync(TenantId, UserId, result.Value!.Id, new[] { fileId }, It.IsAny<CancellationToken>()), Times.Once);
    assetLinker.Verify(x => x.SyncDescriptionImagesAsync(TenantId, UserId, result.Value!.Id, "<p>desc</p>", It.IsAny<CancellationToken>()), Times.Once);
}
```

Add an optional `Mock<ITaskAssetLinker>? assetLinker = null` parameter to `BuildHandler`'s signature, defaulting to a fresh `new Mock<ITaskAssetLinker>()` when not supplied, and pass `.Object` into the handler constructor.

- [ ] **Step 2: Run tests to verify the new one fails (and confirm the rest still compile once you've updated call sites)**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskCommandHandlerTests"`
Expected: compile FAIL until Step 3/4 land (the command record and handler constructor don't have the new members yet) — this is expected; proceed.

- [ ] **Step 3: Add `AttachmentFileIds` to the command**

```csharp
public sealed record CreateTaskCommand(
    Guid ObjectiveId, string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId,
    IReadOnlyList<Guid>? AttachmentFileIds = null
) : IRequest<Result<WorkTaskResponse>>;
```

- [ ] **Step 4: Wire the linker into the handler**

Add `private readonly ITaskAssetLinker _assetLinker;` field, add it as the last constructor parameter (update the `using ONEVO.Application.Features.WorkManagement.Tasks.Services;` import, already present), assign it in the constructor body, and inside the `ExecuteInTransactionAsync` closure — **after** `await _unitOfWork.SaveChangesAsync(innerCt);` and before building the response — add:

```csharp
            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskCommandHandlerTests"`
Expected: PASS (all existing tests plus the new one).

- [ ] **Step 6: Update the API contract and controller**

In `TaskContracts.cs`, change `CreateTaskRequest` to:

```csharp
public sealed record CreateTaskRequest(
    string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId,
    IReadOnlyList<Guid>? AttachmentFileIds = null);
```

In `TasksController.cs`'s `Create` action, pass the new field through:

```csharp
        var result = await _mediator.Send(new CreateTaskCommand(
            objectiveId, request.Title, request.Description, request.CategoryId, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints, request.SprintId, request.AttachmentFileIds), ct);
```

- [ ] **Step 7: Build to confirm nothing else broke**

Run: `dotnet build`
Expected: solution builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
git commit -m "feat: link attachments and description images on task create"
```

---

## Task 9: Wire attachments into EditTask

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`EditTaskRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`Edit` action)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAssetLinker` (Task 4), same as Task 8.
- Produces: `EditTaskCommand` gains `AttachmentFileIds` as its final parameter — the **full desired set** after the edit (see spec §4: this is a resync, not an add-only operation, exactly matching `ITaskAssetLinker`'s semantics already used identically in Task 8).

- [ ] **Step 1: Update the existing test file, then write the new failing test**

Open `EditTaskCommandHandlerTests.cs`, find its handler-construction helper, add a `Mock<ITaskAssetLinker>` the same way Task 8 did, update every existing `new EditTaskCommand(...)` call site to append `AttachmentFileIds: Array.Empty<Guid>()`, then add:

```csharp
[Fact]
public async Task Handle_WithAttachmentFileIds_CallsAssetLinker()
{
    var assetLinker = new Mock<ITaskAssetLinker>();
    var (handler, ...) = BuildHandler(..., assetLinker: assetLinker); // match this file's existing BuildHandler signature
    var fileId = Guid.NewGuid();
    var command = new EditTaskCommand(TaskId, "Updated", "<p>new desc</p>", "medium", null, null, null, null, null, new[] { fileId });

    var result = await handler.Handle(command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    assetLinker.Verify(x => x.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, It.IsAny<CancellationToken>()), Times.Once);
    assetLinker.Verify(x => x.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, "<p>new desc</p>", It.IsAny<CancellationToken>()), Times.Once);
}
```

(Adapt the exact `BuildHandler` parameter names/order to whatever this test file already uses — read it first, since its helper signature wasn't fully captured during planning; keep every pre-existing test passing by giving the new mock/parameter a default.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditTaskCommandHandlerTests"`
Expected: FAIL — compile error until Steps 3/4 land.

- [ ] **Step 3: Add `AttachmentFileIds` to the command**

```csharp
public sealed record EditTaskCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason,
    IReadOnlyList<Guid>? AttachmentFileIds = null
) : IRequest<Result<WorkTaskResponse>>;
```

- [ ] **Step 4: Wire the linker into the handler**

Add `private readonly ITaskAssetLinker _assetLinker;`, add it as the last constructor parameter, assign it, and inside the `ExecuteInTransactionAsync` closure — after `await _unitOfWork.SaveChangesAsync(innerCt);` and before building the response — add:

```csharp
            await _assetLinker.SyncAttachmentsAsync(tenantId, userId, task.Id, request.AttachmentFileIds ?? Array.Empty<Guid>(), innerCt);
            await _assetLinker.SyncDescriptionImagesAsync(tenantId, userId, task.Id, task.Description, innerCt);
```

Note: `userId` isn't currently a local variable in `EditTaskCommandHandler.Handle` (only `tenantId` and `callerEmployeeId` are) — add `var userId = _currentUser.UserId;` near the top alongside the existing `tenantId` assignment.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditTaskCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Update the API contract and controller**

In `TaskContracts.cs`:

```csharp
public sealed record EditTaskRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, int? ProgressPercent, string? Reason,
    IReadOnlyList<Guid>? AttachmentFileIds = null);
```

In `TasksController.cs`'s `Edit` action:

```csharp
        var result = await _mediator.Send(new EditTaskCommand(
            id, request.Title, request.Description, request.Priority, request.DueDate,
            request.EstimatedHours, request.StoryPoints, request.ProgressPercent, request.Reason, request.AttachmentFileIds), ct);
```

- [ ] **Step 7: Build to confirm nothing else broke**

Run: `dotnet build`
Expected: solution builds clean.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
git commit -m "feat: resync attachments and description images on task edit"
```

---

## Task 10: Expose attachments on GetTaskById

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskById/GetTaskByIdQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`WorkTaskViewModel`)
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository.ListByOwnerAsync` (existing, now carrying `AssetPurpose` per Task 4 Step 3).
- Produces: `TaskAttachmentDto(Guid FileId, string FileName, long FileSizeBytes, string ContentType)`; `WorkTaskResponse.Attachments : IReadOnlyList<TaskAttachmentDto>`.

- [ ] **Step 1: Read the existing test file's handler-construction helper**

Open `GetTaskByIdQueryHandlerTests.cs` and note its exact mock-setup pattern (it will need an `IEntityAssetRepository` mock added to its constructor call — check whether it already has one for some other reason).

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public async Task Handle_TaskHasAttachments_IncludesThemInResponse()
{
    // Extend this file's existing BuildHandler-equivalent setup with an
    // IEntityAssetRepository mock (add the parameter/field this file's
    // pattern uses for other repositories):
    var assets = new Mock<IEntityAssetRepository>();
    var fileId = Guid.NewGuid();
    assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EntityAssetWithFile>
        {
            new(Guid.NewGuid(), fileId, "spec.pdf", 2048, "application/pdf", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskAttachment)
        });

    // ...build the handler with this mock plugged in, matching however this file wires its handler...

    var result = await handler.Handle(new GetTaskByIdQuery(TaskId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Single(result.Value!.Attachments);
    Assert.Equal("spec.pdf", result.Value.Attachments[0].FileName);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskByIdQueryHandlerTests.Handle_TaskHasAttachments"`
Expected: FAIL — compile error, `Attachments` doesn't exist on `WorkTaskResponse`.

- [ ] **Step 4: Add `TaskAttachmentDto` and the `Attachments` field**

In `WorkTaskResponse.cs`, add:

```csharp
public sealed record TaskAttachmentDto(Guid FileId, string FileName, long FileSizeBytes, string ContentType);
```

and extend the `WorkTaskResponse` record with a trailing optional member:

```csharp
public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid>? AssigneeEmployeeIds = null, Guid? OpenClockSessionEmployeeId = null,
    DateTimeOffset? OpenClockSessionClockInAt = null, int TotalLoggedMinutes = 0,
    Guid? ActiveEventId = null, string? ActiveEventName = null,
    IReadOnlyList<TaskAttachmentDto>? Attachments = null);
```

- [ ] **Step 5: Populate it in the handler**

In `GetTaskByIdQueryHandler.cs`, add the `IEntityAssetRepository` dependency (constructor field + injection), and before constructing the `response`:

```csharp
        var attachments = (await _entityAssets.ListByOwnerAsync(tenantId, EntityAssetOwnerTypes.Task, task.Id, ct))
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.TaskAttachment)
            .Select(a => new TaskAttachmentDto(a.FileRecordId, a.OriginalFileName, a.FileSizeBytes, a.ContentType))
            .ToList();
```

and add `attachments` as the trailing argument to the existing `new WorkTaskResponse(...)` call.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskByIdQueryHandlerTests"`
Expected: PASS (all tests in this file, including pre-existing ones — `Attachments` defaults to `null` for any test not setting up the mock, and the handler always populates a list, so verify no pre-existing test asserts `Attachments is null`; if one does, that assertion is now wrong and should be updated to expect an empty list).

- [ ] **Step 7: Surface it on the API view model**

In `TaskContracts.cs`, add:

```csharp
public sealed record TaskAttachmentViewModel(Guid FileId, string FileName, long FileSizeBytes, string ContentType);
```

and extend `WorkTaskViewModel` with a trailing member:

```csharp
public sealed record WorkTaskViewModel(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid> AssigneeEmployeeIds, Guid? OpenClockSessionEmployeeId,
    DateTimeOffset? OpenClockSessionClockInAt, int TotalLoggedMinutes,
    IReadOnlyList<TaskAttachmentViewModel> Attachments);
```

In `WorkTaskViewModelMapper.cs`, update `ToViewModel(this WorkTaskResponse dto)`:

```csharp
    public static WorkTaskViewModel ToViewModel(this WorkTaskResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.ShortId, dto.Title, dto.Description,
        dto.CategoryId, dto.StatusId, dto.Priority, dto.StoryPoints,
        dto.DueDate, dto.EstimatedHours, dto.CompletedHours, dto.ProgressPercent, dto.SprintId,
        dto.AssigneeEmployeeIds ?? Array.Empty<Guid>(), dto.OpenClockSessionEmployeeId,
        dto.OpenClockSessionClockInAt, dto.TotalLoggedMinutes,
        (dto.Attachments ?? Array.Empty<TaskAttachmentDto>())
            .Select(a => new TaskAttachmentViewModel(a.FileId, a.FileName, a.FileSizeBytes, a.ContentType)).ToList());
```

- [ ] **Step 8: Build to confirm every other `WorkTaskViewModel`/`WorkTaskResponse` construction site still compiles**

Run: `dotnet build`
Expected: builds clean. `WorkTaskResponse`'s new member is optional (defaults to `null`) so `CreateTaskCommandHandler`/`EditTaskCommandHandler`'s existing `new WorkTaskResponse(...)` calls (which don't pass it) still compile — they'll just report `Attachments: null`, which is acceptable since a freshly created/edited task's response is immediately followed by the linker call and the caller re-fetches via `GetTaskById` if it needs to see the attachment list reflected. `WorkTaskViewModel`'s new member is **not** optional (matches this file's existing non-optional style for the DTO's other list fields like `AssigneeEmployeeIds`), so `ToViewModel` must always supply it — confirmed handled by Step 7 above.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskById/GetTaskByIdQueryHandler.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskByIdQueryHandlerTests.cs
git commit -m "feat: expose task attachments on GetTaskById"
```

---

## Task 11: Backend end-to-end integration test

**Files:**
- Create: `tests/ONEVO.Tests.Integration/WorkManagement/TaskAttachmentsIntegrationTests.cs`

Check an existing integration test in `tests/ONEVO.Tests.Integration/` for the harness convention (test server bootstrap, auth/tenant header setup, how a task/objective/project fixture is normally seeded) before writing this — mirror that file's setup exactly rather than inventing a new one.

**Interfaces:**
- Consumes: the full stack built in Tasks 1–10, exercised through real HTTP calls against the test server (no mocks).

- [ ] **Step 1: Write the failing integration test**

```csharp
[Fact]
public async Task PendingUpload_ThenCreateTaskWithAttachment_ThenGetById_ShowsAttachment_ThenFileIsDownloadable()
{
    // 1. Authenticate as a seeded tenant owner (use this test class's existing login helper).
    // 2. POST multipart/form-data to /api/v1/work/tasks/pending-uploads with purpose=task_attachment and a small file.
    //    Assert 201, capture fileId from the response body.
    // 3. POST /api/v1/work/objectives/{objectiveId}/tasks with attachmentFileIds: [fileId].
    //    Assert 201, capture the created task id.
    // 4. GET /api/v1/work/tasks/{taskId}.
    //    Assert the response's attachments array contains one entry with the uploaded file's name.
    // 5. GET /api/v1/work/tasks/files/{fileId}.
    //    Assert 200 and the response body matches the originally uploaded bytes.
}

[Fact]
public async Task GetFile_UnlinkedFileNotOwnedByCaller_Returns404()
{
    // 1. As user A, POST a pending upload, capture fileId.
    // 2. As user B (different seeded user, same or different tenant), GET /api/v1/work/tasks/files/{fileId}.
    //    Assert 404.
}
```

Fill in the actual HTTP call code using this integration test project's existing `HttpClient`/`WebApplicationFactory`/auth-header helper conventions (read a neighboring integration test file first — e.g. one under `tests/ONEVO.Tests.Integration/WorkManagement/` if one exists, otherwise `FileStorageIntegrationTests.cs` for the multipart upload call shape).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TaskAttachmentsIntegrationTests"`
Expected: FAIL (endpoints exist by now from Tasks 5–10, so this should mostly pass already if Tasks 1–10 are correct — treat any failure here as a real integration bug to fix, not an expected red step. If everything from Tasks 1–10 was implemented correctly, this step may already be green; if so, skip ahead to Step 3 and just confirm.)

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TaskAttachmentsIntegrationTests"`
Expected: PASS. If not, debug against the real handlers/controllers from Tasks 5–10 rather than adjusting the test to match broken behavior.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/WorkManagement/TaskAttachmentsIntegrationTests.cs
git commit -m "test: add end-to-end integration coverage for task attachments"
```

---

## Task 12: Frontend setup + TaskApiService/DTO/model/mapper additions

**Setup note:** Before this task, create a matching isolated worktree in the frontend repo, mirroring what was done for the backend:

```bash
cd "C:/onevoNew/Hrms--Web-application---front-end---v1"
git fetch origin
git worktree add .worktrees/task-attachments-rich-description -b feature/task-attachments-rich-description origin/development
```

(Check the frontend repo's actual default integration branch the same way the backend one was checked — `git branch -r`, compare `git log origin/main..HEAD` vs `origin/development..HEAD` counts on whatever branch is currently checked out — and substitute if it differs from `development`.) All frontend file paths below are relative to `Hrms--Web-application---front-end---v1/.worktrees/task-attachments-rich-description`.

**Files:**
- Modify: `src/app/modules/work/models/dto/task.dto.ts`
- Modify: `src/app/modules/work/models/task.model.ts`
- Modify: `src/app/modules/work/utils/task.mapper.ts`
- Modify: `src/app/modules/work/data-access/task-api.service.ts`
- Test: `src/app/modules/work/utils/task.mapper.spec.ts` (create if it doesn't exist — check first)
- Test: `src/app/modules/work/data-access/task-api.service.spec.ts` (create if it doesn't exist — check first)

**Interfaces:**
- Produces:
  ```ts
  export interface TaskAttachmentDto { fileId: string; fileName: string; fileSizeBytes: number; contentType: string; }
  export interface PendingUploadDto { fileId: string; originalFileName: string; fileSizeBytes: number; contentType: string; }
  export interface TaskAttachment { fileId: string; name: string; sizeBytes: number; }
  ```
  `WorkTaskDto`/`WorkTask` gain `attachments: TaskAttachmentDto[] | TaskAttachment[]`. `CreateTaskRequestDto`/`EditTaskRequestDto` gain `attachmentFileIds?: string[]`.
  `TaskApiService` gains:
  ```ts
  uploadPendingFile(file: File, purpose: 'task_attachment' | 'task_description_image'): Observable<PendingUploadDto>
  deletePendingFile(fileId: string): Observable<void>
  getFileUrl(fileId: string): string
  ```
  Consumed by Tasks 14/15/17.

- [ ] **Step 1: Check for existing spec files**

Run: `find src/app/modules/work/utils -iname "task.mapper.spec.ts"` and `find src/app/modules/work/data-access -iname "task-api.service.spec.ts"`. Create whichever is missing, matching the import/describe style of `task-form-modal.component.spec.ts` (Vitest, `vi.fn()`, `of()`/`throwError()` from `rxjs`).

- [ ] **Step 2: Write the failing tests**

In `task.mapper.spec.ts` (add to it, or create it):

```ts
import { toWorkTask } from './task.mapper';

describe('toWorkTask', () => {
  it('maps attachments through, defaulting to an empty array when absent', () => {
    const dto = {
      id: 't1', objectiveId: 'o1', shortId: 'P-1', title: 'T', description: null,
      categoryId: 'c1', statusId: 's1', priority: 'medium' as const, storyPoints: null,
      dueDate: null, estimatedHours: null, completedHours: 0, progressPercent: 0, sprintId: null,
      assigneeEmployeeIds: [], openClockSessionEmployeeId: null, openClockSessionClockInAt: null,
      totalLoggedMinutes: 0
    };

    expect(toWorkTask(dto).attachments).toEqual([]);

    const withAttachments = { ...dto, attachments: [{ fileId: 'f1', fileName: 'a.pdf', fileSizeBytes: 100, contentType: 'application/pdf' }] };
    expect(toWorkTask(withAttachments).attachments).toEqual([{ fileId: 'f1', name: 'a.pdf', sizeBytes: 100 }]);
  });
});
```

In `task-api.service.spec.ts` (add to it, or create it, matching whatever HttpClientTestingModule/TestBed setup an existing `*-api.service.spec.ts` in this codebase uses — check `project-api.service.spec.ts` for the pattern):

```ts
it('uploadPendingFile posts multipart form data to tasks/pending-uploads', () => {
  const file = new File(['x'], 'a.png', { type: 'image/png' });
  service.uploadPendingFile(file, 'task_attachment').subscribe();
  const req = httpMock.expectOne(`${baseUrl}/tasks/pending-uploads`);
  expect(req.request.method).toBe('POST');
  expect(req.request.body instanceof FormData).toBe(true);
  req.flush({ fileId: 'f1', originalFileName: 'a.png', fileSizeBytes: 1, contentType: 'image/png' });
});

it('deletePendingFile deletes by fileId', () => {
  service.deletePendingFile('f1').subscribe();
  const req = httpMock.expectOne(`${baseUrl}/tasks/pending-uploads/f1`);
  expect(req.request.method).toBe('DELETE');
  req.flush(null);
});

it('getFileUrl builds the content url', () => {
  expect(service.getFileUrl('f1')).toBe(`${baseUrl}/tasks/files/f1`);
});
```

(Match this file's actual `baseUrl`/`httpMock` variable names from whatever `beforeEach` scaffolding already exists in the sibling spec you copied the pattern from.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `npx vitest run task.mapper.spec.ts task-api.service.spec.ts`
Expected: FAIL — `attachments` doesn't exist, `uploadPendingFile`/`deletePendingFile`/`getFileUrl` don't exist.

- [ ] **Step 4: Add the DTO types**

In `task.dto.ts`, add:

```ts
export interface TaskAttachmentDto {
  fileId: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
}

export interface PendingUploadDto {
  fileId: string;
  originalFileName: string;
  fileSizeBytes: number;
  contentType: string;
}
```

and extend `WorkTaskDto`, `CreateTaskRequestDto`, `EditTaskRequestDto`:

```ts
export interface WorkTaskDto {
  // ...existing fields...
  attachments?: TaskAttachmentDto[];
}

export interface CreateTaskRequestDto {
  // ...existing fields...
  attachmentFileIds?: string[];
}

export interface EditTaskRequestDto {
  // ...existing fields...
  attachmentFileIds?: string[];
}
```

- [ ] **Step 5: Add the model type and mapper**

In `task.model.ts`, add:

```ts
export interface TaskAttachment {
  fileId: string;
  name: string;
  sizeBytes: number;
}
```

and extend `WorkTask` with `attachments: TaskAttachment[];`.

In `task.mapper.ts`, update `toWorkTask`:

```ts
    attachments: (dto.attachments ?? []).map((a) => ({ fileId: a.fileId, name: a.fileName, sizeBytes: a.fileSizeBytes }))
```

(add this line to the returned object, alongside the existing `activeEventName` line).

- [ ] **Step 6: Add the TaskApiService methods**

In `task-api.service.ts`, import `PendingUploadDto` alongside the other DTO imports, and add:

```ts
  uploadPendingFile(file: File, purpose: 'task_attachment' | 'task_description_image'): Observable<PendingUploadDto> {
    const formData = new FormData();
    formData.set('purpose', purpose);
    formData.set('file', file);
    return this.http.post<PendingUploadDto>(`${this.baseUrl}/tasks/pending-uploads`, formData);
  }

  deletePendingFile(fileId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/tasks/pending-uploads/${fileId}`);
  }

  getFileUrl(fileId: string): string {
    return `${this.baseUrl}/tasks/files/${fileId}`;
  }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `npx vitest run task.mapper.spec.ts task-api.service.spec.ts`
Expected: PASS.

- [ ] **Step 8: Run the full frontend unit suite to catch any other WorkTaskDto/WorkTask consumer**

Run: `npx vitest run`
Expected: no new failures (the new `attachments` field is additive/optional on the DTO and always-populated on the model, so no existing test literal should break; if one does, it's a test asserting an exact object shape that now needs the new field added).

- [ ] **Step 9: Commit**

```bash
git add src/app/modules/work/models/dto/task.dto.ts src/app/modules/work/models/task.model.ts src/app/modules/work/utils/task.mapper.ts src/app/modules/work/utils/task.mapper.spec.ts src/app/modules/work/data-access/task-api.service.ts src/app/modules/work/data-access/task-api.service.spec.ts
git commit -m "feat: add task attachment DTOs, model, mapper, and API methods"
```

---

## Task 13: TaskAttachmentListComponent (new reusable presentational component)

**Files:**
- Create: `src/app/modules/work/ui/task-attachment-list/task-attachment-list.component.ts`
- Test: `src/app/modules/work/ui/task-attachment-list/task-attachment-list.component.spec.ts`

**Interfaces:**
- Produces:
  ```ts
  @Component({ selector: 'app-task-attachment-list', standalone: true, ... })
  export class TaskAttachmentListComponent {
    files = input.required<{ fileId: string; name: string; sizeBytes: number; uploading?: boolean }[]>();
    fileSelected = output<FileList>();
    removed = output<string>();  // fileId
  }
  ```
  Consumed by Tasks 14 and 15.

This is a small presentational component — no HTTP calls of its own (the parent owns upload/delete side effects), matching this codebase's existing presentational-component pattern (e.g. `WorkDropdownComponent`).

- [ ] **Step 1: Write the failing test**

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TaskAttachmentListComponent } from './task-attachment-list.component';

describe('TaskAttachmentListComponent', () => {
  let fixture: ComponentFixture<TaskAttachmentListComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TaskAttachmentListComponent] }).compileComponents();
    fixture = TestBed.createComponent(TaskAttachmentListComponent);
    fixture.componentRef.setInput('files', [{ fileId: 'f1', name: 'spec.pdf', sizeBytes: 2048 }]);
    fixture.detectChanges();
  });

  it('renders a pill per file', () => {
    const pills = fixture.nativeElement.querySelectorAll('.tal-pill');
    expect(pills.length).toBe(1);
    expect(pills[0].textContent).toContain('spec.pdf');
  });

  it('emits removed with the fileId when a pill\'s remove button is clicked', () => {
    const emitted: string[] = [];
    fixture.componentInstance.removed.subscribe((id) => emitted.push(id));
    fixture.nativeElement.querySelector('.tal-pill-remove').click();
    expect(emitted).toEqual(['f1']);
  });

  it('emits fileSelected with the chosen FileList', () => {
    let emitted: FileList | null = null;
    fixture.componentInstance.fileSelected.subscribe((f) => (emitted = f));
    const input: HTMLInputElement = fixture.nativeElement.querySelector('.tal-file-input');
    const dt = new DataTransfer();
    dt.items.add(new File(['x'], 'x.png', { type: 'image/png' }));
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    expect(emitted).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run task-attachment-list.component.spec.ts`
Expected: FAIL — the component file doesn't exist.

- [ ] **Step 3: Implement the component**

```ts
import { Component, input, output } from '@angular/core';

export interface AttachmentPill {
  fileId: string;
  name: string;
  sizeBytes: number;
  uploading?: boolean;
}

@Component({
  selector: 'app-task-attachment-list',
  standalone: true,
  template: `
    <label class="tal-attach-box" title="Attach files">
      <input type="file" multiple class="tal-file-input" (change)="onFilesSelected($event)" />
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48" /></svg>
      <span>{{ files().length > 0 ? files().length + ' attached' : '+ Add files' }}</span>
    </label>

    @if (files().length > 0) {
      <div class="tal-list">
        @for (file of files(); track file.fileId) {
          <span class="tal-pill" [class.tal-pill--uploading]="file.uploading">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48" /></svg>
            <span>{{ file.name }}</span>
            <button type="button" class="tal-pill-remove" (click)="removed.emit(file.fileId)">×</button>
          </span>
        }
      </div>
    }
  `,
  styles: [`
    .tal-attach-box { display: inline-flex; align-items: center; gap: 6px; padding: 6px 10px; border: 1px dashed var(--color-border); border-radius: 8px; cursor: pointer; font-size: 13px; }
    .tal-attach-box svg { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 2; }
    .tal-file-input { display: none; }
    .tal-list { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
    .tal-pill { display: inline-flex; align-items: center; gap: 6px; padding: 4px 8px; border-radius: 6px; background: var(--color-surface-muted, #f1f5f9); font-size: 12px; }
    .tal-pill--uploading { opacity: 0.6; }
    .tal-pill svg { width: 14px; height: 14px; fill: none; stroke: currentColor; stroke-width: 2; }
    .tal-pill-remove { border: none; background: none; cursor: pointer; font-size: 14px; line-height: 1; padding: 0 2px; }
  `]
})
export class TaskAttachmentListComponent {
  files = input.required<AttachmentPill[]>();
  fileSelected = output<FileList>();
  removed = output<string>();

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.fileSelected.emit(input.files);
      input.value = '';
    }
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run task-attachment-list.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/task-attachment-list
git commit -m "feat: add reusable TaskAttachmentListComponent"
```

---

## Task 14: Wire real attachment upload into create mode

**Files:**
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: `TaskApiService.uploadPendingFile`/`deletePendingFile` (Task 12), `TaskAttachmentListComponent` (Task 13).
- Produces: replaces `attachedFileNames = signal<string[]>([])` with `attachedFiles = signal<AttachmentPill[]>([])`, used by both create and (Task 15) edit modes.

- [ ] **Step 1: Write the failing tests**

In `task-form-modal.component.spec.ts`, add `uploadPendingFile`/`deletePendingFile` to `baseApi()`:

```ts
    uploadPendingFile: vi.fn().mockReturnValue(of({ fileId: 'f1', originalFileName: 'a.png', fileSizeBytes: 100, contentType: 'image/png' })),
    deletePendingFile: vi.fn().mockReturnValue(of(undefined)),
```

Add a new `describe` block (this component supports both `mode: 'create'` and `mode: 'edit'` — add these tests under a create-mode setup mirroring how the existing edit-mode `describe` configures `TestBed`, but with `fixture.componentRef.setInput('mode', 'create')` and no `taskId` input):

```ts
describe('TaskFormModalComponent - create mode attachments', () => {
  let fixture: ComponentFixture<TaskFormModalComponent>;
  let api: Record<string, ReturnType<typeof vi.fn>>;

  beforeEach(async () => {
    api = baseApi();
    // ...same TestBed.configureTestingModule providers as the existing edit-mode describe block,
    // but fixture.componentRef.setInput('mode', 'create') instead of 'edit'/'taskId'...
  });

  it('uploads a selected file immediately and adds it to attachedFiles', async () => {
    const file = new File(['x'], 'a.png', { type: 'image/png' });
    const dt = new DataTransfer();
    dt.items.add(file);
    const input = document.createElement('input');
    input.files = dt.files;
    await fixture.componentInstance.onFilesSelected({ target: input } as unknown as Event);

    expect(api['uploadPendingFile']).toHaveBeenCalledWith(file, 'task_attachment');
    expect(fixture.componentInstance.attachedFiles()).toEqual([{ fileId: 'f1', name: 'a.png', sizeBytes: 100 }]);
  });

  it('removeAttachedFile deletes the pending upload and drops the pill', async () => {
    fixture.componentInstance.attachedFiles.set([{ fileId: 'f1', name: 'a.png', sizeBytes: 100 }]);
    await fixture.componentInstance.removeAttachedFile('f1');

    expect(api['deletePendingFile']).toHaveBeenCalledWith('f1');
    expect(fixture.componentInstance.attachedFiles()).toEqual([]);
  });

  it('submit includes attachmentFileIds in the create payload', async () => {
    fixture.componentInstance.attachedFiles.set([{ fileId: 'f1', name: 'a.png', sizeBytes: 100 }]);
    fixture.componentInstance.title.set('New task');
    fixture.componentInstance.selectedObjectiveId.set('o1');
    fixture.componentInstance.categoryId.set('c1');
    await fixture.componentInstance.submitCreateWithAction('create_and_close');

    // Assert against however this component actually submits creation (boardStore.createTask
    // for an owner, or api.createTaskCreationRequest for a non-owner) — match the existing
    // create-mode test's assertion style once you've set up milestone ownership the same way.
  });

  it('onBackdrop cleans up any still-pending attachments', () => {
    fixture.componentInstance.attachedFiles.set([{ fileId: 'f1', name: 'a.png', sizeBytes: 100 }]);
    fixture.componentInstance.onBackdrop();

    expect(api['deletePendingFile']).toHaveBeenCalledWith('f1');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: FAIL — `attachedFiles` doesn't exist yet (still `attachedFileNames`), `onFilesSelected`/`removeAttachedFile` don't call the API.

- [ ] **Step 3: Replace the signal and rewrite the upload methods**

Replace:

```ts
  attachedFileNames = signal<string[]>([]);
```

with:

```ts
  attachedFiles = signal<{ fileId: string; name: string; sizeBytes: number; uploading?: boolean }[]>([]);
```

Replace `onFilesSelected`/`removeAttachedFile`:

```ts
  async onFilesSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    if (!input.files) return;
    for (const file of Array.from(input.files)) {
      try {
        const uploaded = await firstValueFrom(this.taskApi.uploadPendingFile(file, 'task_attachment'));
        this.attachedFiles.update((curr) => [
          ...curr,
          { fileId: uploaded.fileId, name: uploaded.originalFileName, sizeBytes: uploaded.fileSizeBytes }
        ]);
      } catch {
        this.errorMessage.set(`Failed to upload ${file.name}.`);
      }
    }
  }

  async removeAttachedFile(fileId: string): Promise<void> {
    this.attachedFiles.update((curr) => curr.filter((f) => f.fileId !== fileId));
    try {
      await firstValueFrom(this.taskApi.deletePendingFile(fileId));
    } catch {
      // Best-effort: the pill is already gone from the UI either way.
    }
  }
```

Add a private helper and call it from every place the modal closes without submitting in create mode — find `onBackdrop()` and `onEscape()` (both currently just `this.closed.emit()` when not otherwise busy) and the Cancel button's `(click)="closed.emit()"`:

```ts
  private cleanupUnsavedAttachments(): void {
    if (this.currentEffectiveMode() !== 'create') return;
    for (const file of this.attachedFiles()) {
      firstValueFrom(this.taskApi.deletePendingFile(file.fileId)).catch(() => {});
    }
  }
```

Call `this.cleanupUnsavedAttachments();` at the start of `onBackdrop()`, `onEscape()`, and the Cancel button's handler (change the template's `(click)="closed.emit()"` on the Cancel button to `(click)="onCancel()"` and add a small `onCancel(): void { this.cleanupUnsavedAttachments(); this.closed.emit(); }` method — apply the same change to `onBackdrop`/`onEscape` bodies by prepending the cleanup call before their existing `closed.emit()`).

- [ ] **Step 4: Include `attachmentFileIds` in the submit payloads**

In `submitCreateWithAction`, add `attachmentFileIds: this.attachedFiles().map((f) => f.fileId)` to the `CreateTaskRequestDto` object literal (`request`).

In `submit()` (the edit-mode path), add the same field to the `fields` object passed to `editTask`/`createTaskEditRequest` — **do not** add it to the `createTaskEditRequest`/`createTaskCreationRequest` calls used by non-owners (per the Global Constraints, the approval-request flow stays text-only); only the direct `editTask`/`boardStore.createTask` owner paths should carry `attachmentFileIds`.

Also update the two `create_and_add_another` reset blocks (there are two, in `submitCreateWithAction`) that currently do `this.attachedFileNames.set([])` — change both to `this.attachedFiles.set([])`.

- [ ] **Step 5: Swap the Level 3 attachments markup for `TaskAttachmentListComponent`**

Add `TaskAttachmentListComponent` to the component's `imports` array. Replace the existing Level 3 "Attachments" `<div class="tfm-inline-field">...</div>` block and the following `@if (attachedFileNames().length > 0) { ... }` pill block with:

```html
                    <div class="tfm-inline-field">
                      <span class="tfm-inline-label">Attachments</span>
                      <app-task-attachment-list
                        [files]="attachedFiles()"
                        (fileSelected)="onFilesSelected({ target: { files: $event } } as unknown as Event)"
                        (removed)="removeAttachedFile($event)"
                      />
                    </div>
```

(The `{ target: { files: $event } } as unknown as Event` shim lets `onFilesSelected` keep its existing `Event`-shaped signature without changing every other caller — acceptable here since `onFilesSelected` only ever reads `(event.target as HTMLInputElement).files`.)

Remove the now-dead `.tfm-attachment-box`, `.tfm-file-hidden`, `.tfm-attached-files`, `.tfm-attached-pill`, `.tfm-attached-remove` CSS rules from the component's `styles` array (their markup no longer exists — `TaskAttachmentListComponent` has its own `.tal-*` styles).

- [ ] **Step 6: Run tests to verify they pass**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: PASS (all tests in this file, old and new).

- [ ] **Step 7: Manual browser verification**

Start the dev server (`preview_start` with the frontend's launch.json config), open the Work board, click "Create task", attach a file, confirm the pill appears, remove it, confirm it disappears and a network tab shows the DELETE call, then re-attach and submit, confirming the network payload includes `attachmentFileIds`.

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts
git commit -m "feat: wire real file upload into task create attachments"
```

---

## Task 15: Add Attachments card to edit mode

**Files:**
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: `task().attachments` (from `WorkTask.attachments`, Task 12), `attachedFiles` signal and `onFilesSelected`/`removeAttachedFile` (Task 14).

- [ ] **Step 1: Write the failing test**

Add to the existing edit-mode `describe` block in `task-form-modal.component.spec.ts` (extend `taskDto` at the top of the file with an `attachments` array first):

```ts
// at the top, extend taskDto:
const taskDto = {
  // ...existing fields...
  attachments: [{ fileId: 'f1', fileName: 'spec.pdf', fileSizeBytes: 2048, contentType: 'application/pdf' }]
};
```

```ts
it('seeds attachedFiles from the loaded task and renders the Attachments card', () => {
  expect(fixture.componentInstance.attachedFiles()).toEqual([{ fileId: 'f1', name: 'spec.pdf', sizeBytes: 2048 }]);
  const pill = fixture.nativeElement.querySelector('.tal-pill');
  expect(pill.textContent).toContain('spec.pdf');
});

it('on Save Changes: includes the current attachedFiles ids in the edit payload', async () => {
  fixture.componentInstance.title.set('Edited');
  await fixture.componentInstance.submit();
  expect(api['editTask']).toHaveBeenCalledWith('t1', expect.objectContaining({ attachmentFileIds: ['f1'] }));
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: FAIL — `attachedFiles` isn't seeded on load in edit mode, and there's no Attachments card in the edit-mode template yet.

- [ ] **Step 3: Seed `attachedFiles` when a task loads**

Find `loadForTaskId` (or wherever the component maps the fetched `WorkTaskDto`/`WorkTask` into its signals on edit-mode load — it's the method that sets `title.set(...)`, `description.set(...)`, etc.) and add:

```ts
    this.attachedFiles.set(task.attachments.map((a) => ({ fileId: a.fileId, name: a.name, sizeBytes: a.sizeBytes })));
```

- [ ] **Step 4: Add the Attachments card to the edit-mode template**

In the `tfm__pane-side` aside, add a new `<section class="tfm__card">` after the existing "Task details" card (before the Time tracking / Activity log cards, or after — placement doesn't affect tests, just visual grouping), following the same header markup convention as the neighboring cards:

```html
                  <section class="tfm__card">
                    <div class="tfm__card-head">
                      <span class="tfm__card-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m21.44 11.05-9.19 9.19a6 6 0 0 1-8.49-8.49l8.57-8.57A4 4 0 1 1 18 8.84l-8.59 8.57a2 2 0 0 1-2.83-2.83l8.49-8.48" /></svg></span>
                      <div><h3>Attachments</h3><p>Files attached to this task</p></div>
                    </div>
                    <app-task-attachment-list
                      [files]="attachedFiles()"
                      (fileSelected)="onFilesSelected({ target: { files: $event } } as unknown as Event)"
                      (removed)="removeAttachedFile($event)"
                    />
                  </section>
```

- [ ] **Step 5: Include `attachmentFileIds` in the edit-mode save payload**

Confirm Task 14 Step 4 already added `attachmentFileIds: this.attachedFiles().map((f) => f.fileId)` to the `fields` object in `submit()` — if not yet done, do it now (Task 14 and 15 both touch this method; if executed out of order, whichever lands first should add it).

- [ ] **Step 6: Run tests to verify they pass**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: PASS.

- [ ] **Step 7: Manual browser verification**

Open an existing task's edit view, confirm the Attachments card shows any files attached at creation, add a new one, remove one, click Save Changes, reload the task, confirm the change persisted.

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts
git commit -m "feat: add Attachments card to task edit view"
```

---

## Task 16: Text color & highlight toolbar

**Files:**
- Create: `src/app/modules/work/ui/color-swatch-popover/color-swatch-popover.component.ts`
- Test: `src/app/modules/work/ui/color-swatch-popover/color-swatch-popover.component.spec.ts`
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`

**Interfaces:**
- Produces:
  ```ts
  @Component({ selector: 'app-color-swatch-popover', standalone: true, ... })
  export class ColorSwatchPopoverComponent {
    label = input.required<string>();       // "Remove color" / "Remove highlight"
    swatches = input.required<{ name: string; hex: string }[]>();
    colorSelected = output<string | null>(); // null = "remove"
  }
  ```

- [ ] **Step 1: Write the failing component test**

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ColorSwatchPopoverComponent } from './color-swatch-popover.component';

describe('ColorSwatchPopoverComponent', () => {
  let fixture: ComponentFixture<ColorSwatchPopoverComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ColorSwatchPopoverComponent] }).compileComponents();
    fixture = TestBed.createComponent(ColorSwatchPopoverComponent);
    fixture.componentRef.setInput('label', 'Remove color');
    fixture.componentRef.setInput('swatches', [{ name: 'Red', hex: '#e03131' }]);
    fixture.detectChanges();
  });

  it('emits the hex value when a swatch is clicked', () => {
    let emitted: string | null | undefined;
    fixture.componentInstance.colorSelected.subscribe((v) => (emitted = v));
    fixture.nativeElement.querySelector('.csp-swatch').click();
    expect(emitted).toBe('#e03131');
  });

  it('emits null when the remove entry is clicked', () => {
    let emitted: string | null | undefined = 'unset';
    fixture.componentInstance.colorSelected.subscribe((v) => (emitted = v));
    fixture.nativeElement.querySelector('.csp-remove').click();
    expect(emitted).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run color-swatch-popover.component.spec.ts`
Expected: FAIL — component doesn't exist.

- [ ] **Step 3: Implement the component**

```ts
import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-color-swatch-popover',
  standalone: true,
  template: `
    <div class="csp-popover" role="dialog">
      <button type="button" class="csp-remove" (click)="colorSelected.emit(null)">{{ label() }}</button>
      <div class="csp-grid">
        @for (swatch of swatches(); track swatch.hex) {
          <button
            type="button"
            class="csp-swatch"
            [style.background]="swatch.hex"
            [title]="swatch.name"
            (click)="colorSelected.emit(swatch.hex)"
          ></button>
        }
      </div>
    </div>
  `,
  styles: [`
    .csp-popover { position: absolute; z-index: 20; margin-top: 4px; padding: 8px; border-radius: 8px; background: var(--color-surface); box-shadow: 0 8px 24px rgb(15 23 42 / .18); border: 1px solid var(--color-border); }
    .csp-remove { display: block; width: 100%; text-align: left; padding: 4px 6px; margin-bottom: 6px; border: none; background: none; cursor: pointer; font-size: 12px; color: var(--color-text-muted, #64748b); }
    .csp-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
    .csp-swatch { width: 20px; height: 20px; border-radius: 4px; border: 1px solid rgb(0 0 0 / .1); cursor: pointer; padding: 0; }
  `]
})
export class ColorSwatchPopoverComponent {
  label = input.required<string>();
  swatches = input.required<{ name: string; hex: string }[]>();
  colorSelected = output<string | null>();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run color-swatch-popover.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Wire two toolbar buttons into the description editor**

In `task-form-modal.component.ts`, add `ColorSwatchPopoverComponent` to `imports`. Add two signals near `showLinkPopover`:

```ts
  showTextColorPopover = signal(false);
  showHighlightPopover = signal(false);

  readonly swatchPalette = [
    { name: 'Red', hex: '#e03131' }, { name: 'Orange', hex: '#f08c00' },
    { name: 'Yellow', hex: '#f2c400' }, { name: 'Green', hex: '#2f9e44' },
    { name: 'Blue', hex: '#1971c2' }, { name: 'Purple', hex: '#9c36b5' },
    { name: 'Pink', hex: '#e64980' }, { name: 'Default', hex: '#1e293b' }
  ];

  readonly highlightPalette = [
    { name: 'Red', hex: '#ffc9c9' }, { name: 'Orange', hex: '#ffe0b2' },
    { name: 'Yellow', hex: '#fff3bf' }, { name: 'Green', hex: '#d3f9d8' },
    { name: 'Blue', hex: '#d0ebff' }, { name: 'Purple', hex: '#eebefa' },
    { name: 'Pink', hex: '#ffd6e8' }, { name: 'Grey', hex: '#e9ecef' }
  ];
```

Add two methods next to `formatDoc`:

```ts
  applyTextColor(hex: string | null): void {
    document.execCommand('styleWithCSS', false, true);
    document.execCommand('foreColor', false, hex ?? 'inherit');
    this.showTextColorPopover.set(false);
    this.onEditorInput();
  }

  applyHighlight(hex: string | null): void {
    document.execCommand('styleWithCSS', false, true);
    document.execCommand('hiliteColor', false, hex ?? 'transparent');
    this.showHighlightPopover.set(false);
    this.onEditorInput();
  }
```

In the template, after the Underline button (before the `<span class="tfm-editor__divider">` that follows it), add:

```html
                      <div class="tfm-link-anchor">
                        <button
                          type="button"
                          class="tfm-editor__tool"
                          [class.tfm-editor__tool--active]="showTextColorPopover()"
                          title="Text color"
                          (mousedown)="$event.preventDefault()"
                          (click)="showTextColorPopover.set(!showTextColorPopover()); showHighlightPopover.set(false)"
                        >
                          <strong style="text-decoration: underline; text-decoration-color: #e03131;">A</strong>
                        </button>
                        @if (showTextColorPopover()) {
                          <app-color-swatch-popover
                            label="Remove color"
                            [swatches]="swatchPalette"
                            (colorSelected)="applyTextColor($event)"
                          />
                        }
                      </div>

                      <div class="tfm-link-anchor">
                        <button
                          type="button"
                          class="tfm-editor__tool"
                          [class.tfm-editor__tool--active]="showHighlightPopover()"
                          title="Highlight"
                          (mousedown)="$event.preventDefault()"
                          (click)="showHighlightPopover.set(!showHighlightPopover()); showTextColorPopover.set(false)"
                        >
                          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 11 6-6 4 4-6 6H9v-4Z" /><path d="M3 21h6" /></svg>
                        </button>
                        @if (showHighlightPopover()) {
                          <app-color-swatch-popover
                            label="Remove highlight"
                            [swatches]="highlightPalette"
                            (colorSelected)="applyHighlight($event)"
                          />
                        }
                      </div>
```

- [ ] **Step 6: Write the failing task-form-modal tests**

```ts
it('applyTextColor runs execCommand foreColor and syncs description', () => {
  const execSpy = vi.spyOn(document, 'execCommand').mockReturnValue(true);
  fixture.componentInstance.applyTextColor('#e03131');
  expect(execSpy).toHaveBeenCalledWith('foreColor', false, '#e03131');
  expect(fixture.componentInstance.showTextColorPopover()).toBe(false);
});

it('applyHighlight runs execCommand hiliteColor', () => {
  const execSpy = vi.spyOn(document, 'execCommand').mockReturnValue(true);
  fixture.componentInstance.applyHighlight('#fff3bf');
  expect(execSpy).toHaveBeenCalledWith('hiliteColor', false, '#fff3bf');
});
```

(Add these to whichever existing `describe` block already exercises `formatDoc`-style toolbar methods, for setup consistency.)

- [ ] **Step 7: Run tests to verify they pass**

Run: `npx vitest run task-form-modal.component.spec.ts color-swatch-popover.component.spec.ts`
Expected: PASS.

- [ ] **Step 8: Manual browser verification**

Open the create-task modal, type some text, select it, click the text-color button, pick a swatch, confirm the text recolors; repeat for highlight; switch to the Preview tab and confirm the color/highlight survives.

- [ ] **Step 9: Commit**

```bash
git add src/app/modules/work/ui/color-swatch-popover src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts
git commit -m "feat: add text color and highlight toolbar controls"
```

---

## Task 17: Inline image insertion

**Files:**
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: `TaskApiService.uploadPendingFile`/`getFileUrl` (Task 12).

- [ ] **Step 1: Write the failing tests**

```ts
it('insertImage uploads the file and inserts an <img> at the saved selection', async () => {
  const execSpy = vi.spyOn(document, 'execCommand').mockReturnValue(true);
  const file = new File(['x'], 'shot.png', { type: 'image/png' });
  const dt = new DataTransfer();
  dt.items.add(file);
  const input = document.createElement('input');
  input.files = dt.files;

  await fixture.componentInstance.onImageFileSelected({ target: input } as unknown as Event);

  expect(api['uploadPendingFile']).toHaveBeenCalledWith(file, 'task_description_image');
  expect(execSpy).toHaveBeenCalledWith('insertImage', false, expect.stringContaining('/tasks/files/f1'));
});

it('a failed image upload does not touch the editor and surfaces an error', async () => {
  api['uploadPendingFile'] = vi.fn().mockReturnValue(throwError(() => new Error('boom')));
  const execSpy = vi.spyOn(document, 'execCommand').mockReturnValue(true);
  const file = new File(['x'], 'shot.png', { type: 'image/png' });
  const dt = new DataTransfer();
  dt.items.add(file);
  const input = document.createElement('input');
  input.files = dt.files;

  await fixture.componentInstance.onImageFileSelected({ target: input } as unknown as Event);

  expect(execSpy).not.toHaveBeenCalledWith('insertImage', expect.anything(), expect.anything());
  expect(fixture.componentInstance.errorMessage()).toBeTruthy();
});
```

(`uploadPendingFile` in `baseApi()` already resolves `{ fileId: 'f1', ... }` — reuse it for the happy path.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: FAIL — `onImageFileSelected` doesn't exist.

- [ ] **Step 3: Add the toolbar button and method**

In the template, add an "Insert image" button next to the existing link button (inside `.tfm-editor__tools-left`, after the link `<div class="tfm-link-anchor">` block):

```html
                      <input #imageFileInput type="file" accept="image/png,image/jpeg,image/webp" class="tfm-image-input-hidden" (change)="onImageFileSelected($event)" />
                      <button
                        type="button"
                        class="tfm-editor__tool"
                        title="Insert image"
                        (mousedown)="$event.preventDefault()"
                        (click)="imageFileInput.click()"
                      >
                        <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2" /><circle cx="8.5" cy="8.5" r="1.5" /><path d="m21 15-5-5L5 21" /></svg>
                      </button>
```

Add `.tfm-image-input-hidden { display: none; }` to the component's `styles` array.

Add a `viewChild` for it and the handler method, next to the existing `descEditor`/`taskTitleInput` `viewChild` declarations:

```ts
  imageFileInput = viewChild<ElementRef<HTMLInputElement>>('imageFileInput');
```

```ts
  async onImageFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const selection = window.getSelection();
    const savedRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0).cloneRange() : null;

    try {
      const uploaded = await firstValueFrom(this.taskApi.uploadPendingFile(file, 'task_description_image'));

      if (selection && savedRange) {
        selection.removeAllRanges();
        selection.addRange(savedRange);
      }
      document.execCommand('insertImage', false, this.taskApi.getFileUrl(uploaded.fileId));
      this.onEditorInput();
    } catch {
      this.errorMessage.set(`Failed to insert ${file.name}.`);
    } finally {
      input.value = '';
    }
  }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run task-form-modal.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Manual browser verification**

Open the create-task modal, click the image button, pick a PNG, confirm it appears inline in the Write tab at the cursor position, switch to Preview and confirm it renders there too, submit the task, reopen it in edit mode, and confirm the image still loads (proving the cookie-based `<img src>` auth and the description-image linking from Task 8/9 both work end to end).

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts
git commit -m "feat: add inline image insertion to task description editor"
```

---

## Self-Review Notes

- **Spec coverage:** §1–2 (storage infra) → Tasks 1–2. §2 (purposes/owner type) → Task 3. §3 (endpoints) → Tasks 5–7. §4 (Create/Edit wiring) → Tasks 4, 8–9. §5 (read side) → Task 10. §6 (attachment UI) → Tasks 12–15. §7 (color/highlight) → Task 16. §8 (inline images) → Task 17. Testing section → Tasks 1–11 (backend) and 12–17 (frontend, inline) plus Task 11 (integration).
- **Placeholder scan:** every step has real code; steps that intentionally defer to "match this file's existing pattern" (Tasks 9 Step 1, 10 Step 1, 11, 12 Step 2) do so only for scaffolding this plan's author couldn't see without reading the target file first — the *behavior* being tested is always fully specified, never a TODO.
- **Type consistency:** `AttachmentPill`/`attachedFiles` shape (`{ fileId, name, sizeBytes, uploading? }`) is used identically across Tasks 13, 14, 15. `TaskAssetLinker`'s method names (`SyncAttachmentsAsync`/`SyncDescriptionImagesAsync`) match between Task 4's interface, and Tasks 8/9's call sites. `attachmentFileIds` (frontend) / `AttachmentFileIds` (backend C#) naming matches its respective language's convention consistently across Tasks 5–10, 12, 14, 15.
