# WM Plan 4: Cleanups + Project Monitoring Engine (design)

Date: 2026-09-29. Scope: Work Management only, both repos. The work stays on the current branch
`feature/wm-hierarchy-approval-notification-engine` (no branch switch). Builds on Plans 1-3 of the
approval/notification engine (spec 2026-09-28-wm-hierarchy-approval-notification-engine-design.md).

## Part A: Small updates

### A1. Replace repeated parent walking in queries
These handlers walk `ParentObjectiveId` one query per level: GetObjectiveById, GetObjectiveMembers,
GetObjectiveSubtree, GetObjectiveSprints, GetObjectiveTasks, GetMyProjectMilestones,
GetProjectCalendar, plus MilestoneMembershipCoordinator's ancestor loops. Each now loads the project
tree once (`IWorkHierarchyService.LoadTreeAsync`) and uses `ProjectModuleTree.AncestorChain` /
`IsAtOrAbove` / `AtOrBelow`. Behaviour is unchanged, and the existing tests are kept (mocks switch to
`WorkHierarchyServiceMocks`). MoveTaskStatus's completed-hours roll-up walks with tracked entities
for mutation, so it stays as it is.

### A2. Remove dead code and useless tests (WM only)
- Remove WM types/methods with no production callers after Plans 1-3 (found by reference search) and
  tests that only assert mocks or duplicate other tests.
- Every removal is listed in its commit message.
- `SprintLifecycleJob` and `Sprint.OverdueNotifiedAt` go away in Part B (replaced by the monitor).

### A3. Approvals page: Requests and History tabs
- The page uses the shared `app-tabs` with two tabs: **Requests** (default) and **History**.
- **Requests** tab: My Pending Requests, Status-template requests, **Task Requests** (engine task
  rows only), **Sprint Requests** (new section, engine sprint rows), Allocation Requests, Module
  Change Requests and Invitations.
- **History** tab: the decided-request list that used to live in `ApprovalHistoryModalComponent`,
  rendered inline and loaded when the tab first opens. The History toolbar button and the modal are
  removed.
- The selected tab is kept in the `?tab=` query param.

### A4. Current-hours hint on allocation requests
`WorkApprovalRequestResponse` gets `decimal? CurrentAllocatedHours`, set for `module.allocation_extend`
rows from the project tree. The allocation row shows "Current {x} h → Requested +{y} h (= {x+y} h)".

### A5. Task-edit stale fix
A pending `task.edit` is stale only if **another edit after the snapshot changed a field that this
request changes**:
1. Rebuild each field's value at snapshot time from the current task, rolled back through
   `TaskEditLog`s with `ChangedAt > snapshot`: the earliest log's OldValues for that field wins.
2. Fields the request changes are the payload fields whose value differs from the snapshot-time value.
   progressPercent counts only if set; sprintId only if set.
3. Fields changed by others are the NewValues keys of those logs, plus `progressPercent` if a
   `TaskPercentageLog` exists after the snapshot.
4. The request is stale iff those two field sets intersect.

Status moves, clock-in and push no longer cause stale. Pure helper
`TaskEditConflictDetector` (unit-tested).

## Part B: Project Monitoring Engine

The monitor predicts and detects exceptions on Modules, Sprints and Tasks, warns while planning (it
**never blocks** a save), and notifies the creator-position holder through the existing
`IWorkNotificationEngine`.

### B1. Working capacity (pure: `WorkCapacityCalculator`)
- `DailyHours(LegalEntity?)` = end − start (overnight wraps) − break minutes. Falls back to **8h** when
  start or end is unset or the result is ≤ 0.
- `WorkingDays(from, to, workingDays)` counts inclusive dates whose ISO weekday is in the legal
  entity's `StandardWorkingDays` JSON (default Mon-Fri). No holidays.
- The calendar comes from the project's `OwningLegalEntityId`, falling back to the tenant's primary
  legal entity. Resolved by `IWorkCalendarResolver` → `WorkCalendar(DailyHours, WorkingDays set)`.

### B2. Rules (pure: `ProjectMonitorRules`, input = project snapshot, today)
| Code | Target | Condition |
|---|---|---|
| `module_over_capacity` | module | AllocatedHours > members × WorkingDays(start..end) × daily |
| `module_capacity_shortfall` | module | not achieved, today ≤ end, remaining (Allocated − Completed) > members × WorkingDays(max(today,start)..end) × daily; only when not already `module_over_capacity` |
| `employee_deadline_overload` | task | employee deadline-window check fails for this task (below) |
| `task_clocked_over_estimate` | task | clocked hours (closed sessions) > EstimatedHours > 0 |
| `module_clocked_over_allocated` | module | clocked hours of tasks at or below the module > AllocatedHours > 0 |
| `sprint_overdue` | sprint | Active, EndDate < today, has an unfinished task |
| `task_overdue` | task | DueDate < today, status not MarksTaskComplete |
| `module_overdue` | module | not achieved, EndDate < today, has an unfinished task at or below it |

