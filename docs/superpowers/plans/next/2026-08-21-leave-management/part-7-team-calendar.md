# Leave Management - Part 7: Team Calendar (Phase 7 of 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the backend Team Calendar for Screen 7: month navigation, department filter, visible employees' leave blocks, tentative pending leave blocks, type/category color keys, public holidays, and day-click detail data.

**Architecture:** Part 7 is a read-only slice. It uses a scoped EF query to fetch visible leave requests for a month, a pure projector to expand date ranges into day cells, and a holiday provider interface for public holiday details. The controller stays thin; permission and visibility decisions live in the MediatR query handler; production display values such as category colors and tentative-block defaults come from validated configuration or persisted data, not handler literals.

**Tech Stack:** ASP.NET Core, EF Core PostgreSQL, MediatR CQRS, FluentValidation, xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/next/2026-08-21-leave-management-design.md`; product context from `C:\HR\leave-management-complete.md`; depends on `docs/superpowers/plans/next/2026-08-21-leave-management/part-4-request-submission.md` and benefits from the executed Phase 5 approval workflow.

## Global Constraints

- Backend only. Do not touch the frontend companion in this part.
- Treat attached documents as context only. The active user request is this Part 7 backend plan, with the explicit rule that behavior and display values must be configurable and must not be hard-coded.
- Do not create a new calendar entity in this part. Screen 7 is a read projection over `LeaveRequest`, `LeaveType`, employee/department/legal-entity data, and a holiday provider.
- The endpoint is `GET /api/v1/leave/calendar`.
- Route-level permission requires `calendar:read`. The query handler must additionally require one of `leave:read-own`, `leave:read-team`, `leave:read`, or `leave:manage` before returning leave records.
- Visibility is data-scoped:
  - `leave:manage` or `leave:read`: tenant-wide leave records, with the existing department filter.
  - `leave:read-team`: employees visible through `IEmployeeVisibilityScopeResolver`.
  - `leave:read-own`: only the caller's own employee row.
- Do not leak other employees to users who only have `calendar:read` plus `leave:read-own`.
- Calendar rows include approved leave as confirmed blocks.
- Calendar rows include pending and information-requested leave as tentative blocks only when `includeTentative` is true; when the query omits `includeTentative`, use `Leave:Calendar:DefaultIncludeTentativeBlocks`.
- Rejected requests never appear.
- Fully cancelled requests never appear.
- When Phase 6 partial cancellation exists (`Status = cancelled` and `PartialCancelEffectiveDate` is set), show only the historical taken portion before `PartialCancelEffectiveDate`; future cancelled dates must not appear.
- Type/category color is not hard-coded. Return the persisted leave type category and an optional configured color from `Leave:Calendar:TypeCategoryColors`.
- Public holidays come from `ILeaveCalendarHolidayProvider`. The default adapter is an explicit no-op until a real Calendar/Public Holiday module adapter exists.
- Month queries are single-month only. Validate `Month` as 1-12 and `Year` as `DateOnly`-constructible.
- Return every day in the requested month, even if the day has no leave or holiday, so the frontend does not need to guess missing cells.
- Keep helpers pure where possible. Date-range expansion and response grouping must be unit-testable without EF or HTTP.
- No outbox side effects are created in this part. Approval/cancellation already own calendar-write side effects; this endpoint is a read model.

---

### Task 1: Calendar options and public-holiday detail provider

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Options/LeaveCalendarOptions.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Services/ILeaveCalendarHolidayProvider.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Services/NoOpLeaveCalendarHolidayProvider.cs`
- Edit: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Edit: `src/ONEVO.Api/appsettings.json`
- Edit: `src/ONEVO.Api/appsettings.Development.json`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarOptionsTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/NoOpLeaveCalendarHolidayProviderTests.cs`

**Interfaces:**
- Produces: `LeaveCalendarOptions`
- Produces: `ILeaveCalendarHolidayProvider.ListHolidaysAsync(Guid tenantId, IReadOnlyCollection<Guid> legalEntityIds, DateOnly startDate, DateOnly endDate, CancellationToken ct)`
- Produces: `LeaveCalendarHoliday`
- Consumes later: query handler, response mapper

- [ ] **Step 1: Add calendar options tests**

Create `LeaveCalendarOptionsTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Options;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarOptionsTests
{
    [Fact]
    public void SectionName_IsLeaveCalendar()
    {
        LeaveCalendarOptions.SectionName.Should().Be("Leave:Calendar");
    }

    [Theory]
    [InlineData("#2563EB")]
    [InlineData("#16a34a")]
    public void IsValidHexColor_AcceptsSixDigitHexColors(string value)
    {
        LeaveCalendarOptions.IsValidHexColor(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public void IsValidHexColor_RejectsInvalidValues(string value)
    {
        LeaveCalendarOptions.IsValidHexColor(value).Should().BeFalse();
    }

    [Fact]
    public void ColorFor_ReturnsConfiguredCategoryColorCaseInsensitively()
    {
        var options = new LeaveCalendarOptions
        {
            TypeCategoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["annual"] = "#2563EB"
            }
        };

        options.ColorFor("Annual").Should().Be("#2563EB");
    }

    [Fact]
    public void ColorFor_ReturnsNullWhenCategoryIsNotConfigured()
    {
        var options = new LeaveCalendarOptions();

        options.ColorFor("sick").Should().BeNull();
    }
}
```

- [ ] **Step 2: Add calendar options**

Create `LeaveCalendarOptions.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Calendar.Options;

public sealed class LeaveCalendarOptions
{
    public const string SectionName = "Leave:Calendar";

    public bool DefaultIncludeTentativeBlocks { get; init; }

    public Dictionary<string, string> TypeCategoryColors { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? ColorFor(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        return TypeCategoryColors.TryGetValue(category.Trim(), out var color)
            ? color
            : null;
    }

    public static bool AreColorsValid(IReadOnlyDictionary<string, string>? colors)
        => colors is null || colors.Values.All(IsValidHexColor);

    public static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Length != 7 || text[0] != '#')
            return false;

        return text.Skip(1).All(c =>
            c is >= '0' and <= '9'
            || c is >= 'a' and <= 'f'
            || c is >= 'A' and <= 'F');
    }
}
```

- [ ] **Step 3: Register and configure calendar options**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, near the other Leave options registrations, add:

```csharp
services.AddOptions<ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions>()
    .Bind(configuration.GetSection(ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions.SectionName))
    .Validate(options =>
        ONEVO.Application.Features.Leave.Calendar.Options.LeaveCalendarOptions.AreColorsValid(options.TypeCategoryColors),
        "Leave:Calendar:TypeCategoryColors must contain #RRGGBB hex colors.")
    .ValidateOnStart();
