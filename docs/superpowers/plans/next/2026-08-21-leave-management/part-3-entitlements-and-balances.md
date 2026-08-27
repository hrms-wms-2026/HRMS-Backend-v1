# Leave Management - Part 3: Entitlements + Balances (Phase 3 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend Entitlements and Balances slice for Screens 3 and 4: bulk entitlement preview/generate, manual assignment, adjust, recalculate, My Balances, Team Balances, and All Balances.

**Architecture:** Part 3 keeps calculation pure and storage explicit. `LeaveEntitlementCalculator` receives policy, employee, year, carry-forward, and configured working-day data and returns numbers only; repositories load and persist EF entities; MediatR handlers orchestrate tenant checks, permission-scoped employee selection, audit rows, and responses; controllers stay thin. All production business values must come from request data, persisted policy data, employee data, legal-entity configuration, or app configuration.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product context from `C:\HR\leave-management-complete.md`; depends on `docs/superpowers/plans/next/2026-08-21-leave-management/part-2-leave-policies.md`.

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat `C:\HR\leave-management-complete.md` and the earlier part files as product context. The user's active request is this Part 3 backend plan, with the explicit rule that production business values must not be hard-coded.
- Phase 2 is assumed executed: `LeavePolicy`, policy child rows, `ILeavePolicyRepository`, and `/api/v1/leave/policies` exist.
- No production business value may be hard-coded. Entitlement amount, carry-forward cap, expiry months, accrual method/start, proration method, probation settings, legal entity, working days, year bounds, and adjustment amounts must come from request data, persisted policy data, employee data, legal-entity configuration, or app configuration.
- Concrete values such as `2026`, `20m`, `5m`, or `LK` are allowed only inside tests and named fixture helpers. Add at least one test proving a non-fixture request/policy value is persisted or returned.
- Remaining shown by balances is computed as `(TotalDays + CarriedForwardDays) - UsedDays - PendingDays`. Do not store a separate remaining column.
- Request submission is not part of this phase. `PendingDays` is read and displayed, but Phase 4 will update it when pending requests exist.
- Approval and cancellation are not part of this phase. Do not change `UsedDays` except through the HR adjust/recalculate workflows explicitly listed here.
- Every entitlement creation, manual assignment, adjustment, and recalculation must write a `LeaveBalanceAudit` row in the same transaction.
- Balance audit read endpoints, CSV export endpoints, scheduled monthly accrual, and year-end carry-forward/forfeiture jobs are Phase 8. This phase writes audit rows now so Phase 8 can surface them later.
- Team balances must use `EmployeeHierarchyClosure` direct and indirect report rows. Do not infer team membership from department.
- Mid-year legal-entity-change warnings must use `PositionAssignment` history joined to `Position.LegalEntityId`. Do not invent a change date from `Employee.UpdatedAt`.
- Keep closed vocabularies as string constants. Do not add C# enums or PostgreSQL enum/check constraints.
- Do not rely on Domain property initializer defaults for business behavior. Handlers must assign values from request/policy/config data explicitly.

---

### Task 1: Pure entitlement calculation helper

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Services/ILeaveWorkingDayCounter.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Services/LeaveWorkingDayCounter.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Services/LeaveEntitlementCalculator.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementCalculatorTests.cs`

**Interfaces:**
- Produces: `ILeaveWorkingDayCounter.CountWorkingDays(DateOnly from, DateOnly to, IReadOnlyCollection<int> standardWorkingDays)`
- Produces: `LeaveEntitlementCalculator.Calculate(LeaveEntitlementCalculationInput input)`
- Consumes later: `LeaveEntitlementCalculationResult.TotalDays`, `CarriedForwardDays`, `ForfeitedDays`, `ProbationRestrictionApplied`, `SkipReason`

- [ ] **Step 1: Write the failing calculator tests**

Create `LeaveEntitlementCalculatorTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Entitlement.Services;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveEntitlementCalculatorTests
{
    private static readonly int[] FixtureWorkingDays = [1, 2, 3, 4, 5];

    [Fact]
    public void Calculate_CalendarProration_MatchesProductWorkedExample()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(new LeaveEntitlementCalculationInput(
            Year: 2026,
            HireDate: new DateOnly(2026, 7, 1),
            ProbationEndDate: null,
            AnnualEntitlementDays: 20m,
            PreviousYearRemainingDays: 0m,
            CarryForwardMaxDays: 5m,
            CarryForwardExpiryMonths: 3,
            AccrualMethod: LeaveAccrualMethods.Annual,
            AccrualStart: LeaveAccrualStarts.Immediately,
            AccrualAfterNMonths: null,
            ProrationMethod: LeaveProrationMethods.CalendarDays,
            ProbationRestriction: false,
            FirstYearReducedPercent: null,
            StandardWorkingDays: FixtureWorkingDays,
            AsOfDate: new DateOnly(2026, 8, 21)));

        result.TotalDays.Should().Be(10.0m);
        result.CarriedForwardDays.Should().Be(0m);
        result.SkipReason.Should().BeNull();
    }

    [Fact]
    public void Calculate_CarryForward_UsesConfiguredPolicyCap()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(new LeaveEntitlementCalculationInput(
            Year: 2027,
            HireDate: new DateOnly(2025, 2, 1),
            ProbationEndDate: null,
            AnnualEntitlementDays: 20m,
            PreviousYearRemainingDays: 8m,
            CarryForwardMaxDays: 5m,
            CarryForwardExpiryMonths: 3,
            AccrualMethod: LeaveAccrualMethods.Annual,
            AccrualStart: LeaveAccrualStarts.Immediately,
            AccrualAfterNMonths: null,
            ProrationMethod: LeaveProrationMethods.CalendarDays,
            ProbationRestriction: false,
            FirstYearReducedPercent: null,
            StandardWorkingDays: FixtureWorkingDays,
            AsOfDate: new DateOnly(2027, 1, 1)));

        result.CarriedForwardDays.Should().Be(5m);
        result.ForfeitedDays.Should().Be(3m);
        result.TotalDays.Should().Be(20m);
    }

    [Fact]
    public void Calculate_UsesNonFixturePolicyAmountFromInput()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(new LeaveEntitlementCalculationInput(
            Year: 2026,
            HireDate: new DateOnly(2024, 1, 1),
            ProbationEndDate: null,
            AnnualEntitlementDays: 17.5m,
            PreviousYearRemainingDays: 0m,
            CarryForwardMaxDays: null,
            CarryForwardExpiryMonths: null,
            AccrualMethod: LeaveAccrualMethods.Annual,
            AccrualStart: LeaveAccrualStarts.Immediately,
            AccrualAfterNMonths: null,
            ProrationMethod: LeaveProrationMethods.CalendarDays,
            ProbationRestriction: false,
            FirstYearReducedPercent: null,
            StandardWorkingDays: FixtureWorkingDays,
            AsOfDate: new DateOnly(2026, 1, 1)));

        result.TotalDays.Should().Be(17.5m);
    }

    [Fact]
    public void Calculate_MonthlyAccrual_UsesAsOfDate()
    {
        var calculator = new LeaveEntitlementCalculator(new LeaveWorkingDayCounter());

        var result = calculator.Calculate(new LeaveEntitlementCalculationInput(
            Year: 2026,
            HireDate: new DateOnly(2024, 1, 1),
            ProbationEndDate: null,
            AnnualEntitlementDays: 24m,
            PreviousYearRemainingDays: 0m,
            CarryForwardMaxDays: null,
            CarryForwardExpiryMonths: null,
            AccrualMethod: LeaveAccrualMethods.Monthly,
            AccrualStart: LeaveAccrualStarts.Immediately,
            AccrualAfterNMonths: null,
            ProrationMethod: LeaveProrationMethods.CalendarDays,
            ProbationRestriction: false,
            FirstYearReducedPercent: null,
            StandardWorkingDays: FixtureWorkingDays,
            AsOfDate: new DateOnly(2026, 3, 15)));

        result.TotalDays.Should().Be(6m);
    }

    [Fact]
    public void CountWorkingDays_UsesConfiguredLegalEntityWorkingDays()
    {
        var count = new LeaveWorkingDayCounter().CountWorkingDays(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 7),
            [2, 4]);

        count.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveEntitlementCalculatorTests
```

Expected: FAIL because the calculator and working-day counter do not exist.

- [ ] **Step 3: Add the working-day counter**

Create `ILeaveWorkingDayCounter.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Entitlement.Services;

public interface ILeaveWorkingDayCounter
{
    int CountWorkingDays(DateOnly from, DateOnly to, IReadOnlyCollection<int> standardWorkingDays);
}
```

Create `LeaveWorkingDayCounter.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Entitlement.Services;

public class LeaveWorkingDayCounter : ILeaveWorkingDayCounter
{
    public int CountWorkingDays(DateOnly from, DateOnly to, IReadOnlyCollection<int> standardWorkingDays)
    {
        if (to < from)
            return 0;

        var configured = standardWorkingDays.ToHashSet();
        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            if (configured.Contains(isoDay))
                count++;
        }

        return count;
    }
}
```

- [ ] **Step 4: Add the calculator records and implementation**

Create `LeaveEntitlementCalculator.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Entitlement.Services;

public record LeaveEntitlementCalculationInput(
    int Year,
    DateOnly? HireDate,
    DateOnly? ProbationEndDate,
    decimal AnnualEntitlementDays,
    decimal PreviousYearRemainingDays,
    decimal? CarryForwardMaxDays,
    int? CarryForwardExpiryMonths,
    string AccrualMethod,
    string AccrualStart,
    int? AccrualAfterNMonths,
    string ProrationMethod,
    bool ProbationRestriction,
    decimal? FirstYearReducedPercent,
    IReadOnlyCollection<int> StandardWorkingDays,
    DateOnly AsOfDate);

