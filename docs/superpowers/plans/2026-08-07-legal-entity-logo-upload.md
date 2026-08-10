# Legal Entity Logo Upload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the hard-disabled "Upload logo" button in Company General Settings into a working upload/display/remove flow, end to end across the backend (`HRMS-Backend-v1`) and frontend (`Hrms--Web-application---front-end---v1`) repos.

**Architecture:** `PUT /{id}/logo` becomes a direct multipart upload that calls the existing `IFileStorageService.UploadAsync` pipeline (same pattern as `UploadFaceScanCommandHandler`), sidestepping the file-ownership-validation gap that blocked it before. A new `GET /{id}/logo` proxies bytes from Cloudflare R2 via one new, deliberately minimal, read-only `IFileStorageService.OpenReadAsync(tenantId, fileId, ct)` method. The frontend derives the image URL itself from data it already has (`legalEntityId` + `logoFileId`) — no new response fields anywhere.

**Tech Stack:** ASP.NET Core / MediatR / EF Core (Postgres) / xUnit + Moq + FluentAssertions on the backend; Angular 21 (standalone components, `@ngrx/signals` stores) / Karma+Jasmine on the frontend.

**Design doc:** `docs/superpowers/specs/2026-08-07-legal-entity-logo-upload-design.md` (this repo).

## Global Constraints

- Backend commands run from `C:\onevoNew\HRMS-Backend-v1` (no `.sln` — build/test per-project via the `.csproj` paths given in each task).
- Frontend commands run from `C:\onevoNew\Hrms--Web-application---front-end---v1`.
- `UploadPurposeCatalog.CompanyLogo` already enforces 5 MB max, PNG/JPEG/WebP only — do not duplicate that logic elsewhere; the frontend's client-side check is a UX nicety, not the source of truth.
- Never touch `IFileStorageService.UploadAsync`/`BeginReservationAsync`/`CompleteUploadAsync`/`CancelReservationAsync` or the ownership-validation lookup Part 2C deferred — the only interface addition is the new `OpenReadAsync`.
- Every new/changed controller action needs a `[RequirePermission("legal_entity:update")]` attribute — `LegalEntitiesControllerArchitectureTests.EveryAction_HasARequirePermissionAttribute` enforces this.

---

## Task 1: `IFileStorageService.OpenReadAsync` + `FileStreamDto`

**Files:**
- Create: `src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileStreamDto.cs`
- Modify: `src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`
- Modify: `src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs`
- Modify: `tests/ONEVO.Tests.Unit/Fakes/FakeObjectStorageAdapter.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`

**Interfaces:**
- Produces: `FileStreamDto(Stream Content, string ContentType)` in `ONEVO.Application.Features.Storage.File.DTOs.Responses`.
- Produces: `IFileStorageService.OpenReadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) -> Task<Result<FileStreamDto>>`, implemented on `FileStorageService`.
- Produces: `FakeObjectStorageAdapter.ShouldFailGet` (bool, default `false`) — when `true`, `GetObjectAsync` throws `ObjectStorageException`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs`, just above the final closing `}` of the class, and add `using ONEVO.Application.Common.Models;` and `using ONEVO.Domain.Features.Storage.File.Entities;` to the top of the file alongside the existing `using` lines:

```csharp
    [Fact]
    public async Task OpenReadAsync_FileNotFound_ReturnsNotFound()
    {
        var reservations = new FakeFileUploadReservationRepository();
        var quota = new FakeStorageQuotaService();
        var service = CreateService(
            reservations, new FakeFileRecordRepository(), quota, new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task OpenReadAsync_Success_ReturnsStreamAndContentType()
    {
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.PendingScan,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            new FakeObjectStorageAdapter(), new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Value!.ContentType);
        Assert.NotNull(result.Value!.Content);
    }

    [Fact]
    public async Task OpenReadAsync_ObjectStorageFailure_Returns502()
    {
        var tenantId = Guid.NewGuid();
        var fileRecords = new FakeFileRecordRepository();
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            StorageKey = "tenants/logo/photo.png",
            OriginalFileName = "photo.png",
            SafeFileName = "photo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(),
            Status = FileRecordStatus.PendingScan,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await fileRecords.AddAsync(record, CancellationToken.None);
        var objectStorage = new FakeObjectStorageAdapter { ShouldFailGet = true };
        var service = CreateService(
            new FakeFileUploadReservationRepository(), fileRecords, new FakeStorageQuotaService(),
            objectStorage, new FakeUnitOfWork());

        var result = await service.OpenReadAsync(tenantId, record.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.StatusCode);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `C:\onevoNew\HRMS-Backend-v1`): `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: build FAILS — `OpenReadAsync` does not exist on `FileStorageService`/`IFileStorageService`, and `FakeObjectStorageAdapter.ShouldFailGet` does not exist yet.

- [ ] **Step 3: Add `FileStreamDto`**

Create `src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileStreamDto.cs`:

```csharp
namespace ONEVO.Application.Features.Storage.File.DTOs.Responses;

public sealed record FileStreamDto(Stream Content, string ContentType);
```

- [ ] **Step 4: Add `OpenReadAsync` to `IFileStorageService`**

In `src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`, add this member inside the interface, after `UploadAsync` and before the closing `}`:

```csharp

    /// <summary>
    /// Opens a readable stream for a file this tenant already legitimately
    /// owns (e.g. one referenced by a domain entity's own FileId column).
    /// This is not a lookup for validating untrusted, client-supplied file
    /// ids - callers must already know the id is legitimately theirs before
    /// calling it. The tenant filter here is a second, defensive check, not
    /// the primary trust boundary.
    /// </summary>
    Task<Result<FileStreamDto>> OpenReadAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default);
