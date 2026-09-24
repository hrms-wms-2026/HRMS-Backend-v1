# Work Management — Task Status Governance (Part 1 of 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Backend foundation for Task Status Public/Private visibility, an eagerly-seeded default
status template on every Objective, owner-driven dynamic status customization (create/edit/delete),
the missing `Objective.CompletedHours`/`WorkTask.CompletedHours` rollup, and the two confirmed
authorization gaps (`AssignTask`, `MoveTaskStatus`) — all prerequisites Sprint completion (Part 2)
depends on.

**Architecture:** Extends the existing `TaskStatus` entity with a `Visibility` field, adds a shared
`DefaultTaskStatusTemplate` helper consumed by both Project and Objective creation so the eager-seed
logic isn't duplicated, adds the missing Create/Delete TaskStatus commands following the existing
Edit command's exact pattern, and closes the two authorization gaps using the same
`ICallerIdentityResolver`/`IMilestoneMembershipCoordinator` seam every other Work Management handler
already uses.

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL, snake_case columns), MediatR CQRS, FluentValidation, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-17-work-management-sprint-foundation-design.md`

## Global Constraints

- Work Management module only — do not touch `organization`, `layouts/main-layout`, or any other module.
- Dev/Test-only seeders are out of scope for this plan (backend production code only).
- Existing `TaskStatus` rows must default `Visibility = "public"` on migration — do not retroactively
  guess Private for any existing tenant's custom statuses (spec, Data model section).
- `Sprint`-related freezing of tasks (Achieved sprints) is **not** part of this plan — that's added in
  Part 2, once the `Sprint` entity exists. Do not add any Sprint references here.
- Every new owner-only check follows the exact existing pattern: resolve caller's EmployeeId via
  `ICallerIdentityResolver`, compare against `objective.OwnerId`, return `Result.Forbidden(...)` on
  mismatch — do not invent a different authorization mechanism.

---

### Task 1: `TaskStatus.Visibility` field + migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20260817000001_AddTaskStatusVisibility.cs` (+ `.Designer.cs`, via `dotnet ef migrations add`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusConfigurationTests.cs` (extend)

**Interfaces:**
- Produces: `public static class TaskStatusVisibilities { public const string Public = "public"; public const string Private = "private"; }` and `TaskStatus.Visibility` (`string`, default `"public"`) — consumed by every task in this plan and by Part 2/3/4.

- [ ] **Step 1: Read the existing test file to match its style**

Run: nothing to run — just open `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusConfigurationTests.cs` and note its existing assertions before extending it, so the new one matches the file's established pattern (EF model/configuration assertions, not handler behavior).

- [ ] **Step 2: Write the failing test**

Append to `TaskStatusConfigurationTests.cs`:

```csharp
    [Fact]
    public void TaskStatus_DefaultsVisibilityToPublic()
    {
        var status = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "Custom", CreatedAt = DateTimeOffset.UtcNow };

        Assert.Equal(TaskStatusVisibilities.Public, status.Visibility);
    }
```

Add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;` if not already present (needed for `TaskStatusVisibilities`).

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~TaskStatusConfigurationTests`
Expected: FAIL to compile — `TaskStatusVisibilities` and `TaskStatus.Visibility` don't exist yet.

- [ ] **Step 4: Add the entity field and visibility constants**

In `TaskStatus.cs`, add above the class:

```csharp
public static class TaskStatusVisibilities
{
    public const string Public = "public";
    public const string Private = "private";
}
```

Add to the `TaskStatus` class:

```csharp
    public string Visibility { get; set; } = TaskStatusVisibilities.Public;
```

- [ ] **Step 5: Run to verify the test passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~TaskStatusConfigurationTests`
Expected: PASS.

- [ ] **Step 6: Add the EF configuration constraint**

In `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs`, add
inside `Configure`:

```csharp
        builder.Property(s => s.Visibility).HasMaxLength(20).IsRequired().HasDefaultValue(TaskStatusVisibilities.Public);
```

Add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;` (for `TaskStatusVisibilities`) if not
already present in this file.

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add AddTaskStatusVisibility --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations`

Expected: generates a migration whose `Up()` contains
`migrationBuilder.AddColumn<string>(name: "visibility", table: "task_statuses", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "public");`
— confirm this matches the `AddColumn`-before-any-index convention seen in
`20260807114059_AddObjectiveAndProjectAchievedState.cs`. If the tool names the migration file
differently, rename it to `20260817000001_AddTaskStatusVisibility.cs` for chronological ordering
with this plan's other migrations (Part 2 adds more).