public record LeaveEntitlementCalculationResult(
    decimal TotalDays,
    decimal CarriedForwardDays,
    decimal ForfeitedDays,
    bool ProbationRestrictionApplied,
    string? SkipReason);

public class LeaveEntitlementCalculator
{
    private readonly ILeaveWorkingDayCounter _workingDays;

    public LeaveEntitlementCalculator(ILeaveWorkingDayCounter workingDays)
    {
        _workingDays = workingDays;
    }

    public LeaveEntitlementCalculationResult Calculate(LeaveEntitlementCalculationInput input)
    {
        if (input.HireDate is null)
            return new(0m, 0m, 0m, false, "No hire date");

        var yearStart = new DateOnly(input.Year, 1, 1);
        var yearEnd = new DateOnly(input.Year, 12, 31);
        var entitlementStart = ResolveEntitlementStart(input, yearStart);
        if (entitlementStart > yearEnd)
            return new(0m, 0m, 0m, input.ProbationRestriction, "Probation restriction applied");

        var annual = CalculateAnnualPortion(input, entitlementStart, yearStart, yearEnd);
        if (input.FirstYearReducedPercent is { } percent && input.HireDate.Value.Year == input.Year)
            annual = RoundOneDecimal(annual * percent / 100m);

        var carry = CalculateCarryForward(input);
        var forfeited = Math.Max(0m, input.PreviousYearRemainingDays - carry);

        return new(annual, carry, forfeited, input.ProbationRestriction && entitlementStart > yearStart, null);
    }

    private static DateOnly ResolveEntitlementStart(LeaveEntitlementCalculationInput input, DateOnly yearStart)
    {
        var start = input.HireDate!.Value > yearStart ? input.HireDate.Value : yearStart;

        if (input.AccrualStart == LeaveAccrualStarts.AfterProbation && input.ProbationEndDate is { } probationEnd)
            start = Max(start, probationEnd.AddDays(1));

        if (input.AccrualStart == LeaveAccrualStarts.AfterNMonths && input.AccrualAfterNMonths is { } months)
            start = Max(start, input.HireDate.Value.AddMonths(months));

        return start;
    }

    private decimal CalculateAnnualPortion(
        LeaveEntitlementCalculationInput input,
        DateOnly entitlementStart,
        DateOnly yearStart,
        DateOnly yearEnd)
    {
        if (input.AccrualMethod == LeaveAccrualMethods.Monthly)
            return CalculateMonthlyPortion(input, entitlementStart, yearStart, yearEnd);

        if (entitlementStart <= yearStart)
            return RoundOneDecimal(input.AnnualEntitlementDays);

        if (input.ProrationMethod == LeaveProrationMethods.WorkingDays)
        {
            var remaining = _workingDays.CountWorkingDays(entitlementStart, yearEnd, input.StandardWorkingDays);
            var total = _workingDays.CountWorkingDays(yearStart, yearEnd, input.StandardWorkingDays);
            return total == 0 ? 0m : RoundOneDecimal(input.AnnualEntitlementDays * remaining / total);
        }

        var daysInYear = DateTime.IsLeapYear(input.Year) ? 366m : 365m;
        var remainingCalendarDays = yearEnd.DayNumber - entitlementStart.DayNumber;
        return RoundOneDecimal(input.AnnualEntitlementDays * remainingCalendarDays / daysInYear);
    }

    private static decimal CalculateMonthlyPortion(
        LeaveEntitlementCalculationInput input,
        DateOnly entitlementStart,
        DateOnly yearStart,
        DateOnly yearEnd)
    {
        var accrualEnd = input.AsOfDate < yearStart
            ? yearStart.AddDays(-1)
            : input.AsOfDate > yearEnd ? yearEnd : input.AsOfDate;

        if (accrualEnd < entitlementStart)
            return 0m;

        var months = ((accrualEnd.Year - entitlementStart.Year) * 12) + accrualEnd.Month - entitlementStart.Month + 1;
        var monthlyAmount = input.AnnualEntitlementDays / 12m;
        return RoundOneDecimal(monthlyAmount * months);
    }

    private static decimal CalculateCarryForward(LeaveEntitlementCalculationInput input)
    {
        if (input.CarryForwardExpiryMonths is null or <= 0 || input.CarryForwardMaxDays is null or <= 0)
            return 0m;

        return RoundOneDecimal(Math.Min(input.PreviousYearRemainingDays, input.CarryForwardMaxDays.Value));
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static decimal RoundOneDecimal(decimal value) =>
        decimal.Round(value, 1, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 5: Run calculator tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveEntitlementCalculatorTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement/Services tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementCalculatorTests.cs
git commit -m "feat(leave): add entitlement calculator"
```

---

### Task 2: Entitlement repositories, read models, and DTOs

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/RepositoryInterfaces/ILeaveEntitlementRepository.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/DTOs/Responses/LeaveEntitlementResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/DTOs/Responses/LeaveBalanceResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Mappers/LeaveEntitlementMapper.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Entitlement/EfLeaveEntitlementRepository.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs`
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/EfLeaveEntitlementRepositoryTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Policy/EfLeavePolicyRepositoryTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeRepositoryTests.cs`

**Interfaces:**
- Produces: `ILeaveEntitlementRepository`
- Produces: `LeaveEntitlementRow`, `LeaveEntitlementWriteSet`
- Produces: `ILeavePolicyRepository.ListActiveAggregatesByLegalEntityIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, CancellationToken ct)`
- Produces: `LeavePolicyLegalEntityWithName.StandardWorkingDaysJson` for working-day proration from legal-entity configuration
- Produces: `IEmployeeRepository.ListActiveByLegalEntityAsync(Guid tenantId, Guid? legalEntityId, CancellationToken ct)`
- Produces: `IEmployeeRepository.ListLegalEntityChangeWarningsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, int year, CancellationToken ct)`

- [ ] **Step 1: Write repository tests for entitlement persistence and audit**

Create `EfLeaveEntitlementRepositoryTests.cs`:

```csharp
[Fact]
public async Task AddGeneratedAsync_SavesEntitlementsAndAuditInOneCall()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, legalEntityId: Guid.NewGuid(), "EMP-001", "Anu", "Raman");
    var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
    db.Employees.Add(employee);
    db.LeaveTypes.Add(leaveType);
    await db.SaveChangesAsync();

    var entitlement = new LeaveEntitlement
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employee.Id,
        LeaveTypeId = leaveType.Id,
        Year = 2026,
        TotalDays = 17.5m,
        UsedDays = 0m,
        PendingDays = 0m,
        CarriedForwardDays = 2.5m,
        Source = LeaveEntitlementSources.Auto
    };
    var audit = new LeaveBalanceAudit
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employee.Id,
        LeaveTypeId = leaveType.Id,
        ChangeType = LeaveBalanceChangeTypes.Accrual,
        DaysChanged = 20m,
        BalanceAfter = 20m,
        Reason = "Generated from active leave policy"
    };

    var repo = new EfLeaveEntitlementRepository(db);

    await repo.AddGeneratedAsync([new LeaveEntitlementWriteSet(entitlement, audit)], CancellationToken.None);

    (await db.LeaveEntitlements.SingleAsync()).TotalDays.Should().Be(17.5m);
    (await db.LeaveBalanceAudits.SingleAsync()).BalanceAfter.Should().Be(20m);
}

[Fact]
public async Task ListRowsAsync_ComputesRemainingFromTotalCarryUsedAndPending()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, Guid.NewGuid(), "EMP-002", "Maya", "Silva");
    var leaveType = CreateLeaveType(tenantId, "Study Leave", "ST");
    db.Employees.Add(employee);
    db.LeaveTypes.Add(leaveType);
    db.LeaveEntitlements.Add(new LeaveEntitlement
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employee.Id,
        LeaveTypeId = leaveType.Id,
        Year = 2026,
        TotalDays = 12.5m,
        UsedDays = 4m,
        PendingDays = 1.5m,
        CarriedForwardDays = 2m,
        Source = LeaveEntitlementSources.Manual
    });
    await db.SaveChangesAsync();

    var repo = new EfLeaveEntitlementRepository(db);

    var rows = await repo.ListRowsAsync(
        tenantId,
        new LeaveEntitlementListFilter(2026, null, null, null, null, null),
        CancellationToken.None);

    rows.Should().ContainSingle();
    rows[0].RemainingDays.Should().Be(9m);
    rows[0].EmployeeName.Should().Be("Maya Silva");
}
```

- [ ] **Step 2: Write tests for policy lookup by legal entity and employee lookup by legal entity**

Add this test to `EfLeavePolicyRepositoryTests.cs`:

```csharp
[Fact]
public async Task ListActiveAggregatesByLegalEntityIdsAsync_ReturnsPolicyKeyedByLegalEntity()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var legalEntity = CreateLegalEntity(tenantId, "Acme UK");
    legalEntity.StandardWorkingDays = "[2,4]";
    var leaveType = CreateLeaveType(tenantId, "Annual Leave", "AL");
    var policy = CreatePolicy(tenantId, "UK Policy");
    db.LegalEntities.Add(legalEntity);
    db.LeaveTypes.Add(leaveType);
    db.LeavePolicies.Add(policy);
    db.LeavePolicyLeaveTypes.Add(new LeavePolicyLeaveType
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LeavePolicyId = policy.Id,
        LeaveTypeId = leaveType.Id,
        AnnualEntitlementDays = 17.5m
    });
    db.LeavePolicyLegalEntities.Add(new LeavePolicyLegalEntity
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        LeavePolicyId = policy.Id,
        LegalEntityId = legalEntity.Id,
        EffectiveDate = new DateOnly(2026, 1, 1),
        IsActive = true
    });
    await db.SaveChangesAsync();

    var repo = new EfLeavePolicyRepository(db);

    var result = await repo.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, [legalEntity.Id], CancellationToken.None);

    result.Should().ContainKey(legalEntity.Id);
    result[legalEntity.Id].LeaveTypes.Single().Rule.AnnualEntitlementDays.Should().Be(17.5m);
    result[legalEntity.Id].LegalEntities.Single().StandardWorkingDaysJson.Should().Be("[2,4]");
}
```

Add this test to `EfEmployeeRepositoryTests.cs`:

```csharp
[Fact]
public async Task ListActiveByLegalEntityAsync_ReturnsOnlyEmployeesInSelectedLegalEntity()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var selectedLegalEntityId = Guid.NewGuid();
    db.Employees.Add(CreateEmployee(tenantId, selectedLegalEntityId, "EMP-010", "Ravi", "Nadar"));
    db.Employees.Add(CreateEmployee(tenantId, Guid.NewGuid(), "EMP-011", "Tara", "Jones"));
    await db.SaveChangesAsync();

    var repo = new EfEmployeeRepository(db);

    var employees = await repo.ListActiveByLegalEntityAsync(tenantId, selectedLegalEntityId, CancellationToken.None);

    employees.Should().ContainSingle();
    employees[0].EmployeeNumber.Should().Be("EMP-010");
}

