# WM Hierarchy / Approval / Notification Engines — Plan 1: Foundation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the creator-position column, the Module-tree hierarchy service, the Approval engine, the Notification engine, and the generic approval/notification endpoints. The new code runs alongside the existing request flows and does not replace them yet.

**Architecture:**
- `ProjectModuleTree` is a pure in-memory view of one project's Module tree.
- `IWorkHierarchyService` loads that tree and finds the active position holder.
- `IWorkApprovalEngine.SubmitAsync` decides Direct or Pending, using the tree plus a read-only HR `IEmployeeAuthorityResolver` fallback.
- `IWorkNotificationEngine.NotifyAsync` writes `wm_notification_log` rows and enqueues the existing `WorkNotification` outbox message in the caller's transaction.
- Approve/Reject/Cancel commands decide `wm_approval_requests` rows and apply them through an `IApprovalActionApplier` registry. No appliers exist yet; Plans 2–4 add them.

**Tech Stack:** .NET 10, EF Core (Npgsql, snake_case naming), MediatR, xUnit + Moq + FluentAssertions, SQLite in-memory for repository tests.

**Spec:** `docs/superpowers/specs/next/2026-09-28-wm-hierarchy-approval-notification-engine-design.md`

## Global Constraints

- **WM only.** Never edit files under `Features/CoreHr`, `Leave`, `TimeAttendance`, `People`, `Calendar`, or shared notification/outbox code.
  - Exception 1: adding three WM template entries inside the existing WM block of `NotificationTemplateSeeder.cs`. That is where every existing `work_*` template already lives.
  - Exception 2: WM DbSets in `ApplicationDbContext.cs` and WM registrations in `Infrastructure/DependencyInjection.cs`, the same as every earlier WM feature.
- `IEmployeeAuthorityResolver` is called read-only, with `RequiredPermission = "projects:access"` and `Purpose = EmployeeAuthorityPurpose.EmployeeLifecycleApproval`. No CoreHr enum or code change.
- Engines never call `SaveChangesAsync`. Callers wrap the operation in `IUnitOfWork.ExecuteInTransactionAsync` (same convention as `IMilestoneMembershipCoordinator`).
- Every migration that adds a tenant-owned table includes the `TenantTables = [...]` RLS block, so `TenantIsolationArchitectureTests` stays green.
- Every migration data backfill starts with `SET LOCAL app.tenant_context_mode = 'admin';` inside the same `migrationBuilder.Sql` call. The migrator role is `NOBYPASSRLS`, so without it the UPDATE silently touches zero rows (see `20260823175314_AddWorkTaskCategoryId.cs`).
- Test command: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~<Name>"`.
- Gate at the end of the plan: `dotnet build` plus the full unit and architecture suites are green.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Branch: `feature/wm-hierarchy-approval-notification-engine` (already created from `origin/development`).

**Deferred to later plans (by design):**
- Stamping `CreatorPositionObjectiveId` at the 7 create sites. It happens in Plans 2–4 when each create handler is rewritten to go through the engine.
- Replacing the duplicated ancestor-walk loops in query handlers. That is Plan 4, the cleanup plan.

Until then, a null creator position falls back as follows:
- Module → its parent Module;
- Task → its own Module;
- Sprint → the project root Module.

---

## File map

| File | Responsibility |
|---|---|
| `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs` | request entity + status/target/action/source constants |
| `src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs` | notification log entity + kind constants |
| `src/ONEVO.Domain/Features/WorkManagement/{Objectives,Sprints,Tasks}/Entities/*.cs` | add `CreatorPositionObjectiveId` |
| `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs` | table `wm_approval_requests` |
| `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkNotificationLogConfiguration.cs` | table `wm_notification_log` |
| `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalRequestRepository.cs` | request persistence |
| `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkNotificationLogRepository.cs` | log persistence |
| `src/ONEVO.Infrastructure/Migrations/<ts>_AddWorkApprovalEngineFoundation.cs` | columns, tables, backfill, RLS |
| `src/ONEVO.Application/Features/WorkManagement/Hierarchy/ProjectModuleTree.cs` | pure tree logic |
| `src/ONEVO.Application/Features/WorkManagement/Hierarchy/IWorkHierarchyService.cs` + `WorkHierarchyService.cs` | tree loading, active-holder walk |
| `src/ONEVO.Application/Features/WorkManagement/Notifications/**` | repo interface, engine, labels, list query, DTO |
| `src/ONEVO.Application/Features/WorkManagement/Approvals/**` | repo interface, engine, decision rules, applier registry, commands, list query, DTO |
| `src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs` | HTTP endpoints |
| `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` | 3 new `work_*` templates |

---

### Task 1: Entities, EF configuration, repositories and migration

**Files:**
- Create:
  - `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`
  - `src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs`
  - both configurations and both EF repositories from the file map
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalRequestRepository.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/RepositoryInterfaces/IWorkNotificationLogRepository.cs`
- Modify:
  - `Objective.cs`, `Sprint.cs`, `WorkTask.cs` (Domain)
  - `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (next to `TaskEditRequests` DbSet, ~line 314)
  - `src/ONEVO.Infrastructure/DependencyInjection.cs` (WM block, ~line 369)
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/EfWorkApprovalRequestRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `WorkApprovalRequest`, `WorkApprovalRequestStatuses`, `WorkApprovalSources`, `WorkTargetTypes`, `WorkActionTypes`
  - `WorkNotificationLog`, `WorkNotificationKinds`
  - `IWorkApprovalRequestRepository`, `IWorkNotificationLogRepository` (signatures below)
  - `CreatorPositionObjectiveId` (`Guid?`) on `Objective`, `Sprint`, `WorkTask`

- [ ] **Step 1: Create the domain entities**

`src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

public static class WorkApprovalRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    /// <summary>The target was deleted or changed after the request was made - nothing was applied.</summary>
    public const string Stale = "stale";
}

/// <summary>How the approver was resolved. Hierarchy approvals follow the position (anyone at or
/// above it may decide, so a transfer moves the right); Hr approvals are fixed to the resolved
/// HR approver because they sit outside the project tree.</summary>
public static class WorkApprovalSources
{
    public const string Hierarchy = "hierarchy";
    public const string Hr = "hr";
}

public static class WorkTargetTypes
{
    public const string Module = "module";
    public const string Task = "task";
    public const string Sprint = "sprint";
}

public static class WorkActionTypes
{
    public const string TaskCreate = "task.create";
    public const string TaskEdit = "task.edit";
    public const string TaskDelete = "task.delete";
    public const string TaskStatusChange = "task.status_change";
    public const string ModuleEdit = "module.edit";
    public const string ModuleDelete = "module.delete";
    public const string ModuleTransfer = "module.transfer";
    public const string ModuleAchieve = "module.achieve";
    public const string ModuleUnachieve = "module.unachieve";
    public const string ModuleAllocationExtend = "module.allocation_extend";
    public const string SprintCreate = "sprint.create";
    public const string SprintEdit = "sprint.edit";
    public const string SprintDelete = "sprint.delete";
}

/// <summary>
/// One pending-or-decided approval for any Work Management action, replacing the per-type request
/// tables. The approver is resolved from the target's creator position in the project Module tree
/// (see the 2026-09-28 hierarchy/approval/notification engine spec).
/// </summary>
public class WorkApprovalRequest : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    /// <summary>Null for create actions.</summary>
    public Guid? TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    /// <summary>The Module whose current owner holds the approving position. Null only for Hr approvals.</summary>
    public Guid? PositionObjectiveId { get; set; }
    public string ApproverSource { get; set; } = WorkApprovalSources.Hierarchy;
    /// <summary>The approver resolved at submit time - who was notified. Hierarchy approvals are re-checked at decision time.</summary>
    public Guid ApproverEmployeeId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = WorkApprovalRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    /// <summary>Target's UpdatedAt when the request was made - appliers compare it to detect stale requests.</summary>
    public DateTimeOffset? TargetUpdatedAtSnapshot { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
```

`src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

public static class WorkNotificationKinds
{
    public const string Direct = "direct";
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Stale = "stale";
}

/// <summary>One recipient's copy of a Work Management activity - the project-scoped history shown
/// on the Approvals page. The in-app push itself goes through the WorkNotification outbox message.</summary>
public class WorkNotificationLog : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid RecipientEmployeeId { get; set; }
    public Guid ActorEmployeeId { get; set; }
    public string Kind { get; set; } = WorkNotificationKinds.Direct;
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}
```

- [ ] **Step 2: Add `CreatorPositionObjectiveId` to the three entities**

Add this property to `Objective` (after `ReportingManagerId`), to `Sprint` (after `ProjectId`) and to `WorkTask` (after `ObjectiveId`):

```csharp
    /// <summary>The Module whose current owner is this object's creator position - the approver of
    /// edits by anyone below it. Null means "use the default": Module → its parent, Task → its own
    /// Module, Sprint → the project root Module.</summary>
    public Guid? CreatorPositionObjectiveId { get; set; }
```

- [ ] **Step 3: Create the repository interfaces**

`src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalRequestRepository.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;

public interface IWorkApprovalRequestRepository
{
    Task AddAsync(WorkApprovalRequest request, CancellationToken ct = default);
    Task<WorkApprovalRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<bool> HasPendingAsync(Guid tenantId, string targetType, Guid targetId, string actionType, CancellationToken ct = default);
    /// <summary>Newest first. requestedByEmployeeId and status are optional filters.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListByProjectAsync(
        Guid tenantId, Guid projectId, Guid? requestedByEmployeeId, string? status, CancellationToken ct = default);
    void Update(WorkApprovalRequest request);
}
```

