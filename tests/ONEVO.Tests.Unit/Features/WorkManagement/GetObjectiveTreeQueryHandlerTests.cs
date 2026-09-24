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
    public async Task Handle_MilestoneScopedMember_ReturnsFullTree_IsOwnerScopedToOwnedSubtree()
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
        var byId = result.Value!.ToDictionary(o => o.Id);
        // Any active project member sees the WHOLE tree - including sibling branches they don't own.
        Assert.Equal(4, result.Value!.Count);
        Assert.Contains(unrelatedSibling.Id, byId.Keys);
        // IsOwner stays scoped to the owned node + its descendants (drives the UI's editing tools).
        Assert.True(byId[myMilestone.Id].IsOwner);         // self
        Assert.True(byId[myChild.Id].IsOwner);              // descendant (cascade)
        Assert.False(byId[defaultObjective.Id].IsOwner);    // ancestor - view only
        Assert.False(byId[unrelatedSibling.Id].IsOwner);    // sibling branch - view only
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
        // Deliberately NOT a child of defaultObjective — must stay genuinely unrelated to the owned
        // node so the cascade (Part 5) does not reach it.
        var otherNode = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = null,
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
    public async Task Handle_DirectMember_IsOwnerCascadesToDescendantsOfSeparatelyOwnedNode()
    {
        // Caller has direct membership on the default Objective (hasDirectMembership branch) AND separately
        // owns a non-default Objective elsewhere in the tree. That Objective's descendants must show
        // IsOwner == true too, same cascade rule as the non-default-member branch.
        var ownerId = Guid.NewGuid();
        var defaultObjective = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
            Title = "Default", OwnerId = Guid.NewGuid()
        };
        var ownedElsewhere = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = defaultObjective.Id,
            IsActive = true, Title = "Owned Elsewhere", OwnerId = ownerId
        };
        var ownedDescendant = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = ownedElsewhere.Id,
            IsActive = true, Title = "Owned Descendant", OwnerId = Guid.NewGuid()
        };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { defaultObjective, ownedElsewhere, ownedDescendant },
            isMember: true, hasDirectMembership: true, ownedObjectiveIds: new List<Guid> { ownedElsewhere.Id },
            names: new Dictionary<Guid, string> { [ownerId] = "Ada Lovelace" });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byId = result.Value!.ToDictionary(o => o.Id);
        Assert.False(byId[defaultObjective.Id].IsOwner);   // unrelated to the owned subtree
        Assert.True(byId[ownedElsewhere.Id].IsOwner);       // direct membership — unchanged
        Assert.True(byId[ownedDescendant.Id].IsOwner);      // cascade — new behavior this Part adds
    }

    [Fact]
    public async Task Handle_MilestoneScopedMember_IsOwnerCascadesToDescendantsButNotAncestors()
    {
        // 3-level tree: Root (default, ancestor) -> Child (caller's directly-owned node) -> Grandchild (cascade target).
        var ownerId = Guid.NewGuid();
        var root = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, IsActive = true,
            Title = "Root", OwnerId = Guid.NewGuid()
        };
        var child = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = root.Id,
            IsActive = true, Title = "Child", OwnerId = ownerId, Progress = 40m
        };
        var grandchild = new Objective
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = child.Id,
            IsActive = true, Title = "Grandchild", OwnerId = Guid.NewGuid()
        };

        var (handler, _) = BuildHandler(
            ActiveProject(), new List<Objective> { root, child, grandchild },
            isMember: true, hasDirectMembership: false, ownedObjectiveIds: new List<Guid> { child.Id },
            names: new Dictionary<Guid, string> { [ownerId] = "Grace Hopper" });

        var result = await handler.Handle(new GetObjectiveTreeQuery(ProjectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var byId = result.Value!.ToDictionary(o => o.Id);
        Assert.Contains(root.Id, byId.Keys);
        Assert.Contains(child.Id, byId.Keys);
        Assert.Contains(grandchild.Id, byId.Keys);
        Assert.False(byId[root.Id].IsOwner);        // ancestor, view-only — unchanged
        Assert.True(byId[child.Id].IsOwner);         // direct membership — unchanged
        Assert.True(byId[grandchild.Id].IsOwner);    // cascade — new behavior this Part adds
        Assert.Equal(40m, byId[child.Id].Progress);
        Assert.Equal("Grace Hopper", byId[child.Id].OwnerName);
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
