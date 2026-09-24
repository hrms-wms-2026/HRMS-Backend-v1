# Leave hourly ledger — Part 1: Work-day hours + overnight window

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unlock overnight legal-entity work windows and ship a shared `WorkDayHoursCalculator` that leave generation/requests will use as “1 day = these hours”.

**Architecture:** Pure helper in Application (no EF). General Settings validator drops the same-day `start < end` rule, keeps both-or-neither, and rejects `workDayHours <= 0`. No schema change — `TimeOnly` start/end already stored.

**Tech Stack:** .NET, FluentValidation, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-09-09-leave-hourly-ledger-design.md`. Companion frontend: `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-09-leave-hourly-ledger/part-1-overnight-work-hours-ui.md`.

**Global constraints:** Do not touch leave request/entitlement columns in this part. Do not default unset windows to 8h. Equal start and end is a 24h window (spec: `workEnd <= workStart` means next calendar day).

---

### Task 1: WorkDayHoursCalculator

**Files:**
- Create: `src/ONEVO.Application/Common/Helpers/WorkDayHoursCalculator.cs`
- Create: `tests/ONEVO.Tests.Unit/Common/WorkDayHoursCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ONEVO.Tests.Unit/Common/WorkDayHoursCalculatorTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common;

public class WorkDayHoursCalculatorTests
{
    [Fact]
    public void TryCompute_BothNull_ReturnsNull()
    {
        WorkDayHoursCalculator.TryCompute(null, null, 60).Should().BeNull();
    }