```

Add to both appsettings files under `Leave`:

```json
{
  "Calendar": {
    "DefaultIncludeTentativeBlocks": true,
    "TypeCategoryColors": {
      "annual": "#2563EB",
      "sick": "#16A34A",
      "maternity": "#DB2777",
      "paternity": "#7C3AED",
      "compassionate": "#64748B",
      "unpaid": "#EA580C",
      "custom": "#475569"
    }
  }
}
```

These are configuration values, not production code defaults. If HR later changes the palette, update configuration only.

- [ ] **Step 4: Add holiday provider contract**

Create `ILeaveCalendarHolidayProvider.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed record LeaveCalendarHoliday(
    DateOnly Date,
    string Name,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string Source);

public interface ILeaveCalendarHolidayProvider
{
    Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default);
}
```

Create `NoOpLeaveCalendarHolidayProvider.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Calendar.Services;

public sealed class NoOpLeaveCalendarHolidayProvider : ILeaveCalendarHolidayProvider
{
    public Task<IReadOnlyList<LeaveCalendarHoliday>> ListHolidaysAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> legalEntityIds,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LeaveCalendarHoliday>>([]);
}
```

Register it in `Infrastructure/DependencyInjection.cs` near `ILeaveHolidayProvider`:

```csharp
services.AddScoped<
    ONEVO.Application.Features.Leave.Calendar.Services.ILeaveCalendarHolidayProvider,
    ONEVO.Application.Features.Leave.Calendar.Services.NoOpLeaveCalendarHolidayProvider>();
