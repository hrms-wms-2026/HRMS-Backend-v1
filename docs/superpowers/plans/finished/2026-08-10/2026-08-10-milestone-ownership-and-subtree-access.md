# Milestone Ownership Signal + Subtree Access Loosening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a server-computed `IsOwner` field to `GetMyProjectMilestones`'s response, and loosen `GetObjectiveSubtree`'s permission check from Head-only to the same membership-fallback pattern `GetObjective` already uses — unblocking the frontend's Milestone Cards + Tree View feature.

**Architecture:** No new tables, no new endpoints. `IsOwner` is computed inline in the existing `GetMyProjectMilestonesQueryHandler` loop (`objective.OwnerId == userId`), mirroring how `Project.IsLead` is already computed elsewhere. `GetObjectiveSubtreeQueryHandler` gains the exact same permission-resolve + ancestor-walk + membership-check block `GetObjectiveByIdQueryHandler` already has (copied, not extracted — the codebase has this pattern once today with no shared helper, so a second occurrence doesn't yet justify a new abstraction).

**Tech Stack:** .NET 10, MediatR, EF Core + Npgsql, xUnit + Moq.

**Design doc:** `docs/superpowers/specs/next/2026-08-10-milestone-ownership-and-subtree-access-design.md`

## Global Constraints

- No new endpoints, no response shape changes beyond adding fields (never removing/renaming existing ones) — both are additive, non-breaking changes to already-shipped contracts.
- `EditObjective`/`AchieveObjective`/`UnachieveObjective` stay Head-only — out of scope, do not touch their permission checks.
- Every unit test file in this plan already exists — modify in place, do not create a parallel test file.

---

### Task 1: Add `IsOwner` to `GetMyProjectMilestones`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/MyProjectMilestoneResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/MyProjectMilestoneViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs`

**Interfaces:**
- Produces: `MyProjectMilestoneResponse.IsOwner` (bool, new final positional parameter) and `MyProjectMilestoneViewModel.IsOwner` (bool, same position) — consumed by the frontend's `Milestone.isOwner` field (separate frontend plan).

- [ ] **Step 1: Write the failing tests**

Append these two tests to `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs`, just above the final closing `}` of the class:

```csharp
    [Fact]
    public async Task Handle_CallerIsOwner_IsOwnerTrue()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() },
            callerId: OwnerId);

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value!).IsOwner);
    }

    [Fact]
    public async Task Handle_CallerIsNotOwner_IsOwnerFalse()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(result.Value!).IsOwner);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyProjectMilestonesQueryHandlerTests"`
Expected: FAIL — compile error, `IsOwner` does not exist on `MyProjectMilestoneResponse`.

- [ ] **Step 3: Add `IsOwner` to the Application DTO**

In `MyProjectMilestoneResponse.cs`, add `bool IsOwner` as the final positional parameter:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record MyProjectMilestoneResponse(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt, bool IsOwner);
```

- [ ] **Step 4: Compute `IsOwner` in the handler**

In `GetMyProjectMilestonesQueryHandler.cs`, the `items.Add(new MyProjectMilestoneResponse(...))` call inside the `foreach (var membership in memberships)` loop currently ends with `membership.IsActive, membership.RemovedAt));`. Change that closing line to add the new argument:

```csharp
            items.Add(new MyProjectMilestoneResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title,
                objective.OwnerId, ownerName, objective.ReportingManagerId, reportingManagerName,
                objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours,
                objective.IsActive, objective.IsAchieved, objective.AchievedAt,
                membership.IsActive, membership.RemovedAt, objective.OwnerId == userId));
```

- [ ] **Step 5: Add `IsOwner` to the API ViewModel**

In `MyProjectMilestoneViewModel.cs`, add `bool IsOwner` as the final positional parameter, same as Step 3:

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record MyProjectMilestoneViewModel(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt, bool IsOwner);
```

- [ ] **Step 6: Pass `IsOwner` through the mapper**

In `ObjectiveViewModelMapper.cs`, the `ToViewModel(this MyProjectMilestoneResponse dto)` method currently ends with `dto.MembershipIsActive, dto.MembershipRemovedAt);`. Change it to:

```csharp
    public static MyProjectMilestoneViewModel ToViewModel(this MyProjectMilestoneResponse dto) => new(
        dto.ObjectiveId, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title,
        dto.OwnerId, dto.OwnerName, dto.ReportingManagerId, dto.ReportingManagerName,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours,
        dto.ObjectiveIsActive, dto.IsAchieved, dto.AchievedAt,
        dto.MembershipIsActive, dto.MembershipRemovedAt, dto.IsOwner);
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyProjectMilestonesQueryHandlerTests"`
Expected: PASS, all tests including the two new ones.

- [ ] **Step 8: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS, no regressions (positional record append is additive; every other call site is either this handler or the mapper, both updated).

- [ ] **Step 9: Update the Postman doc**

