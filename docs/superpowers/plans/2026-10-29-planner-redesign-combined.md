# Planner Redesign (Mockups A + B + C Combined) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Planner page so it matches one design that merges mockups A and B.
- **Task queue:** search, filter chips, grouped sections and redesigned cards.
- **Project backlog:** a new section with "Take & plan".
- **Day headers:** today highlighted, "Over" in red.
- **Calendar blocks:** meeting blocks with attendee avatars, task blocks with project names.
- **Part 2:** a fixed **type** per category (Task, Bug, Story, Feature, Testing, …) that colours and icons the blocks, splits the day load bar, and lets the Task queue group and filter by type.

**Architecture:**
- **Backend:** four additive changes behind the existing `Planner:Enabled` flag:
  1. attendees on meeting calendar items;
  2. a project name on planner blocks;
  3. a "takeable backlog" list;
  4. a "take & plan" write.
- **Frontend:** reshapes the existing Planner components in place:
  - `calendar-page` — toolbar;
  - `calendar-hour-grid` — headers and blocks;
  - `planner-focus-panel` — Task queue.

  It adds no new routes.

**Tech Stack:**
- **Backend:** .NET 10, EF Core, MediatR, xUnit.
- **Frontend:** Angular 21 standalone + signals, NgRx SignalStore 21, Tailwind, Vitest 4 via `ng test`.

**Spec:**
- `docs/ui-redesign/mockups/planner-mockup-a.webp` and `planner-mockup-b.webp`
- `docs/ui-redesign/mockups/planner-mockup-c-types.webp` — the category-type view. **Where C conflicts with A or B, C wins**; see Part 2.
- `docs/ui-redesign/planner-calendar-meetings-ui-inventory.md` (current behaviour)
- `docs/ui-redesign/planner-calendar-meetings-ui-inventory-addendum-1.md` (backlog gap)
- §"Combined design" below. The mockups don't specify how they merge, so that section is the decision record.

