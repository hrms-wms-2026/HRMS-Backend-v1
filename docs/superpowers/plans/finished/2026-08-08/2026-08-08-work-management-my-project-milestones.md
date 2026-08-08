# Work Management — "My Milestones In This Project" API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /api/v1/work/projects/{projectId}/objectives/mine` — every milestone in a project the caller has ever had a `project_members` row for (any status), with each milestone's current Head name and Reporting Manager name resolved server-side, per `docs/superpowers/specs/next/2026-08-08-work-management-my-project-milestones-design.md`.

**Architecture:** Same ASP.NET Core / CQRS-via-MediatR / EF Core (Npgsql/PostgreSQL) stack as every other Work Management slice. No schema change. Two small repository additions (one method each on two existing repositories) feed a single new query handler that joins in memory — no raw SQL, no new EF join query.

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql), PostgreSQL, MediatR, xUnit + Moq (unit), `dotnet test`.

## Global Constraints

- Domain must not reference Application/Infrastructure/API/EF Core. Application must not reference Infrastructure or `HttpContext`.
- Every async method takes `CancellationToken`, is awaited; no `.Result`/`.Wait()`.
- `Result`/`Result<T>` exactly as `src/ONEVO.Application/Common/Models/Result.cs` defines — controllers use `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`.
- `tenantId`/`userId` always resolved from `ICurrentUser` inside handlers, never trusted from the request body or route beyond `projectId` itself.
- Raw SQL is forbidden except migration RLS-policy SQL — this plan adds no migration and needs no raw SQL at all.
- **No project-existence validation** (design §2, confirmed with the user): the endpoint always returns `200 OK`, an empty array if the caller has no rows for the given `projectId` — never `404`.
- **All membership statuses included, not just active** (design §2, confirmed with the user after an initial narrower answer was revised): the query must not filter `project_members.is_active` at all. The response carries `membershipIsActive`/`membershipRemovedAt` so the frontend can filter; the API does not pre-filter.
- **No server-side Head/Member role computation** (design §2): the response includes `ownerId` (as it already does for every other Objective response in this feature) so the frontend can compare it to the caller's own `userId` itself. This plan adds no role field.

---

### Task 1: Repository additions — `IEmployeeRepository.GetByUserIdsAsync` and `IProjectMemberRepository.ListForUserInProjectAsync`

**Files:**
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`

**Interfaces:**
- Produces: `IEmployeeRepository.GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct)`, `IProjectMemberRepository.ListForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct)` — both consumed by Task 3's query handler.

Plain data-access methods, no independent logic to unit-test — same precedent as every other repository-only task in this feature (e.g. the original plan's Task 3). Verified by `dotnet build` here, exercised for real by Task 3's handler tests (mocked) and, transitively, by anyone calling the live endpoint later.

- [ ] **Step 1: Add `GetByUserIdsAsync` to `IEmployeeRepository`**

Current file (`src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`):

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
```

Change to:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Batch lookup for name resolution - every Employee row for the given UserIds, in one
    /// query. Used instead of N individual GetByUserIdAsync calls when resolving display names for a
    /// list (e.g. Owner/Reporting-Manager names across every milestone in a project).</summary>
    Task<IReadOnlyList<Employee>> GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement in `EfEmployeeRepository`**

Current file (`src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs`):

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db) => _db = db;

    public async Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);
    }
}
```

Change to:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db) => _db = db;

    public async Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetByUserIdsAsync(Guid tenantId, IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        return await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && userIds.Contains(e.UserId))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: Add `ListForUserInProjectAsync` to `IProjectMemberRepository`**

Add to the existing interface in `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`, alongside `GetActiveObjectiveIdsForUserInProjectAsync`:

```csharp
    /// <summary>
    /// Every project_members row for this exact (project, user) pair, regardless of IsActive -
    /// unlike GetActiveObjectiveIdsForUserInProjectAsync (active-only, Guid list) this returns the
    /// full rows (including IsActive/RemovedAt) for every status, so a caller can show "all
    /// milestones I've ever been connected to in this project" and let the frontend filter by
    /// status instead of the API pre-filtering.
    /// </summary>
    Task<IReadOnlyList<ProjectMember>> ListForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfProjectMemberRepository`**

Add to `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`, alongside `GetActiveObjectiveIdsForUserInProjectAsync`:

```csharp
    public async Task<IReadOnlyList<ProjectMember>> ListForUserInProjectAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId)
            .ToListAsync(ct);
    }
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs
git commit -m "feat(work-management): add batch employee lookup and per-project membership listing repository methods"
```

---

### Task 2: DTOs — `MyProjectMilestoneResponse`, `MyProjectMilestoneViewModel`, mapper entry

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/MyProjectMilestoneResponse.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/MyProjectMilestoneViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`

