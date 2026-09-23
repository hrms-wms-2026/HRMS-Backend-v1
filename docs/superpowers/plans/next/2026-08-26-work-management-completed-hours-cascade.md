# Work Management Completed-Hours Cascade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a task's status crosses the "complete" boundary (either direction), roll the task's completed hours up through every ancestor Objective (module) in the tree, not just the task's direct objective, and onto the root objective's Project as well.

**Architecture:** `MoveTaskStatusCommandHandler` is the only place in the codebase that writes `Objective.CompletedHours` (verified: `EditTaskCommandHandler` and `PushTaskCommandHandler` never touch it). Today it updates only the task's direct objective. Add a private cascade helper to that same handler that, starting from the direct objective, walks `Objective.ParentObjectiveId` up to the root (an objective with `ParentObjectiveId == null`), applying the same `+`/`-` delta at every level, then applies that delta to `Project.CompletedHours` once it reaches the root. No schema change — both columns already exist and are already `decimal`.

**Tech Stack:** C#/.NET, MediatR, EF Core, xUnit + Moq (existing patterns in `MoveTaskStatusCommandHandlerTests.cs`).

**Spec:** No separate design doc — this plan **is** the spec. Confirmed via user conversation 2026-08-26: (1) cascade must reach the root Project's own `CompletedHours`, not stop at the root Objective; (2) this is the only known gap in the approval/hours workflow — `ObjectiveChangeRequestTypes.Edit`/`ExtendAllocation` approval, and task-creation/edit request approval, were all verified already correct and are out of scope for this plan.

## Global Constraints

- No EF migration needed — `Objective.CompletedHours` and `Project.CompletedHours` are both pre-existing `decimal` columns.
- Do not change the existing single-level behavior's outward result for the direct objective — only add cascading beyond it. All 20 existing tests in `MoveTaskStatusCommandHandlerTests.cs` must keep passing unmodified in their assertions (the `Build` helper's signature may gain new optional parameters, but no existing call site should need to change).
- The cascade must run inside the same DB transaction as the rest of the status move (`_unitOfWork.ExecuteInTransactionAsync`) — do not introduce a second transaction or a fire-and-forget call.
- Use `GetTrackedByIdForTenantAsync` for every objective/project fetch in the cascade (never the AsNoTracking variant) so EF's change tracker picks up the mutation for `SaveChangesAsync`'s automatic partial UPDATE — this matches how the handler already fetches the direct objective.
- A broken parent chain (a `ParentObjectiveId` pointing at a row that no longer resolves) must not throw — stop the walk at that point rather than crash the whole status-move transaction. This can only happen from pre-existing data corruption, not from this plan's own writes, so treat it as defensive, not a case to unit-test exhaustively.

---

