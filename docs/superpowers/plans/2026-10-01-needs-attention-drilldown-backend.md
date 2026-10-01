# Needs Attention Drill-down — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /employees/{id}/overview/signals/{key}/items` returning the concrete days / alerts / cases / tasks / approvals behind each of the 12 Employee Overview signals, with list and count produced by the same rule.

**Architecture:** Each count rule that today only increments a counter is changed to also collect the thing it counted (dates from `AttendancePeriodCalculator.Classify`, over-break dates from a shared helper, overdue tasks from `EmployeeTaskPeriodCalculator.IsOverdue`). A new `GetEmployeeSignalItemsQueryHandler` dispatches on the signal key to those shared rules plus two new list methods on the notification and exception repositories, and to `GetEmployeeApprovalActivityQuery` with a new `AllItems` flag. Visibility gates mirror `GetEmployeeOverviewSignalsQueryHandler` (hidden source → 403).

**Tech Stack:** .NET 10, MediatR, EF Core (PostgreSQL), xUnit + Moq + FluentAssertions.

**Spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/2026-10-01-needs-attention-drilldown-design.md` (§1 is this plan). The frontend plan is `Hrms--Web-application---front-end---v1/docs/superpowers/plans/2026-10-01-needs-attention-drilldown-frontend.md`.

## Global Constraints

- Endpoint: `GET /employees/{employeeId}/overview/signals/{key}/items?from=YYYY-MM-DD&to=YYYY-MM-DD`, `[RequirePermission("employees:read")]`, same as `/overview/signals`.
- 404 for an unknown key; 403 when the signal's source is hidden from the caller or its module is off.
- At most **50** items, newest first; `total` is the full count.
- For every key, `total` must equal that signal's `value` from `GetEmployeeOverviewSignals` for the same period — never write a second copy of a count rule.
- Existing signal counts, ranks, labels and the `/overview/signals` response shape must not change.
- Repo layout: no `.sln`; build/test per project. Before every `dotnet test`, stop the auto-respawning dev API: `taskkill /IM ONEVO.Api.exe /F` (ignore "not found"), then run the test command immediately.
- Shared working tree: other sessions edit this repo concurrently. Stage files by explicit path only; never `git add -A` / `git add .`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Map

| File | Change |
|---|---|
| `src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs` | `Classify` also returns per-violation date lists; new `OverBreakDays(data)` |
| `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs` | use `OverBreakDays` |
| `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs` | `EmployeeTaskPeriodRow` gains task/project identity |
| `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs` | project those fields |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/EmployeeTaskPeriodCalculator.cs` | extract `IsOverdue` |
| `src/ONEVO.Application/Features/Monitoring/Notifications/RepositoryInterfaces/INotificationRepository.cs` + `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Notifications/EfNotificationRepository.cs` | `ListByTypesAsync` |
| `src/ONEVO.Application/Features/Monitoring/Exceptions/RepositoryInterfaces/IExceptionRepository.cs` + `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Exceptions/EfExceptionRepository.cs` | `ListDetectedInRangeAsync` |
| `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/*` | `AllItems` flag |
| `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewWorkModules.cs` | **new** — shared Work-module gate |
| `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeSignalItemsResponses.cs` | **new** |
| `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQuery.cs` | **new** |
| `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs` | **new** |
| `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` | new action |
| `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorViolationDatesTests.cs` | **new** |
| `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodCalculatorOverdueTests.cs` | **new** |
| `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs` | **new** |

---

### Task 1: Attendance violation dates + shared over-break rule

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorViolationDatesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AttendanceViolationDates(IReadOnlyList<DateOnly> Absent, IReadOnlyList<DateOnly> Late, IReadOnlyList<DateOnly> EarlyDepartures, IReadOnlyList<DateOnly> MissingClockOuts, IReadOnlyList<DateOnly> ShortHours, IReadOnlyList<DateOnly> WorkedOnNonWorkingDay, IReadOnlyList<DateOnly> WorkedDuringTimeOff);`
  - `AttendancePeriodClassification` gains a final positional member `AttendanceViolationDates Dates`.
  - `public sealed record OverBreakDay(DateOnly Date, int MinutesOver);`
  - `public static IReadOnlyList<OverBreakDay> AttendancePeriodCalculator.OverBreakDays(AttendancePeriodData data)` — ascending by date.

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorViolationDatesTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendancePeriodCalculatorViolationDatesTests
{
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(5.5), "t", "t");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly Guid Employee = Guid.NewGuid();

    private static AttendanceRecord Rec(DateOnly d, string? start, string? end, int? requiredMinutes = null, int workedMinutes = 0) => new()
    {
        Id = Guid.NewGuid(), EmployeeId = Employee, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = start is null ? null : DateTimeOffset.Parse(start),
        ActualEnd = end is null ? null : DateTimeOffset.Parse(end),
        RequiredWorkMinutes = requiredMinutes, WorkedMinutes = workedMinutes
    };

    private static AttendancePeriodData Data(int? allowance, Dictionary<DateOnly, int> breaks, IReadOnlyList<LeaveRequest> leaves, params AttendanceRecord[] records) =>
        new(records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks, leaves,
            DateTimeOffset.Parse("2026-07-31T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-31T18:30:00+00:00"),
            ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(),
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2020, 1, 1), null, Today));

    [Fact]
    public void Classify_ReturnsTheDatesBehindEveryCount()
    {
        var d3 = new DateOnly(2026, 8, 3); var d4 = new DateOnly(2026, 8, 4); var d5 = new DateOnly(2026, 8, 5);
        var d6 = new DateOnly(2026, 8, 6); var d7 = new DateOnly(2026, 8, 7); var sat = new DateOnly(2026, 8, 8);
        var data = Data(null, new(), Array.Empty<LeaveRequest>(),
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00", 480, 300),  // short hours
            Rec(d4, "2026-08-04T04:15:00+00:00", "2026-08-04T12:00:00+00:00"),            // late
            Rec(d5, "2026-08-05T03:30:00+00:00", "2026-08-05T05:30:00+00:00"),            // early out
            Rec(d6, "2026-08-06T03:30:00+00:00", null),                                   // missing clock-out
            Rec(sat, "2026-08-08T03:30:00+00:00", "2026-08-08T08:00:00+00:00"));          // worked on Saturday
        // d7 (Fri) has no record -> absent

        var c = AttendancePeriodCalculator.Classify(data);

        c.Dates.ShortHours.Should().Equal(d3);
        c.Dates.Late.Should().Equal(d4);
        c.Dates.EarlyDepartures.Should().Equal(d5);
        c.Dates.MissingClockOuts.Should().Equal(d6);
        c.Dates.WorkedOnNonWorkingDay.Should().Equal(sat);
        c.Dates.Absent.Should().Contain(d7);
        // Lists and counts come from the same loop.
        c.Dates.Absent.Should().HaveCount(c.Absent);
        c.Dates.Late.Should().HaveCount(c.Late);
        c.Dates.EarlyDepartures.Should().HaveCount(c.EarlyDepartures);
        c.Dates.MissingClockOuts.Should().HaveCount(c.MissingClockOuts);
        c.Dates.ShortHours.Should().HaveCount(c.ShortHours);
        c.Dates.WorkedOnNonWorkingDay.Should().HaveCount(c.WorkedOnNonWorkingDay);
        c.Dates.WorkedDuringTimeOff.Should().HaveCount(c.WorkedDuringTimeOff);
    }

    [Fact]
    public void OverBreakDays_ListsOnlyDaysStrictlyOverTheAllowance()
    {
        var d3 = new DateOnly(2026, 8, 3); var d4 = new DateOnly(2026, 8, 4); var d5 = new DateOnly(2026, 8, 5);
        var data = Data(60, new() { [d3] = 60, [d4] = 75, [d5] = 90 }, Array.Empty<LeaveRequest>(),
            Rec(d3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(d4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"),
            Rec(d5, "2026-08-05T03:30:00+00:00", "2026-08-05T12:00:00+00:00"));

        AttendancePeriodCalculator.OverBreakDays(data).Should().Equal(
            new OverBreakDay(d4, 15), new OverBreakDay(d5, 30));
    }

    [Fact]
    public void OverBreakDays_IsEmpty_WhenNoAllowanceIsConfigured()
    {
        var d4 = new DateOnly(2026, 8, 4);
        var data = Data(null, new() { [d4] = 500 }, Array.Empty<LeaveRequest>(),
            Rec(d4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"));

        AttendancePeriodCalculator.OverBreakDays(data).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~AttendancePeriodCalculatorViolationDatesTests"
```
Expected: build error — `AttendancePeriodClassification` has no `Dates`; `OverBreakDays` / `OverBreakDay` do not exist.

