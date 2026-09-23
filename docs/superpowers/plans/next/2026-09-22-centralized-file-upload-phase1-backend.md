# Centralized File Upload — Phase 1 Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every bespoke per-feature upload/resolve endpoint with one generic
`POST/DELETE/GET /api/v1/files` primitive backed by `entity_assets`, and cut Employee avatars +
Legal Entity logos over to it completely — including dropping the old denormalized
`avatar_file_id`/`logo_file_id` columns and deleting every old endpoint/handler, not leaving them
running alongside the new ones.

**Architecture:** Two layers, per the approved design spec: (1) a purpose-validated, no-per-owner-
check upload/delete primitive (`POST /api/v1/files`, `DELETE /api/v1/files/{fileId}`) — safe to
centralize since it only needs "authenticated tenant user + valid purpose + quota", exactly
generalizing the existing `POST /tasks/pending-uploads` pattern; (2) a **dispatched** resolve
endpoint (`GET /api/v1/files/{fileId}`) that looks up the file's `entity_assets` link and applies
an owner-type-specific access policy (`IEntityAssetAccessPolicy`), because task/objective/project
files each already have different, non-negotiable authorization rules
(`GetTaskFileQueryHandler` proves this — projects:read OR active objective membership). Employee
and Legal Entity policies are new in this phase; Task/Objective/Project policies wrap the
*existing* logic unchanged (Phase 2 migrates their controllers to call through this endpoint
instead of their own — not touched in Phase 1).

**Tech Stack:** .NET / ASP.NET Core, MediatR (CQRS), EF Core migration, xUnit + Moq + FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-09-22-centralized-file-upload-design.md`. This plan supersedes that spec's original "Phase 1" sketch with the owner-type-dispatch detail worked out below (the spec described the shape; this plan is what's actually buildable against `GetTaskFileQueryHandler`'s real authorization requirements). Frontend companion: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-22-centralized-file-upload-phase1-frontend.md`.

## Global Constraints

- Every upload/download must go through `IFileStorageService` — never `IObjectStorageAdapter` directly (existing rule, unchanged).
- `entity_assets.owner_type` values are `EntityAssetOwnerTypes` constants only, never raw string literals (existing rule, `Common/Constants/EntityAssetOwnerTypes.cs`).
- A dropped column (`avatar_file_id`, `logo_file_id`) is only safe to drop in the *same* migration file as its backfill, immediately preceding it — this plan's Task 2 does the backfill and drop as one migration since this is a dev-only cutover (no production data to stage a two-step rollout for, per the user's explicit "delete the old stuff" instruction).
- `GET /api/v1/files/{fileId}` must default-deny for any `owner_type` it doesn't have a registered `IEntityAssetAccessPolicy` for — never fail open.

---

### Task 1: `EntityAssetOwnerTypes.Employee` / `.LegalEntity` + `IEntityAssetAccessPolicy`

**Files:**
- Modify: `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`
- Create: `src/ONEVO.Application/Features/Storage/EntityAssets/ServiceInterfaces/IEntityAssetAccessPolicy.cs`
- Create: `src/ONEVO.Application/Features/Storage/EntityAssets/Services/EmployeeEntityAssetAccessPolicy.cs`
- Create: `src/ONEVO.Application/Features/Storage/EntityAssets/Services/LegalEntityEntityAssetAccessPolicy.cs`
- Create: `src/ONEVO.Application/Features/Storage/EntityAssets/Services/EntityAssetAccessPolicyResolver.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/EmployeeEntityAssetAccessPolicyTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/LegalEntityEntityAssetAccessPolicyTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/EntityAssetAccessPolicyResolverTests.cs`

**Interfaces:**
- Produces: `IEntityAssetAccessPolicy.CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct) : Task<bool>` — Task 4's `GetFileQueryHandler` calls this via the resolver.
- Produces: `IEntityAssetAccessPolicyResolver.Resolve(string ownerType) : IEntityAssetAccessPolicy?` — returns null for an unregistered owner type (the default-deny case).

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/EmployeeEntityAssetAccessPolicyTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class EmployeeEntityAssetAccessPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public async Task CanReadAsync_EmployeeExistsInTenant_ReturnsTrue()
    {
        var employees = new Mock<CommonEmployeeRepo>();
        employees.Setup(r => r.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = EmployeeId, TenantId = TenantId });
        var sut = new EmployeeEntityAssetAccessPolicy(employees.Object);

        var result = await sut.CanReadAsync(TenantId, EmployeeId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadAsync_EmployeeNotFoundInTenant_ReturnsFalse()
    {
        var employees = new Mock<CommonEmployeeRepo>();
        employees.Setup(r => r.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);
        var sut = new EmployeeEntityAssetAccessPolicy(employees.Object);

        var result = await sut.CanReadAsync(TenantId, EmployeeId, CancellationToken.None);

        result.Should().BeFalse();
    }
}
```

Create `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/LegalEntityEntityAssetAccessPolicyTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class LegalEntityEntityAssetAccessPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    [Fact]
    public async Task CanReadAsync_LegalEntityExistsInTenant_ReturnsTrue()
    {
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = LegalEntityId, TenantId = TenantId, Name = "Acme", CountryCode = "LKA", CurrencyCode = "LKR", IsActive = true });
        var sut = new LegalEntityEntityAssetAccessPolicy(legalEntities.Object);

        var result = await sut.CanReadAsync(TenantId, LegalEntityId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadAsync_LegalEntityNotFoundInTenant_ReturnsFalse()
    {
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new LegalEntityEntityAssetAccessPolicy(legalEntities.Object);

        var result = await sut.CanReadAsync(TenantId, LegalEntityId, CancellationToken.None);

        result.Should().BeFalse();
    }
}
```

Create `tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/EntityAssetAccessPolicyResolverTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class EntityAssetAccessPolicyResolverTests
{
    [Fact]
    public void Resolve_KnownOwnerType_ReturnsThePolicy()
    {
        var employeePolicy = new Mock<IEntityAssetAccessPolicy>().Object;
        var sut = new EntityAssetAccessPolicyResolver(
            new Dictionary<string, IEntityAssetAccessPolicy> { [EntityAssetOwnerTypes.Employee] = employeePolicy });

        sut.Resolve(EntityAssetOwnerTypes.Employee).Should().BeSameAs(employeePolicy);
    }

    [Fact]
    public void Resolve_UnknownOwnerType_ReturnsNull_DefaultDeny()
    {
        var sut = new EntityAssetAccessPolicyResolver(new Dictionary<string, IEntityAssetAccessPolicy>());

        sut.Resolve("some_unregistered_type").Should().BeNull();
    }
}
```

(Add `using Moq;` and `using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;` to the resolver test's using block alongside the ones shown.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EntityAssets"`
Expected: FAIL to compile — none of these types exist yet.

