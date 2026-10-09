# My Team — Part 2: Shared Pure Extractions (no behavior change)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract four pieces of existing logic into reusable, unit-tested units. Parts 3 and 4
reuse the **same** rules; none of them is copied. Every existing caller switches to the extracted
unit with no behavior change.

**Architecture:**
- `TaskProgressClassifier`, from `GetMyTaskProgressQueryHandler`.
- `ObjectiveTreeExpander`, from `EfProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync`.
- `ILeaveVisibilityScopeProvider`, from `GetLeaveCalendarQueryHandler.ResolveScopeAsync`.
- `EmployeeVisibilityScopeMatcher`: an in-memory mirror of the `EfLeaveCalendarRepository` SQL
  scope filter, kept honest by a parity test.

**Tech Stack:** .NET 10, xUnit, Moq, FluentAssertions, Testcontainers.

**Spec:** `docs/superpowers/specs/next/2026-09-29-my-team-dashboard-design.md` §8.3.2, §8.3.3, §9.2, §9.3.

## Global Constraints

- No behavior change for existing endpoints. Existing tests stay green; only the constructor call
  in the leave calendar test harness changes (Task 3).
- No new permission codes. No migrations.
- WorkManagement code never references CoreHr coverage services.

---

### Task 1: `TaskProgressClassifier`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskProgressClassifier.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskProgress/GetMyTaskProgressQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskProgressClassifierTests.cs`

**Interfaces:**
- Produces, in namespace `ONEVO.Application.Features.WorkManagement.Tasks.Services`:
  - `enum TaskProgressBucket { Completed, Overdue, InProgress, NotStarted }`
  - `static TaskProgressBucket TaskProgressClassifier.Classify(bool marksTaskComplete, int progressPercent, DateOnly? dueDate, DateOnly today)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskProgressClassifierTests.cs
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class TaskProgressClassifierTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [Theory]
    [InlineData(true, 0, null, TaskProgressBucket.Completed)]            // complete column wins
    [InlineData(false, 100, "2026-09-01", TaskProgressBucket.Completed)] // 100% is complete even past due
    [InlineData(false, 40, "2026-09-28", TaskProgressBucket.Overdue)]    // overdue beats in-progress
    [InlineData(false, 0, "2026-09-29", TaskProgressBucket.NotStarted)]  // due today is NOT overdue
    [InlineData(false, 10, "2026-10-05", TaskProgressBucket.InProgress)]
    [InlineData(false, 0, null, TaskProgressBucket.NotStarted)]
    public void Classifies_exactly_like_the_task_progress_widget(bool marksComplete, int progress, string? due, TaskProgressBucket expected)
    {
        var dueDate = due is null ? (DateOnly?)null : DateOnly.Parse(due);
        Assert.Equal(expected, TaskProgressClassifier.Classify(marksComplete, progress, dueDate, Today));
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskProgressClassifierTests"`
Expected: compile error, `TaskProgressClassifier` not found.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskProgressClassifier.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public enum TaskProgressBucket
{
    Completed,
    Overdue,
    InProgress,
    NotStarted,
}

/// <summary>The one Completed / Overdue / In Progress / Not Started rule for Work tasks, shared by
/// the personal Task Progress widget (GetMyTaskProgressQueryHandler) and My Team's Team Progress
/// (spec §8.3.3) so the two can never disagree.</summary>
public static class TaskProgressClassifier
{
    public static TaskProgressBucket Classify(bool marksTaskComplete, int progressPercent, DateOnly? dueDate, DateOnly today)
    {
        // A task can reach 100% via the clock-in Push flow without being dragged to a
        // MarksTaskComplete column - see GetMyActiveTasksAsync, which treats it as done too.
        if (marksTaskComplete || progressPercent >= 100)
            return TaskProgressBucket.Completed;
        if (dueDate is { } due && due < today)
            return TaskProgressBucket.Overdue;
        return progressPercent > 0 ? TaskProgressBucket.InProgress : TaskProgressBucket.NotStarted;
    }
}
```

- [ ] **Step 4: Switch the existing handler to it**

In `GetMyTaskProgressQueryHandler.cs`:
1. Add `using ONEVO.Application.Features.WorkManagement.Tasks.Services;`.
2. Replace the whole `foreach (var row in rows) { ... }` block, including its comment, with:

```csharp
        foreach (var row in rows)
        {
            switch (TaskProgressClassifier.Classify(row.MarksTaskComplete, row.ProgressPercent, row.DueDate, today))
            {
                case TaskProgressBucket.Completed: completed++; break;
                case TaskProgressBucket.Overdue: overdue++; break;
                case TaskProgressBucket.InProgress: inProgress++; break;
                default: notStarted++; break;
            }
        }
