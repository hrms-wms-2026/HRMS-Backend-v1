# Work Management — Unified Tree (Objective → Sprint → Task) view (design)

**Status:** Approved by user 2026-08-20. Backend implementation plan written same day; frontend plan
follows once backend ships (same backend-first order as the Project Page Redesign work).

**Scope:** Work Management module only, both repos. See `docs/superpowers/rules/PROCESS_RULES.md` and
[[feedback_scope_work_management_only]] — do not touch other modules.

## 1. Goal

The Project Detail page's existing "Tree" tab (`app-milestone-tree-tab`) shows only the Objective/Module
hierarchy today. Extend it so that expanding a **leaf** Objective (one with no child Objectives) also
reveals its Sprints and any direct (non-sprint) Tasks as sibling nodes, and expanding a Sprint reveals its
Tasks — all inside the same tree diagram, lazily: Objectives load upfront (unchanged), Sprints load only
when a leaf Objective is expanded, Tasks load only when a Sprint (or a leaf Objective, for direct tasks)
is expanded. Each node kind gets inline action icons on the row itself (not just the side detail panel):
Objective row — `+` (create Sprint), member icon (manage members), edit icon. Sprint row — `+` (create
Task), edit icon. Task row — edit icon only. All existing tabs (Board, Backlog, List, etc.) stay exactly
as they are — this is additive, not a replacement (confirmed with user).

## 2. Current state (verified against code 2026-08-20)

- The Tree tab's data source is `GetObjectiveSubtreeQuery` (`GET /work/objectives/{id}/tree`,
  `[RequirePermission("projects:access")]` + a handler-level reachability check: caller must have active
  membership on the requested objective **or one of its ancestors**, unless they hold the tenant-wide
  `projects:read`/`*` permission). It returns nested Objectives only — no Sprints/Tasks — in one call per
  subtree root. This part is unchanged by this plan.
- **Security gap found, independent of this feature, fixed as Part 1**: `GetObjectiveSprintsQueryHandler`
  and `GetObjectiveTasksQueryHandler` (and their controller actions, `GET
  /work/objectives/{objectiveId}/sprints` and `/tasks`) check only `IsAuthenticated` — no project-
  membership or reachability check, and no `[RequirePermission]` attribute either. Any authenticated
  tenant user who knows/guesses an `objectiveId` can read that objective's full sprint/task list today,
  bypassing the same visibility rule the Objective tree already enforces. Wiring a tree UI to call these
  per-node on expand makes objective ids more discoverable through normal use, so this must be fixed
  first, using the **exact same pattern** `GetObjectiveSubtreeQueryHandler` already uses (self + ancestor
  objective ids, `IProjectMemberRepository.HasActiveMembershipForAnyObjectiveAsync`, `projects:read`/`*`
  permission bypass) — not the different BFS-reachable-set pattern `GetObjectiveTreeQueryHandler` uses,
  since the Tree tab the user is looking at is powered by the ancestors-only handler, and visibility must
  stay consistent within the same screen.
- **No "tasks by sprint" endpoint exists.** `GetObjectiveTasksQuery` returns *all* tasks for an objective
  (direct + sprint tasks together, distinguished only by nullable `SprintId`); the Board/Backlog tabs
  filter client-side. The repository method to do this server-side, `IWorkTaskRepository.GetBySprintIdAsync`,
  already exists but is unused by any handler — Part 2 wires it up as a proper query+endpoint (least new
  plumbing), with the same reachability check baked in from the start.
- **No new "project members" aggregation endpoint is needed.** `ProjectDetailResponse` (the existing
  `GET /work/projects/{id}` response, already fetched by the Project Detail page that hosts this Tree tab)
  already carries `Members: IReadOnlyList<ProjectMemberAvatarDto>` and `MemberCount` — the same
  capped-avatar-list shape the Explanation Card (Project List redesign work) already reuses. The tree
  root's "all project members" display is a frontend-only wiring task against data already on the page.
- **Direct tasks (no Sprint) are schema-legal but not currently creatable.** `WorkTask.SprintId` is
  nullable, but both `CreateTaskCommand` and `CreateTaskCreationRequestCommand` require a non-empty
  `SprintId` (validator rejects `Guid.Empty`). So "direct tasks as tree siblings" is documented, correct
  behavior for the display, but will render an empty set for any objective created through the current
  UI — **not a gap to fix here**; loosening `CreateTaskCommand`'s validator to allow true direct-task
  creation is a separate, unrequested change, out of scope for this plan.
