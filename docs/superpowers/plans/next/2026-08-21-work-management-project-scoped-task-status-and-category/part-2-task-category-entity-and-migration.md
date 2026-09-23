# Part 2: New `TaskCategory` entity, migration, and default seeding

**Read first:** `docs/superpowers/specs/next/2026-08-21-work-management-project-scoped-task-status-and-category-design.md`
§4. Independent of Part 1 — can be done in either order relative to it, both are prerequisites for
Part 3.

**Scope guard:** Work Management module only.

## Goal

Add a per-Project `TaskCategory` entity, shaped and configured exactly like `TaskStatus` but simpler (no
`ObjectiveId`/`Visibility`/`RequiresApproval`/`ApproverId`/`MarksTaskComplete` — Category has no
per-Objective legacy to strip out, it's Project-scoped from day one). Seed 4 default rows
(`Task`/`Bug`/`Story`/`Feature`, matching today's `WorkTaskTypes` values) at Project-creation time, same
call site pattern as `DefaultTaskStatusTemplate`.

## Current state (verified by reading the reference files directly)

- `TaskStatus` entity + `TaskStatusConfiguration` (full content already quoted in Part 1's sibling design
  spec and this plan folder's Part 1) is the shape to mirror, minus the fields this entity doesn't need.
- `DefaultTaskStatusTemplate.BuildRows(tenantId, projectId, objectiveId, createdById, now)` is the
  seeding-helper pattern to mirror (static class, one `BuildRows` method returning a `List<T>`).
- `CreateProjectCommandHandler.cs:282-284` (after Part 1's Task 7 removes the second `AddRangeAsync`
  call) is the seeding call site — add one more `AddRangeAsync` call here for categories, same
  transaction, same pattern.
- Migrations in this repo are generated with:
  `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
  (needs a `ConnectionStrings__MigrationConnection` env var set — see
  `docs/superpowers/plans/2026-08-06-invite-platform-manager-backend.md` for a worked example of this
  exact command in this repo).
- `EfTaskStatusRepository` (full content in Part 1) is the repository-implementation pattern to mirror.

## Files to create

- `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCategory.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskCategoryTemplate.cs`
- `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCategoryRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCategoryRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCategoryConfiguration.cs`
- New EF migration (name TBD by the actual `dotnet ef migrations add` run, e.g.
  `AddTaskCategories` — the tool timestamps the filename automatically, don't hand-write the prefix)

## Files to modify

- `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add
  `public DbSet<TaskCategory> TaskCategories => Set<TaskCategory>();` near the existing
  `public DbSet<TaskStatusEntity> TaskStatuses => Set<TaskStatusEntity>();` line (`:241`).
- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
  — add category seeding, and add the new repository dependency to the constructor.
- Dependency injection registration for the new repository — `grep -rn "ITaskStatusRepository,
  EfTaskStatusRepository" src/ONEVO.Api/` (or wherever this project's DI composition root registers
  repository interfaces) to find the exact registration call and add the matching line for
  `ITaskCategoryRepository`/`EfTaskCategoryRepository`.

## Task 1: `TaskCategory` entity

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public class TaskCategory : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
```

## Task 2: `TaskCategoryConfiguration`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCategoryConfiguration : IEntityTypeConfiguration<TaskCategory>
{
    public void Configure(EntityTypeBuilder<TaskCategory> builder)
    {
        builder.ToTable("task_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.DisplayOrder })
            .HasDatabaseName("ix_task_categories_tenant_id_project_id_display_order");

        builder.HasIndex(c => new { c.TenantId, c.ProjectId, c.Name })
            .IsUnique()
            .HasDatabaseName("ix_task_categories_one_name_per_project");
    }
}
```

Check whether `IEntityTypeConfiguration` classes in this project are auto-discovered
(`ApplyConfigurationsFromAssembly`) or must be registered individually in `OnModelCreating` — grep
`ApplyConfigurationsFromAssembly\|TaskStatusConfiguration()` in `ApplicationDbContext.cs` to confirm
which, and follow the same pattern for `TaskCategoryConfiguration`.

## Task 3: `ITaskCategoryRepository` + `EfTaskCategoryRepository`

Mirror `ITaskStatusRepository`/`EfTaskStatusRepository` exactly, minus the two methods that don't apply
(no `GetByObjectiveIdAsync` equivalent — Category has no per-Objective concept at all, ever):

```csharp
public interface ITaskCategoryRepository
{
    Task AddAsync(TaskCategory category, CancellationToken ct = default);
    Task AddRangeAsync(IReadOnlyList<TaskCategory> categories, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCategory>> GetByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<TaskCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    void Update(TaskCategory category);
    void Remove(TaskCategory category);
}
```

`EfTaskCategoryRepository` implementation follows `EfTaskStatusRepository`'s exact shape (`AsNoTracking`
for the list query, tracked for the by-id query, explicit `Update`/`Remove`).

## Task 4: `DefaultTaskCategoryTemplate`

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskCategoryTemplate
{
    public static List<TaskCategory> BuildRows(Guid tenantId, Guid projectId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskCategory>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Task", DisplayOrder = 0, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Bug", DisplayOrder = 1, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Story", DisplayOrder = 2, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Name = "Feature", DisplayOrder = 3, CreatedById = createdById, CreatedAt = now }
        };
    }
}
```

Names match today's `WorkTaskTypes` constants exactly (`Task`/`Bug`/`Story`/`Feature`) — Part 3's
backfill matches on these exact strings, so don't change the casing/wording here without also updating
Part 3.

## Task 5: Wire seeding into `CreateProjectCommandHandler`

Add `ITaskCategoryRepository` to the constructor. After the (now-single) `_taskStatuses.AddRangeAsync`
call from Part 1's Task 7, add:
```csharp
await _taskCategories.AddRangeAsync(
    DefaultTaskCategoryTemplate.BuildRows(tenantId, project.Id, userId, now), ct);
```

Tests: add an assertion to `CreateProjectCommandHandlerTests` that 4 `TaskCategory` rows exist for the
new project with the expected names/order after creation.

## Task 6: `DbSet` + DI registration + migration

1. Add the `DbSet<TaskCategory>` line to `ApplicationDbContext.cs` (see "Files to modify" above).
2. Register `ITaskCategoryRepository → EfTaskCategoryRepository` in DI, same place/pattern as
   `ITaskStatusRepository`.
3. Generate the migration:
   ```bash
   dotnet ef migrations add AddTaskCategories --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
   ```
   Read the generated migration file before committing it — confirm it only creates the new
   `task_categories` table (indexes included) and touches nothing else. If EF also picked up unrelated
   pending model changes from other in-progress work, stop and flag this rather than committing an
   over-broad migration.
4. Apply the migration locally and confirm it runs clean (this project's usual local-migration-apply
   command — check `docs/superpowers/rules/` or a recent migration-adding plan for the exact command if
   not obvious from `dotnet ef database update` conventions already used elsewhere in this repo).

## Definition of done

- Tasks 1-6 committed (entity+config+repo as one commit, `DefaultTaskCategoryTemplate` +
  `CreateProjectCommandHandler` wiring as a second, migration as a third — or combine as reads naturally,
  this Part is small enough that grouping is a judgment call).
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green.
- `dotnet build` compiles clean.
- The migration applies cleanly against a local database and only touches `task_categories`.