- [ ] **Step 3: Implement**

In `AttendancePeriodCalculator.cs`, replace the `AttendancePeriodClassification` record with:

```csharp
public sealed record AttendanceViolationDates(
    IReadOnlyList<DateOnly> Absent,
    IReadOnlyList<DateOnly> Late,
    IReadOnlyList<DateOnly> EarlyDepartures,
    IReadOnlyList<DateOnly> MissingClockOuts,
    IReadOnlyList<DateOnly> ShortHours,
    IReadOnlyList<DateOnly> WorkedOnNonWorkingDay,
    IReadOnlyList<DateOnly> WorkedDuringTimeOff);

public sealed record AttendancePeriodClassification(
    int WorkingDays, int Attended, int Late, int EarlyDepartures, int MissingClockOuts,
    int Absent, int Leave, int ShortHours, int WorkedOnNonWorkingDay, int WorkedDuringTimeOff,
    IReadOnlyList<EmployeeAttendanceDay> Days,
    AttendanceViolationDates Dates);

public sealed record OverBreakDay(DateOnly Date, int MinutesOver);
```

Replace the body of `Classify` so every counter is derived from a list (one rule, two outputs):

```csharp
    public static AttendancePeriodClassification Classify(AttendancePeriodData data)
    {
        var w = data.Workdays;
        var byDate = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        int attended = 0, leave = 0;
        var absent = new List<DateOnly>(); var late = new List<DateOnly>(); var early = new List<DateOnly>();
        var missing = new List<DateOnly>(); var shortHours = new List<DateOnly>();
        var offDayWork = new List<DateOnly>(); var leaveWork = new List<DateOnly>();
        var days = new List<EmployeeAttendanceDay>();

        for (var d = w.PeriodFrom; d <= w.PeriodTo; d = d.AddDays(1))
        {
            if (!w.InEffectiveRange(d)) { days.Add(new(d, "none")); continue; }

            var expected = w.ExpectedDates.Contains(d);
            var onLeave = data.ApprovedLeaves.Any(l => CoversDate(l, d));
            byDate.TryGetValue(d, out var record);

            if (record?.ActualStart is not null)
            {
                attended++;
                var isMissing = IsMissingClockOut(record, data.Now);
                var isLate = IsLate(record, data.Timezone);
                if (isMissing) missing.Add(d);
                if (isLate) late.Add(d);
                if (IsEarlyDeparture(record, data.Timezone)) early.Add(d);
                if (!expected) offDayWork.Add(d);
                else if (onLeave) leaveWork.Add(d);
                if (expected && record.ActualEnd is not null && record.RequiredWorkMinutes is int req && record.WorkedMinutes < req)
                    shortHours.Add(d);
                days.Add(new(d, isMissing ? "missing_clock_out" : isLate ? "late" : "present"));
            }
            else if (!expected) days.Add(new(d, "off"));
            else if (onLeave) { leave++; days.Add(new(d, "leave")); }
            else if (d < data.Today) { absent.Add(d); days.Add(new(d, "absent")); }
            else days.Add(new(d, "none"));
        }

        return new AttendancePeriodClassification(
            w.Count, attended, late.Count, early.Count, missing.Count, absent.Count, leave,
            shortHours.Count, offDayWork.Count, leaveWork.Count, days,
            new AttendanceViolationDates(absent, late, early, missing, shortHours, offDayWork, leaveWork));
    }

    /// <summary>Days whose total break exceeded the configured allowance, with the minutes over.
    /// The single over-break rule: the discipline counts and the drill-down list both use it.</summary>
    public static IReadOnlyList<OverBreakDay> OverBreakDays(AttendancePeriodData data)
    {
        if (data.BreakAllowanceMinutes is not int allowance)
            return Array.Empty<OverBreakDay>();

        return data.Records
            .Where(r => data.BreakMinutesByDate.TryGetValue(r.Date, out var used) && used > allowance)
            .Select(r => new OverBreakDay(r.Date, data.BreakMinutesByDate[r.Date] - allowance))
            .OrderBy(o => o.Date)
            .ToList();
    }
```

