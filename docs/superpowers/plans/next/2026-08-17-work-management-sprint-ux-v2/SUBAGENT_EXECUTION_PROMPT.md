# Sub-agent prompt: execute the Sprint UX v2 plan (copy-paste this whole block)

Two repos, four ordered plan parts, continuing directly on top of the already-shipped Sprint
Foundation work (backend Parts 1-2, frontend Parts 3-4 — all done, do not re-touch that work except
where these new parts explicitly say to modify a file they built).

1. `HRMS-Backend-v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-ux-v2/part-5-task-edit-requests-and-board-structure.md`
2. `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-ux-v2/part-6-employee-directory.md`
   (no backend dependency — can run in parallel with Part 5 if you want, but land it before Part 7)
3. `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-ux-v2/part-7-editable-task-popup-and-board-structure.md`
   (needs Part 5's endpoints deployed + Part 6's `EmployeeAvatarComponent`)
4. `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-17-work-management-sprint-ux-v2/part-8-backlog-v2-and-calendar-stub.md`
   (needs Part 7 done first)

Spec (read in full before starting Part 5):
`HRMS-Backend-v1/docs/superpowers/specs/next/2026-08-17-work-management-sprint-ux-v2-design.md`

## Scope

Work Management module only, both repos — same boundaries as every prior part of this feature.

## Process

Same as before: **superpowers:subagent-driven-development** or **superpowers:executing-plans**, strict
TDD every task, don't skip steps, don't batch multiple tasks into one commit.

## Things worth knowing before you start

- **`TaskEditRequest` (Part 5) is a deliberate structural mirror of `TaskCreationRequest`.** Read the
  existing `TaskCreationRequest`/`Approve`/`Reject`/`Cancel`/`GetMy` files in full before writing the
  new ones — the plan tells you to at each step. Don't improvise a different shape.
- **`TaskEditRequest`'s response DTOs must carry the resolved requester name from the first commit.**
  This project has an existing, still-unfixed bug (`requestedByName` blank across the Approvals UI for
  every other request type) — Part 5 explicitly calls this out so the new request type doesn't repeat
  it. Don't skip the `ResolveDisplayNamesByEmployeeIdAsync` call thinking it's optional polish.
- **`ReorderTaskStatusesCommand` (Part 5) must reject any submission where the complete-status count
  isn't exactly 1** — both in the FluentValidation validator and defensively in the handler itself
  (the handler-level check is what makes the direct-`Handle()`-call unit tests meaningful, same
  reasoning as Sprint Foundation Part 2's date-validation note).
- **The avatar field genuinely doesn't exist yet** (Part 6) — confirmed by grepping the actual current
  branch, not assumed. Build the initials-fallback path as the expected path, not a rare edge case. If
  you find an avatar field that does exist somewhere I missed, use it — but confirm it's real by
  reading the actual response shape, don't take my earlier research as gospel if the code disagrees.
- **The sprint-status dropdown in `SprintTabComponent` (Part 8) is not a generic "set to any status"
  control** — the backend only exposes `Complete`/`Achieve` as owner-callable actions; `Future`/
  `Active`/`Incomplete` are date-driven by `SprintLifecycleJob`. Part 8's plan spells out exactly how
  `TaskBacklogComponent` should map the dropdown's other values to a no-op-with-message rather than a
  call to an endpoint that doesn't exist.
- **Part 8 replaces `TaskBacklogComponent` and retires `SprintListComponent`'s usage there** — check
  whether anything else still references `SprintListComponent` before deleting it; if nothing does,
  remove the dead files rather than leaving them.

## When you finish each part

Same checkpoint discipline as before — run that repo's full Work Management test filter, report back,
wait before moving to the next part. Part 8 (the last one) ends with a full manual browser pass,
listed explicitly at the bottom of its plan file — don't skip it just because the test suites are
green; a lot of this is genuinely new UI/UX with limited prior coverage to lean on.
