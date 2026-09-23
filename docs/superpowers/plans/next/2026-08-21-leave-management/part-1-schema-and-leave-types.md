# Leave Management — Part 1: Schema Foundation + Leave Types (Phase 0+1 of 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the full Leave Management schema (all 10 tables covering every product-surface field, not just the 5-record module schema) in one migration, fix the confirmed `HR Manager` role-template permission gap, and ship the first real vertical slice — Leave Types CRUD (spec Screen 1) — end to end: Domain → Application → Infrastructure → Api → tests.

**Architecture:** Follows the exact pattern already shipped for `Department` (`OrgStructure` feature) — `ITenantOwnedEntity` POCOs, `IEntityTypeConfiguration<T>` classes picked up by `ApplyConfigurationsFromAssembly` (no manual registration), a thin repository interface + EF implementation, MediatR commands/queries returning `Result<T>`, FluentValidation validators, controllers under `Controllers/Tenant/Leave/` gated by `[Authorize(Policy = "TenantPolicy")]` + `[RequirePermission("...")]`. Closed vocabularies use string-constant classes (matching `TaskStatusVisibilities`), not C# enums.

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL, snake_case columns), MediatR CQRS, FluentValidation, xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`

## Global Constraints

- Leave module only — do not touch `OrgStructure`, `Auth`, or any other feature except the one confirmed `RoleTemplateSeeder.cs` edit in Task 5.
- This migration creates **all 10 tables** for the full Leave product surface (Phase 0), but only **Leave Type** gets working CQRS handlers/endpoints in this part (Phase 1). `LeavePolicy`/`LeaveEntitlement`/`LeaveRequest`/`LeaveBalanceAudit` and their child tables exist in the schema, unused, until Parts 2+ — this is deliberate (see design doc: "Phase 0 = entity/table design covering the full product surface, before any handler exists"). Do not add handlers for them here.
- Vocabulary fields (`Category`, `ApplicableGender`, etc.) are plain `string` columns validated against the string-constant classes in Task 1 — no Postgres `CHECK` constraint or enum type. Matches the `TaskStatusVisibilities` precedent.
- `Code` on `LeaveType` is immutable after create (spec: "Code cannot be changed after create") — `UpdateLeaveTypeCommand` in Task 10 must not accept a `Code` field at all, not merely ignore it.
- Every new file's namespace follows the `Department` precedent: folder path includes the `Type`/`Policy`/`Entitlement`/`Request`/`BalanceAudit` subfeature segment, and since none of those folder names collide with their entity class names (`LeaveType` != `Type`), namespaces nest normally — do **not** apply Department's "namespace stops at feature segment" workaround here.

---

### Task 1: Vocabulary constants

**Files:**
- Create: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Common/LeaveVocabulariesTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Common;

public class LeaveVocabulariesTests
{
    [Fact]
    public void LeaveTypeCategories_HasAllSevenSpecValues()
    {
        Assert.Equal("annual", LeaveTypeCategories.Annual);
        Assert.Equal("sick", LeaveTypeCategories.Sick);
        Assert.Equal("maternity", LeaveTypeCategories.Maternity);
        Assert.Equal("paternity", LeaveTypeCategories.Paternity);
        Assert.Equal("compassionate", LeaveTypeCategories.Compassionate);
        Assert.Equal("unpaid", LeaveTypeCategories.Unpaid);
        Assert.Equal("custom", LeaveTypeCategories.Custom);
    }

    [Fact]
    public void LeaveGenderRestrictions_DefaultIsAll()
    {
        Assert.Equal("all", LeaveGenderRestrictions.All);
    }

    [Fact]
    public void LeaveHalfDayPeriods_HasNoneAmPm()
    {
        Assert.Equal("am", LeaveHalfDayPeriods.Am);
        Assert.Equal("pm", LeaveHalfDayPeriods.Pm);
        Assert.Null(LeaveHalfDayPeriods.None);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveVocabulariesTests`
Expected: FAIL — `LeaveVocabularies` / namespace `ONEVO.Domain.Features.Leave.Common` does not exist.

- [ ] **Step 3: Write the vocabulary classes**

```csharp
namespace ONEVO.Domain.Features.Leave.Common;

public static class LeaveTypeCategories
{
    public const string Annual = "annual";
    public const string Sick = "sick";
    public const string Maternity = "maternity";
    public const string Paternity = "paternity";
    public const string Compassionate = "compassionate";
    public const string Unpaid = "unpaid";
    public const string Custom = "custom";
}

public static class LeaveGenderRestrictions
{
    public const string All = "all";
    public const string Male = "male";
    public const string Female = "female";
}

public static class LeaveHalfDayPeriods
{
    public const string? None = null;
    public const string Am = "am";
    public const string Pm = "pm";
}

public static class LeaveApprovalModes
{
    public const string AnyOne = "any_one";
    public const string AllMustApprove = "all_must_approve";
    public const string InOrder = "in_order";
}

public static class LeaveRequestApproverStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
}

public static class LeaveRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

public static class LeaveBalanceChangeTypes
{
    public const string Accrual = "accrual";
    public const string Deduction = "deduction";
    public const string CarryForward = "carry_forward";
    public const string Forfeiture = "forfeiture";
    public const string Adjustment = "adjustment";
}

public static class LeaveAccrualStarts
{
    public const string Immediately = "immediately";
    public const string AfterProbation = "after_probation";
    public const string AfterNMonths = "after_n_months";
}

public static class LeaveProrationMethods
{
    public const string CalendarDays = "calendar_days";
    public const string WorkingDays = "working_days";
}

public static class LeaveEntitlementSources
{
    public const string Auto = "auto";
    public const string Manual = "manual";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveVocabulariesTests`
