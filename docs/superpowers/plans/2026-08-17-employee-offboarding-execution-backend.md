# Employee Offboarding Execution — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend for the 6-step employee offboarding flow: `offboarding_records` (documented but never built), offboarding-only bypass/penalty/category fields on the existing checklist-template/task entities, a new bypass-approval table, employee-checklist-task CRUD/complete/bypass endpoints, and offboarding completion (employment status, session revocation, user deactivation, read-only lock).

**Architecture:** New `OffboardingRecord`/`OffboardingTaskBypassRequest` entities under `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/`, new repositories following the exact `EfChecklistTemplateRepository`-style pattern already in this codebase, three new thin controllers (`EmployeeOffboardingController`, `EmployeeChecklistTasksController`, `OffboardingBypassRequestsController`) under `Api/Controllers/Tenant/CoreHr/`, all mutations via MediatR commands returning `Result<T>`/`Result`. Extends (does not replace) the existing generic `checklist_templates`/`employee_checklist_tasks` entities and `ChecklistTaskJsonContract`.

**Tech Stack:** ASP.NET Core 8 Web API, EF Core (Npgsql, snake_case, Postgres RLS), MediatR CQRS, FluentValidation, xUnit + FluentAssertions + Moq (unit, EF InMemory), Testcontainers.PostgreSQL (integration).

## Global Constraints

- Work only in `C:\onevoNew\HRMS-Backend-v1`. Do not touch the frontend repo. Do not commit or push beyond staging files per-task (the executor stages and commits each task's own files; leave any final push to the user).
- `tenantId` is never accepted from a request body or query string — always `ICurrentUser.TenantId`.
- Every controller action carries `[RequirePermission("employees:read")]`, `[RequirePermission("employees:write")]`, or (Task 19+) `[RequirePermission("employees:offboard")]` on the four offboarding-record-lifecycle actions specifically. `employees:offboard` is a new permission code (Task 19) — the four lifecycle actions additionally require passing `IEmployeeOffboardingCoverageGuard.EnsureCovered` (Task 19), per the user's 2026-08-18 coverage requirement; permission and coverage are independent checks (see backend design spec §11).
- Controllers inject `IMediator` only.
- `Result<T>`/`Result` (`src/ONEVO.Application/Common/Models/Result.cs`) is the only handler return shape; controllers convert with `result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400)`, matching `EmployeesController` exactly.
- Migrations live flat under `src/ONEVO.Infrastructure/Migrations/`, named `{yyyyMMddHHmmss}_{PascalCaseDescription}.cs`, generated via `dotnet ef migrations add` (never hand-write `CreateTable`/`AlterTable` calls) — the tool's own timestamp is fine, no manual renaming required as long as it sorts after `20260817104921_AddAccessGrantRequestXminConcurrencyToken.cs`.
- No hard delete anywhere in this feature — cancellation/rejection are status transitions, not row deletion.
- A brand-new tenant-owned table's migration **must** declare a `private static readonly string[] TenantTables = [...]` array containing the exact new table name(s) and emit the literal text `CREATE POLICY tenant_isolation` via `migrationBuilder.Sql(...)` in `Up()` — `TenantIsolationArchitectureTests.FindTablesWithRlsPolicy()` regex-scans migration source for exactly this, and a table missing from it fails `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage`. Column-only migrations on already-covered tables need no such entry.
- Do not build a notifications/inbox subsystem, an `employee_lifecycle_events` table, computed payroll amounts, file-evidence upload on tasks, or a new checklist-template-authoring UI — all explicitly out of scope per the design spec's §2 non-goals (`docs/superpowers/specs/next/2026-08-17-employee-offboarding-execution-backend-design.md`).

---

### Task 1: Employment status lookups — `offboarding` and `resigned`

**Files:**
- Modify: `src/ONEVO.Domain/Lookups/EmploymentStatusIds.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20260817120000_AddOffboardingAndResignedEmploymentStatuses.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmploymentStatusIdsTests.cs`

**Interfaces:**
- Produces: `EmploymentStatusIds.Offboarding = 5`, `EmploymentStatusIds.Resigned = 6` — every later task that mutates `Employee.EmploymentStatusId` uses these exact constants, never magic numbers.

**Important:** `LookupDataSeeder.SeedLookupAsync<T>` (the private helper backing every `Seed*` call) skips seeding an entire table if `dbSet.AnyAsync(ct)` is already true — it is not idempotent per-row. Since `employment_statuses` already has 4 rows in every real environment, simply adding two entries to the `EmploymentStatuses()` array will **never** actually insert them anywhere except a brand-new empty database. The migration's `InsertData` call is what actually delivers the two new rows everywhere; the seeder array update is for documentation/fresh-database consistency only, not the real delivery mechanism.

- [ ] **Step 1: Add the named constants**

Replace the full contents of `EmploymentStatusIds.cs`:

```csharp
namespace ONEVO.Domain.Lookups;

/// <summary>Fixed global lookup, seeded by LookupDataSeeder (Id=1 "active", Id=4 "terminated")
/// and backfilled for existing databases by migration 20260817120000 (Id=5 "offboarding",
/// Id=6 "resigned" — LookupDataSeeder's per-table AnyAsync guard means array-only additions
/// never reach an already-seeded database, so the migration's InsertData is the real delivery
/// mechanism for those two rows). Same shape/seeding mechanism as VersionStatusIds
/// (src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs).</summary>
public static class EmploymentStatusIds
{
    public const int Active = 1;
    public const int OnLeave = 2;
    public const int Suspended = 3;
    public const int Terminated = 4;
    public const int Offboarding = 5;
    public const int Resigned = 6;
}
```

- [ ] **Step 2: Update the seeder array (fresh-database consistency)**

In `LookupDataSeeder.cs`, replace:

```csharp
    private static EmploymentStatus[] EmploymentStatuses() =>
    [
        new() { Id = 1, Code = "active",     Label = "Active"     },
        new() { Id = 2, Code = "on_leave",   Label = "On Leave"   },
        new() { Id = 3, Code = "suspended",  Label = "Suspended"  },
        new() { Id = 4, Code = "terminated", Label = "Terminated" },
    ];
```

with:

```csharp
    private static EmploymentStatus[] EmploymentStatuses() =>
    [
        new() { Id = 1, Code = "active",      Label = "Active"      },
        new() { Id = 2, Code = "on_leave",    Label = "On Leave"    },
        new() { Id = 3, Code = "suspended",   Label = "Suspended"   },
        new() { Id = 4, Code = "terminated",  Label = "Terminated"  },
        new() { Id = 5, Code = "offboarding", Label = "Offboarding" },
        new() { Id = 6, Code = "resigned",    Label = "Resigned"    },
    ];
```

- [ ] **Step 3: Generate the backfill migration**

Run:
```bash
cd C:\onevoNew\HRMS-Backend-v1 && dotnet ef migrations add AddOffboardingAndResignedEmploymentStatuses --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```

This generates an empty-`Up()`/`Down()` migration (there's no model change, only data) — replace its `Up()`/`Down()` bodies by hand with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.InsertData(
        table: "employment_statuses",
        columns: ["id", "code", "label"],
        values: new object[,]
        {
            { 5, "offboarding", "Offboarding" },
            { 6, "resigned", "Resigned" },
        });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DeleteData(table: "employment_statuses", keyColumn: "id", keyValues: new object[] { 5, 6 });
}
```

Rename the generated file to `20260817120000_AddOffboardingAndResignedEmploymentStatuses.cs` if the tool picked a different timestamp (must sort after `20260817104921_...`). Confirm the regenerated `ApplicationDbContextModelSnapshot.cs` has no unexpected diffs (there should be none — this migration has no `CreateTable`/`AlterTable`, only `InsertData`).

- [ ] **Step 4: Write and run the constants test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmploymentStatusIdsTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmploymentStatusIdsTests
{
    [Fact]
    public void OffboardingAndResigned_AreDistinctFromExistingStatuses()
    {
        var ids = new[]
        {
            EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave, EmploymentStatusIds.Suspended,
            EmploymentStatusIds.Terminated, EmploymentStatusIds.Offboarding, EmploymentStatusIds.Resigned,
        };
        ids.Should().OnlyHaveUniqueItems();
        EmploymentStatusIds.Offboarding.Should().Be(5);
        EmploymentStatusIds.Resigned.Should().Be(6);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EmploymentStatusIdsTests`
Expected: PASS (this is a values-only test, no infrastructure needed — it will pass immediately, but writing it first documents intent per TDD and catches accidental renumbering later).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Lookups/EmploymentStatusIds.cs src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmploymentStatusIdsTests.cs
git commit -m "feat: add offboarding and resigned employment status lookup rows"
```

---

### Task 2: `OffboardingRecord` entity, EF config, and repository

**Files:**
- Create: `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingRecord.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/OffboardingRecordConfiguration.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingRecordRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingRecordRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingRecordRepositoryTests.cs`

**Interfaces:**
- Produces: `OffboardingRecord` entity with properties exactly as listed in Step 1 below; `OffboardingRecordStatuses.Initiated/InProgress/Completed/Cancelled` constants; `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync/GetTrackedByIdAsync/GetLatestByEmployeeIdAsync/AddAsync/SaveChangesAsync` — Tasks 8-11 and 15 call these exact names. No migration yet (Task 4 creates the table alongside Task 3's entity, in one migration).

- [ ] **Step 1: Entity**

Create `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingRecord.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class OffboardingRecordStatuses
{
    public const string Initiated = "initiated";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>Tracks one employee's exit process end-to-end. See phase1-table-inventory.md
/// (Core HR, offboarding_records) for the documented baseline; RehireEligibility, Notes,
/// ChecklistTemplateId, InitiatedById, PreviousEmploymentStatusId, UpdatedAt, CompletedAt are
/// additions found missing during the 2026-08-17 offboarding-execution design (see
/// specs/next/2026-08-17-employee-offboarding-execution-backend-design.md §4.1).</summary>
public class OffboardingRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly LastWorkingDate { get; set; }
    public string KnowledgeRiskLevel { get; set; } = string.Empty;
    public string? RehireEligibility { get; set; }
    public string? Notes { get; set; }
    public Guid? ChecklistTemplateId { get; set; }
    public string? ExitInterviewNotes { get; set; }
    public string PenaltiesJson { get; set; } = "{}";
    public string Status { get; set; } = OffboardingRecordStatuses.Initiated;
    public Guid InitiatedById { get; set; }
    public int? PreviousEmploymentStatusId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

- [ ] **Step 2: EF configuration**

Create `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/OffboardingRecordConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Offboarding;

public sealed class OffboardingRecordConfiguration : IEntityTypeConfiguration<OffboardingRecord>
{
    public void Configure(EntityTypeBuilder<OffboardingRecord> builder)
    {
        builder.ToTable("offboarding_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(30).IsRequired();
        builder.Property(x => x.KnowledgeRiskLevel).HasMaxLength(10).IsRequired();
        builder.Property(x => x.RehireEligibility).HasMaxLength(20);
        builder.Property(x => x.PenaltiesJson).HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId })
            .HasFilter("status IN ('initiated','in_progress')")
            .IsUnique();
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.Employee>().WithMany()
            .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChecklistTemplate>().WithMany()
            .HasForeignKey(x => x.ChecklistTemplateId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Repository interface and implementation**

Create `src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingRecordRepository.cs`:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

public interface IOffboardingRecordRepository
{
    /// <summary>The one record with status initiated/in_progress for this employee, or null.
    /// Used to enforce "at most one open offboarding per employee" and to drive resume/read-only
    /// banners on the frontend.</summary>
    Task<OffboardingRecord?> GetOpenByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<OffboardingRecord?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Most recent record by CreatedAt regardless of status - used by GET .../offboarding
    /// so a just-completed record is still visible (not only "open" ones).</summary>
    Task<OffboardingRecord?> GetLatestByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task AddAsync(OffboardingRecord record, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

Create `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingRecordRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;

public sealed class EfOffboardingRecordRepository(ApplicationDbContext db) : IOffboardingRecordRepository
{
    public Task<OffboardingRecord?> GetOpenByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.OffboardingRecords.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId
            && (x.Status == OffboardingRecordStatuses.Initiated || x.Status == OffboardingRecordStatuses.InProgress), ct);

    public Task<OffboardingRecord?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.OffboardingRecords.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<OffboardingRecord?> GetLatestByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
        => db.OffboardingRecords.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    public Task AddAsync(OffboardingRecord record, CancellationToken ct = default)
        => db.OffboardingRecords.AddAsync(record, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Register the `DbSet` and DI**

In `ApplicationDbContext.cs`, near `public DbSet<AccessGrantRequest> AccessGrantRequests => Set<AccessGrantRequest>();` (line 210), add:

```csharp
    public DbSet<OffboardingRecord> OffboardingRecords => Set<OffboardingRecord>();
```

In `DependencyInjection.cs`, near `services.AddScoped<IEmployeeChecklistTaskRepository, EfEmployeeChecklistTaskRepository>();` (line 171), add:

```csharp
        services.AddScoped<IOffboardingRecordRepository, EfOffboardingRecordRepository>();
```

Add the `using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;` and `using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;` usings if not already covered by a wildcard.

- [ ] **Step 5: Build to confirm compilation**

Run: `cd C:\onevoNew\HRMS-Backend-v1 && dotnet build src/ONEVO.Domain src/ONEVO.Application src/ONEVO.Infrastructure`
Expected: succeeds. `ApplicationDbContext` will fail to build a real query against `offboarding_records` until Task 4's migration exists, but compilation itself does not require the table to exist yet.

- [ ] **Step 6: Repository unit test (EF InMemory)**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingRecordRepositoryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingRecordRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetOpenByEmployeeIdAsync_ReturnsInitiatedOrInProgress_NotCompleted()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.OffboardingRecords.AddRange(
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Completed },
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.InProgress });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingRecordRepository(db);
        var result = await repo.GetOpenByEmployeeIdAsync(tenantId, employeeId);

        result.Should().NotBeNull();
        result!.Status.Should().Be(OffboardingRecordStatuses.InProgress);
    }

    [Fact]
    public async Task GetLatestByEmployeeIdAsync_ReturnsMostRecentByCreatedAt_EvenIfCompleted()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.OffboardingRecords.AddRange(
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Cancelled, CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) },
            new OffboardingRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Status = OffboardingRecordStatuses.Completed, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingRecordRepository(db);
        var result = await repo.GetLatestByEmployeeIdAsync(tenantId, employeeId);

        result!.Status.Should().Be(OffboardingRecordStatuses.Completed);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OffboardingRecordRepositoryTests`
Expected: PASS (EF InMemory doesn't enforce the partial-unique-index constraint, but exercises the query logic).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Offboarding/ src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/ src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/ src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/ src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingRecordRepositoryTests.cs
git commit -m "feat: add OffboardingRecord entity, config, and repository"
```

---

### Task 3: `OffboardingTaskBypassRequest` entity, EF config, and repository

**Files:**
- Create: `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingTaskBypassRequest.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/OffboardingTaskBypassRequestConfiguration.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingTaskBypassRequestRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingTaskBypassRequestRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingTaskBypassRequestRepositoryTests.cs`

**Interfaces:**
- Produces: `OffboardingTaskBypassRequest` entity, `BypassRequestStatuses.Pending/Approved/Rejected/Cancelled`, `IOffboardingTaskBypassRequestRepository.GetTrackedByIdAsync/HasPendingForTaskAsync/ListPendingByApproverAsync/AddAsync/SaveChangesAsync` — Tasks 13-14 call these exact names.

- [ ] **Step 1: Entity**

