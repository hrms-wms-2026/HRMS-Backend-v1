using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.AchieveSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class AchieveSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberUserId = Guid.NewGuid();

    private (AchieveSprintCommandHandler Handler, Sprint Sprint, Mock<INotificationDispatcher> Notifications, Mock<ISprintActivityLogRepository> Logs) Build(
        string startingStatus, Guid? callerEmployeeId = null, bool? callerCanManage = null)
    {
        var resolvedCallerEmployeeId = callerEmployeeId ?? OwnerEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(resolvedCallerEmployeeId);

        var sprint = new Sprint { Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "S1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 14), Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var project = new Project { Id = ProjectId, TenantId = TenantId, Name = "Proj", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var access = new Mock<ISprintAccessService>();
        access.Setup(x => x.CanManageAsync(TenantId, sprint, UserId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerCanManage ?? (resolvedCallerEmployeeId == OwnerEmployeeId));
        access.Setup(x => x.GetAudienceEmployeeIdsAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { MemberEmployeeId });

        var logs = new Mock<ISprintActivityLogRepository>();

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId });

        var notifications = new Mock<INotificationDispatcher>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<SprintResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<SprintResponse>>> op, CancellationToken ct) => op(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AchieveSprintCommandHandler(
            currentUser.Object, identity.Object, sprints.Object, projects.Object,
            access.Object, logs.Object, membership.Object, notifications.Object, unitOfWork.Object);
        return (handler, sprint, notifications, logs);
    }

    [Theory]
    [InlineData(SprintStatuses.Draft)]
    [InlineData(SprintStatuses.Active)]
    [InlineData(SprintStatuses.Complete)]
    public async Task Handle_AnyNonTerminalStatus_MovesToAchieved(string startingStatus)
    {
        var (handler, sprint, _, _) = Build(startingStatus);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Achieved, sprint.Status);
        Assert.NotNull(sprint.AchievedAt);
    }

    [Fact]
    public async Task Handle_Achieve_NotifiesAudience()
    {
        var (handler, _, notifications, _) = Build(SprintStatuses.Complete);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifications.Verify(
            x => x.SendTemplatedAsync(
                TenantId, MemberUserId, "work_sprint_achieved",
                It.Is<IReadOnlyDictionary<string, string>>(p => p["sprintName"] == "S1" && p["objectiveName"] == "Proj"),
                "sprint", SprintId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CannotManage_ReturnsForbidden()
    {
        var (handler, sprint, _, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerCanManage: false);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerCanManageViaTaskModuleOwnership_AchievesSprint()
    {
        // Caller is not the sprint's creator, but CanManageAsync reports them able to manage via
        // task-module ownership - the service's own logic is unit-tested separately, so this only
        // proves the handler defers to its answer.
        var (handler, sprint, _, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerCanManage: true);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SprintStatuses.Achieved, sprint.Status);
    }

    [Fact]
    public async Task Handle_Achieve_WritesAchievedLog()
    {
        var (handler, _, _, logs) = Build(SprintStatuses.Complete);

        await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.Action == SprintActivityActions.Achieved && l.FromStatus == SprintStatuses.Complete && l.ToStatus == SprintStatuses.Achieved),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