`src/ONEVO.Application/Features/WorkManagement/Notifications/RepositoryInterfaces/IWorkNotificationLogRepository.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;

public interface IWorkNotificationLogRepository
{
    Task AddAsync(WorkNotificationLog log, CancellationToken ct = default);
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<WorkNotificationLog>> ListForRecipientAsync(
        Guid tenantId, Guid projectId, Guid recipientEmployeeId, int skip, int take, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create the EF configurations**

`src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkApprovalRequestConfiguration : IEntityTypeConfiguration<WorkApprovalRequest>
{
    public void Configure(EntityTypeBuilder<WorkApprovalRequest> builder)
    {
        builder.ToTable("wm_approval_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ActionType).HasMaxLength(40).IsRequired();
        builder.Property(r => r.TargetType).HasMaxLength(20).IsRequired();
        builder.Property(r => r.TargetTitle).HasMaxLength(500).IsRequired();
        builder.Property(r => r.ApproverSource).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.Property(r => r.DecisionComment).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.ProjectId, r.Status })
            .HasDatabaseName("ix_wm_approval_requests_tenant_id_project_id_status");
        builder.HasIndex(r => new { r.TenantId, r.ApproverEmployeeId, r.Status })
            .HasDatabaseName("ix_wm_approval_requests_tenant_id_approver_employee_id_status");
        builder.HasIndex(r => new { r.TenantId, r.TargetType, r.TargetId, r.ActionType })
            .IsUnique()
            .HasFilter("status = 'pending' AND target_id IS NOT NULL")
            .HasDatabaseName("ux_wm_approval_requests_one_pending_per_target_action");
    }
}
```

`src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkNotificationLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class WorkNotificationLogConfiguration : IEntityTypeConfiguration<WorkNotificationLog>
{
    public void Configure(EntityTypeBuilder<WorkNotificationLog> builder)
    {
        builder.ToTable("wm_notification_log");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Kind).HasMaxLength(20).IsRequired();
        builder.Property(l => l.ActionType).HasMaxLength(40).IsRequired();
        builder.Property(l => l.TargetType).HasMaxLength(20).IsRequired();
        builder.Property(l => l.TargetTitle).HasMaxLength(500).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.ProjectId, l.RecipientEmployeeId, l.CreatedAt })
            .HasDatabaseName("ix_wm_notification_log_tenant_id_project_id_recipient_created_at")
            .IsDescending(false, false, false, true);
    }
}
```

- [ ] **Step 5: Add the DbSets**

In `ApplicationDbContext.cs`, directly under `public DbSet<TaskEditRequest> TaskEditRequests => Set<TaskEditRequest>();`:

```csharp
        public DbSet<WorkApprovalRequest> WorkApprovalRequests => Set<WorkApprovalRequest>();
        public DbSet<WorkNotificationLog> WorkNotificationLogs => Set<WorkNotificationLog>();
```

Add the two `using` lines at the top:
- `using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;`
- `using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;`

- [ ] **Step 6: Write the failing repository test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/EfWorkApprovalRequestRepositoryTests.cs`. Copy the constructor, `Dispose`, `CreateContext`, `TestClock` and `NoOpPublisher` members exactly from `EfWorkApprovalHistoryRepositoryTests.cs`, changing the connection-string prefix to `approval_requests_`. Then add:

```csharp
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static WorkApprovalRequest NewRequest(Guid? targetId, string status = WorkApprovalRequestStatuses.Pending,
        Guid? requestedBy = null, DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
        ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task, TargetId = targetId,
        TargetTitle = "Audit events", ApproverEmployeeId = Approver,
        RequestedByEmployeeId = requestedBy ?? Requester, Status = status,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task HasPending_TrueOnlyForPendingSameTargetAndAction()
    {
        var targetId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(NewRequest(targetId));
            db.WorkApprovalRequests.Add(NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Rejected));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        (await repo.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit)).Should().BeTrue();
        (await repo.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskDelete)).Should().BeFalse();
        (await repo.HasPendingAsync(Guid.NewGuid(), WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit)).Should().BeFalse();
    }

    [Fact]
    public async Task ListByProject_FiltersByRequesterAndStatus_NewestFirst()
    {
        var older = NewRequest(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = NewRequest(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow);
        var someoneElse = NewRequest(Guid.NewGuid(), requestedBy: Guid.NewGuid());
        var decided = NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Approved);
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.AddRange(older, newer, someoneElse, decided);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        var mine = await repo.ListByProjectAsync(TenantId, ProjectId, Requester, WorkApprovalRequestStatuses.Pending);
        mine.Select(r => r.Id).Should().Equal(newer.Id, older.Id);

        var all = await repo.ListByProjectAsync(TenantId, ProjectId, null, null);
        all.Should().HaveCount(4);
    }
```

Required usings:
- `FluentAssertions`
- `ONEVO.Domain.Features.WorkManagement.Approvals.Entities`
- plus the usings already present in `EfWorkApprovalHistoryRepositoryTests.cs`

- [ ] **Step 7: Run the test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfWorkApprovalRequestRepositoryTests"`
Expected: build FAIL, `EfWorkApprovalRequestRepository` not found.

- [ ] **Step 8: Implement both EF repositories**

`src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalRequestRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkApprovalRequestRepository : IWorkApprovalRequestRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkApprovalRequestRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkApprovalRequest request, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.AddAsync(request, ct);

    public async Task<WorkApprovalRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, ct);

    public async Task<bool> HasPendingAsync(Guid tenantId, string targetType, Guid targetId, string actionType, CancellationToken ct = default)
        => await _db.WorkApprovalRequests.AsNoTracking().AnyAsync(r =>
            r.TenantId == tenantId && r.TargetType == targetType && r.TargetId == targetId
            && r.ActionType == actionType && r.Status == WorkApprovalRequestStatuses.Pending, ct);

    public async Task<IReadOnlyList<WorkApprovalRequest>> ListByProjectAsync(
        Guid tenantId, Guid projectId, Guid? requestedByEmployeeId, string? status, CancellationToken ct = default)
    {
        var query = _db.WorkApprovalRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ProjectId == projectId);
        if (requestedByEmployeeId is { } requester)
            query = query.Where(r => r.RequestedByEmployeeId == requester);
        if (status is not null)
            query = query.Where(r => r.Status == status);

        // Sort client-side: SQLite (unit tests) cannot ORDER BY DateTimeOffset, and the per-project
        // row count is small.
        var rows = await query.ToListAsync(ct);
        return rows.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public void Update(WorkApprovalRequest request) => _db.WorkApprovalRequests.Update(request);
}
```

`src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkNotificationLogRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkNotificationLogRepository : IWorkNotificationLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkNotificationLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkNotificationLog log, CancellationToken ct = default)
        => await _db.WorkNotificationLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<WorkNotificationLog>> ListForRecipientAsync(
        Guid tenantId, Guid projectId, Guid recipientEmployeeId, int skip, int take, CancellationToken ct = default)
        => await _db.WorkNotificationLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ProjectId == projectId && l.RecipientEmployeeId == recipientEmployeeId)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
}
```

> If the `ListForRecipientAsync` ordering later fails under a SQLite test ("DateTimeOffset not supported in ORDER BY"), switch it to the same client-side sort pattern as `ListByProjectAsync`. Production runs on Npgsql, where server-side ordering is correct.

- [ ] **Step 9: Register in DI**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, under the `ITaskEditRequestRepository` registration:

```csharp
        services.AddScoped<EfWorkApprovalRequestRepository>();
        services.AddScoped<IWorkApprovalRequestRepository>(sp => sp.GetRequiredService<EfWorkApprovalRequestRepository>());
        services.AddScoped<EfWorkNotificationLogRepository>();
        services.AddScoped<IWorkNotificationLogRepository>(sp => sp.GetRequiredService<EfWorkNotificationLogRepository>());
```

Add usings:
- `ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces`
- `ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces`

- [ ] **Step 10: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfWorkApprovalRequestRepositoryTests"`
Expected: 2 passed.

- [ ] **Step 11: Generate the migration**

```bash
dotnet build src/ONEVO.Application
dotnet ef migrations add AddWorkApprovalEngineFoundation --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.API/ONEVO.Api.csproj
```

If it fails with NuGet.targets "path1 null", run `dotnet build-server shutdown` and retry. That error comes from a stale MSBuild server, not from the code.

Expected, in the generated `Up`:
- 3 `AddColumn<Guid>` calls (`creator_position_objective_id` on `objectives`, `sprints`, `tasks`);
- 2 `CreateTable` calls;
- the indexes.

**Check the diff of `ApplicationDbContextModelSnapshot.cs`: it must only add these items.** If unrelated modules show up as changed, the snapshot is stale from another checkout. Stop and diagnose before continuing.

- [ ] **Step 12: Append the backfill and RLS to the migration**

At the top of the generated migration class, add:

```csharp
        private static readonly string[] TenantTables =
        [
            "wm_approval_requests",
            "wm_notification_log"
        ];
```

At the **end** of `Up`, append:

```csharp
            // Backfill creator positions (spec §4). onevo_migrator is NOBYPASSRLS, so each statement
            // must open admin tenant context in the same batch or it silently updates zero rows.
            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE objectives
                SET creator_position_objective_id = parent_objective_id
                WHERE creator_position_objective_id IS NULL AND parent_objective_id IS NOT NULL;

                WITH RECURSIVE chain AS (
                    SELECT t.id AS task_id, o.id AS module_id, o.parent_objective_id, o.owner_id, 0 AS depth
                    FROM tasks t JOIN objectives o ON o.id = t.objective_id
                    UNION ALL
                    SELECT c.task_id, p.id, p.parent_objective_id, p.owner_id, c.depth + 1
                    FROM chain c JOIN objectives p ON p.id = c.parent_objective_id
                    WHERE c.depth < 64
                ),
                owned AS (
                    SELECT DISTINCT ON (c.task_id) c.task_id, c.module_id
                    FROM chain c
                    JOIN tasks t ON t.id = c.task_id
                    JOIN employees e ON e.user_id = t.created_by_id AND e.tenant_id = t.tenant_id AND e.id = c.owner_id
                    ORDER BY c.task_id, c.depth DESC
                )
                UPDATE tasks t
                SET creator_position_objective_id = COALESCE(owned.module_id, t.objective_id)
                FROM tasks t2 LEFT JOIN owned ON owned.task_id = t2.id
                WHERE t.id = t2.id AND t.creator_position_objective_id IS NULL;

                WITH RECURSIVE depths AS (
                    SELECT o.id, o.project_id, o.owner_id, 0 AS depth
                    FROM objectives o WHERE o.parent_objective_id IS NULL
                    UNION ALL
                    SELECT c.id, c.project_id, c.owner_id, d.depth + 1
                    FROM objectives c JOIN depths d ON c.parent_objective_id = d.id
                    WHERE d.depth < 64
                ),
                highest AS (
                    SELECT DISTINCT ON (s.id) s.id AS sprint_id, d.id AS module_id
                    FROM sprints s
                    JOIN employees e ON e.user_id = s.created_by_id AND e.tenant_id = s.tenant_id
                    JOIN depths d ON d.project_id = s.project_id AND d.owner_id = e.id
                    ORDER BY s.id, d.depth ASC
                ),
                roots AS (
                    SELECT project_id, id AS module_id FROM objectives WHERE is_default = true
                )
                UPDATE sprints s
                SET creator_position_objective_id = COALESCE(highest.module_id, roots.module_id)
                FROM sprints s2
                LEFT JOIN highest ON highest.sprint_id = s2.id
                LEFT JOIN roots ON roots.project_id = s2.project_id
                WHERE s.id = s2.id AND s.creator_position_objective_id IS NULL;
            ");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
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
            }
```

At the **start** of `Down`, before the generated drops, add:

```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
```

- [ ] **Step 13: Verify the migration script and the architecture suite**

