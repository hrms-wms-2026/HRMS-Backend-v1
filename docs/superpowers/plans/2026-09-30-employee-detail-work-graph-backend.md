# Employee Work Graph — Backend Implementation Plan (Plan 1, backend half)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /api/v1/employees/{id}/work-graph`, returning the projects, modules (objectives) and open tasks around one employee as a node/link graph, behind a reusable employee read-access guard.

**Architecture:** A new `IEmployeeReadAccessGuard` (404/403/visible-row) is reused by this and later Overview widget handlers. `GetEmployeeWorkGraphQueryHandler` (Work Management feature) composes four new repository reads into `{nodes, links, hiddenTaskCount}`. The controller action is a thin MediatR dispatch.

**Tech Stack:** .NET / MediatR / EF Core (Npgsql; InMemory in unit tests) / xUnit + Moq. Clean Architecture: Application must not reference EF Core (enforced by `LayerDependencyTests`).

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md` (§4 decisions, §5 contract). Frontend half: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-09-30-employee-detail-employment-tab-frontend.md`.

## Global Constraints

- Repo is `HRMS-Backend-v1`, branch `feature/task-subtasks` (verify with `git branch --show-current` before every commit; other sessions share this tree — re-run `git status --short` right before each commit and stage only the files listed in that task).
- **No git worktrees.**
- No `.sln`: build/test per project: `dotnet test tests/ONEVO.Tests.Unit`, `dotnet test tests/ONEVO.Tests.Architecture`.
- The `ONEVO.Api.exe` dev server auto-respawns: run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell) **immediately before each `dotnet test`/`dotnet build`**, not once per session.
- Existing endpoints `GET /employees/{id}/detail` and `GET /employees/{id}/position-history` are NOT modified.
- Default objectives (`Objective.IsDefault`) are never emitted as module nodes (spec §4.2).
- Graph task cap is **60** (`MaxTaskNodes`), only status categories `not_started` and `active` (spec §4.4).
- Access = `employees:read` permission + coverage visibility; `org:manage` = unrestricted (spec §4.5).
- Controllers depend only on `IMediator` (enforced by `EmployeesControllerArchitectureTests`).
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: Employee read-access guard

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/ServiceInterfaces/IEmployeeReadAccessGuard.cs`
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/EmployeeReadAccessGuard.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (after the `IEmployeeManageScopeGuard` registration, ~line 315)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeReadAccessGuardTests.cs`

