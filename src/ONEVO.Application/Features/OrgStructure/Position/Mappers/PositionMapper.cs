using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionMapper
{
    // No position_assignments table exists anywhere in this codebase (confirmed by a repo-wide
    // search), so current occupancy is genuinely unmeasurable today, not zero - mirrors
    // PositionArchiveBlockers.ActiveOccupants/ActiveOccupantsCheckSupported.
    private static readonly int? UnsupportedCurrentOccupancy = null;
    private const bool CurrentOccupancyCheckSupported = false;

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

    public static PositionListItemResponse ToListItemResponse(Position entity)
    {
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
            CurrentOccupancyCheckSupported);
    }
}
