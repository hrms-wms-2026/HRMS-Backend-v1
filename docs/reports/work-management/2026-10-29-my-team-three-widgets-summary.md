# My Team three widgets — implementation summary

**Built:** 2026-10-29 to 2026-11-02 on `feature/my-team-three-widgets` in both repos (local, not pushed).
**Plan:** `docs/superpowers/plans/next/2026-10-29-my-team-three-widgets.md` · **Design:** `docs/superpowers/specs/next/2026-10-29-my-team-three-widgets-design.md`
**Status:** every gate is green, including the full integration suite, and the Team Progress month timing passes the 500 ms gate. Only your browser pass is left. Plan and spec stay in `next/` until you sign off in the browser.

## 1. What shipped

| Widget | Key | What changed |
|---|---|---|
| Working / Break / Leave | `team-status` (same key, new title) | Replaces Team Status. Counts for Working (blue), Break (amber) and Leave (red); everything else goes on one neutral line. Each person has a since time, elapsed time, a time-left bar (only when a break allowance or shift end exists) and attendance minutes. The popup has KPI tiles, the right-now donut, the week's working-hours chart, the member table with an attention column, and CSV export. |
| Today's team schedule | `team-schedule` (new) | Each report's own local today: Planner blocks, meetings, live task status, unscheduled work, conflicts and the 60/40 execution score. Mini view: task donut plus an alert. Medium: rows with plan bars. Large: a timeline. The popup has a date navigator (today and earlier), the ranking, unscheduled work, conflicts and CSV export. |
| Team Progress | `team-progress` (upgraded) | Planned vs worked band, unscheduled-work callout and workload ranking on the card; three new popup sections (planned vs worked chart, unscheduled work table, workload table, both with CSV). The allocation figures are untouched. |

## 2. Files changed

### Backend (HRMS-Backend-v1), 15 commits, 53 files

New:
- `Features/WorkManagement/Planner/Services/TaskTimeAttribution.cs` — the pure split into scheduled, outside slot and no plan.
- `Features/WorkManagement/Planner/Services/ExecutionScore.cs` — the 60/40 score and its ranking.
- `Features/WorkManagement/Planner/RepositoryInterfaces/ITeamTimeReadRepository.cs` and `Infrastructure/.../Planner/EfTeamTimeReadRepository.cs` — batched blocks, sessions, tasks, timezones, people and assignees.
- `Features/WorkManagement/Planner/TeamSchedule/*` — query, handler, response and the pure `TeamScheduleComposer`.
- `Features/WorkManagement/Leadership/Services/LedTimeProgressCalculator.cs` and `Queries/GetLedTimeProgress/*` (query, handler, validator).
- `Features/WorkManagement/WeeklyProgress/Services/LeaveWorkWindow.cs` — leave-hours helper moved out of the weekly handler so both widgets share it.
- `Features/TimeAttendance/Team/Services/TeamPresenceMath.cs` — since-when, break remaining and shift end.
- `Features/TimeAttendance/Team/Services/CoveredTeam.cs` — Team Status population plus leave masking, shared by team-today and working-hours.
- `Features/TimeAttendance/Team/Queries/GetTeamWorkingHoursQuery*.cs` and `DTOs/TeamWorkingHoursResponse.cs`.

Modified:
- `TimeAttendance/Queries/AttendanceReadHandlers.cs` — team-today fills the new fields and uses `CoveredTeam`.
- `TimeAttendance/Team/DTOs/TeamTodayResponse.cs` — new member fields and `AsOf`.
- `Meetings/RepositoryInterfaces/IMeetingRepository.cs` and `EfMeetingRepository.cs` — `ListEmployeesDayMeetingsAsync`. The single-employee reader now calls it, so the predicate exists once.
- `Leadership/DTOs/LedProgressDetailResponses.cs` — the new time DTOs.
- `WeeklyProgress/.../GetMyWeeklyWorkProgressQueryHandler.cs` and `WeeklyWorkProgressCalculator.cs` — call the moved leave helper.
- `Dashboard/Layout/Registry/DashboardWidgetRegistry.cs`, `Access/DashboardWidgetAccess.cs`, `Team/DTOs/MyTeamCapabilitiesResponse.cs` (`HasReports`) and `Team/Queries/GetMyTeamCapabilitiesQueryHandler.cs`.
- `Api/Dashboard/Widgets/WidgetDataProviders.cs` — `TeamScheduleWidgetDataProvider`.
- Controllers: `TimeTrackingController`, `PlannerController` (its `Respond` is now generic) and `WorkLeadershipController`.
- `Infrastructure/DependencyInjection.cs`.

