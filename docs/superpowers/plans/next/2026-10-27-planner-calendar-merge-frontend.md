# Planner = Calendar: Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Calendar page the one "Planner": a Focus panel, duration-sized task blocks that can be dragged and resized in the hour grid, auto meetings with amber overlap and "Move after", working hours with per-day load, and summaries in Month and Agenda.

**Architecture:** The planner is an additive layer on `CalendarPageComponent`.
- A `PlannerLayerStore` loads `/planner/me/range` and `/planner/me/focus` beside the existing `CalendarEventStore`. It does nothing while the planner is off.
- The grid maths (busy intervals, overlap segments, drop length, Move-after slot, day load) is pure functions in one util file.
- `CalendarHourGridComponent` renders blocks and bands and handles pointer drag.
- `PlannerFocusPanelComponent` is the drag source; a root `PlannerDragService` carries the dragged task between them.

**Tech Stack:** Angular (standalone, signals, `@ngrx/signals`), Tailwind tokens, vitest, Playwright (live E2E).

**Spec:** `docs/superpowers/specs/next/2026-10-27-planner-calendar-merge-design.md` (in the backend repo).
**Backend contract:** `docs/superpowers/plans/next/2026-10-27-planner-calendar-merge-backend.md` (Tasks 1, 3, 4) and `docs/api/planner-v1.md` v1.1. The backend plan must be done first, or at least its contracts must be frozen.
**Repo / branch:** `C:\Users\User\onexso-worktrees\fe-phase2-e`. Create `feature/planner-calendar-merge` from `feature/calendar-meeting-guard`.

## Global Constraints

- **Planner off:** with `PlannerAvailabilityService.state() !== 'on'`, the page renders exactly as today's Calendar. There is no Focus panel, no blocks, no bands, no load bars and no planner requests, and the nav label is "Calendar".
- **Nav:** the label is **"Planner"** when the planner is on; there is no separate planner nav item.
- **Routes:** `/planner` and `/calendar` both render `CalendarPageComponent`.
- **Grid units:** snap is **15 min** and the minimum block is **15 min**. A block cannot cross midnight. The default drop length with no estimate is **60 min**.
- **Colours:**
  - Task blocks are teal; meetings keep their current purple.
  - Overlaps are amber: an amber tint on the task block's overlapping range and an amber outline on the meeting.
  - Outside working hours is `--color-surface-secondary`; the break is hatched.
- **What counts as busy (C2/C3):**
  - Counted: timed events and task meetings the user attends.
  - Not counted:
    - all-day events;
    - cancelled ones (`meetingStatus === 'cancelled'` or `eventStatus === 'cancelled'`);
    - ones the current employee declined (their `participants` entry has `responseStatus === 'declined'`).
  - Load = the union of busy and block intervals, so an overlap is counted once.
- **The Focus panel:**
  - It is open in Day view and open by default in Week view.
  - Month and Agenda show only the "Focus · N" button.
  - Its open/closed state is saved under localStorage key `planner.focusOpen`; reads and writes are wrapped in try/catch.
  - Each group shows 5 cards, then "+ N more".
- **Error copy:** every planner write error goes through `plannerErrorMessage`.
- **Opening a task:** clicking a block or card navigates to `['/work', projectId, 'board']` with `queryParams: { taskId }`.
- **Verification:** `npm run test` (vitest), `npm run build`, `npm run check:warnings`, and the live E2E (`npx playwright test --config=playwright.live.config.ts`) with backend launch config `backend-api-phase2-d-e2e` pointed at the `be-planner-merge` worktree.

## Review Focus

1. **A timed meeting that crosses midnight.** Its busy interval is clipped per day, so both days show the overlap and the load correctly. Covered by a test in Task 2.
2. **Dropping or moving onto another block** (own-block overlap). The preview turns red and no request is sent. If the server still answers 409, the block snaps back and a toast shows the message. Covered by tests in Task 4.
3. **"Move after" with no room left that day.** A toast "No free time left on {day} for this block" appears and nothing moves. Covered by tests in Tasks 2 and 6.
4. **The planner turned off mid-session** (503 on range or focus). The layer hides itself and the plain calendar keeps working. Covered by a test in Task 3.
5. **A past day in Week view.** Its blocks render but cannot be dragged, resized or dropped on. Covered by a test in Task 4.