- [ ] **Step 3: Add the owner type constants**

In `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`, change:

```csharp
public static class EntityAssetOwnerTypes
{
    public const string Project = "project";
    public const string Objective = "objective";
    public const string Task = "task";
}
```

to:

```csharp
public static class EntityAssetOwnerTypes
{
    public const string Project = "project";
    public const string Objective = "objective";
    public const string Task = "task";
    public const string Employee = "employee";
    public const string LegalEntity = "legal_entity";
}
```

- [ ] **Step 4: Write the policy interface**

Create `src/ONEVO.Application/Features/Storage/EntityAssets/ServiceInterfaces/IEntityAssetAccessPolicy.cs`:

```csharp
namespace ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

/// <summary>
/// Decides whether the current caller may read a file already linked (via entity_assets) to a
/// specific owner. One implementation per owner_type, registered in
/// EntityAssetAccessPolicyResolver - GetFileQueryHandler default-denies any owner_type with no
/// registered policy, so a new owner type is unreadable through the generic resolve endpoint
/// until it explicitly opts in here.
/// </summary>
public interface IEntityAssetAccessPolicy
{
    Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default);
}
```

Create `src/ONEVO.Application/Features/Storage/EntityAssets/ServiceInterfaces/IEntityAssetAccessPolicyResolver.cs`:

```csharp
namespace ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

public interface IEntityAssetAccessPolicyResolver
{
    /// <summary>Null if no policy is registered for this owner_type - the caller must treat that as deny, not as "no restriction".</summary>
    IEntityAssetAccessPolicy? Resolve(string ownerType);
}
```

- [ ] **Step 5: Write the Employee and Legal Entity policies**

Create `src/ONEVO.Application/Features/Storage/EntityAssets/Services/EmployeeEntityAssetAccessPolicy.cs`:

```csharp
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

/// <summary>
/// Coverage-free by design, same reasoning as ICallerIdentityResolver's name/avatar resolution:
/// an avatar carries no more sensitivity than a display name, and every authenticated tenant
/// user already sees employee names across Work Management regardless of People-module
/// management-coverage scoping.
/// </summary>
public sealed class EmployeeEntityAssetAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _employees;

    public EmployeeEntityAssetAccessPolicy(Common.RepositoryInterfaces.IEmployeeRepository employees) => _employees = employees;

    public async Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(tenantId, ownerId, ct);
        return employee is not null;
    }
}
```

Create `src/ONEVO.Application/Features/Storage/EntityAssets/Services/LegalEntityEntityAssetAccessPolicy.cs`:

```csharp
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

/// <summary>A company logo is shown in the topbar/side-navbar to every tenant user regardless of role - coverage-free by the same reasoning as EmployeeEntityAssetAccessPolicy.</summary>
public sealed class LegalEntityEntityAssetAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly ILegalEntityRepository _legalEntities;

    public LegalEntityEntityAssetAccessPolicy(ILegalEntityRepository legalEntities) => _legalEntities = legalEntities;

    public async Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
    {
        var entity = await _legalEntities.GetByIdForTenantAsync(tenantId, ownerId, ct);
        return entity is not null;
    }
}
```

- [ ] **Step 6: Write the resolver**

Create `src/ONEVO.Application/Features/Storage/EntityAssets/Services/EntityAssetAccessPolicyResolver.cs`:

```csharp
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.EntityAssets.Services;

public sealed class EntityAssetAccessPolicyResolver : IEntityAssetAccessPolicyResolver
{
    private readonly IReadOnlyDictionary<string, IEntityAssetAccessPolicy> _policiesByOwnerType;

    public EntityAssetAccessPolicyResolver(IReadOnlyDictionary<string, IEntityAssetAccessPolicy> policiesByOwnerType)
        => _policiesByOwnerType = policiesByOwnerType;

    public IEntityAssetAccessPolicy? Resolve(string ownerType)
        => _policiesByOwnerType.TryGetValue(ownerType, out var policy) ? policy : null;
}
```

- [ ] **Step 7: Register DI**

In the DI composition root (find the existing `AddScoped<IEntityAssetRepository, EfEntityAssetRepository>()`-style registration, likely in `src/ONEVO.Infrastructure/DependencyInjection.cs` or `src/ONEVO.Application/DependencyInjection.cs` — grep for `IEntityAssetRepository` registration to find the right file), add:

```csharp
services.AddScoped<EmployeeEntityAssetAccessPolicy>();
services.AddScoped<LegalEntityEntityAssetAccessPolicy>();
services.AddScoped<IEntityAssetAccessPolicyResolver>(sp => new EntityAssetAccessPolicyResolver(
    new Dictionary<string, IEntityAssetAccessPolicy>
    {
        [EntityAssetOwnerTypes.Employee] = sp.GetRequiredService<EmployeeEntityAssetAccessPolicy>(),
        [EntityAssetOwnerTypes.LegalEntity] = sp.GetRequiredService<LegalEntityEntityAssetAccessPolicy>()
        // Task 2 of the Part 2 plan (project/objective/task consolidation) adds the remaining
        // three owner types here when their controllers migrate to the generic resolve endpoint.
    }));
```