**Repos:**
- Backend `HRMS-Backend-v1`, branch off `development`.
- Frontend `Hrms--Web-application---front-end---v1`, branch off `feature/meeting-from-work-item-form` (PR #134), because it builds on the Task queue changes there.

---

> **Deferred (user, 2026-10-29): Project backlog is out of scope for now.** Skip the following and leave the backlog out of everything else:
> - **D14** and **D15**;
> - **Task 3** (backend backlog and take endpoints);
> - **Task 9**;
> - the backlog parts of **Task 4**: the `backlog` state, `getBacklog`, `takeAndPlan`, and the drag `source`;
> - **Review Focus 1–2**.
>
> Task 10 drops its Project backlog checks.

> **Mockup C (Part 2, at the end) supersedes part of Part 1:**
> - **D5:** the single-colour load bar → **D17** (a segmented bar by type).
> - **D7:** teal for every task block → **D16** (colour and icon by type).
> - **D10** and **D12:** due-date chips → **D19** (type chips; due date is a Group-by option).
> - **D13:** the dot → **D20** (type icon tile).
>
> **Order of work:** Tasks 1–2, 4–8, then Part 2's Tasks 11–16, then **Task 10 last**.
>
> **Task 8 / Task 16:** Task 8 builds the queue skeleton (header, search, sections, cards). Task 16 then swaps the chips and the card's left side to the type versions. An implementer doing both in one pass should build the Part 2 versions directly and skip Task 8's chip and dot tests.

## Combined design (decisions — review these first)

| # | Area | A shows | B shows | **Decision** |
|---|---|---|---|---|
| D1 | Page title | "Planner" | "Planner" | Keep the current `<h1>`. No summary line (the user rejected it). |
| D2 | Period label | "Week of 2026-10-26 ▾" | same | The label becomes a button. It opens `app-date-picker`; picking a date moves `cursor` there. |
| D3 | New event | Split button ▾ | Split button ▾ | **Plain primary "+ New event", no ▾.** The only possible menu item would be "Meeting", which the user removed from the Planner. Change this if a menu item is wanted. |
| D4 | Task queue toggle | Pill "Task queue · 10" with an accent count badge | same | Restyle the existing toggle: outline pill, list icon, count in an accent badge. |
| D5 | Day header | "Mon / 26 Oct", "3h 15m / 8h", bar | same | Two lines (weekday, then `d MMM`), the load text, and the bar. **Today:** accent weekday text, date in a filled accent circle, the **whole column tinted** `color-mix(accent 6%)`. **Over:** text and bar **red** (`--color-danger`), label "`12h / 8h · Over`". Under the limit: teal as today. There is no orange state (A's orange Friday is not explained). |
| D6 | Meeting block | Solid blue: time line, title, subtitle | + attendee avatars (max 3 + "+N") | Solid accent block: top line "`2:00 – 3:00`" and `⋯`, title, subtitle = location (omitted when null), avatar row from `participants`. **`⋯` opens the meeting drawer**, the same as a click. |
| D7 | Task block | Teal outline: time line, title, project | Teal outline with a left accent bar | B's left 3px teal bar + A's content: top line "`2:30 – 4:30`" and `⋯`, title (semibold), subtitle = **project name**. The menu items are unchanged. |
| D8 | Task queue header | "Task queue (10)", ✕ | same | Title + count badge + ✕ (✕ is hidden when not closable, as today). |
| D9 | Search | Search box + `⋯` | Search box + `⋯` | Search box only. It filters loaded cards by title or project name, case-insensitive. **No `⋯`** (no defined menu). |
| D10 | Filter chips | All · Overdue · Due today · This week | same | **All · Overdue · Due today · This week · Backlog**, each with its count. "Backlog" = the existing Assigned backlog group, so every group is reachable. This replaces B's bottom summary box, which is **not built**. |
| D11 | "All" view | Sections with dot, count, "See all", 3 cards each; then Project backlog | One flat list | **A's sectioned view**: one section per non-empty group (Overdue red, Due today orange, This week grey, Backlog grey), **3 cards each**, "See all" selects that group's chip. Then the **Project backlog** section (purple). |
| D12 | Single-group chip | — | Flat list, "+ N more tasks" | That group's cards, **5** then "+ N more tasks" (expands, as today). |
| D13 | Queue card | Dot, title, project, due line; calendar icon + `⋯` on the right | Dot, title, project, 📅 due · left · **Planned Thu**; ⋮⋮ on the right | B's card: the dot coloured by group; a due line with a calendar icon; "Planned Thu" in teal. Right side: **📅 Plan button** (always visible) and a **⋮⋮ handle** below it. The whole card still drags (4px threshold) and a click still opens the task (current behaviour). No `⋯`. |
| D14 | Project backlog | Rows: title, project, "No due date · 3h", **Take & plan**, `⋯`; "+ 2 more tasks" | — | Rows as in A, without `⋯`. **Take & plan** opens the day-and-time picker; submit = take + plan. A backlog row can also be **dragged onto the grid** = take + plan at the drop slot. 3 rows, then "+ N more tasks". |
| D15 | Who can take | — | — | Tasks the user may assign to themselves today: unassigned, not done, not a meeting, not an employee checklist task, not a subtask, in a module where the user is an **effective manager** (owner or active member of the module or an ancestor; `IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`). No approval step. |

---

## Global Constraints

- Everything new is behind `Planner:Enabled`. With it off, backend endpoints answer `503 planner_disabled` and the UI shows none of it (as today).
- Times show in the user's planner time zone (`store.myTimezone()` / the planner zone), as today.
- Plannable days stay **today through +28 days**. Past days are read-only.
- **Copy (exact):**
  - "Task queue", "All", "Overdue", "Due today", "This week", "Backlog", "Project backlog", "See all", "+ N more tasks", "Take & plan", "Search tasks...", "No due date", "Over".
- Existing `data-testid`s used by current specs stay unless a task says it renames them.
- Lint: `npx eslint <changed files>` must be clean. `calendar-page.component.html` keeps its 4 pre-existing touch-picker errors; add none.

## Review Focus

1. **Take & plan on a task someone else assigned meanwhile** → `409 task_already_assigned`, and no block is created. *(Test in Task 3.)*
2. **Take & plan where the slot overlaps an existing block** → `409` (the same code `CreateBlockAsync` uses), and **the task stays unassigned.** *(Test in Task 3.)*
3. **A search that matches nothing in the current chip** → an empty state "No tasks match", not a blank panel. *(Test in Task 8.)*
4. **A meeting with 0 participants or 1 participant** → no avatar row, or exactly one avatar, and no "+0". *(Test in Task 7.)*
5. **The Planner viewed on a week that does not include today** → no column is tinted. *(Test in Task 6.)*

---

## File structure

**Backend**

| File | Change |
|---|---|
| `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingCalendarProjection.cs` | Fill `Participants` on meeting items |
| `src/ONEVO.Application/Features/WorkManagement/Planner/Contracts/PlannerContracts.cs` | `PlannerRangeBlockView.ProjectName`; new `PlannerBacklogItem`, `PlannerBacklogResponse`, `TakeAndPlanRequest`; error code `task_already_assigned` |
| `src/ONEVO.Application/Features/WorkManagement/Planner/RepositoryInterfaces/IPlannerRepository.cs` + `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/Planner/EfPlannerRepository.cs` | Project name on range blocks; `ListTakeableBacklogAsync` |
| `src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerService.cs` | `GetBacklogAsync`, `TakeAndPlanAsync` |
| `src/ONEVO.Api/Controllers/Tenant/WorkManagement/PlannerController.cs` | `GET me/backlog`, `POST me/take` |
| `docs/api/planner-v1.md` | Document both endpoints and the new fields |

**Frontend** (`src/app/modules/`)

| File | Change |
|---|---|
| `work/planner/models/planner.models.ts`, `work/planner/data-access/planner-api.service.ts` | New types and calls |
| `calendar/state/planner-layer.store.ts` | `backlog` state, `takeAndPlan` |
| `calendar/state/planner-drag.service.ts` | `source: 'queue' \| 'backlog'` on the dragged task |
| `calendar/feature/calendar-page/*` | Toolbar (D2–D4), wiring take & plan |
| `calendar/ui/calendar-hour-grid/*` | Day header (D5), meeting block (D6), task block (D7), drop source |
| `calendar/ui/planner-focus-panel/*` | Header, search, chips, sections, cards, project backlog (D8–D14) |
| `calendar/ui/planner-avatar-stack/planner-avatar-stack.component.ts` (new) | Avatars (max 3 + "+N") for meeting blocks |

---

## Task 1 (backend): attendees on meeting calendar items

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Meetings/Services/MeetingCalendarProjection.cs`, `docs/api/meetings-v1.md`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Meetings/MeetingCalendarIntegrationTests.cs`

**Interfaces:**
- Consumes: `IMeetingRepository.ListParticipantsForSeriesAsync(tenantId, seriesIds, ct)` (existing) and the projection's existing employee lookup.
- Produces: meeting `CalendarEventItem.Participants` = series participants (not removed) as `CalendarEventParticipantSummary(EmployeeId, EmployeeName, ResponseStatus)`, **organizer first**, then by name. Occurrence-only overrides are **not** applied (series level only). Note this in `docs/api/meetings-v1.md` under the calendar feed.

- [ ] **Step 1:** Write the failing test `TheFeed_ListsAMeetingsSeriesAttendees_OrganizerFirst`. Seed a series with an organizer and two attendees, read the feed as an attendee, and assert:
  ```csharp
  item.Participants!.Select(p => p.EmployeeId)
    == [organizer, attendeeA, attendeeB]
  ```
  Assert also that a removed participant is absent.
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -c Release --filter "FullyQualifiedName~MeetingCalendarIntegrationTests"`. Expected: FAIL (`Participants` is null). *Needs Docker; the C: drive needs ≥ 15 GB free (see the memory note on disk pressure).*
- [ ] **Step 3:** Implement it. Load the participants for all series in **one** call before building items. Pass `Participants:` in `Item(...)`.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Also run `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~Meeting"`. Expected: all pass.
- [ ] **Step 5:** Commit `feat(meetings): calendar feed meeting items carry their attendees`.

## Task 2 (backend): project name on planner blocks

**Files:**
- Modify: `PlannerContracts.cs:53` (`PlannerRangeBlockView` gets a `string ProjectName` after `ProjectId`), and the repository/service code that builds it.
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerRangeIntegrationTests.cs`

**Interfaces:**
- Produces: `PlannerRangeBlockView(Guid Id, Guid TaskId, Guid ProjectId, string ProjectName, string TaskTitle, DateTimeOffset StartAt, DateTimeOffset EndAt, int PlannedMinutes, int ActualMinutes, bool StillAssigned)`.

- [ ] **Step 1:** Write the failing test `TheRange_NamesEachBlocksProject`. Assert `block.ProjectName == "Portal"` for a seeded block.
- [ ] **Step 2:** Run it with `--filter "FullyQualifiedName~PlannerRangeIntegrationTests"`. Expected: FAIL (compile error until the field exists).
- [ ] **Step 3:** Add the field, joining `Projects` where blocks are read.
- [ ] **Step 4:** Rerun Step 2. Then run the unit tests `--filter "FullyQualifiedName~Planner"`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): range blocks name their project`.

## Task 3 (backend): takeable backlog and take & plan

**Files:**
- Modify: `PlannerContracts.cs`, `IPlannerRepository.cs`, `EfPlannerRepository.cs`, `PlannerService.cs`, `PlannerController.cs`, `docs/api/planner-v1.md`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerServiceIntegrationTests.cs`

**Interfaces:**
- Produces (contracts):
  ```csharp
  public sealed record PlannerBacklogItem(Guid TaskId, Guid ProjectId, string ProjectName, Guid ObjectiveId, string ModuleTitle,
      string Title, string Priority, DateOnly? DueDate, int? EstimatedMinutes);
  public sealed record PlannerBacklogResponse(int Count, IReadOnlyList<PlannerBacklogItem> Items);
  public sealed record TakeAndPlanRequest(Guid TaskId, DateTimeOffset StartAt, DateTimeOffset EndAt);
  // PlannerErrorCodes.TaskAlreadyAssigned = "task_already_assigned" (409)
  ```
- Produces (repository): `Task<IReadOnlyList<PlannerBacklogItem>> ListTakeableBacklogAsync(Guid tenantId, Guid employeeId, CancellationToken ct)`.
  - **Rule:** D15. Find the objectives the employee owns or is an active project member of. Expand each to its descendants via `ProjectModuleTree`. Return the tasks in those objectives where:
    - there is no `TaskAssignment` row;
    - the task is not deleted;
    - progress < 100 and the status does not mark it complete;
    - it is not a meeting (`task_kind`) and not `employee_checklist`;
    - `ParentTaskId == null`.
- Produces (service):
  - `Task<Result<PlannerBacklogResponse>> GetBacklogAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)` — **order:** priority (critical→low), due date (nulls last), title; **take 50**; `Count` = the total.
  - `Task<Result<PlannerDayResponse>> TakeAndPlanAsync(Guid tenantId, Guid callerEmployeeId, TakeAndPlanRequest request, CancellationToken ct)`, in this order:
    1. if the flag is off → 503;
    2. if the task is assigned to anyone → 409 `task_already_assigned`;
    3. the same window/overlap check as `CreateBlockAsync` (`CheckWriteAsync`) → its failure, **before** assigning;
    4. `ISender.Send(new AssignTaskCommand(taskId, caller))` → its failure as-is (403 for non-managers);
    5. `CreateBlockAsync(...)`.
- Produces (API): `GET api/v1/planner/me/backlog` → `PlannerBacklogResponse`; `POST api/v1/planner/me/take` (body `TakeAndPlanRequest`) → `PlannerDayResponse`.

- [ ] **Step 1:** Write the failing tests:
  - `Backlog_ListsUnassignedTopLevelTasksInModulesICanAssign`: a member of module M sees M's and child-module's unassigned task; does **not** see an assigned task, a subtask, a done task, a meeting, or a task in a module they are not part of.
  - `TakeAndPlan_AssignsAndPlans`: the response day has the block and `TaskAssignments` has (task, caller).
  - `TakeAndPlan_RefusesATaskAlreadyAssigned_409_NoBlock`
  - `TakeAndPlan_RefusesAnOverlappingSlot_TaskStaysUnassigned` *(Review Focus 2)*
  - `TakeAndPlan_RefusesANonMember_403`
- [ ] **Step 2:** Run them with `--filter "FullyQualifiedName~PlannerServiceIntegrationTests"`. Expected: FAIL (the members don't exist).
- [ ] **Step 3:** Implement the repository, service and controller per Interfaces. Update `docs/api/planner-v1.md`: the two endpoints, the error code, and the `ProjectName` from Task 2.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Then `pwsh ../dev-env/scripts/verify.ps1 backend`. Expected: build, unit and architecture tests green.
- [ ] **Step 5:** Commit `feat(planner): takeable backlog and take & plan`.

## Task 4 (frontend): models, API, store, drag source

**Files:**
- Modify: `work/planner/models/planner.models.ts`, `work/planner/data-access/planner-api.service.ts`, `calendar/state/planner-layer.store.ts`, `calendar/state/planner-drag.service.ts`
- Test: `calendar/state/planner-layer.store.spec.ts` (create if absent), `work/planner/data-access/planner-api.service.spec.ts`

**Interfaces:**
- Produces:
  - **Types:** TS mirrors of `PlannerBacklogItem`, `PlannerBacklogResponse`, `TakeAndPlanRequest`. `PlannerRangeBlockView.projectName: string`.
  - **API:** `getBacklog(): Observable<PlannerBacklogResponse>` (`GET planner/me/backlog`); `takeAndPlan(r: TakeAndPlanRequest): Observable<PlannerDayResponse>` (`POST planner/me/take`).
  - **Store:**
    - state `backlog: PlannerBacklogResponse | null`, loaded in `load()` alongside `getFocus()`. A failed backlog call leaves `backlog: null` and **does not** deactivate the planner.
    - method `takeAndPlan(taskId: string, startAt: string, endAt: string)` via the existing `write(...)`, which refetches range, focus and backlog.
  - **Drag:** `PlannerDraggedTask.source: 'queue' | 'backlog'`; `start(item, source = 'queue')`.

- [ ] **Step 1:** Write the failing tests:
  - api: `getBacklog` hits `/planner/me/backlog`; `takeAndPlan` posts the body.
  - store: `load()` sets `backlog`; a backlog 500 keeps `active: true`; `takeAndPlan` calls the API, then reloads.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/state/**/*.spec.ts" --include "src/app/modules/work/planner/**/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement per Interfaces.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): backlog and take & plan in the planner store`.

## Task 5 (frontend): toolbar — period picker, Task queue pill, New event (D2–D4)

**Files:**
- Modify: `calendar/feature/calendar-page/calendar-page.component.{html,ts}`
- Test: `calendar/feature/calendar-page/calendar-page.planner.spec.ts`

**Interfaces:**
- Produces:
  - `periodPickerOpen = signal(false)`; `jumpTo(date: string): void` sets `cursor` to that local date and closes the picker.
  - testids: `planner-period-button`, `planner-period-picker`.

- [ ] **Step 1:** Write the failing tests:
  - `the period label opens a date picker and picking a date moves the week` — click `planner-period-button`, then `jumpTo('2026-11-12')`, then expect `viewLabel()` toBe `'Week of 2026-11-09'`.
  - `the Task queue toggle shows its count in a badge` — `planner-focus-toggle` contains an element `[data-testid="planner-focus-count"]` with text `'3'`.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/feature/calendar-page/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement:
  - wrap the label in a button with a chevron; show `app-date-picker` (`[value]="dayDate()"`, `(valueChange)="jumpTo($event)"`) in a popover under it;
  - restyle the toggle as D4;
  - "New event" becomes a primary button with a "+" icon (D3, no menu).
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Update the existing `'Task queue · 3'` assertion to the new markup if needed.
- [ ] **Step 5:** Commit `feat(planner): period picker and toolbar restyle`.

## Task 6 (frontend): day header, today column, Over in red (D5)

**Files:**
- Modify: `calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{html,ts}`
- Test: `calendar/ui/calendar-hour-grid/calendar-hour-grid.planner.spec.ts`

**Interfaces:**
- Produces: on each column VM, `isToday: boolean`, `weekday: string` (e.g. `'Mon'`), `dayLabel: string` (e.g. `'26 Oct'`). The load label for over = `` `${planned} / ${working} · Over` ``. testids `calendar-hour-day-column[data-today]`, `planner-load-label`.

- [ ] **Step 1:** Write the failing tests:
  - `today's column is tinted and its header is accented` — `[data-today="true"]` exists only for TODAY.
  - `no column is tinted on a week without today` *(Review Focus 5)*.
  - `an over-full day reads "· Over" in red` — label `'12h / 8h · Over'`, and the class contains `text-[var(--color-danger)]`.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/ui/calendar-hour-grid/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D5. Today = date equal to today in the planner time zone. Replace `amber` with `--color-danger` for over (label and bar). The tint goes on the column background, under the blocks.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Fix the existing `· over` assertions to `· Over`.
- [ ] **Step 5:** Commit `feat(planner): day headers, today column and red Over`.

## Task 7 (frontend): meeting and task blocks (D6, D7)

**Files:**
- Create: `calendar/ui/planner-avatar-stack/planner-avatar-stack.component.ts` (+ `.spec.ts`)
- Modify: `calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{html,ts}`
- Test: `calendar/ui/calendar-hour-grid/calendar-hour-grid.planner.spec.ts`

**Interfaces:**
- Produces: `PlannerAvatarStackComponent`, selector `app-planner-avatar-stack`:
  - inputs `employeeIds: readonly string[]`, `max = 3`;
  - renders `app-employee-avatar` for the first `max` ids, then `+N` (testid `avatar-stack-more`) only when N > 0.
- Meeting block markup (testid `calendar-meeting-block`):
  - time line `` `${start} – ${end}` `` (testid `block-time`);
  - `⋯` (testid `meeting-block-menu`) → emits `eventClick(id)`;
  - title, location subtitle;
  - `<app-planner-avatar-stack [employeeIds]="participants ids">` only when there are ≥ 1 participants.
- Task block: a left 3px bar (testid `planner-block-accent`); the top line is time + `⋯` (existing menu); title; subtitle `block.projectName` (testid `planner-block-project`).

- [ ] **Step 1:** Write the failing tests:
  - avatar stack: 5 ids → 3 avatars + `'+2'`; 1 id → 1 avatar, no `avatar-stack-more`; 0 ids → nothing *(Review Focus 4)*.
  - grid: a meeting event with 2 participants renders `calendar-meeting-block` with 2 avatars and no "+"; a task block shows `'Portal'` in `planner-block-project`.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/ui/**/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D6/D7. Keep the existing block testids and menu items.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): meeting blocks with attendees, task blocks with project`.

## Task 8 (frontend): Task queue panel — header, search, chips, sections, cards (D8–D13)

**Files:**
- Modify: `calendar/ui/planner-focus-panel/planner-focus-panel.component.{html,ts,spec.ts}`

**Interfaces:**
- Produces (component state):
  - `query = signal('')`;
  - `filter = signal<'all' | PlannerFocusReason>('all')`;
  - `visibleGroups` computed:
    - `all` → each non-empty group with its items cut to **3** (`more = matches − 3`);
    - one reason → that group with **5** then "+ N more tasks" (expands).
  - Search is applied before cutting: a case-insensitive match on `title` or `projectName`.
- Chip testids: `queue-chip-all|overdue|due_today|due_this_week|backlog`, each showing its group count (`all` = the total).
- Section testids: `queue-section-<reason>`, with "See all" (`queue-see-all-<reason>`) → `filter.set(reason)`.
- Empty search result: `queue-no-match` with the text `No tasks match`.
- Card:
  - dot class by reason: overdue `bg-[var(--color-danger)]`, due_today `bg-orange-500`, else `bg-[var(--color-text-secondary)]`;
  - a due line with a calendar icon;
  - "Planned Thu" in teal;
  - right side: `planner-focus-plan` (always visible now) and `planner-focus-grip` (moved to the right).
  - The existing drag/click/keyboard behaviour is unchanged.

- [ ] **Step 1:** Write the failing tests:
  - `All shows one section per non-empty group, 3 cards each, and See all switches the chip`
  - `a chip lists only that group, 5 then + N more tasks`
  - `search filters by title or project across sections`
  - `a search with no match says "No tasks match"` *(Review Focus 3)*
  - `the dot colour follows the group`
  - `the Plan button is visible without hover` — no `opacity-0` class.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/ui/planner-focus-panel/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D8–D13. Keep the drag threshold, ghost, `justDragged` and narrow-screen tap.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Update the old `+ 2 more` and `hint` assertions to the new copy.
- [ ] **Step 5:** Commit `feat(planner): Task queue with search, chips and sections`.

## Task 9 (frontend): Project backlog section and take & plan (D14)

**Files:**
- Modify: `planner-focus-panel.component.{html,ts,spec.ts}`, `calendar-hour-grid.component.ts` (drop source), `calendar-page.component.{html,ts}` + `calendar-page.planner.spec.ts`

**Interfaces:**
- Consumes: Task 4 (`store.backlog()`, `store.takeAndPlan`, `drag.start(item, 'backlog')`).
- Produces:
  - **Panel:**
    - input `backlog: PlannerBacklogResponse | null`;
    - output `take = output<PlannerBacklogItem>()` (Take & plan button, testid `backlog-take`);
    - section `queue-section-project-backlog`, shown in **All** only and only when `backlog.count > 0`;
    - 3 rows then "+ N more tasks";
    - rows drag with `source: 'backlog'`.
  - **Grid:** `blockCreate` payload gains `source: 'queue' | 'backlog'` (from `drag.dragging().source`).
  - **Page:**
    - `onBlockCreate` → `source === 'backlog' ? store.takeAndPlan(...) : store.createBlock(...)`;
    - `pickerItem` accepts a `PlannerBacklogItem`;
    - `onPickerSubmitted` routes a backlog item to `takeAndPlan`.
  - **On a 409 or 403 from take:** show the server message through `NotificationService.error` and refetch (the store's `write` already refetches).

- [ ] **Step 1:** Write the failing tests:
  - panel: `Project backlog shows 3 rows and Take & plan emits take`; `hidden on a single-group chip`; `dragging a backlog row starts a drag with source backlog`.
  - page: `dropping a backlog row calls takeAndPlan`; `the picker submitted for a backlog item calls takeAndPlan`; `a 409 shows the server message`.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/**/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement per Interfaces. A backlog row shows: title, `projectName`, `No due date` or `Due d MMM`, and an estimate (e.g. `3h`) when present.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Then run `npx ng test --watch=false --include "src/app/modules/**/*.spec.ts"` (full modules), `npx tsc -p tsconfig.app.json --noEmit`, and `npx eslint` on the changed files. Expected: all green (only the 4 known lint errors in `calendar-page.component.html`).
- [ ] **Step 5:** Commit `feat(planner): Project backlog with take & plan`.

## Task 10: check it in the running app

- [ ] **Step 1:** Run the API from the backend branch and `npm start` for the frontend.
  - Set `Planner__Enabled=true` and `Meetings__Create=true` in `.env`.
  - Open `https://<tenant>.onexso.com:4200/planner`.
