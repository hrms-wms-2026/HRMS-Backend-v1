# Work Management — Task Foundation, Part 5: My Deadlines Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One read-only endpoint, `GET /api/v1/work/my-deadlines`, giving the caller's own Objective and Task deadlines — Work Management's entire surface for Calendar integration (spec §7). No new table.

**Architecture:** Single query handler, two repository calls (one on `IObjectiveRepository`, one on `IWorkTaskRepository`), no transaction needed (read-only).

**Tech Stack:** Same as Parts 1-4.

**Spec:** `docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md` §7.

## Global Constraints

- Prerequisite: Part 1 (`IWorkTaskRepository`, `ITaskAssignmentRepository`) must be implemented first.
- Do not touch `calendar_events` or any Calendar-module file — this endpoint is the entire integration surface, per spec §7 and the scope guardrail already stated in the Part 1-4 plans.

---

### Task 1: Repository methods for deadline lookups

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Repositories/WorkManagement/ObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Repositories/WorkManagement/WorkTaskRepository.cs`

**Interfaces:**
- Produces: `IObjectiveRepository.GetOwnedByEmployeeIdWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)`, `IWorkTaskRepository.GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)`.

- [ ] **Step 1: Write the failing test for each new repository method — these are thin query methods, so this codebase's convention (confirmed by the absence of dedicated repository-level tests anywhere in Parts 1-4's reference reads) is to test them indirectly through the handler test in Task 2 rather than in isolation. Skip a standalone repository test; proceed to Step 2.**

- [ ] **Step 2: Add to `IObjectiveRepository`:**

```csharp
/// <summary>Active objectives owned by this employee with EndDate in [from, to]. For the
/// my-deadlines endpoint (spec §7) - not used by any other query.</summary>
Task<IReadOnlyList<Objective>> GetOwnedByEmployeeIdWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
```

```csharp
// ObjectiveRepository.cs implementation
public async Task<IReadOnlyList<Objective>> GetOwnedByEmployeeIdWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    => await _db.Set<Objective>().AsNoTracking()
        .Where(o => o.TenantId == tenantId && o.OwnerId == employeeId && o.IsActive && o.EndDate >= from && o.EndDate <= to)
        .ToListAsync(ct);
```

- [ ] **Step 3: Add to `IWorkTaskRepository`:**

```csharp
/// <summary>Tasks with an active assignment to this employee and DueDate in [from, to]. For the
/// my-deadlines endpoint (spec §7) - not used by any other query.</summary>
Task<IReadOnlyList<WorkTask>> GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
```

```csharp
// WorkTaskRepository.cs implementation
public async Task<IReadOnlyList<WorkTask>> GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    => await (
        from t in _db.Set<WorkTask>().AsNoTracking()
        join a in _db.Set<TaskAssignment>().AsNoTracking() on t.Id equals a.TaskId
        where t.TenantId == tenantId && a.EmployeeId == employeeId && t.DueDate != null && t.DueDate >= from && t.DueDate <= to
        select t
    ).Distinct().ToListAsync(ct);
