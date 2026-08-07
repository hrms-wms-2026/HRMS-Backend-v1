
# Work Management — Objective Subtree (Head-only) — Design

**Status:** Approved by user 2026-08-07, ready for implementation planning.

**Relationship to existing work:** This is a new, independent endpoint. It does **not** modify the shipped `GetObjectiveTree` endpoint (`GET /api/v1/work/projects/{projectId}/objectives`) and does **not** modify `docs/superpowers/specs/next/2026-08-06-work-management-milestone-membership-and-achieve-design.md` §5 (scoped visibility for that same endpoint), which remains queued as written. Both were considered and explicitly ruled out as places to make this change, in favor of a standalone addition.

**Origin:** brainstormed live with the user 2026-08-07 via `superpowers:brainstorming`, triggered by the user reviewing `docs/postman-request/Work Management/Get Objective Tree.md` and wanting a differently-scoped read path for a single milestone.

---

## 1. Goal

Give an Objective's current Head a way to fetch just their own milestone's context: its parent's detail plus its full descendant subtree (children, grandchildren, etc.), in one nested response — without requiring project-wide membership or returning the rest of the project's tree.

## 2. Endpoint

```
GET /api/v1/work/objectives/{id}/tree
```

Added to the existing `ObjectivesController` (`src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs`), alongside its other `{id:guid}`-scoped actions (Edit, Delete, Transfer).

**Auth:** Tenant session cookie + CSRF header, `[Authorize(Policy = "TenantPolicy")]` (inherited from the controller). No `[RequirePermission]` attribute — same shape as Delete/Transfer, where the handler itself enforces the Head check rather than a permission gate.

**Authorization rule:** the caller must be the requested Objective's current Head — `objective.OwnerId == callerId`, checked directly, no admin/permission bypass. Matches the existing pattern in `DeleteObjectiveCommandHandler`: `"Only this milestone's head can delete it."` This endpoint uses the same rule for viewing: `"Only this milestone's head can view its subtree."`

## 3. Request

Single route parameter: `id` (the Objective's id — this is the *main* parameter, replacing the project-scoped `projectId` used by the existing tree endpoint). No query parameters, no body.

## 4. Response

`200 OK`, a nested tree:

```json
{
  "parentObjective": { "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null", "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date", "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null" } | null,
  "objective": {
    "id": "guid", "projectId": "guid", "parentObjectiveId": "guid|null", "isDefault": false, "title": "string", "description": "string|null",
    "ownerId": "guid", "reportingManagerId": "guid|null", "createdById": "guid", "startDate": "date", "endDate": "date",
    "progress": 0, "actualHours": null, "allocatedHours": 40, "completedHours": 0, "isActive": true,
    "createdAt": "datetime", "updatedAt": "datetime|null",
    "children": [
      { "...same shape as objective, recursively...": "...", "children": [] }
    ]
  }
}
```

- `parentObjective` is `null` when the requested Objective has no parent (i.e., it's the Project's Default Objective).
- `objective` carries the requested Objective's own full detail plus a `children` array; each child carries the same shape recursively down to leaves.
- Every node uses the full detail field set (same fields as `ObjectiveDetailResponse`), not the lean tree-item shape — the caller gets everything without a follow-up call.
- **Inactive (soft-deleted) descendants are included**, same ambiguity as today's project-wide tree endpoint — the client filters on `isActive` if it only wants live nodes. This is a deliberate deviation from `GetTreeByProjectIdAsync`'s existing `IsActive` filter (§5).

## 5. Data access

New repository method on `IObjectiveRepository` (`src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`):

```csharp
/// <summary>Every Objective for a Project regardless of IsActive, unordered - used to build a
/// Head-scoped subtree in memory. Unlike GetTreeByProjectIdAsync, does not filter to active-only.</summary>
Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
```

EF implementation (`src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`) mirrors `GetTreeByProjectIdAsync` exactly, minus the `&& o.IsActive` predicate.

**Handler flow** (`GetObjectiveSubtreeQueryHandler`, new file under `src/ONEVO.Application/Features/WorkManagement/Objectives/Queries/GetObjectiveSubtree/`):

1. `IsAuthenticated` / `TenantId != Guid.Empty` checks, matching every other handler in this feature.
2. `objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct)` → `NotFound` if `null`.
3. `objective.OwnerId != userId` → `Forbidden("Only this milestone's head can view its subtree.")`.
4. `all = await _objectives.GetAllByProjectIdAsync(tenantId, objective.ProjectId, ct)` — one query for the whole project, reused for both the parent lookup and the subtree build.
5. `parent = objective.ParentObjectiveId is Guid pid ? all.FirstOrDefault(o => o.Id == pid) : null` → mapped via `ObjectiveMapper.ToDetail`.
6. `childrenByParent = all.Where(o => o.ParentObjectiveId.HasValue).ToLookup(o => o.ParentObjectiveId!.Value)`.
7. Recursive build: `ObjectiveMapper.ToSubtreeNode(objective, childrenByParent)`, where the mapper builds each node's `Children` from `childrenByParent[node.Id].Select(c => ToSubtreeNode(c, childrenByParent))`. No cycle guard needed — parent assignment is already constrained elsewhere in this feature to be acyclic (a child's parent is always fixed at creation and never repointed to a descendant).
8. Return `Result.Success(new ObjectiveSubtreeResponse(parent is null ? null : ObjectiveMapper.ToDetail(parent), subtreeRoot))`.

