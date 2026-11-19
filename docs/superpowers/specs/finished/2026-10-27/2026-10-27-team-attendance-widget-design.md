# Team Attendance Widget — Design

Date: 2026-10-27
Repos: HRMS-Backend-v1 + Hrms--Web-application---front-end---v1
Branch: `feature/team-attendance-widget` in both repos (backend cut from `fix/my-team-integration-tests`, frontend from `feature/customizable-dashboard`)

## 1. Intent

Managers on the **My Team** dashboard need one card that answers "is my team turning up?". It shows today and the week or month around it. The user supplied a reference image with four views. Its layout is the design source of truth. Its numbers are demo data.

- **Small:** big attendance %, ring with present/total, change vs the previous period.
- **Medium:** donut with eligible count, four status rows (Present/Absent/Late/On Leave), trend bars.
- **Large:** four pastel stat cards, stacked trend chart, "Today – Employee Status" list with View all.
- **Expanded popup:** five stat cards, daily trend, distribution donut, and a searchable employee × date matrix with Export.

The views are one widget, and the card picks the view from its own rendered size (same approach as Team Progress). The fourth view is a popup opened from the card.

## 2. Goals / non-goals / anti-goals

Goals
- One widget key `team-attendance`, My Team only, added through the existing registry/provider/store pipeline.
- A working **Weekly ↔ Monthly** switch in every view, with ‹ › navigation to earlier periods. Switching changes the real data: counts, rate, comparison, trend buckets, and the matrix columns.
- Numbers come from the existing attendance rules, so they match the Attendance Overview page.

Non-goals
- No new database table, trigger or background job (see §6).
- No change to other widgets, or to the shared components' existing behaviour.
- No date-range picker beyond week/month navigation. No history beyond what attendance rows exist.

Anti-goals (a failure even if it "works")
- A % that disagrees with the legend counts, or day statuses that differ from Classify / team-status for the same employee and day. The rate formula itself intentionally differs from the Overview card (§3).
- Today's not-yet-clocked-in people counted as Absent.
- Anyone outside the viewer's covered team visible in any count, row or export.
- A duplicate stat-card/donut/chart component when a shared one exists.

## 3. Decided by the user

- **Late counts as attended.** The headline is an **Attendance %**, where attended = Present + Late. For the mockup's numbers that is 96%, not 92%.
- **The detail table shows Late as its own status**, separate from Present.
- **Leave days are excluded from the rate denominator.** People on leave don't lower the rate. For example, 25 people with 1 on leave and 24 attended gives 24/24 = 100%. This deliberately differs from the per-employee Overview card's `Attended / WorkingDays`.
- Four views as in the image: three size-driven card views plus the Expanded popup.

## 4. Data semantics

Source of truth: `AttendancePeriodCalculator.Classify(AttendancePeriodData)`. It already handles the following, and the widget must not re-implement any of it:
- expected workdays: legal-entity working weekdays, minus holidays, clipped to hire date, termination date and today
- approved leave
- excused alert dates
- the clock-in policy late threshold
- missing clock-out

Each employee-day status it returns is mapped to a widget bucket:

| Classify status | Widget bucket | In rate denominator | Counts as attended |
|---|---|---|---|
| `present`, `missing_clock_out` | Present | yes | yes |
| `late` | Late | yes | yes |
| `absent` (past expected day, no clock-in, no leave) | Absent | yes | no |
| `leave` | On Leave | **no** | no |
| `none` on today's expected day, no clock-in yet | Not in yet (pending) | no | no |
| `off`, `none` outside employment/future | — (not counted) | no | no |

