# Employee Overview — Recent Activity (Plan 4B, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add `GET /api/v1/employees/{id}/overview/recent-activity`: a newest-first feed of the actions one employee took across attendance, leave and Work Management, with "load older" paging — the **union read** you chose (Option C) that a future activity-events table can replace behind the same contract.

**Architecture:** One repository (`IEmployeeActivityFeedRepository`) runs one small query per source, each returning at most `take` rows older than the cursor, merges them newest-first and returns the top `take` (this is exact for a union: the top `take` overall are always within each source's top `take`). A thin handler adds the coverage guard, paging (`limit + 1` to detect "more"), and human wording.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md` (Option C). Prerequisites: Plan 1 (`IEmployeeReadAccessGuard`) and Plan 2 (controller theory test) committed. Sibling: Plan 4A (`...-checklists-history-upcoming-backend.md`).

## Decisions I have NOT made for you (read before executing)

You have said permission and visibility choices are yours. This endpoint therefore applies **no module permission** — only the route's `[RequirePermission("employees:read")]` plus the coverage guard (the rule already approved in the spec, §4.5). It does not use `attendance:read`, `leave:read`, `tasks:read` or `monitoring:read`. Name a permission and I will add it.

Because this feed shows *what a person did*, these are worth an explicit yes/no from you (the plan applies the conservative default shown):
1. **Content is never shown** — comments appear as "Commented on task WEB-12", never the comment text; leave/correction reasons are not shown.
2. **A coverage viewer sees the employee's Work Management actions across all projects**, including projects the viewer is not a member of (same rule as the Work Network graph).
3. **Only actions the employee took** — approvals *decided by others* about the employee are not listed (they belong to Approval Activity / History).

## What the feed contains (and does not)

Included (each is a real row with the employee as actor and a timestamp): clock in, clock out, leave requested, attendance correction requested, work-area change requested, location change requested, task created, task status changed, task edited, task progress changed, task commented, task work started/stopped, joined a project/module.

Not included, because no per-action record exists: profile edits, document uploads, logins, meeting attendance, and any action made through code paths that write no history row. The response carries `isPartial: true`, and the UI must say "Showing key activity" until an events table exists. (Correction: earlier I said Work Management had no task history. It does — `TaskStatusChangeLog`, `TaskEditLog`, `TaskPercentageLog`, `TaskComment`, `TaskClockingSession` — and this plan uses them.)

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Before every commit: `git branch --show-current` and `git status --short` (shared tree); stage only the task's files. **No git worktrees.**
- Build/test per project. Before **each** run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell; the dev server respawns).
- The `AuditableEntityInterceptor` stamps `CreatedAt = clock.UtcNow` and `CreatedById = currentUser.UserId` (only when `IsAuthenticated`, else `Guid.Empty`) on every added `BaseEntity`. Repository tests therefore set the mocked clock / current user **before each `SaveChangesAsync`** instead of assigning those two properties.
- "Task created" matches `WorkTask.CreatedById == employee.UserId` (a **user** id); every other Work Management source is keyed by `EmployeeId`.
- Paging: `before` is an exclusive upper bound on the event time; `limit` 1..50 (default 20).
- Existing endpoints untouched. Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: Feed repository — attendance and leave sources

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeActivityFeedRepository.cs`
- Create: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeActivityFeedRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeActivityFeedRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `sealed record EmployeeActivityRow(string Kind, Guid SourceId, DateTimeOffset At, string? Target, string? Detail)`
  - `IEmployeeActivityFeedRepository.ListAsync(Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset? before, int take, CancellationToken ct = default) : Task<IReadOnlyList<EmployeeActivityRow>>` — newest first, at most `take`, all with `At < before` when given.
  - Kinds in this task: `attendance_clock_in`, `attendance_clock_out`, `leave_requested`, `attendance_correction_requested`, `work_area_change_requested`, `location_change_requested`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EfEmployeeActivityFeedRepositoryTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static DateTimeOffset T(string value) => DateTimeOffset.Parse(value);

    [Fact]
    public async Task List_ReturnsEverySourceForThatEmployeeOnly_NewestFirst()
    {
        await using var db = BuildDb();
        var leaveType = new LeaveType { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Annual leave", Code = "AL" };
        db.LeaveTypes.Add(leaveType);

        db.AttendanceRecords.AddRange(
            new AttendanceRecord { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = new DateOnly(2026, 9, 21), ActualStart = T("2026-09-21T03:30:00+00:00"), ActualEnd = T("2026-09-21T12:00:00+00:00") },
            new AttendanceRecord { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _otherEmployeeId, Date = new DateOnly(2026, 9, 21), ActualStart = T("2026-09-21T03:00:00+00:00") });
        db.LeaveRequests.Add(new LeaveRequest { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = leaveType.Id, CreatedAt = T("2026-09-20T09:00:00+00:00") });
        db.AttendanceCorrections.Add(new AttendanceCorrection { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, WorkDate = new DateOnly(2026, 9, 18), CreatedAt = T("2026-09-19T09:00:00+00:00") });
        db.WorkAreaChangeRequests.Add(new WorkAreaChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, RequestedWorkModeName = "Remote", RequestedAt = T("2026-09-18T09:00:00+00:00") });
        db.LocationChangeRequests.Add(new LocationChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, RequestedAt = T("2026-09-17T09:00:00+00:00") });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfEmployeeActivityFeedRepository(db).ListAsync(_tenantId, _employeeId, _userId, null, 50);

        Assert.Equal(
            new[]
            {
                "attendance_clock_out", "attendance_clock_in", "leave_requested",
                "attendance_correction_requested", "work_area_change_requested", "location_change_requested"
            },
            rows.Select(r => r.Kind).ToArray());
        Assert.Equal("Annual leave", rows.Single(r => r.Kind == "leave_requested").Target);
        Assert.Equal("2026-09-18", rows.Single(r => r.Kind == "attendance_correction_requested").Target);
        Assert.Equal("Remote", rows.Single(r => r.Kind == "work_area_change_requested").Target);
        Assert.Equal(rows.Select(r => r.At).OrderByDescending(x => x), rows.Select(r => r.At));
    }

    [Fact]
    public async Task List_AppliesTheCursorAsAnExclusiveBound_AndTheTakeLimit()
    {
        await using var db = BuildDb();
        db.AttendanceRecords.AddRange(
            Record(new DateOnly(2026, 9, 21), "2026-09-21T03:00:00+00:00"),
            Record(new DateOnly(2026, 9, 22), "2026-09-22T03:00:00+00:00"),
            Record(new DateOnly(2026, 9, 23), "2026-09-23T03:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new EfEmployeeActivityFeedRepository(db);

        var older = await repo.ListAsync(_tenantId, _employeeId, _userId, T("2026-09-23T03:00:00+00:00"), 50);
        Assert.Equal(new[] { T("2026-09-22T03:00:00+00:00"), T("2026-09-21T03:00:00+00:00") }, older.Select(r => r.At).ToArray());

        var oneOnly = await repo.ListAsync(_tenantId, _employeeId, _userId, null, 1);
        Assert.Equal(T("2026-09-23T03:00:00+00:00"), Assert.Single(oneOnly).At);
    }

    [Fact]
    public async Task List_SkipsClockOutForARecordThatIsStillOpen()
    {
        await using var db = BuildDb();
        db.AttendanceRecords.Add(Record(new DateOnly(2026, 9, 21), "2026-09-21T03:00:00+00:00"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfEmployeeActivityFeedRepository(db).ListAsync(_tenantId, _employeeId, _userId, null, 50);

        Assert.Equal("attendance_clock_in", Assert.Single(rows).Kind);
    }

    private AttendanceRecord Record(DateOnly date, string start) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, Date = date, ActualStart = T(start)
    };

    private ApplicationDbContext BuildDb()
    {
        _currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(_currentUser.Object, _clock.Object),
            new SoftDeleteInterceptor(_clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfEmployeeActivityFeedRepositoryTests"`
