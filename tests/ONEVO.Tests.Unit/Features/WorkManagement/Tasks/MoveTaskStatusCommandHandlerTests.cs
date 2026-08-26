using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class MoveTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid OutsiderEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid OldStatusId = Guid.NewGuid();
    private static readonly Guid NewStatusId = Guid.NewGuid();

        private (
        MoveTaskStatusCommandHandler Handler,
        Objective Objective,
        WorkTask Task,
        Mock<ITaskStatusRepository> Statuses,
        List<TaskStatusChangeLog> StatusChangeLogs,
        List<TaskPercentageLog> PercentageLogs,
        Mock<ITaskClockingSessionRepository> ClockingSessions) Build(

        Guid callerEmployeeId, bool callerIsMember, TaskStatusEntity newStatus, decimal? estimatedHours = 8m,
                bool preserveNullStatusObjectiveId = false, Sprint? sprint = null, bool? callerIsEffectiveManager = null,
        int taskCurrentPercent = 0, bool oldStatusMarksComplete = false,
        bool authenticated = true, bool employeeExists = true, bool taskExists = true,
        bool targetStatusExists = true, bool objectiveExists = true, TaskClockingSession? openSession = null)

    {
        if (!preserveNullStatusObjectiveId && newStatus.ObjectiveId is null)
            newStatus.ObjectiveId = ObjectiveId;

        var currentUser = new Mock<ICurrentUser>();
                currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);

        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? callerEmployeeId : null);

        var task = new WorkTask
        {
            Id = TaskId,
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            ProjectId = ProjectId,
            Title = "A",
            ShortId = "T-1",
            StatusId = OldStatusId,
                        EstimatedHours = estimatedHours,
            ProgressPercent = taskCurrentPercent,
            SprintId = sprint?.Id,

            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskExists ? task : null);

        var oldStatus = new TaskStatusEntity
        {
            Id = OldStatusId,
            TenantId = TenantId,
            Name = "To Do",
                        MarksTaskComplete = oldStatusMarksComplete,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var statuses = new Mock<ITaskStatusRepository>();

                statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, NewStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetStatusExists ? newStatus : null);

        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldStatus);

        var objective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            OwnerId = OwnerEmployeeId,
            IsActive = true,
            Title = "Obj",
            CompletedHours = 0m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objectiveExists ? objective : null);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsMember);
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

                var statusChangeLogs = new List<TaskStatusChangeLog>();
        var statusChangeLogRepository = new Mock<ITaskStatusChangeLogRepository>();
        statusChangeLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskStatusChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskStatusChangeLog, CancellationToken>((log, _) => statusChangeLogs.Add(log))
            .Returns(Task.CompletedTask);

        var percentageLogs = new List<TaskPercentageLog>();
        var percentageLogRepository = new Mock<ITaskPercentageLogRepository>();
        percentageLogRepository.Setup(x => x.AddAsync(It.IsAny<TaskPercentageLog>(), It.IsAny<CancellationToken>()))
            .Callback<TaskPercentageLog, CancellationToken>((log, _) => percentageLogs.Add(log))
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();

        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sprints = new Mock<ISprintRepository>();
        if (sprint is not null)
        {
            sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, sprint.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(sprint);
        }

        var clockingSessions = new Mock<ITaskClockingSessionRepository>();
        clockingSessions.Setup(x => x.GetOpenSessionForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openSession);
        if (openSession is not null)
        {
            clockingSessions.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, openSession.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(openSession);
        }

        var handler = new MoveTaskStatusCommandHandler(
            currentUser.Object,
            identity.Object,
            tasks.Object,
            statuses.Object,
            objectives.Object,
            membership.Object,
                        unitOfWork.Object,
            sprints.Object,
            statusChangeLogRepository.Object,
            percentageLogRepository.Object,
            clockingSessions.Object);

        return (handler, objective, task, statuses, statusChangeLogs, percentageLogs, clockingSessions);

    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, false, newStatus, authenticated: false);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
        Assert.Empty(statusChangeLogs);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, false, newStatus, employeeExists: false);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
        Assert.Empty(statusChangeLogs);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, false, newStatus, taskExists: false);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
        Assert.Empty(statusChangeLogs);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_TargetStatusNotFound_ReturnsNotFound()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, false, newStatus, targetStatusExists: false);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
        Assert.Empty(statusChangeLogs);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_ObjectiveNotFound_ReturnsNotFound()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, false, newStatus, objectiveExists: false);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
        Assert.Empty(statusChangeLogs);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_OnSuccessfulMove_WritesTaskStatusChangeLog()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, _, _) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(statusChangeLogs);
        Assert.Equal(task.Id, logged.TaskId);
        Assert.Equal(OldStatusId, logged.FromStatusId);
        Assert.Equal(newStatus.Id, logged.ToStatusId);
        Assert.Equal(OwnerEmployeeId, logged.EmployeeId);
    }

    [Fact]
    public async Task Handle_WhenMoveMarksTaskComplete_WritesStatusChangePercentageLogTo100()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, percentageLogs, _) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, taskCurrentPercent: 40);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(percentageLogs);
        Assert.Equal(TaskPercentageLogSources.StatusChange, logged.Source);
        Assert.Null(logged.ClockingSessionId);
        Assert.Equal(40, logged.PreviousPercent);
        Assert.Equal(100, logged.NewPercent);
        Assert.Equal(100, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenMoveMarksTaskCompleteWithAnOpenClockingSession_ClosesTheSession()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var clockInAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var openSession = new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId,
            EmployeeId = MemberEmployeeId, ClockInAt = clockInAt
        };
        var (handler, _, task, _, _, _, clockingSessions) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, taskCurrentPercent: 40, openSession: openSession);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(openSession.ClockOutAt);
        Assert.True(openSession.DurationMinutes >= 29);
        // The session stays attributed to whoever originally clocked in, not the status-mover.
        Assert.Equal(MemberEmployeeId, openSession.EmployeeId);
        clockingSessions.Verify(x => x.Update(openSession), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMoveMarksTaskCompleteWithNoOpenClockingSession_DoesNotTouchClockingSessions()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Done", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, _, clockingSessions) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, taskCurrentPercent: 40, openSession: null);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        clockingSessions.Verify(x => x.Update(It.IsAny<TaskClockingSession>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMoveUnmarksCompletion_WritesStatusChangePercentageLogTo0()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, percentageLogs, _) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus,
            taskCurrentPercent: 100, oldStatusMarksComplete: true);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logged = Assert.Single(percentageLogs);
        Assert.Equal(TaskPercentageLogSources.StatusChange, logged.Source);
        Assert.Null(logged.ClockingSessionId);
        Assert.Equal(100, logged.PreviousPercent);
        Assert.Equal(0, logged.NewPercent);
        Assert.Equal(0, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_WhenMoveDoesNotCrossCompletionBoundary_WritesNoPercentageLog()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "In Process", MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, percentageLogs, _) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus, taskCurrentPercent: 30);

        var result = await handler.Handle(new MoveTaskStatusCommand(task.Id, newStatus.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(percentageLogs);
        Assert.Equal(30, task.ProgressPercent);
    }

    [Fact]
    public async Task Handle_TargetStatusBelongsToDifferentProject_ReturnsNotFoundWithoutMovingTask()

    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = Guid.NewGuid(),
            Name = "Other Project",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, _, _) = Build(OwnerEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_TargetStatusIsProjectLevelSameProject_Succeeds()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ObjectiveId = null,
            ProjectId = ProjectId,
            Name = "Project Template",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, _, _) = Build(
            OwnerEmployeeId,
            callerIsMember: false,
            newStatus,
            preserveNullStatusObjectiveId: true);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_Owner_MovingIntoPrivateStatus_Succeeds()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, _, _) = Build(OwnerEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_MemberMovingIntoPublicStatus_Succeeds()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _, _, _, _) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_MemberMovingIntoPrivateStatus_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, _, _, _) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(OldStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_NonMember_ReturnsForbidden()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _, _, _, _) = Build(OutsiderEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PlainMemberOfParentObjective_MovingIntoPrivateStatus_Succeeds()
    {
        // Caller is not this objective's own OwnerId and is not an active member of the exact
        // objective either - but IsEffectiveManagerAsync reports them as an effective manager via
        // an ancestor (parent) membership. This must bypass the whole isMember/Private fallback
        // block, not just the Private check within it - the coordinator's own ancestor-walk logic
        // is unit-tested separately in MilestoneMembershipCoordinatorTests.
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var parentMemberId = Guid.NewGuid();
        var (handler, _, task, _, _, _, _) = Build(
            parentMemberId, callerIsMember: false, newStatus, callerIsEffectiveManager: true);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewStatusId, task.StatusId);
    }

    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_RollsUpHoursOntoTaskAndObjective()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _, _, _, _) = Build(
            OwnerEmployeeId,
            callerIsMember: false,
            newStatus,
            estimatedHours: 8m);
        objective.CompletedHours = 13m;

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8m, task.CompletedHours);
        Assert.Equal(21m, objective.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_WithNullEstimate_RollsUpZeroHours()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _, _, _, _) = Build(
            OwnerEmployeeId,
            callerIsMember: false,
            newStatus,
            estimatedHours: null);
        objective.CompletedHours = 13m;

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Equal(13m, objective.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingOutOfCompleteStatus_ReversesTheRollup()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, statuses, _, _, _) = Build(
            OwnerEmployeeId,
            callerIsMember: false,
            newStatus,
            estimatedHours: 8m);
        task.CompletedHours = 8m;
        objective.CompletedHours = 21m;
        var oldStatusComplete = new TaskStatusEntity
        {
            Id = OldStatusId,
            TenantId = TenantId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldStatusComplete);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, task.CompletedHours);
        Assert.Equal(13m, objective.CompletedHours);
    }

    [Fact]
    public async Task Handle_MovingBetweenCompleteStatuses_LeavesCompletedHoursUnchanged()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Verified",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, statuses, _, _, _) = Build(
            OwnerEmployeeId,
            callerIsMember: false,
            newStatus,
            estimatedHours: 8m);
        task.CompletedHours = 8m;
        objective.CompletedHours = 21m;
        var oldStatusComplete = new TaskStatusEntity
        {
            Id = OldStatusId,
            TenantId = TenantId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, OldStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldStatusComplete);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8m, task.CompletedHours);
        Assert.Equal(21m, objective.CompletedHours);
    }

        [Fact]
    public async Task Handle_BetweenTwoCompleteStatuses_WritesStatusLogButNoPercentageLog()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId, TenantId = TenantId, ProjectId = ProjectId,
            Name = "Verified", MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _, statusChangeLogs, percentageLogs, _) = Build(
            OwnerEmployeeId, callerIsMember: false, newStatus,
            taskCurrentPercent: 100, oldStatusMarksComplete: true);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewStatusId, task.StatusId);
        var statusLog = Assert.Single(statusChangeLogs);
        Assert.Equal(OldStatusId, statusLog.FromStatusId);
        Assert.Equal(NewStatusId, statusLog.ToStatusId);
        Assert.Empty(percentageLogs);
    }

    [Fact]
    public async Task Handle_TaskInAchievedSprint_ReturnsForbidden()

    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var achieved = new Sprint
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            Name = "S1",
            Status = SprintStatuses.Achieved,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _, _, _, _) = Build(OwnerEmployeeId, callerIsMember: false, newStatus, sprint: achieved);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