**Interfaces:**
- Produces: `IEmployeeReadAccessGuard.EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default) : Task<Result<EmployeeListItemResponse>>` — 404 when the employee is not in the tenant, 403 when outside the caller's visibility, else success carrying the visible row (`FullName`, `PositionName`, …). Task 3 consumes it.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Infrastructure.Services.CoreHr;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeReadAccessGuardTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public EmployeeReadAccessGuardTests()
    {
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
    }

    private EmployeeReadAccessGuard CreateGuard() =>
        new(_currentUser.Object, _scopeResolver.Object, _employees.Object);

    private static EmployeeListItemResponse VisibleRow(Guid id) =>
        new(id, "E-001", "Ada Lovelace", "ada@test.dev",
            null, null, null, "Engineer", null, null, "full_time", "active", null, null);

    private void ArrangeExisting() =>
        _employees.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });

    [Fact]
    public async Task EnsureCanRead_ReturnsNotFound_WhenEmployeeDoesNotExistInTenant()
    {
        _employees.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task EnsureCanRead_ReturnsForbidden_WhenEmployeeIsOutsideCallerScope()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(r => r.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task EnsureCanRead_ReturnsVisibleRow_WhenEmployeeIsInsideCallerScope()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(r => r.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisibleRow(_employeeId));

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada Lovelace", result.Value!.FullName);
    }

    [Fact]
    public async Task EnsureCanRead_SkipsScopeResolution_WhenCallerHasOrgManage()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employees.Setup(r => r.GetVisibleByIdAsync(
                _tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisibleRow(_employeeId));

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _scopeResolver.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (PowerShell): `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeReadAccessGuardTests"`
Expected: build FAIL — `EmployeeReadAccessGuard` does not exist.

- [ ] **Step 3: Write the interface**

`src/ONEVO.Application/Features/CoreHr/Employee/ServiceInterfaces/IEmployeeReadAccessGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

/// <summary>
/// Read-side access check for one employee, shared by every per-widget employee endpoint:
/// 404 when the employee is not in the tenant, 403 when outside the caller's visibility scope
/// (org:manage = unrestricted), otherwise the visible list-item row (name, position, ...).
/// GetEmployeeDetail/GetEmployeePositionHistory inline the same checks and are intentionally
/// left untouched.
/// </summary>
public interface IEmployeeReadAccessGuard
{
    Task<Result<EmployeeListItemResponse>> EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the implementation**

`src/ONEVO.Infrastructure/Services/CoreHr/EmployeeReadAccessGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr;

public sealed class EmployeeReadAccessGuard(
    ICurrentUser currentUser,
    IEmployeeVisibilityScopeResolver visibilityScopeResolver,
    IEmployeeRepository employeeRepository) : IEmployeeReadAccessGuard
{
    public const string NotFoundMessage = "The employee or selected organization record could not be found.";
    public const string ForbiddenMessage = "You do not have access to view this employee.";

    public async Task<Result<EmployeeListItemResponse>> EnsureCanRead(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var existing = await employeeRepository.GetByIdAsync(tenantId, employeeId, ct);
        if (existing is null)
            return Result<EmployeeListItemResponse>.NotFound(NotFoundMessage);

        var scope = currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await visibilityScopeResolver.ResolveAsync(tenantId, currentUser.UserId, ct);

        var visible = await employeeRepository.GetVisibleByIdAsync(tenantId, scope, employeeId, ct);
        return visible is null
            ? Result<EmployeeListItemResponse>.Forbidden(ForbiddenMessage)
            : Result<EmployeeListItemResponse>.Success(visible);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, directly after the line registering `IEmployeeManageScopeGuard`, add:

```csharp
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces.IEmployeeReadAccessGuard, ONEVO.Infrastructure.Services.CoreHr.EmployeeReadAccessGuard>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeReadAccessGuardTests"`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/ServiceInterfaces/IEmployeeReadAccessGuard.cs src/ONEVO.Infrastructure/Services/CoreHr/EmployeeReadAccessGuard.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeReadAccessGuardTests.cs
git commit -m "feat(people): add shared employee read-access guard

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Work Management repository reads for the graph

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`, `EfObjectiveRepository.cs`, `EfProjectRepository.cs`, `EfWorkTaskRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkGraphRepositoryReadsTests.cs`

**Interfaces:**
- Produces (all on the existing interfaces; no other implementers exist — verified by grep, only the `Ef*` classes):
  - `IProjectMemberRepository.ListActiveForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default) : Task<IReadOnlyList<ProjectMember>>`
  - `IObjectiveRepository.ListActiveOwnedByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default) : Task<IReadOnlyList<Objective>>`
  - `IProjectRepository.GetActiveByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) : Task<IReadOnlyList<Project>>`
  - `IWorkTaskRepository.ListOpenAssignedToEmployeeAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default) : Task<OpenAssignedTasksPage>`
  - `sealed record OpenAssignedTaskRow(Guid Id, string ShortId, string Title, Guid ProjectId, Guid ObjectiveId, string Category)` and `sealed record OpenAssignedTasksPage(IReadOnlyList<OpenAssignedTaskRow> Items, int TotalCount)`, declared in `IWorkTaskRepository.cs` next to `MyTaskRow`.
- Reuses existing: `IObjectiveRepository.GetByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeWorkGraphRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();

    [Fact]
    public async Task ProjectMembers_ListActiveForEmployee_ReturnsOnlyActiveRowsOfThatEmployee()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.ProjectMembers.AddRange(
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _employeeId, IsActive = true },
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _employeeId, IsActive = false },
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(), EmployeeId = _otherEmployeeId, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfProjectMemberRepository(db).ListActiveForEmployeeAsync(_tenantId, _employeeId);

        var row = Assert.Single(rows);
        Assert.Equal(_employeeId, row.EmployeeId);
    }

    [Fact]
    public async Task Objectives_ListActiveOwnedByEmployee_ExcludesInactiveAndOtherOwners()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var owned = new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Owned", OwnerId = _employeeId, IsActive = true };
        db.Objectives.AddRange(
            owned,
            new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Inactive", OwnerId = _employeeId, IsActive = false },
            new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Title = "Other", OwnerId = _otherEmployeeId, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfObjectiveRepository(db).ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId);

        Assert.Equal(owned.Id, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task Projects_GetActiveByIds_ReturnsOnlyRequestedActiveProjects()
    {
        await using var db = BuildInMemoryDb();
        var active = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Active", Identifier = "ACT", IsActive = true };
        var inactive = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Inactive", Identifier = "OFF", IsActive = false };
        var notRequested = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Other", Identifier = "OTH", IsActive = true };
        db.Projects.AddRange(active, inactive, notRequested);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfProjectRepository(db)
            .GetActiveByIdsForTenantAsync(_tenantId, new[] { active.Id, inactive.Id });

        Assert.Equal(active.Id, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task WorkTasks_ListOpenAssigned_ReturnsNotStartedAndActiveOnly_ActiveFirst_WithTotalAndTake()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var notStarted = NewStatus(projectId, "not_started");
        var active = NewStatus(projectId, "active");
        var done = NewStatus(projectId, "done");
        db.TaskStatuses.AddRange(notStarted, active, done);
        var t1 = NewTask(projectId, objectiveId, notStarted.Id, "WEB-1");
        var t2 = NewTask(projectId, objectiveId, active.Id, "WEB-2");
        var t3 = NewTask(projectId, objectiveId, done.Id, "WEB-3");
        var t4 = NewTask(projectId, objectiveId, active.Id, "WEB-4");
        db.WorkTasks.AddRange(t1, t2, t3, t4);
        db.TaskAssignments.AddRange(Assign(t1.Id), Assign(t2.Id), Assign(t3.Id));
        db.TaskAssignments.Add(new TaskAssignment { Id = Guid.NewGuid(), TaskId = t4.Id, UserId = Guid.NewGuid(), EmployeeId = _otherEmployeeId, AssignedById = Guid.NewGuid() });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new EfWorkTaskRepository(db).ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, take: 1);

        Assert.Equal(2, page.TotalCount);
        var first = Assert.Single(page.Items);
        Assert.Equal("WEB-2", first.ShortId);
        Assert.Equal("active", first.Category);
    }

    private WmTaskStatus NewStatus(Guid projectId, string category) =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = category, Category = category };

    private WorkTask NewTask(Guid projectId, Guid objectiveId, Guid statusId, string shortId) =>
        new() { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = objectiveId, StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId };

    private TaskAssignment Assign(Guid taskId) =>
        new() { Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = _employeeId, AssignedById = Guid.NewGuid() };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkGraphRepositoryReadsTests"`
Expected: build FAIL — the four new methods do not exist.

- [ ] **Step 3: Add the interface members**

`IProjectMemberRepository.cs` — add before `void Update(ProjectMember member);`:

```csharp
    /// <summary>Every active project_members row for this employee across all projects/objectives.</summary>
    Task<IReadOnlyList<ProjectMember>> ListActiveForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
```

`IObjectiveRepository.cs` — add before `void Update(Objective objective);`:

```csharp
    /// <summary>Active objectives owned by this employee, any project, no date window.</summary>
    Task<IReadOnlyList<Objective>> ListActiveOwnedByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
```

`IProjectRepository.cs` — add inside the interface:

```csharp
    /// <summary>Active (IsActive) projects among the given ids, not-soft-deleted, tenant-scoped.</summary>
    Task<IReadOnlyList<Project>> GetActiveByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
```

`IWorkTaskRepository.cs` — add the two records after `MyTaskRow`:

```csharp
/// <summary>A not_started/active task assigned to an employee, for the employee work graph.</summary>
public sealed record OpenAssignedTaskRow(
    Guid Id,
    string ShortId,
    string Title,
    Guid ProjectId,
    Guid ObjectiveId,
    string Category);

public sealed record OpenAssignedTasksPage(IReadOnlyList<OpenAssignedTaskRow> Items, int TotalCount);
```

and this member inside the interface (next to `GetMyTaskProgressRowsAsync`):

