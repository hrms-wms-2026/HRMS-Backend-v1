# My Team widgets: design match, size ladders, and planned-vs-actual charts

**Date:** 2026-11-02
**Branch:** keep working on `feature/my-team-three-widgets` in both repos. That work isn't pushed or browser-passed yet, and this pass finishes it.
**Builds on:** `2026-10-29-my-team-three-widgets-design.md`, the summary `docs/reports/work-management/2026-10-29-my-team-three-widgets-summary.md`, and the dashboard widget standard (see "Rules that still apply").
**Reference images:** the three design previews the user shared on 2026-11-02 ("Team Progress Widget – Updated Design Preview", "Today's Team Schedule – Design Preview", "Working / Break / Leave Status – Design Preview"). They are saved in `docs/design-notes/my-team-widgets-2026-11-02/` (`team-progress-preview.webp`, `team-schedule-preview.webp`, `working-break-leave-preview.webp`).

---

## 1. Why

The three widgets work and their numbers are right, but they look much plainer than the design previews. Data is shown as-is: pills, plain text rows and plain tables, where the designs use KPI tiles, progress bars, legends and charts.

The user's two complaints:

1. **"Widget size increase panum pothu detail athikamakanum. Component-ah space-ikku etha marri adjust panna kudathu."** A bigger card must show more information. It must not just stretch or space out what is already there. Today Working/Break/Leave and Today's team schedule have only three coarse views. In medium they always show 5 rows, so the space under them sits empty. In large they put everyone in one table or timeline that scrolls inside the card. Both break the standard. Team Progress already follows it.
2. **Planned time vs actual work time must be shown as charts and summaries**, both for today and for the week. Today the comparison exists only as numbers or a thin band.

**Success:** with Planner data present, each card at each size looks like the matching panel in the preview images, gains detail as it is resized from 2x2 up to 12x8, and never scrolls. The user can see planned vs actual time per person for today and per day for the week, as charts with summary tiles.

## 2. Goals

