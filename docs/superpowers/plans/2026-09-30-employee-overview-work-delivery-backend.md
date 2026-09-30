# Employee Overview — Work & Delivery (Plan 3A, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add the period helpers Plan 3 needs (`EmployeePeriod.Previous()`, `compare` parsing) and two Work Management widget endpoints: `overview/work` and `overview/delivery` (with previous-period comparison).

**Architecture:** One repository read returns the employee's tasks relevant to a period; a pure `EmployeeTaskPeriodCalculator` turns rows into stats; two thin handlers (guard → module gate → period → calculator) expose them.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md`. Builds on Plan 1 (`IEmployeeReadAccessGuard`) and Plan 2 (`EmployeePeriod`, `EmployeeOverviewAccess`, controller Theory test) — both already committed. Sibling plans: 3B (`...-activity-approvals-backend.md`), frontend 3A/3B.

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Before every commit: `git branch --show-current` and `git status --short` (shared tree); stage only the task's files. **No git worktrees.**
- Build/test per project: `dotnet test tests/ONEVO.Tests.Unit`, `dotnet test tests/ONEVO.Tests.Architecture`. Before **each** test/build run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell; the dev server respawns).
- Period semantics (single source `EmployeePeriod`): inclusive `from..to`; defaults to current month; ≤366 days. `Previous()`: a whole calendar month → the previous whole calendar month; any other range → the same-length window ending the day before `From`.
- `compare` query value: omitted/`none` → off; `previous` → on; anything else → 400.
- Work Management gate for these endpoints: `[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]` on the action (same list as `ProjectsController`; 403 when the tenant lacks WM) **and** permission `tasks:read` **or** viewing your own record (`EmployeeOverviewAccess`). Route stays `[RequirePermission("employees:read")]` + coverage guard.
- Task rules (mirror `GetMyTaskProgressQueryHandler`): completed = `MarksTaskComplete || ProgressPercent >= 100`; else overdue if `DueDate < asOf`; else in-progress if `ProgressPercent > 0`; else not-started. `asOf = min(today, period.To)`.
- A task belongs to the period if the employee is assigned AND (due date in period OR `CompletedAt` in period OR assigned (`AssignedAt`) in period). Date windows use UTC day boundaries.
- On-time = completed with both `DueDate` and `CompletedAt`, and UTC date of `CompletedAt` ≤ `DueDate`. On-time rate denominator = completed tasks that have both.
- Existing endpoints untouched. Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: `Previous()` and `compare` parsing

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeePeriod.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewCompare.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeePeriodPreviousTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeOverviewCompareTests.cs`

**Interfaces:**
- Produces: `EmployeePeriod.Previous() : EmployeePeriod`; `EmployeeOverviewCompare.Parse(string? compare) : Result<bool>` (true = compare with previous). Used by Tasks 3 and Plan 3B.

- [ ] **Step 1: Write the failing tests**

`EmployeePeriodPreviousTests.cs`:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeePeriodPreviousTests
{
    [Fact]
    public void Previous_OfAWholeMonth_IsThePreviousWholeMonth()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).Previous();

        Assert.Equal(new DateOnly(2026, 8, 1), previous.From);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.To);
    }

    [Fact]
    public void Previous_CrossesTheYearBoundary_AndHandlesShortMonths()
    {
        var january = new EmployeePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Previous();
        var march = new EmployeePeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).Previous();

        Assert.Equal(new DateOnly(2025, 12, 1), january.From);
        Assert.Equal(new DateOnly(2025, 12, 31), january.To);
        Assert.Equal(new DateOnly(2026, 2, 1), march.From);
        Assert.Equal(new DateOnly(2026, 2, 28), march.To);
    }

    [Fact]
    public void Previous_OfAnArbitraryRange_IsTheSameLengthEndingTheDayBefore()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 16)).Previous();

        Assert.Equal(new DateOnly(2026, 9, 3), previous.From);
        Assert.Equal(new DateOnly(2026, 9, 9), previous.To);
    }

    [Fact]
    public void Previous_OfAFirstToMidMonthRange_IsNotTreatedAsAWholeMonth()
    {
        var previous = new EmployeePeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15)).Previous();

        Assert.Equal(new DateOnly(2026, 8, 17), previous.From);
        Assert.Equal(new DateOnly(2026, 8, 31), previous.To);
    }
}
```

