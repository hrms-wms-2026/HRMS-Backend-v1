using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Mappers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Department;

public sealed class DepartmentTreeMapperTests
{
    private static readonly IReadOnlyDictionary<Guid, int> EmptyCounts = new Dictionary<Guid, int>();
    private static readonly IReadOnlyDictionary<Guid, string> EmptyNames = new Dictionary<Guid, string>();

    [Fact]
    public void BuildTree_NestsChildrenUnderParent()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var parent = CreateDepartment(tenantId, legalEntityId, "Parent");
        var child = CreateDepartment(tenantId, legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;

        var tree = DepartmentTreeMapper.BuildTree(
            new List<Domain.Features.OrgStructure.Entities.Department> { parent, child },
            EmptyCounts, EmptyCounts, EmptyNames);

        Assert.Single(tree);
        Assert.Equal("Parent", tree[0].Name);
        Assert.Single(tree[0].Children);
        Assert.Equal("Child", tree[0].Children[0].Name);
    }

    [Fact]
    public void BuildTree_TreatsDepartmentWithParentOutsideSet_AsRoot()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var orphan = CreateDepartment(tenantId, legalEntityId, "Orphan");
        orphan.ParentDepartmentId = Guid.NewGuid();

        var tree = DepartmentTreeMapper.BuildTree(
            new List<Domain.Features.OrgStructure.Entities.Department> { orphan },
            EmptyCounts, EmptyCounts, EmptyNames);

        Assert.Single(tree);
        Assert.Equal("Orphan", tree[0].Name);
        Assert.Empty(tree[0].Children);
    }

    [Fact]
    public void BuildTree_DoesNotExposeTenantId()
    {
        var properties = typeof(DepartmentTreeNodeResponse).GetProperties();

        Assert.DoesNotContain(properties, p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildTree_PreservesHeadPositionId_ReadOnly()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var headPositionId = Guid.NewGuid();
        var department = CreateDepartment(tenantId, legalEntityId, "Has Head");
        department.HeadPositionId = headPositionId;

        var tree = DepartmentTreeMapper.BuildTree(
            new List<Domain.Features.OrgStructure.Entities.Department> { department },
            EmptyCounts, EmptyCounts, EmptyNames);

        Assert.Equal(headPositionId, tree[0].HeadPositionId);
    }

    private static Domain.Features.OrgStructure.Entities.Department CreateDepartment(
        Guid tenantId, Guid legalEntityId, string name)
    {
        return new Domain.Features.OrgStructure.Entities.Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            IsActive = true
        };
    }
}
