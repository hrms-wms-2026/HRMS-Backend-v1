using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class PositionMapper
{
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
            childCount);
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
            entity.UpdatedAt);
    }
}