Expected: build FAIL — `EfEmployeeActivityFeedRepository` does not exist.

- [ ] **Step 3: Write the interface**

`IEmployeeActivityFeedRepository.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

/// <summary>One thing an employee did, from a source table. Target is a short subject (a task,
/// a leave type, ...); Detail an optional qualifier ("40% → 60%"). Never contains free text the
/// employee typed (comments, reasons).</summary>
public sealed record EmployeeActivityRow(string Kind, Guid SourceId, DateTimeOffset At, string? Target, string? Detail);

public interface IEmployeeActivityFeedRepository
{
    /// <summary>The employee's most recent actions across attendance, leave and Work Management,
    /// newest first, at most <paramref name="take"/>, all strictly older than <paramref name="before"/>
    /// when it is given. <paramref name="userId"/> is the employee's user id (used for "task created").</summary>
    Task<IReadOnlyList<EmployeeActivityRow>> ListAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset? before, int take, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the Ef repository (attendance and leave sources)**

`EfEmployeeActivityFeedRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

/// <summary>
/// Union read behind the Overview "Recent Activity" widget. Each source is one small query
/// returning at most `take` rows older than the cursor; the merged top `take` is exact because the
/// overall top `take` is always inside each source's own top `take`.
/// </summary>
public sealed class EfEmployeeActivityFeedRepository(ApplicationDbContext db) : IEmployeeActivityFeedRepository
{
    public async Task<IReadOnlyList<EmployeeActivityRow>> ListAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset? before, int take, CancellationToken ct = default)
    {
        var cutoff = before ?? DateTimeOffset.MaxValue;
        var rows = new List<EmployeeActivityRow>();

        rows.AddRange(await AttendanceAndLeaveAsync(tenantId, employeeId, cutoff, take, ct));

        return rows
            .OrderByDescending(r => r.At)
            .ThenBy(r => r.Kind, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }

    private async Task<List<EmployeeActivityRow>> AttendanceAndLeaveAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset cutoff, int take, CancellationToken ct)
    {
        var rows = new List<EmployeeActivityRow>();

        rows.AddRange(await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.ActualStart != null && r.ActualStart < cutoff)
            .OrderByDescending(r => r.ActualStart).Take(take)
            .Select(r => new EmployeeActivityRow("attendance_clock_in", r.Id, r.ActualStart!.Value, null, null))
            .ToListAsync(ct));

        rows.AddRange(await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId && r.ActualEnd != null && r.ActualEnd < cutoff)
            .OrderByDescending(r => r.ActualEnd).Take(take)
            .Select(r => new EmployeeActivityRow("attendance_clock_out", r.Id, r.ActualEnd!.Value, null, null))
            .ToListAsync(ct));

        rows.AddRange(await (
            from r in db.LeaveRequests.AsNoTracking()
            join t in db.LeaveTypes.AsNoTracking() on r.LeaveTypeId equals t.Id
            where r.TenantId == tenantId && r.EmployeeId == employeeId && r.CreatedAt < cutoff
            orderby r.CreatedAt descending
            select new EmployeeActivityRow("leave_requested", r.Id, r.CreatedAt, t.Name, null)
        ).Take(take).ToListAsync(ct));

        var corrections = await db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId && c.CreatedAt < cutoff)
            .OrderByDescending(c => c.CreatedAt).Take(take)
            .Select(c => new { c.Id, c.CreatedAt, c.WorkDate })
            .ToListAsync(ct);
        rows.AddRange(corrections.Select(c => new EmployeeActivityRow(
            "attendance_correction_requested", c.Id, c.CreatedAt, c.WorkDate.ToString("yyyy-MM-dd"), null)));

        rows.AddRange(await db.WorkAreaChangeRequests.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.EmployeeId == employeeId && w.RequestedAt < cutoff)
            .OrderByDescending(w => w.RequestedAt).Take(take)
            .Select(w => new EmployeeActivityRow("work_area_change_requested", w.Id, w.RequestedAt, w.RequestedWorkModeName, null))
            .ToListAsync(ct));

        rows.AddRange(await db.LocationChangeRequests.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.EmployeeId == employeeId && l.RequestedAt < cutoff)
            .OrderByDescending(l => l.RequestedAt).Take(take)
            .Select(l => new EmployeeActivityRow("location_change_requested", l.Id, l.RequestedAt, null, null))
            .ToListAsync(ct));

        return rows;
    }
}
```

Register it in `src/ONEVO.Infrastructure/DependencyInjection.cs`, right after the `IEmployeeReadAccessGuard` registration:

```csharp
        services.AddScoped<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeActivityFeedRepository, ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeActivityFeedRepository>();
