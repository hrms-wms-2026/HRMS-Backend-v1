# Project Detail Milestone Tree View — Backend DTO Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich `GetObjectiveById` and `GetObjectiveSubtree` responses with `OwnerName`, `ReportingManagerName`, `IsOwner` (all three), plus `IsAchieved`/`AchievedAt` on the Subtree node shape only (the single Get-Objective response already has those two), so the frontend's new milestone tree/detail panel doesn't have to resolve raw GUIDs client-side.

**Architecture:** Both handlers already resolve permission via `IPermissionResolver` and load the target `Objective`(s) via `IObjectiveRepository`. This plan adds one more dependency, `IEmployeeRepository` (already registered in the DI container — it's used today by `GetMyProjectMilestonesQueryHandler`, no new registration needed), batch-resolves display names via its existing `GetByUserIdsAsync`, and threads the resulting `Guid -> "First Last"` dictionary plus the caller's own `Guid` through `ObjectiveMapper.ToDetail`/`ToSubtreeNode` as new **optional, trailing** parameters. Because they're optional and trailing, the two other existing callers of `ToDetail` (`EditObjectiveCommandHandler`, `CreateObjectiveCommandHandler`) keep compiling unchanged and keep their current behavior (no names, `IsOwner` defaults to `false`) — this plan does not touch those two files.

**Tech Stack:** ASP.NET Core, MediatR, xUnit, Moq — matches the rest of `ONEVO.Application`/`ONEVO.Tests.Unit`.

## Global Constraints

- No new backend endpoint, no route change, no permission/auth change — purely additive response fields on two existing `200 OK` shapes. Non-breaking for any existing consumer.
- `ObjectiveMapper.ToDetail`/`ToSubtreeNode` new parameters must be optional with safe defaults (`null`/`null`) so `EditObjectiveCommandHandler.cs` and `CreateObjectiveCommandHandler.cs` require **zero changes** — verified by grep that both only call `ObjectiveMapper.ToDetail(objective)` with a single positional argument.
- Every new field is additive at the **end** of each record's positional parameter list (`ObjectiveDetailResponse`, `ObjectiveSubtreeNodeResponse`) — do not reorder existing parameters, since other call sites construct/deconstruct these records positionally.
- Companion frontend plan: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-10-project-detail-milestone-tree-view.md`. That plan's DTOs assume this backend plan has already shipped — **land this plan first.**

---

### Task 1: `GetObjectiveById` — add `OwnerName`, `ReportingManagerName`, `IsOwner`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeRepository.GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct)` returning `IReadOnlyList<Employee>` (`Employee.UserId`, `.FirstName`, `.LastName`) — already defined in `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`, already implemented and DI-registered (used by `GetMyProjectMilestonesQueryHandler`).
- Produces: `ObjectiveDetailResponse` gains three trailing fields: `string? OwnerName, string? ReportingManagerName, bool IsOwner`. `ObjectiveMapper.ToDetail(Objective objective, IReadOnlyDictionary<Guid,string>? namesByUserId = null, Guid? currentUserId = null)` — the two new params are consumed by Task 2 as well.

- [ ] **Step 1: Write the failing tests**

Open `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs`. Add the `IEmployeeRepository` import and update `BuildHandler` to accept and wire an employee mock, then add two new `[Fact]`s. Replace the whole file with:

```csharp
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private static Objective Target(bool isActive = true, Guid? ownerId = null) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsActive = isActive,
        Title = "Sub", OwnerId = ownerId ?? Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective Parent() => new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = null, IsDefault = true, IsActive = true,
        Title = "Default", OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members) BuildHandler(
        Objective? target, List<string> permissions, bool hasAncestorOrSelfMembership,
        Guid? callerId = null, IReadOnlyList<Employee>? employees = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(Parent());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAncestorOrSelfMembership);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(employees ?? []);

        var handler = new GetObjectiveByIdQueryHandler(currentUser.Object, objectives.Object, members.Object, permissionResolver.Object, employeeRepo.Object);
        return (handler, members);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members) = BuildHandler(Target(), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButAncestorOrSelfMembership_Succeeds()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_MembershipCheckIncludesTargetAndAncestorIds()
    {
        var (handler, members) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: true);

        await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        members.Verify(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, UserId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(ObjectiveId) && ids.Contains(ParentId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNoMembership_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Target(), [], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveObjective_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Target(isActive: false), ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ResolvesOwnerAndReportingManagerNames()
    {
        var ownerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var target = new Objective
        {
            Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ParentId, IsActive = true,
            Title = "Sub", OwnerId = ownerId, ReportingManagerId = managerId,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
        };
        var employees = new List<Employee>
        {
            new() { UserId = ownerId, FirstName = "Jane", LastName = "Doe", EmployeeNumber = "E1", Email = "jane@example.com", HireDate = new DateOnly(2020, 1, 1) },
            new() { UserId = managerId, FirstName = "John", LastName = "Smith", EmployeeNumber = "E2", Email = "john@example.com", HireDate = new DateOnly(2019, 1, 1) }
        };
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false, employees: employees);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane Doe", result.Value!.OwnerName);
        Assert.Equal("John Smith", result.Value.ReportingManagerName);
    }

    [Fact]
    public async Task Handle_IsOwnerTrue_WhenCallerIsTheOwner()
    {
        var target = Target(ownerId: UserId);
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.IsOwner);
    }

    [Fact]
    public async Task Handle_IsOwnerFalse_WhenCallerIsNotTheOwner()
    {
        var target = Target();
        var (handler, _) = BuildHandler(target, ["projects:read"], hasAncestorOrSelfMembership: false);

        var result = await handler.Handle(new GetObjectiveByIdQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.Value!.IsOwner);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetObjectiveByIdQueryHandlerTests`
Expected: build error (or failing tests) — `GetObjectiveByIdQueryHandler` constructor doesn't accept an `IEmployeeRepository` yet, and `ObjectiveDetailResponse` has no `OwnerName`/`ReportingManagerName`/`IsOwner` members.

- [ ] **Step 3: Add the three fields to `ObjectiveDetailResponse`**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs`, replace the file with:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveDetailResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner);
```

- [ ] **Step 4: Update `ObjectiveMapper.ToDetail` to accept the optional name lookup + caller id**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`, replace the `ToDetail` method and add a shared private helper (leave `ToTreeItem` and `ToResponse` untouched; `ToSubtreeNode` is updated in Task 2):

```csharp
    public static ObjectiveDetailResponse ToDetail(
        Objective objective, IReadOnlyDictionary<Guid, string>? namesByUserId = null, Guid? currentUserId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.IsAchieved, objective.AchievedAt, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByUserId), ResolveName(objective.ReportingManagerId, namesByUserId),
        currentUserId.HasValue && objective.OwnerId == currentUserId.Value);

    private static string? ResolveName(Guid? userId, IReadOnlyDictionary<Guid, string>? namesByUserId)
        => userId.HasValue && namesByUserId is not null && namesByUserId.TryGetValue(userId.Value, out var name) ? name : null;