- [ ] **Step 8: Apply the migration to the dev database and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: succeeds, no errors. Spot-check: `SELECT visibility FROM task_statuses LIMIT 1;` returns `public` for existing rows.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusConfigurationTests.cs
git commit -m "feat(work): add TaskStatus.Visibility (Public/Private) field"
```

---

### Task 2: Shared default-template helper, wired into Project and Objective creation

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskStatusTemplate.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs` (new), extend `CreateObjectiveCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusVisibilities` (Task 1).
- Produces: `public static class DefaultTaskStatusTemplate { public static List<TaskStatusEntity> BuildRows(Guid tenantId, Guid projectId, Guid? objectiveId, Guid createdById, DateTimeOffset now); }` — a 4-row list (`To Do`[Public], `In Process`[Public], `Review`[Public], `Done`[Private, `MarksTaskComplete=true`]), each with a fresh `Guid.NewGuid()` id. Consumed by `CreateProjectCommandHandler` (twice — once for the project template with `objectiveId: null`, once for the default Objective's own copy) and `CreateObjectiveCommandHandler` (once, for the new sub-Objective).

- [ ] **Step 1: Write the failing test for the helper**

Create `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskStatusTemplateTests
{
    [Fact]
    public void BuildRows_Returns4RowsInDisplayOrderWithCorrectVisibilityAndCompletionFlag()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var objectiveId = Guid.NewGuid();
        var createdById = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rows = DefaultTaskStatusTemplate.BuildRows(tenantId, projectId, objectiveId, createdById, now);

        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(tenantId, r.TenantId));
        Assert.All(rows, r => Assert.Equal(projectId, r.ProjectId));
        Assert.All(rows, r => Assert.Equal(objectiveId, r.ObjectiveId));

        var ordered = rows.OrderBy(r => r.DisplayOrder).ToList();
        Assert.Equal(new[] { "To Do", "In Process", "Review", "Done" }, ordered.Select(r => r.Name));
        Assert.Equal(new[] { 0, 1, 2, 3 }, ordered.Select(r => r.DisplayOrder));
        Assert.Equal(
            new[] { TaskStatusVisibilities.Public, TaskStatusVisibilities.Public, TaskStatusVisibilities.Public, TaskStatusVisibilities.Private },
            ordered.Select(r => r.Visibility));
        Assert.Equal(new[] { false, false, false, true }, ordered.Select(r => r.MarksTaskComplete));
    }

    [Fact]
    public void BuildRows_NullObjectiveId_ProducesProjectLevelTemplateRows()
    {
        var rows = DefaultTaskStatusTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), objectiveId: null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.All(rows, r => Assert.Null(r.ObjectiveId));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DefaultTaskStatusTemplateTests`
Expected: FAIL to compile — `DefaultTaskStatusTemplate` doesn't exist yet.

- [ ] **Step 3: Implement the helper**

```csharp
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskStatusTemplate
{
    public static List<TaskStatusEntity> BuildRows(
        Guid tenantId, Guid projectId, Guid? objectiveId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskStatusEntity>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "In Process", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Review", DisplayOrder = 2, Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedById = createdById, CreatedAt = now }
        };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DefaultTaskStatusTemplateTests`
Expected: PASS (both tests).

- [ ] **Step 5: Wire into `CreateProjectCommandHandler`**

In `CreateProjectCommandHandler.cs`, replace the inline array (currently lines ~252-258) that builds
the 4 project-template `TaskStatusEntity` rows:

```csharp
            await _taskStatuses.AddRangeAsync(new TaskStatusEntity[]
            {
                new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, Name = "To Do", DisplayOrder = 0, CreatedById = userId, CreatedAt = now },
                new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, Name = "In Process", DisplayOrder = 1, CreatedById = userId, CreatedAt = now },
                new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, Name = "Review", DisplayOrder = 2, CreatedById = userId, CreatedAt = now },
                new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, CreatedById = userId, CreatedAt = now }
            }, ct);
```

with two calls — one for the project-level template (`objectiveId: null`), one for the default
Objective's own eager copy (`objectiveId: defaultObjective.Id`):

```csharp
            await _taskStatuses.AddRangeAsync(
                DefaultTaskStatusTemplate.BuildRows(tenantId, project.Id, objectiveId: null, userId, now), ct);
            await _taskStatuses.AddRangeAsync(
                DefaultTaskStatusTemplate.BuildRows(tenantId, project.Id, objectiveId: defaultObjective.Id, userId, now), ct);
```

Add `using ONEVO.Application.Features.WorkManagement.Tasks.Services;` to this file's usings if not
already present.

- [ ] **Step 6: Wire into `CreateObjectiveCommandHandler`**

In `CreateObjectiveCommandHandler.cs`, this handler needs an `ITaskStatusRepository` dependency it
doesn't have today. Add it to the constructor:

```csharp
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IPermissionAutoGrantService _autoGrant;
    private readonly IProjectMemberInvitationRepository _invitations;
    private readonly ITaskStatusRepository _taskStatuses;

    public CreateObjectiveCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership, IPermissionAutoGrantService autoGrant,
        IProjectMemberInvitationRepository invitations, ITaskStatusRepository taskStatuses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _unitOfWork = unitOfWork;
        _membership = membership;
        _autoGrant = autoGrant;
        _invitations = invitations;
        _taskStatuses = taskStatuses;
    }
```

Add `using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;` and
`using ONEVO.Application.Features.WorkManagement.Tasks.Services;` to this file's usings.

Then, right after `await _objectives.AddAsync(objective, innerCt);` (inside the transaction), add:

```csharp
            await _taskStatuses.AddRangeAsync(
                DefaultTaskStatusTemplate.BuildRows(tenantId, objective.ProjectId, objectiveId: objective.Id, userId, now), innerCt);
```

- [ ] **Step 7: Write the failing test for `CreateObjectiveCommandHandler`'s new seeding**

Open `tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/CreateObjectiveCommandHandlerTests.cs`
(if it doesn't exist under this exact path, locate it via
`find tests/ONEVO.Tests.Unit -iname "CreateObjectiveCommandHandlerTests.cs"` and use that path instead).
Add a mock `Mock<ITaskStatusRepository>` to the handler-construction helper used by the file's existing
tests (matching however that file already builds the handler — same pattern as
`AssignTaskCommandHandlerTests.cs`'s `Build(...)` helper shown in this plan's Task 8), then add:

```csharp
    [Fact]
    public async Task Handle_HappyPath_SeedsDefaultTaskStatusTemplateForNewObjective()
    {
        // Arrange using this file's existing Build(...)-style helper, with a valid parent Objective
        // the caller owns (reuse whatever fixture the file's other passing tests already use).

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        taskStatuses.Verify(x => x.AddRangeAsync(
            It.Is<IReadOnlyList<TaskStatusEntity>>(rows => rows.Count == 4 && rows.All(r => r.ObjectiveId == result.Value!.Id)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

Adjust the exact assertion/arrange to match this file's existing conventions (constants for
`TenantId`/`ParentObjectiveId`/etc. already declared at the top of the file) — read the file first,
then add this test consistently with what's already there rather than inventing new fixture names.

- [ ] **Step 8: Run to verify it fails, then implement, then verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateObjectiveCommandHandlerTests`
Expected: FAIL first (constructor signature mismatch / missing mock setup), then PASS after Step 6's
implementation and the test file's mock is added.

- [ ] **Step 9: Update DI registration if `CreateObjectiveCommandHandler` or `ITaskStatusRepository` aren't already both registered together**

Check `src/ONEVO.Infrastructure/DependencyInjection.cs` — `ITaskStatusRepository` should already be
registered (it's used by other handlers). No new registration needed; MediatR handler constructor
injection picks up the new dependency automatically. Just run a full build to confirm no DI resolution
errors: `dotnet build src/ONEVO.Api`.

- [ ] **Step 10: Run the full WorkManagement test filter to check nothing else broke**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS, including `CreateProjectCommandHandlerTests` (find it via
`find tests/ONEVO.Tests.Unit -iname "CreateProjectCommandHandlerTests.cs"` if its existing assertions
on the 4-status seed need updating for the new `Visibility` field — if that test asserts exact row
shapes, extend those assertions to also check `Visibility`, don't just make it pass by weakening it).

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskStatusTemplate.cs src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Objectives/
git commit -m "feat(work): eagerly seed the default Public/Public/Public/Private task status template at Objective creation"
```

---

### Task 3: Lazy-copy fallback carries `Visibility` too

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/GetObjectiveTaskStatusesQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`TaskStatusViewModel`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTaskStatusesQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatus.Visibility` (Task 1).
- Produces: `TaskStatusResponse` and `TaskStatusViewModel` both gain a `Visibility` (`string`) member,
  consumed by the frontend in Part 3.

- [ ] **Step 1: Write the failing test**

Read `GetObjectiveTaskStatusesQueryHandlerTests.cs` first to match its existing style, then add:

```csharp
    [Fact]
    public async Task Handle_NoExistingObjectiveStatuses_CopiesTemplateIncludingVisibility()
    {
        // Arrange: reuse this file's existing "copies from project template" test fixture, but give
        // the project template a row with Visibility = TaskStatusVisibilities.Private (e.g. "Done").

        var result = await handler.Handle(new GetObjectiveTaskStatusesQuery(objectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var done = result.Value!.Single(s => s.Name == "Done");
        Assert.Equal(TaskStatusVisibilities.Private, done.Visibility);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetObjectiveTaskStatusesQueryHandlerTests`
Expected: FAIL to compile — `TaskStatusResponse`/the query handler don't expose `Visibility` yet.

- [ ] **Step 3: Add `Visibility` to `TaskStatusResponse`**

In `TaskStatusResponse.cs`, find the record (likely
`public sealed record TaskStatusResponse(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete);`)
and add `string Visibility` as a parameter:

```csharp
public sealed record TaskStatusResponse(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete, string Visibility);
```

- [ ] **Step 4: Update the query handler's copy logic and `ToResponses` mapping**

In `GetObjectiveTaskStatusesQueryHandler.cs`, the `copies` projection (currently
`Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = request.ObjectiveId, Name = t.Name, DisplayOrder = t.DisplayOrder, RequiresApproval = t.RequiresApproval, MarksTaskComplete = t.MarksTaskComplete, CreatedById = _currentUser.UserId, CreatedAt = now`)
add `Visibility = t.Visibility` to it. And in `ToResponses`, add `s.Visibility` to the
`TaskStatusResponse` construction.

- [ ] **Step 5: Update `TaskStatusViewModel` and its mapper**

In `TaskContracts.cs`, extend
`public sealed record TaskStatusViewModel(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete);`
to add `string Visibility`, and update its `ToViewModel()` mapper extension method (search this file
or a nearby mapper file for where `TaskStatusResponse -> TaskStatusViewModel` is mapped) to pass
`.Visibility` through.

- [ ] **Step 6: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~TaskStatus`
Expected: all PASS. Also run a full build to catch any other place constructing `TaskStatusResponse`
positionally that now needs the extra argument: `dotnet build src/ONEVO.Api` — fix any compile errors
by adding `.Visibility` at each remaining construction site (there should be very few; `TaskStatusResponse`
is only constructed by this query handler).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/GetObjectiveTaskStatusesQueryHandler.cs src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTaskStatusesQueryHandlerTests.cs
git commit -m "feat(work): carry Visibility through the task-status lazy-copy and API response"
```

---

### Task 4: `CreateTaskStatusCommand` (new — owner can add custom statuses)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusVisibilities` (Task 1), same `IObjectiveRepository`/`ICallerIdentityResolver`
  owner-check pattern as `EditTaskStatusCommandHandler`.
- Produces: `CreateTaskStatusCommand(Guid ObjectiveId, string Name, int DisplayOrder, string Visibility, bool MarksTaskComplete, bool RequiresApproval, Guid? ApproverId) : IRequest<Result<TaskStatusResponse>>`
  — consumed by the controller (Task 9) and the frontend (Part 3).

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(Guid callerEmployeeId)
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

        var statuses = new Mock<ITaskStatusRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, statuses.Object, unitOfWork.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_Owner_CreatesStatus()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ObjectiveId, "Blocked", 4, TaskStatusVisibilities.Public, false, false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value!.Name);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "Blocked" && s.ObjectiveId == ObjectiveId && s.Visibility == TaskStatusVisibilities.Public), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new CreateTaskStatusCommand(ObjectiveId, "Blocked", 4, TaskStatusVisibilities.Public, false, false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskStatusCommandHandlerTests`
Expected: FAIL to compile — none of the new types exist yet.

- [ ] **Step 3: Write the command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public sealed record CreateTaskStatusCommand(
    Guid ObjectiveId, string Name, int DisplayOrder, string Visibility, bool MarksTaskComplete,
    bool RequiresApproval, Guid? ApproverId
) : IRequest<Result<TaskStatusResponse>>;
```

- [ ] **Step 4: Write the validator**

```csharp
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandValidator : AbstractValidator<CreateTaskStatusCommand>
{
    public CreateTaskStatusCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty).WithMessage("Objective is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
    }
}
```

- [ ] **Step 5: Write the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandHandler : IRequestHandler<CreateTaskStatusCommand, Result<TaskStatusResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskStatusResponse>> Handle(CreateTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskStatusResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskStatusResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<TaskStatusResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<TaskStatusResponse>.Forbidden("Only this milestone's owner can create task statuses.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var status = new TaskStatusEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                Name = request.Name.Trim(), DisplayOrder = request.DisplayOrder, Visibility = request.Visibility,
                MarksTaskComplete = request.MarksTaskComplete, RequiresApproval = request.RequiresApproval,
                ApproverId = request.ApproverId, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _statuses.AddAsync(status, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusResponse>.Success(new TaskStatusResponse(
                status.Id, status.Name, status.DisplayOrder, status.RequiresApproval, status.ApproverId,
                status.MarksTaskComplete, status.Visibility));
        }, ct);
    }
}
```

- [ ] **Step 6: Run to verify both tests pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateTaskStatusCommandHandlerTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): CreateTaskStatusCommand - milestone owner can add custom task statuses"
```

---

### Task 5: `DeleteTaskStatusCommand` (new — owner can remove a custom status)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs` (find via `find src/ONEVO.Infrastructure -iname "EfWorkTaskRepository.cs"`)
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Produces: `ITaskStatusRepository.Remove(TaskStatusEntity status)`, `IWorkTaskRepository.AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default)`, `DeleteTaskStatusCommand(Guid StatusId) : IRequest<Result>`.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    private (DeleteTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(bool anyTasksInStatus)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);

        var status = new TaskStatusEntity { Id = StatusId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "Blocked", CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(anyTasksInStatus);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, statuses.Object, tasks.Object, unitOfWork.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_NoTasksInStatus_RemovesIt()
    {
        var (handler, statuses) = Build(anyTasksInStatus: false);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Remove(It.Is<TaskStatusEntity>(s => s.Id == StatusId)), Times.Once);
    }

    [Fact]
    public async Task Handle_TasksStillInStatus_ReturnsConflict()
    {
        var (handler, statuses) = Build(anyTasksInStatus: true);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DeleteTaskStatusCommandHandlerTests`
Expected: FAIL to compile.

- [ ] **Step 3: Add the repository methods**

In `ITaskStatusRepository.cs`, add: `void Remove(TaskStatusEntity status);`
In `EfTaskStatusRepository.cs`, add: `public void Remove(TaskStatusEntity status) => _db.TaskStatuses.Remove(status);`

In `IWorkTaskRepository.cs`, add:
```csharp
    /// <summary>True if any active WorkTask currently has this StatusId - used to block deleting a
    /// status that's still in use rather than silently orphaning tasks' FK.</summary>
    Task<bool> AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default);
```
In `EfWorkTaskRepository.cs` (locate via `find src/ONEVO.Infrastructure -iname "EfWorkTaskRepository.cs"`
and read it first to match its existing query style — likely filters `!t.IsDeleted` the same way its
other methods do), add:
```csharp
    public async Task<bool> AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default)
        => await _db.WorkTasks.AnyAsync(t => t.TenantId == tenantId && t.StatusId == statusId && !t.IsDeleted, ct);
