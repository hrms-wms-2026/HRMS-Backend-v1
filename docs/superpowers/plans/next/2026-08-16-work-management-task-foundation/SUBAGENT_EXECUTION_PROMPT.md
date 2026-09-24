# Sub-agent Execution Prompt — Work Management Task Foundation

Copy everything below the line into a fresh Claude Code session (backend session for the backend
half, frontend session for the frontend half — run them as two separate sessions/agents, backend
first).

---

## MISSION

You are implementing an already-brainstormed, already-planned feature: **Work Management Task
Foundation** (Task board/backlog, task-creation-request approvals, an allocation-extend cascading
approval workflow, a shared notification foundation, and a calendar-deadline read endpoint). Every
design decision has already been made and written down. Your job is **execution, not design** —
if something in the plan seems ambiguous or wrong, stop and flag it rather than improvising a
different design.

**Read these two documents FIRST, in full, before touching any code:**

1. The design spec (WHY every decision was made):
   - Backend: `C:\Users\User\Desktop\build\HRMS-Backend-v1\docs\superpowers\specs\next\2026-08-16-work-management-task-foundation-design.md`
   - Frontend: `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1\docs\superpowers\specs\next\2026-08-16-work-management-task-foundation-design.md`
2. This repo's own process rules (HOW work gets done in this codebase — non-negotiable):
   - `docs\superpowers\rules\PROCESS_RULES.md`
   - `docs\superpowers\rules\FILE_CREATION_RULES.md`

## SCOPE GUARDRAIL — READ THIS TWICE

**Work Management module ONLY.** A teammate owns every other module (Core HR, Org Structure,
Calendar, Payroll, etc.) in both repos. Concretely:

- Backend: only touch `ONEVO.Domain/Features/WorkManagement/*`, `ONEVO.Application/Features/WorkManagement/*` (plus the two new Shared-Platform Notification files explicitly called for in Part 4 — nowhere else in SharedPlatform), `ONEVO.Api/Controllers/Tenant/WorkManagement/*`, related migrations/configurations, `docs/postman-request/Work Management/`.
- Frontend: only touch `src/app/modules/work/**` plus the two explicitly-named shared additions (the navbar bell, `core/services/notification-api.service.ts`, `core/state/notifications.store.ts`) — nothing else in `core/` or `layouts/`.
- **Never** touch `calendar_events` or any Calendar-module file (undesigned/unbuilt but owned by a teammate). Part 5's `my-deadlines` endpoint is Work Management's *entire* calendar-facing surface — do not build calendar UI, sync, or storage.
- **Never** touch already-shipped Phase 2 EmployeeId columns (`objectives.owner_id`, `projects.lead_id`, etc.) beyond what each task explicitly says to add.
- If you discover you need to change a file outside these boundaries to make something work, **stop and report it** rather than doing it — that's a sign the plan has a gap, not a green light to expand scope.

## THE PLAN FILES — YOUR ACTUAL WORK ORDER

### Backend (`C:\Users\User\Desktop\build\HRMS-Backend-v1`), execute in this exact order:

1. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-1-schema-and-crud.md`
2. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-2-task-creation-requests.md` (depends on Part 1)
3. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-3-allocation-extend-cascade.md` (depends on Part 1; independent of Part 2)
4. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-4-notification-foundation.md` (Tasks 1-3 are independent of everything else; Task 4 depends on Parts 2 and 3 being done, since it wires notification calls into their handlers; Task 5 is independent)
5. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-5-my-deadlines.md` (depends on Part 1 only)

Parts 2 and 3 can run in parallel with each other (two agents/sessions) once Part 1 is fully done
and merged, since neither touches the other's files. Part 4's Task 4 is the one hard
synchronization point — it cannot start until both Part 2 and Part 3 are committed.

### Frontend (`C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`), after the backend endpoints exist:

1. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-1-board-and-backlog.md`
2. `docs\superpowers\plans\next\2026-08-16-work-management-task-foundation\part-2-approvals-and-notifications.md`

