# WM Event Duration & Hybrid Membership — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give calendar Events a start/end date, let an Event hold whole Modules *and* individual Tasks, allow many Events per Module (one active Event per Task), reshape the project-calendar read, guard task due-date edits against event windows, and add a People filter to the project task list.

**Architecture:** Additive schema change — `calendar_events` gains two date columns, a new `calendar_event_tasks` link table joins Events to individual Tasks alongside the existing `calendar_event_objectives` (whole-module, now non-unique). Command handlers gain window/uniqueness validation (rules R1–R3). The `GetProjectCalendar` read returns per-module event links (whole/partial) plus an event-band collection. No Objective/Module code changes.

**Tech Stack:** .NET 8, EF Core (PostgreSQL, snake_case), MediatR, FluentValidation, xUnit + Moq. Clean-architecture layering (Domain / Application / Infrastructure / Api).

**Spec:** `docs/superpowers/specs/next/2026-09-02-work-management-event-duration-and-hybrid-membership-design.md` (identical copy in the frontend repo). Read it alongside this plan.

## Global Constraints

- **Branch:** `feature/wm-event-duration-hybrid-membership`, cut from `feature/wm-approval-hours-and-component-tuning`. Do not push.
- **Migrations are written and dry-run-checked only — the USER applies them** via `ops/postgres/setup-local-db.ps1 -RunMigrations`. Never run `dotnet ef database update`.
- **No dev-server, no process kills, no push** (standing project rules).
- Build the Application project explicitly before running unit tests if you use `-p:BuildProjectReferences=false`: `dotnet build src/ONEVO.Application` first (that flag skips rebuilding `ONEVO.Application`).
- If `dotnet build` / `dotnet ef` fails with a NuGet.targets *"path1 is null"* error, it is a stale MSBuild server: run `dotnet build-server shutdown` and retry. Not a code problem.
- After `dotnet ef migrations add`, if unrelated modules suddenly look broken, compare `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` line count before/after — a stale-checkout add can corrupt the snapshot. Regenerate from a clean checkout of the branch if so.
- **Architecture suite must stay green:** `dotnet test tests/ONEVO.Tests.Architecture`. `calendar_event_tasks` has no `TenantId` (child of tenant-owned `calendar_events`, exactly like `calendar_event_objectives`) so no new `TenantTables` RLS-coverage row is expected — but run the suite to confirm; if it flags, follow the 2026-08-27 `AddCalendarEventsRlsPolicyCoverage` coverage-migration recipe.
- **One commit per task.** Commit message prefix `feat:` / `refactor:` / `test:` / `docs:` as appropriate. End every commit message with:
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`
- Hex colour rule (unchanged): `^#[0-9a-fA-F]{6}$`. Event `Name` ≤ 255 chars.
- Rules, verbatim from spec §2:
  - **R1** one active event per task; a module may be a whole-member of many active events.
  - **R2** for every member task `t` of active event `e`: `e.StartDate ≤ t.DueDate ≤ e.EndDate`; a task with no `DueDate` cannot be a member.
  - **R3** out-of-window add / due-date edit / window narrowing is rejected `409` naming the task(s); the event is never auto-moved.
  - **R4** whole-module link is live (stores the objective link, not its tasks); its current active tasks are members now and as added later (subject to R2 via D-B).
  - **R5** event authorship check is unchanged.

---

## Task 1: Schema — event dates, `CalendarEventTask`, non-unique module link, migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEvent.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEventTask.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventTaskConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventObjectiveConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (near line 280, by `CalendarEventObjectives`)
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddEventDatesAndHybridMembership.cs` (generated)
- Test: `dotnet test tests/ONEVO.Tests.Architecture`

**Interfaces:**
- Produces:
  - `CalendarEvent.StartDate` / `CalendarEvent.EndDate` — `DateOnly`
  - `class CalendarEventTask { Guid Id; Guid CalendarEventId; Guid TaskId; DateTimeOffset AddedAt; }`
  - `ApplicationDbContext.CalendarEventTasks` — `DbSet<CalendarEventTask>`

- [ ] **Step 1: Add the two date properties to `CalendarEvent`**

In `CalendarEvent.cs`, after `Color`:
```csharp
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
```

- [ ] **Step 2: Create the `CalendarEventTask` entity**

`src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEventTask.cs`:
```csharp
namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

