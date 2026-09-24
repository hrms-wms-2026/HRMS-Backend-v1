# Leave Management - Part 4: Request Submission (Phase 4 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend Leave Request Submission slice for Screen 5: own request submission, HR submission on behalf of an employee, own request list, live balance impact, policy warnings, document requirement enforcement, approver resolution, and pending balance reservation.

**Architecture:** Part 4 keeps day counting, warning assembly, approver resolution, and persistence separate. `LeaveRequestDayCalculator` is pure and receives configured working days plus configured holiday dates. Application handlers orchestrate employee, policy, balance, overlap, blackout, document, warning, and approver checks. Repositories perform EF reads and atomic writes. Controllers only translate HTTP to commands and queries. Production business values must come from request data, persisted policy data, employee data, legal-entity configuration, calendar providers, or strongly validated app configuration.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product context from `C:\HR\leave-management-complete.md`; depends on `docs/superpowers/plans/next/2026-08-21-leave-management/part-3-entitlements-and-balances.md`.

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat attached documents as context only. The active user request is this Part 4 backend plan, with the explicit rule that request behavior must be configurable and must not be hard-coded.
- Phase 3 is assumed executed: `LeaveEntitlementsController`, `LeaveBalancesController`, `ILeaveEntitlementRepository`, `LeaveEntitlementMapper`, `LeaveBalanceMapping`, policy aggregates, and the entitlement/balance entities already exist.
- Do not cut approved balance in this phase. Submission creates a pending request and reserves only `PaidDays` by increasing `LeaveEntitlement.PendingDays`. Phase 5 approval moves pending paid days into used days.
- If a request has unpaid days, those days must not reduce entitlement balance. They remain visible on `LeaveRequest.PaidDays` and `LeaveRequest.UnpaidDays` for Phase 5 payroll/workforce handling.
- Pending request overlap with pending or approved leave blocks submission for the same employee and date range.
- Blackout period, missing required document, missing approver, invalid date range, zero calculated leave days, and insufficient paid balance when unpaid split is disabled block submission.
- Notice period missed, max consecutive days exceeded, team absence threshold exceeded, and calendar or meeting conflicts are warnings unless a persisted policy or app configuration value says otherwise. In this part, use the product Screen 5 behavior: warnings are saved in `ConflictSnapshotJson` and returned in the response.
- Do not hard-code working days, country, holidays, current year, notice period, max consecutive days, document threshold, team absence threshold, backdating, or unpaid split behavior in handlers.
- Request day calculation must use legal-entity standard working days from the active policy aggregate and holiday dates from a provider. It must not assume Monday-Friday in production code.
- App configuration values must be read through `IOptions<LeaveRequestOptions>` and validated on startup. The options file is the only source for cross-tenant request behavior that is not already persisted in policy/type/legal-entity data.
- Additive request behavior options for this part:
  - `Leave:Requests:AllowBackdatedRequests`
  - `Leave:Requests:AllowUnpaidSplitWhenBalanceShort`
  - `Leave:Requests:RequireReason`
  - `Leave:Requests:TentativeCalendarEnabled`
  - `Leave:Requests:EscalationOwnerEmployeeId`
- Supporting file IDs must be tenant-owned `FileRecord` rows. Do not upload or store files in this phase.
- Approver resolution must live behind `ILeaveApproverResolver` because Phase 5 reuses the same service for approval enforcement and delegation checks.
- Calendar conflicts and tentative calendar blocks must live behind application interfaces. If the existing calendar module has no concrete adapter yet, register explicit no-op adapters and keep the seams ready for Phase 5/7 integration.
- Do not add C# enums or PostgreSQL enum/check constraints for leave status vocabulary. Use the existing string constants and table fields.

---

### Task 1: Request options, provider interfaces, and pure day calculation

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/Options/LeaveRequestOptions.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/ILeaveHolidayProvider.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveRequestDayCalculator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/NoOpLeaveHolidayProvider.cs`
- Edit: `src/ONEVO.Application/DependencyInjection.cs`
- Edit: `src/ONEVO.Api/Program.cs` or the existing options-registration file used by this repo
- Edit: `src/ONEVO.Api/appsettings.json`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestDayCalculatorTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestOptionsTests.cs`

**Interfaces:**
- Produces: `LeaveRequestOptions`
- Produces: `ILeaveHolidayProvider.ListHolidaysAsync(Guid tenantId, Guid? legalEntityId, DateOnly start, DateOnly end, CancellationToken ct)`
- Produces: `LeaveRequestDayCalculator.Calculate(LeaveRequestDayCalculationInput input)`
- Consumes later: configured working days, configured public holidays, half-day period, backdating and unpaid split options

- [ ] **Step 1: Write failing day-calculation tests**

Create `LeaveRequestDayCalculatorTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestDayCalculatorTests
{
    [Fact]
    public void Calculate_UsesConfiguredWorkingDaysInsteadOfFixedWeekdays()
    {
        var calculator = new LeaveRequestDayCalculator();

        var result = calculator.Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 17),
            EndDate: new DateOnly(2026, 8, 23),
            HalfDayPeriod: null,
            StandardWorkingDays: [2, 4],
            HolidayDates: []));

        result.TotalDays.Should().Be(2m);
        result.CountedDates.Should().Equal(
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 20));
    }

    [Fact]
    public void Calculate_ExcludesConfiguredHolidayDates()
    {
        var calculator = new LeaveRequestDayCalculator();

        var result = calculator.Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 17),
            EndDate: new DateOnly(2026, 8, 21),
            HalfDayPeriod: null,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: [new DateOnly(2026, 8, 19)]));

        result.TotalDays.Should().Be(4m);
        result.CountedDates.Should().NotContain(new DateOnly(2026, 8, 19));
    }

    [Theory]
    [InlineData("AM")]
    [InlineData("PM")]
    public void Calculate_SingleDayHalfDay_ReturnsHalfDay(string halfDayPeriod)
    {
        var calculator = new LeaveRequestDayCalculator();

        var result = calculator.Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 18),
            EndDate: new DateOnly(2026, 8, 18),
            HalfDayPeriod: halfDayPeriod,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: []));

        result.TotalDays.Should().Be(0.5m);
    }

    [Fact]
    public void Calculate_MultiDayHalfDay_ReducesTotalByHalf()
    {
        var calculator = new LeaveRequestDayCalculator();

        var result = calculator.Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 17),
            EndDate: new DateOnly(2026, 8, 19),
            HalfDayPeriod: "PM",
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: []));

        result.TotalDays.Should().Be(2.5m);
    }

    [Fact]
    public void Calculate_NonWorkingRange_ReturnsZero()
    {
        var calculator = new LeaveRequestDayCalculator();

        var result = calculator.Calculate(new LeaveRequestDayCalculationInput(
            StartDate: new DateOnly(2026, 8, 22),
            EndDate: new DateOnly(2026, 8, 23),
            HalfDayPeriod: null,
            StandardWorkingDays: [1, 2, 3, 4, 5],
            HolidayDates: []));

        result.TotalDays.Should().Be(0m);
        result.CountedDates.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Implement the pure calculator**

Create `LeaveRequestDayCalculator.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed class LeaveRequestDayCalculator
{
    public LeaveRequestDayCalculationResult Calculate(LeaveRequestDayCalculationInput input)
    {
        if (input.EndDate < input.StartDate)
        {
            return new LeaveRequestDayCalculationResult(0m, []);
        }

        var workingDays = input.StandardWorkingDays.ToHashSet();
        var holidays = input.HolidayDates.ToHashSet();
        var countedDates = new List<DateOnly>();

        for (var date = input.StartDate; date <= input.EndDate; date = date.AddDays(1))
        {
            var dayNumber = (int)date.DayOfWeek;
            if (dayNumber == 0)
            {
                dayNumber = 7;
            }

            if (!workingDays.Contains(dayNumber) || holidays.Contains(date))
            {
                continue;
            }

            countedDates.Add(date);
        }

        var total = countedDates.Count;
        if (!string.IsNullOrWhiteSpace(input.HalfDayPeriod) && total > 0)
        {
            total -= 0.5m;
        }

        return new LeaveRequestDayCalculationResult(total, countedDates);
    }
}

public sealed record LeaveRequestDayCalculationInput(
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    IReadOnlyCollection<int> StandardWorkingDays,
    IReadOnlyCollection<DateOnly> HolidayDates);

public sealed record LeaveRequestDayCalculationResult(
    decimal TotalDays,
    IReadOnlyList<DateOnly> CountedDates);
```

- [ ] **Step 3: Add configurable request options**

Create `LeaveRequestOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ONEVO.Application.Features.Leave.Request.Options;

public sealed class LeaveRequestOptions
{
    public const string SectionName = "Leave:Requests";

    public bool AllowBackdatedRequests { get; init; }

    public bool AllowUnpaidSplitWhenBalanceShort { get; init; }

    public bool RequireReason { get; init; }

    public bool TentativeCalendarEnabled { get; init; }

    public Guid? EscalationOwnerEmployeeId { get; init; }

