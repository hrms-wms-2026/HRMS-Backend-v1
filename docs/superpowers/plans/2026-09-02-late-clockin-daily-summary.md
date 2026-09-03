# Late Clock-In Daily Summary Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Once per legal-entity-local working day, find every employee who clocked in late, resolve who is responsible for them via `IEmployeeAuthorityResolver`, and send that responsible person one in-app "daily summary" notification listing their late reports for the day.

**Architecture:** A new `BackgroundService` (`LateClockInDailySummaryJob`) polls every 15 minutes. For every active tenant, it enumerates that tenant's active `LegalEntity` rows; once a legal entity's local clock has passed (shift start + 2h) and it hasn't already run today, it queries `AttendanceRecord` rows already marked `Status == "late"` for that day, groups them by the responsible person (`IEmployeeAuthorityResolver.ResolveApproverAsync`, permission `attendance:read`, with a new "company-wide coverage" fallback tier for employees with no configured manager/department owner), and sends one templated in-app notification per responsible person via the existing `INotificationDispatcher.SendTemplatedAsync`. A DB-backed existence check makes re-runs (job restart, retry) idempotent.

**Tech Stack:** ASP.NET Core `BackgroundService`, EF Core/PostgreSQL, MediatR-free (this is a background job, not a request handler), xUnit for tests.

---

## Before you start: why this needs 7 supporting changes, not just a new job class

