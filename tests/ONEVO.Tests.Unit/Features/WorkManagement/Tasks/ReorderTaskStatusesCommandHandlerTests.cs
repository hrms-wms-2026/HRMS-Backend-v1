using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.ReorderTaskStatuses;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using TaskStatusEntity = ONEVO.Domain.Features.WorkManagement.Tasks.Entities.TaskStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class ReorderTaskStatusesCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Status1 = Guid.NewGuid();
    private static readonly Guid Status2 = Guid.NewGuid();
    private static readonly Guid Status3 = Guid.NewGuid();

    private (ReorderTaskStatusesCommandHandler Handler, List<TaskStatusEntity> Statuses) Build(
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

        // Status3 ("In Process", Active) is a fixed anchor most tests never include in Updates -
        // it keeps the fixture satisfying "at least one Active" while Status1/Status2 are moved
        // around by individual tests, the same way the pre-existing two-row fixture used to work
        // before the Active-minimum invariant existed.
        var statusList = new List<TaskStatusEntity>
        {
            new() { Id = Status1, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "To Do", DisplayOrder = 0, Visibility = TaskStatusVisibilities.Public, Category = TaskStatusCategories.NotStarted, Color = "#94A3B8", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status2, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "Done", DisplayOrder = 2, Visibility = TaskStatusVisibilities.Private, Category = TaskStatusCategories.Done, MarksTaskComplete = true, Color = "#16A34A", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = Status3, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = null, Name = "In Process", DisplayOrder = 1, Visibility = TaskStatusVisibilities.Public, Category = TaskStatusCategories.Active, Color = "#2563EB", CreatedAt = DateTimeOffset.UtcNow }
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetProjectTemplateAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(statusList);
        foreach (var s in statusList)
            statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, s.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<IReadOnlyList<TaskStatusResponse>>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, callerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (callerEmployeeId == OwnerEmployeeId));

        var handler = new ReorderTaskStatusesCommandHandler(currentUser.Object, identity.Object, objectives.Object, projects.Object, statuses.Object, unitOfWork.Object, membership.Object);
        return (handler, statusList);
    }

    [Fact]
    public async Task Handle_ValidReorder_AppliesCategoryAndColor()
    {
        var (handler, statuses) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 1, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8"),
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
        Assert.Equal(TaskStatusVisibilities.Public, statuses.Single(s => s.Id == Status2).Visibility);
        Assert.True(statuses.Single(s => s.Id == Status2).MarksTaskComplete);
        Assert.False(statuses.Single(s => s.Id == Status1).MarksTaskComplete);
    }

    [Fact]
    public async Task Handle_MoveTheDoneRowToActiveWithNoOtherDoneInBatch_ReturnsFailure()
    {
        // Status2 is the only Done row in the whole project; moving it to Active without also
        // promoting some other row to Done leaves the full list with zero Done rows.
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Active, "#2563EB")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TwoDoneEntriesInSameBatch_ReturnsValidationFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A"),
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MoveTheOnlyOtherActiveRowAndTheAnchorOutOfActive_ReturnsFailure()
    {
        // Moves both Status1 (currently NotStarted, left as NotStarted) and the anchor Status3
        // (currently the project's only Active row) out of Active, leaving zero Active rows total.
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status3, 0, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DuplicateStatusIds_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status2, 0, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A"),
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, null!);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NullElementInUpdates_ReturnsFailure()
    {
        var (handler, _) = Build(OwnerEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            null!,
            new(Status2, 1, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, _) = Build(OtherEmployeeId);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 0, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_AppliesAllUpdates()
    {
        var (handler, statuses) = Build(OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new ReorderTaskStatusesCommand(ProjectId, new List<TaskStatusOrderUpdate>
        {
            new(Status1, 1, TaskStatusVisibilities.Public, TaskStatusCategories.NotStarted, "#94A3B8"),
            new(Status2, 2, TaskStatusVisibilities.Public, TaskStatusCategories.Done, "#16A34A")
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, statuses.Single(s => s.Id == Status1).DisplayOrder);
    }
}
