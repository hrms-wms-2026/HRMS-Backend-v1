# Task Subtasks (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Wire up the existing-but-unmapped `WorkTask.ParentTaskId` as a real relationship so a subtask is a full `WorkTask` row, and expose the API surface (create, list, progress counts) the frontend needs to render subtasks on the Board, Backlog, and task detail panel.

**Architecture:** No new entity. A subtask is a `WorkTask` with `ParentTaskId` set, created via a new reduced-field `CreateSubtaskCommand` that inherits `ObjectiveId`/`ProjectId`/`CategoryId` from its parent. `WorkTaskResponse` gains `ParentTaskId`/`SubtaskTotalCount`/`SubtaskCompletedCount`, computed in-memory in `GetProjectTasksQueryHandler` from the same already-materialized task list it already fetches (no new SQL). A new `GetSubtasksQuery` lazy-loads full child rows for the detail panel and the Board/Backlog expand affordance.

**Tech Stack:** .NET, EF Core, MediatR, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-09-21-task-subtasks-design.md`

## Global Constraints

- A subtask cannot itself have subtasks (one level only, per spec's "Out of scope (v1)").
- Subtask creation permission mirrors `CreateTaskCommandHandler`: caller must be `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync` on the **parent's** objective.
- Subtask creation does not re-run `CreateTaskCommandHandler`'s calendar-event-window or allocation-slack checks — those are tied to fields (`EstimatedHours`, and module-wide event coverage) outside the reduced subtask field set (`Title`, `Priority`, `DueDate`, `AssigneeEmployeeId`). This is a deliberate scope decision, not an oversight.
- Every new/changed public method keeps this repo's existing `Result<T>` return convention (`Forbidden`/`NotFound`/`Conflict`/`Failure`/`Success`) — no exceptions thrown for expected failure paths.
- Migration command: `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`, run from the repo root (`HRMS-Backend-v1`).

---

### Task 1: Wire up `ParentTaskId` as a real FK + migration

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskParentRelationship.cs` (generated)
- Test: `tests/ONEVO.Tests.Integration/...` — no new integration test file; verified by Task 3/5's integration coverage exercising the FK.

**Interfaces:**
- Consumes: `WorkTask.ParentTaskId` (already exists on the entity, currently unmapped).
- Produces: a real self-referencing FK + index other tasks read/write directly on `WorkTask.ParentTaskId` — no new C# API from this task.

- [x] **Step 1: Add the FK + index to `WorkTaskConfiguration.cs`**

Edit `Configure` to add, right after the existing `builder.HasIndex(t => new { t.TenantId, t.ProjectId, t.CategoryId })...` block:

```csharp
        builder.HasIndex(t => new { t.TenantId, t.ParentTaskId })
            .HasDatabaseName("ix_tasks_tenant_id_parent_task_id");
```

And after the existing `builder.HasOne<TaskCategory>()...` line, add:

```csharp
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(t => t.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
```

`OnDelete(DeleteBehavior.Restrict)` matches every other FK on this entity — a task with subtasks cannot be hard-deleted without first deleting/reassigning its subtasks.

- [x] **Step 2: Generate the migration**

Run from `HRMS-Backend-v1` repo root:

```bash
dotnet ef migrations add AddTaskParentRelationship --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

- [x] **Step 3: Verify the generated migration**

Open the generated `<timestamp>_AddTaskParentRelationship.cs` and confirm `Up()` contains a `CreateIndex` for `ix_tasks_tenant_id_parent_task_id` and an `AddForeignKey` for `parent_task_id` → `tasks.id` with `onDelete: ReferentialAction.Restrict`. No hand-editing needed if EF generated both — this is a pure additive change (the column already exists in every environment), so no backfill logic is required.

- [x] **Step 4: Build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: builds with no errors.

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(work): wire up WorkTask.ParentTaskId as a real self-referencing FK"
```

---

### Task 2: Add subtask fields to `WorkTaskResponse` / `WorkTaskViewModel`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `WorkTaskResponse` now carries `ParentTaskId` (`Guid?`), `SubtaskTotalCount` (`int`), `SubtaskCompletedCount` (`int`) — Tasks 3, 5, 6 populate these. `WorkTaskViewModel` carries the same three fields on the wire (camelCase via the API's default JSON policy: `parentTaskId`, `subtaskTotalCount`, `subtaskCompletedCount`).

