using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCategory;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCategoryCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateTaskCategoryCommandHandler Handler, Mock<ITaskCategoryRepository> Categories) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerEmployeeId);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var categories = new Mock<ITaskCategoryRepository>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskCategoryResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskCategoryResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new CreateTaskCategoryCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, categories.Object, unitOfWork.Object, membership.Object);
        return (handler, categories);
    }

    [Fact]
    public async Task Handle_Owner_CreatesCategory()
    {
        var (handler, categories) = Build(OwnerEmployeeId);
        var command = new CreateTaskCategoryCommand(ProjectId, "Bug", 1);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bug", result.Value!.Name);
        categories.Verify(x => x.AddAsync(It.Is<TaskCategory>(c => c.Name == "Bug" && c.ProjectId == ProjectId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PlainMemberOfDefaultObjective_CreatesCategory()
    {
        // Category is Project-level configuration any effective manager of the Project's default
        // Objective can change, not just the owner - mirrors Task Status's authorization shape.
        var (handler, categories) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateTaskCategoryCommand(ProjectId, "Feature", 2);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Feature", result.Value!.Name);
        categories.Verify(x => x.AddAsync(It.Is<TaskCategory>(c => c.Name == "Feature" && c.ProjectId == ProjectId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, categories) = Build(OtherEmployeeId);
        var command = new CreateTaskCategoryCommand(ProjectId, "Bug", 1);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        categories.Verify(x => x.AddAsync(It.IsAny<TaskCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