```

- [ ] **Step 5: Inject `IEmployeeRepository` into `GetObjectiveByIdQueryHandler` and resolve names**

Replace `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs` with:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveById;

public class GetObjectiveByIdQueryHandler : IRequestHandler<GetObjectiveByIdQuery, Result<ObjectiveDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEmployeeRepository _employees;

    public GetObjectiveByIdQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver,
        IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
        _employees = employees;
    }

    public async Task<Result<ObjectiveDetailResponse>> Handle(GetObjectiveByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ObjectiveDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ObjectiveDetailResponse>.Forbidden("Tenant context missing.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<ObjectiveDetailResponse>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
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

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, userId, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveDetailResponse>.Forbidden("You do not have access to this milestone.");
        }

        var nameLookupIds = new List<Guid> { objective.OwnerId };
        if (objective.ReportingManagerId.HasValue)
            nameLookupIds.Add(objective.ReportingManagerId.Value);

        var employees = await _employees.GetByUserIdsAsync(tenantId, nameLookupIds, ct);
        var namesByUserId = employees.ToDictionary(e => e.UserId, e => $"{e.FirstName} {e.LastName}");

        return Result<ObjectiveDetailResponse>.Success(ObjectiveMapper.ToDetail(objective, namesByUserId, userId));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetObjectiveByIdQueryHandlerTests`
Expected: all 8 tests PASS.

- [ ] **Step 7: Confirm the two untouched `ToDetail` callers still compile**

Run: `dotnet build src/ONEVO.Application`
Expected: build succeeds — `EditObjectiveCommandHandler.cs` and `CreateObjectiveCommandHandler.cs` both call `ObjectiveMapper.ToDetail(objective)` with a single argument, which still resolves against the new optional-parameter overload unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveDetailResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveByIdQueryHandlerTests.cs
git commit -m "feat: add OwnerName/ReportingManagerName/IsOwner to GetObjectiveById"
```

---

### Task 2: `GetObjectiveSubtree` — add the same three fields, plus `IsAchieved`/`AchievedAt` on subtree nodes

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ObjectiveMapper.ToDetail(objective, namesByUserId, currentUserId)` from Task 1 (used for the `ParentObjective` half of the response).
- Produces: `ObjectiveSubtreeNodeResponse` gains five trailing fields: `string? OwnerName, string? ReportingManagerName, bool IsOwner, bool IsAchieved, DateTimeOffset? AchievedAt`. `ObjectiveMapper.ToSubtreeNode(Objective objective, ILookup<Guid,Objective> childrenByParent, IReadOnlyDictionary<Guid,string>? namesByUserId = null, Guid? currentUserId = null)`.

- [ ] **Step 1: Write the failing tests**

Replace `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
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

    private static Objective Node(Guid id, Guid? parentId, Guid ownerId, bool isDefault = false, bool isActive = true, Guid? reportingManagerId = null) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = parentId,
        IsDefault = isDefault, Title = "N", OwnerId = ownerId, ReportingManagerId = reportingManagerId, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveSubtreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Objective? objective, IReadOnlyList<Objective>? all = null, Guid? callerId = null,
        bool hasReadPermission = true, bool hasMembershipOnAncestor = true, IReadOnlyList<Employee>? employees = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all ?? []);
        if (objective is not null)
        {
            objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, objective.Id, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
            foreach (var node in all ?? [])
                objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, node.Id, It.IsAny<CancellationToken>())).ReturnsAsync(node);
        }

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(
                TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasMembershipOnAncestor);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var employeeRepo = new Mock<IEmployeeRepository>();
        employeeRepo.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(employees ?? []);

        var handler = new GetObjectiveSubtreeQueryHandler(currentUser.Object, objectives.Object, members.Object, permissionResolver.Object, employeeRepo.Object);
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
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId, hasReadPermission: false, hasMembershipOnAncestor: false);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonHeadWithActiveMembershipOnAncestor_ReturnsSuccess()
    {
        var parent = Node(ParentId, parentId: null, ownerId: HeadId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadId);

        var (handler, _) = BuildHandler(
            objective, all: [parent, objective], callerId: OtherUserId,
            hasReadPermission: false, hasMembershipOnAncestor: true);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectiveId, result.Value!.Objective.Id);
    }

    [Fact]
    public async Task Handle_NonHeadWithNoMembershipAnywhereInChain_ReturnsForbidden()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);

        var (handler, _) = BuildHandler(
            objective, all: [objective], callerId: OtherUserId,
            hasReadPermission: false, hasMembershipOnAncestor: false);

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

    [Fact]
    public async Task Handle_ResolvesOwnerNamesAcrossParentAndDescendants()
    {
        var parentOwnerId = Guid.NewGuid();
        var childOwnerId = Guid.NewGuid();
        var parent = Node(ParentId, parentId: null, ownerId: parentOwnerId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadId);
        var child = Node(ChildId, parentId: ObjectiveId, ownerId: childOwnerId);
        var employees = new List<Employee>
        {
            new() { UserId = parentOwnerId, FirstName = "Parent", LastName = "Owner", EmployeeNumber = "E1", Email = "p@example.com", HireDate = new DateOnly(2020, 1, 1) },
            new() { UserId = childOwnerId, FirstName = "Child", LastName = "Owner", EmployeeNumber = "E2", Email = "c@example.com", HireDate = new DateOnly(2020, 1, 1) }
        };

        var (handler, _) = BuildHandler(objective, all: [parent, objective, child], employees: employees);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.Equal("Parent Owner", result.Value!.ParentObjective!.OwnerName);
        var mappedChild = Assert.Single(result.Value.Objective.Children);
        Assert.Equal("Child Owner", mappedChild.OwnerName);
    }

    [Fact]
    public async Task Handle_IsOwnerReflectsTheCallingUser_NotTheHead()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: OtherUserId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId, hasReadPermission: true);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.Objective.IsOwner);
    }

    [Fact]
    public async Task Handle_CarriesIsAchievedAndAchievedAtOntoSubtreeNodes()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        objective.IsAchieved = true;
        objective.AchievedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var (handler, _) = BuildHandler(objective, all: [objective]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.Objective.IsAchieved);
        Assert.Equal(objective.AchievedAt, result.Value.Objective.AchievedAt);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetObjectiveSubtreeQueryHandlerTests`
