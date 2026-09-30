# Employee Overview — Attendance, Discipline & Time-Off (Plan 2, backend half)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three per-widget endpoints for the employee Overview tab — `overview/attendance`, `overview/attendance-discipline`, `overview/time-off` — plus the shared period plumbing (`from`/`to` defaulting to the current month, capped at 12 months) they need.

**Architecture:** `EmployeePeriod` resolves/validates the date range. A pure `AttendancePeriodCalculator` holds the late/early/missing-clock-out/day-status rules (the existing "my monthly summary" handler is refactored to delegate to it so the rules cannot drift). An `IEmployeeAttendancePeriodReader` loads records, breaks, leave and the legal-entity break allowance once; two thin handlers turn that into the attendance summary and the discipline card. A third handler reuses the existing leave balance mapping for time-off. Every handler runs the Plan 1 `IEmployeeReadAccessGuard` first (404/403), then a per-module permission check (module permission **or** viewing your own record).

**Tech Stack:** .NET / MediatR / EF Core / xUnit + Moq + FluentAssertions (attendance tests use FluentAssertions; follow the file you are in).

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md`. Depends on Plan 1 backend (`IEmployeeReadAccessGuard`, commit `937aba43`). Frontend half: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-09-30-employee-overview-attendance-timeoff-frontend.md`.

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Run `git branch --show-current` and `git status --short` right before every commit (other sessions share this tree) and stage only the files listed in the task.
- **No git worktrees.** No `.sln`: build/test per project (`dotnet test tests/ONEVO.Tests.Unit`, `dotnet test tests/ONEVO.Tests.Architecture`).
- `ONEVO.Api.exe` auto-respawns: run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell) immediately before **each** `dotnet test`/`dotnet build`.
- Period rules (single source: `EmployeePeriod`): `from`/`to` both omitted → current calendar month; only one given → 400; `from > to` → 400; inclusive span > **366** days → 400.
- Permissions: route `[RequirePermission("employees:read")]` + guard (coverage). Then attendance endpoints need `attendance:read`, time-off needs `leave:read` — **or** the caller is viewing their own employee record. A failed module check returns 403 (the frontend renders that widget as "no access").
- Location violations are counted only when `MonitoringCapability.WorkLocationVerification` is enabled for that employee; otherwise the field is `null` and `locationTrackingEnabled` is `false`. The count is `OutsideWorkLocationAlert` notifications (written by `LocationRuleEvaluatorJob`, 6-hour cooldown), i.e. alert events, not GPS samples.
- Break allowance is `LegalEntity.BreakDurationMinutes` (same source the existing history rows use). Null allowance ⇒ no over-break counting.
- Leave balances stay in **hours** (no hours-per-day constant exists in the codebase; do not invent one).
- Existing endpoints and DTOs are not changed, except that `GetMyAttendanceMonthlySummary` delegates its counting to the new calculator (behaviour identical; protected by `AttendanceReadHandlerTests.MonthlySummary_*`).
- Two `INotificationRepository` interfaces exist (Monitoring and SharedPlatform). This plan only touches the **Monitoring** one (`ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces`).
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: `EmployeePeriod` and `EmployeeOverviewAccess`

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeePeriod.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewAccess.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeePeriodTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeOverviewAccessTests.cs`

**Interfaces:**
- Produces:
  - `sealed record EmployeePeriod(DateOnly From, DateOnly To)` with `const int MaxDays = 366` and `static Result<EmployeePeriod> Resolve(DateOnly? from, DateOnly? to, DateOnly today)`.
  - `static class EmployeeOverviewAccess { static Task<bool> HasAccessAsync(ICurrentUser user, IEmployeeRepository employees, Guid tenantId, Guid employeeId, string permission, CancellationToken ct) }` — true if the caller holds `permission` or the caller's own default employee id equals `employeeId`. (`IEmployeeRepository` here is `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository`.) Used by Tasks 4-6.

- [ ] **Step 1: Write the failing tests**

`EmployeePeriodTests.cs`:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeePeriodTests
{
    [Fact]
    public void Resolve_DefaultsToTheCurrentCalendarMonth_WhenBothOmitted()
    {
        var result = EmployeePeriod.Resolve(null, null, new DateOnly(2026, 9, 30));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value!.From);
        Assert.Equal(new DateOnly(2026, 9, 30), result.Value.To);
    }

    [Fact]
    public void Resolve_DefaultUsesTheRealLastDayOfShortMonths()
    {
        var result = EmployeePeriod.Resolve(null, null, new DateOnly(2026, 2, 10));

        Assert.Equal(new DateOnly(2026, 2, 28), result.Value!.To);
    }

    [Fact]
    public void Resolve_UsesTheGivenRange()
    {
        var result = EmployeePeriod.Resolve(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value!.From);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Value.To);
    }

    [Fact]
    public void Resolve_Fails400_WhenOnlyOneBoundIsGiven()
    {
        var onlyFrom = EmployeePeriod.Resolve(new DateOnly(2026, 8, 1), null, new DateOnly(2026, 9, 30));
        var onlyTo = EmployeePeriod.Resolve(null, new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));

        Assert.False(onlyFrom.IsSuccess);
        Assert.Equal(400, onlyFrom.StatusCode);
        Assert.False(onlyTo.IsSuccess);
        Assert.Equal(400, onlyTo.StatusCode);
    }

    [Fact]
    public void Resolve_Fails400_WhenFromIsAfterTo()
    {
        var result = EmployeePeriod.Resolve(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void Resolve_AllowsExactly366Days_AndRejects367()
    {
        var ok = EmployeePeriod.Resolve(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), new DateOnly(2026, 9, 30));
        var tooLong = EmployeePeriod.Resolve(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2), new DateOnly(2026, 9, 30));

        Assert.True(ok.IsSuccess);
        Assert.False(tooLong.IsSuccess);
        Assert.Equal(400, tooLong.StatusCode);
    }
}
```

`EmployeeOverviewAccessTests.cs`:

```csharp
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeOverviewAccessTests
{
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public EmployeeOverviewAccessTests()
    {
        _user.SetupGet(u => u.UserId).Returns(_userId);
    }

    [Fact]
    public async Task HasAccess_True_WhenCallerHoldsThePermission_WithoutLookingUpTheCaller()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.True(result);
        _employees.Verify(e => e.GetDefaultForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HasAccess_True_WhenTheCallerIsViewingTheirOwnRecord()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_False_WhenNeitherPermissionNorSelf()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccess_False_WhenTheCallerHasNoEmployeeRecord()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await EmployeeOverviewAccess.HasAccessAsync(
            _user.Object, _employees.Object, _tenantId, _employeeId, "attendance:read", CancellationToken.None);

        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeePeriodTests|FullyQualifiedName~EmployeeOverviewAccessTests"`
Expected: build FAIL — `EmployeePeriod` / `EmployeeOverviewAccess` do not exist.