    [Fact]
    public void Compute_DayShiftMinusBreak()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(18, 0), 60)
            .Should().Be(8.00m);
    }

    [Fact]
    public void Compute_OvernightNightShift()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(22, 0), new TimeOnly(6, 0), 0)
            .Should().Be(8.00m);
    }

    [Fact]
    public void Compute_EqualStartAndEnd_IsTwentyFourHours()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(9, 0), 0)
            .Should().Be(24.00m);
    }

    [Fact]
    public void Compute_BreakLongerThanShift_ReturnsZeroOrNegativeDetected()
    {
        WorkDayHoursCalculator.Compute(new TimeOnly(9, 0), new TimeOnly(10, 0), 120)
            .Should().Be(-1.00m);
    }

    [Fact]
    public void ShiftInterval_Overnight_EndsNextDay()
    {
        var date = new DateOnly(2026, 9, 9);
        var (start, end) = WorkDayHoursCalculator.ShiftInterval(date, new TimeOnly(22, 0), new TimeOnly(6, 0));
        start.Should().Be(date.ToDateTime(new TimeOnly(22, 0)));
        end.Should().Be(date.AddDays(1).ToDateTime(new TimeOnly(6, 0)));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (type not found)

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkDayHoursCalculatorTests
```

Expected: FAIL, `WorkDayHoursCalculator` does not exist.

- [ ] **Step 3: Implement**

Create `src/ONEVO.Application/Common/Helpers/WorkDayHoursCalculator.cs`:

```csharp
namespace ONEVO.Application.Common.Helpers;

public static class WorkDayHoursCalculator
{
    public static decimal? TryCompute(TimeOnly? start, TimeOnly? end, int? breakMinutes)
    {
        if (start is null || end is null)
            return null;
        return Compute(start.Value, end.Value, breakMinutes ?? 0);
    }

    public static decimal Compute(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var shiftMinutes = ShiftLength(start, end).TotalMinutes;
        var net = (decimal)shiftMinutes - breakMinutes;
        return decimal.Round(net / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public static TimeSpan ShiftLength(TimeOnly start, TimeOnly end)
    {
        var startMins = start.Hour * 60 + start.Minute;
        var endMins = end.Hour * 60 + end.Minute;
        var delta = endMins - startMins;
        if (delta <= 0)
            delta += 24 * 60;
        return TimeSpan.FromMinutes(delta);
    }

    public static (DateTime Start, DateTime End) ShiftInterval(DateOnly startDate, TimeOnly workStart, TimeOnly workEnd)
    {
        var start = startDate.ToDateTime(workStart);
        var end = startDate.ToDateTime(workEnd);
        if (workEnd <= workStart)
            end = startDate.AddDays(1).ToDateTime(workEnd);
        return (start, end);
    }
}
```

Note: 09:00–10:00 minus 120 min break = −1.00h. Validator in Task 2 rejects `<= 0`. Helper itself returns the signed net so the validator can use it.

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkDayHoursCalculatorTests
```

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```
git add src/ONEVO.Application/Common/Helpers/WorkDayHoursCalculator.cs tests/ONEVO.Tests.Unit/Common/WorkDayHoursCalculatorTests.cs
git commit -m "feat(org): add WorkDayHoursCalculator with overnight windows"
```

---

### Task 2: Allow overnight on General Settings validator

**Files:**
- Modify: `src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandValidator.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandValidatorTests.cs`

- [ ] **Step 1: Rewrite the two tests that currently require start < end**

In `UpdateLegalEntityGeneralSettingsCommandValidatorTests.cs` replace `WorkStartTime_EqualToEndTime_HasError` and `WorkStartTime_AfterEndTime_HasError` with:

```csharp
[Fact]
public void WorkStartTime_AfterEndTime_Overnight_HasNoError()
{
    var result = _validator.TestValidate(ValidCommand() with
    {
        WorkStartTime = new TimeOnly(22, 0),
        WorkEndTime = new TimeOnly(6, 0),
        BreakDurationMinutes = 0
    });
    result.ShouldNotHaveValidationErrorFor(x => x.WorkStartTime);
    result.ShouldNotHaveValidationErrorFor(x => x.WorkEndTime);
}

[Fact]
public void WorkWindow_BreakCoversWholeShift_HasError()
{
    var result = _validator.TestValidate(ValidCommand() with
    {
        WorkStartTime = new TimeOnly(9, 0),
        WorkEndTime = new TimeOnly(10, 0),
        BreakDurationMinutes = 120
    });
    result.ShouldHaveValidationErrorFor(x => x.BreakDurationMinutes);
}
```

Keep `BothWorkTimesNull_HasNoError`, `ValidWorkStartAndEndTime_HasNoError`, and the one-sided pair tests.

- [ ] **Step 2: Run — overnight test FAIL** (still “must be before”)

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~UpdateLegalEntityGeneralSettingsCommandValidatorTests
```

- [ ] **Step 3: Change the validator**

In `UpdateLegalEntityGeneralSettingsCommandValidator.cs` delete the comment “Overnight shifts are not supported” and the rule:

```csharp
RuleFor(x => x.WorkStartTime)
    .Must((command, start) => start < command.WorkEndTime)
    .WithMessage("Work start time must be before work end time.")
    .When(x => x.WorkStartTime is not null && x.WorkEndTime is not null);
```

Add, after the both-required pair rules:

```csharp
RuleFor(x => x.BreakDurationMinutes)
    .Must((command, brk) =>
    {
        var hours = WorkDayHoursCalculator.TryCompute(command.WorkStartTime, command.WorkEndTime, brk);
        return hours is null || hours > 0m;
    })
    .WithMessage("Break duration must be shorter than the work window.")
    .When(x => x.WorkStartTime is not null && x.WorkEndTime is not null);
```

Add `using ONEVO.Application.Common.Helpers;`. Keep the existing `BreakDurationMinutes >= 0` rule.

- [ ] **Step 4: Run validator tests — expect PASS**

```
dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~UpdateLegalEntityGeneralSettingsCommandValidatorTests
```

- [ ] **Step 5: Commit**

```
git add src/ONEVO.Application/Features/OrgStructure/LegalEntity/Commands/UpdateLegalEntityGeneralSettings/UpdateLegalEntityGeneralSettingsCommandValidator.cs tests/ONEVO.Tests.Unit/Features/OrgStructure/LegalEntity/UpdateLegalEntityGeneralSettingsCommandValidatorTests.cs
git commit -m "feat(org): allow overnight work windows; reject break covering the shift"
```

Part 1 is done when both commits are in and those two test filters are green.