[Fact]
public async Task ListLegalEntityChangeWarningsAsync_UsesPositionAssignmentHistory()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var oldLegalEntityId = Guid.NewGuid();
    var newLegalEntityId = Guid.NewGuid();
    var oldPosition = CreatePosition(tenantId, oldLegalEntityId);
    var newPosition = CreatePosition(tenantId, newLegalEntityId);
    db.Employees.Add(CreateEmployee(tenantId, newLegalEntityId, "EMP-012", "Nila", "Perera", employeeId));
    db.Positions.AddRange(oldPosition, newPosition);
    db.PositionAssignments.AddRange(
        CreatePositionAssignment(tenantId, employeeId, oldPosition.Id, new DateOnly(2026, 1, 1), PositionAssignmentStatus.Ended),
        CreatePositionAssignment(tenantId, employeeId, newPosition.Id, new DateOnly(2026, 6, 1), PositionAssignmentStatus.Active));
    await db.SaveChangesAsync();

    var repo = new EfEmployeeRepository(db);

    var warnings = await repo.ListLegalEntityChangeWarningsAsync(tenantId, [employeeId], 2026, CancellationToken.None);

    warnings[employeeId].Should().Be("Employee changed legal entity on 2026-06-01");
}
```

- [ ] **Step 3: Run repository tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfLeaveEntitlementRepositoryTests|FullyQualifiedName~EfLeavePolicyRepositoryTests|FullyQualifiedName~EfEmployeeRepositoryTests"
```

Expected: FAIL because the new repository contracts and methods do not exist.

- [ ] **Step 4: Add response DTOs and row models**

Create `LeaveEntitlementResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

public record LeaveEntitlementResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    int Year,
    decimal TotalDays,
    decimal CarriedForwardDays,
    decimal UsedDays,
    decimal PendingDays,
    decimal RemainingDays,
    string Source,
    string? ManualReason,
    bool IsOverUtilized,
    string? Warning,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record LeaveEntitlementGenerationPreviewResponse(
    int Year,
    int EmployeeCount,
    int EntitlementLineCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Lines,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped);

public record LeaveEntitlementGenerationResultResponse(
    int Year,
    int CreatedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<LeaveEntitlementGenerationLineResponse> Created,
    IReadOnlyList<LeaveEntitlementGenerationSkipResponse> Skipped,
    IReadOnlyList<LeaveEntitlementGenerationErrorResponse> Errors);

public record LeaveEntitlementGenerationLineResponse(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    decimal TotalDays,
    decimal CarriedForwardDays,
    decimal RemainingDays,
    bool ProbationRestrictionApplied,
    string? Warning);

public record LeaveEntitlementGenerationSkipResponse(
    Guid? EmployeeId,
    string? EmployeeName,
    string Reason);

public record LeaveEntitlementGenerationErrorResponse(
    Guid? EmployeeId,
    string? EmployeeName,
    string Reason);
```

Create `LeaveBalanceResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

public record LeaveBalanceResponse(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    int Year,
    decimal EntitledDays,
    decimal AnnualDays,
    decimal CarriedForwardDays,
    decimal UsedDays,
    decimal PendingDays,
    decimal RemainingDays,
    bool IsNegative,
    DateOnly? CarryForwardExpiresOn);
```

- [ ] **Step 5: Add repository contract**

Create `ILeaveEntitlementRepository.cs`:

```csharp
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;

public interface ILeaveEntitlementRepository
{
    Task<IReadOnlyList<LeaveEntitlementRow>> ListRowsAsync(
        Guid tenantId,
        LeaveEntitlementListFilter filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveEntitlement>> ListExistingAsync(
        Guid tenantId,
        int year,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task<LeaveEntitlement?> GetTrackedByIdAsync(Guid tenantId, Guid entitlementId, CancellationToken ct = default);

    Task<LeaveEntitlement?> GetTrackedByEmployeeTypeYearAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int year,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<(Guid EmployeeId, Guid LeaveTypeId), LeaveEntitlement>> ListPreviousYearAsync(
        Guid tenantId,
        int previousYear,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    Task AddGeneratedAsync(IReadOnlyCollection<LeaveEntitlementWriteSet> writeSets, CancellationToken ct = default);

    Task AddManualAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default);

    Task SaveWithAuditAsync(LeaveEntitlement entitlement, LeaveBalanceAudit audit, CancellationToken ct = default);
}

public record LeaveEntitlementListFilter(
    int Year,
    Guid? EmployeeId,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search);

public record LeaveEntitlementWriteSet(LeaveEntitlement Entitlement, LeaveBalanceAudit Audit);

public record LeaveEntitlementRow(
    LeaveEntitlement Entitlement,
    string EmployeeNumber,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal RemainingDays);
```

- [ ] **Step 6: Extend policy and employee repository interfaces**

Add to `ILeavePolicyRepository`:

```csharp
Task<IReadOnlyDictionary<Guid, LeavePolicyAggregate>> ListActiveAggregatesByLegalEntityIdsAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> legalEntityIds,
    CancellationToken ct = default);
```

Also extend the existing `LeavePolicyLegalEntityWithName` record so Phase 3 can read configured working days without another legal-entity query:

```csharp
public record LeavePolicyLegalEntityWithName(
    LeavePolicyLegalEntity Assignment,
    string LegalEntityName,
    string StandardWorkingDaysJson);
```

Add to `IEmployeeRepository`:

```csharp
Task<IReadOnlyList<Employee>> ListActiveByLegalEntityAsync(
    Guid tenantId,
    Guid? legalEntityId,
    CancellationToken ct = default);

Task<IReadOnlyDictionary<Guid, string>> ListLegalEntityChangeWarningsAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> employeeIds,
    int year,
    CancellationToken ct = default);
```

- [ ] **Step 7: Implement EF repository methods**

Add `ListActiveAggregatesByLegalEntityIdsAsync` to `EfLeavePolicyRepository`:

```csharp
public async Task<IReadOnlyDictionary<Guid, LeavePolicyAggregate>> ListActiveAggregatesByLegalEntityIdsAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> legalEntityIds,
    CancellationToken ct = default)
{
    var assignments = await _db.LeavePolicyLegalEntities
        .AsNoTracking()
        .Where(x => x.TenantId == tenantId && x.IsActive && legalEntityIds.Contains(x.LegalEntityId))
        .ToListAsync(ct);
    var policyIds = assignments.Select(a => a.LeavePolicyId).Distinct().ToArray();

    var aggregates = await BuildAggregatesAsync(
        tenantId,
        await _db.LeavePolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive && policyIds.Contains(p.Id))
            .ToListAsync(ct),
        ct);

    return assignments
        .Join(aggregates, a => a.LeavePolicyId, a => a.Policy.Id, (assignment, aggregate) => new { assignment.LegalEntityId, aggregate })
        .ToDictionary(x => x.LegalEntityId, x => x.aggregate);
}
```

Update the existing legal-entity projection inside `BuildAggregatesAsync`:

```csharp
Item = new LeavePolicyLegalEntityWithName(
    assignment,
    legalEntity.Name,
    legalEntity.StandardWorkingDays)
```

Add `ListLegalEntityChangeWarningsAsync` to `EfEmployeeRepository`:

```csharp
public async Task<IReadOnlyDictionary<Guid, string>> ListLegalEntityChangeWarningsAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> employeeIds,
    int year,
    CancellationToken ct = default)
{
    var yearStart = new DateOnly(year, 1, 1);
    var yearEnd = new DateOnly(year, 12, 31);

    var rows = await (
        from assignment in _db.PositionAssignments.AsNoTracking()
        join position in _db.Positions.AsNoTracking() on assignment.PositionId equals position.Id
        where assignment.TenantId == tenantId
            && employeeIds.Contains(assignment.EmployeeId)
            && assignment.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
            && (assignment.AssignmentStatus == PositionAssignmentStatus.Active
                || assignment.AssignmentStatus == PositionAssignmentStatus.Ended)
            && assignment.EffectiveFrom <= yearEnd
            && (assignment.EffectiveTo == null || assignment.EffectiveTo >= yearStart)
        orderby assignment.EmployeeId, assignment.EffectiveFrom
        select new { assignment.EmployeeId, assignment.EffectiveFrom, position.LegalEntityId })
        .ToListAsync(ct);

    return rows
        .GroupBy(x => x.EmployeeId)
        .Select(group => new
        {
            EmployeeId = group.Key,
            Change = group
                .Zip(group.Skip(1), (previous, current) => new { previous, current })
                .FirstOrDefault(pair => pair.previous.LegalEntityId != pair.current.LegalEntityId)
        })
        .Where(x => x.Change is not null)
        .ToDictionary(
            x => x.EmployeeId,
            x => $"Employee changed legal entity on {x.Change!.current.EffectiveFrom:yyyy-MM-dd}");
}
```

