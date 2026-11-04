# My Team widgets design match: summary

**Date:** 2026-11-02
**Branch:** `feature/my-team-three-widgets` in both repos. Local only: nothing pushed, no PR.
**Spec:** `docs/superpowers/specs/next/2026-11-02-my-team-widgets-design-match-design.md`
**Plan:** `docs/superpowers/plans/next/2026-11-02-my-team-widgets-design-match.md` (spec and plan stay in `next/` until your browser sign-off)

## Status at a glance

| Part | State |
|---|---|
| Backend Tasks 1-4 | Done, committed, every gate green (unit, architecture, integration) |
| Frontend Tasks 5-13 | Done, committed, zero-warnings build clean, ESLint 0 on changed files |
| Task 14: gates | Done (results below) |
| Task 14: browser measuring, overflow sweep, screenshots, p95 timing | **Not done.** The Playwright profile was locked by another process (Chrome PID 33168, started 10:30, parent 30548). It wasn't started by this session, so it wasn't killed, and the pass was skipped at your request. |
| Final whole-branch review by a separate reviewer | **Not done.** No subagent was spawned. Rulings are listed below for your review. |

Because the browser pass did not run, **every section height in the three layout utils is still a starting value**. None of them was measured with `getBoundingClientRect`.

## Commits

Backend (`HRMS-Backend-v1`, on top of `5530b85f`):

- `becfd554` feat(attendance): team today expected minutes and breaks
- `0abcdd98` feat(planner): breaks and expected minutes on team schedule
- `80c02e52` feat(planner): team week composer
- `fd4121d9` feat(planner): team week planned vs actual endpoint
- plus the commit that adds this summary

Frontend (`Hrms--Web-application---front-end---v1`, on top of `5fb735dc`):

- `a0f52d1f` feat(dashboard): team week models, api, store
- `5706c5a8` feat(dashboard): presence layout ladder
- `27836609` feat(dashboard): working/break/leave design + ladder
- `b6520ae9` feat(dashboard): working/break/leave popup design
- `ab75bbad` feat(dashboard): schedule layout ladder
- `78b37720` feat(dashboard): planned vs actual charts
- `a8204522` feat(dashboard): team schedule design + ladder
- `cfcfdb54` feat(dashboard): team schedule popup day/week
- `8a119431` feat(dashboard): team progress design pass

## Files changed

**Backend** (20 files, +954 / -45):

- **Attendance:**
  - `TeamTodayResponse.cs`: `ExpectedMinutes`, `Breaks`, `TeamTodayBreak`
  - `TeamPresenceMath.cs`: `ExpectedMinutes()`
  - `AttendanceReadHandlers.cs`: the team-today member build
- **Planner `TeamSchedule/`:**
  - `TeamScheduleResponse.cs`: `TeamScheduleBreak`, `TeamPresence`, two new member fields
  - `TeamScheduleComposer.cs`: optional `presence` argument
  - `GetTeamScheduleQueryHandler.cs`: presence mapping; population moved out
  - New: `TeamSchedulePopulation.cs`, `TeamScheduleWeekResponse.cs`, `TeamWeekComposer.cs`, `GetTeamScheduleWeekQuery.cs`, `GetTeamScheduleWeekQueryHandler.cs`, `GetTeamScheduleWeekQueryValidator.cs`
- **API:** `PlannerController.cs`, new `GET planner/team/week`
- **Tests:**
  - Unit: `TeamPresenceMathTests`, `GetCoveredTeamTodayQueryHandlerTests`, `GetTeamScheduleQueryHandlerTests`, `TeamScheduleWidgetDataProviderTests` (constructor only)
  - New unit: `TeamWeekComposerTests`, `GetTeamScheduleWeekQueryHandlerTests`
  - New integration: `TeamScheduleWeekIntegrationTests`

**Frontend** (68 files, +5520 / -1208, all under `src/app/modules/dashboard/`):

- **Models, API, stores:**
  - `my-team-today.model.ts`, `team-schedule.model.ts`
  - `team-schedule-api.service.ts`: `getTeamWeek`
  - New `state/team-schedule-week.store.ts`
- **Layout utils (rewritten):**
  - `team-presence-layout.util.ts`
  - `team-schedule-layout.util.ts`
  - `team-progress-layout.util.ts` (time section only)
