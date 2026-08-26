using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class PushTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (
        PushTaskCommandHandler Handler,
        List<TaskClockingSession> Sessions,
        List<TaskPercentageLog> PercentageLogs,
        Mock<IWorkTaskRepository> Tasks,
        Guid CallerEmployeeId,
        WorkTask Task,
        TaskClockingSession? OpenSession) ArrangePushHandlerWithOpenSession(
        bool sessionOwnedByCaller,
        int taskCurrentPercent,
        int clockedInMinutesAgo,
        bool hasOpenSession = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var task = new WorkTask
        {
            Id = TaskId, TenantId = TenantId, Title = "Task", ProgressPercent = taskCurrentPercent,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var openSession = hasOpenSession
            ? new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId,
                EmployeeId = sessionOwnedByCaller ? CallerEmployeeId : OtherEmployeeId,
                ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-clockedInMinutesAgo)
            }
            : null;
        var sessions = new List<TaskClockingSession>();
        var sessionRepository = new Mock<ITaskClockingSessionRepository>();
        sessionRepository.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        sessionRepository.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, CancellationToken _) => openSession);
        sessionRepository.Setup(x => x.AddAsync(It.IsAny<TaskClockingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TaskClockingSession, CancellationToken>((session, _) => sessions.Add(session))
            .Returns(Task.CompletedTask);

        var percentageLogs = new List<TaskPercentageLog>();
        var percentageLogRepository = new Mock<ITaskPercentageLogRepository>();
        percentageLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskPercentageLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskPercentageLog, CancellationToken>((log, _) => percentageLogs.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new PushTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, sessionRepository.Object,
            percentageLogRepository.Object, unitOfWork.Object);
        return (handler, sessions, percentageLogs, tasks, CallerEmployeeId, task, openSession);
    }

    [Fact]
    public async Task Handle_PercentGreaterThanCurrent_ClosesSessionAndLogsPushPercentage()
    {
        var (handler, _, percentageLogs, _, callerEmployeeId, task, openSession) =
            ArrangePushHandlerWithOpenSession(true, 30, 45);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, "made progress"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value!.ProgressPercent);
        Assert.NotNull(openSession!.ClockOutAt);
        Assert.True(openSession.DurationMinutes >= 44 && openSession.DurationMinutes <= 46);
        var logged = Assert.Single(percentageLogs);
        Assert.Equal(TaskPercentageLogSources.Push, logged.Source);
        Assert.Equal(openSession.Id, logged.ClockingSessionId);
        Assert.Equal(callerEmployeeId, logged.EmployeeId);
        Assert.Equal(30, logged.PreviousPercent);
        Assert.Equal(60, logged.NewPercent);
    }

    [Fact]
    public async Task Handle_PercentNotGreaterThanCurrent_ReturnsBadRequest_AndDoesNotCloseSession()
    {
        var (handler, _, percentageLogs, _, _, task, openSession) =
            ArrangePushHandlerWithOpenSession(true, 30, 10);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 30, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Null(openSession!.ClockOutAt);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_NoOpenSession_ReturnsConflict()
    {
        var (handler, _, percentageLogs, _, _, task, _) =
            ArrangePushHandlerWithOpenSession(true, 30, 10, hasOpenSession: false);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_OpenSessionBelongsToSomeoneElse_ReturnsForbidden()
    {
        var (handler, _, percentageLogs, _, _, task, openSession) =
            ArrangePushHandlerWithOpenSession(false, 30, 10);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 60, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(openSession!.ClockOutAt);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_PercentReaches100_UpdatesTaskTo100()
    {
        var (handler, _, _, _, _, task, _) = ArrangePushHandlerWithOpenSession(true, 90, 5);

        var result = await handler.Handle(new PushTaskCommand(task.Id, 100, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, task.ProgressPercent);
    }
}