Create `EfLeaveEntitlementRepository.cs` with batched joins:

```csharp
public async Task<IReadOnlyList<LeaveEntitlementRow>> ListRowsAsync(
    Guid tenantId,
    LeaveEntitlementListFilter filter,
    CancellationToken ct = default)
{
    var query =
        from entitlement in _db.LeaveEntitlements.AsNoTracking()
        join employee in _db.Employees.AsNoTracking() on entitlement.EmployeeId equals employee.Id
        join leaveType in _db.LeaveTypes.AsNoTracking() on entitlement.LeaveTypeId equals leaveType.Id
        join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
        from department in departments.DefaultIfEmpty()
        join legalEntity in _db.LegalEntities.AsNoTracking() on employee.LegalEntityId equals legalEntity.Id into legalEntities
        from legalEntity in legalEntities.DefaultIfEmpty()
        where entitlement.TenantId == tenantId && entitlement.Year == filter.Year
        select new { entitlement, employee, leaveType, department, legalEntity };

    if (filter.EmployeeId is { } employeeId)
        query = query.Where(x => x.entitlement.EmployeeId == employeeId);
    if (filter.LegalEntityId is { } legalEntityId)
        query = query.Where(x => x.employee.LegalEntityId == legalEntityId);
    if (filter.DepartmentId is { } departmentId)
        query = query.Where(x => x.employee.DepartmentId == departmentId);
    if (filter.LeaveTypeId is { } leaveTypeId)
        query = query.Where(x => x.entitlement.LeaveTypeId == leaveTypeId);
    if (!string.IsNullOrWhiteSpace(filter.Search))
    {
        var search = filter.Search.Trim().ToLower();
        query = query.Where(x =>
            x.employee.FirstName.ToLower().Contains(search) ||
            x.employee.LastName.ToLower().Contains(search) ||
            x.employee.EmployeeNumber.ToLower().Contains(search));
    }

    var rows = await query.OrderBy(x => x.employee.FirstName).ThenBy(x => x.employee.LastName).ToListAsync(ct);

    return rows.Select(x => new LeaveEntitlementRow(
        x.entitlement,
        x.employee.EmployeeNumber,
        $"{x.employee.FirstName} {x.employee.LastName}".Trim(),
        x.employee.DepartmentId,
        x.department?.Name,
        x.employee.LegalEntityId,
        x.legalEntity?.Name,
        x.leaveType.Name,
        x.leaveType.Code,
        x.entitlement.TotalDays + x.entitlement.CarriedForwardDays - x.entitlement.UsedDays - x.entitlement.PendingDays))
        .ToList();
}
```

Implement write methods with a transaction when the provider is relational:

```csharp
public async Task AddGeneratedAsync(IReadOnlyCollection<LeaveEntitlementWriteSet> writeSets, CancellationToken ct = default)
{
    await using var transaction = _db.Database.IsRelational()
        ? await _db.Database.BeginTransactionAsync(ct)
        : null;

    await _db.LeaveEntitlements.AddRangeAsync(writeSets.Select(x => x.Entitlement), ct);
    await _db.LeaveBalanceAudits.AddRangeAsync(writeSets.Select(x => x.Audit), ct);
    await _db.SaveChangesAsync(ct);

    if (transaction is not null)
        await transaction.CommitAsync(ct);
}
```

- [ ] **Step 8: Register repository and service**

Patch `DependencyInjection.cs`:

```csharp
services.AddScoped<
    ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces.ILeaveEntitlementRepository,
    ONEVO.Infrastructure.Persistence.Repositories.Leave.Entitlement.EfLeaveEntitlementRepository>();

services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Services.ILeaveWorkingDayCounter,
    ONEVO.Application.Features.Leave.Entitlement.Services.LeaveWorkingDayCounter>();

services.AddScoped<ONEVO.Application.Features.Leave.Entitlement.Services.LeaveEntitlementCalculator>();
```

- [ ] **Step 9: Run repository tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfLeaveEntitlementRepositoryTests|FullyQualifiedName~EfLeavePolicyRepositoryTests|FullyQualifiedName~EfEmployeeRepositoryTests"
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement src/ONEVO.Application/Features/Leave/Balance src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Entitlement src/ONEVO.Application/Features/Leave/Policy/RepositoryInterfaces/ILeavePolicyRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Policy/EfLeavePolicyRepository.cs src/ONEVO.Application/Common/RepositoryInterfaces/IEmployeeRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/EfEmployeeRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement tests/ONEVO.Tests.Unit/Features/Leave/Policy/EfLeavePolicyRepositoryTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeRepositoryTests.cs
git commit -m "feat(leave): add entitlement repository models"
```

---

### Task 3: Bulk entitlement preview and generate

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Options/LeaveEntitlementYearOptions.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/PreviewGenerateEntitlements/PreviewGenerateEntitlementsCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/PreviewGenerateEntitlements/PreviewGenerateEntitlementsCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/PreviewGenerateEntitlements/PreviewGenerateEntitlementsCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/GenerateEntitlements/GenerateEntitlementsCommandHandler.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Modify: `src/ONEVO.Api/appsettings.json`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/PreviewGenerateEntitlementsCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/GenerateEntitlementsCommandHandlerTests.cs`

**Interfaces:**
- Consumes: calculator from Task 1
- Consumes: repositories from Task 2
- Produces: `PreviewGenerateEntitlementsCommand(int Year, Guid? LegalEntityId)`
- Produces: `GenerateEntitlementsCommand(int Year, Guid? LegalEntityId)`

- [ ] **Step 1: Write preview handler tests**

Create `PreviewGenerateEntitlementsCommandHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_UsesConfiguredPolicyValuesForPreview()
{
    var tenantId = Guid.NewGuid();
    var legalEntityId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, legalEntityId, hireDate: new DateOnly(2024, 1, 1));
    var policy = CreatePolicyAggregate(tenantId, legalEntityId, annualEntitlementDays: 17.5m, carryForwardMaxDays: 4m);

    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
        .ReturnsAsync([employee]);
    _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });
    _entitlements.Setup(x => x.ListExistingAsync(tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);
    _entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());

    var handler = BuildHandler();

    var result = await handler.Handle(new PreviewGenerateEntitlementsCommand(2026, legalEntityId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.Lines.Should().ContainSingle();
    result.Value.Lines[0].TotalDays.Should().Be(17.5m);
}

[Fact]
public async Task Handle_SkipsEmployeeWithoutActivePolicy()
{
    var tenantId = Guid.NewGuid();
    var legalEntityId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, legalEntityId, hireDate: new DateOnly(2024, 1, 1));

    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync([employee]);
    _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

    var handler = BuildHandler();

    var result = await handler.Handle(new PreviewGenerateEntitlementsCommand(2026, null), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.Skipped.Should().Contain(x => x.Reason == "No leave policy assigned to employee legal entity");
}
```

- [ ] **Step 2: Write generate handler tests**

Create `GenerateEntitlementsCommandHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_CreatesEntitlementsAndAuditRows()
{
    var tenantId = Guid.NewGuid();
    var legalEntityId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, legalEntityId, hireDate: new DateOnly(2024, 1, 1));
    var policy = CreatePolicyAggregate(tenantId, legalEntityId, annualEntitlementDays: 19m, carryForwardMaxDays: 3m);
    IReadOnlyCollection<LeaveEntitlementWriteSet>? captured = null;

    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
    _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));
    _employees.Setup(x => x.ListActiveByLegalEntityAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
        .ReturnsAsync([employee]);
    _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });
    _entitlements.Setup(x => x.ListExistingAsync(tenantId, 2026, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);
    _entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());
    _entitlements.Setup(x => x.AddGeneratedAsync(It.IsAny<IReadOnlyCollection<LeaveEntitlementWriteSet>>(), It.IsAny<CancellationToken>()))
        .Callback<IReadOnlyCollection<LeaveEntitlementWriteSet>, CancellationToken>((sets, _) => captured = sets)
        .Returns(Task.CompletedTask);

    var handler = BuildHandler();

    var result = await handler.Handle(new GenerateEntitlementsCommand(2026, legalEntityId), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    captured.Should().ContainSingle();
    captured!.Single().Entitlement.TotalDays.Should().Be(19m);
    captured.Single().Audit.ChangeType.Should().Be(LeaveBalanceChangeTypes.Accrual);
}
```

- [ ] **Step 3: Run command tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~PreviewGenerateEntitlementsCommandHandlerTests|FullyQualifiedName~GenerateEntitlementsCommandHandlerTests"
```

Expected: FAIL because commands and handlers do not exist.

- [ ] **Step 4: Add commands and validators**

Create the command records:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.PreviewGenerateEntitlements;

public record PreviewGenerateEntitlementsCommand(
    int Year,
    Guid? LegalEntityId) : IRequest<Result<LeaveEntitlementGenerationPreviewResponse>>;
```

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.GenerateEntitlements;

public record GenerateEntitlementsCommand(
    int Year,
    Guid? LegalEntityId) : IRequest<Result<LeaveEntitlementGenerationResultResponse>>;
```

Create `LeaveEntitlementYearOptions.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Entitlement.Options;

