# Execution prompt: My Team widgets design match (backend + frontend)

Copy everything below the line into the agent that will run it (Claude Code, Cursor, Manus, …). It is self-contained.

---

You are reworking three **My Team** dashboard widgets in an HRMS + Work Management product. The work spans two repositories on the local machine:

- Backend (.NET 9, EF Core, MediatR, PostgreSQL with RLS): `C:\Users\User\Desktop\build\HRMS-Backend-v1`
- Frontend (Angular standalone + signals, Tailwind, Vitest via `ng test`): `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`

The widgets already exist and work: **Working / Break / Leave** (`team-status`), **Today's team schedule** (`team-schedule`) and **Team Progress** (`team-progress`). Their numbers are right, but they look much plainer than the user's design previews. This pass does four things:

1. Makes each widget look like the preview images.
2. Gives Working/Break/Leave and Today's team schedule a real **grow-by-detail** size ladder. A bigger card shows more sections and rows and never scrolls. Today they have a fixed 5 rows and a large view that scrolls.
3. Adds **planned vs actual work time** charts with summary tiles, for today (per person) and for the week (per day). This needs one new endpoint, `GET planner/team/week`.
4. Adds the matching Team Progress visual changes.

## Two rules that override everything else

1. **Stay in scope.** Touch only the files the plan names. Other modules belong to a teammate. Do not refactor, rename or "fix" anything outside the plan. If a task seems to need an out-of-scope change, stop and ask.
2. **Match the existing code; don't invent.** Before writing a handler, card, chart or util, open its nearest existing sibling and copy its shape:
   - adaptive card: `team-attendance-card` + `utils/team-attendance-layout.util.ts`
   - day handler: `GetTeamScheduleQueryHandler`
   - hand-built charts: `ui/working-hours-chart`, `ui/weekly-completion-chart`
   - popup shell: `team-progress-detail-dialog`

## Before you do anything: check the current state

- In both repos, run `git status`, `git branch --show-current` and `git log --oneline -15`.
  - Both repos must be on `feature/my-team-three-widgets`. If either isn't, stop and ask.
  - If a task's commit message is already in the log, verify that task and skip it.
- Run `git log -1 --format=%cd --date=relative` in both repos. If the last commit is only minutes old, another session may still be working. Stop and ask the user before you touch that repo.
- If there are uncommitted changes you didn't make, stop and ask. Never stash, reset or discard them.

## Read first, in this order

1. `HRMS-Backend-v1/CLAUDE.md` and the frontend repo's `CLAUDE.md`: build and test commands, and hard rules.
2. The design: `HRMS-Backend-v1/docs/superpowers/specs/next/2026-11-02-my-team-widgets-design-match-design.md`.
3. The three preview images in `HRMS-Backend-v1/docs/design-notes/my-team-widgets-2026-11-02/`. Look at them before every frontend task.
4. The plan you execute: `HRMS-Backend-v1/docs/superpowers/plans/next/2026-11-02-my-team-widgets-design-match.md`. Its **Global Constraints** apply to every task, and its **Review Focus** lists the edge cases most likely to bite.
5. Background on what already exists: `HRMS-Backend-v1/docs/reports/work-management/2026-10-29-my-team-three-widgets-summary.md`, and the original design `docs/superpowers/specs/next/2026-10-29-my-team-three-widgets-design.md`.

## How to work

- Run backend Tasks 1 → 4 first, then frontend Tasks 5 → 13, then Task 14 (measure, browser pass, gates, summary). The frontend depends on the backend response shapes, so keep this order.
- Use TDD in every task: write the test, see it fail, implement, see it pass, then commit with the plan's message. One commit per task.
  - Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` when you are Claude; otherwise use your own tool's trailer.
- Iterate with:
  - `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~<name>"`
  - `npx ng test --watch=false --include <spec glob>`
  - Run the full gates once, in Task 14.
