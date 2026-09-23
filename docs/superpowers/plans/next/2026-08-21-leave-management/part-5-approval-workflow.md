# Leave Management - Part 5: Approval Workflow (Phase 5 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend approval workflow for Screens 6 and 8: pending approvals, HR request list, approval detail, approve, reject, request more information, employee information response, and bulk approve/reject with partial success.

**Architecture:** Part 5 keeps approval decisions transactional and side effects asynchronous. The decision handlers update `LeaveRequest`, `LeaveRequestApprover`, `LeaveEntitlement`, and `LeaveBalanceAudit` in one unit of work, enqueue leave side-effect outbox messages in that same unit of work, and write in-app notification rows through the existing notification dispatcher before saving. Approval-mode rules live in a pure evaluator so `any_one`, `all_must_approve`, and `in_order` can be tested without EF or HTTP.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, transactional outbox, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product context from `C:\HR\leave-management-complete.md`; depends on `docs/superpowers/plans/next/2026-08-21-leave-management/part-4-request-submission.md`.

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat attached documents as context only. The active user request is this Part 5 backend plan.
- Phase 4 must be executed first. This plan consumes the Phase 4 request submission shape: pending `LeaveRequest` rows, `LeaveRequestApprover` rows, pending paid-day reservation in `LeaveEntitlement.PendingDays`, request conflict snapshots, request calendar conflict provider, and `ILeaveApproverResolver`.
- Use existing permission codes. `leave:approve` gates approval actions and pending-approval views. `leave:read` gates HR all-request views. `leave:read-own` gates employee information responses.
- Do not cut balance on submit. Approval moves `PaidDays` from pending to used: `PendingDays -= PaidDays`, `UsedDays += PaidDays`. Rejection releases the reservation: `PendingDays -= PaidDays`, `UsedDays` unchanged.
- Do not change balance for unpaid days. Unpaid days only travel in side-effect payloads for payroll handling.
- Approval side effects must use the transactional outbox. Calendar confirmation/removal, Workforce Presence, payroll deduction flags, external chat/push/email, and team notifications must not be called directly from controllers or handlers.
- Every outbox message type added in this part must have a registered handler. If a real calendar/payroll/workforce/chat adapter is not available, register an explicit no-op handler so the outbox processor does not retry and fail.
- In-app notifications may be written in the same DbContext transaction with `INotificationDispatcher.SendTemplatedAsync`, because the dispatcher only adds database notification rows and does not call an external provider.
- Use `Result<T>` and `Result` from `ONEVO.Application.Common.Models`, matching the existing Leave slice. Do not introduce exception-driven control flow in new handlers.
- Reject reason is required. Approve comment is optional. Request-info question is required. Employee information response is required.
- Self-approval is blocked unless `LeaveApprovalOptions.AllowSelfApproval` is enabled through configuration. The current persisted policy schema has no self-approval field, so do not hard-code an exception.
- Current conflict re-check is returned in the approval detail and decision responses. In this part it warns; it does not block approval unless an existing validation rule already blocks the request.
- Additive schema is allowed in this part for request-info messages because the current leave schema has no place to store the employee's answer and resume the paused step.
- Keep closed vocabularies as string constants. Do not add C# enums or PostgreSQL enum/check constraints.

---

### Task 1: Approval options, information-request schema, vocabularies, and notification templates

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/Options/LeaveApprovalOptions.cs`
- Edit: `src/ONEVO.Domain/Features/Leave/Common/LeaveVocabularies.cs`
- Create: `src/ONEVO.Domain/Features/Leave/Request/Entities/LeaveRequestInfoMessage.cs`
- Edit: `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs`
- Edit: `src/ONEVO.Infrastructure/Persistence/Configurations/Leave/LeaveRequestConfiguration.cs`
- Add migration: `src/ONEVO.Infrastructure/Migrations/<timestamp>_AddLeaveRequestInfoMessages.cs`
- Edit: `src/ONEVO.Api/appsettings.json`
- Edit: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalOptionsTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalVocabularyTests.cs`

**Interfaces:**
- Produces: `LeaveApprovalOptions`
- Produces: `LeaveRequestStatuses.InformationRequested`
- Produces: `LeaveRequestApproverStatuses.InformationRequested`
- Produces: `LeaveRequestInfoMessage`
- Consumes later: paused request-info workflow and approval self-approval setting

- [ ] **Step 1: Add approval options tests**

Create `LeaveApprovalOptionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Leave.Approval.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveApprovals()
    {
        LeaveApprovalOptions.SectionName.Should().Be("Leave:Approvals");
    }

    [Fact]
    public void AllowSelfApproval_DefaultsToFalse()
    {
        var options = new LeaveApprovalOptions();

        options.AllowSelfApproval.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Add approval options**

Create `LeaveApprovalOptions.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Approval.Options;

public sealed class LeaveApprovalOptions
{
    public const string SectionName = "Leave:Approvals";

    public bool AllowSelfApproval { get; init; }
}
```

Register options in the same startup location used for `LeaveRequestOptions`:

```csharp
services
    .AddOptions<LeaveApprovalOptions>()
    .Bind(configuration.GetSection(LeaveApprovalOptions.SectionName))
    .ValidateOnStart();
```

Add the configuration section:

```json
{
  "Leave": {
    "Approvals": {
      "AllowSelfApproval": false
    }
  }
}
```

- [ ] **Step 3: Extend leave statuses**

Add to `LeaveVocabularies.cs`:

```csharp
public static class LeaveRequestApproverStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Skipped = "skipped";
    public const string InformationRequested = "information_requested";
}

