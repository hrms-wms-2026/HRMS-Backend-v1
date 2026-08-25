# Part 1: Domain entities, EF configuration, migration, repositories

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the four new tables (`TaskEditLog`, `TaskStatusChangeLog`, `TaskClockingSession`,
`TaskPercentageLog`) with entities, EF configurations, one migration (with RLS policies), DbContext
registration, and one repository per entity — nothing consumes them yet, that's Parts 2–8.

**Spec:** `docs/superpowers/specs/next/2026-08-25-work-management-task-time-tracking-and-my-task-design.md`
(§3 Data model)

## Architecture & Conventions — read this before writing any code

- **This is a WorkManagement (WM) module addition.** Every file you create must live in the same
  `Features/WorkManagement/Tasks/...` namespace tree as the sibling entity it's structurally closest to.
  Compare every new file against its named sibling below — do not invent a different shape.
- **Direct structural sibling for all 4 entities: `TaskEditRequest`**
  (`src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditRequest.cs`) — same `BaseEntity`
  inheritance, same "one repository interface per entity, `AddAsync`/`GetByIdForTenantAsync`/
  `GetTrackedByIdForTenantAsync`/`Update`" repository shape
  (`ITaskEditRequestRepository`/`EfTaskEditRequestRepository`), same EF configuration shape
  (`TaskEditRequestConfiguration.cs`).
- **`BaseEntity`** (`src/ONEVO.Domain/Common/BaseEntity.cs`) already gives every entity `Id`, `TenantId`,
  `CreatedAt`, `UpdatedAt`, `CreatedById`, `IsDeleted`, `DeletedAt` — do not redeclare these fields on the
  new entities.
- **Table naming:** snake_case, matching `task_edit_requests`/`task_categories`. This plan uses
  `task_edit_logs`, `task_status_change_logs`, `task_clocking_sessions`, `task_percentage_logs`.
- **RLS is mandatory for every new tenant table — this is the single most commonly missed step in this
  codebase's migrations.** The migration in Task 5 below must enable and force row-level security and
  create the `tenant_isolation` policy for all four new tables, exactly like
  `20260823172054_AddTaskCategories.cs` does for `task_categories` (see that file's `TenantTables` array
  and the `Up`/`Down` RLS SQL blocks — copy that pattern for all 4 table names, do not skip any of them).
- **Do not apply this migration.** Write it, dry-run validate with `BEGIN...ROLLBACK` (Task 6 below), commit
  the code, then stop and tell the user to run
  `.\ops\postgres\setup-local-db.ps1 -RunMigrations` themselves. This project's permission classifier blocks
  `dotnet ef database update` for a real reason — never run it, never suggest running it as your own next
  step.
- **JSON columns** use `HasColumnType("jsonb")`, matching `TaskEditRequestConfiguration.PayloadJson`.
- **Do not register anything in `TasksController` or any command/query handler in this Part** — Parts 2–8
  consume these repositories; this Part only creates the tables and the (currently unused) repository
  plumbing. `dotnet build` must succeed with zero new warnings after this Part even though nothing calls
  the new repositories yet.

## Global Constraints