- **Members (manpower)** = distinct active members and owners of the module AND all its sub-modules (a module's allocation is split across its sub-modules - user decision 2026-09-29). Shared helper ModuleManpower.
- **Unfinished** = status is not `MarksTaskComplete`. Subtasks count as tasks.

**Employee deadline-window check (EDF).** For an employee:
1. Take their open tasks with DueDate ≥ today and EstimatedHours > 0.
2. Per task, remaining = max(0, Estimated − CompletedHours) ÷ assignee count.
3. Sort the tasks by due date.
4. For each distinct due date d: demand(d) = sum of remaining for tasks due ≤ d, and
   capacity(d) = WorkingDays(today..d) × daily.
5. If demand(d) > capacity(d), every task due ≤ d that has not been flagged yet is flagged at-risk,
   with details {employeeId, dueDate, demandHours, capacityHours}.

### B3. Live warnings (preview endpoints, no persistence)
- `POST api/v1/work/projects/{projectId}/monitor/module-check`
  body `{ moduleId?, parentModuleId?, startDate, endDate, allocatedHours, memberEmployeeIds? }` →
  `{ dailyHours, workingDays, memberCount, capacityHours, allocatedHours, warnings[] }`.
  Members come from memberEmployeeIds ∪ the existing members when moduleId is given; a create form
  with no members uses the owner (the caller) as 1.
- `POST api/v1/work/projects/{projectId}/monitor/task-check`
  body `{ taskId?, assigneeEmployeeIds[], dueDate, estimatedHours }` → `{ warnings[] }`. Runs the
  EDF check per assignee with the new or edited task swapped in.
  Message: "{name} has {demand} h of work due by {date} but only {capacity} h of working time."
- Warning = `{ code, message, employeeId? }`. The frontend shows an amber, non-blocking callout in the
  module create/edit form and in the task form modal, debounced 400 ms on field changes. Saving is
  never disabled.

### B4. Alert store + monitor job
- Table `wm_monitor_alerts`: Id, TenantId, ProjectId, TargetType (module/sprint/task), TargetId,
  RuleCode, SubjectEmployeeId?, Message, DetailsJson, FirstDetectedAt, LastSeenAt, ResolvedAt?,
  NotifiedAt?, plus BaseEntity columns. Tenant RLS policy like wm_notification_log.
- A partial unique index covers the open alert key (TenantId, TargetId, RuleCode, SubjectEmployeeId)
  WHERE ResolvedAt IS NULL.
- `ProjectMonitorService.EvaluateProjectAsync(tenantId, projectId, today)`:
  - Build the snapshot and run the rules, then diff the findings against the open alerts.
  - **New alert** → insert it and notify once.
  - **Still present** → update LastSeenAt and Message.
  - **Gone** → set ResolvedAt.
  - A problem that returns later creates a new alert and a new notification.
- **Recipient:** the holder of the target's creator position:
  - Task: CreatorPositionObjectiveId ?? ObjectiveId.
  - Sprint: CreatorPositionObjectiveId ?? root.
  - Module: CreatorPositionObjectiveId ?? ParentObjectiveId ?? itself.
  - The holder is resolved with `FindActiveHolderAsync(excluding Guid.Empty)`, falling back to the
    project lead.
- **Notification:**
  - New kind `alert`, new template `work_monitor_alert`: title "Project alert", body
    "{{actionLabel}}: \"{{targetTitle}}\"".
  - ActionType is `monitor.<rule>`, with labels in WorkActionLabels, e.g. "Module over capacity".
  - Actor is Guid.Empty (system). The related entity is the target.
- **Job:** `ProjectMonitorJob` (BackgroundService, hourly; admin mode + per-tenant switch like
  SprintLifecycleJob) evaluates every active, non-achieved project. It replaces `SprintLifecycleJob`,
  which is deleted along with `Sprint.OverdueNotifiedAt` (the column is dropped in the migration).
- `GET api/v1/work/projects/{projectId}/monitor/alerts` → the open alerts the caller can read
  (targets at or below their memberships; the project lead and root owner see all).

### B5. Tree red marks
- A `ProjectMonitorStore` loads the open alerts for the project on the Tree tab.
- `milestone-tree-node` passes `alerts` for its node id to the module/sprint/task rows.
- Each row shows a red dot, titled with the alert messages (tooltip).
- The tree tab header shows "{n} alerts" in red when n > 0.

### Testing and done criteria
- Unit tests cover the calculator, every rule, EDF, TaskEditConflictDetector, the monitor-service
  diff (new/existing/resolved/notify once), the preview handlers, the inbox hours field and the
  frontend components/stores.
- The gate:
  - backend `dotnet build src/ONEVO.Api -c Release`, unit + architecture tests green;
  - frontend `ng build` and `ng test` green except the known pre-existing
    member-management-popup failure.
- The migration is generated but not applied (the user applies it).