- [ ] **Step 3: Write `EmployeePeriod`**

`EmployeePeriod.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>
/// The inclusive date range every period-aware employee Overview widget reads. Omitted bounds
/// default to the current calendar month; a range is capped at 12 months (366 days) so one
/// widget request cannot scan unbounded history.
/// </summary>
public sealed record EmployeePeriod(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;

    public static Result<EmployeePeriod> Resolve(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is null && to is null)
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            return Result<EmployeePeriod>.Success(new EmployeePeriod(first, last));
        }

        if (from is null || to is null)
            return Result<EmployeePeriod>.Failure("from and to must be provided together.");

        if (from.Value > to.Value)
            return Result<EmployeePeriod>.Failure("from must be less than or equal to to.");

        if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
            return Result<EmployeePeriod>.Failure("The period cannot exceed 12 months.");

        return Result<EmployeePeriod>.Success(new EmployeePeriod(from.Value, to.Value));
    }
}
```

- [ ] **Step 4: Write `EmployeeOverviewAccess`**

`EmployeeOverviewAccess.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>
/// Per-widget module gate for the employee Overview: the caller passes if they hold the module
/// permission, or if they are looking at their own employee record (self-service needs no
/// extra permission). Coverage/visibility is checked separately by IEmployeeReadAccessGuard.
/// </summary>
public static class EmployeeOverviewAccess
{
    public static async Task<bool> HasAccessAsync(
        ICurrentUser user,
        IEmployeeRepository employees,
        Guid tenantId,
        Guid employeeId,
        string permission,
        CancellationToken ct)
    {
        if (user.HasPermission(permission))
            return true;

        var caller = await employees.GetDefaultForUserAsync(tenantId, user.UserId, ct);
        return caller is not null && caller.Id == employeeId;
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeePeriodTests|FullyQualifiedName~EmployeeOverviewAccessTests"`
Expected: 10 passed (6 + 4).

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeePeriod.cs src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewAccess.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeePeriodTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeOverviewAccessTests.cs
git commit -m "feat(people): add employee overview period and access helpers

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `AttendancePeriodCalculator` (and delegate the existing monthly summary to it)

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` (the `GetMyAttendanceMonthlySummaryQuery` handler, lines ~76-135)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorTests.cs`
- Existing test that must stay green: `AttendanceReadHandlerTests.MonthlySummary_CountsPresentLateEarlyAndMissingClockOut`

