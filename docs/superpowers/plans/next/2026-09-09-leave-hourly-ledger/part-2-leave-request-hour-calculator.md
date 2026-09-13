# Leave hourly ledger — Part 2: LeaveRequestHourCalculator

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace working-date + AM/PM day counting with overlap of `[StartAt, EndAt]` against each legal-entity shift, returning decimal hours.

**Architecture:** New pure helper next to `LeaveRequestDayCalculator`. Part 3 swaps the evaluator over and deletes the day calculator. This part only adds the hour calculator and its unit tests. Depends on Part 1 `WorkDayHoursCalculator`.

**Tech Stack:** .NET, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-09-09-leave-hourly-ledger-design.md` § Hour calculator.

**Global constraints:** Round to 2 decimal hours. Full overlap charges `workDayHours` (break already removed). Partial overlap does not subtract break. Overnight hours belong to the shift-start date. Unset window is not handled here — caller must pass a real start/end.

---

### Task 1: Failing tests for the spec examples

**Files:**
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestHourCalculatorTests.cs`
- Create: `src/ONEVO.Application/Features/Leave/Request/Helpers/LeaveRequestHourCalculator.cs`

- [ ] **Step 1: Write tests**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Request.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestHourCalculatorTests
{
    private static readonly TimeOnly Start = new(9, 0);
    private static readonly TimeOnly End = new(18, 0);
    private static readonly int[] Weekdays = [1, 2, 3, 4, 5];

    private static LeaveRequestHourCalculationResult Calc(
        DateTimeOffset from, DateTimeOffset to,
        TimeOnly? workStart = null, TimeOnly? workEnd = null, int breakMinutes = 60,
        IReadOnlyCollection<int>? days = null, IReadOnlyCollection<DateOnly>? holidays = null)
        => new LeaveRequestHourCalculator().Calculate(new LeaveRequestHourCalculationInput(
            from, to,
            workStart ?? Start, workEnd ?? End, breakMinutes,
            days ?? Weekdays, holidays ?? []));

    [Fact]
    public void FullDay_ChargesNetWorkDayHours()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(8.00m);
        r.CountedShiftStartDates.Should().Equal(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void AfternoonPartial_DoesNotSubtractBreak()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(4.00m);
    }

    [Fact]
    public void OvernightNightShift_FullWindow()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 6, 0, 0, TimeSpan.Zero),
            new TimeOnly(22, 0), new TimeOnly(6, 0), 0);
        r.TotalHours.Should().Be(8.00m);
        r.CountedShiftStartDates.Should().Equal(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void MultiDay_ThreeFullShifts()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(24.00m);
    }

    [Fact]
    public void PartialEdges_FourPlusEightPlusFour()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 11, 13, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(16.00m);
    }

    [Fact]
    public void WeekendSkip_FriToMon()
    {
        var r = Calc(
            new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero));
        r.TotalHours.Should().Be(16.00m);
    }

    [Fact]
    public void EndAtNotAfterStartAt_Zero()
    {
        var at = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        Calc(at, at).TotalHours.Should().Be(0m);
        Calc(at, at.AddMinutes(-1)).TotalHours.Should().Be(0m);
    }
}
```

Dates: 9 Sep 2026 = Wednesday, 11 Sep = Friday, 14 Sep = Monday. Confirm with a calendar before running; if the weekday numbers fail, fix the test dates, not the calculator.

ISO weekday: Monday=1 … Sunday=7, matching `LeaveRequestDayCalculator`.

- [ ] **Step 2: Run — expect FAIL**

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveRequestHourCalculatorTests
```

- [ ] **Step 3: Implement**

Create `src/ONEVO.Application/Features/Leave/Request/Helpers/LeaveRequestHourCalculator.cs`:

```csharp
using ONEVO.Application.Common.Helpers;

namespace ONEVO.Application.Features.Leave.Request.Helpers;

public sealed record LeaveRequestHourCalculationInput(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    TimeOnly WorkStart,
    TimeOnly WorkEnd,
    int BreakMinutes,
    IReadOnlyCollection<int> StandardWorkingDays,
    IReadOnlyCollection<DateOnly> HolidayDates);

public sealed record LeaveRequestHourCalculationResult(
    decimal TotalHours,
    IReadOnlyList<DateOnly> CountedShiftStartDates);

public sealed class LeaveRequestHourCalculator
{
    public LeaveRequestHourCalculationResult Calculate(LeaveRequestHourCalculationInput input)
    {
        if (input.EndAt <= input.StartAt)
            return new(0m, []);

        var workDayHours = WorkDayHoursCalculator.Compute(input.WorkStart, input.WorkEnd, input.BreakMinutes);
        var working = input.StandardWorkingDays.ToHashSet();
        var holidays = input.HolidayDates.ToHashSet();
        var counted = new List<DateOnly>();
        decimal total = 0m;

        var fromDate = DateOnly.FromDateTime(input.StartAt.UtcDateTime);
        var toDate = DateOnly.FromDateTime(input.EndAt.UtcDateTime);
        if (input.WorkEnd <= input.WorkStart)
            fromDate = fromDate.AddDays(-1);

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            if (!working.Contains(iso) || holidays.Contains(date))
                continue;

            var (shiftStart, shiftEnd) = WorkDayHoursCalculator.ShiftInterval(date, input.WorkStart, input.WorkEnd);
            var shiftStartDto = new DateTimeOffset(DateTime.SpecifyKind(shiftStart, DateTimeKind.Utc));
            var shiftEndDto = new DateTimeOffset(DateTime.SpecifyKind(shiftEnd, DateTimeKind.Utc));

            var overlapStart = input.StartAt > shiftStartDto ? input.StartAt : shiftStartDto;
            var overlapEnd = input.EndAt < shiftEndDto ? input.EndAt : shiftEndDto;
            if (overlapEnd <= overlapStart)
                continue;

            var overlapHours = (decimal)(overlapEnd - overlapStart).TotalHours;
            var fullHours = (decimal)(shiftEndDto - shiftStartDto).TotalHours;
            total += overlapHours >= fullHours - 0.01m
                ? workDayHours
                : decimal.Round(overlapHours, 2, MidpointRounding.AwayFromZero);
            counted.Add(date);
        }

        return new(decimal.Round(total, 2, MidpointRounding.AwayFromZero), counted);
    }
}
```

Timezone: Part 3 must convert StartAt/EndAt into the legal-entity timezone **before** calling this helper, then pass DateTimeOffsets whose clock values match that zone (offset may be +05:30). Tests above use `TimeSpan.Zero` as a stand-in. Do not switch the helper to `DateTime.Now` or server local time.

- [ ] **Step 4: Run — expect PASS**

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~LeaveRequestHourCalculatorTests
```

If weekend-skip fails, print `counted` dates and fix ISO mapping, not the spec numbers.

- [ ] **Step 5: Commit**

```
git add src/ONEVO.Application/Features/Leave/Request/Helpers/LeaveRequestHourCalculator.cs tests/ONEVO.Tests.Unit/Features/Leave/Request/LeaveRequestHourCalculatorTests.cs
git commit -m "feat(leave): add LeaveRequestHourCalculator for shift-overlap hours"
```
