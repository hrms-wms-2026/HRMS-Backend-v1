# Leave Management - Part 6: Cancellation (Phase 6 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend cancellation workflow for Screen 9: employee cancellation from My Leave, HR cancellation from All Requests, approved-request balance restoration, in-progress partial cancellation, approval-stop behavior, side-effect outbox messages, and user-facing cancellation errors.

**Architecture:** Part 6 keeps cancellation decisions in pure helpers and transactional persistence in one EF repository/service boundary. The handler classifies the request as pending-style, approved-full, or approved-partial; computes restorable paid days from stored per-day allocations; updates `LeaveRequest`, `LeaveRequestApprover`, `LeaveEntitlement`, and `LeaveBalanceAudit` in the same unit of work; writes in-app notifications through `INotificationDispatcher`; and enqueues one leave-cancellation outbox message for calendar, workforce-presence, payroll, and external-notification side effects. Production behavior must come from request data, persisted leave data, legal-entity data, holiday providers, or validated configuration.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, transactional outbox, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product context from `C:\HR\leave-management-complete.md`; depends on `docs/superpowers/plans/next/2026-08-21-leave-management/part-5-approval-workflow.md`.

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat attached documents as context only. The active user request is this Part 6 backend plan, with the explicit rule that cancellation behavior must be configurable and must not hide business values in code.
- Phase 5 must be executed first. This plan consumes the Phase 5 approval shape: approved requests, pending-to-used paid-day movement, request-info pause/resume status, approval-mode history, and leave side-effect outbox registration.
- Use existing permission codes. Employee self-cancel needs `leave:read-own`; HR cancel needs `leave:manage`. The handler must still enforce ownership, because route permission is not the same thing as data access.
- Use one public backend route: `POST /api/v1/leave/requests/{requestId}/cancel`. The endpoint must allow either `leave:read-own` or `leave:manage`; if the existing `RequirePermissionAttribute` cannot express this, add an explicit any-permission filter instead of weakening authorization.
- Cancellation is not an idempotent success. A second cancellation returns `This leave request has already been cancelled`.
- Rejected requests cannot be cancelled. Return `Rejected requests cannot be cancelled`.
- A leave period whose `EndDate` is before the cancellation business date cannot be cancelled. Return `This leave period has already passed and cannot be cancelled`.
- HR cancellation always requires a non-empty reason. Return `A reason is required when cancelling on behalf of an employee`.
- Employee cancellation reason is optional unless a validated `Leave:Cancellation:RequireEmployeeReason` config value is enabled.
- Pending-style cancellation includes `pending` and Phase 5's `information_requested`. It releases pending paid-day reservations (`PendingDays -= PaidDays`), marks open approver rows cancelled, and does not write a `LeaveBalanceAudit` row because no used balance was deducted.
- Approved full cancellation restores approved paid days (`UsedDays -= RestoredPaidDays`) and writes one `LeaveBalanceAudit` row with `ChangeType = LeaveBalanceChangeTypes.Adjustment`.
- Approved partial cancellation applies only when the leave is in progress. It restores only paid units on stored request-day allocations whose leave date is on or after the effective cancellation date, writes one `Adjustment` audit row for the restored paid units, and records `PartialCancelEffectiveDate`.
- The effective cancellation date defaults to the employee legal entity's current date. Resolve that date from `LegalEntity.Timezone`; if missing, use a required validated `Leave:Cancellation:FallbackTimezone` config value. Do not use server local time or hard-coded UTC for business-date decisions.
- If a caller provides `EffectiveDate`, validate it against the original request range. For approved in-progress cancellation, `EffectiveDate` may be today or a future date through `EndDate`; days before it remain taken. For future approved leave, omit `EffectiveDate` and perform a full cancellation.
- Do not change balance for unpaid days. Unpaid days travel in the cancellation outbox payload so payroll/workforce consumers can remove or adjust their own side effects.
- External side effects must use the transactional outbox. Calendar removal/adjustment, Workforce Presence removal/adjustment, payroll deduction removal/adjustment, approval-card cleanup, push/email/chat delivery, and any future attendance write must not be called directly from controllers or handlers.
- Every outbox message type added in this part must have a registered `IOutboxMessageHandler`. If real calendar/payroll/workforce adapters are not available, register an explicit no-op handler so the outbox processor does not retry and fail.
- In-app notifications may be written in the same DbContext transaction through `INotificationDispatcher.SendTemplatedAsync`, because the dispatcher only adds database notification rows.
- Additive schema is allowed in this part for request-day allocations. Partial cancellation needs stored per-day paid/unpaid units; recalculating from today's policy/holiday data would make restoration depend on changed configuration.
- Keep closed vocabularies as string constants. Do not add C# enums or PostgreSQL enum/check constraints.
- Use optimistic concurrency for cancellation writes. Map PostgreSQL `xmin` as a nullable shadow concurrency token on `LeaveRequest`, matching `AccessGrantRequestConfiguration`; catch repository-level concurrency and return `This request was modified by another user. Please refresh and try again`.

---

