# Leave Management — Part 8: Balance Audit Surfacing + CSV Exports + Year-End Job (Phase 8 of 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the `LeaveBalanceAudit` rows that Phases 3/5/6 already write on every accrual/deduction/carry-forward/forfeiture/adjustment (spec §2.5), add CSV export for both the audit trail and entitlement-generation results (spec Screen 3), and add the automatic year-end carry-forward/forfeiture trigger — reusing the calculation logic Phase 3 already shipped rather than reimplementing it.

**Architecture:** A new read-only `ILeaveBalanceAuditRepository`/`EfLeaveBalanceAuditRepository` (mirrors `EfLeaveEntitlementRepository`'s employee/leave-type join pattern) behind a new `LeaveBalanceAuditController`. CSV export reuses `GetBulkOnboardingTemplateQueryHandler`'s exact `(byte[] Content, string ContentType, string FileName)` + `File(...)` pattern — the only CSV precedent in this codebase. The year-end job is a `BackgroundService` matching `ActivityDailySummaryJob`'s daily-check-then-act shape, but for the multi-tenant loop it follows `BulkOnboardingBatchProcessor`'s real pattern: `IWritableTenantContext.SetAdminMode()` to enumerate tenants, then `ITenantContextSwitcher.SwitchToTenantAsync(...)` per tenant before touching that tenant's RLS-protected `Leave*` tables — **this job does not call `IMediator.Send(GenerateEntitlementsCommand)`**, because that handler reads `ICurrentUser.TenantId`/`UserId`, which are HTTP-context-bound and unset in a background job's DI scope (confirmed by reading `GenerateEntitlementsCommandHandler.cs` and cross-checking that neither `ActivityDailySummaryJob` nor `BulkOnboardingBatchProcessor` ever calls `IMediator.Send` for this exact reason). Instead it calls `LeaveEntitlementPlanner.PlanAsync` + `ILeaveEntitlementRepository.AddGeneratedAsync` directly — the same two calls the command handler makes, just with `tenantId` from the loop variable instead of `ICurrentUser` and `CreatedBy = null` on the audit rows (system-generated, not user-attributed).

**Tech Stack:** ASP.NET Core, EF Core (PostgreSQL), MediatR CQRS (for the two new read endpoints only — not the job), xUnit + Moq.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product behaviour: `C:\HR\leave-management-complete.md` §2.5, §4 (carry-forward/forfeiture table), Screen 3 ("Results... Download CSV").

## Global Constraints

- Leave module only. Do not modify `GenerateEntitlementsCommandHandler`, `LeaveEntitlementPlanner`, or `LeaveEntitlementCalculator` — this phase **reuses** them from a new caller, it doesn't change their behavior. If a bug is found in them while building this phase, stop and flag it rather than fixing it inline (out of this plan's scope).
- No new calculation logic. Carry-forward/forfeiture math already lives in `LeaveEntitlementCalculator` (verified: it already returns `CarriedForwardDays` and `ForfeitedDays` per the spec §4 table). This plan's job wires the *trigger*, not the *math*.
- The year-end job must never crash across tenants: one tenant's failure (missing policy, `UniqueConstraintConflictException` from a re-run, etc.) is logged and skipped, not thrown — matching `ActivityDailySummaryJob`'s per-iteration try/catch shape, applied per-tenant here instead of per-employee.
- CSV files: UTF-8 bytes, no BOM (matches `GetBulkOnboardingTemplateQueryHandler.BuildCsv()` exactly — `Encoding.UTF8.GetBytes(...)`, not `Encoding.UTF8` with `preamble: true`).

---

### Task 1: `ILeaveBalanceAuditRepository` + `EfLeaveBalanceAuditRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/RepositoryInterfaces/ILeaveBalanceAuditRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/BalanceAudit/EfLeaveBalanceAuditRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register)

- [ ] **Step 1: Repository interface + row/filter records**