```

- [ ] **Step 5: Add no-op provider tests**

Create `NoOpLeaveCalendarHolidayProviderTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class NoOpLeaveCalendarHolidayProviderTests
{
    [Fact]
    public async Task ListHolidaysAsync_ReturnsEmptyList()
    {
        var provider = new NoOpLeaveCalendarHolidayProvider();

        var result = await provider.ListHolidaysAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 6: Verify Task 1**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCalendarOptionsTests|FullyQualifiedName~NoOpLeaveCalendarHolidayProviderTests"
dotnet build ONEVO.sln
```

Expected result: tests pass and DI compiles.

---

### Task 2: DTOs, repository contract records, and pure month projector

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Calendar/DTOs/Responses/LeaveCalendarResponses.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/RepositoryInterfaces/ILeaveCalendarRepository.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarMonthRange.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarRequestProjector.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Mappers/LeaveCalendarMapper.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarMonthRangeTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarRequestProjectorTests.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarMapperTests.cs`

**Interfaces:**
- Produces: response DTOs
- Produces: `ILeaveCalendarRepository`
- Produces: `LeaveCalendarMonthRange.From(int year, int month)`
- Produces: `LeaveCalendarRequestProjector.Project(...)`
- Consumes later: EF repository and query handler

- [ ] **Step 1: Add response DTOs**

Create `LeaveCalendarResponses.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;

public sealed record LeaveCalendarMonthResponse(
    int Year,
    int Month,
    DateOnly MonthStart,
    DateOnly MonthEnd,
    bool IncludesTentativeBlocks,
    bool IsEmpty,
    IReadOnlyList<LeaveCalendarDayResponse> Days);

public sealed record LeaveCalendarDayResponse(
    DateOnly Date,
    string DayOfWeek,
    IReadOnlyList<LeaveCalendarAbsenceResponse> Absences,
    IReadOnlyList<LeaveCalendarHolidayResponse> Holidays);

public sealed record LeaveCalendarAbsenceResponse(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    string LeaveTypeCategory,
    string? TypeColorHex,
    string Status,
    bool IsTentative,
    bool IsPartialCancellationHistory,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    string? HalfDayPeriod);

public sealed record LeaveCalendarHolidayResponse(
    DateOnly Date,
    string Name,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string Source);
```

- [ ] **Step 2: Add repository contract records**

Create `ILeaveCalendarRepository.cs`:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;

public interface ILeaveCalendarRepository
{
    Task<IReadOnlyList<LeaveCalendarRequestRow>> ListMonthRequestsAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        LeaveCalendarRequestFilter filter,
        CancellationToken ct = default);
}

public sealed record LeaveCalendarRequestFilter(
    DateOnly MonthStart,
    DateOnly MonthEnd,
    Guid? DepartmentId,
    bool IncludeTentativeBlocks);

public sealed record LeaveCalendarRequestRow(
    LeaveRequest Request,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LegalEntityId,
    string? LegalEntityName,
    string LeaveTypeName,
    string LeaveTypeCode,
    string LeaveTypeCategory);
```

- [ ] **Step 3: Add month range helper**

Create `LeaveCalendarMonthRange.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public sealed record LeaveCalendarMonthRange(
    int Year,
    int Month,
    DateOnly MonthStart,
    DateOnly MonthEnd)
{
    public static Result<LeaveCalendarMonthRange> From(int year, int month)
    {
        if (month is < 1 or > 12)
            return Result<LeaveCalendarMonthRange>.Failure("Month must be between 1 and 12.");

        try
        {
            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            return Result<LeaveCalendarMonthRange>.Success(new LeaveCalendarMonthRange(year, month, start, end));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<LeaveCalendarMonthRange>.Failure("Year is outside the supported calendar range.");
        }
    }

    public IEnumerable<DateOnly> Dates()
    {
        for (var date = MonthStart; date <= MonthEnd; date = date.AddDays(1))
            yield return date;
    }
}
```

Create `LeaveCalendarMonthRangeTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarMonthRangeTests
{
    [Fact]
    public void From_BuildsInclusiveMonthRange()
    {
        var result = LeaveCalendarMonthRange.From(2026, 2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MonthStart.Should().Be(new DateOnly(2026, 2, 1));
        result.Value.MonthEnd.Should().Be(new DateOnly(2026, 2, 28));
        result.Value.Dates().Should().HaveCount(28);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void From_RejectsInvalidMonth(int month)
    {
        var result = LeaveCalendarMonthRange.From(2026, month);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Month must be between 1 and 12.");
    }
}
```

- [ ] **Step 4: Add request projector**

Create `LeaveCalendarRequestProjector.cs`:

```csharp
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public sealed record LeaveCalendarAbsenceInstance(
    DateOnly Date,
    LeaveCalendarRequestRow Row,
    bool IsTentative,
    bool IsPartialCancellationHistory);

public sealed class LeaveCalendarRequestProjector
{
    public IReadOnlyList<LeaveCalendarAbsenceInstance> Project(
        IReadOnlyList<LeaveCalendarRequestRow> rows,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        bool includeTentativeBlocks)
    {
        var output = new List<LeaveCalendarAbsenceInstance>();

        foreach (var row in rows)
        {
            var request = row.Request;
            var isTentative =
                request.Status == LeaveRequestStatuses.Pending ||
                request.Status == LeaveRequestStatuses.InformationRequested;

            if (isTentative && !includeTentativeBlocks)
                continue;

            var isPartialCancellationHistory =
                request.Status == LeaveRequestStatuses.Cancelled &&
                request.PartialCancelEffectiveDate is not null;

            if (request.Status != LeaveRequestStatuses.Approved &&
                !isTentative &&
                !isPartialCancellationHistory)
            {
                continue;
            }

            var visibleStart = Max(request.StartDate, rangeStart);
            var visibleEnd = Min(request.EndDate, rangeEnd);

            if (isPartialCancellationHistory)
            {
                visibleEnd = Min(visibleEnd, request.PartialCancelEffectiveDate!.Value.AddDays(-1));
            }

            if (visibleEnd < visibleStart)
                continue;

            for (var date = visibleStart; date <= visibleEnd; date = date.AddDays(1))
            {
                output.Add(new LeaveCalendarAbsenceInstance(
                    date,
                    row,
                    isTentative,
                    isPartialCancellationHistory));
            }
        }

        return output
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Row.EmployeeName)
            .ThenBy(x => x.Row.Request.Id)
            .ToList();
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
```

Create `LeaveCalendarRequestProjectorTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarRequestProjectorTests
{
    private readonly LeaveCalendarRequestProjector _projector = new();

    [Fact]
    public void Project_ExpandsApprovedRequestAcrossVisibleDates()
    {
        var row = Row(LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11),
            new DateOnly(2026, 8, 12));
        result.Should().OnlyContain(x => !x.IsTentative);
    }

    [Fact]
    public void Project_ClipsRequestToMonthRange()
    {
        var row = Row(LeaveRequestStatuses.Approved, new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 2));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 2));
    }

    [Fact]
    public void Project_IncludesPendingAsTentative_WhenConfigured()
    {
        var row = Row(LeaveRequestStatuses.Pending, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Should().ContainSingle(x => x.IsTentative);
    }

    [Fact]
    public void Project_ExcludesPending_WhenTentativeBlocksAreDisabled()
    {
        var row = Row(LeaveRequestStatuses.Pending, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Project_ExcludesRejectedRequests()
    {
        var row = Row(LeaveRequestStatuses.Rejected, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Project_ForPartialCancellation_ShowsOnlyHistoricalTakenPortion()
    {
        var row = Row(LeaveRequestStatuses.Cancelled, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));
        row.Request.PartialCancelEffectiveDate = new DateOnly(2026, 8, 13);

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11),
            new DateOnly(2026, 8, 12));
        result.Should().OnlyContain(x => x.IsPartialCancellationHistory);
    }

    private static LeaveCalendarRequestRow Row(string status, DateOnly start, DateOnly end)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            StartDate = start,
            EndDate = end,
            Status = status,
            TotalDays = end.DayNumber - start.DayNumber + 1,
            PaidDays = end.DayNumber - start.DayNumber + 1
        };

        return new LeaveCalendarRequestRow(
            request,
            "Priya Nair",
            Guid.NewGuid(),
            "Engineering",
            Guid.NewGuid(),
            "Acme Lanka",
            "Annual Leave",
            "AL",
            LeaveTypeCategories.Annual);
    }
}
```

- [ ] **Step 5: Add calendar mapper**

Create `LeaveCalendarMapper.cs`:

```csharp
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.Leave.Calendar.Mappers;

