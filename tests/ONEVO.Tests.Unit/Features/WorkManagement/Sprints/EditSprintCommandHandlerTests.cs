using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.EditSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class EditSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (EditSprintCommandHandler Handler, Sprint Sprint) Build(string sprintStatus, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null)
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
            Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "Old",
            StartDate = sprintStatus == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 1),
            EndDate = sprintStatus == SprintStatuses.Draft ? null : new DateOnly(2026, 9, 14),
            Status = sprintStatus, CreatedAt = DateTimeOffset.UtcNow
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

        var handler = new EditSprintCommandHandler(currentUser.Object, identity.Object, objectives.Object, sprints.Object, unitOfWork.Object, membership.Object);
        return (handler, sprint);
    }

    [Fact]
    public async Task Handle_ActiveSprint_UpdatesFields()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", sprint.Name);
        Assert.Equal(new DateOnly(2026, 9, 16), sprint.EndDate);
    }

    [Theory]
    [InlineData(SprintStatuses.Complete)]
    [InlineData(SprintStatuses.Achieved)]
    public async Task Handle_TerminalSprint_ReturnsConflict(string status)
    {
        var (handler, sprint) = Build(status);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", sprint.StartDate!.Value, sprint.EndDate!.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Old", sprint.Name);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprint) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", sprint.StartDate!.Value, sprint.EndDate!.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Old", sprint.Name);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_EditsSprint()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor membership - the coordinator's own ancestor-walk
        // logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so this only
        // proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, sprint) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);
        var command = new EditSprintCommand(SprintId, "New Name", "Goal", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 16));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", sprint.Name);
    }

    [Fact]
    public async Task Handle_DraftSprint_DatesProvided_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DraftSprint_NameAndGoalOnly_Succeeds()
    {
        var (handler, sprint) = Build(SprintStatuses.Draft);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", sprint.Name);
        Assert.Equal("Goal", sprint.Goal);
        Assert.Null(sprint.StartDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_DatesProvided_UpdatesThem()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 20));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 5), sprint.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 20), sprint.EndDate);
    }

    [Fact]
    public async Task Handle_ActiveSprint_EndBeforeStart_ReturnsFailure()
    {
        var (handler, sprint) = Build(SprintStatuses.Active);
        var command = new EditSprintCommand(SprintId, "Renamed", "Goal", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 5));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
