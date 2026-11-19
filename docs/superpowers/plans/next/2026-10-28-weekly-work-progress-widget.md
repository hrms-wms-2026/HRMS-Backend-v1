# Weekly Work Progress Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the My Work "Weekly Task Progress" card with a server-computed, bootstrap-delivered Weekly Work Progress widget (4 adaptive views + popup), and stop the bootstrap loading data for widgets the web never shows.

**Architecture:** A pure C# calculator turns 14 days of one employee's attendance, planner blocks, task clock sessions, meetings, leave and holidays into this-week / last-week totals; a MediatR query feeds it and is exposed both as `GET api/v1/work/my-weekly-progress` and as the `today-total-time` bootstrap provider. The Angular card reads the bootstrap payload through `injectWidgetDataFeed` and picks its view from its own size (`injectHostSize`).

**Tech Stack:** .NET 9 / EF Core / MediatR / PostgreSQL (RLS); Angular (standalone, signals, OnPush), Tailwind, inline SVG charts.

**Spec:** `docs/superpowers/specs/next/2026-10-28-weekly-work-progress-widget-design.md` (read it first; section numbers below refer to it).

## Global Constraints

- Branch: `chore/uat-hardening` in both repos (user works on the current branch). Never push; never apply migrations to a shared DB.
- Widget key stays `today-total-time`; component class stays `WeeklyProgressCardComponent` (saved layouts reference the key).
- Week = Monday–Sunday in the employee's timezone (`ICalendarTimezoneResolver.ResolveForUserAsync`, UTC fallback like `PlannerService.ZoneOfAsync`).
- Worked = logged task minutes + credited meeting minutes. Attendance minutes are context only. Task weightage is a separate block.
- Status thresholds: `worked / expectedToDate` ≥ 1.0 → `ahead`; ≥ 0.85 → `on_track`; else `behind`; expected = 0 → `no_target`.
- `weekStart`: must be a Monday, not after the current week's Monday, at most 52 weeks back → otherwise 422.
- Card: min 2×2, `DASHBOARD_DONUT_PX` (140) donut never resized, padding `px-4 py-3`, header `mb-2`, 20 px hysteresis, breakpoints: minHeight 260, miniWideWidth 360, miniTilesWidth 640, xsWidth 330, tilesOneRowBodyWidth 520.
- Refresh: 60 s while the tab is visible + on visibility return, via `feed.reload()`.
- No chart library, no mock data, no `??`/`?.` that Angular flags (FE build treats template diagnostics as errors — run `npm run check:warnings`).
- Backend gate: `pwsh ../dev-env/scripts/verify.ps1 backend [-Filter ...]`; FE gate: `npx ng test --watch=false --include <spec>` while iterating, `pwsh ../dev-env/scripts/verify.ps1 frontend` at the end.

## Review Focus

1. **Open clock session or meeting crossing midnight in the employee's zone** — minutes must split across the two days, not land on one. → Task B2 test `Session_over_midnight_splits_across_days`.
2. **Employee in a non-UTC zone at the week boundary** (Sunday 23:30 local = Sunday UTC-earlier) — must count in the local week. → Task B2 test `Week_boundaries_follow_employee_zone`.
3. **Legal entity without working hours** (the 3 Acme entities locally) — `no_target`, card shows the "No working hours set" state, no divide-by-zero. → B2 `No_schedule_gives_no_target` + F5 card spec `renders no-target state`.
4. **Planner switched off** (local default) — scheduled figures hidden in every view and the popup, chart shows Worked vs Expected only. → F4 `hides scheduled series`, F5 `hides Scheduled tile when planner disabled`.
5. **Bootstrap payload missing/errored** — card falls back to its own `get()` once, then polls through the context only if the first load came from it. → F5 spec `fetches when bootstrap has no data`.

---

## Backend (HRMS-Backend-v1)

### Task B1: Registry — `ShownOnWeb`, new `today-total-time` definition, bootstrap skips hidden widgets