**Interfaces:**
- Produces: `MyProjectMilestoneResponse` (Application), `MyProjectMilestoneViewModel` (Api), `ToViewModel(this MyProjectMilestoneResponse)` — consumed by Task 3 (response type) and Task 4 (controller mapping).

Plain data holders and one pure mapping function — no independent behavior. Verification is a successful build, same precedent as every DTO-only task in this feature.

- [ ] **Step 1: `MyProjectMilestoneResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record MyProjectMilestoneResponse(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt);
```

- [ ] **Step 2: `MyProjectMilestoneViewModel`**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record MyProjectMilestoneViewModel(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt);
```

- [ ] **Step 3: Add the mapper entry**

Add to `src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs`, alongside the existing `ToViewModel(this ObjectiveHistoryItemResponse dto)`:

```csharp
    public static MyProjectMilestoneViewModel ToViewModel(this MyProjectMilestoneResponse dto) => new(
        dto.ObjectiveId, dto.ProjectId, dto.ParentObjectiveId, dto.IsDefault, dto.Title,
        dto.OwnerId, dto.OwnerName, dto.ReportingManagerId, dto.ReportingManagerName,
        dto.StartDate, dto.EndDate, dto.AllocatedHours, dto.CompletedHours,
        dto.ObjectiveIsActive, dto.IsAchieved, dto.AchievedAt,
        dto.MembershipIsActive, dto.MembershipRemovedAt);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/DTOs/Responses/MyProjectMilestoneResponse.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/MyProjectMilestoneViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Objectives/ObjectiveViewModelMapper.cs