public static class LeaveRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string InformationRequested = "information_requested";
}
```

Create `LeaveApprovalVocabularyTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalVocabularyTests
{
    [Fact]
    public void InformationRequestedStatus_UsesStableWireValue()
    {
        LeaveRequestStatuses.InformationRequested.Should().Be("information_requested");
        LeaveRequestApproverStatuses.InformationRequested.Should().Be("information_requested");
    }
}
```

- [ ] **Step 4: Add request-info message entity**

Create `LeaveRequestInfoMessage.cs`:

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestInfoMessage : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid SenderEmployeeId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Add to `ApplicationDbContext`:

```csharp
public DbSet<LeaveRequestInfoMessage> LeaveRequestInfoMessages => Set<LeaveRequestInfoMessage>();
```

Add configuration:

```csharp
public class LeaveRequestInfoMessageConfiguration : IEntityTypeConfiguration<LeaveRequestInfoMessage>
{
    public void Configure(EntityTypeBuilder<LeaveRequestInfoMessage> builder)
    {
        builder.ToTable("leave_request_info_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.CreatedAt })
            .HasDatabaseName("ix_leave_request_info_messages_tenant_request_created");

        builder.HasOne<LeaveRequest>()
            .WithMany()
            .HasForeignKey(x => x.LeaveRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Generate a migration:

```powershell
dotnet ef migrations add AddLeaveRequestInfoMessages --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```

Inspect the generated migration and ensure it creates `leave_request_info_messages` with tenant RLS policy matching the other leave tables.

- [ ] **Step 5: Add notification templates**

Append templates in `NotificationTemplateSeeder`:

```csharp
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_approved",
    InAppTitleTemplate = "Leave approved",
    InAppBodyTemplate = "{{leaveTypeName}} from {{startDate}} to {{endDate}} was approved."
},
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_rejected",
    InAppTitleTemplate = "Leave rejected",
    InAppBodyTemplate = "{{leaveTypeName}} from {{startDate}} to {{endDate}} was rejected. {{reason}}"
},
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_information_requested",
    InAppTitleTemplate = "More information requested",
    InAppBodyTemplate = "{{approverName}} requested more information for {{leaveTypeName}} from {{startDate}} to {{endDate}}."
},
new()
{
    Id = Guid.NewGuid(), Code = "leave_request_next_approval_required",
    InAppTitleTemplate = "Leave approval required",
    InAppBodyTemplate = "{{employeeName}} requested {{leaveTypeName}} from {{startDate}} to {{endDate}}."
}
```

- [ ] **Step 6: Verify Task 1**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalOptionsTests|FullyQualifiedName~LeaveApprovalVocabularyTests"
dotnet build ONEVO.sln
```

Expected result: tests pass and the migration compiles.

---

### Task 2: Approval DTOs, repository contract, and EF repository

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/DTOs/Requests/LeaveApprovalRequests.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/DTOs/Responses/LeaveApprovalResponses.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Mappers/LeaveApprovalMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/RepositoryInterfaces/ILeaveApprovalRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/LeaveApprovalRepository.cs`
- Edit: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalMapperTests.cs`

**Interfaces:**
- Produces: approval request/response contracts
- Produces: `ILeaveApprovalRepository`
- Consumes later: approval decision handlers, approval list/detail queries, bulk commands

- [ ] **Step 1: Add request contracts**

Create `LeaveApprovalRequests.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Approval.DTOs.Requests;

public sealed record ApproveLeaveRequestRequest(string? Comment);

public sealed record RejectLeaveRequestRequest(string Reason);

public sealed record RequestLeaveInformationRequest(string Question);

public sealed record RespondLeaveInformationRequest(string Message, IReadOnlyList<Guid>? FileRecordIds);

public sealed record BulkApproveLeaveRequestsRequest(IReadOnlyList<Guid> RequestIds, string? Comment);

public sealed record BulkRejectLeaveRequestsRequest(IReadOnlyList<Guid> RequestIds, string Reason);
```

- [ ] **Step 2: Add response contracts**

Create `LeaveApprovalResponses.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

public sealed record LeaveApprovalDecisionResponse(
    Guid RequestId,
    string Status,
    string CurrentApproverState,
    decimal PaidDaysMovedFromPending,
    decimal UnpaidDays,
    decimal RemainingDays,
    IReadOnlyList<LeaveApprovalWarningResponse> CurrentWarnings);

public sealed record LeaveApprovalWarningResponse(string Code, string Message);

public sealed record LeavePendingApprovalListItemResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record LeaveRequestAllListItemResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record LeaveApprovalDetailResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    decimal PaidDays,
    decimal UnpaidDays,
    string Status,
    string? Reason,
    IReadOnlyList<LeaveApprovalApproverResponse> Approvers,
    IReadOnlyList<LeaveApprovalInfoMessageResponse> InfoMessages,
    string? SubmissionConflictSnapshotJson,
    IReadOnlyList<LeaveApprovalWarningResponse> CurrentWarnings,
    decimal RemainingDays);

public sealed record LeaveApprovalApproverResponse(
    Guid ApproverEmployeeId,
    int SequenceOrder,
    string Status,
    string? Comment,
    Guid? DelegatedFromApproverId,
    DateTimeOffset? DecidedAt);

public sealed record LeaveApprovalInfoMessageResponse(
    Guid SenderEmployeeId,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record LeaveApprovalBulkResultResponse(
    IReadOnlyList<LeaveApprovalBulkItemResponse> Items,
    int SuccessCount,
    int FailureCount);

public sealed record LeaveApprovalBulkItemResponse(
    Guid RequestId,
    bool Success,
    string? Status,
    string? Error);
```

- [ ] **Step 3: Add repository contract**

Create `ILeaveApprovalRepository.cs`:

```csharp
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;

public interface ILeaveApprovalRepository
{
    Task<LeaveApprovalState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default);

    Task<IReadOnlyList<LeavePendingApprovalListRow>> ListPendingForApproverAsync(
        Guid tenantId,
        Guid approverEmployeeId,
        LeaveApprovalListFilter filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequestAllListRow>> ListAllAsync(
        Guid tenantId,
        LeaveRequestAllListFilter filter,
        CancellationToken ct = default);

    Task AddInfoMessageAsync(LeaveRequestInfoMessage message, CancellationToken ct = default);

    Task AddBalanceAuditAsync(LeaveBalanceAudit audit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record LeaveApprovalState(
    LeaveRequest Request,
    LeaveEntitlement? Entitlement,
    Employee Employee,
    Employee? RequesterEmployee,
    string LeaveTypeName,
    string LeaveTypeCode,
    string ApprovalMode,
    IReadOnlyList<LeaveRequestApprover> Approvers,
    IReadOnlyList<LeaveRequestInfoMessage> InfoMessages);

public sealed record LeaveApprovalListFilter(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate);

public sealed record LeaveRequestAllListFilter(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate);

public sealed record LeavePendingApprovalListRow(
    LeaveRequest Request,
    string EmployeeName,
    string LeaveTypeName,
    string LeaveTypeCode);

public sealed record LeaveRequestAllListRow(
    LeaveRequest Request,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    string LeaveTypeName);
```

- [ ] **Step 4: Implement repository queries**

Create `LeaveApprovalRepository.cs` using `ApplicationDbContext`. The state query must load tracked request, tracked entitlement, approvers, info messages, employee, requester, leave type, and approval mode:

```csharp
public async Task<LeaveApprovalState?> GetStateAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
{
    var request = await _db.LeaveRequests
        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == requestId, ct);

    if (request is null)
    {
        return null;
    }

    var employee = await _db.Employees
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct);

    if (employee is null)
    {
        return null;
    }

    var requesterEmployee = request.SubmittedOnBehalfOfBy is null
        ? null
        : await _db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SubmittedOnBehalfOfBy.Value, ct);

    var leaveType = await _db.LeaveTypes
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.LeaveTypeId, ct);

    if (leaveType is null)
    {
        return null;
    }

    var entitlement = await _db.LeaveEntitlements
        .SingleOrDefaultAsync(x =>
            x.TenantId == tenantId &&
            x.EmployeeId == request.EmployeeId &&
            x.LeaveTypeId == request.LeaveTypeId &&
            x.Year == request.StartDate.Year,
            ct);

    var approvers = await _db.LeaveRequestApprovers
        .Where(x => x.TenantId == tenantId && x.LeaveRequestId == request.Id)
        .OrderBy(x => x.SequenceOrder)
        .ThenBy(x => x.Id)
        .ToListAsync(ct);

    var messages = await _db.LeaveRequestInfoMessages
        .AsNoTracking()
        .Where(x => x.TenantId == tenantId && x.LeaveRequestId == request.Id)
        .OrderBy(x => x.CreatedAt)
        .ToListAsync(ct);

    var approvalMode = await ResolveApprovalModeAsync(tenantId, employee.LegalEntityId, request.StartDate.Year, ct);

    return new LeaveApprovalState(
        request,
        entitlement,
        employee,
        requesterEmployee,
        leaveType.Name,
        leaveType.Code,
        approvalMode,
        approvers,
        messages);
}
```

Use the existing Part 2/3 policy aggregate repository for `ResolveApprovalModeAsync`; if no active policy is found, return `LeaveApprovalModes.AnyOne` only in tests. In production code, missing policy should make the handler return a `409` conflict.

- [ ] **Step 5: Implement list queries**

`ListPendingForApproverAsync` must return only requests where the current employee is an actionable approver:

```csharp
var query =
    from approver in _db.LeaveRequestApprovers.AsNoTracking()
    join request in _db.LeaveRequests.AsNoTracking()
        on approver.LeaveRequestId equals request.Id
    join employee in _db.Employees.AsNoTracking()
        on request.EmployeeId equals employee.Id
    join leaveType in _db.LeaveTypes.AsNoTracking()
        on request.LeaveTypeId equals leaveType.Id
    where approver.TenantId == tenantId
        && request.TenantId == tenantId
        && employee.TenantId == tenantId
        && leaveType.TenantId == tenantId
        && approver.ApproverEmployeeId == approverEmployeeId
        && approver.Status == LeaveRequestApproverStatuses.Pending
        && request.Status == LeaveRequestStatuses.Pending
    select new { request, employee, leaveType, approver };
```

Apply search, department, leave type, and date filters before projecting. The handler will apply in-order actionability through the pure evaluator after rows are loaded with their sibling approvers.

- [ ] **Step 6: Add mapper tests**

Create `LeaveApprovalMapperTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Approval.Mappers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalMapperTests
{
    [Fact]
    public void CalculateRemaining_AfterApproval_KeepsPendingReservedBalanceStable()
    {
        var remaining = LeaveApprovalMapper.CalculateRemaining(
            totalDays: 20m,
            carriedForwardDays: 0m,
            usedDays: 8m,
            pendingDays: 0m);

        remaining.Should().Be(12m);
    }
}
```

Create mapper helper:

```csharp
namespace ONEVO.Application.Features.Leave.Approval.Mappers;

public static class LeaveApprovalMapper
{
    public static decimal CalculateRemaining(
        decimal totalDays,
        decimal carriedForwardDays,
        decimal usedDays,
        decimal pendingDays)
    {
        return (totalDays + carriedForwardDays) - usedDays - pendingDays;
    }
}
```

- [ ] **Step 7: Register repository**

```csharp
services.AddScoped<ILeaveApprovalRepository, LeaveApprovalRepository>();
```

- [ ] **Step 8: Verify Task 2**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalMapperTests"
dotnet build ONEVO.sln
```

Expected result: mapper test passes and repository compiles.

---

### Task 3: Pure approval-mode evaluator

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/Helpers/LeaveApprovalModeEvaluator.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalModeEvaluatorTests.cs`

**Interfaces:**
- Produces: `LeaveApprovalModeEvaluator.ApplyApproval(string approvalMode, IReadOnlyList<ApprovalModeRow> rows, Guid currentApproverId)`
- Produces: `LeaveApprovalModeEvaluator.IsActionable(string approvalMode, IReadOnlyList<ApprovalModeRow> rows, Guid approverEmployeeId)`
- Consumes later: approval decision handlers and pending approval list filtering

- [ ] **Step 1: Write evaluator tests**

Create `LeaveApprovalModeEvaluatorTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalModeEvaluatorTests
{
    [Fact]
    public void ApplyApproval_AnyOne_CompletesRequestAndSkipsOtherPendingApprovers()
    {
        var currentApproverId = Guid.NewGuid();
        var otherApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.AnyOne,
            [
                new ApprovalModeRow(currentApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(otherApproverId, 1, LeaveRequestApproverStatuses.Pending)
            ],
            currentApproverId);

        result.RequestCompleted.Should().BeTrue();
        result.ApproversToSkip.Should().ContainSingle().Which.Should().Be(otherApproverId);
    }

    [Fact]
    public void ApplyApproval_AllMustApprove_WaitsForRemainingPendingApprover()
    {
        var currentApproverId = Guid.NewGuid();
        var otherApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.AllMustApprove,
            [
                new ApprovalModeRow(currentApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(otherApproverId, 1, LeaveRequestApproverStatuses.Pending)
            ],
            currentApproverId);

        result.RequestCompleted.Should().BeFalse();
        result.NextApproverIds.Should().ContainSingle().Which.Should().Be(otherApproverId);
    }

    [Fact]
    public void ApplyApproval_InOrder_OnlyAdvancesToNextSequence()
    {
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();

        var result = LeaveApprovalModeEvaluator.ApplyApproval(
            LeaveApprovalModes.InOrder,
            [
                new ApprovalModeRow(firstApproverId, 1, LeaveRequestApproverStatuses.Approved),
                new ApprovalModeRow(secondApproverId, 2, LeaveRequestApproverStatuses.Pending)
            ],
            firstApproverId);

        result.RequestCompleted.Should().BeFalse();
        result.NextApproverIds.Should().ContainSingle().Which.Should().Be(secondApproverId);
    }

    [Fact]
    public void IsActionable_InOrder_ReturnsFalseForLaterSequence()
    {
        var firstApproverId = Guid.NewGuid();
        var secondApproverId = Guid.NewGuid();

        var actionable = LeaveApprovalModeEvaluator.IsActionable(
            LeaveApprovalModes.InOrder,
            [
                new ApprovalModeRow(firstApproverId, 1, LeaveRequestApproverStatuses.Pending),
                new ApprovalModeRow(secondApproverId, 2, LeaveRequestApproverStatuses.Pending)
            ],
            secondApproverId);

        actionable.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Implement evaluator**

Create `LeaveApprovalModeEvaluator.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Approval.Helpers;

public static class LeaveApprovalModeEvaluator
{
    public static ApprovalModeDecision ApplyApproval(
        string approvalMode,
        IReadOnlyList<ApprovalModeRow> rows,
        Guid currentApproverId)
    {
        if (approvalMode == LeaveApprovalModes.AnyOne)
        {
            var toSkip = rows
                .Where(row => row.ApproverEmployeeId != currentApproverId &&
                              row.Status == LeaveRequestApproverStatuses.Pending)
                .Select(row => row.ApproverEmployeeId)
                .ToList();

            return new ApprovalModeDecision(true, toSkip, []);
        }

        var remaining = rows
            .Where(row => row.Status == LeaveRequestApproverStatuses.Pending)
            .OrderBy(row => row.SequenceOrder)
            .ToList();

        if (remaining.Count == 0)
        {
            return new ApprovalModeDecision(true, [], []);
        }

        if (approvalMode == LeaveApprovalModes.InOrder)
        {
            var nextSequence = remaining.Min(row => row.SequenceOrder);
            return new ApprovalModeDecision(
                false,
                [],
                remaining.Where(row => row.SequenceOrder == nextSequence).Select(row => row.ApproverEmployeeId).ToList());
        }

        return new ApprovalModeDecision(false, [], remaining.Select(row => row.ApproverEmployeeId).ToList());
    }

    public static bool IsActionable(
        string approvalMode,
        IReadOnlyList<ApprovalModeRow> rows,
        Guid approverEmployeeId)
    {
        var row = rows.SingleOrDefault(x => x.ApproverEmployeeId == approverEmployeeId);
        if (row is null || row.Status != LeaveRequestApproverStatuses.Pending)
        {
            return false;
        }

        if (approvalMode != LeaveApprovalModes.InOrder)
        {
            return true;
        }

        var firstPendingSequence = rows
            .Where(x => x.Status == LeaveRequestApproverStatuses.Pending)
            .Min(x => x.SequenceOrder);

        return row.SequenceOrder == firstPendingSequence;
    }
}

public sealed record ApprovalModeRow(Guid ApproverEmployeeId, int SequenceOrder, string Status);

public sealed record ApprovalModeDecision(
    bool RequestCompleted,
    IReadOnlyList<Guid> ApproversToSkip,
    IReadOnlyList<Guid> NextApproverIds);
```

- [ ] **Step 3: Verify Task 3**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalModeEvaluatorTests"
```

Expected result: evaluator tests pass.

---

### Task 4: Leave approval side-effect outbox payloads and handlers

**Files:**
- Edit: `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/OutboxHandlers/LeaveApprovalOutboxPayloads.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/OutboxHandlers/NoOpLeaveApprovalSideEffectOutboxHandler.cs`
- Edit: `src/ONEVO.Application/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalOutboxRegistrationTests.cs`

**Interfaces:**
- Produces: leave side-effect outbox message type constants
- Produces: retry-safe no-op handler registrations
- Consumes later: approve/reject/request-info handlers

- [ ] **Step 1: Add message type constants**

Add to `OutboxMessageTypes`:

```csharp
public const string LeaveRequestApproved = "leave_request_approved";
public const string LeaveRequestRejected = "leave_request_rejected";
public const string LeaveInformationRequested = "leave_information_requested";
```

- [ ] **Step 2: Add payload records**

Create `LeaveApprovalOutboxPayloads.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Approval.OutboxHandlers;

public sealed record LeaveRequestApprovedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PaidDays,
    decimal UnpaidDays,
    Guid ApprovedByEmployeeId);

public sealed record LeaveRequestRejectedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PaidDays,
    decimal UnpaidDays,
    Guid RejectedByEmployeeId,
    string Reason);

public sealed record LeaveInformationRequestedPayload(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid RequestedByEmployeeId,
    string Question);
```

- [ ] **Step 3: Add no-op side-effect handler**

Create `NoOpLeaveApprovalSideEffectOutboxHandler.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Leave.Approval.OutboxHandlers;

public sealed class NoOpLeaveApprovalSideEffectOutboxHandler : IOutboxMessageHandler
{
    public NoOpLeaveApprovalSideEffectOutboxHandler(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Register handlers**

In `DependencyInjection.cs`:

```csharp
using ONEVO.Application.Features.Leave.Approval.OutboxHandlers;
```

Register:

```csharp
services.AddScoped<IOutboxMessageHandler>(_ =>
    new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveRequestApproved));
services.AddScoped<IOutboxMessageHandler>(_ =>
    new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveRequestRejected));
services.AddScoped<IOutboxMessageHandler>(_ =>
    new NoOpLeaveApprovalSideEffectOutboxHandler(OutboxMessageTypes.LeaveInformationRequested));
```

- [ ] **Step 5: Verify Task 4**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalOutboxRegistrationTests"
dotnet build ONEVO.sln
```

Expected result: every new leave outbox type has a registered handler, even while real Calendar, Workforce, Payroll, and Chat adapters are deferred.

---

### Task 5: Approve, reject, request-info, and respond-info handlers

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/ApproveLeaveRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/RejectLeaveRequestCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/RequestLeaveInformationCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/RespondLeaveInformationCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/LeaveApprovalDecisionService.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalDecisionServiceTests.cs`

**Interfaces:**
- Produces: decision handlers for approval actions
- Consumes: `ILeaveApprovalRepository`, `IEmployeeRepository`, `IOutboxWriter`, `INotificationDispatcher`, `IFileRecordRepository`, `ILeaveRequestConflictProvider`, `LeaveApprovalModeEvaluator`, `LeaveApprovalOptions`

- [ ] **Step 1: Add command records**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed record ApproveLeaveRequestCommand(Guid RequestId, string? Comment)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed record RejectLeaveRequestCommand(Guid RequestId, string Reason)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed record RequestLeaveInformationCommand(Guid RequestId, string Question)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed record RespondLeaveInformationCommand(Guid RequestId, string Message, IReadOnlyList<Guid> FileRecordIds)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;
```

- [ ] **Step 2: Add validator behavior**

Create validators in the same folder:

```csharp
using FluentValidation;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed class ApproveLeaveRequestCommandValidator : AbstractValidator<ApproveLeaveRequestCommand>
{
    public ApproveLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class RejectLeaveRequestCommandValidator : AbstractValidator<RejectLeaveRequestCommand>
{
    public RejectLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RequestLeaveInformationCommandValidator : AbstractValidator<RequestLeaveInformationCommand>
{
    public RequestLeaveInformationCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RespondLeaveInformationCommandValidator : AbstractValidator<RespondLeaveInformationCommand>
{
    public RespondLeaveInformationCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}
```

- [ ] **Step 3: Write decision service tests**

Create `LeaveApprovalDecisionServiceTests.cs` with these named tests:

```csharp
[Fact]
public async Task ApproveAsync_WhenRequestAlreadyFinal_ReturnsConflict()
{
    // Arrange a state with request.Status = approved.
    // Assert Result.Conflict with "This request has already been approved or rejected".
}

[Fact]
public async Task ApproveAsync_WhenCurrentEmployeeIsNotAssigned_ReturnsForbidden()
{
    // Arrange a pending request with approvers not matching the current employee.
    // Assert Result.Forbidden with "This request is not assigned to you".
}

[Fact]
public async Task ApproveAsync_WhenSelfApprovalDisabledAndApproverIsEmployee_ReturnsConflict()
{
    // Arrange request.EmployeeId equals current approver employee id and AllowSelfApproval = false.
    // Assert the exact self-approval error from Screen 8.
}

[Fact]
public async Task ApproveAsync_WhenAnyOneApproves_MovesPaidDaysFromPendingToUsed()
{
    // Arrange request.PaidDays = 3, entitlement.PendingDays = 3, entitlement.UsedDays = 5.
    // Assert PendingDays = 0, UsedDays = 8, request.Status = approved, and one deduction audit is added.
}

[Fact]
public async Task RejectAsync_ReleasesPendingPaidDaysWithoutUsedDeduction()
{
    // Arrange request.PaidDays = 2, entitlement.PendingDays = 2, entitlement.UsedDays = 4.
    // Assert PendingDays = 0, UsedDays = 4, request.Status = rejected, and no deduction audit is added.
}

[Fact]
public async Task RequestInfoAsync_PausesRequestAndKeepsPendingBalanceReserved()
{
    // Arrange pending request and current actionable approver.
    // Assert request.Status = information_requested, approver.Status = information_requested, PendingDays unchanged.
}

[Fact]
public async Task RespondInfoAsync_ResumesRequestForSameApprover()
{
    // Arrange request.Status = information_requested and approver.Status = information_requested.
    // Assert request.Status = pending, approver.Status = pending, and an info message is added.
}
```

Use the repo's actual mock style. The comments above are test intent; implement each test with concrete objects and assertions.

- [ ] **Step 4: Implement shared decision service**

Create `LeaveApprovalDecisionService.cs` and have command handlers delegate to it. Core approval logic:

```csharp
public async Task<Result<LeaveApprovalDecisionResponse>> ApproveAsync(
    Guid requestId,
    string? comment,
    CancellationToken ct)
{
    var context = await LoadContextAsync(requestId, ct);
    if (!context.Result.IsSuccess)
    {
        return context.Result;
    }

    var state = context.State;
    var approver = FindActionableApprover(state, context.CurrentEmployee.Id);
    if (approver.Result is not null)
    {
        return approver.Result;
    }

    if (!_options.AllowSelfApproval && state.Request.EmployeeId == context.CurrentEmployee.Id)
    {
        return Result<LeaveApprovalDecisionResponse>.Conflict("You cannot approve your own leave request.");
    }

    if (state.Entitlement is null)
    {
        return Result<LeaveApprovalDecisionResponse>.Conflict("Employee's balance has changed since submission. Current balance: 0 days.");
    }

    var currentRemaining = LeaveApprovalMapper.CalculateRemaining(
        state.Entitlement.TotalDays,
        state.Entitlement.CarriedForwardDays,
        state.Entitlement.UsedDays,
        state.Entitlement.PendingDays);

    if (state.Request.PaidDays > 0m && (state.Entitlement.PendingDays < state.Request.PaidDays || currentRemaining < 0m))
    {
        return Result<LeaveApprovalDecisionResponse>.Conflict(
            $"Employee's balance has changed since submission. Current balance: {currentRemaining} days.");
    }

    approver.Row.Status = LeaveRequestApproverStatuses.Approved;
    approver.Row.Comment = comment;
    approver.Row.DecidedAt = _clock.UtcNow;

    var rows = state.Approvers
        .Select(row => new ApprovalModeRow(row.ApproverEmployeeId, row.SequenceOrder, row.Status))
        .ToList();

    var decision = LeaveApprovalModeEvaluator.ApplyApproval(state.ApprovalMode, rows, context.CurrentEmployee.Id);
    foreach (var skippedApproverId in decision.ApproversToSkip)
    {
        var skipped = state.Approvers.Single(row => row.ApproverEmployeeId == skippedApproverId);
        skipped.Status = LeaveRequestApproverStatuses.Skipped;
        skipped.DecidedAt = _clock.UtcNow;
    }

    if (decision.RequestCompleted)
    {
        state.Request.Status = LeaveRequestStatuses.Approved;
        state.Request.ApprovedBy = context.CurrentEmployee.Id;
        state.Request.ApprovedAt = _clock.UtcNow;
        state.Request.UpdatedAt = _clock.UtcNow;

        state.Entitlement.PendingDays -= state.Request.PaidDays;
        state.Entitlement.UsedDays += state.Request.PaidDays;
        state.Entitlement.UpdatedAt = _clock.UtcNow;

        var balanceAfter = LeaveApprovalMapper.CalculateRemaining(
            state.Entitlement.TotalDays,
            state.Entitlement.CarriedForwardDays,
            state.Entitlement.UsedDays,
            state.Entitlement.PendingDays);

        if (state.Request.PaidDays > 0m)
        {
            await _repository.AddBalanceAuditAsync(new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(),
                TenantId = context.TenantId,
                EmployeeId = state.Request.EmployeeId,
                LeaveTypeId = state.Request.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Deduction,
                DaysChanged = -state.Request.PaidDays,
                BalanceAfter = balanceAfter,
                Reason = "Leave request approved",
                RelatedRequestId = state.Request.Id,
                CreatedAt = _clock.UtcNow,
                CreatedBy = context.CurrentUserId
            }, ct);
        }

        await EnqueueApprovedSideEffectsAsync(context, state, ct);
        await NotifyEmployeeAsync(context, state, "leave_request_approved", null, ct);
    }
    else
    {
        await NotifyNextApproversAsync(context, state, decision.NextApproverIds, ct);
    }

    await _repository.SaveChangesAsync(ct);
    return Result<LeaveApprovalDecisionResponse>.Success(MapDecisionResponse(state));
}
```

Reject logic:

```csharp
state.Request.Status = LeaveRequestStatuses.Rejected;
state.Request.UpdatedAt = _clock.UtcNow;
approver.Row.Status = LeaveRequestApproverStatuses.Rejected;
approver.Row.Comment = reason;
approver.Row.DecidedAt = _clock.UtcNow;

foreach (var pending in state.Approvers.Where(row => row.Status == LeaveRequestApproverStatuses.Pending))
{
    pending.Status = LeaveRequestApproverStatuses.Skipped;
    pending.DecidedAt = _clock.UtcNow;
}

if (state.Entitlement is not null)
{
    state.Entitlement.PendingDays -= state.Request.PaidDays;
    state.Entitlement.UpdatedAt = _clock.UtcNow;
}

await EnqueueRejectedSideEffectsAsync(context, state, reason, ct);
await NotifyEmployeeAsync(context, state, "leave_request_rejected", reason, ct);
await _repository.SaveChangesAsync(ct);
```

Request-info logic:

```csharp
state.Request.Status = LeaveRequestStatuses.InformationRequested;
state.Request.UpdatedAt = _clock.UtcNow;
approver.Row.Status = LeaveRequestApproverStatuses.InformationRequested;
approver.Row.Comment = question;
approver.Row.DecidedAt = null;

await _repository.AddInfoMessageAsync(new LeaveRequestInfoMessage
{
    Id = Guid.NewGuid(),
    TenantId = context.TenantId,
    LeaveRequestId = state.Request.Id,
    SenderEmployeeId = context.CurrentEmployee.Id,
    Message = question,
    CreatedAt = _clock.UtcNow
}, ct);

await EnqueueInformationRequestedSideEffectsAsync(context, state, question, ct);
await NotifyEmployeeAsync(context, state, "leave_request_information_requested", question, ct);
await _repository.SaveChangesAsync(ct);
```

Respond-info logic:

```csharp
if (state.Request.EmployeeId != context.CurrentEmployee.Id)
{
    return Result<LeaveApprovalDecisionResponse>.Forbidden("This request does not belong to you.");
}

if (state.Request.Status != LeaveRequestStatuses.InformationRequested)
{
    return Result<LeaveApprovalDecisionResponse>.Conflict("This leave request is not waiting for more information.");
}

var pausedApprover = state.Approvers.SingleOrDefault(row =>
    row.Status == LeaveRequestApproverStatuses.InformationRequested);

if (pausedApprover is null)
{
    return Result<LeaveApprovalDecisionResponse>.Conflict("No paused approver was found for this request.");
}

await ValidateFileRecordsAsync(context.TenantId, fileRecordIds, ct);
await AddRequestDocumentsAsync(context.TenantId, state.Request.Id, fileRecordIds, ct);

state.Request.Status = LeaveRequestStatuses.Pending;
state.Request.UpdatedAt = _clock.UtcNow;
pausedApprover.Status = LeaveRequestApproverStatuses.Pending;

await _repository.AddInfoMessageAsync(new LeaveRequestInfoMessage
{
    Id = Guid.NewGuid(),
    TenantId = context.TenantId,
    LeaveRequestId = state.Request.Id,
    SenderEmployeeId = context.CurrentEmployee.Id,
    Message = message,
    CreatedAt = _clock.UtcNow
}, ct);

await NotifyNextApproversAsync(context, state, [pausedApprover.ApproverEmployeeId], ct);
await _repository.SaveChangesAsync(ct);
```

- [ ] **Step 5: Implement command handlers**

Each handler injects `LeaveApprovalDecisionService` and delegates:

```csharp
public sealed class ApproveLeaveRequestCommandHandler
    : IRequestHandler<ApproveLeaveRequestCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;

    public ApproveLeaveRequestCommandHandler(LeaveApprovalDecisionService service)
    {
        _service = service;
    }

    public Task<Result<LeaveApprovalDecisionResponse>> Handle(ApproveLeaveRequestCommand request, CancellationToken ct)
    {
        return _service.ApproveAsync(request.RequestId, request.Comment, ct);
    }
}
```

Repeat the same explicit delegation shape for reject, request-info, and respond-info.

- [ ] **Step 6: Verify Task 5**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalDecisionServiceTests"
dotnet build ONEVO.sln
```

Expected result: decision tests pass and handlers compile.

---

### Task 6: Pending approvals, HR all requests, and approval detail queries

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/Queries/ListPendingLeaveApprovalsQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Queries/ListAllLeaveRequestsQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Queries/GetLeaveApprovalDetailQuery.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalQueryHandlerTests.cs`

**Interfaces:**
- Produces: pending approval queue
- Produces: HR all requests list
- Produces: approval detail with submission snapshot and current warnings
- Consumes: repository, current user, employee repository, conflict provider, approval evaluator

- [ ] **Step 1: Add query records**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Approval.Queries;

public sealed record ListPendingLeaveApprovalsQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    DateOnly? FromDate,
    DateOnly? ToDate) : IRequest<Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>>;

public sealed record ListAllLeaveRequestsQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LeaveTypeId,
    string? Status,
    DateOnly? FromDate,
    DateOnly? ToDate) : IRequest<Result<IReadOnlyList<LeaveRequestAllListItemResponse>>>;

public sealed record GetLeaveApprovalDetailQuery(Guid RequestId)
    : IRequest<Result<LeaveApprovalDetailResponse>>;
```

- [ ] **Step 2: Implement pending approvals handler**

```csharp
public async Task<Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>> Handle(
    ListPendingLeaveApprovalsQuery query,
    CancellationToken ct)
{
    if (_currentUser.UserId is null)
    {
        return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Forbidden("Authentication required.");
    }

    var tenantId = _currentUser.TenantId;
    var approverEmployee = await _employeeRepository.GetByUserIdAsync(tenantId, _currentUser.UserId.Value, ct);
    if (approverEmployee is null)
    {
        return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.NotFound("Employee profile was not found for the current user.");
    }

    var rows = await _repository.ListPendingForApproverAsync(
        tenantId,
        approverEmployee.Id,
        new LeaveApprovalListFilter(query.Search, query.DepartmentId, query.LeaveTypeId, query.FromDate, query.ToDate),
        ct);

    var actionable = new List<LeavePendingApprovalListItemResponse>();
    foreach (var row in rows)
    {
        var state = await _repository.GetStateAsync(tenantId, row.Request.Id, ct);
        if (state is null)
        {
            continue;
        }

        var modeRows = state.Approvers
            .Select(x => new ApprovalModeRow(x.ApproverEmployeeId, x.SequenceOrder, x.Status))
            .ToList();

        if (!LeaveApprovalModeEvaluator.IsActionable(state.ApprovalMode, modeRows, approverEmployee.Id))
        {
            continue;
        }

        actionable.Add(LeaveApprovalMapper.ToPendingListItem(row));
    }

    return Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Success(actionable);
}
```

- [ ] **Step 3: Implement detail handler**

`GetLeaveApprovalDetailQueryHandler` must:
- Verify current user has an employee profile.
- Load state by tenant and request id.
- Require the current employee to be an assigned approver for the approval detail endpoint.
- Re-run current conflict provider from Part 4.
- Compute remaining days from current entitlement.
- Return stored `ConflictSnapshotJson` as submitted snapshot and current warnings separately.

```csharp
var currentConflicts = await _conflictProvider.ListConflictsAsync(
    tenantId,
    state.Request.EmployeeId,
    state.Request.StartDate,
    state.Request.EndDate,
    ct);

var warnings = currentConflicts
    .Select(conflict => new LeaveApprovalWarningResponse("current_conflict", conflict.Title))
    .ToList();
```

- [ ] **Step 4: Verify Task 6**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveApprovalQueryHandlerTests"
```

Expected result: list/detail query tests pass.

---

### Task 7: Bulk approve/reject commands and HTTP controller

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/BulkApproveLeaveRequestsCommand.cs`
- Create: `src/ONEVO.Application/Features/Leave/Approval/Commands/BulkRejectLeaveRequestsCommand.cs`
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveApprovalsController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/BulkLeaveApprovalCommandHandlerTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Approval/LeaveApprovalsControllerPermissionTests.cs`

**Interfaces:**
- Produces: approval endpoints
- Produces: bulk partial-success responses
- Consumes: decision commands and query handlers

- [ ] **Step 1: Add bulk command records and handlers**

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed record BulkApproveLeaveRequestsCommand(IReadOnlyList<Guid> RequestIds, string? Comment)
    : IRequest<Result<LeaveApprovalBulkResultResponse>>;

public sealed record BulkRejectLeaveRequestsCommand(IReadOnlyList<Guid> RequestIds, string Reason)
    : IRequest<Result<LeaveApprovalBulkResultResponse>>;
```

Bulk approve handler:

```csharp
public async Task<Result<LeaveApprovalBulkResultResponse>> Handle(
    BulkApproveLeaveRequestsCommand command,
    CancellationToken ct)
{
    var items = new List<LeaveApprovalBulkItemResponse>();
    foreach (var requestId in command.RequestIds.Distinct())
    {
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(requestId, command.Comment), ct);
        items.Add(result.IsSuccess
            ? new LeaveApprovalBulkItemResponse(requestId, true, result.Value!.Status, null)
            : new LeaveApprovalBulkItemResponse(requestId, false, null, result.Error));
    }

    return Result<LeaveApprovalBulkResultResponse>.Success(new LeaveApprovalBulkResultResponse(
        items,
        items.Count(x => x.Success),
        items.Count(x => !x.Success)));
}
```

Bulk reject is the same structure but delegates to `RejectLeaveRequestCommand`. Repeat the code instead of hiding behavior in a generic helper.

- [ ] **Step 2: Add controller**

Create `LeaveApprovalsController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.DTOs.Requests;
using ONEVO.Application.Features.Leave.Approval.Queries;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/requests")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveApprovalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveApprovalsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("pending-approvals")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> PendingApprovals(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListPendingLeaveApprovalsQuery(search, departmentId, leaveTypeId, fromDate, toDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("all")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> All(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListAllLeaveRequestsQuery(search, departmentId, leaveTypeId, status, fromDate, toDate), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("{requestId:guid}/approval")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> ApprovalDetail(Guid requestId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeaveApprovalDetailQuery(requestId), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/approve")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] ApproveLeaveRequestRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ApproveLeaveRequestCommand(requestId, request.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/reject")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] RejectLeaveRequestRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RejectLeaveRequestCommand(requestId, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/request-info")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> RequestInfo(Guid requestId, [FromBody] RequestLeaveInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RequestLeaveInformationCommand(requestId, request.Question), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("{requestId:guid}/respond-info")]
    [RequirePermission("leave:read-own")]
    public async Task<IActionResult> RespondInfo(Guid requestId, [FromBody] RespondLeaveInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RespondLeaveInformationCommand(requestId, request.Message, request.FileRecordIds ?? []), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk-approve")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> BulkApprove([FromBody] BulkApproveLeaveRequestsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkApproveLeaveRequestsCommand(request.RequestIds, request.Comment), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk-reject")]
    [RequirePermission("leave:approve")]
    public async Task<IActionResult> BulkReject([FromBody] BulkRejectLeaveRequestsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkRejectLeaveRequestsCommand(request.RequestIds, request.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 3: Verify Task 7**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~BulkLeaveApprovalCommandHandlerTests|FullyQualifiedName~LeaveApprovalsControllerPermissionTests"
dotnet build ONEVO.sln
```

Expected result: bulk tests pass, controller permission tests pass, build passes.

---

### Task 8: Integration tests, live smoke, and documentation sync

**Files:**
- Add integration tests under the existing API test project if Leave endpoint tests exist
- Edit: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Edit: `docs/superpowers/plans/next/SUMMARY.md`
- Edit: `docs/superpowers/plans/SUMMARY.md`
- Add Postman/API docs only if this repo already documents Leave endpoints alongside previous Leave endpoints

**Interfaces:**
- Verifies: permissions, tenant isolation, approval transitions, balance movement, outbox creation, notification creation, partial bulk success
- Produces: updated plan index status

- [ ] **Step 1: Add integration coverage**

Cover:
- Approver can see only assigned actionable pending approvals.
- Later sequence approver in `in_order` mode cannot approve before the first sequence.
- Approving in `any_one` mode approves the request and marks other pending approvers skipped.
- Approving final step moves paid days from pending to used and writes one `Deduction` audit row.
- Rejecting releases pending paid days and does not write a deduction audit row.
- Request-info pauses the request and keeps pending paid days reserved.
- Employee respond-info resumes the request and reopens the same approver row.
- Bulk approve returns partial success when one request is no longer pending.
- Tenant A approver cannot process Tenant B request.
- Outbox rows are written for approve, reject, and request-info transitions.

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Leave.Approval"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet build ONEVO.sln
```

Expected result: unit tests pass, architecture tests pass, build passes.

- [ ] **Step 3: Run live dev-DB smoke**

Against the existing dev smoke tenants from `DevSmokeTestTenantSeeder`, verify:
- A pending request created by Part 4 appears in `GET /api/v1/leave/requests/pending-approvals`.
- The assigned approver can open `GET /api/v1/leave/requests/{requestId}/approval`.
- Approve returns `approved`, the request row is approved, `PendingDays` decreases, `UsedDays` increases, and a deduction audit row exists.
- Reject returns `rejected`, `PendingDays` decreases, and no deduction audit row exists.
- Request-info returns `information_requested`, employee respond-info returns `pending`, and the same approver can approve afterwards.
- Bulk approve returns mixed success/failure when one request is stale.

Record exact commands and results in the phase summary. If Docker/Testcontainers is unavailable, record that the live smoke is pending for the same environmental reason noted in Part 3.

- [ ] **Step 4: Update summaries**

Update:
- `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: mark Phase 5 as written, or executed if implementation has been completed.
- `docs/superpowers/plans/next/SUMMARY.md`: add `part-5-approval-workflow.md` to the written-in-full list.
- `docs/superpowers/plans/SUMMARY.md`: change the Leave Management row from Parts 1-3 executed and Part 4 written to Parts 1-3 executed, Parts 4-5 written.

---

## Execution Handoff

Start with Task 1 and keep each task green before moving to the next one. The safest implementation order is:

1. Options, statuses, request-info message schema, notification templates.
2. DTOs and approval repository.
3. Pure approval-mode evaluator.
4. Outbox payloads and no-op side-effect handlers.
5. Decision commands and handlers.
6. Pending/all/detail queries.
7. Bulk commands and controller.
8. Integration/live smoke/docs sync.

Key behavior to preserve:
- Approval moves only paid days from pending to used.
- Rejection releases only paid pending days.
- Unpaid days do not change entitlement balances.
- Side effects are outbox messages saved in the same transaction as the decision.
- Every outbox message type has a registered handler.
- Request-info pauses and resumes through stored info messages.
- Self-approval is configurable and disabled unless config enables it.
- Approval modes are evaluated by the pure helper, not hand-coded inside controllers.

Before marking complete, run the focused unit suite, architecture suite, build, and live dev-DB smoke or explicitly document the environmental blocker.