/// <summary>Links an Event to a single Task (spec §4). Sits alongside CalendarEventObjective
/// (whole-module, live). No TenantId — child of the tenant-owned calendar_events row.</summary>
public class CalendarEventTask
{
    public Guid Id { get; set; }
    public Guid CalendarEventId { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 3: Create `CalendarEventTaskConfiguration`**

`src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventTaskConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class CalendarEventTaskConfiguration : IEntityTypeConfiguration<CalendarEventTask>
{
    public void Configure(EntityTypeBuilder<CalendarEventTask> builder)
    {
        builder.ToTable("calendar_event_tasks");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.CalendarEventId, e.TaskId })
            .IsUnique()
            .HasDatabaseName("ix_calendar_event_tasks_event_task");
        builder.HasIndex(e => e.TaskId)
            .HasDatabaseName("ix_calendar_event_tasks_task_id");

        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.CalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkTask>()
            .WithMany()
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Map the date columns in `CalendarEventConfiguration`**

In `CalendarEventConfiguration.Configure`, after the `Status` property line:
```csharp
        builder.Property(e => e.StartDate).HasColumnName("start_date").IsRequired();
        builder.Property(e => e.EndDate).HasColumnName("end_date").IsRequired();
```

- [ ] **Step 5: Drop the uniqueness on the module link**

In `CalendarEventObjectiveConfiguration.Configure`, change the composite index — remove `.IsUnique()` so a module can be in many events (keep the index for lookup, keep the `ObjectiveId` index untouched):
```csharp
        builder.HasIndex(e => new { e.CalendarEventId, e.ObjectiveId })
            .HasDatabaseName("ix_calendar_event_objectives_event_objective");
```

- [ ] **Step 6: Register the DbSet**

In `ApplicationDbContext.cs`, directly after the `CalendarEventObjectives` line:
```csharp
    public DbSet<CalendarEventTask> CalendarEventTasks => Set<CalendarEventTask>();
```

- [ ] **Step 7: Build, then generate the migration**

```bash
dotnet build-server shutdown
dotnet build src/ONEVO.Infrastructure
dotnet ef migrations add AddEventDatesAndHybridMembership \
  --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api \
  --output-dir Migrations
```
Expected: a new migration pair under `src/ONEVO.Infrastructure/Migrations/`.

- [ ] **Step 8: Hand-edit the migration `Up` for the existing-rows backfill**

Open the generated `<timestamp>_AddEventDatesAndHybridMembership.cs`. EF will emit `start_date`/`end_date` as `AddColumn<DateOnly>(… nullable: false)`. Existing `dapi` `calendar_events` rows need a value. Replace the two `AddColumn` calls with nullable adds + backfill + `NOT NULL` alter, keeping the rest (new table, index rename) as generated:
```csharp
migrationBuilder.AddColumn<DateOnly>(name: "start_date", table: "calendar_events", nullable: true);
migrationBuilder.AddColumn<DateOnly>(name: "end_date", table: "calendar_events", nullable: true);

// Backfill from the min/max dates of each event's linked objectives (spec §4).
migrationBuilder.Sql(@"
    UPDATE calendar_events ce SET
        start_date = COALESCE(sub.min_start, CURRENT_DATE),
        end_date   = COALESCE(sub.max_end,   CURRENT_DATE)
    FROM (
        SELECT ceo.calendar_event_id,
               MIN(o.start_date) AS min_start,
               MAX(o.end_date)   AS max_end
        FROM calendar_event_objectives ceo
        JOIN objectives o ON o.id = ceo.objective_id
        GROUP BY ceo.calendar_event_id
    ) sub
    WHERE sub.calendar_event_id = ce.id;");
migrationBuilder.Sql("UPDATE calendar_events SET start_date = CURRENT_DATE WHERE start_date IS NULL;");
migrationBuilder.Sql("UPDATE calendar_events SET end_date = CURRENT_DATE WHERE end_date IS NULL;");

migrationBuilder.AlterColumn<DateOnly>(name: "start_date", table: "calendar_events", nullable: false, oldClrType: typeof(DateOnly), oldNullable: true);
migrationBuilder.AlterColumn<DateOnly>(name: "end_date", table: "calendar_events", nullable: false, oldClrType: typeof(DateOnly), oldNullable: true);
```
Confirm `Down` drops `calendar_event_tasks`, drops the two columns, and restores the unique index.

- [ ] **Step 9: Dry-run the SQL (review only, do not apply)**

```bash
dotnet ef migrations script --idempotent \
  --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api \
  --output ../_migration-preview.sql
```
Read `_migration-preview.sql`: verify `create table calendar_event_tasks`, the `start_date`/`end_date` add + backfill + alter, and the `calendar_event_objectives` index going from unique to non-unique. Delete the preview file.

- [ ] **Step 10: Snapshot sanity + architecture suite**

```bash
git diff --stat src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs
dotnet test tests/ONEVO.Tests.Architecture
```
Expected: snapshot diff touches only `CalendarEvent` / `CalendarEventObjective` / `CalendarEventTask`; architecture suite PASS.

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Domain src/ONEVO.Infrastructure
git commit -m "feat: add event dates and calendar_event_tasks link, drop module-link uniqueness"
```

---

## Task 2: Create event — dates, direct task links, rules R1/R2

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CreateCalendarEvent/CreateCalendarEventCommand.cs`
- Modify: `.../CreateCalendarEvent/CreateCalendarEventCommandValidator.cs`
- Modify: `.../CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/ProjectCalendarItemResponse.cs` (also holds `CalendarEventResponse`)
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfCalendarEventRepository.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/CalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `CalendarEventTask` (Task 1); `IWorkTaskRepository.GetByObjectiveIdAsync`, `.GetByProjectAsync` (existing).
- Produces:
  - `CreateCalendarEventCommand(Guid ProjectId, string Name, string Color, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<Guid> TaskIds)`
  - `CalendarEventResponse(… , DateOnly StartDate, DateOnly EndDate, IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<Guid> TaskIds, …)`
  - `record ActiveCalendarEventTaskLink(Guid CalendarEventId, Guid TaskId, string EventName)`
  - `ICalendarEventRepository.AddTaskMembershipsAsync(IReadOnlyCollection<CalendarEventTask>, CancellationToken)`
  - `ICalendarEventRepository.ListActiveTaskLinksForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken)` → `IReadOnlyList<ActiveCalendarEventTaskLink>`

- [ ] **Step 1: Extend the command**

`CreateCalendarEventCommand.cs`:
```csharp
public sealed record CreateCalendarEventCommand(
    Guid ProjectId,
    string Name,
    string Color,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds)
    : IRequest<Result<CalendarEventResponse>>;
```

- [ ] **Step 2: Extend the validator**

Add to `CreateCalendarEventCommandValidator` ctor:
```csharp
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");
        RuleFor(x => x.TaskIds).NotNull();
        RuleForEach(x => x.TaskIds).NotEqual(Guid.Empty).WithMessage("Task ids must not be empty.");
```

- [ ] **Step 3: Extend `CalendarEventResponse` + repo interface**

In `ProjectCalendarItemResponse.cs`, change `CalendarEventResponse` to:
```csharp
public sealed record CalendarEventResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);
```
In `ICalendarEventRepository.cs` add the record + methods:
```csharp
public sealed record ActiveCalendarEventTaskLink(Guid CalendarEventId, Guid TaskId, string EventName);

// … inside the interface:
Task AddTaskMembershipsAsync(IReadOnlyCollection<CalendarEventTask> memberships, CancellationToken ct = default);
Task<IReadOnlyList<CalendarEventTask>> ListTaskMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default);
void RemoveTaskMemberships(IReadOnlyCollection<CalendarEventTask> memberships);
Task<IReadOnlyList<ActiveCalendarEventTaskLink>> ListActiveTaskLinksForTasksAsync(
    Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);
```

- [ ] **Step 4: Implement the new repo methods**

In `EfCalendarEventRepository.cs`:
```csharp
public async Task AddTaskMembershipsAsync(IReadOnlyCollection<CalendarEventTask> memberships, CancellationToken ct = default)
{
    if (memberships.Count > 0)
        await _db.CalendarEventTasks.AddRangeAsync(memberships, ct);
}

public async Task<IReadOnlyList<CalendarEventTask>> ListTaskMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default)
    => await _db.CalendarEventTasks.AsNoTracking()
        .Where(m => m.CalendarEventId == calendarEventId)
        .ToListAsync(ct);

public void RemoveTaskMemberships(IReadOnlyCollection<CalendarEventTask> memberships)
{
    if (memberships.Count > 0)
        _db.CalendarEventTasks.RemoveRange(memberships);
}