```

- [ ] **Step 4: No commit yet — proceed to Task 2, commit together (this task has no independently-testable deliverable per the plan's own Task Right-Sizing rule; it's scaffolding for Task 2).**

### Task 2: `GetMyDeadlines` query, handler, controller, Postman doc

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyDeadlines/{GetMyDeadlinesQuery,GetMyDeadlinesQueryHandler}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/MyDeadlinesResponse.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Create: `docs/postman-request/Work Management/Get My Deadlines.md`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyDeadlinesQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetMyDeadlinesQuery(DateOnly From, DateOnly To) : IRequest<Result<MyDeadlinesResponse>>`, `MyDeadlinesResponse(IReadOnlyList<ObjectiveDeadlineItem> ObjectiveDeadlines, IReadOnlyList<TaskDeadlineItem> TaskDeadlines)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetMyDeadlinesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsOwnedObjectivesAndAssignedTasksInRange()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetOwnedByEmployeeIdWithinRangeAsync(TenantId, EmployeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { new() { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Milestone A", EndDate = new DateOnly(2026, 8, 15), OwnerId = EmployeeId, CreatedAt = DateTimeOffset.UtcNow } });

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetAssignedToEmployeeWithinRangeAsync(TenantId, EmployeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { new() { Id = Guid.NewGuid(), TenantId = TenantId, Title = "Task A", ShortId = "T-1", DueDate = new DateOnly(2026, 8, 20), CreatedAt = DateTimeOffset.UtcNow } });

        var handler = new GetMyDeadlinesQueryHandler(currentUser.Object, identity.Object, objectives.Object, tasks.Object);
        var result = await handler.Handle(new GetMyDeadlinesQuery(from, to), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ObjectiveDeadlines);
        Assert.Single(result.Value!.TaskDeadlines);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write the DTO, query, handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/MyDeadlinesResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record ObjectiveDeadlineItem(Guid ObjectiveId, string Title, DateOnly EndDate);
public sealed record TaskDeadlineItem(Guid TaskId, string ShortId, string Title, DateOnly DueDate);
public sealed record MyDeadlinesResponse(IReadOnlyList<ObjectiveDeadlineItem> ObjectiveDeadlines, IReadOnlyList<TaskDeadlineItem> TaskDeadlines);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyDeadlines/GetMyDeadlinesQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;

public sealed record GetMyDeadlinesQuery(DateOnly From, DateOnly To) : IRequest<Result<MyDeadlinesResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyDeadlines/GetMyDeadlinesQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;

public class GetMyDeadlinesQueryHandler : IRequestHandler<GetMyDeadlinesQuery, Result<MyDeadlinesResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public GetMyDeadlinesQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _tasks = tasks;
    }

    public async Task<Result<MyDeadlinesResponse>> Handle(GetMyDeadlinesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyDeadlinesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<MyDeadlinesResponse>.Forbidden("No employee record for the current user.");

        var objectives = await _objectives.GetOwnedByEmployeeIdWithinRangeAsync(tenantId, callerEmployeeId.Value, request.From, request.To, ct);
        var tasks = await _tasks.GetAssignedToEmployeeWithinRangeAsync(tenantId, callerEmployeeId.Value, request.From, request.To, ct);

        return Result<MyDeadlinesResponse>.Success(new MyDeadlinesResponse(
            objectives.Select(o => new ObjectiveDeadlineItem(o.Id, o.Title, o.EndDate)).ToList(),
            tasks.Where(t => t.DueDate.HasValue).Select(t => new TaskDeadlineItem(t.Id, t.ShortId, t.Title, t.DueDate!.Value)).ToList()));
    }
}
```

- [ ] **Step 4: Add the controller route to `TasksController` (Part 1 Task 11):**

```csharp
[HttpGet("my-deadlines")]
public async Task<IActionResult> MyDeadlines([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
{
    var result = await _mediator.Send(new GetMyDeadlinesQuery(from, to), ct);

    return result.IsSuccess
        ? Ok(result.Value!.ToViewModel())
        : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

- [ ] **Step 5: Write the ViewModel + Contracts mapper, Postman doc.**

- [ ] **Step 6: Run tests, verify PASS. Run the full Work Management test suite one final time to confirm all five parts of this plan series are green together.**

Run: `dotnet test --filter FullyQualifiedName~WorkManagement`

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/ src/ONEVO.Application/Features/WorkManagement/Tasks/ src/ONEVO.Infrastructure/Repositories/WorkManagement/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/ docs/postman-request/Work\ Management/Get\ My\ Deadlines.md docs/postman-request/README.md tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyDeadlinesQueryHandlerTests.cs
git commit -m "feat(work): GetMyDeadlines endpoint - Work Management's calendar-integration surface"
```

## Part 5 complete — full backend plan series done

At this point every endpoint in spec §5/§4/§6/§7 exists, tested, and documented. Move this whole `2026-08-16-work-management-task-foundation/` folder's status to `finished` in `plans/SUMMARY.md` and `plans/finished/SUMMARY.md` once a final full-suite run and review pass are clean (per `FILE_CREATION_RULES.md` rule 2).
