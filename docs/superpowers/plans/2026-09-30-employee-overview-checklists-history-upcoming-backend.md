# Employee Overview — Checklists, Employment History & Upcoming Items (Plan 4A, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add three per-widget endpoints for the employee Overview: `overview/checklists`, `overview/history` and `overview/upcoming`.

**Architecture:** Each is a thin handler: `IEmployeeReadAccessGuard` (404/403) → read existing repositories → shape a response. Two small repository additions are needed (release reminders for a user; nothing else new — checklist, position-assignment, leave and calendar reads already exist).

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md`. Prerequisites: Plan 1 (`IEmployeeReadAccessGuard`) and Plan 2 (`EmployeePeriod`, the controller theory test) committed. Recent Activity is Plan 4B (`...-recent-activity-backend.md`).

## Decisions I have NOT made for you (read before executing)

The user has said that permission and visibility choices are theirs. This plan therefore applies **no module permission** to any of these three endpoints — only the route's `[RequirePermission("employees:read")]` plus the coverage guard, which is the access rule already approved in the spec (§4.5). It does **not** use `attendance:read`, `leave:read`, `tasks:read`, `monitoring:read`, or any other module permission. If you want a module gate, name the permission and I will add it.

Defaults I had to pick so the code has a defined behaviour — each is one line to change:
1. **Private calendar events are excluded** from Upcoming (`CalendarEvent.IsPrivate == true`), even for a viewer with coverage. (Most conservative; the alternative is to show them as "Private event".)
2. **"Releases" means release reminders addressed to the employee** (`ReleaseCalendarEntry.RecipientUserId == employee.UserId`, active, scheduled in the window), not every release of every project he belongs to.
3. **Checklist groups are keyed by task `Category`**, falling back to the lifecycle name ("Onboarding" / "Offboarding") when a task has no category.
4. **Bypassed checklist tasks are not counted as completed**; they are reported separately.
5. **Employment history only contains events that leave a record.** There are no history rows for employment *status*, employment *type* or *work mode* changes, so those are not shown.

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Before every commit: `git branch --show-current` and `git status --short` (shared tree); stage only the task's files. **No git worktrees.**
- Build/test per project: `dotnet test tests/ONEVO.Tests.Unit`, `dotnet test tests/ONEVO.Tests.Architecture`. Before **each** run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell; the dev server respawns).
- Namespace traps (verified): `IPositionRepository` is in `ONEVO.Application.Features.OrgStructure.RepositoryInterfaces`; the `PositionAssignment` **entity** is `ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment` and collides with the Application namespace `...CoreHr.PositionAssignment`, so always alias it (`using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;`); the `Employee` entity is aliased `EmployeeEntity` in files under the `...CoreHr.Employee` namespace.
- Dates sent to the client as `DateOnly` are the **UTC date** of a timestamp.
- Existing endpoints untouched. Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: `overview/checklists`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeChecklistOverviewResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeChecklistOverview/GetEmployeeChecklistOverviewQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeChecklistOverview/GetEmployeeChecklistOverviewQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeChecklistOverviewQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard.EnsureCanRead`; `IEmployeeChecklistTaskRepository.ListByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)` (namespace `ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces`, returns `IReadOnlyList<EmployeeChecklistTask>`; entity in `ONEVO.Domain.Features.CoreHr.Entities` with `LifecycleType`, `Category`, `Status`, `IsRequired`, `EmployeeChecklistTaskStatuses.Completed/Bypassed`).
- Produces: `EmployeeChecklistGroup(string Name, string LifecycleType, int Completed, int Bypassed, int Total)`; `EmployeeChecklistOverviewResponse(IReadOnlyList<EmployeeChecklistGroup> Groups)`; `GetEmployeeChecklistOverviewQuery(Guid EmployeeId)`; `GET /api/v1/employees/{id}/overview/checklists`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeChecklistOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeChecklistOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeChecklistOverviewQueryHandler CreateHandler() => new(_guard.Object, _tasks.Object, _user.Object);

    private EmployeeChecklistTask Task(string lifecycle, string? category, string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
        LifecycleType = lifecycle, Category = category, Status = status, TaskTitle = "t"
    };

    private void Arrange(params EmployeeChecklistTask[] tasks) =>
        _tasks.Setup(t => t.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(tasks);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ReturnsNoGroups_WhenTheEmployeeHasNoChecklistTasks()
    {
        Arrange();

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.Value!.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_GroupsByCategory_AndCountsCompletedBypassedAndTotal()
    {
        Arrange(
            Task("onboarding", "Security training", "completed"),
            Task("onboarding", "Security training", "completed"),
            Task("onboarding", "Equipment setup", "completed"),
            Task("onboarding", "Equipment setup", "pending"),
            Task("onboarding", "Equipment setup", "bypassed"),
            Task("onboarding", "Equipment setup", "in_progress"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        var groups = result.Value!.Groups.ToDictionary(g => g.Name);
        groups["Security training"].Should().Be(new EmployeeChecklistGroup("Security training", "onboarding", 2, 0, 2));
        groups["Equipment setup"].Should().Be(new EmployeeChecklistGroup("Equipment setup", "onboarding", 1, 1, 4));
    }

    [Fact]
    public async Task Handle_FallsBackToTheLifecycleNameWhenTasksHaveNoCategory_AndKeepsLifecyclesApart()
    {
        Arrange(
            Task("onboarding", null, "completed"),
            Task("onboarding", "  ", "pending"),
            Task("offboarding", null, "completed"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        var groups = result.Value!.Groups;
        groups.Should().HaveCount(2);
        groups.Should().ContainSingle(g => g.Name == "Onboarding" && g.LifecycleType == "onboarding" && g.Completed == 1 && g.Total == 2);
        groups.Should().ContainSingle(g => g.Name == "Offboarding" && g.LifecycleType == "offboarding" && g.Completed == 1 && g.Total == 1);
    }

    [Fact]
    public async Task Handle_ListsOnboardingGroupsBeforeOffboardingGroups_ThenByName()
    {
        Arrange(
            Task("offboarding", "Exit interview", "pending"),
            Task("onboarding", "Zebra", "pending"),
            Task("onboarding", "Alpha", "pending"));

        var result = await CreateHandler().Handle(new GetEmployeeChecklistOverviewQuery(_employeeId), CancellationToken.None);

        result.Value!.Groups.Select(g => g.Name).Should().Equal("Alpha", "Zebra", "Exit interview");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeChecklistOverviewQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 3: Write DTOs, query and handler**

`EmployeeChecklistOverviewResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Bypassed tasks are counted in Total but not in Completed.</summary>
public sealed record EmployeeChecklistGroup(string Name, string LifecycleType, int Completed, int Bypassed, int Total);

public sealed record EmployeeChecklistOverviewResponse(IReadOnlyList<EmployeeChecklistGroup> Groups);
```

`GetEmployeeChecklistOverviewQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;

public sealed record GetEmployeeChecklistOverviewQuery(Guid EmployeeId)
    : IRequest<Result<EmployeeChecklistOverviewResponse>>;
```

`GetEmployeeChecklistOverviewQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;

/// <summary>
/// Progress of an employee's onboarding/offboarding checklist tasks, grouped by task category
/// (falling back to the lifecycle name). Lifetime view: it does not follow the month period.
/// </summary>
public sealed class GetEmployeeChecklistOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeChecklistTaskRepository tasks,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeChecklistOverviewQuery, Result<EmployeeChecklistOverviewResponse>>
{
    public async Task<Result<EmployeeChecklistOverviewResponse>> Handle(
        GetEmployeeChecklistOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeChecklistOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var rows = await tasks.ListByEmployeeAsync(tenantId, request.EmployeeId, ct);

        var groups = rows
            .GroupBy(t => (Lifecycle: t.LifecycleType, Name: GroupName(t)))
            .Select(g => new EmployeeChecklistGroup(
                g.Key.Name,
                g.Key.Lifecycle,
                g.Count(t => t.Status == EmployeeChecklistTaskStatuses.Completed),
                g.Count(t => t.Status == EmployeeChecklistTaskStatuses.Bypassed),
                g.Count()))
            .OrderBy(g => g.LifecycleType == "onboarding" ? 0 : 1)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<EmployeeChecklistOverviewResponse>.Success(new EmployeeChecklistOverviewResponse(groups));
    }

    private static string GroupName(EmployeeChecklistTask task) =>
        !string.IsNullOrWhiteSpace(task.Category)
            ? task.Category.Trim()
            : task.LifecycleType.Length == 0
                ? "Checklist"
                : char.ToUpperInvariant(task.LifecycleType[0]) + task.LifecycleType[1..];
}
```

- [ ] **Step 4: Run to verify the handler tests pass**

Run: same command as Step 2. Expected: 5 passed. (If `EmployeeChecklistTaskStatuses` is not in `ONEVO.Domain.Features.CoreHr.Entities`, open `src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs` and add the `using` for the namespace that file declares.)

- [ ] **Step 5: Extend the architecture theory, watch it fail, add the action**

Add to the `[Theory]` in `EmployeesControllerArchitectureTests.cs`:

```csharp
    [InlineData("overview/checklists", "GetOverviewChecklists")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add the using and, after the existing overview actions, the action:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;
```

```csharp
    /// <summary>Overview checklists card: onboarding/offboarding task progress grouped by category.
    /// Lifetime view (not period-aware).</summary>
    [HttpGet("{id:guid}/overview/checklists")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewChecklists(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeChecklistOverviewQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run both suites, then commit**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeChecklistOverviewResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeChecklistOverview src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeChecklistOverviewQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview checklist progress endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `overview/history` (employment history events)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeHistoryResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeHistory/GetEmployeeHistoryQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeHistory/GetEmployeeHistoryQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeHistoryQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Consumes: `IEmployeeRepository.GetByIdAsync(tenantId, id, ct)` (entity: `HireDate`, `ProbationEndDate`, `TerminationDate`); `IPositionAssignmentRepository.ListHistoryForEmployeeAsync(tenantId, employeeId, ct)` (primary-employment assignments, oldest first; entity has `PositionId`, `EffectiveFrom`, `ChangeReason`, `ReportsToEmployeeId`); `IPositionRepository.GetByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken)`; `ILeaveRequestRepository.ListOwnAsync(tenantId, employeeId, LeaveRequestListFilter, ct)`; `IEmployeeChecklistTaskRepository.ListByEmployeeAsync`; `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken)`; `IDateTimeProvider.Today`.
- Produces: `EmployeeHistoryEvent(string Kind, string Title, string? Detail, DateOnly Date)` with kinds `joined | probation_completed | position_changed | manager_changed | leave_approved | checklist_completed | terminated`; `EmployeeHistoryResponse(IReadOnlyList<EmployeeHistoryEvent> Events)`; `GetEmployeeHistoryQuery(Guid EmployeeId, int Limit = 20)` (1..50 else 400); `GET /api/v1/employees/{id}/overview/history?limit=`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeHistoryQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _checklist = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _juniorId = Guid.NewGuid();
    private readonly Guid _seniorId = Guid.NewGuid();
    private readonly Guid _managerA = Guid.NewGuid();
    private readonly Guid _managerB = Guid.NewGuid();

    public GetEmployeeHistoryQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 30));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        ArrangeEmployee(new DateOnly(2024, 2, 1), null, null);
        ArrangeAssignments();
        _positions.Setup(p => p.GetByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Position { Id = _juniorId, TenantId = _tenantId, Name = "Junior Engineer" },
                new Position { Id = _seniorId, TenantId = _tenantId, Name = "Senior Engineer" }
            });
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        _checklist.Setup(c => c.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeChecklistTask>());
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [_managerA] = "Grace Hopper", [_managerB] = "Abitha Devendran" });
    }

    private GetEmployeeHistoryQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _assignments.Object, _positions.Object, _leave.Object,
            _checklist.Object, _identity.Object, _user.Object, _clock.Object);

    private void ArrangeEmployee(DateOnly hire, DateOnly? probationEnd, DateOnly? termination) =>
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity
            {
                Id = _employeeId, TenantId = _tenantId, HireDate = hire, ProbationEndDate = probationEnd, TerminationDate = termination
            });

    private PositionAssignmentEntity Assignment(Guid positionId, string from, Guid? reportsTo, string? reason = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, PositionId = positionId,
        EffectiveFrom = DateOnly.Parse(from), ReportsToEmployeeId = reportsTo, ChangeReason = reason
    };

    private void ArrangeAssignments(params PositionAssignmentEntity[] items) =>
        _assignments.Setup(a => a.ListHistoryForEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Handle_Returns400_ForAnOutOfRangeLimit(int limit)
    {
        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, limit), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ContainsTheJoinEvent_WithTheFirstPositionWhenKnown()
    {
        ArrangeAssignments(Assignment(_juniorId, "2024-02-01", _managerA));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var joined = result.Value!.Events.Single(e => e.Kind == "joined");
        joined.Title.Should().Be("Joined company");
        joined.Detail.Should().Be("Joined as Junior Engineer");
        joined.Date.Should().Be(new DateOnly(2024, 2, 1));
    }

    [Fact]
    public async Task Handle_EmitsPositionAndManagerChangesFromConsecutiveAssignments()
    {
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerB, "Promotion"));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var position = result.Value!.Events.Single(e => e.Kind == "position_changed");
        position.Title.Should().Be("Position changed");
        position.Detail.Should().Be("Junior Engineer → Senior Engineer (Promotion)");
        position.Date.Should().Be(new DateOnly(2025, 8, 15));

        var manager = result.Value.Events.Single(e => e.Kind == "manager_changed");
        manager.Title.Should().Be("Reporting manager changed");
        manager.Detail.Should().Be("Reporting manager changed to Abitha Devendran");
        manager.Date.Should().Be(new DateOnly(2025, 8, 15));
    }

    [Fact]
    public async Task Handle_EmitsNoManagerChange_WhenTheManagerStaysTheSameOrIsUnknown()
    {
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerA),
            Assignment(_seniorId, "2026-01-01", null));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.Value!.Events.Should().NotContain(e => e.Kind == "manager_changed");
        result.Value.Events.Count(e => e.Kind == "position_changed").Should().Be(2);
    }

    [Fact]
    public async Task Handle_EmitsProbationCompletedOnlyOnceTheDateHasPassed()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), new DateOnly(2024, 5, 1), null);
        (await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None))
            .Value!.Events.Should().ContainSingle(e => e.Kind == "probation_completed" && e.Date == new DateOnly(2024, 5, 1));

        ArrangeEmployee(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), null);
        (await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None))
            .Value!.Events.Should().NotContain(e => e.Kind == "probation_completed");
    }

    [Fact]
    public async Task Handle_EmitsTheEndOfEmploymentWhenTerminated()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), null, new DateOnly(2026, 9, 1));

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        result.Value!.Events.Should().ContainSingle(e => e.Kind == "terminated" && e.Title == "Employment ended" && e.Date == new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Handle_EmitsApprovedLeaveAndCompletedChecklistTasks_AndIgnoresTheRest()
    {
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "approved", TotalHours = 24, ApprovedAt = DateTimeOffset.Parse("2026-08-28T10:00:00+00:00") }, "Annual leave", "AL"),
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "pending", TotalHours = 8 }, "Sick leave", "SL"),
                new LeaveRequestListRow(new LeaveRequest { Id = Guid.NewGuid(), Status = "approved", TotalHours = 8, ApprovedAt = null }, "Sick leave", "SL")
            });
        _checklist.Setup(c => c.ListByEmployeeAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmployeeChecklistTask { Id = Guid.NewGuid(), LifecycleType = "onboarding", Category = "Security training", TaskTitle = "Complete course", Status = "completed", CompletedAt = DateTimeOffset.Parse("2026-08-15T09:00:00+00:00") },
                new EmployeeChecklistTask { Id = Guid.NewGuid(), LifecycleType = "onboarding", Category = "Security training", TaskTitle = "Pending one", Status = "pending" }
            });

        var result = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId), CancellationToken.None);

        var leave = result.Value!.Events.Single(e => e.Kind == "leave_approved");
        leave.Title.Should().Be("Leave approved");
        leave.Detail.Should().Be("Annual leave (24 h)");
        leave.Date.Should().Be(new DateOnly(2026, 8, 28));
        var checklist = result.Value.Events.Single(e => e.Kind == "checklist_completed");
        checklist.Title.Should().Be("Checklist task completed");
        checklist.Detail.Should().Be("Security training: Complete course");
        checklist.Date.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task Handle_ReturnsNewestFirst_AndHonoursTheLimit()
    {
        ArrangeEmployee(new DateOnly(2024, 2, 1), new DateOnly(2024, 5, 1), new DateOnly(2026, 9, 1));
        ArrangeAssignments(
            Assignment(_juniorId, "2024-02-01", _managerA),
            Assignment(_seniorId, "2025-08-15", _managerB));

        var all = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, 50), CancellationToken.None);
        all.Value!.Events.Select(e => e.Date).Should().BeInDescendingOrder();
        all.Value.Events.First().Kind.Should().Be("terminated");

        var limited = await CreateHandler().Handle(new GetEmployeeHistoryQuery(_employeeId, 2), CancellationToken.None);
        limited.Value!.Events.Should().HaveCount(2);
        limited.Value.Events.Select(e => e.Date).Should().Equal(all.Value.Events.Take(2).Select(e => e.Date));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeHistoryQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 3: Write DTOs and query**

`EmployeeHistoryResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Kind: joined | probation_completed | position_changed | manager_changed | leave_approved |
/// checklist_completed | terminated. Date is the UTC date of the event.</summary>
public sealed record EmployeeHistoryEvent(string Kind, string Title, string? Detail, DateOnly Date);

public sealed record EmployeeHistoryResponse(IReadOnlyList<EmployeeHistoryEvent> Events);
```

`GetEmployeeHistoryQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;

public sealed record GetEmployeeHistoryQuery(Guid EmployeeId, int Limit = 20)
    : IRequest<Result<EmployeeHistoryResponse>>;
```

- [ ] **Step 4: Write the handler**

`GetEmployeeHistoryQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;

/// <summary>
/// A lifetime timeline built only from facts that leave a record: hire date, probation end,
/// primary-employment position assignments (position and reporting-manager changes), approved
/// leave, completed checklist tasks and termination. Employment status/type/work-mode changes are
/// not recorded anywhere, so they cannot appear.
/// </summary>
public sealed class GetEmployeeHistoryQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IPositionAssignmentRepository assignments,
    IPositionRepository positions,
    ILeaveRequestRepository leave,
    IEmployeeChecklistTaskRepository checklist,
    ICallerIdentityResolver identity,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeHistoryQuery, Result<EmployeeHistoryResponse>>
{
    public const int MaxLimit = 50;

    public async Task<Result<EmployeeHistoryResponse>> Handle(GetEmployeeHistoryQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeHistoryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Limit < 1 || request.Limit > MaxLimit)
            return Result<EmployeeHistoryResponse>.Failure($"limit must be between 1 and {MaxLimit}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeHistoryResponse>.NotFound("The employee or selected organization record could not be found.");

        var today = clock.Today;
        var history = await assignments.ListHistoryForEmployeeAsync(tenantId, request.EmployeeId, ct);
        var positionNames = (await positions.GetByIdsAsync(tenantId, history.Select(h => h.PositionId).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id, p => p.Name);
        var managerIds = history.Where(h => h.ReportsToEmployeeId is not null).Select(h => h.ReportsToEmployeeId!.Value).Distinct().ToList();
        var managerNames = managerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, managerIds, ct)).ToDictionary(p => p.Key, p => p.Value);

        string PositionName(Guid id) => positionNames.GetValueOrDefault(id) ?? "Unknown position";

        var events = new List<EmployeeHistoryEvent>();

        events.Add(new EmployeeHistoryEvent(
            "joined", "Joined company",
            history.Count > 0 ? $"Joined as {PositionName(history[0].PositionId)}" : null,
            employee.HireDate));

        if (employee.ProbationEndDate is { } probationEnd && probationEnd <= today)
            events.Add(new EmployeeHistoryEvent("probation_completed", "Probation completed", null, probationEnd));

        for (var i = 1; i < history.Count; i++)
        {
            var previous = history[i - 1];
            var current = history[i];

            var reason = string.IsNullOrWhiteSpace(current.ChangeReason) ? "" : $" ({current.ChangeReason})";
            events.Add(new EmployeeHistoryEvent(
                "position_changed", "Position changed",
                $"{PositionName(previous.PositionId)} → {PositionName(current.PositionId)}{reason}",
                current.EffectiveFrom));

            if (current.ReportsToEmployeeId is { } managerId && managerId != previous.ReportsToEmployeeId)
            {
                events.Add(new EmployeeHistoryEvent(
                    "manager_changed", "Reporting manager changed",
                    $"Reporting manager changed to {managerNames.GetValueOrDefault(managerId) ?? "a new manager"}",
                    current.EffectiveFrom));
            }
        }

        var leaveRows = await leave.ListOwnAsync(tenantId, request.EmployeeId, new LeaveRequestListFilter(null, null, null, null), ct);
        events.AddRange(leaveRows
            .Where(r => r.Request.Status == "approved" && r.Request.ApprovedAt is not null)
            .Select(r => new EmployeeHistoryEvent(
                "leave_approved", "Leave approved",
                $"{r.LeaveTypeName} ({Math.Round(r.Request.TotalHours, 2)} h)",
                DateOnly.FromDateTime(r.Request.ApprovedAt!.Value.UtcDateTime))));

        var checklistRows = await checklist.ListByEmployeeAsync(tenantId, request.EmployeeId, ct);
        events.AddRange(checklistRows
            .Where(t => t.Status == EmployeeChecklistTaskStatuses.Completed && t.CompletedAt is not null)
            .Select(t => new EmployeeHistoryEvent(
                "checklist_completed", "Checklist task completed",
                $"{(string.IsNullOrWhiteSpace(t.Category) ? t.LifecycleType : t.Category.Trim())}: {t.TaskTitle}",
                DateOnly.FromDateTime(t.CompletedAt!.Value.UtcDateTime))));

        if (employee.TerminationDate is { } terminated && terminated <= today)
            events.Add(new EmployeeHistoryEvent("terminated", "Employment ended", null, terminated));

        var newestFirst = events
            .Select((e, index) => (e, index))
            .OrderByDescending(x => x.e.Date)
            .ThenBy(x => x.index)
            .Select(x => x.e)
            .Take(request.Limit)
            .ToList();

        return Result<EmployeeHistoryResponse>.Success(new EmployeeHistoryResponse(newestFirst));
    }
}
```

- [ ] **Step 5: Run to verify the handler tests pass**

Run: same command as Step 2. Expected: 10 passed (8 facts + 2 theory rows). If `EmployeeEntity`'s `Id`/`TenantId` initialisers or `Position`'s initialiser don't compile, match them to the entity's real settable properties (both are `BaseEntity`-derived; only the names used in the tests are required).

- [ ] **Step 6: Extend the theory, add the action, run both suites, commit**

Add to the `[Theory]`:

```csharp
    [InlineData("overview/history", "GetOverviewHistory")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;` and the action:

```csharp
    /// <summary>Overview employee history: joined, probation, position and reporting-manager changes,
    /// approved leave, completed checklist tasks and termination - newest first. Lifetime view.</summary>
    [HttpGet("{id:guid}/overview/history")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewHistory(Guid id, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeHistoryQuery(id, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeHistoryResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeHistory src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeHistoryQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview employment history endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `overview/upcoming` (calendar, approved leave, release reminders)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/ReleaseCalendar/RepositoryInterfaces/IReleaseCalendarRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfReleaseCalendarRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfReleaseCalendarRepositoryReadsTests.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeUpcomingResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeUpcoming/GetEmployeeUpcomingQuery.cs` and `...QueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeUpcomingQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Produces (repository): `sealed record UpcomingReleaseRow(Guid Id, DateOnly ScheduledDate, string ReminderType, string? Notes, string VersionName, string ProjectName)` and `IReleaseCalendarRepository.ListForRecipientAsync(Guid tenantId, Guid recipientUserId, DateOnly from, DateOnly to, CancellationToken ct = default) : Task<IReadOnlyList<UpcomingReleaseRow>>` — active entries only, ordered by date. (`CreateProjectCommandHandlerTests` mocks this interface with Moq; adding a member does not break it.)
- Consumes: `ICalendarEventRepository.GetInDateRangeForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)`, `GetRecurringMastersForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset to, CancellationToken ct = default)`, `GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default)`; `ICalendarRecurrenceExpander.Expand(string recurrenceRule, DateTimeOffset seriesStart, DateTimeOffset from, DateTimeOffset to)`; `ILeaveRequestRepository.ListOwnAsync`; `IEmployeeRepository.GetByIdAsync` (for `UserId`).
- Produces (endpoint): `EmployeeUpcomingItem(string Kind, string Title, DateTimeOffset Start, DateTimeOffset? End, bool IsAllDay, string? Detail)` (kinds `calendar | leave | release`); `EmployeeUpcomingResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeUpcomingItem> Items)`; `GetEmployeeUpcomingQuery(Guid EmployeeId, int Days = 14)` (1..60 else 400; window = now .. now + Days; at most 10 items, earliest first); `GET /api/v1/employees/{id}/overview/upcoming?days=`.

- [ ] **Step 1: Write the failing repository test**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfReleaseCalendarRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task ListForRecipient_ReturnsActiveEntriesInTheWindowForThatUser_WithVersionAndProjectNames_EarliestFirst()
    {
        await using var db = BuildInMemoryDb();
        var project = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Website", Identifier = "WEB", IsActive = true };
        var v1 = new ProjectVersion { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Name = "v2.0" };
        var v2 = new ProjectVersion { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Name = "v2.1" };
        db.Projects.Add(project);
        db.ProjectVersions.AddRange(v1, v2);
        db.ReleaseCalendarEntries.AddRange(
            Entry(project.Id, v2.Id, _userId, "2026-10-05"),                                   // in window, later
            Entry(project.Id, v1.Id, _userId, "2026-10-01", notes: "Freeze at noon"),          // in window, earlier
            Entry(project.Id, v1.Id, _userId, "2026-12-01"),                                   // outside window
            Entry(project.Id, v1.Id, Guid.NewGuid(), "2026-10-02"),                            // other user
            Entry(project.Id, v1.Id, _userId, "2026-10-03", isActive: false));                 // inactive
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfReleaseCalendarRepository(db)
            .ListForRecipientAsync(_tenantId, _userId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14));

        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5) }, rows.Select(r => r.ScheduledDate).ToArray());
        Assert.Equal("v2.0", rows[0].VersionName);
        Assert.Equal("Website", rows[0].ProjectName);
        Assert.Equal("Freeze at noon", rows[0].Notes);
        Assert.Equal("v2.1", rows[1].VersionName);
    }

    private ReleaseCalendarEntry Entry(Guid projectId, Guid versionId, Guid userId, string date, string? notes = null, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, VersionId = versionId, RecipientUserId = userId,
        ScheduledDate = DateOnly.Parse(date), Notes = notes, IsActive = isActive
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
```

(Remove the unused `ProjectMembers` using if your analyzer flags it; it is not needed.)

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfReleaseCalendarRepositoryReadsTests"`
Expected: build FAIL — `ListForRecipientAsync` does not exist.

- [ ] **Step 3: Add the repository read**

In `IReleaseCalendarRepository.cs` (add the record above the interface, and the member inside):

```csharp
public sealed record UpcomingReleaseRow(
    Guid Id,
    DateOnly ScheduledDate,
    string ReminderType,
    string? Notes,
    string VersionName,
    string ProjectName);
```

```csharp
    /// <summary>Active release reminders addressed to this user, scheduled from..to (inclusive),
    /// earliest first, with the version and project names. For the employee Overview.</summary>
    Task<IReadOnlyList<UpcomingReleaseRow>> ListForRecipientAsync(
        Guid tenantId, Guid recipientUserId, DateOnly from, DateOnly to, CancellationToken ct = default);
```

In `EfReleaseCalendarRepository.cs` add `using Microsoft.EntityFrameworkCore;` and the method:

```csharp
    public async Task<IReadOnlyList<UpcomingReleaseRow>> ListForRecipientAsync(
        Guid tenantId, Guid recipientUserId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await (
            from entry in _db.ReleaseCalendarEntries.AsNoTracking()
            join version in _db.ProjectVersions.AsNoTracking() on entry.VersionId equals version.Id
            join project in _db.Projects.AsNoTracking() on entry.ProjectId equals project.Id
            where entry.TenantId == tenantId
                  && entry.RecipientUserId == recipientUserId
                  && entry.IsActive
                  && entry.ScheduledDate >= from && entry.ScheduledDate <= to
            orderby entry.ScheduledDate
            select new UpcomingReleaseRow(entry.Id, entry.ScheduledDate, entry.ReminderType, entry.Notes, version.Name, project.Name)
        ).ToListAsync(ct);
    }
```

- [ ] **Step 4: Run to verify the repository test passes**

Run: same command as Step 2. Expected: 1 passed.

- [ ] **Step 5: Write the failing handler test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeUpcomingQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarEventRepository> _events = new();
    private readonly Mock<ICalendarRecurrenceExpander> _expander = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IReleaseCalendarRepository> _releases = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T08:00:00+00:00");

    public GetEmployeeUpcomingQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 30));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = _employeeUserId });
        _events.Setup(e => e.GetInDateRangeForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEvent>());
        _events.Setup(e => e.GetRecurringMastersForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CalendarEvent>());
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        _releases.Setup(r => r.ListForRecipientAsync(_tenantId, _employeeUserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UpcomingReleaseRow>());
    }

    private GetEmployeeUpcomingQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _events.Object, _expander.Object, _leave.Object, _releases.Object, _user.Object, _clock.Object);

    private static CalendarEvent Event(string title, string start, string end, bool isPrivate = false, bool allDay = false, string? location = null) => new()
    {
        Id = Guid.NewGuid(), Title = title, StartDate = DateTimeOffset.Parse(start), EndDate = DateTimeOffset.Parse(end),
        IsPrivate = isPrivate, IsAllDay = allDay, Location = location
    };

    private void ArrangeEvents(params CalendarEvent[] events) =>
        _events.Setup(e => e.GetInDateRangeForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Handle_Returns400_ForAnOutOfRangeWindow(int days)
    {
        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, days), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ExcludesPrivateCalendarEvents_AndKeepsTheRest()
    {
        ArrangeEvents(
            Event("Team meeting", "2026-10-03T09:00:00+00:00", "2026-10-03T10:00:00+00:00", location: "Room 4"),
            Event("Dentist", "2026-10-02T09:00:00+00:00", "2026-10-02T10:00:00+00:00", isPrivate: true));

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Kind.Should().Be("calendar");
        item.Title.Should().Be("Team meeting");
        item.Detail.Should().Be("Room 4");
    }

    [Fact]
    public async Task Handle_ExpandsRecurringSeries_SkipsOverriddenOccurrencesAndPrivateSeries()
    {
        var master = Event("Standup", "2026-09-01T09:00:00+00:00", "2026-09-01T09:15:00+00:00");
        master.RecurrenceRule = "FREQ=DAILY";
        var privateMaster = Event("Therapy", "2026-09-01T12:00:00+00:00", "2026-09-01T13:00:00+00:00", isPrivate: true);
        privateMaster.RecurrenceRule = "FREQ=WEEKLY";
        _events.Setup(e => e.GetRecurringMastersForEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { master, privateMaster });

        var overridden = DateTimeOffset.Parse("2026-10-02T09:00:00+00:00");
        _events.Setup(e => e.GetChildrenForMasterAsync(_tenantId, master.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CalendarEvent { Id = Guid.NewGuid(), RecurrenceOriginalStart = overridden } });
        _expander.Setup(x => x.Expand("FREQ=DAILY", master.StartDate, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
            .Returns(new[] { DateTimeOffset.Parse("2026-10-01T09:00:00+00:00"), overridden, DateTimeOffset.Parse("2026-10-03T09:00:00+00:00") });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Select(i => i.Start).Should().Equal(
            DateTimeOffset.Parse("2026-10-01T09:00:00+00:00"), DateTimeOffset.Parse("2026-10-03T09:00:00+00:00"));
        result.Value.Items.Should().OnlyContain(i => i.Title == "Standup" && i.End == i.Start + TimeSpan.FromMinutes(15));
        _events.Verify(e => e.GetChildrenForMasterAsync(_tenantId, privateMaster.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AddsApprovedLeaveAndReleaseReminders()
    {
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId,
                It.Is<LeaveRequestListFilter>(f => f.Status == "approved" && f.FromDate == new DateOnly(2026, 9, 30) && f.ToDate == new DateOnly(2026, 10, 14)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LeaveRequestListRow(new LeaveRequest
                {
                    Id = Guid.NewGuid(), Status = "approved",
                    StartAt = DateTimeOffset.Parse("2026-10-06T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-10-08T23:59:59+00:00")
                }, "Annual leave", "AL")
            });
        _releases.Setup(r => r.ListForRecipientAsync(_tenantId, _employeeUserId, new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UpcomingReleaseRow(Guid.NewGuid(), new DateOnly(2026, 10, 1), "project_release", null, "v2.0", "Website") });

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId), CancellationToken.None);

        var items = result.Value!.Items;
        items.Select(i => i.Kind).Should().Equal("release", "leave");
        items[0].Title.Should().Be("v2.0");
        items[0].Detail.Should().Be("Website");
        items[0].IsAllDay.Should().BeTrue();
        items[1].Title.Should().Be("Annual leave");
        items[1].Detail.Should().Be("Approved");
        items[1].End.Should().Be(DateTimeOffset.Parse("2026-10-08T23:59:59+00:00"));
    }

    [Fact]
    public async Task Handle_SortsEarliestFirst_AndReturnsAtMostTenItems()
    {
        ArrangeEvents(Enumerable.Range(1, 12)
            .Select(i => Event($"Event {i}", $"2026-10-{i:00}T09:00:00+00:00", $"2026-10-{i:00}T10:00:00+00:00"))
            .Reverse()
            .ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 30), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(10);
        result.Value.Items.Select(i => i.Title).First().Should().Be("Event 1");
        result.Value.Items.Select(i => i.Start).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Handle_ReturnsTheWindowDates()
    {
        var result = await CreateHandler().Handle(new GetEmployeeUpcomingQuery(_employeeId, 7), CancellationToken.None);

        result.Value!.From.Should().Be(new DateOnly(2026, 9, 30));
        result.Value.To.Should().Be(new DateOnly(2026, 10, 7));
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeUpcomingQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 7: Write DTOs, query and handler**

`EmployeeUpcomingResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Kind: calendar | leave | release.</summary>
public sealed record EmployeeUpcomingItem(
    string Kind,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay,
    string? Detail);

public sealed record EmployeeUpcomingResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeUpcomingItem> Items);
```

`GetEmployeeUpcomingQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

public sealed record GetEmployeeUpcomingQuery(Guid EmployeeId, int Days = 14)
    : IRequest<Result<EmployeeUpcomingResponse>>;
```

`GetEmployeeUpcomingQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

/// <summary>
/// What is coming up for one employee in the next N days: calendar events (recurring series
/// expanded like GetCalendarEventsQueryHandler; private events are excluded), approved leave, and
/// release reminders addressed to the employee. Earliest first, at most 10.
/// </summary>
public sealed class GetEmployeeUpcomingQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ICalendarEventRepository events,
    ICalendarRecurrenceExpander expander,
    ILeaveRequestRepository leave,
    IReleaseCalendarRepository releases,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeUpcomingQuery, Result<EmployeeUpcomingResponse>>
{
    public const int MaxDays = 60;
    public const int MaxItems = 10;

    public async Task<Result<EmployeeUpcomingResponse>> Handle(GetEmployeeUpcomingQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeUpcomingResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Days < 1 || request.Days > MaxDays)
            return Result<EmployeeUpcomingResponse>.Failure($"days must be between 1 and {MaxDays}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeUpcomingResponse>.NotFound("The employee or selected organization record could not be found.");

        var from = clock.UtcNow;
        var to = from.AddDays(request.Days);
        var fromDate = clock.Today;
        var toDate = fromDate.AddDays(request.Days);
        var items = new List<EmployeeUpcomingItem>();

        foreach (var e in await events.GetInDateRangeForEmployeeAsync(tenantId, request.EmployeeId, from, to, ct))
        {
            if (e.IsPrivate || e.IsRecurrenceCancelled) continue;
            items.Add(new EmployeeUpcomingItem("calendar", e.Title, e.StartDate, e.EndDate, e.IsAllDay, e.Location));
        }

        foreach (var master in await events.GetRecurringMastersForEmployeeAsync(tenantId, request.EmployeeId, to, ct))
        {
            if (master.IsPrivate || string.IsNullOrWhiteSpace(master.RecurrenceRule)) continue;

            var children = await events.GetChildrenForMasterAsync(tenantId, master.Id, ct);
            var duration = master.EndDate - master.StartDate;
            foreach (var start in expander.Expand(master.RecurrenceRule, master.StartDate, from, to))
            {
                // A detached (edited or cancelled) occurrence is represented by its own child row.
                if (children.Any(c => c.RecurrenceOriginalStart == start)) continue;
                items.Add(new EmployeeUpcomingItem("calendar", master.Title, start, start + duration, master.IsAllDay, master.Location));
            }
        }

        var leaveRows = await leave.ListOwnAsync(
            tenantId, request.EmployeeId, new LeaveRequestListFilter("approved", fromDate, toDate, null), ct);
        items.AddRange(leaveRows.Select(r => new EmployeeUpcomingItem(
            "leave", r.LeaveTypeName, r.Request.StartAt, r.Request.EndAt, true, "Approved")));

        var releaseRows = await releases.ListForRecipientAsync(tenantId, employee.UserId, fromDate, toDate, ct);
        items.AddRange(releaseRows.Select(r => new EmployeeUpcomingItem(
            "release", r.VersionName,
            new DateTimeOffset(r.ScheduledDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            null, true, r.ProjectName)));

        var earliest = items.OrderBy(i => i.Start).Take(MaxItems).ToList();
        return Result<EmployeeUpcomingResponse>.Success(new EmployeeUpcomingResponse(fromDate, toDate, earliest));
    }
}
```

- [ ] **Step 8: Run to verify the handler tests pass**

Run: same command as Step 6. Expected: 8 passed (6 facts + 2 theory rows). If `LeaveRequestListFilter`'s constructor arity differs, use the real one (`Status, FromDate, ToDate, LeaveTypeId`).

- [ ] **Step 9: Extend the theory, add the action, run both suites, commit**

Add to the `[Theory]`:

```csharp
    [InlineData("overview/upcoming", "GetOverviewUpcoming")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;` and:

```csharp
    /// <summary>Overview upcoming items: calendar events (private ones excluded), approved leave and
    /// release reminders in the next N days (default 14, max 60). Not period-aware.</summary>
    [HttpGet("{id:guid}/overview/upcoming")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewUpcoming(Guid id, [FromQuery] int days = 14, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeUpcomingQuery(id, days), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green (including `CreateProjectCommandHandlerTests`, which mocks `IReleaseCalendarRepository`).

Live check (dev server): as a user with `employees:read` and coverage, `GET …/overview/checklists`, `…/overview/history?limit=5`, `…/overview/upcoming?days=14` → 200 each; `?limit=0` and `?days=61` → 400; an employee outside coverage → 403. Report what data existed; empty lists are legitimate for an employee with none.

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement/ReleaseCalendar/RepositoryInterfaces/IReleaseCalendarRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfReleaseCalendarRepository.cs src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeUpcomingResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeUpcoming src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EfReleaseCalendarRepositoryReadsTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeUpcomingQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview upcoming items endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- Sources requested by the user: Upcoming = calendar + leave + releases (Task 3); checklists (Task 1); employment history events (Task 2). Recent Activity is Plan 4B.
- **Correction of an earlier statement:** I said earlier that the checklist endpoint was "ready" and that there is "no task history table". Both were wrong: the existing `GET /employees/{id}/checklist-tasks` returns *offboarding* tasks only (hence the new overview endpoint over `ListByEmployeeAsync`, which covers onboarding too), and Work Management does keep task history (`TaskStatusChangeLog`, `TaskEditLog`, `TaskPercentageLog`, `TaskCommentLog`, `TaskClockingSession`, `TaskComment`) — which Plan 4B uses.
- No module permission is used anywhere in this plan (see "Decisions I have NOT made for you").
- Type consistency: `EmployeeChecklistGroup` fields equal the test's record equality; `EmployeeHistoryEvent(Kind, Title, Detail, Date)` and `EmployeeUpcomingItem(Kind, Title, Start, End, IsAllDay, Detail)` match handler constructor calls; `UpcomingReleaseRow` (repo) matches the test and handler.
- Verified by reading source: repository signatures and namespaces (including the `PositionAssignment` entity/namespace collision and `IPositionRepository`'s namespace), calendar range/recurrence methods and the existing expansion logic, `EmployeeChecklistTask` fields, `ReleaseCalendarEntry`/`ProjectVersion`/`Project` fields, `LeaveRequestStatuses.Approved == "approved"`. Not compile-checked or run; the executor's first `dotnet test` is the first compile. Fix compile errors by aligning to real signatures, never by weakening a test.
- Known limits: history omits status/type/work-mode changes (no records); "release" items are reminders addressed to the employee; calendar events he owns or is invited to are shown except private ones; recurring series are capped at the expander's 500 occurrences (irrelevant for a ≤60-day window).
