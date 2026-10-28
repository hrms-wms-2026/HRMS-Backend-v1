# WM-03 and WM-05 — Verification Evidence (Pre-UAT Audit 2026-10-11)

Both findings are confirmed-by-design, not defects. No code change was made for either beyond the
one WM-05 regression test noted below (`ProjectMonitorServiceTests.cs`). Gathered 2026-10-12 as
part of `docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md` (Task 3).

## WM-03 — "Sprint is project-scoped by a confirmed product decision. This is NOT a defect."

**Verdict: CONFIRMED.**

- `Sprint.ProjectId` is the entity's only scoping foreign key — `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs:21`. No `ModuleId`/`ObjectiveId` field exists on `Sprint`.
- `CreatorPositionObjectiveId` (`Sprint.cs:25`) is an approval-routing pointer ("the Module whose current owner is this object's creator position — the approver of edits"), not a scoping FK — disclosed so "no Module reference at all" isn't overstated.
- The module-scoping column was deliberately removed: `src/ONEVO.Infrastructure/Migrations/20260923100313_DropSprintObjectiveId.cs:28-56` drops the old `ObjectiveId` column/index and adds `ix_sprints_tenant_id_project_id_status`.
- `EfSprintRepository.GetByProjectAsync` (`src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs:25-29`) filters by `ProjectId` only.
- Design record: `docs/superpowers/specs/finished/2026-09-23/2026-09-23-wm-independent-sprints-design.md:10,18` — "A Sprint stops belonging to one Module. It becomes a project-level 'bunch of tasks'... Any active project member can create a sprint (no module required)." — `docs/superpowers/plans/finished/2026-09-23/2026-09-23-wm-independent-sprints.md:5`.
- Write path never carries a module id: `sprint-form.component.ts:177-193` → `sprint-api.service.ts:26-28` → `POST /work/projects/{projectId}/sprints`.

**The one real observation (UX, not a defect in scoping):** the Tree tab's "Add sprint" action lives on a Module row (`tree-explanation-panel.component.ts:125`, `module-tree-row.component.ts:109`) and nests resulting sprints visually under that Module (`project-detail.store.ts:273-299`, `sprint-tree-row.component.ts:16`'s depth-based indent). That nesting is derived from **task membership** (`GetObjectiveSprintsQueryHandler` → `EfSprintRepository.GetContainingObjectiveTasksAsync`, `EfSprintRepository.cs:31-40`: finds tasks whose `ObjectiveId` matches and returns the distinct sprints those tasks' `SprintId`s point to), not from any sprint→module FK. A sprint with tasks in multiple modules would appear under each; a brand-new sprint with zero tasks appears under none via this route. This can visually read as module-scoping to a user even though the data model and API never scope by module. **No code change recommended** — this is a UI clarity note for a future design pass, not a WM-03 defect.

## WM-05 — "Overdue sprints are notified, not auto-completed, by current product design. This is NOT a defect."

**Verdict: CONFIRMED.**

- `SprintStatuses` has exactly four values: `Draft`, `Active`, `Complete`, `Achieved` (`Sprint.cs:5-11`). No `Incomplete`/`Overdue` status exists in code today.
- Every site that writes `Sprint.Status`, and what triggers it — all in `SprintWriteService.cs` (the only class with a write-capable `ISprintRepository`):

  | Line | Write | Trigger |
  |---|---|---|
  | `SprintWriteService.cs:160` | → `Active` | `StartSprintCommandHandler` (explicit API call) or an approved decide-request |
  | `SprintWriteService.cs:216` | → `Complete` | `CompleteSprintCommandHandler` (explicit API call) or an approved decide-request |
  | `SprintWriteService.cs:248` | → `Achieved` | `AchieveSprintCommandHandler` (explicit API call) or an approved decide-request |
  | `SprintWriteService.cs:324` | → `Active` (uncomplete) | `RevertWorkApprovalRequestCommandHandler` (explicit API call) |
  | `SprintWriteService.cs:339` | → previous status (unachieve) | `RevertWorkApprovalRequestCommandHandler` (explicit API call) |
  | `Reverters/SprintStartReverter.cs:34` | → `Draft` | `RevertWorkApprovalRequestCommandHandler` (explicit API call) |

  Every one of these six write sites traces to an authenticated user's explicit command or an explicit approve/revert decision on a pending approval request (`DecideWorkApprovalRequestCommandHandler.cs:73-91`, `RevertWorkApprovalRequestCommandHandler.cs:69`) — never a timer or background job.
- The overdue-sprint detector is structurally incapable of writing a Sprint: `ProjectMonitorService`'s constructor (`src/ONEVO.Application/Features/WorkManagement/Monitoring/Services/ProjectMonitorService.cs:29-31`) takes `IProjectMonitorSnapshotLoader, IMonitorAlertRepository, IWorkHierarchyService, IProjectRepository, IWorkNotificationEngine, IDateTimeProvider` — no `ISprintRepository`/`ISprintWriteService`/`IWorkTaskRepository`. `EvaluateProjectAsync` only writes `MonitorAlert` rows and calls `IWorkNotificationEngine.NotifyAsync` once per newly-opened alert.
- `ProjectMonitorRules.SprintOverdue` (`ProjectMonitorRules.cs:42-51`) is a pure static function over a read-only `ProjectMonitorSnapshot` — it cannot mutate anything.
- Historical, non-live evidence only: `src/ONEVO.Infrastructure/Migrations/20260918112327_AddSprintLifecycleRedesign.cs:15` ran a one-time backfill `UPDATE sprints SET status = 'active' WHERE status IN ('future', 'incomplete')` when the old auto-transitioning statuses were retired — this is a completed migration, not a live code path, and cannot run again.
- Design record confirming the old auto-transition job was deliberately removed: `docs/superpowers/plans/next/2026-10-05-wm-notification-completion.md:35-37` — "`SprintLifecycleJob` (old date-driven `Active → Incomplete` auto-transition) was explicitly removed and replaced by an overdue *notification only*... Do not re-introduce an `Incomplete` status or wire this template into the overdue/monitor-alert path."
- The `work_sprint_incomplete` notification (`SprintWriteService.cs:229`) fires only from the **explicit** `ApplyCompleteAsync` path when a user completes a sprint that still has unfinished tasks — unrelated to the overdue/monitor-alert path.

**Regression test added** (Task 3, no behavior change): `tests/ONEVO.Tests.Unit/Features/WorkManagement/Monitoring/ProjectMonitorServiceTests.cs` —
- `Constructor_HasNoSprintWriteDependency` — reflection check that `ProjectMonitorService`'s constructor carries no `ISprintRepository`/`ISprintWriteService`/`IWorkTaskRepository` parameter, so a future change that tries to make it write sprints breaks this test immediately.
- `OverdueActiveSprintWithOpenTasks_OpensAnAlertAndNotifiesOnce_NeverChangesSprintStatus` — pins today's behavior: an overdue Active sprint with an open task opens exactly one `sprint_overdue` alert and sends exactly one Alert-kind notification; the sprint's own in-memory `Status` is never touched.

**No other code changes for WM-03 or WM-05** — both are confirmed-by-design.
