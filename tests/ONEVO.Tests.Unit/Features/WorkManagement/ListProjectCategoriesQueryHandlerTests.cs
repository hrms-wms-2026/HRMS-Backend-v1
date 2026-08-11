using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Queries.ListProjectCategories;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ListProjectCategoriesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Mock<ICurrentUser> AuthenticatedUser(bool authenticated = true, Guid? tenantId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId ?? TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return currentUser;
    }

    [Fact]
    public async Task Handle_Authenticated_ReturnsMappedCategories()
    {
        var currentUser = AuthenticatedUser();
        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetAllForTenantAsync(TenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectCategory> { new() { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Backend", IsActive = true } });

        var handler = new ListProjectCategoriesQueryHandler(currentUser.Object, categories.Object);

        var result = await handler.Handle(new ListProjectCategoriesQuery(false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Backend", result.Value![0].Name);
    }

    [Fact]
    public async Task Handle_PassesIncludeInactiveThrough()
    {
        var currentUser = AuthenticatedUser();
        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.GetAllForTenantAsync(TenantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectCategory>());

        var handler = new ListProjectCategoriesQueryHandler(currentUser.Object, categories.Object);

        var result = await handler.Handle(new ListProjectCategoriesQuery(true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        categories.Verify(x => x.GetAllForTenantAsync(TenantId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var currentUser = AuthenticatedUser(authenticated: false);
        var categories = new Mock<IProjectCategoryRepository>();

        var handler = new ListProjectCategoriesQueryHandler(currentUser.Object, categories.Object);

        var result = await handler.Handle(new ListProjectCategoriesQuery(false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoTenantContext_ReturnsForbidden()
    {
        var currentUser = AuthenticatedUser(tenantId: Guid.Empty);
        var categories = new Mock<IProjectCategoryRepository>();

        var handler = new ListProjectCategoriesQueryHandler(currentUser.Object, categories.Object);

        var result = await handler.Handle(new ListProjectCategoriesQuery(false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