- **G1 Visual match.** All three cards and their popups take the look of the preview images: icon KPI tiles with a %, coloured status chips, per-row progress bars with %, legends, labelled timeline blocks and insight banners. Use the existing visual tokens.
- **G2 Size ladders.** Working/Break/Leave and Today's team schedule get a real ladder: mini, xs, then standard views whose sections are budgeted by measured height. This replaces the fixed 5 rows and the scrolling large view.
- **G3 Daily planned vs actual.** A per-person chart for today: planned (tasks + meetings) next to actual clocked time, with actual split into in plan / outside slot / no plan. It appears on the schedule card at larger sizes and in the schedule popup.
- **G4 Weekly planned vs actual.** A Mon–Sun chart of planned vs actual per day, with a coverage % line and summary tiles (planned, actual, coverage, unscheduled, each vs last week). There is also a per-person weekly table. It appears in the schedule popup (Day/Week switch) and on the schedule card at the largest size. Team Progress gets the same chart for its own scope, from data it already loads.
- **G5 Working-hours trend.** The Working/Break/Leave popup shows working, break and leave stacked per day, with a team-average line. The data already exists.
- **G6 Bug fix.** Rows in the Working/Break/Leave list are cut off and scroll in the medium view (seen in the user's 2026-11-02 screenshot). G2 fixes this.

## 3. Non-goals

- **Month view in the schedule popup.** Only Day and Week.
- **Project/module filters in the Working/Break/Leave popup.** Presence isn't scoped to projects. The schedule popup gets a status filter and search, not project/module filters.
- **"Add Schedule" buttons.** A manager can't create Planner blocks for someone else yet. Show the unscheduled list without an action button.
- **Real face photos.** Keep `app-avatar` with the photo when one exists, otherwise initials.
- **Changing any existing figure's formula.** Allocation, execution score, coverage and attribution stay as the 2026-10-29 spec defines them. This pass changes presentation and adds read endpoints only.
- **New tables or migrations.**

## 4. Anti-goals (a failure even if it works)

- A card that scrolls inside itself at any size.
- A donut, chart or tile that gets bigger because the card got bigger. Bigger cards add sections and rows, never bigger graphics. The donut stays `DASHBOARD_DONUT_PX` (140).
- Empty space under the content in any view. If rows run out, the next section takes the space, or the view drops down a step.
- A chart that invents numbers when the Planner is off. Planner-off views must say so and show clocked time only.
- Numbers on a card that differ from the same numbers on the page or popup it mirrors.

## 5. Rules that still apply

From the dashboard widget standard (memory `feedback-hrms-dashboard-widget-ui-ux-standard`, reference implementation `team-attendance-card` + `utils/team-attendance-layout.util.ts`):

- The view comes from the card's own size (`injectHostSize()`), with about 20px hysteresis.
- Thresholds and section heights live in `utils/<widget>-layout.util.ts`, as a classify/resolve pair plus a `plan…Layout(view, width, height)` budget. The section heights are **measured in the browser**, not guessed.
- Grid: 96px rows with a 12px gap. A column is about 97px wide at 1440px and about 140px at 1920px.
- Secondary controls are hidden in mini/xs.
- Percentages use `formatPercent`.
- Popup: the `app-modal size="2xl"` shell with its own header, one row of `app-kpi-tiles`, ranked tables with a # column, and CSV export where there's a table.
- Charts are hand-built in HTML/SVG like `ui/working-hours-chart` and `ui/weekly-completion-chart`. No chart library.
- Run `npm run check:warnings` before every FE commit.

## 6. Working / Break / Leave

### 6.1 Size ladder

The views and what each adds. "Budget" means the section appears only if its measured height fits; otherwise it is left out, never squeezed.

| View | When | Shows |
|---|---|---|
| **mini** | height < ~260 | Narrow: donut (centre: total, "Team members") beside the three counts. Wide (≥ ~320 body width): stacked bar + legend + counts. Plus a one-line status pill, e.g. "8 employees are currently working ›", that opens the popup. |
| **xs** | narrow (< ~370), taller | Donut over the legend with counts, the status pill, then person rows as the height allows. |
| **standard** | everything else | Sections in priority order, each added while it fits: **(1)** KPI tiles: Working / On break / On leave, each with icon, count and % of team. **(2)** Person rows, at least 3: avatar, name, coloured state dot + label, start time, "2h 30m left" in the state colour, a bar, and %. **(3)** At ≥ ~640 width the rows become table columns: # · Employee · Status chip · Start · Elapsed · Remaining · Today hours ("6h / 8h") · Progress bar + %. Columns drop right-to-left as width shrinks. **(4)** At tall heights, the week's working-hours trend panel (the G5 chart) once at least 5 rows are shown. **(5)** Any leftover height goes to more rows. |

When there are more people than rows: a "View all N ›" link. The card itself never scrolls.

### 6.2 Data

- Everything except "Today hours x / y" already comes from `team/today`.
- **Backend addition:** add `ExpectedMinutes` (int?) to each `team/today` member. It is the legal entity's work window for that date, the same window `ShiftEndsAt` already reads. It is null when no working hours are configured and 0 on leave. Today-hours progress = `attendanceMinutes ÷ expectedMinutes`, capped at 100% for the bar but shown uncapped in text.

### 6.3 Popup

Layout as in the preview:

1. **KPI tiles:** Working · On break · On leave (each with %) · Avg working time (from the working-hours endpoint, vs last week) · Attendance rate (vs last week).
   - **Attendance rate:** read it from the same source as the Team Attendance widget, so the two widgets agree. If that source can't be called for this population without a new query, show this tile only when it can and say so in the plan.
2. **Status distribution donut** (140) with counts and %.
3. **Working hours trend:** Mon–Sun stacked working / break / leave-count marker, with a dashed team-average line (`workedMinutes ÷ peopleWithRecord`).
   - "vs last week" uses a second call with the previous `weekStart`.
4. **Employee status table:** the columns from 6.1 (3), plus Export (CSV, kept).
5. **Insight banners** (only when the count is > 0): "N employees are currently on leave" (names), "N on break" (names), "N working · x% of team".

## 7. Today's team schedule

### 7.1 Size ladder

| View | When | Shows |
|---|---|---|
| **mini** | height < ~260 | Planner on: donut by item status (Completed, In progress, Pending, Meetings; centre: total items) + legend. Wide: stacked bar + legend. Below it, the alert pill "N employees have unscheduled work ›". Planner off: clocked total + "Planner is off" pill. |
| **xs** | narrow, taller | Donut over the legend, an avatar stack (first 5, "+N"), the alert pill. |
| **standard, narrow** (< ~640) | | **(1)** Person rows: avatar, name, presence dot + label, a segmented plan bar (task blocks coloured by status, meetings purple, breaks amber, unscheduled hatched red), completion %. **(2)** At taller heights, the daily planned-vs-actual chart (7.3). |
| **standard, wide** (≥ ~640) | | **(1)** A labelled timeline: hour header; per row the avatar, name and presence; blocks labelled "Task", "Meeting" or "Break" when the block is ≥ ~48px wide, otherwise colour only with a tooltip; "No scheduled work" as a hatched strip; a now-line; a legend under it. Rows are budgeted. **(2)** At taller heights, the daily planned-vs-actual chart. **(3)** At ≥ ~1150 wide and tall, a right-hand side column: summary donut, unscheduled work (top 3), conflicts (top 3). **(4)** At the maximum size (about 12x8), the weekly chart (7.4) under the timeline. |

The planner-off standard view shows per-person bars of clocked time vs the expected work window, plus the existing note. It shows no timeline and no plan figures.

### 7.2 Data for the timeline

- Items, unscheduled segments, conflicts and presence already come from `planner/team`.
- **Backend addition:** add `Breaks: [{ start, end }]` to each member. These are today's break intervals from attendance, read the way `TeamPresenceMath` reads the open break, and only for people the viewer covers for attendance (the presence-chip rule).

### 7.3 Daily planned vs actual (G3)

- **Chart:** one row per person. A light bar for planned (task + meeting minutes). Over or under it, the actual clocked bar split into in plan / outside slot / no plan, using the existing colours. Then the completion %.
- **Order:** the backend's member order.
- **Summary tiles above the chart:** Planned · Actual · In plan % (scheduled ÷ clocked) · Unscheduled.
- **Data:** no backend change. Every figure is already on each `planner/team` member.

### 7.4 Weekly planned vs actual (G4)

- **New endpoint:** `GET api/v1/planner/team/week?weekStart=YYYY-MM-DD`.
  - Same gate and same population as `planner/team` (transitive reports through `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`; never the closure repository).
  - `weekStart` must be a Monday and not in the future. Default: the viewer's current week.
  - Each person's minutes go to **their own** local days, as in the Team Progress time window.
- **Response:**
  - `plannerEnabled`
  - `weekStart`, `weekEnd`
  - `days[7]`: `{ date, plannedTaskMinutes, plannedMeetingMinutes, clockedMinutes, scheduledMinutes, outsideSlotMinutes, noPlanMinutes }`
  - `totals` (same fields + `coveragePercent`)
  - `previous` totals for the same elapsed span of the week before
  - `members[]`: `{ employeeId, displayName, avatarFileId, plannedMinutes, clockedMinutes, scheduledMinutes, unscheduledMinutes, coveragePercent }`
- **How to build it:** reuse `ITeamTimeReadRepository` (batched blocks, sessions, meetings) and `TaskTimeAttribution` per person per local day. Coverage uses planned time **elapsed so far**, the same rule as Team Progress.
- **Planner off:** plan fields are 0, coverage is null, clocked still shows.
- **Performance gate:** p95 under 500 ms on dapi for the full team, measured like the 2026-11-02 Team Progress timing.
- **Chart:** Mon–Sun grouped bars, planned (light) next to actual (stacked in plan / outside slot / no plan), a coverage % line on a right axis, and today marked. Future days show planned only.
- **Summary tiles:** Planned · Actual · Coverage · Unscheduled, each with "vs last week".
- **Per-person weekly table:** # · Employee · Planned · Actual · Coverage bar + % · Unscheduled. Ranked by planned, then actual, then name (the workload order). CSV export.

### 7.5 Popup

- **Header:** the date navigator (kept), a **Day / Week** switch, a status filter (All / Working / In meeting / On break / On leave / Unscheduled), a search box, and close.
- **Day:**
  - **KPI tiles:** Total items · Completed · In progress · Pending · Meetings (each with %).
  - **Main table:** # · Employee + presence · Workload level · Timeline · Planned · Actual · Completion bar + %.
    - Workload level = (planned task + meeting minutes) ÷ `ExpectedMinutes`: High ≥ 90%, Medium 60–90%, Low < 60%. This is my call; the thresholds are named constants.
  - **Right column:** schedule summary donut, unscheduled work (no action button), conflicts, and top workload (top 3).
  - The existing execution ranking stays, below the table.
  - Then the daily planned-vs-actual chart (7.3).
- **Week:** the 7.4 tiles, chart and per-person table. The date navigator steps by week.
- CSV export is kept for both modes.

## 8. Team Progress

It already has a ladder. This pass is visual, plus one chart.

- **Card tiles** take the design form: an icon badge on the right, the hours value, a label, and %. This covers Completed, In progress, Not started and Not yet tasked.
- **Weekly completion chart** (last 10 weeks): change the style to stacked bars by status with a completion % line on a right axis, if the weekly payload has per-status hours. If it has only completed vs allocated, keep those two series and add the % line. Don't add backend fields for this.
- **Planned vs worked:** in the tall, large and maximum views, the band becomes a Mon–Sun (week) or week-bucket (month) mini chart. It uses the `buckets` Team Progress time already returns: planned vs scheduled + unscheduled, with a coverage line. The band stays in the views too short for the chart.
- **Unscheduled work callout:** becomes a small panel with the top 3 people and red bars (`unscheduled` rows are already loaded).
- **Popup:**
  - KPI tiles gain "Total allocated" (`x% completed · N tasks`).
  - Add a **Project breakdown** table (# · Project · Allocated · Completed · Progress bar + %) from the project rows the card already has.
  - The employee breakdown gets search and paging (`app-data-table`).
  - The planned-vs-worked popup chart becomes the 7.4 chart style.
- **Period control:** keep the existing All time / This month / This week select. A Week/Month segmented switch like the preview's would conflict with All time. This is my call.
- **Ranking:** keep the workload ranking ("more workload is not better performance"). Changing it to the preview's "Top performers %" is **the user's call**, still open. If they choose it, the score would be Team Progress completion % per person, and the plan adds it as a separate part.

## 9. Planner off, empty and loading

- **dapi has `Planner:Enabled` off.** Every schedule and plan view must have a designed planner-off state (sections 7.1 and 7.4). It must not fall back to a bare list.
- **Browser pass:** run once with the Planner off as it is now, then turn it on locally and seed a day and a week of blocks and clock sessions for 5 or more reports. Seed through the app's own Planner API or a dev-only script kept out of `src/`. Remove the seed afterwards, or say where it lives.
- Every new section needs loading and empty states, sized the same as their loaded state so the budget doesn't jump.

## 10. Testing and verification

- **Layout utils:** spec tests for every threshold, the hysteresis, and the plan budgets: no section appears that doesn't fit, and leftover height always goes somewhere.
- **Components:** spec tests for each view's sections, planner-off states and the new charts (bars, line, tiles, aria labels).
- **Backend:** unit tests for the weekly composer (day split across timezones, elapsed-coverage, previous span, planner off), `ExpectedMinutes`, and `Breaks`. Integration tests for the new endpoint and the population rule. Run the architecture tests too (`MyTeamBoundaryArchitectureTests`).
- **Browser pass in Playwright:**
  - Force `gridArea` and check every size from 2x2 to 12x8 for each card.
  - At each size, the frame's overflow div must have `scrollHeight - clientHeight == 0`.
  - Take screenshots next to the preview images, with the Planner off and on.
  - The user signs in themselves.
- Gates: `verify.ps1` backend and frontend, `npm run check:warnings`, and eslint on changed files.

## 11. Left to the builder

- Exact pixel thresholds and section heights. Measure them; the numbers above are starting points.
- Icon choice for tiles, from the icon set already used by `app-kpi-tiles`.
- How to order the plan parts, as long as backend endpoints land before the FE parts that use them.
- Splitting the work across commits.