- [x] **Step 1: Add the fields to `WorkTaskResponse`**

In `WorkTaskResponse.cs`, change the record to append the three new optional trailing params (defaults keep every existing positional-argument call site, e.g. in `CreateTaskCommandHandler.cs`, compiling unchanged):

```csharp
public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid>? AssigneeEmployeeIds = null, Guid? OpenClockSessionEmployeeId = null,
    DateTimeOffset? OpenClockSessionClockInAt = null, int TotalLoggedMinutes = 0,
    Guid? ActiveEventId = null, string? ActiveEventName = null,
    IReadOnlyList<TaskAttachmentDto>? Attachments = null,
    IReadOnlyList<TaskAssigneeIdentityDto>? Assignees = null,
    Guid? ParentTaskId = null, int SubtaskTotalCount = 0, int SubtaskCompletedCount = 0);
```

- [x] **Step 2: Add the fields to `WorkTaskViewModel`**

In `TaskContracts.cs`, change `WorkTaskViewModel` to:

```csharp
public sealed record WorkTaskViewModel(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid> AssigneeEmployeeIds, Guid? OpenClockSessionEmployeeId,
    DateTimeOffset? OpenClockSessionClockInAt, int TotalLoggedMinutes,
    IReadOnlyList<TaskAttachmentViewModel> Attachments,
    IReadOnlyList<TaskAssigneeIdentityViewModel> Assignees,
    Guid? ParentTaskId, int SubtaskTotalCount, int SubtaskCompletedCount);
```

This record has no default values (matching its current definition), so the one construction site (the mapper below) must be updated in the same step or the build fails — that's Step 3.

- [x] **Step 3: Update `WorkTaskViewModelMapper.ToViewModel(this WorkTaskResponse dto)`**

In `WorkTaskViewModelMapper.cs`, change the `WorkTaskResponse` overload to pass the three new values through:

```csharp
    public static WorkTaskViewModel ToViewModel(this WorkTaskResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.ShortId, dto.Title, dto.Description,
        dto.CategoryId, dto.StatusId, dto.Priority, dto.StoryPoints,
        dto.DueDate, dto.EstimatedHours, dto.CompletedHours, dto.ProgressPercent, dto.SprintId,
        dto.AssigneeEmployeeIds ?? Array.Empty<Guid>(), dto.OpenClockSessionEmployeeId,
        dto.OpenClockSessionClockInAt, dto.TotalLoggedMinutes,
        (dto.Attachments ?? Array.Empty<TaskAttachmentDto>())
            .Select(a => new TaskAttachmentViewModel(a.FileId, a.FileName, a.FileSizeBytes, a.ContentType)).ToList(),
        (dto.Assignees ?? Array.Empty<TaskAssigneeIdentityDto>())
            .Select(a => new TaskAssigneeIdentityViewModel(a.EmployeeId, a.Name, a.AvatarUrl)).ToList(),
        dto.ParentTaskId, dto.SubtaskTotalCount, dto.SubtaskCompletedCount);
```

- [x] **Step 4: Build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: builds with no errors. If any other file constructs a `WorkTaskViewModel` positionally, the build error will name it — add `default, 0, 0` (or the real values, if known) at the call site; as of this plan's research, `WorkTaskViewModelMapper.cs` is the only construction site.

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs
git commit -m "feat(work): add ParentTaskId and subtask counts to WorkTaskResponse/ViewModel"
```

---

### Task 3: `CreateSubtaskCommand` + handler + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateSubtask/CreateSubtaskCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateSubtask/CreateSubtaskCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (add `CreateSubtaskRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (add `using` + endpoint)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateSubtaskCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `WorkTaskResponse` (Task 2), `IWorkTaskRepository.GetByIdForTenantAsync`/`AddAsync`, `ITaskAssignmentRepository.AddAsync`, `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`/`GetActiveAssigneeAsync`, `ITaskStatusRepository.GetProjectTemplateAsync`, `IProjectRepository.GetByIdForTenantAsync`/`IncrementAndGetNextTaskNumberAsync`.
- Produces: `CreateSubtaskCommand(Guid ParentTaskId, string Title, string? Priority, DateOnly? DueDate, Guid? AssigneeEmployeeId) : IRequest<Result<WorkTaskResponse>>` and endpoint `POST api/v1/work/tasks/{parentTaskId}/subtasks`. Task 4/5's `GetSubtasksQueryHandler` and Task 6's board query both rely on the fact that a created subtask has `ParentTaskId` set and `ProjectId`/`ObjectiveId`/`CategoryId` copied from its parent.

