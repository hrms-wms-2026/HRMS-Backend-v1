# Work Management — Task Edit Requests & Board Structure API (Part 5 of 8) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Depends on the Sprint Foundation plan being complete** (all 4 parts, shipped).

**Goal:** Backend for two independent additions: (1) `TaskEditRequest` — a new approval-request type
letting non-owner Objective members request edits to an existing task, mirroring `TaskCreationRequest`
exactly; (2) `ReorderTaskStatusesCommand` — one atomic bulk-update for the Board Structure tab's
drag-reorder/visibility-toggle/complete-radio form.

**Architecture:** `TaskEditRequest` is a direct structural mirror of `TaskCreationRequest` (same
entity shape, same four commands, same routing/authorization rules) — deliberately, since that
pattern is already proven in this codebase. `ReorderTaskStatusesCommand` is new territory (no
existing bulk-update command in Work Management) but follows the same owner-only +
`ExecuteInTransactionAsync` shape every other command here uses.

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL), MediatR CQRS, FluentValidation, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-17-work-management-sprint-ux-v2-design.md`

## Global Constraints

- Work Management module only.
- `TaskEditRequest`'s response/list DTOs **must carry the requester's resolved display name from the
  first commit** — do not repeat the `requestedByName`-blank gap already found and left unfixed on
  `TaskCreationRequest`/`ObjectiveChangeRequest`/`ObjectiveInvitation`. Resolve it via
  `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`, the same service already used
  elsewhere in this codebase for exactly this purpose.
- `ReorderTaskStatusesCommand` must reject (not silently coerce) any submission where the number of
  rows with `MarksTaskComplete = true` is not exactly 1.
- Every owner-only check follows the exact existing pattern: resolve caller's EmployeeId, compare to
  `objective.OwnerId`, `Result.Forbidden(...)` on mismatch.

---

### Task 1: `TaskEditRequest` entity, migration, repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditRequestConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskEditRequestRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskEditRequestRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: migration via `dotnet ef migrations add AddTaskEditRequests`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskEditRequestConfigurationTests.cs`

**Interfaces:**
- Produces: `TaskEditRequest : BaseEntity { TaskId, RequestedByEmployeeId, PayloadJson, Status, DecidedByEmployeeId?, DecisionComment?, DecidedAt? }`,
  `TaskEditRequestStatuses { Pending, Approved, Rejected, Cancelled }` (same 4 values as `TaskCreationRequestStatuses`),
  `ITaskEditRequestRepository { AddAsync, GetByIdForTenantAsync, GetTrackedByIdForTenantAsync, GetPendingForOwnerEmployeeIdAsync, Update }` — signatures identical to `ITaskCreationRequestRepository`'s, just `TaskEditRequest` in place of `TaskCreationRequest`.

- [ ] **Step 1: Read `TaskCreationRequest.cs`, its EF configuration, and `ITaskCreationRequestRepository`/`EfTaskCreationRequestRepository` in full** (all already exist, all four files) — this task is a structural mirror; match every field, index, and method signature exactly except the entity/table name.

