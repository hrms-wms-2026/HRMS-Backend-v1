# Employee Overview Backend — Part 1: Expected Working Days & Absences

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Working days / absences on the employee Overview come from the legal entity's General-settings working week and holidays, clipped to hire/termination/today — not from whichever `attendance_records` rows happen to exist.

**Architecture:** A pure static `ExpectedWorkdayCalendar.Build(...)` turns (working weekdays, holiday dates, period, hire, termination, today) into the expected-date set. `EmployeeAttendancePeriodReader` loads its inputs (employee row + tenant holiday events) and puts the result on `AttendancePeriodData.Workdays`. A new `AttendancePeriodCalculator.Classify(data)` walks **every date** of the period and returns counts + per-day statuses; the Overview attendance, discipline and self-service monthly summary handlers switch to it.

**Tech Stack:** .NET 9, MediatR, EF Core (PostgreSQL), xUnit + FluentAssertions + Moq.

**Spec:** `docs/superpowers/specs/next/2026-09-30-employee-overview-signals-and-period-correctness-design.md` (Section 1). Part 2 (signals endpoint) consumes the new response fields.

## Global Constraints

- Repo `C:\onevoNew\HRMS-Backend-v1`, branch `feature/task-subtasks`. No `.sln` — build per project: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`, test `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`.
- The dev API (`ONEVO.Api.exe`) auto-respawns and locks DLLs: kill it immediately before every `dotnet test` (`taskkill /F /IM ONEVO.Api.exe`).
- Weekday numbering is ISO (Mon=1 … Sun=7), same as `LegalEntity.StandardWorkingDays` (default `[1,2,3,4,5]`).
- Holidays are tenant-wide `CalendarEvent` rows with `SourceType == CalendarEventSourceTypes.Holiday`, `StartDate` = UTC midnight of the holiday.
- Commit attribution line: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No DB migration in this part.

---

### Task 1: `ExpectedWorkdayCalendar` (pure)

**Files:**
- Create: `src/ONEVO.Application/Features/TimeAttendance/Services/ExpectedWorkdayCalendar.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendanceScheduleResolver.cs` (`ParseWorkingDays` private → `public static IReadOnlySet<int> ParseWorkingDays`)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/ExpectedWorkdayCalendarTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record ExpectedWorkdays(
    DateOnly PeriodFrom, DateOnly PeriodTo,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo,   // null when the employee was not employed in the period
    IReadOnlySet<DateOnly> ExpectedDates,
    IReadOnlySet<DateOnly> HolidayDates)
{
    public int Count => ExpectedDates.Count;
    public bool InEffectiveRange(DateOnly d) => EffectiveFrom is DateOnly f && EffectiveTo is DateOnly t && d >= f && d <= t;
}
public static class ExpectedWorkdayCalendar
{
    public static ExpectedWorkdays Build(IReadOnlySet<int> workingWeekdays, IReadOnlySet<DateOnly> holidays,
        DateOnly from, DateOnly to, DateOnly hireDate, DateOnly? terminationDate, DateOnly today);
    public static int IsoWeekday(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class ExpectedWorkdayCalendarTests
{
    private static readonly IReadOnlySet<int> MonFri = new HashSet<int> { 1, 2, 3, 4, 5 };
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();
    private static readonly DateOnly Sep1 = new(2026, 9, 1), Sep30 = new(2026, 9, 30);

    [Fact]
    public void FullMonth_CountsWeekdaysOnly()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(22); // Sep 2026: 22 weekdays (Tue 1st .. Wed 30th)
        r.ExpectedDates.Should().NotContain(new DateOnly(2026, 9, 5)); // Saturday
    }

    [Fact]
    public void Holidays_AreExcludedAndReported()
    {
        var holiday = new DateOnly(2026, 9, 14);
        var r = ExpectedWorkdayCalendar.Build(MonFri, new HashSet<DateOnly> { holiday }, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(21);
        r.HolidayDates.Should().Contain(holiday);
    }

    [Fact]
    public void MidMonthHire_ClipsTheStart()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2026, 9, 21), null, Sep30);
        r.EffectiveFrom.Should().Be(new DateOnly(2026, 9, 21));
        r.Count.Should().Be(8);
    }

    [Fact]
    public void Termination_ClipsTheEnd()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), new DateOnly(2026, 9, 4), Sep30);
        r.Count.Should().Be(4);
    }

    [Fact]
    public void Today_ClipsTheEnd_ButIncludesToday()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, new DateOnly(2026, 9, 2));
        r.Count.Should().Be(2);
    }

    [Fact]
    public void NotEmployedInPeriod_IsEmpty()
    {
        var r = ExpectedWorkdayCalendar.Build(MonFri, NoHolidays, Sep1, Sep30, new DateOnly(2026, 10, 5), null, Sep30);
        r.Count.Should().Be(0);
        r.EffectiveFrom.Should().BeNull();
        r.InEffectiveRange(new DateOnly(2026, 9, 10)).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify failure** — `taskkill /F /IM ONEVO.Api.exe; dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter ExpectedWorkdayCalendarTests` → compile error (type missing).

- [ ] **Step 3: Implement**

```csharp
namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed record ExpectedWorkdays(
    DateOnly PeriodFrom, DateOnly PeriodTo,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    IReadOnlySet<DateOnly> ExpectedDates,
    IReadOnlySet<DateOnly> HolidayDates)
{
    public int Count => ExpectedDates.Count;
    public bool InEffectiveRange(DateOnly d) =>
        EffectiveFrom is DateOnly f && EffectiveTo is DateOnly t && d >= f && d <= t;
}

