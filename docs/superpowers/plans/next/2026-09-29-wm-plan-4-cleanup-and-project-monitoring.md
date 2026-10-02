# WM Plan 4: Cleanups + Project Monitoring Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the Plan-4 cleanup backlog (tree-walk dedupe, dead code, Approvals tabs, allocation current-hours
hint, task-edit stale fix) and add a Project Monitoring Engine: capacity/deadline predictions shown as
non-blocking warnings, plus an hourly monitor that stores alerts, marks them red on the Tree, and notifies the
creator-position holder.

**Architecture:** All monitor rules are pure static functions over an in-memory project snapshot
(`ProjectMonitorSnapshot`). The same rules feed two consumers: the preview endpoints (live form warnings) and
`ProjectMonitorService`, which diffs findings against `wm_monitor_alerts` and calls the existing
`IWorkNotificationEngine`. The frontend adds a `ProjectMonitorStore`, form callouts and tree-row marks.

**Tech Stack:** .NET 9 / EF Core (Postgres) / MediatR / xUnit + Moq + FluentAssertions; Angular 20 standalone +
signals / Vitest.

**Spec:** `docs/superpowers/specs/next/2026-09-29-wm-plan4-cleanup-and-project-monitoring-design.md`

## Global Constraints

- **Branches and code:**
  - Stay on `feature/wm-hierarchy-approval-notification-engine` in BOTH repos. No branch switch, no push.
  - Work Management code only. The only shared touchpoints allowed are the WM rows in
    `NotificationTemplateSeeder`, `ApplicationDbContext` DbSet, `DependencyInjection.cs` WM registrations, and
    the migration.
  - Never write the word "SQLite" under `src/` (architecture test).
- **Build and test gate:**
  - Backend: `dotnet build src/ONEVO.Api -c Release`, then
    `dotnet test tests/ONEVO.Tests.Unit -c Release --no-build` and
    `dotnet test tests/ONEVO.Tests.Architecture -c Release --no-build`.
  - Use Release: a running Debug API locks `bin/Debug`.
  - Frontend: `npx ng build` and `npx ng test --watch=false`. The known pre-existing failure is
    `member-management-popup.component.spec.ts`.
- **Files and tooling:**
  - Frontend `.ts` files are CRLF. Use the Edit tool, not multi-line sed.
  - No monitor rule ever blocks a save. Warnings are advisory only.
  - Default daily hours = **8** when the legal entity's work window is unset.
  - Migration: generate with `ConnectionStrings__MigrationConnection="Host=x;Database=x;Username=x;Password=x"`.
    Do NOT apply it.
- **Commits:**
  - End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - List removed files/tests in the commit body.

## File Structure

Backend (`src/ONEVO.*`, WM feature folders):
- `Application/Features/WorkManagement/Monitoring/`: new feature folder.
  - `Services/WorkCapacityCalculator.cs`: pure daily hours + working-day count.
  - `Services/WorkCalendar.cs`: record `WorkCalendar(decimal DailyHours, IReadOnlySet<DayOfWeek> WorkingDays)`.
  - `Services/IWorkCalendarResolver.cs`, `Services/WorkCalendarResolver.cs`: project → legal entity → calendar.
  - `Services/ProjectMonitorSnapshot.cs`: records for the in-memory snapshot.
  - `Services/ProjectMonitorRules.cs`: pure rules + EDF, returns `MonitorFinding`s.
  - `Services/MonitorRuleCodes.cs`: the rule code constants.
  - `Services/IProjectMonitorSnapshotLoader.cs`, `Services/ProjectMonitorSnapshotLoader.cs`: load a snapshot
    from the repositories.
  - `Services/IProjectMonitorService.cs`, `Services/ProjectMonitorService.cs`: evaluate + diff + notify.
  - `RepositoryInterfaces/IMonitorAlertRepository.cs`.
  - `Queries/CheckModuleCapacity/*`, `Queries/CheckTaskLoad/*`, `Queries/ListProjectMonitorAlerts/*`.
  - `DTOs/MonitorDtos.cs`.
- Domain `Features/WorkManagement/Monitoring/Entities/MonitorAlert.cs`.
- Infrastructure:
  - `Persistence/Configurations/WorkManagement/MonitorAlertConfiguration.cs`.
  - `Persistence/Repositories/WorkManagement/EfMonitorAlertRepository.cs`.
  - `Services/WorkManagement/ProjectMonitorJob.cs`, replacing `SprintLifecycleJob.cs`.
  - The migration `AddProjectMonitorAlerts`.
- Api: `Controllers/Tenant/WorkManagement/ProjectMonitorController.cs`.
- Tasks: `Tasks/Services/TaskEditConflictDetector.cs`.

Frontend (`src/app/modules/work/`):
- `models/dto/monitor.dto.ts`, `data-access/project-monitor-api.service.ts`, `state/project-monitor.store.ts`.
- `ui/monitor-warning-callout/monitor-warning-callout.component.ts`: amber callout list.
- `ui/approval-history-list/approval-history-list.component.ts`: inline history list, replacing the modal.
- Modified:
  - `feature/work-approvals/work-approvals.component.ts` (tabs, Sprint Requests).
  - `ui/approval-request-row` (hours hint).
  - `ui/sub-module-form`, `ui/task-form-modal` (callouts).
  - `ui/milestone-tree-node`, the three tree rows, `feature/milestone-tree-tab` (red marks).

---

## PART A: CLEANUPS

### Task A1: Ancestor-walk dedupe in queries

**Files:**
- Modify: `Application/Features/WorkManagement/Objectives/Queries/GetObjectiveById/GetObjectiveByIdQueryHandler.cs`
  (loop ~L55-65).