```
(Adjust the exact filter/DbSet-access style to match whatever convention the rest of that file already
uses — this plan cannot see that file's exact current contents; read it before editing.)

- [ ] **Step 4: Write the command and handler**

```csharp
// DeleteTaskStatusCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;

public sealed record DeleteTaskStatusCommand(Guid StatusId) : IRequest<Result>;
```

```csharp
// DeleteTaskStatusCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;

public class DeleteTaskStatusCommandHandler : IRequestHandler<DeleteTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        ITaskStatusRepository statuses, IWorkTaskRepository tasks, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _statuses = statuses;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is null)
            return Result.NotFound("Task status not found.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, status.ObjectiveId.Value, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's owner can delete task statuses.");

        if (await _tasks.AnyActiveByStatusIdAsync(tenantId, status.Id, ct))
            return Result.Conflict("Move all tasks out of this status before deleting it.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _statuses.Remove(status);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 5: Run to verify both tests pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DeleteTaskStatusCommandHandlerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): DeleteTaskStatusCommand - milestone owner can remove unused custom statuses"
```

---

### Task 6: Extend `EditTaskStatusCommand` with `Visibility`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandValidator.cs`
  (find it via `find src/ONEVO.Application -iname "EditTaskStatusCommandValidator.cs"`; create it here
  in this same style if it doesn't already exist)
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`EditTaskStatusRequest`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskStatusCommandHandlerTests.cs`
  (find/extend via `find tests/ONEVO.Tests.Unit -iname "EditTaskStatusCommandHandlerTests.cs"`)

- [ ] **Step 1: Write the failing test**

Read the existing `EditTaskStatusCommandHandlerTests.cs` first to match its fixture style, then add:

```csharp
    [Fact]
    public async Task Handle_Owner_UpdatesVisibility()
    {
        // Arrange using this file's existing Build(...)-style helper and owner fixture.
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Visibility == TaskStatusVisibilities.Private)), Times.Once);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EditTaskStatusCommandHandlerTests`
Expected: FAIL to compile — `EditTaskStatusCommand` doesn't accept a `Visibility` argument yet.

- [ ] **Step 3: Extend the command**

In `EditTaskStatusCommand.cs`, change
`public sealed record EditTaskStatusCommand(Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId) : IRequest<Result>;`
to add `string Visibility` as the last parameter:

```csharp
public sealed record EditTaskStatusCommand(Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, string Visibility) : IRequest<Result>;
```

- [ ] **Step 4: Update the handler**

In `EditTaskStatusCommandHandler.cs`, inside the transaction block, add `status.Visibility = request.Visibility;`
alongside the existing `status.Name = ...`/`status.DisplayOrder = ...` assignments.

- [ ] **Step 5: Add/extend the validator**

If `EditTaskStatusCommandValidator.cs` already exists, add this rule; if it doesn't exist yet, create
it following `CreateTaskStatusCommandValidator`'s exact style from Task 4:

```csharp
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
```

- [ ] **Step 6: Update the API contract and controller call site**

In `TaskContracts.cs`, extend
`public sealed record EditTaskStatusRequest(string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId);`
to add `string Visibility`.

In `TasksController.cs`'s `EditStatus` action, update the command construction:
```csharp
        var result = await _mediator.Send(new EditTaskStatusCommand(id, request.Name, request.DisplayOrder, request.RequiresApproval, request.ApproverId, request.Visibility), ct);
