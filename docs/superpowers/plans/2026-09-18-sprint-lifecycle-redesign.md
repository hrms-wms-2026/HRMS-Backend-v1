# Sprint Lifecycle Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Backlog page's sprint status dropdown with a guided lifecycle — Create (instant, dateless Draft) → Start (commits dates + goal, becomes Active) → Complete (disposes of unfinished tasks, becomes Complete) — across both the `HRMS-Backend-v1` and `Hrms--Web-application---front-end---v1` repos.

**Architecture:** Backend: `Sprint.StartDate/EndDate` become nullable, `Goal` is added, `SprintStatuses` shrinks to `Draft/Active/Complete/Achieved`, `SetSprintStatusCommand` and the old date-driven `SprintLifecycleJob` are deleted, and `CompleteSprintCommand` gains a task-disposition parameter that replaces its old all-tasks-complete gate. Frontend: three new/rewritten dialogs (`SprintStartDialogComponent`, `SprintCompleteDialogComponent`, simplified `SprintFormComponent`) replace the status dropdown in `SprintTabComponent`, wired from `TaskBacklogComponent`; the Tree view (`milestone-tree-tab`) reuses the same Complete dialog rather than being redesigned.

**Tech Stack:** ASP.NET Core / MediatR / EF Core (Npgsql) on the backend; Angular 21 standalone components + `@ngrx/signals` stores, Vitest, on the frontend.

**Spec:** [`docs/superpowers/specs/2026-09-18-sprint-lifecycle-redesign-design.md`](../specs/2026-09-18-sprint-lifecycle-redesign-design.md) — read it alongside this plan; this plan does not repeat its rationale, only the concrete steps.

## Global Constraints

- Every sprint-mutating handler authorizes via `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync(tenantId, objectiveId, callerEmployeeId, ct)` — copy this exact call, not a direct `OwnerId` comparison (existing handlers already establish this precedent).
- Every write transaction goes through `IUnitOfWork.ExecuteInTransactionAsync(...)` + `SaveChangesAsync`, matching every existing Sprint command handler.
- Backend tests: xUnit + Moq, plain `Assert.*` (no FluentAssertions used anywhere in this codebase's Sprint tests).
- Frontend tests: Vitest (`vi.fn()`, `vi.spyOn()`), Angular `TestBed` + `componentRef.setInput(...)`, mirroring `sprint-form.component.spec.ts`.
- Notifications go through `INotificationDispatcher.SendTemplatedAsync(tenantId, userId, templateKey, placeholders, entityType, entityId, ct)` — existing pattern, no new dispatch mechanism.
- No time-tracking/"logged hours" field exists anywhere in either repo — do not invent one. Hours displayed anywhere are `WorkTask.estimatedHours` summed client-side.
- Run backend tests with `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement.Sprints"` (narrow further per task) from the `HRMS-Backend-v1` repo root. Run frontend tests with `npx vitest run <path>` from the `Hrms--Web-application---front-end---v1` repo root.
- Both repos are already on branch `feature/sprint-lifecycle-redesign` (backend off `origin/development`, frontend off `origin/Development`) — commit directly to it, do not create further branches.
- **This working tree is shared with other concurrent sessions.** Before every commit, run `git status` fresh in the relevant repo and confirm only files this task touched are staged — do not blindly `git add -A`.

---

## Part A — Backend (`HRMS-Backend-v1`)

### Task 1: Sprint domain foundation — entity, statuses, migration, response shape, delete the dropdown mechanism

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetProjectSprints/GetProjectSprintsQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQueryHandler.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs:76`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:568` (comment out the `SprintLifecycleJob` hosted-service registration — Task 6 restores it)
- Delete: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/SetSprintStatus/SetSprintStatusCommand.cs`
- Delete: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/SetSprintStatus/SetSprintStatusCommandHandler.cs`
- Delete: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SetSprintStatusCommandHandlerTests.cs`
- Delete: `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs`
- Delete: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs`
- Delete: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobAdminModeTests.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (remove the `SetStatus` action + its `using`)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/AchieveObjectiveCommandHandlerTests.cs`
- Create migration: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddSprintLifecycleRedesign.cs` (generated)

**Interfaces:**
- Produces: `SprintStatuses.Draft`, `.Active`, `.Complete`, `.Achieved` (string constants); `Sprint.Goal` (`string?`), `Sprint.OverdueNotifiedAt` (`DateTimeOffset?`), `Sprint.StartDate`/`EndDate` now `DateOnly?`; `SprintResponse(Guid Id, Guid ObjectiveId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate, string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt)`; `SprintViewModel` mirrors it. These are consumed by every later task in Part A.

- [ ] **Step 1: Update the `Sprint` entity and `SprintStatuses`**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Achieved = "achieved";
}

/// <summary>
/// A time-boxed iteration owned by one Objective. Achieved is a status value, not a use of
/// BaseEntity.IsDeleted - an Achieved sprint must stay visible to the owner's "all sprints" Backlog
/// view and to the Objective-achieve gate check (see AchieveObjectiveCommandHandler), both of which
/// would silently break under the standard !IsDeleted repository filter convention.
/// </summary>
public class Sprint : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }

    /// <summary>Null while Draft - only StartSprintCommand ever sets these, once, moving the
    /// sprint straight to Active. There is no dateless-but-scheduled holding state.</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public string Status { get; set; } = SprintStatuses.Draft;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }

    /// <summary>Set once by SprintLifecycleJob's overdue sweep so the notification fires exactly
    /// once per sprint instead of every 5-minute tick. Never cleared.</summary>
    public DateTimeOffset? OverdueNotifiedAt { get; set; }
}
```

- [ ] **Step 2: Update `SprintResponse` and `SprintContracts`**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintResponse(
    Guid Id, Guid ObjectiveId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);
```

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs
namespace ONEVO.Api.Contracts.WorkManagement.Sprints;

public sealed record CreateSprintRequest(string Name, string? Goal);
public sealed record StartSprintRequest(DateOnly StartDate, DateOnly EndDate, string? Goal);
public sealed record EditSprintRequest(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);
public sealed record CompleteSprintRequest(string Disposition, Guid? TargetSprintId);

public sealed record SprintViewModel(
    Guid Id, Guid ObjectiveId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt);

public static class SprintViewModelMapper
{
    public static SprintViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintResponse dto) =>
        new(dto.Id, dto.ObjectiveId, dto.Name, dto.Goal, dto.StartDate, dto.EndDate, dto.Status, dto.CompletedAt, dto.AchievedAt);
}
```

- [ ] **Step 3: Fix the two query handlers' `new SprintResponse(...)` construction**

In `GetProjectSprintsQueryHandler.cs`, replace the final `return`:

```csharp
        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => new SprintResponse(
                s.Id, s.ObjectiveId, s.Name, s.Goal, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt)).ToList());
```

In `GetObjectiveSprintsQueryHandler.cs`, replace its equivalent single-line construction the same way (add `s.Goal` as the fourth positional argument).

- [ ] **Step 4: Drop the SetSprintStatus feature slice and the old lifecycle job**

Delete the four files listed above. In `SprintsController.cs`, remove the `SetStatus` action (`PATCH sprints/{id}/status`) and its now-unused `using ...Commands.SetSprintStatus;` line.

In `DependencyInjection.cs:568`, comment out (don't delete — Task 6 uncomments and updates it):

```csharp
        // services.AddHostedService<Services.WorkManagement.SprintLifecycleJob>(); // restored in Task 6, rewritten
```

- [ ] **Step 5: Fix `EfSprintRepository.GetByStatusAsync`** (drops the `IsManuallyOverridden` filter, since the field no longer exists)

```csharp
    public async Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default)
        => await _db.Sprints.Where(s => s.Status == status).ToListAsync(ct);
```

- [ ] **Step 6: Fix the Objective-achieve gate**

In `AchieveObjectiveCommandHandler.cs:76`, replace:

```csharp
        if (sprints.Any(s => s.Status is not (SprintStatuses.Complete or SprintStatuses.Achieved)))
```

with:

```csharp
        // A Draft sprint has no work committed yet and shouldn't permanently block achieving the
        // Objective - only an Active sprint (real, committed, unfinished work) blocks it.
        if (sprints.Any(s => s.Status is SprintStatuses.Active))