---

### Task 1: Planner API models and calls for range and focus

**Files:**
- Modify:
  - `src/app/modules/work/planner/models/planner.models.ts`
  - `src/app/modules/work/planner/data-access/planner-api.service.ts`
  - `src/app/modules/work/planner/utils/planner-error-messages.ts`
- Test:
  - `src/app/modules/work/planner/data-access/planner-api.service.spec.ts` (create it if missing)
  - `planner-error-messages.spec.ts`

**Interfaces:**
- Produces, in the camelCase mirrors of the backend contracts:
  - `PlannerWorkWindow { workStart: string /*HH:mm:ss*/; workEnd: string; breakMinutes: number; breakStart: string | null; isWorkingDay: boolean; workingMinutes: number }`
  - `PlannerRangeBlockView { id; taskId; projectId; taskTitle; startAt; endAt; plannedMinutes; actualMinutes; stillAssigned }`
  - `PlannerRangeDay { date; editable; workWindow: PlannerWorkWindow | null; blocks: PlannerRangeBlockView[]; blockMinutes; workingMinutes }`
  - `PlannerRangeResponse { timezone; today; days }`
  - `PlannerFocusReason = 'overdue' | 'due_today' | 'due_this_week' | 'backlog'`
  - `PlannerFocusItem { taskId; projectId; projectName; title; priority; dueDate: string | null; estimatedMinutes: number | null; loggedMinutes; remainingMinutes: number | null; plannedAheadMinutes; firstPlannedDate: string | null; plannedDayCount; fullyPlanned; reason }`
  - `PlannerFocusGroup { reason; count; items }`
  - `PlannerFocusResponse { today; weekEnd; groups }`
  - `PlannerApiService.getRange(from: string, to: string): Observable<PlannerRangeResponse>` (GET `/planner/me/range?from&to`, silent)
  - `PlannerApiService.getFocus(): Observable<PlannerFocusResponse>` (GET `/planner/me/focus`, silent)
  - Error messages:
    - `planner_day_read_only` → "Past days can't be changed."
    - `planner_beyond_horizon` → "You can plan up to 28 days ahead."
    - `planner_invalid_range` → "That range is too long to show."
    - `planner_not_today` keeps its text for the rollout.

- [ ] **Step 1: Write the failing specs.** Use `HttpTestingController` to assert the URL `${apiUrl}/planner/me/range?from=2026-11-02&to=2026-11-08` and the URL `/planner/me/focus`. Assert the three new error messages.
- [ ] **Step 2:** `npx vitest run src/app/modules/work/planner`. Expected: FAIL.
- [ ] **Step 3: Implement the models, the two calls and the messages.**
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(planner): range and focus API calls`.

---

### Task 2: Grid maths (`planner-grid.util.ts`)

**Files:**
- Create: `src/app/modules/calendar/utils/planner-grid.util.ts`
- Test: `src/app/modules/calendar/utils/planner-grid.util.spec.ts`

**Interfaces:** All minutes are from local midnight of `date` in `timezone`, and all instants are ISO strings. Use the existing `utcIsoToLocalParts` / `localPartsToUtcIso` from `shared/utils/timezone.util`.
- `interface MinuteRange { start: number; end: number }`
- `interface BusyInterval extends MinuteRange { eventId: string; title: string }`
- `busyIntervals(events: readonly CalendarEventResponse[], employeeId: string | null, date: string, timezone: string): BusyInterval[]`. It applies the busy rule from the Global Constraints and clips each interval to [0, 1440].
- `blockRange(block: PlannerRangeBlockView, date: string, timezone: string): MinuteRange`
- `overlapSegments(block: MinuteRange, busy: readonly BusyInterval[]): (MinuteRange & { eventId: string; title: string })[]`
- `dropLength(remainingMinutes: number | null, startMinute: number, nextItemStart: number | null, workEnd: number | null): number`
  - It returns `remaining ?? 60`, capped at `min(nextItemStart, workEnd, 1440) − start`.
  - It is snapped down to 15 and is never below 15.
- `moveAfterSlot(length: number, afterMinute: number, taken: readonly MinuteRange[], window: { start: number; end: number } | null): number | null`. It returns the first start ≥ `afterMinute` on a 15-minute snap where [start, start + length) misses every taken range and fits the window (or the day, with no window). It returns null when nothing fits.
- `dayLoad(blocks: readonly MinuteRange[], busy: readonly MinuteRange[], workingMinutes: number): { plannedMinutes: number; freeMinutes: number; over: boolean }`
  - `plannedMinutes` is the union length.
  - `freeMinutes` is `max(0, working − planned)`.
  - `over` is `working > 0 && planned > working`.
- `windowMinutes(w: PlannerWorkWindow): { start: number; end: number; breakStart: number | null; breakEnd: number | null }`

- [ ] **Step 1: Write the failing specs**, with exact values:
  - `busyIntervals` leaves out all-day, cancelled, and declined-by-me events. It keeps an event that someone else declined.
  - A 23:00–01:00 meeting gives [1380, 1440] on day 1 and [0, 60] on day 2 (Review Focus 1).
  - `overlapSegments({540, 630}, [{600, 660}])` gives `[{600, 630}]`.
  - `dropLength`:
    - `(180, 600, 720, 1020)` → 120
    - `(null, 600, null, 1020)` → 60
    - `(10, 600, null, null)` → 15
    - `(200, 1000, null, 1020)` → 15
  - `moveAfterSlot`:
    - `(60, 660, [{660, 690}], {540, 1020})` → 690
    - `(60, 960, [{960, 1020}], {540, 1020})` → null (Review Focus 3)
  - `dayLoad([{540, 630}], [{600, 660}], 420)` → `{ plannedMinutes: 120, freeMinutes: 300, over: false }`, and over = true at working 100.
- [ ] **Step 2:** `npx vitest run src/app/modules/calendar/utils/planner-grid.util.spec.ts`. Expected: FAIL.
- [ ] **Step 3: Implement the functions.**
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(planner): grid maths for busy time, overlap, drop length, move-after and load`.