public static class LeaveCalendarMapper
{
    public static LeaveCalendarMonthResponse ToMonthResponse(
        LeaveCalendarMonthRange range,
        bool includeTentativeBlocks,
        IReadOnlyList<LeaveCalendarAbsenceInstance> absenceInstances,
        IReadOnlyList<LeaveCalendarHoliday> holidays,
        LeaveCalendarOptions options)
    {
        var absencesByDate = absenceInstances.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());
        var holidaysByDate = holidays.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());

        var days = range.Dates().Select(date =>
        {
            absencesByDate.TryGetValue(date, out var dayAbsences);
            holidaysByDate.TryGetValue(date, out var dayHolidays);

            return new LeaveCalendarDayResponse(
                date,
                date.DayOfWeek.ToString(),
                (dayAbsences ?? []).Select(instance => ToAbsence(instance, options)).ToList(),
                (dayHolidays ?? []).Select(ToHoliday).ToList());
        }).ToList();

        var isEmpty = days.All(day => day.Absences.Count == 0 && day.Holidays.Count == 0);
        return new LeaveCalendarMonthResponse(
            range.Year,
            range.Month,
            range.MonthStart,
            range.MonthEnd,
            includeTentativeBlocks,
            isEmpty,
            days);
    }

    private static LeaveCalendarAbsenceResponse ToAbsence(
        LeaveCalendarAbsenceInstance instance,
        LeaveCalendarOptions options)
    {
        var row = instance.Row;
        var request = row.Request;

        return new LeaveCalendarAbsenceResponse(
            request.Id,
            request.EmployeeId,
            row.EmployeeName,
            row.DepartmentId,
            row.DepartmentName,
            row.LegalEntityId,
            row.LegalEntityName,
            request.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            row.LeaveTypeCategory,
            options.ColorFor(row.LeaveTypeCategory),
            request.Status,
            instance.IsTentative,
            instance.IsPartialCancellationHistory,
            request.StartDate,
            request.EndDate,
            request.TotalDays,
            request.HalfDayPeriod);
    }

    private static LeaveCalendarHolidayResponse ToHoliday(LeaveCalendarHoliday holiday)
        => new(holiday.Date, holiday.Name, holiday.LegalEntityId, holiday.LegalEntityName, holiday.Source);
}
```

Create `LeaveCalendarMapperTests.cs`:

```csharp
using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Mappers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarMapperTests
{
    [Fact]
    public void ToMonthResponse_ReturnsAllDaysAndConfiguredColor()
    {
        var range = LeaveCalendarMonthRange.From(2026, 8).Value!;
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 8, 10),
            EndDate = new DateOnly(2026, 8, 10),
            Status = LeaveRequestStatuses.Approved,
            TotalDays = 1m,
            PaidDays = 1m
        };
        var row = new LeaveCalendarRequestRow(
            request, "Priya Nair", null, null, null, null, "Annual Leave", "AL", LeaveTypeCategories.Annual);
        var options = new LeaveCalendarOptions
        {
            TypeCategoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [LeaveTypeCategories.Annual] = "#2563EB"
            }
        };

        var response = LeaveCalendarMapper.ToMonthResponse(
            range,
            includeTentativeBlocks: true,
            [new LeaveCalendarAbsenceInstance(new DateOnly(2026, 8, 10), row, false, false)],
            [new LeaveCalendarHoliday(new DateOnly(2026, 8, 15), "Public holiday", null, null, "test")],
            options);

        response.Days.Should().HaveCount(31);
        response.Days.Single(x => x.Date == new DateOnly(2026, 8, 10))
            .Absences.Single().TypeColorHex.Should().Be("#2563EB");
        response.Days.Single(x => x.Date == new DateOnly(2026, 8, 15))
            .Holidays.Single().Name.Should().Be("Public holiday");
        response.IsEmpty.Should().BeFalse();
    }
}
```

- [ ] **Step 6: Verify Task 2**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCalendarMonthRangeTests|FullyQualifiedName~LeaveCalendarRequestProjectorTests|FullyQualifiedName~LeaveCalendarMapperTests"
dotnet build ONEVO.sln
```

