# Leave Management - Part 2: Leave Policies (Phase 2 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend Leave Policies slice for Screen 2: policy list/get, create with multi-type rules, blackout periods, legal-entity assignment with replace confirmation, and clone, all behind `/api/v1/leave/policies`.

**Architecture:** This continues Part 1's Leave backend shape: tenant-owned Domain entities already exist, Application owns MediatR commands/queries/validators/DTOs, Infrastructure owns EF repositories, and Api controllers stay thin with `[Authorize(Policy = "TenantPolicy")]` plus `[RequirePermission("leave:read")]` or `[RequirePermission("leave:manage")]`. Part 2 includes one additive schema correction because Part 1 created `LeavePolicy.AccrualStart` and `ProrationMethod` but did not persist the spec-required accrual method; add `LeavePolicy.AccrualMethod` before wiring handlers.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat `C:\HR\leave-management-complete.md` as product context only. The user's active request is this Part 2 backend plan.
- Leave Types backend from Part 1 is assumed present and executed: `LeaveType`, `ILeaveTypeRepository`, `/api/v1/leave/types`, and the leave schema migration already exist.
- Do not rebuild tables created in Part 1. The only schema change in this part is adding `leave_policies.accrual_method` and the matching `LeaveAccrualMethods` vocabulary.
- Country is required by the Screen 2 create flow and error copy ("Country is required to determine statutory compliance"), even though the database column remains nullable to preserve the future global-policy escape hatch described in the product guide.
- `LeavePolicyLeaveType.AnnualEntitlementDays` remains the persisted yearly amount. For monthly UI input, accept `MonthlyAccrualDays` in the command/request and store `MonthlyAccrualDays * 12` as `AnnualEntitlementDays`.
- No business value may be hardcoded in production code. Country, legal entity, annual entitlement, monthly accrual amount, carry-forward cap, expiry months, accrual method/start, proration method, notice days, max consecutive days, min days per request, max team absence percent, approval mode, effective dates, blackout dates, and legal-entity replacement confirmation all come from request/configured policy data.
- Test fixtures may use concrete sample values such as `LK`, `20m`, or `2026-01-01`, but they must be treated as fixture data only. Name fixture helpers/constants accordingly and add at least one test proving a non-fixture value from the request is persisted, so the implementation cannot accidentally bake the fixture value into production handlers.
- Legal entity replacement must be explicit. If any selected legal entity already has an active leave-policy assignment, return 409 unless `ConfirmReplaceExistingLegalEntityAssignments` is true.
- Replacing legal-entity assignments must be atomic with the new policy creation. Use a repository method that opens a transaction, deactivates existing active assignments, inserts the new policy aggregate, saves, and commits.
- Phase 2 does not generate entitlements, recalculate balances, submit requests, or enforce blackout/team-absence rules on requests. Those behaviours start in Phases 3 and 4.
- Keep closed vocabularies as string constants; do not add C# enums or PostgreSQL enum/check constraints.

---

### Task 1: Add policy accrual method vocabulary and schema correction

**Files:**
- Modify: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs`
- Modify: `src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicy.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeavePolicyConfiguration.cs`
- Create: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddLeavePolicyAccrualMethod.cs`
- Modify: `src/ONEVO.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Common/LeaveVocabulariesTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/LeavePolicyArchitectureTests.cs`

**Interfaces:**
- Produces: `LeaveAccrualMethods.Annual`, `LeaveAccrualMethods.Monthly`, `LeaveAccrualMethods.Daily`, `LeaveAccrualMethods.All`
- Produces: `LeavePolicy.AccrualMethod : string`

- [ ] **Step 1: Extend the vocabulary test first**

Add these tests to `LeaveVocabulariesTests`:

```csharp
[Fact]
public void LeaveAccrualMethods_HasSpecValues()
{
    Assert.Equal("annual", LeaveAccrualMethods.Annual);
    Assert.Equal("monthly", LeaveAccrualMethods.Monthly);
    Assert.Equal("daily", LeaveAccrualMethods.Daily);
    Assert.Contains("annual", LeaveAccrualMethods.All);
    Assert.Contains("monthly", LeaveAccrualMethods.All);
    Assert.Contains("daily", LeaveAccrualMethods.All);
}

[Fact]
public void LeavePolicyVocabularies_ExposeAllCollectionsForValidators()
{
    Assert.Contains(LeaveApprovalModes.AnyOne, LeaveApprovalModes.All);
    Assert.Contains(LeaveAccrualStarts.Immediately, LeaveAccrualStarts.All);
    Assert.Contains(LeaveProrationMethods.CalendarDays, LeaveProrationMethods.All);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveVocabulariesTests`

Expected: FAIL because `LeaveAccrualMethods` and the new `All` arrays do not exist.

- [ ] **Step 3: Add vocabulary constants**

Patch `LeaveVocabularies.cs` with these exact additions:

```csharp
public static class LeaveAccrualMethods
{
    public const string Annual = "annual";
    public const string Monthly = "monthly";
    public const string Daily = "daily";

    public static readonly string[] All = [Annual, Monthly, Daily];
}
```

Also add these arrays to the existing classes:

```csharp
public static readonly string[] All = [AnyOne, AllMustApprove, InOrder];
```

inside `LeaveApprovalModes`, this array inside `LeaveAccrualStarts`:

```csharp
public static readonly string[] All = [Immediately, AfterProbation, AfterNMonths];
```

and this array inside `LeaveProrationMethods`:

```csharp
public static readonly string[] All = [CalendarDays, WorkingDays];
```

- [ ] **Step 4: Add the Domain property**

Patch `LeavePolicy.cs`:

```csharp
public string AccrualMethod { get; set; } = string.Empty;
```

Place it immediately before `AccrualStart` so the policy fields read as method -> start -> proration.

- [ ] **Step 5: Map the EF column**

Patch `LeavePolicyConfiguration.Configure`:

```csharp
builder.Property(p => p.AccrualMethod).HasMaxLength(20).IsRequired();
```

Place it before the existing `AccrualStart` mapping.

- [ ] **Step 6: Add the EF migration**

Run:

```bash
dotnet ef migrations add AddLeavePolicyAccrualMethod --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Replace the generated migration body with this shape. The `"annual"` value here is a one-time compatibility backfill for policies inserted before `accrual_method` existed; do not leave an ongoing database default and do not set a Domain default.

```csharp
migrationBuilder.AddColumn<string>(
    name: "accrual_method",
    table: "leave_policies",
    type: "character varying(20)",
    maxLength: 20,
    nullable: true);

migrationBuilder.Sql("""
    UPDATE leave_policies
    SET accrual_method = 'annual'
    WHERE accrual_method IS NULL;
    """);

migrationBuilder.AlterColumn<string>(
    name: "accrual_method",
    table: "leave_policies",
    type: "character varying(20)",
    maxLength: 20,
    nullable: false,
    oldClrType: typeof(string),
    oldType: "character varying(20)",
    oldMaxLength: 20,
    oldNullable: true);
```

and `Down` drops the same column:

```csharp
migrationBuilder.DropColumn(
    name: "accrual_method",
    table: "leave_policies");