- [x] **Step 1: Write the command**

Create `CreateSubtaskCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;

public sealed record CreateSubtaskCommand(
    Guid ParentTaskId, string Title, string? Priority, DateOnly? DueDate, Guid? AssigneeEmployeeId
) : IRequest<Result<WorkTaskResponse>>;
```

- [x] **Step 2: Write the failing test for the happy path**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateSubtaskCommandHandlerTests.cs`, mirroring `AssignTaskCommandHandlerTests.cs`'s `Build` helper:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateSubtaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentTaskId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid AssigneeEmployeeId = Guid.NewGuid();
    private static readonly Guid AssigneeUserId = Guid.NewGuid();

    private (CreateSubtaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<ITaskAssignmentRepository> Assignments) Build(
        WorkTask? parent, Employee? assignee, bool callerIsEffectiveManager = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = CallerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);
        projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager);
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, AssigneeEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignee);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>
            {
                new() { Id = StatusId, TenantId = TenantId, Name = "To Do", Category = TaskStatusCategories.NotStarted, DisplayOrder = 0 }
            });

        var assignments = new Mock<ITaskAssignmentRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses.WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses.WorkTaskResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSubtaskCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, projects.Object, tasks.Object,
            statuses.Object, assignments.Object, membership.Object, unitOfWork.Object);
        return (handler, tasks, assignments);
    }

    [Fact]
    public async Task Handle_HappyPathWithAssignee_CreatesSubtaskAndAssigns()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignee = new Employee { Id = AssigneeEmployeeId, TenantId = TenantId, UserId = AssigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var (handler, tasks, assignments) = Build(parent, assignee);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Do the sub-thing", "high", null, AssigneeEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParentTaskId, result.Value!.ParentTaskId);
        Assert.Equal(CategoryId, result.Value.CategoryId);
        tasks.Verify(x => x.AddAsync(It.Is<WorkTask>(t => t.ParentTaskId == ParentTaskId && t.ProjectId == ProjectId && t.ObjectiveId == ObjectiveId && t.CategoryId == CategoryId), It.IsAny<CancellationToken>()), Times.Once);
        assignments.Verify(x => x.AddAsync(It.Is<TaskAssignment>(a => a.EmployeeId == AssigneeEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, tasks, _) = Build(parent: null, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ParentIsAlreadyASubtask_ReturnsConflict()
    {
        var grandparent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, ParentTaskId = Guid.NewGuid(), Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, _) = Build(grandparent, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotEffectiveManager_ReturnsForbidden()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, _) = Build(parent, assignee: null, callerIsEffectiveManager: false);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AssigneeNotActive_ReturnsFailure()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, CategoryId = CategoryId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, tasks, assignments) = Build(parent, assignee: null);

        var result = await handler.Handle(new CreateSubtaskCommand(ParentTaskId, "Title", null, null, AssigneeEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.IsAny<WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CreateSubtaskCommandHandlerTests`
Expected: FAIL — `CreateSubtaskCommandHandler` doesn't exist yet.

- [x] **Step 4: Write the handler**