Expected result: pure helper and mapper tests pass.

---

### Task 3: EF repository for visible calendar requests

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/Leave/Calendar/EfLeaveCalendarRepository.cs`
- Edit: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/EfLeaveCalendarRepositoryTests.cs`

**Interfaces:**
- Consumes: `ILeaveCalendarRepository`
- Consumes: `EmployeeVisibilityScope`
- Produces: batched month request rows with employee, department, legal entity, and leave type metadata
- Consumes later: query handler

- [ ] **Step 1: Implement EF repository**

Create `EfLeaveCalendarRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar;

public sealed class EfLeaveCalendarRepository : ILeaveCalendarRepository
{
    private readonly ApplicationDbContext _db;

    public EfLeaveCalendarRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LeaveCalendarRequestRow>> ListMonthRequestsAsync(
        Guid tenantId,
        EmployeeVisibilityScope scope,
        LeaveCalendarRequestFilter filter,
        CancellationToken ct = default)
    {
        var activePrimaryAssignments = _db.PositionAssignments.AsNoTracking()
            .Where(pa => pa.TenantId == tenantId
                && pa.AssignmentKind == PositionAssignmentKind.PrimaryEmployment
                && pa.AssignmentStatus == PositionAssignmentStatus.Active);

        var query =
            from request in _db.LeaveRequests.AsNoTracking()
            join employee in _db.Employees.AsNoTracking() on request.EmployeeId equals employee.Id
            join leaveType in _db.LeaveTypes.AsNoTracking() on request.LeaveTypeId equals leaveType.Id
            join department in _db.Departments.AsNoTracking() on employee.DepartmentId equals department.Id into departments
            from department in departments.DefaultIfEmpty()
            join legalEntity in _db.LegalEntities.AsNoTracking() on employee.LegalEntityId equals legalEntity.Id into legalEntities
            from legalEntity in legalEntities.DefaultIfEmpty()
            join primaryAssignment in activePrimaryAssignments on employee.Id equals primaryAssignment.EmployeeId into assignmentJoin
            from primaryAssignment in assignmentJoin.DefaultIfEmpty()
            join position in _db.Positions.AsNoTracking() on primaryAssignment!.PositionId equals position.Id into positionJoin
            from position in positionJoin.DefaultIfEmpty()
            where request.TenantId == tenantId
                && request.StartDate <= filter.MonthEnd
                && request.EndDate >= filter.MonthStart
                && (
                    request.Status == LeaveRequestStatuses.Approved
                    || (filter.IncludeTentativeBlocks
                        && (request.Status == LeaveRequestStatuses.Pending
                            || request.Status == LeaveRequestStatuses.InformationRequested))
                    || (request.Status == LeaveRequestStatuses.Cancelled
                        && request.PartialCancelEffectiveDate != null
                        && request.PartialCancelEffectiveDate > filter.MonthStart))
            select new { request, employee, leaveType, department, legalEntity, position };

        if (!scope.CanViewAllTenantEmployees)
        {
            var ownEmployeeId = scope.OwnEmployeeId;
            var coveredPositionIds = scope.CoveredPositionIds;
            var coveredDepartmentIds = scope.CoveredDepartmentIds;
            var companyWideLegalEntityIds = scope.CompanyWideLegalEntityIds;

            query = query.Where(row =>
                (ownEmployeeId != null && row.employee.Id == ownEmployeeId.Value)
                || (row.position != null && coveredPositionIds.Contains(row.position.Id))
                || (row.department != null && coveredDepartmentIds.Contains(row.department.Id))
                || (row.legalEntity != null && companyWideLegalEntityIds.Contains(row.legalEntity.Id)));
        }

        if (filter.DepartmentId is { } departmentId)
            query = query.Where(row => row.employee.DepartmentId == departmentId);

        var rows = await query
            .OrderBy(row => row.request.StartDate)
            .ThenBy(row => row.employee.LastName)
            .ThenBy(row => row.employee.FirstName)
            .ToListAsync(ct);

        return rows.Select(row => new LeaveCalendarRequestRow(
            row.request,
            LeaveEntitlementMapper.EmployeeName(row.employee.FirstName, row.employee.LastName),
            row.employee.DepartmentId,
            row.department?.Name,
            row.employee.LegalEntityId,
            row.legalEntity?.Name,
            row.leaveType.Name,
            row.leaveType.Code,
            row.leaveType.Category)).ToList();
    }
}
```

Do not call `ILeaveHolidayProvider` or any external service from this repository. It only reads persisted leave/org data.

- [ ] **Step 2: Register repository**

In `Infrastructure/DependencyInjection.cs`, near the leave repositories, add:

```csharp
services.AddScoped<
    ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces.ILeaveCalendarRepository,
    ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar.EfLeaveCalendarRepository>();
```