- **Format utils:**
  - `team-presence-format.util.ts`: `minutesLeft`, `todayHoursText`, `todayProgress`, `presencePill`
  - `team-schedule-format.util.ts`: `itemDonut`, `timelineLabel`, `toPlannedActualRows`, `breakSegments`, `workloadLevel`, `toWeekChartDays`, `WORKLOAD_*`
  - `team-progress-time-format.util.ts`: `bucketChart`, `bucketLabel`, `localToday`
  - New `planned-actual-format.util.ts`
- **Cards and popups:**
  - Rewritten: `team-status-card`, `team-schedule-card`
  - Redesigned: `team-status-detail-dialog`, `team-schedule-detail-dialog`
  - Visual pass: `team-progress-card`, `team-progress-detail-dialog`
- **UI:**
  - New: `planned-actual-day-chart`, `planned-actual-week-chart`
  - Changed: `working-hours-chart` (`teamAverage`, `showLeave`), `weekly-completion-chart` (`percentLine`), `team-progress-time` (`chart` input, unscheduled panel), `team-progress-time-detail` (shared week chart)
- **Test fixtures:** `team-presence.testing.ts`, `team-schedule.testing.ts` (`weekResponse`)

## Components reused and new

- **Reused:**
  - `app-widget-card`, `app-popout-header-actions`, `app-modal size="2xl"`, `app-kpi-tiles` (popups), `app-avatar`
  - `injectHostSize`, `donutArcs`, `DASHBOARD_DONUT_PX`, `formatPercent`, `toCsv`/`downloadCsv`
  - `app-working-hours-chart`, `app-weekly-completion-chart`, `app-data-table` (as before)
  - `TeamAttendanceApiService.getSummary`, for the attendance rate
- **New:**
  - `app-planned-actual-day-chart`, `app-planned-actual-week-chart`
  - `TeamScheduleWeekStore`
  - Backend: `TeamWeekComposer`, `TeamSchedulePopulation`
- **Not used:** no chart library and no new npm or NuGet package. No mock data in production code.

## Size ladders (starting values, not browser-measured)

Every card's body is the host size minus a chrome of 32 px wide and 68 px tall (Team Progress: 40 x 80). The donut is always 140 px.

### Working / Break / Leave (`team-presence-layout.util.ts`)

| View | When | Sections, in priority order |
|---|---|---|
| mini | height < 260, or narrow and height < 340 | Donut (team total) beside the counts. From a 320 px body width: stacked bar, legend and pill. The 2x2 donut leaves no room for the pill. |
| xs | width < 370, height ≥ 340 | Donut, legend (72), pill (36), then rows (56 each), then "View all" (28) |
| standard | everything else | Tiles (72), then at least 3 rows (list 56; table head 32 + rows 40 from 640 px wide), then the week trend (150) once 5 rows fit with it, then more rows, then "View all" (28) only while someone is hidden |

- Table columns drop right to left: progress, today hours, remaining, elapsed, start. Status always stays.
- Gap: 12 px.

### Today's team schedule (`team-schedule-layout.util.ts`)

| View | When | Sections |
|---|---|---|
| mini | height < 260, or narrow and height < 412 | Item donut (or the inline bar) and the unscheduled pill. Planner off: team clocked total and a "Planner is off" pill. |
| xs | width < 370, height ≥ 412 | Donut, 4-item legend (96), avatar stack (36), pill (36), then rows (52) |
| rows | 370-639 px wide, or any standard size with the Planner off | Rows (52): segmented plan bar, or clocked vs expected with the Planner off |
| timeline | ≥ 640 px wide, Planner on | Axis (24), lines (44), legend (24); then the day chart (head 64, rows 28) once 5 rows fit with it, one chart row per shown person; then a side column (300 px) at ≥ 1150 px wide and body ≥ 420; then the week chart (220) at ≥ 1150 x 800; leftover height goes to more lines |

### Team Progress (`team-progress-layout.util.ts`)

- New: `TEAM_PROGRESS_TIME_PX.chart = 180`. The bucket chart takes the band's place in tall, large and maximum views when it fits. The band (84) stays when the chart doesn't fit.
- The wide-view callout is now an 84 px top-3 unscheduled panel. 84 px is the most a 5-row wide card can spare while keeping two list rows.

## `GET api/v1/planner/team/week`, as implemented

- **Gate:** the same as `planner/team`. Controller-level `[Authorize(Policy = "TenantPolicy")]`, caller resolved to an employee, otherwise 404.
- **Population:** transitive active reports, read level by level through `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`. The closure repository is never used.
- **`weekStart`:** must be a Monday (validator returns 400). If it starts after the caller's own today: 400 `planner_not_today`. Default: the caller's current Monday.
- **Reads:**
  - One window, `[min person week start − 7 days, max person week end)`.
  - One read each for blocks (skipped with the Planner off), sessions and meetings.
  - No task rows are read.