/// <summary>The days an employee was expected to work in a period: the legal entity's standard
/// working weekdays (General settings) minus synced holidays, clipped to hire date, termination date
/// and today. Attendance rows only exist once someone clocks in, so they can never be the source of
/// "expected" days - that was the 0/0 vs 1/1 bug.</summary>
public static class ExpectedWorkdayCalendar
{
    public static int IsoWeekday(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    public static ExpectedWorkdays Build(
        IReadOnlySet<int> workingWeekdays, IReadOnlySet<DateOnly> holidays,
        DateOnly from, DateOnly to, DateOnly hireDate, DateOnly? terminationDate, DateOnly today)
    {
        var start = from > hireDate ? from : hireDate;
        var end = to;
        if (terminationDate is DateOnly term && term < end) end = term;
        if (today < end) end = today;

        var inRangeHolidays = holidays.Where(h => h >= from && h <= to).ToHashSet();
        if (start > end)
            return new ExpectedWorkdays(from, to, null, null, new HashSet<DateOnly>(), inRangeHolidays);

        var expected = new HashSet<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(1))
            if (workingWeekdays.Contains(IsoWeekday(d)) && !inRangeHolidays.Contains(d))
                expected.Add(d);

        return new ExpectedWorkdays(from, to, start, end, expected, inRangeHolidays);
    }
}
```

In `AttendanceScheduleResolver.cs` change `private static HashSet<int> ParseWorkingDays(string? json)` to `public static HashSet<int> ParseWorkingDays(string? json)` (body unchanged), and extract the existing `scheduleConfigured` expression from `ResolveForDate` into a public helper that `ResolveForDate` then calls:

```csharp
    /// <summary>Same rule clock-in uses: timezone set and resolvable, start and end set, start &lt; end.
    /// When false, clock-in treats every day as a non-working day - the Overview must too.</summary>
    public static bool IsScheduleConfigured(LegalEntity legalEntity) =>
        !string.IsNullOrWhiteSpace(legalEntity.Timezone)
        && TryFindTimezone(legalEntity.Timezone!, out _)
        && legalEntity.WorkStartTime is not null
        && legalEntity.WorkEndTime is not null
        && legalEntity.WorkStartTime.Value < legalEntity.WorkEndTime.Value;
```

Add to `ExpectedWorkdayCalendarTests`:
```csharp
    [Fact]
    public void NoWorkingWeekdays_MeansNoExpectedDays() // unconfigured schedule -> reader passes an empty set
    {
        var r = ExpectedWorkdayCalendar.Build(new HashSet<int>(), NoHolidays, Sep1, Sep30, new DateOnly(2020, 1, 1), null, Sep30);
        r.Count.Should().Be(0);
    }
