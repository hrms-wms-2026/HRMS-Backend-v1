using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionTreeMapper
{
    public static IReadOnlyList<PositionTreeNodeResponse> BuildTree(IReadOnlyList<Position> positions)
    {
        var idsInSet = positions.Select(position => position.Id).ToHashSet();

        var childrenByParentId = positions
            .Where(position => position.ReportsToPositionId is not null
                && idsInSet.Contains(position.ReportsToPositionId.Value))
            .GroupBy(position => position.ReportsToPositionId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(position => position.Name).ToList());

        var roots = positions
            .Where(position => position.ReportsToPositionId is null
                || !idsInSet.Contains(position.ReportsToPositionId.Value))
            .OrderBy(position => position.Name)
            .ToList();

        return roots.Select(root => BuildNode(root, childrenByParentId)).ToList();
    }

    private static PositionTreeNodeResponse BuildNode(
        Position position, IReadOnlyDictionary<Guid, List<Position>> childrenByParentId)
    {
        var childEntities = childrenByParentId.TryGetValue(position.Id, out var children)
            ? children
            : new List<Position>();

        var childNodes = childEntities.Select(child => BuildNode(child, childrenByParentId)).ToList();

        return new PositionTreeNodeResponse(
            position.Id,
            position.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            position.DepartmentId,
            position.Name,
            position.Code,
            position.PositionType,
            position.MaxOccupancy,
            position.ReportsToPositionId,
            position.IsActive,
            childNodes.Count,
            childNodes);
    }
}