### Task 1: Cancellation options, vocabularies, day allocation schema, and concurrency metadata

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Options/LeaveCancellationOptions.cs`
- Edit: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestDayAllocation.cs`
- Edit: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Edit: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs`
- Add migration: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddLeaveRequestDayAllocations.cs`
- Edit: `src/ONEVO.Api/appsettings.json`
- Edit: `src/ONEVO.Api/appsettings.Development.json`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationOptionsTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationVocabularyTests.cs`

**Interfaces:**
- Produces: `LeaveCancellationOptions`
- Produces: `LeaveRequestApproverStatuses.Cancelled`
- Produces: `LeaveRequestDayAllocationStatuses.Active`
- Produces: `LeaveRequestDayAllocationStatuses.Cancelled`
- Produces: `LeaveRequestDayAllocation`
- Produces: `LeaveRequest` `xmin` concurrency metadata
- Consumes later: cancellation classifier, allocation builder, repository, command handler

- [ ] **Step 1: Add cancellation options tests**

Create `LeaveCancellationOptionsTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Cancellation.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveCancellation()
    {
        LeaveCancellationOptions.SectionName.Should().Be("Leave:Cancellation");
    }

    [Fact]
    public void FallbackTimezone_MustBeConfigured()
    {
        var options = new LeaveCancellationOptions();

        options.FallbackTimezone.Should().BeNull();
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Colombo")]
    [InlineData("Sri Lanka Standard Time")]
    public void ResolveTimezone_AcceptsIanaAndWindowsTimezoneIds(string value)
    {
        LeaveCancellationOptions.ResolveTimezone(value).Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Add cancellation options**

Create `LeaveCancellationOptions.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Cancellation.Options;

public sealed class LeaveCancellationOptions
{
    public const string SectionName = "Leave:Cancellation";

    public string? FallbackTimezone { get; init; }

    public bool RequireEmployeeReason { get; init; }

    public static bool IsValidTimezone(string? timezone)
        => ResolveTimezone(timezone) is not null;

    public static TimeZoneInfo? ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return null;

        var trimmed = timezone.Trim();
        if (TryFind(trimmed, out var direct))
            return direct;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out var windowsId)
            && TryFind(windowsId, out var windows))
        {
            return windows;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var ianaId)
            && TryFind(ianaId, out var iana))
        {
            return iana;
        }

        return null;
    }

    private static bool TryFind(string timezoneId, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return zone is not null;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = null;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }
}
```

Register the options in the same startup location used by `LeaveRequestOptions` and Phase 5's `LeaveApprovalOptions`:

```csharp
services
    .AddOptions<LeaveCancellationOptions>()
    .Bind(configuration.GetSection(LeaveCancellationOptions.SectionName))
    .Validate(options => LeaveCancellationOptions.IsValidTimezone(options.FallbackTimezone),
        "Leave:Cancellation:FallbackTimezone must be a valid timezone id.")
    .ValidateOnStart();
```

Add the configuration section to both appsettings files:

```json
{
  "Leave": {
    "Cancellation": {
      "FallbackTimezone": "UTC",
      "RequireEmployeeReason": false
    }
  }
}
```

- [ ] **Step 3: Extend leave vocabularies**

Add to `LeaveVocabularies.cs`:

```csharp
public static class LeaveRequestApproverStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
    public const string InformationRequested = "information_requested";
    public const string Cancelled = "cancelled";
}

public static class LeaveRequestDayAllocationStatuses
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
}
```

If Phase 5 already added `InformationRequested`, preserve it and add only `Cancelled`.

Create `LeaveCancellationVocabularyTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationVocabularyTests
{
    [Fact]
    public void CancellationStatuses_UseStableWireValues()
    {
        LeaveRequestApproverStatuses.Cancelled.Should().Be("cancelled");
        LeaveRequestDayAllocationStatuses.Active.Should().Be("active");
        LeaveRequestDayAllocationStatuses.Cancelled.Should().Be("cancelled");
    }
}
```

- [ ] **Step 4: Add day allocation entity**

Create `LeaveRequestDayAllocation.cs`:

```csharp
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestDayAllocation : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public DateOnly LeaveDate { get; set; }
    public decimal DayUnit { get; set; }
    public decimal PaidUnit { get; set; }
    public decimal UnpaidUnit { get; set; }
    public string Status { get; set; } = LeaveRequestDayAllocationStatuses.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CancelledAt { get; set; }
}
```

Add to `ApplicationDbContext`:

```csharp
public DbSet<LeaveRequestDayAllocation> LeaveRequestDayAllocations => Set<LeaveRequestDayAllocation>();
```

Add configuration next to the other leave request configurations:

```csharp
public class LeaveRequestDayAllocationConfiguration : IEntityTypeConfiguration<LeaveRequestDayAllocation>
{
    public void Configure(EntityTypeBuilder<LeaveRequestDayAllocation> builder)
    {
        builder.ToTable("leave_request_day_allocations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.DayUnit).HasColumnType("numeric(3,1)");
        builder.Property(a => a.PaidUnit).HasColumnType("numeric(3,1)");
        builder.Property(a => a.UnpaidUnit).HasColumnType("numeric(3,1)");
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.LeaveRequestId, a.LeaveDate })
            .IsUnique()
            .HasDatabaseName("ix_leave_request_day_allocations_tenant_request_date");
        builder.HasIndex(a => new { a.TenantId, a.LeaveDate, a.Status })
            .HasDatabaseName("ix_leave_request_day_allocations_tenant_date_status");
        builder.HasOne<LeaveRequest>()
            .WithMany()
            .HasForeignKey(a => a.LeaveRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Add leave request concurrency token**

In `LeaveRequestConfiguration`, add the same nullable shadow `xmin` mapping used by `AccessGrantRequestConfiguration`:

```csharp
builder.Property<uint?>("xmin")
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
```

Do not call `UseXminAsConcurrencyToken()`. This project documents that the pinned Npgsql version does not expose that convenience method.

- [ ] **Step 6: Generate migration**

Run:

```powershell
dotnet ef migrations add AddLeaveRequestDayAllocations --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Inspect the generated migration and ensure:
- It creates `leave_request_day_allocations`.
- It creates tenant RLS policy for `leave_request_day_allocations`, matching the other leave tenant-owned tables.
- It does not create a physical `xmin` column on `leave_requests`.
- It updates the model snapshot with `LeaveRequest` shadow `xmin` metadata.

- [ ] **Step 7: Verify Task 1**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCancellationOptionsTests|FullyQualifiedName~LeaveCancellationVocabularyTests"
dotnet build ONEVO.sln
```

Expected result: tests pass and the migration compiles.

---

### Task 2: Pure cancellation helpers and day allocation builder

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveCancellationMessages.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveBusinessDateResolver.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveCancellationClassifier.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Helpers/LeaveRequestDayAllocationBuilder.cs`
- Edit: `src/ONEVO.Application/Features/Leave/Request/Services/LeaveRequestSubmissionEvaluator.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveBusinessDateResolverTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationClassifierTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveRequestDayAllocationBuilderTests.cs`

**Interfaces:**
- Produces: stable user-facing messages
- Produces: legal-entity/config driven business date
- Produces: pure cancellation classification
- Produces: stored day allocation drafts for request submission and legacy backfill
- Consumes later: command handler and repository

- [ ] **Step 1: Add messages**

Create `LeaveCancellationMessages.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public static class LeaveCancellationMessages
{
    public const string AlreadyCancelled = "This leave request has already been cancelled";
    public const string Rejected = "Rejected requests cannot be cancelled";
    public const string PeriodPassed = "This leave period has already passed and cannot be cancelled";
    public const string HrReasonRequired = "A reason is required when cancelling on behalf of an employee";
    public const string EmployeeReasonRequired = "A reason is required to cancel this leave request.";
    public const string Concurrency = "This request was modified by another user. Please refresh and try again";
    public const string NotOwner = "This request does not belong to you.";
    public const string NotCancellable = "This leave request cannot be cancelled in its current status.";
    public const string InvalidEffectiveDate = "Cancellation effective date must be within the leave request period.";
    public const string NoRestorableDays = "There are no future leave days to restore for this request.";
}
```

- [ ] **Step 2: Add business-date resolver**

Create `LeaveBusinessDateResolver.cs`:

```csharp
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Options;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public sealed class LeaveBusinessDateResolver
{
    private readonly IDateTimeProvider _clock;
    private readonly LeaveCancellationOptions _options;

    public LeaveBusinessDateResolver(
        IDateTimeProvider clock,
        IOptions<LeaveCancellationOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public DateOnly Today(string? legalEntityTimezone)
    {
        var timezoneId = string.IsNullOrWhiteSpace(legalEntityTimezone)
            ? _options.FallbackTimezone!
            : legalEntityTimezone.Trim();

        var zone = LeaveCancellationOptions.ResolveTimezone(timezoneId)!;
        var local = TimeZoneInfo.ConvertTime(_clock.UtcNow, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
```

Tests must prove:
- It uses `LegalEntity.Timezone` when present.
- It uses `Leave:Cancellation:FallbackTimezone` when legal entity timezone is missing.
- A UTC instant close to midnight can produce different dates for two configured timezones.

- [ ] **Step 3: Add cancellation classifier**

Create `LeaveCancellationClassifier.cs`:

```csharp
using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public enum LeaveCancellationKind
{
    PendingStyle,
    ApprovedFull,
    ApprovedPartial
}

public sealed record LeaveCancellationClassification(
    LeaveCancellationKind Kind,
    DateOnly BusinessDate,
    DateOnly? EffectiveDate);

public sealed class LeaveCancellationClassifier
{
    public Result<LeaveCancellationClassification> Classify(
        string status,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly businessDate,
        DateOnly? requestedEffectiveDate)
    {
        if (status == LeaveRequestStatuses.Cancelled)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.AlreadyCancelled);

        if (status == LeaveRequestStatuses.Rejected)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.Rejected);

        if (endDate < businessDate)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.PeriodPassed);

        if (status is LeaveRequestStatuses.Pending or LeaveRequestStatuses.InformationRequested)
        {
            return Result<LeaveCancellationClassification>.Success(
                new LeaveCancellationClassification(LeaveCancellationKind.PendingStyle, businessDate, null));
        }

        if (status != LeaveRequestStatuses.Approved)
            return Result<LeaveCancellationClassification>.Conflict(LeaveCancellationMessages.NotCancellable);

        if (requestedEffectiveDate is { } supplied
            && (supplied < startDate || supplied > endDate))
        {
            return Result<LeaveCancellationClassification>.Failure(LeaveCancellationMessages.InvalidEffectiveDate);
        }

        if (businessDate <= startDate)
        {
            return Result<LeaveCancellationClassification>.Success(
                new LeaveCancellationClassification(LeaveCancellationKind.ApprovedFull, businessDate, null));
        }

        var effectiveDate = requestedEffectiveDate ?? businessDate;
        if (effectiveDate < businessDate)
            effectiveDate = businessDate;

        return Result<LeaveCancellationClassification>.Success(
            new LeaveCancellationClassification(LeaveCancellationKind.ApprovedPartial, businessDate, effectiveDate));
    }
}
```

Use the project's actual `Result<T>` factory signatures when writing the implementation.

Tests must cover:
- Already cancelled returns the exact product message.
- Rejected returns the exact product message.
- Fully passed period returns the exact product message.
- Pending and information-requested are pending-style.
- Approved future request is full cancellation.
- Approved in-progress request defaults effective date to business date.
- Approved in-progress request accepts a future effective date through `EndDate`.
- Effective date outside request range fails.

- [ ] **Step 4: Add day allocation builder**

Create `LeaveRequestDayAllocationBuilder.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public sealed record LeaveRequestDayAllocationDraft(
    DateOnly LeaveDate,
    decimal DayUnit,
    decimal PaidUnit,
    decimal UnpaidUnit);

public sealed class LeaveRequestDayAllocationBuilder
{
    public IReadOnlyList<LeaveRequestDayAllocationDraft> Build(
        IReadOnlyList<DateOnly> countedDates,
        string? halfDayPeriod,
        decimal paidDays,
        decimal unpaidDays)
    {
        var paidRemaining = paidDays;
        var rows = new List<LeaveRequestDayAllocationDraft>();

        foreach (var date in countedDates)
        {
            var unit = !string.IsNullOrWhiteSpace(halfDayPeriod) && countedDates.Count == 1
                ? 0.5m
                : 1m;
            var paid = Math.Min(unit, Math.Max(0m, paidRemaining));
            paidRemaining -= paid;
            rows.Add(new LeaveRequestDayAllocationDraft(date, unit, paid, unit - paid));
        }

        var total = rows.Sum(x => x.DayUnit);
        if (total != paidDays + unpaidDays)
            throw new InvalidOperationException("Leave day allocations do not match the request total.");

        return rows;
    }

    public IReadOnlyList<LeaveRequestDayAllocation> ToEntities(
        Guid tenantId,
        Guid leaveRequestId,
        IReadOnlyList<LeaveRequestDayAllocationDraft> drafts,
        DateTimeOffset now)
        => drafts.Select(draft => new LeaveRequestDayAllocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeaveRequestId = leaveRequestId,
            LeaveDate = draft.LeaveDate,
            DayUnit = draft.DayUnit,
            PaidUnit = draft.PaidUnit,
            UnpaidUnit = draft.UnpaidUnit,
            Status = LeaveRequestDayAllocationStatuses.Active,
            CreatedAt = now
        }).ToList();
}
```

Tests must cover:
- Full-day dates produce one unit each.
- Single half-day produces `0.5` day unit.
- Paid units are allocated from the configured request split and persist as data for cancellation.
- Unpaid tail days do not become restorable paid days during later partial cancellation.
- A mismatch between counted dates and paid/unpaid totals throws.

- [ ] **Step 5: Expose counted dates from request evaluation**

Edit `LeaveRequestEvaluation` in `LeaveRequestSubmissionEvaluator.cs` to include `IReadOnlyList<DateOnly> CountedDates`.

Return `calculated.CountedDates` in the evaluation result. Preview responses do not need to expose this list; it is for submission persistence only.

- [ ] **Step 6: Verify Task 2**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveBusinessDateResolverTests|FullyQualifiedName~LeaveCancellationClassifierTests|FullyQualifiedName~LeaveRequestDayAllocationBuilderTests"
dotnet build ONEVO.sln
```

Expected result: helper tests pass and the request evaluator still compiles.

---

### Task 3: Persist request-day allocations on submission

**Files:**
- Edit: `src/ONEVO.Application/Features/Leave/Request/RepositoryInterfaces/ILeaveRequestRepository.cs`
- Edit: `src/ONEVO.Application/Features/Leave/Request/Commands/SubmitLeaveRequest/SubmitLeaveRequestCommandHandler.cs`
- Edit: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Request/EfLeaveRequestRepository.cs`
- Edit: `tests/ONEVO.Tests.Unit/Features/Leave/Request/SubmitLeaveRequestCommandHandlerTests.cs` or create it if the command handler is currently covered indirectly
- Edit: `tests/ONEVO.Tests.Unit/Features/Leave/Request/EfLeaveRequestRepositoryTests.cs` or create it if missing

**Interfaces:**
- Extends: `LeaveRequestWriteSet`
- Produces: persisted `LeaveRequestDayAllocation` rows for every new request
- Consumes later: partial cancellation restore calculation

- [ ] **Step 1: Extend repository write set**

Change `LeaveRequestWriteSet`:

```csharp
public sealed record LeaveRequestWriteSet(
    LeaveRequest Request,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveRequestDocument> Documents,
    IReadOnlyList<LeaveRequestDayAllocation> DayAllocations,
    LeaveEntitlement Entitlement);
```

Update every constructor call in tests and production code.

- [ ] **Step 2: Build allocations in submit handler**

Inject `LeaveRequestDayAllocationBuilder` into `SubmitLeaveRequestCommandHandler`.

After creating the `LeaveRequest`, build allocation entities from `draft.CountedDates`, `command.HalfDayPeriod`, `draft.PaidDays`, and `draft.UnpaidDays`:

```csharp
var allocationDrafts = _allocationBuilder.Build(
    draft.CountedDates,
    command.HalfDayPeriod,
    draft.PaidDays,
    draft.UnpaidDays);

var dayAllocations = _allocationBuilder.ToEntities(
    _currentUser.TenantId,
    requestId,
    allocationDrafts,
    now);
```

Pass `dayAllocations` into `LeaveRequestWriteSet`.

- [ ] **Step 3: Persist allocations atomically**

In `EfLeaveRequestRepository.AddPendingRequestAsync`, add:

```csharp
await _db.LeaveRequestDayAllocations.AddRangeAsync(writeSet.DayAllocations, ct);
```

Keep it in the same transaction and before the single `SaveChangesAsync` call.

- [ ] **Step 4: Add submission tests**

Cover:
- A 3-day paid request writes three active allocation rows with paid units summing to request `PaidDays`.
- A half-day request writes one allocation row with `DayUnit = 0.5`.
- A mixed paid/unpaid request writes allocation rows whose paid and unpaid sums match the request totals.
- If the repository detects overlap after allocation building, no request, approver, document, entitlement, or allocation write is committed.

- [ ] **Step 5: Verify Task 3**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Leave.Request"
dotnet build ONEVO.sln
```

Expected result: request tests pass and new allocation persistence compiles.

---

### Task 4: Cancellation DTOs, repository contract, and EF repository

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/DTOs/Requests/CancelLeaveRequestRequest.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/DTOs/Responses/CancelLeaveRequestResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Mappers/LeaveCancellationMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/RepositoryInterfaces/ILeaveCancellationRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Cancellation/EfLeaveCancellationRepository.cs`
- Edit: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationMapperTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/EfLeaveCancellationRepositoryTests.cs`

**Interfaces:**
- Produces: cancellation HTTP request/response contracts
- Produces: `ILeaveCancellationRepository`
- Consumes later: command handler and controller

- [ ] **Step 1: Add request and response contracts**

Create `CancelLeaveRequestRequest.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Cancellation.DTOs.Requests;

public sealed record CancelLeaveRequestRequest(
    string? Reason,
    DateOnly? EffectiveDate,
    string? ExpectedVersion);
```

Create `CancelLeaveRequestResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;

public sealed record CancelLeaveRequestResponse(
    Guid RequestId,
    string Status,
    bool IsPartialCancellation,
    DateOnly? EffectiveDate,
    decimal ReleasedPendingDays,
    decimal RestoredUsedDays,
    decimal RemainingDays,
    string? Reason,
    DateTimeOffset CancelledAt);
```

`ExpectedVersion` is optional so existing clients can still cancel, but when the frontend sends it from list/detail screens the repository must set the original `xmin` value and return the product concurrency message on stale writes.

- [ ] **Step 2: Add repository contract**

Create `ILeaveCancellationRepository.cs`:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.RepositoryInterfaces;

public interface ILeaveCancellationRepository
{
    Task<LeaveCancellationState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestDayAllocation>> ListAllocationsAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default);

    Task AddAllocationsAsync(
        IReadOnlyList<LeaveRequestDayAllocation> allocations,
        CancellationToken ct = default);

    Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default);

    void SetExpectedVersion(LeaveRequest request, string? expectedVersion);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record LeaveCancellationState(
    LeaveRequest Request,
    LeaveEntitlement? Entitlement,
    Employee Employee,
    LegalEntity? LegalEntity,
    string LeaveTypeName,
    string LeaveTypeCode,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveCancellationRecipient> ApproverRecipients);

public sealed record LeaveCancellationRecipient(
    Guid EmployeeId,
    Guid? UserId,
    string? DisplayName);
```

If Phase 5 already created an approval repository state that loads the same request/employee/type/approver graph, do not duplicate complex query logic blindly. Either reuse that repository where it fits or keep the cancellation repository focused on cancellation-specific tracked entities and recipient data.

- [ ] **Step 3: Implement EF repository**

Create `EfLeaveCancellationRepository.cs` using `ApplicationDbContext`.

`GetStateAsync` must:
- Load the target `LeaveRequest` as tracked by tenant and id.
- Load the target `LeaveEntitlement` as tracked by employee/type/year.
- Load the target `Employee` as no-tracking.
- Load `LegalEntity` by `Employee.LegalEntityId` as no-tracking.
- Load `LeaveType` as no-tracking.
- Load request approvers as tracked, ordered by sequence and id.
- Load approver recipient employees as no-tracking, including `UserId`.

`SetExpectedVersion` must parse `expectedVersion` as `uint` and set:

```csharp
_db.Entry(request).Property("xmin").OriginalValue = parsed;
```

Ignore null/blank/unparseable tokens, matching `IEmployeeRepository.SetExpectedVersion` precedent.

`SaveChangesAsync` must catch `DbUpdateConcurrencyException` and throw `ConcurrencyConflictException`:

```csharp
try
{
    return await _db.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException ex)
{
    throw new ConcurrencyConflictException(ex);
}
```

- [ ] **Step 4: Add mapper**

Create `LeaveCancellationMapper.cs`:

```csharp
using ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Cancellation.Mappers;

public static class LeaveCancellationMapper
{
    public static CancelLeaveRequestResponse ToResponse(
        LeaveRequest request,
        bool isPartialCancellation,
        decimal releasedPendingDays,
        decimal restoredUsedDays,
        decimal remainingDays,
        DateTimeOffset cancelledAt)
        => new(
            request.Id,
            request.Status,
            isPartialCancellation,
            request.PartialCancelEffectiveDate,
            releasedPendingDays,
            restoredUsedDays,
            remainingDays,
            request.CancellationReason,
            cancelledAt);
}
```

- [ ] **Step 5: Add repository tests**

Cover:
- `GetStateAsync` loads the request tracked and approver rows tracked.
- `GetStateAsync` includes legal entity timezone when the employee has a legal entity.
- `ListAllocationsAsync` returns active and cancelled rows ordered by date.
- `SetExpectedVersion` sets `xmin` original value when a numeric token is supplied.
- `SaveChangesAsync` translates `DbUpdateConcurrencyException` into `ConcurrencyConflictException`.

- [ ] **Step 6: Verify Task 4**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCancellationMapperTests|FullyQualifiedName~EfLeaveCancellationRepositoryTests"
dotnet build ONEVO.sln
```

Expected result: repository and mapper tests pass.

---

### Task 5: Outbox payloads, no-op handler, and notification templates

**Files:**
- Edit: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Outbox/LeaveRequestCancelledPayload.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Outbox/NoOpLeaveCancellationSideEffectOutboxHandler.cs`
- Edit: `src/ONEVO.Application/DependencyInjection.cs` or `src/ONEVO.Infrastructure/DependencyInjection.cs`, matching where outbox handlers are registered after Phase 5
- Edit: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationOutboxTests.cs`
- Edit: `tests/ONEVO.Tests.Unit/Features/Auth/NotificationTemplateSeederTests.cs`

**Interfaces:**
- Produces: `OutboxMessageTypes.LeaveRequestCancelled`
- Produces: cancellation side-effect payload
- Produces: registered no-op outbox handler
- Produces: in-app notification templates
- Consumes later: cancellation command handler

- [ ] **Step 1: Add outbox message type**

Add to `OutboxMessageTypes`:

```csharp
public const string LeaveRequestCancelled = "leave_request_cancelled";
```

Do not add a second message type for partial cancellation. Use the payload's `IsPartialCancellation` and `EffectiveDate` fields so consumers can remove or adjust calendar/workforce/payroll side effects from one stable event family.

- [ ] **Step 2: Add cancellation payload**

Create `LeaveRequestCancelledPayload.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Cancellation.Outbox;

public sealed record LeaveRequestCancelledPayload(
    Guid TenantId,
    Guid RequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    DateOnly OriginalStartDate,
    DateOnly OriginalEndDate,
    bool IsPartialCancellation,
    DateOnly? EffectiveDate,
    decimal ReleasedPendingDays,
    decimal RestoredPaidDays,
    decimal AffectedUnpaidDays,
    Guid CancelledByUserId,
    Guid CancelledByEmployeeId,
    bool CancelledByHr,
    string? Reason,
    DateTimeOffset CancelledAt);
```

Payload meaning:
- Pending-style cancellation: `ReleasedPendingDays > 0`, `RestoredPaidDays = 0`.
- Approved full cancellation: `ReleasedPendingDays = 0`, `RestoredPaidDays = request.PaidDays`.
- Approved partial cancellation: `EffectiveDate` is set, `RestoredPaidDays` is the sum of cancelled future allocation paid units, and `AffectedUnpaidDays` is the cancelled future allocation unpaid-unit sum.

- [ ] **Step 3: Add no-op handler**

Create `NoOpLeaveCancellationSideEffectOutboxHandler.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Cancellation.Outbox;

public sealed class NoOpLeaveCancellationSideEffectOutboxHandler : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.LeaveRequestCancelled;

    public Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
```

Register it:

```csharp
services.AddScoped<IOutboxMessageHandler, NoOpLeaveCancellationSideEffectOutboxHandler>();
```

If Phase 5 introduced a generic leave no-op side-effect handler, add `LeaveRequestCancelled` to that handler instead of creating a second no-op class, but keep a focused unit test for the cancellation type.

- [ ] **Step 4: Add notification templates**

Append templates in `NotificationTemplateSeeder`:

```csharp
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_cancelled_by_employee",
    InAppTitleTemplate = "Leave cancelled",
    InAppBodyTemplate = "{{employeeName}} cancelled {{leaveTypeName}} from {{startDate}} to {{endDate}}."
},
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_cancelled_by_hr",
    InAppTitleTemplate = "Leave cancelled by HR",
    InAppBodyTemplate = "{{cancelledByName}} cancelled your {{leaveTypeName}} from {{startDate}} to {{endDate}}. {{reason}}"
},
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_partially_cancelled",
    InAppTitleTemplate = "Leave partially cancelled",
    InAppBodyTemplate = "{{leaveTypeName}} from {{effectiveDate}} to {{endDate}} was cancelled. {{restoredDays}} days restored."
}
```

Keep the `{{reason}}` placeholder blank-safe in the handler by supplying an empty string when the reason is optional and absent.

- [ ] **Step 5: Add tests**

Cover:
- `OutboxMessageTypes.LeaveRequestCancelled` has wire value `leave_request_cancelled`.
- The no-op handler advertises `LeaveRequestCancelled` and completes.
- The notification template seeder inserts the three new template codes once and remains idempotent.

- [ ] **Step 6: Verify Task 5**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCancellationOutboxTests|FullyQualifiedName~NotificationTemplateSeederTests"
dotnet build ONEVO.sln
```