```

- [ ] **Step 7: Add architecture guard**

Create `LeavePolicyArchitectureTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeavePolicyArchitectureTests
{
    [Fact]
    public void LeavePolicy_IsTenantOwned()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(LeavePolicy)));
    }

    [Fact]
    public void LeavePolicy_HasAccrualMethodProperty()
    {
        var property = typeof(LeavePolicy).GetProperty(nameof(LeavePolicy.AccrualMethod));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }

    [Fact]
    public void Model_LeavePolicy_AccrualMethod_MapsToRequiredColumn()
    {
        using var context = CreateModelInspectionContext();

        var entityType = context.Model.GetEntityTypes().Single(e => e.ClrType == typeof(LeavePolicy));
        var property = entityType.FindProperty(nameof(LeavePolicy.AccrualMethod));

        Assert.NotNull(property);
        Assert.Equal("accrual_method", property!.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(20, property.GetMaxLength());
    }

    [Fact]
    public void LeaveAccrualMethods_UsesStringConstants()
    {
        Assert.Equal("annual", LeaveAccrualMethods.Annual);
        Assert.Equal("monthly", LeaveAccrualMethods.Monthly);
        Assert.Equal("daily", LeaveAccrualMethods.Daily);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"leave-policy-arch-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
```

- [ ] **Step 8: Run tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveVocabulariesTests
dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~LeavePolicyArchitectureTests
```

Expected: both pass.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs src/ONEVO.Domain/Features/Leave/Policy/Entities/LeavePolicy.cs src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeavePolicyConfiguration.cs src/ONEVO.Infrastructure/Migrations tests/ONEVO.Tests.Unit/Features/Leave/Common/LeaveVocabulariesTests.cs tests/ONEVO.Tests.Architecture/LeavePolicyArchitectureTests.cs
git commit -m "feat(leave): add policy accrual method schema"
```

---

### Task 2: Leave policy repository, DTOs, and mapper

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyRepository.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/DTOs/Responses/LeavePolicyResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Mappers/LeavePolicyMapper.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/EfLeavePolicyRepositoryTests.cs`

**Interfaces:**
- Consumes: Part 1 entities `LeavePolicy`, `LeavePolicyLeaveType`, `LeavePolicyBlackoutPeriod`, `LeavePolicyLegalEntity`, `LeaveType`, `LegalEntity`
- Produces: `ILeavePolicyRepository`
- Produces: `LeavePolicyResponse`
- Produces: `LeavePolicyMapper.ToResponse(LeavePolicyAggregate aggregate)`

- [ ] **Step 1: Write repository tests first**

Create `EfLeavePolicyRepositoryTests.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class EfLeavePolicyRepositoryTests
{
    [Fact]
    public async Task ListAsync_ReturnsTenantPoliciesWithTypeAndLegalEntityNames()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "ANNUAL");
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var policy = CreatePolicy(tenantId, "LK Policy");
        db.LeaveTypes.Add(leaveType);
        db.LegalEntities.Add(legalEntity);
        db.LeavePolicies.Add(policy);
        db.LeavePolicyLeaveTypes.Add(new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
            LeaveTypeId = leaveType.Id, AnnualEntitlementDays = 20m
        });
        db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
            LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
        });
        db.LeavePolicies.Add(CreatePolicy(otherTenantId, "Other Tenant Policy"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeavePolicyRepository(db);

        var results = await repo.ListAsync(tenantId, includeInactive: false, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("LK Policy", results[0].Policy.Name);
        Assert.Single(results[0].LeaveTypes);
        Assert.Equal("Annual Leave", results[0].LeaveTypes[0].LeaveTypeName);
        Assert.Single(results[0].LegalEntities);
        Assert.Equal("Acme Lanka", results[0].LegalEntities[0].LegalEntityName);
    }

    [Fact]
    public async Task ListActiveAssignmentConflictsAsync_ReturnsOnlyActiveSelectedLegalEntities()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var selected = CreateLegalEntity(tenantId, "Acme Lanka");
        var notSelected = CreateLegalEntity(tenantId, "Acme UK");
        var policy = CreatePolicy(tenantId, "Existing Policy");
        db.LegalEntities.AddRange(selected, notSelected);
        db.LeavePolicies.Add(policy);
        db.LeavePolicyLegalEntities.AddRange(
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
                LegalEntityId = selected.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            },
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policy.Id,
                LegalEntityId = notSelected.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeavePolicyRepository(db);

        var conflicts = await repo.ListActiveAssignmentConflictsAsync(
            tenantId, [selected.Id], CancellationToken.None);

        Assert.Single(conflicts);
        Assert.Equal(selected.Id, conflicts[0].LegalEntityId);
        Assert.Equal("Acme Lanka", conflicts[0].LegalEntityName);
        Assert.Equal("Existing Policy", conflicts[0].ActivePolicyName);
    }

    [Fact]
    public async Task AddAggregateWithReplacementAsync_DeactivatesOldAssignmentsAndCreatesNewAggregate()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var leaveType = CreateLeaveType(tenantId, "Annual Leave", "ANNUAL");
        var legalEntity = CreateLegalEntity(tenantId, "Acme Lanka");
        var oldPolicy = CreatePolicy(tenantId, "Old Policy");
        db.LeaveTypes.Add(leaveType);
        db.LegalEntities.Add(legalEntity);
        db.LeavePolicies.Add(oldPolicy);
        db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = oldPolicy.Id,
            LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var newPolicy = CreatePolicy(tenantId, "New Policy");
        var typeRules = new[]
        {
            new LeavePolicyLeaveType
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = newPolicy.Id,
                LeaveTypeId = leaveType.Id, AnnualEntitlementDays = 20m
            }
        };
        var assignments = new[]
        {
            new LeavePolicyLegalEntity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = newPolicy.Id,
                LegalEntityId = legalEntity.Id, EffectiveDate = new DateOnly(2026, 1, 1), IsActive = true
            }
        };

        var repo = new EfLeavePolicyRepository(db);

        await repo.AddAggregateWithReplacementAsync(
            newPolicy, typeRules, [], assignments, [legalEntity.Id], CancellationToken.None);

        var oldAssignment = await db.LeavePolicyLegalEntities
            .SingleAsync(x => x.LeavePolicyId == oldPolicy.Id);
        var newAssignment = await db.LeavePolicyLegalEntities
            .SingleAsync(x => x.LeavePolicyId == newPolicy.Id);

        Assert.False(oldAssignment.IsActive);
        Assert.True(newAssignment.IsActive);
        Assert.Equal(2, await db.LeavePolicies.CountAsync(x => x.TenantId == tenantId));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static LeavePolicy CreatePolicy(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Country = "LK",
        AccrualMethod = LeaveAccrualMethods.Annual,
        AccrualStart = LeaveAccrualStarts.Immediately,
        ProrationMethod = LeaveProrationMethods.CalendarDays,
        ApprovalMode = LeaveApprovalModes.AnyOne,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true
    };

    private static LeaveType CreateLeaveType(Guid tenantId, string name, string code) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Code = code,
        Category = LeaveTypeCategories.Annual,
        DefaultDaysPerYear = 20m,
        IsActive = true
    };

    private static LegalEntity CreateLegalEntity(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true
    };
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EfLeavePolicyRepositoryTests`

Expected: FAIL because the policy repository interface and EF implementation do not exist.

- [ ] **Step 3: Add repository interface and read models**

Create `ILeavePolicyRepository.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

public interface ILeavePolicyRepository
{
    Task<IReadOnlyList<LeavePolicyAggregate>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<LeavePolicyAggregate?> GetAggregateByIdAsync(Guid tenantId, Guid leavePolicyId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeavePolicyId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveType>> ListActiveLeaveTypesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> leaveTypeIds, CancellationToken ct = default);

    Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePolicyLegalEntityConflict>> ListActiveAssignmentConflictsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default);

    Task AddAggregateWithReplacementAsync(
        LeavePolicy policy,
        IReadOnlyCollection<LeavePolicyLeaveType> leaveTypes,
        IReadOnlyCollection<LeavePolicyBlackoutPeriod> blackoutPeriods,
        IReadOnlyCollection<LeavePolicyLegalEntity> legalEntityAssignments,
        IReadOnlyCollection<Guid> legalEntityIdsToReplace,
        CancellationToken ct = default);
}

public record LeavePolicyAggregate(
    LeavePolicy Policy,
    IReadOnlyList<LeavePolicyLeaveTypeWithType> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriod> BlackoutPeriods,
    IReadOnlyList<LeavePolicyLegalEntityWithName> LegalEntities);

public record LeavePolicyLeaveTypeWithType(
    LeavePolicyLeaveType Rule,
    string LeaveTypeName,
    string LeaveTypeCode);

public record LeavePolicyLegalEntityWithName(
    LeavePolicyLegalEntity Assignment,
    string LegalEntityName);

public record LeavePolicyLegalEntityConflict(
    Guid LegalEntityId,
    string LegalEntityName,
    Guid ActivePolicyId,
    string ActivePolicyName);
```

- [ ] **Step 4: Add response DTOs**

Create `LeavePolicyResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

public record LeavePolicyListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    string ProrationMethod,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    int Version,
    bool IsActive,
    IReadOnlyList<LeavePolicyLeaveTypeRuleResponse> LeaveTypes,
    IReadOnlyList<LeavePolicyLegalEntityAssignmentResponse> LegalEntities,
    DateTimeOffset CreatedAt);

public record LeavePolicyResponse(
    Guid Id,
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    int MinimumTenureMonths,
    decimal? FirstYearReducedPercent,
    int MinimumNoticeDays,
    int? MaxConsecutiveDays,
    decimal MinDaysPerRequest,
    decimal? MaxTeamAbsencePercent,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    int Version,
    bool IsActive,
    IReadOnlyList<LeavePolicyLeaveTypeRuleResponse> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriodResponse> BlackoutPeriods,
    IReadOnlyList<LeavePolicyLegalEntityAssignmentResponse> LegalEntities,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record LeavePolicyLeaveTypeRuleResponse(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record LeavePolicyBlackoutPeriodResponse(
    Guid Id,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

public record LeavePolicyLegalEntityAssignmentResponse(
    Guid Id,
    Guid LegalEntityId,
    string LegalEntityName,
    DateOnly EffectiveDate,
    bool IsActive);
```

- [ ] **Step 5: Add mapper**

Create `LeavePolicyMapper.cs`:

```csharp
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Mappers;

public static class LeavePolicyMapper
{
    public static LeavePolicyListItemResponse ToListItem(LeavePolicyAggregate aggregate)
    {
        var policy = aggregate.Policy;
        return new LeavePolicyListItemResponse(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.Country ?? string.Empty,
            policy.JobLevel,
            policy.AccrualMethod,
            policy.AccrualStart,
            policy.ProrationMethod,
            policy.ApprovalMode,
            policy.EffectiveFrom,
            policy.Version,
            policy.IsActive,
            aggregate.LeaveTypes.Select(t => ToTypeRule(policy.AccrualMethod, t)).ToList(),
            aggregate.LegalEntities.Select(ToLegalEntityAssignment).ToList(),
            policy.CreatedAt);
    }

    public static LeavePolicyResponse ToResponse(LeavePolicyAggregate aggregate)
    {
        var policy = aggregate.Policy;
        return new LeavePolicyResponse(
            policy.Id,
            policy.Name,
            policy.Description,
            policy.Country ?? string.Empty,
            policy.JobLevel,
            policy.AccrualMethod,
            policy.AccrualStart,
            policy.AccrualAfterNMonths,
            policy.ProrationMethod,
            policy.ProbationRestriction,
            policy.MinimumTenureMonths,
            policy.FirstYearReducedPercent,
            policy.MinimumNoticeDays,
            policy.MaxConsecutiveDays,
            policy.MinDaysPerRequest,
            policy.MaxTeamAbsencePercent,
            policy.ApprovalMode,
            policy.EffectiveFrom,
            policy.Version,
            policy.IsActive,
            aggregate.LeaveTypes.Select(t => ToTypeRule(policy.AccrualMethod, t)).ToList(),
            aggregate.BlackoutPeriods.Select(ToBlackoutPeriod).ToList(),
            aggregate.LegalEntities.Select(ToLegalEntityAssignment).ToList(),
            policy.CreatedAt,
            policy.UpdatedAt);
    }

    private static LeavePolicyLeaveTypeRuleResponse ToTypeRule(
        string accrualMethod,
        LeavePolicyLeaveTypeWithType item)
    {
        var rule = item.Rule;
        var monthlyAccrualDays = accrualMethod == LeaveAccrualMethods.Monthly
            ? decimal.Round(rule.AnnualEntitlementDays / 12m, 1, MidpointRounding.AwayFromZero)
            : null;

        return new LeavePolicyLeaveTypeRuleResponse(
            rule.Id,
            rule.LeaveTypeId,
            item.LeaveTypeName,
            item.LeaveTypeCode,
            rule.AnnualEntitlementDays,
            monthlyAccrualDays,
            rule.CarryForwardMaxDays,
            rule.CarryForwardExpiryMonths);
    }

    private static LeavePolicyBlackoutPeriodResponse ToBlackoutPeriod(
        ONEVO.Domain.Features.Leave.Policy.Entities.LeavePolicyBlackoutPeriod period)
        => new(period.Id, period.StartDate, period.EndDate, period.Reason);

    private static LeavePolicyLegalEntityAssignmentResponse ToLegalEntityAssignment(
        LeavePolicyLegalEntityWithName item)
        => new(item.Assignment.Id, item.Assignment.LegalEntityId, item.LegalEntityName,
            item.Assignment.EffectiveDate, item.Assignment.IsActive);
}
```

- [ ] **Step 6: Add EF repository**

Create `EfLeavePolicyRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy;

public class EfLeavePolicyRepository : ILeavePolicyRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeavePolicyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeavePolicyAggregate>> ListAsync(
        Guid tenantId, bool includeInactive, CancellationToken ct = default)
    {
        var query = _db.LeavePolicies.AsNoTracking().Where(p => p.TenantId == tenantId);
        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        var policies = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return await BuildAggregatesAsync(tenantId, policies, ct);
    }

    public async Task<LeavePolicyAggregate?> GetAggregateByIdAsync(
        Guid tenantId, Guid leavePolicyId, CancellationToken ct = default)
    {
        var policy = await _db.LeavePolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == leavePolicyId, ct);

        if (policy is null)
            return null;

        return (await BuildAggregatesAsync(tenantId, [policy], ct)).Single();
    }

    public async Task<bool> ExistsByNameAsync(
        Guid tenantId, string name, Guid? excludingLeavePolicyId, CancellationToken ct = default)
    {
        var normalized = name.ToLower();
        var query = _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Name.ToLower() == normalized);

        if (excludingLeavePolicyId is { } id)
            query = query.Where(p => p.Id != id);

        return await query.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveType>> ListActiveLeaveTypesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> leaveTypeIds, CancellationToken ct = default)
    {
        return await _db.LeaveTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive && leaveTypeIds.Contains(t.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LegalEntity>> ListActiveLegalEntitiesByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await _db.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && legalEntityIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeavePolicyLegalEntityConflict>> ListActiveAssignmentConflictsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct = default)
    {
        return await (
            from assignment in _db.LeavePolicyLegalEntities.AsNoTracking()
            join policy in _db.LeavePolicies.AsNoTracking() on assignment.LeavePolicyId equals policy.Id
            join legalEntity in _db.LegalEntities.AsNoTracking() on assignment.LegalEntityId equals legalEntity.Id
            where assignment.TenantId == tenantId
                && assignment.IsActive
                && legalEntityIds.Contains(assignment.LegalEntityId)
            orderby legalEntity.Name
            select new LeavePolicyLegalEntityConflict(
                assignment.LegalEntityId,
                legalEntity.Name,
                policy.Id,
                policy.Name))
            .ToListAsync(ct);
    }

    public async Task AddAggregateWithReplacementAsync(
        LeavePolicy policy,
        IReadOnlyCollection<LeavePolicyLeaveType> leaveTypes,
        IReadOnlyCollection<LeavePolicyBlackoutPeriod> blackoutPeriods,
        IReadOnlyCollection<LeavePolicyLegalEntity> legalEntityAssignments,
        IReadOnlyCollection<Guid> legalEntityIdsToReplace,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var activeAssignmentsToReplace = legalEntityIdsToReplace.Count == 0
            ? []
            : await _db.LeavePolicyLegalEntities
                .Where(x => x.TenantId == policy.TenantId
                    && x.IsActive
                    && legalEntityIdsToReplace.Contains(x.LegalEntityId))
                .ToListAsync(ct);

        foreach (var assignment in activeAssignmentsToReplace)
            assignment.IsActive = false;

        if (activeAssignmentsToReplace.Count > 0)
            await _db.SaveChangesAsync(ct);

        await _db.LeavePolicies.AddAsync(policy, ct);
        await _db.LeavePolicyLeaveTypes.AddRangeAsync(leaveTypes, ct);
        await _db.LeavePolicyBlackoutPeriods.AddRangeAsync(blackoutPeriods, ct);
        await _db.LeavePolicyLegalEntities.AddRangeAsync(legalEntityAssignments, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<IReadOnlyList<LeavePolicyAggregate>> BuildAggregatesAsync(
        Guid tenantId, IReadOnlyList<LeavePolicy> policies, CancellationToken ct)
    {
        if (policies.Count == 0)
            return [];

        var policyIds = policies.Select(p => p.Id).ToArray();

        var typeRules = await (
            from rule in _db.LeavePolicyLeaveTypes.AsNoTracking()
            join leaveType in _db.LeaveTypes.AsNoTracking() on rule.LeaveTypeId equals leaveType.Id
            where rule.TenantId == tenantId && policyIds.Contains(rule.LeavePolicyId)
            orderby leaveType.Name
            select new
            {
                rule.LeavePolicyId,
                Item = new LeavePolicyLeaveTypeWithType(rule, leaveType.Name, leaveType.Code)
            })
            .ToListAsync(ct);

        var blackoutPeriods = await _db.LeavePolicyBlackoutPeriods.AsNoTracking()
            .Where(p => p.TenantId == tenantId && policyIds.Contains(p.LeavePolicyId))
            .OrderBy(p => p.StartDate)
            .ToListAsync(ct);

        var legalEntities = await (
            from assignment in _db.LeavePolicyLegalEntities.AsNoTracking()
            join legalEntity in _db.LegalEntities.AsNoTracking() on assignment.LegalEntityId equals legalEntity.Id
            where assignment.TenantId == tenantId && policyIds.Contains(assignment.LeavePolicyId)
            orderby legalEntity.Name
            select new
            {
                assignment.LeavePolicyId,
                Item = new LeavePolicyLegalEntityWithName(assignment, legalEntity.Name)
            })
            .ToListAsync(ct);

        return policies.Select(policy => new LeavePolicyAggregate(
            policy,
            typeRules.Where(x => x.LeavePolicyId == policy.Id).Select(x => x.Item).ToList(),
            blackoutPeriods.Where(x => x.LeavePolicyId == policy.Id).ToList(),
            legalEntities.Where(x => x.LeavePolicyId == policy.Id).Select(x => x.Item).ToList()))
            .ToList();
    }
}
```

- [ ] **Step 7: Register repository in DI**

Patch `DependencyInjection.cs` near the Leave Type repository registration:

```csharp
services.AddScoped<
    ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces.ILeavePolicyRepository,
    ONEVO.Infrastructure.Persistence.Repositories.Leave.Policy.EfLeavePolicyRepository>();
```

- [ ] **Step 8: Run repository tests**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~EfLeavePolicyRepositoryTests`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Policy src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Leave/Policy/EfLeavePolicyRepositoryTests.cs
git commit -m "feat(leave): add leave policy repository and response mapping"
```

---

### Task 3: List and get leave policies

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Queries/ListLeavePolicies/ListLeavePoliciesQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Queries/ListLeavePolicies/ListLeavePoliciesQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Queries/GetLeavePolicy/GetLeavePolicyQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Queries/GetLeavePolicy/GetLeavePolicyQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/ListLeavePoliciesQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/GetLeavePolicyQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ILeavePolicyRepository.ListAsync`
- Consumes: `ILeavePolicyRepository.GetAggregateByIdAsync`
- Produces: `ListLeavePoliciesQuery(bool IncludeInactive)`
- Produces: `GetLeavePolicyQuery(Guid LeavePolicyId)`

- [ ] **Step 1: Write failing handler tests**

Create `ListLeavePoliciesQueryHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class ListLeavePoliciesQueryHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListLeavePoliciesQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_Authenticated_ReturnsMappedPolicies()
    {
        var policy = new LeavePolicy
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "LK Policy",
            Country = "LK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        _repoMock.Setup(r => r.ListAsync(_tenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyAggregate(policy, [], [], [])]);

        var handler = new ListLeavePoliciesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeavePoliciesQuery(false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("LK Policy", result.Value![0].Name);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(false);
        var handler = new ListLeavePoliciesQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new ListLeavePoliciesQuery(false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
```

Create `GetLeavePolicyQueryHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class GetLeavePolicyQueryHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _policyId = Guid.NewGuid();

    public GetLeavePolicyQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_Found_ReturnsMappedPolicy()
    {
        var policy = new LeavePolicy
        {
            Id = _policyId,
            TenantId = _tenantId,
            Name = "LK Policy",
            Country = "LK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicyAggregate(policy, [], [], []));

        var handler = new GetLeavePolicyQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeavePolicyQuery(_policyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("LK Policy", result.Value!.Name);
    }

    [Fact]
    public async Task Handle_Missing_Returns404()
    {
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _policyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeavePolicyAggregate?)null);
        var handler = new GetLeavePolicyQueryHandler(_repoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetLeavePolicyQuery(_policyId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ListLeavePoliciesQueryHandlerTests|FullyQualifiedName~GetLeavePolicyQueryHandlerTests"
```

Expected: FAIL because the query types do not exist.

- [ ] **Step 3: Implement list query**

```csharp
// ListLeavePoliciesQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

public record ListLeavePoliciesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<LeavePolicyListItemResponse>>>;
```

```csharp
// ListLeavePoliciesQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

public class ListLeavePoliciesQueryHandler
    : IRequestHandler<ListLeavePoliciesQuery, Result<IReadOnlyList<LeavePolicyListItemResponse>>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;

    public ListLeavePoliciesQueryHandler(ILeavePolicyRepository policies, ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeavePolicyListItemResponse>>> Handle(
        ListLeavePoliciesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeavePolicyListItemResponse>>.Forbidden("Authentication required.");

        var policies = await _policies.ListAsync(_currentUser.TenantId, request.IncludeInactive, ct);
        return Result<IReadOnlyList<LeavePolicyListItemResponse>>.Success(
            policies.Select(LeavePolicyMapper.ToListItem).ToList());
    }
}
```

- [ ] **Step 4: Implement get query**

```csharp
// GetLeavePolicyQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;

public record GetLeavePolicyQuery(Guid LeavePolicyId) : IRequest<Result<LeavePolicyResponse>>;
```

```csharp
// GetLeavePolicyQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;

public class GetLeavePolicyQueryHandler : IRequestHandler<GetLeavePolicyQuery, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;

    public GetLeavePolicyQueryHandler(ILeavePolicyRepository policies, ICurrentUser currentUser)
    {
        _policies = policies;
        _currentUser = currentUser;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(GetLeavePolicyQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var aggregate = await _policies.GetAggregateByIdAsync(_currentUser.TenantId, request.LeavePolicyId, ct);
        if (aggregate is null)
            return Result<LeavePolicyResponse>.NotFound("Leave policy not found.");

        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate));
    }
}
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ListLeavePoliciesQueryHandlerTests|FullyQualifiedName~GetLeavePolicyQueryHandlerTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Policy/Queries tests/ONEVO.Tests.Unit/Features/Leave/Policy/ListLeavePoliciesQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Policy/GetLeavePolicyQueryHandlerTests.cs
git commit -m "feat(leave): add leave policy list and get queries"
```

---

### Task 4: Create leave policy command with replace-confirmation flow

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CreateLeavePolicy/CreateLeavePolicyCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CreateLeavePolicy/CreateLeavePolicyCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CreateLeavePolicy/CreateLeavePolicyCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/CreateLeavePolicyCommandValidatorTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/CreateLeavePolicyCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `ILeavePolicyRepository`
- Consumes: `IDateTimeProvider.UtcNow`
- Produces: `CreateLeavePolicyCommand`
- Produces: 409 message for replacement conflicts

- [ ] **Step 1: Write validator tests first**

Create `CreateLeavePolicyCommandValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CreateLeavePolicyCommandValidatorTests
{
    private readonly CreateLeavePolicyCommandValidator _validator = new();

    private static CreateLeavePolicyCommand Valid() => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new LeavePolicyTypeRuleInput(Guid.NewGuid(), 20m, null, 5m, 3)],
        [new LeavePolicyBlackoutPeriodInput(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), "Peak closure")],
        [Guid.NewGuid()],
        false);

    [Fact]
    public void ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(Valid());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyCountry_HasError()
    {
        var result = _validator.TestValidate(Valid() with { Country = "" });
        result.ShouldHaveValidationErrorFor(x => x.Country);
    }

    [Fact]
    public void NoLeaveTypes_HasError()
    {
        var result = _validator.TestValidate(Valid() with { LeaveTypes = [] });
        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void DuplicateLeaveTypes_HasError()
    {
        var leaveTypeId = Guid.NewGuid();
        var result = _validator.TestValidate(Valid() with
        {
            LeaveTypes =
            [
                new LeavePolicyTypeRuleInput(leaveTypeId, 20m, null, null, null),
                new LeavePolicyTypeRuleInput(leaveTypeId, 10m, null, null, null)
            ]
        });
        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void MonthlyAccrualMethod_RequiresMonthlyAccrualDays()
    {
        var result = _validator.TestValidate(Valid() with
        {
            AccrualMethod = LeaveAccrualMethods.Monthly,
            LeaveTypes = [new LeavePolicyTypeRuleInput(Guid.NewGuid(), 0m, null, null, null)]
        });

        result.ShouldHaveValidationErrorFor(x => x.LeaveTypes);
    }

    [Fact]
    public void BlackoutEndBeforeStart_HasError()
    {
        var result = _validator.TestValidate(Valid() with
        {
            BlackoutPeriods =
            [
                new LeavePolicyBlackoutPeriodInput(new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 24), null)
            ]
        });

        result.ShouldHaveValidationErrorFor("BlackoutPeriods[0].EndDate");
    }
}
```

- [ ] **Step 2: Write handler tests first**

Create `CreateLeavePolicyCommandHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CreateLeavePolicyCommandHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public CreateLeavePolicyCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "LK Policy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.ListActiveLeaveTypesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType
            {
                Id = _leaveTypeId,
                TenantId = _tenantId,
                Name = "Annual Leave",
                Code = "ANNUAL",
                DefaultDaysPerYear = 20m,
                IsActive = true
            }]);
        _repoMock.Setup(r => r.ListActiveLegalEntitiesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                Name = "Acme Lanka",
                CountryCode = "LKA",
                CurrencyCode = "LKR",
                IsActive = true
            }]);
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid policyId, CancellationToken _) =>
            {
                var policy = new LeavePolicy
                {
                    Id = policyId,
                    TenantId = tenantId,
                    Name = "LK Policy",
                    Country = "LK",
                    AccrualMethod = LeaveAccrualMethods.Annual,
                    AccrualStart = LeaveAccrualStarts.Immediately,
                    ProrationMethod = LeaveProrationMethods.CalendarDays,
                    ApprovalMode = LeaveApprovalModes.AnyOne,
                    EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                return new LeavePolicyAggregate(policy, [], [], []);
            });
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesPolicyAggregate()
    {
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("LK Policy", result.Value!.Name);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.Is<LeavePolicy>(p => p.TenantId == _tenantId && p.Name == "LK Policy"),
            It.Is<IReadOnlyCollection<LeavePolicyLeaveType>>(rules => rules.Single().AnnualEntitlementDays == 20m),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.Is<IReadOnlyCollection<LeavePolicyLegalEntity>>(assignments => assignments.Single().LegalEntityId == _legalEntityId),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsConfigurableValuesFromRequest()
    {
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);
        var effectiveFrom = new DateOnly(2027, 4, 1);

        var result = await handler.Handle(ValidCommand() with
        {
            Country = "GB",
            JobLevel = "L3",
            AccrualMethod = LeaveAccrualMethods.Daily,
            AccrualStart = LeaveAccrualStarts.AfterNMonths,
            AccrualAfterNMonths = 2,
            ProrationMethod = LeaveProrationMethods.WorkingDays,
            ProbationRestriction = true,
            MinimumTenureMonths = 6,
            FirstYearReducedPercent = 75m,
            MinimumNoticeDays = 3,
            MaxConsecutiveDays = 9,
            MinDaysPerRequest = 1m,
            MaxTeamAbsencePercent = 33m,
            ApprovalMode = LeaveApprovalModes.AllMustApprove,
            EffectiveFrom = effectiveFrom,
            LeaveTypes = [new LeavePolicyTypeRuleInput(_leaveTypeId, 18m, null, 4m, 6)]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.Is<LeavePolicy>(p =>
                p.Country == "GB" &&
                p.JobLevel == "L3" &&
                p.AccrualMethod == LeaveAccrualMethods.Daily &&
                p.AccrualStart == LeaveAccrualStarts.AfterNMonths &&
                p.AccrualAfterNMonths == 2 &&
                p.ProrationMethod == LeaveProrationMethods.WorkingDays &&
                p.ProbationRestriction &&
                p.MinimumTenureMonths == 6 &&
                p.FirstYearReducedPercent == 75m &&
                p.MinimumNoticeDays == 3 &&
                p.MaxConsecutiveDays == 9 &&
                p.MinDaysPerRequest == 1m &&
                p.MaxTeamAbsencePercent == 33m &&
                p.ApprovalMode == LeaveApprovalModes.AllMustApprove &&
                p.EffectiveFrom == effectiveFrom),
            It.Is<IReadOnlyCollection<LeavePolicyLeaveType>>(rules =>
                rules.Single().AnnualEntitlementDays == 18m &&
                rules.Single().CarryForwardMaxDays == 4m &&
                rules.Single().CarryForwardExpiryMonths == 6),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.Is<IReadOnlyCollection<LeavePolicyLegalEntity>>(assignments =>
                assignments.Single().EffectiveDate == effectiveFrom),
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MonthlyAccrualAboveLeaveTypeLimit_Returns400()
    {
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand() with
        {
            AccrualMethod = LeaveAccrualMethods.Monthly,
            LeaveTypes = [new LeavePolicyTypeRuleInput(_leaveTypeId, 0m, 2m, null, null)]
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Monthly accrual", result.Error);
    }

    [Fact]
    public async Task Handle_MissingLeaveType_Returns404()
    {
        _repoMock.Setup(r => r.ListActiveLeaveTypesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("The selected leave type no longer exists.", result.Error);
    }

    [Fact]
    public async Task Handle_ExistingActiveLegalEntityAssignment_NotConfirmed_Returns409()
    {
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyLegalEntityConflict(_legalEntityId, "Acme Lanka", Guid.NewGuid(), "Old Policy")]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("Acme Lanka", result.Error);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.IsAny<LeavePolicy>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLeaveType>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLegalEntity>>(),
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingActiveLegalEntityAssignment_Confirmed_Replaces()
    {
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeavePolicyLegalEntityConflict(_legalEntityId, "Acme Lanka", Guid.NewGuid(), "Old Policy")]);
        var handler = new CreateLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand() with { ConfirmReplaceExistingLegalEntityAssignments = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.IsAny<LeavePolicy>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLeaveType>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(),
            It.IsAny<IReadOnlyCollection<LeavePolicyLegalEntity>>(),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == _legalEntityId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private CreateLeavePolicyCommand ValidCommand() => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new LeavePolicyTypeRuleInput(_leaveTypeId, 20m, null, 5m, 3)],
        [],
        [_legalEntityId],
        false);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateLeavePolicyCommandValidatorTests|FullyQualifiedName~CreateLeavePolicyCommandHandlerTests"
```

Expected: FAIL because command, validator, and handler do not exist.

- [ ] **Step 4: Implement command records**

Create `CreateLeavePolicyCommand.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public record CreateLeavePolicyCommand(
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    int MinimumTenureMonths,
    decimal? FirstYearReducedPercent,
    int MinimumNoticeDays,
    int? MaxConsecutiveDays,
    decimal MinDaysPerRequest,
    decimal? MaxTeamAbsencePercent,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    IReadOnlyList<LeavePolicyTypeRuleInput> LeaveTypes,
    IReadOnlyList<LeavePolicyBlackoutPeriodInput> BlackoutPeriods,
    IReadOnlyList<Guid> LegalEntityIds,
    bool ConfirmReplaceExistingLegalEntityAssignments) : IRequest<Result<LeavePolicyResponse>>;

public record LeavePolicyTypeRuleInput(
    Guid LeaveTypeId,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record LeavePolicyBlackoutPeriodInput(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);
```

- [ ] **Step 5: Implement validator**

Create `CreateLeavePolicyCommandValidator.cs`:

```csharp
using FluentValidation;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public class CreateLeavePolicyCommandValidator : AbstractValidator<CreateLeavePolicyCommand>
{
    public CreateLeavePolicyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .WithMessage("Policy name is required and cannot exceed 100 characters.");

        RuleFor(x => x.Country).NotEmpty().MaximumLength(100)
            .WithMessage("Country is required to determine statutory compliance");

        RuleFor(x => x.JobLevel).MaximumLength(100);

        RuleFor(x => x.AccrualMethod).Must(m => LeaveAccrualMethods.All.Contains(m))
            .WithMessage("Accrual method must be one of: annual, monthly, daily.");

        RuleFor(x => x.AccrualStart).Must(s => LeaveAccrualStarts.All.Contains(s))
            .WithMessage("Accrual start must be one of: immediately, after_probation, after_n_months.");

        RuleFor(x => x.AccrualAfterNMonths).GreaterThanOrEqualTo(1)
            .When(x => x.AccrualStart == LeaveAccrualStarts.AfterNMonths)
            .WithMessage("Accrual-after-N-months must be at least 1.");

        RuleFor(x => x.AccrualAfterNMonths).Null()
            .When(x => x.AccrualStart != LeaveAccrualStarts.AfterNMonths)
            .WithMessage("Accrual-after-N-months is only allowed when accrual start is after_n_months.");

        RuleFor(x => x.ProrationMethod).Must(m => LeaveProrationMethods.All.Contains(m))
            .WithMessage("Proration method must be one of: calendar_days, working_days.");

        RuleFor(x => x.ApprovalMode).Must(m => LeaveApprovalModes.All.Contains(m))
            .WithMessage("Approval mode must be one of: any_one, all_must_approve, in_order.");

        RuleFor(x => x.MinimumTenureMonths).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumNoticeDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinDaysPerRequest).GreaterThan(0);
        RuleFor(x => x.MaxConsecutiveDays).GreaterThan(0).When(x => x.MaxConsecutiveDays.HasValue);
        RuleFor(x => x.MaxTeamAbsencePercent).InclusiveBetween(0, 100).When(x => x.MaxTeamAbsencePercent.HasValue);
        RuleFor(x => x.FirstYearReducedPercent).InclusiveBetween(0, 100).When(x => x.FirstYearReducedPercent.HasValue);

        RuleFor(x => x.LeaveTypes).NotEmpty();
        RuleFor(x => x.LeaveTypes)
            .Must(types => types.Select(t => t.LeaveTypeId).Distinct().Count() == types.Count)
            .WithMessage("The same leave type cannot appear twice in one policy.");
        RuleFor(x => x.LeaveTypes)
            .Must((command, types) => command.AccrualMethod != LeaveAccrualMethods.Monthly
                || types.All(t => t.MonthlyAccrualDays.HasValue && t.MonthlyAccrualDays.Value > 0))
            .WithMessage("Monthly accrual days are required for every leave type when accrual method is monthly.");
        RuleFor(x => x.LeaveTypes)
            .Must((command, types) => command.AccrualMethod == LeaveAccrualMethods.Monthly
                || types.All(t => t.AnnualEntitlementDays > 0))
            .WithMessage("Annual entitlement days must be positive for every leave type.");
        RuleForEach(x => x.LeaveTypes).ChildRules(rule =>
        {
            rule.RuleFor(x => x.LeaveTypeId).NotEmpty();
            rule.RuleFor(x => x.CarryForwardMaxDays).GreaterThanOrEqualTo(0)
                .When(x => x.CarryForwardMaxDays.HasValue);
            rule.RuleFor(x => x.CarryForwardExpiryMonths).InclusiveBetween(1, 12)
                .When(x => x.CarryForwardExpiryMonths.HasValue);
        });

        RuleForEach(x => x.BlackoutPeriods).ChildRules(rule =>
        {
            rule.RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
                .WithMessage("Blackout period end date must be on or after start date.");
            rule.RuleFor(x => x.Reason).MaximumLength(200);
        });

        RuleFor(x => x.LegalEntityIds).NotEmpty()
            .WithMessage("Assign one or more legal entities.");
        RuleFor(x => x.LegalEntityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same legal entity cannot appear twice in one policy.");
    }
}
```

- [ ] **Step 6: Implement handler**

Create `CreateLeavePolicyCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;

public class CreateLeavePolicyCommandHandler : IRequestHandler<CreateLeavePolicyCommand, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateLeavePolicyCommandHandler(
        ILeavePolicyRepository policies,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(CreateLeavePolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LeavePolicyResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingLeavePolicyId: null, ct))
            return Result<LeavePolicyResponse>.Conflict("A policy with this name already exists");

        var requestedLeaveTypeIds = request.LeaveTypes.Select(x => x.LeaveTypeId).Distinct().ToArray();
        var activeLeaveTypes = await _policies.ListActiveLeaveTypesByIdsAsync(tenantId, requestedLeaveTypeIds, ct);
        if (activeLeaveTypes.Count != requestedLeaveTypeIds.Length)
            return Result<LeavePolicyResponse>.NotFound("The selected leave type no longer exists.");

        var leaveTypeById = activeLeaveTypes.ToDictionary(x => x.Id);
        foreach (var rule in request.LeaveTypes)
        {
            var annualEntitlement = ToAnnualEntitlement(request.AccrualMethod, rule);
            var leaveType = leaveTypeById[rule.LeaveTypeId];
            if (request.AccrualMethod == LeaveAccrualMethods.Monthly && annualEntitlement > leaveType.DefaultDaysPerYear)
            {
                return Result<LeavePolicyResponse>.Failure(
                    $"Monthly accrual ({rule.MonthlyAccrualDays:0.#} x 12 = {annualEntitlement:0.#} days) exceeds the leave type's annual limit of {leaveType.DefaultDaysPerYear:0.#} days");
            }
        }

        var requestedLegalEntityIds = request.LegalEntityIds.Distinct().ToArray();
        var legalEntities = await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, requestedLegalEntityIds, ct);
        if (legalEntities.Count != requestedLegalEntityIds.Length)
            return Result<LeavePolicyResponse>.NotFound("Legal entity not found.");

        var conflicts = await _policies.ListActiveAssignmentConflictsAsync(tenantId, requestedLegalEntityIds, ct);
        if (conflicts.Count > 0 && !request.ConfirmReplaceExistingLegalEntityAssignments)
            return Result<LeavePolicyResponse>.Conflict(BuildReplacementConflictMessage(conflicts));

        var policyId = Guid.NewGuid();
        var now = _dateTimeProvider.UtcNow;
        var policy = new LeavePolicy
        {
            Id = policyId,
            TenantId = tenantId,
            Name = name,
            Description = request.Description?.Trim(),
            Country = request.Country.Trim(),
            JobLevel = string.IsNullOrWhiteSpace(request.JobLevel) ? null : request.JobLevel.Trim(),
            AccrualMethod = request.AccrualMethod,
            AccrualStart = request.AccrualStart,
            AccrualAfterNMonths = request.AccrualAfterNMonths,
            ProrationMethod = request.ProrationMethod,
            ProbationRestriction = request.ProbationRestriction,
            MinimumTenureMonths = request.MinimumTenureMonths,
            FirstYearReducedPercent = request.FirstYearReducedPercent,
            MinimumNoticeDays = request.MinimumNoticeDays,
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            MinDaysPerRequest = request.MinDaysPerRequest,
            MaxTeamAbsencePercent = request.MaxTeamAbsencePercent,
            ApprovalMode = request.ApprovalMode,
            EffectiveFrom = request.EffectiveFrom,
            Version = 1,
            IsActive = true,
            CreatedAt = now
        };

        var typeRules = request.LeaveTypes.Select(rule => new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            LeaveTypeId = rule.LeaveTypeId,
            AnnualEntitlementDays = ToAnnualEntitlement(request.AccrualMethod, rule),
            CarryForwardMaxDays = rule.CarryForwardMaxDays,
            CarryForwardExpiryMonths = rule.CarryForwardExpiryMonths
        }).ToList();

        var blackoutPeriods = request.BlackoutPeriods.Select(period => new LeavePolicyBlackoutPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Reason = period.Reason?.Trim()
        }).ToList();

        var assignments = requestedLegalEntityIds.Select(legalEntityId => new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = policyId,
            LegalEntityId = legalEntityId,
            EffectiveDate = request.EffectiveFrom,
            IsActive = true
        }).ToList();

        var replacementIds = request.ConfirmReplaceExistingLegalEntityAssignments
            ? conflicts.Select(c => c.LegalEntityId).Distinct().ToArray()
            : [];

        await _policies.AddAggregateWithReplacementAsync(policy, typeRules, blackoutPeriods, assignments, replacementIds, ct);

        var aggregate = await _policies.GetAggregateByIdAsync(tenantId, policyId, ct);
        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate!));
    }

    private static decimal ToAnnualEntitlement(string accrualMethod, LeavePolicyTypeRuleInput rule)
        => accrualMethod == LeaveAccrualMethods.Monthly
            ? decimal.Round(rule.MonthlyAccrualDays!.Value * 12m, 1, MidpointRounding.AwayFromZero)
            : rule.AnnualEntitlementDays;

    private static string BuildReplacementConflictMessage(IReadOnlyList<LeavePolicyLegalEntityConflict> conflicts)
    {
        if (conflicts.Count == 1)
            return $"Legal Entity {conflicts[0].LegalEntityName} already has an active policy. Activating this policy will replace it. Continue?";

        var names = string.Join(", ", conflicts.Select(c => c.LegalEntityName));
        return $"Legal entities already have active policies: {names}. Activating this policy will replace them. Continue?";
    }
}
```

- [ ] **Step 7: Run command tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateLeavePolicyCommandValidatorTests|FullyQualifiedName~CreateLeavePolicyCommandHandlerTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Policy/Commands/CreateLeavePolicy tests/ONEVO.Tests.Unit/Features/Leave/Policy/CreateLeavePolicyCommandValidatorTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Policy/CreateLeavePolicyCommandHandlerTests.cs
git commit -m "feat(leave): add create leave policy command"
```