```bash
dotnet ef migrations script <previous-migration-name> AddWorkApprovalEngineFoundation --project src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj --startup-project src/ONEVO.API/ONEVO.Api.csproj -o <scratchpad>/wm_foundation.sql
dotnet test tests/ONEVO.Tests.Architecture
```

- `<previous-migration-name>` is the migration listed just before the new one in `src/ONEVO.Infrastructure/Migrations`.
- Expected: the script contains the three UPDATEs and two `CREATE POLICY`, and the architecture suite is all green (including `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage`).
- **Do not run `database update`.** The user applies migrations.

- [ ] **Step 14: Commit**

```bash
git add src/ONEVO.Domain src/ONEVO.Application/Features/WorkManagement src/ONEVO.Infrastructure tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/EfWorkApprovalRequestRepositoryTests.cs
git commit -m "feat(work-management): add approval request, notification log and creator position schema

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `ProjectModuleTree` and `IWorkHierarchyService`

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Hierarchy/ProjectModuleTree.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Hierarchy/IWorkHierarchyService.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Hierarchy/WorkHierarchyService.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (WM block)
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Hierarchy/ProjectModuleTreeTests.cs`
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Hierarchy/WorkHierarchyServiceTests.cs`

**Interfaces:**
- Consumes:
  - `IObjectiveRepository.GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken)`
  - `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync(Guid tenantId, Guid employeeId, CancellationToken)`, which returns `Employee?`
- Produces:
  - `ProjectModuleTree` with `Get(Guid)`, `Root`, `AncestorChain(Guid)`, `IsAtOrAbove(Guid employeeId, Guid moduleId)` and `HighestOwnedModuleId(Guid employeeId)`
  - `IWorkHierarchyService.LoadTreeAsync(Guid tenantId, Guid projectId, CancellationToken)`, which returns `Task<ProjectModuleTree>`
  - `IWorkHierarchyService.FindActiveHolderAsync(Guid tenantId, ProjectModuleTree tree, Guid positionModuleId, Guid excludingEmployeeId, CancellationToken)`, which returns `Task<Guid?>`

- [ ] **Step 1: Write the failing tree tests (the A/B/CC/X example lives here)**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Hierarchy/ProjectModuleTreeTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Hierarchy;

public class ProjectModuleTreeTests
{
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Cc = Guid.NewGuid();
    private static readonly Guid X = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static Objective Module(Guid id, Guid? parent, Guid owner, bool isDefault = false) => new()
    {
        Id = id, ParentObjectiveId = parent, OwnerId = owner, IsDefault = isDefault, Title = id.ToString()
    };

    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    private ProjectModuleTree Tree(Guid pOwner) => new(new[]
    {
        Module(_root, null, Lead, isDefault: true),
        Module(_p, _root, pOwner),
        Module(_c, _p, Cc),
    });

    [Fact]
    public void AncestorChain_IsSelfToRoot()
        => Tree(A).AncestorChain(_c).Select(m => m.Id).Should().Equal(_c, _p, _root);

    [Fact]
    public void IsAtOrAbove_OwnerOfPositionOrAncestor_True_ChildOwner_False()
    {
        var tree = Tree(A);
        tree.IsAtOrAbove(A, _p).Should().BeTrue();
        tree.IsAtOrAbove(Lead, _p).Should().BeTrue();
        tree.IsAtOrAbove(Cc, _p).Should().BeFalse();
        tree.IsAtOrAbove(Stranger, _p).Should().BeFalse();
    }

    [Fact]
    public void Transfer_MovesTheRightToTheNewHolder()
    {
        // Spec §4: after P moves from A to X, X (not A) holds C's creator position.
        var tree = Tree(X);
        tree.IsAtOrAbove(X, _p).Should().BeTrue();
        tree.IsAtOrAbove(A, _p).Should().BeFalse();
    }

    [Fact]
    public void HighestOwnedModuleId_PicksClosestToRoot()
    {
        var tree = new ProjectModuleTree(new[]
        {
            Module(_root, null, Lead, isDefault: true),
            Module(_p, _root, A),
            Module(_c, _p, A),
        });
        tree.HighestOwnedModuleId(A).Should().Be(_p);
        tree.HighestOwnedModuleId(Stranger).Should().BeNull();
    }

    [Fact]
    public void Root_IsTheDefaultModule()
        => Tree(A).Root!.Id.Should().Be(_root);

    [Fact]
    public void AncestorChain_CycleInData_Terminates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var tree = new ProjectModuleTree(new[] { Module(a, b, A), Module(b, a, X) });
        tree.AncestorChain(a).Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ProjectModuleTreeTests"`
Expected: build FAIL, `ProjectModuleTree` not found.

- [ ] **Step 3: Implement `ProjectModuleTree`**

`src/ONEVO.Application/Features/WorkManagement/Hierarchy/ProjectModuleTree.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

/// <summary>
/// Immutable in-memory view of one project's Module (Objective) tree - the single place the
/// "walk up ParentObjectiveId" rule lives. Loaded once per operation by IWorkHierarchyService so
/// every hierarchy question after that is a dictionary walk, not a query per level.
/// </summary>
public sealed class ProjectModuleTree
{
    private readonly IReadOnlyDictionary<Guid, Objective> _byId;

    public ProjectModuleTree(IEnumerable<Objective> modules)
        => _byId = modules.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

    public Objective? Get(Guid moduleId) => _byId.GetValueOrDefault(moduleId);

    /// <summary>The project's default (top) Module; falls back to any parentless Module.</summary>
    public Objective? Root =>
        _byId.Values.FirstOrDefault(m => m.ParentObjectiveId is null && m.IsDefault)
        ?? _byId.Values.FirstOrDefault(m => m.ParentObjectiveId is null);

    /// <summary>Self first, root last. Stops on missing parents and on cycles in bad data.</summary>
    public IReadOnlyList<Objective> AncestorChain(Guid moduleId)
    {
        var chain = new List<Objective>();
        var seen = new HashSet<Guid>();
        var cursor = Get(moduleId);
        while (cursor is not null && seen.Add(cursor.Id))
        {
            chain.Add(cursor);
            cursor = cursor.ParentObjectiveId is { } parentId ? Get(parentId) : null;
        }
        return chain;
    }

    /// <summary>True when the employee owns the position Module or any of its ancestors.</summary>
    public bool IsAtOrAbove(Guid employeeId, Guid positionModuleId)
        => AncestorChain(positionModuleId).Any(m => m.OwnerId == employeeId);

    /// <summary>The Module owned by this employee that is closest to the root, or null.</summary>
    public Guid? HighestOwnedModuleId(Guid employeeId)
        => _byId.Values
            .Where(m => m.OwnerId == employeeId)
            .Select(m => (m.Id, Depth: AncestorChain(m.Id).Count))
            .OrderBy(x => x.Depth)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();
}
```

- [ ] **Step 4: Run the tree tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ProjectModuleTreeTests"`
Expected: 6 passed.

- [ ] **Step 5: Write the failing service tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Hierarchy/WorkHierarchyServiceTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Hierarchy;

public class WorkHierarchyServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Cc = Guid.NewGuid();

    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    public WorkHierarchyServiceTests()
    {
        _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>
            {
                new() { Id = _root, ProjectId = ProjectId, OwnerId = Lead, IsDefault = true },
                new() { Id = _p, ProjectId = ProjectId, ParentObjectiveId = _root, OwnerId = A },
                new() { Id = _c, ProjectId = ProjectId, ParentObjectiveId = _p, OwnerId = Cc },
            });
    }

    private void Active(Guid employeeId)
        => _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, UserId = Guid.NewGuid() });

    private WorkHierarchyService Build() => new(_objectives.Object, _membership.Object);

    [Fact]
    public async Task FindActiveHolder_ReturnsPositionOwner_WhenActive()
    {
        Active(A);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().Be(A);
    }

    [Fact]
    public async Task FindActiveHolder_InactiveOwner_WalksUpToNextActiveOwner()
    {
        Active(Lead); // A has no active employee record
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().Be(Lead);
    }

    [Fact]
    public async Task FindActiveHolder_NobodyActive_ReturnsNull()
    {
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveHolder_SkipsTheExcludedEmployee()
    {
        Active(A);
        Active(Lead);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, A)).Should().Be(Lead);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkHierarchyServiceTests"`
Expected: build FAIL, `WorkHierarchyService` not found.

- [ ] **Step 7: Implement the service**

`src/ONEVO.Application/Features/WorkManagement/Hierarchy/IWorkHierarchyService.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

/// <summary>
/// Work Management's hierarchy service: the project Module tree plus "who currently holds this
/// position". Modelled on CoreHr's IEmployeeAuthorityResolver (single interface, fixed walk order,
/// fails closed with null) but over the project tree instead of the org chart.
/// </summary>
public interface IWorkHierarchyService
{
    Task<ProjectModuleTree> LoadTreeAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Walks the position Module and then its ancestors, and returns the first owner who is
    /// an active employee and is not excludingEmployeeId. Null if nobody qualifies.</summary>
    Task<Guid?> FindActiveHolderAsync(
        Guid tenantId, ProjectModuleTree tree, Guid positionModuleId, Guid excludingEmployeeId, CancellationToken ct = default);
}
```

`src/ONEVO.Application/Features/WorkManagement/Hierarchy/WorkHierarchyService.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

public sealed class WorkHierarchyService : IWorkHierarchyService
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public WorkHierarchyService(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _objectives = objectives;
        _membership = membership;
    }

    public async Task<ProjectModuleTree> LoadTreeAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => new(await _objectives.GetAllByProjectIdAsync(tenantId, projectId, ct));

    public async Task<Guid?> FindActiveHolderAsync(
        Guid tenantId, ProjectModuleTree tree, Guid positionModuleId, Guid excludingEmployeeId, CancellationToken ct = default)
    {
        var checkedOwners = new HashSet<Guid>();
        foreach (var module in tree.AncestorChain(positionModuleId))
        {
            var ownerId = module.OwnerId;
            if (ownerId == excludingEmployeeId || !checkedOwners.Add(ownerId))
                continue;
            if (await _membership.GetActiveAssigneeAsync(tenantId, ownerId, ct) is not null)
                return ownerId;
        }
        return null;
    }
}
```

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, WM block:

```csharp
        services.AddScoped<IWorkHierarchyService, WorkHierarchyService>();
```

Add `using ONEVO.Application.Features.WorkManagement.Hierarchy;`.

