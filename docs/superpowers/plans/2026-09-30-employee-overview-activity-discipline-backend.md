# Employee Overview — Activity & Discipline Comparison (Plan 3B, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add the `overview/activity` endpoint (active / idle / meeting time, with previous-period comparison) and retrofit `overview/attendance-discipline` (Plan 2) with the same `compare=previous` support.

**Architecture:** The activity handler sums persisted `ActivityDailySummary` rows for the period (gated by the employee's activity-monitoring toggle). The discipline handler is refactored so one private `MeasureAsync(period)` produces the metrics and is called once, or twice when comparing.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md`. **Prerequisite:** Plan 3A Task 1 committed (`EmployeePeriod.Previous()`, `EmployeeOverviewCompare.Parse`). Plan 2 backend is already committed.

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Before every commit: `git branch --show-current` and `git status --short`; stage only the task's files. **No git worktrees.**
- Build/test per project. Before **each** `dotnet test`: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell).
- `compare`: omitted/`none` → off, `previous` → on, else 400 (`EmployeeOverviewCompare.Parse`). Previous period = `EmployeePeriod.Previous()`.
- Activity gate: route `[RequirePermission("employees:read")]` + coverage guard, then `monitoring:read` **or** viewing your own record (`EmployeeOverviewAccess`). If `MonitoringCapability.ActivityMonitoring` is not enabled for the employee the response is `activityMonitoringEnabled: false` with zero minutes and **no** previous (no summary rows are read).
- Activity numbers come from persisted daily summaries only (`IActivityDailySummaryRepository.GetRangeAsync`); a day appears once its summary exists. `daysWithData` counts summaries with any active/idle/meeting minutes.
- Existing `GetEmployeeAttendanceDisciplineQuery(EmployeeId, From, To)` construction in existing tests must keep compiling: the new `Compare` parameter is trailing and optional; the new response field `Previous` is trailing and optional.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: `overview/activity` endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeActivityOverviewResponses.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeOverview/GetEmployeeActivityOverview/GetEmployeeActivityOverviewQuery.cs`
- Create: `src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeOverview/GetEmployeeActivityOverview/GetEmployeeActivityOverviewQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/GetEmployeeActivityOverviewQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard`, `EmployeePeriod`, `EmployeeOverviewAccess`, `EmployeeOverviewCompare`; `IActivityDailySummaryRepository.GetRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)`; `IMonitoringToggleResolver.IsEnabledAsync(Guid tenantId, Guid employeeId, MonitoringCapability capability, CancellationToken ct = default)`; `ActivityDailySummary.TotalActiveMinutes/TotalIdleMinutes/TotalMeetingMinutes`.
- Produces:
  - `EmployeeActivityMetrics(int ActiveMinutes, int IdleMinutes, int MeetingMinutes, int DaysWithData)`
  - `EmployeeActivityOverviewResponse(DateOnly From, DateOnly To, bool ActivityMonitoringEnabled, int ActiveMinutes, int IdleMinutes, int MeetingMinutes, int DaysWithData, EmployeeActivityMetrics? Previous)`
  - `GetEmployeeActivityOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)`
  - `GET /api/v1/employees/{id}/overview/activity?from&to&compare=previous`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Monitoring;

public sealed class GetEmployeeActivityOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IActivityDailySummaryRepository> _summaries = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeActivityOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private GetEmployeeActivityOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _summaries.Object, _toggles.Object, _user.Object, _clock.Object);

    private ActivityDailySummary Day(int active, int idle, int meeting) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = SepFrom,
        TotalActiveMinutes = active, TotalIdleMinutes = idle, TotalMeetingMinutes = meeting
    };

    private void ArrangeSummaries(DateOnly from, DateOnly to, params ActivityDailySummary[] rows) =>
        _summaries.Setup(s => s.GetRangeAsync(_tenantId, _employeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksMonitoringReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AllowsSelfWithoutMonitoringRead()
    {
        _user.Setup(u => u.HasPermission("monitoring:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        ArrangeSummaries(SepFrom, SepTo);

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SumsMinutesAndCountsDaysWithData()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(150, 72, 65), Day(200, 30, 0), Day(0, 0, 0));

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        var v = result.Value!;
        v.ActivityMonitoringEnabled.Should().BeTrue();
        v.ActiveMinutes.Should().Be(350);
        v.IdleMinutes.Should().Be(102);
        v.MeetingMinutes.Should().Be(65);
        v.DaysWithData.Should().Be(2);
        v.Previous.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsZeroesWithoutReadingSummaries_WhenMonitoringIsDisabled()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeFalse();
        result.Value.ActiveMinutes.Should().Be(0);
        result.Value.Previous.Should().BeNull();
        _summaries.Verify(s => s.GetRangeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_ReadsThePreviousMonthToo()
    {
        ArrangeSummaries(SepFrom, SepTo, Day(100, 10, 5));
        ArrangeSummaries(AugFrom, AugTo, Day(300, 40, 20), Day(100, 10, 0));

        var result = await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.ActiveMinutes.Should().Be(100);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.ActiveMinutes.Should().Be(400);
        result.Value.Previous.IdleMinutes.Should().Be(50);
        result.Value.Previous.MeetingMinutes.Should().Be(20);
        result.Value.Previous.DaysWithData.Should().Be(2);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValueOrInvalidPeriod()
    {
        (await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, null, null, "x"), CancellationToken.None))
            .StatusCode.Should().Be(400);
        (await CreateHandler().Handle(new GetEmployeeActivityOverviewQuery(_employeeId, SepTo, SepFrom), CancellationToken.None))
            .StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeActivityOverviewQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 3: Write DTOs, query and handler**

`EmployeeActivityOverviewResponses.cs`:

```csharp
namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record EmployeeActivityMetrics(
    int ActiveMinutes,
    int IdleMinutes,
    int MeetingMinutes,
    int DaysWithData);

