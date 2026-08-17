using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ReorderTaskStatusesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid Status1 = Guid.NewGuid();
    private static readonly Guid Status2 = Guid.NewGuid();

    private (ReorderTaskStatusesCommandHandler Handler, List<TaskStatusEntity> Statuses) Build(Guid callerEmployeeId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var statusList = new List<TaskStatusEntity>
        {
            new() { Id = Status1, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, MarksTaskComplete = false, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status2, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "Done", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Private, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(statusList);
        foreach (var s in statusList)
            statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ReorderTaskStatusesCommandHandler(currentUser.Object, identity.Object, objectives.Object, statuses.Object, unitOfWork.Object);
        return (handler, statusList);
    }

    [Fact]
    public async Task Handle_ExactlyOneComplete_AppliesAllUpdates()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, DisplayOrder: 1, TaskStatusVisibilities.Public, MarksTaskComplete: false),
            new(Status2, DisplayOrder: 0, TaskStatusVisibilities.Public, MarksTaskComplete: true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
        Assert.Equal(0, statuses.Single(s => s.Id == Status2).DisplayOrder);
        Assert.Equal(TaskStatusVisibilities.Public, statuses.Single(s => s.Id == Status2).Visibility);
    }

    [Fact]
    public async Task Handle_ZeroCompleteStatuses_ReturnsFailure()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, MarksTaskComplete: false),
            new(Status2, 1, TaskStatusVisibilities.Public, MarksTaskComplete: false)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TwoCompleteStatuses_ReturnsFailure()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, MarksTaskComplete: true),
            new(Status2, 1, TaskStatusVisibilities.Public, MarksTaskComplete: true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, null!);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullElementInUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            null!,
            new(Status2, 1, TaskStatusVisibilities.Public, MarksTaskComplete: true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new ReorderTaskStatusesCommand(ObjectiveId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, false), new(Status2, 1, TaskStatusVisibilities.Public, true)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