- [ ] **Step 8: Run both test classes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~Features.WorkManagement.Hierarchy"`
Expected: 10 passed.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Hierarchy src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Hierarchy
git commit -m "feat(work-management): add project module tree and hierarchy service

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Notification engine and templates

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkActionLabels.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/IWorkNotificationEngine.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs`
- Modify:
  - `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` (WM block, after `work_sprint_achieved`)
  - `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications/WorkNotificationEngineTests.cs`

**Interfaces:**
- Consumes:
  - `IWorkNotificationLogRepository.AddAsync`
  - `IOutboxWriter.EnqueueAsync<T>(string type, T payload, Guid? tenantId, CancellationToken)`
  - `WorkNotificationPayload(Guid TenantId, Guid RecipientUserId, string TemplateCode, Dictionary<string,string> Placeholders, string? RelatedEntityType, Guid? RelatedEntityId)`
  - `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`
  - `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync`
- Produces:
  - `WorkNotificationEvent` (record below)
  - `IWorkNotificationEngine.NotifyAsync(WorkNotificationEvent e, CancellationToken ct)`, which returns `Task`
  - `WorkActionLabels.For(string actionType)`, which returns `string`
  - template codes `work_activity_recorded`, `work_approval_requested`, `work_approval_decided`

- [ ] **Step 1: Write the failing tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications/WorkNotificationEngineTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Notifications;

public class WorkNotificationEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid OwnerUser = Guid.NewGuid();
    private static readonly Guid Inactive = Guid.NewGuid();

    private readonly Mock<IWorkNotificationLogRepository> _logs = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly List<WorkNotificationLog> _written = new();
    private readonly List<WorkNotificationPayload> _enqueued = new();

    public WorkNotificationEngineTests()
    {
        _logs.Setup(x => x.AddAsync(It.IsAny<WorkNotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<WorkNotificationLog, CancellationToken>((l, _) => _written.Add(l)).Returns(Task.CompletedTask);
        _outbox.Setup(x => x.EnqueueAsync(It.IsAny<string>(), It.IsAny<WorkNotificationPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, WorkNotificationPayload, Guid?, CancellationToken>((_, p, _, _) => _enqueued.Add(p)).Returns(Task.CompletedTask);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Actor] = "Bala" });
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Owner, UserId = OwnerUser });
    }

    private WorkNotificationEngine Build() => new(_logs.Object, _outbox.Object, _identity.Object, _membership.Object);

    private static WorkNotificationEvent Event(string kind, params Guid[] recipients) => new(
        TenantId, ProjectId, Actor, kind, WorkActionTypes.TaskEdit, WorkTargetTypes.Task,
        Guid.NewGuid(), "Audit events", kind == WorkNotificationKinds.Direct ? null : Guid.NewGuid(), recipients);

    [Fact]
    public async Task Direct_WritesLogAndEnqueuesActivityTemplate_PerActiveRecipient()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Owner));

        _written.Should().ContainSingle(l => l.RecipientEmployeeId == Owner && l.Kind == WorkNotificationKinds.Direct && l.ProjectId == ProjectId);
        _enqueued.Should().ContainSingle();
        _enqueued[0].RecipientUserId.Should().Be(OwnerUser);
        _enqueued[0].TemplateCode.Should().Be("work_activity_recorded");
        _enqueued[0].Placeholders["actorName"].Should().Be("Bala");
        _enqueued[0].Placeholders["actionLabel"].Should().Be("edited the task");
        _enqueued[0].RelatedEntityType.Should().Be(WorkTargetTypes.Task);
    }

    [Fact]
    public async Task ActorAndDuplicatesAndInactiveRecipients_AreSkipped()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Actor, Owner, Owner, Inactive));

        _written.Select(l => l.RecipientEmployeeId).Should().Equal(Owner);
        _enqueued.Should().HaveCount(1);
    }

    [Fact]
    public async Task Requested_UsesRequestTemplateAndLinksTheApproval()
    {
        var e = Event(WorkNotificationKinds.Requested, Owner);
        await Build().NotifyAsync(e);

        _enqueued[0].TemplateCode.Should().Be("work_approval_requested");
        _enqueued[0].RelatedEntityType.Should().Be("work_approval_request");
        _enqueued[0].RelatedEntityId.Should().Be(e.ApprovalRequestId);
        _written[0].ApprovalRequestId.Should().Be(e.ApprovalRequestId);
    }

    [Theory]
    [InlineData(WorkNotificationKinds.Approved, "approved")]
    [InlineData(WorkNotificationKinds.Rejected, "rejected")]
    [InlineData(WorkNotificationKinds.Cancelled, "cancelled")]
    [InlineData(WorkNotificationKinds.Stale, "closed as outdated")]
    public async Task Decisions_UseDecidedTemplateWithDecisionWord(string kind, string decision)
    {
        await Build().NotifyAsync(Event(kind, Owner));

        _enqueued[0].TemplateCode.Should().Be("work_approval_decided");
        _enqueued[0].Placeholders["decision"].Should().Be(decision);
    }

    [Fact]
    public async Task NoRecipientsLeft_DoesNothing()
    {
        await Build().NotifyAsync(Event(WorkNotificationKinds.Direct, Actor));

        _written.Should().BeEmpty();
        _identity.Verify(x => x.ResolveDisplayNamesByEmployeeIdAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkNotificationEngineTests"`
Expected: build FAIL, `WorkNotificationEngine` not found.

- [ ] **Step 3: Implement the labels, the engine and the interface**

`src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkActionLabels.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

/// <summary>Past-tense phrase used as {{actionLabel}} in the work_* templates.</summary>
public static class WorkActionLabels
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [WorkActionTypes.TaskCreate] = "created the task",
        [WorkActionTypes.TaskEdit] = "edited the task",
        [WorkActionTypes.TaskDelete] = "deleted the task",
        [WorkActionTypes.TaskStatusChange] = "changed the status of the task",
        [WorkActionTypes.ModuleEdit] = "edited the module",
        [WorkActionTypes.ModuleDelete] = "deleted the module",
        [WorkActionTypes.ModuleTransfer] = "transferred the module",
        [WorkActionTypes.ModuleAchieve] = "achieved the module",
        [WorkActionTypes.ModuleUnachieve] = "reopened the module",
        [WorkActionTypes.ModuleAllocationExtend] = "extended the allocation of the module",
        [WorkActionTypes.SprintCreate] = "created the sprint",
        [WorkActionTypes.SprintEdit] = "edited the sprint",
        [WorkActionTypes.SprintDelete] = "deleted the sprint",
    };

    public static string For(string actionType) => Labels.GetValueOrDefault(actionType, "changed");
}
```

`src/ONEVO.Application/Features/WorkManagement/Notifications/Services/IWorkNotificationEngine.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

/// <summary>Kind is a WorkNotificationKinds value. ApprovalRequestId is set for every kind except Direct.</summary>
public sealed record WorkNotificationEvent(
    Guid TenantId,
    Guid ProjectId,
    Guid ActorEmployeeId,
    string Kind,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid? ApprovalRequestId,
    IReadOnlyCollection<Guid> RecipientEmployeeIds);

/// <summary>
/// The Work Management notification engine: one wm_notification_log row plus one WorkNotification
/// outbox message per recipient. The actor, duplicates and inactive employees are skipped. Never
/// calls SaveChangesAsync - callers run it inside the same transaction as the change it describes,
/// so a rollback also drops the notification.
/// </summary>
public interface IWorkNotificationEngine
{
    Task NotifyAsync(WorkNotificationEvent notification, CancellationToken ct = default);
}
```

`src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Services;

public sealed class WorkNotificationEngine : IWorkNotificationEngine
{
    public const string ActivityTemplate = "work_activity_recorded";
    public const string RequestedTemplate = "work_approval_requested";
    public const string DecidedTemplate = "work_approval_decided";
    public const string ApprovalRelatedEntityType = "work_approval_request";

    private readonly IWorkNotificationLogRepository _logs;
    private readonly IOutboxWriter _outbox;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMilestoneMembershipCoordinator _membership;

    public WorkNotificationEngine(
        IWorkNotificationLogRepository logs, IOutboxWriter outbox,
        ICallerIdentityResolver identity, IMilestoneMembershipCoordinator membership)
    {
        _logs = logs;
        _outbox = outbox;
        _identity = identity;
        _membership = membership;
    }

    public async Task NotifyAsync(WorkNotificationEvent e, CancellationToken ct = default)
    {
        var recipients = e.RecipientEmployeeIds
            .Where(id => id != Guid.Empty && id != e.ActorEmployeeId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0)
            return;

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(e.TenantId, [e.ActorEmployeeId], ct);
        var placeholders = new Dictionary<string, string>
        {
            ["actorName"] = names.GetValueOrDefault(e.ActorEmployeeId) ?? "A teammate",
            ["actionLabel"] = WorkActionLabels.For(e.ActionType),
            ["targetTitle"] = e.TargetTitle,
            ["decision"] = DecisionWord(e.Kind),
        };
        var template = e.Kind switch
        {
            WorkNotificationKinds.Direct => ActivityTemplate,
            WorkNotificationKinds.Requested => RequestedTemplate,
            _ => DecidedTemplate,
        };
        var (relatedType, relatedId) = e.ApprovalRequestId is { } requestId
            ? (ApprovalRelatedEntityType, (Guid?)requestId)
            : (e.TargetType, e.TargetId);

        foreach (var recipientId in recipients)
        {
            var recipient = await _membership.GetActiveAssigneeAsync(e.TenantId, recipientId, ct);
            if (recipient is null)
                continue;

            await _logs.AddAsync(new WorkNotificationLog
            {
                Id = Guid.NewGuid(), TenantId = e.TenantId, ProjectId = e.ProjectId,
                RecipientEmployeeId = recipientId, ActorEmployeeId = e.ActorEmployeeId, Kind = e.Kind,
                ActionType = e.ActionType, TargetType = e.TargetType, TargetId = e.TargetId,
                TargetTitle = e.TargetTitle, ApprovalRequestId = e.ApprovalRequestId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(e.TenantId, recipient.UserId, template, new Dictionary<string, string>(placeholders), relatedType, relatedId),
                e.TenantId,
                ct);
        }
    }

    private static string DecisionWord(string kind) => kind switch
    {
        WorkNotificationKinds.Approved => "approved",
        WorkNotificationKinds.Rejected => "rejected",
        WorkNotificationKinds.Cancelled => "cancelled",
        WorkNotificationKinds.Stale => "closed as outdated",
        _ => string.Empty,
    };
}
```

DI, WM block: `services.AddScoped<IWorkNotificationEngine, WorkNotificationEngine>();`, plus the using `ONEVO.Application.Features.WorkManagement.Notifications.Services`.

- [ ] **Step 4: Add the three templates**

In `NotificationTemplateSeeder.cs`, directly after the `work_sprint_achieved` entry:

```csharp
            new()
            {
                Id = Guid.NewGuid(), Code = "work_activity_recorded",
                InAppTitleTemplate = "Work update",
                InAppBodyTemplate = "{{actorName}} {{actionLabel}} \"{{targetTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_approval_requested",
                InAppTitleTemplate = "Approval needed",
                InAppBodyTemplate = "{{actorName}} is waiting for your approval: {{actionLabel}} \"{{targetTitle}}\"."
            },
            new()
            {
                Id = Guid.NewGuid(), Code = "work_approval_decided",
                InAppTitleTemplate = "Request {{decision}}",
                InAppBodyTemplate = "{{actorName}}'s request ({{actionLabel}} \"{{targetTitle}}\") was {{decision}}."
            },
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkNotificationEngineTests"`
Expected: 8 passed (4 facts + 4 theory cases).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Notifications src/ONEVO.Infrastructure tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications
git commit -m "feat(work-management): add notification engine with project notification log

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Approval engine (`SubmitAsync`) and decision rules

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IWorkApprovalEngine.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/WorkApprovalEngine.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/WorkApprovalDecisionRules.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/WorkApprovalEngineTests.cs`
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/WorkApprovalDecisionRulesTests.cs`

**Interfaces:**
- Consumes:
  - `IWorkHierarchyService` and `ProjectModuleTree` (Task 2)
  - `IWorkNotificationEngine` (Task 3)
  - `IWorkApprovalRequestRepository` (Task 1)
  - `IProjectRepository.GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken)`, which returns `Project?` with `OwningLegalEntityId`
  - `IEmployeeAuthorityResolver.ResolveApproverAsync(EmployeeApprovalRouteRequest, CancellationToken)`, which returns `Result<EmployeeApprovalRoute>`
- Produces:
  - `WorkAction` and `ApprovalDecision` (records below)
  - `IWorkApprovalEngine.SubmitAsync(WorkAction action, CancellationToken ct)`, which returns `Task<Result<ApprovalDecision>>`
  - `WorkApprovalDecisionRules.CanDecide(ProjectModuleTree tree, WorkApprovalRequest request, Guid employeeId)`, which returns `bool`

- [ ] **Step 1: Write the failing engine tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/WorkApprovalEngineTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class WorkApprovalEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();   // root owner
    private static readonly Guid A = Guid.NewGuid();      // owner of P
    private static readonly Guid Cc = Guid.NewGuid();     // owner of C (child of P)
    private static readonly Guid B = Guid.NewGuid();      // member
    private static readonly Guid HrManager = Guid.NewGuid();

    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly Mock<IProjectRepository> _projects = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authority = new();
    private readonly List<WorkApprovalRequest> _added = new();

    public WorkApprovalEngineTests()
    {
        var tree = new ProjectModuleTree(new[]
        {
            new Objective { Id = _root, OwnerId = Lead, IsDefault = true },
            new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
            new Objective { Id = _c, ParentObjectiveId = _p, OwnerId = Cc },
        });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _p, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(A);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _c, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Cc);
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, tree, _root, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Lead);
        _requests.Setup(x => x.AddAsync(It.IsAny<WorkApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WorkApprovalRequest, CancellationToken>((r, _) => _added.Add(r)).Returns(Task.CompletedTask);
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, OwningLegalEntityId = LegalEntityId });
    }

    private WorkApprovalEngine Build() => new(_hierarchy.Object, _requests.Object, _notifications.Object, _projects.Object, _authority.Object);

    private WorkAction Action(Guid actor, string actionType, string targetType, Guid targetModuleId, Guid? position, Guid? targetId = null)
        => new(TenantId, ProjectId, actor, actionType, targetType, targetId ?? Guid.NewGuid(), "Thing",
            targetModuleId, position, "{}", null);

    [Fact]
    public async Task ChildModuleOwnerEditsOwnModule_RequestGoesToParentOwner()
    {
        // B/CC edits C, whose creator position is P → P's owner (A) approves.
        var result = await Build().SubmitAsync(Action(Cc, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c));

        result.Value!.IsDirect.Should().BeFalse();
        _added.Should().ContainSingle();
        _added[0].ApproverEmployeeId.Should().Be(A);
        _added[0].PositionObjectiveId.Should().Be(_p);
        _added[0].ApproverSource.Should().Be(WorkApprovalSources.Hierarchy);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Requested && e.RecipientEmployeeIds.Single() == A), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task PositionHolderOrAncestor_IsDirect_NoRequest()
    {
        (await Build().SubmitAsync(Action(A, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c))).Value!.IsDirect.Should().BeTrue();
        (await Build().SubmitAsync(Action(Lead, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _c, _p, _c))).Value!.IsDirect.Should().BeTrue();
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task MemberCreatesTask_RequestGoesToModuleOwner()
    {
        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskCreate, WorkTargetTypes.Task, _c, null) with { TargetId = null });

        result.Value!.IsDirect.Should().BeFalse();
        _added[0].ApproverEmployeeId.Should().Be(Cc);
        _added[0].TargetId.Should().BeNull();
    }

    [Fact]
    public async Task NullPosition_TaskFallsBackToTargetModule()
    {
        (await Build().SubmitAsync(Action(Cc, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, null))).Value!.IsDirect.Should().BeTrue();
    }

    [Fact]
    public async Task RootOwnerEditsRoot_GoesToHrReportingApprover()
    {
        _authority.Setup(x => x.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == Lead && r.LegalEntityId == LegalEntityId && r.RequiredPermission == "projects:access"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                HrManager, Guid.NewGuid(), Guid.NewGuid(), "projects:access", EmployeeAuthorityPurpose.EmployeeLifecycleApproval, EmployeeApprovalRouteSource.ReportingLine, null)));

        var result = await Build().SubmitAsync(Action(Lead, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _root, null, _root));

        result.Value!.IsDirect.Should().BeFalse();
        _added[0].ApproverEmployeeId.Should().Be(HrManager);
        _added[0].ApproverSource.Should().Be(WorkApprovalSources.Hr);
        _added[0].PositionObjectiveId.Should().BeNull();
    }

    [Fact]
    public async Task SomeoneElseEditsRoot_RootOwnerApproves()
    {
        await Build().SubmitAsync(Action(A, WorkActionTypes.ModuleEdit, WorkTargetTypes.Module, _root, null, _root));
        _added[0].ApproverEmployeeId.Should().Be(Lead);
    }

    [Fact]
    public async Task NoHolderAndNoHrApprover_Returns422()
    {
        _hierarchy.Setup(x => x.FindActiveHolderAsync(TenantId, It.IsAny<ProjectModuleTree>(), _c, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        _authority.Setup(x => x.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalRoute>.UnprocessableEntity("none"));

        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, _c));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicatePending_Returns409()
    {
        var targetId = Guid.NewGuid();
        _requests.Setup(x => x.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, _c, targetId));

        result.StatusCode.Should().Be(409);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownPositionModule_Returns404()
    {
        var result = await Build().SubmitAsync(Action(B, WorkActionTypes.TaskEdit, WorkTargetTypes.Task, _c, Guid.NewGuid()));
        result.StatusCode.Should().Be(404);
    }
}
```

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/WorkApprovalDecisionRulesTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class WorkApprovalDecisionRulesTests
{
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid X = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid HrManager = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();

    private ProjectModuleTree Tree(Guid pOwner) => new(new[]
    {
        new Objective { Id = _root, OwnerId = Lead, IsDefault = true },
        new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = pOwner },
    });

    private WorkApprovalRequest Hierarchy(Guid approver) => new()
    {
        PositionObjectiveId = _p, ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = approver, RequestedByEmployeeId = Requester
    };

    [Fact]
    public void AfterTransfer_NewHolderDecides_OldHolderCannot()
    {
        var request = Hierarchy(A); // A was notified before the transfer
        var tree = Tree(X);         // P now owned by X
        WorkApprovalDecisionRules.CanDecide(tree, request, X).Should().BeTrue();
        WorkApprovalDecisionRules.CanDecide(tree, request, A).Should().BeFalse();
        WorkApprovalDecisionRules.CanDecide(tree, request, Lead).Should().BeTrue();
    }

    [Fact]
    public void RequesterCanNeverDecideOwnRequest()
        => WorkApprovalDecisionRules.CanDecide(Tree(Requester), Hierarchy(Requester), Requester).Should().BeFalse();

    [Fact]
    public void HrApproval_OnlyTheResolvedApprover()
    {
        var request = new WorkApprovalRequest
        {
            PositionObjectiveId = null, ApproverSource = WorkApprovalSources.Hr,
            ApproverEmployeeId = HrManager, RequestedByEmployeeId = Lead
        };
        WorkApprovalDecisionRules.CanDecide(Tree(A), request, HrManager).Should().BeTrue();
        WorkApprovalDecisionRules.CanDecide(Tree(A), request, A).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkApprovalEngineTests|FullyQualifiedName~WorkApprovalDecisionRulesTests"`
Expected: build FAIL, types not found.

- [ ] **Step 3: Implement the interface, the rules and the engine**

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IWorkApprovalEngine.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>
/// One Work Management action as the approval engine sees it.
/// - TargetModuleId: the Module the target lives in (for a Module target, the Module itself; for a
///   create, the Module being created into; for a Sprint, the project root Module).
/// - PositionModuleId: the target's CreatorPositionObjectiveId (for a create, the caller's choice
///   per spec §5.2 rule 1); null means "use the default".
/// - TargetUpdatedAt: the target's UpdatedAt at submit time, for stale detection.
/// </summary>
public sealed record WorkAction(
    Guid TenantId,
    Guid ProjectId,
    Guid ActorEmployeeId,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid TargetModuleId,
    Guid? PositionModuleId,
    string PayloadJson,
    DateTimeOffset? TargetUpdatedAt);

public sealed record ApprovalDecision(bool IsDirect, Guid? ApprovalRequestId, Guid? ApproverEmployeeId)
{
    public static ApprovalDecision Direct { get; } = new(true, null, null);
    public static ApprovalDecision Pending(Guid requestId, Guid approverEmployeeId) => new(false, requestId, approverEmployeeId);
}