`EmployeeOverviewCompareTests.cs`:

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Helpers;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeOverviewCompareTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("NONE")]
    public void Parse_TreatsOmittedAndNoneAsOff(string? value)
    {
        var result = EmployeeOverviewCompare.Parse(value);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("Previous")]
    public void Parse_TreatsPreviousAsOn(string value)
    {
        var result = EmployeeOverviewCompare.Parse(value);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Parse_RejectsAnythingElseWith400()
    {
        var result = EmployeeOverviewCompare.Parse("last-year");

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeePeriodPreviousTests|FullyQualifiedName~EmployeeOverviewCompareTests"`
Expected: build FAIL — `Previous` / `EmployeeOverviewCompare` missing.

- [ ] **Step 3: Add `Previous()`**

Inside the `EmployeePeriod` record body (after `Resolve`), add:

```csharp
    /// <summary>The comparison window: the previous whole calendar month when this period is exactly
    /// one whole month, otherwise the same number of days ending the day before <see cref="From"/>.</summary>
    public EmployeePeriod Previous()
    {
        var isWholeMonth = From.Day == 1
            && From.Year == To.Year
            && From.Month == To.Month
            && To.Day == DateTime.DaysInMonth(To.Year, To.Month);

        if (isWholeMonth)
        {
            var first = From.AddMonths(-1);
            return new EmployeePeriod(first, new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)));
        }

        var length = To.DayNumber - From.DayNumber + 1;
        var previousTo = From.AddDays(-1);
        return new EmployeePeriod(previousTo.AddDays(-(length - 1)), previousTo);
    }
```

- [ ] **Step 4: Add `EmployeeOverviewCompare`**

`EmployeeOverviewCompare.cs`:

```csharp
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

/// <summary>Parses the shared `compare` query value of comparison-capable Overview widgets.</summary>
public static class EmployeeOverviewCompare
{
    public static Result<bool> Parse(string? compare)
    {
        if (string.IsNullOrWhiteSpace(compare) || compare.Equals("none", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Success(false);
        if (compare.Equals("previous", StringComparison.OrdinalIgnoreCase))
            return Result<bool>.Success(true);
        return Result<bool>.Failure("compare must be 'previous' or 'none'.");
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: same command as Step 2. Expected: 4 + 7 passed (the theory rows count individually).

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeePeriod.cs src/ONEVO.Application/Features/CoreHr/Employee/Helpers/EmployeeOverviewCompare.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeePeriodPreviousTests.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EmployeeOverviewCompareTests.cs
git commit -m "feat(people): add previous-period and compare helpers for overview widgets

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Task period read + `EmployeeTaskPeriodCalculator`

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/EmployeeTaskPeriodCalculator.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodRepositoryReadsTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `sealed record EmployeeTaskPeriodRow(DateOnly? DueDate, DateTimeOffset? CompletedAt, int ProgressPercent, bool MarksTaskComplete, int? StoryPoints)` (declared in `IWorkTaskRepository.cs`)
  - `IWorkTaskRepository.ListForEmployeePeriodAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default) : Task<IReadOnlyList<EmployeeTaskPeriodRow>>`
  - `sealed record TaskPeriodStats(int Assigned, int Completed, int InProgress, int Overdue, int NotStarted, int OnTimeCompleted, int CompletedWithDueDate, int StoryPointsAssigned, int StoryPointsCompleted)` and `static class EmployeeTaskPeriodCalculator { static TaskPeriodStats Compute(IReadOnlyList<EmployeeTaskPeriodRow> rows, DateOnly asOf); static int Percent(int part, int whole) }` — namespace `ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services`.
  (No other implementers/fakes of `IWorkTaskRepository` exist; only `EfWorkTaskRepository`.)

- [ ] **Step 1: Write the failing calculator test**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static EmployeeTaskPeriodRow Row(
        string? due = null, string? completedUtc = null, int progress = 0, bool done = false, int? points = null) =>
        new(due is null ? null : DateOnly.Parse(due),
            completedUtc is null ? null : DateTimeOffset.Parse(completedUtc),
            progress, done, points);

    [Fact]
    public void Compute_BucketsEveryTaskExactlyOnce()
    {
        var rows = new[]
        {
            Row(due: "2026-09-10", completedUtc: "2026-09-09T10:00:00+00:00", done: true, progress: 100), // completed on time
            Row(due: "2026-09-10", completedUtc: "2026-09-12T10:00:00+00:00", done: true, progress: 100), // completed late
            Row(progress: 100),                                                                            // 100% but no status/due
            Row(due: "2026-09-01", progress: 40),                                                          // overdue
            Row(due: "2026-09-30", progress: 40),                                                          // in progress
            Row(due: "2026-09-30")                                                                         // not started
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.Assigned.Should().Be(6);
        s.Completed.Should().Be(3);
        s.Overdue.Should().Be(1);
        s.InProgress.Should().Be(1);
        s.NotStarted.Should().Be(1);
        (s.Completed + s.Overdue + s.InProgress + s.NotStarted).Should().Be(s.Assigned);
    }

    [Fact]
    public void Compute_CountsOnTimeOnlyAmongCompletedTasksThatHaveBothDates()
    {
        var rows = new[]
        {
            Row(due: "2026-09-10", completedUtc: "2026-09-10T23:00:00+00:00", done: true),  // on the due day = on time
            Row(due: "2026-09-10", completedUtc: "2026-09-11T00:30:00+00:00", done: true),  // late
            Row(completedUtc: "2026-09-05T00:00:00+00:00", done: true),                     // no due date -> excluded
            Row(due: "2026-09-10", done: true)                                              // no CompletedAt -> excluded
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.Completed.Should().Be(4);
        s.CompletedWithDueDate.Should().Be(2);
        s.OnTimeCompleted.Should().Be(1);
    }

    [Fact]
    public void Compute_SumsStoryPoints_TreatingNullAsZero()
    {
        var rows = new[]
        {
            Row(done: true, points: 5),
            Row(points: 8),
            Row(done: true)
        };

        var s = EmployeeTaskPeriodCalculator.Compute(rows, AsOf);

        s.StoryPointsAssigned.Should().Be(13);
        s.StoryPointsCompleted.Should().Be(5);
    }

    [Fact]
    public void Compute_OfNothing_IsAllZero()
    {
        EmployeeTaskPeriodCalculator.Compute(Array.Empty<EmployeeTaskPeriodRow>(), AsOf)
            .Should().Be(new TaskPeriodStats(0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(18, 24, 75)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    public void Percent_RoundsAndTreatsAnEmptyWholeAsZero(int part, int whole, int expected)
    {
        EmployeeTaskPeriodCalculator.Percent(part, whole).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeTaskPeriodCalculatorTests"`
Expected: build FAIL — calculator and row record do not exist.

- [ ] **Step 3: Add the row record and interface member**

In `IWorkTaskRepository.cs`, after the `OpenAssignedTasksPage` record (added in Plan 1) add:

```csharp
/// <summary>The fields needed to bucket one employee task for the Overview work/delivery cards.</summary>
public sealed record EmployeeTaskPeriodRow(
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    int ProgressPercent,
    bool MarksTaskComplete,
    int? StoryPoints);
```

and inside the interface, after `ListOpenAssignedToEmployeeAsync`:

```csharp
    /// <summary>Tasks assigned to this employee that belong to from..to: due date in the range, OR
    /// CompletedAt in the range, OR the employee was assigned within the range (UTC day bounds).</summary>
    Task<IReadOnlyList<EmployeeTaskPeriodRow>> ListForEmployeePeriodAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
```

- [ ] **Step 4: Write the calculator**

`EmployeeTaskPeriodCalculator.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;

public sealed record TaskPeriodStats(
    int Assigned,
    int Completed,
    int InProgress,
    int Overdue,
    int NotStarted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted);

/// <summary>
/// Buckets an employee's period tasks with the same rules as GetMyTaskProgressQueryHandler
/// (completed = status marks complete OR progress >= 100), plus on-time and story-point totals.
/// </summary>
public static class EmployeeTaskPeriodCalculator
{
    public static TaskPeriodStats Compute(IReadOnlyList<EmployeeTaskPeriodRow> rows, DateOnly asOf)
    {
        int completed = 0, inProgress = 0, overdue = 0, notStarted = 0;
        int onTime = 0, withDue = 0, pointsAssigned = 0, pointsCompleted = 0;

        foreach (var row in rows)
        {
            var points = row.StoryPoints ?? 0;
            pointsAssigned += points;

            if (row.MarksTaskComplete || row.ProgressPercent >= 100)
            {
                completed++;
                pointsCompleted += points;
                if (row.DueDate is { } due && row.CompletedAt is { } completedAt)
                {
                    withDue++;
                    if (DateOnly.FromDateTime(completedAt.UtcDateTime) <= due)
                        onTime++;
                }
            }
            else if (row.DueDate is { } dueDate && dueDate < asOf)
                overdue++;
            else if (row.ProgressPercent > 0)
                inProgress++;
            else
                notStarted++;
        }

        return new TaskPeriodStats(
            rows.Count, completed, inProgress, overdue, notStarted, onTime, withDue, pointsAssigned, pointsCompleted);
    }

    /// <summary>Rounded whole percentage; 0 when the whole is 0.</summary>
    public static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 5: Run the calculator tests**

Run: same command as Step 2. Expected: 8 passed (4 facts + 4 theory rows).

- [ ] **Step 6: Write the failing repository test**

`EmployeeTaskPeriodRepositoryReadsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeTaskPeriodRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    [Fact]
    public async Task ListForEmployeePeriod_IncludesTasksDueCompletedOrAssignedInThePeriod_AndNothingElse()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var status = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "s", Category = "active", MarksTaskComplete = false };
        var done = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "d", Category = "done", MarksTaskComplete = true };
        db.TaskStatuses.AddRange(status, done);

        var dueInPeriod = Task(projectId, status.Id, "DUE", dueDate: new DateOnly(2026, 9, 10));
        var completedInPeriod = Task(projectId, done.Id, "DONE", completedAt: DateTimeOffset.Parse("2026-09-05T10:00:00+00:00"));
        var assignedInPeriod = Task(projectId, status.Id, "ASSIGNED");
        var outsideEverything = Task(projectId, status.Id, "OLD", dueDate: new DateOnly(2026, 7, 1));
        var otherEmployees = Task(projectId, status.Id, "OTHER", dueDate: new DateOnly(2026, 9, 10));
        db.WorkTasks.AddRange(dueInPeriod, completedInPeriod, assignedInPeriod, outsideEverything, otherEmployees);

        db.TaskAssignments.AddRange(
            Assign(dueInPeriod.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(completedInPeriod.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(assignedInPeriod.Id, _employeeId, "2026-09-03T00:00:00+00:00"),
            Assign(outsideEverything.Id, _employeeId, "2026-06-01T00:00:00+00:00"),
            Assign(otherEmployees.Id, _otherEmployeeId, "2026-09-03T00:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListForEmployeePeriodAsync(_tenantId, _employeeId, From, To);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.DueDate == new DateOnly(2026, 9, 10));
        Assert.Contains(rows, r => r.MarksTaskComplete && r.CompletedAt is not null);
        Assert.Contains(rows, r => r.DueDate is null && !r.MarksTaskComplete);
    }

    [Fact]
    public async Task ListForEmployeePeriod_CarriesStoryPointsAndProgress()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var status = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, Name = "s", Category = "active" };
        db.TaskStatuses.Add(status);
        var task = Task(projectId, status.Id, "P", dueDate: new DateOnly(2026, 9, 10), points: 5, progress: 40);
        db.WorkTasks.Add(task);
        db.TaskAssignments.Add(Assign(task.Id, _employeeId, "2026-06-01T00:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = Assert.Single(await new EfWorkTaskRepository(db).ListForEmployeePeriodAsync(_tenantId, _employeeId, From, To));

        Assert.Equal(5, row.StoryPoints);
        Assert.Equal(40, row.ProgressPercent);
    }

    private WorkTask Task(Guid projectId, Guid statusId, string shortId, DateOnly? dueDate = null,
        DateTimeOffset? completedAt = null, int? points = null, int progress = 0) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId, ObjectiveId = Guid.NewGuid(),
            StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId,
            DueDate = dueDate, CompletedAt = completedAt, StoryPoints = points, ProgressPercent = progress
        };

    private static TaskAssignment Assign(Guid taskId, Guid employeeId, string assignedAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = employeeId,
            AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.Parse(assignedAtUtc)
        };

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

- [ ] **Step 7: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeTaskPeriodRepositoryReadsTests"`
Expected: build FAIL — `ListForEmployeePeriodAsync` does not exist on `EfWorkTaskRepository`.

- [ ] **Step 8: Implement in `EfWorkTaskRepository`**

Inside the class (`TaskStatuses`, `TaskAssignments` are already used there):

```csharp
    public async Task<IReadOnlyList<EmployeeTaskPeriodRow>> ListForEmployeePeriodAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toUtcExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
                  && ((t.DueDate != null && t.DueDate >= from && t.DueDate <= to)
                      || (t.CompletedAt != null && t.CompletedAt >= fromUtc && t.CompletedAt < toUtcExclusive)
                      || _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId
                                                      && a.AssignedAt >= fromUtc && a.AssignedAt < toUtcExclusive))
            select new EmployeeTaskPeriodRow(t.DueDate, t.CompletedAt, t.ProgressPercent, s.MarksTaskComplete, t.StoryPoints)
        ).ToListAsync(ct);
    }
```

- [ ] **Step 9: Run repository + calculator tests and the layer check**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeTaskPeriod"
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LayerDependencyTests"
```
Expected: all pass.

- [ ] **Step 10: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs src/ONEVO.Application/Features/WorkManagement/EmployeeOverview tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodRepositoryReadsTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeTaskPeriodCalculatorTests.cs
git commit -m "feat(work): add employee task period read and stats calculator

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `overview/work` and `overview/delivery` endpoints

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/DTOs/EmployeeWorkOverviewResponses.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/GetEmployeeWorkOverview/GetEmployeeWorkOverviewQuery.cs` and `...QueryHandler.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/GetEmployeeDelivery/GetEmployeeDeliveryQuery.cs` and `...QueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkOverviewQueryHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeDeliveryQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (two `[InlineData]` rows)

**Interfaces:**
- Consumes: Tasks 1-2; `IEmployeeReadAccessGuard`, `EmployeePeriod.Resolve`, `EmployeeOverviewAccess.HasAccessAsync`.
- Produces:
  - `EmployeeWorkOverviewResponse(DateOnly From, DateOnly To, int Assigned, int Completed, int InProgress, int Overdue, int NotStarted, int CompletionRatePercent, int? OnTimeRatePercent)`
  - `EmployeeDeliveryMetrics(int TasksAssigned, int TasksCompleted, int OnTimeCompleted, int CompletedWithDueDate, int StoryPointsAssigned, int StoryPointsCompleted)`
  - `EmployeeDeliveryResponse(DateOnly From, DateOnly To, int TasksAssigned, int TasksCompleted, int OnTimeCompleted, int CompletedWithDueDate, int StoryPointsAssigned, int StoryPointsCompleted, EmployeeDeliveryMetrics? Previous)`
  - `GetEmployeeWorkOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)`; `GetEmployeeDeliveryQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)`
  - `GET …/overview/work?from&to`, `GET …/overview/delivery?from&to&compare=previous`

- [ ] **Step 1: Write the failing work-handler test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkOverviewQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeWorkOverviewQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeWorkOverviewQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

    private void ArrangeRows(params EmployeeTaskPeriodRow[] rows) =>
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static EmployeeTaskPeriodRow Row(string? due = null, string? completed = null, int progress = 0, bool done = false) =>
        new(due is null ? null : DateOnly.Parse(due), completed is null ? null : DateTimeOffset.Parse(completed), progress, done, null);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksTasksReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ComputesCountsAndRates_AsOfTheEarlierOfTodayAndPeriodEnd()
    {
        ArrangeRows(
            Row(due: "2026-09-10", completed: "2026-09-09T10:00:00+00:00", done: true),   // completed, on time
            Row(due: "2026-09-10", completed: "2026-09-12T10:00:00+00:00", done: true),   // completed, late
            Row(due: "2026-09-01", progress: 30),                                          // overdue (before today 15th)
            Row(due: "2026-09-20", progress: 30),                                          // in progress (after today)
            Row(due: "2026-09-25"));                                                       // not started

        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        var v = result.Value!;
        v.Assigned.Should().Be(5);
        v.Completed.Should().Be(2);
        v.Overdue.Should().Be(1);
        v.InProgress.Should().Be(1);
        v.NotStarted.Should().Be(1);
        v.CompletionRatePercent.Should().Be(40);
        v.OnTimeRatePercent.Should().Be(50);
    }

    [Fact]
    public async Task Handle_UsesThePeriodEndAsTheReference_ForAPastPeriod()
    {
        ArrangeRows(Row(due: "2026-08-20", progress: 10));   // not done by 31 Aug -> overdue in August

        var result = await CreateHandler().Handle(
            new GetEmployeeWorkOverviewQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        result.Value!.Overdue.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OnTimeRateIsNull_WhenNoCompletedTaskHasBothDates()
    {
        ArrangeRows(Row(progress: 10));

        var result = await CreateHandler().Handle(new GetEmployeeWorkOverviewQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.OnTimeRatePercent.Should().BeNull();
        result.Value.CompletionRatePercent.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkOverviewQueryHandlerTests"`
Expected: build FAIL — handler/query/response missing.

- [ ] **Step 3: Write DTOs, query and work handler**

`DTOs/EmployeeWorkOverviewResponses.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

public sealed record EmployeeWorkOverviewResponse(
    DateOnly From,
    DateOnly To,
    int Assigned,
    int Completed,
    int InProgress,
    int Overdue,
    int NotStarted,
    int CompletionRatePercent,
    int? OnTimeRatePercent);

public sealed record EmployeeDeliveryMetrics(
    int TasksAssigned,
    int TasksCompleted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted);

/// <summary>Flat current-period metrics plus, when compare=previous, the same metrics for the
/// previous period.</summary>
public sealed record EmployeeDeliveryResponse(
    DateOnly From,
    DateOnly To,
    int TasksAssigned,
    int TasksCompleted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted,
    EmployeeDeliveryMetrics? Previous);
```

`Queries/GetEmployeeWorkOverview/GetEmployeeWorkOverviewQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;

public sealed record GetEmployeeWorkOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeWorkOverviewResponse>>;
```

`Queries/GetEmployeeWorkOverview/GetEmployeeWorkOverviewQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;

public sealed class GetEmployeeWorkOverviewQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeWorkOverviewQuery, Result<EmployeeWorkOverviewResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeWorkOverviewResponse>> Handle(GetEmployeeWorkOverviewQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeWorkOverviewResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeWorkOverviewResponse>.Forbidden("You do not have access to this employee's work.");

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeWorkOverviewResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var rows = await tasks.ListForEmployeePeriodAsync(tenantId, request.EmployeeId, period.Value!.From, period.Value.To, ct);
        var asOf = period.Value.To < clock.Today ? period.Value.To : clock.Today;
        var stats = EmployeeTaskPeriodCalculator.Compute(rows, asOf);

        int? onTimeRate = stats.CompletedWithDueDate == 0
            ? null
            : EmployeeTaskPeriodCalculator.Percent(stats.OnTimeCompleted, stats.CompletedWithDueDate);

        return Result<EmployeeWorkOverviewResponse>.Success(new EmployeeWorkOverviewResponse(
            period.Value.From, period.Value.To,
            stats.Assigned, stats.Completed, stats.InProgress, stats.Overdue, stats.NotStarted,
            EmployeeTaskPeriodCalculator.Percent(stats.Completed, stats.Assigned),
            onTimeRate));
    }
}
```

