# Attendance Day Detail Endpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add one aggregated endpoint, `GET /api/v1/attendance/time-tracking/history-detail`, that returns a single employee-day's attendance summary, a clock/break event timeline, and their TrayApp daily activity (idle/active minutes, app usage) in one response — feeding the frontend's new attendance detail drawer (see the companion frontend plan).

**Architecture:** Extend the existing `AttendanceReadHandler` (in `ONEVO.Application.Features.TimeAttendance.Queries`) with a third handled query, reusing its private `BuildRowsAsync` helper for the summary and a new small block of logic for the timeline and activity lookup. Add one new controller action to the existing `TimeTrackingController`. No new files for repositories or services — everything needed (`IAttendanceReadRepository`, `IActivityDailySummaryRepository`) is already registered in DI.

**Tech Stack:** .NET (ONEVO.Api / ONEVO.Application / ONEVO.Domain), MediatR, xUnit + Moq + FluentAssertions for tests.

## Global Constraints

- Own-data access (`employeeId == caller's employee id`) is always allowed, regardless of `attendance:read` or `monitoring:read`.
- Viewing another employee's summary/timeline requires `attendance:read` **and** the same `IEmployeeAuthorityResolver` visibility check `covered-history` already applies (`EmployeeAuthorityPurpose.TimeTrackingRead`). Failing this returns 403 for the whole request.
- Viewing another employee's daily activity additionally requires `monitoring:read`. If summary/timeline visibility passes but `monitoring:read` is absent, the request still succeeds (200) with `dailyActivity: null` — never 403 the whole request for a missing activity-only permission.
- `employeeId` and `date` are `[FromQuery]` parameters, not route segments — no other endpoint in this codebase binds `DateOnly` from a route segment, and `from`/`to` are query params everywhere else on this controller.
- This plan does not touch `ActivityDailySummaryAggregator`, `ActivityDailySummaryJob`, or any TrayApp ingest code — it only reads already-aggregated `ActivityDailySummary` rows.

---