```

- [ ] **Step 5: Run to verify the tests pass, plus the layer check**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfEmployeeActivityFeedRepositoryTests"
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~LayerDependencyTests"
```
Expected: 3 passed; layer tests pass. (If a required-property error appears for a test entity, set that property in the initializer; the tests set only what the queries read.)

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/RepositoryInterfaces/IEmployeeActivityFeedRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeActivityFeedRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeActivityFeedRepositoryTests.cs
git commit -m "feat(people): add employee activity feed repository (attendance and leave sources)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Feed repository — Work Management sources

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeActivityFeedRepository.cs`
- Modify (test): `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeActivityFeedRepositoryTests.cs`

**Interfaces:**
- Produces (new kinds): `task_created`, `task_status_changed`, `task_edited`, `task_progress_changed`, `task_commented`, `task_clocked_in`, `task_clocked_out`, `module_joined`. Task rows use `Target = "{ShortId} {Title}"`; status change `Detail = "→ {new status name}"`; progress change `Detail = "{previous}% → {new}%"`; `module_joined` `Target` = the module (objective) title, or the project name when the membership is in the project's default objective.

- [ ] **Step 1: Add the failing tests**

Add these usings to the test file:

```csharp
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
```

and add inside the test class:

```csharp
    [Fact]
    public async Task List_ReturnsWorkManagementActions_ForThatEmployeeOnly_WithSubjectsAndDetails()
    {
        await using var db = BuildDb();
        var project = new Project { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Website", Identifier = "WEB", IsActive = true };
        var module = new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Title = "Checkout", OwnerId = Guid.NewGuid(), IsActive = true };
        var root = new Objective { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Title = "Website (default)", OwnerId = Guid.NewGuid(), IsDefault = true, IsActive = true };
        var active = new WmTaskStatus { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, Name = "In review", Category = "active" };
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, ObjectiveId = module.Id,
            StatusId = active.Id, CategoryId = Guid.NewGuid(), ShortId = "WEB-12", Title = "Fix cart"
        };
        db.Projects.Add(project);
        db.Objectives.AddRange(module, root);
        db.TaskStatuses.Add(active);

        // BaseEntity rows get CreatedAt/CreatedById from the mocked clock/user at save time.
        _clock.SetupGet(c => c.UtcNow).Returns(T("2026-09-10T08:00:00+00:00"));
        db.WorkTasks.Add(task);                                                        // created by _userId (task_created)
        await db.SaveChangesAsync();

        _clock.SetupGet(c => c.UtcNow).Returns(T("2026-09-11T08:00:00+00:00"));
        db.TaskComments.Add(new TaskComment { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _employeeId, Content = "secret text" });
        db.TaskComments.Add(new TaskComment { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _otherEmployeeId, Content = "someone else" });
        await db.SaveChangesAsync();

        db.TaskStatusChangeLogs.Add(new TaskStatusChangeLog { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _employeeId, FromStatusId = Guid.NewGuid(), ToStatusId = active.Id, ChangedAt = T("2026-09-12T08:00:00+00:00") });
        db.TaskEditLogs.Add(new TaskEditLog { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _employeeId, ChangedAt = T("2026-09-13T08:00:00+00:00") });
        db.TaskPercentageLogs.Add(new TaskPercentageLog { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _employeeId, PreviousPercent = 40, NewPercent = 60, ChangedAt = T("2026-09-14T08:00:00+00:00") });
        db.TaskClockingSessions.Add(new TaskClockingSession { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, EmployeeId = _employeeId, ClockInAt = T("2026-09-15T08:00:00+00:00"), ClockOutAt = T("2026-09-15T10:00:00+00:00") });
        db.ProjectMembers.AddRange(
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, ObjectiveId = module.Id, EmployeeId = _employeeId, JoinedAt = T("2026-09-16T08:00:00+00:00") },
            new ProjectMember { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = project.Id, ObjectiveId = root.Id, EmployeeId = _employeeId, JoinedAt = T("2026-09-17T08:00:00+00:00") });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfEmployeeActivityFeedRepository(db).ListAsync(_tenantId, _employeeId, _userId, null, 50);
        RowOf(rows, "task_created").Should_HaveTarget("WEB-12 Fix cart");
        Assert.Equal("WEB-12 Fix cart", rows.Single(r => r.Kind == "task_commented").Target);
        Assert.Equal("→ In review", rows.Single(r => r.Kind == "task_status_changed").Detail);
        Assert.Equal("WEB-12 Fix cart", rows.Single(r => r.Kind == "task_edited").Target);
        Assert.Equal("40% → 60%", rows.Single(r => r.Kind == "task_progress_changed").Detail);
        Assert.Contains(rows, r => r.Kind == "task_clocked_in" && r.At == T("2026-09-15T08:00:00+00:00"));
        Assert.Contains(rows, r => r.Kind == "task_clocked_out" && r.At == T("2026-09-15T10:00:00+00:00"));
        Assert.Equal(new[] { "Website", "Checkout" }, rows.Where(r => r.Kind == "module_joined").Select(r => r.Target).ToArray());
        Assert.Single(rows, r => r.Kind == "task_commented");                       // the other employee's comment is excluded
        Assert.DoesNotContain(rows, r => (r.Target ?? "").Contains("secret") || (r.Detail ?? "").Contains("secret"));
    }

    [Fact]
    public async Task List_DoesNotListTasksCreatedByAnotherUser()
    {
        await using var db = BuildDb();
        var otherUser = Guid.NewGuid();
        _currentUser.SetupGet(u => u.UserId).Returns(otherUser);
        _clock.SetupGet(c => c.UtcNow).Returns(T("2026-09-10T08:00:00+00:00"));
        db.WorkTasks.Add(new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(),
            StatusId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), ShortId = "WEB-1", Title = "Not mine"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rows = await new EfEmployeeActivityFeedRepository(db).ListAsync(_tenantId, _employeeId, _userId, null, 50);

        Assert.DoesNotContain(rows, r => r.Kind == "task_created");
    }
