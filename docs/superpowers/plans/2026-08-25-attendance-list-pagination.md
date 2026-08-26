# Attendance List Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add pagination (backend + frontend) to the Time Tracking page's "My attendance history", "My correction requests", and "Team attendance" (covered history) tables.

**Architecture:** Reuse the existing backend `PagedRequest`/`PagedResult<T>` convention (already used by e.g. `ListProjectsQuery`) and the existing frontend `PagedResultDto<T>` + Previous/Next-button convention (already used by the Employee list). No new abstractions — this plan threads the existing pattern through three more list endpoints and their consuming UI.

**Tech Stack:** ASP.NET Core / EF Core / MediatR (`Hrms-Backend-v1`), Angular 21 signal stores (`Hrms--Web-application---front-end---v1`), xUnit + Moq + FluentAssertions (backend tests), Vitest (frontend tests).

## Global Constraints

- Page size is fixed at 20 for all three tables; not user-configurable.
- No sorting UI is added. `PagedRequest.SortBy`/`SortDirection` stay at their unused defaults.
- The approvals inbox (`ListAttendanceCorrectionApprovalsQuery` / `GET /attendance/corrections/approvals`) is out of scope — do not touch it.
- Spec: `docs/superpowers/specs/2026-08-25-attendance-list-pagination-design.md` (this repo).

---

### Task 1: Paginate the attendance-history repository query

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs` (mock signature updates only — see Task 2, which touches the same file for the actual new test)

**Interfaces:**
- Consumes: nothing new.
- Produces: `IAttendanceReadRepository.ListRecordsAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, int skip, int take, CancellationToken ct = default) → Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)>` — this **replaces** the old 5-arg `ListRecordsAsync` (unpaged). It has exactly two callers in the whole codebase (`GetMyAttendanceHistoryQuery` and `GetCoveredAttendanceHistoryQuery` handlers, both updated in Task 2), so there is no unpaged variant left behind.

This task only changes the repository layer. It will not compile standalone until Task 2 updates the two call sites — that's expected; Tasks 1 and 2 land as one commit.

- [ ] **Step 1: Update the repository interface**

In `IAttendanceReadRepository.cs`, replace:

```csharp
    Task<IReadOnlyList<AttendanceRecord>> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);
```

with:

```csharp
    Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        int skip,
        int take,
        CancellationToken ct = default);
```

- [ ] **Step 2: Update the EF implementation**

In `EfAttendanceReadRepository.cs`, replace:

```csharp
    public async Task<IReadOnlyList<AttendanceRecord>> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
        => await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && employeeIds.Contains(x.EmployeeId)
                && x.Date >= from
                && x.Date <= to)
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);
```

with:

```csharp
    public async Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> ListRecordsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && employeeIds.Contains(x.EmployeeId)
                && x.Date >= from
                && x.Date <= to);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Date)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }
```

- [ ] **Step 3: Leave this uncommitted for now**

This task's code will not build until Task 2 updates the two call sites in `AttendanceReadHandlers.cs`. Proceed directly to Task 2 before building or committing.

---

### Task 2: Thread paging through the attendance-history queries and handler

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadQueries.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs`

**Interfaces:**
- Consumes: `IAttendanceReadRepository.ListRecordsAsync(..., int skip, int take, ct)` from Task 1.
- Produces: `GetMyAttendanceHistoryQuery(DateOnly From, DateOnly To, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>` and `GetCoveredAttendanceHistoryQuery(DateOnly From, DateOnly To, Guid? EmployeeId, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>` — both consumed by Task 3's controller.

- [ ] **Step 1: Update the query records**

In `AttendanceReadQueries.cs`, replace:

```csharp
public sealed record GetMyAttendanceHistoryQuery(DateOnly From, DateOnly To) : IRequest<Result<IReadOnlyList<AttendanceHistoryRow>>>;
public sealed record GetCoveredAttendanceHistoryQuery(DateOnly From, DateOnly To, Guid? EmployeeId) : IRequest<Result<IReadOnlyList<AttendanceHistoryRow>>>;
```

with:

```csharp
public sealed record GetMyAttendanceHistoryQuery(DateOnly From, DateOnly To, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>;
public sealed record GetCoveredAttendanceHistoryQuery(DateOnly From, DateOnly To, Guid? EmployeeId, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>;
```

(`PagedRequest`/`PagedResult` are already visible via the existing `using ONEVO.Application.Common.Models;`.)

- [ ] **Step 2: Update the handler's interface list and both `Handle` methods**

In `AttendanceReadHandlers.cs`, change the class declaration:

```csharp
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<IReadOnlyList<AttendanceHistoryRow>>>
```

to:

```csharp
    : IRequestHandler<GetAttendanceTodayQuery, Result<AttendanceTodayResponse>>,
      IRequestHandler<GetMyAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>,
      IRequestHandler<GetCoveredAttendanceHistoryQuery, Result<PagedResult<AttendanceHistoryRow>>>
```

Replace the `GetMyAttendanceHistoryQuery` handler body:

```csharp
    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetMyAttendanceHistoryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: false, employee.LegalEntityId, employee.Id, ct));
    }
```

with:

```csharp
    public async Task<Result<PagedResult<AttendanceHistoryRow>>> Handle(
        GetMyAttendanceHistoryQuery query, CancellationToken ct)
    {
        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<PagedResult<AttendanceHistoryRow>>.Failure(validation);

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee is null)
            return Result<PagedResult<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var skip = (pageNumber - 1) * query.Paging.PageSize;
        var (records, totalCount) = await attendance.ListRecordsAsync(
            currentUser.TenantId, [employee.Id], query.From, query.To, skip, query.Paging.PageSize, ct);
        var rows = await BuildRowsAsync(records, includeEmployee: false, employee.LegalEntityId, employee.Id, ct);
        return Result<PagedResult<AttendanceHistoryRow>>.Success(
            new PagedResult<AttendanceHistoryRow>(rows, pageNumber, query.Paging.PageSize, totalCount));
    }
```

Replace the `GetCoveredAttendanceHistoryQuery` handler body:

```csharp
    public async Task<Result<IReadOnlyList<AttendanceHistoryRow>>> Handle(
        GetCoveredAttendanceHistoryQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.Failure(validation);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<IReadOnlyList<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        IReadOnlyCollection<Guid> employeeIds;
        if (query.EmployeeId is Guid requestedEmployeeId)
        {
            if (!visibility.EmployeeIds.Contains(requestedEmployeeId))
                return Result<IReadOnlyList<AttendanceHistoryRow>>.Forbidden();

            employeeIds = [requestedEmployeeId];
        }
        else
        {
            employeeIds = visibility.EmployeeIds;
        }

        var records = await attendance.ListRecordsAsync(
            currentUser.TenantId, employeeIds, query.From, query.To, ct);
        return Result<IReadOnlyList<AttendanceHistoryRow>>.Success(
            await BuildRowsAsync(records, includeEmployee: true, actor.LegalEntityId, actor.Id, ct));
    }
```

with:

```csharp
    public async Task<Result<PagedResult<AttendanceHistoryRow>>> Handle(
        GetCoveredAttendanceHistoryQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(AttendanceReadPermission))
            return Result<PagedResult<AttendanceHistoryRow>>.Forbidden();

        var validation = ValidateRange(query.From, query.To);
        if (validation is not null)
            return Result<PagedResult<AttendanceHistoryRow>>.Failure(validation);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (actor?.LegalEntityId is null)
            return Result<PagedResult<AttendanceHistoryRow>>.NotFound("Current employee record was not found.");

        var visibility = await authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                currentUser.UserId,
                actor.LegalEntityId.Value,
                AttendanceReadPermission,
                IncludeSelf: true,
                EmployeeAuthorityPurpose.TimeTrackingRead), ct);

        IReadOnlyCollection<Guid> employeeIds;
        if (query.EmployeeId is Guid requestedEmployeeId)
        {
            if (!visibility.EmployeeIds.Contains(requestedEmployeeId))
                return Result<PagedResult<AttendanceHistoryRow>>.Forbidden();

            employeeIds = [requestedEmployeeId];
        }
        else
        {
            employeeIds = visibility.EmployeeIds;
        }

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var skip = (pageNumber - 1) * query.Paging.PageSize;
        var (records, totalCount) = await attendance.ListRecordsAsync(
            currentUser.TenantId, employeeIds, query.From, query.To, skip, query.Paging.PageSize, ct);
        var rows = await BuildRowsAsync(records, includeEmployee: true, actor.LegalEntityId, actor.Id, ct);
        return Result<PagedResult<AttendanceHistoryRow>>.Success(
            new PagedResult<AttendanceHistoryRow>(rows, pageNumber, query.Paging.PageSize, totalCount));
    }
```

`BuildRowsAsync` itself is untouched — it already just processes whatever `records` list it's handed.

- [ ] **Step 3: Update the three existing covered-history tests in `AttendanceReadHandlerTests.cs`**

Add `using ONEVO.Application.Common.Models;` to the top of the test file (needed for `PagedRequest`).

Replace:

```csharp
    [Fact]
    public async Task CoveredHistory_WithEmployeeFilterQueriesOnlyThatVisibleEmployeeAndPreservesIdentity()
    {
        var fixture = CreateFixture();
        var selectedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, selectedId, otherId]));
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = selectedId, Date = new(2026, 8, 21), Status = "late" };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { selectedId })), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync([record]);
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [selectedId] = new(selectedId, "Jane Doe", "EMP-001", "Engineer", "Product", Guid.NewGuid()) });

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), selectedId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].Employee!.DisplayName.Should().Be("Jane Doe");
        result.Value[0].Employee.EmployeeNumber.Should().Be("EMP-001");
    }
```

with:

```csharp
    [Fact]
    public async Task CoveredHistory_WithEmployeeFilterQueriesOnlyThatVisibleEmployeeAndPreservesIdentity()
    {
        var fixture = CreateFixture();
        var selectedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, selectedId, otherId]));
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = selectedId, Date = new(2026, 8, 21), Status = "late" };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { selectedId })), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((new List<AttendanceRecord> { record }, 1));
        fixture.Attendance.Setup(x => x.ListEmployeeIdentitiesAsync(TenantId, LegalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AttendanceHistoryEmployee> { [selectedId] = new(selectedId, "Jane Doe", "EMP-001", "Engineer", "Product", Guid.NewGuid()) });

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), selectedId, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].Employee!.DisplayName.Should().Be("Jane Doe");
        result.Value.Items[0].Employee!.EmployeeNumber.Should().Be("EMP-001");
        result.Value.TotalCount.Should().Be(1);
    }
```

Replace:

```csharp
    [Fact]
    public async Task CoveredHistory_OutsideVisibilityReturnsForbidden()
    {
        var fixture = CreateFixture();
        var hiddenId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), hiddenId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        fixture.Attendance.Verify(x => x.ListRecordsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

with:

```csharp
    [Fact]
    public async Task CoveredHistory_OutsideVisibilityReturnsForbidden()
    {
        var fixture = CreateFixture();
        var hiddenId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId]));

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), hiddenId, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        fixture.Attendance.Verify(x => x.ListRecordsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

Replace:

```csharp
    [Fact]
    public async Task CoveredHistory_WithoutEmployeeFilter_QueriesAllResolverVisibleEmployees()
    {
        var fixture = CreateFixture();
        var secondId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, secondId]));
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Attendance.Verify(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<CancellationToken>()), Times.Once);
    }
```

with:

```csharp
    [Fact]
    public async Task CoveredHistory_WithoutEmployeeFilter_QueriesAllResolverVisibleEmployees()
    {
        var fixture = CreateFixture();
        var secondId = Guid.NewGuid();
        fixture.Authority.Setup(x => x.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(UserId, LegalEntityId, true, [EmployeeId, secondId]));
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((new List<AttendanceRecord>(), 0));

        var result = await fixture.Handler.Handle(new GetCoveredAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), null, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Attendance.Verify(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(EmployeeId) && ids.Contains(secondId)), new(2026, 8, 1), new(2026, 8, 21), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 4: Add a new test proving the paging math and `PagedResult` envelope for `GetMyAttendanceHistoryQuery`**

Add this test anywhere in the `AttendanceReadHandlerTests` class (e.g. right after `CoveredHistory_WithoutEmployeeFilter_QueriesAllResolverVisibleEmployees`):

```csharp
    [Fact]
    public async Task MyHistory_AppliesPagingAndReturnsPagedResult()
    {
        var fixture = CreateFixture();
        var record = new AttendanceRecord { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, Date = new(2026, 8, 21) };
        fixture.Attendance.Setup(x => x.ListRecordsAsync(TenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { EmployeeId })), new(2026, 8, 1), new(2026, 8, 21), 20, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AttendanceRecord> { record }, 45));

        var result = await fixture.Handler.Handle(
            new GetMyAttendanceHistoryQuery(new(2026, 8, 1), new(2026, 8, 21), new PagedRequest { PageNumber = 2, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.PageNumber.Should().Be(2);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalCount.Should().Be(45);
        result.Value.TotalPages.Should().Be(3);
    }
```

This asserts the skip math directly: page 2 with page size 20 must call the repository with `skip = 20`.

- [ ] **Step 5: Build and run the affected tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AttendanceReadHandlerTests`
Expected: all tests in `AttendanceReadHandlerTests` pass, including the 4 touched/added above.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceReadRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceReadRepository.cs src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadQueries.cs src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceReadHandlers.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceReadHandlerTests.cs
git commit -m "feat: paginate my/covered attendance history queries"
```

---

### Task 3: Expose paging on the TimeTrackingController history endpoints

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs`

**Interfaces:**
- Consumes: `GetMyAttendanceHistoryQuery`/`GetCoveredAttendanceHistoryQuery` (now requiring `PagedRequest Paging`) from Task 2.
- Produces: `GET /api/v1/attendance/time-tracking/history?from&to&pageNumber&pageSize` and `GET /api/v1/attendance/time-tracking/covered-history?from&to&employeeId&pageNumber&pageSize`, both returning the serialized `PagedResult<AttendanceHistoryRow>` (camelCase: `items`, `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasNext`, `hasPrevious`) — consumed by Task 7's frontend API service.

- [ ] **Step 1: Add the using and bind `PagedRequest` on both actions**

Add `using ONEVO.Application.Common.Models;` to the top of `TimeTrackingController.cs`.

Replace:

```csharp
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyAttendanceHistoryQuery(from, to), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("covered-history")]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> CoveredHistory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetCoveredAttendanceHistoryQuery(from, to, employeeId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

with:

```csharp
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetMyAttendanceHistoryQuery(from, to, paging), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpGet("covered-history")]
    [RequirePermission("attendance:read")]
    public async Task<IActionResult> CoveredHistory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetCoveredAttendanceHistoryQuery(from, to, employeeId, paging), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 2: Build the API project**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: builds with no errors (this is what proves Tasks 1–3 fit together end to end).

- [ ] **Step 3: Run the full unit test suite once more**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all pass (confirms nothing else references the old `ListRecordsAsync`/query shapes).

- [ ] **Step 4: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Attendance/TimeTrackingController.cs
git commit -m "feat: bind paging query params on attendance history endpoints"
```

---

### Task 4: Paginate the attendance-corrections repository query

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceCorrectionRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceCorrectionRepository.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IAttendanceCorrectionRepository.ListMyAsync(Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status, int skip, int take, CancellationToken ct = default) → Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)>` — replaces the old unpaged `ListMyAsync`. Its only caller is `AttendanceCorrectionWorkflow.ListMyAsync`, updated in Task 5. `ListApprovalInboxAsync` is untouched (different method, out of scope).

- [ ] **Step 1: Update the repository interface**

In `IAttendanceCorrectionRepository.cs`, replace:

```csharp
    Task<IReadOnlyList<AttendanceCorrection>> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status, CancellationToken ct = default);
```

with:

```csharp
    Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default);
```

- [ ] **Step 2: Update the EF implementation**

In `EfAttendanceCorrectionRepository.cs`, replace:

```csharp
    public async Task<IReadOnlyList<AttendanceCorrection>> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        CancellationToken ct = default)
    {
        var query = FromDateFiltered(tenantId, employeeId, from, to);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }
```

with:

```csharp
    public async Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> ListMyAsync(
        Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = FromDateFiltered(tenantId, employeeId, from, to);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }
```

`ListApprovalInboxAsync`, directly below in both files, is untouched.

This task will not compile standalone until Task 5 updates the one call site in `AttendanceCorrectionWorkflow.cs` — proceed directly to Task 5 before building.

---

### Task 5: Thread paging through ListMyAttendanceCorrectionsQuery and the workflow

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceCorrections/AttendanceCorrectionQueries.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceCorrectionNotificationTests.cs`

**Interfaces:**
- Consumes: `IAttendanceCorrectionRepository.ListMyAsync(..., int skip, int take, ct)` from Task 4.
- Produces: `ListMyAttendanceCorrectionsQuery(DateOnly? From, DateOnly? To, string? Status, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceCorrectionResponse>>>` — consumed by Task 6's controller.

- [ ] **Step 1: Update the query record**

In `AttendanceCorrectionQueries.cs`, replace:

```csharp
public sealed record ListMyAttendanceCorrectionsQuery(
    DateOnly? From,
    DateOnly? To,
    string? Status) : IRequest<Result<IReadOnlyList<AttendanceCorrectionResponse>>>;
```

with:

```csharp
public sealed record ListMyAttendanceCorrectionsQuery(
    DateOnly? From,
    DateOnly? To,
    string? Status,
    PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceCorrectionResponse>>>;
```

Leave `ListAttendanceCorrectionApprovalsQuery` (directly below) unchanged.

- [ ] **Step 2: Update `AttendanceCorrectionWorkflow.ListMyAsync`**

Replace:

```csharp
    public async Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> ListMyAsync(
        ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var rows = await corrections.ListMyAsync(currentUser.TenantId, value.Employee.Id,
            request.From, request.To, request.Status, ct);
        return Result<IReadOnlyList<AttendanceCorrectionResponse>>.Success(
            rows.Select(row => ToResponse(row, value.Employee,
                ResolveTimezone(value.LegalEntity, row.WorkDate))).ToArray());
    }
```

with:

```csharp
    public async Task<Result<PagedResult<AttendanceCorrectionResponse>>> ListMyAsync(
        ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PagedResult<AttendanceCorrectionResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var pageNumber = request.Paging.PageNumber < 1 ? 1 : request.Paging.PageNumber;
        var skip = (pageNumber - 1) * request.Paging.PageSize;
        var (rows, totalCount) = await corrections.ListMyAsync(currentUser.TenantId, value.Employee.Id,
            request.From, request.To, request.Status, skip, request.Paging.PageSize, ct);
        var items = rows.Select(row => ToResponse(row, value.Employee,
            ResolveTimezone(value.LegalEntity, row.WorkDate))).ToArray();
        return Result<PagedResult<AttendanceCorrectionResponse>>.Success(
            new PagedResult<AttendanceCorrectionResponse>(items, pageNumber, request.Paging.PageSize, totalCount));
    }
```

- [ ] **Step 3: Update `ListMyAttendanceCorrectionsQueryHandler`**

Replace:

```csharp
public sealed class ListMyAttendanceCorrectionsQueryHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<ListMyAttendanceCorrectionsQuery, Result<IReadOnlyList<AttendanceCorrectionResponse>>>
{
    public Task<Result<IReadOnlyList<AttendanceCorrectionResponse>>> Handle(ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
        => workflow.ListMyAsync(request, ct);
}
```

with:

```csharp
public sealed class ListMyAttendanceCorrectionsQueryHandler(AttendanceCorrectionWorkflow workflow)
    : IRequestHandler<ListMyAttendanceCorrectionsQuery, Result<PagedResult<AttendanceCorrectionResponse>>>
{
    public Task<Result<PagedResult<AttendanceCorrectionResponse>>> Handle(ListMyAttendanceCorrectionsQuery request, CancellationToken ct)
        => workflow.ListMyAsync(request, ct);
}
```

Leave `ListAttendanceCorrectionApprovalsQueryHandler` (directly below) unchanged. `PagedRequest`/`PagedResult` are already visible in this file via the existing `using ONEVO.Application.Common.Models;`.

- [ ] **Step 4: Update the one existing unit test that calls `ListMyAsync`**

In `AttendanceCorrectionNotificationTests.cs`, confirm `using ONEVO.Application.Common.Models;` is present at the top of the file; add it if not.

Replace:

```csharp
        fixture.Corrections.Setup(x => x.ListMyAsync(fixture.TenantId, fixture.EmployeeId,
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { correction });

        var result = await fixture.Workflow.ListMyAsync(
            new ListMyAttendanceCorrectionsQuery(null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ApprovalRequired.Should().BeTrue();
```

with:

```csharp
        fixture.Corrections.Setup(x => x.ListMyAsync(fixture.TenantId, fixture.EmployeeId,
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { correction }, 1));

        var result = await fixture.Workflow.ListMyAsync(
            new ListMyAttendanceCorrectionsQuery(null, null, null, new PagedRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].ApprovalRequired.Should().BeTrue();
```

- [ ] **Step 5: Add a new test proving the paging math for `ListMyAttendanceCorrectionsQuery`**

Add this test to `AttendanceCorrectionNotificationTests.cs`, near `ListMy_DoesNotRecalculateApprovalRequirementFromCurrentPolicy`:

```csharp
    [Fact]
    public async Task ListMy_AppliesPagingAndReturnsPagedResult()
    {
        var fixture = new Fixture(approvalRequired: false);
        var correction = fixture.PendingCorrection();
        fixture.Corrections.Setup(x => x.ListMyAsync(fixture.TenantId, fixture.EmployeeId,
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<string?>(),
                20, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { correction }, 33));

        var result = await fixture.Workflow.ListMyAsync(
            new ListMyAttendanceCorrectionsQuery(null, null, null, new PagedRequest { PageNumber = 2, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.PageNumber.Should().Be(2);
        result.Value.TotalCount.Should().Be(33);
        result.Value.TotalPages.Should().Be(2);
    }
```

- [ ] **Step 6: Build and run the affected tests**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~AttendanceCorrectionNotificationTests`
Expected: all pass, including the 2 touched/added above.

- [ ] **Step 7: Commit**

```bash
git add src/ONEVO.Application/Features/TimeAttendance/RepositoryInterfaces/IAttendanceCorrectionRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/TimeAttendance/EfAttendanceCorrectionRepository.cs src/ONEVO.Application/Features/TimeAttendance/Queries/AttendanceCorrections/AttendanceCorrectionQueries.cs src/ONEVO.Application/Features/TimeAttendance/Commands/AttendanceCorrections/AttendanceCorrectionWorkflow.cs tests/ONEVO.Tests.Unit/Features/TimeAttendance/AttendanceCorrectionNotificationTests.cs
git commit -m "feat: paginate ListMyAttendanceCorrectionsQuery"
```

---

### Task 6: Expose paging on the "my corrections" endpoint

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/Attendance/AttendanceCorrectionsController.cs`
- Modify: `tests/ONEVO.Tests.Integration/Features/TimeAttendance/AttendanceCorrectionsIntegrationTests.cs`

**Interfaces:**
- Consumes: `ListMyAttendanceCorrectionsQuery` (now requiring `PagedRequest Paging`) from Task 5.
- Produces: `GET /api/v1/attendance/corrections/my?from&to&status&pageNumber&pageSize` returning the serialized `PagedResult<AttendanceCorrectionResponse>` — consumed by Task 7's frontend API service.

- [ ] **Step 1: Add the using and bind `PagedRequest` on the `My` action**

Add `using ONEVO.Application.Common.Models;` to the top of `AttendanceCorrectionsController.cs`.

Replace:

```csharp
    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyAttendanceCorrectionsQuery(from, to, status), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

with:

```csharp
    [HttpGet("my")]
    public async Task<IActionResult> My(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
        [FromQuery] PagedRequest paging,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListMyAttendanceCorrectionsQuery(from, to, status, paging), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 2: Update the one integration test that reads this endpoint's response as a bare array**

In `AttendanceCorrectionsIntegrationTests.cs`, replace:

```csharp
        var response = await SendAsync(HttpMethod.Get, _requesterA.Host, "/api/v1/attendance/corrections/my",
            body: null, cookie: _requesterA.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await ReadJsonAsync(response);

        var manual = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == manuallyApprovedId);
        var auto = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == autoApprovedId);

        manual.GetProperty("approvalRequired").GetBoolean().Should().BeTrue();
        auto.GetProperty("approvalRequired").GetBoolean().Should().BeFalse();
```

with:

```csharp
        var response = await SendAsync(HttpMethod.Get, _requesterA.Host, "/api/v1/attendance/corrections/my",
            body: null, cookie: _requesterA.SessionCookie);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadJsonAsync(response);
        var items = page.GetProperty("items");

        page.GetProperty("totalCount").GetInt32().Should().Be(2);

        var manual = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == manuallyApprovedId);
        var auto = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == autoApprovedId);

        manual.GetProperty("approvalRequired").GetBoolean().Should().BeTrue();
        auto.GetProperty("approvalRequired").GetBoolean().Should().BeFalse();
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: builds with no errors.

- [ ] **Step 4: Run the integration test (requires Docker for Testcontainers, or `ONEVO_TEST_DB` pointed at a local Postgres)**

Run: `dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter FullyQualifiedName~AttendanceCorrectionsIntegrationTests.ApiResponse_UsesStoredApprovalRequiredValue_NotDerivedFromStatus`
Expected: PASS. If Docker isn't available in this environment, skip running it but leave the code change in place — it is a mechanical shape fix, not new logic.

- [ ] **Step 5: Run the full backend unit test suite**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`
Expected: all pass. This is the last backend task — this confirms the whole backend side of the feature is internally consistent.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/Attendance/AttendanceCorrectionsController.cs tests/ONEVO.Tests.Integration/Features/TimeAttendance/AttendanceCorrectionsIntegrationTests.cs
git commit -m "feat: bind paging query params on the my-corrections endpoint"
```

---

### Task 7: Add `PagedResultDto` and paginate `TimeTrackingApiService`

**Repo:** `Hrms--Web-application---front-end---v1`

**Files:**
- Modify: `src/app/modules/attendance/models/time-tracking.model.ts`
- Modify: `src/app/modules/attendance/data-access/time-tracking-api.service.ts`
- Modify: `src/app/modules/attendance/data-access/time-tracking-api.service.spec.ts`

**Interfaces:**
- Consumes: the backend's `PagedResult<T>` JSON shape (`items`, `pageNumber`, `pageSize`, `totalCount`, ...) from Tasks 3 and 6.
- Produces: `PagedResultDto<T>` (`{ items: T[]; pageNumber: number; pageSize: number; totalCount: number }`), and `TimeTrackingApiService.getMyHistory(from, to, page, pageSize) → Observable<PagedResultDto<AttendanceHistoryRow>>`, `getCoveredHistory(from, to, page, pageSize, employeeId?) → Observable<PagedResultDto<AttendanceHistoryRow>>`, `getMyCorrections(page, pageSize, from?, to?) → Observable<PagedResultDto<AttendanceCorrectionListItem>>` — all consumed by Task 8's store.

- [ ] **Step 1: Add `PagedResultDto<T>` to the model file**

In `time-tracking.model.ts`, add after the `TimeTrackingRange` type at the end of the file:

```ts
export interface PagedResultDto<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}
```

- [ ] **Step 2: Write the failing api-service tests first**

In `time-tracking-api.service.spec.ts`, replace the import line:

```ts
import { AttendanceHistoryRow, AttendanceTodayResponse } from '../models/time-tracking.model';
```

with:

```ts
import { AttendanceHistoryRow, AttendanceTodayResponse, PagedResultDto } from '../models/time-tracking.model';
```

Replace the `'preserves the expanded History response fields'` test:

```ts
  it('preserves the expanded History response fields', () => {
    const row: AttendanceHistoryRow = {
      attendanceRecordId: 'record-1',
      workDate: '2026-08-22',
      employee: null,
      clockInAt: null,
      clockOutAt: null,
      isActive: false,
      breakMinutes: 45,
      totalWorkedMinutes: 0,
      expectedWorkMode: null,
      attendanceSource: null,
      status: 'over_break',
      canViewDetails: true,
      canRequestCorrection: false,
      canRequestWorkAreaChange: false,
      canCorrect: false,
      statusLabel: 'Over break allowance',
      attentionType: 'over_break',
      attentionLabel: 'Break time has exceeded the allowance',
      attentionSeverity: 'warning',
      breakOverageMinutes: 15,
      isOverBreakAllowance: true
    };
    service.getMyHistory('2026-08-17', '2026-08-23').subscribe((response) => {
      expect(response[0]).toEqual(row);
    });
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/history` && candidate.params.get('from') === '2026-08-17'
    );
    request.flush([row]);
  });

  it('getMyHistory passes from and to query parameters', () => {
    service.getMyHistory('2026-08-17', '2026-08-23').subscribe();
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/history` && candidate.params.get('from') === '2026-08-17'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('from')).toBe('2026-08-17');
    expect(request.request.params.get('to')).toBe('2026-08-23');
    request.flush([]);
  });

  it('getCoveredHistory passes range and optional employeeId', () => {
    service.getCoveredHistory('2026-08-17', '2026-08-23', 'employee-7').subscribe();
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/covered-history` && candidate.params.get('employeeId') === 'employee-7'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('from')).toBe('2026-08-17');
    expect(request.request.params.get('to')).toBe('2026-08-23');
    expect(request.request.params.get('employeeId')).toBe('employee-7');
    request.flush([]);
  });
```

with:

```ts
  it('preserves the expanded History response fields', () => {
    const row: AttendanceHistoryRow = {
      attendanceRecordId: 'record-1',
      workDate: '2026-08-22',
      employee: null,
      clockInAt: null,
      clockOutAt: null,
      isActive: false,
      breakMinutes: 45,
      totalWorkedMinutes: 0,
      expectedWorkMode: null,
      attendanceSource: null,
      status: 'over_break',
      canViewDetails: true,
      canRequestCorrection: false,
      canRequestWorkAreaChange: false,
      canCorrect: false,
      statusLabel: 'Over break allowance',
      attentionType: 'over_break',
      attentionLabel: 'Break time has exceeded the allowance',
      attentionSeverity: 'warning',
      breakOverageMinutes: 15,
      isOverBreakAllowance: true
    };
    const page: PagedResultDto<AttendanceHistoryRow> = { items: [row], pageNumber: 1, pageSize: 20, totalCount: 1 };
    service.getMyHistory('2026-08-17', '2026-08-23', 1, 20).subscribe((response) => {
      expect(response.items[0]).toEqual(row);
    });
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/history` && candidate.params.get('from') === '2026-08-17'
    );
    request.flush(page);
  });

  it('getMyHistory passes from, to, and paging query parameters', () => {
    service.getMyHistory('2026-08-17', '2026-08-23', 2, 20).subscribe();
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/history` && candidate.params.get('from') === '2026-08-17'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('from')).toBe('2026-08-17');
    expect(request.request.params.get('to')).toBe('2026-08-23');
    expect(request.request.params.get('pageNumber')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush({ items: [], pageNumber: 2, pageSize: 20, totalCount: 0 });
  });

  it('getCoveredHistory passes range, paging, and optional employeeId', () => {
    service.getCoveredHistory('2026-08-17', '2026-08-23', 1, 20, 'employee-7').subscribe();
    const request = httpMock.expectOne(
      (candidate) => candidate.url === `${base}/covered-history` && candidate.params.get('employeeId') === 'employee-7'
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('from')).toBe('2026-08-17');
    expect(request.request.params.get('to')).toBe('2026-08-23');
    expect(request.request.params.get('pageNumber')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('employeeId')).toBe('employee-7');
    request.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 });
  });
```

Replace the `'loads requester corrections and pending approvals with status filters'` test:

```ts
  it('loads requester corrections and pending approvals with status filters', () => {
    service.getMyCorrections('2026-08-01', '2026-08-31').subscribe();
    const mine = httpMock.expectOne((candidate) => candidate.url.endsWith('/attendance/corrections/my'));
    expect(mine.request.params.get('from')).toBe('2026-08-01');
    expect(mine.request.params.get('to')).toBe('2026-08-31');
    mine.flush([]);

    service.getApprovalInbox().subscribe();
    const approvals = httpMock.expectOne((candidate) => candidate.url.endsWith('/attendance/corrections/approvals'));
    expect(approvals.request.params.get('status')).toBe('pending');
    approvals.flush([]);
  });
```

with:

```ts
  it('loads requester corrections with paging and optional date filters, and pending approvals with status filters', () => {
    service.getMyCorrections(1, 20, '2026-08-01', '2026-08-31').subscribe();
    const mine = httpMock.expectOne((candidate) => candidate.url.endsWith('/attendance/corrections/my'));
    expect(mine.request.params.get('from')).toBe('2026-08-01');
    expect(mine.request.params.get('to')).toBe('2026-08-31');
    expect(mine.request.params.get('pageNumber')).toBe('1');
    expect(mine.request.params.get('pageSize')).toBe('20');
    mine.flush({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 });

    service.getApprovalInbox().subscribe();
    const approvals = httpMock.expectOne((candidate) => candidate.url.endsWith('/attendance/corrections/approvals'));
    expect(approvals.request.params.get('status')).toBe('pending');
    approvals.flush([]);
  });
```

- [ ] **Step 3: Run the tests to confirm they fail against the current (unpaged) service**

Run: `npx ng test --watch=false --include='**/time-tracking-api.service.spec.ts'`
Expected: FAIL — `getMyHistory`/`getCoveredHistory`/`getMyCorrections` don't accept the new arguments yet, and `response.items` is `undefined` against the old bare-array signatures.

- [ ] **Step 4: Update `TimeTrackingApiService`**

Replace:

```ts
import {
  AttendanceHistoryRow,
  AttendanceTodayResponse,
  ClockInRequest
} from '../models/time-tracking.model';
```

with:

```ts
import {
  AttendanceHistoryRow,
  AttendanceTodayResponse,
  ClockInRequest,
  PagedResultDto
} from '../models/time-tracking.model';
```

Replace:

```ts
  getMyHistory(from: string, to: string): Observable<readonly AttendanceHistoryRow[]> {
    return this.http.get<readonly AttendanceHistoryRow[]>(`${this.basePath}/history`, {
      params: this.rangeParams(from, to)
    });
  }

  getCoveredHistory(
    from: string,
    to: string,
    employeeId?: string
  ): Observable<readonly AttendanceHistoryRow[]> {
    let params = this.rangeParams(from, to);
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }

    return this.http.get<readonly AttendanceHistoryRow[]>(`${this.basePath}/covered-history`, {
      params
    });
  }
```

with:

```ts
  getMyHistory(from: string, to: string, page: number, pageSize: number): Observable<PagedResultDto<AttendanceHistoryRow>> {
    return this.http.get<PagedResultDto<AttendanceHistoryRow>>(`${this.basePath}/history`, {
      params: this.withPaging(this.rangeParams(from, to), page, pageSize)
    });
  }

  getCoveredHistory(
    from: string,
    to: string,
    page: number,
    pageSize: number,
    employeeId?: string
  ): Observable<PagedResultDto<AttendanceHistoryRow>> {
    let params = this.withPaging(this.rangeParams(from, to), page, pageSize);
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }

    return this.http.get<PagedResultDto<AttendanceHistoryRow>>(`${this.basePath}/covered-history`, {
      params
    });
  }