```

- [ ] **Step 5: Add `ShouldFailGet` to `FakeObjectStorageAdapter`**

In `tests/ONEVO.Tests.Unit/Fakes/FakeObjectStorageAdapter.cs`, add a property next to the existing `ShouldFailPut`:

```csharp
    public bool ShouldFailGet { get; set; }
```

Replace the existing `GetObjectAsync` method body:

```csharp
    public Task<Stream> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        if (ShouldFailGet)
        {
            throw new ObjectStorageException("simulated R2 read failure");
        }

        return Task.FromResult<Stream>(new MemoryStream());
    }
```

- [ ] **Step 6: Implement `OpenReadAsync` in `FileStorageService`**

In `src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs`, add this method after `UploadAsync`, before the closing `}` of the class:

```csharp

    public async Task<Result<FileStreamDto>> OpenReadAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default)
    {
        var record = await _fileRecords.GetByIdAsync(tenantId, fileId, ct);
        if (record is null)
        {
            return Result<FileStreamDto>.NotFound("File not found.");
        }

        try
        {
            var stream = await _objectStorage.GetObjectAsync(record.StorageKey, ct);
            return Result<FileStreamDto>.Success(new FileStreamDto(stream, record.ContentType));
        }
        catch (ObjectStorageException ex)
        {
            _logger.LogError(ex, "R2 read failed for tenant {TenantId}, file {FileId}.", tenantId, fileId);
            return Result<FileStreamDto>.Failure("file_read_failed", 502);
        }
    }
```

No new `using` needed — `FileStreamDto`'s namespace is already imported at the top of this file.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~FileStorageServiceTests"`
Expected: PASS, all `FileStorageServiceTests` including the 3 new ones.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileStreamDto.cs src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs src/ONEVO.Infrastructure/Services/Storage/File/FileStorageService.cs tests/ONEVO.Tests.Unit/Fakes/FakeObjectStorageAdapter.cs tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs
git commit -m "feat: add IFileStorageService.OpenReadAsync for tenant-scoped file streaming"
```

---

## Task 2: Delete the dead `IStorageService` interface

**Files:**
- Delete: `src/ONEVO.Application/Common/ServiceInterfaces/IStorageService.cs`

**Interfaces:**
- Consumes: nothing (verified zero consumers during design — see spec).
- Produces: nothing; this is a pure deletion.

- [ ] **Step 1: Confirm there are still no consumers**

Run (from `C:\onevoNew\HRMS-Backend-v1`): `grep -rl "IStorageService" src tests` (Git Bash) or equivalent — confirm the only match is the interface file itself. If anything else now references it, stop and investigate before deleting (do not delete a type something depends on).

- [ ] **Step 2: Delete the file**

```bash
git rm src/ONEVO.Application/Common/ServiceInterfaces/IStorageService.cs
```

- [ ] **Step 3: Verify the solution still builds**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj`
Expected: SUCCESS.

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: remove dead IStorageService interface (zero implementations, zero consumers)"
```

---

## Task 3: `RemoveLegalEntityLogoCommandHandler` — no change needed, add a regression assertion

`DELETE /{id}/logo` already clears `LogoFileId`, which (after Task 1/5/6) becomes the only field the display path depends on — so no code change is required here. This task just makes that guarantee explicit in the test suite so a future change can't silently break it without a failing test.

**Files:**
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `RemoveLegalEntityLogoCommandHandler` (unchanged), `LegalEntity.LogoFileId` (unchanged).

- [ ] **Step 1: Confirm the existing test already covers this**

Open `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs` and re-read `RemoveLogo_ClearsLogoFileId_AndDoesNotTouchOtherFields` (already present, asserts `entity.LogoFileId.Should().BeNull()`). No new test is needed — this step is a verification checkpoint, not a code change.

- [ ] **Step 2: Run the existing test to confirm it still passes untouched**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LegalEntityLogoCommandHandlerTests"`
Expected: PASS (all 4 existing tests, unchanged).

No commit — no files changed in this task. Proceed directly to Task 4.

---

## Task 4: `GetLegalEntityLogoQuery` + `GET /{id}/logo`

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityLogo/GetLegalEntityLogoQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityLogo/GetLegalEntityLogoQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- Modify: `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/GetLegalEntityLogoQueryHandlerTests.cs` (new file)

**Interfaces:**
- Consumes: `IFileStorageService.OpenReadAsync` (Task 1), `ILegalEntityRepository.GetByIdForTenantAsync` (existing), `ICurrentUser` (existing).
- Produces: `GetLegalEntityLogoQuery(Guid LegalEntityId) : IRequest<Result<FileStreamDto>>`; `GetLegalEntityLogoQueryHandler`; controller action `LegalEntitiesController.GetLogo(Guid id, CancellationToken ct)`.

- [ ] **Step 1: Write the failing handler tests**

Create `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/GetLegalEntityLogoQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class GetLegalEntityLogoQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static LegalEntityEntity Entity(Guid id, Guid? logoFileId = null) => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = "Acme Lanka",
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true,
        LogoFileId = logoFileId
    };

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
    }

    [Fact]
    public async Task Handle_NoLogoSet_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid(), logoFileId: null);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EntityNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LogoSet_DelegatesToFileStorageWithTheStoredFileId()
    {
        AuthenticateCurrentUser();
        var fileId = Guid.NewGuid();
        var entity = Entity(Guid.NewGuid(), logoFileId: fileId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        _fileStorage.Verify(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: build FAILS — `GetLegalEntityLogoQuery`/`GetLegalEntityLogoQueryHandler` do not exist yet.

- [ ] **Step 3: Create the query**

Create `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityLogo/GetLegalEntityLogoQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;