    [Range(1, 3660)]
    public int MaximumRequestRangeDays { get; init; } = 3660;
}
```

Register and validate options in the same startup file that existing feature options use:

```csharp
services
    .AddOptions<LeaveRequestOptions>()
    .Bind(configuration.GetSection(LeaveRequestOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.MaximumRequestRangeDays > 0, "MaximumRequestRangeDays must be greater than zero.")
    .ValidateOnStart();
```

Add an explicit section to `appsettings.json`:

```json
{
  "Leave": {
    "Requests": {
      "AllowBackdatedRequests": false,
      "AllowUnpaidSplitWhenBalanceShort": false,
      "RequireReason": false,
      "TentativeCalendarEnabled": false,
      "EscalationOwnerEmployeeId": null,
      "MaximumRequestRangeDays": 3660
    }
  }
}
```

- [ ] **Step 4: Add holiday provider interface and baseline registration**

Create `ILeaveHolidayProvider.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveHolidayProvider
{
    Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId,
        Guid? legalEntityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}
```

Create `NoOpLeaveHolidayProvider.cs` in the same namespace:

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed class NoOpLeaveHolidayProvider : ILeaveHolidayProvider
{
    public Task<IReadOnlyList<DateOnly>> ListHolidaysAsync(
        Guid tenantId,
        Guid? legalEntityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        IReadOnlyList<DateOnly> result = [];
        return Task.FromResult(result);
    }
}
```

Register:

```csharp
services.AddSingleton<LeaveRequestDayCalculator>();
services.AddScoped<ILeaveHolidayProvider, NoOpLeaveHolidayProvider>();
```

- [ ] **Step 5: Verify Task 1**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveRequestDayCalculatorTests|FullyQualifiedName~LeaveRequestOptionsTests"
```

Expected result: all new tests pass, and the calculator proves production code does not assume Monday-Friday.

---

### Task 2: Request DTOs, response contracts, and repository interface

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/DTOs/Requests/SubmitLeaveRequestRequest.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/DTOs/Requests/SubmitLeaveRequestOnBehalfRequest.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/DTOs/Responses/LeaveRequestResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/DTOs/Responses/LeaveRequestListItemResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Mappers/LeaveRequestMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestRepository.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestMapperTests.cs`

**Interfaces:**
- Produces: request and response contracts for submit, submit-on-behalf, and own list
- Produces: repository interface for overlap checks, submit-state reads, delegate reads, team absence counts, own list, and atomic pending creation
- Consumes later: existing `LeaveRequest`, `LeaveRequestApprover`, `LeaveRequestDocument`, `LeaveEntitlement`, `LeavePolicyAggregate`

- [ ] **Step 1: Add request DTOs**

Create `SubmitLeaveRequestRequest.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.DTOs.Requests;

public sealed record SubmitLeaveRequestRequest(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);
```

Create `SubmitLeaveRequestOnBehalfRequest.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.DTOs.Requests;

public sealed record SubmitLeaveRequestOnBehalfRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid>? FileRecordIds);
```

- [ ] **Step 2: Add response DTOs**

Create `LeaveRequestResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.DTOs.Responses;

public sealed record LeaveRequestResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    bool NoticePeriodMissed,
    Guid? SubmittedOnBehalfOfBy,
    LeaveRequestBalanceImpactResponse BalanceImpact,
    IReadOnlyList<LeaveRequestApproverResponse> Approvers,
    LeaveRequestConflictSnapshotResponse ConflictSnapshot,
    DateTime CreatedAt);

public sealed record LeaveRequestBalanceImpactResponse(
    decimal CurrentRemainingDays,
    decimal PendingAfterSubmitDays,
    decimal RemainingAfterSubmitDays);

public sealed record LeaveRequestApproverResponse(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    string Status,
    Guid? DelegatedFromApproverId);

public sealed record LeaveRequestConflictSnapshotResponse(
    IReadOnlyList<LeaveRequestWarningResponse> Warnings,
    IReadOnlyList<LeaveRequestCalendarConflictResponse> CalendarConflicts,
    decimal? TeamAbsencePercent);

public sealed record LeaveRequestWarningResponse(
    string Code,
    string Message);

public sealed record LeaveRequestCalendarConflictResponse(
    string Source,
    string Title,
    DateTime? StartsAt,
    DateTime? EndsAt);
```

Create `LeaveRequestListItemResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.DTOs.Responses;

public sealed record LeaveRequestListItemResponse(
    Guid Id,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    bool NoticePeriodMissed,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

- [ ] **Step 3: Add repository interface**

Create `ILeaveRequestRepository.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

public interface ILeaveRequestRepository
{
    Task<bool> HasOverlappingPendingOrApprovedRequestAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<LeaveRequestSubmitState?> GetSubmitStateAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        int year,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestListRow>> ListOwnAsync(
        Guid tenantId,
        Guid employeeId,
        LeaveRequestListFilter filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveApprovalDelegateRow>> ListActiveDelegatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> approverEmployeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task<int> CountPendingOrApprovedInRangeAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);

    Task AddPendingRequestAsync(
        LeaveRequestWriteSet writeSet,
        CancellationToken ct = default);
}

public sealed record LeaveRequestSubmitState(
    LeaveEntitlement Entitlement,
    LeavePolicyAggregate Policy,
    Guid? LegalEntityId,
    string LeaveTypeName,
    string LeaveTypeCode,
    int? NoticePeriodDays,
    decimal? MinDaysPerRequest,
    decimal? MaxConsecutiveDays,
    bool RequiresDocument,
    decimal? DocumentRequiredAfterDays);

public sealed record LeaveRequestWriteSet(
    LeaveRequest Request,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveRequestDocument> Documents,
    LeaveEntitlement Entitlement);

public sealed record LeaveRequestListFilter(
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LeaveTypeId);

public sealed record LeaveRequestListRow(
    LeaveRequest Request,
    string LeaveTypeName,
    string LeaveTypeCode);

public sealed record LeaveApprovalDelegateRow(
    Guid ApproverEmployeeId,
    Guid DelegateEmployeeId);
```

- [ ] **Step 4: Add mapper tests and mapper**

Create a mapper test proving paid pending reserve is reflected in balance impact:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Mappers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestMapperTests
{
    [Fact]
    public void ToBalanceImpact_ReservesPaidDaysOnly()
    {
        var response = LeaveRequestMapper.ToBalanceImpact(
            currentRemainingDays: 4m,
            currentPendingDays: 1m,
            paidDays: 2.5m);

        response.CurrentRemainingDays.Should().Be(4m);
        response.PendingAfterSubmitDays.Should().Be(3.5m);
        response.RemainingAfterSubmitDays.Should().Be(1.5m);
    }
}
```

Create `LeaveRequestMapper.cs`:

```csharp
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Mappers;

public static class LeaveRequestMapper
{
    public static LeaveRequestBalanceImpactResponse ToBalanceImpact(
        decimal currentRemainingDays,
        decimal currentPendingDays,
        decimal paidDays)
    {
        return new LeaveRequestBalanceImpactResponse(
            CurrentRemainingDays: currentRemainingDays,
            PendingAfterSubmitDays: currentPendingDays + paidDays,
            RemainingAfterSubmitDays: currentRemainingDays - paidDays);
    }
}
```

- [ ] **Step 5: Verify Task 2**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveRequestMapperTests"
```

Expected result: mapper tests pass.

---

### Task 3: EF repository implementation and tenant-safe submit-state reads

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/LeaveRequestRepository.cs`
- Edit: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestRepositoryQueryTests.cs` if this repo already has lightweight EF query tests
- Otherwise cover repository behavior through handler tests in Task 5 and integration smoke in Task 8

**Interfaces:**
- Produces: `LeaveRequestRepository`
- Consumes: `HrmsDbContext`, `ILeaveRequestRepository`, existing leave entities, employee/legal-entity relationships, leave policy aggregate shape from Part 2/3

- [ ] **Step 1: Implement overlapping-request query**

Use tenant, employee, date-range overlap, and pending/approved statuses:

```csharp
public async Task<bool> HasOverlappingPendingOrApprovedRequestAsync(
    Guid tenantId,
    Guid employeeId,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken ct = default)
{
    return await _db.LeaveRequests
        .AsNoTracking()
        .AnyAsync(request =>
            request.TenantId == tenantId &&
            request.EmployeeId == employeeId &&
            (request.Status == LeaveRequestStatuses.Pending ||
             request.Status == LeaveRequestStatuses.Approved) &&
            request.StartDate <= endDate &&
            request.EndDate >= startDate,
            ct);
}
```

- [ ] **Step 2: Implement submit-state query**

Load the tracked entitlement for the employee/type/year and the active policy aggregate for that employee's legal entity. The repository may call the existing policy repository or perform an EF projection, but the handler must receive one coherent state object:

```csharp
public async Task<LeaveRequestSubmitState?> GetSubmitStateAsync(
    Guid tenantId,
    Guid employeeId,
    Guid leaveTypeId,
    int year,
    CancellationToken ct = default)
{
    var entitlement = await _db.LeaveEntitlements
        .Include(e => e.LeaveType)
        .Where(e =>
            e.TenantId == tenantId &&
            e.EmployeeId == employeeId &&
            e.LeaveTypeId == leaveTypeId &&
            e.Year == year)
        .SingleOrDefaultAsync(ct);

    if (entitlement is null)
    {
        return null;
    }

    var employeeLegalEntity = await _db.Employees
        .AsNoTracking()
        .Where(e => e.TenantId == tenantId && e.Id == employeeId)
        .Select(e => e.LegalEntityId)
        .SingleOrDefaultAsync(ct);

    var policy = await LoadActivePolicyAggregateAsync(tenantId, employeeLegalEntity, year, ct);
    if (policy is null)
    {
        return null;
    }

    var policyLeaveType = policy.LeaveTypes.SingleOrDefault(row => row.LeaveTypeId == leaveTypeId);
    if (policyLeaveType is null)
    {
        return null;
    }

    return new LeaveRequestSubmitState(
        Entitlement: entitlement,
        Policy: policy,
        LegalEntityId: employeeLegalEntity,
        LeaveTypeName: entitlement.LeaveType.Name,
        LeaveTypeCode: entitlement.LeaveType.Code,
        NoticePeriodDays: policyLeaveType.NoticePeriodDays,
        MinDaysPerRequest: policyLeaveType.MinDaysPerRequest,
        MaxConsecutiveDays: policyLeaveType.MaxConsecutiveDays,
        RequiresDocument: entitlement.LeaveType.RequiresDocument,
        DocumentRequiredAfterDays: entitlement.LeaveType.DocumentRequiredAfterDays);
}
```

Use the actual policy aggregate types and property names from Part 2/3. If `LoadActivePolicyAggregateAsync` would duplicate the existing `ILeavePolicyRepository`, inject and reuse `ILeavePolicyRepository.ListActiveAggregatesByLegalEntityIdsAsync`.

- [ ] **Step 3: Implement active delegates query**

```csharp
public async Task<IReadOnlyList<LeaveApprovalDelegateRow>> ListActiveDelegatesAsync(
    Guid tenantId,
    IReadOnlyCollection<Guid> approverEmployeeIds,
    DateOnly startDate,
    DateOnly endDate,
    CancellationToken ct = default)
{
    if (approverEmployeeIds.Count == 0)
    {
        return [];
    }

    return await _db.LeaveApprovalDelegates
        .AsNoTracking()
        .Where(row =>
            row.TenantId == tenantId &&
            approverEmployeeIds.Contains(row.ApproverEmployeeId) &&
            row.StartDate <= endDate &&
            row.EndDate >= startDate)
        .Select(row => new LeaveApprovalDelegateRow(
            row.ApproverEmployeeId,
            row.DelegateEmployeeId))
        .ToListAsync(ct);
}
```

- [ ] **Step 4: Implement atomic pending request creation**

`AddPendingRequestAsync` must reserve only paid days:

```csharp
public async Task AddPendingRequestAsync(
    LeaveRequestWriteSet writeSet,
    CancellationToken ct = default)
{
    writeSet.Entitlement.PendingDays += writeSet.Request.PaidDays;
    writeSet.Entitlement.UpdatedAt = DateTime.UtcNow;

    await _db.LeaveRequests.AddAsync(writeSet.Request, ct);
    await _db.LeaveRequestApprovers.AddRangeAsync(writeSet.Approvers, ct);
    await _db.LeaveRequestDocuments.AddRangeAsync(writeSet.Documents, ct);
    await _db.SaveChangesAsync(ct);
}
```

If this repository already has a unit-of-work transaction helper, use it around request, approvers, documents, and entitlement update. Do not split the pending-days reservation into a second save.

- [ ] **Step 5: Register repository**

```csharp
services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
```

- [ ] **Step 6: Verify Task 3**

Run:

```powershell
dotnet build ONEVO.sln
```

Expected result: build passes with the new repository and interface registered.

---

### Task 4: Approver resolver with delegation and configurable escalation owner

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/ILeaveApproverResolver.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveApproverResolver.cs`
- Edit: `src/ONEVO.Application/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveApproverResolverTests.cs`

**Interfaces:**
- Produces: `ILeaveApproverResolver.ResolveAsync(Guid tenantId, Guid employeeId, DateOnly startDate, DateOnly endDate, CancellationToken ct)`
- Consumes: existing employee hierarchy repository, `ILeaveRequestRepository.ListActiveDelegatesAsync`, `LeaveRequestOptions.EscalationOwnerEmployeeId`
- Consumes later: Phase 5 approval workflow

- [ ] **Step 1: Add resolver contracts**

Create `ILeaveApproverResolver.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveApproverResolver
{
    Task<LeaveApproverResolution> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}

public sealed record LeaveApproverResolution(
    IReadOnlyList<LeaveApproverResolutionRow> Approvers);

public sealed record LeaveApproverResolutionRow(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    Guid? DelegatedFromApproverId);
```

- [ ] **Step 2: Write failing resolver tests**

Cover the concrete behavior this phase can guarantee:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveApproverResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesDirectManagerAsFirstApprover()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy
            .Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerId);

        var requests = new Mock<ILeaveRequestRepository>();
        requests
            .Setup(x => x.ListActiveDelegatesAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var resolver = new LeaveApproverResolver(
            hierarchy.Object,
            requests.Object,
            Options.Create(new LeaveRequestOptions()));

        var result = await resolver.ResolveAsync(
            tenantId,
            employeeId,
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 18));

        result.Approvers.Should().ContainSingle()
            .Which.ApproverEmployeeId.Should().Be(managerId);
    }

    [Fact]
    public async Task ResolveAsync_AppliesActiveDelegation()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy
            .Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerId);

        var requests = new Mock<ILeaveRequestRepository>();
        requests
            .Setup(x => x.ListActiveDelegatesAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveApprovalDelegateRow(managerId, delegateId)]);

        var resolver = new LeaveApproverResolver(
            hierarchy.Object,
            requests.Object,
            Options.Create(new LeaveRequestOptions()));

        var result = await resolver.ResolveAsync(
            tenantId,
            employeeId,
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 20));

        var row = result.Approvers.Should().ContainSingle().Subject;
        row.ApproverEmployeeId.Should().Be(delegateId);
        row.DelegatedFromApproverId.Should().Be(managerId);
    }

    [Fact]
    public async Task ResolveAsync_UsesConfiguredEscalationOwnerWhenManagerMissing()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var escalationOwnerId = Guid.NewGuid();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        hierarchy
            .Setup(x => x.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var requests = new Mock<ILeaveRequestRepository>();
        requests
            .Setup(x => x.ListActiveDelegatesAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var resolver = new LeaveApproverResolver(
            hierarchy.Object,
            requests.Object,
            Options.Create(new LeaveRequestOptions { EscalationOwnerEmployeeId = escalationOwnerId }));

        var result = await resolver.ResolveAsync(
            tenantId,
            employeeId,
            new DateOnly(2026, 8, 18),
            new DateOnly(2026, 8, 18));

        result.Approvers.Should().ContainSingle()
            .Which.ApproverEmployeeId.Should().Be(escalationOwnerId);
    }
}
```

Use the actual hierarchy repository interface name from the repo. If direct-manager lookup has a different method name, keep the resolver behavior and adapt only the call site.

- [ ] **Step 3: Implement resolver**

```csharp
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.Leave.Request.Options;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed class LeaveApproverResolver : ILeaveApproverResolver
{
    private readonly IEmployeeHierarchyClosureRepository _hierarchyRepository;
    private readonly ILeaveRequestRepository _requestRepository;
    private readonly LeaveRequestOptions _options;

    public LeaveApproverResolver(
        IEmployeeHierarchyClosureRepository hierarchyRepository,
        ILeaveRequestRepository requestRepository,
        IOptions<LeaveRequestOptions> options)
    {
        _hierarchyRepository = hierarchyRepository;
        _requestRepository = requestRepository;
        _options = options.Value;
    }

    public async Task<LeaveApproverResolution> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var approverId = await _hierarchyRepository.GetDirectManagerEmployeeIdAsync(tenantId, employeeId, ct)
            ?? _options.EscalationOwnerEmployeeId;

        if (approverId is null)
        {
            return new LeaveApproverResolution([]);
        }

        var delegateRows = await _requestRepository.ListActiveDelegatesAsync(
            tenantId,
            [approverId.Value],
            startDate,
            endDate,
            ct);

        var delegateRow = delegateRows.SingleOrDefault(row => row.ApproverEmployeeId == approverId.Value);
        if (delegateRow is not null)
        {
            return new LeaveApproverResolution([
                new LeaveApproverResolutionRow(delegateRow.DelegateEmployeeId, 1, delegateRow.ApproverEmployeeId)
            ]);
        }

        return new LeaveApproverResolution([
            new LeaveApproverResolutionRow(approverId.Value, 1, null)
        ]);
    }
}
```

- [ ] **Step 4: Register resolver**

```csharp
services.AddScoped<ILeaveApproverResolver, LeaveApproverResolver>();
```

- [ ] **Step 5: Verify Task 4**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApproverResolverTests"
```