In `GetEmployeeAttendanceDisciplineQueryHandler.MeasureAsync`, replace the `overBreakDays` / `overBreakMinutes` loop (lines 68-80) with:

```csharp
        var overBreak = AttendancePeriodCalculator.OverBreakDays(data);
        var overBreakDays = overBreak.Count;
        var overBreakMinutes = overBreak.Sum(o => o.MinutesOver);
```

- [ ] **Step 4: Run the new tests plus every existing attendance/discipline/signals test**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TimeAttendance|FullyQualifiedName~EmployeeSignal|FullyQualifiedName~OverviewSignals"
```
Expected: all PASS (existing discipline test `Handle_CountsLateEarlyMissingAndOverBreak_...` still sees 2 days / 45 min).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs src/ONEVO.Application/Features/TimeAttendance/Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodCalculatorViolationDatesTests.cs
git commit -m "feat(attendance): expose the dates behind each overview violation count

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Overdue task identity

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs:29-34`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs:123-143`
- Modify: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/EmployeeTaskPeriodCalculator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodCalculatorOverdueTests.cs`

**Interfaces:**
- Produces:
  - `EmployeeTaskPeriodRow(DateOnly? DueDate, DateTimeOffset? CompletedAt, int ProgressPercent, bool MarksTaskComplete, int? StoryPoints, Guid TaskId = default, string Title = "", Guid ProjectId = default, string ProjectName = "")`
  - `public static bool EmployeeTaskPeriodCalculator.IsOverdue(EmployeeTaskPeriodRow row, DateOnly asOf)`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodCalculatorOverdueTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    [Theory]
    [InlineData("2026-09-14", 0, false, true)]   // past due, open -> overdue
    [InlineData("2026-09-15", 0, false, false)]  // due today -> not overdue
    [InlineData("2026-09-14", 100, false, false)]// progress 100 counts as complete
    [InlineData("2026-09-14", 10, true, false)]  // complete status
    public void IsOverdue_MatchesTheComputeRule(string due, int progress, bool marksComplete, bool expected)
    {
        var row = new EmployeeTaskPeriodRow(DateOnly.Parse(due), null, progress, marksComplete, null);

        EmployeeTaskPeriodCalculator.IsOverdue(row, AsOf).Should().Be(expected);
        EmployeeTaskPeriodCalculator.Compute(new[] { row }, AsOf).Overdue.Should().Be(expected ? 1 : 0);
    }

    [Fact]
    public void IsOverdue_IsFalse_WithoutADueDate() =>
        EmployeeTaskPeriodCalculator.IsOverdue(new EmployeeTaskPeriodRow(null, null, 0, false, null), AsOf).Should().BeFalse();
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeTaskPeriodCalculatorOverdueTests"
```
Expected: build error — `IsOverdue` does not exist.

- [ ] **Step 3: Implement**

`IWorkTaskRepository.cs` — replace the record:

```csharp
public sealed record EmployeeTaskPeriodRow(
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    int ProgressPercent,
    bool MarksTaskComplete,
    int? StoryPoints,
    Guid TaskId = default,
    string Title = "",
    Guid ProjectId = default,
    string ProjectName = "");
```

`EmployeeTaskPeriodCalculator.cs` — add, and use it in `Compute`:

```csharp
    /// <summary>The overdue rule shared by the Overview counts and the drill-down list.</summary>
    public static bool IsOverdue(EmployeeTaskPeriodRow row, DateOnly asOf) =>
        !IsComplete(row) && row.DueDate is { } due && due < asOf;

    private static bool IsComplete(EmployeeTaskPeriodRow row) => row.MarksTaskComplete || row.ProgressPercent >= 100;
```

and in `Compute` change `if (row.MarksTaskComplete || row.ProgressPercent >= 100)` to `if (IsComplete(row))` and `else if (row.DueDate is { } dueDate && dueDate < asOf)` to `else if (IsOverdue(row, asOf))`.

`EfWorkTaskRepository.ListForEmployeePeriodAsync` — add the project join and project the identity fields:

```csharp
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
                  && ((t.DueDate != null && t.DueDate >= fromDate && t.DueDate <= toDate)
                      || (t.CompletedAt != null && t.CompletedAt >= fromUtc && t.CompletedAt < toUtcExclusive)
                      || _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId
                                                      && a.AssignedAt >= fromUtc && a.AssignedAt < toUtcExclusive))
            select new EmployeeTaskPeriodRow(
                t.DueDate, t.CompletedAt, t.ProgressPercent, s.MarksTaskComplete, t.StoryPoints,
                t.Id, t.Title, t.ProjectId, p.Name)
        ).ToListAsync(ct);
```

(`_db.Projects` exists on `ApplicationDbContext`, line ~296; `Project.Name` is the display name.)

- [ ] **Step 4: Build Infrastructure and run Work Management tests**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet build src/ONEVO.Infrastructure
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkManagement"
```
Expected: build succeeds; all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/EmployeeTaskPeriodCalculator.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodCalculatorOverdueTests.cs
git commit -m "feat(work): carry task and project identity on employee period rows

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: List methods for alerts and exception cases; approvals `AllItems`

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/Notifications/RepositoryInterfaces/INotificationRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Notifications/EfNotificationRepository.cs`
- Modify: `src/ONEVO.Application/Features/Monitoring/Exceptions/RepositoryInterfaces/IExceptionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Exceptions/EfExceptionRepository.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQuery.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQueryHandler.cs:119`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeApprovalActivityQueryHandlerTests.cs`

**Interfaces:**
- Produces:
  - `Task<IReadOnlyList<Notification>> INotificationRepository.ListByTypesAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<NotificationType> types, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct)` — newest first.
  - `Task<IReadOnlyList<MonitoringException>> IExceptionRepository.ListDetectedInRangeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct)` — newest first.
  - `GetEmployeeApprovalActivityQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, bool AllItems = false)` — when true, `Items` is not capped at `MaxItems`.

- [ ] **Step 1: Write the failing approvals test**

Add next to `Handle_ReturnsAtMostTenItems_ButCountsEverything` (line ~201), using the file's existing `ArrangeWork` / `Work` helpers:

```csharp
    [Fact]
    public async Task Handle_ReturnsEveryItem_WhenAllItemsIsRequested()
    {
        ArrangeWork(Enumerable.Range(1, 12)
            .Select(i => Work("task_edit", i % 2 == 0 ? "approved" : "pending", Guid.NewGuid(), $"2026-09-{i:00}T08:00:00+00:00"))
            .ToArray());

        var result = await CreateHandler().Handle(
            new GetEmployeeApprovalActivityQuery(_employeeId, From, To, AllItems: true), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(12);
        result.Value.Total.Should().Be(12);
    }
```