public record GetLegalEntityLogoQuery(Guid LegalEntityId) : IRequest<Result<FileStreamDto>>;
```

- [ ] **Step 4: Create the handler**

Create `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityLogo/GetLegalEntityLogoQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;

public class GetLegalEntityLogoQueryHandler
    : IRequestHandler<GetLegalEntityLogoQuery, Result<FileStreamDto>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public GetLegalEntityLogoQueryHandler(
        ILegalEntityRepository legalEntities, ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetLegalEntityLogoQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null || entity.LogoFileId is null)
            return Result<FileStreamDto>.NotFound("Company logo not found.");

        return await _fileStorage.OpenReadAsync(tenantId, entity.LogoFileId.Value, ct);
    }
}
```

- [ ] **Step 5: Add the controller action**

In `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`, add this `using` alongside the others:

```csharp
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;
```

Add this action after `RemoveLogo`, before the closing `}` of the class:

```csharp

    /// <summary>Streams the company's logo image. 404 if no logo is set.</summary>
    [HttpGet("{id:guid}/logo")]
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLegalEntityLogoQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }
```

- [ ] **Step 6: Add an architecture-test assertion for the new route's permission**

In `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`, add this test after `RemoveLogoAction_UsesLegalEntityUpdate`:

```csharp

    [Fact]
    public void GetLogoAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.GetLogo));
        GetPermission(method!).Should().Be("legal_entity:update");
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetLegalEntityLogoQueryHandlerTests"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~LegalEntitiesControllerArchitectureTests"
```
Expected: PASS on both.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/LegalEntity/Queries/GetLegalEntityLogo src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/GetLegalEntityLogoQueryHandlerTests.cs
git commit -m "feat: add GET /legal-entities/{id}/logo to stream the company logo"
```

---

## Task 5: `SetLegalEntityLogoCommand` rework — multipart upload + `PUT /{id}/logo`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommand.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommandValidator.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs`
- Modify: `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`

**Interfaces:**
- Consumes: `IFileStorageService.UploadAsync` (existing, unchanged), `UploadPurposeCatalog.CompanyLogo` (existing, unchanged).
- Produces: `SetLegalEntityLogoCommand(Guid LegalEntityId, Stream Content, string ContentType, string FileName) : IRequest<Result<LegalEntityLogoResponse>>` — replaces the old `(LegalEntityId, FileId)` shape. `LegalEntityLogoResponse` itself is unchanged.

- [ ] **Step 1: Update the failing/changed unit tests first**

In `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs`, replace the whole file with:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class LegalEntityLogoCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static LegalEntityEntity Entity(Guid id, Guid? logoFileId = null) => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = "Acme Lanka",
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true,
        LogoFileId = logoFileId
    };

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
    }

    [Fact]
    public async Task SetLogo_ValidRequest_UploadsAndSetsLogoFileId()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid());
        var originalName = entity.Name;
        var uploadedFileId = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        _fileStorage.Setup(f => f.UploadAsync(
                TenantId, UserId, "logo.png", "image/png", UploadPurposeCatalog.CompanyLogo, content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                uploadedFileId, TenantId, "key", "logo.png", "logo.png", "image/png", 3, new string('a', 64), "PendingScan", DateTimeOffset.UtcNow)));
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(entity.Id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.LogoFileId.Should().Be(uploadedFileId);
        entity.Name.Should().Be(originalName);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLogo_EntityNotFound_ReturnsNotFound_AndNeverUploads()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);
        using var content = new MemoryStream(new byte[] { 1 });

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLogo_UploadRejected_ReturnsUploadFailure_AndDoesNotTouchEntity()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var content = new MemoryStream(new byte[] { 1 });
        _fileStorage.Setup(f => f.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("File exceeds the 5 MB limit for company_logo.", 400));
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(entity.Id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        entity.LogoFileId.Should().BeNull();
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }

    [Fact]
    public async Task RemoveLogo_ClearsLogoFileId_AndDoesNotTouchOtherFields()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid(), Guid.NewGuid());
        var originalName = entity.Name;
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = new RemoveLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new RemoveLegalEntityLogoCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.LogoFileId.Should().BeNull();
        entity.Name.Should().Be(originalName);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveLogo_EntityNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new RemoveLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new RemoveLegalEntityLogoCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: build FAILS — `SetLegalEntityLogoCommand` still has the old `(LegalEntityId, FileId)` shape and `SetLegalEntityLogoCommandHandler` has no `IFileStorageService` dependency yet.

- [ ] **Step 3: Rewrite the command**

Replace `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public record SetLegalEntityLogoCommand(
    Guid LegalEntityId,
    Stream Content,
    string ContentType,
    string FileName
) : IRequest<Result<LegalEntityLogoResponse>>;
```

- [ ] **Step 4: Rewrite the validator**

Replace `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public class SetLegalEntityLogoCommandValidator : AbstractValidator<SetLegalEntityLogoCommand>
{
    public SetLegalEntityLogoCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithMessage("File name is required.");
        RuleFor(x => x.ContentType).NotEmpty().WithMessage("Content type is required.");
    }
}
```

- [ ] **Step 5: Rewrite the handler**

