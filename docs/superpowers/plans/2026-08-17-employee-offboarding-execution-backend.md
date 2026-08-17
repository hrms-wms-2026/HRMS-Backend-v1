# Employee Offboarding Execution — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend for the 6-step employee offboarding flow: `offboarding_records` (documented but never built), offboarding-only bypass/penalty/category fields on the existing checklist-template/task entities, a new bypass-approval table, employee-checklist-task CRUD/complete/bypass endpoints, and offboarding completion (employment status, session revocation, user deactivation, read-only lock).

**Architecture:** New `OffboardingRecord`/`OffboardingTaskBypassRequest` entities under `src/ONEVO.Domain/Features/CoreHr/Offboarding/Entities/`, new repositories following the exact `EfChecklistTemplateRepository`-style pattern already in this codebase, three new thin controllers (`EmployeeOffboardingController`, `EmployeeChecklistTasksController`, `OffboardingBypassRequestsController`) under `Api/Controllers/Tenant/CoreHr/`, all mutations via MediatR commands returning `Result<T>`/`Result`. Extends (does not replace) the existing generic `checklist_templates`/`employee_checklist_tasks` entities and `ChecklistTaskJsonContract`.

**Tech Stack:** ASP.NET Core 8 Web API, EF Core (Npgsql, snake_case, Postgres RLS), MediatR CQRS, FluentValidation, xUnit + FluentAssertions + Moq (unit, EF InMemory), Testcontainers.PostgreSQL (integration).

## Global Constraints

