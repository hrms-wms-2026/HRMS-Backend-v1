# Employee Work & Activity — Task Cards (Plan 4A, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the three task endpoints the Work & Activity tab needs beyond Plan 3A: `work-activity/needs-attention`, `work-activity/recent-tasks` and `work-activity/delivery-trend`.

**Architecture:** Three repository reads on `IWorkTaskRepository`, which return narrow projected rows joined with status and project. They feed three thin query handlers: guard → Work Management gate → shape. The Work summary and Delivery health *numbers* are **not** rebuilt here. They come from Plan 3A's `overview/work` and `overview/delivery`, so the Overview and Work & Activity tabs can never disagree.

**Tech Stack:** ASP.NET Core, MediatR, EF Core (PostgreSQL; InMemory in unit tests), xUnit, Moq, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md` plus the Work & Activity mockup (product owner, 2026-09-30). Sibling plans: 4B (`2026-09-30-employee-work-activity-monitoring-backend.md`), frontend `Hrms--Web-application---front-end---v1/docs/superpowers/plans/next/2026-09-30-employee-work-activity-tab.md`.

## Prerequisites

- **Plan 3A** (`../2026-09-30-employee-overview-work-delivery-backend.md`) Tasks 1–3 are merged: `overview/work` and `overview/delivery` exist. This plan reuses 3A's completion rule.
- Plan 2 is merged (`EmployeePeriod`, `EmployeeOverviewAccess`, the `EmployeesControllerArchitectureTests` Theory).

## Global Constraints

- Work in a clean checkout of `HRMS-Backend-v1` on a new branch `feature/employee-work-activity` cut from `origin/development` after 3A has merged. Do **not** use `C:\HR2\HRMS-Backend-v1` as it is; it is on `feature/attendance-idle-activity-detail` with unrelated uncommitted monitoring changes. Before each commit, run `git branch --show-current` and `git status --short`, and stage only the task's files.
- Before **each** `dotnet test`: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell; the dev server respawns and locks DLLs).
- Task rules are copied verbatim from 3A:
  - **Completed** = `MarksTaskComplete || ProgressPercent >= 100`.
  - Otherwise the task is **overdue** if `DueDate < asOf`.
  - Dates are UTC days; `asOf` = `clock.Today`.
- **Needs attention** looks at *now* and is not period-scoped:
  - Candidates: assigned tasks that are not completed, have a due date, and `DueDate <= asOf + 3 days`.
  - `reason = "overdue"` when `DueDate < asOf`, else `"due_soon"`.
  - Order: overdue tasks first, oldest due date first, then due-soon tasks by due date.
  - Return the top **5** plus `totalCount`.
- **Recent tasks** is also not period-scoped: the 5 assigned tasks most recently changed, ordered by `UpdatedAt ?? CreatedAt` descending.
- **Delivery trend** returns 6 calendar months ending at the month of `to` (default today). Each month counts completed assigned tasks whose `CompletedAt` falls in that UTC month. Tasks with no `CompletedAt` cannot be placed in a month and are not counted.
- The "Review" pill in the mockup is only a status column name. Each row carries `statusName` and `statusColor` from `TaskStatus`; no approval-derived bucket is built.
- **Gate:** every action gets `[RequirePermission("employees:read")]` plus `[RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]`. Every handler runs `IEmployeeReadAccessGuard.EnsureCanRead`, then `EmployeeOverviewAccess.HasAccessAsync(..., "tasks:read", ...)`: `tasks:read` or viewing your own record.
- Existing endpoints and repository methods stay unchanged.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Structure

| File | Responsibility |
|---|---|
| `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs` (modify) | `EmployeeWorkTaskRow` record + 3 read methods |
| `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs` (modify) | EF implementations (projected, `AsNoTracking`) |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/DTOs/EmployeeWorkActivityTaskResponses.cs` (create) | Response DTOs for the 3 endpoints |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/WorkActivityTaskRules.cs` (create) | Pure rules: completion, attention reason/order, month buckets |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/WorkActivity/GetEmployeeNeedsAttention/*` (create) | Query + handler |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/WorkActivity/GetEmployeeRecentTasks/*` (create) | Query + handler |
| `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/WorkActivity/GetEmployeeDeliveryTrend/*` (create) | Query + handler |
| `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (modify) | 3 actions |
| `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkActivityTaskRepositoryReadsTests.cs` (create) | Repository reads |
| `tests/ONEVO.Tests.Unit/Features/WorkManagement/WorkActivityTaskRulesTests.cs` (create) | Pure rules |
| `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkActivityTaskHandlersTests.cs` (create) | The 3 handlers |
| `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (modify) | Route + permission rows |

---

### Task 1: Repository reads

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkActivityTaskRepositoryReadsTests.cs`

**Produces:** `EmployeeWorkTaskRow`, `ListOpenDueByAsync`, `ListRecentlyChangedAssignedAsync`, `ListCompletedAtForEmployeeAsync` (used by Tasks 3–5).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EmployeeWorkActivityTaskRepositoryReadsTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task ListOpenDueBy_ReturnsOnlyOpenAssignedTasksDueOnOrBeforeTheCutoff_WithProjectAndStatus()
    {
        await using var db = BuildInMemoryDb();
        var (open, done) = SeedStatuses(db);
        var due = NewTask(open.Id, "WEB-1", due: new DateOnly(2026, 9, 10));
        var tooLate = NewTask(open.Id, "WEB-2", due: new DateOnly(2026, 9, 20));
        var noDue = NewTask(open.Id, "WEB-3", due: null);
        var completedByStatus = NewTask(done.Id, "WEB-4", due: new DateOnly(2026, 9, 1));
        var completedByProgress = NewTask(open.Id, "WEB-5", due: new DateOnly(2026, 9, 1), progress: 100);
        var someoneElses = NewTask(open.Id, "WEB-6", due: new DateOnly(2026, 9, 1));
        db.WorkTasks.AddRange(due, tooLate, noDue, completedByStatus, completedByProgress, someoneElses);
        db.TaskAssignments.AddRange(Assign(due.Id), Assign(tooLate.Id), Assign(noDue.Id), Assign(completedByStatus.Id),
            Assign(completedByProgress.Id), Assign(someoneElses.Id, _otherEmployeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListOpenDueByAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 15));

        var row = Assert.Single(rows);
        Assert.Equal("WEB-1", row.ShortId);
        Assert.Equal("Website", row.ProjectName);
        Assert.Equal("In Progress", row.StatusName);
        Assert.Equal("#2563EB", row.StatusColor);
    }

    [Fact]
    public async Task ListRecentlyChangedAssigned_OrdersByUpdatedThenCreated_AndTakes()
    {
        await using var db = BuildInMemoryDb();
        var (open, _) = SeedStatuses(db);
        var old = NewTask(open.Id, "WEB-1", created: "2026-09-01T00:00:00+00:00");
        var edited = NewTask(open.Id, "WEB-2", created: "2026-08-01T00:00:00+00:00", updated: "2026-09-20T00:00:00+00:00");
        var fresh = NewTask(open.Id, "WEB-3", created: "2026-09-10T00:00:00+00:00");
        db.WorkTasks.AddRange(old, edited, fresh);
        db.TaskAssignments.AddRange(Assign(old.Id), Assign(edited.Id), Assign(fresh.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListRecentlyChangedAssignedAsync(_tenantId, _employeeId, take: 2);

        Assert.Equal(new[] { "WEB-2", "WEB-3" }, rows.Select(r => r.ShortId));
    }

    [Fact]
    public async Task ListCompletedAt_ReturnsCompletionInstantsOfCompletedAssignedTasksInTheWindow()
    {
        await using var db = BuildInMemoryDb();
        var (open, done) = SeedStatuses(db);
        var inWindow = NewTask(done.Id, "WEB-1", completedAt: "2026-09-05T10:00:00+00:00");
        var byProgress = NewTask(open.Id, "WEB-2", progress: 100, completedAt: "2026-09-06T10:00:00+00:00");
        var outside = NewTask(done.Id, "WEB-3", completedAt: "2026-08-31T23:59:00+00:00");
        var notCompleted = NewTask(open.Id, "WEB-4", completedAt: "2026-09-07T10:00:00+00:00");
        db.WorkTasks.AddRange(inWindow, byProgress, outside, notCompleted);
        db.TaskAssignments.AddRange(Assign(inWindow.Id), Assign(byProgress.Id), Assign(outside.Id), Assign(notCompleted.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfWorkTaskRepository(db).ListCompletedAtForEmployeeAsync(
            _tenantId, _employeeId,
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"));

        Assert.Equal(2, rows.Count);
    }

    private (WmTaskStatus Open, WmTaskStatus Done) SeedStatuses(ApplicationDbContext db)
    {
        db.Projects.Add(new Project { Id = _projectId, TenantId = _tenantId, Name = "Website", Identifier = "WEB", IsActive = true });
        var open = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Name = "In Progress", Category = "active", Color = "#2563EB" };
        var done = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Name = "Done", Category = "done", MarksTaskComplete = true };
        db.TaskStatuses.AddRange(open, done);
        return (open, done);
    }

    private WorkTask NewTask(
        Guid statusId, string shortId, DateOnly? due = null, int progress = 0,
        string? created = null, string? updated = null, string? completedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = Guid.NewGuid(),
        StatusId = statusId, CategoryId = Guid.NewGuid(), ShortId = shortId, Title = shortId,
        DueDate = due, ProgressPercent = progress,
        CreatedAt = created is null ? DateTimeOffset.Parse("2026-09-01T00:00:00+00:00") : DateTimeOffset.Parse(created),
        UpdatedAt = updated is null ? null : DateTimeOffset.Parse(updated),
        CompletedAt = completedAt is null ? null : DateTimeOffset.Parse(completedAt)
    };

    private TaskAssignment Assign(Guid taskId, Guid? employeeId = null) =>
        new() { Id = Guid.NewGuid(), TaskId = taskId, UserId = Guid.NewGuid(), EmployeeId = employeeId ?? _employeeId, AssignedById = Guid.NewGuid() };

    // Copy BuildInMemoryDb() verbatim from EmployeeWorkGraphRepositoryReadsTests (same folder):
    // it builds ApplicationDbContext with the InMemory provider and mocked interceptors/tenant context.
    private static ApplicationDbContext BuildInMemoryDb() => EmployeeWorkGraphRepositoryReadsTestsDb.Build();
}
```

Also extract `BuildInMemoryDb()` so both test classes share it. Move the body of `EmployeeWorkGraphRepositoryReadsTests.BuildInMemoryDb()` into a new file, `tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkGraphRepositoryReadsTestsDb.cs`, as `internal static class EmployeeWorkGraphRepositoryReadsTestsDb { public static ApplicationDbContext Build() { /* moved body */ } }`. Then make the old method `=> EmployeeWorkGraphRepositoryReadsTestsDb.Build();`.

> Note: the InMemory context may `SaveChanges` through `AuditableEntityInterceptor`, which can overwrite `CreatedAt`/`UpdatedAt`. If `ListRecentlyChangedAssigned` fails only on ordering, set the timestamps after the first save with `db.Entry(x).Property(p => p.UpdatedAt).CurrentValue = ...`, save again, then clear the tracker.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityTaskRepositoryReadsTests"`
Expected: build FAIL, because `ListOpenDueByAsync` and the other methods do not exist.