### Task 1: `GetAttendanceDayDetailQuery` — DTOs, query, and handler

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadQueries.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs`

**Interfaces:**
- Produces: `GetAttendanceDayDetailQuery(Guid EmployeeId, DateOnly Date) : IRequest<Result<AttendanceDayDetailResponse>>`; `AttendanceDayDetailResponse(AttendanceHistoryRow Summary, IReadOnlyList<TimelineEvent> TimelineEvents, ActivityDailySummaryDto? DailyActivity)`; `TimelineEvent(string EventType, DateTimeOffset Timestamp, string Source)` — `EventType` is one of `"ClockIn"`, `"ClockOut"`, `"BreakStart"`, `"BreakEnd"`. Task 2 (controller) sends this query and returns `result.Value`/`result.Error` exactly like the existing `History`/`CoveredHistory` actions.

- [x] **Step 1: Write the failing handler tests**

Add to `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs`. First, extend the shared fixture so tests can control the new `IActivityDailySummaryRepository` dependency. Change the `Fixture` record definition (near the bottom of the file) from:

```csharp
private sealed record Fixture(AttendanceReadHandler Handler, Mock<IAttendanceReadRepository> Attendance, Mock<IClockInPolicyRepository> Policies, Mock<IEmployeeAuthorityResolver> Authority, LegalEntity LegalEntity);
```

to:

```csharp
private sealed record Fixture(AttendanceReadHandler Handler, Mock<IAttendanceReadRepository> Attendance, Mock<IClockInPolicyRepository> Policies, Mock<IEmployeeAuthorityResolver> Authority, LegalEntity LegalEntity, Mock<IActivityDailySummaryRepository> ActivitySummaries);
```

Add `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;` and `using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;` to the top of the file.

Change the `CreateFixture` signature from:

```csharp
private static Fixture CreateFixture(string localTimeUtc = "2026-08-21T10:00:00+00:00", string workModeCode = "remote", int employmentTypeId = 1)
```

to:

```csharp
private static Fixture CreateFixture(string localTimeUtc = "2026-08-21T10:00:00+00:00", string workModeCode = "remote", int employmentTypeId = 1, bool hasMonitoringRead = false)
```

Inside `CreateFixture`, change:

```csharp
currentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
```

to:

```csharp
currentUser.Setup(x => x.HasPermission("attendance:read")).Returns(true);
currentUser.Setup(x => x.HasPermission("monitoring:read")).Returns(hasMonitoringRead);
```

Then, right after the existing `var expectedWorkAreas = new Mock<IExpectedWorkAreaResolver>();` block, add:

```csharp
var activitySummaries = new Mock<IActivityDailySummaryRepository>();
activitySummaries.Setup(x => x.GetAsync(TenantId, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((ActivityDailySummary?)null);
```

Change the final `return new Fixture(...)` statement from:

```csharp
return new Fixture(
    new AttendanceReadHandler(currentUser.Object, employees.Object, attendance.Object, authority.Object, todayState),
    attendance,
    policies,
    authority,
    legalEntity);
```

to:

```csharp
return new Fixture(
    new AttendanceReadHandler(currentUser.Object, employees.Object, attendance.Object, authority.Object, todayState, activitySummaries: activitySummaries.Object),
    attendance,
    policies,
    authority,
    legalEntity,
    activitySummaries);
```

Now add these test methods anywhere inside the `AttendanceReadHandlerTests` class:

```csharp
[Fact]
public async Task DayDetail_Self_ReturnsSummaryTimelineAndActivityRegardlessOfPermissions()
{
    var fixture = CreateFixture();
    var record = new AttendanceRecord
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        EmployeeId = EmployeeId,
        Date = new(2026, 8, 21),
        ActualStart = DateTimeOffset.Parse("2026-08-21T04:00:00+00:00"),
        ActualEnd = DateTimeOffset.Parse("2026-08-21T12:30:00+00:00"),
        AttendanceSource = "web"
    };
    fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
        .ReturnsAsync(record);
    fixture.Attendance.Setup(x => x.ListBreaksAsync(TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([new BreakRecord { TenantId = TenantId, EmployeeId = EmployeeId, BreakStart = DateTimeOffset.Parse("2026-08-21T08:00:00+00:00"), BreakEnd = DateTimeOffset.Parse("2026-08-21T08:15:00+00:00") }]);
    fixture.ActivitySummaries.Setup(x => x.GetAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ActivityDailySummary { TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21), TotalActiveMinutes = 200, TotalIdleMinutes = 40 });

    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.Summary.AttendanceRecordId.Should().Be(record.Id);
    result.Value.TimelineEvents.Should().HaveCount(4);
    result.Value.TimelineEvents[0].EventType.Should().Be("ClockIn");
    result.Value.TimelineEvents[1].EventType.Should().Be("BreakStart");
    result.Value.TimelineEvents[2].EventType.Should().Be("BreakEnd");
    result.Value.TimelineEvents[3].EventType.Should().Be("ClockOut");
    result.Value.DailyActivity.Should().NotBeNull();
    result.Value.DailyActivity!.TotalActiveMinutes.Should().Be(200);
}

[Fact]
public async Task DayDetail_OtherEmployee_WithAttendanceReadAndMonitoringRead_ReturnsActivity()
{
    var fixture = CreateFixture(hasMonitoringRead: true);
    var otherId = Guid.NewGuid();
    fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, otherId]));
    var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21) };
    fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);
    fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [otherId] = new(otherId, "Jane Doe", "EMP-001", "Engineer", "Product", null) });
    fixture.ActivitySummaries.Setup(x => x.GetAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ActivityDailySummary { TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21), TotalActiveMinutes = 150 });

    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(otherId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.Summary.Employee!.DisplayName.Should().Be("Jane Doe");
    result.Value.DailyActivity.Should().NotBeNull();
    result.Value.DailyActivity!.TotalActiveMinutes.Should().Be(150);
}

