using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;
using ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayDailyTasks;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.CheckIn.Queries;

public class GetTrayDailyTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = new(2026, 10, 13);
    private static readonly DateTimeOffset WindowStart = new(2026, 10, 12, 18, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = WindowStart.AddDays(1);
    private static readonly DateTimeOffset Now = WindowStart.AddHours(10);

    [Fact]
    public async Task Handle_NoDeviceToken_Returns401WithoutQueryingSessions()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(false);
        var sessions = new Mock<ITaskClockingSessionRepository>(MockBehavior.Strict);

        var sut = CreateSut(device, sessions);
        var result = await sut.Handle(new GetTrayDailyTasksQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task Handle_QueriesByEmployeeIdAndLocalDayWindow_NotByUserId()
    {
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(s => s.GetForEmployeeOverlappingAsync(
                TenantId, EmployeeId, WindowStart, WindowEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(AuthenticatedDevice(), sessions);
        var result = await sut.Handle(new GetTrayDailyTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkDate, result.Value!.WorkDate);
        Assert.Empty(result.Value.Tasks);
        sessions.Verify(s => s.GetForEmployeeOverlappingAsync(
            TenantId, EmployeeId, WindowStart, WindowEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SumsSessionsPerTask_AndCountsOpenSessionUntilNow()
    {
        var taskA = Guid.NewGuid();
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(taskA, "Build login page", Now.AddHours(-5), Now.AddHours(-4)),          // 60 min
            Row(taskA, "Build login page", Now.AddMinutes(-30), null),                  // open, 30 min
        };

        var result = await RunWith(rows);

        var task = Assert.Single(result.Tasks);
        Assert.Equal(90, task.MinutesToday);
        Assert.Equal(2, task.SessionCount);
        Assert.True(task.IsClockedInNow);
        Assert.Equal(90, result.TotalMinutes);
    }

    [Fact]
    public async Task Handle_ClipsSessionThatStartedBeforeLocalMidnight()
    {
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(Guid.NewGuid(), "Night fix", WindowStart.AddMinutes(-45), WindowStart.AddMinutes(15)),
        };

        var result = await RunWith(rows);

        Assert.Equal(15, Assert.Single(result.Tasks).MinutesToday);
    }

    [Fact]
    public async Task Handle_DoneStatus_CountsAsDoneWithFullProgress()
    {
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(Guid.NewGuid(), "Ship report", Now.AddHours(-3), Now.AddHours(-2),
                statusName: "Done", category: TaskStatusCategories.Done, progress: 70),
            Row(Guid.NewGuid(), "Review PR", Now.AddHours(-2), Now.AddHours(-1),
                statusName: "In Progress", category: TaskStatusCategories.Active, progress: 40),
        };

        var result = await RunWith(rows);

        Assert.Equal(2, result.TaskCount);
        Assert.Equal(1, result.DoneCount);
        var done = result.Tasks.Single(t => t.Title == "Ship report");
        Assert.True(done.IsDone);
        Assert.Equal(100, done.ProgressPercent);
        var active = result.Tasks.Single(t => t.Title == "Review PR");
        Assert.False(active.IsDone);
        Assert.Equal(40, active.ProgressPercent);
        Assert.Equal("In Progress", active.StatusName);
    }

    [Fact]
    public async Task Handle_OrdersOpenTaskFirst_ThenMostRecent()
    {
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(Guid.NewGuid(), "Morning", Now.AddHours(-6), Now.AddHours(-5)),
            Row(Guid.NewGuid(), "Running", Now.AddHours(-4), null),
            Row(Guid.NewGuid(), "Afternoon", Now.AddHours(-2), Now.AddHours(-1)),
        };

        var result = await RunWith(rows);

        Assert.Equal(["Running", "Afternoon", "Morning"], result.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task Handle_CapsTaskListButKeepsTotals()
    {
        var rows = Enumerable.Range(0, GetTrayDailyTasksQueryHandler.MaxTasks + 5)
            .Select(i => Row(Guid.NewGuid(), $"Task {i}", Now.AddMinutes(-i - 2), Now.AddMinutes(-i - 1)))
            .ToList();

        var result = await RunWith(rows);

        Assert.Equal(GetTrayDailyTasksQueryHandler.MaxTasks, result.Tasks.Count);
        Assert.Equal(GetTrayDailyTasksQueryHandler.MaxTasks + 5, result.TaskCount);
    }

    [Fact]
    public async Task Handle_ConcurrentTasks_TotalCountsWallClockTimeOnce()
    {
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(Guid.NewGuid(), "A", Now.AddMinutes(-60), Now.AddMinutes(-30)),
            Row(Guid.NewGuid(), "B", Now.AddMinutes(-50), Now.AddMinutes(-20)),
            Row(Guid.NewGuid(), "C", Now.AddMinutes(-10), Now),
        };

        var result = await RunWith(rows);

        Assert.Equal(30 + 30 + 10, result.Tasks.Sum(t => t.MinutesToday));
        Assert.Equal(50, result.TotalMinutes); // -60..-20 and -10..0
    }

    [Fact]
    public async Task Handle_DayClockedOut_OpenSessionStopsAtClockOutAndIsNotRunning()
    {
        var dayOut = Now.AddMinutes(-30);
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(Guid.NewGuid(), "Left open", Now.AddMinutes(-90), null),
            Row(Guid.NewGuid(), "After clock-out", Now.AddMinutes(-10), null),
        };

        var result = await RunWith(rows, new AttendanceRecord { ActualStart = Now.AddHours(-8), ActualEnd = dayOut });

        var leftOpen = result.Tasks.Single(t => t.Title == "Left open");
        Assert.Equal(60, leftOpen.MinutesToday);
        Assert.False(leftOpen.IsClockedInNow);
        Assert.Equal(0, result.Tasks.Single(t => t.Title == "After clock-out").MinutesToday);
        Assert.DoesNotContain(result.Tasks, t => t.IsClockedInNow);
        Assert.Equal(60, result.TotalMinutes);
    }

    [Fact]
    public async Task Handle_ClockOutInFuture_TreatsDayAsStillOpen()
    {
        var rows = new List<EmployeeTaskSessionRow> { Row(Guid.NewGuid(), "Running", Now.AddMinutes(-20), null) };

        var result = await RunWith(rows, new AttendanceRecord { ActualStart = Now.AddHours(-2), ActualEnd = Now.AddHours(3) });

        var task = Assert.Single(result.Tasks);
        Assert.True(task.IsClockedInNow);
        Assert.Equal(20, task.MinutesToday);
    }

    [Fact]
    public async Task Handle_ShowsTodaysPushRange_FromFirstPushToLastPush()
    {
        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var rows = new List<EmployeeTaskSessionRow>
        {
            Row(taskA, "Build login page", Now.AddHours(-5), Now.AddHours(-4)),
            Row(taskA, "Build login page", Now.AddHours(-2), Now.AddHours(-1)),
            Row(taskB, "Write tests", Now.AddMinutes(-30), null),
        };
        var pushes = new List<TaskPercentageLog>
        {
            Push(taskA, 40, 55, Now.AddHours(-4)),
            Push(taskA, 55, 70, Now.AddHours(-1)),
        };

        var result = await RunWith(rows, pushes: pushes);

        var a = Assert.Single(result.Tasks, t => t.TaskId == taskA);
        Assert.Equal(40, a.TodayFromPercent);
        Assert.Equal(70, a.TodayToPercent);
        var b = Assert.Single(result.Tasks, t => t.TaskId == taskB);
        Assert.Null(b.TodayFromPercent);
        Assert.Null(b.TodayToPercent);
    }

    private static TaskPercentageLog Push(Guid taskId, int from, int to, DateTimeOffset at) => new()
    {
        TenantId = TenantId,
        TaskId = taskId,
        EmployeeId = EmployeeId,
        PreviousPercent = from,
        NewPercent = to,
        Source = TaskPercentageLogSources.Push,
        ChangedAt = at,
    };

    private static async Task<TrayDailyTasksDto> RunWith(
        IReadOnlyList<EmployeeTaskSessionRow> rows, AttendanceRecord? record = null,
        IReadOnlyList<TaskPercentageLog>? pushes = null)
    {
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(s => s.GetForEmployeeOverlappingAsync(
                TenantId, EmployeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await CreateSut(AuthenticatedDevice(), sessions, record, pushes)
            .Handle(new GetTrayDailyTasksQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static EmployeeTaskSessionRow Row(
        Guid taskId, string title, DateTimeOffset clockIn, DateTimeOffset? clockOut,
        string? statusName = "In Progress", string? category = TaskStatusCategories.Active, int progress = 0)
        => new(taskId, "T-1", title, "Apollo", progress, statusName, "#3B82F6", category,
            StatusMarksTaskComplete: false, TaskCompletedAt: null, clockIn, clockOut);

    private static Mock<ITrayCurrentDevice> AuthenticatedDevice()
    {
        var device = new Mock<ITrayCurrentDevice>();
        device.Setup(d => d.IsAuthenticated).Returns(true);
        device.Setup(d => d.TenantId).Returns(TenantId);
        device.Setup(d => d.UserId).Returns(UserId);
        return device;
    }

    private static GetTrayDailyTasksQueryHandler CreateSut(
        Mock<ITrayCurrentDevice> device, Mock<ITaskClockingSessionRepository> sessions, AttendanceRecord? record = null,
        IReadOnlyList<TaskPercentageLog>? pushes = null)
    {
        var percentageLogs = new Mock<ITaskPercentageLogRepository>();
        percentageLogs.Setup(p => p.GetPushesByEmployeeAsync(
                TenantId, EmployeeId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pushes ?? []);
        var attendance = new Mock<IAttendanceReadRepository>();
        attendance.Setup(a => a.GetRecordAsync(TenantId, EmployeeId, WorkDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Slug = "dapi" });
        var switcher = new Mock<ITenantContextSwitcher>();
        var todayState = new Mock<IAttendanceTodayStateService>();
        todayState.Setup(t => t.ResolveContextAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(BuildContext()));

        return new GetTrayDailyTasksQueryHandler(
            device.Object, todayState.Object, attendance.Object, tenants.Object, switcher.Object, sessions.Object,
            percentageLogs.Object);
    }

    private static AttendanceTodayContext BuildContext() => new(
        new Employee { Id = EmployeeId, TenantId = TenantId, UserId = UserId, LegalEntityId = LegalEntityId },
        new LegalEntity { Id = LegalEntityId, TenantId = TenantId, Timezone = "Asia/Colombo" },
        "Asia/Colombo",
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo"),
        WorkDate,
        Now,
        Now.ToOffset(TimeSpan.FromHours(5.5)),
        new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
        null,
        null,
        "active_employee_work_mode",
        false,
        false,
        new ClockInPolicy { Id = Guid.NewGuid() },
        "configured",
        new AllowedClockInMethods(false, true, false, false, false, null),
        new AttendanceLocalDayWindow(WindowStart, WindowEnd));
}
