using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskStatus;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
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
    private static readonly Guid StatusId = Guid.NewGuid();

    private (EditTaskStatusCommandHandler Handler, Mock<ITaskStatusRepository> Statuses) Build(
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

        var status = new TaskStatusEntity
        {
            Id = StatusId,
            TenantId = TenantId,
            ObjectiveId = ObjectiveId,
            Name = "In Progress",
            Visibility = TaskStatusVisibilities.Public,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var statuses = new Mock<ITaskStatusRepository>();
        statuses.Setup(x => x.GetByIdForTenantAsync(TenantId, StatusId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var objective = new Objective
        {
            Id = ObjectiveId,
            TenantId = TenantId,
            OwnerId = OwnerEmployeeId,
            IsActive = true,
            Title = "Obj",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (resolvedCallerEmployeeId == OwnerEmployeeId));

        var handler = new EditTaskStatusCommandHandler(
            currentUser.Object, identity.Object, statuses.Object, objectives.Object, unitOfWork.Object, membership.Object);
        return (handler, statuses);
    }

    [Fact]
    public async Task Handle_Owner_UpdatesVisibility()
    {
        var (handler, statuses) = Build();
        var command = new EditTaskStatusCommand(
            StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(
            It.Is<TaskStatusEntity>(s => s.Visibility == TaskStatusVisibilities.Private)), Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, statuses) = Build(callerEmployeeId: OtherEmployeeId);
        var command = new EditTaskStatusCommand(
            StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        statuses.Verify(x => x.Update(It.IsAny<TaskStatusEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaAncestor_UpdatesVisibility()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor (grandparent) membership - the coordinator's own
        // ancestor-walk logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so
        // this only proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, statuses) = Build(callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new EditTaskStatusCommand(
            StatusId, "Review", 2, false, null, TaskStatusVisibilities.Private);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        statuses.Verify(x => x.Update(
            It.Is<TaskStatusEntity>(s => s.Visibility == TaskStatusVisibilities.Private)), Times.Once);
    }
}