Expected result: outbox and notification-template tests pass.

---

### Task 6: Cancellation command handler

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Commands/CancelLeaveRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Commands/CancelLeaveRequestCommandValidator.cs`
- Create: `src/ONEVO.Application/Features/Leave/Cancellation/Commands/CancelLeaveRequestCommandHandler.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/CancelLeaveRequestCommandHandlerTests.cs`

**Interfaces:**
- Produces: `CancelLeaveRequestCommand`
- Produces: employee and HR cancellation behavior
- Consumes: classifier, allocation builder, repository, business-date resolver, outbox writer, notifications

- [ ] **Step 1: Add command and validator**

Create command:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Cancellation.Commands;

public sealed record CancelLeaveRequestCommand(
    Guid RequestId,
    string? Reason,
    DateOnly? EffectiveDate,
    string? ExpectedVersion)
    : IRequest<Result<CancelLeaveRequestResponse>>;
```

Create validator:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Leave.Cancellation.Commands;

public sealed class CancelLeaveRequestCommandValidator : AbstractValidator<CancelLeaveRequestCommand>
{
    public CancelLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(2000);
        RuleFor(x => x.ExpectedVersion).MaximumLength(32);
    }
}
```

- [ ] **Step 2: Implement handler authorization**

Handler dependencies:
- `ICurrentUser`
- `IEmployeeRepository` from the common/Core HR path used by Part 4 and Part 5
- `ILeaveCancellationRepository`
- `LeaveBusinessDateResolver`
- `LeaveCancellationClassifier`
- `LeaveRequestDayAllocationBuilder`
- `LeaveRequestDayCalculator`
- `ILeaveHolidayProvider`
- `ILeavePolicyRepository`
- `ILegalEntityRepository` if repository state cannot load legal entity directly
- `IOutboxWriter`
- `INotificationDispatcher`
- `IDateTimeProvider`
- `IOptions<LeaveCancellationOptions>`

Authorization logic:

```csharp
if (!_currentUser.IsAuthenticated)
    return Result<CancelLeaveRequestResponse>.Forbidden("Authentication required.");