**Files:**
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/GridModels.cs` (record `WidgetDefinition`)
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`
- Modify: `src/ONEVO.Application/Features/Dashboard/Layout/Queries/GetDashboardBootstrap/GetDashboardBootstrapQueryHandler.cs` (the `widgetData.LoadAsync(...)` call)
- Modify: `GetWidgetDataQuery` handler (find with `grep -rn "class GetWidgetDataQueryHandler" src`) — same skip
- Test: `tests/ONEVO.Tests.Unit/Features/Dashboard/Layout/DashboardWidgetRegistryTests.cs`, `.../GetDashboardBootstrapQueryHandlerTests.cs`

**Interfaces:**
- Produces: `WidgetDefinition(..., bool HasProvider, bool ShownOnWeb = true)`; registry entry `new("today-total-time", MyWork, "Weekly work progress", "Your week's worked hours against expected and planned time.", 2, 2, 12, 8, true)`; `ShownOnWeb: false` for `my-tasks`, `work-pattern`, `notifications`, `wellbeing`.

- [ ] **Step 1: Write failing tests**
  - `DashboardWidgetRegistryTests.TodayTotalTime_is_weekly_work_progress_2x2_with_provider`: `Get("today-total-time")` → Title `"Weekly work progress"`, MinW 2, MinH 2, MaxW 12, MaxH 8, HasProvider true.
  - `DashboardWidgetRegistryTests.Web_hidden_widgets_are_flagged`: ShownOnWeb false exactly for `{my-tasks, work-pattern, notifications, wellbeing}`.
  - `GetDashboardBootstrapQueryHandlerTests.Bootstrap_does_not_load_data_for_widgets_hidden_on_web`: a My Work dashboard containing `my-tasks` → orchestrator fake receives keys without `my-tasks`; the `my-tasks` widget DTO has state `client` and `Data == null`; it is still in `Widgets`.