- **Per person:**
  - Seven local days, each composed with `TeamScheduleComposer.Compose` and summed into `days[i]` by the person's own local date.
  - `PlannedElapsedMinutes = TaskTimeAttribution.Attribute(blocks, sessions, weekFrom, min(now, weekTo), now).PlannedMinutes`.
  - Previous figures: the same call shifted −7 days with cutoff `now − 7d`.
- **Totals:**
  - `PlannedMinutes` = task + meeting minutes for the whole week.
  - `UnscheduledMinutes` = outside slot + no plan.
  - `CoveragePercent = min(100, scheduled ÷ elapsed planned task minutes × 100)`, one decimal, rounded half away from zero. It is null without elapsed plan or with the Planner off.
  - `Previous.PlannedMinutes` = last week's elapsed planned task minutes.
- **Members:** ordered by planned desc, then clocked desc, then name.
- **Planner off:** every plan field is 0, coverage is null, clocked minutes are kept.
- **p95 on dapi: not measured** (browser pass skipped). The integration test shows the number of database commands doesn't grow with team size (1 vs 5 reports, equal command count).

## Attendance-rate tile (Task 8)

**Kept.** It reads `TeamAttendanceApiService.getSummary({ mode: 'week' })`, the same source and covered population as the Team Attendance widget. It shows `periodRate`, and "vs last week" from `deltaPp`.

- If that call fails, the tile is dropped.
- A null rate shows "—".

## Screenshots and demo seed

None taken, and no demo Planner seed script written. Both wait for the browser pass, which also needs the section heights measured and the 2x2→12x8 `scrollHeight - clientHeight == 0` sweep.

## Tests run (real counts)

**Backend:**

- `verify.ps1 backend -Full`: build PASS, unit **7460/7460**, architecture **755/755**, integration **925 passed, 1 skipped, 0 failed**.
- `TeamScheduleWeekIntegrationTests`: **1/1** (run alone too).
  - The first attempt during Task 4 could not run: the Docker Desktop engine was returning 500. It passed once Docker recovered.
- `find-unauthorized-endpoints.ps1 backend`: **0** actions without an authorization attribute (32 explicit `[AllowAnonymous]`, all existing).
- `security-scan.ps1 backend -Baseline origin/development`: gitleaks, semgrep (0 new), devskim, osv and trivy pass. **nuget 3 high** and **workflows 8/7/1** findings concern packages and CI workflow files this branch did not touch.

**Frontend:**

- `verify.ps1 frontend`: zero-warnings build PASS. Unit: **5207 passed, 9 failed, 6 errors** across 568 files.
  - The 3 failing files are outside this work: `public/ui/landing-header`, `work/ui/task-comments`, `people/feature/employee-detail`.
  - Run alone, they pass **86/86**: timing flakes under full-suite load. None of them imports a dashboard file.
- **Pre-existing / outside scope:** the 9 flaky failures above, plus the nuget, workflow and osv scanner findings.
- `npm run check:warnings`: clean after every task.
- ESLint on every changed `.ts` and `.html` file: **0**.
- Prettier `--check` on the 13 created files: clean. The rest of `verify.ps1 -Format` didn't run, because the unit step failed first.
- `security-scan.ps1 frontend -Baseline origin/Development`: gitleaks, semgrep (0 new), devskim, npm-audit (shipped deps) and trivy pass. **osv 4 high / 8 medium** includes dev dependencies and is unchanged by this branch (no package changes).
- **Per-task targeted runs:**

  | Task | Specs | Result |
  |---|---|---|
  | 5 | team-schedule specs | 42/42 |
  | 6 | presence layout | 11/11 |
  | 7 | card + format | 31/31 |
  | 8 | card + popup + chart | 32/32 |
  | 9 | schedule layout | 10/10 |
  | 10 | planned-actual specs | 15/15 |
  | 11 | schedule card + format | 56/56 |
  | 12 | all team-schedule specs | 63/63 |
  | 13 | team progress + charts + layout | 199/199 |

## Migrations

**None.**

## Rulings made during the build (for your review)