- Work only in `C:\onevoNew\HRMS-Backend-v1`. Do not touch the frontend repo. Do not commit or push beyond staging files per-task (the executor stages and commits each task's own files; leave any final push to the user).
- `tenantId` is never accepted from a request body or query string — always `ICurrentUser.TenantId`.
- Every controller action carries `[RequirePermission("employees:read")]` or `[RequirePermission("employees:write")]` — no new permission code is introduced (verified against `PermissionSeeder.cs`: the existing granularity is reused).
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
- Produces: `EmployeeChecklistTask.IsBypassable`/`BypassPenaltyDescription`/`Category`; `EmployeeChecklistTaskStatuses.Pending/InProgress/Completed/Bypassed`; `ChecklistTaskDefinition` gains three trailing optional-default parameters (`IsBypassable = false, BypassPenaltyDescription = null, Category = null`) so every existing 7-positional-argument test call site in `ChecklistTaskJsonContractTests.cs` keeps compiling unchanged — Task 6 and Task 10 (instantiation) consume these.

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
```

- [ ] **Step 3: Generate the column-only migration**

Run:
```bash
dotnet ef migrations add AddOffboardingFieldsToEmployeeChecklistTask --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```

Rename to `20260817140000_AddOffboardingFieldsToEmployeeChecklistTask.cs` if needed. This is a column-only change on an already-RLS-covered table (`employee_checklist_tasks` already has `tenant_isolation` from its original migration) — verify the generated `Up()` contains only three `AddColumn` calls and no `TenantTables` array is needed (per the Global Constraints rule).

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
    /// <summary>Step 1 - start an employee's offboarding. Fails 409 if one is already open.</summary>
    [HttpPost]
    [RequirePermission("employees:write")]
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

**Remaining tasks (10-17) continue in the same vertical-slice/TDD shape as Tasks 9 and complete the plan:**

- **Task 10 — Get Offboarding + list offboarding checklist matches** (`GET .../offboarding`, `GET .../offboarding/checklist-matches`): query + handler + controller actions on `EmployeeOffboardingController`, using `IOffboardingRecordRepository.GetLatestByEmployeeIdAsync` (Task 2) and `IChecklistTemplateRepository.ListOffboardingMatchesAsync` (Task 7).
- **Task 11 — Select Checklist** (`POST .../offboarding/select-checklist`): command + handler using `EfEmployeeChecklistTaskRepository.InstantiateAsync` (Task 6) anchored on `LastWorkingDate`, sets `ChecklistTemplateId`/`Status=InProgress` on the tracked `OffboardingRecord` from `GetTrackedByIdAsync` (Task 2).
- **Task 12 — Cancel Offboarding** (`POST .../offboarding/cancel`): command + handler reverting `Employee.EmploymentStatusId` to `OffboardingRecord.PreviousEmploymentStatusId`, setting `Status=Cancelled`, only from `Initiated`/`InProgress`.
- **Task 13 — List and patch employee checklist tasks** (`GET .../checklist-tasks`, `PATCH .../checklist-tasks/{taskId}`): new `EmployeeChecklistTasksController` (route `api/v1/employees/{employeeId}/checklist-tasks`); the patch handler needs a new `IEmployeeChecklistTaskRepository.GetTrackedByIdAsync(tenantId, employeeId, taskId, ct)` method (doesn't exist today — only `ListByEmployeeAsync` does) added to that interface/implementation as part of this task.
- **Task 14 — Complete task and create bypass request** (`POST .../checklist-tasks/{taskId}/complete`, `POST .../checklist-tasks/{taskId}/bypass-requests`): the complete handler blocks (409) if `IOffboardingTaskBypassRequestRepository.HasPendingForTaskAsync` is true (Task 3); the bypass-request handler validates `task.IsBypassable`, `approverId != requestedById`, and the one-pending-per-task partial unique index (catch the DB constraint violation as a `Conflict`, matching the `UniqueConstraintConflictException` pattern from `ChangeEmployeePositionCommandHandler`).
- **Task 15 — Approve/reject bypass request, list my pending** (`POST /api/v1/offboarding-bypass-requests/{id}/approve`, `.../reject`, `GET /api/v1/offboarding-bypass-requests?status=pending`): new `OffboardingBypassRequestsController`; approve/reject handlers enforce `CurrentUser.UserId == request.ApproverId`, approve sets the task to `Bypassed`, reject/cancel returns it to its prior status (track prior status on the bypass request at creation time, or re-derive from whether the task had any `CompletedAt`/other in-flight state — simplest: store `PriorTaskStatus` as an extra field on `OffboardingTaskBypassRequest` set at creation, since reject must restore the exact status the task was in, not assume `Pending`).
- **Task 16 — Complete Employee Exit** (`POST .../offboarding/complete`): the gate check — "every `IsRequired` task for this offboarding is `Completed` or `Bypassed`" — is written as a standalone pure function (e.g. `static bool AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTask> tasks)`) with its own unit tests covering all-done/one-pending/non-required-still-pending cases, called from inside the full handler that also does the `EmploymentStatusId` mapping (`Resigned` if reason is `resignation` else `Terminated`), `TerminationDate`, `User.IsActive = false` (via `IUserRepository.GetByIdAsync` + `IUnitOfWork.SaveChangesAsync`, per the verified-tracked finding in the design spec §3), `ISessionRepository.RevokeAllActiveByUserIdAsync` (Task 8), and `OffboardingRecord.Status = Completed`/`CompletedAt`.
- **Task 17 — Read-only guard on `ChangeEmployeePositionCommandHandler`**: a small `IEmployeeOffboardingLockGuard.EnsureMutable(tenantId, employeeId, ct) -> Task<Result?>` (returns `null` when mutable, a `Conflict` `Result` otherwise) checking `Employee.EmploymentStatusId` against `Resigned`/`Terminated`; call it as the first line inside `ChangeEmployeePositionCommandHandler.Handle` after the employee is loaded, returning its `Result` immediately if non-null.
- **Task 18 — Integration test suite** (`tests/ONEVO.Tests.Integration/CoreHr/Offboarding/OffboardingExecutionIntegrationTests.cs`, following the `ChecklistTemplatesIntegrationTests.cs` Testcontainers.PostgreSQL/`IAsyncLifetime` pattern exactly): full Start→Select-Checklist→Complete-tasks→Complete-Exit happy path; cancel-then-restart; bypass request→approve→task `Bypassed`; bypass request→reject→task returns to prior status; `change-position` returns 409 after completion; RLS coverage confirmed automatically once Task 4's migration is in place (no test-file changes needed for that part — `TenantIsolationArchitectureTests` picks it up).

Each of Tasks 10-18 should be written out with the same Files/Interfaces/failing-test/implementation/passing-test/commit structure as Task 9 before execution begins — this section names the exact classes, methods, and sequencing so that expansion is mechanical, not a design decision.

## Self-Review

**Spec coverage:** §4.1 (offboarding_records + gaps) → Tasks 2, 4. §4.2 (task fields) → Task 5. §4.3 (bypass table) → Tasks 3-4. §5.1 → Task 9. §5.2 → Tasks 7, 11. §5.3 → Tasks 13-15. §5.4 → Task 12. §5.5 → Task 16. §6 (API surface) → Tasks 9-16 collectively. §7 (read-only guard) → Task 17. §8 edge cases (completion race, scoping rule) → Task 14's blocking checks. §9 testing → Task 18. Nothing in the design spec is unaddressed.

**Placeholder scan:** Tasks 1-9 contain complete, real code for every step. Tasks 10-18 are intentionally left as a structured expansion outline rather than fully-inlined TDD steps, given this plan's size — each names exact classes/methods/files/interfaces (not vague direction), and the pattern to expand them into full Task-9-style steps is mechanical and demonstrated five times over by Tasks 2-3, 5-7, and 9. A reviewer picking up Task 10 has everything needed except the literal code, which follows the same shape as every completed task above it.

**Type consistency:** `OffboardingRecordStatuses`, `BypassRequestStatuses`, `EmployeeChecklistTaskStatuses`, `EmploymentStatusIds.Offboarding/Resigned`, and every repository method signature introduced in Tasks 2-8 are used identically in Task 9 and named identically in the Tasks 10-18 outline — no renamed types across the plan.