public class LeaveEntitlementYearOptions
{
    public const string SectionName = "Leave:Entitlements:Years";
    public int MinimumYear { get; init; }
    public int MaximumYear { get; init; }
}
```

Add this section to `src/ONEVO.Api/appsettings.json`; teams can override it per environment without changing code:

```json
"Leave": {
  "Entitlements": {
    "Years": {
      "MinimumYear": 2020,
      "MaximumYear": 2035
    }
  }
}
```

Bind it in `DependencyInjection.cs`:

```csharp
services.AddOptions<LeaveEntitlementYearOptions>()
    .Bind(configuration.GetSection(LeaveEntitlementYearOptions.SectionName))
    .Validate(options => options.MinimumYear > 0, "Leave entitlement minimum year must be configured.")
    .Validate(options => options.MaximumYear >= options.MinimumYear, "Leave entitlement maximum year must be after the minimum year.")
    .ValidateOnStart();
```

Validators must read `IOptions<LeaveEntitlementYearOptions>`. With the configured bounds above, a request for `2036` returns "Balance data is not available for the selected year." The bounds are configuration, not constants inside handlers.

- [ ] **Step 5: Implement preview orchestration**

In `PreviewGenerateEntitlementsCommandHandler`, inject `ICurrentUser`, `IEmployeeRepository`, `ILeavePolicyRepository`, `ILeaveEntitlementRepository`, `LeaveEntitlementCalculator`, and `IDateTimeProvider`. Build preview lines using these rules:

```csharp
var employees = await _employees.ListActiveByLegalEntityAsync(tenantId, request.LegalEntityId, ct);
var legalEntityIds = employees.Select(e => e.LegalEntityId).OfType<Guid>().Distinct().ToArray();
var policiesByLegalEntity = await _policies.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, legalEntityIds, ct);
var existing = await _entitlements.ListExistingAsync(tenantId, request.Year, employees.Select(e => e.Id).ToArray(), ct);
var previous = await _entitlements.ListPreviousYearAsync(tenantId, request.Year - 1, employees.Select(e => e.Id).ToArray(), ct);
var warnings = await _employees.ListLegalEntityChangeWarningsAsync(tenantId, employees.Select(e => e.Id).ToArray(), request.Year, ct);
```

For each employee:

```csharp
if (employee.LegalEntityId is not Guid legalEntityId || !policiesByLegalEntity.TryGetValue(legalEntityId, out var policy))
{
    skipped.Add(new LeaveEntitlementGenerationSkipResponse(
        employee.Id,
        BuildEmployeeName(employee),
        "No leave policy assigned to employee legal entity"));
    continue;
}
```

For each policy leave type, skip existing employee/type/year rows:

```csharp
if (existing.Any(e => e.EmployeeId == employee.Id && e.LeaveTypeId == rule.Rule.LeaveTypeId))
{
    skipped.Add(new LeaveEntitlementGenerationSkipResponse(
        employee.Id,
        BuildEmployeeName(employee),
        $"Entitlement already exists for {request.Year}"));
    continue;
}
```

Calculate from the policy and employee:

```csharp
var priorRemaining = previous.TryGetValue((employee.Id, rule.Rule.LeaveTypeId), out var prior)
    ? prior.TotalDays + prior.CarriedForwardDays - prior.UsedDays - prior.PendingDays
    : 0m;

var calculation = _calculator.Calculate(new LeaveEntitlementCalculationInput(
    request.Year,
    employee.HireDate,
    employee.ProbationEndDate,
    rule.Rule.AnnualEntitlementDays,
    priorRemaining,
    rule.Rule.CarryForwardMaxDays,
    rule.Rule.CarryForwardExpiryMonths,
    policy.Policy.AccrualMethod,
    policy.Policy.AccrualStart,
    policy.Policy.AccrualAfterNMonths,
    policy.Policy.ProrationMethod,
    policy.Policy.ProbationRestriction,
    policy.Policy.FirstYearReducedPercent,
    LegalEntityMapper.ParseStandardWorkingDays(
        policy.LegalEntities.Single(x => x.Assignment.LegalEntityId == legalEntityId).StandardWorkingDaysJson),
    DateOnly.FromDateTime(_dateTimeProvider.UtcNow.UtcDateTime)));
```

If `calculation.SkipReason` is not null, add a skipped row with that exact reason. Otherwise return preview line with `TotalDays`, `CarriedForwardDays`, `RemainingDays = TotalDays + CarriedForwardDays`, and `Warning = warnings.GetValueOrDefault(employee.Id)`.

- [ ] **Step 6: Implement generate orchestration**

Use the same line builder as preview, then persist successful lines:

```csharp
var entitlement = new LeaveEntitlement
{
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    EmployeeId = line.EmployeeId,
    LeaveTypeId = line.LeaveTypeId,
    Year = request.Year,
    TotalDays = line.TotalDays,
    UsedDays = 0m,
    PendingDays = 0m,
    CarriedForwardDays = line.CarriedForwardDays,
    Source = LeaveEntitlementSources.Auto,
    CreatedAt = now
};

var audit = new LeaveBalanceAudit
{
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    EmployeeId = line.EmployeeId,
    LeaveTypeId = line.LeaveTypeId,
    ChangeType = LeaveBalanceChangeTypes.Accrual,
    DaysChanged = line.TotalDays + line.CarriedForwardDays,
    BalanceAfter = line.TotalDays + line.CarriedForwardDays,
    Reason = "Generated from active leave policy",
    CreatedAt = now,
    CreatedBy = _currentUser.UserId
};
```

Call `AddGeneratedAsync` once with all write sets. If all employees are skipped, return a successful result with `CreatedCount = 0`.

- [ ] **Step 7: Run command tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~PreviewGenerateEntitlementsCommandHandlerTests|FullyQualifiedName~GenerateEntitlementsCommandHandlerTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement/Commands src/ONEVO.Application/Features/Leave/Entitlement/Options tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/PreviewGenerateEntitlementsCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/GenerateEntitlementsCommandHandlerTests.cs
git commit -m "feat(leave): add entitlement generation commands"
```

---