### Task 1: Cascade CompletedHours through the Objective ancestor chain and onto the root Project

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)` (already injected as `_objectives`), `IProjectRepository.GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct)` (new dependency, add as constructor param `IProjectRepository projects`, field `_projects`).
- Produces: private `Task CascadeCompletedHoursAsync(Guid tenantId, Objective startingObjective, decimal delta, CancellationToken ct)` on `MoveTaskStatusCommandHandler` — internal only, no other class calls it.

- [ ] **Step 1: Write the failing tests**

Add these tests to `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`. First, extend the `Build` helper to optionally wire a parent/grandparent objective and a project mock. Replace the existing `Build` method signature and body with this version (adds `parentObjective`, `grandparentObjective` optional params, adds `IProjectRepository` wiring, adds `project` to the returned tuple — every existing call site keeps compiling unchanged since these are optional/appended):

```csharp
    private (
        MoveTaskStatusCommandHandler Handler,
        Objective Objective,
        WorkTask Task,
        Mock<ITaskStatusRepository> Statuses,
        List<TaskStatusChangeLog> StatusChangeLogs,
        List<TaskPercentageLog> PercentageLogs,
        Mock<ITaskClockingSessionRepository> ClockingSessions,
        Project Project) Build(

        Guid callerEmployeeId, bool callerIsMember, TaskStatusEntity newStatus, decimal? estimatedHours = 8m,
        bool preserveNullStatusObjectiveId = false, Sprint? sprint = null, bool? callerIsEffectiveManager = null,
        int taskCurrentPercent = 0, bool oldStatusMarksComplete = false,
        bool authenticated = true, bool employeeExists = true, bool taskExists = true,
        bool targetStatusExists = true, bool objectiveExists = true, TaskClockingSession? openSession = null,
        Objective? parentObjective = null, Objective? grandparentObjective = null)

    {
        if (!preserveNullStatusObjectiveId && newStatus.ObjectiveId is null)
            newStatus.ObjectiveId = ObjectiveId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);

        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? callerEmployeeId : null);

        var task = new WorkTask
        {
            Id = TaskId,
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            ProjectId = ProjectId,
            Title = "A",
            ShortId = "T-1",
            StatusId = OldStatusId,
            EstimatedHours = estimatedHours,
            ProgressPercent = taskCurrentPercent,
            SprintId = sprint?.Id,

            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

        var oldStatus = new TaskStatusEntity
        {
            Id = OldStatusId,
            TenantId = TenantId,
            Name = "To Do",
            MarksTaskComplete = oldStatusMarksComplete,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var statuses = new Mock<ITaskStatusRepository>();

        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, NewStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetStatusExists ? newStatus : null);

        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldStatus);

        var objective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            OwnerId = OwnerEmployeeId,
            IsActive = true,
            Title = "Obj",
            CompletedHours = 0m,
            ParentObjectiveId = parentObjective?.Id,
            ProjectId = ProjectId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectiveExists ? objective : null);
        if (parentObjective is not null)
        {
            objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, parentObjective.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(parentObjective);
        }
        if (grandparentObjective is not null)
        {
            objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, grandparentObjective.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(grandparentObjective);
        }

        var project = new Project { Id = ProjectId, TenantId = TenantId, CompletedHours = 0m, CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsMember);
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var statusChangeLogs = new List<TaskStatusChangeLog>();
        var statusChangeLogRepository = new Mock<ITaskStatusChangeLogRepository>();
        statusChangeLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskStatusChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskStatusChangeLog, CancellationToken>((log, _) => statusChangeLogs.Add(log))
            .Returns(Task.CompletedTask);

        var percentageLogs = new List<TaskPercentageLog>();
        var percentageLogRepository = new Mock<ITaskPercentageLogRepository>();
        percentageLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskPercentageLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskPercentageLog, CancellationToken>((log, _) => percentageLogs.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();

        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sprints = new Mock<ISprintRepository>();
        if (sprint is not null)
        {
            sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sprint);
        }

        var clockingSessions = new Mock<ITaskClockingSessionRepository>();
        clockingSessions.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        if (openSession is not null)
        {
            clockingSessions.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, openSession.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(openSession);
        }

        var handler = new MoveTaskStatusCommandHandler(
            currentUser.Object,
            identity.Object,
            tasks.Object,
            statuses.Object,
            objectives.Object,
            membership.Object,
            unitOfWork.Object,
            sprints.Object,
            statusChangeLogRepository.Object,
            percentageLogRepository.Object,
            clockingSessions.Object,
            projects.Object);

        return (handler, objective, task, statuses, statusChangeLogs, percentageLogs, clockingSessions, project);

    }