var currentEmployee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
if (currentEmployee is null)
    return Result<CancelLeaveRequestResponse>.Forbidden("Employee profile was not found for the current user.");

var state = await _repository.GetStateAsync(_currentUser.TenantId, command.RequestId, ct);
if (state is null)
    return Result<CancelLeaveRequestResponse>.NotFound("Leave request was not found.");

var isOwner = state.Request.EmployeeId == currentEmployee.Id;
var isHrCancel = !isOwner;
if (isHrCancel && !_currentUser.HasPermission("leave:manage"))
    return Result<CancelLeaveRequestResponse>.Forbidden(LeaveCancellationMessages.NotOwner);
```

If the owner also has `leave:manage`, treat cancellation of their own request as employee cancellation unless the frontend later sends an explicit on-behalf flag. This keeps the reason requirement aligned to the action, not the user's role collection.

- [ ] **Step 3: Validate reason and classify**

```csharp
if (isHrCancel && string.IsNullOrWhiteSpace(command.Reason))
    return Result<CancelLeaveRequestResponse>.Failure(LeaveCancellationMessages.HrReasonRequired);

if (!isHrCancel && _options.RequireEmployeeReason && string.IsNullOrWhiteSpace(command.Reason))
    return Result<CancelLeaveRequestResponse>.Failure(LeaveCancellationMessages.EmployeeReasonRequired);

