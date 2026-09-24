# Sub-agent prompt: execute the Sprint Foundation plan (copy-paste this whole block)

Two repos, four ordered plan parts. Execute them **strictly in order** — each part depends on the one
before it (Part 2 needs Part 1's `TaskStatusVisibilities`/fixed `MoveTaskStatusCommandHandler`; Part 3
needs Part 1+2's endpoints deployed; Part 4 needs Part 3's `SprintApiService`/`Sprint` model).

1. `HRMS-Backend-v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-foundation/part-1-task-status-governance.md`
2. `HRMS-Backend-v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-foundation/part-2-sprint-entity-and-lifecycle.md`
3. `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-foundation/part-3-frontend-settings-and-task-detail.md`
4. `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-foundation/part-4-frontend-backlog-and-board.md`

Spec (read this first, in full, before starting Part 1):
`HRMS-Backend-v1/docs/superpowers/specs/next/2026-08-17-work-management-sprint-foundation-design.md`

## Scope

**Work Management module only** in both repos — backend: `ONEVO.*/Features/WorkManagement/**`,
`ONEVO.Domain/Features/WorkManagement/**`; frontend: `src/app/modules/work/**`. Don't touch
`organization`, `layouts/main-layout`, `people`, `dashboard`, or `public` in either repo — a teammate
owns those.

## Process

Use **superpowers:subagent-driven-development** or **superpowers:executing-plans** to run each part
task-by-task. Every task in all four parts already follows strict TDD (write the failing test, run it,
implement, run again, commit) — don't skip steps or batch multiple tasks' changes into one commit.

**These plans were written after deep, verified research** — every file path, method signature, and
existing-code claim was confirmed by actually reading the relevant files (not guessed), and a
self-review pass caught and fixed two real issues before you ever see this: (1) `WorkTaskResponse` is
constructed in **four** places, not one — Part 2 Task 5 lists all four explicitly, don't miss any; (2)
`NotificationTemplateSeeder`'s existing idempotency check is all-or-nothing (`AnyTemplatesExistAsync`),
which would silently skip seeding the new Sprint notification templates in any environment that already
ran this seeder once — Part 2 Task 11 fixes this to a per-template check. Trust the plans' file/line
citations; if something doesn't match what you find in the actual code, stop and flag the mismatch
rather than silently improvising around it — that's exactly the kind of drift that caused bugs last
time.

**Places the plan explicitly tells you to read a file before writing to it** (an existing test file's
fixture style, an existing repository implementation's filter conventions, etc.) — actually do that
read first. These aren't placeholders; they're deliberate because the plan author didn't have that
file's exact current byte-for-byte content in context. Match what you find, don't invent it.

## Two things that need your judgment call, flagged explicitly in the plans

- Part 2 Task 3's test note: the "EndDate before StartDate" validation is enforced in both the
  FluentValidation validator and defensively in the handler itself — keep both, don't remove either
  thinking it's redundant (the direct-handler-call unit test needs the handler-level check to be
  meaningful, since it bypasses the MediatR validation pipeline).
- Part 4 Task 1's temporary type-cast note: Task 2 (adding `WorkTask.sprintId` to the frontend model)
  must land before Task 1's `columnsWithTasks` computed can drop its temporary cast — the plan handles
  this via an explicit "pulled forward" step, follow it in the order written.

## When you finish each part

Run that repo's full Work Management test filter (backend: `dotnet test tests/ONEVO.Tests.Unit
--filter FullyQualifiedName~WorkManagement`; frontend: `npx ng test --watch=false`) before moving to
the next part. Part 2's own completion criterion additionally requires the dev server to boot cleanly
(confirms `SprintLifecycleJob`'s DI dependencies resolve). Part 4 ends with a full manual end-to-end
browser verification checklist — do that too, don't stop at green tests alone for this one, given how
much of this feature is genuinely new UI with no prior automated coverage to lean on.

Report back after each part, not just at the very end — this is a big plan and checkpoints matter.