```

- [ ] **Step 7: Update `AchieveObjectiveCommandHandlerTests.cs`**

Add a new test alongside the existing `Handle_SprintNeitherCompleteNorAchieved_ReturnsFailure` (leave that one as-is — it already uses `SprintStatuses.Active`, so it still exercises the new gate correctly):

```csharp
    [Fact]
    public async Task Handle_OnlyDraftSprints_Succeeds()
    {
        var (handler, objectives, _, _) = BuildHandler(SubObjective(createdById: HeadUserId),
            sprints: new List<Sprint> { SprintOnObjective(SprintStatuses.Draft) });

        var result = await handler.Handle(new AchieveObjectiveCommand(SubObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
```

(Match the exact command type/constructor and `BuildHandler`/`SprintOnObjective`/`SubObjective` signatures already used by the surrounding tests in that file — read them first, this snippet assumes their existing shape.)

- [ ] **Step 8: Generate and hand-edit the migration**

Run from `src/ONEVO.Infrastructure`:

```bash
dotnet ef migrations add AddSprintLifecycleRedesign
```

Open the generated `<timestamp>_AddSprintLifecycleRedesign.cs`. As the **first line** of `Up(migrationBuilder)`, insert the data backfill (must run before any status-column changes, and works regardless of whether EF's diff also emits a type/nullability change on the column):

```csharp
            migrationBuilder.Sql(
                "UPDATE sprints SET status = 'active' WHERE status IN ('future', 'incomplete');");
```

Leave the rest of the generated `Up()`/`Down()` (the `AlterColumn` calls for nullable `start_date`/`end_date`, `AddColumn` for `goal`/`overdue_notified_at`, `DropColumn` for `is_manually_overridden`) as EF generated them — do not hand-edit those unless `dotnet build` reports a mismatch.

- [ ] **Step 9: Verify the solution builds and the touched tests pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement.Sprints|FullyQualifiedName~AchieveObjectiveCommandHandlerTests"
```

Expected: build succeeds; all Sprint-namespace tests and `AchieveObjectiveCommandHandlerTests` pass (the deleted files' tests are gone, not failing).

- [ ] **Step 10: Commit**

```bash
git add -A
git status
git commit -m "$(cat <<'EOF'
Rework Sprint domain model for the lifecycle redesign

Sprint.StartDate/EndDate become nullable, Goal is added, and
SprintStatuses drops Future/Incomplete in favor of a plain
Draft/Active/Complete/Achieved set. Deletes the free-form status
dropdown (SetSprintStatusCommand) and the old date-driven
SprintLifecycleJob - both superseded by explicit Start/Complete
commands in the following tasks. Objective-achieve gate now excludes
Draft sprints.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `CreateSprintCommand` — dateless, always Draft, with Goal

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Create` action)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `SprintStatuses.Draft` (Task 1), `SprintResponse` 9-arg shape (Task 1).
- Produces: `CreateSprintCommand(Guid ObjectiveId, string Name, string? Goal)` — consumed by the frontend's create call (Task 7) and by nothing else in this repo.

- [ ] **Step 1: Update the failing tests first**

Replace `CreateSprintCommandHandlerTests.cs` in full:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CreateSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateSprintCommandHandler Handler, Mock<ISprintRepository> Sprints) Build(Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var sprints = new Mock<ISprintRepository>();

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == callerEmployeeId));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object, membership.Object);
        return (handler, sprints);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesDraftSprintWithNoDates()
    {
        var (handler, sprints) = Build(OwnerEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", "Ship the thing");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Draft, result.Value!.Status);
        Assert.Null(result.Value!.StartDate);
        Assert.Null(result.Value!.EndDate);
        Assert.Equal("Ship the thing", result.Value!.Goal);
        sprints.Verify(x => x.AddAsync(
            It.Is<Sprint>(s => s.Status == SprintStatuses.Draft && s.StartDate == null && s.EndDate == null && s.Goal == "Ship the thing"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoGoal_CreatesWithNullGoal()
    {
        var (handler, sprints) = Build(OwnerEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Goal);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprints) = Build(OtherEmployeeId);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_CreatesSprint()
    {
        var (handler, sprints) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateSprintCommand(ObjectiveId, "Sprint 1", null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        sprints.Verify(x => x.AddAsync(It.IsAny<Sprint>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail** (compile error — old constructor shape)

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateSprintCommandHandlerTests"
```

Expected: FAIL to build (`CreateSprintCommand` doesn't have this constructor yet).

- [ ] **Step 3: Update the command and handler**

```csharp
// CreateSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

public sealed record CreateSprintCommand(Guid ObjectiveId, string Name, string? Goal) : IRequest<Result<SprintResponse>>;
```

In `CreateSprintCommandHandler.cs`, replace the body of `Handle` from the date-validation line onward:

```csharp
    public async Task<Result<SprintResponse>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can create sprints.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var sprint = new Sprint
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                Name = request.Name.Trim(), Goal = request.Goal?.Trim(),
                Status = SprintStatuses.Draft, CreatedById = _currentUser.UserId, CreatedAt = DateTimeOffset.UtcNow
            };

            await _sprints.AddAsync(sprint, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate,
                sprint.Status, sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
```

- [ ] **Step 4: Update the controller**

```csharp
    [HttpPost("objectives/{objectiveId:guid}/sprints")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSprintCommand(objectiveId, request.Name, request.Goal), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateSprintCommandHandlerTests"
```

Expected: PASS, all 4 tests green.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs
git commit -m "$(cat <<'EOF'
Make sprint creation instant and dateless

Create now always produces a Draft sprint with no dates - the caller
supplies only a name and an optional goal. Dates are committed later
via StartSprintCommand.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: New `StartSprintCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/StartSprint/StartSprintCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/StartSprint/StartSprintCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/StartSprintCommandHandlerTests.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (new `Start` action)

**Interfaces:**
- Consumes: `SprintStatuses.Draft`/`.Active` (Task 1), `SprintResponse` (Task 1), `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync` (existing).
- Produces: `StartSprintCommand(Guid SprintId, DateOnly StartDate, DateOnly EndDate, string? Goal)`, `POST sprints/{id}/start` — consumed by the frontend's `SprintStartDialogComponent` (Task 9).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/StartSprintCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class StartSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (StartSprintCommandHandler Handler, Sprint Sprint) Build(string startingStatus, Guid? callerEmployeeId = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", Goal = "Old goal",
            Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective.OwnerId == resolvedCallerEmployeeId);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new StartSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object, membership.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_DraftSprint_BecomesActiveWithDates()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), "New goal");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
        Assert.Equal(new DateOnly(2026, 9, 21), sprint.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 2), sprint.EndDate);
        Assert.Equal("New goal", sprint.Goal);
    }

    [Fact]
    public async Task Handle_NoGoalProvided_KeepsExistingGoal()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Old goal", sprint.Goal);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 21), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SprintStatuses.Draft, sprint.Status);
    }

    [Fact]
    public async Task Handle_SprintNotDraft_ReturnsConflict()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft, callerEmployeeId: OtherEmployeeId);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~StartSprintCommandHandlerTests"
```

Expected: FAIL to build (`StartSprintCommand`/`StartSprintCommandHandler` don't exist yet).

- [ ] **Step 3: Implement**

```csharp
// StartSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;

public sealed record StartSprintCommand(Guid SprintId, DateOnly StartDate, DateOnly EndDate, string? Goal) : IRequest<Result<SprintResponse>>;
```

```csharp
// StartSprintCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;

/// <summary>Draft -> Active. The one point where a sprint's dates are ever set - there is no
/// date-driven auto-advance anymore (see SprintLifecycleJob).</summary>
public class StartSprintCommandHandler : IRequestHandler<StartSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public StartSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ISprintRepository sprints, IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _sprints = sprints;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<SprintResponse>> Handle(StartSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can start sprints.");

        if (sprint.Status != SprintStatuses.Draft)
            return Result<SprintResponse>.Conflict("Only a Draft sprint can be started.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.StartDate = request.StartDate;
            sprint.EndDate = request.EndDate;
            if (request.Goal is not null) sprint.Goal = request.Goal.Trim();
            sprint.Status = SprintStatuses.Active;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate,
                sprint.Status, sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
}
```

Add to `SprintsController.cs` (alongside the `using`s, add `using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;`):

```csharp
    [HttpPost("sprints/{id:guid}/start")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Start(Guid id, [FromBody] StartSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new StartSprintCommand(id, request.StartDate, request.EndDate, request.Goal), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~StartSprintCommandHandlerTests"
```

Expected: PASS, all 5 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/StartSprint tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/StartSprintCommandHandlerTests.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs
git commit -m "$(cat <<'EOF'
Add StartSprintCommand

The Draft -> Active transition: owner commits start/end dates (and
optionally updates the goal) in one action, replacing the old
date-driven auto-advance.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `CompleteSprintCommand` — task disposition replaces the all-complete gate

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Complete` action)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetBySprintIdAsync`, `.Update(WorkTask)` (existing); `ITaskStatusRepository.GetByIdForTenantAsync` (existing, already injected).
- Produces: `CompleteSprintCommand(Guid SprintId, string Disposition, Guid? TargetSprintId)` where `Disposition` is `"backlog"` or `"sprint"` — consumed by `SprintCompleteDialogComponent` (Task 10) and by the Tree view's reuse of that same dialog (Task 13).

- [ ] **Step 1: Read the existing `CompleteSprintCommand.cs`** to get its exact current using/namespace block (not reproduced here — open the file) before editing, then write the failing tests:

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Domain.Features.WorkManagement.TaskStatuses.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CompleteSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid TargetSprintId = Guid.NewGuid();
    private static readonly Guid DoneStatusId = Guid.NewGuid();
    private static readonly Guid TodoStatusId = Guid.NewGuid();

    private (CompleteSprintCommandHandler Handler, Sprint Sprint, List<WorkTask> Tasks, Mock<IWorkTaskRepository> TaskRepo) Build(IReadOnlyList<WorkTask> tasks)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, OwnerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((object?)null);

        var taskRepo = new Mock<IWorkTaskRepository>();
        taskRepo.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(tasks);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, DoneStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = DoneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true });
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, TodoStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = TodoStatusId, TenantId = TenantId, Name = "To Do", MarksTaskComplete = false });

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProjectMember>());

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CompleteSprintCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, sprints.Object, taskRepo.Object, statuses.Object,
            members.Object, membership.Object, notifications.Object, unitOfWork.Object);

        return (handler, sprint, tasks.ToList(), taskRepo);
    }

    private static WorkTask MakeTask(Guid statusId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, SprintId = SprintId,
        StatusId = statusId, Title = "Task", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_AllTasksAlreadyComplete_CompletesWithNoDisposition()
    {
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { MakeTask(DoneStatusId) });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.NotNull(sprint.CompletedAt);
        taskRepo.Verify(x => x.Update(It.IsAny<WorkTask>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IncompleteTasksWithBacklogDisposition_ClearsSprintId()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(incomplete.SprintId);
        taskRepo.Verify(x => x.Update(incomplete), Times.Once);
    }

    [Fact]
    public async Task Handle_IncompleteTasksWithSprintDisposition_MovesToTargetSprint()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetSprintId, incomplete.SprintId);
    }

    [Fact]
    public async Task Handle_SprintDispositionWithNoTargetId_ReturnsFailure()
    {
        var (handler, _, _, _) = Build(new List<WorkTask> { MakeTask(TodoStatusId) });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}
```

(If the constructor parameter order or an injected dependency name differs from what's assumed here, read the current `CompleteSprintCommandHandler.cs` constructor first and match it exactly — do not guess.)

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteSprintCommandHandlerTests"
```

Expected: FAIL (wrong `CompleteSprintCommand` constructor arity, and the old handler still 422s on incomplete tasks).

- [ ] **Step 3: Implement**

```csharp
// CompleteSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;

/// <summary>Disposition is "backlog" (clears SprintId on every incomplete task) or "sprint"
/// (moves them to TargetSprintId, required in that case).</summary>
public sealed record CompleteSprintCommand(Guid SprintId, string Disposition, Guid? TargetSprintId) : IRequest<Result<SprintResponse>>;
```

Replace `CompleteSprintCommandHandler.Handle` (keep the constructor/DI fields as they already are, matching what you read in Step 1 — only add `ISprintRepository` lookup for the target sprint if not already injected, which it already is as `_sprints`):

```csharp
    public async Task<Result<SprintResponse>> Handle(CompleteSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        if (request.Disposition is not ("backlog" or "sprint"))
            return Result<SprintResponse>.Failure("Unrecognized disposition.", 422);

        if (request.Disposition == "sprint" && request.TargetSprintId is null)
            return Result<SprintResponse>.Failure("A target sprint is required when moving tasks to another sprint.", 422);

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can complete sprints.");

        if (request.Disposition == "sprint")
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, request.TargetSprintId!.Value, ct);
            if (targetSprint is null || targetSprint.ObjectiveId != objective.Id)
                return Result<SprintResponse>.Failure("Target sprint must belong to the same milestone.", 422);
        }

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var task in tasks)
            {
                var status = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, innerCt);
                if (status is not null && status.MarksTaskComplete) continue;

                task.SprintId = request.Disposition == "sprint" ? request.TargetSprintId : null;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                _tasks.Update(task);
            }

            sprint.Status = SprintStatuses.Complete;
            sprint.CompletedAt = DateTimeOffset.UtcNow;
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            var members = await _members.ListActiveForObjectiveAsync(tenantId, objective.Id, innerCt);
            foreach (var member in members)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, member.EmployeeId, innerCt);
                if (assignee is null) continue;

                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_completed",
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                    "sprint", sprint.Id, innerCt);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate,
                sprint.Status, sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
```

Update `SprintsController.Complete`:

```csharp
    [HttpPost("sprints/{id:guid}/complete")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteSprintCommand(id, request.Disposition, request.TargetSprintId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteSprintCommandHandlerTests"
```

Expected: PASS, all 4 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CompleteSprint tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CompleteSprintCommandHandlerTests.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs
git commit -m "$(cat <<'EOF'
Replace CompleteSprint's all-tasks-complete gate with a disposition

Completing a sprint no longer blocks on unfinished tasks - the owner
now chooses where those tasks go (Backlog or another sprint) as part
of completing, matching the new Complete Sprint dialog.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `EditSprintCommand` — Name/Goal always, dates only while Active

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Edit` action)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/EditSprintCommandHandlerTests.cs`

**Interfaces:**
- Consumes: nothing new beyond Task 1's `SprintResponse`.
- Produces: `EditSprintCommand(Guid SprintId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate)` — consumed by `SprintFormComponent` edit mode (Task 8).

- [ ] **Step 1: Write the failing tests** (mirror the existing `EditSprintCommandHandlerTests.cs` structure — read it first for its exact `Build` helper shape, then add/replace these cases):

```csharp
    [Fact]
    public async Task Handle_DraftSprint_DatesProvided_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DraftSprint_NameAndGoalOnly_Succeeds()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", sprint.Name);
        Assert.Equal("Goal", sprint.Goal);
        Assert.Null(sprint.StartDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_DatesProvided_UpdatesThem()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 20));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 5), sprint.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), sprint.EndDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_EndBeforeStart_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 5));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
```

(Adapt these into the existing test file alongside its existing `Handle_CallerNotObjectiveOwner_...`/`Handle_Complete...Blocked` cases, which stay valid unchanged — just add `null, null` or `Goal` arguments to any pre-existing test's `EditSprintCommand(...)` construction to match the new constructor arity.)

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditSprintCommandHandlerTests"
```

Expected: FAIL to build (constructor arity mismatch).

- [ ] **Step 3: Implement**

```csharp
// EditSprintCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;

public sealed record EditSprintCommand(Guid SprintId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate) : IRequest<Result<SprintResponse>>;
```

Replace `EditSprintCommandHandler.Handle`:

```csharp
    public async Task<Result<SprintResponse>> Handle(EditSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, sprint.ObjectiveId, ct);
        if (objective is null)
            return Result<SprintResponse>.NotFound("Objective not found.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, objective.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only this milestone's owner can edit sprints.");

        if (sprint.Status is SprintStatuses.Complete or SprintStatuses.Achieved)
            return Result<SprintResponse>.Conflict("This sprint has already ended and can no longer be edited.");

        if (sprint.Status == SprintStatuses.Draft && (request.StartDate is not null || request.EndDate is not null))
            return Result<SprintResponse>.Failure("A Draft sprint has no dates yet - start it to set dates.", 422);

        if (sprint.Status == SprintStatuses.Active && request.StartDate is not null && request.EndDate is not null
            && request.EndDate < request.StartDate)
            return Result<SprintResponse>.Failure("End date must not be before start date.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            sprint.Name = request.Name.Trim();
            sprint.Goal = request.Goal?.Trim();
            if (sprint.Status == SprintStatuses.Active && request.StartDate is not null && request.EndDate is not null)
            {
                sprint.StartDate = request.StartDate;
                sprint.EndDate = request.EndDate;
            }
            sprint.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<SprintResponse>.Success(new SprintResponse(
                sprint.Id, sprint.ObjectiveId, sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate,
                sprint.Status, sprint.CompletedAt, sprint.AchievedAt));
        }, ct);
    }
```

Update `SprintsController.Edit`:

```csharp
    [HttpPatch("sprints/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditSprintCommand(id, request.Name, request.Goal, request.StartDate, request.EndDate), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditSprintCommandHandlerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/EditSprint tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/EditSprintCommandHandlerTests.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs
git commit -m "$(cat <<'EOF'
Scope EditSprint's date fields to Active sprints only

A Draft sprint has no dates to edit - only Name/Goal are editable
until Start commits dates. An Active sprint can still have its dates
adjusted via Edit, unchanged from today.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: New `SprintLifecycleJob` — overdue notification only, no status mutation

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:568` (uncomment/restore, same registration)

**Interfaces:**
- Consumes: `ISprintRepository.GetByStatusAsync(SprintStatuses.Active, ct)` (Task 1's fixed version), `IWorkTaskRepository.GetBySprintIdAsync`, `ITaskStatusRepository.GetByIdForTenantAsync`, `INotificationDispatcher.SendTemplatedAsync` (all existing).
- Produces: `SprintLifecycleJob.ShouldNotifyOverdue(DateOnly endDate, DateOnly today, bool allTasksComplete, bool alreadyNotified)` — a pure `static bool`, unit-testable without DI, same precedent as the old `DetermineNextStatus`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs
using ONEVO.Infrastructure.Services.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintLifecycleJobTests
{
    [Fact]
    public void ShouldNotifyOverdue_PastEndDateWithUnfinishedTasksNotYetNotified_ReturnsTrue()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: false, alreadyNotified: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_EndDateNotYetPassed_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 10),
            allTasksComplete: false, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AllTasksComplete_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 15),
            allTasksComplete: true, alreadyNotified: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotifyOverdue_AlreadyNotified_ReturnsFalse()
    {
        var result = SprintLifecycleJob.ShouldNotifyOverdue(
            endDate: new DateOnly(2026, 9, 14), today: new DateOnly(2026, 9, 20),
            allTasksComplete: false, alreadyNotified: true);

        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintLifecycleJobTests"
```

Expected: FAIL to build (`SprintLifecycleJob` was deleted in Task 1 and doesn't exist yet).

- [ ] **Step 3: Implement** (structure mirrors the deleted job — same `BackgroundService`/`PeriodicTimer`/per-tick-DI-scope shape, and the same tenant-major grouping-with-admin-mode pattern for RLS, per the comments already in the old file read during Task 1)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.WorkManagement;

/// <summary>
/// Sends a one-time "sprint overdue" notification when an Active sprint's end date passes with
/// unfinished tasks. Never mutates Sprint.Status - completion is always a manual owner action
/// (CompleteSprintCommand), and there is no more Future/Incomplete auto-advance since Draft sprints
/// have no dates to watch and overdue is a purely computed display state on the frontend.
/// </summary>
public sealed class SprintLifecycleJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<SprintLifecycleJob> _logger;

    public SprintLifecycleJob(IServiceProvider services, ILogger<SprintLifecycleJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SprintLifecycleJob encountered an error.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var sprints = scope.ServiceProvider.GetRequiredService<ISprintRepository>();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
        var statuses = scope.ServiceProvider.GetRequiredService<ITaskStatusRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var objectives = scope.ServiceProvider.GetRequiredService<IObjectiveRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        tenantContext.SetAdminMode();

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var activeSprints = await sprints.GetByStatusAsync(SprintStatuses.Active, ct);

        var notifiedCount = 0;

        foreach (var tenantGroup in activeSprints.GroupBy(s => s.TenantId))
        {
            ct.ThrowIfCancellationRequested();

            var tenantId = tenantGroup.Key;
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null) continue;
            await tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

            var tenantNotified = 0;

            foreach (var sprint in tenantGroup)
            {
                ct.ThrowIfCancellationRequested();
                if (sprint.EndDate is null) continue;

                var sprintTasks = await tasks.GetBySprintIdAsync(sprint.TenantId, sprint.Id, ct);
                var allTasksComplete = sprintTasks.Count > 0;
                foreach (var task in sprintTasks)
                {
                    var status = await statuses.GetByIdForTenantAsync(sprint.TenantId, task.StatusId, ct);
                    if (status is null || !status.MarksTaskComplete) { allTasksComplete = false; break; }
                }

                if (!ShouldNotifyOverdue(sprint.EndDate.Value, today, allTasksComplete, sprint.OverdueNotifiedAt is not null))
                    continue;

                sprint.OverdueNotifiedAt = DateTimeOffset.UtcNow;
                sprints.Update(sprint);
                tenantNotified++;

                var objective = await objectives.GetByIdForTenantAsync(sprint.TenantId, sprint.ObjectiveId, ct);
                if (objective is not null)
                {
                    await notifications.SendTemplatedAsync(
                        sprint.TenantId, tenant.Id, "work_sprint_overdue",
                        new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = objective.Title },
                        "sprint", sprint.Id, ct);
                }
            }

            if (tenantNotified > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("SprintLifecycleJob notified {Count} overdue sprints for tenant {TenantId}.", tenantNotified, tenantId);
            }

            notifiedCount += tenantNotified;
        }
    }

    /// <summary>Pure decision function, unit-tested directly without the BackgroundService/DI machinery.</summary>
    public static bool ShouldNotifyOverdue(DateOnly endDate, DateOnly today, bool allTasksComplete, bool alreadyNotified)
        => today > endDate && !allTasksComplete && !alreadyNotified;
}
```

**Note:** the notification recipient loop above simplifies to the tenant record rather than looking up each active objective member's `UserId` via `IMilestoneMembershipCoordinator`/`IProjectMemberRepository`, unlike `CompleteSprintCommandHandler`. Before finalizing this step, check whether `INotificationDispatcher.SendTemplatedAsync`'s second parameter is genuinely a `UserId` (in which case this job needs the same `IProjectMemberRepository`/`IMilestoneMembershipCoordinator` member-loop `CompleteSprintCommandHandler` uses, injected the same way) — mirror `CompleteSprintCommandHandler`'s notification loop exactly rather than the placeholder `tenant.Id` shown here if so.

In `DependencyInjection.cs:568`, restore:

```csharp
        services.AddHostedService<Services.WorkManagement.SprintLifecycleJob>();
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintLifecycleJobTests"
```

Expected: PASS, all 4 tests green.

- [ ] **Step 5: Run the full Sprints test namespace and the full unit suite once, to catch any cross-task regression before moving to the frontend**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"
```

Expected: PASS, no regressions in Tasks/Objectives/other WorkManagement tests.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "$(cat <<'EOF'
Rebuild SprintLifecycleJob as overdue-notify-only

Replaces the deleted date-driven Future/Incomplete auto-advance job.
The new job never mutates Sprint.Status - overdue is a computed
frontend display state - it only sends the sprint_overdue
notification once per sprint via a new OverdueNotifiedAt stamp.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

Backend is now complete. **Before starting Part B, push this branch** (`git push -u origin feature/sprint-lifecycle-redesign` from `HRMS-Backend-v1`) so the frontend work has a stable API contract to build against and both repos' history is preserved if the session is interrupted.

---

## Part B — Frontend (`Hrms--Web-application---front-end---v1`)

### Task 7: Models, DTOs, mapper, API service, store

**Files:**
- Modify: `src/app/modules/work/models/sprint.model.ts`
- Modify: `src/app/modules/work/models/dto/sprint.dto.ts`
- Modify: `src/app/modules/work/utils/sprint.mapper.ts`
- Modify: `src/app/modules/work/data-access/sprint-api.service.ts`
- Modify: `src/app/modules/work/data-access/sprint-api.service.spec.ts`
- Modify: `src/app/modules/work/state/sprint-list.store.ts`
- Modify: `src/app/modules/work/state/sprint-list.store.spec.ts`

**Interfaces:**
- Produces: `SprintStatus = 'draft' | 'active' | 'complete' | 'achieved'`; `Sprint { ...; goal: string | null; startDate: Date | null; endDate: Date | null }`; `SprintListStore.create(objectiveId, { name, goal? })`, `.start(sprintId, { startDate, endDate, goal? }, objectiveId, activeOnly, projectId?)`, `.complete(sprintId, { disposition, targetSprintId? }, objectiveId, activeOnly, projectId?)`, `.edit(...)` unchanged shape but new payload fields. No more `.setStatus(...)`. Consumed by every component task that follows.

- [ ] **Step 1: Update the models**

```typescript
// sprint.model.ts
export type SprintStatus = 'draft' | 'active' | 'complete' | 'achieved';

export interface Sprint {
  id: string;
  objectiveId: string;
  name: string;
  goal: string | null;
  startDate: Date | null;
  endDate: Date | null;
  status: SprintStatus;
  completedAt: Date | null;
  achievedAt: Date | null;
}
```

```typescript
// sprint.dto.ts
export interface SprintDto {
  id: string;
  objectiveId: string;
  name: string;
  goal: string | null;
  startDate: string | null;
  endDate: string | null;
  status: 'draft' | 'active' | 'complete' | 'achieved';
  completedAt: string | null;
  achievedAt: string | null;
}

export interface CreateSprintRequestDto {
  name: string;
  goal?: string;
}

export interface StartSprintRequestDto {
  startDate: string;
  endDate: string;
  goal?: string;
}

export interface EditSprintRequestDto {
  name: string;
  goal: string | null;
  startDate?: string;
  endDate?: string;
}

export interface CompleteSprintRequestDto {
  disposition: 'backlog' | 'sprint';
  targetSprintId?: string;
}
```

- [ ] **Step 2: Update the mapper**

```typescript
// sprint.mapper.ts
import { Sprint } from '../models/sprint.model';
import { SprintDto } from '../models/dto/sprint.dto';

export function toSprint(dto: SprintDto): Sprint {
  return {
    id: dto.id,
    objectiveId: dto.objectiveId,
    name: dto.name,
    goal: dto.goal,
    startDate: dto.startDate ? new Date(dto.startDate) : null,
    endDate: dto.endDate ? new Date(dto.endDate) : null,
    status: dto.status,
    completedAt: dto.completedAt ? new Date(dto.completedAt) : null,
    achievedAt: dto.achievedAt ? new Date(dto.achievedAt) : null
  };
}
```

- [ ] **Step 3: Write the failing API service test** (add to `sprint-api.service.spec.ts`, alongside the existing two tests — keep them, adjust the `create` test's body to the new shape):

```typescript
  it('create posts to the sprints endpoint', () => {
    service.create('obj-1', { name: 'Sprint 1', goal: 'Ship it' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/objectives/obj-1/sprints`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Sprint 1', goal: 'Ship it' });
    req.flush({});
  });

  it('start posts dates and goal to the start endpoint', () => {
    service.start('sp-1', { startDate: '2026-09-21', endDate: '2026-10-02', goal: 'Goal' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/sprints/sp-1/start`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('complete posts the disposition to the complete endpoint', () => {
    service.complete('sp-1', { disposition: 'backlog' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/sprints/sp-1/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ disposition: 'backlog' });
    req.flush({});
  });
```

- [ ] **Step 4: Run to verify failure**

```bash
npx vitest run src/app/modules/work/data-access/sprint-api.service.spec.ts
```

Expected: FAIL (`service.start` doesn't exist; `complete` doesn't take a body yet).

- [ ] **Step 5: Implement the API service**

```typescript
// sprint-api.service.ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  CompleteSprintRequestDto, CreateSprintRequestDto, EditSprintRequestDto, SprintDto, StartSprintRequestDto
} from '../models/dto/sprint.dto';

@Injectable({ providedIn: 'root' })
export class SprintApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/work`;

  getByObjective(objectiveId: string, activeOnly: boolean): Observable<SprintDto[]> {
    const params = new HttpParams().set('activeOnly', String(activeOnly));
    return this.http.get<SprintDto[]>(`${this.baseUrl}/objectives/${objectiveId}/sprints`, { params });
  }

  getByProject(projectId: string): Observable<SprintDto[]> {
    return this.http.get<SprintDto[]>(`${this.baseUrl}/projects/${projectId}/sprints`);
  }

  create(objectiveId: string, request: CreateSprintRequestDto): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.baseUrl}/objectives/${objectiveId}/sprints`, request);
  }

  start(sprintId: string, request: StartSprintRequestDto): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.baseUrl}/sprints/${sprintId}/start`, request);
  }

  edit(sprintId: string, request: EditSprintRequestDto): Observable<SprintDto> {
    return this.http.patch<SprintDto>(`${this.baseUrl}/sprints/${sprintId}`, request);
  }

  complete(sprintId: string, request: CompleteSprintRequestDto): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.baseUrl}/sprints/${sprintId}/complete`, request);
  }

  achieve(sprintId: string): Observable<SprintDto> {
    return this.http.post<SprintDto>(`${this.baseUrl}/sprints/${sprintId}/achieve`, {});
  }
}
```

- [ ] **Step 6: Run to verify API service tests pass**

```bash
npx vitest run src/app/modules/work/data-access/sprint-api.service.spec.ts
```

Expected: PASS.

- [ ] **Step 7: Write the failing store test** (add to `sprint-list.store.spec.ts`, replacing the `complete` mock name usage and the existing `dto`):

```typescript
  const dto: SprintDto = {
    id: 's1', objectiveId: 'o1', name: 'Sprint 1', goal: null,
    startDate: '2026-09-01', endDate: '2026-09-14', status: 'active',
    completedAt: null, achievedAt: null
  };

  function setup(apiOverrides: Record<string, ReturnType<typeof vi.fn>> = {}) {
    const api = {
      getByObjective: vi.fn().mockReturnValue(of([dto])),
      create: vi.fn(),
      start: vi.fn(),
      edit: vi.fn(),
      complete: vi.fn(),
      achieve: vi.fn(),
      ...apiOverrides
    };
    TestBed.configureTestingModule({
      providers: [SprintListStore, { provide: SprintApiService, useValue: api }]
    });
    return { store: TestBed.inject(SprintListStore), api };
  }

  it('load populates sprints from the API', async () => {
    const { store } = setup();
    await store.load('o1', false);
    expect(store.sprints().length).toBe(1);
    expect(store.sprints()[0].name).toBe('Sprint 1');
  });

  it('start re-fetches the list after succeeding', async () => {
    const { store, api } = setup({ start: vi.fn().mockReturnValue(of(dto)) });
    await store.load('o1', false);
    await store.start('s1', { startDate: '2026-09-21', endDate: '2026-10-02' }, 'o1', false);
    expect(api.getByObjective).toHaveBeenCalledTimes(2);
  });

  it('complete forwards the disposition payload and re-fetches on success', async () => {
    const { store, api } = setup({ complete: vi.fn().mockReturnValue(of(dto)) });
    await store.load('o1', false);
    await store.complete('s1', { disposition: 'backlog' }, 'o1', false);
    expect(api.complete).toHaveBeenCalledWith('s1', { disposition: 'backlog' });
    expect(api.getByObjective).toHaveBeenCalledTimes(2);
  });
