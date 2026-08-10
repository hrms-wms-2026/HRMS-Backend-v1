using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ObjectiveMapperTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RootId = Guid.NewGuid();
    private static readonly Guid Child1Id = Guid.NewGuid();
    private static readonly Guid Child2Id = Guid.NewGuid();
    private static readonly Guid GrandchildId = Guid.NewGuid();

    private static Objective Node(Guid id, Guid? parentId, bool isActive = true) => new()
    {
        Id = id, TenantId = TenantId, ParentObjectiveId = parentId, Title = "N",
        OwnerId = Guid.NewGuid(), IsActive = isActive,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void ToSubtreeNode_NestsChildrenRecursively()
    {
        var root = Node(RootId, parentId: null);
        var child1 = Node(Child1Id, parentId: RootId);
        var child2 = Node(Child2Id, parentId: RootId);
        var grandchild = Node(GrandchildId, parentId: Child1Id);

        var childrenByParent = new[] { child1, child2, grandchild }
            .ToLookup(o => o.ParentObjectiveId!.Value);

        var result = ObjectiveMapper.ToSubtreeNode(root, childrenByParent);

        Assert.Equal(RootId, result.Id);
        Assert.Equal(2, result.Children.Count);

        var mappedChild1 = Assert.Single(result.Children, c => c.Id == Child1Id);
        var grandchildNode = Assert.Single(mappedChild1.Children);
        Assert.Equal(GrandchildId, grandchildNode.Id);
        Assert.Empty(grandchildNode.Children);

        var mappedChild2 = Assert.Single(result.Children, c => c.Id == Child2Id);
        Assert.Empty(mappedChild2.Children);
    }

    [Fact]
    public void ToSubtreeNode_IncludesInactiveChildren()
    {
        var root = Node(RootId, parentId: null);
        var inactiveChild = Node(Child1Id, parentId: RootId, isActive: false);

        var childrenByParent = new[] { inactiveChild }.ToLookup(o => o.ParentObjectiveId!.Value);

        var result = ObjectiveMapper.ToSubtreeNode(root, childrenByParent);

        var mappedChild = Assert.Single(result.Children);
        Assert.False(mappedChild.IsActive);
    }
}
