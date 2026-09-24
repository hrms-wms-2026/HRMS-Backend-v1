# Work Management — Task Foundation, Part 1: Schema + Task CRUD (Board/Backlog) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `task_statuses`, `tasks` (entity name `WorkTask` — see Task 2 naming note), and `task_assignments` tables plus their CRUD/query endpoints, so the frontend's Board and Backlog tabs have a real API to call.

**Architecture:** Standard Work Management CQRS: MediatR `Command`/`Query` + `Handler` (+ `Validator` for commands) under `ONEVO.Application/Features/WorkManagement/Tasks/`, EF configuration + migration under `ONEVO.Infrastructure`, controller under `ONEVO.Api/Controllers/Tenant/WorkManagement/`. Identity resolution goes through the existing `ICallerIdentityResolver` seam — never compare `UserId` directly against a business-ownership column.

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL, snake_case convention, RLS via raw SQL in migrations), MediatR, FluentValidation, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md` §1, §2, §5 (this part covers the schema for §1's "building now" list minus `task_creation_requests`, and all of §5).

## Global Constraints

- EmployeeId only for every person-reference column in this slice — never `UserId` on a business field (spec §2).
- `estimated_hours` create/edit must strictly enforce the slack invariant from spec §3.1 — this is a **blocking** check, unlike the rest of the codebase's warning-only over-allocation convention elsewhere.
- No `sprint_id`/`version_id` columns on `tasks` in this slice (spec §1 — deferred).
- No HR-availability-check columns on `task_assignments` in this slice (spec §1 — deferred).
- Every new migration must enable + force RLS and add the `tenant_isolation` policy, following the exact SQL block already used in `20260805083151_AddObjectiveHierarchyAndChangeRequests.cs` (see reference read in Task 1) — this repo has a documented history of two prior incidents where a new tenant-owned table shipped without this and broke `TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage`.
- **Naming:** the Domain entity is `WorkTask`, not `Task` — `System.Threading.Tasks.Task` is used everywhere in this codebase's async method signatures (`Task<Result<T>>`), so an entity literally named `Task` would create constant `ONEVO.Domain...Task` vs `System.Threading.Tasks.Task` ambiguity. The **database table** is still `tasks` (`builder.ToTable("tasks")`), and the **API contracts/JSON** use `task`/`tasks` — only the C# class name differs.

---

### Task 1: Read reference files before starting

**Files:** none created/modified — read-only orientation task.

- [ ] **Step 1: Read these five files in full to internalize the codebase's conventions before writing any code:**
  - `src/ONEVO.Domain/Features/WorkManagement/ObjectiveChangeRequests/Entities/ObjectiveChangeRequest.cs` (entity + static Types/Statuses classes pattern)
  - `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveChangeRequestConfiguration.cs` (EF configuration pattern)
  - `src/ONEVO.Infrastructure/Migrations/20260805083151_AddObjectiveHierarchyAndChangeRequests.cs` (migration + RLS SQL block — copy this exact `Up`/`Down` RLS pattern for every new table in this plan)
  - `src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/CreateObjective/CreateObjectiveCommandHandler.cs` (handler pattern: `ICurrentUser` → `ICallerIdentityResolver` → validate → `IUnitOfWork.ExecuteInTransactionAsync`)
  - `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ObjectivesController.cs` (controller pattern: `Result` → `IActionResult` mapping via `Problem(result.Error, statusCode: result.StatusCode ?? 400)`)
- [ ] **Step 2: No commit for this task — proceed to Task 2.**

### Task 2: `TaskStatus` entity, EF configuration, repository interface

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs`
- Create: `src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskStatusRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusConfigurationTests.cs`

**Interfaces:**
- Produces: `TaskStatus` (Id, TenantId, ProjectId, ObjectiveId?, Name, DisplayOrder, RequiresApproval, ApproverId?, MarksTaskComplete, CreatedAt, UpdatedAt), `ITaskStatusRepository.{AddAsync, AddRangeAsync, GetByObjectiveIdAsync, GetProjectTemplateAsync, GetByIdForTenantAsync, Update}`.

- [ ] **Step 1: Write the failing test — a plain construction/shape test (this repo has no in-memory EF test harness for configuration classes, so this is a compile-and-default-value smoke test, matching the "no dedicated config test exists elsewhere in Work Management" convention observed in Task 1's reference reads):**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskStatusConfigurationTests
{
    [Fact]
    public void TaskStatus_DefaultsToRequiresApprovalFalseAndMarksTaskCompleteFalse()
    {
        var status = new TaskStatus
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.False(status.RequiresApproval);
        Assert.False(status.MarksTaskComplete);
        Assert.Null(status.ObjectiveId);
        Assert.Null(status.ApproverId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter TaskStatusConfigurationTests`
Expected: FAIL — `ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus` does not exist.

- [ ] **Step 3: Write the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

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
}
```

- [ ] **Step 4: Write the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusConfiguration : IEntityTypeConfiguration<TaskStatus>
{
    public void Configure(EntityTypeBuilder<TaskStatus> builder)
    {
        builder.ToTable("task_statuses");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.DisplayOrder })
            .HasDatabaseName("ix_task_statuses_tenant_id_project_id_objective_id_display_order");

        // Name unique within its template/Objective scope (phase1-table-inventory.md).
        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.ObjectiveId, s.Name })
            .IsUnique()
            .HasDatabaseName("ix_task_statuses_one_name_per_scope");
    }
}
```

- [ ] **Step 5: Write the repository interface + implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskStatusRepository
{
    Task AddAsync(TaskStatus status, CancellationToken ct = default);
    Task AddRangeAsync(IReadOnlyList<TaskStatus> statuses, CancellationToken ct = default);

    /// <summary>Project-level template rows (ObjectiveId == null), ordered by DisplayOrder.</summary>
    Task<IReadOnlyList<TaskStatus>> GetProjectTemplateAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>An Objective's own status rows (ObjectiveId == the given id), ordered by DisplayOrder. Empty if not yet copied from the template.</summary>
    Task<IReadOnlyList<TaskStatus>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    Task<TaskStatus?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    void Update(TaskStatus status);
}
```

