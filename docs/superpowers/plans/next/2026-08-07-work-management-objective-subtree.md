# Work Management — Objective Subtree (Head-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /api/v1/work/objectives/{id}/tree`, a new endpoint restricted to an Objective's current Head, returning that Objective's parent detail plus its full nested descendant subtree, per `docs/superpowers/specs/next/2026-08-07-work-management-objective-subtree-design.md`.

**Architecture:** Same ASP.NET Core / CQRS-via-MediatR / EF Core (Npgsql/PostgreSQL) stack as the rest of the Objectives feature. One new repository method (no schema change), one new query+handler, two new response DTOs (Application layer) plus their two matching ViewModels (API layer, following this codebase's existing DTO→ViewModel split), one new controller action, one new Postman doc. No new permission code — reuses the already-seeded `projects:access`.

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql), PostgreSQL, MediatR, xUnit + Moq (unit tests), `dotnet test`.

## Global Constraints

- Domain must not reference Application/Infrastructure/API/EF Core. Application must not reference Infrastructure or `HttpContext`.
- Every async method takes `CancellationToken` and is awaited; no `.Result`/`.Wait()`.
- `Result`/`Result<T>` exactly as `src/ONEVO.Application/Common/Models/Result.cs` defines — the controller action uses `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`.
- `tenantId`/`userId` always resolved from `ICurrentUser` inside the handler, never trusted from the route or request.
- No new permission code — `[RequirePermission("projects:access")]` on the new action, matching Delete/Transfer/Edit's existing attribute on this same controller (verified in `ObjectivesController.cs`; only `GetTree` lacks it, since that one authorizes via project membership instead).
- No schema/migration change — this task only reads existing `objectives` rows.
- This endpoint is independent of the existing `GET /projects/{projectId}/objectives` (`GetObjectiveTree`) endpoint and of the queued `docs/superpowers/specs/next/2026-08-06-work-management-milestone-membership-and-achieve-design.md` §5 rework of that endpoint — neither is touched by this plan.
- This codebase does not unit-test repository methods or controller actions directly (verified: no `EfObjectiveRepositoryTests.cs`, `ObjectiveMapperTests.cs`, or `ObjectivesControllerTests.cs` exist today) — repository behavior is exercised indirectly through handler tests with mocked repositories, and controller actions are thin pass-throughs verified by build + manual review. This plan follows that same convention, with one deliberate addition: a direct mapper test for the new recursive `ToSubtreeNode`, since recursion is new logic in this feature that a mocked handler test alone would only exercise shallowly.

---

### Task 1: Repository method — `GetAllByProjectIdAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)` — every Objective for a project, active or not. Consumed by Task 3's handler.

- [ ] **Step 1: Add the method to the interface**

In `IObjectiveRepository.cs`, add below the existing `GetTreeByProjectIdAsync` declaration:

```csharp
    /// <summary>Every Objective for a Project regardless of IsActive, unordered - used to build a
    /// Head-scoped subtree in memory. Unlike GetTreeByProjectIdAsync, does not filter to active-only.</summary>
    Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `EfObjectiveRepository.cs`**

Add below the existing `GetTreeByProjectIdAsync` implementation:

```csharp
    public async Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId)
            .ToListAsync(ct);
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs
git commit -m "feat: add GetAllByProjectIdAsync to IObjectiveRepository"
```

---

### Task 2: Response DTOs + recursive mapper (`ToSubtreeNode`)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveMapperTests.cs`