```

Replace:

```ts
  getMyCorrections(from?: string, to?: string): Observable<readonly AttendanceCorrectionListItem[]> {
    return this.http.get<readonly AttendanceCorrectionListItem[]>(`${this.correctionsPath}/my`, {
      params: this.optionalCorrectionParams(from, to)
    });
  }
```

with:

```ts
  getMyCorrections(page: number, pageSize: number, from?: string, to?: string): Observable<PagedResultDto<AttendanceCorrectionListItem>> {
    return this.http.get<PagedResultDto<AttendanceCorrectionListItem>>(`${this.correctionsPath}/my`, {
      params: this.withPaging(this.optionalCorrectionParams(from, to), page, pageSize)
    });
  }
```

Replace:

```ts
  private rangeParams(from: string, to: string): HttpParams {
    return new HttpParams().set('from', from).set('to', to);
  }
```

with:

```ts
  private rangeParams(from: string, to: string): HttpParams {
    return new HttpParams().set('from', from).set('to', to);
  }

  private withPaging(params: HttpParams, page: number, pageSize: number): HttpParams {
    return params.set('pageNumber', page).set('pageSize', pageSize);
  }
```

- [ ] **Step 5: Run the tests again to confirm they pass**

Run: `npx ng test --watch=false --include='**/time-tracking-api.service.spec.ts'`
Expected: PASS, all tests in the file.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/attendance/models/time-tracking.model.ts src/app/modules/attendance/data-access/time-tracking-api.service.ts src/app/modules/attendance/data-access/time-tracking-api.service.spec.ts
git commit -m "feat: paginate attendance history and corrections API calls"
```