```csharp
    /// <summary>Tasks assigned to this employee whose status category is not_started or active,
    /// active first then by ShortId, capped at take; TotalCount is the uncapped total.</summary>
    Task<OpenAssignedTasksPage> ListOpenAssignedToEmployeeAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in the Ef repositories**

`EfProjectMemberRepository.cs` (inside the class):

```csharp
    public async Task<IReadOnlyList<ProjectMember>> ListActiveForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId && m.IsActive)
            .ToListAsync(ct);
    }
```

`EfObjectiveRepository.cs` (inside the class, next to `GetOwnedByEmployeeIdWithinRangeAsync`):

```csharp
    public async Task<IReadOnlyList<Objective>> ListActiveOwnedByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => await _db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.OwnerId == employeeId && o.IsActive)
            .ToListAsync(ct);
```

`EfProjectRepository.cs` (inside the class):

```csharp
    public async Task<IReadOnlyList<Project>> GetActiveByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<Project>();

        return await _db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive && ids.Contains(p.Id))
            .ToListAsync(ct);
    }
```

`EfWorkTaskRepository.cs` (inside the class; `TaskStatusCategories` is already in scope via the existing `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;`):

```csharp
    public async Task<OpenAssignedTasksPage> ListOpenAssignedToEmployeeAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default)
    {
        var query =
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && (s.Category == TaskStatusCategories.NotStarted || s.Category == TaskStatusCategories.Active)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select new { Task = t, s.Category };

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Category)          // "active" sorts before "not_started"
            .ThenBy(x => x.Task.ShortId)
            .Take(take)
            .Select(x => new OpenAssignedTaskRow(x.Task.Id, x.Task.ShortId, x.Task.Title, x.Task.ProjectId, x.Task.ObjectiveId, x.Category))
            .ToListAsync(ct);

        return new OpenAssignedTasksPage(items, total);
    }
```

- [ ] **Step 5: Run to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkGraphRepositoryReadsTests"`
Expected: 4 passed.

- [ ] **Step 6: Run the layer-dependency architecture tests**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LayerDependencyTests"`
Expected: all pass (Application still has no EF reference).

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkGraphRepositoryReadsTests.cs
git commit -m "feat(work): add repository reads for the employee work graph

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

(`git add` on the two directories is safe only if `git status --short` shows no unrelated changes under them — if it does, add the eight modified files by explicit path instead.)

---