- [ ] **Step 2: Write the failing test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskEditRequestConfigurationTests
{
    [Fact]
    public void TaskEditRequest_DefaultsStatusToPending()
    {
        var request = new TaskEditRequest { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), TaskId = Guid.NewGuid(), RequestedByEmployeeId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };

        Assert.Equal(TaskEditRequestStatuses.Pending, request.Status);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~TaskEditRequestConfigurationTests`
Expected: FAIL to compile.

- [ ] **Step 4: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskEditRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// A non-owner Objective member's request to edit an existing task, decided by the task's Objective
/// owner. Structural mirror of TaskCreationRequest - see that entity's doc comment for the design
/// rationale, which applies identically here.
/// </summary>
public class TaskEditRequest : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = TaskEditRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
```

- [ ] **Step 5: Write the EF configuration, DbContext registration, repository interface + implementation, DI registration**

Mirror `TaskCreationRequestConfiguration.cs` (table name `task_edit_requests`, same index shape but
keyed on `TaskId` instead of `ObjectiveId`), `ApplicationDbContext.TaskEditRequests => Set<TaskEditRequest>()`,
`ITaskEditRequestRepository`/`EfTaskEditRequestRepository` (identical method set to
`ITaskCreationRequestRepository`, except `GetPendingForOwnerEmployeeIdAsync` joins through
`tasks.objective_id → objectives.owner_id` instead of directly against `objective_id`, since
`TaskEditRequest` only has `TaskId`, not `ObjectiveId` — read `EfTaskCreationRequestRepository`'s
implementation of that method first to match its join style exactly, adjusting only the extra hop
through `WorkTasks`). Register in `DependencyInjection.cs` alongside `ITaskCreationRequestRepository`.

- [ ] **Step 6: Generate and apply the migration**

Run: `dotnet ef migrations add AddTaskEditRequests --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`,
rename to `20260817000004_AddTaskEditRequests.cs` for ordering after Sprint Foundation's migrations.
Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`.

- [ ] **Step 7: Run to verify the test passes, then commit**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~TaskEditRequestConfigurationTests`
Expected: PASS.

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditRequestConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskEditRequestRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskEditRequestRepository.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskEditRequestConfigurationTests.cs
git commit -m "feat(work): TaskEditRequest entity + repository, mirrors TaskCreationRequest"
```

---

### Task 2: `TaskEditRequestPayload` + `CreateTaskEditRequestCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskEditRequestPayload.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/CreateTaskEditRequestCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskEditRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces: `TaskEditRequestPayload(string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints)`
  (note: no `TaskType` — `EditTaskCommand` doesn't allow changing it either, confirmed by reading that
  handler); `TaskEditRequestResponse(Guid Id, Guid TaskId, string Status, TaskEditRequestPayload Payload, string RequestedByName, DateTimeOffset CreatedAt)`
  (carries the resolved name per this plan's Global Constraints — **do not omit it**);
  `CreateTaskEditRequestCommand(Guid TaskId, string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints) : IRequest<Result<TaskEditRequestResponse>>`.

- [ ] **Step 1: Read `CreateTaskCreationRequestCommandHandler.cs` in full** (already read earlier in
  this project's history — re-read now to confirm current state) — this task mirrors its
  authorization logic (caller must be an active Objective member, must not be the owner) exactly, but
  resolves the Objective via `task.ObjectiveId` first since the command only receives a `TaskId`.

- [ ] **Step 2: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskEditRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid OutsiderEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (CreateTaskEditRequestCommandHandler Handler, Mock<ITaskEditRequestRepository> Requests) Build(
        Guid callerEmployeeId, bool callerIsMember, string sprintStatus = SprintStatuses.Active)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [callerEmployeeId] = "Test Member" });

        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, SprintId = SprintId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", Status = sprintStatus, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(callerIsMember);

        var requests = new Mock<ITaskEditRequestRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskEditRequestResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskEditRequestResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateTaskEditRequestCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, sprints.Object, membership.Object, requests.Object, unitOfWork.Object);
        return (handler, requests);
    }

    [Fact]
    public async Task Handle_ActiveMember_CreatesRequestWithResolvedName()
    {
        var (handler, requests) = Build(MemberEmployeeId, callerIsMember: true);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Member", result.Value!.RequestedByName);
        requests.Verify(x => x.AddAsync(It.Is<TaskEditRequest>(r => r.TaskId == TaskId && r.RequestedByEmployeeId == MemberEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Owner_ReturnsFailure_NoRequestNeeded()
    {
        var (handler, requests) = Build(OwnerEmployeeId, callerIsMember: true);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        requests.Verify(x => x.AddAsync(It.IsAny<TaskEditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotAMember_ReturnsForbidden()
    {
        var (handler, requests) = Build(OutsiderEmployeeId, callerIsMember: false);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_SprintAchieved_ReturnsForbidden()
    {
        var (handler, requests) = Build(MemberEmployeeId, callerIsMember: true, sprintStatus: SprintStatuses.Achieved);
        var command = new CreateTaskEditRequestCommand(TaskId, "New title", null, "high", null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        requests.Verify(x => x.AddAsync(It.IsAny<TaskEditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskEditRequestCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 4: Write the payload, command, validator**

```csharp
// TaskEditRequestPayload.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskEditRequestPayload(
    string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);
```

```csharp
// CreateTaskEditRequestCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public sealed record CreateTaskEditRequestCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<TaskEditRequestResponse>>;
```

```csharp
// CreateTaskEditRequestCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public class CreateTaskEditRequestCommandValidator : AbstractValidator<CreateTaskEditRequestCommand>
{
    public CreateTaskEditRequestCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEqual(Guid.Empty);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Priority).Must(p => p is WorkTaskPriorities.Low or WorkTaskPriorities.Medium or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
        RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue);
    }
}
```

Add `TaskEditRequestResponse(Guid Id, Guid TaskId, string Status, TaskEditRequestPayload Payload, string RequestedByName, DateTimeOffset CreatedAt)`
to `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`, alongside
the other response records already in that file.

- [ ] **Step 5: Write the handler**

```csharp
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public class CreateTaskEditRequestCommandHandler : IRequestHandler<CreateTaskEditRequestCommand, Result<TaskEditRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskEditRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        IObjectiveRepository objectives, ISprintRepository sprints, IMilestoneMembershipCoordinator membership,
        ITaskEditRequestRepository requests, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _objectives = objectives;
        _sprints = sprints;
        _membership = membership;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskEditRequestResponse>> Handle(CreateTaskEditRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskEditRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskEditRequestResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<TaskEditRequestResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskEditRequestResponse>.NotFound("Objective not found.");

        if (objective.OwnerId == callerEmployeeId.Value)
            return Result<TaskEditRequestResponse>.Failure("The milestone owner edits tasks directly - no request needed.", 400);

        var isMember = await _membership.IsActiveMemberAsync(tenantId, objective.Id, callerEmployeeId.Value, ct);
        if (!isMember)
            return Result<TaskEditRequestResponse>.Forbidden("Only active milestone members can request task edits.");

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result<TaskEditRequestResponse>.Forbidden("This task's sprint has been achieved and is now frozen.");
        }

        var payload = new TaskEditRequestPayload(request.Title.Trim(), request.Description?.Trim(), request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, [callerEmployeeId.Value], ct);
        var requesterDisplayName = names.GetValueOrDefault(callerEmployeeId.Value) ?? "A teammate";

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var entity = new TaskEditRequest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                RequestedByEmployeeId = callerEmployeeId.Value, PayloadJson = JsonSerializer.Serialize(payload),
                Status = TaskEditRequestStatuses.Pending, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _requests.AddAsync(entity, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskEditRequestResponse>.Success(
                new TaskEditRequestResponse(entity.Id, entity.TaskId, entity.Status, payload, requesterDisplayName, entity.CreatedAt));
        }, ct);
    }
}
```

Note: this handler does **not** send a notification on create — add one, following the exact pattern
`CreateTaskCreationRequestCommandHandler` already uses (`_notifications.SendTemplatedAsync(...,
"work_task_edit_request_created", ...)` to the objective owner) if you want parity; this plan treats
it as optional polish, not a blocking requirement, since notifications weren't explicitly called out
for this feature in the design conversation. If added, seed the new template code the same way Sprint
Foundation Part 2 Task 11 added its three template codes (per-template existence check, not the
all-or-nothing gate that was already fixed there).

- [ ] **Step 6: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskEditRequestCommandHandlerTests`
Expected: PASS (all 4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/TaskEditRequestPayload.cs src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskEditRequest/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskEditRequestCommandHandlerTests.cs
git commit -m "feat(work): CreateTaskEditRequestCommand - non-owner members can request task edits"
```

---

### Task 3: `ApproveTaskEditRequestCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces: `ApproveTaskEditRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>`.

- [ ] **Step 1: Write the failing test**

Follow `ApproveTaskCreationRequestCommandHandlerTests.cs`'s exact fixture style (read it first) but
simplified: no `Sprint`/`Project`/task-number-increment concerns here, since this approves an edit to
an **existing** task, not creating a new one. Cover: happy path (owner approves, task fields updated,
`EstimatedHours` slack-checked via `IObjectiveAllocationSlackCalculator` the same way `EditTaskCommandHandler`
already does), not-owner (403), already-decided (409), sprint-achieved-since-request-was-raised (403,
defense in depth — re-check at approval time even though it was also checked at creation time, since
the sprint could have been achieved in between).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ApproveTaskEditRequestCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the command and handler**

```csharp
// ApproveTaskEditRequestCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskEditRequest;

public sealed record ApproveTaskEditRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>;
```

```csharp
// ApproveTaskEditRequestCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskEditRequest;

public class ApproveTaskEditRequestCommandHandler : IRequestHandler<ApproveTaskEditRequestCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditRequestRepository _requests;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveTaskEditRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskEditRequestRepository requests,
        IWorkTaskRepository tasks, IObjectiveRepository objectives, ISprintRepository sprints,
        IObjectiveAllocationSlackCalculator slack, IMilestoneMembershipCoordinator membership,
        INotificationDispatcher notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _tasks = tasks;
        _objectives = objectives;
        _sprints = sprints;
        _slack = slack;
        _membership = membership;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(ApproveTaskEditRequestCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetTrackedByIdForTenantAsync(tenantId, request.RequestId, ct);
        if (pending is null)
            return Result<WorkTaskResponse>.NotFound("Request not found.");

        if (pending.Status != TaskEditRequestStatuses.Pending)
            return Result<WorkTaskResponse>.Conflict("This request has already been decided.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, pending.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can decide this request.");

        if (task.SprintId.HasValue)
        {
            var sprint = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
            if (sprint is not null && sprint.Status == SprintStatuses.Achieved)
                return Result<WorkTaskResponse>.Conflict("This task's sprint has been achieved and is now frozen.");
        }

        var payload = JsonSerializer.Deserialize<TaskEditRequestPayload>(pending.PayloadJson)!;

        if (payload.EstimatedHours.HasValue && payload.EstimatedHours.Value != task.EstimatedHours)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, excludingTaskId: task.Id, ct: ct);
            if (payload.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    InsufficientAllocationResponseJson.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            task.Title = payload.Title;
            task.Description = payload.Description;
            task.Priority = payload.Priority;
            task.DueDate = payload.DueDate;
            task.EstimatedHours = payload.EstimatedHours;
            task.StoryPoints = payload.StoryPoints;
            task.UpdatedAt = now;

            pending.Status = TaskEditRequestStatuses.Approved;
            pending.DecidedByEmployeeId = callerEmployeeId.Value;
            pending.DecidedAt = now;
            pending.UpdatedAt = now;
            _requests.Update(pending);

            var requester = await _membership.GetActiveAssigneeAsync(tenantId, pending.RequestedByEmployeeId, innerCt);
            if (requester is not null)
            {
                await _notifications.SendTemplatedAsync(
                    tenantId, requester.UserId, "work_task_edit_request_decided",
                    new Dictionary<string, string> { ["decision"] = "approved", ["taskTitle"] = task.Title, ["objectiveName"] = objective.Title },
                    "task_edit_request", pending.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.TaskType, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId));
        }, ct);
    }
}
```

This introduces the `"work_task_edit_request_decided"` notification template — seed it in
`NotificationTemplateSeeder.cs` using the already-fixed per-template check (Sprint Foundation Part 2
Task 11), alongside a `"work_task_edit_request_created"` one if Task 2's optional create-notification
was added.

- [ ] **Step 4: Run to verify all pass, then commit**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ApproveTaskEditRequestCommandHandlerTests`
Expected: PASS.

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ApproveTaskEditRequest/ src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ApproveTaskEditRequestCommandHandlerTests.cs
git commit -m "feat(work): ApproveTaskEditRequestCommand - applies the requested edit to the task"
```

---

### Task 4: `RejectTaskEditRequestCommand`, `CancelTaskEditRequestCommand`, `GetMyTaskEditRequestsQuery`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskEditRequest/` (2 files)
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CancelTaskEditRequest/` (2 files)
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskEditRequests/` (2 files)
- Test: 3 corresponding test files

**These three are direct mirrors of their `TaskCreationRequest` counterparts** — read
`RejectTaskCreationRequestCommandHandler.cs`, `CancelTaskCreationRequestCommandHandler.cs`, and
`GetMyTaskCreationRequestsQueryHandler.cs` in full (already read once during this project's history;
re-read now), then reproduce each exactly with `TaskEditRequest`/`ITaskEditRequestRepository`/
`TaskEditRequestStatuses` in place of the Creation-request equivalents. The one behavioral difference:
`GetMyTaskEditRequestsQueryHandler`'s list response must include `RequestedByName` (per this plan's
Global Constraints), resolved via `ResolveDisplayNamesByEmployeeIdAsync` for the whole batch in one
call (not looped per-row) — read `GetMyTaskCreationRequestsQueryHandler.cs` specifically to see
whether it already batches this correctly or has the same gap `GetMyTaskCreationRequestsQueryHandler`
itself has (if it doesn't resolve names either, don't copy that gap forward — resolve it here even
though fixing the original is out of this plan's scope).

- [ ] **Step 1: Write failing tests for all three** (Reject: owner-only, sets Rejected + comment,
  notifies; Cancel: requester-only, sets Cancelled; GetMy: returns the caller's own pending requests
  with `RequestedByName` populated).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement all three, mirroring the read reference files exactly** (plus the
  batched-name-resolution fix for the query).
- [ ] **Step 4: Run to verify all pass.**
- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RejectTaskEditRequest/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CancelTaskEditRequest/ src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetMyTaskEditRequests/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/RejectTaskEditRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CancelTaskEditRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetMyTaskEditRequestsQueryHandlerTests.cs
git commit -m "feat(work): Reject/Cancel TaskEditRequest + GetMyTaskEditRequests with resolved requester names"
```

---

### Task 5: `ReorderTaskStatusesCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ReorderTaskStatusesCommandHandlerTests.cs`

**Interfaces:**
- Produces: `TaskStatusOrderUpdate(Guid StatusId, int DisplayOrder, string Visibility, bool MarksTaskComplete)`,
  `ReorderTaskStatusesCommand(Guid ObjectiveId, List<TaskStatusOrderUpdate> Updates) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ReorderTaskStatusesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid Status1 = Guid.NewGuid();
    private static readonly Guid Status2 = Guid.NewGuid();

    private (ReorderTaskStatusesCommandHandler Handler, List<TaskStatusEntity> Statuses) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var statusList = new List<TaskStatusEntity>
        {
            new() { Id = Status1, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, MarksTaskComplete = false, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status2, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "Done", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Private, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(statusList);
        foreach (var s in statusList)
            statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ReorderTaskStatusesCommandHandler(currentUser.Object, identity.Object, objectives.Object, statuses.Object, unitOfWork.Object);
        return (handler, statusList);
    }

    [Fact]
    public async Task Handle_ExactlyOneComplete_AppliesAllUpdates()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, DisplayOrder: 1, TaskStatusVisibilities.Public, MarksTaskComplete: false),
            new(Status2, DisplayOrder: 0, TaskStatusVisibilities.Public, MarksTaskComplete: true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
        Assert.Equal(0, statuses.Single(s => s.Id == Status2).DisplayOrder);
        Assert.Equal(TaskStatusVisibilities.Public, statuses.Single(s => s.Id == Status2).Visibility);
    }

    [Fact]
    public async Task Handle_ZeroCompleteStatuses_ReturnsFailure()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, MarksTaskComplete: false),
            new(Status2, 1, TaskStatusVisibilities.Public, MarksTaskComplete: false)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TwoCompleteStatuses_ReturnsFailure()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, MarksTaskComplete: true),
            new(Status2, 1, TaskStatusVisibilities.Public, MarksTaskComplete: true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, false), new(Status2, 1, TaskStatusVisibilities.Public, true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ReorderTaskStatusesCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the command, validator, handler**

```csharp
// ReorderTaskStatusesCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public sealed record TaskStatusOrderUpdate(Guid StatusId, int DisplayOrder, string Visibility, bool MarksTaskComplete);

public sealed record ReorderTaskStatusesCommand(Guid ObjectiveId, List<TaskStatusOrderUpdate> Updates) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
```

```csharp
// ReorderTaskStatusesCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandValidator : AbstractValidator<ReorderTaskStatusesCommand>
{
    public ReorderTaskStatusesCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty);
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).ChildRules(update =>
        {
            update.RuleFor(u => u.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private);
            update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.Updates).Must(updates => updates.Count(u => u.MarksTaskComplete) == 1)
            .WithMessage("Exactly one status must be marked as the complete status.");
    }
}
```

```csharp
// ReorderTaskStatusesCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandHandler : IRequestHandler<ReorderTaskStatusesCommand, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public ReorderTaskStatusesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(ReorderTaskStatusesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Only this milestone's owner can restructure the board.");

        // Defense in depth beyond the validator (which runs in the MediatR pipeline in production,
        // but not when a test calls Handle directly) - exactly one complete status, always.
        if (request.Updates.Count(u => u.MarksTaskComplete) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Exactly one status must be marked as the complete status.", 422);

        var existing = await _statuses.GetByObjectiveIdAsync(tenantId, objective.Id, ct);
        var byId = existing.ToDictionary(s => s.Id);

        foreach (var update in request.Updates)
        {
            if (!byId.TryGetValue(update.StatusId, out var status))
                return Result<IReadOnlyList<TaskStatusResponse>>.NotFound($"Status {update.StatusId} not found on this milestone.");

            status.DisplayOrder = update.DisplayOrder;
            status.Visibility = update.Visibility;
            status.MarksTaskComplete = update.MarksTaskComplete;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var status in existing.Where(s => request.Updates.Any(u => u.StatusId == s.Id)))
                _statuses.Update(status);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskStatusResponse>>.Success(
                existing.OrderBy(s => s.DisplayOrder)
                    .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId, s.MarksTaskComplete, s.Visibility))
                    .ToList());
        }, ct);
    }
}
```

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ReorderTaskStatusesCommandHandlerTests`
Expected: PASS (all 4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ReorderTaskStatusesCommandHandlerTests.cs
git commit -m "feat(work): ReorderTaskStatusesCommand - atomic drag-reorder/visibility/complete-flag bulk update"
```

---

### Task 6: Controller wiring

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

- [ ] **Step 1: Add contracts**

```csharp
public sealed record CreateTaskEditRequestRequest(string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);
public sealed record RejectTaskEditRequestRequest(string Comment);
public sealed record TaskStatusOrderUpdateRequest(Guid StatusId, int DisplayOrder, string Visibility, bool MarksTaskComplete);
public sealed record ReorderTaskStatusesRequest(List<TaskStatusOrderUpdateRequest> Updates);
```

Add view-model + mapper for `TaskEditRequestResponse` following `TaskCreationRequestViewModel`'s exact
pattern (Id, TaskId, Status, Payload, RequestedByName, CreatedAt).

- [ ] **Step 2: Add controller actions**, mirroring the existing task-creation-request routes exactly
  but under `tasks/{taskId}/edit-requests` / `task-edit-requests/{id}/...`:

```csharp
    [HttpPost("tasks/{taskId:guid}/edit-requests")]
    public async Task<IActionResult> CreateEditRequest(Guid taskId, [FromBody] CreateTaskEditRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskEditRequestCommand(
            taskId, request.Title, request.Description, request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

        return result.IsSuccess ? StatusCode(202, result.Value!.ToViewModel()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-edit-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveEditRequest(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ApproveTaskEditRequestCommand(id), ct);
        return result.IsSuccess ? Ok(result.Value!.ToViewModel()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-edit-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectEditRequest(Guid id, [FromBody] RejectTaskEditRequestRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new RejectTaskEditRequestCommand(id, request.Comment), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("task-edit-requests/{id:guid}/cancel")]
    public async Task<IActionResult> CancelEditRequest(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelTaskEditRequestCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("task-edit-requests/mine")]
    public async Task<IActionResult> MyEditRequests(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyTaskEditRequestsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value!.Select(r => r.ToViewModel()).ToList()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("objectives/{objectiveId:guid}/task-statuses/reorder")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ReorderStatuses(Guid objectiveId, [FromBody] ReorderTaskStatusesRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReorderTaskStatusesCommand(
            objectiveId, request.Updates.Select(u => new TaskStatusOrderUpdate(u.StatusId, u.DisplayOrder, u.Visibility, u.MarksTaskComplete)).ToList()), ct);

        return result.IsSuccess ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList()) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the needed `using` statements for the new command namespaces.

- [ ] **Step 3: Manual verification**

Run: `dotnet build src/ONEVO.Api` then `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: builds clean, all tests PASS — this is Part 5's full completion gate.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
git commit -m "feat(work): wire TaskEditRequest CRUD + ReorderTaskStatuses endpoints"
```

---

## End of Part 5

Part 5 done when all 6 tasks are committed and the full WorkManagement-scoped unit suite is green.
Unblocks Part 6 (frontend employee directory service — no backend dependency, could run in parallel)
and Part 7 (frontend task-detail popup + Board Structure tab UI, which directly consume this part's
endpoints).