Add the necessary `using ONEVO.Application.Common.Constants;`, `using ONEVO.Application.Features.Storage.EntityAssets.Services;`, `using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;` to that file.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EntityAssets"`
Expected: PASS, all 4 tests green.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs src/ONEVO.Application/Features/Storage/EntityAssets/ tests/ONEVO.Tests.Unit/Features/Storage/EntityAssets/
git commit -m "feat: add Employee/LegalEntity entity_assets owner types and access policies"
```

---

### Task 2: Migration — backfill `entity_assets` from `avatar_file_id`/`logo_file_id`, drop both columns

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`
- Modify: `src/ONEVO.Domain/Features/OrgStructure/Entities/LegalEntity.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_MigrateAvatarAndLogoToEntityAssets.cs` (via `dotnet ef migrations add`)

**Interfaces:**
- Produces: every `employees.avatar_file_id is not null` row becomes an `entity_assets` row (`owner_type='employee'`, `asset_purpose='employee_avatar'`, `is_primary=true`); same for `legal_entities.logo_file_id` → `owner_type='legal_entity'`, `asset_purpose='company_logo'`. Both source columns are then dropped. Task 5/6/7 of this plan (and the whole frontend plan) depend on `entity_assets` being the only source of truth after this migration — no code may read `Employee.AvatarFileId`/`LegalEntity.LogoFileId` after this task.

- [ ] **Step 1: Remove the domain properties**

In `src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs`, delete the line:

```csharp
public Guid? AvatarFileId { get; set; }
```

In `src/ONEVO.Domain/Features/OrgStructure/Entities/LegalEntity.cs`, delete the equivalent `LogoFileId` property line.