```

Replace the line `RowOf(rows, "task_created").Should_HaveTarget("WEB-12 Fix cart");` with the plain assertion (that helper does not exist):

```csharp
        Assert.Equal("WEB-12 Fix cart", rows.Single(r => r.Kind == "task_created").Target);
```

- [ ] **Step 2: Run to verify they fail**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfEmployeeActivityFeedRepositoryTests"`
Expected: the two new tests FAIL (no Work Management kinds are returned yet); the three Task 1 tests still pass.

- [ ] **Step 3: Add the Work Management sources**

In `EfEmployeeActivityFeedRepository.cs`, change `ListAsync` to call the new method — replace

```csharp
        rows.AddRange(await AttendanceAndLeaveAsync(tenantId, employeeId, cutoff, take, ct));
```
with
```csharp
        rows.AddRange(await AttendanceAndLeaveAsync(tenantId, employeeId, cutoff, take, ct));
        rows.AddRange(await WorkAsync(tenantId, employeeId, userId, cutoff, take, ct));
```

and add `using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;` is **not** needed (queries use DbSets only). Add this method to the class:

```csharp
    private async Task<List<EmployeeActivityRow>> WorkAsync(
        Guid tenantId, Guid employeeId, Guid userId, DateTimeOffset cutoff, int take, CancellationToken ct)
    {
        var rows = new List<EmployeeActivityRow>();
        static string Label(string shortId, string title) => $"{shortId} {title}";

        var created = await db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedById == userId && t.CreatedAt < cutoff)
            .OrderByDescending(t => t.CreatedAt).Take(take)
            .Select(t => new { t.Id, t.CreatedAt, t.ShortId, t.Title })
            .ToListAsync(ct);
        rows.AddRange(created.Select(t => new EmployeeActivityRow("task_created", t.Id, t.CreatedAt, Label(t.ShortId, t.Title), null)));

        var statusChanges = await (
            from l in db.TaskStatusChangeLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            join s in db.TaskStatuses.AsNoTracking() on l.ToStatusId equals s.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title, ToStatus = s.Name }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(statusChanges.Select(x => new EmployeeActivityRow(
            "task_status_changed", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), $"→ {x.ToStatus}")));

        var edits = await (
            from l in db.TaskEditLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(edits.Select(x => new EmployeeActivityRow("task_edited", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), null)));

        var progress = await (
            from l in db.TaskPercentageLogs.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on l.TaskId equals t.Id
            where l.TenantId == tenantId && l.EmployeeId == employeeId && l.ChangedAt < cutoff
            orderby l.ChangedAt descending
            select new { l.Id, l.ChangedAt, t.ShortId, t.Title, l.PreviousPercent, l.NewPercent }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(progress.Select(x => new EmployeeActivityRow(
            "task_progress_changed", x.Id, x.ChangedAt, Label(x.ShortId, x.Title), $"{x.PreviousPercent}% → {x.NewPercent}%")));

        // Comment text is deliberately never selected.
        var comments = await (
            from c in db.TaskComments.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on c.TaskId equals t.Id
            where c.TenantId == tenantId && c.EmployeeId == employeeId && c.CreatedAt < cutoff
            orderby c.CreatedAt descending
            select new { c.Id, c.CreatedAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(comments.Select(x => new EmployeeActivityRow("task_commented", x.Id, x.CreatedAt, Label(x.ShortId, x.Title), null)));

        var clockIns = await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && s.EmployeeId == employeeId && s.ClockInAt < cutoff
            orderby s.ClockInAt descending
            select new { s.Id, At = s.ClockInAt, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(clockIns.Select(x => new EmployeeActivityRow("task_clocked_in", x.Id, x.At, Label(x.ShortId, x.Title), null)));

        var clockOuts = await (
            from s in db.TaskClockingSessions.AsNoTracking()
            join t in db.WorkTasks.AsNoTracking() on s.TaskId equals t.Id
            where s.TenantId == tenantId && s.EmployeeId == employeeId && s.ClockOutAt != null && s.ClockOutAt < cutoff
            orderby s.ClockOutAt descending
            select new { s.Id, At = s.ClockOutAt!.Value, t.ShortId, t.Title }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(clockOuts.Select(x => new EmployeeActivityRow("task_clocked_out", x.Id, x.At, Label(x.ShortId, x.Title), null)));

        var joins = await (
            from m in db.ProjectMembers.AsNoTracking()
            join o in db.Objectives.AsNoTracking() on m.ObjectiveId equals o.Id
            join p in db.Projects.AsNoTracking() on m.ProjectId equals p.Id
            where m.TenantId == tenantId && m.EmployeeId == employeeId && m.JoinedAt < cutoff
            orderby m.JoinedAt descending
            select new { m.Id, m.JoinedAt, o.IsDefault, ObjectiveTitle = o.Title, ProjectName = p.Name }
        ).Take(take).ToListAsync(ct);
        rows.AddRange(joins.Select(x => new EmployeeActivityRow(
            "module_joined", x.Id, x.JoinedAt, x.IsDefault ? x.ProjectName : x.ObjectiveTitle, null)));

        return rows;
    }
```

