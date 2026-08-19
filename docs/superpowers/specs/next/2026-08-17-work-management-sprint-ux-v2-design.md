# Work Management — Task Edit Requests, Board Structure, Directory & Backlog v2 Design

**Status:** Approved — 2026-08-17
**Depends on:** `2026-08-17-work-management-sprint-foundation-design.md` (Sprint Foundation, shipped) —
this spec extends the Objective Settings page, the task-detail popup, and the Backlog tab that
feature built.

## Summary

Four additions to the shipped Sprint Foundation UI, driven by screenshots reviewed during
brainstorming:

1. **Task Edit Requests** — the task-detail popup becomes always-editable: the Objective owner's
   edits save directly (reusing the existing `EditTaskCommand`); a non-owner member's edits go
   through a new approval request the owner must approve, mirroring `TaskCreationRequest` exactly.
2. **Board Structure tab** — a second tab inside Objective Settings (alongside the existing "Task
   statuses" table) where the owner drag-reorders statuses, toggles Public/Private per status via
   checkbox, and picks exactly one status as "marks complete" via radio button — saved as one atomic
   bulk update, not N individual edits.
3. **Employee directory (name + avatar) everywhere an ID currently leaks** — Work Management's own
   member list, task assignee pickers, and any other place showing a raw employee ID.
4. **Backlog v2** — replaces the Part 4 sidebar-list-plus-separate-table layout with expandable
   sprint tabs (click a sprint → it expands inline to show its tasks; a `+` in the corner of the
   expanded sprint opens task-create scoped to it).

Objective Calendar gets its sub-menu tab and route added as a stub in this pass — no content yet,
deferred to a later pass per the brainstorming decision.

**Hard visual constraint carried through every frontend task in this spec:** nothing above or around
the existing sub-menu tab bar changes, and only existing theme tokens/colors are used — no new colors
introduced.

## 1. Task Edit Requests

New entity `TaskEditRequest`, deliberately mirroring `TaskCreationRequest`'s exact shape and command
set (`Create/Approve/Reject/Cancel`, `GetMyTaskEditRequests`), since that pattern is already proven
and understood in this codebase:

```
TaskEditRequest : BaseEntity
  TaskId: Guid
  RequestedByEmployeeId: Guid
  PayloadJson: string          -- serialized TaskEditRequestPayload(Title, Description, Priority, DueDate, EstimatedHours, StoryPoints)
  Status: string                -- pending / approved / rejected / cancelled
  DecidedByEmployeeId: Guid?
  DecisionComment: string?
  DecidedAt: DateTimeOffset?
```

Payload fields match `EditTaskCommand`'s existing editable set exactly — **not** `TaskType`, which
`EditTaskCommand` doesn't allow changing either (only `CreateTask` sets it, at creation).

**Routing:** to the *task's Objective's* owner (resolved via `task.ObjectiveId → Objective.OwnerId`),
same as `TaskCreationRequest`. **Authorization on create:** requester must be an active member of the
task's Objective and must **not** be the owner (owners edit directly, no request needed — same rule
`CreateTaskCreationRequestCommandHandler` already enforces for creation, reused verbatim for edits).