- [ ] **Step 2:** Walk through each item and confirm it:
  - **Week view:** today's column is tinted, an over day shows a red "· Over", meeting blocks show avatars, task blocks show the project.
  - **Task queue:** each chip and its counts; search, including the no-match state; "See all".
  - **Drag:** a queue card onto the grid creates a block; clicking a card opens the task.
  - **Project backlog:** Take & plan through the picker; dragging a backlog row assigns and plans it; the task then appears under its group.
  - **Period picker:** jumping to another week.
  - **Narrow window (≤ 767px):** a tap opens the picker.
- [ ] **Step 3:** Open PRs (backend → `development`; frontend → stacked on #134), listing D3 and D15 as decisions for the reviewer.

---

# Part 2 — Category types (mockup C)

**Goal:** give every work item category a fixed **type** with its own colour and icon, so the Planner can change by type:
- coloured, iconed blocks;
- a day load bar split by type;
- a Task queue that groups and filters by type.

**Why the backend changes:** categories are free-text names per project (local DB: `task` 67, `story` 46, `bug` 45, `meeting` 14 across 5–11 projects; also `feature`, `Onboarding`, `Offboarding`). One project's "bug" can be another's "Defect", so a name cannot drive the UI. A stored `kind` can.

## Part 2 decisions

| # | Area | C shows | **Decision** |
|---|---|---|---|
| D16 | Block by type | Bug red tint + 🐞, Task teal + ☑, Feature purple + ★, Story purple + 📖, Testing cyan + ⚗; subtitle "Bug · ONEXSO Platform" | Task blocks take the **kind's colour** for the left bar, tint and border, and the **kind's icon** before the title. Subtitle = `categoryName · projectName`. **Meetings keep D6** (accent blue, calendar icon, avatars). C's "Sprint planning · Meeting" in purple is treated as a meeting, so it is blue. |
| D17 | Day header by type | Segmented bar; chips "☑ 1h 30m 🐞 1h 📖 45m" | The bar becomes **segments per kind**: each width = that kind's planned block minutes ÷ working minutes, capped at 100% in total; meeting minutes are not a segment. Under the bar, **chips for each kind with minutes > 0** (icon + duration), in kind-list order, at most **3**, then "+N". The **"· Over" label stays red** (D5); the bar no longer turns red. |
| D18 | Group by | "Group by: Due date / Type" | A segmented switch, default **Due date**, remembered per browser (`localStorage planner.queueGroupBy`). **Due date:** the sections Overdue / Due today / Due this week / Backlog (D11). **Type:** one section per kind present, in kind order, with the same 3 + "See all". |
| D19 | Type chips | All ✓ · Task 12 · Bug 5 · Story 3 · Feature 2 · Testing 2 · Meeting 1 | Chips: **All** plus each kind **present in the queue**, with counts. **Multi-select**; All clears the selection. They filter the cards in either grouping. **No Meeting chip** (meetings are not in the queue). Replaces D10. |
| D20 | Queue card | Type icon tile on the left, ⋮⋮ at the far left, title, "Client Portal · Open 6 days" | Left: ⋮⋮ handle, then a **rounded tile with the kind icon** in the kind colour (replaces the dot). Meta line = project name + one of: **Overdue** → "Open N days" in red (N = whole days since the task was created; "Open today" when 0); **Due today** → "2h left"; **This week** → "Fri · 2h" (weekday of the due date · time left); **Backlog** → "No due date · 2h" or "Due 12 Nov · 2h". Right: 📅 Plan. **No ⋯** (as D13). |
| D21 | Filter icon button | Funnel next to search | **Not built** (no defined filters beyond the chips). |
| D22 | Kind list | — | `task`, `bug`, `story`, `feature`, `testing`, `meeting` (system), `checklist` (system). Display order and labels: Task, Bug, Story, Feature, Testing, Meeting, Checklist. |
| D23 | Kind of existing categories | — | Backfilled once from the trimmed, case-insensitive name: bug / bugs / defect / defects → bug; story / stories / user story → story; feature / features / enhancement → feature; test / tests / testing / qa → testing; SystemKey `meeting` → meeting; onboarding / offboarding → checklist; **anything else → task**. New categories take a kind from the settings form, which defaults to the same inference. |
| D24 | Who sets the kind | — | Project settings → categories editor: a **Type** select per category. System categories (meeting) show their type read-only. |

**Kind styles** (one frontend source of truth, `KIND_META`). **The user approved the final preview on 2026-10-29**: the week grid with typed blocks, split day headers, today tint, red Over, and a Task queue with Group by, type chips, kind tiles and Plan buttons.

| Kind | Icon (inline SVG) | Colour |
|---|---|---|
| task | check-square | teal-600 |
| bug | bug | red-600 |
| story | book-open | violet-600 |
| feature | star | pink-600 (the approved preview; C had purple, too close to Story) |
| testing | flask | cyan-600 |
| meeting | calendar | `--color-accent` |
| checklist | list-checks | slate-500 |

## Part 2 Review Focus

1. **A project whose categories are all custom names**, e.g. "Chore", "Spike" → every one is `task` and renders teal; nothing breaks. *(Test in Task 11.)*
2. **A day with planned minutes over the working minutes** → the segment widths are capped at 100% in total, and the label still reads "· Over" in red. *(Test in Task 15.)*
3. **Type chips with a selection, then a search** → both filters apply (AND); clearing the search keeps the chip selection. *(Test in Task 16.)*
4. **A category renamed from "bug" to "Bugs fixed"** → its kind does **not** change on rename; only the Type select changes it. *(Test in Task 11.)*
5. **Group by Type with no tasks of some kind** → no empty section for that kind, and no chip for it. *(Test in Task 16.)*

## Task 11 (backend): category kind

**Files:**
- Modify:
  - `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs`: add `public string Kind { get; set; } = TaskCategoryKinds.Task;` and a static `TaskCategoryKinds` with the D22 constants plus `Infer(string name, string? systemKey)` implementing D23.
  - `.../Tasks/Services/DefaultTaskCategoryTemplate.cs` and `.../Meetings/Services/MeetingTaskDefaults.cs`: set `Kind`.
  - Wherever the Onboarding/Offboarding categories are created: `Kind = checklist`.
  - `.../Tasks/Commands/CreateTaskCategory/*`, `.../EditTaskCategory/*`: an optional `string? Kind`. Create: null → `Infer`. Edit: null → unchanged. Unknown → 400 `invalid_category_kind`. A system category's kind cannot change → 400.
  - `.../Tasks/DTOs/Responses/TaskCategoryResponse.cs`: `TaskCategoryResponse(Guid Id, string Name, int DisplayOrder, string? SystemKey, string Kind)`; the create/edit request records in `TasksController`.
  - EF configuration: `kind` varchar(20), not null, default `'task'`.
- Create: a migration `AddTaskCategoryKind` with a SQL backfill per D23 (`UPDATE task_categories SET kind = CASE … END`).
  - **Create it, do not apply it to a shared DB** (CLAUDE.md).
  - Check `ApplicationDbContextModelSnapshot.cs` changes only for `kind`.
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCategoryKindsTests.cs` (new), plus the existing create/edit category handler tests.

**Interfaces:**
- Produces: `TaskCategoryKinds.{Task,Bug,Story,Feature,Testing,Meeting,Checklist}`, `TaskCategoryKinds.All`, `TaskCategoryKinds.Infer(string name, string? systemKey) -> string`; `TaskCategoryResponse.Kind`.

- [ ] **Step 1:** Write the failing tests:
  - `Infer` theory: Bug → bug; " defects " → bug; User Story → story; QA → testing; Enhancement → feature; Onboarding → checklist; Chore → task; any name with systemKey `meeting` → meeting *(Review Focus 1)*.
  - Create handler: no kind → inferred. Edit handler: rename "bug" → "Bugs fixed" keeps `bug` *(Review Focus 4)*; kind "epic" → 400 `invalid_category_kind`; changing the meeting category's kind → 400.
- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~TaskCategor"`. Expected: FAIL.
- [ ] **Step 3:** Implement per Files. Generate the migration with `dotnet ef migrations add AddTaskCategoryKind --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`, then add the backfill SQL.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Then `pwsh ../dev-env/scripts/verify.ps1 backend`. Expected: green, including architecture tests.
- [ ] **Step 5:** Commit `feat(work): categories carry a kind (task, bug, story, feature, testing, meeting, checklist)`.

## Task 12 (backend): kind, category and created date in the Planner

**Files:**
- Modify: `PlannerContracts.cs`, `IPlannerRepository.cs` (`PlannerFocusTaskRow`), `EfPlannerRepository.cs`, `PlannerFocus.cs`, the range-block builder, `docs/api/planner-v1.md`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Planner/PlannerFocusTests.cs`, `tests/ONEVO.Tests.Integration/WorkManagement/Planner/PlannerFocusIntegrationTests.cs`, `PlannerRangeIntegrationTests.cs`

**Interfaces:**
- Produces:
  - `PlannerFocusItem(..., string Reason, string CategoryName, string CategoryKind, DateTimeOffset CreatedAt)` — appended at the end.
  - `PlannerRangeBlockView(..., bool StillAssigned, string CategoryName, string CategoryKind)` — appended at the end.
  - A task with no category → `CategoryName = ""`, `CategoryKind = "task"`.

- [ ] **Step 1:** Write the failing tests:
  - focus integration: a bug-category task's item has `CategoryKind == "bug"`, `CategoryName == "bug"`, and `CreatedAt` equal to the seeded value;
  - range integration: the block carries the kind;
  - unit: `PlannerFocus.Build` passes the fields through.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~PlannerFocus|FullyQualifiedName~PlannerRange"` (unit + integration). Expected: FAIL.
- [ ] **Step 3:** Left-join `TaskCategories` in the focus and range queries and carry the fields through.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): queue items and blocks carry category kind; items carry created date`.

## Task 13 (frontend): kind model, styles and the settings Type select

**Files:**
- Create: `work/utils/task-kind.ts`:
  - `TaskKind` = 'task' / 'bug' / 'story' / 'feature' / 'testing' / 'meeting' / 'checklist';
  - `TASK_KINDS: readonly TaskKind[]` (D22 order);
  - `KIND_META: Record<TaskKind, { label: string; icon: string; color: string; tint: string }>` (SVG path data and Tailwind classes per the styles table);
  - `kindOf(value: string | null | undefined): TaskKind` (unknown → 'task').
- Create: `work/ui/task-kind-icon/task-kind-icon.component.ts`: selector `app-task-kind-icon`, inputs `kind: TaskKind`, `tile = false` (a rounded tinted tile when true).
- Modify: `work/models/task-category.model.ts` (`kind: TaskKind`), `work/utils/task-category.mapper.ts`, `work/data-access/project-category-api.service.ts` (send `kind` on create/edit), `work/ui/category-list-editor/category-list-editor.component.ts` (a Type select per row; disabled for a system category).
- Test: `work/utils/task-kind.spec.ts`, `work/ui/task-kind-icon/task-kind-icon.component.spec.ts`, `work/ui/category-list-editor/category-list-editor.component.spec.ts`

- [ ] **Step 1:** Write the failing tests:
  - `kindOf('bug')` is 'bug', `kindOf('epic')` is 'task', `kindOf(null)` is 'task';
  - the icon component renders a tile with the kind's tint when `tile`;
  - the editor shows a Type select per category, emits the change with `kind`, and disables it for systemKey 'meeting'.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/work/utils/task-kind.spec.ts" --include "src/app/modules/work/ui/task-kind-icon/**" --include "src/app/modules/work/ui/category-list-editor/**"`. Expected: FAIL.