### Task 3: GetEmployeeWorkGraph query + handler

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeWorkGraph/DTOs/Responses/EmployeeWorkGraphResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeWorkGraph/Queries/GetEmployeeWorkGraph/GetEmployeeWorkGraphQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeWorkGraph/Queries/GetEmployeeWorkGraph/GetEmployeeWorkGraphQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkGraphQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard.EnsureCanRead` (Task 1); the four repository reads from Task 2; existing `IObjectiveRepository.GetByIdsForTenantAsync`; `ICurrentUser.TenantId`.
- Produces: `sealed record GetEmployeeWorkGraphQuery(Guid EmployeeId) : IRequest<Result<EmployeeWorkGraphResponse>>`; response/records exactly as in the spec §5 (below). Task 4 consumes the query.

- [ ] **Step 1: Write the failing tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkGraphQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();

    public GetEmployeeWorkGraphQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Saif Ahamed", "saif@test.dev",
                null, null, null, "Watercraft Engineer", null, null, "full_time", "active", null, null)));
        _members.Setup(m => m.ListActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProjectMember>());
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _tasks.Setup(t => t.ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAssignedTasksPage(Array.Empty<OpenAssignedTaskRow>(), 0));
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Objective>());
        _projects.Setup(p => p.GetActiveByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Project>());
    }

    private GetEmployeeWorkGraphQueryHandler CreateHandler() =>
        new(_guard.Object, _members.Object, _objectives.Object, _projects.Object, _tasks.Object, _currentUser.Object);

    private Project Proj(string name) => new() { Id = _projectId, TenantId = _tenantId, Name = name, Identifier = "P", IsActive = true };

    private Objective Obj(Guid id, string title, Guid ownerId, bool isDefault = false) =>
        new() { Id = id, TenantId = _tenantId, ProjectId = _projectId, Title = title, OwnerId = ownerId, IsDefault = isDefault, IsActive = true };

    private void ArrangeObjectivesAndProjects(params Objective[] objectives)
    {
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectives);
        _projects.Setup(p => p.GetActiveByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Proj("Website") });
    }

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        _members.Verify(m => m.ListActiveForEmployeeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmployeeWithNoWork_ReturnsOnlyTheCenterNode()
    {
        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var node = Assert.Single(result.Value!.Nodes);
        Assert.Equal($"employee:{_employeeId}", node.Id);
        Assert.Equal("employee", node.Kind);
        Assert.Equal("Saif Ahamed", node.Label);
        Assert.Equal("Watercraft Engineer", node.Sublabel);
        Assert.Empty(result.Value.Links);
        Assert.Equal(0, result.Value.HiddenTaskCount);
    }

    [Fact]
    public async Task Handle_BuildsProjectModuleAndTaskNodesWithRolesAndLinks()
    {
        var ownedModule = Guid.NewGuid();
        var memberModule = Guid.NewGuid();
        var viaTaskModule = Guid.NewGuid();
        var defaultObjective = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();

        _members.Setup(m => m.ListActiveForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = memberModule, EmployeeId = _employeeId, IsActive = true },
                new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = defaultObjective, EmployeeId = _employeeId, IsActive = true }
            });
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(ownedModule, "Owned module", _employeeId) });
        var taskInModule = Guid.NewGuid();
        var taskInDefault = Guid.NewGuid();
        _tasks.Setup(t => t.ListOpenAssignedToEmployeeAsync(_tenantId, _employeeId, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAssignedTasksPage(new[]
            {
                new OpenAssignedTaskRow(taskInModule, "WEB-1", "Fix cart", _projectId, viaTaskModule, "active"),
                new OpenAssignedTaskRow(taskInDefault, "WEB-2", "Write docs", _projectId, defaultObjective, "not_started")
            }, TotalCount: 65));
        ArrangeObjectivesAndProjects(
            Obj(ownedModule, "Owned module", _employeeId),
            Obj(memberModule, "Member module", otherOwner),
            Obj(viaTaskModule, "Via task module", otherOwner),
            Obj(defaultObjective, "Website (default)", otherOwner, isDefault: true));

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        var graph = result.Value!;
        var byId = graph.Nodes.ToDictionary(n => n.Id);

        Assert.Equal("project", byId[$"project:{_projectId}"].Kind);
        Assert.Equal("owner", byId[$"module:{ownedModule}"].Role);
        Assert.Equal("member", byId[$"module:{memberModule}"].Role);
        Assert.Equal("contributor", byId[$"module:{viaTaskModule}"].Role);
        Assert.DoesNotContain($"module:{defaultObjective}", byId.Keys);
        Assert.Equal("active", byId[$"task:{taskInModule}"].Status);
        Assert.Equal("WEB-1", byId[$"task:{taskInModule}"].Sublabel);
        Assert.Equal(65 - 2, graph.HiddenTaskCount);

        bool Has(string source, string target, string kind) =>
            graph.Links.Any(l => l.Source == source && l.Target == target && l.Kind == kind);

        var me = $"employee:{_employeeId}";
        var project = $"project:{_projectId}";
        Assert.True(Has(me, project, "works_on"));
        Assert.True(Has(project, $"module:{ownedModule}", "contains"));
        Assert.True(Has(me, $"module:{ownedModule}", "owns"));
        Assert.True(Has(me, $"module:{memberModule}", "member_of"));
        Assert.DoesNotContain(graph.Links, l => l.Source == me && l.Target == $"module:{viaTaskModule}");
        Assert.True(Has($"module:{viaTaskModule}", $"task:{taskInModule}", "has_task"));
        Assert.True(Has(project, $"task:{taskInDefault}", "has_task"));
    }

    [Fact]
    public async Task Handle_SkipsNodesWhoseProjectIsInactiveOrMissing()
    {
        var module = Guid.NewGuid();
        _objectives.Setup(o => o.ListActiveOwnedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(module, "Orphan module", _employeeId) });
        _objectives.Setup(o => o.GetByIdsForTenantAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Obj(module, "Orphan module", _employeeId) });
        // projects mock keeps its default: no active project returned.

        var result = await CreateHandler().Handle(new GetEmployeeWorkGraphQuery(_employeeId), CancellationToken.None);

        Assert.Single(result.Value!.Nodes); // only the employee node
        Assert.Empty(result.Value.Links);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkGraphQueryHandlerTests"`
Expected: build FAIL — handler/query/response types do not exist.

- [ ] **Step 3: Write the response DTOs**

`.../EmployeeWorkGraph/DTOs/Responses/EmployeeWorkGraphResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;

public static class WorkGraphNodeKinds
{
    public const string Employee = "employee";
    public const string Project = "project";
    public const string Module = "module";
    public const string Task = "task";
}

public static class WorkGraphModuleRoles
{
    public const string Owner = "owner";
    public const string Member = "member";
    public const string Contributor = "contributor";
}

public static class WorkGraphLinkKinds
{
    public const string WorksOn = "works_on";
    public const string Contains = "contains";
    public const string Owns = "owns";
    public const string MemberOf = "member_of";
    public const string HasTask = "has_task";
}

public sealed record WorkGraphNode(
    string Id,
    string Kind,
    string Label,
    string? Sublabel = null,
    string? Role = null,
    string? Status = null,
    Guid? ProjectId = null,
    Guid? ObjectiveId = null,
    Guid? TaskId = null);

public sealed record WorkGraphLink(string Source, string Target, string Kind);

public sealed record EmployeeWorkGraphResponse(
    IReadOnlyList<WorkGraphNode> Nodes,
    IReadOnlyList<WorkGraphLink> Links,
    int HiddenTaskCount);
```

- [ ] **Step 4: Write the query**

`.../Queries/GetEmployeeWorkGraph/GetEmployeeWorkGraphQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;

public sealed record GetEmployeeWorkGraphQuery(Guid EmployeeId) : IRequest<Result<EmployeeWorkGraphResponse>>;
```

- [ ] **Step 5: Write the handler**

