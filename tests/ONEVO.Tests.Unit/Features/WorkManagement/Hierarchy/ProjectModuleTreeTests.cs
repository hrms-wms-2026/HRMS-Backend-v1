using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Hierarchy;

public class ProjectModuleTreeTests
{
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Cc = Guid.NewGuid();
    private static readonly Guid X = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static Objective Module(Guid id, Guid? parent, Guid owner, bool isDefault = false) => new()
    {
        Id = id, ParentObjectiveId = parent, OwnerId = owner, IsDefault = isDefault, Title = id.ToString()
    };

    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    private ProjectModuleTree Tree(Guid pOwner) => new(new[]
    {
        Module(_root, null, Lead, isDefault: true),
        Module(_p, _root, pOwner),
        Module(_c, _p, Cc),
    });

    [Fact]
    public void AncestorChain_IsSelfToRoot()
        => Tree(A).AncestorChain(_c).Select(m => m.Id).Should().Equal(_c, _p, _root);

    [Fact]
    public void IsAtOrAbove_OwnerOfPositionOrAncestor_True_ChildOwner_False()
    {
        var tree = Tree(A);
        tree.IsAtOrAbove(A, _p).Should().BeTrue();
        tree.IsAtOrAbove(Lead, _p).Should().BeTrue();
        tree.IsAtOrAbove(Cc, _p).Should().BeFalse();
        tree.IsAtOrAbove(Stranger, _p).Should().BeFalse();
    }

    [Fact]
    public void Transfer_MovesTheRightToTheNewHolder()
    {
        // Spec §4: after P moves from A to X, X (not A) holds C's creator position.
        var tree = Tree(X);
        tree.IsAtOrAbove(X, _p).Should().BeTrue();
        tree.IsAtOrAbove(A, _p).Should().BeFalse();
    }

    [Fact]
    public void HighestOwnedModuleId_PicksClosestToRoot()
    {
        var tree = new ProjectModuleTree(new[]
        {
            Module(_root, null, Lead, isDefault: true),
            Module(_p, _root, A),
            Module(_c, _p, A),
        });
        tree.HighestOwnedModuleId(A).Should().Be(_p);
        tree.HighestOwnedModuleId(Stranger).Should().BeNull();
    }

    [Fact]
    public void Root_IsTheDefaultModule()
        => Tree(A).Root!.Id.Should().Be(_root);

    [Fact]
    public void AncestorChain_CycleInData_Terminates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var tree = new ProjectModuleTree(new[] { Module(a, b, A), Module(b, a, X) });
        tree.AncestorChain(a).Should().HaveCount(2);
    }
}
