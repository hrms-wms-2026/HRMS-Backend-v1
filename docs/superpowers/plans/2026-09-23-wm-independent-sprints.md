# Independent Sprints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Sprint a project-level "bunch of tasks" (no Module), with a module→task tree picker for sprint creation, owner-gated task assignment, a full sprint activity log, and a Tree/Sprint view switch in the Event (CalendarEvent) modal.

**Architecture:** Backend keeps the build green at every task: new pieces (activity log table, `ISprintAccessService`, `ISprintTaskAssignmentService`) land first, then every handler is moved off `Sprint.ObjectiveId`, and only the last backend task deletes the column. Frontend first swaps the model (`objectiveId` → `projectId` + `canManage`), then extracts a reusable `ModuleTaskPickerComponent` from the event modal and uses it in the sprint form and the event modal's two views.

**Tech Stack:** .NET 10 / MediatR / FluentValidation / EF Core (Npgsql, snake_case) / xUnit + Moq; Angular 21 standalone + `@ngrx/signals`, Vitest.

**Spec:** `HRMS-Backend-v1/docs/superpowers/specs/2026-09-23-wm-independent-sprints-design.md` — read it first; decisions are referenced as D1–D8.

## Global Constraints

- Work Management module only. Do not touch other modules' code; do not kill processes you did not start (a running `ONEVO.Api` may lock `bin/Debug` — build with `--configuration Release` if so).
- Every sprint-mutating handler: resolve caller via `ICallerIdentityResolver.ResolveCallerEmployeeIdAsync`, gate via `ISprintAccessService.CanManageAsync` (Task A2), wrap writes in `IUnitOfWork.ExecuteInTransactionAsync` + `SaveChangesAsync`, and write exactly one `SprintActivityLog` row per action (Task A1).
- Task ownership gate for assignment is **`IMilestoneMembershipCoordinator.IsEffectiveOwnerAsync`** (owner of module or any ancestor) — NOT `IsEffectiveManagerAsync`.
- Backend tests: xUnit + Moq, plain `Assert.*`. Test files live in `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/` (or the existing folder of the handler under test).
- Backend commands (run from `HRMS-Backend-v1` root):
  - narrow: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~<Name>"`
  - architecture: `dotnet test tests/ONEVO.Tests.Architecture`
  - If `dotnet build` fails with NuGet.targets "path1 null": run `dotnet build-server shutdown` and retry (stale MSBuild server, not code).
  - If you use `-p:BuildProjectReferences=false`, first `dotnet build src/ONEVO.Application` or new Application code will not be seen.
- Migrations: `dotnet ef migrations add <Name> --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release`. If design-time factory needs a connection string, set a dummy env var `ConnectionStrings__MigrationConnection="Host=localhost;Database=x;Username=x;Password=x"`. **Always open the generated migration and the `ApplicationDbContextModelSnapshot.cs` diff before committing** — if the snapshot diff touches unrelated modules, stop (stale-checkout snapshot corruption) and regenerate from a clean tree.
- New tenant-owned table ⇒ `tenant_isolation` RLS block in the same migration using the `TenantTables` + `foreach` convention (copy from `20260913152103_AddWorkModesRlsPolicyCoverage.cs`). Architecture test `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` must stay green.
- Frontend tests: Vitest; run `npx vitest run <path>` from `Hrms--Web-application---front-end---v1` root. Full: `npx vitest run`. Build: `npx ng build`.
- Frontend UI copy: user-facing word is "Module" (not Objective), "Milestone" (not Event), "Sprint".
- **Shared working tree:** before every commit run `git status` and stage only files the task touched. Never `git add -A`.
- Commit message trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Map

**Backend (new)**
- `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs` — entity + `SprintActivityActions` constants
- `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintActivityLogRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintActivityLogRepository.cs`
- `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintActivityLogConfiguration.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintAccessService.cs` + `SprintAccessService.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintTaskAssignmentService.cs` + `SprintTaskAssignmentService.cs`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintActivityLogFactory.cs` — tiny static builder
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/SetSprintTasks/*`
- `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetSprintActivity/*`
- Migrations: `AddSprintActivityLogs`, `DropSprintObjectiveId`

**Frontend (new)**
- `src/app/modules/work/ui/module-task-picker/module-task-picker.component.ts` (+ spec)
- `src/app/modules/work/utils/module-task-selection.ts` (+ spec) — pure selection helpers shared by picker + event modal
- `src/app/modules/work/ui/sprint-history/sprint-history.component.ts` (+ spec)
- `src/app/modules/work/ui/sprint-task-picker/sprint-task-picker.component.ts` (+ spec) — the event modal's Sprint view

---

## Part A — Backend (`HRMS-Backend-v1`)

### Task A1: `SprintActivityLog` entity, table, repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintActivityLogRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintActivityLogFactory.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintActivityLogConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintActivityLogRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (DbSet next to `Sprints`, ~line 308)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (next to `EfSprintRepository`, ~line 356)
- Create (generated): `src/ONEVO.Infrastructure/Migrations/<ts>_AddSprintActivityLogs.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintActivityLogFactoryTests.cs`

**Interfaces — Produces:**
- `SprintActivityLog : BaseEntity { Guid SprintId; Guid EmployeeId; string Action; string? FromStatus; string? ToStatus; string? DetailsJson; DateTimeOffset OccurredAt }`
- `SprintActivityActions.Created|Edited|Started|Completed|Achieved|TasksAdded|TasksRemoved` (strings `"created"`, `"edited"`, `"started"`, `"completed"`, `"achieved"`, `"tasks_added"`, `"tasks_removed"`)
- `ISprintActivityLogRepository.AddAsync(SprintActivityLog, ct)`, `GetForSprintAsync(Guid tenantId, Guid sprintId, ct) → IReadOnlyList<SprintActivityLog>` (oldest first)
- `SprintActivityLogFactory.Create(Guid tenantId, Guid sprintId, Guid employeeId, string action, string? fromStatus = null, string? toStatus = null, object? details = null) → SprintActivityLog`

- [x] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintActivityLogFactoryTests.cs
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintActivityLogFactoryTests
{
    [Fact]
    public void Create_FillsIdentityStatusAndSerializesDetails()
    {
        var tenantId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var log = SprintActivityLogFactory.Create(tenantId, sprintId, employeeId, SprintActivityActions.Started,
            SprintStatuses.Draft, SprintStatuses.Active, new { taskIds = new[] { taskId } });

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(sprintId, log.SprintId);
        Assert.Equal(employeeId, log.EmployeeId);
        Assert.Equal("started", log.Action);
        Assert.Equal("draft", log.FromStatus);
        Assert.Equal("active", log.ToStatus);
        Assert.Contains(taskId.ToString(), log.DetailsJson);
    }

    [Fact]
    public void Create_NoDetails_LeavesDetailsJsonNull()
    {
        var log = SprintActivityLogFactory.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), SprintActivityActions.Created);
        Assert.Null(log.DetailsJson);
        Assert.Null(log.FromStatus);
    }
}
```

- [x] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintActivityLogFactoryTests"`
Expected: build error — `SprintActivityLogFactory` / `SprintActivityActions` not found.

- [x] **Step 3: Implement entity, factory, repository interface**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintActivityActions
{
    public const string Created = "created";
    public const string Edited = "edited";
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Achieved = "achieved";
    public const string TasksAdded = "tasks_added";
    public const string TasksRemoved = "tasks_removed";
}

/// <summary>Audit row for every sprint action (spec D6). Who/when/what - never updated or deleted.</summary>
public class SprintActivityLog : BaseEntity
{
    public Guid SprintId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintActivityLogFactory.cs
using System.Text.Json;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public static class SprintActivityLogFactory
{
    public static SprintActivityLog Create(
        Guid tenantId, Guid sprintId, Guid employeeId, string action,
        string? fromStatus = null, string? toStatus = null, object? details = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SprintActivityLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SprintId = sprintId, EmployeeId = employeeId,
            Action = action, FromStatus = fromStatus, ToStatus = toStatus,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            OccurredAt = now, CreatedAt = now
        };
    }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintActivityLogRepository.cs
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;

public interface ISprintActivityLogRepository
{
    Task AddAsync(SprintActivityLog log, CancellationToken ct = default);
    Task<IReadOnlyList<SprintActivityLog>> GetForSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
```

- [x] **Step 4: Run the test — PASS**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintActivityLogFactoryTests"` → 2 passed.

- [x] **Step 5: EF configuration, repository, DbSet, DI**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintActivityLogConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class SprintActivityLogConfiguration : IEntityTypeConfiguration<SprintActivityLog>
{
    public void Configure(EntityTypeBuilder<SprintActivityLog> builder)
    {
        builder.ToTable("sprint_activity_logs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Action).HasMaxLength(30).IsRequired();
        builder.Property(log => log.FromStatus).HasMaxLength(20);
        builder.Property(log => log.ToStatus).HasMaxLength(20);

        builder.HasIndex(log => new { log.TenantId, log.SprintId, log.OccurredAt })
            .HasDatabaseName("ix_sprint_activity_logs_tenant_id_sprint_id_occurred_at");

        builder.HasOne<Sprint>().WithMany().HasForeignKey(log => log.SprintId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintActivityLogRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfSprintActivityLogRepository : ISprintActivityLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfSprintActivityLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(SprintActivityLog log, CancellationToken ct = default)
        => await _db.SprintActivityLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<SprintActivityLog>> GetForSprintAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
        => await _db.SprintActivityLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.SprintId == sprintId)
            .OrderBy(log => log.OccurredAt)
            .ToListAsync(ct);
}
```

`ApplicationDbContext.cs`, directly below `public DbSet<Sprint> Sprints => Set<Sprint>();`:
```csharp
    public DbSet<SprintActivityLog> SprintActivityLogs => Set<SprintActivityLog>();
```

`DependencyInjection.cs`, directly below the two `EfSprintRepository` lines:
```csharp
        services.AddScoped<EfSprintActivityLogRepository>();
        services.AddScoped<ISprintActivityLogRepository>(sp => sp.GetRequiredService<EfSprintActivityLogRepository>());
```

- [x] **Step 6: Generate migration and add RLS**

Run: `dotnet ef migrations add AddSprintActivityLogs --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release`

Open the generated `<ts>_AddSprintActivityLogs.cs`. Verify `Up` only creates `sprint_activity_logs` (+ index + FK) and the snapshot diff only adds that entity. Then add to the class, above `Up`:
```csharp
        private static readonly string[] TenantTables =
        [
            "sprint_activity_logs"
        ];
```
and at the **end** of `Up` (after `CreateIndex`) paste the `foreach (var table in TenantTables) { migrationBuilder.Sql($@" ALTER TABLE ... CREATE POLICY tenant_isolation ... "); }` block verbatim from `20260913152103_AddWorkModesRlsPolicyCoverage.cs` `Up`, and at the **start** of `Down` (before `DropTable`) the `DROP POLICY IF EXISTS` foreach block from that file's `Down`.

- [x] **Step 7: Build + architecture suite**

Run: `dotnet build src/ONEVO.Api --configuration Release` → succeeds.
Run: `dotnet test tests/ONEVO.Tests.Architecture` → all pass (RLS coverage includes `sprint_activity_logs`).

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/SprintActivityLog.cs src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintActivityLogRepository.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintActivityLogFactory.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintActivityLogConfiguration.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintActivityLogRepository.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintActivityLogFactoryTests.cs
git commit -m "Add sprint_activity_logs table for sprint audit trail"
```

---

### Task A2: `ISprintAccessService` (who can manage a sprint, who hears about it)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintAccessService.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintAccessService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (next to `IMilestoneMembershipCoordinator` registration, ~line 416)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintAccessServiceTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetBySprintIdAsync(tenantId, sprintId, ct)`, `IWorkTaskRepository.GetByProjectAsync(tenantId, projectId, ct)`, `IMilestoneMembershipCoordinator.IsEffectiveOwnerAsync(tenantId, objectiveId, employeeId, ct)`, `IProjectMemberRepository.ListActiveForObjectiveAsync(tenantId, objectiveId, ct)`.
- Produces:
```csharp
public interface ISprintAccessService
{
    Task<bool> CanManageAsync(Guid tenantId, Sprint sprint, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);
    Task<IReadOnlySet<Guid>> GetManageableSprintIdsAsync(Guid tenantId, Guid projectId, IReadOnlyList<Sprint> sprints, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
```

- [x] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintAccessServiceTests.cs
using Moq;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintAccessServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CreatorUserId = Guid.NewGuid();
    private static readonly Guid CallerUserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid ModuleA = Guid.NewGuid();
    private static readonly Guid ModuleB = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IProjectMemberRepository> _members = new();

    private SprintAccessService Build() => new(_tasks.Object, _membership.Object, _members.Object);

    private static Sprint NewSprint() => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S", CreatedById = CreatorUserId
    };

    private static WorkTask NewTask(Guid sprintId, Guid objectiveId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId, SprintId = sprintId
    };

    [Fact]
    public async Task CanManage_Creator_True_WithoutTaskLookup()
    {
        var sprint = NewSprint();
        var result = await Build().CanManageAsync(TenantId, sprint, CreatorUserId, CallerEmployeeId);
        Assert.True(result);
        _tasks.Verify(x => x.GetBySprintIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CanManage_OwnerOfAnyTaskModule_True()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA), NewTask(sprint.Id, ModuleB) });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleB, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task CanManage_NotCreatorAndOwnsNoTaskModule_False()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA) });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.False(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task CanManage_EmptySprint_NotCreator_False()
    {
        var sprint = NewSprint();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkTask>());
        Assert.False(await Build().CanManageAsync(TenantId, sprint, CallerUserId, CallerEmployeeId));
    }

    [Fact]
    public async Task GetManageableSprintIds_BatchesOwnershipPerModule()
    {
        var mine = NewSprint();
        var owned = NewSprint();
        owned.CreatedById = Guid.NewGuid();
        var foreign = NewSprint();
        foreign.CreatedById = Guid.NewGuid();
        mine.CreatedById = CallerUserId;

        _tasks.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                NewTask(owned.Id, ModuleA), NewTask(owned.Id, ModuleA), NewTask(foreign.Id, ModuleB)
            });
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ModuleB, CallerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ids = await Build().GetManageableSprintIdsAsync(TenantId, ProjectId, new List<Sprint> { mine, owned, foreign }, CallerUserId, CallerEmployeeId);

        Assert.Contains(mine.Id, ids);
        Assert.Contains(owned.Id, ids);
        Assert.DoesNotContain(foreign.Id, ids);
        _membership.Verify(x => x.IsEffectiveOwnerAsync(TenantId, ModuleA, CallerEmployeeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAudience_DistinctActiveMembersAcrossTaskModules()
    {
        var sprint = NewSprint();
        var shared = Guid.NewGuid();
        var onlyB = Guid.NewGuid();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask> { NewTask(sprint.Id, ModuleA), NewTask(sprint.Id, ModuleB) });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared } });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared }, new() { EmployeeId = onlyB } });

        var audience = await Build().GetAudienceEmployeeIdsAsync(TenantId, sprint.Id);

        Assert.Equal(2, audience.Count);
        Assert.Contains(shared, audience);
        Assert.Contains(onlyB, audience);
    }
}
```

> Before typing: confirm the exact namespaces of `IProjectMemberRepository` and `ProjectMember` (`grep -rn "interface IProjectMemberRepository\|class ProjectMember\b" src`) and of `IMilestoneMembershipCoordinator` (`Objectives/Services`) and adjust the `using`s. Remove any unused `using`.

- [x] **Step 2: Run — FAIL** (`SprintAccessService` not found)

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintAccessServiceTests"`

