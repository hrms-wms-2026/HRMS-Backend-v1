# Work Management — Foundation Slice (Projects, Objectives, Creation Transaction) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Build the database schema, Domain entities, repositories, and the `POST /api/v1/work/projects` creation transaction (Project + Default Objective + creator membership + Default Version + release reminder + optional labels + optional logo, all atomic) for Work Management Slice 1 of 6.

**Architecture:** ASP.NET Core Clean Architecture / CQRS via MediatR, EF Core + Npgsql against PostgreSQL, tenant isolation via `ITenantOwnedEntity` + EF global query filter + PostgreSQL RLS. New entities follow `BaseEntity`; the multi-entity creation transaction is orchestrated through `IUnitOfWork.SaveChangesAsync` (one commit), not per-repository saves.

**Tech Stack:** .NET / ASP.NET Core, EF Core (Npgsql provider), PostgreSQL, MediatR, FluentValidation, xUnit (`dotnet test`).

## Global Constraints

- Domain (`ONEVO.Domain`) must not reference Application, Infrastructure, API, or EF Core.
- Application must not reference Infrastructure implementations or `HttpContext`/`IFormFile`.
- Controllers must never inject or use `ApplicationDbContext`.
- Every async method takes `CancellationToken` and is awaited; no `.Result`/`.Wait()`.
- Validation runs through the existing MediatR `ValidationBehavior` (FluentValidation) — handlers never call a validator manually.
- Use `Result`/`Result<T>` exactly as defined in `src/ONEVO.Application/Common/Models/Result.cs` (`Success`/`Failure`/`NotFound`/`Forbidden`/`Conflict`) — there is no `ToActionResult()`; controllers use the inline `result.IsSuccess ? Ok(...) : Problem(result.Error, statusCode: result.StatusCode ?? 400)` ternary (see `LegalEntitiesController.cs`).
- `tenant_id`, `owning_legal_entity_id`, and `lead_id` are never trusted from the request body — resolved from `ICurrentUser` (`src/ONEVO.Application/Common/ServiceInterfaces/ICurrentUser.cs`: `UserId`, `TenantId`, `IsAuthenticated`) and `ILegalEntityRepository.GetPrimaryByTenantIdAsync` (`src/ONEVO.Application/Features/OrgStructure/RepositoryInterfaces/ILegalEntityRepository.cs`).
- Raw SQL is forbidden except the migration's RLS policy SQL, following the exact pattern in `src/ONEVO.Infrastructure/Migrations/20260729082336_AddTenantSessionExchangeChallenges.cs`.
- No feature code may reserve storage quota or touch `file_records`/`file_upload_reservations` directly — only `IFileStorageService` (`src/ONEVO.Application/Features/Storage/File/ServiceInterfaces/IFileStorageService.cs`), enforced by an existing architecture test.
- Do not build Slice 2-6 endpoints (list/detail/category reads, project/objective updates, member management, invitations, version status movement) — those are separate future plans. Do not add `entity_assets` owner types other than `"project"`. Do not touch the dangling `workspace_id` references on `tasks`/`time_logs`/`documents`/`wiki_pages`/`repositories` (separate future phases, already flagged in `docs/superpowers/project_ core/phase1-table-inventory.md`).
- Schema reference: `docs/superpowers/project_ core/phase1-table-inventory.md` (Foundation + Projects + Objectives section, already updated 2026-08-03) is the authoritative column list — this plan's entity code must match it exactly except where noted (`ProjectVersion` instead of bare `Version` to avoid colliding with `System.Version`; `version_statuses` uses `int Id`/`string Code`/`string Label` matching the repo's existing lookup shape, not the doc's placeholder `smallint`/`status` pairing — already corrected in the doc).

**Deviation recorded during execution (2026-08-03, Task 2):** `.UseXminAsConcurrencyToken()` does not exist in the installed `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2 package (confirmed by inspecting the assembly directly — no `Xmin` identifier anywhere in it). Since this Foundation slice only ever inserts `projects`/`objectives`/`versions` rows and never updates them, xmin optimistic concurrency was dropped from `ProjectConfiguration`/`ObjectiveConfiguration`/`ProjectVersionConfiguration` for this slice. Whichever future slice first adds an UPDATE path (Slice 3 for Project/Objective edits, Slice 6 for version status movement) must research the correct current API before adding a concurrency token — a naive `HasColumnName("xmin")` shadow-property approach risks EF Core generating a migration that tries to `ALTER TABLE ... ADD COLUMN xmin`, which PostgreSQL rejects since `xmin` is a reserved system column.

**Bug fixed in an existing architecture test during execution (2026-08-03, Task 9):** `FileStorageArchitectureTests.ControllersAndApplicationHandlers_DoNotDependOnStorageAdaptersOrConcreteStorageServices` forbade any type reference containing the substring `"FileStorageService"` — which also matches the sanctioned `IFileStorageService` interface name (since `"IFileStorageService".Contains("FileStorageService")` is true), so any correct handler using the interface (exactly what `CreateProjectCommandHandler` does) would fail this check. Fixed by forbidding the fully-qualified concrete class name (`ONEVO.Infrastructure.Services.Storage.File.FileStorageService`) instead of the bare fragment. Full architecture suite run: 342 passed, 1 unrelated pre-existing failure (`CredentialOwnershipCompletionArchitectureTests.LocalEnvironment_WhenPresent_ContainsNoDeprecatedProviderSettings`, which checks the local `.env` file for a deprecated `Email__Provider` key — untouched by this work, confirmed independent).

**Important limitation confirmed during execution (2026-08-03, Task 8):** `grep -r "new Employee" src/` returns **zero matches anywhere in this codebase** — there is currently no flow, anywhere (not tenant provisioning, not the DevSmokeTestTenantSeeder, nowhere), that creates an `employees` row for any user. `CreateProjectCommandHandler` correctly requires one (`project_members.employee_id` is non-null per the locked spec), so **any real tenant owner/user in this system today cannot create a Project until a separate Employee-onboarding feature exists** to give them an employee record. This is out of scope for Work Management to fix. The integration test (Task 8) works around this by seeding an `employees` row directly for its test-provisioned tenant owners, the same way it already seeds `project_categories` directly since no creation endpoint exists for that either. Flagged here so this isn't mistaken for a Work Management bug later — it is a pre-existing gap in the rest of the system.

**Deviation recorded during execution (2026-08-03, Task 6):** `IFileStorageService.CancelReservationAsync` explicitly rejects (409 Conflict) cancelling an already-`Completed` reservation — confirmed by reading `FileStorageService.cs`: "Cancelling after completion is rejected, not silently accepted, since bytes have already moved to used." There is no method anywhere on `IFileStorageService` to reverse a completed upload. `CreateProjectCommandHandler`'s failure path therefore logs the orphaned `file_record` id (for manual/future reconciliation) instead of calling a compensation method that would itself fail — the same documented limitation `SetLegalEntityLogoCommandHandler` already accepts for file-ownership validation.

**Deviation recorded during execution (2026-08-03, Task 3 prep):** `EntityAsset.TenantId` was changed from nullable (`Guid?`, matching the tables doc's "nullable for platform-level assets" note) to non-nullable (`Guid`), and the entity now inherits `BaseEntity` directly instead of hand-implementing `ITenantOwnedEntity`. Root cause: `ApplicationDbContext.BuildGenericTenantAndSoftDeleteFilterBody` builds the tenant global query filter via `Expression.Property(parameter, nameof(ITenantOwnedEntity.TenantId))` against the entity's concrete CLR type, then `Expression.Equal` against the non-nullable `CurrentTenantId` — this throws `InvalidOperationException` (`Equal not defined for Nullable<Guid> and Guid`) at model-build time for any entity whose public `TenantId` property is nullable. Confirmed by reproducing the exact error via `dotnet ef migrations add`. Every other `ITenantOwnedEntity` in this codebase has non-nullable `TenantId`, and this task never creates a platform-level (null-tenant) row, so non-nullable is both correct for current scope and consistent with the rest of the codebase. `docs/superpowers/project_ core/phase1-table-inventory.md`'s `entity_assets.tenant_id` note has been updated to match.

---

### Task 1: Domain entities

**Files:**
- Create: `src/ONEVO.Domain/Features/Storage/EntityAssets/Entities/EntityAsset.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/ProjectCategory.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Projects/Entities/Project.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Objectives/Entities/Objective.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/ProjectMembers/Entities/ProjectMember.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/ProjectInvitations/Entities/ProjectMemberInvitation.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/ProjectVersion.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/ReleaseCalendar/Entities/ReleaseCalendarEntry.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Labels/Entities/Label.cs`
- Create: `src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs`
- Modify: `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`

**Interfaces:**
- Produces: all entity classes/properties referenced by every later task (EF configs, repositories, handler). No behavior beyond `BaseEntity`'s domain-event helper — these are data holders, so there is no unit test for this task; verification is `dotnet build` succeeding, per the note below.

These are plain data classes with no independent behavior to unit-test (matching the existing `Employee`/`LegalEntity` precedent — entity classes in this codebase have no tests of their own; behavior is tested through the handlers that use them). Verification for this task is a successful build, not a red/green test cycle.

- [x] **Step 1: `EntityAsset`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Storage.EntityAssets.Entities;

/// <summary>
/// Generic link from a product entity to a file. Scoped to owner_type "project"
/// only for now (Work Management project cover/logo) — see EntityAssetOwnerTypes.
/// </summary>
public class EntityAsset : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string AssetPurpose { get; set; } = string.Empty;
    public Guid FileRecordId { get; set; }
    public bool IsPrimary { get; set; }
    public int? SortOrder { get; set; }
    public string? MetadataJson { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    Guid ITenantOwnedEntity.TenantId => TenantId ?? Guid.Empty;
}
```