/// <summary>
/// The Work Management approval engine. Direct means "apply now": the caller applies the change and
/// calls IWorkNotificationEngine with Kind = Direct. Pending means a wm_approval_requests row was
/// added and the approver was notified. Never calls SaveChangesAsync - run it inside the caller's
/// transaction.
/// </summary>
public interface IWorkApprovalEngine
{
    Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default);
}
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/WorkApprovalDecisionRules.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Who may approve or reject a pending request, evaluated at decision time (spec §5.3).</summary>
public static class WorkApprovalDecisionRules
{
    public static bool CanDecide(ProjectModuleTree tree, WorkApprovalRequest request, Guid employeeId)
    {
        if (employeeId == request.RequestedByEmployeeId)
            return false;

        if (request.ApproverSource == WorkApprovalSources.Hr || request.PositionObjectiveId is null)
            return employeeId == request.ApproverEmployeeId;

        // Hierarchy: the position follows transfers, so the check is re-run against today's tree.
        // The stored ApproverEmployeeId can still decide when it is an ancestor that was picked
        // because the position owner was inactive - IsAtOrAbove already covers that.
        return tree.IsAtOrAbove(employeeId, request.PositionObjectiveId.Value);
    }
}
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/WorkApprovalEngine.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class WorkApprovalEngine : IWorkApprovalEngine
{
    /// <summary>Every Work Management user holds it, so the HR fallback resolves the reporting-line
    /// manager instead of failing on a permission most managers were never granted.</summary>
    public const string HrFallbackPermission = "projects:access";

    private readonly IWorkHierarchyService _hierarchy;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IProjectRepository _projects;
    private readonly IEmployeeAuthorityResolver _authority;

    public WorkApprovalEngine(
        IWorkHierarchyService hierarchy, IWorkApprovalRequestRepository requests, IWorkNotificationEngine notifications,
        IProjectRepository projects, IEmployeeAuthorityResolver authority)
    {
        _hierarchy = hierarchy;
        _requests = requests;
        _notifications = notifications;
        _projects = projects;
        _authority = authority;
    }

    public async Task<Result<ApprovalDecision>> SubmitAsync(WorkAction action, CancellationToken ct = default)
    {
        var tree = await _hierarchy.LoadTreeAsync(action.TenantId, action.ProjectId, ct);
        var targetModule = tree.Get(action.TargetModuleId);
        if (targetModule is null)
            return Result<ApprovalDecision>.NotFound("The module this action belongs to was not found.");

        // Spec §5.2 rule 1 + §5.4: a Module with no creator position is a root Module. Its own
        // owner has nobody above them in the tree (so the HR approver decides); anyone else editing
        // it goes to the root owner.
        var position = action.PositionModuleId;
        var useHr = false;
        if (position is null)
        {
            if (action.TargetType == WorkTargetTypes.Module && targetModule.OwnerId == action.ActorEmployeeId)
                useHr = true;
            else
                position = action.TargetModuleId;
        }

        Guid? approver = null;
        var source = WorkApprovalSources.Hierarchy;
        if (!useHr)
        {
            if (tree.Get(position!.Value) is null)
                return Result<ApprovalDecision>.NotFound("The module this action belongs to was not found.");
            if (tree.IsAtOrAbove(action.ActorEmployeeId, position.Value))
                return Result<ApprovalDecision>.Success(ApprovalDecision.Direct);
            approver = await _hierarchy.FindActiveHolderAsync(action.TenantId, tree, position.Value, action.ActorEmployeeId, ct);
        }

        if (approver is null)
        {
            approver = await ResolveHrApproverAsync(action, ct);
            source = WorkApprovalSources.Hr;
            position = null;
        }
        if (approver is null)
            return Result<ApprovalDecision>.UnprocessableEntity("No eligible approver was found for this action.");

        if (action.TargetId is { } targetId
            && await _requests.HasPendingAsync(action.TenantId, action.TargetType, targetId, action.ActionType, ct))
            return Result<ApprovalDecision>.Conflict("A request for this change is already waiting for approval.");

        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = action.TenantId, ProjectId = action.ProjectId,
            ActionType = action.ActionType, TargetType = action.TargetType, TargetId = action.TargetId,
            TargetTitle = action.TargetTitle, PositionObjectiveId = position, ApproverSource = source,
            ApproverEmployeeId = approver.Value, RequestedByEmployeeId = action.ActorEmployeeId,
            PayloadJson = action.PayloadJson, Status = WorkApprovalRequestStatuses.Pending,
            TargetUpdatedAtSnapshot = action.TargetUpdatedAt, CreatedAt = DateTimeOffset.UtcNow
        };
        await _requests.AddAsync(request, ct);

        await _notifications.NotifyAsync(new WorkNotificationEvent(
            action.TenantId, action.ProjectId, action.ActorEmployeeId, WorkNotificationKinds.Requested,
            action.ActionType, action.TargetType, action.TargetId, action.TargetTitle, request.Id, [approver.Value]), ct);

        return Result<ApprovalDecision>.Success(ApprovalDecision.Pending(request.Id, approver.Value));
    }

    private async Task<Guid?> ResolveHrApproverAsync(WorkAction action, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(action.TenantId, action.ProjectId, ct);
        if (project is null)
            return null;

        var route = await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            action.ActorEmployeeId, project.OwningLegalEntityId, HrFallbackPermission,
            EmployeeAuthorityPurpose.EmployeeLifecycleApproval), ct);

        return route.IsSuccess && route.Value!.ApproverEmployeeId != action.ActorEmployeeId
            ? route.Value.ApproverEmployeeId
            : null;
    }
}
```

DI, WM block: `services.AddScoped<IWorkApprovalEngine, WorkApprovalEngine>();`, plus the using `ONEVO.Application.Features.WorkManagement.Approvals.Services`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkApprovalEngineTests|FullyQualifiedName~WorkApprovalDecisionRulesTests"`
Expected: 12 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Approvals/Services src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals
git commit -m "feat(work-management): add creator-position approval engine with HR root fallback

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Applier registry and Approve/Reject/Cancel commands

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionApplier.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalActionApplierRegistry.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/WorkApprovalRequestResponse.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Mappers/WorkApprovalRequestMapper.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/DecideWorkApprovalRequest/DecideWorkApprovalRequestCommand.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/DecideWorkApprovalRequest/DecideWorkApprovalRequestCommandHandler.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/DecideWorkApprovalRequestCommandHandlerTests.cs`
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalActionApplierRegistryTests.cs`

Approve, Reject and Cancel share 90% of their flow (load, pending check, authority, stamp, notify, save), so they are **one command with a `Decision` enum**. That keeps the endpoint surface at three routes without three copies of the handler.

**Interfaces:**
- Consumes:
  - `WorkApprovalDecisionRules.CanDecide` (Task 4)
  - `IWorkHierarchyService.LoadTreeAsync` (Task 2)
  - `IWorkNotificationEngine` (Task 3)
  - `IWorkApprovalRequestRepository` (Task 1)
  - `ICallerIdentityResolver`, `ICurrentUser`, `IUnitOfWork`
- Produces:
  - `IApprovalActionApplier { string ActionType; Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext, CancellationToken) }`
  - `ApprovalApplyContext(WorkApprovalRequest Request, string PayloadJson, Guid DeciderEmployeeId)`
  - `ApplyOutcome` with `Applied`, `Stale`, `Invalid(string)`
  - `IApprovalActionApplierRegistry.Find(string actionType)`, which returns `IApprovalActionApplier?`
  - `DecideWorkApprovalRequestCommand(Guid RequestId, WorkApprovalDecision Decision, string? EditedPayloadJson, string? Comment)`, which returns `IRequest<Result<WorkApprovalRequestResponse>>`
  - `WorkApprovalRequestResponse` (record below)
  - `WorkApprovalRequestMapper.ToResponse(WorkApprovalRequest, IReadOnlyDictionary<Guid,string> names)`