- Every new table gets RLS (`ALTER TABLE ... ENABLE/FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy).
- Never run `dotnet ef database update` or any DB-mutating script — write and dry-run validate only.
- Follow `TaskEditRequest`/`TaskEditRequestConfiguration`/`EfTaskEditRequestRepository` as the structural
  template for all 4 entities.

---

### Task 1: `TaskEditLog` entity + configuration + repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditLog.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditLogConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskEditLogRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskEditLogRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskEditLogRepositoryTests.cs`

**Interfaces:**
- Produces: `TaskEditLog` entity with `TaskId (Guid)`, `EmployeeId (Guid)`, `Source (string)`,
  `EditRequestId (Guid?)`, `OldValuesJson (string)`, `NewValuesJson (string)`, `Reason (string?)`,
  `ChangedAt (DateTimeOffset)`. `ITaskEditLogRepository.AddAsync(TaskEditLog, ct)`,
  `GetForTaskAsync(Guid tenantId, Guid taskId, ct) -> IReadOnlyList<TaskEditLog>` (ordered by `ChangedAt`
  ascending — the history feed in Part 7 sorts the merged result itself, but returning pre-sorted per-table
  data keeps that merge simple).

- [ ] **Step 1: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskEditLogSources
{
    public const string Direct = "direct";
    public const string ApprovedRequest = "approved_request";
}

/// <summary>Audit row for every applied change to a WorkTask's editable fields — written by
/// EditTaskCommandHandler (Source = Direct) and ApproveTaskEditRequestCommandHandler
/// (Source = ApprovedRequest, EditRequestId set). Visible to every project member on the task detail
/// page, not owner-gated - see design spec §6.</summary>
public class TaskEditLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Source { get; set; } = TaskEditLogSources.Direct;
    public Guid? EditRequestId { get; set; }
    public string OldValuesJson { get; set; } = "{}";
    public string NewValuesJson { get; set; } = "{}";
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskEditLogConfiguration : IEntityTypeConfiguration<TaskEditLog>
{
    public void Configure(EntityTypeBuilder<TaskEditLog> builder)
    {
        builder.ToTable("task_edit_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Source).HasMaxLength(20).IsRequired();
        builder.Property(l => l.OldValuesJson).HasColumnType("jsonb");
        builder.Property(l => l.NewValuesJson).HasColumnType("jsonb");
        builder.Property(l => l.Reason).HasColumnType("text");

        builder.HasIndex(l => new { l.TenantId, l.TaskId, l.ChangedAt })
            .HasDatabaseName("ix_task_edit_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskEditRequest>().WithMany().HasForeignKey(l => l.EditRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Write the repository interface**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskEditLogRepository
{
    Task AddAsync(TaskEditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskEditLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the EF repository**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskEditLogRepository : ITaskEditLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskEditLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskEditLog log, CancellationToken ct = default)
        => await _db.TaskEditLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskEditLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskEditLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TaskId == taskId)
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Write the repository test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EfTaskEditLogRepositoryTests : WorkManagementRepositoryTestBase
{
    [Fact]
    public async Task GetForTaskAsync_ReturnsOnlyThisTenantsLogsForThisTask_OrderedByChangedAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await using var db = CreateContext();
        var repo = new EfTaskEditLogRepository(db);

        var older = new TaskEditLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new TaskEditLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow };
        var otherTenant = new TaskEditLog { Id = Guid.NewGuid(), TenantId = otherTenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(older);
        await repo.AddAsync(newer);
        await repo.AddAsync(otherTenant);
        await db.SaveChangesAsync();

        var result = await repo.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(older.Id, result[0].Id);
        Assert.Equal(newer.Id, result[1].Id);
    }
}
```

**Note:** `WorkManagementRepositoryTestBase` is this plan's assumed shared in-memory/SQLite test-context
helper — check `tests/ONEVO.Tests.Unit/Features/WorkManagement/` for whichever base class
`EfSprintRepositoryTests` or `EfTaskEditRequestRepositoryTests` (if one exists) actually uses and match
that exact name instead of inventing `WorkManagementRepositoryTestBase` if it doesn't already exist —
this codebase's repository tests all share one context-creation helper, find it before writing a new one.

- [ ] **Step 6: Run the test to verify it fails (compile error — `TaskEditLogs` DbSet doesn't exist yet)**

Run: `dotnet test --filter EfTaskEditLogRepositoryTests`
Expected: build error, `DbContext` has no `TaskEditLogs` member (fixed in Task 5).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskEditLog.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskEditLogConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskEditLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskEditLogRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskEditLogRepositoryTests.cs
git commit -m "feat(work): add TaskEditLog entity, configuration, and repository"
```

---

### Task 2: `TaskStatusChangeLog` entity + configuration + repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatusChangeLog.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusChangeLogConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusChangeLogRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusChangeLogRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskStatusChangeLogRepositoryTests.cs`

**Interfaces:**
- Produces: `TaskStatusChangeLog` with `TaskId`, `EmployeeId`, `FromStatusId (Guid)`, `ToStatusId (Guid)`,
  `ChangedAt`. `ITaskStatusChangeLogRepository.AddAsync(...)`, `GetForTaskAsync(tenantId, taskId, ct)`.

- [ ] **Step 1: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>Audit row for every Task status move — written by MoveTaskStatusCommandHandler. Every
/// status move writes one of these unconditionally (unlike TaskPercentageLog's status-driven rows,
/// which only appear when MarksTaskComplete actually flips). See design spec §5.</summary>
public class TaskStatusChangeLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid FromStatusId { get; set; }
    public Guid ToStatusId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskStatusChangeLogConfiguration : IEntityTypeConfiguration<TaskStatusChangeLog>
{
    public void Configure(EntityTypeBuilder<TaskStatusChangeLog> builder)
    {
        builder.ToTable("task_status_change_logs");
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.TenantId, l.TaskId, l.ChangedAt })
            .HasDatabaseName("ix_task_status_change_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskStatusEntity>().WithMany().HasForeignKey(l => l.FromStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskStatusEntity>().WithMany().HasForeignKey(l => l.ToStatusId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

**Check before writing this:** confirm the exact C# type name for the Task Status entity — this codebase
names it `TaskStatusEntity` (see `WorkTaskResponse`/`TasksController` usage of `TaskStatusViewModel` and
the `TaskStatuses` DbSet type in `ApplicationDbContext.cs:282`) to avoid colliding with
`System.Threading.Tasks.TaskStatus`, mirroring why `WorkTask` isn't just called `Task`. Grep
`ApplicationDbContext.cs` for `TaskStatuses =>` to confirm the exact type before using it here.

- [ ] **Step 3: Write the repository interface**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskStatusChangeLogRepository
{
    Task AddAsync(TaskStatusChangeLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskStatusChangeLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the EF repository**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskStatusChangeLogRepository : ITaskStatusChangeLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskStatusChangeLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskStatusChangeLog log, CancellationToken ct = default)
        => await _db.TaskStatusChangeLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskStatusChangeLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskStatusChangeLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TaskId == taskId)
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Write the repository test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EfTaskStatusChangeLogRepositoryTests : WorkManagementRepositoryTestBase
{
    [Fact]
    public async Task GetForTaskAsync_ReturnsOnlyThisTenantsLogsForThisTask_OrderedByChangedAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = CreateContext();
        var repo = new EfTaskStatusChangeLogRepository(db);

        var older = new TaskStatusChangeLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), FromStatusId = Guid.NewGuid(), ToStatusId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var newer = new TaskStatusChangeLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), FromStatusId = older.ToStatusId, ToStatusId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(older);
        await repo.AddAsync(newer);
        await db.SaveChangesAsync();

        var result = await repo.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(older.Id, result[0].Id);
        Assert.Equal(newer.Id, result[1].Id);
    }
}
```

- [ ] **Step 6: Run the test to verify it fails, then Step 7: commit**

Run: `dotnet test --filter EfTaskStatusChangeLogRepositoryTests` — expect a build error (`TaskStatusChangeLogs`
DbSet doesn't exist, fixed in Task 5 of this Part).

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskStatusChangeLog.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskStatusChangeLogConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskStatusChangeLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskStatusChangeLogRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskStatusChangeLogRepositoryTests.cs
git commit -m "feat(work): add TaskStatusChangeLog entity, configuration, and repository"
```

---

### Task 3: `TaskClockingSession` entity + configuration + repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskClockingSession.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskClockingSessionConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskClockingSessionRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskClockingSessionRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskClockingSessionRepositoryTests.cs`

**Interfaces:**
- Produces: `TaskClockingSession` with `TaskId`, `EmployeeId`, `ClockInAt (DateTimeOffset)`,
  `ClockOutAt (DateTimeOffset?)`, `DurationMinutes (int?)`, `Reason (string?)`.
  `ITaskClockingSessionRepository.AddAsync(...)`,
  `GetOpenSessionForTaskAsync(tenantId, taskId, ct) -> TaskClockingSession?` (the per-task lock check
  Part 6 uses before allowing a new Clock In),
  `GetTrackedByIdForTenantAsync(tenantId, id, ct) -> TaskClockingSession?` (Push needs a tracked entity to
  close), `GetForTaskAsync(tenantId, taskId, ct) -> IReadOnlyList<TaskClockingSession>`, `Update(...)`.

- [ ] **Step 1: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>One Clock-in-to-Push work session on a Task. At most one session per Task may be open
/// (ClockOutAt == null) at a time - enforced by a partial unique index in the migration, not just here.
/// One employee may have several different Tasks' sessions open simultaneously; the lock is per-Task,
/// not per-employee. See design spec §3-4.</summary>
public class TaskClockingSession : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ClockInAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClockOutAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Reason { get; set; }
}
```

- [ ] **Step 2: Write the EF configuration (including the partial unique index)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskClockingSessionConfiguration : IEntityTypeConfiguration<TaskClockingSession>
{
    public void Configure(EntityTypeBuilder<TaskClockingSession> builder)
    {
        builder.ToTable("task_clocking_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Reason).HasColumnType("text");

        builder.HasIndex(s => new { s.TenantId, s.TaskId })
            .HasDatabaseName("ix_task_clocking_sessions_one_open_per_task")
            .IsUnique()
            .HasFilter("clock_out_at IS NULL");

        builder.HasIndex(s => new { s.TenantId, s.TaskId, s.ClockInAt })
            .HasDatabaseName("ix_task_clocking_sessions_tenant_id_task_id_clock_in_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

**Note the `HasFilter` string uses the snake_case column name** (`clock_out_at`), not the C# property name
(`ClockOutAt`) — EF's `HasFilter` takes a raw SQL fragment, it does not translate property names. Confirm
the actual generated column name in the migration (Task 5) matches this filter exactly, or the partial
index silently won't do what you think.

- [ ] **Step 3: Write the repository interface**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskClockingSessionRepository
{
    Task AddAsync(TaskClockingSession session, CancellationToken ct = default);
    Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    void Update(TaskClockingSession session);
}
```

- [ ] **Step 4: Write the EF repository**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskClockingSessionRepository : ITaskClockingSessionRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskClockingSessionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskClockingSession session, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AddAsync(session, ct);

    public async Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.TaskId == taskId && s.ClockOutAt == null, ct);

    public async Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskClockingSessions.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskClockingSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TaskId == taskId)
            .OrderBy(s => s.ClockInAt)
            .ToListAsync(ct);

    public void Update(TaskClockingSession session) => _db.TaskClockingSessions.Update(session);
}
```

- [ ] **Step 5: Write the repository test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EfTaskClockingSessionRepositoryTests : WorkManagementRepositoryTestBase
{
    [Fact]
    public async Task GetOpenSessionForTaskAsync_ReturnsOnlyTheOpenSession_NotClosedOnes()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = CreateContext();
        var repo = new EfTaskClockingSessionRepository(db);

        var closed = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-2), ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1), DurationMinutes = 60 };
        var open = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(closed);
        await repo.AddAsync(open);
        await db.SaveChangesAsync();

        var result = await repo.GetOpenSessionForTaskAsync(tenantId, taskId);

        Assert.NotNull(result);
        Assert.Equal(open.Id, result!.Id);
    }

    [Fact]
    public async Task GetForTaskAsync_ReturnsAllSessionsOrderedByClockInAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = CreateContext();
        var repo = new EfTaskClockingSessionRepository(db);

        var first = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow.AddHours(-2), ClockOutAt = DateTimeOffset.UtcNow.AddHours(-1), DurationMinutes = 60 };
        var second = new TaskClockingSession { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), ClockInAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(second);
        await repo.AddAsync(first);
        await db.SaveChangesAsync();

        var result = await repo.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(first.Id, result[0].Id);
        Assert.Equal(second.Id, result[1].Id);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail, then Step 7: commit**

Run: `dotnet test --filter EfTaskClockingSessionRepositoryTests` — expect a build error
(`TaskClockingSessions` DbSet doesn't exist, fixed in Task 5 of this Part).

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskClockingSession.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskClockingSessionConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskClockingSessionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskClockingSessionRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskClockingSessionRepositoryTests.cs
git commit -m "feat(work): add TaskClockingSession entity, configuration, and repository"
```