- Frontend already has every modal this feature needs, all reusable unmodified: `app-sprint-create-form`
  / `app-sprint-edit-form` (Sprint create/edit), `app-task-create-modal` (pass a single-element
  `activeSprints` to scope creation to one sprint) / `app-task-detail-modal` (Task edit), `app-milestone-
  settings-modal` (Objective edit, already exists but currently only wired from the side detail panel, not
  the tree row itself), `app-objective-members-popup` (Objective member management, same
  already-exists-but-only-in-side-panel situation). No new modals are needed — the frontend plan (written
  after backend ships) is about wiring existing components onto tree rows and adding lazy-fetch-on-expand,
  not building new UI primitives.

## 3. Backend plan

**Parts 1-2 — shipped 2026-08-21, 417/417 WorkManagement tests:**
- **Part 1** — add the missing reachability check to `GetObjectiveSprintsQueryHandler` and
  `GetObjectiveTasksQueryHandler`, plus `[RequirePermission("projects:access")]` on both controller
  actions (matching `GetSubtree`'s convention exactly).
- **Part 2** — new `GetSprintTasksQuery`/handler/endpoint (`GET /work/sprints/{sprintId}/tasks`), built
  with the same reachability check from the start, using the existing `GetBySprintIdAsync` repository
  method.

**Parts 3-5 — added 2026-08-21, not started, driven by the requirement expansion in §4 below:**
- **Part 3** — enrich `GetObjectiveTreeQuery`'s response (`ObjectiveTreeItemResponse`/`ObjectiveTreeItemViewModel`)
  with `Progress`, `OwnerName`, and a real per-node `IsOwner` (direct-membership-on-this-exact-objective,
  not "reachable via ancestor/descendant walk") flag — the frontend's project-wide tree switch (§4) depends
  on this to gate action-icon visibility per branch. See
  `plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/part-3-enrich-objective-tree-response.md`.
- **Part 4** — Delete Task (soft delete). No migration needed — `WorkTask : BaseEntity` already has
  unused `IsDeleted`/`DeletedAt` columns and the global EF query filter + `SoftDeleteInterceptor` already
  handle everything once a `Remove()` call exists. Objective-owner-only authorization, matching every other
  Task mutation in this module. See `part-4-delete-task-soft-delete.md`.
- **Part 5** — make `SprintId` optional on Task creation, across all three independent code paths:
  `CreateTaskCommand`, `CreateTaskCreationRequestCommand`, and `ApproveTaskCreationRequestCommandHandler`
  (the approval path re-validates the sprint independently and is easy to miss). `TaskStatus` resolution is
  already Objective-scoped, not Sprint-scoped, so no status-lookup logic needs to change. See
  `part-5-sprint-optional-task-creation.md`.

## 4. Frontend — requirement expanded 2026-08-21, rewritten from the original 3-part design

**What shipped from the original design (Parts 1-3, frontend repo, `905a9bb`..`076321e`):** the Modules
carousel and the old side detail panel (`MilestoneDetailPanelComponent`) are both fully removed;
`MilestoneTreeNodeComponent` gained a `kind: 'objective' | 'sprint' | 'task'` discriminator, lazy-load
state, and fetch-on-first-expand for Sprints/Tasks; a single shared icon set (create-sub/create-sprint/
create-task/edit/members) was wired to the already-existing modals. This all stays in place as the
foundation — see the status headers now added to `part-1-*.md`/`part-2-*.md`/`part-3-*.md` in the frontend
repo's plan folder for exact commit references.

**What the user asked for after seeing it, requiring 4 new parts (`part-4` through `part-7`, same folder):**

1. **Six icons on Module rows** (not the shared set above): Create sub module, Edit module, Member
   management, Add task (new — direct task creation under a Module, no Sprint), Add Sprint, Achieve sub
   module (new). **Four icons on Sprint rows**, plus a sprint icon, status badge, and completion bar chart:
   Create task, Edit Sprint, Achieve Sprint (new), Complete Sprint (new). **Two icons on Task rows**, plus
   assignee display: Edit task (ungated, unchanged), Delete task (new, owner-gated).
2. **Three separate row components** (`ModuleTreeRowComponent`, `SprintTreeRowComponent`,
   `TaskTreeRowComponent`) instead of one recursive component rendering all three kinds inline —
   `MilestoneTreeNodeComponent` becomes a thin kind-switching recursive shell.
3. **The side detail panel comes back** — explicitly reversing Part 3's removal — restyled to match the
   Project List page's `ProjectExplanationCardComponent` (Tailwind-inline styling, card wrapper, KPI grid,
   full-width action buttons), showing full detail for whichever row (any of the 3 kinds) is selected.