```
and to the existing resolver tests (grep `AttendanceScheduleResolver` under `tests/ONEVO.Tests.Unit`): `IsScheduleConfigured` is false for a null/blank timezone, for a missing start or end, and for start ≥ end; true for `Asia/Colombo` 09:00–17:00.

- [ ] **Step 4: Run tests** — same command → 7 passed, plus the new resolver cases.
- [ ] **Step 5: Commit** — `git add` the three files; `git commit -m "feat(attendance): expected-workday calendar from legal entity week, holidays, hire/termination"`.

---

### Task 2: Holiday dates read + reader loads `Workdays`

**Files:**
- Modify: `src/ONEVO.Application/Features/Calendar/RepositoryInterfaces/ICalendarEventRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/Calendar/EfCalendarEventRepository.cs` (append after `RemoveHolidayEventsForYearAsync`, ~line 169)
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/IEmployeeAttendancePeriodReader.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/EmployeeAttendancePeriodReader.cs`
- Modify tests that construct `AttendancePeriodData` positionally: `GetEmployeeAttendanceOverviewQueryHandlerTests.cs`, `GetEmployeeAttendanceDisciplineQueryHandlerTests.cs`, `GetEmployeeAttendanceDisciplineCompareTests.cs`

**Interfaces — Produces:**
- `Task<IReadOnlyList<DateOnly>> ListHolidayDatesAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);`
- `AttendancePeriodData` gains a final positional member `ExpectedWorkdays Workdays`.

- [ ] **Step 1: Repository method**

Interface:
```csharp
    /// <summary>Distinct dates of synced holiday events (SourceType == holiday) whose StartDate falls in [from, to].</summary>
    Task<IReadOnlyList<DateOnly>> ListHolidayDatesAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);
```
EF:
```csharp
    public async Task<IReadOnlyList<DateOnly>> ListHolidayDatesAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var starts = await _db.PersonalCalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.SourceType == CalendarEventSourceTypes.Holiday
                        && e.StartDate >= start && e.StartDate < end)
            .Select(e => e.StartDate)
            .ToListAsync(ct);
        return starts.Select(s => DateOnly.FromDateTime(s.UtcDateTime)).Distinct().ToList();
    }
```
Also implement it in any test fake of `ICalendarEventRepository` the build reports (grep `: ICalendarEventRepository` under `tests/`), returning an empty list.

- [ ] **Step 2: Record + reader**

Append `ExpectedWorkdays Workdays` as the last parameter of `AttendancePeriodData`. In `EmployeeAttendancePeriodReader` add ctor deps `IEmployeeRepository employees, ICalendarEventRepository calendarEvents` (namespaces `ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces`, `ONEVO.Application.Features.Calendar.RepositoryInterfaces`) and, before the `return`:

```csharp
        var employee = await employees.GetByIdAsync(tenantId, employeeId, ct);
        var holidays = (await calendarEvents.ListHolidayDatesAsync(tenantId, period.From, period.To, ct)).ToHashSet();
        // No legal entity, or General settings without working hours/timezone: clock-in treats every
        // day as non-working, so nothing is "expected" - otherwise every such employee floods with absences.
        IReadOnlySet<int> weekdays = legalEntity is not null && AttendanceScheduleResolver.IsScheduleConfigured(legalEntity)
            ? AttendanceScheduleResolver.ParseWorkingDays(legalEntity.StandardWorkingDays)
            : new HashSet<int>();
        var workdays = ExpectedWorkdayCalendar.Build(
            weekdays, holidays, period.From, period.To,
            employee?.HireDate ?? period.From, employee?.TerminationDate, clock.Today);
```
and pass `workdays` as the new last argument. (DI already resolves both repositories; no registration change.)

- [ ] **Step 3: Fix the three test helpers** — each builds `new AttendancePeriodData(...)`; add a final argument. Use a helper in each file:
```csharp
private static ExpectedWorkdays Weekdays(DateOnly from, DateOnly to, DateOnly today) =>
    ExpectedWorkdayCalendar.Build(new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<DateOnly>(), from, to, new DateOnly(2020, 1, 1), null, today);
```
passing the period the test arranges (`new DateOnly(2026, 8, 1)`, `new DateOnly(2026, 8, 31)`, `Today`).

- [ ] **Step 4: Build + run** — `taskkill /F /IM ONEVO.Api.exe; dotnet build src/ONEVO.Api/ONEVO.Api.csproj && dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TimeAttendance"` → all green (behaviour unchanged so far).
- [ ] **Step 5: Commit** — `feat(attendance): period reader loads expected workdays (employee dates + tenant holidays)`.