```

- [ ] **Step 5: Run both test classes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskProgressClassifierTests|FullyQualifiedName~GetMyTaskProgressQueryHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskProgressClassifierTests.cs
git commit -m "refactor(work): extract TaskProgressClassifier from the task-progress widget"
```

---

### Task 2: `ObjectiveTreeExpander`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ObjectiveTreeExpander.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs` (`GetActiveObjectiveIdsForEmployeeInProjectAsync`, L44-L80)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveTreeExpanderTests.cs`

**Interfaces:**
- Produces, in namespace `ONEVO.Application.Features.WorkManagement.Objectives.Services`:
  - `readonly record struct ObjectiveTreeNode(Guid Id, Guid? ParentObjectiveId)`
  - `static HashSet<Guid> ObjectiveTreeExpander.ExpandWithDescendants(IEnumerable<Guid> roots, IReadOnlyCollection<ObjectiveTreeNode> activeNodes)`
    returns the roots themselves plus every descendant reachable through `activeNodes`.
  - `static IReadOnlyDictionary<Guid, Guid?> ObjectiveTreeExpander.ParentMap(IReadOnlyCollection<ObjectiveTreeNode> nodes)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveTreeExpanderTests.cs
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public sealed class ObjectiveTreeExpanderTests
{
    [Fact]
    public void Expands_a_root_to_its_full_subtree_and_keeps_the_root()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid(), b = Guid.NewGuid(), other = Guid.NewGuid();
        var nodes = new[]
        {
            new ObjectiveTreeNode(root, null), new ObjectiveTreeNode(a, root), new ObjectiveTreeNode(a1, a),
            new ObjectiveTreeNode(b, root), new ObjectiveTreeNode(other, null),
        };

        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { a }, nodes);

        Assert.Equal(new[] { a, a1 }.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void A_root_missing_from_the_active_tree_is_returned_but_expands_to_nothing()
    {
        var inactiveMembershipObjective = Guid.NewGuid();
        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { inactiveMembershipObjective }, Array.Empty<ObjectiveTreeNode>());
        Assert.Equal(new[] { inactiveMembershipObjective }, result);
    }

    [Fact]
    public void Overlapping_roots_are_not_duplicated()
    {
        Guid root = Guid.NewGuid(), child = Guid.NewGuid();
        var nodes = new[] { new ObjectiveTreeNode(root, null), new ObjectiveTreeNode(child, root) };
        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { root, child }, nodes);
        Assert.Equal(2, result.Count);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ObjectiveTreeExpanderTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ObjectiveTreeExpander.cs
namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public readonly record struct ObjectiveTreeNode(Guid Id, Guid? ParentObjectiveId);

/// <summary>The one "a module grants its whole sub-module subtree" walk, shared by task visibility
/// (EfProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync) and My Team's Work I
/// Lead scope (spec §8.3.2). Pure: callers load the active objective tree once and pass it in.</summary>
public static class ObjectiveTreeExpander
{
    public static HashSet<Guid> ExpandWithDescendants(IEnumerable<Guid> roots, IReadOnlyCollection<ObjectiveTreeNode> activeNodes)
    {
        var result = new HashSet<Guid>(roots);
        if (result.Count == 0)
            return result;

        var childrenByParentId = activeNodes
            .Where(node => node.ParentObjectiveId is not null)
            .GroupBy(node => node.ParentObjectiveId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToList());

        var pending = new Queue<Guid>(result);
        while (pending.Count > 0)
        {
            if (!childrenByParentId.TryGetValue(pending.Dequeue(), out var childIds))
                continue;
            foreach (var childId in childIds)
            {
                if (result.Add(childId))
                    pending.Enqueue(childId);
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<Guid, Guid?> ParentMap(IReadOnlyCollection<ObjectiveTreeNode> nodes)
        => nodes.ToDictionary(node => node.Id, node => node.ParentObjectiveId);
}
```

