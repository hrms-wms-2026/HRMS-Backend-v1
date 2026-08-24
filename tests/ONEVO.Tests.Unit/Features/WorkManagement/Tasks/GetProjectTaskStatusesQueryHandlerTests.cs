using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetProjectTaskStatusesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_ProjectExists_ReturnsTemplateRowsInDisplayOrder()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", DisplayOrder = 2, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "To Do", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", DisplayOrder = 3, MarksTaskComplete = true, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "In Process", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetProjectTaskStatusesQueryHandler(currentUser.Object, projects.Object, statuses.Object);
        var result = await handler.Handle(new GetProjectTaskStatusesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        Assert.Equal(new[] { "To Do", "In Process", "Review", "Done" }, result.Value!.Select(s => s.Name).ToArray());

        statuses.Verify(x => x.AddRangeAsync(It.IsAny<IReadOnlyList<TaskStatusEntity>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var statuses = new Mock<ITaskStatusRepository>();

        var handler = new GetProjectTaskStatusesQueryHandler(currentUser.Object, projects.Object, statuses.Object);
        var result = await handler.Handle(new GetProjectTaskStatusesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectInactive_ReturnsNotFound()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var inactiveProject = ActiveProject();
        inactiveProject.IsActive = false;

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveProject);

        var statuses = new Mock<ITaskStatusRepository>();

        var handler = new GetProjectTaskStatusesQueryHandler(currentUser.Object, projects.Object, statuses.Object);
        var result = await handler.Handle(new GetProjectTaskStatusesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsTemplateRowsIncludingVisibility()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskStatusEntity>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    ProjectId = ProjectId,
                    Name = "Done",
                    DisplayOrder = 3,
                    MarksTaskComplete = true,
                    Visibility = TaskStatusVisibilities.Private,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            });

        var handler = new GetProjectTaskStatusesQueryHandler(currentUser.Object, projects.Object, statuses.Object);
        var result = await handler.Handle(new GetProjectTaskStatusesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var done = result.Value!.Single(s => s.Name == "Done");
        Assert.Equal(TaskStatusVisibilities.Private, done.Visibility);
    }
}