Expected result: resolver tests pass.

---

### Task 5: Conflict snapshot, team absence warning, and tentative calendar interface

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/ILeaveRequestConflictProvider.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/ILeaveTentativeCalendarWriter.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/ILeaveTeamAbsenceWarningService.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveTeamAbsenceWarningService.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/NoOpLeaveRequestConflictProvider.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Services/NoOpLeaveTentativeCalendarWriter.cs`
- Edit: `src/ONEVO.Application/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveTeamAbsenceWarningServiceTests.cs`

**Interfaces:**
- Produces: conflict snapshot source for Screen 5
- Produces: tentative calendar writer seam for later real calendar adapter
- Consumes: `EmployeeHierarchyClosure`, policy max team absence percent, request repository counts

- [ ] **Step 1: Add conflict and calendar interfaces**

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveRequestConflictProvider
{
    Task<IReadOnlyList<LeaveRequestCalendarConflict>> ListConflictsAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}

public sealed record LeaveRequestCalendarConflict(
    string Source,
    string Title,
    DateTime? StartsAt,
    DateTime? EndsAt);

public interface ILeaveTentativeCalendarWriter
{
    Task CreateTentativeAsync(
        Guid tenantId,
        Guid leaveRequestId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}
```

Baseline implementations:

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public sealed class NoOpLeaveRequestConflictProvider : ILeaveRequestConflictProvider
{
    public Task<IReadOnlyList<LeaveRequestCalendarConflict>> ListConflictsAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        IReadOnlyList<LeaveRequestCalendarConflict> result = [];
        return Task.FromResult(result);
    }
}