```

- [ ] **Step 7: Run to verify all pass, then full build**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EditTaskStatusCommandHandlerTests`
then `dotnet build src/ONEVO.Api` to catch any other construction site of `EditTaskStatusCommand`
that now needs the extra argument.
Expected: all PASS, build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): EditTaskStatusCommand can now update Visibility"
```

---

### Task 7: `MoveTaskStatusCommandHandler` — authorization + `CompletedHours` rollup

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IMilestoneMembershipCoordinator.IsActiveMemberAsync(tenantId, objectiveId, employeeId, ct)`
  (already exists, confirmed signature).
- Produces (if not already present from a separate, earlier plan —
  check `IObjectiveRepository.cs` first): `Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)`.
  **Skip this specific addition if the method already exists** (it may have been added by the
  separately-tracked `2026-08-17-work-management-allocation-overcommit-fix.md` plan) — check before
  adding a duplicate.

- [ ] **Step 1: Check whether `IObjectiveRepository.GetTrackedByIdForTenantAsync` already exists**

Run: `grep -n "GetTrackedByIdForTenantAsync" src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
If it prints a match, skip Step 2 entirely. If it prints nothing, do Step 2.

- [ ] **Step 2 (conditional): Add `GetTrackedByIdForTenantAsync` to `IObjectiveRepository`/`EfObjectiveRepository`**

In `IObjectiveRepository.cs`, add:
```csharp
    /// <summary>
    /// Same lookup as <see cref="GetByIdForTenantAsync"/>, but returns the entity tracked by the
    /// DbContext's change tracker instead of AsNoTracking. Use on write paths that later mutate the
    /// entity directly - tracking it from the start lets EF's identity map correctly deduplicate
    /// against any other tracked query that touches the same row later in the same request.
    /// </summary>
    Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
