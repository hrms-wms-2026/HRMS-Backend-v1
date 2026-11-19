# Weekly Work Progress widget + dashboard bootstrap clean-up — design

Date: 2026-10-28. Repos: `HRMS-Backend-v1` and `Hrms--Web-application---front-end---v1`. Base branch: `chore/uat-hardening` in both
(current working branch; the frontend has uncommitted Quick Actions work on it — commit or stash that first, see the plan).

## 1. Intent

The My Work dashboard's "Weekly Task Progress" card (widget key `today-total-time`, component `weekly-progress-card`) is
replaced by a **Weekly Work Progress** widget that shows how an employee's week is going against what was expected and
what they planned: Expected hours, Scheduled (planned) hours, Worked hours, Remaining hours, a day-by-day Scheduled vs
Worked chart, a comparison with last week, and task status weightage in a detail popup.

Approved visual reference: the user's 5-view mock (Small, Horizontal, Vertical, Large, Expanded popup), 2026-10-28.
The user's written Markdown specification referred to in their prompt was **never received**; every formula below that
isn't stated in their prompt is marked *(my call)*.

At the same time the dashboard's single-API loading is tightened (section 7).

## 2. Goals

- One reusable card whose layout follows its own rendered size (the dashboard widget standard: 2×2 minimum, fixed
  140px donut, bigger card = more detail, never a bigger donut).
- All figures computed on the server from real data (attendance, planner, task clocking, meetings, tasks), delivered
  through the dashboard **bootstrap** (one call) and refreshed through the existing widget-data refresh.
- Popup with the full breakdown, same shell as the Team Progress / Team Attendance popups.

## 3. Non-goals

- No change to the Planner itself (v1 on `development`; v1.1 lives on `origin/feature/planner-calendar-merge`). The
  widget only reads `planned_task_blocks` and meetings.
- No team / manager view of this widget (My Work only, own data only).
- No new chart library. No mock data.
- No new summary tables in this iteration (see 6.4 for why and when to add one).

## 4. Anti-goals (would be a failure even if it "works")

