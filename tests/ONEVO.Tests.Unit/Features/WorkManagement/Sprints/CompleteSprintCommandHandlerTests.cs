using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;
using TaskStatus = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class CompleteSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid OtherProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid TargetSprintId = Guid.NewGuid();
    private static readonly Guid DoneStatusId = Guid.NewGuid();
    private static readonly Guid TodoStatusId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();

    private (CompleteSprintCommandHandler Handler, Sprint Sprint, Sprint TargetSprint, List<WorkTask> Tasks, Mock<IWorkTaskRepository> TaskRepo, Mock<INotificationDispatcher> Notifications, Mock<ISprintActivityLogRepository> Logs) Build(
        IReadOnlyList<WorkTask> tasks, Guid? callerEmployeeId = null, bool? callerCanManage = null, bool includeAudienceMember = false,
        string targetSprintStatus = SprintStatuses.Draft, Guid? targetSprintProjectId = null, string sprintStatus = SprintStatuses.Active)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "S1",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            Status = sprintStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var targetSprint = new Sprint
        {
            Id = TargetSprintId, TenantId = TenantId, ProjectId = targetSprintProjectId ?? ProjectId, Name = "S2",
            Status = targetSprintStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, TargetSprintId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSprint);

        var access = new Mock<ISprintAccessService>();
        access.Setup(x => x.CanManageAsync(TenantId, sprint, UserId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerCanManage ?? (resolvedCallerEmployeeId == OwnerEmployeeId));
        access.Setup(x => x.GetAudienceEmployeeIdsAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(includeAudienceMember ? new List<Guid> { MemberEmployeeId } : new List<Guid>());

        var logs = new Mock<ISprintActivityLogRepository>();

        var project = new Project { Id = ProjectId, TenantId = TenantId, Name = "Proj", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        if (includeAudienceMember)
        {
            membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId });
        }

        var taskRepo = new Mock<IWorkTaskRepository>();
        taskRepo.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(tasks);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, DoneStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = DoneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true });
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, TodoStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = TodoStatusId, TenantId = TenantId, Name = "To Do", MarksTaskComplete = false });

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CompleteSprintCommandHandler(
            currentUser.Object, identity.Object, sprints.Object, taskRepo.Object, statuses.Object, projects.Object,
            access.Object, logs.Object, membership.Object, notifications.Object, unitOfWork.Object);

        return (handler, sprint, targetSprint, tasks.ToList(), taskRepo, notifications, logs);
    }

    private static WorkTask MakeTask(Guid statusId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, SprintId = SprintId,
        StatusId = statusId, Title = "Task", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_AllTasksAlreadyComplete_CompletesWithNoDisposition()
    {
        var (handler, sprint, _, _, taskRepo, _, _) = Build(new List<WorkTask> { MakeTask(DoneStatusId) });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.NotNull(sprint.CompletedAt);
        taskRepo.Verify(x => x.Update(It.IsAny<WorkTask>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IncompleteTasksWithBacklogDisposition_ClearsSprintId()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, _, _, taskRepo, _, _) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(incomplete.SprintId);
        taskRepo.Verify(x => x.Update(incomplete), Times.Once);
    }

    [Fact]
    public async Task Handle_IncompleteTasksWithSprintDisposition_MovesToTargetSprint()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, _, _, taskRepo, _, _) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetSprintId, incomplete.SprintId);
    }

    [Fact]
    public async Task Handle_SprintDispositionWithNoTargetId_ReturnsFailure()
    {
        var (handler, _, _, _, _, _, _) = Build(new List<WorkTask> { MakeTask(TodoStatusId) });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CannotManage_ReturnsForbidden()
    {
        var (handler, sprint, _, _, _, _, _) = Build(new List<WorkTask> { MakeTask(DoneStatusId) }, callerEmployeeId: OtherEmployeeId, callerCanManage: false);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerCanManageViaTaskModuleOwnership_CompletesSprint()
    {
        // Caller is not the sprint's creator, but CanManageAsync reports them able to manage via
        // task-module ownership - the service's own logic is unit-tested separately, so this only
        // proves the handler defers to its answer.
        var (handler, sprint, _, _, _, _, _) = Build(
            new List<WorkTask> { MakeTask(DoneStatusId) }, callerEmployeeId: OtherEmployeeId, callerCanManage: true);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
    }

    [Fact]
    public async Task Handle_TargetSprintInDifferentProject_ReturnsFailure()
    {
        var (handler, _, _, _, _, _, _) = Build(new List<WorkTask> { MakeTask(TodoStatusId) }, targetSprintProjectId: OtherProjectId);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TargetSprintInSameProjectFromAnotherModule_IsAccepted()
    {
        // The target sprint belongs to the same project, but (per spec D2) may hold tasks owned by
        // an entirely different module than the sprint being completed - that's fine, since the
        // module-ownership gate lives on task assignment, not sprint-to-sprint moves.
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, targetSprint, _, taskRepo, _, _) = Build(new List<WorkTask> { incomplete }, targetSprintProjectId: ProjectId);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetSprintId, incomplete.SprintId);
        Assert.Equal(ProjectId, targetSprint.ProjectId);
    }

    [Fact]
    public async Task Handle_Complete_NotifiesAudience()
    {
        var (handler, _, _, _, _, notifications, _) = Build(new List<WorkTask> { MakeTask(DoneStatusId) }, includeAudienceMember: true);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifications.Verify(
            x => x.SendTemplatedAsync(
                TenantId, MemberUserId, "work_sprint_completed",
                It.Is<IReadOnlyDictionary<string, string>>(p => p["sprintName"] == "S1" && p["objectiveName"] == "Proj"),
                "sprint", SprintId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Complete_WritesCompletedLog()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, _, _, _, _, _, logs) = Build(new List<WorkTask> { incomplete });

        await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.Action == SprintActivityActions.Completed && l.FromStatus == SprintStatuses.Active && l.ToStatus == SprintStatuses.Complete
            && l.DetailsJson != null && l.DetailsJson.Contains(incomplete.Id.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SprintDispositionWithMovedTasks_WritesTasksAddedLogOnTargetSprint()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, _, _, _, _, _, logs) = Build(new List<WorkTask> { incomplete });

        await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.SprintId == TargetSprintId && l.Action == SprintActivityActions.TasksAdded
            && l.DetailsJson != null && l.DetailsJson.Contains(incomplete.Id.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BacklogDispositionWithMovedTasks_DoesNotWriteTasksAddedLog()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, _, _, _, _, _, logs) = Build(new List<WorkTask> { incomplete });

        await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l => l.Action == SprintActivityActions.TasksAdded), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SprintStatuses.Achieved)]
    [InlineData(SprintStatuses.Draft)]
    [InlineData(SprintStatuses.Complete)]
    public async Task Handle_SprintNotActive_ReturnsConflictAndDoesNothing(string sprintStatus)
    {
        var (handler, sprint, _, _, taskRepo, notifications, logs) = Build(new List<WorkTask> { MakeTask(TodoStatusId) }, sprintStatus: sprintStatus);

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(sprintStatus, sprint.Status);
        taskRepo.Verify(x => x.Update(It.IsAny<WorkTask>()), Times.Never);
        logs.Verify(x => x.AddAsync(It.IsAny<SprintActivityLog>(), It.IsAny<CancellationToken>()), Times.Never);
        notifications.Verify(x => x.SendTemplatedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
