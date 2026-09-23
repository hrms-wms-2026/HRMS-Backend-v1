using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using TaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ClockInTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CurrentStatusId = Guid.NewGuid();

    private (ClockInTaskCommandHandler Handler, List<TaskClockingSession> Added, Guid CallerEmployeeId, WorkTask Task, List<TaskStatusChangeLog> StatusChanges) ArrangeClockInHandler(
        bool isAssignee, bool hasOpenSession, int taskProgressPercent,
        Guid? openSessionEmployeeId = null, bool authenticated = true,
        bool employeeExists = true, bool taskExists = true,
        string currentStatusCategory = TaskStatusCategories.Active,
        IReadOnlyList<TaskStatus>? projectTemplate = null, Mock<IWorkTaskRepository>? taskRepository = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? CallerEmployeeId : null);

        var task = new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, StatusId = CurrentStatusId, Title = "Task", ProgressPercent = taskProgressPercent,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = taskRepository ?? new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

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
                EmployeeId = openSessionEmployeeId ?? CallerEmployeeId, ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
            : null;
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        sessions.Setup(x => x.AddAsync(It.IsAny<TaskClockingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TaskClockingSession, CancellationToken>((session, _) => added.Add(session))
            .Returns(Task.CompletedTask);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, CurrentStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = CurrentStatusId, TenantId = TenantId, ProjectId = ProjectId, Category = currentStatusCategory });
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectTemplate ?? new List<TaskStatus>());
        var statusChangesAdded = new List<TaskStatusChangeLog>();
        var statusChanges = new Mock<ITaskStatusChangeLogRepository>();
        statusChanges.Setup(x => x.AddAsync(It.IsAny<TaskStatusChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskStatusChangeLog, CancellationToken>((log, _) => statusChangesAdded.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<ClockInTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<ClockInTaskResponse>>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ClockInTaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, assignments.Object, sessions.Object, statuses.Object, statusChanges.Object, unitOfWork.Object);
        return (handler, added, CallerEmployeeId, task, statusChangesAdded);
    }

    [Fact]
    public async Task Handle_ActiveStatus_DoesNotMoveOrLogStatusChange()
    {
        var (handler, sessions, _, task, changes) = ArrangeClockInHandler(true, false, 20);
        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.MovedToStatus);
        Assert.Equal(CurrentStatusId, task.StatusId);
        Assert.Empty(changes);
        Assert.Single(sessions);
    }

    [Fact]
    public async Task Handle_AllActiveStatusesPrivate_RejectsWithoutOpeningSessionOrMoving()
    {
        var (handler, sessions, _, task, changes) = ArrangeClockInHandler(true, false, 20,
            currentStatusCategory: TaskStatusCategories.NotStarted, projectTemplate: new List<TaskStatus>
            {
                new() { Id = Guid.NewGuid(), Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Private }
            });
        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("module owner", result.Error);
        Assert.Equal(CurrentStatusId, task.StatusId);
        Assert.Empty(sessions);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Handle_PrivateActiveComesFirst_SkipsItAndUsesLowestOrderedPublicActive()
    {
        var expectedId = Guid.NewGuid();
        var (handler, sessions, _, task, changes) = ArrangeClockInHandler(true, false, 20,
            currentStatusCategory: TaskStatusCategories.NotStarted, projectTemplate: new List<TaskStatus>
            {
                new() { Id = Guid.NewGuid(), Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, DisplayOrder = 5 },
                new() { Id = Guid.NewGuid(), Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Private, DisplayOrder = 0 },
                new() { Id = expectedId, Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, DisplayOrder = 2 }
            });
        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedId, task.StatusId);
        Assert.Equal(expectedId, result.Value!.MovedToStatus!.Id);
        Assert.Single(sessions);
        Assert.Single(changes);
    }

    [Fact]
    public async Task Handle_DoneStatusBelow100Percent_DoesNotAutoMove()
    {
        var (handler, sessions, _, task, changes) = ArrangeClockInHandler(true, false, 20, currentStatusCategory: TaskStatusCategories.Done);
        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.MovedToStatus);
        Assert.Equal(CurrentStatusId, task.StatusId);
        Assert.Empty(changes);
        Assert.Single(sessions);
    }
    [Fact]
    public async Task Handle_AssigneeWithNoOpenSessionAndTaskNotLocked_OpensSession()
    {
        var (handler, sessions, callerEmployeeId, task, _) = ArrangeClockInHandler(true, false, 20);

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
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, true, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskInNotStartedStatus_AutoMovesToFirstPublicActiveStatus()
    {
        var activeStatusId = Guid.NewGuid();
        var tasks = new Mock<IWorkTaskRepository>();
        var template = new List<TaskStatus>
        {
            new() { Id = CurrentStatusId, TenantId = TenantId, ProjectId = ProjectId, Category = TaskStatusCategories.NotStarted, Visibility = TaskStatusVisibilities.Public },
            new() { Id = activeStatusId, TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, Color = "#2563EB" }
        };
        var (handler, _, callerEmployeeId, task, changes) = ArrangeClockInHandler(
            true, false, 20, currentStatusCategory: TaskStatusCategories.NotStarted, projectTemplate: template, taskRepository: tasks);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(activeStatusId, result.Value!.MovedToStatus!.Id);
        Assert.Equal(activeStatusId, task.StatusId);
        tasks.Verify(x => x.Update(task), Times.Once);
        Assert.Equal("In Process", result.Value.MovedToStatus.Name);
        Assert.Equal("#2563EB", result.Value.MovedToStatus.Color);
        var change = Assert.Single(changes);
        Assert.Equal(CurrentStatusId, change.FromStatusId);
        Assert.Equal(activeStatusId, change.ToStatusId);
        Assert.Equal(callerEmployeeId, change.EmployeeId);
    }

    [Fact]
    public async Task Handle_TaskLockedAt100Percent_ReturnsConflict()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_CallerNotAnAssignee_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(false, false, 20);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, authenticated: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, employeeExists: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(true, false, 20, taskExists: false);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_TaskAt99Percent_OpensSession()
    {
        var before = DateTimeOffset.UtcNow;
        var (handler, sessions, callerEmployeeId, task, _) = ArrangeClockInHandler(true, false, 99);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.True(result.IsSuccess);
        var opened = Assert.Single(sessions);
        Assert.Equal(task.Id, opened.TaskId);
        Assert.Equal(callerEmployeeId, opened.EmployeeId);
        Assert.InRange(opened.ClockInAt, before, after);
        Assert.Null(opened.ClockOutAt);
    }

    [Fact]
    public async Task Handle_OpenSessionOwnedByDifferentEmployee_ReturnsConflict()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(
            true, true, 20, openSessionEmployeeId: Guid.NewGuid());

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task Handle_NotAssigneeAndTaskComplete_ReturnsForbiddenBeforeLockCheck()
    {
        var (handler, sessions, _, task, _) = ArrangeClockInHandler(false, false, 100);

        var result = await handler.Handle(new ClockInTaskCommand(task.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(sessions);
    }
}