```
In `EfObjectiveRepository.cs`, add:
```csharp
    public async Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Objectives
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, ct);
    }
```

- [ ] **Step 3: Write the failing tests**

Read `MoveTaskStatusCommandHandlerTests.cs` (shown in full in this plan's research) first. Replace its
existing `Handle_ValidMove_UpdatesStatus` test's `Build`-equivalent setup (it currently constructs the
handler inline per-test with only 4 constructor args) with a shared `Build(...)` helper matching the
style of `AssignTaskCommandHandlerTests.cs`, since the handler's constructor is changing shape. Then:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class MoveTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid OutsiderEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OldStatusId = Guid.NewGuid();
    private static readonly Guid NewStatusId = Guid.NewGuid();

    private (MoveTaskStatusCommandHandler Handler, Objective Objective, WorkTask Task) Build(
        Guid callerEmployeeId, bool callerIsMember, TaskStatusEntity newStatus, decimal? estimatedHours = 8m)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", StatusId = OldStatusId, EstimatedHours = estimatedHours, CreatedAt = DateTimeOffset.UtcNow };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var oldStatus = new TaskStatusEntity { Id = OldStatusId, TenantId = TenantId, Name = "To Do", MarksTaskComplete = false, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, NewStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(newStatus);
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(oldStatus);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CompletedHours = 0m, CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsMember);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new MoveTaskStatusCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, statuses.Object, objectives.Object, membership.Object, unitOfWork.Object);
        return (handler, objective, task);
    }

    [Fact]
    public async Task Handle_Owner_MovingIntoPrivateStatus_Succeeds()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(OwnerEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_MemberMovingIntoPublicStatus_Succeeds()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_MemberMovingIntoPrivateStatus_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_NonMember_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(OutsiderEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_RollsUpHoursOntoTaskAndObjective()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(OwnerEmployeeId, callerIsMember: false, newStatus, estimatedHours: 8m);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8m, task.CompletedHours);
        Assert.Equal(8m, objective.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingOutOfCompleteStatus_ReversesTheRollup()
    {
        var newStatus = new TaskStatusEntity { Id = NewStatusId, TenantId = TenantId, Name = "In Process", MarksTaskComplete = false, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, objective, task) = Build(OwnerEmployeeId, callerIsMember: false, newStatus, estimatedHours: 8m);
        // Simulate the task already having been completed under the old status.
        task.CompletedHours = 8m;
        objective.CompletedHours = 8m;
        // Re-point the old status mock to be MarksTaskComplete=true for this test.
        var oldStatusComplete = new TaskStatusEntity { Id = OldStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        // Note: this requires re-wiring the statuses mock for OldStatusId to oldStatusComplete before
        // calling Handle - if Build(...) doesn't expose the statuses mock, extend Build's return tuple
        // to include it (Mock<ITaskStatusRepository> Statuses) so this test can re-Setup it here.

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Equal(0m, objective.CompletedHours);
    }
}
```

