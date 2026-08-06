# Work Management — Edit/Delete/View Project Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `PUT /api/v1/work/projects/{id}` (Edit), `DELETE /api/v1/work/projects/{id}` (soft delete), `GET /api/v1/work/projects/{id}` (replaces the existing `501` placeholder), `GET /api/v1/work/projects/mine`, and `GET /api/v1/work/projects?userId={userId}` — the second slice of `ProjectsController`, on top of the already-shipped Foundation slice (Create Project).

**Architecture:** Same ASP.NET Core Clean Architecture / CQRS-via-MediatR / EF Core (Npgsql/PostgreSQL) stack as the Foundation slice. No new tables, columns, or migrations — every change is new repository query/update methods on the three existing repositories (`IProjectRepository`, `IObjectiveRepository`, `IProjectMemberRepository`), new MediatR commands/queries, and new controller actions. Edit's Project+Default-Objective cascade update is one `IUnitOfWork.SaveChangesAsync` transaction, matching the Foundation slice's multi-entity-write pattern exactly.

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql provider), PostgreSQL, MediatR, FluentValidation, xUnit + Moq (unit), xUnit + Testcontainers (integration), `dotnet test`.

## Global Constraints

- Domain (`ONEVO.Domain`) must not reference Application, Infrastructure, API, or EF Core.
- Application must not reference Infrastructure implementations or `HttpContext`/`IFormFile`.
- Controllers must never inject or use `ApplicationDbContext`. `ProjectsController` continues to only inject `IMediator` (no `ICurrentUser` in the controller — every handler resolves tenant/user context itself via `ICurrentUser`, matching `CreateProjectCommandHandler`'s existing convention).
- Every async method takes `CancellationToken` and is awaited; no `.Result`/`.Wait()`.
- Validation runs through the existing MediatR `ValidationBehavior` (FluentValidation) — handlers never call a validator manually.
- Use `Result`/`Result<T>` exactly as defined in `src/ONEVO.Application/Common/Models/Result.cs` — no `ToActionResult()`; controllers use the inline `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)` ternary.
- `tenant_id` and `userId` are never trusted from the request body — resolved from `ICurrentUser` inside each handler.
- No new migration: `Project`/`Objective`/`ProjectMember` schemas are unchanged; this slice only adds C# read/update methods against existing columns.
- No optimistic concurrency token — per the design (`docs/superpowers/specs/2026-08-04-work-management-projects-edit-delete-view-design.md` §2), Edit ships as plain last-write-wins. `ProjectConfiguration.cs`/`ObjectiveConfiguration.cs` are not modified by this plan.
- **Permission model updated 2026-08-04** per `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §2: `projects:write` is retired in favor of `projects:access` (the new module-wide base gate) for every action in this plan except `ListByUser`, which keeps `projects:read` (unchanged — the cross-user "view others" permission). `projects:access` is also newly required on `ListMine` (`/mine`), which previously required no permission at all. `projects:create` (used by the already-shipped `Create` action, Foundation Slice 1) is intentionally **left unchanged** in this plan's Task 7 — retiring it is a separate follow-up that also needs a `PermissionSeeder.cs` change and a data migration for existing tenants' `role_permissions` rows (tracked in the milestone-hierarchy design §8), not silently folded in here. `PermissionSeeder.cs` does not yet seed `projects:access` — implementing Task 7 as written below requires that seed to exist first (a prerequisite task, not currently in this plan — see the note at the top of Task 7).
- `docs/postman-request/Work Management/<Endpoint Name>.md` is required for every finished endpoint per `docs/superpowers/rules/PROCESS_RULES.md` rule 6 — Task 9 adds the four new ones.

### Corrections to the design doc (found during pre-implementation verification, 2026-08-04)

The design (`specs/2026-08-04-work-management-projects-edit-delete-view-design.md`) was checked against the actual current code before writing this plan. It is correct on architecture and scope; four implementation-level details it stated were inaccurate. Per `PROCESS_RULES.md` rule 5, the spec file itself is not edited — corrections are recorded here instead, the same way the Foundation plan logged its own execution-time deviations:

1. **§2's "same length limits on name/description/color as Create" is not accurate.** `CreateProjectCommandValidator` has no rule for `Description` or `Color` at all — `Color`'s only length limit today is the DB-layer `HasMaxLength(20)` in `ProjectConfiguration.cs`, so an over-length `Color` currently fails as an unhandled EF/DB exception (500), not a clean `400`, in Create. This plan's `EditProjectCommandValidator` (Task 3) adds a real `MaximumLength(20)` rule for `Color` so Edit does not inherit that latent bug. `Description` is left deliberately unbounded (matching Create's actual behavior, not the design's stated-but-nonexistent limit) — no arbitrary length invented without a schema basis.
2. **§4/5's claim that `PagedRequest`/`PagedResult<T>` are "already used by `ListTenantsQueryHandler`"** is false — a repo-wide search found they are referenced nowhere outside their own definition files (`src/ONEVO.Application/Common/Models/PagedRequest.cs`, `PagedResult.cs`); `ListTenantsQueryHandler` hand-rolls its own paging with `DefaultPageSize = 25`, not 20, and no sort support. This plan (Task 6) wires up the real `PagedRequest`/`PagedResult<T>` types for the first time in this codebase rather than adding a third bespoke paging shape or perpetuating the false "already proven" claim — `PagedRequest`'s own default (`PageSize = 20`) is used as documented in the design, it just isn't inherited from `ListTenantsQueryHandler`.
3. **§1/§2's category-existence check ("must exist/be active/belong to tenant → 404") is validator-level per the design's phrasing, but in Create it is actually handler-level** (`CreateProjectCommandHandler.cs:101-103`, via `IProjectCategoryRepository.GetByIdForTenantAsync` + `IsActive` check, not a FluentValidation rule — a validator cannot await a repository call). Edit's handler (Task 3) puts the equivalent check in the same place: the handler, not the validator.
4. **The design assumes `IProjectRepository`/`IObjectiveRepository`/`IProjectMemberRepository` are usable as-is for reads.** In fact all three currently expose only `AddAsync`/existence-check methods (no `GetById`, no update, no membership lookup, no list-join). Task 1 below adds every read/update method this slice needs; this is new repository surface, not reuse of something that already existed.

---

### Task 1: Repository read/update methods (`IProjectRepository`, `IObjectiveRepository`, `IProjectMemberRepository`)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext.Projects`/`Objectives`/`ProjectMembers` `DbSet<T>` properties (already registered, Foundation Task 2).
- Produces: `IProjectRepository.GetByIdForTenantAsync`, `.Update`, `.ListForMemberAsync`; `IObjectiveRepository.GetDefaultByProjectIdAsync`, `.Update`; `IProjectMemberRepository.HasActiveMembershipAsync` — consumed by every handler in Tasks 3-6.

These are plain data-access methods with no independent business logic (same precedent as `EfLegalEntityRepository`'s `GetByIdForTenantAsync`/`Update` pair). Verification for this task is a successful build, not a red/green unit-test cycle — correctness of the query logic (especially the `ListForMemberAsync` DISTINCT-join) is proven by the Task 8 integration tests, which exercise it over a real PostgreSQL database.

- [ ] **Step 1: `IProjectRepository` — add `GetByIdForTenantAsync`, `Update`, `ListForMemberAsync`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectRepository
{
    Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default);

    Task AddAsync(Project project, CancellationToken ct = default);

    Task<Project?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(Project project);

    /// <summary>
    /// Projects where the given user has at least one active project_members row, joined and
    /// distinct on project_id (a user can be a member of the same project via more than one
    /// Objective, since project_members' uniqueness is (tenant_id, project_id, objective_id,
    /// user_id), not (tenant_id, project_id, user_id) — this must never return the same project
    /// twice). Both the project and the membership row must be active.
    /// </summary>
    Task<(IReadOnlyList<Project> Items, int TotalCount)> ListForMemberAsync(
        Guid tenantId, Guid targetUserId, int skip, int take, string? sortBy, string sortDirection,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: `EfProjectRepository` — implement the three new members**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Identifier == identifier, ct);
    }

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        await _db.Projects.AddAsync(project, ct);
    }

    public async Task<Project?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, ct);
    }

    public void Update(Project project)
    {
        _db.Projects.Update(project);
    }

    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> ListForMemberAsync(
        Guid tenantId, Guid targetUserId, int skip, int take, string? sortBy, string sortDirection,
        CancellationToken ct = default)
    {
        var baseQuery = (
            from pm in _db.ProjectMembers.AsNoTracking()
            join p in _db.Projects.AsNoTracking() on pm.ProjectId equals p.Id
            where pm.TenantId == tenantId && pm.UserId == targetUserId && pm.IsActive && p.IsActive
            select p
        ).Distinct();

        var total = await baseQuery.CountAsync(ct);

        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        var ordered = (normalizedSortBy, descending) switch
        {
            ("name", true) => baseQuery.OrderByDescending(p => p.Name),
            ("name", false) => baseQuery.OrderBy(p => p.Name),
            ("startdate", true) => baseQuery.OrderByDescending(p => p.StartDate),
            ("startdate", false) => baseQuery.OrderBy(p => p.StartDate),
            ("targetdate", true) => baseQuery.OrderByDescending(p => p.TargetDate),
            ("targetdate", false) => baseQuery.OrderBy(p => p.TargetDate),
            (_, true) => baseQuery.OrderByDescending(p => p.CreatedAt),
            _ => baseQuery.OrderBy(p => p.CreatedAt)
        };

        var items = await ordered.Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }
}
```

- [ ] **Step 3: `IObjectiveRepository` — add `GetDefaultByProjectIdAsync`, `Update`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

public interface IObjectiveRepository
{
    Task AddAsync(Objective objective, CancellationToken ct = default);

    Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    void Update(Objective objective);
}
```

