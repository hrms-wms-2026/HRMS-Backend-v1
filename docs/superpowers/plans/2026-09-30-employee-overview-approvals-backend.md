# Employee Overview — Approval Activity (Plan 3C, backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add `GET /api/v1/employees/{id}/overview/approvals`: the approval requests one employee made (or was invited to) in a period, across leave, attendance corrections, work-area changes, location changes and the Work Management approvals (task creation/edit, objective changes, task status changes, project invitations) — newest first, with status counts.

**Architecture:** Attendance and leave already expose per-employee list reads (`ListMyAsync` / `ListOwnAsync` take an explicit `employeeId`), so they are reused as-is. Work Management's history read is project-scoped, so one new cross-project repository method (`ListRequestedByEmployeeAsync`) is added. One handler merges the sources, applying a per-source permission check, and normalises every status to `pending | approved | rejected | cancelled`.

**Spec:** `docs/superpowers/specs/2026-09-30-employee-detail-redesign-design.md`. Prerequisites: Plan 1 (`IEmployeeReadAccessGuard`) and Plan 2 (`EmployeePeriod`, controller theory test) — both committed.

## Global Constraints

- Repo `HRMS-Backend-v1`, branch `feature/task-subtasks`. Before every commit: `git branch --show-current` and `git status --short`; stage only the task's files. **No git worktrees.**
- Build/test per project. Before **each** `dotnet test`: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue` (PowerShell).
- Period: `EmployeePeriod.Resolve` (default current month, ≤366 days). An item belongs to the period when its **request timestamp** is inside it (UTC day bounds): leave `Request.CreatedAt`, correction `CreatedAt`, work-area/location `RequestedAt`, Work Management `CreatedAt`.
- Subject rule: requests the employee **made**; for project invitations, invitations sent **to** the employee. Approvals the employee *decided* for others are not shown.
- Per-source permission (or viewing your own record): leave → `leave:read`; attendance kinds → `attendance:read`; Work Management kinds → `tasks:read`. A source the caller may not see is silently omitted; if the caller may see **none**, return 403.
- Attendance/leave sources are read with no date filter (their own date filters mean work date / leave dates, not request time) capped at 200 per type, then filtered in memory by request timestamp.
- Approver names exist only for Work Management records (`ApproverId`/`DecidedById` are employee ids resolved with `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`). Leave/attendance rows carry no reliable approver employee id, so `approverName` is `null` for them; invitations also have none.
- Status normalisation: `pending`→pending; `approved`,`accepted`,`applied`→approved; `rejected`,`declined`→rejected; anything else (`cancelled`,`expired`,`outdated`, unknown)→cancelled.
- Items returned: newest 10; counts (`pending`,`approved`,`rejected`,`total`) cover every matching item.
- Existing endpoints and `ListForEmployeeAsync` (project-scoped) untouched.
- Commit messages end with: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`

---