- [ ] **Step 3: Add repository tests**

Create `EfLeaveCalendarRepositoryTests.cs`. Use the same test DbContext factory pattern already used by `EfLeavePolicyRepositoryTests` and `EfLeaveEntitlementRepositoryTests`.

Cover:
- Approved requests overlapping the month are returned.
- Pending requests are returned only when `IncludeTentativeBlocks = true`.
- Rejected requests are excluded.
- Fully cancelled requests are excluded.
- Partially cancelled requests with `PartialCancelEffectiveDate` after month start are returned for the projector to clip.
- `DepartmentId` filter limits rows to employees in that department.
- Restricted `EmployeeVisibilityScope` returns own/covered rows and excludes unrelated rows.
- `EmployeeVisibilityScope.Unrestricted()` can return all tenant rows.

Test method names:

```csharp
[Fact]
public async Task ListMonthRequestsAsync_ReturnsApprovedRequestsOverlappingMonth()
```

```csharp
[Fact]
public async Task ListMonthRequestsAsync_ExcludesPending_WhenTentativeBlocksDisabled()
```

```csharp
[Fact]
public async Task ListMonthRequestsAsync_AppliesVisibilityScope()
```

- [ ] **Step 4: Verify Task 3**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~EfLeaveCalendarRepositoryTests"
dotnet build ONEVO.sln
```

Expected result: repository tests pass and DI compiles.

---

### Task 4: Team calendar query handler and access scoping

**Files:**
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Queries/GetLeaveCalendarQuery.cs`
- Create: `src/ONEVO.Application/Features/Leave/Calendar/Helpers/LeaveCalendarMessages.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/GetLeaveCalendarQueryHandlerTests.cs`

**Interfaces:**
- Produces: `GetLeaveCalendarQuery`
- Consumes: options, repository, projector, mapper, holiday provider, employee visibility resolver, current user
- Produces: permission-safe `LeaveCalendarMonthResponse`

- [ ] **Step 1: Add messages**

Create `LeaveCalendarMessages.cs`:

```csharp
namespace ONEVO.Application.Features.Leave.Calendar.Helpers;

public static class LeaveCalendarMessages
{
    public const string AuthRequired = "Authentication required.";
    public const string TenantMissing = "Tenant context missing.";
    public const string CalendarPermissionRequired = "Permission 'calendar:read' required.";
    public const string LeaveScopeRequired = "A leave read permission is required to view the leave calendar.";
    public const string NoEmployee = "Employee profile was not found for the current user.";
}
```

- [ ] **Step 2: Add query and handler**

Create `GetLeaveCalendarQuery.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Mappers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.Leave.Calendar.Queries;

public sealed record GetLeaveCalendarQuery(
    int Year,
    int Month,
    Guid? DepartmentId,
    bool? IncludeTentative)
    : IRequest<Result<LeaveCalendarMonthResponse>>;

public sealed class GetLeaveCalendarQueryHandler
    : IRequestHandler<GetLeaveCalendarQuery, Result<LeaveCalendarMonthResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopes;
    private readonly ILeaveCalendarRepository _repository;
    private readonly ILeaveCalendarHolidayProvider _holidays;
    private readonly LeaveCalendarRequestProjector _projector;
    private readonly LeaveCalendarOptions _options;

    public GetLeaveCalendarQueryHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        IEmployeeVisibilityScopeResolver visibilityScopes,
        ILeaveCalendarRepository repository,
        ILeaveCalendarHolidayProvider holidays,
        LeaveCalendarRequestProjector projector,
        IOptions<LeaveCalendarOptions> options)
    {
        _currentUser = currentUser;
        _employees = employees;
        _visibilityScopes = visibilityScopes;
        _repository = repository;
        _holidays = holidays;
        _projector = projector;
        _options = options.Value;
    }

    public async Task<Result<LeaveCalendarMonthResponse>> Handle(
        GetLeaveCalendarQuery query,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.AuthRequired);

        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.TenantMissing);

        if (!_currentUser.HasPermission("calendar:read"))
            return Result<LeaveCalendarMonthResponse>.Forbidden(LeaveCalendarMessages.CalendarPermissionRequired);

        var rangeResult = LeaveCalendarMonthRange.From(query.Year, query.Month);
        if (!rangeResult.IsSuccess)
            return Result<LeaveCalendarMonthResponse>.Failure(rangeResult.Error!, rangeResult.StatusCode ?? 400);

        var scopeResult = await ResolveScopeAsync(ct);
        if (!scopeResult.IsSuccess)
            return Result<LeaveCalendarMonthResponse>.Failure(scopeResult.Error!, scopeResult.StatusCode ?? 403);

        var includeTentative = query.IncludeTentative ?? _options.DefaultIncludeTentativeBlocks;
        var range = rangeResult.Value!;
        var rows = await _repository.ListMonthRequestsAsync(
            _currentUser.TenantId,
            scopeResult.Value!,
            new LeaveCalendarRequestFilter(range.MonthStart, range.MonthEnd, query.DepartmentId, includeTentative),
            ct);

        var legalEntityIds = rows
            .Select(row => row.LegalEntityId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        var holidays = await _holidays.ListHolidaysAsync(
            _currentUser.TenantId,
            legalEntityIds,
            range.MonthStart,
            range.MonthEnd,
            ct);

        var instances = _projector.Project(rows, range.MonthStart, range.MonthEnd, includeTentative);
        var response = LeaveCalendarMapper.ToMonthResponse(range, includeTentative, instances, holidays, _options);
        return Result<LeaveCalendarMonthResponse>.Success(response);
    }

    private async Task<Result<EmployeeVisibilityScope>> ResolveScopeAsync(CancellationToken ct)
    {
        if (_currentUser.HasPermission("leave:manage") || _currentUser.HasPermission("leave:read"))
            return Result<EmployeeVisibilityScope>.Success(EmployeeVisibilityScope.Unrestricted());

        if (_currentUser.HasPermission("leave:read-team"))
            return Result<EmployeeVisibilityScope>.Success(
                await _visibilityScopes.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct));

        if (_currentUser.HasPermission("leave:read-own"))
        {
            var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
            if (employee is null)
                return Result<EmployeeVisibilityScope>.NotFound(LeaveCalendarMessages.NoEmployee);

            return Result<EmployeeVisibilityScope>.Success(new EmployeeVisibilityScope(
                false,
                employee.Id,
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }

        return Result<EmployeeVisibilityScope>.Forbidden(LeaveCalendarMessages.LeaveScopeRequired);
    }
}
```