```csharp
// src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskStatusRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Repositories.WorkManagement;

public class TaskStatusRepository : ITaskStatusRepository
{
    private readonly ApplicationDbContext _db;

    public TaskStatusRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskStatus status, CancellationToken ct = default)
        => await _db.Set<TaskStatus>().AddAsync(status, ct);

    public async Task AddRangeAsync(IReadOnlyList<TaskStatus> statuses, CancellationToken ct = default)
        => await _db.Set<TaskStatus>().AddRangeAsync(statuses, ct);

    public async Task<IReadOnlyList<TaskStatus>> GetProjectTemplateAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.Set<TaskStatus>().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ProjectId == projectId && s.ObjectiveId == null)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TaskStatus>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Set<TaskStatus>().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ObjectiveId == objectiveId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

    public async Task<TaskStatus?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Set<TaskStatus>().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public void Update(TaskStatus status) => _db.Set<TaskStatus>().Update(status);
}
```

- [ ] **Step 6: Register the repository in DI** — find the existing `services.AddScoped<IObjectiveRepository, ObjectiveRepository>();` line in `src/ONEVO.Infrastructure/DependencyInjection.cs` and add directly below it:

```csharp
services.AddScoped<ITaskStatusRepository, TaskStatusRepository>();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter TaskStatusConfigurationTests`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatus.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusRepository.cs src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskStatusRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskStatusConfigurationTests.cs
git commit -m "feat(work): add TaskStatus entity, configuration, and repository"
```

### Task 3: `WorkTask` entity, EF configuration, repository interface

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Create: `src/ONEVO.Infrastructure/Repositories/WorkManagement/WorkTaskRepository.cs`

**Interfaces:**
- Consumes: `TaskStatus` (Task 2).
- Produces: `WorkTask` (Id, ProjectId, TenantId, ParentTaskId?, ObjectiveId, ShortId, Title, Description?, TaskType, StatusId, Priority, StoryPoints?, DueDate?, EstimatedHours?, CompletedHours, ProgressPercent, StartedAt?, CompletedAt?, CreatedById, CreatedAt, UpdatedAt), `IWorkTaskRepository.{AddAsync, GetByIdForTenantAsync, GetTrackedByIdForTenantAsync, GetByObjectiveIdAsync, GetActiveAllocationSumByObjectiveIdAsync, Update}`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/WorkTaskConfigurationTests.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class WorkTaskConfigurationTests
{
    [Fact]
    public void WorkTask_DefaultsToZeroProgressAndZeroCompletedHours()
    {
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(),
            ObjectiveId = Guid.NewGuid(), ShortId = "PRJ-1", Title = "Do the thing",
            TaskType = WorkTaskTypes.Task, StatusId = Guid.NewGuid(), Priority = WorkTaskPriorities.Medium,
            CreatedById = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.Equal(0, task.ProgressPercent);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Null(task.ParentTaskId);
        Assert.Null(task.CompletedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WorkTaskConfigurationTests`
Expected: FAIL — `WorkTask` does not exist.

- [ ] **Step 3: Write the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class WorkTaskTypes
{
    public const string Task = "task";
    public const string Bug = "bug";
    public const string Story = "story";
    public const string Feature = "feature";
}

public static class WorkTaskPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

/// <summary>
/// Core Work Management item. Table name stays "tasks" (see plan's Global Constraints naming
/// note) - the C# class is WorkTask to avoid colliding with System.Threading.Tasks.Task.
/// </summary>
public class WorkTask : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid ObjectiveId { get; set; }
    public string ShortId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TaskType { get; set; } = WorkTaskTypes.Task;
    public Guid StatusId { get; set; }
    public string Priority { get; set; } = WorkTaskPriorities.Medium;
    public int? StoryPoints { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal CompletedHours { get; set; }
    public int ProgressPercent { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

- [ ] **Step 4: Write the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ShortId).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(500).IsRequired();
        builder.Property(t => t.TaskType).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Priority).HasMaxLength(20).IsRequired();
        builder.Property(t => t.EstimatedHours).HasColumnType("numeric(18,2)");
        builder.Property(t => t.CompletedHours).HasColumnType("numeric(18,2)");

        builder.HasIndex(t => new { t.TenantId, t.ObjectiveId, t.StatusId })
            .HasDatabaseName("ix_tasks_tenant_id_objective_id_status_id");
        builder.HasIndex(t => new { t.TenantId, t.ShortId })
            .IsUnique()
            .HasDatabaseName("ix_tasks_one_short_id_per_tenant");

        builder.HasOne<TaskStatus>().WithMany().HasForeignKey(t => t.StatusId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5: Write the repository interface + implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface IWorkTaskRepository
{
    Task AddAsync(WorkTask task, CancellationToken ct = default);
    Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>SUM(EstimatedHours) across active tasks in this Objective — the "SUM(direct_tasks.estimated_hours)"
    /// half of the slack formula in spec §3.1. Excludes the task identified by `excludingTaskId` (used on
    /// edit, to avoid double-counting the task's own current value against its own proposed new value).</summary>
    Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default);

    void Update(WorkTask task);
}
```

```csharp
// src/ONEVO.Infrastructure/Repositories/WorkManagement/WorkTaskRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Repositories.WorkManagement;

public class WorkTaskRepository : IWorkTaskRepository
{
    private readonly ApplicationDbContext _db;

    public WorkTaskRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkTask task, CancellationToken ct = default)
        => await _db.Set<WorkTask>().AddAsync(task, ct);

    public async Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Set<WorkTask>().AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Set<WorkTask>().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.Set<WorkTask>().AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId)
            .ToListAsync(ct);

    public async Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default)
        => await _db.Set<WorkTask>().AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.Id != (excludingTaskId ?? Guid.Empty))
            .SumAsync(t => t.EstimatedHours ?? 0m, ct);

    public void Update(WorkTask task) => _db.Set<WorkTask>().Update(task);
}
```

- [ ] **Step 6: Register in DI** — add `services.AddScoped<IWorkTaskRepository, WorkTaskRepository>();` next to Task 2's registration.

- [ ] **Step 7: Run test, verify PASS. Step 8: Commit.**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/WorkTask.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkTaskConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Repositories/WorkManagement/WorkTaskRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/WorkTaskConfigurationTests.cs
git commit -m "feat(work): add WorkTask entity, configuration, and repository"
```