Expected: build error / failures — constructor and DTO fields don't exist yet.

- [ ] **Step 3: Add the five fields to `ObjectiveSubtreeNodeResponse`**

Replace `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs` with:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveSubtreeResponse(ObjectiveDetailResponse? ParentObjective, ObjectiveSubtreeNodeResponse Objective);

public sealed record ObjectiveSubtreeNodeResponse(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner, bool IsAchieved, DateTimeOffset? AchievedAt,
    IReadOnlyList<ObjectiveSubtreeNodeResponse> Children);
```

- [ ] **Step 4: Update `ObjectiveMapper.ToSubtreeNode`**

In `src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs`, replace the `ToSubtreeNode` method (leave the `ToDetail`/`ResolveName` from Task 1, `ToTreeItem`, and `ToResponse` as they are):

```csharp
    public static ObjectiveSubtreeNodeResponse ToSubtreeNode(
        Objective objective, ILookup<Guid, Objective> childrenByParent,
        IReadOnlyDictionary<Guid, string>? namesByUserId = null, Guid? currentUserId = null) => new(
        objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title, objective.Description,
        objective.OwnerId, objective.ReportingManagerId, objective.CreatedById, objective.StartDate, objective.EndDate,
        objective.Progress, objective.ActualHours, objective.AllocatedHours, objective.CompletedHours,
        objective.IsActive, objective.CreatedAt, objective.UpdatedAt,
        ResolveName(objective.OwnerId, namesByUserId), ResolveName(objective.ReportingManagerId, namesByUserId),
        currentUserId.HasValue && objective.OwnerId == currentUserId.Value,
        objective.IsAchieved, objective.AchievedAt,
        childrenByParent[objective.Id].Select(c => ToSubtreeNode(c, childrenByParent, namesByUserId, currentUserId)).ToList());
```

- [ ] **Step 5: Inject `IEmployeeRepository` into `GetObjectiveSubtreeQueryHandler` and resolve names across the whole project's objectives**

Replace `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs` with:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;

public class GetObjectiveSubtreeQueryHandler : IRequestHandler<GetObjectiveSubtreeQuery, Result<ObjectiveSubtreeResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEmployeeRepository _employees;

    public GetObjectiveSubtreeQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver,
        IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
        _employees = employees;
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

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var ancestor = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (ancestor is null)
                    break;

                selfAndAncestorIds.Add(ancestor.Id);
                cursor = ancestor;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, userId, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<ObjectiveSubtreeResponse>.Forbidden("You do not have access to this milestone.");
        }

        var all = await _objectives.GetAllByProjectIdAsync(tenantId, objective.ProjectId, ct);

        var nameLookupIds = all
            .SelectMany(o => new[] { (Guid?)o.OwnerId, o.ReportingManagerId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var employees = await _employees.GetByUserIdsAsync(tenantId, nameLookupIds, ct);
        var namesByUserId = employees.ToDictionary(e => e.UserId, e => $"{e.FirstName} {e.LastName}");

        var parent = objective.ParentObjectiveId is Guid parentId
            ? all.FirstOrDefault(o => o.Id == parentId)
            : null;

        var childrenByParent = all
            .Where(o => o.ParentObjectiveId.HasValue)
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var response = new ObjectiveSubtreeResponse(
            parent is null ? null : ObjectiveMapper.ToDetail(parent, namesByUserId, userId),
            ObjectiveMapper.ToSubtreeNode(objective, childrenByParent, namesByUserId, userId));

        return Result<ObjectiveSubtreeResponse>.Success(response);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetObjectiveSubtreeQueryHandlerTests`