var businessDate = _businessDateResolver.Today(state.LegalEntity?.Timezone);
var classificationResult = _classifier.Classify(
    state.Request.Status,
    state.Request.StartDate,
    state.Request.EndDate,
    businessDate,
    command.EffectiveDate);
if (!classificationResult.IsSuccess)
    return Result<CancelLeaveRequestResponse>.Failure(
        classificationResult.Error!,
        classificationResult.StatusCode ?? 400);
```

- [ ] **Step 4: Ensure allocation rows exist**

Read allocations:

```csharp
var allocations = await _repository.ListAllocationsAsync(
    _currentUser.TenantId,
    state.Request.Id,
    ct);
```

If none exist, build and persist legacy/backfill allocation rows:
- Resolve working days from the active policy assignment for the employee legal entity and request year, matching Part 4.
- Load holiday dates through `ILeaveHolidayProvider`.
- Re-run `LeaveRequestDayCalculator`.
- Build rows with `LeaveRequestDayAllocationBuilder`.
- If calculated total does not match `state.Request.TotalDays`, return a conflict explaining that day allocation cannot be reconstructed for partial cancellation. Full pending/full approved cancellation may proceed using request totals, but partial approved cancellation must not guess.

Implementation shape:

```csharp
if (allocations.Count == 0)
{
    var backfilled = await BuildLegacyAllocationsAsync(state, ct);
    if (classification.Kind == LeaveCancellationKind.ApprovedPartial
        && backfilled.Sum(x => x.DayUnit) != state.Request.TotalDays)
    {
        return Result<CancelLeaveRequestResponse>.Conflict(
            "This request cannot be partially cancelled because its day allocation history is unavailable.");
    }

    await _repository.AddAllocationsAsync(backfilled, ct);
    allocations = backfilled;
}
```

- [ ] **Step 5: Apply pending-style cancellation**

For pending or information-requested:

```csharp
var releasedPendingDays = state.Request.PaidDays;
if (state.Entitlement is not null)
{
    state.Entitlement.PendingDays = Math.Max(0m, state.Entitlement.PendingDays - releasedPendingDays);
    state.Entitlement.UpdatedAt = now;
}