- [ ] **Step 3:** Implement per Files.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(work): task kinds with colours and icons; Type in category settings`.

## Task 14 (frontend): blocks by kind (D16)

**Files:**
- Modify: `work/planner/models/planner.models.ts` (`PlannerRangeBlockView.categoryName`, `categoryKind`; `PlannerFocusItem.categoryName`, `categoryKind`, `createdAt`), `calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{html,ts}`
- Test: `calendar-hour-grid.planner.spec.ts`

**Interfaces:**
- Consumes: Task 13 `KIND_META`, `kindOf`, `app-task-kind-icon`; Task 7 block markup.
- Produces: the block root gets `data-kind`; the subtitle testid stays `planner-block-project` with the text "categoryName · projectName" (just projectName when categoryName is empty).

- [ ] **Step 1:** Write the failing tests:
  - a bug block has `data-kind="bug"`, a red bar class (`KIND_META.bug.color`) and the subtitle 'bug · Portal';
  - a block with no category shows 'Portal' and `data-kind="task"`.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/ui/calendar-hour-grid/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D16 on top of Task 7's block. Meetings unchanged.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): blocks coloured and iconed by kind`.

## Task 15 (frontend): day header split by kind (D17)

**Files:**
- Modify: `calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{html,ts}`; `calendar/utils/planner-grid.util.ts` (`kindMinutes(blocks: readonly PlannerRangeBlockView[]): ReadonlyMap<TaskKind, number>`)
- Test: `calendar/utils/planner-grid.util.spec.ts`, `calendar-hour-grid.planner.spec.ts`