Register the pure projector in `Infrastructure/DependencyInjection.cs` or `Application/DependencyInjection.cs` next to the other leave helpers:

```csharp
services.AddSingleton<ONEVO.Application.Features.Leave.Calendar.Helpers.LeaveCalendarRequestProjector>();
```

- [ ] **Step 3: Add handler tests**

Create `GetLeaveCalendarQueryHandlerTests.cs`.

Cover:
- Unauthenticated caller returns `Authentication required.`
- Caller without `calendar:read` returns the calendar permission message.
- Caller with `calendar:read` but no leave read permission returns the leave-scope message.
- `leave:read-own` builds an own-employee-only scope.
- `leave:read-team` uses `IEmployeeVisibilityScopeResolver`.
- `leave:read` and `leave:manage` use unrestricted scope.
- `IncludeTentative = null` uses `LeaveCalendarOptions.DefaultIncludeTentativeBlocks`.
- `IncludeTentative = false` overrides the default.
- Handler passes visible legal entity IDs to `ILeaveCalendarHolidayProvider`.
- Response contains every day in the requested month.

Use this current-user helper in the test file:

```csharp
private static Mock<ICurrentUser> CurrentUser(Guid tenantId, Guid userId, params string[] permissions)
{
    var currentUser = new Mock<ICurrentUser>();
    currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
    currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
    currentUser.SetupGet(x => x.UserId).Returns(userId);
    currentUser.Setup(x => x.HasPermission(It.IsAny<string>()))
        .Returns<string>(permissions.Contains);
    return currentUser;
}
```

- [ ] **Step 4: Verify Task 4**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~GetLeaveCalendarQueryHandlerTests"
dotnet build ONEVO.sln
```

Expected result: access-scope tests pass and handler compiles.

---

### Task 5: HTTP endpoint

**Files:**
- Create: `src/ONEVO.Api/Controllers/Tenant/Leave/LeaveCalendarController.cs`
- Create: `tests/ONEVO.Tests.Unit/Features/Leave/Calendar/LeaveCalendarControllerTests.cs`

**Interfaces:**
- Produces: `GET /api/v1/leave/calendar?year=2026&month=8&departmentId=&includeTentative=`
- Consumes: `GetLeaveCalendarQuery`

- [ ] **Step 1: Add controller**

Create `LeaveCalendarController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.Leave.Calendar.Queries;

namespace ONEVO.Api.Controllers.Tenant.Leave;

