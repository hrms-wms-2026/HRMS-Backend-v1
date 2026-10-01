# Employee Overview Backend — Part 2: Ranked Violation Signals Endpoint

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `GET /api/v1/employees/{id}/overview/signals?from&to` returns every *active* violation for the employee in the period, ordered by the user-confirmed importance rank, so the frontend KPI strip and Needs Attention card render from one list.

**Architecture:** The query handler re-uses the existing per-widget queries via MediatR `ISender` (each already applies `IEmployeeReadAccessGuard` and its own gating), adds a small monitoring read, and hands everything to a pure `EmployeeSignalCatalogue.Build(inputs)` that owns the 12 definitions and ordering. Each source is fetched in its own try/catch — one failing source omits only its signals.

**Tech Stack:** .NET 9, MediatR, EF Core, xUnit + FluentAssertions + Moq.

**Spec:** `docs/superpowers/specs/next/2026-09-30-employee-overview-signals-and-period-correctness-design.md` (Section 4).

**Depends on:** Part 1 (`part-1-expected-working-days.md`) merged — needs `EmployeeAttendanceOverviewResponse.Absent / ShortHours / WorkedOnNonWorkingDay / WorkedDuringTimeOff`.

## Global Constraints

- Repo `C:\onevoNew\HRMS-Backend-v1`, branch `feature/task-subtasks`. Build `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`; kill `ONEVO.Api.exe` immediately before every `dotnet test`.
- Order is **strictly by Rank** (1 = most important). Severity is colour only, never order.
- A signal is returned only when active (value ≥ 1). A source that is module-gated off, invisible to the viewer, or failed → its signals are omitted, never zero.
- Work Management module keys (same as `EmployeesController` `RequireAnyModule`): `worksync_foundation, projects, objectives_milestones, tasks, boards, planning_sprints`.
- Commit attribution: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Catalogue (verbatim from spec)

| Rank | Key | Severity | Category | Value / Unit / Denominator | Source field |
|---|---|---|---|---|---|
| 1 | `absent_days` | critical | attendance | Absent / days / WorkingDays | attendance.Absent |
| 2 | `missing_clock_outs` | critical | attendance | MissingClockOuts / count / Present | attendance.MissingClockOuts |
| 3 | `over_break` | warning | attendance | OverBreakMinutes / minutes / OverBreakDays | discipline |
| 4 | `late_clock_ins` | warning | attendance | Late / count / Present | attendance.Late |
| 5 | `early_clock_outs` | warning | attendance | EarlyClockOuts / count / Present | discipline |
| 6 | `short_hours_days` | warning | attendance | ShortHours / days / Present | attendance.ShortHours |
| 7 | `location_violations` | warning | attendance | LocationViolations / count / – | discipline (null ⇒ omitted) |
| 8 | `overdue_tasks` | critical | work | Overdue / count / Assigned | work |
| 9 | `pending_approvals` | info | approvals | Pending / count / – | approvals |
| 10 | `off_schedule_work` | info | attendance | WorkedOnNonWorkingDay + WorkedDuringTimeOff / days / – | attendance |
| 11 | `idle_activity_alerts` | warning | monitoring | LongIdleAlert + LowActivityAlert / count / – | notifications |
| 12 | `monitoring_exceptions` | critical | monitoring | exception cases detected in range / count / – | exceptions |

---

### Task 1: DTOs + pure catalogue

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeOverviewSignalsResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeSignalCatalogue.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeSignalCatalogueTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record EmployeeSignal(
    string Key, int Rank, string Severity, string Category,
    int Value, string Unit, int? Denominator, string Label, string Detail);
public sealed record EmployeeOverviewSignalsResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeSignal> Signals);
public sealed record EmployeeMonitoringSignalCounts(int IdleAlerts, int Exceptions);
public sealed record EmployeeSignalInputs(
    EmployeeAttendanceOverviewResponse? Attendance,
    EmployeeAttendanceDisciplineResponse? Discipline,
    EmployeeWorkOverviewResponse? Work,
    EmployeeApprovalActivityResponse? Approvals,
    EmployeeMonitoringSignalCounts? Monitoring);