---

### Task 3: `PlannerLayerStore` and `PlannerDragService`

**Files:**
- Create:
  - `src/app/modules/calendar/state/planner-layer.store.ts`
  - `src/app/modules/calendar/state/planner-drag.service.ts`
- Test: `src/app/modules/calendar/state/planner-layer.store.spec.ts`

**Interfaces:**
- `PlannerLayerStore` (a `signalStore`, provided in `CalendarPageComponent`):
  - **State:**
    - `active: boolean` (planner on)
    - `range: PlannerRangeResponse | null`
    - `focus: PlannerFocusResponse | null`
    - `saving: boolean`
    - `error: string | null`
  - **Computed:**
    - `dayByDate(): ReadonlyMap<string, PlannerRangeDay>`
    - `focusCount(): number` (the sum of group counts)
  - **Methods:**
    - `load(from: string, to: string): Promise<void>`. It does nothing unless availability is 'on'. It fetches the range and the focus in parallel. A 503 `planner_disabled` sets `active=false` and calls `PlannerAvailabilityService.markAvailable(false)`.
    - `createBlock(taskId, startAt, endAt)`, `moveBlock(blockId, startAt, endAt)` and `removeBlock(blockId)`, all `Promise<boolean>`. Each one:
      1. calls the API;
      2. on success, re-runs `load` for the last range and returns true;
      3. on failure, puts `plannerErrorMessage(code)` in `error`, raises a `NotificationService` toast and returns false.
- `PlannerDragService` (root):
  - `readonly dragging = signal<{ taskId: string; projectId: string; title: string; minutes: number | null } | null>(null)`
  - `start(item: PlannerFocusItem): void`
  - `end(): void`

- [ ] **Step 1: Write the failing specs:**
  - When off it does nothing.
  - It loads both reads.
  - A 503 hides the layer and marks availability off (Review Focus 4).
  - A write refetches.
  - A 409 sets the overlap message and returns false.