In `docs/postman-request/Work Management/My Project Milestones.md`, add `"isOwner": true` to the response JSON example (after `"membershipIsActive": true, "membershipRemovedAt": "datetime|null"`), and add one sentence to the Description section: "`isOwner` is `true` when the caller is this milestone's current Head — computed server-side the same way `Project.isLead` is, so the frontend never needs its own user id."

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/MyProjectMilestoneResponse.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/MyProjectMilestoneViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs "docs/postman-request/Work Management/My Project Milestones.md"
git commit -m "feat(work-management): add isOwner to GetMyProjectMilestones response"
```

---

### Task 2: Loosen `GetObjectiveSubtree` from Head-only to membership-based

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberRepository.HasActiveMembershipForAnyObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, IReadOnlyList<Guid> objectiveIds, CancellationToken ct = default)` (already exists, used by `GetObjectiveByIdQueryHandler`), `IPermissionResolver.ResolveAsync(Guid userId, Guid tenantId, CancellationToken ct)` (already exists, returns `IReadOnlyList<string>` of permission keys, `["*"]` for Super Admin).
- Produces: no change to `GetObjectiveSubtreeQueryHandler`'s public shape — same constructor call site in DI registration (constructor now takes 4 params instead of 2, DI container resolves the two new interfaces automatically since they're already registered for `GetObjectiveByIdQueryHandler`'s use).

- [ ] **Step 1: Write the failing test**

Add this test to `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs`, just above the final closing `}` of the class. It requires two new mocks in `BuildHandler` (added in Step 2 below) — write the test first assuming that signature exists:

```csharp
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
```

- [ ] **Step 2: Update `BuildHandler` to support the new constructor and mocks**

Replace the existing `BuildHandler` method in the same file with:

```csharp
    private (GetObjectiveSubtreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Objective? objective, IReadOnlyList<Objective>? all = null, Guid? callerId = null,
        bool hasReadPermission = true, bool hasMembershipOnAncestor = true)
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

        var handler = new GetObjectiveSubtreeQueryHandler(currentUser.Object, objectives.Object, members.Object, permissionResolver.Object);
        return (handler, objectives);
    }
```

Note: the existing `Handle_CallerNotHead_ReturnsForbidden` test (line 56 today) calls `BuildHandler(objective, all: [objective], callerId: OtherUserId)` with no explicit `hasReadPermission`/`hasMembershipOnAncestor` — the new defaults (`hasReadPermission: true`) would make that test start passing where it previously expected `403`. Fix that existing test in the same step: change its call to `BuildHandler(objective, all: [objective], callerId: OtherUserId, hasReadPermission: false, hasMembershipOnAncestor: false)` so it still asserts the correct "no access at all" `403` case — it's testing "not authorized", not "not Head", now that Head is no longer the only path to authorization.

- [ ] **Step 3: Add the missing `using` statements**

At the top of the test file, add:

```csharp
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveSubtreeQueryHandlerTests"`
Expected: FAIL — compile error, `GetObjectiveSubtreeQueryHandler` constructor doesn't accept 4 arguments yet.

- [ ] **Step 5: Update the handler's permission check**

Replace the full contents of `GetObjectiveSubtreeQueryHandler.cs` with:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
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

    public GetObjectiveSubtreeQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives,
        IProjectMemberRepository members, IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
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

This is the exact same permission-check block `GetObjectiveByIdQueryHandler` already uses (copied, not extracted — see the plan's Architecture note).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetObjectiveSubtreeQueryHandlerTests"`
Expected: PASS, all 7 tests (5 existing + 2 new).

- [ ] **Step 7: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS, no regressions.

- [ ] **Step 8: Update the Postman doc**

In `docs/postman-request/Work Management/Get Objective Subtree.md`, change the `**Permission:**` line from `projects:access + caller must be {id}'s current Head.` to `projects:access + (projects:read/* OR active membership on this milestone or any of its ancestors — same pattern as Get Objective).` and update the `403` row in the Errors table from `Caller is not {id}'s current Head, or lacks projects:access` to `Caller lacks projects:access, or has neither projects:read/* nor an active membership on this milestone or an ancestor of it`.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetObjectiveSubtreeQueryHandlerTests.cs "docs/postman-request/Work Management/Get Objective Subtree.md"
git commit -m "feat(work-management): loosen GetObjectiveSubtree from Head-only to membership-based access"
```

---

### Task 3: Move this plan and its spec to finished, sync SUMMARY.md files

**Files:**
- Modify: `docs/superpowers/plans/SUMMARY.md`
- Modify: `docs/superpowers/plans/next/SUMMARY.md`
- Modify: `docs/superpowers/specs/SUMMARY.md`
- Modify: `docs/superpowers/specs/next/SUMMARY.md`
- Move: this plan file and its design doc into their `finished/2026-08-10/` subfolders

- [ ] **Step 1: Move the plan and spec files**

```bash
mkdir -p docs/superpowers/plans/finished/2026-08-10 docs/superpowers/specs/finished/2026-08-10
git mv docs/superpowers/plans/next/2026-08-10-milestone-ownership-and-subtree-access.md docs/superpowers/plans/finished/2026-08-10/
git mv docs/superpowers/specs/next/2026-08-10-milestone-ownership-and-subtree-access-design.md docs/superpowers/specs/finished/2026-08-10/
```

- [ ] **Step 2: Update `plans/SUMMARY.md`, `plans/next/SUMMARY.md`, `specs/SUMMARY.md`, `specs/next/SUMMARY.md`**

Add a row/entry for this plan and design in each file's `finished` section (following the exact format of the most recent `2026-08-09` entries already there), and remove the corresponding `next/` entries added when the design/plan were created. State clearly in `plans/SUMMARY.md` that this unblocks the frontend's Milestone Cards plan (in the `Hrms--Web-application---front-end---v1` repo).

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/plans/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/specs/SUMMARY.md docs/superpowers/specs/next/SUMMARY.md docs/superpowers/plans/finished/2026-08-10 docs/superpowers/specs/finished/2026-08-10
git commit -m "docs: move milestone ownership/subtree-access plan+spec to finished/2026-08-10"
```