### Frontend (Hrms--Web-application---front-end---v1), 6 commits, 69 files

- New cards and popups: `feature/team-status-card` (rewritten), `feature/team-status-detail-dialog`, `feature/team-schedule-card`, `feature/team-schedule-detail-dialog`.
- New UI components: `ui/working-hours-chart`, `ui/team-progress-time` (card sections), `ui/team-progress-time-detail` (popup sections).
- Utils: `team-presence-format`, `team-presence-layout`, `team-schedule-format`, `team-schedule-layout` and `team-progress-time-format`, each with a spec, plus `.testing.ts` fixtures. `team-progress-layout` now reserves room for the time sections.
- Data and state: `team-schedule-api`, `team-working-hours-api`, `team-progress-detail-api` (`getTimeProgress`), `team-schedule.store`, and `my-team-led-progress.store` (time slice).
- Models: `team-schedule`, `my-team-today` (new fields), `team-progress-detail` (time types) and `my-team-capabilities` (`hasReports`).
- `widgets/widget-registry.ts` registers `team-schedule`; `utils/my-team-widget-visibility.util.ts` gains `teamScheduleVisible`.
- Deleted: `utils/team-status-groups.util(.spec).ts`, used only by the old Team Status card.

## 3. Reused, not rebuilt

`PlannerActuals` (day bounds, attendance minutes, meeting minutes in day), `ExpectedWorkdayCalendar`, `AttendanceScheduleResolver`, `EmployeeVisibilityScopeMatcher`, `LedScopeFilter` / `LedProgressPeriod`, the common `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`, `donutArcs`, `injectHostSize`, `DASHBOARD_DONUT_PX`, `app-widget-card`, `app-popout-header-actions`, `app-modal`, `app-kpi-tiles`, shared `app-data-table` and `toCsv` / `downloadCsv`.

## 4. Formulas as implemented

**Attribution** (one person, window `[from, to)`, open session ends at `now`):
- Merge each task's sessions and blocks.
- `scheduled` = minutes of the sessions ∩ blocks of the **same** task.
- `outsideSlot` = the rest of a task that has a block in the window; `noPlan` = all of a task that has none.
- `parallelClockMinutes` = (sum of per-task clocked minutes) − (union across tasks).
- Rounding is half away from zero; the remainder goes to outside slot or no plan, so `scheduled + outsideSlot + noPlan == clocked` always holds.

**Execution score (today):**
- `itemCompletion` = completed due items ÷ items due by now (a meeting counts as done once it ends).
- `timeCoverage` = min(100, scheduled ÷ planned task minutes elapsed by now).
- `score = 0.6 × itemCompletion + 0.4 × timeCoverage`, with one decimal, half away from zero.
- Not ranked when Planner is off, on leave, nothing is due yet, or there is no planned time; a part that has a denominator is still shown.
- Ranking order: score, then item completion, then time coverage, then name.