- If `dotnet build` fails with a NuGet.targets error mentioning `Value cannot be null` / `path1`, the cause is a stale MSBuild server, not your code. Run `dotnet build-server shutdown` and retry.
- `-p:BuildProjectReferences=false` skips rebuilding `ONEVO.Application`. If you use it, build `src/ONEVO.Application` first.
- Don't leave an API or dev server running: it locks the Debug bin and breaks the user's build. Build with `-c Release`. Never kill processes you didn't start.
- Integration tests need Docker Desktop. If a run is stopped, remove the leftover `postgres:16-alpine` and ryuk test containers. Never touch the user's `supabase_*` containers.
- **Browser pass (Task 14).** The user runs the API (`localhost:7229`) and the web app (`https://dapi.onexso.com:4200`) and signs in in the **Playwright** window. Ask them to restart the API after the backend tasks. API calls from the signed-in page use `fetch(\`https://${location.hostname}:7229/api/v1/...\`, { credentials: 'include' })`. The Playwright window slows `requestAnimationFrame` to about 550 ms, so wait about 3 s after each resize before you measure.

## Rules you must not break

- **Never apply a database migration** and never run SQL that writes to any database. None is expected. Demo Planner data for the browser pass goes through the app's own Planner API, from a script in `../dev-env/scripts/`, never in `src/`.
- **Never push, open a PR, or move plan/spec files** from `next/` to `finished/`. The user does that after their browser sign-off.
- **Tenant isolation.** Every new read is tenant-filtered and runs under RLS, and the architecture suite must stay green. `find-unauthorized-endpoints.ps1 backend` must report 0. `planner/team/week` copies `planner/team`'s gate.
- **Never use `IEmployeeHierarchyClosureRepository`** in WorkManagement or Dashboard/Team (`MyTeamBoundaryArchitectureTests`). Reports come from `IEmployeeRepository.ListDirectReportEmployeeIdsAsync`, level by level.
- **Card standard:**
  - The donut is always `DASHBOARD_DONUT_PX` (140 px) and never resizes.
  - **A card never scrolls inside itself.** A bigger card adds sections and rows; it never stretches what is there. No empty space under the content.
  - Padding `px-4 py-3`, header `mb-2`.
  - Views come from `injectHostSize()` with about 20 px hysteresis.
  - Section heights are measured in the browser, not guessed.
  - Use `styleUrl`, never an imported shared styles const (it hangs the build).
- **Planner off.** `Planner:Enabled` is off on dapi. Every plan figure is replaced by a "Planner is off" state showing clocked time, never shown as 0 and never drawn as an empty chart.
- **Never mix units.** Estimate hours, planned minutes, clocked minutes and attendance minutes are never added together or drawn on one axis. "Not yet tasked" is never called "Unscheduled".
- **Missing values.** A missing denominator is `null` in the API and "—" in the UI, never 0% or 100% by default.
- **Privacy.** Private meeting titles show as "Private meeting" everywhere, including CSV. No leave reason anywhere. A viewer who isn't leave-authorized for someone never sees them as Leave.
- **Ranking.** Keep the workload ranking. Don't add "Top performers %". Don't add a Month view, "Add Schedule" buttons, or project/module filters to the Working/Break/Leave popup.
- `npm run check:warnings` must report 0: no needless `??` / `?.`, and map lookups are typed `T | undefined`. Run Prettier only on files you create.
- No new npm or NuGet packages, no chart library, no mock or hard-coded data in production code, no suppressed analyzers or tests, no `--no-verify`.

## Report when done

Write `HRMS-Backend-v1/docs/reports/work-management/2026-11-02-my-team-widgets-design-match-summary.md` with:

- the files changed in each repo
- the components reused, and the new ones
- the size ladder of each card (views, sections in priority order, the measured section heights)
- the `planner/team/week` formulas as implemented
- the endpoint's p95 timing on dapi
- the attendance-rate tile decision (Task 8)
- the screenshot list (each size and popup, Planner off and on) and where the demo seed script lives
- every test command you ran with its real pass/fail counts, with pre-existing failures listed separately
- the commit hashes in each repo
- migrations to apply (expected: none)
- open questions and limitations

Never claim a test passed that you didn't run. If something couldn't run (for example, Docker for integration tests), say so.
