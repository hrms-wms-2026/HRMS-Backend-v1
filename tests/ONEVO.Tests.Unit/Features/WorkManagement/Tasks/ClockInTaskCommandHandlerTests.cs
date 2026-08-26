using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ClockInTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (ClockInTaskCommandHandler Handler, List<TaskClockingSession> Added, Guid CallerEmployeeId, WorkTask Task) ArrangeClockInHandler(
        bool isAssignee, bool hasOpenSession, int taskProgressPercent)
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
            Id = TaskId, TenantId = TenantId, Title = "Task", ProgressPercent = taskProgressPercent,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskAndEmployeeAsync(TaskId, CallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isAssignee
                ? new TaskAssignment { Id = Guid.NewGuid(), TaskId = TaskId, UserId = UserId, EmployeeId = CallerEmployeeId, AssignedById = CallerEmployeeId, AssignedAt = DateTimeOffset.UtcNow }
                : null);

        var added = new List<TaskClockingSession>();
        var openSession = hasOpenSession
            ? new TaskClockingSession
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId,
                EmployeeId = CallerEmployeeId, ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
            : null;
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        sessions.Setup(x => x.AddAsync(It.IsAny<TaskClockingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TaskClockingSession, CancellationToken>((session, _) => added.Add(session))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ClockInTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, assignments.Object, sessions.Object, unitOfWork.Object);
        return (handler, added, CallerEmployeeId, task);
    }

    [Fact]
    public async Task Handle_AssigneeWithNoOpenSessionAndTaskNotLocked_OpensSession()
    {
        var (handler, sessions, callerEmployeeId, task) = ArrangeClockInHandler(true, false, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var opened = Assert.Single(sessions);
        Assert.Equal(task.Id, opened.TaskId);
        Assert.Equal(callerEmployeeId, opened.EmployeeId);
        Assert.Null(opened.ClockOutAt);
    }

    [Fact]
    public async Task Handle_TaskAlreadyHasOpenSession_ReturnsConflict()
    {
        var (handler, sessions, _, task) = ArrangeClockInHandler(true, true, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskLockedAt100Percent_ReturnsConflict()
    {
        var (handler, sessions, _, task) = ArrangeClockInHandler(true, false, 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_CallerNotAnAssignee_ReturnsForbidden()
    {
        var (handler, sessions, _, task) = ArrangeClockInHandler(false, false, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }
}