Note on the last test: extend `Build(...)`'s return tuple to also return the `Mock<ITaskStatusRepository>`
so `Handle_MovingOutOfCompleteStatus_ReversesTheRollup` can re-`Setup` the old status's
`MarksTaskComplete = true` before calling `Handle` — the version above documents the intent; wire the
actual mock access when implementing this step rather than leaving it as written prose.

- [ ] **Step 4: Run to verify failures**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~MoveTaskStatusCommandHandlerTests`
Expected: FAIL to compile — handler constructor doesn't accept the new dependencies yet.

- [ ] **Step 5: Rewrite the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public class MoveTaskStatusCommandHandler : IRequestHandler<MoveTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IUnitOfWork _unitOfWork;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskStatusRepository statuses, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _statuses = statuses;
        _objectives = objectives;
        _membership = membership;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MoveTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var newStatus = await _statuses.GetByIdForTenantAsync(tenantId, request.NewStatusId, ct);
        if (newStatus is null)
            return Result.NotFound("Target status not found.");

        var objective = await _objectives.GetTrackedByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null)
            return Result.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
        {
            var isMember = await _membership.IsActiveMemberAsync(tenantId, objective.Id, callerEmployeeId.Value, ct);
            if (!isMember)
                return Result.Forbidden("Only active milestone members can move tasks.");
            if (newStatus.Visibility == TaskStatusVisibilities.Private)
                return Result.Forbidden("Only the milestone owner can move a task into this status.");
        }

        var oldStatus = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, ct);
        var wasComplete = oldStatus?.MarksTaskComplete ?? false;
        var willBeComplete = newStatus.MarksTaskComplete;

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.StatusId = newStatus.Id;

            if (!wasComplete && willBeComplete)
            {
                task.CompletedHours = task.EstimatedHours ?? 0m;
                task.CompletedAt = DateTimeOffset.UtcNow;
                task.ProgressPercent = 100;
                objective.CompletedHours += task.CompletedHours;
            }
            else if (wasComplete && !willBeComplete)
            {
                objective.CompletedHours -= task.CompletedHours;
                task.CompletedHours = 0m;
                task.CompletedAt = null;
                task.ProgressPercent = 0;
            }

            task.UpdatedAt = DateTimeOffset.UtcNow;
            objective.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 6: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~MoveTaskStatusCommandHandlerTests`
