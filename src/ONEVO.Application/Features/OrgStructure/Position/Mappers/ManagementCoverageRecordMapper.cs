using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class ManagementCoverageRecordMapper
{
    public static ManagementCoverageRecordResponse ToResponse(
        ManagementCoverageRecord entity,
        string? ownerPositionName,
        string? coveredPositionName,
        string? coveredDepartmentName)
    {
        return new ManagementCoverageRecordResponse(
            entity.Id,
            entity.OwnerPositionId,
            ownerPositionName,
            entity.CoveredTargetType,
            entity.CoveredPositionId,
            coveredPositionName,
            entity.CoveredDepartmentId,
            coveredDepartmentName,
            entity.OwnerOrder,
            entity.Source,
            entity.IsLocked,
            entity.Status);
    }
}