Create `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingTaskBypassRequest.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class BypassRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>Mirrors task_approvals' shape (single named approver, one pending request per
/// subject row) per the 2026-08-17 offboarding-execution design's explicit instruction to follow
/// that pattern without touching Work Management tables. No notification-row FK - the
/// notifications table doesn't exist anywhere in this codebase (see design spec §2).</summary>
public class OffboardingTaskBypassRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeChecklistTaskId { get; set; }
    public Guid OffboardingRecordId { get; set; }
    public Guid RequestedById { get; set; }
    public Guid ApproverId { get; set; }
    public string BypassReason { get; set; } = string.Empty;
    public string? PenaltyDescription { get; set; }
    /// <summary>The task's Status at the moment this request was created (pending/in_progress) -
    /// restored onto the task by RejectBypassRequestCommandHandler (Task 15) so rejection returns
    /// the task to exactly where it was, not an assumed default.</summary>
    public string PriorTaskStatus { get; set; } = string.Empty;
    public string Status { get; set; } = BypassRequestStatuses.Pending;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
}
```

- [ ] **Step 2: EF configuration**

Create `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/OffboardingTaskBypassRequestConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.Offboarding;

public sealed class OffboardingTaskBypassRequestConfiguration : IEntityTypeConfiguration<OffboardingTaskBypassRequest>
{
    public void Configure(EntityTypeBuilder<OffboardingTaskBypassRequest> builder)
    {
        builder.ToTable("offboarding_task_bypass_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BypassReason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PenaltyDescription).HasMaxLength(500);
        builder.Property(x => x.PriorTaskStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionComment).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.ApproverId, x.Status });
        builder.HasIndex(x => x.EmployeeChecklistTaskId)
            .HasFilter("status = 'pending'")
            .IsUnique();
        builder.HasOne<EmployeeChecklistTask>().WithMany()
            .HasForeignKey(x => x.EmployeeChecklistTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OffboardingRecord>().WithMany()
            .HasForeignKey(x => x.OffboardingRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Repository interface and implementation**

Create `src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingTaskBypassRequestRepository.cs`:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

public interface IOffboardingTaskBypassRequestRepository
{
    Task<OffboardingTaskBypassRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<bool> HasPendingForTaskAsync(Guid tenantId, Guid employeeChecklistTaskId, CancellationToken ct = default);
    Task<IReadOnlyList<OffboardingTaskBypassRequest>> ListPendingByApproverAsync(Guid tenantId, Guid approverId, CancellationToken ct = default);
    Task AddAsync(OffboardingTaskBypassRequest request, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

Create `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingTaskBypassRequestRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;

public sealed class EfOffboardingTaskBypassRequestRepository(ApplicationDbContext db) : IOffboardingTaskBypassRequestRepository
{
    public Task<OffboardingTaskBypassRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);

    public Task<bool> HasPendingForTaskAsync(Guid tenantId, Guid employeeChecklistTaskId, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AnyAsync(x =>
            x.TenantId == tenantId && x.EmployeeChecklistTaskId == employeeChecklistTaskId && x.Status == BypassRequestStatuses.Pending, ct);

    public Task<IReadOnlyList<OffboardingTaskBypassRequest>> ListPendingByApproverAsync(Guid tenantId, Guid approverId, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ApproverId == approverId && x.Status == BypassRequestStatuses.Pending)
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<OffboardingTaskBypassRequest>)t.Result, ct);

    public Task AddAsync(OffboardingTaskBypassRequest request, CancellationToken ct = default)
        => db.OffboardingTaskBypassRequests.AddAsync(request, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Register the `DbSet` and DI**

In `ApplicationDbContext.cs`, add next to `OffboardingRecords`:

```csharp
    public DbSet<OffboardingTaskBypassRequest> OffboardingTaskBypassRequests => Set<OffboardingTaskBypassRequest>();
```

In `DependencyInjection.cs`, add next to the `IOffboardingRecordRepository` registration:

```csharp
        services.AddScoped<IOffboardingTaskBypassRequestRepository, EfOffboardingTaskBypassRequestRepository>();
```

- [ ] **Step 5: Build**

Run: `dotnet build src/ONEVO.Domain src/ONEVO.Application src/ONEVO.Infrastructure`
Expected: succeeds.

- [ ] **Step 6: Repository unit test (EF InMemory)**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingTaskBypassRequestRepositoryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingTaskBypassRequestRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task HasPendingForTaskAsync_TrueOnlyForPendingStatus()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        db.OffboardingTaskBypassRequests.Add(new OffboardingTaskBypassRequest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = taskId,
            OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = Guid.NewGuid(),
            BypassReason = "x", Status = BypassRequestStatuses.Rejected,
        });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingTaskBypassRequestRepository(db);
        (await repo.HasPendingForTaskAsync(tenantId, taskId)).Should().BeFalse();
    }

    [Fact]
    public async Task ListPendingByApproverAsync_ScopesToApproverAndPendingOnly()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        db.OffboardingTaskBypassRequests.AddRange(
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = approverId, BypassReason = "a", Status = BypassRequestStatuses.Pending },
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = approverId, BypassReason = "b", Status = BypassRequestStatuses.Approved },
            new OffboardingTaskBypassRequest { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeChecklistTaskId = Guid.NewGuid(), OffboardingRecordId = Guid.NewGuid(), RequestedById = Guid.NewGuid(), ApproverId = Guid.NewGuid(), BypassReason = "c", Status = BypassRequestStatuses.Pending });
        await db.SaveChangesAsync();

        var repo = new EfOffboardingTaskBypassRequestRepository(db);
        var result = await repo.ListPendingByApproverAsync(tenantId, approverId);

        result.Should().ContainSingle().Which.BypassReason.Should().Be("a");
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OffboardingTaskBypassRequestRepositoryTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/OffboardingTaskBypassRequest.cs src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Offboarding/OffboardingTaskBypassRequestConfiguration.cs src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingTaskBypassRequestRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingTaskBypassRequestRepository.cs src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingTaskBypassRequestRepositoryTests.cs
git commit -m "feat: add OffboardingTaskBypassRequest entity, config, and repository"
```

---

### Task 4: Migration creating both new tables with RLS

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/20260817130000_AddOffboardingExecutionTables.cs`
- Modify: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (regenerated by tooling)

**Interfaces:**
- Consumes: `OffboardingRecord`/`OffboardingTaskBypassRequest` entities and configs from Tasks 2-3.
- Produces: the real `offboarding_records` and `offboarding_task_bypass_requests` tables that Tasks 5-16 read/write.

- [ ] **Step 1: Generate the migration**

Run:
```bash
cd C:\onevoNew\HRMS-Backend-v1 && dotnet ef migrations add AddOffboardingExecutionTables --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```

Rename to `20260817130000_AddOffboardingExecutionTables.cs` if needed (must sort after Task 1's `20260817120000_...`). Verify `Up()` contains two `CreateTable` calls (`offboarding_records`, `offboarding_task_bypass_requests`), their FKs, and the two partial-unique indexes from Tasks 2-3's configurations.

- [ ] **Step 2: Add the RLS policy block by hand**

Following the exact `AddEmployeeProfileChildTables` pattern, add at the top of the generated class (as a `private static readonly` field) and at the end of `Up()`:

```csharp
private static readonly string[] TenantTables = ["offboarding_records", "offboarding_task_bypass_requests"];
```

At the end of `Up()`, after the `CreateTable`/`CreateIndex` calls:

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

At the top of `Down()`, before the `DropTable` calls:

```csharp
foreach (var table in TenantTables)
{
    migrationBuilder.Sql($@"
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
    ");
}
```

- [ ] **Step 3: Build and confirm no RLS-disable regression**

Run: `dotnet build src/ONEVO.Infrastructure`
Confirm the migration's `Up()` contains no `DISABLE ROW LEVEL SECURITY`/`DROP POLICY` calls (those belong only in `Down()`) — `TenantIsolationArchitectureTests.Migrations_NeverDisableRowLevelSecurity_InTheUpDirection` enforces this.

- [ ] **Step 4: Apply locally if a dev database is configured**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
If no local Postgres is reachable, skip — Task 17's Testcontainers integration tests exercise this migration against a real Postgres instead; note the skip in the final report.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/
git commit -m "feat: migrate offboarding_records and offboarding_task_bypass_requests tables with RLS"
```

---

### Task 5: Offboarding fields on `EmployeeChecklistTask` and the checklist-task JSON contract

**Files:**
- Modify: `src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Onboarding/EmployeeChecklistTaskConfiguration.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Models/ChecklistTaskContract.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/20260817140000_AddOffboardingFieldsToEmployeeChecklistTask.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ChecklistTaskJsonContractTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeChecklistTaskStatusesTests.cs`

**Interfaces:**
- Produces: `EmployeeChecklistTask.IsBypassable`/`BypassPenaltyDescription`/`Category`/`OffboardingRecordId`; `EmployeeChecklistTaskStatuses.Pending/InProgress/Completed/Bypassed`; `ChecklistTaskDefinition` gains three trailing optional-default parameters (`IsBypassable = false, BypassPenaltyDescription = null, Category = null`) so every existing 7-positional-argument test call site in `ChecklistTaskJsonContractTests.cs` keeps compiling unchanged — Task 6 and Task 11 (instantiation) consume these.
- **`OffboardingRecordId` (`Guid?`, nullable, null for onboarding tasks) scopes a task to one specific offboarding attempt.** Without it, a cancelled-and-restarted offboarding (Task 12) would leave two sets of `lifecycle_type='offboarding'` rows for the same employee with no way to tell which is current — the completion gate (Task 16) and task list (Task 13) would silently mix an abandoned attempt's tasks with the live one. `ChecklistTaskJsonContract.ToEmployeeChecklistTasks` deliberately does **not** set this (it has no concept of an offboarding record, only template/employee/lifecycle) — `SelectOffboardingChecklistCommandHandler` (Task 11) sets it on each returned task as a post-processing step before saving.

- [ ] **Step 1: Entity and status constants**

Replace the full contents of `EmployeeChecklistTask.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class EmployeeChecklistTaskStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    /// <summary>Approved-bypass terminal state - counts as "done" for the offboarding
    /// completion gate (see CompleteOffboardingCommandHandler, Task 15) but is distinct from
    /// Completed for audit and the Track Exit Work progress view.</summary>
    public const string Bypassed = "bypassed";
}

/// <summary>A checklist task instantiated for one employee. IsBypassable/BypassPenaltyDescription/
/// Category are offboarding-only fields (default false/null/null) copied from the owning
/// template's task definition at instantiation - see design spec §4.2.</summary>
public class EmployeeChecklistTask : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? TemplateId { get; set; }
    public string LifecycleType { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int? Sequence { get; set; }
    public Guid AssignedToId { get; set; }
    public DateOnly DueDate { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsBypassable { get; set; } = false;
    public string? BypassPenaltyDescription { get; set; }
    public string? Category { get; set; }
    public Guid? OffboardingRecordId { get; set; }
    public string Status { get; set; } = EmployeeChecklistTaskStatuses.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
}
```

- [ ] **Step 2: EF configuration**

In `EmployeeChecklistTaskConfiguration.cs`, add inside `Configure(...)` (after the existing `IsRequired` line):

```csharp
        builder.Property(x => x.IsBypassable).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.BypassPenaltyDescription).HasMaxLength(500);
        builder.Property(x => x.Category).HasMaxLength(40);
        builder.HasIndex(x => new { x.TenantId, x.OffboardingRecordId });
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.OffboardingRecord>().WithMany()
            .HasForeignKey(x => x.OffboardingRecordId).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 3: Generate the column-only migration**

Run:
```bash
dotnet ef migrations add AddOffboardingFieldsToEmployeeChecklistTask --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```

