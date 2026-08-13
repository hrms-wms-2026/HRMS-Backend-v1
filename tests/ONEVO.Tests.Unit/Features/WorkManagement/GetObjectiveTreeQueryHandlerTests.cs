using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveTree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveTreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveTreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, IReadOnlyList<Objective>? tree = null, bool isMember = true,
        bool hasDirectMembership = true, List<Guid>? ownedObjectiveIds = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(isMember);
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, UserId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasDirectMembership);
        members.Setup(x => x.GetActiveObjectiveIdsForUserInProjectAsync(TenantId, ProjectId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownedObjectiveIds ?? new List<Guid>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTreeByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree ?? []);

        var handler = new GetObjectiveTreeQueryHandler(currentUser.Object, projects.Object, members.Object, objectives.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_ActiveMember_ReturnsTree()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_NotAMember_ReturnsForbidden()
    {
        var (handler, _) = BuildHandler(ActiveProject(), isMember: false, hasDirectMembership: true, ownedObjectiveIds: new List<Guid>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null, isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MilestoneScopedMember_ReturnsOnlyOwnSubtreePlusAncestors()
    {
        var defaultObjective = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true };
        var myMilestone = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };
        var myChild = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = myMilestone.Id, IsActive = true };
        var unrelatedSibling = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective, myMilestone, myChild, unrelatedSibling },
            isMember: true, hasDirectMembership: false, ownedObjectiveIds: new List<Guid> { myMilestone.Id });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var returnedIds = result.Value!.Select(o => o.Id).ToHashSet();
        Assert.Contains(defaultObjective.Id, returnedIds); // ancestor context
        Assert.Contains(myMilestone.Id, returnedIds);       // self
        Assert.Contains(myChild.Id, returnedIds);            // descendant
        Assert.DoesNotContain(unrelatedSibling.Id, returnedIds); // NOT a sibling branch
    }

    [Fact]
    public async Task Handle_DirectMember_StillSeesFullTree()
    {
        var defaultObjective = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true };
        var someMilestone = new Objective { Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id, IsActive = true };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective, someMilestone },
            isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }
}