Research into the existing codebase (recorded here so the reasoning isn't lost) found that **`IEmployeeAuthorityResolver.ResolveApproverAsync` cannot currently be called from a background job.** It reads `_currentUser.TenantId` (`EmployeeAuthorityResolver.cs:174`), and the only registered `ICurrentUser` implementation (`CurrentUserService`) reads `TenantId` from `HttpContext` claims (`CurrentUserService.cs:24-31`) — which is `null` in a `BackgroundService`, so `TenantId` silently returns `Guid.Empty` and every lookup fails to find anyone. This is why the closest existing precedent, `LeaveYearEndEntitlementJob`, carries this comment: *"Does not call IMediator - GenerateEntitlementsCommandHandler depends on ICurrentUser, which is HTTP-context-bound and unavailable here."* Every other job in the codebase avoids this by only calling repositories that take `tenantId` as an explicit parameter.

The fix (Task 1) is small and safe: `ICurrentUser.TenantId` already has an unused fallback available — `ITenantContext.TenantId`, the same ambient scoped value that `LeaveYearEndEntitlementJob`'s `SwitchToTenantAsync` already sets for RLS purposes (`TenantContextAccessor.cs`, `TenantContextSwitcher.cs`). Falling back to it only changes behavior when `HttpContext` is `null`, which never happens on a real web request, so no existing authenticated code path changes.

Second, the user asked for late employees who have no resolvable manager to fall back to a fixed HR/Admin recipient rather than being dropped silently. `ResolveApproverAsync` only walks Position coverage → Department coverage → Reporting line today, and returns `UnprocessableEntity` if none match (`EmployeeAuthorityResolver.cs:250-251`) — even though a `ManagementCoverageRecord.TargetCompany` ("cover the whole company") coverage type already exists and is already used by the *visibility* path (`AddManagedVisibilityAsync`, lines 126/163), just not by the *approver* path. `ClockInPolicy.NotificationRecipientResolver` even already has a constant named `management_coverage_owner` for exactly this idea, unused today. Task 2 adds company-wide coverage as a final fallback tier to `ResolveApproverAsync` itself (reusing the existing generic `TryResolveFromCoverageAsync` helper), which is the correct place for it — every future caller benefits, not just this job, and it can only turn existing failures into successes (it only runs when the first three tiers already returned nothing).

## Locked product decisions (from user Q&A during design)

- Recipient: the late employee's resolved approver/manager (via `IEmployeeAuthorityResolver`), not the employee themselves.
- Timing: once per legal-entity-local working day, a fixed 2 hours after that legal entity's configured shift start (`LegalEntity.WorkStartTime`). Late clock-ins after that time on the same day are not retroactively added to that day's summary — accepted v1 behavior.
- "Late" = `AttendanceRecord.Status == AttendanceRecord.StatusLate`. This already excludes leave (`StatusOnTimeOff`), holidays/non-working days (`StatusNonWorkingDay`, `StatusOffDay`), and days without a configured schedule (`StatusNoSchedule`/`StatusPolicyNotConfigured`) — no extra filtering needed.
- No-shows (never clocked in) are out of scope for v1 — only actual late clock-ins.
- No resolvable manager → fall back to the legal entity's company-wide coverage owner (if configured); if that's also not configured, skip the employee and log a warning (no further escalation in v1).
- Scope is backend-only; no TrayApp changes.

## File Structure

```
src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs     (modify — tenant fallback)
src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/
  Models/EmployeeApprovalRouteSource.cs                                 (modify — add CompanyCoverage)
  Models/EmployeeAuthorityPurpose.cs                                    (modify — add AttendanceLateNotification)
  Services/EmployeeAuthorityResolver.cs                                 (modify — company-wide fallback tier)
src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs (modify)
src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs   (modify)
src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs (modify)
src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/EfLegalEntityRepository.cs        (modify)
src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs                     (modify)
src/ONEVO.Infrastructure/Persistence/Repositories/SharedPlatform/EfNotificationRepository.cs      (modify)
src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs                        (modify — new template)
src/ONEVO.Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJob.cs                    (create — the job)
src/ONEVO.Infrastructure/DependencyInjection.cs                                                   (modify — register job)
tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverTests.cs         (modify — new Facts)
tests/ONEVO.Tests.Unit/Infrastructure/Identity/CurrentUser/CurrentUserServiceTests.cs              (create)
tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs                  (modify — new Fact)
tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/EfLegalEntityRepositoryTests.cs           (modify — new Fact)
tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/EfNotificationRepositoryTests.cs      (create)
tests/ONEVO.Tests.Unit/Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJobRelatedEntityIdTests.cs (create)
tests/ONEVO.Tests.Unit/Features/TimeAttendance/LateClockInDailySummaryJobTests.cs                  (create)
```

No new tables/entities/migrations — everything needed already exists on `AttendanceRecord`, `Notification`, and `ManagementCoverageRecord`.

---

### Task 1: Let `ICurrentUser.TenantId` fall back to the ambient tenant context outside HTTP requests

**Files:**
- Modify: `src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs`
- Test: `tests/ONEVO.Tests.Unit/Infrastructure/Identity/CurrentUser/CurrentUserServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.AspNetCore.Http;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.CurrentUser;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Identity.CurrentUser;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public void TenantId_FallsBackToTenantContext_WhenNoHttpContext()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var tenantContext = new Mock<ITenantContext>();
        var expectedTenantId = Guid.NewGuid();
        tenantContext.Setup(t => t.TenantId).Returns(expectedTenantId);

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContext.Object);

        Assert.Equal(expectedTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_PrefersHttpContextClaim_WhenHttpContextPresent()
    {
        var claimTenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("tenant_id", claimTenantId.ToString())
            }));
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.TenantId).Returns(Guid.NewGuid()); // must be ignored

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContext.Object);

        Assert.Equal(claimTenantId, sut.TenantId);
    }

    [Fact]
    public void TenantId_FallsBackThroughRealTenantContextAccessor_AfterSwitchToTenant()
    {
        // Proves the actual production wiring this fix exists for: a background job calls
        // ITenantContextSwitcher.SwitchToTenantAsync, which calls IWritableTenantContext.Resolve(...)
        // on TenantContextAccessor - the same object also registered as ITenantContext. This test
        // uses the real TenantContextAccessor (not a mock of the interface) so a change to that
        // class's Resolve()/TenantId wiring would break this test too, not just an interface stub.
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var tenantContextAccessor = new ONEVO.Infrastructure.Identity.Tenancy.TenantContextAccessor();
        var tenantId = Guid.NewGuid();
        tenantContextAccessor.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
            tenantId, "acme", ONEVO.Domain.Features.InfrastructureModule.Entities.TenantStatus.Active, PlanCode: null));

        var sut = new ONEVO.Infrastructure.Identity.CurrentUser.CurrentUserService(
            httpContextAccessor.Object, tenantContextAccessor);

        Assert.Equal(tenantId, sut.TenantId);
    }
}
```

Confirm `TenantRegistryEntry`'s exact constructor parameter order/names against `TenantContextSwitcher.cs`/`LeaveYearEndEntitlementJob.cs` (`new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null)`) before running.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CurrentUserServiceTests"`
Expected: FAIL to compile — `CurrentUserService` has no constructor accepting `ITenantContext`.

- [ ] **Step 3: Write minimal implementation**

Modify `src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.CurrentUser;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    public Guid UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    // No HttpContext means this is running outside a web request (e.g. a BackgroundService that
    // has already switched into a specific tenant via ITenantContextSwitcher.SwitchToTenantAsync,
    // which sets this same scoped ITenantContext). Falling back only when HttpContext is null
    // never changes behavior on a real authenticated request.
    public Guid TenantId
    {
        get
        {
            if (_httpContextAccessor.HttpContext is null)
                return _tenantContext.TenantId;

            var value = _httpContextAccessor.HttpContext.User?.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public string Email
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public IReadOnlyList<string> Permissions
    {
        get
        {
            var claims = _httpContextAccessor.HttpContext?.User?.FindAll("permission");
            return claims?.Select(c => c.Value).ToList() ?? [];
        }
    }

    public bool HasPermission(string permission)
        => Permissions.Contains(permission);

    public bool IsAuthenticated
        => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public string? SessionBinding
        => _httpContextAccessor.HttpContext?.User?.FindFirstValue("csrf_token_hash");

    public DateTimeOffset? SessionExpiresAt
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("session_expires_at");
            return DateTimeOffset.TryParse(value, out var expiresAt) ? expiresAt : null;
        }
    }

    public Guid? SessionId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("session_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid? LegalEntityId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("legal_entity_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
```

Note: `IsAuthenticated` deliberately still returns `false` with no `HttpContext` — the fallback only helps tenant-scoped *data* lookups (`ResolveApproverAsync` reads `TenantId` but never checks `IsAuthenticated`), it does not make the job "authenticated as a user".

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CurrentUserServiceTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Identity/CurrentUser/CurrentUserService.cs tests/ONEVO.Tests.Unit/Infrastructure/Identity/CurrentUser/CurrentUserServiceTests.cs
git commit -m "fix: fall back ICurrentUser.TenantId to ambient tenant context outside HTTP requests"
```

---

### Task 2: Add a company-wide coverage fallback tier to `ResolveApproverAsync`

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRouteSource.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `EmployeeAuthorityResolverTests.cs` (uses the existing `EmployeeAuthorityTestGraph` harness already in that folder):

```csharp
[Fact] // New. Company-wide coverage resolves as approver when no position/department/reporting-line match exists.
public async Task Approval_ResolvesCompanyWideCoverage_WhenNoOtherTierMatches()
{
    var graph = new EmployeeAuthorityTestGraph();
    var legalEntityId = Guid.NewGuid();

    var subject = graph.AddEmployee(legalEntityId); // no manager, no position/department coverage

    var hrPosition = graph.AddPosition(legalEntityId);
    var hrOwner = graph.AddEmployee(legalEntityId);
    graph.AddPrimaryAssignment(hrOwner.Id, hrPosition.Id);
    graph.GrantPermission(hrOwner.UserId, AttendanceApprove);

    graph.AddCoverage(
        legalEntityId, hrPosition.Id, ManagementCoverageRecord.TargetCompany,
        coveredPositionId: null, coveredDepartmentId: null, ownerOrder: 1);

    var resolver = graph.BuildResolver();
    var route = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
        subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

    Assert.True(route.IsSuccess);
    Assert.Equal(hrOwner.UserId, route.Value!.ApproverUserId);
    Assert.Equal(EmployeeApprovalRouteSource.CompanyCoverage, route.Value.Source);
}

[Fact] // New. Company-wide coverage is not used when a nearer tier already resolves.
public async Task Approval_PrefersReportingLine_OverCompanyWideCoverage()
{
    var graph = new EmployeeAuthorityTestGraph();
    var legalEntityId = Guid.NewGuid();

    var managerPosition = graph.AddPosition(legalEntityId);
    var manager = graph.AddEmployee(legalEntityId);
    graph.AddPrimaryAssignment(manager.Id, managerPosition.Id);
    graph.GrantPermission(manager.UserId, AttendanceApprove);

    var subject = graph.AddEmployee(legalEntityId);
    graph.SetManager(subject.Id, manager.Id);

    var hrPosition = graph.AddPosition(legalEntityId);
    var hrOwner = graph.AddEmployee(legalEntityId);
    graph.AddPrimaryAssignment(hrOwner.Id, hrPosition.Id);
    graph.GrantPermission(hrOwner.UserId, AttendanceApprove);
    graph.AddCoverage(
        legalEntityId, hrPosition.Id, ManagementCoverageRecord.TargetCompany,
        coveredPositionId: null, coveredDepartmentId: null, ownerOrder: 1);

    var resolver = graph.BuildResolver();
    var route = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
        subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

    Assert.True(route.IsSuccess);
    Assert.Equal(manager.UserId, route.Value!.ApproverUserId);
    Assert.Equal(EmployeeApprovalRouteSource.ReportingLine, route.Value.Source);
}

[Fact] // New. Still fails when nobody, including company-wide coverage, is configured.
public async Task Approval_StillFails_WhenNoCoverageAtAll()
{
    var graph = new EmployeeAuthorityTestGraph();
    var legalEntityId = Guid.NewGuid();
    var subject = graph.AddEmployee(legalEntityId);

    var resolver = graph.BuildResolver();
    var route = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
        subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

    Assert.False(route.IsSuccess);
}
```

Add `using ONEVO.Domain.Features.CoreHr.Entities;` at the top of the test file if `ManagementCoverageRecord` isn't already imported there (check the existing `using` block first — it's referenced in `EmployeeAuthorityTestGraph.cs` already, confirm the namespace matches).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~EmployeeAuthorityResolverTests"`
Expected: `Approval_ResolvesCompanyWideCoverage_WhenNoOtherTierMatches` FAILS (`route.IsSuccess` is `false`); `Approval_PrefersReportingLine_OverCompanyWideCoverage` and `Approval_StillFails_WhenNoCoverageAtAll` PASS already (they're regression guards, not new behavior).

- [ ] **Step 3: Add `CompanyCoverage` to the source enum**

Modify `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRouteSource.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

public enum EmployeeApprovalRouteSource
{
    PositionCoverage,
    DepartmentCoverage,
    ReportingLine,
    CompanyCoverage,
}
```

- [ ] **Step 4: Add the fallback tier to `ResolveApproverAsync`**

In `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs`, insert a new block between the end of the reporting-line `foreach` loop (line 248) and the final `return Result<EmployeeApprovalRoute>.UnprocessableEntity(...)` (lines 250-251):

```csharp
        foreach (var ancestorEmployeeId in ancestorChain)
        {
            var ancestorEmployee = await _employeeRepository.GetByIdAsync(tenantId, ancestorEmployeeId, cancellationToken);
            if (ancestorEmployee is null || ancestorEmployee.LegalEntityId != request.LegalEntityId)
                continue;

            var hasPermission = await _permissionRepository.UserHasPermissionCodeAsync(
                ancestorEmployee.UserId, request.RequiredPermission, now, cancellationToken);
            if (!hasPermission)
                continue;

            var ancestorAssignment = await _positionAssignmentRepository.GetActivePrimaryAsync(
                tenantId, ancestorEmployeeId, cancellationToken);
            if (ancestorAssignment is null)
                continue;

            return Result<EmployeeApprovalRoute>.Success(new EmployeeApprovalRoute(
                ancestorEmployeeId, ancestorEmployee.UserId, ancestorAssignment.PositionId,
                request.RequiredPermission, request.Purpose, EmployeeApprovalRouteSource.ReportingLine, null));
        }

        // Final fallback: a manually configured company-wide coverage owner (ClockInPolicy calls
        // this concept "management_coverage_owner"), used only when nobody in the subject's
        // position/department coverage or reporting line qualifies. Reuses the same coverage
        // resolution helper as the tiers above - the only difference is the covered-target type.
        var companyCoverage = await _positionRepository.ListActiveCoverageByCoveredTargetAsync(
            tenantId, request.LegalEntityId, ManagementCoverageRecord.TargetCompany,
            coveredPositionId: null, coveredDepartmentId: null, excludingRecordId: null, cancellationToken);

        var companyRoute = await TryResolveFromCoverageAsync(
            tenantId, request, companyCoverage, subject.Id, descendantSet,
            EmployeeApprovalRouteSource.CompanyCoverage, now, cancellationToken);
        if (companyRoute is not null)
            return Result<EmployeeApprovalRoute>.Success(companyRoute);

        return Result<EmployeeApprovalRoute>.UnprocessableEntity(
            "No eligible approver was found for this employee and action.");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~EmployeeAuthorityResolverTests"`
Expected: PASS (all Facts in the file, including the 3 new ones)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeApprovalRouteSource.cs src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Services/EmployeeAuthorityResolver.cs tests/ONEVO.Tests.Unit/Features/CoreHr/EmployeeAuthority/EmployeeAuthorityResolverTests.cs
git commit -m "feat: add company-wide coverage as a final ResolveApproverAsync fallback tier"
```

---

### Task 3: Add the `AttendanceLateNotification` authority purpose

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs`

- [ ] **Step 1: Add the enum value** (no test needed — this is a call-site hint enum with no behavior, per its own doc comment: "adding a new value never requires a migration")

```csharp
namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Internal application-logic classifier for why IEmployeeAuthorityResolver is being asked to
/// resolve visibility or an approver. Never persisted - purely a call-site hint (e.g. for future
/// per-purpose caching or logging), it does not change resolver behavior in Part 0. Callers pick
/// the closest fit; adding a new value never requires a migration.
/// </summary>
public enum EmployeeAuthorityPurpose
{
    EmployeeListRead,
    TimeTrackingRead,
    AttendanceCorrectionApproval,
    WorkAreaChangeApproval,
    TimeOffApproval,
    OnboardingApproval,
    OffboardingApproval,
    EmployeeLifecycleApproval,
    AttendanceLateNotification,
}
```

- [ ] **Step 2: Build to confirm no breakage**

Run: `dotnet build src/ONEVO.Application`
Expected: Build succeeds (adding an enum member is additive).

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/EmployeeAuthority/Models/EmployeeAuthorityPurpose.cs
git commit -m "feat: add AttendanceLateNotification authority purpose"
```

---

### Task 4: Query late attendance records by tenant + date + status

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs` (this file already exists with a `BuildInMemoryDb()` / `NewDbContext(...)` helper pair using `Microsoft.EntityFrameworkCore.InMemoryDatabase` plus mocked `ICurrentUser`/`IDateTimeProvider`/`IPublisher`/`ITenantContext` interceptor args — reuse those helpers verbatim, do not invent a new fixture)

- [ ] **Step 1: Write the failing unit test**

Add this `[Fact]` to the existing `EfAttendanceReadRepositoryTests` class:

```csharp
[Fact]
public async Task ListByStatusAsync_ReturnsOnlyMatchingTenantDateAndStatus()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var date = new DateOnly(2026, 9, 1);

    db.AttendanceRecords.AddRange(
        new AttendanceRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(), Date = date, Status = AttendanceRecord.StatusLate, LateMinutes = 12, ExpectedWorkingDay = true },
        new AttendanceRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(), Date = date, Status = AttendanceRecord.StatusOnTime, LateMinutes = 0, ExpectedWorkingDay = true },
        new AttendanceRecord { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(), Date = date.AddDays(-1), Status = AttendanceRecord.StatusLate, LateMinutes = 5, ExpectedWorkingDay = true },
        new AttendanceRecord { Id = Guid.NewGuid(), TenantId = otherTenantId, EmployeeId = Guid.NewGuid(), Date = date, Status = AttendanceRecord.StatusLate, LateMinutes = 20, ExpectedWorkingDay = true });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var repository = new EfAttendanceReadRepository(db);
    var result = await repository.ListByStatusAsync(tenantId, date, AttendanceRecord.StatusLate, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal(12, result[0].LateMinutes);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EfAttendanceReadRepositoryTests.ListByStatusAsync_ReturnsOnlyMatchingTenantDateAndStatus"`
Expected: FAIL to compile — `ListByStatusAsync` doesn't exist yet.

- [ ] **Step 3: Add the method to the interface**

Add to `IAttendanceReadRepository.cs` (alongside the other `List...Async` methods):

```csharp
Task<IReadOnlyList<AttendanceRecord>> ListByStatusAsync(
    Guid tenantId, DateOnly date, string status, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfAttendanceReadRepository`**

Add to `EfAttendanceReadRepository.cs`:

```csharp
public async Task<IReadOnlyList<AttendanceRecord>> ListByStatusAsync(
    Guid tenantId, DateOnly date, string status, CancellationToken ct = default)
    => await db.AttendanceRecords
        .AsNoTracking()
        .Where(x => x.TenantId == tenantId && x.Date == date && x.Status == status)
        .ToListAsync(ct);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EfAttendanceReadRepositoryTests"`
Expected: PASS (all Facts in the file, including the new one)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs
git commit -m "feat: add IAttendanceReadRepository.ListByStatusAsync for daily status queries"
```

---

### Task 5: List a tenant's active legal entities without requiring an acting user

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/EfLegalEntityRepository.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/EfLegalEntityRepositoryTests.cs` (this file already has `BuildInMemoryDb()` and a `CreateLegalEntity(tenantId, name)` helper — reuse both)

- [ ] **Step 1: Write the failing unit test**

Add this `[Fact]` to the existing `EfLegalEntityRepositoryTests` class:

```csharp
[Fact]
public async Task ListActiveForTenantAsync_ReturnsOnlyActiveEntitiesForThatTenant()
{
    await using var db = BuildInMemoryDb();
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();
    var inactive = CreateLegalEntity(tenantId, "Inactive Co");
    inactive.IsActive = false;
    db.LegalEntities.AddRange(
        CreateLegalEntity(tenantId, "Active Co"),
        inactive,
        CreateLegalEntity(otherTenantId, "Other Tenant Co"));
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var repository = new EfLegalEntityRepository(db);
    var result = await repository.ListActiveForTenantAsync(tenantId, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal("Active Co", result[0].Name);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EfLegalEntityRepositoryTests.ListActiveForTenantAsync_ReturnsOnlyActiveEntitiesForThatTenant"`
Expected: FAIL to compile — `ListActiveForTenantAsync` doesn't exist yet.

- [ ] **Step 3: Add the method to the interface**

Add to `ILegalEntityRepository.cs`:

```csharp
// Cross-tenant background jobs (no acting user) need every active legal entity for one tenant.
// ListAccessibleAsync always requires a userId even on its management-access branch, so it isn't
// usable from a BackgroundService.
Task<IReadOnlyList<LegalEntity>> ListActiveForTenantAsync(Guid tenantId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfLegalEntityRepository`**

Add to `EfLegalEntityRepository.cs`:

```csharp
public async Task<IReadOnlyList<LegalEntity>> ListActiveForTenantAsync(Guid tenantId, CancellationToken ct = default)
    => await _db.LegalEntities
        .AsNoTracking()
        .Where(entity => entity.TenantId == tenantId && entity.IsActive)
        .OrderBy(entity => entity.Name)
        .ToListAsync(ct);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EfLegalEntityRepositoryTests"`
Expected: PASS (all Facts in the file, including the new one)

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/OrgStructure/LegalEntity/RepositoryInterfaces/ILegalEntityRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/OrgStructure/EfLegalEntityRepository.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/EfLegalEntityRepositoryTests.cs
git commit -m "feat: add ILegalEntityRepository.ListActiveForTenantAsync for background jobs"
```

---

### Task 6: Idempotency check on the SharedPlatform notification repository

**Files:**
- Modify: `src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/SharedPlatform/EfNotificationRepository.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/EfNotificationRepositoryTests.cs` (no unit-test file exists yet for this repository — follow the exact `BuildInMemoryDb()`/`NewDbContext(...)` helper pattern already established in `tests/ONEVO.Tests.Unit/Features/TimeAttendance/EfAttendanceReadRepositoryTests.cs`, i.e. `UseInMemoryDatabase` plus mocked `ICurrentUser`/`IDateTimeProvider`/`IPublisher`/`ITenantContext` passed into `ApplicationDbContext`'s interceptor constructor args)

- [ ] **Step 1: Write the failing unit test**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public sealed class EfNotificationRepositoryTests
{
    [Fact]
    public async Task ExistsAsync_TrueOnlyForExactTenantRecipientTemplateAndRelatedEntity()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();
        var relatedEntityId = Guid.NewGuid();

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientUserId = recipientUserId,
            TemplateCode = "attendance_late_clockin_daily_summary", Title = "t", Body = "b",
            RelatedEntityType = "attendance_late_daily_summary", RelatedEntityId = relatedEntityId,
            IsRead = false, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfNotificationRepository(db);

        Assert.True(await repository.ExistsAsync(
            tenantId, recipientUserId, "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", relatedEntityId, CancellationToken.None));
        Assert.False(await repository.ExistsAsync(
            tenantId, recipientUserId, "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await repository.ExistsAsync(
            tenantId, Guid.NewGuid(), "attendance_late_clockin_daily_summary",
            "attendance_late_daily_summary", relatedEntityId, CancellationToken.None));
    }

    private static ApplicationDbContext BuildInMemoryDb()
        => NewDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext NewDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(options,
            new AuditableEntityInterceptor(currentUser.Object, dateTime.Object),
            new SoftDeleteInterceptor(dateTime.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EfNotificationRepositoryTests"`
Expected: FAIL to compile — `ExistsAsync` doesn't exist yet.

- [ ] **Step 3: Add the method to the interface**

Add to `INotificationRepository.cs`:

```csharp
// Idempotency guard for jobs that may be retried or restarted: has this exact
// (recipient, template, related entity) notification already been sent?
Task<bool> ExistsAsync(
    Guid tenantId, Guid recipientUserId, string templateCode,
    string relatedEntityType, Guid relatedEntityId, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfNotificationRepository`**

Add to `EfNotificationRepository.cs`:

```csharp
public async Task<bool> ExistsAsync(
    Guid tenantId, Guid recipientUserId, string templateCode,
    string relatedEntityType, Guid relatedEntityId, CancellationToken ct = default)
    => await _db.Notifications.AsNoTracking().AnyAsync(n =>
        n.TenantId == tenantId
        && n.RecipientUserId == recipientUserId
        && n.TemplateCode == templateCode
        && n.RelatedEntityType == relatedEntityType
        && n.RelatedEntityId == relatedEntityId, ct);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EfNotificationRepositoryTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Common/RepositoryInterfaces/INotificationRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/SharedPlatform/EfNotificationRepository.cs tests/ONEVO.Tests.Unit/Features/SharedPlatform/Notifications/EfNotificationRepositoryTests.cs
git commit -m "feat: add INotificationRepository.ExistsAsync for job idempotency"
```

---

### Task 7: Seed the notification template

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs`

**Known merge hotspot:** this file's `templates` list has previously conflicted across branches (each branch appends near the end). Add this entry as its own list item near the other `attendance_*` entries (around the `work_area_change_request_*` group) — do not reorder or remove any existing entry, and if a merge conflict appears here, keep entries from **both** sides.

- [ ] **Step 1: Add the template entry**

Insert into the `templates` list in `NotificationTemplateSeeder.cs`, after the `attendance_correction_request_cancelled` entry:

```csharp
new()
{
    Id = Guid.NewGuid(), Code = "attendance_late_clockin_daily_summary",
    InAppTitleTemplate = "Late clock-in summary for {{date}}",
    InAppBodyTemplate = "{{lateCount}} employee(s) clocked in late today: {{lateEmployees}}."
},
```

- [ ] **Step 2: Verify seeding is idempotent by inspection**

The existing `SeedAsync` loop already skips any `Code` that's already in the database (`GetTemplateByCodeAsync` check at line 190), so no new test is needed here — this is additive data, not logic. Confirm by running the app locally once and checking `SELECT code FROM notification_templates WHERE code = 'attendance_late_clockin_daily_summary';` returns one row after startup.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs
git commit -m "feat: seed attendance_late_clockin_daily_summary notification template"
```

---

### Task 8: The `LateClockInDailySummaryJob` background service

**Files:**
- Create: `src/ONEVO.Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJob.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJobRelatedEntityIdTests.cs`

This task has two parts: a small pure helper (TDD'd first, since it's the only branch-free logic worth a unit test) and the job itself (verified by the Task 9 integration tests, since everything else it does is DB/DI-bound).

- [ ] **Step 1: Write the failing test for the deterministic related-entity-id helper**

```csharp
using Xunit;
using ONEVO.Infrastructure.Services.TimeAttendance;

namespace ONEVO.Tests.Unit.Infrastructure.Services.TimeAttendance;

public sealed class LateClockInDailySummaryJobRelatedEntityIdTests
{
    [Fact]
    public void BuildRelatedEntityId_IsDeterministic_ForSameInputs()
    {
        var legalEntityId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 1);

        var first = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, date);
        var second = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, date);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildRelatedEntityId_Differs_AcrossDates()
    {
        var legalEntityId = Guid.NewGuid();

        var day1 = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, new DateOnly(2026, 9, 1));
        var day2 = LateClockInDailySummaryJob.BuildRelatedEntityId(legalEntityId, new DateOnly(2026, 9, 2));

        Assert.NotEqual(day1, day2);
    }

    [Fact]
    public void BuildRelatedEntityId_Differs_AcrossLegalEntities()
    {
        var date = new DateOnly(2026, 9, 1);

        var a = LateClockInDailySummaryJob.BuildRelatedEntityId(Guid.NewGuid(), date);
        var b = LateClockInDailySummaryJob.BuildRelatedEntityId(Guid.NewGuid(), date);

        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LateClockInDailySummaryJobRelatedEntityIdTests"`
Expected: FAIL to compile — `LateClockInDailySummaryJob` doesn't exist yet.

- [ ] **Step 3: Create the job**

Create `src/ONEVO.Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJob.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.LegalEntity.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.TimeAttendance;

/// <summary>
/// Once per legal-entity-local working day, 2 hours after that legal entity's configured shift
/// start, sends each employee's resolved attendance approver (or, failing that, the legal
/// entity's company-wide coverage owner - see EmployeeAuthorityResolver.ResolveApproverAsync's
/// CompanyCoverage fallback tier) one notification listing that day's late clock-ins.
///
/// Same admin-mode tenant listing + SwitchToTenantAsync mechanism as LeaveYearEndEntitlementJob,
/// but with one DI scope PER TENANT rather than one shared scope for the whole tick - see the
/// comment on RunTickAsync for why. IEmployeeAuthorityResolver depends on ICurrentUser.TenantId,
/// which (after the fix in CurrentUserService) falls back to the ambient ITenantContext this loop
/// sets via SwitchToTenantAsync - unlike LeaveYearEndEntitlementJob, this job CAN use the resolver.
/// </summary>
public sealed class LateClockInDailySummaryJob : BackgroundService
{
    private const string AttendanceReadPermission = "attendance:read";
    private const string TemplateCode = "attendance_late_clockin_daily_summary";
    private const string RelatedEntityType = "attendance_late_daily_summary";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OffsetFromShiftStart = TimeSpan.FromHours(2);

    private readonly IServiceProvider _services;
    private readonly ILogger<LateClockInDailySummaryJob> _logger;
    private readonly Dictionary<Guid, DateOnly> _lastRunLocalDateByLegalEntity = new();

    public LateClockInDailySummaryJob(IServiceProvider services, ILogger<LateClockInDailySummaryJob> logger)
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
                await RunTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Late clock-in daily summary job iteration failed; will retry.");
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

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// ActivityDailySummaryJob.RunAggregationAsync / LeaveYearEndEntitlementJob.RunForYearAsync.
    ///
    /// Unlike LeaveYearEndEntitlementJob (which reuses one CreateAsyncScope/DbContext across every
    /// tenant because it never opens an explicit transaction), this job's per-legal-entity work
    /// runs inside IUnitOfWork.ExecuteInTransactionAsync. Reusing one DbContext across tenants
    /// would leave tenant A's tracked Notification entities in the same change tracker while
    /// tenant B's SwitchToTenantAsync flips the ambient RLS tenant underneath it - EF's change
    /// tracker is never cleared by SaveChangesAsync. Each tenant therefore gets its own scope (and
    /// so its own ApplicationDbContext), matching the tenant boundary to the DbContext lifetime
    /// exactly. The one scope created up front is only used to list tenants in admin mode.</summary>
    public async Task RunTickAsync(CancellationToken ct)
    {
        IReadOnlyList<Tenant> tenants;
        await using (var listScope = _services.CreateAsyncScope())
        {
            listScope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
            var tenantRepository = listScope.ServiceProvider.GetRequiredService<ITenantRepository>();
            tenants = await tenantRepository.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct);
        }

        foreach (var tenant in tenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var tenantScope = _services.CreateAsyncScope();
                await ProcessTenantAsync(tenantScope.ServiceProvider, tenant, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Late clock-in daily summary failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
    }

    private async Task ProcessTenantAsync(IServiceProvider services, Tenant tenant, CancellationToken ct)
    {
        var tenantSwitcher = services.GetRequiredService<ITenantContextSwitcher>();
        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var legalEntities = services.GetRequiredService<ILegalEntityRepository>();
        var activeLegalEntities = await legalEntities.ListActiveForTenantAsync(tenant.Id, ct);
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var utcNow = clock.UtcNow;

        foreach (var legalEntity in activeLegalEntities)
        {
            ct.ThrowIfCancellationRequested();

            var resolution = AttendanceScheduleResolver.Resolve(legalEntity, utcNow);
            if (resolution.Schedule.Status != "configured"
                || !resolution.Schedule.IsWorkingDay
                || resolution.Schedule.Start is not { } shiftStart)
                continue;

            var dueAt = shiftStart.ToTimeSpan() + OffsetFromShiftStart;
            var alreadyRunToday = _lastRunLocalDateByLegalEntity.TryGetValue(legalEntity.Id, out var lastRun)
                && lastRun == resolution.WorkDate;
            if (resolution.LocalNow.TimeOfDay < dueAt || alreadyRunToday)
                continue;

            try
            {
                await RunForLegalEntityAsync(services, tenant.Id, legalEntity, resolution.WorkDate, ct);
                // Only recorded on success: a transient failure (DB blip, one bad row) should be
                // retried on the next 15-minute tick for the rest of today, not silently skipped
                // until tomorrow. The DB-level ExistsAsync check in RunForLegalEntityAsync makes a
                // retry after partial success safe (already-sent recipients are not re-notified).
                _lastRunLocalDateByLegalEntity[legalEntity.Id] = resolution.WorkDate;
            }
            catch (Exception ex)
            {
                // One legal entity's failure must not stop the rest of this tenant's legal
                // entities (or the rest of this tenant's tick) from being processed - same
                // per-unit isolation as LeaveYearEndEntitlementJob.RunForYearAsync's per-tenant
                // try/catch.
                _logger.LogWarning(ex,
                    "Late clock-in daily summary failed for legal entity {LegalEntityId} in tenant {TenantId}; will retry next tick.",
                    legalEntity.Id, tenant.Id);
            }
        }
    }

    private async Task RunForLegalEntityAsync(
        IServiceProvider services, Guid tenantId, LegalEntity legalEntity, DateOnly workDate, CancellationToken ct)
    {
        var attendance = services.GetRequiredService<IAttendanceReadRepository>();
        var employees = services.GetRequiredService<
            ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        var authority = services.GetRequiredService<IEmployeeAuthorityResolver>();
        var dispatcher = services.GetRequiredService<INotificationDispatcher>();
        var notifications = services.GetRequiredService<INotificationRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var lateRecords = await attendance.ListByStatusAsync(tenantId, workDate, AttendanceRecord.StatusLate, ct);
        if (lateRecords.Count == 0)
            return;

        var employeesById = await employees.ListByIdsAsync(
            tenantId, lateRecords.Select(r => r.EmployeeId).Distinct().ToArray(), ct);

        var legalEntityLateRecords = lateRecords
            .Where(r => employeesById.TryGetValue(r.EmployeeId, out var employee)
                && employee.LegalEntityId == legalEntity.Id)
            .ToList();
        if (legalEntityLateRecords.Count == 0)
            return;

        var byRecipient = new Dictionary<Guid, List<(Employee Employee, AttendanceRecord Record)>>();
        foreach (var record in legalEntityLateRecords)
        {
            var employee = employeesById[record.EmployeeId];
            var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                employee.Id, legalEntity.Id, AttendanceReadPermission,
                EmployeeAuthorityPurpose.AttendanceLateNotification), ct);

            if (!route.IsSuccess || route.Value is null)
            {
                _logger.LogWarning(
                    "No approver resolved for late clock-in employee {EmployeeId} in legal entity {LegalEntityId}; skipping.",
                    employee.Id, legalEntity.Id);
                continue;
            }

            if (!byRecipient.TryGetValue(route.Value.ApproverUserId, out var list))
                byRecipient[route.Value.ApproverUserId] = list = new List<(Employee, AttendanceRecord)>();
            list.Add((employee, record));
        }

        if (byRecipient.Count == 0)
            return;

        var relatedEntityId = BuildRelatedEntityId(legalEntity.Id, workDate);

        await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
        {
            var sentCount = 0;
            foreach (var (recipientUserId, lateForRecipient) in byRecipient)
            {
                var alreadySent = await notifications.ExistsAsync(
                    tenantId, recipientUserId, TemplateCode, RelatedEntityType, relatedEntityId, transactionCt);
                if (alreadySent)
                    continue;

                var lateEmployeeLines = lateForRecipient
                    .Select(x => $"{x.Employee.FirstName} {x.Employee.LastName} ({x.Record.LateMinutes} min)");
                var placeholders = new Dictionary<string, string>
                {
                    ["lateCount"] = lateForRecipient.Count.ToString(),
                    ["lateEmployees"] = string.Join(", ", lateEmployeeLines),
                    ["date"] = workDate.ToString("yyyy-MM-dd"),
                };

                await dispatcher.SendTemplatedAsync(
                    tenantId, recipientUserId, TemplateCode, placeholders,
                    RelatedEntityType, relatedEntityId, transactionCt);
                sentCount++;
            }

            if (sentCount > 0)
                await unitOfWork.SaveChangesAsync(transactionCt);

            return sentCount;
        }, ct);

        _logger.LogInformation(
            "Late clock-in daily summary processed. TenantId={TenantId} LegalEntityId={LegalEntityId} Date={Date} LateEmployees={LateCount} Recipients={RecipientCount}",
            tenantId, legalEntity.Id, workDate, legalEntityLateRecords.Count, byRecipient.Count);
    }

    /// <summary>Deterministic per (legal entity, work date) id used as the notification's
    /// RelatedEntityId, so ExistsAsync can detect "already sent today" without a time-range query
    /// that would need timezone-boundary handling. Not cryptographic - just a stable mix.</summary>
    public static Guid BuildRelatedEntityId(Guid legalEntityId, DateOnly workDate)
    {
        var bytes = legalEntityId.ToByteArray();
        var dayNumberBytes = BitConverter.GetBytes(workDate.DayNumber);
        for (var i = 0; i < dayNumberBytes.Length; i++)
            bytes[i] ^= dayNumberBytes[i];
        return new Guid(bytes);
    }
}
```

- [ ] **Step 4: Run the helper test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LateClockInDailySummaryJobRelatedEntityIdTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Register the job**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, add near the other `AddHostedService<...Job>()` calls (e.g. next to `services.AddHostedService<ONEVO.Infrastructure.Services.Leave.LeaveYearEndEntitlementJob>();` around line 207):

```csharp
services.AddHostedService<ONEVO.Infrastructure.Services.TimeAttendance.LateClockInDailySummaryJob>();
```

- [ ] **Step 6: Build the full solution**

Run: `dotnet build`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJob.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Infrastructure/Services/TimeAttendance/LateClockInDailySummaryJobRelatedEntityIdTests.cs
git commit -m "feat: add LateClockInDailySummaryJob background service"
```

---

### Task 9: Job-level tests with mocked dependencies

**Files:**
- Create: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/LateClockInDailySummaryJobTests.cs`

Follow `tests/ONEVO.Tests.Unit/Features/Leave/LeaveYearEndEntitlementJobTests.cs` exactly: build a plain `ServiceCollection`, register `Mock.Of<T>()`/`Mock<T>` for every dependency the job resolves via `_services.CreateAsyncScope()`, `BuildServiceProvider()`, then construct the job directly with `new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance)` and call `RunTickAsync` — no Testcontainers/WebApplicationFactory/real Postgres needed, matching how every other job in this codebase (`LeaveYearEndEntitlementJob`, and by the same reasoning `ActivityDailySummaryJob`) is tested.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.LegalEntity.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Services.TimeAttendance;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class LateClockInDailySummaryJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunTickAsync_NoActiveTenants_CompletesWithoutError()
    {
        var (provider, _) = BuildProvider(tenants: new List<Tenant>());
        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunTickAsync_SkipsLegalEntity_WhenShiftStartOffsetNotYetReached()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(11, 30)); // due at 13:30 UTC, now is 12:00
        var (provider, mocks) = BuildProvider(legalEntities: new List<LegalEntity> { legalEntity });
        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None);

        mocks.Attendance.Verify(a => a.ListByStatusAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_SendsNotification_ToResolvedApprover_ForLateEmployee()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0)); // due at 11:00 UTC, now is 12:00
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var approverUserId = Guid.NewGuid();
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(
                It.Is<EmployeeApprovalRouteRequest>(r => r.SubjectEmployeeId == lateEmployee.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), approverUserId, Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));
        mocks.Notifications
            .Setup(n => n.ExistsAsync(TenantId, approverUserId, "attendance_late_clockin_daily_summary",
                "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            TenantId, approverUserId, "attendance_late_clockin_daily_summary",
            It.Is<IReadOnlyDictionary<string, string>>(p => p["lateCount"] == "1" && p["lateEmployees"].Contains("Jane Doe")),
            "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTickAsync_DoesNotResend_WhenNotificationAlreadyExists()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var approverUserId = Guid.NewGuid();
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.Success(
                new EmployeeApprovalRoute(Guid.NewGuid(), approverUserId, Guid.NewGuid(),
                    "attendance:read", EmployeeAuthorityPurpose.AttendanceLateNotification,
                    EmployeeApprovalRouteSource.ReportingLine, null)));
        mocks.Notifications
            .Setup(n => n.ExistsAsync(TenantId, approverUserId, "attendance_late_clockin_daily_summary",
                "attendance_late_daily_summary", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already sent

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);
        await job.RunTickAsync(CancellationToken.None);

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunTickAsync_SkipsEmployee_WhenNoApproverResolvableAtAll()
    {
        var legalEntity = CreateLegalEntity(workStartTime: new TimeOnly(9, 0));
        var lateEmployee = CreateEmployee(LegalEntityId, "Jane", "Doe");
        var lateRecord = new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = lateEmployee.Id,
            Date = DateOnly.FromDateTime(UtcNow.UtcDateTime), Status = AttendanceRecord.StatusLate, LateMinutes = 15
        };

        var (provider, mocks) = BuildProvider(
            legalEntities: new List<LegalEntity> { legalEntity },
            lateRecords: new List<AttendanceRecord> { lateRecord },
            employeesById: new Dictionary<Guid, Employee> { [lateEmployee.Id] = lateEmployee });

        mocks.Authority
            .Setup(a => a.ResolveApproverAsync(It.IsAny<EmployeeApprovalRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ONEVO.Application.Common.Models.Result<EmployeeApprovalRoute>.UnprocessableEntity("none"));

        var job = new LateClockInDailySummaryJob(provider, NullLogger<LateClockInDailySummaryJob>.Instance);

        await job.RunTickAsync(CancellationToken.None); // must not throw

        mocks.Dispatcher.Verify(d => d.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static LegalEntity CreateLegalEntity(TimeOnly workStartTime) => new()
    {
        Id = LegalEntityId, TenantId = TenantId, Name = "Test Co", CountryCode = "US", CurrencyCode = "USD",
        IsActive = true, Timezone = "UTC", WorkStartTime = workStartTime, WorkEndTime = workStartTime.AddHours(8),
        StandardWorkingDays = "[1,2,3,4,5,6,7]"
    };

    private static Employee CreateEmployee(Guid legalEntityId, string firstName, string lastName) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), LegalEntityId = legalEntityId,
        EmployeeNumber = $"EMP-{Guid.NewGuid():N}"[..12], FirstName = firstName, LastName = lastName,
        Email = $"{firstName}.{lastName}@example.test", HireDate = new DateOnly(2026, 1, 1)
    };

    private sealed record Mocks(
        Mock<IAttendanceReadRepository> Attendance,
        Mock<IEmployeeAuthorityResolver> Authority,
        Mock<INotificationDispatcher> Dispatcher,
        Mock<INotificationRepository> Notifications);

    private static (IServiceProvider Provider, Mocks Mocks) BuildProvider(
        List<Tenant>? tenants = null,
        List<LegalEntity>? legalEntities = null,
        List<AttendanceRecord>? lateRecords = null,
        Dictionary<Guid, Employee>? employeesById = null)
    {
        tenants ??= new List<Tenant> { new() { Id = TenantId, Slug = "test-co", Status = TenantStatus.Active } };
        legalEntities ??= new List<LegalEntity>();
        lateRecords ??= new List<AttendanceRecord>();
        employeesById ??= new Dictionary<Guid, Employee>();

        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(t => t.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants);

        var legalEntityRepo = new Mock<ILegalEntityRepository>();
        legalEntityRepo.Setup(r => r.ListActiveForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntities);

        var attendanceRepo = new Mock<IAttendanceReadRepository>();
        attendanceRepo.Setup(r => r.ListByStatusAsync(
                TenantId, It.IsAny<DateOnly>(), AttendanceRecord.StatusLate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lateRecords);

        var employeeRepo = new Mock<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>();
        employeeRepo.Setup(r => r.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<Guid, Employee>)employeesById);

        var authority = new Mock<IEmployeeAuthorityResolver>();
        var dispatcher = new Mock<INotificationDispatcher>();
        var notifications = new Mock<INotificationRepository>();
        notifications.Setup(n => n.ExistsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<int>>, CancellationToken>((op, ct) => op(ct));

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(UtcNow);

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IWritableTenantContext>());
        services.AddSingleton(tenantRepo.Object);
        services.AddSingleton(legalEntityRepo.Object);
        services.AddSingleton(attendanceRepo.Object);
        services.AddSingleton(employeeRepo.Object);
        services.AddSingleton(authority.Object);
        services.AddSingleton(dispatcher.Object);
        services.AddSingleton(notifications.Object);
        services.AddSingleton(unitOfWork.Object);
        services.AddSingleton(clock.Object);
        services.AddSingleton(Mock.Of<ITenantContextSwitcher>());

        return (services.BuildServiceProvider(), new Mocks(attendanceRepo, authority, dispatcher, notifications));
    }
}
```

Confirm the exact static factory method names on `Result<T>` (`Success`, `UnprocessableEntity`) against `ONEVO.Application.Common.Models.Result<T>` before running — they are used the same way in `EmployeeAuthorityResolver.cs` itself (e.g. `Result<EmployeeApprovalRoute>.Success(...)`, `Result<EmployeeApprovalRoute>.UnprocessableEntity(...)`), so copy that exact call shape.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LateClockInDailySummaryJobTests"`
Expected: FAIL to compile until Tasks 1-8 are all in place (this test exercises the real job class and every repository method added in this plan).

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LateClockInDailySummaryJobTests"`
Expected: PASS (5 tests)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: All tests pass, no regressions.

- [ ] **Step 5: Commit**

```bash
git add tests/ONEVO.Tests.Unit/Features/TimeAttendance/LateClockInDailySummaryJobTests.cs
git commit -m "test: add LateClockInDailySummaryJob unit tests with mocked dependencies"
```

---

## Out of scope for this plan (explicitly deferred)

- No-show / never-clocked-in detection and notification.
- Retroactive inclusion of late clock-ins that happen after the daily job has already run for that legal entity/day.
- Any TrayApp UI changes.
- Email delivery (the SharedPlatform notification system's mail half is already a known no-op elsewhere in the codebase — out of scope here too).
- Making `ClockInPolicy.NotificationRecipientResolver` actually pluggable/read anywhere (it stays an unused config field, same as today, since this feature never reads it).