foreach (var approver in state.Approvers.Where(a =>
    a.Status is LeaveRequestApproverStatuses.Pending or LeaveRequestApproverStatuses.InformationRequested))
{
    approver.Status = LeaveRequestApproverStatuses.Cancelled;
    approver.DecidedAt = now;
}

foreach (var allocation in allocations.Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active))
{
    allocation.Status = LeaveRequestDayAllocationStatuses.Cancelled;
    allocation.CancelledAt = now;
}

state.Request.Status = LeaveRequestStatuses.Cancelled;
state.Request.CancellationReason = trimmedReason;
state.Request.PartialCancelEffectiveDate = null;
state.Request.UpdatedAt = now;
```

Do not add a `LeaveBalanceAudit` row for pending-style cancellation. It releases a reservation, not a used-balance adjustment.

- [ ] **Step 6: Apply approved full cancellation**

For approved requests whose business date is before or on `StartDate`:

```csharp
var restoredUsedDays = state.Request.PaidDays;
if (state.Entitlement is not null)
{
    state.Entitlement.UsedDays = Math.Max(0m, state.Entitlement.UsedDays - restoredUsedDays);
    state.Entitlement.UpdatedAt = now;
}

foreach (var allocation in allocations.Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active))
{
    allocation.Status = LeaveRequestDayAllocationStatuses.Cancelled;
    allocation.CancelledAt = now;
}