git commit -m "feat(work-management): add MyProjectMilestone DTOs and view model mapper"
```

---

### Task 3: `GetMyProjectMilestonesQuery` + Handler (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectMemberRepository.ListForUserInProjectAsync` (Task 1), `IObjectiveRepository.GetAllByProjectIdAsync` (existing), `IEmployeeRepository.GetByUserIdsAsync` (Task 1).
- Produces: `GetMyProjectMilestonesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<MyProjectMilestoneResponse>>>` — consumed by Task 4's controller.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyProjectMilestonesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultObjectiveId = Guid.NewGuid();
    private static readonly Guid MilestoneId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ReportingManagerId = Guid.NewGuid();

    private static ProjectMember Membership(Guid objectiveId, bool isActive = true, DateTimeOffset? removedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId, UserId = UserId,
        EmployeeId = Guid.NewGuid(), IsActive = isActive, RemovedAt = removedAt, JoinedAt = DateTimeOffset.UtcNow
    };

    private static Objective DefaultObjective() => new()
    {
        Id = DefaultObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
        Title = "Default", OwnerId = OwnerId, ReportingManagerId = null,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), AllocatedHours = 40m, CompletedHours = 0m
    };

    private static Objective Milestone(bool isActive = true, bool isAchieved = false) => new()
    {
        Id = MilestoneId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = DefaultObjectiveId, IsDefault = false, IsActive = isActive,
        Title = "Design Phase", OwnerId = OwnerId, ReportingManagerId = ReportingManagerId, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 20m, CompletedHours = 5m
    };

    private static Employee Owner() => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = OwnerId, FirstName = "Alice", LastName = "Owner" };
    private static Employee ReportingManager() => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = ReportingManagerId, FirstName = "Bob", LastName = "Manager" };

    private (GetMyProjectMilestonesQueryHandler Handler, Mock<IEmployeeRepository> Employees) BuildHandler(
        List<ProjectMember> memberships, List<Objective> objectives, List<Employee>? employees = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? UserId);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListForUserInProjectAsync(TenantId, ProjectId, callerId ?? UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);

        var objectivesRepo = new Mock<IObjectiveRepository>();
        objectivesRepo.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);

        var employeesRepo = new Mock<IEmployeeRepository>();
        employeesRepo.Setup(x => x.GetByUserIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(employees ?? new List<Employee>());

        var handler = new GetMyProjectMilestonesQueryHandler(currentUser.Object, members.Object, objectivesRepo.Object, employeesRepo.Object);
        return (handler, employeesRepo);
    }

    [Fact]
    public async Task Handle_NoMemberships_ReturnsEmptyList()
    {
        var (handler, _) = BuildHandler(new List<ProjectMember>(), new List<Objective>());

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ActiveMembership_ReturnsMilestoneWithResolvedNames()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(MilestoneId, item.ObjectiveId);
        Assert.Equal("Alice Owner", item.OwnerName);
        Assert.Equal("Bob Manager", item.ReportingManagerName);
        Assert.True(item.MembershipIsActive);
        Assert.Null(item.MembershipRemovedAt);
    }

    [Fact]
    public async Task Handle_RemovedMembership_StillIncludedWithMembershipIsActiveFalse()
    {
        var removedAt = DateTimeOffset.UtcNow;
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId, isActive: false, removedAt: removedAt) },
            new List<Objective> { Milestone() },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.False(item.MembershipIsActive);
        Assert.Equal(removedAt, item.MembershipRemovedAt);
    }

    [Fact]
    public async Task Handle_MilestoneSoftDeletedAndAchieved_StillIncludedWithOwnStatus()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone(isActive: false, isAchieved: true) },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.False(item.ObjectiveIsActive);
        Assert.True(item.IsAchieved);
        Assert.True(item.MembershipIsActive);
    }

    [Fact]
    public async Task Handle_DefaultObjectiveMembership_ReportingManagerFieldsNull()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(DefaultObjectiveId) },
            new List<Objective> { DefaultObjective() },
            new List<Employee> { Owner() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.True(item.IsDefault);
        Assert.Null(item.ReportingManagerId);
        Assert.Null(item.ReportingManagerName);
        Assert.Equal("Alice Owner", item.OwnerName);
    }

    [Fact]
    public async Task Handle_UnresolvableEmployee_NameFieldIsNull()
    {
        var (handler, _) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId) },
            new List<Objective> { Milestone() },
            new List<Employee>());

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Null(item.OwnerName);
        Assert.Null(item.ReportingManagerName);
    }

    [Fact]
    public async Task Handle_TwoMilestonesSameOwner_BatchLookupCalledOnceWithDedupedIds()
    {
        var secondMilestoneId = Guid.NewGuid();
        var secondMilestone = new Objective
        {
            Id = secondMilestoneId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = DefaultObjectiveId, IsActive = true,
            Title = "Second Phase", OwnerId = OwnerId, ReportingManagerId = ReportingManagerId,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 10m, CompletedHours = 0m
        };

        var (handler, employees) = BuildHandler(
            new List<ProjectMember> { Membership(MilestoneId), Membership(secondMilestoneId) },
            new List<Objective> { Milestone(), secondMilestone },
            new List<Employee> { Owner(), ReportingManager() });

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        employees.Verify(x => x.GetByUserIdsAsync(TenantId,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Distinct().Count() == ids.Count && ids.Contains(OwnerId) && ids.Contains(ReportingManagerId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var members = new Mock<IProjectMemberRepository>();
        var objectivesRepo = new Mock<IObjectiveRepository>();
        var employeesRepo = new Mock<IEmployeeRepository>();

        var handler = new GetMyProjectMilestonesQueryHandler(currentUser.Object, members.Object, objectivesRepo.Object, employeesRepo.Object);

        var result = await handler.Handle(new GetMyProjectMilestonesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyProjectMilestonesQueryHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `GetMyProjectMilestonesQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;

public sealed record GetMyProjectMilestonesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<MyProjectMilestoneResponse>>>;
```

- [ ] **Step 4: `GetMyProjectMilestonesQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;

public class GetMyProjectMilestonesQueryHandler : IRequestHandler<GetMyProjectMilestonesQuery, Result<IReadOnlyList<MyProjectMilestoneResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IEmployeeRepository _employees;

    public GetMyProjectMilestonesQueryHandler(
        ICurrentUser currentUser, IProjectMemberRepository members,
        IObjectiveRepository objectives, IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _members = members;
        _objectives = objectives;
        _employees = employees;
    }

    public async Task<Result<IReadOnlyList<MyProjectMilestoneResponse>>> Handle(GetMyProjectMilestonesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Forbidden("Tenant context missing.");

        var memberships = await _members.ListForUserInProjectAsync(tenantId, request.ProjectId, userId, ct);
        if (memberships.Count == 0)
            return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(Array.Empty<MyProjectMilestoneResponse>());

        var allObjectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        var objectivesById = allObjectives.ToDictionary(o => o.Id);

        var nameLookupIds = new HashSet<Guid>();
        foreach (var membership in memberships)
        {
            if (!objectivesById.TryGetValue(membership.ObjectiveId, out var objective))
                continue;

            nameLookupIds.Add(objective.OwnerId);
            if (objective.ReportingManagerId.HasValue)
                nameLookupIds.Add(objective.ReportingManagerId.Value);
        }

        var employees = await _employees.GetByUserIdsAsync(tenantId, nameLookupIds.ToList(), ct);
        var namesByUserId = employees.ToDictionary(e => e.UserId, e => $"{e.FirstName} {e.LastName}");

        var items = new List<MyProjectMilestoneResponse>();
        foreach (var membership in memberships)
        {
            if (!objectivesById.TryGetValue(membership.ObjectiveId, out var objective))
                continue;

            namesByUserId.TryGetValue(objective.OwnerId, out var ownerName);
            string? reportingManagerName = null;
            if (objective.ReportingManagerId.HasValue)
                namesByUserId.TryGetValue(objective.ReportingManagerId.Value, out reportingManagerName);

            items.Add(new MyProjectMilestoneResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.IsDefault, objective.Title,
                objective.OwnerId, ownerName, objective.ReportingManagerId, reportingManagerName,
                objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours,
                objective.IsActive, objective.IsAchieved, objective.AchievedAt,
                membership.IsActive, membership.RemovedAt));
        }

        return Result<IReadOnlyList<MyProjectMilestoneResponse>>.Success(items);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetMyProjectMilestonesQueryHandlerTests`
Expected: PASS (8/8).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones tests/ONEVO.Tests.Unit/Features/WorkManagement/GetMyProjectMilestonesQueryHandlerTests.cs
git commit -m "feat(work-management): add GetMyProjectMilestonesQuery vertical slice"
```

---

### Task 4: Controller wiring — `ObjectivesController`

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`

**Interfaces:**
- Consumes: `GetMyProjectMilestonesQuery` (Task 3), `MyProjectMilestoneResponse.ToViewModel()` (Task 2).
- Produces: `GET /api/v1/work/projects/{projectId:guid}/objectives/mine` — the route this whole plan exists to ship.

- [ ] **Step 1: Add the `using` and the new action**

Add to the existing `using` block in `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyProjectMilestones;
```

Add the new action, immediately after the existing `GetTree` action (the last method in the class, before the closing `}`):

```csharp
    /// <summary>Every milestone in this project the caller has ever had a project_members row for, any status - the frontend filters by objectiveIsActive/isAchieved/membershipIsActive as needed. Owner and Reporting Manager names are resolved server-side. No [RequirePermission] beyond the module base gate: this endpoint can only ever return the caller's own rows, so an unrelated projectId just yields an empty array, never 403/404.</summary>
    [HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives/mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetMine(Guid projectId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyProjectMilestonesQuery(projectId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(m => m.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 3: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass, no regressions — every test from Tasks 1-3 plus every pre-existing test in the repo.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs
git commit -m "feat(work-management): wire GET /projects/{projectId}/objectives/mine"
```

---

### Task 5: `docs/postman-request/` doc for the new endpoint

**Files:**
- Create: `docs/postman-request/Work Management/My Project Milestones.md`
- Modify: `docs/postman-request/README.md` — update the Work Management file count/description.

**Interfaces:**
- Consumes: nothing code-facing — required by `docs/superpowers/rules/PROCESS_RULES.md` rule 6, same format as every existing file in this folder.

- [ ] **Step 1: `My Project Milestones.md`**

```markdown
# My Project Milestones

**GET** `/api/v1/work/projects/{projectId}/objectives/mine`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` (module base gate only — this endpoint can only ever return the caller's own rows, so an unrelated `projectId` just yields an empty array, never `403`/`404` beyond the base permission check).

## Description

Every milestone in the given project the caller has ever had a `project_members` row for, at any status (active, removed, or transferred-away) — the frontend is expected to filter by `membershipIsActive`/`objectiveIsActive`/`isAchieved` as needed; the API does not pre-filter to active-only. Each milestone's current Head (`ownerId`) and Reporting Manager (`reportingManagerId`) names are resolved server-side as `ownerName`/`reportingManagerName` (`First Last`, from the matching `Employee` record) — the frontend derives whether the caller themselves is the Head by comparing `ownerId` to their own `userId`; this endpoint does not compute or return a role field. `reportingManagerId`/`reportingManagerName` are `null` for the Default Objective (it has no Reporting Manager). A nonexistent or inaccessible `projectId` returns `200` with an empty array, never `404`.

## Response

`200 OK`

```json
[
  {
    "objectiveId": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false,
    "title": "string", "ownerId": "guid", "ownerName": "string|null",
    "reportingManagerId": "guid|null", "reportingManagerName": "string|null",
    "startDate": "date", "endDate": "date", "allocatedHours": "decimal", "completedHours": "decimal",
    "objectiveIsActive": true, "isAchieved": false, "achievedAt": "datetime|null",
    "membershipIsActive": true, "membershipRemovedAt": "datetime|null"
  }
]
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (`GetMine`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`
Plan: `docs/superpowers/plans/next/2026-08-08-work-management-my-project-milestones.md`
Design: `docs/superpowers/specs/next/2026-08-08-work-management-my-project-milestones-design.md`
```

- [ ] **Step 2: Update `docs/postman-request/README.md`**

In the `Work Management/` bullet under `## Modules`, update the endpoint count and add this endpoint to the enumerated list (the exact wording depends on the file's state at the time this task runs — read it first, then bump the count by one and add "My Project Milestones" to the Objectives group).

- [ ] **Step 3: Commit**

```bash
git add "docs/postman-request/Work Management/My Project Milestones.md" docs/postman-request/README.md
git commit -m "docs(work-management): add postman-request doc for My Project Milestones"
```

---

## Self-review

**Spec coverage** (against `docs/superpowers/specs/next/2026-08-08-work-management-my-project-milestones-design.md`):
- §2 Endpoint contract (route, auth, response shape, no-404-on-missing-project) → Tasks 3, 4.
- §3 Implementation approach (reuse `GetActiveObjectiveIdsForUserInProjectAsync`'s sibling for all-statuses, reuse `GetAllByProjectIdAsync`, batch employee lookup) → Tasks 1, 3. Note: the design's step 1 mentions `GetActiveObjectiveIdsForUserInProjectAsync` as context for why a new all-statuses method is needed — the actual implementation calls the new `ListForUserInProjectAsync` (Task 1), not the active-only method, per the confirmed "all statuses" revision.
- §4 New/changed files → Tasks 1-5 cover every file listed.
- §5 Error handling → Task 3's handler (403 unauthenticated, empty-200 for no rows) + Task 3's tests.
- §6 Testing → Task 3's 8 unit tests cover every scenario the design lists (empty, active, removed-but-included, soft-deleted/achieved-but-included, Default Objective null RM, unresolvable employee, dedup batch call, unauthenticated). No integration test, per the design's explicit call-out of the pre-existing `CreateProjectEndpointTests` fixture issue.

**Placeholder scan:** no "TBD"/"similar to Task N"/unshown code — every step has runnable code or an exact `dotnet`/`git` command, except Task 5 Step 2's README update, which is explicitly left to read-the-file-first because the file's exact current wording will have moved on by execution time (already true as of this plan's writing) — not a placeholder in the forbidden sense (vague instruction with no actual content), but a genuine "read current state, then make the same kind of edit already made to it 2026-08-08" instruction, consistent with how every prior plan's non-mechanical doc-sync steps are written.

**Type consistency:** `MyProjectMilestoneResponse`'s field list (Task 2) matches exactly between its own definition, the handler's construction of it (Task 3), the `ToViewModel()` mapper (Task 2), and `MyProjectMilestoneViewModel`'s own field list (Task 2) — same names, same order, same nullability throughout.
