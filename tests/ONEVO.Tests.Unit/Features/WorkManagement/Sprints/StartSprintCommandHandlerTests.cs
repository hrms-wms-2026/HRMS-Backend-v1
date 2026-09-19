using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class StartSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (StartSprintCommandHandler Handler, Sprint Sprint) Build(string startingStatus, Guid? callerEmployeeId = null)
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
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", Goal = "Old goal",
            Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(objective.OwnerId == resolvedCallerEmployeeId);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new StartSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object, membership.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_DraftSprint_BecomesActiveWithDates()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), "New goal");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
        Assert.Equal(new DateOnly(2026, 9, 21), sprint.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 2), sprint.EndDate);
        Assert.Equal("New goal", sprint.Goal);
    }

    [Fact]
    public async Task Handle_NoGoalProvided_KeepsExistingGoal()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Old goal", sprint.Goal);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 21), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SprintStatuses.Draft, sprint.Status);
    }

    [Fact]
    public async Task Handle_SprintNotDraft_ReturnsConflict()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft, callerEmployeeId: OtherEmployeeId);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