### Task 4: `TaskAssignment` entity, EF configuration, repository interface

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskAssignment.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskAssignmentConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskAssignmentRepository.cs`
- Create: `src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskAssignmentRepository.cs`

**Interfaces:**
- Consumes: `WorkTask` (Task 3).
- Produces: `TaskAssignment` (Id, TaskId, UserId, EmployeeId, AssignedById, AssignedAt), `ITaskAssignmentRepository.{AddAsync, GetByTaskIdAsync, GetByTaskAndEmployeeAsync, Remove}`. Note: unlike other entities in this plan, `TaskAssignment` does **not** inherit `BaseEntity` — it has no `TenantId` column of its own (tenant scoping happens via the `WorkTask` join, matching the inventory doc's column list exactly, which has no `tenant_id`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssignmentConfigurationTests.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAssignmentConfigurationTests
{
    [Fact]
    public void TaskAssignment_CanBeConstructedWithRequiredFields()
    {
        var assignment = new TaskAssignment
        {
            Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(), AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.UtcNow
        };

        Assert.NotEqual(Guid.Empty, assignment.TaskId);
        Assert.NotEqual(Guid.Empty, assignment.EmployeeId);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL (`TaskAssignment` does not exist).**

- [ ] **Step 3: Write the entity**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskAssignment.cs
namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>Who is assigned to a task. HR-availability-check enrichment deferred - see plan Global Constraints.</summary>
public class TaskAssignment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AssignedById { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}
```

- [ ] **Step 4: Write the EF configuration**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskAssignmentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignments");
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.TaskId, a.UserId }).IsUnique()
            .HasDatabaseName("ix_task_assignments_one_per_task_user");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Repository interface + implementation**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskAssignmentRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskAssignmentRepository
{
    Task AddAsync(TaskAssignment assignment, CancellationToken ct = default);
    Task<IReadOnlyList<TaskAssignment>> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default);
    Task<TaskAssignment?> GetByTaskAndEmployeeAsync(Guid taskId, Guid employeeId, CancellationToken ct = default);
    void Remove(TaskAssignment assignment);
}
```

```csharp
// src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskAssignmentRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Repositories.WorkManagement;

public class TaskAssignmentRepository : ITaskAssignmentRepository
{
    private readonly ApplicationDbContext _db;

    public TaskAssignmentRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskAssignment assignment, CancellationToken ct = default)
        => await _db.Set<TaskAssignment>().AddAsync(assignment, ct);

    public async Task<IReadOnlyList<TaskAssignment>> GetByTaskIdAsync(Guid taskId, CancellationToken ct = default)
        => await _db.Set<TaskAssignment>().AsNoTracking().Where(a => a.TaskId == taskId).ToListAsync(ct);

    public async Task<TaskAssignment?> GetByTaskAndEmployeeAsync(Guid taskId, Guid employeeId, CancellationToken ct = default)
        => await _db.Set<TaskAssignment>().FirstOrDefaultAsync(a => a.TaskId == taskId && a.EmployeeId == employeeId, ct);

    public void Remove(TaskAssignment assignment) => _db.Set<TaskAssignment>().Remove(assignment);
}
```

- [ ] **Step 6: Register in DI** — add `services.AddScoped<ITaskAssignmentRepository, TaskAssignmentRepository>();`.

- [ ] **Step 7: Run test, verify PASS. Step 8: Commit.**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskAssignment.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskAssignmentConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskAssignmentRepository.cs src/ONEVO.Infrastructure/Repositories/WorkManagement/TaskAssignmentRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssignmentConfigurationTests.cs
git commit -m "feat(work): add TaskAssignment entity, configuration, and repository"
```

### Task 5: Migration — create all three tables with RLS

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskFoundationTables.cs` (run `dotnet ef migrations add AddTaskFoundationTables --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api` to generate the real timestamp/Designer file — do not hand-write the Designer file)

**Interfaces:** none — this task wires the DB to match Tasks 2-4's `OnModelCreating`-registered configurations.

- [ ] **Step 1: Generate the migration**

Run: `dotnet ef migrations add AddTaskFoundationTables --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

- [ ] **Step 2: Open the generated `Up` method and confirm it created `task_statuses`, `tasks`, `task_assignments` with every column/index from Tasks 2-4's configurations. If EF's default column ordering/naming differs from the inventory doc's expectations, fix column types manually in the generated file (e.g. `numeric(18,2)` for hours columns should already be correct from the `HasColumnType` calls — verify, don't assume).**

- [ ] **Step 3: Append RLS SQL for all three tables at the end of `Up`, copying the exact policy block from `20260805083151_AddObjectiveHierarchyAndChangeRequests.cs`, repeated per table:**

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE task_statuses ENABLE ROW LEVEL SECURITY;
    ALTER TABLE task_statuses FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS tenant_isolation ON task_statuses;
    CREATE POLICY tenant_isolation ON task_statuses
        USING (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        )
        WITH CHECK (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        );

    ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
    ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS tenant_isolation ON tasks;
    CREATE POLICY tenant_isolation ON tasks
        USING (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        )
        WITH CHECK (
            current_setting('app.tenant_context_mode', true) = 'admin'
            OR (
                current_setting('app.tenant_context_mode', true) = 'tenant'
                AND tenant_id::text = current_setting('app.current_tenant_id', true)
            )
        );
