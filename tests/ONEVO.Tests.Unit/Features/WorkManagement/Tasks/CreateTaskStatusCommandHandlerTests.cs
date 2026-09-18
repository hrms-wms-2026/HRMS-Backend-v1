using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private (CreateTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(
        Guid callerEmployeeId, bool? callerIsEffectiveManager = null, List<TaskStatusEntity>? existing = null)
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

        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new List<TaskStatusEntity>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<TaskStatusResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<TaskStatusResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new CreateTaskStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_Owner_CreatesActiveStatus()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value!.Name);
        Assert.False(result.Value.MarksTaskComplete);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s =>
            s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null &&
            s.Category == TaskStatusCategories.Active && s.Color == "#2563EB" && !s.MarksTaskComplete), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoneCategory_DerivesMarksTaskCompleteTrue()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Shipped", 5, TaskStatusVisibilities.Private, TaskStatusCategories.Done, "#16A34A", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.MarksTaskComplete);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.MarksTaskComplete), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DoneCategoryWhenDoneAlreadyExists_ReturnsConflict()
    {
        var existingDone = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", Category = TaskStatusCategories.Done, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses) = Build(OwnerEmployeeId, existing: new List<TaskStatusEntity> { existingDone });
        var command = new CreateTaskStatusCommand(ProjectId, "Also Done", 5, TaskStatusVisibilities.Private, TaskStatusCategories.Done, "#16A34A", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(OtherEmployeeId);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.AddAsync(It.IsAny<TaskStatusEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_CreatesStatus()
    {
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new CreateTaskStatusCommand(ProjectId, "Blocked", 4, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB", false, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.AddAsync(It.Is<TaskStatusEntity>(s => s.Name == "Blocked" && s.ProjectId == ProjectId && s.ObjectiveId == null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
