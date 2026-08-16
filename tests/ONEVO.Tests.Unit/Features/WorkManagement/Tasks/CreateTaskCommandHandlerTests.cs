using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultStatusId = Guid.NewGuid();

    private static Objective Owned(decimal allocatedHours) => new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = EmployeeId,
        IsActive = true, AllocatedHours = allocatedHours, CreatedAt = DateTimeOffset.UtcNow
    };

    private (CreateTaskCommandHandler Handler, Mock<IWorkTaskRepository> Tasks) BuildHandler(
        Objective objective, decimal existingAllocationSum)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective);
        objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, Identifier = "WEB", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        projects.Setup(x => x.IncrementAndGetNextTaskNumberAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7L);

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = DefaultStatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow }
            });

        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(TenantId, ObjectiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAllocationSum);

        var slackCalculator = new ObjectiveAllocationSlackCalculator(objectives.Object, tasks.Object);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<WorkTaskResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<WorkTaskResponse>>> op, CancellationToken ct) => op(ct));

        var handler = new CreateTaskCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, projects.Object, tasks.Object,
            statuses.Object, slackCalculator, unitOfWork.Object);
        return (handler, tasks);
    }

    [Fact]
    public async Task Handle_OwnerWithinSlack_CreatesTask()
    {
        var (handler, tasks) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m);
        var command = new CreateTaskCommand(ObjectiveId, "Build the thing", null, "task", "medium", null, EstimatedHours: 30m, StoryPoints: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        tasks.Verify(x => x.AddAsync(It.Is<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(t => t.EstimatedHours == 30m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OwnerExceedsSlack_ReturnsConflictWithAvailableSlack()
    {
        var (handler, tasks) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m);
        var command = new CreateTaskCommand(ObjectiveId, "Too big", null, "task", "medium", null, EstimatedHours: 70m, StoryPoints: null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        tasks.Verify(x => x.AddAsync(It.IsAny<Domain.Features.WorkManagement.Tasks.Entities.WorkTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OwnerWithinSlack_GeneratesProjectPrefixedShortId()
    {
        var (handler, _) = BuildHandler(Owned(allocatedHours: 100m), existingAllocationSum: 40m);
        var result = await handler.Handle(
            new CreateTaskCommand(ObjectiveId, "Build the thing", null, "task", "medium", null, EstimatedHours: 30m, StoryPoints: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("WEB-7", result.Value!.ShortId);
        Assert.Equal(DefaultStatusId, result.Value.StatusId);
    }
}