```csharp
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

namespace ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

public interface ILeaveBalanceAuditRepository
{
    Task<IReadOnlyList<LeaveBalanceAuditRow>> ListRowsAsync(
        Guid tenantId, LeaveBalanceAuditListFilter filter, CancellationToken ct = default);
}

public record LeaveBalanceAuditListFilter(
    Guid? EmployeeId,
    Guid? LeaveTypeId,
    string? ChangeType,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    int PageSize);

public record LeaveBalanceAuditRow(
    LeaveBalanceAudit Audit,
    string EmployeeNumber,
    string EmployeeName,
    string LeaveTypeName,
    string LeaveTypeCode);
```

- [ ] **Step 2: EF implementation — mirrors `EfLeaveEntitlementRepository.ListRowsAsync`'s join shape**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.BalanceAudit;

public class EfLeaveBalanceAuditRepository : ILeaveBalanceAuditRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveBalanceAuditRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveBalanceAuditRow>> ListRowsAsync(
        Guid tenantId, LeaveBalanceAuditListFilter filter, CancellationToken ct = default)
    {
        var query =
            from audit in _db.LeaveBalanceAudits.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on audit.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on audit.LeaveTypeId equals leaveType.Id
            where audit.TenantId == tenantId
            select new { audit, employee, leaveType };

        if (filter.EmployeeId is { } employeeId)
            query = query.Where(x => x.audit.EmployeeId == employeeId);
        if (filter.LeaveTypeId is { } leaveTypeId)
            query = query.Where(x => x.audit.LeaveTypeId == leaveTypeId);
        if (!string.IsNullOrWhiteSpace(filter.ChangeType))
            query = query.Where(x => x.audit.ChangeType == filter.ChangeType);
        if (filter.FromDate is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.audit.CreatedAt >= fromUtc);
        }
        if (filter.ToDate is { } to)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.audit.CreatedAt < toUtc);
        }

        var rows = await query
            .OrderByDescending(x => x.audit.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return rows.Select(x => new LeaveBalanceAuditRow(
            x.audit, x.employee.EmployeeNumber,
            $"{x.employee.FirstName} {x.employee.LastName}".Trim(),
            x.leaveType.Name, x.leaveType.Code)).ToList();
    }
}
```

- [ ] **Step 3: Register in DI**

Add next to the existing Leave repository registrations in `DependencyInjection.cs`:
```csharp
        services.AddScoped<ILeaveBalanceAuditRepository, EfLeaveBalanceAuditRepository>();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/BalanceAudit/RepositoryInterfaces src/ONEVO.Infrastructure/Persistence/Repositories/Leave/BalanceAudit src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(leave): add ILeaveBalanceAuditRepository and EF implementation"
```

---

### Task 2: `ListBalanceAuditQuery`

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/DTOs/Responses/LeaveBalanceAuditResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/Mappers/LeaveBalanceAuditMapper.cs`
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/Queries/ListBalanceAudit/ListBalanceAuditQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/Queries/ListBalanceAudit/ListBalanceAuditQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/ListBalanceAuditQueryHandlerTests.cs`

- [ ] **Step 1: Response DTO + mapper**

```csharp
// LeaveBalanceAuditResponse.cs
namespace ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

public record LeaveBalanceAuditResponse(
    Guid Id,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    string ChangeType,
    decimal DaysChanged,
    decimal BalanceAfter,
    string? Reason,
    Guid? RelatedRequestId,
    DateTimeOffset CreatedAt);
```

```csharp
// LeaveBalanceAuditMapper.cs
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Mappers;

public static class LeaveBalanceAuditMapper
{
    public static LeaveBalanceAuditResponse ToResponse(LeaveBalanceAuditRow row) => new(
        row.Audit.Id, row.Audit.EmployeeId, row.EmployeeNumber, row.EmployeeName,
        row.Audit.LeaveTypeId, row.LeaveTypeName, row.LeaveTypeCode,
        row.Audit.ChangeType, row.Audit.DaysChanged, row.Audit.BalanceAfter,
        row.Audit.Reason, row.Audit.RelatedRequestId, row.Audit.CreatedAt);
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.BalanceAudit;

public class ListBalanceAuditQueryHandlerTests
{
    private readonly Mock<ILeaveBalanceAuditRepository> _repoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public ListBalanceAuditQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
    }