public async Task<IReadOnlyList<ActiveCalendarEventTaskLink>> ListActiveTaskLinksForTasksAsync(
    Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
{
    if (taskIds.Count == 0) return Array.Empty<ActiveCalendarEventTaskLink>();
    return await (
        from link in _db.CalendarEventTasks.AsNoTracking()
        join ev in _db.CalendarEvents.AsNoTracking() on link.CalendarEventId equals ev.Id
        where taskIds.Contains(link.TaskId)
            && ev.TenantId == tenantId
            && ev.Status == CalendarEventStatuses.Active
        select new ActiveCalendarEventTaskLink(ev.Id, link.TaskId, ev.Name))
        .ToListAsync(ct);
}
```

- [ ] **Step 5: Write the failing handler tests**

Replace the existing `Create_RejectsObjectiveAlreadyInAnotherActiveEvent` test (that rule is gone — modules may now be in many events) and add the window/uniqueness cases. In `CalendarEventCommandHandlerTests.cs`:
```csharp
[Fact]
public async Task Create_AllowsModuleAlreadyInAnotherActiveEvent()
{
    var h = NewCreateHarness();
    h.Objectives(Objective());
    h.TasksInObjective(ObjectiveId); // no tasks -> no R2 work
    var result = await h.Handle(new CreateCalendarEventCommand(
        ProjectId, "E", "#ABCDEF", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        new[] { ObjectiveId }, Array.Empty<Guid>()));
    Assert.True(result.IsSuccess);
}

[Fact]
public async Task Create_PersistsEventWithDatesAndDirectTaskLinks()
{
    var h = NewCreateHarness();
    var taskId = Guid.NewGuid();
    h.Objectives(Objective());
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = new DateOnly(2026, 3, 10), Title = "T" });
    var result = await h.Handle(new CreateCalendarEventCommand(
        ProjectId, "E", "#ABCDEF", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        Array.Empty<Guid>(), new[] { taskId }));
    Assert.True(result.IsSuccess);
    Assert.Equal(new DateOnly(2026, 3, 1), result.Value!.StartDate);
    Assert.Equal(new[] { taskId }, result.Value.TaskIds);
    Assert.Single(h.AddedTaskMemberships!);
}

[Theory]
[InlineData("2026-02-28")]  // before window
[InlineData("2026-04-01")]  // after window
public async Task Create_RejectsTaskWithDueDateOutsideWindow(string due)
{
    var h = NewCreateHarness();
    var taskId = Guid.NewGuid();
    h.Objectives(Objective());
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = DateOnly.Parse(due), Title = "T" });
    var result = await h.Handle(new CreateCalendarEventCommand(
        ProjectId, "E", "#ABCDEF", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        Array.Empty<Guid>(), new[] { taskId }));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task Create_RejectsTaskWithNoDueDate()
{
    var h = NewCreateHarness();
    var taskId = Guid.NewGuid();
    h.Objectives(Objective());
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, DueDate = null, Title = "T" });
    var result = await h.Handle(new CreateCalendarEventCommand(
        ProjectId, "E", "#ABCDEF", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        Array.Empty<Guid>(), new[] { taskId }));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task Create_RejectsTaskAlreadyInAnotherActiveEvent()
{
    var h = NewCreateHarness();
    var taskId = Guid.NewGuid();
    h.Objectives(Objective());
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = new DateOnly(2026, 3, 10), Title = "T" });
    h.TaskAlreadyLinked(taskId, "Other event");
    var result = await h.Handle(new CreateCalendarEventCommand(
        ProjectId, "E", "#ABCDEF", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
        Array.Empty<Guid>(), new[] { taskId }));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}
```
Add a small `NewCreateHarness()` builder in the test file that wires the mocks (`IProjectRepository`, `IObjectiveRepository`, `IWorkTaskRepository`, `ICalendarEventRepository`, `IUnitOfWork`, user context) and exposes `AddedTaskMemberships`. Model it on the existing inline mock setup already in this file.

- [ ] **Step 6: Run the tests — expect FAIL (compile errors / 200 instead of 409)**

```bash
dotnet build src/ONEVO.Application
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CalendarEventCommandHandlerTests"
```

- [ ] **Step 7: Implement the handler**

In `CreateCalendarEventCommandHandler.cs`: inject `IWorkTaskRepository _tasks`. **Delete** the `ListActiveMembershipsForObjectivesAsync` conflict block (module-in-many-events is now allowed). After the objective-existence check, before building the entity:
```csharp
var startDate = request.StartDate;
var endDate = request.EndDate;

// Direct task picks must belong to the project.
var projectTasks = await _tasks.GetByProjectAsync(tenantId, request.ProjectId, ct);
var projectTaskById = projectTasks.ToDictionary(t => t.Id);
var directTaskIds = request.TaskIds.Distinct().ToList();
var missingTasks = directTaskIds.Where(id => !projectTaskById.ContainsKey(id)).ToList();
if (missingTasks.Count > 0)
    return Result<CalendarEventResponse>.NotFound($"Task(s) not found in project: {string.Join(", ", missingTasks)}.");

// Whole-module links contribute their current active tasks (R4).
var moduleTasks = new List<WorkTask>();
foreach (var objId in objectiveIds)
    moduleTasks.AddRange(await _tasks.GetByObjectiveIdAsync(tenantId, objId, ct));

var memberTasks = moduleTasks
    .Concat(directTaskIds.Select(id => projectTaskById[id]))
    .GroupBy(t => t.Id).Select(g => g.First()).ToList();

// R2: every member task has a DueDate inside [startDate, endDate].
var outOfWindow = memberTasks
    .Where(t => t.DueDate is null || t.DueDate < startDate || t.DueDate > endDate)
    .Select(t => t.ShortId).ToList();
if (outOfWindow.Count > 0)
    return Result<CalendarEventResponse>.Conflict(
        $"Task(s) fall outside the event window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}: {string.Join(", ", outOfWindow)}. Widen the event or remove them.");

// R1: no member task is already in another active event.
var alreadyLinked = await _calendarEvents.ListActiveTaskLinksForTasksAsync(
    tenantId, memberTasks.Select(t => t.Id).ToList(), ct);
if (alreadyLinked.Count > 0)
    return Result<CalendarEventResponse>.Conflict(
        $"Task(s) already belong to an active event: {string.Join(", ", alreadyLinked.Select(l => l.EventName).Distinct())}.");
```
Set `StartDate`/`EndDate` on the new `CalendarEvent`. After `AddMembershipsAsync(memberships…)` inside the transaction, also:
```csharp
var taskMemberships = directTaskIds.Select(id => new CalendarEventTask
{
    Id = Guid.NewGuid(), CalendarEventId = calendarEvent.Id, TaskId = id, AddedAt = now
}).ToList();
await _calendarEvents.AddTaskMembershipsAsync(taskMemberships, innerCt);
```
Update `ToResponse` to take `(CalendarEvent, IReadOnlyList<Guid> objectiveIds, IReadOnlyList<Guid> taskIds)` and pass `calendarEvent.StartDate`, `calendarEvent.EndDate`, `taskIds`.

- [ ] **Step 8: Wire the contract + controller**

`CalendarContracts.cs`:
```csharp
public sealed record CreateCalendarEventRequest(
    string Name, string Color, DateOnly StartDate, DateOnly EndDate,
    List<Guid> ObjectiveIds, List<Guid> TaskIds);