- [ ] **Step 4: Switch `EfProjectMemberRepository` to it**

Add `using ONEVO.Application.Features.WorkManagement.Objectives.Services;` and replace the whole
`GetActiveObjectiveIdsForEmployeeInProjectAsync` method with:

```csharp
    public async Task<IReadOnlyList<Guid>> GetActiveObjectiveIdsForEmployeeInProjectAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        var membershipObjectiveIds = await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ProjectId == projectId && m.EmployeeId == employeeId && m.IsActive)
            .Select(m => m.ObjectiveId)
            .ToListAsync(ct);

        if (membershipObjectiveIds.Count == 0)
            return Array.Empty<Guid>();

        var objectiveTree = (await _db.Objectives.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsActive)
                .Select(o => new { o.Id, o.ParentObjectiveId })
                .ToListAsync(ct))
            .Select(o => new ObjectiveTreeNode(o.Id, o.ParentObjectiveId))
            .ToList();

        return ObjectiveTreeExpander.ExpandWithDescendants(membershipObjectiveIds, objectiveTree).ToList();
    }
```

- [ ] **Step 5: Build and run the WorkManagement unit tests**

Run: `dotnet build` then `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/Services/ObjectiveTreeExpander.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/ObjectiveTreeExpanderTests.cs
git commit -m "refactor(work): extract ObjectiveTreeExpander from project-member visibility"
```

---

### Task 3: `ILeaveVisibilityScopeProvider`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Services/ILeaveVisibilityScopeProvider.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Services/LeaveVisibilityScopeProvider.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Calendar/Queries/GetLeaveCalendarQuery.cs` (constructor; `ResolveScopeAsync` at L100-L126)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (next to the `ILeaveCalendarHolidayProvider` registration, around L258)
- Modify: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/GetLeaveCalendarQueryHandlerTests.cs` L317-L324 (harness constructor only)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveVisibilityScopeProviderTests.cs`

**Interfaces:**
- Consumes:
  - `ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository.GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken)`,
    which returns `ONEVO.Domain.Features.CoreHr.Entities.Employee?`. This is the interface the
    calendar handler already uses.
  - `IEmployeeVisibilityScopeResolver.ResolveAsync(Guid tenantId, Guid userId, CancellationToken)`,
    which returns `EmployeeVisibilityScope`.
- Produces, in namespace `ONEVO.Application.Features.Leave.Calendar.Services`:

```csharp
public enum LeaveVisibilityScopeFailure { NoEmployee, NoLeaveReadPermission }
public sealed record LeaveVisibilityScopeResolution(EmployeeVisibilityScope? Scope, LeaveVisibilityScopeFailure? Failure);
public interface ILeaveVisibilityScopeProvider
{
    Task<LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveVisibilityScopeProviderTests.cs
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using Xunit;
using DomainEmployee = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public sealed class LeaveVisibilityScopeProviderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (LeaveVisibilityScopeProvider Sut, Mock<IEmployeeVisibilityScopeResolver> Scopes, Mock<IEmployeeRepository> Employees)
        Build(params string[] permissions)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.TenantId).Returns(TenantId);
        user.SetupGet(x => x.UserId).Returns(UserId);
        user.Setup(x => x.HasPermission(It.IsAny<string>())).Returns<string>(p => permissions.Contains(p));
        var scopes = new Mock<IEmployeeVisibilityScopeResolver>();
        var employees = new Mock<IEmployeeRepository>();
        return (new LeaveVisibilityScopeProvider(user.Object, employees.Object, scopes.Object), scopes, employees);
    }

    [Theory]
    [InlineData("leave:read")]
    [InlineData("leave:manage")]
    public async Task Read_or_manage_is_unrestricted(string permission)
    {
        var (sut, _, _) = Build(permission);
        var result = await sut.ResolveForCurrentUserAsync();
        Assert.True(result.Scope!.CanViewAllTenantEmployees);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Read_team_uses_the_raw_coverage_scope_resolver()
    {
        var (sut, scopes, _) = Build("leave:read-team");
        var expected = new EmployeeVisibilityScope(false, Guid.NewGuid(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        scopes.Setup(x => x.ResolveAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.Same(expected, result.Scope);
    }

    [Fact]
    public async Task Read_own_is_self_only()
    {
        var (sut, _, employees) = Build("leave:read-own");
        var me = new DomainEmployee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId };
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(me);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.False(result.Scope!.CanViewAllTenantEmployees);
        Assert.Equal(me.Id, result.Scope.OwnEmployeeId);
    }

    [Fact]
    public async Task Read_own_without_employee_fails_with_NoEmployee()
    {
        var (sut, _, employees) = Build("leave:read-own");
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainEmployee?)null);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.Equal(LeaveVisibilityScopeFailure.NoEmployee, result.Failure);
    }

    [Fact]
    public async Task No_leave_permission_fails_with_NoLeaveReadPermission()
    {
        var (sut, _, _) = Build("attendance:read");
        var result = await sut.ResolveForCurrentUserAsync();
        Assert.Equal(LeaveVisibilityScopeFailure.NoLeaveReadPermission, result.Failure);
        Assert.Null(result.Scope);
    }
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~LeaveVisibilityScopeProviderTests"`
Expected: compile error.

- [ ] **Step 3: Implement the interface and the provider**

```csharp
// src/ONEVO.Application/Features/Leave/Calendar/Services/ILeaveVisibilityScopeProvider.cs
using ONEVO.Application.Features.CoreHr.Employee.Models;

namespace ONEVO.Application.Features.Leave.Calendar.Services;

public enum LeaveVisibilityScopeFailure
{
    NoEmployee,
    NoLeaveReadPermission,
}

public sealed record LeaveVisibilityScopeResolution(EmployeeVisibilityScope? Scope, LeaveVisibilityScopeFailure? Failure);

/// <summary>The Leave domain's own answer to "whose leave may the current user see":
/// leave:read / leave:manage = everyone, leave:read-team = raw management coverage,
/// leave:read-own = self. Shared by the leave calendar and My Team's Team Status leave masking
/// (spec §9.3).</summary>
public interface ILeaveVisibilityScopeProvider
{
    Task<LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/Leave/Calendar/Services/LeaveVisibilityScopeProvider.cs
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed class LeaveVisibilityScopeProvider(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IEmployeeVisibilityScopeResolver visibilityScopes) : ILeaveVisibilityScopeProvider
{
    public async Task<LeaveVisibilityScopeResolution> ResolveForCurrentUserAsync(CancellationToken ct = default)
    {
        if (currentUser.HasPermission("leave:manage") || currentUser.HasPermission("leave:read"))
            return new LeaveVisibilityScopeResolution(EmployeeVisibilityScope.Unrestricted(), null);

        if (currentUser.HasPermission("leave:read-team"))
        {
            return new LeaveVisibilityScopeResolution(
                await visibilityScopes.ResolveAsync(currentUser.TenantId, currentUser.UserId, ct), null);
        }

        if (currentUser.HasPermission("leave:read-own"))
        {
            var employee = await employees.GetByUserIdAsync(currentUser.TenantId, currentUser.UserId, ct);
            if (employee is null)
                return new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoEmployee);

            return new LeaveVisibilityScopeResolution(
                new EmployeeVisibilityScope(false, employee.Id, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()),
                null);
        }

        return new LeaveVisibilityScopeResolution(null, LeaveVisibilityScopeFailure.NoLeaveReadPermission);
    }
}
```

- [ ] **Step 4: Switch the calendar handler to it**

