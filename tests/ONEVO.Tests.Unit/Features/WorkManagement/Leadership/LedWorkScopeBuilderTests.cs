using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Leadership;

public sealed class LedWorkScopeBuilderTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly End = new(2026, 12, 31);

    private static LedObjectiveRow Row(Guid id, Guid? parent, bool isDefault = false) => new(id, Project, parent, $"M-{id:N}"[..6], isDefault, End);

    [Fact]
    public void Nested_owned_modules_roll_up_into_the_topmost_head_only()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid(), a11 = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(a1, a), Row(a11, a1) };

        var scope = LedWorkScopeBuilder.Build(new[] { a, a1 }, tree, membershipObjectiveIds: new[] { a });

        var head = Assert.Single(scope.HeadModules);
        Assert.Equal(a, head.ObjectiveId);
        Assert.Equal(new[] { a, a1, a11 }.OrderBy(x => x), head.SubtreeObjectiveIds.OrderBy(x => x));
        Assert.False(head.IsRootModule);
    }

    [Fact]
    public void Sibling_owned_modules_are_separate_heads()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(b, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { a, b }, tree, new[] { a, b });

        Assert.Equal(2, scope.HeadModules.Count);
        Assert.Empty(scope.HeadModules[0].SubtreeObjectiveIds.Intersect(scope.HeadModules[1].SubtreeObjectiveIds));
    }

    [Fact]
    public void Root_owner_heads_the_whole_project()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(b, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { root }, tree, new[] { root });

        var head = Assert.Single(scope.HeadModules);
        Assert.True(head.IsRootModule);
        Assert.Equal(3, head.SubtreeObjectiveIds.Count);
    }

    // Review clarification (2026-09-30): explicit invariant - owner marked in the domain but with
    // no membership access must NEVER be auto-granted visibility. This is treated as a
    // data-integrity condition (logged by WorkLeadershipService, see Task 3), not a normal path.
    [Fact]
    public void SECURITY_INVARIANT_Owner_without_membership_access_is_dropped_never_auto_granted()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root) };

        var scope = LedWorkScopeBuilder.Build(new[] { a }, tree, membershipObjectiveIds: Array.Empty<Guid>());

        Assert.Empty(scope.HeadModules);
        Assert.Equal(new[] { a }, scope.DroppedOwnedObjectiveIds);
    }

    [Fact]
    public void SECURITY_INVARIANT_A_dropped_owned_module_never_leaks_into_AllObjectiveIds()
    {
        Guid root = Guid.NewGuid(), owned = Guid.NewGuid(), sibling = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(owned, root), Row(sibling, root) };

        // Caller owns two modules; has membership on only one of them.
        var scope = LedWorkScopeBuilder.Build(new[] { owned, sibling }, tree, membershipObjectiveIds: new[] { sibling });

        Assert.DoesNotContain(owned, scope.AllObjectiveIds);
        Assert.Contains(sibling, scope.AllObjectiveIds);
        Assert.Contains(owned, scope.DroppedOwnedObjectiveIds);
    }

    [Fact]
    public void Membership_on_an_ancestor_grants_access_to_the_owned_subtree()
    {
        Guid root = Guid.NewGuid(), a = Guid.NewGuid(), a1 = Guid.NewGuid();
        var tree = new[] { Row(root, null, true), Row(a, root), Row(a1, a) };

        var scope = LedWorkScopeBuilder.Build(new[] { a }, tree, membershipObjectiveIds: new[] { root });

        Assert.Equal(new[] { a, a1 }.OrderBy(x => x), Assert.Single(scope.HeadModules).SubtreeObjectiveIds.OrderBy(x => x));
    }

    [Fact]
    public void Nothing_owned_yields_empty_scope()
    {
        var scope = LedWorkScopeBuilder.Build(Array.Empty<Guid>(), Array.Empty<LedObjectiveRow>(), Array.Empty<Guid>());
        Assert.Same(LedWorkScope.Empty, scope);
    }
}