**Team Progress time:**
- Window = `LedProgressPeriod.Window` of the **UTC** today, the existing convention; each person's minutes go to **their own** local days.
- `PlannedMinutes` = the whole period's planned commitment.
- `CoveragePercent` = scheduled ÷ planned task minutes **elapsed by now**, capped at 100 and null without a plan.
- Previous period: the same elapsed span. For a week, `from−7 … today−7`; for a month, the first of the previous month to the same day, clamped to that month's length. The previous `now` is shifted back by the same number of days.
- Buckets: days for a week, ISO weeks clipped to the month for a month.
- Workload: ordered by planned, then clocked, then name. `utilization = planned ÷ expected` (not capped). `expected` = working weekdays minus holidays across the whole period, times the work window, minus approved leave (`LeaveWorkWindow`); null without configured working hours.
- Unscheduled list: the newest 200, plus `UnscheduledTotal`.
- Planner off: every plan figure is 0 and coverage is null; clocked minutes still show.

**Working / Break / Leave:**
- `StateSince`: clock-in for working, the open break's start for on break.
- `BreakRemainingMinutes = max(0, allowance − used)`, only on break and only with an allowance.
- `ShiftEndsAt`: the legal entity's end time on the work date, only while working or on a break.
- `AttendanceMinutes = PlannerActuals.AttendanceMinutes(...)`.
- Week trend: an open record counts up to now today and up to local midnight on a past day. Leave is counted only for people the viewer is leave-authorized for.

## 5. Endpoints

| Method | Route | Gate (copied from sibling) |
|---|---|---|
| GET | `api/v1/attendance/time-tracking/team/working-hours?weekStart=` | `[RequirePermission("attendance:read")]` like `team/today` |
| GET | `api/v1/planner/team?date=` | `TenantPolicy`, no permission attribute, like `employees/{id}`; returns `plannerEnabled:false` instead of 503 |
| GET | `api/v1/work/led-progress/time?period=week\|month&projectId=&objectiveId=` | class-level module gate, like `led-progress/period` |
| GET | `team/today` (extended) | unchanged; adds `stateSince`, `breakRemainingMinutes`, `shiftEndsAt`, `attendanceMinutes`, `asOf` |
| GET | `dashboard/team/capabilities` (extended) | unchanged; adds `hasReports` |

`find-unauthorized-endpoints.ps1 backend` reports **0** actions without authorization.

## 6. Tables, migrations, performance

- **Tables created:** none. **Migrations to apply:** none.
- **Index:** not added. I did not run `EXPLAIN` on dapi's `planned_task_blocks`; block reads by task join through `tasks` and use the existing `task_id` FK index.
- **Team Progress month timing (Task 8 Step 6), measured 2026-11-02 on dapi** (local API, Release test build stopped first). Called from the signed-in browser: 2 warm-up calls, then 20 timed calls each.

  | Request | p50 | p95 | max | Team size |
  |---|---|---|---|---|
  | `GET work/led-progress/time?period=month` | 165 ms | **181 ms** | 202 ms | 25 people |
  | `GET work/led-progress/time?period=week` | 149 ms | **162 ms** | 191 ms | 25 people |

  Well under the 500 ms gate (spec §7), so no summary table is needed. **Caveat:** on dapi the Planner is off and there are no clocked sessions in October or November. The timing therefore covers scope resolution, the people set and capacity (employees, legal entities, holidays, leave), but not heavy block or session volume. Measure again once real clocking data exists.

## 7. Test runs (real counts)