Clock-ins on non-expected days (`worked_on_non_working_day`, Classify's `!expected` branch) are **not** counted in the rate (*my call*), because they are not scheduled days. A clock-in on an expected leave day counts as attended, consistent with Classify.

- **Excused dates:** Classify applies excused dates only to the violation lists, not to `Days`. The widget follows the day list (*my call*: a late day that was excused still happened). If the builder finds that the Overview card's own numbers drop excused days, the widget must match the Overview.
- **Period rate** = Σ attended ÷ Σ (Present + Late + Absent) employee-days over the period. This is a weighted rate, not an average of daily percentages. A zero denominator gives `null`, shown as "—" (never 0% or NaN).
- **Daily bucket rate:** the same formula for one date.
- **Comparison** = rate − previous-period rate, in percentage points, shown as "↑ 4 pp vs last week". For an in-progress period, the previous period is clipped to the same number of elapsed days (week: Mon..same weekday; month: day 1..same day number, capped at that month's length). A completed period is compared with the whole previous period.
- **Today snapshot** (Small ring, Medium donut/legend, stat cards): today's counts per bucket plus pending, out of today's eligible headcount. Today's eligible headcount = covered employees whose today is an expected workday. Leave people count in that headcount (so the four buckets + pending sum to it), but not in today's rate. **Small/Medium/Large stat cards show today**. The **trend shows the selected period**. Labels make this explicit ("Today" on the snapshot, the range on the trend).
- **Expanded popup** stat cards show **period totals in employee-days** for the selected week/month, labelled as days (for example "Present · 112 days"). The "Total" card shows the eligible employee count. *My call*: the popup is the period view. If a past period is selected, the card views' snapshot still says Today.
- **Timezone / today:** the week/month date range comes from the actor's legal-entity work date (`AttendanceScheduleResolver.Resolve`), the same as `GetCoveredTeamTodayQuery`. Week = ISO Monday–Sunday. *My call:* each employee is classified in their **own** legal entity's timezone, calendar and today. `AttendancePeriodData` is already per-employee, so a remote employee isn't marked absent before their own day ends.
- **Late beats missing clock-out (my call):** Classify returns `missing_clock_out` before `late`. If a day is in `Dates.Late`, the widget files it as **Late**, so the user's "Late shown separately" requirement holds.
- **Today engine (my call):** today's snapshot, today's trend bucket and today's matrix cell use the **same engine as the team-status widget** (`AttendanceDayStatusResolver` / `TeamStatusMapper`, including `ShouldHaveClockedIn`). Past days use Classify. That way two cards on the same dashboard never disagree about today. Under that engine, "pending" means someone hasn't clocked in and their start time plus grace hasn't passed yet. Once it has passed, team-status shows them Absent, and so does this widget.
- **Leave visibility (my call, follows existing rule):** the widget applies the same `ILeaveVisibilityScopeProvider` masking as team-status. When the viewer isn't allowed to see an employee's leave, that leave day shows and counts exactly as team-status shows it (absent) in counts, rate, preview, matrix and export. Leave facts never leak past what team-status already reveals.

## 5. Scope and permissions

- Same gate and scope as `GetCoveredTeamTodayQuery`: `attendance:read` permission, `IEmployeeAuthorityResolver.ResolveVisibilityAsync(..., IncludeSelf:false, EmployeeAuthorityPurpose.TimeTrackingRead)`, with the actor id stripped. "Team" means exactly what the team-status widget already shows.
- The scope is derived server-side on every call. Client-supplied filters (department, search) only narrow within it. An unknown or out-of-scope department id returns an empty result, never other people.
- Widget access: same rule as `team-status` in `DashboardWidgetAccess` (entitlement + permission). The builder must check how team-status is gated and mirror it.

## 6. Backend architecture

**No projection table.** Justification: the work is bounded to ≤ 31 days × covered employees, and every input is already indexed per tenant+employee+date. A team summary cache would need hierarchy-change invalidation and would risk leaking ex-members. Live computation with **batched** loads is cheap and always fresh. The builder must avoid the per-employee `EmployeeAttendancePeriodReader.LoadAsync` loop (≈8 queries per employee). Build a team variant instead.

`ITeamAttendancePeriodReader` (new, Application/TimeAttendance/Services) loads, once per request, for the covered ids and a date range:
- the attendance records: `ListRecordsAsync` with all ids, sized take
- approved leaves: `ListApprovedCoveringAsync` already takes a list
- holidays once
- each distinct legal entity once
- employees by id in one query (add a batch repo method if missing)
- excused dates and clock-in policy thresholds batched (add batch methods if only per-employee ones exist)

It then builds one `AttendancePeriodData` per employee and calls the unchanged `Classify`. Break data is not needed.

- **Range guard:** a week or a calendar month only, from `mode` + `anchor`. Anything else is a 400. The previous-period comparison is a second classification over the clipped previous range, inside the same batched load (load from previous-period start to current end once).
- **Indexes:** check the existing `attendance_records` / leave indexes against the query plans. Add a migration only if a plan shows a seq scan on realistic data. Expected: none needed.

### Endpoints (new controller actions under the existing attendance time-tracking controller, or the controller the builder finds most fitting)

1. `GET attendance/time-tracking/team/attendance-summary?mode=week|month&anchor=yyyy-MM-dd&departmentId=`
   Returns `{ mode, rangeStart, rangeEnd, timezone, today, eligibleToday, todaySnapshot{present,late,absent,onLeave,pending}, todayRate, period{present,late,absent,onLeave, rate}, previousRate, deltaPp, trend[{date, present, late, absent, onLeave, rate}], todayPreview[≤6 {employeeId, name, avatarUrl, department, status, clockInLocal}], departments[{id,name}], calculatedAtUtc, isProvisional }`. `isProvisional` is true when the range includes today. The `todayPreview` order is late, absent, not-in-yet, present, leave, then by name (*my call*: problems first).
2. `GET attendance/time-tracking/team/attendance-employees?mode&anchor&departmentId&search&page&pageSize(≤50)`
   Returns paged `{ items[{employeeId, name, avatarUrl, department, days[{date, status}], attended, eligibleDays, rate}], total }`, with sorting by name. Search filters on name in SQL, before classification.
   Export uses this endpoint, paging through all pages of the current filter, then the existing `csvCell`/`downloadCsv` helpers (formula-injection escaping already there). Move them to a shared util if they're only in `people/utils`.

**Widget data provider:** `TeamAttendanceWidgetDataProvider` mirrors endpoint 1 with the default mode=week, anchor=today. The registry entry has `HasProvider: true`, catalog min 3×3, max 12×8, and a default rect on the My Team default layout. *My call*: place it under team-status and update the permission-variant layouts so no hole is left. Update the My Team catalog-count test.

## 7. Frontend

- **Widget:** `team-attendance-card` in `modules/dashboard/feature/`, registered in `widget-registry.ts`. A store/api service follows the team-progress pattern: bootstrap data first, refetch on period change, `switchMap`-style latest-wins cancellation, and the cache keyed by mode+anchor+department.
- **Size modes:** a `team-attendance-layout.util.ts` modelled on `team-progress-layout.util.ts` (ResizeObserver + rAF + hysteresis): `small` / `medium` / `large`, with thresholds chosen so 3×3–4×4 gives small, wide-short gives medium, and ≥ ~6 rows and wide gives large. Narrow-tall falls back to medium stacked. The thresholds are left to the builder, with spec tests like team-progress.
- **Period control:** one shared piece of state, `{ mode, anchor, departmentId }`, lives in the store, so changing size or opening the popup keeps it. Small view uses a compact `app-select` ("Week"/"Month") with ‹ ›. The others use `app-select` + range label + ‹ ›. Persist the mode in `DashboardWidget.SettingsJson` only if the frontend already has a settings-save path for widgets. Otherwise keep it in memory (no localStorage).
- **Reuse:**
  - `app-kpi-tiles` for the stat cards. Extend it backwards-compatibly if the pastel full-tint look in the image needs a variant flag.
  - `progress-ring` for the Small ring.
  - The team-progress donut approach (`pie-slices.util`) for the distribution donut.
  - A trend bar chart: generalise `weekly-completion-chart` only if it can take stacked segments without changing its current look. Otherwise one new `ui/stacked-day-chart` under dashboard/ui, used by both card and popup.
  - `app-avatar`, `app-modal` (2xl, full-screen < 640px, Esc/focus as it already does), `data-table` if it supports sticky first columns and horizontal scroll, otherwise a plain scroll table, `app-empty-state`, `app-error-state`, `loader`.
- **Colours:** Present green, Absent red, Late amber, Leave blue, Not in yet neutral. Use the theme tokens (`--color-success`, etc.) where they exist, as kpi-tiles does.
- **Matrix cell icons:** ✓ present (green), clock (amber) late, ✕ absent (red), leave (blue), "–" off/none/future. The Late status is visibly distinct (user requirement). Monthly shows 28–31 columns with horizontal scroll and the name column sticky.
- **States:** loading skeleton, no covered employees, no expected days in period ("No working days in this period"), error + retry, `null` rate shown as "—". A provisional period shows a small "Today in progress" hint.
- The FE zero-warnings rule applies (`npm run check:warnings`), and component styles go in `styleUrl`, not shared consts.

## 8. Tests

Backend unit tests:
- bucket mapping, including missing_clock_out → Present, today pending ≠ Absent, leave excluded from the denominator, and off-day work not counted
- weighted rate, null on zero
- the pp delta with a clipped in-progress previous period
- February in a leap year, and month/year rollover
- scope: an out-of-scope department gives an empty result, self is excluded, and no `attendance:read` gives 403
- the range guard
- the provider registered with `HasProvider`
- the registry/variant tests updated

Also a query-count style check that the reader doesn't issue per-employee queries (or a code-review assertion if no harness exists). Integration tests only if Docker is available; report honestly if not.

Frontend specs:
- the layout util thresholds/hysteresis
- each view renders from one component
- the period switch triggers a refetch with the right params, and latest-wins
- monthly matrix columns = days in month
- the popup keeps the period
- export builds escaped CSV for the current filter
- empty, error and null states

Verification: `dotnet build -c Release`, the unit and architecture suites, the FE `ng build`, the targeted and full specs, and `npm run check:warnings`. Then a browser pass on dapi (the user signs in) for S/M/L/popup × week/month at desktop and 375px width. Latency is measured on dapi data if possible, otherwise reported as not measured.

## 9. Left to the builder

- Exact controller placement, DTO names, size thresholds, and the chart component choice (reuse vs new).
- Whether kpi-tiles needs a variant.
- Batch repo method shapes.

No push, PR, merge, or migration against a shared database without the user's OK.
