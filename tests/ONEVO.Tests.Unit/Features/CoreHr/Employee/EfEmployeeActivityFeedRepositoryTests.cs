using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using WmTaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
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
        Assert.Equal("WEB-12 Fix cart", rows.Single(r => r.Kind == "task_created").Target);
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