");
```

Note: `task_assignments` has **no `tenant_id` column** (Task 4) so it gets **no RLS policy** of its own — tenant isolation for it is enforced transitively through its `task_id` FK to `tasks`, which does have RLS. Do not add a `tenant_id` column to `task_assignments` just to give it RLS; that would contradict Task 4's design and the inventory doc.

- [ ] **Step 4: Add the matching `DROP POLICY`/`DISABLE ROW LEVEL SECURITY` calls at the top of `Down`, before `DropTable`, mirroring the reference migration's `Down` method (drop `task_statuses` and `tasks` policies; `task_assignments` has none to drop).**

- [ ] **Step 5: Apply the migration to a local/dev Postgres instance and verify RLS is live**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Then, connected to that Postgres instance: `SELECT tablename, policyname FROM pg_policies WHERE tablename IN ('task_statuses', 'tasks');`
Expected: one `tenant_isolation` row per table.

- [ ] **Step 6: Run the architecture test suite to confirm no RLS-coverage regression**

Run: `dotnet test --filter TenantIsolationArchitectureTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(work): migration for task_statuses, tasks, task_assignments with RLS"
```

### Task 6: `CreateTask` command — Objective owner direct-create with slack enforcement

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/IObjectiveAllocationSlackCalculator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ObjectiveAllocationSlackCalculator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository`, `IObjectiveRepository.GetByIdForTenantAsync`, `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync`, `IUnitOfWork.ExecuteInTransactionAsync`.
- Produces: `IObjectiveAllocationSlackCalculator.CalculateAsync(Guid tenantId, Objective objective, CancellationToken ct) -> Task<decimal>` (reused unchanged by Part 3's allocation-extend flow — do not duplicate this formula there), `CreateTaskCommand(Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints) : IRequest<Result<WorkTaskResponse>>`.

- [ ] **Step 1: Write the failing test — happy path + the slack-block case**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Objective Owned(decimal allocatedHours) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = EmployeeId,
        IsActive = true, AllocatedHours = allocatedHours, CreatedAt = DateTimeOffset.UtcNow
    };

    private (CreateTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks) BuildHandler(
        Objective objective, decimal existingAllocationSum)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAllocationSum);

        var slackCalculator = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateTaskCommandHandler(currentUser.Object, identity.Object, objectives.Object, tasks.Object, slackCalculator, unitOfWork.Object);
        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_OwnerWithinSlack_CreatesTask()
    {
        var (handler, tasks) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m);
        var command = new CreateTaskCommand(ObjectiveId, "Build the thing", null, "task", "medium", null, EstimatedHours: 30m, StoryPoints: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.Is<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(t => t.EstimatedHours == 30m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnerExceedsSlack_ReturnsConflictWithAvailableSlack()
    {
        // allocated 100, already 40 used -> 60 slack; requesting 70 exceeds it.
        var (handler, tasks) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m);
        var command = new CreateTaskCommand(ObjectiveId, "Too big", null, "task", "medium", null, EstimatedHours: 70m, StoryPoints: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL (types don't exist yet).**

- [ ] **Step 3: Write `IObjectiveAllocationSlackCalculator`/`ObjectiveAllocationSlackCalculator`** — implements spec §3.1's formula exactly: `slack = objective.AllocatedHours - SUM(active child objectives' AllocatedHours) - SUM(active tasks' EstimatedHours)`. This plan's Part 1 only needs the task half; the child-objective half is included now so Part 3 (allocation-extend) can reuse this same service unchanged rather than re-deriving the formula.

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/IObjectiveAllocationSlackCalculator.cs
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>Implements spec §3.1's slack formula: AllocatedHours - SUM(active child objectives) - SUM(active tasks).</summary>
public interface IObjectiveAllocationSlackCalculator
{
    Task<decimal> CalculateAsync(Guid tenantId, Objective objective, Guid? excludingTaskId = null, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ObjectiveAllocationSlackCalculator.cs
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public class ObjectiveAllocationSlackCalculator : IObjectiveAllocationSlackCalculator
{
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public ObjectiveAllocationSlackCalculator(IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _objectives = objectives;
        _tasks = tasks;
    }

    public async Task<decimal> CalculateAsync(Guid tenantId, Objective objective, Guid? excludingTaskId = null, CancellationToken ct = default)
    {
        var children = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, objective.Id, ct);
        var childSum = children.Sum(c => c.AllocatedHours);
        var taskSum = await _tasks.GetActiveAllocationSumByObjectiveIdAsync(tenantId, objective.Id, excludingTaskId, ct);
        return objective.AllocatedHours - childSum - taskSum;
    }
}
```

- [ ] **Step 4: Write the response DTO, command, validator**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/WorkTaskResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    string TaskType, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent);

/// <summary>Returned alongside a 409 slack-conflict so the frontend can offer the extend-allocation flow (spec §3.2).</summary>
public sealed record InsufficientAllocationResponse(decimal AvailableSlackHours, string SuggestedAction = "extend_allocation");
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public sealed record CreateTaskCommand(
    Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<WorkTaskResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandValidator.cs
using FluentValidation;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.ObjectiveId).NotEqual(Guid.Empty).WithMessage("Objective is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500).WithMessage("Title is required and must be 500 characters or fewer.");
        RuleFor(x => x.TaskType).Must(t => t is WorkTaskTypes.Task or WorkTaskTypes.Bug or WorkTaskTypes.Story or WorkTaskTypes.Feature)
            .WithMessage("Task type must be task, bug, story, or feature.");
        RuleFor(x => x.Priority).Must(p => p is WorkTaskPriorities.Low or WorkTaskPriorities.Medium or WorkTaskPriorities.High or WorkTaskPriorities.Critical)
            .WithMessage("Priority must be low, medium, high, or critical.");
        RuleFor(x => x.EstimatedHours).GreaterThanOrEqualTo(0).When(x => x.EstimatedHours.HasValue)
            .WithMessage("Estimated hours must not be negative.");
    }
}
```

- [ ] **Step 5: Write the handler** — `ShortId` generation reuses `projects.next_task_number`/`identifier` exactly as `phase1-table-inventory.md`'s `projects` table already documents (`identifier` + atomically-incremented `next_task_number`); do not invent a second ID scheme. For this task, fetch the objective's `Project` via `IObjectiveRepository` → need the project's `Identifier`/`NextTaskNumber` — add a minimal read through the existing `IProjectRepository` (already present in the codebase; do not create a new one).

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public class CreateTaskCommandHandler : IRequestHandler<CreateTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives,
        IWorkTaskRepository tasks, IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _tasks = tasks;
        _slack = slack;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(CreateTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return Result<WorkTaskResponse>.NotFound("Objective not found.");

        if (objective.OwnerId != callerEmployeeId.Value)
            return Result<WorkTaskResponse>.Forbidden("Only this milestone's owner can create tasks directly. Non-owner members must submit a task creation request.");

        if (request.EstimatedHours.HasValue)
        {
            var slack = await _slack.CalculateAsync(tenantId, objective, ct: ct);
            if (request.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    System.Text.Json.JsonSerializer.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var now = DateTimeOffset.UtcNow;
            var task = new WorkTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = objective.Id,
                ShortId = $"TASK-{Guid.NewGuid():N}".Substring(0, 12), // see Task 6 follow-up note below re: real project-prefixed numbering
                Title = request.Title.Trim(), Description = request.Description?.Trim(),
                TaskType = request.TaskType, Priority = request.Priority, DueDate = request.DueDate,
                EstimatedHours = request.EstimatedHours, StoryPoints = request.StoryPoints,
                CompletedHours = 0m, ProgressPercent = 0, CreatedById = userId, CreatedAt = now
            };
            // StatusId: the task's initial column. Task 10 (task-status auto-copy) must run before this
            // handler can resolve a real default status id - wire the actual lookup there; this task's
            // scope is allocation enforcement + persistence, not status resolution.

            await _tasks.AddAsync(task, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.TaskType, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent));
        }, ct);
    }
}
```

**Follow-up note for the implementer:** the `ShortId` line above is a placeholder-free but temporary value (a GUID fragment) so this task's tests pass in isolation — Task 9 (below) replaces it with the real `projects.identifier` + atomically-incremented `next_task_number` scheme once `IProjectRepository`'s exact method names are confirmed by reading `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs` at that point (not read during this plan's research pass — confirm before Task 9). Similarly, `StatusId` is left as `Guid.Empty`-default here; Task 10 wires the real default-status lookup and this handler gets a one-line update to call it. Do not skip either follow-up — flag both explicitly when reporting Task 6 complete.

- [ ] **Step 6: Register command/handler/validator, slack calculator in DI (MediatR handlers are typically auto-registered via assembly scanning in this codebase — confirm by checking `services.AddMediatR(...)` in `DependencyInjection.cs`; if validators are also auto-registered via `AddValidatorsFromAssembly`, no manual registration needed for those two. Manually register the slack calculator:**

```csharp
services.AddScoped<IObjectiveAllocationSlackCalculator, ObjectiveAllocationSlackCalculator>();
```

- [ ] **Step 7: Run tests, verify PASS.**

Run: `dotnet test --filter CreateTaskCommandHandlerTests`

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/ src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
git commit -m "feat(work): CreateTask command with slack-based allocation enforcement"
```

### Task 7: `GetObjectiveTasks` query — Board (grouped) and Backlog (flat) via one endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTasksQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetByObjectiveIdAsync`, `WorkTaskResponse` (Task 6).
- Produces: `GetObjectiveTasksQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>`. Grouping by status for the Board view is a frontend concern (spec Part frontend §2) — this query always returns the flat list; no server-side `view` branching needed since grouping-by-`StatusId` from a flat list is trivial client-side and keeps this one query serving both tabs, per spec §5.

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetObjectiveTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ReturnsAllTasksForObjective()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "A", ShortId = "T-1", CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "B", ShortId = "T-2", CreatedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetObjectiveTasksQueryHandler(currentUser.Object, tasks.Object);
        var result = await handler.Handle(new GetObjectiveTasksQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write query + handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;

public sealed record GetObjectiveTasksQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<WorkTaskResponse>>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTasks/GetObjectiveTasksQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;

public class GetObjectiveTasksQueryHandler : IRequestHandler<GetObjectiveTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;

    public GetObjectiveTasksQueryHandler(ICurrentUser currentUser, IWorkTaskRepository tasks)
    {
        _currentUser = currentUser;
        _tasks = tasks;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetObjectiveTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Authentication required.");

        var items = await _tasks.GetByObjectiveIdAsync(_currentUser.TenantId, request.ObjectiveId, ct);
        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.TaskType, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent)).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
```

- [ ] **Step 4: Run test, verify PASS. Step 5: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTasksQueryHandlerTests.cs
git commit -m "feat(work): GetObjectiveTasks query for Board/Backlog"
```

### Task 8: `EditTask` command (re-runs slack check) and `MoveTaskStatus` command

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/{EditTaskCommand,EditTaskCommandHandler,EditTaskCommandValidator}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/{MoveTaskStatusCommand,MoveTaskStatusCommandHandler}.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetTrackedByIdForTenantAsync`, `IObjectiveAllocationSlackCalculator` (Task 6).
- Produces: `EditTaskCommand(Guid TaskId, string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints) : IRequest<Result<WorkTaskResponse>>`, `MoveTaskStatusCommand(Guid TaskId, Guid NewStatusId) : IRequest<Result>`.

- [ ] **Step 1: Write the failing tests** (one per handler — happy path for `MoveTaskStatus`; happy path + slack-exceeded for `EditTask`, mirroring Task 6's test structure exactly, using `GetTrackedByIdForTenantAsync` instead of `AddAsync` as the mutation-verification point, and `GetActiveAllocationSumByObjectiveIdAsync(..., excludingTaskId: task.Id, ...)` in the mock setup so the task's own current allocation is excluded from the sum per Task 3's repository doc-comment).

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (EditTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks) Build(decimal allocatedHours, decimal existingSumExcludingThisTask)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "Old", ShortId = "T-1", EstimatedHours = 10m, CreatedAt = DateTimeOffset.UtcNow };

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSumExcludingThisTask);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var slack = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new EditTaskCommandHandler(currentUser.Object, tasks.Object, objectives.Object, slack, unitOfWork.Object);
        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_IncreaseWithinSlack_Updates()
    {
        // allocated 100, other tasks 40 -> slack 60; new estimate 50 fits.
        var (handler, tasks) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 50m, StoryPoints: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
    }

    [Fact]
    public async Task Handle_IncreaseExceedsSlack_ReturnsConflict()
    {
        var (handler, _) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 70m, StoryPoints: null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests, verify FAIL.**

- [ ] **Step 3: Write `EditTaskCommand`/`Validator`/`Handler`** (validator mirrors `CreateTaskCommandValidator` minus the `ObjectiveId`/`TaskType` rules, which don't change on edit):

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public sealed record EditTaskCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<WorkTaskResponse>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/EditTaskCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public class EditTaskCommandHandler : IRequestHandler<EditTaskCommand, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly IObjectiveRepository _objectives;
    private readonly IObjectiveAllocationSlackCalculator _slack;
    private readonly IUnitOfWork _unitOfWork;

    public EditTaskCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, IObjectiveRepository objectives,
        IObjectiveAllocationSlackCalculator slack, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _objectives = objectives;
        _slack = slack;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkTaskResponse>> Handle(EditTaskCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        if (request.EstimatedHours.HasValue && request.EstimatedHours.Value != task.EstimatedHours)
        {
            var objective = await _objectives.GetByIdForTenantAsync(tenantId, task.ObjectiveId, ct);
            if (objective is null)
                return Result<WorkTaskResponse>.NotFound("Objective not found.");

            var slack = await _slack.CalculateAsync(tenantId, objective, excludingTaskId: task.Id, ct: ct);
            if (request.EstimatedHours.Value > slack)
                return Result<WorkTaskResponse>.Conflict(
                    System.Text.Json.JsonSerializer.Serialize(new InsufficientAllocationResponse(slack)));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.Title = request.Title.Trim();
            task.Description = request.Description?.Trim();
            task.Priority = request.Priority;
            task.DueDate = request.DueDate;
            task.EstimatedHours = request.EstimatedHours;
            task.StoryPoints = request.StoryPoints;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);

            return Result<WorkTaskResponse>.Success(new WorkTaskResponse(
                task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
                task.TaskType, task.StatusId, task.Priority, task.StoryPoints,
                task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent));
        }, ct);
    }
}
```

- [ ] **Step 4: Write `MoveTaskStatusCommand`/`Handler`** — per spec §5, unconditional in this slice (no `task_approvals` bypass logic yet):

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public sealed record MoveTaskStatusCommand(Guid TaskId, Guid NewStatusId) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/MoveTaskStatusCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public class MoveTaskStatusCommandHandler : IRequestHandler<MoveTaskStatusCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public MoveTaskStatusCommandHandler(
        ICurrentUser currentUser, IWorkTaskRepository tasks, ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _tasks = tasks;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MoveTaskStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result.NotFound("Task not found.");

        var newStatus = await _statuses.GetByIdForTenantAsync(tenantId, request.NewStatusId, ct);
        if (newStatus is null)
            return Result.NotFound("Target status not found.");

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            task.StatusId = newStatus.Id;
            if (newStatus.MarksTaskComplete)
            {
                task.CompletedAt = DateTimeOffset.UtcNow;
                task.ProgressPercent = 100;
            }
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);
    }
}
```

- [ ] **Step 5: Write the `MoveTaskStatus` happy-path test**, run both test files, verify PASS. **Step 6: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTask/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/MoveTaskStatus/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/MoveTaskStatusCommandHandlerTests.cs
git commit -m "feat(work): EditTask (slack re-check) and MoveTaskStatus commands"
```