/// <summary>Sums of the persisted daily activity summaries in the period. When activity monitoring
/// is not enabled for the employee, ActivityMonitoringEnabled is false and everything is zero.</summary>
public sealed record EmployeeActivityOverviewResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    int ActiveMinutes,
    int IdleMinutes,
    int MeetingMinutes,
    int DaysWithData,
    EmployeeActivityMetrics? Previous);
```

`GetEmployeeActivityOverviewQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;

public sealed record GetEmployeeActivityOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeActivityOverviewResponse>>;
```

`GetEmployeeActivityOverviewQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;

public sealed class GetEmployeeActivityOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IActivityDailySummaryRepository summaries,
    IMonitoringToggleResolver toggles,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeActivityOverviewQuery, Result<EmployeeActivityOverviewResponse>>
{
    public const string ModulePermission = "monitoring:read";

    public async Task<Result<EmployeeActivityOverviewResponse>> Handle(
        GetEmployeeActivityOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeActivityOverviewResponse>.Forbidden("You do not have access to this employee's activity.");

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeActivityOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var enabled = await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct);
        if (!enabled)
        {
            return Result<EmployeeActivityOverviewResponse>.Success(new EmployeeActivityOverviewResponse(
                period.Value!.From, period.Value.To, false, 0, 0, 0, 0, null));
        }

        var current = await MeasureAsync(tenantId, request.EmployeeId, period.Value!, ct);
        EmployeeActivityMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, period.Value!.Previous(), ct)
            : null;

        return Result<EmployeeActivityOverviewResponse>.Success(new EmployeeActivityOverviewResponse(
            period.Value!.From, period.Value.To, true,
            current.ActiveMinutes, current.IdleMinutes, current.MeetingMinutes, current.DaysWithData, previous));
    }

    private async Task<EmployeeActivityMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, EmployeePeriod period, CancellationToken ct)
    {
        var rows = await summaries.GetRangeAsync(tenantId, employeeId, period.From, period.To, ct);
        return new EmployeeActivityMetrics(
            rows.Sum(r => r.TotalActiveMinutes),
            rows.Sum(r => r.TotalIdleMinutes),
            rows.Sum(r => r.TotalMeetingMinutes),
            rows.Count(r => r.TotalActiveMinutes + r.TotalIdleMinutes + r.TotalMeetingMinutes > 0));
    }
}
```

- [ ] **Step 4: Run to verify the handler tests pass**

Run: same command as Step 2. Expected: 6 passed.

- [ ] **Step 5: Extend the architecture theory, watch it fail, add the action**

Add to the `[Theory]` in `EmployeesControllerArchitectureTests.cs`:

```csharp
    [InlineData("overview/activity", "GetOverviewActivity")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add the using

```csharp
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;
```

and after `GetOverviewDelivery`:

```csharp
    /// <summary>Overview activity panel: active / idle / meeting minutes summed from the persisted
    /// daily summaries; with compare=previous also the previous period. Reports
    /// activityMonitoringEnabled=false (and zeros) when monitoring is off for the employee.</summary>
    [HttpGet("{id:guid}/overview/activity")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewActivity(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeActivityOverviewQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 6: Run both suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/DTOs/Responses/EmployeeActivityOverviewResponses.cs src/ONEVO.Application/Features/Monitoring/ActivityMonitoring/Queries/EmployeeOverview src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/Monitoring/GetEmployeeActivityOverviewQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview activity endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Discipline `compare=previous`

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQuery.cs`
- Modify (full rewrite): `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (`GetOverviewAttendanceDiscipline`)
- Test (new): `tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceDisciplineCompareTests.cs`
- Existing tests that must stay green: `GetEmployeeAttendanceDisciplineQueryHandlerTests`

**Interfaces:**
- Produces: `EmployeeAttendanceDisciplineMetrics(int LateClockIns, int EarlyClockOuts, int MissingClockOuts, int OverBreakDays, int OverBreakMinutes, int? LocationViolations)`; `EmployeeAttendanceDisciplineResponse` gains trailing `EmployeeAttendanceDisciplineMetrics? Previous = null`; `GetEmployeeAttendanceDisciplineQuery` gains trailing `string? Compare = null`; the endpoint gains `compare` query param.

- [ ] **Step 1: Write the failing compare tests**

`GetEmployeeAttendanceDisciplineCompareTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class GetEmployeeAttendanceDisciplineCompareTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeAttendanceDisciplineCompareTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 21));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
    }

    private GetEmployeeAttendanceDisciplineQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _reader.Object, _toggles.Object, _notifications.Object, _user.Object, _clock.Object);

    private AttendanceRecord Late(DateOnly d) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = new DateTimeOffset(d.Year, d.Month, d.Day, 4, 30, 0, TimeSpan.Zero),   // 10:00 Colombo = late
        ActualEnd = new DateTimeOffset(d.Year, d.Month, d.Day, 12, 0, 0, TimeSpan.Zero)
    };

    private void ArrangePeriod(DateOnly from, DateOnly to, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.Is<EmployeePeriod>(p => p.From == from && p.To == to), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-09-21T00:00:00+00:00"), new DateOnly(2026, 9, 21), 60,
                new Dictionary<DateOnly, int>(), Array.Empty<LeaveRequest>(), DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

    [Fact]
    public async Task Handle_WithoutCompare_HasNoPrevious_AndLoadsOnlyTheCurrentPeriod()
    {
        ArrangePeriod(SepFrom, SepTo, Late(new DateOnly(2026, 9, 2)));

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        result.Value!.Previous.Should().BeNull();
        _reader.Verify(r => r.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_MeasuresThePreviousMonthWithTheSameRules()
    {
        ArrangePeriod(SepFrom, SepTo, Late(new DateOnly(2026, 9, 2)));
        ArrangePeriod(AugFrom, AugTo, Late(new DateOnly(2026, 8, 3)), Late(new DateOnly(2026, 8, 4)), Late(new DateOnly(2026, 8, 5)));

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.LateClockIns.Should().Be(1);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.LateClockIns.Should().Be(3);
        result.Value.Previous.LocationViolations.Should().Be(2);
    }

    [Fact]
    public async Task Handle_PreviousLocationViolationsAreNull_WhenTrackingIsOff()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ArrangePeriod(SepFrom, SepTo);
        ArrangePeriod(AugFrom, AugTo);

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.LocationViolations.Should().BeNull();
        result.Value.Previous!.LocationViolations.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValue()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, SepFrom, SepTo, "last-year"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceDisciplineCompareTests"`
Expected: build FAIL — the query has no `Compare` parameter and the response has no `Previous`.

- [ ] **Step 3: Extend the response and query**

In `EmployeeAttendanceOverviewResponses.cs`, replace the existing `EmployeeAttendanceDisciplineResponse` record (keep its doc comment) with:

```csharp
public sealed record EmployeeAttendanceDisciplineMetrics(
    int LateClockIns,
    int EarlyClockOuts,
    int MissingClockOuts,
    int OverBreakDays,
    int OverBreakMinutes,
    int? LocationViolations);

public sealed record EmployeeAttendanceDisciplineResponse(
    DateOnly From,
    DateOnly To,
    int LateClockIns,
    int EarlyClockOuts,
    int MissingClockOuts,
    int OverBreakDays,
    int OverBreakMinutes,
    bool LocationTrackingEnabled,
    int? LocationViolations,
    EmployeeAttendanceDisciplineMetrics? Previous = null);
```

Replace `GetEmployeeAttendanceDisciplineQuery.cs` with:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed record GetEmployeeAttendanceDisciplineQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeAttendanceDisciplineResponse>>;
```

- [ ] **Step 4: Rewrite the handler**

Replace the whole of `GetEmployeeAttendanceDisciplineQueryHandler.cs` with:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed class GetEmployeeAttendanceDisciplineQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeAttendancePeriodReader reader,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceDisciplineQuery, Result<EmployeeAttendanceDisciplineResponse>>
{
    public const string ModulePermission = "attendance:read";

    public async Task<Result<EmployeeAttendanceDisciplineResponse>> Handle(
        GetEmployeeAttendanceDisciplineQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeAttendanceDisciplineResponse>.Forbidden("You do not have access to this employee's attendance.");

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var legalEntityId = access.Value!.LegalEntityId;
        var trackingEnabled = await toggles.IsEnabledAsync(
            tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct);

        var current = await MeasureAsync(tenantId, request.EmployeeId, legalEntityId, period.Value!, trackingEnabled, ct);
        EmployeeAttendanceDisciplineMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, legalEntityId, period.Value!.Previous(), trackingEnabled, ct)
            : null;

        return Result<EmployeeAttendanceDisciplineResponse>.Success(new EmployeeAttendanceDisciplineResponse(
            period.Value!.From,
            period.Value.To,
            current.LateClockIns,
            current.EarlyClockOuts,
            current.MissingClockOuts,
            current.OverBreakDays,
            current.OverBreakMinutes,
            trackingEnabled,
            current.LocationViolations,
            previous));
    }

    private async Task<EmployeeAttendanceDisciplineMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, bool trackingEnabled, CancellationToken ct)
    {
        var data = await reader.LoadAsync(tenantId, employeeId, legalEntityId, period, ct);
        var counts = AttendancePeriodCalculator.Count(data.Records, data.Timezone, data.Now);

        var overBreakDays = 0;
        var overBreakMinutes = 0;
        if (data.BreakAllowanceMinutes is int allowance)
        {
            foreach (var record in data.Records)
            {
                if (data.BreakMinutesByDate.TryGetValue(record.Date, out var used) && used > allowance)
                {
                    overBreakDays += 1;
                    overBreakMinutes += used - allowance;
                }
            }
        }

        int? locationViolations = trackingEnabled
            ? await notifications.CountByTypeAsync(
                tenantId, employeeId, NotificationType.OutsideWorkLocationAlert,
                data.RangeStartUtc, data.RangeEndUtc, ct)
            : null;

        return new EmployeeAttendanceDisciplineMetrics(
            counts.LateArrivals, counts.EarlyDepartures, counts.MissingClockOuts,
            overBreakDays, overBreakMinutes, locationViolations);
    }
}
```

- [ ] **Step 5: Add `compare` to the controller action**

In `EmployeesController.cs`, replace the body/signature of `GetOverviewAttendanceDiscipline` so it reads:

```csharp
    public async Task<IActionResult> GetOverviewAttendanceDiscipline(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceDisciplineQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

(keep the existing `[HttpGet("{id:guid}/overview/attendance-discipline")]` and `[RequirePermission("employees:read")]` attributes and the doc comment above it).

- [ ] **Step 6: Run the old and new discipline tests, then both suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceDiscipline"
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: the existing `GetEmployeeAttendanceDisciplineQueryHandlerTests` (5) and the new compare tests (4) pass, then both suites are fully green. If an existing discipline test fails, the refactor changed behaviour — fix the handler, never the old test.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceDisciplineCompareTests.cs
git commit -m "feat(people): add previous-period comparison to attendance discipline

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- Activity panel (active / idle / meeting time), "Not monitored" when the toggle is off, previous-period comparison: Task 1. Discipline comparison retrofit (late, early, missing, over-break, location): Task 2.
- Type consistency: `EmployeeActivityMetrics` fields = handler `MeasureAsync` output = response fields; `EmployeeAttendanceDisciplineMetrics` (T2) matches the handler's `MeasureAsync` and the test's `Previous!.LateClockIns/LocationViolations`; `EmployeePeriod.Previous()` / `EmployeeOverviewCompare.Parse` come from Plan 3A T1.
- Verified by reading source: `IActivityDailySummaryRepository.GetRangeAsync` (ct is required, no default), `ActivityDailySummary` field names, `IMonitoringToggleResolver.IsEnabledAsync(tenantId, employeeId, capability, ct)`, the committed discipline handler/query/response shapes (rewritten verbatim above except the compare additions). Not compile-checked; the executor's first `dotnet test` is the first compile.
- Known limits: activity needs the daily-summary job to have run (today's partial day appears later); the existing monitoring `daily-range` endpoint's 31-day cap does not apply here (this handler reads the repository directly, up to 366 days).
- Not here: approvals (Plan 3C backend); frontend (3A/3B).