- [ ] **Step 4: Run to verify the tests pass**

Run: same command as Step 2. Expected: 5 passed. (In the first WM test, the ordering of `module_joined` targets is newest first: the default-objective join (project name "Website", 09-17) then "Checkout" (09-16). If a `TaskComment`/`TaskClockingSession` initializer needs another required property, set it in the test.)

- [ ] **Step 5: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfEmployeeActivityFeedRepository.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/EfEmployeeActivityFeedRepositoryTests.cs
git commit -m "feat(people): add work management sources to the employee activity feed

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `overview/recent-activity` handler and endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeRecentActivityResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeRecentActivity/GetEmployeeRecentActivityQuery.cs` and `...QueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeRecentActivityQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Consumes: `IEmployeeReadAccessGuard`, `IEmployeeRepository.GetByIdAsync` (for `UserId`), `IEmployeeActivityFeedRepository` (Tasks 1-2).
- Produces: `EmployeeActivityItem(string Id, string Kind, string Action, string? Target, string? Detail, DateTimeOffset At)`; `EmployeeRecentActivityResponse(IReadOnlyList<EmployeeActivityItem> Items, DateTimeOffset? NextBefore, bool IsPartial)`; `GetEmployeeRecentActivityQuery(Guid EmployeeId, DateTimeOffset? Before = null, int Limit = 20)` (limit 1..50 else 400); `GET /api/v1/employees/{id}/overview/recent-activity?before=&limit=`. `NextBefore` = the last item's `At` when more exist, else `null`; `IsPartial` is always `true` until an events table exists.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeRecentActivityQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeActivityFeedRepository> _feed = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();

    public GetEmployeeRecentActivityQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _employees.Setup(e => e.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId, UserId = _employeeUserId });
        Arrange();
    }

    private GetEmployeeRecentActivityQueryHandler CreateHandler() => new(_guard.Object, _employees.Object, _feed.Object, _user.Object);

    private static EmployeeActivityRow Row(string kind, string at, string? target = null, string? detail = null) =>
        new(kind, Guid.NewGuid(), DateTimeOffset.Parse(at), target, detail);

    private void Arrange(params EmployeeActivityRow[] rows) =>
        _feed.Setup(f => f.ListAsync(_tenantId, _employeeId, _employeeUserId, It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Handle_Returns400_ForAnOutOfRangeLimit(int limit)
    {
        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, limit), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_AsksTheFeedForOneMoreThanTheLimit_UsingTheEmployeesUserId_AndTheCursor()
    {
        var before = DateTimeOffset.Parse("2026-09-20T00:00:00+00:00");

        await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, before, 20), CancellationToken.None);

        _feed.Verify(f => f.ListAsync(_tenantId, _employeeId, _employeeUserId, before, 21, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TurnsRowsIntoWordedItems_KeepingTargetsAndDetails()
    {
        Arrange(
            Row("attendance_clock_in", "2026-09-21T03:30:00+00:00"),
            Row("task_status_changed", "2026-09-20T10:00:00+00:00", "WEB-12 Fix cart", "→ In review"),
            Row("leave_requested", "2026-09-19T09:00:00+00:00", "Annual leave"),
            Row("module_joined", "2026-09-18T09:00:00+00:00", "Checkout"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        var items = result.Value!.Items;
        items.Select(i => i.Action).Should().Equal("Clocked in", "Changed task status", "Requested leave", "Joined");
        items[1].Target.Should().Be("WEB-12 Fix cart");
        items[1].Detail.Should().Be("→ In review");
        items[0].Id.Should().StartWith("attendance_clock_in:");
    }

    [Theory]
    [InlineData("attendance_clock_out", "Clocked out")]
    [InlineData("attendance_correction_requested", "Requested an attendance correction")]
    [InlineData("work_area_change_requested", "Requested a work area change")]
    [InlineData("location_change_requested", "Requested a location change")]
    [InlineData("task_created", "Created task")]
    [InlineData("task_edited", "Edited task")]
    [InlineData("task_progress_changed", "Updated task progress")]
    [InlineData("task_commented", "Commented on task")]
    [InlineData("task_clocked_in", "Started working on task")]
    [InlineData("task_clocked_out", "Stopped working on task")]
    public async Task Handle_HasWordingForEveryKind(string kind, string expected)
    {
        Arrange(Row(kind, "2026-09-21T03:30:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Single().Action.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_FallsBackToAGenericActionForAnUnknownKind()
    {
        Arrange(Row("something_new", "2026-09-21T03:30:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId), CancellationToken.None);

        result.Value!.Items.Single().Action.Should().Be("Activity");
    }

    [Fact]
    public async Task Handle_TrimsTheExtraRow_AndReturnsACursorWhenMoreExist()
    {
        Arrange(Enumerable.Range(1, 3).Select(i => Row("task_edited", $"2026-09-2{i}T00:00:00+00:00")).Reverse().ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, 2), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(2);
        result.Value.NextBefore.Should().Be(result.Value.Items.Last().At);
    }

    [Fact]
    public async Task Handle_HasNoCursorWhenEverythingFits_AndIsAlwaysPartial()
    {
        Arrange(Row("task_edited", "2026-09-21T00:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeRecentActivityQuery(_employeeId, null, 2), CancellationToken.None);

        result.Value!.NextBefore.Should().BeNull();
        result.Value.IsPartial.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeRecentActivityQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 3: Write DTOs, query and handler**

`EmployeeRecentActivityResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Action is human wording ("Changed task status"); Target the subject ("WEB-12 Fix cart");
/// Detail an optional qualifier ("→ In review"). Never contains text the employee typed.</summary>
public sealed record EmployeeActivityItem(
    string Id,
    string Kind,
    string Action,
    string? Target,
    string? Detail,
    DateTimeOffset At);

/// <summary>NextBefore is the cursor for the next (older) page, null when there is none. IsPartial is
/// true while the feed is a union over existing tables rather than a complete event log.</summary>
public sealed record EmployeeRecentActivityResponse(
    IReadOnlyList<EmployeeActivityItem> Items,
    DateTimeOffset? NextBefore,
    bool IsPartial);
```

`GetEmployeeRecentActivityQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;

public sealed record GetEmployeeRecentActivityQuery(Guid EmployeeId, DateTimeOffset? Before = null, int Limit = 20)
    : IRequest<Result<EmployeeRecentActivityResponse>>;
```

`GetEmployeeRecentActivityQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;

/// <summary>
/// Newest-first feed of what an employee did, over existing tables (Option C in the spec: a future
/// activity-events table can replace the repository behind the same response). No module
/// permission: employees:read + coverage only.
/// </summary>
public sealed class GetEmployeeRecentActivityQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    IEmployeeActivityFeedRepository feed,
    ICurrentUser currentUser)
    : IRequestHandler<GetEmployeeRecentActivityQuery, Result<EmployeeRecentActivityResponse>>
{
    public const int MaxLimit = 50;

    private static readonly Dictionary<string, string> Actions = new()
    {
        ["attendance_clock_in"] = "Clocked in",
        ["attendance_clock_out"] = "Clocked out",
        ["leave_requested"] = "Requested leave",
        ["attendance_correction_requested"] = "Requested an attendance correction",
        ["work_area_change_requested"] = "Requested a work area change",
        ["location_change_requested"] = "Requested a location change",
        ["task_created"] = "Created task",
        ["task_status_changed"] = "Changed task status",
        ["task_edited"] = "Edited task",
        ["task_progress_changed"] = "Updated task progress",
        ["task_commented"] = "Commented on task",
        ["task_clocked_in"] = "Started working on task",
        ["task_clocked_out"] = "Stopped working on task",
        ["module_joined"] = "Joined"
    };

    public async Task<Result<EmployeeRecentActivityResponse>> Handle(GetEmployeeRecentActivityQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var access = await guard.EnsureCanRead(tenantId, request.EmployeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeRecentActivityResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        if (request.Limit < 1 || request.Limit > MaxLimit)
            return Result<EmployeeRecentActivityResponse>.Failure($"limit must be between 1 and {MaxLimit}.");

        var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeRecentActivityResponse>.NotFound("The employee or selected organization record could not be found.");

        // One extra row tells us whether an older page exists.
        var rows = await feed.ListAsync(tenantId, request.EmployeeId, employee.UserId, request.Before, request.Limit + 1, ct);
        var page = rows.Take(request.Limit)
            .Select(r => new EmployeeActivityItem(
                $"{r.Kind}:{r.SourceId}", r.Kind, Actions.GetValueOrDefault(r.Kind, "Activity"), r.Target, r.Detail, r.At))
            .ToList();

        DateTimeOffset? nextBefore = rows.Count > request.Limit ? page[^1].At : null;
        return Result<EmployeeRecentActivityResponse>.Success(new EmployeeRecentActivityResponse(page, nextBefore, IsPartial: true));
    }
}
```

- [ ] **Step 4: Run to verify the handler tests pass**

Run: same command as Step 2. Expected: 18 passed (6 facts + 2 `Returns400` rows + 10 `HasWordingForEveryKind` rows).

- [ ] **Step 5: Extend the theory, add the action, run both suites**

Add to the `[Theory]` in `EmployeesControllerArchitectureTests.cs`:

```csharp
    [InlineData("overview/recent-activity", "GetOverviewRecentActivity")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add `using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;` and:

```csharp
    /// <summary>Overview recent activity: what the employee did (attendance, leave, Work Management),
    /// newest first; pass the previous response's nextBefore as `before` for older items. Partial by
    /// design - see GetEmployeeRecentActivityQueryHandler.</summary>
    [HttpGet("{id:guid}/overview/recent-activity")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewRecentActivity(
        Guid id, [FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeRecentActivityQuery(id, before, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

- [ ] **Step 6: Live check**

Start the API; as a user with `employees:read` and coverage: `GET …/overview/recent-activity` → 200 with items newest first and `isPartial: true`; follow `nextBefore` as `before` → older items, no overlap with page 1; `?limit=0` → 400; outside coverage → 403; a comment's text never appears in any field. In a dev tenant with little history the list can be short or empty — say so instead of implying the busy-feed path was exercised.

- [ ] **Step 7: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeRecentActivityResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeRecentActivity src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeRecentActivityQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview recent activity endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- The feed covers the user's ask ("every action he makes in the application including work management activity") as far as existing rows allow, and is honest about the rest (`isPartial`, the not-included list). It fixes my earlier wrong claim that Work Management keeps no task history.
- No module permission is applied; visibility rules that could surprise you are listed as decisions at the top with conservative defaults (no content, coverage-level WM visibility, actor-only).
- Type consistency: `EmployeeActivityRow(Kind, SourceId, At, Target, Detail)` is produced by the repository (T1/T2), consumed by the handler (T3) and the handler test; `EmployeeActivityItem` id is `"{kind}:{sourceId}"`; `ListAsync(tenantId, employeeId, userId, before, take, ct)` is called identically in the handler and asserted in the test (`limit + 1`).
- Verified by reading source: entity/`DbSet` names and fields for every source; the interceptor stamping behaviour (drives how tests are written); `Employee.UserId`. Not compile-checked or run; the executor's first `dotnet test` is the first compile. Fix compile errors by aligning to real signatures, never by weakening a test.- Known limits: each source query runs separately (~15 small queries per page); fine for a per-employee, page-sized read, and the future events table removes it. Clock-in/out come from the attendance record (one in + one out per day), not from every tray event.
