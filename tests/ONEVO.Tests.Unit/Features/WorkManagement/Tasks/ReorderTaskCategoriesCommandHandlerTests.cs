using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskCategories;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ReorderTaskCategoriesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Category1 = Guid.NewGuid();
    private static readonly Guid Category2 = Guid.NewGuid();

    private (ReorderTaskCategoriesCommandHandler Handler, List<TaskCategory> Categories) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(callerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var categoryList = new List<TaskCategory>
        {
            new() { Id = Category1, TenantId = TenantId, ProjectId = ProjectId, Name = "Bug", DisplayOrder = 0, CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Category2, TenantId = TenantId, ProjectId = ProjectId, Name = "Feature", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow }
        };
        var categories = new Mock<ITaskCategoryRepository>();
        categories.Setup(x => x.GetByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(categoryList);
        foreach (var c in categoryList)
            categories.Setup(x => x.GetByIdForTenantAsync(TenantId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskCategoryResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskCategoryResponse>>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new ReorderTaskCategoriesCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, categories.Object, unitOfWork.Object, membership.Object);
        return (handler, categoryList);
    }

    [Fact]
    public async Task Handle_ValidUpdates_AppliesAllUpdates()
    {
        var (handler, categories) = Build(OwnerEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            new(Category1, DisplayOrder: 1),
            new(Category2, DisplayOrder: 0)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, categories.Single(c => c.Id == Category1).DisplayOrder);
        Assert.Equal(0, categories.Single(c => c.Id == Category2).DisplayOrder);
    }

    [Fact]
    public async Task Handle_DuplicateCategoryIds_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            new(Category2, 0),
            new(Category2, 1)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, null!);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullElementInUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            null!,
            new(Category2, 1)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnknownCategoryId_ReturnsNotFound()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            new(Guid.NewGuid(), 0)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, _) = Build(OtherEmployeeId);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            new(Category1, 0), new(Category2, 1)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_AppliesAllUpdates()
    {
        var (handler, categories) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new ReorderTaskCategoriesCommand(ProjectId, new List<TaskCategoryOrderUpdate>
        {
            new(Category1, DisplayOrder: 1),
            new(Category2, DisplayOrder: 0)
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, categories.Single(c => c.Id == Category1).DisplayOrder);
        Assert.Equal(0, categories.Single(c => c.Id == Category2).DisplayOrder);
    }
}
