
# Work Management — "My Milestones In This Project" API — Design

**Status:** Approved by user 2026-08-08, ready for implementation planning.

**Builds on:** `docs/superpowers/specs/finished/2026-08-08/2026-08-06-work-management-milestone-membership-and-achieve-design.md` (the membership model — `project_members` rows scoped by `ObjectiveId`, `IsActive`/`RemovedAt` — and the repository methods it shipped: `IProjectMemberRepository.GetActiveObjectiveIdsForUserInProjectAsync`, `IObjectiveRepository.GetAllByProjectIdAsync`). This design adds one new read endpoint on top of that shipped membership model; it does not change any write path.

**Origin:** brainstormed live with the user 2026-08-08 via `superpowers:brainstorming`, immediately following the membership/Achieve plan's completion. The user wants a frontend-facing screen that, given a project, shows every milestone the logged-in user is connected to in that project, along with who currently heads it and who its reporting manager is — by name, not just id.

---

## 1. Goal

One new read-only endpoint: given a `projectId` and the caller's own identity (from the session cookie), return every milestone (Objective) in that project the caller has ever had a `project_members` row for — active or not — with each milestone's current Head name and current Reporting Manager name resolved server-side. The frontend derives "am I the Head or just a Member" itself (it already has the caller's own `userId` and each milestone's `ownerId`); it also derives what to show/hide from the per-row membership status this endpoint returns, rather than the API pre-filtering to "active only".

## 2. Endpoint contract

**`GET /api/v1/work/projects/{projectId}/objectives/mine`**