```
Add `DateOnly StartDate`, `DateOnly EndDate`, `IReadOnlyList<Guid> TaskIds` to `CalendarEventViewModel` and its mapper. In `CalendarController.CreateEvent`:
```csharp
var command = new CreateCalendarEventCommand(
    projectId, request.Name, request.Color, request.StartDate, request.EndDate,
    request.ObjectiveIds, request.TaskIds);
```

- [ ] **Step 9: Run tests — expect PASS**

```bash
dotnet build src/ONEVO.Application
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CalendarEventCommandHandlerTests"
```

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application src/ONEVO.Infrastructure src/ONEVO.Api tests
git commit -m "feat: calendar event create takes dates and direct task links, enforces R1/R2"
```

---

## Task 3: Update event — dates, task links, replace semantics, R3 re-validation

**Files:**
- Modify: `.../Commands/UpdateCalendarEvent/UpdateCalendarEventCommand.cs`
- Modify: `.../Commands/UpdateCalendarEvent/UpdateCalendarEventCommandValidator.cs`
- Modify: `.../Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Test: `CalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: Task 2's repo methods + `CalendarEventResponse` shape.
- Produces: `UpdateCalendarEventCommand(Guid Id, string? Name, string? Color, DateOnly? StartDate, DateOnly? EndDate, IReadOnlyList<Guid>? ObjectiveIds, IReadOnlyList<Guid>? TaskIds)`

- [ ] **Step 1: Extend command + validator**

```csharp
public sealed record UpdateCalendarEventCommand(
    Guid Id, string? Name, string? Color,
    DateOnly? StartDate, DateOnly? EndDate,
    IReadOnlyList<Guid>? ObjectiveIds, IReadOnlyList<Guid>? TaskIds)
    : IRequest<Result<CalendarEventResponse>>;
```
Validator: extend the "at least one field" rule to include `StartDate`, `EndDate`, `TaskIds`; add `RuleForEach(x => x.TaskIds!).NotEqual(Guid.Empty).When(x => x.TaskIds is not null)`.

- [ ] **Step 2: Write the failing tests**

Replace `Update_RejectsObjectiveAlreadyInDifferentActiveEvent` with `Update_AllowsModuleInAnotherActiveEvent` (assert success). Add:
```csharp
[Fact]
public async Task Update_NarrowingWindowThatOrphansMemberTask_Rejected()
{
    var h = NewUpdateHarness(start: "2026-03-01", end: "2026-03-31");
    var taskId = Guid.NewGuid();
    h.EventTaskLinks(taskId);
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = new DateOnly(2026, 3, 20), Title = "T" });
    var result = await h.Handle(new UpdateCalendarEventCommand(
        h.EventId, null, null, null, new DateOnly(2026, 3, 10), null, null));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task Update_EmptyTaskIds_ClearsAllTaskLinks()
{
    var h = NewUpdateHarness(start: "2026-03-01", end: "2026-03-31");
    var taskId = Guid.NewGuid();
    h.EventTaskLinks(taskId);
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = new DateOnly(2026, 3, 20), Title = "T" });
    var result = await h.Handle(new UpdateCalendarEventCommand(
        h.EventId, null, null, null, null, null, Array.Empty<Guid>()));
    Assert.True(result.IsSuccess);
    Assert.Single(h.RemovedTaskMemberships!);
}

[Fact]
public async Task Update_AddTaskOutsideWindow_Rejected()
{
    var h = NewUpdateHarness(start: "2026-03-01", end: "2026-03-31");
    var taskId = Guid.NewGuid();
    h.ProjectTasks(new WorkTask { Id = taskId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
        DueDate = new DateOnly(2026, 4, 15), Title = "T" });
    var result = await h.Handle(new UpdateCalendarEventCommand(
        h.EventId, null, null, null, null, null, new[] { taskId }));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}
```

- [ ] **Step 3: Run — expect FAIL.** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CalendarEventCommandHandlerTests"`

- [ ] **Step 4: Implement the handler**

In `UpdateCalendarEventCommandHandler.cs`: inject `IWorkTaskRepository _tasks`. **Delete** the cross-event objective-conflict block. After loading the event:
```csharp
var startDate = request.StartDate ?? calendarEvent.StartDate;
var endDate = request.EndDate ?? calendarEvent.EndDate;
if (endDate < startDate)
    return Result<CalendarEventResponse>.Failure("End date must be on or after the start date.");

var currentTaskLinks = await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct);
var desiredTaskIds = request.TaskIds is null
    ? currentTaskLinks.Select(l => l.TaskId).Distinct().ToList()
    : request.TaskIds.Distinct().ToList();
```
Resolve `memberTasks` exactly as in Task 2 Step 7 but using `objectiveIds` (the resolved desired module set) and `desiredTaskIds`. Run the same **R2** out-of-window check against `[startDate, endDate]`. Run **R1** via `ListActiveTaskLinksForTasksAsync` but ignore links whose `CalendarEventId == calendarEvent.Id`. Then:
```csharp
calendarEvent.StartDate = startDate;
calendarEvent.EndDate = endDate;
// name/color as today

var existingTaskIds = currentTaskLinks.Select(l => l.TaskId).ToHashSet();
var desired = desiredTaskIds.ToHashSet();
var taskLinksToRemove = currentTaskLinks.Where(l => !desired.Contains(l.TaskId)).ToList();
var taskLinksToAdd = desiredTaskIds.Where(id => !existingTaskIds.Contains(id))
    .Select(id => new CalendarEventTask { Id = Guid.NewGuid(), CalendarEventId = calendarEvent.Id, TaskId = id, AddedAt = now })
    .ToList();
```
Inside the transaction call `RemoveTaskMemberships(taskLinksToRemove)` and `AddTaskMembershipsAsync(taskLinksToAdd, innerCt)` alongside the objective diff. Return via the updated `ToResponse(calendarEvent, objectiveIds, desiredTaskIds)`.

- [ ] **Step 5: Contract + controller**

`UpdateCalendarEventRequest` gains `DateOnly? StartDate, DateOnly? EndDate, List<Guid>? TaskIds`. `CalendarController.UpdateEvent` passes them.

- [ ] **Step 6: Run — expect PASS.** Then full calendar suite:
```bash
dotnet build src/ONEVO.Application
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CalendarEvents"
```

- [ ] **Step 7: Commit**
```bash
git add src/ONEVO.Application src/ONEVO.Api tests
git commit -m "feat: calendar event update takes dates and task links with R3 re-validation"
```

---

## Task 4: Reshape `GetProjectCalendar` — per-module event links + bands

