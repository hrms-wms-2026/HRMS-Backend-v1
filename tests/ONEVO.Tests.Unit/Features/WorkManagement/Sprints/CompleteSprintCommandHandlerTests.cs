using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
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
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid TargetSprintId = Guid.NewGuid();
    private static readonly Guid DoneStatusId = Guid.NewGuid();
    private static readonly Guid TodoStatusId = Guid.NewGuid();

    private (CompleteSprintCommandHandler Handler, Sprint Sprint, List<WorkTask> Tasks, Mock<IWorkTaskRepository> TaskRepo) Build(IReadOnlyList<WorkTask> tasks)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OwnerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            Status = SprintStatuses.Active, CreatedAt = DateTimeOffset.UtcNow
        };
        var targetSprint = new Sprint
        {
            Id = TargetSprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S2",
            Status = SprintStatuses.Draft, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        sprints.Setup(x => x.GetByIdForTenantAsync(TenantId, TargetSprintId, It.IsAny<CancellationToken>())).ReturnsAsync(targetSprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, OwnerEmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var taskRepo = new Mock<IWorkTaskRepository>();
        taskRepo.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(tasks);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, DoneStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = DoneStatusId, TenantId = TenantId, Name = "Done", MarksTaskComplete = true });
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, TodoStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskStatus { Id = TodoStatusId, TenantId = TenantId, Name = "To Do", MarksTaskComplete = false });

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProjectMember>());

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CompleteSprintCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, sprints.Object, taskRepo.Object, statuses.Object,
            members.Object, membership.Object, notifications.Object, unitOfWork.Object);

        return (handler, sprint, tasks.ToList(), taskRepo);
    }

    private static WorkTask MakeTask(Guid statusId) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = ObjectiveId, SprintId = SprintId,
        StatusId = statusId, Title = "Task", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_AllTasksAlreadyComplete_CompletesWithNoDisposition()
    {
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { MakeTask(DoneStatusId) });

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
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "backlog", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(incomplete.SprintId);
        taskRepo.Verify(x => x.Update(incomplete), Times.Once);
    }

    [Fact]
    public async Task Handle_IncompleteTasksWithSprintDisposition_MovesToTargetSprint()
    {
        var incomplete = MakeTask(TodoStatusId);
        var (handler, sprint, _, taskRepo) = Build(new List<WorkTask> { incomplete });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", TargetSprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetSprintId, incomplete.SprintId);
    }

    [Fact]
    public async Task Handle_SprintDispositionWithNoTargetId_ReturnsFailure()
    {
        var (handler, _, _, _) = Build(new List<WorkTask> { MakeTask(TodoStatusId) });

        var result = await handler.Handle(new CompleteSprintCommand(SprintId, "sprint", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}
