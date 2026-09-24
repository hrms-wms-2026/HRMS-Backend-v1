# ClickUp-style Task Status Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Group task statuses into fixed Not Started/Active/Done categories with per-status color, auto-move a task out of Not Started on clock-in, and redesign the status editor and board columns to match.

**Architecture:** Add `Category`/`Color` columns to the existing `TaskStatus` entity (backend, `HRMS-Backend-v1`), thread them through Create/Edit/Delete/Reorder commands and their DTOs, change `ClockInTaskCommandHandler` to auto-move a task off a Not Started status, then rebuild the Angular status editor and board column header to consume the new fields (frontend, `Hrms--Web-application---front-end---v1`). Backend tasks land first since the frontend depends on the new API contract.

**Tech Stack:** .NET 8 / EF Core (Npgsql, PostgreSQL with FORCE ROW LEVEL SECURITY) / MediatR / FluentValidation / xUnit + Moq for the backend; Angular 21 standalone components / signals / Angular CDK drag-drop / Vitest for the frontend.

**Spec:** `docs/superpowers/specs/2026-09-18-task-status-groups-design.md` (present in both repos' worktrees)

## Global Constraints

- Category is one of exactly three fixed string constants: `not_started`, `active`, `done` (mirrors the existing `TaskStatusVisibilities` pattern — no enum, no "Space"/template concept).
- `MarksTaskComplete` becomes fully derived server-side (`true` iff `Category == done`) and is removed from every client-writable DTO (Create/Edit/Reorder requests) — single writer, no drift.
- The Done category is capped at exactly one row per `(TenantId, ProjectId, ObjectiveId)` scope (rename/recolor/toggle-private only). Active must always have at least one row. Not Started has no minimum.
- `task_statuses` is a FORCE ROW LEVEL SECURITY table. Any raw SQL migration touching it in bulk (not through EF's normal per-request `SET app.tenant_context_mode`) MUST start with `SET LOCAL app.tenant_context_mode = 'admin';` inside the same `migrationBuilder.Sql(...)` block, or every statement silently matches zero rows. See `20260916092952_BackfillTrayEmployeeIdentity.cs` for the reference pattern.
- Only the project's default-objective "effective manager" (owner, or an ancestor-objective owner) may create/edit/delete/reorder task statuses — this is existing, unchanged behavior (`IMilestoneMembershipCoordinator.IsEffectiveManagerAsync`), not something this feature loosens or tightens.
- Colors are 6-digit hex strings, `^#[0-9A-Fa-f]{6}$`, stored uppercase-or-lowercase as given (no normalization needed).
- Default colors: Not Started `#94A3B8`, Active `#2563EB` (Done's default is `#16A34A`).
- All new backend tests follow this codebase's established convention exactly: inline `new TaskStatusEntity { ... }` object initializers (no builder/factory), `Mock<T>` from Moq per dependency, a private `Build`/`Arrange...Handler` helper returning a tuple, `[Fact]` methods calling `handler.Handle(command, CancellationToken.None)`. Do not introduce a new builder pattern.
- All new frontend tests follow the existing convention exactly: Vitest (`vi.fn().mockReturnValue(of(...))`, never `mockResolvedValue`, since `TaskApiService` returns `Observable`), `TestBed.configureTestingModule` with a partial `useValue` mock, `fixture.componentRef.setInput(...)` for signal inputs, DOM assertions via `fixture.nativeElement.querySelectorAll(...)`.

---

## Backend Tasks (repo: `HRMS-Backend-v1`, worktree `C:\onevoNew\HRMS-Backend-v1\.worktrees\task-status-groups`)

### Task 1: Domain model — Category/Color on TaskStatus, seed template, migration

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskStatusTemplate.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs`
- Create: EF migration (generated, then hand-edited) under `src/ONEVO.Infrastructure/Migrations/`

**Interfaces:**
- Produces: `TaskStatusCategories.NotStarted`/`.Active`/`.Done` (string constants), `TaskStatus.Category` (string), `TaskStatus.Color` (string) — every later backend task reads/writes these.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DefaultTaskStatusTemplateTests
{
    [Fact]
    public void BuildRows_AssignsExactlyOneNotStartedOneDoneAndAtLeastOneActive()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var createdById = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var rows = DefaultTaskStatusTemplate.BuildRows(tenantId, projectId, null, createdById, now);

        Assert.Single(rows, r => r.Category == TaskStatusCategories.NotStarted);
        Assert.True(rows.Count(r => r.Category == TaskStatusCategories.Active) >= 1);
        Assert.Single(rows, r => r.Category == TaskStatusCategories.Done);
        Assert.Single(rows, r => r.MarksTaskComplete);
        Assert.All(rows, r => Assert.Matches("^#[0-9A-Fa-f]{6}$", r.Color));
    }

    [Fact]
    public void BuildRows_DoneRowMarksTaskCompleteMatchesCategory()
    {
        var rows = DefaultTaskStatusTemplate.BuildRows(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var done = rows.Single(r => r.Category == TaskStatusCategories.Done);
        Assert.True(done.MarksTaskComplete);
        Assert.Equal(TaskStatusVisibilities.Private, done.Visibility);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter DefaultTaskStatusTemplateTests`
Expected: FAIL to compile — `TaskStatusCategories` does not exist, `TaskStatus.Category`/`.Color` do not exist.

- [ ] **Step 3: Add `TaskStatusCategories` and the new columns to the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskStatusVisibilities
{
    public const string Public = "public";
    public const string Private = "private";
}

public static class TaskStatusCategories
{
    public const string NotStarted = "not_started";
    public const string Active = "active";
    public const string Done = "done";
}

/// <summary>
/// Configurable task-status definitions. A row with ObjectiveId == null is a Project-level
/// template; a row with ObjectiveId set is that Objective's own independently-customizable copy.
/// See docs/superpowers/project_ core/phase1-table-inventory.md "task_statuses".
/// </summary>
public class TaskStatus : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ObjectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool RequiresApproval { get; set; }
    public Guid? ApproverId { get; set; }
    public bool MarksTaskComplete { get; set; }
    public string Visibility { get; set; } = TaskStatusVisibilities.Public;
    public string Category { get; set; } = TaskStatusCategories.NotStarted;
    public string Color { get; set; } = "#94A3B8";
}
```

- [ ] **Step 4: Configure the new columns**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusConfiguration : IEntityTypeConfiguration<TaskStatusEntity>
{
    public void Configure(EntityTypeBuilder<TaskStatusEntity> builder)
    {
        builder.ToTable("task_statuses");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Visibility).HasMaxLength(20).IsRequired().HasDefaultValue(TaskStatusVisibilities.Public);
        builder.Property(s => s.Category).HasMaxLength(20).IsRequired().HasDefaultValue(TaskStatusCategories.NotStarted);
        builder.Property(s => s.Color).HasMaxLength(7).IsRequired().HasDefaultValue("#94A3B8");

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.DisplayOrder })
            .HasDatabaseName("ix_task_statuses_tenant_id_project_id_objective_id_display_order");

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.Name })
            .IsUnique()
            .HasDatabaseName("ix_task_statuses_one_name_per_scope");
    }
}
```

- [ ] **Step 5: Update the seed template**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskStatusTemplate.cs
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public static class DefaultTaskStatusTemplate
{
    public static List<TaskStatusEntity> BuildRows(
        Guid tenantId, Guid projectId, Guid? objectiveId, Guid createdById, DateTimeOffset now)
    {
        return new List<TaskStatusEntity>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, Color = "#94A3B8", Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Color = "#2563EB", Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Review", DisplayOrder = 2, Category = TaskStatusCategories.Active, Color = "#7C3AED", Visibility = TaskStatusVisibilities.Public, CreatedById = createdById, CreatedAt = now },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, ObjectiveId = objectiveId, Name = "Done", DisplayOrder = 3, Category = TaskStatusCategories.Done, Color = "#16A34A", MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedById = createdById, CreatedAt = now }
        };
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter DefaultTaskStatusTemplateTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Generate the EF migration**

Run from repo root:
```bash
dotnet ef migrations add AddTaskStatusCategoryAndColor --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
This generates `AddColumn` calls for `category`/`color` as **nullable** (since the entity now has non-null defaults but existing rows have no value yet — EF will still see them as required by the C# type; if the generated migration marks them `nullable: false` with a `defaultValue`, that's fine for *new* rows but will backfill existing rows with the literal default string via a normal `ALTER TABLE ... ADD COLUMN ... DEFAULT '...'`, which is NOT what we want — we need per-scope backfill logic, not one flat default). **Before editing further, open the generated file and change both `AddColumn<string>` calls to `nullable: true` with no `defaultValue`, and remove the `AlterColumn` EF might have also generated** — we'll add our own `AlterColumn` to `nullable: false` after the data backfill, further down in the same `Up()` method.

- [ ] **Step 8: Verify the exact current column list before writing the backfill SQL**

Run: `psql <connection> -c "\d task_statuses"` (or read `src/ONEVO.Infrastructure/Migrations/20260816182551_AddTaskFoundationTables.cs` lines 19-42 plus `20260817000001_AddTaskStatusVisibility.cs` for the full authoritative column list). Confirm it is exactly: `id, project_id, objective_id, name, display_order, requires_approval, approver_id, marks_task_complete, tenant_id, created_at, updated_at, created_by_id, is_deleted, deleted_at, visibility` before the two new columns. If it differs, adjust the INSERT statement in Step 9 accordingly.

- [ ] **Step 9: Hand-edit the migration's `Up()` to insert the backfill + repair SQL between the nullable `AddColumn` calls and the `NOT NULL` `AlterColumn` calls**

```csharp
// src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskStatusCategoryAndColor.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Adds Category (not_started/active/done) and Color to task_statuses, then backfills every
    /// existing row and repairs any (tenant, project, objective) scope left without an Active
    /// category row - see docs/superpowers/specs/2026-09-18-task-status-groups-design.md for the
    /// heuristic. SET LOCAL app.tenant_context_mode = 'admin' is required: task_statuses is FORCE
    /// ROW LEVEL SECURITY and migrations run as onevo_migrator (NOSUPERUSER NOBYPASSRLS) - without
    /// it every statement below silently matches zero rows (see BackfillTrayEmployeeIdentity's
    /// migration comment for the empirical verification behind this requirement).
    /// </summary>
    public partial class AddTaskStatusCategoryAndColor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "task_statuses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "task_statuses",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                -- Done: the row already flagged complete in its scope.
                UPDATE task_statuses d
                SET category = 'done', color = '#16A34A'
                WHERE d.marks_task_complete = true;

                -- Fallback Done: a scope with no marks_task_complete row at all (pre-dates the
                -- "exactly one complete status" invariant enforced by ReorderTaskStatuses) - take
                -- the highest DisplayOrder row in that scope.
                UPDATE task_statuses d
                SET category = 'done', color = '#16A34A'
                WHERE d.category IS NULL
                  AND d.display_order = (
                      SELECT MAX(s.display_order) FROM task_statuses s
                      WHERE s.tenant_id = d.tenant_id AND s.project_id = d.project_id
                        AND s.objective_id IS NOT DISTINCT FROM d.objective_id
                  );

                -- Not Started: the lowest-DisplayOrder row remaining in each scope.
                UPDATE task_statuses n
                SET category = 'not_started', color = '#94A3B8'
                WHERE n.category IS NULL
                  AND n.display_order = (
                      SELECT MIN(s.display_order) FROM task_statuses s
                      WHERE s.tenant_id = n.tenant_id AND s.project_id = n.project_id
                        AND s.objective_id IS NOT DISTINCT FROM n.objective_id
                        AND s.category IS NULL
                  );

                -- Active: everything else remaining.
                UPDATE task_statuses a
                SET category = 'active', color = '#2563EB'
                WHERE a.category IS NULL;

                -- Repair pass: any scope left with zero Active rows (e.g. a project that only ever
                -- had 2 statuses) gets a synthetic "In Progress" Active row, so clock-in's "find
                -- first Active status" can never come up empty. Guarded against the unique
                -- (tenant_id, project_id, objective_id, name) index too, in case a row named
                -- "In Progress" already exists in that scope under a different category.
                INSERT INTO task_statuses (
                    id, tenant_id, project_id, objective_id, name, display_order,
                    requires_approval, approver_id, marks_task_complete, visibility,
                    category, color, created_at, created_by_id, is_deleted
                )
                SELECT
                    gen_random_uuid(), scope.tenant_id, scope.project_id, scope.objective_id,
                    'In Progress', scope.max_order + 1,
                    false, NULL, false, 'public', 'active', '#2563EB', now(),
                    (SELECT s.created_by_id FROM task_statuses s
                     WHERE s.tenant_id = scope.tenant_id AND s.project_id = scope.project_id
                       AND s.objective_id IS NOT DISTINCT FROM scope.objective_id
                     ORDER BY s.display_order LIMIT 1),
                    false
                FROM (
                    SELECT tenant_id, project_id, objective_id, MAX(display_order) AS max_order
                    FROM task_statuses
                    GROUP BY tenant_id, project_id, objective_id
                ) scope
                WHERE NOT EXISTS (
                    SELECT 1 FROM task_statuses s3
                    WHERE s3.tenant_id = scope.tenant_id AND s3.project_id = scope.project_id
                      AND s3.objective_id IS NOT DISTINCT FROM scope.objective_id
                      AND s3.category = 'active'
                )
                AND NOT EXISTS (
                    SELECT 1 FROM task_statuses s4
                    WHERE s4.tenant_id = scope.tenant_id AND s4.project_id = scope.project_id
                      AND s4.objective_id IS NOT DISTINCT FROM scope.objective_id
                      AND s4.name = 'In Progress'
                );
            """);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "task_statuses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "not_started",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "task_statuses",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#94A3B8",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "color", table: "task_statuses");
            migrationBuilder.DropColumn(name: "category", table: "task_statuses");
        }
    }
}
```

Also delete the paired `.Designer.cs` EF generated for you in Step 7 only if you changed the migration's structural shape enough to desync it — normally just re-run `dotnet ef migrations add` fresh if the hand-edit gets messy, then re-apply this same hand-edit to the newly generated pair.

- [ ] **Step 10: Apply and verify locally**

```bash
dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: migration applies with no errors. Then verify with `psql <connection> -c "SELECT category, count(*) FROM task_statuses GROUP BY category;"` — every existing row has a non-null category, and `psql <connection> -c "SELECT tenant_id, project_id, objective_id FROM task_statuses WHERE category='active' GROUP BY 1,2,3 HAVING count(*) = 0;"` returns zero rows is not directly expressible that way — instead run: `psql <connection> -c "SELECT tenant_id, project_id, objective_id FROM task_statuses GROUP BY 1,2,3 HAVING count(*) FILTER (WHERE category='active') = 0;"` and confirm it returns **zero rows** (proving the repair pass left no scope without an Active status).

- [ ] **Step 11: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs \
        src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs \
        src/ONEVO.Application/Features/WorkManagement/Tasks/Services/DefaultTaskStatusTemplate.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DefaultTaskStatusTemplateTests.cs \
        src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add Category/Color to TaskStatus with per-scope backfill"
```

### Task 2: Expose Category/Color on the read path (TaskStatusResponse, ViewModel, GetProjectTaskStatuses)

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskStatuses/GetProjectTaskStatusesQueryHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (the `TaskStatusViewModel` record only, in this task)
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs` (the `TaskStatusResponse` mapper only)
- Create or modify (check first): `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTaskStatusesQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusCategories`, `TaskStatus.Category`/`.Color` (Task 1).
- Produces: `TaskStatusResponse(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete, string Visibility, string Category, string Color)` — every remaining backend task (3-8) constructs or reads this record with this exact 9-argument shape.

- [ ] **Step 1: Write the failing test**

First check whether `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTaskStatusesQueryHandlerTests.cs` already exists. If it does, add the test below to it (matching its existing `Build`-helper style); if not, create it fresh:

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTaskStatusesQueryHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetProjectTaskStatusesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (GetProjectTaskStatusesQueryHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(List<TaskStatusEntity> rows)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        var handler = new GetProjectTaskStatusesQueryHandler(currentUser.Object, projects.Object, statuses.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_ReturnsCategoryAndColorForEachStatus()
    {
        var row = new TaskStatusEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process",
            DisplayOrder = 1, Category = TaskStatusCategories.Active, Color = "#2563EB",
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _) = Build(new List<TaskStatusEntity> { row });

        var result = await handler.Handle(new GetProjectTaskStatusesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = Assert.Single(result.Value!);
        Assert.Equal(TaskStatusCategories.Active, response.Category);
        Assert.Equal("#2563EB", response.Color);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectTaskStatusesQueryHandlerTests`
Expected: FAIL to compile — `TaskStatusResponse` has no `Category`/`Color` positional members yet.

- [ ] **Step 3: Add the fields to `TaskStatusResponse` and update the query handler's mapping**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusResponse(
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval,
    Guid? ApproverId, bool MarksTaskComplete, string Visibility,
    string Category, string Color);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskStatuses/GetProjectTaskStatusesQueryHandler.cs
// (only the mapping method changes; the rest of the file is unchanged)
    private static IReadOnlyList<TaskStatusResponse> ToResponses(IReadOnlyList<TaskStatusEntity> statuses)
        => statuses.OrderBy(s => s.DisplayOrder)
            .Select(s => new TaskStatusResponse(
                s.Id, s.Name, s.DisplayOrder, s.RequiresApproval,
                s.ApproverId, s.MarksTaskComplete, s.Visibility, s.Category, s.Color))
            .ToList();
```

- [ ] **Step 4: Update the API-layer ViewModel and mapper**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs
// (only this record changes in this task; everything else in the file is untouched here)
public sealed record TaskStatusViewModel(
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval,
    Guid? ApproverId, bool MarksTaskComplete, string Visibility,
    string Category, string Color);
```

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs
// (only this method changes in this task)
    public static TaskStatusViewModel ToViewModel(this TaskStatusResponse dto) => new(
        dto.Id, dto.Name, dto.DisplayOrder, dto.RequiresApproval,
        dto.ApproverId, dto.MarksTaskComplete, dto.Visibility, dto.Category, dto.Color);
```

Every other `new TaskStatusResponse(...)` call site elsewhere in the codebase (Create/Edit/Reorder handlers — Tasks 3-6 below) will now fail to compile until those tasks add the two new positional arguments. That is expected and is fixed in each of those tasks; do not touch those handlers in this task.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter GetProjectTaskStatusesQueryHandlerTests`
Expected: PASS. (The rest of the solution will not build yet — that's expected until Tasks 3-6 land; do not attempt a full `dotnet build` gate at the end of this task.)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs \
        src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetProjectTaskStatuses/GetProjectTaskStatusesQueryHandler.cs \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetProjectTaskStatusesQueryHandlerTests.cs
git commit -m "feat: expose Category/Color through TaskStatusResponse and its ViewModel"
```

### Task 3: CreateTaskStatusCommand — Category/Color, derive MarksTaskComplete, reject a second Done

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`CreateTaskStatusRequest` only)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`CreateStatus` action only, ~line 232-241)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusResponse` 9-arg shape (Task 2), `TaskStatusCategories` (Task 1).
- Produces: `CreateTaskStatusCommand(Guid ProjectId, string Name, int DisplayOrder, string Visibility, string Category, string Color, bool RequiresApproval, Guid? ApproverId)` — no other task constructs this directly, but the shape (no `MarksTaskComplete` param) is the pattern Tasks 4 and 6 mirror for their own commands.

- [ ] **Step 1: Update the failing tests first**

Replace the whole file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskStatusCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null, List<TaskStatusEntity>? existing = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new List<TaskStatusEntity>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new CreateTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_Owner_CreatesActiveStatus()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value!.Name);
        Assert.False(result.Value.MarksTaskComplete);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s =>
            s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null &&
            s.Category == TaskStatusCategories.Active && s.Color == "#2563EB" && !s.MarksTaskComplete), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoneCategory_DerivesMarksTaskCompleteTrue()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Shipped", 5, TaskStatusVisibilities.Private, TaskStatusCategories.Done, "#16A34A", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.MarksTaskComplete);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.MarksTaskComplete), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoneCategoryWhenDoneAlreadyExists_ReturnsConflict()
    {
        var existingDone = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", Category = TaskStatusCategories.Done, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses) = Build(OwnerEmployeeId, existing: new List<TaskStatusEntity> { existingDone });
        var command = new CreateTaskStatusCommand(ProjectId, "Also Done", 5, TaskStatusVisibilities.Private, TaskStatusCategories.Done, "#16A34A", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_CreatesStatus()
    {
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CreateTaskStatusCommandHandlerTests`
Expected: FAIL to compile — `CreateTaskStatusCommand` still has the old 7-arg `MarksTaskComplete`-based shape.

- [ ] **Step 3: Update the command record**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public sealed record CreateTaskStatusCommand(
    Guid ProjectId, string Name, int DisplayOrder, string Visibility, string Category, string Color,
    bool RequiresApproval, Guid? ApproverId
) : IRequest<Result<TaskStatusResponse>>;
```

- [ ] **Step 4: Update the validator**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandValidator : AbstractValidator<CreateTaskStatusCommand>
{
    public CreateTaskStatusCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("Project is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
        RuleFor(x => x.Category).Must(c => c is TaskStatusCategories.NotStarted or TaskStatusCategories.Active or TaskStatusCategories.Done)
            .WithMessage("Category must be not_started, active, or done.");
        RuleFor(x => x.Color).Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Color must be a 6-digit hex code, e.g. #2563EB.");
    }
}
```

- [ ] **Step 5: Update the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/CreateTaskStatusCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;

public class CreateTaskStatusCommandHandler : IRequestHandler<CreateTaskStatusCommand, Result<TaskStatusResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public CreateTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<TaskStatusResponse>> Handle(CreateTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskStatusResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<TaskStatusResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<TaskStatusResponse>.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<TaskStatusResponse>.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result<TaskStatusResponse>.Forbidden("Only an owner or member of this project can create task statuses.");

        if (request.Category == TaskStatusCategories.Done)
        {
            var existing = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
            if (existing.Any(s => s.Category == TaskStatusCategories.Done))
                return Result<TaskStatusResponse>.Conflict("This project already has a Done status; edit or delete it first.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var status = new TaskStatusEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id, ObjectiveId = null,
                Name = request.Name.Trim(), DisplayOrder = request.DisplayOrder, Visibility = request.Visibility,
                Category = request.Category, Color = request.Color,
                MarksTaskComplete = request.Category == TaskStatusCategories.Done,
                RequiresApproval = request.RequiresApproval,
                ApproverId = request.ApproverId, CreatedById = _currentUser.UserId, CreatedAt = now
            };

            await _statuses.AddAsync(status, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<TaskStatusResponse>.Success(new TaskStatusResponse(
                status.Id, status.Name, status.DisplayOrder, status.RequiresApproval, status.ApproverId,
                status.MarksTaskComplete, status.Visibility, status.Category, status.Color));
        }, ct);
    }
}
```

- [ ] **Step 6: Update the API contract and controller call site**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs
// (only this record changes in this task)
public sealed record CreateTaskStatusRequest(
    string Name, int DisplayOrder, string Visibility, string Category, string Color,
    bool RequiresApproval, Guid? ApproverId);
```

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs (CreateStatus action, ~line 232-241)
    [HttpPost("projects/{projectId:guid}/task-statuses")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> CreateStatus(Guid projectId, [FromBody] CreateTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskStatusCommand(
            projectId, request.Name, request.DisplayOrder, request.Visibility, request.Category, request.Color,
            request.RequiresApproval, request.ApproverId), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CreateTaskStatusCommandHandlerTests`
Expected: PASS (5 tests).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskStatus/ \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs \
        src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskStatusCommandHandlerTests.cs
git commit -m "feat: CreateTaskStatus takes Category/Color, derives MarksTaskComplete, rejects a second Done"
```

### Task 4: EditTaskStatusCommand — Category/Color, guard the Done cap and Active minimum

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`EditTaskStatusRequest` only)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`EditStatus` action only, ~line 258-268)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusCategories` (Task 1).
- Produces: `EditTaskStatusCommand(Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, string Visibility, string Category, string Color)`.

- [ ] **Step 1: Update the failing tests first**

Replace the whole file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskStatusCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    private (EditTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses, TaskStatusEntity Status) Build(
        string statusCategory, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null,
        Guid? statusObjectiveId = null, List<TaskStatusEntity>? siblings = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var status = new TaskStatusEntity
        {
            Id = StatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = statusObjectiveId,
            Name = "In Progress", Category = statusCategory,
            MarksTaskComplete = statusCategory == TaskStatusCategories.Done,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var all = new List<TaskStatusEntity>(siblings ?? new List<TaskStatusEntity>()) { status };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(status);
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var handler = new EditTaskStatusCommandHandler(
            currentUser.Object, identity.Object, statuses.Object, objectives.Object, projects.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses, status);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesVisibilityAndColor()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s =>
            s.Visibility == TaskStatusVisibilities.Private && s.Color == "#7C3AED" && !s.MarksTaskComplete)), Times.Once);
    }

    [Fact]
    public async Task Handle_ChangeToDoneWhenAnotherDoneExists_ReturnsConflict()
    {
        var otherDone = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", Category = TaskStatusCategories.Done, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, siblings: new List<TaskStatusEntity> { otherDone });
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyDoneRowOutOfDone_ReturnsConflict()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Done);
        var command = new EditTaskStatusCommand(StatusId, "Done", 3, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#2563EB");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyActiveRowOutOfActive_ReturnsConflict()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active);
        var command = new EditTaskStatusCommand(StatusId, "To Do", 0, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveOneOfTwoActiveRowsToNotStarted_Succeeds()
    {
        var otherActive = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, siblings: new List<TaskStatusEntity> { otherActive });
        var command = new EditTaskStatusCommand(StatusId, "To Do", 0, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Category == TaskStatusCategories.NotStarted)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, callerEmployeeId: OtherEmployeeId);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StatusHasObjectiveId_ReturnsNotFound()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, statusObjectiveId: ObjectiveId);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter EditTaskStatusCommandHandlerTests`
Expected: FAIL to compile — `EditTaskStatusCommand` has no `Category`/`Color` parameters yet.

- [ ] **Step 3: Update the command record**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public sealed record EditTaskStatusCommand(
    Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId,
    string Visibility, string Category, string Color
) : IRequest<Result>;
```

- [ ] **Step 4: Update the validator**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public class EditTaskStatusCommandValidator : AbstractValidator<EditTaskStatusCommand>
{
    public EditTaskStatusCommandValidator()
    {
        RuleFor(x => x.StatusId).NotEqual(Guid.Empty).WithMessage("Task status is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithMessage("Name is required and must be 100 characters or fewer.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must not be negative.");
        RuleFor(x => x.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private)
            .WithMessage("Visibility must be public or private.");
        RuleFor(x => x.Category).Must(c => c is TaskStatusCategories.NotStarted or TaskStatusCategories.Active or TaskStatusCategories.Done)
            .WithMessage("Category must be not_started, active, or done.");
        RuleFor(x => x.Color).Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Color must be a 6-digit hex code, e.g. #2563EB.");
    }
}
```

- [ ] **Step 5: Update the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/EditTaskStatusCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;

public class EditTaskStatusCommandHandler : IRequestHandler<EditTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskStatusRepository _statuses;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public EditTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskStatusRepository statuses,
        IObjectiveRepository objectives, IProjectRepository projects, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _statuses = statuses;
        _objectives = objectives;
        _projects = projects;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(EditTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is not null)
            return Result.NotFound("Task status not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, status.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only an owner or member of this project can change task status configuration.");

        var siblings = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);

        if (request.Category == TaskStatusCategories.Done && status.Category != TaskStatusCategories.Done
            && siblings.Any(s => s.Id != status.Id && s.Category == TaskStatusCategories.Done))
            return Result.Conflict("This project already has a Done status; edit or delete it first.");

        if (status.Category == TaskStatusCategories.Done && request.Category != TaskStatusCategories.Done)
            return Result.Conflict("A project must always have exactly one Done status.");

        if (status.Category == TaskStatusCategories.Active && request.Category != TaskStatusCategories.Active
            && siblings.Count(s => s.Category == TaskStatusCategories.Active) <= 1)
            return Result.Conflict("A project must always have at least one Active status.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            status.Name = request.Name.Trim();
            status.DisplayOrder = request.DisplayOrder;
            status.RequiresApproval = request.RequiresApproval;
            status.ApproverId = request.ApproverId;
            status.Visibility = request.Visibility;
            status.Category = request.Category;
            status.Color = request.Color;
            status.MarksTaskComplete = request.Category == TaskStatusCategories.Done;
            status.UpdatedAt = DateTimeOffset.UtcNow;
            _statuses.Update(status);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 6: Update the API contract and controller call site**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs
// (only this record changes in this task)
public sealed record EditTaskStatusRequest(
    string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, string Visibility,
    string Category, string Color);
```

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs (EditStatus action, ~line 258-268)
    [HttpPatch("task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> EditStatus(Guid id, [FromBody] EditTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskStatusCommand(
            id, request.Name, request.DisplayOrder, request.RequiresApproval, request.ApproverId,
            request.Visibility, request.Category, request.Color), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter EditTaskStatusCommandHandlerTests`
Expected: PASS (7 tests).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/ \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs \
        src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskStatusCommandHandlerTests.cs
git commit -m "feat: EditTaskStatus supports Category/Color with Done-cap and Active-minimum guards"
```

### Task 5: DeleteTaskStatusCommand — guard the last Active row and the sole Done row

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusCategories` (Task 1), `ITaskStatusRepository.GetProjectTemplateAsync` (existing).
- No new public shape produced — `DeleteTaskStatusCommand(Guid StatusId)` is unchanged.

- [ ] **Step 1: Update the failing tests first**

Replace the whole file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskStatusCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    private (DeleteTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(
        string statusCategory, bool anyTasksInStatus, Guid? callerEmployeeId = null,
        bool? callerIsEffectiveManager = null, Guid? statusObjectiveId = null, List<TaskStatusEntity>? siblings = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var status = new TaskStatusEntity { Id = StatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = statusObjectiveId, Name = "Blocked", Category = statusCategory, CreatedAt = DateTimeOffset.UtcNow };
        var all = new List<TaskStatusEntity>(siblings ?? new List<TaskStatusEntity>()) { status };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(status);
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.AnyActiveByStatusIdAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(anyTasksInStatus);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var handler = new DeleteTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, tasks.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_DeleteOneOfTwoActiveRows_Succeeds()
    {
        var otherActive = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", Category = TaskStatusCategories.Active, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses) = Build(TaskStatusCategories.Active, anyTasksInStatus: false, siblings: new List<TaskStatusEntity> { otherActive });

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Remove(It.Is<TaskStatusEntity>(s => s.Id == StatusId)), Times.Once);
    }

    [Fact]
    public async Task Handle_DeleteTheLastActiveRow_ReturnsConflict()
    {
        var (handler, statuses) = Build(TaskStatusCategories.Active, anyTasksInStatus: false);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeleteTheSoleDoneRow_ReturnsConflict()
    {
        var (handler, statuses) = Build(TaskStatusCategories.Done, anyTasksInStatus: false);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeleteANotStartedRowWithNoSiblingConstraint_Succeeds()
    {
        var (handler, statuses) = Build(TaskStatusCategories.NotStarted, anyTasksInStatus: false);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Remove(It.Is<TaskStatusEntity>(s => s.Id == StatusId)), Times.Once);
    }

    [Fact]
    public async Task Handle_PhysicalTaskReferenceStillUsesStatus_ReturnsConflict()
    {
        var otherActive = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", Category = TaskStatusCategories.Active, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses) = Build(TaskStatusCategories.Active, anyTasksInStatus: true, siblings: new List<TaskStatusEntity> { otherActive });

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(TaskStatusCategories.NotStarted, anyTasksInStatus: false, callerEmployeeId: OtherEmployeeId);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StatusHasObjectiveId_ReturnsNotFound()
    {
        var (handler, statuses) = Build(TaskStatusCategories.NotStarted, anyTasksInStatus: false, statusObjectiveId: ObjectiveId);

        var result = await handler.Handle(new DeleteTaskStatusCommand(StatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        statuses.Verify(x => x.Remove(It.IsAny<TaskStatusEntity>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter DeleteTaskStatusCommandHandlerTests`
Expected: FAIL — the last-Active and sole-Done tests fail because the handler has no such guard yet (it currently only checks `AnyActiveByStatusIdAsync`).

- [ ] **Step 3: Add the guards to the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;

public class DeleteTaskStatusCommandHandler : IRequestHandler<DeleteTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IWorkTaskRepository _tasks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public DeleteTaskStatusCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IWorkTaskRepository tasks,
        IUnitOfWork unitOfWork, IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _tasks = tasks;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result> Handle(DeleteTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result.Forbidden("No employee record for the current user.");

        var status = await _statuses.GetByIdForTenantAsync(tenantId, request.StatusId, ct);
        if (status is null || status.ObjectiveId is not null)
            return Result.NotFound("Task status not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, status.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result.Forbidden("Only an owner or member of this project can delete task statuses.");

        if (status.Category == TaskStatusCategories.Done)
            return Result.Conflict("A project must always have exactly one Done status; edit it instead of deleting it.");

        if (status.Category == TaskStatusCategories.Active)
        {
            var siblings = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
            if (siblings.Count(s => s.Category == TaskStatusCategories.Active) <= 1)
                return Result.Conflict("A project must always have at least one Active status.");
        }

        if (await _tasks.AnyActiveByStatusIdAsync(tenantId, status.Id, ct))
            return Result.Conflict("Move all tasks out of this status before deleting it.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _statuses.Remove(status);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter DeleteTaskStatusCommandHandlerTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskStatus/DeleteTaskStatusCommandHandler.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskStatusCommandHandlerTests.cs
git commit -m "feat: DeleteTaskStatus guards the last Active row and the sole Done row"
```

### Task 6: ReorderTaskStatusesCommand — Category/Color per row, derive MarksTaskComplete server-side, add the Active-minimum invariant

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandValidator.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (`TaskStatusOrderUpdateRequest`, `ReorderTaskStatusesRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`ReorderStatuses` action only, ~line 243-256)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ReorderTaskStatusesCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusCategories` (Task 1), `TaskStatusResponse` 9-arg shape (Task 2).
- Produces: `TaskStatusOrderUpdate(Guid StatusId, int DisplayOrder, string Visibility, string Category, string Color)` — **`MarksTaskComplete` is removed from this record**; nothing later in this plan constructs it directly, but note the shape change for anyone extending this command later.

- [ ] **Step 1: Update the failing tests first**

Replace the whole file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ReorderTaskStatusesCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ReorderTaskStatusesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Status1 = Guid.NewGuid();
    private static readonly Guid Status2 = Guid.NewGuid();
    private static readonly Guid Status3 = Guid.NewGuid();

    private (ReorderTaskStatusesCommandHandler Handler, List<TaskStatusEntity> Statuses) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        // Status3 ("In Process", Active) is a fixed anchor most tests never include in Updates -
        // it keeps the fixture satisfying "at least one Active" while Status1/Status2 are moved
        // around by individual tests, the same way the pre-existing two-row fixture used to work
        // before the Active-minimum invariant existed.
        var statusList = new List<TaskStatusEntity>
        {
            new() { Id = Status1, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, Category = TaskStatusCategories.NotStarted, Color = "#94A3B8", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status2, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "Done", DisplayOrder = 2, Visibility = TaskStatusVisibilities.Private, Category = TaskStatusCategories.Done, MarksTaskComplete = true, Color = "#16A34A", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status3, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "In Process", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Public, Category = TaskStatusCategories.Active, Color = "#2563EB", CreatedAt = DateTimeOffset.UtcNow }
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(statusList);
        foreach (var s in statusList)
            statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new ReorderTaskStatusesCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, unitOfWork.Object, membership.Object);
        return (handler, statusList);
    }

    [Fact]
    public async Task Handle_ValidReorder_AppliesCategoryAndColor()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 1, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8"),
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
        Assert.Equal(TaskStatusVisibilities.Public, statuses.Single(s => s.Id == Status2).Visibility);
        Assert.True(statuses.Single(s => s.Id == Status2).MarksTaskComplete);
        Assert.False(statuses.Single(s => s.Id == Status1).MarksTaskComplete);
    }

    [Fact]
    public async Task Handle_MoveTheDoneRowToActiveWithNoOtherDoneInBatch_ReturnsFailure()
    {
        // Status2 is the only Done row in the whole project; moving it to Active without also
        // promoting some other row to Done leaves the full list with zero Done rows.
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TwoDoneEntriesInSameBatch_ReturnsValidationFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A"),
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyOtherActiveRowAndTheAnchorOutOfActive_ReturnsFailure()
    {
        // Moves both Status1 (currently NotStarted, left as NotStarted) and the anchor Status3
        // (currently the project's only Active row) out of Active, leaving zero Active rows total.
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status3, 0, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DuplicateStatusIds_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status2, 0, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A"),
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, null!);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullElementInUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            null!,
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, _) = Build(OtherEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_AppliesAllUpdates()
    {
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 1, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8"),
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ReorderTaskStatusesCommandHandlerTests`
Expected: FAIL to compile — `TaskStatusOrderUpdate` still has the old `MarksTaskComplete`-based 4-arg shape.

- [ ] **Step 3: Update the command record**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public sealed record TaskStatusOrderUpdate(Guid StatusId, int DisplayOrder, string Visibility, string Category, string Color);

public sealed record ReorderTaskStatusesCommand(Guid ProjectId, List<TaskStatusOrderUpdate> Updates) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
```

- [ ] **Step 4: Update the validator**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandValidator : AbstractValidator<ReorderTaskStatusesCommand>
{
    public ReorderTaskStatusesCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEqual(Guid.Empty);
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).NotNull()
            .WithMessage("Updates must not contain null entries.");
        RuleForEach(x => x.Updates)
            .Where(update => update is not null)
            .ChildRules(update =>
            {
                update.RuleFor(u => u.Visibility).Must(v => v is TaskStatusVisibilities.Public or TaskStatusVisibilities.Private);
                update.RuleFor(u => u.Category).Must(c => c is TaskStatusCategories.NotStarted or TaskStatusCategories.Active or TaskStatusCategories.Done);
                update.RuleFor(u => u.Color).Matches("^#[0-9A-Fa-f]{6}$");
                update.RuleFor(u => u.DisplayOrder).GreaterThanOrEqualTo(0);
            });
        RuleFor(x => x.Updates).Must(updates =>
                updates is not null
                && updates.All(u => u is not null)
                && updates.Count(u => u.StatusId != Guid.Empty) == updates.Select(u => u.StatusId).Distinct().Count())
            .WithMessage("Updates must not contain duplicate status IDs.");
        RuleFor(x => x.Updates).Must(updates =>
                updates is not null
                && updates.All(u => u is not null)
                && updates.Count(u => u.Category == TaskStatusCategories.Done) <= 1)
            .WithMessage("At most one status in a single reorder call may be marked Done.");
    }
}
```

- [ ] **Step 5: Update the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ReorderTaskStatusesCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;

public class ReorderTaskStatusesCommandHandler : IRequestHandler<ReorderTaskStatusesCommand, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectRepository _projects;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMilestoneMembershipCoordinator _membership;

    public ReorderTaskStatusesCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IProjectRepository projects, ITaskStatusRepository statuses, IUnitOfWork unitOfWork,
        IMilestoneMembershipCoordinator membership)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _projects = projects;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _membership = membership;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(ReorderTaskStatusesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project not found.");

        var defaultObjective = await _objectives.GetDefaultByProjectIdAsync(tenantId, project.Id, ct);
        if (defaultObjective is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Project has no default milestone.");

        if (!await _membership.IsEffectiveManagerAsync(tenantId, defaultObjective.Id, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Only an owner or member of this project can restructure the board.");

        // Defense in depth beyond the validator (which runs in the MediatR pipeline in production,
        // but not when a test calls Handle directly).
        if (request.Updates is null || request.Updates.Any(u => u is null))
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain null entries.", 422);

        if (request.Updates.Select(u => u.StatusId).Distinct().Count() != request.Updates.Count)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("Updates must not contain duplicate status IDs.", 422);

        if (request.Updates.Count(u => u.Category == TaskStatusCategories.Done) > 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("At most one status in a single reorder call may be marked Done.", 422);

        var existing = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var byId = existing.ToDictionary(s => s.Id);

        foreach (var update in request.Updates)
        {
            if (!byId.TryGetValue(update.StatusId, out var status))
                return Result<IReadOnlyList<TaskStatusResponse>>.NotFound($"Status {update.StatusId} not found on this milestone.");

            status.DisplayOrder = update.DisplayOrder;
            status.Visibility = update.Visibility;
            status.Category = update.Category;
            status.Color = update.Color;
            status.MarksTaskComplete = update.Category == TaskStatusCategories.Done;
            status.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (existing.Count(s => s.Category == TaskStatusCategories.Done) != 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("A project must always have exactly one Done status.", 422);

        if (existing.Count(s => s.Category == TaskStatusCategories.Active) < 1)
            return Result<IReadOnlyList<TaskStatusResponse>>.Failure("A project must always have at least one Active status.", 422);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            foreach (var status in existing.Where(s => request.Updates.Any(u => u.StatusId == s.Id)))
                _statuses.Update(status);

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<IReadOnlyList<TaskStatusResponse>>.Success(
                existing.OrderBy(s => s.DisplayOrder)
                    .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId, s.MarksTaskComplete, s.Visibility, s.Category, s.Color))
                    .ToList());
        }, ct);
    }
}
```

- [ ] **Step 6: Update the API contract and controller call site**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs
// (only these two records change in this task)
public sealed record TaskStatusOrderUpdateRequest(
    Guid StatusId, int DisplayOrder, string Visibility, string Category, string Color);

public sealed record ReorderTaskStatusesRequest(List<TaskStatusOrderUpdateRequest> Updates);
```

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs (ReorderStatuses action, ~line 243-256)
    [HttpPost("projects/{projectId:guid}/task-statuses/reorder")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ReorderStatuses(
        Guid projectId, [FromBody] ReorderTaskStatusesRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReorderTaskStatusesCommand(
            projectId,
            request.Updates.Select(u => new TaskStatusOrderUpdate(
                u.StatusId, u.DisplayOrder, u.Visibility, u.Category, u.Color)).ToList()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ReorderTaskStatusesCommandHandlerTests`
Expected: PASS (9 tests).

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ReorderTaskStatuses/ \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs \
        src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ReorderTaskStatusesCommandHandlerTests.cs
git commit -m "feat: ReorderTaskStatuses carries Category/Color and enforces the Active-minimum invariant"
```

### Task 7: CreateTaskCommandHandler — pick the default status by Category, not by the complete-flag

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs:95-98`
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs` (add to the existing `BuildHandler` helper and add one new test; do not rewrite the whole file — it is large and has many unrelated tests further down)

**Interfaces:**
- Consumes: `TaskStatusCategories` (Task 1).
- No new public shape produced.

- [ ] **Step 1: Add the failing test**

In `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`, change the `BuildHandler` method's signature to accept an optional status template override, and update its body's `statuses.Setup(...)` call:

```csharp
// Change the BuildHandler signature (around line 24) to add this parameter:
    private (CreateTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks, Mock<ISprintRepository> Sprints) BuildHandler(
        Objective objective, decimal existingAllocationSum, string sprintStatus = SprintStatuses.Active,
        Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null,
        bool categoryExists = true, Guid? categoryProjectId = null,
        Mock<ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces.ICalendarEventRepository>? calendarEvents = null,
        Mock<ITaskAssetLinker>? assetLinker = null,
        List<TaskStatusEntity>? statusTemplate = null)
    {
        // ... unchanged body until the `statuses` setup ...

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(statusTemplate ?? new List<TaskStatusEntity>
            {
                new() { Id = DefaultStatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, CreatedAt = DateTimeOffset.UtcNow }
            });

        // ... rest of the method unchanged ...
    }
```

Then add this test directly after `Handle_OwnerWithinSlack_GeneratesProjectPrefixedShortId`:

```csharp
    [Fact]
    public async Task Handle_NoNotStartedStatusExists_FallsBackToFirstActiveStatus()
    {
        var activeStatusId = Guid.NewGuid();
        var template = new List<TaskStatusEntity>
        {
            new() { Id = activeStatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Name = "In Process", DisplayOrder = 0, Category = TaskStatusCategories.Active, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Name = "Done", DisplayOrder = 1, Category = TaskStatusCategories.Done, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _, _) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m, statusTemplate: template);

        var result = await handler.Handle(
            new CreateTaskCommand(ObjectiveId, "Build the thing", null, CategoryId, "medium", null, EstimatedHours: 30m, StoryPoints: null, SprintId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(activeStatusId, result.Value!.StatusId);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter Handle_NoNotStartedStatusExists_FallsBackToFirstActiveStatus`
Expected: FAIL — the handler's current `Where(s => !s.MarksTaskComplete).OrderBy(...).FirstOrDefault()` picks the Active row correctly here too by coincidence (it's the only non-complete row), so **this specific test may actually pass already** — that's fine, it documents the desired behavior; the meaningful regression check is Step 4 confirming `Handle_OwnerWithinSlack_GeneratesProjectPrefixedShortId` still passes after the change. Proceed to the implementation regardless, since the design's intent (category-driven selection, not complete-flag-driven) is the correctness property being locked in, independent of whether this particular fixture happens to already agree.

- [ ] **Step 3: Update the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs (lines 95-98)
        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var defaultStatus = statuses.Where(s => s.Category == TaskStatusCategories.NotStarted).OrderBy(s => s.DisplayOrder).FirstOrDefault()
            ?? statuses.Where(s => s.Category == TaskStatusCategories.Active).OrderBy(s => s.DisplayOrder).FirstOrDefault();
        if (defaultStatus is null)
```

Add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;` at the top of the file if it is not already present (needed for `TaskStatusCategories`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CreateTaskCommandHandlerTests`
Expected: PASS, including the pre-existing `Handle_OwnerWithinSlack_GeneratesProjectPrefixedShortId` (its single-row fixture defaults to `Category = not_started` via the entity's own default, so it is still selected).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
git commit -m "feat: CreateTask picks the default status by Category (Not Started, then Active)"
```

### Task 8: ClockInTaskCommand — auto-move off a Not Started status, without bypassing the private-status permission

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommand.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/ClockInTaskResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (add `ClockInTaskViewModel`/`TaskStatusMoveInfoViewModel`)
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs` (add the new mapper)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs` (`ClockIn` action, ~line 375-384)
- Modify: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `TaskStatusCategories`, `TaskStatusVisibilities` (Task 1), `ITaskStatusRepository` (existing), `ITaskStatusChangeLogRepository`/`TaskStatusChangeLog` (existing, same shape `MoveTaskStatusCommandHandler` writes).
- Produces: `TaskStatusMoveInfo(Guid Id, string Name, string Color)`, `ClockInTaskResponse(TaskStatusMoveInfo? MovedToStatus)` — Task 13 (frontend) consumes the equivalent JSON shape as `movedToStatus: { id, name, color } | null`.

- [ ] **Step 1: Update the failing tests first**

Replace the whole file `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs` with:

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ClockInTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CurrentStatusId = Guid.NewGuid();

    private (ClockInTaskCommandHandler Handler, List<TaskClockingSession> Added, Guid CallerEmployeeId, WorkTask Task, List<TaskStatusChangeLog> StatusChanges) ArrangeClockInHandler(
        bool isAssignee, bool hasOpenSession, int taskProgressPercent,
        Guid? openSessionEmployeeId = null, bool authenticated = true,
        bool employeeExists = true, bool taskExists = true,
        string currentStatusCategory = TaskStatusCategories.Active,
        List<TaskStatusEntity>? projectTemplate = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? CallerEmployeeId : null);

        var task = new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, StatusId = CurrentStatusId,
            Title = "Task", ProgressPercent = taskProgressPercent, CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskAndEmployeeAsync(TaskId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isAssignee
                ? new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, UserId = UserId, EmployeeId = CallerEmployeeId, AssignedById = CallerEmployeeId, AssignedAt = DateTimeOffset.UtcNow }
                : null);

        var added = new List<TaskClockingSession>();
        var openSession = hasOpenSession
            ? new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId,
                EmployeeId = openSessionEmployeeId ?? CallerEmployeeId, ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
            : null;
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        sessions.Setup(x => x.AddAsync(It.IsAny<TaskClockingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TaskClockingSession, CancellationToken>((session, _) => added.Add(session))
            .Returns(Task.CompletedTask);

        var currentStatus = new TaskStatusEntity { Id = CurrentStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "Current", Category = currentStatusCategory, CreatedAt = DateTimeOffset.UtcNow };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, CurrentStatusId, It.IsAny<CancellationToken>())).ReturnsAsync(currentStatus);
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectTemplate ?? new List<TaskStatusEntity>
            {
                currentStatus,
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, Color = "#2563EB", CreatedAt = DateTimeOffset.UtcNow }
            });

        var statusChanges = new List<TaskStatusChangeLog>();
        var statusChangeLogs = new Mock<ITaskStatusChangeLogRepository>();
        statusChangeLogs.Setup(x => x.AddAsync(It.IsAny<TaskStatusChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskStatusChangeLog, CancellationToken>((log, _) => statusChanges.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ClockInTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ClockInTaskResponse>>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ClockInTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, assignments.Object, sessions.Object,
            statuses.Object, statusChangeLogs.Object, unitOfWork.Object);
        return (handler, added, CallerEmployeeId, task, statusChanges);
    }

    [Fact]
    public async Task Handle_AssigneeWithNoOpenSessionAndTaskNotLocked_OpensSession()
    {
        var (handler, sessions, callerEmployeeId, task, _) = ArrangeClockInHandler(true, false, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var opened = Assert.Single(sessions);
        Assert.Equal(task.Id, opened.TaskId);
        Assert.Equal(callerEmployeeId, opened.EmployeeId);
        Assert.Null(opened.ClockOutAt);
        Assert.Null(result.Value!.MovedToStatus);
    }

    [Fact]
    public async Task Handle_TaskInNotStartedStatus_AutoMovesToFirstPublicActiveStatus()
    {
        var activeStatusId = Guid.NewGuid();
        var template = new List<TaskStatusEntity>
        {
            new() { Id = CurrentStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = activeStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, Color = "#2563EB", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _, callerEmployeeId, task, statusChanges) = ArrangeClockInHandler(
            true, false, 20, currentStatusCategory: TaskStatusCategories.NotStarted, projectTemplate: template);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.MovedToStatus);
        Assert.Equal(activeStatusId, result.Value.MovedToStatus!.Id);
        Assert.Equal("In Process", result.Value.MovedToStatus.Name);
        Assert.Equal("#2563EB", result.Value.MovedToStatus.Color);
        Assert.Equal(activeStatusId, task.StatusId);
        var logged = Assert.Single(statusChanges);
        Assert.Equal(CurrentStatusId, logged.FromStatusId);
        Assert.Equal(activeStatusId, logged.ToStatusId);
        Assert.Equal(callerEmployeeId, logged.EmployeeId);
    }

    [Fact]
    public async Task Handle_TaskInNotStartedStatusWithOnlyPrivateActiveStatuses_ReturnsConflict()
    {
        var template = new List<TaskStatusEntity>
        {
            new() { Id = CurrentStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, Category = TaskStatusCategories.NotStarted, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, sessions, _, task, statusChanges) = ArrangeClockInHandler(
            true, false, 20, currentStatusCategory: TaskStatusCategories.NotStarted, projectTemplate: template);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
        Assert.Empty(statusChanges);
    }

    [Fact]
    public async Task Handle_TaskAlreadyHasOpenSession_ReturnsConflict()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, true, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskLockedAt100Percent_ReturnsConflict()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_CallerNotAnAssignee_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(false, false, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, authenticated: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, employeeExists: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, taskExists: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskAt99Percent_OpensSession()
    {
        var before = DateTimeOffset.UtcNow;
        var (handler, sessions, callerEmployeeId, task, _) = ArrangeClockInHandler(true, false, 99);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.True(result.IsSuccess);
        var opened = Assert.Single(sessions);
        Assert.Equal(task.Id, opened.TaskId);
        Assert.Equal(callerEmployeeId, opened.EmployeeId);
        Assert.InRange(opened.ClockInAt, before, after);
        Assert.Null(opened.ClockOutAt);
    }

    [Fact]
    public async Task Handle_OpenSessionOwnedByDifferentEmployee_ReturnsConflict()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(
            true, true, 20, openSessionEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NotAssigneeAndTaskComplete_ReturnsForbiddenBeforeLockCheck()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(false, false, 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ClockInTaskCommandHandlerTests`
Expected: FAIL to compile — `ClockInTaskCommandHandler`'s constructor doesn't take `ITaskStatusRepository`/`ITaskStatusChangeLogRepository` yet, and `Result<ClockInTaskResponse>` doesn't exist.

- [ ] **Step 3: Create the response DTO**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/ClockInTaskResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusMoveInfo(Guid Id, string Name, string Color);

public sealed record ClockInTaskResponse(TaskStatusMoveInfo? MovedToStatus);
```

- [ ] **Step 4: Update the command and handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public sealed record ClockInTaskCommand(Guid TaskId) : IRequest<Result<ClockInTaskResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ClockInTaskCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public class ClockInTaskCommandHandler : IRequestHandler<ClockInTaskCommand, Result<ClockInTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskStatusChangeLogRepository _statusChangeLogs;
    private readonly IUnitOfWork _unitOfWork;

    public ClockInTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments, ITaskClockingSessionRepository sessions,
        ITaskStatusRepository statuses, ITaskStatusChangeLogRepository statusChangeLogs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
        _statuses = statuses;
        _statusChangeLogs = statusChangeLogs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ClockInTaskResponse>> Handle(ClockInTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ClockInTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<ClockInTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<ClockInTaskResponse>.NotFound("Task not found.");

        if (await _assignments.GetByTaskAndEmployeeAsync(task.Id, callerEmployeeId.Value, ct) is null)
            return Result<ClockInTaskResponse>.Forbidden("Only an assignee of this task can clock in.");

        if (task.ProgressPercent == 100)
            return Result<ClockInTaskResponse>.Conflict("This task is complete - reduce its percentage before clocking in again.");

        if (await _sessions.GetOpenSessionForTaskAsync(tenantId, task.Id, ct) is not null)
            return Result<ClockInTaskResponse>.Conflict("This task already has an open clock-in session.");

        var currentStatus = await _statuses.GetByIdForTenantAsync(tenantId, task.StatusId, ct);
        TaskStatus? moveTarget = null;
        if (currentStatus is not null && currentStatus.Category == TaskStatusCategories.NotStarted)
        {
            var candidates = await _statuses.GetProjectTemplateAsync(tenantId, task.ProjectId, ct);
            moveTarget = candidates
                .Where(s => s.Category == TaskStatusCategories.Active && s.Visibility == TaskStatusVisibilities.Public)
                .OrderBy(s => s.DisplayOrder)
                .FirstOrDefault();
            if (moveTarget is null)
                return Result<ClockInTaskResponse>.Conflict("Every Active status on this project is private - ask the module owner to move this task first.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            await _sessions.AddAsync(new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                EmployeeId = callerEmployeeId.Value, ClockInAt = now
            }, innerCt);

            TaskStatusMoveInfo? movedInfo = null;
            if (moveTarget is not null)
            {
                var fromStatusId = task.StatusId;
                task.StatusId = moveTarget.Id;
                task.UpdatedAt = now;
                await _statusChangeLogs.AddAsync(new TaskStatusChangeLog
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id,
                    EmployeeId = callerEmployeeId.Value, FromStatusId = fromStatusId, ToStatusId = moveTarget.Id,
                    ChangedAt = now
                }, innerCt);
                movedInfo = new TaskStatusMoveInfo(moveTarget.Id, moveTarget.Name, moveTarget.Color);
            }

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<ClockInTaskResponse>.Success(new ClockInTaskResponse(movedInfo));
        }, ct);
    }
}
```

- [ ] **Step 5: Add the ViewModel and mapper, update the controller**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs
// (add these two new records in this task; nothing else in the file changes here)
public sealed record TaskStatusMoveInfoViewModel(Guid Id, string Name, string Color);

public sealed record ClockInTaskViewModel(TaskStatusMoveInfoViewModel? MovedToStatus);
```

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs
// (add this new mapper method; nothing else in the file changes here)
    public static ClockInTaskViewModel ToViewModel(this ClockInTaskResponse dto) => new(
        dto.MovedToStatus is null ? null : new TaskStatusMoveInfoViewModel(dto.MovedToStatus.Id, dto.MovedToStatus.Name, dto.MovedToStatus.Color));
```

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs (ClockIn action, ~line 375-384)
    [HttpPost("tasks/{id:guid}/clock-in")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> ClockIn(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ClockInTaskCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ClockInTaskCommandHandlerTests`
Expected: PASS (13 tests).

- [ ] **Step 7: Run the full backend unit test suite to confirm no other call sites broke**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: PASS — this is the first point since Task 2 where the solution is expected to fully compile and every test (including ones untouched by this plan) should be green again. If anything outside the files this plan touched fails to compile, search for other callers of `CreateTaskStatusCommand`, `EditTaskStatusCommand`, `ReorderTaskStatusesCommand`, `TaskStatusOrderUpdate`, `ClockInTaskCommand`, or `new TaskStatusResponse(` that this plan didn't anticipate, and update them to the new shapes following the same pattern as the tasks above.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/ClockInTask/ \
        src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/ClockInTaskResponse.cs \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs \
        src/ONEVO.Api/Contracts/WorkManagement/Tasks/WorkTaskViewModelMapper.cs \
        src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs \
        tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/ClockInTaskCommandHandlerTests.cs
git commit -m "feat: clock-in auto-moves a Not Started task to the first public Active status"
```

This is the last backend task. Backend work is done once this commit lands and Step 7's full-suite run is green.

---

## Frontend Tasks (repo: `Hrms--Web-application---front-end---v1`, worktree `C:\onevoNew\Hrms--Web-application---front-end---v1\.worktrees\task-status-groups`)

These tasks assume the backend API from Tasks 1-8 is deployed/available against the dev tenant used for manual verification. Unit tests mock `TaskApiService`, so they do not require the real backend to be running.

### Task 9: DTO and model updates — Category/Color on the wire, ClockIn response shape

**Files:**
- Modify: `src/app/modules/work/models/task.model.ts`
- Modify: `src/app/modules/work/models/dto/task.dto.ts`
- Modify: `src/app/modules/work/data-access/task-api.service.ts`

**Interfaces:**
- Produces: `TaskStatusCategory = 'not_started' | 'active' | 'done'` (exported from `task.model.ts`), `TaskStatusColumn.category`/`.color`, `TaskStatusDto.category`/`.color`, `CreateTaskStatusRequestDto`/`EditTaskStatusRequestDto` with `category`/`color` and no `marksTaskComplete`, `ClockInResponseDto { movedToStatus: { id: string; name: string; color: string } | null }`, `TaskApiService.reorderTaskStatuses(projectId, updates)` where each update is `{ statusId, displayOrder, visibility, category, color }` (no `marksTaskComplete`), `TaskApiService.clockIn(taskId): Observable<ClockInResponseDto>`. Tasks 11 and 13 depend on these exact names.

This task has no dedicated unit test of its own (it is a pure type/shape change with no runtime behavior) — its correctness is verified by Tasks 11 and 13's tests compiling and passing against these new shapes. Do not skip re-running `npm run build` (or `ng build`) at the end of this task to catch any other file in the codebase that references the old `marksTaskComplete`-based `CreateTaskStatusRequestDto`/`EditTaskStatusRequestDto`/`TaskStatusOrderUpdate`-equivalent shapes.

- [ ] **Step 1: Add the shared category type and update `TaskStatusColumn`**

```ts
// src/app/modules/work/models/task.model.ts
export type TaskStatusCategory = 'not_started' | 'active' | 'done';

export interface TaskStatusColumn {
  id: string;
  name: string;
  displayOrder: number;
  requiresApproval: boolean;
  marksTaskComplete: boolean;
  visibility: 'public' | 'private';
  category: TaskStatusCategory;
  color: string;
}
```
(The rest of `task.model.ts` — `TaskAttachment`, `WorkTask` — is unchanged.)

- [ ] **Step 2: Update the DTOs**

Open `src/app/modules/work/models/dto/task.dto.ts` and change `TaskStatusDto`, `CreateTaskStatusRequestDto`, `EditTaskStatusRequestDto` to:

```ts
import { TaskStatusCategory } from '../task.model';

export interface TaskStatusDto {
  id: string;
  name: string;
  displayOrder: number;
  requiresApproval: boolean;
  approverId: string | null;
  marksTaskComplete: boolean;
  visibility: 'public' | 'private';
  category: TaskStatusCategory;
  color: string;
}

export interface CreateTaskStatusRequestDto {
  name: string;
  displayOrder: number;
  visibility: 'public' | 'private';
  category: TaskStatusCategory;
  color: string;
  requiresApproval: boolean;
  approverId?: string;
}

export interface EditTaskStatusRequestDto {
  name: string;
  displayOrder: number;
  requiresApproval: boolean;
  approverId?: string;
  visibility: 'public' | 'private';
  category: TaskStatusCategory;
  color: string;
}

export interface ClockInResponseDto {
  movedToStatus: { id: string; name: string; color: string } | null;
}
```
Adjust the import path (`'../task.model'`) if `task.dto.ts` lives at a different relative depth than assumed — verify against the existing relative imports already in that file for `WorkTaskDto` etc.

- [ ] **Step 3: Update `TaskApiService`**

```ts
// src/app/modules/work/data-access/task-api.service.ts
// (only these three methods change; the rest of the file, including all imports except adding
// TaskStatusCategory and ClockInResponseDto, is unchanged)
  clockIn(taskId: string): Observable<ClockInResponseDto> {
    return this.http.post<ClockInResponseDto>(`${this.baseUrl}/tasks/${taskId}/clock-in`, {});
  }

  reorderTaskStatuses(
    projectId: string,
    updates: { statusId: string; displayOrder: number; visibility: string; category: TaskStatusCategory; color: string }[]
  ): Observable<TaskStatusDto[]> {
    return this.http.post<TaskStatusDto[]>(`${this.baseUrl}/projects/${projectId}/task-statuses/reorder`, { updates });
  }
```
Add `ClockInResponseDto` to the existing import block from `../models/dto/task.dto` at the top of the file, and add `import { TaskStatusCategory } from '../models/task.model';` if not already importing from there.

- [ ] **Step 4: Build to catch any other call site**

Run: `npm run build` (or the project's configured `ng build` script) from `Hrms--Web-application---front-end---v1`.
Expected: compile errors, if any, only in `board-structure-editor.component.ts` and its spec (both rewritten in Task 11), `project-form-modal.component.ts` (Task 10), `task-board-column.component.ts` (Task 12), and `task-clock-widget.component.ts` (Task 13) — every other file should already compile. If an unexpected file fails, it is another caller of the old DTO shapes this plan did not anticipate; update it the same way the later tasks update their target files (add `category`/`color`, drop `marksTaskComplete` from writes).

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/models/task.model.ts \
        src/app/modules/work/models/dto/task.dto.ts \
        src/app/modules/work/data-access/task-api.service.ts
git commit -m "feat: add Category/Color and ClockIn response shape to task status DTOs"
```

### Task 10: Extract the project-color-picker into a shared `ColorSwatchPickerComponent`

**Files:**
- Create: `src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.ts`
- Create: `src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.spec.ts`
- Modify: `src/app/modules/work/ui/project-form-modal/project-form-modal.component.ts`

**Interfaces:**
- Produces: `ColorSwatchPickerComponent` with `presets = input.required<{ value: string; label: string }[]>()`, `selected = input<string>('')`, `colorChange = output<string>()`. Task 11's grouped status editor imports and uses this component directly.

- [ ] **Step 0: Check for existing test hooks before refactoring**

Open `src/app/modules/work/ui/project-form-modal/project-form-modal.component.spec.ts` and search for `color-field`, `proj-color-swatch`, or `proj-color-native`. If any test queries those selectors directly, the new shared component (Step 1) must render the same `data-testid="color-field"` wrapper and the same `proj-color-swatch`/`proj-color-native` CSS class names so those tests keep passing unmodified — the extraction must be a pure move, not a redesign. Adjust the class names in Step 1 to match whatever the spec actually asserts on if it differs from what is written below.

- [ ] **Step 1: Write the failing test for the new component**

```ts
// src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { ColorSwatchPickerComponent } from './color-swatch-picker.component';

describe('ColorSwatchPickerComponent', () => {
  const presets = [
    { value: '#2563eb', label: 'Blue' },
    { value: '#16a34a', label: 'Green' }
  ];

  function setup(selected = '') {
    TestBed.configureTestingModule({ imports: [ColorSwatchPickerComponent] });
    const fixture = TestBed.createComponent(ColorSwatchPickerComponent);
    fixture.componentRef.setInput('presets', presets);
    fixture.componentRef.setInput('selected', selected);
    return fixture;
  }

  it('renders one swatch button per preset', () => {
    const fixture = setup();
    fixture.detectChanges();
    const swatches = fixture.nativeElement.querySelectorAll('.proj-color-swatch:not(.proj-color-swatch--custom)');
    expect(swatches.length).toBe(2);
  });

  it('clicking a preset swatch emits colorChange with that preset value', () => {
    const fixture = setup();
    fixture.detectChanges();
    const emitted: string[] = [];
    fixture.componentInstance.colorChange.subscribe((c) => emitted.push(c));

    const swatches = fixture.nativeElement.querySelectorAll('.proj-color-swatch:not(.proj-color-swatch--custom)') as NodeListOf<HTMLButtonElement>;
    swatches[1].click();

    expect(emitted).toEqual(['#16a34a']);
  });

  it('marks the swatch matching the selected value as active', () => {
    const fixture = setup('#16a34a');
    fixture.detectChanges();
    const swatches = fixture.nativeElement.querySelectorAll('.proj-color-swatch:not(.proj-color-swatch--custom)') as NodeListOf<HTMLButtonElement>;
    expect(swatches[0].classList.contains('proj-color-swatch--active')).toBe(false);
    expect(swatches[1].classList.contains('proj-color-swatch--active')).toBe(true);
  });

  it('toggling custom reveals a native color input, and changing it emits colorChange', () => {
    const fixture = setup();
    fixture.detectChanges();
    const customButton = fixture.nativeElement.querySelector('.proj-color-swatch--custom') as HTMLButtonElement;
    customButton.click();
    fixture.detectChanges();

    const nativeInput = fixture.nativeElement.querySelector('.proj-color-native') as HTMLInputElement;
    expect(nativeInput).not.toBeNull();

    const emitted: string[] = [];
    fixture.componentInstance.colorChange.subscribe((c) => emitted.push(c));
    nativeInput.value = '#123456';
    nativeInput.dispatchEvent(new Event('input'));

    expect(emitted).toEqual(['#123456']);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.spec.ts`
Expected: FAIL — the component file does not exist yet.

- [ ] **Step 3: Create the component** (extracted verbatim from `project-form-modal.component.ts:206-257,768-791,889-896`, parameterized)

```ts
// src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.ts
import { Component, input, output, signal } from '@angular/core';

@Component({
  selector: 'app-color-swatch-picker',
  standalone: true,
  template: `
    <span data-testid="color-field" class="flex items-center gap-2 flex-wrap">
      @for (preset of presets(); track preset.value) {
        <button
          type="button"
          class="proj-color-swatch"
          [class.proj-color-swatch--active]="selected() === preset.value && !showCustom()"
          [style.background]="preset.value"
          [title]="preset.label"
          (click)="selectPreset(preset.value)"
        ></button>
      }
      <div class="relative flex items-center">
        <button
          type="button"
          class="proj-color-swatch proj-color-swatch--custom"
          [class.proj-color-swatch--active]="showCustom()"
          title="Custom color"
          (click)="toggleCustom()"
        >
          <svg class="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 21a4 4 0 01-4-4V5a2 2 0 012-2h4a2 2 0 012 2v12a4 4 0 01-4 4zm0 0h12a2 2 0 002-2v-4a2 2 0 00-2-2h-2.343M11 7.343l1.657-1.657a2 2 0 012.828 0l2.829 2.829a2 2 0 010 2.828l-8.486 8.485M7 17h.01"/></svg>
        </button>
        @if (showCustom()) {
          <input
            type="color"
            class="proj-color-native"
            [value]="selected() || '#2563eb'"
            (input)="emitColor($any($event.target).value)"
            title="Pick a custom color"
          />
        }
      </div>
      @if (selected()) {
        <div class="flex items-center gap-1 ml-1">
          <div class="h-4 w-4 rounded-full border border-[var(--color-border)] shrink-0" [style.background]="selected()"></div>
          <span class="text-[10px] font-mono text-[var(--color-text-secondary)]">{{ selected() }}</span>
        </div>
      }
    </span>
  `
})
export class ColorSwatchPickerComponent {
  presets = input.required<{ value: string; label: string }[]>();
  selected = input<string>('');
  colorChange = output<string>();

  protected readonly showCustom = signal(false);

  selectPreset(value: string): void {
    this.showCustom.set(false);
    this.colorChange.emit(value);
  }

  toggleCustom(): void {
    this.showCustom.update((v) => !v);
  }

  emitColor(value: string): void {
    this.colorChange.emit(value);
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/modules/work/ui/color-swatch-picker/color-swatch-picker.component.spec.ts`
Expected: PASS (4 tests). If Step 0 found different existing selectors, adjust the template's class names to match before this step, not after.

- [ ] **Step 5: Refactor `project-form-modal.component.ts` to use it**

Replace the template block at lines 206-257 (the `<!-- Color Picker -->` section) with:

```html
                  <span class="col-span-2 flex flex-col gap-1">
                    <label class="text-xs font-semibold text-[var(--color-text-secondary)]">Project Color</label>
                    <app-color-swatch-picker [presets]="colorPresets" [selected]="color()" (colorChange)="color.set($event)" />
                  </span>
```

Remove the now-unused `showCustomColor` signal (line 791) and the `selectPresetColor()`/`toggleCustomColor()` methods (lines 889-896) from the component class — the shared component owns that state now. Keep the `colorPresets` array (lines 768-773) and the `color` signal (line 790) as-is; both are still used, just passed into the new component. Add `ColorSwatchPickerComponent` to the component's `imports` array.

- [ ] **Step 6: Run the existing project-form-modal tests**

Run: `npx vitest run src/app/modules/work/ui/project-form-modal/project-form-modal.component.spec.ts`
Expected: PASS with no changes needed to that spec file, because Step 0/Step 4 preserved the exact selectors it depends on. If it fails, the mismatch identified in Step 0 was not fully carried into Step 3 — fix the component template, not the spec.

- [ ] **Step 7: Commit**

```bash
git add src/app/modules/work/ui/color-swatch-picker/ \
        src/app/modules/work/ui/project-form-modal/project-form-modal.component.ts
git commit -m "refactor: extract project color picker into a shared ColorSwatchPickerComponent"
```

### Task 11: Rebuild the status editor as three grouped, cross-draggable sections

**Files:**
- Modify: `src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.ts` (full rewrite)
- Modify: `src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.spec.ts` (full rewrite)

**Interfaces:**
- Consumes: `TaskStatusColumn.category`/`.color` (Task 9), `TaskApiService.createStatus`/`.reorderTaskStatuses`/`.deleteStatus` new shapes (Task 9), `ColorSwatchPickerComponent` (Task 10).
- No new public shape produced — `BoardStructureEditorComponent`'s `open`/`projectId`/`statuses`/`saved`/`closed` inputs/outputs are unchanged, so `task-status-settings-tab.component.ts` (the only caller, per the design doc) needs no changes.

- [ ] **Step 1: Replace the spec file first (TDD against the new behavior)**

Replace the whole file `src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.spec.ts` with:

```ts
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { of } from 'rxjs';
import { BoardStructureEditorComponent } from './board-structure-editor.component';
import { TaskApiService } from '../../data-access/task-api.service';
import { TaskStatusColumn } from '../../models/task.model';
import { TaskStatusDto } from '../../models/dto/task.dto';

describe('BoardStructureEditorComponent', () => {
  const statuses: TaskStatusColumn[] = [
    { id: 's4', name: 'Done', displayOrder: 3, requiresApproval: false, marksTaskComplete: true, visibility: 'private', category: 'done', color: '#16A34A' },
    { id: 's1', name: 'To Do', displayOrder: 0, requiresApproval: false, marksTaskComplete: false, visibility: 'public', category: 'not_started', color: '#94A3B8' },
    { id: 's3', name: 'Review', displayOrder: 2, requiresApproval: false, marksTaskComplete: false, visibility: 'private', category: 'active', color: '#7C3AED' },
    { id: 's2', name: 'In Process', displayOrder: 1, requiresApproval: false, marksTaskComplete: false, visibility: 'public', category: 'active', color: '#2563EB' }
  ];

  function setup() {
    const reorderTaskStatuses = vi.fn().mockReturnValue(of([] as TaskStatusDto[]));
    const createStatus = vi.fn().mockReturnValue(of({
      id: 's5', name: 'Blocked', displayOrder: 4, requiresApproval: false, approverId: null,
      marksTaskComplete: false, visibility: 'public', category: 'active', color: '#2563EB'
    } as TaskStatusDto));
    const deleteStatus = vi.fn().mockReturnValue(of(undefined));
    TestBed.configureTestingModule({
      imports: [BoardStructureEditorComponent],
      providers: [{ provide: TaskApiService, useValue: { reorderTaskStatuses, createStatus, deleteStatus } }]
    });
    const fixture = TestBed.createComponent(BoardStructureEditorComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('projectId', 'project-1');
    fixture.componentRef.setInput('statuses', statuses);
    return { fixture, reorderTaskStatuses, createStatus, deleteStatus };
  }

  function rowNames(fixture: ReturnType<typeof setup>['fixture']): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.board-structure__name') as NodeListOf<HTMLElement>)
      .map((el) => el.textContent?.trim() ?? '');
  }

  it('groups rows under Not started / Active / Done, each sorted by DisplayOrder', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    expect(groups.length).toBe(3);
    expect(groups[0].querySelector('.board-structure__group-title')?.textContent).toContain('Not started');
    expect(groups[1].querySelector('.board-structure__group-title')?.textContent).toContain('Active');
    expect(groups[2].querySelector('.board-structure__group-title')?.textContent).toContain('Done');

    expect(Array.from(groups[0].querySelectorAll('.board-structure__name')).map((n) => n.textContent?.trim())).toEqual(['To Do']);
    expect(Array.from(groups[1].querySelectorAll('.board-structure__name')).map((n) => n.textContent?.trim())).toEqual(['In Process', 'Review']);
    expect(Array.from(groups[2].querySelectorAll('.board-structure__name')).map((n) => n.textContent?.trim())).toEqual(['Done']);
  });

  it('the Done row and the last remaining Active row have no delete icon; a non-sole Active row does', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    const activeRows = groups[1].querySelectorAll('.board-structure__row');
    const doneRows = groups[2].querySelectorAll('.board-structure__row');

    expect(activeRows[0].querySelector('.board-structure__delete-icon')).not.toBeNull();
    expect(activeRows[1].querySelector('.board-structure__delete-icon')).not.toBeNull();
    expect(doneRows[0].querySelector('.board-structure__delete-icon')).toBeNull();
  });

  it('the Done group has no add button; Not started and Active do', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    expect(groups[0].querySelector('.board-structure__add-icon')).not.toBeNull();
    expect(groups[1].querySelector('.board-structure__add-icon')).not.toBeNull();
    expect(groups[2].querySelector('.board-structure__add-icon')).toBeNull();
  });

  it('the lock icon toggles a row visibility between public and private', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    const reviewLock = groups[1].querySelectorAll('.board-structure__row')[1].querySelector('.board-structure__lock-icon') as HTMLButtonElement;
    expect(reviewLock.classList.contains('board-structure__lock-icon--active')).toBe(true); // Review starts private
    reviewLock.click();
    fixture.detectChanges();
    expect(reviewLock.classList.contains('board-structure__lock-icon--active')).toBe(false);
  });

  it('clicking + on the Active group, naming it, and confirming creates a status with category active and a default color', async () => {
    const { fixture, createStatus } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    (groups[1].querySelector('.board-structure__add-icon') as HTMLButtonElement).click();
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('.board-structure__new-input') as HTMLInputElement;
    input.value = 'Blocked';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const addButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((b) => b.textContent?.trim() === 'Add')!;
    addButton.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(createStatus).toHaveBeenCalledWith('project-1', {
      name: 'Blocked', displayOrder: 4, visibility: 'public', category: 'active', color: '#2563EB', requiresApproval: false
    });
    expect(rowNames(fixture)).toContain('Blocked');
  });

  it('dragging a row from Active into Not started changes its category before saving', async () => {
    const { fixture, reorderTaskStatuses } = setup();
    fixture.detectChanges();

    // Simulate a completed cross-list drag the way CDK reports it: previousContainer is the
    // Active list ('board-structure-drop-active'), container is the Not started list.
    const activeList = fixture.componentInstance['active']();
    const notStartedList = fixture.componentInstance['notStarted']();
    fixture.componentInstance.onDrop({
      previousContainer: { id: 'board-structure-drop-active', data: activeList },
      container: { id: 'board-structure-drop-not_started', data: notStartedList },
      previousIndex: 0,
      currentIndex: 1
    } as unknown as Parameters<typeof fixture.componentInstance.onDrop>[0], 'not_started');
    fixture.detectChanges();

    const saveButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((b) => b.textContent?.includes('Update'))!;
    saveButton.click();
    await fixture.whenStable();

    expect(reorderTaskStatuses).toHaveBeenCalledTimes(1);
    const [, updates] = reorderTaskStatuses.mock.calls[0] as [string, { statusId: string; category: string }[]];
    const moved = updates.find((u) => u.statusId === 's2')!;
    expect(moved.category).toBe('not_started');
  });

  it('the delete icon on a deletable row deletes that status and removes its row', async () => {
    const { fixture, deleteStatus } = setup();
    fixture.detectChanges();

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    const reviewDelete = groups[1].querySelectorAll('.board-structure__row')[1].querySelector('.board-structure__delete-icon') as HTMLButtonElement;
    reviewDelete.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(deleteStatus).toHaveBeenCalledWith('s3');
    expect(rowNames(fixture)).not.toContain('Review');
  });

  it('emits saved after a create or delete, so the parent reloads its own statuses list', async () => {
    const { fixture } = setup();
    fixture.detectChanges();
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    const groups = fixture.nativeElement.querySelectorAll('.board-structure__group');
    (groups[1].querySelectorAll('.board-structure__row')[1].querySelector('.board-structure__delete-icon') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(saved).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.spec.ts`
Expected: FAIL — the current component has no grouping, no color dot, no lock icon, and a different `DraftRow` shape.

- [ ] **Step 3: Rewrite the component**

```ts
// src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.ts
import { Component, effect, inject, input, output, signal, WritableSignal } from '@angular/core';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, CdkDropListGroup, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';
import { firstValueFrom } from 'rxjs';
import { TaskApiService } from '../../data-access/task-api.service';
import { ModalComponent } from '../../../../shared/ui/modal/modal.component';
import { ButtonComponent } from '../../../../shared/ui/button/button.component';
import { ColorSwatchPickerComponent } from '../color-swatch-picker/color-swatch-picker.component';
import { TaskStatusColumn, TaskStatusCategory } from '../../models/task.model';

interface DraftRow {
  id: string;
  name: string;
  visibility: 'public' | 'private';
  category: TaskStatusCategory;
  color: string;
}

const GROUP_LABELS: Record<TaskStatusCategory, string> = {
  not_started: 'Not started',
  active: 'Active',
  done: 'Done'
};

const GROUP_DEFAULT_COLOR: Record<TaskStatusCategory, string> = {
  not_started: '#94A3B8',
  active: '#2563EB',
  done: '#16A34A'
};

@Component({
  selector: 'app-board-structure-editor',
  standalone: true,
  imports: [ModalComponent, CdkDropList, CdkDropListGroup, CdkDrag, CdkDragHandle, ButtonComponent, ColorSwatchPickerComponent],
  template: `
    <app-modal [open]="open()" title="Edit task statuses" (closed)="closed.emit()">
      <div cdkDropListGroup>
        @for (group of groups; track group) {
          <div class="board-structure__group">
            <div class="board-structure__group-header">
              <span class="board-structure__group-title">{{ groupLabel(group) }}</span>
              <span class="board-structure__group-count">{{ rowsFor(group)().length }}</span>
              @if (group !== 'done') {
                <button type="button" class="board-structure__add-icon" [attr.aria-label]="'Add status to ' + groupLabel(group)"
                  (click)="startAdd(group)">+</button>
              }
            </div>

            @if (addingTo() === group) {
              <div class="board-structure__toolbar">
                <input class="board-structure__new-input" placeholder="Status name" [value]="newStatusName()"
                  (input)="newStatusName.set($any($event.target).value)" (keydown.enter)="confirmAdd()" />
                <app-button variant="primary" [loading]="adding()" (pressed)="confirmAdd()">Add</app-button>
                <app-button variant="secondary" [disabled]="adding()" (pressed)="cancelAdd()">Cancel</app-button>
              </div>
            }

            <div cdkDropList [cdkDropListData]="rowsFor(group)()" [id]="'board-structure-drop-' + group"
              (cdkDropListDropped)="onDrop($event, group)" class="board-structure__list">
              @for (row of rowsFor(group)(); track row.id) {
                <div cdkDrag [cdkDragData]="row" class="board-structure__row">
                  <span class="board-structure__drag-handle" cdkDragHandle>⠿</span>
                  <button type="button" class="board-structure__color-dot" [style.background]="row.color"
                    [attr.aria-label]="'Change color for ' + row.name" (click)="toggleColorPicker(row.id)"></button>
                  @if (colorPickerFor() === row.id) {
                    <app-color-swatch-picker [presets]="colorPresets" [selected]="row.color"
                      (colorChange)="setColor(row.id, $event)" />
                  }
                  <span class="board-structure__name">{{ row.name }}</span>
                  <button type="button" class="board-structure__lock-icon"
                    [class.board-structure__lock-icon--active]="row.visibility === 'private'"
                    [attr.aria-label]="row.visibility === 'private' ? 'Make public' : 'Make private'"
                    (click)="toggleVisibility(row.id)">🔒</button>
                  @if (canDelete(row)) {
                    <button type="button" class="board-structure__delete-icon" aria-label="Delete status" (click)="deleteRow(row.id)">🗑</button>
                  }
                </div>
              }
            </div>
          </div>
        }
      </div>

      @if (errorMessage()) {
        <p class="board-structure__error" role="alert">{{ errorMessage() }}</p>
      }

      <div class="board-structure__actions">
        <app-button variant="secondary" [disabled]="saving()" (pressed)="closed.emit()">Cancel</app-button>
        <app-button variant="primary" [loading]="saving()" (pressed)="save()">Update</app-button>
      </div>
    </app-modal>
  `,
  styles: [`
    .board-structure__group { margin-bottom: 16px; }
    .board-structure__group-header { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; font-size: 12px; font-weight: 600; color: var(--color-text-secondary); text-transform: uppercase; }
    .board-structure__group-count { color: var(--color-text-tertiary); font-weight: 400; }
    .board-structure__add-icon { margin-left: auto; width: 22px; height: 22px; border-radius: 999px; border: 1px solid var(--color-border); background: var(--color-surface); cursor: pointer; font-size: 14px; line-height: 1; }
    .board-structure__toolbar { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .board-structure__new-input { flex: 1; border: 1px solid var(--color-border); border-radius: 8px; padding: 8px 10px; background: var(--color-surface); color: var(--color-text-primary); }
    .board-structure__list { display: flex; flex-direction: column; gap: 4px; min-height: 8px; }
    .board-structure__row { position: relative; display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-surface); }
    .board-structure__drag-handle { cursor: grab; color: var(--color-text-secondary); }
    .board-structure__color-dot { width: 12px; height: 12px; border-radius: 999px; border: none; cursor: pointer; padding: 0; }
    .board-structure__name { flex: 1; font-size: 13px; color: var(--color-text-primary); }
    .board-structure__lock-icon { background: none; border: none; cursor: pointer; opacity: 0.35; font-size: 13px; }
    .board-structure__lock-icon--active { opacity: 1; }
    .board-structure__delete-icon { background: none; border: none; cursor: pointer; font-size: 14px; line-height: 1; color: var(--color-text-secondary); }
    .board-structure__delete-icon:hover { color: var(--color-danger-text); }
    .board-structure__error { color: var(--color-danger-text); font-size: 12px; margin-bottom: 8px; }
    .board-structure__actions { display: flex; justify-content: flex-end; gap: 8px; }
  `]
})
export class BoardStructureEditorComponent {
  private readonly taskApi = inject(TaskApiService);

  readonly groups: TaskStatusCategory[] = ['not_started', 'active', 'done'];
  readonly colorPresets = [
    { value: '#2563eb', label: 'Blue' },
    { value: '#16a34a', label: 'Green' },
    { value: '#7c3aed', label: 'Purple' },
    { value: '#ea580c', label: 'Orange' },
    { value: '#94a3b8', label: 'Grey' }
  ];

  open = input(false);
  projectId = input.required<string>();
  statuses = input.required<readonly TaskStatusColumn[]>();
  saved = output<void>();
  closed = output<void>();

  protected readonly notStarted = signal<DraftRow[]>([]);
  protected readonly active = signal<DraftRow[]>([]);
  protected readonly done = signal<DraftRow[]>([]);
  protected readonly saving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly addingTo = signal<TaskStatusCategory | null>(null);
  protected readonly newStatusName = signal('');
  protected readonly adding = signal(false);
  protected readonly colorPickerFor = signal<string | null>(null);

  constructor() {
    // Same re-sync-on-open rationale as before this rewrite: this instance is never re-created
    // once wired into objective-settings, so ngOnInit would only capture the first load.
    effect(() => {
      if (this.open()) {
        const rows = [...this.statuses()].sort((a, b) => a.displayOrder - b.displayOrder)
          .map((s): DraftRow => ({ id: s.id, name: s.name, visibility: s.visibility, category: s.category, color: s.color }));
        this.notStarted.set(rows.filter((r) => r.category === 'not_started'));
        this.active.set(rows.filter((r) => r.category === 'active'));
        this.done.set(rows.filter((r) => r.category === 'done'));
        this.errorMessage.set(null);
      } else {
        this.addingTo.set(null);
        this.newStatusName.set('');
        this.colorPickerFor.set(null);
      }
    });
  }

  groupLabel(group: TaskStatusCategory): string {
    return GROUP_LABELS[group];
  }

  rowsFor(group: TaskStatusCategory): WritableSignal<DraftRow[]> {
    return group === 'not_started' ? this.notStarted : group === 'active' ? this.active : this.done;
  }

  canDelete(row: DraftRow): boolean {
    if (row.category === 'done') return false;
    if (row.category === 'active' && this.active().length <= 1) return false;
    return true;
  }

  toggleVisibility(id: string): void {
    this.updateRow(id, (r) => ({ ...r, visibility: r.visibility === 'private' ? 'public' : 'private' }));
  }

  toggleColorPicker(id: string): void {
    this.colorPickerFor.set(this.colorPickerFor() === id ? null : id);
  }

  setColor(id: string, color: string): void {
    this.updateRow(id, (r) => ({ ...r, color }));
    this.colorPickerFor.set(null);
  }

  private updateRow(id: string, update: (row: DraftRow) => DraftRow): void {
    for (const group of this.groups) {
      const sig = this.rowsFor(group);
      if (sig().some((r) => r.id === id)) {
        sig.set(sig().map((r) => (r.id === id ? update(r) : r)));
        return;
      }
    }
  }

  onDrop(event: CdkDragDrop<DraftRow[]>, targetGroup: TaskStatusCategory): void {
    const sourceList = event.previousContainer.data;
    const targetList = event.container.data;
    if (event.previousContainer === event.container) {
      moveItemInArray(targetList, event.previousIndex, event.currentIndex);
      this.rowsFor(targetGroup).set([...targetList]);
      return;
    }
    transferArrayItem(sourceList, targetList, event.previousIndex, event.currentIndex);
    targetList[event.currentIndex] = { ...targetList[event.currentIndex], category: targetGroup };
    const sourceGroup = event.previousContainer.id.replace('board-structure-drop-', '') as TaskStatusCategory;
    this.rowsFor(sourceGroup).set([...sourceList]);
    this.rowsFor(targetGroup).set([...targetList]);
  }

  startAdd(group: TaskStatusCategory): void {
    this.addingTo.set(group);
    this.newStatusName.set('');
  }

  cancelAdd(): void {
    this.addingTo.set(null);
    this.newStatusName.set('');
  }

  async confirmAdd(): Promise<void> {
    const group = this.addingTo();
    const name = this.newStatusName().trim();
    if (!group || !name) return;
    this.adding.set(true);
    this.errorMessage.set(null);
    try {
      const created = await firstValueFrom(this.taskApi.createStatus(this.projectId(), {
        name, displayOrder: this.flatten().length, visibility: 'public',
        category: group, color: GROUP_DEFAULT_COLOR[group], requiresApproval: false
      }));
      const sig = this.rowsFor(group);
      sig.set([...sig(), { id: created.id, name: created.name, visibility: created.visibility, category: created.category, color: created.color }]);
      this.addingTo.set(null);
      this.newStatusName.set('');
      this.saved.emit();
    } catch (err: unknown) {
      const detail = (err as { error?: { detail?: string } })?.error?.detail;
      this.errorMessage.set(detail || 'Failed to add the status.');
    } finally {
      this.adding.set(false);
    }
  }

  async deleteRow(id: string): Promise<void> {
    this.errorMessage.set(null);
    try {
      await firstValueFrom(this.taskApi.deleteStatus(id));
      for (const group of this.groups) {
        const sig = this.rowsFor(group);
        if (sig().some((r) => r.id === id)) {
          sig.set(sig().filter((r) => r.id !== id));
          break;
        }
      }
      this.saved.emit();
    } catch (err: unknown) {
      const detail = (err as { error?: { detail?: string } })?.error?.detail;
      this.errorMessage.set(detail || 'Move all tasks out of this status before deleting it.');
    }
  }

  private flatten(): DraftRow[] {
    return [...this.notStarted(), ...this.active(), ...this.done()];
  }

  async save(): Promise<void> {
    this.saving.set(true);
    this.errorMessage.set(null);
    try {
      const rows = this.flatten();
      await firstValueFrom(this.taskApi.reorderTaskStatuses(
        this.projectId(),
        rows.map((r, index) => ({ statusId: r.id, displayOrder: index, visibility: r.visibility, category: r.category, color: r.color }))
      ));
      this.saved.emit();
      this.closed.emit();
    } catch (err: unknown) {
      const detail = (err as { error?: { detail?: string } })?.error?.detail;
      this.errorMessage.set(detail || 'Failed to save. Make sure exactly one status is marked Done.');
    } finally {
      this.saving.set(false);
    }
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/modules/work/ui/board-structure-editor/board-structure-editor.component.spec.ts`
Expected: PASS (8 tests).

- [ ] **Step 5: Verify manually in the browser**

Start the dev server, open a project's task status settings, and confirm: three labeled groups render, dragging a row between groups works and persists after "Update", the color dot opens the shared picker and changing it is reflected immediately, the lock icon toggles, Done's row has no delete icon, and deleting the second-to-last Active row is blocked (shows the backend's 409 message) while other Active rows delete fine.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/board-structure-editor/
git commit -m "feat: redesign the status editor as three grouped, cross-draggable sections"
```

### Task 12: Render the status color on the board column header

**Files:**
- Modify: `src/app/modules/work/ui/task-board-column/task-board-column.component.ts`
- Modify or create (check first): `src/app/modules/work/ui/task-board-column/task-board-column.component.spec.ts`

**Interfaces:**
- Consumes: `TaskStatusColumn.color`/`.category` (Task 9). `status = input.required<TaskStatusColumn>()` already exists on this component (line 770) — no input signature change.

- [ ] **Step 0: Check for an existing spec file**

Run: `find src/app/modules/work/ui/task-board-column -name "*.spec.ts"` (or check via your editor). If `task-board-column.component.spec.ts` exists, read it fully first and add the test below into it using its existing setup/mock conventions rather than introducing a second, inconsistent style. If it does not exist, create it fresh with a minimal `TestBed` setup mirroring `board-structure-editor.component.spec.ts`'s conventions (standalone component import, `fixture.componentRef.setInput(...)` for `status`/`tasks`/any other required inputs — read the component's `input.required<...>()` declarations near line 770 onward to know exactly which inputs must be set for the fixture to render without error).

- [ ] **Step 1: Write the failing test**

```ts
// Add to task-board-column.component.spec.ts (new or existing file, adapted to its actual required inputs)
it('renders a status-colored indicator before the column title', () => {
  const fixture = setup({ id: 's1', name: 'In Process', displayOrder: 1, requiresApproval: false, marksTaskComplete: false, visibility: 'public', category: 'active', color: '#2563EB' });
  fixture.detectChanges();

  const dot = fixture.nativeElement.querySelector('.task-board-column__status-dot') as HTMLElement;
  expect(dot).not.toBeNull();
  expect(dot.style.background).toContain('37, 99, 235'); // rgb() form of #2563EB, as the browser normalizes inline styles
});
```
Adjust the `setup(...)` call to match whatever helper (or inline `fixture.componentRef.setInput('status', {...})`) the actual spec file uses — the point of this test is only to assert `.task-board-column__status-dot` exists and carries `status().color` as its background.

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/task-board-column/task-board-column.component.spec.ts`
Expected: FAIL — no `.task-board-column__status-dot` element exists yet.

- [ ] **Step 3: Add the indicator to the header template**

Find the header block (verified to currently read exactly this, no color binding anywhere):
```html
<div class="task-board-column__header">
  <h3 class="task-board-column__title">
    <span class="task-board-column__title-text">{{ status().name }}</span>
    <span class="task-board-column__count">{{ tasks().length }}</span>
  </h3>
  <div class="task-board-column__actions">
```
Change the `<h3>` block to:
```html
<div class="task-board-column__header">
  <h3 class="task-board-column__title">
    <span class="task-board-column__status-dot" [style.background]="status().color"
      [class.task-board-column__status-dot--done]="status().category === 'done'"></span>
    <span class="task-board-column__title-text">{{ status().name }}</span>
    <span class="task-board-column__count">{{ tasks().length }}</span>
  </h3>
  <div class="task-board-column__actions">
```
Add the corresponding style next to the existing `.task-board-column__header`/`.task-board-column__title` rules (around lines 311-331):
```css
.task-board-column__status-dot { display: inline-block; width: 8px; height: 8px; border-radius: 999px; margin-right: 4px; }
.task-board-column__status-dot--done { border-radius: 3px; } /* a small square reads as "done" distinctly from the round in-progress dot, without needing an icon font */
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/modules/work/ui/task-board-column/task-board-column.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Verify manually in the browser**

Open the task board and confirm each column header shows a small colored dot matching that status's configured color, with the Done column's dot rendered as a small square instead of a circle.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/task-board-column/
git commit -m "feat: show the configured status color on board column headers"
```

### Task 13: Toast the user when clock-in auto-moves their task's status

**Files:**
- Modify: `src/app/modules/work/ui/task-clock-widget/task-clock-widget.component.ts` (`onClockIn()`, lines 166-169)
- Modify or create (check first): `src/app/modules/work/ui/task-clock-widget/task-clock-widget.component.spec.ts`

**Interfaces:**
- Consumes: `TaskApiService.clockIn(taskId): Observable<ClockInResponseDto>` (Task 9), `NotificationService.success(message: string)`/`.error(message: string)` (existing, `src/app/core/services/notification.service.ts`).

- [ ] **Step 0: Check for an existing spec file**

If `task-clock-widget.component.spec.ts` exists, read it fully and add the tests below using its existing `TestBed`/mock conventions. If not, create it fresh mirroring `board-structure-editor.component.spec.ts`'s conventions, providing `TaskApiService` and `NotificationService` as `useValue` partial mocks.

- [ ] **Step 1: Write the failing tests**

```ts
// Add to task-clock-widget.component.spec.ts
it('onClockIn with no status move just emits clockedIn and shows no toast', async () => {
  const { component, clockIn, success } = setup({ movedToStatus: null });

  await component.onClockIn();

  expect(clockIn).toHaveBeenCalledTimes(1);
  expect(success).not.toHaveBeenCalled();
});

it('onClockIn with a status move shows a success toast naming the new status', async () => {
  const { component, success } = setup({ movedToStatus: { id: 'st-1', name: 'In Process', color: '#2563EB' } });

  await component.onClockIn();

  expect(success).toHaveBeenCalledWith(expect.stringContaining('In Process'));
});

it('onClockIn surfaces a backend error via a toast instead of throwing', async () => {
  const { component, error } = setup(undefined, { detail: 'Every Active status on this project is private.' });

  await component.onClockIn();

  expect(error).toHaveBeenCalledWith('Every Active status on this project is private.');
});
```
Adapt the `setup(...)` helper signature to whatever pattern the actual spec file already uses for constructing the component and its inputs (e.g. `task` is likely a required signal input — check the component's existing `input.required<WorkTask>()` declaration and set it via `fixture.componentRef.setInput('task', ...)` the same way other specs in this codebase do); the important part is that `TaskApiService.clockIn` and `NotificationService.success`/`.error` are `vi.fn()` mocks whose return/throw behavior the test controls, e.g.:

```ts
function setup(clockInResult?: { movedToStatus: { id: string; name: string; color: string } | null }, errorBody?: { detail: string }) {
  const clockIn = errorBody
    ? vi.fn().mockReturnValue(throwError(() => ({ error: errorBody })))
    : vi.fn().mockReturnValue(of(clockInResult ?? { movedToStatus: null }));
  const success = vi.fn();
  const error = vi.fn();
  TestBed.configureTestingModule({
    imports: [TaskClockWidgetComponent],
    providers: [
      { provide: TaskApiService, useValue: { clockIn } },
      { provide: NotificationService, useValue: { success, error } }
    ]
  });
  const fixture = TestBed.createComponent(TaskClockWidgetComponent);
  // set whatever required inputs the component declares, e.g.:
  // fixture.componentRef.setInput('task', { id: 'task-1', ... } as WorkTask);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, clockIn, success, error };
}
```
(Add `import { throwError } from 'rxjs';` alongside the existing `of` import.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/app/modules/work/ui/task-clock-widget/task-clock-widget.component.spec.ts`
Expected: FAIL — `onClockIn()` today neither reads `movedToStatus` nor catches errors.

- [ ] **Step 3: Update `onClockIn()`**

```ts
// src/app/modules/work/ui/task-clock-widget/task-clock-widget.component.ts (lines 166-169)
  private readonly notificationService = inject(NotificationService);

  async onClockIn(): Promise<void> {
    try {
      const response = await firstValueFrom(this.taskApi.clockIn(this.task().id));
      if (response.movedToStatus) {
        this.notificationService.success(`Moved to "${response.movedToStatus.name}" to start tracking time.`);
      }
      this.clockedIn.emit();
    } catch (err: unknown) {
      const detail = (err as { error?: { detail?: string } })?.error?.detail;
      this.notificationService.error(detail || 'Failed to clock in.');
    }
  }
```
Add `import { NotificationService } from '../../../../core/services/notification.service';` at the top (adjust the relative path to match this file's actual depth — verify against another file under `src/app/modules/work/` that already imports `NotificationService`, e.g. wherever `company-selector.component.ts`'s sibling-depth equivalent import resolves from, since `task-clock-widget` sits one level deeper under `ui/`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/app/modules/work/ui/task-clock-widget/task-clock-widget.component.spec.ts`
Expected: PASS.

- [ ] **Step 5: Verify manually in the browser**

Clock in on a task that is currently in a Not Started status and confirm a success toast appears naming the status it moved to, the board reflects the new column immediately (or on next refresh, per whatever refresh mechanism `clockedIn` already triggers in the parent), and clocking in on a task already in an Active/Done status shows no toast.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/task-clock-widget/
git commit -m "feat: toast the user when clock-in auto-moves their task off Not Started"
```

This is the last frontend task.

---

## Self-Review Notes (spec coverage, placeholders, type consistency)

- **Spec coverage:** every section of `2026-09-18-task-status-groups-design.md` maps to a task above — data model/migration (Task 1), read-path exposure (Task 2), Create/Edit/Delete/Reorder guards (Tasks 3-6), default-status selection (Task 7), clock-in auto-move (Task 8), shared DTOs (Task 9), color picker extraction (Task 10), grouped editor (Task 11), board color (Task 12), clock-in toast (Task 13). The spec's "Out of Scope" section (status templates, per-objective override UI, status-list edit permissions) has deliberately no corresponding task.
- **Placeholder scan:** no task above contains "TBD"/"handle appropriately"/"similar to Task N" in place of real code; every code step is complete and compilable as written, with three explicitly-flagged exceptions where this plan cannot know the exact current file contents without the executor looking (Task 10 Step 0, Task 12 Step 0, Task 13 Step 0) — each names exactly what to check and how to adapt, which is a verification instruction, not a placeholder for logic.
- **Type consistency check performed:** `TaskStatusResponse`'s 9-argument shape introduced in Task 2 is used identically in Tasks 3, 6, and 8's `new TaskStatusResponse(...)` calls. `TaskStatusOrderUpdate`'s 5-argument no-`MarksTaskComplete` shape from Task 6 has no other constructor call site in this plan. `ClockInTaskResponse`/`TaskStatusMoveInfo` from Task 8 match `ClockInResponseDto`'s shape in Task 9 and are consumed identically in Task 13. `TaskStatusCategories.NotStarted/Active/Done` (backend) and `TaskStatusCategory = 'not_started' | 'active' | 'done'` (frontend) use the same three literal string values throughout every task on both sides.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-18-task-status-groups.md` (present in both repos' worktrees). Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