Expected: all 10 tests PASS.

- [ ] **Step 7: Run the full unit test suite to confirm nothing else broke**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests PASS, including `GetObjectiveByIdQueryHandlerTests` from Task 1 and any tests covering `EditObjectiveCommandHandler`/`CreateObjectiveCommandHandler` (unmodified, should be unaffected).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/ObjectiveSubtreeResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Mappers/ObjectiveMapper.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs
git commit -m "feat: add OwnerName/ReportingManagerName/IsOwner/IsAchieved/AchievedAt to GetObjectiveSubtree"
```

---

### Task 3: Update Postman-style API docs (`PROCESS_RULES.md` rule 6)

**Files:**
- Modify: `docs/postman-request/Work Management/Get Objective.md`
- Modify: `docs/postman-request/Work Management/Get Objective Subtree.md`

**Interfaces:** None — documentation only, no code.

- [ ] **Step 1: Update `Get Objective.md`'s response example**

In `docs/postman-request/Work Management/Get Objective.md`, in the `## Response` section, replace the JSON block with (adds the three new trailing fields):

```json
{
  "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false,
  "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null",
  "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": "decimal|null",
  "allocatedHours": "decimal", "completedHours": "decimal", "isActive": true, "isAchieved": false,
  "achievedAt": "datetime|null", "createdAt": "datetime", "updatedAt": "datetime|null",
  "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": false
}
```

Add one sentence above the JSON block: `Added 2026-08-10: \`ownerName\`, \`reportingManagerName\` (resolved server-side, \`null\` if the referenced employee record can't be found), and \`isOwner\` (true when the caller is the milestone's owner) — added for the Project Detail milestone tree view's detail panel.`

- [ ] **Step 2: Update `Get Objective Subtree.md`'s response example**

In `docs/postman-request/Work Management/Get Objective Subtree.md`, in the `## Response` section, replace the JSON block with:

```json
{
  "parentObjective": { "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true, "isAchieved": false, "achievedAt": "datetime|null", "createdAt": "datetime", "updatedAt": "datetime|null", "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": false } | null,
  "objective": {
    "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null",
    "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date",
    "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true,
    "createdAt": "datetime", "updatedAt": "datetime|null",
    "ownerName": "string|null", "reportingManagerName": "string|null", "isOwner": false, "isAchieved": false, "achievedAt": "datetime|null",
    "children": []
  }
}
```

Replace the line `Each entry in \`children\` has the same shape as \`objective\`, recursively.` with: `Each entry in \`children\` has the same shape as \`objective\`, recursively. Added 2026-08-10: \`ownerName\`/\`reportingManagerName\` (resolved once across every node in the project, \`null\` if not found), \`isOwner\` (per-node, true only when the caller is that specific node's owner — not inherited from an ancestor), and \`isAchieved\`/\`achievedAt\` (previously only returned by the single Get Objective endpoint, now also on every subtree node) — added for the Project Detail milestone tree view.`

- [ ] **Step 3: Commit**

```bash
git add "docs/postman-request/Work Management/Get Objective.md" "docs/postman-request/Work Management/Get Objective Subtree.md"
git commit -m "docs: document OwnerName/ReportingManagerName/IsOwner/IsAchieved/AchievedAt additions"
```

---

## After this plan finishes

- Move this plan file from `plans/next/` to `plans/finished/2026-08-10/` (or the actual completion date) and update `plans/SUMMARY.md`, `plans/finished/SUMMARY.md`, `plans/next/SUMMARY.md` per `FILE_CREATION_RULES.md` rule 2.
- Move the corresponding design doc's status the same way if this was its only remaining dependency (check `docs/superpowers/specs/next/SUMMARY.md` — the design doc lives in the **frontend** repo since it's the shared full-stack spec).
- Notify/trigger the frontend plan (`Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-10-project-detail-milestone-tree-view.md`) — it can now be implemented against the real, shipped response shape.