`entity_assets.tenant_id` is nullable per the tables doc (platform-level assets have no tenant), so `EntityAsset` cannot inherit `BaseEntity` (which requires a non-nullable `TenantId`). It implements `ITenantOwnedEntity` directly instead — same pattern already used by `LegalEntity` for a different reason. The explicit interface implementation satisfies `ITenantOwnedEntity.TenantId` (non-nullable) from the nullable `TenantId` property; the EF global tenant filter (Task 2) still applies correctly because it filters on the interface member, and every row this slice creates always has a real `TenantId` set.

- [x] **Step 2: `EntityAssetOwnerTypes` constant**

```csharp
namespace ONEVO.Application.Common.Constants;

/// <summary>Centralized entity_assets.owner_type values. Add a new constant here — never a raw string literal — when a new owner type is wired up.</summary>
public static class EntityAssetOwnerTypes
{
    public const string Project = "project";
}
```

- [x] **Step 3: Add `ProjectCover` to `UploadPurposeCatalog`**

Modify `src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs`:

```csharp
public const string CompanyLogo = "company_logo";
public const string EmployeeAvatar = "employee_avatar";
public const string GenericDocument = "generic_document";
public const string ProjectCover = "project_cover";
```

and in the `Rules` dictionary initializer, add:

```csharp
[ProjectCover] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
```

(reuses the existing `ImageContentTypes`/`ImageExtensions` lists already defined in that file — same 5 MB image-only rule as `CompanyLogo`/`EmployeeAvatar`).

- [x] **Step 4: `ProjectCategory`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Projects.Entities;

public class ProjectCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
```

- [x] **Step 5: `Project`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Projects.Entities;

public class Project : BaseEntity
{
    public Guid OwningLegalEntityId { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public long NextTaskNumber { get; set; } = 1;
    public string? Description { get; set; }
    public Guid LeadId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal AllocatedHours { get; set; }
    public decimal CompletedHours { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [x] **Step 6: `Objective`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

public class Objective : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ParentObjectiveId { get; set; }
    public bool IsDefault { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Progress { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal AllocatedHours { get; set; }
    public decimal CompletedHours { get; set; }
}
```

- [x] **Step 7: `ProjectMember`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

public static class ProjectMembershipSources
{
    public const string System = "system";
    public const string ObjectiveInvitation = "objective_invitation";
}

public class ProjectMember : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }
    public string MembershipSource { get; set; } = ProjectMembershipSources.System;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
}
```

- [x] **Step 8: `ProjectMemberInvitation`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;

public static class ProjectInvitationStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
}

public class ProjectMemberInvitation : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitedEmployeeId { get; set; }
    public string Status { get; set; } = ProjectInvitationStatuses.Pending;
    public Guid InvitedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

This entity/table is created in this slice for its FK shape (`project_members.objective_id` and the Default Objective's membership row reference it conceptually) but has no commands/queries in this slice — those are Slice 5.

- [x] **Step 9: `VersionStatus`**

```csharp
namespace ONEVO.Domain.Features.WorkManagement.Versions.Entities;

/// <summary>Fixed global lookup, seeded 1=planned, 2=released, 3=archived. Same shape/seeding mechanism as ONEVO.Domain.Lookups (EmploymentType, Severity, etc.) — see LookupDataSeeder.</summary>
public class VersionStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public static class VersionStatusIds
{
    public const int Planned = 1;
    public const int Released = 2;
    public const int Archived = 3;
}
```

- [x] **Step 10: `ProjectVersion`**

Named `ProjectVersion`, not `Version`, to avoid colliding with `System.Version` (maps to the `versions` table via `ToTable("versions")` in Task 2 — the C# type name does not need to match the table name).

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Versions.Entities;

public class ProjectVersion : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int StatusId { get; set; } = VersionStatusIds.Planned;
}
```

- [x] **Step 11: `ReleaseCalendarEntry`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

public static class ReleaseReminderTypes
{
    public const string ProjectRelease = "project_release";
}

public class ReleaseCalendarEntry : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid VersionId { get; set; }
    public Guid RecipientUserId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public string ReminderType { get; set; } = ReleaseReminderTypes.ProjectRelease;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [x] **Step 12: `Label`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Labels.Entities;

public class Label : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}
```

- [x] **Step 13: Verify build**

Run: `dotnet build src/ONEVO.Domain/ONEVO.Domain.csproj`
Expected: build succeeds with 0 errors (new files compile; no Application/Infrastructure/API references from Domain).

- [x] **Step 14: Commit**

```bash
git add src/ONEVO.Domain/Features/Storage/EntityAssets src/ONEVO.Domain/Features/WorkManagement src/ONEVO.Application/Common/Constants/EntityAssetOwnerTypes.cs src/ONEVO.Application/Features/Storage/File/Helpers/UploadPurposeCatalog.cs
git commit -m "feat(work-management): add Foundation slice domain entities"
```

---

### Task 2: EF Core configurations + xmin concurrency + DbSet registration

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Storage/EntityAssetConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectCategoryConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ObjectiveConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectMemberInvitationConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/VersionStatusConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ProjectVersionConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/ReleaseCalendarEntryConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/LabelConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: entity classes from Task 1.
- Produces: `DbSet<T>` properties on `ApplicationDbContext` that Task 4's repositories query against (`db.Projects`, `db.Objectives`, `db.ProjectMembers`, `db.ProjectVersions`, `db.ReleaseCalendarEntries`, `db.Labels`, `db.EntityAssets`, `db.ProjectCategories`, `db.VersionStatuses`, `db.ProjectMemberInvitations`).

`IEntityTypeConfiguration<T>` classes are auto-discovered by `modelBuilder.ApplyConfigurationsFromAssembly(...)` in every EF Core setup this repo uses elsewhere (confirmed by the Legal Entity slice needing no manual `ApplyConfiguration` call) — no `OnModelCreating` wiring beyond adding the `DbSet` properties is required. Column names are snake_case automatically (the repo's existing global naming convention, confirmed by every migration already inspected using `snake_case` without explicit `HasColumnName` calls).

- [x] **Step 1: `EntityAssetConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Storage;

