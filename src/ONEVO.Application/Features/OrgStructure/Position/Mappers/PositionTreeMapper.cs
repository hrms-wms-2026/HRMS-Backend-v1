using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionTreeMapper
{
    private static readonly IReadOnlyDictionary<Guid, PositionOccupancyPreview> EmptyOccupancyByPositionId =
        new Dictionary<Guid, PositionOccupancyPreview>();

    private static readonly PositionOccupancyPreview EmptyOccupancyPreview =
        new(0, Array.Empty<PositionOccupantPreviewItem>());

    public static IReadOnlyList<PositionTreeNodeResponse> BuildTree(
        IReadOnlyList<Position> positions,
        IReadOnlyDictionary<Guid, PositionOccupancyPreview>? occupancyByPositionId = null)
    {
        var occupancy = occupancyByPositionId ?? EmptyOccupancyByPositionId;
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

        return roots.Select(root => BuildNode(root, childrenByParentId, occupancy)).ToList();
    }

    private static PositionTreeNodeResponse BuildNode(
        Position position,
        IReadOnlyDictionary<Guid, List<Position>> childrenByParentId,
        IReadOnlyDictionary<Guid, PositionOccupancyPreview> occupancyByPositionId)
    {
        var childEntities = childrenByParentId.TryGetValue(position.Id, out var children)
            ? children
            : new List<Position>();

        var childNodes = childEntities.Select(child => BuildNode(child, childrenByParentId, occupancyByPositionId)).ToList();

        var preview = occupancyByPositionId.TryGetValue(position.Id, out var found) ? found : EmptyOccupancyPreview;
        var occupantPreview = preview.OccupantPreview.Select(PositionMapper.ToOccupantPreviewResponse).ToList();

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
            childNodes,
            preview.AssignedCount,
            occupantPreview,
            preview.AssignedCount - occupantPreview.Count);
    }
}