1. **Task 1:** the test fixture schedule is 09:00-17:30, so the expected-minutes assertion is 510, not the plan's 480.
2. **Task 3:** `Previous.PlannedMinutes` is last week's *elapsed planned task* minutes. The input has no previous meeting or whole-week plan. So "Planned vs last week" compares this week's whole plan with last week's elapsed task plan.
3. **Task 4:** the week handler reads no task rows. One read window covers both weeks. The integration test uses small DispatchProxy fakes for identity and the reporting line, with the real EF repositories.
4. **Task 6:**
   - `PresencePlan` gained `viewAll`.
   - Narrow cards need 340 px for xs, else mini.
   - At 2x2 the 140 px donut leaves no room for the pill.
   - The no-empty-space test allows under one row plus its gap.
5. **Task 7:**
   - Card tiles are card-local tone tiles, not `app-kpi-tiles`, because its margin breaks the 72 px budget.
   - Icons are `users` / `clock` / `alert-triangle`: there is no coffee or calendar icon in the shared set.
   - The card loads the working-hours week once, when the trend first fits.
6. **Task 8:**
   - The team-average line is per person on its own scale.
   - Leave is a red count mark (people), never a minutes segment.
   - The Attention column is kept.
   - "Not clocked in" moved from a tile to a line under the donut.
7. **Task 9:**
   - xs needs 412 px.
   - The day chart shows one row per shown person.
   - The week chart is reserved before the day chart at the maximum size.
   - The spec was mutation-checked instead of being seen failing first.
8. **Task 11:**
   - With the Planner off, standard views show "Planner is off" as a chip beside the title.
   - Unscheduled colour changed amber → red hatched, so breaks can be amber.
   - Per-day week coverage only for finished days.
9. **Task 12:**
   - The week status filter uses each person's status today.
   - The Coverage tile replaces In plan in Week mode.
   - The "Unscheduled people" day tile moved to the side column.
10. **Task 13:**
    - The weekly payload has completed hours only, so the % line is the cumulative completed share of the total allocation.
    - At 12x8 the card keeps 4 list rows (was 6) because of the chart.
    - Buckets don't split outside slot from no plan.
    - The popup already had Total allocated and employee search + paging. A Project breakdown panel was added (All time only).

## Open questions and limitations

- **Browser pass, still to do:**
  - Measure section heights and update the `*_SECTION_PX` constants.
  - Sweep 2x2 to 12x8 for overflow.
  - Screenshots with the Planner off and on.
  - p95 of `planner/team/week`.
  - A demo seed script in `../dev-env/scripts/`.
- Card tile icons are the closest shared icons, not the preview's.
- Ranking stays the workload ranking. "Top performers %" is still your call.
- The 140 px donut does not fit a 2x2 body (136 px). This also affects the existing Team Attendance reference card. To be confirmed in the browser pass.

## Follow-up changes (same day, from your review of the popup and cards)

| Change | Commits |
|---|---|
| Team Progress popup: Project breakdown is now a bar chart (allocated vs completed hours per project, one shared scale). | FE `7a01dcd0` |
| Team Progress popup: Workload ranking spans two grid columns, so its table no longer scrolls sideways. | FE `7a01dcd0` |
| Overdue tasks: the total overdue estimated hours sit on top, with a pie of overdue hours by project and a table of overdue tasks showing each task's allocated (estimated) hours. Card: the overdue panel title shows the total overdue hours and each row shows its task's hours ("—" without an estimate). | BE `343b723e`, FE `d31d8df3` |
| Working / Break / Leave: the donut is in hours. The centre shows the team's total working hours, and the legend shows working, break and leave hours. In xs, an employee-by-employee working-hours breakdown (worked / break / leave on one bar) sits under the donut. | BE `6d34d51f`, FE `d07a4ff4` |

- **New API fields:**
  - `team/today` members: `LeaveMinutes`. It is the work window when on leave, and only where the viewer may see the leave. Masked leave adds 0.
  - `led work progress`: `EstimatedHours` on each overdue task, plus `OverdueHours`, `OverdueUnestimatedTaskCount` and `OverdueByProject`.
  - The handler now uses one grouped query for every overdue task, in place of the conditional count query.
- **Overdue pie with a module filter:** the server totals are per project only. With a module filter, the pie and total cover the listed overdue tasks only, and the total is marked "(listed tasks)".
- **Tests:**
  - BE `verify.ps1 backend`: unit 7464/7464, architecture 755/755.
  - Overdue integration tests: 4/4.
  - FE: all dashboard specs 845/845 (81 files), check:warnings clean, ESLint 0.
- **Not done:** browser check of these changes.
