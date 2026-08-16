using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetObjectiveTaskStatusesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Handle_NoObjectiveStatusesYet_CopiesFromProjectTemplate()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByObjectiveIdAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>());
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", DisplayOrder = 2, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow }
            });

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var handler = new GetObjectiveTaskStatusesQueryHandler(currentUser.Object, objectives.Object, statuses.Object, unitOfWork.Object);
        var result = await handler.Handle(new GetObjectiveTaskStatusesQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        statuses.Verify(x => x.AddRangeAsync(It.Is<IReadOnlyList<TaskStatusEntity>>(list => list.Count == 4 && list.All(s => s.ObjectiveId == ObjectiveId)), It.IsAny<CancellationToken>()), Times.Once);
    }
}