- [ ] **Step 2: Run to verify it fails**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeApprovalActivity"
```
Expected: build error — no `AllItems` parameter.

- [ ] **Step 3: Implement**

`GetEmployeeApprovalActivityQuery.cs`:

```csharp
/// <summary>AllItems=true skips the MaxItems cap (used by the Needs Attention drill-down).</summary>
public sealed record GetEmployeeApprovalActivityQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, bool AllItems = false)
    : IRequest<Result<EmployeeApprovalActivityResponse>>;
```

Handler line 119: `ordered.Take(MaxItems).ToList()` → `(request.AllItems ? ordered : ordered.Take(MaxItems)).ToList()`.

`INotificationRepository.cs` — add after `CountByTypeAsync`:

```csharp
    /// <summary>The notifications <see cref="CountByTypeAsync"/> counts (any of <paramref name="types"/>),
    /// newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<Notification>> ListByTypesAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<NotificationType> types,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct);
```

`EfNotificationRepository.cs` — add after `CountByTypeAsync`:

```csharp
    public async Task<IReadOnlyList<Notification>> ListByTypesAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<NotificationType> types,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct) =>
        await _db.MonitoringNotifications.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.EmployeeId == employeeId && types.Contains(n.Type)
                        && n.CreatedAt >= fromUtc && n.CreatedAt < toUtcExclusive)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
```

`IExceptionRepository.cs` — add after `CountDetectedInRangeAsync`:

```csharp
    /// <summary>The cases <see cref="CountDetectedInRangeAsync"/> counts, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<MonitoringException>> ListDetectedInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct);
```

`EfExceptionRepository.cs` — add after `CountDetectedInRangeAsync`:

```csharp
    public async Task<IReadOnlyList<MonitoringException>> ListDetectedInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int take, CancellationToken ct) =>
        await _db.Exceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.EmployeeId == employeeId
                        && e.DetectedAt >= fromUtc && e.DetectedAt < toUtcExclusive)
            .OrderByDescending(e => e.DetectedAt)
            .Take(take)
            .ToListAsync(ct);
```

Any other class implementing these interfaces (test fakes, in-memory stores) must get the same members — find them with `grep -rln ": INotificationRepository\|: IExceptionRepository" src tests` and implement with the same filter over their backing list.

- [ ] **Step 4: Build and run**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet build src/ONEVO.Infrastructure
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeApprovalActivity|FullyQualifiedName~Monitoring"
```
Expected: build succeeds; all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/Monitoring/Notifications/RepositoryInterfaces/INotificationRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Notifications/EfNotificationRepository.cs src/ONEVO.Application/Features/Monitoring/Exceptions/RepositoryInterfaces/IExceptionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/Monitoring/Exceptions/EfExceptionRepository.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQuery.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQueryHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeApprovalActivityQueryHandlerTests.cs
git commit -m "feat(overview): list methods behind monitoring counts; uncapped approval items

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
(Also `git add` any fake repository files changed in Step 3.)

---

### Task 4: Signal items query — contract, gates, attendance keys

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeSignalItemsResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewWorkModules.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeOverviewSignals/GetEmployeeOverviewSignalsQueryHandler.cs:41-42,70-76` (use the shared gate)
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `AttendancePeriodCalculator.Classify(...).Dates`, `AttendancePeriodCalculator.OverBreakDays` (Task 1).
- Produces (later tasks and the frontend rely on these exact names; JSON is camelCase):

```csharp
public sealed record EmployeeSignalItem(
    string Kind, string Id, DateOnly Date, string Title, string? Subtitle,
    DateTimeOffset? OccurredAt = null,
    Guid? ProjectId = null, string? ProjectName = null, DateOnly? DueDate = null,
    string? ApprovalKind = null, DateTimeOffset? RequestedAt = null, string? ApproverName = null);

public sealed record EmployeeSignalItemsResponse(
    string Key, int Total, string? Timezone, IReadOnlyList<EmployeeSignalItem> Items);

