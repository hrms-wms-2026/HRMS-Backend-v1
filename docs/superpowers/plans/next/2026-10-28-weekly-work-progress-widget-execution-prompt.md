# Execution prompt — Weekly Work Progress widget (backend + frontend)

Copy everything below the line into the executing agent (Claude Code, Cursor, Manus, …). It is self-contained.

---

You are implementing the **Weekly Work Progress** dashboard widget for an HRMS + Work Management product, across two
repositories on the local machine:

- Backend (.NET 9, EF Core, MediatR, PostgreSQL with RLS): `C:\Users\User\Desktop\build\HRMS-Backend-v1`
- Frontend (Angular standalone + signals, Tailwind): `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`

## Read first (in this order)

1. `HRMS-Backend-v1/CLAUDE.md` and the frontend repo's `CLAUDE.md` — build/test commands and hard rules.
2. The design: `HRMS-Backend-v1/docs/superpowers/specs/next/2026-10-28-weekly-work-progress-widget-design.md`
3. The plan you execute: `HRMS-Backend-v1/docs/superpowers/plans/next/2026-10-28-weekly-work-progress-widget.md`
4. Reference implementations to copy patterns from:
   - Adaptive card: `frontend/src/app/modules/dashboard/feature/team-attendance-card/` + `utils/team-attendance-layout.util.ts`
   - Popup shell: `frontend/.../feature/team-progress-detail-dialog/`
   - Bootstrap feed: `frontend/.../widgets/widget-data-feed.ts` (used by `active-projects-card`)
   - Provider: `HRMS-Backend-v1/src/ONEVO.Api/Dashboard/Widgets/WidgetDataProviders.cs` (`TeamProgressWidgetDataProvider`)
   - Planner day maths: `HRMS-Backend-v1/src/ONEVO.Application/Features/WorkManagement/Planner/Services/PlannerService.cs` (`ComposeAsync`) and `PlannerActuals.cs`
   - Expected days: `.../TimeAttendance/Services/ExpectedWorkdayCalendar.cs`, `AttendanceScheduleResolver.cs`

## What you build (summary — the spec is authoritative)

- Backend: `WidgetDefinition.ShownOnWeb` so the bootstrap stops loading data for widgets the web hides; a pure
  `WeeklyWorkProgressCalculator`; a task-weightage read repository; `GetMyWeeklyWorkProgressQuery`;
  `GET api/v1/work/my-weekly-progress?weekStart=`; a bootstrap provider for widget key `today-total-time`
  (now "Weekly work progress", min 2×2).
- Frontend: model + API service, layout util (views `mini`, `mini-wide`, `xs`, `standard`), insights util,
  `app-daily-work-chart` (inline SVG), rewritten `weekly-progress-card` that loads from the bootstrap, and a
  `weekly-work-progress-detail-dialog` popup. Delete the orphan `today-total-time-card`.

## How to work

- Execute the plan task by task, in order: B1 → B5, then F1 → F7. TDD inside each task: write the test, see it fail,
  implement, see it pass, commit. One commit per task with the message given in the plan.
- Work on the current branch `chore/uat-hardening` in both repos. Do **not** create branches, push, open PRs, or apply
  migrations to any database. If B3 needs an index migration, create it and report it.
- Before F1, the frontend has uncommitted Quick Actions work. Stop and ask the user whether to commit it first. Never
  stash, reset or discard it.
- Use the dev-env gate: `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "<filter>"` while iterating,
  `pwsh ../dev-env/scripts/verify.ps1 backend` (and `-Full` if Docker is running) at the end;
  `npx ng test --watch=false --include <spec>` while iterating, `npm run check:warnings` and
  `pwsh ../dev-env/scripts/verify.ps1 frontend` at the end.
- If `dotnet build` fails with NuGet.targets "path1 null", run `dotnet build-server shutdown` and retry.
- Do not leave a Claude-started API running: it locks the Debug bin and breaks the user's build. Build with `-c Release`.
- The user runs the API and signs in to the browser at `https://dapi.onexso.com:4200` themselves. Ask them to restart
  the API before the browser pass.

## Rules you must not break

- Tenant isolation: every new query filters by tenant and runs under RLS; the architecture suite must stay green.
- `find-unauthorized-endpoints.ps1 backend` must report 0 unprotected actions.
- Worked hours = logged task minutes + credited meeting minutes. Never add task estimates or attendance into Worked.
- Card standard: 2×2 minimum; the donut is always `DASHBOARD_DONUT_PX` (140 px) and never resizes; a bigger card
  shows more detail, not bigger graphics; padding `px-4 py-3`; header `mb-2`; views from `injectHostSize()` with
  20 px hysteresis.
- No new npm or NuGet packages, no chart library, no mock data, no suppressed analyzers/tests, no `--no-verify`.
- Frontend template diagnostics are build errors: no needless `??` / `?.`; type lookups that can miss as `T | undefined`.
- Planner config `Planner:Enabled` is off locally: the widget must hide scheduled figures, not show `0h scheduled`.

## Report when done

Write `HRMS-Backend-v1/docs/reports/work-management/2026-10-28-weekly-work-progress-widget-summary.md` with:
files modified (per repo), existing components reused, backend calculations implemented, summary tables created or
reused (expected: none — spec 6.4), freshness strategy, API endpoints, every test command you ran with its real
pass/fail counts, commits (hashes), and limitations. Never claim a test passed that you didn't run; if something
couldn't run (e.g. Docker), say so.