---

### Task 5: Clone leave policy command

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CloneLeavePolicy/CloneLeavePolicyCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CloneLeavePolicy/CloneLeavePolicyCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Policy/Commands/CloneLeavePolicy/CloneLeavePolicyCommandValidator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/CloneLeavePolicyCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/CloneLeavePolicyCommandValidatorTests.cs`

**Interfaces:**
- Consumes: `ILeavePolicyRepository.GetAggregateByIdAsync`
- Consumes: `ILeavePolicyRepository.AddAggregateWithReplacementAsync`
- Produces: `CloneLeavePolicyCommand`

- [ ] **Step 1: Write validator tests**

Create `CloneLeavePolicyCommandValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CloneLeavePolicyCommandValidatorTests
{
    private readonly CloneLeavePolicyCommandValidator _validator = new();

    [Fact]
    public void ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "LK Policy Copy", "LK", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_HasError()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "", "LK", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptyCountry_HasError()
    {
        var result = _validator.TestValidate(new CloneLeavePolicyCommand(
            Guid.NewGuid(), "Copy", "", [Guid.NewGuid()], new DateOnly(2026, 1, 1), false));

        result.ShouldHaveValidationErrorFor(x => x.Country);
    }
}
```

- [ ] **Step 2: Write handler tests**

Create `CloneLeavePolicyCommandHandlerTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CloneLeavePolicyCommandHandlerTests
{
    private readonly Mock<ILeavePolicyRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourcePolicyId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public CloneLeavePolicyCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));

        var source = new LeavePolicy
        {
            Id = _sourcePolicyId,
            TenantId = _tenantId,
            Name = "Source Policy",
            Country = "UK",
            AccrualMethod = LeaveAccrualMethods.Annual,
            AccrualStart = LeaveAccrualStarts.Immediately,
            ProrationMethod = LeaveProrationMethods.CalendarDays,
            ApprovalMode = LeaveApprovalModes.AnyOne,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var sourceTypeRule = new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LeavePolicyId = _sourcePolicyId,
            LeaveTypeId = _leaveTypeId,
            AnnualEntitlementDays = 20m,
            CarryForwardMaxDays = 5m,
            CarryForwardExpiryMonths = 3
        };

        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _sourcePolicyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeavePolicyAggregate(
                source,
                [new LeavePolicyLeaveTypeWithType(sourceTypeRule, "Annual Leave", "ANNUAL")],
                [new LeavePolicyBlackoutPeriod
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    LeavePolicyId = _sourcePolicyId,
                    StartDate = new DateOnly(2026, 12, 24),
                    EndDate = new DateOnly(2026, 12, 26),
                    Reason = "Peak closure"
                }],
                []));
        _repoMock.Setup(r => r.ExistsByNameAsync(_tenantId, "LK Copy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.ListActiveLegalEntitiesByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                Name = "Acme Lanka",
                CountryCode = "LKA",
                CurrencyCode = "LKR",
                IsActive = true
            }]);
        _repoMock.Setup(r => r.ListActiveAssignmentConflictsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, It.Is<Guid>(id => id != _sourcePolicyId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid policyId, CancellationToken _) =>
            {
                var clone = new LeavePolicy
                {
                    Id = policyId,
                    TenantId = tenantId,
                    Name = "LK Copy",
                    Country = "LK",
                    AccrualMethod = LeaveAccrualMethods.Annual,
                    AccrualStart = LeaveAccrualStarts.Immediately,
                    ProrationMethod = LeaveProrationMethods.CalendarDays,
                    ApprovalMode = LeaveApprovalModes.AnyOne,
                    EffectiveFrom = new DateOnly(2026, 1, 1)
                };
                return new LeavePolicyAggregate(clone, [], [], []);
            });
    }

    [Fact]
    public async Task Handle_ValidCommand_CopiesRulesAndBlackoutPeriods()
    {
        var handler = new CloneLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.AddAggregateWithReplacementAsync(
            It.Is<LeavePolicy>(p => p.Name == "LK Copy" && p.Country == "LK"),
            It.Is<IReadOnlyCollection<LeavePolicyLeaveType>>(rules => rules.Single().AnnualEntitlementDays == 20m),
            It.Is<IReadOnlyCollection<LeavePolicyBlackoutPeriod>>(periods => periods.Single().Reason == "Peak closure"),
            It.Is<IReadOnlyCollection<LeavePolicyLegalEntity>>(assignments => assignments.Single().LegalEntityId == _legalEntityId),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SourceMissing_Returns404()
    {
        _repoMock.Setup(r => r.GetAggregateByIdAsync(_tenantId, _sourcePolicyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeavePolicyAggregate?)null);
        var handler = new CloneLeavePolicyCommandHandler(
            _repoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private CloneLeavePolicyCommand ValidCommand() =>
        new(_sourcePolicyId, "LK Copy", "LK", [_legalEntityId], new DateOnly(2026, 1, 1), false);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CloneLeavePolicyCommandValidatorTests|FullyQualifiedName~CloneLeavePolicyCommandHandlerTests"
```

Expected: FAIL.

- [ ] **Step 4: Implement command and validator**

```csharp
// CloneLeavePolicyCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public record CloneLeavePolicyCommand(
    Guid SourcePolicyId,
    string Name,
    string Country,
    IReadOnlyList<Guid> LegalEntityIds,
    DateOnly EffectiveFrom,
    bool ConfirmReplaceExistingLegalEntityAssignments) : IRequest<Result<LeavePolicyResponse>>;
```

```csharp
// CloneLeavePolicyCommandValidator.cs
using FluentValidation;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public class CloneLeavePolicyCommandValidator : AbstractValidator<CloneLeavePolicyCommand>
{
    public CloneLeavePolicyCommandValidator()
    {
        RuleFor(x => x.SourcePolicyId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100)
            .WithMessage("Country is required to determine statutory compliance");
        RuleFor(x => x.LegalEntityIds).NotEmpty()
            .WithMessage("Assign one or more legal entities.");
        RuleFor(x => x.LegalEntityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The same legal entity cannot appear twice in one policy.");
    }
}
```

- [ ] **Step 5: Implement handler**

Create `CloneLeavePolicyCommandHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Mappers;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Policy.Entities;

namespace ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;

public class CloneLeavePolicyCommandHandler : IRequestHandler<CloneLeavePolicyCommand, Result<LeavePolicyResponse>>
{
    private readonly ILeavePolicyRepository _policies;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CloneLeavePolicyCommandHandler(
        ILeavePolicyRepository policies,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _policies = policies;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeavePolicyResponse>> Handle(CloneLeavePolicyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeavePolicyResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var source = await _policies.GetAggregateByIdAsync(tenantId, request.SourcePolicyId, ct);
        if (source is null)
            return Result<LeavePolicyResponse>.NotFound("Leave policy not found.");

        var name = request.Name.Trim();
        if (await _policies.ExistsByNameAsync(tenantId, name, excludingLeavePolicyId: null, ct))
            return Result<LeavePolicyResponse>.Conflict("A policy with this name already exists");

        var requestedLegalEntityIds = request.LegalEntityIds.Distinct().ToArray();
        var legalEntities = await _policies.ListActiveLegalEntitiesByIdsAsync(tenantId, requestedLegalEntityIds, ct);
        if (legalEntities.Count != requestedLegalEntityIds.Length)
            return Result<LeavePolicyResponse>.NotFound("Legal entity not found.");

        var conflicts = await _policies.ListActiveAssignmentConflictsAsync(tenantId, requestedLegalEntityIds, ct);
        if (conflicts.Count > 0 && !request.ConfirmReplaceExistingLegalEntityAssignments)
            return Result<LeavePolicyResponse>.Conflict(BuildReplacementConflictMessage(conflicts));

        var newPolicyId = Guid.NewGuid();
        var original = source.Policy;
        var clone = new LeavePolicy
        {
            Id = newPolicyId,
            TenantId = tenantId,
            Name = name,
            Description = original.Description,
            Country = request.Country.Trim(),
            JobLevel = original.JobLevel,
            AccrualMethod = original.AccrualMethod,
            AccrualStart = original.AccrualStart,
            AccrualAfterNMonths = original.AccrualAfterNMonths,
            ProrationMethod = original.ProrationMethod,
            ProbationRestriction = original.ProbationRestriction,
            MinimumTenureMonths = original.MinimumTenureMonths,
            FirstYearReducedPercent = original.FirstYearReducedPercent,
            MinimumNoticeDays = original.MinimumNoticeDays,
            MaxConsecutiveDays = original.MaxConsecutiveDays,
            MinDaysPerRequest = original.MinDaysPerRequest,
            MaxTeamAbsencePercent = original.MaxTeamAbsencePercent,
            ApprovalMode = original.ApprovalMode,
            EffectiveFrom = request.EffectiveFrom,
            Version = 1,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        var typeRules = source.LeaveTypes.Select(item => new LeavePolicyLeaveType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            LeaveTypeId = item.Rule.LeaveTypeId,
            AnnualEntitlementDays = item.Rule.AnnualEntitlementDays,
            CarryForwardMaxDays = item.Rule.CarryForwardMaxDays,
            CarryForwardExpiryMonths = item.Rule.CarryForwardExpiryMonths
        }).ToList();

        var blackoutPeriods = source.BlackoutPeriods.Select(period => new LeavePolicyBlackoutPeriod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Reason = period.Reason
        }).ToList();

        var assignments = requestedLegalEntityIds.Select(legalEntityId => new LeavePolicyLegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeavePolicyId = newPolicyId,
            LegalEntityId = legalEntityId,
            EffectiveDate = request.EffectiveFrom,
            IsActive = true
        }).ToList();

        var replacementIds = request.ConfirmReplaceExistingLegalEntityAssignments
            ? conflicts.Select(c => c.LegalEntityId).Distinct().ToArray()
            : [];

        await _policies.AddAggregateWithReplacementAsync(clone, typeRules, blackoutPeriods, assignments, replacementIds, ct);

        var aggregate = await _policies.GetAggregateByIdAsync(tenantId, newPolicyId, ct);
        return Result<LeavePolicyResponse>.Success(LeavePolicyMapper.ToResponse(aggregate!));
    }

    private static string BuildReplacementConflictMessage(IReadOnlyList<LeavePolicyLegalEntityConflict> conflicts)
    {
        if (conflicts.Count == 1)
            return $"Legal Entity {conflicts[0].LegalEntityName} already has an active policy. Activating this policy will replace it. Continue?";

        var names = string.Join(", ", conflicts.Select(c => c.LegalEntityName));
        return $"Legal entities already have active policies: {names}. Activating this policy will replace them. Continue?";
    }
}
```

- [ ] **Step 6: Run tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CloneLeavePolicyCommandValidatorTests|FullyQualifiedName~CloneLeavePolicyCommandHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Policy/Commands/CloneLeavePolicy tests/ONEVO.Tests.Unit/Features/Leave/Policy/CloneLeavePolicyCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Policy/CloneLeavePolicyCommandValidatorTests.cs
git commit -m "feat(leave): add clone leave policy command"
```

---

### Task 6: API contracts and `LeavePoliciesController`

**Files:**
- Create: `src/ONEVO.Api/Contracts/Leave/Policies/CreateLeavePolicyRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Leave/Policies/CloneLeavePolicyRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeavePoliciesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/LeavePoliciesControllerTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/LeavePoliciesControllerArchitectureTests.cs`

**Interfaces:**
- Consumes: `ListLeavePoliciesQuery`
- Consumes: `GetLeavePolicyQuery`
- Consumes: `CreateLeavePolicyCommand`
- Consumes: `CloneLeavePolicyCommand`
- Produces: `/api/v1/leave/policies`

- [ ] **Step 1: Write controller tests first**

Create `LeavePoliciesControllerTests.cs`:

```csharp
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePoliciesControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly LeavePoliciesController _sut;
    private readonly Guid _policyId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public LeavePoliciesControllerTests()
    {
        _sut = new LeavePoliciesController(_mediatorMock.Object);
    }

    [Fact]
    public async Task List_SendsQueryAndReturnsOk()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<ListLeavePoliciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LeavePolicyListItemResponse>>.Success([]));

        var result = await _sut.List(includeInactive: true, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListLeavePoliciesQuery>(q => q.IncludeInactive),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_SendsQueryAndReturnsOk()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<GetLeavePolicyQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var result = await _sut.Get(_policyId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<GetLeavePolicyQuery>(q => q.LeavePolicyId == _policyId),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_MapsRequestToCommand()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<CreateLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var request = SampleCreateRequest(confirm: true);

        var result = await _sut.Create(request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CreateLeavePolicyCommand>(c =>
                c.Name == "LK Policy" &&
                c.Country == "LK" &&
                c.ConfirmReplaceExistingLegalEntityAssignments),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Clone_MapsRequestToCommand()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<CloneLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var request = new CloneLeavePolicyRequest("LK Copy", "LK", [_legalEntityId], new DateOnly(2026, 1, 1), false);

        var result = await _sut.Clone(_policyId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CloneLeavePolicyCommand>(c => c.SourcePolicyId == _policyId && c.Name == "LK Copy"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_Conflict_ReturnsProblem409()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<CreateLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Conflict("Legal Entity Acme already has an active policy. Activating this policy will replace it. Continue?"));

        var result = await _sut.Create(SampleCreateRequest(confirm: false), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    private CreateLeavePolicyRequest SampleCreateRequest(bool confirm) => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new CreateLeavePolicyTypeRuleRequest(_leaveTypeId, 20m, null, 5m, 3)],
        [],
        [_legalEntityId],
        confirm);

    private LeavePolicyResponse SampleResponse() => new(
        _policyId, "LK Policy", null, "LK", null, LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately, null, LeaveProrationMethods.CalendarDays, false,
        0, null, 7, 14, 0.5m, 20m, LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1), 1, true, [], [], [], DateTimeOffset.UtcNow, null);
}
```

- [ ] **Step 2: Write architecture tests**

Create `LeavePoliciesControllerArchitectureTests.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class LeavePoliciesControllerArchitectureTests
{
    private static readonly Type ControllerType = typeof(LeavePoliciesController);

    [Fact]
    public void Controller_RequiresTenantPolicy()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("TenantPolicy", attr!.Policy);
    }

    [Fact]
    public void Controller_HasCorrectBaseRoute()
    {
        var attr = ControllerType.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("api/v1/leave/policies", attr!.Template);
    }

    [Fact]
    public void ReadActions_RequireLeaveRead()
    {
        Assert.Equal("leave:read", GetPermission(nameof(LeavePoliciesController.List)));
        Assert.Equal("leave:read", GetPermission(nameof(LeavePoliciesController.Get)));
    }

    [Fact]
    public void MutatingActions_RequireLeaveManage()
    {
        Assert.Equal("leave:manage", GetPermission(nameof(LeavePoliciesController.Create)));
        Assert.Equal("leave:manage", GetPermission(nameof(LeavePoliciesController.Clone)));
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantId()
    {
        foreach (var contractType in new[] { typeof(CreateLeavePolicyRequest), typeof(CloneLeavePolicyRequest) })
        {
            var names = contractType.GetProperties().Select(p => p.Name);
            Assert.DoesNotContain(names, n => string.Equals(n, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Controller_InjectsIMediatorOnly()
    {
        var constructor = Assert.Single(ControllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("IMediator", parameter.ParameterType.Name);
    }

    private static string GetPermission(string methodName)
    {
        var method = ControllerType.GetMethod(methodName);
        var attribute = method!.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute)
            .GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        return (string)field!.GetValue(attribute)!;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeavePoliciesControllerTests
dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~LeavePoliciesControllerArchitectureTests
```

Expected: FAIL because contracts and controller do not exist.

- [ ] **Step 4: Add API contracts**

Create `CreateLeavePolicyRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.Leave.Policies;

public record CreateLeavePolicyRequest(
    string Name,
    string? Description,
    string Country,
    string? JobLevel,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    int MinimumTenureMonths,
    decimal? FirstYearReducedPercent,
    int MinimumNoticeDays,
    int? MaxConsecutiveDays,
    decimal MinDaysPerRequest,
    decimal? MaxTeamAbsencePercent,
    string ApprovalMode,
    DateOnly EffectiveFrom,
    IReadOnlyList<CreateLeavePolicyTypeRuleRequest> LeaveTypes,
    IReadOnlyList<CreateLeavePolicyBlackoutPeriodRequest> BlackoutPeriods,
    IReadOnlyList<Guid> LegalEntityIds,
    bool ConfirmReplaceExistingLegalEntityAssignments);

public record CreateLeavePolicyTypeRuleRequest(
    Guid LeaveTypeId,
    decimal AnnualEntitlementDays,
    decimal? MonthlyAccrualDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths);

public record CreateLeavePolicyBlackoutPeriodRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);
```

Create `CloneLeavePolicyRequest.cs`:

```csharp
namespace ONEVO.Api.Contracts.Leave.Policies;

public record CloneLeavePolicyRequest(
    string Name,
    string Country,
    IReadOnlyList<Guid> LegalEntityIds,
    DateOnly EffectiveFrom,
    bool ConfirmReplaceExistingLegalEntityAssignments);
```

- [ ] **Step 5: Add controller**

Create `LeavePoliciesController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/policies")]
[Authorize(Policy = "TenantPolicy")]
public class LeavePoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeavePoliciesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListLeavePoliciesQuery(includeInactive), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{leavePolicyId:guid}")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Get(Guid leavePolicyId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeavePolicyQuery(leavePolicyId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Create([FromBody] CreateLeavePolicyRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CreateLeavePolicyCommand(
                request.Name,
                request.Description,
                request.Country,
                request.JobLevel,
                request.AccrualMethod,
                request.AccrualStart,
                request.AccrualAfterNMonths,
                request.ProrationMethod,
                request.ProbationRestriction,
                request.MinimumTenureMonths,
                request.FirstYearReducedPercent,
                request.MinimumNoticeDays,
                request.MaxConsecutiveDays,
                request.MinDaysPerRequest,
                request.MaxTeamAbsencePercent,
                request.ApprovalMode,
                request.EffectiveFrom,
                request.LeaveTypes.Select(x => new LeavePolicyTypeRuleInput(
                    x.LeaveTypeId,
                    x.AnnualEntitlementDays,
                    x.MonthlyAccrualDays,
                    x.CarryForwardMaxDays,
                    x.CarryForwardExpiryMonths)).ToList(),
                request.BlackoutPeriods.Select(x => new LeavePolicyBlackoutPeriodInput(
                    x.StartDate,
                    x.EndDate,
                    x.Reason)).ToList(),
                request.LegalEntityIds,
                request.ConfirmReplaceExistingLegalEntityAssignments),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{leavePolicyId:guid}/clone")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Clone(
        Guid leavePolicyId,
        [FromBody] CloneLeavePolicyRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CloneLeavePolicyCommand(
                leavePolicyId,
                request.Name,
                request.Country,
                request.LegalEntityIds,
                request.EffectiveFrom,
                request.ConfirmReplaceExistingLegalEntityAssignments),
            ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 6: Run controller tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeavePoliciesControllerTests
dotnet test tests/ONEVO.Tests.Architecture --filter FullyQualifiedName~LeavePoliciesControllerArchitectureTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Api/Contracts/Leave/Policies src/ONEVO.Api/Controllers/Tenant/Leave/LeavePoliciesController.cs tests/ONEVO.Tests.Unit/Features/Leave/Policy/LeavePoliciesControllerTests.cs tests/ONEVO.Tests.Architecture/LeavePoliciesControllerArchitectureTests.cs
git commit -m "feat(leave): expose leave policy endpoints"
```

---

### Task 7: Integration tests for policy golden paths

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Features/Leave/LeavePoliciesIntegrationTests.cs`

**Interfaces:**
- Consumes: real `/api/v1/leave/types`
- Consumes: real `/api/v1/leave/policies`
- Produces: HTTP proof for create/list/get/clone/replace-confirmation/permission gate

- [ ] **Step 1: Add integration tests**

Create `LeavePoliciesIntegrationTests.cs` by copying the setup helpers from `LeaveTypesIntegrationTests.cs`, then add these tests. The concrete values in this integration file are fixture values only; keep them inside helper methods or local variables and do not copy them into production handler defaults.

```csharp
[Fact]
public async Task CreatePolicy_AsOwner_Returns200AndPersists()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Annual Leave", "ANNUAL");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);

    var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("LK Annual Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJsonAsync(response);
    json.GetProperty("name").GetString().Should().Be("LK Annual Policy");
    json.GetProperty("leaveTypes").EnumerateArray().Should().ContainSingle();
    json.GetProperty("legalEntities").EnumerateArray().Should().ContainSingle();
}

[Fact]
public async Task CreatePolicy_WithoutLeaveManage_Returns403()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Sick Leave", "SICK");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);

    var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("Blocked Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _noManage.SessionCookie, csrfToken: _noManage.CsrfHeader);

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task CreatePolicy_ExistingActiveLegalEntity_NotConfirmed_Returns409()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Compassionate Leave", "COMP");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);

    var first = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("First Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
    first.StatusCode.Should().Be(HttpStatusCode.OK);

    var second = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("Second Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);

    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var json = await ReadJsonAsync(second);
    json.ToString().Should().Contain("already has an active policy");
}

[Fact]
public async Task CreatePolicy_ExistingActiveLegalEntity_Confirmed_Replaces()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Study Leave", "STUDY");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);

    var first = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("Old Study Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
    first.StatusCode.Should().Be(HttpStatusCode.OK);

    var second = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("New Study Policy", leaveTypeId, legalEntityId, confirm: true),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);

    second.StatusCode.Should().Be(HttpStatusCode.OK);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var activeAssignments = await db.LeavePolicyLegalEntities
        .CountAsync(x => x.TenantId == _tenantId && x.LegalEntityId == legalEntityId && x.IsActive);
    activeAssignments.Should().Be(1);
}

[Fact]
public async Task ClonePolicy_CopiesLeaveTypesAndBlackouts()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Maternity Leave", "MAT");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);
    var create = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/policies",
        CreatePolicyBody("Maternity Policy", leaveTypeId, legalEntityId, confirm: false),
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);
    var created = await ReadJsonAsync(create);
    var policyId = created.GetProperty("id").GetGuid();

    var clone = await SendAsync(HttpMethod.Post, _owner.Host, $"/api/v1/leave/policies/{policyId}/clone",
        new
        {
            name = "Maternity Policy Copy",
            country = "LK",
            legalEntityIds = new[] { legalEntityId },
            effectiveFrom = "2027-01-01",
            confirmReplaceExistingLegalEntityAssignments = true
        },
        cookie: _owner.SessionCookie, csrfToken: _owner.CsrfHeader);

    clone.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJsonAsync(clone);
    json.GetProperty("name").GetString().Should().Be("Maternity Policy Copy");
    json.GetProperty("leaveTypes").EnumerateArray().Should().ContainSingle();
    json.GetProperty("blackoutPeriods").EnumerateArray().Should().ContainSingle();
}
```

Add these helper methods inside the test class:

```csharp
private async Task<Guid> CreateLeaveTypeAsync(string name, string code)
{
    var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/types",
        new
        {
            name,
            code,
            description = "integration fixture type",
            category = "custom",
            isPaid = true,
            requiresApproval = true,
            requiresDocument = false,
            documentRequiredAfterDays = (int?)null,
            acceptedDocumentTypes = Array.Empty<string>(),
            maxConsecutiveDays = (int?)null,
            defaultDaysPerYear = 20m,
            carryForwardAllowed = true,
            maxCarryForwardDays = 5m,
            carryForwardExpiryMonths = 3,
            proRataForNewJoiners = true,
            applicableGender = "all",
            minimumNoticeDays = 0
        },
        cookie: _owner.SessionCookie,
        csrfToken: _owner.CsrfHeader);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
}

private async Task<Guid> GetPrimaryLegalEntityIdAsync(Guid tenantId)
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    return await db.LegalEntities
        .Where(x => x.TenantId == tenantId && x.IsPrimary)
        .Select(x => x.Id)
        .SingleAsync();
}