Rename to `20260817140000_AddOffboardingFieldsToEmployeeChecklistTask.cs` if needed. This is a change on an already-RLS-covered table (`employee_checklist_tasks` already has `tenant_isolation` from its original migration) — verify the generated `Up()` contains four `AddColumn` calls (`is_bypassable`, `bypass_penalty_description`, `category`, `offboarding_record_id`), one `AddForeignKey` (to `offboarding_records` — this is why this migration's timestamp must sort after Task 4's, which creates that table), and one `CreateIndex`. No `TenantTables` array is needed (per the Global Constraints rule — `employee_checklist_tasks` is already covered, and adding a column/FK to an existing table doesn't touch its RLS policy).

- [ ] **Step 4: Extend `ChecklistTaskDefinition` and the contract's parse/serialize/instantiate methods**

In `ChecklistTaskContract.cs`, replace the `ChecklistTaskDefinition` record:

```csharp
public sealed record ChecklistTaskDefinition(
    string Title,
    string OwnerType,
    Guid? AssignedToId,
    int? DueOffsetDays,
    DateOnly? DueDate,
    int? Sequence,
    bool IsRequired,
    bool IsBypassable = false,
    string? BypassPenaltyDescription = null,
    string? Category = null);
```

In `ParseOne(...)`, after the existing `isRequired` extraction (`var isRequired = isRequiredEl.GetBoolean();`), add:

```csharp
        var isBypassable = item.TryGetProperty("isBypassable", out var isBypassableEl)
            && isBypassableEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            && isBypassableEl.GetBoolean();

        string? bypassPenaltyDescription = item.TryGetProperty("bypassPenaltyDescription", out var penaltyEl) && penaltyEl.ValueKind != JsonValueKind.Null
            ? penaltyEl.GetString()
            : null;

        string? category = item.TryGetProperty("category", out var categoryEl) && categoryEl.ValueKind != JsonValueKind.Null
            ? categoryEl.GetString()
            : null;
```

and change the final `return` statement to:

```csharp
        return new ChecklistTaskDefinition(
            title, ownerType, assignedToId, dueOffsetDays, dueDate, sequence, isRequired,
            isBypassable, bypassPenaltyDescription, category);
```

In `SerializeTemplateTasks(...)`, add the three fields to the anonymous projection:

```csharp
        var payload = tasks.Select(t => new
        {
            title = t.Title,
            ownerType = t.OwnerType,
            assignedToId = t.AssignedToId?.ToString(),
            dueOffsetDays = t.DueOffsetDays,
            sequence = t.Sequence,
            isRequired = t.IsRequired,
            isBypassable = t.IsBypassable,
            bypassPenaltyDescription = t.BypassPenaltyDescription,
            category = t.Category,
        });
```

In `ToEmployeeChecklistTasks(...)`, add the three fields to the constructed `EmployeeChecklistTask`:

```csharp
            IsBypassable = definition.IsBypassable,
            BypassPenaltyDescription = definition.BypassPenaltyDescription,
            Category = definition.Category,
```

(placed alongside the existing `IsRequired = definition.IsRequired,` line).

- [ ] **Step 5: Extend the contract tests**

In `ChecklistTaskJsonContractTests.cs`, add:

```csharp
    [Fact]
    public void Parse_OffboardingFields_DefaultToFalseAndNull_WhenAbsent()
    {
        var json = "[{\"title\":\"Return laptop\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result[0].IsBypassable.Should().BeFalse();
        result[0].BypassPenaltyDescription.Should().BeNull();
        result[0].Category.Should().BeNull();
    }

    [Fact]
    public void Parse_OffboardingFields_ParsesWhenPresent()
    {
        var json = "[{\"title\":\"Return laptop\",\"ownerType\":\"employee\",\"dueOffsetDays\":1,\"isRequired\":true," +
                    "\"isBypassable\":true,\"bypassPenaltyDescription\":\"Deduct from final settlement\",\"category\":\"asset_return\"}]";

        var result = ChecklistTaskJsonContract.Parse(json, ChecklistTaskDueRuleMode.OffsetDays);

        result[0].IsBypassable.Should().BeTrue();
        result[0].BypassPenaltyDescription.Should().Be("Deduct from final settlement");
        result[0].Category.Should().Be("asset_return");
    }

    [Fact]
    public void ToEmployeeChecklistTasks_CopiesOffboardingFieldsOntoTheInstantiatedTask()
    {
        var defs = new List<ChecklistTaskDefinition>
        {
            new("Return laptop", ChecklistTaskOwnerTypes.Employee, null, 1, null, 1, true, true, "None", "asset_return"),
        };

        var tasks = ChecklistTaskJsonContract.ToEmployeeChecklistTasks(
            defs, Guid.NewGuid(), Guid.NewGuid(), null, "offboarding", Guid.NewGuid(), new DateOnly(2026, 1, 1), ChecklistTaskDueRuleMode.OffsetDays);

        tasks[0].IsBypassable.Should().BeTrue();
        tasks[0].BypassPenaltyDescription.Should().Be("None");
        tasks[0].Category.Should().Be("asset_return");
    }
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ChecklistTaskJsonContractTests`
Expected: all pass, including the pre-existing tests (the new record parameters are trailing-optional, so `new("Submit ID", ChecklistTaskOwnerTypes.Employee, null, 2, null, 1, true)`-style 7-arg calls in the existing file still compile and still produce `IsBypassable=false`).

- [ ] **Step 6: Status constants test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeChecklistTaskStatusesTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeChecklistTaskStatusesTests
{
    [Fact]
    public void AllFourStatuses_AreDistinct()
    {
        new[]
        {
            EmployeeChecklistTaskStatuses.Pending, EmployeeChecklistTaskStatuses.InProgress,
            EmployeeChecklistTaskStatuses.Completed, EmployeeChecklistTaskStatuses.Bypassed,
        }.Should().OnlyHaveUniqueItems();
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ChecklistTaskJsonContractTests|FullyQualifiedName~EmployeeChecklistTaskStatusesTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/Onboarding/Entities/EmployeeChecklistTask.cs src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/Onboarding/EmployeeChecklistTaskConfiguration.cs src/ONEVO.Application/Features/CoreHr/Onboarding/Models/ChecklistTaskContract.cs src/ONEVO.Infrastructure/Migrations/ tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ChecklistTaskJsonContractTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeChecklistTaskStatusesTests.cs
git commit -m "feat: add offboarding bypass/penalty/category fields to EmployeeChecklistTask and the checklist task JSON contract"
```

---

### Task 6: Relax `InstantiateAsync` to accept offboarding templates

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs`

**Interfaces:**
- Consumes: `ChecklistTaskDefinition`'s offboarding fields from Task 5.
- Produces: `EfEmployeeChecklistTaskRepository.InstantiateAsync` accepts `template.TemplateType` of either `"onboarding"` or `"offboarding"` — Task 10 (Select Checklist) calls this with an offboarding template.

This is the single highest-risk change in the plan — it alters behavior on the existing onboarding finalization path, not just adds new behavior. It gets its own task with its own test cycle so it can be reviewed independently of Task 5's additive columns.

- [ ] **Step 1: Write the failing tests (offboarding now works, onboarding still works identically)**

In `OnboardingPersistenceRepositoryTests.cs`, find the existing test(s) exercising `EfEmployeeChecklistTaskRepository.InstantiateAsync` and add two more, following the same `ApplicationDbContext` InMemory setup pattern already used in that file:

```csharp
    [Fact]
    public async Task InstantiateAsync_OffboardingTemplate_Succeeds()
    {
        await using var db = CreateContext(); // reuse this file's existing context-creation helper
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var template = new ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Standard Offboarding",
            TemplateType = "offboarding", IsActive = true,
            TasksJson = "[{\"title\":\"Return laptop\",\"ownerType\":\"employee\",\"dueOffsetDays\":0,\"isRequired\":true,\"isBypassable\":true,\"bypassPenaltyDescription\":\"None\",\"category\":\"asset_return\"}]",
        };

        var repo = new EfEmployeeChecklistTaskRepository(db);
        var tasks = await repo.InstantiateAsync(template, employeeId, userId, editedTasksJson: null, anchorDate: new DateOnly(2026, 9, 1));

        tasks.Should().ContainSingle();
        tasks[0].LifecycleType.Should().Be("offboarding");
        tasks[0].IsBypassable.Should().BeTrue();
    }

    [Fact]
    public async Task InstantiateAsync_InactiveOffboardingTemplate_StillThrows()
    {
        await using var db = CreateContext();
        var template = new ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Inactive",
            TemplateType = "offboarding", IsActive = false, TasksJson = "[]",
        };

        var repo = new EfEmployeeChecklistTaskRepository(db);
        var act = async () => await repo.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 1, 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InstantiateAsync_OnboardingTemplate_StillSucceeds_RegressionCheck()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var template = new ChecklistTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Standard Onboarding",
            TemplateType = "onboarding", IsActive = true,
            TasksJson = "[{\"title\":\"Sign NDA\",\"ownerType\":\"employee\",\"dueOffsetDays\":0,\"isRequired\":true}]",
        };

        var repo = new EfEmployeeChecklistTaskRepository(db);
        var tasks = await repo.InstantiateAsync(template, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 1, 1));

        tasks.Should().ContainSingle();
        tasks[0].LifecycleType.Should().Be("onboarding");
        tasks[0].IsBypassable.Should().BeFalse();
    }
```

- [ ] **Step 2: Run to verify the new offboarding test fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~InstantiateAsync_OffboardingTemplate_Succeeds`
Expected: FAIL with `ArgumentException: Only active onboarding templates can be instantiated.`

- [ ] **Step 3: Relax the guard**

In `EfOnboardingPersistenceRepositories.cs`, in `EfEmployeeChecklistTaskRepository.InstantiateAsync`, replace:

```csharp
        if (!template.IsActive || template.TemplateType != "onboarding")
            throw new ArgumentException("Only active onboarding templates can be instantiated.", nameof(template));
```

with:

```csharp
        if (!template.IsActive || (template.TemplateType != "onboarding" && template.TemplateType != "offboarding"))
            throw new ArgumentException("Only active onboarding or offboarding templates can be instantiated.", nameof(template));
```

The rest of the method body is unchanged — `ChecklistTaskJsonContract.ToEmployeeChecklistTasks` already receives `template.TemplateType` as its `lifecycleType` argument, so an offboarding template correctly produces `LifecycleType = "offboarding"` tasks with no further change.

- [ ] **Step 4: Run all three tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~InstantiateAsync`
Expected: all three PASS (offboarding succeeds, inactive still throws, onboarding regression check still succeeds).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingPersistenceRepositoryTests.cs
git commit -m "feat: allow InstantiateAsync to instantiate offboarding templates alongside onboarding"
```

---

### Task 7: Offboarding checklist template matching

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingChecklistTemplateMatchTests.cs`

**Interfaces:**
- Produces: `IChecklistTemplateRepository.ListOffboardingMatchesAsync(tenantId, legalEntityId, departmentId, positionId, ct) -> Task<IReadOnlyList<ChecklistTemplateMatch>>` — Task 9 (Choose Exit Checklist step) calls this exact signature.

A sibling method is added rather than generalizing `ListOnboardingMatchesAsync` in place, to avoid touching the onboarding call site at all (keeps this task's blast radius to zero for existing onboarding code, matching Task 6's separately-reviewable-risk principle).

- [ ] **Step 1: Add the interface method**

In `IOnboardingPersistenceRepositories.cs`, in `IChecklistTemplateRepository`, add alongside `ListOnboardingMatchesAsync`:

```csharp
    /// <summary>Same match-level ordering as ListOnboardingMatchesAsync (position, then
    /// department, then company/default) but for active offboarding templates.</summary>
    Task<IReadOnlyList<ChecklistTemplateMatch>> ListOffboardingMatchesAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it**

In `EfChecklistTemplateRepository`, add, right after `ListOnboardingMatchesAsync`:

```csharp
    public async Task<IReadOnlyList<ChecklistTemplateMatch>> ListOffboardingMatchesAsync(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, Guid? positionId, CancellationToken ct = default)
    {
        var candidates = await db.ChecklistTemplates.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.TemplateType == "offboarding" && x.LegalEntityId == legalEntityId
                && (
                    (positionId != null && x.PositionId == positionId)
                    || (departmentId != null && x.PositionId == null && x.DepartmentId == departmentId)
                    || (x.PositionId == null && x.DepartmentId == null)
                ))
            .ToListAsync(ct);

        return candidates
            .Select(t => new ChecklistTemplateMatch(t, t.PositionId is not null
                ? ChecklistTemplateMatchLevels.Position
                : t.DepartmentId is not null ? ChecklistTemplateMatchLevels.Department : ChecklistTemplateMatchLevels.Company))
            .OrderBy(m => m.MatchLevel switch
            {
                ChecklistTemplateMatchLevels.Position => 0,
                ChecklistTemplateMatchLevels.Department => 1,
                _ => 2,
            })
            .ThenBy(m => m.Template.Name)
            .ToList();
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Application src/ONEVO.Infrastructure`
Expected: succeeds (any other class implementing `IChecklistTemplateRepository`, e.g. a test double, must add this method too — grep for `: IChecklistTemplateRepository` to check for others; only `EfChecklistTemplateRepository` is expected).

- [ ] **Step 4: Test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingChecklistTemplateMatchTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingChecklistTemplateMatchTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ListOffboardingMatchesAsync_PrefersPositionMatch_ExcludesOnboardingTemplates()
    {
        await using var db = CreateContext();
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        db.ChecklistTemplates.AddRange(
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Company Default", TemplateType = "offboarding", LegalEntityId = legalEntityId, IsActive = true, TasksJson = "[]" },
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "High-Risk Exit", TemplateType = "offboarding", LegalEntityId = legalEntityId, PositionId = positionId, IsActive = true, TasksJson = "[]" },
            new ChecklistTemplate { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Onboarding Default", TemplateType = "onboarding", LegalEntityId = legalEntityId, IsActive = true, TasksJson = "[]" });
        await db.SaveChangesAsync();

        var repo = new EfChecklistTemplateRepository(db);
        var matches = await repo.ListOffboardingMatchesAsync(tenantId, legalEntityId, departmentId: null, positionId: positionId);

        matches.Should().HaveCount(2);
        matches[0].Template.Name.Should().Be("High-Risk Exit");
        matches[0].MatchLevel.Should().Be(ChecklistTemplateMatchLevels.Position);
        matches.Should().NotContain(m => m.Template.Name == "Onboarding Default");
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OffboardingChecklistTemplateMatchTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingChecklistTemplateMatchTests.cs
git commit -m "feat: add offboarding checklist template matching"
```

---

### Task 8: Bulk session revocation

**Files:**
- Modify: `src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/ISessionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/SessionRevocationTests.cs`

**Interfaces:**
- Produces: `ISessionRepository.RevokeAllActiveByUserIdAsync(Guid userId, CancellationToken ct = default) -> Task<int>` (returns the count revoked) — Task 15 (Complete Employee Exit) calls this.

- [ ] **Step 1: Add the interface method**

In `ISessionRepository.cs`, add:

```csharp
    /// <summary>Revokes every non-revoked session for a user in one bulk update. Returns the
    /// number of sessions revoked. Used by offboarding completion - see design spec §5.5.</summary>
    Task<int> RevokeAllActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `EfAuthRepository`**

Add, near the existing `ISessionRepository.RevokeByIdAsync`/`RevokeByKeyHashAsync` explicit implementations:

```csharp
    async Task<int> ISessionRepository.RevokeAllActiveByUserIdAsync(Guid userId, CancellationToken ct)
        => await _db.Sessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsRevoked, true), ct);
```

(`ExecuteUpdateAsync` commits immediately and bypasses the "caller must call `IUnitOfWork.SaveChangesAsync`" convention the two single-session `RevokeBy*` methods rely on — that's intentional here: a bulk revoke has no other tracked-entity changes it needs to land atomically with.)

- [ ] **Step 3: Build**

Run: `dotnet build src/ONEVO.Application src/ONEVO.Infrastructure`

- [ ] **Step 4: Test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/SessionRevocationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class SessionRevocationTests
{
    [Fact]
    public async Task RevokeAllActiveByUserIdAsync_RevokesOnlyThatUsersActiveSessions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        db.Sessions.AddRange(
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsRevoked = false, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsRevoked = true, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new Session { Id = Guid.NewGuid(), TenantId = tenantId, UserId = otherUserId, IsRevoked = false, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();

        ISessionRepository repo = new EfAuthRepository(db);
        var count = await repo.RevokeAllActiveByUserIdAsync(userId);

        count.Should().Be(1);
        (await db.Sessions.Where(s => s.UserId == userId).AllAsync(s => s.IsRevoked)).Should().BeTrue();
        (await db.Sessions.Where(s => s.UserId == otherUserId).AnyAsync(s => !s.IsRevoked)).Should().BeTrue();
    }
}
```

**Note:** `ExecuteUpdateAsync` is not supported by the EF InMemory provider. If this test fails with a `NotSupportedException` referencing `ExecuteUpdate`, replace the InMemory setup with the same Testcontainers.PostgreSQL pattern used in `ChecklistTemplatesIntegrationTests.cs` (§ referenced in Task 17) rather than working around it with a non-bulk loop — the whole point of this method is the bulk `UPDATE`. Move this test to `tests/ONEVO.Tests.Integration/CoreHr/Offboarding/` in that case and confirm during Step 5 which one was needed.

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SessionRevocationTests`
Expected: PASS, or confirms the InMemory limitation described above.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/ISessionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Auth/Login/EfAuthRepository.cs tests/
git commit -m "feat: add bulk session revocation for offboarding completion"
```

---

### Task 9: Start Offboarding (`POST /api/v1/employees/{id}/offboarding`)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommandValidator.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Offboarding/StartOffboardingRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/StartOffboardingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeRepository.GetTrackedByIdAsync` (CoreHr.Employee namespace), `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync/AddAsync/SaveChangesAsync` (Task 2), `ICurrentUser`, `IDateTimeProvider`.
- Produces: `StartOffboardingCommand(Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel, string? RehireEligibility, string? Notes) : IRequest<Result<Guid>>` (returns the new `OffboardingRecord.Id`) — Task 10-16's controller actions and the frontend plan's Step 1 form both target this exact request shape.

- [ ] **Step 1: Command, validator, and contract**

Create `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public sealed record StartOffboardingCommand(
    Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel,
    string? RehireEligibility, string? Notes) : IRequest<Result<Guid>>;
```

Create `StartOffboardingCommandValidator.cs`:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public sealed class StartOffboardingCommandValidator : AbstractValidator<StartOffboardingCommand>
{
    private static readonly string[] ValidReasons = ["resignation", "termination", "retirement", "contract_end"];
    private static readonly string[] ValidRiskLevels = ["low", "medium", "high", "critical"];
    private static readonly string[] ValidRehireEligibility = ["eligible", "not_eligible", "conditional"];

    public StartOffboardingCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Reason).Must(r => ValidReasons.Contains(r))
            .WithMessage($"Reason must be one of: {string.Join(", ", ValidReasons)}.");
        RuleFor(x => x.KnowledgeRiskLevel).Must(r => ValidRiskLevels.Contains(r))
            .WithMessage($"Knowledge risk level must be one of: {string.Join(", ", ValidRiskLevels)}.");
        RuleFor(x => x.RehireEligibility)
            .Must(r => r is null || ValidRehireEligibility.Contains(r))
            .WithMessage($"Rehire eligibility must be one of: {string.Join(", ", ValidRehireEligibility)}.");
        RuleFor(x => x.Notes).MaximumLength(4000);
    }
}
```

Create `src/ONEVO.Api/Contracts/CoreHr/Offboarding/StartOffboardingRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record StartOffboardingRequest(
    string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel, string? RehireEligibility, string? Notes);