- [ ] **Step 4: `EfObjectiveRepository` — implement the two new members**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveRepository : IObjectiveRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Objective objective, CancellationToken ct = default)
    {
        await _db.Objectives.AddAsync(objective, ct);
    }

    public async Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault, ct);
    }

    public void Update(Objective objective)
    {
        _db.Objectives.Update(objective);
    }
}
```

- [ ] **Step 5: `IProjectMemberRepository` — add `HasActiveMembershipAsync`**

```csharp
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);

    Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default);
}
```

- [ ] **Step 6: `EfProjectMemberRepository` — implement `HasActiveMembershipAsync`**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }

    public async Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid projectId, Guid userId, CancellationToken ct = default)
    {
        return await _db.ProjectMembers
            .AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.ProjectId == projectId && m.UserId == userId && m.IsActive, ct);
    }
}
```

- [ ] **Step 7: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: build succeeds with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectRepository.cs src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs
git commit -m "feat(work-management): add project/objective/member read+update repository methods"
```

---

### Task 2: Response DTOs, ViewModels, and mapper extensions

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectDetailResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectListItemResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectDetailViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectListItemViewModel.cs`
- Create: `src/ONEVO.Api/Contracts/Common/PagedResultViewModel.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs`

**Interfaces:**
- Consumes: `Project` entity (Foundation Task 1).
- Produces: `ProjectMapper.ToDetail(Project, bool isLead)`, `ProjectMapper.ToListItem(Project, bool isLead)` — consumed by Tasks 3-6's handlers. `ProjectViewModelMapper`'s new `ToViewModel` overloads — consumed by Task 7's controller actions.

Plain data holders and pure mapping functions, same precedent as Foundation Task 1/`ProjectMapper.cs`/`ProjectViewModelMapper.cs` — no independent behavior to unit-test; verification is a successful build.

- [ ] **Step 1: `ProjectDetailResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectDetailResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
```

- [ ] **Step 2: `ProjectListItemResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectListItemResponse(
    Guid Id, string Name, string Identifier, Guid CategoryId, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead);
```

- [ ] **Step 3: Extend `ProjectMapper` with `ToDetail`/`ToListItem`**

Add to `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs` (inside the existing `ProjectMapper` static class, alongside the existing `ToSummary` overloads):

```csharp
    public static ProjectDetailResponse ToDetail(Project project, bool isLead) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.Description,
        project.LeadId, project.StartDate, project.TargetDate, project.Color,
        project.ActualHours, project.AllocatedHours, project.CompletedHours,
        project.IsActive, project.CreatedAt, project.UpdatedAt, isLead);

    public static ProjectListItemResponse ToListItem(Project project, bool isLead) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.LeadId,
        project.StartDate, project.TargetDate, project.Color, project.IsActive,
        project.AllocatedHours, project.CompletedHours, isLead);
```

- [ ] **Step 4: `ProjectDetailViewModel`**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectDetailViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsLead);
```

- [ ] **Step 5: `ProjectListItemViewModel`**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public sealed record ProjectListItemViewModel(
    Guid Id, string Name, string Identifier, Guid CategoryId, Guid LeadId,
    DateOnly StartDate, DateOnly TargetDate, string? Color, bool IsActive,
    decimal AllocatedHours, decimal CompletedHours, bool IsLead);
```

- [ ] **Step 6: `PagedResultViewModel<T>`**

First API-layer consumer of paging in this codebase — a thin 1:1 mirror of `ONEVO.Application.Common.Models.PagedResult<T>`, kept in a new `Contracts/Common/` folder since it isn't specific to Work Management and future paged endpoints elsewhere can reuse it.

```csharp
namespace ONEVO.Api.Contracts.Common;

public sealed record PagedResultViewModel<T>(
    IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasNext, bool HasPrevious);
```

- [ ] **Step 7: Extend `ProjectViewModelMapper` with the new mappings**

Add to `src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs` (add `using ONEVO.Api.Contracts.Common;` at the top):