### Task 9: Real `ShortId` generation — replace Task 6's placeholder

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommand.cs` (no field changes expected, but reconfirm after reading `IProjectRepository`)

**Interfaces:**
- Consumes: `IProjectRepository` (read its actual file — `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs` — before writing this task; it almost certainly already exposes the atomic `next_task_number` increment used by the existing Project `identifier` design, since `phase1-table-inventory.md`'s `projects.next_task_number` column comment says "atomically incremented when creating a task" — find and reuse that existing increment method, do not add a second one).

- [ ] **Step 1: Read `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs` in full and identify the exact method name for atomically incrementing `next_task_number` (or, if no such method exists yet despite the column comment implying one should, that is a real gap — in that case add `Task<int> IncrementAndGetNextTaskNumberAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)` to `IProjectRepository` and its EF-backed implementation, using a single atomic SQL `UPDATE ... SET next_task_number = next_task_number + 1 ... RETURNING next_task_number` via `ExecuteSqlInterpolatedAsync`, to avoid a read-then-write race under concurrent task creation).**

- [ ] **Step 2: Update `CreateTaskCommandHandler` to inject `IProjectRepository` and replace the placeholder `ShortId` line:**

```csharp
var taskNumber = await _projects.IncrementAndGetNextTaskNumberAsync(tenantId, objective.ProjectId, innerCt);
var shortId = $"{project.Identifier}-{taskNumber}";
```

(Fetch `project` via the same repository, keyed by `objective.ProjectId`, before entering the transaction — mirroring how `objective` is already fetched pre-transaction in Task 6.)

- [ ] **Step 3: Add a test asserting the generated `ShortId` matches `{identifier}-{number}` format, using a mocked `IProjectRepository.IncrementAndGetNextTaskNumberAsync` returning a fixed number.**

- [ ] **Step 4: Run `CreateTaskCommandHandlerTests`, verify PASS (including the new assertion and the two pre-existing tests from Task 6, which must still pass unmodified). Step 5: Commit.**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/ src/ONEVO.Application/Features/WorkManagement/Projects/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommandHandlerTests.cs
git commit -m "feat(work): real project-prefixed ShortId generation for tasks"
```