**Interfaces:**
- Consumes: `Objective` domain entity (`src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs` — `Id`, `ProjectId`, `ParentObjectiveId`, `IsDefault`, `Title`, `Description`, `OwnerId`, `ReportingManagerId`, `CreatedById`, `StartDate`, `EndDate`, `Progress`, `ActualHours`, `AllocatedHours`, `CompletedHours`, `IsActive`, `CreatedAt`, `UpdatedAt`).
- Produces: `ObjectiveSubtreeResponse(ObjectiveDetailResponse? ParentObjective, ObjectiveSubtreeNodeResponse Objective)`, `ObjectiveSubtreeNodeResponse(..., IReadOnlyList<ObjectiveSubtreeNodeResponse> Children)`, and `ObjectiveMapper.ToSubtreeNode(Objective objective, ILookup<Guid, Objective> childrenByParent) : ObjectiveSubtreeNodeResponse`. Consumed by Task 3's handler.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveMapperTests.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ObjectiveMapperTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid Child1Id = Guid.NewGuid();
    private static readonly Guid Child2Id = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();

    private static Objective Node(Guid id, Guid? parentId, bool isActive = true) => new()
    {
        Id = id, TenantId = TenantId, ParentObjectiveId = parentId, Title = "N",
        OwnerId = Guid.NewGuid(), IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void ToSubtreeNode_NestsChildrenRecursively()
    {
        var root = Node(RootId, parentId: null);
        var child1 = Node(Child1Id, parentId: RootId);
        var child2 = Node(Child2Id, parentId: RootId);
        var grandchild = Node(GrandchildId, parentId: Child1Id);

        var childrenByParent = new[] { child1, child2, grandchild }
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var result = ObjectiveMapper.ToSubtreeNode(root, childrenByParent);

        Assert.Equal(RootId, result.Id);
        Assert.Equal(2, result.Children.Count);

        var mappedChild1 = Assert.Single(result.Children, c => c.Id == Child1Id);
        var grandchildNode = Assert.Single(mappedChild1.Children);
        Assert.Equal(GrandchildId, grandchildNode.Id);
        Assert.Empty(grandchildNode.Children);

        var mappedChild2 = Assert.Single(result.Children, c => c.Id == Child2Id);
        Assert.Empty(mappedChild2.Children);
    }

    [Fact]
    public void ToSubtreeNode_IncludesInactiveChildren()
    {
        var root = Node(RootId, parentId: null);
        var inactiveChild = Node(Child1Id, parentId: RootId, isActive: false);

        var childrenByParent = new[] { inactiveChild }.ToLookup(o => o.ParentObjectiveId!.Value);

        var result = ObjectiveMapper.ToSubtreeNode(root, childrenByParent);

        var mappedChild = Assert.Single(result.Children);
        Assert.False(mappedChild.IsActive);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ObjectiveMapperTests`
Expected: FAIL — `ObjectiveSubtreeResponse`/`ObjectiveMapper.ToSubtreeNode` don't exist yet (compile error).

- [ ] **Step 3: Create the response DTOs**

Create `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveSubtreeResponse(ObjectiveDetailResponse? ParentObjective, ObjectiveSubtreeNodeResponse Objective);

public sealed record ObjectiveSubtreeNodeResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    IReadOnlyList<ObjectiveSubtreeNodeResponse> Children);
```

- [ ] **Step 4: Add `ToSubtreeNode` to `ObjectiveMapper`**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`, add this method inside the existing `ObjectiveMapper` class (alongside `ToDetail`/`ToTreeItem`):

```csharp
    public static ObjectiveSubtreeNodeResponse ToSubtreeNode(Objective objective, ILookup<Guid, Objective> childrenByParent) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt,
        childrenByParent[objective.Id].Select(c => ToSubtreeNode(c, childrenByParent)).ToList());
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ObjectiveMapperTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/ObjectiveMapperTests.cs
git commit -m "feat: add ObjectiveSubtreeResponse DTOs and recursive ToSubtreeNode mapper"
```

---

### Task 3: `GetObjectiveSubtreeQuery` + handler + unit tests

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync(Guid, Guid, CancellationToken)` (existing), `IObjectiveRepository.GetAllByProjectIdAsync(Guid, Guid, CancellationToken)` (Task 1), `ObjectiveMapper.ToDetail(Objective)` (existing), `ObjectiveMapper.ToSubtreeNode(Objective, ILookup<Guid, Objective>)` (Task 2), `ICurrentUser.IsAuthenticated/TenantId/UserId` (existing).
- Produces: `GetObjectiveSubtreeQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveSubtreeResponse>>` and `GetObjectiveSubtreeQueryHandler`. Consumed by Task 4's controller action.

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveSubtreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();

    private static Objective Node(Guid id, Guid? parentId, Guid ownerId, bool isDefault = false, bool isActive = true) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = parentId,
        IsDefault = isDefault, Title = "N", OwnerId = ownerId, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveSubtreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Objective? objective, IReadOnlyList<Objective>? all = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all ?? []);

        var handler = new GetObjectiveSubtreeQueryHandler(currentUser.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsNullParent()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ParentObjective);
        Assert.Equal(ObjectiveId, result.Value.Objective.Id);
        Assert.Empty(result.Value.Objective.Children);
    }

    [Fact]
    public async Task Handle_HeadWithParentAndDescendants_ReturnsNestedTree()
    {
        var parent = Node(ParentId, parentId: null, ownerId: OtherUserId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadId);
        var child = Node(ChildId, parentId: ObjectiveId, ownerId: HeadId);
        var grandchild = Node(GrandchildId, parentId: ChildId, ownerId: HeadId);

        var (handler, _) = BuildHandler(objective, all: [parent, objective, child, grandchild]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParentId, result.Value!.ParentObjective!.Id);

        var mappedChild = Assert.Single(result.Value.Objective.Children);
        Assert.Equal(ChildId, mappedChild.Id);

        var mappedGrandchild = Assert.Single(mappedChild.Children);
        Assert.Equal(GrandchildId, mappedGrandchild.Id);
    }

    [Fact]
    public async Task Handle_IncludesInactiveDescendants()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        var inactiveChild = Node(ChildId, parentId: ObjectiveId, ownerId: HeadId, isActive: false);

        var (handler, _) = BuildHandler(objective, all: [objective, inactiveChild]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        var mappedChild = Assert.Single(result.Value!.Objective.Children);
        Assert.False(mappedChild.IsActive);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveSubtreeQueryHandlerTests`
Expected: FAIL — `GetObjectiveSubtreeQuery`/`GetObjectiveSubtreeQueryHandler` don't exist yet (compile error).

- [ ] **Step 3: Create the query**

Create `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public sealed record GetObjectiveSubtreeQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveSubtreeResponse>>;
```

- [ ] **Step 4: Create the handler**

Create `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public class GetObjectiveSubtreeQueryHandler : IRequestHandler<GetObjectiveSubtreeQuery, Result<ObjectiveSubtreeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;

    public GetObjectiveSubtreeQueryHandler(ICurrentUser currentUser, IObjectiveRepository objectives)
    {
        _currentUser = currentUser;
        _objectives = objectives;
    }

    public async Task<Result<ObjectiveSubtreeResponse>> Handle(GetObjectiveSubtreeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<ObjectiveSubtreeResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != userId)
            return Result<ObjectiveSubtreeResponse>.Forbidden("Only this milestone's head can view its subtree.");

        var all = await _objectives.GetAllByProjectIdAsync(tenantId, objective.ProjectId, ct);

        var parent = objective.ParentObjectiveId is Guid parentId
            ? all.FirstOrDefault(o => o.Id == parentId)
            : null;

        var childrenByParent = all
            .Where(o => o.ParentObjectiveId.HasValue)
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var response = new ObjectiveSubtreeResponse(
            parent is null ? null : ObjectiveMapper.ToDetail(parent),
            ObjectiveMapper.ToSubtreeNode(objective, childrenByParent));

        return Result<ObjectiveSubtreeResponse>.Success(response);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetObjectiveSubtreeQueryHandlerTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs
git commit -m "feat: add GetObjectiveSubtreeQuery and handler"
```

---

### Task 4: Controller action + API ViewModels

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`

**Interfaces:**
- Consumes: `GetObjectiveSubtreeQuery` (Task 3), `ObjectiveDetailViewModel` (existing), `ObjectiveDetailResponse.ToViewModel()` (existing).
- Produces: `GET /api/v1/work/objectives/{id}/tree` HTTP endpoint. Nothing downstream in this codebase consumes this — it's the outermost layer.

- [ ] **Step 1: Add the ViewModels**

Create `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs`:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveSubtreeViewModel(ObjectiveDetailViewModel? ParentObjective, ObjectiveSubtreeNodeViewModel Objective);

public sealed record ObjectiveSubtreeNodeViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    IReadOnlyList<ObjectiveSubtreeNodeViewModel> Children);
```

- [ ] **Step 2: Add mapper extensions**

In `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, add these two methods inside the existing `ObjectiveViewModelMapper` class:

```csharp
    public static ObjectiveSubtreeViewModel ToViewModel(this ObjectiveSubtreeResponse dto) => new(
        dto.ParentObjective?.ToViewModel(), dto.Objective.ToViewModel());

    public static ObjectiveSubtreeNodeViewModel ToViewModel(this ObjectiveSubtreeNodeResponse dto) => new(
        dto.Id, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title, dto.Description,
        dto.OwnerId, dto.ReportingManagerId, dto.CreatedById, dto.StartDate, dto.EndDate,
        dto.Progress, dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt,
        dto.Children.Select(c => c.ToViewModel()).ToList());
```

- [ ] **Step 3: Add the controller action**

In `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`, add this `using` alongside the existing ones:

```csharp
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
```

Then add this action after `Transfer` and before `ApproveChangeRequest`:

```csharp
    /// <summary>An Objective's parent detail plus its full nested descendant subtree. Caller must be {id}'s current Head.</summary>
    [HttpGet("{id:guid}/tree")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetSubtree(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveSubtreeQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 5: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS, no regressions (all pre-existing Objectives tests plus the new ones from Tasks 2–3).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveSubtreeViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs
git commit -m "feat: wire GET /api/v1/work/objectives/{id}/tree"
```

---

### Task 5: Postman documentation

**Files:**
- Create: `docs/postman-request/Work Management/Get Objective Subtree.md`

- [ ] **Step 1: Write the doc**

Create `docs/postman-request/Work Management/Get Objective Subtree.md`:

```markdown
# Get Objective Subtree

**GET** `/api/v1/work/objectives/{id}/tree`

**Auth:** Tenant session cookie + CSRF header. Policy: `TenantPolicy`. **Permission:** `projects:access` + caller must be `{id}`'s current Head.

## Description

Returns `{id}`'s parent Objective detail (if any) plus its full nested descendant subtree (children, grandchildren, ...), each carrying the full detail field set. Independent of `GET /api/v1/work/projects/{projectId}/objectives` — this is a Head-only, single-milestone read, not a project-wide one. Inactive (soft-deleted) descendants are included; the client filters on `isActive` if it only wants live nodes.

## Response

`200 OK`:

```json
{
  "parentObjective": { "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null" } | null,
  "objective": {
    "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null",
    "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date",
    "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true,
    "createdAt": "datetime", "updatedAt": "datetime|null",
    "children": []
  }
}
```

`parentObjective` is `null` when `{id}` has no parent (i.e., it's the Project's Default Objective). Each entry in `children` has the same shape as `objective`, recursively.

## Errors

| Status | Cause |
|---|---|
| `403` | Caller is not `{id}`'s current Head, or lacks `projects:access` |
| `404` | Objective doesn't exist in tenant |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetSubtree`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-07-work-management-objective-subtree.md`
```

- [ ] **Step 2: Commit**

```bash
git add "docs/postman-request/Work Management/Get Objective Subtree.md"
git commit -m "docs: add Postman doc for Get Objective Subtree"
```