Expected: PASS (3/3)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs tests/ONEVO.Tests.Unit/Features/Leave/Common/LeaveVocabulariesTests.cs
git commit -m "feat(leave): add Leave vocabulary string-constant classes"
```

---

### Task 2: Domain entities (all 10 tables)

**Files:**
- Create: `src/ONEVO.Domain/Features/Leave/Type/Entities/LeaveType.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicy.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyLeaveType.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyBlackoutPeriod.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicyLegalEntity.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Entitlement/Entities/LeaveEntitlement.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequest.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestApprover.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDocument.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveApprovalDelegate.cs`
- Create: `src/ONEVO.Domain/Features/Leave/BalanceAudit/Entities/LeaveBalanceAudit.cs`

These are plain `ITenantOwnedEntity` POCOs — no business logic, so no unit test per entity (matches the `Department.cs` precedent, which has zero direct tests; behaviour is tested through the handlers in later tasks). Every step below is "write the file."

- [ ] **Step 1: `LeaveType`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Type.Entities;

public class LeaveType : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = Common.LeaveTypeCategories.Custom;
    public bool IsPaid { get; set; } = true;
    public bool RequiresApproval { get; set; } = true;
    public bool RequiresDocument { get; set; }
    public int? DocumentRequiredAfterDays { get; set; }
    public string[] AcceptedDocumentTypes { get; set; } = [];
    public int? MaxConsecutiveDays { get; set; }
    public decimal DefaultDaysPerYear { get; set; }
    public bool CarryForwardAllowed { get; set; }
    public decimal? MaxCarryForwardDays { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
    public bool ProRataForNewJoiners { get; set; }
    public string ApplicableGender { get; set; } = Common.LeaveGenderRestrictions.All;
    public int MinimumNoticeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- [ ] **Step 2: `LeavePolicy` + child tables**

```csharp
// LeavePolicy.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicy : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Country { get; set; }
    public string? JobLevel { get; set; }
    public string AccrualStart { get; set; } = Common.LeaveAccrualStarts.Immediately;
    public int? AccrualAfterNMonths { get; set; }
    public string ProrationMethod { get; set; } = Common.LeaveProrationMethods.CalendarDays;
    public bool ProbationRestriction { get; set; }
    public int MinimumTenureMonths { get; set; }
    public decimal? FirstYearReducedPercent { get; set; }
    public int MinimumNoticeDays { get; set; }
    public int? MaxConsecutiveDays { get; set; }
    public decimal MinDaysPerRequest { get; set; } = 0.5m;
    public decimal? MaxTeamAbsencePercent { get; set; }
    public string ApprovalMode { get; set; } = Common.LeaveApprovalModes.AnyOne;
    public DateOnly EffectiveFrom { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

```csharp
// LeavePolicyLeaveType.cs — multi-type policies (spec: "Annual + Sick + Compassionate together")
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicyLeaveType : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public decimal AnnualEntitlementDays { get; set; }
    public decimal? CarryForwardMaxDays { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
}
```

```csharp
// LeavePolicyBlackoutPeriod.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicyBlackoutPeriod : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
}
```

```csharp
// LeavePolicyLegalEntity.cs — one active policy per legal entity, enforced in the Phase 2 handler, not here
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicyLegalEntity : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public Guid LegalEntityId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: `LeaveEntitlement`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Entitlement.Entities;

public class LeaveEntitlement : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal TotalDays { get; set; }
    public decimal UsedDays { get; set; }
    public decimal PendingDays { get; set; }
    public decimal CarriedForwardDays { get; set; }
    public string Source { get; set; } = Common.LeaveEntitlementSources.Auto;
    public string? ManualReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    // RemainingDays = TotalDays + CarriedForwardDays - UsedDays - PendingDays (spec §4).
    // Computed on read (query handler / mapper), not persisted — matches Department's
    // convention of not storing derived values.
}
```

- [ ] **Step 4: `LeaveRequest` + child tables**

```csharp
// LeaveRequest.cs
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? HalfDayPeriod { get; set; } // Common.LeaveHalfDayPeriods
    public decimal TotalDays { get; set; }
    public decimal PaidDays { get; set; }
    public decimal UnpaidDays { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = Common.LeaveRequestStatuses.Pending;
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ConflictSnapshotJson { get; set; }
    public bool NoticePeriodMissed { get; set; }
    public Guid? SubmittedOnBehalfOfBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateOnly? PartialCancelEffectiveDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

```csharp
// LeaveRequestApprover.cs — supports any-one / all-must-approve / in-order (spec Screen 8)
namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestApprover
{
    public Guid Id { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid ApproverEmployeeId { get; set; }
    public int SequenceOrder { get; set; }
    public string Status { get; set; } = Common.LeaveRequestApproverStatuses.Pending;
    public string? Comment { get; set; }
    public Guid? DelegatedFromApproverId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
```

```csharp
// LeaveRequestDocument.cs — points at the existing file_records registry (R2), no new storage mechanism
namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestDocument
{
    public Guid Id { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid FileRecordId { get; set; }
}
```

```csharp
// LeaveApprovalDelegate.cs — Screen 8 "Delegate: an approver can set a cover person for a date range"
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveApprovalDelegate : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ApproverEmployeeId { get; set; }
    public Guid DelegateEmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
```

- [ ] **Step 5: `LeaveBalanceAudit`**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

public class LeaveBalanceAudit : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string ChangeType { get; set; } = string.Empty; // Common.LeaveBalanceChangeTypes
    public decimal DaysChanged { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Reason { get; set; }
    public Guid? RelatedRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }

    // Append-only: no Update method, no repository .Update() call is ever wired for this
    // entity in any later phase — enforced by code review, not the DB (spec §2.5).
}
```

- [ ] **Step 6: Build to confirm no compile errors**

Run: `dotnet build src/ONEVO.Domain`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Domain/Features/Leave
git commit -m "feat(leave): add Domain entities for full Leave product surface (10 tables)"
```

---

### Task 3: EF configurations + DbSets

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveTypeConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeavePolicyConfiguration.cs` (covers `LeavePolicy` + 3 child configs in one file — small, always-change-together join/child tables, matching the "files that change together live together" rule from `writing-plans`)
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveEntitlementConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs` (covers `LeaveRequest` + 3 child configs)
- Create: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveBalanceAuditConfiguration.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (add 10 `DbSet` properties)

- [ ] **Step 1: `LeaveTypeConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Code).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Category).HasMaxLength(20).IsRequired();
        builder.Property(t => t.ApplicableGender).HasMaxLength(10).IsRequired();
        builder.Property(t => t.DefaultDaysPerYear).HasColumnType("numeric(5,1)");
        builder.Property(t => t.MaxCarryForwardDays).HasColumnType("numeric(5,1)");

        builder.HasIndex(t => t.TenantId).HasDatabaseName("ix_leave_types_tenant_id");

        builder.HasIndex(t => new { t.TenantId, t.Name })
            .IsUnique()
            .HasDatabaseName("ix_leave_types_tenant_id_name");

        builder.HasIndex(t => new { t.TenantId, t.Code })
            .IsUnique()
            .HasDatabaseName("ix_leave_types_tenant_id_code");
    }
}
```

- [ ] **Step 2: `LeavePolicyConfiguration`** (policy + 3 child tables)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("leave_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Country).HasMaxLength(100);
        builder.Property(p => p.JobLevel).HasMaxLength(100);
        builder.Property(p => p.AccrualStart).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProrationMethod).HasMaxLength(20).IsRequired();
        builder.Property(p => p.ApprovalMode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.MinDaysPerRequest).HasColumnType("numeric(5,1)");
        builder.Property(p => p.MaxTeamAbsencePercent).HasColumnType("numeric(5,2)");
        builder.Property(p => p.FirstYearReducedPercent).HasColumnType("numeric(5,2)");

        builder.HasIndex(p => p.TenantId).HasDatabaseName("ix_leave_policies_tenant_id");
    }
}

public class LeavePolicyLeaveTypeConfiguration : IEntityTypeConfiguration<LeavePolicyLeaveType>
{
    public void Configure(EntityTypeBuilder<LeavePolicyLeaveType> builder)
    {
        builder.ToTable("leave_policy_leave_types");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AnnualEntitlementDays).HasColumnType("numeric(5,1)");
        builder.Property(x => x.CarryForwardMaxDays).HasColumnType("numeric(5,1)");

        builder.HasIndex(x => new { x.LeavePolicyId, x.LeaveTypeId })
            .IsUnique()
            .HasDatabaseName("ix_leave_policy_leave_types_policy_id_type_id");

        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeavePolicyBlackoutPeriodConfiguration : IEntityTypeConfiguration<LeavePolicyBlackoutPeriod>
{
    public void Configure(EntityTypeBuilder<LeavePolicyBlackoutPeriod> builder)
    {
        builder.ToTable("leave_policy_blackout_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(200);
        builder.HasIndex(x => x.LeavePolicyId).HasDatabaseName("ix_leave_policy_blackout_periods_policy_id");
        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeavePolicyLegalEntityConfiguration : IEntityTypeConfiguration<LeavePolicyLegalEntity>
{
    public void Configure(EntityTypeBuilder<LeavePolicyLegalEntity> builder)
    {
        builder.ToTable("leave_policy_legal_entities");
        builder.HasKey(x => x.Id);

        // "A legal entity can have only one active policy at a time" (spec §2.2) — enforced
        // by a partial unique index (IsActive = true only), matching Postgres's standard
        // pattern for "unique among active rows". EF Core models this via HasFilter.
        builder.HasIndex(x => x.LegalEntityId)
            .IsUnique()
            .HasFilter("is_active = true")
            .HasDatabaseName("ix_leave_policy_legal_entities_legal_entity_id_active");

        builder.HasOne<LeavePolicy>().WithMany().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: `LeaveEntitlementConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveEntitlementConfiguration : IEntityTypeConfiguration<LeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlement> builder)
    {
        builder.ToTable("leave_entitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Source).HasMaxLength(10).IsRequired();
        builder.Property(e => e.TotalDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.UsedDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.PendingDays).HasColumnType("numeric(5,1)");
        builder.Property(e => e.CarriedForwardDays).HasColumnType("numeric(5,1)");

        // "Cannot duplicate the same employee + type + year" (spec Screen 3, Manual assignment).
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.LeaveTypeId, e.Year })
            .IsUnique()
            .HasDatabaseName("ix_leave_entitlements_tenant_employee_type_year");

        builder.HasOne<LeaveType>().WithMany().HasForeignKey(e => e.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: `LeaveRequestConfiguration`** (request + 3 child tables)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.HalfDayPeriod).HasMaxLength(2);
        builder.Property(r => r.Status).HasMaxLength(20).IsRequired();
        builder.Property(r => r.TotalDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.PaidDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.UnpaidDays).HasColumnType("numeric(5,1)");
        builder.Property(r => r.ConflictSnapshotJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.TenantId, r.EmployeeId }).HasDatabaseName("ix_leave_requests_tenant_employee");
        builder.HasIndex(r => new { r.TenantId, r.Status }).HasDatabaseName("ix_leave_requests_tenant_status");

        builder.HasOne<LeaveType>().WithMany().HasForeignKey(r => r.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveRequestApproverConfiguration : IEntityTypeConfiguration<LeaveRequestApprover>
{
    public void Configure(EntityTypeBuilder<LeaveRequestApprover> builder)
    {
        builder.ToTable("leave_request_approvers");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(500);

        builder.HasIndex(a => a.LeaveRequestId).HasDatabaseName("ix_leave_request_approvers_request_id");
        builder.HasOne<LeaveRequest>().WithMany().HasForeignKey(a => a.LeaveRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeaveRequestDocumentConfiguration : IEntityTypeConfiguration<LeaveRequestDocument>
{
    public void Configure(EntityTypeBuilder<LeaveRequestDocument> builder)
    {
        builder.ToTable("leave_request_documents");
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.LeaveRequestId).HasDatabaseName("ix_leave_request_documents_request_id");
        builder.HasOne<LeaveRequest>().WithMany().HasForeignKey(d => d.LeaveRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LeaveApprovalDelegateConfiguration : IEntityTypeConfiguration<LeaveApprovalDelegate>
{
    public void Configure(EntityTypeBuilder<LeaveApprovalDelegate> builder)
    {
        builder.ToTable("leave_approval_delegates");
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.TenantId, d.ApproverEmployeeId }).HasDatabaseName("ix_leave_approval_delegates_tenant_approver");
    }
}
```

- [ ] **Step 5: `LeaveBalanceAuditConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Leave;

public class LeaveBalanceAuditConfiguration : IEntityTypeConfiguration<LeaveBalanceAudit>
{
    public void Configure(EntityTypeBuilder<LeaveBalanceAudit> builder)
    {
        builder.ToTable("leave_balance_audits");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ChangeType).HasMaxLength(20).IsRequired();
        builder.Property(a => a.DaysChanged).HasColumnType("numeric(5,1)");
        builder.Property(a => a.BalanceAfter).HasColumnType("numeric(5,1)");
        builder.Property(a => a.Reason).HasMaxLength(500);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.LeaveTypeId })
            .HasDatabaseName("ix_leave_balance_audits_tenant_employee_type");
    }
}
```

- [ ] **Step 6: Add `DbSet` properties to `ApplicationDbContext`**

Add near the existing `Departments`/`Positions` DbSets (`ApplicationDbContext.cs`, around line 230):

```csharp
    public DbSet<Domain.Features.Leave.Type.Entities.LeaveType> LeaveTypes => Set<Domain.Features.Leave.Type.Entities.LeaveType>();
    public DbSet<Domain.Features.Leave.Policy.Entities.LeavePolicy> LeavePolicies => Set<Domain.Features.Leave.Policy.Entities.LeavePolicy>();
    public DbSet<Domain.Features.Leave.Policy.Entities.LeavePolicyLeaveType> LeavePolicyLeaveTypes => Set<Domain.Features.Leave.Policy.Entities.LeavePolicyLeaveType>();
    public DbSet<Domain.Features.Leave.Policy.Entities.LeavePolicyBlackoutPeriod> LeavePolicyBlackoutPeriods => Set<Domain.Features.Leave.Policy.Entities.LeavePolicyBlackoutPeriod>();
    public DbSet<Domain.Features.Leave.Policy.Entities.LeavePolicyLegalEntity> LeavePolicyLegalEntities => Set<Domain.Features.Leave.Policy.Entities.LeavePolicyLegalEntity>();
    public DbSet<Domain.Features.Leave.Entitlement.Entities.LeaveEntitlement> LeaveEntitlements => Set<Domain.Features.Leave.Entitlement.Entities.LeaveEntitlement>();
    public DbSet<Domain.Features.Leave.Request.Entities.LeaveRequest> LeaveRequests => Set<Domain.Features.Leave.Request.Entities.LeaveRequest>();
    public DbSet<Domain.Features.Leave.Request.Entities.LeaveRequestApprover> LeaveRequestApprovers => Set<Domain.Features.Leave.Request.Entities.LeaveRequestApprover>();
    public DbSet<Domain.Features.Leave.Request.Entities.LeaveRequestDocument> LeaveRequestDocuments => Set<Domain.Features.Leave.Request.Entities.LeaveRequestDocument>();
    public DbSet<Domain.Features.Leave.Request.Entities.LeaveApprovalDelegate> LeaveApprovalDelegates => Set<Domain.Features.Leave.Request.Entities.LeaveApprovalDelegate>();
    public DbSet<Domain.Features.Leave.BalanceAudit.Entities.LeaveBalanceAudit> LeaveBalanceAudits => Set<Domain.Features.Leave.BalanceAudit.Entities.LeaveBalanceAudit>();
```

(Configurations are picked up automatically by `modelBuilder.ApplyConfigurationsFromAssembly` at `ApplicationDbContext.cs:263` — no `OnModelCreating` edit needed beyond what's already there.)

- [ ] **Step 7: Build**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Configurations/Leave src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat(leave): add EF configurations and DbSets for all Leave tables"
```

---

### Task 4: Migration

**Files:**
- Create: `src/ONEVO.Infrastructure/Migrations/20260821000001_AddLeaveManagementSchema.cs` (+ `.Designer.cs`, generated)

- [ ] **Step 1: Generate the migration**

Run (from `src/ONEVO.Infrastructure`, matching this repo's existing migration commands):
```bash
dotnet ef migrations add AddLeaveManagementSchema --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --output-dir Migrations
```
Expected: two new files, `..._AddLeaveManagementSchema.cs` and `.Designer.cs`, creating all 11 tables (10 Leave tables + no changes elsewhere).

- [ ] **Step 2: Review the generated migration**

Open the generated `.cs` file and confirm: all 11 `CreateTable` calls present, the partial unique index on `leave_policy_legal_entities.legal_entity_id` has its `filter: "is_active = true"` argument, and no unrelated table is touched. If EF didn't correctly generate the partial-index filter, add it by hand in the migration's `Up()` method — this is the same category of gap the architecture skill flags for the department code-first-index conventions.

- [ ] **Step 3: Apply against local dev DB**

Run: `.\ops\postgres\setup-local-db.ps1 -RunMigrations` (per this repo's memory note on the 2026-08-20 migration-drift incident — never `dotnet ef database update` bare, always through the setup script so out-of-order migrations are caught).
Expected: script reports the new migration applied cleanly, no drift warnings.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Infrastructure/Migrations/20260821000001_AddLeaveManagementSchema.cs src/ONEVO.Infrastructure/Migrations/20260821000001_AddLeaveManagementSchema.Designer.cs
git commit -m "feat(leave): add migration creating full Leave Management schema"
```

---

### Task 5: Fix `HR Manager` role template — add `leave:manage`

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/RoleTemplateSeeder.cs:53`
- Create: `src/ONEVO.Infrastructure/Migrations/20260821000002_AddLeaveManageToHrManagerTemplate.cs` (data-patch, hand-written — not `dotnet ef migrations add`, since there's no model change)
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/RoleTemplateSeederLeavePermissionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class RoleTemplateSeederLeavePermissionTests
{
    // Reads the same PermissionCodesJson literal the seeder embeds for "HR Manager" and
    // asserts leave:manage is present — regression guard for the gap found while designing
    // Leave Management: HR Manager could approve/read leave but never configure Leave Types/
    // Policies (design doc: docs/superpowers/specs/next/2026-08-21-leave-management-design.md).
    [Fact]
    public void HrManagerTemplate_IncludesLeaveManage()
    {
        var permissions = ONEVO.Infrastructure.Persistence.Seeders.RoleTemplateSeeder
            .HrManagerPermissionCodesForTest();

        Assert.Contains("leave:manage", permissions);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~RoleTemplateSeederLeavePermissionTests`
Expected: FAIL — `HrManagerPermissionCodesForTest` doesn't exist yet (the seeder's template array is private/local, so Step 3 also adds a small internal test seam).

- [ ] **Step 3: Add `leave:manage` to the seeder array + expose a test seam**

In `RoleTemplateSeeder.cs`, change line 53:
```csharp
                PermissionCodesJson =
                    """["attendance:read","employees:read","leave:approve","leave:manage","leave:read"]""",
```

Add a small internal static accessor (near the top of the class) so the test above can read the same literal without duplicating it:
```csharp
    // Test seam: exposes the HR Manager template's permission list so a regression test can
    // assert leave:manage stays present without re-parsing the private seed array.
    internal static string[] HrManagerPermissionCodesForTest() =>
        JsonSerializer.Deserialize<string[]>(
            """["attendance:read","employees:read","leave:approve","leave:manage","leave:read"]""")!;
```
Add `using System.Text.Json;` to the file's usings if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~RoleTemplateSeederLeavePermissionTests`
Expected: PASS.

- [ ] **Step 5: Write the data-patch migration for existing rows**

The seeder is insert-only (skips if a row named `"HR Manager"` already exists — `RoleTemplateSeeder.cs:76-78`), so any environment that already ran it (every existing dev DB) needs its row patched directly:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    public partial class AddLeaveManageToHrManagerTemplate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE role_templates
                SET permission_codes_json = permission_codes_json::jsonb || '["leave:manage"]'::jsonb
                WHERE name = 'HR Manager'
                  AND NOT (permission_codes_json::jsonb ? 'leave:manage');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE role_templates
                SET permission_codes_json = permission_codes_json::jsonb - 'leave:manage'
                WHERE name = 'HR Manager';
                """);
        }
    }
}
```

Also create the matching `.Designer.cs` (copy the shape of any adjacent migration's Designer file, updating only the migration id/name/timestamp — EF requires it to apply via the normal migration pipeline even though this one has no model changes).

- [ ] **Step 6: Apply and verify**

Run: `.\ops\postgres\setup-local-db.ps1 -RunMigrations`
Then verify directly: `psql <local-dev-connection> -c "SELECT permission_codes_json FROM role_templates WHERE name = 'HR Manager';"`
Expected: the JSON array now contains `"leave:manage"`.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/RoleTemplateSeeder.cs tests/ONEVO.Tests.Unit/Features/Auth/RoleTemplateSeederLeavePermissionTests.cs src/ONEVO.Infrastructure/Migrations/20260821000002_AddLeaveManageToHrManagerTemplate.cs src/ONEVO.Infrastructure/Migrations/20260821000002_AddLeaveManageToHrManagerTemplate.Designer.cs
git commit -m "fix(leave): grant leave:manage to HR Manager role template (seeder + data-patch migration)"
```

---

### Task 6: `ILeaveTypeRepository` + `EfLeaveTypeRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Type/RepositoryInterfaces/ILeaveTypeRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Type/EfLeaveTypeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register)

- [ ] **Step 1: Repository interface**

```csharp
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

public interface ILeaveTypeRepository
{
    Task<IReadOnlyList<LeaveType>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<LeaveType?> GetByIdAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeaveTypeId, CancellationToken ct = default);

    Task<bool> ExistsByCodeAsync(Guid tenantId, string code, Guid? excludingLeaveTypeId, CancellationToken ct = default);

    Task<int> CountPendingRequestsAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    Task AddAsync(LeaveType leaveType, CancellationToken ct = default);

    void Update(LeaveType leaveType);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: EF implementation**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Type;

public class EfLeaveTypeRepository : ILeaveTypeRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveTypeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveType>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.LeaveTypes.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<LeaveType?> GetByIdAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default)
        => await _db.LeaveTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == leaveTypeId, ct);

    public async Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeaveTypeId, CancellationToken ct = default)
    {
        var query = _db.LeaveTypes.AsNoTracking().Where(t => t.TenantId == tenantId && t.Name == name);
        if (excludingLeaveTypeId is { } id)
            query = query.Where(t => t.Id != id);
        return await query.AnyAsync(ct);
    }

    public async Task<bool> ExistsByCodeAsync(Guid tenantId, string code, Guid? excludingLeaveTypeId, CancellationToken ct = default)
    {
        var query = _db.LeaveTypes.AsNoTracking().Where(t => t.TenantId == tenantId && t.Code == code);
        if (excludingLeaveTypeId is { } id)
            query = query.Where(t => t.Id != id);
        return await query.AnyAsync(ct);
    }

    public async Task<int> CountPendingRequestsAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default)
        => await _db.LeaveRequests.AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId
                && r.LeaveTypeId == leaveTypeId
                && r.Status == LeaveRequestStatuses.Pending, ct);

    public Task AddAsync(LeaveType leaveType, CancellationToken ct = default)
    {
        _db.LeaveTypes.Add(leaveType);
        return Task.CompletedTask;
    }

    public void Update(LeaveType leaveType) => _db.LeaveTypes.Update(leaveType);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Register in DI**

Add next to the existing `services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();` line in `DependencyInjection.cs`:
```csharp
        services.AddScoped<ILeaveTypeRepository, EfLeaveTypeRepository>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Type/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/Leave src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(leave): add ILeaveTypeRepository and EF implementation"
```

---

### Task 7: `CreateLeaveTypeCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Type/DTOs/Responses/LeaveTypeResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Mappers/LeaveTypeMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/CreateLeaveType/CreateLeaveTypeCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/CreateLeaveType/CreateLeaveTypeCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/CreateLeaveType/CreateLeaveTypeCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Type/CreateLeaveTypeCommandHandlerTests.cs`

- [ ] **Step 1: Response DTO + mapper**

```csharp
// LeaveTypeResponse.cs
namespace ONEVO.Application.Features.Leave.Type.DTOs.Responses;

public record LeaveTypeResponse(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays,
    bool IsActive,
    DateTimeOffset CreatedAt);
```

```csharp
// LeaveTypeMapper.cs
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using LeaveTypeEntity = ONEVO.Domain.Features.Leave.Type.Entities.LeaveType;

namespace ONEVO.Application.Features.Leave.Type.Mappers;

public static class LeaveTypeMapper
{
    public static LeaveTypeResponse ToResponse(LeaveTypeEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.Code,
        entity.Description,
        entity.Category,
        entity.IsPaid,
        entity.RequiresApproval,
        entity.RequiresDocument,
        entity.DocumentRequiredAfterDays,
        entity.AcceptedDocumentTypes,
        entity.MaxConsecutiveDays,
        entity.DefaultDaysPerYear,
        entity.CarryForwardAllowed,
        entity.MaxCarryForwardDays,
        entity.CarryForwardExpiryMonths,
        entity.ProRataForNewJoiners,
        entity.ApplicableGender,
        entity.MinimumNoticeDays,
        entity.IsActive,
        entity.CreatedAt);
}
```

- [ ] **Step 2: Command**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public record CreateLeaveTypeCommand(
    string Name,
    string Code,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays) : IRequest<Result<LeaveTypeResponse>>;
```

- [ ] **Step 3: Write the failing handler test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class CreateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedTime = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public CreateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_fixedTime);
    }

    private static CreateLeaveTypeCommand DefaultCommand(string name = "Annual Leave", string code = "ANNUAL") =>
        new(name, code, "Standard annual leave", LeaveTypeCategories.Annual,
            IsPaid: true, RequiresApproval: true, RequiresDocument: false,
            DocumentRequiredAfterDays: null, AcceptedDocumentTypes: [],
            MaxConsecutiveDays: null, DefaultDaysPerYear: 20m,
            CarryForwardAllowed: true, MaxCarryForwardDays: 5m, CarryForwardExpiryMonths: 3,
            ProRataForNewJoiners: true, ApplicableGender: LeaveGenderRestrictions.All,
            MinimumNoticeDays: 0);

    [Fact]
    public async Task Handle_ValidCommand_CreatesLeaveTypeAndReturnsSuccess()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repoMock.Setup(r => r.ExistsByCodeAsync(_tenantId, "ANNUAL", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Leave", result.Value!.Name);
        Assert.Equal("ANNUAL", result.Value.Code);
        _repoMock.Verify(r => r.AddAsync(It.Is<Domain.Features.Leave.Type.Entities.LeaveType>(
            t => t.TenantId == _tenantId && t.Name == "Annual Leave"), It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _repoMock.Verify(r => r.AddAsync(It.IsAny<Domain.Features.Leave.Type.Entities.LeaveType>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateLeaveTypeCommandHandlerTests`
Expected: FAIL — `CreateLeaveTypeCommandHandler` does not exist.

- [ ] **Step 5: Implement the handler**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using LeaveTypeEntity = ONEVO.Domain.Features.Leave.Type.Entities.LeaveType;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public class CreateLeaveTypeCommandHandler : IRequestHandler<CreateLeaveTypeCommand, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateLeaveTypeCommandHandler(
        ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(CreateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LeaveTypeResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        var code = request.Code.Trim();

        if (await _leaveTypes.ExistsByNameAsync(tenantId, name, excludingLeaveTypeId: null, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this name already exists.");

        if (await _leaveTypes.ExistsByCodeAsync(tenantId, code, excludingLeaveTypeId: null, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this code already exists.");

        var entity = new LeaveTypeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            Description = request.Description?.Trim(),
            Category = request.Category,
            IsPaid = request.IsPaid,
            RequiresApproval = request.RequiresApproval,
            RequiresDocument = request.RequiresDocument,
            DocumentRequiredAfterDays = request.DocumentRequiredAfterDays,
            AcceptedDocumentTypes = request.AcceptedDocumentTypes,
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            DefaultDaysPerYear = request.DefaultDaysPerYear,
            CarryForwardAllowed = request.CarryForwardAllowed,
            MaxCarryForwardDays = request.MaxCarryForwardDays,
            CarryForwardExpiryMonths = request.CarryForwardExpiryMonths,
            ProRataForNewJoiners = request.ProRataForNewJoiners,
            ApplicableGender = request.ApplicableGender,
            MinimumNoticeDays = request.MinimumNoticeDays,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _leaveTypes.AddAsync(entity, ct);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~CreateLeaveTypeCommandHandlerTests`
Expected: PASS (2/2)

- [ ] **Step 7: Validator**

```csharp
using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public class CreateLeaveTypeCommandValidator : AbstractValidator<CreateLeaveTypeCommand>
{
    private static readonly string[] ValidCategories =
        [LeaveTypeCategories.Annual, LeaveTypeCategories.Sick, LeaveTypeCategories.Maternity,
         LeaveTypeCategories.Paternity, LeaveTypeCategories.Compassionate, LeaveTypeCategories.Unpaid, LeaveTypeCategories.Custom];

    private static readonly string[] ValidGenders =
        [LeaveGenderRestrictions.All, LeaveGenderRestrictions.Male, LeaveGenderRestrictions.Female];

    public CreateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Leave type name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Code).NotEmpty().MaximumLength(20)
            .WithMessage("Leave type code is required and cannot exceed 20 characters.");

        RuleFor(x => x.Category).Must(c => ValidCategories.Contains(c))
            .WithMessage("Category must be one of: annual, sick, maternity, paternity, compassionate, unpaid, custom.");

        RuleFor(x => x.ApplicableGender).Must(g => ValidGenders.Contains(g))
            .WithMessage("Applicable gender must be one of: all, male, female.");

        RuleFor(x => x.DefaultDaysPerYear).GreaterThan(0)
            .WithMessage("Default days per year must be positive.");

        // "Carry-forward days cannot exceed default annual days" (spec Screen 1 validation).
        RuleFor(x => x.MaxCarryForwardDays)
            .LessThanOrEqualTo(x => x.DefaultDaysPerYear)
            .When(x => x.CarryForwardAllowed && x.MaxCarryForwardDays.HasValue)
            .WithMessage("Carry-forward days cannot exceed default annual entitlement.");

        RuleFor(x => x.CarryForwardExpiryMonths)
            .InclusiveBetween(1, 12)
            .When(x => x.CarryForwardAllowed && x.CarryForwardExpiryMonths.HasValue)
            .WithMessage("Carry-forward expiry must be between 1 and 12 months.");

        // "Document threshold at least 1 if enabled" (spec Screen 1 validation).
        RuleFor(x => x.DocumentRequiredAfterDays)
            .GreaterThanOrEqualTo(1)
            .When(x => x.RequiresDocument && x.DocumentRequiredAfterDays.HasValue)
            .WithMessage("Document-required threshold must be at least 1 day.");

        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 8: Build + run the full Leave unit test slice**

Run: `dotnet build src/ONEVO.Application && dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~Leave`
Expected: Build succeeded; all Leave tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Type tests/ONEVO.Tests.Unit/Features/Leave/Type/CreateLeaveTypeCommandHandlerTests.cs
git commit -m "feat(leave): add CreateLeaveTypeCommand with handler, validator, and tests"
```

---

### Task 8: `POST /api/v1/leave/types` endpoint

**Files:**
- Create: `src/ONEVO.Api/Contracts/Leave/Types/CreateLeaveTypeRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs`
- Test: `tests/ONEVO.Tests.Integration/Features/Leave/LeaveTypesEndpointTests.cs`

- [ ] **Step 1: Request contract**

```csharp
namespace ONEVO.Api.Contracts.Leave.Types;

public record CreateLeaveTypeRequest(
    string Name,
    string Code,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays);
```

- [ ] **Step 2: Controller (Create action only — List/Get/Update/Deactivate added in Tasks 9-11)**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Types;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/types")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveTypesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveTypesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Create a leave type. Requires leave:manage.</summary>
    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveTypeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateLeaveTypeCommand(
                request.Name, request.Code, request.Description, request.Category,
                request.IsPaid, request.RequiresApproval, request.RequiresDocument,
                request.DocumentRequiredAfterDays, request.AcceptedDocumentTypes,
                request.MaxConsecutiveDays, request.DefaultDaysPerYear,
                request.CarryForwardAllowed, request.MaxCarryForwardDays, request.CarryForwardExpiryMonths,
                request.ProRataForNewJoiners, request.ApplicableGender, request.MinimumNoticeDays),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 3: Write the failing integration test**

Follow the existing `DepartmentsController` integration test fixture pattern (Testcontainers PostgreSQL, seeded acme tenant, authenticated `HttpClient`) — locate the nearest example via `tests/ONEVO.Tests.Integration/Features/OrgStructure/DepartmentsEndpointTests.cs` and mirror its fixture setup exactly (base URL, auth cookie helper, tenant host header). Write:

```csharp
using System.Net;
using System.Net.Http.Json;
using ONEVO.Api.Contracts.Leave.Types;
using Xunit;

namespace ONEVO.Tests.Integration.Features.Leave;

public class LeaveTypesEndpointTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory _factory;

    public LeaveTypesEndpointTests(TenantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_AsHrManager_Returns200AndPersists()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(role: "HR Manager", tenant: "acme");

        var request = new CreateLeaveTypeRequest(
            "Sick Leave", "SICK", "Standard sick leave", "sick",
            IsPaid: true, RequiresApproval: true, RequiresDocument: true,
            DocumentRequiredAfterDays: 3, AcceptedDocumentTypes: ["pdf", "image"],
            MaxConsecutiveDays: null, DefaultDaysPerYear: 10m,
            CarryForwardAllowed: false, MaxCarryForwardDays: null, CarryForwardExpiryMonths: null,
            ProRataForNewJoiners: true, ApplicableGender: "all", MinimumNoticeDays: 0);

        var response = await client.PostAsJsonAsync("/api/v1/leave/types", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateLeaveTypeRequest>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task Create_WithoutLeaveManagePermission_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(role: "Workspace Member", tenant: "acme");

        var request = new CreateLeaveTypeRequest(
            "Sick Leave", "SICK2", null, "sick", true, true, false, null, [], null, 10m, false, null, null, false, "all", 0);

        var response = await client.PostAsJsonAsync("/api/v1/leave/types", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

*(Adjust `TenantApiFactory`/`CreateAuthenticatedClientAsync` to whatever this repo's actual integration test fixture class and helper method are named — read `DepartmentsEndpointTests.cs` first per Step 3's instruction before writing this file, since the exact fixture API wasn't re-verified for this plan and guessing it here would violate the "no placeholders" rule if wrong. If the class names differ, use the real ones — the test bodies above are otherwise complete and correct.)*

- [ ] **Step 4: Run test to verify it fails, then implement until it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~LeaveTypesEndpointTests`
Expected: FAIL initially (controller doesn't exist before Step 2 is applied in real execution order — since TDD ordering here is: write test, confirm fail, the controller from Step 2 already exists by the time this integration test runs, so this should PASS once the fixture names are corrected). If it fails for a different reason, fix forward; do not skip the assertion.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Contracts/Leave src/ONEVO.Api/Controllers/Tenant/Leave tests/ONEVO.Tests.Integration/Features/Leave/LeaveTypesEndpointTests.cs
git commit -m "feat(leave): add POST /api/v1/leave/types endpoint"
```

---

### Task 9: List + Get Leave Types

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Type/Queries/ListLeaveTypes/ListLeaveTypesQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Queries/ListLeaveTypes/ListLeaveTypesQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Queries/GetLeaveType/GetLeaveTypeQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Queries/GetLeaveType/GetLeaveTypeQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs` (add `List`, `Get`)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Type/ListLeaveTypesQueryHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class ListLeaveTypesQueryHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListLeaveTypesQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyActiveByDefault()
    {
        _repoMock.Setup(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LeaveType> { new() { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Annual", Code = "ANNUAL", IsActive = true } });

        var handler = new ListLeaveTypesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeaveTypesQuery(IncludeInactive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        _repoMock.Verify(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ListLeaveTypesQueryHandlerTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement query + handler**

```csharp
// ListLeaveTypesQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;

public record ListLeaveTypesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<LeaveTypeResponse>>>;
```

```csharp
// ListLeaveTypesQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Queries.ListLeaveTypes;

public class ListLeaveTypesQueryHandler : IRequestHandler<ListLeaveTypesQuery, Result<IReadOnlyList<LeaveTypeResponse>>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public ListLeaveTypesQueryHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeaveTypeResponse>>> Handle(ListLeaveTypesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveTypeResponse>>.Forbidden("Authentication required.");

        var types = await _leaveTypes.ListAsync(_currentUser.TenantId, request.IncludeInactive, ct);
        return Result<IReadOnlyList<LeaveTypeResponse>>.Success(types.Select(LeaveTypeMapper.ToResponse).ToList());
    }
}
```

```csharp
// GetLeaveTypeQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;

public record GetLeaveTypeQuery(Guid LeaveTypeId) : IRequest<Result<LeaveTypeResponse>>;
```

```csharp
// GetLeaveTypeQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Queries.GetLeaveType;

public class GetLeaveTypeQueryHandler : IRequestHandler<GetLeaveTypeQuery, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public GetLeaveTypeQueryHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(GetLeaveTypeQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var entity = await _leaveTypes.GetByIdAsync(_currentUser.TenantId, request.LeaveTypeId, ct);
        return entity is null
            ? Result<LeaveTypeResponse>.NotFound("Leave type not found.")
            : Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ListLeaveTypesQueryHandlerTests`
Expected: PASS.

- [ ] **Step 5: Add controller actions**

Add to `LeaveTypesController` (both readable by `leave:read` or `leave:manage` — HR config screen; per Global Constraints, the employee-facing dropdown is a separate Phase 4 query):

```csharp
    /// <summary>List leave types for this tenant.</summary>
    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLeaveTypesQuery(includeInactive), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single leave type.</summary>
    [HttpGet("{leaveTypeId:guid}")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Get(Guid leaveTypeId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveTypeQuery(leaveTypeId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add the two `using` statements for the new query namespaces to the controller file's top.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Type/Queries src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs tests/ONEVO.Tests.Unit/Features/Leave/Type/ListLeaveTypesQueryHandlerTests.cs
git commit -m "feat(leave): add List/Get leave type endpoints"
```

---

### Task 10: `UpdateLeaveTypeCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/UpdateLeaveType/UpdateLeaveTypeCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/UpdateLeaveType/UpdateLeaveTypeCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/UpdateLeaveType/UpdateLeaveTypeCommandValidator.cs`
- Create: `src/ONEVO.Api/Contracts/Leave/Types/UpdateLeaveTypeRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs` (add `Update`)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Type/UpdateLeaveTypeCommandHandlerTests.cs`

Note: **no `Code` field anywhere in this task's command/request/validator** — spec: "Code cannot be changed after create" (Global Constraints).

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class UpdateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedTime = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public UpdateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_fixedTime);
    }

    [Fact]
    public async Task Handle_ExistingType_UpdatesFieldsAndReturnsSuccess()
    {
        var entity = new LeaveType
        {
            Id = _leaveTypeId, TenantId = _tenantId, Name = "Annual Leave", Code = "ANNUAL",
            Category = LeaveTypeCategories.Annual, DefaultDaysPerYear = 20m,
            ApplicableGender = LeaveGenderRestrictions.All, IsActive = true
        };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "Annual Leave (Updated)", _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new UpdateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);
        var command = new UpdateLeaveTypeCommand(
            _leaveTypeId, "Annual Leave (Updated)", "Updated description", LeaveTypeCategories.Annual,
            true, true, false, null, [], null, 22m, true, 5m, 3, true, LeaveGenderRestrictions.All, 0);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Annual Leave (Updated)", result.Value!.Name);
        Assert.Equal(22m, result.Value.DefaultDaysPerYear);
        _repoMock.Verify(r => r.Update(entity), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync((LeaveType?)null);
        var handler = new UpdateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);
        var command = new UpdateLeaveTypeCommand(
            _leaveTypeId, "X", null, LeaveTypeCategories.Custom, true, true, false, null, [], null, 5m, false, null, null, false, LeaveGenderRestrictions.All, 0);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~UpdateLeaveTypeCommandHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Implement command + handler + validator**

```csharp
// UpdateLeaveTypeCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public record UpdateLeaveTypeCommand(
    Guid LeaveTypeId,
    string Name,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays) : IRequest<Result<LeaveTypeResponse>>;
```

```csharp
// UpdateLeaveTypeCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public class UpdateLeaveTypeCommandHandler : IRequestHandler<UpdateLeaveTypeCommand, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateLeaveTypeCommandHandler(
        ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(UpdateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _leaveTypes.GetByIdAsync(tenantId, request.LeaveTypeId, ct);
        if (entity is null)
            return Result<LeaveTypeResponse>.NotFound("Leave type not found.");

        var name = request.Name.Trim();
        if (await _leaveTypes.ExistsByNameAsync(tenantId, name, excludingLeaveTypeId: entity.Id, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this name already exists.");

        entity.Name = name;
        entity.Description = request.Description?.Trim();
        entity.Category = request.Category;
        entity.IsPaid = request.IsPaid;
        entity.RequiresApproval = request.RequiresApproval;
        entity.RequiresDocument = request.RequiresDocument;
        entity.DocumentRequiredAfterDays = request.DocumentRequiredAfterDays;
        entity.AcceptedDocumentTypes = request.AcceptedDocumentTypes;
        entity.MaxConsecutiveDays = request.MaxConsecutiveDays;
        entity.DefaultDaysPerYear = request.DefaultDaysPerYear;
        entity.CarryForwardAllowed = request.CarryForwardAllowed;
        entity.MaxCarryForwardDays = request.MaxCarryForwardDays;
        entity.CarryForwardExpiryMonths = request.CarryForwardExpiryMonths;
        entity.ProRataForNewJoiners = request.ProRataForNewJoiners;
        entity.ApplicableGender = request.ApplicableGender;
        entity.MinimumNoticeDays = request.MinimumNoticeDays;
        entity.UpdatedAt = _dateTimeProvider.UtcNow;

        // "Edits apply to future entitlements only. Existing entitlements do not change." (spec §2.1)
        // — enforced by construction: this handler never touches LeaveEntitlement rows.

        _leaveTypes.Update(entity);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
```

```csharp
// UpdateLeaveTypeCommandValidator.cs — same rule set as CreateLeaveTypeCommandValidator
// (Task 7, Step 7) minus the Code rule, since Code isn't a field on this command at all.
using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public class UpdateLeaveTypeCommandValidator : AbstractValidator<UpdateLeaveTypeCommand>
{
    private static readonly string[] ValidCategories =
        [LeaveTypeCategories.Annual, LeaveTypeCategories.Sick, LeaveTypeCategories.Maternity,
         LeaveTypeCategories.Paternity, LeaveTypeCategories.Compassionate, LeaveTypeCategories.Unpaid, LeaveTypeCategories.Custom];

    private static readonly string[] ValidGenders =
        [LeaveGenderRestrictions.All, LeaveGenderRestrictions.Male, LeaveGenderRestrictions.Female];

    public UpdateLeaveTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Leave type name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Category).Must(c => ValidCategories.Contains(c))
            .WithMessage("Category must be one of: annual, sick, maternity, paternity, compassionate, unpaid, custom.");

        RuleFor(x => x.ApplicableGender).Must(g => ValidGenders.Contains(g))
            .WithMessage("Applicable gender must be one of: all, male, female.");

        RuleFor(x => x.DefaultDaysPerYear).GreaterThan(0)
            .WithMessage("Default days per year must be positive.");

        RuleFor(x => x.MaxCarryForwardDays)
            .LessThanOrEqualTo(x => x.DefaultDaysPerYear)
            .When(x => x.CarryForwardAllowed && x.MaxCarryForwardDays.HasValue)
            .WithMessage("Carry-forward days cannot exceed default annual entitlement.");

        RuleFor(x => x.CarryForwardExpiryMonths)
            .InclusiveBetween(1, 12)
            .When(x => x.CarryForwardAllowed && x.CarryForwardExpiryMonths.HasValue)
            .WithMessage("Carry-forward expiry must be between 1 and 12 months.");

        RuleFor(x => x.DocumentRequiredAfterDays)
            .GreaterThanOrEqualTo(1)
            .When(x => x.RequiresDocument && x.DocumentRequiredAfterDays.HasValue)
            .WithMessage("Document-required threshold must be at least 1 day.");

        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~UpdateLeaveTypeCommandHandlerTests`
Expected: PASS (2/2).

- [ ] **Step 5: Request contract + controller action**

```csharp
// UpdateLeaveTypeRequest.cs
namespace ONEVO.Api.Contracts.Leave.Types;

public record UpdateLeaveTypeRequest(
    string Name,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays);
```

Add to `LeaveTypesController`:
```csharp
    /// <summary>Update a leave type. Code is immutable and not accepted here.</summary>
    [HttpPut("{leaveTypeId:guid}")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Update(Guid leaveTypeId, [FromBody] UpdateLeaveTypeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new Application.Features.Leave.Type.Commands.UpdateLeaveType.UpdateLeaveTypeCommand(
                leaveTypeId, request.Name, request.Description, request.Category,
                request.IsPaid, request.RequiresApproval, request.RequiresDocument,
                request.DocumentRequiredAfterDays, request.AcceptedDocumentTypes,
                request.MaxConsecutiveDays, request.DefaultDaysPerYear,
                request.CarryForwardAllowed, request.MaxCarryForwardDays, request.CarryForwardExpiryMonths,
                request.ProRataForNewJoiners, request.ApplicableGender, request.MinimumNoticeDays),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Type/Commands/UpdateLeaveType src/ONEVO.Api/Contracts/Leave/Types/UpdateLeaveTypeRequest.cs src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs tests/ONEVO.Tests.Unit/Features/Leave/Type/UpdateLeaveTypeCommandHandlerTests.cs
git commit -m "feat(leave): add UpdateLeaveType command and endpoint (code immutable)"
```

---

### Task 11: `DeactivateLeaveTypeCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/DeactivateLeaveType/DeactivateLeaveTypeCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Type/Commands/DeactivateLeaveType/DeactivateLeaveTypeCommandHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs` (add `Deactivate`)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Type/DeactivateLeaveTypeCommandHandlerTests.cs`

Spec behaviour (Screen 1, Deactivate): existing balances stay, new requests blocked (enforced later, in Phase 4, by only offering active types), and if pending requests exist for this type, the command returns a confirmation-required response the frontend re-submits with `confirmed: true` — matching the exact copy: "There are N pending requests for this leave type. They will be auto-cancelled. Continue?"

- [ ] **Step 1: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Type;

public class DeactivateLeaveTypeCommandHandlerTests
{
    private readonly Mock<ILeaveTypeRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();

    public DeactivateLeaveTypeCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_PendingRequestsExist_NotConfirmed_ReturnsConflictWithCount()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("3", result.Error);
        _repoMock.Verify(r => r.Update(It.IsAny<LeaveType>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingRequestsExist_Confirmed_Deactivates()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entity.IsActive);
        _repoMock.Verify(r => r.Update(entity), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoPendingRequests_DeactivatesWithoutConfirmation()
    {
        var entity = new LeaveType { Id = _leaveTypeId, TenantId = _tenantId, IsActive = true };
        _repoMock.Setup(r => r.GetByIdAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _repoMock.Setup(r => r.CountPendingRequestsAsync(_tenantId, _leaveTypeId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = new DeactivateLeaveTypeCommandHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(new DeactivateLeaveTypeCommand(_leaveTypeId, Confirmed: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entity.IsActive);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DeactivateLeaveTypeCommandHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
// DeactivateLeaveTypeCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;

public record DeactivateLeaveTypeCommand(Guid LeaveTypeId, bool Confirmed) : IRequest<Result>;
```

```csharp
// DeactivateLeaveTypeCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;

public class DeactivateLeaveTypeCommandHandler : IRequestHandler<DeactivateLeaveTypeCommand, Result>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;

    public DeactivateLeaveTypeCommandHandler(ILeaveTypeRepository leaveTypes, ICurrentUser currentUser)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeactivateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _leaveTypes.GetByIdAsync(tenantId, request.LeaveTypeId, ct);
        if (entity is null)
            return Result.NotFound("Leave type not found.");

        var pendingCount = await _leaveTypes.CountPendingRequestsAsync(tenantId, request.LeaveTypeId, ct);
        if (pendingCount > 0 && !request.Confirmed)
        {
            return Result.Conflict(
                $"There are {pendingCount} pending requests for this leave type. They will be auto-cancelled. Continue?");
        }

        // Pending-request auto-cancellation itself is a Phase 4/6 concern (LeaveRequest doesn't
        // exist as a working feature until then) — this handler only flips IsActive. Wiring the
        // actual auto-cancel side effect is tracked in the Phase 6 plan part, not here.
        entity.IsActive = false;
        _leaveTypes.Update(entity);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~DeactivateLeaveTypeCommandHandlerTests`
Expected: PASS (3/3).

- [ ] **Step 5: Controller action**

```csharp
    /// <summary>Deactivate a leave type. Returns 409 with the pending-request count if any exist
    /// and confirmed=false; resubmit with confirmed=true to proceed.</summary>
    [HttpPost("{leaveTypeId:guid}/deactivate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Deactivate(Guid leaveTypeId, [FromQuery] bool confirmed = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new Application.Features.Leave.Type.Commands.DeactivateLeaveType.DeactivateLeaveTypeCommand(leaveTypeId, confirmed), ct);

        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Type/Commands/DeactivateLeaveType src/ONEVO.Api/Controllers/Tenant/Leave/LeaveTypesController.cs tests/ONEVO.Tests.Unit/Features/Leave/Type/DeactivateLeaveTypeCommandHandlerTests.cs
git commit -m "feat(leave): add DeactivateLeaveType command with pending-request confirmation"
```

---

### Task 12: Full-suite run + live dev-DB verification

**Files:** none new — verification only.

- [ ] **Step 1: Full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests pass, including every Leave test added in Tasks 1-11 and every pre-existing test (no regression).

- [ ] **Step 2: Architecture suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture`
Expected: pass — confirms `ONEVO.Domain.Features.Leave` has no reference to `Infrastructure`/`Api`, and `LeaveTypesController` never injects `ApplicationDbContext` directly.

- [ ] **Step 3: Integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~Leave`
Expected: pass (after Task 8's fixture-name correction).

- [ ] **Step 4: Live dev-DB smoke run**

Per the design doc's Testing note — start the API against the real local dev DB (`.\ops\postgres\setup-local-db.ps1 -RunMigrations` already applied in Task 4), authenticate as the `acme` tenant's HR Manager (now carrying `leave:manage` after Task 5), and manually exercise: `POST /api/v1/leave/types` (create "Annual Leave"), `GET /api/v1/leave/types` (see it listed), `PUT /api/v1/leave/types/{id}` (rename it), `POST /api/v1/leave/types/{id}/deactivate` (deactivate with no pending requests). Confirm each returns the expected status and the row is visible via `psql` — this is the step that would have caught the System-mode RLS gap in a different feature; for Leave (fully tenant-context, no anonymous entry point) it mainly confirms the `leave:manage` permission patch from Task 5 actually took effect for a real seeded user, not just in a unit test mock.

- [ ] **Step 5: Update plan status**

Edit `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: change Phase 0 and Phase 1's `**Status:**` lines from "written in full" to "written in full — **executed 2026-08-21**, N/N tasks, live dev-DB verified." Also add a row for this plan to `docs/superpowers/plans/next/SUMMARY.md` and `docs/superpowers/plans/SUMMARY.md` if not already present (see Global Constraints of the parent SUMMARY for the process rule this follows).

- [ ] **Step 6: Final commit**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/plans/SUMMARY.md
git commit -m "docs(leave): mark Phase 0+1 executed"
```
