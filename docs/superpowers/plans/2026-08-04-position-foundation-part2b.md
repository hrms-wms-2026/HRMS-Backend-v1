# Position Foundation Part 2B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the application-layer surface for Position management (commands, queries, validators, response DTOs, mappers, repository additions, and API request contracts) in `C:\onevoNew\HRMS-Backend-v1`, matching the corrected Department/Position user journey, without exposing HTTP endpoints, controllers, or migrations.

**Architecture:** MediatR CQRS, mirroring the existing Department Part 2B pattern exactly: `record` commands/queries implementing `IRequest<Result<T>>`, `IRequestHandler<TCommand, Result<T>>` classes constructor-injecting repository interfaces + `ICurrentUser` + `IDateTimeProvider`, FluentValidation `AbstractValidator<T>` per command/query, static mapper classes with zero DB access, and a static `*ArchiveDependencyEvaluator` service for blocker counting. All new Application-layer types physically live under `Position/...` subfolders but their **namespaces stop at `OrgStructure`** (never contain a `.Position.` segment) to keep `PositionPart2AArchitectureTests.PositionPart2A_DoesNotExpose_Controllers_Commands_Queries_Or_RequestContracts` passing unchanged.

**Tech Stack:** .NET (C#), MediatR 14.1.0, FluentValidation 12.1.1 + FluentValidation.DependencyInjectionExtensions, EF Core 10.0.9 (Npgsql), xUnit 2.9.3, Moq.

## Global Constraints

- Work only inside `C:\onevoNew\HRMS-Backend-v1`. Do not touch OneVo-HR docs, frontend, Postman, migrations/schema, Department schema, Legal Entity schema, Auth/System Config code, or add any Position controller/routes.
- **Namespace convention (load-bearing, verified against source):** physically create files under `src/ONEVO.Application/Features/OrgStructure/Position/{Commands,Queries,Responses,Mappers,Services,RepositoryInterfaces}/...` exactly as directed per task, but declare namespaces as follows (all confirmed by reading the existing Department/LegalEntity/Position files, which document this exact rationale inline as "a `.Department`/`.Position` segment would collide with the entity type and force using-aliases everywhere"):
  - Commands: `ONEVO.Application.Features.OrgStructure.Commands.<CommandFolderName>` (e.g. `...Commands.CreatePosition`)
  - Queries: `ONEVO.Application.Features.OrgStructure.Queries.<QueryFolderName>` (e.g. `...Queries.ListPositions`)
  - Response DTOs: `ONEVO.Application.Features.OrgStructure.DTOs.Responses` (same namespace Department's response DTOs already use — this is intentional and safe; type names never collide)
  - Mappers: `ONEVO.Application.Features.OrgStructure.Mappers`
  - Services (archive evaluator): `ONEVO.Application.Features.OrgStructure.Services`
  - `PositionPage` (repository-level paged result): `ONEVO.Application.Features.OrgStructure.RepositoryInterfaces` (same namespace `IPositionRepository` already uses)
  - Api Contracts: `ONEVO.Api.Contracts.OrgStructure.Positions`
- **tenantId is never accepted from any request contract, command constructor argument populated from a client, or query string.** It always comes from `ICurrentUser.TenantId` inside the handler, exactly like every existing Department handler.
- **legalEntityId is never accepted in an `ONEVO.Api.Contracts.OrgStructure.Positions` request body.** It is a property on the MediatR command/query (to be populated from the route by a future controller, out of scope here), never a request-contract property.
- **No C# enum is introduced anywhere under `src/ONEVO.Application/Features/OrgStructure/Position` or `src/ONEVO.Api/Contracts/OrgStructure/Positions`** for position type, sort field, sort direction, or status. Unlike Department (which uses `DepartmentSortBy`/`SortDirection` enums internally), Position's `sortBy`/`sortDirection` stay plain, lowercase-normalized `string` values end-to-end (validator allowlist → handler → repository `ApplySort` string switch). This is a deliberate deviation from the Department pattern, required because the verification script greps `src/ONEVO.Application/Features/OrgStructure/Position` and `src/ONEVO.Api/Contracts/OrgStructure/Positions` for `SortDirection`/`PositionSort`/`enum .*Position` and expects zero matches.
- Never inject `ApplicationDbContext` into a handler — only repository interfaces.
- Never use `DateTimeOffset.UtcNow`/`DateTime.UtcNow` directly in the Application layer — always `IDateTimeProvider.UtcNow`.
- Never introduce a `?? Guid.Empty` fallback or `LegalEntityIdValue`/`DepartmentIdValue` helper identifiers anywhere under the Position Domain/Application/Infrastructure surface (scanned by `PositionSurface_DoesNotReintroduce_FakeGuidEmptyIdHelpers`, which recursively scans `Application/Features/OrgStructure/Position` — i.e. every file this plan creates).
- Do not create, modify, or reference security roles, permission codes, or role-creation fields anywhere in Position code.
- Do not implement occupant assignment, `position_assignments`, or access approval. No such table/entity exists anywhere in `src/` (verified by repo-wide grep) — `CheckPositionArchiveCommand`'s active-occupant count must be reported as `null` with `ActiveOccupantsCheckSupported = false`, never a fabricated `0`.
- Do not add a Department `HeadPositionId` mutation path anywhere in Position commands/contracts.
- Do not modify `PositionPart2AArchitectureTests.cs`, `PositionConfiguration.cs`, `Position.cs` (domain entity), any migration, or any Department file.
- Existing `IPositionRepository` methods (`AddAsync`, `Update`, `GetByIdAsync` (both overloads), `GetByIdForLegalEntityAsync`, `ListByLegalEntityAsync`, `ExistsByCodeAsync`, `ExistsByNameAsync`, `ExistsInDepartmentAsync`, `IsDescendantAsync`, `CountActiveByDepartmentAsync`, `CountActiveReportsToPositionAsync`, `AddReportingHistoryAsync`, `AddManagementCoverageRecordAsync`, `GetCurrentReportingHistoryAsync`, `GetLockedReportingStructureCoverageAsync`, `SaveChangesAsync`) must be reused as-is, never re-implemented or signature-changed.
- `Position.LegalEntityId`/`DepartmentId` are `Guid?` in the domain entity (transitional nullability for legacy rows) — never change this. Response DTOs expose `LegalEntityId` as non-nullable `Guid` (safe: every code path that constructs a response DTO does so from a `Position` already fetched through a `legalEntityId`-filtered repository call, so the value is guaranteed non-null at that point — use `entity.LegalEntityId!.Value` with a one-line comment explaining why). `DepartmentId` stays `Guid?` in every response DTO (legacy rows can genuinely have no department).
- No new methods are needed on `IDepartmentRepository`/`ILegalEntityRepository` — reuse `GetByIdForTenantAsync` (LegalEntity) and `GetByIdForLegalEntityAsync` (Department) exactly as Department's own handlers do.
- No DI registration changes are expected (MediatR and FluentValidation.DependencyInjectionExtensions auto-scan assemblies for handlers/validators — Department's Part 2B needed none). If a task's build/test step reveals otherwise, note it in that task's report rather than silently editing `Program.cs`.
- Every new `.cs` file must be ASCII-only (no smart quotes/em-dashes) — verified by `rg -n "[^\x00-\x7F]"` in the final verification task.
- Block-bodied methods only in `EfPositionRepository.cs` (no `=>` expression-bodied members) — enforced by `EfPositionRepository_HasNoExpressionBodiedMembers`.

---

## File Structure

```
src/ONEVO.Application/Features/OrgStructure/Position/
  Responses/
    PositionResponse.cs
    PositionListItemResponse.cs
    PositionTreeNodeResponse.cs
    PositionPageResponse.cs
    PositionArchiveBlockers.cs
  Mappers/
    PositionMapper.cs
    PositionTreeMapper.cs
  RepositoryInterfaces/
    PositionPage.cs                          (IPositionRepository.cs already exists here - only add to it)
  Services/
    PositionArchiveDependencyEvaluator.cs
  Queries/
    GetPositionById/
      GetPositionByIdQuery.cs
      GetPositionByIdQueryValidator.cs
      GetPositionByIdQueryHandler.cs
    ListPositions/
      ListPositionsQuery.cs
      ListPositionsQueryValidator.cs
      ListPositionsQueryHandler.cs
    GetPositionTree/
      GetPositionTreeQuery.cs
      GetPositionTreeQueryValidator.cs
      GetPositionTreeQueryHandler.cs
  Commands/
    CreatePosition/
      CreatePositionCommand.cs
      CreatePositionCommandValidator.cs
      CreatePositionCommandHandler.cs
    UpdatePosition/
      UpdatePositionCommand.cs
      UpdatePositionCommandValidator.cs
      UpdatePositionCommandHandler.cs
    ArchivePosition/
      ArchivePositionCommand.cs
      ArchivePositionCommandValidator.cs
      ArchivePositionCommandHandler.cs
    RestorePosition/
      RestorePositionCommand.cs
      RestorePositionCommandValidator.cs
      RestorePositionCommandHandler.cs
    CheckPositionArchive/
      CheckPositionArchiveCommand.cs
      CheckPositionArchiveCommandValidator.cs
      CheckPositionArchiveCommandHandler.cs

src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs   (extend, do not replace)

src/ONEVO.Api/Contracts/OrgStructure/Positions/
  CreatePositionRequest.cs
  UpdatePositionRequest.cs

tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/
  EfPositionRepositoryTests.cs                (extend if created by Part 2A, else create)
  PositionTreeMapperTests.cs
  GetPositionByIdQueryHandlerTests.cs
  ListPositionsQueryHandlerTests.cs
  GetPositionTreeQueryHandlerTests.cs
  CreatePositionCommandHandlerTests.cs
  UpdatePositionCommandHandlerTests.cs
  ArchiveRestoreCheckPositionCommandHandlerTests.cs

tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs

POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md   (repo root)
```

---

### Task 1: Response DTOs and repository-level PositionPage

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/PositionResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/PositionListItemResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/PositionTreeNodeResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/PositionPageResponse.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Responses/PositionArchiveBlockers.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/PositionPage.cs`

**Interfaces:**
- Consumes: `ONEVO.Domain.Features.OrgStructure.Entities.Position` (existing entity, read-only reference in `PositionPage.cs`).
- Produces: the 5 response record types below (namespace `ONEVO.Application.Features.OrgStructure.DTOs.Responses`) and `PositionPage` (namespace `ONEVO.Application.Features.OrgStructure.RepositoryInterfaces`) — every later task consumes these exact shapes.

- [ ] **Step 1: Create `PositionResponse.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? DepartmentName,
    string? ReportsToPositionName,
    int ChildCount);
```

- [ ] **Step 2: Create `PositionListItemResponse.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// Deliberately omits DepartmentName/ReportsToPositionName/ChildCount: populating them per
// row would require an extra query per row (N+1) in ListPositionsQueryHandler. PositionResponse
// (single-item GetPositionByIdQuery) and PositionTreeNodeResponse (already has the full set
// loaded in memory) populate the richer fields cheaply; the paginated list does not.
public record PositionListItemResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
```

- [ ] **Step 3: Create `PositionTreeNodeResponse.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionTreeNodeResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    int ChildCount,
    IReadOnlyList<PositionTreeNodeResponse> Children);
```

- [ ] **Step 4: Create `PositionPageResponse.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionPageResponse(
    IReadOnlyList<PositionListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
```

- [ ] **Step 5: Create `PositionArchiveBlockers.cs`**

```csharp
namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// ActiveOccupants is nullable, not defaulted to 0: no position_assignments/employee-position
// table exists anywhere in this codebase (confirmed by a repo-wide search), so the count is
// genuinely unmeasurable today, not zero. ActiveOccupantsCheckSupported=false documents that
// limitation explicitly rather than faking a verified zero. CanArchive intentionally excludes
// ActiveOccupants from the gate for the same reason: an unverifiable count cannot block anything.
public record PositionArchiveBlockers(
    int? ActiveOccupants,
    bool ActiveOccupantsCheckSupported,
    int HeadOfDepartments,
    int ActiveChildPositions)
{
    public bool CanArchive => HeadOfDepartments == 0 && ActiveChildPositions == 0;
}
```

- [ ] **Step 6: Create `PositionPage.cs`**

```csharp
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public sealed record PositionPage(
    IReadOnlyList<Position> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
```

- [ ] **Step 7: Build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj --no-restore --verbosity minimal`
Expected: build succeeds (these are pure data records with no other dependencies).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Responses src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/PositionPage.cs
git commit -m "feat(position): add Part 2B response DTOs and PositionPage"
```

---

### Task 2: Mappers

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Mappers/PositionMapper.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Mappers/PositionTreeMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/PositionTreeMapperTests.cs`

**Interfaces:**
- Consumes: `PositionResponse`, `PositionListItemResponse`, `PositionTreeNodeResponse` (Task 1); `ONEVO.Domain.Features.OrgStructure.Entities.Position` (existing).
- Produces: `PositionMapper.ToResponse(Position, string?, string?, int)`, `PositionMapper.ToListItemResponse(Position)`, `PositionTreeMapper.BuildTree(IReadOnlyList<Position>)` — every query/command handler task below calls these.

- [ ] **Step 1: Create `PositionMapper.cs`**

```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionMapper
{
    public static PositionResponse ToResponse(
        Position entity, string? departmentName, string? reportsToPositionName, int childCount)
    {
        return new PositionResponse(
            entity.Id,
            entity.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            entity.DepartmentId,
            entity.Name,
            entity.Code,
            entity.PositionType,
            entity.MaxOccupancy,
            entity.ReportsToPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt,
            departmentName,
            reportsToPositionName,
            childCount);
    }

    public static PositionListItemResponse ToListItemResponse(Position entity)
    {
        return new PositionListItemResponse(
            entity.Id,
            entity.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            entity.DepartmentId,
            entity.Name,
            entity.Code,
            entity.PositionType,
            entity.MaxOccupancy,
            entity.ReportsToPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}
```

- [ ] **Step 2: Create `PositionTreeMapper.cs`**

```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionTreeMapper
{
    public static IReadOnlyList<PositionTreeNodeResponse> BuildTree(IReadOnlyList<Position> positions)
    {
        var idsInSet = positions.Select(position => position.Id).ToHashSet();

        var childrenByParentId = positions
            .Where(position => position.ReportsToPositionId is not null
                && idsInSet.Contains(position.ReportsToPositionId.Value))
            .GroupBy(position => position.ReportsToPositionId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(position => position.Name).ToList());

        var roots = positions
            .Where(position => position.ReportsToPositionId is null
                || !idsInSet.Contains(position.ReportsToPositionId.Value))
            .OrderBy(position => position.Name)
            .ToList();

        return roots.Select(root => BuildNode(root, childrenByParentId)).ToList();
    }

    private static PositionTreeNodeResponse BuildNode(
        Position position, IReadOnlyDictionary<Guid, List<Position>> childrenByParentId)
    {
        var childEntities = childrenByParentId.TryGetValue(position.Id, out var children)
            ? children
            : new List<Position>();

        var childNodes = childEntities.Select(child => BuildNode(child, childrenByParentId)).ToList();

        return new PositionTreeNodeResponse(
            position.Id,
            position.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            position.DepartmentId,
            position.Name,
            position.Code,
            position.PositionType,
            position.MaxOccupancy,
            position.ReportsToPositionId,
            position.IsActive,
            childNodes.Count,
            childNodes);
    }
}
```

- [ ] **Step 3: Write failing tests for `PositionTreeMapper`**

```csharp
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class PositionTreeMapperTests
{
    [Fact]
    public void BuildTree_NestsChildrenUnderReportsToPositionId()
    {
        var legalEntityId = Guid.NewGuid();
        var root = CreatePosition(legalEntityId, "CEO", reportsToPositionId: null);
        var child = CreatePosition(legalEntityId, "VP Sales", reportsToPositionId: root.Id);
        var grandchild = CreatePosition(legalEntityId, "Sales Manager", reportsToPositionId: child.Id);

        var tree = PositionTreeMapper.BuildTree([root, child, grandchild]);

        Assert.Single(tree);
        Assert.Equal("CEO", tree[0].Name);
        Assert.Equal(1, tree[0].ChildCount);
        Assert.Single(tree[0].Children);
        Assert.Equal("VP Sales", tree[0].Children[0].Name);
        Assert.Single(tree[0].Children[0].Children);
        Assert.Equal("Sales Manager", tree[0].Children[0].Children[0].Name);
        Assert.Empty(tree[0].Children[0].Children[0].Children);
    }

    [Fact]
    public void BuildTree_TreatsOrphanedReportsToAsRoot()
    {
        var legalEntityId = Guid.NewGuid();
        var missingParentId = Guid.NewGuid();
        var orphan = CreatePosition(legalEntityId, "Orphan Manager", reportsToPositionId: missingParentId);

        var tree = PositionTreeMapper.BuildTree([orphan]);

        Assert.Single(tree);
        Assert.Equal("Orphan Manager", tree[0].Name);
    }

    [Fact]
    public void BuildTree_OrdersSiblingsByName()
    {
        var legalEntityId = Guid.NewGuid();
        var zeta = CreatePosition(legalEntityId, "Zeta Lead", reportsToPositionId: null);
        var alpha = CreatePosition(legalEntityId, "Alpha Lead", reportsToPositionId: null);

        var tree = PositionTreeMapper.BuildTree([zeta, alpha]);

        Assert.Equal(2, tree.Count);
        Assert.Equal("Alpha Lead", tree[0].Name);
        Assert.Equal("Zeta Lead", tree[1].Name);
    }

    private static ONEVO.Domain.Features.OrgStructure.Entities.Position CreatePosition(
        Guid legalEntityId, string name, Guid? reportsToPositionId)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LegalEntityId = legalEntityId,
            Name = name,
            Code = name.Replace(" ", "-").ToUpperInvariant(),
            ReportsToPositionId = reportsToPositionId,
            IsActive = true
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~PositionTreeMapperTests" --verbosity minimal`
Expected: 3/3 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Mappers tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/PositionTreeMapperTests.cs
git commit -m "feat(position): add PositionMapper and PositionTreeMapper"
```

---

### Task 3: Repository additions — `ListPageAsync` and `CountHeadDepartmentReferencesAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs` (append 2 method signatures — do not touch any existing signature)
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs` (append 2 implementations + 1 private `ApplySort` helper — do not touch any existing method)
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs` (create — Part 2A did not add this file; confirm with `ls tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/` first, and if it already exists, append to it instead of overwriting)

**Interfaces:**
- Consumes: `PositionPage` (Task 1).
- Produces: `IPositionRepository.ListPageAsync(...)` and `IPositionRepository.CountHeadDepartmentReferencesAsync(...)` — consumed by Task 5 (List) and Task 9 (Archive/Restore/Check) respectively.

- [ ] **Step 1: Read the existing repository test pattern**

Read `tests/ONEVO.Tests.Unit/Features/OrgStructure/Department/EfDepartmentRepositoryTests.cs` in full before writing tests here — it defines `BuildInMemoryDb()` (constructs `ApplicationDbContext` with `UseInMemoryDatabase`, mocked `ICurrentUser`/`IDateTimeProvider`/`IPublisher`/`ITenantContext`, and the real `AuditableEntityInterceptor`/`SoftDeleteInterceptor`/`DomainEventDispatchInterceptor`). Mirror that exact helper shape below — it is reproduced in Step 4.

- [ ] **Step 2: Append to `IPositionRepository.cs`**

Add these two members inside the existing `interface IPositionRepository { ... }` body, immediately after `CountActiveReportsToPositionAsync`, before the `// Ancillary reporting & coverage helpers` comment:

```csharp
    Task<PositionPage> ListPageAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid? departmentId,
        string? search,
        bool includeInactive,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<int> CountHeadDepartmentReferencesAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default);
```

Do not add a `using` for `PositionPage` — it is already in the same namespace (`ONEVO.Application.Features.OrgStructure.RepositoryInterfaces`) as `IPositionRepository`.

- [ ] **Step 3: Append to `EfPositionRepository.cs`**

Add these members immediately after `CountActiveReportsToPositionAsync`, before `AddReportingHistoryAsync`. `sortBy`/`sortDirection` arrive already trimmed and lowercased by `ListPositionsQueryHandler` (Task 5) — this method does not re-normalize them:

```csharp
    public async Task<PositionPage> ListPageAsync(
        Guid tenantId,
        Guid legalEntityId,
        Guid? departmentId,
        string? search,
        bool includeInactive,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Positions
            .AsNoTracking()
            .Where(position => position.TenantId == tenantId && position.LegalEntityId == legalEntityId);

        if (!includeInactive)
        {
            query = query.Where(position => position.IsActive);
        }

        if (departmentId is not null)
        {
            query = query.Where(position => position.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(position =>
                position.Name.ToLower().Contains(normalizedSearch)
                || (position.Code != null && position.Code.ToLower().Contains(normalizedSearch)));
        }

        query = ApplySort(query, sortBy, sortDirection);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PositionPage(items, totalCount, page, pageSize, totalPages);
    }

    private static IQueryable<Position> ApplySort(IQueryable<Position> query, string sortBy, string sortDirection)
    {
        var ascending = sortDirection == "asc";

        return sortBy switch
        {
            "code" => ascending
                ? query.OrderBy(position => position.Code).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.Code).ThenBy(position => position.Id),
            "department" => ascending
                ? query.OrderBy(position => position.DepartmentId).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.DepartmentId).ThenBy(position => position.Id),
            "reportsto" => ascending
                ? query.OrderBy(position => position.ReportsToPositionId).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.ReportsToPositionId).ThenBy(position => position.Id),
            "type" => ascending
                ? query.OrderBy(position => position.PositionType).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.PositionType).ThenBy(position => position.Id),
            "capacity" => ascending
                ? query.OrderBy(position => position.MaxOccupancy).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.MaxOccupancy).ThenBy(position => position.Id),
            "status" => ascending
                ? query.OrderBy(position => position.IsActive).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.IsActive).ThenBy(position => position.Id),
            "createdat" => ascending
                ? query.OrderBy(position => position.CreatedAt).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.CreatedAt).ThenBy(position => position.Id),
            "updatedat" => ascending
                ? query.OrderBy(position => position.UpdatedAt).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.UpdatedAt).ThenBy(position => position.Id),
            _ => ascending
                ? query.OrderBy(position => position.Name).ThenBy(position => position.Id)
                : query.OrderByDescending(position => position.Name).ThenBy(position => position.Id),
        };
    }

    public async Task<int> CountHeadDepartmentReferencesAsync(
        Guid tenantId, Guid legalEntityId, Guid positionId, CancellationToken ct = default)
    {
        var count = await _db.Departments
            .AsNoTracking()
            .Where(department =>
                department.TenantId == tenantId
                && department.LegalEntityId == legalEntityId
                && department.HeadPositionId == positionId)
            .CountAsync(ct);

        return count;
    }
```

- [ ] **Step 4: Write failing tests in `EfPositionRepositoryTests.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class EfPositionRepositoryTests
{
    [Fact]
    public async Task ListPageAsync_FiltersByTenantAndLegalEntity_AndReturnsPaginationMetadata()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "Alpha", "ALPHA"),
            CreatePosition(tenantId, legalEntityId, "Beta", "BETA"),
            CreatePosition(tenantId, otherLegalEntityId, "Other LE", "OTHERLE"),
            CreatePosition(otherTenantId, legalEntityId, "Other Tenant", "OTHERTEN"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId: null, search: null, includeInactive: false,
            sortBy: "name", sortDirection: "asc", page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal("Alpha", page.Items[0].Name);
        Assert.Equal("Beta", page.Items[1].Name);
    }

    [Fact]
    public async Task ListPageAsync_SkipsAndTakesForSecondPage()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            db.Positions.Add(CreatePosition(tenantId, legalEntityId, $"Position {i:00}", $"POS{i:00}"));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId: null, search: null, includeInactive: false,
            sortBy: "name", sortDirection: "asc", page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal("Position 02", page.Items[0].Name);
        Assert.Equal("Position 03", page.Items[1].Name);
    }

    [Fact]
    public async Task ListPageAsync_FiltersInactiveRowsUnlessIncluded()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var inactive = CreatePosition(tenantId, legalEntityId, "Retired", "RETIRED");
        inactive.IsActive = false;
        db.Positions.AddRange(CreatePosition(tenantId, legalEntityId, "Active", "ACTIVE"), inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var activeOnly = await repository.ListPageAsync(
            tenantId, legalEntityId, null, null, includeInactive: false,
            "name", "asc", 1, 10, CancellationToken.None);
        var allRows = await repository.ListPageAsync(
            tenantId, legalEntityId, null, null, includeInactive: true,
            "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(activeOnly.Items);
        Assert.Equal(2, allRows.TotalCount);
    }

    [Fact]
    public async Task ListPageAsync_FiltersBySearchAcrossNameAndCode()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "Finance Manager", "FIN-MGR"),
            CreatePosition(tenantId, legalEntityId, "Sales Lead", "SALES-LEAD"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var byName = await repository.ListPageAsync(
            tenantId, legalEntityId, null, "finance", false, "name", "asc", 1, 10, CancellationToken.None);
        var byCode = await repository.ListPageAsync(
            tenantId, legalEntityId, null, "sales-lead", false, "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(byName.Items);
        Assert.Equal("Finance Manager", byName.Items[0].Name);
        Assert.Single(byCode.Items);
        Assert.Equal("Sales Lead", byCode.Items[0].Name);
    }

    [Fact]
    public async Task ListPageAsync_FiltersByDepartmentId()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var inDept = CreatePosition(tenantId, legalEntityId, "In Dept", "INDEPT");
        inDept.DepartmentId = departmentId;
        db.Positions.AddRange(inDept, CreatePosition(tenantId, legalEntityId, "No Dept", "NODEPT"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var page = await repository.ListPageAsync(
            tenantId, legalEntityId, departmentId, null, false, "name", "asc", 1, 10, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("In Dept", page.Items[0].Name);
    }

    [Fact]
    public async Task CountHeadDepartmentReferencesAsync_CountsDepartmentsReferencingThisPositionAsHead()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var position = CreatePosition(tenantId, legalEntityId, "Head Candidate", "HEAD");
        db.Positions.Add(position);
        db.Departments.Add(new ONEVO.Domain.Features.OrgStructure.Entities.Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Operations",
            HeadPositionId = position.Id,
            IsActive = true
        });
        db.Departments.Add(new ONEVO.Domain.Features.OrgStructure.Entities.Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = "Sales",
            HeadPositionId = null,
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var count = await repository.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, position.Id, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CountHeadDepartmentReferencesAsync_ReturnsZero_WhenNoDepartmentReferencesPosition()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var position = CreatePosition(tenantId, legalEntityId, "Not A Head", "NOTHEAD");
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var count = await repository.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, position.Id, CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExistsByCodeAsync_MatchesCaseInsensitively()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        db.Positions.Add(CreatePosition(tenantId, legalEntityId, "Finance Manager", "CS-MGR"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var exists = await repository.ExistsByCodeAsync(
            tenantId, legalEntityId, "cs-mgr", excludingPositionId: null, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task ListByLegalEntityAsync_ExcludesOtherTenantAndOtherLegalEntityPositions()
    {
        // GetPositionTreeQueryHandler builds the tree directly from this method's output, so
        // proving cross-tenant/cross-legal-entity exclusion here is what actually discharges
        // "tree excludes cross-company positions" - a handler test with a hand-built mock list
        // cannot prove the repository-level filter is correct.
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Positions.AddRange(
            CreatePosition(tenantId, legalEntityId, "In Scope", "INSCOPE"),
            CreatePosition(tenantId, otherLegalEntityId, "Other Legal Entity", "OTHERLE"),
            CreatePosition(otherTenantId, legalEntityId, "Other Tenant", "OTHERTEN"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfPositionRepository(db);

        var results = await repository.ListByLegalEntityAsync(
            tenantId, legalEntityId, includeInactive: false, departmentId: null, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("In Scope", results[0].Name);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }

    private static ONEVO.Domain.Features.OrgStructure.Entities.Position CreatePosition(
        Guid tenantId, Guid legalEntityId, string name, string code)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            Code = code,
            IsActive = true
        };
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~EfPositionRepositoryTests" --verbosity minimal`
Expected: 9/9 PASS.

- [ ] **Step 6: Build the Infrastructure and Architecture projects to confirm no expression-bodied member was introduced**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --no-restore --verbosity minimal`
Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --filter "FullyQualifiedName~PositionPart2AArchitectureTests" --verbosity minimal`
Expected: both succeed; all `PositionPart2AArchitectureTests` still pass (this confirms the new repository methods did not violate the block-bodied-member rule or the tenantId-first-parameter rule).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/EfPositionRepositoryTests.cs
git commit -m "feat(position): add ListPageAsync and CountHeadDepartmentReferencesAsync to IPositionRepository"
```

---

### Task 4: GetPositionByIdQuery

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionById/GetPositionByIdQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionById/GetPositionByIdQueryValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionById/GetPositionByIdQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `PositionResponse`, `PositionMapper.ToResponse` (Tasks 1-2); `IPositionRepository.GetByIdForLegalEntityAsync`, `CountActiveReportsToPositionAsync` (existing); `IDepartmentRepository.GetByIdForLegalEntityAsync` (existing); `ILegalEntityRepository.GetByIdForTenantAsync` (existing); `ICurrentUser`, `Result<T>`.
- Produces: `GetPositionByIdQuery(Guid LegalEntityId, Guid PositionId) : IRequest<Result<PositionResponse>>` — a later controller (out of scope) will dispatch this.

- [ ] **Step 1: Create `GetPositionByIdQuery.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;

public record GetPositionByIdQuery(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<PositionResponse>>;
```

- [ ] **Step 2: Create `GetPositionByIdQueryValidator.cs`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;

public class GetPositionByIdQueryValidator : AbstractValidator<GetPositionByIdQuery>
{
    public GetPositionByIdQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
```

- [ ] **Step 3: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class GetPositionByIdQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetPositionByIdQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private GetPositionByIdQueryHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPositionDoesNotExist()
    {
        var positionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.OrgStructure.Entities.Position?)null);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsPositionWithDepartmentAndReportsToNames_WhenBothPresent()
    {
        var departmentId = Guid.NewGuid();
        var reportsToId = Guid.NewGuid();
        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            DepartmentId = departmentId,
            ReportsToPositionId = reportsToId,
            Name = "Customer Support Manager",
            Code = "CS-MGR",
            IsActive = true
        };
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Customer Support", IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = reportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Operations Manager", IsActive = true });
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, position.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support", result.Value!.DepartmentName);
        Assert.Equal("Operations Manager", result.Value.ReportsToPositionName);
        Assert.Equal(2, result.Value.ChildCount);
    }

    [Fact]
    public async Task Handle_ReturnsNullNames_WhenDepartmentAndReportsToAreAbsent()
    {
        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            DepartmentId = null,
            ReportsToPositionId = null,
            Name = "Founder",
            Code = "FOUNDER",
            IsActive = true
        };
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, position.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DepartmentName);
        Assert.Null(result.Value.ReportsToPositionName);
        _departmentsMock.Verify(
            d => d.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail (handler does not exist yet)**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPositionByIdQueryHandlerTests" --verbosity minimal`
Expected: build error (`GetPositionByIdQueryHandler` does not exist).

- [ ] **Step 5: Create `GetPositionByIdQueryHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;

public class GetPositionByIdQueryHandler
    : IRequestHandler<GetPositionByIdQuery, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetPositionByIdQueryHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<PositionResponse>> Handle(
        GetPositionByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionResponse>.NotFound("Legal entity not found.");

        var entity = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (entity == null)
            return Result<PositionResponse>.NotFound("Position not found.");

        string? departmentName = null;
        if (entity.DepartmentId is { } departmentId)
        {
            var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, departmentId, ct);
            departmentName = department?.Name;
        }

        string? reportsToPositionName = null;
        if (entity.ReportsToPositionId is { } reportsToPositionId)
        {
            var reportsToPosition = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToPositionId, ct);
            reportsToPositionName = reportsToPosition?.Name;
        }

        var childCount = await _positions.CountActiveReportsToPositionAsync(tenantId, request.LegalEntityId, entity.Id, ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(entity, departmentName, reportsToPositionName, childCount));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPositionByIdQueryHandlerTests" --verbosity minimal`
Expected: 3/3 PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionById tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionByIdQueryHandlerTests.cs
git commit -m "feat(position): add GetPositionByIdQuery"
```

---

### Task 5: ListPositionsQuery

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/ListPositions/ListPositionsQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/ListPositions/ListPositionsQueryValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/ListPositions/ListPositionsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ListPositionsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `PositionPageResponse`, `PositionListItemResponse`, `PositionMapper.ToListItemResponse` (Tasks 1-2); `IPositionRepository.ListPageAsync` (Task 3); `ILegalEntityRepository.GetByIdForTenantAsync`; `ICurrentUser`, `Result<T>`.
- Produces: `ListPositionsQuery(...) : IRequest<Result<PositionPageResponse>>`.

- [ ] **Step 1: Create `ListPositionsQuery.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public record ListPositionsQuery(
    Guid LegalEntityId,
    Guid? DepartmentId,
    string? Search,
    bool IncludeInactive,
    int Page,
    int PageSize,
    string SortBy,
    string SortDirection) : IRequest<Result<PositionPageResponse>>;
```

- [ ] **Step 2: Create `ListPositionsQueryValidator.cs`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public class ListPositionsQueryValidator : AbstractValidator<ListPositionsQuery>
{
    private static readonly string[] AllowedSortBy =
        ["name", "code", "department", "reportsto", "type", "capacity", "status", "createdat", "updatedat"];
    private static readonly string[] AllowedSortDirections = ["asc", "desc"];
    private const int MaxPageSize = 100;

    public ListPositionsQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search cannot exceed 100 characters.");

        RuleFor(x => x.SortBy)
            .NotEmpty().WithMessage("SortBy is required.")
            .Must(sortBy => AllowedSortBy.Contains(sortBy.Trim().ToLowerInvariant()))
            .WithMessage("SortBy must be one of: name, code, department, reportsTo, type, capacity, status, createdAt, updatedAt.");

        RuleFor(x => x.SortDirection)
            .NotEmpty().WithMessage("SortDirection is required.")
            .Must(direction => AllowedSortDirections.Contains(direction.Trim().ToLowerInvariant()))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");

        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {MaxPageSize}.");
    }
}
```

- [ ] **Step 3: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class ListPositionsQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public ListPositionsQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private ListPositionsQueryHandler CreateHandler()
        => new(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    private static ListPositionsQuery DefaultQuery(
        Guid legalEntityId,
        Guid? departmentId = null,
        string? search = null,
        bool includeInactive = false,
        int page = 1,
        int pageSize = 20,
        string sortBy = "name",
        string sortDirection = "asc")
        => new(legalEntityId, departmentId, search, includeInactive, page, pageSize, sortBy, sortDirection);

    [Fact]
    public async Task Handle_PassesTrimmedLowercasedSortAndSearchToRepository()
    {
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, "finance", false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage([], 0, 1, 20, 0));

        var query = DefaultQuery(_legalEntityId, search: "  finance  ", sortBy: "  Name  ", sortDirection: "ASC");

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(
            p => p.ListPageAsync(_tenantId, _legalEntityId, null, "finance", false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadataFromRepository()
    {
        var items = new List<ONEVO.Domain.Features.OrgStructure.Entities.Position>
        {
            new() { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Alpha", Code = "A", IsActive = true }
        };
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, null, false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage(items, 41, 1, 20, 3));

        var result = await CreateHandler().Handle(DefaultQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(41, result.Value!.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Single(result.Value.Items);
        Assert.Equal("Alpha", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLegalEntityMissing()
    {
        var missingLegalEntityId = Guid.NewGuid();
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, missingLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(DefaultQuery(missingLegalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListPositionsQueryHandlerTests" --verbosity minimal`
Expected: build error (`ListPositionsQueryHandler` does not exist).

- [ ] **Step 5: Create `ListPositionsQueryHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public class ListPositionsQueryHandler
    : IRequestHandler<ListPositionsQuery, Result<PositionPageResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public ListPositionsQueryHandler(
        IPositionRepository positions,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<PositionPageResponse>> Handle(
        ListPositionsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionPageResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionPageResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionPageResponse>.NotFound("Legal entity not found.");

        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var sortBy = request.SortBy.Trim().ToLowerInvariant();
        var sortDirection = request.SortDirection.Trim().ToLowerInvariant();

        var page = await _positions.ListPageAsync(
            tenantId,
            request.LegalEntityId,
            request.DepartmentId,
            normalizedSearch,
            request.IncludeInactive,
            sortBy,
            sortDirection,
            request.Page,
            request.PageSize,
            ct);

        var items = page.Items.Select(PositionMapper.ToListItemResponse).ToList();
        var response = new PositionPageResponse(items, page.Page, page.PageSize, page.TotalCount, page.TotalPages);

        return Result<PositionPageResponse>.Success(response);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ListPositionsQueryHandlerTests" --verbosity minimal`
Expected: 3/3 PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Queries/ListPositions tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ListPositionsQueryHandlerTests.cs
git commit -m "feat(position): add ListPositionsQuery"
```

---

### Task 6: GetPositionTreeQuery

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionTree/GetPositionTreeQuery.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionTree/GetPositionTreeQueryValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionTree/GetPositionTreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionTreeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `PositionTreeNodeResponse`, `PositionTreeMapper.BuildTree` (Task 2); `IPositionRepository.ListByLegalEntityAsync` (existing, already active-only by default via its `includeInactive` parameter); `ILegalEntityRepository.GetByIdForTenantAsync`; `ICurrentUser`, `Result<T>`.
- Produces: `GetPositionTreeQuery(Guid LegalEntityId, bool IncludeInactive) : IRequest<Result<IReadOnlyList<PositionTreeNodeResponse>>>`.

- [ ] **Step 1: Create `GetPositionTreeQuery.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public record GetPositionTreeQuery(
    Guid LegalEntityId,
    bool IncludeInactive) : IRequest<Result<IReadOnlyList<PositionTreeNodeResponse>>>;
```

- [ ] **Step 2: Create `GetPositionTreeQueryValidator.cs`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public class GetPositionTreeQueryValidator : AbstractValidator<GetPositionTreeQuery>
{
    public GetPositionTreeQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
    }
}
```

- [ ] **Step 3: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class GetPositionTreeQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetPositionTreeQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private GetPositionTreeQueryHandler CreateHandler()
        => new(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_BuildsTreeFromLegalEntityScopedPositions_ExcludingOtherLegalEntities()
    {
        // ListByLegalEntityAsync is already filtered SQL-side by tenantId+legalEntityId (existing
        // Part 2A implementation), so a position from another legal entity or tenant is never in
        // the list the mock returns - this test asserts the handler passes the correct scope and
        // trusts the mapper to build the tree only from what the repository returned.
        var root = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "CEO", Code = "CEO", IsActive = true
        };
        var child = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "VP Sales", Code = "VP-SALES",
            ReportsToPositionId = root.Id, IsActive = true
        };
        _positionsMock
            .Setup(p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.OrgStructure.Entities.Position> { root, child });

        var result = await CreateHandler().Handle(
            new GetPositionTreeQuery(_legalEntityId, IncludeInactive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("CEO", result.Value[0].Name);
        Assert.Single(result.Value[0].Children);
        Assert.Equal("VP Sales", result.Value[0].Children[0].Name);
        _positionsMock.Verify(
            p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLegalEntityMissing()
    {
        var missingLegalEntityId = Guid.NewGuid();
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, missingLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(
            new GetPositionTreeQuery(missingLegalEntityId, false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPositionTreeQueryHandlerTests" --verbosity minimal`
Expected: build error (`GetPositionTreeQueryHandler` does not exist).

- [ ] **Step 5: Create `GetPositionTreeQueryHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;

public class GetPositionTreeQueryHandler
    : IRequestHandler<GetPositionTreeQuery, Result<IReadOnlyList<PositionTreeNodeResponse>>>
{
    private readonly IPositionRepository _positions;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public GetPositionTreeQueryHandler(
        IPositionRepository positions,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PositionTreeNodeResponse>>> Handle(
        GetPositionTreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<IReadOnlyList<PositionTreeNodeResponse>>.NotFound("Legal entity not found.");

        var positions = await _positions.ListByLegalEntityAsync(
            tenantId, request.LegalEntityId, request.IncludeInactive, departmentId: null, ct);

        var tree = PositionTreeMapper.BuildTree(positions);

        return Result<IReadOnlyList<PositionTreeNodeResponse>>.Success(tree);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~GetPositionTreeQueryHandlerTests" --verbosity minimal`
Expected: 2/2 PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Queries/GetPositionTree tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/GetPositionTreeQueryHandlerTests.cs
git commit -m "feat(position): add GetPositionTreeQuery"
```

---

### Task 7: CreatePositionCommand

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition/CreatePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `PositionResponse`, `PositionMapper.ToResponse` (Tasks 1-2); `IPositionRepository.ExistsByCodeAsync`, `ExistsByNameAsync`, `GetByIdForLegalEntityAsync`, `AddAsync`, `SaveChangesAsync` (existing); `IDepartmentRepository.GetByIdForLegalEntityAsync`; `ILegalEntityRepository.GetByIdForTenantAsync`; `ICurrentUser`, `IDateTimeProvider`, `Result<T>`; `Position.TypeUnique`/`Position.TypePooled` constants (existing, `ONEVO.Domain.Features.OrgStructure.Entities`).
- Produces: `CreatePositionCommand(Guid LegalEntityId, Guid DepartmentId, string Name, string Code, string PositionType, int MaxOccupancy, Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>` — the future controller (out of scope) will populate `LegalEntityId` from the route and everything else from `CreatePositionRequest` (Task 10).

- [ ] **Step 1: Create `CreatePositionCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public record CreatePositionCommand(
    Guid LegalEntityId,
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>;
```

- [ ] **Step 2: Create `CreatePositionCommandValidator.cs`**

```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public class CreatePositionCommandValidator : AbstractValidator<CreatePositionCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.Compiled);

    public CreatePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.DepartmentId).NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Position name is required.")
            .MaximumLength(100).WithMessage("Position name cannot exceed 100 characters.");

        // Split into two chains deliberately: a trailing .When() scopes to every preceding
        // rule in the same chain, not just the one immediately before it. Keeping NotEmpty
        // and MaximumLength unconditional (no .When()) is what makes "code is required" and
        // "code cannot exceed 40 characters" actually enforce; only the regex needs the guard,
        // since code.Trim() would NullReferenceException on a null Code otherwise.
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Position code is required.")
            .MaximumLength(40).WithMessage("Position code cannot exceed 40 characters.");

        RuleFor(x => x.Code)
            .Must(code => CodePattern.IsMatch(code.Trim()))
            .WithMessage("Position code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x.PositionType)
            .NotEmpty().WithMessage("Position type is required.")
            .Must(type => type == Position.TypeUnique || type == Position.TypePooled)
            .WithMessage("Position type must be 'unique' or 'pooled'.");

        RuleFor(x => x.MaxOccupancy)
            .Equal(1).WithMessage("Single-occupancy positions must have a capacity of exactly 1.")
            .When(x => x.PositionType == Position.TypeUnique);

        RuleFor(x => x.MaxOccupancy)
            .GreaterThanOrEqualTo(1).WithMessage("Capacity must be at least 1.")
            .When(x => x.PositionType == Position.TypePooled);
    }
}
```

- [ ] **Step 3: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class CreatePositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public CreatePositionCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = _departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Customer Support", IsActive = true });
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.ExistsByNameAsync(_tenantId, _legalEntityId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private CreatePositionCommandHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

    private CreatePositionCommand ValidCommand(
        Guid? departmentId = null, string name = "Customer Support Manager", string code = "CS-MGR",
        string positionType = "unique", int maxOccupancy = 1, Guid? reportsToPositionId = null)
        => new(_legalEntityId, departmentId ?? _departmentId, name, code, positionType, maxOccupancy, reportsToPositionId);

    [Fact]
    public async Task Handle_Succeeds_WithValidLegalEntityAndDepartment()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support Manager", result.Value!.Name);
        Assert.Equal("CS-MGR", result.Value.Code);
        Assert.Equal("Customer Support", result.Value.DepartmentName);
        Assert.Equal(_now, result.Value.CreatedAt);
        _positionsMock.Verify(p => p.AddAsync(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>(), It.IsAny<CancellationToken>()), Times.Once);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsNameAndCode()
    {
        ONEVO.Domain.Features.OrgStructure.Entities.Position? added = null;
        _positionsMock
            .Setup(p => p.AddAsync(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>(), It.IsAny<CancellationToken>()))
            .Callback<ONEVO.Domain.Features.OrgStructure.Entities.Position, CancellationToken>((entity, _) => added = entity)
            .Returns(Task.CompletedTask);

        var command = ValidCommand(name: "  Customer Support Manager  ", code: "  CS-MGR  ");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support Manager", added!.Name);
        Assert.Equal("CS-MGR", added.Code);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDepartmentMissing()
    {
        var missingDepartmentId = Guid.NewGuid();
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDepartmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var result = await CreateHandler().Handle(ValidCommand(departmentId: missingDepartmentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDepartmentBelongsToAnotherLegalEntity()
    {
        // Department repository is itself scoped by legalEntityId, so a department from another
        // legal entity is simply never returned by GetByIdForLegalEntityAsync - the default mock
        // setup (returns null for any unmatched Guid combination) already models this correctly.
        var departmentFromAnotherLegalEntity = Guid.NewGuid();

        var result = await CreateHandler().Handle(ValidCommand(departmentId: departmentFromAnotherLegalEntity), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenCodeAlreadyExists()
    {
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, "CS-MGR", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void Validator_RejectsEmptyCode()
    {
        var validator = new CreatePositionCommandValidator();

        var emptyResult = validator.Validate(ValidCommand(code: ""));
        var whitespaceResult = validator.Validate(ValidCommand(code: "   "));

        Assert.False(emptyResult.IsValid);
        Assert.False(whitespaceResult.IsValid);
    }

    [Fact]
    public void Validator_RejectsInvalidCodeCharacters()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(code: "CS MGR!"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsInvalidPositionType()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "manager"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsSingleOccupancyCapacityOtherThanOne()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "unique", maxOccupancy: 2));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_AllowsMultiOccupancyCapacityGreaterThanOne()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "pooled", maxOccupancy: 5));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenReportsToPositionBelongsToAnotherLegalEntity()
    {
        var reportsToId = Guid.NewGuid();
        // GetByIdForLegalEntityAsync default mock setup returns null for unmatched combinations,
        // modeling a reports-to position that belongs to a different legal entity.

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: reportsToId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenReportsToPositionIsInactive()
    {
        var reportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position
            {
                Id = reportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Inactive Manager", IsActive = false
            });

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: reportsToId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~CreatePositionCommandHandlerTests" --verbosity minimal`
Expected: build error (`CreatePositionCommandHandler` does not exist).

- [ ] **Step 5: Create `CreatePositionCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;

public class CreatePositionCommandHandler
    : IRequestHandler<CreatePositionCommand, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreatePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PositionResponse>> Handle(
        CreatePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionResponse>.NotFound("Legal entity not found.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.Conflict("Department is inactive.");

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _positions.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingPositionId: null, ct))
            return Result<PositionResponse>.Conflict("Position code already exists in this legal entity.");

        if (await _positions.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingPositionId: null, ct))
            return Result<PositionResponse>.Conflict("Position name already exists in this legal entity.");

        PositionEntity? reportsTo = null;
        if (request.ReportsToPositionId is { } reportsToId)
        {
            reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo == null)
                return Result<PositionResponse>.NotFound("Reports-to position not found in this legal entity.");
            if (!reportsTo.IsActive)
                return Result<PositionResponse>.Conflict("Reports-to position is inactive.");
            // A new position has no Id yet, so self-reference and cycle checks are impossible
            // here - they only become reachable once the position already exists (see
            // UpdatePositionCommandHandler).
        }

        var entity = new PositionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = request.LegalEntityId,
            DepartmentId = request.DepartmentId,
            Name = name,
            Code = code,
            PositionType = request.PositionType,
            MaxOccupancy = request.MaxOccupancy,
            ReportsToPositionId = request.ReportsToPositionId,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _positions.AddAsync(entity, ct);
        await _positions.SaveChangesAsync(ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(entity, department.Name, reportsTo?.Name, childCount: 0));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~CreatePositionCommandHandlerTests" --verbosity minimal`
Expected: 12/12 PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/CreatePositionCommandHandlerTests.cs
git commit -m "feat(position): add CreatePositionCommand"
```

---

### Task 8: UpdatePositionCommand

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition/UpdatePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: same as Task 7, plus `IPositionRepository.IsDescendantAsync`, `Update` (existing).
- Produces: `UpdatePositionCommand(Guid LegalEntityId, Guid PositionId, Guid DepartmentId, string Name, string Code, string PositionType, int MaxOccupancy, Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>`.

- [ ] **Step 1: Create `UpdatePositionCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public record UpdatePositionCommand(
    Guid LegalEntityId,
    Guid PositionId,
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId) : IRequest<Result<PositionResponse>>;
```

- [ ] **Step 2: Create `UpdatePositionCommandValidator.cs`**

```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public class UpdatePositionCommandValidator : AbstractValidator<UpdatePositionCommand>
{
    private static readonly Regex CodePattern = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.Compiled);

    public UpdatePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
        RuleFor(x => x.DepartmentId).NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Position name is required.")
            .MaximumLength(100).WithMessage("Position name cannot exceed 100 characters.");

        // Split into two chains deliberately: a trailing .When() scopes to every preceding
        // rule in the same chain, not just the one immediately before it. Keeping NotEmpty
        // and MaximumLength unconditional (no .When()) is what makes "code is required" and
        // "code cannot exceed 40 characters" actually enforce; only the regex needs the guard,
        // since code.Trim() would NullReferenceException on a null Code otherwise.
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Position code is required.")
            .MaximumLength(40).WithMessage("Position code cannot exceed 40 characters.");

        RuleFor(x => x.Code)
            .Must(code => CodePattern.IsMatch(code.Trim()))
            .WithMessage("Position code may only contain letters, numbers, hyphens, and underscores.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));

        RuleFor(x => x.PositionType)
            .NotEmpty().WithMessage("Position type is required.")
            .Must(type => type == Position.TypeUnique || type == Position.TypePooled)
            .WithMessage("Position type must be 'unique' or 'pooled'.");

        RuleFor(x => x.MaxOccupancy)
            .Equal(1).WithMessage("Single-occupancy positions must have a capacity of exactly 1.")
            .When(x => x.PositionType == Position.TypeUnique);

        RuleFor(x => x.MaxOccupancy)
            .GreaterThanOrEqualTo(1).WithMessage("Capacity must be at least 1.")
            .When(x => x.PositionType == Position.TypePooled);

        RuleFor(x => x)
            .Must(x => x.ReportsToPositionId == null || x.ReportsToPositionId != x.PositionId)
            .WithMessage("A position cannot report to itself.");
    }
}
```

- [ ] **Step 3: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class UpdatePositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public UpdatePositionCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = _departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Customer Support", IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position
            {
                Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
                Name = "Old Name", Code = "OLD-CODE", PositionType = "unique", MaxOccupancy = 1, IsActive = true
            });
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.ExistsByNameAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private UpdatePositionCommandHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

    private UpdatePositionCommand ValidCommand(
        Guid? departmentId = null, string name = "New Name", string code = "NEW-CODE",
        string positionType = "unique", int maxOccupancy = 1, Guid? reportsToPositionId = null)
        => new(_legalEntityId, _positionId, departmentId ?? _departmentId, name, code, positionType, maxOccupancy, reportsToPositionId);

    [Fact]
    public async Task Handle_PreservesTenantAndLegalEntityScope_WhenUpdatingFields()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_legalEntityId, result.Value!.LegalEntityId);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("NEW-CODE", result.Value.Code);
        _positionsMock.Verify(
            p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsReportingToItself()
    {
        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RejectsReportingCycle_WhenNewReportsToIsADescendant()
    {
        var descendantId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, descendantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position
            {
                Id = descendantId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Descendant", IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, descendantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: descendantId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllowsReportsToChange_WhenNotADescendant()
    {
        var newParentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position
            {
                Id = newParentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: newParentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newParentId, result.Value!.ReportsToPositionId);
        Assert.Equal("New Manager", result.Value.ReportsToPositionName);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPositionMissing()
    {
        var missingPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.OrgStructure.Entities.Position?)null);

        var command = new UpdatePositionCommand(_legalEntityId, missingPositionId, _departmentId, "Name", "CODE", "unique", 1, null);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Validator_RejectsEmptyCode()
    {
        var validator = new UpdatePositionCommandValidator();

        var emptyResult = validator.Validate(ValidCommand(code: ""));
        var whitespaceResult = validator.Validate(ValidCommand(code: "   "));

        Assert.False(emptyResult.IsValid);
        Assert.False(whitespaceResult.IsValid);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~UpdatePositionCommandHandlerTests" --verbosity minimal`
Expected: build error (`UpdatePositionCommandHandler` does not exist).

- [ ] **Step 5: Create `UpdatePositionCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;

public class UpdatePositionCommandHandler
    : IRequestHandler<UpdatePositionCommand, Result<PositionResponse>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdatePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<PositionResponse>> Handle(
        UpdatePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionResponse>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionResponse>.NotFound("Legal entity not found.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<PositionResponse>.NotFound("Position not found.");

        if (request.ReportsToPositionId == request.PositionId)
            return Result<PositionResponse>.Conflict("A position cannot report to itself.");

        var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.DepartmentId, ct);
        if (department == null)
            return Result<PositionResponse>.NotFound("Department not found in this legal entity.");
        if (!department.IsActive)
            return Result<PositionResponse>.Conflict("Department is inactive.");

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _positions.ExistsByCodeAsync(tenantId, request.LegalEntityId, code, excludingPositionId: request.PositionId, ct))
            return Result<PositionResponse>.Conflict("Position code already exists in this legal entity.");

        if (await _positions.ExistsByNameAsync(tenantId, request.LegalEntityId, name, excludingPositionId: request.PositionId, ct))
            return Result<PositionResponse>.Conflict("Position name already exists in this legal entity.");

        PositionEntity? reportsTo = null;
        if (request.ReportsToPositionId is { } reportsToId)
        {
            reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo == null)
                return Result<PositionResponse>.NotFound("Reports-to position not found in this legal entity.");
            if (!reportsTo.IsActive)
                return Result<PositionResponse>.Conflict("Reports-to position is inactive.");

            var reportsToIsDescendant = await _positions.IsDescendantAsync(
                tenantId, request.LegalEntityId, existing.Id, reportsToId, ct);
            if (reportsToIsDescendant)
                return Result<PositionResponse>.Conflict("Cannot set reports-to: would create a circular reporting hierarchy.");
        }

        // Mutate the fetched entity directly; do not construct a detached replacement.
        existing.DepartmentId = request.DepartmentId;
        existing.Name = name;
        existing.Code = code;
        existing.PositionType = request.PositionType;
        existing.MaxOccupancy = request.MaxOccupancy;
        existing.ReportsToPositionId = request.ReportsToPositionId;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);
        await _positions.SaveChangesAsync(ct);

        var childCount = await _positions.CountActiveReportsToPositionAsync(tenantId, request.LegalEntityId, existing.Id, ct);

        return Result<PositionResponse>.Success(
            PositionMapper.ToResponse(existing, department.Name, reportsTo?.Name, childCount));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~UpdatePositionCommandHandlerTests" --verbosity minimal`
Expected: 6/6 PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Commands/UpdatePosition tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/UpdatePositionCommandHandlerTests.cs
git commit -m "feat(position): add UpdatePositionCommand"
```

---

### Task 9: PositionArchiveDependencyEvaluator, ArchivePositionCommand, RestorePositionCommand, CheckPositionArchiveCommand

**Files:**
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Services/PositionArchiveDependencyEvaluator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/ArchivePosition/ArchivePositionCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/ArchivePosition/ArchivePositionCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/ArchivePosition/ArchivePositionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/RestorePosition/RestorePositionCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/RestorePosition/RestorePositionCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/RestorePosition/RestorePositionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CheckPositionArchive/CheckPositionArchiveCommand.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CheckPositionArchive/CheckPositionArchiveCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/OrgStructure/Position/Commands/CheckPositionArchive/CheckPositionArchiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ArchiveRestoreCheckPositionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `PositionArchiveBlockers` (Task 1); `IPositionRepository.CountActiveReportsToPositionAsync` (existing), `CountHeadDepartmentReferencesAsync` (Task 3), `GetByIdForLegalEntityAsync`, `Update`, `SaveChangesAsync` (existing); `IDepartmentRepository.GetByIdForLegalEntityAsync`; `ILegalEntityRepository.GetByIdForTenantAsync`; `ICurrentUser`, `IDateTimeProvider`, `Result<T>`.
- Produces: `ArchivePositionCommand(Guid LegalEntityId, Guid PositionId) : IRequest<Result<bool>>`, `RestorePositionCommand(Guid LegalEntityId, Guid PositionId) : IRequest<Result<bool>>`, `CheckPositionArchiveCommand(Guid LegalEntityId, Guid PositionId) : IRequest<Result<PositionArchiveBlockers>>`.

- [ ] **Step 1: Create `PositionArchiveDependencyEvaluator.cs`**

```csharp
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Services;

// No position_assignments/employee-position table exists anywhere in this codebase (confirmed
// by a repo-wide search), so ActiveOccupants is always reported as null/unsupported rather than
// a fabricated 0 - a documented schema limitation, not an unverified guess, mirroring
// DepartmentArchiveDependencyEvaluator's precedent for PositionDependencyCheckSupported.
public static class PositionArchiveDependencyEvaluator
{
    public static async Task<PositionArchiveBlockers> EvaluateAsync(
        IPositionRepository positions,
        IDepartmentRepository departments,
        Guid tenantId,
        Guid legalEntityId,
        Guid positionId,
        CancellationToken ct)
    {
        var activeChildPositions = await positions.CountActiveReportsToPositionAsync(
            tenantId, legalEntityId, positionId, ct);
        var headOfDepartments = await positions.CountHeadDepartmentReferencesAsync(
            tenantId, legalEntityId, positionId, ct);

        return new PositionArchiveBlockers(
            ActiveOccupants: null,
            ActiveOccupantsCheckSupported: false,
            HeadOfDepartments: headOfDepartments,
            ActiveChildPositions: activeChildPositions);
    }

    public static bool CanArchive(PositionArchiveBlockers blockers)
    {
        return blockers.CanArchive;
    }

    public static string BuildMessage(PositionArchiveBlockers blockers)
    {
        if (blockers.CanArchive)
        {
            return "No active child positions or department-head references are linked to this position.";
        }

        var reasons = new List<string>();
        if (blockers.ActiveChildPositions > 0)
        {
            reasons.Add("child positions");
        }
        if (blockers.HeadOfDepartments > 0)
        {
            reasons.Add("department head assignments");
        }

        var joined = reasons.Count == 1 ? reasons[0] : string.Join(" and ", reasons);
        return $"This position cannot be archived yet. Resolve linked {joined} first.";
    }
}
```

- [ ] **Step 2: Create `ArchivePositionCommand.cs`, `RestorePositionCommand.cs`, `CheckPositionArchiveCommand.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public record ArchivePositionCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<bool>>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public record RestorePositionCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<bool>>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public record CheckPositionArchiveCommand(
    Guid LegalEntityId,
    Guid PositionId) : IRequest<Result<PositionArchiveBlockers>>;
```

- [ ] **Step 3: Create the three validators**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public class ArchivePositionCommandValidator : AbstractValidator<ArchivePositionCommand>
{
    public ArchivePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
```

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public class RestorePositionCommandValidator : AbstractValidator<RestorePositionCommand>
{
    public RestorePositionCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
```

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public class CheckPositionArchiveCommandValidator : AbstractValidator<CheckPositionArchiveCommand>
{
    public CheckPositionArchiveCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");
    }
}
```

- [ ] **Step 4: Write failing handler tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class ArchiveRestoreCheckPositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public ArchiveRestoreCheckPositionCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private ONEVO.Domain.Features.OrgStructure.Entities.Position CreatePositionEntity(bool isActive, Guid? departmentId = null, Guid? reportsToPositionId = null)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = departmentId,
            ReportsToPositionId = reportsToPositionId, Name = "Manager", Code = "MGR", IsActive = isActive
        };
    }

    [Fact]
    public async Task Archive_Blocks_WhenActiveChildPositionsExist()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.Update(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>()), Times.Never);
    }

    [Fact]
    public async Task Archive_Blocks_WhenReferencedAsDepartmentHead()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Archive_DoesNotReparentChildren_WhenBlocked()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        // A blocked archive must touch nothing: no Update on the target or any other position
        // (i.e. no silent reparenting of children), and no SaveChangesAsync call at all.
        _positionsMock.Verify(p => p.Update(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>()), Times.Never);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_Succeeds_WhenNoBlockers()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        _positionsMock.Verify(p => p.Update(It.Is<ONEVO.Domain.Features.OrgStructure.Entities.Position>(pos => !pos.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Restore_Blocks_WhenDepartmentInactive()
    {
        var departmentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: false, departmentId: departmentId));
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Ops", IsActive = false });

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.Update(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>()), Times.Never);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenDepartmentActiveAndNoReportsTo()
    {
        var departmentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: false, departmentId: departmentId));
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Ops", IsActive = true });

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.Update(It.Is<ONEVO.Domain.Features.OrgStructure.Entities.Position>(pos => pos.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Restore_IsIdempotent_WhenAlreadyActive()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.Update(It.IsAny<ONEVO.Domain.Features.OrgStructure.Entities.Position>()), Times.Never);
    }

    [Fact]
    public async Task CheckArchive_ReturnsExactBlockerCounts_AndFlagsOccupantsUnsupported()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new CheckPositionArchiveCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new CheckPositionArchiveCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.ActiveChildPositions);
        Assert.Equal(1, result.Value.HeadOfDepartments);
        Assert.Null(result.Value.ActiveOccupants);
        Assert.False(result.Value.ActiveOccupantsCheckSupported);
        Assert.False(result.Value.CanArchive);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ArchiveRestoreCheckPositionCommandHandlerTests" --verbosity minimal`
Expected: build error (handlers do not exist).

- [ ] **Step 6: Create `ArchivePositionCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;

public class ArchivePositionCommandHandler
    : IRequestHandler<ArchivePositionCommand, Result<bool>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ArchivePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(ArchivePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Position not found.");

        var blockers = await PositionArchiveDependencyEvaluator.EvaluateAsync(
            _positions, _departments, tenantId, request.LegalEntityId, existing.Id, ct);
        if (!PositionArchiveDependencyEvaluator.CanArchive(blockers))
        {
            return Result<bool>.Conflict(PositionArchiveDependencyEvaluator.BuildMessage(blockers));
        }

        // Archive is a soft-deactivation, never a physical delete: reporting and audit history
        // referencing this row remain intact. Child positions are not reparented automatically -
        // the blocker check above already refused to archive while active children exist.
        existing.IsActive = false;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);
        await _positions.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
```

- [ ] **Step 7: Create `RestorePositionCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;

public class RestorePositionCommandHandler
    : IRequestHandler<RestorePositionCommand, Result<bool>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RestorePositionCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<bool>> Handle(RestorePositionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<bool>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<bool>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<bool>.NotFound("Legal entity not found.");
        if (!legalEntity.IsActive)
            return Result<bool>.Conflict("Cannot restore: the legal entity is inactive.");

        // GetByIdForLegalEntityAsync has no IsActive filter, so this also finds
        // already-archived rows - required for restore to work at all.
        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<bool>.NotFound("Position not found.");

        if (existing.IsActive)
        {
            // Already active: idempotent success, matching RestoreDepartmentCommandHandler's
            // precedent of not treating a repeat call as an error.
            return Result<bool>.Success(true);
        }

        if (existing.DepartmentId is { } departmentId)
        {
            var department = await _departments.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, departmentId, ct);
            if (department is null || !department.IsActive)
            {
                return Result<bool>.Conflict(
                    "Cannot restore: the department is missing or inactive. Restore or reassign the department first.");
            }
        }

        if (existing.ReportsToPositionId is { } reportsToId)
        {
            var reportsTo = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, reportsToId, ct);
            if (reportsTo is null || !reportsTo.IsActive)
            {
                return Result<bool>.Conflict(
                    "Cannot restore: the reports-to position is missing or inactive. Restore or reassign it first.");
            }
        }

        // Restore only flips IsActive. Children, code, name, and reporting line are untouched.
        existing.IsActive = true;
        existing.UpdatedAt = _dateTimeProvider.UtcNow;

        _positions.Update(existing);
        await _positions.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
```

- [ ] **Step 8: Create `CheckPositionArchiveCommandHandler.cs`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Services;

namespace ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;

public class CheckPositionArchiveCommandHandler
    : IRequestHandler<CheckPositionArchiveCommand, Result<PositionArchiveBlockers>>
{
    private readonly IPositionRepository _positions;
    private readonly IDepartmentRepository _departments;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrentUser _currentUser;

    public CheckPositionArchiveCommandHandler(
        IPositionRepository positions,
        IDepartmentRepository departments,
        ILegalEntityRepository legalEntities,
        ICurrentUser currentUser)
    {
        _positions = positions;
        _departments = departments;
        _legalEntities = legalEntities;
        _currentUser = currentUser;
    }

    public async Task<Result<PositionArchiveBlockers>> Handle(
        CheckPositionArchiveCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PositionArchiveBlockers>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PositionArchiveBlockers>.Forbidden("Tenant context missing.");

        var legalEntity = await _legalEntities.GetByIdForTenantAsync(tenantId, request.LegalEntityId, ct);
        if (legalEntity == null)
            return Result<PositionArchiveBlockers>.NotFound("Legal entity not found.");

        var existing = await _positions.GetByIdForLegalEntityAsync(tenantId, request.LegalEntityId, request.PositionId, ct);
        if (existing == null)
            return Result<PositionArchiveBlockers>.NotFound("Position not found.");

        var blockers = await PositionArchiveDependencyEvaluator.EvaluateAsync(
            _positions, _departments, tenantId, request.LegalEntityId, existing.Id, ct);

        return Result<PositionArchiveBlockers>.Success(blockers);
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~ArchiveRestoreCheckPositionCommandHandlerTests" --verbosity minimal`
Expected: 8/8 PASS.

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/Position/Services src/ONEVO.Application/Features/OrgStructure/Position/Commands/ArchivePosition src/ONEVO.Application/Features/OrgStructure/Position/Commands/RestorePosition src/ONEVO.Application/Features/OrgStructure/Position/Commands/CheckPositionArchive tests/ONEVO.Tests.Unit/Features/OrgStructure/Position/ArchiveRestoreCheckPositionCommandHandlerTests.cs
git commit -m "feat(position): add archive, restore, and check-archive commands"
```

---

### Task 10: API request contracts

**Files:**
- Create: `src/ONEVO.Api/Contracts/OrgStructure/Positions/CreatePositionRequest.cs`
- Create: `src/ONEVO.Api/Contracts/OrgStructure/Positions/UpdatePositionRequest.cs`

No `ListPositionsRequest` (Department's `ListDepartmentsQuery` is built directly from query-string parameters by the controller, not a wrapper contract — Position follows the same pattern; a controller is out of scope for Part 2B anyway). No `ArchivePositionRequest`/`RestorePositionRequest` (Department's archive/restore endpoints take no request body — `ArchiveDepartmentRequest.cs`/`RestoreDepartmentRequest.cs` do not exist in the Departments contracts folder).

**Interfaces:**
- Consumes: nothing (plain records).
- Produces: `CreatePositionRequest`, `UpdatePositionRequest` — a future controller (out of scope) will map these plus a route-bound `legalEntityId` into `CreatePositionCommand`/`UpdatePositionCommand`.

- [ ] **Step 1: Create `CreatePositionRequest.cs`**

```csharp
namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record CreatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
```

- [ ] **Step 2: Create `UpdatePositionRequest.cs`**

```csharp
namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record UpdatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/OrgStructure/Positions
git commit -m "feat(position): add CreatePositionRequest and UpdatePositionRequest contracts"
```

---

### Task 11: Architecture tests — `PositionPart2BArchitectureTests.cs`

**Files:**
- Create: `tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs`

This task must run after every prior task (it references every type this plan creates). Do not modify `PositionPart2AArchitectureTests.cs` or `DepartmentPart2BArchitectureTests.cs`.

**Interfaces:**
- Consumes: every type created in Tasks 1-10.
- Produces: nothing further — this is the plan's own guard rail.

- [ ] **Step 1: Create `PositionPart2BArchitectureTests.cs`**

```csharp
using System.Reflection;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards Part 2B scope for Position: commands, queries, validators, DTOs, mappers, repository
/// additions, and API request contracts. Asserts no controller/route exists yet, request
/// contracts and commands exclude ownership/role/occupant fields, CQRS handlers do not bypass
/// repository abstractions with ApplicationDbContext, no new C# enum is introduced for
/// type/sort/status, and every file lives under the expected OrgStructure/Position path.
/// </summary>
public class PositionPart2BArchitectureTests
{
    private static readonly Type[] RequestContractTypes =
    [
        typeof(CreatePositionRequest),
        typeof(UpdatePositionRequest)
    ];

    private static readonly Type[] CommandTypes =
    [
        typeof(CreatePositionCommand),
        typeof(UpdatePositionCommand),
        typeof(ArchivePositionCommand),
        typeof(RestorePositionCommand),
        typeof(CheckPositionArchiveCommand)
    ];

    private static readonly Type[] QueryTypes =
    [
        typeof(GetPositionByIdQuery),
        typeof(ListPositionsQuery),
        typeof(GetPositionTreeQuery)
    ];

    private static readonly Type[] HandlerTypes =
    [
        typeof(CreatePositionCommandHandler),
        typeof(UpdatePositionCommandHandler),
        typeof(ArchivePositionCommandHandler),
        typeof(RestorePositionCommandHandler),
        typeof(CheckPositionArchiveCommandHandler),
        typeof(GetPositionByIdQueryHandler),
        typeof(ListPositionsQueryHandler),
        typeof(GetPositionTreeQueryHandler)
    ];

    private static readonly string[] ForbiddenContractPropertyNames =
    [
        "TenantId", "LegalEntityId", "Role", "Permission", "Occupant", "Assignment",
        "HeadOfDepartment", "IsDepartmentHead", "HeadPositionId"
    ];

    private static readonly string[] ForbiddenCommandPropertyNames =
    [
        "Role", "Permission", "Occupant", "Assignment", "HeadOfDepartment", "IsDepartmentHead", "HeadPositionId"
    ];

    private static readonly Assembly ApplicationAssembly = typeof(IPositionRepository).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.DepartmentsController).Assembly;

    [Theory]
    [MemberData(nameof(AllRequestContractTypes))]
    public void RequestContracts_DoNotContainForbiddenOwnershipOrRoleFields(Type contractType)
    {
        var offendingProperties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ForbiddenContractPropertyNames.Any(forbidden =>
                p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            offendingProperties.Count == 0,
            $"{contractType.Name} must not expose: {string.Join(", ", offendingProperties)}");
    }

    [Theory]
    [MemberData(nameof(AllCommandTypes))]
    public void Commands_DoNotContainForbiddenRoleOrOccupantFields(Type commandType)
    {
        var offendingProperties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ForbiddenCommandPropertyNames.Any(forbidden =>
                p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            offendingProperties.Count == 0,
            $"{commandType.Name} must not expose: {string.Join(", ", offendingProperties)}");
    }

    [Theory]
    [MemberData(nameof(AllHandlerTypes))]
    public void Handlers_DoNotUseApplicationDbContextDirectly(Type handlerType)
    {
        var constructors = handlerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var parameterTypes = constructors.SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

        var usesDbContext = parameterTypes.Any(t => t.Name.Contains("DbContext", StringComparison.OrdinalIgnoreCase));

        Assert.False(usesDbContext, $"{handlerType.Name} must not inject ApplicationDbContext directly; use repository abstractions.");
    }

    [Fact]
    public void NoPositionsController_ExistsYetInPart2B()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(t => t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase) && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(controllers);
    }

    [Fact]
    public void ApplicationAssembly_DoesNotDeclare_PositionNamedEnums()
    {
        // Scoped to OrgStructure namespaces deliberately: the Application assembly also holds
        // unrelated features (billing, OAuth apps, config templates, MFA, ...) that could
        // coincidentally declare an enum containing "Position" in its name for a reason that has
        // nothing to do with this plan's type/sort/status constraint. Scanning the whole assembly
        // would make this test a false-positive trap for other teams' unrelated work.
        var offendingEnums = ApplicationAssembly.GetTypes()
            .Where(t => t.IsEnum
                && t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                && t.Namespace is not null
                && t.Namespace.Contains("OrgStructure", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offendingEnums.Count == 0, "No enum type may be introduced for Position type/sort/status: " + string.Join(", ", offendingEnums));
    }

    [Fact]
    public void ApiAssembly_DoesNotDeclare_PositionNamedEnums()
    {
        var offendingEnums = ApiAssembly.GetTypes()
            .Where(t => t.IsEnum
                && t.Name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                && t.Namespace is not null
                && t.Namespace.Contains("OrgStructure", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offendingEnums.Count == 0, "No enum type may be introduced for Position type/sort/status: " + string.Join(", ", offendingEnums));
    }

    [Theory]
    [InlineData("Commands/CreatePosition", "CreatePositionCommand.cs")]
    [InlineData("Commands/CreatePosition", "CreatePositionCommandHandler.cs")]
    [InlineData("Commands/CreatePosition", "CreatePositionCommandValidator.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommand.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommandHandler.cs")]
    [InlineData("Commands/UpdatePosition", "UpdatePositionCommandValidator.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommand.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommandHandler.cs")]
    [InlineData("Commands/ArchivePosition", "ArchivePositionCommandValidator.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommand.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommandHandler.cs")]
    [InlineData("Commands/RestorePosition", "RestorePositionCommandValidator.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommand.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommandHandler.cs")]
    [InlineData("Commands/CheckPositionArchive", "CheckPositionArchiveCommandValidator.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQuery.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQueryHandler.cs")]
    [InlineData("Queries/GetPositionById", "GetPositionByIdQueryValidator.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQuery.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQueryHandler.cs")]
    [InlineData("Queries/ListPositions", "ListPositionsQueryValidator.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQuery.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQueryHandler.cs")]
    [InlineData("Queries/GetPositionTree", "GetPositionTreeQueryValidator.cs")]
    [InlineData("Responses", "PositionResponse.cs")]
    [InlineData("Responses", "PositionListItemResponse.cs")]
    [InlineData("Responses", "PositionTreeNodeResponse.cs")]
    [InlineData("Responses", "PositionPageResponse.cs")]
    [InlineData("Responses", "PositionArchiveBlockers.cs")]
    [InlineData("Mappers", "PositionMapper.cs")]
    [InlineData("Mappers", "PositionTreeMapper.cs")]
    [InlineData("RepositoryInterfaces", "PositionPage.cs")]
    [InlineData("Services", "PositionArchiveDependencyEvaluator.cs")]
    public void Part2BFiles_LiveUnderOrgStructurePositionFolder(string subfolder, string fileName)
    {
        var positionRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position");
        var path = Path.Combine(positionRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/Position/{subfolder}");
    }

    [Fact]
    public void PositionApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly()
    {
        var positionAppRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position");

        var csFiles = Directory.GetFiles(positionAppRoot, "*.cs", SearchOption.AllDirectories);

        var offendingFiles = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("DateTimeOffset.UtcNow"))
            {
                offendingFiles.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offendingFiles.Count == 0,
            "Position Application layer must use IDateTimeProvider rather than DateTimeOffset.UtcNow, but found matches in: " + string.Join(", ", offendingFiles));
    }

    public static IEnumerable<object[]> AllRequestContractTypes()
        => RequestContractTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllCommandTypes()
        => CommandTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllHandlerTypes()
        => HandlerTypes.Select(t => new object[] { t });

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
```

- [ ] **Step 2: Run the new architecture tests**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --filter "FullyQualifiedName~PositionPart2BArchitectureTests" --verbosity minimal`
Expected: all Facts/Theories PASS. If `RequestContracts_DoNotContainForbiddenOwnershipOrRoleFields` fails because `HeadPositionId`-substring-matching also flags a legitimate property (it should not, given this plan's DTOs), stop and re-read Task 1/10 output before adjusting the test — do not weaken the forbidden-list to make it pass.

- [ ] **Step 3: Run the full architecture suite to confirm Part 2A and Department Part 2B still pass unchanged**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --verbosity minimal`
Expected: all tests PASS, including every `PositionPart2AArchitectureTests` and `DepartmentPart2BArchitectureTests` fact (neither file was modified by this plan).

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Architecture/PositionPart2BArchitectureTests.cs
git commit -m "test(position): add PositionPart2BArchitectureTests"
```

---

### Task 12: Final verification and report

**Files:**
- Create: `POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md` (repo root of `HRMS-Backend-v1`)

**Interfaces:**
- Consumes: the full diff produced by Tasks 1-11.
- Produces: nothing further — this is the plan's closing task. Do not commit or push (per the original task instructions) unless explicitly asked.

- [ ] **Step 1: Full build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj --no-restore --verbosity minimal`
Expected: build succeeds with 0 errors.

- [ ] **Step 2: Full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --no-restore --no-build --verbosity minimal`
Expected: all tests PASS (record the total count and the count of new Position tests added by this plan for the report).

- [ ] **Step 3: Full architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --no-restore --no-build --verbosity minimal`
Expected: all tests PASS.

- [ ] **Step 4: Focused verification searches**

Run each and compare actual output against the stated expectation. Record any deviation verbatim in the report's "Remaining risks" section rather than silently editing code to force a false green.

```bash
rg -n "tenantId|TenantId" src/ONEVO.Api/Contracts/OrgStructure/Positions src/ONEVO.Api/Controllers/Tenant/OrgStructure
```
Expected: no Position request-body `tenantId` exposure (the `Controllers/Tenant/OrgStructure` matches, if any, belong to `DepartmentsController` and are out of scope — only confirm nothing under `Contracts/OrgStructure/Positions` matches).

```bash
rg -n "legalEntityId|LegalEntityId" src/ONEVO.Api/Contracts/OrgStructure/Positions
```
Expected: no matches (Task 10's contracts never reference `LegalEntityId`).

```bash
rg -n "CreateRole|roleName|permission|permissions|org:read|org:manage" src/ONEVO.Api/Contracts/OrgStructure/Positions src/ONEVO.Application/Features/OrgStructure/Position
```
Expected: no matches.

```bash
rg -n "DateTimeOffset\.UtcNow|DateTime\.UtcNow" src/ONEVO.Application/Features/OrgStructure/Position
```
Expected: 0 matches (also enforced by `PositionApplicationLayer_DoesNotUseDateTimeOffsetUtcNowDirectly` in Task 11).

```bash
rg -n "Guid\.Empty|00000000-0000-0000-0000-000000000000|LegalEntityIdValue|DepartmentIdValue" src/ONEVO.Domain/Features/OrgStructure/Position src/ONEVO.Application/Features/OrgStructure/Position src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position tests
```
Expected: no active fake-id fallback (`Guid.Empty` may legitimately appear as `if (tenantId == Guid.Empty)` guard checks in every handler — that is a comparison, not a fallback default, and is expected/correct; only flag `?? Guid.Empty` or `LegalEntityIdValue`/`DepartmentIdValue` identifiers as real findings).

```bash
rg -n "enum .*Position|PositionType|SortDirection|PositionSort" src/ONEVO.Application/Features/OrgStructure/Position src/ONEVO.Api/Contracts/OrgStructure/Positions
```
Expected: matches on the string literal usages of `PositionType` (the property name and the `Position.TypeUnique`/`Position.TypePooled` constant references) are expected and fine — these are plain `string`, not an enum declaration. Zero matches on `enum .*Position`, `SortDirection` (the Department enum must never appear in Position code per this plan's design), or `PositionSort`.

- [ ] **Step 5: ASCII scan**

Run against every file this plan created or modified (list them from `git status`/`git diff --stat` against the branch base):

```bash
rg -n "[^\x00-\x7F]" <each touched file>
```
Expected: no matches in any file.

- [ ] **Step 6: Diff whitespace check**

Run: `git diff --check`
Expected: no output (no trailing whitespace or conflict markers).

- [ ] **Step 7: Write `POSITION_FOUNDATION_PART2B_APPLICATION_CONTRACTS_REPORT.md`**

Create this file at the repo root of `HRMS-Backend-v1` with the following sections, populated with the real file list, real test counts, and real command output gathered in Steps 1-6 (do not invent numbers):

```markdown
# Position Foundation Part 2B — Application & Contracts Report

## Files read
[list every file read during planning/implementation research — the Department Part 2B
command/query/validator/handler/DTO/mapper files, IDepartmentRepository, EfDepartmentRepository,
Position Part 2A entities/repository/architecture tests, Department/LegalEntity entities,
Department Part 2B architecture+unit test conventions, existing Position API contracts folder
state, csproj package references, DepartmentsController route template]

## Files changed
### Created
[full list from Tasks 1-11, grouped by layer: Responses, Mappers, RepositoryInterfaces,
Services, Queries, Commands, Api Contracts, Tests, Architecture Tests]

### Modified
- src/ONEVO.Application/Features/OrgStructure/Position/RepositoryInterfaces/IPositionRepository.cs
  (added ListPageAsync, CountHeadDepartmentReferencesAsync — no existing signature changed)
- src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/Position/EfPositionRepository.cs
  (added ListPageAsync, ApplySort, CountHeadDepartmentReferencesAsync — no existing method changed)

## Commands, queries, and contracts added
[table: type name, namespace, purpose]

## Validation rules
[summarize FluentValidation rules per command/query, including the code regex, max lengths,
capacity-vs-type rule, allowlist values for sortBy/sortDirection. Note explicitly: PositionType
is matched case-sensitively against Position.TypeUnique/TypePooled ("unique"/"pooled" only,
lowercase) and is never normalized/trimmed before comparison - "Unique" or " unique" are
rejected by design, not oversight. Page/pageSize bounds are enforced (1-100) but no default is
applied inside this layer; a future Part 2C controller is expected to supply defaults before
constructing ListPositionsQuery, mirroring how ListDepartmentsQuery is invoked today.]

## Route-scope / selected-company rule for legalEntityId
legalEntityId is a property on every Position command/query, populated exclusively by a future
controller from the URL route segment (mirroring DepartmentsController's
`api/v1/org/legal-entities/{legalEntityId:guid}/departments` pattern) — never accepted from a
request body. No Position controller was added in this task.

## tenantId statement
tenantId is never accepted from any request contract, command, or query. Every handler resolves
it exclusively from ICurrentUser.TenantId, matching every existing Department Part 2B handler.

## Role/access statement
No Position command, query, handler, validator, or contract creates, mutates, or references
security roles, permission codes, or access-role assignment. Position screens do not create
roles. Position.DefaultRoleId (a Part 2A legacy field) is never read or written by any Part 2B
command or handler.

## Deferred scope
- Occupant assignment and position_assignments: not implemented. No such table/entity exists
  anywhere in this codebase (verified by repo-wide search) — CheckPositionArchiveCommand reports
  ActiveOccupants as null with ActiveOccupantsCheckSupported=false rather than a fabricated zero.
- Access approval: not implemented (no access-role concept touched by Position in this task).
- Department head assignment: remains deferred. Position contracts/commands never expose a way
  to set departments.head_position_id; Department.HeadPositionId continues to be read-only where
  it already surfaces (Department's own DTOs), unchanged by this task.

## Schema limitations found
- No position_assignments/employee-position table exists, so active-occupant counts cannot be
  measured. Documented in PositionArchiveBlockers.ActiveOccupants (nullable) and
  ActiveOccupantsCheckSupported (false), consistent with DepartmentArchiveDependencyEvaluator's
  precedent for PositionDependencyCheckSupported.

## Test results
[paste the real dotnet test summary lines for both projects, plus a breakdown: N new Position
unit tests across M files, K new Position architecture tests]

## Verification command output
[paste the real output of every rg command from Step 4, and confirm the ASCII scan and
git diff --check were clean]

## Remaining risks
[any real deviation found during Steps 1-6; if none, say so explicitly rather than omitting the
section]
```

- [ ] **Step 8: Final response checklist**

Confirm and state explicitly (do not just imply):
- Exact files changed (created + modified), from `git status`/`git diff --stat`.
- Test counts: total unit tests, total architecture tests, and how many are new.
- Repository methods added: `IPositionRepository.ListPageAsync`, `IPositionRepository.CountHeadDepartmentReferencesAsync` (both also implemented in `EfPositionRepository`).
- Schema limitation: active-occupant checks are unsupported (no `position_assignments` table exists) — this blocked `CheckPositionArchiveCommand.ActiveOccupants` from ever being a real, verified count.
- No controller, routes, or migrations were added.
- No `tenantId`/`legalEntityId` request-body ownership fields were exposed.
- No role creation or access-role mutation was added.
- Remaining risks (from the report).
- Do not commit or push beyond what each task step already committed locally; do not create a PR.

---