| Command | Result |
|---|---|
| `verify.ps1 backend` (final, after the architecture fix) | build PASS · unit **7428 passed, 0 failed** · architecture **755 passed, 0 failed** |
| `verify.ps1 backend` (first full run, before the fix) | unit 7427 / 0 · architecture **753 passed, 2 failed** — see section 8 |
| Targeted `verify.ps1 backend -Filter …` during Tasks 1–8 and 12 | 8/8, 702/702, 711/711, 8/8, 44/44, 249/249, 197/197, 98/98 — all passed |
| `dotnet test` integration, filter `TeamTimeReadRepository\|Planner` (2026-10-29, Docker up) | **23 passed**, 0 failed |
| Full integration suite, `dotnet test tests/ONEVO.Tests.Integration -c Release` (2026-11-02, Docker up) | **924 passed, 0 failed, 1 skipped** of 925 (35 min). The skip is the existing `NotificationReleaseSmokeTests`, which needs a database copy; it is not from this branch. Includes the new `TeamTimeReadRepositoryTests` and the updated `MyTeamCapabilitiesIntegrationTests`. |
| `verify.ps1 frontend` | build-clean PASS ("no warnings, no errors") · unit **5146 passed (5146)** |
| `npm run check:warnings` after Tasks 9, 10 and 11 | clean each time |
| `npx eslint` on every changed `.ts` / `.html` file | 0 errors, 0 warnings |
| `security-scan.ps1 backend -Baseline origin/development` | semgrep, gitleaks and devskim: 0 new. NuGet (3 high) and workflows (8/7/1): existing dependency and CI findings; this branch changes no `csproj` or workflow file. |
| `security-scan.ps1 frontend -Baseline origin/Development` | semgrep, gitleaks, devskim and npm-audit (shipped deps): 0 new. OSV (4 high / 8 medium, including dev deps): existing; this branch changes no `package.json`. |

Pre-existing failures: none in any suite I ran.

## 8. Commits

**Backend:**

| Commit | Message |
|---|---|
| `c84c010a` | attribution |
| `7b8aae4e` | batched reads |
| `b114352d` | team-today fields |
| `2a572819` | working hours |
| `203b304e` | execution score |
| `d3b9ad0d` | team schedule query |
| `0ae228b6` | widget registration |
| `7039e069` | team progress time |
| `abde0499`, `403732e4`, `c21a482d`, `16b9f29d`, `5427fa61`, `ffb68828` | coverage audit |
| `e5ee3830` | architecture fix |

**Frontend:**

| Commit | Message |
|---|---|
| `44cd6653` | Working / Break / Leave |
| `eee6d51f` | team schedule |
| `71722242` | team progress upgrade |
| `d5cbac86` | Prettier on the new files only |
| `9f43fa13`, `5fb735dc` | coverage audit |

**Architecture fix (`e5ee3830`):** the plan said to use `IEmployeeHierarchyClosureRepository` for the schedule population and `HasReports`. `MyTeamBoundaryArchitectureTests` forbids that interface in WorkManagement and Dashboard/Team (My Team spec §10). Both now use the common `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`, the documented route the Planner and meetings already take. Transitive reports are read level by level, and a seen set guards against a cycle in bad data. The tests were not changed.

## 9. Calls made during the build and known limitations

1. **Team Progress period switch:** the card keeps its existing *All time / This month / This week* select instead of adding a second Week/Month select. Time figures follow it, with All time reading as this week, and the band and popup say which period they show.
2. **Coverage denominator:** coverage uses planned time **elapsed so far**, not the whole period's plan. Otherwise it would read low early in every week. The tooltip says so.
3. **Team Progress layout:** the time sections take list rows only while the module and project lists keep at least 2 rows. One existing layout expectation changed: a 12×8 card now shows ≥ 6 list rows instead of ≥ 8, because the band takes about two.
4. **Schedule presence chip:** comes from `GetCoveredTeamTodayQuery`, so it appears only for reports the viewer covers for attendance, only for today, and with Team Status's leave masking.
5. **Schedule dates:** the "future date" check uses the **viewer's** today. A report who is behind the viewer's timezone sees their own date, which may still be "yesterday" for them.
6. **Leave display:** a leave row shows type and end date only where Team Status already sends them (leave-authorized viewers). Half-day leave can't be told apart, because the payload has no interval.
7. **Working / Break / Leave member table:** limited to the 50 members `team/today` returns. The popup says "showing 50 of N"; the counts cover everyone.
8. **Links to time tracking:** the old Team Status card linked each person and "View team" to the time-tracking page; the new card opens its popup instead. Add the links back if they are missed.
9. **Still to do:** the browser pass at 2×2, medium and large plus each popup, with the Planner on and off, on dapi.onexso.com:4200 with you signed in. Then move the plan and spec to `finished/`.
