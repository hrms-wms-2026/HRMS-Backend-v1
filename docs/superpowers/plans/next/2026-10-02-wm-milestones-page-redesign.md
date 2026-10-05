# WM Milestones Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, inline in the session (no subagents). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the Work Management "Project Calendar" page (tab label becomes "Milestones") so milestone coverage reads visually on the Gantt bars (gray by default, proportionally colored by task coverage), reuses the Tree tab's own row/detail-panel visual language instead of bespoke markup, and gets a real per-milestone detail view (Overview/Modules/Tasks/Activity) backed by real data.

**Architecture:** Backend adds three read endpoints (`GET /calendar-events/{id}`, `/tasks`, `/activity`) and one write-side log table, all additive to the existing `Features/WorkManagement/CalendarEvents` slice — no existing contract changes except new optional fields. Frontend keeps the existing zoom/scroll Gantt (`ProjectCalendarComponent`) and extends `ObjectiveTimelineBarComponent`'s fill logic, reuses `ModuleTreeRowComponent` in a new compact mode for the row labels, and adds a new `MilestoneExplanationPanelComponent` sibling to the Tree tab's own `TreeExplanationPanelComponent`.

**Tech Stack:** .NET 8 / EF Core / MediatR / PostgreSQL (RLS) on the backend; Angular (standalone components, signals, `@ngrx/signals` stores) on the frontend.

**Spec:** The Design section immediately below is the spec for this plan — there is no separate spec file.

## Global Constraints