Create `CreateSubtaskCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;

public class CreateSubtaskCommandHandler : IRequestHandler<CreateSubtaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSubtaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        ITaskAssignmentRepository assignments, IMilestoneMembershipCoordinator membership, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _tasks = tasks;
        _statuses = statuses;
        _assignments = assignments;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(CreateSubtaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var parent = await _tasks.GetByIdForTenantAsync(tenantId, request.ParentTaskId, ct);
        if (parent is null)
            return Result<WorkTaskResponse>.NotFound("Parent task not found.");

        if (parent.ParentTaskId is not null)
            return Result<WorkTaskResponse>.Conflict("A subtask cannot itself have subtasks.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, parent.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create subtasks.");

        Domain.Features.CoreHr.Entities.Employee? assignee = null;
        if (request.AssigneeEmployeeId is { } assigneeEmployeeId)
        {
            assignee = await _membership.GetActiveAssigneeAsync(tenantId, assigneeEmployeeId, ct);
            if (assignee is null)
                return Result<WorkTaskResponse>.Failure("The assignee must be an active employee in this tenant.");
        }

        var project = await _projects.GetByIdForTenantAsync(tenantId, parent.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Project not found.");

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var defaultStatus = statuses.Where(s => s.Category == TaskStatusCategories.NotStarted).OrderBy(s => s.DisplayOrder).FirstOrDefault()
            ?? statuses.Where(s => s.Category == TaskStatusCategories.Active).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
            return Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, parent.ProjectId, innerCt);
            var now = DateTimeOffset.UtcNow;
            var subtask = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = parent.ProjectId, ParentTaskId = parent.Id,
                ObjectiveId = parent.ObjectiveId, ShortId = $"{project.Identifier}-{taskNumber}",
                StatusId = defaultStatus.Id, Title = request.Title.Trim(), CategoryId = parent.CategoryId,
                Priority = request.Priority ?? WorkTaskPriorities.Medium, DueDate = request.DueDate,
                CompletedHours = 0m, ProgressPercent = 0, CreatedById = userId, CreatedAt = now
            };

            await _tasks.AddAsync(subtask, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            if (assignee is not null)
            {
                await _assignments.AddAsync(new TaskAssignment
                {
                    Id = Guid.NewGuid(), TaskId = subtask.Id, UserId = assignee.UserId, EmployeeId = assignee.Id,
                    AssignedById = callerEmployeeId.Value, AssignedAt = now
                }, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
            }

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                subtask.Id, subtask.ObjectiveId, subtask.ShortId, subtask.Title, subtask.Description,
                subtask.CategoryId, subtask.StatusId, subtask.Priority, subtask.StoryPoints,
                subtask.DueDate, subtask.EstimatedHours, subtask.CompletedHours, subtask.ProgressPercent, subtask.SprintId,
                AssigneeEmployeeIds: assignee is not null ? new[] { assignee.Id } : Array.Empty<Guid>(),
                ParentTaskId: subtask.ParentTaskId));
        }, ct);
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CreateSubtaskCommandHandlerTests`
Expected: PASS (all 5 tests).

- [x] **Step 6: Add the request contract**

In `TaskContracts.cs`, add near `AssignTaskRequest`:

```csharp
public sealed record CreateSubtaskRequest(string Title, string? Priority, DateOnly? DueDate, Guid? AssigneeEmployeeId);
```

- [x] **Step 7: Add the controller endpoint**