---

### Task 4: `TaskPercentageLog` entity + configuration + repository

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskPercentageLog.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskPercentageLogConfiguration.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskPercentageLogRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskPercentageLogRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskPercentageLogRepositoryTests.cs`

**Interfaces:**
- Produces: `TaskPercentageLog` with `TaskId`, `EmployeeId`, `PreviousPercent (int)`, `NewPercent (int)`,
  `Source (string)`, `ClockingSessionId (Guid?)`, `Reason (string?)`, `ChangedAt`.
  `ITaskPercentageLogRepository.AddAsync(...)`, `GetForTaskAsync(tenantId, taskId, ct)`.

- [ ] **Step 1: Write the entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskPercentageLogSources
{
    public const string Push = "push";
    public const string ManualEdit = "manual_edit";
    public const string StatusChange = "status_change";
}

/// <summary>Audit row for every change to WorkTask.ProgressPercent, from any of its three sources:
/// a Push (ClockingSessionId set), a manual Task Edit (direct or approved-request), or
/// MoveTaskStatusCommandHandler's existing MarksTaskComplete side effect. See design spec §4-5.</summary>
public class TaskPercentageLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public int PreviousPercent { get; set; }
    public int NewPercent { get; set; }
    public string Source { get; set; } = TaskPercentageLogSources.ManualEdit;
    public Guid? ClockingSessionId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskPercentageLogConfiguration : IEntityTypeConfiguration<TaskPercentageLog>
{
    public void Configure(EntityTypeBuilder<TaskPercentageLog> builder)
    {
        builder.ToTable("task_percentage_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Source).HasMaxLength(20).IsRequired();
        builder.Property(l => l.Reason).HasColumnType("text");

        builder.HasIndex(l => new { l.TenantId, l.TaskId, l.ChangedAt })
            .HasDatabaseName("ix_task_percentage_logs_tenant_id_task_id_changed_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskClockingSession>().WithMany().HasForeignKey(l => l.ClockingSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Write the repository interface**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskPercentageLogRepository
{
    Task AddAsync(TaskPercentageLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskPercentageLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the EF repository**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskPercentageLogRepository : ITaskPercentageLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskPercentageLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskPercentageLog log, CancellationToken ct = default)
        => await _db.TaskPercentageLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskPercentageLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskPercentageLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TaskId == taskId)
            .OrderBy(l => l.ChangedAt)
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Write the repository test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EfTaskPercentageLogRepositoryTests : WorkManagementRepositoryTestBase
{
    [Fact]
    public async Task GetForTaskAsync_ReturnsLogsAcrossAllSources_OrderedByChangedAt()
    {
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = CreateContext();
        var repo = new EfTaskPercentageLogRepository(db);

        var pushEntry = new TaskPercentageLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), PreviousPercent = 0, NewPercent = 40, Source = TaskPercentageLogSources.Push, ClockingSessionId = Guid.NewGuid(), ChangedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var manualEntry = new TaskPercentageLog { Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = Guid.NewGuid(), PreviousPercent = 40, NewPercent = 20, Source = TaskPercentageLogSources.ManualEdit, ClockingSessionId = null, ChangedAt = DateTimeOffset.UtcNow };
        await repo.AddAsync(pushEntry);
        await repo.AddAsync(manualEntry);
        await db.SaveChangesAsync();

        var result = await repo.GetForTaskAsync(tenantId, taskId);

        Assert.Equal(2, result.Count);
        Assert.Equal(pushEntry.Id, result[0].Id);
        Assert.Equal(manualEntry.Id, result[1].Id);
        Assert.Null(result[1].ClockingSessionId);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail (compile error, fixed in Task 5), then Step 7: commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskPercentageLog.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskPercentageLogConfiguration.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskPercentageLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskPercentageLogRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskPercentageLogRepositoryTests.cs
git commit -m "feat(work): add TaskPercentageLog entity, configuration, and repository"
```

