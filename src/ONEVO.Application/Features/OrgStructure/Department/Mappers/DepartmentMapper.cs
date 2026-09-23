using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class DepartmentMapper
{
    public static DepartmentResponse ToResponse(Department entity)
    {
        return new DepartmentResponse(
            entity.Id,
            entity.LegalEntityId,
            entity.Name,
            entity.Code,
            entity.ParentDepartmentId,
            entity.HeadPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    public static DepartmentListItemResponse ToListItemResponse(
        Department entity,
        IReadOnlyDictionary<Guid, int> positionCountsByDepartmentId,
        IReadOnlyDictionary<Guid, int> employeeCountsByDepartmentId,
        IReadOnlyDictionary<Guid, string> positionNamesById)
    {
        var headPositionTitle = entity.HeadPositionId is { } headPositionId
            && positionNamesById.TryGetValue(headPositionId, out var name)
                ? name
                : null;

        return new DepartmentListItemResponse(
            entity.Id,
            entity.LegalEntityId,
            entity.Name,
            entity.Code,
            entity.ParentDepartmentId,
            entity.HeadPositionId,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt,
            positionCountsByDepartmentId.GetValueOrDefault(entity.Id),
            employeeCountsByDepartmentId.GetValueOrDefault(entity.Id),
            headPositionTitle);
    }
}