Replace `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo/SetLegalEntityLogoCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;

public class SetLegalEntityLogoCommandHandler
    : IRequestHandler<SetLegalEntityLogoCommand, Result<LegalEntityLogoResponse>>
{
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorageService _fileStorage;

    public SetLegalEntityLogoCommandHandler(
        ILegalEntityRepository legalEntities, ICurrentUser currentUser, IFileStorageService fileStorage)
    {
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<LegalEntityLogoResponse>> Handle(SetLegalEntityLogoCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LegalEntityLogoResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LegalEntityLogoResponse>.Forbidden("Tenant context missing.");

        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (entity is null)
            return Result<LegalEntityLogoResponse>.NotFound("Company not found.");

        var uploadResult = await _fileStorage.UploadAsync(
            tenantId, _currentUser.UserId, request.FileName, request.ContentType,
            UploadPurposeCatalog.CompanyLogo, request.Content, ct);

        if (!uploadResult.IsSuccess)
            return Result<LegalEntityLogoResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

        entity.LogoFileId = uploadResult.Value!.Id;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        _legalEntities.Update(entity);
        await _legalEntities.SaveChangesAsync(ct);

        return Result<LegalEntityLogoResponse>.Success(new LegalEntityLogoResponse(entity.Id, entity.LogoFileId));
    }
}
```

- [ ] **Step 6: Add the controller action and remove the stale "deferred" comment**

In `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`:

Delete this comment block currently sitting above the `[ApiController]` attribute:

```csharp
// PUT /{id}/logo is deliberately not exposed here: SetLegalEntityLogoCommandHandler
// (Part 2B) sets LogoFileId with no tenant-ownership/purpose validation, and
// IFileStorageService has no lookup method that could provide it without becoming
// a Storage-feature change outside this task's scope. See Part 2C report §5.
```

Add this `using` alongside the others:

```csharp
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
```

