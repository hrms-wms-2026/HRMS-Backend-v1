using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTaskCategories;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetProjectTaskCategoriesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_ProjectExists_ReturnsCategoriesInDisplayOrder()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveProject());

        var categories = new Mock<ITaskCategoryRepository>();
        categories.Setup(x => x.GetByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskCategory>
            {
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Bug", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow },
                new() { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Feature", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow }
            });

        var handler = new GetProjectTaskCategoriesQueryHandler(currentUser.Object, projects.Object, categories.Object);
        var result = await handler.Handle(new GetProjectTaskCategoriesQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(new[] { "Feature", "Bug" }, result.Value!.Select(c => c.Name).ToArray());
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

        var categories = new Mock<ITaskCategoryRepository>();

        var handler = new GetProjectTaskCategoriesQueryHandler(currentUser.Object, projects.Object, categories.Object);
        var result = await handler.Handle(new GetProjectTaskCategoriesQuery(ProjectId), CancellationToken.None);

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

        var categories = new Mock<ITaskCategoryRepository>();

        var handler = new GetProjectTaskCategoriesQueryHandler(currentUser.Object, projects.Object, categories.Object);
        var result = await handler.Handle(new GetProjectTaskCategoriesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);

        var projects = new Mock<IProjectRepository>();
        var categories = new Mock<ITaskCategoryRepository>();

        var handler = new GetProjectTaskCategoriesQueryHandler(currentUser.Object, projects.Object, categories.Object);
        var result = await handler.Handle(new GetProjectTaskCategoriesQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