In `GetLeaveCalendarQuery.cs`:
1. Delete the `_employees` and `_visibilityScopes` fields and constructor parameters.
2. Add the constructor parameter `ILeaveVisibilityScopeProvider leaveScopes` in their place,
   stored as `private readonly ILeaveVisibilityScopeProvider _leaveScopes;`.
3. Add `using ONEVO.Application.Features.Leave.Calendar.Services;`.
4. Replace `ResolveScopeAsync` with:

```csharp
    private async Task<Result<EmployeeVisibilityScope>> ResolveScopeAsync(CancellationToken ct)
    {
        var resolution = await _leaveScopes.ResolveForCurrentUserAsync(ct);
        return resolution.Failure switch
        {
            LeaveVisibilityScopeFailure.NoEmployee =>
                Result<EmployeeVisibilityScope>.NotFound(LeaveCalendarMessages.NoEmployee),
            LeaveVisibilityScopeFailure.NoLeaveReadPermission =>
                Result<EmployeeVisibilityScope>.Forbidden(LeaveCalendarMessages.LeaveScopeRequired),
            _ => Result<EmployeeVisibilityScope>.Success(resolution.Scope!),
        };
    }
```

Remove `using` lines that are now unused, then run `dotnet build` and fix only what it reports.

- [ ] **Step 5: Register it**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, directly after the
`ILeaveCalendarHolidayProvider` registration:

```csharp
        services.AddScoped<ONEVO.Application.Features.Leave.Calendar.Services.ILeaveVisibilityScopeProvider,
            ONEVO.Application.Features.Leave.Calendar.Services.LeaveVisibilityScopeProvider>();
```

- [ ] **Step 6: Update the existing calendar test harness**

In `GetLeaveCalendarQueryHandlerTests.cs` L317-L324, build the provider from the **same** mocks,
so every existing assertion on `employees` and `visibilityScopes` still holds:

```csharp
            var handler = new GetLeaveCalendarQueryHandler(
                currentUser.Object,
                new LeaveVisibilityScopeProvider(currentUser.Object, employees.Object, visibilityScopes.Object),
                repository.Object,
                holidays.Object,
                new LeaveCalendarRequestProjector(),
                Options.Create(options ?? new LeaveCalendarOptions()));
```

Add `using ONEVO.Application.Features.Leave.Calendar.Services;`.