---

### Task 8: Thread paging through `TimeTrackingStore`

**Repo:** `Hrms--Web-application---front-end---v1`

**Files:**
- Modify: `src/app/modules/attendance/state/time-tracking.store.ts`
- Modify: `src/app/modules/attendance/state/time-tracking.store.spec.ts`

**Interfaces:**
- Consumes: `TimeTrackingApiService.getMyHistory/getCoveredHistory/getMyCorrections` (new signatures) from Task 7.
- Produces new store state/signals: `historyPage`, `historyPageSize`, `historyTotalCount`; `coveredHistoryPage`, `coveredHistoryPageSize`, `coveredHistoryTotalCount`; `myCorrectionsPage`, `myCorrectionsPageSize`, `myCorrectionsTotalCount`. Produces updated method signatures: `loadMyHistory(range, page = 1)`, `loadCoveredHistory(range, employeeId?, page = 1)`, `loadMyCorrections(page = 1)` — all consumed by Task 9's component.

- [ ] **Step 1: Update the failing store test's mocks first**

In `time-tracking.store.spec.ts`, replace:

```ts
    api = {
      getToday: vi.fn(() => of(today)),
      getMyHistory: vi.fn(() => of([])),
      getCoveredHistory: vi.fn(() => of([])),
      clockIn: vi.fn(() => of(today)),
      startBreak: vi.fn(() => of(today)),
      endBreak: vi.fn(() => of(today)),
      clockOut: vi.fn(() => of(today))
    };
```

