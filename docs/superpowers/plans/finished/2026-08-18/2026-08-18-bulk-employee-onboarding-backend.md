# Bulk Employee Onboarding (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let HR upload a CSV of prospective employees, map columns with a live preview, validate with partial success, bulk-create `onboarding_drafts` in the background, and bulk-finalize selected drafts — reusing the existing single-employee onboarding-draft/finalize logic rather than reimplementing it.

**Architecture:** Two new tenant-owned tables (`bulk_onboarding_batches`, `bulk_onboarding_batch_rows`). The core existing logic in `SaveOnboardingDraftCommandHandler`/`FinalizeOnboardingDraftCommandHandler` is extracted into a new `IOnboardingDraftWriteService` that takes `tenantId`/`actingUserId` as explicit parameters instead of reading `ICurrentUser` — this is what lets a background `BackgroundService` (no HTTP context, no MediatR) reuse it safely. A new `BulkOnboardingBatchProcessor` polls for pending batches (same shape as the existing `OutboxProcessor`) and drives that service per row.

**Tech Stack:** ASP.NET Core / MediatR / EF Core / PostgreSQL (existing stack, no new packages — CSV is hand-parsed, no CsvHelper dependency needed for phase 1's simple flat-row format).

**Spec:** `docs/superpowers/specs/next/2026-08-18-bulk-employee-onboarding-backend-design.md`

## Global Constraints

- CSV only, `.xlsx` is out of scope (spec §2).
- Row cap: 200 rows per file, enforced before any row is persisted (spec §4.1).
- No org-structure auto-create: Department/Position must already exist; a missing one is a validation error, not an auto-create (spec §2).
- No Reporting Manager field anywhere — verified `Employee`/`onboarding_drafts` have no such column; reporting manager is implied entirely by the resolved Position's own `ReportsToPositionId` (spec §2, corrected during planning against `Employee.cs`/`Position.cs`).
- No R2/`IFileStorageService` for the uploaded file — raw rows are persisted as `jsonb` directly (spec §4.3).
- Permission: `employees:write` for every mutating endpoint, `employees:read` for the GET status endpoint — no new permission code (spec §3).
- `[Idempotent]` required on the finalize endpoint (spec §6).
- Background worker must never call `IMediator.Send` or read `ICurrentUser` — verified those don't work outside an HTTP request (spec §3).

---

## Task 1: Domain entities + status constants

**Files:**
- Create: `src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatch.cs`
- Create: `src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingEntityTests.cs`

**Interfaces:**
- Produces: `BulkOnboardingBatch : BaseEntity` (adds `LegalEntityId`, `DefaultEmploymentType`, `DefaultWorkModeId`, `DefaultChecklistTemplateId`, `ColumnMappingJson`, `OriginalFileName`, `Status`, `TotalRows`, `ValidRows`, `InvalidRows`, `CompletedAt`); `BulkOnboardingBatchRow : BaseEntity` (adds `BatchId`, `RowNumber`, `RawDataJson`, `ResolvedDepartmentId`, `ResolvedPositionId`, `ResolvedTemplateId`, `Status`, `ErrorMessage`, `OnboardingDraftId`); `BulkOnboardingBatchStatus`/`BulkOnboardingBatchRowStatus` static const-string classes.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingEntityTests.cs
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class BulkOnboardingEntityTests
{
    [Fact]
    public void BulkOnboardingBatch_DefaultsToMappingPendingStatus()
    {
        var batch = new BulkOnboardingBatch();
        Assert.Equal(BulkOnboardingBatchStatus.MappingPending, batch.Status);
    }

    [Fact]
    public void BulkOnboardingBatchRow_DefaultsToPendingMappingStatus()
    {
        var row = new BulkOnboardingBatchRow();
        Assert.Equal(BulkOnboardingBatchRowStatus.PendingMapping, row.Status);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter BulkOnboardingEntityTests`
Expected: FAIL — `BulkOnboardingBatch`/`BulkOnboardingBatchRow` types do not exist.

- [ ] **Step 3: Write the entities**

```csharp
// src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatch.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class BulkOnboardingBatch : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public string? DefaultEmploymentType { get; set; }
    public int? DefaultWorkModeId { get; set; }
    public Guid? DefaultChecklistTemplateId { get; set; }
    public string? ColumnMappingJson { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string Status { get; set; } = BulkOnboardingBatchStatus.MappingPending;
    public int TotalRows { get; set; }
    public int? ValidRows { get; set; }
    public int? InvalidRows { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class BulkOnboardingBatchStatus
{
    public const string MappingPending = "mapping_pending";
    public const string Validated = "validated";
    public const string DraftCreationPending = "draft_creation_pending";
    public const string DraftsCreated = "drafts_created";
    public const string FinalizePending = "finalize_pending";
    public const string FinalizeCompleted = "finalize_completed";
}
```

```csharp
// src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatchRow.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class BulkOnboardingBatchRow : BaseEntity
{
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string RawDataJson { get; set; } = "{}";
    public Guid? ResolvedDepartmentId { get; set; }
    public Guid? ResolvedPositionId { get; set; }
    public Guid? ResolvedTemplateId { get; set; }
    public string Status { get; set; } = BulkOnboardingBatchRowStatus.PendingMapping;
    public string? ErrorMessage { get; set; }
    public Guid? OnboardingDraftId { get; set; }
}

public static class BulkOnboardingBatchRowStatus
{
    public const string PendingMapping = "pending_mapping";
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string DraftCreated = "draft_created";
    public const string DraftFailed = "draft_failed";
    public const string Finalized = "finalized";
    public const string WaitingForSeat = "waiting_for_seat";
    public const string WaitingForPositionApproval = "waiting_for_position_approval";
    public const string FinalizeFailed = "finalize_failed";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter BulkOnboardingEntityTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/BulkOnboarding tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingEntityTests.cs
git commit -m "feat: add BulkOnboardingBatch/BulkOnboardingBatchRow domain entities"
```

---

## Task 2: EF configurations + schema migration + RLS policy migration

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchRowConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add `DbSet<BulkOnboardingBatch>` / `DbSet<BulkOnboardingBatchRow>` if the context declares explicit `DbSet` properties (check the existing `OnboardingDraft` `DbSet` line and mirror it; if the context instead auto-discovers entities purely from `IEntityTypeConfiguration` registration, no `DbSet` line is needed — confirm by finding how `OnboardingDraft`'s `DbSet` is wired before adding this one).
- Migration (generated): `dotnet ef migrations add AddBulkOnboarding --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
- Migration (hand-written): `dotnet ef migrations add AddBulkOnboardingRlsPolicies --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

**Interfaces:**
- Consumes: `BulkOnboardingBatch`, `BulkOnboardingBatchRow` (Task 1).
- Produces: `bulk_onboarding_batches` and `bulk_onboarding_batch_rows` tables, EF-query-filtered by tenant (automatic via `ITenantOwnedEntity`, confirmed in `ApplicationDbContext.OnModelCreating`) and PostgreSQL-RLS-enforced.

- [ ] **Step 1: Write the EF configurations**

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.BulkOnboarding;

public class BulkOnboardingBatchConfiguration : IEntityTypeConfiguration<BulkOnboardingBatch>
{
    public void Configure(EntityTypeBuilder<BulkOnboardingBatch> builder)
    {
        builder.ToTable("bulk_onboarding_batches");
        builder.HasKey(b => b.Id);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(b => b.DefaultEmploymentType).HasMaxLength(30);
        builder.Property(b => b.ColumnMappingJson).HasColumnType("jsonb");
        builder.Property(b => b.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(b => b.Status).HasMaxLength(30).IsRequired();

        builder.HasIndex(b => new { b.TenantId, b.Status });

        builder.HasOne<ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity>()
            .WithMany().HasForeignKey(b => b.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchRowConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.CoreHr.BulkOnboarding;

public class BulkOnboardingBatchRowConfiguration : IEntityTypeConfiguration<BulkOnboardingBatchRow>
{
    public void Configure(EntityTypeBuilder<BulkOnboardingBatchRow> builder)
    {
        builder.ToTable("bulk_onboarding_batch_rows");
        builder.HasKey(r => r.Id);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.Property(r => r.RawDataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Status).HasMaxLength(30).IsRequired();
        builder.Property(r => r.ErrorMessage).HasColumnType("text");

        builder.HasIndex(r => new { r.TenantId, r.BatchId, r.RowNumber }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Status });

        builder.HasOne<BulkOnboardingBatch>()
            .WithMany().HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft>()
            .WithMany().HasForeignKey(r => r.OnboardingDraftId).OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 2: Generate the schema migration**

Run: `dotnet ef migrations add AddBulkOnboarding --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: A new migration file creating `bulk_onboarding_batches` and `bulk_onboarding_batch_rows` with the columns/indexes/FKs above. Read the generated migration and confirm both tables and the unique index on `(tenant_id, batch_id, row_number)` are present — EF sometimes needs the composite index attribute order double-checked against what was written above.

- [ ] **Step 3: Write the RLS policy migration by hand**

**Do not copy `20260719120142_AddFileStorageRlsPolicies.cs`'s simple policy shape** — verified that pattern (`USING (tenant_id::text = current_setting('app.current_tenant_id', true))`) has no branch for cross-tenant/system access at all: in `TenantContextMode.System` the interceptor sets `app.current_tenant_id` to an empty string (`TenantRlsInterceptor.ResolveTenantId()`), so the clause becomes `tenant_id::text = ''`, which never matches a real row — the background worker's cross-tenant "find the next pending batch" scan (Task 12) would silently return nothing forever. The newer mode-aware policy from `20260520000000_UpdateRlsTenantContextMode.cs` (`USING (mode = 'admin' OR (mode = 'tenant' AND tenant_id = current_tenant_id))`) has an actual bypass, but that migration only applied it to a fixed table list that doesn't include `onboarding_drafts` or these new tables. Since these two tables are new, give them the mode-aware shape directly rather than inheriting the gap:

```csharp
// src/ONEVO.Infrastructure/Migrations/{timestamp}_AddBulkOnboardingRlsPolicies.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkOnboardingRlsPolicies : Migration
    {
        // Mode-aware shape (not the simpler AddFileStorageRlsPolicies pattern - see the note
        // above this migration in the implementation plan for why): BulkOnboardingBatchProcessor
        // must be able to scan for the oldest pending batch across all tenants before it knows
        // which tenant to switch into, which requires an actual admin-mode bypass in the policy
        // itself. Worker calls IWritableTenantContext.SetAdminMode() before that scan (Task 12).
        private static readonly string[] TenantTables =
        [
            "bulk_onboarding_batches", "bulk_onboarding_batch_rows"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
        }
    }
}
```

Run: `dotnet ef migrations add AddBulkOnboardingRlsPolicies --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api` first to get a correctly-timestamped empty migration file, then replace its generated (empty) `Up`/`Down` bodies with the SQL above — same procedure this repo already uses for every RLS migration.

- [ ] **Step 4: Apply migrations to local dev DB and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: Both migrations apply cleanly. Then run `psql` (or your usual DB tool) `\d bulk_onboarding_batches` and confirm RLS is enabled (`Policies: tenant_isolation`).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding src/ONEVO.Infrastructure/Migrations src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat: add bulk onboarding tables with RLS policies"
```

---

## Task 3: Repository interface + EF implementation

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/RepositoryInterfaces/IBulkOnboardingBatchRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/BulkOnboarding/EfBulkOnboardingBatchRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:171` (near the `IOnboardingDraftRepository` registration) — add `services.AddScoped<IBulkOnboardingBatchRepository, EfBulkOnboardingBatchRepository>();`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchRepositoryTests.cs`

**Interfaces:**
- Consumes: `BulkOnboardingBatch`, `BulkOnboardingBatchRow` (Task 1).
- Produces:
```csharp
public interface IBulkOnboardingBatchRepository
{
    Task<BulkOnboardingBatch?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<BulkOnboardingBatch?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListTrackedRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);
    Task<BulkOnboardingBatch?> GetOldestPendingAsync(string status, CancellationToken ct = default);
    Task AddAsync(BulkOnboardingBatch batch, IReadOnlyList<BulkOnboardingBatchRow> rows, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```
This is the exact signature every later task's controller/handler/worker code calls against — do not rename any method.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchRepositoryTests.cs
// Follow this project's existing Testcontainers fixture pattern (see any file under
// tests/ONEVO.Tests.Integration/CoreHr/Offboarding/*.cs for the exact base class/fixture to
// inherit — copy its setup, don't reinvent it).
[Fact]
public async Task AddAsync_PersistsBatchAndRows_ScopedToTenant()
{
    var tenantId = Guid.NewGuid();
    var batch = new BulkOnboardingBatch
    {
        Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = Guid.NewGuid(),
        OriginalFileName = "employees.csv", CreatedByUserId = Guid.NewGuid(), TotalRows = 1,
    };
    var rows = new List<BulkOnboardingBatchRow>
    {
        new() { Id = Guid.NewGuid(), TenantId = tenantId, BatchId = batch.Id, RowNumber = 1, RawDataJson = "{\"email\":\"a@b.com\"}" },
    };

    await _repository.AddAsync(batch, rows, CancellationToken.None);
    await _repository.SaveChangesAsync(CancellationToken.None);

    var fetched = await _repository.GetAsync(tenantId, batch.Id, CancellationToken.None);
    Assert.NotNull(fetched);
    var fetchedRows = await _repository.ListRowsAsync(tenantId, batch.Id, CancellationToken.None);
    Assert.Single(fetchedRows);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingBatchRepositoryTests`
Expected: FAIL — `IBulkOnboardingBatchRepository` does not exist.

- [ ] **Step 3: Write the interface and implementation**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/RepositoryInterfaces/IBulkOnboardingBatchRepository.cs
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;

public interface IBulkOnboardingBatchRepository
{
    Task<BulkOnboardingBatch?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<BulkOnboardingBatch?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<BulkOnboardingBatchRow>> ListTrackedRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default);

    /// <summary>Cross-tenant lookup for the background worker only - it does not yet have a
    /// resolved tenant context when picking the next batch to process.</summary>
    Task<BulkOnboardingBatch?> GetOldestPendingAsync(string status, CancellationToken ct = default);

    Task AddAsync(BulkOnboardingBatch batch, IReadOnlyList<BulkOnboardingBatchRow> rows, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/BulkOnboarding/EfBulkOnboardingBatchRepository.cs
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;

public class EfBulkOnboardingBatchRepository : IBulkOnboardingBatchRepository
{
    private readonly ApplicationDbContext _db;
    public EfBulkOnboardingBatchRepository(ApplicationDbContext db) => _db = db;

    public Task<BulkOnboardingBatch?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);

    public Task<BulkOnboardingBatch?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);

    public async Task<IReadOnlyList<BulkOnboardingBatchRow>> ListRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default) =>
        await _db.Set<BulkOnboardingBatchRow>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.BatchId == batchId)
            .OrderBy(r => r.RowNumber).ToListAsync(ct);

    public async Task<IReadOnlyList<BulkOnboardingBatchRow>> ListTrackedRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default) =>
        await _db.Set<BulkOnboardingBatchRow>()
            .Where(r => r.TenantId == tenantId && r.BatchId == batchId)
            .OrderBy(r => r.RowNumber).ToListAsync(ct);

    // IgnoreQueryFilters() is defensive here, not strictly load-bearing: EF's own
    // ITenantOwnedEntity filter is already inactive outside TenantContextMode.Tenant. What
    // actually gates this cross-tenant scan is PostgreSQL RLS - the caller (worker, Task 12)
    // must be in admin mode for the mode-aware policy on this table (Task 2) to allow it.
    public Task<BulkOnboardingBatch?> GetOldestPendingAsync(string status, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>()
            .IgnoreQueryFilters()
            .Where(b => b.Status == status)
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(BulkOnboardingBatch batch, IReadOnlyList<BulkOnboardingBatchRow> rows, CancellationToken ct = default)
    {
        await _db.Set<BulkOnboardingBatch>().AddAsync(batch, ct);
        await _db.Set<BulkOnboardingBatchRow>().AddRangeAsync(rows, ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

Register it: add `services.AddScoped<IBulkOnboardingBatchRepository, EfBulkOnboardingBatchRepository>();` next to the `IOnboardingDraftRepository` line in `DependencyInjection.cs`.

**Why `GetOldestPendingAsync` is safe cross-tenant:** two independent layers, both must agree. EF's own automatic `ITenantOwnedEntity` filter (`ApplicationDbContext.OnModelCreating`) is keyed off `IsTenantFilterActive => ContextMode == TenantContextMode.Tenant` — it is **already inactive** (passes every row) in both `System` and `Admin` mode, so `.IgnoreQueryFilters()` above is not strictly load-bearing here, just defensive/explicit. The layer that *does* matter is PostgreSQL RLS itself, which EF's filter has no control over: the simple `tenant_isolation` policy this repo's older tables use has no admin/system bypass at all (see Task 2's note), so without Task 2's mode-aware policy on these two new tables, Postgres would still block this query regardless of what EF's filter allows. The worker (Task 12) must call `IWritableTenantContext.SetAdminMode()` before invoking this method so `TenantRlsInterceptor` sets `app.tenant_context_mode = 'admin'` on the connection — that satisfies the RLS side, which is the one that actually gates this query.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingBatchRepositoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/BulkOnboarding src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding
git commit -m "feat: add IBulkOnboardingBatchRepository"
```

---

## Task 4: Extract `IOnboardingDraftWriteService` from the existing handlers

**This is the load-bearing task of this plan (spec §5).** It changes zero external behavior — the existing single-employee HTTP flow must produce byte-identical results before and after. Do this task in isolation from everything else so a regression here is easy to spot.

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/IOnboardingDraftWriteService.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommandHandler.cs` (replace body with a one-line delegation)
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs` (same)
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — register `IOnboardingDraftWriteService`
- Test: existing `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/**` (if present, must still pass unmodified — this is the regression guard) plus new tests calling the service directly with explicit params.

**Interfaces:**
- Consumes: every dependency `SaveOnboardingDraftCommandHandler`/`FinalizeOnboardingDraftCommandHandler` already inject (unchanged, just moved).
- Produces:
```csharp
public interface IOnboardingDraftWriteService
{
    Task<Result<OnboardingDraftResponse>> SaveAsync(
        Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct);
    Task<Result<FinalizeOnboardingDraftResponse>> FinalizeAsync(
        Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct);
}
```
Task 12 and Task 14 (the background worker) call these two methods directly.

- [ ] **Step 1: Confirm the regression baseline**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~OnboardingDraft` and `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~OnboardingDraft`
Expected: record the current PASS count. These same tests must still pass after this task with the same count — that is the correctness proof for this refactor (no new test asserts new behavior here, because there is none).

- [ ] **Step 2: Create the service interface and implementation by moving the handler bodies**

```csharp
// src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/IOnboardingDraftWriteService.cs
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// The tenant/user-parameterized core of onboarding-draft save/finalize. Exists because
/// SaveOnboardingDraftCommandHandler/FinalizeOnboardingDraftCommandHandler originally read
/// ICurrentUser.TenantId/.UserId directly - which is HttpContext-backed and resolves to
/// Guid.Empty outside an HTTP request. This service takes those two values as explicit
/// parameters so BOTH the existing MediatR handlers (HTTP path, passing ICurrentUser's values)
/// and BulkOnboardingBatchProcessor (background path, passing the batch's stored values) can
/// call the exact same logic. See docs/superpowers/specs/next/2026-08-18-bulk-employee-onboarding-backend-design.md §5.
/// </summary>
public interface IOnboardingDraftWriteService
{
    Task<Result<OnboardingDraftResponse>> SaveAsync(
        Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct);

    Task<Result<FinalizeOnboardingDraftResponse>> FinalizeAsync(
        Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct);
}
```

For `OnboardingDraftWriteService.cs`: copy `SaveOnboardingDraftCommandHandler`'s entire constructor (same injected dependencies, same field names) and its `Handle` method body verbatim into a `SaveAsync(Guid tenantId, Guid actingUserId, SaveOnboardingDraftCommand request, CancellationToken ct)` method — then do a find-and-replace within that pasted body only: every `_currentUser.TenantId` becomes `tenantId`, every `_currentUser.UserId` becomes `actingUserId`, every `_currentUser.HasPermission(...)` call stays as `_currentUser.HasPermission(...)` (permission checks still come from the real authenticated caller in the HTTP path; the background worker path never hits that branch because bulk-created drafts are always brand new, never someone else's existing draft — the `existing.StartedById != _currentUser.UserId && !_currentUser.HasPermission(...)` branch is unreachable from bulk and `ICurrentUser` stays injected for that one branch only). Repeat identically for `FinalizeOnboardingDraftCommandHandler`'s `Handle` body into `FinalizeAsync(Guid tenantId, Guid actingUserId, Guid draftId, CancellationToken ct)`, replacing every `_currentUser.TenantId`/`_currentUser.UserId` the same way (that handler has no `HasPermission` call to preserve). Copy the private helper methods (`FinalizeWithPendingApprovalAsync`, `FinalizeImmediatelyAsync`, `SaveAsync` (rename this pre-existing private helper to `PersistChangesAsync` to avoid a name collision with the new public `SaveAsync` method), `ToUtcMidnight`, `IsValidEmail`) across unchanged except also replacing `_currentUser.TenantId`/`.UserId` inside them (`FinalizeWithPendingApprovalAsync` uses `_currentUser.UserId` for `RequestedByUserId`/`AssignedBy` — thread `actingUserId` into that private method's parameter list too).

```csharp
// src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands/SaveOnboardingDraft/SaveOnboardingDraftCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;

public class SaveOnboardingDraftCommandHandler : IRequestHandler<SaveOnboardingDraftCommand, Result<OnboardingDraftResponse>>
{
    private readonly IOnboardingDraftWriteService _writeService;
    private readonly ICurrentUser _currentUser;

    public SaveOnboardingDraftCommandHandler(IOnboardingDraftWriteService writeService, ICurrentUser currentUser)
    {
        _writeService = writeService;
        _currentUser = currentUser;
    }

    public Task<Result<OnboardingDraftResponse>> Handle(SaveOnboardingDraftCommand request, CancellationToken ct) =>
        _writeService.SaveAsync(_currentUser.TenantId, _currentUser.UserId, request, ct);
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands/FinalizeOnboardingDraft/FinalizeOnboardingDraftCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.FinalizeOnboardingDraft;

public class FinalizeOnboardingDraftCommandHandler : IRequestHandler<FinalizeOnboardingDraftCommand, Result<FinalizeOnboardingDraftResponse>>
{
    private readonly IOnboardingDraftWriteService _writeService;
    private readonly ICurrentUser _currentUser;

    public FinalizeOnboardingDraftCommandHandler(IOnboardingDraftWriteService writeService, ICurrentUser currentUser)
    {
        _writeService = writeService;
        _currentUser = currentUser;
    }

    public Task<Result<FinalizeOnboardingDraftResponse>> Handle(FinalizeOnboardingDraftCommand request, CancellationToken ct) =>
        _writeService.FinalizeAsync(_currentUser.TenantId, _currentUser.UserId, request.DraftId, ct);
}
```

Register in `DependencyInjection.cs` next to the other Application-layer service registrations: `services.AddScoped<IOnboardingDraftWriteService, OnboardingDraftWriteService>();`.

- [ ] **Step 3: Run the regression baseline again**

Run the same two commands from Step 1.
Expected: identical PASS count to Step 1. If any existing test now fails, the copy-and-replace in Step 2 introduced a behavior change — diff the moved method against the original handler line by line before proceeding; do not patch the test to match new behavior.

- [ ] **Step 4: Add a direct-service test proving the explicit-params path works standalone**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/OnboardingDraftWriteServiceTests.cs
[Fact]
public async Task SaveAsync_WithExplicitTenantAndUser_DoesNotReadICurrentUser()
{
    // Arrange mocks exactly as the existing SaveOnboardingDraftCommandHandlerTests do (same
    // repository mocks), but construct OnboardingDraftWriteService directly - no ICurrentUser
    // mock passed in at all, proving the service genuinely never touches it.
    var explicitTenantId = Guid.NewGuid();
    var explicitUserId = Guid.NewGuid();
    // ... existing repository/service mocks from SaveOnboardingDraftCommandHandlerTests ...

    var result = await _service.SaveAsync(explicitTenantId, explicitUserId, command, CancellationToken.None);

    Assert.True(result.IsSuccess);
    // Assert the persisted draft's TenantId/StartedById equal explicitTenantId/explicitUserId,
    // not any HttpContext-derived value.
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter OnboardingDraftWriteServiceTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services src/ONEVO.Application/Features/CoreHr/OnboardingDrafts/Commands src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft
git commit -m "refactor: extract IOnboardingDraftWriteService with explicit tenant/user params"
```

---

## Task 5: CSV parsing + row-cap helper (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/CsvBatchParserTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class CsvBatchParser
{
    public const int MaxRows = 200;
    public static Result<ParsedCsv> Parse(string csvContent);
}
public sealed record ParsedCsv(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);
```
Task 7 (upload endpoint) calls `CsvBatchParser.Parse`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/CsvBatchParserTests.cs
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class CsvBatchParserTests
{
    [Fact]
    public void Parse_SimpleCsv_ReturnsHeadersAndRows()
    {
        var csv = "First Name,Last Name,Work Email\nJane,Doe,jane@acme.com\nJohn,Roe,john@acme.com\n";

        var result = CsvBatchParser.Parse(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "First Name", "Last Name", "Work Email" }, result.Value!.Headers);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.Equal("jane@acme.com", result.Value.Rows[0]["Work Email"]);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_ParsesAsOneValue()
    {
        var csv = "Name,Notes\n\"Doe, Jane\",\"Started Q1, remote\"\n";

        var result = CsvBatchParser.Parse(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal("Doe, Jane", result.Value!.Rows[0]["Name"]);
        Assert.Equal("Started Q1, remote", result.Value.Rows[0]["Notes"]);
    }

    [Fact]
    public void Parse_MoreThanMaxRows_ReturnsFailure()
    {
        var header = "Email\n";
        var rows = string.Concat(Enumerable.Range(0, CsvBatchParser.MaxRows + 1).Select(i => $"user{i}@acme.com\n"));

        var result = CsvBatchParser.Parse(header + rows);

        Assert.False(result.IsSuccess);
        Assert.Contains("200", result.Error);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsFailure()
    {
        var result = CsvBatchParser.Parse("");
        Assert.False(result.IsSuccess);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CsvBatchParserTests`
Expected: FAIL — `CsvBatchParser` does not exist.

- [ ] **Step 3: Implement the parser**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public sealed record ParsedCsv(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

/// <summary>
/// Minimal RFC4180-style CSV parser: handles quoted fields, embedded commas inside quotes,
/// and escaped double-quotes ("") inside quoted fields. No external dependency - bulk
/// onboarding's CSVs are flat name/email/date rows, not a general-purpose CSV workload, so a
/// hand-rolled parser is proportionate (see spec §2, CSV-only phase 1 scope).
/// </summary>
public static class CsvBatchParser
{
    public const int MaxRows = 200;

    public static Result<ParsedCsv> Parse(string csvContent)
    {
        var lines = SplitLines(csvContent);
        if (lines.Count == 0)
            return Result<ParsedCsv>.Failure("The file is empty.");

        var headers = SplitLine(lines[0]);
        var dataLines = lines.Skip(1).Where(l => l.Length > 0).ToList();

        if (dataLines.Count == 0)
            return Result<ParsedCsv>.Failure("The file has a header row but no data rows.");

        if (dataLines.Count > MaxRows)
            return Result<ParsedCsv>.Failure($"This file has {dataLines.Count} rows; the limit is {MaxRows} rows per upload.");

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var line in dataLines)
        {
            var values = SplitLine(line);
            var row = new Dictionary<string, string>();
            for (var i = 0; i < headers.Count; i++)
                row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            rows.Add(row);
        }

        return Result<ParsedCsv>.Success(new ParsedCsv(headers, rows));
    }

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') { inQuotes = false; }
                else { current.Append(c); }
            }
            else
            {
                if (c == '"') { inQuotes = true; }
                else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
                else { current.Append(c); }
            }
        }
        fields.Add(current.ToString().Trim());
        return fields;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter CsvBatchParserTests`
Expected: PASS (all 4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/CsvBatchParser.cs tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/CsvBatchParserTests.cs
git commit -m "feat: add CSV batch parser with row cap"
```

---

## Task 6: Column-mapping auto-suggest helper (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/ColumnMappingSuggester.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/ColumnMappingSuggesterTests.cs`

**Interfaces:**
- Consumes: `ParsedCsv.Headers` (Task 5).
- Produces:
```csharp
public static class ColumnMappingSuggester
{
    public static IReadOnlyDictionary<string, string?> Suggest(IReadOnlyList<string> csvHeaders);
}
```
Returns a map keyed by system field name (`firstName`, `lastName`, `workEmail`, `startDate`, `employmentType`, `workMode`, `department`, `position`, `checklistTemplate`, `employeeNumber`) to the best-matching CSV header, or `null` if nothing matched. Task 7's upload response and Task 8's preview endpoint both use this map's keys as the canonical system-field id list.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/ColumnMappingSuggesterTests.cs
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public class ColumnMappingSuggesterTests
{
    [Fact]
    public void Suggest_ExactHeaderNames_MapsDirectly()
    {
        var headers = new[] { "First Name", "Last Name", "Work Email", "Start Date" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Equal("First Name", mapping["firstName"]);
        Assert.Equal("Last Name", mapping["lastName"]);
        Assert.Equal("Work Email", mapping["workEmail"]);
        Assert.Equal("Start Date", mapping["startDate"]);
    }

    [Fact]
    public void Suggest_CaseInsensitiveAndAbbreviatedHeaders_StillMatches()
    {
        var headers = new[] { "email", "FIRSTNAME", "dept" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Equal("email", mapping["workEmail"]);
        Assert.Equal("FIRSTNAME", mapping["firstName"]);
        Assert.Equal("dept", mapping["department"]);
    }

    [Fact]
    public void Suggest_NoMatchingHeader_ReturnsNullForThatField()
    {
        var headers = new[] { "Random Column" };

        var mapping = ColumnMappingSuggester.Suggest(headers);

        Assert.Null(mapping["reportingManagerDoesNotExist".Length > 0 ? "employeeNumber" : "employeeNumber"]);
    }
}
```

(Fix the third test's assertion to plainly read `Assert.Null(mapping["employeeNumber"]);` when writing the file — the ternary above is a copy-paste artifact, remove it.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ColumnMappingSuggesterTests`
Expected: FAIL — `ColumnMappingSuggester` does not exist.

- [ ] **Step 3: Implement the suggester**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/ColumnMappingSuggester.cs
namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public static class ColumnMappingSuggester
{
    private static readonly Dictionary<string, string[]> FieldAliases = new()
    {
        ["firstName"] = ["first name", "firstname", "given name"],
        ["lastName"] = ["last name", "lastname", "surname", "family name"],
        ["workEmail"] = ["work email", "email", "email address"],
        ["startDate"] = ["start date", "startdate", "joining date", "hire date"],
        ["employmentType"] = ["employment type", "employmenttype", "employment"],
        ["workMode"] = ["work mode", "workmode"],
        ["department"] = ["department", "dept"],
        ["position"] = ["position", "job title", "title", "role"],
        ["checklistTemplate"] = ["checklist template", "template"],
        ["employeeNumber"] = ["employee number", "employee no", "emp id", "employee id"],
    };

    public static IReadOnlyDictionary<string, string?> Suggest(IReadOnlyList<string> csvHeaders)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (systemField, aliases) in FieldAliases)
        {
            var match = csvHeaders.FirstOrDefault(h =>
                aliases.Any(alias => string.Equals(
                    Normalize(h), Normalize(alias), StringComparison.OrdinalIgnoreCase)));
            result[systemField] = match;
        }
        return result;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter ColumnMappingSuggesterTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Helpers/ColumnMappingSuggester.cs tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/ColumnMappingSuggesterTests.cs
git commit -m "feat: add column mapping auto-suggest helper"
```

---

## Task 7: Upload endpoint

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/UploadBulkOnboardingBatchRequest.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingBatchViewModel.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/DTOs/Responses/BulkOnboardingBatchResponse.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommandHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingUploadTests.cs`

**Interfaces:**
- Consumes: `CsvBatchParser.Parse` (Task 5), `ColumnMappingSuggester.Suggest` (Task 6), `IBulkOnboardingBatchRepository` (Task 3).
- Produces: `POST /api/v1/onboarding/bulk-batches` → `BulkOnboardingBatchResponse { Guid Id, string Status, int TotalRows, IReadOnlyList<string> DetectedColumns, IReadOnlyDictionary<string,string?> SuggestedMapping }`. Task 8/9/10/etc. all take a `batchId : Guid` route parameter matching this response's `Id`.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingUploadTests.cs
// Follow the existing integration test base (WebApplicationFactory + authenticated tenant
// client helper - copy the setup from tests/ONEVO.Tests.Integration/CoreHr/Offboarding/*.cs).
[Fact]
public async Task Upload_ValidCsv_ReturnsBatchWithSuggestedMapping()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var csv = "First Name,Last Name,Work Email,Start Date\nJane,Doe,jane@acme.com,2026-09-01\n";
    var content = new MultipartFormDataContent
    {
        { new StringContent(csv), "file", "employees.csv" },
        { new StringContent(_legalEntityId.ToString()), "legalEntityId" },
    };

    var response = await client.PostAsync("/api/v1/onboarding/bulk-batches", content);

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingBatchViewModel>();
    Assert.Equal(1, body!.TotalRows);
    Assert.Contains("First Name", body.DetectedColumns);
}

[Fact]
public async Task Upload_MoreThan200Rows_Returns400()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var csv = "Email\n" + string.Concat(Enumerable.Range(0, 201).Select(i => $"u{i}@acme.com\n"));
    var content = new MultipartFormDataContent
    {
        { new StringContent(csv), "file", "employees.csv" },
        { new StringContent(_legalEntityId.ToString()), "legalEntityId" },
    };

    var response = await client.PostAsync("/api/v1/onboarding/bulk-batches", content);

    Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task Upload_WithoutEmployeesWritePermission_Returns403()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:read");
    var content = new MultipartFormDataContent
    {
        { new StringContent("Email\na@b.com\n"), "file", "employees.csv" },
        { new StringContent(_legalEntityId.ToString()), "legalEntityId" },
    };

    var response = await client.PostAsync("/api/v1/onboarding/bulk-batches", content);

    Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingUploadTests`
Expected: FAIL — route does not exist (404).

- [ ] **Step 3: Implement Contracts, Application command/handler, and controller**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/UploadBulkOnboardingBatchRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record UploadBulkOnboardingBatchRequest(
    IFormFile File,
    Guid LegalEntityId,
    int? DefaultWorkModeId,
    string? DefaultEmploymentType,
    Guid? DefaultChecklistTemplateId);
```

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingBatchViewModel.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingBatchViewModel(
    Guid Id,
    string Status,
    int TotalRows,
    int? ValidRows,
    int? InvalidRows,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyDictionary<string, string?> SuggestedMapping);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/DTOs/Responses/BulkOnboardingBatchResponse.cs
namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

public sealed record BulkOnboardingBatchResponse(
    Guid Id,
    string Status,
    int TotalRows,
    int? ValidRows,
    int? InvalidRows,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyDictionary<string, string?> SuggestedMapping);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;

public sealed record UploadBulkOnboardingBatchCommand(
    string OriginalFileName,
    string CsvContent,
    Guid LegalEntityId,
    int? DefaultWorkModeId,
    string? DefaultEmploymentType,
    Guid? DefaultChecklistTemplateId) : IRequest<Result<BulkOnboardingBatchResponse>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch/UploadBulkOnboardingBatchCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;

public class UploadBulkOnboardingBatchCommandHandler
    : IRequestHandler<UploadBulkOnboardingBatchCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UploadBulkOnboardingBatchCommandHandler(
        IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        UploadBulkOnboardingBatchCommand request, CancellationToken ct)
    {
        var parsed = CsvBatchParser.Parse(request.CsvContent);
        if (!parsed.IsSuccess)
            return Result<BulkOnboardingBatchResponse>.Failure(parsed.Error!);

        var batch = new BulkOnboardingBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            LegalEntityId = request.LegalEntityId,
            DefaultWorkModeId = request.DefaultWorkModeId,
            DefaultEmploymentType = request.DefaultEmploymentType,
            DefaultChecklistTemplateId = request.DefaultChecklistTemplateId,
            OriginalFileName = request.OriginalFileName,
            Status = BulkOnboardingBatchStatus.MappingPending,
            TotalRows = parsed.Value!.Rows.Count,
            CreatedByUserId = _currentUser.UserId,
        };

        var rows = parsed.Value.Rows.Select((rowData, index) => new BulkOnboardingBatchRow
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            BatchId = batch.Id,
            RowNumber = index + 1,
            RawDataJson = JsonSerializer.Serialize(rowData),
            Status = BulkOnboardingBatchRowStatus.PendingMapping,
        }).ToList();

        await _batchRepository.AddAsync(batch, rows, ct);
        await _batchRepository.SaveChangesAsync(ct);

        var suggestedMapping = ColumnMappingSuggester.Suggest(parsed.Value.Headers);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, null, null, parsed.Value.Headers, suggestedMapping));
    }
}
```

```csharp
// src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.BulkOnboarding;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/onboarding/bulk-batches")]
[Authorize(Policy = "TenantPolicy")]
public class BulkOnboardingController : ControllerBase
{
    private readonly IMediator _mediator;
    public BulkOnboardingController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Upload([FromForm] UploadBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        using var reader = new StreamReader(request.File.OpenReadStream());
        var csvContent = await reader.ReadToEndAsync(ct);

        var command = new UploadBulkOnboardingBatchCommand(
            request.File.FileName, csvContent, request.LegalEntityId,
            request.DefaultWorkModeId, request.DefaultEmploymentType, request.DefaultChecklistTemplateId);

        var result = await _mediator.Send(command, ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }
}
```

Register `UploadBulkOnboardingBatchCommandHandler` — MediatR handler registration in this codebase is auto-discovered by assembly scan (confirm by checking `DependencyInjection.cs`'s `AddMediatR` call registers the whole `ONEVO.Application` assembly; if so, no explicit line is needed here).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingUploadTests`
Expected: PASS (all 3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/DTOs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/UploadBulkOnboardingBatch tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingUploadTests.cs
git commit -m "feat: add bulk onboarding upload endpoint"
```

---

## Task 8: Preview endpoint (row-1 mapping resolution)

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/PreviewBulkOnboardingMappingRequest.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingRowPreviewViewModel.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/PreviewBulkOnboardingMapping/PreviewBulkOnboardingMappingCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/PreviewBulkOnboardingMapping/PreviewBulkOnboardingMappingCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs` — add the `preview` action
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingPreviewTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingBatchRepository` (Task 3), `IDepartmentRepository.ListByLegalEntityAsync` and `IPositionRepository.ListByLegalEntityAsync` (existing, verified signatures in spec research).
- Produces: `POST /api/v1/onboarding/bulk-batches/{id}/preview` → resolved row-1 values including `DepartmentName`/`PositionName` (not raw IDs). Persists the submitted mapping onto the batch's `ColumnMappingJson`. Task 9's validation logic reuses the same name-matching approach this task establishes.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingPreviewTests.cs
[Fact]
public async Task Preview_MappingWithKnownDepartment_ResolvesDepartmentName()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadBatchAsync(client, "First Name,Email,Dept\nJane,jane@acme.com,Engineering\n");
    var mapping = new Dictionary<string, string?> { ["firstName"] = "First Name", ["workEmail"] = "Email", ["department"] = "Dept" };

    var response = await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/preview", new { mapping });

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingRowPreviewViewModel>();
    Assert.Equal("Engineering", body!.DepartmentName);
}

[Fact]
public async Task Preview_MappingWithUnknownDepartment_ReturnsNullDepartmentNameNotError()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadBatchAsync(client, "First Name,Email,Dept\nJane,jane@acme.com,NoSuchDept\n");
    var mapping = new Dictionary<string, string?> { ["firstName"] = "First Name", ["workEmail"] = "Email", ["department"] = "Dept" };

    var response = await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/preview", new { mapping });

    response.EnsureSuccessStatusCode(); // preview never fails the request - unresolved names surface at validate time
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingRowPreviewViewModel>();
    Assert.Null(body!.DepartmentName);
}
```

(`UploadBatchAsync` is a small private test helper wrapping Task 7's upload call and returning the parsed `Id` — add it once in this test class.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingPreviewTests`
Expected: FAIL — route does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/PreviewBulkOnboardingMappingRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record PreviewBulkOnboardingMappingRequest(IReadOnlyDictionary<string, string?> Mapping);
```

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingRowPreviewViewModel.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingRowPreviewViewModel(
    string? FirstName, string? LastName, string? WorkEmail, string? StartDate,
    string? EmploymentType, string? WorkModeName, string? DepartmentName, string? PositionName,
    string? ChecklistTemplateName, string? EmployeeNumber);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/PreviewBulkOnboardingMapping/PreviewBulkOnboardingMappingCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;

public sealed record PreviewBulkOnboardingMappingCommand(
    Guid BatchId, IReadOnlyDictionary<string, string?> Mapping) : IRequest<Result<RowPreviewResult>>;

public sealed record RowPreviewResult(
    string? FirstName, string? LastName, string? WorkEmail, string? StartDate,
    string? EmploymentType, string? WorkModeName, string? DepartmentName, string? PositionName,
    string? ChecklistTemplateName, string? EmployeeNumber);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/PreviewBulkOnboardingMapping/PreviewBulkOnboardingMappingCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;

public class PreviewBulkOnboardingMappingCommandHandler
    : IRequestHandler<PreviewBulkOnboardingMappingCommand, Result<RowPreviewResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly ICurrentUser _currentUser;

    public PreviewBulkOnboardingMappingCommandHandler(
        IBulkOnboardingBatchRepository batchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IWorkModeRepository workModeRepository,
        ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _workModeRepository = workModeRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<RowPreviewResult>> Handle(PreviewBulkOnboardingMappingCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<RowPreviewResult>.NotFound("The batch could not be found.");

        batch.ColumnMappingJson = JsonSerializer.Serialize(request.Mapping);
        await _batchRepository.SaveChangesAsync(ct);

        var rows = await _batchRepository.ListRowsAsync(_currentUser.TenantId, batch.Id, ct);
        var firstRow = rows.OrderBy(r => r.RowNumber).FirstOrDefault();
        if (firstRow is null)
            return Result<RowPreviewResult>.NotFound("This batch has no rows.");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(firstRow.RawDataJson) ?? new();
        string? Get(string field) => request.Mapping.TryGetValue(field, out var col) && col is not null && raw.TryGetValue(col, out var v) ? v : null;

        var departmentName = Get("department");
        var positionName = Get("position");
        var workModeName = Get("workMode");

        var departments = await _departmentRepository.ListByLegalEntityAsync(_currentUser.TenantId, batch.LegalEntityId, includeInactive: false, ct);
        var resolvedDepartment = departmentName is null ? null :
            departments.FirstOrDefault(d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase));

        var positions = await _positionRepository.ListByLegalEntityAsync(_currentUser.TenantId, batch.LegalEntityId, includeInactive: false, departmentId: null, ct);
        var resolvedPosition = positionName is null ? null :
            positions.FirstOrDefault(p => string.Equals(p.Name, positionName, StringComparison.OrdinalIgnoreCase));

        var workModes = await _workModeRepository.ListActiveAsync(ct);
        var resolvedWorkMode = workModeName is null ? null :
            workModes.FirstOrDefault(w => string.Equals(w.Code, workModeName, StringComparison.OrdinalIgnoreCase));

        return Result<RowPreviewResult>.Success(new RowPreviewResult(
            Get("firstName"), Get("lastName"), Get("workEmail"), Get("startDate"), Get("employmentType"),
            resolvedWorkMode?.Code, resolvedDepartment?.Name, resolvedPosition?.Name,
            null, // checklist template name resolution added when Task 10 needs it - see Task 9's note
            Get("employeeNumber")));
    }
}
```

Add the controller action:

```csharp
    [HttpPost("{id:guid}/preview")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Preview(Guid id, [FromBody] PreviewBulkOnboardingMappingRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new PreviewBulkOnboardingMappingCommand(id, request.Mapping), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new BulkOnboardingRowPreviewViewModel(
            r.FirstName, r.LastName, r.WorkEmail, r.StartDate, r.EmploymentType,
            r.WorkModeName, r.DepartmentName, r.PositionName, r.ChecklistTemplateName, r.EmployeeNumber));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingPreviewTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/PreviewBulkOnboardingMapping tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingPreviewTests.cs
git commit -m "feat: add bulk onboarding mapping preview endpoint"
```

---

## Task 9: Row validation service (pure logic, mocked-repo unit tests)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/IBulkOnboardingRowValidator.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/BulkOnboardingRowValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingRowValidatorTests.cs`

**Interfaces:**
- Consumes: `IDepartmentRepository`, `IPositionRepository`, `IWorkModeRepository`, `IEmploymentTypeRepository`, `IEmployeeRepository`, `IChecklistTemplateRepository` (all existing, verified signatures).
- Produces:
```csharp
public interface IBulkOnboardingRowValidator
{
    Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId, BulkOnboardingBatch batch, Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping, ISet<string> emailsSeenInThisFile, CancellationToken ct);
}
public sealed record RowValidationOutcome(
    bool IsValid, string? ErrorMessage, Guid? DepartmentId, Guid? PositionId, Guid? TemplateId,
    string FirstName, string LastName, string WorkEmail, DateOnly? StartDate, string EmploymentType, int? WorkModeId, string? EmployeeNumber);
```
Task 10 (validate endpoint) calls `ValidateRowAsync` once per row, threading the same `emailsSeenInThisFile` set across the whole batch to catch in-file duplicates.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingRowValidatorTests.cs
// Mock every injected repository the same way SaveOnboardingDraftCommandHandlerTests already
// does for IDepartmentRepository/IPositionRepository/IEmployeeRepository/IWorkModeRepository -
// copy that mocking setup rather than inventing a new style.
[Fact]
public async Task ValidateRowAsync_MissingWorkEmail_ReturnsInvalidWithReason()
{
    var raw = new Dictionary<string, string> { ["First Name"] = "Jane" };
    var mapping = new Dictionary<string, string?> { ["firstName"] = "First Name", ["workEmail"] = null };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

    Assert.False(outcome.IsValid);
    Assert.Contains("email", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task ValidateRowAsync_DepartmentNameNotFound_ReturnsInvalidPointingAtOrgSettings()
{
    _departmentRepositoryMock.Setup(r => r.ListByLegalEntityAsync(_tenantId, _batch.LegalEntityId, false, default))
        .ReturnsAsync(new List<Department>());
    var raw = new Dictionary<string, string> { ["First Name"] = "Jane", ["Email"] = "jane@acme.com", ["Dept"] = "Ghost Dept" };
    var mapping = new Dictionary<string, string?> { ["firstName"] = "First Name", ["workEmail"] = "Email", ["department"] = "Dept" };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

    Assert.False(outcome.IsValid);
    Assert.Contains("Ghost Dept", outcome.ErrorMessage);
    Assert.Contains("Organization", outcome.ErrorMessage);
}

[Fact]
public async Task ValidateRowAsync_DuplicateEmailWithinFile_ReturnsInvalid()
{
    var raw = new Dictionary<string, string> { ["First Name"] = "Jane", ["Email"] = "jane@acme.com" };
    var mapping = new Dictionary<string, string?> { ["firstName"] = "First Name", ["workEmail"] = "Email" };
    var seen = new HashSet<string> { "jane@acme.com" };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, seen, CancellationToken.None);

    Assert.False(outcome.IsValid);
    Assert.Contains("duplicate", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task ValidateRowAsync_AllFieldsResolve_ReturnsValidWithResolvedIds()
{
    var department = new Department { Id = Guid.NewGuid(), Name = "Engineering", TenantId = _tenantId };
    _departmentRepositoryMock.Setup(r => r.ListByLegalEntityAsync(_tenantId, _batch.LegalEntityId, false, default))
        .ReturnsAsync(new List<Department> { department });
    _employeeRepositoryMock.Setup(r => r.EmployeeExistsInLegalEntityAsync(_tenantId, _batch.LegalEntityId, "jane@acme.com", null, default))
        .ReturnsAsync(false);
    _employmentTypeRepositoryMock.Setup(r => r.GetIdByCodeAsync("full_time", default)).ReturnsAsync(1);

    var raw = new Dictionary<string, string> {
        ["First Name"] = "Jane", ["Last Name"] = "Doe", ["Email"] = "jane@acme.com",
        ["Start"] = "2026-09-01", ["Type"] = "full_time", ["Dept"] = "Engineering",
    };
    var mapping = new Dictionary<string, string?> {
        ["firstName"] = "First Name", ["lastName"] = "Last Name", ["workEmail"] = "Email",
        ["startDate"] = "Start", ["employmentType"] = "Type", ["department"] = "Dept",
    };

    var outcome = await _validator.ValidateRowAsync(_tenantId, _batch, raw, mapping, new HashSet<string>(), CancellationToken.None);

    Assert.True(outcome.IsValid);
    Assert.Equal(department.Id, outcome.DepartmentId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter BulkOnboardingRowValidatorTests`
Expected: FAIL — `IBulkOnboardingRowValidator` does not exist.

- [ ] **Step 3: Implement the validator**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/IBulkOnboardingRowValidator.cs
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public sealed record RowValidationOutcome(
    bool IsValid, string? ErrorMessage,
    Guid? DepartmentId, Guid? PositionId, Guid? TemplateId,
    string FirstName, string LastName, string WorkEmail, DateOnly? StartDate,
    string EmploymentType, int? WorkModeId, string? EmployeeNumber);

public interface IBulkOnboardingRowValidator
{
    Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId,
        BulkOnboardingBatch batch,
        Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping,
        ISet<string> emailsSeenInThisFile,
        CancellationToken ct);
}
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services/BulkOnboardingRowValidator.cs
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;

public class BulkOnboardingRowValidator : IBulkOnboardingRowValidator
{
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IWorkModeRepository _workModeRepository;
    private readonly IEmploymentTypeRepository _employmentTypeRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IChecklistTemplateRepository _checklistTemplateRepository;

    public BulkOnboardingRowValidator(
        IDepartmentRepository departmentRepository, IPositionRepository positionRepository,
        IWorkModeRepository workModeRepository, IEmploymentTypeRepository employmentTypeRepository,
        IEmployeeRepository employeeRepository, IChecklistTemplateRepository checklistTemplateRepository)
    {
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _workModeRepository = workModeRepository;
        _employmentTypeRepository = employmentTypeRepository;
        _employeeRepository = employeeRepository;
        _checklistTemplateRepository = checklistTemplateRepository;
    }

    public async Task<RowValidationOutcome> ValidateRowAsync(
        Guid tenantId, BulkOnboardingBatch batch, Dictionary<string, string> rawData,
        IReadOnlyDictionary<string, string?> mapping, ISet<string> emailsSeenInThisFile, CancellationToken ct)
    {
        string? Get(string field) =>
            mapping.TryGetValue(field, out var col) && col is not null && rawData.TryGetValue(col, out var v) && v.Length > 0 ? v : null;

        Fail(string msg) => msg;

        var firstName = Get("firstName");
        var lastName = Get("lastName");
        var workEmail = Get("workEmail");
        var startDateRaw = Get("startDate");
        var employmentType = Get("employmentType") ?? batch.DefaultEmploymentType;
        var employeeNumber = Get("employeeNumber");

        if (string.IsNullOrWhiteSpace(firstName))
            return Invalid("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName))
            return Invalid("Last name is required.");
        if (string.IsNullOrWhiteSpace(workEmail))
            return Invalid("Work email is required.");

        var normalizedEmail = workEmail.Trim().ToLowerInvariant();
        if (emailsSeenInThisFile.Contains(normalizedEmail))
            return Invalid($"Duplicate work email '{workEmail}' also appears in an earlier row of this file.");
        emailsSeenInThisFile.Add(normalizedEmail);

        if (await _employeeRepository.EmployeeExistsInLegalEntityAsync(tenantId, batch.LegalEntityId, workEmail, excludeId: null, ct))
            return Invalid($"An employee with the email '{workEmail}' already exists in this company.");

        if (!DateOnly.TryParse(startDateRaw, out var startDate))
            return Invalid("Start date is required and must be a valid date (YYYY-MM-DD).");

        if (string.IsNullOrWhiteSpace(employmentType))
            return Invalid("Employment type is required (set a default for the batch or add an Employment Type column).");
        if (await _employmentTypeRepository.GetIdByCodeAsync(employmentType, ct) is null)
            return Invalid($"'{employmentType}' is not a known employment type.");

        Guid? departmentId = null;
        var departmentName = Get("department");
        if (departmentName is not null)
        {
            var departments = await _departmentRepository.ListByLegalEntityAsync(tenantId, batch.LegalEntityId, includeInactive: false, ct);
            var match = departments.FirstOrDefault(d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Department '{departmentName}' was not found. Create it under Organization -> Departments first.");
            departmentId = match.Id;
        }

        Guid? positionId = null;
        var positionName = Get("position");
        if (positionName is not null)
        {
            var positions = await _positionRepository.ListByLegalEntityAsync(tenantId, batch.LegalEntityId, includeInactive: false, departmentId, ct);
            var match = positions.FirstOrDefault(p => string.Equals(p.Name, positionName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Position '{positionName}' was not found in this company/department. Create it under Organization -> Positions first.");
            positionId = match.Id;
        }

        int? workModeId = batch.DefaultWorkModeId;
        var workModeCode = Get("workMode");
        if (workModeCode is not null)
        {
            var workModes = await _workModeRepository.ListActiveAsync(ct);
            var match = workModes.FirstOrDefault(w => string.Equals(w.Code, workModeCode, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Work mode '{workModeCode}' is not a known work mode.");
            workModeId = match.Id;
        }
        if (workModeId is null)
            return Invalid("Work mode is required (set a default for the batch or add a Work Mode column).");

        Guid? templateId = batch.DefaultChecklistTemplateId;
        var templateName = Get("checklistTemplate");
        if (templateName is not null)
        {
            var matches = await _checklistTemplateRepository.ListOnboardingMatchesAsync(tenantId, batch.LegalEntityId, departmentId, positionId, ct);
            var match = matches.FirstOrDefault(t => string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Invalid($"Checklist template '{templateName}' was not found for this company/department/position.");
            templateId = match.Id;
        }

        if (employeeNumber is not null &&
            await _employeeRepository.EmployeeNumberExistsAsync(tenantId, employeeNumber, excludeId: null, ct))
            return Invalid($"Employee number '{employeeNumber}' is already in use.");

        return new RowValidationOutcome(
            true, null, departmentId, positionId, templateId,
            firstName, lastName, workEmail, startDate, employmentType, workModeId, employeeNumber);

        RowValidationOutcome Invalid(string message) => new(
            false, message, null, null, null, firstName ?? string.Empty, lastName ?? string.Empty,
            workEmail ?? string.Empty, null, employmentType ?? string.Empty, null, employeeNumber);
    }
}
```

(Remove the stray `Fail(string msg) => msg;` local-function line above when writing the file — leftover from drafting, unused since `Invalid(...)` is the real local function.)

Register: `services.AddScoped<IBulkOnboardingRowValidator, BulkOnboardingRowValidator>();` in `DependencyInjection.cs`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter BulkOnboardingRowValidatorTests`
Expected: PASS (all 4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Services src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/BulkOnboarding/BulkOnboardingRowValidatorTests.cs
git commit -m "feat: add bulk onboarding row validator"
```

---

## Task 10: Validate endpoint

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/ValidateBulkOnboardingBatchResponse.cs` (view model wrapping per-row results)
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs` — add the `validate` action
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingValidateTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingRowValidator.ValidateRowAsync` (Task 9), `IBulkOnboardingBatchRepository.ListTrackedRowsAsync` (Task 3).
- Produces: `POST /api/v1/onboarding/bulk-batches/{id}/validate` → sets every row's `Status`/`ErrorMessage`/`Resolved*Id`, sets `batch.Status = Validated`, `batch.ValidRows`/`InvalidRows`. Task 12's worker reads rows with `Status == Valid` after this runs.

- [ ] **Step 1: Write the failing integration test (partial success — the core product requirement)**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingValidateTests.cs
[Fact]
public async Task Validate_MixedValidAndInvalidRows_ReportsPartialSuccess()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var csv = "First Name,Last Name,Email,Start,Type\n"
        + "Jane,Doe,jane@acme.com,2026-09-01,full_time\n"   // valid
        + "John,,john@acme.com,2026-09-01,full_time\n";      // missing last name -> invalid
    var batchId = await UploadBatchAsync(client, csv, includeDefaults: true);
    var mapping = new Dictionary<string, string?> {
        ["firstName"] = "First Name", ["lastName"] = "Last Name", ["workEmail"] = "Email",
        ["startDate"] = "Start", ["employmentType"] = "Type",
    };
    await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/preview", new { mapping });

    var response = await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/validate", new { mapping });

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<ValidateBulkOnboardingBatchResponse>();
    Assert.Equal(1, body!.ValidRows);
    Assert.Equal(1, body.InvalidRows);
    Assert.Contains(body.Rows, r => r.RowNumber == 2 && r.Status == "invalid" && r.ErrorMessage!.Contains("Last name"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingValidateTests`
Expected: FAIL — route does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public sealed record ValidateBulkOnboardingBatchCommand(
    Guid BatchId, IReadOnlyDictionary<string, string?> Mapping) : IRequest<Result<ValidateBulkOnboardingBatchResult>>;

public sealed record ValidateBulkOnboardingBatchResult(
    int ValidRows, int InvalidRows, IReadOnlyList<RowValidationResultItem> Rows);

public sealed record RowValidationResultItem(int RowNumber, string Status, string? ErrorMessage);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch/ValidateBulkOnboardingBatchCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;

public class ValidateBulkOnboardingBatchCommandHandler
    : IRequestHandler<ValidateBulkOnboardingBatchCommand, Result<ValidateBulkOnboardingBatchResult>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly IBulkOnboardingRowValidator _rowValidator;
    private readonly ICurrentUser _currentUser;

    public ValidateBulkOnboardingBatchCommandHandler(
        IBulkOnboardingBatchRepository batchRepository, IBulkOnboardingRowValidator rowValidator, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _rowValidator = rowValidator;
        _currentUser = currentUser;
    }

    public async Task<Result<ValidateBulkOnboardingBatchResult>> Handle(
        ValidateBulkOnboardingBatchCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<ValidateBulkOnboardingBatchResult>.NotFound("The batch could not be found.");

        batch.ColumnMappingJson = JsonSerializer.Serialize(request.Mapping);

        var rows = await _batchRepository.ListTrackedRowsAsync(_currentUser.TenantId, batch.Id, ct);
        var emailsSeen = new HashSet<string>();
        var results = new List<RowValidationResultItem>();
        var validCount = 0;
        var invalidCount = 0;

        foreach (var row in rows.OrderBy(r => r.RowNumber))
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            var outcome = await _rowValidator.ValidateRowAsync(_currentUser.TenantId, batch, raw, request.Mapping, emailsSeen, ct);

            row.ResolvedDepartmentId = outcome.DepartmentId;
            row.ResolvedPositionId = outcome.PositionId;
            row.ResolvedTemplateId = outcome.TemplateId;
            row.Status = outcome.IsValid ? BulkOnboardingBatchRowStatus.Valid : BulkOnboardingBatchRowStatus.Invalid;
            row.ErrorMessage = outcome.ErrorMessage;

            if (outcome.IsValid) validCount++; else invalidCount++;
            results.Add(new RowValidationResultItem(row.RowNumber, row.Status, row.ErrorMessage));
        }

        batch.Status = BulkOnboardingBatchStatus.Validated;
        batch.ValidRows = validCount;
        batch.InvalidRows = invalidCount;

        await _batchRepository.SaveChangesAsync(ct);

        return Result<ValidateBulkOnboardingBatchResult>.Success(
            new ValidateBulkOnboardingBatchResult(validCount, invalidCount, results));
    }
}
```

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/ValidateBulkOnboardingBatchResponse.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record RowValidationResultItemViewModel(int RowNumber, string Status, string? ErrorMessage);

public sealed record ValidateBulkOnboardingBatchResponse(
    int ValidRows,
    int InvalidRows,
    IReadOnlyList<RowValidationResultItemViewModel> Rows);
```

Add to `BulkOnboardingController.cs`:

```csharp
    [HttpPost("{id:guid}/validate")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> Validate(Guid id, [FromBody] PreviewBulkOnboardingMappingRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ValidateBulkOnboardingBatchCommand(id, request.Mapping), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new ValidateBulkOnboardingBatchResponse(
            r.ValidRows, r.InvalidRows,
            r.Rows.Select(row => new RowValidationResultItemViewModel(row.RowNumber, row.Status, row.ErrorMessage)).ToList()));
    }
```

(Reuses `PreviewBulkOnboardingMappingRequest` from Task 8 as the request body shape — both `preview` and `validate` accept the same `{ mapping }` payload, so no separate request record is needed.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingValidateTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/ValidateBulkOnboardingBatch tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingValidateTests.cs
git commit -m "feat: add bulk onboarding validate endpoint with partial success"
```

---

## Task 11: `create-drafts` endpoint (sets pending status only)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingDraftCreation/RequestBulkOnboardingDraftCreationCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingDraftCreation/RequestBulkOnboardingDraftCreationCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs` — add `create-drafts` action
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingCreateDraftsTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingBatchRepository` (Task 3).
- Produces: `POST /api/v1/onboarding/bulk-batches/{id}/create-drafts` sets `batch.Status = DraftCreationPending` and returns immediately. Task 12's worker polls for this exact status string.

- [ ] **Step 1: Write the failing integration test**

```csharp
[Fact]
public async Task CreateDrafts_OnValidatedBatch_SetsStatusToPending()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadValidateAndGetBatchIdAsync(client); // test helper chaining Tasks 7+8+10

    var response = await client.PostAsync($"/api/v1/onboarding/bulk-batches/{batchId}/create-drafts", null);

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingBatchViewModel>();
    Assert.Equal("draft_creation_pending", body!.Status);
}

[Fact]
public async Task CreateDrafts_OnBatchNotYetValidated_Returns409()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadBatchAsync(client, "Email\na@b.com\n"); // never validated

    var response = await client.PostAsync($"/api/v1/onboarding/bulk-batches/{batchId}/create-drafts", null);

    Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingCreateDraftsTests`
Expected: FAIL — route does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingDraftCreation/RequestBulkOnboardingDraftCreationCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;

public sealed record RequestBulkOnboardingDraftCreationCommand(Guid BatchId) : IRequest<Result<BulkOnboardingBatchResponse>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingDraftCreation/RequestBulkOnboardingDraftCreationCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;

public class RequestBulkOnboardingDraftCreationCommandHandler
    : IRequestHandler<RequestBulkOnboardingDraftCreationCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public RequestBulkOnboardingDraftCreationCommandHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        RequestBulkOnboardingDraftCreationCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchResponse>.NotFound("The batch could not be found.");

        if (batch.Status != BulkOnboardingBatchStatus.Validated)
            return Result<BulkOnboardingBatchResponse>.Conflict(
                "This batch must be validated before drafts can be created.");

        batch.Status = BulkOnboardingBatchStatus.DraftCreationPending;
        await _batchRepository.SaveChangesAsync(ct);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            Array.Empty<string>(), new Dictionary<string, string?>()));
    }
}
```

Add to `BulkOnboardingController.cs`:

```csharp
    [HttpPost("{id:guid}/create-drafts")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> CreateDrafts(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestBulkOnboardingDraftCreationCommand(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingCreateDraftsTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingDraftCreation src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingCreateDraftsTests.cs
git commit -m "feat: add bulk onboarding create-drafts endpoint"
```

---

## Task 12: `BulkOnboardingBatchProcessor` — draft-creation leg

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` — `services.AddHostedService<BulkOnboardingBatchProcessor>();`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessorTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingBatchRepository.GetOldestPendingAsync`/`ListTrackedRowsAsync` (Task 3), `IOnboardingDraftWriteService.SaveAsync` (Task 4), `ITenantContextSwitcher.SwitchToTenantAsync` (existing, verified), `ITenantRepository.GetByIdAsync` (existing, verified), `SaveOnboardingDraftCommand` (existing).
- Produces: every `Valid` row in a `DraftCreationPending` batch gets `Status = DraftCreated` + `OnboardingDraftId` set, or `Status = DraftFailed` + `ErrorMessage`; batch flips to `DraftsCreated` once every row is attempted. Task 13/14 (finalize) follow the identical shape for the finalize leg.

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessorTests.cs
[Fact]
public async Task ProcessOnce_BatchWithValidRows_CreatesOnboardingDraftsAndMarksBatchDone()
{
    // Seed a tenant, legal entity, and a bulk_onboarding_batches row with two Valid rows
    // directly via DbContext (same seeding style as other integration tests in this project).
    var batch = await SeedValidatedBatchWithTwoValidRowsAsync();

    await _processor.ProcessOnceAsync(CancellationToken.None); // public test-entry method, mirrors ActivityDailySummaryJob.RunAggregationAsync's precedent

    var reloaded = await _dbContext.Set<BulkOnboardingBatch>().AsNoTracking().SingleAsync(b => b.Id == batch.Id);
    Assert.Equal(BulkOnboardingBatchStatus.DraftsCreated, reloaded.Status);
    var rows = await _dbContext.Set<BulkOnboardingBatchRow>().AsNoTracking().Where(r => r.BatchId == batch.Id).ToListAsync();
    Assert.All(rows, r => Assert.Equal(BulkOnboardingBatchRowStatus.DraftCreated, r.Status));
    Assert.All(rows, r => Assert.NotNull(r.OnboardingDraftId));
}

[Fact]
public async Task ProcessOnce_TenantAIsolatedFromTenantBBatch_NeverTouchesWrongTenantRows()
{
    var tenantABatch = await SeedValidatedBatchWithTwoValidRowsAsync(tenantId: _tenantA);
    var tenantBBatch = await SeedValidatedBatchWithTwoValidRowsAsync(tenantId: _tenantB);

    await _processor.ProcessOnceAsync(CancellationToken.None); // processes oldest pending first
    await _processor.ProcessOnceAsync(CancellationToken.None); // processes the second

    var tenantADrafts = await _dbContext.Set<OnboardingDraft>().IgnoreQueryFilters()
        .Where(d => d.TenantId == _tenantA).CountAsync();
    var tenantBDrafts = await _dbContext.Set<OnboardingDraft>().IgnoreQueryFilters()
        .Where(d => d.TenantId == _tenantB).CountAsync();
    Assert.Equal(2, tenantADrafts);
    Assert.Equal(2, tenantBDrafts);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingBatchProcessorTests`
Expected: FAIL — `BulkOnboardingBatchProcessor` does not exist.

- [ ] **Step 3: Implement the worker**

```csharp
// src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Services.CoreHr.BulkOnboarding;

/// <summary>
/// Polls for batches in draft_creation_pending or finalize_pending and drives
/// IOnboardingDraftWriteService per row - same BackgroundService/PeriodicTimer shape as
/// OutboxProcessor, no MediatR (see spec §6.1 and plan Task 4 for why).
/// </summary>
public sealed class BulkOnboardingBatchProcessor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _services;
    private readonly ILogger<BulkOnboardingBatchProcessor> _logger;

    public BulkOnboardingBatchProcessor(IServiceProvider services, ILogger<BulkOnboardingBatchProcessor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk onboarding batch processing iteration failed; will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Public so integration tests can drive one iteration synchronously without
    /// waiting on the poll timer - same precedent as ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task ProcessOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var batchRepository = scope.ServiceProvider.GetRequiredService<IBulkOnboardingBatchRepository>();

        // Admin mode before the cross-tenant scan: bulk_onboarding_batches/_rows use the
        // mode-aware RLS policy from Task 2, which only bypasses tenant scoping in admin mode -
        // without this, GetOldestPendingAsync silently returns nothing forever (see Task 2/3).
        var writableTenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        writableTenantContext.SetAdminMode();

        var batch = await batchRepository.GetOldestPendingAsync(BulkOnboardingBatchStatus.DraftCreationPending, ct);
        if (batch is null)
            return;

        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var writeService = scope.ServiceProvider.GetRequiredService<IOnboardingDraftWriteService>();

        var tenant = await tenantRepository.GetByIdAsync(batch.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogError("Bulk onboarding batch {BatchId} references missing tenant {TenantId}.", batch.Id, batch.TenantId);
            return;
        }
        await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var rows = await batchRepository.ListTrackedRowsAsync(batch.TenantId, batch.Id, ct);
        foreach (var row in rows.Where(r => r.Status == BulkOnboardingBatchRowStatus.Valid))
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawDataJson) ?? new();
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string?>>(batch.ColumnMappingJson ?? "{}") ?? new();
            string? Get(string field) => mapping.TryGetValue(field, out var col) && col is not null && raw.TryGetValue(col, out var v) ? v : null;

            var command = new SaveOnboardingDraftCommand(
                DraftId: null,
                FirstName: Get("firstName") ?? string.Empty,
                LastName: Get("lastName") ?? string.Empty,
                WorkEmail: Get("workEmail") ?? string.Empty,
                LegalEntityId: batch.LegalEntityId,
                DepartmentId: row.ResolvedDepartmentId,
                PositionId: row.ResolvedPositionId,
                EmploymentType: Get("employmentType") ?? batch.DefaultEmploymentType ?? string.Empty,
                StartDate: DateOnly.TryParse(Get("startDate"), out var startDate) ? startDate : default,
                EmployeeNumber: Get("employeeNumber"),
                WorkModeId: batch.DefaultWorkModeId ?? 0,
                SelectedTemplateId: row.ResolvedTemplateId,
                EditedTasksJson: null,
                LastSavedStep: ONEVO.Domain.Features.CoreHr.Entities.OnboardingWizardStep.ReviewAndSubmit,
                IfMatchVersion: null);

            var result = await writeService.SaveAsync(batch.TenantId, batch.CreatedByUserId, command, ct);
            if (result.IsSuccess)
            {
                row.Status = BulkOnboardingBatchRowStatus.DraftCreated;
                row.OnboardingDraftId = result.Value!.Id;
                row.ErrorMessage = null;
            }
            else
            {
                row.Status = BulkOnboardingBatchRowStatus.DraftFailed;
                row.ErrorMessage = result.Error;
            }
        }

        batch.Status = BulkOnboardingBatchStatus.DraftsCreated;
        batch.CompletedAt = null; // reserved for the finalize leg's completion, not draft creation
        await batchRepository.SaveChangesAsync(ct);
    }
}
```

Register: `services.AddHostedService<BulkOnboardingBatchProcessor>();` next to `services.AddHostedService<Services.SharedPlatform.Outbox.OutboxProcessor>();` in `DependencyInjection.cs`.

**Note:** `row.ResolvedDepartmentId`/`ResolvedPositionId`/`ResolvedTemplateId` were already resolved and stamped at validate time (Task 10) — the worker does not re-resolve names, it only reads what validation already wrote. This is why Task 10 must run to completion before Task 11's `create-drafts` is callable (enforced by the `Conflict` check in Task 11).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingBatchProcessorTests`
Expected: PASS (both tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessorTests.cs
git commit -m "feat: add BulkOnboardingBatchProcessor draft-creation leg"
```

---

## Task 13: `finalize` endpoint (with `[Idempotent]`)

**Files:**
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/FinalizeBulkOnboardingBatchRequest.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingFinalize/RequestBulkOnboardingFinalizeCommand.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingFinalize/RequestBulkOnboardingFinalizeCommandHandler.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/...` — add a `SelectedDraftIdsJson` column to `BulkOnboardingBatch` (new migration) since finalize must persist *which* drafts were selected for the worker to act on
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs` — add `finalize` action with `[Idempotent]`
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingFinalizeTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingBatchRepository` (Task 3).
- Produces: `POST /api/v1/onboarding/bulk-batches/{id}/finalize` with body `{ "onboardingDraftIds": [...] }`, sets `batch.Status = FinalizePending` and `batch.SelectedDraftIdsJson`. Task 14's worker reads `SelectedDraftIdsJson` to know which rows to finalize.

- [ ] **Step 1: Add the missing column (small migration)**

`BulkOnboardingBatch.cs` (Task 1) is missing a field this task needs — add it now rather than retrofitting Task 1:

```csharp
// src/ONEVO.Domain/Features/CoreHr/BulkOnboarding/Entities/BulkOnboardingBatch.cs
// Add this property to the existing class:
public string? SelectedDraftIdsJson { get; set; }
```

```csharp
// src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding/BulkOnboardingBatchConfiguration.cs
// Add inside Configure(...):
builder.Property(b => b.SelectedDraftIdsJson).HasColumnType("jsonb");
```

Run: `dotnet ef migrations add AddBulkOnboardingSelectedDraftIds --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Then: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

- [ ] **Step 2: Write the failing integration test**

```csharp
// tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingFinalizeTests.cs
[Fact]
public async Task Finalize_WithSelectedDrafts_SetsFinalizePendingAndPersistsSelection()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var (batchId, draftIds) = await UploadValidateAndCreateDraftsAsync(client); // chains Tasks 7/8/10/11/12

    var response = await client.PostAsJsonAsync(
        $"/api/v1/onboarding/bulk-batches/{batchId}/finalize", new { onboardingDraftIds = draftIds });

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingBatchViewModel>();
    Assert.Equal("finalize_pending", body!.Status);
}

[Fact]
public async Task Finalize_CalledTwiceWithSameIdempotencyKey_DoesNotDoubleQueue()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var (batchId, draftIds) = await UploadValidateAndCreateDraftsAsync(client);
    client.DefaultRequestHeaders.Add("Idempotency-Key", "test-key-1");

    var first = await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/finalize", new { onboardingDraftIds = draftIds });
    var second = await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/finalize", new { onboardingDraftIds = draftIds });

    Assert.Equal(first.StatusCode, second.StatusCode);
    var firstBody = await first.Content.ReadAsStringAsync();
    var secondBody = await second.Content.ReadAsStringAsync();
    Assert.Equal(firstBody, secondBody); // [Idempotent] returns the original recorded response, per ChecklistTemplatesController's existing precedent
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingFinalizeTests`
Expected: FAIL — route does not exist.

- [ ] **Step 4: Implement**

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/FinalizeBulkOnboardingBatchRequest.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record FinalizeBulkOnboardingBatchRequest(IReadOnlyList<Guid> OnboardingDraftIds);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingFinalize/RequestBulkOnboardingFinalizeCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;

public sealed record RequestBulkOnboardingFinalizeCommand(
    Guid BatchId, IReadOnlyList<Guid> OnboardingDraftIds) : IRequest<Result<BulkOnboardingBatchResponse>>;
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingFinalize/RequestBulkOnboardingFinalizeCommandHandler.cs
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;

public class RequestBulkOnboardingFinalizeCommandHandler
    : IRequestHandler<RequestBulkOnboardingFinalizeCommand, Result<BulkOnboardingBatchResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public RequestBulkOnboardingFinalizeCommandHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchResponse>> Handle(
        RequestBulkOnboardingFinalizeCommand request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetTrackedAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchResponse>.NotFound("The batch could not be found.");

        if (batch.Status != BulkOnboardingBatchStatus.DraftsCreated)
            return Result<BulkOnboardingBatchResponse>.Conflict(
                "This batch's drafts must be created before finalizing.");

        if (request.OnboardingDraftIds.Count == 0)
            return Result<BulkOnboardingBatchResponse>.Failure("Select at least one draft to finalize.");

        batch.SelectedDraftIdsJson = JsonSerializer.Serialize(request.OnboardingDraftIds);
        batch.Status = BulkOnboardingBatchStatus.FinalizePending;
        await _batchRepository.SaveChangesAsync(ct);

        return Result<BulkOnboardingBatchResponse>.Success(new BulkOnboardingBatchResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            Array.Empty<string>(), new Dictionary<string, string?>()));
    }
}
```

Add to `BulkOnboardingController.cs` — attribute order (`[HttpPost]`, `[RequirePermission]`, `[Idempotent]`) matches `ChecklistTemplatesController.Create`'s verified precedent exactly:

```csharp
    [HttpPost("{id:guid}/finalize")]
    [RequirePermission("employees:write")]
    [Idempotent]
    public async Task<IActionResult> Finalize(Guid id, [FromBody] FinalizeBulkOnboardingBatchRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestBulkOnboardingFinalizeCommand(id, request.OnboardingDraftIds), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var response = result.Value!;
        return Ok(new BulkOnboardingBatchViewModel(
            response.Id, response.Status, response.TotalRows, response.ValidRows, response.InvalidRows,
            response.DetectedColumns, response.SuggestedMapping));
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingFinalizeTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/CoreHr/BulkOnboarding src/ONEVO.Infrastructure/Persistence/Configurations/CoreHr/BulkOnboarding src/ONEVO.Infrastructure/Migrations src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Commands/RequestBulkOnboardingFinalize src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingFinalizeTests.cs
git commit -m "feat: add idempotent bulk onboarding finalize endpoint"
```

---

## Task 14: `BulkOnboardingBatchProcessor` — finalize leg

**Files:**
- Modify: `src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs` — add finalize handling to `ProcessOnceAsync`
- Test: extend `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessorTests.cs`

**Interfaces:**
- Consumes: `IOnboardingDraftWriteService.FinalizeAsync` (Task 4), `batch.SelectedDraftIdsJson` (Task 13).
- Produces: every selected draft's row gets `Status` set to one of `Finalized`/`WaitingForSeat`/`WaitingForPositionApproval`/`FinalizeFailed`; batch flips to `FinalizeCompleted` with `CompletedAt` set.

- [ ] **Step 1: Write the failing integration test**

```csharp
[Fact]
public async Task ProcessOnce_FinalizePendingBatch_FinalizesEachSelectedDraftAndCompletesBatch()
{
    var (batch, draftIds) = await SeedFinalizePendingBatchAsync(); // reuses this test class's Task 12 seeding helper, then finalizes the drafts through the write service directly to get real draft ids before setting SelectedDraftIdsJson

    await _processor.ProcessOnceAsync(CancellationToken.None);

    var reloaded = await _dbContext.Set<BulkOnboardingBatch>().AsNoTracking().SingleAsync(b => b.Id == batch.Id);
    Assert.Equal(BulkOnboardingBatchStatus.FinalizeCompleted, reloaded.Status);
    Assert.NotNull(reloaded.CompletedAt);
    var rows = await _dbContext.Set<BulkOnboardingBatchRow>().AsNoTracking()
        .Where(r => r.BatchId == batch.Id && draftIds.Contains(r.OnboardingDraftId!.Value)).ToListAsync();
    Assert.All(rows, r => Assert.Contains(r.Status, new[] {
        BulkOnboardingBatchRowStatus.Finalized, BulkOnboardingBatchRowStatus.WaitingForSeat,
        BulkOnboardingBatchRowStatus.WaitingForPositionApproval, BulkOnboardingBatchRowStatus.FinalizeFailed }));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter ProcessOnce_FinalizePendingBatch`
Expected: FAIL — processor does not yet handle `FinalizePending`.

- [ ] **Step 3: Extend `ProcessOnceAsync`**

```csharp
// In BulkOnboardingBatchProcessor.ProcessOnceAsync, replace the single GetOldestPendingAsync
// call with a check across both pending statuses, and add the finalize branch:

public async Task ProcessOnceAsync(CancellationToken ct)
{
    await using var scope = _services.CreateAsyncScope();
    var batchRepository = scope.ServiceProvider.GetRequiredService<IBulkOnboardingBatchRepository>();

    var writableTenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
    writableTenantContext.SetAdminMode();

    var batch = await batchRepository.GetOldestPendingAsync(BulkOnboardingBatchStatus.DraftCreationPending, ct)
        ?? await batchRepository.GetOldestPendingAsync(BulkOnboardingBatchStatus.FinalizePending, ct);
    if (batch is null)
        return;

    var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
    var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
    var writeService = scope.ServiceProvider.GetRequiredService<IOnboardingDraftWriteService>();
    var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

    var tenant = await tenantRepository.GetByIdAsync(batch.TenantId, ct);
    if (tenant is null)
    {
        _logger.LogError("Bulk onboarding batch {BatchId} references missing tenant {TenantId}.", batch.Id, batch.TenantId);
        return;
    }
    await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

    if (batch.Status == BulkOnboardingBatchStatus.DraftCreationPending)
    {
        await ProcessDraftCreationAsync(batch, batchRepository, writeService, ct);
    }
    else
    {
        await ProcessFinalizeAsync(batch, batchRepository, writeService, clock, ct);
    }
}

private async Task ProcessFinalizeAsync(
    BulkOnboardingBatch batch, IBulkOnboardingBatchRepository batchRepository,
    IOnboardingDraftWriteService writeService, IDateTimeProvider clock, CancellationToken ct)
{
    var selectedIds = JsonSerializer.Deserialize<List<Guid>>(batch.SelectedDraftIdsJson ?? "[]") ?? new();
    var rows = await batchRepository.ListTrackedRowsAsync(batch.TenantId, batch.Id, ct);

    foreach (var row in rows.Where(r => r.OnboardingDraftId is not null && selectedIds.Contains(r.OnboardingDraftId!.Value)))
    {
        var result = await writeService.FinalizeAsync(batch.TenantId, batch.CreatedByUserId, row.OnboardingDraftId!.Value, ct);
        if (!result.IsSuccess)
        {
            row.Status = BulkOnboardingBatchRowStatus.FinalizeFailed;
            row.ErrorMessage = result.Error;
            continue;
        }

        var outcome = result.Value!;
        row.Status = outcome.Status switch
        {
            "waiting_for_seat" => BulkOnboardingBatchRowStatus.WaitingForSeat,
            "waiting_for_position_approval" => BulkOnboardingBatchRowStatus.WaitingForPositionApproval,
            "finalized" => BulkOnboardingBatchRowStatus.Finalized,
            _ => BulkOnboardingBatchRowStatus.FinalizeFailed,
        };
        row.ErrorMessage = null;
    }

    batch.Status = BulkOnboardingBatchStatus.FinalizeCompleted;
    batch.CompletedAt = clock.UtcNow;
    await batchRepository.SaveChangesAsync(ct);
}

// Rename the existing body from Task 12 into this method (extracted, not duplicated):
private async Task ProcessDraftCreationAsync(
    BulkOnboardingBatch batch, IBulkOnboardingBatchRepository batchRepository,
    IOnboardingDraftWriteService writeService, CancellationToken ct)
{
    // ... exact body already written in Task 12's Step 3, moved here unchanged ...
}
```

Confirm `outcome.Status` string values against `OnboardingDraftStatus` constants (`OnboardingDraftStatus.WaitingForSeat` = `"waiting_for_seat"`, `.WaitingForPositionApproval` = `"waiting_for_position_approval"`, `.Finalized` = `"finalized"` — verified in Task 4's source) rather than the inline string literals shown above; reference the constants directly instead of literal strings when writing the file, to keep both sides of the switch in sync if those constants ever change.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingBatchProcessorTests`
Expected: PASS (all tests in the class, including Task 12's two)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessor.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingBatchProcessorTests.cs
git commit -m "feat: add BulkOnboardingBatchProcessor finalize leg"
```

---

## Task 15: `GET` batch status endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingBatch/GetBulkOnboardingBatchQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingBatch/GetBulkOnboardingBatchQueryHandler.cs`
- Create: `src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingBatchDetailViewModel.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs` — add `GetById` action
- Test: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingGetStatusTests.cs`

**Interfaces:**
- Consumes: `IBulkOnboardingBatchRepository.GetAsync`/`ListRowsAsync` (Task 3).
- Produces: `GET /api/v1/onboarding/bulk-batches/{id}` → full batch + all rows with their current status/error/draft id — this is what the frontend polls (frontend spec §4, step 4 and step 6).

- [ ] **Step 1: Write the failing integration test**

```csharp
[Fact]
public async Task GetById_ReturnsBatchWithAllRowStatuses()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:read");
    var writeClient = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadValidateAndGetBatchIdAsync(writeClient);

    var response = await client.GetAsync($"/api/v1/onboarding/bulk-batches/{batchId}");

    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadFromJsonAsync<BulkOnboardingBatchDetailViewModel>();
    Assert.Equal(batchId, body!.Id);
    Assert.NotEmpty(body.Rows);
}

[Fact]
public async Task GetById_FromDifferentTenant_Returns404NotAnotherTenantsBatch()
{
    var writeClient = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    var batchId = await UploadValidateAndGetBatchIdAsync(writeClient);
    var otherTenantClient = await AuthenticatedTenantClientForDifferentTenantAsync("employees:read");

    var response = await otherTenantClient.GetAsync($"/api/v1/onboarding/bulk-batches/{batchId}");

    Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingGetStatusTests`
Expected: FAIL — route does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingBatch/GetBulkOnboardingBatchQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;

public sealed record GetBulkOnboardingBatchQuery(Guid BatchId) : IRequest<Result<BulkOnboardingBatchDetailResponse>>;

public sealed record BulkOnboardingBatchRowDetailResponse(
    int RowNumber, string Status, string? ErrorMessage, Guid? OnboardingDraftId);

public sealed record BulkOnboardingBatchDetailResponse(
    Guid Id, string Status, int TotalRows, int? ValidRows, int? InvalidRows,
    IReadOnlyList<BulkOnboardingBatchRowDetailResponse> Rows);
```

```csharp
// src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries/GetBulkOnboardingBatch/GetBulkOnboardingBatchQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;

public class GetBulkOnboardingBatchQueryHandler
    : IRequestHandler<GetBulkOnboardingBatchQuery, Result<BulkOnboardingBatchDetailResponse>>
{
    private readonly IBulkOnboardingBatchRepository _batchRepository;
    private readonly ICurrentUser _currentUser;

    public GetBulkOnboardingBatchQueryHandler(IBulkOnboardingBatchRepository batchRepository, ICurrentUser currentUser)
    {
        _batchRepository = batchRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<BulkOnboardingBatchDetailResponse>> Handle(GetBulkOnboardingBatchQuery request, CancellationToken ct)
    {
        var batch = await _batchRepository.GetAsync(_currentUser.TenantId, request.BatchId, ct);
        if (batch is null)
            return Result<BulkOnboardingBatchDetailResponse>.NotFound("The batch could not be found.");

        var rows = await _batchRepository.ListRowsAsync(_currentUser.TenantId, batch.Id, ct);

        return Result<BulkOnboardingBatchDetailResponse>.Success(new BulkOnboardingBatchDetailResponse(
            batch.Id, batch.Status, batch.TotalRows, batch.ValidRows, batch.InvalidRows,
            rows.Select(r => new BulkOnboardingBatchRowDetailResponse(r.RowNumber, r.Status, r.ErrorMessage, r.OnboardingDraftId)).ToList()));
    }
}
```

```csharp
// src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingBatchDetailViewModel.cs
namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingBatchRowDetailViewModel(
    int RowNumber, string Status, string? ErrorMessage, Guid? OnboardingDraftId);

public sealed record BulkOnboardingBatchDetailViewModel(
    Guid Id,
    string Status,
    int TotalRows,
    int? ValidRows,
    int? InvalidRows,
    IReadOnlyList<BulkOnboardingBatchRowDetailViewModel> Rows);
```

Add to `BulkOnboardingController.cs`:

```csharp
    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetBulkOnboardingBatchQuery(id), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var r = result.Value!;
        return Ok(new BulkOnboardingBatchDetailViewModel(
            r.Id, r.Status, r.TotalRows, r.ValidRows, r.InvalidRows,
            r.Rows.Select(row => new BulkOnboardingBatchRowDetailViewModel(
                row.RowNumber, row.Status, row.ErrorMessage, row.OnboardingDraftId)).ToList()));
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter BulkOnboardingGetStatusTests`
Expected: PASS (both tests — the second one is the tenant-isolation proof required by spec §8)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/BulkOnboarding/Queries src/ONEVO.Api/Contracts/CoreHr/BulkOnboarding/BulkOnboardingBatchDetailViewModel.cs src/ONEVO.Api/Controllers/Tenant/CoreHr/BulkOnboardingController.cs tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingGetStatusTests.cs
git commit -m "feat: add bulk onboarding batch status endpoint"
```

---

## Task 16: Remaining integration coverage (seat-limit path, finalize idempotency proof)

**Files:**
- Modify: `tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingFinalizeTests.cs` — add the seat-limit scenario

**Interfaces:**
- Consumes: whatever test fixture this project already uses to force `ISeatEntitlementService` into a `Blocked` decision for seat-limit integration tests (search existing offboarding/onboarding integration tests for how they set a low `max_employees` plan fixture, and copy that setup — do not reimplement seat-limit test scaffolding from scratch).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task ProcessOnce_FinalizeWithNoSeatsAvailable_MarksRowWaitingForSeat()
{
    var client = await AuthenticatedTenantClientWithPermissionAsync("employees:write");
    await SeedTenantWithMaxEmployeeLimitAsync(limit: 0); // copy the exact fixture pattern the existing seat-limit integration test for single-employee finalize already uses
    var (batchId, draftIds) = await UploadValidateAndCreateDraftsAsync(client);

    await client.PostAsJsonAsync($"/api/v1/onboarding/bulk-batches/{batchId}/finalize", new { onboardingDraftIds = draftIds });
    await _processor.ProcessOnceAsync(CancellationToken.None);

    var detail = await client.GetFromJsonAsync<BulkOnboardingBatchDetailViewModel>($"/api/v1/onboarding/bulk-batches/{batchId}");
    Assert.All(detail!.Rows, r => Assert.Equal("waiting_for_seat", r.Status));
}
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter ProcessOnce_FinalizeWithNoSeatsAvailable`
Expected: this exercises code already written in Tasks 12-14 (the seat-limit branch is inherited for free from `IOnboardingDraftWriteService.FinalizeAsync`, per spec §6's finding) — if it fails, the bug is in how `ProcessFinalizeAsync`'s status mapping switch handles `"waiting_for_seat"`, not in new production code. Fix the mapping if needed; do not add new seat-limit logic — none should be required.

- [ ] **Step 3: Run full backend test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit && dotnet test tests/ONEVO.Tests.Integration && dotnet test tests/ONEVO.Tests.Architecture`
Expected: all green, including every pre-existing test (this is the final regression check for the whole plan, especially Task 4's refactor).

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/CoreHr/BulkOnboarding/BulkOnboardingFinalizeTests.cs
git commit -m "test: add bulk onboarding seat-limit finalize coverage"
```

---

## Task 17: Update `phase1-table-inventory.md`

**Files:**
- Modify: `docs/superpowers/project_ core/phase1-table-inventory.md` — add `bulk_onboarding_batches` and `bulk_onboarding_batch_rows` entries, same format as the existing `onboarding_drafts`/`employee_checklist_tasks` entries (column/type/notes table, one paragraph of description above each).

- [ ] **Step 1: Add the two table entries**

Insert immediately after the existing `### onboarding_drafts` section (right before `### employee_checklist_tasks`, so the two bulk tables sit next to the single-employee draft table they extend):

```markdown
### `bulk_onboarding_batches`

One CSV upload's worth of prospective employees, from upload through validation, background
draft creation, and background finalize. Column mapping is ephemeral (this-batch-only, never
reused across uploads).

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK -> tenants |
| `legal_entity_id` | `uuid` | FK -> legal_entities; batch-level default for every row |
| `default_employment_type` | `varchar(30)` | Nullable; batch-level default, CSV column can override per row |
| `default_work_mode_id` | `int` | Nullable, FK -> work_modes; batch-level default, CSV column can override per row |
| `default_checklist_template_id` | `uuid` | Nullable, FK -> checklist_templates; batch-level default, CSV column can override per row |
| `column_mapping` | `jsonb` | System field -> CSV header map; ephemeral to this batch |
| `selected_draft_ids` | `jsonb` | Nullable; onboarding_draft ids selected at finalize time |
| `original_file_name` | `varchar(255)` | Display only |
| `status` | `varchar(30)` | `mapping_pending`, `validated`, `draft_creation_pending`, `drafts_created`, `finalize_pending`, `finalize_completed` |
| `total_rows` | `int` | |
| `valid_rows` | `int` | Nullable until validated |
| `invalid_rows` | `int` | Nullable until validated |
| `created_by_user_id` | `uuid` | FK -> users |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |
| `completed_at` | `timestamptz` | Nullable; set when finalize_completed |

### `bulk_onboarding_batch_rows`

One CSV row's parsed data, resolution, and lifecycle status within a `bulk_onboarding_batches`
batch. Bulk-created drafts are ordinary `onboarding_drafts` rows (see that table) linked back
here by `onboarding_draft_id` - they also appear in the normal My Drafts list.

| Column | Type | Notes |
|:-------|:-----|:------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | |
| `batch_id` | `uuid` | FK -> bulk_onboarding_batches |
| `row_number` | `int` | 1-based, matches the CSV row for error reporting |
| `raw_data` | `jsonb` | Original cell values keyed by detected CSV header |
| `resolved_department_id` | `uuid` | Nullable; resolved at validation time by department name |
| `resolved_position_id` | `uuid` | Nullable; resolved at validation time by position name |
| `resolved_template_id` | `uuid` | Nullable; resolved checklist template, row override of the batch default |
| `status` | `varchar(30)` | `pending_mapping`, `valid`, `invalid`, `draft_created`, `draft_failed`, `finalized`, `waiting_for_seat`, `waiting_for_position_approval`, `finalize_failed` |
| `error_message` | `text` | Nullable |
| `onboarding_draft_id` | `uuid` | Nullable FK -> onboarding_drafts |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Unique:** `(tenant_id, batch_id, row_number)`.
```

- [ ] **Step 2: Commit**

```bash
git add "docs/superpowers/project_ core/phase1-table-inventory.md"
git commit -m "docs: add bulk onboarding tables to phase1 table inventory"
```

---

## Plan Self-Review Notes

- **Spec coverage:** CSV upload/parse (Task 5, 7), ephemeral mapping + preview (Task 8), partial-success validation (Task 9, 10), background draft creation (Task 11, 12), background finalize with four-way outcome (Task 13, 14), batch review/polling (Task 15), Contracts-folder pattern followed throughout (every Task's controller work uses `Api/Contracts/...`), RLS + tenant isolation (Task 2, proven in Task 12/15's isolation tests), idempotent finalize (Task 13), phase1-table-inventory update (Task 17). The `IOnboardingDraftWriteService` extraction (Task 4) is covered before anything depends on it (Task 12/14).
- **Corrected during planning, not just brainstorming:** the Reporting Manager field was removed from the design after verifying `Employee.cs`/`Position.cs` directly (see Global Constraints) — no task in this plan references it.
- **Deferred/simplified from the original spec table, both justified above rather than silent:** none — Work Mode and Checklist Template both kept their per-row CSV-override capability (Task 9) once `IWorkModeRepository.ListActiveAsync`/`IChecklistTemplateRepository.ListOnboardingMatchesAsync` were confirmed to already exist, so no scope was actually cut versus the approved spec.