## 6. New types

- `ObjectiveSubtreeResponse(ObjectiveDetailResponse? ParentObjective, ObjectiveSubtreeNodeResponse Objective)` — new file, `DTOs/Responses/ObjectiveSubtreeResponse.cs`.
- `ObjectiveSubtreeNodeResponse(Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description, Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate, decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, IReadOnlyList<ObjectiveSubtreeNodeResponse> Children)` — same file.
- `GetObjectiveSubtreeQuery(Guid ObjectiveId) : IRequest<Result<ObjectiveSubtreeResponse>>` — new file, `Queries/GetObjectiveSubtree/GetObjectiveSubtreeQuery.cs`.
- `ObjectiveMapper.ToSubtreeNode(Objective, ILookup<Guid, Objective>)` — new method on the existing static mapper.

No changes to `ObjectiveDetailResponse`, `ObjectiveTreeItemResponse`, or the existing `ObjectiveMapper.ToDetail`/`ToTreeItem` methods.

## 7. Errors

| Status | Cause |
|---|---|
| `403` | Caller is not this Objective's current Head, or not authenticated |
| `404` | Objective doesn't exist in tenant |

No `409`/`400` cases — this is a pure read with no state precondition.

## 8. Documentation

New Postman doc `docs/postman-request/Work Management/Get Objective Subtree.md`, following the existing `Get Objective Tree.md` doc's format (Auth / Description / Response / Errors / Source sections). The existing `Get Objective Tree.md` is left unchanged.

## 9. Out of scope

- Any change to `GetObjectiveTree` (`/projects/{projectId}/objectives`) or its queued §5 scoped-visibility rework in the 2026-08-06 spec.
- Membership-sync, Achieve, or any other capability from the 2026-08-06 spec — unrelated to this endpoint.
- Pagination or depth-limiting on the subtree — milestone hierarchies in this codebase are expected to be shallow (parent/child/grandchild-scale), matching the existing project-wide tree endpoint's own no-pagination precedent.

## 10. Self-review

- No placeholders — every field and rule traces to an explicit answer from the 2026-08-07 brainstorming session.
- Internally consistent with the shipped milestone-hierarchy design: reuses `ObjectiveDetailResponse`'s exact field set, reuses the Head-check wording pattern from `DeleteObjectiveCommandHandler`, and reuses the existing `GetTreeByProjectIdAsync` query shape (minus the `IsActive` filter) rather than inventing a new access pattern.
- Scope: one small, independent endpoint — no decomposition needed.
- Ambiguity resolved: three points were put to the user directly (replace-vs-new-endpoint, response nesting shape, detail-level and inactive-node inclusion) rather than guessed, including an explicit reversal (initially "replace the queued spec," then walked back to "leave everything else untouched, add a new endpoint") re-confirmed once surfaced.
