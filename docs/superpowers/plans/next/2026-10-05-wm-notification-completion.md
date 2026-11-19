# WM Notification Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make two Work Management (WM) notification gaps work end-to-end: (1) a sprint that is completed while it still has unfinished tasks must notify with the `work_sprint_incomplete` template instead of silently reusing `work_sprint_completed`; (2) the notification bell must let the recipient Approve or Reject a pending WM approval request directly from the dropdown, without navigating to the Approvals page first.

**Architecture:** Task 1 is a one-line branch in `SprintWriteService.ApplyCompleteAsync` that already has every piece it needs (the method already computes `movedTaskIds`, the template already exists in the seeder, the notification engine's recipient/navigation plumbing already handles the `"sprint"` relatedEntityType) — this is restoring a dropped branch, not new design. Task 2 adds inline Approve/Reject affordances to the existing `NotificationDropdownComponent`/`NotificationBellComponent`, reusing the exact same `WorkApprovalsApiService.approveWorkApproval`/`rejectWorkApproval` calls the Approvals page already uses (both take only the approval request id — no project context needed — so the globally-scoped bell can call them directly).

**Tech Stack:** .NET 9 / EF Core / xUnit / Moq / FluentAssertions (backend, `HRMS-Backend-v1`); Angular 21 standalone components / Vitest (frontend, `Hrms--Web-application---front-end---v1`).

**Spec:** No separate spec doc — this plan was written directly from live investigation (two Explore-agent audits of the backend notification engine and the frontend bell/navigation wiring, plus `git log` archaeology on the Sprint lifecycle redesign) in session `dfab916b-e6f9-46cb-b517-53a0bd13c408`, 2026-10-05. Background is captured inline per task below.

## Global Constraints

- **Work Management module only.** Do not touch CoreHr, Leave, TimeAttendance, People, Calendar, or shared notification/outbox infrastructure code. (See memory `feedback_scope_work_management_only`.)
- **Stay on the current branch in both repos — do not create a new branch.** Backend and frontend are both currently on `feature/wm-milestones-page-redesign`. (User's explicit instruction this session; see memory `feedback_hrms_no_new_branch_for_fixes`.)
- **Do not push, and do not open a PR.**
- Backend build/test must use `-c Release` (a running Debug API locks `bin/Debug` DLLs — don't kill the user's dev server, just build/test Release instead).
- Every commit message ends with: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`
- **Useless-test rule:** don't delete a test unless it tests deleted code, duplicates another test, or asserts nothing behavioural.
- Do NOT touch the nine retired templates (`work_task_creation_request_*`, `work_allocation_extend_request_*`, `work_objective_edit_request_*`, etc.) — confirmed intentionally superseded by the unified `work_approval_requested/decided/commented` flow, not bugs. Do not add triggers for them and do not delete the seed rows (the seeder's idempotency/count tests hard-code the template list — removing rows is out of scope for this plan).

## Review Focus

1. **A sprint completed with zero unfinished tasks (including an empty sprint) must still send `work_sprint_completed`, never `work_sprint_incomplete`** — the branch is keyed on `movedTaskIds.Count > 0`, and Task 1's Test C pins the all-complete case.
2. **The bell's Approve/Reject buttons must appear only for `work_approval_requested`**, never for `work_approval_decided` or `work_approval_commented` (both share the same `relatedEntityType: "work_approval_request"`, so a naive check on that field would wrongly show buttons on already-decided notifications too) — Task 2's Test 1 pins this.
3. **Deciding a request that's already been decided/cancelled by someone else (or where the viewer is no longer the approver, e.g. after a position transfer) must show an inline "Already handled" state, not crash or silently do nothing** — Task 2's Test 4 pins this by mocking a rejected Observable from the API call.
4. **Clicking Approve/Reject must not also fire the notification's own click-to-navigate handler** (`itemClicked`) — the two are now sibling buttons, not nested, so this is structurally guaranteed, but Task 2's Test 2 asserts `navigate` was never called on an approve click as a regression guard.
5. **Clicking Approve twice in quick succession (double-click, or slow network) must not fire two decide calls** — guarded by `pendingDecisionIds` disabling the buttons for the duration of the in-flight call; Task 2's Test 5 pins this.

---

## Background: why `work_sprint_incomplete` is dead and how to revive it correctly

`work_sprint_incomplete` is seeded in `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs:105-110` ("Sprint ended incomplete" / `"{{sprintName}}" on {{objectiveName}} ended with unfinished tasks and is now Incomplete.`) but has **no caller anywhere in `src/`**. Git history explains why: `SprintStatuses` (`src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs`) only has `Draft/Active/Complete/Achieved` — there is no `Incomplete` status today. Commit `6595f562` ("Rebuild SprintLifecycleJob as overdue-notify-only") explicitly removed the old date-driven `Active → Incomplete` auto-transition and replaced it with an overdue *notification only* (status is never mutated for lateness — that job was itself later replaced by the hourly `ProjectMonitorJob`'s generic `sprint_overdue` alert rule).

**Do not** re-introduce an `Incomplete` status or wire this template into the overdue/monitor-alert path — that would duplicate `work_monitor_alert`'s `sprint_overdue` rule, which already owns "sprint is running late."

The correct, narrow fix: `ApplyCompleteAsync` (`src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintWriteService.cs:191-228`) already computes `movedTaskIds` — the tasks that were **not** done when the sprint was completed and got moved to the backlog or another sprint. When `movedTaskIds.Count > 0`, the sprint ended with unfinished work: that is exactly the scenario the seeded template's body text describes. Today line 226 always sends `"work_sprint_completed"` regardless. Fix: send `"work_sprint_incomplete"` instead, only in that case.

## Task 1: Backend — notify `work_sprint_incomplete` when a sprint completes with unfinished tasks

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintWriteService.cs:226`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs`

**Interfaces:**
- Consumes: `SprintWriteService.ApplyCompleteAsync` already has `movedTaskIds` (a `List<Guid>` built in the loop at lines 201-211) and the private `NotifyAudienceAsync(Guid tenantId, Sprint sprint, IReadOnlyList<Guid> audience, string templateCode, CancellationToken ct)` helper (line 275) that calls `INotificationDispatcher.SendTemplatedAsync(tenantId, recipientUserId, templateCode, placeholders, "sprint", sprint.Id, ct)` per audience member.
- Produces: no new public signatures — this is a one-line behavioural change inside an existing method.

- [ ] **Step 1: Write the failing tests**

Add `using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;` and `using ONEVO.Domain.Features.CoreHr.Entities;` to the top of `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs` (they are not currently imported there), then add these two tests after the existing `ApplyComplete_Backlog_ClearsIncompleteTasksAndCompletes` test:

```csharp
[Fact]
public async Task ApplyComplete_UnfinishedTasksMovedToBacklog_NotifiesIncomplete()
{
    var sprint = Sprint(SprintStatuses.Active);
    var objectiveId = Guid.NewGuid();
    var todo = Guid.NewGuid();
    var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = todo, ObjectiveId = objectiveId };
    _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
    _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, todo, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TaskStatus { Id = todo, MarksTaskComplete = false });

    var employeeId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    _w.Members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, objectiveId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = employeeId, ObjectiveId = objectiveId, ProjectId = ProjectId } });
    _w.Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Employee { Id = employeeId, UserId = userId });

    await _w.Writes().ApplyCompleteAsync(TenantId, Actor, sprint, new SprintCompleteInput("backlog", null));

    _w.Notifications.Verify(x => x.SendTemplatedAsync(
        TenantId, userId, "work_sprint_incomplete",
        It.IsAny<IReadOnlyDictionary<string, string>>(), "sprint", sprint.Id, It.IsAny<CancellationToken>()), Times.Once);
    _w.Notifications.Verify(x => x.SendTemplatedAsync(
        It.IsAny<Guid>(), It.IsAny<Guid>(), "work_sprint_completed",
        It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task ApplyComplete_AllTasksAlreadyComplete_NotifiesCompleted()
{
    var sprint = Sprint(SprintStatuses.Active);
    var objectiveId = Guid.NewGuid();
    var done = Guid.NewGuid();
    var task = new WorkTask { Id = Guid.NewGuid(), SprintId = sprint.Id, StatusId = done, ObjectiveId = objectiveId };
    _w.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask> { task });
    _w.Statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, done, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TaskStatus { Id = done, MarksTaskComplete = true });

    var employeeId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    _w.Members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, objectiveId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = employeeId, ObjectiveId = objectiveId, ProjectId = ProjectId } });
    _w.Membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Employee { Id = employeeId, UserId = userId });

    await _w.Writes().ApplyCompleteAsync(TenantId, Actor, sprint, new SprintCompleteInput("backlog", null));

    _w.Notifications.Verify(x => x.SendTemplatedAsync(
        TenantId, userId, "work_sprint_completed",
        It.IsAny<IReadOnlyDictionary<string, string>>(), "sprint", sprint.Id, It.IsAny<CancellationToken>()), Times.Once);
    _w.Notifications.Verify(x => x.SendTemplatedAsync(
        It.IsAny<Guid>(), It.IsAny<Guid>(), "work_sprint_incomplete",
        It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~SprintWriteServiceTests"`
Expected: both new tests FAIL — `ApplyComplete_UnfinishedTasksMovedToBacklog_NotifiesIncomplete` fails because the code currently always sends `"work_sprint_completed"` (the `Times.Once` check on `work_sprint_incomplete` fails); `ApplyComplete_AllTasksAlreadyComplete_NotifiesCompleted` should already PASS (current behaviour already sends `work_sprint_completed` unconditionally) — that's fine, it's there to pin the no-regression case going forward.

- [ ] **Step 3: Implement the fix**

In `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintWriteService.cs`, replace line 226:

```csharp
        await NotifyAudienceAsync(tenantId, trackedSprint, audience, "work_sprint_completed", ct);
```

with:

```csharp
        var completionTemplateCode = movedTaskIds.Count > 0 ? "work_sprint_incomplete" : "work_sprint_completed";
        await NotifyAudienceAsync(tenantId, trackedSprint, audience, completionTemplateCode, ct);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit -c Release --filter "FullyQualifiedName~SprintWriteServiceTests"`
Expected: all tests in the file PASS, including both new ones.

- [ ] **Step 5: Run the full backend gate**

Run: `dotnet build src/ONEVO.Api -c Release` then `dotnet test tests/ONEVO.Tests.Unit -c Release` then `dotnet test tests/ONEVO.Tests.Architecture -c Release`
Expected: 0 warnings / 0 errors on the build; all unit tests pass; all architecture tests pass (no new SQLite references, no cross-module violations — this change touches only `src/ONEVO.Application/Features/WorkManagement`).

- [ ] **Step 6: Commit**

```bash
git add tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintWriteServiceTests.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintWriteService.cs
git commit -m "$(cat <<'EOF'
fix(wm-sprints): notify work_sprint_incomplete when completion leaves unfinished tasks

ApplyCompleteAsync always sent work_sprint_completed even when tasks were
moved to the backlog/another sprint because they weren't done. The
work_sprint_incomplete template existed in the seeder but had no caller
since the old status-mutating SprintLifecycleJob was retired. Branch on
the already-computed movedTaskIds count instead of reviving any
Incomplete status (that concept stays retired - lateness is covered by
ProjectMonitorJob's sprint_overdue alert).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Frontend — inline Approve/Reject on `work_approval_requested` notifications in the bell

**Background:** `WorkApprovalsApiService.approveWorkApproval(id, editedPayloadJson?, comment?)` and `.rejectWorkApproval(id, comment?)` (`src/app/modules/work/data-access/work-approvals-api.service.ts:46-51`) both operate on the approval request id alone — no project context required — and the backend handler (`DecideWorkApprovalRequestCommandHandler.cs`) treats `comment` as fully optional on both approve and reject. This means the globally-scoped notification bell can call them directly using the notification's own `relatedEntityId`, with no need to look up a per-project "pending approvals" list first. `NotificationItem`/`NotificationDto` (`src/app/core/services/notification-api.service.ts`) already carries `templateCode`, which distinguishes `work_approval_requested` (actionable) from `work_approval_decided`/`work_approval_commented` (not actionable — same `relatedEntityType` but already resolved).

**Note for whoever executes this task:** `notification-dropdown.component.ts` and `notification-bell.component.ts` were already edited in the working tree during the investigation session that produced this plan (uncommitted). Read both files as they currently stand before touching them — Steps 1 and 3 below describe the target state; if the files already match it, skip straight to Step 2/4 (run the tests) rather than re-applying identical edits.

**Files:**
- Modify: `src/app/layouts/main-layout/top-navbar/notification-bell/notification-dropdown/notification-dropdown.component.ts`
- Modify: `src/app/layouts/main-layout/top-navbar/notification-bell/notification-bell.component.ts`
- Test: `src/app/layouts/main-layout/top-navbar/notification-bell/notification-bell.component.spec.ts`

**Interfaces:**
- Consumes: `WorkApprovalsApiService.approveWorkApproval(id: string, editedPayloadJson?: string, comment?: string): Observable<WorkApprovalRequestDto>` and `.rejectWorkApproval(id: string, comment?: string): Observable<WorkApprovalRequestDto>`; `NotificationItem.templateCode: string`, `.relatedEntityId: string | null`, `.isRead: boolean`; `NotificationsStore.markRead(id: string): Promise<void>`.
- Produces: `NotificationDropdownComponent` gains inputs `pendingIds: ReadonlySet<string>`, `decisions: ReadonlyMap<string, NotificationDecisionState>` and outputs `approveClicked: Output<NotificationItem>`, `rejectClicked: Output<NotificationItem>`, where `NotificationDecisionState = 'approved' | 'rejected' | 'error'` is exported from that file. `NotificationBellComponent` exposes `onApprove(item)`/`onReject(item)` handlers.

- [ ] **Step 1: Target state for `notification-dropdown.component.ts`**

The component renders each notification as a `<div class="notification-dropdown__row">` wrapping the existing clickable `<button class="notification-dropdown__item">` (unchanged class name and click-to-navigate behaviour, so existing tests that query `.notification-dropdown__item` keep passing) plus, only when `item.templateCode === 'work_approval_requested'`, either an "Approve"/"Reject" button pair (`data-testid="notification-approve"` / `"notification-reject"`, disabled while `pendingIds` contains the item's id) or, once a decision exists in the `decisions` map, a text label (`data-testid="notification-decision"`, "Approved" / "Rejected" / "Already handled" for the `'error'` state). Full file:

```typescript
import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { NotificationItem } from '../../../../../core/services/notification-api.service';

/** WM approval-request notifications can be decided right from the bell; every other
 * type (including the already-decided/commented approval notifications, which share
 * the same relatedEntityType) stays click-to-navigate only. */
const ACTIONABLE_TEMPLATE_CODE = 'work_approval_requested';

export type NotificationDecisionState = 'approved' | 'rejected' | 'error';

@Component({
  selector: 'app-notification-dropdown',
  standalone: true,
  imports: [DatePipe],
  template: `
    <div class="notification-dropdown" data-testid="notification-dropdown">
      <div class="notification-dropdown__header">
        <span>Notifications</span>
        <button type="button" class="notification-dropdown__mark-all" (click)="markAllRead.emit()">
          Mark all read
        </button>
      </div>
      @if (items().length === 0) {
        <p class="notification-dropdown__empty">You're all caught up.</p>
      } @else {
        <ul class="notification-dropdown__list">
          @for (item of items(); track item.id) {
            <li>
              <div class="notification-dropdown__row">
                <button
                  type="button"
                  class="notification-dropdown__item"
                  [class.notification-dropdown__item--unread]="!item.isRead"
                  (click)="itemClicked.emit(item)"
                >
                  <span class="notification-dropdown__title">{{ item.title }}</span>
                  <span class="notification-dropdown__body">{{ item.body }}</span>
                  <span class="notification-dropdown__time">{{ item.createdAt | date: 'MMM d, h:mm a' }}</span>
                </button>
                @if (isActionable(item)) {
                  @if (decisionState(item.id); as decision) {
                    <span
                      class="notification-dropdown__decision"
                      [class.notification-dropdown__decision--error]="decision === 'error'"
                      data-testid="notification-decision"
                    >
                      {{ decision === 'approved' ? 'Approved' : decision === 'rejected' ? 'Rejected' : 'Already handled' }}
                    </span>
                  } @else {
                    <div class="notification-dropdown__actions">
                      <button
                        type="button"
                        class="notification-dropdown__action notification-dropdown__action--approve"
                        data-testid="notification-approve"
                        [disabled]="isPending(item.id)"
                        (click)="approveClicked.emit(item)"
                      >
                        Approve
                      </button>
                      <button
                        type="button"
                        class="notification-dropdown__action notification-dropdown__action--reject"
                        data-testid="notification-reject"
                        [disabled]="isPending(item.id)"
                        (click)="rejectClicked.emit(item)"
                      >
                        Reject
                      </button>
                    </div>
                  }
                }
              </div>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: [`
    .notification-dropdown {
      width: 320px;
      max-height: 420px;
      overflow: auto;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: 12px;
      box-shadow: 0 8px 24px color-mix(in srgb, var(--color-text-primary) 12%, transparent);
    }
    .notification-dropdown__header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 12px;
      font-size: 13px;
      font-weight: 600;
      color: var(--color-text-primary);
      border-bottom: 1px solid var(--color-border);
    }
    .notification-dropdown__mark-all {
      border: none;
      background: none;
      color: var(--color-text-secondary);
      cursor: pointer;
      font-size: 12px;
    }
    .notification-dropdown__empty {
      margin: 0;
      padding: 16px;
      font-size: 13px;
      color: var(--color-text-secondary);
    }
    .notification-dropdown__list {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .notification-dropdown__row {
      display: flex;
      flex-direction: column;
      width: 100%;
      border-bottom: 1px solid var(--color-border);
    }
    .notification-dropdown__item {
      display: flex;
      flex-direction: column;
      gap: 2px;
      width: 100%;
      text-align: left;
      padding: 10px 12px 4px;
      border: none;
      background: transparent;
      cursor: pointer;
      color: var(--color-text-primary);
      font: inherit;
    }
    .notification-dropdown__item--unread {
      background: var(--color-surface-secondary);
    }
    .notification-dropdown__title {
      font-size: 13px;
      font-weight: 600;
    }
    .notification-dropdown__body,
    .notification-dropdown__time {
      font-size: 12px;
      color: var(--color-text-secondary);
    }
    .notification-dropdown__actions {
      display: flex;
      gap: 8px;
      padding: 0 12px 10px;
    }
    .notification-dropdown__action {
      flex: 1;
      padding: 5px 0;
      border-radius: 6px;
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
    }
    .notification-dropdown__action--approve {
      color: var(--color-success, #1a7f37);
      border-color: var(--color-success, #1a7f37);
    }
    .notification-dropdown__action--reject {
      color: var(--color-danger);
      border-color: var(--color-danger);
    }
    .notification-dropdown__action:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .notification-dropdown__decision {
      padding: 0 12px 10px;
      font-size: 12px;
      font-weight: 600;
      color: var(--color-text-secondary);
    }
    .notification-dropdown__decision--error {
      color: var(--color-danger);
    }
  `]
})
export class NotificationDropdownComponent {
  items = input.required<readonly NotificationItem[]>();
  pendingIds = input<ReadonlySet<string>>(new Set());
  decisions = input<ReadonlyMap<string, NotificationDecisionState>>(new Map());
  itemClicked = output<NotificationItem>();
  approveClicked = output<NotificationItem>();
  rejectClicked = output<NotificationItem>();
  markAllRead = output<void>();

  isActionable(item: NotificationItem): boolean {
    return item.templateCode === ACTIONABLE_TEMPLATE_CODE;
  }

  isPending(id: string): boolean {
    return this.pendingIds().has(id);
  }

  decisionState(id: string): NotificationDecisionState | undefined {
    return this.decisions().get(id);
  }
}
```

- [ ] **Step 2: Target state for `notification-bell.component.ts`**

Add the `WorkApprovalsApiService` dependency, two signals (`pendingDecisionIds`, `decisions`), and `onApprove`/`onReject` handlers that funnel through a shared `decide` helper: guards against a missing `relatedEntityId` or a duplicate click while already pending, calls the API, records the outcome, and marks the notification read on success. Apply these changes on top of the current file:

```typescript
import { Component, OnDestroy, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthStore } from '../../../../core/auth/state/auth.store';
import { PermissionService } from '../../../../core/auth/state/permission.service';
import { NotificationsStore } from '../../../../core/state/notifications.store';
import { TrayPresenceStore } from '../../../../core/tray-presence/state/tray-presence.store';
import { tenantWorkspaceReady } from '../../../../core/tray-presence/utils/tenant-workspace-ready';
import { NotificationItem } from '../../../../core/services/notification-api.service';
import { NotificationNavigationService } from '../../../../core/services/notification-navigation.service';
import { PopoverComponent } from '../../../../shared/ui/popover/popover.component';
import { WorkApprovalsApiService } from '../../../../modules/work/data-access/work-approvals-api.service';
import {
  NotificationDecisionState,
  NotificationDropdownComponent
} from './notification-dropdown/notification-dropdown.component';
```

Template's `popover-panel` block:

```html
      <div popover-panel>
        <app-notification-dropdown
          [items]="store.items()"
          [pendingIds]="pendingDecisionIds()"
          [decisions]="decisions()"
          (itemClicked)="onItemClicked($event)"
          (approveClicked)="onApprove($event)"
          (rejectClicked)="onReject($event)"
          (markAllRead)="onMarkAllRead()"
        />
      </div>
```

Class body additions (new fields alongside the existing `isOpen` signal, and new methods at the end of the class):

```typescript
  private readonly workApprovalsApi = inject(WorkApprovalsApiService);
  protected readonly pendingDecisionIds = signal<ReadonlySet<string>>(new Set());
  protected readonly decisions = signal<ReadonlyMap<string, NotificationDecisionState>>(new Map());
```

```typescript
  async onApprove(item: NotificationItem): Promise<void> {
    await this.decide(item, () => this.workApprovalsApi.approveWorkApproval(item.relatedEntityId!), 'approved');
  }

  async onReject(item: NotificationItem): Promise<void> {
    await this.decide(item, () => this.workApprovalsApi.rejectWorkApproval(item.relatedEntityId!), 'rejected');
  }

  private async decide(
    item: NotificationItem,
    call: () => ReturnType<WorkApprovalsApiService['approveWorkApproval']>,
    successState: NotificationDecisionState
  ): Promise<void> {
    if (!item.relatedEntityId || this.pendingDecisionIds().has(item.id)) return;

    this.pendingDecisionIds.update(ids => new Set(ids).add(item.id));
    try {
      await firstValueFrom(call());
      this.decisions.update(map => new Map(map).set(item.id, successState));
      if (!item.isRead) await this.store.markRead(item.id);
    } catch {
      // Already decided/cancelled by someone else, or no longer the approver - surface it inline.
      this.decisions.update(map => new Map(map).set(item.id, 'error'));
    } finally {
      this.pendingDecisionIds.update(ids => {
        const next = new Set(ids);
        next.delete(item.id);
        return next;
      });
    }
  }
```

- [ ] **Step 3: Write the new tests**

Append to `src/app/layouts/main-layout/top-navbar/notification-bell/notification-bell.component.spec.ts`. Add these imports at the top of the file alongside the existing ones:

```typescript
import { of, throwError } from 'rxjs';
import { WorkApprovalsApiService } from '../../../../modules/work/data-access/work-approvals-api.service';
```

(Note: `of` may already be imported for other suites in this file — check before duplicating; if it's already imported from `'rxjs'`, just add `throwError` and `WorkApprovalsApiService` to the existing import lines.)

Add these five tests inside the existing `describe('NotificationBellComponent', ...)` block:

```typescript
  it('shows Approve/Reject buttons only for an actionable work_approval_requested notification', async () => {
    const actionable = {
      id: 'n5', templateCode: 'work_approval_requested', title: 'Approval needed', body: 'Please review',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-1', isRead: false, readAt: null, createdAt: new Date()
    };
    const decided = {
      id: 'n6', templateCode: 'work_approval_decided', title: 'Approval decided', body: 'It was approved',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-2', isRead: false, readAt: null, createdAt: new Date()
    };
    const storeStub = {
      unreadCount: () => 2, items: () => [actionable, decided],
      refreshUnreadCount: vi.fn().mockResolvedValue(undefined), loadMine: vi.fn().mockResolvedValue(undefined),
      markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
    };
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [
      { provide: NotificationsStore, useValue: storeStub },
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: TaskApiService, useValue: { getNotificationNavigation: vi.fn() } },
      { provide: AuthStore, useValue: { authenticated: () => true, activeModules: () => [] } },
      { provide: WorkApprovalsApiService, useValue: { approveWorkApproval: vi.fn(), rejectWorkApproval: vi.fn() } }
    ] });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="notification-bell"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('[data-testid="notification-approve"]').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('[data-testid="notification-reject"]').length).toBe(1);
  });

  it('approves a work approval request from the bell, marks it read, and does not navigate', async () => {
    const item = {
      id: 'n7', templateCode: 'work_approval_requested', title: 'Approval needed', body: 'Please review',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-7', isRead: false, readAt: null, createdAt: new Date()
    };
    const storeStub = {
      unreadCount: () => 1, items: () => [item],
      refreshUnreadCount: vi.fn().mockResolvedValue(undefined), loadMine: vi.fn().mockResolvedValue(undefined),
      markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
    };
    const approveWorkApproval = vi.fn().mockReturnValue(of({ id: 'req-7', status: 'approved' }));
    const navigate = vi.fn().mockResolvedValue(true);
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [
      { provide: NotificationsStore, useValue: storeStub },
      { provide: Router, useValue: { navigate } },
      { provide: TaskApiService, useValue: { getNotificationNavigation: vi.fn() } },
      { provide: AuthStore, useValue: { authenticated: () => true, activeModules: () => [] } },
      { provide: WorkApprovalsApiService, useValue: { approveWorkApproval, rejectWorkApproval: vi.fn() } }
    ] });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="notification-bell"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.nativeElement.querySelector('[data-testid="notification-approve"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(approveWorkApproval).toHaveBeenCalledWith('req-7');
    expect(storeStub.markRead).toHaveBeenCalledWith('n7');
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="notification-decision"]').textContent.trim()).toBe('Approved');
  });

  it('rejects a work approval request from the bell', async () => {
    const item = {
      id: 'n8', templateCode: 'work_approval_requested', title: 'Approval needed', body: 'Please review',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-8', isRead: false, readAt: null, createdAt: new Date()
    };
    const storeStub = {
      unreadCount: () => 1, items: () => [item],
      refreshUnreadCount: vi.fn().mockResolvedValue(undefined), loadMine: vi.fn().mockResolvedValue(undefined),
      markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
    };
    const rejectWorkApproval = vi.fn().mockReturnValue(of({ id: 'req-8', status: 'rejected' }));
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [
      { provide: NotificationsStore, useValue: storeStub },
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: TaskApiService, useValue: { getNotificationNavigation: vi.fn() } },
      { provide: AuthStore, useValue: { authenticated: () => true, activeModules: () => [] } },
      { provide: WorkApprovalsApiService, useValue: { approveWorkApproval: vi.fn(), rejectWorkApproval } }
    ] });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="notification-bell"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.nativeElement.querySelector('[data-testid="notification-reject"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(rejectWorkApproval).toHaveBeenCalledWith('req-8');
    expect(fixture.nativeElement.querySelector('[data-testid="notification-decision"]').textContent.trim()).toBe('Rejected');
  });

  it('shows "Already handled" when deciding an approval that was already resolved', async () => {
    const item = {
      id: 'n9', templateCode: 'work_approval_requested', title: 'Approval needed', body: 'Please review',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-9', isRead: true, readAt: new Date(), createdAt: new Date()
    };
    const storeStub = {
      unreadCount: () => 0, items: () => [item],
      refreshUnreadCount: vi.fn().mockResolvedValue(undefined), loadMine: vi.fn().mockResolvedValue(undefined),
      markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
    };
    const rejectWorkApproval = vi.fn().mockReturnValue(throwError(() => new Error('409 Conflict')));
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [
      { provide: NotificationsStore, useValue: storeStub },
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: TaskApiService, useValue: { getNotificationNavigation: vi.fn() } },
      { provide: AuthStore, useValue: { authenticated: () => true, activeModules: () => [] } },
      { provide: WorkApprovalsApiService, useValue: { approveWorkApproval: vi.fn(), rejectWorkApproval } }
    ] });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="notification-bell"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.nativeElement.querySelector('[data-testid="notification-reject"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(rejectWorkApproval).toHaveBeenCalledWith('req-9');
    expect(fixture.nativeElement.querySelector('[data-testid="notification-decision"]').textContent.trim()).toBe('Already handled');
  });

  it('disables the Approve/Reject buttons while a decision is in flight so a double-click cannot fire twice', async () => {
    const item = {
      id: 'n10', templateCode: 'work_approval_requested', title: 'Approval needed', body: 'Please review',
      relatedEntityType: 'work_approval_request', relatedEntityId: 'req-10', isRead: false, readAt: null, createdAt: new Date()
    };
    const storeStub = {
      unreadCount: () => 1, items: () => [item],
      refreshUnreadCount: vi.fn().mockResolvedValue(undefined), loadMine: vi.fn().mockResolvedValue(undefined),
      markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
    };
    let resolveApprove!: (value: unknown) => void;
    const pending = new Promise(resolve => { resolveApprove = resolve; });
    const approveWorkApproval = vi.fn().mockReturnValue({
      subscribe: (observer: { next: (v: unknown) => void; complete: () => void }) => {
        pending.then(value => { observer.next(value); observer.complete(); });
        return { unsubscribe: () => {} };
      }
    });
    TestBed.configureTestingModule({ imports: [NotificationBellComponent], providers: [
      { provide: NotificationsStore, useValue: storeStub },
      { provide: Router, useValue: { navigate: vi.fn() } },
      { provide: TaskApiService, useValue: { getNotificationNavigation: vi.fn() } },
      { provide: AuthStore, useValue: { authenticated: () => true, activeModules: () => [] } },
      { provide: WorkApprovalsApiService, useValue: { approveWorkApproval, rejectWorkApproval: vi.fn() } }
    ] });
    const fixture = TestBed.createComponent(NotificationBellComponent);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="notification-bell"]').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const approveBtn: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="notification-approve"]');
    approveBtn.click();
    fixture.detectChanges();
    expect(approveBtn.disabled).toBe(true);
    approveBtn.click();
    resolveApprove({ id: 'req-10', status: 'approved' });
    await fixture.whenStable();

    expect(approveWorkApproval).toHaveBeenCalledTimes(1);
  });
```

- [ ] **Step 4: Run the tests**

Run: `npx ng test --watch=false --include="src/app/layouts/main-layout/top-navbar/notification-bell/**/*.spec.ts"`
Expected: all tests in the file PASS (the 6 pre-existing navigation tests plus the 5 new ones = 11 total; the `notifications.store.spec.ts` tests are unaffected and not required for this run but running them too is harmless).

- [ ] **Step 5: Run the full frontend gate**

Run: `npx ng build --configuration development` then `npx ng test --watch=false`
Expected: build succeeds with no new errors (pre-existing NG8102/NG8107 template-diagnostic warnings on unrelated files are fine); full test suite passes except two already-known-flaky specs that fail only in the full run and pass in isolation — `milestone-tree-tab.component.spec.ts`'s `openMembers=1` deep-link test and `task-form-modal.component.spec.ts`'s attachment-download test (both pre-existing, confirmed unrelated to this change by the investigation that produced this plan). Any other failure must be investigated, not waved through.

- [ ] **Step 6: Commit**

```bash
git add src/app/layouts/main-layout/top-navbar/notification-bell/notification-dropdown/notification-dropdown.component.ts src/app/layouts/main-layout/top-navbar/notification-bell/notification-bell.component.ts src/app/layouts/main-layout/top-navbar/notification-bell/notification-bell.component.spec.ts
git commit -m "$(cat <<'EOF'
feat(wm-notifications): inline Approve/Reject on approval-request bell items

work_approval_requested notifications can now be decided right from the
notification dropdown instead of requiring a trip to the Approvals page.
Reuses WorkApprovalsApiService.approveWorkApproval/rejectWorkApproval
directly against the notification's relatedEntityId (both endpoints take
only the request id, no project scope needed). Buttons are gated to the
work_approval_requested templateCode only - decided/commented
notifications on the same request keep their plain click-to-navigate
behaviour. A failed decide (already resolved by someone else, or no
longer the approver) shows an inline "Already handled" state instead of
erroring silently.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (both repos)

Backend (`HRMS-Backend-v1`):
```bash
dotnet build src/ONEVO.Api -c Release
dotnet test tests/ONEVO.Tests.Unit -c Release
dotnet test tests/ONEVO.Tests.Architecture -c Release
```
Paste the real pass counts. Any failure must either be fixed or proven pre-existing on `origin/development` (checkout clean, re-run, compare) before reporting done.

Frontend (`Hrms--Web-application---front-end---v1`):
```bash
npx ng build --configuration development
npx ng test --watch=false
```
Paste the real pass counts. Only the two named pre-existing flakes are acceptable failures.

**Out of scope, flagged for the user separately, not to be fixed in this plan:**
- `ProjectMonitorJob` (the hourly background service behind `work_monitor_alert`) could not be confirmed as actually running against a real database from static analysis alone — ask the user to verify it live if sprint/module/task overdue alerts seem to not be firing.
- `notification-bell.component.ts`'s polling is gated behind `tenantWorkspaceReady(...)` (auth +, when `activity_monitoring` is on, the tray-presence gate). If a specific user reports the bell never updates at all (not just missing buttons), check whether that gate is unlocking for them before assuming the notification code itself is broken.
- No manual browser pass was done for either task in this plan — flag to the user as the remaining check once both tasks are merged, same as prior WM features.