```

- [ ] **Step 8: Run to verify failure**

```bash
npx vitest run src/app/modules/work/state/sprint-list.store.spec.ts
```

Expected: FAIL (`store.start` doesn't exist yet; `SprintDto` shape mismatch).

- [ ] **Step 9: Implement the store** — replace `create`/`edit`/`complete`, add `start`, remove `setStatus`:

```typescript
import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { SprintApiService } from '../data-access/sprint-api.service';
import { toSprint } from '../utils/sprint.mapper';
import { Sprint } from '../models/sprint.model';
import { CompleteSprintRequestDto, CreateSprintRequestDto, EditSprintRequestDto, StartSprintRequestDto } from '../models/dto/sprint.dto';

interface SprintListState {
  readonly sprints: readonly Sprint[];
  readonly loading: boolean;
  readonly error: string | null;
}

const initialState: SprintListState = { sprints: [], loading: false, error: null };

function extractError(err: any, fallback: string): string {
  return err?.error?.detail || err?.error?.message || err?.message || fallback;
}

export const SprintListStore = signalStore(
  { providedIn: 'root' },
  withState<SprintListState>(initialState),
  withMethods((store, api = inject(SprintApiService)) => ({
    async loadForProject(projectId: string): Promise<void> {
      patchState(store, { loading: true, error: null });
      try {
        const dtos = await firstValueFrom(api.getByProject(projectId));
        patchState(store, { sprints: dtos.map(toSprint), loading: false });
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to load sprints.'), loading: false });
      }
    },

    async load(objectiveId: string, activeOnly: boolean): Promise<void> {
      patchState(store, { loading: true, error: null });
      try {
        const dtos = await firstValueFrom(api.getByObjective(objectiveId, activeOnly));
        patchState(store, { sprints: dtos.map(toSprint), loading: false });
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to load sprints.'), loading: false });
      }
    },

    async create(objectiveId: string, request: CreateSprintRequestDto, activeOnly: boolean, projectId?: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.create(objectiveId, request));
        await (projectId ? this.loadForProject(projectId) : this.load(objectiveId, activeOnly));
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to create the sprint.') });
        return false;
      }
    },

    async start(sprintId: string, request: StartSprintRequestDto, objectiveId: string, activeOnly: boolean, projectId?: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.start(sprintId, request));
        await (projectId ? this.loadForProject(projectId) : this.load(objectiveId, activeOnly));
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to start the sprint.') });
        return false;
      }
    },

    async edit(sprintId: string, request: EditSprintRequestDto, objectiveId: string, activeOnly: boolean, projectId?: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.edit(sprintId, request));
        await (projectId ? this.loadForProject(projectId) : this.load(objectiveId, activeOnly));
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to update the sprint.') });
        return false;
      }
    },

    async complete(sprintId: string, request: CompleteSprintRequestDto, objectiveId: string, activeOnly: boolean, projectId?: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.complete(sprintId, request));
        await (projectId ? this.loadForProject(projectId) : this.load(objectiveId, activeOnly));
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Every task in this sprint must be complete first.') });
        return false;
      }
    },

    async achieve(sprintId: string, objectiveId: string, activeOnly: boolean, projectId?: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.achieve(sprintId));
        await (projectId ? this.loadForProject(projectId) : this.load(objectiveId, activeOnly));
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to achieve the sprint.') });
        return false;
      }
    }
  }))
);
```

- [ ] **Step 10: Run to verify all pass**

```bash
npx vitest run src/app/modules/work/state/sprint-list.store.spec.ts src/app/modules/work/data-access/sprint-api.service.spec.ts
```

Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/app/modules/work/models/sprint.model.ts src/app/modules/work/models/dto/sprint.dto.ts src/app/modules/work/utils/sprint.mapper.ts src/app/modules/work/data-access/sprint-api.service.ts src/app/modules/work/data-access/sprint-api.service.spec.ts src/app/modules/work/state/sprint-list.store.ts src/app/modules/work/state/sprint-list.store.spec.ts
git commit -m "$(cat <<'EOF'
Rework sprint models/DTOs/store for the lifecycle redesign

SprintStatus drops future/incomplete, dates become nullable, goal is
added. SprintListStore gains start()/loses setStatus(), matching the
new backend commands.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: `SprintFormComponent` — dateless create, Name/Goal-only edit for Draft

**Files:**
- Modify: `src/app/modules/work/ui/sprint-form/sprint-form.component.ts`
- Modify: `src/app/modules/work/ui/sprint-form/sprint-form.component.spec.ts`

**Interfaces:**
- Consumes: `SprintListStore.create`/`.edit` (Task 7).
- Produces: unchanged public API shape (`mode`, `open`, `objectiveId`, `modules`, `isObjectiveOwner`, `sprint` inputs; `created`/`saved`/`cancelled` outputs) — consumed by `TaskBacklogComponent` (Task 12), unchanged wiring.

- [ ] **Step 1: Update the failing tests** — replace the four existing `it(...)` blocks:

```typescript
  it('renders create mode with module options and emits created after store create', async () => {
    setup({ modules: [
      { objectiveId: 'obj-1', title: 'Module One', isOwner: true },
      { objectiveId: 'obj-2', title: 'Module Two', isOwner: false }
    ] });
    const component = fixture.componentInstance as any;
    component.selectedObjectiveId.set('obj-1');
    component.name.set('Sprint X');
    component.goal.set('Ship it');
    const created = vi.fn();
    component.created.subscribe(created);

    await component.submit();

    expect(storeStub.create).toHaveBeenCalledWith('obj-1', { name: 'Sprint X', goal: 'Ship it' }, false);
    expect(created).toHaveBeenCalledTimes(1);
  });

  it('requires a module in create mode when a module list is provided', async () => {
    setup({ modules: [{ objectiveId: 'obj-1', title: 'Module One', isOwner: true }] });
    const component = fixture.componentInstance as any;
    component.name.set('Sprint X');
    await component.submit();

    expect(storeStub.create).not.toHaveBeenCalled();
    expect(component.moduleError()).toContain('Select a module');
  });

  it('renders edit mode without a module picker and emits saved with the edit payload', async () => {
    setup({ mode: 'edit', sprint });
    const component = fixture.componentInstance as any;
    component.name.set('Sprint One Renamed');
    const saved = vi.fn();
    component.saved.subscribe(saved);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-select')).toBeNull();
    await component.submit();

    expect(storeStub.edit).toHaveBeenCalledWith('sp-1', { name: 'Sprint One Renamed', goal: null, startDate: undefined, endDate: undefined }, 'obj-1', false);
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('re-seeds edit fields when a different sprint arrives while open', () => {
    setup({ mode: 'edit', sprint });
    const other = { ...sprint, id: 'sp-2', name: 'Sprint Two' };
    fixture.componentRef.setInput('sprint', other);
    fixture.detectChanges();

    expect((fixture.componentInstance as any).name()).toBe('Sprint Two');
  });
```

Update the `sprint` fixture at the top of the file to the new `Sprint` shape (`goal: null`, and for an Active sprint keep real dates):

```typescript
  const sprint: Sprint = {
    id: 'sp-1', objectiveId: 'obj-1', name: 'Sprint One', goal: null,
    startDate: new Date('2026-09-01T00:00:00.000Z'),
    endDate: new Date('2026-09-14T00:00:00.000Z'), status: 'active', completedAt: null, achievedAt: null
  };
```

- [ ] **Step 2: Run to verify failure**

```bash
npx vitest run src/app/modules/work/ui/sprint-form/sprint-form.component.spec.ts
```

Expected: FAIL (component has no `goal` signal; submit still sends dates).

- [ ] **Step 3: Implement**

```typescript
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ModalComponent } from '../../../../shared/ui/modal/modal.component';
import { InputComponent } from '../../../../shared/ui/input/input.component';
import { ButtonComponent } from '../../../../shared/ui/button/button.component';
import { WorkDropdownComponent, WorkDropdownOption } from '../work-dropdown/work-dropdown.component';
import { SprintListStore } from '../../state/sprint-list.store';
import { Sprint } from '../../models/sprint.model';

export interface SprintFormModuleOption {
  objectiveId: string;
  title: string;
  isOwner: boolean;
}

@Component({
  selector: 'app-sprint-form',
  standalone: true,
  imports: [ModalComponent, InputComponent, ButtonComponent, WorkDropdownComponent],
  template: `
    <app-modal [open]="open()" [title]="mode() === 'edit' ? 'Edit sprint' : 'Create sprint'" (closed)="cancelled.emit()">
      <div class="sprint-form">
        @if (mode() === 'create' && modules().length > 0) {
          <app-work-dropdown
            [options]="moduleOptions()"
            [selected]="selectedObjectiveId()"
            fieldLabel="Module"
            placeholder="Select a module"
            [error]="moduleError() || ''"
            leadingIcon="module"
            [fullWidth]="true"
            (selectionChanged)="selectedObjectiveId.set($event)"
          />
        }
        <app-input label="Sprint name" [value]="name()" (valueChange)="name.set($event)" />
        <app-input label="Sprint goal (optional)" [value]="goal()" (valueChange)="goal.set($event)" />
        @if (mode() === 'edit' && sprint()?.status === 'active') {
          <app-input label="Start date" type="date" [value]="startDate()" (valueChange)="startDate.set($event)" />
          <app-input label="End date" type="date" [value]="endDate()" (valueChange)="endDate.set($event)" />
        }

        @if (store.error()) {
          <p class="sprint-form__error" role="alert">{{ store.error() }}</p>
        }
        @if (moduleError()) {
          <p class="sprint-form__error" role="alert">{{ moduleError() }}</p>
        }

        <div class="sprint-form__actions">
          <app-button variant="secondary" [disabled]="submitting()" (pressed)="cancelled.emit()">Cancel</app-button>
          <app-button variant="primary" [loading]="submitting()" (pressed)="submit()">
            {{ mode() === 'edit' ? 'Save changes' : 'Create sprint' }}
          </app-button>
        </div>
      </div>
    </app-modal>
  `,
  styles: [`
    .sprint-form { display: flex; flex-direction: column; gap: 8px; }
    .sprint-form__error { color: var(--color-danger-text); font-size: 12px; }
    .sprint-form__actions { display: flex; justify-content: flex-end; gap: 8px; }
  `]
})
export class SprintFormComponent {
  protected readonly store = inject(SprintListStore);

  mode = input<'create' | 'edit'>('create');
  open = input(false);
  objectiveId = input<string>('');
  modules = input<readonly SprintFormModuleOption[]>([]);
  isObjectiveOwner = input(false);
  sprint = input<Sprint | null>(null);

  created = output<void>();
  saved = output<void>();
  cancelled = output<void>();

  protected readonly name = signal('');
  protected readonly goal = signal('');
  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly submitting = signal(false);
  protected readonly selectedObjectiveId = signal('');
  protected readonly moduleError = signal<string | null>(null);

  protected readonly moduleOptions = computed<WorkDropdownOption[]>(() =>
    this.modules().map((module) => ({ value: module.objectiveId, label: module.title }))
  );

  protected readonly effectiveObjectiveId = computed(() =>
    this.modules().length > 0 ? this.selectedObjectiveId() : this.objectiveId()
  );

  constructor() {
    effect(() => {
      this.open();
      const current = this.sprint();
      if (this.mode() === 'edit' && current) {
        this.name.set(current.name);
        this.goal.set(current.goal ?? '');
        this.startDate.set(current.startDate ? current.startDate.toISOString().slice(0, 10) : '');
        this.endDate.set(current.endDate ? current.endDate.toISOString().slice(0, 10) : '');
      } else if (this.mode() === 'create') {
        this.name.set('');
        this.goal.set('');
        this.selectedObjectiveId.set('');
      }
      this.moduleError.set(null);
    });
  }

  async submit(): Promise<void> {
    this.moduleError.set(null);
    if (this.mode() === 'create') {
      const objectiveId = this.effectiveObjectiveId();
      if (!objectiveId) {
        this.moduleError.set('Select a module first.');
        return;
      }

      this.submitting.set(true);
      try {
        const ok = await this.store.create(objectiveId, {
          name: this.name().trim(),
          goal: this.goal().trim() || undefined
        }, false);
        if (ok) {
          this.name.set('');
          this.goal.set('');
          this.selectedObjectiveId.set('');
          this.created.emit();
        }
      } finally {
        this.submitting.set(false);
      }
      return;
    }

    const current = this.sprint();
    if (!current) return;
    this.submitting.set(true);
    try {
      const isActive = current.status === 'active';
      const ok = await this.store.edit(current.id, {
        name: this.name().trim(),
        goal: this.goal().trim() || null,
        startDate: isActive ? this.startDate() : undefined,
        endDate: isActive ? this.endDate() : undefined
      }, this.objectiveId(), false);
      if (ok) this.saved.emit();
    } finally {
      this.submitting.set(false);
    }
  }
}
```

- [ ] **Step 4: Run to verify all pass**

```bash
npx vitest run src/app/modules/work/ui/sprint-form/sprint-form.component.spec.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-form
git commit -m "$(cat <<'EOF'
Simplify SprintFormComponent to name/goal-only creation

Create drops date inputs entirely (a sprint is created as a dateless
Draft). Edit mode only shows date inputs once the sprint is Active.
Drops the dead "Request sprint" label branch.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: New `SprintStartDialogComponent`

**Files:**
- Create: `src/app/modules/work/ui/sprint-start-dialog/sprint-start-dialog.component.ts`
- Create: `src/app/modules/work/ui/sprint-start-dialog/sprint-start-dialog.component.spec.ts`

**Interfaces:**
- Consumes: `SprintListStore.start` (Task 7), `WorkTask` (existing, for the summary computations), `ModalComponent`/`InputComponent`/`ButtonComponent` (existing shared UI, same imports as `SprintFormComponent`).
- Produces: `SprintStartDialogComponent` with inputs `open: boolean`, `sprint: Sprint | null`, `tasks: readonly WorkTask[]`, `objectiveId: string`; outputs `started: void`, `cancelled: void` — consumed by `SprintTabComponent`/`TaskBacklogComponent` (Task 12).

- [ ] **Step 1: Write the failing test**

```typescript
// sprint-start-dialog.component.spec.ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SprintStartDialogComponent } from './sprint-start-dialog.component';
import { SprintListStore } from '../../state/sprint-list.store';
import { Sprint } from '../../models/sprint.model';
import { WorkTask } from '../../models/task.model';

describe('SprintStartDialogComponent', () => {
  let fixture: ComponentFixture<SprintStartDialogComponent>;
  let storeStub: { start: ReturnType<typeof vi.fn> };

  const sprint: Sprint = {
    id: 'sp-1', objectiveId: 'obj-1', name: 'Sprint 1', goal: 'Old goal',
    startDate: null, endDate: null, status: 'draft', completedAt: null, achievedAt: null
  };

  const tasks: WorkTask[] = [
    { id: 't1', objectiveId: 'obj-1', sprintId: 'sp-1', title: 'Task 1', estimatedHours: 4, assigneeEmployeeIds: ['e1'] } as WorkTask,
    { id: 't2', objectiveId: 'obj-1', sprintId: 'sp-1', title: 'Task 2', estimatedHours: null, assigneeEmployeeIds: [] } as WorkTask
  ];

  function setup(overrides: Record<string, unknown> = {}): void {
    storeStub = { start: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [SprintStartDialogComponent],
      providers: [{ provide: SprintListStore, useValue: storeStub }]
    });
    fixture = TestBed.createComponent(SprintStartDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('sprint', sprint);
    fixture.componentRef.setInput('tasks', tasks);
    fixture.componentRef.setInput('objectiveId', 'obj-1');
    for (const [key, value] of Object.entries(overrides)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
  }

  it('pre-fills the goal from the sprint and summarizes tasks/hours/assignees', () => {
    setup();
    const component = fixture.componentInstance as any;
    expect(component.goal()).toBe('Old goal');
    expect(component.taskCount()).toBe(2);
    expect(component.totalEstimatedHours()).toBe(4);
    expect(component.assigneeCount()).toBe(1);
    expect(component.hasUnestimatedTasks()).toBe(true);
    expect(component.hasUnassignedTasks()).toBe(true);
  });

  it('starts the sprint with the chosen dates and emits started', async () => {
    setup();
    const component = fixture.componentInstance as any;
    component.startDate.set('2026-09-21');
    component.endDate.set('2026-10-02');
    const started = vi.fn();
    component.started.subscribe(started);

    await component.submit();

    expect(storeStub.start).toHaveBeenCalledWith(
      'sp-1', { startDate: '2026-09-21', endDate: '2026-10-02', goal: 'Old goal' }, 'obj-1', false
    );
    expect(started).toHaveBeenCalledTimes(1);
  });

  it('does not submit when either date is missing', async () => {
    setup();
    const component = fixture.componentInstance as any;
    component.startDate.set('2026-09-21');
    await component.submit();

    expect(storeStub.start).not.toHaveBeenCalled();
    expect(component.dateError()).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
npx vitest run src/app/modules/work/ui/sprint-start-dialog/sprint-start-dialog.component.spec.ts
```

Expected: FAIL (module/file doesn't exist).

- [ ] **Step 3: Implement**

```typescript
// sprint-start-dialog.component.ts
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ModalComponent } from '../../../../shared/ui/modal/modal.component';
import { InputComponent } from '../../../../shared/ui/input/input.component';
import { ButtonComponent } from '../../../../shared/ui/button/button.component';
import { SprintListStore } from '../../state/sprint-list.store';
import { Sprint } from '../../models/sprint.model';
import { WorkTask } from '../../models/task.model';

@Component({
  selector: 'app-sprint-start-dialog',
  standalone: true,
  imports: [ModalComponent, InputComponent, ButtonComponent],
  template: `
    <app-modal [open]="open()" [title]="'Start ' + (sprint()?.name ?? 'sprint')" (closed)="cancelled.emit()">
      <div class="sprint-start-dialog">
        <p class="sprint-start-dialog__summary">
          {{ taskCount() }} task{{ taskCount() === 1 ? '' : 's' }} ·
          {{ totalEstimatedHours() }}h estimated ·
          {{ assigneeCount() }} member{{ assigneeCount() === 1 ? '' : 's' }} assigned
        </p>

        <app-input label="Start date" type="date" [value]="startDate()" (valueChange)="startDate.set($event)" />
        <app-input label="End date" type="date" [value]="endDate()" (valueChange)="endDate.set($event)" />
        <app-input label="Sprint goal (optional)" [value]="goal()" (valueChange)="goal.set($event)" />

        @if (hasUnestimatedTasks()) {
          <p class="sprint-start-dialog__warning">⚠ Some tasks have no estimate.</p>
        }
        @if (hasUnassignedTasks()) {
          <p class="sprint-start-dialog__warning">⚠ Some tasks have no assignee.</p>
        }
        <p class="sprint-start-dialog__hint">You can still start the sprint.</p>

        @if (dateError()) {
          <p class="sprint-start-dialog__error" role="alert">{{ dateError() }}</p>
        }
        @if (store.error()) {
          <p class="sprint-start-dialog__error" role="alert">{{ store.error() }}</p>
        }

        <div class="sprint-start-dialog__actions">
          <app-button variant="secondary" [disabled]="submitting()" (pressed)="cancelled.emit()">Cancel</app-button>
          <app-button variant="primary" [loading]="submitting()" (pressed)="submit()">Start sprint</app-button>
        </div>
      </div>
    </app-modal>
  `,
  styles: [`
    .sprint-start-dialog { display: flex; flex-direction: column; gap: 8px; }
    .sprint-start-dialog__summary { font-size: 12px; color: var(--color-text-secondary); margin: 0 0 4px; }
    .sprint-start-dialog__warning { color: var(--color-warning-text, #92400e); font-size: 12px; margin: 0; }
    .sprint-start-dialog__hint { font-size: 12px; color: var(--color-text-secondary); margin: 0; }
    .sprint-start-dialog__error { color: var(--color-danger-text); font-size: 12px; }
    .sprint-start-dialog__actions { display: flex; justify-content: flex-end; gap: 8px; }
  `]
})
export class SprintStartDialogComponent {
  protected readonly store = inject(SprintListStore);

  open = input(false);
  sprint = input<Sprint | null>(null);
  tasks = input<readonly WorkTask[]>([]);
  objectiveId = input<string>('');
  activeOnly = input(false);
  projectId = input<string | undefined>(undefined);

  started = output<void>();
  cancelled = output<void>();

  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly goal = signal('');
  protected readonly submitting = signal(false);
  protected readonly dateError = signal<string | null>(null);

  protected readonly taskCount = computed(() => this.tasks().length);
  protected readonly totalEstimatedHours = computed(() =>
    this.tasks().reduce((sum, t) => sum + (t.estimatedHours ?? 0), 0)
  );
  protected readonly assigneeCount = computed(() =>
    new Set(this.tasks().flatMap((t) => t.assigneeEmployeeIds)).size
  );
  protected readonly hasUnestimatedTasks = computed(() =>
    this.tasks().some((t) => t.estimatedHours == null)
  );
  protected readonly hasUnassignedTasks = computed(() =>
    this.tasks().some((t) => t.assigneeEmployeeIds.length === 0)
  );

  constructor() {
    effect(() => {
      this.open();
      this.goal.set(this.sprint()?.goal ?? '');
      this.startDate.set('');
      this.endDate.set('');
      this.dateError.set(null);
    });
  }

  async submit(): Promise<void> {
    this.dateError.set(null);
    const current = this.sprint();
    if (!current || !this.startDate() || !this.endDate()) {
      this.dateError.set('Start and end dates are required.');
      return;
    }

    this.submitting.set(true);
    try {
      const ok = await this.store.start(current.id, {
        startDate: this.startDate(),
        endDate: this.endDate(),
        goal: this.goal().trim() || undefined
      }, this.objectiveId(), this.activeOnly(), this.projectId());
      if (ok) this.started.emit();
    } finally {
      this.submitting.set(false);
    }
  }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
npx vitest run src/app/modules/work/ui/sprint-start-dialog/sprint-start-dialog.component.spec.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-start-dialog
git commit -m "$(cat <<'EOF'
Add SprintStartDialogComponent

Draft -> Active: shows a task/hours/assignee summary, collects
start/end dates and an editable goal, warns (non-blocking) about
unestimated or unassigned tasks.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: New `SprintCompleteDialogComponent`

**Files:**
- Create: `src/app/modules/work/ui/sprint-complete-dialog/sprint-complete-dialog.component.ts`
- Create: `src/app/modules/work/ui/sprint-complete-dialog/sprint-complete-dialog.component.spec.ts`

**Interfaces:**
- Consumes: `SprintListStore.complete` (Task 7), `WorkDropdownComponent` (existing, for the target-sprint picker).
- Produces: `SprintCompleteDialogComponent` with inputs `open`, `sprint: Sprint | null`, `tasks: readonly WorkTask[]`, `otherSprints: readonly Sprint[]` (same-objective candidates, excluding self and any Complete/Achieved), `objectiveId`; outputs `completed: void`, `cancelled: void` — consumed by `SprintTabComponent`/`TaskBacklogComponent` (Task 12) **and** `milestone-tree-tab.component.ts` (Task 13).

- [ ] **Step 1: Write the failing test**

```typescript
// sprint-complete-dialog.component.spec.ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SprintCompleteDialogComponent } from './sprint-complete-dialog.component';
import { SprintListStore } from '../../state/sprint-list.store';
import { Sprint } from '../../models/sprint.model';
import { WorkTask } from '../../models/task.model';

describe('SprintCompleteDialogComponent', () => {
  let fixture: ComponentFixture<SprintCompleteDialogComponent>;
  let storeStub: { complete: ReturnType<typeof vi.fn> };

  const sprint: Sprint = {
    id: 'sp-1', objectiveId: 'obj-1', name: 'Sprint 1', goal: null,
    startDate: new Date('2026-09-01'), endDate: new Date('2026-09-14'), status: 'active', completedAt: null, achievedAt: null
  };
  const otherSprint: Sprint = { ...sprint, id: 'sp-2', name: 'Sprint 2', status: 'draft' };

  const tasks: WorkTask[] = [
    { id: 't1', objectiveId: 'obj-1', sprintId: 'sp-1', title: 'Done task', estimatedHours: 4, statusId: 'done' } as WorkTask,
    { id: 't2', objectiveId: 'obj-1', sprintId: 'sp-1', title: 'Open task', estimatedHours: 3, statusId: 'todo' } as WorkTask
  ];

  function setup(overrides: Record<string, unknown> = {}): void {
    storeStub = { complete: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [SprintCompleteDialogComponent],
      providers: [{ provide: SprintListStore, useValue: storeStub }]
    });
    fixture = TestBed.createComponent(SprintCompleteDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('sprint', sprint);
    fixture.componentRef.setInput('tasks', tasks);
    fixture.componentRef.setInput('otherSprints', [otherSprint]);
    fixture.componentRef.setInput('objectiveId', 'obj-1');
    for (const [key, value] of Object.entries(overrides)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
  }

  it('defaults to Move to Backlog and completes with that disposition', async () => {
    setup();
    const component = fixture.componentInstance as any;
    expect(component.disposition()).toBe('backlog');
    const completed = vi.fn();
    component.completed.subscribe(completed);

    await component.submit();

    expect(storeStub.complete).toHaveBeenCalledWith('sp-1', { disposition: 'backlog' }, 'obj-1', false);
    expect(completed).toHaveBeenCalledTimes(1);
  });

  it('requires a target sprint when disposition is sprint', async () => {
    setup();
    const component = fixture.componentInstance as any;
    component.disposition.set('sprint');
    await component.submit();

    expect(storeStub.complete).not.toHaveBeenCalled();
    expect(component.targetError()).toBeTruthy();
  });

  it('completes with the chosen target sprint', async () => {
    setup();
    const component = fixture.componentInstance as any;
    component.disposition.set('sprint');
    component.targetSprintId.set('sp-2');

    await component.submit();

    expect(storeStub.complete).toHaveBeenCalledWith('sp-1', { disposition: 'sprint', targetSprintId: 'sp-2' }, 'obj-1', false);
  });
});
```

- [ ] **Step 2: Run to verify failure**

```bash
npx vitest run src/app/modules/work/ui/sprint-complete-dialog/sprint-complete-dialog.component.spec.ts
```

Expected: FAIL (module/file doesn't exist).

- [ ] **Step 3: Implement**

```typescript
// sprint-complete-dialog.component.ts
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { ModalComponent } from '../../../../shared/ui/modal/modal.component';
import { ButtonComponent } from '../../../../shared/ui/button/button.component';
import { WorkDropdownComponent, WorkDropdownOption } from '../work-dropdown/work-dropdown.component';
import { SprintListStore } from '../../state/sprint-list.store';
import { Sprint } from '../../models/sprint.model';
import { WorkTask } from '../../models/task.model';

@Component({
  selector: 'app-sprint-complete-dialog',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, WorkDropdownComponent],
  template: `
    <app-modal [open]="open()" [title]="'Complete ' + (sprint()?.name ?? 'sprint')" (closed)="cancelled.emit()">
      <div class="sprint-complete-dialog">
        <p class="sprint-complete-dialog__summary">
          {{ totalCount() }} tasks total · {{ completeCount() }} completed · {{ incompleteCount() }} incomplete
        </p>
        <p class="sprint-complete-dialog__summary">{{ totalEstimatedHours() }}h planned</p>

        @if (incompleteCount() > 0) {
          <p class="sprint-complete-dialog__label">What should happen to the {{ incompleteCount() }} incomplete task{{ incompleteCount() === 1 ? '' : 's' }}?</p>
          <label class="sprint-complete-dialog__radio">
            <input type="radio" name="disposition" [checked]="disposition() === 'backlog'" (change)="disposition.set('backlog')" />
            Move to Backlog
          </label>
          <label class="sprint-complete-dialog__radio">
            <input type="radio" name="disposition" [checked]="disposition() === 'sprint'" (change)="disposition.set('sprint')" />
            Move to another sprint
          </label>
          @if (disposition() === 'sprint') {
            <app-work-dropdown
              [options]="otherSprintOptions()"
              [selected]="targetSprintId()"
              placeholder="Select a sprint"
              leadingIcon="sprint"
              [fullWidth]="true"
              (selectionChanged)="targetSprintId.set($event)"
            />
          }
        }

        @if (targetError()) {
          <p class="sprint-complete-dialog__error" role="alert">{{ targetError() }}</p>
        }
        @if (store.error()) {
          <p class="sprint-complete-dialog__error" role="alert">{{ store.error() }}</p>
        }

        <div class="sprint-complete-dialog__actions">
          <app-button variant="secondary" [disabled]="submitting()" (pressed)="cancelled.emit()">Cancel</app-button>
          <app-button variant="primary" [loading]="submitting()" (pressed)="submit()">Complete sprint</app-button>
        </div>
      </div>
    </app-modal>
  `,
  styles: [`
    .sprint-complete-dialog { display: flex; flex-direction: column; gap: 8px; }
    .sprint-complete-dialog__summary { font-size: 12px; color: var(--color-text-secondary); margin: 0; }
    .sprint-complete-dialog__label { font-size: 12px; font-weight: 600; color: var(--color-text-primary); margin: 6px 0 0; }
    .sprint-complete-dialog__radio { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--color-text-primary); }
    .sprint-complete-dialog__error { color: var(--color-danger-text); font-size: 12px; }
    .sprint-complete-dialog__actions { display: flex; justify-content: flex-end; gap: 8px; }
  `]
})
export class SprintCompleteDialogComponent {
  protected readonly store = inject(SprintListStore);

  open = input(false);
  sprint = input<Sprint | null>(null);
  tasks = input<readonly WorkTask[]>([]);
  otherSprints = input<readonly Sprint[]>([]);
  objectiveId = input<string>('');
  activeOnly = input(false);
  projectId = input<string | undefined>(undefined);
  completeStatusIds = input<ReadonlySet<string>>(new Set());

  completed = output<void>();
  cancelled = output<void>();

  protected readonly disposition = signal<'backlog' | 'sprint'>('backlog');
  protected readonly targetSprintId = signal('');
  protected readonly submitting = signal(false);
  protected readonly targetError = signal<string | null>(null);

  protected readonly totalCount = computed(() => this.tasks().length);
  protected readonly completeCount = computed(() =>
    this.tasks().filter((t) => this.completeStatusIds().has(t.statusId)).length
  );
  protected readonly incompleteCount = computed(() => this.totalCount() - this.completeCount());
  protected readonly totalEstimatedHours = computed(() =>
    this.tasks().reduce((sum, t) => sum + (t.estimatedHours ?? 0), 0)
  );
  protected readonly otherSprintOptions = computed<WorkDropdownOption[]>(() =>
    this.otherSprints().map((s) => ({ value: s.id, label: s.name }))
  );

  constructor() {
    effect(() => {
      this.open();
      this.disposition.set('backlog');
      this.targetSprintId.set('');
      this.targetError.set(null);
    });
  }

  async submit(): Promise<void> {
    this.targetError.set(null);
    const current = this.sprint();
    if (!current) return;

    if (this.disposition() === 'sprint' && !this.targetSprintId()) {
      this.targetError.set('Select a target sprint.');
      return;
    }

    this.submitting.set(true);
    try {
      const ok = await this.store.complete(current.id, this.disposition() === 'sprint'
        ? { disposition: 'sprint', targetSprintId: this.targetSprintId() }
        : { disposition: 'backlog' },
        this.objectiveId(), this.activeOnly(), this.projectId());
      if (ok) this.completed.emit();
    } finally {
      this.submitting.set(false);
    }
  }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
npx vitest run src/app/modules/work/ui/sprint-complete-dialog/sprint-complete-dialog.component.spec.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-complete-dialog
git commit -m "$(cat <<'EOF'
Add SprintCompleteDialogComponent

Active -> Complete: shows a completion summary and lets the owner
choose where incomplete tasks go (Backlog or another sprint) instead
of silently blocking completion until every task is done.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: `SprintTabComponent` — three-state rendering, no dropdown

**Files:**
- Modify: `src/app/modules/work/ui/sprint-tab/sprint-tab.component.ts`
- Modify: `src/app/modules/work/ui/sprint-tab/sprint-tab.component.spec.ts`

**Interfaces:**
- Consumes: nothing new (still receives `sprint`, `tasks`, `statusNames`, `statuses`, `objectiveOwners`, `currentEmployeeId`, `expanded` as inputs).
- Produces: drops `statusChangeRequested` output; adds `startRequested: output<void>()`, `completeRequested: output<void>()`. Consumed by `TaskBacklogComponent` (Task 12).

- [ ] **Step 1: Read the current `sprint-tab.component.spec.ts` in full** (not reproduced here — open it) to see its exact existing test structure, then replace any test exercising `statusChangeRequested`/the dropdown with these:

```typescript
  it('draft sprint shows "No dates set" and a Start sprint button', () => {
    setup({ sprint: { ...baseSprint, status: 'draft', startDate: null, endDate: null } });
    expect(fixture.nativeElement.textContent).toContain('No dates set');
    expect(fixture.nativeElement.querySelector('[data-testid="start-sprint"]')).toBeTruthy();
  });

  it('active sprint shows a Complete Sprint button and no status dropdown', () => {
    setup({ sprint: { ...baseSprint, status: 'active' } });
    expect(fixture.nativeElement.querySelector('[data-testid="complete-sprint"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-work-dropdown')).toBeNull();
  });

  it('emits startRequested when Start sprint is clicked', () => {
    setup({ sprint: { ...baseSprint, status: 'draft', startDate: null, endDate: null } });
    const handler = vi.fn();
    (fixture.componentInstance as any).startRequested.subscribe(handler);
    fixture.nativeElement.querySelector('[data-testid="start-sprint"]').click();
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('emits completeRequested when Complete Sprint is clicked', () => {
    setup({ sprint: { ...baseSprint, status: 'active' } });
    const handler = vi.fn();
    (fixture.componentInstance as any).completeRequested.subscribe(handler);
    fixture.nativeElement.querySelector('[data-testid="complete-sprint"]').click();
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('complete sprint renders read-only with no action buttons', () => {
    setup({ sprint: { ...baseSprint, status: 'complete' } });
    expect(fixture.nativeElement.querySelector('[data-testid="start-sprint"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="complete-sprint"]')).toBeNull();
  });
```

(Adjust the `setup`/`baseSprint` fixture at the top of the file to the new `Sprint` shape, matching Task 8's pattern — `goal: null`, nullable dates.)

- [ ] **Step 2: Run to verify failure**

```bash
npx vitest run src/app/modules/work/ui/sprint-tab/sprint-tab.component.spec.ts
```

Expected: FAIL (no `data-testid="start-sprint"`/`"complete-sprint"`, no `startRequested`/`completeRequested` outputs yet).

- [ ] **Step 3: Implement**

```typescript
import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { TaskTableComponent } from '../task-table/task-table.component';
import { Sprint } from '../../models/sprint.model';
import { TaskStatusColumn, WorkTask } from '../../models/task.model';

@Component({
  selector: 'app-sprint-tab',
  standalone: true,
  imports: [DatePipe, TaskTableComponent],
  template: `
    <div class="sprint-tab" [attr.data-status]="sprint().status">
      <div class="sprint-tab__header">
        <button type="button" class="sprint-tab__expand" (click)="toggled.emit()" [attr.aria-expanded]="expanded()">
          <svg class="sprint-tab__expand-icon" [class.rotate-90]="expanded()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 6 6 6-6 6" /></svg>
          <span class="min-w-0 truncate">{{ sprint().name }}</span>
        </button>

        @if (sprint().status === 'draft') {
          <span class="sprint-tab__dates">No dates set</span>
        } @else if (sprint().startDate && sprint().endDate) {
          <span class="sprint-tab__dates">{{ sprint().startDate | date: 'MMM d' }} – {{ sprint().endDate | date: 'MMM d' }}</span>
          @if (isOverdue()) {
            <span class="sprint-tab__overdue">{{ daysOverdue() }} day{{ daysOverdue() === 1 ? '' : 's' }} overdue</span>
          } @else if (sprint().status === 'active' && daysRemaining() !== null) {
            <span class="sprint-tab__remaining">{{ daysRemaining() }} day{{ daysRemaining() === 1 ? '' : 's' }} remaining</span>
          }
        }

        <span class="sprint-tab__counts">{{ tasks().length }} tasks · {{ totalEstimatedHours() }}h estimated</span>

        @if (isObjectiveOwner()) {
          @if (sprint().status === 'draft') {
            <button type="button" class="sprint-tab__edit" (click)="editRequested.emit()"><span>Edit sprint</span></button>
            <button type="button" class="sprint-tab__primary" data-testid="start-sprint" (click)="startRequested.emit()">Start sprint →</button>
          } @else if (sprint().status === 'active') {
            <button type="button" class="sprint-tab__edit" (click)="editRequested.emit()"><span>Edit</span></button>
            <button type="button" class="sprint-tab__primary" data-testid="complete-sprint" (click)="completeRequested.emit()">Complete Sprint</button>
          } @else {
            <span class="sprint-tab__status-text" [attr.data-status]="sprint().status">{{ sprint().status }}</span>
          }
        } @else {
          <span class="sprint-tab__status-text" [attr.data-status]="sprint().status"><span class="sprint-tab__status-dot" aria-hidden="true"></span>{{ sprint().status }}</span>
        }
      </div>

      @if (expanded() && sprint().status !== 'complete') {
        <div class="sprint-tab__body">
          <div class="sprint-tab__body-toolbar">
            <button type="button" class="sprint-tab__add-task" aria-label="Add task to this sprint" title="Add task to this sprint" (click)="createTaskRequested.emit()"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14" /></svg></button>
          </div>
          <app-task-table
            [tasks]="tasks()"
            [statusNames]="statusNames()"
            [statuses]="statuses()"
            [objectiveOwners]="objectiveOwners()"
            [currentEmployeeId]="currentEmployeeId()"
            (viewed)="taskViewed.emit($event)"
            (statusChangeRequested)="taskStatusChangeRequested.emit($event)"
            (taskChanged)="taskChanged.emit()"
          />
        </div>
      } @else if (expanded() && sprint().status === 'complete') {
        <div class="sprint-tab__body sprint-tab__body--readonly">{{ tasks().length }} tasks in this completed sprint.</div>
      }
    </div>
  `,
  styles: [`
    .sprint-tab { border: 1px solid var(--color-border); border-radius: 12px; margin-bottom: 10px; overflow: hidden; background: var(--color-surface); box-shadow: 0 1px 2px rgb(15 23 42 / 0.03); }
    .sprint-tab[data-status="complete"] { opacity: 0.75; }
    .sprint-tab__header { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; min-height: 52px; padding: 10px 14px; background: var(--color-surface); }
    .sprint-tab__expand { display: inline-flex; min-width: 0; flex: 1; align-items: center; gap: 7px; text-align: left; background: none; border: none; cursor: pointer; font-size: 13px; font-weight: 600; color: var(--color-text-primary); }
    .sprint-tab__expand:focus-visible, .sprint-tab__edit:focus-visible, .sprint-tab__primary:focus-visible, .sprint-tab__add-task:focus-visible { outline: 2px solid var(--color-focus-ring); outline-offset: 2px; }
    .sprint-tab__expand-icon { width: 15px; height: 15px; flex: 0 0 auto; color: var(--color-accent); transition: transform 180ms ease; }
    .sprint-tab__dates, .sprint-tab__counts { flex: 0 0 auto; font-size: 11px; color: var(--color-text-secondary); }
    .sprint-tab__overdue { flex: 0 0 auto; font-size: 11px; font-weight: 600; color: var(--color-danger-text, #b91c1c); }
    .sprint-tab__remaining { flex: 0 0 auto; font-size: 11px; color: var(--color-text-secondary); }
    .sprint-tab__edit, .sprint-tab__primary { display: inline-flex; align-items: center; gap: 5px; border: 1px solid var(--color-border); border-radius: 8px; padding: 7px 10px; background: var(--color-surface); color: var(--color-text-secondary); font-size: 12px; font-weight: 500; cursor: pointer; transition: background-color 160ms ease, border-color 160ms ease, color 160ms ease; }
    .sprint-tab__primary { border-color: var(--color-accent); color: var(--color-accent); font-weight: 600; }
    .sprint-tab__edit:hover, .sprint-tab__primary:hover { border-color: var(--color-accent); background: var(--color-surface-secondary); color: var(--color-text-primary); }
    .sprint-tab__status-text { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--color-text-secondary); text-transform: capitalize; }
    .sprint-tab__status-dot { width: 7px; height: 7px; border-radius: 999px; background: var(--color-accent); }
    .sprint-tab__body { border-top: 1px solid var(--color-border); padding: 12px 14px; }
    .sprint-tab__body--readonly { font-size: 12px; color: var(--color-text-secondary); }
    .sprint-tab__body-toolbar { display: flex; justify-content: flex-end; margin-bottom: 8px; }
    .sprint-tab__add-task { display: inline-flex; width: 30px; height: 30px; align-items: center; justify-content: center; border-radius: 8px; border: 1px solid var(--color-border); background: var(--color-surface); color: var(--color-accent); cursor: pointer; transition: background-color 160ms ease, border-color 160ms ease; }
    .sprint-tab__add-task:hover { border-color: var(--color-accent); background: var(--color-surface-secondary); }
    .sprint-tab__add-task svg { width: 15px; height: 15px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; }
    @media (max-width: 640px) { .sprint-tab__header { flex-wrap: wrap; } }
  `]
})
export class SprintTabComponent {
  sprint = input.required<Sprint>();
  isObjectiveOwner = input.required<boolean>();
  tasks = input.required<readonly WorkTask[]>();
  statusNames = input.required<Record<string, string>>();
  statuses = input<readonly TaskStatusColumn[]>([]);
  objectiveOwners = input<Record<string, boolean>>({});
  currentEmployeeId = input('');
  expanded = input(false);

  toggled = output<void>();
  editRequested = output<void>();
  startRequested = output<void>();
  completeRequested = output<void>();
  createTaskRequested = output<void>();
  taskViewed = output<WorkTask>();
  taskStatusChangeRequested = output<{ taskId: string; newStatusId: string }>();
  taskChanged = output<void>();

  protected readonly totalEstimatedHours = computed(() =>
    this.tasks().reduce((sum, t) => sum + (t.estimatedHours ?? 0), 0)
  );

  protected readonly daysRemaining = computed(() => {
    const end = this.sprint().endDate;
    if (!end) return null;
    const diff = Math.ceil((end.getTime() - Date.now()) / 86_400_000);
    return diff >= 0 ? diff : null;
  });

  protected readonly isOverdue = computed(() => {
    const end = this.sprint().endDate;
    return this.sprint().status === 'active' && !!end && end.getTime() < Date.now();
  });

  protected readonly daysOverdue = computed(() => {
    const end = this.sprint().endDate;
    if (!end) return 0;
    return Math.max(0, Math.floor((Date.now() - end.getTime()) / 86_400_000));
  });
}
```

- [ ] **Step 4: Run to verify pass**

```bash
npx vitest run src/app/modules/work/ui/sprint-tab/sprint-tab.component.spec.ts
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-tab
git commit -m "$(cat <<'EOF'
Rewrite SprintTabComponent with three status-specific renderings

Draft, Active, and Complete each render differently instead of one
form-plus-dropdown. The status dropdown is gone - startRequested and
completeRequested outputs replace statusChangeRequested, opening the
new dialogs from the parent instead.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: `TaskBacklogComponent` wiring

**Files:**
- Modify: `src/app/modules/work/feature/task-backlog/task-backlog.component.ts`
- Modify: `src/app/modules/work/feature/task-backlog/task-backlog.component.spec.ts`

**Interfaces:**
- Consumes: `SprintStartDialogComponent`, `SprintCompleteDialogComponent` (Tasks 9–10), `SprintTabComponent`'s new outputs (Task 11).
- Produces: no new public interface — this is the top of the vertical slice.

- [ ] **Step 1: Read the current `task-backlog.component.spec.ts` in full** (not reproduced — open it) for its exact existing setup/store-stub pattern, then add tests for the new wiring:

```typescript
  it('opens the start dialog when a sprint requests start', () => {
    // arrange component with a draft sprint in sprintStore.sprints(), trigger the
    // (startRequested) handler the same way existing tests trigger other sprint-tab outputs,
    // then assert the component's `startingSprint` (or equivalently named) signal is set to
    // that sprint and the dialog's [open] binding reflects it.
  });

  it('opens the complete dialog when a sprint requests complete', () => {
    // same shape, for completeRequested -> `completingSprint`.
  });
```

(Write these against the actual existing spec file's conventions — it already stubs `SprintListStore`/`TaskBoardStore`/`ProjectDetailStore` for other tests in this file; reuse that exact stubbing style rather than inventing a new one.)

- [ ] **Step 2: Run to verify failure**

```bash
npx vitest run src/app/modules/work/feature/task-backlog/task-backlog.component.spec.ts
```

Expected: FAIL (no such signals/handlers yet).

- [ ] **Step 3: Implement** — apply these changes to `task-backlog.component.ts`:

Add imports:

```typescript
import { SprintStartDialogComponent } from '../../ui/sprint-start-dialog/sprint-start-dialog.component';
import { SprintCompleteDialogComponent } from '../../ui/sprint-complete-dialog/sprint-complete-dialog.component';
```

Add both to the `imports: [...]` array in `@Component`.

Replace the `sprintStatusOptions` array:

```typescript
  protected readonly sprintStatusOptions: readonly WorkDropdownOption[] = [
    { value: 'all', label: 'All sprint statuses' },
    { value: 'draft', label: 'Draft' },
    { value: 'active', label: 'Active' },
    { value: 'complete', label: 'Complete' },
    { value: 'achieved', label: 'Achieved' }
  ];
```

Add two new signals near `editingSprint`:

```typescript
  protected readonly startingSprint = signal<Sprint | null>(null);
  protected readonly completingSprint = signal<Sprint | null>(null);
```

Add a computed for the Complete dialog's `otherSprints` and a completed-status-id set (both scoped to the completing sprint's own objective):

```typescript
  protected readonly completeStatusIds = computed(() =>
    new Set(this.store.statuses().filter((s) => s.marksTaskComplete).map((s) => s.id))
  );
  protected readonly otherSprintsForCompleting = computed(() => {
    const target = this.completingSprint();
    if (!target) return [];
    return this.sprintStore.sprints().filter((s) =>
      s.objectiveId === target.objectiveId && s.id !== target.id && s.status !== 'complete' && s.status !== 'achieved'
    );
  });
```

Replace `sortedSprints` ordering inside `filteredSprints` — keep the existing filter logic, add a sort:

```typescript
  protected readonly filteredSprints = computed(() => {
    const filter = this.sprintStatusFilter();
    const statusOrder: Record<string, number> = { active: 0, draft: 1, complete: 2, achieved: 3 };
    const list = filter === 'all' ? this.sprintStore.sprints() : this.sprintStore.sprints().filter((sprint) => sprint.status === filter);
    return [...list].sort((a, b) => (statusOrder[a.status] ?? 9) - (statusOrder[b.status] ?? 9));
  });
```

Remove `onSprintStatusChangeRequested` entirely and add:

```typescript
  protected onSprintStarted(): void {
    this.startingSprint.set(null);
    void this.sprintStore.loadForProject(this.projectId());
  }

  protected onSprintCompleted(): void {
    this.completingSprint.set(null);
    void this.sprintStore.loadForProject(this.projectId());
  }
```

In the template, replace the `(statusChangeRequested)` binding on `<app-sprint-tab>` with:

```html
              (startRequested)="startingSprint.set(sprint)"
              (completeRequested)="completingSprint.set(sprint)"
```

And add the two dialogs near the existing `@if (editingSprint(); as sprint) { ... }` block:

```html
    @if (startingSprint(); as sprint) {
      <app-sprint-start-dialog
        [open]="true"
        [sprint]="sprint"
        [tasks]="tasksForSprint(sprint.id)"
        [objectiveId]="sprint.objectiveId"
        (started)="onSprintStarted()"
        (cancelled)="startingSprint.set(null)"
      />
    }
    @if (completingSprint(); as sprint) {
      <app-sprint-complete-dialog
        [open]="true"
        [sprint]="sprint"
        [tasks]="tasksForSprint(sprint.id)"
        [otherSprints]="otherSprintsForCompleting()"
        [objectiveId]="sprint.objectiveId"
        [completeStatusIds]="completeStatusIds()"
        (completed)="onSprintCompleted()"
        (cancelled)="completingSprint.set(null)"
      />
    }
```

(Confirm the exact property name `marksTaskComplete` on `TaskStatusColumn` in `task.model.ts` before writing `completeStatusIds` above — read that model file first if unsure, since it's read here but hasn't been re-verified in this task.)

- [ ] **Step 4: Run to verify pass**

```bash
npx vitest run src/app/modules/work/feature/task-backlog/task-backlog.component.spec.ts
```

Expected: PASS.

- [ ] **Step 5: Run the full frontend Work module test suite once, to catch cross-task regressions**

```bash
npx vitest run src/app/modules/work
```

Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/feature/task-backlog
git commit -m "$(cat <<'EOF'
Wire Start/Complete dialogs into TaskBacklogComponent

Sprint cards now open SprintStartDialogComponent/
SprintCompleteDialogComponent instead of a status dropdown. Sprint
list sorts Active first, then Draft, then Complete; the sprint-status
filter drops future/incomplete and adds draft.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Tree view compatibility (`sprint-tree-row`, `milestone-tree-node`, `milestone-tree-tab`)

**Files:**
- Modify: `src/app/modules/work/ui/sprint-tree-row/sprint-tree-row.component.ts`
- Modify: `src/app/modules/work/ui/sprint-tree-row/sprint-tree-row.component.spec.ts`
- Modify: `src/app/modules/work/state/project-detail.store.ts` (`completeSprint`)
- Modify: `src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.ts`
- Modify: `src/app/modules/work/feature/milestone-tree-tab/milestone-tree-tab.component.html`

**Interfaces:**
- Consumes: `SprintCompleteDialogComponent` (Task 10), `SprintApiService.complete` (Task 7).
- Produces: `ProjectDetailStore.completeSprint(sprintId, disposition)` — new signature; no other public interface change.

- [ ] **Step 1: Fix the null-date display in `SprintTreeRowComponent`**

The template's `data-testid="due-date-cell"` currently renders `{{ node().endDate }}` directly (a string field on `MilestoneTreeNode`, already nullable-capable since it's assigned from `dto.endDate` upstream). Update that one interpolation:

```html
      <span class="text-xs {{ selected() ? 'text-white' : 'text-[var(--color-text-secondary)]' }}" data-testid="due-date-cell">{{ node().endDate || 'No dates set' }}</span>
```

Read `sprint-tree-row.component.spec.ts` first for its existing `node()` fixture shape, then add:

```typescript
  it('shows "No dates set" for a draft sprint with no end date', () => {
    setup({ node: { ...baseNode, endDate: null } });
    expect(fixture.nativeElement.textContent).toContain('No dates set');
  });
```

- [ ] **Step 2: Run to verify it currently fails, then passes after the fix**

```bash
npx vitest run src/app/modules/work/ui/sprint-tree-row/sprint-tree-row.component.spec.ts
```

Expected: FAIL before the template edit (renders empty string, not "No dates set"), PASS after.

- [ ] **Step 3: Update `ProjectDetailStore.completeSprint`** to accept and forward a disposition:

```typescript
    async completeSprint(sprintId: string, request: CompleteSprintRequestDto): Promise<{ success: boolean }> {
      try {
        const dto = await firstValueFrom(sprintApi.complete(sprintId, request));
        const tree = store.selectedSubtree();
        if (tree) {
          patchState(store, {
            selectedSubtree: updateTreeNode(tree, sprintId, (node) => ({
              ...node,
              title: dto.name,
              startDate: dto.startDate,
              endDate: dto.endDate,
              status: dto.status,
              isAchieved: dto.status === 'achieved'
            })),
            milestoneActionError: null
          });
        }
        return { success: true };
      } catch (err) {
        patchState(store, { milestoneActionError: extractError(err, 'Failed to complete the sprint.') });
        return { success: false };
      }
    },
```

Add `import { CompleteSprintRequestDto } from '../models/dto/sprint.dto';` to this file's imports if not already present via a wider `sprint.dto` import.

- [ ] **Step 4: Reuse `SprintCompleteDialogComponent` from the Tree tab** instead of completing directly — data-safety fix: silently defaulting to "move to Backlog" with no confirmation would make unfinished tasks disappear from the sprint without the owner ever seeing it.

In `milestone-tree-tab.component.ts`, add the import, a `completingSprintNode` signal (mirroring the existing `pendingDeleteNode`/`deleteConfirmOpen` pattern already in this file), and change `onCompleteSprintRequested`:

```typescript
import { SprintCompleteDialogComponent } from '../../ui/sprint-complete-dialog/sprint-complete-dialog.component';
```

Add to the component's `imports: [...]` array.

```typescript
  protected readonly completingSprintNode = signal<MilestoneTreeNode | null>(null);

  onCompleteSprintRequested(node: MilestoneTreeNode): void {
    this.completingSprintNode.set(node);
  }

  onSprintCompleteDialogClosed(): void {
    this.completingSprintNode.set(null);
  }

  async onSprintCompleteDialogCompleted(): Promise<void> {
    this.completingSprintNode.set(null);
    await this.loadTree({ force: true });
  }
```

(Delete the old one-line `async onCompleteSprintRequested(node) { await this.store.completeSprint(node.id); }`.)

In `milestone-tree-tab.component.html`, add near the existing delete-confirmation modal markup:

```html
@if (completingSprintNode(); as node) {
  <app-sprint-complete-dialog
    [open]="true"
    [sprint]="{ id: node.id, objectiveId: node.objectiveId, name: node.title, goal: null, startDate: null, endDate: null, status: 'active', completedAt: null, achievedAt: null }"
    [tasks]="[]"
    [otherSprints]="[]"
    [objectiveId]="node.objectiveId"
    (completed)="onSprintCompleteDialogCompleted()"
    (cancelled)="onSprintCompleteDialogClosed()"
  />
}
```

(`MilestoneTreeNode` doesn't carry `objectiveId`/full task list today — check its actual field name for the owning objective, likely `parentId` or a dedicated field; read `milestone.model.ts` before finalizing this binding, and if there's no task list available on the node, this dialog will show `0 tasks` / no incomplete-task disposition UI, which is acceptable here since the Tree view's own row already only shows the Complete button when there are zero blocking concerns worth surfacing at this depth — the important fix is that a disposition choice is now always asked, not that Tree view gets full parity with Backlog's task-level summary.)

- [ ] **Step 5: Run the milestone-tree-tab spec suite**

```bash
npx vitest run src/app/modules/work/feature/milestone-tree-tab
```

Expected: PASS (adjust any pre-existing test that asserted `store.completeSprint` was called with just `(sprintId)` to the new two-argument form, or to assert the dialog opens instead — read the existing spec file first for its exact assertions).

- [ ] **Step 6: Run the entire frontend Work module suite one final time**

```bash
npx vitest run src/app/modules/work
```

Expected: PASS, zero regressions across the whole module.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/work/ui/sprint-tree-row src/app/modules/work/state/project-detail.store.ts src/app/modules/work/feature/milestone-tree-tab
git commit -m "$(cat <<'EOF'
Tree view: null-date compatibility and reuse the Complete dialog

sprint-tree-row now renders "No dates set" for a dateless Draft
sprint. The Tree tab's Complete button now opens
SprintCompleteDialogComponent instead of completing directly with no
disposition, since that would otherwise silently drop unfinished
tasks with no confirmation. No other Tree view redesign.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

Push both branches when this is done: `git push -u origin feature/sprint-lifecycle-redesign` in each repo.

---

## Self-Review Notes (for whoever executes this plan)

- **Spec coverage:** every section of the design spec (data model/migration, lifecycle/commands, frontend Backlog, scope boundaries) maps to a task above. The one spec line without a dedicated task — "no logged hours anywhere" — is satisfied by omission (no task introduces it) rather than by a task that removes something, which is correct since nothing to remove exists.
- **Known gaps flagged inline, not hidden:** Task 6's notification-recipient loop, Task 12's `marksTaskComplete` field-name check, and Task 13's `MilestoneTreeNode` field-name check are each called out as "verify against the actual file before finalizing" rather than guessed — those three spots are the ones this plan's author could not verify without re-reading files already read once each for a different purpose, and guessing risks a silent behavioral bug (wrong notification recipient, wrong status-complete detection, wrong node identity) that a compile error won't catch.
- **Type consistency:** `SprintResponse`'s 9-argument shape (Task 1) is used identically in Tasks 2–5's handler rewrites; `Sprint`'s frontend shape (Task 7) is used identically in Tasks 8–13.