### Task 1: Cross-project Work Management approvals read

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalHistoryRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalHistoryRepository.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/EfWorkApprovalHistoryRequestedByTests.cs`

**Interfaces:**
- Produces: `IWorkApprovalHistoryRepository.ListRequestedByEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct = default) : Task<IReadOnlyList<WorkApprovalHistoryRecord>>` — same record type as the existing method; kinds `task_creation`, `task_edit`, `objective_edit | allocation_extend | objective_change`, `task_status_change`, `objective_invitation`. (No other implementers/fakes exist; only `EfWorkApprovalHistoryRepository`.)

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfWorkApprovalHistoryRequestedByTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private static readonly DateTimeOffset InWindow = DateTimeOffset.Parse("2026-09-10T00:00:00+00:00");
    private static readonly DateTimeOffset BeforeWindow = DateTimeOffset.Parse("2026-07-01T00:00:00+00:00");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2026-09-01T00:00:00+00:00");
    private static readonly DateTimeOffset ToExclusive = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00");

    private Objective Obj(bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Title = isDefault ? "Root" : "Checkout",
        OwnerId = Guid.NewGuid(), IsDefault = isDefault, IsActive = true
    };

    [Fact]
    public async Task ListRequestedBy_ReturnsEachKindTheEmployeeRequested_AndNothingElse()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        var root = Obj(isDefault: true);
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id,
            StatusId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), ShortId = "WEB-1", Title = "Fix cart"
        };
        db.Objectives.AddRange(objective, root);
        db.WorkTasks.Add(task);

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        db.TaskCreationRequests.AddRange(
            new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId },
            new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _otherEmployeeId });
        db.TaskEditRequests.Add(new TaskEditRequest { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, RequestedByEmployeeId = _employeeId });
        db.ObjectiveChangeRequests.Add(new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.Edit,
            RequestedById = _employeeId, ReportingManagerId = Guid.NewGuid()
        });
        db.TaskStatusChangeRequests.Add(new TaskStatusChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, RequestedByEmployeeId = _employeeId
        });
        db.ProjectMemberInvitations.AddRange(
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _employeeId, InvitedById = _otherEmployeeId },
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _otherEmployeeId, InvitedById = _employeeId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(
            new[] { "objective_edit", "objective_invitation", "task_creation", "task_edit", "task_status_change" },
            records.Select(r => r.Kind).OrderBy(k => k).ToArray());
        Assert.All(records, r => Assert.Equal("pending", r.Status));
    }

    [Fact]
    public async Task ListRequestedBy_ExcludesRequestsCreatedOutsideTheWindow()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        db.Objectives.Add(objective);

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        var inside = new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId };
        db.TaskCreationRequests.Add(inside);
        await db.SaveChangesAsync();

        _clock.SetupGet(c => c.UtcNow).Returns(BeforeWindow);
        db.TaskCreationRequests.Add(new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(inside.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListRequestedBy_MapsObjectiveChangeTypesToTheirKinds_AndFillsApproverAndDecision()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        var manager = Guid.NewGuid();
        db.Objectives.Add(objective);
        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        db.ObjectiveChangeRequests.AddRange(
            new ObjectiveChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.ExtendAllocation, RequestedById = _employeeId, ReportingManagerId = manager, Status = ObjectiveChangeRequestStatuses.Approved },
            new ObjectiveChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.Transfer, RequestedById = _employeeId, ReportingManagerId = manager });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        var extend = Assert.Single(records, r => r.Kind == "allocation_extend");
        Assert.Equal("approved", extend.Status);
        Assert.Equal(manager, extend.ApproverId);
        Assert.Equal(manager, extend.DecidedById);
        var transfer = Assert.Single(records, r => r.Kind == "objective_change");
        Assert.Null(transfer.DecidedById);
    }

    private ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, _clock.Object),
            new SoftDeleteInterceptor(_clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
```

> `AuditableEntityInterceptor` stamps `CreatedAt = clock.UtcNow` on every added `BaseEntity`, which is why the tests move the mocked clock between saves instead of setting `CreatedAt` directly.

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfWorkApprovalHistoryRequestedByTests"`
Expected: build FAIL — `ListRequestedByEmployeeAsync` does not exist.

- [ ] **Step 3: Add the interface member**

In `IWorkApprovalHistoryRepository.cs`, inside the interface after `ListForEmployeeAsync`:

```csharp
    /// <summary>Cross-project: requests this employee made (task creation/edit, objective changes,
    /// task status changes) plus project invitations sent to them, created in
    /// fromUtc &lt;= CreatedAt &lt; toUtcExclusive, newest first. For the employee Overview.</summary>
    Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListRequestedByEmployeeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `EfWorkApprovalHistoryRepository`**

Add this method to the class (all needed `using`s are already at the top of the file):

```csharp
    public async Task<IReadOnlyList<WorkApprovalHistoryRecord>> ListRequestedByEmployeeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct = default)
    {
        var taskCreation = await (
            from request in _db.TaskCreationRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, request.ObjectiveId, "task_creation", request.Status, objective.Title, request.PayloadJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var taskEdits = await (
            from request in _db.TaskEditRequests.AsNoTracking()
            join task in _db.WorkTasks.AsNoTracking() on request.TaskId equals task.Id
            join objective in _db.Objectives.AsNoTracking() on task.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, task.ObjectiveId, "task_edit", request.Status, task.Title, request.PayloadJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? objective.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var objectiveChanges = await (
            from request in _db.ObjectiveChangeRequests.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on request.ObjectiveId equals objective.Id
            where request.TenantId == tenantId
                  && request.RequestedById == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id,
                request.ObjectiveId,
                request.RequestType == ObjectiveChangeRequestTypes.ExtendAllocation
                    ? "allocation_extend"
                    : request.RequestType == ObjectiveChangeRequestTypes.Edit
                        ? "objective_edit"
                        : "objective_change",
                request.Status,
                objective.Title,
                request.PayloadJson,
                request.RequestedById,
                request.ReportingManagerId,
                // Same convention as ListForEmployeeAsync: DecidedById historically holds a UserId for
                // objective changes, so the reporting manager (the only actor allowed to decide) is the
                // reliable decision actor once the request is no longer pending.
                request.Status == ObjectiveChangeRequestStatuses.Pending ? null : request.ReportingManagerId,
                null,
                request.CreatedAt,
                request.DecidedAt)
        ).ToListAsync(ct);

        var statusChanges = await (
            from request in _db.TaskStatusChangeRequests.AsNoTracking()
            join root in _db.Objectives.AsNoTracking() on request.ProjectId equals root.ProjectId
            where request.TenantId == tenantId
                  && root.IsDefault
                  && request.RequestedByEmployeeId == employeeId
                  && request.CreatedAt >= fromUtc && request.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                request.Id, root.Id, "task_status_change", request.Status, "Task statuses", request.ChangesJson,
                request.RequestedByEmployeeId, request.DecidedByEmployeeId ?? root.OwnerId,
                request.DecidedByEmployeeId, request.DecisionComment, request.CreatedAt, request.DecidedAt)
        ).ToListAsync(ct);

        var invitations = await (
            from invitation in _db.ProjectMemberInvitations.AsNoTracking()
            join objective in _db.Objectives.AsNoTracking() on invitation.ObjectiveId equals objective.Id
            where invitation.TenantId == tenantId
                  && invitation.InvitedEmployeeId == employeeId
                  && invitation.CreatedAt >= fromUtc && invitation.CreatedAt < toUtcExclusive
            select new WorkApprovalHistoryRecord(
                invitation.Id, invitation.ObjectiveId, "objective_invitation", invitation.Status, objective.Title, null,
                invitation.InvitedById, invitation.InvitedEmployeeId,
                invitation.Status == ProjectInvitationStatuses.Pending ? null : invitation.InvitedEmployeeId,
                null, invitation.CreatedAt, invitation.DecidedAt)
        ).ToListAsync(ct);

        return taskCreation
            .Concat(taskEdits)
            .Concat(objectiveChanges)
            .Concat(statusChanges)
            .Concat(invitations)
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
    }
