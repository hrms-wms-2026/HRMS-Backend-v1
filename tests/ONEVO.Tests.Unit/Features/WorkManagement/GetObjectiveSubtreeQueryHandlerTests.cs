using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveSubtreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid HeadUserId = Guid.NewGuid();
    private static readonly Guid HeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();

    private static Objective Node(Guid id, Guid? parentId, Guid ownerId, bool isDefault = false, bool isActive = true, Guid? reportingManagerId = null) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = parentId,
        IsDefault = isDefault, Title = "N", OwnerId = ownerId, ReportingManagerId = reportingManagerId, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveSubtreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Objective? objective, IReadOnlyList<Objective>? all = null, Guid? callerId = null,
        bool hasReadPermission = true, bool hasMembershipOnAncestor = true, IReadOnlyDictionary<Guid, string>? names = null)
    {
        var resolvedCallerUserId = callerId ?? HeadUserId;
        var resolvedCallerEmployeeId = resolvedCallerUserId == OtherUserId ? OtherEmployeeId : HeadEmployeeId;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(resolvedCallerUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, resolvedCallerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedCallerEmployeeId);
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(names ?? new Dictionary<Guid, string>());

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all ?? []);
        if (objective is not null)
        {
            objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, objective.Id, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
            foreach (var node in all ?? [])
                objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, node.Id, It.IsAny<CancellationToken>())).ReturnsAsync(node);
        }

        var members = new Mock<IProjectMemberRepository>();
        members.Setup(x => x.HasActiveMembershipForAnyObjectiveAsync(
                TenantId, ProjectId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasMembershipOnAncestor);

        var permissionResolver = new Mock<IPermissionResolver>();
        permissionResolver.Setup(x => x.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasReadPermission ? new List<string> { "projects:read" } : new List<string>());

        var handler = new GetObjectiveSubtreeQueryHandler(currentUser.Object, identity.Object, objectives.Object, members.Object, permissionResolver.Object);
        return (handler, objectives);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        var (handler, _) = BuildHandler(null);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_CallerNotHead_ReturnsForbidden()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId, hasReadPermission: false, hasMembershipOnAncestor: false);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NonHeadWithActiveMembershipOnAncestor_ReturnsSuccess()
    {
        var parent = Node(ParentId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadEmployeeId);

        var (handler, _) = BuildHandler(
            objective, all: [parent, objective], callerId: OtherUserId,
            hasReadPermission: false, hasMembershipOnAncestor: true);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectiveId, result.Value!.Objective.Id);
    }

    [Fact]
    public async Task Handle_NonHeadWithNoMembershipAnywhereInChain_ReturnsForbidden()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);

        var (handler, _) = BuildHandler(
            objective, all: [objective], callerId: OtherUserId,
            hasReadPermission: false, hasMembershipOnAncestor: false);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsNullParent()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ParentObjective);
        Assert.Equal(ObjectiveId, result.Value.Objective.Id);
        Assert.Empty(result.Value.Objective.Children);
    }

    [Fact]
    public async Task Handle_HeadWithParentAndDescendants_ReturnsNestedTree()
    {
        var parent = Node(ParentId, parentId: null, ownerId: OtherEmployeeId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadEmployeeId);
        var child = Node(ChildId, parentId: ObjectiveId, ownerId: HeadEmployeeId);
        var grandchild = Node(GrandchildId, parentId: ChildId, ownerId: HeadEmployeeId);

        var (handler, _) = BuildHandler(objective, all: [parent, objective, child, grandchild]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ParentId, result.Value!.ParentObjective!.Id);

        var mappedChild = Assert.Single(result.Value.Objective.Children);
        Assert.Equal(ChildId, mappedChild.Id);

        var mappedGrandchild = Assert.Single(mappedChild.Children);
        Assert.Equal(GrandchildId, mappedGrandchild.Id);
    }

    [Fact]
    public async Task Handle_IncludesInactiveDescendants()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);
        var inactiveChild = Node(ChildId, parentId: ObjectiveId, ownerId: HeadEmployeeId, isActive: false);

        var (handler, _) = BuildHandler(objective, all: [objective, inactiveChild]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        var mappedChild = Assert.Single(result.Value!.Objective.Children);
        Assert.False(mappedChild.IsActive);
    }

    [Fact]
    public async Task Handle_ResolvesOwnerNamesAcrossParentAndDescendants()
    {
        var parentOwnerId = Guid.NewGuid();
        var childOwnerId = Guid.NewGuid();
        var parent = Node(ParentId, parentId: null, ownerId: parentOwnerId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadEmployeeId);
        var child = Node(ChildId, parentId: ObjectiveId, ownerId: childOwnerId);
        var names = new Dictionary<Guid, string>
        {
            [parentOwnerId] = "Parent Owner",
            [childOwnerId] = "Child Owner"
        };

        var (handler, _) = BuildHandler(objective, all: [parent, objective, child], names: names);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.Equal("Parent Owner", result.Value!.ParentObjective!.OwnerName);
        var mappedChild = Assert.Single(result.Value.Objective.Children);
        Assert.Equal("Child Owner", mappedChild.OwnerName);
    }

    [Fact]
    public async Task Handle_IsOwnerReflectsTheCallingUser_NotTheHead()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: OtherEmployeeId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId, hasReadPermission: true);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.Objective.IsOwner);
    }

    [Fact]
    public async Task Handle_CarriesIsAchievedAndAchievedAtOntoSubtreeNodes()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadEmployeeId, isDefault: true);
        objective.IsAchieved = true;
        objective.AchievedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var (handler, _) = BuildHandler(objective, all: [objective]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.True(result.Value!.Objective.IsAchieved);
        Assert.Equal(objective.AchievedAt, result.Value.Objective.AchievedAt);
    }
}