- [ ] **Step 4: Run the work-handler tests**

Run: same command as Step 2. Expected: 6 passed.

- [ ] **Step 5: Write the failing delivery-handler test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeDeliveryQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly SepFrom = new(2026, 9, 1);
    private static readonly DateOnly SepTo = new(2026, 9, 30);
    private static readonly DateOnly AugFrom = new(2026, 8, 1);
    private static readonly DateOnly AugTo = new(2026, 8, 31);

    public GetEmployeeDeliveryQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
    }

    private GetEmployeeDeliveryQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

    private void ArrangeRows(DateOnly from, DateOnly to, params EmployeeTaskPeriodRow[] rows) =>
        _tasks.Setup(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    private static EmployeeTaskPeriodRow Done(string due, string completed, int? points = null) =>
        new(DateOnly.Parse(due), DateTimeOffset.Parse(completed), 100, true, points);

    [Fact]
    public async Task Handle_Forbidden_WhenCallerLacksTasksReadAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ReturnsTheCurrentPeriodMetrics_AndNoPreviousByDefault()
    {
        ArrangeRows(SepFrom, SepTo,
            Done("2026-09-10", "2026-09-09T10:00:00+00:00", 5),
            Done("2026-09-10", "2026-09-12T10:00:00+00:00", 3),
            new EmployeeTaskPeriodRow(new DateOnly(2026, 9, 25), null, 0, false, 8));

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, SepFrom, SepTo), CancellationToken.None);

        var v = result.Value!;
        v.TasksAssigned.Should().Be(3);
        v.TasksCompleted.Should().Be(2);
        v.OnTimeCompleted.Should().Be(1);
        v.CompletedWithDueDate.Should().Be(2);
        v.StoryPointsAssigned.Should().Be(16);
        v.StoryPointsCompleted.Should().Be(8);
        v.Previous.Should().BeNull();
        _tasks.Verify(t => t.ListForEmployeePeriodAsync(_tenantId, _employeeId, AugFrom, AugTo, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithComparePrevious_AddsThePreviousMonthsMetrics()
    {
        ArrangeRows(SepFrom, SepTo, Done("2026-09-10", "2026-09-09T10:00:00+00:00", 5));
        ArrangeRows(AugFrom, AugTo,
            Done("2026-08-10", "2026-08-09T10:00:00+00:00", 2),
            Done("2026-08-12", "2026-08-11T10:00:00+00:00", 2));

        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, SepFrom, SepTo, "previous"), CancellationToken.None);

        result.Value!.TasksCompleted.Should().Be(1);
        result.Value.Previous.Should().NotBeNull();
        result.Value.Previous!.TasksCompleted.Should().Be(2);
        result.Value.Previous.StoryPointsCompleted.Should().Be(4);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnUnknownCompareValue()
    {
        var result = await CreateHandler().Handle(new GetEmployeeDeliveryQuery(_employeeId, null, null, "last-year"), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeDeliveryQueryHandlerTests"`
Expected: build FAIL — delivery query/handler missing.

- [ ] **Step 7: Write the delivery query and handler**

`Queries/GetEmployeeDelivery/GetEmployeeDeliveryQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;

public sealed record GetEmployeeDeliveryQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeDeliveryResponse>>;
```

`Queries/GetEmployeeDelivery/GetEmployeeDeliveryQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;

public sealed class GetEmployeeDeliveryQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeDeliveryQuery, Result<EmployeeDeliveryResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeDeliveryResponse>> Handle(GetEmployeeDeliveryQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeDeliveryResponse>.Forbidden("You do not have access to this employee's work.");

        var compare = EmployeeOverviewCompare.Parse(request.Compare);
        if (!compare.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(compare.Error!, compare.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeDeliveryResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var current = await MeasureAsync(tenantId, request.EmployeeId, period.Value!, ct);
        EmployeeDeliveryMetrics? previous = compare.Value
            ? await MeasureAsync(tenantId, request.EmployeeId, period.Value!.Previous(), ct)
            : null;

        return Result<EmployeeDeliveryResponse>.Success(new EmployeeDeliveryResponse(
            period.Value!.From, period.Value.To,
            current.TasksAssigned, current.TasksCompleted, current.OnTimeCompleted, current.CompletedWithDueDate,
            current.StoryPointsAssigned, current.StoryPointsCompleted, previous));
    }

    private async Task<EmployeeDeliveryMetrics> MeasureAsync(
        Guid tenantId, Guid employeeId, EmployeePeriod period, CancellationToken ct)
    {
        var rows = await tasks.ListForEmployeePeriodAsync(tenantId, employeeId, period.From, period.To, ct);
        var asOf = period.To < clock.Today ? period.To : clock.Today;
        var s = EmployeeTaskPeriodCalculator.Compute(rows, asOf);
        return new EmployeeDeliveryMetrics(
            s.Assigned, s.Completed, s.OnTimeCompleted, s.CompletedWithDueDate, s.StoryPointsAssigned, s.StoryPointsCompleted);
    }
}
```

- [ ] **Step 8: Run the delivery tests**

Run: same command as Step 6. Expected: 4 passed.

- [ ] **Step 9: Extend the architecture theory, watch it fail, add the controller actions**

In `EmployeesControllerArchitectureTests.cs`, add to the existing `[Theory]` (next to the other `[InlineData("overview/...")]` rows):

```csharp
    [InlineData("overview/work", "GetOverviewWork")]
    [InlineData("overview/delivery", "GetOverviewDelivery")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the two new rows.

In `EmployeesController.cs` add usings:

```csharp
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
```

and after `GetOverviewTimeOff`:

```csharp
    /// <summary>Overview work card: assigned/completed/in-progress/overdue task counts and
    /// completion + on-time rates for one employee over from..to (default: current month).</summary>
    [HttpGet("{id:guid}/overview/work")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetOverviewWork(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkOverviewQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview delivery card: tasks completed, on-time completion and story points; with
    /// compare=previous also the previous period's figures.</summary>
    [HttpGet("{id:guid}/overview/delivery")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetOverviewDelivery(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDeliveryQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

(`RequireAnyModule` lives in `ONEVO.Api.Filters`, already imported by this controller.)

- [ ] **Step 10: Run both suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

- [ ] **Step 11: Live check**

Start the API; as a user with `employees:read` + `tasks:read` in a tenant with Work Management: `GET /api/v1/employees/{id}/overview/work` → 200, counts sum consistently (`completed + inProgress + overdue + notStarted == assigned`); `…/overview/delivery?compare=previous` → `previous` present; `?compare=x` → 400; as a caller lacking `tasks:read` on someone else → 403; in a tenant without WM modules → 403. Report what was and was not exercised (an employee with no tasks legitimately returns zeros).

- [ ] **Step 12: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement/EmployeeOverview src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkOverviewQueryHandlerTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeDeliveryQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview work and delivery endpoints

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- Spec/user coverage: Work Management card (assigned/completed/in progress/overdue, completion + on-time rates) and Delivery (tasks, on-time, story points) with previous-period comparison. The Work Insight Index stays deferred (formula undefined).
- Story points exist on `WorkTask.StoryPoints` (verified), so Delivery's story-points row is real.
- Type consistency: `EmployeeTaskPeriodRow` (T2) → calculator (T2) → both handlers (T3); `EmployeePeriod.Previous`/`EmployeeOverviewCompare.Parse` (T1) → delivery handler; response field order in `EmployeeDeliveryResponse` matches the handler's constructor call.
- Verified by reading source: `GetMyTaskProgressQueryHandler` bucketing, `EfWorkTaskRepository` query style, `TaskAssignment.AssignedAt`, `WorkTask.CompletedAt` set only by `MoveTaskStatus` (so tasks that hit 100% without a status move have no `CompletedAt` and are excluded from the on-time denominator — intentional), `RequireAnyModuleAttribute` allows methods. Not compile-checked: none of this plan's code has been built; the executor's first `dotnet test` is the first compile.
- Known limits: a task completed via 100% progress only (no status move) counts as completed but not toward on-time; tasks assigned before the period whose due date and completion fall outside it are excluded by design.
- Not here: activity, discipline comparison, approvals (Plan 3B backend).
