# Frontend Request: Two Work Management Additions Needed for the Projects Screens

**Status:** Shipped 2026-08-09. Captured 2026-08-08 while brainstorming the frontend's Work Management Projects List / Project Detail design (see the frontend repo's `docs/superpowers/specs/next/2026-08-08-work-management-projects-milestones-design.md`, §6); implemented the same session per direct user request, skipping a separate brainstorm/spec pass since both items were already fully specified below. See "What shipped" at the bottom of this file for the as-built detail.

**Context:** The frontend is building the Projects list + Create/Edit/Delete/Achieve/Unachieve popups + a read-only milestone view, against the existing Work Management API. Two things are missing that block two specific pieces of that UI (category picker/filter, and the active/archived filter) — everything else in that design works against today's API unchanged. Both were confirmed by reading the actual C# source, not just the Postman docs (which are stale on one of these — see below).

## 1. No endpoint lists Project Categories

`ProjectsController` and `ObjectivesController` are the only two controllers under `src/ONEVO.Api/Controllers/Tenant/WorkManagement/` — there is no `ProjectCategoriesController` or equivalent anywhere in the backend.

The `ProjectCategory` entity exists (`src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/ProjectCategory.cs`: `Id`, `Name`, `IsActive`, plus `BaseEntity`'s `TenantId` etc.) and `IProjectCategoryRepository` (`src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectCategoryRepository.cs`) exists — but it only has `GetByIdForTenantAsync(tenantId, id)`, used internally by `CreateProjectCommandHandler`/`EditProjectCommandHandler` to validate a submitted `categoryId`. There is no list-all method and no query/controller/endpoint exposing categories to a client at all.

**Needed:** A read endpoint the frontend can call to populate a category dropdown (Create/Edit Project form) and a category filter (Project list page). Minimal shape: `GET /api/v1/work/project-categories` → `[{ id, name }]`, active-only by default (mirrors the `includeInactive` pattern already used by `GET /api/v1/org/legal-entities`, if that convention should carry over). Gate: same `projects:access` permission as the rest of Work Management, or `projects:read` — whichever this feature decides fits the module's existing read-permission split (see `List Projects.md`'s `projects:access` vs `projects:read` distinction for precedent).

**Repository method — confirmed with user 2026-08-08:** add a sibling method to `IProjectCategoryRepository`, tenant-scoped the same way as the existing one: `Task<IReadOnlyList<ProjectCategory>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default)`. Keep `GetByIdForTenantAsync` as-is (still used by Create/Edit's `categoryId` validation) — this is an addition, not a replacement.

Note on scoping (confirmed with user 2026-08-08, to avoid ambiguity for whoever implements this): `ProjectCategory` has no `UserId` column — only `TenantId` (via `BaseEntity`). Categories are shared across every user in a tenant, not owned per-user. `GetByIdForTenantAsync`'s existing `id` parameter is the category's own `Id` (e.g. "Backend", "Marketing"); the `tenantId` parameter there is purely tenant-isolation, same as it will be on the new `GetAllForTenantAsync` — never a user filter.

## 2. List Projects response is missing `isAchieved`/`achievedAt`

`ProjectListItemViewModel` (`src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectListItemViewModel.cs`, used by `GET /api/v1/work/projects/mine` and `GET /api/v1/work/projects?userId=`) is:

```csharp
public sealed record ProjectListItemViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead);
```

`ProjectDetailViewModel` (`GET /api/v1/work/projects/{id}`, the single-project read) already has both fields:

```csharp
public sealed record ProjectDetailViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
```

(The migration `20260807114059_AddObjectiveAndProjectAchievedState` already added the underlying `IsAchieved`/`AchievedAt` columns to `projects` — this is a view-model/mapping gap, not a schema gap. `docs/postman-request/Work Management/Get Project.md` is also stale here — it doesn't document these two fields even though `ProjectDetailViewModel` already returns them; worth a docs pass separately per `docs/superpowers/rules/PROCESS_RULES.md` rule 7.)

**Needed:** Add `IsAchieved`/`AchievedAt` to `ProjectListItemViewModel` and the mapper that builds it (`ProjectViewModelMapper.ToViewModel(ProjectListItemResponse)`), sourced the same way `ProjectDetailViewModel`'s mapper already does. This lets the frontend show an achieved badge per card and implement an Active/Archived filter without an extra `GET /work/projects/{id}` call per card.

## Suggested next step

Both are small, same-module additions — bundle them into one slice (confirmed with the frontend-side user 2026-08-08: do them together rather than as two separate asks). Run `superpowers:brainstorming` fresh on this when picked up; likely a single half-day slice (one new query + controller action for #1, one view-model field addition + mapper update for #2, no new tables, no migration for #2 since the columns already exist).

## What shipped (2026-08-09)

Both landed exactly as specified above, bundled into one change:

**#1 — `GET /api/v1/work/project-categories`:**
- `IProjectCategoryRepository.GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default)` — added to the interface and to `EfProjectCategoryRepository`, filtering `IsActive` unless `includeInactive` is true, ordered by `Name`. `GetByIdForTenantAsync` untouched.
- New `ListProjectCategoriesQuery`/`ListProjectCategoriesQueryHandler` (`Features/WorkManagement/Projects/Queries/ListProjectCategories/`) — same auth-check shape as `GetProjectByIdQueryHandler` (authenticated + tenant context required), no membership fallback (categories are tenant-wide, not per-project).
- New `ProjectCategoryListItemResponse(Guid Id, string Name)` (Application DTO) and `ProjectMapper.ToListItem(ProjectCategory)`.
- New `ProjectCategoryViewModel(Guid Id, string Name)` (API contract) and its `ToViewModel()` mapper extension in the existing `ProjectViewModelMapper.cs`.
- New `ProjectCategoriesController` (`Controllers/Tenant/WorkManagement/`) — `[Route("api/v1/work/project-categories")]`, single `[HttpGet]` action, `[RequirePermission("projects:access")]` (settled on `projects:access` alone, not `projects:read` as an alternative — matches the module-wide base gate every other Work Management read/write endpoint already uses), `includeInactive` as an optional query param (default `false`).
- No new DI registration needed — `EfProjectCategoryRepository` was already registered for both its concrete type and `IProjectCategoryRepository` in `DependencyInjection.cs`.

**#2 — `isAchieved`/`achievedAt` on `GET /api/v1/work/projects/mine`:**
- Added to `ProjectListItemResponse` (Application DTO), `ProjectMapper.ToListItem(Project, bool)` (now passes `project.IsAchieved, project.AchievedAt`), `ProjectListItemViewModel` (API contract), and `ProjectViewModelMapper.ToViewModel(ProjectListItemResponse)`. Applies to both `ListMine` and `ListByUser` (`?userId=`) — both go through the same handler/mapper.

**Verification:** `dotnet build src/ONEVO.Api/ONEVO.Api.csproj` — 0 warnings, 0 errors. New `ListProjectCategoriesQueryHandlerTests.cs` added (auth/tenant-context checks + `includeInactive` pass-through, matching `GetProjectByIdQueryHandlerTests`'s Moq/xUnit convention). `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"` — 160/160 passed (156 pre-existing + 4 new), no regressions.

**Docs updated alongside:** `docs/postman-request/Work Management/List Projects.md` (added `isAchieved`/`achievedAt` to both routes' response shape), new `docs/postman-request/Work Management/List Project Categories.md`, `docs/postman-request/README.md` module count bumped to 24.