- Modify the same way:
  - `GetObjectiveMembers/GetObjectiveMembersQueryHandler.cs`
  - `GetObjectiveSubtree/GetObjectiveSubtreeQueryHandler.cs`
  - `Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQueryHandler.cs`
  - `Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
  - `Objectives/Queries/GetMyProjectMilestones/GetMyProjectMilestonesQueryHandler.cs`
  - `CalendarEvents/Queries/GetProjectCalendar/GetProjectCalendarQueryHandler.cs`
  - `Objectives/Services/MilestoneMembershipCoordinator.cs` (L90-120 ancestor loops).
- Test: the existing handler tests in `tests/ONEVO.Tests.Unit/Features/WorkManagement/**`.

**Interfaces:**
- Consumes: `IWorkHierarchyService.LoadTreeAsync(tenantId, projectId, ct)`, `ProjectModuleTree.AncestorChain(id)`.
- Produces: no new API. Behaviour-preserving.

The recipe per handler:

- [ ] **Step 1: Replace the `while (cursor.ParentObjectiveId is not null) { await _objectives.GetByIdForTenantAsync(...) }` loop.**

```csharp
var tree = await _hierarchy.LoadTreeAsync(tenantId, objective.ProjectId, ct);
var ancestors = tree.AncestorChain(objective.Id).Skip(1); // parents only, nearest first
// existing per-ancestor logic now iterates `ancestors`
```

  For handlers that built their own `objectivesById` dictionary for walking (GetMyProjectMilestones,
  GetProjectCalendar), construct `new ProjectModuleTree(allObjectives)` from the list they already load and
  use `AncestorChain` instead of the inline cursor loop (no extra query).
  - Inject `IWorkHierarchyService` via the constructor where it is needed.
  - Existing tests: construct the handler with `WorkHierarchyServiceMocks.WithModules(<modules the test already
    returns from the objectives mock>)`. Move `WorkHierarchyServiceMocks` from the `Tasks` test namespace to
    `tests/.../WorkManagement/WorkHierarchyServiceMocks.cs` (namespace `ONEVO.Tests.Unit.Features.WorkManagement`)
    and update its usings.
  - Remove any now-unused `_objectives.GetByIdForTenantAsync` setups for ancestors from those tests.
- [ ] **Step 2: Build and run the WM tests.**
  `dotnet build src/ONEVO.Api -c Release && dotnet test tests/ONEVO.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~WorkManagement"`.
  Expected: all green.
- [ ] **Step 3: Commit.** `refactor(work-management): query handlers walk the module tree via ProjectModuleTree instead of a query per ancestor`.

### Task A2: Task-edit stale fix

**Files:**
- Create: `Application/Features/WorkManagement/Tasks/Services/TaskEditConflictDetector.cs`.
- Modify: `Tasks/Appliers/TaskEditApplier.cs`.
- Modify: `Tasks/RepositoryInterfaces/ITaskPercentageLogRepository.cs` (+ Ef impl): add
  `Task<bool> AnyAfterAsync(Guid tenantId, Guid taskId, DateTimeOffset after, CancellationToken ct = default)`.
- Test: `tests/.../WorkManagement/Tasks/TaskEditConflictDetectorTests.cs`, `TaskEditApplierTests.cs` (existing).

**Interfaces:**
- Produces: `static bool TaskEditConflictDetector.IsStale(WorkTask current, TaskEditInput request, IReadOnlyList<TaskEditLog> logsAfterSnapshot, bool progressChangedAfterSnapshot)`.

- [ ] **Step 1: Failing tests.**

```csharp
public class TaskEditConflictDetectorTests
{
    private static WorkTask Task() => new() { Title = "B", Priority = "medium", EstimatedHours = 5m, ProgressPercent = 40 };
    private static TaskEditInput Input(string title = "A", decimal? est = 5m, int? progress = null)
        => new(title, null, "medium", null, est, null, progress, null, null);
    private static TaskEditLog Log(string field, object? oldV, object? newV) => new()
    {
        OldValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = oldV }),
        NewValuesJson = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = newV })
    };

    [Fact] public void NoLogs_NotStale()
        => TaskEditConflictDetector.IsStale(Task(), Input(title: "C"), [], false).Should().BeFalse();

    [Fact] public void OtherChangedSameField_Stale()
        => TaskEditConflictDetector.IsStale(Task(), Input(title: "C"), [Log("title", "A", "B")], false).Should().BeTrue();
}
```

  Write these concrete cases (each `[Fact]`):
  1. No logs → false.
  2. The log changed `estimatedHours` (3→5), the request payload has title "C" (snapshot title "B") and
     est = 3 (its form value = the snapshot value) → false (request changes only title; other changed only
     estimate).
  3. The log changed `title` ("A"→"B"), the request title "C" (snapshot "A") → true.
  4. The log changed `title` ("A"→"B"), the request title "A" (unchanged from the snapshot) and est 9 → false.
  5. `progressChangedAfterSnapshot = true` and request ProgressPercent = 70 → true. With ProgressPercent null →
     false.

  (The `WorkTask` is a class, not a record: build fresh instances in each test instead of `with`.)
- [ ] **Step 2: Run.** `dotnet test ... --filter TaskEditConflictDetectorTests`. Expected: FAIL (type missing).
- [ ] **Step 3: Implement.**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>A pending task edit is stale only when someone else, after the request's snapshot, changed a field
/// that this request also changes. Status moves, clock-in and push write no edit logs, so they never make a
/// request stale.</summary>
public static class TaskEditConflictDetector
{
    public static bool IsStale(WorkTask current, TaskEditInput request,
        IReadOnlyList<TaskEditLog> logsAfterSnapshot, bool progressChangedAfterSnapshot)
    {
        var snapshot = new Dictionary<string, string?>
        {
            ["title"] = Norm(current.Title), ["description"] = Norm(current.Description),
            ["priority"] = Norm(current.Priority), ["dueDate"] = Norm(current.DueDate),
            ["estimatedHours"] = Norm(current.EstimatedHours), ["storyPoints"] = Norm(current.StoryPoints),
            ["sprintId"] = Norm(current.SprintId),
        };
        var changedByOthers = new HashSet<string>();
        // newest first → the last write is the earliest log's OldValue, i.e. the snapshot-time value
        foreach (var log in logsAfterSnapshot.OrderByDescending(l => l.ChangedAt))
            foreach (var (field, oldValue) in Parse(log.OldValuesJson))
            {
                snapshot[field] = oldValue;
                changedByOthers.Add(field);
            }
        if (progressChangedAfterSnapshot) changedByOthers.Add("progressPercent");
        if (changedByOthers.Count == 0) return false;

        var requested = new Dictionary<string, string?>
        {
            ["title"] = Norm(request.Title.Trim()), ["description"] = Norm(request.Description?.Trim()),
            ["priority"] = Norm(request.Priority), ["dueDate"] = Norm(request.DueDate),
            ["estimatedHours"] = Norm(request.EstimatedHours), ["storyPoints"] = Norm(request.StoryPoints),
        };
        if (request.SprintId is not null) requested["sprintId"] = Norm(request.SprintId);
        var changedByRequest = requested.Where(kv => snapshot.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => kv.Key).ToHashSet();
        if (request.ProgressPercent is not null) changedByRequest.Add("progressPercent");

        return changedByRequest.Overlaps(changedByOthers);
    }

    private static IEnumerable<(string, string?)> Parse(string json)
        => (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [])
            .Select(kv => (kv.Key, kv.Value.ValueKind == JsonValueKind.Null ? null : Norm(kv.Value.ToString())));

    private static string? Norm(object? value) => value switch
    {
        null => null,
        decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n.ToString("0.####", CultureInfo.InvariantCulture),
        _ => value.ToString()?.ToLowerInvariant() is { } s2 && Guid.TryParse(s2, out _) ? s2 : value.ToString(),
    };
}
```

  In `TaskEditApplier`, replace the `UpdatedAt > snap` check with:

```csharp
if (request.TargetUpdatedAtSnapshot is { } snap && (task.UpdatedAt ?? task.CreatedAt) > snap)
{
    var logs = (await _editLogs.GetForTaskAsync(request.TenantId, task.Id, ct)).Where(l => l.ChangedAt > snap).ToList();
    var progressMoved = await _percentageLogs.AnyAfterAsync(request.TenantId, task.Id, snap, ct);
    var input0 = JsonSerializer.Deserialize<TaskEditInput>(context.PayloadJson, TaskPayload.Options);
    if (input0 is not null && TaskEditConflictDetector.IsStale(task, input0, logs, progressMoved))
        return ApplyOutcome.Stale;
}
```

  (Deserialize the payload once and reuse it below. Inject `ITaskEditLogRepository` and
  `ITaskPercentageLogRepository`.) Update `TaskEditApplierTests`:
  - The old "task updated after snapshot → Stale" test becomes "updated after snapshot by a status move (no
    edit logs) → Applied".
  - Add "an other-user title edit after the snapshot + request changes title → Stale".
- [ ] **Step 4: Run the tests.** Expected: PASS.
- [ ] **Step 5: Commit.** `fix(work-management): task edit approval goes stale only on a conflicting field edit`.

### Task A3: Current allocated hours on inbox rows

**Files:**
- Modify: `Approvals/DTOs/WorkApprovalRequestResponse.cs` (add a trailing `decimal? CurrentAllocatedHours = null`).
- Modify: `Approvals/Mappers/WorkApprovalRequestMapper.cs`.
- Modify: `Queries/ListProjectWorkApprovals/ListProjectWorkApprovalsQueryHandler.cs`.
- Test: `tests/.../WorkManagement/Approvals/ListProjectWorkApprovalsQueryHandlerTests.cs`.

- [ ] **Step 1: Failing test.** An inbox with one `module.allocation_extend` pending row targeting module M
  (AllocatedHours 120) → the response has `CurrentAllocatedHours == 120`. A task row → null.
- [ ] **Step 2: Implement.**
  - Load `tree` once for both scopes (move the `LoadTreeAsync` above the switch).
  - Mapper signature: `ToResponse(WorkApprovalRequest r, IReadOnlyDictionary<Guid,string> names, ProjectModuleTree? tree = null)`.
  - Set `CurrentAllocatedHours = r.ActionType == WorkActionTypes.ModuleAllocationExtend && r.TargetId is { } id ? tree?.Get(id)?.AllocatedHours : null`.
- [ ] **Step 3: Run the Approvals tests. Commit.** `feat(work-management): inbox rows carry the module's current allocated hours for allocation requests`.

### Task A4: Frontend: Approvals tabs, Sprint Requests section, hours hint

**Files:**
- Create: `ui/approval-history-list/approval-history-list.component.ts` (+ spec). Move the list markup, styles
  and `historyKindLabel` rendering out of `ui/approval-history-modal/approval-history-modal.component.ts`.
  Inputs: `items`, `loading`, `error`. Output: `retry`.
- Delete: `ui/approval-history-modal/*` (the component + spec) once nothing imports it.
- Modify: `feature/work-approvals/work-approvals.component.ts` (+ spec).
- Modify: `models/dto/work-approval.dto.ts` (`currentAllocatedHours?: number | null`), `models/approval.model.ts`
  if the ApprovalItem mapper needs it, `state/work-approvals.store.ts` mapping.
- Modify: `ui/approval-request-row/approval-request-row.component.ts` (+ spec).

- [ ] **Step 1: Failing specs.**
  1. The approvals page renders `app-tabs` with "Requests" and "History".
  2. Clicking History shows `app-approval-history-list` and calls `getApprovalHistory` once.
  3. Engine rows with `targetType === 'sprint'` appear under an `h2` "Sprint Requests", not "Task Requests".
  4. The approval row for an allocation item with `currentAllocatedHours: 120` and requested +30 shows
     "Current 120 h → +30 h (150 h)".
  5. The `?tab=history` query param opens History.
- [ ] **Step 2: Implement.**
  - Use the shared `TabsComponent` from `shared/ui/tabs` (read its inputs first).
  - `activeTab = signal<'requests'|'history'>('requests')`, initialised from `route.snapshot.queryParamMap.get('tab')`.
  - Switching calls `router.navigate([], { queryParams: { tab }, queryParamsHandling: 'merge', replaceUrl: true })`.
  - Remove the History toolbar button, `historyOpen` and the `<app-approval-history-modal>`.
  - The pending-requests section still uses `sentPendingRequests()` (history items with status pending), so
    keep `loadHistory()` running on init as today.
  - Split `workApprovals()` into `taskApprovals = computed(() => workApprovals().filter(i => i.targetType !== 'sprint'))`
    and `sprintApprovals = computed(() => …=== 'sprint')`.
- [ ] **Step 3:** `npx ng test --watch=false --include='**/work/**'`. Expected: green.
- [ ] **Step 4: Commit.** `feat(work): Approvals page Requests/History tabs, separate Sprint Requests, current-hours hint on allocation rows`.

### Task A5: Dead code and useless test sweep (WM only)

- [ ] **Step 1: Find candidates, backend.** For each public type/method under
  `src/ONEVO.Application/Features/WorkManagement` and the WM infrastructure repositories, check it with
  `grep -rn "<Name>" src tests`. A type with no reference outside its own file and its own test is dead.
  Specifically re-check the leftovers of Plans 1-3:
  - old request-table repositories/DTOs (`ObjectiveChangeRequest*`, `TaskCreationRequest*`, `TaskEditRequest*`,
    `TaskStatusChangeRequest*` backend types whose tables were dropped);
  - `ISprintRepository.GetByStatusAsync` (only the job used it; delete it with the job in Task B6 if still unused).
- [ ] **Step 2: Find candidates, frontend.** Search `src/app/modules/work` exports with `grep -rn`. Also flag
  specs that only assert `toBeTruthy()` on creation while another spec already covers the component.
- [ ] **Step 3:** Delete them. Build and run the full gate for both repos.
- [ ] **Step 4: Commit per repo,** listing every removed file/test. `chore(work-management): remove dead code and redundant tests`.

---

## PART B: PROJECT MONITORING ENGINE

### Task B1: Capacity calculator + calendar resolver

**Files:**
- Create: `Monitoring/Services/WorkCalendar.cs`, `WorkCapacityCalculator.cs`, `IWorkCalendarResolver.cs`,
  `WorkCalendarResolver.cs`.
- Test: `tests/.../WorkManagement/Monitoring/WorkCapacityCalculatorTests.cs`.

**Interfaces (Produces):**

```csharp
public sealed record WorkCalendar(decimal DailyHours, IReadOnlySet<DayOfWeek> WorkingDays)
{
    public static WorkCalendar Default { get; } = new(8m, new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday });
    public int WorkingDaysBetween(DateOnly from, DateOnly to) => WorkCapacityCalculator.WorkingDays(from, to, WorkingDays);
    public decimal Capacity(int people, DateOnly from, DateOnly to) => people * WorkingDaysBetween(from, to) * DailyHours;
}
public static class WorkCapacityCalculator
{
    public const decimal DefaultDailyHours = 8m;
    public static decimal DailyHours(TimeOnly? start, TimeOnly? end, int? breakMinutes);
    public static int WorkingDays(DateOnly from, DateOnly to, IReadOnlySet<DayOfWeek> days); // inclusive; 0 if to<from
    public static IReadOnlySet<DayOfWeek> ParseWorkingDays(string? json); // "[1,2,3,4,5]" ISO 1=Mon..7=Sun; bad/empty → Mon-Fri
    public static WorkCalendar FromLegalEntity(LegalEntity? entity);
}
public interface IWorkCalendarResolver { Task<WorkCalendar> ForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default); }
```

- [ ] **Step 1: Failing tests.**
  - `DailyHours(09:00, 17:00, null) == 8`.
  - `DailyHours(09:00, 18:00, 60) == 8`.
  - `DailyHours(22:00, 06:00, 0) == 8` (overnight).
  - `DailyHours(null, 17:00, 0) == 8` (fallback).
  - `DailyHours(09:00, 09:30, 60) == 8` (≤0 → fallback).
  - `WorkingDays(Mon 2026-09-28, Fri 2026-10-09, Mon-Fri) == 10`.
  - `WorkingDays(to < from) == 0`.
  - `ParseWorkingDays("[1,2,3,4,5,6]")` contains Saturday.
  - `ParseWorkingDays("junk")` is Mon-Fri.
- [ ] **Step 2: Implement.**
  - Daily hours: `var minutes = (end - start).TotalMinutes` (TimeOnly subtraction wraps midnight), minus the
    break. `minutes <= 0 → 8`. Result `Math.Round((decimal)minutes / 60m, 2)`.
  - ISO day 7 → `DayOfWeek.Sunday`, else `(DayOfWeek)n`.
  - `WorkCalendarResolver`:
    - `project = IProjectRepository.GetByIdForTenantAsync`.
    - `le = ILegalEntityRepository.GetByIdForTenantAsync(tenantId, project.OwningLegalEntityId) ?? GetPrimaryByTenantIdAsync(tenantId)`.
    - Return `WorkCapacityCalculator.FromLegalEntity(le)`.
  - Register it scoped in the Application WM DI (find where `IWorkHierarchyService` is registered and add it
    next to it).
- [ ] **Step 3: Run the tests: PASS. Commit.** `feat(work-management): working capacity calculator and project work calendar`.

### Task B2: Snapshot + rules + EDF

**Files:**
- Create: `Monitoring/Services/MonitorRuleCodes.cs`, `ProjectMonitorSnapshot.cs`, `ProjectMonitorRules.cs`.
- Test: `tests/.../WorkManagement/Monitoring/ProjectMonitorRulesTests.cs`.

**Interfaces (Produces):**

```csharp
public static class MonitorRuleCodes
{
    public const string ModuleOverCapacity = "module_over_capacity";
    public const string ModuleCapacityShortfall = "module_capacity_shortfall";
    public const string EmployeeDeadlineOverload = "employee_deadline_overload";
    public const string TaskClockedOverEstimate = "task_clocked_over_estimate";
    public const string ModuleClockedOverAllocated = "module_clocked_over_allocated";
    public const string SprintOverdue = "sprint_overdue";
    public const string TaskOverdue = "task_overdue";
    public const string ModuleOverdue = "module_overdue";
}
public static class MonitorTargetTypes { public const string Module = "module"; public const string Sprint = "sprint"; public const string Task = "task"; }

public sealed record MonitorModule(Guid Id, Guid? ParentId, Guid? CreatorPositionModuleId, string Title, DateOnly StartDate, DateOnly EndDate,
    decimal AllocatedHours, decimal CompletedHours, bool IsAchieved, int MemberCount);
public sealed record MonitorSprint(Guid Id, Guid? CreatorPositionModuleId, string Name, string Status, DateOnly? EndDate);
public sealed record MonitorTask(Guid Id, Guid ModuleId, Guid? CreatorPositionModuleId, Guid? SprintId, string Title, DateOnly? DueDate,
    decimal? EstimatedHours, decimal CompletedHours, bool IsComplete, decimal ClockedHours, IReadOnlyList<Guid> AssigneeIds);
public sealed record ProjectMonitorSnapshot(Guid ProjectId, WorkCalendar Calendar,
    IReadOnlyList<MonitorModule> Modules, IReadOnlyList<MonitorSprint> Sprints, IReadOnlyList<MonitorTask> Tasks);
public sealed record MonitorFinding(string RuleCode, string TargetType, Guid TargetId, string TargetTitle,
    Guid? SubjectEmployeeId, string Message, IReadOnlyDictionary<string, object?> Details);

public static class ProjectMonitorRules
{
    public static IReadOnlyList<MonitorFinding> Evaluate(ProjectMonitorSnapshot s, DateOnly today);
    public static MonitorFinding? ModuleOverCapacity(MonitorModule m, WorkCalendar cal);
    public static MonitorFinding? ModuleCapacityShortfall(MonitorModule m, WorkCalendar cal, DateOnly today);
    public static IReadOnlyList<MonitorFinding> EmployeeDeadlineOverload(Guid employeeId, IReadOnlyList<MonitorTask> openTasks, WorkCalendar cal, DateOnly today);
}
```

- [ ] **Step 1: Failing tests (one `[Fact]` each).** Calendar = Default, today = Mon 2026-10-05.
  1. Module 10 working days (Mon 10-05 → Fri 10-16), 5 members, allocated 500 → `module_over_capacity`
     (capacity 400). Allocated 400 → none.
  2. Shortfall: module 10-05 → 10-16, allocated 200, completed 0, 5 members, today Mon 10-12 (5 working days
     left → capacity 200) → none. Allocated 201 → `module_capacity_shortfall`. Achieved → none. Past end → none.
  3. The shortfall is not reported when over-capacity is already reported for the same module.
  4. EDF, today Mon 10-05, one employee:
     - task A due Fri 10-09 est 20 → capacity(10-09) = 5 days × 8 = 40 → ok;
     - add task B due Wed 10-07 est 30: demand(10-07) = 30 > capacity 24 → B flagged;
       demand(10-09) = 50 > 40 → A also flagged.
     - Two assignees on B → remaining 15 each → demand(10-07) = 15 ≤ 24; demand(10-09) = 35 ≤ 40 → none.
  5. EDF ignores tasks with no due date, due before today, complete, or with estimate ≤ 0. Remaining uses
     `max(0, est - completed)`.
  6. `task_clocked_over_estimate`: clocked 6, est 5 → finding. Est null → none.
  7. `module_clocked_over_allocated`: the clocked sum of tasks in the module and its child modules exceeds
     the allocation.
  8. `sprint_overdue`: active, end 10-02 < today, one incomplete task → finding. Complete-status sprint or all
     tasks done → none.
  9. `task_overdue`: due 10-02, not complete → finding.
  10. `module_overdue`: end 10-02, not achieved, an incomplete task in a child module → finding.
- [ ] **Step 2: Run the tests: FAIL.**
- [ ] **Step 3: Implement** `ProjectMonitorRules`. Core pieces:

```csharp
public static MonitorFinding? ModuleOverCapacity(MonitorModule m, WorkCalendar cal)
{
    var capacity = cal.Capacity(Math.Max(1, m.MemberCount), m.StartDate, m.EndDate);
    return m.AllocatedHours > capacity
        ? new(MonitorRuleCodes.ModuleOverCapacity, MonitorTargetTypes.Module, m.Id, m.Title, null,
            $"Allocated {Fmt(m.AllocatedHours)} h but {Math.Max(1, m.MemberCount)} member(s) can produce at most {Fmt(capacity)} h between {m.StartDate:MMM d} and {m.EndDate:MMM d}.",
            new Dictionary<string, object?> { ["allocatedHours"] = m.AllocatedHours, ["capacityHours"] = capacity, ["memberCount"] = m.MemberCount })
        : null;
}

public static MonitorFinding? ModuleCapacityShortfall(MonitorModule m, WorkCalendar cal, DateOnly today)
{
    if (m.IsAchieved || today > m.EndDate) return null;
    var from = today > m.StartDate ? today : m.StartDate;
    var remaining = Math.Max(0, m.AllocatedHours - m.CompletedHours);
    var capacity = cal.Capacity(Math.Max(1, m.MemberCount), from, m.EndDate);
    return remaining > capacity
        ? new(MonitorRuleCodes.ModuleCapacityShortfall, MonitorTargetTypes.Module, m.Id, m.Title, null,
            $"{Fmt(remaining)} h of work remain but the team can produce only {Fmt(capacity)} h before the {m.EndDate:MMM d} deadline.",
            new Dictionary<string, object?> { ["remainingHours"] = remaining, ["capacityHours"] = capacity })
        : null;
}

public static IReadOnlyList<MonitorFinding> EmployeeDeadlineOverload(Guid employeeId, IReadOnlyList<MonitorTask> tasks, WorkCalendar cal, DateOnly today)
{
    var open = tasks
        .Where(t => !t.IsComplete && t.DueDate is { } d && d >= today && (t.EstimatedHours ?? 0) > 0 && t.AssigneeIds.Contains(employeeId))
        .Select(t => (Task: t, Due: t.DueDate!.Value,
            Remaining: Math.Max(0, t.EstimatedHours!.Value - t.CompletedHours) / Math.Max(1, t.AssigneeIds.Count)))
        .OrderBy(x => x.Due).ToList();
    var findings = new List<MonitorFinding>();
    var flagged = new HashSet<Guid>();
    foreach (var due in open.Select(x => x.Due).Distinct())
    {
        var demand = open.Where(x => x.Due <= due).Sum(x => x.Remaining);
        var capacity = cal.Capacity(1, today, due);
        if (demand <= capacity) continue;
        foreach (var x in open.Where(x => x.Due <= due && flagged.Add(x.Task.Id)))
            findings.Add(new(MonitorRuleCodes.EmployeeDeadlineOverload, MonitorTargetTypes.Task, x.Task.Id, x.Task.Title, employeeId,
                $"Assignee has {Fmt(demand)} h of work due by {due:MMM d} but only {Fmt(capacity)} h of working time.",
                new Dictionary<string, object?> { ["employeeId"] = employeeId, ["dueDate"] = due.ToString("yyyy-MM-dd"), ["demandHours"] = demand, ["capacityHours"] = capacity }));
    }
    return findings;
}
private static string Fmt(decimal h) => h.ToString("0.#", CultureInfo.InvariantCulture);
```

  `Evaluate`:
  - Build the module tree from `ParentId` via `new ProjectModuleTree(...)`? No: `ProjectModuleTree` takes
    `Objective`s, so compute subtree ids here with a small local children-map BFS.
  - Then run all rules. EDF runs over the distinct assignees of all tasks.
  - `module_capacity_shortfall` is skipped when `module_over_capacity` fired for the same module.
  - Sprint "has unfinished task" = any snapshot task with that SprintId and `!IsComplete`.
  - `Active` = `SprintStatuses.Active`.
  - Messages follow the spec wording.
- [ ] **Step 4: Run the tests: PASS. Commit.** `feat(work-management): project monitor rules (capacity, forecast, deadline overload, overdue, clocked hours)`.

### Task B3: Snapshot loader + preview queries + controller

**Files:**
- Create: `Monitoring/Services/IProjectMonitorSnapshotLoader.cs`, `ProjectMonitorSnapshotLoader.cs`.
- Create: `Monitoring/DTOs/MonitorDtos.cs`.
- Create: `Monitoring/Queries/CheckModuleCapacity/{CheckModuleCapacityQuery,Handler}.cs`.
- Create: `Monitoring/Queries/CheckTaskLoad/{CheckTaskLoadQuery,Handler}.cs`.
- Modify: `ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs` (+ Ef impl): add
  `Task<IReadOnlyList<ProjectMember>> ListActiveForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)`.
- Modify: `Tasks/RepositoryInterfaces/ITaskAssignmentRepository.cs` (or wherever assignments are listed; grep
  `GetByTaskIds`). Needs a batched `taskId → employeeIds` lookup. Add
  `Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetEmployeeIdsByTaskIdsAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)`
  if none exists.
- Create: `Api/Controllers/Tenant/WorkManagement/ProjectMonitorController.cs`.
- Test: `tests/.../Monitoring/CheckModuleCapacityQueryHandlerTests.cs`, `CheckTaskLoadQueryHandlerTests.cs`.

**Interfaces (Produces):**

```csharp
public interface IProjectMonitorSnapshotLoader { Task<ProjectMonitorSnapshot> LoadAsync(Guid tenantId, Guid projectId, CancellationToken ct = default); }
public sealed record MonitorWarning(string Code, string Message, Guid? EmployeeId = null);
public sealed record ModuleCapacityCheckResponse(decimal DailyHours, int WorkingDays, int MemberCount, decimal CapacityHours, decimal AllocatedHours, IReadOnlyList<MonitorWarning> Warnings);
public sealed record TaskLoadCheckResponse(IReadOnlyList<MonitorWarning> Warnings);
public sealed record CheckModuleCapacityQuery(Guid ProjectId, Guid? ModuleId, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, IReadOnlyList<Guid>? MemberEmployeeIds) : IRequest<Result<ModuleCapacityCheckResponse>>;
public sealed record CheckTaskLoadQuery(Guid ProjectId, Guid? TaskId, IReadOnlyList<Guid> AssigneeEmployeeIds, DateOnly? DueDate, decimal? EstimatedHours) : IRequest<Result<TaskLoadCheckResponse>>;
```

- [ ] **Step 1: Loader.**
  - Load the objectives (`GetAllByProjectIdAsync`, active only), members (`ListActiveForProjectAsync`), sprints
    (`ISprintRepository` project list; grep for an existing `GetByProjectIdAsync`), tasks (`GetByProjectAsync`),
    statuses (project template → `MarksTaskComplete` set), closed-session minutes
    (`GetTotalClosedSessionMinutesForTasksAsync`), assignments, and the calendar (`IWorkCalendarResolver`).
  - MemberCount = distinct(active members of that module ∪ {OwnerId}).
  - ClockedHours = minutes / 60m.
  - IsComplete = the status id is in the MarksTaskComplete set.
- [ ] **Step 2: Failing handler tests.**
  - `CheckModuleCapacity`, calendar Default, 2026-10-05 → 10-16, 5 member ids, allocated 500 → capacity 400,
    one warning `module_over_capacity`. Allocated 300 → no warnings.
  - `CheckTaskLoad`: the loader returns an assignee with a task due 10-07 est 20; a new task due 10-07 est 10
    with today = 10-05 → a warning with that EmployeeId (demand 30 > 24).
  - Editing (TaskId = the existing task): that task is replaced by the new values, not double-counted.
  - Unauthenticated → Forbidden.
  - Inject `TimeProvider` (grep: if the codebase has `IClock`/`IDateTimeProvider`, use that) for `today`.
- [ ] **Step 3: Implement the handlers.**
  - Resolve the caller employee with `ICallerIdentityResolver`, then check they have an active membership in
    the project (`HasActiveMembershipAsync`) or are its lead. Otherwise Forbidden.
  - Module check: members = `MemberEmployeeIds ∪ existing members (when ModuleId) ∪ caller`. Reuse
    `ProjectMonitorRules.ModuleOverCapacity` plus `ModuleCapacityShortfall` (with CompletedHours from the
    existing module, or 0) and map findings to `MonitorWarning(code, message)`.
  - Task check: build the task list from the snapshot, swap/add a synthetic `MonitorTask` for the new values,
    then run `EmployeeDeadlineOverload` per assignee. Only findings for the new/edited task id count; return one
    warning per assignee. Its message is prefixed with the employee's display name
    (`ResolveDisplayNamesByEmployeeIdAsync`).
- [ ] **Step 4: Controller.** Route `api/v1/work`, the same attributes as `WorkApprovalsController`:
  - `POST projects/{projectId:guid}/monitor/module-check` body `ModuleCapacityCheckRequest(Guid? ModuleId, DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, List<Guid>? MemberEmployeeIds)`.
  - `POST projects/{projectId:guid}/monitor/task-check` body `TaskLoadCheckRequest(Guid? TaskId, List<Guid> AssigneeEmployeeIds, DateOnly? DueDate, decimal? EstimatedHours)`.
  - `GET projects/{projectId:guid}/monitor/alerts` (wired in B5).
- [ ] **Step 5: Build + tests: PASS. Commit.** `feat(work-management): live capacity and deadline-load checks for module and task forms`.

### Task B4: Alert entity, repository, migration (+ retire sprint OverdueNotifiedAt)

**Files:**
- Create: `Domain/Features/WorkManagement/Monitoring/Entities/MonitorAlert.cs`.

```csharp
public class MonitorAlert : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public Guid? SubjectEmployeeId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset FirstDetectedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
}
```

- Create: `Infrastructure/Persistence/Configurations/WorkManagement/MonitorAlertConfiguration.cs`:
  - Table `wm_monitor_alerts`; max lengths TargetType 20, RuleCode 40, TargetTitle 500, Message 1000.
  - Index `(TenantId, ProjectId, ResolvedAt)` named `ix_wm_monitor_alerts_tenant_project_resolved`.
  - Unique filtered index `(TenantId, TargetId, RuleCode, SubjectEmployeeId)` with
    `.HasFilter("resolved_at IS NULL").AreNullsDistinct(false)`, named `ux_wm_monitor_alerts_open_key`.
- Create: `Application/.../Monitoring/RepositoryInterfaces/IMonitorAlertRepository.cs`:

```csharp
public interface IMonitorAlertRepository
{
    Task<IReadOnlyList<MonitorAlert>> ListOpenTrackedForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<MonitorAlert>> ListOpenForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task AddAsync(MonitorAlert alert, CancellationToken ct = default);
}
```

- Create: `EfMonitorAlertRepository.cs`. Register it in `Infrastructure/DependencyInjection.cs` next to
  `EfWorkNotificationLogRepository`. Add `DbSet<MonitorAlert> MonitorAlerts` in `ApplicationDbContext` next to
  `WorkNotificationLogs`.
- Modify: `Domain/.../Sprints/Entities/Sprint.cs`: delete `OverdueNotifiedAt` and the sprint configuration
  mapping (grep `OverdueNotifiedAt`).

- [ ] **Step 1:** Add the files above. Build.
- [ ] **Step 2: Migration.**
  `ConnectionStrings__MigrationConnection="Host=x;Database=x;Username=x;Password=x" dotnet ef migrations add AddProjectMonitorAlerts -p src/ONEVO.Infrastructure -s src/ONEVO.Api -c ApplicationDbContext --configuration Release`.
  - Inspect the generated file: it must ONLY create `wm_monitor_alerts` (+ indexes) and drop
    `sprints.overdue_notified_at`. If it touches other modules, the snapshot is stale: stop and diagnose (see
    memory `feedback_hrms_migration_snapshot_corruption`).
  - Append the same RLS block used in `20260928061506_AddWorkApprovalEngineFoundation.cs` for
    `wm_monitor_alerts` in Up, and its drop in Down.
- [ ] **Step 3: Run the architecture tests** (RLS coverage, naming). Expected: PASS.
- [ ] **Step 4: Commit.** `feat(work-management): wm_monitor_alerts table (RLS) and drop sprint overdue_notified_at`.

### Task B5: Monitor service (diff + notify) + alerts query + template

**Files:**
- Create: `Monitoring/Services/IProjectMonitorService.cs`, `ProjectMonitorService.cs`.
- Create: `Monitoring/Queries/ListProjectMonitorAlerts/{Query,Handler}.cs`; DTO
  `MonitorAlertResponse(Guid Id, string TargetType, Guid TargetId, string TargetTitle, string RuleCode, Guid? SubjectEmployeeId, string Message, DateTimeOffset FirstDetectedAt)`.
- Modify: `Domain/.../Notifications/Entities/WorkNotificationLog.cs`: `WorkNotificationKinds.Alert = "alert"`.
- Modify: `WorkNotificationEngine.cs`:
  - add `MonitorAlertTemplate = "work_monitor_alert"`;
  - `Kind == Alert` → that template;
  - `DecisionWord(Alert) = string.Empty`;
  - skip the actor name lookup when `ActorEmployeeId == Guid.Empty` (use "ONEVO").
- Modify: `WorkActionLabels.cs`: labels for `monitor.<code>`:
  - module_over_capacity → "Module over capacity"
  - module_capacity_shortfall → "Module cannot finish in time"
  - employee_deadline_overload → "Assignee overloaded before deadline"
  - task_clocked_over_estimate → "Clocked hours exceed estimate"
  - module_clocked_over_allocated → "Clocked hours exceed allocation"
  - sprint_overdue → "Sprint past end date"
  - task_overdue → "Task past due date"
  - module_overdue → "Module past end date"
- Modify: `Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`: add `work_monitor_alert`
  (InAppTitleTemplate "Project alert", InAppBodyTemplate `{{actionLabel}}: "{{targetTitle}}"`) after
  `work_approval_decided`. Update `tests/.../Features/Auth/NotificationTemplateSeederTests.cs` to count 37 and
  add the code to its list.
- Test: `tests/.../Monitoring/ProjectMonitorServiceTests.cs`, `ListProjectMonitorAlertsQueryHandlerTests.cs`,
  `WorkNotificationEngineTests.cs` (alert template case).

**Interfaces (Produces):**

```csharp
public interface IProjectMonitorService
{
    /// <summary>Evaluates one project, upserts/resolves wm_monitor_alerts and notifies once per new alert. Does NOT save.</summary>
    Task<int> EvaluateProjectAsync(Guid tenantId, Guid projectId, DateOnly today, CancellationToken ct = default);
}
```

- [ ] **Step 1: Failing service tests** (mock the loader, the alerts repo, `IWorkHierarchyService`,
  `IWorkNotificationEngine`, `IProjectRepository`, `IWorkTaskRepository`, `ISprintRepository`).
  1. A new finding → `AddAsync` with FirstDetectedAt = LastSeenAt = now and NotifiedAt set;
     `NotifyAsync` once with Kind `alert`, ActionType `monitor.module_over_capacity`, recipients = [holder].
  2. The same finding with an open alert → no add, no notify, LastSeenAt updated.
  3. An open alert with no finding → ResolvedAt set, no notify.
  4. Holder resolution uses `CreatorPositionObjectiveId ?? ObjectiveId` for tasks. `FindActiveHolderAsync`
     returns null → recipient = project LeadId.
- [ ] **Step 2: Implement.**
  - Key = `(TargetId, RuleCode, SubjectEmployeeId)`.
  - The position module per target type follows the spec §B4:
    - task: the snapshot's `CreatorPositionModuleId ?? ModuleId` (the records carry it since B2).
    - sprint: `?? tree.Root.Id`.
    - module: `?? ParentId ?? Id`.
  - Group new findings by recipient + target so one target with several subjects (EDF per employee) sends one
    notification per alert row.
  - `DetailsJson = JsonSerializer.Serialize(finding.Details)`.
- [ ] **Step 3: Alerts query.**
  - The caller must be a project member or the lead.
  - Visible modules = `tree.AtOrBelow(caller's direct module memberships)`; the lead or root owner sees all.
  - Filter alerts: module targets in visible, task/sprint targets whose module (via snapshot-free lookup: load
    tasks' `ObjectiveId` with `GetObjectiveIdsByTaskIdsAsync`) is visible. Sprints are visible to any project
    member.
  - Wire the GET in the controller.
- [ ] **Step 4: Run the tests: PASS. Commit.** `feat(work-management): project monitor service stores alerts and notifies the creator position once`.

### Task B6: ProjectMonitorJob replaces SprintLifecycleJob

**Files:**
- Create: `Infrastructure/Services/WorkManagement/ProjectMonitorJob.cs`.
- Delete: `SprintLifecycleJob.cs` and `tests/.../Sprints/SprintLifecycleJobTests.cs`.
- Delete `ISprintAudienceResolver` + impl if now unused (grep).
- Delete `ISprintRepository.GetByStatusAsync` if unused.
- Modify: `Infrastructure/DependencyInjection.cs:655`: register `ProjectMonitorJob` instead.
- Modify: `IProjectRepository` (+ Ef): `Task<IReadOnlyList<Project>> ListActiveAcrossTenantsAsync(CancellationToken ct = default)`
  (admin-mode sweep: `IsActive && !IsAchieved && !IsDeleted`), if no equivalent exists.
- Test: `tests/.../Monitoring/ProjectMonitorJobTests.cs`: a static pure helper
  `ProjectMonitorJob.ShouldRun(DateTimeOffset now, DateTimeOffset? lastRun)` returns true when lastRun is null
  or ≥ 1h old.

- [ ] **Step 1:** Write the job, mirroring SprintLifecycleJob's structure:
  - `PeriodicTimer` 1h; the first run happens after a 1-minute delay.
  - Admin mode, group projects by tenant, `SwitchToTenantAsync`, then
    `EvaluateProjectAsync(tenantId, projectId, today)` per project.
  - `SaveChangesAsync` per tenant; the try/catch per tenant logs and continues.
  - `today = DateOnly.FromDateTime(UtcNow)`.
- [ ] **Step 2:** Delete the old job + tests. Build. Full unit + architecture gate.
- [ ] **Step 3: Commit.** `feat(work-management): hourly ProjectMonitorJob replaces SprintLifecycleJob (overdue sprint is now a monitor rule)`.
  Body lists the removed files.

### Task B7: Frontend monitor data layer + warning callouts in forms

**Files:**
- Create: `models/dto/monitor.dto.ts`:

```ts
export interface MonitorWarningDto { code: string; message: string; employeeId?: string | null; }
export interface ModuleCapacityCheckDto { dailyHours: number; workingDays: number; memberCount: number; capacityHours: number; allocatedHours: number; warnings: MonitorWarningDto[]; }
export interface TaskLoadCheckDto { warnings: MonitorWarningDto[]; }
export interface MonitorAlertDto { id: string; targetType: 'module' | 'sprint' | 'task'; targetId: string; targetTitle: string; ruleCode: string; subjectEmployeeId: string | null; message: string; firstDetectedAt: string; }
```

- Create: `data-access/project-monitor-api.service.ts` (+ spec). It has `checkModule(projectId, body)`,
  `checkTask(projectId, body)` and `listAlerts(projectId)`. Base URL pattern: copy from
  `work-approvals-api.service.ts`.
- Create: `ui/monitor-warning-callout/monitor-warning-callout.component.ts` (+ spec):
  - input `warnings: MonitorWarningDto[]`;
  - renders nothing when empty, otherwise `role="status"`, amber border/bg using the existing
    `--color-warning*` tokens (grep the tokens; fall back to `#fef3c7`/`#92400e` as used in approvals);
  - heading "Heads up — you can still save"; `data-testid="monitor-warnings"`.
- Modify: `ui/sub-module-form/sub-module-form.component.ts`:
  - new input `projectId` (pass it from milestone-tree-tab);
  - an effect debounced 400 ms on (dates, allocated hours, selected members) calls `checkModule`;
  - it shows the callout above the actions;
  - the submit button stays enabled;
  - errors from the check are swallowed (callout hidden).
- Modify: `ui/task-form-modal/task-form-modal.component.ts`:
  - same pattern on (assignees, due date, estimated hours) → `checkTask({ taskId: mode==='edit' ? taskId : null, ... })`;
  - skip the call when there are no assignees or no due date or estimate.
- Test: the specs for the api service, the callout, and the two forms (the warning appears; submit is not
  disabled).

- [ ] **Step 1: Failing specs.** **Step 2: Implement.** **Step 3:** `ng test` for the work folder. **Step 4: Commit.**
  `feat(work): live capacity and deadline-load warnings in module and task forms (never blocking)`.

### Task B8: Frontend tree red marks

**Files:**
- Create: `state/project-monitor.store.ts` (+ spec), a root-provided signal store:
  - `load(projectId)`;
  - `alerts = signal<MonitorAlertDto[]>([])`;
  - `alertsByTarget = computed(() => Map<targetId, MonitorAlertDto[]>)`;
  - `alertsFor(id): MonitorAlertDto[]`;
  - `count = computed(...)`.
- Modify: `feature/milestone-tree-tab/milestone-tree-tab.component.ts/.html`:
  - call `monitor.load(projectId)` on init and after tree reloads;
  - header badge `data-testid="monitor-alert-count"`: "{n} alerts" in `var(--color-danger)` when n > 0.
- Modify: `ui/milestone-tree-node/milestone-tree-node.component.ts`: inject the store; pass
  `[alerts]="monitor.alertsFor(node().id)"` to each row.
  - Check which id the node uses for sprint/task rows (`node().id` vs `sprintId`/`taskId`) and map
    accordingly.
- Modify: the module/sprint/task tree rows:
  - add `alerts = input<readonly MonitorAlertDto[]>([])`;
  - render a red dot `<span data-testid="tree-row-alert" class="tree-row__alert" [attr.title]="alertTitle()" aria-label="Has alerts">`
    after the title when there are alerts;
  - `alertTitle = computed(() => alerts().map(a => a.message).join('\n'))`;
  - dot style: `inline-block w-2 h-2 rounded-full bg-[var(--color-danger)] ml-1.5`.
- Test: row specs (the dot shows with alerts, the title lists messages), the store spec, and the tree-tab spec
  (count badge).

- [ ] **Step 1: Failing specs.** **Step 2: Implement.** **Step 3:** `ng build` + full `ng test`. **Step 4: Commit.**
  `feat(work): project monitor alerts shown as red marks on the Tree`.

### Task C: Final gate + docs

- [ ] Backend full gate (Release build, unit, architecture). Frontend `ng build` + full `ng test`.
- [ ] Review with `git diff <base>...HEAD --stat` in both repos. The only files touched outside WM are the
  allowed shared touchpoints.
- [ ] Move nothing to `finished/` yet (the user still has the migration + browser pass to do).
- [ ] Update memory `project_hrms_wm_approval_notification_engine.md` with the Plan 4 results.