```

- [ ] **Step 5: Run to verify it passes**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~EfWorkApprovalHistoryRequestedByTests"`
Expected: 3 passed. (If a required-property error appears for one of the test entities, set that property in the test's object initializer — the tests build entities with only the properties the repository reads.)

- [ ] **Step 6: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalHistoryRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalHistoryRepository.cs tests/ONEVO.Tests.Unit/Features/WorkManagement/EfWorkApprovalHistoryRequestedByTests.cs
git commit -m "feat(work): list approval requests made by an employee across projects

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `overview/approvals` query, handler and endpoint

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeApprovalActivityResponses.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQuery.cs`
- Create: `src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity/GetEmployeeApprovalActivityQueryHandler.cs`
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeApprovalActivityQueryHandlerTests.cs`
- Modify (test): `tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs` (one `[InlineData]`)

**Interfaces:**
- Consumes: Task 1; `ILeaveRequestRepository.ListOwnAsync(Guid tenantId, Guid employeeId, LeaveRequestListFilter filter, CancellationToken ct)` → `IReadOnlyList<LeaveRequestListRow(LeaveRequest Request, string LeaveTypeName, string LeaveTypeCode)>`; `IAttendanceCorrectionRepository.ListMyAsync(Guid tenantId, Guid employeeId, DateOnly? from, DateOnly? to, string? status, int skip, int take, CancellationToken ct)`; `IWorkAreaChangeRequestRepository.ListMyAsync(` same shape `)`; `ILocationChangeRequestRepository.ListMyAsync(Guid tenantId, Guid employeeId, string? status, int skip, int take, CancellationToken ct)` (all return `(IReadOnlyList<T> Items, int TotalCount)`); `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync(Guid tenantId, IReadOnlyList<Guid> employeeIds, CancellationToken ct)` → `IReadOnlyDictionary<Guid,string>` (namespace `ONEVO.Application.Features.WorkManagement.Common.Services`).
- Produces:
  - `EmployeeApprovalItem(string Id, string Kind, string Label, string? Detail, string Status, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt, string? ApproverName)`
  - `EmployeeApprovalActivityResponse(DateOnly From, DateOnly To, int Pending, int Approved, int Rejected, int Total, IReadOnlyList<EmployeeApprovalItem> Items)`
  - `GetEmployeeApprovalActivityQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)`
  - `GET /api/v1/employees/{id}/overview/approvals?from&to`
  - Kinds: `leave`, `attendance_correction`, `work_area_change`, `location_change`, `task_creation`, `task_edit`, `objective_edit`, `allocation_extend`, `objective_change`, `task_status_change`, `project_invitation`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class GetEmployeeApprovalActivityQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IAttendanceCorrectionRepository> _corrections = new();
    private readonly Mock<IWorkAreaChangeRequestRepository> _workAreas = new();
    private readonly Mock<ILocationChangeRequestRepository> _locations = new();
    private readonly Mock<IWorkApprovalHistoryRepository> _workApprovals = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    public GetEmployeeApprovalActivityQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(_userId);
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 21));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId });

        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequestListRow>());
        ArrangeCorrections();
        ArrangeWorkAreas();
        ArrangeLocations();
        _workApprovals.Setup(w => w.ListRequestedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkApprovalHistoryRecord>());
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
    }

    private GetEmployeeApprovalActivityQueryHandler CreateHandler() =>
        new(_guard.Object, _employees.Object, _leave.Object, _corrections.Object, _workAreas.Object,
            _locations.Object, _workApprovals.Object, _identity.Object, _user.Object, _clock.Object);

    private void ArrangeCorrections(params AttendanceCorrection[] items) =>
        _corrections.Setup(c => c.ListMyAsync(_tenantId, _employeeId, null, null, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<AttendanceCorrection>)items, items.Length));

    private void ArrangeWorkAreas(params WorkAreaChangeRequest[] items) =>
        _workAreas.Setup(w => w.ListMyAsync(_tenantId, _employeeId, null, null, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<WorkAreaChangeRequest>)items, items.Length));

    private void ArrangeLocations(params LocationChangeRequest[] items) =>
        _locations.Setup(l => l.ListMyAsync(_tenantId, _employeeId, null, 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<LocationChangeRequest>)items, items.Length));

    private void ArrangeWork(params WorkApprovalHistoryRecord[] records) =>
        _workApprovals.Setup(w => w.ListRequestedByEmployeeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

    private void ArrangeLeave(params (string status, string created)[] items) =>
        _leave.Setup(l => l.ListOwnAsync(_tenantId, _employeeId, It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.Select(i => new LeaveRequestListRow(
                new LeaveRequest
                {
                    Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = Guid.NewGuid(),
                    Status = i.status, CreatedAt = DateTimeOffset.Parse(i.created),
                    StartAt = DateTimeOffset.Parse("2026-09-23T00:00:00+00:00"), EndAt = DateTimeOffset.Parse("2026-09-25T00:00:00+00:00")
                },
                "Annual leave", "AL")).ToList());

    private static AttendanceCorrection Correction(string status, string created) => new()
    {
        Id = Guid.NewGuid(), Status = status, CreatedAt = DateTimeOffset.Parse(created),
        WorkDate = new DateOnly(2026, 9, 4), CorrectionType = AttendanceCorrection.TypeClockOut
    };

    private static WorkApprovalHistoryRecord Work(string kind, string status, Guid approver, string created, Guid? decidedBy = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), kind, status, "Checkout", null, Guid.NewGuid(), approver, decidedBy, null,
            DateTimeOffset.Parse(created), null);

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_Forbidden_WhenCallerMaySeeNoSourceAndIsNotViewingSelf()
    {
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_OnlyReadsTheSourcesTheCallerMaySee()
    {
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);
        _user.Setup(u => u.HasPermission("attendance:read")).Returns(true);
        ArrangeCorrections(Correction("pending", "2026-09-05T08:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle(i => i.Kind == "attendance_correction");
        _leave.Verify(l => l.ListOwnAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LeaveRequestListFilter>(), It.IsAny<CancellationToken>()), Times.Never);
        _workApprovals.Verify(w => w.ListRequestedByEmployeeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllowsSelfWithoutAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MergesAllSources_NewestFirst_AndFiltersToThePeriod()
    {
        ArrangeLeave(("pending", "2026-09-12T09:00:00+00:00"), ("approved", "2026-08-30T09:00:00+00:00")); // second is outside
        ArrangeCorrections(Correction("approved", "2026-09-05T08:00:00+00:00"));
        ArrangeWorkAreas(new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(), Status = "rejected", RequestedAt = DateTimeOffset.Parse("2026-09-20T08:00:00+00:00"),
            Date = new DateOnly(2026, 9, 21), CurrentWorkModeName = "Onsite", RequestedWorkModeName = "Remote"
        });
        ArrangeLocations(new LocationChangeRequest { Id = Guid.NewGuid(), Status = "applied", RequestedAt = DateTimeOffset.Parse("2026-09-02T08:00:00+00:00") });
        ArrangeWork(Work("task_creation", "pending", Guid.NewGuid(), "2026-09-15T08:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var v = result.Value!;
        v.Total.Should().Be(5);
        v.Pending.Should().Be(2);
        v.Approved.Should().Be(2);   // approved correction + applied location change
        v.Rejected.Should().Be(1);
        v.Items.Select(i => i.Kind).Should().Equal("work_area_change", "task_creation", "leave", "attendance_correction", "location_change");
        v.Items.Select(i => i.RequestedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Handle_BuildsLabelAndDetailPerKind()
    {
        ArrangeLeave(("pending", "2026-09-12T09:00:00+00:00"));
        ArrangeCorrections(Correction("pending", "2026-09-05T08:00:00+00:00"));
        ArrangeWorkAreas(new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(), Status = "pending", RequestedAt = DateTimeOffset.Parse("2026-09-06T08:00:00+00:00"),
            Date = new DateOnly(2026, 9, 21), CurrentWorkModeName = "Onsite", RequestedWorkModeName = "Remote"
        });

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var byKind = result.Value!.Items.ToDictionary(i => i.Kind);
        byKind["leave"].Label.Should().Be("Leave request");
        byKind["leave"].Detail.Should().Be("Annual leave · 2026-09-23 → 2026-09-25");
        byKind["attendance_correction"].Label.Should().Be("Attendance correction");
        byKind["attendance_correction"].Detail.Should().Be("2026-09-04 · clock out");
        byKind["work_area_change"].Label.Should().Be("Work area change");
        byKind["work_area_change"].Detail.Should().Be("2026-09-21 · Onsite → Remote");
    }

    [Theory]
    [InlineData("accepted", "approved")]
    [InlineData("applied", "approved")]
    [InlineData("declined", "rejected")]
    [InlineData("expired", "cancelled")]
    [InlineData("outdated", "cancelled")]
    [InlineData("something_new", "cancelled")]
    public async Task Handle_NormalisesStatuses(string raw, string expected)
    {
        ArrangeWork(Work("task_status_change", raw, Guid.NewGuid(), "2026-09-10T08:00:00+00:00"));

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.Value!.Items.Single().Status.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_NamesTheApproverForWorkRecords_ButNotForInvitations()
    {
        var approver = Guid.NewGuid();
        var decider = Guid.NewGuid();
        ArrangeWork(
            Work("task_creation", "pending", approver, "2026-09-10T08:00:00+00:00"),
            Work("task_edit", "approved", approver, "2026-09-09T08:00:00+00:00", decidedBy: decider),
            Work("objective_invitation", "pending", _employeeId, "2026-09-08T08:00:00+00:00"));
        _identity.Setup(i => i.ResolveDisplayNamesByEmployeeIdAsync(_tenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [approver] = "Abitha Devendran", [decider] = "Grace Hopper" });

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        var byKind = result.Value!.Items.ToDictionary(i => i.Kind);
        byKind["task_creation"].ApproverName.Should().Be("Abitha Devendran");
        byKind["task_edit"].ApproverName.Should().Be("Grace Hopper");
        byKind["project_invitation"].Label.Should().Be("Project invitation");
        byKind["project_invitation"].ApproverName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsAtMostTenItems_ButCountsEverything()
    {
        ArrangeWork(Enumerable.Range(1, 12)
            .Select(i => Work("task_edit", i % 2 == 0 ? "approved" : "pending", Guid.NewGuid(), $"2026-09-{i:00}T08:00:00+00:00"))
            .ToArray());

        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, From, To), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(10);
        result.Value.Total.Should().Be(12);
        result.Value.Pending.Should().Be(6);
        result.Value.Approved.Should().Be(6);
    }

    [Fact]
    public async Task Handle_Returns400_ForAnInvalidPeriod()
    {
        var result = await CreateHandler().Handle(new GetEmployeeApprovalActivityQuery(_employeeId, To, From), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~GetEmployeeApprovalActivityQueryHandlerTests"`
Expected: build FAIL — query/handler/response missing.

- [ ] **Step 3: Write DTOs and query**

`EmployeeApprovalActivityResponses.cs`:

```csharp
namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Status is one of pending | approved | rejected | cancelled. ApproverName is only known
/// for Work Management requests.</summary>
public sealed record EmployeeApprovalItem(
    string Id,
    string Kind,
    string Label,
    string? Detail,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    string? ApproverName);

public sealed record EmployeeApprovalActivityResponse(
    DateOnly From,
    DateOnly To,
    int Pending,
    int Approved,
    int Rejected,
    int Total,
    IReadOnlyList<EmployeeApprovalItem> Items);
```

`GetEmployeeApprovalActivityQuery.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;

public sealed record GetEmployeeApprovalActivityQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeApprovalActivityResponse>>;
```

- [ ] **Step 4: Write the handler**

`GetEmployeeApprovalActivityQueryHandler.cs`:

```csharp
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;

/// <summary>
/// "Approval Activity" for one employee: requests they made (or project invitations sent to them)
/// across leave, attendance and Work Management, newest first. Each source is included only if the
/// caller holds that module's permission or is viewing their own record.
/// </summary>
public sealed class GetEmployeeApprovalActivityQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ILeaveRequestRepository leave,
    IAttendanceCorrectionRepository corrections,
    IWorkAreaChangeRequestRepository workAreas,
    ILocationChangeRequestRepository locations,
    IWorkApprovalHistoryRepository workApprovals,
    ICallerIdentityResolver identity,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeApprovalActivityQuery, Result<EmployeeApprovalActivityResponse>>
{
    public const int MaxItems = 10;
    private const int PerSourceCap = 200;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["leave"] = "Leave request",
        ["attendance_correction"] = "Attendance correction",
        ["work_area_change"] = "Work area change",
        ["location_change"] = "Location change",
        ["task_creation"] = "Task creation",
        ["task_edit"] = "Task edit",
        ["objective_edit"] = "Objective edit",
        ["allocation_extend"] = "Allocation extension",
        ["objective_change"] = "Objective change",
        ["task_status_change"] = "Task status change",
        ["project_invitation"] = "Project invitation"
    };

    public async Task<Result<EmployeeApprovalActivityResponse>> Handle(
        GetEmployeeApprovalActivityQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employeeId = request.EmployeeId;

        var access = await guard.EnsureCanRead(tenantId, employeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeApprovalActivityResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeApprovalActivityResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var isSelf = caller is not null && caller.Id == employeeId;
        bool Has(string permission) => isSelf || currentUser.HasPermission(permission);
        var canLeave = Has("leave:read");
        var canAttendance = Has("attendance:read");
        var canWork = Has("tasks:read");
        if (!canLeave && !canAttendance && !canWork)
            return Result<EmployeeApprovalActivityResponse>.Forbidden("You do not have access to this employee's approval activity.");

        var fromUtc = new DateTimeOffset(period.Value!.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toUtcExclusive = new DateTimeOffset(period.Value.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        bool InWindow(DateTimeOffset at) => at >= fromUtc && at < toUtcExclusive;

        var items = new List<EmployeeApprovalItem>();

        if (canLeave)
        {
            var rows = await leave.ListOwnAsync(tenantId, employeeId, new LeaveRequestListFilter(null, null, null, null), ct);
            items.AddRange(rows.Where(r => InWindow(r.Request.CreatedAt)).Select(r => new EmployeeApprovalItem(
                r.Request.Id.ToString(), "leave", Labels["leave"],
                $"{r.LeaveTypeName} · {DateOnly.FromDateTime(r.Request.StartAt.UtcDateTime):yyyy-MM-dd} → {DateOnly.FromDateTime(r.Request.EndAt.UtcDateTime):yyyy-MM-dd}",
                NormalizeStatus(r.Request.Status), r.Request.CreatedAt, r.Request.ApprovedAt, null)));
        }

        if (canAttendance)
        {
            var (correctionRows, _) = await corrections.ListMyAsync(tenantId, employeeId, null, null, null, 0, PerSourceCap, ct);
            items.AddRange(correctionRows.Where(c => InWindow(c.CreatedAt)).Select(c => new EmployeeApprovalItem(
                c.Id.ToString(), "attendance_correction", Labels["attendance_correction"],
                $"{c.WorkDate:yyyy-MM-dd} · {c.CorrectionType.Replace('_', ' ')}",
                NormalizeStatus(c.Status), c.CreatedAt, c.ReviewedAt, null)));

            var (workAreaRows, _) = await workAreas.ListMyAsync(tenantId, employeeId, null, null, null, 0, PerSourceCap, ct);
            items.AddRange(workAreaRows.Where(w => InWindow(w.RequestedAt)).Select(w => new EmployeeApprovalItem(
                w.Id.ToString(), "work_area_change", Labels["work_area_change"],
                $"{w.Date:yyyy-MM-dd} · {w.CurrentWorkModeName} → {w.RequestedWorkModeName}",
                NormalizeStatus(w.Status), w.RequestedAt, w.ReviewedAt, null)));

            var (locationRows, _) = await locations.ListMyAsync(tenantId, employeeId, null, 0, PerSourceCap, ct);
            items.AddRange(locationRows.Where(l => InWindow(l.RequestedAt)).Select(l => new EmployeeApprovalItem(
                l.Id.ToString(), "location_change", Labels["location_change"], null,
                NormalizeStatus(l.Status), l.RequestedAt, l.ReviewedAt, null)));
        }

        if (canWork)
        {
            var records = await workApprovals.ListRequestedByEmployeeAsync(tenantId, employeeId, fromUtc, toUtcExclusive, ct);
            var nameIds = records
                .Where(r => r.Kind != "objective_invitation")
                .Select(r => r.DecidedById ?? r.ApproverId)
                .Distinct()
                .ToList();
            var names = nameIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameIds, ct)).ToDictionary(p => p.Key, p => p.Value);

            items.AddRange(records.Select(r =>
            {
                var isInvitation = r.Kind == "objective_invitation";
                var kind = isInvitation ? "project_invitation" : r.Kind;
                string? approver = isInvitation ? null : names.GetValueOrDefault(r.DecidedById ?? r.ApproverId);
                return new EmployeeApprovalItem(
                    r.Id.ToString(), kind, Labels.GetValueOrDefault(kind, "Approval"), r.SubjectTitle,
                    NormalizeStatus(r.Status), r.CreatedAt, r.DecidedAt, approver);
            }));
        }

        var ordered = items.OrderByDescending(i => i.RequestedAt).ToList();
        return Result<EmployeeApprovalActivityResponse>.Success(new EmployeeApprovalActivityResponse(
            period.Value.From,
            period.Value.To,
            ordered.Count(i => i.Status == "pending"),
            ordered.Count(i => i.Status == "approved"),
            ordered.Count(i => i.Status == "rejected"),
            ordered.Count,
            ordered.Take(MaxItems).ToList()));
    }

    private static string NormalizeStatus(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pending" => "pending",
        "approved" or "accepted" or "applied" => "approved",
        "rejected" or "declined" => "rejected",
        _ => "cancelled"
    };
}
```

- [ ] **Step 5: Run to verify the handler tests pass**

Run: same command as Step 2. Expected: 15 passed (9 facts + 6 theory rows). If the `ArrangeCorrections`/`ArrangeWorkAreas`/`ArrangeLocations` `ReturnsAsync((IReadOnlyList<T>, int))` tuple form does not compile in your Moq version, build the tuple explicitly: `.ReturnsAsync((Items: (IReadOnlyList<T>)items, TotalCount: items.Length))`.

- [ ] **Step 6: Extend the architecture theory, watch it fail, add the action**

Add to the `[Theory]` in `EmployeesControllerArchitectureTests.cs`:

```csharp
    [InlineData("overview/approvals", "GetOverviewApprovals")]
```

Run `Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture --filter "FullyQualifiedName~OverviewWidgetActions"` → expected FAIL for the new row.

In `EmployeesController.cs` add the using

```csharp
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
```

and after `GetOverviewActivity`:

```csharp
    /// <summary>Overview approval activity: requests the employee made in from..to across leave,
    /// attendance and Work Management (newest 10 plus pending/approved/rejected counts). Each source
    /// is included only when the caller may read it or is viewing their own record.</summary>
    [HttpGet("{id:guid}/overview/approvals")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewApprovals(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeApprovalActivityQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Run both suites**

Run:
```powershell
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Architecture
Stop-Process -Name ONEVO.Api -Force -ErrorAction SilentlyContinue; dotnet test tests/ONEVO.Tests.Unit
```
Expected: both fully green.

- [ ] **Step 8: Live check**

Start the API; as a user with `employees:read` + `leave:read` + `attendance:read` + `tasks:read`: `GET /api/v1/employees/{id}/overview/approvals` → 200; items newest first, `total >= items.length`, max 10 items. With only `leave:read`, the response contains only `leave` items. With none of the three on someone else → 403; on your own record → 200. Report what data existed — an employee with no requests legitimately returns zeros and an empty list.

- [ ] **Step 9: Commit**

```bash
git branch --show-current && git status --short
git add src/ONEVO.Application/Features/CoreHr/Employee/DTOs/Responses/EmployeeApprovalActivityResponses.cs src/ONEVO.Application/Features/CoreHr/Employee/Queries/GetEmployeeApprovalActivity src/ONEVO.Api/Controllers/Tenant/CoreHr/EmployeesController.cs tests/ONEVO.Tests.Unit/Features/CoreHr/Employee/GetEmployeeApprovalActivityQueryHandlerTests.cs tests/ONEVO.Tests.Architecture/EmployeesControllerArchitectureTests.cs
git commit -m "feat(people): add employee overview approval activity endpoint

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

- The user's requirement — "project approvals" must be included — is met by the Work Management kinds (task creation/edit, objective edit / allocation extension / change, task status change, project invitation) alongside leave, attendance correction, work-area and location changes.
- Type consistency: `WorkApprovalHistoryRecord` positional order in the handler test (`Id, ObjectiveId, Kind, Status, SubjectTitle, PayloadJson, RequestedById, ApproverId, DecidedById, DecisionComment, CreatedAt, DecidedAt`) matches the repository record; `EmployeeApprovalItem`/`EmployeeApprovalActivityResponse` field order matches the handler's constructor calls and the frontend model (Plan 3 frontend).
- Verified by reading source: existing `ListForEmployeeAsync` query shapes (mirrored), `ListMyAsync`/`ListOwnAsync` signatures, entity property names and status constants, `AuditableEntityInterceptor` stamping `CreatedAt`, `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`. Not compile-checked; not run. Fix compile errors by aligning to real signatures, never by weakening a test.
- Known limits: attendance/leave sources are capped at 200 per type before the in-memory period filter; leave/attendance items show no approver (no reliable approver employee id on those rows); a 100%-progress-only data gap does not apply here. Invitation items are invitations *to* the employee, so "approver" is intentionally absent.
- Not here: the frontend for all Plan 3 widgets (Plans 3A/3B frontend).