- [ ] **Step 1: Write the failing registry test**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ApprovalActionApplierRegistryTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalActionApplierRegistryTests
{
    private sealed class StubApplier(string actionType) : IApprovalActionApplier
    {
        public string ActionType { get; } = actionType;
        public Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct) => Task.FromResult(ApplyOutcome.Applied);
    }

    [Fact]
    public void Find_ReturnsRegisteredApplier_NullOtherwise()
    {
        var edit = new StubApplier("task.edit");
        var registry = new ApprovalActionApplierRegistry(new[] { edit });
        registry.Find("task.edit").Should().BeSameAs(edit);
        registry.Find("task.delete").Should().BeNull();
    }

    [Fact]
    public void DuplicateActionType_FailsFastAtConstruction()
    {
        var act = () => new ApprovalActionApplierRegistry(new[] { new StubApplier("task.edit"), new StubApplier("task.edit") });
        act.Should().Throw<InvalidOperationException>().WithMessage("*task.edit*");
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/DecideWorkApprovalRequestCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class DecideWorkApprovalRequestCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();        // position holder
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IApprovalActionApplierRegistry> _appliers = new();
    private readonly Mock<IApprovalActionApplier> _applier = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly WorkApprovalRequest _request;

    public DecideWorkApprovalRequestCommandHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
            }));
        _request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = WorkActionTypes.TaskEdit,
            TargetType = WorkTargetTypes.Task, TargetId = Guid.NewGuid(), TargetTitle = "Audit events",
            PositionObjectiveId = _p, ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = A,
            RequestedByEmployeeId = Requester, PayloadJson = "{\"title\":\"old\"}", Status = WorkApprovalRequestStatuses.Pending
        };
        _requests.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, _request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_request);
        _applier.SetupGet(x => x.ActionType).Returns(WorkActionTypes.TaskEdit);
        _appliers.Setup(x => x.Find(WorkActionTypes.TaskEdit)).Returns(_applier.Object);
    }

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private DecideWorkApprovalRequestCommandHandler Build() => new(
        _currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object, _appliers.Object, _notifications.Object, _uow);

    private static DecideWorkApprovalRequestCommand Cmd(Guid id, WorkApprovalDecision d, string? payload = null)
        => new(id, d, payload, "ok");

    [Fact]
    public async Task Approve_ByHolder_AppliesEditedPayload_MarksApproved_NotifiesRequester()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.Is<ApprovalApplyContext>(c => c.PayloadJson == "{\"title\":\"new\"}" && c.DeciderEmployeeId == A), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyOutcome.Applied);

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve, "{\"title\":\"new\"}"), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Approved);
        _request.PayloadJson.Should().Be("{\"title\":\"new\"}");
        _request.DecidedByEmployeeId.Should().Be(A);
        _uow.SaveCallCount.Should().Be(1);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Approved && e.RecipientEmployeeIds.Single() == Requester), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Approve_StaleTarget_MarksStale_AppliesNothingElse()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Stale);

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Stale);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e => e.Kind == WorkNotificationKinds.Stale), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Approve_InvalidPayload_Returns422_StaysPending()
    {
        Caller(A);
        _applier.Setup(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(ApplyOutcome.Invalid("bad title"));

        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default);

        result.StatusCode.Should().Be(422);
        result.Error.Should().Be("bad title");
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
        _uow.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Approve_NoApplierRegistered_Returns422()
    {
        Caller(A);
        _appliers.Setup(x => x.Find(WorkActionTypes.TaskEdit)).Returns((IApprovalActionApplier?)null);
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Approve), default)).StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Reject_ByHolder_MarksRejected_NeverCallsApplier()
    {
        Caller(A);
        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Reject), default);

        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Rejected);
        _applier.Verify(x => x.ApplyAsync(It.IsAny<ApprovalApplyContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WorkApprovalDecision.Approve)]
    [InlineData(WorkApprovalDecision.Reject)]
    public async Task Decide_ByNonHolder_Forbidden(WorkApprovalDecision decision)
    {
        Caller(Stranger);
        (await Build().Handle(Cmd(_request.Id, decision), default)).StatusCode.Should().Be(403);
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Pending);
    }

    [Fact]
    public async Task Cancel_OnlyRequester_NotifiesApprover()
    {
        Caller(A);
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Cancel), default)).StatusCode.Should().Be(403);

        Caller(Requester);
        var result = await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Cancel), default);
        result.IsSuccess.Should().BeTrue();
        _request.Status.Should().Be(WorkApprovalRequestStatuses.Cancelled);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Cancelled && e.RecipientEmployeeIds.Single() == A), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AlreadyDecided_Returns409()
    {
        Caller(A);
        _request.Status = WorkApprovalRequestStatuses.Approved;
        (await Build().Handle(Cmd(_request.Id, WorkApprovalDecision.Reject), default)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UnknownRequest_Returns404()
    {
        Caller(A);
        (await Build().Handle(Cmd(Guid.NewGuid(), WorkApprovalDecision.Approve), default)).StatusCode.Should().Be(404);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApprovalActionApplierRegistryTests|FullyQualifiedName~DecideWorkApprovalRequestCommandHandlerTests"`
Expected: build FAIL, types not found.

- [ ] **Step 4: Implement the applier contract and registry**

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/IApprovalActionApplier.cs`:

```csharp
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed record ApprovalApplyContext(WorkApprovalRequest Request, string PayloadJson, Guid DeciderEmployeeId);

public enum ApplyOutcomeKind { Applied, Stale, Invalid }

public sealed record ApplyOutcome(ApplyOutcomeKind Kind, string? Error = null)
{
    public static ApplyOutcome Applied { get; } = new(ApplyOutcomeKind.Applied);
    /// <summary>Target deleted, or changed since Request.TargetUpdatedAtSnapshot - nothing applied.</summary>
    public static ApplyOutcome Stale { get; } = new(ApplyOutcomeKind.Stale);
    /// <summary>The (possibly approver-edited) payload fails validation - nothing applied, the request stays pending.</summary>
    public static ApplyOutcome Invalid(string error) => new(ApplyOutcomeKind.Invalid, error);
}

/// <summary>
/// Applies one approved Work Management action type. One implementation per WorkActionTypes value,
/// registered in DI as IApprovalActionApplier. Runs inside the decide command's transaction and must
/// not call SaveChangesAsync.
/// </summary>
public interface IApprovalActionApplier
{
    string ActionType { get; }
    Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct);
}

public interface IApprovalActionApplierRegistry
{
    IApprovalActionApplier? Find(string actionType);
}
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalActionApplierRegistry.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class ApprovalActionApplierRegistry : IApprovalActionApplierRegistry
{
    private readonly IReadOnlyDictionary<string, IApprovalActionApplier> _byActionType;

    public ApprovalActionApplierRegistry(IEnumerable<IApprovalActionApplier> appliers)
    {
        var map = new Dictionary<string, IApprovalActionApplier>();
        foreach (var applier in appliers)
        {
            if (!map.TryAdd(applier.ActionType, applier))
                throw new InvalidOperationException($"Two approval appliers are registered for '{applier.ActionType}'.");
        }
        _byActionType = map;
    }

    public IApprovalActionApplier? Find(string actionType) => _byActionType.GetValueOrDefault(actionType);
}
```

- [ ] **Step 5: Implement the DTO, mapper, command and handler**

`src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/WorkApprovalRequestResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public sealed record WorkApprovalRequestResponse(
    Guid Id,
    Guid ProjectId,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    string Status,
    Guid RequestedByEmployeeId,
    string RequestedByName,
    Guid ApproverEmployeeId,
    string ApproverName,
    string PayloadJson,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Mappers/WorkApprovalRequestMapper.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Mappers;

public static class WorkApprovalRequestMapper
{
    public static WorkApprovalRequestResponse ToResponse(WorkApprovalRequest r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.ProjectId, r.ActionType, r.TargetType, r.TargetId, r.TargetTitle, r.Status,
        r.RequestedByEmployeeId, names.GetValueOrDefault(r.RequestedByEmployeeId) ?? "Unknown",
        r.ApproverEmployeeId, names.GetValueOrDefault(r.ApproverEmployeeId) ?? "Unknown",
        r.PayloadJson, r.DecisionComment, r.CreatedAt, r.DecidedAt);
}
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/DecideWorkApprovalRequest/DecideWorkApprovalRequestCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;

public enum WorkApprovalDecision { Approve, Reject, Cancel }

/// <summary>EditedPayloadJson is only honoured for Approve (the approver may adjust the change before applying it).</summary>
public sealed record DecideWorkApprovalRequestCommand(
    Guid RequestId, WorkApprovalDecision Decision, string? EditedPayloadJson, string? Comment)
    : IRequest<Result<WorkApprovalRequestResponse>>;
```

`src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/DecideWorkApprovalRequest/DecideWorkApprovalRequestCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;

public sealed class DecideWorkApprovalRequestCommandHandler
    : IRequestHandler<DecideWorkApprovalRequestCommand, Result<WorkApprovalRequestResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IApprovalActionApplierRegistry _appliers;
    private readonly IWorkNotificationEngine _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public DecideWorkApprovalRequestCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkApprovalRequestRepository requests,
        IWorkHierarchyService hierarchy, IApprovalActionApplierRegistry appliers,
        IWorkNotificationEngine notifications, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
        _appliers = appliers;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkApprovalRequestResponse>> Handle(DecideWorkApprovalRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkApprovalRequestResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<WorkApprovalRequestResponse>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        var request = await _requests.GetTrackedByIdForTenantAsync(tenantId, command.RequestId, ct);
        if (request is null)
            return Result<WorkApprovalRequestResponse>.NotFound("Approval request not found.");
        if (request.Status != WorkApprovalRequestStatuses.Pending)
            return Result<WorkApprovalRequestResponse>.Conflict("This request has already been decided.");

        if (command.Decision == WorkApprovalDecision.Cancel)
        {
            if (request.RequestedByEmployeeId != caller)
                return Result<WorkApprovalRequestResponse>.Forbidden("Only the person who made this request can cancel it.");
        }
        else
        {
            var tree = await _hierarchy.LoadTreeAsync(tenantId, request.ProjectId, ct);
            if (!WorkApprovalDecisionRules.CanDecide(tree, request, caller))
                return Result<WorkApprovalRequestResponse>.Forbidden("You are not the approver for this request.");
        }

        IApprovalActionApplier? applier = null;
        if (command.Decision == WorkApprovalDecision.Approve)
        {
            applier = _appliers.Find(request.ActionType);
            if (applier is null)
                return Result<WorkApprovalRequestResponse>.UnprocessableEntity("This request type can no longer be approved.");
        }

        var payload = command.Decision == WorkApprovalDecision.Approve && !string.IsNullOrWhiteSpace(command.EditedPayloadJson)
            ? command.EditedPayloadJson!
            : request.PayloadJson;

        var result = await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            string status, kind;
            switch (command.Decision)
            {
                case WorkApprovalDecision.Approve:
                    var outcome = await applier!.ApplyAsync(new ApprovalApplyContext(request, payload, caller), innerCt);
                    if (outcome.Kind == ApplyOutcomeKind.Invalid)
                        return Result<WorkApprovalRequestResponse>.UnprocessableEntity(outcome.Error ?? "The requested change is not valid.");
                    (status, kind) = outcome.Kind == ApplyOutcomeKind.Applied
                        ? (WorkApprovalRequestStatuses.Approved, WorkNotificationKinds.Approved)
                        : (WorkApprovalRequestStatuses.Stale, WorkNotificationKinds.Stale);
                    if (outcome.Kind == ApplyOutcomeKind.Applied)
                        request.PayloadJson = payload;
                    break;
                case WorkApprovalDecision.Reject:
                    (status, kind) = (WorkApprovalRequestStatuses.Rejected, WorkNotificationKinds.Rejected);
                    break;
                default:
                    (status, kind) = (WorkApprovalRequestStatuses.Cancelled, WorkNotificationKinds.Cancelled);
                    break;
            }

            request.Status = status;
            request.DecidedByEmployeeId = caller;
            request.DecisionComment = command.Comment?.Trim();
            request.DecidedAt = DateTimeOffset.UtcNow;
            _requests.Update(request);

            var notifyWho = command.Decision == WorkApprovalDecision.Cancel ? request.ApproverEmployeeId : request.RequestedByEmployeeId;
            await _notifications.NotifyAsync(new WorkNotificationEvent(
                tenantId, request.ProjectId, caller, kind, request.ActionType, request.TargetType,
                request.TargetId, request.TargetTitle, request.Id, [notifyWho]), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<WorkApprovalRequestResponse>.Success(null!);
        }, ct);

        if (!result.IsSuccess)
            return result;

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, [request.RequestedByEmployeeId, request.ApproverEmployeeId], ct);
        return Result<WorkApprovalRequestResponse>.Success(WorkApprovalRequestMapper.ToResponse(request, names));
    }
}
```

DI, WM block:

```csharp
        services.AddScoped<IApprovalActionApplierRegistry, ApprovalActionApplierRegistry>();
```

MediatR discovers the handler automatically, as it does for every existing WM handler.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApprovalActionApplierRegistryTests|FullyQualifiedName~DecideWorkApprovalRequestCommandHandlerTests"`
Expected: 12 passed.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Approvals src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals
git commit -m "feat(work-management): add unified approve/reject/cancel with action applier registry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: List queries and `WorkApprovalsController`