public class EntityAssetConfiguration : IEntityTypeConfiguration<EntityAsset>
{
    public void Configure(EntityTypeBuilder<EntityAsset> builder)
    {
        builder.ToTable("entity_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.OwnerType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.AssetPurpose).HasMaxLength(50).IsRequired();
        builder.Property(a => a.MetadataJson).HasColumnType("jsonb");
        builder.Property(a => a.CreatedByType).HasMaxLength(30).IsRequired();

        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(a => a.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 2: `ProjectCategoryConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectCategoryConfiguration : IEntityTypeConfiguration<ProjectCategory>
{
    public void Configure(EntityTypeBuilder<ProjectCategory> builder)
    {
        builder.ToTable("project_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Name })
            .IsUnique()
            .HasDatabaseName("ix_project_categories_tenant_id_name");
    }
}
```

Case-insensitive tenant-uniqueness on `Name` is enforced at the Application layer (normalize to lowercase before the uniqueness check — see Task 6), consistent with how `LegalEntity.NameExistsForTenantAsync` handles case-insensitive comparisons in this repo (compares an already-normalized value, not a SQL `LOWER()` expression index).

- [x] **Step 3: `ProjectConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Identifier).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Color).HasMaxLength(20);
        builder.Property(p => p.ActualHours).HasColumnType("numeric(18,2)");
        builder.Property(p => p.AllocatedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(p => p.CompletedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(p => p.NextTaskNumber).HasDefaultValue(1L);

        builder.HasIndex(p => new { p.TenantId, p.Identifier })
            .IsUnique()
            .HasDatabaseName("ix_projects_tenant_id_identifier");
        builder.HasIndex(p => new { p.TenantId, p.OwningLegalEntityId, p.UpdatedAt })
            .HasDatabaseName("ix_projects_tenant_id_owning_legal_entity_id_updated_at");
        builder.HasIndex(p => new { p.TenantId, p.CategoryId, p.IsActive })
            .HasDatabaseName("ix_projects_tenant_id_category_id_is_active");

        builder.HasOne<LegalEntity>()
            .WithMany()
            .HasForeignKey(p => p.OwningLegalEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.UseXminAsConcurrencyToken();
    }
}
```

- [x] **Step 4: `ObjectiveConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ObjectiveConfiguration : IEntityTypeConfiguration<Objective>
{
    public void Configure(EntityTypeBuilder<Objective> builder)
    {
        builder.ToTable("objectives");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Title).HasMaxLength(255).IsRequired();
        builder.Property(o => o.Progress).HasColumnType("numeric(5,2)").HasDefaultValue(0m);
        builder.Property(o => o.ActualHours).HasColumnType("numeric(18,2)");
        builder.Property(o => o.AllocatedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.CompletedHours).HasColumnType("numeric(18,2)").HasDefaultValue(0m);

        builder.HasIndex(o => new { o.TenantId, o.ProjectId, o.ParentObjectiveId })
            .HasDatabaseName("ix_objectives_tenant_id_project_id_parent_objective_id");
        builder.HasIndex(o => new { o.TenantId, o.OwnerId, o.IsActive })
            .HasDatabaseName("ix_objectives_tenant_id_owner_id_is_active");
        builder.HasIndex(o => new { o.TenantId, o.ProjectId })
            .IsUnique()
            .HasFilter("is_default = true")
            .HasDatabaseName("ix_objectives_one_default_per_project");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(o => o.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(o => o.ParentObjectiveId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.UseXminAsConcurrencyToken();
    }
}
```

- [x] **Step 5: `ProjectMemberConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MembershipSource).HasMaxLength(30).IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.UserId })
            .IsUnique()
            .HasDatabaseName("ix_project_members_tenant_project_objective_user");
        builder.HasIndex(m => new { m.TenantId, m.UserId, m.IsActive, m.ProjectId })
            .HasDatabaseName("ix_project_members_tenant_user_active_project");
        builder.HasIndex(m => new { m.TenantId, m.ProjectId, m.ObjectiveId, m.IsActive })
            .HasDatabaseName("ix_project_members_tenant_project_objective_active");

        builder.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(m => m.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 6: `ProjectMemberInvitationConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectMemberInvitationConfiguration : IEntityTypeConfiguration<ProjectMemberInvitation>
{
    public void Configure(EntityTypeBuilder<ProjectMemberInvitation> builder)
    {
        builder.ToTable("project_member_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Status).HasMaxLength(20).IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.InvitedUserId, i.Status })
            .HasDatabaseName("ix_project_member_invitations_tenant_invited_user_status");
        builder.HasIndex(i => new { i.TenantId, i.ProjectId, i.ObjectiveId, i.InvitedUserId })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_project_member_invitations_one_pending");

        builder.HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Objective>().WithMany().HasForeignKey(i => i.ObjectiveId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 7: `VersionStatusConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class VersionStatusConfiguration : IEntityTypeConfiguration<VersionStatus>
{
    public void Configure(EntityTypeBuilder<VersionStatus> builder)
    {
        builder.ToTable("version_statuses");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Label).HasMaxLength(50).IsRequired();

        builder.HasIndex(s => s.Code).IsUnique().HasDatabaseName("ix_version_statuses_code");
    }
}
```

- [x] **Step 8: `ProjectVersionConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ProjectVersionConfiguration : IEntityTypeConfiguration<ProjectVersion>
{
    public void Configure(EntityTypeBuilder<ProjectVersion> builder)
    {
        builder.ToTable("versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(v => new { v.TenantId, v.ProjectId, v.StatusId })
            .HasDatabaseName("ix_versions_tenant_id_project_id_status_id");

        builder.HasOne<Project>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VersionStatus>().WithMany().HasForeignKey(v => v.StatusId).OnDelete(DeleteBehavior.Restrict);

        builder.UseXminAsConcurrencyToken();
    }
}
```

- [x] **Step 9: `ReleaseCalendarEntryConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class ReleaseCalendarEntryConfiguration : IEntityTypeConfiguration<ReleaseCalendarEntry>
{
    public void Configure(EntityTypeBuilder<ReleaseCalendarEntry> builder)
    {
        builder.ToTable("release_calendar");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReminderType).HasMaxLength(30).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.RecipientUserId, r.ScheduledDate, r.IsActive })
            .HasDatabaseName("ix_release_calendar_tenant_recipient_scheduled_active");
        builder.HasIndex(r => new { r.VersionId, r.RecipientUserId })
            .IsUnique()
            .HasFilter("is_active = true AND reminder_type = 'project_release'")
            .HasDatabaseName("ix_release_calendar_one_active_project_release");

        builder.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProjectVersion>().WithMany().HasForeignKey(r => r.VersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 10: `LabelConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Color).HasMaxLength(20).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.ProjectId, l.Name })
            .IsUnique()
            .HasDatabaseName("ix_labels_tenant_id_project_id_name");

        builder.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [x] **Step 11: Add `DbSet` properties to `ApplicationDbContext`**

Modify `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` — add near the other feature `DbSet`s (matching the existing one-line-per-set style seen at lines 67-87):

```csharp
public DbSet<EntityAsset> EntityAssets => Set<EntityAsset>();
public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
public DbSet<Project> Projects => Set<Project>();
public DbSet<Objective> Objectives => Set<Objective>();
public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
public DbSet<ProjectMemberInvitation> ProjectMemberInvitations => Set<ProjectMemberInvitation>();
public DbSet<VersionStatus> VersionStatuses => Set<VersionStatus>();
public DbSet<ProjectVersion> ProjectVersions => Set<ProjectVersion>();
public DbSet<ReleaseCalendarEntry> ReleaseCalendarEntries => Set<ReleaseCalendarEntry>();
public DbSet<Label> Labels => Set<Label>();
```

Add the corresponding `using` statements for each entity namespace at the top of the file.

- [x] **Step 12: Verify build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: build succeeds with 0 errors.

- [x] **Step 13: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Storage/EntityAssetConfiguration.cs src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat(work-management): add EF configurations for Foundation slice entities"
```

---

### Task 3: Migration + RLS + lookup seeding

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddWorkManagementFoundation.cs` (run `dotnet ef migrations add AddWorkManagementFoundation` to generate the real timestamped filename + the paired `.Designer.cs`/model snapshot update — do not hand-write the Designer file)
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs`

**Interfaces:**
- Consumes: EF configurations from Task 2 (the migration is generated from the model, not hand-written table-by-table).
- Produces: the 10 new tables in PostgreSQL, RLS-protected, plus 3 seeded `version_statuses` rows available to Task 6's handler.

- [x] **Step 1: Generate the migration**

Run: `dotnet ef migrations add AddWorkManagementFoundation --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`

Expected: a new `<timestamp>_AddWorkManagementFoundation.cs` + `.Designer.cs` appear under `src/ONEVO.Infrastructure/Migrations/`, and `ApplicationDbContextModelSnapshot.cs` is updated. Open the generated `.cs` file and confirm it creates exactly these 10 tables: `entity_assets`, `project_categories`, `projects`, `objectives`, `project_members`, `project_member_invitations`, `version_statuses`, `versions`, `release_calendar`, `labels`.

- [x] **Step 2: Add the RLS policy block to the generated migration's `Up`/`Down`**

Following the exact pattern in `20260729082336_AddTenantSessionExchangeChallenges.cs`, add this to the bottom of the generated `Up(MigrationBuilder migrationBuilder)` method (after the `CreateTable`/`CreateIndex` calls EF already generated) — `version_statuses` is excluded from `TenantTables` since it is a global, not tenant-scoped, lookup:

```csharp
private static readonly string[] TenantTables =
[
    "entity_assets", "project_categories", "projects", "objectives",
    "project_members", "project_member_invitations", "versions",
    "release_calendar", "labels"
];
```

At the end of `Up`:

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

`entity_assets.tenant_id` is nullable (platform-level assets), but every row this slice ever creates has a real `tenant_id`, so the same policy predicate is correct — a null `tenant_id` row would simply never match the tenant branch, which is acceptable since this slice never creates one.

At the top of `Down(MigrationBuilder migrationBuilder)`, before the generated `DropTable` calls:

```csharp
foreach (var table in TenantTables)
{
    migrationBuilder.Sql($@"
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
    ");
}
```

- [x] **Step 3: Add `version_statuses` seeding to `LookupDataSeeder`**

Modify `src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs`:

```csharp
private async Task SeedAllAsync(ApplicationDbContext db, CancellationToken ct)
{
    await SeedAsync(db, db.EmploymentTypes, EmploymentTypes(), "employment types", ct);
    await SeedAsync(db, db.EmploymentStatuses, EmploymentStatuses(), "employment statuses", ct);
    await SeedAsync(db, db.WorkModes, WorkModes(), "work modes", ct);
    await SeedAsync(db, db.ApprovalStatuses, ApprovalStatuses(), "approval statuses", ct);
    await SeedAsync(db, db.Severities, Severities(), "severities", ct);
    await SeedAsync(db, db.VersionStatuses, VersionStatuses(), "version statuses", ct);
}

private static VersionStatus[] VersionStatuses() =>
[
    new() { Id = VersionStatusIds.Planned,  Code = "planned",  Label = "Planned"  },
    new() { Id = VersionStatusIds.Released, Code = "released", Label = "Released" },
    new() { Id = VersionStatusIds.Archived, Code = "archived", Label = "Archived" },
];
```

Add `using ONEVO.Domain.Features.WorkManagement.Versions.Entities;` to the file's usings.

- [x] **Step 4: Apply the migration to the local dev database and verify**

Run: `dotnet ef database update --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api`
Expected: migration applies with no errors. Then run:
`psql -d <local_db> -c "SELECT tablename, rowsecurity, forcerowsecurity FROM pg_tables WHERE tablename IN ('projects','objectives','project_members','entity_assets') ;"`
Expected: `rowsecurity` and `forcerowsecurity` both `t` (true) for all 4 rows.

- [x] **Step 5: Verify lookup seeding**

Run the API locally once (`dotnet run --project src/ONEVO.Api`) so `LookupDataSeeder` executes, then:
`psql -d <local_db> -c "SELECT id, code, label FROM version_statuses ORDER BY id;"`
Expected: exactly 3 rows — `(1, planned, Planned)`, `(2, released, Released)`, `(3, archived, Archived)`.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations src/ONEVO.Infrastructure/Persistence/Seeders/LookupDataSeeder.cs
git commit -m "feat(work-management): add Foundation slice migration, RLS policies, and version_statuses seeding"
```

---

### Task 4: Repository interfaces + EF implementations

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectCategoryRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/RepositoryInterfaces/IProjectRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Objectives/RepositoryInterfaces/IObjectiveRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ProjectMembers/RepositoryInterfaces/IProjectMemberRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Versions/RepositoryInterfaces/IProjectVersionRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/ReleaseCalendar/RepositoryInterfaces/IReleaseCalendarRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Labels/RepositoryInterfaces/ILabelRepository.cs`
- Create: `src/ONEVO.Application/Common/RepositoryInterfaces/IEntityAssetRepository.cs`
- Create: `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectCategoryRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfObjectiveRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectMemberRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfProjectVersionRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfReleaseCalendarRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfLabelRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEntityAssetRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` DbSets from Task 2.
- Produces: the repository interfaces `CreateProjectCommandHandler` (Task 6) injects: `IProjectCategoryRepository.GetByIdForTenantAsync`, `IProjectRepository.IdentifierExistsForTenantAsync`/`AddAsync`, `IObjectiveRepository.AddAsync`, `IProjectMemberRepository.AddAsync`, `IProjectVersionRepository.AddAsync`, `IReleaseCalendarRepository.AddAsync`, `ILabelRepository.NameExistsInProjectAsync`/`AddAsync`, `IEntityAssetRepository.AddAsync`, `IEmployeeRepository.GetByUserIdAsync`.

Only the methods this slice's creation transaction actually needs are added now (no speculative list/update/delete methods for later slices) — matching the plain-custom-methods style of `ILegalEntityRepository` (no generic `IRepository<T>`).

- [x] **Step 1: `IProjectCategoryRepository` + `EfProjectCategoryRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectCategoryRepository
{
    Task<ProjectCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectCategoryRepository : IProjectCategoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectCategoryRepository(ApplicationDbContext db) => _db = db;

    public async Task<ProjectCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ProjectCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);
    }
}
```

- [x] **Step 2: `IProjectRepository` + `EfProjectRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;

public interface IProjectRepository
{
    Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default);

    Task AddAsync(Project project, CancellationToken ct = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> IdentifierExistsForTenantAsync(Guid tenantId, string identifier, CancellationToken ct = default)
    {
        return await _db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId && p.Identifier == identifier, ct);
    }

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        await _db.Projects.AddAsync(project, ct);
    }
}
```

- [x] **Step 3: `IObjectiveRepository` + `EfObjectiveRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;

public interface IObjectiveRepository
{
    Task AddAsync(Objective objective, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveRepository : IObjectiveRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Objective objective, CancellationToken ct = default)
    {
        await _db.Objectives.AddAsync(objective, ct);
    }
}
```

- [x] **Step 4: `IProjectMemberRepository` + `EfProjectMemberRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;

public interface IProjectMemberRepository
{
    Task AddAsync(ProjectMember member, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectMemberRepository : IProjectMemberRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectMemberRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectMember member, CancellationToken ct = default)
    {
        await _db.ProjectMembers.AddAsync(member, ct);
    }
}
```

- [x] **Step 5: `IProjectVersionRepository` + `EfProjectVersionRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;

public interface IProjectVersionRepository
{
    Task AddAsync(ProjectVersion version, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectVersionRepository : IProjectVersionRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectVersionRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ProjectVersion version, CancellationToken ct = default)
    {
        await _db.ProjectVersions.AddAsync(version, ct);
    }
}
```

- [x] **Step 6: `IReleaseCalendarRepository` + `EfReleaseCalendarRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;

public interface IReleaseCalendarRepository
{
    Task AddAsync(ReleaseCalendarEntry entry, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfReleaseCalendarRepository : IReleaseCalendarRepository
{
    private readonly ApplicationDbContext _db;

    public EfReleaseCalendarRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(ReleaseCalendarEntry entry, CancellationToken ct = default)
    {
        await _db.ReleaseCalendarEntries.AddAsync(entry, ct);
    }
}
```

- [x] **Step 7: `ILabelRepository` + `EfLabelRepository`**

```csharp
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;

namespace ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;

public interface ILabelRepository
{
    Task AddAsync(Label label, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfLabelRepository : ILabelRepository
{
    private readonly ApplicationDbContext _db;

    public EfLabelRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Label label, CancellationToken ct = default)
    {
        await _db.Labels.AddAsync(label, ct);
    }
}
```

Duplicate/normalized-name checking for labels within one creation request happens in the validator (Task 6) against the in-memory request payload — no per-name DB round trip is needed since a brand-new Project has no existing labels yet.

- [x] **Step 8: `IEntityAssetRepository` + `EfEntityAssetRepository`**

```csharp
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEntityAssetRepository
{
    Task AddAsync(EntityAsset asset, CancellationToken ct = default);
}
```

```csharp
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEntityAssetRepository : IEntityAssetRepository
{
    private readonly ApplicationDbContext _db;

    public EfEntityAssetRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(EntityAsset asset, CancellationToken ct = default)
    {
        await _db.EntityAssets.AddAsync(asset, ct);
    }
}
```

- [x] **Step 9: `IEmployeeRepository` + `EfEmployeeRepository`**

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEmployeeRepository : IEmployeeRepository
{
    private readonly ApplicationDbContext _db;

    public EfEmployeeRepository(ApplicationDbContext db) => _db = db;

    public async Task<Employee?> GetByUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        return await _db.Set<Employee>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);
    }
}
```

Uses `_db.Set<Employee>()` rather than a named `DbSet` property because `Employee`'s existing `DbSet` property name on `ApplicationDbContext` was not confirmed during research — check `ApplicationDbContext.cs` first; if a `db.Employees` property already exists, use that instead of `Set<Employee>()` for consistency with the rest of this file.

- [x] **Step 10: Register all 9 repositories in `DependencyInjection.cs`**

Modify `src/ONEVO.Infrastructure/DependencyInjection.cs`, following the exact two-line concrete-then-interface pattern already used for `EfLegalEntityRepository`:

```csharp
services.AddScoped<EfProjectCategoryRepository>();
services.AddScoped<IProjectCategoryRepository>(sp => sp.GetRequiredService<EfProjectCategoryRepository>());
services.AddScoped<EfProjectRepository>();
services.AddScoped<IProjectRepository>(sp => sp.GetRequiredService<EfProjectRepository>());
services.AddScoped<EfObjectiveRepository>();
services.AddScoped<IObjectiveRepository>(sp => sp.GetRequiredService<EfObjectiveRepository>());
services.AddScoped<EfProjectMemberRepository>();
services.AddScoped<IProjectMemberRepository>(sp => sp.GetRequiredService<EfProjectMemberRepository>());
services.AddScoped<EfProjectVersionRepository>();
services.AddScoped<IProjectVersionRepository>(sp => sp.GetRequiredService<EfProjectVersionRepository>());
services.AddScoped<EfReleaseCalendarRepository>();
services.AddScoped<IReleaseCalendarRepository>(sp => sp.GetRequiredService<EfReleaseCalendarRepository>());
services.AddScoped<EfLabelRepository>();
services.AddScoped<ILabelRepository>(sp => sp.GetRequiredService<EfLabelRepository>());
services.AddScoped<EfEntityAssetRepository>();
services.AddScoped<IEntityAssetRepository>(sp => sp.GetRequiredService<EfEntityAssetRepository>());
services.AddScoped<EfEmployeeRepository>();
services.AddScoped<IEmployeeRepository>(sp => sp.GetRequiredService<EfEmployeeRepository>());
```

Add the corresponding `using` statements for each namespace.

- [x] **Step 11: Verify build**

Run: `dotnet build`
Expected: full solution builds with 0 errors.

- [x] **Step 12: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement src/ONEVO.Application/Common/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(work-management): add Foundation slice repositories"
```

---

### Task 5: Permission seeds

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs`

**Interfaces:**
- Produces: permission codes `members:read`, `members:manage`, `invitations:manage`, `invitations:respond`, `versions:write`, `labels:manage`, checked by `[RequirePermission(...)]` in Task 7's controller and future slices' controllers.

- [x] **Step 1: Add the new permission codes**

Modify the array ending at line 260 in `PermissionSeeder.cs` — insert immediately before the closing `];`, after the existing `roadmaps:write` line:

```csharp
        Perm("roadmaps:write", "Create and edit roadmaps.", "work_management"),

        // Work Management — Projects (Foundation slice additions)
        Perm("members:read", "View project members.", "work_management"),
        Perm("members:manage", "Activate, deactivate, or remove project members.", "work_management"),
        Perm("invitations:manage", "Send and cancel project/objective invitations.", "work_management"),
        Perm("invitations:respond", "Accept or decline a project/objective invitation.", "work_management"),
        Perm("versions:write", "Create and change project version status.", "work_management"),
        Perm("labels:manage", "Create and edit project labels.", "work_management"),
    ];
```

(`projects:read`/`projects:write`/`projects:create` and `okr:read`/`okr:write` already exist earlier in this same array — do not duplicate them.)

- [x] **Step 2: Verify by running the seeder against a local dev database**

Run: `dotnet run --project src/ONEVO.Api` once (Development environment), then:
`psql -d <local_db> -c "SELECT code FROM permissions WHERE code IN ('members:read','members:manage','invitations:manage','invitations:respond','versions:write','labels:manage') ORDER BY code;"`
Expected: exactly 6 rows returned, one per new code.

- [x] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/PermissionSeeder.cs
git commit -m "feat(work-management): seed new Foundation slice permission codes"
```

---

### Task 6: `CreateProjectCommand` vertical slice (unit-tested)

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommand.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject/CreateProjectCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Requests/CreateProjectLabelInput.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/DTOs/Responses/ProjectCreationResponse.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/Projects/Mappers/ProjectMapper.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/CreateProjectCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IProjectCategoryRepository`, `IProjectRepository`, `IObjectiveRepository`, `IProjectMemberRepository`, `IProjectVersionRepository`, `IReleaseCalendarRepository`, `ILabelRepository`, `IEntityAssetRepository`, `IEmployeeRepository`, `ILegalEntityRepository.GetPrimaryByTenantIdAsync`, `IAuditLogRepository.AddAsync` (`src/ONEVO.Application/Features/Auth/Login/RepositoryInterfaces/IAuditLogRepository.cs`), `IFileStorageService.UploadAsync`/`CancelReservationAsync`, `IUnitOfWork.SaveChangesAsync` (`src/ONEVO.Application/Common/RepositoryInterfaces/IUnitOfWork.cs`), `ICurrentUser`.
- Produces: `CreateProjectCommand` and `Result<ProjectCreationResponse>` — the exact types Task 7's controller sends/receives.

- [x] **Step 1: `CreateProjectLabelInput` and `ProjectCreationResponse`**

```csharp
namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;

public sealed record CreateProjectLabelInput(string Name, string Color);
```

```csharp
namespace ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

public sealed record ProjectSummaryDto(
    Guid Id, string Name, string Identifier, Guid CategoryId, string? Description,
    Guid LeadId, DateOnly StartDate, DateOnly TargetDate, string? Color,
    decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt);

public sealed record ObjectiveSummaryDto(
    Guid Id, Guid ProjectId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours);

public sealed record ProjectVersionSummaryDto(Guid Id, string Name, int StatusId, string StatusCode);

public sealed record ReleaseReminderSummaryDto(Guid Id, Guid VersionId, DateOnly ScheduledDate, string ReminderType);

public sealed record LabelSummaryDto(Guid Id, string Name, string Color);

public sealed record ProjectMembershipSummaryDto(Guid Id, Guid ObjectiveId, Guid UserId, string MembershipSource);

public sealed record ProjectLogoSummaryDto(Guid FileRecordId, string OriginalFileName);

public sealed record ProjectCreationResponse(
    ProjectSummaryDto Project,
    ObjectiveSummaryDto DefaultObjective,
    ProjectVersionSummaryDto DefaultVersion,
    ReleaseReminderSummaryDto ReleaseReminder,
    IReadOnlyList<LabelSummaryDto> Labels,
    ProjectMembershipSummaryDto CreatorMembership,
    ProjectLogoSummaryDto? Logo);
```

- [x] **Step 2: `ProjectMapper`**

```csharp
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Mappers;

public static class ProjectMapper
{
    public static ProjectSummaryDto ToSummary(Project project) => new(
        project.Id, project.Name, project.Identifier, project.CategoryId, project.Description,
        project.LeadId, project.StartDate, project.TargetDate, project.Color,
        project.ActualHours, project.AllocatedHours, project.CompletedHours,
        project.IsActive, project.CreatedAt);

    public static ObjectiveSummaryDto ToSummary(Objective objective) => new(
        objective.Id, objective.ProjectId, objective.IsDefault, objective.Title, objective.OwnerId,
        objective.StartDate, objective.EndDate, objective.AllocatedHours, objective.CompletedHours);

    public static ProjectVersionSummaryDto ToSummary(ProjectVersion version, string statusCode) => new(
        version.Id, version.Name, version.StatusId, statusCode);

    public static ReleaseReminderSummaryDto ToSummary(ReleaseCalendarEntry entry) => new(
        entry.Id, entry.VersionId, entry.ScheduledDate, entry.ReminderType);

    public static LabelSummaryDto ToSummary(Label label) => new(label.Id, label.Name, label.Color);

    public static ProjectMembershipSummaryDto ToSummary(ProjectMember member) => new(
        member.Id, member.ObjectiveId, member.UserId, member.MembershipSource);
}
```

- [x] **Step 3: `CreateProjectCommand`**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(
    Guid CategoryId,
    string Name,
    string Identifier,
    string? Description,
    DateOnly StartDate,
    DateOnly TargetDate,
    DateOnly ReleaseDate,
    string? Color,
    decimal? ActualHours,
    decimal DefaultObjectiveAllocatedHours,
    IReadOnlyList<CreateProjectLabelInput> Labels,
    string? LogoFileName,
    string? LogoContentType,
    Stream? LogoContent
) : IRequest<Result<ProjectCreationResponse>>;
```

`LogoContent` is a plain `Stream`, not `IFormFile` — the controller (Task 7) extracts the stream at the API boundary and passes it down, matching `IFileStorageService.UploadAsync`'s own `Stream content` parameter and the "Handlers must not depend on ... IFormFile" constraint.

- [x] **Step 4: `CreateProjectCommandValidator`**

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must be 200 characters or fewer.");

        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Project identifier is required.")
            .MaximumLength(20).WithMessage("Project identifier must be 20 characters or fewer.")
            .Matches("^[A-Za-z][A-Za-z0-9]*$").WithMessage("Project identifier must start with a letter and contain only letters and digits.");

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage("Category is required.");

        RuleFor(x => x.TargetDate)
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("Target date must not be earlier than start date.");

        RuleFor(x => x.ActualHours)
            .GreaterThanOrEqualTo(0).WithMessage("Actual hours must not be negative.")
            .When(x => x.ActualHours is not null);

        RuleFor(x => x.DefaultObjectiveAllocatedHours)
            .GreaterThanOrEqualTo(0).WithMessage("Allocated hours must not be negative.");

        RuleForEach(x => x.Labels).ChildRules(label =>
        {
            label.RuleFor(l => l.Name)
                .NotEmpty().WithMessage("Label name is required.")
                .MaximumLength(50).WithMessage("Label name must be 50 characters or fewer.");
            label.RuleFor(l => l.Color)
                .NotEmpty().WithMessage("Label color is required.")
                .MaximumLength(20).WithMessage("Label color must be 20 characters or fewer.");
        });

        RuleFor(x => x.Labels)
            .Must(labels => labels
                .Select(l => l.Name.Trim().ToLowerInvariant())
                .Distinct()
                .Count() == labels.Count)
            .WithMessage("Duplicate label names are not allowed in the same request.");
    }
}
```

- [x] **Step 5: Write the failing unit tests**

```csharp
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateProjectCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static CreateProjectCommand ValidCommand(IReadOnlyList<CreateProjectLabelInput>? labels = null) => new(
        CategoryId, "Website Revamp", "WEB", "desc",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15),
        "#2563EB", 10m, 40m, labels ?? [], null, null, null);

    private (CreateProjectCommandHandler Handler, Mock<IProjectRepository> Projects) BuildHandler(
        bool categoryExists = true, bool identifierExists = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categoryExists ? new ProjectCategory { Id = CategoryId, TenantId = TenantId, Name = "General" } : null);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.IdentifierExistsForTenantAsync(TenantId, "WEB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identifierExists);

        var objectives = new Mock<IObjectiveRepository>();
        var members = new Mock<IProjectMemberRepository>();
        var versions = new Mock<IProjectVersionRepository>();
        var releaseCalendar = new Mock<IReleaseCalendarRepository>();
        var labels = new Mock<ILabelRepository>();
        var entityAssets = new Mock<IEntityAssetRepository>();
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, EmployeeNumber = "E1", HireDate = new DateOnly(2020, 1, 1) });

        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(x => x.GetPrimaryByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = LegalEntityId, TenantId = TenantId, IsPrimary = true, Name = "Acme" });

        var auditLogs = new Mock<IAuditLogRepository>();
        var fileStorage = new Mock<IFileStorageService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateProjectCommandHandler(
            currentUser.Object, categories.Object, projects.Object, objectives.Object, members.Object,
            versions.Object, releaseCalendar.Object, labels.Object, entityAssets.Object, employees.Object,
            legalEntities.Object, auditLogs.Object, fileStorage.Object, unitOfWork.Object);

        return (handler, projects);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessWithDefaultObjectiveMembershipVersionAndReminder()
    {
        var (handler, _) = BuildHandler();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Website Revamp", result.Value!.Project.Name);
        Assert.True(result.Value.DefaultObjective.IsDefault);
        Assert.Equal(result.Value.Project.Id, result.Value.DefaultObjective.ProjectId);
        Assert.Equal(1, result.Value.DefaultVersion.StatusId);
        Assert.Equal(result.Value.DefaultVersion.Id, result.Value.ReleaseReminder.VersionId);
        Assert.Equal(UserId, result.Value.CreatorMembership.UserId);
        Assert.Equal(result.Value.DefaultObjective.Id, result.Value.CreatorMembership.ObjectiveId);
    }

    [Fact]
    public async Task Handle_DuplicateIdentifier_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(identifierExists: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundForTenant_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(categoryExists: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllocatedHoursExceedActualHours_StillSucceeds_OverAllocationIsWarningOnly()
    {
        var (handler, _) = BuildHandler();
        var command = ValidCommand() with { ActualHours = 5m, DefaultObjectiveAllocatedHours = 999m };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(999m, result.Value!.DefaultObjective.AllocatedHours);
    }

    [Fact]
    public async Task Handle_DuplicateLabelNamesInRequest_ReturnsValidationConflict()
    {
        var (handler, _) = BuildHandler();
        var command = ValidCommand([new CreateProjectLabelInput("Backend", "#111111"), new CreateProjectLabelInput("backend", "#222222")]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
```

- [x] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateProjectCommandHandlerTests`
Expected: FAIL — `CreateProjectCommandHandler` does not exist yet (compile error).

- [x] **Step 7: `CreateProjectCommandHandler`**

```csharp
using MediatR;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Projects.Mappers;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ReleaseCalendar.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Versions.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Labels.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Labels.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.ReleaseCalendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Versions.Entities;

namespace ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;

public class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Result<ProjectCreationResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectCategoryRepository _categories;
    private readonly IProjectRepository _projects;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IProjectVersionRepository _versions;
    private readonly IReleaseCalendarRepository _releaseCalendar;
    private readonly ILabelRepository _labels;
    private readonly IEntityAssetRepository _entityAssets;
    private readonly IEmployeeRepository _employees;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProjectCommandHandler(
        ICurrentUser currentUser,
        IProjectCategoryRepository categories,
        IProjectRepository projects,
        IObjectiveRepository objectives,
        IProjectMemberRepository members,
        IProjectVersionRepository versions,
        IReleaseCalendarRepository releaseCalendar,
        ILabelRepository labels,
        IEntityAssetRepository entityAssets,
        IEmployeeRepository employees,
        ILegalEntityRepository legalEntities,
        IAuditLogRepository auditLogs,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _categories = categories;
        _projects = projects;
        _objectives = objectives;
        _members = members;
        _versions = versions;
        _releaseCalendar = releaseCalendar;
        _labels = labels;
        _entityAssets = entityAssets;
        _employees = employees;
        _legalEntities = legalEntities;
        _auditLogs = auditLogs;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProjectCreationResponse>> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectCreationResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectCreationResponse>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(tenantId, userId, ct);
        if (employee is null)
            return Result<ProjectCreationResponse>.Forbidden("No employee record for the current user.");

        var legalEntity = await _legalEntities.GetPrimaryByTenantIdAsync(tenantId, ct);
        if (legalEntity is null)
            return Result<ProjectCreationResponse>.Forbidden("Tenant has no primary company configured.");

        var category = await _categories.GetByIdForTenantAsync(tenantId, request.CategoryId, ct);
        if (category is null || !category.IsActive)
            return Result<ProjectCreationResponse>.NotFound("Project category not found.");

        var identifier = request.Identifier.Trim().ToUpperInvariant();
        if (await _projects.IdentifierExistsForTenantAsync(tenantId, identifier, ct))
            return Result<ProjectCreationResponse>.Conflict("A project with this identifier already exists.");

        var normalizedLabelNames = request.Labels
            .Select(l => l.Name.Trim().ToLowerInvariant())
            .ToList();
        if (normalizedLabelNames.Distinct().Count() != normalizedLabelNames.Count)
            return Result<ProjectCreationResponse>.Conflict("Duplicate label names are not allowed in the same request.");

        Domain.Features.Storage.File.DTOs.Responses.FileRecordDto? uploadedLogo = null;
        if (request.LogoContent is not null && request.LogoFileName is not null && request.LogoContentType is not null)
        {
            var uploadResult = await _fileStorage.UploadAsync(
                tenantId, userId, request.LogoFileName, request.LogoContentType,
                UploadPurposeCatalog.ProjectCover, request.LogoContent, ct);

            if (!uploadResult.IsSuccess)
                return Result<ProjectCreationResponse>.Failure(uploadResult.Error!, uploadResult.StatusCode ?? 400);

            uploadedLogo = uploadResult.Value;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;

            var project = new Project
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwningLegalEntityId = legalEntity.Id,
                CategoryId = category.Id,
                Name = request.Name.Trim(),
                Identifier = identifier,
                Description = request.Description?.Trim(),
                LeadId = userId,
                StartDate = request.StartDate,
                TargetDate = request.TargetDate,
                Color = request.Color,
                ActualHours = request.ActualHours,
                AllocatedHours = 0m,
                CompletedHours = 0m,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultObjective = new Objective
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ParentObjectiveId = null,
                IsDefault = true,
                Title = project.Name,
                Description = project.Description,
                OwnerId = userId,
                IsActive = true,
                StartDate = project.StartDate,
                EndDate = project.TargetDate,
                Progress = 0m,
                ActualHours = project.ActualHours,
                AllocatedHours = request.DefaultObjectiveAllocatedHours,
                CompletedHours = 0m,
                CreatedById = userId,
                CreatedAt = now
            };

            var creatorMembership = new ProjectMember
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                ObjectiveId = defaultObjective.Id,
                UserId = userId,
                EmployeeId = employee.Id,
                MembershipSource = ProjectMembershipSources.System,
                IsActive = true,
                JoinedAt = now,
                CreatedById = userId,
                CreatedAt = now
            };

            var defaultVersion = new ProjectVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = "Initial Release",
                StatusId = VersionStatusIds.Planned,
                CreatedById = userId,
                CreatedAt = now
            };

            var releaseReminder = new ReleaseCalendarEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                VersionId = defaultVersion.Id,
                RecipientUserId = userId,
                ScheduledDate = request.ReleaseDate,
                ReminderType = ReleaseReminderTypes.ProjectRelease,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = now
            };

            var labels = request.Labels.Select(l => new Label
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = project.Id,
                Name = l.Name.Trim(),
                Color = l.Color,
                CreatedById = userId,
                CreatedAt = now
            }).ToList();

            EntityAsset? logoAsset = null;
            if (uploadedLogo is not null)
            {
                logoAsset = new EntityAsset
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OwnerType = EntityAssetOwnerTypes.Project,
                    OwnerId = project.Id,
                    AssetPurpose = UploadPurposeCatalog.ProjectCover,
                    FileRecordId = uploadedLogo.Id,
                    IsPrimary = true,
                    CreatedByType = "user",
                    CreatedById = userId,
                    CreatedAt = now
                };
            }