- [ ] **Step 3: Add the record and interface members**

In `IWorkTaskRepository.cs`, after the `TaskProgressRow` record, add:

```csharp
/// <summary>An employee's assigned task with its project and status, for the Work & Activity
/// Needs attention and Recent tasks cards.</summary>
public sealed record EmployeeWorkTaskRow(
    Guid Id,
    string ShortId,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string StatusName,
    string StatusColor,
    bool MarksTaskComplete,
    string Priority,
    int? StoryPoints,
    DateOnly? DueDate,
    int ProgressPercent,
    DateTimeOffset LastChangedAt);
```

Inside `IWorkTaskRepository`, after `ListOpenAssignedToEmployeeAsync`, add:

```csharp
    /// <summary>Assigned, not-completed (status does not mark complete and progress below 100)
    /// tasks with a due date on or before dueOnOrBefore - every overdue task plus the upcoming window.</summary>
    Task<IReadOnlyList<EmployeeWorkTaskRow>> ListOpenDueByAsync(Guid tenantId, Guid employeeId, DateOnly dueOnOrBefore, CancellationToken ct = default);

    /// <summary>The employee's assigned tasks ordered by UpdatedAt ?? CreatedAt descending, capped at take.</summary>
    Task<IReadOnlyList<EmployeeWorkTaskRow>> ListRecentlyChangedAssignedAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default);

    /// <summary>CompletedAt of every completed (status marks complete or progress 100) assigned task
    /// whose CompletedAt is in [fromUtc, toUtcExclusive).</summary>
    Task<IReadOnlyList<DateTimeOffset>> ListCompletedAtForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfWorkTaskRepository`** (after `ListOpenAssignedToEmployeeAsync`)