**Files:**
- Create:
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/ListProjectWorkApprovals/ListProjectWorkApprovalsQuery.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/ListProjectWorkApprovals/ListProjectWorkApprovalsQueryHandler.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/DTOs/WorkNotificationLogResponse.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Queries/ListProjectWorkNotifications/ListProjectWorkNotificationsQuery.cs`
  - `src/ONEVO.Application/Features/WorkManagement/Notifications/Queries/ListProjectWorkNotifications/ListProjectWorkNotificationsQueryHandler.cs`
  - `src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs`
- Test:
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ListProjectWorkApprovalsQueryHandlerTests.cs`
  - `tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications/ListProjectWorkNotificationsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces the HTTP API that Plans 2–4 (frontend) consume:
  - `GET api/v1/work/projects/{projectId}/approvals?scope=inbox|mine&status=`, which returns `WorkApprovalRequestResponse[]`
  - `POST api/v1/work/approvals/{id}/approve` with body `{ editedPayloadJson?, comment? }`
  - `POST api/v1/work/approvals/{id}/reject` with body `{ comment? }`
  - `POST api/v1/work/approvals/{id}/cancel`
  - `GET api/v1/work/projects/{projectId}/work-notifications?page=1`, which returns `WorkNotificationLogResponse[]`

- [ ] **Step 1: Write the failing query tests**

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/ListProjectWorkApprovalsQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ListProjectWorkApprovalsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();

    public ListProjectWorkApprovalsQueryHandlerTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Requester] = "Bala" });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = A },
                new Objective { Id = _other, ParentObjectiveId = _root, OwnerId = Guid.NewGuid() },
            }));
    }

    private WorkApprovalRequest Pending(Guid position) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = position,
        ApproverSource = WorkApprovalSources.Hierarchy, RequestedByEmployeeId = Requester,
        Status = WorkApprovalRequestStatuses.Pending, ActionType = WorkActionTypes.TaskEdit,
        TargetType = WorkTargetTypes.Task, TargetTitle = "t"
    };

    private ListProjectWorkApprovalsQueryHandler Build() => new(_currentUser.Object, _identity.Object, _requests.Object, _hierarchy.Object);

    [Fact]
    public async Task Inbox_ReturnsOnlyPendingTheCallerCanDecide()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(A);
        var mine = Pending(_p);
        var notMine = Pending(_other);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, null, WorkApprovalRequestStatuses.Pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { mine, notMine });

        var result = await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "inbox", null), default);

        result.Value!.Select(r => r.Id).Should().Equal(mine.Id);
        result.Value![0].RequestedByName.Should().Be("Bala");
    }

    [Fact]
    public async Task Mine_ReturnsCallerRequests_WithStatusFilter()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Requester);
        _requests.Setup(x => x.ListByProjectAsync(TenantId, ProjectId, Requester, "approved", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkApprovalRequest> { Pending(_p) });

        var result = await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "mine", "approved"), default);

        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task UnknownScope_Returns400()
    {
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(A);
        (await Build().Handle(new ListProjectWorkApprovalsQuery(ProjectId, "everything", null), default)).StatusCode.Should().Be(400);
    }
}
```

`tests/ONEVO.Tests.Unit/Features/WorkManagement/Notifications/ListProjectWorkNotificationsQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Notifications;

public class ListProjectWorkNotificationsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task ReturnsCallerRowsForProject_PagedBy50_WithActorNameAndLabel()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(Me);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Actor] = "Bala" });
        var logs = new Mock<IWorkNotificationLogRepository>();
        logs.Setup(x => x.ListForRecipientAsync(TenantId, ProjectId, Me, 50, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkNotificationLog>
            {
                new() { Id = Guid.NewGuid(), ActorEmployeeId = Actor, Kind = WorkNotificationKinds.Direct,
                        ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task, TargetTitle = "Audit" }
            });

        var handler = new ListProjectWorkNotificationsQueryHandler(currentUser.Object, identity.Object, logs.Object);
        var result = await handler.Handle(new ListProjectWorkNotificationsQuery(ProjectId, 2), default);

        result.Value.Should().ContainSingle();
        result.Value![0].ActorName.Should().Be("Bala");
        result.Value[0].ActionLabel.Should().Be("edited the task");
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ListProjectWorkApprovalsQueryHandlerTests|FullyQualifiedName~ListProjectWorkNotificationsQueryHandlerTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement the queries**

`ListProjectWorkApprovalsQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;

/// <summary>Scope "inbox" = pending requests the caller can decide right now; "mine" = requests the caller made.</summary>
public sealed record ListProjectWorkApprovalsQuery(Guid ProjectId, string Scope, string? Status)
    : IRequest<Result<IReadOnlyList<WorkApprovalRequestResponse>>>;
```

`ListProjectWorkApprovalsQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;

public sealed class ListProjectWorkApprovalsQueryHandler
    : IRequestHandler<ListProjectWorkApprovalsQuery, Result<IReadOnlyList<WorkApprovalRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalRequestRepository _requests;
    private readonly IWorkHierarchyService _hierarchy;

    public ListProjectWorkApprovalsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity,
        IWorkApprovalRequestRepository requests, IWorkHierarchyService hierarchy)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
        _hierarchy = hierarchy;
    }

    public async Task<Result<IReadOnlyList<WorkApprovalRequestResponse>>> Handle(ListProjectWorkApprovalsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Forbidden("No employee record for the current user.");
        var caller = callerEmployeeId.Value;

        IReadOnlyList<WorkApprovalRequest> rows;
        switch (query.Scope)
        {
            case "inbox":
                var pending = await _requests.ListByProjectAsync(tenantId, query.ProjectId, null, WorkApprovalRequestStatuses.Pending, ct);
                var tree = await _hierarchy.LoadTreeAsync(tenantId, query.ProjectId, ct);
                rows = pending.Where(r => WorkApprovalDecisionRules.CanDecide(tree, r, caller)).ToList();
                break;
            case "mine":
                rows = await _requests.ListByProjectAsync(tenantId, query.ProjectId, caller, query.Status, ct);
                break;
            default:
                return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Failure("Scope must be 'inbox' or 'mine'.");
        }

        var ids = rows.SelectMany(r => new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId }).Distinct().ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, ids, ct);
        return Result<IReadOnlyList<WorkApprovalRequestResponse>>.Success(
            rows.Select(r => WorkApprovalRequestMapper.ToResponse(r, names)).ToList());
    }
}
```

`src/ONEVO.Application/Features/WorkManagement/Notifications/DTOs/WorkNotificationLogResponse.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Notifications.DTOs;

public sealed record WorkNotificationLogResponse(
    Guid Id,
    string Kind,
    string ActionType,
    string ActionLabel,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid ActorEmployeeId,
    string ActorName,
    Guid? ApprovalRequestId,
    DateTimeOffset CreatedAt);
```

`ListProjectWorkNotificationsQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Notifications.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

public sealed record ListProjectWorkNotificationsQuery(Guid ProjectId, int Page = 1)
    : IRequest<Result<IReadOnlyList<WorkNotificationLogResponse>>>;
```

`ListProjectWorkNotificationsQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.DTOs;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

public sealed class ListProjectWorkNotificationsQueryHandler
    : IRequestHandler<ListProjectWorkNotificationsQuery, Result<IReadOnlyList<WorkNotificationLogResponse>>>
{
    public const int PageSize = 50;

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkNotificationLogRepository _logs;

    public ListProjectWorkNotificationsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkNotificationLogRepository logs)
    {
        _currentUser = currentUser;
        _identity = identity;
        _logs = logs;
    }

    public async Task<Result<IReadOnlyList<WorkNotificationLogResponse>>> Handle(ListProjectWorkNotificationsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkNotificationLogResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkNotificationLogResponse>>.Forbidden("No employee record for the current user.");

        var page = Math.Max(1, query.Page);
        var rows = await _logs.ListForRecipientAsync(tenantId, query.ProjectId, callerEmployeeId.Value, (page - 1) * PageSize, PageSize, ct);
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, rows.Select(r => r.ActorEmployeeId).Distinct().ToList(), ct);

        return Result<IReadOnlyList<WorkNotificationLogResponse>>.Success(rows.Select(r => new WorkNotificationLogResponse(
            r.Id, r.Kind, r.ActionType, WorkActionLabels.For(r.ActionType), r.TargetType, r.TargetId, r.TargetTitle,
            r.ActorEmployeeId, names.GetValueOrDefault(r.ActorEmployeeId) ?? "A teammate", r.ApprovalRequestId, r.CreatedAt)).ToList());
    }
}
```

- [ ] **Step 4: Run the query tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ListProjectWorkApprovalsQueryHandlerTests|FullyQualifiedName~ListProjectWorkNotificationsQueryHandlerTests"`
Expected: 4 passed.

- [ ] **Step 5: Add the controller**

`src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs`. Copy the `using` lines for `Authorize`, `RequireAnyModule` and `MediatR` exactly from `TaskStatusChangeRequestsController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.Queries.ListProjectWorkApprovals;
using ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

public sealed record ApproveWorkApprovalRequestRequest(string? EditedPayloadJson, string? Comment);
public sealed record RejectWorkApprovalRequestRequest(string? Comment);

/// <summary>The unified Work Management approval inbox and project notification history, backed by
/// the approval and notification engines.</summary>
[ApiController]
[Route("api/v1/work")]
[Authorize(Policy = "TenantPolicy")]
[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
public class WorkApprovalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkApprovalsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("projects/{projectId:guid}/approvals")]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] string scope = "inbox", [FromQuery] string? status = null, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListProjectWorkApprovalsQuery(projectId, scope, status), ct));

    [HttpPost("approvals/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveWorkApprovalRequestRequest? body, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Approve, body?.EditedPayloadJson, body?.Comment), ct));

    [HttpPost("approvals/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectWorkApprovalRequestRequest? body, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Reject, null, body?.Comment), ct));

    [HttpPost("approvals/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => ToResult(await _mediator.Send(new DecideWorkApprovalRequestCommand(id, WorkApprovalDecision.Cancel, null, null), ct));

    [HttpGet("projects/{projectId:guid}/work-notifications")]
    public async Task<IActionResult> Notifications(Guid projectId, [FromQuery] int page = 1, CancellationToken ct = default)
        => ToResult(await _mediator.Send(new ListProjectWorkNotificationsQuery(projectId, page), ct));

    private IActionResult ToResult<T>(ONEVO.Application.Common.Models.Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

- [ ] **Step 6: Build and run the full gate**

```bash
dotnet build
dotnet test tests/ONEVO.Tests.Unit
dotnet test tests/ONEVO.Tests.Architecture
```

Expected: build succeeds, and both suites are green.
- If an architecture test flags the controller (for example, a required attribute such as `[Idempotent]` on POSTs), follow what the test message asks for, using the sibling `TaskStatusChangeRequestsController` as the model.
- Record the pass counts in the commit message.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.API/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement
git commit -m "feat(work-management): expose unified approvals inbox, decisions and notification history endpoints

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## What Plans 2–4 will cover (written after this plan lands, against the real code)

- **Plan 2 (spec Part 4):**
  - Task create/edit/delete/status change through `IWorkApprovalEngine`.
  - Stamp `CreatorPositionObjectiveId` in `CreateTask`, `CreateSubtask`, `DuplicateTask` and the task-creation applier.
  - `IApprovalActionApplier`s for the task actions, moving logic out of `ApproveTask*Request` handlers.
  - Migrate the pending rows of the 3 task request tables and drop them; delete their handlers, endpoints and tests.
  - Frontend: move to the generic approvals API.
- **Plan 3 (spec Parts 5–6):**
  - Module actions and appliers, stamping in `CreateObjective`, and dropping `objective_change_requests`.
  - Sprint create/edit and a new delete command, stamping in `CreateSprint`, and retiring `SprintAccessService.CanManageAsync`.
- **Plan 4 (spec Part 7):**
  - Replace the duplicated ancestor walks with `ProjectModuleTree`.
  - Remove dead WM code and useless tests (each listed in the commit).
  - Frontend Approvals page Requests + History tabs, plus spec cleanup.