```

- [ ] **Step 2: Handler**

Create `StartOffboardingCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public class StartOffboardingCommandHandler(
    IEmployeeRepository employeeRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<StartOffboardingCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(StartOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("The employee could not be found.");

        if (employee.UserId == currentUser.UserId)
            return Result<Guid>.Forbidden("You cannot start offboarding on your own record.");

        var existingOpen = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, employee.Id, ct);
        if (existingOpen is not null)
            return Result<Guid>.Conflict("This employee already has an offboarding in progress.");

        var record = new OffboardingRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Reason = request.Reason,
            LastWorkingDate = request.LastWorkingDate,
            KnowledgeRiskLevel = request.KnowledgeRiskLevel,
            RehireEligibility = request.RehireEligibility,
            Notes = request.Notes,
            Status = OffboardingRecordStatuses.Initiated,
            InitiatedById = currentUser.UserId,
            PreviousEmploymentStatusId = employee.EmploymentStatusId,
            CreatedAt = clock.UtcNow,
        };
        await offboardingRecordRepository.AddAsync(record, ct);

        employee.EmploymentStatusId = ONEVO.Domain.Lookups.EmploymentStatusIds.Offboarding;

        await offboardingRecordRepository.SaveChangesAsync(ct);

        return Result<Guid>.Success(record.Id);
    }
}
```

(Both the new `OffboardingRecord` insert and the tracked `Employee.EmploymentStatusId` change land in the same `SaveChangesAsync` call since they're on the same `DbContext` via `ApplicationDbContext` — no explicit `ExecuteInTransactionAsync` is needed here, unlike `ChangeEmployeePositionCommandHandler`, because there's no raw-SQL write involved, only two tracked-entity changes in one `SaveChanges`.)

- [ ] **Step 3: Controller**

Create `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/{employeeId:guid}/offboarding")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeeOffboardingController(IMediator mediator) : ControllerBase
{
    /// <summary>Step 1 - start an employee's offboarding. Fails 409 if one is already open, 403 if
    /// the caller lacks employees:offboard or doesn't cover this employee (Task 19).</summary>
    [HttpPost]
    [RequirePermission("employees:offboard")]
    [Idempotent]
    public async Task<IActionResult> Start(Guid employeeId, [FromBody] StartOffboardingRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new StartOffboardingCommand(employeeId, request.Reason, request.LastWorkingDate, request.KnowledgeRiskLevel, request.RehireEligibility, request.Notes), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Start), new { employeeId }, new { offboardingRecordId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 4: Handler unit test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/StartOffboardingCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class StartOffboardingCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _actingUserId = Guid.NewGuid();

    public StartOffboardingCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _currentUser.Setup(c => c.UserId).Returns(_actingUserId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private StartOffboardingCommandHandler CreateSut() =>
        new(_employeeRepository.Object, _offboardingRecordRepository.Object, _currentUser.Object, _clock.Object);

    private StartOffboardingCommand CreateCommand() =>
        new(_employeeId, "resignation", new DateOnly(2026, 12, 1), "medium", "eligible", "Notice period completed.");

    [Fact]
    public async Task Handle_EmployeeNotFound_ReturnsNotFound()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_SelfOffboarding_ReturnsForbidden()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, UserId = _actingUserId });

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AlreadyHasOpenOffboarding_ReturnsConflict()
    {
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, UserId = Guid.NewGuid() });
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress });

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesRecordAndSetsOffboardingStatus()
    {
        var employee = new EmployeeEntity { Id = _employeeId, UserId = Guid.NewGuid(), EmploymentStatusId = EmploymentStatusIds.Active };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        OffboardingRecord? added = null;
        _offboardingRecordRepository.Setup(r => r.AddAsync(It.IsAny<OffboardingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<OffboardingRecord, CancellationToken>((r, _) => added = r)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().Handle(CreateCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Reason.Should().Be("resignation");
        added.PreviousEmploymentStatusId.Should().Be(EmploymentStatusIds.Active);
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Offboarding);
        _offboardingRecordRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~StartOffboardingCommandHandlerTests`
Expected: all pass.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build src/ONEVO.Api`
Expected: succeeds (confirms the controller/DI/routing wiring compiles).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/ src/ONEVO.Api/Contracts/CoreHr/Offboarding/StartOffboardingRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/StartOffboardingCommandHandlerTests.cs
git commit -m "feat: add Start Offboarding endpoint"
```

---

### Task 10: Get Offboarding + list offboarding checklist matches

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/GetOffboarding/GetOffboardingQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/GetOffboarding/GetOffboardingQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/GetOffboarding/OffboardingRecordResponse.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingChecklistMatches/ListOffboardingChecklistMatchesQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingChecklistMatches/ListOffboardingChecklistMatchesQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/GetOffboardingQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IOffboardingRecordRepository.GetLatestByEmployeeIdAsync` (Task 2), `IChecklistTemplateRepository.ListOffboardingMatchesAsync` (Task 7), `IPositionAssignmentRepository.GetActivePrimaryAsync(tenantId, employeeId, ct)` (existing, used by `ChangeEmployeePositionCommandHandler`, returns the active primary `PositionAssignment` with `.PositionId`).
- Produces: `OffboardingRecordResponse(Guid Id, Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel, string? RehireEligibility, string? Notes, Guid? ChecklistTemplateId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? CompletedAt)`; `GetOffboardingQuery(Guid EmployeeId) : IRequest<Result<OffboardingRecordResponse?>>` (Success with a `null` Value means "never offboarded", not an error); `ChecklistTemplateMatchResponse(Guid Id, string Name, string MatchLevel)`; `ListOffboardingChecklistMatchesQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<ChecklistTemplateMatchResponse>>>` — Task 11's frontend consumer and Task 16's read-only-banner logic rely on `OffboardingRecordResponse.Status`.

- [ ] **Step 1: Query records and response DTO**

Create `GetOffboardingQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public sealed record GetOffboardingQuery(Guid EmployeeId) : IRequest<Result<OffboardingRecordResponse?>>;
```

Create `OffboardingRecordResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public sealed record OffboardingRecordResponse(
    Guid Id, Guid EmployeeId, string Reason, DateOnly LastWorkingDate, string KnowledgeRiskLevel,
    string? RehireEligibility, string? Notes, Guid? ChecklistTemplateId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? CompletedAt);
```

- [ ] **Step 2: Handler**

Create `GetOffboardingQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public class GetOffboardingQueryHandler(IOffboardingRecordRepository repository, ICurrentUser currentUser)
    : IRequestHandler<GetOffboardingQuery, Result<OffboardingRecordResponse?>>
{
    public async Task<Result<OffboardingRecordResponse?>> Handle(GetOffboardingQuery request, CancellationToken ct)
    {
        var record = await repository.GetLatestByEmployeeIdAsync(currentUser.TenantId, request.EmployeeId, ct);
        if (record is null)
            return Result<OffboardingRecordResponse?>.Success(null);

        return Result<OffboardingRecordResponse?>.Success(new OffboardingRecordResponse(
            record.Id, record.EmployeeId, record.Reason, record.LastWorkingDate, record.KnowledgeRiskLevel,
            record.RehireEligibility, record.Notes, record.ChecklistTemplateId, record.Status,
            record.CreatedAt, record.UpdatedAt, record.CompletedAt));
    }
}
```

- [ ] **Step 3: Checklist-matches query and handler**

Create `ListOffboardingChecklistMatchesQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingChecklistMatches;

public sealed record ChecklistTemplateMatchResponse(Guid Id, string Name, string MatchLevel);

public sealed record ListOffboardingChecklistMatchesQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<ChecklistTemplateMatchResponse>>>;
```

Create `ListOffboardingChecklistMatchesQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingChecklistMatches;

public class ListOffboardingChecklistMatchesQueryHandler(
    IEmployeeRepository employeeRepository,
    IPositionAssignmentRepository positionAssignmentRepository,
    IChecklistTemplateRepository checklistTemplateRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListOffboardingChecklistMatchesQuery, Result<IReadOnlyList<ChecklistTemplateMatchResponse>>>
{
    public async Task<Result<IReadOnlyList<ChecklistTemplateMatchResponse>>> Handle(
        ListOffboardingChecklistMatchesQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employee = await employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.NotFound("The employee could not be found.");
        if (employee.LegalEntityId is not Guid legalEntityId)
            return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.UnprocessableEntity("This employee has no assigned legal entity.");

        var activeAssignment = await positionAssignmentRepository.GetActivePrimaryAsync(tenantId, employee.Id, ct);

        var matches = await checklistTemplateRepository.ListOffboardingMatchesAsync(
            tenantId, legalEntityId, employee.DepartmentId, activeAssignment?.PositionId, ct);

        return Result<IReadOnlyList<ChecklistTemplateMatchResponse>>.Success(
            matches.Select(m => new ChecklistTemplateMatchResponse(m.Template.Id, m.Template.Name, m.MatchLevel)).ToList());
    }
}
```

- [ ] **Step 4: Controller actions**

In `EmployeeOffboardingController.cs`, add (imports for the two new query namespaces):

```csharp
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> Get(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetOffboardingQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("checklist-matches")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetChecklistMatches(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListOffboardingChecklistMatchesQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 5: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/GetOffboardingQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class GetOffboardingQueryHandlerTests
{
    [Fact]
    public async Task Handle_NoRecordExists_ReturnsSuccessWithNullValue()
    {
        var repo = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repo.Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        var result = await new GetOffboardingQueryHandler(repo.Object, currentUser.Object)
            .Handle(new GetOffboardingQuery(employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RecordExists_MapsToResponse()
    {
        var repo = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repo.Setup(r => r.GetLatestByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord
            {
                Id = Guid.NewGuid(), EmployeeId = employeeId, Reason = "resignation",
                LastWorkingDate = new DateOnly(2026, 12, 1), KnowledgeRiskLevel = "low",
                Status = OffboardingRecordStatuses.InProgress,
            });

        var result = await new GetOffboardingQueryHandler(repo.Object, currentUser.Object)
            .Handle(new GetOffboardingQuery(employeeId), CancellationToken.None);

        result.Value!.Status.Should().Be(OffboardingRecordStatuses.InProgress);
        result.Value.Reason.Should().Be("resignation");
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~GetOffboardingQueryHandlerTests`
Expected: PASS. Also run `dotnet build src/ONEVO.Api` to confirm the controller wiring compiles.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/GetOffboardingQueryHandlerTests.cs
git commit -m "feat: add Get Offboarding and list offboarding checklist matches endpoints"
```

---

### Task 11: Select Checklist (instantiate tasks)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/SelectOffboardingChecklist/SelectOffboardingChecklistCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/SelectOffboardingChecklist/SelectOffboardingChecklistCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Offboarding/SelectOffboardingChecklistRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/SelectOffboardingChecklistCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync/SaveChangesAsync` (Task 2), `IChecklistTemplateRepository.GetByIdAsync` (existing), `EfEmployeeChecklistTaskRepository.InstantiateAsync` (Task 6, now offboarding-capable), `IEmployeeRepository.GetByIdAsync`.
- Produces: `SelectOffboardingChecklistCommand(Guid EmployeeId, Guid TemplateId) : IRequest<Result>` — sets every instantiated task's `OffboardingRecordId` (Task 5's new column) before saving, which Tasks 13/16 depend on for correct scoping.

- [ ] **Step 1: Command and contract**

Create `SelectOffboardingChecklistCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;

public sealed record SelectOffboardingChecklistCommand(Guid EmployeeId, Guid TemplateId) : IRequest<Result>;
```

Create `src/ONEVO.Api/Contracts/CoreHr/Offboarding/SelectOffboardingChecklistRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record SelectOffboardingChecklistRequest(Guid TemplateId);
```

- [ ] **Step 2: Handler**

Create `SelectOffboardingChecklistCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;

public class SelectOffboardingChecklistCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IChecklistTemplateRepository checklistTemplateRepository,
    IEmployeeChecklistTaskRepository employeeChecklistTaskRepository,
    IEmployeeRepository employeeRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<SelectOffboardingChecklistCommand, Result>
{
    public async Task<Result> Handle(SelectOffboardingChecklistCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");
        if (record.Status != OffboardingRecordStatuses.Initiated)
            return Result.Conflict("A checklist has already been selected for this offboarding.");

        var template = await checklistTemplateRepository.GetByIdAsync(tenantId, request.TemplateId, ct);
        if (template is null || !template.IsActive || template.TemplateType != "offboarding")
            return Result.NotFound("The selected checklist template does not exist or is not an active offboarding template.");

        var employee = await employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");
        if (template.LegalEntityId != employee.LegalEntityId)
            return Result.UnprocessableEntity("This template does not belong to the employee's company.");

        var tasks = await employeeChecklistTaskRepository.InstantiateAsync(
            template, employee.Id, employee.UserId, editedTasksJson: null, anchorDate: record.LastWorkingDate, ct);
        foreach (var task in tasks)
            task.OffboardingRecordId = record.Id;

        record.ChecklistTemplateId = template.Id;
        record.Status = OffboardingRecordStatuses.InProgress;
        record.UpdatedAt = clock.UtcNow;

        await offboardingRecordRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

(`InstantiateAsync` adds the new `EmployeeChecklistTask` rows to the same tracked `ApplicationDbContext` that `record` came from — both repositories share one scoped `DbContext` instance, so the single `SaveChangesAsync` call at the end commits the new tasks and the record's tracked changes together, same pattern as Task 9's `StartOffboardingCommandHandler`.)

- [ ] **Step 3: Controller action**

In `EmployeeOffboardingController.cs`, add:

```csharp
    [HttpPost("select-checklist")]
    [RequirePermission("employees:offboard")]
    [Idempotent]
    public async Task<IActionResult> SelectChecklist(Guid employeeId, [FromBody] SelectOffboardingChecklistRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new SelectOffboardingChecklistCommand(employeeId, request.TemplateId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/SelectOffboardingChecklistCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.SelectOffboardingChecklist;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class SelectOffboardingChecklistCommandHandlerTests
{
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<IChecklistTemplateRepository> _checklistTemplateRepository = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _employeeChecklistTaskRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _templateId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public SelectOffboardingChecklistCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private SelectOffboardingChecklistCommandHandler CreateSut() => new(
        _offboardingRecordRepository.Object, _checklistTemplateRepository.Object,
        _employeeChecklistTaskRepository.Object, _employeeRepository.Object, _currentUser.Object, _clock.Object);

    [Fact]
    public async Task Handle_NoOpenRecord_ReturnsNotFound()
    {
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OffboardingRecord?)null);

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChecklistAlreadySelected_ReturnsConflict()
    {
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress });

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_InstantiatesTasksAndAdvancesStatus()
    {
        var record = new OffboardingRecord { Id = Guid.NewGuid(), EmployeeId = _employeeId, Status = OffboardingRecordStatuses.Initiated, LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _checklistTemplateRepository.Setup(r => r.GetByIdAsync(_tenantId, _templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChecklistTemplate { Id = _templateId, TenantId = _tenantId, TemplateType = "offboarding", IsActive = true, LegalEntityId = _legalEntityId, TasksJson = "[]" });
        var employee = new EmployeeEntity { Id = _employeeId, LegalEntityId = _legalEntityId, UserId = Guid.NewGuid() };
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        var instantiatedTasks = new List<EmployeeChecklistTask> { new() { Id = Guid.NewGuid(), TenantId = _tenantId } };
        _employeeChecklistTaskRepository
            .Setup(r => r.InstantiateAsync(It.IsAny<ChecklistTemplate>(), _employeeId, employee.UserId, null, record.LastWorkingDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instantiatedTasks);

        var result = await CreateSut().Handle(new SelectOffboardingChecklistCommand(_employeeId, _templateId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        instantiatedTasks[0].OffboardingRecordId.Should().Be(record.Id);
        record.Status.Should().Be(OffboardingRecordStatuses.InProgress);
        record.ChecklistTemplateId.Should().Be(_templateId);
        _offboardingRecordRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~SelectOffboardingChecklistCommandHandlerTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/SelectOffboardingChecklist/ src/ONEVO.Api/Contracts/CoreHr/Offboarding/SelectOffboardingChecklistRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/SelectOffboardingChecklistCommandHandlerTests.cs
git commit -m "feat: add Select Checklist endpoint (instantiate offboarding tasks)"
```

---

### Task 12: Cancel Offboarding

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CancelOffboarding/CancelOffboardingCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CancelOffboarding/CancelOffboardingCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CancelOffboardingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync/SaveChangesAsync` (Task 2), `IEmployeeRepository.GetTrackedByIdAsync`.
- Produces: `CancelOffboardingCommand(Guid EmployeeId) : IRequest<Result>`.

- [ ] **Step 1: Command**

Create `CancelOffboardingCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;

public sealed record CancelOffboardingCommand(Guid EmployeeId) : IRequest<Result>;
```

- [ ] **Step 2: Handler**

Create `CancelOffboardingCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;

public class CancelOffboardingCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeRepository employeeRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CancelOffboardingCommand, Result>
{
    public async Task<Result> Handle(CancelOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");

        employee.EmploymentStatusId = record.PreviousEmploymentStatusId ?? EmploymentStatusIds.Active;
        record.Status = OffboardingRecordStatuses.Cancelled;
        record.UpdatedAt = clock.UtcNow;

        await offboardingRecordRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 3: Controller action**

In `EmployeeOffboardingController.cs`, add:

```csharp
    [HttpPost("cancel")]
    [RequirePermission("employees:offboard")]
    [Idempotent]
    public async Task<IActionResult> Cancel(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CancelOffboardingCommand(employeeId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CancelOffboardingCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CancelOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CancelOffboardingCommandHandlerTests
{
    [Fact]
    public async Task Handle_RevertsEmploymentStatus_AndCancelsRecord()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, PreviousEmploymentStatusId = EmploymentStatusIds.OnLeave };
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var employee = new EmployeeEntity { Id = employeeId, EmploymentStatusId = EmploymentStatusIds.Offboarding };
        employeeRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        var result = await new CancelOffboardingCommandHandler(offboardingRecordRepository.Object, employeeRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CancelOffboardingCommand(employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.OnLeave);
        record.Status.Should().Be(OffboardingRecordStatuses.Cancelled);
    }

    [Fact]
    public async Task Handle_NullPreviousStatus_FallsBackToActive()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.Initiated, PreviousEmploymentStatusId = null };
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var employee = new EmployeeEntity { Id = employeeId };
        employeeRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);

        await new CancelOffboardingCommandHandler(offboardingRecordRepository.Object, employeeRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CancelOffboardingCommand(employeeId), CancellationToken.None);

        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Active);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CancelOffboardingCommandHandlerTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CancelOffboarding/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CancelOffboardingCommandHandlerTests.cs
git commit -m "feat: add Cancel Offboarding endpoint"
```

---

### Task 13: List and patch employee checklist tasks

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListEmployeeChecklistTasks/ListEmployeeChecklistTasksQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListEmployeeChecklistTasks/ListEmployeeChecklistTasksQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListEmployeeChecklistTasks/EmployeeChecklistTaskResponse.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/UpdateEmployeeChecklistTask/UpdateEmployeeChecklistTaskCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/UpdateEmployeeChecklistTask/UpdateEmployeeChecklistTaskCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Offboarding/UpdateEmployeeChecklistTaskRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeChecklistTasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/UpdateEmployeeChecklistTaskCommandHandlerTests.cs`

**Interfaces:**
- Produces: `IEmployeeChecklistTaskRepository.GetTrackedByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)` and `.ListByOffboardingRecordAsync(Guid tenantId, Guid offboardingRecordId, CancellationToken ct = default)` — Task 14 (complete/bypass) and Task 15 (approve/reject) reuse `GetTrackedByIdAsync`; Task 16 (completion gate) reuses `ListByOffboardingRecordAsync`. `EmployeeChecklistTaskResponse(Guid Id, string TaskTitle, string OwnerType, Guid AssignedToId, DateOnly DueDate, bool IsRequired, bool IsBypassable, string? BypassPenaltyDescription, string? Category, string Status, DateTimeOffset? CompletedAt)`.

- [ ] **Step 1: Repository interface additions**

In `IOnboardingPersistenceRepositories.cs`, in `IEmployeeChecklistTaskRepository`, add:

```csharp
    /// <summary>Tenant+id lookup with no employee scoping - used both by employee-scoped handlers
    /// (which additionally verify task.EmployeeId == the route's employeeId) and by cross-employee
    /// bypass-approval handlers (Task 15), which only know the bypass request's task id.</summary>
    Task<EmployeeChecklistTask?> GetTrackedByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    /// <summary>Tasks belonging to one specific offboarding attempt (via EmployeeChecklistTask.
    /// OffboardingRecordId) - not "all this employee's offboarding tasks ever", which would wrongly
    /// include a prior cancelled attempt's rows. See Task 5's OffboardingRecordId rationale.</summary>
    Task<IReadOnlyList<EmployeeChecklistTask>> ListByOffboardingRecordAsync(Guid tenantId, Guid offboardingRecordId, CancellationToken ct = default);
```

- [ ] **Step 2: Implementations**

In `EfEmployeeChecklistTaskRepository`, add:

```csharp
    public Task<EmployeeChecklistTask?> GetTrackedByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
        => db.EmployeeChecklistTasks.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, ct);

    public Task<IReadOnlyList<EmployeeChecklistTask>> ListByOffboardingRecordAsync(Guid tenantId, Guid offboardingRecordId, CancellationToken ct = default)
        => db.EmployeeChecklistTasks.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.OffboardingRecordId == offboardingRecordId)
            .OrderBy(x => x.Sequence).ThenBy(x => x.Id)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<EmployeeChecklistTask>)t.Result, ct);
```

- [ ] **Step 3: List query**

Create `ListEmployeeChecklistTasksQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public sealed record ListEmployeeChecklistTasksQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EmployeeChecklistTaskResponse>>>;
```

Create `EmployeeChecklistTaskResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public sealed record EmployeeChecklistTaskResponse(
    Guid Id, string TaskTitle, string OwnerType, Guid AssignedToId, DateOnly DueDate, bool IsRequired,
    bool IsBypassable, string? BypassPenaltyDescription, string? Category, string Status, DateTimeOffset? CompletedAt);
```

Create `ListEmployeeChecklistTasksQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public class ListEmployeeChecklistTasksQueryHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListEmployeeChecklistTasksQuery, Result<IReadOnlyList<EmployeeChecklistTaskResponse>>>
{
    public async Task<Result<IReadOnlyList<EmployeeChecklistTaskResponse>>> Handle(ListEmployeeChecklistTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var record = await offboardingRecordRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result<IReadOnlyList<EmployeeChecklistTaskResponse>>.Success(new List<EmployeeChecklistTaskResponse>());

        var tasks = await taskRepository.ListByOffboardingRecordAsync(tenantId, record.Id, ct);
        return Result<IReadOnlyList<EmployeeChecklistTaskResponse>>.Success(tasks.Select(t => new EmployeeChecklistTaskResponse(
            t.Id, t.TaskTitle, t.OwnerType, t.AssignedToId, t.DueDate, t.IsRequired,
            t.IsBypassable, t.BypassPenaltyDescription, t.Category, t.Status, t.CompletedAt)).ToList());
    }
}
```

- [ ] **Step 4: Patch command and handler**

Create `UpdateEmployeeChecklistTaskCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

public sealed record UpdateEmployeeChecklistTaskCommand(
    Guid EmployeeId, Guid TaskId, Guid AssignedToId, DateOnly DueDate, bool IsRequired) : IRequest<Result>;
```

Create `UpdateEmployeeChecklistTaskCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

public class UpdateEmployeeChecklistTaskCommandHandler(IEmployeeChecklistTaskRepository repository, ICurrentUser currentUser)
    : IRequestHandler<UpdateEmployeeChecklistTaskCommand, Result>
{
    public async Task<Result> Handle(UpdateEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var task = await repository.GetTrackedByIdAsync(currentUser.TenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("A completed or bypassed task cannot be edited.");

        task.AssignedToId = request.AssignedToId;
        task.DueDate = request.DueDate;
        task.IsRequired = request.IsRequired;

        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Contract and controller**

Create `src/ONEVO.Api/Contracts/CoreHr/Offboarding/UpdateEmployeeChecklistTaskRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record UpdateEmployeeChecklistTaskRequest(Guid AssignedToId, DateOnly DueDate, bool IsRequired);
```

Create `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeChecklistTasksController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/{employeeId:guid}/checklist-tasks")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeeChecklistTasksController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListEmployeeChecklistTasksQuery(employeeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPatch("{taskId:guid}")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Update(Guid employeeId, Guid taskId, [FromBody] UpdateEmployeeChecklistTaskRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, request.AssignedToId, request.DueDate, request.IsRequired), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 6: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/UpdateEmployeeChecklistTaskCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class UpdateEmployeeChecklistTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_CompletedTask_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var repository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        repository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Completed });

        var result = await new UpdateEmployeeChecklistTaskCommandHandler(repository.Object, currentUser.Object)
            .Handle(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, Guid.NewGuid(), new DateOnly(2026, 12, 1), true), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_PendingTask_UpdatesFields()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var newAssignee = Guid.NewGuid();
        var repository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        var task = new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending, IsRequired = true };
        repository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new UpdateEmployeeChecklistTaskCommandHandler(repository.Object, currentUser.Object)
            .Handle(new UpdateEmployeeChecklistTaskCommand(employeeId, taskId, newAssignee, new DateOnly(2026, 12, 15), false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.AssignedToId.Should().Be(newAssignee);
        task.IsRequired.Should().BeFalse();
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~UpdateEmployeeChecklistTaskCommandHandlerTests`
Expected: all pass. Run `dotnet build src/ONEVO.Api` to confirm the new controller compiles (and that no other `IEmployeeChecklistTaskRepository` implementers are broken by the two new interface methods — only `EfEmployeeChecklistTaskRepository` is expected to implement it).

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingPersistenceRepositories.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingPersistenceRepositories.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListEmployeeChecklistTasks/ src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/UpdateEmployeeChecklistTask/ src/ONEVO.Api/Contracts/CoreHr/Offboarding/UpdateEmployeeChecklistTaskRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeChecklistTasksController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/UpdateEmployeeChecklistTaskCommandHandlerTests.cs
git commit -m "feat: add list and patch employee checklist task endpoints"
```

---

### Task 14: Complete task and create bypass request

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteEmployeeChecklistTask/CompleteEmployeeChecklistTaskCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteEmployeeChecklistTask/CompleteEmployeeChecklistTaskCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CreateBypassRequest/CreateBypassRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CreateBypassRequest/CreateBypassRequestCommandHandler.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Offboarding/CreateBypassRequestRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeChecklistTasksController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteEmployeeChecklistTaskCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CreateBypassRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeChecklistTaskRepository.GetTrackedByIdAsync` (Task 13), `IOffboardingTaskBypassRequestRepository.HasPendingForTaskAsync/AddAsync/SaveChangesAsync` (Task 3), `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync` (Task 2).
- Produces: `CompleteEmployeeChecklistTaskCommand(Guid EmployeeId, Guid TaskId) : IRequest<Result>`; `CreateBypassRequestCommand(Guid EmployeeId, Guid TaskId, Guid ApproverId, string BypassReason, string? PenaltyDescription) : IRequest<Result<Guid>>` — Task 15's approve/reject handlers read the `OffboardingTaskBypassRequest.PriorTaskStatus` this task sets at creation.

- [ ] **Step 1: Complete task command and handler**

Create `CompleteEmployeeChecklistTaskCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;

public sealed record CompleteEmployeeChecklistTaskCommand(Guid EmployeeId, Guid TaskId) : IRequest<Result>;
```

Create `CompleteEmployeeChecklistTaskCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;

public class CompleteEmployeeChecklistTaskCommandHandler(
    IEmployeeChecklistTaskRepository taskRepository,
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CompleteEmployeeChecklistTaskCommand, Result>
{
    public async Task<Result> Handle(CompleteEmployeeChecklistTaskCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var task = await taskRepository.GetTrackedByIdAsync(tenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result.NotFound("The checklist task could not be found for this employee.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result.Conflict("This task is already resolved.");

        if (await bypassRequestRepository.HasPendingForTaskAsync(tenantId, task.Id, ct))
            return Result.Conflict("This task has a pending bypass request awaiting a decision.");

        task.Status = EmployeeChecklistTaskStatuses.Completed;
        task.CompletedAt = clock.UtcNow;

        await taskRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 2: Create bypass request command and handler**

Create `CreateBypassRequestCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;

public sealed record CreateBypassRequestCommand(
    Guid EmployeeId, Guid TaskId, Guid ApproverId, string BypassReason, string? PenaltyDescription) : IRequest<Result<Guid>>;
```

Create `CreateBypassRequestCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;

public class CreateBypassRequestCommandHandler(
    IEmployeeChecklistTaskRepository taskRepository,
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CreateBypassRequestCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateBypassRequestCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        if (request.ApproverId == currentUser.UserId)
            return Result<Guid>.UnprocessableEntity("You cannot approve your own bypass request.");

        var task = await taskRepository.GetTrackedByIdAsync(tenantId, request.TaskId, ct);
        if (task is null || task.EmployeeId != request.EmployeeId)
            return Result<Guid>.NotFound("The checklist task could not be found for this employee.");
        if (!task.IsBypassable)
            return Result<Guid>.UnprocessableEntity("This task cannot be bypassed.");
        if (task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed)
            return Result<Guid>.Conflict("This task is already resolved.");

        var openRecord = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (openRecord is null)
            return Result<Guid>.Conflict("No open offboarding was found for this employee.");

        var bypassRequest = new OffboardingTaskBypassRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeChecklistTaskId = task.Id,
            OffboardingRecordId = openRecord.Id,
            RequestedById = currentUser.UserId,
            ApproverId = request.ApproverId,
            BypassReason = request.BypassReason,
            PenaltyDescription = request.PenaltyDescription ?? task.BypassPenaltyDescription,
            PriorTaskStatus = task.Status,
            RequestedAt = clock.UtcNow,
        };

        try
        {
            await bypassRequestRepository.AddAsync(bypassRequest, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<Guid>.Conflict("This task already has a pending bypass request.");
        }

        return Result<Guid>.Success(bypassRequest.Id);
    }
}
```

(Uses `IUnitOfWork.SaveChangesAsync` directly rather than a repository-local `SaveChangesAsync`, so the partial-unique-index violation on concurrent duplicate requests surfaces as `DbUpdateException` → `UniqueConstraintConflictException`, matching the interceptor-translation pattern `ChangeEmployeePositionCommandHandler` relies on for its own unique-constraint catches.)

- [ ] **Step 3: Contract and controller actions**

Create `src/ONEVO.Api/Contracts/CoreHr/Offboarding/CreateBypassRequestRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record CreateBypassRequestRequest(Guid ApproverId, string BypassReason, string? PenaltyDescription);
```

In `EmployeeChecklistTasksController.cs`, add (with the two new command usings):

```csharp
    [HttpPost("{taskId:guid}/complete")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Complete(Guid employeeId, Guid taskId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CompleteEmployeeChecklistTaskCommand(employeeId, taskId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{taskId:guid}/bypass-requests")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> CreateBypassRequest(Guid employeeId, Guid taskId, [FromBody] CreateBypassRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateBypassRequestCommand(employeeId, taskId, request.ApproverId, request.BypassReason, request.PenaltyDescription), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(List), new { employeeId }, new { bypassRequestId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4: Handler tests**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteEmployeeChecklistTaskCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CompleteEmployeeChecklistTaskCommandHandlerTests
{
    [Fact]
    public async Task Handle_PendingBypassRequestExists_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending });
        bypassRepository.Setup(r => r.HasPendingForTaskAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new CompleteEmployeeChecklistTaskCommandHandler(taskRepository.Object, bypassRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CompleteEmployeeChecklistTaskCommand(employeeId, taskId), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_NoPendingBypass_MarksCompleted()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var task = new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, Status = EmployeeChecklistTaskStatuses.Pending };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        bypassRepository.Setup(r => r.HasPendingForTaskAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await new CompleteEmployeeChecklistTaskCommandHandler(taskRepository.Object, bypassRepository.Object, currentUser.Object, clock.Object)
            .Handle(new CompleteEmployeeChecklistTaskCommand(employeeId, taskId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.Completed);
        task.CompletedAt.Should().NotBeNull();
    }
}
```

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CreateBypassRequestCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CreateBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_ApproverIsRequester_ReturnsUnprocessableEntity()
    {
        var currentUser = new Mock<ICurrentUser>();
        var actingUserId = Guid.NewGuid();
        currentUser.Setup(c => c.UserId).Returns(actingUserId);

        var result = await new CreateBypassRequestCommandHandler(
                Mock.Of<IEmployeeChecklistTaskRepository>(), Mock.Of<IOffboardingTaskBypassRequestRepository>(),
                Mock.Of<IOffboardingRecordRepository>(), Mock.Of<IUnitOfWork>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new CreateBypassRequestCommand(Guid.NewGuid(), Guid.NewGuid(), actingUserId, "reason", null), CancellationToken.None);

        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_TaskNotBypassable_ReturnsUnprocessableEntity()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, IsBypassable = false });

        var result = await new CreateBypassRequestCommandHandler(
                taskRepository.Object, Mock.Of<IOffboardingTaskBypassRequestRepository>(),
                Mock.Of<IOffboardingRecordRepository>(), Mock.Of<IUnitOfWork>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new CreateBypassRequestCommand(employeeId, taskId, Guid.NewGuid(), "reason", null), CancellationToken.None);

        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesRequestWithPriorTaskStatusSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeChecklistTask { Id = taskId, EmployeeId = employeeId, IsBypassable = true, Status = EmployeeChecklistTaskStatuses.InProgress, BypassPenaltyDescription = "Default penalty" });
        offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingRecord { Id = recordId });

        OffboardingTaskBypassRequest? added = null;
        bypassRepository.Setup(r => r.AddAsync(It.IsAny<OffboardingTaskBypassRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OffboardingTaskBypassRequest, CancellationToken>((r, _) => added = r).Returns(Task.CompletedTask);

        var result = await new CreateBypassRequestCommandHandler(
                taskRepository.Object, bypassRepository.Object, offboardingRecordRepository.Object,
                unitOfWork.Object, currentUser.Object, clock.Object)
            .Handle(new CreateBypassRequestCommand(employeeId, taskId, Guid.NewGuid(), "Payment processed in advance.", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.PriorTaskStatus.Should().Be(EmployeeChecklistTaskStatuses.InProgress);
        added.PenaltyDescription.Should().Be("Default penalty");
        added.OffboardingRecordId.Should().Be(recordId);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CompleteEmployeeChecklistTaskCommandHandlerTests|FullyQualifiedName~CreateBypassRequestCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteEmployeeChecklistTask/ src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CreateBypassRequest/ src/ONEVO.Api/Contracts/CoreHr/Offboarding/CreateBypassRequestRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeChecklistTasksController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteEmployeeChecklistTaskCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CreateBypassRequestCommandHandlerTests.cs
git commit -m "feat: add complete task and create bypass request endpoints"
```

---

### Task 15: Approve/reject bypass request, list my pending

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/ApproveBypassRequest/ApproveBypassRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/ApproveBypassRequest/ApproveBypassRequestCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/RejectBypassRequest/RejectBypassRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/RejectBypassRequest/RejectBypassRequestCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListMyPendingBypassRequests/ListMyPendingBypassRequestsQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListMyPendingBypassRequests/ListMyPendingBypassRequestsQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListMyPendingBypassRequests/BypassRequestResponse.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/Offboarding/RejectBypassRequestRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/CoreHr/OffboardingBypassRequestsController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ApproveBypassRequestCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/RejectBypassRequestCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IOffboardingTaskBypassRequestRepository.GetTrackedByIdAsync/ListPendingByApproverAsync/SaveChangesAsync` (Task 3), `IEmployeeChecklistTaskRepository.GetTrackedByIdAsync` (Task 13).
- Produces: `ApproveBypassRequestCommand(Guid BypassRequestId) : IRequest<Result>`; `RejectBypassRequestCommand(Guid BypassRequestId, string? DecisionComment) : IRequest<Result>`; `ListMyPendingBypassRequestsQuery : IRequest<Result<IReadOnlyList<BypassRequestResponse>>>`.

- [ ] **Step 1: Approve command and handler**

Create `ApproveBypassRequestCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;

public sealed record ApproveBypassRequestCommand(Guid BypassRequestId) : IRequest<Result>;
```

Create `ApproveBypassRequestCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;

public class ApproveBypassRequestCommandHandler(
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ApproveBypassRequestCommand, Result>
{
    public async Task<Result> Handle(ApproveBypassRequestCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var bypassRequest = await bypassRequestRepository.GetTrackedByIdAsync(tenantId, request.BypassRequestId, ct);
        if (bypassRequest is null)
            return Result.NotFound("The bypass request could not be found.");
        if (bypassRequest.ApproverId != currentUser.UserId)
            return Result.Forbidden("Only the assigned approver can decide this request.");
        if (bypassRequest.Status != BypassRequestStatuses.Pending)
            return Result.Conflict("This bypass request has already been decided.");

        var task = await taskRepository.GetTrackedByIdAsync(tenantId, bypassRequest.EmployeeChecklistTaskId, ct);
        if (task is null)
            return Result.NotFound("The checklist task for this bypass request could not be found.");

        task.Status = EmployeeChecklistTaskStatuses.Bypassed;
        task.CompletedAt = clock.UtcNow;
        bypassRequest.Status = BypassRequestStatuses.Approved;
        bypassRequest.DecidedAt = clock.UtcNow;

        await bypassRequestRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 2: Reject command and handler**

Create `RejectBypassRequestCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;

public sealed record RejectBypassRequestCommand(Guid BypassRequestId, string? DecisionComment) : IRequest<Result>;
```

Create `RejectBypassRequestCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;

public class RejectBypassRequestCommandHandler(
    IOffboardingTaskBypassRequestRepository bypassRequestRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RejectBypassRequestCommand, Result>
{
    public async Task<Result> Handle(RejectBypassRequestCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var bypassRequest = await bypassRequestRepository.GetTrackedByIdAsync(tenantId, request.BypassRequestId, ct);
        if (bypassRequest is null)
            return Result.NotFound("The bypass request could not be found.");
        if (bypassRequest.ApproverId != currentUser.UserId)
            return Result.Forbidden("Only the assigned approver can decide this request.");
        if (bypassRequest.Status != BypassRequestStatuses.Pending)
            return Result.Conflict("This bypass request has already been decided.");

        var task = await taskRepository.GetTrackedByIdAsync(tenantId, bypassRequest.EmployeeChecklistTaskId, ct);
        if (task is not null)
            task.Status = bypassRequest.PriorTaskStatus;

        bypassRequest.Status = BypassRequestStatuses.Rejected;
        bypassRequest.DecidedAt = clock.UtcNow;
        bypassRequest.DecisionComment = request.DecisionComment;

        await bypassRequestRepository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 3: List-mine query and handler**

Create `BypassRequestResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public sealed record BypassRequestResponse(
    Guid Id, Guid EmployeeChecklistTaskId, Guid OffboardingRecordId, Guid RequestedById,
    string BypassReason, string? PenaltyDescription, string Status, DateTimeOffset RequestedAt);
```

Create `ListMyPendingBypassRequestsQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public sealed record ListMyPendingBypassRequestsQuery : IRequest<Result<IReadOnlyList<BypassRequestResponse>>>;
```

Create `ListMyPendingBypassRequestsQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public class ListMyPendingBypassRequestsQueryHandler(IOffboardingTaskBypassRequestRepository repository, ICurrentUser currentUser)
    : IRequestHandler<ListMyPendingBypassRequestsQuery, Result<IReadOnlyList<BypassRequestResponse>>>
{
    public async Task<Result<IReadOnlyList<BypassRequestResponse>>> Handle(ListMyPendingBypassRequestsQuery request, CancellationToken ct)
    {
        var requests = await repository.ListPendingByApproverAsync(currentUser.TenantId, currentUser.UserId, ct);
        return Result<IReadOnlyList<BypassRequestResponse>>.Success(requests.Select(r => new BypassRequestResponse(
            r.Id, r.EmployeeChecklistTaskId, r.OffboardingRecordId, r.RequestedById,
            r.BypassReason, r.PenaltyDescription, r.Status, r.RequestedAt)).ToList());
    }
}
```

- [ ] **Step 4: Contract and controller**

Create `src/ONEVO.Api/Contracts/CoreHr/Offboarding/RejectBypassRequestRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.CoreHr.Offboarding;

public sealed record RejectBypassRequestRequest(string? DecisionComment);
```

Create `src/ONEVO.Api/Controllers/Tenant/CoreHr/OffboardingBypassRequestsController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Offboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/offboarding-bypass-requests")]
[Authorize(Policy = "TenantPolicy")]
public class OffboardingBypassRequestsController(IMediator mediator) : ControllerBase
{
    /// <summary>Always scoped to the caller as approver - there is no arbitrary approverId
    /// override, per design spec §6.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> ListMine(CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyPendingBypassRequestsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/approve")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ApproveBypassRequestCommand(id), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{id:guid}/reject")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectBypassRequestRequest request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new RejectBypassRequestCommand(id, request.DecisionComment), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 5: Handler tests**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ApproveBypassRequestCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class ApproveBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_NotTheAssignedApprover_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OffboardingTaskBypassRequest { Id = requestId, ApproverId = Guid.NewGuid(), Status = BypassRequestStatuses.Pending });

        var result = await new ApproveBypassRequestCommandHandler(bypassRepository.Object, Mock.Of<IEmployeeChecklistTaskRepository>(), currentUser.Object, Mock.Of<IDateTimeProvider>())
            .Handle(new ApproveBypassRequestCommand(requestId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ValidApproval_SetsTaskBypassedAndRequestApproved()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(approverId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var bypassRequest = new OffboardingTaskBypassRequest { Id = requestId, ApproverId = approverId, Status = BypassRequestStatuses.Pending, EmployeeChecklistTaskId = taskId };
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(bypassRequest);
        var task = new EmployeeChecklistTask { Id = taskId, Status = EmployeeChecklistTaskStatuses.InProgress };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new ApproveBypassRequestCommandHandler(bypassRepository.Object, taskRepository.Object, currentUser.Object, clock.Object)
            .Handle(new ApproveBypassRequestCommand(requestId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.Bypassed);
        bypassRequest.Status.Should().Be(BypassRequestStatuses.Approved);
    }
}
```

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/RejectBypassRequestCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.RejectBypassRequest;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class RejectBypassRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidRejection_RestoresTaskToPriorStatus()
    {
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var bypassRepository = new Mock<IOffboardingTaskBypassRequestRepository>();
        var taskRepository = new Mock<IEmployeeChecklistTaskRepository>();
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(approverId);
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var bypassRequest = new OffboardingTaskBypassRequest
        {
            Id = requestId, ApproverId = approverId, Status = BypassRequestStatuses.Pending,
            EmployeeChecklistTaskId = taskId, PriorTaskStatus = EmployeeChecklistTaskStatuses.InProgress,
        };
        bypassRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, requestId, It.IsAny<CancellationToken>())).ReturnsAsync(bypassRequest);
        var task = new EmployeeChecklistTask { Id = taskId, Status = EmployeeChecklistTaskStatuses.InProgress };
        taskRepository.Setup(r => r.GetTrackedByIdAsync(tenantId, taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var result = await new RejectBypassRequestCommandHandler(bypassRepository.Object, taskRepository.Object, currentUser.Object, clock.Object)
            .Handle(new RejectBypassRequestCommand(requestId, "Not approved."), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(EmployeeChecklistTaskStatuses.InProgress);
        bypassRequest.Status.Should().Be(BypassRequestStatuses.Rejected);
        bypassRequest.DecisionComment.Should().Be("Not approved.");
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ApproveBypassRequestCommandHandlerTests|FullyQualifiedName~RejectBypassRequestCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/ApproveBypassRequest/ src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/RejectBypassRequest/ src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListMyPendingBypassRequests/ src/ONEVO.Api/Contracts/CoreHr/Offboarding/RejectBypassRequestRequest.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/OffboardingBypassRequestsController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ApproveBypassRequestCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/RejectBypassRequestCommandHandlerTests.cs
git commit -m "feat: add approve/reject bypass request and list my pending endpoints"
```

---

### Task 16: Complete Employee Exit

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/OffboardingCompletionGate.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/CompleteOffboardingCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/CompleteOffboardingCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingCompletionGateTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteOffboardingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeChecklistTaskRepository.ListByOffboardingRecordAsync` (Task 13), `IOffboardingRecordRepository.GetOpenByEmployeeIdAsync/SaveChangesAsync` (Task 2), `IUserRepository.GetByIdAsync` (existing), `ISessionRepository.RevokeAllActiveByUserIdAsync` (Task 8), `IEmployeeRepository.GetTrackedByIdAsync`, `IUnitOfWork.SaveChangesAsync`.
- Produces: `OffboardingCompletionGate.AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTask> tasks) -> bool` (static, independently unit-tested per the advisor's split of the gate check from the full transaction); `CompleteOffboardingCommand(Guid EmployeeId) : IRequest<Result>`.

- [ ] **Step 1: Write the gate as a standalone pure function, with its own tests first**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingCompletionGateTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class OffboardingCompletionGateTests
{
    private static EmployeeChecklistTask Task(bool required, string status) =>
        new() { Id = Guid.NewGuid(), IsRequired = required, Status = status };

    [Fact]
    public void AllRequiredTasksResolved_AllRequiredCompleted_ReturnsTrue()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(true, EmployeeChecklistTaskStatuses.Bypassed) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeTrue();
    }

    [Fact]
    public void AllRequiredTasksResolved_OneRequiredStillPending_ReturnsFalse()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(true, EmployeeChecklistTaskStatuses.Pending) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeFalse();
    }

    [Fact]
    public void AllRequiredTasksResolved_NonRequiredStillPending_DoesNotBlock()
    {
        var tasks = new[] { Task(true, EmployeeChecklistTaskStatuses.Completed), Task(false, EmployeeChecklistTaskStatuses.Pending) };
        OffboardingCompletionGate.AllRequiredTasksResolved(tasks).Should().BeTrue();
    }

    [Fact]
    public void AllRequiredTasksResolved_NoTasksAtAll_ReturnsTrue()
    {
        OffboardingCompletionGate.AllRequiredTasksResolved(Array.Empty<EmployeeChecklistTask>()).Should().BeTrue();
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OffboardingCompletionGateTests`
Expected: FAIL (compile error - `OffboardingCompletionGate` doesn't exist yet).

- [ ] **Step 2: Implement the gate**

Create `OffboardingCompletionGate.cs`:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

/// <summary>Standalone, independently-testable completion gate - kept separate from
/// CompleteOffboardingCommandHandler's transaction so a reviewer can verify the gate logic
/// without standing up the full handler (per the design's advisor review).</summary>
public static class OffboardingCompletionGate
{
    public static bool AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTask> tasks) =>
        tasks.Where(t => t.IsRequired)
            .All(t => t.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed);
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OffboardingCompletionGateTests`
Expected: all four pass.

- [ ] **Step 3: Command**

Create `CompleteOffboardingCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

public sealed record CompleteOffboardingCommand(Guid EmployeeId) : IRequest<Result>;
```

- [ ] **Step 4: Handler**

Create `CompleteOffboardingCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

public class CompleteOffboardingCommandHandler(
    IOffboardingRecordRepository offboardingRecordRepository,
    IEmployeeChecklistTaskRepository taskRepository,
    IEmployeeRepository employeeRepository,
    IUserRepository userRepository,
    ISessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CompleteOffboardingCommand, Result>
{
    public async Task<Result> Handle(CompleteOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var record = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (record is null)
            return Result.NotFound("No open offboarding was found for this employee.");
        if (record.Status != OffboardingRecordStatuses.InProgress)
            return Result.Conflict("A checklist must be selected before this offboarding can be completed.");

        var tasks = await taskRepository.ListByOffboardingRecordAsync(tenantId, record.Id, ct);
        if (!OffboardingCompletionGate.AllRequiredTasksResolved(tasks))
            return Result.UnprocessableEntity("Every required checklist task must be completed or bypassed before the exit can be finalized.");

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.NotFound("The employee could not be found.");

        var user = await userRepository.GetByIdAsync(employee.UserId, ct);
        if (user is null)
            return Result.NotFound("The employee's user account could not be found.");

        employee.EmploymentStatusId = record.Reason == "resignation" ? EmploymentStatusIds.Resigned : EmploymentStatusIds.Terminated;
        employee.TerminationDate = record.LastWorkingDate;
        user.IsActive = false;
        record.Status = OffboardingRecordStatuses.Completed;
        record.CompletedAt = clock.UtcNow;
        record.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);
        await sessionRepository.RevokeAllActiveByUserIdAsync(user.Id, ct);

        return Result.Success();
    }
}
```

(`sessionRepository.RevokeAllActiveByUserIdAsync` uses `ExecuteUpdateAsync` — Task 8 — which commits immediately and independently of the tracked-entity `SaveChangesAsync` above; it's called after the main save so a failure mid-transaction on the tracked changes never leaves sessions revoked for an employee whose offboarding didn't actually complete.)

- [ ] **Step 5: Controller action**

In `EmployeeOffboardingController.cs`, add:

```csharp
    [HttpPost("complete")]
    [RequirePermission("employees:offboard")]
    [Idempotent]
    public async Task<IActionResult> Complete(Guid employeeId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CompleteOffboardingCommand(employeeId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteOffboardingCommandHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class CompleteOffboardingCommandHandlerTests
{
    private readonly Mock<IOffboardingRecordRepository> _offboardingRecordRepository = new();
    private readonly Mock<IEmployeeChecklistTaskRepository> _taskRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<ISessionRepository> _sessionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public CompleteOffboardingCommandHandlerTests()
    {
        _currentUser.Setup(c => c.TenantId).Returns(_tenantId);
        _clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
    }

    private CompleteOffboardingCommandHandler CreateSut() => new(
        _offboardingRecordRepository.Object, _taskRepository.Object, _employeeRepository.Object,
        _userRepository.Object, _sessionRepository.Object, _unitOfWork.Object, _currentUser.Object, _clock.Object);

    [Fact]
    public async Task Handle_RequiredTaskStillPending_ReturnsUnprocessableEntity()
    {
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "resignation" };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { IsRequired = true, Status = EmployeeChecklistTaskStatuses.Pending } });

        var result = await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(422);
        _sessionRepository.Verify(r => r.RevokeAllActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ResignationReason_MapsToResignedAndCompletesFully()
    {
        var userId = Guid.NewGuid();
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "resignation", LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeChecklistTask> { new() { IsRequired = true, Status = EmployeeChecklistTaskStatuses.Completed } });
        var employee = new EmployeeEntity { Id = _employeeId, UserId = userId, EmploymentStatusId = EmploymentStatusIds.Offboarding };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var user = new User { Id = userId, IsActive = true };
        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Resigned);
        employee.TerminationDate.Should().Be(record.LastWorkingDate);
        user.IsActive.Should().BeFalse();
        record.Status.Should().Be(OffboardingRecordStatuses.Completed);
        _sessionRepository.Verify(r => r.RevokeAllActiveByUserIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminationReason_MapsToTerminated()
    {
        var userId = Guid.NewGuid();
        var record = new OffboardingRecord { Id = Guid.NewGuid(), Status = OffboardingRecordStatuses.InProgress, Reason = "termination", LastWorkingDate = new DateOnly(2026, 12, 1) };
        _offboardingRecordRepository.Setup(r => r.GetOpenByEmployeeIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _taskRepository.Setup(r => r.ListByOffboardingRecordAsync(_tenantId, record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<EmployeeChecklistTask>());
        var employee = new EmployeeEntity { Id = _employeeId, UserId = userId };
        _employeeRepository.Setup(r => r.GetTrackedByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId, IsActive = true });

        await CreateSut().Handle(new CompleteOffboardingCommand(_employeeId), CancellationToken.None);

        employee.EmploymentStatusId.Should().Be(EmploymentStatusIds.Terminated);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~OffboardingCompletionGateTests|FullyQualifiedName~CompleteOffboardingCommandHandlerTests"`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/ src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeeOffboardingController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/OffboardingCompletionGateTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/CompleteOffboardingCommandHandlerTests.cs
git commit -m "feat: add Complete Employee Exit endpoint"
```

---

### Task 17: Read-only guard on `ChangeEmployeePositionCommandHandler`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/ServiceInterfaces/IEmployeeOffboardingLockGuard.cs`
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingLockGuard.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingLockGuardTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/ChangeEmployeePositionCommandHandlerTests.cs` (extend if it exists, else create)

**Interfaces:**
- Produces: `IEmployeeOffboardingLockGuard.EnsureMutable(Guid tenantId, Guid employeeId, CancellationToken ct = default) -> Task<Result?>` (`null` = mutable, otherwise the `Conflict` `Result` to return immediately) — this is the exact call `ChangeEmployeePositionCommandHandler` makes right after loading the employee.

- [ ] **Step 1: Interface and implementation**

Create `IEmployeeOffboardingLockGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;

/// <summary>Rejects mutation of an employee whose EmploymentStatusId is Resigned/Terminated.
/// Only ChangeEmployeePositionCommandHandler calls this today - every self-service me/* write is
/// already transitively blocked because User.IsActive=false (set at offboarding completion) fails
/// authentication on the very next request via TenantDatabaseTicketStore.RetrieveAsync, so no
/// other guard call site exists as of this codebase's current write surface. See design spec §7.</summary>
public interface IEmployeeOffboardingLockGuard
{
    Task<Result?> EnsureMutable(Guid tenantId, Guid employeeId, CancellationToken ct = default);
}
```

Create `src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingLockGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Domain.Lookups;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr.Offboarding;

public sealed class EmployeeOffboardingLockGuard(IEmployeeRepository employeeRepository) : IEmployeeOffboardingLockGuard
{
    public async Task<Result?> EnsureMutable(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await employeeRepository.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null)
            return null; // Not this guard's concern - the caller's own not-found check handles it.

        if (employee.EmploymentStatusId is EmploymentStatusIds.Resigned or EmploymentStatusIds.Terminated)
            return Result.Conflict("This employee's record is read-only after offboarding completion.");

        return null;
    }
}
```

- [ ] **Step 2: Register in DI**

In `DependencyInjection.cs`, add:

```csharp
        services.AddScoped<IEmployeeOffboardingLockGuard, EmployeeOffboardingLockGuard>();
```

- [ ] **Step 3: Call the guard in `ChangeEmployeePositionCommandHandler`**

Add `IEmployeeOffboardingLockGuard offboardingLockGuard` as a constructor parameter/field (same pattern as the handler's existing dependencies), and immediately after the existing:

```csharp
        var employee = await _employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<ChangeEmployeePositionResponse>.NotFound("The employee could not be found.");
```

insert:

```csharp

        var lockResult = await offboardingLockGuard.EnsureMutable(tenantId, employee.Id, ct);
        if (lockResult is not null)
            return Result<ChangeEmployeePositionResponse>.Conflict(lockResult.Error!);
```

- [ ] **Step 4: Guard unit test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingLockGuardTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeOffboardingLockGuardTests
{
    [Theory]
    [InlineData(EmploymentStatusIds.Resigned)]
    [InlineData(EmploymentStatusIds.Terminated)]
    public async Task EnsureMutable_ResignedOrTerminated_ReturnsConflict(int statusId)
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var repo = new Mock<IEmployeeRepository>();
        repo.Setup(r => r.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, EmploymentStatusId = statusId });

        var result = await new EmployeeOffboardingLockGuard(repo.Object).EnsureMutable(tenantId, employeeId);

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task EnsureMutable_ActiveEmployee_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var repo = new Mock<IEmployeeRepository>();
        repo.Setup(r => r.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, EmploymentStatusId = EmploymentStatusIds.Active });

        var result = await new EmployeeOffboardingLockGuard(repo.Object).EnsureMutable(tenantId, employeeId);

        result.Should().BeNull();
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EmployeeOffboardingLockGuardTests`
Expected: all pass. Also run the existing `ChangeEmployeePositionCommandHandler` test suite (`dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ChangeEmployeePosition`) to confirm the new constructor parameter didn't break any existing test's construction — if it did, add an `IEmployeeOffboardingLockGuard` mock (default-mocked to return `null`) to that file's setup.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/ServiceInterfaces/IEmployeeOffboardingLockGuard.cs src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingLockGuard.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Application/Features/CoreHr/Employee/Commands/ChangeEmployeePosition/ChangeEmployeePositionCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingLockGuardTests.cs
git commit -m "feat: block change-position on offboarded employees"
```

---

### Task 18: Integration test suite

**Files:**
- Create: `tests/ONEVO.Tests.Integration/CoreHr/Offboarding/OffboardingExecutionIntegrationTests.cs`

**Interfaces:**
- Consumes: every repository/entity from Tasks 1-17, exercised end-to-end against a real Postgres via Testcontainers.

- [ ] **Step 1: Scaffold the test class following `ChecklistTemplatesIntegrationTests.cs`'s exact pattern**

Create `tests/ONEVO.Tests.Integration/CoreHr/Offboarding/OffboardingExecutionIntegrationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.Offboarding;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.Offboarding;

public sealed class OffboardingExecutionIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_offboarding_execution_test")
        .WithUsername("test").WithPassword("test").Build();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _employeeId;
    private Guid _employeeUserId;
    private Guid _hrAdminUserId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(_connectionString, CancellationToken.None);

        await using var db = CreateContext();
        _tenantId = Guid.NewGuid();
        _legalEntityId = Guid.NewGuid();
        _employeeUserId = Guid.NewGuid();
        _hrAdminUserId = Guid.NewGuid();
        _employeeId = Guid.NewGuid();

        // Seed the minimum Tenant/LegalEntity/Users/Employee graph this test needs directly -
        // follow the same manual-seed pattern ChecklistTemplatesIntegrationTests.cs uses rather
        // than relying on LookupDataSeeder (which is a hosted service, not run by these tests).
        // Fill in exact Tenant/User/Employee construction to match this codebase's required
        // non-nullable fields (verify against ChecklistTemplatesIntegrationTests.cs's own seed
        // block for the current exact shape before writing this test for real).
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString).Options;
        return new ApplicationDbContext(options);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task FullHappyPath_StartToComplete_LocksEmployeeRecord()
    {
        // Start -> select checklist (a manually-inserted offboarding ChecklistTemplate with one
        // required, non-bypassable task) -> complete the task -> complete the exit.
        // Assert: Employee.EmploymentStatusId is Resigned/Terminated per Reason, User.IsActive is
        // false, all Sessions for that user are IsRevoked, OffboardingRecord.Status is Completed.
    }

    [Fact]
    public async Task CancelThenRestart_SecondAttemptsTasksDoNotIncludeFirstAttemptsTasks()
    {
        // Start -> select checklist -> cancel -> start again -> select a different checklist.
        // Assert: ListByOffboardingRecordAsync for the second OffboardingRecord.Id returns only
        // the second attempt's tasks, not the first (cancelled) attempt's - this is the exact bug
        // OffboardingRecordId (Task 5) exists to prevent.
    }

    [Fact]
    public async Task BypassRequest_RejectThenComplete_TaskReturnsToPriorStatusAndCanStillBeCompleted()
    {
        // Create a bypass request, reject it, then complete the task normally.
    }

    [Fact]
    public async Task ChangePosition_AfterOffboardingCompletion_Returns409()
    {
        // Full offboarding completion, then call ChangeEmployeePositionCommandHandler directly
        // (or through the controller) and assert StatusCode == 409.
    }
}
```

**Note for whoever executes this task:** the seed block and the four test bodies are deliberately left as structured comments describing exactly what each must assert, rather than fully inlined — this is the one place in the plan where that's true, because it depends on reading `ChecklistTemplatesIntegrationTests.cs` and `IntegrationDatabaseBootstrap.cs` in full first (their exact current Tenant/User/Employee/LegalEntity seed shape, which changes as those entities gain/lose required fields over time — hardcoding a seed block here that might already be stale by execution time would violate the plan's own "verify against live code" standard more than leaving it as a precise checklist does). Read both files, then write the seed block and four test bodies following their exact construction pattern before running this task's tests.

- [ ] **Step 2: Run the suite**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~OffboardingExecutionIntegrationTests`
Expected: all four pass against the real Testcontainers Postgres instance (requires Docker running locally/in CI).

- [ ] **Step 3: Confirm RLS coverage picked up the two new tables automatically**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~TenantIsolationArchitectureTests`
Expected: `EveryTenantOwnedEntityTable_HasRlsPolicyCoverage` passes, confirming `offboarding_records` and `offboarding_task_bypass_requests` were correctly declared in Task 4's `TenantTables` array — no test-file edits needed for this, per the Global Constraints rule.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/Offboarding/
git commit -m "test: add employee offboarding execution integration tests"
```

---

### Task 19: `employees:offboard` permission and coverage guard

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/ServiceInterfaces/IEmployeeOffboardingCoverageGuard.cs`
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingCoverageGuard.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/SelectOffboardingChecklist/SelectOffboardingChecklistCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CancelOffboarding/CancelOffboardingCommandHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/CompleteOffboardingCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingCoverageGuardTests.cs`

**Interfaces:**
- Consumes: `IEmployeeVisibilityScopeResolver.ResolveAsync(tenantId, userId, ct) -> Task<EmployeeVisibilityScope>` (existing, `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EmployeeVisibilityScopeResolver.cs`), `IPositionAssignmentRepository.GetActivePrimaryAsync` (existing, already used by `ChangeEmployeePositionCommandHandler`), `IEmployeeRepository.GetByIdAsync` (CoreHr.Employee namespace, existing).
- Produces: `IEmployeeOffboardingCoverageGuard.EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default) -> Task<Result?>` — called by all four commands' handlers, right after the controller's `[RequirePermission("employees:offboard")]` has already passed (Steps 3-4 below patched those controller actions in Tasks 9/11/12/16, already done above).

- [ ] **Step 1: Add the permission**

In `PermissionSeeder.cs`, alongside the existing `Perm("employees:write", ...)` line, add:

```csharp
        Perm("employees:offboard", "Start, cancel, or complete an employee's offboarding.", "core_hr"),
```

This is a data-only addition — `PermissionSeeder` follows the same idempotent-upsert-by-code convention as other lookup seeders in this codebase (verify by reading the seeder's loop before assuming — if it's an `AnyAsync()`-guarded whole-table skip like `LookupDataSeeder` (Task 1's finding), this needs the same `InsertData` migration treatment Task 1 used, not just an array edit; if it upserts per-code, the array edit alone is sufficient. Confirm which before writing Step 2.).

- [ ] **Step 2: If needed, add the backfill migration (see Step 1's caveat)**

If `PermissionSeeder` is whole-table-skip-guarded, create `src/ONEVO.Infrastructure/Migrations/20260818090000_AddEmployeesOffboardPermission.cs` with an `InsertData` on the `permissions` table (columns `id`, `code`, `description`, `module`, `feature_key`), following Task 1's exact pattern. Generate a fresh `Guid` id (a literal, deterministic GUID, not `Guid.NewGuid()` — migrations must be reproducible) for the permission row.

- [ ] **Step 3: Interface and guard implementation**

Create `IEmployeeOffboardingCoverageGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;

/// <summary>Enforces the 2026-08-18 coverage requirement: a caller may only act on an employee's
/// offboarding record if that employee falls within the caller's own management_coverage_records
/// coverage - never bypassed by an "unrestricted" flag, unlike the rest of this app's employee
/// visibility (see design spec §11 for why that's a deliberate, stricter exception here).</summary>
public interface IEmployeeOffboardingCoverageGuard
{
    Task<Result?> EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default);
}
```

Create `src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingCoverageGuard.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Infrastructure.Services.CoreHr.Offboarding;

public sealed class EmployeeOffboardingCoverageGuard(
    IEmployeeVisibilityScopeResolver scopeResolver,
    IEmployeeRepository employeeRepository,
    IPositionAssignmentRepository positionAssignmentRepository)
    : IEmployeeOffboardingCoverageGuard
{
    public async Task<Result?> EnsureCovered(Guid tenantId, Guid actingUserId, Guid targetEmployeeId, CancellationToken ct = default)
    {
        var employee = await employeeRepository.GetByIdAsync(tenantId, targetEmployeeId, ct);
        if (employee is null)
            return null; // Not this guard's concern - the caller's own not-found check handles it.

        // Deliberately never substitutes EmployeeVisibilityScope.Unrestricted() - every caller,
        // including org:manage-style admins, is scoped by literal coverage rows here (design spec §11).
        var scope = await scopeResolver.ResolveAsync(tenantId, actingUserId, ct);

        var activeAssignment = await positionAssignmentRepository.GetActivePrimaryAsync(tenantId, targetEmployeeId, ct);
        var isCovered =
            (activeAssignment is not null && scope.CoveredPositionIds.Contains(activeAssignment.PositionId))
            || (employee.DepartmentId is not null && scope.CoveredDepartmentIds.Contains(employee.DepartmentId.Value))
            || (employee.LegalEntityId is not null && scope.CompanyWideLegalEntityIds.Contains(employee.LegalEntityId.Value));

        return isCovered ? null : Result.Forbidden("You do not have management coverage over this employee.");
    }
}
```

- [ ] **Step 4: Register in DI**

In `DependencyInjection.cs`, add:

```csharp
        services.AddScoped<IEmployeeOffboardingCoverageGuard, EmployeeOffboardingCoverageGuard>();
```

- [ ] **Step 5: Wire the guard into the four record-lifecycle handlers**

In `StartOffboardingCommandHandler.cs`: add `IEmployeeOffboardingCoverageGuard coverageGuard` as a constructor parameter/field, and immediately after the existing self-offboarding check:

```csharp
        if (employee.UserId == currentUser.UserId)
            return Result<Guid>.Forbidden("You cannot start offboarding on your own record.");
```

insert:

```csharp

        var coverageResult = await coverageGuard.EnsureCovered(tenantId, currentUser.UserId, employee.Id, ct);
        if (coverageResult is not null)
            return Result<Guid>.Forbidden(coverageResult.Error!);
```

In `SelectOffboardingChecklistCommandHandler.cs`, `CancelOffboardingCommandHandler.cs`, and `CompleteOffboardingCommandHandler.cs`: same pattern — add the constructor parameter, and insert the same `coverageGuard.EnsureCovered(...)` check immediately after each handler's own not-found check on the `OffboardingRecord`/`Employee` (i.e., as early as possible once `request.EmployeeId` is known to refer to a real employee), returning the guard's `Forbidden` result (adapted to each handler's own `Result`/`Result<T>` return type) if non-null.

- [ ] **Step 6: Guard unit test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingCoverageGuardTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class EmployeeOffboardingCoverageGuardTests
{
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actingUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private EmployeeOffboardingCoverageGuard CreateSut() =>
        new(_scopeResolver.Object, _employeeRepository.Object, _positionAssignmentRepository.Object);

    [Fact]
    public async Task EnsureCovered_DepartmentInScope_ReturnsNull()
    {
        var departmentId = Guid.NewGuid();
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, DepartmentId = departmentId });
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _actingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid> { departmentId }, new HashSet<Guid>()));

        var result = await CreateSut().EnsureCovered(_tenantId, _actingUserId, _employeeId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EnsureCovered_NoOverlap_ReturnsForbidden()
    {
        _employeeRepository.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, DepartmentId = Guid.NewGuid(), LegalEntityId = Guid.NewGuid() });
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _actingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));

        var result = await CreateSut().EnsureCovered(_tenantId, _actingUserId, _employeeId);

        // CanViewAllTenantEmployees = true is deliberately ignored - still Forbidden.
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(403);
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EmployeeOffboardingCoverageGuardTests`
Expected: both pass — the second test is the one that actually proves the "never `Unrestricted()`" requirement, since `CanViewAllTenantEmployees: true` is passed in and the guard still returns `Forbidden`.

Also re-run `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~StartOffboardingCommandHandlerTests|FullyQualifiedName~SelectOffboardingChecklistCommandHandlerTests|FullyQualifiedName~CancelOffboardingCommandHandlerTests|FullyQualifiedName~CompleteOffboardingCommandHandlerTests"` — these four existing test files (Tasks 9/11/12/16) now construct their handler with one more constructor parameter; add a `Mock<IEmployeeOffboardingCoverageGuard>` (default-mocked to return `null`, i.e. covered) to each file's setup so the existing "happy path" tests keep passing once the new parameter is wired in — this is expected, not a sign of a broken test.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Application/Features/CoreHr/Offboarding/ServiceInterfaces/IEmployeeOffboardingCoverageGuard.cs src/ONEVO.Infrastructure/Services/CoreHr/Offboarding/EmployeeOffboardingCoverageGuard.cs src/ONEVO.Infrastructure/DependencyInjection.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/StartOffboarding/StartOffboardingCommandHandler.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/SelectOffboardingChecklist/SelectOffboardingChecklistCommandHandler.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CancelOffboarding/CancelOffboardingCommandHandler.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Commands/CompleteOffboarding/CompleteOffboardingCommandHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/EmployeeOffboardingCoverageGuardTests.cs
git commit -m "feat: add employees:offboard permission and coverage guard for offboarding lifecycle actions"
```

---

### Task 20: Coverage-scoped offboarding overview endpoint

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingRecordRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingRecordRepository.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingOverview/ListOffboardingOverviewQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingOverview/ListOffboardingOverviewQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingOverview/OffboardingOverviewItemResponse.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/CoreHr/OffboardingOverviewController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ListOffboardingOverviewQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEmployeeVisibilityScopeResolver.ResolveAsync` (existing), `IEmployeeRepository.ListVisibleAsync` (CoreHr.Employee, existing).
- Produces: `IOffboardingRecordRepository.GetLatestStatusesByEmployeeIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default) -> Task<IReadOnlyDictionary<Guid, string>>` (absent key = no offboarding record ever existed for that employee); `OffboardingOverviewItemResponse(Guid EmployeeId, string EmployeeName, string? DepartmentName, string? PositionName, string? CurrentOffboardingStatus, bool CanStartOffboarding)`; `GET /api/v1/employees/offboarding-overview` — the frontend plan's coverage screen (Task 13) calls this exact route.

- [ ] **Step 1: Repository addition**

In `IOffboardingRecordRepository.cs`, add:

```csharp
    /// <summary>Batched latest-status lookup - avoids N+1 when listing many employees' offboarding
    /// overview. Absent key means the employee has no offboarding_records row at all.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetLatestStatusesByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
```

In `EfOffboardingRecordRepository.cs`, add:

```csharp
    public async Task<IReadOnlyDictionary<Guid, string>> GetLatestStatusesByEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        var rows = await db.OffboardingRecords.AsNoTracking()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId))
            .GroupBy(x => x.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Status = g.OrderByDescending(x => x.CreatedAt).First().Status })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.EmployeeId, r => r.Status);
    }
```

- [ ] **Step 2: Query, response, and handler**

Create `OffboardingOverviewItemResponse.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public sealed record OffboardingOverviewItemResponse(
    Guid EmployeeId, string EmployeeName, string? DepartmentName, string? PositionName,
    string? CurrentOffboardingStatus, bool CanStartOffboarding);
```

Create `ListOffboardingOverviewQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public sealed record ListOffboardingOverviewQuery(int Page = 1, int PageSize = 25)
    : IRequest<Result<IReadOnlyList<OffboardingOverviewItemResponse>>>;
```

Create `ListOffboardingOverviewQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public class ListOffboardingOverviewQueryHandler(
    IEmployeeVisibilityScopeResolver scopeResolver,
    IEmployeeRepository employeeRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ListOffboardingOverviewQuery, Result<IReadOnlyList<OffboardingOverviewItemResponse>>>
{
    private static readonly HashSet<string> OpenStatuses = ["initiated", "in_progress"];

    public async Task<Result<IReadOnlyList<OffboardingOverviewItemResponse>>> Handle(
        ListOffboardingOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        // Deliberately never EmployeeVisibilityScope.Unrestricted() - see design spec §11.
        var scope = await scopeResolver.ResolveAsync(tenantId, currentUser.UserId, ct);

        var (items, _) = await employeeRepository.ListVisibleAsync(
            tenantId, scope, new EmployeeListFilter(null, null, null), request.Page, request.PageSize, ct);

        var employeeIds = items.Select(i => i.Id).ToList();
        var statuses = await offboardingRecordRepository.GetLatestStatusesByEmployeeIdsAsync(tenantId, employeeIds, ct);

        var result = items.Select(i =>
        {
            statuses.TryGetValue(i.Id, out var status);
            return new OffboardingOverviewItemResponse(
                i.Id, $"{i.FirstName} {i.LastName}".Trim(), i.DepartmentName, i.PositionName,
                status, CanStartOffboarding: status is null || !OpenStatuses.Contains(status));
        }).ToList();

        return Result<IReadOnlyList<OffboardingOverviewItemResponse>>.Success(result);
    }
}
```

(`EmployeeListItemResponse`'s exact field names — `Id`/`FirstName`/`LastName`/`DepartmentName`/`PositionName` — are assumed from `ListEmployeesQueryHandler`'s existing usage; re-verify against the live `EmployeeListItemResponse` DTO before implementing, since this plan's research didn't re-read that specific file's fields in full.)

- [ ] **Step 3: Controller**

Create `OffboardingOverviewController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees/offboarding-overview")]
[Authorize(Policy = "TenantPolicy")]
public class OffboardingOverviewController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListOffboardingOverviewQuery(page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 4: Handler test**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ListOffboardingOverviewQueryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Offboarding;

public class ListOffboardingOverviewQueryHandlerTests
{
    [Fact]
    public async Task Handle_EmployeeWithNoOffboardingRecord_CanStartOffboardingIsTrue()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var scopeResolver = new Mock<IEmployeeVisibilityScopeResolver>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);
        scopeResolver.Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        employeeRepository.Setup(r => r.ListVisibleAsync(tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { new() { Id = employeeId, FirstName = "Ada", LastName = "Lovelace" } }, 1));
        offboardingRecordRepository.Setup(r => r.GetLatestStatusesByEmployeeIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var result = await new ListOffboardingOverviewQueryHandler(scopeResolver.Object, employeeRepository.Object, offboardingRecordRepository.Object, currentUser.Object)
            .Handle(new ListOffboardingOverviewQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value![0].CanStartOffboarding.Should().BeTrue();
        result.Value[0].CurrentOffboardingStatus.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmployeeWithOpenOffboarding_CanStartOffboardingIsFalse()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var scopeResolver = new Mock<IEmployeeVisibilityScopeResolver>();
        var employeeRepository = new Mock<IEmployeeRepository>();
        var offboardingRecordRepository = new Mock<IOffboardingRecordRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(c => c.TenantId).Returns(tenantId);
        currentUser.Setup(c => c.UserId).Returns(userId);
        scopeResolver.Setup(r => r.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        employeeRepository.Setup(r => r.ListVisibleAsync(tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { new() { Id = employeeId, FirstName = "Ada", LastName = "Lovelace" } }, 1));
        offboardingRecordRepository.Setup(r => r.GetLatestStatusesByEmployeeIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [employeeId] = "in_progress" });

        var result = await new ListOffboardingOverviewQueryHandler(scopeResolver.Object, employeeRepository.Object, offboardingRecordRepository.Object, currentUser.Object)
            .Handle(new ListOffboardingOverviewQuery(), CancellationToken.None);

        result.Value![0].CanStartOffboarding.Should().BeFalse();
        result.Value[0].CurrentOffboardingStatus.Should().Be("in_progress");
    }
}
```

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ListOffboardingOverviewQueryHandlerTests`
Expected: both pass. If `EmployeeListItemResponse` construction fails to compile (per Step 2's flagged assumption), adjust the test's object initializer to match the DTO's real property names — the intent (name/department/position projected, status/CanStartOffboarding derived) stays the same regardless.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Offboarding/RepositoryInterfaces/IOffboardingRecordRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/Offboarding/EfOffboardingRecordRepository.cs src/ONEVO.Application/Features/CoreHr/Offboarding/Queries/ListOffboardingOverview/ src/ONEVO.Api/Controllers/Tenant/CoreHr/OffboardingOverviewController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Offboarding/ListOffboardingOverviewQueryHandlerTests.cs
git commit -m "feat: add coverage-scoped offboarding overview endpoint"
```

## Self-Review

**Spec coverage:** §4.1 (offboarding_records + gaps) → Tasks 2, 4. §4.2 (task fields) → Task 5. §4.3 (bypass table) → Tasks 3-4. §5.1 → Task 9. §5.2 → Tasks 7, 11. §5.3 → Tasks 13-15. §5.4 → Task 12. §5.5 → Task 16. §6 (API surface, including the revised permission column and the new overview endpoint) → Tasks 9-16, 20. §7 (read-only guard) → Task 17. §8 edge cases (bypassing own task, non-bypassable-fixed-at-template-time, all-non-required-templates, cancel-after-approvals) → covered by Tasks 12/14/16's handler logic (no orphaned-approval cleanup needed since nothing is deleted). §9 testing → Task 18, plus every task's own unit tests. §11 (coverage-scoped access, added 2026-08-18) → Tasks 19-20. Nothing in the design spec is unaddressed.

**Placeholder scan:** Every task (1-20) contains complete, real code for every step, with two explicit and justified exceptions: Task 18's integration-test seed block and four test bodies are left as precise structured comments rather than inlined code, because correctly inlining them requires reading two other test files' exact current field-by-field construction first (see that task's embedded note); Task 20 Step 2 flags one unverified assumption (`EmployeeListItemResponse`'s exact property names) rather than guessing them, since this plan's research phase never re-read that specific DTO. Both are flagged dependencies, not missing thought — hardcoding an unverified guess would be less honest than naming what needs a quick check first.

**Type consistency:** `OffboardingRecordStatuses`, `BypassRequestStatuses`, `EmployeeChecklistTaskStatuses`, `EmploymentStatusIds.Offboarding/Resigned/Terminated`, `OffboardingRecordId`, `PriorTaskStatus`, `employees:offboard`, and every repository method signature introduced in Tasks 2-8, 13, and 19-20 are used identically across every consuming task — cross-checked `GetTrackedByIdAsync`'s two distinct overloads (`IOffboardingRecordRepository`'s tenant+id form from Task 2, and `IEmployeeChecklistTaskRepository`'s tenant+id form from Task 13, deliberately without an employeeId parameter since Task 15's cross-employee approve/reject flow can't supply one) are called consistently with the right one in each task that uses them. Confirmed Task 19's `IEmployeeOffboardingCoverageGuard` is distinct from Task 17's `IEmployeeOffboardingLockGuard` — same naming family, different concern (coverage vs. post-completion read-only), both real and both referenced only where intended.
