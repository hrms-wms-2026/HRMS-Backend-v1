using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintStatus;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class SetSprintStatusCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (SetSprintStatusCommandHandler Handler, Sprint Sprint) Build(
        string startingStatus, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint
        {
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14),
            Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == resolvedCallerEmployeeId));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new SetSprintStatusCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object, membership.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_CompletedSprint_CanMoveBackToActive()
    {
        var (handler, sprint) = Build(SprintStatuses.Complete);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, SprintStatuses.Active), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
        Assert.Null(sprint.CompletedAt);
        Assert.True(sprint.IsManuallyOverridden);
    }

    [Fact]
    public async Task Handle_AchievedSprint_CanMoveBackToFuture()
    {
        var (handler, sprint) = Build(SprintStatuses.Achieved);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, SprintStatuses.Future), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Future, sprint.Status);
        Assert.Null(sprint.AchievedAt);
    }

    [Fact]
    public async Task Handle_MovingToComplete_SetsCompletedAtWithNoTaskCompletenessGate()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, SprintStatuses.Complete), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.NotNull(sprint.CompletedAt);
    }

    [Fact]
    public async Task Handle_UnrecognizedStatus_Returns422()
    {
        var (handler, _) = Build(SprintStatuses.Active);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, "bogus"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotObjectiveOwner_ReturnsForbidden()
    {
        var (handler, sprint) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, SprintStatuses.Complete), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_SetsStatus()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor membership - the coordinator's own ancestor-walk
        // logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so this only
        // proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, sprint) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new SetSprintStatusCommand(SprintId, SprintStatuses.Complete), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Complete, sprint.Status);
        Assert.True(sprint.IsManuallyOverridden);
    }
}