with:

```ts
    api = {
      getToday: vi.fn(() => of(today)),
      getMyHistory: vi.fn(() => of({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })),
      getCoveredHistory: vi.fn(() => of({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })),
      getMyCorrections: vi.fn(() => of({ items: [], pageNumber: 1, pageSize: 20, totalCount: 0 })),
      clockIn: vi.fn(() => of(today)),
      startBreak: vi.fn(() => of(today)),
      endBreak: vi.fn(() => of(today)),
      clockOut: vi.fn(() => of(today))
    };
```

and add `getMyCorrections: ReturnType<typeof vi.fn>;` to the `api` type declaration just above (alongside the existing `getMyHistory`/`getCoveredHistory` entries).

Add a new test at the end of the `describe` block, right after `'shows a friendly covered-history message for a 403 response'`:

```ts
  it('loadMyHistory resets to the requested page and stores the paged totals', async () => {
    api.getMyHistory.mockReturnValue(of({ items: [], pageNumber: 3, pageSize: 20, totalCount: 61 }));

    await store.loadMyHistory({ from: '2026-08-17', to: '2026-08-23' }, 3);

    expect(api.getMyHistory).toHaveBeenCalledWith('2026-08-17', '2026-08-23', 3, 20);
    expect(store.historyPage()).toBe(3);
    expect(store.historyTotalCount()).toBe(61);
  });

  it('loadMyCorrections defaults to page 1 and stores the paged totals', async () => {
    api.getMyCorrections.mockReturnValue(of({ items: [], pageNumber: 1, pageSize: 20, totalCount: 5 }));

    await store.loadMyCorrections();

    expect(api.getMyCorrections).toHaveBeenCalledWith(1, 20);
    expect(store.myCorrectionsPage()).toBe(1);
    expect(store.myCorrectionsTotalCount()).toBe(5);
  });
```

- [ ] **Step 2: Run the store tests to confirm they fail**

Run: `npx ng test --watch=false --include='**/time-tracking.store.spec.ts'`
Expected: FAIL — `store.historyPage`, `store.myCorrectionsPage`, `store.myCorrectionsTotalCount` don't exist yet, and existing tests fail because the mocked `getMyHistory`/`getCoveredHistory` now return an envelope the current store code doesn't unwrap.

- [ ] **Step 3: Add paging state**

In `time-tracking.store.ts`, add a module-level constant right after the existing top-level constants:

```ts
const DEFAULT_PAGE_SIZE = 20;
```

Add to the `TimeTrackingState` interface, after `historyError`:

```ts
  readonly historyPage: number;
  readonly historyPageSize: number;
  readonly historyTotalCount: number;
```

After `coveredHistoryError`:

```ts
  readonly coveredHistoryPage: number;
  readonly coveredHistoryPageSize: number;
  readonly coveredHistoryTotalCount: number;
```

After `myCorrectionsError`:

```ts
  readonly myCorrectionsPage: number;
  readonly myCorrectionsPageSize: number;
  readonly myCorrectionsTotalCount: number;
```

Add matching defaults to `initialState` in the same three spots:

```ts
  historyPage: 1,
  historyPageSize: DEFAULT_PAGE_SIZE,
  historyTotalCount: 0,
```

```ts
  coveredHistoryPage: 1,
  coveredHistoryPageSize: DEFAULT_PAGE_SIZE,
  coveredHistoryTotalCount: 0,
```

```ts
  myCorrectionsPage: 1,
  myCorrectionsPageSize: DEFAULT_PAGE_SIZE,
  myCorrectionsTotalCount: 0,
```

- [ ] **Step 4: Update the three loader methods**

Replace:

```ts
      async loadMyHistory(range: TimeTrackingRange): Promise<void> {
        if (store.historyLoading()) return;
        patchState(store, { historyLoading: true, historyError: null, selectedHistoryRange: range });
        try {
          const history = await firstValueFrom(api.getMyHistory(range.from, range.to));
          patchState(store, { history, historyLoading: false, historyError: null });
        } catch (error) {
          patchState(store, { historyLoading: false, historyError: errors.toSafeMessage(error, TIME_TRACKING_HISTORY_ERROR) });
        }
      },

      async loadCoveredHistory(range: TimeTrackingRange, employeeId?: string): Promise<void> {
        if (store.coveredHistoryLoading()) return;
        const selectedEmployeeId = employeeId?.trim() || null;
        patchState(store, { coveredHistoryLoading: true, coveredHistoryError: null, selectedCoveredEmployeeId: selectedEmployeeId });
        try {
          const coveredHistory = await firstValueFrom(api.getCoveredHistory(range.from, range.to, selectedEmployeeId ?? undefined));
          patchState(store, { coveredHistory, coveredHistoryLoading: false, coveredHistoryError: null });
        } catch (error) {
          const message = thisIsForbidden(error) ? TIME_TRACKING_COVERED_FORBIDDEN_ERROR : errors.toSafeMessage(error, TIME_TRACKING_COVERED_HISTORY_ERROR);
          patchState(store, { coveredHistoryLoading: false, coveredHistoryError: message });
        }
      },

      async loadMyCorrections(): Promise<void> {
        if (store.myCorrectionsLoading()) return;
        patchState(store, { myCorrectionsLoading: true, myCorrectionsError: null });
        try {
          const myCorrections = await firstValueFrom(api.getMyCorrections());
          patchState(store, { myCorrections, myCorrectionsLoading: false, myCorrectionsError: null });
        } catch (error) {
          patchState(store, { myCorrectionsLoading: false, myCorrectionsError: errors.toSafeMessage(error, CORRECTIONS_LOAD_ERROR) });
        }
      },
```