- [x] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintAccessService.cs
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>
/// Spec D5: a sprint can be managed (edit/start/complete/achieve) by its creator, or by anyone who
/// effectively owns (IsEffectiveOwnerAsync - own or ancestor) the Module of any task currently in it.
/// </summary>
public interface ISprintAccessService
{
    Task<bool> CanManageAsync(Guid tenantId, Sprint sprint, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetManageableSprintIdsAsync(
        Guid tenantId, Guid projectId, IReadOnlyList<Sprint> sprints, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default);

    /// <summary>Distinct active members of every Module that has a task in this sprint.</summary>
    Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintAccessService.cs
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public class SprintAccessService : ISprintAccessService
{
    private readonly IWorkTaskRepository _tasks;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IProjectMemberRepository _members;

    public SprintAccessService(IWorkTaskRepository tasks, IMilestoneMembershipCoordinator membership, IProjectMemberRepository members)
    {
        _tasks = tasks;
        _membership = membership;
        _members = members;
    }

    public async Task<bool> CanManageAsync(Guid tenantId, Sprint sprint, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default)
    {
        if (sprint.CreatedById == callerUserId) return true;

        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprint.Id, ct);
        foreach (var objectiveId in tasks.Select(t => t.ObjectiveId).Distinct())
        {
            if (await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct))
                return true;
        }
        return false;
    }

    public async Task<IReadOnlySet<Guid>> GetManageableSprintIdsAsync(
        Guid tenantId, Guid projectId, IReadOnlyList<Sprint> sprints, Guid callerUserId, Guid callerEmployeeId, CancellationToken ct = default)
    {
        var result = new HashSet<Guid>(sprints.Where(s => s.CreatedById == callerUserId).Select(s => s.Id));
        var pending = sprints.Where(s => !result.Contains(s.Id)).Select(s => s.Id).ToHashSet();
        if (pending.Count == 0) return result;

        var projectTasks = await _tasks.GetByProjectAsync(tenantId, projectId, ct);
        var ownership = new Dictionary<Guid, bool>();
        foreach (var group in projectTasks.Where(t => t.SprintId is not null && pending.Contains(t.SprintId.Value)).GroupBy(t => t.SprintId!.Value))
        {
            foreach (var objectiveId in group.Select(t => t.ObjectiveId).Distinct())
            {
                if (!ownership.TryGetValue(objectiveId, out var owns))
                {
                    owns = await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct);
                    ownership[objectiveId] = owns;
                }
                if (owns) { result.Add(group.Key); break; }
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetAudienceEmployeeIdsAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
    {
        var tasks = await _tasks.GetBySprintIdAsync(tenantId, sprintId, ct);
        var audience = new HashSet<Guid>();
        foreach (var objectiveId in tasks.Select(t => t.ObjectiveId).Distinct())
        {
            var members = await _members.ListActiveForObjectiveAsync(tenantId, objectiveId, ct);
            foreach (var member in members) audience.Add(member.EmployeeId);
        }
        return audience.ToList();
    }
}
```

DI (`DependencyInjection.cs`, below `services.AddScoped<IMilestoneMembershipCoordinator, MilestoneMembershipCoordinator>();`):
```csharp
        services.AddScoped<ISprintAccessService, SprintAccessService>();
```
(add `using ONEVO.Application.Features.WorkManagement.Sprints.Services;` at the top if missing.)

- [x] **Step 4: Run — PASS** (6 tests)

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintAccessService.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintAccessService.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintAccessServiceTests.cs
git commit -m "Add SprintAccessService: creator or task-module owner manages a sprint"
```

---

### Task A3: `ISprintTaskAssignmentService` (validate + apply task moves into/out of a sprint)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintTaskAssignmentService.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintTaskAssignmentService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (below the A2 line)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintTaskAssignmentServiceTests.cs`

**Interfaces:**
- Consumes: `IWorkTaskRepository.GetTrackedByIdForTenantAsync`, `IWorkTaskRepository.Update`, `ISprintRepository.GetByIdForTenantAsync`, `IMilestoneMembershipCoordinator.IsEffectiveOwnerAsync`.
- Produces:
```csharp
public sealed record SprintTaskChangeSet(IReadOnlyList<WorkTask> ToAdd, IReadOnlyList<WorkTask> ToRemove)
{
    public static readonly SprintTaskChangeSet Empty = new(Array.Empty<WorkTask>(), Array.Empty<WorkTask>());
    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0;
}
public interface ISprintTaskAssignmentService
{
    Task<Result<SprintTaskChangeSet>> PrepareAsync(Guid tenantId, Sprint sprint, IReadOnlyCollection<Guid> addTaskIds, IReadOnlyCollection<Guid> removeTaskIds, Guid callerEmployeeId, CancellationToken ct = default);
    void Apply(SprintTaskChangeSet changes, Guid sprintId);
}
```

Rules (spec D2/D4), in `PrepareAsync`:
1. `sprint.Status` must be Draft or Active, else `Conflict("Tasks can only be added to or removed from a Draft or Active sprint.")`.
2. For each add id (distinct): task must exist and `task.ProjectId == sprint.ProjectId`, else `NotFound("Task {id} not found in this project.")`. Skip (no-op) if `task.SprintId == sprint.Id`. If task's current sprint exists and is `Achieved` → `Forbidden("Task {shortId} is in an achieved sprint and is frozen.")`. Caller must `IsEffectiveOwnerAsync(task.ObjectiveId)` → else `Forbidden("You can only add tasks from modules you own.")`.
3. For each remove id (distinct, not also in add): task must exist with `task.SprintId == sprint.Id`, else skip silently. Caller must own its module → else `Forbidden("You can only remove tasks from modules you own.")`.
4. `Apply`: `ToAdd` → `SprintId = sprintId`; `ToRemove` → `SprintId = null`; set `UpdatedAt`, call `_tasks.Update(task)`.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintTaskAssignmentServiceTests.cs
using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SprintTaskAssignmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid OwnedModule = Guid.NewGuid();
    private static readonly Guid ForeignModule = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();

    public SprintTaskAssignmentServiceTests()
    {
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, OwnedModule, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _membership.Setup(x => x.IsEffectiveOwnerAsync(TenantId, ForeignModule, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private SprintTaskAssignmentService Build() => new(_tasks.Object, _sprints.Object, _membership.Object);

    private static Sprint NewSprint(string status = SprintStatuses.Draft) =>
        new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "S", Status = status };

    private WorkTask Seed(Guid objectiveId, Guid? sprintId = null, Guid? projectId = null)
    {
        var task = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = projectId ?? ProjectId, ObjectiveId = objectiveId, SprintId = sprintId, ShortId = "T-1" };
        _tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        return task;
    }

    [Fact]
    public async Task Add_OwnedBacklogTask_IsInChangeSet_AndApplySetsSprint()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ToAdd);
        Build().Apply(result.Value!, sprint.Id);
        Assert.Equal(sprint.Id, task.SprintId);
    }

    [Fact]
    public async Task Add_TaskFromOtherSprint_MovesIt()
    {
        var sprint = NewSprint();
        var other = NewSprint(SprintStatuses.Active);
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);
        var task = Seed(OwnedModule, other.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Build().Apply(result.Value!, sprint.Id);

        Assert.Equal(sprint.Id, task.SprintId);
    }