- [ ] **Step 2: Build to find every broken reference**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj 2>&1 | grep -i "AvatarFileId\|LogoFileId"`
Expected: a list of every file still referencing these properties. As of this plan being written, that list is:
- `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs` (Task 3 rewrites this)
- `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/SetMyAvatarCommandHandler.cs` (Task 5 deletes this whole file)
- `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/GetMyAvatarQueryHandler.cs` (Task 5 deletes this whole file)
- `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeAvatar/GetEmployeeAvatarQueryHandler.cs` (Task 5 deletes this whole file — the one-off endpoint from earlier this session, superseded by the generic resolve endpoint)
- `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` (4 projections — Task 6 removes the `AvatarFileId` argument from each)
- `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs` and its `ObjectiveMemberItemResponse` (uses `EmployeeIdentityDto.AvatarFileId`, which reads `employee.AvatarFileId` inside `CallerIdentityResolver` — Task 7 rewrites this whole chain to use `entity_assets` instead)
- `src/ONEVO.Application/Features/WorkManagement/Common/Services/CallerIdentityResolver.cs` (`EmployeeIdentityDto.AvatarFileId` construction — Task 7)
- Legal entity equivalents: `SetLegalEntityLogoCommandHandler.cs`, `GetLegalEntityLogoQueryHandler.cs`, `RemoveLegalEntityLogoCommandHandler.cs` (Task 5 deletes these)
- Any test file referencing `AvatarFileId =`/`LogoFileId =` on an `Employee`/`LegalEntity` object initializer (fix each to remove that line — the entity no longer has the property).

Do not fix these yet — Steps 3-4 below write the migration first (TDD: the migration is the next unit under test), then this plan's later tasks fix each compile error as part of their own scoped work.

- [ ] **Step 2: Generate and edit the migration**

Run: `dotnet ef migrations add MigrateAvatarAndLogoToEntityAssets --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`

This auto-generates the `DropColumn` calls for `avatar_file_id`/`logo_file_id` (from Step 1's entity changes) but not the backfill `INSERT`. Open the generated migration file and add the backfill as raw SQL **before** the auto-generated `DropColumn` calls, inside `Up()`:

```csharp
migrationBuilder.Sql(@"
    INSERT INTO entity_assets (id, tenant_id, owner_type, owner_id, asset_purpose, file_record_id, is_primary, sort_order, created_by_type, created_by_id, created_at)
    SELECT gen_random_uuid(), tenant_id, 'employee', id, 'employee_avatar', avatar_file_id, true, NULL, 'system', id, now()
    FROM employees
    WHERE avatar_file_id IS NOT NULL AND deleted_at IS NULL;
");
migrationBuilder.Sql(@"
    INSERT INTO entity_assets (id, tenant_id, owner_type, owner_id, asset_purpose, file_record_id, is_primary, sort_order, created_by_type, created_by_id, created_at)
    SELECT gen_random_uuid(), tenant_id, 'legal_entity', id, 'company_logo', logo_file_id, true, NULL, 'system', id, now()
    FROM legal_entities
    WHERE logo_file_id IS NOT NULL AND deleted_at IS NULL;
");
```

(`created_by_id` is set to the owner's own id as a placeholder — there is no real "who uploaded this" actor recoverable from the old denormalized column; this matches how `SinceOrInvitedAt`-style backfills elsewhere in this codebase handle missing historical actor data.)

Then verify the migration's `Down()` method drops the backfilled rows before re-adding the columns (EF's auto-generated `Down()` already re-adds `avatar_file_id`/`logo_file_id` as nullable columns; add a `migrationBuilder.Sql("DELETE FROM entity_assets WHERE owner_type IN ('employee','legal_entity');")` before that, so `Down()` doesn't leave orphaned rows if ever run).

- [ ] **Step 3: Apply the migration against the dev database and verify manually**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: migration applies with no errors. Manually verify: `SELECT count(*) FROM entity_assets WHERE owner_type = 'employee';` returns a row for every employee that previously had `avatar_file_id` set (cross-check against your own test account, whose avatar upload from earlier this session should now show up here).

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Entities/Employee.cs src/ONEVO.Domain/Features/OrgStructure/Entities/LegalEntity.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: migrate avatar_file_id/logo_file_id into entity_assets, drop both columns"
```

Note: the repo will not build again until Tasks 3, 5, 6, and 7 below fix every reference this task's Step 2 build found. That is expected and by design (TDD at the migration level) — proceed directly to Task 3.

---

### Task 3: Generic `POST /api/v1/files` + `DELETE /api/v1/files/{fileId}`

**Files:**
- Create: `src/ONEVO.Application/Features/Storage/File/Commands/UploadFile/UploadFileCommand.cs`
- Create: `src/ONEVO.Application/Features/Storage/File/Commands/UploadFile/UploadFileCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Storage/File/Commands/DeleteFile/DeleteFileCommand.cs`
- Create: `src/ONEVO.Application/Features/Storage/File/Commands/DeleteFile/DeleteFileCommandHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Storage/FilesController.cs`
- Create: `src/ONEVO.Api/Contracts/Storage/UploadFileFormRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Storage/UploadFileViewModel.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadFileCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/DeleteFileCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IFileStorageService.UploadAsync`/`DeleteAsync`/`GetRecordAsync` (existing), `UploadPurposeCatalog.IsSupported` (existing).
- Produces: `POST /api/v1/files` returns `{fileId, originalFileName, fileSizeBytes, contentType}` — the frontend `UploadService.upload()` (Phase 1 frontend plan) consumes this shape exactly, mirroring `TaskPendingUploadViewModel`'s existing shape so no new frontend response type is needed beyond renaming.

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadFileCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class UploadFileCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private UploadFileCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        return new UploadFileCommandHandler(_fileStorage.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_UnsupportedPurpose_ReturnsBadRequest_WithoutCallingFileStorage()
    {
        var sut = CreateHandler();
        using var stream = new MemoryStream();

        var result = await sut.Handle(new UploadFileCommand("not_a_real_purpose", "a.png", "image/png", stream), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _fileStorage.Verify(f => f.UploadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SupportedPurpose_DelegatesToFileStorageUploadAsync()
    {
        var sut = CreateHandler();
        using var stream = new MemoryStream();
        var record = new FileRecordDto(Guid.NewGuid(), TenantId, "key", "a.png", "a.png", "image/png", 100, "sha", "active", DateTimeOffset.UtcNow, UserId, null);
        _fileStorage.Setup(f => f.UploadAsync(TenantId, UserId, "a.png", "image/png", "employee_avatar", stream, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(record));

        var result = await sut.Handle(new UploadFileCommand("employee_avatar", "a.png", "image/png", stream), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(record.Id);
    }
}
```

Create `tests/ONEVO.Tests.Unit/Features/Storage/File/DeleteFileCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Commands.DeleteFile;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class DeleteFileCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private DeleteFileCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        return new DeleteFileCommandHandler(_fileStorage.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_CallerIsNotTheUploader_ReturnsForbidden()
    {
        var sut = CreateHandler();
        _fileStorage.Setup(f => f.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha", "active", DateTimeOffset.UtcNow, Guid.NewGuid(), null)));

        var result = await sut.Handle(new DeleteFileCommand(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _fileStorage.Verify(f => f.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsTheUploader_DeletesIt()
    {
        var sut = CreateHandler();
        _fileStorage.Setup(f => f.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha", "active", DateTimeOffset.UtcNow, UserId, null)));
        _fileStorage.Setup(f => f.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await sut.Handle(new DeleteFileCommand(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Storage.File.UploadFileCommandHandlerTests|FullyQualifiedName~Storage.File.DeleteFileCommandHandlerTests"`
Expected: FAIL to compile — none of these types exist yet.

- [ ] **Step 3: Write the upload command + handler**

Create `src/ONEVO.Application/Features/Storage/File/Commands/UploadFile/UploadFileCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.Commands.UploadFile;

public record UploadFileCommand(string Purpose, string FileName, string ContentType, Stream Content) : IRequest<Result<FileRecordDto>>;
```

Create `src/ONEVO.Application/Features/Storage/File/Commands/UploadFile/UploadFileCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Commands.UploadFile;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, Result<FileRecordDto>>
{
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public UploadFileCommandHandler(IFileStorageService fileStorage, ICurrentUser currentUser)
    {
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileRecordDto>> Handle(UploadFileCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileRecordDto>.Forbidden("Authentication required.");

        if (!UploadPurposeCatalog.IsSupported(request.Purpose))
            return Result<FileRecordDto>.Failure($"Unsupported upload purpose '{request.Purpose}'.", 400);

        return await _fileStorage.UploadAsync(
            _currentUser.TenantId, _currentUser.UserId, request.FileName, request.ContentType, request.Purpose, request.Content, ct);
    }
}
```

- [ ] **Step 4: Write the delete command + handler**

Create `src/ONEVO.Application/Features/Storage/File/Commands/DeleteFile/DeleteFileCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Storage.File.Commands.DeleteFile;

public record DeleteFileCommand(Guid FileId) : IRequest<Result>;
```

Create `src/ONEVO.Application/Features/Storage/File/Commands/DeleteFile/DeleteFileCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Commands.DeleteFile;

public class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand, Result>
{
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public DeleteFileCommandHandler(IFileStorageService fileStorage, ICurrentUser currentUser)
    {
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
        if (!recordResult.IsSuccess)
            return Result.Failure(recordResult.Error!, recordResult.StatusCode ?? 400);

        if (recordResult.Value!.UploadedByUserId != _currentUser.UserId)
            return Result.Forbidden("Only the uploader may delete this file.");

        return await _fileStorage.DeleteAsync(tenantId, _currentUser.UserId, request.FileId, ct);
    }
}
```

- [ ] **Step 5: Write the controller and its contracts**

Create `src/ONEVO.Api/Contracts/Storage/UploadFileFormRequest.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.Storage;

public class UploadFileFormRequest
{
    public string Purpose { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
```

Create `src/ONEVO.Api/Contracts/Storage/UploadFileViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.Storage;

public record UploadFileViewModel(Guid FileId, string OriginalFileName, long FileSizeBytes, string ContentType);
```

Create `src/ONEVO.Api/Controllers/Tenant/Storage/FilesController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Storage;
using ONEVO.Application.Features.Storage.File.Commands.DeleteFile;
using ONEVO.Application.Features.Storage.File.Commands.UploadFile;
using ONEVO.Application.Features.Storage.File.Queries.GetFile;

namespace ONEVO.Api.Controllers.Tenant.Storage;

/// <summary>
/// The single generic upload/resolve/delete surface every upload feature goes through - see
/// docs/superpowers/specs/next/2026-09-22-centralized-file-upload-design.md. Purpose validation
/// happens in UploadFileCommandHandler; per-owner read authorization for GET happens in
/// GetFileQueryHandler via IEntityAssetAccessPolicyResolver, never here.
/// </summary>
[ApiController]
[Route("api/v1/files")]
[Authorize(Policy = "TenantPolicy")]
public class FilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FilesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadFileFormRequest request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();
        var result = await _mediator.Send(new UploadFileCommand(request.Purpose, request.File.FileName, request.File.ContentType, stream), ct);

        return result.IsSuccess
            ? StatusCode(201, new UploadFileViewModel(result.Value!.Id, result.Value.OriginalFileName, result.Value.FileSizeBytes, result.Value.ContentType))
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteFileCommand(fileId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{fileId:guid}")]
    public async Task<IActionResult> Get(Guid fileId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetFileQuery(fileId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        return File(result.Value!.Content, result.Value!.ContentType);
    }
}
```

(`GetFileQuery` doesn't exist yet — Task 4 writes it. This controller will not compile until Task 4 is done; that's expected, move directly to it.)

- [ ] **Step 6: Run the tests to verify Steps 3-4 pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Storage.File.UploadFileCommandHandlerTests|FullyQualifiedName~Storage.File.DeleteFileCommandHandlerTests"`
Expected: PASS, all 4 tests green (the controller not compiling yet does not block these Application-layer tests).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/Commands/ src/ONEVO.Api/Controllers/Tenant/Storage/ src/ONEVO.Api/Contracts/Storage/ tests/ONEVO.Tests.Unit/Features/Storage/File/
git commit -m "feat: add generic POST/DELETE /api/v1/files primitive"
```

---

### Task 4: `GET /api/v1/files/{fileId}` — owner-type-dispatched resolve

**Files:**
- Create: `src/ONEVO.Application/Features/Storage/File/Queries/GetFile/GetFileQuery.cs`
- Create: `src/ONEVO.Application/Features/Storage/File/Queries/GetFile/GetFileQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/GetFileQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository.GetByFileRecordIdAsync` (existing), `IEntityAssetAccessPolicyResolver.Resolve` (Task 1), `IFileStorageService.GetRecordAsync`/`OpenReadAsync` (existing).
- Produces: `GetFileQuery(Guid FileId) : IRequest<Result<FileStreamDto>>` — Task 3's `FilesController.Get` sends this.

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/Storage/File/GetFileQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Queries.GetFile;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class GetFileQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<IEntityAssetAccessPolicyResolver> _policyResolver = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GetFileQueryHandler CreateHandler()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        return new GetFileQueryHandler(_entityAssets.Object, _policyResolver.Object, _fileStorage.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_UnlinkedFile_UploaderCanReadIt()
    {
        var sut = CreateHandler();
        _entityAssets.Setup(r => r.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(f => f.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha", "active", DateTimeOffset.UtcNow, UserId, null)));
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await sut.Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnlinkedFile_SomeoneElseCannotReadIt()
    {
        var sut = CreateHandler();
        _entityAssets.Setup(r => r.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(f => f.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha", "active", DateTimeOffset.UtcNow, Guid.NewGuid(), null)));

        var result = await sut.Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_UnregisteredOwnerType_DefaultDenies()
    {
        var sut = CreateHandler();
        _entityAssets.Setup(r => r.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "some_unregistered_type", OwnerId = OwnerId, FileRecordId = FileId });
        _policyResolver.Setup(r => r.Resolve("some_unregistered_type")).Returns((IEntityAssetAccessPolicy?)null);

        var result = await sut.Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_PolicyDenies_ReturnsNotFound()
    {
        var sut = CreateHandler();
        _entityAssets.Setup(r => r.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "employee", OwnerId = OwnerId, FileRecordId = FileId });
        var policy = new Mock<IEntityAssetAccessPolicy>();
        policy.Setup(p => p.CanReadAsync(TenantId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _policyResolver.Setup(r => r.Resolve("employee")).Returns(policy.Object);

        var result = await sut.Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_PolicyAllows_StreamsIt()
    {
        var sut = CreateHandler();
        _entityAssets.Setup(r => r.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "employee", OwnerId = OwnerId, FileRecordId = FileId });
        var policy = new Mock<IEntityAssetAccessPolicy>();
        policy.Setup(p => p.CanReadAsync(TenantId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _policyResolver.Setup(r => r.Resolve("employee")).Returns(policy.Object);
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await sut.Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetFileQueryHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Write the query and handler**

Create `src/ONEVO.Application/Features/Storage/File/Queries/GetFile/GetFileQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.Storage.File.Queries.GetFile;

public record GetFileQuery(Guid FileId) : IRequest<Result<FileStreamDto>>;
```

Create `src/ONEVO.Application/Features/Storage/File/Queries/GetFile/GetFileQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;

namespace ONEVO.Application.Features.Storage.File.Queries.GetFile;

/// <summary>
/// The single generic file-read path every feature resolves images/documents through. An
/// unlinked ("pending upload") file is visible only to its uploader. A linked file is dispatched
/// to the IEntityAssetAccessPolicy registered for its owner_type - an unregistered owner_type
/// default-denies (see EntityAssetAccessPolicyResolver).
/// </summary>
public class GetFileQueryHandler : IRequestHandler<GetFileQuery, Result<FileStreamDto>>
{
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IEntityAssetAccessPolicyResolver _policyResolver;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUser _currentUser;

    public GetFileQueryHandler(
        IEntityAssetRepository entityAssets, IEntityAssetAccessPolicyResolver policyResolver,
        IFileStorageService fileStorage, ICurrentUser currentUser)
    {
        _entityAssets = entityAssets;
        _policyResolver = policyResolver;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<Result<FileStreamDto>> Handle(GetFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);

        if (link is null)
        {
            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess || recordResult.Value!.DeletedAt is not null || recordResult.Value.UploadedByUserId != _currentUser.UserId)
                return Result<FileStreamDto>.NotFound("File not found.");

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        var policy = _policyResolver.Resolve(link.OwnerType);
        if (policy is null)
            return Result<FileStreamDto>.NotFound("File not found.");

        var canRead = await policy.CanReadAsync(tenantId, link.OwnerId, ct);
        if (!canRead)
            return Result<FileStreamDto>.NotFound("File not found.");

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetFileQueryHandlerTests"`
Expected: PASS, all 5 tests green.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: `FilesController` now compiles (GetFileQuery exists). The build will still fail on the `AvatarFileId`/`LogoFileId` references Task 2 Step 2 found — that's expected, proceed to Task 5.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/Queries/ tests/ONEVO.Tests.Unit/Features/Storage/File/GetFileQueryHandlerTests.cs
git commit -m "feat: add owner-type-dispatched GET /api/v1/files/{fileId}"
```

---

### Task 5: Delete every old employee-avatar and legal-entity-logo endpoint/handler

**Files:**
- Delete: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/` (whole folder)
- Delete: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/` (whole folder)
- Delete: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeAvatar/` (whole folder — the one-off from earlier this session)
- Delete: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyAvatarQueryHandlerTests.cs`, `GetEmployeeAvatarQueryHandlerTests.cs`
- Delete: the Legal Entity logo command/query folders (find via `find src/ONEVO.Application/Features/OrgStructure -iname "*Logo*"`) and their tests
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (remove `SetMyAvatar`, `GetMyAvatar`, `GetEmployeeAvatar` actions and their `using`s)
- Modify: `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs` (remove `SetLogo`, `GetLogo`, `RemoveLogo` actions and their `using`s)

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this task only deletes. Task 6/7 and the frontend plan are what re-point every consumer at `POST/GET/DELETE /api/v1/files` instead.

- [ ] **Step 1: Find every file to delete**

Run: `find src/ONEVO.Application/Features/OrgStructure -iname "*Logo*"` and `find tests -iname "*Logo*" -path "*LegalEntity*"` — list every match; these are the Legal Entity logo command/query/test files this task deletes, alongside the three Employee-avatar folders listed above.

- [ ] **Step 2: Delete the Application-layer folders**

```bash
git rm -r src/ONEVO.Application/Features/CoreHr/Employee/Commands/SetMyAvatar/
git rm -r src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyAvatar/
git rm -r src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeAvatar/
git rm tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyAvatarQueryHandlerTests.cs
git rm tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeAvatarQueryHandlerTests.cs
# Then git rm -r each Legal Entity logo folder/test file found in Step 1.
```

- [ ] **Step 3: Remove the controller actions**

In `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`, delete the `SetMyAvatar`, `GetMyAvatar`, and `GetEmployeeAvatar` action methods (and the now-unused `using ONEVO.Application.Features.CoreHr.Employee.Commands.SetMyAvatar;`, `using ...Queries.GetMyAvatar;`, `using ...Queries.GetEmployeeAvatar;` lines).

In `src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs`, delete the `SetLogo`, `GetLogo`, and `RemoveLogo` action methods and their now-unused `using`s.

- [ ] **Step 4: Build to confirm nothing else references the deleted types**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj 2>&1 | grep -i "error"`
Expected: only the `AvatarFileId`/`LogoFileId`-related errors from Task 2 Step 2 that Tasks 6-7 haven't fixed yet remain — no errors referencing the just-deleted types. If any appear, fix that reference now (it means something outside this plan's anticipated list depended on the deleted code).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: delete the old per-feature avatar/logo endpoints, superseded by the generic files endpoint"
```

---

### Task 6: Point `GetMyProfile` + `EfEmployeeRepository` avatar reads at `entity_assets`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs` (4 `EmployeeListItemResponse` projections)
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployee/GetEmployeeQueryHandler.cs`
- Test: update `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetMyProfileQueryHandlerTests.cs`, `ListEmployeesQueryHandlerTests.cs`, `GetEmployeeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEntityAssetRepository.GetPrimaryFileIdsByOwnerAsync(tenantId, "employee", employeeIds, "employee_avatar", ct)` (existing, already batch-shaped).
- Produces: `MyPersonalInformationResponse.AvatarFileId` and `EmployeeListItemResponse.AvatarFileId` are now sourced from `entity_assets`, not a denormalized column — the frontend's `getFileUrl(fileId)` (Phase 1 frontend plan) works unchanged either way since it was already keyed by raw file id.

- [ ] **Step 1: Simplify `EmployeeListItemResponse`**

In `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeListItemResponse.cs`, remove the `AvatarUrl` field added earlier this session (no longer needed — the frontend resolves the URL itself from `AvatarFileId` via the generic endpoint):

```csharp
    string? WorkModeLabel = null,
    Guid? AvatarFileId = null);
```

- [ ] **Step 2: Remove the `AvatarFileId` argument from all 4 `EfEmployeeRepository` projections**

In `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeRepository.cs`, remove the trailing `AvatarFileId: row.e.AvatarFileId` / `AvatarFileId: row.Row.e.AvatarFileId` named argument from all 4 `new EmployeeListItemResponse(...)` calls (grep `AvatarFileId: row` to find them) — the entity no longer has that column to project from (Task 2 dropped it).

- [ ] **Step 3: Batch-resolve avatars in `ListEmployeesQueryHandler`**

In `src/ONEVO.Application/Features/CoreHr/Employee/Queries/ListEmployees/ListEmployeesQueryHandler.cs`, replace the `IFileStorageService`-based signed-URL resolution block added earlier this session with an `IEntityAssetRepository` lookup. Change the constructor to inject `IEntityAssetRepository entityAssets` instead of `IFileStorageService fileStorage` (remove the now-unused `AvatarUrlExpiry` constant and `IFileStorageService` field), and replace the resolution block before the final `return` with:

```csharp
        var avatarFileIdByEmployeeId = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            _currentUser.TenantId, EntityAssetOwnerTypes.Employee, items.Select(i => i.Id).ToList(), UploadPurposeCatalog.EmployeeAvatar, ct);
        items = items.Select(i => avatarFileIdByEmployeeId.TryGetValue(i.Id, out var fileId)
            ? i with { AvatarFileId = fileId }
            : i).ToList();
```

Add `using ONEVO.Application.Common.Constants;` and `using ONEVO.Application.Features.Storage.File.Helpers;` and `using ONEVO.Application.Common.RepositoryInterfaces;` (for `IEntityAssetRepository`) to this file's usings.

- [ ] **Step 4: Resolve the single-employee avatar in `GetEmployeeQueryHandler`**

In `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployee/GetEmployeeQueryHandler.cs`, replace the `IFileStorageService`-based signed-URL block with:

```csharp
        var avatarFileIdByEmployeeId = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Employee, new[] { request.EmployeeId }, UploadPurposeCatalog.EmployeeAvatar, ct);
        var avatarFileId = avatarFileIdByEmployeeId.GetValueOrDefault(request.EmployeeId);

        return Result<EmployeeListItemResponse>.Success(visible with
        {
            InvitationStatus = InvitationStatusOf(invitation, _clock.UtcNow),
            InvitationExpiresAt = invitation?.ExpiresAt,
            AvatarFileId = avatarFileId == Guid.Empty ? null : avatarFileId
        });
```

(`GetPrimaryFileIdsByOwnerAsync` returns `IReadOnlyDictionary<Guid, Guid>` — a missing key means no avatar, so `GetValueOrDefault` yields `Guid.Empty`; the ternary converts that back to `null`.) Change the constructor to inject `IEntityAssetRepository entityAssets` instead of `IFileStorageService fileStorage`, remove the `AvatarUrlExpiry` constant.

- [ ] **Step 5: Point `GetMyProfile` at `entity_assets` too**

In `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/MyProfileResponse.cs`, `AvatarFileId` on `MyPersonalInformationResponse` stays `Guid?` — no change needed there.

In `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetMyProfile/GetMyProfileQueryHandler.cs`, inject `IEntityAssetRepository entityAssets`, and replace `employee.AvatarFileId` (Task 2 removed this property) in the `MyPersonalInformationResponse` construction with a resolved lookup:

```csharp
        var avatarFileIdByEmployeeId = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Employee, new[] { employee.Id }, UploadPurposeCatalog.EmployeeAvatar, ct);
        var avatarFileId = avatarFileIdByEmployeeId.TryGetValue(employee.Id, out var fid) ? (Guid?)fid : null;
```

and use `avatarFileId` in place of `employee.AvatarFileId` in the `MyPersonalInformationResponse` constructor call. Add the same two `using`s as Step 3.

- [ ] **Step 6: Fix the now-broken tests**

In `GetMyProfileQueryHandlerTests.cs`, `ListEmployeesQueryHandlerTests.cs`, `GetEmployeeQueryHandlerTests.cs`: remove every `AvatarFileId = ...` set directly on an `Employee` object initializer (the entity no longer has that property — Task 2), add a `Mock<IEntityAssetRepository>` field to each test class in place of the `Mock<IFileStorageService>` field added earlier this session, and update the avatar-specific tests added earlier this session (`Handle_ReturnsAvatarFileId_WhenEmployeeHasOneSet`, `Handle_ResolvesAvatarUrl_ForRowsWithAnAvatarFileId...`, `Handle_ResolvesAvatarUrl_WhenEmployeeHasAnAvatarFileId`) to instead mock `_entityAssets.Setup(r => r.GetPrimaryFileIdsByOwnerAsync(...))` returning a dictionary containing the test's employee id, and assert on `result.Value!.AvatarFileId` (or `.PersonalInformation.AvatarFileId`) equaling that file id directly — no signed-URL string assertion anymore.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CoreHr.Employee"`
Expected: PASS, all green.

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj 2>&1 | grep -i error`
Expected: only Task 7's `GetObjectiveMembersQueryHandler`/`CallerIdentityResolver` errors remain (not yet fixed) — proceed to Task 7.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: resolve employee avatars from entity_assets instead of a denormalized column"
```

---

### Task 7: Point `CallerIdentityResolver`/`GetObjectiveMembersQueryHandler` at `entity_assets`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Common/Services/CallerIdentityResolver.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveMemberItemResponse.cs`
- Test: update `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveMembersQueryHandlerTests.cs`

**Interfaces:**
- Produces: `ObjectiveMemberItemResponse.AvatarFileId` (renamed from `AvatarUrl`) — the frontend plan's `task-form-modal`/`member-management-popup` build the display URL via `getFileUrl(fileId)` instead of consuming a pre-resolved signed URL.

- [ ] **Step 1: Fix `EmployeeIdentityDto` construction in `CallerIdentityResolver`**

In `src/ONEVO.Application/Features/WorkManagement/Common/Services/CallerIdentityResolver.cs`, `ResolveIdentitiesByEmployeeIdAsync` currently builds `new EmployeeIdentityDto($"{employee.FirstName} {employee.LastName}", employee.AvatarFileId)` — `employee.AvatarFileId` no longer exists (Task 2). Inject `IEntityAssetRepository entityAssets` into this class, and batch-resolve avatars for the whole `employeeIds` list up front:

```csharp
    public async Task<IReadOnlyDictionary<Guid, EmployeeIdentityDto>> ResolveIdentitiesByEmployeeIdAsync(
        Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
    {
        var distinctIds = employeeIds.Distinct().ToList();
        var avatarFileIdByEmployeeId = await _entityAssets.GetPrimaryFileIdsByOwnerAsync(
            tenantId, EntityAssetOwnerTypes.Employee, distinctIds, UploadPurposeCatalog.EmployeeAvatar, ct);

        var result = new Dictionary<Guid, EmployeeIdentityDto>();
        foreach (var employeeId in distinctIds)
        {
            var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
            if (employee is not null)
            {
                var avatarFileId = avatarFileIdByEmployeeId.TryGetValue(employeeId, out var fid) ? (Guid?)fid : null;
                result[employeeId] = new EmployeeIdentityDto($"{employee.FirstName} {employee.LastName}", avatarFileId);
            }
        }
        return result;
    }
```

Add `using ONEVO.Application.Common.Constants;`, `using ONEVO.Application.Common.RepositoryInterfaces;`, `using ONEVO.Application.Features.Storage.File.Helpers;` to this file.

- [ ] **Step 2: Simplify `ObjectiveMemberItemResponse` and its handler**

In `ObjectiveMemberItemResponse.cs`, rename `AvatarUrl` back to `AvatarFileId` (`Guid?`).

In `GetObjectiveMembersQueryHandler.cs`, remove the `IFileStorageService`/`GetSignedUrlAsync`-based resolution block added earlier this session (the `AvatarUrlExpiry` constant, the `avatarUrlByEmployeeId` loop, the `_fileStorage` field/constructor param) — `EmployeeIdentityDto.AvatarFileId` from `ResolveIdentitiesByEmployeeIdAsync` (Step 1, already resolved via `entity_assets`) is now used directly:

```csharp
        var items = new List<ObjectiveMemberItemResponse>();
        items.AddRange(activeMembers.Select(m => new ObjectiveMemberItemResponse(
            m.EmployeeId, NameOf(m.EmployeeId), AvatarFileIdOf(m.EmployeeId), IsHead: m.EmployeeId == objective.OwnerId,
            Pending: false, InviteType: null, InvitationId: null, SinceOrInvitedAt: m.JoinedAt)));
        items.AddRange(pendingInvites.Select(i => new ObjectiveMemberItemResponse(
            i.InvitedEmployeeId, NameOf(i.InvitedEmployeeId), AvatarFileIdOf(i.InvitedEmployeeId), IsHead: false,
            Pending: true, InviteType: i.InviteType, InvitationId: i.Id, SinceOrInvitedAt: i.CreatedAt)));

        return Result<ObjectiveMemberListResponse>.Success(new ObjectiveMemberListResponse(items));

        Guid? AvatarFileIdOf(Guid employeeId) => identitiesByEmployeeId.GetValueOrDefault(employeeId)?.AvatarFileId;
```

(`NameOf` local function already exists from earlier this session — keep it. `AvatarFileIdOf` is a new local function alongside it, defined after the `return` per C# local-function ordering rules, matching `NameOf`'s existing placement.)

- [ ] **Step 3: Fix the tests**

In `GetObjectiveMembersQueryHandlerTests.cs`: the `identity.Setup(x => x.ResolveIdentitiesByEmployeeIdAsync(...))` mocks already return `EmployeeIdentityDto` objects directly (unaffected by Step 1's internal change, since the interface signature is unchanged) — no change needed there. Remove the `Mock<IFileStorageService> fileStorage` field, its `GetSignedUrlAsync` setup, and the `fileStorage.Object` constructor argument added earlier this session (the handler no longer takes `IFileStorageService`). Update `Handle_ResolvesAvatarUrlForMembersWithAnAvatarFileId_AndNullForThoseWithout` to assert on `i.AvatarFileId == avatarFileId` directly (the `EmployeeIdentityDto`'s own `AvatarFileId`, no signed-URL string) instead of a resolved URL string.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetObjectiveMembersQueryHandlerTests"`
Expected: PASS, all green.

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj 2>&1 | grep -i error`
Expected: 0 errors — every reference Task 2 Step 2 found is now fixed.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS, full suite green (verifies nothing else in the codebase silently depended on the deleted/renamed pieces).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: resolve objective-member avatars from entity_assets instead of a signed R2 URL"
```

## Self-Review Notes

- Spec coverage: this plan implements the full backend half of "full centralization" for Employee + Legal Entity specifically — the two owner types actually blocking the currently-visible bug. Project/Objective/Task file *endpoints* are intentionally **not** migrated to the new generic controller in this plan (their existing endpoints keep working unchanged); that migration, plus the `monitoring_face_scans` string-duplication fix, is Part 2 — a separate plan, written when this one ships, per this repo's own `FILE_CREATION_RULES.md` (split by independent task, not by line count).
- No placeholders: every step has real, compilable code.
- Type consistency checked: `AvatarFileId` is `Guid?` everywhere across this plan (response records, handler locals, test assertions) — never conflated with a URL string, which is entirely the frontend's responsibility now via `getFileUrl(fileId)`.
- This plan explicitly **deletes** old code (Task 5) rather than leaving it running alongside the new endpoint, per the user's explicit instruction.