- [ ] **Step 2: Run** `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~Dashboard.Layout"` → the 3 new tests FAIL.
- [ ] **Step 3: Implement** the record field, registry values, and filter `ShownOnWeb` in both the bootstrap and single-widget-refresh paths (state `WidgetDataStatus.Client`). Update the registry's doc comment (today-total-time is no longer client-sourced).
- [ ] **Step 4: Run** the same filter → PASS. The existing "registry HasProvider matches registered providers" test will now FAIL until B5 — mark nothing skipped; B5 fixes it. (If you run tasks strictly one at a time, do B5's registration line now and keep the provider class for B5.)
- [ ] **Step 5: Commit** `feat(dashboard): bootstrap skips widgets the web hides; weekly progress becomes a 2x2 provider widget`

### Task B2: Pure calculator

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/WeeklyProgress/Services/WeeklyWorkProgressCalculator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/WeeklyProgress/Services/WeeklyWorkProgressInput.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/WeeklyProgress/DTOs/WeeklyWorkProgressResponse.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/WeeklyProgress/WeeklyWorkProgressCalculatorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record WeeklyMeetingInterval(DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, bool Declined, bool Cancelled);
  public sealed record WeeklyAttendanceDay(DateOnly Date, DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, int WorkedMinutes, int BreakMinutes);
  public sealed record WeeklyWorkProgressInput(
      DateOnly WeekStart, TimeZoneInfo Zone, DateTimeOffset Now,
      IReadOnlySet<int> WorkingWeekdays, TimeOnly? WorkStart, TimeOnly? WorkEnd,   // null = legal entity schedule not configured
      IReadOnlySet<DateOnly> Holidays, DateOnly HireDate, DateOnly? TerminationDate,
      IReadOnlyDictionary<DateOnly, decimal?> LeaveHoursByDate,                    // null value = full-day leave
      IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> PlannedBlocks,
      IReadOnlyList<WeeklyMeetingInterval> Meetings,
      IReadOnlyList<(DateTimeOffset In, DateTimeOffset? Out)> ClockSessions,
      IReadOnlyList<WeeklyAttendanceDay> Attendance);
  public static class WeeklyProgressStatuses { Ahead="ahead"; OnTrack="on_track"; Behind="behind"; NoTarget="no_target"; }
  public static WeekProgressDto WeeklyWorkProgressCalculator.Compute(WeeklyWorkProgressInput input);
  public sealed record WeekDayProgressDto(DateOnly Date, bool IsExpected, bool IsHoliday, int LeaveMinutes, int ExpectedMinutes, int ScheduledMinutes, int WorkedMinutes, int AttendanceMinutes);
  public sealed record WeekProgressDto(int ExpectedMinutes, int ExpectedToDateMinutes, int ScheduledTaskMinutes, int ScheduledMeetingMinutes, int ScheduledMinutes, int LoggedTaskMinutes, int CreditedMeetingMinutes, int WorkedMinutes, int AttendanceMinutes, int RemainingMinutes, decimal ProgressPercent, decimal PlanningCoveragePercent, decimal? ScheduleAdherencePercent, string Status, IReadOnlyList<WeekDayProgressDto> Days);
  public sealed record WeekTaskWeightsDto(decimal CompletedHours, decimal InProgressHours, decimal NotStartedHours, int UnestimatedCount);
  public sealed record WeeklyWorkProgressResponse(DateOnly WeekStart, DateOnly WeekEnd, DateOnly Today, string Timezone, bool PlannerEnabled, WeekProgressDto ThisWeek, WeekProgressDto LastWeek, WeekTaskWeightsDto Tasks);
  ```
- Reuse inside: `ExpectedWorkdayCalendar.Build(..., today: weekEnd)` for expected days, `PlannerActuals.TaskMinutes` per day window, `PlannerActuals.MeetingMinutes`, `MeetingPersonHours.AllDayMinutesPerDay`, and the attendance-minutes rule of `PlannerService.AttendanceMinutes` (extract it to a `public static` helper in `PlannerActuals` rather than duplicating; keep `PlannerService` calling it). Day windows = local midnight..midnight converted with `Zone` (copy `PlannerService.Bounds`, or make it `internal static` and reuse).

- [ ] **Step 1: Write failing tests** (Asia/Colombo zone, schedule 09:00–18:00 Mon–Fri unless stated; `Now` fixed):
  - `Full_week_expected_is_working_days_times_required`: no leave/holidays → ExpectedMinutes 2700, 7 Days, Sat/Sun IsExpected false.
  - `Holiday_and_full_leave_remove_a_day_partial_leave_removes_hours`: holiday Tue, full leave Wed, 4 h leave Thu → Expected 2700−540−540−240 = 1380.
  - `Hired_midweek_starts_expected_at_hire_date`; `Terminated_midweek_stops_expected`.
  - `No_schedule_gives_no_target`: WorkStart null → Expected 0, Status `no_target`, ProgressPercent 0.
  - `Worked_is_logged_plus_credited_meetings_not_attendance`: 120 min session + ended 60 min meeting + 480 attendance → Worked 180, Attendance 480.
  - `Declined_cancelled_and_future_meetings_are_not_credited`; `All_day_meeting_counts_AllDayMinutesPerDay_scheduled`.
  - `Session_over_midnight_splits_across_days`: 23:00–01:00 local Mon→Tue → Mon 60, Tue 60.
  - `Open_session_counts_up_to_now`.
  - `Week_boundaries_follow_employee_zone`: a session Sun 23:30–23:50 local (Sun 18:00 UTC) lands on Sunday of this week.
  - `Expected_to_date_prorates_today_by_work_window`: Now = Wed 13:30 local → ExpectedToDate = 540×2 + 540×(4.5/9) = 1350.
  - `Status_thresholds`: worked/expectedToDate 1.0 → ahead; 0.85 → on_track; 0.84 → behind.
  - `Coverage_and_adherence`: scheduled 1920, expected 2400, worked 1620 on past days with scheduled 1800 → coverage 80.0, adherence = min(worked,scheduled)/scheduled over past+today days, rounded to 1 decimal; nothing scheduled → adherence null.
  - `Remaining_never_negative`.
- [ ] **Step 2: Run** `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~WeeklyWorkProgressCalculator"` → FAIL (types missing).
- [ ] **Step 3: Implement** the records and `Compute`. Percentages: `Math.Round(x * 100m, 1, MidpointRounding.AwayFromZero)`.
- [ ] **Step 4: Run** the filter → all PASS; also `-Filter "FullyQualifiedName~Planner"` → PASS (helper extraction didn't change the planner).
- [ ] **Step 5: Commit** `feat(wm): weekly work progress calculator`

### Task B3: Task weightage read + index check

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/WeeklyProgress/RepositoryInterfaces/IWeeklyWorkProgressReadRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/WeeklyProgress/EfWeeklyWorkProgressReadRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register scoped, next to the planner repository ~line 457)
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/WeeklyProgress/WeeklyWorkProgressReadRepositoryTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<WeekTaskWeight>> ListWeekTaskWeightsAsync(Guid tenantId, Guid employeeId, DateOnly weekStart, DateOnly weekEnd, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);` with `public sealed record WeekTaskWeight(Guid TaskId, decimal? EstimatedHours, string Category);`
- Rule: tasks assigned to the employee (`task_assignments`), not deleted, **not meeting tasks** (default `WorkTasks` filter already hides meetings), and (DueDate in [weekStart, weekEnd]) OR (a non-deleted planned block in [fromUtc, toUtc)) OR (a clock session overlapping [fromUtc, toUtc)). Category from the task's `TaskStatus.Category`. One SQL query, `AsNoTracking`.

- [ ] **Step 1: Write failing integration tests** (Testcontainers, existing integration fixtures as in `PlannerServiceIntegrationTests`): due-in-week task included; planned-in-week task due next month included; clocked-in-week task included; other employee's task excluded; meeting task excluded; deleted task excluded; Category comes through.
- [ ] **Step 2: Run** `pwsh ../dev-env/scripts/verify.ps1 backend -Full` (Docker) → FAIL. If Docker is not running, say so in the report and continue; do not skip writing the tests.
- [ ] **Step 3: Implement** the repository.
- [ ] **Step 4: Index check:** inspect `ApplicationDbContextModelSnapshot.cs` for indexes covering `task_clocking_sessions(employee_id, clock_in_at)` and `attendance_records(employee_id, date)`. Only if one is missing, add a migration `AddWeeklyProgressReadIndexes` (no RLS change needed; index only). Report either way.
- [ ] **Step 5: Run** the integration tests → PASS (or report Docker unavailable). Commit `feat(wm): weekly task weightage read`

### Task B4: Query + handler

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/WeeklyProgress/Queries/GetMyWeeklyWorkProgress/GetMyWeeklyWorkProgressQuery.cs` (+ `...QueryHandler.cs`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/WeeklyProgress/GetMyWeeklyWorkProgressQueryHandlerTests.cs`

**Interfaces:**
- Consumes: B2 `WeeklyWorkProgressCalculator.Compute`, DTOs; B3 repository; existing `IPlannerRepository.ListBlocksAsync/ListSessionsAsync`, `IMeetingRepository.ListEmployeeDayMeetingsAsync`, `IAttendanceReadRepository.ListRecordsAsync`, `ICalendarEventRepository.ListHolidayDatesAsync`, `ILeaveRequestReadRepository.ListApprovedCoveringAsync` (+ day allocations' `HoursUnit` for partial days), legal entity repository `GetByIdForTenantAsync`, `ICalendarTimezoneResolver`, `ICallerIdentityResolver` (UserId → EmployeeId), `IOptions<PlannerOptions>`, `IDateTimeProvider`.
- Produces: `public sealed record GetMyWeeklyWorkProgressQuery(DateOnly? WeekStart) : IRequest<Result<WeeklyWorkProgressResponse>>;`

- [ ] **Step 1: Write failing tests** with fakes: no employee → 404; `WeekStart` Tuesday → 422; future Monday → 422; 53 weeks back → 422; null → current week (Monday in employee zone, Now fixed Wed); `PlannerEnabled` mirrors options; repositories are each called **once** with the 14-day range [lastWeekStart 00:00 local, thisWeekEnd+1 00:00 local) (assert call count = 1 — guards against N+1 per day); LastWeek computed from the same data.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~GetMyWeeklyWorkProgress"` → FAIL.
- [ ] **Step 3: Implement**: load once for 14 days, build two `WeeklyWorkProgressInput`s (this week / last week), `Compute` each, aggregate `WeekTaskWeight`s into `WeekTaskWeightsDto` (`done` → completed, `active` → in progress, `not_started` → not started, null estimate → `UnestimatedCount`).
- [ ] **Step 4: Run** → PASS. Commit `feat(wm): my weekly work progress query`

### Task B5: Endpoint + bootstrap provider

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/MyWorkProgressController.cs`
- Modify: `src/ONEVO.Api/Dashboard/Widgets/WidgetDataProviders.cs` (provider + registration line; update the class comment that lists client-sourced widgets)
- Test: `tests/ONEVO.Tests.Unit/Api/Dashboard/Widgets/WeeklyWorkProgressWidgetDataProviderTests.cs`; existing registration test

**Interfaces:**
- Produces: `[Route("api/v1/work")] [Authorize(Policy = "TenantPolicy")] [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]` controller, action `[HttpGet("my-weekly-progress")] MyWeeklyProgress([FromQuery] DateOnly? weekStart, CancellationToken ct)` returning the response or the Result's status. Provider `WeeklyWorkProgressWidgetDataProvider : EndpointMirrorWidgetDataProvider`, `Key = "today-total-time"`, `Gate = EndpointGate.For<MyWorkProgressController>(nameof(MyWorkProgressController.MyWeeklyProgress))`, `LoadAsync` sends `new GetMyWeeklyWorkProgressQuery(null)`.

- [ ] **Step 1: Write failing tests** following `TeamProgressWidgetDataProviderTests`: gate denied → Forbidden; success maps the body unchanged; handler failure keeps status. Registration test (registry `HasProvider` ⇔ keyed provider) now passes with 7 providers.
- [ ] **Step 2: Run** `-Filter "FullyQualifiedName~Dashboard"` → FAIL, then implement, then PASS.
- [ ] **Step 3: Security checks**: `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` → 0 unprotected actions.
- [ ] **Step 4: Full gate** `pwsh ../dev-env/scripts/verify.ps1 backend` → build + unit + architecture green. Record counts.
- [ ] **Step 5: Commit** `feat(wm): my-weekly-progress endpoint and bootstrap provider`

---

## Frontend (Hrms--Web-application---front-end---v1)

Before F1: the branch has uncommitted Quick Actions work (`quick-actions-card`, `grid-math`, `dashboard-grid`, `dashboard-shell`, `dashboard.store`, `widget-registry`). **Ask the user** whether to commit it first; never stash or discard it silently.

### Task F1: Model + API service

**Files:**
- Create: `src/app/modules/dashboard/models/weekly-work-progress.model.ts`
- Create: `src/app/modules/dashboard/data-access/weekly-work-progress-api.service.ts` (+ `.spec.ts`)

**Interfaces:**
- Produces: TS interfaces mirroring B2 DTOs in camelCase: `WeeklyWorkProgressResponse`, `WeekProgress`, `WeekDayProgress`, `WeekTaskWeights`; `type WeeklyProgressStatus = 'ahead' | 'on_track' | 'behind' | 'no_target'`. Service `WeeklyWorkProgressApiService.get(weekStart?: string): Observable<WeeklyWorkProgressResponse>` → `GET {apiBase}/work/my-weekly-progress` with `weekStart` param only when given (copy the base-URL pattern from `team-attendance-api.service.ts`).

- [ ] **Step 1:** spec: `get()` sends no params; `get('2026-10-19')` sends `weekStart=2026-10-19`. Run `npx ng test --watch=false --include src/app/modules/dashboard/data-access/weekly-work-progress-api.service.spec.ts` → FAIL.
- [ ] **Step 2:** implement → PASS. Commit `feat(dashboard): weekly work progress model and api`

### Task F2: Layout util

**Files:**
- Create: `src/app/modules/dashboard/utils/weekly-work-progress-layout.util.ts` (+ `.spec.ts`)

**Interfaces:**
- Produces: `type WeeklyProgressView = 'mini' | 'mini-wide' | 'xs' | 'standard'`; `WEEKLY_PROGRESS_BREAKPOINTS = { minHeight: 260, miniWideWidth: 360, miniTilesWidth: 640, xsWidth: 330, tilesOneRowBodyWidth: 520 }`; `WEEKLY_PROGRESS_HYSTERESIS = 20`; `WEEKLY_PROGRESS_CARD_CHROME = { width: 32, height: 68 }`; `classifyWeeklyProgressSize(w, h)`, `resolveWeeklyProgressView(w, h, previous)` (same contract as `resolveTeamAttendanceView`); `planWeeklyProgressSections(view, bodyWidth, bodyHeight): { miniTiles: boolean; stackedTiles: number; tilesOneRow: boolean; chart: boolean; comparison: boolean }` with fixed section heights `{ gap: 12, tileRow: 64, stackedTile: 52, chart: 190, comparison: 96 }` — chart only when it fits after the hero+tiles, comparison only after the chart.

- [ ] **Step 1:** spec pins: 2×2 at 1440 (≈206×204) → `mini`; 5×2 (≈533×204) → `mini-wide`, `miniTiles` false; 7×2 (≥640) → `miniTiles` true; 2×5 (≈206×528) → `xs` with `stackedTiles` 4 when height allows, fewer when not; 6×3 → `standard` without chart; 6×5 → chart; 6×7 → chart + comparison; hysteresis keeps the previous view within 20 px; 0×0 → previous or `standard`. Run → FAIL.
- [ ] **Step 2:** implement → PASS. Commit `feat(dashboard): weekly progress layout plan`

### Task F3: Insights util

**Files:**
- Create: `src/app/modules/dashboard/utils/weekly-work-progress-insights.util.ts` (+ `.spec.ts`)

**Interfaces:**
- Produces: `interface WeeklyInsight { tone: 'positive' | 'warning' | 'info'; text: string }`; `buildWeeklyInsights(res: WeeklyWorkProgressResponse): WeeklyInsight[]` (max 4, order below); `progressDeltaPoints(res): number | null` (this `progressPercent` − last `progressPercent`, null when last week's expected is 0).

- [ ] **Step 1:** spec, one test per rule, exact copy:
  1. worked delta vs last week → `Worked 12% more hours than last week.` (positive) / `… fewer …` (warning); hidden when last week's worked is 0.
  2. planner enabled and expected > 0 → `Schedule coverage is 80%.` (info).
  3. past expected days with worked < scheduled (planner on) or < expected (planner off) → `Wed and Sat are below schedule.` / `… below expected.` (warning; day names joined with `, ` and ` and `).
  4. status → `On track to meet weekly target.` (positive) / `Ahead of weekly target.` (positive) / `Behind weekly target.` (warning); `no_target` → none.
  5. attendance − worked > 60 min this week → `Clocked in 3h 10m without task time.` (info).
  Run → FAIL; implement → PASS. Commit `feat(dashboard): weekly progress insights`

### Task F4: Daily Work vs Schedule chart

**Files:**
- Create: `src/app/modules/dashboard/ui/daily-work-chart/daily-work-chart.component.{ts,html,css,spec.ts}`

**Interfaces:**
- Produces: `<app-daily-work-chart [days]="WeekDayProgress[]" [showScheduled]="boolean" [compact]="boolean" />` — inline SVG, 7 groups Mon..Sun: Scheduled bar (blue `#3b82f6`), Worked bar (green `#22c55e`), Expected dashed polyline; x label `Mon` + `6h / 5h` (scheduled / worked, or worked / expected when `showScheduled` false); y axis hours scaled to max(expected, scheduled, worked) rounded up to an even hour; `compact` hides the y axis and the second label line; `<title>` tooltip per bar; legend row above.

- [ ] **Step 1:** spec: 7 groups; bar heights proportional (data-testid attrs); `showScheduled=false` renders no scheduled bars and changes the label; all-zero week renders without NaN; `compact` hides axis. Run → FAIL; implement → PASS. Commit `feat(dashboard): daily work vs schedule chart`

### Task F5: Card rewrite

**Files:**
- Modify (rewrite): `src/app/modules/dashboard/feature/weekly-progress-card/weekly-progress-card.component.{ts,html,spec.ts}` (+ create `.css` if needed)
- Delete: `src/app/modules/dashboard/feature/today-total-time-card/` (orphan; confirm with `grep -rn TodayTotalTimeCardComponent src` → only its own files)
- Modify: `src/app/modules/dashboard/widgets/widget-registry.spec.ts` only if it asserts the old title

**Interfaces:**
- Consumes: F1 service, F2 layout util, F3 `progressDeltaPoints`, F4 chart, `injectWidgetDataFeed`, `injectHostSize`, `DASHBOARD_DONUT_PX`, `app-kpi-tiles`, `formatMinutes` (`dashboard/util/timeline-segments.util`).
- Produces: card emits nothing; opens F6 dialog via a local `open` signal.

- [ ] **Step 1:** spec (use `host-size.testing.ts` FakeResizeObserver, `instances[0]`):
  - `takes the bootstrap payload` (WIDGET_DATA success → no HTTP); `fetches when bootstrap has no data` (one `get()`); polling calls `feed.reload()` every 60 s while visible.
  - per view: `mini` shows donut `68%` + `On Track` only; `mini-wide` shows `+12%` + both week bars; `xs` shows stacked tiles; `standard` 6×5 shows chart; 6×7 shows comparison tiles.
  - `hides Scheduled tile when planner disabled`; `renders no-target state` (`No working hours set for your legal entity`); error state with Retry.
  - donut element is exactly `DASHBOARD_DONUT_PX` square in every view; centre text never wider than the ring (assert class `truncate`/fixed font size per spec).
  - clicking the body opens the dialog; clicking the week select doesn't.
  Run → FAIL.
- [ ] **Step 2:** implement. Title "Weekly Work Progress". Week select (This week / Last week) only in `mini-wide`(≥640) and `standard`; changing it calls `get(lastMonday)` and shows that week (no comparison row for last week — its comparison would need the week before). Percent via `formatPercent`/one decimal rule from `team-progress-format.util`.
- [ ] **Step 3:** run spec → PASS; delete orphan; run `npx ng test --watch=false --include "src/app/modules/dashboard/**/*.spec.ts"` → PASS. Commit `feat(dashboard): weekly work progress card with adaptive views`

### Task F6: Detail dialog

**Files:**
- Create: `src/app/modules/dashboard/feature/weekly-work-progress-detail-dialog/weekly-work-progress-detail-dialog.component.{ts,html,css,spec.ts}`

**Interfaces:**
- Consumes: F1–F4, `app-modal` (`size="2xl"`, hide default header via component CSS as `team-progress-detail-dialog` does), `app-kpi-tiles` (`maxColumns` 4), `status-donut.util` `donutArcs`.
- Produces: `<app-weekly-work-progress-detail-dialog [open]="boolean" [initial]="WeeklyWorkProgressResponse" (closed)="..." />`.

- [ ] **Step 1:** spec: header shows `Oct 26 – Nov 1, 2026`; week select loads last week via `get(weekStart)`; KPI tiles Expected/Scheduled/Worked/Remaining with hints `(80%)` etc.; Schedule Adherence donut centre = coverage %; Last Week Comparison shows worked delta `+22%` and completion delta `+12%`; Task Status donut segments Completed/In Progress/Not Started with hours and `(60%)` shares, centre `40h Total`; insights list from F3 (max 4); planner off hides Scheduled tile, adherence panel; full-screen class under 640 px. Run → FAIL.
- [ ] **Step 2:** implement, wire into the card. → PASS. Commit `feat(dashboard): weekly work progress popup`

### Task F7: Verification

- [ ] **Step 1:** `npm run check:warnings` → zero warnings/errors.
- [ ] **Step 2:** `pwsh ../dev-env/scripts/verify.ps1 frontend` → record pass/fail counts; known baseline failure: notification-settings spec; work/* timeout flakes pass when run alone — re-run those individually before reporting.
- [ ] **Step 3:** Browser pass (user signs in at `https://dapi.onexso.com:4200`; the user runs the API themselves — ask them to restart it on the new backend build). In Playwright force sizes by setting `gridArea` on the parent of `app-widget-frame`: 2×2, 5×2, 7×2, 2×5, 6×3, 6×5, 6×7, popup, mobile 375 px. Planner is off locally → also verify with `Planner:Enabled=true` and a few planned blocks / clock sessions created through the app (not SQL) if the user agrees. Full-page screenshots per the user's rule (stretch the viewport to the inner scrollHeight).
- [ ] **Step 4:** Report (spec §7 of the user's prompt): files modified, components reused, calculations, summary tables (none — spec 6.4), freshness strategy, endpoints, tests run with real counts, limitations (Planner v1 = today-only planning → future-day scheduled is 0 until v1.1 merges; the user's Markdown spec never received; insight copy is mine). Save it to `HRMS-Backend-v1/docs/reports/work-management/2026-10-28-weekly-work-progress-widget-summary.md`.
