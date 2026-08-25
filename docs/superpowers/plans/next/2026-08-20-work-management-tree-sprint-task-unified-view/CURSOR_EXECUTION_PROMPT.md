# Cursor execution prompt — Unified Tree (Objective→Sprint→Task) Backend, Parts 3-5

Copy-paste this whole file as your instruction to Cursor.

---

Repo: `HRMS-Backend-v1`. Current branch, no new branch needed.

**Already shipped, do not redo:** Part 1 (Sprint/Task authorization fix) and Part 2 (Get Sprint Tasks
endpoint) — both landed and verified (417/417 WorkManagement tests) before this prompt was written. This
prompt covers the **new** Parts 3-5, added after the requirement expanded.

**Spec (read in full before starting Part 3):**
`docs/superpowers/specs/next/2026-08-20-work-management-tree-sprint-task-unified-view-design.md` §4
(rewritten 2026-08-21 for the expanded requirement).

**Plan parts, in this order:**
3. `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-3-enrich-objective-tree-response.md`
   — do this first: the frontend's Part 1 (separate plan, frontend repo) depends on this response shape to
   switch the Tree tab's data source.
4. `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-4-delete-task-soft-delete.md`
   — independent of Part 3, can be done in either order relative to it, but do it before Part 5 (Part 5's
   tests reuse task-creation fixtures that are easier to extend once delete exists as a cleanup option, and
   both touch neighboring test files in the same handler-test class).
5. `docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-5-sprint-optional-task-creation.md`
   — do this last. It touches three independent handlers (`CreateTaskCommand`, `CreateTaskCreationRequestCommand`,
   `ApproveTaskCreationRequestCommand`) — do NOT stop after fixing the first one and assume the others are
   covered by the same validator change. They are three separate files with three separate validators.

## Hard scope rule

Same as every prior plan in this session: **Work Management module only.** Allowed paths:
`src/ONEVO.Domain/Features/WorkManagement/**`, `src/ONEVO.Application/Features/WorkManagement/**`,
`src/ONEVO.Api/Controllers/Tenant/WorkManagement/**`, `src/ONEVO.Api/Contracts/WorkManagement/**`,
`tests/ONEVO.Tests.Unit/Features/WorkManagement/**`, `docs/postman-request/Work Management/**`,
`docs/superpowers/**` (status updates only). Never touch `organization`, `layouts/main-layout`, `public`,
admin/platform, or CoreHR folders in either repo — a teammate owns those. Never run `dotnet run` or kill a
running dotnet process without asking first — it may belong to someone else's session; ask before touching
it. `dotnet build`/`dotnet test` are fine.

## Process discipline

Test-first, one task = one commit, run `dotnet test tests/ONEVO.Tests.Unit --filter
FullyQualifiedName~WorkManagement` after every task and confirm green before moving on, full-solution
`dotnet build` after each whole part. Go from Part 3 to Part 4 to Part 5 without stopping for approval as
long as tests stay green; stop and flag clearly if a test won't pass without touching something the plan
didn't ask for, or if a file the plan describes doesn't match what you actually find in the repo — trust the
real code over the plan's description, code moves fast in this repo and these plans were written from a
fresh read but things can still have shifted.

## Things worth knowing

- **Part 3's `ToTreeItem` signature change is breaking.** Grep `grep -rn "ObjectiveMapper.ToTreeItem"
  src/` before touching it and after finishing it, to make sure every call site (should be exactly one, in
  `GetObjectiveTreeQueryHandler`) got updated and the build isn't silently broken somewhere unexpected.
- **Part 3's `IsOwner` must mean "direct membership on this exact node," not "reachable."** This is the
  single most important detail in Part 3 — re-read that plan file's "IsOwner semantics" paragraph before
  writing the handler change. Getting this wrong (e.g. setting `IsOwner = true` for every node in the
  `reachable` set) silently breaks the frontend's icon-visibility-scoping requirement in a way that won't
  show up as a failing backend test, only as a frontend bug later — so also write the test described in
  Part 3 task 5 that explicitly checks an ancestor-only node has `IsOwner = false`, don't skip it as
  "obvious."
- **Part 4 needs no migration** — `WorkTask` already has `IsDeleted`/`DeletedAt` via `BaseEntity`, and the
  global EF query filter + `SoftDeleteInterceptor` already handle everything once you add a `Remove()` call.
  If you find yourself about to write an `AddColumn` migration for this, stop — you've misread the plan,
  re-read the "Current state" section of `part-4-delete-task-soft-delete.md` first.
- **Part 4's authorization is objective-owner-only, no permission-bypass path.** Don't copy the
  tenant-permission-bypass pattern from the Part 1 *read* handlers into this *delete* handler — every other
  Task mutation in this codebase (`DeleteTaskStatus`, `UnassignTask`, `CreateTask`) is gated purely on
  `objective.OwnerId == callerEmployeeId`, with no alternate path for a tenant-wide `projects:*` permission
  holder. Match that, don't invent a new authorization shape.
- **Part 5 touches three files that each independently deserialize/require a Sprint** —
  `CreateTaskCommandHandler`, `CreateTaskCreationRequestCommandHandler`, and
  `ApproveTaskCreationRequestCommandHandler`. The third one is the easiest to forget since it's not reached
  by calling either Create command directly — it only runs when a previously-created pending request gets
  approved. Run `grep -rln TaskCreationRequestPayload src/` as a final check before calling Part 5 done, to
  catch any fourth call site this plan might have missed.

## When you finish

Run the full `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` suite one more
time after Part 5, plus a full-solution `dotnet build`. Report the final test count and anything flagged
mid-way. Leave the plan folder in `plans/next/` — don't move it to `finished/` yourself; the frontend half
(separate plans, frontend repo) still needs to ship and get a manual browser pass first.
