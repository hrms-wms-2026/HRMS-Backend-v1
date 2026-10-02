using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

public class StartSprintCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SprintId = Guid.NewGuid();

    private (StartSprintCommandHandler Handler, Sprint Sprint, Mock<ISprintActivityLogRepository> Logs) Build(
        string startingStatus, Guid? callerEmployeeId = null, bool? callerCanManage = null)
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
            Id = SprintId, TenantId = TenantId, ProjectId = ProjectId, Name = "S1", Goal = "Old goal",
            Status = startingStatus, CreatedAt = DateTimeOffset.UtcNow
        };
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, SprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);

        var wiring = new SprintTestWiring(TenantId, ProjectId);
        // "Can manage" is now "is a project member" - the engine decides direct vs request.
        wiring.Members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, resolvedCallerEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerCanManage ?? (resolvedCallerEmployeeId == OwnerEmployeeId));
        var logs = new Mock<ISprintActivityLogRepository>();

        wiring.Sprints = sprints;
        wiring.Logs = logs;
        wiring.Identity = identity;

        var handler = new StartSprintCommandHandler(currentUser.Object, identity.Object, sprints.Object, wiring.Members.Object, wiring.Writes(), wiring.Submitter());
        return (handler, sprint, logs);
    }

    [Fact]
    public async Task Handle_DraftSprint_BecomesActiveWithDates()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Draft);
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
        var (handler, sprint, _) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Old goal", sprint.Goal);
    }

    [Fact]
    public async Task Handle_EndDateBeforeStartDate_ReturnsFailure()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Draft);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 21), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SprintStatuses.Draft, sprint.Status);
    }

    [Fact]
    public async Task Handle_SprintNotDraft_ReturnsConflict()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Active);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotProjectMember_ReturnsForbidden()
    {
        var (handler, sprint, _) = Build(SprintStatuses.Draft, callerEmployeeId: OtherEmployeeId, callerCanManage: false);
        var command = new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_Start_WritesStartedLog()
    {
        var (handler, sprint, logs) = Build(SprintStatuses.Draft);
        await handler.Handle(new StartSprintCommand(SprintId, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 2), null), CancellationToken.None);
        logs.Verify(x => x.AddAsync(It.Is<SprintActivityLog>(l =>
            l.Action == SprintActivityActions.Started && l.FromStatus == SprintStatuses.Draft && l.ToStatus == SprintStatuses.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