[Fact]
public async Task DayDetail_OtherEmployee_WithAttendanceReadOnly_NullsActivityWithoutForbidden()
{
    var fixture = CreateFixture();
    var otherId = Guid.NewGuid();
    fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, otherId]));
    var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = otherId, Date = new(2026, 8, 21) };
    fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, otherId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);
    fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [otherId] = new(otherId, "Jane Doe", "EMP-001", "Engineer", "Product", null) });

    // Default fixture currentUser has attendance:read = true, monitoring:read unset (false).
    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(otherId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.DailyActivity.Should().BeNull();
    fixture.ActivitySummaries.Verify(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task DayDetail_OutsideVisibility_ReturnsForbidden()
{
    var fixture = CreateFixture();
    var hiddenId = Guid.NewGuid();
    fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(hiddenId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeFalse();
    result.StatusCode.Should().Be(403);
    fixture.Attendance.Verify(x => x.GetRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task DayDetail_NoAttendanceRecordForDate_ReturnsNotFound()
{
    var fixture = CreateFixture();
    fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>()))
        .ReturnsAsync((AttendanceRecord?)null);

    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeFalse();
    result.StatusCode.Should().Be(404);
}

[Fact]
public async Task DayDetail_NoActivitySummaryRow_ReturnsNullActivityNotError()
{
    var fixture = CreateFixture();
    var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21) };
    fixture.Attendance.Setup(x => x.GetRecordAsync(TenantId, EmployeeId, new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync(record);
    // fixture's default ActivitySummaries setup already returns null for any employee/date.

    var result = await fixture.Handler.Handle(new GetAttendanceDayDetailQuery(EmployeeId, new(2026, 8, 21)), CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    result.Value!.DailyActivity.Should().BeNull();
}

```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AttendanceReadHandlerTests.DayDetail"`
Expected: compile error (`GetAttendanceDayDetailQuery`, `AttendanceDayDetailResponse`, `TimelineEvent` don't exist yet) or, once Steps 3-4 below add the types but not the handler case, a runtime failure because `AttendanceReadHandler` doesn't implement `IRequestHandler<GetAttendanceDayDetailQuery, ...>` yet.

- [x] **Step 3: Add the query and response DTOs**

In `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadQueries.cs`, add after the existing `GetCoveredAttendanceHistoryQuery` line:

```csharp
public sealed record GetAttendanceDayDetailQuery(Guid EmployeeId, DateOnly Date) : IRequest<Result<AttendanceDayDetailResponse>>;
```

In `src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs`, add `using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;` at the top, and append at the end of the file:

```csharp
public sealed record TimelineEvent(
    string EventType,
    DateTimeOffset Timestamp,
    string Source);

public sealed record AttendanceDayDetailResponse(
    AttendanceHistoryRow Summary,
    IReadOnlyList<TimelineEvent> TimelineEvents,
    ActivityDailySummaryDto? DailyActivity);
```

- [x] **Step 4: Implement the handler**

In `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs`, add these usings at the top:

```csharp
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetActivityDailySummary;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
```

Change the class declaration from:

```csharp
public sealed class AttendanceReadHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IAttendanceReadRepository attendance,
    IEmployeeAuthorityResolver authority,
    IAttendanceTodayStateService todayState,
    ILeaveRequestReadRepository? leaveRequests = null,
    ILegalEntityRepository? legalEntities = null,
    IDateTimeProvider? dateTimeProvider = null)
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>
```

to:

```csharp
public sealed class AttendanceReadHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IAttendanceReadRepository attendance,
    IEmployeeAuthorityResolver authority,
    IAttendanceTodayStateService todayState,
    ILeaveRequestReadRepository? leaveRequests = null,
    ILegalEntityRepository? legalEntities = null,
    IDateTimeProvider? dateTimeProvider = null,
    IActivityDailySummaryRepository? activitySummaries = null)
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetAttendanceDayDetailQuery, Result<AttendanceDayDetailResponse>>
```

Then add this method right after the existing `Handle(GetCoveredAttendanceHistoryQuery ...)` method (before `BuildRowsAsync`):

```csharp
public async Task<Result<AttendanceDayDetailResponse>> Handle(
    GetAttendanceDayDetailQuery query, CancellationToken ct)
{
    if (!currentUser.IsAuthenticated)
        return Result<AttendanceDayDetailResponse>.Forbidden();

    var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
    if (actor is null)
        return Result<AttendanceDayDetailResponse>.NotFound("Current employee record was not found.");

    var isSelf = query.EmployeeId == actor.Id;
    bool canSeeActivity;

    if (isSelf)
    {
        canSeeActivity = true;
    }
    else
    {
        if (!currentUser.HasPermission(AttendanceReadPermission))
            return Result<AttendanceDayDetailResponse>.Forbidden();
        if (actor.LegalEntityId is null)
            return Result<AttendanceDayDetailResponse>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        if (!visibility.EmployeeIds.Contains(query.EmployeeId))
            return Result<AttendanceDayDetailResponse>.Forbidden();

        canSeeActivity = currentUser.HasPermission("monitoring:read");
    }

    var record = await attendance.GetRecordAsync(currentUser.TenantId, query.EmployeeId, query.Date, ct);
    if (record is null)
        return Result<AttendanceDayDetailResponse>.NotFound("No attendance record was found for this date.");

    var rows = await BuildRowsAsync([record], includeEmployee: !isSelf, actor.LegalEntityId, actor.Id, ct);
    var summary = rows[0];

    var legalEntity = legalEntities is not null && actor.LegalEntityId is Guid entityId
        ? await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, entityId, ct)
        : null;
    var timezone = TryFindTimezone(legalEntity?.Timezone ?? record.ScheduleTimezone);
    var dayWindow = AttendanceTodayStateService.GetLocalDayWindow(query.Date, timezone);
    var breaks = await attendance.ListBreaksAsync(
        currentUser.TenantId, query.EmployeeId, dayWindow.Start, dayWindow.End, ct);

    var timelineEvents = new List<TimelineEvent>();
    if (record.ActualStart is DateTimeOffset clockIn)
        timelineEvents.Add(new TimelineEvent("ClockIn", clockIn, record.AttendanceSource ?? "web"));
    foreach (var breakRecord in breaks)
    {
        timelineEvents.Add(new TimelineEvent("BreakStart", breakRecord.BreakStart, breakRecord.AutoDetected ? "desktop_tray" : "web"));
        if (breakRecord.BreakEnd is DateTimeOffset breakEnd)
            timelineEvents.Add(new TimelineEvent("BreakEnd", breakEnd, breakRecord.AutoDetected ? "desktop_tray" : "web"));
    }
    if (record.ActualEnd is DateTimeOffset clockOut)
        timelineEvents.Add(new TimelineEvent("ClockOut", clockOut, record.AttendanceSource ?? "web"));
    timelineEvents = timelineEvents.OrderBy(item => item.Timestamp).ToList();

    ActivityDailySummaryDto? dailyActivity = null;
    if (canSeeActivity && activitySummaries is not null)
    {
        var activityEntity = await activitySummaries.GetAsync(currentUser.TenantId, query.EmployeeId, query.Date, ct);
        if (activityEntity is not null)
            dailyActivity = GetActivityDailySummaryQueryHandler.Map(activityEntity);
    }

    return Result<AttendanceDayDetailResponse>.Success(
        new AttendanceDayDetailResponse(summary, timelineEvents, dailyActivity));
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AttendanceReadHandlerTests"`
Expected: PASS (all `DayDetail_*` tests plus every pre-existing test in the file, unaffected by the new optional constructor parameter).

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadQueries.cs src/ONEVO.Application/Features/TimeAttendance/DTOs/Responses/AttendanceReadResponses.cs src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs
git commit -m "feat: add GetAttendanceDayDetailQuery aggregating attendance day summary, timeline, and TrayApp daily activity"
```

---

### Task 2: `history-detail` controller endpoint

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs`
- Test: `tests/ONEVO.Tests.Unit/Controllers/Tenant/Attendance/TimeTrackingControllerTests.cs`

**Interfaces:**
- Consumes: `GetAttendanceDayDetailQuery(Guid EmployeeId, DateOnly Date) : IRequest<Result<AttendanceDayDetailResponse>>` from Task 1.
- Produces: `GET /api/v1/attendance/time-tracking/history-detail?employeeId={guid}&date={yyyy-MM-dd}` — 200 with `AttendanceDayDetailResponse` body on success, otherwise `Problem(result.Error, statusCode: result.StatusCode ?? 400)` matching every other action on this controller. This is the exact route/shape the frontend's `TimeTrackingApiService.getDayDetail` (frontend plan part-1, Task 2) calls.

- [x] **Step 1: Write the failing controller test**

Add to `tests/ONEVO.Tests.Unit/Controllers/Tenant/Attendance/TimeTrackingControllerTests.cs`. Add `using ONEVO.Application.Features.TimeAttendance.Queries;` if not already present (it is, via the `DTOs.Responses` using — check and add `Queries` explicitly since `GetAttendanceDayDetailQuery` lives there). Add this test:

```csharp
[Fact]
public async Task HistoryDetail_SendsQueryWithEmployeeIdAndDateAndReturnsOk()
{
    var mediator = new Mock<IMediator>();
    var expected = new AttendanceDayDetailResponse(
        new AttendanceHistoryRow(
            Guid.NewGuid(), WorkDate, null, null, null, false, 0, 0, null, null, "present",
            true, false, false, false),
        Array.Empty<TimelineEvent>(),
        null);
    mediator
        .Setup(x => x.Send(It.IsAny<GetAttendanceDayDetailQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<AttendanceDayDetailResponse>.Success(expected));
    var controller = new TimeTrackingController(mediator.Object);

    var result = await controller.HistoryDetail(EmployeeId, WorkDate, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result);
    Assert.Same(expected, ok.Value);
    mediator.Verify(x => x.Send(
        It.Is<GetAttendanceDayDetailQuery>(q => q.EmployeeId == EmployeeId && q.Date == WorkDate),
        It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task HistoryDetail_ForbiddenResult_ReturnsProblemWith403()
{
    var mediator = new Mock<IMediator>();
    mediator
        .Setup(x => x.Send(It.IsAny<GetAttendanceDayDetailQuery>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<AttendanceDayDetailResponse>.Forbidden());
    var controller = new TimeTrackingController(mediator.Object);

    var result = await controller.HistoryDetail(EmployeeId, WorkDate, CancellationToken.None);

    var problem = Assert.IsType<ObjectResult>(result);
    Assert.Equal(403, problem.StatusCode);
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TimeTrackingControllerTests.HistoryDetail"`
Expected: compile error — `TimeTrackingController.HistoryDetail` doesn't exist yet.

- [x] **Step 3: Add the controller action**

In `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs`, add after the `CoveredHistory` action, before the closing brace of the class:

```csharp
    [HttpGet("history-detail")]
    public async Task<IActionResult> HistoryDetail(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly date,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetAttendanceDayDetailQuery(employeeId, date), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

No `[RequirePermission]` attribute — matching `History` (self-service, no attribute) rather than `CoveredHistory`, because this one route serves both self and others, and self access must always succeed regardless of any permission. All permission logic lives in the handler from Task 1.

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~TimeTrackingControllerTests"`
Expected: PASS (new tests plus every pre-existing test in the file).

- [x] **Step 5: Full backend test suite and build sanity check**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: Build succeeded, 0 errors.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: PASS, no regressions in unrelated tests.

- [x] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs tests/ONEVO.Tests.Unit/Controllers/Tenant/Attendance/TimeTrackingControllerTests.cs
git commit -m "feat: expose GET .../history-detail for the attendance day detail drawer"
```

---

## After this plan

The frontend plan (`Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-08-27-attendance-history-redesign-frontend/`) depends on this endpoint existing. Its part-2 manual verification step runs against a live instance of this backend — start it with the local dev script before that step.

Once both backend tasks are done and tests pass, update this repo's `docs/superpowers/plans/SUMMARY.md` and `docs/superpowers/plans/next/SUMMARY.md` to add a row for this plan (status: pending until both tasks are checked off, then move to `finished/<date>/` per `FILE_CREATION_RULES.md` — see that file for the exact move procedure).