4. **Tree tab data source switches** from the single-node `GetObjectiveSubtreeQuery`-backed
   `GET /work/objectives/{id}/tree` to the project-wide, ancestor-aware `GetObjectiveTreeQuery`-backed
   `GET /work/projects/{projectId}/objectives` — this is what makes "a member of a child module sees the
   parent module's tree context but not the parent's own Sprints/Tasks" actually work; the single-node
   endpoint has no way to express ancestor-context-only visibility. **Confirmed with the user 2026-08-21**:
   fetch the whole reachable tree once, but pair it with a client-side Module filter dropdown at the top of
   the Tree tab, defaulted to the currently active module — so the view looks unchanged by default and the
   user opts into seeing more by switching the filter, no extra network round-trip on switch.
5. **Action-icon visibility scoped to the caller's own branch, confirmed explicitly by the user** ("Modules
   tabs-ela irukka 6 icon-um child-ikku show aagathu, view-only-ah irukkum" — the 6 module icons must not
   show on modules the caller doesn't directly own, even though the wider tree now shows them via the
   ancestor-aware query). This is why backend Part 3's `IsOwner` flag must mean "direct membership on this
   exact node," not "reachable" — the two are different questions and conflating them silently breaks this
   requirement without failing any backend test.
6. **Sprint becomes optional for task creation**, both via the owner-direct path and the non-owner
   create-request-for-approval path (backend Part 5).

**New frontend plan parts** (`Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-20-work-management-tree-sprint-task-unified-view/`):
- `part-4-project-wide-tree-data-source.md` — data-source switch, flat-list-to-tree reconstruction,
  depends on backend Part 3.
- `part-5-module-sprint-task-row-components.md` — the 3 new row components and full 6/4/2 icon sets;
  Delete-task's icon is built here but its API call is stubbed as a no-op pending backend Part 4.
- `part-6-restore-explanation-panel.md` — the restyled side panel, reusing Part 5's row content/actions
  per kind.
- `part-7-delete-task-and-sprint-optional-create.md` — wires the two backend-dependent gaps (delete-task,
  sprint-less task creation) once backend Parts 4-5 ship; hard-blocked until then.

**Reused, unmodified:** `app-sprint-create-form`, `app-sprint-edit-form`, `app-task-create-modal`,
`app-task-detail-modal`, `app-milestone-settings-modal`, `app-create-sub-module-modal`,
`app-objective-members-popup`, and now also `SprintApiService.achieve`/`.complete` (data-access methods
that already existed but were never called from any UI before this expansion).

## 5. Non-functional / testing

Same conventions as the Project Page Redesign plan: CQRS/`Result<T>` pattern, `ICallerIdentityResolver`
for identity, `docs/postman-request/Work Management/*.md` for every new/changed endpoint, full `dotnet test
tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` must stay green on the backend,
`npx ng test --watch=false --include='**/modules/work/**/*.spec.ts'` green on the frontend. Backend Part
1's regression test (a non-member correctly getting 403/Forbidden) remains the most important test from the
original scope — it closed a real, previously-live security hole. From the expansion: backend Part 3's
"ancestor-only node has `IsOwner = false`" test and frontend Part 5's "ancestor-context row renders zero
icons" test are the equivalent-importance tests for the new icon-visibility-scoping requirement — both
sides must be covered, a green backend alone does not prove the frontend actually hides the icons.

This whole feature (both repos) does not move to `finished/` on green tests alone — a full manual browser
pass is required first, per this session's own repeated experience on the Project Page Redesign work (three
separate real bugs were caught only by manual testing). See each `CURSOR_EXECUTION_PROMPT.md`'s closing
section for the specific manual checks required.