- **Auth:** Tenant session cookie + CSRF header, `TenantPolicy`. `[RequirePermission("projects:access")]` — same module-wide base gate as `GET /objectives/mine/history` and `GET /objectives/change-requests/mine`. No stricter per-project check: the endpoint is inherently self-scoped (it can only ever return the caller's own rows), so an unrelated `projectId` just yields an empty array, not a 403/404.
- **Route placement:** declared in `ObjectivesController` via an absolute route override (`[HttpGet("~/api/v1/work/projects/{projectId:guid}/objectives/mine")]`), exactly like the existing `GetTree` action's `~/api/v1/work/projects/{projectId:guid}/objectives` override. No collision: `.../objectives` (0 extra segments) vs `.../objectives/mine` (1 extra segment) are distinguishable at every routing stage.
- **Request:** no body, no query parameters. `projectId` from the route, caller identity from `ICurrentUser`.
- **Response:** `200 OK`, always — even for a `projectId` that doesn't exist in the tenant, or one the caller has zero rows in. No `404`. This is a deliberate simplification (confirmed with the user): validating the project's existence first would cost an extra query for no behavioral benefit, since "project doesn't exist" and "project exists but I have no milestones there" are indistinguishable to the caller either way — both are just "nothing here for you."

```json
[
  {
    "objectiveId": "guid",
    "projectId": "guid",
    "parentObjectiveId": "guid|null",
    "isDefault": false,
    "title": "string",
    "ownerId": "guid",
    "ownerName": "string",
    "reportingManagerId": "guid|null",
    "reportingManagerName": "string|null",
    "startDate": "date",
    "endDate": "date",
    "allocatedHours": "decimal",
    "completedHours": "decimal",
    "objectiveIsActive": true,
    "isAchieved": false,
    "achievedAt": "datetime|null",
    "membershipIsActive": true,
    "membershipRemovedAt": "datetime|null"
  }
]
```

- `ownerName`/`reportingManagerName` are `First Last` resolved from the `Employee` record matching `ownerId`/`reportingManagerId` (via `Employee.UserId`), same shape as the existing `FullName` convention used in `PlatformUserResponse`/`InviteMapper`. `reportingManagerId`/`reportingManagerName` are both `null` only for the Default Objective (which has no Reporting Manager, per the shipped membership design). If an owner or reporting manager's `Employee` record can't be resolved (deleted/never existed — should not happen in practice but isn't guaranteed by a DB constraint), the corresponding `*Name` field is `null` rather than the request failing.
- `objectiveIsActive`/`isAchieved`/`achievedAt` describe the **milestone itself** (soft-deleted or Achieved). `membershipIsActive`/`membershipRemovedAt` describe **this caller's `project_members` row** for that milestone (removed via Transfer/member-removal/Achieve-cleanup, per the shipped membership design). Both pairs are independent — a milestone can be fully active while the caller's own membership on it was removed (e.g. they were Transferred away), or a milestone can be Achieved while the caller's membership is still active (if they had another reason to stay). The frontend is expected to use both pairs together to decide what to show under whatever status filter the user picks — this design does not prescribe the frontend's filter semantics.
- Rows are unordered (matches every other list endpoint in this feature — no explicit `ORDER BY` requirement was raised).

## 3. Implementation approach

Confirmed with the user: reuse existing repository methods over adding a new SQL join, since two of the three needed queries already exist from the just-shipped membership plan.

1. **New repository method** — `IProjectMemberRepository.ListForUserInProjectAsync(tenantId, projectId, userId)`: every `project_members` row (any `IsActive` value) for this exact `(tenantId, projectId, userId)` triple. This is new — the shipped methods only cover "active-only, this project" (`GetActiveObjectiveIdsForUserInProjectAsync`) or "inactive-only, all projects" (`ListInactiveMembershipsForUserAsync`); neither is "all statuses, this one project," which is what "frontend filters by status" requires.
2. **Reuse** `IObjectiveRepository.GetAllByProjectIdAsync(tenantId, projectId)` (already shipped, regardless-of-`IsActive`) — fetch every Objective in the project once, then filter in-memory to the `ObjectiveId`s present in step 1's rows. Using the regardless-of-active variant (not `GetTreeByProjectIdAsync`, which is active-only) so a milestone that was later soft-deleted still shows up if the caller has a membership row pointing to it — consistent with "frontend filters by status," not the API pre-filtering.
3. **New repository method** — `IEmployeeRepository.GetByUserIdsAsync(tenantId, userIds)`: batch-fetch `Employee` rows for a set of `userId`s in one query. Collect the distinct `ownerId`/`reportingManagerId` values across all milestones from step 2, resolve them all in this one call, then build the response by joining ownership/RM-name from an in-memory dictionary. Avoids an N+1 of individual `GetByUserIdAsync` calls (up to 2 per milestone).
4. Join steps 1+2+3 in the query handler (no new SQL, no raw SQL — matches the Global Constraints already established for this feature: "Raw SQL is forbidden except migration RLS-policy SQL").

No repository method returns pre-joined DTOs; the handler does the join in memory, same as `GetObjectiveTreeQueryHandler` already does for its own scoped-subtree computation.

## 4. New/changed files

- **Modify:** `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs` + `src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs` — add `GetByUserIdsAsync`, alongside the existing `GetByUserIdAsync`.
- **Modify:** `IProjectMemberRepository` + `EfProjectMemberRepository` — add `ListForUserInProjectAsync`.
- **Create:** `MyProjectMilestoneResponse` DTO (Application layer) and `MyProjectMilestoneViewModel` (Api layer) + mapper entries, following the existing `ObjectiveTreeItemResponse`/`ViewModel` + mapper pattern.
- **Create:** `GetMyProjectMilestonesQuery` + `GetMyProjectMilestonesQueryHandler` under `Objectives/Queries/GetMyProjectMilestones/`.
- **Modify:** `ObjectivesController` — one new action, `GetMine` (or similar), wired to the new query.
- **Tests:** unit tests for the two new repository methods' consuming handler (mocked repos, same pattern as every other query handler in this feature) and for the new `EfProjectMemberRepository`/`EfEmployeeRepository` methods if this feature's convention unit-tests repository methods directly (it currently does not — repository-only changes in this feature have been verified by build + exercised indirectly through handler tests and, where present, integration tests). Postman doc: one new file under `docs/postman-request/Work Management/`, per `PROCESS_RULES.md` rule 6.

## 5. Error handling

- Unauthenticated → `403` (via `[RequirePermission]`, same as every gated endpoint in this feature).
- Any `projectId` (nonexistent, or one the caller has no rows in) → `200 OK`, `[]`. No `404` path exists for this endpoint (confirmed with the user).
- Owner/Reporting-Manager `Employee` record unresolvable → that row's `*Name` field is `null`; the request still succeeds. This should not happen in steady state (every `OwnerId`/`ReportingManagerId` is validated to be an active employee at the point it's assigned, per the shipped membership design) but the handler does not assume it's impossible.

## 6. Testing

- Unit tests on `GetMyProjectMilestonesQueryHandler` (mocked `IProjectMemberRepository`, `IObjectiveRepository`, `IEmployeeRepository`): empty-membership-list → empty response; single active membership → correct fields incl. resolved names; a membership whose milestone was later soft-deleted or Achieved still appears with its own status fields set correctly; a removed (`IsActive = false`) membership still appears (not filtered out) with `membershipIsActive: false`; Default Objective membership appears with `reportingManagerId`/`reportingManagerName` both `null`; an unresolvable owner/RM employee id yields `null` for that name field without failing the request; two milestones sharing the same owner only trigger one batched employee lookup covering both (asserted via the mock's batch-call arguments, not call count, since the handler is expected to call the batch method once with the deduplicated id set).
- No integration test is planned for this endpoint given the existing `CreateProjectEndpointTests` fixture's pre-existing, already-documented environment issue (every test in that class currently fails on `POST /api/v1/work/projects` returning `403`, confirmed pre-existing and unrelated to any of this feature's code) — adding more tests to a fixture that can't currently run would not produce a verifiable green signal. This is a testing-infrastructure gap, not a scope decision; revisit once that fixture issue is fixed.

## 7. Self-review

- No placeholders — every field in the response contract, every new repository method's signature, and every file this touches is spelled out above.
- Internally consistent with the shipped membership design: reuses its repository methods and entity shapes without changing any of them; adds no new schema, no new tables, no new columns.
- Scope: one read endpoint, two small repository additions (one new method each on two existing repositories), no write-path changes — small enough for a single implementation plan, not decomposed further.
- Ambiguity resolved via direct questions to the user: caller-only (never another user's id) confirmed; role (Head/Member) explicitly NOT computed server-side, left to the frontend via `ownerId` comparison; owner-name resolution generalized to every milestone (not just the Default Objective) after the user's own correction; active-only membership scope was initially agreed then explicitly revised by the user to "all statuses, frontend filters" — this document reflects the revised, final decision.