- A donut that grows with the card, or text spilling out of the ring (the bug in the current card).
- Task estimates / task completion mixed into "Worked" hours. Task weightage, task completion status and logged hours
  stay **separate** metrics (user's prompt).
- The card firing its own extra API calls on dashboard load.
- Showing `0h scheduled` as if the employee planned nothing when the Planner is switched off.

## 5. Data sources (verified in code and the local DB, 2026-10-28)

| Figure | Source | Reuse |
|---|---|---|
| Employee's week and "today" | Employee timezone via `ICalendarTimezoneResolver.ResolveForUserAsync` (same zone the Planner uses) | `PlannerService.ZoneOfAsync` logic |
| Expected days | Legal entity `standard_working_days` minus holiday dates (`calendar_events` holidays) clipped to hire/termination | `AttendanceScheduleResolver.ParseWorkingDays`, `IsScheduleConfigured`, `ExpectedWorkdayCalendar.Build` (pass the week's last day as `today` to get the whole week) |
| Daily required minutes | Legal entity `work_end_time − work_start_time` (Dapi: 09:00–18:00 = 540) | `AttendanceScheduleResolver.Resolve*` → `AttendanceSchedule.RequiredWorkMinutes` |
| Leave | Approved leave covering the week; `leave_request_day_allocations.hours_unit` for partial days | `ILeaveRequestReadRepository.ListApprovedCoveringAsync` |
| Attendance worked minutes | `attendance_records.worked_minutes`; today while clocked in = now − actual_start − breaks | `PlannerService.AttendanceMinutes` rule |
| Scheduled task minutes | `planned_task_blocks` (`start_at`/`end_at`), clipped per day | `IPlannerRepository.ListBlocksAsync(from, to)` |
| Scheduled / credited meeting minutes | Meeting occurrences the employee attends (not declined, not cancelled) | `IMeetingRepository.ListEmployeeDayMeetingsAsync(from, to)`, `MinutesInDay` rule, `PlannerActuals.MeetingMinutes` |
| Logged task minutes | `task_clocking_sessions`, clipped per day, open session counts to now | `IPlannerRepository.ListSessionsAsync(from, to)`, `PlannerActuals.TaskMinutes` |
| Task status weightage | `tasks.estimated_hours` + `task_statuses.category` (`done` / `active` / `not_started`) for tasks assigned to the employee | `task_assignments` (single assignee) |

Local DB facts that shape testing: `attendance_records` has 325 rows for 2026-10-19..31 (25 employees);
`planned_task_blocks`, `task_clocking_sessions`, `meeting_details` and holiday events are **empty**; 158 assigned
tasks have estimates; only Dapi Technologies has working hours configured; the Planner flag
(`Planner:Enabled`) is **off** in local config.

## 6. Calculations

All minutes are integers; the week is **Monday–Sunday in the employee's timezone**. "Now" is `IDateTimeProvider.UtcNow`.

### 6.1 Per day (Mon..Sun)

- `isExpected` — working weekday, not a holiday, inside hire/termination.
- `leaveMinutes` — full-day approved leave = the day's required minutes; partial = `hours_unit × 60`, capped at the
  day's required minutes *(my call)*.
- `expectedMinutes` — `isExpected ? max(0, requiredMinutes − leaveMinutes) : 0`.
- `scheduledTaskMinutes` — planned blocks' minutes inside the day.
- `scheduledMeetingMinutes` — counted meetings' minutes inside the day (all-day meeting = `MeetingPersonHours.AllDayMinutesPerDay`).
- `scheduledMinutes` = task + meeting.
- `loggedTaskMinutes` — all clocked task minutes inside the day (every task, not only planned ones — differs from the
  Planner's day total on purpose).
- `creditedMeetingMinutes` — counted meetings that have ended.
- `workedMinutes` = logged + credited *(user's prompt: Actual Logged Task Hours + Credited Meeting Hours)*.
- `attendanceMinutes` — clocked-in time from attendance (shown in the popup as context, not part of Worked).
- Future days have worked = 0; past/today expected still counts toward the week's Expected.

### 6.2 Week totals

- `expected`, `scheduled(Task|Meeting)`, `loggedTask`, `creditedMeeting`, `worked`, `attendance` = sums of the days.
- `expectedToDate` — expected minutes of days before today + today's expected × share of today's work window elapsed
  (0 before work start, 1 after work end) *(my call)*.
- `remaining` = `max(0, expected − worked)`.
- `progressPercent` = `worked / expected` (0 when expected is 0), one decimal.
- `planningCoveragePercent` = `scheduled / expected` ("Schedule coverage" in the mock).
- `scheduleAdherencePercent` = `min(worked, scheduled) / scheduled` over **past and today** days only (null when nothing scheduled) *(my call)*.
- `status` *(my call)*: `no_target` when expected = 0; else by `worked / expectedToDate`: ≥ 1.0 → `ahead`,
  ≥ 0.85 → `on_track`, else `behind`. Labels: "Ahead", "On Track", "Behind".

### 6.3 Comparison and tasks

- `lastWeek` — the same totals for the previous Monday–Sunday (full week). The card shows
  "+12% vs last week (56%)" as **percentage-point** delta of `progressPercent` (this week to date vs last week full),
  matching the mock.
- `tasks` — over tasks assigned to the employee that are **due in the week, or planned or clocked in the week**
  *(my call)*: `completedHours` (category `done`), `inProgressHours` (`active`), `notStartedHours` (`not_started`),
  each from `estimated_hours`; `unestimatedCount` for tasks with no estimate. Meeting tasks excluded.

### 6.4 Performance and freshness *(my call)*

One request reads one employee over **14 days** (this + last week): ~8 indexed queries (blocks, sessions, meetings,
attendance, leave, holidays, legal entity, tasks). This is cheap and always fresh, so **no summary table** now.
The builder must confirm indexes exist for `(tenant_id, employee_id, start_at)` on `planned_task_blocks` (exists),
`task_clocking_sessions (employee_id, clock_in_at)` and `attendance_records (employee_id, date)`, and add a migration
only if one is missing. Revisit a `employee_daily_work_summaries` table only if (a) a team roll-up of this metric is
built, or (b) the provider's p95 exceeds 150 ms on dapi data.
Freshness: computed on every bootstrap / widget refresh; the card refreshes through the widget context every 60 s
while visible and on tab-visibility return (same as the My Team cards).

## 7. Dashboard single-API clean-up

Correction to an earlier claim in the conversation: the six provider cards **do** read bootstrap data (via
`injectWidgetDataFeed`, since `1542c15f`); there is no double fetch for them. The real waste, verified:

1. **Providers run for widgets the web never shows.** `my-tasks` is in the My Work default layout, so its provider
   (`GetMyActiveTasksQuery`) runs on every My Work bootstrap although the web hides it (`HIDDEN_WIDGET_KEYS`:
   my-tasks, work-pattern, notifications, wellbeing). Fix: move "hidden on web" into the backend registry
   (`WidgetDefinition.ShownOnWeb`), skip provider loads for those widgets (state `client`, no data), keep them in the
   saved layout (the layout PUT still needs the full set), and leave the FE set as is.
2. **The weekly card makes 3 calls of its own** (`today`, `history`, `monthly-summary`). The new widget gets a provider,
   so it loads from the bootstrap and makes **no** call on load.
3. **Orphan component**: `feature/today-total-time-card` is not referenced by the registry or any route — delete it.

Out of scope (noted for later): today-at-a-glance, todays-schedule, action-center, quick-actions stay client-sourced on
purpose (see the `DashboardWidgetRegistry` comment).

## 8. API

`GET /api/v1/work/my-weekly-progress?weekStart=yyyy-MM-dd` (optional; must be a Monday; not in the future; at most
52 weeks back → 422 otherwise). `TenantPolicy` only, **no module gate** (like the planner): every My Work user has
the widget, so an HR-only tenant must still get its attendance-based week (changed during the final review; it was
first gated like `TasksController`). Caller's own employee only (no employee id parameter). 404 when the caller has no
employee record.

The bootstrap provider `WeeklyWorkProgressWidgetDataProvider` (key `today-total-time`) mirrors this endpoint with no
`weekStart` (current week), as the other `EndpointMirrorWidgetDataProvider`s do.

Response (camelCase):

```json
{
  "weekStart": "2026-10-26", "weekEnd": "2026-11-01", "today": "2026-10-28", "timezone": "Asia/Colombo",
  "plannerEnabled": false,
  "thisWeek": {
    "expectedMinutes": 2700, "expectedToDateMinutes": 1620,
    "scheduledTaskMinutes": 0, "scheduledMeetingMinutes": 0, "scheduledMinutes": 0,
    "loggedTaskMinutes": 0, "creditedMeetingMinutes": 0, "workedMinutes": 0,
    "attendanceMinutes": 1350, "remainingMinutes": 2700,
    "progressPercent": 0.0, "planningCoveragePercent": 0.0, "scheduleAdherencePercent": null,
    "status": "behind",
    "days": [ { "date": "2026-10-26", "isExpected": true, "isHoliday": false, "leaveMinutes": 0,
                "expectedMinutes": 540, "scheduledMinutes": 0, "workedMinutes": 0, "attendanceMinutes": 510 } ]
  },
  "lastWeek": { "...same shape as thisWeek, days included..." },
  "tasks": { "completedHours": 24, "inProgressHours": 10, "notStartedHours": 6, "unestimatedCount": 1 }
}
```

`plannerEnabled` mirrors `PlannerOptions.Enabled`. When false, scheduled figures are still computed (they will be 0)
but the card **hides** Scheduled / coverage / adherence and the chart shows Worked vs Expected only.

## 9. Frontend

### 9.1 Card (`weekly-progress-card`, registry key `today-total-time` unchanged so saved layouts survive)

Title "Weekly Work Progress". Reads data through `injectWidgetDataFeed` (apply = set signal; fetch = the new
`WeeklyWorkProgressApiService.get()`; polls 60 s through `feed.reload()`). Views from `injectHostSize()` via
`utils/weekly-work-progress-layout.util.ts` (named breakpoints, 20 px hysteresis, 0×0 → standard):

| View | When (card host px) | Content (mock name) |
|---|---|---|
| `mini` | height < 260, width < 360 | 140 px donut with `68%` + status inside; nothing else (Small, 2×2) |
| `mini-wide` | height < 260, width ≥ 360 | big `68%`, status, `↗ +12% vs last week (56%)` left; This week / Last week bars right; from width ≥ 640 add Expected / Scheduled / Worked tiles (Horizontal) |
| `xs` | height ≥ 260, width < 330 | donut on top, delta under it, then tiles stacked one per row as height allows: Expected, Scheduled, Worked, Remaining (Vertical) |
| `standard` | height ≥ 260, width ≥ 330 | donut + delta left, 4 KPI tiles right (2×2 grid under 520 px body width, one row above); taller adds the **Daily Work vs Schedule** chart, taller again the **Weekly Comparison** tiles (Large) |

Grid reference: 96 px rows, 12 px gap (2 rows = 204 px … 6 rows = 636 px). Card chrome `px-4 py-3`, header `mb-2`.
Donut = `DASHBOARD_DONUT_PX` (140), centre text stacked and centred, never resized. Week selector hidden in `mini`/`xs`.
Planner off → Scheduled tile and chart series hidden, coverage hidden. Clicking the card body (not controls) opens the popup.
Loading / error / `no_target` ("No working hours set for your legal entity") states.

### 9.2 Daily Work vs Schedule chart (`ui/daily-work-chart`)

New inline-SVG component (no library): per day two bars (Scheduled blue, Worked green) and an Expected dashed line;
x labels `Mon` + `6h / 5h`; y axis in hours scaled to the week's max; tooltips; `compact` input hides the y axis.
Reuses colours from the existing Team Progress status palette. Shared by card and popup.

### 9.3 Popup (`weekly-work-progress-detail-dialog`)

Same shell as `team-progress-detail-dialog` (`app-modal size="2xl"`, own header, full-screen < 640 px). Header: title,
week range ("Oct 26 – Nov 1, 2026"), week select (This week / Last week → `get(weekStart)`), close.
Body: donut + delta + `app-kpi-tiles` (Expected, Scheduled, Worked, Remaining; `maxColumns` 4); Daily Work vs
Schedule (full size); Schedule Adherence donut (Scheduled / Worked / Not worked, centre = coverage %); Last Week
Comparison (Worked hours, Completion rate with deltas); Task Status (Weightage) donut via `status-donut.util`
(Completed / In progress / Not started hours, centre = total hours); Key Insights list built by a pure FE util from
the payload *(my call; rules)*: worked delta vs last week, coverage %, past expected days where worked < scheduled
(or < expected when the planner is off), on-track projection, attendance vs worked gap > 1 h ("Clocked in but not on
tasks"). Max 4 insights.

### 9.4 Reused

`injectHostSize`, `DASHBOARD_DONUT_PX`, `formatPercent` / `formatHourValue`, `app-kpi-tiles`, `app-modal`,
`status-donut.util`, `injectWidgetDataFeed`, the Team Progress popup CSS shell. Removed: current
`weekly-progress-card` internals (rewritten in place), orphan `today-total-time-card`.

## 10. Backend registry changes

`today-total-time`: title "Weekly work progress", description "Your week's worked hours against expected and planned
time.", min 2×2 (max 12×8), `HasProvider = true`. `WidgetDefinition` gains `ShownOnWeb` (false for my-tasks,
work-pattern, notifications, wellbeing). No migration needed for the registry (code-defined).

## 11. Edge cases

Legal entity without hours → `no_target`; employee hired mid-week → expected starts at hire date; terminated → stops;
week with a holiday; full and partial leave; open clock session over midnight (split per day); meeting spanning
midnight; declined / cancelled meeting (0); all-day meeting; block on a non-working day (counts as scheduled, expected
0); planner disabled; timezone ≠ UTC (week boundaries); unestimated tasks; last week before hire (expected 0 →
comparison hidden); `weekStart` not a Monday / future / > 52 weeks → 422.

## 12. Testing

Backend: unit tests for the calculator (pure, every edge case above), handler tests (gates, 404, 422, planner flag),
provider registration test, bootstrap skip-hidden test, architecture suite; one integration test (Docker) for the
repository reads. Frontend: layout util spec, card spec per view (FakeResizeObserver), chart spec, popup spec, insights
util spec, feed (bootstrap vs fetch) spec; `npm run check:warnings`; Playwright pass at 2×2, wide 2-row, tall narrow,
large and popup by forcing `gridArea` on the parent of `app-widget-frame`, on dapi with seeded planner/clock data.

## 13. Left to the builder

Exact SQL/EF shapes, file split inside the Application feature folder, tooltip copy, and insight wording — within the
rules above.
