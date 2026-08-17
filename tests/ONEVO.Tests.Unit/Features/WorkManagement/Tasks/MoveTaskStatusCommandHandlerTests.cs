using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
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
    private static readonly Guid OldStatusId = Guid.NewGuid();
    private static readonly Guid NewStatusId = Guid.NewGuid();

    private (MoveTaskStatusCommandHandler Handler, Objective Objective, WorkTask Task, Mock<ITaskStatusRepository> Statuses) Build(
        Guid callerEmployeeId, bool callerIsMember, TaskStatusEntity newStatus, decimal? estimatedHours = 8m)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var task = new WorkTask
        {
            Id = TaskId,
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            Title = "A",
            ShortId = "T-1",
            StatusId = OldStatusId,
            EstimatedHours = estimatedHours,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var oldStatus = new TaskStatusEntity
        {
            Id = OldStatusId,
            TenantId = TenantId,
            Name = "To Do",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, NewStatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newStatus);
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
            .ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsActiveMemberAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsMember);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new MoveTaskStatusCommandHandler(
            currentUser.Object,
            identity.Object,
            tasks.Object,
            statuses.Object,
            objectives.Object,
            membership.Object,
            unitOfWork.Object);

        return (handler, objective, task, statuses);
    }

    [Fact]
    public async Task Handle_Owner_MovingIntoPrivateStatus_Succeeds()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _) = Build(OwnerEmployeeId, callerIsMember: false, newStatus);

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
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

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
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, task, _) = Build(MemberEmployeeId, callerIsMember: true, newStatus);

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
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, _, _, _) = Build(OutsiderEmployeeId, callerIsMember: false, newStatus);

        var result = await handler.Handle(new MoveTaskStatusCommand(TaskId, NewStatusId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MovingIntoCompleteStatus_RollsUpHoursOntoTaskAndObjective()
    {
        var newStatus = new TaskStatusEntity
        {
            Id = NewStatusId,
            TenantId = TenantId,
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _) = Build(
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
            Name = "Done",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, _) = Build(
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
            Name = "In Process",
            MarksTaskComplete = false,
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, statuses) = Build(
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
            Name = "Verified",
            MarksTaskComplete = true,
            Visibility = TaskStatusVisibilities.Private,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var (handler, objective, task, statuses) = Build(
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
}