    [Fact]
    public async Task Handle_ReturnsRowsFromRepository()
    {
        var row = new LeaveBalanceAuditRow(
            new LeaveBalanceAudit
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = Guid.NewGuid(), LeaveTypeId = Guid.NewGuid(),
                ChangeType = LeaveBalanceChangeTypes.Deduction, DaysChanged = -3m, BalanceAfter = 7m,
                Reason = "Leave approved", CreatedAt = DateTimeOffset.UtcNow
            },
            "EMP001", "Priya Kumar", "Annual Leave", "ANNUAL");

        _repoMock.Setup(r => r.ListRowsAsync(_tenantId, It.IsAny<LeaveBalanceAuditListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);

        var handler = new ListBalanceAuditQueryHandler(_repoMock.Object, _currentUserMock.Object);
        var result = await handler.Handle(
            new ListBalanceAuditQuery(EmployeeId: null, LeaveTypeId: null, ChangeType: null, FromDate: null, ToDate: null, Page: 1, PageSize: 25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Priya Kumar", result.Value![0].EmployeeName);
        Assert.Equal(-3m, result.Value[0].DaysChanged);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ListBalanceAuditQueryHandlerTests`
Expected: FAIL — types don't exist.

- [ ] **Step 4: Implement query + handler**

```csharp
// ListBalanceAuditQuery.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

public record ListBalanceAuditQuery(
    Guid? EmployeeId,
    Guid? LeaveTypeId,
    string? ChangeType,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    int PageSize) : IRequest<Result<IReadOnlyList<LeaveBalanceAuditResponse>>>;
```

```csharp
// ListBalanceAuditQueryHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.Mappers;
using ONEVO.Application.Features.Leave.BalanceAudit.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

public class ListBalanceAuditQueryHandler : IRequestHandler<ListBalanceAuditQuery, Result<IReadOnlyList<LeaveBalanceAuditResponse>>>
{
    private readonly ILeaveBalanceAuditRepository _audits;
    private readonly ICurrentUser _currentUser;

    public ListBalanceAuditQueryHandler(ILeaveBalanceAuditRepository audits, ICurrentUser currentUser)
    {
        _audits = audits;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceAuditResponse>>> Handle(ListBalanceAuditQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveBalanceAuditResponse>>.Forbidden("Authentication required.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 200 ? 25 : request.PageSize;

        var rows = await _audits.ListRowsAsync(
            _currentUser.TenantId,
            new LeaveBalanceAuditListFilter(
                request.EmployeeId, request.LeaveTypeId, request.ChangeType, request.FromDate, request.ToDate, page, pageSize),
            ct);

        return Result<IReadOnlyList<LeaveBalanceAuditResponse>>.Success(
            rows.Select(LeaveBalanceAuditMapper.ToResponse).ToList());
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~ListBalanceAuditQueryHandlerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/BalanceAudit tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/ListBalanceAuditQueryHandlerTests.cs
git commit -m "feat(leave): add ListBalanceAuditQuery"
```

---

### Task 3: `LeaveBalanceAuditController`

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalanceAuditController.cs`
- Test: `tests/ONEVO.Tests.Integration/Features/Leave/LeaveBalanceAuditEndpointTests.cs`

Read-only for both HR (`leave:read`/`leave:manage`) and, per spec §2.5 being referenced from the employee's own balance history — this task only wires the HR-facing list; an employee-scoped `leave:read-own` variant filtering to their own `EmployeeId` is not added here since no Screen 4 UI element calls for it yet (the balance card's "History" table is served by the existing entitlement/request endpoints, not this audit trail) — do not add it speculatively.

- [ ] **Step 1: Controller**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.BalanceAudit.Queries.ListBalanceAudit;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/balance-audit")]
[Authorize(Policy = "TenantPolicy")]
public class LeaveBalanceAuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveBalanceAuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Append-only balance audit trail. Filterable by employee, leave type, change
    /// type, and date range.</summary>
    [HttpGet]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? changeType,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListBalanceAuditQuery(employeeId, leaveTypeId, changeType, fromDate, toDate, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

- [ ] **Step 2: Write the integration test**

Follow `LeaveEntitlementsAndBalancesIntegrationTests.cs`'s fixture setup exactly (it already exercises `leave:read` against a seeded acme tenant — read that file first to confirm the fixture class name and helper method, since Part 1's integration test task flagged the same fixture-naming uncertainty and this repo's actual test file is the authoritative answer now that it exists):

```csharp
using System.Net;
using System.Net.Http.Json;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Integration.Features.Leave;

public class LeaveBalanceAuditEndpointTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory _factory;

    public LeaveBalanceAuditEndpointTests(TenantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_AsHrManager_ReturnsOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync(role: "HR Manager", tenant: "acme");

        var response = await client.GetAsync("/api/v1/leave/balance-audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<LeaveBalanceAuditResponse>>();
        Assert.NotNull(body);
    }
}
```

*(As with Part 1's Task 8, confirm the exact fixture class/method names against `LeaveEntitlementsAndBalancesIntegrationTests.cs` before finalizing this file — that test already exists in this repo and is the real, current answer, not a placeholder.)*

- [ ] **Step 3: Run and fix forward until it passes**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~LeaveBalanceAuditEndpointTests`
Expected: PASS once fixture names match the real file.

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalanceAuditController.cs tests/ONEVO.Tests.Integration/Features/Leave/LeaveBalanceAuditEndpointTests.cs
git commit -m "feat(leave): add GET /api/v1/leave/balance-audit endpoint"
```

---

### Task 4: CSV export — Balance Audit

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/Helpers/LeaveBalanceAuditCsvBuilder.cs`
- Create: `src/ONEVO.Application/Features/Leave/BalanceAudit/DTOs/Responses/LeaveExportFile.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalanceAuditController.cs` (add `Export`)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/LeaveBalanceAuditCsvBuilderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.BalanceAudit.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.BalanceAudit;

public class LeaveBalanceAuditCsvBuilderTests
{
    [Fact]
    public void Build_ProducesHeaderAndOneRowPerAudit()
    {
        var rows = new List<LeaveBalanceAuditResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "EMP001", "Priya Kumar", Guid.NewGuid(), "Annual Leave", "ANNUAL",
                "deduction", -3m, 7m, "Leave approved", Guid.NewGuid(), new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero))
        };

        var file = LeaveBalanceAuditCsvBuilder.Build(rows);

        var text = Encoding.UTF8.GetString(file.Content);
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Employee Number,Employee Name,Leave Type,Change Type,Days Changed,Balance After,Reason,Date", lines[0]);
        Assert.Contains("EMP001,Priya Kumar,Annual Leave,deduction,-3,7,Leave approved,2026-04-10", lines[1]);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("leave-balance-audit.csv", file.FileName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveBalanceAuditCsvBuilderTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
// LeaveExportFile.cs — shared shape for every CSV export in this phase, matching
// BulkOnboardingTemplateFile's (Content, ContentType, FileName) precedent.
namespace ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

public record LeaveExportFile(byte[] Content, string ContentType, string FileName);
```

```csharp
// LeaveBalanceAuditCsvBuilder.cs
using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.BalanceAudit.Helpers;

public static class LeaveBalanceAuditCsvBuilder
{
    public static LeaveExportFile Build(IReadOnlyList<LeaveBalanceAuditResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Employee Number,Employee Name,Leave Type,Change Type,Days Changed,Balance After,Reason,Date\n");

        foreach (var row in rows)
        {
            sb.Append(Csv(row.EmployeeNumber)).Append(',')
              .Append(Csv(row.EmployeeName)).Append(',')
              .Append(Csv(row.LeaveTypeName)).Append(',')
              .Append(Csv(row.ChangeType)).Append(',')
              .Append(row.DaysChanged).Append(',')
              .Append(row.BalanceAfter).Append(',')
              .Append(Csv(row.Reason ?? "")).Append(',')
              .Append(row.CreatedAt.ToString("yyyy-MM-dd"))
              .Append('\n');
        }

        return new LeaveExportFile(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "leave-balance-audit.csv");
    }

    // Quotes a field only when it contains a comma, quote, or newline — minimal CSV escaping,
    // matching the level of rigor GetBulkOnboardingTemplateQueryHandler.BuildCsv() uses (none,
    // since its fields are controlled labels) but extended here since Reason/EmployeeName are
    // free text and can legitimately contain commas.
    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveBalanceAuditCsvBuilderTests`
Expected: PASS.

- [ ] **Step 5: Controller action**

```csharp
    /// <summary>CSV export of the audit trail, same filters as List. No pagination — capped
    /// at 5000 rows to keep the export bounded.</summary>
    [HttpGet("export")]
    [RequirePermission("leave:read")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? changeType,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListBalanceAuditQuery(employeeId, leaveTypeId, changeType, fromDate, toDate, Page: 1, PageSize: 5000), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var file = Application.Features.Leave.BalanceAudit.Helpers.LeaveBalanceAuditCsvBuilder.Build(result.Value!);
        return File(file.Content, file.ContentType, file.FileName);
    }
```

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/BalanceAudit/Helpers src/ONEVO.Application/Features/Leave/BalanceAudit/DTOs/Responses/LeaveExportFile.cs src/ONEVO.Api/Controllers/Tenant/Leave/LeaveBalanceAuditController.cs tests/ONEVO.Tests.Unit/Features/Leave/BalanceAudit/LeaveBalanceAuditCsvBuilderTests.cs
git commit -m "feat(leave): add CSV export for balance audit"
```

---

### Task 5: CSV export — Entitlement generation preview (spec Screen 3 "Download CSV")

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementGenerationCsvBuilder.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveEntitlementsController.cs` (add `PreviewGenerateExport`)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementGenerationCsvBuilderTests.cs`

Reuses `PreviewGenerateEntitlementsQuery` (already shipped, Phase 3) — no new planning logic. The "Download CSV" spec behavior (Screen 3, "Results: Successful count · Skipped ... Download CSV") is satisfied by exporting the same preview the HR admin already reviewed before clicking Generate, not a second re-run after generation — this avoids a race where the CSV could reflect a different plan than what was actually persisted.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class LeaveEntitlementGenerationCsvBuilderTests
{
    [Fact]
    public void Build_ListsCreatedLinesThenSkippedRows()
    {
        var preview = new LeaveEntitlementGenerationPreviewResponse(
            2027, 1, 1,
            Lines: [new LeaveEntitlementGenerationLineResponse(
                Guid.NewGuid(), "EMP001", "Priya Kumar", Guid.NewGuid(), "Annual Leave",
                20m, 5m, 25m, false, 0m, null, null)],
            Skipped: [new LeaveEntitlementGenerationSkipResponse(Guid.NewGuid(), "Alex Roy", "No leave policy assigned to their legal entity")]);

        var file = LeaveEntitlementGenerationCsvBuilder.Build(preview);

        var text = Encoding.UTF8.GetString(file.Content);
        Assert.Contains("EMP001,Priya Kumar,Annual Leave,20,5,25", text);
        Assert.Contains("Alex Roy,,Skipped,No leave policy assigned to their legal entity", text);
        Assert.Equal("leave-entitlement-generation-2027.csv", file.FileName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveEntitlementGenerationCsvBuilderTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

public static class LeaveEntitlementGenerationCsvBuilder
{
    public static LeaveExportFile Build(LeaveEntitlementGenerationPreviewResponse preview)
    {
        var sb = new StringBuilder();
        sb.Append("Employee Number,Employee Name,Leave Type,Total Days,Carried Forward,Remaining,Status,Reason\n");

        foreach (var line in preview.Lines)
        {
            sb.Append(line.EmployeeNumber).Append(',')
              .Append(Csv(line.EmployeeName)).Append(',')
              .Append(Csv(line.LeaveTypeName)).Append(',')
              .Append(line.TotalDays).Append(',')
              .Append(line.CarriedForwardDays).Append(',')
              .Append(line.RemainingDays).Append(',')
              .Append("Will be created").Append(',')
              .Append(Csv(line.Warning ?? "")).Append('\n');
        }

        foreach (var skip in preview.Skipped)
        {
            sb.Append("").Append(',')
              .Append(Csv(skip.EmployeeName ?? "")).Append(',')
              .Append("").Append(',').Append("").Append(',').Append("").Append(',').Append("")
              .Append(',').Append("Skipped").Append(',')
              .Append(Csv(skip.Reason)).Append('\n');
        }

        return new LeaveExportFile(
            Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"leave-entitlement-generation-{preview.Year}.csv");
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveEntitlementGenerationCsvBuilderTests`
Expected: PASS.

- [ ] **Step 5: Controller action**

Add to `LeaveEntitlementsController` (same request shape as the existing `PreviewGenerate` action):
```csharp
    [HttpPost("generate/preview/export")]
    [RequirePermission("leave:manage")]
    public async Task<IActionResult> PreviewGenerateExport(
        [FromBody] GenerateEntitlementsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new PreviewGenerateEntitlementsQuery(request.Year, request.LegalEntityId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var file = Application.Features.Leave.Entitlement.Helpers.LeaveEntitlementGenerationCsvBuilder.Build(result.Value!);
        return File(file.Content, file.ContentType, file.FileName);
    }
```

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/Leave/Entitlement/Helpers/LeaveEntitlementGenerationCsvBuilder.cs src/ONEVO.Api/Controllers/Tenant/Leave/LeaveEntitlementsController.cs tests/ONEVO.Tests.Unit/Features/Leave/Entitlement/LeaveEntitlementGenerationCsvBuilderTests.cs
git commit -m "feat(leave): add CSV export for entitlement generation preview"
```

---

### Task 6: `LeaveYearEndEntitlementJob`

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/Leave/LeaveYearEndEntitlementJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs` (register as hosted service)
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/LeaveYearEndEntitlementJobTests.cs`

Runs daily, and on Jan 1 UTC of a year it hasn't yet processed, generates that year's entitlements for every active tenant — reusing `LeaveEntitlementPlanner` + `ILeaveEntitlementRepository.AddGeneratedAsync`, the exact same two calls `GenerateEntitlementsCommandHandler` makes, so carry-forward/forfeiture math is identical to a manual HR-triggered generate. One tenant's failure doesn't stop the others.

- [ ] **Step 1: Write the failing test — the public per-tenant method, not the timer loop (matches `ActivityDailySummaryJob.RunAggregationAsync`'s "public entry for tests" precedent)**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Entities;
using ONEVO.Infrastructure.Services.Leave;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave;

public class LeaveYearEndEntitlementJobTests
{
    [Fact]
    public async Task RunForYearAsync_NoActiveTenants_CompletesWithoutError()
    {
        var services = new ServiceCollection();
        var tenantRepoMock = new Moq.Mock<ITenantRepository>();
        tenantRepoMock
            .Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Tenant>());

        services.AddSingleton(tenantRepoMock.Object);
        services.AddSingleton(Moq.Mock.Of<IWritableTenantContext>());
        services.AddSingleton(Moq.Mock.Of<ITenantContextSwitcher>());
        services.AddSingleton(Moq.Mock.Of<LeaveEntitlementPlanner>());
        services.AddSingleton(Moq.Mock.Of<ILeaveEntitlementRepository>());
        var provider = services.BuildServiceProvider();

        var job = new LeaveYearEndEntitlementJob(provider, NullLogger<LeaveYearEndEntitlementJob>.Instance);

        // Should not throw with zero tenants.
        await job.RunForYearAsync(2027, CancellationToken.None);
    }
}
```

*(This test only exercises the zero-tenant path without deep mocking of `LeaveEntitlementPlanner`'s own dependency chain, since that chain is already covered by `GenerateEntitlementsCommandHandler`'s existing Phase 3 tests — this test's job is to prove the job's own tenant-loop/error-isolation shape, not re-prove the calculator.)*

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveYearEndEntitlementJobTests`
Expected: FAIL — `LeaveYearEndEntitlementJob` doesn't exist.

- [ ] **Step 3: Implement the job**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;

namespace ONEVO.Infrastructure.Services.Leave;

/// <summary>
/// Daily-checked job that generates the new year's Leave entitlements (with carry-forward and
/// forfeiture already computed by LeaveEntitlementCalculator) for every active tenant, once,
/// on Jan 1 UTC. Same BackgroundService/daily-check shape as ActivityDailySummaryJob; same
/// admin-mode tenant enumeration + per-tenant SwitchToTenantAsync shape as
/// BulkOnboardingBatchProcessor. Does not call IMediator — GenerateEntitlementsCommandHandler
/// depends on ICurrentUser, which is HTTP-context-bound and unavailable here.
/// </summary>
public sealed class LeaveYearEndEntitlementJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeOnly TargetUtcTime = new(1, 0);

    private readonly IServiceProvider _services;
    private readonly ILogger<LeaveYearEndEntitlementJob> _logger;
    private int? _lastProcessedYear;

    public LeaveYearEndEntitlementJob(IServiceProvider services, ILogger<LeaveYearEndEntitlementJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now.Month == 1 && now.Day == 1
                    && now.TimeOfDay >= TargetUtcTime.ToTimeSpan()
                    && _lastProcessedYear != now.Year)
                {
                    await RunForYearAsync(now.Year, stoppingToken);
                    _lastProcessedYear = now.Year;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave year-end entitlement job iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Public entry for tests / manual triggers — same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync.</summary>
    public async Task RunForYearAsync(int year, CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();

        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenants = await tenantRepository.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct);

        _logger.LogInformation("Leave year-end entitlement job started. Year={Year} TenantCount={Count}", year, tenants.Count);

        var processedTenants = 0;
        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ProcessTenantAsync(scope.ServiceProvider, tenant, year, ct);
                processedTenants++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Leave year-end generation failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }

        _logger.LogInformation(
            "Leave year-end entitlement job finished. Year={Year} ProcessedTenants={Processed}/{Total}",
            year, processedTenants, tenants.Count);
    }

    private static async Task ProcessTenantAsync(
        IServiceProvider services, Tenant tenant, int year, CancellationToken ct)
    {
        var tenantSwitcher = services.GetRequiredService<ITenantContextSwitcher>();
        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var planner = services.GetRequiredService<LeaveEntitlementPlanner>();
        var entitlements = services.GetRequiredService<ILeaveEntitlementRepository>();
        var clock = services.GetRequiredService<IDateTimeProvider>();

        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var plan = await planner.PlanAsync(tenant.Id, year, legalEntityId: null, asOfDate, ct);
        if (plan.Lines.Count == 0)
            return;

        var now = clock.UtcNow;
        var writeSets = plan.Lines.Select(line =>
        {
            var entitlement = new LeaveEntitlement
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                EmployeeId = line.EmployeeId,
                LeaveTypeId = line.LeaveTypeId,
                Year = year,
                TotalDays = line.TotalDays,
                UsedDays = 0m,
                PendingDays = 0m,
                CarriedForwardDays = line.CarriedForwardDays,
                Source = LeaveEntitlementSources.Auto,
                CreatedAt = now
            };

            var audits = new List<LeaveBalanceAudit>
            {
                new()
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = line.EmployeeId, LeaveTypeId = line.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Accrual,
                    DaysChanged = line.TotalDays + line.CarriedForwardDays,
                    BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                    Reason = "Year-end automatic generation", CreatedAt = now, CreatedBy = null
                }
            };

            if (line.ForfeitedDays > 0m)
            {
                audits.Add(new LeaveBalanceAudit
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = line.EmployeeId, LeaveTypeId = line.LeaveTypeId,
                    ChangeType = LeaveBalanceChangeTypes.Forfeiture,
                    DaysChanged = -line.ForfeitedDays,
                    BalanceAfter = line.TotalDays + line.CarriedForwardDays,
                    Reason = "Carry-forward cap applied during year-end generation",
                    CreatedAt = now, CreatedBy = null
                });
            }

            return new LeaveEntitlementWriteSet(entitlement, audits);
        }).ToList();

        try
        {
            await entitlements.AddGeneratedAsync(writeSets, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            // Already generated for this tenant/year (job re-ran, or HR already generated
            // manually) — idempotent no-op, matches spec §4 "Year-end already ran: Skip".
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveYearEndEntitlementJobTests`
Expected: PASS.

- [ ] **Step 5: Register as hosted service**

Add next to the other `AddHostedService` calls in `DependencyInjection.cs` (near `ActivityDailySummaryJob`'s registration):
```csharp
        services.AddHostedService<Services.Leave.LeaveYearEndEntitlementJob>();
```

- [ ] **Step 6: Build**

Run: `dotnet build src/ONEVO.Infrastructure`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/Leave src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/Leave/LeaveYearEndEntitlementJobTests.cs
git commit -m "feat(leave): add automatic year-end entitlement generation job"
```

---

### Task 7: Full-suite run + live dev-DB verification

**Files:** none new — verification only.

- [ ] **Step 1: Full unit suite**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests pass, no regressions in Phases 0-7.

- [ ] **Step 2: Architecture suite**

Run: `dotnet test tests/ONEVO.Tests.Architecture`
Expected: pass — `LeaveYearEndEntitlementJob` lives in Infrastructure (not Application), `LeaveBalanceAuditController` never injects `ApplicationDbContext` directly.

- [ ] **Step 3: Integration suite**

Run: `dotnet test tests/ONEVO.Tests.Integration --filter FullyQualifiedName~Leave`
Expected: pass (after Task 3's fixture-name correction).

- [ ] **Step 4: Live dev-DB smoke run**

Start the API against the real local dev DB. As the `acme` tenant's HR Manager: `GET /api/v1/leave/balance-audit` (confirm rows from Phases 3/5/6's already-written audit trail appear — e.g. a Deduction row from an approved request, an Adjustment row from a manual entitlement change), `GET /api/v1/leave/balance-audit/export` (confirm a CSV downloads with the right columns), `POST /api/v1/leave/entitlements/generate/preview/export` for a future year (confirm CSV downloads). For the year-end job: call `LeaveYearEndEntitlementJob.RunForYearAsync` directly from a throwaway test harness or `dotnet run`-time hook against a target year, confirm entitlement rows + Accrual/Forfeiture audit rows appear for `acme`'s seeded employees, and confirm a second call for the same year no-ops (idempotent) rather than duplicating rows.

- [ ] **Step 5: Update plan status**

Edit `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: mark Phase 8 `**Status:**` as "written in full — **executed [date]**, N/N tasks, live dev-DB verified." Update `plans/next/SUMMARY.md` and `plans/SUMMARY.md` entries to reflect Phase 8 done.

- [ ] **Step 6: Final commit**

```bash
git add docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md docs/superpowers/plans/next/SUMMARY.md docs/superpowers/plans/SUMMARY.md
git commit -m "docs(leave): mark Phase 8 executed"
```