private static object CreatePolicyBody(string name, Guid leaveTypeId, Guid legalEntityId, bool confirm) => new
{
    name,
    description = "integration fixture policy",
    country = "LK",
    jobLevel = (string?)null,
    accrualMethod = "annual",
    accrualStart = "immediately",
    accrualAfterNMonths = (int?)null,
    prorationMethod = "calendar_days",
    probationRestriction = false,
    minimumTenureMonths = 0,
    firstYearReducedPercent = (decimal?)null,
    minimumNoticeDays = 7,
    maxConsecutiveDays = 14,
    minDaysPerRequest = 0.5m,
    maxTeamAbsencePercent = 20m,
    approvalMode = "any_one",
    effectiveFrom = "2026-01-01",
    leaveTypes = new[]
    {
        new
        {
            leaveTypeId,
            annualEntitlementDays = 20m,
            monthlyAccrualDays = (decimal?)null,
            carryForwardMaxDays = 5m,
            carryForwardExpiryMonths = 3
        }
    },
    blackoutPeriods = new[]
    {
        new
        {
            startDate = "2026-12-24",
            endDate = "2026-12-26",
            reason = "Peak closure"
        }
    },
    legalEntityIds = new[] { legalEntityId },
    confirmReplaceExistingLegalEntityAssignments = confirm
};
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~LeavePoliciesIntegrationTests`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/ONEVO.Tests.Integration/Features/Leave/LeavePoliciesIntegrationTests.cs
git commit -m "test(leave): cover leave policy endpoints"
```