```csharp
    public async Task<IReadOnlyList<EmployeeWorkTaskRow>> ListOpenDueByAsync(Guid tenantId, Guid employeeId, DateOnly dueOnOrBefore, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && t.DueDate.HasValue
                  && t.DueDate <= dueOnOrBefore
                  && !s.MarksTaskComplete
                  && t.ProgressPercent < 100
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby t.DueDate
            select new EmployeeWorkTaskRow(
                t.Id, t.ShortId, t.Title, t.ProjectId, p.Name, s.Name, s.Color, s.MarksTaskComplete,
                t.Priority, t.StoryPoints, t.DueDate, t.ProgressPercent, t.UpdatedAt ?? t.CreatedAt)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeWorkTaskRow>> ListRecentlyChangedAssignedAsync(Guid tenantId, Guid employeeId, int take, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby (t.UpdatedAt ?? t.CreatedAt) descending
            select new EmployeeWorkTaskRow(
                t.Id, t.ShortId, t.Title, t.ProjectId, p.Name, s.Name, s.Color, s.MarksTaskComplete,
                t.Priority, t.StoryPoints, t.DueDate, t.ProgressPercent, t.UpdatedAt ?? t.CreatedAt)
        ).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ListCompletedAtForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            where t.TenantId == tenantId
                  && t.CompletedAt.HasValue
                  && t.CompletedAt >= fromUtc
                  && t.CompletedAt < toUtcExclusive
                  && (s.MarksTaskComplete || t.ProgressPercent >= 100)
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            select t.CompletedAt!.Value
        ).ToListAsync(ct);
    }
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EmployeeWorkActivityTaskRepositoryReadsTests|FullyQualifiedName~EmployeeWorkGraphRepositoryReadsTests"`
Expected: PASS (the work-graph tests still pass after the `BuildInMemoryDb` extraction).

