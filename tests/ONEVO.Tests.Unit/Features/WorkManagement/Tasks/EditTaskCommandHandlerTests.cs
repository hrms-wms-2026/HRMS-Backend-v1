using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (EditTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks) Build(decimal allocatedHours, decimal existingSumExcludingThisTask)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var task = new WorkTask { Id = TaskId, TenantId = TenantId, ObjectiveId = ObjectiveId, Title = "Old", ShortId = "T-1", EstimatedHours = 10m, CreatedAt = DateTimeOffset.UtcNow };

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSumExcludingThisTask);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, AllocatedHours = allocatedHours, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var slack = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new EditTaskCommandHandler(currentUser.Object, tasks.Object, objectives.Object, slack, unitOfWork.Object);
        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_IncreaseWithinSlack_Updates()
    {
        var (handler, tasks) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 50m, StoryPoints: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
    }

    [Fact]
    public async Task Handle_IncreaseExceedsSlack_ReturnsConflict()
    {
        var (handler, _) = Build(allocatedHours: 100m, existingSumExcludingThisTask: 40m);
        var result = await handler.Handle(new EditTaskCommand(TaskId, "New Title", null, "medium", null, EstimatedHours: 70m, StoryPoints: null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("\"availableSlackHours\"", result.Error);
        Assert.DoesNotContain("\"AvailableSlackHours\"", result.Error);
    }
}
