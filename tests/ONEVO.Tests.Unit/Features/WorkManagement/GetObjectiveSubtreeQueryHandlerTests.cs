using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveSubtree;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class GetObjectiveSubtreeQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();

    private static Objective Node(Guid id, Guid? parentId, Guid ownerId, bool isDefault = false, bool isActive = true) => new()
    {
        Id = id, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = parentId,
        IsDefault = isDefault, Title = "N", OwnerId = ownerId, IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private (GetObjectiveSubtreeQueryHandler Handler, Mock<IObjectiveRepository> Objectives) BuildHandler(
        Objective? objective, IReadOnlyList<Objective>? all = null, Guid? callerId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? HeadId);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(objective);
        objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(all ?? []);

        var handler = new GetObjectiveSubtreeQueryHandler(currentUser.Object, objectives.Object);
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
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        var (handler, _) = BuildHandler(objective, all: [objective], callerId: OtherUserId);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjective_ReturnsNullParent()
    {
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
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
        var parent = Node(ParentId, parentId: null, ownerId: OtherUserId, isDefault: true);
        var objective = Node(ObjectiveId, parentId: ParentId, ownerId: HeadId);
        var child = Node(ChildId, parentId: ObjectiveId, ownerId: HeadId);
        var grandchild = Node(GrandchildId, parentId: ChildId, ownerId: HeadId);

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
        var objective = Node(ObjectiveId, parentId: null, ownerId: HeadId, isDefault: true);
        var inactiveChild = Node(ChildId, parentId: ObjectiveId, ownerId: HeadId, isActive: false);

        var (handler, _) = BuildHandler(objective, all: [objective, inactiveChild]);

        var result = await handler.Handle(new GetObjectiveSubtreeQuery(ObjectiveId), CancellationToken.None);

        var mappedChild = Assert.Single(result.Value!.Objective.Children);
        Assert.False(mappedChild.IsActive);
    }
}