with:

```ts
      async loadMyHistory(range: TimeTrackingRange, page = 1): Promise<void> {
        if (store.historyLoading()) return;
        patchState(store, { historyLoading: true, historyError: null, selectedHistoryRange: range });
        try {
          const result = await firstValueFrom(api.getMyHistory(range.from, range.to, page, store.historyPageSize()));
          patchState(store, {
            history: result.items,
            historyPage: result.pageNumber,
            historyTotalCount: result.totalCount,
            historyLoading: false,
            historyError: null
          });
        } catch (error) {
          patchState(store, { historyLoading: false, historyError: errors.toSafeMessage(error, TIME_TRACKING_HISTORY_ERROR) });
        }
      },

      async loadCoveredHistory(range: TimeTrackingRange, employeeId?: string, page = 1): Promise<void> {
        if (store.coveredHistoryLoading()) return;
        const selectedEmployeeId = employeeId?.trim() || null;
        patchState(store, { coveredHistoryLoading: true, coveredHistoryError: null, selectedCoveredEmployeeId: selectedEmployeeId });
        try {
          const result = await firstValueFrom(api.getCoveredHistory(range.from, range.to, page, store.coveredHistoryPageSize(), selectedEmployeeId ?? undefined));
          patchState(store, {
            coveredHistory: result.items,
            coveredHistoryPage: result.pageNumber,
            coveredHistoryTotalCount: result.totalCount,
            coveredHistoryLoading: false,
            coveredHistoryError: null
          });
        } catch (error) {
          const message = thisIsForbidden(error) ? TIME_TRACKING_COVERED_FORBIDDEN_ERROR : errors.toSafeMessage(error, TIME_TRACKING_COVERED_HISTORY_ERROR);
          patchState(store, { coveredHistoryLoading: false, coveredHistoryError: message });
        }
      },

      async loadMyCorrections(page = 1): Promise<void> {
        if (store.myCorrectionsLoading()) return;
        patchState(store, { myCorrectionsLoading: true, myCorrectionsError: null });
        try {
          const result = await firstValueFrom(api.getMyCorrections(page, store.myCorrectionsPageSize()));
          patchState(store, {
            myCorrections: result.items,
            myCorrectionsPage: result.pageNumber,
            myCorrectionsTotalCount: result.totalCount,
            myCorrectionsLoading: false,
            myCorrectionsError: null
          });
        } catch (error) {
          patchState(store, { myCorrectionsLoading: false, myCorrectionsError: errors.toSafeMessage(error, CORRECTIONS_LOAD_ERROR) });
        }
      },
```

- [ ] **Step 5: Run the store tests again to confirm they pass**

Run: `npx ng test --watch=false --include='**/time-tracking.store.spec.ts'`
Expected: PASS, all tests in the file.

- [ ] **Step 6: Commit**

```bash
git add src/app/modules/attendance/state/time-tracking.store.ts src/app/modules/attendance/state/time-tracking.store.spec.ts
git commit -m "feat: track pagination state for attendance history and corrections"
```

---

### Task 9: Add Previous/Next pagination UI to the Time Tracking page

**Repo:** `Hrms--Web-application---front-end---v1`

**Files:**
- Modify: `src/app/modules/attendance/feature/time-tracking/time-tracking.component.ts`
- Modify: `src/app/modules/attendance/feature/time-tracking/time-tracking.component.html`
- Modify: `src/app/modules/attendance/feature/time-tracking/time-tracking.component.css`
- Modify: `src/app/modules/attendance/feature/time-tracking/time-tracking.component.spec.ts`

**Interfaces:**
- Consumes: `store.loadMyHistory/loadCoveredHistory/loadMyCorrections` (new `page` param) and `store.historyPage/historyPageSize/historyTotalCount` (+ covered/corrections equivalents) from Task 8.
- Produces: `goToHistoryPage(page)`, `goToCoveredHistoryPage(page)`, `goToCorrectionsPage(page)` component methods; a `Math` reference exposed on the component (mirroring `employee-list.component.ts`) for the templates to compute page counts.

- [ ] **Step 1: Extend the `StoreMock` type and `setup()` helper, then write the failing component tests**

This file's `StoreMock` type and `setup()` helper (near the top of `time-tracking.component.spec.ts`) construct the mocked `TimeTrackingStore` from plain `signal(...)` calls, e.g.:

```ts
      history: signal(history),
      historyLoading: signal(false),
      historyError: signal(null),
```

Add the nine new paging fields to the `StoreMock` type, after the matching existing entries:

```ts
  historyPage: ReturnType<typeof signal<number>>;
  historyPageSize: ReturnType<typeof signal<number>>;
  historyTotalCount: ReturnType<typeof signal<number>>;
```

```ts
  coveredHistoryPage: ReturnType<typeof signal<number>>;
  coveredHistoryPageSize: ReturnType<typeof signal<number>>;
  coveredHistoryTotalCount: ReturnType<typeof signal<number>>;
```

```ts
  myCorrectionsPage: ReturnType<typeof signal<number>>;
  myCorrectionsPageSize: ReturnType<typeof signal<number>>;
  myCorrectionsTotalCount: ReturnType<typeof signal<number>>;
```

Add matching defaults inside `setup()`'s `store` object literal, in the same three spots:

```ts
      historyPage: signal(1),
      historyPageSize: signal(20),
      historyTotalCount: signal(0),
```

```ts
      coveredHistoryPage: signal(1),
      coveredHistoryPageSize: signal(20),
      coveredHistoryTotalCount: signal(0),
```

```ts
      myCorrectionsPage: signal(1),
      myCorrectionsPageSize: signal(20),
      myCorrectionsTotalCount: signal(0),
```

Then add these three tests, near the existing `'shows only the actions enabled by the backend response flags'` test:

```ts
  it('shows the my-history Previous/Next control only when there is more than one page, and disables Previous on page 1', async () => {
    const { fixture, store } = await setup({}, false, [coveredRow]);
    store.historyTotalCount.set(41);
    fixture.detectChanges();

    const pageInfo = fixture.nativeElement.querySelector('.tt-pagination-info');
    expect(pageInfo?.textContent).toContain('Page 1 of 3');

    const buttons = fixture.nativeElement.querySelectorAll('.tt-pagination-buttons button');
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(true);
    expect((buttons[1] as HTMLButtonElement).disabled).toBe(false);
  });

  it('hides the my-history Previous/Next control when everything fits on one page', async () => {
    const { fixture, store } = await setup({}, false, [coveredRow]);
    store.historyTotalCount.set(5);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.tt-pagination-info')).toBeNull();
  });

  it('goToHistoryPage asks the store to load the requested page for the currently selected range', async () => {
    const { fixture, store } = await setup({}, false, [coveredRow]);
    const component = fixture.componentInstance;

    component.goToHistoryPage(2);

    expect(store.loadMyHistory).toHaveBeenCalledWith(expect.any(Object), 2);
  });
```

