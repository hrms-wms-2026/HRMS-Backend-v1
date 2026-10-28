# Weekly Work Progress widget — implementation summary

Date: 2026-10-28. Branch `chore/uat-hardening` in both repos. Everything is committed locally and **nothing is pushed**.
Spec: `docs/superpowers/specs/next/2026-10-28-weekly-work-progress-widget-design.md`.
Plan: `docs/superpowers/plans/next/2026-10-28-weekly-work-progress-widget.md`.

## What was built

The My Work card under widget key `today-total-time` is now **Weekly Work Progress**:
- **Size and views:** the minimum size is 2×2. The card has four size-driven views (mini, mini-wide, xs, standard) plus a detail popup.
- **Data path:** it loads from the dashboard **bootstrap**. It makes no API call of its own on load.
- **Bootstrap clean-up:** the bootstrap no longer loads data for widgets the web hides. `my-tasks` was loading on every My Work load.

## Files

### Backend (HRMS-Backend-v1)

**Created**
- `Application/Features/WorkManagement/WeeklyProgress/`
  - `DTOs/WeeklyWorkProgressResponse.cs`
  - `Services/WeeklyWorkProgressCalculator.cs`
  - `Services/WeeklyWorkProgressInput.cs`
  - `RepositoryInterfaces/IWeeklyWorkProgressReadRepository.cs`
  - `Queries/GetMyWeeklyWorkProgress/{Query,QueryHandler}.cs`
- `Infrastructure/Persistence/Repositories/WorkManagement/WeeklyProgress/EfWeeklyWorkProgressReadRepository.cs`
- `Api/Controllers/Tenant/WorkManagement/MyWorkProgressController.cs`
- Migration `20261028170646_AddWeeklyProgressReadIndexes`

**Modified**
- `Dashboard/Layout/Registry/{GridModels,DashboardWidgetRegistry}.cs`
- `Queries/GetDashboardBootstrap/GetDashboardBootstrapQueryHandler.cs`
- `Queries/GetWidgetData/GetWidgetDataQueryHandler.cs`
- `Api/Dashboard/Widgets/WidgetDataProviders.cs`
- `Planner/Services/{PlannerActuals,PlannerService}.cs`
- `Infrastructure/DependencyInjection.cs`
- `TaskClockingSessionConfiguration.cs`
- Model snapshot

**Tests**
- Unit: `WeeklyWorkProgressCalculatorTests` and `GetMyWeeklyWorkProgressQueryHandlerTests`.
- Unit: `WeeklyWorkProgressWidgetDataProviderTests`, plus updated registry, bootstrap and widget-data tests.
- Integration: `WeeklyWorkProgressReadRepositoryTests`.

### Frontend (Hrms--Web-application---front-end---v1)

**Created**
- `models/weekly-work-progress.model.ts`
- `data-access/weekly-work-progress-api.service.ts`
- Utils in `utils/`:
  - `weekly-work-progress-layout.util.ts`
  - `weekly-work-progress-insights.util.ts`
  - `weekly-work-progress-format.util.ts`
- `ui/daily-work-chart/`
- `feature/weekly-work-progress-detail-dialog/`
- A spec next to each of the above.

**Rewritten:** `feature/weekly-progress-card/`.

**Deleted:** the orphan `feature/today-total-time-card/`.

The user's uncommitted Quick Actions work on the same branch was not touched or committed.

## Existing components reused

- **Card building blocks:** `app-widget-card`, `app-popout-header-actions`, `app-kpi-tiles`, `app-modal`, `app-select`.
- **Dashboard helpers:** `injectHostSize`, `DASHBOARD_DONUT_PX` (140 px donut, never resized), `donutArcs` (status-donut util), `injectWidgetDataFeed` (bootstrap feed), `formatMinutes`.
- **Directives:** `appKeyboardClick` and `appStopClickPropagation`.
- **Look:** the popup copies the Team Progress / Team Attendance shell. No chart library was added; the daily chart is HTML bars plus an SVG line.
- **Backend:** the calculation reuses the Planner's day rules, now shared in `PlannerActuals`:
  - day bounds in the employee's zone
  - meeting minutes per day, with all-day meetings counted as 480 minutes
  - clock sessions clipped to the day, with open sessions counted up to now
  - attendance minutes while clocked in

  It also reuses `ExpectedWorkdayCalendar` and `AttendanceScheduleResolver`.

## Calculations

The week runs Monday to Sunday in the employee's timezone, using the same resolver as the Planner.

**Per day**
- **Expected:** the legal entity's work window (Dapi: 540 min) on expected days, minus leave. Expected days are working weekdays that are not holidays and fall inside the hire and termination dates.
- **Leave:** the overlap of approved leave with that day's work window.
- **Scheduled:** planned task blocks plus meetings that were not declined or cancelled.
- **Worked:** logged task minutes (every task, not only planned ones) plus credited meetings (meetings that have ended).
- **Attendance:** reported beside Worked, never added into it.