**Interfaces:**
- Produces: on the column VM, `kindSegments: readonly { kind: TaskKind; widthPct: number }[]` (sum ≤ 100) and `kindChips: { shown: readonly { kind: TaskKind; label: string }[]; more: number }`. testids `planner-load-segment` (with `data-kind`), `planner-kind-chip` (with `data-kind`), `planner-kind-more`.

- [ ] **Step 1:** Write the failing tests:
  - `kindMinutes` sums by kind;
  - for a column with task 90m, bug 60m, story 45m over 480m working: 3 segments with widths 18.75 / 12.5 / 9.375 and 3 chips '1h 30m', '1h', '45m';
  - a fourth kind adds '+1';
  - an over day caps the segment total at 100 and the label still ends '· Over' *(Review Focus 2)*.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/**/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D17. Scale the widths down proportionally when the total exceeds 100.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(planner): day load split by kind`.

## Task 16 (frontend): Task queue by type — Group by, type chips, kind tile, meta line (D18–D21)

**Files:**
- Modify: `calendar/ui/planner-focus-panel/planner-focus-panel.component.{html,ts,spec.ts}`

**Interfaces:**
- Consumes: Task 8 (`query`, sections, cards), Task 13 (`TaskKind`, `TASK_KINDS`, `app-task-kind-icon`).
- Produces:
  - `groupBy` signal: 'due' or 'kind', read from `localStorage planner.queueGroupBy` (default 'due') and written back on change;
  - `kinds` signal: `ReadonlySet<TaskKind>` (empty = All);
  - `presentKinds` computed (kinds with ≥ 1 item, in TASK_KINDS order, with counts);
  - `visibleGroups` (Task 8) now takes `groupBy` and the `kinds` filter: AND with `query`.
- testids: `queue-group-by-due`, `queue-group-by-kind`, `queue-kind-chip-all`, `queue-kind-chip-<kind>`, `queue-section-kind-<kind>`, `planner-focus-meta`.
- Meta line: D20. N for "Open N days" = whole days between `createdAt`'s date and today in the planner time zone.
- Removed: the due-date chips from Task 8 (`queue-chip-*`). Due-date grouping now comes from Group by.

- [ ] **Step 1:** Write the failing tests:
  - Group by Type shows a section per present kind, with no empty kinds *(Review Focus 5)*, and the choice is remembered;
  - type chips are multi-select; All clears;
  - a chip and a search combine as AND, and clearing the search keeps the chip *(Review Focus 3)*;
  - an overdue card's meta reads 'Client Portal · Open 6 days' in a danger class;
  - a due-today card reads '· 2h left'; a this-week card reads '· Fri · 2h';
  - a card shows `app-task-kind-icon` with `tile`, and no dot.
- [ ] **Step 2:** Run `npx ng test --watch=false --include "src/app/modules/calendar/ui/planner-focus-panel/*.spec.ts"`. Expected: FAIL.
- [ ] **Step 3:** Implement D18–D21.
- [ ] **Step 4:** Rerun Step 2. Expected: PASS. Then run the full `npx ng test --watch=false --include "src/app/modules/**/*.spec.ts"`, `npx tsc -p tsconfig.app.json --noEmit`, and `npx eslint` on the changed files. Expected: all green (only the 4 known lint errors).
- [ ] **Step 5:** Commit `feat(planner): Task queue groups and filters by type`.

### Task 10 additions (run last)

Add these checks to Task 10 Step 2:
- **Project settings:** change a category's Type and see its blocks and cards recolour.
- **Day header:** check the split bar and the chips on a day with three kinds.
- **Group by Type:** check the sections, the type chips plus search, and the overdue "Open N days".
- **Migration:** check that it backfilled the local DB, e.g. `select name, kind from task_categories`.
