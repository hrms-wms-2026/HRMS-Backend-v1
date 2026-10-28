# Execution prompt — My Team three widgets (backend + frontend)

Copy everything below the line into the executing agent (Claude Code, Cursor, Manus, …). It is self-contained.

---

You are implementing three **My Team** dashboard widgets for an HRMS + Work Management product, across two
repositories on the local machine:

- Backend (.NET 9, EF Core, MediatR, PostgreSQL with RLS): `C:\Users\User\Desktop\build\HRMS-Backend-v1`
- Frontend (Angular standalone + signals, Tailwind, Vitest via `ng test`): `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`

The three widgets:
1. **Team Progress** (existing key `team-progress`) — upgraded in place: Week ⇄ Month, planned vs worked time,
   unscheduled work, workload ranking. Its existing allocation numbers must not change.
2. **Today's Team Schedule** (new key `team-schedule`) — each report's plan for today, live progress, unscheduled
   work, conflicts, execution ranking.
3. **Working / Break / Leave** (key `team-status`) — **replaces** the Team Status widget under the same key.

## Two rules that override everything else

1. **Stay in scope.** Touch only the files the plan names (Work Management, Planner, the Team Status/attendance team
   query, the dashboard widgets). Other modules belong to a teammate: do not refactor, rename or "fix" anything outside
   the plan. If a task seems to need an out-of-scope change, stop and ask.
2. **Match the existing code, do not invent.** Before writing a handler, repository, card or util, open its nearest
   existing sibling and copy its shape: e.g. `GetLedPeriodProgressQueryHandler` for a led-scope query,
   `GetCoveredTeamTodayQuery` (in `TimeAttendance/Queries/AttendanceReadHandlers.cs`) for coverage and leave masking,
   `PlannerActuals` for day maths, `team-attendance-card` + `team-attendance-layout.util.ts` for an adaptive card,
   `team-progress-detail-dialog` for the popup shell.

## Before you do anything: check current state

- In both repos run `git status`, `git branch --show-current` and `git log --oneline -15`. The plan's checkboxes and
  this prompt may be stale: if a task's commit message already exists in the log, verify it and skip it.
- Run `git log -1 --format=%cd --date=relative` in both repos. If the last commit is minutes old, another session may
  still be working: stop and ask the user before touching that repo.

## Read first (in this order)

1. `HRMS-Backend-v1/CLAUDE.md` and the frontend repo's `CLAUDE.md` — build/test commands and hard rules.
2. The design: `HRMS-Backend-v1/docs/superpowers/specs/next/2026-10-29-my-team-three-widgets-design.md`
3. The plan you execute: `HRMS-Backend-v1/docs/superpowers/plans/next/2026-10-29-my-team-three-widgets.md`
   (its **Global Constraints** apply to every task; its **Review Focus** lists the edge cases most likely to bite).
4. Background: `HRMS-Backend-v1/docs/reports/work-management/2026-10-28-day-planner-backend-explained.md` (Planner v1;
   v1.1 is **not** merged — do not use anything from `origin/feature/planner-calendar-merge`).

## How to work

- Task 0 first. The frontend has uncommitted Quick Actions work on `chore/uat-hardening`: **stop and ask the user**
  whether to commit it. Never stash, reset or discard it. Then merge `origin/development` into the frontend branch and
  create `feature/my-team-three-widgets` in both repos.
- Then backend Tasks 1 → 8, frontend Tasks 9 → 11, the test-coverage audit (Task 12, **mandatory, run last**), then the
  gates (Task 13). Frontend tasks depend on backend response shapes, so keep the order.
- TDD in every task: write the test, see it fail, implement, see it pass, commit with the plan's message. One commit
  per task (Task 12: one commit per section). Commit messages end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` when you are Claude; otherwise your own tool's trailer.
- Iterate with `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~<name>"` and
  `npx ng test --watch=false --include <spec glob>`. Finish with the full gates in Task 13.
- If `dotnet build` fails with a NuGet.targets error mentioning `Value cannot be null` / `path1`, it is a stale MSBuild
  server, not your code: run `dotnet build-server shutdown` and retry.
- `-p:BuildProjectReferences=false` skips rebuilding `ONEVO.Application`; build `src/ONEVO.Application` first if you use it.
- Do not leave an API or dev server running (it locks the Debug bin and breaks the user's build). Build with `-c Release`.
  Never kill processes you did not start.
- The user runs the API and signs in at `https://dapi.onexso.com:4200`. Ask them to restart the API before the browser pass.

## Rules you must not break

- **Never apply a database migration.** No `dotnet ef database update`, no `setup-local-db.ps1 -RunMigrations`, no SQL
  against a shared or local database that writes. None is expected; if Task 2's query plan really needs an index, create
  the migration, dry-run it only inside `BEGIN … ROLLBACK`, commit the code, and tell the user the exact command to run.
  This is not optional: the database holds real local dev data.
- **Never push, open a PR, or move plan/spec files** from `next/` to `finished/`. The user does that after their
  browser sign-off.
- Tenant isolation: every new read is tenant-filtered and runs under RLS; the architecture suite must stay green.
  `find-unauthorized-endpoints.ps1 backend` must report 0. New endpoints copy the permission attributes of their sibling
  (`team/today` for attendance, `led-progress/period` for led progress, `employees/{id}` for planner).
- Estimate hours, planned minutes, clocked minutes and attendance minutes are **never** added together or drawn on one
  axis. "Not yet tasked" is never called "Unscheduled".
- A missing denominator is `null` in the API and "—" with a reason in the UI. Never 0% or 100% by default.
- Private meeting titles show as "Private meeting" for managers everywhere, including CSV. No leave reason anywhere;
  leave type only where Team Status shows it today. A viewer not leave-authorized for someone never sees them as Leave.
- A schedule never makes someone "Working". Only the attendance resolver does.
- Card standard: 2×2 minimum; donut always `DASHBOARD_DONUT_PX` (140 px), never resized; bigger card = more detail;
  padding `px-4 py-3`, header `mb-2`; views from `injectHostSize()` with ~20 px hysteresis; popup = `app-modal size="2xl"`
  with one KPI tile row, CSV export, no tabs. Use `styleUrl`, never an imported shared styles const (it hangs the build).
- `npm run check:warnings` must report 0: no needless `??` / `?.`; map lookups typed `T | undefined`.
- No new npm or NuGet packages, no chart library, no mock or hard-coded data in production code, no suppressed
  analyzers/tests, no `--no-verify`.
- `Planner:Enabled` is off locally: schedule figures must be replaced by "Planner is off", not shown as 0.

## Report when done

Write `HRMS-Backend-v1/docs/reports/work-management/2026-10-29-my-team-three-widgets-summary.md` with: files changed
per repo; components and services reused; the attribution and score formulas as implemented; endpoints added or
extended; tables created (expected: none) and the Team Progress month perf number from Task 8 Step 6; every test command
you ran with its real pass/fail counts (pre-existing failures listed separately); commit hashes per repo; migrations to
apply (expected: none); open questions and limitations. Never claim a test passed that you did not run; if something
could not run (e.g. Docker for integration tests), say so.