- [ ] **Step 5a: Check that PostgreSQL translates the queries**

InMemory evaluates LINQ in memory, so it cannot catch Npgsql translation failures. The `orderby (t.UpdatedAt ?? t.CreatedAt)` and the `select new EmployeeWorkTaskRow(...)` join projection need a real database. If Docker is running, add one `ONEVO.Tests.Integration` test per new method, following an existing repository test in that project. Otherwise, run the API against the dev database and call the endpoints once Task 4 is in, before you commit Task 4. Either way, run this step before building on top of Task 1.

If `orderby (t.UpdatedAt ?? t.CreatedAt)` fails to translate, order by `t.UpdatedAt.HasValue ? t.UpdatedAt.Value : t.CreatedAt` instead.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/Tasks/RepositoryInterfaces/IWorkTaskRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkTaskRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkActivityTaskRepositoryReadsTests.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkGraphRepositoryReadsTestsDb.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EmployeeWorkGraphRepositoryReadsTests.cs
git commit -m "feat(work): employee task reads for work & activity cards

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: DTOs and pure rules

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/DTOs/EmployeeWorkActivityTaskResponses.cs`
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/WorkActivityTaskRules.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/WorkActivityTaskRulesTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class WorkActivityTaskRulesTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static EmployeeWorkTaskRow Row(string shortId, string? due) => new(
        Guid.NewGuid(), shortId, shortId, Guid.NewGuid(), "Website", "In Progress", "#2563EB", false,
        "medium", 3, due is null ? null : DateOnly.Parse(due), 20, DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"));

    [Fact]
    public void ToAttention_PutsOverdueFirstOldestFirst_ThenDueSoon_AndCapsAtFive()
    {
        var rows = new[]
        {
            Row("A", "2026-09-17"), Row("B", "2026-09-10"), Row("C", "2026-09-14"),
            Row("D", "2026-09-15"), Row("E", "2026-09-01"), Row("F", "2026-09-16")
        };

        var result = WorkActivityTaskRules.ToAttention(rows, AsOf);

        result.TotalCount.Should().Be(6);
        result.Items.Select(i => i.ShortId).Should().Equal("E", "B", "C", "D", "F");
        result.Items[0].Reason.Should().Be("overdue");
        result.Items[0].OverdueDays.Should().Be(14);
        result.Items[3].Reason.Should().Be("due_soon");
        result.Items[3].OverdueDays.Should().BeNull();
    }

    [Fact]
    public void MonthlyCompleted_ReturnsSixMonthsEndingAtTheGivenMonth_WithCounts()
    {
        var completed = new[]
        {
            DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
            DateTimeOffset.Parse("2026-09-30T23:59:59+00:00"),
            DateTimeOffset.Parse("2026-04-15T10:00:00+00:00")
        };

        var months = WorkActivityTaskRules.MonthlyCompleted(completed, new DateOnly(2026, 9, 12), 6);

        months.Select(m => m.Month).Should().Equal("2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09");
        months.Select(m => m.Completed).Should().Equal(1, 0, 0, 0, 0, 2);
    }

    [Fact]
    public void TrendWindow_SpansTheFirstOfTheFirstMonthToTheFirstOfTheNextMonth()
    {
        var (from, to) = WorkActivityTaskRules.TrendWindow(new DateOnly(2026, 1, 20), 6);

        from.Should().Be(DateTimeOffset.Parse("2025-08-01T00:00:00+00:00"));
        to.Should().Be(DateTimeOffset.Parse("2026-02-01T00:00:00+00:00"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkActivityTaskRulesTests"`
