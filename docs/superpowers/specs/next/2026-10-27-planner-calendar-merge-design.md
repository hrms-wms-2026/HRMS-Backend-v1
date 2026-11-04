# Planner = Calendar: merge the day planner into the calendar

**Status:** Approved 2026-10-27. The user locked the Day, Week and Focus-panel mockups that were shown in chat. Builder-check answers are folded in below.
**Builds on:** `2026-10-26-meeting-hours-and-day-planner-design.md` (Part B, the day planner), `docs/api/planner-v1.md`, and the meetings-as-tasks Phase 2 calendar merge (`itemKind: 'meeting'`).
**Repos:** backend (this repo) and the Angular tenant app (`front end-org/Hrms--Web-application---front-end---v1`).

## 1. Intent

Today an employee has two separate places: **Calendar** (events and task meetings) and **Planner** (drop due tasks on today's timeline). The user wants one place, named **Planner**:

- The system tells the employee what to focus on: overdue tasks, tasks due today, tasks due this week, and the assigned backlog.
- The employee plans their working hours by dragging those tasks onto the calendar grid.
- Meetings they organise or are invited to fill their time slots automatically.
- When a planned task overlaps a meeting, the overlap shows in a different colour. The employee sees it and reshuffles their own plan.
- A block's height follows its duration.
- Everything planned shows in the calendar's Day, Week, Month and Agenda views.

**Success:** an employee opens Planner in the morning, sees what to focus on, drags tasks into free working time, sees clashes with meetings at a glance, and fixes them by dragging. They never need a second screen.

## 2. Goals, non-goals, anti-goals

**Goals**
- One nav item, **Planner**, which replaces both "Calendar" and the separate "Planner".
- A Focus panel with four groups (Overdue, Due today, Due this week, Assigned backlog), ranked.
- Planned task blocks shown inside the calendar's hour grid (Day and Week), sized by duration.
  - They can be dragged to move them, including to another day.
  - They can be resized by dragging the bottom edge, snapping to 15 minutes.
- Meetings appear automatically, using the calendar data that already includes task meetings.
- A task block that overlaps a meeting is laid out side by side with it. The overlapping part is tinted amber and a warning pill offers "Move after".
- Working hours (legal entity schedule) are visible. Time outside them is dimmed and the break is hatched.
- A per-day load is shown: working hours vs planned. A day planned beyond its working hours is flagged "over".
- **Days ahead can be planned.** Today and future days are editable; past days are read-only. This is a change from "today only".

**Non-goals**
- Auto-scheduling: the system suggests *what* to work on, not *when*. The employee places every block.
- Changing a task's due date, estimate or status from the planner. That rule stays from planner v1.
- Moving or editing meetings from a planner block. Meetings keep their own drawer and rules.
- A manager-facing team planner screen. The read endpoint for a teammate's day stays as it is, with no new UI.
- Native mobile apps.

**Anti-goals** (failures even if everything else works)
- Planning becomes slower than today's planner. For example, the Focus list is hidden behind extra clicks in Day view.
- Overlaps block the user from saving. An overlap with a meeting is only ever a warning.
- The plain calendar stops working for tenants with the planner off. With `Planner:Enabled` off, the page must behave exactly like today's Calendar.

## 3. Decisions taken in the conversation

| # | Decision | Source |
|---|---|---|
| D1 | The nav label is **Planner**, and the separate Planner nav item is removed. | user |
| D2 | The Focus groups are Overdue, Due today, Due this week, and Assigned backlog. | user |
| D3 | Meetings that include the user fill their time automatically. | user |
| D4 | A meeting overlap is shown side by side, with the overlapping part in a different colour (amber). The user reshuffles the plan themselves. | user |
| D5 | Block height = duration. | user |
| D6 | Planned data shows in the calendar views (Day, Week, Month, Agenda) as in the mockups. | user |
| D7 | Future days can be planned. The locked Week mockup shows blocks on Wed–Fri and a drop target on Fri. | user (locked mockup) |
| D8 | The Focus panel is open by default in Day view, closable in Week view, and a "Focus · N" button reopens it. | locked mockup |
| D9 | While dragging, a dashed preview block sized to the task's estimate is shown. | locked mockup |

## 4. Behaviour

### 4.1 Navigation and flags
- **The nav item** keeps id `calendar` and its current gating: module `calendar` and permission `calendar:read`.
  - Its label is **Planner** when the planner is available (`PlannerAvailabilityService.state() === 'on'`). Otherwise it is **Calendar**.
  - The old `planner` nav item is deleted.
- **Routes:**
  - `/planner` renders the calendar page.
  - `/calendar` stays and renders the same page, so old links and email links keep working.
  - The old `PlannerPageComponent` route goes away. Its `/planner` URL now lands on the merged page.
- **Planner off:**
  - With the planner off (`503 planner_disabled`, or the work modules missing), the page is today's Calendar: no Focus panel, no blocks, no load bars.
  - The planner layer is purely additive on top of the calendar.

### 4.2 Focus panel
Each card shows:
- the title;
- the project name (muted);
- a priority dot, shown for `high` or `critical`;
- the due date or "No due date";
- the remaining estimate ("3h left").
- If the task already has blocks on today or a later day, it also shows "planned Wed" (the first future planned day). Several days read "planned Wed +1".

Groups, in this order, with counts:
1. **Overdue:** assigned to me, not done, not a meeting, due date < today.
2. **Due today:** due date = today.
3. **Due this week:** today < due date ≤ the last day of the current week (Monday-based, the same as the calendar's `startOfWeek`).
4. **Assigned backlog:** assigned to me, not done, not a meeting, with no due date or a due date after this week.

**Order within a group:**
1. Fully planned tasks go to the bottom.
2. Then priority: critical, high, medium, low.
3. Then due date ascending (nulls last).
4. Then title.

**Remaining estimate:** `max(0, estimatedHours×60 − loggedMinutes)`, where loggedMinutes is the task's total clocked minutes (task clock sessions; the same source as planner "actual"). With no estimate, nothing is shown.

**Fully planned:** the planned minutes on today and future days are ≥ the remaining estimate. A task with no estimate is never "fully planned". It just gets the "planned …" tag.

**Group caps:** each group shows its first 5 cards and then "+ N more". The backlog group starts collapsed after 5 cards.

**The panel in each view:**
- Day view: always visible on desktop.
- Week view: open by default and closable.
- Month and Agenda: hidden, with the "Focus · N" button available.
- The open/closed state is remembered per user in local storage.

### 4.3 Blocks on the grid
- **Placement:** blocks are drawn in the existing `CalendarHourGridComponent` columns together with events. The current `layoutDayEvents` column-packing is extended to take planner blocks as a second item kind, so overlaps get side-by-side columns.
- **Visual style:**
  - Task block: teal.
  - Meeting: purple, as task meetings are today.
  - Personal or synced calendar events: their current styling.
  - Text: the title, the time range and the duration. When the block is too short for all of it, only the title shows; the full detail is in a tooltip.
- **Drop from the Focus panel:**
  - A dashed preview follows the pointer, snapped to 15 minutes.
  - Its length is the task's remaining estimate, capped at the next item (block or meeting) or the end of working hours. The minimum is 15 minutes.
  - With no estimate, the length is 60 minutes, capped the same way.
  - On drop the block is created.
- **Move:** drag a block to another time or another day column (Week view). Its length is kept.
- **Resize:** drag the bottom edge in 15-minute steps; the minimum is 15 minutes. A block cannot cross midnight.
- **Keyboard and accessibility:** a block's "⋯" menu offers Move earlier/later 15 min, Shorter/Longer 15 min, Move after meeting and Remove.
- **Touch (narrow screens):** tapping a Focus card or a block opens a small picker (day, start, duration) instead of drag.
- **Own-block overlap:** blocks may not overlap each other; the server still answers 409 `planner_block_overlap`. While dragging over a place that would overlap, the preview turns red and the drop is refused client-side with the same message.
- **Past days:** read-only. Blocks are shown, but there is no drag, resize or drop.
- **Clicking a block** opens the task (the existing task detail route or drawer).
- **Planned vs actual:** a block shows its planned minutes. Actual minutes show in the tooltip and in the Day view header.

### 4.4 Meetings and overlap
- **Source:** meetings come from the calendar events feed (`itemKind: 'meeting'` rows and other events), not from the planner response, so the grid has a single source for them.
- **Which events count as a clash:**
  - The amber treatment applies when a task block overlaps a task meeting or a timed calendar event the user is attending.
  - These never count: declined, cancelled and all-day events (see C3).
- **Overlap display** (computed client-side from the two intervals):
  - Both items share the cluster's columns.
  - The overlapping time range on the task block is tinted amber.
  - The meeting gets an amber outline.
  - A pill on the block reads "Overlaps {meeting title} · Move after".
- **"Move after":** moves the block to the first free slot of the same length on the same day, starting at the end of the clashing meeting, inside working hours, and clear of blocks and meetings.
  - If there is none, a toast says "No free time left on {day} for this block" and nothing moves.
- The server's existing `overlapsMeeting` flag stays and remains authoritative for other consumers (reports). The UI uses its own calculation only to know *which part* to tint.

### 4.5 Working hours and load
- **Source:** the employee's legal entity schedule (`WorkStartTime`, `WorkEndTime`, the break minutes and `StandardWorkingDays`), resolved the same way as `AttendanceScheduleResolver`, in the employee's timezone.
- **Break placement:** the break is drawn centred in the window. If the schedule has an explicit break start, use that instead. The builder checks which one exists.
- **With no work window configured:**
  - There is no dimming and no load bar.
  - The Day header shows "Working hours not set" instead of the number.
- **Non-working days:**
  - Week view shows the legal entity's working days by default, with a "Show weekend" toggle.
  - A non-working day's column is dimmed but can still be planned.
- **Day header (Day view):** Working hours, Planned (blocks + meetings that are not declined or cancelled, inside the day), Free (working minus planned, floored at 0), and Actual / attendance (the existing numbers).
- **Week column header:** a bar of planned ÷ working hours, plus "5h 30m / 7h". When planned > working, the bar is amber and the text reads "over".
- **Month cell:** a summary chip such as "3 tasks · 2 meetings · 5h". Clicking the chip opens Day view for that date.
- **Agenda:** blocks are listed with events in time order and marked as planned tasks.

## 5. Backend changes

All of these stay behind `Planner:Enabled`. `docs/api/planner-v1.md` is updated, or a v2 section is added.

1. **Future days are editable.**
   - The block create, move and delete rules change from "today only" to "today or later, up to 28 days ahead" (the horizon is the builder's call, kept as a constant).
   - Past days stay read-only (`planner_not_today` becomes `planner_day_read_only`; keep accepting the old code in the frontend error map during rollout).
   - `GET /planner/me?date=` accepts future dates in the same horizon.
   - A move to another day is allowed if both days are editable.
2. **Range read for the grid:** `GET /api/v1/planner/me/range?from=YYYY-MM-DD&to=YYYY-MM-DD`, at most 42 days (a Month grid).
   - It returns, per day:
     - `date`, `editable`;
     - the work window (`workStart`, `workEnd`, `breakMinutes` and break start if known, `isWorkingDay`) or null;
     - `blocks` (as in v1);
     - `plannedMinutes` (blocks plus counted meetings);
     - `workingMinutes`.
   - Meetings are **not** repeated here; the frontend already has them from `/calendar`.
   - One query per concern for the whole range, not per day.
3. **Focus list:** `GET /api/v1/planner/me/focus`.
   - It returns the four groups. Each item has `taskId`, `title`, `projectName`, `priority`, `dueDate`, `estimatedMinutes`, `loggedMinutes`, `remainingMinutes`, `plannedAheadMinutes` (today and later), `firstPlannedDate`, `plannedDayCount`, `reason` (`overdue | due_today | due_this_week | backlog`), and the total count per group.
   - It is already sorted as in 4.2.
   - Capped at 50 items per group, with counts uncapped.
   - "This week" uses the employee's timezone.
4. **Write responses:** writes keep returning a day, the day of the block written. A move across days returns the target day, and the frontend refetches the visible range.
5. **The v1 `dueTasks` list** stays in `GET /planner/me` for compatibility. The new page does not use it.

Unchanged: the block rules (assigned task, not a meeting, ≥ 15 minutes, inside one day, no overlap with own blocks), the per-employee plan lock, manager read access, private meeting masking, and the actual-minutes rules.

## 6. Frontend changes (outline; the plan decides the order)

- Nav config: delete the `planner` item; the `calendar` item's label follows planner availability.
- Routes: add `/planner` pointing at the calendar page and remove the old planner page.
- The calendar page gets a planner layer:
  - A `PlannerRangeStore` that loads `/planner/me/range` for the visible range alongside the calendar events.
  - A `FocusStore` that loads `/planner/me/focus` and refreshes it after each write.
- The hour grid takes blocks and work windows as inputs. It renders blocks, the dimmed and break bands, the drag preview, move/resize, and overlap tinting.
- The Week header renders the load bars. The Month cell renders the summary chips. The Agenda lists blocks.
- Focus panel component: groups, cards, drag source, and the touch picker.
- Delete `PlannerPageComponent` and its spec. Update `e2e-live/meetings-and-planner.live.spec.ts` so its planner journey runs on the merged page.

## 7. Testing

- **Backend:** TDD unit and integration tests for the future-day rules, the range endpoint (per-day numbers, work windows, the 42-day cap, the past-day read-only flag) and the focus grouping, sorting and remaining minutes, including timezone edges at week boundaries.
- **Frontend:** vitest specs for:
  - the layout with blocks and events;
  - overlap segment calculation;
  - "Move after" slot finding;
  - the drop length cap;
  - the load bar and the "over" state;
  - nav label switching;
  - planner-off fallback to the plain calendar.
- **Live E2E:** the existing journeys still pass. A new one covers dragging from the Focus panel to tomorrow in Week view, resizing by the edge, an overlap with a meeting showing amber, and "Move after" clearing it.

## 8. Builder-check calls (made by Claude, not the user; flagged for review)

| # | Question | Call |
|---|---|---|
| C1 | Whose timezone sets the work window? | **The employee's own zone** (the `/calendar/my-timezone` zone the planner and the grid already use). The legal entity's `WorkStartTime`–`WorkEndTime` is read as wall-clock in that zone, so 09:00–17:00 stays 09:00–17:00 for a remote employee. The resolver's working-day and configured semantics are reused, but not its zone. |
| C2 | What does the load ("Planned", "Free", "over") count? | **The frontend computes it** from the blocks plus the busy `/calendar` items it already shows: task meetings and timed events the user attends, excluding declined, cancelled and all-day ones. Overlapping time is **counted once** (the union of busy intervals), so an overlap alone never pushes a day to "over". The range endpoint returns only `blockMinutes` and `workingMinutes` per day, not a `plannedMinutes` that would disagree with the grid. §4.5 and §5.2 are read with this change. |
| C3 | Where does "free" (show-as) come from? | **Nowhere yet.** Drop the "free" exclusion from §4.4. Every timed, non-declined, non-cancelled, non-all-day event counts as busy. A show-as field is a later change. |
| C4 | Whose logged minutes reduce the remaining estimate? | **All clock sessions on the task, by anyone.** The estimate is task-level, so "3h left" is what the task still needs. |
| C5 | Overnight windows (WorkEnd ≤ WorkStart)? | Treated as not configured, the same as `IsScheduleConfigured`: "Working hours not set", no dimming, no load bar. |
| C6 | Days past the 28-day horizon in a 42-day range? | Returned with `editable: false`. |
| C7 | Break start? | `LegalEntity` has only `BreakDurationMinutes`, so the break is centred in the window. |

## 9. Left to the builder

- The exact visual tokens (teal/purple/amber shades from the app's palette) and the tooltip layout.
- Whether the drag uses native HTML5 drag-and-drop or pointer events. Pointer events are recommended for resize and preview, and the E2E must be able to drive it.
- The future-planning horizon (28 days suggested).