public static class EmployeeSignalCatalogue { public static IReadOnlyList<EmployeeSignal> Build(EmployeeSignalInputs inputs); }
```

- [ ] **Step 1: Failing tests**

```csharp
public sealed class EmployeeSignalCatalogueTests
{
    private static readonly DateOnly F = new(2026, 9, 1), T = new(2026, 9, 30);
    private static EmployeeAttendanceOverviewResponse Att(int absent = 0, int missing = 0, int late = 0, int shortH = 0, int offDay = 0, int leaveWork = 0) =>
        new(F, T, 22, 10, late, missing, 0, Array.Empty<EmployeeAttendanceDay>(), absent, shortH, offDay, leaveWork);
    private static EmployeeAttendanceDisciplineResponse Disc(int early = 0, int obDays = 0, int obMin = 0, int? loc = null) =>
        new(F, T, 0, early, 0, obDays, obMin, loc is not null, loc);
    private static EmployeeWorkOverviewResponse Work(int overdue) => new(F, T, 6, 1, 2, overdue, 2, 17, null);
    private static EmployeeApprovalActivityResponse Appr(int pending) => new(F, T, pending, 0, 0, pending, Array.Empty<EmployeeApprovalItem>());

    [Fact]
    public void AllQuiet_ReturnsEmpty() =>
        EmployeeSignalCatalogue.Build(new(Att(), Disc(), Work(0), Appr(0), new(0, 0))).Should().BeEmpty();

    [Fact]
    public void OrdersStrictlyByRank_NotSeverity()
    {
        var s = EmployeeSignalCatalogue.Build(new(Att(late: 2), Disc(obDays: 1, obMin: 25), Work(1), Appr(2), new(0, 3)));
        s.Select(x => x.Key).Should().Equal("over_break", "late_clock_ins", "overdue_tasks", "pending_approvals", "monitoring_exceptions");
    }

    [Fact]
    public void OverBreak_ValueIsMinutes_DenominatorIsDays()
    {
        var s = EmployeeSignalCatalogue.Build(new(null, Disc(obDays: 2, obMin: 40), null, null, null)).Single();
        (s.Value, s.Unit, s.Denominator).Should().Be((40, "minutes", 2));
    }

    [Fact]
    public void MissingSources_OmitTheirSignals()
    {
        var s = EmployeeSignalCatalogue.Build(new(Att(absent: 3), null, null, null, null));
        s.Select(x => x.Key).Should().Equal("absent_days");
    }

    [Fact]
    public void LocationViolations_NullMeansTrackingOff_Omitted() =>
        EmployeeSignalCatalogue.Build(new(null, Disc(loc: null), null, null, null)).Should().BeEmpty();