- [ ] **Step 2: Run the component tests to confirm the new ones fail**

Run: `npx ng test --watch=false --include='**/time-tracking.component.spec.ts'`
Expected: FAIL — `goToHistoryPage` doesn't exist yet, and `.tt-pagination-info` isn't in the template yet.

- [ ] **Step 3: Add the component methods and `Math` reference**

In `time-tracking.component.ts`, add near the top of the class body (alongside the other `readonly` signal declarations):

```ts
  readonly Math = Math;
```

Add these methods near `retryHistory`/`retryCoveredHistory`:

```ts
  goToHistoryPage(page: number): void {
    const range = this.store.selectedHistoryRange() ?? this.currentWeekRange;
    void this.store.loadMyHistory(range, page);
  }

  goToCoveredHistoryPage(page: number): void {
    const range = this.store.selectedHistoryRange() ?? this.currentWeekRange;
    void this.store.loadCoveredHistory(range, this.store.selectedCoveredEmployeeId() ?? undefined, page);
  }

  goToCorrectionsPage(page: number): void {
    void this.store.loadMyCorrections(page);
  }
```

- [ ] **Step 4: Add the pagination CSS**

In `time-tracking.component.css`, append a new line at the end of the file:

```css
.tt-pagination{display:flex;align-items:center;justify-content:space-between;margin-top:.875rem;font-size:.8125rem;color:var(--color-text-secondary)}.tt-pagination-info{font-weight:500}.tt-pagination-buttons{display:flex;align-items:center;gap:.5rem}
```

- [ ] **Step 5: Add the pagination block to the "My attendance history" table**

In `time-tracking.component.html`, replace (this is the end of the my-history table, immediately followed by the "My correction requests" section — see lines ~198-205 of the current file):

```html
          </tbody>
        </table>
      </div>
    }
    </section>

    <section class="history-card" aria-labelledby="my-corrections-heading">
```

with:

```html
          </tbody>
        </table>
      </div>
      @if (store.historyTotalCount() > store.historyPageSize()) {
        <div class="tt-pagination">
          <span class="tt-pagination-info">
            Page {{ store.historyPage() }} of {{ Math.ceil(store.historyTotalCount() / store.historyPageSize()) }}
          </span>
          <div class="tt-pagination-buttons">
            <app-button type="button" variant="secondary" [disabled]="store.historyPage() <= 1" (pressed)="goToHistoryPage(store.historyPage() - 1)">Previous</app-button>
            <app-button type="button" variant="secondary" [disabled]="store.historyPage() * store.historyPageSize() >= store.historyTotalCount()" (pressed)="goToHistoryPage(store.historyPage() + 1)">Next</app-button>
          </div>
        </div>
      }
    }
    </section>

    <section class="history-card" aria-labelledby="my-corrections-heading">
```

- [ ] **Step 6: Add the pagination block to the "My correction requests" table**

Replace (the end of the corrections table, immediately followed by the correction-form modal — see lines ~228-237 of the current file):

```html
          </tbody>
        </table>
      </div>
    }
    </section>

    @if (correctionFormOpen(); as selectedRow) {
```

with:

```html
          </tbody>
        </table>
      </div>
      @if (store.myCorrectionsTotalCount() > store.myCorrectionsPageSize()) {
        <div class="tt-pagination">
          <span class="tt-pagination-info">
            Page {{ store.myCorrectionsPage() }} of {{ Math.ceil(store.myCorrectionsTotalCount() / store.myCorrectionsPageSize()) }}
          </span>
          <div class="tt-pagination-buttons">
            <app-button type="button" variant="secondary" [disabled]="store.myCorrectionsPage() <= 1" (pressed)="goToCorrectionsPage(store.myCorrectionsPage() - 1)">Previous</app-button>
            <app-button type="button" variant="secondary" [disabled]="store.myCorrectionsPage() * store.myCorrectionsPageSize() >= store.myCorrectionsTotalCount()" (pressed)="goToCorrectionsPage(store.myCorrectionsPage() + 1)">Next</app-button>
          </div>
        </div>
      }
    }
    </section>

    @if (correctionFormOpen(); as selectedRow) {
```

- [ ] **Step 7: Add the pagination block to the "Team attendance" (covered history) table**

Replace (the end of the covered-history table, at the end of the file — see lines ~300-309 of the current file):

```html
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  }
</div>
```

with:

```html
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (store.coveredHistoryTotalCount() > store.coveredHistoryPageSize()) {
          <div class="tt-pagination">
            <span class="tt-pagination-info">
              Page {{ store.coveredHistoryPage() }} of {{ Math.ceil(store.coveredHistoryTotalCount() / store.coveredHistoryPageSize()) }}
            </span>
            <div class="tt-pagination-buttons">
              <app-button type="button" variant="secondary" [disabled]="store.coveredHistoryPage() <= 1" (pressed)="goToCoveredHistoryPage(store.coveredHistoryPage() - 1)">Previous</app-button>
              <app-button type="button" variant="secondary" [disabled]="store.coveredHistoryPage() * store.coveredHistoryPageSize() >= store.coveredHistoryTotalCount()" (pressed)="goToCoveredHistoryPage(store.coveredHistoryPage() + 1)">Next</app-button>
            </div>
          </div>
        }
      }
    </section>
  }
</div>
```

Note the new `@if` block sits *inside* the existing `} @else {` table branch (same indentation level as the `<div class="table-scroll">` it follows), so it only renders once the table itself is showing (not during the loading/error/empty branches above it).

- [ ] **Step 8: Run the component tests again to confirm they pass**

Run: `npx ng test --watch=false --include='**/time-tracking.component.spec.ts'`
Expected: PASS, all tests in the file (including the 3 new ones from Step 1 and every pre-existing test unaffected by this change — the 3 pre-existing MY/TEAM-toggle failures noted in the spec are unrelated and out of scope for this plan).

- [ ] **Step 9: Manual smoke check in the browser**

Run the dev server, sign in as a tenant user with more than 20 correction requests or more than 20 days of history in the selected range (seed test data if needed), open `/attendance/time-tracking`, and confirm:
- The Previous/Next control appears under a table only once its total exceeds 20 rows.
- Next/Previous swap the visible rows and update "Page X of Y".
- Previous is disabled on page 1, Next is disabled on the last page.
- Switching the date range (Apply) or the MY/TEAM tab resets back to page 1.

- [ ] **Step 10: Commit**

```bash
git add src/app/modules/attendance/feature/time-tracking/time-tracking.component.ts src/app/modules/attendance/feature/time-tracking/time-tracking.component.html src/app/modules/attendance/feature/time-tracking/time-tracking.component.css src/app/modules/attendance/feature/time-tracking/time-tracking.component.spec.ts
git commit -m "feat: add Previous/Next pagination UI to the Time Tracking page"
```