```

This changes every existing test's destructuring from a 7-tuple to an 8-tuple (adds `Project`). Update every existing call site in the file (there are 20) by adding a trailing `_` (or `project` where you want it) to the left-hand tuple pattern — e.g. `var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(...)` becomes `var (handler, _, task, _, statusChangeLogs, percentageLogs, _, _) = Build(...)`. Do this mechanically for all 20 pre-existing `Build(` call sites in the file (every test from `Handle_NotAuthenticated_ReturnsForbidden` through `Handle_TaskInAchievedSprint_ReturnsForbidden`).

Then add these three new tests at the end of the file, before the final closing `}`:

```csharp
    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_CascadesHoursThroughAncestorChainAndProject()
    {
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var grandparent = new Objective
        {
            Id = grandparentId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true,
            Title = "Grandparent", CompletedHours = 50m, ParentObjectiveId = null, ProjectId = ProjectId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var parent = new Objective
        {
            Id = parentId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true,
            Title = "Parent", CompletedHours = 20m, ParentObjectiveId = grandparentId, ProjectId = ProjectId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _, _, _, _, project) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, estimatedHours: 8m,
            parentObjective: parent, grandparentObjective: grandparent);
        objective.CompletedHours = 13m;
        project.CompletedHours = 100m;

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8m, task.CompletedHours);
        Assert.Equal(21m, objective.CompletedHours);
        Assert.Equal(28m, parent.CompletedHours);
        Assert.Equal(58m, grandparent.CompletedHours);
        Assert.Equal(108m, project.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingOutOfCompleteStatus_ReversesCascadeThroughAncestorChainAndProject()
    {
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var grandparent = new Objective
        {
            Id = grandparentId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true,
            Title = "Grandparent", CompletedHours = 58m, ParentObjectiveId = null, ProjectId = ProjectId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var parent = new Objective
        {
            Id = parentId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true,
            Title = "Parent", CompletedHours = 28m, ParentObjectiveId = grandparentId, ProjectId = ProjectId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, statuses, _, _, _, project) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, estimatedHours: 8m,
            parentObjective: parent, grandparentObjective: grandparent);
        task.CompletedHours = 8m;
        objective.CompletedHours = 21m;
        project.CompletedHours = 108m;
        var oldStatusComplete = new TaskStatusEntity
        {
            Id = OldStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldStatusComplete);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Equal(13m, objective.CompletedHours);
        Assert.Equal(20m, parent.CompletedHours);
        Assert.Equal(50m, grandparent.CompletedHours);
        Assert.Equal(100m, project.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_ObjectiveWithNoParent_UpdatesOnlyProject()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _, _, _, _, project) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, estimatedHours: 8m);
        objective.CompletedHours = 13m;
        project.CompletedHours = 100m;

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(21m, objective.CompletedHours);
        Assert.Equal(108m, project.CompletedHours);
    }
```

Add the missing `using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;` and `using ONEVO.Domain.Features.WorkManagement.Projects.Entities;` to the top of the test file's using block.

- [ ] **Step 2: Run tests to verify the new ones fail and everything else still compiles**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MoveTaskStatusCommandHandlerTests"`
Expected: build error — `MoveTaskStatusCommandHandler` has no 12-argument constructor yet (the test file now passes `projects.Object` as a 12th arg). This is the "red" step; a compile failure counts as the expected failure here since the constructor doesn't exist yet.

- [ ] **Step 3: Implement the cascade in the handler**

In `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`:

Add the using and field/constructor param:

```csharp
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
```

```csharp
    private readonly ITaskClockingSessionRepository _clockingSessions;
    private readonly IProjectRepository _projects;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IWorkTaskRepository tasks,
        ITaskStatusRepository statuses,
        IObjectiveRepository objectives,
        IMilestoneMembershipCoordinator membership,
        IUnitOfWork unitOfWork,
        ISprintRepository sprints,
        ITaskStatusChangeLogRepository statusChangeLogs,
        ITaskPercentageLogRepository percentageLogs,
        ITaskClockingSessionRepository clockingSessions,
        IProjectRepository projects)

    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _statuses = statuses;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
        _sprints = sprints;
        _statusChangeLogs = statusChangeLogs;
        _percentageLogs = percentageLogs;
        _clockingSessions = clockingSessions;
        _projects = projects;

    }
```