These two can also run in parallel with each other — they touch almost entirely disjoint file
sets (Part 1: Board/Backlog/task-create-modal; Part 2: Approvals tab/navbar bell), except both
edit `work.routes.ts` — coordinate that one file's edits (rebase/merge, don't overwrite) if run
truly in parallel.

Each plan file already contains every task broken into bite-sized TDD steps (write failing test →
run it, confirm it fails → write minimal implementation → run test, confirm it passes → commit).
**Follow the steps in order, inside each task, inside each file, in the file order above.** Do not
skip ahead or reorder tasks within a file — later tasks depend on earlier ones' exact type/method
names (each task's "Interfaces: Consumes/Produces" header tells you exactly what's available from
earlier tasks).

## HOW TO EXECUTE — REQUIRED SUB-SKILL

This project uses the Superpowers skill system. Before starting, invoke:

```
superpowers:subagent-driven-development
```

(or `superpowers:executing-plans` if you're running this inline in one session rather than
dispatching a fresh subagent per task — both are named at the top of every plan file). Follow
whichever of those two skills you invoke exactly — they define the commit-per-task, review-between-
tasks discipline this codebase expects. Do not invent your own execution loop.

## RULES (from PROCESS_RULES.md / FILE_CREATION_RULES.md — do not skip these)

1. **One commit per task step**, using the exact `git commit -m "..."` message given at the end of
   each task in the plan files — do not batch multiple tasks into one commit, do not rewrite the
   commit messages.
2. **Every new tenant-owned table gets RLS** (`ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL
   SECURITY` + a `tenant_isolation` policy) in the same migration that creates it. This codebase
   has broken this twice before by accident — the plans give you the exact SQL block to copy each
   time. Never skip it, never defer it to "a follow-up migration."
3. **Every finished API endpoint gets a Markdown doc** under `docs/postman-request/<Module>/<Endpoint
   Name>.md` (backend only) — method+route, auth/permission line, description, request/response
   JSON examples, an error-status table, and a Source section linking the controller/handler files
   and this plan. Update `docs/postman-request/README.md`'s index in the same commit.
4. **Keep `docs/superpowers/plans/next/SUMMARY.md` (and `specs/next/SUMMARY.md`) current.** When
   you finish a whole part-file, note its status in that repo's `plans/next/SUMMARY.md`. When
   *all* parts of this plan (backend 5 parts, frontend 2 parts, in each repo respectively) are
   done and reviewed clean, move the whole `2026-08-16-work-management-task-foundation/` folder
   from `plans/next/` to `plans/finished/<completion-date>/` in each repo, and update both repos'
   `plans/SUMMARY.md` accordingly. Do not do this prematurely — only after a full clean review
   pass on every part.
5. **Whenever a task tells you to "read X before writing this" or "confirm the exact method name
   before proceeding"** (several tasks in Parts 3 and 4 flag this explicitly, e.g. confirming
   `IObjectiveChangeRequestRepository`'s real method names, or `IProjectRepository`'s task-number
   increment method) — actually stop and read that file. These are places the plan's own research
   pass didn't cover, flagged honestly rather than guessed. Do not assume the sketched code in the
   plan is 100% correct there; verify against real code first.
6. **Never** use `--no-verify`, skip pre-commit hooks, or bypass a failing architecture test by
   deleting/disabling it. If `TenantIsolationArchitectureTests` or any other architecture test
   fails after your change, fix the underlying gap (per rule 2 above), don't suppress the test.

## TESTING — WHAT "DONE" MEANS FOR EVERY TASK

Every task in every plan file already specifies its own tests inline (xUnit + Moq for backend,
Jasmine/Karma + `HttpTestingController` for frontend Angular). Follow the TDD loop exactly as
written per task:

