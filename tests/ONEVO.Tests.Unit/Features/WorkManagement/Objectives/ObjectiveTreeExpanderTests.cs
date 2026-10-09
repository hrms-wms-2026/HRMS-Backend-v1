using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public sealed class ObjectiveTreeExpanderTests
{
    [Fact]
    public void Expands_a_root_to_its_full_subtree_and_keeps_the_root()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid(), b = Guid.NewGuid(), other = Guid.NewGuid();
        var nodes = new[]
        {
            new ObjectiveTreeNode(root, null), new ObjectiveTreeNode(a, root), new ObjectiveTreeNode(a1, a),
            new ObjectiveTreeNode(b, root), new ObjectiveTreeNode(other, null),
        };

        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { a }, nodes);

        Assert.Equal(new[] { a, a1 }.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void A_root_missing_from_the_active_tree_is_returned_but_expands_to_nothing()
    {
        var inactiveMembershipObjective = Guid.NewGuid();
        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { inactiveMembershipObjective }, Array.Empty<ObjectiveTreeNode>());
        Assert.Equal(new[] { inactiveMembershipObjective }, result);
    }

    [Fact]
    public void Overlapping_roots_are_not_duplicated()
    {
        Guid root = Guid.NewGuid(), child = Guid.NewGuid();
        var nodes = new[] { new ObjectiveTreeNode(root, null), new ObjectiveTreeNode(child, root) };
        var result = ObjectiveTreeExpander.ExpandWithDescendants(new[] { root, child }, nodes);
        Assert.Equal(2, result.Count);
    }
}
