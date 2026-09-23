using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Mappers;

public static class ManagementCoverageRecordMapper
{
    public static ManagementCoverageRecordResponse ToResponse(
        ManagementCoverageRecord entity,
        string? ownerPositionName,
        string? coveredPositionName,
        string? coveredDepartmentName,
        string? responsibleEmployeeName = null)
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
            entity.Status,
            entity.ResponsibleEmployeeId,
            responsibleEmployeeName);
    }

    // Mirrors the frontend's formatResponsibilityLabel: order 1 is Primary Manager, every order
    // above that is "Backup Manager" numbered from 1 (order 2 = Backup Manager 1, order 3 = Backup
    // Manager 2, ...). Used to phrase user-safe conflict messages without exposing ownerOrder or
    // any DB constraint/index name.
    public static string ResponsibilityLevelLabel(int ownerOrder) =>
        ownerOrder <= 1 ? "Primary Manager" : $"Backup Manager {ownerOrder - 1}";
}
