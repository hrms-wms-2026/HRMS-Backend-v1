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

        var wiring = new SprintTestWiring(TenantId, ProjectId);
        // "Can manage" is now "is a project member" - the engine decides direct vs request.
        wiring.Members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerCanManage ?? (resolvedCallerEmployeeId == OwnerEmployeeId));
        // Audience = active members of the Modules of the sprint's tasks.
        wiring.Tasks.Setup(x => x.GetBySprintIdAsync(TenantId, SprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.Tasks.Entities.WorkTask> { new() { Id = Guid.NewGuid(), ObjectiveId = Guid.NewGuid(), SprintId = SprintId } });
        wiring.Members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities.ProjectMember> { new() { EmployeeId = MemberEmployeeId } });
        var logs = new Mock<ISprintActivityLogRepository>();

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = MemberUserId });

        var notifications = new Mock<INotificationDispatcher>();

        wiring.Sprints = sprints;
        wiring.Projects = projects;
        wiring.Logs = logs;
        wiring.Membership = membership;
        wiring.Notifications = notifications;
        wiring.Identity = identity;

        var handler = new AchieveSprintCommandHandler(
            currentUser.Object, identity.Object, sprints.Object, wiring.Members.Object, wiring.Writes(), wiring.Submitter());
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
    public async Task Handle_NotProjectMember_ReturnsForbidden()
    {
        var (handler, sprint, _, _) = Build(SprintStatuses.Active, callerEmployeeId: OtherEmployeeId, callerCanManage: false);

        var result = await handler.Handle(new AchieveSprintCommand(SprintId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(SprintStatuses.Active, sprint.Status);
    }

    [Fact]
    public async Task Handle_CallerIsProjectMember_AchievesSprint()
    {
        // Caller is not the sprint's creator but is a project member; the engine (mocked Direct
        // here) decides direct vs request, so the handler lets any project member through.
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