In `TasksController.cs`, add the using directive near the other `Commands.` imports:

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateSubtask;
```

Then add the endpoint, right after the existing `Create` action (the `objectives/{objectiveId:guid}/tasks` one):

```csharp
    [HttpPost("tasks/{parentTaskId:guid}/subtasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreateSubtask(Guid parentTaskId, [FromBody] CreateSubtaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSubtaskCommand(
            parentTaskId, request.Title, request.Priority, request.DueDate, request.AssigneeEmployeeId), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [x] **Step 8: Build and run the full unit suite**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj && dotnet test tests/ONEVO.Tests.Unit`
Expected: builds clean, all tests pass (including the 5 new ones).

- [x] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateSubtask/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateSubtaskCommandHandlerTests.cs
git commit -m "feat(work): add CreateSubtaskCommand and POST tasks/{id}/subtasks endpoint"
```

---

### Task 4: `IWorkTaskRepository.GetByParentTaskIdAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`

**Interfaces:**
- Consumes: `WorkTask.ParentTaskId` (Task 1).
- Produces: `Task<IReadOnlyList<WorkTask>> GetByParentTaskIdAsync(Guid tenantId, Guid parentTaskId, CancellationToken ct = default)` — consumed by Task 5's `GetSubtasksQueryHandler`.

- [x] **Step 1: Add the interface method**

In `IWorkTaskRepository.cs`, add next to `GetByObjectiveIdAsync`:

```csharp
    Task<IReadOnlyList<WorkTask>> GetByParentTaskIdAsync(Guid tenantId, Guid parentTaskId, CancellationToken ct = default);
```

- [x] **Step 2: Implement it**

In `EfWorkTaskRepository.cs`, add next to `GetByObjectiveIdAsync`:

```csharp
    public async Task<IReadOnlyList<WorkTask>> GetByParentTaskIdAsync(Guid tenantId, Guid parentTaskId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ParentTaskId == parentTaskId)
            .ToListAsync(ct);
```

- [x] **Step 3: Build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: builds with no errors.

- [x] **Step 4: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs
git commit -m "feat(work): add IWorkTaskRepository.GetByParentTaskIdAsync"
```

---

### Task 5: `GetSubtasksQuery` + handler + endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSubtasks/GetSubtasksQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSubtasks/GetSubtasksQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetSubtasksQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetByIdForTenantAsync`/`GetByParentTaskIdAsync` (Task 4), `IProjectRepository.GetByIdForTenantAsync`, `IProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync`, `IPermissionResolver.ResolveAsync`, `ITaskAssignmentRepository.GetByTaskIdsAsync`, `ICallerIdentityResolver.ResolveIdentitiesByEmployeeIdAsync`, `IFileStorageService.GetSignedUrlAsync`.
- Produces: `GetSubtasksQuery(Guid ParentTaskId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>` and endpoint `GET api/v1/work/tasks/{parentTaskId}/subtasks` — this is what the frontend's Board/Backlog expand affordance and the task detail panel call.

- [x] **Step 1: Write the query**

Create `GetSubtasksQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;

public sealed record GetSubtasksQuery(Guid ParentTaskId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
```

- [x] **Step 2: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetSubtasksQueryHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetSubtasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentTaskId = Guid.NewGuid();

    private (GetSubtasksQueryHandler Handler, Mock<IWorkTaskRepository> Tasks) Build(
        WorkTask? parent, IReadOnlyList<WorkTask> children, bool hasReadPermission = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);
        identity.Setup(x => x.ResolveIdentitiesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, EmployeeIdentitySummary>());

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(parent);
        tasks.Setup(x => x.GetByParentTaskIdAsync(TenantId, ParentTaskId, It.IsAny<CancellationToken>())).ReturnsAsync(children);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new HashSet<string> { "projects:read" } : new HashSet<string>());

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>());

        var fileStorage = new Mock<IFileStorageService>();

        var handler = new GetSubtasksQueryHandler(
            currentUser.Object, identity.Object, fileStorage.Object, tasks.Object, projects.Object,
            members.Object, permissionResolver.Object, assignments.Object);
        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsChildren()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var child = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, ParentTaskId = ParentTaskId, Title = "Child", ShortId = "PRJ-2", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(parent, new[] { child });

        var result = await handler.Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(child.Id, result.Value![0].Id);
        Assert.Equal(ParentTaskId, result.Value[0].ParentTaskId);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var (handler, _) = Build(parent: null, children: Array.Empty<WorkTask>());

        var result = await handler.Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoReadPermissionAndNotAMember_ReturnsNotFound()
    {
        var parent = new WorkTask { Id = ParentTaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = Guid.NewGuid(), Title = "Parent", ShortId = "PRJ-1", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(parent, Array.Empty<WorkTask>(), hasReadPermission: false);

        var result = await handler.Handle(new GetSubtasksQuery(ParentTaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

Note: if `ICallerIdentityResolver.ResolveIdentitiesByEmployeeIdAsync`'s return type isn't named `EmployeeIdentitySummary`, check the actual interface (`grep -n "ResolveIdentitiesByEmployeeIdAsync" src/ONEVO.Application/Features/WorkManagement/Common/Services/ICallerIdentityResolver.cs`) and use its real return type in the mock setup instead.

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetSubtasksQueryHandlerTests`
Expected: FAIL — `GetSubtasksQueryHandler` doesn't exist yet.

- [x] **Step 4: Write the handler**

Create `GetSubtasksQueryHandler.cs`, mirroring `GetTaskByIdQueryHandler`'s permission check and `GetProjectTasksQueryHandler`'s assignee-identity resolution:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;

public sealed class GetSubtasksQueryHandler : IRequestHandler<GetSubtasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private static readonly TimeSpan AvatarUrlExpiry = TimeSpan.FromMinutes(15);

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IFileStorageService _fileStorage;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ITaskAssignmentRepository _assignments;

    public GetSubtasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IFileStorageService fileStorage,
        IWorkTaskRepository tasks, IProjectRepository projects, IProjectMemberRepository members,
        IPermissionResolver permissionResolver, ITaskAssignmentRepository assignments)
    {
        _currentUser = currentUser;
        _identity = identity;
        _fileStorage = fileStorage;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _assignments = assignments;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetSubtasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("No employee record for the current user.");

        var parent = await _tasks.GetByIdForTenantAsync(tenantId, request.ParentTaskId, ct);
        if (parent is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, parent.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission)
        {
            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(parent.ObjectiveId))
                return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");
        }

        var children = await _tasks.GetByParentTaskIdAsync(tenantId, parent.Id, ct);
        if (children.Count == 0)
            return Result<IReadOnlyList<WorkTaskResponse>>.Success(Array.Empty<WorkTaskResponse>());

        var assignments = await _assignments.GetByTaskIdsAsync(children.Select(c => c.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        var distinctAssigneeIds = assigneesByTaskId.Values.SelectMany(ids => ids).Distinct().ToList();
        var identitiesByEmployeeId = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, distinctAssigneeIds, ct);
        var assigneeIdentityByEmployeeId = new Dictionary<Guid, TaskAssigneeIdentityDto>();
        foreach (var employeeId in distinctAssigneeIds)
        {
            if (!identitiesByEmployeeId.TryGetValue(employeeId, out var identity))
            {
                assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, "Unknown employee", null);
                continue;
            }

            string? avatarUrl = null;
            if (identity.AvatarFileId is { } avatarFileId)
            {
                var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, avatarFileId, AvatarUrlExpiry, ct);
                avatarUrl = urlResult.IsSuccess ? urlResult.Value : null;
            }
            assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, identity.Name, avatarUrl);
        }

        var responses = children.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.CategoryId, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            AssigneeEmployeeIds: assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()),
            Assignees: assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>())
                .Select(employeeId => assigneeIdentityByEmployeeId[employeeId]).ToList(),
            ParentTaskId: t.ParentTaskId)).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetSubtasksQueryHandlerTests`
Expected: PASS (all 3 tests). If `EmployeeIdentitySummary`/return type mismatches surface a compile error in the test file, fix the mock's generic type to whatever `ICallerIdentityResolver.ResolveIdentitiesByEmployeeIdAsync` actually declares — the handler code above only relies on `.AvatarFileId` and `.Name` being present on that type, which the existing `GetProjectTasksQueryHandler` already depends on identically.

- [x] **Step 6: Add the controller endpoint**

In `TasksController.cs`, add the using directive:

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;
```

Then add, right after the new `CreateSubtask` action from Task 3:

```csharp
    [HttpGet("tasks/{parentTaskId:guid}/subtasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetSubtasks(Guid parentTaskId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSubtasksQuery(parentTaskId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [x] **Step 7: Build and run the full unit suite**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj && dotnet test tests/ONEVO.Tests.Unit`
Expected: builds clean, all tests pass.

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSubtasks/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetSubtasksQueryHandlerTests.cs
git commit -m "feat(work): add GetSubtasksQuery and GET tasks/{id}/subtasks endpoint"
```

---

### Task 6: Board/Backlog query — exclude subtasks, compute progress counts

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTasksQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskStatusRepository.GetProjectTemplateAsync` (already used elsewhere in this codebase, not yet injected into this handler).
- Produces: `GetProjectTasksQueryHandler.Handle(...)` now excludes subtasks from its returned list and populates `SubtaskTotalCount`/`SubtaskCompletedCount` on every returned parent — this is what the frontend Board/Backlog badge (Task 3 of the frontend plan) reads.

- [x] **Step 1: Update `BuildHandler` to accept a status repository**

The current `BuildHandler` helper in this file (verified 2026-09-21) takes `(Project? project, IReadOnlyList<Guid> accessibleObjectiveIds, bool hasReadPermission, IReadOnlyList<WorkTask>? tasks = null, IReadOnlyList<TaskAssignment>? assignments = null, bool authenticated = true, IReadOnlyDictionary<Guid, EmployeeIdentityDto>? identities = null, string? signedAvatarUrl = "...")` and constructs the handler positionally as `new GetProjectTasksQueryHandler(currentUser.Object, identity.Object, fileStorage.Object, projects.Object, members.Object, permissions.Object, taskRepository.Object, assignmentRepository.Object, sessionRepository.Object, CalendarEventRepositoryMocks.Empty().Object)`.

Add a `statuses` parameter and a `ITaskStatusRepository` mock. Change the `BuildHandler` signature to add, after `signedAvatarUrl`:

```csharp
        string? signedAvatarUrl = "https://files.example.test/signed-avatar",
        IReadOnlyList<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>? statuses = null)
```

Add inside the method body, near the other repository mocks:

```csharp
        var statusRepository = new Mock<ITaskStatusRepository>();
        statusRepository.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(statuses ?? Array.Empty<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>());
```

And change the final `return new GetProjectTasksQueryHandler(...)` call to append `statusRepository.Object` as the last constructor argument (matching Step 3 below, which adds the new dependency at the end of the handler's own constructor parameter list):

```csharp
        return new GetProjectTasksQueryHandler(
            currentUser.Object, identity.Object, fileStorage.Object, projects.Object, members.Object,
            permissions.Object, taskRepository.Object, assignmentRepository.Object, sessionRepository.Object,
            CalendarEventRepositoryMocks.Empty().Object, statusRepository.Object);
```

- [x] **Step 2: Write the failing tests**

Add these two tests to the same file, after `Handle_PopulatesAssigneeIdentitiesWithResolvedNameAndSignedAvatarUrl`:

```csharp
    [Fact]
    public async Task Handle_ExcludesSubtasksFromTopLevelList()
    {
        var parent = Task(ObjectiveA, "Parent");
        var subtask = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveA, ParentTaskId = parent.Id,
            ShortId = "P-9001", Title = "Child", CategoryId = Guid.NewGuid(), StatusId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, new[] { parent, subtask });

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = Assert.Single(result.Value!);
        Assert.Equal(parent.Id, task.Id);
    }

    [Fact]
    public async Task Handle_ComputesSubtaskProgressCounts()
    {
        var doneStatusId = Guid.NewGuid();
        var openStatusId = Guid.NewGuid();
        var parent = Task(ObjectiveA, "Parent");
        var doneSubtask = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveA, ParentTaskId = parent.Id, ShortId = "P-9002", Title = "Done child", CategoryId = Guid.NewGuid(), StatusId = doneStatusId, CreatedAt = DateTimeOffset.UtcNow };
        var openSubtask = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveA, ParentTaskId = parent.Id, ShortId = "P-9003", Title = "Open child", CategoryId = Guid.NewGuid(), StatusId = openStatusId, CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new List<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus>
        {
            new() { Id = doneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, DisplayOrder = 1 },
            new() { Id = openStatusId, TenantId = TenantId, Name = "To Do", MarksTaskComplete = false, DisplayOrder = 0 }
        };
        var handler = BuildHandler(ActiveProject(), Array.Empty<Guid>(), hasReadPermission: true, new[] { parent, doneSubtask, openSubtask }, statuses: statuses);

        var result = await handler.Handle(new GetProjectTasksQuery(ProjectId), CancellationToken.None);

        var task = Assert.Single(result.Value!);
        Assert.Equal(2, task.SubtaskTotalCount);
        Assert.Equal(1, task.SubtaskCompletedCount);
    }
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectTasksQueryHandlerTests`
Expected: compile error (`GetProjectTasksQueryHandler` doesn't yet accept an `ITaskStatusRepository`, and `WorkTaskResponse` has no `SubtaskTotalCount`/`SubtaskCompletedCount` members to assert on until Task 2 of this plan is done — Task 2 must be completed before this task). Once compiling, the two new tests FAIL against the unmodified handler.

- [x] **Step 4: Update the handler**

In `GetProjectTasksQueryHandler.cs`, add a constructor dependency on `ITaskStatusRepository`:

```csharp
    private readonly ITaskStatusRepository _statuses;
```

Add `ITaskStatusRepository statuses` as the **last** constructor parameter (after `calendarEvents`), assign `_statuses = statuses;` in the body, matching the append-at-the-end order the `BuildHandler` test helper update in Step 1 already assumes. Then change the body: right after the existing

```csharp
        var items = await _tasks.GetByProjectAsync(tenantId, project.Id, ct);
        if (accessibleObjectiveIds is not null)
            items = items.Where(t => accessibleObjectiveIds.Contains(t.ObjectiveId)).ToList();
```

replace it with:

```csharp
        var allItems = await _tasks.GetByProjectAsync(tenantId, project.Id, ct);
        if (accessibleObjectiveIds is not null)
            allItems = allItems.Where(t => accessibleObjectiveIds.Contains(t.ObjectiveId)).ToList();

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var completingStatusIds = statuses.Where(s => s.MarksTaskComplete).Select(s => s.Id).ToHashSet();

        var subtasksByParentId = allItems
            .Where(t => t.ParentTaskId is not null)
            .GroupBy(t => t.ParentTaskId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = allItems.Where(t => t.ParentTaskId is null).ToList();
```

Every subsequent line in the method that reads `items` (the assignment lookups, event links, open sessions, the final `.Select(t => new WorkTaskResponse(...))`) stays unchanged, since `items` is now the top-level-only list. Only the final response projection needs two new named arguments — change:

```csharp
            Attachments: null,
            Assignees: assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>())
                .Select(employeeId => assigneeIdentityByEmployeeId[employeeId]).ToList())).ToList();
```

to:

```csharp
            Attachments: null,
            Assignees: assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>())
                .Select(employeeId => assigneeIdentityByEmployeeId[employeeId]).ToList(),
            ParentTaskId: t.ParentTaskId,
            SubtaskTotalCount: subtasksByParentId.GetValueOrDefault(t.Id, new List<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>()).Count,
            SubtaskCompletedCount: subtasksByParentId.GetValueOrDefault(t.Id, new List<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>()).Count(s => completingStatusIds.Contains(s.StatusId)))).ToList();
```

Add `using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;` if `ITaskStatusRepository` isn't already reachable (it lives in the same namespace as `IWorkTaskRepository`, already imported).

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectTasksQueryHandlerTests`
Expected: PASS, including every pre-existing test in this file (they should be unaffected by the new dependency, since Step 1's `BuildHandler` update defaults `statuses` to an empty list) and the 2 new ones from Step 2.

- [x] **Step 6: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests pass.

- [x] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTasksQueryHandlerTests.cs
git commit -m "feat(work): exclude subtasks from the board query and compute per-parent progress counts"
```

---

## Definition of done

- `dotnet build` and `dotnet test tests/ONEVO.Tests.Unit` are clean.
- `POST api/v1/work/tasks/{parentTaskId}/subtasks` creates a subtask with the reduced field set and optional assignee.
- `GET api/v1/work/tasks/{parentTaskId}/subtasks` returns the parent's direct children.
- `GET api/v1/work/projects/{projectId}/tasks` no longer includes subtasks as top-level rows, and every returned task carries accurate `subtaskTotalCount`/`subtaskCompletedCount`.
- Once this plan is executed and reviewed clean, move this file from `plans/next/` to `plans/finished/<completion-date>/` and update `plans/SUMMARY.md` / `specs/SUMMARY.md` per `docs/superpowers/rules/FILE_CREATION_RULES.md`.
