# Part 4: Task Category CRUD commands/queries + Postman docs

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`
§4. **Hard prerequisites:** Part 2 (entity/repo/migration) and Part 1's Task 2 pattern (the
`IsEffectiveManagerAsync`-against-default-Objective authorization shape) — read Part 1's Task 2 in this
same folder before starting, this Part reuses that exact authorization shape.

**Scope guard:** Work Management module only.

## Goal

Give Task Category the same CRUD surface Task Status has after Part 1's rework: get-all-for-project,
create, edit, delete, reorder. Simpler than Task Status — no `Visibility`/`RequiresApproval`/
`ApproverId`/`MarksTaskComplete` fields, and no legacy per-Objective rows to guard against (Category
never had a per-Objective mode, this is new from Part 2).

## Files to create

- `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskCategories/GetProjectTaskCategoriesQuery.cs`
  + Handler
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskCategory/CreateTaskCategoryCommand.cs`
  + Handler + Validator
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskCategory/EditTaskCategoryCommand.cs`
  + Handler + Validator
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskCategory/DeleteTaskCategoryCommand.cs`
  + Handler
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskCategories/ReorderTaskCategoriesCommand.cs`
  + Handler + Validator
- `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCategoryResponse.cs`
- 5 new Postman docs under `docs/postman-request/Work Management/` (see Task 6).

## Files to modify

- `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (or wherever Task Status's
  equivalent 5 routes live after Part 1 — add the Category routes alongside them, same controller,
  matching route-naming convention: `GET/POST /work/projects/{projectId}/task-categories`,
  `PATCH /work/task-categories/{id}`, `DELETE /work/task-categories/{id}`,
  `PATCH /work/projects/{projectId}/task-categories/reorder` — mirror whatever exact path shape Part 1's
  Task Status routes ended up using, for consistency, rather than inventing a different shape here).
- `docs/postman-request/README.md` — add the 5 new endpoints to the Work Management count/list, per
  `PROCESS_RULES.md` rule 6.

## Task 1: `TaskCategoryResponse`

```csharp
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public record TaskCategoryResponse(Guid Id, string Name, int DisplayOrder);
```

## Task 2: `GetProjectTaskCategoriesQuery`

Mirrors Part 1's `GetProjectTaskStatuses` (Task 1 of Part 1) exactly, minus the template-fallback logic
Task Status needed (Category has none — `_categories.GetByProjectIdAsync` always returns real rows,
seeded once at Project creation by Part 2, never empty for an active Project). No authorization gate
beyond "Project exists and caller can reach it" — read-only, follow whatever read-access pattern this
module uses elsewhere for Project-scoped GETs (e.g. how the Project detail GET is gated) rather than
requiring `IsEffectiveManagerAsync` for a plain read.

## Task 3: `CreateTaskCategoryCommand` / `EditTaskCategoryCommand` / `DeleteTaskCategoryCommand`

Same three-handler shape as Part 1's Tasks 2-4, minus the "reject legacy per-Objective rows" guard
(nothing to reject — every `TaskCategory` row is Project-scoped from creation). Authorization: same
`IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct)` check against the
Project's default Objective. Delete needs the same "don't delete a category still in use" guard Task
Status has (`_tasks.AnyActiveByStatusIdAsync` equivalent) — add
`AnyActiveByCategoryIdAsync(tenantId, categoryId, ct)` to `IWorkTaskRepository`/`EfWorkTaskRepository`
(mirror the existing `AnyActiveByStatusIdAsync` implementation exactly, swap the filtered column).

Tests: same shape as Part 1's Tasks 2-4 (owner succeeds, non-owner default-Objective member succeeds,
unrelated caller Forbidden; delete-while-in-use returns Conflict).

## Task 4: `ReorderTaskCategoriesCommand`

Mirrors Part 1's Task 5, minus the `MarksTaskComplete`-exactly-one-true validation (Category has no such
field — reorder here only ever changes `DisplayOrder`, nothing else per-row). Uniqueness-of-`StatusId`
check in the update list still applies (same shape, renamed to `CategoryId`).

## Task 5: Controller routes

Add the 5 routes to `TasksController.cs` (or wherever Part 1 ended up placing the re-scoped Task Status
routes — same controller, same section of the file). Contracts (`CreateTaskCategoryRequest`,
`EditTaskCategoryRequest`, `ReorderTaskCategoriesRequest`) go in
`src/ONEVO.Api/Contracts/WorkManagement/Tasks/` alongside the existing Task Status contracts, same file
or a sibling file per whatever this project's existing convention is for grouping contracts (check
`TaskContracts.cs`'s current file boundaries before deciding).

## Task 6: Postman docs

One `.md` file per endpoint under `docs/postman-request/Work Management/` — `Get Project Task
Categories.md`, `Create Task Category.md`, `Edit Task Category.md`, `Delete Task Category.md`, `Reorder
Task Categories.md`. Follow the standard 6-section format (`PROCESS_RULES.md` rule 6): method+route,
auth/permission/idempotency, description, request example, response example, error table, Source
section. Update `docs/postman-request/README.md`'s Work Management count and file list to include these
5 (currently 51 after this session's earlier Sprint-doc commit — becomes 56).

## Task 7: Full regression pass

1. `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement`.
2. `dotnet build`.
3. Manually verify (via the Postman docs' own request examples, or a local `dotnet run` + curl/Postman
   session if one is already available — do not start a new `dotnet run` without asking first, per this
   module's standing scope-guard rule) that creating, editing, reordering, and deleting a category
   against a real seeded Project behaves as documented.

## Definition of done

- Tasks 1-6 committed (one commit per command/query is reasonable given how small each is; combine
  Create/Edit/Delete into one commit if that reads more naturally for this Part).
- Task 7's regression pass is clean.
- All 5 new Postman docs exist and `docs/postman-request/README.md` is updated.
- This whole `2026-08-21-work-management-project-scoped-task-status-and-category/` plan folder (Parts
  1-4) stays in `plans/next/` — pending the frontend companion plan (separate, written in the frontend
  repo) and a manual browser pass, same convention as every other feature in this project's plan
  history.