**Files:**
- Modify: `.../CalendarEvents/DTOs/Responses/ProjectCalendarItemResponse.cs`
- Modify: `.../CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQuery.cs`
- Modify: `.../CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
- Modify: `.../CalendarEvents/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfCalendarEventRepository.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetProjectCalendarQueryHandlerTests.cs`

**Interfaces:**
- Produces:
  - `record ProjectCalendarEventLink(Guid EventId, string EventName, string EventColor, DateOnly EventStartDate, DateOnly EventEndDate, string Membership, int TasksInEventCount, int TaskTotalCount)` — `Membership` is `"whole"` or `"partial"`.
  - `ProjectCalendarItemResponse` — drops `CalendarEventId`/`CalendarEventColor`, gains `IReadOnlyList<ProjectCalendarEventLink> Events`.
  - `record ProjectCalendarEventBand(Guid EventId, string Name, string Color, DateOnly StartDate, DateOnly EndDate, bool CanEdit)`
  - `record ProjectCalendarResponse(IReadOnlyList<ProjectCalendarItemResponse> Modules, IReadOnlyList<ProjectCalendarEventBand> Bands)`
  - `GetProjectCalendarQuery : IRequest<Result<ProjectCalendarResponse>>`
  - `record ActiveEventTaskMembership(Guid EventId, Guid TaskId, Guid ObjectiveId)` — task links joined to their task's objective, for the "partial" grouping.
  - `record ActiveEventHeader(Guid EventId, string Name, string Color, DateOnly StartDate, DateOnly EndDate)`

- [ ] **Step 1: New response records**

Rewrite the top of `ProjectCalendarItemResponse.cs` to the shapes in *Interfaces* above. Keep `CalendarEventResponse` as changed in Task 2.

- [ ] **Step 2: Repo — active event headers + task memberships with objective**

Add to `ICalendarEventRepository`:
```csharp
Task<IReadOnlyList<ActiveEventHeader>> ListActiveEventHeadersForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
Task<IReadOnlyList<ActiveEventTaskMembership>> ListActiveTaskMembershipsForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
```
Implement in `EfCalendarEventRepository`:
```csharp
public async Task<IReadOnlyList<ActiveEventHeader>> ListActiveEventHeadersForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    => await _db.CalendarEvents.AsNoTracking()
        .Where(e => e.TenantId == tenantId && e.ProjectId == projectId && e.Status == CalendarEventStatuses.Active)
        .OrderBy(e => e.CreatedAt)
        .Select(e => new ActiveEventHeader(e.Id, e.Name, e.Color, e.StartDate, e.EndDate))
        .ToListAsync(ct);

public async Task<IReadOnlyList<ActiveEventTaskMembership>> ListActiveTaskMembershipsForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    => await (
        from link in _db.CalendarEventTasks.AsNoTracking()
        join ev in _db.CalendarEvents.AsNoTracking() on link.CalendarEventId equals ev.Id
        join tk in _db.WorkTasks.AsNoTracking() on link.TaskId equals tk.Id
        where ev.TenantId == tenantId && ev.ProjectId == projectId && ev.Status == CalendarEventStatuses.Active
        select new ActiveEventTaskMembership(ev.Id, link.TaskId, tk.ObjectiveId))
        .ToListAsync(ct);
```
`ListActiveMembershipsForProjectAsync` (objective links) stays as-is and still supplies the "whole" links.

- [ ] **Step 3: Rewrite the failing tests**

Rewrite `GetProjectCalendarQueryHandlerTests.cs`'s first test and add cases:
```csharp
[Fact]
public async Task Handle_ListsWholeAndPartialEventLinks_PerModule()
{
    // root (default) has no links; child is a WHOLE member of eventA;
    // "other" module has 1 of its 2 tasks linked to eventB -> PARTIAL, count 1/2.
    var h = NewHarness();
    h.Objectives(root, child, other);
    h.ObjectiveLinks(new ActiveCalendarEventMembership(eventA, ChildId, "#111111"));
    h.EventHeaders(
        new ActiveEventHeader(eventA, "A", "#111111", D("2026-03-01"), D("2026-03-31")),
        new ActiveEventHeader(eventB, "B", "#222222", D("2026-04-01"), D("2026-04-30")));
    h.TaskMemberships(new ActiveEventTaskMembership(eventB, taskX, OtherId));
    h.ObjectiveTaskCounts(OtherId, active: 2);

    var result = await h.Handle(new GetProjectCalendarQuery(ProjectId));

    Assert.True(result.IsSuccess);
    var childLinks = result.Value!.Modules.Single(m => m.ObjectiveId == ChildId).Events;
    Assert.Equal("whole", Assert.Single(childLinks).Membership);
    var otherLinks = result.Value.Modules.Single(m => m.ObjectiveId == OtherId).Events;
    var partial = Assert.Single(otherLinks);
    Assert.Equal("partial", partial.Membership);
    Assert.Equal(1, partial.TasksInEventCount);
    Assert.Equal(2, partial.TaskTotalCount);
    Assert.Equal(2, result.Value.Bands.Count);
}

[Fact]
public async Task Handle_ModuleInTwoEvents_ReturnsBothLinks() { /* child whole in A, one task of child in B */ }
```
Keep `Handle_NotAuthenticated_ReturnsForbidden` (adjust to the new result type).

- [ ] **Step 4: Run — expect FAIL.**

- [ ] **Step 5: Rewrite the handler**

`GetProjectCalendarQueryHandler.Handle` — after resolving objectives and `IsEffectiveManager`:
```csharp
var wholeLinks   = await _calendarEvents.ListActiveMembershipsForProjectAsync(tenantId, request.ProjectId, ct);
var taskLinks    = await _calendarEvents.ListActiveTaskMembershipsForProjectAsync(tenantId, request.ProjectId, ct);
var eventHeaders = await _calendarEvents.ListActiveEventHeadersForProjectAsync(tenantId, request.ProjectId, ct);
var headerById   = eventHeaders.ToDictionary(h => h.EventId);

// active task count per objective (from the tasks repo — inject IWorkTaskRepository)
var allTasks = await _tasks.GetByProjectAsync(tenantId, request.ProjectId, ct);
var taskCountByObjective = allTasks.GroupBy(t => t.ObjectiveId).ToDictionary(g => g.Key, g => g.Count());

var wholeByObjective   = wholeLinks.GroupBy(l => l.ObjectiveId)
    .ToDictionary(g => g.Key, g => g.Select(x => x.CalendarEventId).ToHashSet());
var partialByObjective = taskLinks.GroupBy(l => l.ObjectiveId)
    .ToDictionary(g => g.Key, g => g.GroupBy(x => x.EventId).ToDictionary(e => e.Key, e => e.Count()));