---

### Task 3: `AttendancePeriodCalculator.Classify` + Overview attendance response

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Services/AttendancePeriodCalculator.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/EmployeeAttendanceOverviewResponses.cs`
- Modify: `.../Queries/EmployeeOverview/GetEmployeeAttendanceOverview/GetEmployeeAttendanceOverviewQueryHandler.cs`
- Modify: `.../Queries/EmployeeOverview/GetEmployeeAttendanceDiscipline/GetEmployeeAttendanceDisciplineQueryHandler.cs` (line 66: `Count(...)` → `Classify(data)`, map `Late`, `EarlyDepartures`, `MissingClockOuts`)
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendancePeriodClassifyTests.cs`, update `GetEmployeeAttendanceOverviewQueryHandlerTests.cs`

**Interfaces — Produces (Part 2 relies on these exact names):**
```csharp
public sealed record AttendancePeriodClassification(
    int WorkingDays, int Attended, int Late, int EarlyDepartures, int MissingClockOuts,
    int Absent, int Leave, int ShortHours, int WorkedOnNonWorkingDay, int WorkedDuringTimeOff,
    IReadOnlyList<EmployeeAttendanceDay> Days);
public static AttendancePeriodClassification Classify(AttendancePeriodData data);

public sealed record EmployeeAttendanceOverviewResponse(
    DateOnly From, DateOnly To, int WorkingDays, int Present, int Late, int MissingClockOuts, int LeaveDays,
    IReadOnlyList<EmployeeAttendanceDay> Days,
    int Absent, int ShortHours, int WorkedOnNonWorkingDay, int WorkedDuringTimeOff);
```
`Present` = attended days (unchanged meaning: any clock-in).

- [ ] **Step 1: Failing tests** (`AttendancePeriodClassifyTests`) — build `AttendancePeriodData` for 2026-09-01..30, today 2026-09-30, Mon–Fri, UTC timezone, `Now = 2026-09-30T12:00Z`:
  - `NoRecords_EveryPastExpectedDayIsAbsent`: records empty → `WorkingDays == 22`, `Absent == 21` (today not yet absent), `Days.Count == 30`, `Days.Single(d => d.Date == new DateOnly(2026,9,30)).Status == "none"`.
  - `ApprovedLeave_IsLeaveNotAbsent`: one leave covering 2026-09-08..09 → `Leave == 2`, `Absent == 19`.
  - `ShortHours_CountedWhenWorkedBelowRequired`: record 2026-09-01 on time, `ActualEnd` set, `RequiredWorkMinutes = 480`, `WorkedMinutes = 300` → `ShortHours == 1`, `Attended == 1`.
  - `WeekendWork_CountsAsNonWorkingDayWork`: record 2026-09-05 (Sat) with `ActualStart` → `WorkedOnNonWorkingDay == 1`, `WorkingDays` still 22.
  - `WorkDuringLeave_Counted`: leave covering 2026-09-02 and a clock-in that day → `WorkedDuringTimeOff == 1`, `Leave == 0` for that day.
  - `PreHireDays_AreNone`: workdays built with hire 2026-09-21 → `Days.Where(d => d.Date < new DateOnly(2026,9,21)).All(d => d.Status == "none")`, `Absent == 7`.

  Record builder (mirrors existing tests): `new AttendanceRecord { Id = Guid.NewGuid(), Date = d, ExpectedWorkingDay = true, ScheduledStart = new(9,0), ScheduledEnd = new(17,0), ActualStart = ..., ActualEnd = ..., RequiredWorkMinutes = ..., WorkedMinutes = ... }`.

- [ ] **Step 2: Run** → compile failure on `Classify`.

- [ ] **Step 3: Implement** in `AttendancePeriodCalculator` (keep `Count`, `DayStatus` for callers not migrated):