---

### Task 8: Verification, live dev-DB smoke, and summary updates

**Files:**
- Modify: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Modify: `docs/superpowers/plans/next/SUMMARY.md`
- Modify: `docs/superpowers/plans/SUMMARY.md`

**Interfaces:**
- Consumes: every file from Tasks 1-7
- Produces: Phase 2 marked executed only after tests and live dev-DB smoke pass

- [ ] **Step 1: Run targeted unit tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~Leave.Policy
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveVocabulariesTests
```

Expected: all targeted unit tests pass.

- [ ] **Step 2: Run targeted architecture tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LeavePolicyArchitectureTests|FullyQualifiedName~LeavePoliciesControllerArchitectureTests"
```

Expected: pass.

- [ ] **Step 3: Run targeted integration tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~LeaveTypesIntegrationTests|FullyQualifiedName~LeavePoliciesIntegrationTests"
```

Expected: pass.

- [ ] **Step 4: Run full suites**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit
dotnet test tests/ONEVO.Tests.Architecture
```

Expected: both pass. If a pre-existing flaky integration class outside Leave fails during a broader run, rerun the failing class once and document whether the failure reproduces.

- [ ] **Step 5: Live dev-DB smoke**

Against the real local dev DB and the seeded `acme` tenant:

1. Apply migrations.
2. Authenticate as an HR Manager/tenant owner with `leave:manage`.
3. Create a leave type if the tenant does not already have one.
4. `POST /api/v1/leave/policies` with one leave type, one blackout period, and the tenant's primary legal entity.
5. `GET /api/v1/leave/policies` and confirm the policy appears with leave type and legal entity names.
6. `GET /api/v1/leave/policies/{id}` and confirm child arrays are present.
7. Repeat create against the same legal entity with `confirmReplaceExistingLegalEntityAssignments = false`; confirm 409 and message names the legal entity.
8. Repeat create with `confirmReplaceExistingLegalEntityAssignments = true`; confirm 200 and exactly one active `leave_policy_legal_entities` row remains for that legal entity.

- [ ] **Step 6: Update phase summaries after execution**

Only after Steps 1-5 pass, edit the summaries:

- In `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`, change Phase 2 status to `written in full - executed <date>, live dev-DB verified`.
- In `docs/superpowers/plans/next/SUMMARY.md`, add Phase 2 execution status to the leave-management row.
- In `docs/superpowers/plans/SUMMARY.md`, update the leave-management status row to include Phase 2 executed.

- [ ] **Step 7: Commit final execution status**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/plans/SUMMARY.md
git commit -m "docs(leave): mark Phase 2 executed"
```

---

## Execution Handoff

Plan complete for backend Part 2. Implement it only after confirming Part 1's branch is the active base or merged into the current working tree. Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