var modules = objectives.Select(o =>
{
    var links = new List<ProjectCalendarEventLink>();
    var total = taskCountByObjective.GetValueOrDefault(o.Id, 0);
    var wholeEventIds = wholeByObjective.GetValueOrDefault(o.Id, new HashSet<Guid>());
    foreach (var eid in wholeEventIds)
        if (headerById.TryGetValue(eid, out var hd))
            links.Add(new ProjectCalendarEventLink(eid, hd.Name, hd.Color, hd.StartDate, hd.EndDate, "whole", total, total));
    if (partialByObjective.TryGetValue(o.Id, out var perEvent))
        foreach (var (eid, count) in perEvent)
            if (!wholeEventIds.Contains(eid) && headerById.TryGetValue(eid, out var hd))
                links.Add(new ProjectCalendarEventLink(eid, hd.Name, hd.Color, hd.StartDate, hd.EndDate, "partial", count, total));

    var canEdit = IsEffectiveManager(o) && !o.IsAchieved && !o.IsDefault;
    return new ProjectCalendarItemResponse(o.Id, o.ProjectId, o.ParentObjectiveId, o.Title,
        o.StartDate, o.EndDate, o.IsActive, o.IsAchieved, canEdit, links);
}).ToList();

// Bands: an event is editable if the caller is an effective manager of any objective that contributes to it.
var contributingObjByEvent = wholeLinks.GroupBy(l => l.CalendarEventId)
    .ToDictionary(g => g.Key, g => g.Select(x => x.ObjectiveId).ToHashSet());
foreach (var tl in taskLinks)
    contributingObjByEvent.GetValueOrDefault(tl.EventId, new HashSet<Guid>()).Add(tl.ObjectiveId);
var bands = eventHeaders.Select(hd =>
{
    var objs = contributingObjByEvent.GetValueOrDefault(hd.EventId, new HashSet<Guid>());
    var canEdit = objs.Any(id => objectivesById.TryGetValue(id, out var ob) && IsEffectiveManager(ob));
    return new ProjectCalendarEventBand(hd.EventId, hd.Name, hd.Color, hd.StartDate, hd.EndDate, canEdit);
}).ToList();

return Result<ProjectCalendarResponse>.Success(new ProjectCalendarResponse(modules, bands));
```
(Add `IWorkTaskRepository _tasks` to the ctor.)

- [ ] **Step 6: Contract + controller**

Replace `ProjectCalendarItemViewModel` with the new module + link + band view models and a `ProjectCalendarViewModel(Modules, Bands)` wrapper; update `CalendarViewModelMapper`. `CalendarController.GetProjectCalendar` maps `result.Value` (now the wrapper) to that view model.

- [ ] **Step 7: Run — expect PASS.**
```bash
dotnet build src/ONEVO.Application
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetProjectCalendarQueryHandlerTests"
```

- [ ] **Step 8: Commit**
```bash
git add src/ONEVO.Application src/ONEVO.Infrastructure src/ONEVO.Api tests
git commit -m "feat: project calendar returns per-module event links and event bands"
```

---

## Task 5: Task due-date edit guard (R3) + `WorkTaskResponse` event fields

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Modify: `.../Tasks/Commands/EditTask/EditTaskCommandHandler.cs`
- Modify: `.../Tasks/Commands/ApproveTaskEditRequest/ApproveTaskEditRequestCommandHandler.cs`
- Modify: `.../CalendarEvents/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfCalendarEventRepository.cs`
- Modify: `.../Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs` and `.../GetTaskById/GetTaskByIdQueryHandler.cs` (populate the new fields)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs`, `ApproveTaskEditRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces:
  - `WorkTaskResponse` gains trailing optional params `Guid? ActiveEventId = null, string? ActiveEventName = null`.
  - `record ActiveEventWindow(Guid EventId, string Name, DateOnly StartDate, DateOnly EndDate)`
  - `ICalendarEventRepository.ListActiveEventWindowsForTaskAsync(Guid tenantId, Guid taskId, Guid objectiveId, CancellationToken)` → union of the task's direct link and any whole-module link on `objectiveId`.

- [ ] **Step 1: Extend `WorkTaskResponse`** — append the two optional params after `TotalLoggedMinutes`. Optional defaults mean existing constructor calls still compile.

- [ ] **Step 2: Repo method**

```csharp
public sealed record ActiveEventWindow(Guid EventId, string Name, DateOnly StartDate, DateOnly EndDate);