    [Fact]
    public async Task Add_TaskInAchievedSprint_Forbidden()
    {
        var sprint = NewSprint();
        var achieved = NewSprint(SprintStatuses.Achieved);
        _sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, achieved.Id, It.IsAny<CancellationToken>())).ReturnsAsync(achieved);
        var task = Seed(OwnedModule, achieved.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, new[] { task.Id }, Array.Empty<Guid>(), Caller);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Add_TaskFromModuleNotOwned_Forbidden()
    {
        var task = Seed(ForeignModule);
        var result = await Build().PrepareAsync(TenantId, NewSprint(), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Add_TaskFromOtherProject_NotFound()
    {
        var task = Seed(OwnedModule, projectId: Guid.NewGuid());
        var result = await Build().PrepareAsync(TenantId, NewSprint(), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task EndedSprint_Conflict(string status)
    {
        var task = Seed(OwnedModule);
        var result = await Build().PrepareAsync(TenantId, NewSprint(status), new[] { task.Id }, Array.Empty<Guid>(), Caller);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Remove_OwnedTaskInSprint_ClearsSprint()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule, sprint.Id);

        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Build().Apply(result.Value!, sprint.Id);

        Assert.Null(task.SprintId);
    }

    [Fact]
    public async Task Remove_TaskFromModuleNotOwned_Forbidden()
    {
        var sprint = NewSprint();
        var task = Seed(ForeignModule, sprint.Id);
        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Remove_TaskNotInThisSprint_IsIgnored()
    {
        var sprint = NewSprint();
        var task = Seed(OwnedModule, Guid.NewGuid());
        var result = await Build().PrepareAsync(TenantId, sprint, Array.Empty<Guid>(), new[] { task.Id }, Caller);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEmpty);
    }
}
```

- [x] **Step 2: Run — FAIL**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintTaskAssignmentServiceTests"`

- [x] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintTaskAssignmentService.cs
using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public sealed record SprintTaskChangeSet(IReadOnlyList<WorkTask> ToAdd, IReadOnlyList<WorkTask> ToRemove)
{
    public static readonly SprintTaskChangeSet Empty = new(Array.Empty<WorkTask>(), Array.Empty<WorkTask>());
    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0;
}

/// <summary>Spec D2/D4. PrepareAsync validates and returns tracked tasks (no mutation); Apply
/// mutates them - call Apply inside the caller's ExecuteInTransactionAsync.</summary>
public interface ISprintTaskAssignmentService
{
    Task<Result<SprintTaskChangeSet>> PrepareAsync(
        Guid tenantId, Sprint sprint, IReadOnlyCollection<Guid> addTaskIds, IReadOnlyCollection<Guid> removeTaskIds,
        Guid callerEmployeeId, CancellationToken ct = default);

    void Apply(SprintTaskChangeSet changes, Guid sprintId);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintTaskAssignmentService.cs
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

public class SprintTaskAssignmentService : ISprintTaskAssignmentService
{
    private readonly IWorkTaskRepository _tasks;
    private readonly ISprintRepository _sprints;
    private readonly IMilestoneMembershipCoordinator _membership;

    public SprintTaskAssignmentService(IWorkTaskRepository tasks, ISprintRepository sprints, IMilestoneMembershipCoordinator membership)
    {
        _tasks = tasks;
        _sprints = sprints;
        _membership = membership;
    }

    public async Task<Result<SprintTaskChangeSet>> PrepareAsync(
        Guid tenantId, Sprint sprint, IReadOnlyCollection<Guid> addTaskIds, IReadOnlyCollection<Guid> removeTaskIds,
        Guid callerEmployeeId, CancellationToken ct = default)
    {
        var addIds = addTaskIds.Distinct().ToList();
        var removeIds = removeTaskIds.Distinct().Where(id => !addIds.Contains(id)).ToList();
        if (addIds.Count == 0 && removeIds.Count == 0)
            return Result<SprintTaskChangeSet>.Success(SprintTaskChangeSet.Empty);

        if (sprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
            return Result<SprintTaskChangeSet>.Conflict("Tasks can only be added to or removed from a Draft or Active sprint.");

        var ownership = new Dictionary<Guid, bool>();
        async Task<bool> OwnsAsync(Guid objectiveId)
        {
            if (!ownership.TryGetValue(objectiveId, out var owns))
            {
                owns = await _membership.IsEffectiveOwnerAsync(tenantId, objectiveId, callerEmployeeId, ct);
                ownership[objectiveId] = owns;
            }
            return owns;
        }

        var toAdd = new List<WorkTask>();
        foreach (var id in addIds)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, id, ct);
            if (task is null || task.ProjectId != sprint.ProjectId)
                return Result<SprintTaskChangeSet>.NotFound($"Task {id} not found in this project.");
            if (task.SprintId == sprint.Id) continue;
            if (task.SprintId is not null)
            {
                var current = await _sprints.GetByIdForTenantAsync(tenantId, task.SprintId.Value, ct);
                if (current is not null && current.Status == SprintStatuses.Achieved)
                    return Result<SprintTaskChangeSet>.Forbidden($"Task {task.ShortId} is in an achieved sprint and is frozen.");
            }
            if (!await OwnsAsync(task.ObjectiveId))
                return Result<SprintTaskChangeSet>.Forbidden("You can only add tasks from modules you own.");
            toAdd.Add(task);
        }

        var toRemove = new List<WorkTask>();
        foreach (var id in removeIds)
        {
            var task = await _tasks.GetTrackedByIdForTenantAsync(tenantId, id, ct);
            if (task is null || task.SprintId != sprint.Id) continue;
            if (!await OwnsAsync(task.ObjectiveId))
                return Result<SprintTaskChangeSet>.Forbidden("You can only remove tasks from modules you own.");
            toRemove.Add(task);
        }

        return Result<SprintTaskChangeSet>.Success(new SprintTaskChangeSet(toAdd, toRemove));
    }

    public void Apply(SprintTaskChangeSet changes, Guid sprintId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var task in changes.ToAdd)
        {
            task.SprintId = sprintId;
            task.UpdatedAt = now;
            _tasks.Update(task);
        }
        foreach (var task in changes.ToRemove)
        {
            task.SprintId = null;
            task.UpdatedAt = now;
            _tasks.Update(task);
        }
    }
}
```

DI: `services.AddScoped<ISprintTaskAssignmentService, SprintTaskAssignmentService>();` below the A2 line.

- [x] **Step 4: Run — PASS** (10 tests)

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Services/ISprintTaskAssignmentService.cs src/ONEVO.Application/Features/WorkManagement/Sprints/Services/SprintTaskAssignmentService.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintTaskAssignmentServiceTests.cs
git commit -m "Add SprintTaskAssignmentService enforcing owner-only, one-sprint task moves"
```

---

### Task A4: New `SprintResponse` shape + project/objective sprint list queries

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintResponse.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs`
- Modify: every `new SprintResponse(` call site (`grep -rn "new SprintResponse(" src`): Create/Edit/Start/Complete/Achieve handlers + both query handlers
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetProjectSprints/GetProjectSprintsQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetObjectiveSprints/GetObjectiveSprintsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/GetProjectSprintsQueryHandlerTests.cs`, `GetObjectiveSprintsQueryHandlerTests.cs` (update + add)

**Interfaces — Produces:**
- `SprintResponse(Guid Id, Guid ProjectId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate, string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt, bool CanManage)`
- `SprintResponse.From(Sprint s, bool canManage)` static helper — use it at every call site from now on.
- `SprintViewModel` mirrors it (`ProjectId`, `CanManage`), `ObjectiveId` removed from the wire.
- `ISprintRepository.GetContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, bool activeOnly, CancellationToken ct)` — sprints having ≥1 task with `ObjectiveId == objectiveId`.
- `ISprintRepository.GetByProjectAsync` now filters `s.ProjectId == projectId` directly (no Objective join).

- [x] **Step 1: Update the response + view model**

```csharp
// SprintResponse.cs
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintResponse(
    Guid Id, Guid ProjectId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt, bool CanManage)
{
    public static SprintResponse From(Sprint s, bool canManage) => new(
        s.Id, s.ProjectId, s.Name, s.Goal, s.StartDate, s.EndDate, s.Status, s.CompletedAt, s.AchievedAt, canManage);
}
```

```csharp
// SprintContracts.cs - replace SprintViewModel + mapper
public sealed record SprintViewModel(
    Guid Id, Guid ProjectId, string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string Status, DateTimeOffset? CompletedAt, DateTimeOffset? AchievedAt, bool CanManage);

public static class SprintViewModelMapper
{
    public static SprintViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintResponse dto) =>
        new(dto.Id, dto.ProjectId, dto.Name, dto.Goal, dto.StartDate, dto.EndDate, dto.Status, dto.CompletedAt, dto.AchievedAt, dto.CanManage);
}
```

In each **command** handler (Create/Edit/Start/Complete/Achieve) replace the `new SprintResponse(sprint.Id, sprint.ObjectiveId, ...)` expression with `SprintResponse.From(sprint, canManage: true)` (the caller just passed the manage gate). Leave their gates untouched in this task.

- [x] **Step 2: Repository methods**

`ISprintRepository` — add:
```csharp
    /// <summary>Sprints holding at least one task of this Module (spec §3.3 - the Tree tab's leaf expansion).</summary>
    Task<IReadOnlyList<Sprint>> GetContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, bool activeOnly, CancellationToken ct = default);
```

`EfSprintRepository` — replace `GetByProjectAsync` and add the new method:
```csharp
    public async Task<IReadOnlyList<Sprint>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ProjectId == projectId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, bool activeOnly, CancellationToken ct = default)
    {
        var sprintIds = _db.WorkTasks
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.SprintId != null)
            .Select(t => t.SprintId!.Value);
        return await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && sprintIds.Contains(s.Id)
                        && (!activeOnly || s.Status == SprintStatuses.Active))
            .ToListAsync(ct);
    }
```
> Check the DbSet name for `WorkTask` in `ApplicationDbContext.cs` (`grep -n "DbSet<WorkTask>" src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`) and use it. Also check whether `WorkTask` rows are soft-deleted via `IsDeleted`; if a global query filter does not already exclude them, add `&& !t.IsDeleted`.

- [x] **Step 3: Write failing query tests**

Rewrite `GetProjectSprintsQueryHandlerTests.cs` around the new constructor (Step 4 below). Required cases:
1. `projects:read` caller → all project sprints, `CanManage` from `GetManageableSprintIdsAsync`.
2. Non-`projects:read` caller with **active project membership** (`IProjectMemberRepository.HasActiveMembershipAsync(tenantId, projectId, employeeId)` → true) → all project sprints (no per-module filtering).
3. Non-`projects:read` caller without membership → 403.
4. Project missing/inactive → 404.

Example for case 2:
```csharp
    [Fact]
    public async Task Member_SeesAllProjectSprints_WithCanManageFlags()
    {
        var mine = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "A", Status = SprintStatuses.Draft };
        var other = new Sprint { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "B", Status = SprintStatuses.Active };
        _sprints.Setup(x => x.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Sprint> { mine, other });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
        _members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _access.Setup(x => x.GetManageableSprintIdsAsync(TenantId, ProjectId, It.IsAny<IReadOnlyList<Sprint>>(), UserId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { mine.Id });

        var result = await Build().Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.True(result.Value.Single(s => s.Id == mine.Id).CanManage);
        Assert.False(result.Value.Single(s => s.Id == other.Id).CanManage);
    }
```
> Match the existing test file's fixture style (`Build()` helper, `Mock<ICurrentUser>` etc.) and the exact return type of `IPermissionResolver.ResolveAsync` (check the interface; use that collection type in `ReturnsAsync`).

Rewrite `GetObjectiveSprintsQueryHandlerTests.cs`: keep the existing access-check cases (they still walk ancestors for module access), and change the data assertion so the handler calls `GetContainingObjectiveTasksAsync(tenantId, objectiveId, activeOnly)` and returns `CanManage` from `GetManageableSprintIdsAsync(tenantId, objective.ProjectId, …)`.

- [x] **Step 4: Implement the query handlers**

`GetProjectSprintsQueryHandler`: add `ISprintAccessService _access` to the constructor. Replace the `accessibleObjectiveIds` block and the projection with:
```csharp
        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<SprintResponse>>.Forbidden("You do not have access to this project.");

        var sprints = await _sprints.GetByProjectAsync(tenantId, project.Id, ct);
        var manageable = await _access.GetManageableSprintIdsAsync(tenantId, project.Id, sprints, userId, callerEmployeeId.Value, ct);

        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => SprintResponse.From(s, manageable.Contains(s.Id))).ToList());
```

`GetObjectiveSprintsQueryHandler`: add `ISprintAccessService _access`; keep the access walk; replace the final fetch/projection with:
```csharp
        var sprints = await _sprints.GetContainingObjectiveTasksAsync(tenantId, request.ObjectiveId, request.ActiveOnly, ct);
        var manageable = await _access.GetManageableSprintIdsAsync(tenantId, objective.ProjectId, sprints, userId, callerEmployeeId.Value, ct);
        return Result<IReadOnlyList<SprintResponse>>.Success(
            sprints.Select(s => SprintResponse.From(s, manageable.Contains(s.Id))).ToList());
```

- [x] **Step 5: Run the sprint test folder — PASS**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement.Sprints"`
Fix any other test fixture that constructed `SprintResponse` positionally (switch to `SprintResponse.From`). Also `grep -rn "SprintViewModel(" tests src` and fix.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints
git commit -m "Sprint response carries ProjectId + CanManage; project members see all project sprints"
```

---

### Task A5: `CreateSprint` becomes project-scoped with optional tasks

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint/CreateSprintCommand.cs`
- Modify: `.../CreateSprint/CreateSprintCommandValidator.cs`
- Modify: `.../CreateSprint/CreateSprintCommandHandler.cs`
- Modify: `src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs` (`CreateSprintRequest`)
- Modify: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs` (`Create` route)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs` (rewrite)

**Interfaces:**
- Consumes: `ISprintTaskAssignmentService` (A3), `ISprintActivityLogRepository` (A1), `IProjectRepository.GetByIdForTenantAsync`, `IProjectMemberRepository.HasActiveMembershipAsync`, `IPermissionResolver.ResolveAsync`.
- Produces: `CreateSprintCommand(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds)`; `CreateSprintRequest(string Name, string? Goal, IReadOnlyList<Guid>? TaskIds)`; route `POST api/v1/work/projects/{projectId}/sprints`.

- [x] **Step 1: Write the failing tests** (replace the file's cases)

Required cases (use the StartSprint test file's fixture style):
1. Active project member, no tasks → 201-equivalent success, sprint `ProjectId` set, `Status == Draft`, `CreatedById == UserId`, exactly one `created` log added (`_logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.Created), ...), Times.Once)`), assignment `PrepareAsync` called with empty lists.
2. With `TaskIds` → `PrepareAsync(tenantId, It.IsAny<Sprint>(), taskIds, empty, employeeId)` called; `Apply` called; a second log `tasks_added` with the ids in `DetailsJson`.
3. `PrepareAsync` returns `Forbidden` → handler returns 403 and `_sprints.AddAsync` never called.
4. Not a member and no `projects:read` → 403.
5. `projects:read` permission, not a member → success.
6. Project not found → 404.

```csharp
    [Fact]
    public async Task Handle_WithTasks_AssignsAndLogsTasksAdded()
    {
        var taskIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var changes = new SprintTaskChangeSet(
            taskIds.Select(id => new WorkTask { Id = id, TenantId = TenantId, ProjectId = ProjectId }).ToList(),
            Array.Empty<WorkTask>());
        _assignment.Setup(x => x.PrepareAsync(TenantId, It.IsAny<Sprint>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(changes));

        var result = await Build().Handle(new CreateSprintCommand(ProjectId, "Sprint 1", null, taskIds), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignment.Verify(x => x.Apply(changes, result.Value!.Id), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksAdded && l.DetailsJson!.Contains(taskIds[0].ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [x] **Step 2: Run — FAIL**

- [x] **Step 3: Implement**

```csharp
// CreateSprintCommand.cs
public sealed record CreateSprintCommand(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds) : IRequest<Result<SprintResponse>>;
```

Validator: replace the `ObjectiveId` rule with `RuleFor(x => x.ProjectId).NotEqual(Guid.Empty).WithMessage("Project is required.");` and add `RuleFor(x => x.TaskIds).NotNull();`.

Handler (full replacement of `Handle` and constructor; keep the namespace/usings style):
```csharp
public class CreateSprintCommandHandler : IRequestHandler<CreateSprintCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintRepository _sprints;
    private readonly ISprintTaskAssignmentService _assignment;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver, ISprintRepository sprints,
        ISprintTaskAssignmentService assignment, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _sprints = sprints;
        _assignment = assignment;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<SprintResponse>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only project members can create sprints.");

        var now = DateTimeOffset.UtcNow;
        var sprint = new Sprint
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = project.Id,
            Name = request.Name.Trim(), Goal = request.Goal?.Trim(),
            Status = SprintStatuses.Draft, CreatedById = userId, CreatedAt = now
        };

        var prepared = await _assignment.PrepareAsync(tenantId, sprint, request.TaskIds, Array.Empty<Guid>(), callerEmployeeId.Value, ct);
        if (!prepared.IsSuccess)
            return Result<SprintResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);
        var changes = prepared.Value!;

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await _sprints.AddAsync(sprint, innerCt);
            _assignment.Apply(changes, sprint.Id);
            await _logs.AddAsync(SprintActivityLogFactory.Create(
                tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Created, toStatus: SprintStatuses.Draft), innerCt);
            if (changes.ToAdd.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(
                    tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.TasksAdded,
                    details: new { taskIds = changes.ToAdd.Select(t => t.Id) }), innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage: true));
        }, ct);
    }
}
```
> `Result<T>.Failure(error, statusCode)` preserves 403/404/409 from `PrepareAsync`. Verify the `IProjectRepository` / `IPermissionResolver` namespaces from `GetProjectSprintsQueryHandler.cs`'s `using`s and copy them.

Contract: `public sealed record CreateSprintRequest(string Name, string? Goal, IReadOnlyList<Guid>? TaskIds);`

Controller — replace the `Create` action:
```csharp
    [HttpPost("projects/{projectId:guid}/sprints")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateSprintRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateSprintCommand(projectId, request.Name, request.Goal, request.TaskIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? StatusCode(201, result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [x] **Step 4: Run — PASS**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateSprintCommandHandlerTests"` and then `dotnet build src/ONEVO.Api --configuration Release`. If any integration/controller test posts to `objectives/{id}/sprints`, update it to `projects/{projectId}/sprints` (`grep -rn "objectives/.*sprints" tests`).

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/CreateSprint src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/CreateSprintCommandHandlerTests.cs
git commit -m "Create sprints at project level with optional owned tasks"
```

---

### Task A6: `SetSprintTasks` command + `PUT /sprints/{id}/tasks`

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/SetSprintTasks/SetSprintTasksCommand.cs`
- Create: `.../SetSprintTasks/SetSprintTasksCommandHandler.cs`
- Modify: `SprintContracts.cs` (+ `SetSprintTasksRequest`), `SprintsController.cs` (+ action)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SetSprintTasksCommandHandlerTests.cs`

**Interfaces — Produces:** `SetSprintTasksCommand(Guid SprintId, IReadOnlyList<Guid> AddTaskIds, IReadOnlyList<Guid> RemoveTaskIds) : IRequest<Result<SprintResponse>>`; `SetSprintTasksRequest(IReadOnlyList<Guid>? AddTaskIds, IReadOnlyList<Guid>? RemoveTaskIds)`.

Rule: no `CanManage` gate here — per D2 the per-task module-ownership check in `PrepareAsync` IS the gate (a module owner may put their tasks into anyone's sprint). The returned `CanManage` is computed with `ISprintAccessService.CanManageAsync` **after** the change.

- [x] **Step 1: Failing tests** — cases: (1) sprint not found → 404; (2) prepare forbidden → 403, no `SaveChangesAsync`; (3) adds + removes → `Apply` once, one `tasks_added` log and one `tasks_removed` log; (4) empty change set → success, no logs, no save.

```csharp
    [Fact]
    public async Task Handle_AddAndRemove_LogsBoth()
    {
        var added = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId };
        var removed = new WorkTask { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId };
        var changes = new SprintTaskChangeSet(new[] { added }, new[] { removed });
        _assignment.Setup(x => x.PrepareAsync(TenantId, _sprint, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SprintTaskChangeSet>.Success(changes));

        var result = await Build().Handle(new SetSprintTasksCommand(SprintId, new[] { added.Id }, new[] { removed.Id }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assignment.Verify(x => x.Apply(changes, SprintId), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksAdded), It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksRemoved), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [x] **Step 2: Run — FAIL**

- [x] **Step 3: Implement**

```csharp
// SetSprintTasksCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;

public sealed record SetSprintTasksCommand(Guid SprintId, IReadOnlyList<Guid> AddTaskIds, IReadOnlyList<Guid> RemoveTaskIds) : IRequest<Result<SprintResponse>>;
```

```csharp
// SetSprintTasksCommandHandler.cs  (usings: copy from CreateSprintCommandHandler + Sprints.Services)
namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;

public class SetSprintTasksCommandHandler : IRequestHandler<SetSprintTasksCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly ISprintTaskAssignmentService _assignment;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public SetSprintTasksCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ISprintRepository sprints,
        ISprintTaskAssignmentService assignment, ISprintAccessService access, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _assignment = assignment;
        _access = access;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(SetSprintTasksCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var prepared = await _assignment.PrepareAsync(tenantId, sprint, request.AddTaskIds, request.RemoveTaskIds, callerEmployeeId.Value, ct);
        if (!prepared.IsSuccess)
            return Result<SprintResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);
        var changes = prepared.Value!;

        if (changes.IsEmpty)
            return Result<SprintResponse>.Success(SprintResponse.From(sprint,
                await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct)));

        await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _assignment.Apply(changes, sprint.Id);
            if (changes.ToAdd.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(tenantId, sprint.Id, callerEmployeeId.Value,
                    SprintActivityActions.TasksAdded, details: new { taskIds = changes.ToAdd.Select(t => t.Id) }), innerCt);
            if (changes.ToRemove.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(tenantId, sprint.Id, callerEmployeeId.Value,
                    SprintActivityActions.TasksRemoved, details: new { taskIds = changes.ToRemove.Select(t => t.Id) }), innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result.Success();
        }, ct);

        var canManage = await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct);
        return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage));
    }
}
```
> Check `IUnitOfWork.ExecuteInTransactionAsync`'s overloads (`grep -n "ExecuteInTransactionAsync" src/ONEVO.Application/Common/RepositoryInterfaces/IUnitOfWork.cs`). If it has no non-generic `Result` overload, return `Result<SprintResponse>` from the lambda instead (compute `canManage` inside the transaction after `SaveChangesAsync`) and mock that generic in tests.

Contract: `public sealed record SetSprintTasksRequest(IReadOnlyList<Guid>? AddTaskIds, IReadOnlyList<Guid>? RemoveTaskIds);`

Controller:
```csharp
    [HttpPut("sprints/{id:guid}/tasks")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> SetTasks(Guid id, [FromBody] SetSprintTasksRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new SetSprintTasksCommand(id, request.AddTaskIds ?? Array.Empty<Guid>(), request.RemoveTaskIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [x] **Step 4: Run — PASS**; `dotnet build src/ONEVO.Api --configuration Release`.

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands/SetSprintTasks src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SetSprintTasksCommandHandlerTests.cs
git commit -m "Add PUT /sprints/{id}/tasks for bulk add/remove of owned tasks"
```

---

### Task A7: Edit / Start / Complete / Achieve use `CanManageAsync`, log, and notify the task-module audience

**Files:**
- Modify: `Sprints/Commands/EditSprint/EditSprintCommandHandler.cs`
- Modify: `Sprints/Commands/StartSprint/StartSprintCommandHandler.cs`
- Modify: `Sprints/Commands/CompleteSprint/CompleteSprintCommandHandler.cs`
- Modify: `Sprints/Commands/AchieveSprint/AchieveSprintCommandHandler.cs`
- Test: the four matching `*CommandHandlerTests.cs` files

**Interfaces — Consumes:** `ISprintAccessService.CanManageAsync`, `ISprintAccessService.GetAudienceEmployeeIdsAsync`, `ISprintActivityLogRepository.AddAsync`, `IProjectRepository.GetByIdForTenantAsync` (project name for notifications).

Common edit for all four handlers:
1. Remove the `IObjectiveRepository _objectives` dependency and the `objective` lookup.
2. Inject `ISprintAccessService _access` and `ISprintActivityLogRepository _logs` (constructor params appended in that order before `IUnitOfWork` is fine — just keep tests in sync).
3. Replace the gate with:
```csharp
        if (!await _access.CanManageAsync(tenantId, sprint, _currentUser.UserId, callerEmployeeId.Value, ct))
            return Result<SprintResponse>.Forbidden("Only the sprint's creator or an owner of one of its tasks' modules can <verb> this sprint.");
```
(`<verb>` = edit / start / complete / achieve).
4. Inside the transaction, before `SaveChangesAsync`, add one log:
   - Edit: `SprintActivityLogFactory.Create(tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.Edited, details: new { name = sprint.Name, goal = sprint.Goal, startDate = sprint.StartDate, endDate = sprint.EndDate })`
   - Start: `... SprintActivityActions.Started, SprintStatuses.Draft, SprintStatuses.Active, new { startDate = request.StartDate, endDate = request.EndDate })`
   - Complete: capture `var fromStatus = sprint.Status;` before mutation; `... SprintActivityActions.Completed, fromStatus, SprintStatuses.Complete, new { disposition = request.Disposition, targetSprintId = request.TargetSprintId, movedTaskIds })` where `movedTaskIds` is the list of task ids whose `SprintId` the loop changed.
   - Achieve: `var fromStatus = sprint.Status;` → `... SprintActivityActions.Achieved, fromStatus, SprintStatuses.Achieved)`
5. Complete — replace the target-sprint check with:
```csharp
        if (request.Disposition == "sprint")
        {
            var targetSprint = await _sprints.GetByIdForTenantAsync(tenantId, request.TargetSprintId!.Value, ct);
            if (targetSprint is null || targetSprint.ProjectId != sprint.ProjectId || targetSprint.Id == sprint.Id
                || targetSprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result<SprintResponse>.Failure("Target sprint must be another Draft or Active sprint in the same project.", 422);
        }
```
6. Complete + Achieve notifications — replace the `members` loop with:
```csharp
            var project = await _projects.GetByIdForTenantAsync(tenantId, sprint.ProjectId, innerCt);
            var audience = await _access.GetAudienceEmployeeIdsAsync(tenantId, sprint.Id, innerCt);
            foreach (var employeeId in audience)
            {
                var assignee = await _membership.GetActiveAssigneeAsync(tenantId, employeeId, innerCt);
                if (assignee is null) continue;
                await _notifications.SendTemplatedAsync(
                    tenantId, assignee.UserId, "work_sprint_completed",   // "work_sprint_achieved" in Achieve
                    new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = project?.Name ?? "the project" },
                    "sprint", sprint.Id, innerCt);
            }
```
   In **Complete**, compute `audience` **before** the task-disposition loop (moving tasks out would shrink the audience). Remove `IProjectMemberRepository _members` from both if no longer used; keep `IMilestoneMembershipCoordinator _membership` (for `GetActiveAssigneeAsync`); add `IProjectRepository _projects`.
7. Return `SprintResponse.From(sprint, canManage: true)`.

- [x] **Step 1: Update tests first (they will fail to compile / fail)**

In each of the four test files, change the builder: drop `IObjectiveRepository`/objective setup; add
```csharp
        var access = new Mock<ISprintAccessService>();
        access.Setup(x => x.CanManageAsync(TenantId, It.IsAny<Sprint>(), UserId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId == OwnerEmployeeId);
        access.Setup(x => x.GetAudienceEmployeeIdsAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { OwnerEmployeeId });
        var logs = new Mock<ISprintActivityLogRepository>();
```
and sprint fixtures use `ProjectId = ProjectId` instead of `ObjectiveId = ObjectiveId`. Add to each file one test asserting the log, e.g. for Start:
```csharp
    [Fact]
    public async Task Handle_Start_WritesStartedLog()
    {
        var (handler, sprint, logs) = Build(SprintStatuses.Draft);
        await handler.Handle(new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null), CancellationToken.None);
        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.Action == SprintActivityActions.Started && l.FromStatus == SprintStatuses.Draft && l.ToStatus == SprintStatuses.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```
(extend `Build` to return the `logs` mock). Complete: add a test that a target sprint in a **different project** → 422, and one that a target in the **same project but from another module's tasks** is accepted. Complete/Achieve: assert `SendTemplatedAsync` is called once per audience member with `["objectiveName"] = project name`.

- [x] **Step 2: Run — FAIL**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement.Sprints"`

- [x] **Step 3: Implement the four handler changes above**

- [x] **Step 4: Run — PASS** (whole Sprints folder)

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Sprints/Commands tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints
git commit -m "Gate sprint lifecycle by creator/task-module owners and log every action"
```

---

### Task A8: Sprint tasks/activity read access + `GET /sprints/{id}/activity`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSprintTasks/GetSprintTasksQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetSprintActivity/GetSprintActivityQuery.cs`
- Create: `.../GetSprintActivity/GetSprintActivityQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintActivityResponse.cs`
- Modify: `SprintContracts.cs` (+ `SprintActivityViewModel` + mapper), `SprintsController.cs` (+ `GetActivity`)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/GetSprintActivityQueryHandlerTests.cs`; update `GetSprintTasksQueryHandlerTests.cs` (find with `grep -rln "GetSprintTasksQueryHandler" tests`)

**Interfaces — Produces:**
- `SprintActivityResponse(Guid Id, Guid EmployeeId, string Action, string? FromStatus, string? ToStatus, string? DetailsJson, DateTimeOffset OccurredAt)`
- `GetSprintActivityQuery(Guid SprintId) : IRequest<Result<IReadOnlyList<SprintActivityResponse>>>`
- `SprintActivityViewModel` — same fields.

Shared access rule (both handlers): caller has `projects:read`/`*`, **or** `IProjectMemberRepository.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId)`; else 403 `"You do not have access to this project."`.

- [x] **Step 1: Failing tests** — Activity: (1) member → rows in repository order mapped 1:1; (2) non-member without permission → 403; (3) sprint missing → 404. SprintTasks: replace the module-walk cases with member / non-member cases; remove `IObjectiveRepository` from its fixture.

- [x] **Step 2: Run — FAIL**

- [x] **Step 3: Implement**

In `GetSprintTasksQueryHandler`: drop `_objectives`; replace the block from `var objective = ...` through the end of the `if (!hasReadPermission) { ... }` with:
```csharp
        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("You do not have access to this project.");
```

```csharp
// SprintActivityResponse.cs
namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

public sealed record SprintActivityResponse(
    Guid Id, Guid EmployeeId, string Action, string? FromStatus, string? ToStatus, string? DetailsJson, DateTimeOffset OccurredAt);
```

```csharp
// GetSprintActivityQuery.cs
public sealed record GetSprintActivityQuery(Guid SprintId) : IRequest<Result<IReadOnlyList<SprintActivityResponse>>>;
```

`GetSprintActivityQueryHandler` — constructor `(ICurrentUser, ICallerIdentityResolver, ISprintRepository, IProjectMemberRepository, IPermissionResolver, ISprintActivityLogRepository)`; body = the same auth prologue as `GetSprintTasksQueryHandler` (authenticated, tenant, employee, sprint lookup, access rule above), then:
```csharp
        var logs = await _logs.GetForSprintAsync(tenantId, sprint.Id, ct);
        return Result<IReadOnlyList<SprintActivityResponse>>.Success(logs.Select(l => new SprintActivityResponse(
            l.Id, l.EmployeeId, l.Action, l.FromStatus, l.ToStatus, l.DetailsJson, l.OccurredAt)).ToList());
```

Contracts:
```csharp
public sealed record SprintActivityViewModel(
    Guid Id, Guid EmployeeId, string Action, string? FromStatus, string? ToStatus, string? DetailsJson, DateTimeOffset OccurredAt);

public static class SprintActivityViewModelMapper
{
    public static SprintActivityViewModel ToViewModel(this Application.Features.WorkManagement.Sprints.DTOs.Responses.SprintActivityResponse dto) =>
        new(dto.Id, dto.EmployeeId, dto.Action, dto.FromStatus, dto.ToStatus, dto.DetailsJson, dto.OccurredAt);
}
```

Controller:
```csharp
    [HttpGet("sprints/{id:guid}/activity")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetActivity(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSprintActivityQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(a => a.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [x] **Step 4: Run — PASS**

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetSprintTasks src/ONEVO.Application/Features/WorkManagement/Sprints/Queries/GetSprintActivity src/ONEVO.Application/Features/WorkManagement/Sprints/DTOs/Responses/SprintActivityResponse.cs src/ONEVO.Api/Contracts/WorkManagement/Sprints/SprintContracts.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/SprintsController.cs tests/ONEVO.Tests.Unit
git commit -m "Project members can read sprint tasks and sprint activity history"
```

---

### Task A9: Task handlers + Achieve-Module gate stop using `Sprint.ObjectiveId`

**Files:**
- Modify: `Tasks/Commands/EditTask/EditTaskCommandHandler.cs` (~line 90-98 target-sprint check; transaction body)
- Modify: `Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs:109`
- Modify: `Tasks/Commands/CreateTaskCreationRequest/CreateTaskCreationRequestCommandHandler.cs:74`
- Modify: `Tasks/Commands/ApproveTaskCreationRequest/ApproveTaskCreationRequestCommandHandler.cs:108`
- Modify: `Objectives/Commands/AchieveObjective/AchieveObjectiveCommandHandler.cs:75-79`
- Modify: `ISprintRepository` / `EfSprintRepository` (+ `AnyActiveContainingObjectiveTasksAsync`)
- Test: `EditTaskCommandHandlerTests.cs`, `CreateTaskCommandHandlerTests.cs`, `AchieveObjectiveCommandHandlerTests.cs` (locate with `grep -rln "<HandlerName>" tests`)

**Interfaces — Produces:** `ISprintRepository.AnyActiveContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, CancellationToken ct) → Task<bool>`.

Changes:
1. CreateTask / CreateTaskCreationRequest / ApproveTaskCreationRequest: `sprint.ObjectiveId != objective.Id` → `sprint.ProjectId != objective.ProjectId`; also reject ended sprints: `|| sprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active)`. Update the error text to `"Target sprint must be a Draft or Active sprint in the same project."` (keep each file's existing status code).
2. EditTask target check:
```csharp
            if (targetSprint is null || targetSprint.ProjectId != objective.ProjectId)
                return Result<WorkTaskResponse>.Conflict("Target sprint must belong to the same project.");
            if (targetSprint.Status is not (SprintStatuses.Draft or SprintStatuses.Active))
                return Result<WorkTaskResponse>.Forbidden("Tasks can only be moved into a Draft or Active sprint.");
```
   EditTask sprint-move logging: inject `ISprintActivityLogRepository _sprintLogs`; capture `var previousSprintId = task.SprintId;` before the transaction; inside the transaction where `task.SprintId = targetSprint.Id;` is set, add:
```csharp
                await _sprintLogs.AddAsync(SprintActivityLogFactory.Create(tenantId, targetSprint.Id, callerEmployeeId.Value,
                    SprintActivityActions.TasksAdded, details: new { taskIds = new[] { task.Id } }), innerCt);
                if (previousSprintId is not null)
                    await _sprintLogs.AddAsync(SprintActivityLogFactory.Create(tenantId, previousSprintId.Value, callerEmployeeId.Value,
                        SprintActivityActions.TasksRemoved, details: new { taskIds = new[] { task.Id } }), innerCt);
```
3. Repository:
```csharp
    // ISprintRepository
    Task<bool> AnyActiveContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    // EfSprintRepository
    public async Task<bool> AnyActiveContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.WorkTasks.AnyAsync(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.SprintId != null
               && _db.Sprints.Any(s => s.Id == t.SprintId && s.Status == SprintStatuses.Active), ct);
```
   (use the DbSet name confirmed in A4.)
4. AchieveObjective — replace lines 75-79 with:
```csharp
        // A module is blocked while any of its tasks sits in an Active sprint (sprints are project-level now).
        if (await _sprints.AnyActiveContainingObjectiveTasksAsync(tenantId, objective.Id, ct))
            return Result<ObjectiveChangeOutcomeResponse>.Failure("Tasks of this milestone are still in an Active sprint - complete that sprint first.");
```

- [x] **Step 1: Update/add failing tests**
  - EditTask: "target sprint in another project → 409"; "target sprint from another module, same project → success and `SprintId` updated"; "target sprint Complete → 403"; "move logs tasks_added on target and tasks_removed on source" (add `Mock<ISprintActivityLogRepository>` to the fixture's constructor call).
  - CreateTask: "sprint in same project but other module → accepted".
  - AchieveObjective: replace the old `GetByObjectiveIdAsync` setups with `AnyActiveContainingObjectiveTasksAsync` returning true → failure, false → proceeds.

- [x] **Step 2: Run — FAIL**, **Step 3: implement**, **Step 4: Run — PASS**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"`

- [x] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands src/ONEVO.Application/Features/WorkManagement/Objectives/Commands/AchieveObjective src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs tests/ONEVO.Tests.Unit
git commit -m "Tasks may join any Draft/Active sprint in their project; module achieve checks active sprint tasks"
```

---

### Task A10: `SprintLifecycleJob` overdue audience

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs` (~lines 50-60 resolve, 114-126 notify)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs` (only if it exercises the notify path; the pure `ShouldNotifyOverdue` tests need no change)

- [x] **Step 1:** In `RunOnceAsync` replace the `objectives` / `members` resolutions with:
```csharp
        var access = scope.ServiceProvider.GetRequiredService<ISprintAccessService>();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
```
and replace the block from `var objective = await objectives.GetByIdForTenantAsync(...)` to the end of the member `foreach` with:
```csharp
                var project = await projects.GetByIdForTenantAsync(sprint.TenantId, sprint.ProjectId, ct);
                var audience = await access.GetAudienceEmployeeIdsAsync(tenantId, sprint.Id, ct);
                foreach (var employeeId in audience)
                {
                    var assignee = await membership.GetActiveAssigneeAsync(tenantId, employeeId, ct);
                    if (assignee is null) continue;

                    await notifications.SendTemplatedAsync(
                        tenantId, assignee.UserId, "work_sprint_overdue",
                        new Dictionary<string, string> { ["sprintName"] = sprint.Name, ["objectiveName"] = project?.Name ?? "the project" },
                        "sprint", sprint.Id, ct);
                }
```
Fix `using`s (remove unused Objectives/ProjectMember ones, add `Sprints.Services` + `Projects.RepositoryInterfaces`).

- [x] **Step 2:** `dotnet build src/ONEVO.Api --configuration Release`; `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~SprintLifecycleJob"` → PASS.

- [x] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/WorkManagement/SprintLifecycleJob.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Sprints/SprintLifecycleJobTests.cs
git commit -m "Overdue sprint notice goes to members of every task module in the sprint"
```

---

### Task A11: Drop `Sprint.ObjectiveId` (entity, config, repo, column) + full backend verification

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs` (remove `ObjectiveId`, update the summary comment to "A time-boxed, project-level bunch of tasks (spec 2026-09-23)")
- Modify: `SprintConfiguration.cs` — replace the objective index with:
```csharp
        builder.HasIndex(s => new { s.TenantId, s.ProjectId, s.Status })
            .HasDatabaseName("ix_sprints_tenant_id_project_id_status");
```
- Modify: `ISprintRepository` / `EfSprintRepository` — delete `GetByObjectiveIdAsync` and `GetActiveByObjectiveIdAsync`
- Modify: every remaining compile error (`dotnet build` will list them) — expected: test fixtures that set `ObjectiveId = ...` on `Sprint` (`SprintConfigurationTests`, etc.), demo seeders (`grep -rn "new Sprint" src tests`)
- Create (generated): `<ts>_DropSprintObjectiveId.cs`

- [x] **Step 1:** Make the code edits above; `dotnet build src/ONEVO.Api --configuration Release` and fix every error until green. For any seeder creating sprints, just delete the `ObjectiveId = ...` initializer (sprint keeps `ProjectId`).

- [x] **Step 2: Generate migration**

Run: `dotnet ef migrations add DropSprintObjectiveId --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release`

Open it. Expected `Up`: `DropIndex("ix_sprints_tenant_id_objective_id_status")`, `DropColumn("objective_id", "sprints")`, `CreateIndex("ix_sprints_tenant_id_project_id_status")`. Insert as the **first** statement of `Up` (before `DropIndex`):
```csharp
            // Defensive re-sync: project_id was always written at creation, but make sure it matches
            // the owning objective before that link is dropped for good.
            migrationBuilder.Sql(@"
                UPDATE sprints s
                SET project_id = o.project_id
                FROM objectives o
                WHERE s.objective_id = o.id AND s.project_id <> o.project_id;
            ");
```
Verify `Down` re-adds `objective_id` (nullable is acceptable for Down — if codegen made it non-nullable with default `Guid.Empty`, leave it; Down is best-effort). Verify the snapshot diff touches only `Sprint`.

- [x] **Step 3: Full backend verification**

Run: `dotnet test tests/ONEVO.Tests.Unit` → all green.
Run: `dotnet test tests/ONEVO.Tests.Architecture` → all green.
Run: `grep -rn "ObjectiveId" src/ONEVO.Application/Features/WorkManagement/Sprints src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs` → only `GetObjectiveSprints`/`GetContainingObjectiveTasksAsync` parameter names remain (no `sprint.ObjectiveId`).

- [x] **Step 4: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Sprints/Entities/Sprint.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/SprintConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Sprints/RepositoryInterfaces/ISprintRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfSprintRepository.cs src/ONEVO.Infrastructure/Migrations/ tests/
git commit -m "Drop Sprint.ObjectiveId - sprints are project-level"
```
(also stage any seeder file you had to fix — list it explicitly.)

---

## Part B — Frontend (`Hrms--Web-application---front-end---v1`)

All paths below are relative to `src/app/modules/work/` unless stated.

### Task B1: Sprint model/DTO/API/store move to project scope

**Files:**
- Modify: `models/dto/sprint.dto.ts`, `models/sprint.model.ts`, `utils/sprint.mapper.ts`
- Modify: `data-access/sprint-api.service.ts`
- Modify: `state/sprint-list.store.ts` (+ `state/sprint-list.store.spec.ts`)
- Modify: `ui/sprint-start-dialog/sprint-start-dialog.component.ts`, `ui/sprint-complete-dialog/sprint-complete-dialog.component.ts` (drop `objectiveId`/`activeOnly` inputs; add `projectId = input.required<string>()` if not present)
- Modify: every compile error from `objectiveId` on `Sprint`/`SprintDto` — known: `feature/task-backlog/task-backlog.component.ts` (lines ~66-110, 313-370, 505-520), `ui/task-form-modal/task-form-modal.component.ts` (~2605, ~2851-2866), all `*.spec.ts` sprint fixtures

**Interfaces — Produces:**
```ts
// sprint.dto.ts
export interface SprintDto { id: string; projectId: string; name: string; goal: string | null; startDate: string | null; endDate: string | null;
  status: 'draft' | 'active' | 'complete' | 'achieved'; completedAt: string | null; achievedAt: string | null; canManage: boolean; }
export interface CreateSprintRequestDto { name: string; goal?: string; taskIds?: string[]; }
export interface SetSprintTasksRequestDto { addTaskIds: string[]; removeTaskIds: string[]; }
export interface SprintActivityDto { id: string; employeeId: string; action: 'created' | 'edited' | 'started' | 'completed' | 'achieved' | 'tasks_added' | 'tasks_removed';
  fromStatus: string | null; toStatus: string | null; detailsJson: string | null; occurredAt: string; }
// sprint.model.ts: Sprint = same as before but projectId instead of objectiveId, plus canManage: boolean
// SprintApiService
create(projectId: string, request: CreateSprintRequestDto): Observable<SprintDto>   // POST /work/projects/{projectId}/sprints
setTasks(sprintId: string, request: SetSprintTasksRequestDto): Observable<SprintDto> // PUT /work/sprints/{id}/tasks
getActivity(sprintId: string): Observable<SprintActivityDto[]>                        // GET /work/sprints/{id}/activity
getByObjective(objectiveId, activeOnly) — KEEP (Tree tab uses it; semantics changed server-side)
// SprintListStore
create(projectId: string, request: CreateSprintRequestDto): Promise<boolean>
setTasks(sprintId: string, request: SetSprintTasksRequestDto, projectId: string): Promise<boolean>
start(sprintId: string, request: StartSprintRequestDto, projectId: string): Promise<boolean>
edit(sprintId: string, request: EditSprintRequestDto, projectId: string): Promise<boolean>
complete(sprintId: string, request: CompleteSprintRequestDto, projectId: string): Promise<boolean>
achieve(sprintId: string, projectId: string): Promise<boolean>
loadForProject(projectId: string): Promise<void>   // unchanged; the objective-scoped load() is REMOVED
```

- [x] **Step 1: Update the store spec first** (`state/sprint-list.store.spec.ts`): every call to the new signatures; add

```ts
  it('setTasks calls the API then reloads the project sprints', async () => {
    api.setTasks.mockReturnValue(of(sprintDto));
    api.getByProject.mockReturnValue(of([sprintDto]));
    const ok = await store.setTasks('sp-1', { addTaskIds: ['t1'], removeTaskIds: [] }, 'p-1');
    expect(ok).toBe(true);
    expect(api.setTasks).toHaveBeenCalledWith('sp-1', { addTaskIds: ['t1'], removeTaskIds: [] });
    expect(api.getByProject).toHaveBeenCalledWith('p-1');
  });
```
(Match the spec file's existing `api` mock shape; add `setTasks: vi.fn()` to it and `projectId: 'p-1', canManage: true` to its `sprintDto` fixture.)

- [x] **Step 2: Run — FAIL**: `npx vitest run src/app/modules/work/state/sprint-list.store.spec.ts`

- [x] **Step 3: Implement** DTO/model/mapper (`projectId: dto.projectId, canManage: dto.canManage`), API methods above, store:

```ts
    async create(projectId: string, request: CreateSprintRequestDto): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.create(projectId, request));
        await this.loadForProject(projectId);
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to create the sprint.') });
        return false;
      }
    },

    async setTasks(sprintId: string, request: SetSprintTasksRequestDto, projectId: string): Promise<boolean> {
      patchState(store, { error: null });
      try {
        await firstValueFrom(api.setTasks(sprintId, request));
        await this.loadForProject(projectId);
        return true;
      } catch (err) {
        patchState(store, { error: extractError(err, 'Failed to update the sprint tasks.') });
        return false;
      }
    },
```
and rewrite `start/edit/complete/achieve` to `(sprintId, [request,] projectId)` each ending with `await this.loadForProject(projectId);`. Delete `load(objectiveId, activeOnly)`.

- [x] **Step 4: Fix every compile/type error** (`npx ng build` lists them). Minimal, behavior-neutral fixes in this task only:
  - start/complete dialogs: call store with `this.projectId()`; remove `objectiveId`/`activeOnly` inputs; in `task-backlog.component.ts` template pass `[projectId]="projectId()"` and delete `[objectiveId]="sprint.objectiveId"` on `app-sprint-start-dialog` / `app-sprint-complete-dialog` / edit `app-sprint-form`.
  - `task-backlog.component.ts` `[isObjectiveOwner]="objectiveOwners()[sprint.objectiveId] ?? false"` → `[isObjectiveOwner]="sprint.canManage"` (renamed properly in B4).
  - `otherSprintsForCompleting`: drop the `s.objectiveId === target.objectiveId &&` clause.
  - `modalPrefillModuleId`: delete the two `objectiveId` branches (target sprint / expanded sprint); keep the filter fallback.
  - `moveTargets` / `moveBlockedReason`: leave for B4 but make them compile — replace `sprint.objectiveId === objectiveId &&` with nothing (B4 rewrites these).
  - `task-form-modal.component.ts`: `availableSprints` → `this.sprints().filter((s) => s.status === 'draft' || s.status === 'active')`; activeSprints filter → `this.sprints().filter((s) => s.status === 'active')`; delete the "ensure module is inherited" block (`matchingSprint?.objectiveId` …).
  - All spec fixtures: `objectiveId: 'x'` on a `Sprint`/`SprintDto` → `projectId: 'p-1', canManage: true`.
  - `sprint-form.component.ts`: temporarily call `this.store.create(this.projectId(), {...})` with a new `projectId = input<string>('')` and `this.store.edit(current.id, {...}, this.projectId())` — fully rewritten in B3.

- [x] **Step 5: Run** `npx vitest run src/app/modules/work` → green (adjust the sprint-form spec's `create` expectation to `('p-1', {...})` by setting input `projectId`). `npx ng build` → success.

- [x] **Step 6: Commit**

```bash
git add src/app/modules/work/models src/app/modules/work/utils/sprint.mapper.ts src/app/modules/work/data-access/sprint-api.service.ts src/app/modules/work/state/sprint-list.store.ts src/app/modules/work/state/sprint-list.store.spec.ts src/app/modules/work/ui/sprint-start-dialog src/app/modules/work/ui/sprint-complete-dialog src/app/modules/work/ui/sprint-form src/app/modules/work/ui/task-form-modal src/app/modules/work/feature/task-backlog
git commit -m "Frontend sprints are project-scoped with canManage"
```
(stage any other spec file you touched explicitly.)

---

### Task B2: Pure selection helpers + `ModuleTaskPickerComponent`

**Files:**
- Create: `utils/module-task-selection.ts` + `utils/module-task-selection.spec.ts`
- Create: `ui/module-task-picker/module-task-picker.component.ts` + `.spec.ts`

**Interfaces — Produces:**
```ts
// utils/module-task-selection.ts
export type ModuleCheckState = 'checked' | 'indeterminate' | 'unchecked';
export interface PickerModule { objectiveId: string; title: string; isAchieved?: boolean; }
export interface PickerTask { id: string; title: string; objectiveId: string; sprintId: string | null; shortId?: string; }

export function moduleCheckState(mode: 'live' | 'bulk', objectiveId: string, tasks: readonly PickerTask[] | undefined,
  selectedTaskIds: ReadonlySet<string>, selectedObjectiveIds: ReadonlySet<string>, isPickable: (t: PickerTask) => boolean): ModuleCheckState;
export function toggleModuleLive(objectiveId: string, tasks: readonly PickerTask[] | undefined,
  selectedTaskIds: ReadonlySet<string>, selectedObjectiveIds: ReadonlySet<string>): { taskIds: Set<string>; objectiveIds: Set<string> };
export function toggleModuleBulk(tasks: readonly PickerTask[], selectedTaskIds: ReadonlySet<string>, isPickable: (t: PickerTask) => boolean): Set<string>;
export function toggleId(ids: ReadonlySet<string>, id: string): Set<string>;

// ModuleTaskPickerComponent  selector 'app-module-task-picker'
modules = input.required<readonly PickerModule[]>();
moduleMode = input<'live' | 'bulk'>('live');
allowedObjectiveIds = input<ReadonlySet<string> | null>(null);   // null = all modules pickable
sprintNames = input<Record<string, string>>({});                 // sprintId -> name, for the "In Sprint X" badge
frozenSprintIds = input<ReadonlySet<string>>(new Set());         // achieved sprints: their tasks are disabled
currentSprintId = input<string | null>(null);                    // the sprint being edited: no badge for its own tasks
selectedTaskIds = input<ReadonlySet<string>>(new Set());
selectedObjectiveIds = input<ReadonlySet<string>>(new Set());
selectedTaskIdsChange = output<Set<string>>();
selectedObjectiveIdsChange = output<Set<string>>();
```

Behavior:
- **live** (event Tree view): identical to today's modal — ticking a module adds it to `selectedObjectiveIds`, removes that module's individually ticked tasks, and locks its task checkboxes ("included (whole module)"); state is `'checked'` iff in `selectedObjectiveIds`, `'indeterminate'` if some/all loaded tasks ticked individually.
- **bulk** (sprint form): ticking a module loads its tasks if needed, then if every pickable task is selected → deselect them all, else select all pickable ones. State: `'checked'` if all pickable loaded tasks selected (and ≥1), `'indeterminate'` if some, else `'unchecked'`. `selectedObjectiveIds` never emitted.
- A task is **pickable** iff `allowedObjectiveIds` is null or contains its module, AND its `sprintId` is not in `frozenSprintIds`. Non-pickable tasks render disabled. Modules not in `allowedObjectiveIds` render with a disabled checkbox and a muted "Not your module" label (they can still expand to show tasks read-only).
- Badge: when `task.sprintId && task.sprintId !== currentSprintId()` show `In {{ sprintNames()[task.sprintId] ?? 'another sprint' }}` (and `· frozen` when in `frozenSprintIds`).
- Keep `data-testid`s: `module-expand`, `module-checkbox`, `task-checkbox`; add `task-sprint-badge`.
- Lazy loads with `TaskApiService.getTasks(objectiveId)` on first expand (and on first bulk tick), caching per module in a `moduleTasks` signal; on error caches `[]`.

- [ ] **Step 1: Write the failing helper spec**

```ts
// utils/module-task-selection.spec.ts
import { moduleCheckState, toggleId, toggleModuleBulk, toggleModuleLive, PickerTask } from './module-task-selection';

const t = (id: string, objectiveId = 'o1', sprintId: string | null = null): PickerTask => ({ id, title: id, objectiveId, sprintId });
const all = () => true;

describe('module-task-selection', () => {
  it('live: ticking a module selects it and drops its individual task ticks', () => {
    const r = toggleModuleLive('o1', [t('a'), t('b')], new Set(['a', 'x']), new Set());
    expect([...r.objectiveIds]).toEqual(['o1']);
    expect([...r.taskIds]).toEqual(['x']);
  });

  it('live: unticking a module removes it', () => {
    const r = toggleModuleLive('o1', [], new Set(), new Set(['o1']));
    expect(r.objectiveIds.size).toBe(0);
  });

  it('bulk: selects every pickable task, then deselects on second toggle', () => {
    const tasks = [t('a'), t('b', 'o1', 'frozen')];
    const pickable = (x: PickerTask) => x.sprintId !== 'frozen';
    const first = toggleModuleBulk(tasks, new Set(), pickable);
    expect([...first]).toEqual(['a']);
    const second = toggleModuleBulk(tasks, first, pickable);
    expect(second.size).toBe(0);
  });

  it('bulk state: checked when all pickable selected, indeterminate when some', () => {
    const tasks = [t('a'), t('b')];
    expect(moduleCheckState('bulk', 'o1', tasks, new Set(['a', 'b']), new Set(), all)).toBe('checked');
    expect(moduleCheckState('bulk', 'o1', tasks, new Set(['a']), new Set(), all)).toBe('indeterminate');
    expect(moduleCheckState('bulk', 'o1', tasks, new Set(), new Set(), all)).toBe('unchecked');
  });

  it('live state: checked only via whole-module selection', () => {
    expect(moduleCheckState('live', 'o1', [t('a')], new Set(), new Set(['o1']), all)).toBe('checked');
    expect(moduleCheckState('live', 'o1', [t('a')], new Set(['a']), new Set(), all)).toBe('indeterminate');
  });

  it('toggleId adds and removes', () => {
    expect([...toggleId(new Set(['a']), 'b')]).toEqual(['a', 'b']);
    expect([...toggleId(new Set(['a']), 'a')]).toEqual([]);
  });
});
```

- [ ] **Step 2: Run — FAIL**, then implement:

```ts
// utils/module-task-selection.ts
export type ModuleCheckState = 'checked' | 'indeterminate' | 'unchecked';
export interface PickerModule { objectiveId: string; title: string; isAchieved?: boolean; }
export interface PickerTask { id: string; title: string; objectiveId: string; sprintId: string | null; shortId?: string; }

export function toggleId(ids: ReadonlySet<string>, id: string): Set<string> {
  const next = new Set(ids);
  if (next.has(id)) next.delete(id); else next.add(id);
  return next;
}

export function toggleModuleLive(
  objectiveId: string, tasks: readonly PickerTask[] | undefined,
  selectedTaskIds: ReadonlySet<string>, selectedObjectiveIds: ReadonlySet<string>
): { taskIds: Set<string>; objectiveIds: Set<string> } {
  const objectiveIds = new Set(selectedObjectiveIds);
  const taskIds = new Set(selectedTaskIds);
  if (objectiveIds.has(objectiveId)) {
    objectiveIds.delete(objectiveId);
  } else {
    objectiveIds.add(objectiveId);
    // whole module supersedes individual task ticks for that module
    for (const task of tasks ?? []) taskIds.delete(task.id);
  }
  return { taskIds, objectiveIds };
}

export function toggleModuleBulk(
  tasks: readonly PickerTask[], selectedTaskIds: ReadonlySet<string>, isPickable: (t: PickerTask) => boolean
): Set<string> {
  const pickable = tasks.filter(isPickable);
  const next = new Set(selectedTaskIds);
  const allSelected = pickable.length > 0 && pickable.every((task) => next.has(task.id));
  for (const task of pickable) {
    if (allSelected) next.delete(task.id); else next.add(task.id);
  }
  return next;
}

export function moduleCheckState(
  mode: 'live' | 'bulk', objectiveId: string, tasks: readonly PickerTask[] | undefined,
  selectedTaskIds: ReadonlySet<string>, selectedObjectiveIds: ReadonlySet<string>, isPickable: (t: PickerTask) => boolean
): ModuleCheckState {
  if (mode === 'live' && selectedObjectiveIds.has(objectiveId)) return 'checked';
  const list = (tasks ?? []).filter(isPickable);
  const picked = list.filter((task) => selectedTaskIds.has(task.id)).length;
  if (picked === 0) return 'unchecked';
  if (mode === 'bulk' && picked === list.length) return 'checked';
  return 'indeterminate';
}
```

- [ ] **Step 3: Write the failing component spec**

```ts
// ui/module-task-picker/module-task-picker.component.spec.ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ModuleTaskPickerComponent } from './module-task-picker.component';
import { TaskApiService } from '../../data-access/task-api.service';

const tasks = [
  { id: 'a', title: 'Task A', objectiveId: 'o1', sprintId: null },
  { id: 'b', title: 'Task B', objectiveId: 'o1', sprintId: 'sp-2' },
  { id: 'c', title: 'Task C', objectiveId: 'o1', sprintId: 'sp-frozen' }
];

describe('ModuleTaskPickerComponent', () => {
  let fixture: ComponentFixture<ModuleTaskPickerComponent>;
  const getTasks = vi.fn().mockReturnValue(of(tasks));

  function setup(inputs: Record<string, unknown>): void {
    TestBed.configureTestingModule({
      imports: [ModuleTaskPickerComponent],
      providers: [{ provide: TaskApiService, useValue: { getTasks } }]
    });
    fixture = TestBed.createComponent(ModuleTaskPickerComponent);
    fixture.componentRef.setInput('modules', [{ objectiveId: 'o1', title: 'Design' }, { objectiveId: 'o2', title: 'Build' }]);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  it('bulk: ticking a module emits all pickable tasks (frozen excluded, other-sprint included)', async () => {
    setup({ moduleMode: 'bulk', frozenSprintIds: new Set(['sp-frozen']) });
    let emitted: Set<string> | undefined;
    fixture.componentInstance.selectedTaskIdsChange.subscribe((v) => (emitted = v));

    await fixture.componentInstance.onModuleToggled('o1');

    expect([...(emitted ?? [])].sort()).toEqual(['a', 'b']);
  });

  it('bulk: modules outside allowedObjectiveIds cannot be ticked', async () => {
    setup({ moduleMode: 'bulk', allowedObjectiveIds: new Set(['o2']) });
    const spy = vi.fn();
    fixture.componentInstance.selectedTaskIdsChange.subscribe(spy);
    await fixture.componentInstance.onModuleToggled('o1');
    expect(spy).not.toHaveBeenCalled();
  });

  it('shows an "In <sprint>" badge for tasks already in another sprint', async () => {
    setup({ moduleMode: 'bulk', sprintNames: { 'sp-2': 'Sprint 2' } });
    await fixture.componentInstance.toggleExpand('o1');
    fixture.detectChanges();
    const badges = fixture.nativeElement.querySelectorAll('[data-testid="task-sprint-badge"]');
    expect(badges[0].textContent).toContain('Sprint 2');
  });

  it('live: ticking a module emits objectiveIds and locks its tasks', async () => {
    setup({ moduleMode: 'live' });
    let objectives: Set<string> | undefined;
    fixture.componentInstance.selectedObjectiveIdsChange.subscribe((v) => (objectives = v));
    await fixture.componentInstance.onModuleToggled('o1');
    expect([...(objectives ?? [])]).toEqual(['o1']);
  });
});
```

- [ ] **Step 4: Run — FAIL**, then implement the component:

```ts
// ui/module-task-picker/module-task-picker.component.ts
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskApiService } from '../../data-access/task-api.service';
import {
  ModuleCheckState, PickerModule, PickerTask, moduleCheckState, toggleId, toggleModuleBulk, toggleModuleLive
} from '../../utils/module-task-selection';

@Component({
  selector: 'app-module-task-picker',
  standalone: true,
  template: `
    @for (module of modules(); track module.objectiveId) {
      <div class="rounded px-1 py-0.5">
        <div class="flex items-center gap-2 text-sm">
          <button type="button" class="w-4 text-[var(--color-text-secondary)]" [attr.aria-expanded]="expanded().has(module.objectiveId)"
                  (click)="toggleExpand(module.objectiveId)" data-testid="module-expand">
            {{ expanded().has(module.objectiveId) ? '▾' : '▸' }}
          </button>
          <input type="checkbox" data-testid="module-checkbox"
                 [disabled]="!isModuleAllowed(module.objectiveId)"
                 [checked]="checkState(module.objectiveId) === 'checked'"
                 [indeterminate]="checkState(module.objectiveId) === 'indeterminate'"
                 (change)="onModuleToggled(module.objectiveId)" />
          <span class="truncate text-[var(--color-text-primary)]">{{ module.title }}</span>
          @if (module.isAchieved) { <span class="text-xs text-[var(--color-text-secondary)]">Achieved</span> }
          @if (!isModuleAllowed(module.objectiveId)) { <span class="text-xs text-[var(--color-text-secondary)]">Not your module</span> }
        </div>
        @if (expanded().has(module.objectiveId)) {
          <div class="ml-8 border-l border-[var(--color-border)] pl-2">
            @for (task of moduleTasks()[module.objectiveId] ?? []; track task.id) {
              <label class="flex items-center gap-2 rounded px-2 py-1 text-sm hover:bg-[var(--color-surface-muted)]">
                <input type="checkbox" data-testid="task-checkbox"
                       [checked]="isTaskLocked(module.objectiveId) || selectedTaskIds().has(task.id)"
                       [disabled]="isTaskLocked(module.objectiveId) || !isPickable(task)"
                       (change)="onTaskToggled(task.id)" />
                <span class="truncate text-[var(--color-text-primary)]">{{ task.title }}</span>
                @if (isTaskLocked(module.objectiveId)) {
                  <span class="text-xs text-[var(--color-text-secondary)]">included (whole module)</span>
                } @else if (task.sprintId && task.sprintId !== currentSprintId()) {
                  <span class="text-xs text-[var(--color-text-secondary)]" data-testid="task-sprint-badge">
                    In {{ sprintNames()[task.sprintId] ?? 'another sprint' }}@if (frozenSprintIds().has(task.sprintId)) { · frozen }
                  </span>
                }
              </label>
            } @empty {
              <p class="px-2 py-1 text-xs text-[var(--color-text-secondary)]">No tasks in this module.</p>
            }
          </div>
        }
      </div>
    } @empty {
      <p class="text-sm text-[var(--color-text-secondary)]">No modules are visible.</p>
    }
  `
})
export class ModuleTaskPickerComponent {
  private readonly taskApi = inject(TaskApiService);

  readonly modules = input.required<readonly PickerModule[]>();
  readonly moduleMode = input<'live' | 'bulk'>('live');
  readonly allowedObjectiveIds = input<ReadonlySet<string> | null>(null);
  readonly sprintNames = input<Record<string, string>>({});
  readonly frozenSprintIds = input<ReadonlySet<string>>(new Set());
  readonly currentSprintId = input<string | null>(null);
  readonly selectedTaskIds = input<ReadonlySet<string>>(new Set());
  readonly selectedObjectiveIds = input<ReadonlySet<string>>(new Set());

  readonly selectedTaskIdsChange = output<Set<string>>();
  readonly selectedObjectiveIdsChange = output<Set<string>>();

  readonly expanded = signal<Set<string>>(new Set());
  readonly moduleTasks = signal<Record<string, PickerTask[]>>({});

  isModuleAllowed = (objectiveId: string): boolean => {
    const allowed = this.allowedObjectiveIds();
    return allowed === null || allowed.has(objectiveId);
  };

  isPickable = (task: PickerTask): boolean =>
    this.isModuleAllowed(task.objectiveId) && !(task.sprintId && this.frozenSprintIds().has(task.sprintId));

  isTaskLocked = (objectiveId: string): boolean =>
    this.moduleMode() === 'live' && this.selectedObjectiveIds().has(objectiveId);

  checkState(objectiveId: string): ModuleCheckState {
    return moduleCheckState(this.moduleMode(), objectiveId, this.moduleTasks()[objectiveId],
      this.selectedTaskIds(), this.selectedObjectiveIds(), this.isPickable);
  }

  async onModuleToggled(objectiveId: string): Promise<void> {
    if (!this.isModuleAllowed(objectiveId)) return;
    if (this.moduleMode() === 'live') {
      const next = toggleModuleLive(objectiveId, this.moduleTasks()[objectiveId], this.selectedTaskIds(), this.selectedObjectiveIds());
      this.selectedTaskIdsChange.emit(next.taskIds);
      this.selectedObjectiveIdsChange.emit(next.objectiveIds);
      return;
    }
    const tasks = await this.ensureTasks(objectiveId);
    this.selectedTaskIdsChange.emit(toggleModuleBulk(tasks, this.selectedTaskIds(), this.isPickable));
  }

  onTaskToggled(taskId: string): void {
    this.selectedTaskIdsChange.emit(toggleId(this.selectedTaskIds(), taskId));
  }

  async toggleExpand(objectiveId: string): Promise<void> {
    const next = new Set(this.expanded());
    if (next.has(objectiveId)) {
      next.delete(objectiveId);
      this.expanded.set(next);
      return;
    }
    next.add(objectiveId);
    this.expanded.set(next);
    await this.ensureTasks(objectiveId);
  }

  private async ensureTasks(objectiveId: string): Promise<PickerTask[]> {
    const cached = this.moduleTasks()[objectiveId];
    if (cached) return cached;
    let tasks: PickerTask[] = [];
    try {
      const dtos = await firstValueFrom(this.taskApi.getTasks(objectiveId));
      tasks = dtos.map((d) => ({ id: d.id, title: d.title, objectiveId: d.objectiveId, sprintId: d.sprintId, shortId: d.shortId }));
    } catch {
      tasks = [];
    }
    this.moduleTasks.set({ ...this.moduleTasks(), [objectiveId]: tasks });
    return tasks;
  }
}
```

- [ ] **Step 5: Run — PASS**: `npx vitest run src/app/modules/work/utils/module-task-selection.spec.ts src/app/modules/work/ui/module-task-picker`

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/utils/module-task-selection.ts src/app/modules/work/utils/module-task-selection.spec.ts src/app/modules/work/ui/module-task-picker
git commit -m "Add reusable module/task tree picker (live + bulk modes)"
```

---

### Task B3: `SprintFormComponent` — no module dropdown, task picker, create + edit diff

**Files:**
- Modify: `ui/sprint-form/sprint-form.component.ts` (rewrite) + `sprint-form.component.spec.ts` (rewrite)

**Interfaces:**
- Consumes: `ModuleTaskPickerComponent` (B2), `SprintListStore.create/edit/setTasks` (B1), `TaskApiService.getBySprintId`.
- Produces (inputs/outputs):
```ts
mode = input<'create' | 'edit'>('create');
open = input(false);
projectId = input.required<string>();
modules = input<readonly SprintFormModuleOption[]>([]);   // SprintFormModuleOption { objectiveId; title; isEffectiveOwner: boolean }
sprint = input<Sprint | null>(null);
allSprints = input<readonly Sprint[]>([]);                // for badges + frozen set
created = output<void>(); saved = output<void>(); cancelled = output<void>();
```

Behavior:
- Fields: Sprint name, Sprint goal (optional), Active-only date range (edit, as today), then a "Tasks" box with the picker: `moduleMode="bulk"`, `allowedObjectiveIds = set of modules with isEffectiveOwner`, `sprintNames` from `allSprints`, `frozenSprintIds` = ids of achieved sprints, `currentSprintId` = edited sprint id.
- Helper text under picker: "You can add tasks from modules you own. A task already in another sprint will move here."
- Create: `store.create(projectId, { name, goal?, taskIds: [...selected] })`.
- Edit: on open, load `taskApi.getBySprintId(sprint.id)` → `initialTaskIds` + `selectedTaskIds`. On save: `store.edit(...)` then, if the diff is non-empty, `store.setTasks(sprint.id, { addTaskIds: selected − initial, removeTaskIds: initial − selected }, projectId)`. Emit `saved` only if both succeed.
- Name required: if empty show `nameError` "Sprint name is required." and don't call the store.

- [ ] **Step 1: Rewrite the spec (failing)**

```ts
describe('SprintFormComponent', () => {
  let fixture: ComponentFixture<SprintFormComponent>;
  let storeStub: any;
  const taskApi = { getBySprintId: vi.fn().mockReturnValue(of([{ id: 't1', objectiveId: 'o1', sprintId: 'sp-1' }])), getTasks: vi.fn().mockReturnValue(of([])) };
  const sprint: Sprint = { id: 'sp-1', projectId: 'p-1', name: 'Sprint One', goal: null, startDate: null, endDate: null,
    status: 'draft', completedAt: null, achievedAt: null, canManage: true };

  function setup(inputs: Record<string, unknown> = {}): void {
    storeStub = { error: signal(null), create: vi.fn().mockResolvedValue(true), edit: vi.fn().mockResolvedValue(true), setTasks: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      imports: [SprintFormComponent],
      providers: [{ provide: SprintListStore, useValue: storeStub }, { provide: TaskApiService, useValue: taskApi }]
    });
    fixture = TestBed.createComponent(SprintFormComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('projectId', 'p-1');
    fixture.componentRef.setInput('modules', [
      { objectiveId: 'o1', title: 'Design', isEffectiveOwner: true },
      { objectiveId: 'o2', title: 'Build', isEffectiveOwner: false }
    ]);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  it('create sends name, goal and picked task ids at project level', async () => {
    setup();
    const c = fixture.componentInstance as any;
    c.name.set('Sprint X'); c.goal.set('Ship it'); c.selectedTaskIds.set(new Set(['t1', 't2']));
    const created = vi.fn(); c.created.subscribe(created);
    await c.submit();
    expect(storeStub.create).toHaveBeenCalledWith('p-1', { name: 'Sprint X', goal: 'Ship it', taskIds: ['t1', 't2'] });
    expect(created).toHaveBeenCalled();
  });

  it('only modules the user effectively owns are pickable', () => {
    setup();
    const c = fixture.componentInstance as any;
    expect([...c.allowedObjectiveIds()]).toEqual(['o1']);
  });

  it('requires a name', async () => {
    setup();
    await (fixture.componentInstance as any).submit();
    expect(storeStub.create).not.toHaveBeenCalled();
    expect((fixture.componentInstance as any).nameError()).toContain('required');
  });

  it('edit sends only the add/remove diff', async () => {
    setup({ mode: 'edit', sprint });
    await fixture.whenStable();
    const c = fixture.componentInstance as any;
    expect([...c.selectedTaskIds()]).toEqual(['t1']);
    c.selectedTaskIds.set(new Set(['t9']));
    await c.submit();
    expect(storeStub.edit).toHaveBeenCalled();
    expect(storeStub.setTasks).toHaveBeenCalledWith('sp-1', { addTaskIds: ['t9'], removeTaskIds: ['t1'] }, 'p-1');
  });

  it('edit with unchanged tasks skips setTasks', async () => {
    setup({ mode: 'edit', sprint });
    await fixture.whenStable();
    await (fixture.componentInstance as any).submit();
    expect(storeStub.setTasks).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run — FAIL**

- [ ] **Step 3: Implement** — keep the existing `app-modal`, inputs styling and date-range block; replace the Module dropdown with:

```html
        <div class="sprint-form__tasks">
          <label class="sprint-form__label">Tasks</label>
          <p class="sprint-form__hint">You can add tasks from modules you own. A task already in another sprint will move here.</p>
          <div class="sprint-form__picker">
            <app-module-task-picker
              moduleMode="bulk"
              [modules]="modules()"
              [allowedObjectiveIds]="allowedObjectiveIds()"
              [sprintNames]="sprintNames()"
              [frozenSprintIds]="frozenSprintIds()"
              [currentSprintId]="sprint()?.id ?? null"
              [selectedTaskIds]="selectedTaskIds()"
              (selectedTaskIdsChange)="selectedTaskIds.set($event)"
            />
          </div>
          <p class="sprint-form__hint">{{ selectedTaskIds().size }} task(s) selected</p>
        </div>
```
styles: `.sprint-form__picker { max-height: 18rem; overflow-y: auto; border: 1px solid var(--color-border); border-radius: 6px; padding: 8px; }` and `.sprint-form__hint { font-size: 12px; color: var(--color-text-secondary); margin: 0; }`.

Class body (replace module-related members):
```ts
export interface SprintFormModuleOption { objectiveId: string; title: string; isEffectiveOwner: boolean; }

  private readonly taskApi = inject(TaskApiService);
  projectId = input.required<string>();
  allSprints = input<readonly Sprint[]>([]);
  protected readonly selectedTaskIds = signal<Set<string>>(new Set());
  private initialTaskIds = new Set<string>();
  protected readonly nameError = signal<string | null>(null);
  protected readonly allowedObjectiveIds = computed(() => new Set(this.modules().filter((m) => m.isEffectiveOwner).map((m) => m.objectiveId)));
  protected readonly sprintNames = computed(() => Object.fromEntries(this.allSprints().map((s) => [s.id, s.name])));
  protected readonly frozenSprintIds = computed(() => new Set(this.allSprints().filter((s) => s.status === 'achieved').map((s) => s.id)));

  constructor() {
    effect(() => {
      this.open();
      const current = this.sprint();
      this.nameError.set(null);
      if (this.mode() === 'edit' && current) {
        this.name.set(current.name);
        this.goal.set(current.goal ?? '');
        this.startDate.set(current.startDate ? current.startDate.toISOString().slice(0, 10) : '');
        this.endDate.set(current.endDate ? current.endDate.toISOString().slice(0, 10) : '');
        void this.loadCurrentTasks(current.id);
      } else if (this.mode() === 'create') {
        this.name.set('');
        this.goal.set('');
        this.initialTaskIds = new Set();
        this.selectedTaskIds.set(new Set());
      }
    });
  }

  private async loadCurrentTasks(sprintId: string): Promise<void> {
    try {
      const tasks = await firstValueFrom(this.taskApi.getBySprintId(sprintId));
      this.initialTaskIds = new Set(tasks.map((t) => t.id));
    } catch {
      this.initialTaskIds = new Set();
    }
    this.selectedTaskIds.set(new Set(this.initialTaskIds));
  }

  async submit(): Promise<void> {
    if (!this.name().trim()) { this.nameError.set('Sprint name is required.'); return; }
    this.nameError.set(null);
    this.submitting.set(true);
    try {
      if (this.mode() === 'create') {
        const ok = await this.store.create(this.projectId(), {
          name: this.name().trim(), goal: this.goal().trim() || undefined, taskIds: [...this.selectedTaskIds()]
        });
        if (ok) this.created.emit();
        return;
      }
      const current = this.sprint();
      if (!current) return;
      const isActive = current.status === 'active';
      const edited = await this.store.edit(current.id, {
        name: this.name().trim(), goal: this.goal().trim() || null,
        startDate: isActive ? this.startDate() : undefined, endDate: isActive ? this.endDate() : undefined
      }, this.projectId());
      if (!edited) return;
      const selected = this.selectedTaskIds();
      const addTaskIds = [...selected].filter((id) => !this.initialTaskIds.has(id));
      const removeTaskIds = [...this.initialTaskIds].filter((id) => !selected.has(id));
      if (addTaskIds.length > 0 || removeTaskIds.length > 0) {
        const ok = await this.store.setTasks(current.id, { addTaskIds, removeTaskIds }, this.projectId());
        if (!ok) return;
      }
      this.saved.emit();
    } finally {
      this.submitting.set(false);
    }
  }
```
Show `nameError` under the name input with the existing `.sprint-form__error` style. Remove `objectiveId`, `isObjectiveOwner`, `selectedObjectiveId`, `moduleError`, `moduleOptions`, `effectiveObjectiveId`, and the `WorkDropdownComponent` import. Add `ModuleTaskPickerComponent` to `imports`. Widen the modal if `app-modal` supports a size input (check `shared/ui/modal/modal.component.ts`; use its wide/large size if present).

- [ ] **Step 4: Run — PASS**: `npx vitest run src/app/modules/work/ui/sprint-form`

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-form
git commit -m "Sprint form picks tasks from owned modules; edit saves task diff"
```

---

### Task B4: Backlog wiring — create for all members, move to any Draft/Active sprint, `canManage`

**Files:**
- Modify: `feature/task-backlog/task-backlog.component.ts` + `task-backlog.component.spec.ts`
- Modify: `ui/sprint-tab/sprint-tab.component.ts` + spec (rename input `isObjectiveOwner` → `canManage`)
- Modify: `ui/move-tasks-to-sprint-dialog/move-tasks-to-sprint-dialog.component.ts` (empty-state text only)

Changes:
1. Toolbar: `@if (ownedModuleOptions().length > 0)` → `@if (modules().length > 0)` for "Create Sprint"; the create `@if` likewise.
2. `app-sprint-form` (create) inputs: `[projectId]="projectId()"`, `[modules]="sprintFormModules()"`, `[allSprints]="sprintStore.sprints()"`; edit form same plus `[sprint]="sprint"`.
```ts
  protected readonly sprintFormModules = computed(() =>
    this.modules().map((m) => ({ objectiveId: m.objectiveId, title: m.title, isEffectiveOwner: m.isEffectiveOwner })));
```
   Delete `ownedModuleOptions` and its comment.
3. `app-sprint-tab`: `[canManage]="sprint.canManage"` (remove `isObjectiveOwner`). In `sprint-tab.component.ts`: `canManage = input.required<boolean>();` and use it in `canStart`/`canEdit`/`canClose`.
4. Move targets:
```ts
  private readonly ownedObjectiveIds = computed(() =>
    new Set(this.modules().filter((m) => m.isEffectiveOwner).map((m) => m.objectiveId)));
  // Sprints are project-level: any Draft/Active sprint is a valid target, but only for tasks
  // whose module the user owns (backend SprintTaskAssignment/EditTask enforce the same rule).
  protected readonly moveBlockedReason = computed(() =>
    this.selectedTasks().some((task) => !this.ownedObjectiveIds().has(task.objectiveId))
      ? 'You can only move tasks from modules you own.' : null);
  protected readonly moveTargets = computed<SprintMoveTarget[]>(() => {
    if (this.selectedTasks().length === 0) return [];
    const order: Record<string, number> = { active: 0, draft: 1 };
    return this.sprintStore.sprints()
      .filter((sprint) => sprint.status === 'draft' || sprint.status === 'active')
      .sort((a, b) => (order[a.status] - order[b.status]) || ((a.startDate?.getTime() ?? Infinity) - (b.startDate?.getTime() ?? Infinity)))
      .map((sprint) => {
        const sprintTasks = this.tasksForSprint(sprint.id);
        return { sprint, currentTaskCount: sprintTasks.length,
          currentHours: sprintTasks.reduce((sum, task) => sum + (task.estimatedHours ?? 0), 0) };
      });
  });
```
5. Dialog empty text → `No Draft or Active sprints in this project yet. Create one first.`
6. Show the sprint's status next to each move target if the dialog doesn't already (check its template; if it only shows name/goal, add `<span class="move-tasks-dialog__option-meta">{{ target.sprint.status === 'active' ? 'Active' : 'Draft' }}</span>`).

- [ ] **Step 1: Failing specs** in `task-backlog.component.spec.ts` (update the sprint fixtures to `projectId`/`canManage`, milestone fixtures to include `isEffectiveOwner`):
  - "Create Sprint button shows for a member who owns no module" (modules with `isOwner:false,isEffectiveOwner:false`).
  - "move targets include draft and active sprints across modules, exclude complete/achieved".
  - "move is blocked when a selected task's module is not effectively owned".
  - sprint-tab spec: "hides Start when canManage is false".

- [ ] **Step 2: Run — FAIL**, **Step 3: implement**, **Step 4: Run — PASS**

Run: `npx vitest run src/app/modules/work/feature/task-backlog src/app/modules/work/ui/sprint-tab src/app/modules/work/ui/move-tasks-to-sprint-dialog`

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/feature/task-backlog src/app/modules/work/ui/sprint-tab src/app/modules/work/ui/move-tasks-to-sprint-dialog
git commit -m "Backlog: any member creates sprints; move owned tasks to any Draft/Active sprint"
```

---

### Task B5: Tree tab sprint children only show the leaf module's tasks

**Files:**
- Modify: `state/project-detail.store.ts` (`loadTasksForSprint`, ~line 326)
- Test: `state/project-detail.store.spec.ts` (find the `loadTasksForSprint` describe)

- [ ] **Step 1: Failing test** — sprint node under leaf `o1`; `getBySprintId` returns tasks for `o1` and `o2`; after `loadTasksForSprint`, the node's children contain only the `o1` task.

- [ ] **Step 2: Implement** — in `loadTasksForSprint` replace `const children = tasks.map(` with:
```ts
        // Sprints are project-level (may hold tasks from several modules); under a module's tree
        // node only that module's tasks belong.
        const children = tasks
          .filter((task) => !sprint.parentObjectiveId || task.objectiveId === sprint.parentObjectiveId)
          .map((task) =>
```

- [ ] **Step 3: Run — PASS**: `npx vitest run src/app/modules/work/state/project-detail.store.spec.ts`

- [ ] **Step 4: Commit**

```bash
git add src/app/modules/work/state/project-detail.store.ts src/app/modules/work/state/project-detail.store.spec.ts
git commit -m "Tree tab: sprint node lists only its module's tasks"
```

---

### Task B6: `SprintHistoryComponent` in the expanded sprint row

**Files:**
- Create: `ui/sprint-history/sprint-history.component.ts` + `.spec.ts`
- Modify: `ui/sprint-tab/sprint-tab.component.ts` (render inside the expanded area, below the task list)

**Interfaces — Produces:** selector `app-sprint-history`, input `sprintId = input.required<string>()`.

Behavior: on `sprintId` change, loads `SprintApiService.getActivity(id)`, then `EmployeeDirectoryStore.ensureLoaded(uniqueEmployeeIds)`. Renders a collapsible "History (n)" header (collapsed by default) and newest-first rows: `<name> <verb> · <d MMM HH:mm>` where verb map:
```ts
const VERBS: Record<SprintActivityDto['action'], string> = {
  created: 'created the sprint', edited: 'edited the sprint', started: 'started the sprint',
  completed: 'completed the sprint', achieved: 'achieved the sprint',
  tasks_added: 'added {n} task(s)', tasks_removed: 'removed {n} task(s)'
};
```
`{n}` = `JSON.parse(detailsJson).taskIds.length` (guard with try/catch → omit count). Use Angular `DatePipe` with format `'d MMM HH:mm'`. Error → "Couldn't load history."; empty → "No activity yet."

- [ ] **Step 1: Failing spec**

```ts
describe('SprintHistoryComponent', () => {
  it('renders newest first with employee names and task counts', async () => {
    const api = { getActivity: vi.fn().mockReturnValue(of([
      { id: '1', employeeId: 'e1', action: 'created', fromStatus: null, toStatus: 'draft', detailsJson: null, occurredAt: '2026-09-23T10:00:00Z' },
      { id: '2', employeeId: 'e2', action: 'tasks_added', fromStatus: null, toStatus: null, detailsJson: '{"taskIds":["a","b"]}', occurredAt: '2026-09-23T11:00:00Z' }
    ])) };
    const directory = { ensureLoaded: vi.fn().mockResolvedValue(undefined), get: (id: string) => ({ id, name: id === 'e1' ? 'Ravi' : 'Priya' }) };
    TestBed.configureTestingModule({ imports: [SprintHistoryComponent], providers: [
      { provide: SprintApiService, useValue: api }, { provide: EmployeeDirectoryStore, useValue: directory }] });
    const fixture = TestBed.createComponent(SprintHistoryComponent);
    fixture.componentRef.setInput('sprintId', 'sp-1');
    fixture.detectChanges();
    await fixture.whenStable();
    (fixture.componentInstance as any).expanded.set(true);
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="sprint-history-row"]');
    expect(rows[0].textContent).toContain('Priya added 2 task(s)');
    expect(rows[1].textContent).toContain('Ravi created the sprint');
  });
});
```

- [ ] **Step 2: Run — FAIL**, **Step 3: implement** (standalone component; `effect` on `sprintId` triggers `load()`; signals `entries`, `loading`, `error`, `expanded`), add `<app-sprint-history [sprintId]="sprint().id" />` inside sprint-tab's expanded block and add it to sprint-tab `imports`.

- [ ] **Step 4: Run — PASS**: `npx vitest run src/app/modules/work/ui/sprint-history src/app/modules/work/ui/sprint-tab` (sprint-tab spec must provide a `SprintApiService` stub with `getActivity: () => of([])` and an `EmployeeDirectoryStore` stub if not already present).

- [ ] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/sprint-history src/app/modules/work/ui/sprint-tab
git commit -m "Show sprint activity history in the expanded sprint row"
```

---

### Task B7: Event modal Tree view runs on the shared picker

**Files:**
- Modify: `ui/calendar-event-modal/calendar-event-modal.component.ts`
- Test: `ui/calendar-event-modal/calendar-event-modal.component.spec.ts` (existing 4 tests must stay green unchanged)

Changes:
- Replace the inline module/task `@for` block with:
```html
          <app-module-task-picker
            moduleMode="live"
            [modules]="modules()"
            [selectedTaskIds]="selectedTaskIds()"
            [selectedObjectiveIds]="selectedObjectiveIds()"
            (selectedTaskIdsChange)="selectedTaskIds.set($event)"
            (selectedObjectiveIdsChange)="selectedObjectiveIds.set($event)"
          />
```
- Keep public members the spec uses: `selectedObjectiveIds`, `selectedTaskIds`, `valid`, `submit`, `error`, `isTaskLocked`, `toggleObjective`. Re-implement the latter two with the helpers:
```ts
  isTaskLocked = (objectiveId: string): boolean => this.selectedObjectiveIds().has(objectiveId);
  toggleObjective(objectiveId: string): void {
    const next = toggleModuleLive(objectiveId, undefined, this.selectedTaskIds(), this.selectedObjectiveIds());
    this.selectedTaskIds.set(next.taskIds);
    this.selectedObjectiveIds.set(next.objectiveIds);
  }
```
- Delete `expanded`, `moduleTasks`, `moduleCheckState`, `toggleTask`, `toggleExpand` and the `TaskApiService` injection from the modal (the picker owns them). Keep the spec's `TaskApiService` provider — the picker needs it.

- [ ] **Step 1:** Run the existing spec first to confirm green baseline: `npx vitest run src/app/modules/work/ui/calendar-event-modal`
- [ ] **Step 2:** Refactor as above.
- [ ] **Step 3:** Re-run — still 4/4 green.
- [ ] **Step 4: Commit**

```bash
git add src/app/modules/work/ui/calendar-event-modal
git commit -m "Event modal tree view uses the shared module/task picker"
```

---

### Task B8: Event modal Sprint view + Tree/Sprint switch

**Files:**
- Create: `ui/sprint-task-picker/sprint-task-picker.component.ts` + `.spec.ts`
- Modify: `ui/calendar-event-modal/calendar-event-modal.component.ts` + spec
- Modify: `feature/project-calendar/project-calendar.component.ts` (~line 151: pass `[projectId]="projectId() ?? ''"`)

**Interfaces — Produces:**
```ts
// SprintTaskPickerComponent  selector 'app-sprint-task-picker'
projectId = input.required<string>();
selectedTaskIds = input<ReadonlySet<string>>(new Set());
lockedObjectiveIds = input<ReadonlySet<string>>(new Set());  // modules ticked whole in Tree view
selectedTaskIdsChange = output<Set<string>>();
// exported for tests:
export function orderSprintsForTimeline(sprints: readonly SprintDto[]): SprintDto[];
```

Behavior:
- On first render with a `projectId`, loads `SprintApiService.getByProject(projectId)` once; order via `orderSprintsForTimeline`: sprints **with** `startDate` ascending by `startDate`; then dateless (Draft) sprints in the order returned by the API (server creation order). Each row: ▸ expand, tri-state checkbox (`data-testid="sprint-checkbox"`), name, status chip, `startDate – endDate` or "No dates yet".
- Expand/tick lazy-loads `TaskApiService.getBySprintId(sprint.id)` (cache per sprint).
- Sprint tick = one-time bulk: select all its tasks that are not locked (a task is locked when `lockedObjectiveIds` has its `objectiveId`; render ticked+disabled with "included (whole module)"); second tick deselects them. Use `toggleModuleBulk` / `moduleCheckState('bulk', ...)` from B2 with `isPickable = task => !lockedObjectiveIds().has(task.objectiveId)`.
- Task rows `data-testid="sprint-task-checkbox"`.

Event modal:
- Add `readonly projectId = input<string>('');` and `readonly view = signal<'tree' | 'sprint'>('tree');` (reset to `'tree'` in the existing `effect`).
- Above the picker box, a segmented switch:
```html
          <div class="mb-2 inline-flex rounded-md border border-[var(--color-border)] p-0.5 text-xs" role="tablist" aria-label="Pick tasks by">
            <button type="button" role="tab" data-testid="view-tree" [attr.aria-selected]="view() === 'tree'"
                    class="rounded px-2 py-1" [class.bg-[var(--color-surface-muted)]]="view() === 'tree'" (click)="view.set('tree')">Tree view</button>
            <button type="button" role="tab" data-testid="view-sprint" [attr.aria-selected]="view() === 'sprint'"
                    class="rounded px-2 py-1" [class.bg-[var(--color-surface-muted)]]="view() === 'sprint'" (click)="view.set('sprint')">Sprint view</button>
          </div>
```
- Heading text: `view() === 'tree' ? 'Modules & tasks' : 'Sprints & tasks'`.
- Body: `@if (view() === 'tree') { <app-module-task-picker ... /> } @else { <app-sprint-task-picker [projectId]="projectId()" [selectedTaskIds]="selectedTaskIds()" [lockedObjectiveIds]="selectedObjectiveIds()" (selectedTaskIdsChange)="selectedTaskIds.set($event)" /> }` — one shared `selectedTaskIds`, so switching views keeps the selection.
- Payload unchanged (`objectiveIds`, `taskIds`).

- [ ] **Step 1: Failing specs**

```ts
// sprint-task-picker.component.spec.ts
import { orderSprintsForTimeline } from './sprint-task-picker.component';

const s = (id: string, startDate: string | null) => ({ id, projectId: 'p1', name: id, goal: null, startDate, endDate: null,
  status: startDate ? 'active' : 'draft', completedAt: null, achievedAt: null, canManage: false } as const);

describe('orderSprintsForTimeline', () => {
  it('orders dated sprints by start date, then dateless drafts in API order', () => {
    const ordered = orderSprintsForTimeline([s('d1', null), s('late', '2026-10-01'), s('d2', null), s('early', '2026-09-01')]);
    expect(ordered.map((x) => x.id)).toEqual(['early', 'late', 'd1', 'd2']);
  });
});

describe('SprintTaskPickerComponent', () => {
  it('ticking a sprint selects its tasks except ones locked by a whole-module tick', async () => {
    TestBed.configureTestingModule({ imports: [SprintTaskPickerComponent], providers: [
      { provide: SprintApiService, useValue: { getByProject: () => of([s('sp1', '2026-09-01')]) } },
      { provide: TaskApiService, useValue: { getBySprintId: () => of([
        { id: 'a', title: 'A', objectiveId: 'o1', sprintId: 'sp1' }, { id: 'b', title: 'B', objectiveId: 'o2', sprintId: 'sp1' }]) } }] });
    const fixture = TestBed.createComponent(SprintTaskPickerComponent);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('lockedObjectiveIds', new Set(['o2']));
    fixture.detectChanges();
    await fixture.whenStable();
    let emitted: Set<string> | undefined;
    fixture.componentInstance.selectedTaskIdsChange.subscribe((v) => (emitted = v));
    await fixture.componentInstance.onSprintToggled('sp1');
    expect([...(emitted ?? [])]).toEqual(['a']);
  });
});
```

Add to `calendar-event-modal.component.spec.ts` (provide `SprintApiService` stub `{ getByProject: () => of([]) }` and `TaskApiService` stub with `getBySprintId: () => of([])` too):
```ts
  it('keeps the task selection when switching between Tree and Sprint view', () => {
    const c = fixture.componentInstance;
    c.selectedTaskIds.set(new Set(['t1']));
    c.view.set('sprint');
    fixture.detectChanges();
    c.view.set('tree');
    fixture.detectChanges();
    expect([...c.selectedTaskIds()]).toEqual(['t1']);
  });

  it('shows the Sprint view list when switched', () => {
    fixture.nativeElement.querySelector('[data-testid="view-sprint"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-sprint-task-picker')).not.toBeNull();
  });
```

- [ ] **Step 2: Run — FAIL**, **Step 3: implement** (component follows B2's picker structure; `ensureTasks(sprintId)` mapping `WorkTaskDto` → `PickerTask`; `effect` on `projectId` loads sprints once per id).

- [ ] **Step 4:** In `project-calendar.component.ts` add `[projectId]="projectId() ?? ''"` to `<app-calendar-event-modal>`.

- [ ] **Step 5: Run — PASS**: `npx vitest run src/app/modules/work/ui/sprint-task-picker src/app/modules/work/ui/calendar-event-modal src/app/modules/work/feature/project-calendar`

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/sprint-task-picker src/app/modules/work/ui/calendar-event-modal src/app/modules/work/feature/project-calendar/project-calendar.component.ts
git commit -m "Milestone modal: Tree/Sprint view switch with timeline-ordered sprint picker"
```

---

### Task B9: Full verification + manual browser pass

- [ ] **Step 1:** `npx vitest run` (whole frontend). Compare failures against `git stash`-free baseline: if any failures exist outside `src/app/modules/work`, check with `git log -1 --format=%H -- <failing spec>` whether they predate this branch; only WM failures introduced by this plan must be fixed. Record the numbers.
- [ ] **Step 2:** `npx ng build` → success, no new warnings from touched files.
- [ ] **Step 3:** Grep sanity: `grep -rn "objectiveId" src/app/modules/work --include=*.ts | grep -i "sprint\." ` → no `sprint.objectiveId` left.
- [ ] **Step 4: Manual browser pass** (backend running with both migrations applied — **ask the user to run `dotnet ef database update` if you are not allowed to touch their DB**; use the `Claude_Browser` preview tools):
  1. Backlog → "Create Sprint" as a user who owns module A only: picker shows A pickable, B greyed "Not your module". Tick module A → all A tasks ticked; untick one; create. Sprint appears with those tasks.
  2. Pick a task already in Sprint 2 → badge "In Sprint 2"; after save it moved.
  3. Backlog multi-select tasks from A → Move → list shows all Draft + Active sprints of the project (not just A's).
  4. Sprint row: Start/Complete visible for creator and for the owner of a task's module; hidden for a plain member.
  5. Expand sprint → History shows created / tasks added / started / completed with names and times.
  6. Calendar → Create milestone → switch to Sprint view: sprints listed dated-first by start date, drafts last; tick a sprint → its tasks ticked; switch to Tree view → same tasks still ticked; save → milestone shows those tasks.
  7. Tree tab: expand module A → sprint node → only A's tasks listed.
- [ ] **Step 5:** Report results (numbers + screenshots) to the user. Do not merge or push unless the user asks.
