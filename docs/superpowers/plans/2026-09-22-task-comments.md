# Task Comments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add comments (with replies, rich text, attachments, emoji reactions, and an author-only edit/delete audit trail) to the task detail view.

**Architecture:** Three new tables (`task_comments`, `task_comment_logs`, `task_comment_reactions`). Comments reuse the existing `EntityAsset`/`IFileStorageService` pending-upload pipeline for attachments and inline images (two new upload purposes, one new `EntityAssetOwnerTypes.Comment`), and the existing `TaskAssetLinker` service generalized to also sync a comment's assets. A new `ITaskAccessResolver` service extracts the task-visibility check duplicated across every new comment handler. Comment edit/delete events feed into the existing unified `GetTaskHistoryQuery` feed as a new entry type — no separate log page. Frontend: a new standalone `TaskCommentsComponent` mounted in `task-form-modal.component.ts`'s edit pane, reusing `TaskAttachmentListComponent`, `ColorSwatchPopoverComponent`, and `EmployeeAvatarComponent` verbatim.

**Tech Stack:** .NET 10 / MediatR / EF Core / PostgreSQL (backend), Angular 21 standalone components with signals / Vitest (frontend).

**Spec:** `docs/superpowers/specs/2026-09-22-task-comments-design.md`

## Global Constraints

- No nested replies beyond one level: a reply's target must always be a top-level comment (`ParentCommentId is null`); replying to a reply is a 400.
- Only the comment's author (`comment.EmployeeId == callerEmployeeId`) may edit or delete it — no owner override, no admin override.
- Anyone who can view the task (same rule `GetTaskByIdQueryHandler` already applies) can list, post, reply, and react.
- Comment *creation* is never logged to Task History — only edits and deletes, and only as `{who, when, action}`, never the changed content.
- `comment_attachment` purpose: pdf, png/jpg/jpeg/webp/gif, doc/docx, xls/xlsx, zip — 25MB cap. `comment_description_image` purpose: png/jpeg/webp — 5MB cap. Both mirror the existing `task_attachment`/`task_description_image` rules exactly.
- A deleted comment with existing replies is kept as a soft-deleted row with `Content` stripped from every API response (tombstone); a deleted comment with no replies is filtered out of `GetCommentsForTaskQuery`'s response entirely.
- Backend tests use Moq for handler-level tests (matching `CreateTaskCommandHandlerTests.cs`). Frontend tests use Vitest with `vi.fn()`, matching `task-form-modal.component.spec.ts`.
- Backend repo root for all backend paths below: `HRMS-Backend-v1`. Frontend repo root for all frontend paths below: `Hrms--Web-application---front-end---v1`. Both repos stay on the current branch (`feature/wm-event-duration-hybrid-membership`), matching this project's recent convention of stacking WM features on one long-running branch rather than branching per feature.

---

## Task 1: Domain entities and EF configurations

**Files:**
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskComment.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentLog.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentReaction.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentLogConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentReactionConfiguration.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCommentEntityTests.cs`

**Interfaces:**
- Produces: `TaskComment { TaskId, EmployeeId, ParentCommentId, Content, IsEdited }` (plus inherited `Id/TenantId/CreatedAt/UpdatedAt/CreatedById/IsDeleted/DeletedAt` from `BaseEntity`); `TaskCommentLog { TaskId, CommentId, EmployeeId, Action, OccurredAt }`; `TaskCommentReaction { CommentId, EmployeeId, Emoji }`. Consumed starting Task 2.

This is a thin entity-defaults test (this codebase has no precedent of testing bare entity classes elsewhere, so keep it to defaulting behavior only — it exists to give this task its own red/green cycle per the task-sizing rule, not because entity defaults are risky).

- [x] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCommentEntityTests
{
    [Fact]
    public void TaskComment_Defaults_IsNotEditedAndHasNoParent()
    {
        var comment = new TaskComment();

        Assert.False(comment.IsEdited);
        Assert.Null(comment.ParentCommentId);
        Assert.False(comment.IsDeleted);
    }

    [Fact]
    public void TaskCommentLog_DefaultsAction_ToEdited()
    {
        var log = new TaskCommentLog();

        Assert.Equal(TaskCommentLogActions.Edited, log.Action);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCommentEntityTests"`
Expected: FAIL — compile error, `TaskComment`/`TaskCommentLog`/`TaskCommentLogActions` don't exist.

- [x] **Step 3: Create the entities**

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskComment.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>
/// A comment (or, when ParentCommentId is set, a reply) on a WorkTask. Replies
/// always target a top-level comment — ParentCommentId never points at another
/// reply, enforced by the command handler, not the schema.
/// Soft delete (IsDeleted/DeletedAt) is inherited from BaseEntity: a deleted
/// comment's row is kept (so replies underneath aren't orphaned) but every API
/// response strips Content once IsDeleted is true.
/// </summary>
public class TaskComment : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsEdited { get; set; }
}
```

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentLog.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCommentLogActions
{
    public const string Edited = "edited";
    public const string Deleted = "deleted";
}

/// <summary>
/// Audit row for a comment edit or delete: who + when + which action, never the
/// changed content. Comment creation is never logged here — the comment itself
/// is already visible as the creation event.
/// </summary>
public class TaskCommentLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Action { get; set; } = TaskCommentLogActions.Edited;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
```

```csharp
// src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentReaction.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>One emoji reaction from one employee on one comment. A user may
/// stack several distinct emoji on the same comment (unique per (CommentId,
/// EmployeeId, Emoji)), but not the same emoji twice.</summary>
public class TaskCommentReaction : BaseEntity
{
    public Guid CommentId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCommentEntityTests"`
Expected: PASS.