- Work Management only. Never touch `Features/Calendar/` (the unrelated personal/company calendar module) — it has its own `CalendarEvent` entity at `Domain/Features/Calendar/Entities/CalendarEvent.cs` and its own `CalendarController.cs` under `Controllers/Tenant/Calendar/`. This plan's `CalendarEvent` is always `Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEvent.cs`.
- Every new tenant-owned table gets its RLS `tenant_isolation` policy in the **same migration** that creates it, or `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` fails the build.
- No `dotnet ef database update` — generating the migration is enough; the user applies it.
- New/changed frontend DTO fields are optional (`?`) so TypeScript compiles and existing tests don't need every call site rewritten — this is a type-safety convenience, not a claim that the page works end-to-end before the user applies the new migration. `Create`/`Update`/`Close` on an un-migrated database will fail outright once Part B's handler code expects a `description` column and `calendar_event_activity_logs` table that don't exist yet. State this plainly in the final handoff report: the migration must be applied before the API runs against this branch's code, full stop.
- Before starting Part A, run the full backend and frontend test suites once and record the passing counts and any already-flaky spec names — this is the "before" baseline that Part E (Task E1) and the final Task L report against, so a regression introduced by this plan is visible instead of blending into pre-existing flakiness.
- TDD: write the failing test, watch it fail, implement, watch it pass, commit. Every task ends green.
- Several tests below (flagged inline, e.g. Task A3's `Handle_ComputesSubtreeProgressFromDoneTaskStatuses`, Task C1's and Task D3's handler tests) show their **arrange** step as a comment describing what to seed, not literal seed code, because this plan doesn't have that test file's exact in-memory/mock fixture helpers in hand. **A comment is not a test body.** Before running any such test, open the real spec file next to it, find how its neighboring tests already construct Objectives/WorkTasks/TaskStatuses/mocked repositories, and write the actual seed code in that same style. Never commit a test whose arrange step is still a comment.
- Backend gate: `dotnet build src/ONEVO.Api -c Release`, then `dotnet test tests/ONEVO.Tests.Unit -c Release`, then `dotnet test tests/ONEVO.Tests.Architecture -c Release`.
- Frontend gate: `npx ng build`, then `npx ng test --watch=false --browsers=ChromeHeadless`.
- Commit messages end with the executing session's own attribution line (not a hardcoded one from this plan).

## Review Focus

- A milestone whose linked objective has **zero direct tasks** (`taskTotalCount == 0`): must not divide by zero anywhere (progress, fill %) and must render "—" / an empty bar rather than crashing or showing 0%/NaN%.
- A module linked to **two different active milestones at once** (possible: a `whole` link covers all of an objective's tasks *at read time*, but new tasks created after that link was made can separately be hand-picked into a second event — see Design §4): the bar must stack both colors instead of only showing one and silently hiding the other's coverage.
- **Editing a milestone from the legend/panel today silently drops every task picked individually** (`bandsToEvents()` always seeds `taskIds: []`, and `UpdateCalendarEventCommandHandler` treats an explicit `taskIds: []` as "remove all direct task links"). This plan's Part I4 must close this before it ships, not just work around it in the new panel.
- A milestone with **no linked objectives or tasks visible to the current viewer** (permission-filtered to zero): the Tasks tab and Key Metrics must show an empty state, not an error.
- The **Tree tab must not change** as a side effect — Progress for this page is computed locally in the Calendar read, not by touching `Objective.Progress` or `ObjectiveMapper` (used by Tree/detail queries). Confirm with a Tree-tab spec run that nothing there moved.

---

## Design

### 1. Scope disambiguation

Two unrelated `CalendarEvent` entities exist in this codebase:

| | This plan touches | This plan does NOT touch |
|---|---|---|
| Entity | `ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities.CalendarEvent` | `ONEVO.Domain.Features.Calendar.Entities.CalendarEvent` (personal/company calendar) |
| Controller | `Controllers/Tenant/WorkManagement/CalendarController.cs` | `Controllers/Tenant/Calendar/CalendarController.cs` |
| Table | `calendar_events` (Project Calendar / Milestones feature) | also `calendar_events` physically, but a different EF model/feature slice — **do not confuse table name with feature** |

Only the visible **tab label** changes, from "Calendar" to "Milestones", on the project-detail page. Routes, `data-testid="calendar-*"` attributes, and the sidebar's unrelated personal-Calendar nav entry in `nav-items.config.ts` are untouched.

### 2. Existing APIs reused vs. new (the user asked for this explicitly)

**Reused as-is:**
- `GET projects/{projectId}/calendar` (`GetProjectCalendarQueryHandler`) — only gains one new optional field (`ProgressPercent`) per module; everything else is unchanged.
- `POST projects/{projectId}/calendar-events`, `PATCH calendar-events/{id}`, `POST calendar-events/{id}/close` — gain one optional `Description` field; existing behavior unchanged.
- `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` — already used by `ObjectiveMapper`/`GetObjectiveTreeQueryHandler` to resolve owner names; reused for the milestone's "Owner" field.
- `ProjectMonitorStore` (frontend, already loaded by the Tree tab) — reused to detect "at risk" alerts on a milestone's linked modules, instead of inventing a new alert concept.
- `app-data-table` (shared component, already used by Backlog/Approvals) — would back a future List view, see §7 Exclusions.
- `ModuleTreeRowComponent`, `TreeExplanationPanelComponent`'s shell/CSS conventions (`app-card`, bordered detail rows, metric tiles, `panel-*` test ids) — reused/extended, not duplicated.
- `WorkTaskResponse`'s existing `GetByProjectAsync` task list — reused and filtered in memory for the new Tasks tab; no new `IWorkTaskRepository` method.

**New (nothing existing could serve these):**
- `GET calendar-events/{id}` — no endpoint today returns a single event's full detail (Description, resolved owner name); Create/Update/Close only return the lightweight `CalendarEventResponse` shape as a side effect of a write.
- `GET calendar-events/{id}/tasks` — `GetObjectiveTasks` only takes one objective id; a milestone's tasks span multiple objectives (direct picks plus whole-linked objectives' tasks), which no existing query expresses.
- `GET calendar-events/{id}/activity` — no activity/audit log exists for `CalendarEvent` or `Objective` today (only `Task` and `Sprint` have one).
- One new repository method, `ITaskStatusRepository.GetByIdsForTenantAsync` — bulk-fetch specific `TaskStatus` rows by id, needed because no existing method returns every status used across a whole project's tasks in one call (only per-objective or per-template).

### 3. Progress % — scoped to this page only

`Objective.Progress` is a stored column that is **never recalculated after creation** (confirmed: no production handler under `Features/WorkManagement/Tasks/**` writes it; only a demo seeder does, with an hours formula that doesn't generalize). Fixing that column everywhere would mean changing `ObjectiveMapper.ToDetail/ToTreeItem/ToSubtreeNode` and every handler that calls them — the Tree tab, objective detail, etc. That is out of scope for a Calendar-page redesign.

Instead, Part A adds progress **only to the Calendar read**, as a new field computed on every request, leaving `Objective.Progress` and the Tree tab exactly as they are today (the Tree tab will keep showing 0% — a known, called-out follow-up, not a regression this plan causes).

Definition: for objective O, let S = O's id plus every descendant's id (`ProjectModuleTree.AtOrBelow([O.Id])` — the *subtree*, not just direct tasks, so a parent with no direct tasks of its own still shows its children's completion instead of a misleading 0%). Progress = round(100 × doneCount / totalCount) over all tasks whose `ObjectiveId ∈ S`, where "done" = `TaskStatus.MarksTaskComplete` (the same boolean the `AddTaskStatusCategoryAndColor` migration keeps in sync with `category = 'done'`). If totalCount is 0, Progress is `null` (frontend renders "—", never "0%" or "NaN%").

### 4. Bar fill — stacking, not just tint

Confirmed from reading `GetProjectCalendarQueryHandler.cs:92`: `taskTotalCount` is the objective's **direct** task count (not subtree) — this is a different, deliberately narrower number than the Progress rollup above, and the two must stay separate fields (`ProgressPercent` vs. the existing `TasksInEventCount`/`TaskTotalCount` per link) so they're never conflated.

R1 (task exclusivity, enforced in Create/UpdateCalendarEventCommandHandler) only blocks a task from belonging to **two active events at once at write time**. It does not retroactively re-check a `whole`-linked objective's *newly created* tasks, so in practice one objective's `events[]` can legitimately contain links to two different events simultaneously (e.g. event A holds it `whole` from before some of its tasks existed; a later task on it is explicitly hand-picked into event B). The fill must stack, not just show one:

- If any link has `membership === 'whole'`, the bar is 100% that link's color (a whole link means *every current task* is covered, so partial numbers from a second link are a subset and not worth a second segment).
- Otherwise, render each partial link as a left-to-right stacked segment sized to `tasksInEventCount / taskTotalCount`, in event order, clamped so segments never sum past 100%.
- No link at all → flat neutral gray, no segment.

### 5. Milestone status pill — derived, not stored

No "On Track / At Risk" concept exists on `CalendarEvent` or `Objective`. Rather than add a new stored enum, derive it client-side from data that already exists:

```
completed  : event.status === 'archived'
upcoming   : today < event.startDate
at risk    : today > event.endDate, OR any linked objective has an open ProjectMonitorStore alert
on track   : none of the above
```

### 6. Overview tab data source

`CalendarEvent` has no `Description` column and the calendar read's bands never carry a resolved owner name. Rather than bloat the list read, Part B adds one new detail endpoint, `GET calendar-events/{id}`, returning the full `CalendarEventResponse` shape plus `Description` and `CreatedByName`. The frontend's Overview tab, and critically the **Edit** action, both go through this endpoint — see §8.

### 7. The real bug this plan fixes in passing

`ProjectCalendarStore.bandsToEvents()` (`project-calendar.store.ts:51-68`) builds its `CalendarEventDto[]` from `bands`, and always sets `taskIds: []` (the comment on that function admits it: "Exact task-link ids are not in the read payload"). `CalendarEventModalComponent`'s `effect()` seeds `selectedTaskIds` straight from `event()?.taskIds`, and `UpdateCalendarEventCommandHandler` treats an explicit empty `TaskIds` as "the caller wants zero direct task links" (`desiredTaskIds = request.TaskIds is null ? current : request.TaskIds.Distinct()`). **Today, opening Edit on any milestone from the legend and saving silently deletes every individually-picked task link.** Part I4 fixes this by having "Edit" fetch the new `GET calendar-events/{id}` detail endpoint (real `taskIds`) before opening the modal, instead of reusing the stub from `store.events()`.

### 8. Milestone explanation panel — sibling component, not an extension

`TreeExplanationPanelComponent` is keyed on `MilestoneTreeNode` (`kind: 'objective' | 'sprint' | 'task'`) with per-kind `@if` branches wired to Tree-tab-specific action outputs (create sub-module, achieve, members...). Adding a fourth kind would ripple into the Tree tab and its specs for no shared benefit. Instead, Part I3 creates `MilestoneExplanationPanelComponent` as a **sibling** that copies the same visual shell (`app-card`, the kind-label header, bordered detail-row list, 3-tile metrics grid, `panel-*` test id convention) but is keyed on the milestone's own data shapes, with its own tab strip (Overview/Modules/Tasks/Activity) instead of the Tree panel's single view.

### 9. Not included in this plan

- Auto-cascading a parent module's milestone coverage down to its children — linking stays exactly as explicit as the existing create/edit modal already makes it.
- Module rows in the Calendar's tree column do **not** get their own explanation panel this round (clicking a module row just expands/collapses, same as today) — that already exists in the Tree tab.
- Key-Metrics-driven filtering (e.g. clicking the "Completed" tile to filter the Tasks list) — the tiles are read-only counts.

The List/Timeline toggle and Month prev/next navigation *were* in an earlier draft's exclusion list, on the reasoning that the actual Tamil-language requirements never asked for them, only the reference screenshot showed them. The user was then asked explicitly and chose "include the full screenshot redesign" and "build everything now" — so Part J below builds both, using the defaults already settled on elsewhere in this Design section (continuous zoom/scroll stays exactly as deliberately built before; Month prev/next is a scroll convenience on top of it, not a second paged view; List mode reuses the existing shared `app-data-table` component).

---

## Part A: Backend — Objective progress rollup (Calendar page only)

### Task A1: `ObjectiveProgressCalculator`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Services/ObjectiveProgressCalculator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/ObjectiveProgressCalculatorTests.cs`

**Interfaces:**
- Produces: `ObjectiveProgressCalculator.Calculate(int doneCount, int totalCount) -> int?`, used by Task A3.

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public class ObjectiveProgressCalculatorTests
{
    [Fact]
    public void Calculate_WithNoTasks_ReturnsNull()
    {
        Assert.Null(ObjectiveProgressCalculator.Calculate(doneCount: 0, totalCount: 0));
    }

    [Theory]
    [InlineData(4, 10, 40)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 3, 33)] // rounds, doesn't truncate to 0
    [InlineData(2, 3, 67)]
    public void Calculate_RoundsToNearestPercent(int done, int total, int expected)
    {
        Assert.Equal(expected, ObjectiveProgressCalculator.Calculate(done, total));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ObjectiveProgressCalculatorTests -c Release`
Expected: FAIL — `ObjectiveProgressCalculator` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Services;

/// <summary>Pure done/total -> percent rollup, shared by anything that needs an Objective's (or a
/// subtree's) task-completion percentage. Null means "no tasks to measure" - render "-", never 0%.</summary>
public static class ObjectiveProgressCalculator
{
    public static int? Calculate(int doneCount, int totalCount)
        => totalCount <= 0 ? null : (int)Math.Round(100.0 * doneCount / totalCount, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ObjectiveProgressCalculatorTests -c Release`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Services/ObjectiveProgressCalculator.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/ObjectiveProgressCalculatorTests.cs
git commit -m "feat(wm-calendar): add pure objective progress calculator"
```

### Task A2: `ITaskStatusRepository.GetByIdsForTenantAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskStatusRepositoryTests.cs` (create if no such file exists yet for this repo; otherwise add to it)

**Interfaces:**
- Produces: `ITaskStatusRepository.GetByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct) -> Task<IReadOnlyList<TaskStatusEntity>>`, used by Task A3.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetByIdsForTenantAsync_ReturnsOnlyMatchingTenantAndIds()
{
    await using var db = CreateInMemoryDbContext(); // use this test file's existing fixture/helper
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var wanted = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), Name = "Done", Category = "done", Color = "#16A34A", MarksTaskComplete = true, DisplayOrder = 0 };
    var unwanted = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), Name = "Active", Category = "active", Color = "#2563EB", MarksTaskComplete = false, DisplayOrder = 1 };
    var otherTenant = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = otherTenantId, ProjectId = Guid.NewGuid(), Name = "Done", Category = "done", Color = "#16A34A", MarksTaskComplete = true, DisplayOrder = 0 };
    db.TaskStatuses.AddRange(wanted, unwanted, otherTenant);
    await db.SaveChangesAsync();
    var repo = new EfTaskStatusRepository(db);

    var result = await repo.GetByIdsForTenantAsync(tenantId, new[] { wanted.Id, otherTenant.Id }, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal(wanted.Id, result[0].Id);
}
```

(If this test file needs its own `CreateInMemoryDbContext()` helper, copy the pattern another `Ef*RepositoryTests.cs` file in the same test project already uses — do not invent a new fixture style.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetByIdsForTenantAsync_ReturnsOnlyMatchingTenantAndIds -c Release`
Expected: FAIL — method does not exist on `ITaskStatusRepository`.

- [ ] **Step 3: Write minimal implementation**

In `ITaskStatusRepository.cs`, add after `GetByIdForTenantAsync`:

```csharp
    /// <summary>Bulk fetch of specific status rows by id, for computing a done/total rollup across
    /// many tasks' StatusIds in one query - no existing method returns every status used by a
    /// whole project's tasks in one call.</summary>
    Task<IReadOnlyList<TaskStatusEntity>> GetByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
```

In `EfTaskStatusRepository.cs`, add after `GetByIdForTenantAsync`:

```csharp
    public async Task<IReadOnlyList<TaskStatusEntity>> GetByIdsForTenantAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await _db.TaskStatuses.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.Id))
            .ToListAsync(ct);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetByIdsForTenantAsync_ReturnsOnlyMatchingTenantAndIds -c Release`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskStatusRepositoryTests.cs
git commit -m "feat(wm-calendar): bulk task-status lookup by ids"
```

### Task A3: Wire progress into `GetProjectCalendarQueryHandler`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/ProjectCalendarItemResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetProjectCalendarQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ObjectiveProgressCalculator.Calculate(int, int) -> int?` (Task A1), `ITaskStatusRepository.GetByIdsForTenantAsync` (Task A2), `ProjectModuleTree.AtOrBelow(IEnumerable<Guid>) -> IReadOnlySet<Guid>` (existing).
- Produces: `ProjectCalendarItemResponse.ProgressPercent : int?`, surfaced to the frontend as `ProjectCalendarModuleDto.progressPercent`.

- [ ] **Step 1: Write the failing test**

Add to `GetProjectCalendarQueryHandlerTests.cs` (follow that file's existing test-setup pattern for building objectives/tasks/statuses — match its existing fixture helper names):

```csharp
[Fact]
public async Task Handle_ComputesSubtreeProgressFromDoneTaskStatuses()
{
    // Arrange: parent objective with no direct tasks, one child objective with 4 done / 6 not-done tasks.
    // (Use this test file's existing helpers to seed Objectives/WorkTasks/TaskStatuses -
    // match whatever in-memory or mock repository setup the surrounding tests already use.)
    // ... seed parent (0 direct tasks), child (6 tasks: 4 with a MarksTaskComplete=true status, 2 without) ...

    var result = await _handler.Handle(new GetProjectCalendarQuery(projectId), CancellationToken.None);

    var parentItem = result.Value!.Modules.Single(m => m.ObjectiveId == parentId);
    var childItem = result.Value!.Modules.Single(m => m.ObjectiveId == childId);
    Assert.Equal(67, parentItem.ProgressPercent); // subtree rollup: 4/6 via its child
    Assert.Equal(67, childItem.ProgressPercent);
}

[Fact]
public async Task Handle_ObjectiveWithNoTasksInSubtree_HasNullProgress()
{
    // Arrange: a leaf objective with zero tasks.
    var result = await _handler.Handle(new GetProjectCalendarQuery(projectId), CancellationToken.None);

    var item = result.Value!.Modules.Single(m => m.ObjectiveId == emptyObjectiveId);
    Assert.Null(item.ProgressPercent);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectCalendarQueryHandlerTests -c Release`
Expected: FAIL — `ProgressPercent` does not exist on `ProjectCalendarItemResponse`.

- [ ] **Step 3: Write minimal implementation**

In `ProjectCalendarItemResponse.cs`, add `ProgressPercent` to the record (append as the last positional parameter so existing positional constructions don't need every call site rewritten — but this handler is the only producer, so it's safe to add anywhere; put it last for minimal diff risk):

```csharp
public sealed record ProjectCalendarItemResponse(
    Guid ObjectiveId,
    Guid ProjectId,
    Guid? ParentObjectiveId,
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    bool IsAchieved,
    bool CanEdit,
    IReadOnlyList<ProjectCalendarEventLink> Events,
    int? ProgressPercent);
```

In `GetProjectCalendarQueryHandler.cs`, inject `ITaskStatusRepository` (constructor + field, same pattern as the existing injected repositories). **This adds a new required constructor parameter**, so every `new GetProjectCalendarQueryHandler(...)` call in `GetProjectCalendarQueryHandlerTests.cs` needs a mock/fake `ITaskStatusRepository` added to its argument list before anything in that file will even compile — do this first, across the whole file, before writing the new tests below. Then, before the `var modules = objectives.Select(...)` block, add:

```csharp
        var statusIds = allTasks.Select(t => t.StatusId).Distinct().ToList();
        var statuses = await _taskStatuses.GetByIdsForTenantAsync(tenantId, statusIds, ct);
        var doneStatusIds = statuses.Where(s => s.MarksTaskComplete).Select(s => s.Id).ToHashSet();
        var tasksByObjective = allTasks.ToLookup(t => t.ObjectiveId);
```

Then inside the `objectives.Select(objective => { ... })` block, before the `return new ProjectCalendarItemResponse(...)` line, add:

```csharp
            var subtreeIds = tree.AtOrBelow(new[] { objective.Id });
            var subtreeTasks = subtreeIds.SelectMany(id => tasksByObjective[id]).ToList();
            var progressPercent = ObjectiveProgressCalculator.Calculate(
                subtreeTasks.Count(t => doneStatusIds.Contains(t.StatusId)), subtreeTasks.Count);
```

And update the final `return` to pass it:

```csharp
            return new ProjectCalendarItemResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.Title,
                objective.StartDate, objective.EndDate, objective.IsActive, objective.IsAchieved, canEdit, links,
                progressPercent);
```

Add the two missing `using`s at the top of the handler file:

```csharp
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
```

In `CalendarContracts.cs`, add `ProgressPercent` to `ProjectCalendarModuleViewModel` and thread it through `ToViewModel`:

```csharp
public sealed record ProjectCalendarModuleViewModel(
    Guid ObjectiveId,
    Guid ProjectId,
    Guid? ParentObjectiveId,
    string Title,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    bool IsAchieved,
    bool CanEdit,
    IReadOnlyList<ProjectCalendarEventLinkViewModel> Events,
    int? ProgressPercent);
```

```csharp
            response.Modules.Select(m => new ProjectCalendarModuleViewModel(
                m.ObjectiveId, m.ProjectId, m.ParentObjectiveId, m.Title,
                m.StartDate, m.EndDate, m.IsActive, m.IsAchieved, m.CanEdit,
                m.Events.Select(e => new ProjectCalendarEventLinkViewModel(
                    e.EventId, e.EventName, e.EventColor, e.EventStartDate, e.EventEndDate,
                    e.Membership, e.TasksInEventCount, e.TaskTotalCount)).ToList(),
                m.ProgressPercent)).ToList(),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectCalendarQueryHandlerTests -c Release`
Expected: PASS, all tests in the file green (check no earlier test in this file positionally constructs `ProjectCalendarItemResponse` — if any do, add `, null` or the right value to each; search the file for `new ProjectCalendarItemResponse(` first).

- [ ] **Step 5: Run the Tree tab's own handler tests to confirm no side effect**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "GetObjectiveTreeQueryHandlerTests|GetObjectiveSubtreeQueryHandlerTests|GetObjectiveByIdQueryHandlerTests" -c Release`
Expected: PASS, unchanged — these don't touch `ObjectiveMapper` from this task, confirming Progress stayed scoped to Calendar only.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/ProjectCalendarItemResponse.cs src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetProjectCalendarQueryHandlerTests.cs
git commit -m "feat(wm-calendar): surface subtree task-completion progress on the calendar read"
```

---

## Part B: Backend — Milestone detail (Description column + `GET calendar-events/{id}`)

### Task B1: `Description` column + `CalendarEventActivityLog` table (one migration)

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEvent.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventConfiguration.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/CalendarEventActivityLog.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/CalendarEventActivityLogConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create (scaffolded then hand-edited): `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddCalendarEventDescriptionAndActivityLog.cs`

**Interfaces:**
- Produces: `CalendarEvent.Description : string?`; `CalendarEventActivityLog` entity (Id, TenantId, CalendarEventId, Action, PerformedById, PerformedAt, DetailsJson), used by Part D.

- [ ] **Step 1: Add the Description property and entity**

In `CalendarEvent.cs`, add after `Color`:

```csharp
    public string? Description { get; set; }
```

Create `CalendarEventActivityLog.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

public static class CalendarEventActivityActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Closed = "closed";
}

/// <summary>Audit row for a milestone's own Activity tab - same shape as Tasks.Entities.TaskEditLog,
/// kept separate because it is keyed on CalendarEventId, not TaskId.</summary>
public class CalendarEventActivityLog : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CalendarEventId { get; set; }
    public string Action { get; set; } = CalendarEventActivityActions.Created;
    public Guid PerformedById { get; set; }
    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DetailsJson { get; set; } = "{}";
}
```

In `CalendarEventConfiguration.cs`, add after the `Status` property line:

```csharp
        builder.Property(e => e.Description).HasColumnType("text");
```

Create `CalendarEventActivityLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public sealed class CalendarEventActivityLogConfiguration : IEntityTypeConfiguration<CalendarEventActivityLog>
{
    public void Configure(EntityTypeBuilder<CalendarEventActivityLog> builder)
    {
        builder.ToTable("calendar_event_activity_logs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Action).HasMaxLength(20).IsRequired();
        builder.Property(e => e.DetailsJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.CalendarEventId, e.PerformedAt })
            .HasDatabaseName("ix_calendar_event_activity_logs_tenant_event_performed_at");
        builder.HasOne<CalendarEvent>()
            .WithMany()
            .HasForeignKey(e => e.CalendarEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

In `ApplicationDbContext.cs`, add after the `CalendarEvents` DbSet line (`:300`):

```csharp
    public DbSet<CalendarEventActivityLog> CalendarEventActivityLogs => Set<CalendarEventActivityLog>();
```

- [ ] **Step 2: Scaffold the migration**

Run (Windows PowerShell, from the backend repo root):

```bash
$env:ConnectionStrings__MigrationConnection = "Host=localhost;Database=dummy;Username=dummy;Password=dummy"
dotnet ef migrations add AddCalendarEventDescriptionAndActivityLog --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release
```

If the design-time factory still fails to build without a real DB, add `--no-build` after first running `dotnet build src/ONEVO.Api -c Release`.

- [ ] **Step 3: Add the RLS policy block to the scaffolded migration's `Up`/`Down`**

Open the newly generated `<timestamp>_AddCalendarEventDescriptionAndActivityLog.cs`. Confirm it contains `AddColumn<string>("description", "calendar_events", ...)` and `CreateTable("calendar_event_activity_logs", ...)` — if EF named the table differently, rename it back to `calendar_event_activity_logs` in both the migration and `CalendarEventActivityLogConfiguration.cs` to match. Then append this block at the end of `Up()`, copied from `20260922071349_AddTaskComments.cs`'s pattern:

```csharp
            migrationBuilder.Sql(@"
                ALTER TABLE calendar_event_activity_logs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE calendar_event_activity_logs FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON calendar_event_activity_logs;
                CREATE POLICY tenant_isolation ON calendar_event_activity_logs
                    USING (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    )
                    WITH CHECK (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    );
            ");
```

And at the start of `Down()`:

```csharp
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON calendar_event_activity_logs;
                ALTER TABLE calendar_event_activity_logs DISABLE ROW LEVEL SECURITY;
            ");
```

- [ ] **Step 4: Verify no pending model changes and the architecture suite is green**

Run:
```bash
dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
```
Expected: no pending changes; `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` passes (both `calendar_events` and `calendar_event_activity_logs` covered).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/CalendarEvents/Entities/ src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(wm-calendar): add milestone description column and activity-log table with RLS"
```

### Task B2: `GET calendar-events/{id}` + `Description` on write paths

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/ProjectCalendarItemResponse.cs` (this is the file that already holds `CalendarEventResponse` — see Task A3, it's a multi-record file, not one named after `CalendarEventResponse` itself)
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CreateCalendarEvent/{CreateCalendarEventCommand.cs,CreateCalendarEventCommandHandler.cs,CreateCalendarEventCommandValidator.cs}`
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/UpdateCalendarEvent/{UpdateCalendarEventCommand.cs,UpdateCalendarEventCommandHandler.cs}`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventById/GetCalendarEventByIdQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventById/GetCalendarEventByIdQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/CalendarEventCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetCalendarEventByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventRepository.GetByIdForTenantAsync`, `.ListMembershipsForEventAsync`, `.ListTaskMembershipsForEventAsync` (all existing), `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` (existing, signature: `(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) -> Task<IReadOnlyDictionary<Guid,string>>` — confirm exact signature against `GetObjectiveTreeQueryHandler.cs:86` before using).
- Produces: `CalendarEventDetailResponse` (new record), route `GET calendar-events/{id}`.

- [ ] **Step 1: Write the failing tests**

Add to `CalendarEventCommandHandlerTests.cs` (match its existing arrange/act/assert style):

```csharp
[Fact]
public async Task CreateCalendarEvent_PersistsDescription()
{
    var command = /* existing valid create command builder, plus */ Description: "Covers the Q3 release scope.";
    var result = await _createHandler.Handle(command, CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.Equal("Covers the Q3 release scope.", result.Value!.Description);
}
```

Create `GetCalendarEventByIdQueryHandlerTests.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.CalendarEvents;

public class GetCalendarEventByIdQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDetailWithDescriptionAndOwnerName()
    {
        // Arrange: a CalendarEvent with Description set and CreatedById = some employee,
        // ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync mocked to return that employee's name.
        var result = await _handler.Handle(new GetCalendarEventByIdQuery(eventId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Covers the Q3 release scope.", result.Value!.Description);
        Assert.Equal("Priya Shankar", result.Value!.CreatedByName);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        var result = await _handler.Handle(new GetCalendarEventByIdQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "CreateCalendarEvent_PersistsDescription|GetCalendarEventByIdQueryHandlerTests" -c Release`
Expected: FAIL (no `Description` param, no such query/handler yet).

- [ ] **Step 3: Write minimal implementation**

In `ProjectCalendarItemResponse.cs`, add `Description` to `CalendarEventResponse`:

```csharp
public sealed record CalendarEventResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);
```

Add `CalendarEventDetailResponse` to the same file:

```csharp
public sealed record CalendarEventDetailResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Color,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    Guid CreatedById,
    string? CreatedByName,
    IReadOnlyList<Guid> ObjectiveIds,
    IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt,
    Guid? ArchivedById,
    DateTimeOffset? ArchivedAt);
```

In `CreateCalendarEventCommand.cs`, add `string? Description` as the last parameter. In `CreateCalendarEventCommandValidator.cs`, add:
```csharp
        RuleFor(x => x.Description).MaximumLength(2000);
```
In `CreateCalendarEventCommandHandler.cs`: add `Description = request.Description?.Trim()` to the `calendarEvent` object initializer, and add `calendarEvent.Description` as a parameter to `ToResponse` — update its signature to `internal static CalendarEventResponse ToResponse(CalendarEvent calendarEvent, IReadOnlyList<Guid> objectiveIds, IReadOnlyList<Guid> taskIds)` body to include `calendarEvent.Description` in the positional `new(...)` (after `endDate`, matching the record's new field order above). Update every call site of `ToResponse` in `UpdateCalendarEventCommandHandler.cs` and `CloseCalendarEventCommandHandler.cs` — they already just forward `calendarEvent`, so no change needed there beyond the record shape now carrying Description automatically.

In `UpdateCalendarEventCommand.cs`, add `string? Description` as the last parameter (nullable means "leave unchanged", same convention as `Name`). In `UpdateCalendarEventCommandHandler.cs`, add:
```csharp
        calendarEvent.Description = request.Description is null ? calendarEvent.Description : request.Description.Trim();
```
right after the existing `calendarEvent.Color = ...` line.

Create `GetCalendarEventByIdQuery.cs`:
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;

public sealed record GetCalendarEventByIdQuery(Guid Id) : IRequest<Result<CalendarEventDetailResponse>>;
```

Create `GetCalendarEventByIdQueryHandler.cs` (mirror `CloseCalendarEventCommandHandler`'s auth/lookup pattern):
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;

public sealed class GetCalendarEventByIdQueryHandler : IRequestHandler<GetCalendarEventByIdQuery, Result<CalendarEventDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IProjectMemberRepository _members;

    public GetCalendarEventByIdQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        ICalendarEventRepository calendarEvents, IProjectMemberRepository members)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _members = members;
    }

    public async Task<Result<CalendarEventDetailResponse>> Handle(GetCalendarEventByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<CalendarEventDetailResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<CalendarEventDetailResponse>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<CalendarEventDetailResponse>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.Id, ct);
        if (calendarEvent is null)
            return Result<CalendarEventDetailResponse>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<CalendarEventDetailResponse>.Forbidden("You do not have access to this project.");

        var memberships = await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct);
        var taskLinks = await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { calendarEvent.CreatedById }, ct);

        return Result<CalendarEventDetailResponse>.Success(new CalendarEventDetailResponse(
            calendarEvent.Id, calendarEvent.ProjectId, calendarEvent.Name, calendarEvent.Color, calendarEvent.Status,
            calendarEvent.StartDate, calendarEvent.EndDate, calendarEvent.Description,
            calendarEvent.CreatedById, names.GetValueOrDefault(calendarEvent.CreatedById),
            memberships.Select(m => m.ObjectiveId).ToList(), taskLinks.Select(l => l.TaskId).ToList(),
            calendarEvent.CreatedAt, calendarEvent.ArchivedById, calendarEvent.ArchivedAt));
    }
}
```

> Before compiling, confirm `ResolveDisplayNamesByEmployeeIdAsync`'s exact signature in `ICallerIdentityResolver` (used at `GetObjectiveTreeQueryHandler.cs:86`) and adjust the call above to match exactly if it differs (e.g. argument order, return type).

In `CalendarContracts.cs`, add the view-model + mapper:
```csharp
public sealed record CalendarEventDetailViewModel(
    Guid Id, Guid ProjectId, string Name, string Color, string Status,
    DateOnly StartDate, DateOnly EndDate, string? Description,
    Guid CreatedById, string? CreatedByName,
    IReadOnlyList<Guid> ObjectiveIds, IReadOnlyList<Guid> TaskIds,
    DateTimeOffset CreatedAt, Guid? ArchivedById, DateTimeOffset? ArchivedAt);
```
and in `CalendarViewModelMapper`:
```csharp
    public static CalendarEventDetailViewModel ToViewModel(this CalendarEventDetailResponse response)
        => new(response.Id, response.ProjectId, response.Name, response.Color, response.Status,
            response.StartDate, response.EndDate, response.Description, response.CreatedById, response.CreatedByName,
            response.ObjectiveIds, response.TaskIds, response.CreatedAt, response.ArchivedById, response.ArchivedAt);
```
Also update the existing `CalendarEventResponse`'s `ToViewModel` and its `CalendarEventViewModel` record to carry `Description` through (same pattern as above — add the field to both and the mapper body).

In `CalendarController.cs`, add:
```csharp
    [HttpGet("calendar-events/{id:guid}")]
    public async Task<IActionResult> GetEvent(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById.GetCalendarEventByIdQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
(add the `using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventById;` to the top imports instead of the inline fully-qualified name, for consistency with the file's other usings).

Also update `CreateCalendarEventRequest`/`UpdateCalendarEventRequest` in `CalendarContracts.cs` to carry `string? Description`, and the controller's `CreateEvent`/`UpdateEvent` actions to pass `request.Description` into the command constructors.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "CalendarEventCommandHandlerTests|GetCalendarEventByIdQueryHandlerTests" -c Release`
Expected: PASS. Fix any other test in `CalendarEventCommandHandlerTests.cs` that positionally constructs `CalendarEventResponse` without the new `Description` slot.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/ src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/
git commit -m "feat(wm-calendar): add milestone detail endpoint and description field"
```

---

## Part C: Backend — Tasks-by-milestone endpoint

### Task C1: `GET calendar-events/{id}/tasks`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/CalendarEventTaskSummaryResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventTasks/GetCalendarEventTasksQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventTasks/GetCalendarEventTasksQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetCalendarEventTasksQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetByProjectAsync` (existing — reused, no new repository method, per the Design §2 decision), `ICalendarEventRepository.ListMembershipsForEventAsync`/`.ListTaskMembershipsForEventAsync` (existing), `ITaskStatusRepository.GetByIdsForTenantAsync` (Task A2, reused for `MarksTaskComplete` and `Category`), `IModuleReadAccess.CanReadAsync` and `IPermissionResolver.ResolveAsync` (existing — same pair `GetObjectiveTasksQueryHandler` uses).
- Produces: `CalendarEventTaskSummaryResponse`, route `GET calendar-events/{id}/tasks`.

**Authorization note:** a milestone's tasks can span several objectives (one per whole-linked objective, plus whichever objectives the directly-picked tasks happen to live on). Checking only project membership (as the draft of this task originally did) is weaker than `GetObjectiveTasksQueryHandler`'s own per-objective `IModuleReadAccess` gate — a plain project member could otherwise see tasks from an objective they have no read access to via this new endpoint even though the single-objective endpoint would 403 them. Mirror that gate here, once per distinct objective among the member tasks, keeping the existing `"*"` permission bypass (its absence was a real, previously-fixed bug elsewhere — see `[[feedback_hrms_wm_parent_cascade_read_access]]` in project memory — don't reintroduce a coverage-scoped-only check).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_ReturnsUnionOfDirectPicksAndWholeLinkedObjectiveTasks()
{
    // Arrange: event has one whole-linked objective (3 tasks) and one directly-picked task
    // from a different, not-whole-linked objective. Caller has read access to both.
    var result = await _handler.Handle(new GetCalendarEventTasksQuery(eventId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal(4, result.Value!.Count); // 3 + 1, no duplicates
}

[Fact]
public async Task Handle_EventWithNoLinkedTasks_ReturnsEmptyList()
{
    var result = await _handler.Handle(new GetCalendarEventTasksQuery(eventWithNoLinksId), CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.Empty(result.Value!);
}

[Fact]
public async Task Handle_FiltersOutTasksFromObjectivesTheCallerCannotRead()
{
    // Arrange: event links two objectives' tasks; mock IModuleReadAccess.CanReadAsync to return
    // false for one of them and the caller's permissions to not include "*".
    var result = await _handler.Handle(new GetCalendarEventTasksQuery(eventId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.All(result.Value!, t => Assert.NotEqual(unreadableObjectiveId, t.ObjectiveId));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetCalendarEventTasksQueryHandlerTests -c Release`
Expected: FAIL — handler doesn't exist.

- [ ] **Step 3: Write minimal implementation**

Create `CalendarEventTaskSummaryResponse.cs`:
```csharp
namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

/// <summary>A lighter task shape for the milestone Tasks tab and its Key Metrics tile - deliberately
/// not the full WorkTaskResponse (no assignees/clock-session plumbing needed here). StatusCategory
/// (not just the done flag) lets the UI break Key Metrics into not_started/active/done, not just
/// done-vs-everything-else.</summary>
public sealed record CalendarEventTaskSummaryResponse(
    Guid Id, string ShortId, string Title, Guid StatusId, bool MarksTaskComplete, string StatusCategory,
    int ProgressPercent, Guid ObjectiveId, string ObjectiveTitle);
```

Create `GetCalendarEventTasksQuery.cs`:
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;

public sealed record GetCalendarEventTasksQuery(Guid CalendarEventId) : IRequest<Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>>;
```

Create `GetCalendarEventTasksQueryHandler.cs`:
```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;

public sealed class GetCalendarEventTasksQueryHandler : IRequestHandler<GetCalendarEventTasksQuery, Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleReadAccess _readAccess;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _taskStatuses;

    public GetCalendarEventTasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ICalendarEventRepository calendarEvents,
        IProjectMemberRepository members, IObjectiveRepository objectives, IModuleReadAccess readAccess,
        IPermissionResolver permissionResolver, IWorkTaskRepository tasks, ITaskStatusRepository taskStatuses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _members = members;
        _objectives = objectives;
        _readAccess = readAccess;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _taskStatuses = taskStatuses;
    }

    public async Task<Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>> Handle(GetCalendarEventTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.CalendarEventId, ct);
        if (calendarEvent is null)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("You do not have access to this project.");

        var wholeObjectiveIds = (await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct))
            .Select(m => m.ObjectiveId).ToHashSet();
        var directTaskIds = (await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct))
            .Select(l => l.TaskId).ToHashSet();

        var projectTasks = await _tasks.GetByProjectAsync(tenantId, calendarEvent.ProjectId, ct);
        var memberTasks = projectTasks
            .Where(t => wholeObjectiveIds.Contains(t.ObjectiveId) || directTaskIds.Contains(t.Id))
            .ToList();
        if (memberTasks.Count == 0)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(Array.Empty<CalendarEventTaskSummaryResponse>());

        // Same per-objective read gate GetObjectiveTasksQueryHandler uses - a milestone can span
        // several objectives, and project membership alone is a weaker check than that endpoint's.
        var permissions = await _permissionResolver.ResolveAsync(_currentUser.UserId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");
        if (!hasReadPermission)
        {
            var objectivesById = (await _objectives.GetAllByProjectIdAsync(tenantId, calendarEvent.ProjectId, ct))
                .ToDictionary(o => o.Id);
            var readableObjectiveIds = new HashSet<Guid>();
            foreach (var objectiveId in memberTasks.Select(t => t.ObjectiveId).Distinct())
            {
                if (objectivesById.TryGetValue(objectiveId, out var objective)
                    && await _readAccess.CanReadAsync(tenantId, objective, employeeId.Value, ct))
                    readableObjectiveIds.Add(objectiveId);
            }
            memberTasks = memberTasks.Where(t => readableObjectiveIds.Contains(t.ObjectiveId)).ToList();
        }
        if (memberTasks.Count == 0)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(Array.Empty<CalendarEventTaskSummaryResponse>());

        var statuses = await _taskStatuses.GetByIdsForTenantAsync(tenantId, memberTasks.Select(t => t.StatusId).Distinct().ToList(), ct);
        var statusById = statuses.ToDictionary(s => s.Id);

        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, calendarEvent.ProjectId, ct);
        var titleByObjectiveId = objectives.ToDictionary(o => o.Id, o => o.Title);

        var responses = memberTasks.Select(t =>
        {
            var status = statusById.GetValueOrDefault(t.StatusId);
            return new CalendarEventTaskSummaryResponse(
                t.Id, t.ShortId, t.Title, t.StatusId, status?.MarksTaskComplete ?? false, status?.Category ?? "not_started",
                t.ProgressPercent, t.ObjectiveId, titleByObjectiveId.GetValueOrDefault(t.ObjectiveId, ""));
        }).ToList();

        return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(responses);
    }
}
```

> `_objectives.GetAllByProjectIdAsync` is now called twice in this method (once for the permission filter, once for titles) — if that read is expensive, hoist it to a single call before the `hasReadPermission` block and reuse the dictionary for both; left as two calls above only for clarity of the diff against the no-filter version.

In `CalendarContracts.cs`, add:
```csharp
public sealed record CalendarEventTaskSummaryViewModel(
    Guid Id, string ShortId, string Title, Guid StatusId, bool MarksTaskComplete, string StatusCategory,
    int ProgressPercent, Guid ObjectiveId, string ObjectiveTitle);

public static class CalendarEventTaskSummaryViewModelMapper
{
    public static CalendarEventTaskSummaryViewModel ToViewModel(this CalendarEventTaskSummaryResponse r)
        => new(r.Id, r.ShortId, r.Title, r.StatusId, r.MarksTaskComplete, r.StatusCategory, r.ProgressPercent, r.ObjectiveId, r.ObjectiveTitle);
}
```

In `CalendarController.cs`, add:
```csharp
    [HttpGet("calendar-events/{id:guid}/tasks")]
    public async Task<IActionResult> GetEventTasks(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCalendarEventTasksQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
(add `using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;` to the controller's usings)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetCalendarEventTasksQueryHandlerTests -c Release`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/ src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetCalendarEventTasksQueryHandlerTests.cs
git commit -m "feat(wm-calendar): add milestone tasks endpoint"
```

---

## Part D: Backend — Activity log (CalendarEvent only, per Design §8)

### Task D1: `ICalendarEventActivityLogRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/RepositoryInterfaces/ICalendarEventActivityLogRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfCalendarEventActivityLogRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/EfCalendarEventActivityLogRepositoryTests.cs`

**Interfaces:**
- Produces: `ICalendarEventActivityLogRepository.AddAsync(CalendarEventActivityLog, CancellationToken)`, `.ListByEventIdAsync(Guid calendarEventId, CancellationToken) -> Task<IReadOnlyList<CalendarEventActivityLog>>`, used by Tasks D2/D3.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task AddAsync_ThenListByEventIdAsync_ReturnsNewestFirst()
{
    await using var db = CreateInMemoryDbContext();
    var repo = new EfCalendarEventActivityLogRepository(db);
    var eventId = Guid.NewGuid();
    var older = new CalendarEventActivityLog { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = eventId, Action = CalendarEventActivityActions.Created, PerformedById = Guid.NewGuid(), PerformedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
    var newer = new CalendarEventActivityLog { Id = Guid.NewGuid(), TenantId = TenantId, CalendarEventId = eventId, Action = CalendarEventActivityActions.Updated, PerformedById = Guid.NewGuid(), PerformedAt = DateTimeOffset.UtcNow };
    await repo.AddAsync(older, CancellationToken.None);
    await repo.AddAsync(newer, CancellationToken.None);
    await db.SaveChangesAsync();

    var result = await repo.ListByEventIdAsync(eventId, CancellationToken.None);

    Assert.Equal(new[] { newer.Id, older.Id }, result.Select(r => r.Id));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter AddAsync_ThenListByEventIdAsync_ReturnsNewestFirst -c Release`
Expected: FAIL — interface/class don't exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// ICalendarEventActivityLogRepository.cs
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

public interface ICalendarEventActivityLogRepository
{
    Task AddAsync(CalendarEventActivityLog log, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventActivityLog>> ListByEventIdAsync(Guid calendarEventId, CancellationToken ct = default);
}
```

```csharp
// EfCalendarEventActivityLogRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfCalendarEventActivityLogRepository : ICalendarEventActivityLogRepository
{
    private readonly ApplicationDbContext _db;
    public EfCalendarEventActivityLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEventActivityLog log, CancellationToken ct = default)
        => await _db.CalendarEventActivityLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<CalendarEventActivityLog>> ListByEventIdAsync(Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventActivityLogs.AsNoTracking()
            .Where(l => l.CalendarEventId == calendarEventId)
            .OrderByDescending(l => l.PerformedAt)
            .ToListAsync(ct);
}
```

In `DependencyInjection.cs`, add near the existing `ICalendarEventRepository` registration (`:350`):
```csharp
        services.AddScoped<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventActivityLogRepository, EfCalendarEventActivityLogRepository>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter AddAsync_ThenListByEventIdAsync_ReturnsNewestFirst -c Release`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/RepositoryInterfaces/ICalendarEventActivityLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfCalendarEventActivityLogRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/EfCalendarEventActivityLogRepositoryTests.cs
git commit -m "feat(wm-calendar): add calendar event activity log repository"
```

### Task D2: Write activity rows from Create/Update/Close handlers

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CreateCalendarEvent/CreateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/UpdateCalendarEvent/UpdateCalendarEventCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/CloseCalendarEvent/CloseCalendarEventCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/CalendarEventCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventActivityLogRepository.AddAsync` (Task D1).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task CreateCalendarEvent_WritesCreatedActivityLog()
{
    var result = await _createHandler.Handle(ValidCreateCommand(), CancellationToken.None);
    _activityLogRepoMock.Verify(r => r.AddAsync(
        It.Is<CalendarEventActivityLog>(l => l.Action == CalendarEventActivityActions.Created && l.CalendarEventId == result.Value!.Id),
        It.IsAny<CancellationToken>()), Times.Once);
}
```
(Add the mirror tests `UpdateCalendarEvent_WritesUpdatedActivityLog` and `CloseCalendarEvent_WritesClosedActivityLog` the same way, matching whatever mocking framework — Moq, per the `_activityLogRepoMock.Verify` call above — this test file already uses for its other repository mocks.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "WritesCreatedActivityLog|WritesUpdatedActivityLog|WritesClosedActivityLog" -c Release`
Expected: FAIL — handlers don't call the new repository yet (constructors don't even take it).

- [ ] **Step 3: Write minimal implementation**

In each of the three handlers: add `ICalendarEventActivityLogRepository _activityLogs` as a constructor parameter/field (same pattern as the other injected repositories), and inside the existing `_unitOfWork.ExecuteInTransactionAsync(async innerCt => { ... }, ct)` block, right before `await _unitOfWork.SaveChangesAsync(innerCt);`, add one call:

Create handler:
```csharp
            await _activityLogs.AddAsync(new CalendarEventActivityLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = calendarEvent.Id,
                Action = CalendarEventActivityActions.Created, PerformedById = actorResult.EmployeeId,
                PerformedAt = now, DetailsJson = $"{{\"name\":{System.Text.Json.JsonSerializer.Serialize(calendarEvent.Name)}}}"
            }, innerCt);
```

Update handler (place after the existing field-assignment lines, inside the same transaction block):
```csharp
            await _activityLogs.AddAsync(new CalendarEventActivityLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventId = calendarEvent.Id,
                Action = CalendarEventActivityActions.Updated, PerformedById = actorResult.EmployeeId,
                PerformedAt = now, DetailsJson = $"{{\"name\":{System.Text.Json.JsonSerializer.Serialize(calendarEvent.Name)}}}"
            }, innerCt);
```

Close handler:
```csharp
            await _activityLogs.AddAsync(new CalendarEventActivityLog
            {
                Id = Guid.NewGuid(), TenantId = _currentUser.TenantId, CalendarEventId = calendarEvent.Id,
                Action = CalendarEventActivityActions.Closed, PerformedById = employeeId.Value,
                PerformedAt = calendarEvent.ArchivedAt!.Value, DetailsJson = "{}"
            }, innerCt);
```
(add `using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;` and `using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;` to each file if not already present — `CreateCalendarEventCommandHandler.cs` already has both).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "CalendarEventCommandHandlerTests" -c Release`
Expected: PASS, all tests in the file (update every existing handler-construction call site in this test file to pass a mock `ICalendarEventActivityLogRepository` — the constructors now require it).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Commands/ tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/CalendarEventCommandHandlerTests.cs
git commit -m "feat(wm-calendar): record activity log entries on create/update/close"
```

### Task D3: `GET calendar-events/{id}/activity`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/DTOs/Responses/CalendarEventActivityEntryResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventActivity/GetCalendarEventActivityQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/CalendarEvents/Queries/GetCalendarEventActivity/GetCalendarEventActivityQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetCalendarEventActivityQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ICalendarEventActivityLogRepository.ListByEventIdAsync` (Task D1), `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`.
- Produces: `CalendarEventActivityEntryResponse`, route `GET calendar-events/{id}/activity`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Handle_ReturnsEntriesWithResolvedPerformerNames_NewestFirst()
{
    var result = await _handler.Handle(new GetCalendarEventActivityQuery(eventId), CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.Equal("updated", result.Value![0].Action);
    Assert.Equal("Priya Shankar", result.Value![0].PerformedByName);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetCalendarEventActivityQueryHandlerTests -c Release`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

```csharp
// CalendarEventActivityEntryResponse.cs
namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

public sealed record CalendarEventActivityEntryResponse(
    Guid Id, string Action, Guid PerformedById, string? PerformedByName, DateTimeOffset PerformedAt, string DetailsJson);
```

```csharp
// GetCalendarEventActivityQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;

public sealed record GetCalendarEventActivityQuery(Guid CalendarEventId) : IRequest<Result<IReadOnlyList<CalendarEventActivityEntryResponse>>>;
```

```csharp
// GetCalendarEventActivityQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;

public sealed class GetCalendarEventActivityQueryHandler : IRequestHandler<GetCalendarEventActivityQuery, Result<IReadOnlyList<CalendarEventActivityEntryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly ICalendarEventActivityLogRepository _activityLogs;
    private readonly IProjectMemberRepository _members;

    public GetCalendarEventActivityQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ICalendarEventRepository calendarEvents,
        ICalendarEventActivityLogRepository activityLogs, IProjectMemberRepository members)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _activityLogs = activityLogs;
        _members = members;
    }

    public async Task<Result<IReadOnlyList<CalendarEventActivityEntryResponse>>> Handle(GetCalendarEventActivityQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.CalendarEventId, ct);
        if (calendarEvent is null)
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Forbidden("You do not have access to this project.");

        var entries = await _activityLogs.ListByEventIdAsync(calendarEvent.Id, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, entries.Select(e => e.PerformedById).Distinct().ToList(), ct);

        var responses = entries.Select(e => new CalendarEventActivityEntryResponse(
            e.Id, e.Action, e.PerformedById, names.GetValueOrDefault(e.PerformedById), e.PerformedAt, e.DetailsJson)).ToList();

        return Result<IReadOnlyList<CalendarEventActivityEntryResponse>>.Success(responses);
    }
}
```

In `CalendarContracts.cs`:
```csharp
public sealed record CalendarEventActivityEntryViewModel(
    Guid Id, string Action, Guid PerformedById, string? PerformedByName, DateTimeOffset PerformedAt, string DetailsJson);

public static class CalendarEventActivityEntryViewModelMapper
{
    public static CalendarEventActivityEntryViewModel ToViewModel(this CalendarEventActivityEntryResponse r)
        => new(r.Id, r.Action, r.PerformedById, r.PerformedByName, r.PerformedAt, r.DetailsJson);
}
```

In `CalendarController.cs`:
```csharp
    [HttpGet("calendar-events/{id:guid}/activity")]
    public async Task<IActionResult> GetEventActivity(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCalendarEventActivityQuery(id), ct);
        return result.IsSuccess
            ? Ok(result.Value!.Select(e => e.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```
(add `using ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventActivity;`)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetCalendarEventActivityQueryHandlerTests -c Release`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/CalendarEvents/ src/ONEVO.Api/Contracts/WorkManagement/CalendarEvents/CalendarContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/CalendarController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/CalendarEvents/GetCalendarEventActivityQueryHandlerTests.cs
git commit -m "feat(wm-calendar): add milestone activity endpoint"
```

---

## Part E: Backend — Full-suite gate

### Task E1: Verify the whole backend suite

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release`
Expected: PASS, including the pre-existing counts plus every test added in Parts A-D.

- [ ] **Step 2: Run the architecture suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture -c Release`
Expected: PASS — in particular `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` (covers `calendar_event_activity_logs` from Task B1) and whatever SQLite-naming / forbidden-pattern tests exist (don't introduce the literal word "SQLite" anywhere under `src/`).

- [ ] **Step 3: Build Release**

Run: `dotnet build src/ONEVO.Api -c Release`
Expected: 0 errors. (If NuGet.targets complains about a null path, it's a stale MSBuild server — run `dotnet build-server shutdown` and retry, not a code problem.)

- [ ] **Step 4: Record the baseline for handoff**

Note the exact passing counts from Steps 1-2 (e.g. "5,XXX/5,XXX unit, 7XX/7XX architecture") — the final report to the user must state these alongside the new counts so a regression is visible.

- [ ] **Step 5: Commit** (only if Steps 1-3 required any fix-up changes; otherwise skip — this task is a gate, not a code change)

---

## Part F: Frontend — DTOs and API service

### Task F1: New/extended DTOs and `CalendarApiService` methods

**Files:**
- Modify: `src/app/modules/work/models/dto/calendar.dto.ts`
- Modify: `src/app/modules/work/data-access/calendar-api.service.ts`
- Modify: `src/app/modules/work/data-access/calendar-api.service.spec.ts` (if it exists — check `project-api.service.spec.ts` for the sibling pattern if `calendar-api.service.spec.ts` doesn't exist yet, and create it following that pattern)

**Interfaces:**
- Produces: `ProjectCalendarModuleDto.progressPercent: number | null`; `CalendarEventDto.description?: string | null`, `.createdByName?: string | null`; new `CalendarEventTaskSummaryDto`, `CalendarEventActivityEntryDto`; `CalendarApiService.getEvent(id)`, `.getEventTasks(id)`, `.getEventActivity(id)`.

- [ ] **Step 1: Write the failing test**

```typescript
it('getEvent fetches a single calendar event detail by id', () => {
  service.getEvent('event-1').subscribe();
  const req = httpMock.expectOne('/api/v1/work/calendar-events/event-1');
  expect(req.request.method).toBe('GET');
  req.flush({ id: 'event-1', projectId: 'p1', name: 'Release 1', color: '#2563EB', status: 'active', startDate: '2026-10-01', endDate: '2026-10-31', description: null, createdById: 'e1', createdByName: 'Priya Shankar', objectiveIds: [], taskIds: [], createdAt: '2026-10-01T00:00:00Z', archivedById: null, archivedAt: null });
});

it('getEventTasks fetches the tasks linked to a milestone', () => {
  service.getEventTasks('event-1').subscribe();
  const req = httpMock.expectOne('/api/v1/work/calendar-events/event-1/tasks');
  expect(req.request.method).toBe('GET');
  req.flush([]);
});

it('getEventActivity fetches the activity log for a milestone', () => {
  service.getEventActivity('event-1').subscribe();
  const req = httpMock.expectOne('/api/v1/work/calendar-events/event-1/activity');
  expect(req.request.method).toBe('GET');
  req.flush([]);
});
```

(Match this test file's existing `TestBed`/`HttpClientTestingModule` setup exactly — copy its `beforeEach` verbatim rather than re-deriving it.)

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=calendar-api.service.spec`
Expected: FAIL — methods don't exist.

- [ ] **Step 3: Write minimal implementation**

In `calendar.dto.ts`, modify `ProjectCalendarModuleDto` (add after `allocatedHours?`):
```typescript
  progressPercent?: number | null;
```
Modify `CalendarEventDto` (add after `objectiveIds`/`taskIds`, before `createdAt?`):
```typescript
  description?: string | null;
  createdByName?: string | null;
```
Add new types at the end of the file:
```typescript
export interface CalendarEventTaskSummaryDto {
  id: string;
  shortId: string;
  title: string;
  statusId: string;
  marksTaskComplete: boolean;
  /** 'not_started' | 'active' | 'done' - backs the Key Metrics breakdown (In Progress is
   *  specifically 'active', not "everything that isn't done", so not-started tasks don't
   *  get miscounted as in progress). */
  statusCategory: string;
  progressPercent: number;
  objectiveId: string;
  objectiveTitle: string;
}

export interface CalendarEventActivityEntryDto {
  id: string;
  action: 'created' | 'updated' | 'closed' | string;
  performedById: string;
  performedByName: string | null;
  performedAt: string;
  detailsJson: string;
}
```

In `calendar-api.service.ts`, find the existing `createEvent`/`updateEvent`/`closeEvent` methods and add three siblings in the same style (same base URL constant, same `HttpClient` injection already in the file):
```typescript
  getEvent(id: string): Observable<CalendarEventDto> {
    return this.http.get<CalendarEventDto>(`${this.baseUrl}/calendar-events/${id}`);
  }

  getEventTasks(id: string): Observable<CalendarEventTaskSummaryDto[]> {
    return this.http.get<CalendarEventTaskSummaryDto[]>(`${this.baseUrl}/calendar-events/${id}/tasks`);
  }

  getEventActivity(id: string): Observable<CalendarEventActivityEntryDto[]> {
    return this.http.get<CalendarEventActivityEntryDto[]>(`${this.baseUrl}/calendar-events/${id}/activity`);
  }
```
(Match the exact `baseUrl`/injected-client field names already in the file — read it first and use its real names, not `this.baseUrl`/`this.http` if those aren't what it actually calls them.) Add `CalendarEventTaskSummaryDto, CalendarEventActivityEntryDto` to the existing import from `../models/dto/calendar.dto`.

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=calendar-api.service.spec`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/models/dto/calendar.dto.ts src/app/modules/work/data-access/calendar-api.service.ts src/app/modules/work/data-access/calendar-api.service.spec.ts
git commit -m "feat(wm-calendar): add milestone detail/tasks/activity API methods and DTOs"
```

### Task F2: Description field on the create/edit milestone modal

The backend (Task B2) and the DTOs (Task F1) carry `description` end to end, but nothing in the UI lets a user type one or shows it back — without this task, the Overview tab's description (Task I3) is always empty and the manual verification checklist (Task L, step 4) has nothing to check.

**Files:**
- Modify: `src/app/modules/work/models/dto/calendar.dto.ts`
- Modify: `src/app/modules/work/ui/calendar-event-modal/calendar-event-modal.component.ts`
- Modify: `src/app/modules/work/ui/calendar-event-modal/calendar-event-modal.component.spec.ts`

**Interfaces:**
- Produces: `CreateCalendarEventRequestDto.description?: string`, `UpdateCalendarEventRequestDto.description?: string`; the modal's `save` output payload now includes `description`.

- [ ] **Step 1: Write the failing test**

```typescript
it('includes the typed description in the save payload', () => {
  fixture.componentRef.setInput('modules', []);
  fixture.detectChanges();
  component.name.set('Release 1');
  component.color.set('#2563EB');
  component.startDate.set('2026-10-01');
  component.endDate.set('2026-10-31');
  component.selectedObjectiveIds.set(new Set(['o1']));
  fixture.nativeElement.querySelector('[data-testid="event-description"]').value = 'Covers the Q3 release scope.';
  fixture.nativeElement.querySelector('[data-testid="event-description"]').dispatchEvent(new Event('input'));
  fixture.detectChanges();

  let saved: any;
  component.save.subscribe((payload: any) => (saved = payload));
  fixture.nativeElement.querySelector('[data-testid="event-save"]').click();

  expect(saved.description).toBe('Covers the Q3 release scope.');
});

it('pre-fills the description textarea when editing an existing event', () => {
  fixture.componentRef.setInput('event', { id: 'e1', projectId: 'p1', name: 'R1', color: '#2563EB', status: 'active', startDate: '2026-10-01', endDate: '2026-10-31', description: 'Existing text.', objectiveIds: [], taskIds: [] });
  fixture.componentRef.setInput('modules', []);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="event-description"]').value).toBe('Existing text.');
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=calendar-event-modal`
Expected: FAIL — no `event-description` field exists yet.

- [ ] **Step 3: Write minimal implementation**

In `calendar.dto.ts`, add `description?: string;` to both `CreateCalendarEventRequestDto` and `UpdateCalendarEventRequestDto` (and `description?: string | null;` to `CalendarEventDto` if Task F1 didn't already add it there — check first, F1 did add it).

In `calendar-event-modal.component.ts`, add `readonly description = signal('');` next to the other signals, add this textarea right after the Color `<label>` block and before the task-picker `<div>`:

```html
        <label class="mb-4 block text-sm text-[var(--color-text-secondary)]">Description
          <textarea
            class="mt-1 w-full rounded-md border border-[var(--color-border)] bg-transparent px-3 py-2 text-[var(--color-text-primary)]"
            rows="3" maxlength="2000"
            [value]="description()"
            (input)="description.set($any($event.target).value)"
            data-testid="event-description"
          ></textarea>
        </label>
```

In the `effect()` inside the constructor, add `this.description.set(event?.description ?? '');` alongside the other `this.X.set(event?.X ?? ...)` lines. In `submit()`, add `description: this.description().trim()` to the emitted object — **not** `|| undefined`: the backend's Update handler treats an explicit `null`/missing `description` as "leave unchanged" (same convention as `Name`/`Color`), so always sending the box's current trimmed value (including `''`) is what lets a user actually clear a previously-set description; mapping `''` to `undefined` would make "never typed one" and "deleted the existing one" indistinguishable and silently un-clearable.

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=calendar-event-modal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/models/dto/calendar.dto.ts src/app/modules/work/ui/calendar-event-modal/
git commit -m "feat(wm-calendar): add description field to the create/edit milestone modal"
```

---

## Part G: Frontend — Gray default, proportional fill, diamond markers

### Task G1: `ObjectiveTimelineBarComponent` — stacked fill instead of whole-bar tint

**Files:**
- Modify: `src/app/modules/work/ui/objective-timeline-bar/objective-timeline-bar.component.ts`
- Modify: `src/app/modules/work/ui/objective-timeline-bar/objective-timeline-bar.component.spec.ts`

**Interfaces:**
- Produces: `fillSegments: Signal<{ color: string; leftPct: number; widthPct: number }[]>`, replacing the whole-bar `barColor()` tint for linked modules (unlinked modules now render flat neutral gray, matching Design §4's stacking rule).

- [ ] **Step 1: Write the failing test**

```typescript
it('renders a single 100%-width segment when the module is whole-linked to an event', () => {
  fixture.componentRef.setInput('item', { ...baseItem, events: [{ eventId: 'e1', eventName: 'R1', eventColor: '#2563EB', eventStartDate: '2026-10-01', eventEndDate: '2026-10-31', membership: 'whole', tasksInEventCount: 5, taskTotalCount: 5 }] });
  fixture.detectChanges();
  const segments = fixture.nativeElement.querySelectorAll('[data-testid="timeline-bar-fill-segment"]');
  expect(segments.length).toBe(1);
  expect(segments[0].style.width).toBe('100%');
  expect(segments[0].style.backgroundColor).toBeTruthy();
});

it('stacks two partial segments without exceeding 100%', () => {
  fixture.componentRef.setInput('item', {
    ...baseItem,
    events: [
      { eventId: 'e1', eventName: 'R1', eventColor: '#2563EB', eventStartDate: '2026-10-01', eventEndDate: '2026-10-10', membership: 'partial', tasksInEventCount: 4, taskTotalCount: 10 },
      { eventId: 'e2', eventName: 'R2', eventColor: '#16A34A', eventStartDate: '2026-10-11', eventEndDate: '2026-10-20', membership: 'partial', tasksInEventCount: 6, taskTotalCount: 10 }
    ]
  });
  fixture.detectChanges();
  const segments: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="timeline-bar-fill-segment"]'));
  expect(segments.length).toBe(2);
  expect(segments[0].style.left).toBe('0%');
  expect(segments[0].style.width).toBe('40%');
  expect(segments[1].style.left).toBe('40%');
  expect(segments[1].style.width).toBe('60%');
});

it('renders no fill segment and a gray bar when the module has no event links', () => {
  fixture.componentRef.setInput('item', { ...baseItem, events: [] });
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelectorAll('[data-testid="timeline-bar-fill-segment"]').length).toBe(0);
  const bar = fixture.nativeElement.querySelector('.timeline-bar');
  expect(getComputedStyle(bar).getPropertyValue('--bar-color').trim()).toBe('#94A3B8');
});

it('does not divide by zero when a linked module has zero total tasks', () => {
  fixture.componentRef.setInput('item', { ...baseItem, events: [{ eventId: 'e1', eventName: 'R1', eventColor: '#2563EB', eventStartDate: '2026-10-01', eventEndDate: '2026-10-10', membership: 'partial', tasksInEventCount: 0, taskTotalCount: 0 }] });
  expect(() => fixture.detectChanges()).not.toThrow();
  expect(fixture.nativeElement.querySelectorAll('[data-testid="timeline-bar-fill-segment"]').length).toBe(0);
});
```

(Match this spec file's existing `baseItem` fixture object and `fixture`/`TestBed` setup exactly — these tests assume one already exists, per the file you're editing.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=objective-timeline-bar`
Expected: FAIL — no `data-testid="timeline-bar-fill-segment"` elements exist yet.

- [ ] **Step 3: Write minimal implementation**

Replace the `primaryEventLink`/`barColor`/`partialBadge` computeds and the template's color binding in `objective-timeline-bar.component.ts`:

```typescript
interface FillSegment { color: string; leftPct: number; widthPct: number; }

  /** Stacked colored segments for this bar's event links (Design §4): a whole link is always
   *  100% in its own color; otherwise each partial link gets a left-to-right segment sized to
   *  its own task coverage, clamped so segments never sum past 100%. */
  readonly fillSegments = computed<FillSegment[]>(() => {
    const links = this.item().events ?? [];
    const whole = links.find((l) => l.membership === 'whole');
    if (whole) return [{ color: whole.eventColor, leftPct: 0, widthPct: 100 }];
    const segments: FillSegment[] = [];
    let cursor = 0;
    for (const link of links) {
      if (cursor >= 100) break;
      const pct = link.taskTotalCount > 0 ? (link.tasksInEventCount / link.taskTotalCount) * 100 : 0;
      const width = Math.min(pct, 100 - cursor);
      if (width <= 0) continue;
      segments.push({ color: link.eventColor, leftPct: cursor, widthPct: width });
      cursor += width;
    }
    return segments;
  });

  private static readonly NEUTRAL_GRAY = '#94A3B8';

  readonly barColor = () => {
    if (this.item().isAchieved) return '#64748B';
    if (this.isRoot()) return '#1E293B';
    return ObjectiveTimelineBarComponent.NEUTRAL_GRAY;
  };

  /** "N/M" when the bar has exactly one partial link, else null - kept for a text fallback
   *  alongside the visual fill, same spot it rendered in before. */
  readonly partialBadge = computed(() => {
    const links = this.item().events ?? [];
    if (links.length !== 1 || links[0].membership !== 'partial') return null;
    return `${links[0].tasksInEventCount}/${links[0].taskTotalCount}`;
  });
```

Remove the old `primaryEventLink` computed entirely (its two uses above replace it). **Leave the `readonly branchColor = input<string | null>(null);` line in place for this task** — Task G2 deletes both the input and its caller binding together in one commit, so this task's own build stays green on its own (removing only the input here, before G2 removes the binding, would break `ng build` between commits). In the template, add the fill-segment layer right after the opening `<div class="timeline-bar" ...>` tag and before the resize buttons:

```html
      @for (segment of fillSegments(); track segment.color + segment.leftPct) {
        <div
          class="timeline-bar__fill"
          data-testid="timeline-bar-fill-segment"
          [style.left.%]="segment.leftPct"
          [style.width.%]="segment.widthPct"
          [style.backgroundColor]="segment.color"
        ></div>
      }
```

Add its CSS next to `.timeline-bar__resize`:
```css
    .timeline-bar__fill { position: absolute; top: 0; bottom: 0; z-index: 0; opacity: .85; }
```
And ensure `.timeline-bar__content` keeps `z-index: 1` so the title/badges stay above the fill — add `position: relative; z-index: 1;` to the existing `.timeline-bar__content` rule.

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=objective-timeline-bar`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/objective-timeline-bar/
git commit -m "feat(wm-calendar): stacked proportional milestone fill on timeline bars"
```

### Task G2: Remove band overlay and branch colors, add diamond markers

**Files:**
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.spec.ts`

**Interfaces:**
- Consumes: `fillSegments`/new `barColor` from Task G1 (no direct call — `app-objective-timeline-bar` already reads `item().events`, so no template wiring change needed there beyond removing the now-unused `[branchColor]` input binding).
- Removes: `BRANCH_PALETTE`, `branchColors`, `bandRects`, the `.project-calendar__bands` template block, the `rangeToPercent` import.
- Produces: one diamond marker per `(row, eventLink)` pair, `data-testid="calendar-milestone-marker"`.

- [ ] **Step 1: Write the failing test**

```typescript
it('shows no full-width milestone band overlay', () => {
  // ... existing fixture setup that previously populated store.bands() ...
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('.project-calendar__band')).toBeNull();
});

it('renders a diamond marker on a row linked to a milestone, at the link end date', () => {
  // ... seed store.modules() with one module whose events[] has one link, endDate known ...
  fixture.detectChanges();
  const marker = fixture.nativeElement.querySelector('[data-testid="calendar-milestone-marker"]');
  expect(marker).toBeTruthy();
  expect(marker.title).toContain('R1');
});

it('rows with no event link render no diamond marker', () => {
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelectorAll('[data-testid="calendar-milestone-marker"]').length).toBe(0);
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: the band-removal test FAILs (band still renders) and the new marker tests FAIL (no such test id yet). Some existing tests asserting `.project-calendar__band`/`calendar-chevron`'s old branch-colored look may also need updating in this same pass — read the current spec file first and remove/adjust any assertion that depended on `BRANCH_PALETTE`/band rendering.

- [ ] **Step 3: Write minimal implementation**

In `project-calendar.component.ts`:
1. Delete the `BRANCH_PALETTE` constant (top of file).
2. Delete the `branchColors` computed and the `bandRects` computed.
3. Delete the `@if (bandRects().length) { ... }` template block (the `.project-calendar__bands`/`.project-calendar__band` markup).
4. Remove `[branchColor]="branchColors().get(row.branchRootId)"` from the `<app-objective-timeline-bar>` binding, **and** delete the `readonly branchColor = input<string | null>(null);` line (and its doc comment) from `objective-timeline-bar.component.ts` in this same task/commit — Task G1 deliberately left it in place so that task's own build stayed green; this is where it actually gets removed, input and binding together.
5. Remove the now-unused `rangeToPercent` import from `calendar-date.utils`.
6. Add a new computed that flattens every row's event links into marker positions, right after `bandRects` used to be:

```typescript
  interface MilestoneMarker { objectiveId: string; eventId: string; name: string; startDate: string; endDate: string; left: number; color: string; }

  readonly milestoneMarkers = computed<MilestoneMarker[]>(() => {
    const markers: MilestoneMarker[] = [];
    for (const row of this.rows()) {
      for (const link of row.item.events ?? []) {
        markers.push({
          objectiveId: row.item.objectiveId,
          eventId: link.eventId,
          name: link.eventName,
          startDate: link.eventStartDate,
          endDate: link.eventEndDate,
          left: this.dayIndex(link.eventEndDate) * this.dayWidth(),
          color: link.eventColor
        });
      }
    }
    return markers;
  });
```

(`MilestoneMarker` as a top-level `interface` above the component class, not nested, to match the file's existing style — `DragState`-style interfaces in this codebase sit above the class.)

7. In the per-row template block (`@for (row of rows(); ...)`), inside `.project-calendar__track`, after the `<app-objective-timeline-bar .../>` element, add:

```html
                    @for (marker of markersForRow(row.item.objectiveId); track marker.eventId) {
                      <span
                        class="project-calendar__milestone-marker"
                        data-testid="calendar-milestone-marker"
                        [style.left.px]="marker.left"
                        [style.background]="marker.color"
                        [title]="marker.name + '  ' + marker.startDate + ' → ' + marker.endDate"
                      ></span>
                    }
```

8. Add the helper method (template calls a method here, not a computed, because it's parameterized per row):
```typescript
  markersForRow(objectiveId: string): { eventId: string; name: string; startDate: string; endDate: string; left: number; color: string }[] {
    return this.milestoneMarkers().filter((m) => m.objectiveId === objectiveId);
  }
```

9. Add the CSS (next to `.project-calendar__today-line`) — note `background` is set per-marker inline above (so two milestones on the same row show their own colors, not one flat accent color); this rule only supplies shape/position:
```css
    .project-calendar__milestone-marker { position: absolute; top: 50%; z-index: 4; width: 9px; height: 9px; transform: translate(-50%, -50%) rotate(45deg); border-radius: 2px; box-shadow: 0 0 0 1.5px var(--color-surface); }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/project-calendar/
git commit -m "feat(wm-calendar): remove full-width milestone bands, add per-row diamond markers"
```

---

## Part H: Frontend — Compact tree row reuse

### Task H1: `compact` mode on `ModuleTreeRowComponent`

**Files:**
- Modify: `src/app/modules/work/ui/module-tree-row/module-tree-row.component.ts`
- Modify: `src/app/modules/work/ui/module-tree-row/module-tree-row.component.spec.ts`

**Interfaces:**
- Produces: `compact = input(false)` — when true, renders title/chevron/folder-icon only (no progress/owner/actions columns), reusing the same chevron/folder SVG markup and the same `data-testid="tree-row"`/`"chevron"`/`"title-text"` ids so existing Tree-tab assertions on the non-compact path are untouched.

- [ ] **Step 1: Write the failing test**

```typescript
it('compact mode renders only title, chevron and folder icon - no progress, owner or actions columns', () => {
  fixture.componentRef.setInput('compact', true);
  fixture.componentRef.setInput('node', baseNode);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="title-text"]')).toBeTruthy();
  expect(fixture.nativeElement.querySelector('[data-testid="progress-cell"]')).toBeNull();
  expect(fixture.nativeElement.querySelector('[data-testid="owner-cell"]')).toBeNull();
  expect(fixture.nativeElement.querySelector('[data-testid="row-actions"]')).toBeNull();
});

it('non-compact mode (default) is unchanged - still renders progress, owner and actions columns', () => {
  fixture.componentRef.setInput('node', baseNode);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="progress-cell"]')).toBeTruthy();
});

it('compact mode shows the given progressLabel text, or nothing when null', () => {
  fixture.componentRef.setInput('compact', true);
  fixture.componentRef.setInput('node', baseNode);
  fixture.componentRef.setInput('progressLabel', '40%');
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="compact-progress-label"]').textContent).toContain('40%');

  fixture.componentRef.setInput('progressLabel', null);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="compact-progress-label"]')).toBeNull();
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=module-tree-row`
Expected: FAIL — `compact` input doesn't exist, so the first test's negative assertions pass by accident but the input itself errors; confirm it fails on the missing input, not a false pass.

- [ ] **Step 3: Write minimal implementation**

Add `compact = input(false);` and `progressLabel = input<string | null>(null);` to the component class (next to `showChevron`) — `progressLabel` lets a compact-mode caller show its own progress text (e.g. "40%" or "—" for "no tasks to measure") without forcing progress through `MilestoneTreeNode`, which types `progress` as a non-nullable `number` and has no "no data" state. Wrap the existing template's outer `<div class="grid grid-cols-[1fr_112px_40px_88px_140px] ...">` row in an `@if (!compact()) { ... } @else { ... }`:

```html
    @if (!compact()) {
      <div
        class="grid grid-cols-[1fr_112px_40px_88px_140px] items-center gap-3 rounded-md px-2 py-1.5 cursor-pointer tree-row {{ selected() ? 'bg-[var(--color-accent)]' : '' }}"
        data-testid="tree-row"
        (click)="selectedRow.emit(node())"
      >
        <!-- ... existing full template body, unchanged ... -->
      </div>
    } @else {
      <div
        class="flex items-center gap-2 min-w-0 flex-1 rounded-md px-2 cursor-pointer tree-row {{ selected() ? 'bg-[var(--color-accent)]' : '' }}"
        data-testid="tree-row"
        (click)="selectedRow.emit(node())"
      >
        @if (showChevron()) {
          <button
            type="button"
            class="flex h-4 w-4 shrink-0 items-center justify-center transition-transform {{ selected() ? 'text-white' : 'text-[var(--color-text-secondary)]' }}"
            [style.transform]="expanded() ? 'rotate(90deg)' : 'rotate(0deg)'"
            data-testid="chevron"
            [attr.aria-label]="expanded() ? 'Collapse' : 'Expand'"
            (click)="onChevron($event)"
          >
            <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <polyline points="9 18 15 12 9 6" />
            </svg>
          </button>
        } @else {
          <span class="h-4 w-4 shrink-0"></span>
        }
        <svg data-testid="folder-icon" class="h-4 w-4 shrink-0 {{ selected() ? 'text-white' : 'text-[var(--color-accent)]' }}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <path d="M3 6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a4 4 0 0 1-2-2V6Z" />
        </svg>
        <span class="min-w-0 flex-1">
          <span data-testid="title-text" class="block min-w-0 truncate text-sm {{ selected() ? 'text-white' : 'text-[var(--color-text-primary)]' }}">{{ node().title }}</span>
          <span class="block text-[11px] {{ selected() ? 'text-white/80' : 'text-[var(--color-text-secondary)]' }}">{{ node().startDate }} → {{ node().endDate }}</span>
        </span>
        @if (progressLabel(); as label) {
          <span data-testid="compact-progress-label" class="shrink-0 text-xs font-medium {{ selected() ? 'text-white' : 'text-[var(--color-text-secondary)]' }}">{{ label }}</span>
        }
      </div>
    }
```

Note two deliberate differences from the non-compact branch: **no depth-based `paddingLeft` here** — in this plan's one caller (`project-calendar.component.ts`, Task H2), the caller wraps this whole component in its own indentation div (`marginLeft: row.depth * 18`, matching the existing `.project-calendar__guides` connector column width exactly), so adding another `depth() * 20` padding inside the row here would double-indent every level on top of that; and `flex-1 min-w-0` on the host so it fills whatever width its wrapper gives it and the title still truncates with an ellipsis rather than overflowing (don't add a fixed height here — the calendar's 58px row height plus `align-items: center` on the wrapping `.project-calendar__objective` cell, which Task H2 sets, is what centers this row; adding a conflicting `min-h` here would grow the row past 58px). (Leave the original full-row body exactly as-is inside the `@if (!compact())` branch — just re-indent it; don't rewrite its internals.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=module-tree-row`
Expected: PASS, and the Tree tab's own specs that render this component without setting `compact` stay green (default `false` preserves current behavior exactly).

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/module-tree-row/
git commit -m "feat(wm-calendar): add compact mode to ModuleTreeRowComponent"
```

### Task H2: Wire the compact row into the Calendar's left column

**Files:**
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.spec.ts`

**Interfaces:**
- Consumes: `ModuleTreeRowComponent` with `compact=true` (Task H1), `ProjectCalendarModuleDto.progressPercent` (Task A3/F1).

- [ ] **Step 1: Write the failing test**

```typescript
it('renders each objective label via the shared compact tree row, not bespoke markup', () => {
  fixture.detectChanges();
  const row = fixture.nativeElement.querySelector('[data-testid="tree-row"]');
  expect(row).toBeTruthy();
  expect(fixture.nativeElement.querySelector('[data-testid="folder-icon"]')).toBeTruthy();
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: FAIL — no `app-module-tree-row` in this template yet, `[data-testid="tree-row"]` doesn't exist here.

- [ ] **Step 3: Write minimal implementation**

In `project-calendar.component.ts`, add `ModuleTreeRowComponent` to the `imports` array and add these two adapter methods:

```typescript
  toTreeNode(item: ProjectCalendarModuleDto): MilestoneTreeNode {
    return {
      id: item.objectiveId,
      title: item.title,
      description: item.description ?? null,
      startDate: item.startDate,
      endDate: item.endDate,
      progress: item.progressPercent ?? 0,
      allocatedHours: item.allocatedHours ?? 0,
      completedHours: 0,
      isActive: item.isActive,
      isAchieved: item.isAchieved,
      ownerName: null,
      isOwner: false,
      children: [],
      kind: 'objective'
    };
  }

  /** Review Focus: a subtree with zero tasks must read as "-", never "0%" - progressPercent is
   *  null in exactly that case (Task A3), so this is the only place that distinction survives;
   *  toTreeNode()'s `progress` field above is a plain number and can't carry it. */
  progressLabelFor(item: ProjectCalendarModuleDto): string | null {
    return item.progressPercent == null ? '—' : `${item.progressPercent}%`;
  }
```
(import `MilestoneTreeNode` from `'../../models/milestone.model'` and `ModuleTreeRowComponent` from `'../../ui/module-tree-row/module-tree-row.component'`.)

Replace the `.project-calendar__objective` block's inner markup (lines currently building `.project-calendar__guides`/`.project-calendar__objective-body`/`.project-calendar__objective-title`/`.project-calendar__objective-dates` by hand) with:

```html
                  <div class="project-calendar__objective">
                    @if (row.depth > 0) {
                      <div class="project-calendar__guides" [style.width.px]="row.depth * 18">
                        @for (continues of row.ancestorLines; track $index) {
                          <span class="project-calendar__guide-col" [class.project-calendar__guide-col--through]="continues"></span>
                        }
                        <span class="project-calendar__guide-col project-calendar__guide-col--elbow" [class.project-calendar__guide-col--elbow-through]="!row.isLast"></span>
                      </div>
                    }
                    <div class="min-w-0 flex-1" [style.marginLeft.px]="row.depth * 18">
                      <app-module-tree-row
                        [compact]="true"
                        [node]="toTreeNode(row.item)"
                        [progressLabel]="progressLabelFor(row.item)"
                        [depth]="row.depth"
                        [showChevron]="row.hasChildren && row.depth > 0"
                        [expanded]="!isCollapsed(row.item.objectiveId)"
                        (chevronClicked)="toggleCollapsed(row.item.objectiveId, $event)"
                      />
                    </div>
                  </div>
```

**This replaces the original block's indentation, not just its title/date markup** — the original rendered `.project-calendar__guides` (absolutely-positioned connector lines, `left: 12px`, drawing *only* the lines) beside `.project-calendar__objective-body` which carried the real indent via `[style.marginLeft.px]="row.depth * 18"`. Deleting `.project-calendar__objective-body` without replacing its `marginLeft` would leave every nested row rendering at the depth-0 x-position with the connector lines drawn through the middle of the chevron/folder icon — the `<div [style.marginLeft.px]="row.depth * 18">` wrapper above is that replacement, using the same `18` the guide columns already use (not `20`, so the two line up exactly; this is also why Task H1's compact row must add no depth padding of its own — it would double the indent on top of this wrapper). Keep the `.project-calendar__guides` block exactly as it already is, unchanged. Remove the now-dead `.project-calendar__objective-title`/`.project-calendar__objective-dates`/`.project-calendar__chevron*`/`.project-calendar__folder-icon`/`.project-calendar__branch-dot` CSS rules and the `formatDateRange` method if nothing else in the file still calls it (check before deleting). Add `display: flex; align-items: center;` to the existing `.project-calendar__objective` CSS rule (it currently reads `position: relative; min-width: 0; overflow: hidden; padding: 9px 10px 8px 12px;` — keep those, just add the two new declarations) so the compact row sits vertically centered in the 58px grid row, matching where `app-objective-timeline-bar` draws its bar (`top: 20px`) in the track cell beside it — and do **not** give the compact row itself any fixed height, or it will grow the row past 58px (Task H1's note).

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: PASS. Fix any pre-existing test in this spec file that asserted on the removed `.project-calendar__objective-title`/`calendar-chevron` test ids — the chevron's test id is now `chevron` (from `ModuleTreeRowComponent`) not `calendar-chevron`; update those assertions.

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/project-calendar/
git commit -m "feat(wm-calendar): reuse the Tree tab's compact row component for calendar labels"
```

---

## Part I: Frontend — Milestone explanation panel, status derivation, legend wiring, edit-modal data-loss fix

### Task I1: `deriveMilestoneStatus` utility

**Files:**
- Create: `src/app/modules/work/utils/milestone-status.utils.ts`
- Create: `src/app/modules/work/utils/milestone-status.utils.spec.ts`

**Interfaces:**
- Produces: `deriveMilestoneStatus(event: { status: string; startDate: string; endDate: string }, today: string, hasOpenAlert: boolean) -> 'upcoming' | 'on_track' | 'at_risk' | 'completed'`.

- [ ] **Step 1: Write the failing test**

```typescript
import { deriveMilestoneStatus } from './milestone-status.utils';

describe('deriveMilestoneStatus', () => {
  const event = { status: 'active', startDate: '2026-10-01', endDate: '2026-10-31' };

  it('returns completed for an archived event regardless of dates', () => {
    expect(deriveMilestoneStatus({ ...event, status: 'archived' }, '2026-09-01', false)).toBe('completed');
  });
  it('returns upcoming when today is before the start date', () => {
    expect(deriveMilestoneStatus(event, '2026-09-15', false)).toBe('upcoming');
  });
  it('returns at_risk when overdue', () => {
    expect(deriveMilestoneStatus(event, '2026-11-01', false)).toBe('at_risk');
  });
  it('returns at_risk when there is an open monitor alert, even if not overdue', () => {
    expect(deriveMilestoneStatus(event, '2026-10-15', true)).toBe('at_risk');
  });
  it('returns on_track otherwise', () => {
    expect(deriveMilestoneStatus(event, '2026-10-15', false)).toBe('on_track');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=milestone-status.utils`
Expected: FAIL — module doesn't exist.

- [ ] **Step 3: Write minimal implementation**

```typescript
export type MilestoneStatus = 'upcoming' | 'on_track' | 'at_risk' | 'completed';

/** Design §5: no stored status exists on CalendarEvent - derive it from dates plus whether any
 *  linked objective has an open ProjectMonitorStore alert, rather than add a new stored enum. */
export function deriveMilestoneStatus(
  event: { status: string; startDate: string; endDate: string },
  today: string,
  hasOpenAlert: boolean
): MilestoneStatus {
  if (event.status === 'archived') return 'completed';
  if (today < event.startDate) return 'upcoming';
  if (hasOpenAlert || today > event.endDate) return 'at_risk';
  return 'on_track';
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=milestone-status.utils`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/utils/milestone-status.utils.ts src/app/modules/work/utils/milestone-status.utils.spec.ts
git commit -m "feat(wm-calendar): add pure milestone status derivation"
```

### Task I2: `ProjectCalendarStore` — detail/tasks/activity loaders

**Files:**
- Modify: `src/app/modules/work/state/project-calendar.store.ts`
- Modify: `src/app/modules/work/state/project-calendar.store.spec.ts`

**Interfaces:**
- Consumes: `CalendarApiService.getEvent/getEventTasks/getEventActivity` (Task F1).
- Produces: `store.loadEventDetail(id)`, `store.loadEventTasks(id)`, `store.loadEventActivity(id)`, `store.clearEventPanel()`, plus state signals `eventDetail`, `eventTasks`, `eventActivity` (all cleared together whenever a different milestone's detail starts loading, or the panel closes — see the race-condition note below).

**Race condition this task must close:** if a user opens milestone A, then quickly opens milestone B before A's `getEventTasks` response arrives, a naive `patchState` on resolution lets A's stale tasks land in `eventTasks` *after* B is already open (or, if B's own fetch fails, A's tasks stay showing under B's name indefinitely). Guard this the same way `ProjectMonitorStore.load()` already does in this codebase (`project-monitor.store.ts`): a monotonically increasing `loadVersion` counter captured before each request, and the response is only applied if the counter hasn't moved since.

- [ ] **Step 1: Write the failing test**

```typescript
it('loadEventDetail fetches, stores, and returns the full event detail including real taskIds', async () => {
  calendarApiMock.getEvent.and.returnValue(of({ id: 'e1', projectId: 'p1', name: 'R1', color: '#2563EB', status: 'active', startDate: '2026-10-01', endDate: '2026-10-31', description: 'desc', objectiveIds: ['o1'], taskIds: ['t1', 't2'], createdByName: 'Priya' }));
  const result = await store.loadEventDetail('e1');
  expect(store.eventDetail()?.taskIds).toEqual(['t1', 't2']);
  expect(result?.taskIds).toEqual(['t1', 't2']);
});

it('loadEventDetail returns null on failure, even if a previous successful fetch for the same id is still in state', async () => {
  calendarApiMock.getEvent.and.returnValue(of({ id: 'e1', projectId: 'p1', name: 'R1', color: '#2563EB', status: 'active', startDate: '2026-10-01', endDate: '2026-10-31', objectiveIds: [], taskIds: ['t1'] }));
  await store.loadEventDetail('e1'); // first, successful fetch
  calendarApiMock.getEvent.and.returnValue(throwError(() => new Error('network error')));
  const result = await store.loadEventDetail('e1'); // refresh of the same id fails
  expect(result).toBeNull();
  expect(store.eventDetail()?.id).toBe('e1'); // stale detail stays displayed - caller must check the RETURN value, not re-read state, to know this fetch failed
});

it('loadEventTasks fetches and stores the milestone task list', async () => {
  calendarApiMock.getEventTasks.and.returnValue(of([{ id: 't1', shortId: 'T-1', title: 'Build thing', statusId: 's1', marksTaskComplete: false, statusCategory: 'active', progressPercent: 40, objectiveId: 'o1', objectiveTitle: 'Module A' }]));
  await store.loadEventTasks('e1');
  expect(store.eventTasks().length).toBe(1);
});

it('loadEventActivity fetches and stores the activity log', async () => {
  calendarApiMock.getEventActivity.and.returnValue(of([{ id: 'a1', action: 'created', performedById: 'emp1', performedByName: 'Priya', performedAt: '2026-10-01T00:00:00Z', detailsJson: '{}' }]));
  await store.loadEventActivity('e1');
  expect(store.eventActivity().length).toBe(1);
});

it('opening a second milestone before the first one\'s tasks resolve discards the stale response', async () => {
  const firstCall = new Subject<CalendarEventTaskSummaryDto[]>();
  const secondCall = new Subject<CalendarEventTaskSummaryDto[]>();
  calendarApiMock.getEventTasks.and.returnValues(firstCall.asObservable(), secondCall.asObservable());

  const firstLoad = store.loadEventTasks('e1'); // starts, not yet resolved
  const secondLoad = store.loadEventTasks('e2'); // starts before e1 resolves
  secondCall.next([{ id: 't2', shortId: 'T-2', title: 'B', statusId: 's1', marksTaskComplete: false, statusCategory: 'active', progressPercent: 0, objectiveId: 'o2', objectiveTitle: 'M2' }]);
  secondCall.complete();
  await secondLoad;
  firstCall.next([{ id: 't1', shortId: 'T-1', title: 'A', statusId: 's1', marksTaskComplete: false, statusCategory: 'active', progressPercent: 0, objectiveId: 'o1', objectiveTitle: 'M1' }]); // arrives late
  firstCall.complete();
  await firstLoad;

  expect(store.eventTasks().map((t) => t.id)).toEqual(['t2']); // e1's late response never overwrote e2's
});

it('clearEventPanel resets detail, tasks and activity', () => {
  store.clearEventPanel();
  expect(store.eventDetail()).toBeNull();
  expect(store.eventTasks()).toEqual([]);
  expect(store.eventActivity()).toEqual([]);
});
```

(Match this spec file's existing mock-service setup style exactly; import `Subject, throwError` from `rxjs` alongside whatever this file already imports from there.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.store`
Expected: FAIL — methods/signals don't exist.

- [ ] **Step 3: Write minimal implementation**

In the `ProjectCalendarState` interface, add:
```typescript
  readonly eventDetail: CalendarEventDto | null;
  readonly eventTasks: readonly CalendarEventTaskSummaryDto[];
  readonly eventActivity: readonly CalendarEventActivityEntryDto[];
```
and the matching defaults in `initialState` (`eventDetail: null, eventTasks: [], eventActivity: []`). Inside `withMethods((store, calendarApi = ..., objectiveApi = ...) => { ... })`, add a module-private counter the same way `ProjectMonitorStore` does it (a plain closure variable, not state — it doesn't need to survive outside this store instance):

```typescript
    let eventDetailVersion = 0;
    let eventTasksVersion = 0;
    let eventActivityVersion = 0;
```

Then add the four methods alongside the existing `createEvent`/`updateEvent`:

```typescript
      /** Returns the freshly-fetched detail (or null on failure/supersession) so a caller that
       *  needs to act on THIS specific fetch's outcome - Task I4's openEditEvent, in particular -
       *  doesn't have to infer it from `store.eventDetail()`, which on a failed refresh of an
       *  already-open panel still holds the OLD (matching-id, but stale) detail and would wrongly
       *  look like success if read back after the fact. */
      async loadEventDetail(id: string): Promise<CalendarEventDto | null> {
        const version = ++eventDetailVersion;
        // Only clear state when switching to a DIFFERENT milestone - this method is also used to
        // silently refresh the currently-open panel (after Edit, after a save). If it unconditionally
        // cleared first, refreshing the open panel would flash it to empty and reset its active tab
        // on every single refresh, including ones the user never asked to see.
        if (store.eventDetail()?.id !== id) patchState(store, { eventDetail: null });
        try {
          const detail = await firstValueFrom(calendarApi.getEvent(id));
          if (version === eventDetailVersion) patchState(store, { eventDetail: detail });
          return version === eventDetailVersion ? detail : null;
        } catch (err) {
          if (version === eventDetailVersion) patchState(store, { error: extractError(err, 'Failed to load the milestone.') });
          return null;
        }
      },

      async loadEventTasks(id: string): Promise<void> {
        const version = ++eventTasksVersion;
        try {
          const tasks = await firstValueFrom(calendarApi.getEventTasks(id));
          if (version === eventTasksVersion) patchState(store, { eventTasks: tasks });
        } catch (err) {
          if (version === eventTasksVersion) patchState(store, { error: extractError(err, 'Failed to load the milestone tasks.') });
        }
      },

      async loadEventActivity(id: string): Promise<void> {
        const version = ++eventActivityVersion;
        try {
          const activity = await firstValueFrom(calendarApi.getEventActivity(id));
          if (version === eventActivityVersion) patchState(store, { eventActivity: activity });
        } catch (err) {
          if (version === eventActivityVersion) patchState(store, { error: extractError(err, 'Failed to load the milestone activity.') });
        }
      },

      clearEventPanel(): void {
        eventDetailVersion++; eventTasksVersion++; eventActivityVersion++; // orphan any in-flight requests
        patchState(store, { eventDetail: null, eventTasks: [], eventActivity: [] });
      }
```
Import `CalendarEventTaskSummaryDto, CalendarEventActivityEntryDto` from the DTO file.

- [ ] **Step 3b: Make `createEvent`/`updateEvent`/`closeEvent` refresh an open panel**

The panel (Task I4) stays open across a save or close, but `createEvent`/`updateEvent`/`closeEvent` today only call `load(projectId)` (refreshing the Gantt), never the panel's own `eventDetail`/`eventTasks`/`eventActivity` — so after editing a milestone's linked tasks or closing it, the open panel keeps showing the pre-edit links/activity until the user closes and reopens it. Add one line to each of the three existing methods, right after their own `await load(projectId);` (or the close method's reload) call — refresh all three, since a save can change the linked task set, not just the name/dates:
```typescript
        if (store.eventDetail()?.id === id) {
          void this.loadEventDetail(id);
          void this.loadEventTasks(id);
          void this.loadEventActivity(id);
        }
```
(`updateEvent(id, ...)` and `closeEvent(id)` already have `id` in scope; for `createEvent`, skip this line — there's no existing panel open for an event that didn't exist yet. `loadEventDetail` now only clears its slot when the id actually changes — Step 3 above — so this refresh updates the open panel in place instead of flashing it empty.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.store`
Expected: PASS

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.store`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/state/project-calendar.store.ts src/app/modules/work/state/project-calendar.store.spec.ts
git commit -m "feat(wm-calendar): add store loaders for milestone detail, tasks and activity"
```

### Task I3: `MilestoneExplanationPanelComponent`

**Files:**
- Create: `src/app/modules/work/ui/milestone-explanation-panel/milestone-explanation-panel.component.ts`
- Create: `src/app/modules/work/ui/milestone-explanation-panel/milestone-explanation-panel.component.spec.ts`

**Interfaces:**
- Consumes: `CalendarEventDto` (detail, via `event` input), `CalendarEventTaskSummaryDto[]` (via `tasks` input), `CalendarEventActivityEntryDto[]` (via `activity` input), `deriveMilestoneStatus` (Task I1).
- Produces: outputs `editRequested`, `closeRequested`, `tabActivated` (emits which tab was opened, so the host can lazy-load Tasks/Activity only once).

- [ ] **Step 1: Write the failing test**

```typescript
it('shows the Overview tab by default with status pill, dates, description and owner', () => {
  fixture.componentRef.setInput('event', baseEvent);
  fixture.componentRef.setInput('hasOpenAlert', false);
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="panel-status"]').textContent).toContain('On Track');
  expect(fixture.nativeElement.querySelector('[data-testid="panel-description"]').textContent).toContain(baseEvent.description);
  expect(fixture.nativeElement.querySelector('[data-testid="panel-owner"]').textContent).toContain(baseEvent.createdByName);
});

it('switching to the Tasks tab emits tabActivated("tasks") exactly once per activation', () => {
  fixture.componentRef.setInput('event', baseEvent);
  fixture.detectChanges();
  const emitted: string[] = [];
  component.tabActivated.subscribe((t: string) => emitted.push(t));
  fixture.nativeElement.querySelector('[data-testid="panel-tab-tasks"]').click();
  fixture.detectChanges();
  expect(emitted).toEqual(['tasks']);
});

it('renders Key Metrics tiles computed from the tasks input, using status category not just the done flag', () => {
  fixture.componentRef.setInput('event', baseEvent);
  fixture.componentRef.setInput('tasks', [
    { id: 't1', shortId: 'T-1', title: 'A', statusId: 's1', marksTaskComplete: true, statusCategory: 'done', progressPercent: 100, objectiveId: 'o1', objectiveTitle: 'M' },
    { id: 't2', shortId: 'T-2', title: 'B', statusId: 's2', marksTaskComplete: false, statusCategory: 'active', progressPercent: 40, objectiveId: 'o1', objectiveTitle: 'M' },
    { id: 't3', shortId: 'T-3', title: 'C', statusId: 's3', marksTaskComplete: false, statusCategory: 'not_started', progressPercent: 0, objectiveId: 'o1', objectiveTitle: 'M' }
  ]);
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="panel-tab-tasks"]').click();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="panel-metric-total"]').textContent).toContain('3');
  expect(fixture.nativeElement.querySelector('[data-testid="panel-metric-completed"]').textContent).toContain('1');
  // "In Progress" means status category 'active' specifically - the not_started task must not be counted here.
  expect(fixture.nativeElement.querySelector('[data-testid="panel-metric-in-progress"]').textContent).toContain('1');
});

it('resolves Modules tab rows to titles via the modules input, not raw objective ids', () => {
  fixture.componentRef.setInput('event', { ...baseEvent, objectiveIds: ['o1'] });
  fixture.componentRef.setInput('modules', [{ objectiveId: 'o1', title: 'Employee Management' }]);
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="panel-tab-modules"]').click();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="panel-module-row"]').textContent).toContain('Employee Management');
  expect(fixture.nativeElement.querySelector('[data-testid="panel-module-row"]').textContent).not.toContain('o1');
});

it('shows an empty state on the Tasks tab when there are no linked tasks', () => {
  fixture.componentRef.setInput('event', baseEvent);
  fixture.componentRef.setInput('tasks', []);
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="panel-tab-tasks"]').click();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="panel-tasks-empty"]')).toBeTruthy();
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=milestone-explanation-panel`
Expected: FAIL — component doesn't exist.

- [ ] **Step 3: Write minimal implementation**

```typescript
import { Component, computed, input, output, signal } from '@angular/core';
import { CardComponent } from '../../../../shared/ui/card/card.component';
import { CalendarEventDto, CalendarEventTaskSummaryDto, CalendarEventActivityEntryDto } from '../../models/dto/calendar.dto';
import { deriveMilestoneStatus, MilestoneStatus } from '../../utils/milestone-status.utils';

type PanelTab = 'overview' | 'modules' | 'tasks' | 'activity';

const STATUS_LABEL: Record<MilestoneStatus, string> = {
  upcoming: 'Upcoming', on_track: 'On Track', at_risk: 'At Risk', completed: 'Completed'
};

@Component({
  selector: 'app-milestone-explanation-panel',
  standalone: true,
  imports: [CardComponent],
  template: `
    <app-card>
      <div data-testid="milestone-explanation-panel" class="flex flex-col gap-5 p-4">
        <div class="flex items-start justify-between gap-3 border-b border-[var(--color-border)] pb-4">
          <div class="min-w-0">
            <div class="mb-2 flex items-center gap-2">
              <span class="h-2 w-2 shrink-0 rounded-full" [style.background]="event().color" aria-hidden="true"></span>
              <span class="text-[11px] font-bold uppercase tracking-wider text-[var(--color-text-secondary)]">Milestone</span>
            </div>
            <h2 class="truncate text-base font-bold leading-tight text-[var(--color-text-primary)]">{{ event().name }}</h2>
            <span data-testid="panel-status" class="mt-2 inline-flex rounded-full bg-[var(--color-surface-secondary)] px-2.5 py-1 text-xs font-medium text-[var(--color-text-secondary)]">{{ statusLabel() }}</span>
          </div>
          <button type="button" class="text-[var(--color-text-secondary)]" aria-label="Close panel" (click)="dismissed.emit()">×</button>
        </div>

        <div class="flex gap-1 border-b border-[var(--color-border)]" role="tablist">
          @for (tab of tabs; track tab) {
            <button
              type="button" role="tab" [attr.data-testid]="'panel-tab-' + tab"
              [attr.aria-selected]="activeTab() === tab"
              class="px-3 py-2 text-xs font-medium capitalize {{ activeTab() === tab ? 'border-b-2 border-[var(--color-accent)] text-[var(--color-text-primary)]' : 'text-[var(--color-text-secondary)]' }}"
              (click)="activateTab(tab)"
            >{{ tab }}</button>
          }
        </div>

        @if (activeTab() === 'overview') {
          @if (event().description; as description) {
            <p data-testid="panel-description" class="text-xs leading-relaxed text-[var(--color-text-secondary)]">{{ description }}</p>
          }
          <div class="flex flex-col rounded-xl border border-[var(--color-border)] bg-[var(--color-surface-secondary)]/30 px-3 py-1 text-xs">
            <div class="flex items-center justify-between py-2 border-b border-[var(--color-border)]/50">
              <span class="text-[var(--color-text-secondary)] font-medium">Owner</span>
              <span data-testid="panel-owner">{{ event().createdByName ?? '—' }}</span>
            </div>
            <div class="flex items-center justify-between py-2">
              <span class="text-[var(--color-text-secondary)] font-medium">Target Date</span>
              <span class="font-semibold text-[var(--color-text-primary)]">{{ event().endDate }}</span>
            </div>
          </div>
        }

        @if (activeTab() === 'modules') {
          <ul class="flex flex-col gap-1.5 text-xs">
            @for (objectiveId of event().objectiveIds; track objectiveId) {
              <li data-testid="panel-module-row" class="rounded-md border border-[var(--color-border)] px-2.5 py-1.5">{{ moduleTitleFor(objectiveId) }}</li>
            } @empty {
              <li data-testid="panel-modules-empty" class="text-[var(--color-text-secondary)]">No modules linked.</li>
            }
          </ul>
        }

        @if (activeTab() === 'tasks') {
          <div class="grid grid-cols-3 gap-2">
            <div class="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-center shadow-sm">
              <div data-testid="panel-metric-total" class="text-base font-bold text-[var(--color-text-primary)]">{{ tasks().length }}</div>
              <div class="text-[10px] font-medium text-[var(--color-text-secondary)] mt-0.5">Total Tasks</div>
            </div>
            <div class="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-center shadow-sm">
              <div data-testid="panel-metric-completed" class="text-base font-bold text-[var(--color-text-primary)]">{{ completedTaskCount() }}</div>
              <div class="text-[10px] font-medium text-[var(--color-text-secondary)] mt-0.5">Completed</div>
            </div>
            <div class="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] p-3 text-center shadow-sm">
              <div data-testid="panel-metric-in-progress" class="text-base font-bold text-[var(--color-text-primary)]">{{ inProgressTaskCount() }}</div>
              <div class="text-[10px] font-medium text-[var(--color-text-secondary)] mt-0.5">In Progress</div>
            </div>
          </div>
          <ul class="flex flex-col gap-1.5 text-xs">
            @for (task of tasks(); track task.id) {
              <li data-testid="panel-task-row" class="flex items-center justify-between rounded-md border border-[var(--color-border)] px-2.5 py-1.5">
                <span>{{ task.title }}</span>
                <span class="text-[var(--color-text-secondary)]">{{ task.objectiveTitle }}</span>
              </li>
            } @empty {
              <li data-testid="panel-tasks-empty" class="text-[var(--color-text-secondary)]">No tasks linked to this milestone.</li>
            }
          </ul>
        }

        @if (activeTab() === 'activity') {
          <ul class="flex flex-col gap-2 text-xs">
            @for (entry of activity(); track entry.id) {
              <li data-testid="panel-activity-row" class="rounded-md border border-[var(--color-border)] px-2.5 py-1.5">
                <span class="font-medium">{{ entry.performedByName ?? 'Someone' }}</span> {{ entry.action }} this milestone
                <span class="block text-[10px] text-[var(--color-text-secondary)]">{{ entry.performedAt }}</span>
              </li>
            } @empty {
              <li data-testid="panel-activity-empty" class="text-[var(--color-text-secondary)]">No activity yet.</li>
            }
          </ul>
        }

        <div class="border-t border-[var(--color-border)] pt-4">
          <div class="grid grid-cols-2 gap-2">
            <button type="button" class="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2.5 text-center text-sm font-medium text-[var(--color-text-secondary)]" data-testid="panel-edit" (click)="editRequested.emit(event().id)">Edit milestone</button>
            <button type="button" class="rounded-lg bg-[var(--color-accent)] px-3 py-2.5 text-center text-sm font-medium text-white" data-testid="panel-close" (click)="closeRequested.emit(event().id)">Close milestone</button>
          </div>
        </div>
      </div>
    </app-card>
  `
})
export class MilestoneExplanationPanelComponent {
  readonly event = input.required<CalendarEventDto>();
  /** Only objectiveId/title are read here - pass the page's existing store.modules() straight
   *  through, no need for a narrower type. */
  readonly modules = input<readonly { objectiveId: string; title: string }[]>([]);
  readonly tasks = input<readonly CalendarEventTaskSummaryDto[]>([]);
  readonly activity = input<readonly CalendarEventActivityEntryDto[]>([]);
  readonly hasOpenAlert = input(false);
  readonly today = input(new Date().toISOString().slice(0, 10));

  readonly editRequested = output<string>();
  readonly closeRequested = output<string>();
  readonly dismissed = output<void>();
  /** Emits the tab name the first time it's activated, so the host can lazy-load its data. */
  readonly tabActivated = output<PanelTab>();

  readonly tabs: PanelTab[] = ['overview', 'modules', 'tasks', 'activity'];
  readonly activeTab = signal<PanelTab>('overview');
  private readonly activatedTabs = new Set<PanelTab>(['overview']);

  readonly statusLabel = computed(() => STATUS_LABEL[deriveMilestoneStatus(this.event(), this.today(), this.hasOpenAlert())]);
  readonly completedTaskCount = computed(() => this.tasks().filter((t) => t.statusCategory === 'done').length);
  readonly inProgressTaskCount = computed(() => this.tasks().filter((t) => t.statusCategory === 'active').length);

  moduleTitleFor(objectiveId: string): string {
    return this.modules().find((m) => m.objectiveId === objectiveId)?.title ?? objectiveId;
  }

  activateTab(tab: PanelTab): void {
    this.activeTab.set(tab);
    if (!this.activatedTabs.has(tab)) {
      this.activatedTabs.add(tab);
      this.tabActivated.emit(tab);
    }
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=milestone-explanation-panel`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/milestone-explanation-panel/
git commit -m "feat(wm-calendar): add milestone explanation panel with lazy tabs"
```

### Task I4: Wire the panel into the page; fix the Edit data-loss bug

**Files:**
- Modify: `src/app/modules/work/ui/calendar-event-legend/calendar-event-legend.component.ts`
- Modify: `src/app/modules/work/ui/calendar-event-legend/calendar-event-legend.component.spec.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.spec.ts`

**Interfaces:**
- Consumes: `MilestoneExplanationPanelComponent` (Task I3), `store.loadEventDetail/loadEventTasks/loadEventActivity` (Task I2).
- Adds: `CalendarEventLegendComponent.select = output<string>()`, emitted when the row body (not the edit/close icon buttons) is clicked.

- [ ] **Step 1: Write the failing test**

In `calendar-event-legend.component.spec.ts`:
```typescript
it('emits select with the event id when a row body is clicked', () => {
  const emitted: string[] = [];
  component.select.subscribe((id: string) => emitted.push(id));
  fixture.nativeElement.querySelector('.calendar-event').click();
  expect(emitted).toEqual([baseEvents[0].eventId]);
});

it('clicking the edit button does not also emit select', () => {
  const emitted: string[] = [];
  component.select.subscribe((id: string) => emitted.push(id));
  fixture.nativeElement.querySelector('.calendar-event__action').click();
  expect(emitted).toEqual([]);
});
```

In `project-calendar.component.spec.ts`:
```typescript
it('clicking a milestone opens the explanation panel and fetches its detail and tasks', async () => {
  // ... store.loadEventDetail/loadEventTasks spies ...
  fixture.detectChanges();
  fixture.nativeElement.querySelector('.calendar-event').click();
  await fixture.whenStable();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="milestone-explanation-panel"]')).toBeTruthy();
});

it('Edit fetches the real event detail (with its actual taskIds) before opening the modal, instead of reusing the zero-taskIds band stub', async () => {
  // ... seed store.loadEventDetail to resolve a detail with taskIds: ['t1','t2'] ...
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="panel-edit"]')?.click()
    ?? fixture.nativeElement.querySelector('.calendar-event__action').click();
  await fixture.whenStable();
  fixture.detectChanges();
  // the modal's event input should now carry the full detail, not store.events()'s stub
  expect(component.editingEvent()?.taskIds).toEqual(['t1', 't2']);
  expect(component.modalOpen()).toBe(true);
});

it('Edit does NOT open the modal if the detail fetch fails - a blank modal would silently become Create, wiping all existing links on save', async () => {
  // ... seed store.loadEventDetail to leave store.eventDetail() null / set store.error() ...
  fixture.detectChanges();
  fixture.nativeElement.querySelector('.calendar-event__action').click();
  await fixture.whenStable();
  fixture.detectChanges();
  expect(component.modalOpen()).toBe(false);
});

it('derives at-risk only from alerts that are not already muted for an achieved module', () => {
  // ... store.eventDetail() with objectiveIds: ['o1'], monitor.alertsFor('o1') returning one alert with inAchievedModule: true ...
  expect(component.hasOpenAlertForEvent('e1')).toBe(false);
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern="calendar-event-legend|project-calendar.component"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

In `calendar-event-legend.component.ts`, add `readonly select = output<string>();` and on the `<article class="calendar-event">` element add `(click)="select.emit(event.eventId)"`. On the existing edit/close `<button>` elements (inside `.calendar-event__actions`), add `(click)="$event.stopPropagation(); edit.emit(event.eventId)"` / `(click)="$event.stopPropagation(); close.emit(event.eventId)"` — i.e. keep each button's existing emit, just add `$event.stopPropagation()` in front of it so the new row-level `select` doesn't also fire.

In `project-calendar.component.ts`:
1. Add `MilestoneExplanationPanelComponent` to `imports`.
2. Add state: `readonly panelEventId = signal<string | null>(null);` and `readonly openAlertObjectiveIds = computed(() => new Set(/* if ProjectMonitorStore is already injected elsewhere on this page; otherwise inject it here */));` — if `ProjectMonitorStore` isn't already loaded on this page, inject it (`private readonly monitor = inject(ProjectMonitorStore);`) and call `this.monitor.load(id)` alongside the existing `this.store.load(id)` in `ngOnInit`.
3. Add methods:
```typescript
  openMilestonePanel(eventId: string): void {
    this.panelEventId.set(eventId);
    void this.store.loadEventDetail(eventId);
  }

  closeMilestonePanel(): void {
    this.panelEventId.set(null);
    this.store.clearEventPanel();
  }

  onPanelTabActivated(eventId: string, tab: 'overview' | 'modules' | 'tasks' | 'activity'): void {
    if (tab === 'tasks') void this.store.loadEventTasks(eventId);
    if (tab === 'activity') void this.store.loadEventActivity(eventId);
  }

  /** Design §5's "at risk" input: an open alert on a linked module, excluding the ones the Tree
   *  tab itself treats as informational-only (faded, never counted) - an achieved module's alert
   *  shouldn't flag its milestone as at-risk either. */
  hasOpenAlertForEvent(eventId: string): boolean {
    const detail = this.store.eventDetail();
    if (!detail || detail.id !== eventId) return false;
    return detail.objectiveIds.some((id) => this.monitor.alertsFor(id).some((a) => !a.inAchievedModule));
  }

  async openEditEvent(eventId: string): Promise<void> {
    // Real taskIds - see Design §7, fixes the legend-stub data loss. Act on the RETURN value, not
    // store.eventDetail() afterward: if this panel was already open on eventId and the refresh
    // fails, state still holds the old (matching-id) detail, which would look like success if read
    // back from state - only the return value distinguishes "fetch failed" from "fetch succeeded".
    const detail = await this.store.loadEventDetail(eventId);
    if (!detail) return; // fetch failed or was superseded - do NOT open a blank modal, it would save as a brand-new event and silently drop every existing link
    this.editingEvent.set(detail);
    this.modalOpen.set(true);
  }
```
4. Change the existing `openEditEvent(eventId: string)` method's old body (`const event = this.store.events().find(...)`) — replace it entirely with the async version above (same method name, now `async`). Update its one call site, `(edit)="openEditEvent($event)"` on `<app-calendar-event-legend>`, to still work the same (Angular handles an async event handler fine, no template change needed).
5. On `<app-calendar-event-legend ... />`, add `(select)="openMilestonePanel($event)"`. Also update the component's existing `closeEvent(id: string)` method (already present, currently just `await this.store.closeEvent(id);`) to close the panel when the event being closed is the one currently open — append `if (this.panelEventId() === id) this.closeMilestonePanel();` after the existing `await this.store.closeEvent(id);` line. A closed (archived) milestone drops off the active legend entirely (`store.activeEvents()` only returns active bands) and its Edit action would 400 against an archived event, so the panel must not linger open on it.
6. Change the existing `.project-calendar__layout` grid from two columns to a layout that can hold a third, wider column while the panel is open — change its CSS rule from `grid-template-columns: minmax(0, 1fr) 220px;` to:
```css
    .project-calendar__layout { display: grid; min-width: 0; grid-template-columns: minmax(0, 1fr) 220px; align-items: start; gap: 14px; }
    .project-calendar__layout--panel-open { grid-template-columns: minmax(0, 1fr) 340px; }
```
and bind the modifier class on the existing `.project-calendar__layout` element: `[class.project-calendar__layout--panel-open]="panelEventId()"`. Place the panel in the right-hand column, **above** the legend (so Overview/Modules/Tasks/Activity is the first thing seen on selecting a milestone, with the full milestones list still reachable by scrolling down in the same column) — inside that right-hand column `<div>`, before `<app-calendar-event-legend .../>`, add:
```html
          @if (panelEventId(); as eventId) {
            @if (store.eventDetail(); as detail) {
              @if (detail.id === eventId) {
                <app-milestone-explanation-panel
                  [event]="detail"
                  [modules]="store.modules()"
                  [tasks]="store.eventTasks()"
                  [activity]="store.eventActivity()"
                  [hasOpenAlert]="hasOpenAlertForEvent(eventId)"
                  (tabActivated)="onPanelTabActivated(eventId, $event)"
                  (editRequested)="openEditEvent($event)"
                  (closeRequested)="closeEvent($event)"
                  (dismissed)="closeMilestonePanel()"
                />
              }
            }
          }
```
(`store.modules()` already has `objectiveId`/`title` fields — it satisfies the panel's `modules` input shape structurally, no mapping needed.) If `.project-calendar__layout`'s single `<div>` currently wraps both the timeline-scroll and the legend as direct siblings, wrap the legend (and now the panel) in their own `<div>` so the CSS selector above has a single grid cell to target — check the current markup before assuming it's already two top-level children.

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern="calendar-event-legend|project-calendar.component"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/calendar-event-legend/ src/app/modules/work/feature/project-calendar/
git commit -m "fix(wm-calendar): wire milestone explanation panel; fetch real taskIds before Edit to stop silent task-link loss"
```

---

## Part J: Frontend — List/Timeline toggle and Month navigation

Per Design §9, the user explicitly asked for this to be included. Both pieces are additive to the existing Gantt: nothing about zoom/fit/scroll changes, and no new backend data is needed — List mode reads the same `store.modules()`/`store.activeEvents()` the Gantt already has.

### Task J1: Month label and prev/next scroll

**Files:**
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.spec.ts`

**Interfaces:**
- Produces: `currentMonthLabel: Signal<string>` (the month at the left edge of the visible scroll viewport), `scrollMonth(direction: 1 | -1): void`.

**This task's toolbar markup renders where the existing zoom/fit controls already render, not necessarily inside `fixture.nativeElement`** — `#toolbarTpl`'s content is handed to `WorkPageToolbarStore.setTemplate()` (see `ngAfterViewInit`) rather than shown directly in this component's own template tree. Before writing a single test, find and read this spec file's existing test for `data-testid="fit-to-screen"` or `data-testid="zoom-controls"` (both already exist in the component and must already be tested somehow) and copy its exact DOM-access technique verbatim for the new tests below — don't assume `fixture.nativeElement.querySelector(...)` reaches toolbar content until that existing test proves it does for this file.

- [ ] **Step 1: Write the failing test**

```typescript
it('shows the month at the current scroll position in the toolbar', () => {
  fixture.detectChanges();
  // Use this spec file's existing toolbar-access pattern (see note above) in place of a bare
  // fixture.nativeElement query if the fit-to-screen test reaches the toolbar differently.
  expect(fixture.nativeElement.querySelector('[data-testid="calendar-month-label"]').textContent.trim().length).toBeGreaterThan(0);
});

it('scrollMonth(1) scrolls the timeline container forward by about 30 days of width', () => {
  component.zoomDayWidth.set(20);
  fixture.detectChanges();
  const scrollSpy = spyOn(component['scrollContainer']()!.nativeElement, 'scrollBy');
  component.scrollMonth(1);
  expect(scrollSpy).toHaveBeenCalledWith(jasmine.objectContaining({ left: 30 * component.dayWidth() }));
});

it('scrollMonth(-1) scrolls the timeline container backward', () => {
  component.zoomDayWidth.set(20);
  fixture.detectChanges();
  const scrollSpy = spyOn(component['scrollContainer']()!.nativeElement, 'scrollBy');
  component.scrollMonth(-1);
  expect(scrollSpy).toHaveBeenCalledWith(jasmine.objectContaining({ left: -30 * component.dayWidth() }));
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless` (see the Known gotchas note on single-spec filtering)
Expected: FAIL — no such test id/method yet.

- [ ] **Step 3: Write minimal implementation**

Add a signal tracking the scroll container's current `scrollLeft`, updated on scroll, and derive the label from it:

```typescript
  readonly scrollLeft = signal(0);

  /** The label column (LABEL_WIDTH px) scrolls together with the date columns inside the same
   *  scroll container, so the day actually at the left edge of the *date area* is offset by
   *  LABEL_WIDTH, not by raw scrollLeft - subtracting it here is what keeps the label in sync with
   *  what's actually visible instead of running LABEL_WIDTH-worth of days ahead. */
  readonly currentMonthLabel = computed(() => {
    const dayIndexAtLeftEdge = Math.floor(Math.max(0, this.scrollLeft() - this.LABEL_WIDTH) / this.dayWidth());
    const day = addCalendarDays(this.timelineStart(), dayIndexAtLeftEdge);
    return parseCalendarDate(day).toLocaleDateString('en-US', { month: 'long', year: 'numeric', timeZone: 'UTC' });
  });

  onTimelineScroll(event: Event): void {
    this.scrollLeft.set((event.target as HTMLElement).scrollLeft);
  }

  scrollMonth(direction: 1 | -1): void {
    this.scrollContainer()?.nativeElement.scrollBy({ left: direction * 30 * this.dayWidth(), behavior: 'smooth' });
  }
```

Add `(scroll)="onTimelineScroll($event)"` to the existing `<div class="project-calendar__timeline-scroll" #scrollContainer>` element. In the `#toolbarTpl` template, before the existing `.project-calendar__zoom` div, add:
```html
        <div class="project-calendar__month-nav" data-testid="calendar-month-nav">
          <button type="button" class="project-calendar__month-step" aria-label="Previous month" (click)="scrollMonth(-1)">‹</button>
          <span data-testid="calendar-month-label">{{ currentMonthLabel() }}</span>
          <button type="button" class="project-calendar__month-step" aria-label="Next month" (click)="scrollMonth(1)">›</button>
        </div>
```
Add minimal matching CSS next to `.project-calendar__zoom`:
```css
    .project-calendar__month-nav { display: flex; align-items: center; gap: 6px; height: 30px; padding: 0 4px; font-size: 12px; font-weight: 600; color: var(--color-text-primary); }
    .project-calendar__month-step { display: flex; height: 22px; width: 22px; align-items: center; justify-content: center; border: 0; border-radius: 6px; background: transparent; color: var(--color-text-secondary); cursor: pointer; }
    .project-calendar__month-step:hover { background: var(--color-surface-secondary); color: var(--color-text-primary); }
```
(`addCalendarDays`/`parseCalendarDate` are already imported from `calendar-date.utils` in this file.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/project-calendar/
git commit -m "feat(wm-calendar): add month label and prev/next scroll navigation"
```

### Task J2: List/Timeline toggle backed by `app-data-table`

**Files:**
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.ts`
- Modify: `src/app/modules/work/feature/project-calendar/project-calendar.component.spec.ts`

**Interfaces:**
- Consumes: `app-data-table` (`src/app/shared/ui/data-table/data-table.component.ts` — **read its full public API, inputs/outputs and the `appDataTableCell`/`appDataTableRowAfter` directive usage, before wiring it**; the sketch below shows the intended columns and data, not a verified exact template against that component's current inputs).
- Produces: `viewMode: Signal<'timeline' | 'list'>`, `milestoneListRows: Signal<{eventId, name, color, status, endDate, moduleCount}[]>`.

Same toolbar caveat as Task J1: the view-toggle buttons render through `#toolbarTpl`/`WorkPageToolbarStore`, not necessarily reachable via a bare `fixture.nativeElement.querySelector` — copy whatever DOM-access pattern Task J1 ended up using for its own toolbar test.

- [ ] **Step 1: Write the failing test**

```typescript
it('defaults to timeline view and shows the Gantt grid', () => {
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="project-calendar-timeline"]').hidden).toBe(false);
  expect(fixture.nativeElement.querySelector('[data-testid="project-calendar-list"]')).toBeFalsy();
});

it('switching to List mode shows a table row per active milestone with its linked module count, counting collapsed modules too', () => {
  // ... seed store.activeEvents() with one band; store.modules() with THREE modules whose events[]
  // reference its eventId, where at least one of the three is a descendant collapsed out of
  // rows() via collapsedIds - the count below must still be 3, not however many rows() exposes.
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="view-toggle-list"]').click();
  fixture.detectChanges();
  expect(fixture.nativeElement.querySelector('[data-testid="project-calendar-timeline"]').hidden).toBe(true);
  const row = fixture.nativeElement.querySelector('[data-testid="milestone-list-row"]');
  expect(row.textContent).toContain('3');
});

it('clicking a List row opens the milestone panel, same as clicking its legend entry', () => {
  // ... as above ...
  fixture.nativeElement.querySelector('[data-testid="view-toggle-list"]').click();
  fixture.detectChanges();
  fixture.nativeElement.querySelector('[data-testid="milestone-list-row"]').click();
  expect(component.panelEventId()).toBe(/* the seeded eventId */ jasmine.any(String));
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Add state and a derived row list. **Count linked modules from `store.modules()`, not from `rows()`** — `rows()` (built by `buildCalendarTreeOrder`) omits any objective collapsed behind a closed parent, so a List-mode count built from it would shrink or grow as the user expands/collapses the unrelated Gantt tree instead of staying a stable fact about the milestone. **Derive at-risk status per event from the modules it's actually linked to**, not from whichever single event's detail happens to be loaded in the panel (`hasOpenAlertForEvent` only works for that one open panel) — every row in this list needs its own alert check:

```typescript
  readonly viewMode = signal<'timeline' | 'list'>('timeline');

  private eventLinksFromAllModules(): Map<string, string[]> {
    const objectiveIdsByEvent = new Map<string, string[]>();
    for (const module of this.store.modules()) {
      for (const link of module.events ?? []) {
        const list = objectiveIdsByEvent.get(link.eventId);
        if (list) list.push(module.objectiveId); else objectiveIdsByEvent.set(link.eventId, [module.objectiveId]);
      }
    }
    return objectiveIdsByEvent;
  }

  readonly milestoneListRows = computed(() => {
    const objectiveIdsByEvent = this.eventLinksFromAllModules();
    return this.store.activeEvents().map((band) => {
      const linkedObjectiveIds = objectiveIdsByEvent.get(band.eventId) ?? [];
      const hasOpenAlert = linkedObjectiveIds.some((id) => this.monitor.alertsFor(id).some((a) => !a.inAchievedModule));
      return {
        eventId: band.eventId,
        name: band.name,
        color: band.color,
        status: deriveMilestoneStatus({ status: 'active', startDate: band.startDate, endDate: band.endDate }, this.today, hasOpenAlert),
        endDate: band.endDate,
        moduleCount: linkedObjectiveIds.length
      };
    });
  });
```
(import `deriveMilestoneStatus` from `'../../utils/milestone-status.utils'`. `hasOpenAlertForEvent`, defined in Task I4, stays as the panel's own single-event convenience method — this is a separate, list-wide computation, not a replacement for it.)

**Keep the timeline mounted and hide it instead of destroying it with `@if`/`@else`.** `ngAfterViewInit` attaches the `ResizeObserver` to `#scrollContainer` exactly once; if List mode's `@else` branch removes that element from the DOM, switching back to Timeline re-creates a *new* `#scrollContainer` the observer was never attached to, so `containerWidth`/`fitDayWidth`/Fit-to-screen all go stale from that point on. Change `[style.display]`/structural approach: keep the existing `<div class="project-calendar__layout">...</div>` block rendering unconditionally (still inside the existing `@else` branch for loading/empty, unchanged), add `data-testid="project-calendar-timeline"` and `[hidden]="viewMode() !== 'timeline'"` directly on it, and add the List markup as a **sibling** `@if (viewMode() === 'list')` block after it (not an `@else` of it) — `ResizeObserver`'s existing `if (width) this.containerWidth.set(width)` guard (`project-calendar.component.ts`, `ngAfterViewInit`) already no-ops harmlessly while a `[hidden]` element reports width 0, so no change is needed there:

```html
          <div class="project-calendar__layout" data-testid="project-calendar-timeline" [hidden]="viewMode() !== 'timeline'">
            <!-- ... existing timeline-scroll + legend/panel markup, completely unchanged ... -->
          </div>
          @if (viewMode() === 'list') {
            <div data-testid="project-calendar-list">
              <app-data-table
                [columns]="milestoneListColumns"
                [rows]="milestoneListRows()"
                (rowClicked)="openMilestonePanel($event.eventId)"
              >
                <ng-template appDataTableCell="name" let-row>
                  <span data-testid="milestone-list-row" class="flex items-center gap-2">
                    <span class="h-2 w-2 rounded-full" [style.background]="row.color"></span>{{ row.name }}
                  </span>
                </ng-template>
                <ng-template appDataTableCell="status" let-row>{{ row.status }}</ng-template>
                <ng-template appDataTableCell="endDate" let-row>{{ row.endDate }}</ng-template>
                <ng-template appDataTableCell="moduleCount" let-row>{{ row.moduleCount }}</ng-template>
              </app-data-table>
            </div>
          }
```
Add the column config as a class field:
```typescript
  readonly milestoneListColumns: DataTableColumn[] = [
    { key: 'name', label: 'Milestone', width: '40%' },
    { key: 'status', label: 'Status' },
    { key: 'endDate', label: 'Target Date' },
    { key: 'moduleCount', label: 'Modules' }
  ];
```
Add the toggle buttons to `#toolbarTpl`, next to the month-nav from Task J1:
```html
        <div class="project-calendar__view-toggle" role="tablist" aria-label="View mode">
          <button type="button" role="tab" data-testid="view-toggle-timeline" [attr.aria-selected]="viewMode() === 'timeline'" (click)="viewMode.set('timeline')">Timeline</button>
          <button type="button" role="tab" data-testid="view-toggle-list" [attr.aria-selected]="viewMode() === 'list'" (click)="viewMode.set('list')">List</button>
        </div>
```

> **Before committing:** confirm `app-data-table`'s actual output name for a row click (it may not be `rowClicked` — check the component file) and its exact `columns`/`rows` input names and types (`DataTableColumn` as read earlier only had `key`/`label`/`width`/`sortable`/`interactive`/`align`/`hideLabel` — there was no row-click output visible in the portion of the file read while planning this; search the rest of that file for how Backlog or Approvals wires row selection and copy that exact mechanism instead of guessing `rowClicked`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-calendar.component`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/project-calendar/
git commit -m "feat(wm-calendar): add List view toggle backed by the shared data table"
```

---

## Part K: Frontend — Tab label rename

### Task K1: Rename the project-detail "Calendar" tab to "Milestones"

**Files:**
- Modify: `src/app/modules/work/feature/project-detail/project-detail.component.html`
- Modify: `src/app/modules/work/feature/project-detail/project-detail.component.spec.ts`

**Interfaces:** none (text-only change).

- [ ] **Step 1: Write the failing test**

Grep this component's spec file and `project-detail.component.html` for the current tab text first (`grep -n "Calendar" src/app/modules/work/feature/project-detail/project-detail.component.html src/app/modules/work/feature/project-detail/project-detail.component.spec.ts`) — confirm which literal string renders the tab label (likely a `routerLink`'s sibling text node, matching the `Tree`/`Board`/`Backlog`/`Approvals`/`Settings` siblings already in that file). Then add/adjust a test:

```typescript
it('shows "Milestones" as the Calendar tab label', () => {
  fixture.detectChanges();
  const tabs: string[] = Array.from(fixture.nativeElement.querySelectorAll('nav a, nav button')).map((el: any) => el.textContent.trim());
  expect(tabs).toContain('Milestones');
  expect(tabs).not.toContain('Calendar');
});
```

(Adjust the selector to match however this file's existing tab-link tests already query the tab bar — copy that pattern rather than guessing a new one.)

- [ ] **Step 2: Run test to verify it fails**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-detail.component`
Expected: FAIL — label still says "Calendar".

- [ ] **Step 3: Write minimal implementation**

In `project-detail.component.html`, change only the visible text of the Calendar tab's link (keep its `routerLink`, icon and any `data-testid` exactly as-is — only the text node changes) from `Calendar` to `Milestones`. Do **not** touch `nav-items.config.ts` (that's the sidebar's unrelated personal-Calendar entry, per Design §1) — grep it to confirm its "Calendar" string is untouched by this diff.

- [ ] **Step 4: Run test to verify it passes**

Run: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=project-detail.component`
Expected: PASS. Also run the full work-module spec sweep once to catch any other spec asserting the literal text "Calendar" for this tab: `npx ng test --watch=false --browsers=ChromeHeadless --test-path-pattern=work`

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/project-detail/
git commit -m "chore(wm-calendar): relabel the project-detail Calendar tab to Milestones"
```

---

## Task L: Full frontend gate + manual browser pass

**Files:** none (verification only)

- [ ] **Step 1:** `npx ng build` — expect 0 errors.
- [ ] **Step 2:** `npx ng test --watch=false --browsers=ChromeHeadless` — expect full green; re-run any single flaky spec alone before treating it as a real failure (this suite has pre-existing flakes under load, per project history, unrelated to this change).
- [ ] **Step 3:** Record before/after test counts for the handoff report.
- [ ] **Step 4 (manual, by the user in a browser):**
  1. Apply the new backend migration (`AddCalendarEventDescriptionAndActivityLog`) to a local DB, then run the API.
  2. Open a project's Milestones tab. Confirm module bars are gray by default, and a module with zero tasks shows "—" for progress (not "0%") next to its title.
  3. Create a milestone, typing something into its new Description field, covering 4 of a 10-task module (whole link a different small module, and hand-pick 4 tasks from the big one). Confirm the big module's bar fills ~40% in the milestone's color, the small module's bar fills 100%.
  4. Click the milestone's diamond/legend row — confirm the explanation panel opens with Overview (status pill, the description you just typed, owner, target date), and that Modules/Tasks/Activity tabs load real data when clicked, with the Modules tab showing module titles (not raw ids).
  5. Click Edit from the panel or legend; confirm the modal shows the actual previously-picked tasks still checked (not unchecked) and the description you typed is pre-filled — this is the data-loss bug fix from Task I4. Save without changing anything; re-open Edit and confirm the same tasks are still checked.
  6. Switch the toolbar to List view; confirm it shows one row per active milestone with its linked-module count, and clicking a row opens the same explanation panel. Switch back to Timeline and confirm zoom/Fit still work (the Gantt shouldn't have lost its sizing).
  7. Use the Month prev/next controls; confirm the month label updates and the timeline scrolls roughly a month's width each click.
  8. Confirm the tab label reads "Milestones" and the sidebar's separate personal Calendar nav entry is unaffected.