public async Task<IReadOnlyList<ActiveEventWindow>> ListActiveEventWindowsForTaskAsync(
    Guid tenantId, Guid taskId, Guid objectiveId, CancellationToken ct = default)
{
    var direct =
        from link in _db.CalendarEventTasks.AsNoTracking()
        join ev in _db.CalendarEvents.AsNoTracking() on link.CalendarEventId equals ev.Id
        where link.TaskId == taskId && ev.TenantId == tenantId && ev.Status == CalendarEventStatuses.Active
        select new ActiveEventWindow(ev.Id, ev.Name, ev.StartDate, ev.EndDate);
    var viaModule =
        from link in _db.CalendarEventObjectives.AsNoTracking()
        join ev in _db.CalendarEvents.AsNoTracking() on link.CalendarEventId equals ev.Id
        where link.ObjectiveId == objectiveId && ev.TenantId == tenantId && ev.Status == CalendarEventStatuses.Active
        select new ActiveEventWindow(ev.Id, ev.Name, ev.StartDate, ev.EndDate);
    return await direct.Concat(viaModule).Distinct().ToListAsync(ct);
}
```

- [ ] **Step 3: Failing tests**

`EditTaskCommandHandlerTests.cs`:
```csharp
[Fact]
public async Task EditTask_DueDateOutsideActiveEventWindow_Rejected()
{
    var h = NewHarness();
    h.Task(dueDate: new DateOnly(2026, 3, 10), objectiveId: ObjectiveId);
    h.ActiveEventWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
    var result = await h.Handle(EditWith(dueDate: new DateOnly(2026, 4, 5)));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task EditTask_DueDateWithinWindow_Succeeds()
{
    var h = NewHarness();
    h.Task(dueDate: new DateOnly(2026, 3, 10), objectiveId: ObjectiveId);
    h.ActiveEventWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
    var result = await h.Handle(EditWith(dueDate: new DateOnly(2026, 3, 20)));
    Assert.True(result.IsSuccess);
}
```
Mirror one rejection test in `ApproveTaskEditRequestCommandHandlerTests.cs`. If these handler test files don't exist yet, create them modelled on `CalendarEventCommandHandlerTests` (Moq, static ids, inline mock wiring). Inject a `Mock<ICalendarEventRepository>` into the handler under test.

- [ ] **Step 4: Run — expect FAIL.**

- [ ] **Step 5: Implement the guard**

In `EditTaskCommandHandler.Handle`, after the task is loaded and before the transaction, when `request.DueDate != task.DueDate`:
```csharp
var windows = await _calendarEvents.ListActiveEventWindowsForTaskAsync(tenantId, task.Id, task.ObjectiveId, ct);
if (windows.Count > 0)
{
    if (request.DueDate is null)
        return Result<WorkTaskResponse>.Conflict(
            $"This task is in active event(s) {string.Join(", ", windows.Select(w => w.Name))}; a due date is required.");
    var bad = windows.Where(w => request.DueDate < w.StartDate || request.DueDate > w.EndDate).ToList();
    if (bad.Count > 0)
        return Result<WorkTaskResponse>.Conflict(
            $"Due date {request.DueDate:yyyy-MM-dd} is outside event window(s): {string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.");
}
```
Inject `ICalendarEventRepository _calendarEvents`. Apply the identical block in `ApproveTaskEditRequestCommandHandler` against `payload.DueDate`.

- [ ] **Step 6: Populate the response fields (read side)**

In `GetProjectTasksQueryHandler` and `GetTaskByIdQueryHandler`, after loading tasks, batch-load direct task→event links (`ListActiveTaskLinksForTasksAsync` from Task 2) and set `ActiveEventId` / `ActiveEventName` on each `WorkTaskResponse` where present. (Whole-module membership is intentionally *not* surfaced here — only an explicit link shows the chip.)

- [ ] **Step 7: Run — expect PASS.** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditTask|FullyQualifiedName~ApproveTaskEditRequest"`

- [ ] **Step 8: Commit**
```bash
git add src/ONEVO.Application src/ONEVO.Infrastructure tests
git commit -m "feat: block task due-date edits that fall outside an active event window"
```

---

## Task 6: Task-create guard for whole-module events (D-B)

**Files:**
- Modify: `.../Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- Modify: `.../Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs`
- Modify: `.../CalendarEvents/RepositoryInterfaces/ICalendarEventRepository.cs` + `EfCalendarEventRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`, `ApproveTaskCreationRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces: `ICalendarEventRepository.ListActiveEventWindowsForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken)` → `IReadOnlyList<ActiveEventWindow>` (whole-module links only — the `viaModule` half of Task 5 Step 2).

- [ ] **Step 1: Repo method** — extract the `viaModule` query from Task 5 into its own public method:
```csharp
public async Task<IReadOnlyList<ActiveEventWindow>> ListActiveEventWindowsForObjectiveAsync(
    Guid tenantId, Guid objectiveId, CancellationToken ct = default)
    => await (
        from link in _db.CalendarEventObjectives.AsNoTracking()
        join ev in _db.CalendarEvents.AsNoTracking() on link.CalendarEventId equals ev.Id
        where link.ObjectiveId == objectiveId && ev.TenantId == tenantId && ev.Status == CalendarEventStatuses.Active
        select new ActiveEventWindow(ev.Id, ev.Name, ev.StartDate, ev.EndDate)).ToListAsync(ct);
```

- [ ] **Step 2: Failing tests**
```csharp
[Fact]
public async Task CreateTask_IntoWholeModuleEvent_RequiresDueDate()
{
    var h = NewHarness();
    h.ObjectiveInActiveEvent(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
    var result = await h.Handle(CreateWith(dueDate: null));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task CreateTask_IntoWholeModuleEvent_OutOfWindow_Rejected()
{
    var h = NewHarness();
    h.ObjectiveInActiveEvent(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
    var result = await h.Handle(CreateWith(dueDate: new DateOnly(2026, 4, 10)));
    Assert.False(result.IsSuccess);
    Assert.Equal(409, result.StatusCode);
}

[Fact]
public async Task CreateTask_ObjectiveNotInAnyEvent_NoDueDate_Succeeds()
{
    var h = NewHarness();               // no event on the objective
    var result = await h.Handle(CreateWith(dueDate: null));
    Assert.True(result.IsSuccess);
}
```

- [ ] **Step 3: Run — expect FAIL.**

- [ ] **Step 4: Implement** — in `CreateTaskCommandHandler.Handle`, after the objective + project checks, before building the task:
```csharp
var eventWindows = await _calendarEvents.ListActiveEventWindowsForObjectiveAsync(tenantId, objective.Id, ct);
if (eventWindows.Count > 0)
{
    if (request.DueDate is null)
        return Result<WorkTaskResponse>.Conflict(
            $"This module is in active event(s) {string.Join(", ", eventWindows.Select(w => w.Name))}; a due date is required.");
    var bad = eventWindows.Where(w => request.DueDate < w.StartDate || request.DueDate > w.EndDate).ToList();
    if (bad.Count > 0)
        return Result<WorkTaskResponse>.Conflict(
            $"Due date {request.DueDate:yyyy-MM-dd} is outside event window(s): {string.Join(", ", bad.Select(w => $"{w.Name} {w.StartDate:yyyy-MM-dd}..{w.EndDate:yyyy-MM-dd}"))}. Widen the event first.");
}
```
Inject `ICalendarEventRepository _calendarEvents`. Apply the same block in `ApproveTaskCreationRequestCommandHandler` when the task is materialised (using the payload's objective + due date).

- [ ] **Step 5: Run — expect PASS.**

- [ ] **Step 6: Commit**
```bash
git add src/ONEVO.Application src/ONEVO.Infrastructure tests
git commit -m "feat: block creating an out-of-window task in a module tied to an active event"
```

---

## Task 7: People filter on the project task list

**Files:**
- Modify: `.../Tasks/Queries/GetProjectTasks/GetProjectTasksQuery.cs`
- Modify: `.../Tasks/Queries/GetProjectTasks/GetProjectTasksQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (the `GET projects/{projectId}/tasks` action, ~line 109)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTasksQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetProjectTasksQuery(Guid ProjectId, IReadOnlyList<Guid>? AssigneeEmployeeIds = null)`

- [ ] **Step 1: Extend the query record** — add `IReadOnlyList<Guid>? AssigneeEmployeeIds = null`.

- [ ] **Step 2: Failing tests**
```csharp
[Fact]
public async Task GetProjectTasks_FilterByAssignee_ReturnsOnlyMatchingTasks()
{
    var h = NewHarness();
    h.Tasks(t1 /* assigned emp A */, t2 /* assigned emp B */, t3 /* unassigned */);
    var result = await h.Handle(new GetProjectTasksQuery(ProjectId, new[] { EmpA }));
    Assert.Equal(new[] { t1.Id }, result.Value!.Select(r => r.Id));
}

[Fact]
public async Task GetProjectTasks_EmptyAssigneeFilter_ReturnsAll()
{
    var h = NewHarness();
    h.Tasks(t1, t2, t3);
    var result = await h.Handle(new GetProjectTasksQuery(ProjectId, Array.Empty<Guid>()));
    Assert.Equal(3, result.Value!.Count);
}

[Fact]
public async Task GetProjectTasks_AssigneeNotOnAnyTask_ReturnsNone()
{
    var h = NewHarness();
    h.Tasks(t1, t2);
    var result = await h.Handle(new GetProjectTasksQuery(ProjectId, new[] { Guid.NewGuid() }));
    Assert.Empty(result.Value!);
}
```

- [ ] **Step 3: Run — expect FAIL.**

- [ ] **Step 4: Implement** — in `GetProjectTasksQueryHandler.Handle`, right after `assigneesByTaskId` is built:
```csharp
if (request.AssigneeEmployeeIds is { Count: > 0 } wanted)
{
    var wantedSet = wanted.ToHashSet();
    items = items.Where(t =>
        assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()).Any(wantedSet.Contains)).ToList();
}
```
(Place it before the `openSessions` / `totalLoggedMinutes` lookups so those queries only run for the filtered set.)

- [ ] **Step 5: Controller** — bind and pass the query param:
```csharp
[HttpGet("projects/{projectId:guid}/tasks")]
public async Task<IActionResult> GetProjectTasks(
    Guid projectId, [FromQuery] Guid[]? assigneeEmployeeIds, CancellationToken ct)
{
    var result = await _mediator.Send(new GetProjectTasksQuery(projectId, assigneeEmployeeIds), ct);
    ...
}
```

- [ ] **Step 6: Run — expect PASS.** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetProjectTasksQueryHandlerTests"`

- [ ] **Step 7: Commit**
```bash
git add src/ONEVO.Application src/ONEVO.Api tests
git commit -m "feat: optional assignee filter on the project task list"
```

---

## Task 8: Documentation

**Files:**
- Modify: `docs/postman-request/Work Management/Create Calendar Event.md`, `Update Calendar Event.md`, `Get Project Calendar.md`, `Edit Task.md`, `Create Task.md`, and the project-tasks GET doc (find under `docs/postman-request/Work Management/`).
- Modify: `docs/**/phase1-table-inventory.md` (search for the filename).
- Modify: `docs/core/ARCHITECTURE.md` — Work Management entity list.

- [ ] **Step 1** — In each Postman-request MD, update the JSON request/response bodies to the new shapes: event create/update gain `startDate`, `endDate`, `taskIds`; `Get Project Calendar` returns `{ modules: [ { …, events: [ { eventId, eventColor, membership, tasksInEventCount, taskTotalCount } ] } ], bands: [ { eventId, name, color, startDate, endDate, canEdit } ] }`; `Edit Task` / `Create Task` note the `409` when the due date is outside a linked event window; project-tasks GET documents `?assigneeEmployeeIds=`.

- [ ] **Step 2** — `phase1-table-inventory.md`: `calendar_events` gains `start_date date NOT NULL`, `end_date date NOT NULL`; add row for `calendar_event_tasks (id, calendar_event_id, task_id, added_at)`; note `calendar_event_objectives` composite index is no longer unique.

- [ ] **Step 3** — `ARCHITECTURE.md`: add `CalendarEventTask` beside `CalendarEventObjective` in the WM section.

- [ ] **Step 4: Commit**
```bash
git add docs
git commit -m "docs: event dates, hybrid membership, calendar read shape, people filter"
```

---

## Task 9: Test-coverage audit

**Files:** all `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/*` and the touched Tasks test files.

- [ ] **Step 1: Boundary sweep (R2).** Ensure explicit cases exist for a member task due date **exactly on** `StartDate`, **exactly on** `EndDate`, one day before `StartDate`, one day after `EndDate` — on both create and update. Add any missing.
- [ ] **Step 2: R1 negative.** A task in an **archived** event does **not** block adding it to a new active event. Add if missing.
- [ ] **Step 3: Replace semantics.** `ObjectiveIds` present + `TaskIds` absent leaves task links untouched, and vice-versa; both `[]` clears both.
- [ ] **Step 4: Read correctness.** Module that is *both* a whole member of event A and has a stray task linked to event A → returns a single `"whole"` link, not `"whole"` + `"partial"`. Partial count counts only that module's linked tasks, not the event's total.
- [ ] **Step 5: Guard paths.** Task-edit guard triggers via a **whole-module** link (no direct link) as well as via a direct link. Task-create guard does nothing when the objective is in no event.
- [ ] **Step 6: People filter.** A task with no assignees is excluded when a filter is supplied and included when it is not.
- [ ] **Step 7: Full run.**
```bash
dotnet build-server shutdown
dotnet build src/ONEVO.Application
dotnet test tests/ONEVO.Tests.Unit
dotnet test tests/ONEVO.Tests.Architecture
```
Expected: all green. Record the test counts in the commit message.
- [ ] **Step 8: Commit**
```bash
git add tests
git commit -m "test: boundary and negative coverage for event windows, membership, filter"
```

---

## Task 10: Verification & migration hand-off

- [ ] **Step 1: Clean build + full suite** (as Task 9 Step 7). Paste the pass counts into the plan's SUMMARY entry.
- [ ] **Step 2: Confirm the migration file is committed** and `_migration-preview.sql` is deleted / git-ignored.
- [ ] **Step 3: Tell the user to apply the migration:**
  > Migration `AddEventDatesAndHybridMembership` is ready. Run `ops/postgres/setup-local-db.ps1 -RunMigrations` when you're ready; I have not applied it.
- [ ] **Step 4:** After the user confirms the DB is migrated and the frontend plan is also done, move this file `docs/superpowers/plans/next/ → docs/superpowers/plans/finished/` and update `docs/superpowers/plans/next/SUMMARY.md`.

---

## Self-review notes (author)

- **Spec coverage:** §4 → T1; §5.1 → T2; §5.2 → T3; §5.4 → T4; §5.6 → T5; §5.7 → T6; §6.2 backend → T7; §7.3 docs → T8; §8 testing → per-task + T9; §9 order preserved. §6.1 backend (remove "tasks assigned to me" endpoint) is **frontend-plan-owned** — the endpoint `GET projects/{projectId}/my-tasks` stays unless the frontend plan confirms nothing else consumes it; noted there.
- **Deferred to frontend plan:** everything in spec §6.1 (route/nav/page removal), §7 (event editor, timeline rendering, task chips), §6.2 frontend (the People multi-select control).
- **Type consistency:** `CalendarEventResponse` shape is fixed in T2 and reused unchanged in T3. `ActiveEventWindow` defined in T5, reused in T6. `ProjectCalendarResponse` wrapper introduced in T4 and is the controller's new return type.
- **Open default (D-B):** T6 rejects the out-of-window task-create. If the user flips this at spec review to "silently exclude", T6 becomes: create the task normally and simply do not write a `CalendarEventTask` row (there is none to write for a whole-module link anyway) — i.e. delete T6 entirely.
