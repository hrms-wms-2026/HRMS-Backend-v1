# Cursor execution prompt — Work Management Project Page Redesign (Backend)

Copy-paste this whole file as your instruction to Cursor.

---

Repo: `HRMS-Backend-v1` (.NET backend). Work on the current branch as-is — do not create a new branch
unless told to. Read the spec first, then execute the 3 plan parts in order:

**Spec (read in full before starting Part 1):**
`docs/superpowers/specs/next/2026-08-20-work-management-project-page-redesign-design.md`

**Plan parts, in this order:**
1. `docs/superpowers/plans/next/2026-08-20-work-management-project-page-redesign/part-1-release-date-and-banner-image.md`
2. `docs/superpowers/plans/next/2026-08-20-work-management-project-page-redesign/part-2-add-project-member.md`
3. `docs/superpowers/plans/next/2026-08-20-work-management-project-page-redesign/part-3-member-notifications-outbox.md`
   (**hard dependency**: needs Part 2's `AddProjectMemberCommandHandler` to exist — do not start Part 3
   before Part 2 is committed and its tests are green)

## Hard scope rule — read this before touching anything

**Work Management module only.** Allowed to touch:
- `src/ONEVO.Domain/Features/WorkManagement/**`
- `src/ONEVO.Application/Features/WorkManagement/**`
- `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` (adding one constant)
- `src/ONEVO.Application/DependencyInjection.cs` (adding one DI registration line + one `using`)
- `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` (appending two template entries)
- `src/ONEVO.Infrastructure/Services/SharedPlatform/**` only if a plan task explicitly says so (none do
  in this plan — you're only adding a new handler class in `ONEVO.Application`, not touching the
  Infrastructure-layer Outbox mechanism itself)
- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/**`
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/**`
- `docs/postman-request/Work Management/**`
- `docs/superpowers/plans/**`, `docs/superpowers/specs/**` (status/SUMMARY updates only)

**Never touch:** anything under `organization`, `layouts/main-layout`, `public`, admin/platform, or
CoreHR/other-module folders in either repo. A teammate is actively developing those — do not edit,
delete, or "clean up" anything there even if it looks unrelated or stale.

**Never run `dotnet run` or kill any running dotnet process** — if the build/test is blocked by a locked
file or a running process, stop and report it rather than killing something; it may belong to someone
else's active session. `dotnet build`/`dotnet test` are fine and expected — just don't start a long-running
server process, and don't run destructive git commands (`reset --hard`, `push --force`, `clean -f`)
without asking first.

## Process discipline

- **Test-driven**: for every numbered task in a plan part, write the test first (or alongside), confirm
  it fails for the right reason, then implement, then confirm it passes. Don't write a batch of
  implementation code and backfill tests afterward.
- **One task = one commit.** Don't squash multiple numbered tasks into one commit, and don't let one
  commit span two plan parts.
- **After every task**, run `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`
  and confirm it's green before moving to the next task. Don't accumulate several tasks' worth of
  unverified changes.
- **After finishing each whole part**, additionally run a full-solution `dotnet build` to catch anything
  the filtered test run wouldn't (e.g. a DI registration typo in a part of the graph the WorkManagement
  tests don't exercise).
- **Don't stop for approval between parts** — go straight from Part 1 → Part 2 → Part 3 as long as each
  part's tests are green when you finish it. **Do stop and flag clearly** if: a test fails and you can't
  make it pass without changing something the plan didn't ask you to change, a file the plan describes
  doesn't match what you actually find in the repo (the plan was written by reading the real code, but
  code moves fast here — trust what you read over what the plan assumed, and flag the mismatch rather than
  silently improvising), or a task would require touching a file outside the scope list above.

## Things worth knowing before you start (read once, keep in mind throughout)

- **Part 2's `AddProjectMemberCommandHandler` must be a close structural twin of the existing
  `AddObjectiveMemberCommandHandler`** — same DI shape, same `Result<T>` short-circuit order, same
  `ProjectMemberInvitation` field construction. Don't invent a different shape. The only real difference
  is the owner-gate (`project.LeadId` instead of `objective.OwnerId`) and where the target Objective
  comes from (the project's Default Objective, loaded server-side, not passed on the wire).
- **Do not modify `AddObjectiveMemberCommandHandler`, `AcceptObjectiveInvitationCommandHandler`'s
  membership logic, or `RejectObjectiveInvitationCommandHandler` in Part 2.** Part 3 touches
  `AcceptObjectiveInvitationCommandHandler`, but only to add one notification call — its existing
  membership-upsert logic must not change.
- **Part 3's `IsDefault` gate is the single most important correctness detail in this whole plan.** The
  `work_project_member_accepted` notification must fire only when the accepted invitation's Objective has
  `IsDefault == true`. Accepting a real (non-default) Objective invitation must continue to send zero
  notifications, exactly as it does today. Write the regression test for the non-default case explicitly
  — don't assume the happy-path test alone proves this.
- **The banner upload in Part 1 is a genuinely new, independent field from Logo** — don't merge them or
  reuse the same `EntityAsset.AssetPurpose`. Two separate optional uploads, two separate `EntityAsset`
  rows when both are present.
- **Every finished endpoint needs a `docs/postman-request/Work Management/<Endpoint Name>.md` file**
  (or an update to an existing one) per this repo's own `docs/superpowers/rules/PROCESS_RULES.md` rule 6
  — method+route, auth/permission/idempotency line, description, request body example, response body
  example, an error-status table, and a Source section linking the real controller/handler files and this
  plan. Copy the request/response shape from the actual DTOs you just wrote, not from memory.
- **`ResolveDisplayNamesByEmployeeIdAsync`** (on `ICallerIdentityResolver`) is the existing, already-used
  way to turn an EmployeeId into a display name for a notification placeholder — see
  `RequestAllocationExtensionCommandHandler.cs` for the exact call shape. Don't build a new name-resolution
  helper.

## When you finish

Run the full `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` suite one
more time after Part 3, plus a full-solution `dotnet build`. Report: which tasks/parts are done, the final
test count (e.g. "412/412"), and anything you flagged mid-way per the "stop and flag" rule above. Do not
move the plan folder from `plans/next/` to `plans/finished/` yourself — leave that for the human to do
once the frontend half (a separate, not-yet-written plan) also ships, per this repo's `finished`/`next`
convention (see `docs/superpowers/rules/FILE_CREATION_RULES.md`).