Expected: build FAIL, because `WorkActivityTaskRules` does not exist.

- [ ] **Step 3: Create the DTOs**

`EmployeeWorkActivityTaskResponses.cs`:

```csharp
namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

/// <summary>One task row on the Work & Activity Needs attention / Recent tasks cards.</summary>
public sealed record EmployeeWorkTaskItem(
    Guid TaskId,
    string ShortId,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string StatusName,
    string StatusColor,
    string Priority,
    int? StoryPoints,
    DateOnly? DueDate,
    int ProgressPercent);

/// <summary>reason: "overdue" | "due_soon". OverdueDays is set only for overdue items.</summary>
public sealed record EmployeeAttentionItem(
    Guid TaskId,
    string ShortId,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string StatusName,
    string StatusColor,
    int? StoryPoints,
    DateOnly DueDate,
    string Reason,
    int? OverdueDays,
    int? DaysUntilDue);

public sealed record EmployeeNeedsAttentionResponse(DateOnly AsOf, int TotalCount, IReadOnlyList<EmployeeAttentionItem> Items);

public sealed record EmployeeRecentTasksResponse(IReadOnlyList<EmployeeWorkTaskItem> Items);

/// <summary>Month is "YYYY-MM"; Completed counts tasks whose CompletedAt falls in that UTC month.</summary>
public sealed record EmployeeDeliveryTrendMonth(string Month, int Completed);

public sealed record EmployeeDeliveryTrendResponse(IReadOnlyList<EmployeeDeliveryTrendMonth> Months);
```

- [ ] **Step 4: Create the rules**

`WorkActivityTaskRules.cs`:

```csharp
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Services;

/// <summary>Pure shaping rules for the Work & Activity task cards. Completion and overdue follow
/// Plan 3A (EmployeeTaskPeriodCalculator) so the Overview and Work & Activity tabs agree.</summary>
public static class WorkActivityTaskRules
{
    public const int DueSoonDays = 3;
    public const int MaxItems = 5;
    public const int TrendMonths = 6;

    public static EmployeeNeedsAttentionResponse ToAttention(IReadOnlyList<EmployeeWorkTaskRow> openDueRows, DateOnly asOf)
    {
        var items = openDueRows
            .Where(r => r.DueDate.HasValue)
            .OrderBy(r => r.DueDate!.Value < asOf ? 0 : 1)
            .ThenBy(r => r.DueDate)
            .Take(MaxItems)
            .Select(r =>
            {
                var due = r.DueDate!.Value;
                var overdue = due < asOf;
                return new EmployeeAttentionItem(
                    r.Id, r.ShortId, r.Title, r.ProjectId, r.ProjectName, r.StatusName, r.StatusColor, r.StoryPoints, due,
                    overdue ? "overdue" : "due_soon",
                    overdue ? asOf.DayNumber - due.DayNumber : null,
                    overdue ? null : due.DayNumber - asOf.DayNumber);
            })
            .ToList();

        return new EmployeeNeedsAttentionResponse(asOf, openDueRows.Count(r => r.DueDate.HasValue), items);
    }

    public static EmployeeWorkTaskItem ToItem(EmployeeWorkTaskRow r) => new(
        r.Id, r.ShortId, r.Title, r.ProjectId, r.ProjectName, r.StatusName, r.StatusColor,
        r.Priority, r.StoryPoints, r.DueDate, r.ProgressPercent);

    /// <summary>[first day of the first month, first day of the month after endMonth) in UTC.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) TrendWindow(DateOnly endMonthDay, int months)
    {
        var endMonth = new DateOnly(endMonthDay.Year, endMonthDay.Month, 1);
        var first = endMonth.AddMonths(-(months - 1));
        var after = endMonth.AddMonths(1);
        return (Utc(first), Utc(after));
    }

    public static IReadOnlyList<EmployeeDeliveryTrendMonth> MonthlyCompleted(
        IReadOnlyList<DateTimeOffset> completedAt, DateOnly endMonthDay, int months)
    {
        var endMonth = new DateOnly(endMonthDay.Year, endMonthDay.Month, 1);
        var counts = completedAt
            .Select(c => c.ToUniversalTime())
            .GroupBy(c => (c.Year, c.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<EmployeeDeliveryTrendMonth>(months);
        for (var i = months - 1; i >= 0; i--)
        {
            var m = endMonth.AddMonths(-i);
            counts.TryGetValue((m.Year, m.Month), out var n);
            result.Add(new EmployeeDeliveryTrendMonth($"{m.Year:D4}-{m.Month:D2}", n));
        }
        return result;
    }

    private static DateTimeOffset Utc(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~WorkActivityTaskRulesTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/DTOs/EmployeeWorkActivityTaskResponses.cs src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Services/WorkActivityTaskRules.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/WorkActivityTaskRulesTests.cs
git commit -m "feat(work): work & activity task DTOs and shaping rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The three query handlers

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/WorkActivity/GetEmployeeNeedsAttention/GetEmployeeNeedsAttentionQuery.cs` + `...Handler.cs`
- Create: `.../GetEmployeeRecentTasks/GetEmployeeRecentTasksQuery.cs` + `...Handler.cs`
- Create: `.../GetEmployeeDeliveryTrend/GetEmployeeDeliveryTrendQuery.cs` + `...Handler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkActivityTaskHandlersTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class GetEmployeeWorkActivityTaskHandlersTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 15);

    public GetEmployeeWorkActivityTaskHandlersTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, Guid.NewGuid(), null, "full_time", "active", null, null)));
    }

    private EmployeeWorkTaskRow Row(string shortId, string due) => new(
        Guid.NewGuid(), shortId, shortId, Guid.NewGuid(), "Website", "Review", "#7C3AED", false,
        "high", 5, DateOnly.Parse(due), 60, DateTimeOffset.Parse("2026-09-10T00:00:00+00:00"));

    [Fact]
    public async Task NeedsAttention_ReadsTasksDueWithinThreeDays_AndShapesThem()
    {
        _tasks.Setup(t => t.ListOpenDueByAsync(_tenantId, _employeeId, new DateOnly(2026, 9, 18), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("A", "2026-09-17"), Row("B", "2026-09-13") });
        var handler = new GetEmployeeNeedsAttentionQueryHandler(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeNeedsAttentionQuery(_employeeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Select(i => i.ShortId).Should().Equal("B", "A");
        result.Value.Items[0].StatusName.Should().Be("Review");
    }

    [Fact]
    public async Task NeedsAttention_PassesThroughGuardFailure_WithoutReadingTasks()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));
        var handler = new GetEmployeeNeedsAttentionQueryHandler(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeNeedsAttentionQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        _tasks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RecentTasks_Forbidden_WithoutTasksReadWhenNotSelf()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });
        var handler = new GetEmployeeRecentTasksQueryHandler(_guard.Object, _employees.Object, _tasks.Object, _user.Object);

        var result = await handler.Handle(new GetEmployeeRecentTasksQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RecentTasks_AllowsSelf_AndTakesFive()
    {
        _user.Setup(u => u.HasPermission("tasks:read")).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        _tasks.Setup(t => t.ListRecentlyChangedAssignedAsync(_tenantId, _employeeId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("A", "2026-09-20") });
        var handler = new GetEmployeeRecentTasksQueryHandler(_guard.Object, _employees.Object, _tasks.Object, _user.Object);

        var result = await handler.Handle(new GetEmployeeRecentTasksQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.Priority.Should().Be("high");
    }

    [Fact]
    public async Task DeliveryTrend_DefaultsToTheCurrentMonth_AndReadsASixMonthWindow()
    {
        _tasks.Setup(t => t.ListCompletedAtForEmployeeAsync(_tenantId, _employeeId,
                DateTimeOffset.Parse("2026-04-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { DateTimeOffset.Parse("2026-09-03T08:00:00+00:00") });
        var handler = new GetEmployeeDeliveryTrendQueryHandler(_guard.Object, _employees.Object, _tasks.Object, _user.Object, _clock.Object);

        var result = await handler.Handle(new GetEmployeeDeliveryTrendQuery(_employeeId, null), CancellationToken.None);

        result.Value!.Months.Should().HaveCount(6);
        result.Value.Months[^1].Should().Be(new ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs.EmployeeDeliveryTrendMonth("2026-09", 1));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkActivityTaskHandlersTests"`
Expected: build FAIL, because the queries do not exist.

- [ ] **Step 3: Create the queries**

`GetEmployeeNeedsAttentionQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;

public sealed record GetEmployeeNeedsAttentionQuery(Guid EmployeeId) : IRequest<Result<EmployeeNeedsAttentionResponse>>;
```

`GetEmployeeRecentTasksQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;

public sealed record GetEmployeeRecentTasksQuery(Guid EmployeeId) : IRequest<Result<EmployeeRecentTasksResponse>>;
```

`GetEmployeeDeliveryTrendQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;

/// <summary>To = any day in the last month of the 6-month window; null = today.</summary>
public sealed record GetEmployeeDeliveryTrendQuery(Guid EmployeeId, DateOnly? To) : IRequest<Result<EmployeeDeliveryTrendResponse>>;
```

- [ ] **Step 4: Create the handlers**

`GetEmployeeNeedsAttentionQueryHandler.cs`:

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

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;

public sealed class GetEmployeeNeedsAttentionQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeNeedsAttentionQuery, Result<EmployeeNeedsAttentionResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeNeedsAttentionResponse>> Handle(GetEmployeeNeedsAttentionQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeNeedsAttentionResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeNeedsAttentionResponse>.Forbidden("You do not have access to this employee's tasks.");

        var asOf = clock.Today;
        var rows = await tasks.ListOpenDueByAsync(tenantId, request.EmployeeId, asOf.AddDays(WorkActivityTaskRules.DueSoonDays), ct);
        return Result<EmployeeNeedsAttentionResponse>.Success(WorkActivityTaskRules.ToAttention(rows, asOf));
    }
}
```

`GetEmployeeRecentTasksQueryHandler.cs`:

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

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;

public sealed class GetEmployeeRecentTasksQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeRecentTasksQuery, Result<EmployeeRecentTasksResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeRecentTasksResponse>> Handle(GetEmployeeRecentTasksQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeRecentTasksResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeRecentTasksResponse>.Forbidden("You do not have access to this employee's tasks.");

        var rows = await tasks.ListRecentlyChangedAssignedAsync(tenantId, request.EmployeeId, WorkActivityTaskRules.MaxItems, ct);
        return Result<EmployeeRecentTasksResponse>.Success(new EmployeeRecentTasksResponse(rows.Select(WorkActivityTaskRules.ToItem).ToList()));
    }
}
```

`GetEmployeeDeliveryTrendQueryHandler.cs`:

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

namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;

public sealed class GetEmployeeDeliveryTrendQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IWorkTaskRepository tasks,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeDeliveryTrendQuery, Result<EmployeeDeliveryTrendResponse>>
{
    public const string ModulePermission = "tasks:read";

