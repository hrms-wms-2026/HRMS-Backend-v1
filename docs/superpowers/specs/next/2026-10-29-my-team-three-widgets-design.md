# My Team — Team Progress upgrade, Today's Team Schedule, Working / Break / Leave

**Date:** 2026-10-29 · **Repos:** HRMS-Backend-v1 + Hrms--Web-application---front-end---v1 · **Status:** design, not built

Source brief: the user's "My Team Dashboard — Three Widgets | Implementation Master Prompt" plus three mockups
(Team Progress, Today's Team Schedule, Working / Break / Leave). The mockups show content per size, not sizes or donut
sizes: the dashboard standard below wins wherever they disagree.

---

## 1. Intent

A manager opening **My Team** should answer, in seconds:

1. **Team Progress:** how much of the work I lead is done, did people follow their plans, what was worked without a plan,
   and who carries the most load?
2. **Today's Team Schedule:** what is each of my people planned to do today, what are they doing now, how far through the
   plan are they, and what happened off-plan?
3. **Working / Break / Leave:** who is working, on break or on approved leave right now, since when, and for how long?

Success = the numbers are honest (every percentage names its denominator, "no data" never shows as 0%), they match the
screens they mirror, and each widget is useful at 2x2 and grows by adding detail.

## 2. Decisions taken with the user (2026-10-29)

| # | Decision |
|---|---|
| D1 | Exactly three widgets. **Team Progress** is upgraded in place (key `team-progress`, title unchanged). There is no team "Weekly Work Progress" widget: its ideas (planned vs actual, unscheduled, week/month) move into Team Progress. The My Work widget `today-total-time` is **not touched**. |
| D2 | **Working / Break / Leave replaces Team Status**: same widget key `team-status`, same permission gate and saved layouts; new title, content and popup. |
| D3 | **Today's Team Schedule** is a new My Team widget (new key `team-schedule`). The My Work `todays-schedule` widget is not touched. |
| D4 | Minimum size **2x2** for all three; the mockups' 1x1/1x2 "small" views map onto our 2x2 mini view. |
| D5 | Ranking in Today's Team Schedule uses the **60/40 execution score** (section 6.3). Workload ranking (Team Progress) and execution ranking (Schedule) stay separate. |
| D6 | The dashboard widget standard applies unchanged: memory `feedback-hrms-dashboard-widget-ui-ux-standard` and the visual tokens in `reference-hrms-dashboard-widget-visual-style` (2x2 min, one 140 px donut, host-size views, Team-Progress popup shell, KPI tiles row, CSV export, `styleUrl` only, zero template warnings). |

## 3. Non-goals and anti-goals

- No change to the Planner workflow, task clocking, meetings rules, attendance rules or leave approval.
- No manager edit of anyone's plan (Planner v1 is read-only for managers): no "Add schedule" button.
- No new chart library; no second calendar/planner inside a widget (a read-only timeline only).
- No change to how Team Progress's existing 4 statuses and Total Allocated are calculated.
- **Anti-goals (a failure even if it "works"):** adding allocated estimate hours to clocked minutes; calling *Not yet tasked*
  "Unscheduled"; showing 0% / 100% where there is no denominator; counting a scheduled block as hours worked; marking
  someone Working because they have a schedule; leaking a private meeting title or a leave reason; showing a no-signal
  employee as Working, Break or Leave.

## 4. What exists today (audit, verified on `chore/uat-hardening` = `origin/development` + local work, 2026-10-29)

### 4.1 Backend

| Area | Where | Facts that shape the design |
|---|---|---|
| Team Progress data | `Features/WorkManagement/Leadership/*`; widget provider `TeamProgressWidgetDataProvider` (mirrors `WorkLeadershipController.LedProgress`) | **"Team" = the Modules the caller leads** (`IWorkLeadershipService.ResolveLedScopeAsync`), not the reporting line. Totals come from trigger-maintained `wm_module_hour_summaries` via `LedHourRollup`. Buckets: Completed = status `MarksTaskComplete` or progress ≥ 100; In progress = progress > 0; Not started; Not yet tasked = allocation not given to tasks. |
| Team Progress detail | `GetLedPeriodProgress` (week/month, inclusive due-date window), `GetLedEmployeeHours` (paged, shared tasks counted in full per assignee + `SharedHours`), `GetLedProgressTasks`, `GetLedWeeklyCompletion` (ISO weeks, UTC `CompletedAt`, 10 weeks default) | Week/Month and the Employee Breakdown **already exist** in the popup. Dates are **UTC** dates by deliberate V1 choice (`LedProgressPeriod.UtcToday`). Detail reads are top-level tasks only. |
| Planner v1 | `Features/WorkManagement/Planner/*`, table `planned_task_blocks` | In `development`. v1.1 (`origin/feature/planner-calendar-merge`) is **not merged**. Flag `Planner:Enabled` defaults **false**. Manager view = any ancestor in the reporting line, private meeting titles masked. Rules in `PlannerActuals` (day bounds, task minutes clipped to now, meeting minutes in day, all-day = 480 min, meeting actual only after it ends, attendance minutes). |
| My Work weekly progress | `Features/WorkManagement/WeeklyProgress/*` | Reuses `PlannerActuals` + `ExpectedWorkdayCalendar` (working weekdays, holidays, hire/termination) + leave hours to get expected minutes per day. Reusable for **capacity**. |
| Clocking | `TaskClockingSession` (`TaskId, EmployeeId, ClockInAt, ClockOutAt?, DurationMinutes?`) | At most one **open session per task** (unique filtered index); an employee *can* have open sessions on two tasks at once. Indexes: `(tenant, task, clock_in_at)`, `(tenant, employee, clock_in_at)`. |
| Meetings | `IMeetingRepository.ListEmployeeDayMeetingsAsync` (one employee) | Materialized occurrences only; organizer/attendee/override rules; `Declined`, `Cancelled`, `IsPrivate`. No multi-employee version yet. |
| Team Status | `GetCoveredTeamTodayQuery` in `TimeAttendance/Queries/AttendanceReadHandlers.cs`; `TeamStatusMapper`; `AttendanceDayStatusResolver`; widget provider `TeamStatusWidgetDataProvider` | Population = `attendance:read` **coverage** (`IEmployeeAuthorityResolver`, `TimeTrackingRead`), caller excluded. Batched reads of records, breaks, approved leave for the legal-entity work date. Statuses: working, on_break (incl. over-break), clocked_out, absent, not_started, on_leave, not_scheduled + late overlay + attention. **Leave masking**: an unauthorized viewer sees `on_leave` as `absent`. Legal entity `BreakDurationMinutes` and the attendance schedule are available here. |
| Hierarchy | `IEmployeeHierarchyClosureRepository.GetDescendantEmployeeIdsAsync` | Transitive reports of a manager. |
| Widget gate | `DashboardWidgetAccess` | `team-status` + `team-attendance` need `CanViewPeopleStatus`; `team-progress` needs `LeadsWork`. |

### 4.2 Frontend (`src/app/modules/dashboard`)

| Piece | Where | Reuse |
|---|---|---|
| Registry | `widgets/widget-registry.ts` | `team-status` → new component; add `team-schedule`. |
| Team Progress card | `feature/team-progress-card` (views mini, xs, medium, wide, tall, large, maximum via `utils/team-progress-layout.util.ts`) | Extend in place. |
| Team Progress popup | `feature/team-progress-detail-dialog` (period / project / module selects, Status Distribution, Weekly completion, Employee Breakdown, Overdue) | Extend in place. |
| Team Status card | `feature/team-status-card` | Replaced by the new Working / Break / Leave card under the same key. |
| Reference widget | `feature/team-attendance-card` + `team-attendance-detail-dialog` + `utils/team-attendance-layout.util.ts` | Copy its structure for the two new cards. |
| Charts / UI | `ui/weekly-completion-chart`, `ui/attendance-day-chart`, `ui/daily-work-chart`, `app-kpi-tiles`, `app-modal`, `app-widget-card`, `app-popout-header-actions`, shared `app-data-table`, `utils/host-size.util.ts`, `utils/dashboard-donut.util.ts` | Reuse; no new chart library. |

FE branch state: `origin/development` is 45 commits ahead of `chore/uat-hardening`, which also holds the uncommitted
Quick Actions corner work. Settle that before the feature branch is cut (section 10).

## 5. Shared metric rules

### 5.1 Three kinds of hours (never added together, never on one axis)

| Name in UI | Meaning | Unit | Source |
|---|---|---|---|
| **Allocated / estimate hours** | Task estimates and module allocation, exactly as Team Progress computes them today | decimal hours | `wm_module_hour_summaries`, led-progress reads |
| **Planned (scheduled) time** | Planner blocks (+ eligible meetings where stated) | minutes | `planned_task_blocks`, meeting occurrences |
| **Worked (clocked) time** | Task clocking sessions | minutes | `task_clocking_sessions` |
| **Attendance time** | HR worked time, breaks excluded | minutes | `attendance_records` (context only) |

### 5.2 Task-time attribution (new shared pure service, Application layer)

`TaskTimeAttribution` (name is the builder's call), pure and unit-tested, next to `PlannerActuals`. For one employee and a
window `[from, to)`:

1. Blocks of the employee in the window (not deleted), clipped to the window.
2. Clocking sessions of the employee in the window: open session → end at `min(now, to)`; clip to the window; drop
   negative/zero.
3. Per task, **merge overlapping sessions** of the same task (an instant counts once). Overlap between *different* tasks is
   kept (it matches today's clocking rules) but flagged as a `parallelClock` data-quality count.
4. Per task: `scheduledMinutes` = minutes of (merged sessions ∩ merged blocks of the **same task**);
   `outsideSlotMinutes` = clocked minutes of a task that *has* a block in the window but outside it;
   `noPlanMinutes` = clocked minutes of a task with no block in the window.
   `unscheduled = outsideSlot + noPlan`; `scheduled + unscheduled = clocked` always.
5. Returns per-task and total figures plus the segments (task, start, end, kind) for drill-down.

Example (from the brief, must be a unit test): block 09:00–10:00, clocked 09:20–10:20 → 40 scheduled, 20 outside slot;
task B clocked 11:00–11:45 without a block → 45 no-plan. Clocked 105 = 40 + 65.

The Planner's own `PlannerDayResponse` numbers stay exactly as they are (its block actual = whole-day task minutes).

### 5.3 Percentages, periods, comparisons

- Every percentage names its denominator in a tooltip; zero denominator → "—" plus a reason ("No plan", "Nothing
  allocated"), never 0% or 100%. Use the existing `formatPercent` so small values read "0.5%".
- **Allocation completion %** = completed estimate hours ÷ allocated hours (existing Team Progress rule).
- **Schedule coverage %** = scheduled-attributed minutes ÷ planned task minutes; display capped at 100%, with overflow
  shown as unscheduled minutes, not hidden.
- Weeks are ISO (Monday–Sunday), half-open internally. Allocation figures keep their existing **UTC** dates (unchanged).
  Time figures are bucketed by **each employee's local date** (`ICalendarTimezoneResolver`, as the Planner does).
- Comparisons use the **same elapsed span**: this week to date vs the same weekdays/time of last week; month to date vs
  the same number of elapsed days of last month. Rates compare in percentage points (pp); hours/counts in %; zero prior →
  "New".
- "No data" (source unavailable, Planner off) is different from a true 0 and renders differently.

### 5.4 Planner flag

When `Planner:Enabled` is false, every planned/scheduled/unscheduled figure is replaced by a "Planner is off" note.
Clocked time still shows (it does not depend on the Planner) but the schedule split is hidden.

## 6. Widget specs

### 6.1 Team Progress (upgrade, key `team-progress`)

**Scope stays the led scope.** Time figures cover the clocking sessions and Planner blocks **on tasks inside the led
Modules** (whole subtree, top-level and subtasks, meetings excluded) and the project/module filters narrow them the same
way they narrow allocation. People in the ranking = assignees of in-scope tasks plus anyone with blocks/sessions on them.

**Kept as is:** 4 status KPIs + Total allocated, Status Distribution donut, weekly completion chart, Employee Breakdown
(search/sort/page, shared-task wording), Overdue list, project/module filters.

**Added:**

1. **Week ⇄ Month switch on the card** (standard and larger views; hidden in mini/xs like the Team Attendance select).
   Default Week. It drives the period KPIs (existing `led-progress/period` maths), the trend (weeks of the month or days of
   the week) and the new sections.
2. **Planned vs Worked band** (minutes, separate visual from allocation): planned task time, worked inside plan, worked
   outside slot, worked with no plan. Coverage % beside it.
3. **Unscheduled work** callout: total minutes + number of people; popup panel lists person, task, time range, minutes,
   kind ("No plan" / "Outside slot"). Wording: "Worked without a matching Planner slot." Amber, not red.
4. **Workload ranking** (top N on card, full table in popup): rank by **planned commitment minutes** in the period
   (in-scope task blocks), ties by worked minutes then name. Columns: planned, worked, unscheduled, capacity utilization
   (= planned ÷ expected minutes from `ExpectedWorkdayCalendar` minus leave; only when the legal entity has working hours,
   else "—"; above 100% kept and flagged). Tooltip: "More workload is not better performance."
5. **As-of** label from the store's load time; current period only is live.

**Card views** (extend `team-progress-layout.util.ts`, keep its breakpoints style): mini = donut + allocation % (as now);
xs = donut over figure; medium = KPI tiles + top 3 workload; wide = KPIs + trend + unscheduled callout; large/maximum =
KPIs, distribution, trend, planned-vs-worked band, top 5 workload. Exact allocation of sections per view is the
builder's call within the standard.

**Popup:** existing sections + a "Planned vs worked" section (chart of planned/worked/unscheduled per day or week),
Unscheduled work panel and Workload ranking table (CSV export). Overview-first, no tabs.

Acceptance: 826 + 2,291 + 2,316 + 14,767 = 20,200 still reconciles; 90 min clocked on a task with no block shows +90 min
unscheduled and changes no allocation figure; Month changes the ranking, not just the label; a shared task does not
inflate team allocation.

### 6.2 Today's Team Schedule (new, key `team-schedule`)

**Population:** the caller's transitive reports (`GetDescendantEmployeeIdsAsync`), the same rule the Planner uses for
manager reads; inactive/terminated excluded. Gate: available when the caller has at least one report; plus the Planner
flag note when off. Private meetings are always "Private meeting".

**Per employee for their local today:** blocks, eligible meetings (not cancelled/declined; cancelled shown faintly but
outside every count), clocking sessions, task statuses, plus the attendance state from section 6.3 (shared logic) as a
chip.

**Item status:** tasks — Completed (task status `MarksTaskComplete` or progress ≥ 100), In progress (open session or
progress > 0), Pending; meetings — Upcoming, In meeting, Finished. A block whose time has started without a clock shows
"Scheduled · not clocked", never "Behind" before its start.

**Counts (no overlapping pie):** tiles for Tasks planned, Completed, In progress, Pending, Meetings, Unscheduled people,
Conflicts. The donut shows **task** status only; meetings are a separate count.

**Conflicts:** block ∩ meeting (not all-day), parallel open clocks on two tasks. Warning only.

#### 6.3 Execution score (D5)

- `itemCompletion` = completed eligible items ÷ eligible items **due by now** (an item counts once its start ≤ now).
- `timeCoverage` = min(100, scheduled-attributed task minutes ÷ planned task minutes elapsed by now).
- `score = 0.6 × itemCompletion + 0.4 × timeCoverage`, labelled **"Execution score (today)"**, with both parts shown.
- Not ranked (with reason) when: no eligible item yet, no planned task minutes, approved leave covering the day, or Planner
  off. If only one part is computable, show it but give no composite.
- Ties: itemCompletion, then timeCoverage, then name. Unscheduled time is shown next to the score, never subtracted.
- Rows re-sort only on a data refresh, not on every clock tick.

**Card views:** mini = task-status donut + "N people with unscheduled work"; medium = people rows (avatar, chip, mini
plan bar, completion %); large = timeline (employee × work hours, now-marker, striped unscheduled segments, conflict
mark). **Popup:** date navigator (today and past days; future disabled — Planner v1), KPI tiles, scrollable timeline with
sticky names, planned/actual/score columns, Execution ranking, Unscheduled work panel, Conflicts panel, CSV export.

Acceptance: an employee with no plan who clocks 2 h appears with "Unscheduled 2h" and "No plan" (not 0%); 10:00–11:00
block with 10:20–11:20 clock → 40 / 20; two blocks of one task never double the actual; private titles never leak; leave
→ "Not ranked"; future items do not lower the score.

### 6.4 Working / Break / Leave (replaces Team Status, key `team-status`)

**Population and states:** exactly Team Status's (`GetCoveredTeamTodayQuery`, same coverage, same masking). Mapping:
working → **Working** (blue), on_break → **Break** (orange), on_leave → **Leave** (red). clocked_out, not_started,
absent, not_scheduled go to one neutral line ("4 not clocked in · 2 clocked out · 1 not scheduled"), never a 4th colour.
Late stays as a small tag on the Working/Break row. The donut is drawn over the three states with the centre "N of M".

**Added fields per member** (extend the existing response; no new table): `stateSince` (clock-in time, or the open break's
start), `breakRemainingMinutes` (legal entity `BreakDurationMinutes` − break used today, only when configured),
`shiftEndsAt` (from the attendance schedule, only when known), `attendanceMinutes` (as `PlannerActuals.AttendanceMinutes`).
Leave shows "All day" / "Half day" / its interval, never a countdown. The client ticks elapsed time between refreshes;
counts change only on refresh.

**Card views:** mini = donut + 3 counts; medium = 3 counter pills + 4–5 rows (avatar, chip, since, time left bar only
when a denominator exists); large = counters + table. **Popup:** KPI tiles (Working, Break, Leave, + neutral not-clocked
count), donut, **Working hours trend** for the week from attendance worked/break minutes (reuse
`team-attendance-period-reader` style batched reads, `attendance-day-chart` style chart), full member table with CSV
export (no leave reason/type for unauthorized viewers; leave type only where Team Status shows it today).

Everything Team Status shows today that is not in the new design (attention list, "needs attention" count) moves into the
popup's member table as an attention column, so nothing is lost.

## 7. Data architecture

**Decision: no new tables in this release; batched live reads.** Reasons: today's status and today's schedule are small
(one day × team size) and already served live; Team Progress's period figures cover one week/month of sessions/blocks on
in-scope tasks, which the existing `(tenant, task, start)` / `(tenant, employee, clock_in_at)` indexes serve. Every read
is set-based (one query per source per request), never per employee.

New repository methods needed (builder names them):
- Blocks and sessions for a **set of task ids** in a window (Team Progress) and for a **set of employee ids** in a window
  (Schedule).
- Meetings for a **set of employees** in a window (batched version of `ListEmployeeDayMeetingsAsync`, same rules).
- Timezones for a set of employees (batched resolver call, or one query over employee/legal-entity zones).

**Performance gate:** measure the Team Progress month read on the demo tenant (dapi) and a seeded large led scope. If p95
> 500 ms, add an `employee_work_day` summary (tenant, employee, local_date: planned, scheduled, outside-slot, no-plan,
clocked minutes) maintained by the existing background-job conventions, with RLS coverage migration — as a follow-up
design, not in this release.

Migrations in this release: none expected. An index on `planned_task_blocks (tenant_id, task_id, start_at)` only if the
query plan needs it (then with the usual migration report; the developer applies it).

## 8. Security

- Team Progress: unchanged led-scope rule; filters only narrow it.
- Schedule: reporting-line descendants only; private meeting titles masked in card, popup, tooltips and CSV.
- Working / Break / Leave: unchanged coverage + leave masking; no leave reason anywhere.
- New detail queries re-check the same gates as their parent widget (`RequirePermission` per CLAUDE.md), and every new
  read is tenant-scoped.

## 9. Tests

- Unit: `TaskTimeAttribution` (brief examples, open session, cross-midnight, two blocks of one task, parallel clocks,
  deleted blocks), execution score (each "not ranked" reason, future items, ties), status mapping to 3 states + neutral,
  break remaining / shift end with and without configuration, comparison windows (same elapsed span, zero prior).
- Handler tests for each new/extended query: scope, filters, Planner off, empty team, masking.
- Integration (Docker): the batched repository reads against seeded data.
- FE: layout util specs (view per size), card specs per view, popup specs, `npm run check:warnings`, and a Playwright pass
  at 2x2 and at large sizes on dapi.onexso.com:4200 (user signs in).

## 10. Delivery order

1. Branches: settle the FE Quick Actions work and bring `development` in, then `feature/my-team-three-widgets` in both
   repos.
2. Shared attribution service + tests.
3. Working / Break / Leave (smallest, reuses Team Status) — BE fields, FE card + popup.
4. Today's Team Schedule — BE query + widget registration (backend registry, access, provider), FE card + popup.
5. Team Progress upgrade — BE period time figures + workload, FE card views + popup sections.
6. Full gates both repos, browser pass, summary report in `docs/reports/work-management/`.

## 11. Calls made by the designer (check these)

- Team Progress time figures use the **led scope's tasks** (not the reporting line) and include subtasks; meetings are
  left out of Team Progress workload.
- Today's Team Schedule population = **transitive reports** (Planner's manager rule), not attendance coverage.
- Late becomes a tag; clocked out / not started / absent / not scheduled collapse into one neutral line.
- No new tables now; a summary table only if the performance gate fails.
- Allocation dates stay UTC (existing); time figures use employee-local dates.
