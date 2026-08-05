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

    public static DepartmentListItemResponse ToListItemResponse(Department entity)
    {
        return new DepartmentListItemResponse(
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
}