---

### Task 5: DbContext registration, DI registration, and the EF migration

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add 4 `DbSet<T>` properties near
  the existing `TaskEditRequests` one at line 288)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register the 4 new repository interfaces —
  find where `ITaskEditRequestRepository` is registered and add the 4 new ones in the same block, same
  `AddScoped<TInterface, TImpl>()` pattern)
- Create: migration via `dotnet ef migrations add AddTaskTimeTrackingAndEditHistory` (generates
  `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskTimeTrackingAndEditHistory.cs` +
  `.Designer.cs` + updates `ApplicationDbContextModelSnapshot.cs`)

**Interfaces:**
- Consumes: all 4 entities/configurations/repositories from Tasks 1–4.
- Produces: `ApplicationDbContext.TaskEditLogs`, `.TaskStatusChangeLogs`, `.TaskClockingSessions`,
  `.TaskPercentageLogs` (all `DbSet<T>`) — every later Part's handler depends on these existing.

- [ ] **Step 1: Add the 4 DbSet properties**

In `ApplicationDbContext.cs`, immediately after line 288 (`public DbSet<TaskEditRequest> TaskEditRequests
=> Set<TaskEditRequest>();`), add:

```csharp
    public DbSet<TaskEditLog> TaskEditLogs => Set<TaskEditLog>();
    public DbSet<TaskStatusChangeLog> TaskStatusChangeLogs => Set<TaskStatusChangeLog>();
    public DbSet<TaskClockingSession> TaskClockingSessions => Set<TaskClockingSession>();
    public DbSet<TaskPercentageLog> TaskPercentageLogs => Set<TaskPercentageLog>();
```