state.Request.Status = LeaveRequestStatuses.Cancelled;
state.Request.CancellationReason = trimmedReason;
state.Request.PartialCancelEffectiveDate = null;
state.Request.UpdatedAt = now;

await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
{
    Id = Guid.NewGuid(),
    TenantId = _currentUser.TenantId,
    EmployeeId = state.Request.EmployeeId,
    LeaveTypeId = state.Request.LeaveTypeId,
    ChangeType = LeaveBalanceChangeTypes.Adjustment,
    DaysChanged = restoredUsedDays,
    BalanceAfter = remainingAfterRestore,
    Reason = BuildAuditReason(isHrCancel, trimmedReason, isPartial: false),
    RelatedRequestId = state.Request.Id,
    CreatedAt = now,
    CreatedBy = _currentUser.UserId
}, ct);
```

- [ ] **Step 7: Apply approved partial cancellation**

For approved in-progress requests:

```csharp
var effectiveDate = classification.EffectiveDate!.Value;
var futureAllocations = allocations
    .Where(a => a.Status == LeaveRequestDayAllocationStatuses.Active && a.LeaveDate >= effectiveDate)
    .ToList();

var restoredUsedDays = futureAllocations.Sum(a => a.PaidUnit);
var affectedUnpaidDays = futureAllocations.Sum(a => a.UnpaidUnit);
if (restoredUsedDays <= 0m && affectedUnpaidDays <= 0m)
    return Result<CancelLeaveRequestResponse>.Conflict(LeaveCancellationMessages.NoRestorableDays);

if (state.Entitlement is not null)
{
    state.Entitlement.UsedDays = Math.Max(0m, state.Entitlement.UsedDays - restoredUsedDays);
    state.Entitlement.UpdatedAt = now;
}

foreach (var allocation in futureAllocations)
{
    allocation.Status = LeaveRequestDayAllocationStatuses.Cancelled;
    allocation.CancelledAt = now;
}

state.Request.Status = LeaveRequestStatuses.Cancelled;
state.Request.CancellationReason = trimmedReason;
state.Request.PartialCancelEffectiveDate = effectiveDate;
state.Request.UpdatedAt = now;
```

Write a `LeaveBalanceAudit` row only when `restoredUsedDays > 0`. If the partial cancellation only removes unpaid future days, enqueue side effects and mark the request cancelled but do not write a zero-day balance audit.

Keep `LeaveRequest.StartDate` and `LeaveRequest.EndDate` unchanged. `PartialCancelEffectiveDate` tells downstream readers which suffix was cancelled, while the original range remains auditable.

- [ ] **Step 8: Enqueue side effects and in-app notifications**

Before saving, enqueue:

```csharp
await _outbox.EnqueueAsync(
    OutboxMessageTypes.LeaveRequestCancelled,
    new LeaveRequestCancelledPayload(
        _currentUser.TenantId,
        state.Request.Id,
        state.Request.EmployeeId,
        state.Request.LeaveTypeId,
        state.LeaveTypeName,
        state.Request.StartDate,
        state.Request.EndDate,
        isPartialCancellation,
        state.Request.PartialCancelEffectiveDate,
        releasedPendingDays,
        restoredUsedDays,
        affectedUnpaidDays,
        _currentUser.UserId,
        currentEmployee.Id,
        isHrCancel,
        trimmedReason,
        now),
    _currentUser.TenantId,
    ct);
```

Notifications:
- HR cancellation: notify the employee user if `state.Employee.UserId` is set, using `leave_request_cancelled_by_hr`.
- Employee cancellation of an approved request: notify all distinct approver users, using `leave_request_cancelled_by_employee`.
- Employee cancellation of a pending or information-requested request: notify all distinct open approver users, using `leave_request_cancelled_by_employee`, so stale approval work items can be understood even before external approval-card cleanup exists.
- Partial approved cancellation: notify the employee when HR performs it and all approvers when the employee performs it, using `leave_request_partially_cancelled`.

Use placeholders:
- `employeeName`
- `cancelledByName`
- `leaveTypeName`
- `startDate`
- `endDate`
- `effectiveDate`
- `restoredDays`
- `reason`

Do not call external notification providers directly.

- [ ] **Step 9: Save and handle concurrency**

Set expected version before mutation if the caller supplied it:

```csharp
_repository.SetExpectedVersion(state.Request, command.ExpectedVersion);
```

Call a single repository save after request, approver, entitlement, audit, notification rows, and outbox rows have all been staged.

```csharp
try
{
    await _repository.SaveChangesAsync(ct);
}
catch (ConcurrencyConflictException)
{
    return Result<CancelLeaveRequestResponse>.Conflict(LeaveCancellationMessages.Concurrency);
}
```

Return `CancelLeaveRequestResponse` with remaining days calculated through `LeaveEntitlementMapper.Remaining(...)`, using the same effective carry-forward logic as the balance screens.

- [ ] **Step 10: Add command handler tests**

Cover:
- Employee can cancel own pending request and pending paid days are released.
- Employee can cancel own information-requested request and open approver row is marked cancelled.
- Pending cancellation does not create a `LeaveBalanceAudit` row.
- Employee cannot cancel another employee's request without `leave:manage`.
- HR can cancel another employee's request only with a reason.
- HR cancellation notifies the employee.
- Employee cancellation of approved leave notifies all approvers.
- Cancelled request returns exact already-cancelled message.
- Rejected request returns exact rejected message.
- Fully passed request returns exact period-passed message.
- Approved future cancellation restores all paid days and writes one adjustment audit.
- Approved in-progress cancellation restores only allocation rows on or after effective date.
- Partial cancellation of unpaid-only future days does not write a zero-day balance audit but still enqueues side effects.
- Stale expected version maps to exact concurrency message.
- Outbox message is enqueued once with `LeaveRequestCancelled` and the correct partial/full payload fields.

- [ ] **Step 11: Verify Task 6**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~CancelLeaveRequestCommandHandlerTests"
dotnet build ONEVO.sln
```

Expected result: cancellation command tests pass and the handler compiles.

---

### Task 7: HTTP authorization filter and cancellation endpoint