- [ ] **Step 7: Run the leave calendar tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Leave.Calendar"`
Expected: PASS. Every pre-existing calendar test is unchanged apart from the harness line.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Calendar src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Leave/Calendar
git commit -m "refactor(leave): extract ILeaveVisibilityScopeProvider from the leave calendar"
```

---

### Task 4: `EmployeeVisibilityScopeMatcher` plus parity with the SQL filter

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Models/EmployeeVisibilityScopeMatcher.cs`
- Modify: `tests/ONEVO.Tests.Integration/MyTeam/MyTeamDb.cs` (add leave helpers)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeVisibilityScopeMatcherTests.cs`
- Test: `tests/ONEVO.Tests.Integration/MyTeam/EmployeeVisibilityScopeMatcherParityTests.cs`

**Interfaces:**
- Consumes: `MyTeamDb` (Part 1 Task 1):
  - `static Task<MyTeamDb> CreateAsync()`, `Guid TenantId`, `Guid LegalEntityId`
  - `ApplicationDbContext NewContext(CountingDbCommandInterceptor? counter = null)`
  - `Employee AddEmployee(db, Guid? departmentId = null, Guid? legalEntityId = null, string? lastName = null)`
  - `Guid AddDepartment(db, Guid? parentId = null, bool active = true, Guid? legalEntityId = null)`
  - `Guid AddPositionHeldBy(db, Guid employeeId)`
- Produces:
  - `static bool EmployeeVisibilityScopeMatcher.Includes(EmployeeVisibilityScope scope, Guid employeeId, Guid? primaryPositionId, Guid? departmentId, Guid? legalEntityId)`,
    in namespace `ONEVO.Application.Features.CoreHr.Employee.Models`.
  - `Guid MyTeamDb.AddLeaveType(ApplicationDbContext db)`
  - `LeaveRequest MyTeamDb.AddApprovedLeave(ApplicationDbContext db, Guid employeeId, Guid leaveTypeId, DateOnly from, DateOnly to)`

- [ ] **Step 1: Write the failing unit test**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeVisibilityScopeMatcherTests.cs
using ONEVO.Application.Features.CoreHr.Employee.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeVisibilityScopeMatcherTests
{
    private static readonly Guid Emp = Guid.NewGuid(), Pos = Guid.NewGuid(), Dept = Guid.NewGuid(), Le = Guid.NewGuid();

    private static EmployeeVisibilityScope Scope(Guid? own = null, Guid? pos = null, Guid? dept = null, Guid? le = null) => new(
        false, own,
        pos is null ? new HashSet<Guid>() : new HashSet<Guid> { pos.Value },
        dept is null ? new HashSet<Guid>() : new HashSet<Guid> { dept.Value },
        le is null ? new HashSet<Guid>() : new HashSet<Guid> { le.Value });

    [Fact] public void Unrestricted_includes_everyone() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(EmployeeVisibilityScope.Unrestricted(), Emp, null, null, null));
    [Fact] public void Own_employee_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(own: Emp), Emp, null, null, null));
    [Fact] public void Covered_primary_position_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(pos: Pos), Emp, Pos, null, null));
    [Fact] public void Covered_department_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(dept: Dept), Emp, null, Dept, null));
    [Fact] public void Company_wide_legal_entity_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(le: Le), Emp, null, null, Le));
    [Fact] public void Sub_department_is_NOT_included_raw_coverage_only() => Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(dept: Dept), Emp, null, Guid.NewGuid(), null));
    [Fact] public void Nothing_matching_is_excluded() => Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(), Emp, Pos, Dept, Le));
}
```

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeVisibilityScopeMatcherTests"`
Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/CoreHr/Employee/Models/EmployeeVisibilityScopeMatcher.cs
namespace ONEVO.Application.Features.CoreHr.Employee.Models;

/// <summary>In-memory mirror of the EmployeeVisibilityScope SQL filter in
/// EfLeaveCalendarRepository.ListMonthRequestsAsync: own OR covered primary position OR covered
/// department (exact, no descendants) OR company-wide legal entity. Used where the subjects are
/// already loaded, e.g. My Team leave masking (spec §9.3). EmployeeVisibilityScopeMatcherParityTests
/// keeps it identical to the SQL.</summary>
public static class EmployeeVisibilityScopeMatcher
{
    public static bool Includes(
        EmployeeVisibilityScope scope, Guid employeeId, Guid? primaryPositionId, Guid? departmentId, Guid? legalEntityId)
    {
        if (scope.CanViewAllTenantEmployees)
            return true;
        if (scope.OwnEmployeeId is Guid own && own == employeeId)
            return true;
        if (primaryPositionId is Guid position && scope.CoveredPositionIds.Contains(position))
            return true;
        if (departmentId is Guid department && scope.CoveredDepartmentIds.Contains(department))
            return true;
        return legalEntityId is Guid le && scope.CompanyWideLegalEntityIds.Contains(le);
    }
}
```

- [ ] **Step 4: Run the unit test**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeVisibilityScopeMatcherTests"`
Expected: PASS.

- [ ] **Step 5: Add leave seeding helpers to `MyTeamDb`**

Add these `using` lines:
- `using ONEVO.Domain.Features.Leave.Common;`
- `using ONEVO.Domain.Features.Leave.Request.Entities;`
- `using ONEVO.Domain.Features.Leave.Type.Entities;`

Then add these members. The field shapes are copied from
`tests/ONEVO.Tests.Integration/Features/Leave/LeaveCalendarIntegrationTests.cs` L133-L162.