Replace the two direct-objective mutation lines with cascade calls. The `!wasComplete && willBeComplete` branch's `objective.CompletedHours += task.CompletedHours;` becomes:

```csharp
                await CascadeCompletedHoursAsync(tenantId, objective, task.CompletedHours, innerCt);
```

(keep this line positioned exactly where `objective.CompletedHours += task.CompletedHours;` was — before the percentage log write, since `task.CompletedHours` is already set to `task.EstimatedHours ?? 0m` on the line above it).

The `wasComplete && !willBeComplete` branch currently reads:
```csharp
                objective.CompletedHours -= task.CompletedHours;
                task.CompletedHours = 0m;
```
Replace with (capture the delta before zeroing the task, since the cascade needs the pre-reset value):
```csharp
                var reversedHours = task.CompletedHours;
                task.CompletedHours = 0m;
                await CascadeCompletedHoursAsync(tenantId, objective, -reversedHours, innerCt);
```

Remove the now-redundant `objective.UpdatedAt = now;` line that sits after the two branches if it becomes a duplicate write — check first: the handler currently has one `objective.UpdatedAt = now;` right before `await _unitOfWork.SaveChangesAsync(innerCt);`. Leave that line as-is; it still correctly stamps the direct objective. Do not add `UpdatedAt` stamping to cascaded ancestors or the project — out of scope, `UpdatedAt` on those rows isn't part of this fix.

Add the private helper method at the bottom of the class, before the final closing brace:

```csharp
    private async Task CascadeCompletedHoursAsync(Guid tenantId, Objective startingObjective, decimal delta, CancellationToken ct)
    {
        if (delta == 0m)
            return;

        var current = startingObjective;
        while (true)
        {
            current.CompletedHours += delta;

            if (current.ParentObjectiveId is null)
            {
                var project = await _projects.GetTrackedByIdForTenantAsync(tenantId, current.ProjectId, ct);
                if (project is not null)
                    project.CompletedHours += delta;
                return;
            }

            var parent = await _objectives.GetTrackedByIdForTenantAsync(tenantId, current.ParentObjectiveId.Value, ct);
            if (parent is null)
                return;

            current = parent;
        }
    }
```

Note `Objective` doesn't need an extra `using` — it's already imported at the top of this file (`ONEVO.Domain.Features.WorkManagement.Tasks.Entities` for `WorkTask`/`TaskStatus`; check whether `ONEVO.Domain.Features.WorkManagement.Objectives.Entities` is already present — it's implicitly available because `IObjectiveRepository`'s return type `Objective` already appears in the existing `var objective = await _objectives.GetTrackedByIdForTenantAsync(...)` line, so the type is already resolvable via the existing usings in this file).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~MoveTaskStatusCommandHandlerTests"`
Expected: PASS, all 23 tests (20 pre-existing + 3 new) green.

- [ ] **Step 5: Run the full Work Management unit suite to catch any other break**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`
Expected: PASS, no regressions in sibling handlers.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "fix(work): cascade task CompletedHours through ancestor Objectives and onto Project"
```

---

## Self-Review Notes (completed during plan authoring)

- **Spec coverage:** Single requirement (cascade CompletedHours to all ancestors + root Project on both completion and un-completion) — Task 1 covers both directions plus the "already at root" edge case explicitly.
- **Placeholder scan:** No TODOs/TBDs; all steps carry real, complete code including the mechanical 20-site tuple-update instruction (called out explicitly rather than hidden behind "update the tests" hand-waving).
- **Type consistency:** `CascadeCompletedHoursAsync(Guid, Objective, decimal, CancellationToken)` signature matches both call sites; `Project.CompletedHours` and `Objective.CompletedHours` are both `decimal`, no cast needed; constructor param order matches DI's positional resolution (irrelevant for DI itself, since .NET resolves by type, but kept consistent with existing field-declaration order for readability).