- [ ] **Step 2: Register the 4 repositories in `DependencyInjection.cs`**

Find the line registering `ITaskEditRequestRepository` (grep for it — it's in the same
`AddScoped<..., Ef...>()` block as `IWorkTaskRepository`). Add immediately after it:

```csharp
        services.AddScoped<ITaskEditLogRepository, EfTaskEditLogRepository>();
        services.AddScoped<ITaskStatusChangeLogRepository, EfTaskStatusChangeLogRepository>();
        services.AddScoped<ITaskClockingSessionRepository, EfTaskClockingSessionRepository>();
        services.AddScoped<ITaskPercentageLogRepository, EfTaskPercentageLogRepository>();
```

- [ ] **Step 3: Build to confirm entities/configs are picked up**

Run: `dotnet build`
Expected: succeeds. The 4 repository tests from Tasks 1–4 should now also compile — run them:
Run: `dotnet test --filter "EfTaskEditLogRepositoryTests|EfTaskStatusChangeLogRepositoryTests|EfTaskClockingSessionRepositoryTests|EfTaskPercentageLogRepositoryTests"`
Expected: all pass.

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddTaskTimeTrackingAndEditHistory --project src/ONEVO.Infrastructure
--startup-project src/ONEVO.Api`

(Confirm the exact `--project`/`--startup-project` values by checking how the most recent migration,
`AddCalendarEvents`, was generated — check for a `dotnet-ef` alias or script in this repo's docs/README
before assuming the flags above are exactly right.)

- [ ] **Step 5: Open the generated migration and add RLS policies for all 4 new tables**

The generated `Up()` will have 4 `CreateTable` calls (one per entity) plus the partial unique index from
Task 3. **After** the last `CreateTable`/`CreateIndex` call, add the RLS block — copy
`20260823172054_AddTaskCategories.cs`'s `TenantTables` array + `Up()`'s `foreach` RLS SQL block +
`Down()`'s matching teardown, but with:

```csharp
private static readonly string[] TenantTables =
[
    "task_edit_logs", "task_status_change_logs", "task_clocking_sessions", "task_percentage_logs"
];
```

Verify the generated `CreateTable` calls used exactly these 4 snake_case table names — if EF pluralized or
named them differently, either fix the `ToTable(...)` calls in Tasks 1–4's configurations and regenerate,
or match `TenantTables` to whatever actually got generated. Do not let this array silently miss a table —
double check with `grep -c "ENABLE ROW LEVEL SECURITY" <migration file>` returning `4` after this step (grep
count of the `foreach` body's SQL, since it's one `Sql()` call per table in the loop, not the literal string
count — inspect the generated code directly).

- [ ] **Step 6: Dry-run validate the migration — do NOT apply it**

```bash
psql "$env:CONNECTION_STRING" -c "BEGIN; \i <path-to-generated-migration-sql-if-you-scripted-it>; ROLLBACK;"
```

(Adjust to however this repo's existing migrations are typically dry-run tested — check
`ops/postgres/setup-local-db.ps1` for a `-DryRun` or `-WhatIf`-style flag before hand-rolling a psql
script; use whatever mechanism the last few migrations in `docs/superpowers/plans/next/` used, if any is
documented, rather than inventing a new validation method.)

- [ ] **Step 7: Commit — do not run the migration against the real database**

```bash
git add src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Infrastructure/Migrations/
git commit -m "feat(work): DbContext/DI registration and migration for task time-tracking and edit-history tables"
```

**STOP after this commit.** Tell the user the migration is written and committed but not applied, and the
exact command to run themselves: `.\ops\postgres\setup-local-db.ps1 -RunMigrations`. Do not run it
yourself under any circumstances, even if asked to "just run it" mid-session — restate this rule and wait.

---

## Self-review checklist for this Part

- [ ] All 4 new tables appear in the `TenantTables` RLS array in the migration — grep the migration file for
  each of the 4 table names and confirm each appears in both the `CreateTable` call and the RLS array.
- [ ] The partial unique index on `task_clocking_sessions` filters on the actual generated column name
  (verify against the migration's `CreateIndex` call, not assumed).
- [ ] No file in this Part references `EditTaskCommand`, `MoveTaskStatusCommand`,
  `ApproveTaskEditRequestCommand`, `TasksController`, or any query handler — those are Parts 2–8.
- [ ] `dotnet build` and the 4 new repository test files all pass before moving to Part 2.
