# Planner, Calendar and Meetings — UI inventory for the redesign

> **Snapshot date:** 2026-10-29
>
> **Covers:** what the Planner (the Calendar page), the Task queue and meeting creation do today, so the redesign keeps every behaviour it needs.
>
> **Based on:** the code as it stands after frontend PRs #132 → #133 → #134 and backend PRs #201 and #203. Some of those PRs may not be merged yet.
>
> **Does not cover:** visual styling. The redesign owns that.

---

## 0. Feature flags (read this first)

Two backend flags decide what users see. Both are **off by default**.

| Flag | Off | On |
|---|---|---|
| `Planner:Enabled` | The page is a plain **Calendar**. Every planner endpoint answers `503 planner_disabled` and the UI hides every planner piece. | The page becomes the **Planner**: work hours, task blocks, Task queue, load bars. |
| `Meetings:Create` | Meeting writes answer `503 meetings_disabled`. The UI then hides the "meeting" option. | Meetings can be created as work items. |

- **Local dev:** `appsettings.Development.json` turns the Planner on (PR #201). `Meetings__Create=true` lives only in the local `.env`.
- **Detection:** the app has no flags endpoint. It learns that meetings are off from the first `503` or `409` (`MeetingsAvailabilityService`).

---

## 1. The Planner page (`/planner`, `app-calendar-page`)

### 1.1 Layout (desktop)

```
┌ Planner ───────────────────────────────────────────────────────────────┐
│ ‹ Today ›  Week of 2026-10-26        Month|Week|Day|Agenda  ◯ Weekend  │
│                                      Task queue · 10   [New event]     │
├──────────────────────────────────────────────────────┬─────────────────┤
│ hour grid / month grid / agenda                      │ Task queue      │
│                                                      │ panel           │
└──────────────────────────────────────────────────────┴─────────────────┘
```

- **Page header:** just the page name — "Planner", or "Calendar" while the planner is off. No summary line.
- **Padding:** on desktop the content gutter is 12px / 16px, so the grid gets the room.
- **Toolbar:**
  - `‹` / `Today` / `›` step through the period.
  - The label reads "Week of YYYY-MM-DD" in Week view, the date in Day view, "Month YYYY" in Month view and "Agenda" in Agenda view.
  - The view switch: Month · Week · Day · Agenda.
  - **Weekend switch:** Planner only, Week view only.
  - **Task queue · N toggle:** Planner only. N is the total count across all groups.
  - **New event** opens the event form (§3).
- **Removed on purpose:** there is no separate "New meeting" or "Meeting (work item)" button anywhere. Meetings are made from the work item form (§4).

### 1.2 Views

| View | Shows | Planner extras |
|---|---|---|
| **Week** | Hour grid for 7 days, or only the working days while the Weekend switch is off | Work-hours shading, task blocks, a per-day load bar in the column header, Task queue panel (open by default, remembered) |
| **Day** | Hour grid for one day | A **day totals** strip (below). The Task queue panel is always shown and cannot be closed. |
| **Month** | Month grid | A chip per day with tasks/meetings counts and planned time. The panel shows only when toggled. |
| **Agenda** | A list of events and planner blocks | The panel shows only when toggled. |

**Day totals strip** (Day view):

| Item | Notes |
|---|---|
| `Working hours 8h` | Reads "Working hours not set" when the schedule is not configured |
| `Planned 3h` | |
| `Free 5h` | |
| `Actual / attendance 45m / 2h` | Shown only when attendance data exists |

### 1.3 Hour grid — what is drawn

| Element | Look today | Data source |
|---|---|---|
| **Working hours** | White | Legal entity `work_start_time` → `work_end_time` (§2.1) |
| **Off hours** | Grey bands before the work start and after the work end | Same as above |
| **Rest day** | Whole column grey (Weekend switch on) | `standard_working_days` |
| **Break** | **Not drawn** (removed: its time was only a guess). The break still comes off the working time. | `break_duration_minutes` |
| **Now line** | Red line and dot with the time label | Client clock, in the user's time zone |
| **Calendar events** | Coloured blocks | Calendar events API |
| **Meetings** | Blue blocks (e.g. "meeting A") | Calendar feed, item kind `meeting` |
| **Task blocks** (planner) | Teal outline: title, `02:30–04:30 · 2h`, `⋯` menu | Planner range API |
| **Overlap tint** | The part of a task block under a meeting turns amber, with an "Overlaps *X* · Move after" pill | Client-side clash check |
| **Load bar** (column header) | `4h 15m / 8h`. Teal under the limit; amber with "· over" above it | Planned blocks and busy calendar time, overlapping time counted once |

### 1.4 Hour grid — interactions

| Gesture | Result |
|---|---|
| Click an empty slot | **New event** form at that hour |
| Drag across empty slots | **New event** form for that range |
| Click an event or meeting | Event form, or the **meeting occurrence drawer** for a meeting |
| Click a task block | Opens the task (task form in edit mode) over the planner |
| Drag a task block | Moves it, snapping to 15 min; can change day |
| Drag a block's bottom edge | Resizes it |
| `⋯` on a block | Earlier 15 min · Later 15 min · Shorter 15 min · Longer 15 min · Move after meeting (only when it overlaps a meeting) · Remove |
| Drop a Task queue card | Creates a block (§1.5) |

**Rules:**
- Blocks can be planned for **today through 28 days ahead**. Past days are read-only.
- One person's blocks never overlap each other. The drop preview turns invalid over another block.
- A block may overlap a meeting; it is then tinted and offers "Move after".

### 1.5 Task queue panel (code name: Focus panel)

- **Heading:** "Task queue"
- **Hint:** "⋮⋮ Drag a card onto the calendar · click to open"
- **Groups** (in this order):
  1. **Overdue**, in red
  2. **Due today**
  3. **Due this week** (through Sunday)
  4. **Assigned backlog** (no due date, or due after this week)
- **Card limits:** 5 cards per group, then "+ N more". The server sends at most 50 per group.

**Card content:**

```
⋮⋮  ● Due-today demo task 4            [📅]
    E-pos_System
    Due 15 Oct · 4h left · planned Thu +1
```

- **●** — a red dot for high or critical priority.
- **Project name.**
- **Due:** "Due 15 Oct", "Today" or "No due date".
- **Left:** "4h left" = the estimate minus the time logged **by everyone** on the task. Hidden when there is no estimate.
- **Planned:** "planned Thu +1" = the first planned day from today on, plus how many more days.
- **Fully planned cards** (planned time ≥ time left) sink to the bottom of their group and fade.
- **📅 Plan button** appears on hover or keyboard focus. It opens the day-and-time picker.

**Card interactions:**

| Action | Wide screen | Narrow (≤ 767px) |
|---|---|---|
| Click / Enter / Space | **Opens the task** | Opens the day-and-time **picker** |
| Press and move ≥ 4px | **Drag**. A ghost card ("title · 3h 30m · Drop on a time") follows the pointer, the source card dims and turns dashed, the cursor grabs, and the grid shows the drop preview. | No drag |
| 📅 Plan | Picker | Picker |

- **Picker:** day, start time and length, then Plan or Cancel. Days are limited to the plannable ones.

**Scope — which tasks appear:**
- Tasks **assigned to the user** (being a lead or owner is not enough), in **any project**.
- Only tasks that are not done: progress < 100%, status not "complete", not deleted.
- **Excluded:** meetings, done tasks, deleted tasks, and tasks assigned only to others.
- **Included today — confirm whether the redesign wants these:**
  - subtasks, as separate cards;
  - onboarding/offboarding checklist tasks (they live in the Office project);
  - tasks in archived projects.
- **Order within a group:** not fully planned first, then priority (Critical → Low), then due date, then title.

### 1.6 Weekend switch

- A small switch labelled "Weekend" (`role="switch"`). It appears only in Week view with the planner on.
- **Off:** only the legal entity's working days. **On:** all 7 days.
- The choice is remembered per browser (`localStorage planner.showWeekend`).
- With no schedule configured, all 7 days show either way.

### 1.7 Mobile

- Narrow screens: tapping a card opens the picker instead of dragging.
- On mobile the layout keeps its own padding (a bottom nav).

---

## 2. Where the Planner's numbers come from

### 2.1 Work window, per user per day

- **Which legal entity:** the employee's own, or the tenant's primary one when the employee has none.
- **Fields used:**
  - `work_start_time`, `work_end_time`, `break_duration_minutes`
  - `standard_working_days` (e.g. `[1,2,3,4,5]` = Mon–Fri)
  - `timezone`
- **"Configured" means:** the time zone is valid, start and end are both set, and start < end. Clock-in uses the same rule.
  - When the schedule is not configured: no grey shading, no load bar, and "Working hours not set".
- **Working minutes** = (end − start) − break, or 0 on a rest day.
- **Break placement:** the schedule has no break start time. The backend centres the break in the window, rounded to 15 min. It is not drawn any more.

### 2.2 Load

- **Planned for a day** = task blocks plus busy calendar time. Overlapping time is counted once.
- **"over"** = planned time above working time.

### 2.3 Office project

- **What it is:** every tenant has one hidden system project, "Office" (`system_purpose = 'office'`).
- **Who creates it:** the backend, automatically —
  - at API startup, for every tenant;
  - when an employee is onboarded or an access grant is approved;
  - when an onboarding or offboarding checklist is chosen;
  - on demand, when the first Office meeting is created.
- **What it holds:**
  - **Onboarding/offboarding checklist tasks** (`task_kind = employee_checklist`).
  - **Office meetings** — meetings in no other project, such as a standup or a company call.
- **How the UI treats it:**
  - The meeting form shows it as an "Office" choice, not as a project.
  - Board and Backlog hide "Create work item" for it.

---

## 3. "New event" form (calendar event)

**Opened by:** "New event", clicking or dragging on an empty slot, or clicking an existing event.

**Main parts:**
- Title.
- Date and time, with a **Full day** switch.
- Recurrence; saving or deleting a recurring event asks "this event" or "all events".
- Participants (employees and guests).
- An online meeting ("Add Teams meeting", "Add Zoom meeting", "Paste a meeting link").
- A conflict check.
- For invitees:
  - Accept or Decline;
  - Nominate replacement;
  - Request conflict resolution.

**Meetings-as-tasks rule (when meetings can be created):** a new event that gets **people or a meeting link** is no longer a plain event.
- The form offers **"Make it a meeting"**.
- It then hands over to the **meeting form**, carrying the title and time.
- So a personal event stays an event, and anything with attendees becomes a meeting work item.

---

## 4. Creating a meeting (work item)

### 4.1 Entry points (after PR #134)

| Where | How |
|---|---|
| **Work → Tree / Board / Backlog** | **Create work item** → pick category **"meeting"** → the same dialog turns into the meeting form |
| **Planner** | New event with people or a link → "Make it a meeting" → meeting form |
| ~~New meeting (Tree, Board)~~ | Removed |
| ~~Meeting (work item) (Planner)~~ | Removed |

### 4.2 Category switch inside "Create work item"

- **Where "meeting" is offered:** only while **creating** on Tree, Board or Backlog, and only while meetings can be written. In edit mode, and everywhere else, it is not offered:
  - dashboard quick-create;
  - drafts;
  - offboarding;
  - meeting action items.
- **Never pre-selected:** the form never opens on "meeting", even when it is the first category.
- **Choosing "meeting":**
  - the body swaps to the meeting form;
  - the **title, description and module carry over**;
  - the task footer hides, because the meeting form has its own Cancel and Create;
  - choosing another category swaps back.
- **Identification:** the "meeting" category is the system category with `systemKey = 'meeting'`. Each project has one.
- **After Create:** the dialog closes. Board opens the new meeting's drawer; Tree reloads its meetings.

### 4.3 Meeting form fields (create)

| Field | Notes |
|---|---|
| Project | "Office" (default) or one of the user's projects |
| Module | Required when a project is chosen |
| Title | Required |
| Agenda | Rich text |
| Date · Start · Minutes · Time zone | Minutes 15–480 in steps of 15; time zone defaults to the browser's |
| All day · Private | Toggles |
| Location | Free text |
| Repeats | None · Daily · Weekly · Bi-weekly · Monthly · Custom (RRULE) |
| Platform | No online meeting · Microsoft Teams · Zoom |
| Participants | Employees and guest emails |
| Bulk invite panel | Add a module's or project's people at once; warns above a provider's size limit (Teams 1000, Zoom 300) |
| Availability warnings | Leave, holidays and working hours of the people picked. Advisory only; it never blocks Create. |

**Outcomes:**
- **"Meeting created"**, or **"Sent for approval"** — a module member who is not its owner requests, and the owner approves.
- People who could not be invited are listed: "no longer active" or "not found".
- An **Office meeting** needs `calendar:write`. If the Office project is not ready yet, the API answers "being set up — try again in a moment".

### 4.4 Meeting after creation

- **Status:** follows the clock and is never set by hand — Scheduled → In progress → Completed. **Cancelled** is shown only in the Calendar.
- **Short id:** `<PROJECT>-M<n>`, e.g. `HWPORTAL-M4`.
- **Occurrence drawer:**
  - Edit; after the meeting ends, only its notes can be edited;
  - Move; Cancel meeting;
  - Accept / Decline;
  - Attendees: Add people ("Add to this occurrence only" or "Add to the series"), Remove;
  - Online meeting;
  - Attendance;
  - Action items (create tasks from the meeting).
- **Recurring edits:** ask for "this", "this and following" or "all". The repeat rule and time zone apply to the series only.

---

## 5. Meetings in Work (Board and Tree)

### 5.1 Board

- Each meeting is a card in the status column its time puts it in:
  - Scheduled → the first *not started* column;
  - In progress → the first *active* column;
  - Completed → the first *done* column.
- It moves on its own as the meeting starts and ends (re-checked every minute).
- It **cannot be dragged** and carries an **"M"** badge.
- **Cancelled** meetings are hidden.
- Column counts include meetings.
- **Filters:**
  - Module and search apply.
  - Sprint (other than All/Individual), category and priority filters **hide** meetings.
  - An assignee filter keeps them when it includes the viewer.

### 5.2 Tree

- A meeting row is laid out like a task row and indented at its tasks' depth:

```
[M] E2E clash mvqf2ilf  HWPORTAL-M4  Completed   10:15 AM – 10:45 AM   (DO)   2026-10-28   Accepted 👁
```

- **Columns:** name cell (M badge, title, short id, status) · time, in the progress column · **organiser avatar**, in the assignee column · date · my response and the 👁 view button.
- **Placement:** after the module's **own tasks**, before its sprints and sub-modules. A meeting belongs to a module, so its depth follows the module, not the sprint.
- **Organiser avatar:** needs backend PR #203 (`organizerEmployeeId`). Without it the column shows "—".

---

## 6. Open points for the redesign

1. **Task queue scope:** should subtasks, checklist tasks and archived-project tasks be shown?
2. **Break time:** it is not shown. Showing it truthfully needs a "break start" field in the legal entity settings (backend + Org settings).
3. **Holidays:** working days ignore public holidays in the Planner (the load bar and the Weekend switch). The availability warnings in the meeting form do consider holidays.
4. **Office project visibility:** it is hidden as a project. Decide whether users should ever browse it.
5. **Accessibility debt:** the touch-picker backdrop on the Planner page has 4 lint errors (`click` with no key handler, not focusable). This was there before.
6. **Flags:** decide the behaviour and copy for tenants with the Planner off, or with meetings off or read-only.

---

## 7. Code map (frontend, `src/app/modules`)

| Area | Path |
|---|---|
| Planner / Calendar page | `calendar/feature/calendar-page/` |
| Hour grid (week/day) | `calendar/ui/calendar-hour-grid/` |
| Month grid | `calendar/ui/calendar-month-grid/` |
| Task queue panel | `calendar/ui/planner-focus-panel/` |
| Day-and-time picker | `calendar/ui/planner-block-picker/` |
| Event form | `calendar/ui/calendar-event-form-modal/` |
| Planner state | `calendar/state/planner-layer.store.ts`, `planner-drag.service.ts` |
| Grid maths | `calendar/utils/planner-grid.util.ts` |
| Meeting form | `work/meetings/ui/meeting-form-modal/` |
| Meeting drawer | `work/meetings/feature/meeting-occurrence-drawer/` |
| Meeting card / tree row | `work/meetings/ui/meeting-card/` |
| Work item form | `work/ui/task-form-modal/` |
| Tree / Board / Backlog | `work/feature/milestone-tree-tab/`, `task-board/`, `task-backlog/` |

**Backend:**
- `Application/Features/WorkManagement/Planner/` — `PlannerService`, `PlannerFocus`, `PlannerWorkWindows`.
- `.../Meetings/` — `MeetingPlacementResolver`, `MeetingReadService`, `MeetingsOptions`.
- `Infrastructure/Services/CoreHr/OfficeProject*`.