### Task 10: Task-status template copy-on-first-access + `EditTaskStatus`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/{GetObjectiveTaskStatusesQuery,GetObjectiveTaskStatusesQueryHandler}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskStatus/{EditTaskStatusCommand,EditTaskStatusCommandHandler}.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs` (resolve real default `StatusId`, replacing Task 6's `Guid.Empty` follow-up note)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTaskStatusesQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetObjectiveTaskStatusesQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>` — auto-copies the Project template into Objective-scoped rows on first call if none exist yet. `EditTaskStatusCommand(Guid StatusId, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId) : IRequest<Result>`, Objective-owner-only (spec §5).

- [ ] **Step 1: Write the failing test — the auto-copy behavior is the interesting case**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetObjectiveTaskStatusesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_NoObjectiveStatusesYet_CopiesFromProjectTemplate()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatus>()); // none yet
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatus>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", DisplayOrder = 2, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow }
            });

        var unitOfWork = new Mock<Application.Common.RepositoryInterfaces.IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var handler = new GetObjectiveTaskStatusesQueryHandler(currentUser.Object, objectives.Object, statuses.Object, unitOfWork.Object);
        var result = await handler.Handle(new GetObjectiveTaskStatusesQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        statuses.Verify(x => x.AddRangeAsync(It.Is<IReadOnlyList<TaskStatus>>(list => list.Count == 4 && list.All(s => s.ObjectiveId == ObjectiveId)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run test, verify FAIL.**

- [ ] **Step 3: Write `TaskStatusResponse` DTO, query, handler.** The four seed statuses (To Do / In Process / Review / Done) themselves are seeded onto the **Project template** at Project-creation time — that seeding hook belongs in the existing `CreateProjectCommandHandler` (a one-line addition: after creating the Project, `AddRangeAsync` four `TaskStatus` rows with `ObjectiveId = null`). Add that hook in this task too, since `GetProjectTemplateAsync` returning empty for a Project created before this feature shipped is an acceptable, documented gap (not retroactively backfilled) but every **new** Project must get real defaults.

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskStatusResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskStatusResponse(Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete);
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/GetObjectiveTaskStatusesQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

public sealed record GetObjectiveTaskStatusesQuery(Guid ObjectiveId) : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetObjectiveTaskStatuses/GetObjectiveTaskStatusesQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

public class GetObjectiveTaskStatusesQueryHandler : IRequestHandler<GetObjectiveTaskStatusesQuery, Result<IReadOnlyList<TaskStatusResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveRepository _objectives;
    private readonly ITaskStatusRepository _statuses;
    private readonly IUnitOfWork _unitOfWork;

    public GetObjectiveTaskStatusesQueryHandler(
        ICurrentUser currentUser, IObjectiveRepository objectives, ITaskStatusRepository statuses, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _objectives = objectives;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<TaskStatusResponse>>> Handle(GetObjectiveTaskStatusesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskStatusResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<IReadOnlyList<TaskStatusResponse>>.NotFound("Objective not found.");

        var existing = await _statuses.GetByObjectiveIdAsync(tenantId, request.ObjectiveId, ct);
        if (existing.Count > 0)
            return Result<IReadOnlyList<TaskStatusResponse>>.Success(ToResponses(existing));

        var template = await _statuses.GetProjectTemplateAsync(tenantId, objective.ProjectId, ct);
        var now = DateTimeOffset.UtcNow;
        var copies = template.Select(t => new TaskStatus
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = objective.ProjectId, ObjectiveId = request.ObjectiveId,
            Name = t.Name, DisplayOrder = t.DisplayOrder, RequiresApproval = t.RequiresApproval,
            MarksTaskComplete = t.MarksTaskComplete, CreatedById = _currentUser.UserId, CreatedAt = now
        }).ToList();

        if (copies.Count == 0)
            return Result<IReadOnlyList<TaskStatusResponse>>.Success(Array.Empty<TaskStatusResponse>());

        await _statuses.AddRangeAsync(copies, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<IReadOnlyList<TaskStatusResponse>>.Success(ToResponses(copies));
    }

    private static IReadOnlyList<TaskStatusResponse> ToResponses(IReadOnlyList<TaskStatus> statuses)
        => statuses.OrderBy(s => s.DisplayOrder)
            .Select(s => new TaskStatusResponse(s.Id, s.Name, s.DisplayOrder, s.RequiresApproval, s.ApproverId, s.MarksTaskComplete))
            .ToList();
}
```

- [ ] **Step 4: In `CreateTaskCommandHandler` (Task 6/9), replace the `StatusId` follow-up note: resolve the default status via `ITaskStatusRepository.GetByObjectiveIdAsync` filtered to `MarksTaskComplete == false` ordered by `DisplayOrder`, first row — inject `ITaskStatusRepository` and call it before entering the transaction, returning `Result<WorkTaskResponse>.Failure("No task statuses configured for this milestone yet.", 422)` if empty (should not happen once Step 3's Project-creation hook is live, but this is a real user-facing case for pre-existing Projects).**

- [ ] **Step 5: Write `EditTaskStatusCommand`/`Handler`, Objective-owner-only** (same owner check pattern as `CreateTaskCommandHandler`'s `objective.OwnerId != callerEmployeeId.Value` branch).

- [ ] **Step 6: Add the four-status seeding hook to `CreateProjectCommandHandler`** (find the file via `Glob **/CreateProjectCommandHandler.cs`, read it, add the `AddRangeAsync` call inside its existing transaction block, right after the Project entity is added — do not open a second transaction).

- [ ] **Step 7: Run all Task 10 tests, verify PASS. Also re-run `CreateTaskCommandHandlerTests` (Tasks 6/9) since Step 4 changed that handler — verify still PASS.**

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/ src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetObjectiveTaskStatusesQueryHandlerTests.cs
git commit -m "feat(work): task-status template copy-on-first-access, default status wiring, EditTaskStatus"
```

### Task 11: Task assignment add/remove + Controller wiring for all of Part 1

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/{AssignTaskCommand,AssignTaskCommandHandler}.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/UnassignTask/{UnassignTaskCommand,UnassignTaskCommandHandler}.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/*.cs` (Request/ViewModel + Mapper, mirroring `src/ONEVO.Api/Contracts/WorkManagement/Objectives/*`)
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs`
- Create: `docs/postman-request/Work Management/Create Task.md`, `Get Objective Tasks.md`, `Get Objective Task Statuses.md`, `Edit Task.md`, `Move Task Status.md`, `Assign Task.md`, `Unassign Task.md`, `Edit Task Status.md` (per `PROCESS_RULES.md` rule 6 — one file per endpoint, sections: method+route, auth/permission line, description, request body example, response body example, error-status table, Source section)

**Interfaces:**
- Consumes: everything from Tasks 6-10.
- Produces: `AssignTaskCommand(Guid TaskId, Guid EmployeeId) : IRequest<Result>`, `UnassignTaskCommand(Guid TaskId, Guid EmployeeId) : IRequest<Result>`, and the full `TasksController` route surface: `POST /api/v1/work/objectives/{objectiveId}/tasks`, `GET /api/v1/work/objectives/{objectiveId}/tasks`, `GET /api/v1/work/objectives/{objectiveId}/task-statuses`, `PATCH /api/v1/work/objectives/{objectiveId}/task-statuses/{id}`, `PATCH /api/v1/work/tasks/{id}`, `PATCH /api/v1/work/tasks/{id}/status`, `POST /api/v1/work/tasks/{id}/assignments`, `DELETE /api/v1/work/tasks/{id}/assignments/{employeeId}`.

- [ ] **Step 1: Write `AssignTaskCommand`/`Handler` and `UnassignTaskCommand`/`Handler`** — straightforward `ITaskAssignmentRepository.AddAsync`/`Remove`, resolving `EmployeeId → UserId` the same way `AddObjectiveMemberCommandHandler` already does (read that file for the exact lookup call before writing this — likely via `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync`, which already returns an `Employee` with `.UserId`).

- [ ] **Step 2: Write matching unit tests for both handlers (happy path + "task not found" + "employee not active"), following the exact Moq setup pattern from Tasks 6-8's tests.**

- [ ] **Step 3: Read `src/ONEVO.Api/Contracts/WorkManagement/Objectives/CreateObjectiveRequest.cs` and its Mapper file for the exact Contracts-layer pattern, then write the Tasks equivalents: `CreateTaskRequest`, `EditTaskRequest`, `MoveTaskStatusRequest`, `AssignTaskRequest`, `EditTaskStatusRequest` records plus `WorkTaskViewModel`/`TaskStatusViewModel` + `.ToViewModel()` extension methods on the corresponding Response DTOs (Tasks 6, 10).**

- [ ] **Step 4: Write `TasksController`**, following `ObjectivesController`'s exact `Result → IActionResult` mapping convention (`Problem(result.Error, statusCode: result.StatusCode ?? 400)` on failure):

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
public class TasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public TasksController(IMediator mediator) => _mediator = mediator;

    [HttpPost("objectives/{objectiveId:guid}/tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid objectiveId, [FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateTaskCommand(
            objectiveId, request.Title, request.Description, request.TaskType, request.Priority,
            request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/tasks")]
    public async Task<IActionResult> GetByObjective(Guid objectiveId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTasksQuery(objectiveId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(t => t.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("objectives/{objectiveId:guid}/task-statuses")]
    public async Task<IActionResult> GetStatuses(Guid objectiveId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetObjectiveTaskStatusesQuery(objectiveId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(s => s.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("objectives/{objectiveId:guid}/task-statuses/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> EditStatus(Guid objectiveId, Guid id, [FromBody] EditTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskStatusCommand(id, request.Name, request.DisplayOrder, request.RequiresApproval, request.ApproverId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("tasks/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new EditTaskCommand(id, request.Title, request.Description, request.Priority, request.DueDate, request.EstimatedHours, request.StoryPoints), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("tasks/{id:guid}/status")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> MoveStatus(Guid id, [FromBody] MoveTaskStatusRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new MoveTaskStatusCommand(id, request.NewStatusId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("tasks/{id:guid}/assignments")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AssignTaskCommand(id, request.EmployeeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("tasks/{id:guid}/assignments/{employeeId:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Unassign(Guid id, Guid employeeId, CancellationToken ct)
    {
        var result = await _mediator.Send(new UnassignTaskCommand(id, employeeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 5: Write the 8 Postman-doc markdown files listed under Files above, following the exact section order `PROCESS_RULES.md` rule 6 requires (method+route / auth-permission-idempotency line / description / request body example / response body example / error-status table / Source section linking the controller+handler files and this plan).**

- [ ] **Step 6: Update `docs/postman-request/README.md`'s Work Management module index to list the 8 new files (per the existing convention noted in `plans/SUMMARY.md`'s "Open items" — keep this index in sync going forward, don't let it go stale like it did 2026-08-08→09).**

- [ ] **Step 7: Run the full Work Management unit test suite**

Run: `dotnet test --filter FullyQualifiedName~WorkManagement`
Expected: all PASS, including every test from Tasks 2-11.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AssignTask/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/UnassignTask/ src/ONEVO.Api/Contracts/WorkManagement/Tasks/ src/ONEVO.Api/Controllers/Tenant/WorkManagement/TasksController.cs docs/postman-request/Work\ Management/ docs/postman-request/README.md tests/
git commit -m "feat(work): TasksController, assignment commands, Postman docs for Task Foundation Part 1"
```

## Part 1 complete

All of spec §5's endpoints exist, the slack invariant (§3.1) is enforced on create/edit, and `task_creation_requests`/`extend_allocation`/Notification/`my-deadlines` remain for Parts 2-5. Update `docs/superpowers/plans/next/SUMMARY.md` and `docs/superpowers/plans/SUMMARY.md` to list this file as `pending` (this whole multi-part plan moves to `finished` only once Part 5 also finishes) before moving to Part 2.
