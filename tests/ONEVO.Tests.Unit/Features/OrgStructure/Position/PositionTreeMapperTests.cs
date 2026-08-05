using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class PositionTreeMapperTests
{
    [Fact]
    public void BuildTree_NestsChildrenUnderReportsToPositionId()
    {
        var legalEntityId = Guid.NewGuid();
        var root = CreatePosition(legalEntityId, "CEO", reportsToPositionId: null);
        var child = CreatePosition(legalEntityId, "VP Sales", reportsToPositionId: root.Id);
        var grandchild = CreatePosition(legalEntityId, "Sales Manager", reportsToPositionId: child.Id);

        var tree = PositionTreeMapper.BuildTree([root, child, grandchild]);

        Assert.Single(tree);
        Assert.Equal("CEO", tree[0].Name);
        Assert.Equal(1, tree[0].ChildCount);
        Assert.Single(tree[0].Children);
        Assert.Equal("VP Sales", tree[0].Children[0].Name);
        Assert.Single(tree[0].Children[0].Children);
        Assert.Equal("Sales Manager", tree[0].Children[0].Children[0].Name);
        Assert.Empty(tree[0].Children[0].Children[0].Children);
    }

    [Fact]
    public void BuildTree_TreatsOrphanedReportsToAsRoot()
    {
        var legalEntityId = Guid.NewGuid();
        var missingParentId = Guid.NewGuid();
        var orphan = CreatePosition(legalEntityId, "Orphan Manager", reportsToPositionId: missingParentId);

        var tree = PositionTreeMapper.BuildTree([orphan]);

        Assert.Single(tree);
        Assert.Equal("Orphan Manager", tree[0].Name);
    }

    [Fact]
    public void BuildTree_OrdersSiblingsByName()
    {
        var legalEntityId = Guid.NewGuid();
        var zeta = CreatePosition(legalEntityId, "Zeta Lead", reportsToPositionId: null);
        var alpha = CreatePosition(legalEntityId, "Alpha Lead", reportsToPositionId: null);

        var tree = PositionTreeMapper.BuildTree([zeta, alpha]);

        Assert.Equal(2, tree.Count);
        Assert.Equal("Alpha Lead", tree[0].Name);
        Assert.Equal("Zeta Lead", tree[1].Name);
    }

    private static ONEVO.Domain.Features.OrgStructure.Entities.Position CreatePosition(
        Guid legalEntityId, string name, Guid? reportsToPositionId)
    {
        return new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LegalEntityId = legalEntityId,
            Name = name,
            Code = name.Replace(" ", "-").ToUpperInvariant(),
            ReportsToPositionId = reportsToPositionId,
            IsActive = true
        };
    }
}
