using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class AchieveSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();

    private (AchieveSprintCommandHandler Handler, Sprint Sprint, Mock<INotificationDispatcher> Notifications) Build(
        string startingStatus, Guid? callerEmployeeId = null, bool? callerIsEffectiveManager = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ObjectiveId = ObjectiveId, Name = "S1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, OwnerId = OwnerEmployeeId, IsActive = true, Title = "Obj", CreatedAt = DateTimeOffset.UtcNow };
        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember>
            {
                new() { EmployeeId = MemberEmployeeId, ObjectiveId = ObjectiveId, IsActive = true }
            });

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId });
        // Mirrors direct-owner-only behavior by default so pre-existing tests keep passing
        // unmodified; callerIsEffectiveManager lets a test override this to simulate an
        // ancestor-cascade grant (the coordinator's own ancestor-walk logic is unit-tested
        // separately in MilestoneMembershipCoordinatorTests).
        membership.Setup(x => x.IsEffectiveManagerAsync(TenantId, ObjectiveId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerIsEffectiveManager ?? (objective.OwnerId == resolvedCallerEmployeeId));

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveSprintCommandHandler(
            currentUser.Object, identity.Object, objectives.Object, sprints.Object,
            members.Object, membership.Object, notifications.Object, unitOfWork.Object);
        return (handler, sprint, notifications);
    }

    [Theory]
    [InlineData(SprintStatuses.Future)]
    [InlineData(SprintStatuses.Active)]
    [InlineData(SprintStatuses.Incomplete)]
    public async Task Handle_AnyNonTerminalStatus_MovesToAchieved(string startingStatus)
    {
        var (handler, sprint, _) = Build(startingStatus);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Achieved, sprint.Status);
        Assert.NotNull(sprint.AchievedAt);
    }

    [Fact]
    public async Task Handle_Achieve_NotifiesObjectiveMembers()
    {
        var (handler, _, notifications) = Build(SprintStatuses.Complete);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifications.Verify(
            x => x.SendTemplatedAsync(
                TenantId, MemberUserId, "work_sprint_achieved",
                It.Is<IReadOnlyDictionary<string, string>>(p => p["sprintName"] == "S1" && p["objectiveName"] == "Obj"),
                "sprint", SprintId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerIsEffectiveManagerViaCascade_AchievesSprint()
    {
        // Caller is not this objective's own OwnerId, but IsEffectiveManagerAsync reports them as
        // an effective manager via an ancestor membership - the coordinator's own ancestor-walk
        // logic is unit-tested separately in MilestoneMembershipCoordinatorTests, so this only
        // proves the handler defers to its answer instead of the direct OwnerId check.
        var (handler, sprint, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerIsEffectiveManager: true);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Achieved, sprint.Status);
    }
}