            await _projects.AddAsync(project, ct);
            await _objectives.AddAsync(defaultObjective, ct);
            await _members.AddAsync(creatorMembership, ct);
            await _versions.AddAsync(defaultVersion, ct);
            await _releaseCalendar.AddAsync(releaseReminder, ct);
            foreach (var label in labels)
                await _labels.AddAsync(label, ct);
            if (logoAsset is not null)
                await _entityAssets.AddAsync(logoAsset, ct);

            await _auditLogs.AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Action = "project.created",
                ResourceType = "Project",
                ResourceId = project.Id,
                NewValuesJson = $"{{\"name\":\"{project.Name}\",\"identifier\":\"{project.Identifier}\"}}",
                CreatedAt = now
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);

            var response = new ProjectCreationResponse(
                ProjectMapper.ToSummary(project),
                ProjectMapper.ToSummary(defaultObjective),
                ProjectMapper.ToSummary(defaultVersion, "planned"),
                ProjectMapper.ToSummary(releaseReminder),
                labels.Select(ProjectMapper.ToSummary).ToList(),
                ProjectMapper.ToSummary(creatorMembership),
                uploadedLogo is not null ? new ProjectLogoSummaryDto(uploadedLogo.Id, uploadedLogo.OriginalFileName) : null);

            return Result<ProjectCreationResponse>.Success(response);
        }
        catch
        {
            if (uploadedLogo is not null)
            {
                // Compensate: the business transaction failed after the logo was
                // already uploaded — release the file record via the same
                // IFileStorageService boundary rather than leaving an orphan.
                await _fileStorage.CancelReservationAsync(tenantId, uploadedLogo.Id, "project_creation_failed", ct);
            }
            throw;
        }
    }
}
```

`FileRecordDto.OriginalFileName`/`.Id` field names are assumed from the DTO's evident purpose (`FileUploadReservationDto`/`FileRecordDto` were referenced but not fully read during research) — before this step, read `src/ONEVO.Application/Features/Storage/File/DTOs/Responses/FileRecordDto.cs` and adjust the property names used above (`uploadedLogo.Id`, `uploadedLogo.OriginalFileName`) to match its actual property names if they differ. Likewise confirm `CancelReservationAsync`'s first `Guid` parameter is the right identifier to pass a completed file record's id to — if `CompleteUploadAsync`/`UploadAsync` already finalizes the reservation such that `CancelReservationAsync` no longer applies post-completion, use whatever compensation method `IFileStorageService` actually exposes for a completed upload (re-read the interface's XML doc comments, already captured in full above, before finalizing this step).

- [x] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateProjectCommandHandlerTests -v`
Expected: all 5 tests PASS.

