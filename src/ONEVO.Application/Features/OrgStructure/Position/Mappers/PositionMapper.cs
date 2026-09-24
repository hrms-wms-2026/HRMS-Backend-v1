using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionMapper
{
    // Hard backend cap on how many occupants are returned per position in list/tree responses -
    // callers needing the full roster use the employees list endpoint filtered by position, not
    // a pooled position's occupant preview.
    public const int OccupantPreviewLimit = 4;

    // CurrentOccupancy/CurrentOccupancyCheckSupported remain the long-standing (null, false)
    // placeholder on purpose - see the comment on PositionListItemResponse for why this pair is
    // not being populated as part of this change.
    private static readonly int? UnsupportedCurrentOccupancy = null;
    private const bool CurrentOccupancyCheckSupported = false;

    private static readonly PositionOccupancyPreview EmptyOccupancyPreview =
        new(0, Array.Empty<PositionOccupantPreviewItem>());

    public static PositionResponse ToResponse(
        Position entity, string? departmentName, string? reportsToPositionName, int childCount)
    {
        return new PositionResponse(
            entity.Id,
            entity.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            entity.DepartmentId,
            entity.Name,
            entity.Code,
            entity.PositionType,
            entity.MaxOccupancy,
            entity.ReportsToPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt,
            departmentName,
            reportsToPositionName,
            childCount,
            UnsupportedCurrentOccupancy,
            CurrentOccupancyCheckSupported);
    }

    public static PositionListItemResponse ToListItemResponse(
        Position entity,
        IReadOnlyDictionary<Guid, PositionOccupancyPreview> occupancyByPositionId,
        IReadOnlyDictionary<Guid, bool> requiresApprovalByPositionId)
    {
        var preview = occupancyByPositionId.TryGetValue(entity.Id, out var found) ? found : EmptyOccupancyPreview;
        var occupantPreview = preview.OccupantPreview.Select(ToOccupantPreviewResponse).ToList();
        var requiresApproval = requiresApprovalByPositionId.TryGetValue(entity.Id, out var flag) && flag;

        return new PositionListItemResponse(
            entity.Id,
            entity.LegalEntityId!.Value, // safe: only ever mapped from a legalEntityId-scoped fetch
            entity.DepartmentId,
            entity.Name,
            entity.Code,
            entity.PositionType,
            entity.MaxOccupancy,
            entity.ReportsToPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt,
            UnsupportedCurrentOccupancy,
            CurrentOccupancyCheckSupported,
            preview.AssignedCount,
            occupantPreview,
            preview.AssignedCount - occupantPreview.Count,
            requiresApproval);
    }

    public static PositionOccupantPreviewResponse ToOccupantPreviewResponse(PositionOccupantPreviewItem item)
    {
        var displayName = $"{item.FirstName} {item.LastName}".Trim();
        return new PositionOccupantPreviewResponse(
            item.EmployeeId, displayName, BuildInitials(item.FirstName, item.LastName), item.AvatarFileId, AvatarUrl: null);
    }

    private static string BuildInitials(string firstName, string lastName)
    {
        var initials = new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => char.ToUpperInvariant(part.Trim()[0]));

        var result = new string(initials.ToArray());
        return result.Length > 0 ? result : "?";
    }
}
