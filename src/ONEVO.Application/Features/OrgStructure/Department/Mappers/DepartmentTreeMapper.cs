using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class DepartmentTreeMapper
{
    public static IReadOnlyList<DepartmentTreeNodeResponse> BuildTree(
        IReadOnlyList<Department> departments,
        IReadOnlyDictionary<Guid, int> positionCountsByDepartmentId,
        IReadOnlyDictionary<Guid, int> employeeCountsByDepartmentId,
        IReadOnlyDictionary<Guid, string> positionNamesById)
    {
        var idsInSet = departments.Select(department => department.Id).ToHashSet();

        var childrenByParentId = departments
            .Where(department => department.ParentDepartmentId is not null
                && idsInSet.Contains(department.ParentDepartmentId.Value))
            .GroupBy(department => department.ParentDepartmentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(department => department.Name).ToList());

        var roots = departments
            .Where(department => department.ParentDepartmentId is null
                || !idsInSet.Contains(department.ParentDepartmentId.Value))
            .OrderBy(department => department.Name)
            .ToList();

        return roots
            .Select(root => BuildNode(root, childrenByParentId, positionCountsByDepartmentId, employeeCountsByDepartmentId, positionNamesById))
            .ToList();
    }

    private static DepartmentTreeNodeResponse BuildNode(
        Department department,
        IReadOnlyDictionary<Guid, List<Department>> childrenByParentId,
        IReadOnlyDictionary<Guid, int> positionCountsByDepartmentId,
        IReadOnlyDictionary<Guid, int> employeeCountsByDepartmentId,
        IReadOnlyDictionary<Guid, string> positionNamesById)
    {
        var children = childrenByParentId.TryGetValue(department.Id, out var childDepartments)
            ? childDepartments
                .Select(child => BuildNode(child, childrenByParentId, positionCountsByDepartmentId, employeeCountsByDepartmentId, positionNamesById))
                .ToList()
            : new List<DepartmentTreeNodeResponse>();

        var headPositionTitle = department.HeadPositionId is { } headPositionId
            && positionNamesById.TryGetValue(headPositionId, out var name)
                ? name
                : null;

        return new DepartmentTreeNodeResponse(
            department.Id,
            department.LegalEntityId,
            department.Name,
            department.Code,
            department.ParentDepartmentId,
            department.HeadPositionId,
            department.IsActive,
            children,
            positionCountsByDepartmentId.GetValueOrDefault(department.Id),
            employeeCountsByDepartmentId.GetValueOrDefault(department.Id),
            headPositionTitle);
    }
}