    public async Task<Result<EmployeeDeliveryTrendResponse>> Handle(GetEmployeeDeliveryTrendQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeDeliveryTrendResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (!await EmployeeOverviewAccess.HasAccessAsync(currentUser, employees, tenantId, request.EmployeeId, ModulePermission, ct))
            return Result<EmployeeDeliveryTrendResponse>.Forbidden("You do not have access to this employee's tasks.");

        var endDay = request.To ?? clock.Today;
        var (from, to) = WorkActivityTaskRules.TrendWindow(endDay, WorkActivityTaskRules.TrendMonths);
        var completed = await tasks.ListCompletedAtForEmployeeAsync(tenantId, request.EmployeeId, from, to, ct);

        return Result<EmployeeDeliveryTrendResponse>.Success(new EmployeeDeliveryTrendResponse(
            WorkActivityTaskRules.MonthlyCompleted(completed, endDay, WorkActivityTaskRules.TrendMonths)));
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeWorkActivityTaskHandlersTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/WorkManagement/EmployeeOverview/Queries/WorkActivity tests/ONEVO.Tests.Unit/Features/WorkManagement/GetEmployeeWorkActivityTaskHandlersTests.cs
git commit -m "feat(work): needs-attention, recent-tasks and delivery-trend queries

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Controller actions + architecture test

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs` (after `GetOverviewTimeOff` / 3A's work and delivery actions)
- Modify: `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs`

- [ ] **Step 1: Add the failing architecture rows**

In `EmployeesController_OverviewWidgetActions_RequireEmployeesReadAndUseTheirRoute`, add:

```csharp
    [InlineData("work-activity/needs-attention", "GetWorkActivityNeedsAttention")]
    [InlineData("work-activity/recent-tasks", "GetWorkActivityRecentTasks")]
    [InlineData("work-activity/delivery-trend", "GetWorkActivityDeliveryTrend")]
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~EmployeesControllerArchitectureTests"`
Expected: 3 FAIL, "Could not locate the GetWorkActivityNeedsAttention action".

- [ ] **Step 3: Add the actions**

Add the three `using` lines for the query namespaces at the top of the file, then:

```csharp
    /// <summary>Work & Activity Needs attention card: open tasks overdue or due within 3 days (top 5 + total).</summary>
    [HttpGet("{id:guid}/work-activity/needs-attention")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityNeedsAttention(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeNeedsAttentionQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Recent tasks card: the 5 most recently changed assigned tasks.</summary>
    [HttpGet("{id:guid}/work-activity/recent-tasks")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityRecentTasks(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeRecentTasksQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity delivery chart: completed tasks per month for the 6 months ending at `to`'s month.</summary>
    [HttpGet("{id:guid}/work-activity/delivery-trend")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityDeliveryTrend(Guid id, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDeliveryTrendQuery(id, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

(`ONEVO.Api.Filters` is already imported for `RequirePermission`; `RequireAnyModule` lives in the same namespace.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Architecture` and then `dotnet test tests/ONEVO.Tests.Unit`
Expected: all PASS.

- [ ] **Step 5: Manual smoke** (API running, logged-in tenant session; `{apiBase}` is the local API origin from `PORTS.md`)

```bash
curl -s -b cookies.txt "{apiBase}/api/v1/employees/{id}/work-activity/needs-attention"
```

Expected: `200` with `{ "asOf": "...", "totalCount": n, "items": [...] }`. For a tenant without Work Management: `403`.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(corehr): expose work & activity task endpoints

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- Mockup coverage: Needs attention (Task 3), Recent tasks (Task 3), Delivery chart (Task 3). Work summary and delivery percentages come from 3A.
- The completion rule matches 3A (`MarksTaskComplete || ProgressPercent >= 100`) in both the SQL filter and the docs.
- Names used across tasks: `EmployeeWorkTaskRow`, `ListOpenDueByAsync`, `ListRecentlyChangedAssignedAsync`, `ListCompletedAtForEmployeeAsync`, `WorkActivityTaskRules.{ToAttention, ToItem, TrendWindow, MonthlyCompleted, DueSoonDays, MaxItems, TrendMonths}`.
- Known limit: tasks completed without a `CompletedAt` (legacy rows) are left out of the monthly trend.