```csharp
    public Guid AddLeaveType(ApplicationDbContext db)
    {
        var id = Guid.NewGuid();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = TenantId, Name = "Annual", Code = $"AN{Guid.NewGuid():N}"[..8],
            Category = LeaveTypeCategories.Annual, IsPaid = true, RequiresApproval = true,
            DefaultDaysPerYear = 10m, ApplicableGender = LeaveGenderRestrictions.All,
        });
        return id;
    }

    public LeaveRequest AddApprovedLeave(ApplicationDbContext db, Guid employeeId, Guid leaveTypeId, DateOnly from, DateOnly to)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            StartAt = new DateTimeOffset(from.ToDateTime(new TimeOnly(0, 0)), TimeSpan.Zero),
            EndAt = new DateTimeOffset(to.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero),
            TotalHours = 8m, PaidHours = 8m, Status = LeaveRequestStatuses.Approved,
            ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.LeaveRequests.Add(request);
        return request;
    }
```

- [ ] **Step 6: Write the parity integration test**

For every scope shape, the matcher must select the same employees as `EfLeaveCalendarRepository`.

```csharp
// tests/ONEVO.Tests.Integration/MyTeam/EmployeeVisibilityScopeMatcherParityTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class EmployeeVisibilityScopeMatcherParityTests
{
    [Fact]
    public async Task Matcher_selects_exactly_the_employees_the_leave_calendar_SQL_selects()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid parentDept, coveredPosition, strangerId;
        var all = new List<Guid>();
        await using (var db = helper.NewContext())
        {
            var leaveTypeId = helper.AddLeaveType(db);
            parentDept = helper.AddDepartment(db);
            var childDept = helper.AddDepartment(db, parentDept);
            var inParent = helper.AddEmployee(db, departmentId: parentDept);
            var inChild = helper.AddEmployee(db, departmentId: childDept);
            var positionHolder = helper.AddEmployee(db);
            coveredPosition = helper.AddPositionHeldBy(db, positionHolder.Id);
            var stranger = helper.AddEmployee(db);
            strangerId = stranger.Id;
            foreach (var e in new[] { inParent, inChild, positionHolder, stranger })
            {
                helper.AddApprovedLeave(db, e.Id, leaveTypeId, today, today);
                all.Add(e.Id);
            }
            await db.SaveChangesAsync();
        }

        var scopes = new[]
        {
            new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid> { parentDept }, new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, null, new HashSet<Guid> { coveredPosition }, new HashSet<Guid>(), new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, strangerId, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { helper.LegalEntityId }),
        };

        await using var read = helper.NewContext();
        var repository = new EfLeaveCalendarRepository(read);
        var employees = await read.Employees.AsNoTracking().Where(e => all.Contains(e.Id)).ToListAsync();
        var primaryPositionByEmployee = await read.PositionAssignments.AsNoTracking()
            .Where(pa => all.Contains(pa.EmployeeId))
            .ToDictionaryAsync(pa => pa.EmployeeId, pa => pa.PositionId);

        foreach (var scope in scopes)
        {
            var sqlRows = await repository.ListMonthRequestsAsync(helper.TenantId, scope,
                new LeaveCalendarRequestFilter(today.AddDays(-1), today.AddDays(1), null, false));
            var sqlIds = sqlRows.Select(r => r.Request.EmployeeId).Distinct().OrderBy(x => x).ToList();

            var matcherIds = employees
                .Where(e => EmployeeVisibilityScopeMatcher.Includes(scope, e.Id,
                    primaryPositionByEmployee.TryGetValue(e.Id, out var p) ? p : null, e.DepartmentId, e.LegalEntityId))
                .Select(e => e.Id).OrderBy(x => x).ToList();

            matcherIds.Should().Equal(sqlIds);
        }
    }
}
```

- [ ] **Step 7: Run it**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~EmployeeVisibilityScopeMatcherParityTests"`
Expected: PASS.

If `SaveChangesAsync` fails because a `LeaveRequest` or `LeaveType` column has no default, read the
Postgres error, add that field to the helper, and re-run. Never weaken the assertion.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Models/EmployeeVisibilityScopeMatcher.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeVisibilityScopeMatcherTests.cs tests/ONEVO.Tests.Integration/MyTeam
git commit -m "feat(corehr): EmployeeVisibilityScopeMatcher with SQL parity test"
```

## Part 2 done when

- All four extractions are committed, and every pre-existing test is green.
- `dotnet build` shows no new warnings in the touched files.