### Task 4: Manual assignment, adjust, and recalculate

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/CreateManualEntitlement/CreateManualEntitlementCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/AdjustEntitlement/AdjustEntitlementCommandHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/RecalculateEntitlement/RecalculateEntitlementCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Commands/RecalculateEntitlement/RecalculateEntitlementCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/CreateManualEntitlementCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/AdjustEntitlementCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/RecalculateEntitlementCommandHandlerTests.cs`

**Interfaces:**
- Produces: `CreateManualEntitlementCommand(EmployeeId, LeaveTypeId, Year, TotalDays, CarriedForwardDays, Reason)`
- Produces: `AdjustEntitlementCommand(EntitlementId, TotalDays, CarriedForwardDays, Reason, ConfirmNegativeRemaining)`
- Produces: `RecalculateEntitlementCommand(EntitlementId, ConfirmNegativeRemaining)`

- [ ] **Step 1: Write manual assignment tests**

Create `CreateManualEntitlementCommandHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ManualAssignment_PersistsRequestValuesAndAudit()
{
    var tenantId = Guid.NewGuid();
    var employeeId = Guid.NewGuid();
    var leaveTypeId = Guid.NewGuid();
    LeaveEntitlement? capturedEntitlement = null;
    LeaveBalanceAudit? capturedAudit = null;

    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
    _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    _employees.Setup(x => x.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateEmployee(tenantId, employeeId));
    _leaveTypes.Setup(x => x.GetByIdAsync(tenantId, leaveTypeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateLeaveType(tenantId, leaveTypeId));
    _entitlements.Setup(x => x.GetTrackedByEmployeeTypeYearAsync(tenantId, employeeId, leaveTypeId, 2026, It.IsAny<CancellationToken>()))
        .ReturnsAsync((LeaveEntitlement?)null);
    _entitlements.Setup(x => x.AddManualAsync(It.IsAny<LeaveEntitlement>(), It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
        .Callback<LeaveEntitlement, LeaveBalanceAudit, CancellationToken>((entitlement, audit, _) =>
        {
            capturedEntitlement = entitlement;
            capturedAudit = audit;
        })
        .Returns(Task.CompletedTask);

    var handler = BuildHandler();

    var result = await handler.Handle(
        new CreateManualEntitlementCommand(employeeId, leaveTypeId, 2026, 13.5m, 1.5m, "Contractual top-up"),
        CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    capturedEntitlement!.TotalDays.Should().Be(13.5m);
    capturedEntitlement.CarriedForwardDays.Should().Be(1.5m);
    capturedEntitlement.Source.Should().Be(LeaveEntitlementSources.Manual);
    capturedAudit!.ChangeType.Should().Be(LeaveBalanceChangeTypes.Accrual);
    capturedAudit.Reason.Should().Be("Contractual top-up");
}
```

- [ ] **Step 2: Write adjust and recalculate tests**

Create `AdjustEntitlementCommandHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_AdjustBelowUsed_RequiresConfirmation()
{
    var tenantId = Guid.NewGuid();
    var entitlement = CreateEntitlement(tenantId, totalDays: 4m, usedDays: 5m, pendingDays: 0m, carriedForwardDays: 0m);
    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entitlement);

    var handler = BuildHandler();

    var result = await handler.Handle(
        new AdjustEntitlementCommand(entitlement.Id, 3m, 0m, "Policy correction", ConfirmNegativeRemaining: false),
        CancellationToken.None);

    result.IsSuccess.Should().BeFalse();
    result.Error.Should().Contain("Employee will show negative balance");
}

[Fact]
public async Task Handle_AdjustWithConfirmation_SavesAuditWithDelta()
{
    var tenantId = Guid.NewGuid();
    var entitlement = CreateEntitlement(tenantId, totalDays: 10m, usedDays: 2m, pendingDays: 1m, carriedForwardDays: 0m);
    LeaveBalanceAudit? capturedAudit = null;
    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
    _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
    _entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entitlement);
    _entitlements.Setup(x => x.SaveWithAuditAsync(entitlement, It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
        .Callback<LeaveEntitlement, LeaveBalanceAudit, CancellationToken>((_, audit, _) => capturedAudit = audit)
        .Returns(Task.CompletedTask);

    var handler = BuildHandler();

    var result = await handler.Handle(
        new AdjustEntitlementCommand(entitlement.Id, 12m, 1m, "Manager correction", ConfirmNegativeRemaining: false),
        CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    capturedAudit!.ChangeType.Should().Be(LeaveBalanceChangeTypes.Adjustment);
    capturedAudit.DaysChanged.Should().Be(3m);
    capturedAudit.BalanceAfter.Should().Be(10m);
}
```

Create `RecalculateEntitlementCommandHandlerTests.cs` with this behavior:

```csharp
[Fact]
public async Task Handle_Recalculate_UsesCurrentPolicyButKeepsUsedAndPending()
{
    var tenantId = Guid.NewGuid();
    var legalEntityId = Guid.NewGuid();
    var entitlement = CreateEntitlement(tenantId, totalDays: 10m, usedDays: 2m, pendingDays: 1m, carriedForwardDays: 0m);
    var employee = CreateEmployee(tenantId, legalEntityId, hireDate: new DateOnly(2024, 1, 1));
    var policy = CreatePolicyAggregate(tenantId, legalEntityId, annualEntitlementDays: 14m, carryForwardMaxDays: 0m);

    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entitlement);
    _employees.Setup(x => x.GetByIdAsync(tenantId, entitlement.EmployeeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(employee);
    _policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });

    var handler = BuildHandler();

    var result = await handler.Handle(new RecalculateEntitlementCommand(entitlement.Id, true), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    entitlement.TotalDays.Should().Be(14m);
    entitlement.UsedDays.Should().Be(2m);
    entitlement.PendingDays.Should().Be(1m);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateManualEntitlementCommandHandlerTests|FullyQualifiedName~AdjustEntitlementCommandHandlerTests|FullyQualifiedName~RecalculateEntitlementCommandHandlerTests"
```

Expected: FAIL because commands and handlers do not exist.

- [ ] **Step 4: Add command records and validators**

Use these records:

```csharp
public record CreateManualEntitlementCommand(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int Year,
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason) : IRequest<Result<LeaveEntitlementResponse>>;

public record AdjustEntitlementCommand(
    Guid EntitlementId,
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason,
    bool ConfirmNegativeRemaining) : IRequest<Result<LeaveEntitlementResponse>>;

public record RecalculateEntitlementCommand(
    Guid EntitlementId,
    bool ConfirmNegativeRemaining) : IRequest<Result<LeaveEntitlementResponse>>;
```

Validators:

```csharp
RuleFor(x => x.Year).GreaterThan(0);
RuleFor(x => x.TotalDays).GreaterThan(0);
RuleFor(x => x.CarriedForwardDays).GreaterThanOrEqualTo(0);
RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
```

- [ ] **Step 5: Implement handlers**

Manual assignment handler rules:

```csharp
if (await _entitlements.GetTrackedByEmployeeTypeYearAsync(tenantId, request.EmployeeId, request.LeaveTypeId, request.Year, ct) is not null)
    return Result<LeaveEntitlementResponse>.Conflict("Cannot duplicate the same employee, leave type, and year.");
```

Create entitlement and audit from request values:

```csharp
var entitlement = new LeaveEntitlement
{
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    EmployeeId = request.EmployeeId,
    LeaveTypeId = request.LeaveTypeId,
    Year = request.Year,
    TotalDays = request.TotalDays,
    UsedDays = 0m,
    PendingDays = 0m,
    CarriedForwardDays = request.CarriedForwardDays,
    Source = LeaveEntitlementSources.Manual,
    ManualReason = request.Reason.Trim(),
    CreatedAt = now
};
```

Adjustment handler delta:

```csharp
var oldBalance = entitlement.TotalDays + entitlement.CarriedForwardDays - entitlement.UsedDays - entitlement.PendingDays;
var newBalance = request.TotalDays + request.CarriedForwardDays - entitlement.UsedDays - entitlement.PendingDays;
if (newBalance < 0m && !request.ConfirmNegativeRemaining)
    return Result<LeaveEntitlementResponse>.Failure(
        $"New entitlement ({request.TotalDays + request.CarriedForwardDays:0.#} days) is less than already used ({entitlement.UsedDays:0.#} days). Employee will show negative balance");

entitlement.TotalDays = request.TotalDays;
entitlement.CarriedForwardDays = request.CarriedForwardDays;
entitlement.ManualReason = request.Reason.Trim();
entitlement.UpdatedAt = now;
```

Audit delta:

```csharp
var audit = new LeaveBalanceAudit
{
    Id = Guid.NewGuid(),
    TenantId = tenantId,
    EmployeeId = entitlement.EmployeeId,
    LeaveTypeId = entitlement.LeaveTypeId,
    ChangeType = LeaveBalanceChangeTypes.Adjustment,
    DaysChanged = newBalance - oldBalance,
    BalanceAfter = newBalance,
    Reason = request.Reason.Trim(),
    CreatedAt = now,
    CreatedBy = _currentUser.UserId
};
```

Recalculate uses the Task 3 calculation line builder for one existing entitlement, replaces only `TotalDays` and `CarriedForwardDays`, leaves `UsedDays` and `PendingDays` untouched, then writes an `Adjustment` audit row.

- [ ] **Step 6: Run command tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~CreateManualEntitlementCommandHandlerTests|FullyQualifiedName~AdjustEntitlementCommandHandlerTests|FullyQualifiedName~RecalculateEntitlementCommandHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement/Commands tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/CreateManualEntitlementCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/AdjustEntitlementCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/RecalculateEntitlementCommandHandlerTests.cs
git commit -m "feat(leave): add entitlement mutation commands"
```

---

### Task 5: Entitlement list and balance queries

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Queries/ListEntitlements/ListEntitlementsQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Queries/ListEntitlements/ListEntitlementsQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/GetMyBalances/GetMyBalancesQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/GetMyBalances/GetMyBalancesQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/ListTeamBalances/ListTeamBalancesQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/ListTeamBalances/ListTeamBalancesQueryHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/ListAllBalances/ListAllBalancesQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/ListAllBalances/ListAllBalancesQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeHierarchyClosure/RepositoryInterfaces/IEmployeeHierarchyClosureRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeHierarchyClosureRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/ListEntitlementsQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Balance/GetMyBalancesQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Balance/ListTeamBalancesQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Balance/ListAllBalancesQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeHierarchyClosure/EfEmployeeHierarchyClosureRepositoryTests.cs`

**Interfaces:**
- Produces: `ListEntitlementsQuery`
- Produces: `GetMyBalancesQuery`
- Produces: `ListTeamBalancesQuery`
- Produces: `ListAllBalancesQuery`
- Produces: `IEmployeeHierarchyClosureRepository.GetDescendantEmployeeIdsAsync`

- [ ] **Step 1: Write hierarchy repository test**

Add to `EfEmployeeHierarchyClosureRepositoryTests.cs`:

```csharp
[Fact]
public async Task GetDescendantEmployeeIdsAsync_ReturnsDirectAndIndirectReports()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var managerId = Guid.NewGuid();
    var directId = Guid.NewGuid();
    var indirectId = Guid.NewGuid();
    db.EmployeeHierarchyClosures.AddRange(
        new EmployeeHierarchyClosure { TenantId = tenantId, AncestorEmployeeId = managerId, DescendantEmployeeId = directId, Depth = 1 },
        new EmployeeHierarchyClosure { TenantId = tenantId, AncestorEmployeeId = managerId, DescendantEmployeeId = indirectId, Depth = 2 });
    await db.SaveChangesAsync();

    var repo = new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>());

    var ids = await repo.GetDescendantEmployeeIdsAsync(tenantId, managerId, CancellationToken.None);

    ids.Should().BeEquivalentTo([directId, indirectId]);
}
```

- [ ] **Step 2: Write balance query tests**

Create `GetMyBalancesQueryHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_ReturnsOnlyCurrentEmployeeBalances()
{
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var employee = CreateEmployee(tenantId, userId);
    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _currentUser.SetupGet(x => x.UserId).Returns(userId);
    _employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(employee);
    _entitlements.Setup(x => x.ListRowsAsync(
            tenantId,
            It.Is<LeaveEntitlementListFilter>(f => f.EmployeeId == employee.Id && f.Year == 2026),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync([CreateRow(employee.Id, remainingDays: 7m)]);

    var handler = BuildHandler();

    var result = await handler.Handle(new GetMyBalancesQuery(2026), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle(x => x.RemainingDays == 7m);
}
```

Create `ListTeamBalancesQueryHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_FiltersToDirectAndIndirectReports()
{
    var tenantId = Guid.NewGuid();
    var managerUserId = Guid.NewGuid();
    var managerEmployee = CreateEmployee(tenantId, managerUserId);
    var reportId = Guid.NewGuid();
    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _currentUser.SetupGet(x => x.UserId).Returns(managerUserId);
    _employees.Setup(x => x.GetByUserIdAsync(tenantId, managerUserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(managerEmployee);
    _hierarchy.Setup(x => x.GetDescendantEmployeeIdsAsync(tenantId, managerEmployee.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync([reportId]);
    _entitlements.Setup(x => x.ListRowsAsync(
            tenantId,
            It.Is<LeaveEntitlementListFilter>(f => f.Year == 2026),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync([CreateRow(reportId, remainingDays: 4m), CreateRow(Guid.NewGuid(), remainingDays: 9m)]);

    var handler = BuildHandler();

    var result = await handler.Handle(new ListTeamBalancesQuery(2026, null, null, null), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().ContainSingle();
    result.Value![0].EmployeeId.Should().Be(reportId);
}
```

Create `ListAllBalancesQueryHandlerTests.cs` proving filters are forwarded:

```csharp
[Fact]
public async Task Handle_ForwardsAllBalanceFilters()
{
    var tenantId = Guid.NewGuid();
    var legalEntityId = Guid.NewGuid();
    var departmentId = Guid.NewGuid();
    var leaveTypeId = Guid.NewGuid();
    _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    _currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    _entitlements.Setup(x => x.ListRowsAsync(
            tenantId,
            It.Is<LeaveEntitlementListFilter>(f =>
                f.Year == 2026 &&
                f.LegalEntityId == legalEntityId &&
                f.DepartmentId == departmentId &&
                f.LeaveTypeId == leaveTypeId &&
                f.Search == "anu"),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);

    var handler = BuildHandler();

    var result = await handler.Handle(
        new ListAllBalancesQuery(2026, legalEntityId, departmentId, leaveTypeId, "anu"),
        CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetMyBalancesQueryHandlerTests|FullyQualifiedName~ListTeamBalancesQueryHandlerTests|FullyQualifiedName~ListAllBalancesQueryHandlerTests|FullyQualifiedName~EfEmployeeHierarchyClosureRepositoryTests"
```

Expected: FAIL because queries and hierarchy method do not exist.

- [ ] **Step 4: Extend hierarchy repository**

Add to `IEmployeeHierarchyClosureRepository`:

```csharp
Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
    Guid tenantId,
    Guid managerEmployeeId,
    CancellationToken ct = default);
```

Implement in `EfEmployeeHierarchyClosureRepository`:

```csharp
public async Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
    Guid tenantId,
    Guid managerEmployeeId,
    CancellationToken ct = default)
{
    return await _db.EmployeeHierarchyClosures
        .AsNoTracking()
        .Where(c => c.TenantId == tenantId && c.AncestorEmployeeId == managerEmployeeId && c.Depth > 0)
        .Select(c => c.DescendantEmployeeId)
        .Distinct()
        .ToListAsync(ct);
}
```

- [ ] **Step 5: Add query records**

```csharp
public record ListEntitlementsQuery(
    int Year,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveEntitlementResponse>>>;

public record GetMyBalancesQuery(int Year) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;

public record ListTeamBalancesQuery(
    int Year,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;

public record ListAllBalancesQuery(
    int Year,
    Guid? LegalEntityId,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Search) : IRequest<Result<IReadOnlyList<LeaveBalanceResponse>>>;
```

- [ ] **Step 6: Implement mappers and handlers**

Entitlement list handler must enrich rows with legal-entity-change warnings before mapping:

```csharp
var rows = await _entitlements.ListRowsAsync(tenantId, new LeaveEntitlementListFilter(
    request.Year, null, request.LegalEntityId, request.DepartmentId, request.LeaveTypeId, request.Search), ct);
var warnings = await _employees.ListLegalEntityChangeWarningsAsync(
    tenantId,
    rows.Select(r => r.Entitlement.EmployeeId).Distinct().ToArray(),
    request.Year,
    ct);

return Result<IReadOnlyList<LeaveEntitlementResponse>>.Success(
    rows.Select(row => LeaveEntitlementMapper.ToResponse(row, warnings.GetValueOrDefault(row.Entitlement.EmployeeId))).ToList());
```

Balance mapper:

```csharp
private static LeaveBalanceResponse ToBalance(LeaveEntitlementRow row)
{
    var annual = row.Entitlement.TotalDays;
    var carry = row.Entitlement.CarriedForwardDays;
    var remaining = annual + carry - row.Entitlement.UsedDays - row.Entitlement.PendingDays;

    return new LeaveBalanceResponse(
        row.Entitlement.EmployeeId,
        row.EmployeeNumber,
        row.EmployeeName,
        row.DepartmentId,
        row.DepartmentName,
        row.LegalEntityId,
        row.LegalEntityName,
        row.Entitlement.LeaveTypeId,
        row.LeaveTypeName,
        row.LeaveTypeCode,
        row.Entitlement.Year,
        annual + carry,
        annual,
        carry,
        row.Entitlement.UsedDays,
        row.Entitlement.PendingDays,
        remaining,
        remaining < 0m,
        null);
}
```

Team handler filters after fetching rows:

```csharp
var reportIds = await _hierarchy.GetDescendantEmployeeIdsAsync(tenantId, manager.Id, ct);
var reportSet = reportIds.ToHashSet();
var rows = await _entitlements.ListRowsAsync(tenantId, new LeaveEntitlementListFilter(
    request.Year, null, null, request.DepartmentId, request.LeaveTypeId, request.Search), ct);

return Result<IReadOnlyList<LeaveBalanceResponse>>.Success(
    rows.Where(r => reportSet.Contains(r.Entitlement.EmployeeId)).Select(ToBalance).ToList());
```

Get My handler resolves the active employee from the authenticated user and passes `EmployeeId` in the filter. All Balances passes legal-entity, department, leave-type, and search filters through to the repository.

- [ ] **Step 7: Run query tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~ListEntitlementsQueryHandlerTests|FullyQualifiedName~GetMyBalancesQueryHandlerTests|FullyQualifiedName~ListTeamBalancesQueryHandlerTests|FullyQualifiedName~ListAllBalancesQueryHandlerTests|FullyQualifiedName~EfEmployeeHierarchyClosureRepositoryTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement/Queries src/ONEVO.Application/Features/Leave/Balance/Queries src/ONEVO.Application/Features/CoreHr/EmployeeHierarchyClosure/RepositoryInterfaces/IEmployeeHierarchyClosureRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeHierarchyClosureRepository.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/ListEntitlementsQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Balance tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeHierarchyClosure/EfEmployeeHierarchyClosureRepositoryTests.cs
git commit -m "feat(leave): add balance queries"
```

---

### Task 6: Entitlements and balances API controllers

**Files:**
- Create: `src/ONEVO.Api/Contracts/Leave/Entitlements/GenerateEntitlementsRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Leave/Entitlements/CreateManualEntitlementRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Leave/Entitlements/AdjustEntitlementRequest.cs`
- Create: `src/ONEVO.Api/Contracts/Leave/Entitlements/RecalculateEntitlementRequest.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveEntitlementsController.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalancesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementsControllerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalancesControllerTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/LeaveEntitlementsControllerArchitectureTests.cs`
- Test: `tests/ONEVO.Tests.Architecture/LeaveBalancesControllerArchitectureTests.cs`

**Interfaces:**
- Produces: `/api/v1/leave/entitlements`
- Produces: `/api/v1/leave/balances/my`
- Produces: `/api/v1/leave/balances/team`
- Produces: `/api/v1/leave/balances/all`

- [ ] **Step 1: Write controller architecture tests**

Create `LeaveEntitlementsControllerArchitectureTests.cs`:

```csharp
[Fact]
public void Controller_RequiresTenantPolicy()
{
    var attr = typeof(LeaveEntitlementsController).GetCustomAttribute<AuthorizeAttribute>();
    Assert.Equal("TenantPolicy", attr!.Policy);
}

[Fact]
public void MutatingActions_RequireLeaveManage()
{
    Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.PreviewGenerate)));
    Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Generate)));
    Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.CreateManual)));
    Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Adjust)));
    Assert.Equal("leave:manage", GetPermission(nameof(LeaveEntitlementsController.Recalculate)));
}

[Fact]
public void Controller_InjectsIMediatorOnly()
{
    var constructor = Assert.Single(typeof(LeaveEntitlementsController).GetConstructors());
    var parameter = Assert.Single(constructor.GetParameters());
    Assert.Equal("IMediator", parameter.ParameterType.Name);
}
```

Create `LeaveBalancesControllerArchitectureTests.cs`:

```csharp
[Fact]
public void Controller_RequiresTenantPolicy()
{
    var attr = typeof(LeaveBalancesController).GetCustomAttribute<AuthorizeAttribute>();
    Assert.Equal("TenantPolicy", attr!.Policy);
}

[Fact]
public void BalanceActions_UseExpectedPermissions()
{
    Assert.Equal("leave:read-own", GetPermission(nameof(LeaveBalancesController.My)));
    Assert.Equal("leave:read-team", GetPermission(nameof(LeaveBalancesController.Team)));
    Assert.Equal("leave:read", GetPermission(nameof(LeaveBalancesController.All)));
}
```

- [ ] **Step 2: Write unit controller tests**

Create tests proving each route sends the expected command/query:

```csharp
[Fact]
public async Task Generate_SendsGenerateCommand()
{
    var mediator = new Mock<IMediator>();
    mediator.Setup(x => x.Send(It.IsAny<GenerateEntitlementsCommand>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<LeaveEntitlementGenerationResultResponse>.Success(
            new LeaveEntitlementGenerationResultResponse(2026, 0, 0, 0, [], [], [])));
    var controller = new LeaveEntitlementsController(mediator.Object);

    var response = await controller.Generate(new GenerateEntitlementsRequest(2026, Guid.NewGuid()), CancellationToken.None);

    response.Should().BeOfType<OkObjectResult>();
    mediator.Verify(x => x.Send(It.Is<GenerateEntitlementsCommand>(c => c.Year == 2026), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task My_SendsGetMyBalancesQuery()
{
    var mediator = new Mock<IMediator>();
    mediator.Setup(x => x.Send(It.IsAny<GetMyBalancesQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<IReadOnlyList<LeaveBalanceResponse>>.Success([]));
    var controller = new LeaveBalancesController(mediator.Object);

    var response = await controller.My(2026, CancellationToken.None);

    response.Should().BeOfType<OkObjectResult>();
    mediator.Verify(x => x.Send(It.Is<GetMyBalancesQuery>(q => q.Year == 2026), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 3: Run controller tests to verify they fail**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~LeaveEntitlementsControllerTests|FullyQualifiedName~LeaveBalancesControllerTests"
dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LeaveEntitlementsControllerArchitectureTests|FullyQualifiedName~LeaveBalancesControllerArchitectureTests"
```

Expected: FAIL because controllers and request contracts do not exist.

- [ ] **Step 4: Add API request contracts**

Create contracts:

```csharp
namespace ONEVO.Api.Contracts.Leave.Entitlements;

public record GenerateEntitlementsRequest(int Year, Guid? LegalEntityId);

public record CreateManualEntitlementRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int Year,
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason);

public record AdjustEntitlementRequest(
    decimal TotalDays,
    decimal CarriedForwardDays,
    string Reason,
    bool ConfirmNegativeRemaining);

public record RecalculateEntitlementRequest(bool ConfirmNegativeRemaining);
```

- [ ] **Step 5: Add `LeaveEntitlementsController`**

```csharp
[ApiController]
[Route("api/v1/leave/entitlements")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveEntitlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveEntitlementsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> List(
        [FromQuery] int year,
        [FromQuery] Guid? legalEntityId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListEntitlementsQuery(year, legalEntityId, departmentId, leaveTypeId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("generate/preview")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> PreviewGenerate([FromBody] GenerateEntitlementsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new PreviewGenerateEntitlementsCommand(request.Year, request.LegalEntityId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("generate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Generate([FromBody] GenerateEntitlementsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GenerateEntitlementsCommand(request.Year, request.LegalEntityId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("manual")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> CreateManual([FromBody] CreateManualEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CreateManualEntitlementCommand(
            request.EmployeeId, request.LeaveTypeId, request.Year, request.TotalDays, request.CarriedForwardDays, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPut("{entitlementId:guid}/adjust")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Adjust(Guid entitlementId, [FromBody] AdjustEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new AdjustEntitlementCommand(
            entitlementId, request.TotalDays, request.CarriedForwardDays, request.Reason, request.ConfirmNegativeRemaining), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{entitlementId:guid}/recalculate")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> Recalculate(Guid entitlementId, [FromBody] RecalculateEntitlementRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RecalculateEntitlementCommand(entitlementId, request.ConfirmNegativeRemaining), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 6: Add `LeaveBalancesController`**

```csharp
[ApiController]
[Route("api/v1/leave/balances")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveBalancesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveBalancesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("my")]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> My([FromQuery] int year, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyBalancesQuery(year), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("team")]
    [RequirePermission("leave:read-team")]
    public async Task<IActionResult> Team(
        [FromQuery] int year,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListTeamBalancesQuery(year, departmentId, leaveTypeId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("all")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> All(
        [FromQuery] int year,
        [FromQuery] Guid? legalEntityId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListAllBalancesQuery(year, legalEntityId, departmentId, leaveTypeId, search), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 7: Run controller tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~LeaveEntitlementsControllerTests|FullyQualifiedName~LeaveBalancesControllerTests"
dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LeaveEntitlementsControllerArchitectureTests|FullyQualifiedName~LeaveBalancesControllerArchitectureTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Contracts/Leave/Entitlements src/ONEVO.Api/Controllers/Tenant/Leave/LeaveEntitlementsController.cs src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalancesController.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementsControllerTests.cs tests/ONEVO.Tests.Unit/Features/Leave/Balance/LeaveBalancesControllerTests.cs tests/ONEVO.Tests.Architecture/LeaveEntitlementsControllerArchitectureTests.cs tests/ONEVO.Tests.Architecture/LeaveBalancesControllerArchitectureTests.cs
git commit -m "feat(leave): expose entitlement and balance endpoints"
```

---

### Task 7: Integration tests, live dev-DB smoke, and summary updates

**Files:**
- Create: `tests/ONEVO.Tests.Integration/Features/Leave/LeaveEntitlementsAndBalancesIntegrationTests.cs`
- Modify: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Modify: `docs/superpowers/plans/next/SUMMARY.md`
- Modify: `docs/superpowers/plans/SUMMARY.md`

**Interfaces:**
- Consumes: `/api/v1/leave/types`
- Consumes: `/api/v1/leave/policies`
- Consumes: `/api/v1/leave/entitlements`
- Consumes: `/api/v1/leave/balances`
- Produces: Phase 3 marked executed only after tests and live dev-DB smoke pass

- [ ] **Step 1: Add integration coverage**

Create `LeaveEntitlementsAndBalancesIntegrationTests.cs` using the authenticated tenant fixture helpers from `LeavePoliciesIntegrationTests.cs`. Add tests:

```csharp
[Fact]
public async Task GenerateEntitlements_AsOwner_CreatesBalancesAndAuditRows()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Annual Leave", "AL");
    var legalEntityId = await GetPrimaryLegalEntityIdAsync(_tenantId);
    await CreatePolicyAsync("Annual Policy", leaveTypeId, legalEntityId, annualEntitlementDays: 17.5m);

    var generate = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/entitlements/generate",
        new { year = 2026, legalEntityId },
        cookie: _owner.SessionCookie,
        csrfToken: _owner.CsrfHeader);

    generate.StatusCode.Should().Be(HttpStatusCode.OK);

    var balances = await SendAsync(HttpMethod.Get, _owner.Host, "/api/v1/leave/balances/all?year=2026",
        cookie: _owner.SessionCookie,
        csrfToken: _owner.CsrfHeader);
    balances.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJsonAsync(balances);
    json.EnumerateArray().Should().Contain(x => x.GetProperty("annualDays").GetDecimal() == 17.5m);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var auditCount = await db.LeaveBalanceAudits.CountAsync(x => x.TenantId == _tenantId && x.ChangeType == "accrual");
    auditCount.Should().BeGreaterThan(0);
}

[Fact]
public async Task ManualEntitlement_AsOwner_UsesRequestValues()
{
    var leaveTypeId = await CreateLeaveTypeAsync("Study Leave", "ST");
    var employeeId = await GetPrimaryEmployeeIdAsync(_tenantId);

    var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/entitlements/manual",
        new
        {
            employeeId,
            leaveTypeId,
            year = 2026,
            totalDays = 13.5m,
            carriedForwardDays = 1.5m,
            reason = "Contractual study leave"
        },
        cookie: _owner.SessionCookie,
        csrfToken: _owner.CsrfHeader);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJsonAsync(response);
    json.GetProperty("totalDays").GetDecimal().Should().Be(13.5m);
    json.GetProperty("carriedForwardDays").GetDecimal().Should().Be(1.5m);
}

[Fact]
public async Task Entitlements_WithoutLeaveManage_Returns403()
{
    var response = await SendAsync(HttpMethod.Post, _owner.Host, "/api/v1/leave/entitlements/generate",
        new { year = 2026, legalEntityId = (Guid?)null },
        cookie: _noManage.SessionCookie,
        csrfToken: _noManage.CsrfHeader);

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

The concrete values in this integration file are fixture values only. Keep them local to test helpers and do not move them into handlers.

- [ ] **Step 2: Run integration tests**

Run:

```bash
dotnet test tests/ONEVO.Tests.Integration --filter "FullyQualifiedName~LeavePoliciesIntegrationTests|FullyQualifiedName~LeaveEntitlementsAndBalancesIntegrationTests"
```

Expected: PASS.

- [ ] **Step 3: Run targeted unit and architecture suites**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~Leave.Entitlement
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~Leave.Balance
dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LeaveEntitlementsControllerArchitectureTests|FullyQualifiedName~LeaveBalancesControllerArchitectureTests"
```

Expected: PASS.

- [ ] **Step 4: Run full unit and architecture suites**

Run:

```bash
dotnet test tests/ONEVO.Tests.Unit
dotnet test tests/ONEVO.Tests.Architecture
```

Expected: PASS.

- [ ] **Step 5: Live dev-DB smoke**

Against the real local dev DB and the seeded `acme` tenant:

1. Apply migrations.
2. Authenticate as an HR Manager or tenant owner with `leave:manage`.
3. Create a leave type if the tenant does not already have one.
4. Create an active leave policy for the tenant's primary legal entity with annual entitlement, carry-forward cap, expiry months, accrual method/start, proration method, and effective date supplied in the request.
5. `POST /api/v1/leave/entitlements/generate/preview` for the selected year and legal entity. Confirm the preview amount matches the policy request values.
6. `POST /api/v1/leave/entitlements/generate` with the same year and legal entity. Confirm successful and skipped counts are returned.
7. `GET /api/v1/leave/entitlements?year=<year>` and confirm generated rows show employee, leave type, annual, carry-forward, used, pending, and remaining.
8. `GET /api/v1/leave/balances/all?year=<year>` and confirm remaining is `annual + carry-forward - used - pending`.
9. `POST /api/v1/leave/entitlements/manual` for a different employee/type/year combination and confirm request values are persisted.
10. `PUT /api/v1/leave/entitlements/{id}/adjust` with a reason and confirm a `leave_balance_audits` row is inserted.
11. `POST /api/v1/leave/entitlements/{id}/recalculate` and confirm `UsedDays` and `PendingDays` are unchanged.

- [ ] **Step 6: Update phase summaries after execution**

Only after Steps 1-5 pass, edit the summaries:

- In `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`, change Phase 3 status to `written in full - executed <date>, live dev-DB verified`.
- In `docs/superpowers/plans/next/SUMMARY.md`, add Phase 3 execution status to the leave-management row.
- In `docs/superpowers/plans/SUMMARY.md`, update the leave-management status row to include Phase 3 executed.

- [ ] **Step 7: Commit final execution status**

```bash
git add tests/ONEVO.Tests.Integration/Features/Leave/LeaveEntitlementsAndBalancesIntegrationTests.cs docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/plans/SUMMARY.md
git commit -m "test(leave): cover entitlement and balance endpoints"
```

---

## Execution Handoff

Plan complete for backend Part 3. Implement it only after confirming Part 2 is the active base or merged into the current working tree. Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