    [Fact]
    public void OffScheduleWork_SumsBothCounts() =>
        EmployeeSignalCatalogue.Build(new(Att(offDay: 1, leaveWork: 2), null, null, null, null)).Single().Value.Should().Be(3);
}
```

- [ ] **Step 2: Run** `--filter EmployeeSignalCatalogueTests` → compile failure.

- [ ] **Step 3: Implement** — DTO file holds the four records above. Catalogue:

```csharp
public static class EmployeeSignalCatalogue
{
    public static IReadOnlyList<EmployeeSignal> Build(EmployeeSignalInputs i)
    {
        var list = new List<EmployeeSignal>();
        void Add(bool when, string key, int rank, string sev, string cat, int value, string unit, int? denom, string label, string detail)
        { if (when && value > 0) list.Add(new(key, rank, sev, cat, value, unit, denom, label, detail)); }
        static string S(int n, string one, string many) => n == 1 ? one : many;

        var a = i.Attendance; var d = i.Discipline; var w = i.Work; var p = i.Approvals; var m = i.Monitoring;

        if (a is not null)
            Add(true, "absent_days", 1, "critical", "attendance", a.Absent, "days", a.WorkingDays,
                $"{a.Absent} absent {S(a.Absent, "day", "days")}", $"No clock-in or approved leave on {a.Absent} of {a.WorkingDays} working days");
        if (a is not null)
            Add(true, "missing_clock_outs", 2, "critical", "attendance", a.MissingClockOuts, "count", a.Present,
                $"{a.MissingClockOuts} missing clock-{S(a.MissingClockOuts, "out", "outs")}", "Time entries need review");
        if (d is not null)
            Add(true, "over_break", 3, "warning", "attendance", d.OverBreakMinutes, "minutes", d.OverBreakDays,
                $"{d.OverBreakMinutes} min over break allowance", $"Exceeded on {d.OverBreakDays} {S(d.OverBreakDays, "day", "days")}");
        if (a is not null)
            Add(true, "late_clock_ins", 4, "warning", "attendance", a.Late, "count", a.Present,
                $"{a.Late} late {S(a.Late, "arrival", "arrivals")}", $"Across {a.Present} attended {S(a.Present, "day", "days")}");
        if (d is not null)
            Add(true, "early_clock_outs", 5, "warning", "attendance", d.EarlyClockOuts, "count", a?.Present,
                $"{d.EarlyClockOuts} early clock-{S(d.EarlyClockOuts, "out", "outs")}", "Left before scheduled end");
        if (a is not null)
            Add(true, "short_hours_days", 6, "warning", "attendance", a.ShortHours, "days", a.Present,
                $"{a.ShortHours} short-hours {S(a.ShortHours, "day", "days")}", "Worked less than the required hours");
        if (d?.LocationViolations is int loc)
            Add(true, "location_violations", 7, "warning", "attendance", loc, "count", null,
                $"{loc} outside-location {S(loc, "alert", "alerts")}", "Clocked in away from the work location");
        if (w is not null)
            Add(true, "overdue_tasks", 8, "critical", "work", w.Overdue, "count", w.Assigned,
                $"{w.Overdue} overdue {S(w.Overdue, "task", "tasks")}",
                w.Assigned > 0 ? $"{w.Overdue * 100 / w.Assigned}% of assigned work overdue" : "Past due date");
        if (p is not null)
            Add(true, "pending_approvals", 9, "info", "approvals", p.Pending, "count", null,
                $"{p.Pending} pending approval {S(p.Pending, "request", "requests")}", "Awaiting review");
        if (a is not null)
        {
            var off = a.WorkedOnNonWorkingDay + a.WorkedDuringTimeOff;
            Add(true, "off_schedule_work", 10, "info", "attendance", off, "days", null,
                $"{off} off-schedule work {S(off, "day", "days")}", "Worked on a non-working day or during time off");
        }
        if (m is not null)
        {
            Add(true, "idle_activity_alerts", 11, "warning", "monitoring", m.IdleAlerts, "count", null,
                $"{m.IdleAlerts} idle / low-activity {S(m.IdleAlerts, "alert", "alerts")}", "Raised by activity monitoring");
            Add(true, "monitoring_exceptions", 12, "critical", "monitoring", m.Exceptions, "count", null,
                $"{m.Exceptions} monitoring {S(m.Exceptions, "exception", "exceptions")}", "Flagged cases to review");
        }

        return list.OrderBy(s => s.Rank).ToList();
    }
}
```

- [ ] **Step 4: Run** → 6 passed. **Step 5: Commit** — `feat(people): employee overview signal catalogue (12 ranked violations)`.

---

### Task 2: Exception count read

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/Exceptions/RepositoryInterfaces/IExceptionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Exceptions/EfExceptionRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Exceptions/EfExceptionRepositoryTests.cs` (add one case, follow the file's existing in-memory/SQLite setup)

- [ ] **Step 1:** Add
```csharp
    /// <summary>Exception cases for this employee with fromUtc &lt;= DetectedAt &lt; toUtcExclusive, any status.</summary>
    Task<int> CountDetectedInRangeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
```
EF:
```csharp
    public Task<int> CountDetectedInRangeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => _db.Exceptions.AsNoTracking().CountAsync(
            e => e.TenantId == tenantId && e.EmployeeId == employeeId && e.DetectedAt >= fromUtc && e.DetectedAt < toUtcExclusive, ct);
```
- [ ] **Step 2:** Test: seed 3 exceptions for the employee (one before range, two inside) + one for another employee → returns 2. Implement any test fakes of `IExceptionRepository` the build flags.
- [ ] **Step 3:** Kill API, run `--filter EfExceptionRepositoryTests` → green. **Commit** — `feat(monitoring): count exception cases detected in a range`.

---

### Task 3: Query, handler, route

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeOverviewSignals/GetEmployeeOverviewSignalsQuery.cs`
- Create: `.../GetEmployeeOverviewSignals/GetEmployeeOverviewSignalsQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (after `GetOverviewApprovals`, ~line 234)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeOverviewSignalsQueryHandlerTests.cs`

**Interfaces:** `public sealed record GetEmployeeOverviewSignalsQuery(Guid EmployeeId, DateOnly? From, DateOnly? To) : IRequest<Result<EmployeeOverviewSignalsResponse>>;`

- [ ] **Step 1: Failing tests** (Moq `ISender`, `IEmployeeReadAccessGuard`, `IModuleEntitlementService`, `IMonitoringToggleResolver`, `INotificationRepository`, `IExceptionRepository`, `ICurrentUser`, `IDateTimeProvider`, `ILogger<...>` via `NullLogger`):
  - `GuardFailure_Returns403_AndSendsNothing` — guard returns Forbidden → 403; `sender.Verify(s => s.Send(It.IsAny<IRequest<It.IsAnyType>>(), ...), Times.Never)` (or verify each concrete query type never sent).
  - `WorkModuleOff_OmitsOverdueTasks` — entitlement returns false for all six keys; work query must not be sent; attendance returns absent 2 → result keys `["absent_days"]`.
  - `MonitoringOff_OmitsMonitoringSignals` — toggle `ActivityMonitoring` false → notification/exception repos never called.
  - `FailingSource_IsIsolated` — sender throws for the discipline query; attendance succeeds with late 1 → result contains `late_clock_ins`, no exception escapes.
  - `FailedResult_TreatedAsMissing` — approvals query returns `Result.Forbidden` → no `pending_approvals`, still 200.

- [ ] **Step 2: Run** → compile failure.

- [ ] **Step 3: Implement handler**

```csharp
public sealed class GetEmployeeOverviewSignalsQueryHandler(
    IEmployeeReadAccessGuard guard, ISender sender, IModuleEntitlementService modules,
    IMonitoringToggleResolver toggles, INotificationRepository notifications, IExceptionRepository exceptions,
    ICurrentUser currentUser, IDateTimeProvider clock, ILogger<GetEmployeeOverviewSignalsQueryHandler> logger)
    : IRequestHandler<GetEmployeeOverviewSignalsQuery, Result<EmployeeOverviewSignalsResponse>>
{
    private static readonly string[] WorkModules =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public async Task<Result<EmployeeOverviewSignalsResponse>> Handle(GetEmployeeOverviewSignalsQuery q, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var access = await guard.EnsureCanRead(tenantId, q.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeOverviewSignalsResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(q.From, q.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeOverviewSignalsResponse>.Failure(period.Error!, period.StatusCode ?? 400);
        var (from, to) = (period.Value!.From, period.Value.To);

        var attendance = await Safe("attendance", () => sender.Send(new GetEmployeeAttendanceOverviewQuery(q.EmployeeId, from, to), ct));
        var discipline = await Safe("discipline", () => sender.Send(new GetEmployeeAttendanceDisciplineQuery(q.EmployeeId, from, to, null), ct));
        var work = await WorkEnabled(tenantId, ct)
            ? await Safe("work", () => sender.Send(new GetEmployeeWorkOverviewQuery(q.EmployeeId, from, to), ct))
            : null;
        var approvals = await Safe("approvals", () => sender.Send(new GetEmployeeApprovalActivityQuery(q.EmployeeId, from, to), ct));
        var monitoring = await SafeValue("monitoring", () => MonitoringAsync(tenantId, q.EmployeeId, from, to, ct));

        var signals = EmployeeSignalCatalogue.Build(new EmployeeSignalInputs(attendance, discipline, work, approvals, monitoring));
        return Result<EmployeeOverviewSignalsResponse>.Success(new EmployeeOverviewSignalsResponse(from, to, signals));
    }

    private async Task<bool> WorkEnabled(Guid tenantId, CancellationToken ct)
    {
        foreach (var key in WorkModules)
            if (await modules.IsModuleEnabledAsync(tenantId, key, ct)) return true;
        return false;
    }

    private async Task<EmployeeMonitoringSignalCounts?> MonitoringAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!await toggles.IsEnabledAsync(tenantId, employeeId, MonitoringCapability.ActivityMonitoring, ct)) return null;
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var idle = await notifications.CountByTypeAsync(tenantId, employeeId, NotificationType.LongIdleAlert, start, end, ct)
                 + await notifications.CountByTypeAsync(tenantId, employeeId, NotificationType.LowActivityAlert, start, end, ct);
        var cases = await exceptions.CountDetectedInRangeAsync(tenantId, employeeId, start, end, ct);
        return new EmployeeMonitoringSignalCounts(idle, cases);
    }

    private async Task<T?> Safe<T>(string source, Func<Task<Result<T>>> load) where T : class
    {
        try { var r = await load(); return r.IsSuccess ? r.Value : null; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning(ex, "Overview signal source {Source} failed", source); return null; }
    }

    private async Task<T?> SafeValue<T>(string source, Func<Task<T?>> load) where T : class
    {
        try { return await load(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning(ex, "Overview signal source {Source} failed", source); return null; }
    }
}
```
(`System.Exception` — watch the `MonitoringException` alias clash: do **not** import `ONEVO.Domain.Features.Monitoring.Exceptions.Entities` in this file.) Monitoring uses UTC day bounds; acceptable for alert counts (documented in the XML summary).

Controller:
```csharp
    /// <summary>Overview signals: every active violation for the employee in from..to, ordered by
    /// importance rank. Drives the KPI strip and Needs Attention card.</summary>
    [HttpGet("{id:guid}/overview/signals")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewSignals(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeOverviewSignalsQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 4:** Kill API; run the new tests, then full Unit + Architecture suites; paste raw totals. Integration: add to the existing employee-overview integration test class (grep `overview/attendance` under `tests/ONEVO.Tests.Integration`) one case — `GET /overview/signals` for a seeded employee returns 200 and a `signals` array; an employee with zero attendance rows in a past month returns an `absent_days` signal. Run it only if the integration environment is available; otherwise report "integration not run" explicitly.
- [ ] **Step 5: Commit** — `feat(people): GET /employees/{id}/overview/signals ranked violations endpoint`.