- [x] **Step 5: Add the EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).HasColumnType("text").IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.TaskId, c.CreatedAt })
            .HasDatabaseName("ix_task_comments_tenant_id_task_id_created_at");
        builder.HasIndex(c => c.ParentCommentId)
            .HasDatabaseName("ix_task_comments_parent_comment_id");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskComment>().WithMany().HasForeignKey(c => c.ParentCommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentLogConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentLogConfiguration : IEntityTypeConfiguration<TaskCommentLog>
{
    public void Configure(EntityTypeBuilder<TaskCommentLog> builder)
    {
        builder.ToTable("task_comment_logs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Action).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.TaskId, l.OccurredAt })
            .HasDatabaseName("ix_task_comment_logs_tenant_id_task_id_occurred_at");

        builder.HasOne<WorkTask>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskComment>().WithMany().HasForeignKey(l => l.CommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentReactionConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class TaskCommentReactionConfiguration : IEntityTypeConfiguration<TaskCommentReaction>
{
    public void Configure(EntityTypeBuilder<TaskCommentReaction> builder)
    {
        builder.ToTable("task_comment_reactions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Emoji).HasMaxLength(32).IsRequired();

        builder.HasIndex(r => new { r.CommentId, r.EmployeeId, r.Emoji })
            .IsUnique()
            .HasDatabaseName("ix_task_comment_reactions_comment_id_employee_id_emoji");

        builder.HasOne<TaskComment>().WithMany().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 6: Build to confirm configurations compile**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: builds clean.

- [x] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskComment.cs src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentLog.cs src/ONEVO.Domain/Features/WorkManagement/Tasks/Entities/TaskCommentReaction.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentConfiguration.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentLogConfiguration.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/TaskCommentReactionConfiguration.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCommentEntityTests.cs
git commit -m "feat: add TaskComment/TaskCommentLog/TaskCommentReaction entities"
```

---

## Task 2: Repositories, DbSets, DI registration

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentLogRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentReactionRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentLogRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentReactionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EfTaskCommentRepositoryTests.cs` — **skip**; this repo has no precedent of unit-testing `Ef*Repository` classes directly (they're exercised via integration tests only, per `EfTaskEditLogRepository`'s total absence from `tests/ONEVO.Tests.Unit`). This task's test coverage instead comes from Task 14's integration tests exercising these repositories through real handlers against a real database — noted here so a reviewer doesn't look for a missing unit test file.

**Interfaces:**
- Produces:
  ```csharp
  public interface ITaskCommentRepository
  {
      Task AddAsync(TaskComment comment, CancellationToken ct = default);
      Task<TaskComment?> GetByIdForTenantAsync(Guid tenantId, Guid commentId, CancellationToken ct = default);
      Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
  }

  public interface ITaskCommentLogRepository
  {
      Task AddAsync(TaskCommentLog log, CancellationToken ct = default);
      Task<IReadOnlyList<TaskCommentLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
  }

  public interface ITaskCommentReactionRepository
  {
      Task AddAsync(TaskCommentReaction reaction, CancellationToken ct = default);
      Task RemoveAsync(TaskCommentReaction reaction, CancellationToken ct = default);
      Task<TaskCommentReaction?> GetAsync(Guid tenantId, Guid commentId, Guid employeeId, string emoji, CancellationToken ct = default);
      Task<IReadOnlyList<TaskCommentReaction>> GetForCommentIdsAsync(Guid tenantId, IReadOnlyList<Guid> commentIds, CancellationToken ct = default);
  }
  ```
  Consumed starting Task 6. `GetByIdForTenantAsync` returns a **change-tracked** entity (no `AsNoTracking`) since edit/delete handlers mutate it directly and call `SaveChangesAsync`, matching `EfFileRecordRepository.GetByIdAsync`'s documented convention (see the 2026-09-14 plan's Task 2 note).

This task has no test of its own — it's pure plumbing (interfaces + EF implementations + registration) that Task 6 onward exercises through handler tests with `Moq`. Steps are build-verification only, not TDD.

- [x] **Step 1: Create the repository interfaces**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentRepository
{
    Task AddAsync(TaskComment comment, CancellationToken ct = default);

    /// <summary>Returns a change-tracked entity — callers may mutate it directly and call
    /// IUnitOfWork.SaveChangesAsync rather than a separate Update method.</summary>
    Task<TaskComment?> GetByIdForTenantAsync(Guid tenantId, Guid commentId, CancellationToken ct = default);

    /// <summary>All comments for the task (top-level and replies, including soft-deleted
    /// ones) ordered oldest-first — filtering/grouping is the query handler's job.</summary>
    Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentLogRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentLogRepository
{
    Task AddAsync(TaskCommentLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCommentLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentReactionRepository.cs
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentReactionRepository
{
    Task AddAsync(TaskCommentReaction reaction, CancellationToken ct = default);
    Task RemoveAsync(TaskCommentReaction reaction, CancellationToken ct = default);
    Task<TaskCommentReaction?> GetAsync(Guid tenantId, Guid commentId, Guid employeeId, string emoji, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCommentReaction>> GetForCommentIdsAsync(Guid tenantId, IReadOnlyList<Guid> commentIds, CancellationToken ct = default);
}
```

- [x] **Step 2: Implement the EF repositories**

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentRepository : ITaskCommentRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskComment comment, CancellationToken ct = default)
        => await _db.TaskComments.AddAsync(comment, ct);

    public async Task<TaskComment?> GetByIdForTenantAsync(Guid tenantId, Guid commentId, CancellationToken ct = default)
        => await _db.TaskComments.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == commentId, ct);

    public async Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskComments.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentLogRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentLogRepository : ITaskCommentLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCommentLog log, CancellationToken ct = default)
        => await _db.TaskCommentLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<TaskCommentLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => await _db.TaskCommentLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.TaskId == taskId)
            .OrderBy(log => log.OccurredAt)
            .ToListAsync(ct);
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentReactionRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCommentReactionRepository : ITaskCommentReactionRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCommentReactionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCommentReaction reaction, CancellationToken ct = default)
        => await _db.TaskCommentReactions.AddAsync(reaction, ct);

    public Task RemoveAsync(TaskCommentReaction reaction, CancellationToken ct = default)
    {
        _db.TaskCommentReactions.Remove(reaction);
        return Task.CompletedTask;
    }

    public async Task<TaskCommentReaction?> GetAsync(Guid tenantId, Guid commentId, Guid employeeId, string emoji, CancellationToken ct = default)
        => await _db.TaskCommentReactions.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.CommentId == commentId && r.EmployeeId == employeeId && r.Emoji == emoji, ct);

    public async Task<IReadOnlyList<TaskCommentReaction>> GetForCommentIdsAsync(Guid tenantId, IReadOnlyList<Guid> commentIds, CancellationToken ct = default)
        => await _db.TaskCommentReactions.AsNoTracking()
            .Where(r => r.TenantId == tenantId && commentIds.Contains(r.CommentId))
            .ToListAsync(ct);
}
```

- [x] **Step 3: Register the DbSets**

In `ApplicationDbContext.cs`, add next to the existing `TaskEditLogs`/`TaskStatusChangeLogs` DbSets:

```csharp
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskCommentLog> TaskCommentLogs => Set<TaskCommentLog>();
    public DbSet<TaskCommentReaction> TaskCommentReactions => Set<TaskCommentReaction>();
```

- [x] **Step 4: Register in DI**

In `DependencyInjection.cs`, add next to the existing `EfTaskEditLogRepository`/`EfTaskStatusChangeLogRepository` registrations:

```csharp
        services.AddScoped<EfTaskCommentRepository>();
        services.AddScoped<ITaskCommentRepository>(sp => sp.GetRequiredService<EfTaskCommentRepository>());
        services.AddScoped<EfTaskCommentLogRepository>();
        services.AddScoped<ITaskCommentLogRepository>(sp => sp.GetRequiredService<EfTaskCommentLogRepository>());
        services.AddScoped<EfTaskCommentReactionRepository>();
        services.AddScoped<ITaskCommentReactionRepository>(sp => sp.GetRequiredService<EfTaskCommentReactionRepository>());
```

- [x] **Step 5: Build to confirm everything wires up**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: builds clean.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentLogRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/ITaskCommentReactionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentLogRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfTaskCommentReactionRepository.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: add task comment repositories and DI wiring"
```

---

## Task 3: Migration — schema + RLS

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskComments.cs` (generated, then hand-edited)
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddTaskComments.Designer.cs` (generated)
- Modify: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (generated)

**Interfaces:**
- Produces: the `task_comments`, `task_comment_logs`, `task_comment_reactions` tables with tenant RLS enabled, matching the pattern in `20260826053637_AddTaskTimeTrackingAndEditHistory.cs`. Consumed by every handler from Task 6 onward.

No TDD cycle here — this is a generated migration, hand-patched for RLS exactly like every prior tenant-owned table in this codebase. Verification is `dotnet build` plus a review diff against Task 1/2's entity/configuration shapes, not a unit test.

- [x] **Step 1: Generate the migration**

Run: `dotnet ef migrations add AddTaskComments --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: creates two new files under `src/ONEVO.Infrastructure/Migrations/` and updates `ApplicationDbContextModelSnapshot.cs`.

- [x] **Step 2: Review the generated `Up()`/`Down()` for the three `CreateTable` calls**

Confirm each of `task_comments`, `task_comment_logs`, `task_comment_reactions` has the standard `BaseEntity` columns (`id, tenant_id, created_at, updated_at, created_by_id, is_deleted, deleted_at`) plus the domain columns from Task 1's configurations, and that the FK constraints match: `task_comments.task_id → tasks.id` (Restrict), `task_comments.parent_comment_id → task_comments.id` (Restrict), `task_comment_logs.task_id → tasks.id` (Restrict), `task_comment_logs.comment_id → task_comments.id` (Restrict), `task_comment_reactions.comment_id → task_comments.id` (Restrict). If EF ordered the three `CreateTable` calls in a way that references a not-yet-created table (e.g. `task_comment_logs` before `task_comments`), reorder them manually — `task_comments` must be created first.

- [x] **Step 3: Hand-add the RLS block**

At the top of the generated migration class, add a `TenantTables` array and wire it into `Up()`/`Down()`, copying the exact block from `20260826053637_AddTaskTimeTrackingAndEditHistory.cs`:

```csharp
        private static readonly string[] TenantTables =
        [
            "task_comments", "task_comment_logs", "task_comment_reactions"
        ];
```

At the end of `Up()` (after all `CreateTable`/`CreateIndex` calls), add:

```csharp
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

At the top of `Down()` (before the `DropTable` calls), add:

```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
```

Order the `DropTable` calls in `Down()` as `task_comment_reactions`, `task_comment_logs`, `task_comments` (children before parent, mirroring `Up()`'s reverse).

- [x] **Step 4: Build**

Run: `dotnet build`
Expected: solution builds clean.

- [x] **Step 5: Apply the migration to the local dev database**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: succeeds, no errors. (This is also what Task 14's integration tests will run against, so catching a schema problem now is cheaper than in Task 14.)

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: add task_comments/task_comment_logs/task_comment_reactions migration"
```

---

## Task 4: Comment upload purposes + owner type + generalize `TaskAssetLinker`

**Files:**
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`
- Modify: `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAssetLinker.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAssetLinker.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposeCatalogTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssetLinkerTests.cs`

**Interfaces:**
- Produces: `UploadPurposeCatalog.CommentAttachment = "comment_attachment"`, `UploadPurposeCatalog.CommentDescriptionImage = "comment_description_image"`; `EntityAssetOwnerTypes.Comment = "comment"`; and two new `ITaskAssetLinker` methods:
  ```csharp
  Task SyncCommentAttachmentsAsync(
      Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

  Task SyncCommentDescriptionImagesAsync(
      Guid tenantId, Guid userId, Guid commentId, string? contentHtml, CancellationToken ct = default);
  ```
  Consumed by Task 6 (`CreateTaskCommentCommandHandler`) and Task 7 (`EditTaskCommentCommandHandler`).

`TaskAssetLinker`'s private `SyncAsync` already takes `purpose` as a parameter but hardcodes `EntityAssetOwnerTypes.Task` as the owner type internally (see the 2026-09-14 plan's Task 4). This task generalizes it to take `ownerType` as a parameter too, so the exact same add/remove-diff logic serves both tasks and comments — no duplicated linking logic.

- [x] **Step 1: Write the failing tests**

In `UploadPurposeCatalogTests.cs`, add:

```csharp
[Fact]
public void CommentAttachment_IsSupported_MatchesTaskAttachmentRule()
{
    Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.CommentAttachment));
    var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.CommentAttachment)!;
    Assert.Equal(25 * 1024 * 1024, rule.MaxSizeBytes);
    Assert.Contains("application/zip", rule.AllowedContentTypes);
}

[Fact]
public void CommentDescriptionImage_IsSupported_ImageOnlyFiveMegabytes()
{
    Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.CommentDescriptionImage));
    var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.CommentDescriptionImage)!;
    Assert.Equal(5 * 1024 * 1024, rule.MaxSizeBytes);
    Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
}
```

In `TaskAssetLinkerTests.cs`, add (reusing the file's existing `Build()`/`Uploaded()` helpers):

```csharp
[Fact]
public async Task SyncCommentAttachmentsAsync_NewFileUploadedByCaller_LinksItUnderCommentOwnerType()
{
    var (linker, assets, _, fileRecords) = Build();
    var commentId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
    assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
    assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Comment, commentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EntityAssetWithFile>());

    await linker.SyncCommentAttachmentsAsync(TenantId, UserId, commentId, new[] { fileId }, CancellationToken.None);

    assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
        a.OwnerType == EntityAssetOwnerTypes.Comment && a.OwnerId == commentId &&
        a.AssetPurpose == UploadPurposeCatalog.CommentAttachment && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SyncCommentDescriptionImagesAsync_ExtractsFileIdFromHtml_LinksItUnderCommentOwnerType()
{
    var (linker, assets, _, fileRecords) = Build();
    var commentId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var html = $"<p>See <img src=\"/api/v1/work/tasks/files/{fileId}\"></p>";
    fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
    assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
    assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Comment, commentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EntityAssetWithFile>());

    await linker.SyncCommentDescriptionImagesAsync(TenantId, UserId, commentId, html, CancellationToken.None);

    assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
        a.OwnerType == EntityAssetOwnerTypes.Comment &&
        a.AssetPurpose == UploadPurposeCatalog.CommentDescriptionImage && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposeCatalogTests|FullyQualifiedName~TaskAssetLinkerTests"`
Expected: FAIL — compile errors, `CommentAttachment`/`CommentDescriptionImage`/`EntityAssetOwnerTypes.Comment`/`SyncCommentAttachmentsAsync`/`SyncCommentDescriptionImagesAsync` don't exist.

- [x] **Step 3: Add the two upload purposes**

In `UploadPurposeCatalog.cs`, add next to `TaskAttachment`/`TaskDescriptionImage`:

```csharp
    public const string CommentAttachment = "comment_attachment";
    public const string CommentDescriptionImage = "comment_description_image";
```

and register both rules in the `Rules` dictionary, reusing the existing `TaskAttachmentContentTypes`/`TaskAttachmentExtensions`/`ImageContentTypes`/`ImageExtensions` lists (same allowed types as the task versions — no new lists needed):

```csharp
        [CommentAttachment] = new UploadPurposeRule(25 * 1024 * 1024, TaskAttachmentContentTypes, TaskAttachmentExtensions),
        [CommentDescriptionImage] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
```

- [x] **Step 4: Add the owner type constant**

In `EntityAssetOwnerTypes.cs`, add:

```csharp
    public const string Comment = "comment";
```

- [x] **Step 5: Run the `UploadPurposeCatalogTests` to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~UploadPurposeCatalogTests"`
Expected: PASS.

- [x] **Step 6: Generalize `TaskAssetLinker`'s private sync method to take an owner type**

In `ITaskAssetLinker.cs`, add the two new methods to the interface:

```csharp
    Task SyncCommentAttachmentsAsync(
        Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default);

    Task SyncCommentDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid commentId, string? contentHtml, CancellationToken ct = default);
```

In `TaskAssetLinker.cs`, change the private `SyncAsync` signature to take `ownerType` as a parameter instead of hardcoding `EntityAssetOwnerTypes.Task`:

```csharp
    private async Task SyncAsync(
        Guid tenantId, Guid userId, string ownerType, Guid ownerId, string purpose, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct)
    {
        var current = (await _assets.ListByOwnerAsync(tenantId, ownerType, ownerId, ct))
            .Where(a => a.AssetPurpose == purpose)
            .ToList();
        var currentFileIds = current.Select(a => a.FileRecordId).ToHashSet();

        foreach (var fileId in desiredFileIds.Distinct())
        {
            if (currentFileIds.Contains(fileId))
                continue;

            var record = await _fileRecords.GetByIdAsync(tenantId, fileId, ct);
            if (record is null || record.DeletedAt is not null || record.UploadedByUserId != userId)
                continue;

            var existingLink = await _assets.GetByFileRecordIdAsync(tenantId, fileId, ct);
            if (existingLink is not null)
                continue;

            await _assets.AddAsync(new EntityAsset
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwnerType = ownerType,
                OwnerId = ownerId,
                AssetPurpose = purpose,
                FileRecordId = fileId,
                IsPrimary = false,
                CreatedByType = "user",
                CreatedById = userId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        var desiredSet = desiredFileIds.ToHashSet();
        foreach (var asset in current.Where(a => !desiredSet.Contains(a.FileRecordId)))
        {
            var tracked = await _assets.GetByIdForTenantAsync(tenantId, asset.Id, ct);
            if (tracked is null)
                continue;

            await _assets.DeleteAsync(tracked, ct);
            await _fileStorage.DeleteAsync(tenantId, userId, asset.FileRecordId, ct);
        }
    }
```

Update the two existing public methods to pass `EntityAssetOwnerTypes.Task` explicitly (their behavior is unchanged, only the call site grows a parameter):

```csharp
    public Task SyncAttachmentsAsync(
        Guid tenantId, Guid userId, Guid taskId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default)
        => SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Task, taskId, UploadPurposeCatalog.TaskAttachment, desiredFileIds, ct);

    public Task SyncDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid taskId, string? descriptionHtml, CancellationToken ct = default)
    {
        var desiredFileIds = ExtractFileIds(descriptionHtml);
        return SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Task, taskId, UploadPurposeCatalog.TaskDescriptionImage, desiredFileIds, ct);
    }
```

Add the two new public methods, and factor the regex-extraction that both `SyncDescriptionImagesAsync` and `SyncCommentDescriptionImagesAsync` need into a shared private `ExtractFileIds` helper (pulled out of the existing inline logic in `SyncDescriptionImagesAsync`):

```csharp
    public Task SyncCommentAttachmentsAsync(
        Guid tenantId, Guid userId, Guid commentId, IReadOnlyList<Guid> desiredFileIds, CancellationToken ct = default)
        => SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Comment, commentId, UploadPurposeCatalog.CommentAttachment, desiredFileIds, ct);

    public Task SyncCommentDescriptionImagesAsync(
        Guid tenantId, Guid userId, Guid commentId, string? contentHtml, CancellationToken ct = default)
    {
        var desiredFileIds = ExtractFileIds(contentHtml);
        return SyncAsync(tenantId, userId, EntityAssetOwnerTypes.Comment, commentId, UploadPurposeCatalog.CommentDescriptionImage, desiredFileIds, ct);
    }

    private static IReadOnlyList<Guid> ExtractFileIds(string? html)
        => string.IsNullOrEmpty(html)
            ? Array.Empty<Guid>()
            : DescriptionImageRefPattern.Matches(html)
                .Select(m => Guid.TryParse(m.Groups[1].Value, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
```

Remove the now-duplicated inline extraction logic from the body of `SyncDescriptionImagesAsync` (replaced by the `ExtractFileIds` call above).

- [x] **Step 7: Run all `TaskAssetLinkerTests` to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskAssetLinkerTests"`
Expected: PASS (all task-owner tests continue passing unchanged, plus the two new comment-owner tests).

- [x] **Step 8: Full build check**

Run: `dotnet build`
Expected: builds clean.

- [x] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAssetLinker.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAssetLinker.cs tests/ONEVO.Tests.Unit/Features/Storage/File/UploadPurposeCatalogTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAssetLinkerTests.cs
git commit -m "feat: add comment_attachment/comment_description_image purposes; generalize TaskAssetLinker for comment owner type"
```

---

## Task 5: `ITaskAccessResolver` shared visibility check

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAccessResolver.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAccessResolver.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAccessResolverTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record TaskAccessContext(WorkTask Task, Guid CallerEmployeeId);

  public interface ITaskAccessResolver
  {
      Task<Result<TaskAccessContext>> ResolveViewableTaskAsync(
          Guid tenantId, Guid userId, Guid taskId, CancellationToken ct = default);
  }
  ```
  Consumed by every handler from Task 6 onward. This extracts the exact visibility check `GetTaskByIdQueryHandler` already performs (auth → tenant → caller employee id → task exists → project active → `projects:read` permission or objective membership) into one reusable service, since six new comment handlers all need it identically. `GetTaskByIdQueryHandler` itself is left untouched — it already works, and refactoring stable code is out of scope for this feature.

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAccessResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (TaskAccessResolver Resolver, Mock<IWorkTaskRepository> Tasks, Mock<IProjectRepository> Projects,
        Mock<IProjectMemberRepository> Members, Mock<IPermissionResolver> Permissions) Build()
    {
        var tasks = new Mock<IWorkTaskRepository>();
        var projects = new Mock<IProjectRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var permissions = new Mock<IPermissionResolver>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var resolver = new TaskAccessResolver(identity.Object, tasks.Object, projects.Object, members.Object, permissions.Object);
        return (resolver, tasks, projects, members, permissions);
    }

    private static WorkTask Task_(Guid objectiveId) => new() { Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId };
    private static Project ActiveProject() => new() { Id = ProjectId, TenantId = TenantId, IsActive = true };

    [Fact]
    public async Task ResolveViewableTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        var (resolver, tasks, _, _, _) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync((WorkTask?)null);

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_HasProjectsReadPermission_Succeeds()
    {
        var (resolver, tasks, projects, _, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "projects:read" });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.CallerEmployeeId);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionAndNotAnObjectiveMember_ReturnsNotFound()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ResolveViewableTaskAsync_NoPermissionButIsObjectiveMember_Succeeds()
    {
        var (resolver, tasks, projects, members, permissions) = Build();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(Task_(ObjectiveId));
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveProject());
        permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ObjectiveId });

        var result = await resolver.ResolveViewableTaskAsync(TenantId, UserId, TaskId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
```

(Check the exact `Project`/`WorkTask` entity namespaces and the `IProjectMemberRepository`/`IPermissionResolver` method signatures against `GetTaskByIdQueryHandler.cs` — copy them verbatim if any differ from what's shown above; that handler is the ground truth this test mirrors.)

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskAccessResolverTests"`
Expected: FAIL — compile error, `TaskAccessResolver`/`TaskAccessContext`/`ITaskAccessResolver` don't exist.

- [x] **Step 3: Create the interface**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAccessResolver.cs
using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed record TaskAccessContext(WorkTask Task, Guid CallerEmployeeId);

/// <summary>
/// The exact task-visibility check GetTaskByIdQueryHandler performs, extracted
/// so every comment handler (which all need the identical check) doesn't
/// duplicate it. Returns Forbidden for auth/tenant problems, NotFound for a
/// task that doesn't exist or that the caller can't see — matching
/// GetTaskByIdQueryHandler's existing "never leak existence" behavior.
/// </summary>
public interface ITaskAccessResolver
{
    Task<Result<TaskAccessContext>> ResolveViewableTaskAsync(
        Guid tenantId, Guid userId, Guid taskId, CancellationToken ct = default);
}
```

- [x] **Step 4: Implement it**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAccessResolver.cs
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskAccessResolver : ITaskAccessResolver
{
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public TaskAccessResolver(
        ICallerIdentityResolver identity, IWorkTaskRepository tasks, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver)
    {
        _identity = identity;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<TaskAccessContext>> ResolveViewableTaskAsync(
        Guid tenantId, Guid userId, Guid taskId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result<TaskAccessContext>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TaskAccessContext>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, taskId, ct);
        if (task is null)
            return Result<TaskAccessContext>.NotFound("Task not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, task.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<TaskAccessContext>.NotFound("Task not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission)
        {
            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<TaskAccessContext>.NotFound("Task not found.");
        }

        return Result<TaskAccessContext>.Success(new TaskAccessContext(task, callerEmployeeId.Value));
    }
}
```

- [x] **Step 5: Register in DI**

In `DependencyInjection.cs`, next to the `ITaskAssetLinker` registration:

```csharp
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Tasks.Services.ITaskAccessResolver,
            ONEVO.Application.Features.WorkManagement.Tasks.Services.TaskAccessResolver>();
```

- [x] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskAccessResolverTests"`
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Services/ITaskAccessResolver.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Services/TaskAccessResolver.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskAccessResolverTests.cs
git commit -m "feat: add ITaskAccessResolver shared task-visibility check"
```

---

## Task 6: Response DTOs + `CreateTaskCommentCommand` (post + reply)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCommentResponses.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskComment/CreateTaskCommentCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskComment/CreateTaskCommentCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommentCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAccessResolver.ResolveViewableTaskAsync` (Task 5), `ITaskCommentRepository.AddAsync`/`GetByIdForTenantAsync` (Task 2), `ITaskAssetLinker.SyncCommentAttachmentsAsync`/`SyncCommentDescriptionImagesAsync` (Task 4), `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` (existing), `IUnitOfWork.SaveChangesAsync` (existing).
- Produces:
  ```csharp
  public sealed record TaskCommentResponse(
      Guid Id, Guid TaskId, Guid? ParentCommentId, Guid EmployeeId, string EmployeeName, string Content,
      bool IsEdited, bool IsDeleted, DateTimeOffset CreatedAt,
      IReadOnlyList<TaskCommentReactionDto> Reactions, IReadOnlyList<TaskAttachmentDto> Attachments,
      IReadOnlyList<TaskCommentResponse> Replies);

  public sealed record TaskCommentReactionDto(string Emoji, IReadOnlyList<Guid> EmployeeIds);

  public sealed record CreateTaskCommentCommand(
      Guid? TaskId, Guid? ParentCommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds
  ) : IRequest<Result<TaskCommentResponse>>;
  ```
  `TaskAttachmentDto` is the existing record already defined alongside `WorkTaskResponse` — reused as-is for comment attachments, not redefined. Consumed by Task 11 (controller). `TaskCommentResponse`/`TaskCommentReactionDto` also consumed by Task 7-10.

**Two routes, one command:** `POST tasks/{taskId}/comments` sends `CreateTaskCommentCommand(taskId, null, ...)`; `POST comments/{id}/replies` sends `CreateTaskCommentCommand(null, id, ...)`. The handler resolves the real `TaskId` from the parent comment when `ParentCommentId` is set (so the reply endpoint never needs the task id in its URL), and 400s if that parent is itself a reply.

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (CreateTaskCommentCommandHandler Handler, Mock<ITaskCommentRepository> Comments, Mock<ITaskAssetLinker> Linker)
        Build(Result<TaskAccessContext>? accessResult = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessResult ?? Result<TaskAccessContext>.Success(
                new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        var linker = new Mock<ITaskAssetLinker>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [EmployeeId] = "Priya" });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateTaskCommentCommandHandler(
            currentUser.Object, access.Object, comments.Object, linker.Object, identity.Object, unitOfWork.Object);
        return (handler, comments, linker);
    }

    [Fact]
    public async Task Handle_TopLevelComment_CreatesAndSyncsAssets()
    {
        var (handler, comments, linker) = Build();

        var result = await handler.Handle(
            new CreateTaskCommentCommand(TaskId, null, "Looks good", new[] { Guid.NewGuid() }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.EmployeeId);
        Assert.Null(result.Value.ParentCommentId);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.TaskId == TaskId && c.EmployeeId == EmployeeId && c.Content == "Looks good"), It.IsAny<CancellationToken>()), Times.Once);
        linker.Verify(x => x.SyncCommentAttachmentsAsync(TenantId, UserId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        linker.Verify(x => x.SyncCommentDescriptionImagesAsync(TenantId, UserId, It.IsAny<Guid>(), "Looks good", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyToTopLevelComment_ResolvesTaskIdFromParent()
    {
        var (handler, comments, _) = Build();
        var parentId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = parentId, TenantId = TenantId, TaskId = TaskId, ParentCommentId = null });

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, parentId, "Agreed", Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(parentId, result.Value!.ParentCommentId);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.TaskId == TaskId && c.ParentCommentId == parentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyToAReply_ReturnsBadRequest()
    {
        var (handler, comments, _) = Build();
        var topLevelId = Guid.NewGuid();
        var replyId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, replyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = replyId, TenantId = TenantId, TaskId = TaskId, ParentCommentId = topLevelId });

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, replyId, "Can't nest", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ParentCommentNotFound_ReturnsNotFound()
    {
        var (handler, comments, _) = Build();
        var parentId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskComment?)null);

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, parentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoTaskVisibility_PropagatesAccessFailure()
    {
        var (handler, _, _) = Build(Result<TaskAccessContext>.NotFound("Task not found."));

        var result = await handler.Handle(
            new CreateTaskCommentCommand(TaskId, null, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskCommentCommandHandlerTests"`
Expected: FAIL — compile error, none of the new types exist yet.

- [x] **Step 3: Create the response DTOs**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCommentResponses.cs
namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskCommentReactionDto(string Emoji, IReadOnlyList<Guid> EmployeeIds);

public sealed record TaskCommentResponse(
    Guid Id, Guid TaskId, Guid? ParentCommentId, Guid EmployeeId, string EmployeeName, string Content,
    bool IsEdited, bool IsDeleted, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskCommentReactionDto> Reactions, IReadOnlyList<TaskAttachmentDto> Attachments,
    IReadOnlyList<TaskCommentResponse> Replies);
```

Check `TaskAttachmentDto`'s exact declared location (it's defined alongside `WorkTaskResponse` — likely in `WorkTaskResponse.cs` in this same `DTOs/Responses` folder) and confirm this new file's namespace matches so it's visible without an extra `using`.

- [x] **Step 4: Create the command**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskComment/CreateTaskCommentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;

/// <summary>
/// Posts a top-level comment (TaskId set, ParentCommentId null) or a reply
/// (ParentCommentId set, TaskId null — the handler resolves the task from the
/// parent). Exactly one of TaskId/ParentCommentId must be non-null; the
/// controller enforces this by construction (two distinct routes).
/// </summary>
public sealed record CreateTaskCommentCommand(
    Guid? TaskId, Guid? ParentCommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds
) : IRequest<Result<TaskCommentResponse>>;
```

- [x] **Step 5: Implement the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskComment/CreateTaskCommentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;

public sealed class CreateTaskCommentCommandHandler : IRequestHandler<CreateTaskCommentCommand, Result<TaskCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskAssetLinker assetLinker, ICallerIdentityResolver identity, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _assetLinker = assetLinker;
        _identity = identity;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskCommentResponse>> Handle(CreateTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCommentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        Guid taskId;
        if (request.ParentCommentId is { } parentId)
        {
            var parent = await _comments.GetByIdForTenantAsync(tenantId, parentId, ct);
            if (parent is null || parent.IsDeleted)
                return Result<TaskCommentResponse>.NotFound("Comment not found.");
            if (parent.ParentCommentId is not null)
                return Result<TaskCommentResponse>.Failure("Cannot reply to a reply.", 400);

            taskId = parent.TaskId;
        }
        else
        {
            taskId = request.TaskId ?? Guid.Empty;
        }

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, taskId, ct);
        if (!access.IsSuccess)
            return Result<TaskCommentResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TaskId = taskId,
            EmployeeId = access.Value!.CallerEmployeeId,
            ParentCommentId = request.ParentCommentId,
            Content = request.Content,
            IsEdited = false,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _comments.AddAsync(comment, ct);
        await _assetLinker.SyncCommentAttachmentsAsync(tenantId, userId, comment.Id, request.AttachmentFileIds, ct);
        await _assetLinker.SyncCommentDescriptionImagesAsync(tenantId, userId, comment.Id, request.Content, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { comment.EmployeeId }, ct);
        var employeeName = names.GetValueOrDefault(comment.EmployeeId) ?? "A teammate";

        return Result<TaskCommentResponse>.Success(new TaskCommentResponse(
            comment.Id, comment.TaskId, comment.ParentCommentId, comment.EmployeeId, employeeName, comment.Content,
            comment.IsEdited, false, comment.CreatedAt,
            Array.Empty<TaskCommentReactionDto>(), Array.Empty<TaskAttachmentDto>(), Array.Empty<TaskCommentResponse>()));
    }
}
```

- [x] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateTaskCommentCommandHandlerTests"`
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskCommentResponses.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/CreateTaskComment/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/CreateTaskCommentCommandHandlerTests.cs
git commit -m "feat: add CreateTaskCommentCommand for posting comments and replies"
```

---

## Task 7: `EditTaskCommentCommand` (author-only, writes the audit log)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskComment/EditTaskCommentCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskComment/EditTaskCommentCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommentCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAccessResolver` (Task 5), `ITaskCommentRepository.GetByIdForTenantAsync` (Task 2, tracked), `ITaskCommentLogRepository.AddAsync` (Task 2), `ITaskAssetLinker` (Task 4).
- Produces: `EditTaskCommentCommand(Guid CommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds) : IRequest<Result<TaskCommentResponse>>`. Consumed by Task 11 (controller).

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AuthorEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private (EditTaskCommentCommandHandler Handler, Mock<ITaskCommentRepository> Comments, Mock<ITaskCommentLogRepository> Logs)
        Build(Guid callerEmployeeId, TaskComment? existingComment)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(
                new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, callerEmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, CommentId, It.IsAny<CancellationToken>())).ReturnsAsync(existingComment);

        var logs = new Mock<ITaskCommentLogRepository>();
        var linker = new Mock<ITaskAssetLinker>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [AuthorEmployeeId] = "Priya" });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new EditTaskCommentCommandHandler(
            currentUser.Object, access.Object, comments.Object, logs.Object, linker.Object, identity.Object, unitOfWork.Object);
        return (handler, comments, logs);
    }

    private static TaskComment Existing() => new()
    {
        Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = AuthorEmployeeId,
        Content = "original", IsEdited = false, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_Author_UpdatesContentAndWritesLog()
    {
        var (handler, comments, logs) = Build(AuthorEmployeeId, Existing());

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "edited text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("edited text", result.Value!.Content);
        Assert.True(result.Value.IsEdited);
        logs.Verify(x => x.AddAsync(It.Is<TaskCommentLog>(l =>
            l.CommentId == CommentId && l.EmployeeId == AuthorEmployeeId && l.Action == TaskCommentLogActions.Edited), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthor_ReturnsForbidden()
    {
        var otherEmployeeId = Guid.NewGuid();
        var (handler, _, logs) = Build(otherEmployeeId, Existing());

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "edited text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        logs.Verify(x => x.AddAsync(It.IsAny<TaskCommentLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CommentNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = Build(AuthorEmployeeId, null);

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedComment_ReturnsNotFound()
    {
        var deleted = Existing();
        deleted.IsDeleted = true;
        var (handler, _, _) = Build(AuthorEmployeeId, deleted);

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditTaskCommentCommandHandlerTests"`
Expected: FAIL — compile error, `EditTaskCommentCommand`/`EditTaskCommentCommandHandler` don't exist.

- [x] **Step 3: Create the command**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskComment/EditTaskCommentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;

public sealed record EditTaskCommentCommand(
    Guid CommentId, string Content, IReadOnlyList<Guid> AttachmentFileIds
) : IRequest<Result<TaskCommentResponse>>;
```

- [x] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskComment/EditTaskCommentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;

public sealed class EditTaskCommentCommandHandler : IRequestHandler<EditTaskCommentCommand, Result<TaskCommentResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentLogRepository _logs;
    private readonly ITaskAssetLinker _assetLinker;
    private readonly ICallerIdentityResolver _identity;
    private readonly IUnitOfWork _unitOfWork;

    public EditTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentLogRepository logs, ITaskAssetLinker assetLinker, ICallerIdentityResolver identity, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _logs = logs;
        _assetLinker = assetLinker;
        _identity = identity;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TaskCommentResponse>> Handle(EditTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<TaskCommentResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result<TaskCommentResponse>.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result<TaskCommentResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (comment.EmployeeId != access.Value!.CallerEmployeeId)
            return Result<TaskCommentResponse>.Forbidden("Only the comment's author may edit it.");

        comment.Content = request.Content;
        comment.IsEdited = true;
        comment.UpdatedAt = DateTimeOffset.UtcNow;

        await _assetLinker.SyncCommentAttachmentsAsync(tenantId, userId, comment.Id, request.AttachmentFileIds, ct);
        await _assetLinker.SyncCommentDescriptionImagesAsync(tenantId, userId, comment.Id, request.Content, ct);
        await _logs.AddAsync(new TaskCommentLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = comment.TaskId, CommentId = comment.Id,
            EmployeeId = comment.EmployeeId, Action = TaskCommentLogActions.Edited,
            OccurredAt = DateTimeOffset.UtcNow, CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, new[] { comment.EmployeeId }, ct);
        var employeeName = names.GetValueOrDefault(comment.EmployeeId) ?? "A teammate";

        return Result<TaskCommentResponse>.Success(new TaskCommentResponse(
            comment.Id, comment.TaskId, comment.ParentCommentId, comment.EmployeeId, employeeName, comment.Content,
            comment.IsEdited, false, comment.CreatedAt,
            Array.Empty<TaskCommentReactionDto>(), Array.Empty<TaskAttachmentDto>(), Array.Empty<TaskCommentResponse>()));
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EditTaskCommentCommandHandlerTests"`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/EditTaskComment/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/EditTaskCommentCommandHandlerTests.cs
git commit -m "feat: add EditTaskCommentCommand, author-only, writes audit log"
```

---

## Task 8: `DeleteTaskCommentCommand` (author-only, soft delete, writes the audit log)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskComment/DeleteTaskCommentCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskComment/DeleteTaskCommentCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskCommentCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAccessResolver` (Task 5), `ITaskCommentRepository.GetByIdForTenantAsync` (Task 2, tracked), `ITaskCommentLogRepository.AddAsync` (Task 2).
- Produces: `DeleteTaskCommentCommand(Guid CommentId) : IRequest<Result>`. Consumed by Task 11.

Attachments/inline images linked to a deleted comment are **not** unlinked or removed — the spec leaves them in place (out of scope; matches the deferred-cleanup decision already made for the description-image feature).

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AuthorEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private (DeleteTaskCommentCommandHandler Handler, Mock<ITaskCommentLogRepository> Logs, Mock<IUnitOfWork> UnitOfWork)
        Build(Guid callerEmployeeId, TaskComment? existingComment)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(
                new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, callerEmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, CommentId, It.IsAny<CancellationToken>())).ReturnsAsync(existingComment);

        var logs = new Mock<ITaskCommentLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteTaskCommentCommandHandler(currentUser.Object, access.Object, comments.Object, logs.Object, unitOfWork.Object);
        return (handler, logs, unitOfWork);
    }

    private static TaskComment Existing() => new()
    {
        Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = AuthorEmployeeId,
        Content = "to delete", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_Author_SoftDeletesAndWritesLog()
    {
        var comment = Existing();
        var (handler, logs, unitOfWork) = Build(AuthorEmployeeId, comment);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(comment.IsDeleted);
        Assert.NotNull(comment.DeletedAt);
        logs.Verify(x => x.AddAsync(It.Is<TaskCommentLog>(l =>
            l.CommentId == CommentId && l.EmployeeId == AuthorEmployeeId && l.Action == TaskCommentLogActions.Deleted), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthor_ReturnsForbidden()
    {
        var comment = Existing();
        var otherEmployeeId = Guid.NewGuid();
        var (handler, logs, _) = Build(otherEmployeeId, comment);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.False(comment.IsDeleted);
        logs.Verify(x => x.AddAsync(It.IsAny<TaskCommentLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CommentNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = Build(AuthorEmployeeId, null);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteTaskCommentCommandHandlerTests"`
Expected: FAIL — compile error.

- [x] **Step 3: Create the command**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskComment/DeleteTaskCommentCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;

public sealed record DeleteTaskCommentCommand(Guid CommentId) : IRequest<Result>;
```

- [x] **Step 4: Implement the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskComment/DeleteTaskCommentCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;

public sealed class DeleteTaskCommentCommandHandler : IRequestHandler<DeleteTaskCommentCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTaskCommentCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteTaskCommentCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error!, access.StatusCode ?? 400);

        if (comment.EmployeeId != access.Value!.CallerEmployeeId)
            return Result.Forbidden("Only the comment's author may delete it.");

        var now = DateTimeOffset.UtcNow;
        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.UpdatedAt = now;

        await _logs.AddAsync(new TaskCommentLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = comment.TaskId, CommentId = comment.Id,
            EmployeeId = comment.EmployeeId, Action = TaskCommentLogActions.Deleted,
            OccurredAt = now, CreatedById = userId, CreatedAt = now
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~DeleteTaskCommentCommandHandlerTests"`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/DeleteTaskComment/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/DeleteTaskCommentCommandHandlerTests.cs
git commit -m "feat: add DeleteTaskCommentCommand, author-only, soft delete, writes audit log"
```

---

## Task 9: Add/remove reaction commands

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddTaskCommentReaction/AddTaskCommentReactionCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddTaskCommentReaction/AddTaskCommentReactionCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RemoveTaskCommentReaction/RemoveTaskCommentReactionCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RemoveTaskCommentReaction/RemoveTaskCommentReactionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCommentReactionCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAccessResolver` (Task 5), `ITaskCommentRepository.GetByIdForTenantAsync` (Task 2), `ITaskCommentReactionRepository` (Task 2).
- Produces: `AddTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>`, `RemoveTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>`. Consumed by Task 11. Both are **idempotent**: adding a reaction that already exists, or removing one that doesn't, is a success no-op — reactions are viewer-only (any user who can view the task may react, no author-only restriction).

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCommentReactionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private static TaskComment Comment() => new() { Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = Guid.NewGuid() };

    private (Mock<ICurrentUser> CurrentUser, Mock<ITaskAccessResolver> Access, Mock<ITaskCommentRepository> Comments, Mock<ITaskCommentReactionRepository> Reactions) BuildDeps()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, CommentId, It.IsAny<CancellationToken>())).ReturnsAsync(Comment());

        var reactions = new Mock<ITaskCommentReactionRepository>();
        return (currentUser, access, comments, reactions);
    }

    [Fact]
    public async Task AddReaction_NewEmoji_AddsIt()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>())).ReturnsAsync((TaskCommentReaction?)null);
        var handler = new AddTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new AddTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.AddAsync(It.Is<TaskCommentReaction>(r => r.CommentId == CommentId && r.EmployeeId == EmployeeId && r.Emoji == "👍"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddReaction_AlreadyExists_IsIdempotentNoOp()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskCommentReaction { CommentId = CommentId, EmployeeId = EmployeeId, Emoji = "👍" });
        var handler = new AddTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new AddTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.AddAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveReaction_Existing_RemovesIt()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        var existing = new TaskCommentReaction { CommentId = CommentId, EmployeeId = EmployeeId, Emoji = "👍" };
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var handler = new RemoveTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new RemoveTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.RemoveAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReaction_DoesNotExist_IsIdempotentNoOp()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>())).ReturnsAsync((TaskCommentReaction?)null);
        var handler = new RemoveTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new RemoveTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.RemoveAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCommentReactionCommandHandlerTests"`
Expected: FAIL — compile error.

- [x] **Step 3: Create the commands**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddTaskCommentReaction/AddTaskCommentReactionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;

public sealed record AddTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RemoveTaskCommentReaction/RemoveTaskCommentReactionCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;

public sealed record RemoveTaskCommentReactionCommand(Guid CommentId, string Emoji) : IRequest<Result>;
```

- [x] **Step 4: Implement the handlers**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddTaskCommentReaction/AddTaskCommentReactionCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;

public sealed class AddTaskCommentReactionCommandHandler : IRequestHandler<AddTaskCommentReactionCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentReactionRepository _reactions;
    private readonly IUnitOfWork _unitOfWork;

    public AddTaskCommentReactionCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentReactionRepository reactions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _reactions = reactions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddTaskCommentReactionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error!, access.StatusCode ?? 400);

        var existing = await _reactions.GetAsync(tenantId, comment.Id, access.Value!.CallerEmployeeId, request.Emoji, ct);
        if (existing is not null)
            return Result.Success();

        await _reactions.AddAsync(new TaskCommentReaction
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CommentId = comment.Id,
            EmployeeId = access.Value.CallerEmployeeId, Emoji = request.Emoji,
            CreatedById = userId, CreatedAt = DateTimeOffset.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RemoveTaskCommentReaction/RemoveTaskCommentReactionCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;

public sealed class RemoveTaskCommentReactionCommandHandler : IRequestHandler<RemoveTaskCommentReactionCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentReactionRepository _reactions;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveTaskCommentReactionCommandHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentReactionRepository reactions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _reactions = reactions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveTaskCommentReactionCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        var comment = await _comments.GetByIdForTenantAsync(tenantId, request.CommentId, ct);
        if (comment is null || comment.IsDeleted)
            return Result.NotFound("Comment not found.");

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, comment.TaskId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error!, access.StatusCode ?? 400);

        var existing = await _reactions.GetAsync(tenantId, comment.Id, access.Value!.CallerEmployeeId, request.Emoji, ct);
        if (existing is null)
            return Result.Success();

        await _reactions.RemoveAsync(existing, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TaskCommentReactionCommandHandlerTests"`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/AddTaskCommentReaction/ src/ONEVO.Application/Features/WorkManagement/Tasks/Commands/RemoveTaskCommentReaction/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/TaskCommentReactionCommandHandlerTests.cs
git commit -m "feat: add add/remove reaction commands, idempotent, viewer-level access"
```

---

## Task 10: `GetCommentsForTaskQuery` (read side — nesting, reactions, attachments, tombstones)

**Files:**
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetCommentsForTask/GetCommentsForTaskQuery.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetCommentsForTask/GetCommentsForTaskQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetCommentsForTaskQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ITaskAccessResolver` (Task 5), `ITaskCommentRepository.GetForTaskAsync` (Task 2), `ITaskCommentReactionRepository.GetForCommentIdsAsync` (Task 2), the new `IEntityAssetRepository.ListByOwnersAsync` (this task), `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`.
- Produces:
  ```csharp
  public sealed record EntityAssetWithFileAndOwner(
      Guid OwnerId, Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt, string AssetPurpose);
  ```
  and `GetCommentsForTaskQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskCommentResponse>>>`. Consumed by Task 11.

`ListByOwnersAsync` is a new, additive batch method alongside the existing single-owner `ListByOwnerAsync` — it doesn't touch or risk any existing call site. Its own record type (`EntityAssetWithFileAndOwner`, carrying `OwnerId`) is separate from `EntityAssetWithFile` (which has no `OwnerId` field and is used by every existing single-owner caller) rather than changing that shared record and forcing every existing call site to adapt.

**Tombstone algorithm:** replies never have their own replies, so a deleted reply is always dropped (never shown, never a tombstone — nothing depends on a reply staying present for a grandchild's sake). A deleted top-level comment is dropped if it has zero *surviving* replies (i.e., counted after already dropping deleted replies), otherwise kept as a tombstone (`Content = ""`, `IsDeleted = true`) with its surviving replies attached. Top-level comments are ordered newest-first (`CreatedAt` descending); replies within a thread are ordered oldest-first (chronological reading order).

- [x] **Step 1: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetCommentsForTaskQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (GetCommentsForTaskQueryHandler Handler, Mock<ITaskCommentRepository> Comments) Build(IReadOnlyList<TaskComment> comments)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var commentRepo = new Mock<ITaskCommentRepository>();
        commentRepo.Setup(x => x.GetForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(comments);

        var reactions = new Mock<ITaskCommentReactionRepository>();
        reactions.Setup(x => x.GetForCommentIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskCommentReaction>());

        var assets = new Mock<IEntityAssetRepository>();
        assets.Setup(x => x.ListByOwnersAsync(TenantId, EntityAssetOwnerTypes.Comment, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFileAndOwner>());

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments.Select(c => c.EmployeeId).Distinct().ToDictionary(id => id, _ => "Priya"));

        var handler = new GetCommentsForTaskQueryHandler(currentUser.Object, access.Object, commentRepo.Object, reactions.Object, assets.Object, identity.Object);
        return (handler, commentRepo);
    }

    [Fact]
    public async Task Handle_TopLevelWithReply_NestsReplyUnderIt()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "root", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "reply", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Single(result.Value![0].Replies);
        Assert.Equal("reply", result.Value[0].Replies[0].Content);
    }

    [Fact]
    public async Task Handle_DeletedTopLevelWithNoReplies_IsExcluded()
    {
        var comments = new List<TaskComment>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "gone", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_DeletedTopLevelWithSurvivingReply_IsTombstoned()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "gone", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "still here", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Single(result.Value!);
        Assert.True(result.Value![0].IsDeleted);
        Assert.Equal(string.Empty, result.Value[0].Content);
        Assert.Single(result.Value[0].Replies);
    }

    [Fact]
    public async Task Handle_DeletedReply_IsDroppedFromItsParent()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "root", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "deleted reply", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Single(result.Value!);
        Assert.Empty(result.Value![0].Replies);
    }

    [Fact]
    public async Task Handle_MultipleTopLevel_OrderedNewestFirst()
    {
        var older = new TaskComment { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "older", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new TaskComment { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "newer", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(new List<TaskComment> { older, newer });

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Equal("newer", result.Value![0].Content);
        Assert.Equal("older", result.Value[1].Content);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetCommentsForTaskQueryHandlerTests"`
Expected: FAIL — compile error.

- [x] **Step 3: Add the batch entity-asset lookup**

In `IEntityAssetRepository.cs`, add next to `ListByOwnerAsync`:

```csharp
    /// <summary>Batched sibling of ListByOwnerAsync for when the caller already has many
    /// owner ids in hand (e.g. every comment on a task) and wants to avoid N+1 queries.
    /// Returns EntityAssetWithFileAndOwner (not EntityAssetWithFile) so existing single-owner
    /// callers are unaffected.</summary>
    Task<IReadOnlyList<EntityAssetWithFileAndOwner>> ListByOwnersAsync(
        Guid tenantId, string ownerType, IReadOnlyList<Guid> ownerIds, CancellationToken ct = default);
```

and, in the same file, the new record next to `EntityAssetWithFile`:

```csharp
public sealed record EntityAssetWithFileAndOwner(
    Guid OwnerId, Guid Id, Guid FileRecordId, string OriginalFileName, long FileSizeBytes, string ContentType, DateTimeOffset CreatedAt, string AssetPurpose);
```

In `EfEntityAssetRepository.cs`, add:

```csharp
    public async Task<IReadOnlyList<EntityAssetWithFileAndOwner>> ListByOwnersAsync(
        Guid tenantId, string ownerType, IReadOnlyList<Guid> ownerIds, CancellationToken ct = default)
    {
        return await _db.EntityAssets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OwnerType == ownerType && ownerIds.Contains(a.OwnerId))
            .Join(_db.FileRecords.AsNoTracking(), a => a.FileRecordId, f => f.Id,
                (a, f) => new EntityAssetWithFileAndOwner(a.OwnerId, a.Id, f.Id, f.OriginalFileName, f.FileSizeBytes, f.ContentType, a.CreatedAt, a.AssetPurpose))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }
```

- [x] **Step 4: Create the query**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetCommentsForTask/GetCommentsForTaskQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

public sealed record GetCommentsForTaskQuery(Guid TaskId) : IRequest<Result<IReadOnlyList<TaskCommentResponse>>>;
```

- [x] **Step 5: Implement the handler**

```csharp
// src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetCommentsForTask/GetCommentsForTaskQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

public sealed class GetCommentsForTaskQueryHandler : IRequestHandler<GetCommentsForTaskQuery, Result<IReadOnlyList<TaskCommentResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITaskAccessResolver _access;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskCommentReactionRepository _reactions;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ICallerIdentityResolver _identity;

    public GetCommentsForTaskQueryHandler(
        ICurrentUser currentUser, ITaskAccessResolver access, ITaskCommentRepository comments,
        ITaskCommentReactionRepository reactions, IEntityAssetRepository entityAssets, ICallerIdentityResolver identity)
    {
        _currentUser = currentUser;
        _access = access;
        _comments = comments;
        _reactions = reactions;
        _entityAssets = entityAssets;
        _identity = identity;
    }

    public async Task<Result<IReadOnlyList<TaskCommentResponse>>> Handle(GetCommentsForTaskQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskCommentResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var access = await _access.ResolveViewableTaskAsync(tenantId, _currentUser.UserId, request.TaskId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<TaskCommentResponse>>.Failure(access.Error!, access.StatusCode ?? 400);

        var allComments = await _comments.GetForTaskAsync(tenantId, request.TaskId, ct);
        var commentIds = allComments.Select(c => c.Id).ToList();

        var allReactions = await _reactions.GetForCommentIdsAsync(tenantId, commentIds, ct);
        var reactionsByComment = allReactions.GroupBy(r => r.CommentId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<TaskCommentReactionDto>)g.GroupBy(r => r.Emoji)
                .Select(eg => new TaskCommentReactionDto(eg.Key, eg.Select(r => r.EmployeeId).ToList()))
                .ToList());

        var allAssets = await _entityAssets.ListByOwnersAsync(tenantId, EntityAssetOwnerTypes.Comment, commentIds, ct);
        var attachmentsByComment = allAssets
            .Where(a => a.AssetPurpose == UploadPurposeCatalog.CommentAttachment)
            .GroupBy(a => a.OwnerId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskAttachmentDto>)g.Select(a => new TaskAttachmentDto(a.FileRecordId, a.OriginalFileName, a.FileSizeBytes, a.ContentType)).ToList());

        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, allComments.Select(c => c.EmployeeId).Distinct().ToList(), ct);

        TaskCommentResponse ToResponse(TaskComment c, IReadOnlyList<TaskCommentResponse> replies) => new(
            c.Id, c.TaskId, c.ParentCommentId, c.EmployeeId, names.GetValueOrDefault(c.EmployeeId) ?? "A teammate",
            c.IsDeleted ? string.Empty : c.Content, c.IsEdited, c.IsDeleted, c.CreatedAt,
            reactionsByComment.GetValueOrDefault(c.Id, Array.Empty<TaskCommentReactionDto>()),
            attachmentsByComment.GetValueOrDefault(c.Id, Array.Empty<TaskAttachmentDto>()),
            replies);

        var repliesByParent = allComments
            .Where(c => c.ParentCommentId is not null && !c.IsDeleted)
            .GroupBy(c => c.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ToList());

        var result = new List<TaskCommentResponse>();
        foreach (var topLevel in allComments.Where(c => c.ParentCommentId is null))
        {
            var replies = repliesByParent.GetValueOrDefault(topLevel.Id, new List<TaskComment>());
            if (topLevel.IsDeleted && replies.Count == 0)
                continue;

            var replyResponses = replies.Select(r => ToResponse(r, Array.Empty<TaskCommentResponse>())).ToList();
            result.Add(ToResponse(topLevel, replyResponses));
        }

        result = result.OrderByDescending(r => r.CreatedAt).ToList();

        return Result<IReadOnlyList<TaskCommentResponse>>.Success(result);
    }
}
```

- [x] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetCommentsForTaskQueryHandlerTests"`
Expected: PASS.

- [x] **Step 7: Full build check**

Run: `dotnet build`
Expected: builds clean (confirms `IEntityAssetRepository`'s new method doesn't break any other hand-written implementer — the earlier repo-wide search found none besides `EfEntityAssetRepository` and Moq usages in tests).

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetCommentsForTask/ tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetCommentsForTaskQueryHandlerTests.cs
git commit -m "feat: add GetCommentsForTaskQuery with nesting, reactions, attachments, tombstones"
```

---

## Task 11: `CommentsController` + API contracts

**Files:**
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentContracts.cs`
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentViewModelMapper.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/CommentsController.cs`
- Test: `tests/ONEVO.Tests.Integration/WorkManagement/TaskCommentsIntegrationTests.cs` (started here, extended in Task 14)

**Interfaces:**
- Consumes: `CreateTaskCommentCommand` (Task 6), `EditTaskCommentCommand` (Task 7), `DeleteTaskCommentCommand` (Task 8), `AddTaskCommentReactionCommand`/`RemoveTaskCommentReactionCommand` (Task 9), `GetCommentsForTaskQuery` (Task 10). Reuses the existing `TaskAttachmentViewModel` from `TaskContracts.cs` — not redefined.
- Produces the 7 routes from the spec:
  ```
  GET    tasks/{taskId:guid}/comments
  POST   tasks/{taskId:guid}/comments
  POST   comments/{id:guid}/replies
  PATCH  comments/{id:guid}
  DELETE comments/{id:guid}
  POST   comments/{id:guid}/reactions
  DELETE comments/{id:guid}/reactions/{emoji}
  ```
  Consumed by the frontend starting Task 16.

This task's own test is a thin controller-reachability check via one integration test (the full behavioral matrix — nesting, reactions, tombstones, access control — is Task 14's job, since it needs the real handlers wired through DI against a real database, which the integration test project already provides via its existing fixture).

- [x] **Step 1: Write the failing integration test**

Check `tests/ONEVO.Tests.Integration/WorkManagement/TaskAttachmentsIntegrationTests.cs` for this project's fixture setup (base class, authenticated `HttpClient`, how a task/project/objective get seeded) and copy that scaffolding exactly. Then add:

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement;

public class TaskCommentsIntegrationTests : /* same base class as TaskAttachmentsIntegrationTests */
{
    [Fact]
    public async Task PostComment_ThenGetComments_ReturnsIt()
    {
        var taskId = await SeedTaskAsync(); // reuse whatever helper TaskAttachmentsIntegrationTests uses

        var postResponse = await Client.PostAsJsonAsync($"api/v1/work/tasks/{taskId}/comments",
            new { content = "First comment", attachmentFileIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await Client.GetAsync($"api/v1/work/tasks/{taskId}/comments");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("First comment", body);
    }
}
```

(Adjust the request/response shapes to match whatever conventions `TaskAttachmentsIntegrationTests.cs` already uses for auth headers, tenant setup, and JSON casing — copy its pattern rather than guessing.)

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TaskCommentsIntegrationTests"`
Expected: FAIL — 404, no such route yet.

- [x] **Step 3: Add the API contracts**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentContracts.cs
namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskCommentRequest(string Content, IReadOnlyList<Guid>? AttachmentFileIds);
public sealed record EditTaskCommentRequest(string Content, IReadOnlyList<Guid>? AttachmentFileIds);
public sealed record AddTaskCommentReactionRequest(string Emoji);

public sealed record TaskCommentReactionViewModel(string Emoji, IReadOnlyList<Guid> EmployeeIds);

public sealed record TaskCommentViewModel(
    Guid Id, Guid TaskId, Guid? ParentCommentId, Guid EmployeeId, string EmployeeName, string Content,
    bool IsEdited, bool IsDeleted, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskCommentReactionViewModel> Reactions, IReadOnlyList<TaskAttachmentViewModel> Attachments,
    IReadOnlyList<TaskCommentViewModel> Replies);
```

- [x] **Step 4: Add the mapper**

```csharp
// src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentViewModelMapper.cs
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public static class TaskCommentViewModelMapper
{
    public static TaskCommentViewModel ToViewModel(this TaskCommentResponse response) => new(
        response.Id, response.TaskId, response.ParentCommentId, response.EmployeeId, response.EmployeeName, response.Content,
        response.IsEdited, response.IsDeleted, response.CreatedAt,
        response.Reactions.Select(r => new TaskCommentReactionViewModel(r.Emoji, r.EmployeeIds)).ToList(),
        response.Attachments.Select(a => new TaskAttachmentViewModel(a.FileId, a.FileName, a.FileSizeBytes, a.ContentType)).ToList(),
        response.Replies.Select(ToViewModel).ToList());
}
```

- [x] **Step 5: Create the controller**

```csharp
// src/ONEVO.Api/Controllers/Tenant/WorkManagement/CommentsController.cs
using Mediator = MediatR.IMediator;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work")]
public class CommentsController : ControllerBase
{
    private readonly Mediator _mediator;

    public CommentsController(Mediator mediator) => _mediator = mediator;

    [HttpGet("tasks/{taskId:guid}/comments")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> GetComments(Guid taskId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCommentsForTaskQuery(taskId), ct);

        return result.IsSuccess
            ? Ok(result.Value!.Select(c => c.ToViewModel()).ToList())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("tasks/{taskId:guid}/comments")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> PostComment(Guid taskId, [FromBody] CreateTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTaskCommentCommand(taskId, null, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("comments/{id:guid}/replies")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> PostReply(Guid id, [FromBody] CreateTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CreateTaskCommentCommand(null, id, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("comments/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> EditComment(Guid id, [FromBody] EditTaskCommentRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new EditTaskCommentCommand(id, request.Content, request.AttachmentFileIds ?? Array.Empty<Guid>()), ct);

        return result.IsSuccess
            ? Ok(result.Value!.ToViewModel())
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("comments/{id:guid}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteTaskCommentCommand(id), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("comments/{id:guid}/reactions")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> AddReaction(Guid id, [FromBody] AddTaskCommentReactionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new AddTaskCommentReactionCommand(id, request.Emoji), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpDelete("comments/{id:guid}/reactions/{emoji}")]
    [RequirePermission("projects:access")]
    public async Task<IActionResult> RemoveReaction(Guid id, string emoji, CancellationToken ct)
    {
        var result = await _mediator.Send(new RemoveTaskCommentReactionCommand(id, emoji), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

Check `TasksController.cs`'s exact `using`s for `[ApiController]`/`[Route]`/`[RequirePermission]`/the real `IMediator` type (the `Mediator = MediatR.IMediator` alias above is illustrative — copy the exact import `TasksController.cs` uses instead) and match them precisely, along with its constructor-injection style, so `CommentsController` is stylistically identical to its sibling.

- [x] **Step 6: Build**

Run: `dotnet build src/ONEVO.Api`
Expected: builds clean.

- [x] **Step 7: Run the integration test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TaskCommentsIntegrationTests"`
Expected: PASS.

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentContracts.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskCommentViewModelMapper.cs src/ONEVO.Api/Controllers/Tenant/WorkManagement/CommentsController.cs tests/ONEVO.Tests.Integration/WorkManagement/TaskCommentsIntegrationTests.cs
git commit -m "feat: add CommentsController wiring all comment endpoints"
```

---

## Task 12: `GetTaskFileQueryHandler` — allow comment-linked files

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile/GetTaskFileQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskFileQueryHandlerTests.cs` (create if it doesn't already exist — check first; if it does, add to it)

**Interfaces:**
- Consumes: `ITaskAccessResolver` (Task 5), `ITaskCommentRepository.GetByIdForTenantAsync` (Task 2).
- Produces: `GET tasks/files/{fileId}` (existing route, unchanged) now also serves files linked to a comment via `EntityAssetOwnerTypes.Comment`, gated by the same task-visibility rule as every other task file.

The handler currently duplicates the exact task-visibility check inline (lines 70-94 of the existing file) instead of using `ITaskAccessResolver` — because that resolver didn't exist yet when this handler was written. Since this handler is being touched anyway to add the comment branch, replace its inline duplicate with a call to `ITaskAccessResolver` (introduced in Task 5) rather than writing a second duplicate for the comment branch. This is in-scope because it's the same edit this task already needs to make, not a speculative unrelated refactor.

- [x] **Step 1: Write the failing unit tests**

Check for an existing `GetTaskFileQueryHandlerTests.cs`; if present, add to it, matching its existing `Build()`-style helper. Add:

```csharp
[Fact]
public async Task Handle_FileLinkedToCommentOnViewableTask_ReturnsStream()
{
    var (handler, entityAssets, comments, access, fileStorage) = Build();
    var commentId = Guid.NewGuid();
    var taskId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EntityAsset { OwnerType = EntityAssetOwnerTypes.Comment, OwnerId = commentId, FileRecordId = fileId });
    comments.Setup(x => x.GetByIdForTenantAsync(TenantId, commentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TaskComment { Id = commentId, TaskId = taskId, TenantId = TenantId });
    access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, taskId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = taskId, TenantId = TenantId }, Guid.NewGuid())));
    fileStorage.Setup(x => x.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "f.png", "image/png")));

    var result = await handler.Handle(new GetTaskFileQuery(fileId), CancellationToken.None);

    Assert.True(result.IsSuccess);
}

[Fact]
public async Task Handle_FileLinkedToCommentOnNonViewableTask_ReturnsNotFound()
{
    var (handler, entityAssets, comments, access, _) = Build();
    var commentId = Guid.NewGuid();
    var taskId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EntityAsset { OwnerType = EntityAssetOwnerTypes.Comment, OwnerId = commentId, FileRecordId = fileId });
    comments.Setup(x => x.GetByIdForTenantAsync(TenantId, commentId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TaskComment { Id = commentId, TaskId = taskId, TenantId = TenantId });
    access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, taskId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<TaskAccessContext>.NotFound("Task not found."));

    var result = await handler.Handle(new GetTaskFileQuery(fileId), CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal(404, result.StatusCode);
}
```

(Match `Build()`'s exact mock construction to whatever `GetTaskFileQueryHandlerTests.cs` already has for `TenantId`/`UserId`/the other existing task-branch tests — those must keep passing unchanged after Step 3's refactor.)

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskFileQueryHandlerTests"`
Expected: FAIL — compile error (constructor shape changes in Step 3) or the two new tests fail with 404 (comment branch not yet handled).

- [x] **Step 3: Replace the inline check with `ITaskAccessResolver`, and add the comment branch**

Replace the handler's constructor and `Handle` body:

```csharp
public sealed class GetTaskFileQueryHandler : IRequestHandler<GetTaskFileQuery, Result<FileStreamDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly ITaskCommentRepository _comments;
    private readonly ITaskAccessResolver _access;
    private readonly IFileStorageService _fileStorage;

    public GetTaskFileQueryHandler(
        ICurrentUser currentUser, IEntityAssetRepository entityAssets, ITaskCommentRepository comments,
        ITaskAccessResolver access, IFileStorageService fileStorage)
    {
        _currentUser = currentUser;
        _entityAssets = entityAssets;
        _comments = comments;
        _access = access;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileStreamDto>> Handle(GetTaskFileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<FileStreamDto>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<FileStreamDto>.Forbidden("Tenant context missing.");

        var link = await _entityAssets.GetByFileRecordIdAsync(tenantId, request.FileId, ct);

        if (link is null)
        {
            var recordResult = await _fileStorage.GetRecordAsync(tenantId, request.FileId, ct);
            if (!recordResult.IsSuccess || recordResult.Value!.DeletedAt is not null || recordResult.Value.UploadedByUserId != userId)
                return Result<FileStreamDto>.NotFound("File not found.");

            return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
        }

        Guid taskId;
        if (link.OwnerType == EntityAssetOwnerTypes.Task)
        {
            taskId = link.OwnerId;
        }
        else if (link.OwnerType == EntityAssetOwnerTypes.Comment)
        {
            var comment = await _comments.GetByIdForTenantAsync(tenantId, link.OwnerId, ct);
            if (comment is null)
                return Result<FileStreamDto>.NotFound("File not found.");
            taskId = comment.TaskId;
        }
        else
        {
            return Result<FileStreamDto>.NotFound("File not found.");
        }

        var access = await _access.ResolveViewableTaskAsync(tenantId, userId, taskId, ct);
        if (!access.IsSuccess)
            return Result<FileStreamDto>.NotFound("File not found.");

        return await _fileStorage.OpenReadAsync(tenantId, request.FileId, ct);
    }
}
```

Update its class doc-comment to mention comment-linked files too, and update the imports: drop `ICallerIdentityResolver`, `IWorkTaskRepository`, `IProjectRepository`, `IProjectMemberRepository`, `IPermissionResolver`, `Auth.Permission.ServiceInterfaces`, `ProjectMembers.RepositoryInterfaces`, `Projects.RepositoryInterfaces` (no longer used directly); add `ITaskAccessResolver` and `ITaskCommentRepository`.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskFileQueryHandlerTests"`
Expected: PASS — both new tests and every pre-existing task-branch test (now routed through `ITaskAccessResolver` instead of the inline check, same outcomes).

- [x] **Step 5: Full build and test run**

Run: `dotnet build && dotnet test tests/ONEVO.Tests.Unit`
Expected: builds clean, full unit suite green (confirms no other test file constructs `GetTaskFileQueryHandler` with the old constructor shape).

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskFile/GetTaskFileQueryHandler.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskFileQueryHandlerTests.cs
git commit -m "feat: serve comment-linked files from GetTaskFile; dedupe visibility check via ITaskAccessResolver"
```

---

## Task 13: Fold comment edit/delete logs into the unified Task History feed

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskHistoryResponses.cs`
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskHistory/GetTaskHistoryQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs` (existing file — add to it)

**Interfaces:**
- Consumes: `ITaskCommentLogRepository.GetForTaskAsync` (Task 2).
- Produces: `TaskHistoryEntryTypes.Comment = "comment"`, `TaskCommentLogEntryDetails(Guid CommentId, string Action)`, and a sixth optional slot on `TaskHistoryEntryResponse`. Consumed by Task 21 (frontend history rendering).

No new endpoint — `GET tasks/{id}/history` (unchanged route) now includes comment edit/delete entries merged into the same sorted feed as edit/status-change/clock-session/percentage-change entries.

- [x] **Step 1: Write the failing unit test**

Open `GetTaskHistoryQueryHandlerTests.cs`, find its existing `Build()`-style fixture (it already mocks `ITaskEditLogRepository`/`ITaskStatusChangeLogRepository`/`ITaskClockingSessionRepository`/`ITaskPercentageLogRepository`), and add a `Mock<ITaskCommentLogRepository>` alongside them (constructor now takes one more dependency — every existing test in this file needs that mock added to its `Build()` call, defaulting to an empty list so pre-existing tests are unaffected). Add:

```csharp
[Fact]
public async Task Handle_CommentLogsPresent_IncludedInMergedFeed()
{
    var taskId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var commentId = Guid.NewGuid();
    var (handler, editLogs, statusLogs, sessions, percentageLogs, commentLogs) = Build();
    SetupEmptyDefaults(editLogs, statusLogs, sessions, percentageLogs); // whatever this file's existing helper is named for "no entries from the other four sources"
    commentLogs.Setup(x => x.GetForTaskAsync(TenantId, taskId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TaskCommentLog> { new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TaskId = taskId, CommentId = commentId,
            EmployeeId = employeeId, Action = TaskCommentLogActions.Deleted, OccurredAt = DateTimeOffset.UtcNow
        } });

    var result = await handler.Handle(new GetTaskHistoryQuery(taskId), CancellationToken.None);

    Assert.True(result.IsSuccess);
    var entry = Assert.Single(result.Value!.Entries);
    Assert.Equal(TaskHistoryEntryTypes.Comment, entry.Type);
    Assert.Equal(commentId, entry.Comment!.CommentId);
    Assert.Equal(TaskCommentLogActions.Deleted, entry.Comment.Action);
}
```

(Match this file's exact existing `Build()` signature/return tuple and "no entries" helper name — copy its established pattern for the other four log sources rather than guessing; the new mock/assertion follows the same shape as the existing `Edit`/`StatusChange` tests already in this file.)

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskHistoryQueryHandlerTests"`
Expected: FAIL — compile error, `TaskHistoryEntryTypes.Comment`/`entry.Comment`/the new constructor parameter don't exist.

- [x] **Step 3: Extend the response DTOs**

In `TaskHistoryResponses.cs`:

```csharp
public static class TaskHistoryEntryTypes
{
    public const string Edit = "edit";
    public const string StatusChange = "status_change";
    public const string ClockSession = "clock_session";
    public const string PercentageChange = "percentage_change";
    public const string Comment = "comment";
}

public sealed record TaskHistoryEntryResponse(
    string Type, DateTimeOffset OccurredAt, Guid EmployeeId, string EmployeeName,
    TaskEditEntryDetails? Edit, TaskStatusChangeEntryDetails? StatusChange,
    TaskClockSessionEntryDetails? ClockSession, TaskPercentageChangeEntryDetails? PercentageChange,
    TaskCommentLogEntryDetails? Comment = null);

public sealed record TaskCommentLogEntryDetails(Guid CommentId, string Action);
```

(`Comment` defaults to `null` so the four existing call sites in `GetTaskHistoryQueryHandler.cs` that construct `TaskHistoryEntryResponse` positionally — for `Edit`/`StatusChange`/`ClockSession`/`PercentageChange` entries — keep compiling unchanged.)

- [x] **Step 4: Fold comment logs into the handler**

In `GetTaskHistoryQueryHandler.cs`, add the constructor dependency:

```csharp
    private readonly ITaskCommentLogRepository _commentLogs;
```

add it to the constructor parameter list and assignment, then after the existing `var percentageLogs = await _percentageLogs.GetForTaskAsync(...)` line, add:

```csharp
        var commentLogs = await _commentLogs.GetForTaskAsync(tenantId, task.Id, ct);
```

and after the existing `foreach (var log in standalonePercentageLogs) { ... }` block, add:

```csharp
        foreach (var log in commentLogs)
        {
            entries.Add(new TaskHistoryEntryResponse(
                TaskHistoryEntryTypes.Comment, log.OccurredAt, log.EmployeeId, string.Empty,
                null, null, null, null, new TaskCommentLogEntryDetails(log.CommentId, log.Action)));
        }
```

- [x] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetTaskHistoryQueryHandlerTests"`
Expected: PASS — the new test plus every pre-existing test in this file (their `Build()` calls now also construct the handler with the comment-logs mock, defaulted empty).

- [x] **Step 6: Expose the new field through the API view model**

`TaskHistoryEntryViewModel`/`TaskHistoryViewModelMapper` in `src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs` (lines ~124-135) reuse the Application-layer detail records (`TaskEditEntryDetails` etc.) directly rather than redefining API-layer duplicates — follow that same convention for `Comment`:

```csharp
public sealed record TaskHistoryEntryViewModel(
    string Type, DateTimeOffset OccurredAt, Guid EmployeeId, string EmployeeName,
    TaskEditEntryDetails? Edit, TaskStatusChangeEntryDetails? StatusChange,
    TaskClockSessionEntryDetails? ClockSession, TaskPercentageChangeEntryDetails? PercentageChange,
    TaskCommentLogEntryDetails? Comment);

public static class TaskHistoryViewModelMapper
{
    public static IReadOnlyList<TaskHistoryEntryViewModel> ToViewModel(this TaskHistoryResponse response) =>
        response.Entries.Select(entry => new TaskHistoryEntryViewModel(
            entry.Type, entry.OccurredAt, entry.EmployeeId, entry.EmployeeName,
            entry.Edit, entry.StatusChange, entry.ClockSession, entry.PercentageChange, entry.Comment)).ToList();
}
```

- [x] **Step 7: Register in DI if not already covered**

`ITaskCommentLogRepository` was already registered in Task 2's DI step — no change needed here. Run: `dotnet build` to confirm `GetTaskHistoryQueryHandler`'s new constructor parameter resolves cleanly wherever it's constructed via DI (the controller action, not a test).

- [x] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/DTOs/Responses/TaskHistoryResponses.cs src/ONEVO.Application/Features/WorkManagement/Tasks/Queries/GetTaskHistory/GetTaskHistoryQueryHandler.cs src/ONEVO.Api/Contracts/WorkManagement/Tasks/TaskContracts.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/Tasks/GetTaskHistoryQueryHandlerTests.cs
git commit -m "feat: fold comment edit/delete audit log into the unified Task History feed"
```

---

## Task 14: Backend integration tests — full flow and access control

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/WorkManagement/TaskCommentsIntegrationTests.cs` (extends Task 11's starter test)

**Interfaces:**
- Consumes: every endpoint from Task 11, running against the real database through the integration test fixture (same one `TaskAttachmentsIntegrationTests.cs` uses).

This is the plan's end-to-end confidence check: real HTTP calls through the real controller, real handlers, real EF Core, real migration from Task 3 — not mocks. It's the closing task precisely because it needs everything from Tasks 1-13 in place.

- [x] **Step 1: Write the additional integration tests**

Add to `TaskCommentsIntegrationTests.cs` (reusing whatever `SeedTaskAsync`/second-user/auth-switching helpers `TaskAttachmentsIntegrationTests.cs` already established for its own cross-user access-control tests):

```csharp
[Fact]
public async Task ReplyToReply_ReturnsBadRequest()
{
    var taskId = await SeedTaskAsync();
    var topLevelId = await PostCommentAsync(taskId, "root");
    var replyId = await PostReplyAsync(topLevelId, "first reply");

    var response = await Client.PostAsJsonAsync($"api/v1/work/comments/{replyId}/replies", new { content = "nested reply" });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task EditComment_AsNonAuthor_ReturnsForbidden()
{
    var taskId = await SeedTaskAsync();
    var commentId = await PostCommentAsync(taskId, "original");
    using var otherUserClient = CreateClientForSecondUser(); // reuse whatever cross-user helper TaskAttachmentsIntegrationTests uses

    var response = await otherUserClient.PatchAsJsonAsync($"api/v1/work/comments/{commentId}", new { content = "hijacked" });

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task DeleteComment_WithReplies_TombstonesButKeepsReplies()
{
    var taskId = await SeedTaskAsync();
    var topLevelId = await PostCommentAsync(taskId, "root");
    await PostReplyAsync(topLevelId, "a reply");

    var deleteResponse = await Client.DeleteAsync($"api/v1/work/comments/{topLevelId}");
    Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

    var comments = await Client.GetFromJsonAsync<List<Dictionary<string, object>>>($"api/v1/work/tasks/{taskId}/comments");
    var topLevel = comments!.Single(c => c["id"].ToString() == topLevelId.ToString());
    Assert.Equal(true, topLevel["isDeleted"]);
    Assert.Equal("", topLevel["content"]);
    Assert.Single((System.Text.Json.JsonElement)(object)topLevel["replies"]!);
}

[Fact]
public async Task AddReaction_ThenGetComments_ShowsGroupedReaction()
{
    var taskId = await SeedTaskAsync();
    var commentId = await PostCommentAsync(taskId, "react to me");

    var addResponse = await Client.PostAsJsonAsync($"api/v1/work/comments/{commentId}/reactions", new { emoji = "👍" });
    Assert.Equal(HttpStatusCode.NoContent, addResponse.StatusCode);

    var comments = await Client.GetFromJsonAsync<List<Dictionary<string, object>>>($"api/v1/work/tasks/{taskId}/comments");
    var comment = comments!.Single(c => c["id"].ToString() == commentId.ToString());
    Assert.True(comment.ContainsKey("reactions"));
}

[Fact]
public async Task CommentAttachment_UploadedThenLinked_ServedThroughGetTaskFile()
{
    var taskId = await SeedTaskAsync();
    var fileId = await UploadPendingFileAsync("comment_attachment", "note.pdf"); // reuse the pending-upload helper from TaskAttachmentsIntegrationTests

    var postResponse = await Client.PostAsJsonAsync($"api/v1/work/tasks/{taskId}/comments",
        new { content = "see attached", attachmentFileIds = new[] { fileId } });
    Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

    var fileResponse = await Client.GetAsync($"api/v1/work/tasks/files/{fileId}");
    Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
}

[Fact]
public async Task GetComments_WithoutTaskVisibility_ReturnsNotFound()
{
    var taskId = await SeedTaskAsync();
    using var outsiderClient = CreateClientForUserWithNoProjectAccess(); // reuse whatever helper establishes a user outside the project

    var response = await outsiderClient.GetAsync($"api/v1/work/tasks/{taskId}/comments");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

(Every helper referenced in parentheses — `PostCommentAsync`, `PostReplyAsync`, `CreateClientForSecondUser`, `UploadPendingFileAsync`, `CreateClientForUserWithNoProjectAccess` — should already exist in some form in `TaskAttachmentsIntegrationTests.cs` or this project's shared integration test base class; add small local wrapper methods in `TaskCommentsIntegrationTests.cs` around whatever the real helper names turn out to be rather than reinventing the seeding/auth plumbing.)

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~TaskCommentsIntegrationTests"`
Expected: FAIL on the newly-added tests only (Task 11's original `PostComment_ThenGetComments_ReturnsIt` already passes).

- [x] **Step 3: Fix anything the tests surface**

If a test fails for a reason other than "not implemented yet" (e.g. a JSON casing mismatch, a route typo), fix it in the relevant Task 6-13 file rather than adjusting the test to match a bug.

- [x] **Step 4: Run the full backend suite**

Run: `dotnet test`
Expected: unit + integration + architecture tests all green.

- [x] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Integration/WorkManagement/TaskCommentsIntegrationTests.cs
git commit -m "test: add end-to-end and access-control coverage for task comments"
```

---

## Task 15: Frontend DTOs

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/work/models/dto/task-comment.dto.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/models/dto/task-history.dto.ts`

**Interfaces:**
- Produces:
  ```typescript
  export interface TaskCommentReactionDto { emoji: string; employeeIds: string[]; }
  export interface TaskCommentDto {
    id: string; taskId: string; parentCommentId: string | null; employeeId: string; employeeName: string;
    content: string; isEdited: boolean; isDeleted: boolean; createdAt: string;
    reactions: TaskCommentReactionDto[]; attachments: AttachmentPill[]; replies: TaskCommentDto[];
  }
  ```
  and `TaskHistoryEntryDto`'s `type` union gains `'comment'`, plus a `comment: TaskCommentLogEntryDetailsDto | null` field. Consumed by Task 16 onward.

Root: `Hrms--Web-application---front-end---v1`. No TDD cycle — these are plain type declarations with no runtime behavior; verified by the TypeScript compiler in Task 16's first build, not a standalone test.

- [x] **Step 1: Create the comment DTOs**

```typescript
// src/app/modules/work/models/dto/task-comment.dto.ts
import { AttachmentPill } from '../../ui/task-attachment-list/task-attachment-list.component';

export interface TaskCommentReactionDto {
  emoji: string;
  employeeIds: string[];
}

export interface TaskCommentDto {
  id: string;
  taskId: string;
  parentCommentId: string | null;
  employeeId: string;
  employeeName: string;
  content: string;
  isEdited: boolean;
  isDeleted: boolean;
  createdAt: string;
  reactions: TaskCommentReactionDto[];
  attachments: AttachmentPill[];
  replies: TaskCommentDto[];
}

export interface CreateTaskCommentRequestDto {
  content: string;
  attachmentFileIds: string[];
}

export interface EditTaskCommentRequestDto {
  content: string;
  attachmentFileIds: string[];
}
```

- [x] **Step 2: Extend the history DTO**

In `task-history.dto.ts`, add:

```typescript
export interface TaskCommentLogEntryDetailsDto {
  commentId: string;
  action: 'edited' | 'deleted';
}
```

and update `TaskHistoryEntryDto`:

```typescript
export interface TaskHistoryEntryDto {
  type: 'edit' | 'status_change' | 'clock_session' | 'percentage_change' | 'comment';
  occurredAt: string;
  employeeId: string;
  employeeName: string;
  edit: TaskEditEntryDetailsDto | null;
  statusChange: TaskStatusChangeEntryDetailsDto | null;
  clockSession: TaskClockSessionEntryDetailsDto | null;
  percentageChange: TaskPercentageChangeEntryDetailsDto | null;
  comment: TaskCommentLogEntryDetailsDto | null;
}
```

- [x] **Step 3: Build check**

Run: `npx tsc --noEmit` (or the project's usual `npm run build` if `tsc --noEmit` isn't wired as a standalone script — check `package.json` first)
Expected: fails at `task-history-feed.component.ts`'s `summaryFor` switch (now non-exhaustive over the widened `type` union) — this is expected and is exactly what Task 21 fixes. Confirm no *other* file fails to compile; if one does, it means something already destructured `TaskHistoryEntryDto` assuming a closed set of fields in a way this addition broke, which needs investigating before continuing.

- [x] **Step 4: Commit**

```bash
git add src/app/modules/work/models/dto/task-comment.dto.ts src/app/modules/work/models/dto/task-history.dto.ts
git commit -m "feat: add task comment DTOs; extend history DTO with comment entry type"
```

---

## Task 16: `TaskCommentApiService`

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/work/data-access/task-comment-api.service.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/data-access/task-comment-api.service.spec.ts`

**Interfaces:**
- Produces:
  ```typescript
  class TaskCommentApiService {
    getComments(taskId: string): Observable<TaskCommentDto[]>;
    postComment(taskId: string, request: CreateTaskCommentRequestDto): Observable<TaskCommentDto>;
    postReply(commentId: string, request: CreateTaskCommentRequestDto): Observable<TaskCommentDto>;
    editComment(commentId: string, request: EditTaskCommentRequestDto): Observable<TaskCommentDto>;
    deleteComment(commentId: string): Observable<void>;
    addReaction(commentId: string, emoji: string): Observable<void>;
    removeReaction(commentId: string, emoji: string): Observable<void>;
  }
  ```
  Consumed by Task 17. Attachment upload reuses `TaskApiService.uploadPendingFile`/`deletePendingFile`/`getFileUrl` directly (Task 17 injects both services) rather than duplicating them here — those methods aren't task-specific despite living on `TaskApiService`, they already take a `purpose` string.

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Write the failing test**

```typescript
// src/app/modules/work/data-access/task-comment-api.service.spec.ts
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TaskCommentApiService } from './task-comment-api.service';
import { environment } from '../../../../environments/environment';

describe('TaskCommentApiService', () => {
  let service: TaskCommentApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TaskCommentApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getComments GETs the task comments route', () => {
    service.getComments('task-1').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/tasks/task-1/comments`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('postComment POSTs to the task comments route', () => {
    service.postComment('task-1', { content: 'hi', attachmentFileIds: [] }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/tasks/task-1/comments`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('postReply POSTs to the replies route', () => {
    service.postReply('comment-1', { content: 'reply', attachmentFileIds: [] }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/comments/comment-1/replies`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('editComment PATCHes the comment route', () => {
    service.editComment('comment-1', { content: 'edited', attachmentFileIds: [] }).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/comments/comment-1`);
    expect(req.request.method).toBe('PATCH');
    req.flush({});
  });

  it('deleteComment DELETEs the comment route', () => {
    service.deleteComment('comment-1').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/comments/comment-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('addReaction POSTs to the reactions route', () => {
    service.addReaction('comment-1', '👍').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/comments/comment-1/reactions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ emoji: '👍' });
    req.flush(null);
  });

  it('removeReaction DELETEs the specific emoji route', () => {
    service.removeReaction('comment-1', '👍').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/work/comments/comment-1/reactions/${encodeURIComponent('👍')}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
```

(Confirm the exact `provideHttpClient`/`provideHttpClientTesting` setup style against a sibling `*.service.spec.ts` in `data-access/` — copy it if it differs from what's shown.)

- [x] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/data-access/task-comment-api.service.spec.ts`
Expected: FAIL — `TaskCommentApiService` doesn't exist.

- [x] **Step 3: Implement the service**

```typescript
// src/app/modules/work/data-access/task-comment-api.service.ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { CreateTaskCommentRequestDto, EditTaskCommentRequestDto, TaskCommentDto } from '../models/dto/task-comment.dto';

@Injectable({ providedIn: 'root' })
export class TaskCommentApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/work`;

  getComments(taskId: string): Observable<TaskCommentDto[]> {
    return this.http.get<TaskCommentDto[]>(`${this.baseUrl}/tasks/${taskId}/comments`);
  }

  postComment(taskId: string, request: CreateTaskCommentRequestDto): Observable<TaskCommentDto> {
    return this.http.post<TaskCommentDto>(`${this.baseUrl}/tasks/${taskId}/comments`, request);
  }

  postReply(commentId: string, request: CreateTaskCommentRequestDto): Observable<TaskCommentDto> {
    return this.http.post<TaskCommentDto>(`${this.baseUrl}/comments/${commentId}/replies`, request);
  }

  editComment(commentId: string, request: EditTaskCommentRequestDto): Observable<TaskCommentDto> {
    return this.http.patch<TaskCommentDto>(`${this.baseUrl}/comments/${commentId}`, request);
  }

  deleteComment(commentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/comments/${commentId}`);
  }

  addReaction(commentId: string, emoji: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/comments/${commentId}/reactions`, { emoji });
  }

  removeReaction(commentId: string, emoji: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/comments/${commentId}/reactions/${encodeURIComponent(emoji)}`);
  }
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/modules/work/data-access/task-comment-api.service.spec.ts`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/app/modules/work/data-access/task-comment-api.service.ts src/app/modules/work/data-access/task-comment-api.service.spec.ts
git commit -m "feat: add TaskCommentApiService"
```

---

## Task 17: `TaskCommentsComponent` — list, post, edit, delete, reply (plain text)

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.spec.ts`

**Interfaces:**
- Consumes: `TaskCommentApiService` (Task 16), `TaskApiService.getCurrentEmployee()` (existing), `EmployeeAvatarComponent` (existing, selector `app-employee-avatar`, `employeeId` input).
- Produces: `app-task-comments` component with `taskId = input.required<string>()`. Consumed by Task 20.

This task ships the core CRUD/nesting/author-gating behavior with a **plain `<textarea>`** composer — rich text formatting, attachment upload, and inline images are Task 18's slice, and emoji reactions are Task 19's, kept separate so each has its own focused red/green cycle rather than one giant component landing in one step.

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Write the failing tests**

```typescript
// src/app/modules/work/ui/task-comments/task-comments.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TaskCommentsComponent } from './task-comments.component';
import { TaskCommentApiService } from '../../data-access/task-comment-api.service';
import { TaskApiService } from '../../data-access/task-api.service';
import { TaskCommentDto } from '../../models/dto/task-comment.dto';

describe('TaskCommentsComponent', () => {
  let fixture: ComponentFixture<TaskCommentsComponent>;
  let component: TaskCommentsComponent;
  let commentApi: { getComments: ReturnType<typeof vi.fn>; postComment: ReturnType<typeof vi.fn>; postReply: ReturnType<typeof vi.fn>; editComment: ReturnType<typeof vi.fn>; deleteComment: ReturnType<typeof vi.fn>; addReaction: ReturnType<typeof vi.fn>; removeReaction: ReturnType<typeof vi.fn> };
  let taskApi: { getCurrentEmployee: ReturnType<typeof vi.fn> };

  const authorComment = (overrides: Partial<TaskCommentDto> = {}): TaskCommentDto => ({
    id: 'c1', taskId: 't1', parentCommentId: null, employeeId: 'me', employeeName: 'Priya',
    content: 'hello', isEdited: false, isDeleted: false, createdAt: new Date().toISOString(),
    reactions: [], attachments: [], replies: [], ...overrides
  });

  beforeEach(async () => {
    commentApi = {
      getComments: vi.fn(() => of([authorComment()])),
      postComment: vi.fn(() => of(authorComment())),
      postReply: vi.fn(() => of(authorComment({ id: 'r1', parentCommentId: 'c1' }))),
      editComment: vi.fn(() => of(authorComment({ content: 'edited', isEdited: true }))),
      deleteComment: vi.fn(() => of(void 0)),
      addReaction: vi.fn(() => of(void 0)),
      removeReaction: vi.fn(() => of(void 0))
    };
    taskApi = { getCurrentEmployee: vi.fn(() => of({ employeeId: 'me' })) };

    await TestBed.configureTestingModule({
      imports: [TaskCommentsComponent],
      providers: [
        { provide: TaskCommentApiService, useValue: commentApi },
        { provide: TaskApiService, useValue: taskApi }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(TaskCommentsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('taskId', 't1');
    fixture.detectChanges();
  });

  it('loads comments for the task on init', () => {
    expect(commentApi.getComments).toHaveBeenCalledWith('t1');
    expect(component.comments().length).toBe(1);
  });

  it('posts a new top-level comment and reloads the list', async () => {
    component.composerContent.set('a new comment');
    await component.submitComment();

    expect(commentApi.postComment).toHaveBeenCalledWith('t1', { content: 'a new comment', attachmentFileIds: [] });
    expect(commentApi.getComments).toHaveBeenCalledTimes(2);
    expect(component.composerContent()).toBe('');
  });

  it('shows edit/delete controls only for the caller\'s own comment', () => {
    expect(component.canModify(authorComment({ employeeId: 'me' }))).toBe(true);
    expect(component.canModify(authorComment({ employeeId: 'someone-else' }))).toBe(false);
  });

  it('editComment sends the update and exits edit mode', async () => {
    component.startEdit(authorComment());
    component.editContent.set('edited');
    await component.saveEdit('c1');

    expect(commentApi.editComment).toHaveBeenCalledWith('c1', { content: 'edited', attachmentFileIds: [] });
    expect(component.editingCommentId()).toBeNull();
  });

  it('deleteComment calls the API and reloads', async () => {
    await component.deleteComment('c1');

    expect(commentApi.deleteComment).toHaveBeenCalledWith('c1');
    expect(commentApi.getComments).toHaveBeenCalledTimes(2);
  });

  it('postReply targets the parent comment id', async () => {
    component.startReply('c1');
    component.replyContent.set('a reply');
    await component.submitReply('c1');

    expect(commentApi.postReply).toHaveBeenCalledWith('c1', { content: 'a reply', attachmentFileIds: [] });
    expect(component.replyingToId()).toBeNull();
  });
});
```

(Check `task-form-modal.component.spec.ts` for this project's exact `TestBed`/`vi.fn()` conventions — e.g. whether it uses `{ provide: X, useValue }` or a lighter mocking helper — and match it if different from what's shown.)

- [x] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/app/modules/work/ui/task-comments/task-comments.component.spec.ts`
Expected: FAIL — `TaskCommentsComponent` doesn't exist.

- [x] **Step 3: Implement the component**

```typescript
// src/app/modules/work/ui/task-comments/task-comments.component.ts
import { Component, inject, input, signal, OnInit } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskCommentApiService } from '../../data-access/task-comment-api.service';
import { TaskApiService } from '../../data-access/task-api.service';
import { TaskCommentDto } from '../../models/dto/task-comment.dto';
import { EmployeeAvatarComponent } from '../employee-avatar/employee-avatar.component';

@Component({
  selector: 'app-task-comments',
  standalone: true,
  imports: [EmployeeAvatarComponent],
  template: `
    <div class="tc-composer">
      <textarea
        class="tc-textarea"
        placeholder="Write a comment..."
        [value]="composerContent()"
        (input)="composerContent.set($any($event.target).value)"
      ></textarea>
      <button type="button" class="tc-post-btn" [disabled]="!composerContent().trim()" (click)="submitComment()">Comment</button>
    </div>

    @if (comments().length === 0) {
      <p class="tc-empty">No comments yet</p>
    } @else {
      <ul class="tc-list">
        @for (comment of comments(); track comment.id) {
          <li class="tc-item">
            <ng-container *ngTemplateOutlet="commentRow; context: { $implicit: comment }" />
            @if (comment.replies.length > 0) {
              <ul class="tc-replies">
                @for (reply of comment.replies; track reply.id) {
                  <li><ng-container *ngTemplateOutlet="commentRow; context: { $implicit: reply }" /></li>
                }
              </ul>
            }
            @if (replyingToId() === comment.id) {
              <div class="tc-reply-composer">
                <textarea class="tc-textarea" [value]="replyContent()" (input)="replyContent.set($any($event.target).value)"></textarea>
                <button type="button" (click)="submitReply(comment.id)">Reply</button>
                <button type="button" (click)="cancelReply()">Cancel</button>
              </div>
            } @else if (!comment.parentCommentId) {
              <button type="button" class="tc-reply-link" (click)="startReply(comment.id)">Reply</button>
            }
          </li>
        }
      </ul>
    }

    <ng-template #commentRow let-comment>
      <div class="tc-row">
        <app-employee-avatar [employeeId]="comment.employeeId" />
        <div class="tc-row-body">
          <div class="tc-row-head">
            <span class="tc-author">{{ comment.employeeName }}</span>
            <time class="tc-time">{{ comment.createdAt | date: 'short' }}</time>
            @if (comment.isEdited) { <span class="tc-edited">(edited)</span> }
          </div>

          @if (comment.isDeleted) {
            <p class="tc-deleted">[comment deleted]</p>
          } @else if (editingCommentId() === comment.id) {
            <textarea class="tc-textarea" [value]="editContent()" (input)="editContent.set($any($event.target).value)"></textarea>
            <button type="button" (click)="saveEdit(comment.id)">Save</button>
            <button type="button" (click)="cancelEdit()">Cancel</button>
          } @else {
            <p class="tc-content">{{ comment.content }}</p>
            @if (canModify(comment)) {
              <div class="tc-actions">
                <button type="button" (click)="startEdit(comment)">Edit</button>
                <button type="button" (click)="deleteComment(comment.id)">Delete</button>
              </div>
            }
          }
        </div>
      </div>
    </ng-template>
  `,
  styles: [`
    .tc-composer { display: flex; flex-direction: column; gap: 6px; margin-bottom: 12px; }
    .tc-textarea { width: 100%; min-height: 60px; border: 1px solid var(--color-border); border-radius: 8px; padding: 8px; font: inherit; resize: vertical; box-sizing: border-box; }
    .tc-post-btn { align-self: flex-end; }
    .tc-empty { color: var(--color-text-secondary); font-size: 13px; }
    .tc-list, .tc-replies { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
    .tc-replies { margin-left: 32px; margin-top: 8px; }
    .tc-row { display: flex; gap: 8px; }
    .tc-row-body { flex: 1; min-width: 0; }
    .tc-row-head { display: flex; align-items: baseline; gap: 6px; }
    .tc-author { font-weight: 650; }
    .tc-time { font-size: 11px; color: var(--color-text-secondary); }
    .tc-edited { font-size: 11px; color: var(--color-text-secondary); }
    .tc-content { margin: 2px 0; white-space: pre-wrap; }
    .tc-deleted { margin: 2px 0; color: var(--color-text-secondary); font-style: italic; }
    .tc-actions, .tc-reply-link { display: flex; gap: 8px; }
  `]
})
export class TaskCommentsComponent implements OnInit {
  private readonly commentApi = inject(TaskCommentApiService);
  private readonly taskApi = inject(TaskApiService);

  taskId = input.required<string>();

  comments = signal<TaskCommentDto[]>([]);
  currentEmployeeId = signal<string | null>(null);
  composerContent = signal('');
  editingCommentId = signal<string | null>(null);
  editContent = signal('');
  replyingToId = signal<string | null>(null);
  replyContent = signal('');

  async ngOnInit(): Promise<void> {
    const me = await firstValueFrom(this.taskApi.getCurrentEmployee());
    this.currentEmployeeId.set(me.employeeId);
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.comments.set(await firstValueFrom(this.commentApi.getComments(this.taskId())));
  }

  canModify(comment: TaskCommentDto): boolean {
    return comment.employeeId === this.currentEmployeeId();
  }

  async submitComment(): Promise<void> {
    const content = this.composerContent().trim();
    if (!content) return;
    await firstValueFrom(this.commentApi.postComment(this.taskId(), { content, attachmentFileIds: [] }));
    this.composerContent.set('');
    await this.reload();
  }

  startEdit(comment: TaskCommentDto): void {
    this.editingCommentId.set(comment.id);
    this.editContent.set(comment.content);
  }

  cancelEdit(): void {
    this.editingCommentId.set(null);
    this.editContent.set('');
  }

  async saveEdit(commentId: string): Promise<void> {
    const content = this.editContent().trim();
    if (!content) return;
    await firstValueFrom(this.commentApi.editComment(commentId, { content, attachmentFileIds: [] }));
    this.editingCommentId.set(null);
    this.editContent.set('');
    await this.reload();
  }

  async deleteComment(commentId: string): Promise<void> {
    await firstValueFrom(this.commentApi.deleteComment(commentId));
    await this.reload();
  }

  startReply(commentId: string): void {
    this.replyingToId.set(commentId);
    this.replyContent.set('');
  }

  cancelReply(): void {
    this.replyingToId.set(null);
    this.replyContent.set('');
  }

  async submitReply(parentCommentId: string): Promise<void> {
    const content = this.replyContent().trim();
    if (!content) return;
    await firstValueFrom(this.commentApi.postReply(parentCommentId, { content, attachmentFileIds: [] }));
    this.replyingToId.set(null);
    this.replyContent.set('');
    await this.reload();
  }
}
```

Check `EmployeeAvatarComponent`'s exact import path (`../employee-avatar/employee-avatar.component` above is inferred from its selector — confirm the real relative path from `ui/task-comments/` before using it) and whether this project's Angular version needs `NgTemplateOutlet`/`DatePipe` explicitly listed in `imports` (add `CommonModule` or the specific standalone directives/pipes if the build reports them missing).

- [x] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/app/modules/work/ui/task-comments/task-comments.component.spec.ts`
Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/app/modules/work/ui/task-comments/task-comments.component.ts src/app/modules/work/ui/task-comments/task-comments.component.spec.ts
git commit -m "feat: add TaskCommentsComponent — list, post, edit, delete, reply"
```

---

## Task 18: `CommentComposerComponent` — rich text, attachments, inline images

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/data-access/task-api.service.ts`
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/comment-composer/comment-composer.component.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/comment-composer/comment-composer.component.spec.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.spec.ts` (update existing tests that assert on the plain-`<textarea>` composer)

**Interfaces:**
- Consumes: `TaskApiService.uploadPendingFile`/`deletePendingFile`/`getFileUrl` (existing, purpose type widened by this task), `TaskAttachmentListComponent` (existing, reused as-is), `ColorSwatchPopoverComponent` (existing, reused as-is).
- Produces:
  ```typescript
  class CommentComposerComponent {
    initialContent = input<string>('');
    initialAttachments = input<AttachmentPill[]>([]);
    placeholder = input<string>('Write a comment...');
    submitLabel = input<string>('Comment');
    showCancel = input<boolean>(false);
    submitted = output<{ content: string; attachmentFileIds: string[] }>();
    cancelled = output<void>();
  }
  ```
  Consumed by Task 20's rewrite of `TaskCommentsComponent`'s template (new comment, edit, and reply all use this one component instead of three separate plain textareas from Task 17).

Task 17's composer was intentionally plain-text so its CRUD/nesting logic could land with a simple red/green cycle. This task is the one place the already-approved rich-text pattern (Bold/Italic/Underline, color/highlight via `execCommand`, inline image insertion with selection save/restore) gets reused for comments — copying the exact mechanism from the 2026-09-14 description-editor feature (`document.execCommand('foreColor'|'hiliteColor'|'insertImage', ...)`), not inventing a new one. It is a **new component**, not a duplication of `task-form-modal.component.ts`'s inline editor, because that file's toolbar logic isn't extracted into a reusable unit — refactoring it now would be an unrelated, riskier change to stable code; this task instead writes a smaller, independent implementation of the same well-established pattern, scoped to comments.

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Widen `TaskApiService`'s upload purpose type**

Open `task-api.service.ts` and find `uploadPendingFile`'s parameter type. If it's currently typed narrowly as `'task_attachment' | 'task_description_image'`, widen it to a shared type:

```typescript
export type PendingUploadPurpose = 'task_attachment' | 'task_description_image' | 'comment_attachment' | 'comment_description_image';
```

placed near the top of `task-api.service.ts` (or wherever `PendingUploadDto` is already imported from, if that's a more natural home — check that file first), and update `uploadPendingFile(file: File, purpose: PendingUploadPurpose)`'s signature to use it. `deletePendingFile`/`getFileUrl` are purpose-agnostic already and need no change.

- [x] **Step 2: Write the failing composer test**

```typescript
// src/app/modules/work/ui/comment-composer/comment-composer.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { CommentComposerComponent } from './comment-composer.component';
import { TaskApiService } from '../../data-access/task-api.service';

describe('CommentComposerComponent', () => {
  let fixture: ComponentFixture<CommentComposerComponent>;
  let component: CommentComposerComponent;
  let taskApi: { uploadPendingFile: ReturnType<typeof vi.fn>; deletePendingFile: ReturnType<typeof vi.fn>; getFileUrl: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    taskApi = {
      uploadPendingFile: vi.fn(() => of({ fileId: 'f1', originalFileName: 'a.png', fileSizeBytes: 10, contentType: 'image/png' })),
      deletePendingFile: vi.fn(() => of(void 0)),
      getFileUrl: vi.fn((id: string) => `/files/${id}`)
    };

    await TestBed.configureTestingModule({
      imports: [CommentComposerComponent],
      providers: [{ provide: TaskApiService, useValue: taskApi }]
    }).compileComponents();

    fixture = TestBed.createComponent(CommentComposerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('emits submitted with the current editor content and attachment ids', () => {
    const submitted = vi.fn();
    component.submitted.subscribe(submitted);
    component.editorHtml.set('<p>hello</p>');
    component.attachedFiles.set([{ fileId: 'f1', name: 'a.png', sizeBytes: 10 }]);

    component.submit();

    expect(submitted).toHaveBeenCalledWith({ content: '<p>hello</p>', attachmentFileIds: ['f1'] });
  });

  it('does not emit submitted when the editor is empty', () => {
    const submitted = vi.fn();
    component.submitted.subscribe(submitted);
    component.editorHtml.set('   ');

    component.submit();

    expect(submitted).not.toHaveBeenCalled();
  });

  it('onFilesSelected uploads each file as comment_attachment and appends the pill', async () => {
    const file = new File(['x'], 'a.png', { type: 'image/png' });
    const fileList = { 0: file, length: 1, item: () => file } as unknown as FileList;

    await component.onFilesSelected(fileList);

    expect(taskApi.uploadPendingFile).toHaveBeenCalledWith(file, 'comment_attachment');
    expect(component.attachedFiles()).toEqual([{ fileId: 'f1', name: 'a.png', sizeBytes: 10 }]);
  });

  it('removeAttachedFile drops the pill and calls deletePendingFile', () => {
    component.attachedFiles.set([{ fileId: 'f1', name: 'a.png', sizeBytes: 10 }]);

    component.removeAttachedFile('f1');

    expect(taskApi.deletePendingFile).toHaveBeenCalledWith('f1');
    expect(component.attachedFiles()).toEqual([]);
  });
});
```

- [x] **Step 3: Run tests to verify they fail**

Run: `npx vitest run src/app/modules/work/ui/comment-composer/comment-composer.component.spec.ts`
Expected: FAIL — `CommentComposerComponent` doesn't exist.

- [x] **Step 4: Implement the composer**

```typescript
// src/app/modules/work/ui/comment-composer/comment-composer.component.ts
import { Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TaskApiService } from '../../data-access/task-api.service';
import { TaskAttachmentListComponent, AttachmentPill } from '../task-attachment-list/task-attachment-list.component';
import { ColorSwatchPopoverComponent } from '../color-swatch-popover/color-swatch-popover.component';

const TEXT_SWATCHES = [
  { name: 'Red', hex: '#e03131' }, { name: 'Orange', hex: '#e8590c' }, { name: 'Yellow', hex: '#f08c00' },
  { name: 'Green', hex: '#2f9e44' }, { name: 'Blue', hex: '#1971c2' }, { name: 'Purple', hex: '#9c36b5' }, { name: 'Pink', hex: '#e64980' }
];

@Component({
  selector: 'app-comment-composer',
  standalone: true,
  imports: [TaskAttachmentListComponent, ColorSwatchPopoverComponent],
  template: `
    <div class="cc-toolbar">
      <button type="button" (mousedown)="$event.preventDefault()" (click)="format('bold')" aria-label="Bold"><b>B</b></button>
      <button type="button" (mousedown)="$event.preventDefault()" (click)="format('italic')" aria-label="Italic"><i>I</i></button>
      <button type="button" (mousedown)="$event.preventDefault()" (click)="format('underline')" aria-label="Underline"><u>U</u></button>
      <span class="cc-toolbar-popover-anchor">
        <button type="button" (mousedown)="$event.preventDefault()" (click)="colorPopoverOpen.set(!colorPopoverOpen())" aria-label="Text color">A</button>
        @if (colorPopoverOpen()) {
          <app-color-swatch-popover label="Remove color" [swatches]="swatches" (colorSelected)="applyColor($event)" />
        }
      </span>
      <span class="cc-toolbar-popover-anchor">
        <button type="button" (mousedown)="$event.preventDefault()" (click)="highlightPopoverOpen.set(!highlightPopoverOpen())" aria-label="Highlight">H</button>
        @if (highlightPopoverOpen()) {
          <app-color-swatch-popover label="Remove highlight" [swatches]="swatches" (colorSelected)="applyHighlight($event)" />
        }
      </span>
      <label class="cc-toolbar-image-btn" aria-label="Insert image">
        <input type="file" accept="image/png,image/jpeg,image/webp" class="cc-hidden-input" (change)="onImageSelected($event)" />
        🖼
      </label>
    </div>

    <div
      #editor
      class="cc-editor"
      contenteditable="true"
      [attr.data-placeholder]="placeholder()"
      (input)="onEditorInput()"
    ></div>

    <app-task-attachment-list [files]="attachedFiles()" (fileSelected)="onFilesSelected($event)" (removed)="removeAttachedFile($event)" />

    @if (uploadError()) {
      <p class="cc-error" role="alert">{{ uploadError() }}</p>
    }

    <div class="cc-actions">
      @if (showCancel()) {
        <button type="button" class="cc-cancel-btn" (click)="cancel()">Cancel</button>
      }
      <button type="button" class="cc-submit-btn" [disabled]="isEditorEmpty()" (click)="submit()">{{ submitLabel() }}</button>
    </div>
  `,
  styles: [`
    .cc-toolbar { display: flex; align-items: center; gap: 4px; margin-bottom: 4px; }
    .cc-toolbar button { border: 1px solid transparent; background: none; border-radius: 6px; width: 26px; height: 26px; cursor: pointer; }
    .cc-toolbar button:hover { background: var(--color-surface-secondary); }
    .cc-toolbar-popover-anchor { position: relative; }
    .cc-toolbar-image-btn { position: relative; display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 6px; cursor: pointer; }
    .cc-toolbar-image-btn:hover { background: var(--color-surface-secondary); }
    .cc-hidden-input { display: none; }
    .cc-editor { min-height: 56px; border: 1px solid var(--color-border); border-radius: 8px; padding: 8px; }
    .cc-editor:empty::before { content: attr(data-placeholder); color: var(--color-text-secondary); }
    .cc-error { color: var(--color-danger); font-size: 12px; }
    .cc-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 6px; }
  `]
})
export class CommentComposerComponent {
  private readonly taskApi = inject(TaskApiService);
  private readonly editorRef = viewChild.required<ElementRef<HTMLDivElement>>('editor');
  private savedRange: Range | null = null;

  readonly swatches = TEXT_SWATCHES;

  initialContent = input<string>('');
  initialAttachments = input<AttachmentPill[]>([]);
  placeholder = input<string>('Write a comment...');
  submitLabel = input<string>('Comment');
  showCancel = input<boolean>(false);

  submitted = output<{ content: string; attachmentFileIds: string[] }>();
  cancelled = output<void>();

  editorHtml = signal('');
  attachedFiles = signal<AttachmentPill[]>([]);
  colorPopoverOpen = signal(false);
  highlightPopoverOpen = signal(false);
  uploadError = signal<string | null>(null);

  constructor() {
    queueMicrotask(() => {
      this.editorRef().nativeElement.innerHTML = this.initialContent();
      this.editorHtml.set(this.initialContent());
    });
    this.attachedFiles.set(this.initialAttachments());
  }

  isEditorEmpty(): boolean {
    return this.editorHtml().replace(/<[^>]*>/g, '').trim().length === 0 && this.attachedFiles().length === 0;
  }

  onEditorInput(): void {
    this.editorHtml.set(this.editorRef().nativeElement.innerHTML);
  }

  format(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false);
    this.onEditorInput();
  }

  applyColor(hex: string | null): void {
    document.execCommand('foreColor', false, hex ?? undefined);
    this.colorPopoverOpen.set(false);
    this.onEditorInput();
  }

  applyHighlight(hex: string | null): void {
    document.execCommand('hiliteColor', false, hex ?? undefined);
    this.highlightPopoverOpen.set(false);
    this.onEditorInput();
  }

  private saveSelection(): void {
    const selection = window.getSelection();
    this.savedRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;
  }

  private restoreSelection(): void {
    if (!this.savedRange) return;
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(this.savedRange);
  }

  async onImageSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.saveSelection();
    try {
      const uploaded = await firstValueFrom(this.taskApi.uploadPendingFile(file, 'comment_description_image'));
      this.restoreSelection();
      document.execCommand('insertImage', false, this.taskApi.getFileUrl(uploaded.fileId));
      this.onEditorInput();
    } catch {
      this.uploadError.set('Could not insert that image. Try again.');
    }
  }

  async onFilesSelected(files: FileList): Promise<void> {
    for (const file of Array.from(files)) {
      try {
        const uploaded = await firstValueFrom(this.taskApi.uploadPendingFile(file, 'comment_attachment'));
        this.attachedFiles.update(current => [...current, { fileId: uploaded.fileId, name: uploaded.originalFileName, sizeBytes: uploaded.fileSizeBytes }]);
      } catch {
        this.uploadError.set(`Could not attach "${file.name}". Try again.`);
      }
    }
  }

  removeAttachedFile(fileId: string): void {
    this.attachedFiles.update(current => current.filter(f => f.fileId !== fileId));
    firstValueFrom(this.taskApi.deletePendingFile(fileId)).catch(() => {});
  }

  submit(): void {
    if (this.isEditorEmpty()) return;
    this.submitted.emit({ content: this.editorHtml(), attachmentFileIds: this.attachedFiles().map(f => f.fileId) });
    this.editorHtml.set('');
    this.attachedFiles.set([]);
    this.editorRef().nativeElement.innerHTML = '';
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
```

Check whether this Angular version's `viewChild.required` signal-query API matches what's already used elsewhere in this codebase (`task-form-modal.component.ts` likely has an equivalent element-ref pattern for its own editor) — match whatever convention is already established rather than introducing a second style.

- [x] **Step 5: Run composer tests to verify they pass**

Run: `npx vitest run src/app/modules/work/ui/comment-composer/comment-composer.component.spec.ts`
Expected: PASS.

- [x] **Step 6: Rewrite `TaskCommentsComponent` to use the composer**

Replace every `<textarea class="tc-textarea" ...>` block in `task-comments.component.ts`'s template (the top-level composer, the reply composer, and the edit-mode composer) with `<app-comment-composer>`, and update the corresponding handler methods:

```typescript
// new top-level composer
<app-comment-composer placeholder="Write a comment..." submitLabel="Comment" (submitted)="submitComment($event)" />

// reply composer (only when replyingToId() === comment.id)
<app-comment-composer placeholder="Write a reply..." submitLabel="Reply" [showCancel]="true"
  (submitted)="submitReply(comment.id, $event)" (cancelled)="cancelReply()" />

// edit composer (only when editingCommentId() === comment.id)
<app-comment-composer [initialContent]="comment.content" [initialAttachments]="comment.attachments" submitLabel="Save" [showCancel]="true"
  (submitted)="saveEdit(comment.id, $event)" (cancelled)="cancelEdit()" />
```

Add `CommentComposerComponent` to `TaskCommentsComponent`'s `imports` array, and update the handler signatures to take the emitted payload instead of reading from the now-removed `composerContent`/`editContent`/`replyContent` signals:

```typescript
  async submitComment(payload: { content: string; attachmentFileIds: string[] }): Promise<void> {
    await firstValueFrom(this.commentApi.postComment(this.taskId(), payload));
    await this.reload();
  }

  async saveEdit(commentId: string, payload: { content: string; attachmentFileIds: string[] }): Promise<void> {
    await firstValueFrom(this.commentApi.editComment(commentId, payload));
    this.editingCommentId.set(null);
    await this.reload();
  }

  async submitReply(parentCommentId: string, payload: { content: string; attachmentFileIds: string[] }): Promise<void> {
    await firstValueFrom(this.commentApi.postReply(parentCommentId, payload));
    this.replyingToId.set(null);
    await this.reload();
  }
```

Remove the now-unused `composerContent`/`editContent`/`replyContent` signals and the plain-`<textarea>` template blocks they backed.

- [x] **Step 7: Update `TaskCommentsComponent`'s existing spec for the new composer contract**

In `task-comments.component.spec.ts`, replace the tests that set `component.composerContent()`/`component.editContent()`/`component.replyContent()` and call `submitComment()`/`saveEdit()`/`submitReply()` with no arguments — they now take the emitted `{ content, attachmentFileIds }` payload directly:

```typescript
it('posts a new top-level comment and reloads the list', async () => {
  await component.submitComment({ content: 'a new comment', attachmentFileIds: [] });

  expect(commentApi.postComment).toHaveBeenCalledWith('t1', { content: 'a new comment', attachmentFileIds: [] });
  expect(commentApi.getComments).toHaveBeenCalledTimes(2);
});
```

apply the same shape change to the edit and reply tests.

- [x] **Step 8: Run both spec files to verify everything passes**

Run: `npx vitest run src/app/modules/work/ui/comment-composer/comment-composer.component.spec.ts src/app/modules/work/ui/task-comments/task-comments.component.spec.ts`
Expected: PASS.

- [x] **Step 9: Commit**

```bash
git add src/app/modules/work/data-access/task-api.service.ts src/app/modules/work/ui/comment-composer/ src/app/modules/work/ui/task-comments/task-comments.component.ts src/app/modules/work/ui/task-comments/task-comments.component.spec.ts
git commit -m "feat: add CommentComposerComponent with rich text, attachments, inline images"
```

---

## Task 19: Emoji reactions — default 4 + full picker, toggle

**Files:**
- Create: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/emoji-picker/emoji-picker.component.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/emoji-picker/emoji-picker.component.spec.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-comments/task-comments.component.spec.ts` (add reaction tests)

**Interfaces:**
- Consumes: `TaskCommentApiService.addReaction`/`removeReaction` (Task 16).
- Produces:
  ```typescript
  class EmojiPickerComponent {
    emojiSelected = output<string>();
  }
  ```
  Consumed by `TaskCommentsComponent`'s per-comment reaction bar.

No emoji-picker library exists in this codebase today (confirmed by search), and pulling one in is a separate dependency decision outside this feature's scope — so `EmojiPickerComponent` is a small self-contained grid of a curated common-emoji set (~70 emoji across faces/gestures/objects/symbols), not an exhaustive Unicode picker. This satisfies "pick any emoji you want" for practical purposes without a new npm dependency; swapping in a full Unicode library later is a drop-in replacement of just this component if ever needed.

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Write the failing emoji-picker test**

```typescript
// src/app/modules/work/ui/emoji-picker/emoji-picker.component.spec.ts
import { TestBed } from '@angular/core/testing';
import { describe, it, expect, vi } from 'vitest';
import { EmojiPickerComponent } from './emoji-picker.component';

describe('EmojiPickerComponent', () => {
  it('emits emojiSelected when a grid emoji is clicked', async () => {
    await TestBed.configureTestingModule({ imports: [EmojiPickerComponent] }).compileComponents();
    const fixture = TestBed.createComponent(EmojiPickerComponent);
    const component = fixture.componentInstance;
    const selected = vi.fn();
    component.emojiSelected.subscribe(selected);
    fixture.detectChanges();

    const firstButton: HTMLButtonElement = fixture.nativeElement.querySelector('.ep-emoji');
    firstButton.click();

    expect(selected).toHaveBeenCalledWith(component.emojis[0]);
  });
});
```

- [x] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/emoji-picker/emoji-picker.component.spec.ts`
Expected: FAIL — `EmojiPickerComponent` doesn't exist.

- [x] **Step 3: Implement the picker**

```typescript
// src/app/modules/work/ui/emoji-picker/emoji-picker.component.ts
import { Component, output } from '@angular/core';

const CURATED_EMOJI = [
  '😀', '😂', '😅', '😊', '😍', '🥳', '😎', '🤔', '😢', '😮', '😡', '🥺', '👀', '🙌',
  '👍', '👎', '👏', '🙏', '💪', '🤝', '✌️', '🤞', '👌', '🤟', '🫡',
  '❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '💯', '🔥', '✨', '⭐', '🎉', '🎊',
  '✅', '❌', '⚠️', '❓', '❗', '💡', '📌', '🚀', '🐛', '🛠️', '📎', '🕐', '📅',
  '☕', '🍕', '🎂', '🍾', '🍻', '🐶', '🐱', '🦄', '🌟', '🌈', '☀️', '🌙', '💤',
  '👋', '🙋', '🤦', '🤷', '💀', '👻', '🤖', '🎯'
];

@Component({
  selector: 'app-emoji-picker',
  standalone: true,
  template: `
    <div class="ep-popover" role="dialog" (click)="$event.stopPropagation()">
      <div class="ep-grid">
        @for (emoji of emojis; track emoji) {
          <button type="button" class="ep-emoji" (mousedown)="$event.preventDefault()" (click)="emojiSelected.emit(emoji)">{{ emoji }}</button>
        }
      </div>
    </div>
  `,
  styles: [`
    .ep-popover { position: absolute; z-index: 20; margin-top: 4px; padding: 8px; border-radius: 8px; background: var(--color-surface); box-shadow: 0 8px 24px rgb(15 23 42 / .18); border: 1px solid var(--color-border); max-width: 260px; }
    .ep-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 4px; max-height: 200px; overflow-y: auto; }
    .ep-emoji { border: none; background: none; cursor: pointer; font-size: 18px; padding: 4px; border-radius: 6px; }
    .ep-emoji:hover { background: var(--color-surface-secondary); }
  `]
})
export class EmojiPickerComponent {
  readonly emojis = CURATED_EMOJI;
  emojiSelected = output<string>();
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/app/modules/work/ui/emoji-picker/emoji-picker.component.spec.ts`
Expected: PASS.

- [x] **Step 5: Add the reaction bar to `TaskCommentsComponent`**

In the `#commentRow` template (from Task 17/18), after the content/edit block, add:

```html
@if (!comment.isDeleted) {
  <div class="tc-reactions">
    @for (defaultEmoji of defaultReactionEmojis; track defaultEmoji) {
      <button
        type="button"
        class="tc-reaction-pill"
        [class.tc-reaction-pill--active]="hasReacted(comment, defaultEmoji)"
        (click)="toggleReaction(comment, defaultEmoji)"
      >{{ defaultEmoji }} {{ reactionCount(comment, defaultEmoji) || '' }}</button>
    }
    @for (r of otherReactions(comment); track r.emoji) {
      <button
        type="button"
        class="tc-reaction-pill"
        [class.tc-reaction-pill--active]="hasReacted(comment, r.emoji)"
        (click)="toggleReaction(comment, r.emoji)"
      >{{ r.emoji }} {{ r.employeeIds.length }}</button>
    }
    <span class="tc-reaction-add-anchor">
      <button type="button" class="tc-reaction-add-btn" (click)="toggleEmojiPicker(comment.id)">+</button>
      @if (emojiPickerOpenFor() === comment.id) {
        <app-emoji-picker (emojiSelected)="onEmojiPicked(comment, $event)" />
      }
    </span>
  </div>
}
```

Add `EmojiPickerComponent` to `TaskCommentsComponent`'s `imports`, and add to the class:

```typescript
  readonly defaultReactionEmojis = ['👍', '❤️', '😂', '🎉'];
  emojiPickerOpenFor = signal<string | null>(null);

  reactionFor(comment: TaskCommentDto, emoji: string): TaskCommentReactionDto | undefined {
    return comment.reactions.find(r => r.emoji === emoji);
  }

  reactionCount(comment: TaskCommentDto, emoji: string): number {
    return this.reactionFor(comment, emoji)?.employeeIds.length ?? 0;
  }

  hasReacted(comment: TaskCommentDto, emoji: string): boolean {
    const me = this.currentEmployeeId();
    return !!me && (this.reactionFor(comment, emoji)?.employeeIds.includes(me) ?? false);
  }

  otherReactions(comment: TaskCommentDto): TaskCommentReactionDto[] {
    return comment.reactions.filter(r => !this.defaultReactionEmojis.includes(r.emoji));
  }

  toggleEmojiPicker(commentId: string): void {
    this.emojiPickerOpenFor.set(this.emojiPickerOpenFor() === commentId ? null : commentId);
  }

  async onEmojiPicked(comment: TaskCommentDto, emoji: string): Promise<void> {
    this.emojiPickerOpenFor.set(null);
    await this.toggleReaction(comment, emoji);
  }

  async toggleReaction(comment: TaskCommentDto, emoji: string): Promise<void> {
    if (this.hasReacted(comment, emoji)) {
      await firstValueFrom(this.commentApi.removeReaction(comment.id, emoji));
    } else {
      await firstValueFrom(this.commentApi.addReaction(comment.id, emoji));
    }
    await this.reload();
  }
```

Add matching styles:

```css
.tc-reactions { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; margin-top: 6px; }
.tc-reaction-pill { border: 1px solid var(--color-border); border-radius: 999px; background: var(--color-surface); padding: 2px 8px; font-size: 12px; cursor: pointer; }
.tc-reaction-pill--active { border-color: var(--color-accent); background: color-mix(in srgb, var(--color-accent) 12%, var(--color-surface)); }
.tc-reaction-add-anchor { position: relative; }
.tc-reaction-add-btn { border: 1px dashed var(--color-border); border-radius: 999px; background: none; width: 22px; height: 22px; cursor: pointer; color: var(--color-text-secondary); }
```

- [x] **Step 6: Write and run the `TaskCommentsComponent` reaction tests**

Add to `task-comments.component.spec.ts` (extending the mocked `commentApi` fixture already in the file from Task 17/18):

```typescript
it('toggleReaction adds a reaction the caller has not made yet', async () => {
  const comment = authorComment({ reactions: [] });

  await component.toggleReaction(comment, '👍');

  expect(commentApi.addReaction).toHaveBeenCalledWith('c1', '👍');
});

it('toggleReaction removes a reaction the caller already made', async () => {
  const comment = authorComment({ reactions: [{ emoji: '👍', employeeIds: ['me'] }] });

  await component.toggleReaction(comment, '👍');

  expect(commentApi.removeReaction).toHaveBeenCalledWith('c1', '👍');
});

it('hasReacted reflects whether the current employee is in the reaction', () => {
  const comment = authorComment({ reactions: [{ emoji: '👍', employeeIds: ['me'] }] });

  expect(component.hasReacted(comment, '👍')).toBe(true);
  expect(component.hasReacted(comment, '🎉')).toBe(false);
});
```

Run: `npx vitest run src/app/modules/work/ui/task-comments/task-comments.component.spec.ts`
Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add src/app/modules/work/ui/emoji-picker/ src/app/modules/work/ui/task-comments/task-comments.component.ts src/app/modules/work/ui/task-comments/task-comments.component.spec.ts
git commit -m "feat: add emoji reactions — default set plus curated picker, toggle"
```

---

## Task 20: Mount `TaskCommentsComponent` in the task detail modal

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts`
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`

**Interfaces:**
- Consumes: `TaskCommentsComponent` (Tasks 17-19).
- Produces: a new collapsible "Comments" section in `tfm__pane-side`, edit-mode only (comments require an existing task id, same constraint as the existing Attachments/Activity Log sections).

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Write the failing test**

Add to `task-form-modal.component.spec.ts` (matching whatever pattern its existing Activity Log/Attachments section tests already use for opening the modal in edit mode with a seeded `taskId`):

```typescript
it('mounts app-task-comments with the current task id in edit mode', () => {
  // reuse this file's existing edit-mode setup (whatever seeds `component.taskId` / opens the modal on an existing task)
  fixture.detectChanges();

  const commentsEl: HTMLElement | null = fixture.nativeElement.querySelector('app-task-comments');
  expect(commentsEl).not.toBeNull();
});
```

- [x] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts -t "mounts app-task-comments"`
Expected: FAIL — no `app-task-comments` element in the template yet.

- [x] **Step 3: Add the import**

Near the top of `task-form-modal.component.ts`, add:

```typescript
import { TaskCommentsComponent } from '../task-comments/task-comments.component';
```

and add `TaskCommentsComponent` to the `@Component`'s `imports` array (alongside `TaskHistoryFeedComponent`/`TaskAttachmentListComponent`/etc., found around line 58).

- [x] **Step 4: Add the template section**

After the existing "Activity Log Section" block (ends around line 1391, right before `@if (errorMessage()) { ... }`), add:

```html
<!-- Comments Section -->
@if (taskId()) {
  <div class="tfm-doc-section tfm-doc-section--collapsible">
    <button
      type="button"
      class="tfm-doc-section-toggle"
      (click)="showComments.set(!showComments())"
      [attr.aria-expanded]="showComments()"
    >
      <div class="tfm-doc-section-head">
        <span class="tfm-doc-section-icon">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z" /></svg>
        </span>
        <span class="tfm-doc-section-title">Comments</span>
      </div>
      <svg class="tfm-chevron-svg" [class.tfm-chevron-svg--open]="showComments()" viewBox="0 0 24 24" aria-hidden="true">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </button>

    @if (showComments()) {
      <div class="tfm-doc-activity-body">
        <app-task-comments [taskId]="taskId()!" />
      </div>
    }
  </div>
}
```

(Check `taskId()`'s exact signal name/type against this file's other edit-mode-only sections — the Attachments/Activity Log sections already gate on it or an equivalent "is this an existing task" signal; match whichever one they use rather than assuming `taskId()`.)

- [x] **Step 5: Add the `showComments` signal**

Near the existing `history = signal<TaskHistoryEntryDto[]>([])`/`showActivityLog` signal declarations, add:

```typescript
  showComments = signal(false);
```

- [x] **Step 6: Run the test to verify it passes**

Run: `npx vitest run src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts -t "mounts app-task-comments"`
Expected: PASS.

- [x] **Step 7: Run the full `task-form-modal` spec suite**

Run: `npx vitest run src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts`
Expected: PASS (confirms the new import/template addition didn't break any pre-existing test in this large file).

- [x] **Step 8: Commit**

```bash
git add src/app/modules/work/ui/task-form-modal/task-form-modal.component.ts src/app/modules/work/ui/task-form-modal/task-form-modal.component.spec.ts
git commit -m "feat: mount TaskCommentsComponent in the task detail modal"
```

---

## Task 21: Render comment entries in the Task History feed

**Files:**
- Modify: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-history-feed/task-history-feed.component.ts`
- Test: `Hrms--Web-application---front-end---v1/src/app/modules/work/ui/task-history-feed/task-history-feed.component.spec.ts` (create if it doesn't already exist — check first)

**Interfaces:**
- Consumes: `TaskHistoryEntryDto`'s widened `type`/`comment` fields (Task 15).
- Produces: `summaryFor()` handles `'comment'` entries ("posted a comment" is intentionally never reachable — see below), completing the switch Task 15's DTO widening left non-exhaustive.

Task 15 widened `TaskHistoryEntryDto['type']` to include `'comment'`, which makes `summaryFor()`'s `switch` in this file non-exhaustive — TypeScript already flags this as a compile error (confirmed in Task 15's Step 3). This task is the fix.

Root: `Hrms--Web-application---front-end---v1`.

- [x] **Step 1: Write the failing test**

Check whether `task-history-feed.component.spec.ts` already exists; if so add to it, otherwise create it matching the conventions of a sibling `*.component.spec.ts` in `ui/`:

```typescript
import { TestBed } from '@angular/core/testing';
import { describe, it, expect } from 'vitest';
import { TaskHistoryFeedComponent } from './task-history-feed.component';
import { TaskHistoryEntryDto } from '../../models/dto/task-history.dto';

describe('TaskHistoryFeedComponent', () => {
  const commentEditedEntry: TaskHistoryEntryDto = {
    type: 'comment', occurredAt: new Date().toISOString(), employeeId: 'e1', employeeName: 'Priya',
    edit: null, statusChange: null, clockSession: null, percentageChange: null,
    comment: { commentId: 'c1', action: 'edited' }
  };
  const commentDeletedEntry: TaskHistoryEntryDto = { ...commentEditedEntry, comment: { commentId: 'c1', action: 'deleted' } };

  it('summarizes an edited-comment entry', async () => {
    await TestBed.configureTestingModule({ imports: [TaskHistoryFeedComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TaskHistoryFeedComponent);
    fixture.componentRef.setInput('entries', [commentEditedEntry]);
    const component = fixture.componentInstance;

    expect(component.summaryFor(commentEditedEntry)).toBe('edited a comment');
  });

  it('summarizes a deleted-comment entry', async () => {
    await TestBed.configureTestingModule({ imports: [TaskHistoryFeedComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TaskHistoryFeedComponent);
    fixture.componentRef.setInput('entries', [commentDeletedEntry]);
    const component = fixture.componentInstance;

    expect(component.summaryFor(commentDeletedEntry)).toBe('deleted a comment');
  });
});
```

- [x] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/app/modules/work/ui/task-history-feed/task-history-feed.component.spec.ts`
Expected: FAIL (either a TypeScript compile error from the non-exhaustive switch, or the test itself failing since `'comment'` isn't handled).

- [x] **Step 3: Extend `summaryFor`**

In `task-history-feed.component.ts`, add a case to the existing `switch` in `summaryFor()`:

```typescript
      case 'comment': {
        const c = entry.comment!;
        return c.action === 'deleted' ? 'deleted a comment' : 'edited a comment';
      }
```

- [x] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/app/modules/work/ui/task-history-feed/task-history-feed.component.spec.ts`
Expected: PASS.

- [x] **Step 5: Full frontend build and test suite**

Run: `npx tsc --noEmit && npx vitest run`
Expected: builds clean, full Vitest suite green (confirms Task 15's compile error from the widened DTO is now fully resolved and nothing else broke).

- [x] **Step 6: Commit**

```bash
git add src/app/modules/work/ui/task-history-feed/task-history-feed.component.ts src/app/modules/work/ui/task-history-feed/task-history-feed.component.spec.ts
git commit -m "feat: render comment edit/delete entries in the Task History feed"
```

---

## Task 22: Manual browser verification

No code changes — this is the closing verification pass across both repos together, using the dev servers (not a substitute for Tasks 1-21's automated tests, which must already be green).

- [ ] **Step 1: Start both dev servers**

Backend: run the API project (whatever command `docs/superpowers/plans/2026-09-14-task-attachments-rich-description.md`'s own manual-verification step used, or `dotnet run --project src/ONEVO.Api`). Frontend: `npm start` (or this repo's equivalent) in `Hrms--Web-application---front-end---v1`.

- [ ] **Step 2: Post a comment with formatting, an attachment, and an inline image**

Open a task's detail modal, post a comment using Bold/Italic/Underline/color/highlight, attach a file, and insert an inline image. Confirm it appears in the list immediately with formatting rendered and the attachment pill/inline image both visible.

- [ ] **Step 3: Reply, react, edit, delete**

Reply to the comment (confirm the reply nests under it, flat, not further nestable). React with one of the 4 default emoji and one picked from the "+" picker; confirm both toggle on/off correctly and show the reactor count. Edit the comment; confirm the "(edited)" tag appears and the content updates. As a *different* user (or by temporarily forging `currentEmployeeId` in devtools if a second test account isn't handy), confirm Edit/Delete controls are absent on someone else's comment. Delete a comment that has no replies and confirm it disappears from the list entirely; delete a comment that has a reply and confirm it becomes a "[comment deleted]" tombstone with the reply still visible underneath.

- [ ] **Step 4: Confirm the Task History feed picks up the edit/delete**

Open the task's existing "Activity log" section and confirm the comment edit and comment delete from Step 3 both appear in the same unified feed as any other history entries, with "edited a comment"/"deleted a comment" text.

- [ ] **Step 5: Report back**

Note any UX rough edges found (e.g. picker positioning, emoji rendering, reload flicker) as follow-up items — do not silently patch scope beyond what this plan covers without flagging it first.