**Files:**
- Create: `src/ONEVO.Api/Filters/RequireAnyPermissionAttribute.cs`
- Edit: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveRequestsController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Cancellation/LeaveCancellationControllerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Api/Filters/RequireAnyPermissionAttributeTests.cs`

**Interfaces:**
- Produces: `POST /api/v1/leave/requests/{requestId}/cancel`
- Produces: either-permission authorization support
- Consumes: `CancelLeaveRequestCommand`

- [ ] **Step 1: Add any-permission filter**

Create `RequireAnyPermissionAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAnyPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        _permissions = permissions;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (_permissions.Any(currentUser.HasPermission))
            return;

        context.Result = new ObjectResult(new
        {
            type = "https://onevo.com/errors/forbidden",
            title = "Forbidden",
            status = 403,
            detail = $"One of these permissions is required: {string.Join(", ", _permissions)}."
        })
        { StatusCode = 403 };
    }
}
```

Keep `RequirePermissionAttribute` unchanged for single-permission endpoints.

- [ ] **Step 2: Add controller action**

Edit `LeaveRequestsController.cs`:

```csharp
using ONEVO.Application.Features.Leave.Cancellation.Commands;
using ONEVO.Application.Features.Leave.Cancellation.DTOs.Requests;
```

Add:

```csharp
[HttpPost("{requestId:guid}/cancel")]
[RequireAnyPermission("leave:read-own", "leave:manage")]
public async Task<IActionResult> Cancel(
    Guid requestId,
    [FromBody] CancelLeaveRequestRequest request,
    CancellationToken ct)
{
    var result = await _mediator.Send(new CancelLeaveRequestCommand(
        requestId,
        request.Reason,
        request.EffectiveDate,
        request.ExpectedVersion), ct);

    return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
}
```

- [ ] **Step 3: Add controller/filter tests**

Cover:
- `RequireAnyPermissionAttribute` allows `leave:read-own`.
- `RequireAnyPermissionAttribute` allows `leave:manage`.
- It rejects an authenticated user with neither permission.
- It returns unauthorized when unauthenticated.
- Controller sends `CancelLeaveRequestCommand` with route id, reason, effective date, and expected version.
- Controller returns `Ok` on success and Problem on failure.

- [ ] **Step 4: Verify Task 7**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCancellationControllerTests|FullyQualifiedName~RequireAnyPermissionAttributeTests"
dotnet build ONEVO.sln
```

Expected result: endpoint and filter tests pass.

---

### Task 8: Integration tests, live smoke, and documentation sync

**Files:**
- Add integration tests under the existing API test project if Leave endpoint tests exist
- Edit: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Edit: `docs/superpowers/plans/next/SUMMARY.md`
- Edit: `docs/superpowers/plans/SUMMARY.md`
- Add Postman/API docs only if this repo already documents Leave endpoints alongside previous Leave endpoints

**Interfaces:**
- Verifies: permissions, tenant isolation, status transitions, balance restoration, allocation rows, outbox creation, notification creation, optimistic concurrency
- Produces: updated plan index status

- [ ] **Step 1: Add integration coverage**

Cover:
- Employee can cancel own pending request through `POST /api/v1/leave/requests/{id}/cancel`.
- Pending cancellation releases `PendingDays` but does not create a balance audit row.
- Employee cannot cancel another employee's request without `leave:manage`.
- HR can cancel another employee's pending request only with a reason.
- Approved future cancellation restores `UsedDays`, creates one adjustment audit, and enqueues `leave_request_cancelled`.
- Approved in-progress cancellation marks only future allocation rows cancelled and restores only their paid units.
- Rejected request returns the exact rejected-request message.
- Already cancelled request returns the exact already-cancelled message.
- Fully passed request returns the exact passed-period message.
- Concurrent approve/cancel or cancel/cancel race returns the exact refresh-and-try-again message for the stale writer.
- Tenant A cannot cancel Tenant B's leave request.
- In-app notification rows are created for HR cancellation and employee cancellation of approved leave.

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Leave.Cancellation|FullyQualifiedName~Leave.Request"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet build ONEVO.sln
```

Expected result: cancellation/request unit tests pass, architecture tests pass, build passes.

- [ ] **Step 3: Run live dev-DB smoke**

Against the existing dev smoke tenants from `DevSmokeTestTenantSeeder`, verify:
- Create a pending request, then cancel it as the employee. Confirm request status `cancelled`, `PendingDays` returns to the previous value, no balance audit row is written, and an outbox row exists.
- Create a request, approve it through Phase 5, then cancel before `StartDate`. Confirm `UsedDays` decreases by `PaidDays`, one adjustment audit row exists, and a cancellation outbox row exists.
- Create or seed an in-progress approved request with day allocations, cancel from the business date, and confirm only allocation rows from the effective date through `EndDate` are cancelled.
- HR cancellation without reason returns the exact product error.
- A fully passed request returns the exact product error.
- A stale `ExpectedVersion` returns the exact concurrency error.

Record exact commands and results in the phase summary. If Docker/Testcontainers is unavailable, record that the live smoke is pending for the same environmental reason noted in Parts 3 and 4.

- [ ] **Step 4: Update summaries**

Update:
- `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: mark Phase 6 as written in full, or executed if implementation has been completed.
- `docs/superpowers/plans/next/SUMMARY.md`: add `part-6-cancellation.md` to the written-in-full list.
- `docs/superpowers/plans/SUMMARY.md`: change the Leave Management row from Parts 1-4 executed and Part 5 written to Parts 1-4 executed and Parts 5-6 written, unless Part 5 has also been executed.

---

## Execution Handoff

Start with Task 1 and keep each task green before moving to the next one. The safest implementation order is:

1. Cancellation options, vocabularies, day-allocation schema, and request concurrency metadata.
2. Pure helper tests for business date, classification, and day allocation.
3. Persist allocation rows during request submission.
4. Cancellation DTOs and EF repository.
5. Outbox payload, registered no-op handler, and notification templates.
6. Cancellation command handler.
7. Any-permission filter and HTTP endpoint.
8. Integration/live smoke/docs sync.

Key behavior to preserve:
- Pending and information-requested cancellation releases pending paid-day reservations only.
- Approved cancellation restores used paid days only.
- Partial cancellation restores only stored future paid allocation units.
- Unpaid days never alter entitlement balance.
- HR reason is required; employee reason stays config-driven.
- Business date comes from legal-entity timezone or validated fallback config.
- Side effects are outbox messages saved in the same transaction as the state change.
- Every new outbox type has a registered handler.
- `xmin` concurrency maps stale writes to the exact product refresh message.

Before marking complete, run the focused unit suite, architecture suite, build, and live dev-DB smoke or explicitly document the environmental blocker.