[ApiController]
[Route("api/v1/leave/calendar")]
[Authorize(Policy = "TenantPolicy")]
public sealed class LeaveCalendarController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveCalendarController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [RequirePermission("calendar:read")]
    public async Task<IActionResult> Get(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? departmentId,
        [FromQuery] bool? includeTentative,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetLeaveCalendarQuery(year, month, departmentId, includeTentative),
            ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
```

Keep the handler-level leave-scope checks from Task 4. The controller filter only proves the caller can open the calendar module.

- [ ] **Step 2: Add controller tests**

Create `LeaveCalendarControllerTests.cs`:

```csharp
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarControllerTests
{
    [Fact]
    public async Task Get_SendsCalendarQuery()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetLeaveCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveCalendarMonthResponse>.Success(new LeaveCalendarMonthResponse(
                2026,
                8,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                true,
                true,
                [])));

        var controller = new LeaveCalendarController(mediator.Object);
        var result = await controller.Get(2026, 8, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), false, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(
            It.Is<GetLeaveCalendarQuery>(q =>
                q.Year == 2026 &&
                q.Month == 8 &&
                q.DepartmentId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") &&
                q.IncludeTentative == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ReturnsProblem_WhenQueryFails()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetLeaveCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveCalendarMonthResponse>.Forbidden("Nope"));

        var controller = new LeaveCalendarController(mediator.Object);
        var result = await controller.Get(2026, 8, null, null, CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }
}
```

- [ ] **Step 3: Verify Task 5**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~LeaveCalendarControllerTests"
dotnet build ONEVO.sln
```

Expected result: controller tests pass and route compiles.

---

### Task 6: Integration tests, smoke seeding, and docs sync

**Files:**
- Add integration tests under the existing API test project if Leave endpoint tests exist
- Edit only if live smoke users lack the permission: `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`
- Edit: `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`
- Edit: `docs/superpowers/plans/next/SUMMARY.md`
- Edit: `docs/superpowers/plans/SUMMARY.md`
- Add Postman/API docs only if this repo already documents Leave endpoints alongside previous Leave endpoints

**Interfaces:**
- Verifies: permissions, tenant isolation, own/team/all visibility, tentative block toggle, holiday provider integration, response shape, no N+1 query surprises
- Produces: updated plan index status

- [ ] **Step 1: Add integration coverage**

Cover:
- Caller without `calendar:read` receives 403.
- Caller with `calendar:read` but no leave read permission receives 403 from handler.
- Caller with `calendar:read` + `leave:read-own` sees only their own leave.
- Caller with `calendar:read` + `leave:read-team` sees only employees in the resolved visibility scope.
- Caller with `calendar:read` + `leave:read` sees tenant leave records.
- Department filter limits visible rows.
- Approved request appears on each day in its date range.
- Pending request appears only when `includeTentative=true` or the configured default is true.
- Rejected and fully cancelled requests do not appear.
- Partial-cancelled historical portion appears only before `PartialCancelEffectiveDate`.
- Response includes 28/29/30/31 day cells according to the requested month.
- Holiday provider rows are returned on the matching day.
- Tenant A cannot see Tenant B requests.

- [ ] **Step 2: Patch smoke seeding only if needed**

If dev smoke users are assigned permissions directly from `DevSmokeTestTenantSeeder.HrManagerPermissionCodes`, add `calendar:read` to that local/dev-only list:

```csharp
private static readonly IReadOnlyList<string> HrManagerPermissionCodes =
[
    "org:read", "org:manage", "employees:read", "employees:write", "roles:read",
    "calendar:read", "leave:read", "leave:manage", "leave:approve"
];
```

Do not add a production role-template migration in this task unless local testing proves real HR Manager users do not receive `calendar:read` through the Calendar module auto-grant. If a production role-template patch is needed, make it an explicit follow-up because `calendar:read` is a universal Calendar-module permission, not an HR-only Leave permission.

- [ ] **Step 3: Run focused verification**

Run:

```powershell
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~Leave.Calendar"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj
dotnet build ONEVO.sln
```

Expected result: calendar unit tests pass, architecture tests pass, build passes.

- [ ] **Step 4: Run live dev-DB smoke**

Against the existing dev smoke tenants from `DevSmokeTestTenantSeeder`, verify:
- `GET /api/v1/leave/calendar?year=2026&month=8` returns 200 for a user with `calendar:read` and a leave read permission.
- A user with only own leave scope sees only their own leave rows.
- An HR user sees all seeded leave rows in the tenant.
- `includeTentative=false` hides pending requests.
- `includeTentative=true` shows pending requests with `isTentative: true`.
- Department filtering changes the visible absence list.
- No records from another tenant appear.

Record exact commands and results in the phase summary. If Docker/Testcontainers is unavailable, record that the live smoke is pending for the same environmental reason noted in Parts 3, 4, and 5.

- [ ] **Step 5: Update summaries**

Update:
- `docs/superpowers/plans/next/2026-08-21-leave-management/SUMMARY.md`: mark Phase 7 as written in full, or executed if implementation has been completed.
- `docs/superpowers/plans/next/SUMMARY.md`: add `part-7-team-calendar.md` to the written-in-full list.
- `docs/superpowers/plans/SUMMARY.md`: change the Leave Management row from Parts 1-4 executed and Parts 5-6 written to the current executed/written state after this phase.

---

## Execution Handoff

Start with Task 1 and keep each task green before moving to the next one. The safest implementation order is:

1. Calendar options and holiday-detail provider.
2. Response DTOs, repository contract records, and pure range projector.
3. EF repository with visibility filtering.
4. Query handler access/scope assembly.
5. HTTP endpoint.
6. Integration/live smoke/docs sync.

Key behavior to preserve:
- `calendar:read` alone is not enough to see leave data.
- `leave:read-own` sees only the caller's own leave.
- `leave:read-team` uses `IEmployeeVisibilityScopeResolver`.
- `leave:read` and `leave:manage` can see tenant leave records.
- Approved leave is confirmed.
- Pending and information-requested leave is tentative and controlled by query/config.
- Rejected and fully cancelled requests are hidden.
- Partial-cancelled historical portions are clipped before the effective cancellation date.
- Type colors are config-driven; leave type category is always returned.
- Public holidays come from `ILeaveCalendarHolidayProvider`, with an explicit no-op adapter until a real provider exists.

Before marking complete, run the focused unit suite, architecture suite, build, and live dev-DB smoke or explicitly document the environmental blocker.