`.../Queries/GetEmployeeWorkGraph/GetEmployeeWorkGraphQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;

/// <summary>
/// Builds the employee-centred work graph (spec 2026-09-30 §4/§5): the employee, the projects
/// he belongs to, the non-default objectives ("modules") he owns / is a member of / has open
/// work in, and his not_started/active tasks (capped). Access is the shared employee read-access
/// guard, so a coverage viewer sees his whole Work Management footprint (deliberate HR decision).
/// </summary>
public class GetEmployeeWorkGraphQueryHandler : IRequestHandler<GetEmployeeWorkGraphQuery, Result<EmployeeWorkGraphResponse>>
{
    public const int MaxTaskNodes = 60;

    private readonly IEmployeeReadAccessGuard _guard;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICurrentUser _currentUser;

    public GetEmployeeWorkGraphQueryHandler(
        IEmployeeReadAccessGuard guard,
        IProjectMemberRepository members,
        IObjectiveRepository objectives,
        IProjectRepository projects,
        IWorkTaskRepository tasks,
        ICurrentUser currentUser)
    {
        _guard = guard;
        _members = members;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _currentUser = currentUser;
    }

    public async Task<Result<EmployeeWorkGraphResponse>> Handle(GetEmployeeWorkGraphQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;
        var employeeId = request.EmployeeId;

        var access = await _guard.EnsureCanRead(tenantId, employeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkGraphResponse>.Failure(access.Error!, access.StatusCode ?? 400);
        var employee = access.Value!;

        var memberships = await _members.ListActiveForEmployeeAsync(tenantId, employeeId, ct);
        var owned = await _objectives.ListActiveOwnedByEmployeeAsync(tenantId, employeeId, ct);
        var taskPage = await _tasks.ListOpenAssignedToEmployeeAsync(tenantId, employeeId, MaxTaskNodes, ct);

        var objectiveIds = memberships.Select(m => m.ObjectiveId)
            .Concat(owned.Select(o => o.Id))
            .Concat(taskPage.Items.Select(t => t.ObjectiveId))
            .ToHashSet();
        var objectivesById = (await _objectives.GetByIdsForTenantAsync(tenantId, objectiveIds, ct))
            .Where(o => o.IsActive)
            .ToDictionary(o => o.Id);

        var projectIds = memberships.Select(m => m.ProjectId)
            .Concat(owned.Select(o => o.ProjectId))
            .Concat(taskPage.Items.Select(t => t.ProjectId))
            .Concat(objectivesById.Values.Select(o => o.ProjectId))
            .ToHashSet();
        var projectsById = (await _projects.GetActiveByIdsForTenantAsync(tenantId, projectIds, ct))
            .ToDictionary(p => p.Id);

        var memberObjectiveIds = memberships.Select(m => m.ObjectiveId).ToHashSet();
        var employeeNodeId = $"employee:{employeeId}";
        var nodes = new List<WorkGraphNode>
        {
            new(employeeNodeId, WorkGraphNodeKinds.Employee, employee.FullName, employee.PositionName)
        };
        var links = new List<WorkGraphLink>();

        foreach (var project in projectsById.Values.OrderBy(p => p.Name))
        {
            var projectNodeId = $"project:{project.Id}";
            nodes.Add(new WorkGraphNode(projectNodeId, WorkGraphNodeKinds.Project, project.Name, project.Identifier, ProjectId: project.Id));
            links.Add(new WorkGraphLink(employeeNodeId, projectNodeId, WorkGraphLinkKinds.WorksOn));
        }

        // A task's module node exists only for a non-default objective inside an active project;
        // default objectives are the project itself, so their tasks link to the project directly.
        var moduleObjectives = objectivesById.Values
            .Where(o => !o.IsDefault && projectsById.ContainsKey(o.ProjectId))
            .OrderBy(o => o.Title)
            .ToList();

        foreach (var objective in moduleObjectives)
        {
            var moduleNodeId = $"module:{objective.Id}";
            var isOwner = objective.OwnerId == employeeId;
            var isMember = memberObjectiveIds.Contains(objective.Id);
            var role = isOwner ? WorkGraphModuleRoles.Owner
                : isMember ? WorkGraphModuleRoles.Member
                : WorkGraphModuleRoles.Contributor;

            nodes.Add(new WorkGraphNode(moduleNodeId, WorkGraphNodeKinds.Module, objective.Title,
                Role: role, ProjectId: objective.ProjectId, ObjectiveId: objective.Id));
            links.Add(new WorkGraphLink($"project:{objective.ProjectId}", moduleNodeId, WorkGraphLinkKinds.Contains));
            if (isOwner)
                links.Add(new WorkGraphLink(employeeNodeId, moduleNodeId, WorkGraphLinkKinds.Owns));
            else if (isMember)
                links.Add(new WorkGraphLink(employeeNodeId, moduleNodeId, WorkGraphLinkKinds.MemberOf));
        }

        var moduleIds = moduleObjectives.Select(o => o.Id).ToHashSet();
        var renderedTasks = 0;
        foreach (var task in taskPage.Items)
        {
            if (!projectsById.ContainsKey(task.ProjectId))
                continue;

            var taskNodeId = $"task:{task.Id}";
            nodes.Add(new WorkGraphNode(taskNodeId, WorkGraphNodeKinds.Task, task.Title, task.ShortId,
                Status: task.Category, ProjectId: task.ProjectId, ObjectiveId: task.ObjectiveId, TaskId: task.Id));
            var parent = moduleIds.Contains(task.ObjectiveId) ? $"module:{task.ObjectiveId}" : $"project:{task.ProjectId}";
            links.Add(new WorkGraphLink(parent, taskNodeId, WorkGraphLinkKinds.HasTask));
            renderedTasks++;
        }

        var hidden = Math.Max(0, taskPage.TotalCount - renderedTasks);
        return Result<EmployeeWorkGraphResponse>.Success(new EmployeeWorkGraphResponse(nodes, links, hidden));
    }
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkGraphQueryHandlerTests"`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement/EmployeeWorkGraph tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkGraphQueryHandlerTests.cs
git commit -m "feat(work): add GetEmployeeWorkGraph query handler

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Controller endpoint

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (add `using` + action after `GetPositionHistory`, ~line 134)
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs`

**Interfaces:**
- Consumes: `GetEmployeeWorkGraphQuery(Guid EmployeeId)` (Task 3).
- Produces: `GET /api/v1/employees/{id:guid}/work-graph` → 200 `EmployeeWorkGraphResponse`, or ProblemDetails with the handler's status code (403/404). The frontend plan's `PeopleApiService.getWorkGraph` calls it.

- [ ] **Step 1: Write the failing architecture test**

Add inside `EmployeesControllerArchitectureTests` (above `FindRepositoryPath`), reusing the existing helper:

```csharp
    [Fact]
    public void EmployeesController_GetWorkGraph_RequiresEmployeesReadAndUsesTheWorkGraphRoute()
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var actionIndex = source.IndexOf("public async Task<IActionResult> GetWorkGraph(", StringComparison.Ordinal);
        Assert.True(actionIndex > 0, "Could not locate the GetWorkGraph action.");

        var preceding = source[..actionIndex];
        var routeIndex = preceding.LastIndexOf("[HttpGet(\"{id:guid}/work-graph\")]", StringComparison.Ordinal);
        var permissionIndex = preceding.LastIndexOf("[RequirePermission(\"employees:read\")]", StringComparison.Ordinal);
        Assert.True(routeIndex > 0, "GetWorkGraph is missing [HttpGet(\"{id:guid}/work-graph\")].");
        Assert.True(permissionIndex > routeIndex, "GetWorkGraph is missing [RequirePermission(\"employees:read\")] after its route attribute.");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~EmployeesController_GetWorkGraph"`