Add this action after `RemoveLogo` (or after the `GetLogo` action added in Task 4 — order doesn't matter, just keep it inside the class body):

```csharp

    /// <summary>Uploads/replaces the company's logo. Accepts multipart/form-data with a "logo" file field.</summary>
    [HttpPut("{id:guid}/logo")]
    [RequestSizeLimit(6 * 1024 * 1024)] // 6 MB limit (5 MB image + overhead)
    [RequirePermission("legal_entity:update")]
    public async Task<IActionResult> SetLogo(Guid id, IFormFile logo, CancellationToken ct)
    {
        if (logo is null || logo.Length == 0)
            return Problem("logo file is required.", statusCode: 400);

        await using var stream = logo.OpenReadStream();
        var result = await _mediator.Send(
            new SetLegalEntityLogoCommand(id, stream, logo.ContentType, logo.FileName), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Flip the architecture tests that assert the route doesn't exist**

In `tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs`:

Update the class doc comment (currently lines 11-16) to remove the stale "Part 2C deliberately deferred it" claim:

```csharp
/// <summary>
/// Guards the Part 2C controller wiring for Legal Entity / Company General
/// Settings: correct folder ownership, correct permission on every action,
/// no TenantId anywhere in the HTTP surface.
/// </summary>
```

Replace the `NoPutLogoRoute_Exists` test:

```csharp
    [Fact]
    public void PutLogoRoute_Exists_AndUsesPutVerb()
    {
        var setLogo = ControllerType.GetMethod(nameof(LegalEntitiesController.SetLogo));
        var httpPut = setLogo!.GetCustomAttribute<HttpPutAttribute>();

        Assert.NotNull(httpPut);
        Assert.Contains("logo", httpPut!.Template ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetLogoAction_UsesLegalEntityUpdate()
    {
        var method = ControllerType.GetMethod(nameof(LegalEntitiesController.SetLogo));
        GetPermission(method!).Should().Be("legal_entity:update");
    }
```

Update the doc comment on `Controller_DoesNotReferenceFileRecordRepositoryDirectly` (currently says "now that PUT /logo is deferred") to:

```csharp
    [Fact]
    public void Controller_DoesNotReferenceFileRecordRepositoryDirectly()
    {
        // Redundant with the pre-existing FileStorageArchitectureTests guard,
        // but pins the specific expectation for this controller: uploads and
        // reads must always go through IFileStorageService, never a direct
        // file-repository dependency.
        var fields = ControllerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var offenders = fields
            .Where(f => f.FieldType.FullName?.Contains("FileRecordRepository") == true)
            .ToList();

        Assert.Empty(offenders);
    }
```

- [ ] **Step 8: Run all the affected tests**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LegalEntityLogoCommandHandlerTests"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~LegalEntitiesControllerArchitectureTests"
```
Expected: PASS on both.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/SetLegalEntityLogo src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/LegalEntityLogoCommandHandlerTests.cs tests/ONEVO.Tests.Architecture/LegalEntitiesControllerArchitectureTests.cs
git commit -m "feat: expose PUT /legal-entities/{id}/logo as a direct multipart upload"
```

---

## Task 6: Integration tests for the full upload/display/remove flow

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs`

**Interfaces:**
- Consumes: `PUT /api/v1/org/legal-entities/{id}/logo`, `GET /api/v1/org/legal-entities/{id}/logo`, `DELETE /api/v1/org/legal-entities/{id}/logo` (all now real routes as of Tasks 4-5).

- [ ] **Step 1: Add a multipart-capable HTTP helper**

The file's existing `SendAsync` helper (in the `// ── HTTP helpers ──` section near the bottom) only supports a JSON-serializable `body` object (`request.Content = JsonContent.Create(body)`), which can't send a file. Add a sibling helper right after `SendAsync` (around line 820):

```csharp

    private async Task<HttpResponseMessage> SendMultipartAsync(
        HttpMethod method, string host, string path, HttpContent content,
        string? cookie = null, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Host = host;
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-Token", csrfToken);

        return await _client.SendAsync(request);
    }
```

Add `using System.Net.Http.Headers;` to the top of the file alongside the existing `using System.Net.Http.Json;`.

- [ ] **Step 2: Replace the stale "route doesn't exist" test and add real coverage**

Find the `// ── 7. Logo ──` section (around line 400) and replace the `SetLogo_RouteDoesNotExist_ByDesign` test (around line 419) with:

```csharp
    [Fact]
    public async Task SetLogo_ValidImage_UploadsAndReturnsFileId()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo Upload Co", "LOGOU1", "REG-LOGOU1");

        using var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG magic bytes + padding
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "logo", "logo.png");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("logoFileId").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetLogo_OversizedFile_IsRejected()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo Oversize Co", "LOGOU2", "REG-LOGOU2");

        using var content = new MultipartFormDataContent();
        var bytes = new byte[6 * 1024 * 1024]; // over the 5 MB company_logo purpose limit
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "logo", "big.png");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetLogo_WrongContentType_IsRejected()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo WrongType Co", "LOGOU3", "REG-LOGOU3");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("not an image"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "logo", "notes.txt");

        var response = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", content,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogo_NoLogoSet_Returns404()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo NoneSet Co", "LOGOU4", "REG-LOGOU4");

        var response = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLogo_AfterUpload_ReturnsImageBytes()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo RoundTrip Co", "LOGOU5", "REG-LOGOU5");

        using var uploadContent = new MultipartFormDataContent();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        uploadContent.Add(fileContent, "logo", "logo.png");
        var uploadResponse = await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", uploadContent,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK, await uploadResponse.Content.ReadAsStringAsync());

        var getResponse = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var returnedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        returnedBytes.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task RemoveLogo_ThenGetLogo_Returns404Again()
    {
        var company = await CreateCompanyAsync(_tenantA, "Logo RemoveThenGet Co", "LOGOU6", "REG-LOGOU6");

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        uploadContent.Add(fileContent, "logo", "logo.png");
        await SendMultipartAsync(HttpMethod.Put, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", uploadContent,
            cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        await SendAsync(HttpMethod.Delete, _tenantA.Host, $"/api/v1/org/legal-entities/{company.Id}/logo",
            body: null, cookie: _tenantA.SessionCookie, csrfToken: _tenantA.CsrfHeader);

        var getResponse = await SendAsync(HttpMethod.Get, _tenantA.Host,
            $"/api/v1/org/legal-entities/{company.Id}/logo", body: null, cookie: _tenantA.SessionCookie);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
```

- [ ] **Step 3: Run the integration tests**

Run (needs Docker for Testcontainers, or set `ONEVO_TEST_DB` to a local Postgres connection string first):
```bash
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests"
```
Expected: PASS, all tests in the file including the 6 new/replaced ones.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/OrgStructure/LegalEntity/LegalEntitiesIntegrationTests.cs
git commit -m "test: cover the full logo upload/display/remove HTTP flow"
```

---

## Task 7: Frontend — `LegalEntityApiService` logo methods

**Files:**
- Modify: `src/app/modules/organization/data-access/legal-entity-api.service.ts`
- Test: `src/app/modules/organization/data-access/legal-entity-api.service.spec.ts`

**Interfaces:**
- Produces: `LegalEntityApiService.uploadLogo(legalEntityId: string, file: File): Observable<{ legalEntityId: string; logoFileId: string | null }>`; `.removeLogo(legalEntityId: string): Observable<void>`; `.getLogoUrl(legalEntityId: string, logoFileId: string): string` (pure, synchronous).

- [ ] **Step 1: Write the failing tests**

Add to `src/app/modules/organization/data-access/legal-entity-api.service.spec.ts`, inside the existing `describe` block, after the last `it(...)`:

```typescript
  it('uploads a logo as multipart form data', () => {
    const file = new File(['fake image bytes'], 'logo.png', { type: 'image/png' });

    service.uploadLogo('company-1', file).subscribe((result) => {
      expect(result).toEqual({ legalEntityId: 'company-1', logoFileId: 'file-1' });
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-1/logo`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('logo')).toBe(file);
    req.flush({ legalEntityId: 'company-1', logoFileId: 'file-1' });
  });

  it('removes a logo with a DELETE', () => {
    service.removeLogo('company-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-1/logo`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('builds the logo display URL with a cache-busting query param', () => {
    const url = service.getLogoUrl('company-1', 'file-1');

    expect(url).toBe(`${environment.apiUrl}/org/legal-entities/company-1/logo?v=file-1`);
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `C:\onevoNew\Hrms--Web-application---front-end---v1`):
```bash
npm test -- --watch=false --include="**/legal-entity-api.service.spec.ts"
```
Expected: FAIL — `uploadLogo`/`removeLogo`/`getLogoUrl` do not exist on `LegalEntityApiService` yet.

- [ ] **Step 3: Implement the service methods**

In `src/app/modules/organization/data-access/legal-entity-api.service.ts`, add these methods inside the `LegalEntityApiService` class, after `updateGeneralSettings`:

```typescript

  uploadLogo(legalEntityId: string, file: File): Observable<{ legalEntityId: string; logoFileId: string | null }> {
    const formData = new FormData();
    formData.append('logo', file);
    return this.http.put<{ legalEntityId: string; logoFileId: string | null }>(
      `${environment.apiUrl}/org/legal-entities/${legalEntityId}/logo`,
      formData
    );
  }

  removeLogo(legalEntityId: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiUrl}/org/legal-entities/${legalEntityId}/logo`);
  }

  /** Pure helper - no request. `?v=` busts the browser's image cache after a re-upload. */
  getLogoUrl(legalEntityId: string, logoFileId: string): string {
    return `${environment.apiUrl}/org/legal-entities/${legalEntityId}/logo?v=${logoFileId}`;
  }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- --watch=false --include="**/legal-entity-api.service.spec.ts"`
Expected: PASS, all tests in the file.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/organization/data-access/legal-entity-api.service.ts src/app/modules/organization/data-access/legal-entity-api.service.spec.ts
git commit -m "feat: add logo upload/remove/display-URL methods to LegalEntityApiService"
```

---

## Task 8: Frontend — `GeneralSettingsStore` logo state

**Files:**
- Modify: `src/app/modules/organization/state/general-settings.store.ts`
- Test: `src/app/modules/organization/state/general-settings.store.spec.ts`

**Interfaces:**
- Consumes: `LegalEntityApiService.uploadLogo`/`.removeLogo` (Task 7).
- Produces: `GeneralSettingsStore.uploadingLogo(): boolean`; `.removingLogo(): boolean`; `.uploadLogo(legalEntityId: string, file: File): Promise<boolean>`; `.removeLogo(legalEntityId: string): Promise<boolean>`.

- [ ] **Step 1: Write the failing tests**

Add to `src/app/modules/organization/state/general-settings.store.spec.ts`, inside the `describe` block, after the last `it(...)`:

```typescript
  it('uploadLogo() PUTs multipart and patches logoFileId on success', async () => {
    const loadPromise = store.load('company-a');
    httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/general-settings`).flush(settingsA);
    await loadPromise;

    const file = new File(['bytes'], 'logo.png', { type: 'image/png' });
    const uploadPromise = store.uploadLogo('company-a', file);

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`);
    expect(req.request.method).toBe('PUT');
    req.flush({ legalEntityId: 'company-a', logoFileId: 'file-1' });

    const ok = await uploadPromise;
    expect(ok).toBe(true);
    expect(store.settings()?.logoFileId).toBe('file-1');
    expect(store.uploadingLogo()).toBe(false);
  });

  it('uploadLogo() sets saveError and keeps prior settings on failure', async () => {
    const loadPromise = store.load('company-a');
    httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/general-settings`).flush(settingsA);
    await loadPromise;

    const file = new File(['bytes'], 'logo.png', { type: 'image/png' });
    const uploadPromise = store.uploadLogo('company-a', file);

    httpMock
      .expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`)
      .flush({ detail: 'File exceeds the 5 MB limit.' }, { status: 400, statusText: 'Bad Request' });

    const ok = await uploadPromise;
    expect(ok).toBe(false);
    expect(store.saveError()).toBe('File exceeds the 5 MB limit.');
    expect(store.settings()).toEqual(settingsA);
    expect(store.uploadingLogo()).toBe(false);
  });

  it('removeLogo() DELETEs and clears logoFileId on success', async () => {
    const loadPromise = store.load('company-a');
    httpMock
      .expectOne(`${environment.apiUrl}/org/legal-entities/company-a/general-settings`)
      .flush({ ...settingsA, logoFileId: 'file-1' });
    await loadPromise;

    const removePromise = store.removeLogo('company-a');

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    const ok = await removePromise;
    expect(ok).toBe(true);
    expect(store.settings()?.logoFileId).toBeNull();
    expect(store.removingLogo()).toBe(false);
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- --watch=false --include="**/general-settings.store.spec.ts"`
Expected: FAIL — `store.uploadLogo`/`store.removeLogo`/`store.uploadingLogo`/`store.removingLogo` do not exist yet.

- [ ] **Step 3: Implement the store changes**

In `src/app/modules/organization/state/general-settings.store.ts`, update the `GeneralSettingsState` interface:

```typescript
export interface GeneralSettingsState {
  readonly legalEntityId: string | null;
  readonly settings: LegalEntityGeneralSettings | null;
  readonly loading: boolean;
  readonly saving: boolean;
  readonly uploadingLogo: boolean;
  readonly removingLogo: boolean;
  readonly loadError: string | null;
  readonly saveError: string | null;
}
```

Update `initialState`:

```typescript
const initialState: GeneralSettingsState = {
  legalEntityId: null,
  settings: null,
  loading: false,
  saving: false,
  uploadingLogo: false,
  removingLogo: false,
  loadError: null,
  saveError: null
};
```

Add these two methods inside `withMethods((store, api = inject(LegalEntityApiService)) => ({ ... }))`, after `save`:

```typescript

    async uploadLogo(legalEntityId: string, file: File): Promise<boolean> {
      patchState(store, { uploadingLogo: true, saveError: null });
      try {
        const result = await firstValueFrom(api.uploadLogo(legalEntityId, file));
        const current = store.settings();
        if (current && store.legalEntityId() === legalEntityId) {
          patchState(store, { settings: { ...current, logoFileId: result.logoFileId }, uploadingLogo: false });
        } else {
          patchState(store, { uploadingLogo: false });
        }
        return true;
      } catch (err) {
        patchState(store, { uploadingLogo: false, saveError: extractErrorMessage(err, 'Failed to upload logo.') });
        return false;
      }
    },

    async removeLogo(legalEntityId: string): Promise<boolean> {
      patchState(store, { removingLogo: true, saveError: null });
      try {
        await firstValueFrom(api.removeLogo(legalEntityId));
        const current = store.settings();
        if (current && store.legalEntityId() === legalEntityId) {
          patchState(store, { settings: { ...current, logoFileId: null }, removingLogo: false });
        } else {
          patchState(store, { removingLogo: false });
        }
        return true;
      } catch (err) {
        patchState(store, { removingLogo: false, saveError: extractErrorMessage(err, 'Failed to remove logo.') });
        return false;
      }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `npm test -- --watch=false --include="**/general-settings.store.spec.ts"`
Expected: PASS, all tests in the file.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/organization/state/general-settings.store.ts src/app/modules/organization/state/general-settings.store.spec.ts
git commit -m "feat: add uploadLogo/removeLogo to GeneralSettingsStore"
```

---

## Task 9: Frontend — wire up the component UI

**Files:**
- Modify: `src/app/modules/organization/feature/general-settings/general-settings.component.ts`
- Modify: `src/app/modules/organization/feature/general-settings/general-settings.component.html`
- Modify: `src/app/modules/organization/feature/general-settings/general-settings.component.css`
- Test: `src/app/modules/organization/feature/general-settings/general-settings.component.spec.ts`

**Interfaces:**
- Consumes: `GeneralSettingsStore.uploadLogo`/`.removeLogo`/`.uploadingLogo`/`.removingLogo` (Task 8), `LegalEntityApiService.getLogoUrl` (Task 7, injected directly for the pure URL helper — no HTTP call).

- [ ] **Step 1: Write the failing component tests**

Add to `src/app/modules/organization/feature/general-settings/general-settings.component.spec.ts`, inside the `describe` block, after the last `it(...)`:

```typescript
  it('shows the placeholder icon and disabled-looking hint when no logo is set, and uploads a selected file', async () => {
    const fixture = await setupWithLoadedSettings();

    const fileInput: HTMLInputElement = fixture.nativeElement.querySelector('input[type="file"]');
    expect(fileInput).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.gs-logo-preview img')).toBeFalsy();

    const file = new File(['bytes'], 'logo.png', { type: 'image/png' });
    Object.defineProperty(fileInput, 'files', { value: [file] });
    fileInput.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`);
    expect(req.request.method).toBe('PUT');
    req.flush({ legalEntityId: 'company-a', logoFileId: 'file-1' });
    await fixture.whenStable();
    fixture.detectChanges();

    const img: HTMLImageElement = fixture.nativeElement.querySelector('.gs-logo-preview img');
    expect(img).toBeTruthy();
    expect(img.src).toContain(`${environment.apiUrl}/org/legal-entities/company-a/logo?v=file-1`);
    expect(fixture.nativeElement.querySelector('.gs-logo-remove-btn')).toBeTruthy();
  });

  it('rejects an oversized file client-side without calling the API', async () => {
    const fixture = await setupWithLoadedSettings();
    const notification = TestBed.inject(NotificationService);
    spyOn(notification, 'error');

    const fileInput: HTMLInputElement = fixture.nativeElement.querySelector('input[type="file"]');
    const oversized = new File([new Uint8Array(6 * 1024 * 1024)], 'big.png', { type: 'image/png' });
    Object.defineProperty(fileInput, 'files', { value: [oversized] });
    fileInput.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(notification.error).toHaveBeenCalled();
    httpMock.expectNone(`${environment.apiUrl}/org/legal-entities/company-a/logo`);
  });

  it('removes the logo when the Remove button is clicked', async () => {
    const fixture = await setupWithLoadedSettings();
    const fileInput: HTMLInputElement = fixture.nativeElement.querySelector('input[type="file"]');
    const file = new File(['bytes'], 'logo.png', { type: 'image/png' });
    Object.defineProperty(fileInput, 'files', { value: [file] });
    fileInput.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`).flush({ legalEntityId: 'company-a', logoFileId: 'file-1' });
    await fixture.whenStable();
    fixture.detectChanges();

    const removeBtn: HTMLButtonElement = fixture.nativeElement.querySelector('.gs-logo-remove-btn');
    removeBtn.click();
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiUrl}/org/legal-entities/company-a/logo`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.gs-logo-preview img')).toBeFalsy();
  });
```

Add `import { environment } from '../../../../../environments/environment';` to the top of the spec file if it is not already imported (check first — it likely already is, per the existing `setupWithLoadedSettings` helper's use of `environment.apiUrl`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- --watch=false --include="**/general-settings.component.spec.ts"`
Expected: FAIL — no `<input type="file">`, no `.gs-logo-remove-btn`, button is still `disabled`.

- [ ] **Step 3: Update the component TypeScript**

In `src/app/modules/organization/feature/general-settings/general-settings.component.ts`:

Add to the imports at the top (same relative depth `../../data-access/...` that `general-settings.store.ts` already uses to reach the same file from its own sibling `state/` folder):

```typescript
import { LegalEntityApiService } from '../../data-access/legal-entity-api.service';
```

Add this injected field alongside the existing `private readonly notificationService = inject(NotificationService);`:

```typescript
  private readonly legalEntityApi = inject(LegalEntityApiService);
```

Add these members to the class, after `submitting`:

```typescript
  readonly uploadingLogo = this.settingsStore.uploadingLogo;
  readonly removingLogo = this.settingsStore.removingLogo;

  private static readonly ALLOWED_LOGO_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
  private static readonly MAX_LOGO_BYTES = 5 * 1024 * 1024;
```

Add these methods, after `onSubmit`:

```typescript

  logoUrl(): string | null {
    const company = this.selectedLegalEntity();
    const logoFileId = this.settingsStore.settings()?.logoFileId;
    if (!company || !logoFileId) {
      return null;
    }
    return this.legalEntityApi.getLogoUrl(company.id, logoFileId);
  }

  async onLogoFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    if (!GeneralSettingsComponent.ALLOWED_LOGO_TYPES.includes(file.type)) {
      this.notificationService.error('Logo must be a PNG, JPG, or WebP image.');
      return;
    }
    if (file.size > GeneralSettingsComponent.MAX_LOGO_BYTES) {
      this.notificationService.error('Logo must be 5 MB or smaller.');
      return;
    }

    const company = this.selectedLegalEntity();
    if (!company) {
      return;
    }

    const ok = await this.settingsStore.uploadLogo(company.id, file);
    if (ok) {
      this.notificationService.success('Logo uploaded.');
    } else {
      this.notificationService.error(this.settingsStore.saveError() ?? 'Failed to upload logo.');
    }
  }

  async onRemoveLogo(): Promise<void> {
    const company = this.selectedLegalEntity();
    if (!company) {
      return;
    }

    const ok = await this.settingsStore.removeLogo(company.id);
    if (ok) {
      this.notificationService.success('Logo removed.');
    } else {
      this.notificationService.error(this.settingsStore.saveError() ?? 'Failed to remove logo.');
    }
  }
```

- [ ] **Step 4: Update the component HTML**

In `src/app/modules/organization/feature/general-settings/general-settings.component.html`, replace the whole `<!-- Logo — first -->` section (lines 28-44) with:

```html
      <!-- Logo — first -->
      <section class="gs-section">
        <h2 class="gs-section__title">Company Logo</h2>
        <div class="gs-logo-upload-area">
          <div class="gs-logo-preview" aria-label="Company logo preview">
            @if (logoUrl(); as src) {
              <img [src]="src" alt="Company logo" class="gs-logo-preview__img" />
            } @else {
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="18" height="18" x="3" y="3" rx="2" ry="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/></svg>
            }
          </div>
          <div class="gs-logo-info">
            <p class="gs-logo-info__title">Upload your company logo</p>
            <p class="gs-logo-info__hint">PNG, JPG or WebP · Max 5 MB · Recommended 256×256 px</p>
            <div class="gs-logo-actions">
              <label class="gs-logo-btn" [class.gs-logo-btn--busy]="uploadingLogo()">
                <input type="file" accept="image/png,image/jpeg,image/webp" (change)="onLogoFileSelected($event)" [disabled]="uploadingLogo() || removingLogo()" />
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" x2="12" y1="3" y2="15"/></svg>
                {{ uploadingLogo() ? 'Uploading…' : 'Upload logo' }}
              </label>
              @if (logoUrl()) {
                <button type="button" class="gs-logo-remove-btn" [disabled]="uploadingLogo() || removingLogo()" (click)="onRemoveLogo()">
                  {{ removingLogo() ? 'Removing…' : 'Remove' }}
                </button>
              }
            </div>
          </div>
        </div>
      </section>
```

- [ ] **Step 5: Update the component CSS**

In `src/app/modules/organization/feature/general-settings/general-settings.component.css`, replace the `.gs-logo-btn` rule (it currently has `cursor: not-allowed` since the button was permanently disabled) and the `.gs-logo-btn__badge` rule with:

```css
.gs-logo-preview__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 10px;
}

.gs-logo-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.gs-logo-btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  margin-top: 0.25rem;
  padding: 0.375rem 0.875rem;
  border-radius: var(--radius-content);
  border: 1px solid var(--color-border);
  background: var(--color-surface-secondary);
  color: var(--color-text-primary);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.gs-logo-btn--busy {
  opacity: 0.7;
  cursor: default;
}

.gs-logo-btn input[type='file'] {
  position: absolute;
  inset: 0;
  opacity: 0;
  cursor: pointer;
}

.gs-logo-btn input[type='file']:disabled {
  cursor: default;
}

.gs-logo-remove-btn {
  margin-top: 0.25rem;
  padding: 0.375rem 0.875rem;
  border-radius: var(--radius-content);
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-danger-text);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}
```

Remove the old `.gs-logo-btn__badge` rule entirely (no longer referenced by the HTML).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm test -- --watch=false --include="**/general-settings.component.spec.ts"`
Expected: PASS, all tests in the file including the 3 new ones.

- [ ] **Step 7: Manual smoke test**

Start the backend (`dotnet run` from `ONEVO.Api`) and frontend (`npm start`), sign in to a tenant, go to Settings → General Settings, and:
1. Upload a PNG/JPG under 5 MB — confirm the preview image appears and a success toast shows.
2. Reload the page — confirm the logo still shows (proves the `GET` display path and `logoFileId` persistence both work, not just the immediate post-upload state).
3. Click Remove — confirm it reverts to the placeholder icon.
4. Try a file over 5 MB or a non-image file — confirm the client-side rejection message appears and no network request fires (check the browser Network tab).

This step has no automated pass/fail — if the `<img src>` 401s in the browser despite the cookie analysis in the design doc, that invalidates the "no token/header plumbing needed" assumption and the component needs a blob-fetch fallback instead. Confirm this works before treating the feature as done.

- [ ] **Step 8: Commit**

```bash
git add src/app/modules/organization/feature/general-settings/general-settings.component.ts src/app/modules/organization/feature/general-settings/general-settings.component.html src/app/modules/organization/feature/general-settings/general-settings.component.css src/app/modules/organization/feature/general-settings/general-settings.component.spec.ts
git commit -m "feat: wire up logo upload/display/remove in General Settings UI"
```

---

## Final check

After Task 9, run the full backend and frontend suites once more to confirm nothing elsewhere regressed:

```bash
# from C:\onevoNew\HRMS-Backend-v1
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LegalEntitiesIntegrationTests"

# from C:\onevoNew\Hrms--Web-application---front-end---v1
npm test -- --watch=false
```
