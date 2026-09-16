using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.CreateProjectCategory;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class CreateProjectCategoryCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record HandlerSetup(
        CreateProjectCategoryCommandHandler Handler,
        Mock<IProjectCategoryRepository> Categories,
        Mock<IUnitOfWork> UnitOfWork);

    private static HandlerSetup BuildHandler(bool isAuthenticated = true, bool nameExists = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var categories = new Mock<IProjectCategoryRepository>();
        categories.Setup(x => x.ExistsByNameAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nameExists);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateProjectCategoryCommandHandler(currentUser.Object, categories.Object, unitOfWork.Object);

        return new HandlerSetup(handler, categories, unitOfWork);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesCategoryAndReturnsListItem()
    {
        var setup = BuildHandler();

        var result = await setup.Handler.Handle(new CreateProjectCategoryCommand("  Design  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Design", result.Value!.Name);
        setup.Categories.Verify(x => x.AddAsync(
            It.Is<ProjectCategory>(c => c.Name == "Design" && c.TenantId == TenantId && c.IsActive && c.CreatedById == UserId),
            It.IsAny<CancellationToken>()), Times.Once);
        setup.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var setup = BuildHandler(nameExists: true);

        var result = await setup.Handler.Handle(new CreateProjectCategoryCommand("Design"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        setup.Categories.Verify(x => x.AddAsync(It.IsAny<ProjectCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        var setup = BuildHandler(isAuthenticated: false);

        var result = await setup.Handler.Handle(new CreateProjectCategoryCommand("Design"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