Expected: FAIL — "Could not locate the GetWorkGraph action."

- [ ] **Step 3: Add the action**

In `EmployeesController.cs` add to the usings (keep alphabetical near the other `WorkManagement`/`CoreHr` usings):

```csharp
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;
```

and after the `GetPositionHistory` action:

```csharp
    /// <summary>Work Network graph: the employee's projects, modules (objectives) and open tasks
    /// as nodes/links. Coverage-scoped like the detail read; see GetEmployeeWorkGraphQueryHandler.</summary>
    [HttpGet("{id:guid}/work-graph")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkGraph(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkGraphQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run architecture + unit suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green (unit suite was 3940+ passing at last record; no test may regress).

- [ ] **Step 5: Manual endpoint check (dev server)**

Start the API through the repo's usual launch config, sign in as a tenant user with `employees:read`, and `GET https://<tenant>.onexso.com:<port>/api/v1/employees/<employee-id>/work-graph`.
Expected: 200 with `nodes[0].kind == "employee"`; an id outside the caller's coverage → 403; an unknown GUID → 404. If a dev tenant employee has no Work Management data, the response is the single employee node — that is correct, not a failure.

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): expose GET /employees/{id}/work-graph

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review (against the spec)

- §3/§4.1-4.5 (module = objective, default objective skipped, flat modules, 60 cap, coverage access): Tasks 2-3 (+ Global Constraints).
- §4.6 (existing endpoints untouched): no task modifies them.
- §5 contract: response records in Task 3 match the JSON field-for-field (`role`, `status`, `hiddenTaskCount`; node ids `employee:/project:/module:/task:`; link kinds).
- Not in this plan by design: period plumbing, Overview endpoints, activity feed, Work Arrangement/Employment Record/History (frontend-only from existing endpoints).
- Type consistency: `OpenAssignedTasksPage`/`OpenAssignedTaskRow` defined in Task 2, used verbatim in Task 3; `IEmployeeReadAccessGuard.EnsureCanRead` defined in Task 1, used verbatim in Task 3; `GetEmployeeWorkGraphQuery(Guid EmployeeId)` defined in Task 3, used in Task 4.
- Verified by reading source: `EmployeeVisibilityScope(bool CanViewAllTenantEmployees, Guid? OwnEmployeeId, 3 × IReadOnlySet<Guid>)` + `Unrestricted()`; `ITenantContext` lives in `ONEVO.Application.Common.ServiceInterfaces`; `IEmployeeRepository.GetByIdAsync(tenantId, id, ct)` is used the same way by `GetEmployeeDetailQueryHandler`. Not compile-checked: the plan's code has not been built yet — the executor's first `dotnet test` is the first compile.