- [ ] **Step 2:** Run the spec. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the spec. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(planner): planner layer store and drag hand-off`.

---

### Task 4: Hour grid: blocks, bands, overlap and pointer drag

**Files:**
- Modify:
  - `src/app/modules/calendar/utils/calendar-hour-grid.util.ts`
  - `src/app/modules/calendar/ui/calendar-hour-grid/calendar-hour-grid.component.{ts,html}`
  - `src/app/modules/calendar/feature/calendar-day-view/*`
  - `src/app/modules/calendar/feature/calendar-week-view/*` (pass-through of the new inputs and outputs)
- Test:
  - `calendar-hour-grid.util.spec.ts`
  - `calendar-hour-grid.component.spec.ts`

**Interfaces:**
- **Layout:** `layoutDayEvents` becomes `layoutDayItems(items: readonly GridItem[], dayStart: Date): readonly PositionedItem[]`.
  - It uses `type GridItem = { kind: 'event'; event: CalendarEventResponse; start: string; end: string } | { kind: 'block'; block: PlannerRangeBlockView; start: string; end: string }` and the same column-packing.
  - Keep `layoutDayEvents` as a wrapper so existing callers still work.
- **New hour grid inputs:**
  - `blocks = input<ReadonlyMap<string, readonly PlannerRangeBlockView[]>>(new Map())`
  - `workWindows = input<ReadonlyMap<string, PlannerWorkWindow | null>>(new Map())`
  - `editableDates = input<ReadonlySet<string>>(new Set())`
  - `loads = input<ReadonlyMap<string, { plannedMinutes: number; workingMinutes: number; over: boolean }>>(new Map())`
  - `employeeId = input<string | null>(null)`
  - `focusRemaining = input<ReadonlyMap<string, number | null>>(new Map())` (the remaining minutes per taskId, for the drop preview)
- **New outputs:**
  - `blockCreate = output<{ taskId: string; date: string; startMinutes: number; endMinutes: number }>()`
  - `blockMove = output<{ blockId: string; date: string; startMinutes: number; endMinutes: number }>()`
  - `blockOpen = output<PlannerRangeBlockView>()`
  - `blockMoveAfter = output<{ block: PlannerRangeBlockView; date: string; afterMinute: number }>()`
  - `blockRemove = output<string>()`
- **Rendering, per column:**
  - Outside-window bands and the hatched break (from `windowMinutes`). On a day with `isWorkingDay: false`, the whole column is dimmed but stays plannable.
  - A block's `title=` tooltip reads "{title} · {HH:mm–HH:mm} · {planned} planned · {actual} actual".
  - Blocks: `data-testid="planner-block"`, the title, `HH:mm–HH:mm`, and the duration. Below 30 min only the title shows, with the rest in `title=`.
  - The amber overlap tint `data-testid="planner-overlap"` over each `overlapSegments` range, and the pill `data-testid="planner-overlap-pill"` with the text "Overlaps {title} · Move after". Clicking the pill emits `blockMoveAfter` with `afterMinute` = the segment's meeting end.
  - On editable dates:
    - a bottom-edge handle `data-testid="planner-block-resize"`;
    - a "⋯" menu `data-testid="planner-block-menu"` with Earlier/Later 15, Shorter/Longer 15, Move after meeting and Remove.
- **The header cell under the date** shows `data-testid="planner-load"`: a bar plus "5h 30m / 7h", amber with "over" when `over` is true.
- **Pointer drag:** reuse the existing pointer-capture pattern (`onSlotPointerDown`).
  - On a block: pointerdown starts a move and pointerdown on the handle starts a resize.
  - While `PlannerDragService.dragging()` is set, pointermove over a column shows `data-testid="planner-drop-preview"` sized with `dropLength`, and pointerup emits `blockCreate`.
  - If the preview range intersects another block on that date, it gets `data-invalid="true"` and nothing is emitted (Review Focus 2).
  - Non-editable dates show no preview and emit nothing (Review Focus 5).
  - Snap is 15.

- [ ] **Step 1: Write the failing util specs.** A block and an event that overlap get two columns. Blocks-only layout is unchanged from events-only for equal ranges.
- [ ] **Step 2: Write the failing component specs.** Use `fixture.componentRef.setInput` and synthetic `PointerEvent`s; a 30-min slot is 1440 px ÷ 48 in the test layout. The specs cover:
  - The block renders with a height proportional to its duration.
  - The amber segment and the pill appear only when overlapping; the pill emits `blockMoveAfter`.
  - Dragging a block 2 slots down emits `blockMove` +60 min; dragging the handle emits a longer end.
  - A drop while dragging emits `blockCreate` with the `dropLength` end; a drop on an occupied range emits nothing.
  - A past date has no handle, no menu and no preview.
  - The load cell shows "over".
- [ ] **Step 3:** `npx vitest run src/app/modules/calendar`. Expected: FAIL.
- [ ] **Step 4: Implement**, keeping existing event drag-to-create (`slotRange`) working.
- [ ] **Step 5:** Run the same command. Expected: PASS.
- [ ] **Step 6: Commit** with `feat(planner): task blocks, working hours and overlap in the calendar hour grid`.

---

### Task 5: Focus panel and touch picker

**Files:**
- Create:
  - `src/app/modules/calendar/ui/planner-focus-panel/planner-focus-panel.component.{ts,html,spec.ts}`
  - `src/app/modules/calendar/ui/planner-block-picker/planner-block-picker.component.{ts,spec.ts}`

**Interfaces:**
- **`PlannerFocusPanelComponent`:**
  - `focus = input<PlannerFocusResponse | null>(null)`
  - `closable = input(true)`
  - `closed = output<void>()`
  - `pick = output<PlannerFocusItem>()` (touch / narrow)
  - `open = output<PlannerFocusItem>()` (title click)
- **Panel layout:** groups in this order, headings "Overdue · N" (danger colour), "Due today · N", "Due this week · N" and "Assigned backlog · N".
- **Each card** (`data-testid="planner-focus-card"`) shows:
  - the title;
  - the project name (muted);
  - a priority dot for high or critical;
  - "Due {d MMM}", "Today" or "No due date";
  - "{h}h left" when `remainingMinutes` is set;
  - "planned {Ddd}" (+N for more days) when `firstPlannedDate` is set.
  - `fullyPlanned` cards are dimmed.
- **Group size:** 5 cards, then a "+ N more" button that expands the group.
- **Drag:** pointerdown on a card on a wide screen calls `PlannerDragService.start(item)`; pointerup anywhere calls `end()`. On a narrow screen (`matchMedia('(max-width: 767px)')`) a tap emits `pick` instead.
- **`PlannerBlockPickerComponent`:**
  - `item = input.required<PlannerFocusItem | PlannerRangeBlockView>()`
  - `editableDates = input.required<readonly string[]>()`
  - `submitted = output<{ date: string; startMinutes: number; minutes: number }>()`
  - `cancelled = output<void>()`
  - Fields: day (a select of `editableDates`), start time (15-min steps) and duration (15–480, in 15s). Its default duration is `remainingMinutes ?? 60`.

- [ ] **Step 1: Write the failing specs:**
  - the group order and counts;
  - "+ N more" expands the group;
  - the card text for each field;
  - a dimmed card when fully planned;
  - pointerdown starts the drag with the item;
  - a narrow-screen tap emits `pick`;
  - the picker defaults and emits.
- [ ] **Step 2:** Run the specs. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the specs. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(planner): Focus panel and touch picker`.

---

### Task 6: Wire the layer into the Calendar page, Month and Agenda

**Files:**
- Modify:
  - `src/app/modules/calendar/feature/calendar-page/calendar-page.component.{ts,html,spec.ts}`
  - `src/app/modules/calendar/ui/calendar-month-grid/*`
  - `src/app/modules/calendar/feature/calendar-agenda-view/*`

**Interfaces:**
- **The page:**
  - Provide `PlannerLayerStore`.
  - On every range change (the same place `store.loadEvents` is called), call `layer.load(from, to)` with the visible dates. Month uses the visible 6-week grid, at most 42 days.
  - Build the grid inputs from `layer.dayByDate()`, `busyIntervals` and `dayLoad`.
- **Handlers:**
  - `blockCreate` / `blockMove` convert minutes to ISO with `localPartsToUtcIso` and call the store.
  - `blockMoveAfter` uses `moveAfterSlot` with the date's taken ranges (blocks except this one, plus busy). On null it toasts "No free time left on {Ddd d MMM} for this block" (Review Focus 3).
  - `blockOpen` and the panel's `open` navigate to the task.
  - The panel's `pick` and a block's narrow-screen tap open `PlannerBlockPickerComponent`.
- **Day header:** above the grid when the layer is active, `data-testid="planner-day-totals"` shows:
  - Working hours (or "Working hours not set");
  - Planned;
  - Free;
  - Actual / attendance, from `GET /planner/me?date=` (the existing `getMyDay`), loaded in Day view only.
- **Panel placement:** a right column (`md:grid-cols-[1fr_18rem]`).
  - Day view: always shown.
  - Week view: shown when `focusOpen()`.
  - The "Focus · N" button (`data-testid="planner-focus-toggle"`) is in the toolbar.
  - Persist `planner.focusOpen` as in the Global Constraints.
- **Weekend:** Week view shows only the working days of the first day's `workWindow` week (all 7 when no window is known). A "Show weekend" toggle (`data-testid="planner-show-weekend"`, localStorage `planner.showWeekend`) shows all 7. The week view gets `days = input<readonly string[] | null>(null)` to override its 7-day list.
- **Month:** the month grid gets `summaries = input<ReadonlyMap<string, { tasks: number; meetings: number; plannedMinutes: number }>>(new Map())` and renders `data-testid="planner-month-summary"` as "3 tasks · 2 meetings · 5h". Clicking a chip opens Day view on that date (the existing `dayClick`).
- **Agenda:** the agenda view gets `blocks = input<readonly PlannerRangeBlockView[]>([])` and merges them in time order. Each block is marked "Planned task" (`data-testid="planner-agenda-block"`).

- [ ] **Step 1: Write the failing page specs:**
  - With the planner off: no panel, no planner requests, and the existing calendar specs still pass.
  - With it on: the Day view shows the totals and the panel, and the Week view toggle hides and shows the panel and persists the choice.
  - Move-after with no room shows the toast.
  - Week view hides Sat/Sun for a Mon–Fri entity until "Show weekend" is on.
  - The month chip text, and agenda blocks in order.
- [ ] **Step 2:** `npx vitest run src/app/modules/calendar`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run the same command. Expected: PASS.
- [ ] **Step 5: Commit** with `feat(planner): calendar page becomes the planner - focus panel, day totals, month and agenda`.

---

### Task 7: Nav, routes, remove the old planner page, live E2E

**Files:**
- Modify:
  - `src/app/layouts/main-layout/nav/nav-items.config.ts`
  - `navigation-state.service.ts` (+ spec)
  - `src/app/app.routes.ts`
  - `e2e-live/meetings-and-planner.live.spec.ts`
- Delete:
  - `src/app/modules/work/planner/feature/planner-page/` (component and spec)
  - the `planner` nav item
- Test:
  - `navigation-state.service.spec.ts`
  - `app.routes` spec, if one exists
  - live E2E

**Interfaces:**
- The `calendar` nav item's label is resolved in `NavigationStateService`: "Planner" when `plannerAvailability.state() === 'on'`, else "Calendar".
- `/planner` loads `CALENDAR_ROUTES` with the same guards and data as `/calendar` (`permissions: ['calendar:read'], modules: ['calendar']`).

- [ ] **Step 1: Write the failing specs:**
  - The nav shows one item, labelled "Planner" when on and "Calendar" when off.
  - The `/planner` route resolves to the calendar page.
- [ ] **Step 2:** Run the specs. Expected: FAIL.
- [ ] **Step 3: Implement**, delete the old page, and fix its imports. `PlannerAvailabilityService` and `PlannerApiService` stay.
- [ ] **Step 4: Run the full checks.** `npm run test && npm run build && npm run check:warnings`. Expected: all green, 0 warnings.
- [ ] **Step 5: Rewrite the live planner journey and add new ones.** Use the Week view:
  1. Drag the first Focus card onto tomorrow 10:00 with `page.mouse` (pointer events). Expect a `planner-block` with the card's title.
  2. Drag its `planner-block-resize` handle one slot down. Expect the duration to grow by 30 min.
  3. Create a meeting overlapping it (the meeting form, as in the existing meeting journey). Expect `planner-overlap-pill`, click it, then expect no `planner-overlap`.
  4. Remove the block from its menu.
  5. In a separate test, check that the nav link reads "Planner" and `/calendar` still loads.
- [ ] **Step 6: Run the live E2E twice.** Expected: all green both runs.
- [ ] **Step 7: Commit** with `feat(planner): one Planner nav item; the old planner page is gone; live journeys`, and push `feature/planner-calendar-merge`. Do not open a PR.
