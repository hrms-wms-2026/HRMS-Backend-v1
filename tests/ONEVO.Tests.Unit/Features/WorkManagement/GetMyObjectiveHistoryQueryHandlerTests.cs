using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetMyObjectiveHistory;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetMyObjectiveHistoryQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private (GetMyObjectiveHistoryQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        List<ProjectMember> inactiveMemberships, Objective? objective)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.ListInactiveMembershipsForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(inactiveMemberships);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);

        var handler = new GetMyObjectiveHistoryQueryHandler(currentUser.Object, members.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_HasInactiveMembership_ReturnsHistoryItem()
    {
        var removedAt = DateTimeOffset.UtcNow;
        var membership = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, IsActive = false, RemovedAt = removedAt };
        var objective = new Objective { Id = ObjectiveId, TenantId = TenantId, ProjectId = ProjectId, Title = "Old Milestone", IsAchieved = true };

        var (handler, _) = BuildHandler(new List<ProjectMember> { membership }, objective);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!);
        Assert.Equal(ObjectiveId, item.ObjectiveId);
        Assert.Equal("Old Milestone", item.Title);
        Assert.True(item.IsAchieved);
        Assert.Equal(removedAt, item.RemovedAt);
    }

    [Fact]
    public async Task Handle_NoInactiveMemberships_ReturnsEmptyList()
    {
        var (handler, _) = BuildHandler(new List<ProjectMember>(), null);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ObjectiveNoLongerExists_SkipsItSilently()
    {
        var membership = new ProjectMember { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, UserId = UserId, IsActive = false, RemovedAt = DateTimeOffset.UtcNow };
        var (handler, _) = BuildHandler(new List<ProjectMember> { membership }, null);

        var result = await handler.Handle(new GetMyObjectiveHistoryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }
}