- [x] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Projects tests/ONEVO.Tests.Unit/Features/WorkManagement
git commit -m "feat(work-management): add CreateProjectCommand vertical slice with unit tests"
```

---

### Task 7: `POST /api/v1/work/projects` controller

**Files:**
- Create: `src/ONEVO.Api/Contracts/WorkManagement/Projects/CreateProjectFormRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/WorkManagement/ProjectsController.cs`

**Interfaces:**
- Consumes: `CreateProjectCommand` (Task 6), `[RequirePermission]` (`src/ONEVO.Api/Filters/RequirePermissionAttribute.cs`), `[Idempotent]` (`src/ONEVO.Api/Filters/IdempotentAttribute.cs`).
- Produces: the live `POST /api/v1/work/projects` HTTP endpoint Task 8's integration test calls.

- [x] **Step 1: `CreateProjectFormRequest`**

```csharp
using Microsoft.AspNetCore.Http;

namespace ONEVO.Api.Contracts.WorkManagement.Projects;

public class CreateProjectFormRequest
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public DateOnly ReleaseDate { get; set; }
    public string? Color { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal DefaultObjectiveAllocatedHours { get; set; }

    /// <summary>JSON-encoded array of {"name":"...","color":"..."} objects, sent as a multipart string field.</summary>
    public string? LabelsJson { get; set; }

    public IFormFile? Logo { get; set; }
}
```

- [x] **Step 2: `ProjectsController`**

```csharp
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.WorkManagement.Projects;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProject;
using ONEVO.Application.Features.WorkManagement.Projects.DTOs.Requests;