Expected: PASS (all 6 tests).

- [ ] **Step 7: Full regression check**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS — this handler's constructor changed shape, so check for any other test file
constructing it directly (unlikely, but verify).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "fix(work): MoveTaskStatusCommandHandler now enforces membership/Private-status authorization and rolls up completed hours onto the Objective"
```

---

### Task 8: `AssignTaskCommandHandler` — owner-only fix

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/AssignTaskCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AssignTaskCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IObjectiveRepository.GetByIdForTenantAsync`, `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync` (both already used elsewhere; this handler needs to add an `IObjectiveRepository` dependency it doesn't have today).

- [ ] **Step 1: Write the failing test**

The existing `AssignTaskCommandHandlerTests.cs` (shown in full in this plan's research) builds `task`
without an `ObjectiveId` and doesn't mock `IObjectiveRepository` at all. Update its `Build(...)` helper
to set `ObjectiveId = ObjectiveId` on the task fixture and add an `IObjectiveRepository` mock returning
an `Objective` owned by `OwnerEmployeeId`, then add two new constants and tests:

```csharp
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();

    // In Build(...): add ObjectiveId = ObjectiveId to the WorkTask fixture, add:
    //   var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
    //   var objectives = new Mock<IObjectiveRepository>();
    //   objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
    // and pass objectives.Object into the handler constructor, changing Build's caller-employee-id
    // parameter so existing tests (currently implicitly the owner via CallerEmployeeId) still pass.

    [Fact]
    public async Task Handle_CallerNotObjectiveOwner_ReturnsForbidden()
    {
        // Arrange via an updated Build(...) that lets the test pick a non-owner CallerEmployeeId.
        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow };
        var assignee = new Employee { Id = EmployeeId, TenantId = TenantId, UserId = AssigneeUserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) };
        var (handler, assignments) = Build(task, assignee, callerEmployeeId: Guid.NewGuid() /* not the owner */);

        var result = await handler.Handle(new AssignTaskCommand(TaskId, EmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        assignments.Verify(x => x.AddAsync(It.IsAny<TaskAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Update the existing `Handle_HappyPath_AddsAssignment` and `Handle_TaskNotFound_ReturnsNotFound` and
`Handle_EmployeeNotActive_ReturnsFailure` tests' calls to `Build(...)` to pass `OwnerEmployeeId` as the
caller (since `CallerEmployeeId` in the original file's identity mock must now match the objective's
`OwnerId` for those "happy path" tests to still succeed under the new authorization check) — i.e., have
`identity.Setup(...).ReturnsAsync(OwnerEmployeeId)` for those, keeping a separate outsider id only for
the new Forbidden test.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AssignTaskCommandHandlerTests`
Expected: FAIL — handler doesn't check ownership yet, so the new Forbidden test fails (returns success
instead); compile also fails until `Build(...)`'s signature and the constructor call are updated.

- [ ] **Step 3: Add the ownership check**

In `AssignTaskCommandHandler.cs`, add an `IObjectiveRepository _objectives` field + constructor
parameter, then in `Handle`, right after the existing `task is null` check:

```csharp
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result.Forbidden("Only this milestone's owner can assign tasks.");
```

Add `using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;` to this file's
usings.

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~AssignTaskCommandHandlerTests`
Expected: PASS (all 4 tests).

- [ ] **Step 5: Apply the identical fix to `UnassignTaskCommandHandler`**

Read `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/UnassignTask/UnassignTaskCommandHandler.cs`
and its test file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/UnassignTaskCommandHandlerTests.cs`
first — if it has the same missing-ownership-check gap (likely, given it's the mirror-image command),
apply the same fix with the same test-first process as Steps 1-4 above. If it already has an ownership
check, skip this step and note that in the commit message instead.

- [ ] **Step 6: Full regression check**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/AssignTaskCommandHandler.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/UnassignTask/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/AssignTaskCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/UnassignTaskCommandHandlerTests.cs
git commit -m "fix(work): AssignTask/UnassignTask now require the caller to be the milestone owner"
```

---

### Task 9: Controller wiring for `CreateTaskStatus`/`DeleteTaskStatus`

**Files:**
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`

**Interfaces:**
- Consumes: `CreateTaskStatusCommand` (Task 4), `DeleteTaskStatusCommand` (Task 5).
- Produces: `POST /api/v1/work/objectives/{objectiveId}/task-statuses`,
  `DELETE /api/v1/work/task-statuses/{id}` — consumed by the frontend in Part 3.

- [ ] **Step 1: Add the request contract**

In `TaskContracts.cs`, add:

```csharp
public sealed record CreateTaskStatusRequest(
    string Name, int DisplayOrder, string Visibility, bool MarksTaskComplete, bool RequiresApproval, Guid? ApproverId);
```

- [ ] **Step 2: Add the controller actions**

In `TasksController.cs`, add `using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;`
and `using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;`, then add these
two actions near the existing `EditStatus` action:

```csharp
    [HttpPost("objectives/{objectiveId:guid}/task-statuses")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreateStatus(Guid objectiveId, [FromBody] CreateTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskStatusCommand(
            objectiveId, request.Name, request.DisplayOrder, request.Visibility, request.MarksTaskComplete,
            request.RequiresApproval, request.ApproverId), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> DeleteStatus(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTaskStatusCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Note `.ToViewModel()` here reuses the exact same `TaskStatusResponse -> TaskStatusViewModel` mapper
extension already used by `GetStatuses`/`EditStatus` — Task 3 already extended it with `Visibility`,
so no further mapper change is needed.

- [ ] **Step 3: Manual verification**

Run: `dotnet build src/ONEVO.Api` — confirm it compiles. Then run the full test suite:
`dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
Expected: builds clean, all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
git commit -m "feat(work): wire CreateTaskStatus/DeleteTaskStatus endpoints"
```

---

## End of Part 1

Part 1 is complete when all 9 tasks are committed and `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` is fully green. This unblocks Part 2 (Sprint entity + lifecycle),
which depends on `TaskStatus.MarksTaskComplete` being reliably checkable per-task (already true) and
on `Objective.CompletedHours` being live-accurate (now true, from Task 7) for Sprint's own
all-tasks-complete gate to be meaningful.