**On approve:** apply the payload's fields to the existing `WorkTask` the same way
`EditTaskCommandHandler` does (including its existing allocation-slack re-check when `EstimatedHours`
changes — reuse `IObjectiveAllocationSlackCalculator`, don't re-implement it). **Learn from the
`requestedByName` gap found earlier in this project:** the list/response DTOs for this new request
type must carry the requester's resolved display name from day one — do not repeat the mistake of
shipping ID-only and fixing it later.

**Frozen-sprint interaction:** if the task's Sprint is `Achieved`, creating an edit request must be
blocked the same way direct edits already are (`EditTaskCommandHandler`'s existing freeze check,
reused) — an edit *request* on a frozen task makes no more sense than a direct edit on one.

## 2. Board Structure tab (Objective Settings)

Objective Settings gains an internal tab strip: **Task statuses** (existing table, unchanged) and
**Board structure** (new). Board structure shows the same statuses in their current order with an
**Edit** button; clicking it opens a form with:

- **Drag-and-drop reordering** of all statuses for that Objective.
- **Checkbox per status**: Public/Private (independent per row — several can be Private at once, no
  mutual exclusion).
- **Radio button, one per status group**: which single status "marks complete." **Hard rule, enforced
  server-side, not just client-side**: exactly one status must have `MarksTaskComplete = true` after
  the save — reject the whole bulk update (all-or-nothing) if zero or more than one row has it set.
  Rationale: Sprint completion (`CompleteSprintCommandHandler`) depends on every task being in *a*
  complete-flagged status; allowing zero would make sprint completion permanently impossible, and
  allowing several is unnecessary ambiguity the radio button already rules out in the UI.
- **"Add status"** stays wired to the already-shipped `CreateTaskStatusCommand` (called immediately
  when clicked, joining the draggable list) — not folded into the bulk save; no reason to change what
  already works.

**New backend command**: `ReorderTaskStatusesCommand(ObjectiveId, List<TaskStatusOrderUpdate{StatusId, DisplayOrder, Visibility, MarksTaskComplete}>)`,
owner-only, one transaction, updates every listed status's three fields atomically. Reuses `Delete`'s
existing not-in-use check? No — reordering/toggling doesn't delete anything, so no such check applies
here; only the exactly-one-complete-status rule above.

## 3. Employee directory (name + avatar)

**New shared frontend service**, `src/app/modules/work/data-access/employee-directory.service.ts`,
wrapping whatever People-module endpoint currently returns `FullName` (and, once confirmed against
the actual current branch, an avatar field) for a set of employee IDs — **with an in-memory cache
keyed by employee ID**, so repeated lookups across different components (assignee picker, member
list, task-detail popup) don't refetch. Every consuming component asks this one service, never calls
a raw "get all employees" endpoint directly.

**Fallback contract**: if the avatar field is null/absent for an employee, render initials (from
`FirstName`/`LastName` — reuse whatever initials-building logic already exists elsewhere in this
codebase, e.g. `PositionMapper.BuildInitials` on the backend as a reference for the *algorithm*, not
something the frontend calls) inside a colored circle, using existing theme tokens only. Never show a
raw GUID anywhere in the UI as a fallback.

**Places this replaces raw-ID display**: Work Management's own `ObjectiveMemberListDto`-driven member
list/UI, the task-create modal's assignee dropdown (already flagged as using a raw
"get all employees" call in Part 4 — replace that call with this service), and the task-detail
popup's assignee display.

## 4. Backlog v2

Replaces Part 4's `SprintListComponent` (sidebar list) + separately-filtered `TaskTableComponent`
combination entirely. New layout, driven by the reviewed screenshot:

- Page shows only a **Create Sprint** button plus a list of sprint tabs (owner sees all statuses,
  member sees Active-only — same visibility rule as before, unchanged).
- Each sprint tab: an **Expand** control (anyone can use — toggles whether that sprint's task list
  shows inline below it), an **Edit** button (owner-only — opens the existing `EditSprintCommand` form),
  and the sprint's status shown via a **dropdown that only the owner can open/change** (members see
  the status as plain text, not an interactive control).
- Expanding a sprint tab reveals its task list inline (reuse `TaskTableComponent` or a trimmed variant
  of it, filtered to that sprint's tasks) with a **View** action per task opening the (now
  always-editable, per §1) task-detail popup.
- A **`+` icon in the corner of an expanded sprint** opens the same task-create modal Part 3 built,
  but with `sprintId` **pre-filled and the dropdown hidden** — the user is already inside that
  specific sprint's expanded view, so re-picking a sprint would be confusing. **The Board tab's own
  "Create task" button keeps the visible sprint dropdown as-is** — Board can show tasks from several
  Active sprints at once, so there's no single implied sprint context there the way there is inside
  an expanded Backlog sprint row.

This directly fixes the minor UX gap flagged after Part 4's E2E check (sprint dropdown not refreshing
after create) — Backlog v2's data flow reloads sprint state from one place, not two separate stores
racing each other.

## Objective Calendar (stub only, this pass)

New sub-menu tab **Calendar**, new route `milestones/:milestoneId/calendar`, pointing at a minimal
placeholder component (same stubbing pattern already used for "My Task"/"List" via
`MilestoneComingSoonTabComponent` before those were built) — no real calendar content, no new backend
endpoints, in this spec. A full design for its content is a separate future spec, per the earlier
brainstorming decision to keep Calendar scoped out of Sprint Foundation.

## Explicitly out of scope

- Real Calendar content (deferred, stub tab only).
- Any change to `ObjectiveParentConstraintChecker` or other already-settled business rules from the
  earlier Sprint Foundation spec.
- A People-module backend change to add a real avatar-URL field — this spec consumes whatever
  currently exists (confirmed at implementation time against the actual branch), falling back to
  initials; if the field truly doesn't exist anywhere reachable, initials-only is the acceptable
  outcome for this pass, not a blocker.