**Per week**
- Expected, scheduled, worked and attendance totals.
- **Expected to date:** past days plus the elapsed share of today's window.
- **Remaining:** expected minus worked, never below 0.
- **Progress %:** worked ÷ expected.
- **Planning coverage %:** scheduled ÷ expected.
- **Schedule adherence %:** Σ min(worked, scheduled) ÷ Σ scheduled over the days so far.
- **Status:** worked ÷ expected-to-date. ≥ 1.0 is Ahead, ≥ 0.85 is On Track, otherwise Behind. No working hours gives "no target".

**Last week:** the same figures for the previous Monday–Sunday. The card shows the progress change in percentage points.

**Task weightage:** for the caller's assigned non-meeting tasks that are due in the week, planned in it or clocked in it, estimated hours are split by status category (done / active / not started). This stays separate from worked hours.

## Summary tables, background processing, freshness

- **No summary table was created** (spec 6.4). One request reads 14 days for one employee with indexed queries, each source read once.
- **New index:** the only missing one was added, `task_clocking_sessions (tenant_id, employee_id, clock_in_at)`.
- **Freshness:** figures are computed on every bootstrap and widget refresh. The card refreshes through the dashboard every 60 s while the tab is visible, and again when the tab becomes visible.
- **When to add a summary table:** for a team roll-up of this metric, or if the provider's p95 goes over 150 ms on real data.

## API

`GET /api/v1/work/my-weekly-progress?weekStart=yyyy-MM-dd`
- `weekStart` is optional. It must be a Monday, not in the future and at most 52 weeks back; otherwise 422.
- The response always includes the week before the requested one.
- Authorization is `TenantPolicy`, with no module gate. Only the caller's own data is returned.
- 404 when the caller has no employee record.

Bootstrap: `WeeklyWorkProgressWidgetDataProvider` (key `today-total-time`) mirrors that endpoint for the current week.

## Tests run (real results)

| Command | Result |
|---|---|
| `verify.ps1 backend` (Release build + unit + architecture) | build PASS, unit **7312/7312**, architecture **755/755** |
| After the final-review fix: unit `--filter Dashboard\|WeeklyProgress`, architecture | **275/275**, **755/755** |
| Integration `--filter WeeklyWorkProgressReadRepository\|Planner` (Docker) | **19/19** |
| `find-unauthorized-endpoints.ps1 backend` | **0** actions without authorization |
| `ng test` dashboard/**/*.spec.ts | 66 files, **687/687** (before the popup fix added one more test, now 10/10 in its file) |
| `npm run check:warnings` | clean: no warnings, no errors |
| `eslint src/app/modules/dashboard` | 0 problems |
| `verify.ps1 frontend` | build clean; unit **4796/4803** |

The 7 failures in the full frontend run:
- **1** was the new popup test, written to fail before its fix. It is green now.
- **6** are in three `work/*` specs. They pass alone (173/173) and are the known load-timeout flakes.

## Final review

This was a self-review, not a separate reviewer. It found two issues; both are fixed, each with a test that failed first.

1. **The popup kept reloading last week.** It reloaded on every 60 s card refresh and briefly showed this week. Now it only resets when the week changes.
2. **HR-only tenants would have seen a permanent error card.** The endpoint first required a Work Management module, but every My Work user has the widget. The module gate is removed, matching the Planner.

## Limitations and pending items

- **Browser pass not done.** The user must:
  1. Rebuild and restart the API. The running API is old code without this endpoint or provider.
  2. Sign in at `https://dapi.onexso.com:4200`.
  3. Check the card at 2×2, 5×2, 7×2, 2×5, 6×3, 6×5 and 6×7, plus the popup and a phone-width screen.
- **Migration not applied:** `20261028170646_AddWeeklyProgressReadIndexes`. It creates the index non-concurrently, so apply it in a quiet window on large data.
- **Most figures are 0 locally.** The Planner flag is off and `planned_task_blocks`, `task_clocking_sessions` and `meeting_details` are empty. Only Expected, Attendance and Task weightage have real local data. Worked will read 0 until people clock task time.
- **Planner v1 on `development` only allows planning today**, so future days of the week show 0 scheduled until v1.1 (`feature/planner-calendar-merge`) is merged.
- **The user's original Markdown spec was never received.** The status thresholds, the leave rule, the task-weightage selection and the insight wording are my calls (spec §6, §9.3).
- **Deferred minors:**
  - The card's last-week copy isn't refreshed while it stays on "Last week".
  - `GET widgets/available` still lists the web-hidden widgets; the web filters them out.
