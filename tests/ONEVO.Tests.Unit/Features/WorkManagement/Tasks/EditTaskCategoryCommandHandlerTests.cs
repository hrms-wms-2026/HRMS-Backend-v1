using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskCategory;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCategoryCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private (EditTaskCategoryCommandHandler Handler, Mock<ITaskCategoryRepository> Categories) Build(
        Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var category = new TaskCategory
        {
            Id = CategoryId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Name = "Bug",
            DisplayOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var categories = new Mock<ITaskCategoryRepository>();
        categories.Setup(x => x.GetByIdForTenantAsync(TenantId, CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var project = new Project
        {
            Id = ProjectId,
            TenantId = TenantId,
            IsActive = true,
            Name = "Proj",
            Identifier = "PRJ",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var defaultObjective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            OwnerId = OwnerEmployeeId,
            IsActive = true,
            Title = "Obj",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultObjective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var handler = new EditTaskCategoryCommandHandler(
            currentUser.Object, identity.Object, categories.Object, objectives.Object, projects.Object, unitOfWork.Object, membership.Object);
        return (handler, categories);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesCategory()
    {
        var (handler, categories) = Build();
        var command = new EditTaskCategoryCommand(CategoryId, "Feature", 2);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        categories.Verify(x => x.Update(
            It.Is<TaskCategory>(c => c.Name == "Feature" && c.DisplayOrder == 2)), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_UpdatesCategory()
    {
        var (handler, categories) = Build(callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new EditTaskCategoryCommand(CategoryId, "Feature", 2);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        categories.Verify(x => x.Update(
            It.Is<TaskCategory>(c => c.Name == "Feature" && c.DisplayOrder == 2)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, categories) = Build(callerEmployeeId: OtherEmployeeId);
        var command = new EditTaskCategoryCommand(CategoryId, "Feature", 2);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        categories.Verify(x => x.Update(It.IsAny<TaskCategory>()), Times.Never);
    }
}
