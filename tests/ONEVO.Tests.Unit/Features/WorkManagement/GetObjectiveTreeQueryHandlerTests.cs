using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
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
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static Project ActiveProject() => new()
    {
        Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1", CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveTreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Project? project, IReadOnlyList<Objective>? tree = null, bool isMember = true,
        bool hasDirectMembership = true, List<Guid>? ownedObjectiveIds = null,
        IReadOnlyDictionary<Guid, string>? names = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(names ?? new Dictionary<Guid, string>());

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(project);

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(isMember);
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(TenantId, ProjectId, EmployeeId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasDirectMembership);
        members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownedObjectiveIds ?? new List<Guid>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetTreeByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(tree ?? []);

        var handler = new GetObjectiveTreeQueryHandler(currentUser.Object, identity.Object, projects.Object, members.Object, objectives.Object);
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

    [Fact]
    public async Task Handle_DirectMember_IsOwnerTrueOnlyOnDirectlyOwnedNodes()
    {
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();
        var defaultObjective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
            Title = "Default", OwnerId = ownerId, Progress = 12.5m
        };
        var otherNode = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id,
            IsActive = true, Title = "Other", OwnerId = otherOwnerId, Progress = 80m
        };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective, otherNode },
            isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid> { defaultObjective.Id },
            names: new Dictionary<Guid, string> { [ownerId] = "Ada Lovelace" });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byId = result.Value!.ToDictionary(o => o.Id);
        Assert.True(byId[defaultObjective.Id].IsOwner);
        Assert.False(byId[otherNode.Id].IsOwner);
        Assert.Equal(12.5m, byId[defaultObjective.Id].Progress);
        Assert.Equal("Ada Lovelace", byId[defaultObjective.Id].OwnerName);
    }

    [Fact]
    public async Task Handle_MilestoneScopedMember_AncestorNodesHaveIsOwnerFalse()
    {
        var ownerId = Guid.NewGuid();
        var defaultObjective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
            Title = "Default", OwnerId = Guid.NewGuid()
        };
        var myMilestone = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id,
            IsActive = true, Title = "Mine", OwnerId = ownerId, Progress = 40m
        };
        var myChild = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = myMilestone.Id,
            IsActive = true, Title = "Child", OwnerId = Guid.NewGuid()
        };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective, myMilestone, myChild },
            isMember: true, hasDirectMembership: false, ownedObjectiveIds: new List<Guid> { myMilestone.Id },
            names: new Dictionary<Guid, string> { [ownerId] = "Grace Hopper" });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byId = result.Value!.ToDictionary(o => o.Id);
        Assert.Contains(defaultObjective.Id, byId.Keys);
        Assert.Contains(myMilestone.Id, byId.Keys);
        Assert.Contains(myChild.Id, byId.Keys);
        Assert.False(byId[defaultObjective.Id].IsOwner);
        Assert.True(byId[myMilestone.Id].IsOwner);
        Assert.False(byId[myChild.Id].IsOwner);
        Assert.Equal(40m, byId[myMilestone.Id].Progress);
        Assert.Equal("Grace Hopper", byId[myMilestone.Id].OwnerName);
    }

    [Fact]
    public async Task Handle_OwnerIdMissingFromNames_OwnerNameIsNull()
    {
        var defaultObjective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
            Title = "Default", OwnerId = Guid.NewGuid()
        };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective },
            isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid> { defaultObjective.Id },
            names: new Dictionary<Guid, string>());

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Single().OwnerName);
    }
}
