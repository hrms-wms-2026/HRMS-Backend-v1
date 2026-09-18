using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid StatusId = Guid.NewGuid();

    private (EditTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses, TaskStatusEntity Status) Build(
        string statusCategory, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null,
        Guid? statusObjectiveId = null, List<TaskStatusEntity>? siblings = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var status = new TaskStatusEntity
        {
            Id = StatusId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = statusObjectiveId,
            Name = "In Progress", Category = statusCategory,
            MarksTaskComplete = statusCategory == TaskStatusCategories.Done,
            Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow
        };
        var all = new List<TaskStatusEntity>(siblings ?? new List<TaskStatusEntity>()) { status };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, StatusId, It.IsAny<CancellationToken>())).ReturnsAsync(status);
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all);

        var project = new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "Proj", Identifier = "PRJ", CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var defaultObjective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(defaultObjective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var handler = new EditTaskStatusCommandHandler(
            currentUser.Object, identity.Object, statuses.Object, objectives.Object, projects.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses, status);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesVisibilityAndColor()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s =>
            s.Visibility == TaskStatusVisibilities.Private && s.Color == "#7C3AED" && !s.MarksTaskComplete)), Times.Once);
    }

    [Fact]
    public async Task Handle_ChangeToDoneWhenAnotherDoneExists_ReturnsConflict()
    {
        var otherDone = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Done", Category = TaskStatusCategories.Done, MarksTaskComplete = true, Visibility = TaskStatusVisibilities.Private, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, siblings: new List<TaskStatusEntity> { otherDone });
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyDoneRowOutOfDone_ReturnsConflict()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Done);
        var command = new EditTaskStatusCommand(StatusId, "Done", 3, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#2563EB");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyActiveRowOutOfActive_ReturnsConflict()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active);
        var command = new EditTaskStatusCommand(StatusId, "To Do", 0, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MoveOneOfTwoActiveRowsToNotStarted_Succeeds()
    {
        var otherActive = new TaskStatusEntity { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, Name = "Review", Category = TaskStatusCategories.Active, Visibility = TaskStatusVisibilities.Public, CreatedAt = DateTimeOffset.UtcNow };
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, siblings: new List<TaskStatusEntity> { otherActive });
        var command = new EditTaskStatusCommand(StatusId, "To Do", 0, false, null, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(It.Is<TaskStatusEntity>(s => s.Category == TaskStatusCategories.NotStarted)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, callerEmployeeId: OtherEmployeeId);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_StatusHasObjectiveId_ReturnsNotFound()
    {
        var (handler, statuses, _) = Build(TaskStatusCategories.Active, statusObjectiveId: ObjectiveId);
        var command = new EditTaskStatusCommand(StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private, TaskStatusCategories.Active, "#7C3AED");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }
}