**Interfaces:**
- Produces (namespace `ONEVO.Application.Features.TimeAttendance.Services`):
  - `sealed record AttendancePeriodCounts(int WorkingDays, int DaysPresent, int LateArrivals, int EarlyDepartures, int MissingClockOuts)`
  - `static class AttendancePeriodCalculator`:
    - `TimeZoneInfo ResolveTimezone(string? timezone)` — UTC on null/blank/unknown id
    - `AttendancePeriodCounts Count(IReadOnlyList<AttendanceRecord> records, TimeZoneInfo timezone, DateTimeOffset now)`
    - `string DayStatus(AttendanceRecord record, TimeZoneInfo timezone, DateTimeOffset now, DateOnly today, bool hasApprovedLeave)` → one of `present | late | missing_clock_out | leave | off | absent | none`
    - `bool CoversDate(LeaveRequest leave, DateOnly date)` — UTC-date comparison, identical to `BuildRowsAsync`.
  Used by Tasks 4-5.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodCalculatorTests
{
    private static readonly TimeZoneInfo Colombo =
        TimeZoneInfo.CreateCustomTimeZone("test-colombo", TimeSpan.FromHours(5.5), "test", "test");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T00:00:00+00:00");
    private static readonly DateOnly Today = new(2026, 8, 21);

    private static AttendanceRecord Record(DateOnly date, string? actualStartUtc = null, string? actualEndUtc = null, bool working = true) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Date = date,
            ExpectedWorkingDay = working,
            ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
            ActualStart = actualStartUtc is null ? null : DateTimeOffset.Parse(actualStartUtc),
            ActualEnd = actualEndUtc is null ? null : DateTimeOffset.Parse(actualEndUtc)
        };

    private static readonly AttendanceRecord OnTime = Record(new(2026, 8, 3), "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00");
    private static readonly AttendanceRecord Late = Record(new(2026, 8, 4), "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00");
    private static readonly AttendanceRecord EarlyOut = Record(new(2026, 8, 5), "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00");
    private static readonly AttendanceRecord MissingOut = Record(new(2026, 8, 6), "2026-08-06T03:30:00+00:00");
    private static readonly AttendanceRecord NeverIn = Record(new(2026, 8, 7));

    [Fact]
    public void Count_MatchesTheMonthlySummaryRules()
    {
        var counts = AttendancePeriodCalculator.Count(new[] { OnTime, Late, EarlyOut, MissingOut, NeverIn }, Colombo, Now);

        counts.WorkingDays.Should().Be(5);
        counts.DaysPresent.Should().Be(4);
        counts.LateArrivals.Should().Be(1);
        counts.EarlyDepartures.Should().Be(1);
        counts.MissingClockOuts.Should().Be(1);
    }

    [Fact]
    public void Count_OfNothing_IsAllZero()
    {
        var counts = AttendancePeriodCalculator.Count(Array.Empty<AttendanceRecord>(), Colombo, Now);

        counts.Should().Be(new AttendancePeriodCounts(0, 0, 0, 0, 0));
    }

    [Fact]
    public void DayStatus_ClassifiesEveryDay()
    {
        AttendancePeriodCalculator.DayStatus(OnTime, Colombo, Now, Today, false).Should().Be("present");
        AttendancePeriodCalculator.DayStatus(Late, Colombo, Now, Today, false).Should().Be("late");
        AttendancePeriodCalculator.DayStatus(EarlyOut, Colombo, Now, Today, false).Should().Be("present");
        AttendancePeriodCalculator.DayStatus(MissingOut, Colombo, Now, Today, false).Should().Be("missing_clock_out");
        AttendancePeriodCalculator.DayStatus(NeverIn, Colombo, Now, Today, false).Should().Be("absent");
    }

    [Fact]
    public void DayStatus_LeaveBeatsAbsent_OffDayIsOff_AndTodayNotYetInIsNone()
    {
        AttendancePeriodCalculator.DayStatus(NeverIn, Colombo, Now, Today, hasApprovedLeave: true).Should().Be("leave");
        AttendancePeriodCalculator.DayStatus(Record(new(2026, 8, 8), working: false), Colombo, Now, Today, false).Should().Be("off");
        AttendancePeriodCalculator.DayStatus(Record(Today), Colombo, Now, Today, false).Should().Be("none");
    }

    [Fact]
    public void DayStatus_WorkedThroughApprovedLeave_StillCountsAsPresent()
    {
        AttendancePeriodCalculator.DayStatus(OnTime, Colombo, Now, Today, hasApprovedLeave: true).Should().Be("present");
    }

    [Fact]
    public void CoversDate_IsInclusiveOnBothEnds_UsingUtcDates()
    {
        var leave = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            StartAt = DateTimeOffset.Parse("2026-08-10T00:00:00+00:00"),
            EndAt = DateTimeOffset.Parse("2026-08-12T23:59:59+00:00")
        };

        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 9)).Should().BeFalse();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 10)).Should().BeTrue();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 12)).Should().BeTrue();
        AttendancePeriodCalculator.CoversDate(leave, new DateOnly(2026, 8, 13)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Zone")]
    public void ResolveTimezone_FallsBackToUtc(string? id)
    {
        AttendancePeriodCalculator.ResolveTimezone(id).Should().Be(TimeZoneInfo.Utc);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AttendancePeriodCalculatorTests"`
Expected: build FAIL — `AttendancePeriodCalculator` does not exist.

- [ ] **Step 3: Write the calculator**

`AttendancePeriodCalculator.cs`:

```csharp
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record AttendancePeriodCounts(
    int WorkingDays,
    int DaysPresent,
    int LateArrivals,
    int EarlyDepartures,
    int MissingClockOuts);

/// <summary>
/// The late / early-departure / missing-clock-out rules behind every attendance summary. Moved
/// out of AttendanceReadHandler's monthly summary so the employee Overview cards and the
/// self-service summary can never disagree. Display-only rules: zero grace, deliberately not
/// coupled to ClockInPolicy.LateArrivalMinute payroll tiers.
/// </summary>
public static class AttendancePeriodCalculator
{
    private static readonly TimeSpan LateOrEarlyGrace = TimeSpan.Zero;

    public static TimeZoneInfo ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static AttendancePeriodCounts Count(
        IReadOnlyList<AttendanceRecord> records, TimeZoneInfo timezone, DateTimeOffset now)
    {
        var workingDays = 0;
        var daysPresent = 0;
        var late = 0;
        var early = 0;
        var missing = 0;

        foreach (var record in records)
        {
            if (record.ExpectedWorkingDay)
                workingDays += 1;

            if (record.ActualStart is null)
                continue;

            daysPresent += 1;
            if (IsLate(record, timezone)) late += 1;
            if (IsEarlyDeparture(record, timezone)) early += 1;
            if (IsMissingClockOut(record, now)) missing += 1;
        }

        return new AttendancePeriodCounts(workingDays, daysPresent, late, early, missing);
    }

    public static string DayStatus(
        AttendanceRecord record, TimeZoneInfo timezone, DateTimeOffset now, DateOnly today, bool hasApprovedLeave)
    {
        if (record.ActualStart is not null)
        {
            if (IsMissingClockOut(record, now)) return "missing_clock_out";
            if (IsLate(record, timezone)) return "late";
            return "present";
        }

        if (hasApprovedLeave) return "leave";
        if (!record.ExpectedWorkingDay || record.IsHoliday) return "off";
        return record.Date < today ? "absent" : "none";
    }

    public static bool CoversDate(LeaveRequest leave, DateOnly date) =>
        DateOnly.FromDateTime(leave.StartAt.UtcDateTime) <= date
        && DateOnly.FromDateTime(leave.EndAt.UtcDateTime) >= date;

    private static bool IsLate(AttendanceRecord record, TimeZoneInfo timezone)
    {
        if (record.ActualStart is not DateTimeOffset actualStart || record.ScheduledStart is not TimeOnly scheduledStart)
            return false;
        var localStart = TimeZoneInfo.ConvertTime(actualStart, timezone).TimeOfDay;
        return localStart - scheduledStart.ToTimeSpan() > LateOrEarlyGrace;
    }

    private static bool IsEarlyDeparture(AttendanceRecord record, TimeZoneInfo timezone)
    {
        if (record.ActualEnd is not DateTimeOffset actualEnd || record.ScheduledEnd is not TimeOnly scheduledEnd)
            return false;
        var localEnd = TimeZoneInfo.ConvertTime(actualEnd, timezone).TimeOfDay;
        return scheduledEnd.ToTimeSpan() - localEnd > LateOrEarlyGrace;
    }

    private static bool IsMissingClockOut(AttendanceRecord record, DateTimeOffset now) =>
        record.ActualStart is DateTimeOffset start
        && record.ActualEnd is null
        && now - start >= AttendanceDayStatusResolver.MissingClockOutThreshold;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AttendancePeriodCalculatorTests"`
Expected: 9 passed (6 facts + 3 theory rows).

- [ ] **Step 5: Make the existing monthly summary delegate**

In `AttendanceReadHandlers.cs`, inside the `GetMyAttendanceMonthlySummaryQuery` handler, replace everything from `var workingDays = 0;` through the closing `}` of the `foreach` loop with:

```csharp
        var counts = AttendancePeriodCalculator.Count(records, timezone, now);
```

and replace the final `return` with:

```csharp
        return Result<AttendanceMonthlySummaryResponse>.Success(
            new AttendanceMonthlySummaryResponse(
                counts.WorkingDays, counts.DaysPresent, counts.LateArrivals, counts.EarlyDepartures, counts.MissingClockOuts));
```

Then delete the now-unused `private static readonly TimeSpan LateOrEarlyGrace = TimeSpan.Zero;` field **and its comment block** at the top of the class (the rule now lives in the calculator). Leave `TryFindTimezone` alone (still used by `BuildRowsAsync`).

- [ ] **Step 6: Run the existing attendance tests**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AttendanceReadHandlerTests|FullyQualifiedName~AttendancePeriodCalculatorTests"`
Expected: all pass, including `MonthlySummary_CountsPresentLateEarlyAndMissingClockOut` (5/4/1/1/1) and `MonthlySummary_RangeOverAMonthIsRejected`. If the first fails, the delegation changed behaviour — fix the delegation, never the test.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorTests.cs
git commit -m "refactor(attendance): extract shared attendance period calculator

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Count notifications by type in a range

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/Notifications/RepositoryInterfaces/INotificationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Notifications/EfNotificationRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/EfNotificationRepositoryCountTests.cs`

**Interfaces:**
- Produces: `INotificationRepository.CountByTypeAsync(Guid tenantId, Guid employeeId, NotificationType type, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct) : Task<int>` — `fromUtc <= CreatedAt < toUtcExclusive`. Used by Task 5. No other implementers or fakes exist (verified by grep).

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Notifications;

namespace ONEVO.Tests.Unit.Features.Monitoring;

public sealed class EfNotificationRepositoryCountTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private Notification N(NotificationType type, string createdAtUtc, Guid? employeeId = null) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = employeeId ?? _employeeId,
            Type = type, Title = "t", Message = "m", CreatedAt = DateTimeOffset.Parse(createdAtUtc)
        };

    [Fact]
    public async Task CountByType_CountsOnlyThatEmployeeAndTypeInsideTheHalfOpenWindow()
    {
        await using var db = BuildInMemoryDb();
        db.MonitoringNotifications.AddRange(
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-01T00:00:00+00:00"),   // in (inclusive start)
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-15T10:00:00+00:00"),   // in
            N(NotificationType.OutsideWorkLocationAlert, "2026-09-01T00:00:00+00:00"),   // out (exclusive end)
            N(NotificationType.OutsideWorkLocationAlert, "2026-07-31T23:59:59+00:00"),   // out (before)
            N(NotificationType.LongIdleAlert, "2026-08-10T00:00:00+00:00"),              // wrong type
            N(NotificationType.OutsideWorkLocationAlert, "2026-08-10T00:00:00+00:00", Guid.NewGuid())); // other employee
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var count = await new EfNotificationRepository(db).CountByTypeAsync(
            _tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert,
            DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
            CancellationToken.None);

        Assert.Equal(2, count);
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new Mock<IDateTimeProvider>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfNotificationRepositoryCountTests"`
Expected: build FAIL — `CountByTypeAsync` does not exist.

- [ ] **Step 3: Add the interface member**

In the **Monitoring** `INotificationRepository.cs`, after `ExistsRecentAsync`:

```csharp
    /// <summary>How many notifications of this type this employee received with
    /// <paramref name="fromUtc"/> &lt;= CreatedAt &lt; <paramref name="toUtcExclusive"/>.</summary>
    Task<int> CountByTypeAsync(
        Guid tenantId, Guid employeeId, NotificationType type,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
```

- [ ] **Step 4: Implement in `EfNotificationRepository`**

After `ExistsRecentAsync` in the Monitoring `EfNotificationRepository.cs`:

```csharp
    public async Task<int> CountByTypeAsync(
        Guid tenantId, Guid employeeId, NotificationType type,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct) =>
        await _db.MonitoringNotifications.AsNoTracking().CountAsync(
            n => n.TenantId == tenantId && n.EmployeeId == employeeId && n.Type == type
                 && n.CreatedAt >= fromUtc && n.CreatedAt < toUtcExclusive, ct);
```

- [ ] **Step 5: Run to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfNotificationRepositoryCountTests"`
Expected: 1 passed.

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/Monitoring/Notifications/RepositoryInterfaces/INotificationRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Notifications/EfNotificationRepository.cs tests/ONEVO.Tests.Unit/Features/Monitoring/EfNotificationRepositoryCountTests.cs
git commit -m "feat(monitoring): count notifications by type within a window

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Period reader + `overview/attendance` endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Services/IEmployeeAttendancePeriodReader.cs`
- Create: `src/ONEVO.Application/Features/TimeAttendance/Services/EmployeeAttendancePeriodReader.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs` (register after the `IAttendanceTodayStateService` block, ~line 53)
- Create: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs`
- Create: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceOverview/GetEmployeeAttendanceOverviewQuery.cs`
- Create: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceOverview/GetEmployeeAttendanceOverviewQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceOverviewQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs`

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard.EnsureCanRead` (Plan 1); `EmployeePeriod.Resolve`, `EmployeeOverviewAccess.HasAccessAsync` (Task 1); `AttendancePeriodCalculator` (Task 2).
- Produces:
  - `sealed record AttendancePeriodData(IReadOnlyList<AttendanceRecord> Records, TimeZoneInfo Timezone, DateTimeOffset Now, DateOnly Today, int? BreakAllowanceMinutes, IReadOnlyDictionary<DateOnly,int> BreakMinutesByDate, IReadOnlyList<LeaveRequest> ApprovedLeaves, DateTimeOffset RangeStartUtc, DateTimeOffset RangeEndUtc)`
  - `IEmployeeAttendancePeriodReader.LoadAsync(Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default) : Task<AttendancePeriodData>` (Task 5 reuses it)
  - `GetEmployeeAttendanceOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To) : IRequest<Result<EmployeeAttendanceOverviewResponse>>`
  - `EmployeeAttendanceDay(DateOnly Date, string Status)`; `EmployeeAttendanceOverviewResponse(DateOnly From, DateOnly To, int WorkingDays, int Present, int Late, int MissingClockOuts, int LeaveDays, IReadOnlyList<EmployeeAttendanceDay> Days)`
  - `GET /api/v1/employees/{id}/overview/attendance?from=&to=` → 200 / 400 / 403 / 404.

- [ ] **Step 1: Write the failing handler test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class GetEmployeeAttendanceOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);

    public GetEmployeeAttendanceOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, _legalEntityId, null, "full_time", "active", null, null)));
    }

    private GetEmployeeAttendanceOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _reader.Object, _user.Object, _clock.Object);

    private AttendanceRecord Rec(DateOnly d, string? start = null, string? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeData(IReadOnlyList<AttendanceRecord> records, IReadOnlyList<LeaveRequest>? leaves = null) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, _legalEntityId, It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, 60,
                new Dictionary<DateOnly, int>(), leaves ?? Array.Empty<LeaveRequest>(),
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

    [Fact]
    public async Task Handle_PassesThroughGuardFailure_WithoutReadingData()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _reader.Verify(r => r.LoadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksAttendanceReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_AllowsSelfWithoutAttendanceRead()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        ArrangeData(Array.Empty<AttendanceRecord>());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DefaultsToTheCurrentMonth_WhenNoRangeIsGiven()
    {
        ArrangeData(Array.Empty<AttendanceRecord>());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.From.Should().Be(new DateOnly(2026, 8, 1));
        result.Value.To.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public async Task Handle_CountsSummaryAndBuildsTheDayStrip()
    {
        var leave = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = _employeeId,
            StartAt = DateTimeOffset.Parse("2026-08-07T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-08-07T23:59:59+00:00")
        };
        ArrangeData(
            new[]
            {
                Rec(new(2026, 8, 3), "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),  // present
                Rec(new(2026, 8, 4), "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),  // late
                Rec(new(2026, 8, 6), "2026-08-06T03:30:00+00:00"),                               // missing clock-out
                Rec(new(2026, 8, 7)),                                                            // leave
                Rec(new(2026, 8, 10))                                                            // absent
            },
            new[] { leave });

        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        var v = result.Value!;
        v.WorkingDays.Should().Be(5);
        v.Present.Should().Be(3);
        v.Late.Should().Be(1);
        v.MissingClockOuts.Should().Be(1);
        v.LeaveDays.Should().Be(1);
        v.Days.Select(d => d.Status).Should().Equal("present", "late", "missing_clock_out", "leave", "absent");
        v.Days.Select(d => d.Date).Should().BeInAscendingOrder();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceOverviewQueryHandlerTests"`
Expected: build FAIL — reader, query, handler, response types do not exist.

- [ ] **Step 3: Write the reader interface + data record**

`IEmployeeAttendancePeriodReader.cs`:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

/// <summary>Everything the attendance Overview cards need for one employee and period, loaded once.</summary>
public sealed record AttendancePeriodData(
    IReadOnlyList<AttendanceRecord> Records,
    TimeZoneInfo Timezone,
    DateTimeOffset Now,
    DateOnly Today,
    int? BreakAllowanceMinutes,
    IReadOnlyDictionary<DateOnly, int> BreakMinutesByDate,
    IReadOnlyList<LeaveRequest> ApprovedLeaves,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc);

public interface IEmployeeAttendancePeriodReader
{
    Task<AttendancePeriodData> LoadAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the reader**

`EmployeeAttendancePeriodReader.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed class EmployeeAttendancePeriodReader(
    IAttendanceReadRepository attendance,
    ILeaveRequestReadRepository leaveRequests,
    ILegalEntityRepository legalEntities,
    IDateTimeProvider clock) : IEmployeeAttendancePeriodReader
{
    // A 366-day period has at most 366 attendance rows.
    private const int MaxRecords = 400;

    public async Task<AttendancePeriodData> LoadAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, EmployeePeriod period, CancellationToken ct = default)
    {
        var (records, _) = await attendance.ListRecordsAsync(
            tenantId, [employeeId], period.From, period.To, 0, MaxRecords, ct);

        var legalEntity = legalEntityId is Guid entityId
            ? await legalEntities.GetByIdForTenantAsync(tenantId, entityId, ct)
            : null;
        var timezone = AttendancePeriodCalculator.ResolveTimezone(
            legalEntity?.Timezone ?? records.FirstOrDefault()?.ScheduleTimezone);
        var now = clock.UtcNow;

        var rangeStart = AttendanceTodayStateService.GetLocalDayWindow(period.From, timezone).Start;
        var rangeEnd = AttendanceTodayStateService.GetLocalDayWindow(period.To, timezone).End;
        var breaks = await attendance.ListBreaksAsync(tenantId, employeeId, rangeStart, rangeEnd, ct);

        // Same rule the history rows use: computed from break records when any exist, otherwise
        // the persisted per-day BreakMinutes.
        var breakMinutesByDate = new Dictionary<DateOnly, int>();
        foreach (var record in records)
        {
            var window = AttendanceTodayStateService.GetLocalDayWindow(record.Date, timezone);
            breakMinutesByDate[record.Date] = breaks.Count > 0
                ? AttendanceTodayStateService.CalculateBreakUsage(breaks, window, now)
                : record.BreakMinutes;
        }

        var leaves = await leaveRequests.ListApprovedCoveringAsync(tenantId, [employeeId], period.From, period.To, ct);

        return new AttendancePeriodData(
            records, timezone, now, clock.Today, legalEntity?.BreakDurationMinutes,
            breakMinutesByDate, leaves, rangeStart, rangeEnd);
    }
}
```

Register it in `src/ONEVO.Application/DependencyInjection.cs` directly after the `IAttendanceTodayStateService` registration (the 3-line `services.AddScoped<…IAttendanceTodayStateService, …AttendanceTodayStateService>();` block):

```csharp
        services.AddScoped<
            ONEVO.Application.Features.TimeAttendance.Services.IEmployeeAttendancePeriodReader,
            ONEVO.Application.Features.TimeAttendance.Services.EmployeeAttendancePeriodReader>();
```

- [ ] **Step 5: Write the response DTOs, query and handler**

`DTOs/Responses/EmployeeAttendanceOverviewResponses.cs`:

```csharp
namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

/// <summary>Status is one of present | late | missing_clock_out | leave | off | absent | none.</summary>
public sealed record EmployeeAttendanceDay(DateOnly Date, string Status);

public sealed record EmployeeAttendanceOverviewResponse(
    DateOnly From,
    DateOnly To,
    int WorkingDays,
    int Present,
    int Late,
    int MissingClockOuts,
    int LeaveDays,
    IReadOnlyList<EmployeeAttendanceDay> Days);
```

`Queries/EmployeeOverview/GetEmployeeAttendanceOverview/GetEmployeeAttendanceOverviewQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;

public sealed record GetEmployeeAttendanceOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeAttendanceOverviewResponse>>;
```

`Queries/EmployeeOverview/GetEmployeeAttendanceOverview/GetEmployeeAttendanceOverviewQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;

public sealed class GetEmployeeAttendanceOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeAttendancePeriodReader reader,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeAttendanceOverviewQuery, Result<EmployeeAttendanceOverviewResponse>>
{
    public const string ModulePermission = "attendance:read";

    public async Task<Result<EmployeeAttendanceOverviewResponse>> Handle(
        GetEmployeeAttendanceOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeAttendanceOverviewResponse>.Forbidden("You do not have access to this employee's attendance.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
        var counts = AttendancePeriodCalculator.Count(data.Records, data.Timezone, data.Now);

        var days = data.Records
            .OrderBy(r => r.Date)
            .Select(r =>
            {
                var hasLeave = data.ApprovedLeaves.Any(l => AttendancePeriodCalculator.CoversDate(l, r.Date));
                return new EmployeeAttendanceDay(
                    r.Date, AttendancePeriodCalculator.DayStatus(r, data.Timezone, data.Now, data.Today, hasLeave));
            })
            .ToList();

        return Result<EmployeeAttendanceOverviewResponse>.Success(new EmployeeAttendanceOverviewResponse(
            period.Value!.From,
            period.Value.To,
            counts.WorkingDays,
            counts.DaysPresent,
            counts.LateArrivals,
            counts.MissingClockOuts,
            days.Count(d => d.Status == "leave"),
            days));
    }
}
```

- [ ] **Step 6: Run the handler tests**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceOverviewQueryHandlerTests"`
Expected: 6 passed.

- [ ] **Step 7: Write the failing architecture test**

Add to `EmployeesControllerArchitectureTests.cs` (above `FindRepositoryPath`):

```csharp
    [Theory]
    [InlineData("overview/attendance", "GetOverviewAttendance")]
    public void EmployeesController_OverviewWidgetActions_RequireEmployeesReadAndUseTheirRoute(string route, string action)
    {
        var path = FindRepositoryPath(
            "src", "ONEVO.Api", "Controllers", "Tenant", "CoreHr", "EmployeesController.cs");
        var source = File.ReadAllText(path);

        var actionIndex = source.IndexOf($"public async Task<IActionResult> {action}(", StringComparison.Ordinal);
        Assert.True(actionIndex > 0, $"Could not locate the {action} action.");

        var preceding = source[..actionIndex];
        var routeIndex = preceding.LastIndexOf($"[HttpGet(\"{{id:guid}}/{route}\")]", StringComparison.Ordinal);
        var permissionIndex = preceding.LastIndexOf("[RequirePermission(\"employees:read\")]", StringComparison.Ordinal);
        Assert.True(routeIndex > 0, $"{action} is missing its [HttpGet] route for {route}.");
        Assert.True(permissionIndex > routeIndex, $"{action} is missing [RequirePermission(\"employees:read\")] after its route attribute.");
    }
```

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"`
Expected: FAIL — "Could not locate the GetOverviewAttendance action."

- [ ] **Step 8: Add the controller action**

In `EmployeesController.cs` add the using

```csharp
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
```

and, after the `GetWorkGraph` action:

```csharp
    /// <summary>Overview attendance card: present/late/missing-clock-out/leave counts and the
    /// per-day strip for one employee over from..to (default: current month).</summary>
    [HttpGet("{id:guid}/overview/attendance")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewAttendance(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceOverviewQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 9: Run architecture + full unit suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green. (This also proves the DI registration compiles; a missing registration would only surface at runtime — Step 10 covers it.)

- [ ] **Step 10: Manual endpoint check**

Start the API, sign in as a tenant user with `employees:read` + `attendance:read`, call `GET /api/v1/employees/{id}/overview/attendance` (no params) → 200, `from` = first of the current month, `days` ordered ascending. Call with `?from=2026-09-02&to=2026-09-01` → 400. Call as a user without `attendance:read` for someone else's id → 403. A DI failure ("Unable to resolve service for type IEmployeeAttendancePeriodReader") here means Step 4's registration is missing.

- [ ] **Step 11: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/TimeAttendance/Services/IEmployeeAttendancePeriodReader.cs src/ONEVO.Application/Features/TimeAttendance/Services/EmployeeAttendancePeriodReader.cs src/ONEVO.Application/DependencyInjection.cs src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceOverviewQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview attendance endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: `overview/attendance-discipline` endpoint

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs` (add the discipline record)
- Create: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQuery.cs`
- Create: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceDisciplineQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (add an `[InlineData]`)

**Interfaces:**
- Consumes: Task 1-4 items; `IMonitoringToggleResolver.IsEnabledAsync(Guid tenantId, Guid employeeId, MonitoringCapability capability, CancellationToken ct = default)`; `INotificationRepository.CountByTypeAsync` (Task 3).
- Produces:
  - `EmployeeAttendanceDisciplineResponse(DateOnly From, DateOnly To, int LateClockIns, int EarlyClockOuts, int MissingClockOuts, int OverBreakDays, int OverBreakMinutes, bool LocationTrackingEnabled, int? LocationViolations)`
  - `GetEmployeeAttendanceDisciplineQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)`
  - `GET /api/v1/employees/{id}/overview/attendance-discipline?from=&to=`

- [ ] **Step 1: Write the failing test**

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
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class GetEmployeeAttendanceDisciplineQueryHandlerTests
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
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset RangeStart = DateTimeOffset.Parse("2026-07-31T18:30:00+00:00");
    private static readonly DateTimeOffset RangeEnd = DateTimeOffset.Parse("2026-08-31T18:30:00+00:00");

    public GetEmployeeAttendanceDisciplineQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, RangeStart, RangeEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    private GetEmployeeAttendanceDisciplineQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _reader.Object, _toggles.Object, _notifications.Object, _user.Object, _clock.Object);

    private AttendanceRecord Rec(DateOnly d, string? start = null, string? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeData(int? allowance, Dictionary<DateOnly, int> breaks, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks,
                Array.Empty<LeaveRequest>(), RangeStart, RangeEnd));

    private static readonly DateOnly D3 = new(2026, 8, 3);
    private static readonly DateOnly D4 = new(2026, 8, 4);
    private static readonly DateOnly D5 = new(2026, 8, 5);
    private static readonly DateOnly D6 = new(2026, 8, 6);

    private AttendanceRecord[] Standard() => new[]
    {
        Rec(D3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
        Rec(D4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),  // late
        Rec(D5, "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00"),  // early out
        Rec(D6, "2026-08-06T03:30:00+00:00")                                // missing clock-out
    };

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksAttendanceReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_CountsLateEarlyMissingAndOverBreak_AndLocationAlertsWhenTrackingIsOn()
    {
        ArrangeData(60, new Dictionary<DateOnly, int> { [D3] = 30, [D4] = 75, [D5] = 90, [D6] = 60 }, Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        var v = result.Value!;
        v.LateClockIns.Should().Be(1);
        v.EarlyClockOuts.Should().Be(1);
        v.MissingClockOuts.Should().Be(1);
        v.OverBreakDays.Should().Be(2);          // D4 (+15) and D5 (+30); D6 == allowance is not over
        v.OverBreakMinutes.Should().Be(45);
        v.LocationTrackingEnabled.Should().BeTrue();
        v.LocationViolations.Should().Be(3);
    }

    [Fact]
    public async Task Handle_HidesLocationViolations_WhenLocationTrackingIsDisabled()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        ArrangeData(60, new Dictionary<DateOnly, int>(), Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.LocationTrackingEnabled.Should().BeFalse();
        result.Value.LocationViolations.Should().BeNull();
        _notifications.Verify(n => n.CountByTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CountsNoOverBreak_WhenNoAllowanceIsConfigured()
    {
        ArrangeData(null, new Dictionary<DateOnly, int> { [D4] = 500 }, Standard());

        var result = await CreateHandler().Handle(new GetEmployeeAttendanceDisciplineQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.OverBreakDays.Should().Be(0);
        result.Value.OverBreakMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeAttendanceDisciplineQuery(_employeeId, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceDisciplineQueryHandlerTests"`
Expected: build FAIL — query/handler/response do not exist.

- [ ] **Step 3: Add the response record**

Append to `EmployeeAttendanceOverviewResponses.cs`:

```csharp
/// <summary>LocationViolations is null (and LocationTrackingEnabled false) when work-location
/// verification is not enabled for this employee. It counts OutsideWorkLocationAlert
/// notifications, i.e. alert events (6-hour cooldown), not GPS samples.</summary>
public sealed record EmployeeAttendanceDisciplineResponse(
    DateOnly From,
    DateOnly To,
    int LateClockIns,
    int EarlyClockOuts,
    int MissingClockOuts,
    int OverBreakDays,
    int OverBreakMinutes,
    bool LocationTrackingEnabled,
    int? LocationViolations);
```

- [ ] **Step 4: Write the query and handler**

`GetEmployeeAttendanceDisciplineQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed record GetEmployeeAttendanceDisciplineQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeAttendanceDisciplineResponse>>;
```

`GetEmployeeAttendanceDisciplineQueryHandler.cs`:

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

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeAttendanceDisciplineResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
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

        var trackingEnabled = await toggles.IsEnabledAsync(
            tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct);
        int? locationViolations = trackingEnabled
            ? await notifications.CountByTypeAsync(
                tenantId, request.EmployeeId, NotificationType.OutsideWorkLocationAlert,
                data.RangeStartUtc, data.RangeEndUtc, ct)
            : null;

        return Result<EmployeeAttendanceDisciplineResponse>.Success(new EmployeeAttendanceDisciplineResponse(
            period.Value!.From,
            period.Value.To,
            counts.LateArrivals,
            counts.EarlyDepartures,
            counts.MissingClockOuts,
            overBreakDays,
            overBreakMinutes,
            trackingEnabled,
            locationViolations));
    }
}
```

- [ ] **Step 5: Run to verify the handler tests pass**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeAttendanceDisciplineQueryHandlerTests"`
Expected: 5 passed.