1. Write the failing test exactly as given in the plan (or adapted per the task's own "confirm
   real method names first" note).
2. Run it, confirm it actually fails for the reason expected (missing type/method) — not for an
   unrelated compile error.
3. Write the implementation.
4. Run the test again, confirm it passes.
5. Commit.

**In addition to per-task tests, run these full-suite checks at the checkpoints the plans call
out:**

- Backend, after every migration (Part 1 Task 5, Part 2 Task 1, Part 4 Task 1): apply the
  migration to a real local/dev Postgres instance (`dotnet ef database update`), then run
  `dotnet test --filter TenantIsolationArchitectureTests` and confirm PASS. Also manually confirm
  via `SELECT tablename, policyname FROM pg_policies WHERE tablename IN (...)` that the RLS
  policy is actually live — a passing architecture test is necessary but the plans ask for this
  direct DB check too, since that's how a prior RLS gap in this codebase was actually caught.
- Backend, at the end of every part file: `dotnet test --filter FullyQualifiedName~WorkManagement`
  (Parts 1-3, 5) or `dotnet test --filter "FullyQualifiedName~WorkManagement|FullyQualifiedName~Notifications"`
  (Part 4) — full green run before moving to the next part.
- Frontend, at the end of every part file: `npm test` (full suite) and `npm run build` (confirms
  no TypeScript/template errors) — both clean before moving on.
- Frontend, Part 1 Task 9 and Part 2 Task 6 are **manual browser verification** tasks — actually
  start the dev server and click through the described flows (drag a card between columns,
  trigger the 409 allocation-exceeded case, click the notification bell) rather than treating them
  as optional. If you can't run a browser in your environment, say so explicitly instead of
  silently skipping and claiming the task is done.
- Whenever a task says "run the existing tests for file X and confirm they still pass unmodified"
  (this happens several times, e.g. Part 3 Task 3 must not regress the other five
  `ObjectiveChangeRequest` branches) — actually run that existing test file, don't assume it's
  fine because your new code compiles.

Never report a task complete without having actually run its tests and seen them pass. If a test
fails and you can't figure out why after reasonable investigation, stop and report the failure
with the actual error output — don't paper over it or weaken the assertion to make it pass.

## HOW TO HANDLE DOCUMENTATION / BUILDING CONTEXT

Before starting **each** part file (not just once at the very start):

1. Re-skim that part file's own header (Goal / Architecture / Tech Stack / Spec link / Global
   Constraints) — it names exactly which spec section it implements and what's out of scope.
2. If the part references an earlier part's types/interfaces (every "Interfaces: Consumes" line),
   confirm those actually exist in the codebase as committed (not just as described in the plan) —
   the plan was written by reading real code once, but the code changes as you implement it, so
   don't trust a stale mental model.
3. For any task that says "read file X in full before writing this" — do exactly that, in full,
   before writing the task's code. These call-outs exist specifically because the plan's own
   research pass didn't cover that file, so the plan's sketched code may need adjustment once you
   see the real thing.

After finishing all work in a repo:

1. Update that repo's `docs/superpowers/plans/next/SUMMARY.md` entry for this plan (status, what's
   done).
2. Update `docs/superpowers/specs/next/SUMMARY.md` if the spec's implementation status changed.
3. Backend only: confirm `docs/postman-request/README.md`'s module index lists every new endpoint
   doc you created (rule 3 above) — this has gone stale before in this exact codebase; don't repeat
   that mistake.
4. Do **not** move anything from `next/` to `finished/` until every part in that repo is complete
   and reviewed — a partially-done multi-part plan stays in `next/`.

## IF SOMETHING IS UNCLEAR

Every plan file was written from a real, approved design spec after a full brainstorming session
with the product owner — it is not a rough draft. If you hit a genuine ambiguity or the plan's
sketched code conflicts with what you find in the real codebase:

1. First, check whether the task itself already flags this (several tasks explicitly say "confirm
   X before finalizing" — that's not a gap, that's the plan being honest about what it couldn't
   verify).
2. If it's a genuine new gap, stop, do not guess a fix, and report exactly what you found and why
   it doesn't match the plan — the user will decide whether to amend the plan or the design spec.
3. Do not silently expand scope to "helpfully" fix an unrelated problem you notice along the way —
   flag it instead (per this codebase's own convention of flagging drift/gaps in `SUMMARY.md` files
   rather than fixing them opportunistically, unless directly in the path of the current task).