public sealed record GetEmployeeSignalItemsQuery(Guid EmployeeId, string Key, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeSignalItemsResponse>>;
```

`Kind` values: `attendance_day`, `monitoring_alert`, `exception_case`, `task`, `approval`. `GetEmployeeSignalItemsQueryHandler.MaxItems = 50`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs`. It includes the arrange code for every later key so Tasks 5-6 only add `[Fact]`s:

```csharp
using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeSignalItemsQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeAttendancePeriodReader> _reader = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IModuleEntitlementService> _modules = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IExceptionRepository> _exceptions = new();
    private readonly Mock<IExceptionScopeResolver> _exceptionScope = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromHours(5.5), "c", "c");
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    public GetEmployeeSignalItemsQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _modules.Setup(m => m.IsModuleEnabledAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(true, Guid.NewGuid(), Array.Empty<Guid>()));
        ArrangeAttendance(null, new Dictionary<DateOnly, int>());
    }

    private GetEmployeeSignalItemsQueryHandler CreateHandler() =>
        new(_guard.Object, _reader.Object, _tasks.Object, _sender.Object, _modules.Object, _toggles.Object,
            _notifications.Object, _exceptions.Object, _exceptionScope.Object, _user.Object, _clock.Object);

    private Task<Result<EmployeeSignalItemsResponse>> Run(string key) =>
        CreateHandler().Handle(new GetEmployeeSignalItemsQuery(_employeeId, key, From, To), CancellationToken.None);

    private AttendanceRecord Rec(DateOnly d, string start, string? end) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = d, ExpectedWorkingDay = true,
        ScheduledStart = new TimeOnly(9, 0), ScheduledEnd = new TimeOnly(17, 30),
        ActualStart = DateTimeOffset.Parse(start), ActualEnd = end is null ? null : DateTimeOffset.Parse(end)
    };

    private void ArrangeAttendance(int? allowance, Dictionary<DateOnly, int> breaks, params AttendanceRecord[] records) =>
        _reader.Setup(r => r.LoadAsync(_tenantId, _employeeId, It.IsAny<Guid?>(), It.IsAny<EmployeePeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendancePeriodData(
                records, Colombo, DateTimeOffset.Parse("2026-08-21T00:00:00+00:00"), Today, allowance, breaks,
                Array.Empty<LeaveRequest>(), DateTimeOffset.Parse("2026-07-31T18:30:00+00:00"), DateTimeOffset.Parse("2026-08-31T18:30:00+00:00"),
                ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), From, To, new DateOnly(2020, 1, 1), null, Today)));

    private static readonly DateOnly D3 = new(2026, 8, 3);
    private static readonly DateOnly D4 = new(2026, 8, 4);
    private static readonly DateOnly D5 = new(2026, 8, 5);

    [Fact]
    public async Task UnknownKey_Returns404()
    {
        var result = await Run("not_a_signal");
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AccessDenied_PassesThroughTheGuardStatus()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Failure("nope", 403));
        (await Run("late_clock_ins")).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task LateClockIns_ListsTheLateDays_NewestFirst_WithTheEmployeeTimezone()
    {
        ArrangeAttendance(null, new(),
            Rec(D3, "2026-08-03T04:15:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"),
            Rec(D5, "2026-08-05T04:00:00+00:00", "2026-08-05T12:00:00+00:00"));

        var r = (await Run("late_clock_ins")).Value!;

        r.Key.Should().Be("late_clock_ins");
        r.Timezone.Should().Be("Asia/Colombo");
        r.Total.Should().Be(2);
        r.Items.Select(i => i.Date).Should().Equal(D5, D3);
        r.Items.Should().OnlyContain(i => i.Kind == "attendance_day" && i.Id == i.Date.ToString("yyyy-MM-dd"));
        r.Items[0].Title.Should().Be("Wed 5 Aug");
    }

    [Fact]
    public async Task OverBreak_ListsDaysWithMinutesOver()
    {
        ArrangeAttendance(60, new() { [D3] = 60, [D4] = 95 },
            Rec(D3, "2026-08-03T03:30:00+00:00", "2026-08-03T12:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", "2026-08-04T12:00:00+00:00"));

        var r = (await Run("over_break")).Value!;

        r.Total.Should().Be(1);
        r.Items.Single().Date.Should().Be(D4);
        r.Items.Single().Subtitle.Should().Be("35 min over allowance");
    }

    [Theory]
    [InlineData("absent_days")]
    [InlineData("missing_clock_outs")]
    [InlineData("late_clock_ins")]
    [InlineData("early_clock_outs")]
    [InlineData("short_hours_days")]
    [InlineData("off_schedule_work")]
    public async Task AttendanceKeys_TotalEqualsTheClassifierCountBehindTheSignal(string key)
    {
        var records = new[]
        {
            Rec(D3, "2026-08-03T04:15:00+00:00", "2026-08-03T05:00:00+00:00"),
            Rec(D4, "2026-08-04T03:30:00+00:00", null),
            Rec(new DateOnly(2026, 8, 8), "2026-08-08T03:30:00+00:00", "2026-08-08T08:00:00+00:00")
        };
        ArrangeAttendance(null, new(), records);
        var data = await _reader.Object.LoadAsync(_tenantId, _employeeId, null, EmployeePeriod.Resolve(From, To, Today).Value!);
        var c = AttendancePeriodCalculator.Classify(data);
        var expected = key switch
        {
            "absent_days" => c.Absent, "missing_clock_outs" => c.MissingClockOuts, "late_clock_ins" => c.Late,
            "early_clock_outs" => c.EarlyDepartures, "short_hours_days" => c.ShortHours,
            _ => c.WorkedOnNonWorkingDay + c.WorkedDuringTimeOff
        };

        var r = (await Run(key)).Value!;

        r.Total.Should().Be(expected);
        r.Items.Should().HaveCount(Math.Min(expected, GetEmployeeSignalItemsQueryHandler.MaxItems));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests"
```
Expected: build error — `GetEmployeeSignalItemsQueryHandler` etc. do not exist.

- [ ] **Step 3: Implement**

`EmployeeSignalItemsResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>One concrete thing behind a Needs Attention signal. Kind is attendance_day |
/// monitoring_alert | exception_case | task | approval; Id is the record id, or the yyyy-MM-dd date
/// for attendance_day. Task-only and approval-only fields are null for other kinds.</summary>
public sealed record EmployeeSignalItem(
    string Kind,
    string Id,
    DateOnly Date,
    string Title,
    string? Subtitle,
    DateTimeOffset? OccurredAt = null,
    Guid? ProjectId = null,
    string? ProjectName = null,
    DateOnly? DueDate = null,
    string? ApprovalKind = null,
    DateTimeOffset? RequestedAt = null,
    string? ApproverName = null);

/// <summary>Total is the full count (equals the signal's Value); Items holds at most 50, newest
/// first. Timezone is the employee's attendance timezone for attendance and alert keys.</summary>
public sealed record EmployeeSignalItemsResponse(
    string Key, int Total, string? Timezone, IReadOnlyList<EmployeeSignalItem> Items);
```

`EmployeeOverviewWorkModules.cs`:

```csharp
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>The Work Management gate for overview signals: any one of these modules enables the
/// overdue-tasks signal and its drill-down.</summary>
public static class EmployeeOverviewWorkModules
{
    private static readonly string[] Keys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public static async Task<bool> IsEnabledAsync(IModuleEntitlementService modules, Guid tenantId, CancellationToken ct)
    {
        foreach (var key in Keys)
            if (await modules.IsModuleEnabledAsync(tenantId, key, ct))
                return true;
        return false;
    }
}
```

In `GetEmployeeOverviewSignalsQueryHandler`, delete the `WorkModules` field and the private `WorkEnabled` method, and change line 60's `await WorkEnabled(tenantId, ct)` to `await EmployeeOverviewWorkModules.IsEnabledAsync(modules, tenantId, ct)`.

`GetEmployeeSignalItemsQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;

public sealed record GetEmployeeSignalItemsQuery(Guid EmployeeId, string Key, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeSignalItemsResponse>>;
```

`GetEmployeeSignalItemsQueryHandler.cs` (attendance keys now; monitoring/task/approval branches are added in Tasks 5-6 and return 404 until then):

```csharp
using System.Globalization;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;

/// <summary>
/// The concrete items behind one Overview signal (see <see cref="EmployeeSignalCatalogue"/>).
/// Every branch reuses the exact rule that produces that signal's count, so Total always equals
/// the signal's Value. A source the signals endpoint would omit (module off, toggle off, outside
/// exception scope) is a 403 here.
/// </summary>
public sealed class GetEmployeeSignalItemsQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeAttendancePeriodReader reader,
    IWorkTaskRepository tasks,
    ISender sender,
    IModuleEntitlementService modules,
    IMonitoringToggleResolver toggles,
    INotificationRepository notifications,
    IExceptionRepository exceptions,
    IExceptionScopeResolver exceptionScope,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeSignalItemsQuery, Result<EmployeeSignalItemsResponse>>
{
    public const int MaxItems = 50;

    private static readonly HashSet<string> AttendanceKeys =
        ["absent_days", "missing_clock_outs", "late_clock_ins", "early_clock_outs", "short_hours_days", "off_schedule_work", "over_break"];

    public async Task<Result<EmployeeSignalItemsResponse>> Handle(GetEmployeeSignalItemsQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeSignalItemsResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeSignalItemsResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        if (AttendanceKeys.Contains(request.Key))
        {
            var data = await reader.LoadAsync(tenantId, request.EmployeeId, access.Value!.LegalEntityId, period.Value!, ct);
            return Result<EmployeeSignalItemsResponse>.Success(AttendanceItems(request.Key, data));
        }

        return NotFound(request.Key);
    }

    private static EmployeeSignalItemsResponse AttendanceItems(string key, AttendancePeriodData data)
    {
        if (key == "over_break")
        {
            var over = AttendancePeriodCalculator.OverBreakDays(data);
            return Page(key, data.Timezone.Id, over.Select(o => Day(o.Date, $"{o.MinutesOver} min over allowance")).ToList());
        }

        var dates = AttendancePeriodCalculator.Classify(data).Dates;
        var items = key switch
        {
            "absent_days" => dates.Absent.Select(d => Day(d, "No clock-in or approved leave")),
            "missing_clock_outs" => dates.MissingClockOuts.Select(d => Day(d, "No clock-out recorded")),
            "late_clock_ins" => dates.Late.Select(d => Day(d, "Clocked in after the scheduled start")),
            "early_clock_outs" => dates.EarlyDepartures.Select(d => Day(d, "Left before the scheduled end")),
            "short_hours_days" => dates.ShortHours.Select(d => Day(d, "Worked less than the required hours")),
            _ => dates.WorkedOnNonWorkingDay.Select(d => Day(d, "Worked on a non-working day"))
                .Concat(dates.WorkedDuringTimeOff.Select(d => Day(d, "Worked during approved time off")))
        };
        return Page(key, data.Timezone.Id, items.ToList());
    }

    private static EmployeeSignalItem Day(DateOnly date, string subtitle) =>
        new("attendance_day", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date,
            date.ToString("ddd d MMM", CultureInfo.InvariantCulture), subtitle);

    /// <summary>Newest first, capped at MaxItems; Total is the uncapped count.</summary>
    private static EmployeeSignalItemsResponse Page(string key, string? timezone, IReadOnlyList<EmployeeSignalItem> all) =>
        new(key, all.Count, timezone,
            all.OrderByDescending(i => i.Date).ThenByDescending(i => i.OccurredAt).Take(MaxItems).ToList());

    private static Result<EmployeeSignalItemsResponse> NotFound(string key) =>
        Result<EmployeeSignalItemsResponse>.Failure($"Unknown overview signal '{key}'.", 404);

    private static Result<EmployeeSignalItemsResponse> Hidden() =>
        Result<EmployeeSignalItemsResponse>.Failure("This signal's source is not available for this employee.", 403);
}
```

(`Hidden()` and the unused constructor dependencies are used by Tasks 5-6; leave them.)

- [ ] **Step 4: Run tests**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests|FullyQualifiedName~OverviewSignals"
```
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeSignalItemsResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewWorkModules.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQuery.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeOverviewSignals/GetEmployeeOverviewSignalsQueryHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs
git commit -m "feat(overview): signal items query for attendance violations

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Monitoring keys (location, idle, exceptions)

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `INotificationRepository.ListByTypesAsync`, `IExceptionRepository.ListDetectedInRangeAsync` (Task 3); `CountByTypeAsync`, `CountDetectedInRangeAsync` (existing) for `Total`.
- Window rules (copy of the count rules): `location_violations` uses `data.RangeStartUtc`/`data.RangeEndUtc` from the attendance reader (as the discipline handler does); `idle_activity_alerts` and `monitoring_exceptions` use UTC day bounds `from 00:00Z` → `(to+1) 00:00Z` (as `GetEmployeeOverviewSignalsQueryHandler.MonitoringAsync` does).

- [ ] **Step 1: Write the failing tests** (append to the test class)

```csharp
    private static readonly DateTimeOffset UtcStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UtcEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private Notification Alert(NotificationType type, string at) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Type = type,
        Title = "Low activity", Message = "No input for 30 min", CreatedAt = DateTimeOffset.Parse(at)
    };

    [Fact]
    public async Task IdleAlerts_ListsBothAlertTypes_WithTotalFromTheCounts()
    {
        var alerts = new[] { Alert(NotificationType.LongIdleAlert, "2026-08-04T06:00:00+00:00"), Alert(NotificationType.LowActivityAlert, "2026-08-05T07:30:00+00:00") };
        _notifications.Setup(n => n.ListByTypesAsync(_tenantId, _employeeId,
                It.Is<IReadOnlyCollection<NotificationType>>(t => t.Count == 2 && t.Contains(NotificationType.LongIdleAlert) && t.Contains(NotificationType.LowActivityAlert)),
                UtcStart, UtcEnd, GetEmployeeSignalItemsQueryHandler.MaxItems, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alerts.OrderByDescending(a => a.CreatedAt).ToList());
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.LongIdleAlert, UtcStart, UtcEnd, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.LowActivityAlert, UtcStart, UtcEnd, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var r = (await Run("idle_activity_alerts")).Value!;

        r.Total.Should().Be(2);
        r.Items.Should().OnlyContain(i => i.Kind == "monitoring_alert");
        r.Items[0].Date.Should().Be(new DateOnly(2026, 8, 5));          // local date in employee timezone
        r.Items[0].OccurredAt.Should().Be(DateTimeOffset.Parse("2026-08-05T07:30:00+00:00"));
        r.Items[0].Title.Should().Be("Low activity");
    }

    [Fact]
    public async Task IdleAlerts_Returns403_WhenActivityMonitoringIsOff()
    {
        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await Run("idle_activity_alerts")).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task LocationViolations_UseTheAttendanceRange_And403WhenVerificationIsOff()
    {
        var rangeStart = DateTimeOffset.Parse("2026-07-31T18:30:00+00:00");
        var rangeEnd = DateTimeOffset.Parse("2026-08-31T18:30:00+00:00");
        _notifications.Setup(n => n.ListByTypesAsync(_tenantId, _employeeId,
                It.Is<IReadOnlyCollection<NotificationType>>(t => t.Single() == NotificationType.OutsideWorkLocationAlert),
                rangeStart, rangeEnd, GetEmployeeSignalItemsQueryHandler.MaxItems, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Alert(NotificationType.OutsideWorkLocationAlert, "2026-08-04T05:00:00+00:00") });
        _notifications.Setup(n => n.CountByTypeAsync(_tenantId, _employeeId, NotificationType.OutsideWorkLocationAlert, rangeStart, rangeEnd, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        (await Run("location_violations")).Value!.Total.Should().Be(1);

        _toggles.Setup(t => t.IsEnabledAsync(_tenantId, _employeeId, MonitoringCapability.WorkLocationVerification, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await Run("location_violations")).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task MonitoringExceptions_ListCases_And403OutsideTheExceptionScope()
    {
        var c = new ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Title = "Sustained low activity",
            Description = "3 days below threshold", DetectedAt = DateTimeOffset.Parse("2026-08-06T04:00:00+00:00")
        };
        _exceptions.Setup(e => e.ListDetectedInRangeAsync(_tenantId, _employeeId, UtcStart, UtcEnd, GetEmployeeSignalItemsQueryHandler.MaxItems, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { c });
        _exceptions.Setup(e => e.CountDetectedInRangeAsync(_tenantId, _employeeId, UtcStart, UtcEnd, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var r = (await Run("monitoring_exceptions")).Value!;
        r.Items.Single().Should().Match<EmployeeSignalItem>(i => i.Kind == "exception_case" && i.Id == c.Id.ToString() && i.Title == "Sustained low activity");

        _exceptionScope.Setup(s => s.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionScope(false, Guid.NewGuid(), Array.Empty<Guid>()));
        (await Run("monitoring_exceptions")).StatusCode.Should().Be(403);
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests"
```
Expected: the 4 new tests FAIL with status 404 (keys not handled yet).

- [ ] **Step 3: Implement** — in `Handle`, before `return NotFound(request.Key);` add:

```csharp
        var legalEntityId = access.Value!.LegalEntityId;
        var (from, to) = (period.Value!.From, period.Value.To);
        var utcStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var utcEnd = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        switch (request.Key)
        {
            case "location_violations":
            {
                if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.WorkLocationVerification, ct))
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                return await AlertsAsync(tenantId, request, data.Timezone, [NotificationType.OutsideWorkLocationAlert], data.RangeStartUtc, data.RangeEndUtc, ct);
            }
            case "idle_activity_alerts":
            {
                if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                return await AlertsAsync(tenantId, request, data.Timezone, [NotificationType.LongIdleAlert, NotificationType.LowActivityAlert], utcStart, utcEnd, ct);
            }
            case "monitoring_exceptions":
            {
                if (!await toggles.IsEnabledAsync(tenantId, request.EmployeeId, MonitoringCapability.ActivityMonitoring, ct))
                    return Hidden();
                var scope = await exceptionScope.ResolveAsync(forAction: false, [request.EmployeeId], ct);
                if (scope?.CanSee(request.EmployeeId) != true)
                    return Hidden();
                var data = await reader.LoadAsync(tenantId, request.EmployeeId, legalEntityId, period.Value, ct);
                var total = await exceptions.CountDetectedInRangeAsync(tenantId, request.EmployeeId, utcStart, utcEnd, ct);
                var cases = await exceptions.ListDetectedInRangeAsync(tenantId, request.EmployeeId, utcStart, utcEnd, MaxItems, ct);
                var items = cases.Select(c => new EmployeeSignalItem(
                    "exception_case", c.Id.ToString(), LocalDate(c.DetectedAt, data.Timezone), c.Title, c.Description,
                    OccurredAt: c.DetectedAt)).ToList();
                return Result<EmployeeSignalItemsResponse>.Success(new(request.Key, total, data.Timezone.Id, items));
            }
        }
```

Add the helpers to the class:

```csharp
    private async Task<Result<EmployeeSignalItemsResponse>> AlertsAsync(
        Guid tenantId, GetEmployeeSignalItemsQuery request, TimeZoneInfo timezone, NotificationType[] types,
        DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
    {
        var total = 0;
        foreach (var type in types)
            total += await notifications.CountByTypeAsync(tenantId, request.EmployeeId, type, fromUtc, toUtcExclusive, ct);
        var rows = await notifications.ListByTypesAsync(tenantId, request.EmployeeId, types, fromUtc, toUtcExclusive, MaxItems, ct);
        var items = rows.Select(n => new EmployeeSignalItem(
            "monitoring_alert", n.Id.ToString(), LocalDate(n.CreatedAt, timezone), n.Title, n.Message,
            OccurredAt: n.CreatedAt)).ToList();
        return Result<EmployeeSignalItemsResponse>.Success(new(request.Key, total, timezone.Id, items));
    }

    private static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo timezone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timezone).DateTime);
```

Add `using ONEVO.Domain.Features.Monitoring.Notifications.Entities;` at the top.

- [ ] **Step 4: Run tests**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests"
```
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs
git commit -m "feat(overview): signal items for location, idle and exception signals

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Overdue tasks and pending approvals keys

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `EmployeeTaskPeriodCalculator.IsOverdue`, extended `EmployeeTaskPeriodRow` (Task 2); `GetEmployeeApprovalActivityQuery(..., AllItems: true)` (Task 3); `EmployeeOverviewWorkModules.IsEnabledAsync` (Task 4).
- Overdue `asOf` must match `GetEmployeeWorkOverviewQueryHandler`: `period.To < clock.Today ? period.To : clock.Today`.

- [ ] **Step 1: Write the failing tests** (append)

```csharp
    [Fact]
    public async Task OverdueTasks_ListOnlyOverdueRows_WithProjectIdentity()
    {
        var project = Guid.NewGuid();
        var overdue = new EmployeeTaskPeriodRow(new DateOnly(2026, 8, 10), null, 20, false, null, Guid.NewGuid(), "Write API docs", project, "Apollo");
        var done = new EmployeeTaskPeriodRow(new DateOnly(2026, 8, 10), null, 100, false, null, Guid.NewGuid(), "Done", project, "Apollo");
        var future = new EmployeeTaskPeriodRow(new DateOnly(2026, 8, 25), null, 0, false, null, Guid.NewGuid(), "Later", project, "Apollo");
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { overdue, done, future });

        var r = (await Run("overdue_tasks")).Value!;

        r.Total.Should().Be(1);
        r.Timezone.Should().BeNull();
        r.Items.Single().Should().Match<EmployeeSignalItem>(i =>
            i.Kind == "task" && i.Id == overdue.TaskId.ToString() && i.Title == "Write API docs"
            && i.ProjectId == project && i.ProjectName == "Apollo" && i.DueDate == new DateOnly(2026, 8, 10));
    }

    [Fact]
    public async Task OverdueTasks_Returns403_WhenNoWorkModuleIsEnabled()
    {
        _modules.Setup(m => m.IsModuleEnabledAsync(_tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await Run("overdue_tasks")).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task PendingApprovals_ListOnlyPendingItems_FromTheUncappedQuery()
    {
        var pending = new EmployeeApprovalItem("a1", "leave", "Leave request", "Annual · 2026-10-03 → 2026-10-05", "pending", DateTimeOffset.Parse("2026-08-28T05:00:00+00:00"), null, null);
        var approved = pending with { Id = "a2", Status = "approved" };
        _sender.Setup(s => s.Send(It.Is<GetEmployeeApprovalActivityQuery>(q => q.AllItems && q.EmployeeId == _employeeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeApprovalActivityResponse>.Success(new(From, To, 1, 1, 0, 2, new[] { pending, approved })));

        var r = (await Run("pending_approvals")).Value!;

        r.Total.Should().Be(1);
        r.Items.Single().Should().Match<EmployeeSignalItem>(i =>
            i.Kind == "approval" && i.Id == "a1" && i.ApprovalKind == "leave" && i.Title == "Leave request"
            && i.Subtitle == "Annual · 2026-10-03 → 2026-10-05" && i.RequestedAt == pending.RequestedAt);
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests"
```
Expected: the 3 new tests FAIL with 404.

- [ ] **Step 3: Implement** — add two cases to the `switch` in `Handle`:

```csharp
            case "overdue_tasks":
            {
                if (!await EmployeeOverviewWorkModules.IsEnabledAsync(modules, tenantId, ct))
                    return Hidden();
                var rows = await tasks.ListForEmployeePeriodAsync(tenantId, request.EmployeeId, from, to, ct);
                var asOf = to < clock.Today ? to : clock.Today;
                var items = rows
                    .Where(r => EmployeeTaskPeriodCalculator.IsOverdue(r, asOf))
                    .Select(r => new EmployeeSignalItem(
                        "task", r.TaskId.ToString(), r.DueDate!.Value, r.Title, r.ProjectName,
                        ProjectId: r.ProjectId, ProjectName: r.ProjectName, DueDate: r.DueDate))
                    .ToList();
                return Result<EmployeeSignalItemsResponse>.Success(Page(request.Key, null, items));
            }
            case "pending_approvals":
            {
                var activity = await sender.Send(new GetEmployeeApprovalActivityQuery(request.EmployeeId, from, to, AllItems: true), ct);
                if (!activity.IsSuccess)
                    return Result<EmployeeSignalItemsResponse>.Failure(activity.Error!, activity.StatusCode ?? 400);
                var items = activity.Value!.Items
                    .Where(i => i.Status == "pending")
                    .Select(i => new EmployeeSignalItem(
                        "approval", i.Id, DateOnly.FromDateTime(i.RequestedAt.UtcDateTime), i.Label, i.Detail,
                        ApprovalKind: i.Kind, RequestedAt: i.RequestedAt, ApproverName: i.ApproverName))
                    .ToList();
                return Result<EmployeeSignalItemsResponse>.Success(Page(request.Key, null, items));
            }
```

Add usings: `ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity` and `ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services`.

- [ ] **Step 4: Run tests**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeSignalItemsQueryHandlerTests"
```
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeSignalItems/GetEmployeeSignalItemsQueryHandler.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeSignalItemsQueryHandlerTests.cs
git commit -m "feat(overview): signal items for overdue tasks and pending approvals

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Controller endpoint, full suite, live check

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (after `GetOverviewSignals`, ~line 253)

**Interfaces:**
- Consumes: `GetEmployeeSignalItemsQuery` (Task 4).
- Produces: `GET /employees/{id}/overview/signals/{key}/items?from&to` → `EmployeeSignalItemsResponse` (camelCase JSON: `key, total, timezone, items[{kind,id,date,title,subtitle,occurredAt,projectId,projectName,dueDate,approvalKind,requestedAt,approverName}]`).

- [ ] **Step 1: Add the action**

```csharp
    /// <summary>Needs Attention drill-down: the days / alerts / cases / tasks / approvals behind one
    /// overview signal (at most 50, newest first; total is the full count). 404 for an unknown key,
    /// 403 when that signal's source is hidden from the caller.</summary>
    [HttpGet("{id:guid}/overview/signals/{key}/items")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewSignalItems(
        Guid id, string key, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeSignalItemsQuery(id, key, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;`.

- [ ] **Step 2: Build the API and run the whole unit suite**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet build src/ONEVO.Api
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Unit
```
Expected: build succeeds; the unit suite is fully green. Paste the final `Passed!/Failed!` summary line into the task report — do not summarize it.

- [ ] **Step 3: Architecture tests**

```bash
taskkill /IM ONEVO.Api.exe /F
dotnet test tests/ONEVO.Tests.Architecture
```
Expected: PASS.

- [ ] **Step 4: Live check against the dev tenant**

Start the API (the user's usual way; it auto-respawns once its process exists). With an authenticated session on a dev tenant (`*.onexso.com`), call for one employee who has activity this month:

```
GET /employees/{id}/overview/signals?from=2026-09-01&to=2026-09-30
GET /employees/{id}/overview/signals/{each returned key}/items?from=2026-09-01&to=2026-09-30
GET /employees/{id}/overview/signals/not_a_key/items
```
Expected: for each returned signal, `items.total == signal.value`; the unknown key returns 404. Record the actual numbers in the task report.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs
git commit -m "feat(overview): expose GET overview/signals/{key}/items

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