- [ ] **Step 6: Extend the architecture test, watch it fail, add the action**

In `EmployeesControllerArchitectureTests.cs`, add a second attribute line to the `[Theory]` from Task 4:

```csharp
    [InlineData("overview/attendance-discipline", "GetOverviewAttendanceDiscipline")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add the using

```csharp
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
```

and after `GetOverviewAttendance`:

```csharp
    /// <summary>Overview attendance-discipline card: late clock-ins, early clock-outs, missing
    /// clock-outs, over-break days/minutes and (when location tracking is on) location alerts.</summary>
    [HttpGet("{id:guid}/overview/attendance-discipline")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewAttendanceDiscipline(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceDisciplineQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run both suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

- [ ] **Step 8: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/GetEmployeeAttendanceDisciplineQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview attendance-discipline endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: `overview/time-off` endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Balance/DTOs/Responses/EmployeeTimeOffResponse.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/GetEmployeeTimeOff/GetEmployeeTimeOffQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Balance/Queries/GetEmployeeTimeOff/GetEmployeeTimeOffQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Leave/GetEmployeeTimeOffQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (add an `[InlineData]`)

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard`, `EmployeeOverviewAccess` (Task 1), existing `ILeaveEntitlementRepository.ListRowsAsync(Guid tenantId, LeaveEntitlementListFilter filter, CancellationToken ct)`, `LeaveEntitlementListFilter(int Year, Guid? EmployeeId, IReadOnlyCollection<Guid>? EmployeeIds, Guid? LegalEntityId, Guid? DepartmentId, Guid? LeaveTypeId, int? EmploymentStatusId, string? Search)`, `LeaveBalanceMapping.MapAsync(ILeavePolicyRepository, tenantId, year, asOfDate, rows, ct) : Task<IReadOnlyList<LeaveBalanceResponse>>`, `ILeaveRequestReadRepository.ListApprovedCoveringAsync`.
- Produces:
  - `EmployeeTimeOffBalance(Guid LeaveTypeId, string LeaveTypeName, string LeaveTypeCode, decimal EntitledHours, decimal UsedHours, decimal PendingHours, decimal RemainingHours, bool IsNegative)`
  - `EmployeeUpcomingLeave(Guid LeaveTypeId, string? LeaveTypeName, DateOnly StartDate, DateOnly EndDate, decimal TotalHours)`
  - `EmployeeTimeOffResponse(int Year, IReadOnlyList<EmployeeTimeOffBalance> Balances, EmployeeUpcomingLeave? NextLeave)`
  - `GetEmployeeTimeOffQuery(Guid EmployeeId, int? Year)`
  - `GET /api/v1/employees/{id}/overview/time-off?year=` — year-based, **ignores the month period**; `NextLeave` = the earliest approved leave that has not ended, within the next 90 days.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Leave;

public sealed class GetEmployeeTimeOffQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlements = new();
    private readonly Mock<ILeavePolicyRepository> _policies = new();
    private readonly Mock<ILeaveRequestReadRepository> _leaveRequests = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T06:00:00+00:00");
    private static readonly DateOnly Today = new(2026, 8, 21);

    public GetEmployeeTimeOffQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("leave:read")).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _entitlements.Setup(e => e.ListRowsAsync(_tenantId, It.IsAny<LeaveEntitlementListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _leaveRequests.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequest>());
    }

    private GetEmployeeTimeOffQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _entitlements.Object, _policies.Object, _leaveRequests.Object, _user.Object, _clock.Object);

    private LeaveRequest Leave(string startUtc, string endUtc, decimal hours, Guid? typeId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = typeId ?? Guid.NewGuid(),
        StartAt = DateTimeOffset.Parse(startUtc), EndAt = DateTimeOffset.Parse(endUtc), TotalHours = hours
    };

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksLeaveReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("leave:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Handle_Returns400_ForAnImplausibleYear(int year)
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, year), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DefaultsToTheCurrentYear_AndQueriesOnlyThatEmployee()
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.Value!.Year.Should().Be(2026);
        _entitlements.Verify(e => e.ListRowsAsync(
            _tenantId,
            It.Is<LeaveEntitlementListFilter>(f => f.Year == 2026 && f.EmployeeId == _employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PicksTheEarliestNotYetEndedApprovedLeave_AsNextLeave()
    {
        var earlier = Leave("2026-08-10T00:00:00+00:00", "2026-08-12T23:59:59+00:00", 24);   // already ended
        var next = Leave("2026-09-23T00:00:00+00:00", "2026-09-25T23:59:59+00:00", 24);
        var later = Leave("2026-10-05T00:00:00+00:00", "2026-10-06T23:59:59+00:00", 16);
        _leaveRequests.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, Today.AddDays(90), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { later, earlier, next });

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        var leave = result.Value!.NextLeave!;
        leave.StartDate.Should().Be(new DateOnly(2026, 9, 23));
        leave.EndDate.Should().Be(new DateOnly(2026, 9, 25));
        leave.TotalHours.Should().Be(24);
        leave.LeaveTypeId.Should().Be(next.LeaveTypeId);
    }

    [Fact]
    public async Task Handle_NextLeaveIsNull_WhenNothingIsUpcoming()
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.Value!.NextLeave.Should().BeNull();
        result.Value.Balances.Should().BeEmpty();
    }
}
```

> The mapping of entitlement rows to balances is `LeaveBalanceMapping.MapAsync`, already covered by the existing leave balance tests; this test deliberately returns no rows and asserts filter/permission/period/next-leave behaviour.

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeTimeOffQueryHandlerTests"`
Expected: build FAIL — query/handler/response do not exist.

- [ ] **Step 3: Write the response DTOs**

`EmployeeTimeOffResponse.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

public sealed record EmployeeTimeOffBalance(
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal EntitledHours,
    decimal UsedHours,
    decimal PendingHours,
    decimal RemainingHours,
    bool IsNegative);

public sealed record EmployeeUpcomingLeave(
    Guid LeaveTypeId,
    string? LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalHours);

/// <summary>Balances are in hours (the leave module has no hours-per-day constant). NextLeave is
/// the earliest approved leave that has not ended, within the next 90 days.</summary>
public sealed record EmployeeTimeOffResponse(
    int Year,
    IReadOnlyList<EmployeeTimeOffBalance> Balances,
    EmployeeUpcomingLeave? NextLeave);
```

- [ ] **Step 4: Write the query and handler**

`GetEmployeeTimeOffQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;

public sealed record GetEmployeeTimeOffQuery(Guid EmployeeId, int? Year)
    : IRequest<Result<EmployeeTimeOffResponse>>;
```

`GetEmployeeTimeOffQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;

public sealed class GetEmployeeTimeOffQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ILeaveEntitlementRepository entitlements,
    ILeavePolicyRepository policies,
    ILeaveRequestReadRepository leaveRequests,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeTimeOffQuery, Result<EmployeeTimeOffResponse>>
{
    public const string ModulePermission = "leave:read";
    private const int UpcomingWindowDays = 90;

    public async Task<Result<EmployeeTimeOffResponse>> Handle(GetEmployeeTimeOffQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeTimeOffResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeTimeOffResponse>.Forbidden("You do not have access to this employee's time off.");

        var today = clock.Today;
        var year = request.Year ?? today.Year;
        if (year < 2000 || year > 2100)
            return Result<EmployeeTimeOffResponse>.Failure("year must be between 2000 and 2100.");

        var rows = await entitlements.ListRowsAsync(
            tenantId,
            new LeaveEntitlementListFilter(year, request.EmployeeId, null, null, null, null, null, null),
            ct);
        var mapped = await LeaveBalanceMapping.MapAsync(
            policies, tenantId, year, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), rows, ct);

        var balances = mapped
            .Select(b => new EmployeeTimeOffBalance(
                b.LeaveTypeId, b.LeaveTypeName, b.LeaveTypeCode,
                b.EntitledHours, b.UsedHours, b.PendingHours, b.RemainingHours, b.IsNegative))
            .ToList();

        var upcoming = await leaveRequests.ListApprovedCoveringAsync(
            tenantId, [request.EmployeeId], today, today.AddDays(UpcomingWindowDays), ct);
        var next = upcoming
            .Where(l => DateOnly.FromDateTime(l.EndAt.UtcDateTime) >= today)
            .OrderBy(l => l.StartAt)
            .FirstOrDefault();

        var typeNames = mapped.GroupBy(b => b.LeaveTypeId).ToDictionary(g => g.Key, g => g.First().LeaveTypeName);
        var nextLeave = next is null
            ? null
            : new EmployeeUpcomingLeave(
                next.LeaveTypeId,
                typeNames.GetValueOrDefault(next.LeaveTypeId),
                DateOnly.FromDateTime(next.StartAt.UtcDateTime),
                DateOnly.FromDateTime(next.EndAt.UtcDateTime),
                next.TotalHours);

        return Result<EmployeeTimeOffResponse>.Success(new EmployeeTimeOffResponse(year, balances, nextLeave));
    }
}
```

- [ ] **Step 5: Run to verify the handler tests pass**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeTimeOffQueryHandlerTests"`
Expected: 7 passed (5 facts + 2 theory rows).

- [ ] **Step 6: Extend the architecture test, watch it fail, add the action**

Add to the `[Theory]` in `EmployeesControllerArchitectureTests.cs`:

```csharp
    [InlineData("overview/time-off", "GetOverviewTimeOff")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add the using

```csharp
using ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;
```

and after `GetOverviewAttendanceDiscipline`:

```csharp
    /// <summary>Overview time-off card: this year's leave balances (hours) plus the next
    /// approved leave. Year-based - it does not follow the month period.</summary>
    [HttpGet("{id:guid}/overview/time-off")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewTimeOff(
        Guid id, [FromQuery] int? year = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeTimeOffQuery(id, year), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run both suites and check the endpoints live**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

Live: start the API; as a user with `employees:read` + `leave:read` + `attendance:read` call all three routes for one employee → 200 each. `GET …/overview/attendance-discipline` for an employee with location verification off → `locationTrackingEnabled: false, locationViolations: null`. `GET …/overview/time-off?year=1999` → 400. Report what returned; if the dev tenant has no leave/attendance data the payloads are legitimately empty — say so instead of implying the data path was exercised.

- [ ] **Step 8: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/Leave/Balance/DTOs/Responses/EmployeeTimeOffResponse.cs src/ONEVO.Application/Features/Leave/Balance/Queries/GetEmployeeTimeOff src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/Leave/GetEmployeeTimeOffQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview time-off endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review (against the spec and the conversation)

- Period plumbing deferred from Plan 1: `EmployeePeriod` (Task 1) — default current month, both-or-neither, `from<=to`, ≤366 days.
- Attendance card (present/late/missing clock-out/leave + per-day strip): Tasks 2, 4. Discipline card incl. **over-break** and **location violations only when tracking is on** (the user's explicit additions): Tasks 3, 5. Time-off: Task 6.
- Per-widget permission + self access + coverage guard: Tasks 1, 4-6 (every handler runs `IEmployeeReadAccessGuard` first).
- "Existing behaviour unchanged": Task 2 Step 6 re-runs `AttendanceReadHandlerTests`.
- Type consistency: `EmployeePeriod`/`EmployeeOverviewAccess` (T1) → T4/5/6; `AttendancePeriodCalculator` members (T2) → T4/5; `CountByTypeAsync(tenantId, employeeId, type, fromUtc, toUtcExclusive, ct)` (T3) → T5 test + handler; `AttendancePeriodData` fields (T4) → T5 test constructor (positional order: Records, Timezone, Now, Today, BreakAllowanceMinutes, BreakMinutesByDate, ApprovedLeaves, RangeStartUtc, RangeEndUtc) — identical in both tests.
- Verified by reading source: `IEmployeeRepository.GetDefaultForUserAsync(tenantId, userId, ct)` (CoreHr interface); `IAttendanceReadRepository.ListRecordsAsync/ListBreaksAsync` signatures; `AttendanceTodayStateService.GetLocalDayWindow/CalculateBreakUsage` are `public static`; `IMonitoringToggleResolver.IsEnabledAsync(tenantId, employeeId, capability, ct)`; `NotificationType.OutsideWorkLocationAlert`; `LegalEntity.BreakDurationMinutes`; `LeaveBalanceMapping.MapAsync` signature; `LeaveEntitlementListFilter` shape; `LeaveRequest` fields. Not compile-checked: none of this plan's code has been built; the executor's first `dotnet test` is the first compile. Fix compile errors by aligning to real signatures, never by weakening a test.
- Known limits (intentional, surfaced in the frontend): leave day count relies on an attendance row existing for the date; location count is alert events not samples; balances are hours not days.
- Not in this plan: work summary, delivery, approvals, activity insights (Plan 3); upcoming items, recent activity, checklists, history events (Plan 4).