public sealed class NoOpLeaveTentativeCalendarWriter : ILeaveTentativeCalendarWriter
{
    public Task CreateTentativeAsync(
        Guid tenantId,
        Guid leaveRequestId,
        Guid employeeId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Add team absence warning service**

```csharp
namespace ONEVO.Application.Features.Leave.Request.Services;

public interface ILeaveTeamAbsenceWarningService
{
    Task<LeaveTeamAbsenceWarning?> BuildWarningAsync(
        Guid tenantId,
        Guid managerEmployeeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal? maxTeamAbsencePercent,
        CancellationToken ct = default);
}

public sealed record LeaveTeamAbsenceWarning(
    decimal TeamAbsencePercent,
    string Message);
```

Implementation:

```csharp
public sealed class LeaveTeamAbsenceWarningService : ILeaveTeamAbsenceWarningService
{
    private readonly IEmployeeHierarchyClosureRepository _hierarchyRepository;
    private readonly ILeaveRequestRepository _requestRepository;

    public LeaveTeamAbsenceWarningService(
        IEmployeeHierarchyClosureRepository hierarchyRepository,
        ILeaveRequestRepository requestRepository)
    {
        _hierarchyRepository = hierarchyRepository;
        _requestRepository = requestRepository;
    }

    public async Task<LeaveTeamAbsenceWarning?> BuildWarningAsync(
        Guid tenantId,
        Guid managerEmployeeId,
        DateOnly startDate,
        DateOnly endDate,
        decimal? maxTeamAbsencePercent,
        CancellationToken ct = default)
    {
        if (maxTeamAbsencePercent is null)
        {
            return null;
        }

        var teamMemberIds = await _hierarchyRepository.ListDescendantEmployeeIdsAsync(
            tenantId,
            managerEmployeeId,
            ct);

        if (teamMemberIds.Count == 0)
        {
            return null;
        }

        var absentCount = await _requestRepository.CountPendingOrApprovedInRangeAsync(
            tenantId,
            teamMemberIds,
            startDate,
            endDate,
            ct);

        var percent = Math.Round(absentCount * 100m / teamMemberIds.Count, 2);
        if (percent <= maxTeamAbsencePercent.Value)
        {
            return null;
        }

        return new LeaveTeamAbsenceWarning(
            percent,
            $"{absentCount} team members already have pending or approved leave during this period.");
    }
}
```

- [ ] **Step 3: Register services**

```csharp
services.AddScoped<ILeaveRequestConflictProvider, NoOpLeaveRequestConflictProvider>();
services.AddScoped<ILeaveTentativeCalendarWriter, NoOpLeaveTentativeCalendarWriter>();
services.AddScoped<ILeaveTeamAbsenceWarningService, LeaveTeamAbsenceWarningService>();
```

- [ ] **Step 4: Verify Task 5**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveTeamAbsenceWarningServiceTests"
```

Expected result: warning service tests pass and no calendar integration is faked inside the handler.

---

### Task 6: Submit command, validator, and handler

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequestCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequestCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/SubmitLeaveRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces: own and on-behalf submission application path
- Consumes: current user service, employee repository, leave request repository, file record repository, request options, day calculator, holiday provider, approver resolver, conflict provider, team absence warning service, tentative calendar writer

- [ ] **Step 1: Add command and validator**

```csharp
using MediatR;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Commands;

public sealed record SubmitLeaveRequestCommand(
    Guid? EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? HalfDayPeriod,
    string? Reason,
    IReadOnlyList<Guid> FileRecordIds,
    bool IsOnBehalfRequest) : IRequest<LeaveRequestResponse>;
```

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Leave.Request.Commands;

public sealed class SubmitLeaveRequestCommandValidator : AbstractValidator<SubmitLeaveRequestCommand>
{
    private static readonly string[] HalfDayValues = ["AM", "PM"];

    public SubmitLeaveRequestCommandValidator()
    {
        RuleFor(x => x.LeaveTypeId).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.HalfDayPeriod)
            .Must(value => value is null || HalfDayValues.Contains(value))
            .WithMessage("HalfDayPeriod must be AM or PM.");
        RuleFor(x => x.EmployeeId)
            .NotEmpty()
            .When(x => x.IsOnBehalfRequest);
    }
}
```

- [ ] **Step 2: Write failing handler tests**

Cover at least these cases in `SubmitLeaveRequestCommandHandlerTests.cs`:

```csharp
[Fact]
public async Task Handle_WhenBackdatedAndConfigDisallowsBackdating_ReturnsConflict()
{
    // Arrange with AllowBackdatedRequests = false and request start date before injected today.
    // Assert the handler returns the repo's standard conflict exception type or result failure.
}

[Fact]
public async Task Handle_WhenOverlapExists_BlocksSubmission()
{
    // Arrange ILeaveRequestRepository.HasOverlappingPendingOrApprovedRequestAsync = true.
    // Assert no request is persisted.
}

[Fact]
public async Task Handle_WhenDocumentRequiredAfterThresholdAndNoFile_BlocksSubmission()
{
    // Arrange RequiresDocument = true, DocumentRequiredAfterDays = 2, calculated total = 3.
    // Assert missing supporting document error.
}

[Fact]
public async Task Handle_WhenBalanceShortAndUnpaidSplitDisabled_BlocksSubmission()
{
    // Arrange current remaining = 1 and total days = 3 with AllowUnpaidSplitWhenBalanceShort = false.
    // Assert no request is persisted.
}

[Fact]
public async Task Handle_WhenBalanceShortAndUnpaidSplitEnabled_SplitsPaidAndUnpaidDays()
{
    // Arrange current remaining = 1 and total days = 3 with AllowUnpaidSplitWhenBalanceShort = true.
    // Assert request PaidDays = 1, UnpaidDays = 2, and entitlement PendingDays increases by 1.
}

[Fact]
public async Task Handle_WhenNoticePeriodMissed_SavesWarningAndAllowsSubmit()
{
    // Arrange NoticePeriodDays larger than days until start.
    // Assert request.NoticePeriodMissed = true and request is persisted as Pending.
}

[Fact]
public async Task Handle_WhenNoApproverResolved_BlocksSubmission()
{
    // Arrange ILeaveApproverResolver returns no rows.
    // Assert no request is persisted.
}
```

Use the repo's actual error style. Existing handlers likely use an application exception, result wrapper, or `NotFoundException`/`ValidationException`; follow the local pattern exactly.

- [ ] **Step 3: Implement handler flow**

Handler pseudo-code with exact decisions:

```csharp
public async Task<LeaveRequestResponse> Handle(SubmitLeaveRequestCommand command, CancellationToken ct)
{
    var tenantId = _currentUser.GetTenantIdOrThrow();
    var requesterUserId = _currentUser.GetUserIdOrThrow();
    var today = _clock.Today;

    var requesterEmployee = await _employeeRepository.GetByUserIdAsync(tenantId, requesterUserId, ct);
    if (requesterEmployee is null)
    {
        throw new NotFoundException("Employee profile was not found for the current user.");
    }

    var targetEmployee = command.IsOnBehalfRequest
        ? await _employeeRepository.GetByIdAsync(tenantId, command.EmployeeId!.Value, ct)
        : requesterEmployee;

    if (targetEmployee is null)
    {
        throw new NotFoundException("Employee was not found.");
    }

    if (command.StartDate < today && !_options.AllowBackdatedRequests)
    {
        throw new ValidationException("Backdated leave requests are not enabled.");
    }

    var rangeDays = command.EndDate.DayNumber - command.StartDate.DayNumber + 1;
    if (rangeDays > _options.MaximumRequestRangeDays)
    {
        throw new ValidationException("Leave request date range exceeds the configured maximum.");
    }

    if (await _requestRepository.HasOverlappingPendingOrApprovedRequestAsync(
            tenantId,
            targetEmployee.Id,
            command.StartDate,
            command.EndDate,
            ct))
    {
        throw new ValidationException("A pending or approved leave request already overlaps this period.");
    }

    var year = command.StartDate.Year;
    var state = await _requestRepository.GetSubmitStateAsync(
        tenantId,
        targetEmployee.Id,
        command.LeaveTypeId,
        year,
        ct);

    if (state is null)
    {
        throw new ValidationException("No active entitlement and policy were found for this employee, leave type, and year.");
    }

    var standardWorkingDays = LeavePolicyLegalEntityConfigReader.ReadStandardWorkingDays(state.Policy, state.LegalEntityId);
    var holidays = await _holidayProvider.ListHolidaysAsync(
        tenantId,
        state.LegalEntityId,
        command.StartDate,
        command.EndDate,
        ct);

    var calculatedDays = _dayCalculator.Calculate(new LeaveRequestDayCalculationInput(
        command.StartDate,
        command.EndDate,
        command.HalfDayPeriod,
        standardWorkingDays,
        holidays));

    if (calculatedDays.TotalDays <= 0)
    {
        throw new ValidationException("Leave request must include at least one working day.");
    }

    var warnings = new List<LeaveRequestWarningResponse>();
    ValidateBlockingPolicyRules(command, state, calculatedDays.TotalDays, warnings, today);

    var currentRemaining = (state.Entitlement.TotalDays + state.Entitlement.CarriedForwardDays)
        - state.Entitlement.UsedDays
        - state.Entitlement.PendingDays;

    var paidDays = Math.Min(calculatedDays.TotalDays, Math.Max(0m, currentRemaining));
    var unpaidDays = calculatedDays.TotalDays - paidDays;
    if (unpaidDays > 0m && !_options.AllowUnpaidSplitWhenBalanceShort)
    {
        throw new ValidationException("Insufficient leave balance for this request.");
    }

    await ValidateFileRecordsAsync(tenantId, command.FileRecordIds, ct);

    var approverResolution = await _approverResolver.ResolveAsync(
        tenantId,
        targetEmployee.Id,
        command.StartDate,
        command.EndDate,
        ct);

    if (approverResolution.Approvers.Count == 0)
    {
        throw new ValidationException("No approver could be resolved for this leave request.");
    }

    var calendarConflicts = await _conflictProvider.ListConflictsAsync(
        tenantId,
        targetEmployee.Id,
        command.StartDate,
        command.EndDate,
        ct);

    var teamWarning = await _teamAbsenceWarningService.BuildWarningAsync(
        tenantId,
        targetEmployee.Id,
        command.StartDate,
        command.EndDate,
        state.Policy.MaxTeamAbsencePercent,
        ct);

    if (teamWarning is not null)
    {
        warnings.Add(new LeaveRequestWarningResponse("team_absence_threshold", teamWarning.Message));
    }

    var request = BuildPendingRequest(command, tenantId, requesterEmployee, targetEmployee, state, calculatedDays.TotalDays, paidDays, unpaidDays, warnings, calendarConflicts);
    var approvers = BuildApproverRows(request.Id, approverResolution.Approvers);
    var documents = BuildDocumentRows(request.Id, command.FileRecordIds);

    await _requestRepository.AddPendingRequestAsync(
        new LeaveRequestWriteSet(request, approvers, documents, state.Entitlement),
        ct);

    if (_options.TentativeCalendarEnabled)
    {
        await _tentativeCalendarWriter.CreateTentativeAsync(
            tenantId,
            request.Id,
            targetEmployee.Id,
            command.StartDate,
            command.EndDate,
            ct);
    }

    return MapResponse(request, approvers, state, warnings, calendarConflicts, currentRemaining);
}
```

`ValidateBlockingPolicyRules` must apply:
- `RequireReason` from config.
- `MinDaysPerRequest` from persisted policy leave-type row.
- Blackout periods from persisted policy blackout rows.
- Required document threshold from persisted leave type row.
- Notice-period warning from persisted policy leave-type row.
- Max consecutive days warning from persisted policy leave-type row.

Do not read any of those values from constants in the handler.

- [ ] **Step 4: Conflict snapshot JSON**

Serialize a stable JSON object into `LeaveRequest.ConflictSnapshotJson`:

```json
{
  "warnings": [
    { "code": "notice_period_missed", "message": "Notice period was missed." }
  ],
  "calendarConflicts": [
    { "source": "calendar", "title": "Quarterly planning", "startsAt": "2026-08-18T10:00:00Z", "endsAt": "2026-08-18T11:00:00Z" }
  ],
  "teamAbsencePercent": 42.5
}
```

Use the repo's standard JSON serializer options. Do not store UI-only text that cannot be localized later unless existing notification/error handling already uses literal English messages.

- [ ] **Step 5: Verify Task 6**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~SubmitLeaveRequestCommandHandlerTests"
```

Expected result: all submission behavior tests pass.

---

### Task 7: Own list query and HTTP controller

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Request/Queries/ListMyLeaveRequestsQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Queries/ListMyLeaveRequestsQueryValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Queries/ListMyLeaveRequestsQueryHandler.cs`
- Create: `src/ONEVO.Api/Controllers/V1/LeaveRequestsController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/ListMyLeaveRequestsQueryHandlerTests.cs`
- Add integration tests if the repo has endpoint test coverage for Leave controllers

**Interfaces:**
- Produces: `POST /api/v1/leave/requests`
- Produces: `POST /api/v1/leave/requests/on-behalf`
- Produces: `GET /api/v1/leave/requests/my`
- Consumes: existing permission attributes and controller response conventions

- [ ] **Step 1: Add own-list query**

```csharp
using MediatR;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Request.Queries;

public sealed record ListMyLeaveRequestsQuery(
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate,
    Guid? LeaveTypeId) : IRequest<IReadOnlyList<LeaveRequestListItemResponse>>;
```

Handler:

```csharp
public async Task<IReadOnlyList<LeaveRequestListItemResponse>> Handle(
    ListMyLeaveRequestsQuery query,
    CancellationToken ct)
{
    var tenantId = _currentUser.GetTenantIdOrThrow();
    var userId = _currentUser.GetUserIdOrThrow();
    var employee = await _employeeRepository.GetByUserIdAsync(tenantId, userId, ct);
    if (employee is null)
    {
        throw new NotFoundException("Employee profile was not found for the current user.");
    }

    var rows = await _requestRepository.ListOwnAsync(
        tenantId,
        employee.Id,
        new LeaveRequestListFilter(query.Status, query.FromDate, query.ToDate, query.LeaveTypeId),
        ct);

    return rows.Select(LeaveRequestMapper.ToListItem).ToList();
}
```

- [ ] **Step 2: Add controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Leave.Request.Commands;
using ONEVO.Application.Features.Leave.Request.DTOs.Requests;
using ONEVO.Application.Features.Leave.Request.Queries;

namespace ONEVO.Api.Controllers.V1;

[ApiController]
[Route("api/v1/leave/requests")]
public sealed class LeaveRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveRequestsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> Submit([FromBody] SubmitLeaveRequestRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(new SubmitLeaveRequestCommand(
            EmployeeId: null,
            LeaveTypeId: request.LeaveTypeId,
            StartDate: request.StartDate,
            EndDate: request.EndDate,
            HalfDayPeriod: request.HalfDayPeriod,
            Reason: request.Reason,
            FileRecordIds: request.FileRecordIds ?? [],
            IsOnBehalfRequest: false), ct);

        return CreatedAtAction(nameof(GetMine), new { id = response.Id }, response);
    }

    [HttpPost("on-behalf")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> SubmitOnBehalf([FromBody] SubmitLeaveRequestOnBehalfRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(new SubmitLeaveRequestCommand(
            EmployeeId: request.EmployeeId,
            LeaveTypeId: request.LeaveTypeId,
            StartDate: request.StartDate,
            EndDate: request.EndDate,
            HalfDayPeriod: request.HalfDayPeriod,
            Reason: request.Reason,
            FileRecordIds: request.FileRecordIds ?? [],
            IsOnBehalfRequest: true), ct);

        return CreatedAtAction(nameof(GetMine), new { id = response.Id }, response);
    }

    [HttpGet("my")]
    [RequirePermission("leave:read-own")]
    public async Task<ActionResult<IReadOnlyList<LeaveRequestListItemResponse>>> GetMine(
        [FromQuery] string? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? leaveTypeId,
        CancellationToken ct)
    {
        var response = await _mediator.Send(new ListMyLeaveRequestsQuery(
            status,
            fromDate,
            toDate,
            leaveTypeId), ct);

        return Ok(response);
    }
}
```

If the repo has an `ApiResponse<T>` wrapper, return the same wrapper used by `LeaveEntitlementsController` and `LeaveBalancesController`.

- [ ] **Step 3: Verify Task 7**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~ListMyLeaveRequestsQueryHandlerTests"
dotnet build ONEVO.sln
```

Expected result: query tests pass and controller compiles with the existing response convention.

---

### Task 8: Integration coverage, live dev-DB smoke, and documentation sync

**Files:**
- Add integration tests under the existing Leave or API test folder if present
- Edit: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Edit: `docs/superpowers/plans/next/SUMMARY.md`
- Edit: `docs/superpowers/plans/SUMMARY.md`
- Add Postman/API docs only if this repo already documents new endpoints alongside previous Leave endpoints

**Interfaces:**
- Verifies: tenant isolation, permissions, request creation, pending reservation, own list
- Produces: updated plan index status

- [ ] **Step 1: Add endpoint or handler integration tests**

Cover:
- `POST /api/v1/leave/requests` creates a pending request for the caller's employee.
- `POST /api/v1/leave/requests/on-behalf` requires `leave:manage`.
- Own submit does not allow changing `EmployeeId`.
- Pending submission increases `LeaveEntitlement.PendingDays` by `PaidDays`.
- Own list returns only the caller's requests for the current tenant.
- Tenant A cannot read or affect Tenant B requests.

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Leave.Request"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet build ONEVO.sln
```

Expected result: unit tests pass, architecture tests pass, build passes.

- [ ] **Step 3: Run live dev-DB smoke**

Against the existing dev smoke tenants from `DevSmokeTestTenantSeeder`, verify:
- An employee with generated entitlement can submit annual leave.
- The response returns pending status, paid/unpaid split, warnings, and balance impact.
- `GET /api/v1/leave/balances/my` shows reduced remaining because `PendingDays` increased.
- `GET /api/v1/leave/requests/my` shows the new request.
- HR can submit on behalf of an employee with `leave:manage`.

Record exact commands and result in the phase summary. If Docker/Testcontainers is unavailable, record that the live smoke is pending for the same environmental reason noted in Part 3.

- [ ] **Step 4: Update summaries**

Update the phase index and top-level summaries:
- `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: mark Phase 4 as written, or executed if implementation has been completed.
- `docs/superpowers/plans/next/SUMMARY.md`: add `part-4-request-submission.md` to the written-in-full list.
- `docs/superpowers/plans/SUMMARY.md`: change the Leave Management row from Parts 1-3 executed to Parts 1-3 executed and Part 4 written.

---

## Execution Handoff

Start with Task 1 and keep each task green before moving to the next one. The safest implementation order is:

1. Pure calculator/options/providers.
2. DTOs and repository contracts.
3. EF repository implementation.
4. Approver resolver.
5. Warning/conflict/calendar provider seams.
6. Submit command handler.
7. List query and controller.
8. Integration/live smoke/docs sync.

Key behavior to preserve:
- Submission reserves only paid days in `PendingDays`.
- Approval balance cutting stays in Phase 5.
- Backdating and unpaid split are controlled by `LeaveRequestOptions`.
- Working days and holidays are provider/policy driven.
- Calendar conflicts are warnings and are stored in `ConflictSnapshotJson`.
- No business value is hard-coded in production handlers.

Before marking complete, run the focused unit suite, architecture suite, build, and live dev-DB smoke or explicitly document the environmental blocker.