namespace ONEVO.Api.Controllers.Tenant.WorkManagement;

[ApiController]
[Route("api/v1/work/projects")]
[Authorize(Policy = "TenantPolicy")]
public class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Creates a Project with its Default Objective, creator membership, Default Version, release reminder, optional labels, and optional logo — all in one atomic transaction.</summary>
    [HttpPost]
    [RequirePermission("projects:create")]
    [Idempotent]
    public async Task<IActionResult> Create([FromForm] CreateProjectFormRequest request, CancellationToken ct)
    {
        var labels = string.IsNullOrWhiteSpace(request.LabelsJson)
            ? new List<CreateProjectLabelInput>()
            : JsonSerializer.Deserialize<List<CreateProjectLabelInput>>(
                request.LabelsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        Stream? logoStream = null;
        if (request.Logo is { Length: > 0 } logo)
            logoStream = logo.OpenReadStream();

        var command = new CreateProjectCommand(
            request.CategoryId,
            request.Name,
            request.Identifier,
            request.Description,
            request.StartDate,
            request.TargetDate,
            request.ReleaseDate,
            request.Color,
            request.ActualHours,
            request.DefaultObjectiveAllocatedHours,
            labels,
            request.Logo?.FileName,
            request.Logo?.ContentType,
            logoStream);

        var result = await _mediator.Send(command, ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Project.Id }, result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Placeholder route target for the Create action's Location header. The real GET-by-id read endpoint is built in Slice 2; this action only exists so CreatedAtAction can resolve a route name — it returns 501 until Slice 2 lands.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("projects:read")]
    public IActionResult GetById(Guid id) => StatusCode(501);
}
```

The `GetById` placeholder exists only so `nameof(GetById)` resolves and `CreatedAtAction` can build a valid `Location` header route — it deliberately returns `501 Not Implemented` until Slice 2 replaces it with the real read handler. This keeps `Location: /api/v1/work/projects/{id}` correct today without implementing the actual read query in this slice.

- [x] **Step 3: Verify build**

Run: `dotnet build`
Expected: 0 errors.

- [x] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Contracts/WorkManagement src/ONEVO.Api/Controllers/Tenant/WorkManagement
git commit -m "feat(work-management): add POST /api/v1/work/projects endpoint"
```

---

### Task 8: Integration test — full HTTP flow + tenant RLS isolation

**Files:**
- Test: `tests/ONEVO.Tests.Integration/Features/WorkManagement/CreateProjectEndpointTests.cs`

**Interfaces:**
- Consumes: the existing integration test fixture/factory this test project already uses for authenticated tenant HTTP calls (check `tests/ONEVO.Tests.Integration/Support/` or an existing test file under `tests/ONEVO.Tests.Integration/Features/` for the established `WebApplicationFactory`/fixture pattern, authenticated-client helper, and Testcontainers PostgreSQL setup before writing this file — reuse it exactly rather than inventing a second fixture).

- [x] **Step 1: Read one existing integration test for the fixture pattern**

Before writing this file, open one existing test under `tests/ONEVO.Tests.Integration/Features/` (or `tests/ONEVO.Tests.Integration/Auth/`) to confirm: the base fixture class name, how an authenticated tenant HTTP client is obtained (cookie-based session per the architecture — likely a helper that logs in a seeded dev-smoke tenant user and returns an `HttpClient` with the session cookie attached), and how a second tenant's client is obtained for the cross-tenant assertion. Match that exact pattern in Step 2 below rather than the illustrative structure shown.

- [x] **Step 2: Write the integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ONEVO.Tests.Integration.Features.WorkManagement;

public class CreateProjectEndpointTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public CreateProjectEndpointTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_ValidRequest_Returns201WithDefaultObjectiveVersionAndMembership()
    {
        using var client = await _factory.CreateAuthenticatedTenantClientAsync("acme");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(_factory.AcmeGeneralCategoryId.ToString()), "CategoryId" },
            { new StringContent("Website Revamp"), "Name" },
            { new StringContent("WEB"), "Identifier" },
            { new StringContent("2026-01-01"), "StartDate" },
            { new StringContent("2026-06-01"), "TargetDate" },
            { new StringContent("2026-06-15"), "ReleaseDate" },
            { new StringContent("40"), "DefaultObjectiveAllocatedHours" }
        };

        var response = await client.PostAsync("/api/v1/work/projects", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.GetProperty("defaultObjective").GetProperty("isDefault").GetBoolean());
        Assert.Equal(1, body.GetProperty("defaultVersion").GetProperty("statusId").GetInt32());
    }

    [Fact]
    public async Task Create_DuplicateIdentifierSameTenant_Returns409()
    {
        using var client = await _factory.CreateAuthenticatedTenantClientAsync("acme");

        using var firstForm = BuildForm(_factory.AcmeGeneralCategoryId, "DUP");
        var first = await client.PostAsync("/api/v1/work/projects", firstForm);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var secondForm = BuildForm(_factory.AcmeGeneralCategoryId, "DUP");
        var second = await client.PostAsync("/api/v1/work/projects", secondForm);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_ThenSecondTenantCannotSeeTheProjectRow_TenantRlsHolds()
    {
        using var acmeClient = await _factory.CreateAuthenticatedTenantClientAsync("acme");
        using var form = BuildForm(_factory.AcmeGeneralCategoryId, "ISO1");
        var created = await acmeClient.PostAsync("/api/v1/work/projects", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var projectId = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("project").GetProperty("id").GetGuid();

        // Direct DB read scoped to the OTHER seeded dev-smoke tenant (dapi) must not
        // return the row created above — proves RLS isolation, not just an
        // application-level filter that a raw query could bypass.
        var isVisibleToOtherTenant = await _factory.ExistsForTenantAsync("dapi", projectId);
        Assert.False(isVisibleToOtherTenant);
    }

    private static MultipartFormDataContent BuildForm(Guid categoryId, string identifier) => new()
    {
        { new StringContent(categoryId.ToString()), "CategoryId" },
        { new StringContent("Isolation Check"), "Name" },
        { new StringContent(identifier), "Identifier" },
        { new StringContent("2026-01-01"), "StartDate" },
        { new StringContent("2026-06-01"), "TargetDate" },
        { new StringContent("2026-06-15"), "ReleaseDate" },
        { new StringContent("10"), "DefaultObjectiveAllocatedHours" }
    };
}
```

This test references `IntegrationTestFactory.CreateAuthenticatedTenantClientAsync`, `.AcmeGeneralCategoryId`, and `.ExistsForTenantAsync` as illustrative names — replace them with whatever the actual fixture class and helper methods are named per Step 1's findings. The three behaviors under test (201 + correct nested defaults, 409 on duplicate identifier, and — critically — a second tenant's RLS-scoped connection cannot see the row) must all be preserved regardless of the exact fixture API.

- [x] **Step 3: Run the test to verify it fails, then passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~CreateProjectEndpointTests -v`
Expected: FAILs first for a legitimate reason (missing seeded category, wrong fixture method names — adjust to match Step 1's findings), then PASSes once aligned with the real fixture and once `AcmeGeneralCategoryId` (or equivalent) is seeded — add one `ProjectCategory` row for the `acme` dev-smoke tenant to whatever seeder/fixture setup this test project already uses for per-test data, following that project's existing seeding convention rather than inserting raw SQL in the test itself.

- [x] **Step 4: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/WorkManagement
git commit -m "test(work-management): add CreateProject endpoint integration tests incl. tenant RLS isolation"
```

---

### Task 9: Architecture test — no direct file-storage bypass

**Files:**
- Modify or extend: `tests/ONEVO.Tests.Architecture/` (find the existing `FileStorageArchitectureTests.cs`-style file referenced in the design research and either add a test method to it or create a sibling file in the same folder, matching its existing structure)

**Interfaces:**
- Consumes: the existing `NoApplicationType_BypassesFileStorageService_ByUsingFileRepositoriesDirectly`-style reflection test pattern.

- [x] **Step 1: Read the existing `FileStorageArchitectureTests.cs` in full**

Confirm its exact reflection approach (which assembly/namespace it scans, which types it excludes, how it asserts) before writing this task's addition — it already scans `ONEVO.Application.Features.*` excluding `Storage.File` itself, per the research summary; the Work Management types under `ONEVO.Application.Features.WorkManagement.*` should already be covered by that existing broad scan with zero changes needed. If the existing test's namespace filter is broad enough, this task is: run it and confirm it still passes with the new code added (no test file changes needed). Only add a new test method if the existing one is scoped narrower than `Features.*` (e.g., hard-coded to specific feature namespaces).

- [x] **Step 2: Run the architecture test suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture -v`
Expected: all tests PASS, including the file-storage bypass test, confirming `CreateProjectCommandHandler` only reaches file storage through `IFileStorageService`.

- [x] **Step 3: If the existing test needed a namespace-filter widening, commit that change**

```bash
git add tests/ONEVO.Tests.Architecture
git commit -m "test(work-management): confirm architecture guard covers WorkManagement feature namespace"
```

If no change was needed, skip this commit — do not create an empty commit.

---

## Self-Review

**Spec coverage:** Design doc §4 (creation transaction sequence) → Task 6 handler steps 1-9 in order (resolve context → validate → upload logo → construct 6 entities → one `SaveChangesAsync` → audit → compensate on failure → response). §2 (adaptations table) → reflected directly in entity/config/repo/controller code (`BaseEntity`, `Result<T>` ternary, seeded permission reuse, `[Idempotent]` reuse, `IUnitOfWork`, `entity_assets` scoped to `project`, `xmin`, `Controllers/Tenant/WorkManagement/`). §3 (scope) → Tasks 1-9 build exactly the in-scope tables/endpoint; nothing from "Out" is touched. Tables-doc schema (already updated 2026-08-03) → every entity property in Task 1 matches a documented column.

**Placeholder scan:** No TBD/TODO. Two explicit "confirm before finalizing" notes remain in Task 6 Step 7 (exact `FileRecordDto` property names) and Task 8 Step 1 (exact integration fixture API) — these are legitimate "read the real file first" research steps, not vague placeholders; the behavior and intent at each point is fully specified either way.

**Type consistency:** `Result<ProjectCreationResponse>` used consistently from `CreateProjectCommand` (Task 6 Step 3) through the handler (Step 7) to the controller (Task 7 Step 2). `ProjectVersion` (not `Version`) used consistently across Tasks 1, 2, 4, 6. Repository interface method names introduced in Task 4 (`GetByIdForTenantAsync`, `IdentifierExistsForTenantAsync`, `AddAsync` ×7, `GetByUserIdAsync`) match exactly what Task 6's handler and its mocked unit tests call. Permission codes seeded in Task 5 (`projects:create`, `projects:read`) match exactly what Task 7's `[RequirePermission(...)]` attributes declare.