```csharp
    /// <summary>Classifies every date of the period against the expected-workday calendar. The
    /// single source for WorkingDays/Absent - never derive those from which records exist.</summary>
    public static AttendancePeriodClassification Classify(AttendancePeriodData data)
    {
        var w = data.Workdays;
        var byDate = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        int attended = 0, late = 0, early = 0, missing = 0, absent = 0, leave = 0, shortHours = 0, offDayWork = 0, leaveWork = 0;
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
                var status = IsMissingClockOut(record, data.Now) ? "missing_clock_out"
                    : IsLate(record, data.Timezone) ? "late" : "present";
                if (status == "missing_clock_out") missing++;
                if (status == "late") late++;
                if (IsEarlyDeparture(record, data.Timezone)) early++;
                if (!expected) offDayWork++;
                else if (onLeave) leaveWork++;
                if (expected && record.ActualEnd is not null && record.RequiredWorkMinutes is int req && record.WorkedMinutes < req)
                    shortHours++;
                days.Add(new(d, status));
            }
            else if (!expected) days.Add(new(d, "off"));
            else if (onLeave) { leave++; days.Add(new(d, "leave")); }
            else if (d < data.Today) { absent++; days.Add(new(d, "absent")); }
            else days.Add(new(d, "none"));
        }

        return new AttendancePeriodClassification(
            w.Count, attended, late, early, missing, absent, leave, shortHours, offDayWork, leaveWork, days);
    }
```
Add `using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;`.

Overview handler body after `LoadAsync` becomes:
```csharp
        var c = AttendancePeriodCalculator.Classify(data);
        return Result<EmployeeAttendanceOverviewResponse>.Success(new EmployeeAttendanceOverviewResponse(
            period.Value!.From, period.Value.To, c.WorkingDays, c.Attended, c.Late, c.MissingClockOuts, c.Leave,
            c.Days, c.Absent, c.ShortHours, c.WorkedOnNonWorkingDay, c.WorkedDuringTimeOff));
```
Update the doc comment on `EmployeeAttendanceDay` to include `none` meaning "outside employment / future / today not yet clocked in".

- [ ] **Step 4: Update `GetEmployeeAttendanceOverviewQueryHandlerTests`** — assertions that expected `WorkingDays` equal to record count now expect the calendar count from the `Weekdays(...)` helper (Aug 2026 up to Today 2026-08-21 = 15 weekdays). Adjust each failing assertion to the calendar-derived value; do not delete tests.
- [ ] **Step 5: Run** — kill API, `dotnet test ... --filter "FullyQualifiedName~TimeAttendance"` → green.
- [ ] **Step 6: Commit** — `fix(attendance): overview working days/absences from the expected-workday calendar`.

---

### Task 4: Self-service monthly summary + termination date on employee detail

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs` (~lines 80–97, monthly summary)
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeDetailResponse.cs:13-17`
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeDetail/GetEmployeeDetailQueryHandler.cs:106-110`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs`

- [ ] **Step 1:** Add optional ctor dependency `IEmployeeAttendancePeriodReader? periodReader = null` to `AttendanceReadHandler` (same optional pattern as its existing `legalEntities` / `dateTimeProvider`). In the monthly summary, when `periodReader` is not null:
```csharp
        var data = await periodReader.LoadAsync(currentUser.TenantId, employee.Id, employee.LegalEntityId,
            new EmployeePeriod(query.From, query.To), ct);
        var c = AttendancePeriodCalculator.Classify(data);
        return Result<AttendanceMonthlySummaryResponse>.Success(new AttendanceMonthlySummaryResponse(
            c.WorkingDays, c.Attended, c.Late, c.EarlyDepartures, c.MissingClockOuts));
```
otherwise keep the existing `Count` path. Check `EmployeePeriod`'s constructor in `Features/CoreHr/Employee/Helpers/EmployeePeriod.cs`; if it has no public ctor use `EmployeePeriod.Resolve(query.From, query.To, today).Value!`.
- [ ] **Step 2:** Add a test to `AttendanceReadHandlerTests`: with a mocked reader returning a 22-weekday September and no records, the monthly summary returns `WorkingDays == 22`, `DaysPresent == 0`.
- [ ] **Step 3:** Append `DateOnly? TerminationDate` as the last member of `EmployeeDetailJobInformation`, pass `existing.TerminationDate` in the handler; fix every other `new EmployeeDetailJobInformation(` the build flags.
- [ ] **Step 4:** Kill API; `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`; full unit + architecture suites: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` and `dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj` → paste the raw pass/fail totals into the task report.
- [ ] **Step 5: Commit** — `fix(attendance): monthly summary uses expected workdays; expose termination date on employee detail`.