```csharp
    public static ProjectDetailViewModel ToViewModel(this ProjectDetailResponse dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.Description,
        dto.LeadId, dto.StartDate, dto.TargetDate, dto.Color,
        dto.ActualHours, dto.AllocatedHours, dto.CompletedHours,
        dto.IsActive, dto.CreatedAt, dto.UpdatedAt, dto.IsLead);

    public static ProjectListItemViewModel ToViewModel(this ProjectListItemResponse dto) => new(
        dto.Id, dto.Name, dto.Identifier, dto.CategoryId, dto.LeadId,
        dto.StartDate, dto.TargetDate, dto.Color, dto.IsActive,
        dto.AllocatedHours, dto.CompletedHours, dto.IsLead);

    public static PagedResultViewModel<ProjectListItemViewModel> ToViewModel(this PagedResult<ProjectListItemResponse> page) => new(
        page.Items.Select(ToViewModel).ToList(), page.PageNumber, page.PageSize, page.TotalCount, page.TotalPages, page.HasNext, page.HasPrevious);
```

(`PagedResult<T>` needs `using ONEVO.Application.Common.Models;` added to the file's using list too.)

- [ ] **Step 8: Verify build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj && dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: build succeeds with 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectDetailResponse.cs src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectListItemResponse.cs src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectDetailViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectListItemViewModel.cs src/ONEVO.Api/Contracts/Common/PagedResultViewModel.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/ProjectViewModelMapper.cs
git commit -m "feat(work-management): add project detail/list response DTOs and view models"
```

---

### Task 3: `EditProjectCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EditProjectCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.GetByIdForTenantAsync`/`.Update`, `IObjectiveRepository.GetDefaultByProjectIdAsync`/`.Update`, `IProjectCategoryRepository.GetByIdForTenantAsync` (Task 1 and Foundation), `IUnitOfWork.SaveChangesAsync` (Foundation), `ProjectMapper.ToDetail` (Task 2).
- Produces: `EditProjectCommand(Guid ProjectId, string Name, string? Description, Guid CategoryId, DateOnly StartDate, DateOnly TargetDate, string? Color, decimal? ActualHours, string? Identifier) : IRequest<Result<ProjectDetailResponse>>` — consumed by Task 7's controller.

**Ownership check added 2026-08-04** per `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §4: a Project is the tree's root node, and the tree's recursive rule says only a node's own Head — the Project's `LeadId` — has unrestricted control over that node itself. The handler below therefore returns `403` for any caller who is not the project's lead, matching `DeleteProjectCommandHandler`'s existing lead-only check exactly (same rule, no approval needed either, since the Project is the root — see the design's §4 root exception).

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class EditProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static EditProjectCommand ValidCommand(string? identifier = "WEB") => new(
        ProjectId, "Website Revamp v2", "updated desc", CategoryId,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1), "#111111", 12m, identifier);

    private static Project ExistingProject(Guid? leadId = null) => new()
    {
        Id = ProjectId, TenantId = TenantId, CategoryId = CategoryId, Name = "Website Revamp",
        Identifier = "WEB", LeadId = leadId ?? UserId, StartDate = new DateOnly(2026, 1, 1),
        TargetDate = new DateOnly(2026, 6, 1), IsActive = true, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective ExistingDefaultObjective() => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true,
        Title = "Website Revamp", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1),
        OwnerId = UserId, CreatedAt = DateTimeOffset.UtcNow
    };

    private (EditProjectCommandHandler Handler, Mock<IProjectRepository> Projects, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, Objective? defaultObjective, bool categoryExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categoryExists ? new ProjectCategory { Id = CategoryId, TenantId = TenantId, Name = "General", IsActive = true } : null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new EditProjectCommandHandler(currentUser.Object, projects.Object, objectives.Object, categories.Object, unitOfWork.Object);
        return (handler, projects, objectives);
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesProjectAndCascadesDefaultObjective()
    {
        var (handler, projects, objectives) = BuildHandler(ExistingProject(), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Website Revamp v2", result.Value!.Name);
        projects.Verify(x => x.Update(It.Is<Project>(p => p.Name == "Website Revamp v2" && p.TargetDate == new DateOnly(2026, 7, 1))), Times.Once);
        objectives.Verify(x => x.Update(It.Is<Objective>(o => o.Title == "Website Revamp v2" && o.EndDate == new DateOnly(2026, 7, 1))), Times.Once);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(null, null);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_IdentifierChangeAttempted_ReturnsBadRequest()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(identifier: "DIFFERENT"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_IdentifierOmittedOrBlank_SkipsImmutabilityCheck(string? identifier)
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(identifier: identifier), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundForTenant_ReturnsNotFound()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(), ExistingDefaultObjective(), categoryExists: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _, _) = BuildHandler(ExistingProject(leadId: OtherUserId), ExistingDefaultObjective());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (types don't exist yet)**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditProjectCommandHandlerTests`
Expected: FAIL to compile — `EditProjectCommand`/`EditProjectCommandHandler` not defined.

- [ ] **Step 3: `EditProjectCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public sealed record EditProjectCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly TargetDate,
    string? Color,
    decimal? ActualHours,
    string? Identifier
) : IRequest<Result<ProjectDetailResponse>>;
```

- [ ] **Step 4: `EditProjectCommandValidator`**

Reuses Create's rules where they apply (`Name`, `TargetDate >= StartDate`, `CategoryId` non-empty, `ActualHours >= 0`), plus a real `Color` length rule that Create itself is missing (see the "Corrections to the design doc" note above — `Color`'s only current enforcement is the DB column's `HasMaxLength(20)`, which this validator now catches as a clean `400` instead of a DB exception). `Description` is deliberately left without a rule, matching Create's actual (unbounded) behavior.

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public class EditProjectCommandValidator : AbstractValidator<EditProjectCommand>
{
    public EditProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must be 200 characters or fewer.");

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage("Category is required.");

        RuleFor(x => x.TargetDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("Target date must not be earlier than start date.");

        RuleFor(x => x.Color)
            .MaximumLength(20).WithMessage("Color must be 20 characters or fewer.")
            .When(x => x.Color is not null);

        RuleFor(x => x.ActualHours)
            .GreaterThanOrEqualTo(0).WithMessage("Actual hours must not be negative.")
            .When(x => x.ActualHours is not null);
    }
}
```

- [ ] **Step 5: `EditProjectCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;

public class EditProjectCommandHandler : IRequestHandler<EditProjectCommand, Result<ProjectDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectCategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;

    public EditProjectCommandHandler(
        ICurrentUser currentUser,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IProjectCategoryRepository categories,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _objectives = objectives;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectDetailResponse>> Handle(EditProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectDetailResponse>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result<ProjectDetailResponse>.NotFound("Project not found.");

        // Project is the tree's root node (milestone-hierarchy design §4) - only its own
        // Head, the Lead, has unrestricted control over the node itself. Same rule
        // DeleteProjectCommandHandler already enforces; no approval needed either, since
        // the root has no Reporting Manager to route a request to.
        if (project.LeadId != userId)
            return Result<ProjectDetailResponse>.Forbidden("Only the project lead can edit this project.");

        // Blank (empty/whitespace-only) is treated the same as omitted, not as "change to
        // blank" - many JSON clients default an untouched optional string field to "" rather
        // than null, and that must not trip a spurious 400 on every ordinary edit.
        if (!string.IsNullOrWhiteSpace(request.Identifier))
        {
            var normalizedIdentifier = request.Identifier.Trim().ToUpperInvariant();
            if (!string.Equals(normalizedIdentifier, project.Identifier, StringComparison.Ordinal))
                return Result<ProjectDetailResponse>.Failure("Project identifier cannot be changed after creation.");
        }

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null || !category.IsActive)
            return Result<ProjectDetailResponse>.NotFound("Project category not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<ProjectDetailResponse>.NotFound("Default objective not found for this project.");

        var now = DateTimeOffset.UtcNow;

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.CategoryId = category.Id;
        project.StartDate = request.StartDate;
        project.TargetDate = request.TargetDate;
        project.Color = request.Color;
        project.ActualHours = request.ActualHours;
        project.UpdatedAt = now;

        // Default Objective mirrors the Project's title/description/dates and "stays in sync
        // on Project edit" per phase1-table-inventory.md. LeadId/Identifier/AllocatedHours/
        // CompletedHours/IsActive are intentionally left untouched - not part of this request.
        defaultObjective.Title = project.Name;
        defaultObjective.Description = project.Description;
        defaultObjective.StartDate = project.StartDate;
        defaultObjective.EndDate = project.TargetDate;
        defaultObjective.UpdatedAt = now;

        _projects.Update(project);
        _objectives.Update(defaultObjective);
        await _unitOfWork.SaveChangesAsync(ct);

        // Always true here - the lead-only check above already rejected any non-lead caller.
        return Result<ProjectDetailResponse>.Success(ProjectMapper.ToDetail(project, isLead: true));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter EditProjectCommandHandlerTests`
Expected: PASS (8/8 — 5 `[Fact]`s + the 3-case `[Theory]`).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject tests/ONEVO.Tests.Unit/Features/WorkManagement/EditProjectCommandHandlerTests.cs
git commit -m "feat(work-management): add EditProjectCommand vertical slice"
```

---

### Task 4: `DeleteProjectCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject/DeleteProjectCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject/DeleteProjectCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteProjectCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.GetByIdForTenantAsync`/`.Update` (Task 1), `IUnitOfWork.SaveChangesAsync` (Foundation).
- Produces: `DeleteProjectCommand(Guid ProjectId) : IRequest<Result>` — consumed by Task 7's controller.

No validator: the command has no user-supplied fields beyond the route id, so there is nothing for FluentValidation to check (matches `DeleteLegalEntityCommand`'s sibling pattern, except that command does carry a confirm-name field this one doesn't need).

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class DeleteProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject(Guid leadId) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = leadId, IsActive = true,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (DeleteProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(Project? project)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteProjectCommandHandler(currentUser.Object, projects.Object, unitOfWork.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_LeadDeletesActiveProject_Succeeds()
    {
        var (handler, projects) = BuildHandler(ActiveProject(leadId: UserId));

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.Update(It.Is<Project>(p => !p.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Handle_NonLeadCaller_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(leadId: OtherUserId));

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsConflict()
    {
        var project = ActiveProject(leadId: UserId);
        project.IsActive = false;
        var (handler, _) = BuildHandler(project);

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new DeleteProjectCommand(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter DeleteProjectCommandHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `DeleteProjectCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;

public sealed record DeleteProjectCommand(Guid ProjectId) : IRequest<Result>;
```

- [ ] **Step 4: `DeleteProjectCommandHandler`**

Lead-check happens before the already-deleted check, so a non-lead never learns whether a project is already deleted (matches the design's error precedence: `403` for "not the lead" is a stricter gate than the `409` idempotency-of-state check).

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;

public class DeleteProjectCommandHandler : IRequestHandler<DeleteProjectCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProjectCommandHandler(ICurrentUser currentUser, IProjectRepository projects, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _projects = projects;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null)
            return Result.NotFound("Project not found.");

        if (project.LeadId != userId)
            return Result.Forbidden("Only the project lead can delete this project.");

        if (!project.IsActive)
            return Result.Conflict("Project already deleted.");

        project.IsActive = false;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        _projects.Update(project);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter DeleteProjectCommandHandlerTests`
Expected: PASS (4/4).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject tests/ONEVO.Tests.Unit/Features/WorkManagement/DeleteProjectCommandHandlerTests.cs
git commit -m "feat(work-management): add DeleteProjectCommand vertical slice"
```

---

### Task 5: `GetProjectByIdQuery` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/GetProjectByIdQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/GetProjectByIdQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetProjectByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.GetByIdForTenantAsync` (Task 1), `IProjectMemberRepository.HasActiveMembershipAsync` (Task 1), `IPermissionResolver.ResolveAsync` (existing, `src/ONEVO.Application/Features/Auth/Permission/ServiceInterfaces/IPermissionResolver.cs`).
- Produces: `GetProjectByIdQuery(Guid ProjectId) : IRequest<Result<ProjectDetailResponse>>` — consumed by Task 7's controller.

This is the endpoint that replaces the `501` placeholder and implements the design's dual-path authorization (§Endpoint 3): `projects:read` (or `*`) grants access outright; otherwise an active `project_members` row for this exact project does. `IPermissionResolver.ResolveAsync` is called directly (not `ICurrentUser.HasPermission`) so this always checks the caller's current effective permission set, not whatever permission claims happen to be baked into their session token.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetProjectByIdQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LeadId = Guid.NewGuid();

    private static Project Project(bool isActive = true) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = LeadId, IsActive = isActive,
        Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetProjectByIdQueryHandler Handler, Mock<IProjectMemberRepository> Members) BuildHandler(
        Project? project, List<string> permissions, bool isActiveMember)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isActiveMember);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

        var handler = new GetProjectByIdQueryHandler(currentUser.Object, projects.Object, members.Object, permissionResolver.Object);
        return (handler, members);
    }

    [Fact]
    public async Task Handle_HasReadPermission_SucceedsWithoutCheckingMembership()
    {
        var (handler, members) = BuildHandler(Project(), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        members.Verify(x => x.HasActiveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoPermissionButActiveMember_Succeeds()
    {
        var (handler, _) = BuildHandler(Project(), [], isActiveMember: true);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NoPermissionAndNotMember_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(Project(), [], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WildcardPermission_Succeeds()
    {
        var (handler, _) = BuildHandler(Project(), ["*"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Project(isActive: false), ["projects:read"], isActiveMember: false);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LeadCaller_IsLeadTrue()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(LeadId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(Project());

        var members = new Mock<IProjectMemberRepository>();
        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(LeadId, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(["projects:read"]);

        var handler = new GetProjectByIdQueryHandler(currentUser.Object, projects.Object, members.Object, permissionResolver.Object);

        var result = await handler.Handle(new GetProjectByIdQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsLead);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetProjectByIdQueryHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `GetProjectByIdQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

public sealed record GetProjectByIdQuery(Guid ProjectId) : IRequest<Result<ProjectDetailResponse>>;
```

- [ ] **Step 4: `GetProjectByIdQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;

public class GetProjectByIdQueryHandler : IRequestHandler<GetProjectByIdQuery, Result<ProjectDetailResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public GetProjectByIdQueryHandler(
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver)
    {
        _currentUser = currentUser;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<ProjectDetailResponse>> Handle(GetProjectByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectDetailResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectDetailResponse>.Forbidden("Tenant context missing.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<ProjectDetailResponse>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var isMember = await _members.HasActiveMembershipAsync(tenantId, project.Id, userId, ct);
            if (!isMember)
                return Result<ProjectDetailResponse>.Forbidden("You do not have access to this project.");
        }

        var isLead = project.LeadId == userId;
        return Result<ProjectDetailResponse>.Success(ProjectMapper.ToDetail(project, isLead));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter GetProjectByIdQueryHandlerTests`
Expected: PASS (6/6).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById tests/ONEVO.Tests.Unit/Features/WorkManagement/GetProjectByIdQueryHandlerTests.cs
git commit -m "feat(work-management): add GetProjectByIdQuery vertical slice"
```

---

### Task 6: `ListProjectsQuery` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects/ListProjectsQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects/ListProjectsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/ListProjectsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectRepository.ListForMemberAsync` (Task 1), `PagedRequest`/`PagedResult<T>` (existing, `src/ONEVO.Application/Common/Models/`).
- Produces: `ListProjectsQuery(Guid? TargetUserId, PagedRequest Paging) : IRequest<Result<PagedResult<ProjectListItemResponse>>>` — consumed by Task 7's controller for both `GET /projects/mine` (`TargetUserId = null`) and `GET /projects?userId=` (`TargetUserId` set from the query param).

`TargetUserId = null` means "the caller's own id" — resolved inside the handler via `ICurrentUser.UserId`, never passed in by the controller, matching every other handler's convention of resolving identity itself rather than trusting a caller-supplied value.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListProjectsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static Project MakeProject(Guid leadId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, LeadId = leadId, IsActive = true,
        Name = "P", Identifier = "P" + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow
    };

    private (ListProjectsQueryHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
        IReadOnlyList<Project> items, int total)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.ListForMemberAsync(
                TenantId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, total));

        var handler = new ListProjectsQueryHandler(currentUser.Object, projects.Object);
        return (handler, projects);
    }

    [Fact]
    public async Task Handle_NullTargetUserId_ResolvesToCallersOwnId()
    {
        var (handler, projects) = BuildHandler([MakeProject(UserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExplicitTargetUserId_UsesItInsteadOfCaller()
    {
        var (handler, projects) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        projects.Verify(x => x.ListForMemberAsync(TenantId, OtherUserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_IsLead_ComputedAgainstTargetUserIdNotCaller()
    {
        var (handler, _) = BuildHandler([MakeProject(OtherUserId)], 1);

        var result = await handler.Handle(new ListProjectsQuery(OtherUserId, new PagedRequest()), CancellationToken.None);

        Assert.True(result.Value!.Items.Single().IsLead);
    }

    [Fact]
    public async Task Handle_ReturnsPagingMetadataFromRepository()
    {
        var (handler, _) = BuildHandler([MakeProject(UserId)], total: 47);

        var result = await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 2, PageSize = 10 }), CancellationToken.None);

        Assert.Equal(47, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(10, result.Value.PageSize);
    }

    [Fact]
    public async Task Handle_NonPositivePageNumber_ClampedToOne()
    {
        var (handler, projects) = BuildHandler([], 0);

        await handler.Handle(new ListProjectsQuery(null, new PagedRequest { PageNumber = 0 }), CancellationToken.None);

        projects.Verify(x => x.ListForMemberAsync(TenantId, UserId, 0, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ListProjectsQueryHandlerTests`
Expected: FAIL to compile — types not defined.

- [ ] **Step 3: `ListProjectsQuery`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

public sealed record ListProjectsQuery(
    Guid? TargetUserId,
    PagedRequest Paging
) : IRequest<Result<PagedResult<ProjectListItemResponse>>>;
```

- [ ] **Step 4: `ListProjectsQueryHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

public class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, Result<PagedResult<ProjectListItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _projects;

    public ListProjectsQueryHandler(ICurrentUser currentUser, IProjectRepository projects)
    {
        _currentUser = currentUser;
        _projects = projects;
    }

    public async Task<Result<PagedResult<ProjectListItemResponse>>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<PagedResult<ProjectListItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<PagedResult<ProjectListItemResponse>>.Forbidden("Tenant context missing.");

        var targetUserId = request.TargetUserId ?? _currentUser.UserId;
        var pageNumber = request.Paging.PageNumber < 1 ? 1 : request.Paging.PageNumber;
        var skip = (pageNumber - 1) * request.Paging.PageSize;

        var (items, total) = await _projects.ListForMemberAsync(
            tenantId, targetUserId, skip, request.Paging.PageSize, request.Paging.SortBy, request.Paging.SortDirection, ct);

        var dtoItems = items.Select(p => ProjectMapper.ToListItem(p, p.LeadId == targetUserId)).ToList();

        return Result<PagedResult<ProjectListItemResponse>>.Success(
            new PagedResult<ProjectListItemResponse>(dtoItems, pageNumber, request.Paging.PageSize, total));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ListProjectsQueryHandlerTests`
Expected: PASS (5/5).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects tests/ONEVO.Tests.Unit/Features/WorkManagement/ListProjectsQueryHandlerTests.cs
git commit -m "feat(work-management): add ListProjectsQuery vertical slice"
```

---

### Task 7: Controller wiring — `ProjectsController`

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/EditProjectRequest.cs`

**Interfaces:**
- Consumes: `EditProjectCommand` (Task 3), `DeleteProjectCommand` (Task 4), `GetProjectByIdQuery` (Task 5), `ListProjectsQuery` (Task 6), `PagedRequest` (existing), the `ToViewModel` overloads (Task 2).
- Produces: the five HTTP actions this whole plan exists to ship.

Route disambiguation (no ambiguity): `""` (empty, list-by-query-param), `"mine"` (literal), `"{id:guid}"` (guid-constrained parameter) are three distinct templates — ASP.NET Core matches the literal `mine` segment and the empty root template before ever considering the constrained parameter route, exactly as the design notes.

- [ ] **Step 1: `EditProjectRequest` (JSON body contract — Edit has no file upload, unlike Create)**

```csharp
namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public class EditProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }

    /// <summary>Optional. If present and different from the project's current identifier, the request is rejected with 400 — identifier is immutable after creation.</summary>
    public string? Identifier { get; set; }
}
```

**Prerequisite (not part of this plan — flagged here, not silently assumed):** `PermissionSeeder.cs` must seed `projects:access` before this step's `[RequirePermission("projects:access")]` attributes can resolve to anything at runtime — that seed change, and retiring `projects:create`/`projects:write` from it, is tracked as a separate follow-up in `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §8. `projects:create` on `Create` (below) is deliberately left unchanged for the same reason.

- [ ] **Step 2: Replace `ProjectsController.cs` in full**

```csharp
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.DeleteProject;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.EditProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.GetProjectById;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjects;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/projects")]
[Authorize(Policy = "TenantPolicy")]
public class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a Project with its Default Objective, creator membership, Default Version, release reminder, optional labels, and optional logo — all in one atomic transaction.</summary>
    [HttpPost]
    [RequirePermission("projects:create")]
    [Idempotent]
    public async Task<IActionResult> Create([FromForm] CreateProjectFormRequest request, CancellationToken ct)
    {
        var labels = string.IsNullOrWhiteSpace(request.LabelsJson)
            ? new List<CreateProjectLabelInput>()
            : JsonSerializer.Deserialize<List<CreateProjectLabelInput>>(
                request.LabelsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        Stream? logoStream = null;
        if (request.Logo is { Length: > 0 } logo)
            logoStream = logo.OpenReadStream();

        var command = new CreateProjectCommand(
            request.CategoryId,
            request.Name,
            request.Identifier,
            request.Description,
            request.StartDate,
            request.TargetDate,
            request.ReleaseDate,
            request.Color,
            request.ActualHours,
            request.DefaultObjectiveAllocatedHours,
            labels,
            request.Logo?.FileName,
            request.Logo?.ContentType,
            logoStream);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Updates a Project's editable fields (name, description, category, dates, color, actual hours). Cascades the same title/description/dates onto the Project's Default Objective in the same transaction. Identifier is immutable.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditProjectRequest request, CancellationToken ct)
    {
        var command = new EditProjectCommand(
            id, request.Name, request.Description, request.CategoryId,
            request.StartDate, request.TargetDate, request.Color, request.ActualHours, request.Identifier);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Soft-deletes a Project (is_active = false). Only the project lead may delete, even with projects:access. Already-deleted returns 409.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteProjectCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Gets a single Project by id. No [RequirePermission] here on purpose: access is granted by projects:read/* OR by having an active project_members row for this project — the handler checks both, since the attribute alone would hard-block members who lack the tenant-wide permission.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProjectByIdQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>The caller's own projects. Requires projects:access (the module-wide base gate) — this only ever returns the caller's own data, so no additional permission is needed beyond that base gate.</summary>
    [HttpGet("mine")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ListMine([FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(null, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Any given user's projects (admin/company-owner path). If userId doesn't resolve to a user with any active membership, returns an empty page, not 404 — list semantics. projects:read is unchanged by the 2026-08-04 permission-model update (it stays the sole "view others" gate); role configuration is expected to grant projects:access alongside it, not enforced here as a second attribute check.</summary>
    [HttpGet]
    [RequirePermission("projects:read")]
    public async Task<IActionResult> ListByUser([FromQuery] Guid userId, [FromQuery] PagedRequest paging, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListProjectsQuery(userId, paging), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

The old `[HttpGet("{id:guid}")] public IActionResult GetById(Guid id) => StatusCode(501);` placeholder (and its `[RequirePermission("projects:read")]` attribute, which the design explicitly says the real implementation must not carry) is fully replaced by the version above.

- [ ] **Step 3: Verify build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run the full unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all tests pass, including the new ones from Tasks 3-6 and the existing `CreateProjectCommandHandlerTests`.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs src/ONEVO.Api/Contracts/WorkManagement/Projects/EditProjectRequest.cs
git commit -m "feat(work-management): wire Edit/Delete/GetById/List actions into ProjectsController"
```

---

### Task 8: Integration tests — full HTTP flow

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs` (add new `[Fact]` methods to the existing class — same fixture, same two provisioned tenants, no new `InitializeAsync` setup needed)

**Interfaces:**
- Consumes: the existing fixture (`_tenantA`, `_tenantB`, `_tenantACategoryId`, `SendCreateProjectAsync`, `SendJsonAsync`, `ReadJsonAsync` helpers — all already present in this file).

**Scope decision, stated plainly:** this fixture provisions exactly one authenticated user per tenant (the tenant owner, via `ProvisionAndLoginOwnerAsync`). Standing up a second, lower-privileged, authenticated-over-HTTP user within the *same* tenant would require building out the separate employee-invitation accept/login flow — real work, out of scope for this slice. The three permission-vs-membership branches inside `GetProjectByIdQueryHandler` (permission-holder-non-member, member-without-permission, neither→403) are already proven precisely and cheaply at the handler-unit-test level (Task 5, mocked `IPermissionResolver`/`IProjectMemberRepository`) — HTTP-layer coverage below sticks to what the two-owners-per-tenant fixture can reach for real: the owning tenant's owner (who is always both the lead and the creator-membership holder) succeeding, and cross-tenant isolation. Same reasoning applies to `?userId=`'s `projects:read` requirement — `RequirePermissionAttribute`'s generic missing-permission behavior is already exercised by `Create`'s existing `projects:create` requirement; this task proves the endpoint-specific behavior (`mine` needs `projects:access`, `?userId=` additionally needs `projects:read`, and both return real paginated data), not the attribute's negative path again. Both provisioned tenant owners are expected to hold `projects:access` (and, being owners, likely `projects:read` too) once `PermissionSeeder.cs` seeds `projects:access` per the Task 7 prerequisite note — no fixture change is needed here beyond that seed existing.

- [ ] **Step 1: Add Edit tests**

Add inside the `CreateProjectEndpointTests` class, after `Create_ThenSecondTenantCannotSeeTheProjectRow_TenantIsolationHolds`:

```csharp
    [Fact]
    public async Task Edit_ValidRequest_UpdatesProjectAndCascadesDefaultObjective()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Edit Target", "EDT1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantA, projectId, "Edit Target Renamed", "EDT1");
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK, await editResponse.Content.ReadAsStringAsync());

        var editJson = await ReadJsonAsync(editResponse);
        editJson.GetProperty("name").GetString().Should().Be("Edit Target Renamed");

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        (await ReadJsonAsync(getResponse)).GetProperty("name").GetString().Should().Be("Edit Target Renamed");
    }

    [Fact]
    public async Task Edit_IdentifierChangeAttempted_Returns400()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Immutable Id Target", "IMM1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantA, projectId, "Immutable Id Target", "CHANGED");

        editResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_CrossTenantProjectId_Returns404()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Cross Tenant Edit Target", "CTE1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var editResponse = await SendEditProjectAsync(_tenantB, projectId, "Should Not Apply", "CTE1");

        editResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant B must not be able to see or edit tenant A's project - RLS + EF global filter scoping");
    }
```

- [ ] **Step 2: Add Delete tests**

```csharp
    [Fact]
    public async Task Delete_ByLead_SoftDeletesAndExcludesFromGetById()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Delete Target", "DEL1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var deleteResponse = await SendDeleteProjectAsync(_tenantA, projectId);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "a soft-deleted project must not be viewable via GetById");
    }

    [Fact]
    public async Task Delete_AlreadyDeleted_Returns409()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Double Delete Target", "DBL1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var first = await SendDeleteProjectAsync(_tenantA, projectId);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await SendDeleteProjectAsync(_tenantA, projectId);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
```

- [ ] **Step 3: Add GetById test**

```csharp
    [Fact]
    public async Task GetById_OwningLead_ReturnsProjectWithIsLeadTrue()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "GetById Target", "GET1");
        var projectId = (await ReadJsonAsync(created)).GetProperty("project").GetProperty("id").GetGuid();

        var getResponse = await SendGetProjectAsync(_tenantA, projectId);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadJsonAsync(getResponse);
        json.GetProperty("isLead").GetBoolean().Should().BeTrue();
    }
```

- [ ] **Step 4: Add List tests**

```csharp
    [Fact]
    public async Task ListMine_ReturnsOnlyCallersOwnProjects_RequiresOnlyBaseModuleAccess()
    {
        await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Mine List Target", "MIN1");

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/projects/mine?pageSize=50"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("identifier").GetString() == "MIN1").Should().BeTrue();
    }

    [Fact]
    public async Task ListByUser_RequiresProjectsReadPermission_OwnerHasItAndSucceeds()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "ByUser List Target", "BYU1");
        var ownerUserId = (await ReadJsonAsync(created)).GetProperty("creatorMembership").GetProperty("userId").GetGuid();

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, $"/api/v1/work/projects?userId={ownerUserId}&pageSize=50"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(response);
        json.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("identifier").GetString() == "BYU1").Should().BeTrue();
    }

    [Fact]
    public async Task ListForMember_MultiObjectiveMembership_DoesNotDuplicateProjectRow()
    {
        var created = await SendCreateProjectAsync(_tenantA, _tenantACategoryId, "Dedup List Target", "DUP2");
        var createdJson = await ReadJsonAsync(created);
        var projectId = createdJson.GetProperty("project").GetProperty("id").GetGuid();
        var ownerUserId = createdJson.GetProperty("creatorMembership").GetProperty("userId").GetGuid();
        var defaultObjectiveId = createdJson.GetProperty("defaultObjective").GetProperty("id").GetGuid();

        // No sub-Objective creation endpoint exists yet (Objective CRUD is a later phase - see
        // next-plan/Project Management.md) - seed a second Objective + a second membership row
        // for the SAME project + SAME user directly, exactly as ListForMemberAsync's DISTINCT
        // must handle: project_members' uniqueness is (tenant_id, project_id, objective_id,
        // user_id), so this is a legitimate second row, not a data error.
        await SeedSecondMembershipViaExtraObjectiveAsync(_tenantA.TenantId, projectId, ownerUserId, defaultObjectiveId);

        var response = await _client.SendAsync(BuildGetRequest(_tenantA, "/api/v1/work/projects/mine?pageSize=50"));
        var json = await ReadJsonAsync(response);

        json.GetProperty("items").EnumerateArray().Count(p => p.GetProperty("id").GetGuid() == projectId).Should().Be(1,
            "a user with two active memberships in the same project (via two Objectives) must see that project exactly once");
    }
```

- [ ] **Step 5: Add the shared HTTP + seeding helpers used above**

Add near the existing `SendCreateProjectAsync`/`SeedProjectCategoryAsync` helpers:

```csharp
    private async Task<HttpResponseMessage> SendEditProjectAsync(TenantSession session, Guid projectId, string name, string? identifier)
    {
        var body = new
        {
            name,
            description = "edited description",
            categoryId = session == _tenantA ? _tenantACategoryId : _tenantBCategoryId,
            startDate = "2026-01-01",
            targetDate = "2026-08-01",
            color = "#123456",
            actualHours = 5,
            identifier
        };

        return await SendJsonAsync(HttpMethod.Put, session.Host, $"/api/v1/work/projects/{projectId}", body,
            cookie: session.SessionCookie, csrfToken: session.CsrfHeader);
    }

    private async Task<HttpResponseMessage> SendDeleteProjectAsync(TenantSession session, Guid projectId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/work/projects/{projectId}");
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendGetProjectAsync(TenantSession session, Guid projectId)
        => await _client.SendAsync(BuildGetRequest(session, $"/api/v1/work/projects/{projectId}"));

    private HttpRequestMessage BuildGetRequest(TenantSession session, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = session.Host;
        request.Headers.Add("Cookie", session.SessionCookie);
        request.Headers.Add("X-CSRF-Token", session.CsrfHeader);
        return request;
    }

    private async Task SeedSecondMembershipViaExtraObjectiveAsync(Guid tenantId, Guid projectId, Guid userId, Guid defaultObjectiveId)
    {
        using var scope = _factory.Services.CreateScope();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenantId, tenantId.ToString(), TenantStatus.Active, null));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var employee = await db.Employees.SingleAsync(e => e.TenantId == tenantId && e.UserId == userId);

        var subObjective = new ONEVO.Domain.Features.WorkManagement.Objectives.Entities.Objective
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ParentObjectiveId = defaultObjectiveId,
            IsDefault = false, Title = "Sub Objective", OwnerId = userId, IsActive = true,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Objectives.Add(subObjective);

        db.ProjectMembers.Add(new ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMember
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = subObjective.Id,
            UserId = userId, EmployeeId = employee.Id,
            MembershipSource = ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMembershipSources.ObjectiveInvitation,
            IsActive = true, JoinedAt = DateTimeOffset.UtcNow, CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }
```

- [ ] **Step 6: Run the integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter CreateProjectEndpointTests`
Expected: all `[Fact]`s in the class pass (the 3 pre-existing Create tests plus the 8 new ones added here). Requires Docker running locally (Testcontainers), same precondition as the existing suite.

- [ ] **Step 7: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs
git commit -m "test(work-management): add HTTP integration tests for Edit/Delete/GetById/List"
```

---

### Task 9: `docs/postman-request/` docs for the four new endpoints

**Files:**
- Create: `docs/postman-request/Work Management/Edit Project.md`
- Create: `docs/postman-request/Work Management/Delete Project.md`
- Create: `docs/postman-request/Work Management/Get Project.md`
- Create: `docs/postman-request/Work Management/List Projects.md`

**Interfaces:**
- Consumes: nothing code-facing — this is documentation required by `docs/superpowers/rules/PROCESS_RULES.md` rule 6 ("Every finished API endpoint gets a plain-Markdown doc under `docs/postman-request/`"), same required sections and format as the existing `Work Management/Create Project.md`: method+route, auth/permission/idempotency line, description, request body example, response body example, error-status table, Source section.

- [ ] **Step 1: `Edit Project.md`**

```markdown
# Edit Project

**PUT** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access`
**Idempotent:** No `Idempotency-Key` support — a plain last-write-wins update (no optimistic concurrency token; see Global Constraints in the implementation plan for why).

## Description

Updates a Project's editable fields and cascades the same `name`/`description`/`startDate`/`targetDate` onto its Default Objective, in one transaction. `identifier` is immutable — if the request body includes one that differs from the project's current value, the request is rejected with `400`. Only the project's lead may edit it — matches Delete's existing lead-only rule (a Project is the tree's root node; only its own Head has unrestricted control over it, per `docs/superpowers/specs/2026-08-04-work-management-milestone-hierarchy-design.md` §4).

## Request

Content type: `application/json`.

```json
{
  "name": "Website Revamp v2",
  "description": "Rebuild the marketing site. (optional)",
  "categoryId": "guid — existing, active, tenant-owned project category",
  "startDate": "2026-08-01",
  "targetDate": "2027-01-01",
  "color": "#2563EB (optional, <= 20 chars)",
  "actualHours": "12 (optional, >= 0)",
  "identifier": "WEB (optional — only send if you want the immutability check to run; omit to skip it entirely)"
}
```

## Response

`200 OK`

```json
{
  "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "description": "string|null",
  "leadId": "guid", "startDate": "date", "targetDate": "date", "color": "string|null",
  "actualHours": "decimal|null", "allocatedHours": "decimal", "completedHours": "decimal",
  "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null", "isLead": true
}
```

## Errors

| Status | Cause |
|---|---|
| `400` | Validation failure (dates, `color` length), or the request tried to change `identifier` |
| `403` | Caller lacks `projects:access`, or has it but is not the project lead |
| `404` | Project doesn't exist in tenant, or `categoryId` invalid/inactive/not tenant-owned |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Edit`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/EditProject/EditProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
```

- [ ] **Step 2: `Delete Project.md`**

```markdown
# Delete Project

**DELETE** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:access` **and** caller must be the project's `leadId`.
**Idempotent:** No — a second call against an already-deleted project returns `409`, not a silent `204`.

## Description

Soft-deletes a Project (`is_active = false`, `updated_at` bumped). No cascade — `objectives`/`project_members`/`release_calendar`/etc. rows are untouched and keep their own independent lifecycle.

## Request

No body.

## Response

`204 No Content`

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access`, or has it but is not the project lead |
| `404` | Project doesn't exist in tenant |
| `409` | Project is already soft-deleted |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`Delete`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/DeleteProject/DeleteProjectCommandHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
```

- [ ] **Step 3: `Get Project.md`**

```markdown
# Get Project

**GET** `/api/v1/work/projects/{id}`

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.
**Permission:** `projects:read` **OR** an active `project_members` row for this project — checked in this order by the handler, not by `[RequirePermission]` (which would hard-block members lacking the tenant-wide permission).

## Description

Gets a single Project. A soft-deleted project (`is_active = false`) is treated as not found. `isLead` is always computed directly as `project.leadId == callerId`, independent of which access path (permission vs. membership) was used.

## Response

`200 OK`

```json
{
  "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "description": "string|null",
  "leadId": "guid", "startDate": "date", "targetDate": "date", "color": "string|null",
  "actualHours": "decimal|null", "allocatedHours": "decimal", "completedHours": "decimal",
  "isActive": true, "createdAt": "datetime", "updatedAt": "datetime|null", "isLead": true
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller has neither `projects:read` nor an active membership row for this project |
| `404` | Project doesn't exist in tenant, or exists but `is_active = false` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`GetById`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/GetProjectById/GetProjectByIdQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
```

- [ ] **Step 4: `List Projects.md`**

```markdown
# List Projects

**GET** `/api/v1/work/projects/mine` — caller's own projects. **Permission:** `projects:access` (the module-wide base gate — every Work Management endpoint requires this).
**GET** `/api/v1/work/projects?userId={userId}` — any given user's projects. **Permission:** `projects:read` (the separate "view others" grant, admin/company-owner path).

**Auth:** Tenant session cookie (`onevo_session`) + CSRF header. Policy: `TenantPolicy`.

## Description

Both routes return the target user's active `project_members` rows joined to active `projects`, deduplicated on `project_id` (a user can have more than one active membership on the same project via different Objectives). Query params: `pageNumber` (default 1), `pageSize` (default 20, capped 100), `sortBy` (`name` | `startDate` | `targetDate`, default sorts by creation date), `sortDirection` (`asc` | `desc`, default `asc`).

If `userId` doesn't resolve to any user with active memberships in the tenant, the response is an empty page (`200 OK`, `items: []`) — list semantics, not `404`.

## Response

`200 OK`

```json
{
  "items": [
    { "id": "guid", "name": "string", "identifier": "string", "categoryId": "guid", "leadId": "guid",
      "startDate": "date", "targetDate": "date", "color": "string|null", "isActive": true,
      "allocatedHours": "decimal", "completedHours": "decimal", "isLead": true }
  ],
  "pageNumber": 1, "pageSize": 20, "totalCount": 3, "totalPages": 1, "hasNext": false, "hasPrevious": false
}
```

## Errors

| Status | Cause |
|---|---|
| `403` | Caller lacks `projects:access` (either route), or (`?userId=` route only) lacks `projects:read` |

## Source

Controller: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs` (`ListMine`, `ListByUser`)
Handler: `src/ONEVO.Application/Features/WorkManagement/Projects/Queries/ListProjects/ListProjectsQueryHandler.cs`
Plan: `docs/superpowers/plans/2026-08-04-work-management-projects-edit-delete-view.md`
```

- [ ] **Step 5: Commit**

```bash
git add "docs/postman-request/Work Management/Edit Project.md" "docs/postman-request/Work Management/Delete Project.md" "docs/postman-request/Work Management/Get Project.md" "docs/postman-request/Work Management/List Projects.md"
git commit -m "docs(work-management): add postman-request docs for Edit/Delete/GetById/List Projects"
```

---

## Self-review

**Spec coverage** (against `docs/superpowers/specs/2026-08-04-work-management-projects-edit-delete-view-design.md`):
- §1 Endpoints table (5 routes, auth/permission column) → Task 7.
- §Endpoint 3 authorization detail (permission-or-membership, `isLead` computation) → Task 5.
- §2 Edit Project (editable fields, immutable identifier, Default Objective cascade, validation, no concurrency token, response shape, lead-only ownership check added 2026-08-04 per the milestone-hierarchy design's root-node rule) → Tasks 2, 3.
- §3 Delete Project (soft delete, lead-only, 409-on-already-deleted, 204) → Tasks 2, 4.
- §4/5 List Projects (mine + by-user, `PagedRequest`/`PagedResult<T>`, DISTINCT-on-project_id, field lists, `mine` requires only `projects:access` vs `?userId=` additionally requires `projects:read` — permission model revised 2026-08-04, see the "Permission model updated" Global Constraint above) → Tasks 1, 2, 6, 7.
- §5 Response DTOs/ViewModels (`ProjectDetailResponse`/`ProjectListItemResponse`, API-layer ViewModels, `ProjectViewModelMapper` extension) → Task 2.
- §6 Error handling summary → covered across Tasks 3-6 handler logic and Task 8 tests.
- §7 Testing approach (xUnit+Testcontainers HTTP, restricted-role RLS, handler/validator unit tests) → Tasks 3-6 (unit), Task 8 (integration), with the one explicitly-scoped-out sub-case documented plainly in Task 8 rather than silently dropped.
- §8/§9 Deferred work (Milestone-in-charge, xmin, lifecycle/approval/progress) → explicitly out of scope for this plan too; nothing here touches them.

**Placeholder scan:** no "TBD"/"similar to Task N"/unshown code — every step has runnable code or an exact `dotnet` command.

**Type consistency:** `EditProjectCommand`/`DeleteProjectCommand`/`GetProjectByIdQuery`/`ListProjectsQuery` field names and types match their handler constructors and their controller call sites exactly across Tasks 3-7; `ProjectMapper.ToDetail`/`ToListItem` signatures match `ProjectDetailResponse`/`ProjectListItemResponse` record shapes and are called identically from all three consuming handlers (Tasks 3, 5, 6); `IProjectRepository`/`IObjectiveRepository`/`IProjectMemberRepository` new members declared in Task 1 match every call site in Tasks 3-6 exactly (same method names, parameter order, nullability).
