using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ConvertTaskToSubtaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SourceObjectiveId = Guid.NewGuid();
    private static readonly Guid TargetObjectiveId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid NewParentId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private static WorkTask Task(Guid? parentTaskId = null, Guid? sprintId = null) => new()
    {
        Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = SourceObjectiveId,
        ParentTaskId = parentTaskId, ShortId = "WEB-1", Title = "Task 2", CategoryId = CategoryId,
        StatusId = StatusId, Priority = WorkTaskPriorities.Medium, SprintId = sprintId,
        CompletedHours = 0m, ProgressPercent = 0, CreatedAt = DateTimeOffset.UtcNow
    };

    private static WorkTask NewParent(Guid? parentTaskId = null, Guid? projectId = null, Guid? objectiveId = null) => new()
    {
        Id = NewParentId, TenantId = TenantId, ProjectId = projectId ?? ProjectId,
        ObjectiveId = objectiveId ?? TargetObjectiveId, ParentTaskId = parentTaskId,
        ShortId = "WEB-3", Title = "Task 3", CategoryId = CategoryId, StatusId = StatusId,
        Priority = WorkTaskPriorities.Medium, CompletedHours = 0m, ProgressPercent = 0, CreatedAt = DateTimeOffset.UtcNow
    };

    private static Objective Owned(Guid id, Guid? projectId = null) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = projectId ?? ProjectId, OwnerId = EmployeeId,
        IsActive = true, AllocatedHours = 100m, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow
    };

    private (ConvertTaskToSubtaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks) BuildHandler(
        WorkTask task, WorkTask newParent, Objective targetObjective,
        Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null,
        IReadOnlyList<WorkTask>? children = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? EmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, newParent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(newParent);
        tasks.Setup(x => x.GetTrackedByParentTaskIdAsync(TenantId, task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children ?? new List<WorkTask>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, targetObjective.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetObjective);

        var assignments = new Mock<ITaskAssignmentRepository>();
        assignments.Setup(x => x.GetByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskAssignment>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, targetObjective.Id, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (targetObjective.OwnerId == resolvedCallerEmployeeId));

        var handler = new ConvertTaskToSubtaskCommandHandler(
            currentUser.Object, identity.Object, tasks.Object, objectives.Object, assignments.Object,
            membership.Object, unitOfWork.Object);

        return (handler, tasks);
    }

    private static ConvertTaskToSubtaskCommand Command(Guid? taskId = null, Guid? newParentTaskId = null) =>
        new(taskId ?? TaskId, newParentTaskId ?? NewParentId);

    [Fact]
    public async Task Handle_ValidTarget_ReparentsTaskAndClearsSprintId()
    {
        var (handler, tasks) = BuildHandler(Task(sprintId: SprintId), NewParent(), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewParentId, result.Value!.ParentTaskId);
        Assert.Equal(TargetObjectiveId, result.Value.ObjectiveId);
        Assert.Null(result.Value.SprintId);
    }

    [Fact]
    public async Task Handle_TaskHasOwnSubtasks_FlattensChildrenOntoNewParent()
    {
        var childTask = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = SourceObjectiveId,
            ParentTaskId = TaskId, ShortId = "WEB-2", Title = "Child", CategoryId = CategoryId, StatusId = StatusId,
            Priority = WorkTaskPriorities.Medium, CompletedHours = 0m, ProgressPercent = 0, CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _) = BuildHandler(Task(), NewParent(), Owned(TargetObjectiveId), children: new List<WorkTask> { childTask });

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewParentId, childTask.ParentTaskId);
        Assert.Equal(TargetObjectiveId, childTask.ObjectiveId);
    }

    [Fact]
    public async Task Handle_SelfTarget_ReturnsConflictAndSkipsLookups()
    {
        var (handler, tasks) = BuildHandler(Task(), NewParent(), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(newParentTaskId: TaskId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.GetTrackedByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TargetAlreadyIsASubtask_ReturnsConflict()
    {
        var (handler, _) = BuildHandler(Task(), NewParent(parentTaskId: Guid.NewGuid()), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("A subtask cannot itself have subtasks.", result.Error);
    }

    [Fact]
    public async Task Handle_TargetIsCurrentlyOwnChild_ReturnsConflict()
    {
        // A task currently one of `task`'s own children already has ParentTaskId set, so the
        // one-level-nesting rule rejects it before any cycle-specific check would be needed.
        var (handler, _) = BuildHandler(Task(), NewParent(parentTaskId: TaskId), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("A subtask cannot itself have subtasks.", result.Error);
    }

    [Fact]
    public async Task Handle_TargetInDifferentProject_ReturnsConflict()
    {
        var foreignProjectId = Guid.NewGuid();
        var (handler, tasks) = BuildHandler(Task(), NewParent(projectId: foreignProjectId), Owned(TargetObjectiveId, projectId: foreignProjectId));

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.GetTrackedByParentTaskIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerNotManagerOfTargetObjective_ReturnsForbiddenAndDoesNotMutate()
    {
        var (handler, tasks) = BuildHandler(
            Task(), NewParent(), Owned(TargetObjectiveId), callerEmployeeId: Guid.NewGuid(), callerIsEffectiveManager: false);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        tasks.Verify(x => x.GetTrackedByParentTaskIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Task(), NewParent(), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(taskId: Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TargetNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(Task(), NewParent(), Owned(TargetObjectiveId));

        var result = await handler.Handle(Command(newParentTaskId: Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
